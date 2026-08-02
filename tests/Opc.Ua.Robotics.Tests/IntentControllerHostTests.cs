/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.RobotIntent;
using Opc.Ua.Tests;
using Opc.Ua.RobotIntent.Server;
using RiDataTypeIds = Opc.Ua.RobotIntent.DataTypeIds;
using RiNamespaces = Opc.Ua.RobotIntent.Namespaces;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Exercises the OPC UA - Robot Intent execution lifecycle.
    /// </summary>
    /// <remarks>
    /// These assert the normative rules the specification can actually be observed to
    /// keep: the clause 6.2 admission order, the clause 6.3 state pairing, the clause
    /// 6.4 queue, the clause 6.5 right to refuse a cancel, and the clause 7.2 base
    /// immutability. A test that only proved "a method exists" would prove nothing.
    /// </remarks>
    [TestFixture]
    public class IntentControllerHostTests
    {
        private ServiceMessageContext m_messageContext = null!;
        private SystemContext m_context = null!;
        private IntentControllerState m_controller = null!;
        private ScriptedExecutor m_executor = null!;
        private IntentControllerHost m_host = null!;
        private readonly List<NodeState> m_added = [];

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            m_messageContext = ServiceMessageContext.Create(telemetry);
            m_messageContext.NamespaceUris.Append(RiNamespaces.RobotIntent);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = m_messageContext.NamespaceUris,
                EncodeableFactory = m_messageContext.Factory
            };

            m_controller = new IntentControllerState(null);
            m_controller.Create(
                m_context,
                new NodeId("Controller", 1),
                new QualifiedName("Controller", 1),
                new LocalizedText("Controller"),
                true);

            m_executor = new ScriptedExecutor();
            m_added.Clear();
            m_host = new IntentControllerHost(
                m_controller,
                m_executor,
                (node, ct) =>
                {
                    lock (m_added)
                    {
                        m_added.Add(node);
                    }
                    return default;
                },
                Options());
            m_host.Start(m_context);
        }

        [TearDown]
        public void TearDown()
        {
            m_host?.Dispose();
        }

        private static IntentControllerHostOptions Options(
            OperationalModeEnum mode = OperationalModeEnum.AutomaticExternal,
            bool requireAuthority = false)
        {
            var options = new IntentControllerHostOptions
            {
                OperationalMode = mode,
                RequireControlAuthority = requireAuthority,
                AxisCount = 6,
                MaxQueueDepth = 4
            };
            options.Accept(RiDataTypeIds.LinearMoveIntentDataType);
            options.Accept(RiDataTypeIds.JointMoveIntentDataType);
            options.Accept(RiDataTypeIds.GraspIntentDataType, cancelSupported: false);
            return options;
        }

        private static Pose3DDataType Pose(double x = 0, double y = 0, double z = 0)
        {
            return new Pose3DDataType
            {
                FrameId = "base",
                Position = new[] { x, y, z },
                Orientation = new[] { 0.0, 0.0, 0.0, 1.0 }
            };
        }

        private static LinearMoveIntentDataType Move(
            string id = "",
            BufferModeEnum buffer = BufferModeEnum.Aborting)
        {
            return new LinearMoveIntentDataType
            {
                IntentId = id,
                BufferMode = buffer,
                Target = Pose(1, 0, 0)
            };
        }

        // ------------------------------------------------------- clause 6.2 admission

        [Test]
        public void SubmitReturnsAHandleWithoutWaitingForTheMotion()
        {
            m_executor.Gate = new SemaphoreSlim(0);

            IntentAdmission admission = m_host.SubmitIntent(m_context, null, Move());

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.True);
                Assert.That(admission.IntentId, Is.Not.Empty);
                Assert.That(admission.Operation, Is.Not.EqualTo(NodeId.Null),
                    "submission must return the IntentOperation that tracks the work");
            });
            m_executor.Gate!.Release();
        }

        [Test]
        public void SubmissionIsRefusedOutsideAutomaticModes()
        {
            using var host = NewHost(Options(OperationalModeEnum.ManualReducedSpeed));

            IntentAdmission admission = host.SubmitIntent(m_context, null, Move());

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.NotPermittedInMode));
            });
        }

        [Test]
        public void SubmissionIsRefusedWithoutCommandAuthority()
        {
            using var host = NewHost(Options(requireAuthority: true));

            IntentAdmission admission = host.SubmitIntent(m_context, new NodeId("s1", 1), Move());

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ControlNotOwned));
            });
        }

        [Test]
        public void AuthorityIsExclusiveAndReleasedWhenTheSessionCloses()
        {
            using var host = NewHost(Options(requireAuthority: true));
            var first = new NodeId("s1", 1);
            var second = new NodeId("s2", 1);

            Assert.That(host.RequestControl(m_context, first, out _), Is.True);
            Assert.That(host.RequestControl(m_context, second, out NodeId? owner), Is.False);
            Assert.That(owner, Is.EqualTo(first));

            host.OnSessionClosed(m_context, first);

            Assert.That(host.RequestControl(m_context, second, out _), Is.True,
                "a closed Session must not lock the robot for good");
        }

        [Test]
        public void AnUndeclaredIntentTypeIsRefused()
        {
            var options = new IntentControllerHostOptions { RequireControlAuthority = false };
            options.Accept(RiDataTypeIds.JointMoveIntentDataType);
            using var host = NewHost(options);

            IntentAdmission admission = host.SubmitIntent(m_context, null, Move());

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
            });
        }

        [Test]
        public void AnUnnormalisedOrientationIsRefused()
        {
            var intent = new LinearMoveIntentDataType
            {
                BufferMode = BufferModeEnum.Aborting,
                Target = new Pose3DDataType
                {
                    FrameId = "base",
                    Position = new[] { 1.0, 0.0, 0.0 },
                    // Not a rotation: accepting it would command an orientation
                    // nobody specified.
                    Orientation = new[] { 0.0, 0.0, 0.0, 0.5 }
                }
            };

            IntentAdmission admission = m_host.SubmitIntent(m_context, null, intent);

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
            });
        }

        [Test]
        public void JointTargetsMustMatchTheDeclaredAxisCount()
        {
            var intent = new JointMoveIntentDataType
            {
                BufferMode = BufferModeEnum.Aborting,
                HasJointTargets = true,
                JointTargets = new[] { 0.1, 0.2, 0.3 }
            };

            IntentAdmission admission = m_host.SubmitIntent(m_context, null, intent);

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
            });
        }

        [Test]
        public void AuthorityIsCheckedBeforeParameters()
        {
            // The order in clause 6.2 is normative: a caller that lacks authority must
            // be told that, not that its parameters are wrong.
            using var host = NewHost(Options(requireAuthority: true));
            var bad = new LinearMoveIntentDataType { Target = null! };

            IntentAdmission admission = host.SubmitIntent(m_context, new NodeId("s1", 1), bad);

            Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ControlNotOwned));
        }

        // ------------------------------------------------------------ clause 6.3 state

        [Test]
        public void EveryExecutionStateHasExactlyOnePartTenPairing()
        {
            // The generic overload is net5+, and this test project also targets .NET
            // Framework. Enumerating rather than listing the values is the point: a
            // state added without a clause 6.3 pairing must fail here.
#if NET5_0_OR_GREATER
            foreach (ExecutionStateEnum state in Enum.GetValues<ExecutionStateEnum>())
#else
            foreach (ExecutionStateEnum state in
                Enum.GetValues(typeof(ExecutionStateEnum)).Cast<ExecutionStateEnum>())
#endif
            {
                Assert.DoesNotThrow(
                    () => IntentControllerHost.MapToProgramState(state),
                    $"{state} has no clause 6.3 pairing");
            }
        }

        [Test]
        public void TheStatePairingMatchesTheSpecificationTable()
        {
            Assert.Multiple(() =>
            {
                Assert.That(IntentControllerHost.MapToProgramState(ExecutionStateEnum.Accepted), Is.EqualTo(1u));
                Assert.That(IntentControllerHost.MapToProgramState(ExecutionStateEnum.Queued), Is.EqualTo(1u));
                Assert.That(IntentControllerHost.MapToProgramState(ExecutionStateEnum.Executing), Is.EqualTo(2u));
                Assert.That(IntentControllerHost.MapToProgramState(ExecutionStateEnum.Cancelling), Is.EqualTo(2u));
                Assert.That(IntentControllerHost.MapToProgramState(ExecutionStateEnum.Suspended), Is.EqualTo(3u));
                Assert.That(IntentControllerHost.MapToProgramState(ExecutionStateEnum.Succeeded), Is.EqualTo(4u));
                Assert.That(IntentControllerHost.MapToProgramState(ExecutionStateEnum.Failed), Is.EqualTo(4u));
                Assert.That(IntentControllerHost.MapToProgramState(ExecutionStateEnum.Cancelled), Is.EqualTo(4u));
                Assert.That(IntentControllerHost.MapToProgramState(ExecutionStateEnum.Retriable), Is.EqualTo(4u));
            });
        }

        [Test]
        public async Task ASucceededIntentPublishesItsResultAndReachesHalted()
        {
            IntentAdmission admission = m_host.SubmitIntent(m_context, null, Move());
            IntentOperationState node = await WaitForTerminalAsync(admission.IntentId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(node.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(node.Result!.Value, Is.Not.Null);
                Assert.That(node.Result!.Value!.Failure, Is.EqualTo(IntentFailureEnum.None));
                Assert.That(node.Result!.Value!.IntentId, Is.EqualTo(admission.IntentId));
                Assert.That(node.FinalResultData, Is.Not.Null,
                    "the terminal result must also be reachable where Part 10 says it is");
            });
        }

        // ------------------------------------------------------------ clause 6.4 queue

        [Test]
        public async Task BufferedWorkQueuesBehindWhatIsExecuting()
        {
            m_executor.Gate = new SemaphoreSlim(0);

            IntentAdmission first = m_host.SubmitIntent(m_context, null, Move("a"));
            IntentAdmission second = m_host.SubmitIntent(
                m_context, null, Move("b", BufferModeEnum.Buffered));

            Assert.That(second.Accepted, Is.True);
            await WaitAsync(() => m_executor.Started.Contains("a")).ConfigureAwait(false);
            Assert.That(m_executor.Started, Does.Not.Contain("b"),
                "buffered work must not start while its predecessor executes");

            m_executor.Gate!.Release(2);
            await WaitForTerminalAsync("b").ConfigureAwait(false);
            Assert.That(m_executor.Started, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public async Task AnAbortingSubmissionSupersedesTheQueue()
        {
            m_executor.Gate = new SemaphoreSlim(0);

            m_host.SubmitIntent(m_context, null, Move("a"));
            m_host.SubmitIntent(m_context, null, Move("b", BufferModeEnum.Buffered));
            await WaitAsync(() => m_executor.Started.Contains("a")).ConfigureAwait(false);

            m_host.SubmitIntent(m_context, null, Move("c"));
            m_executor.Gate!.Release(3);

            IntentOperationState superseded = await WaitForTerminalAsync("b").ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(superseded.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(superseded.Result!.Value!.Failure, Is.EqualTo(IntentFailureEnum.Superseded),
                    "replaced work must be distinguishable from work a client cancelled");
            });
        }

        [Test]
        public void TheQueueIsBoundedByMaxQueueDepth()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            var options = Options();
            options.MaxQueueDepth = 1;
            using var host = NewHost(options);

            host.SubmitIntent(m_context, null, Move("a", BufferModeEnum.Buffered));
            IntentAdmission overflow = host.SubmitIntent(
                m_context, null, Move("b", BufferModeEnum.Buffered));

            Assert.Multiple(() =>
            {
                Assert.That(overflow.Accepted, Is.False);
                Assert.That(overflow.Failure, Is.EqualTo(IntentFailureEnum.QueueFull));
            });
            m_executor.Gate!.Release(4);
        }

        // ------------------------------------------------------- clause 6.5 cancelling

        [Test]
        public async Task CancellingQueuedWorkTerminatesItWithoutRunningIt()
        {
            m_executor.Gate = new SemaphoreSlim(0);

            m_host.SubmitIntent(m_context, null, Move("a"));
            m_host.SubmitIntent(m_context, null, Move("b", BufferModeEnum.Buffered));
            await WaitAsync(() => m_executor.Started.Contains("a")).ConfigureAwait(false);

            Assert.That(m_host.CancelIntent(m_context, null, "b"), Is.True);

            IntentOperationState cancelled = await WaitForTerminalAsync("b").ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(cancelled.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(m_executor.Started, Does.Not.Contain("b"));
            });
            m_executor.Gate!.Release(2);
        }

        [Test]
        public async Task AServerMayRefuseACancel()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_executor.RefuseCancel = true;

            m_host.SubmitIntent(m_context, null, Move("a"));
            await WaitAsync(() => m_executor.Started.Contains("a")).ConfigureAwait(false);

            Assert.That(m_host.CancelIntent(m_context, null, "a"), Is.False,
                "some motions cannot be abandoned part-way, and the Server says so");

            m_executor.Gate!.Release();
            IntentOperationState node = await WaitForTerminalAsync("a").ConfigureAwait(false);
            Assert.That(node.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Succeeded));
        }

        [Test]
        public async Task AnAcceptedCancelStopsTheWorkAndReportsCancelled()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_executor.HonourCancellation = true;

            m_host.SubmitIntent(m_context, null, Move("a"));
            await WaitAsync(() => m_executor.Started.Contains("a")).ConfigureAwait(false);

            Assert.That(m_host.CancelIntent(m_context, null, "a"), Is.True);

            IntentOperationState node = await WaitForTerminalAsync("a").ConfigureAwait(false);
            Assert.That(node.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Cancelled));
        }

        [Test]
        public async Task RetryCreatesANewOperationAndLeavesTheOriginalTerminal()
        {
            m_executor.Outcome = IntentOutcome.Retriable(
                IntentFailureEnum.GraspFailed, "nothing in the gripper");

            IntentAdmission first = m_host.SubmitIntent(m_context, null, Move("a"));
            IntentOperationState original = await WaitForTerminalAsync("a").ConfigureAwait(false);
            Assert.That(original.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Retriable));

            m_executor.Outcome = IntentOutcome.Success;
            IntentAdmission retry = m_host.Retry(m_context, null, "a");

            Assert.Multiple(() =>
            {
                Assert.That(retry.Accepted, Is.True);
                Assert.That(retry.IntentId, Is.Not.EqualTo(first.IntentId));
                Assert.That(retry.Operation, Is.Not.EqualTo(first.Operation),
                    "a retry is a new attempt, and the history of the first survives");
            });
            await WaitForTerminalAsync(retry.IntentId).ConfigureAwait(false);
            Assert.That(original.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Retriable));
        }

        // ------------------------------------------------------------ clause 7 missions

        [Test]
        public async Task AMissionRunsItsStepsInOrder()
        {
            MissionAdmission admission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[]
                {
                    Step("s1", 1, released: true),
                    Step("s2", 2, released: true)
                }
            });

            Assert.That(admission.Accepted, Is.True);
            await WaitAsync(() => m_executor.Started.Length >= 2).ConfigureAwait(false);
            Assert.That(m_executor.Started, Has.Length.EqualTo(2));
        }

        [Test]
        public void AMissionUpdateThatWouldAlterTheBaseIsRefused()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                MissionUpdateId = 0,
                Steps = new[] { Step("s1", 1, released: true), Step("s2", 2, released: false) }
            });

            MissionUpdateOutcome outcome = m_host.UpdateMission(m_context, null, "m1", 1, new[]
            {
                Step("renamed", 1, released: true),
                Step("s2", 2, released: false)
            });

            Assert.That(outcome.Result, Is.EqualTo(MissionUpdateResultEnum.BaseConflict),
                "the base is committed and may already have executed");
            m_executor.Gate!.Release(4);
        }

        [Test]
        public void AnOutdatedMissionUpdateIsRejectedRatherThanAppliedOutOfOrder()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                MissionUpdateId = 5,
                Steps = new[] { Step("s1", 1, released: true), Step("s2", 2, released: false) }
            });

            MissionUpdateOutcome stale = m_host.UpdateMission(m_context, null, "m1", 5, new[]
            {
                Step("s1", 1, released: true),
                Step("s3", 3, released: false)
            });

            Assert.That(stale.Result, Is.EqualTo(MissionUpdateResultEnum.Outdated));
            m_executor.Gate!.Release(4);
        }

        [Test]
        public void AHorizonUpdateIsAccepted()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                MissionUpdateId = 0,
                Steps = new[] { Step("s1", 1, released: true), Step("s2", 2, released: false) }
            });

            MissionUpdateOutcome outcome = m_host.UpdateMission(m_context, null, "m1", 1, new[]
            {
                Step("s1", 1, released: true),
                Step("s9", 9, released: false)
            });

            Assert.That(outcome.Result, Is.EqualTo(MissionUpdateResultEnum.Accepted));
            m_executor.Gate!.Release(4);
        }

        [Test]
        public void AnUnknownMissionUpdateIsReportedAsSuch()
        {
            MissionUpdateOutcome outcome = m_host.UpdateMission(
                m_context, null, "nope", 1, new[] { Step("s1", 1, released: false) });

            Assert.That(outcome.Result, Is.EqualTo(MissionUpdateResultEnum.UnknownMission));
        }

        [Test]
        public void ReleasedStepsMustFormAPrefix()
        {
            MissionAdmission admission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[]
                {
                    Step("s1", 1, released: false),
                    Step("s2", 2, released: true)
                }
            });

            Assert.That(admission.Accepted, Is.False,
                "a released step after an unreleased one makes 'the base' meaningless");
        }

        private static MissionStepDataType Step(string id, uint sequence, bool released)
        {
            return new MissionStepDataType
            {
                StepId = id,
                SequenceId = sequence,
                Released = released,
                Intent = Move(id)
            };
        }

        // ------------------------------------------------------------------- helpers

        private IntentControllerHost NewHost(IntentControllerHostOptions options)
        {
            var controller = new IntentControllerState(null);
            controller.Create(
                m_context,
                new NodeId(Guid.NewGuid().ToString(), 1),
                new QualifiedName("Controller", 1),
                new LocalizedText("Controller"),
                true);
            var host = new IntentControllerHost(
                controller, m_executor, (_, _) => default, options);
            host.Start(m_context);
            return host;
        }

        private async Task<IntentOperationState> WaitForTerminalAsync(string intentId)
        {
            IntentOperationState? node = null;
            await WaitAsync(() =>
            {
                node = FindOperation(intentId);
                return node?.ExecutionState?.Value is { } state && IntentOutcome.IsTerminal(state);
            }).ConfigureAwait(false);
            return node!;
        }

        private IntentOperationState? FindOperation(string intentId)
        {
            lock (m_added)
            {
                return m_added
                    .OfType<IntentOperationState>()
                    .FirstOrDefault(n => n.IntentId?.Value == intentId);
            }
        }

        private static async Task WaitAsync(Func<bool> condition, int timeoutMs = 5000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.Fail("timed out waiting for the expected condition");
        }

        /// <summary>
        /// A stand-in for the robot. It records what it was asked to do, can be held
        /// open on a gate so a test can observe the queue, and can refuse a cancel.
        /// </summary>
        private sealed class ScriptedExecutor : IIntentExecutor
        {
            public ConcurrentQueue<string> StartedQueue { get; } = new();
            public string[] Started => StartedQueue.ToArray();
            public SemaphoreSlim? Gate { get; set; }
            public bool RefuseCancel { get; set; }
            public bool HonourCancellation { get; set; }
            public IntentOutcome Outcome { get; set; } = IntentOutcome.Success;

            public async ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution, CancellationToken cancellationToken)
            {
                StartedQueue.Enqueue(execution.Intent.IntentId ?? execution.IntentId);
                execution.Progress.ReportProgress(0);

                if (Gate != null)
                {
                    try
                    {
                        await Gate.WaitAsync(
                            HonourCancellation ? cancellationToken : CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return new IntentOutcome { State = ExecutionStateEnum.Cancelled };
                    }
                }
                else if (HonourCancellation)
                {
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return new IntentOutcome { State = ExecutionStateEnum.Cancelled };
                    }
                }

                execution.Progress.ReportProgress(1);
                return Outcome;
            }

            public bool CanCancel(IntentExecution execution)
            {
                return !RefuseCancel;
            }
        }
    }
}
