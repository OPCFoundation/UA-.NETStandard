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
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Tests;
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
                    lock (m_addedLock)
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
            using IntentControllerHost host = NewHost(Options(OperationalModeEnum.ManualReducedSpeed));

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
            using IntentControllerHost host = NewHost(Options(requireAuthority: true));

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
            using IntentControllerHost host = NewHost(Options(requireAuthority: true));
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
            using IntentControllerHost host = NewHost(options);

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
            using IntentControllerHost host = NewHost(Options(requireAuthority: true));
            var bad = new LinearMoveIntentDataType { Target = null! };

            IntentAdmission admission = host.SubmitIntent(m_context, new NodeId("s1", 1), bad);

            Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ControlNotOwned));
        }

        [Test]
        public void RefusalCreatesNoOperationAndDoesNotExecute()
        {
            IntentAdmission admission = m_host.SubmitIntent(m_context, null,
                new LinearMoveIntentDataType { IntentId = "bad" });

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(admission.Message, Is.Not.Empty);
                Assert.That(FindOperation("bad"), Is.Null);
                Assert.That(m_executor.Started, Is.Empty);
            });
        }

        [Test]
        public void DuplicateOutstandingIntentIdIsRefused()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            Assert.That(m_host.SubmitIntent(m_context, null, Move("same")).Accepted, Is.True);

            IntentAdmission duplicate = m_host.SubmitIntent(m_context, null, Move("same"));

            Assert.That(duplicate.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
            m_executor.Gate.Release();
        }

        [Test]
        public async Task RetainedTerminalIntentIdCannotBeReused()
        {
            IntentAdmission first = m_host.SubmitIntent(m_context, null, Move("same"));
            await WaitForTerminalAsync(first.IntentId).ConfigureAwait(false);

            IntentAdmission second = m_host.SubmitIntent(m_context, null, Move("same"));

            Assert.That(second.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
        }

        [Test]
        public async Task SubmitMethodRefusalReturnsGoodWithOutputArguments()
        {
            using IntentControllerHost host = NewHost(Options(OperationalModeEnum.ManualReducedSpeed));

            SubmitIntentMethodStateResult result = await host.Controller.SubmitIntent!.OnCallAsync!(
                m_context,
                host.Controller.SubmitIntent,
                host.Controller.NodeId,
                Move(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.Accepted, Is.False);
                Assert.That(result.Failure, Is.EqualTo(IntentFailureEnum.NotPermittedInMode));
                Assert.That(result.Message.Text, Is.Not.Empty);
            });
        }

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
                Assert.That(node.FinalResultData!.FindChild(
                    m_context,
                    new QualifiedName(nameof(IntentOperationState.Result), node.BrowseName.NamespaceIndex)),
                    Is.Not.Null);
            });
        }

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
            Assert.That(m_executor.Started, Is.EqualTo(["a", "b"]));
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
            IntentResultDataType result = superseded.Result!.Value!;
            IntentResultDataType final = ReadFinalResult(superseded);
            Assert.Multiple(() =>
            {
                Assert.That(superseded.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(result.State, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(result.Failure, Is.EqualTo(IntentFailureEnum.Superseded),
                    "replaced work must be distinguishable from work a client cancelled");
                Assert.That(result.StartTime, Is.Not.EqualTo(default(DateTime)));
                Assert.That(result.EndTime, Is.GreaterThanOrEqualTo(result.StartTime));
                Assert.That(result.HasAchievedPose, Is.False);
                Assert.That(final.State, Is.EqualTo(result.State));
                Assert.That(final.Failure, Is.EqualTo(result.Failure));
                Assert.That(superseded.QueuePosition!.Value, Is.Zero);
            });
        }

        [Test]
        public void TheQueueIsBoundedByMaxQueueDepth()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            IntentControllerHostOptions options = Options();
            options.MaxQueueDepth = 1;
            using IntentControllerHost host = NewHost(options);

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

        [Test]
        public void MaxQueueDepthZeroAcceptsOnlyAbortingSubmissions()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            IntentControllerHostOptions options = Options();
            options.MaxQueueDepth = 0;
            using IntentControllerHost host = NewHost(options);

            Assert.That(host.SubmitIntent(m_context, null, Move("a")).Accepted, Is.True);
            IntentAdmission buffered = host.SubmitIntent(
                m_context, null, Move("b", BufferModeEnum.Buffered));

            Assert.Multiple(() =>
            {
                Assert.That(buffered.Accepted, Is.False);
                Assert.That(buffered.Failure, Is.EqualTo(IntentFailureEnum.QueueFull));
            });
            m_executor.Gate.Release(2);
        }

        [Test]
        public async Task QueuePositionsAreRenumberedAsTheQueueDrains()
        {
            m_executor.Gate = new SemaphoreSlim(0);

            m_host.SubmitIntent(m_context, null, Move("a"));
            m_host.SubmitIntent(m_context, null, Move("b", BufferModeEnum.Buffered));
            m_host.SubmitIntent(m_context, null, Move("c", BufferModeEnum.Buffered));
            await WaitAsync(() => FindOperation("c")?.QueuePosition?.Value > 0).ConfigureAwait(false);

            m_executor.Gate.Release();
            await WaitAsync(() => m_executor.Started.Contains("b")).ConfigureAwait(false);
            await WaitAsync(() => FindOperation("c")?.QueuePosition?.Value == 1).ConfigureAwait(false);
            m_executor.Gate.Release(2);

            Assert.That(FindOperation("c")!.QueuePosition!.Value, Is.LessThanOrEqualTo(1));
        }

        [Test]
        public async Task BlendingCompletesThePredecessorAtTheReportedPose()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            using var blendGate = new SemaphoreSlim(0);
            IntentControllerHostOptions options = Options();
            options.BlendingSupported = true;
            options.Capabilities.Clear();
            options.Capabilities.Add(new DeclaredCapability
            {
                IntentType = RiDataTypeIds.LinearMoveIntentDataType,
                SupportedBufferModes = new[]
                {
                    BufferModeEnum.Aborting,
                    BufferModeEnum.Buffered,
                    BufferModeEnum.BlendingNext
                }
            });
            using IntentControllerHost host = NewHost(options);
            m_executor.OnExecute = e =>
            {
                if (e.IntentId == "a")
                {
                    _ = Task.Run(async () =>
                    {
                        await blendGate.WaitAsync().ConfigureAwait(false);
                        e.Progress.ReportBlendBegin(Pose(2, 0, 0));
                        m_executor.Gate.Release();
                    });
                }
            };

            host.SubmitIntent(m_context, null, Move("a"));
            await WaitAsync(() => m_executor.Started.Contains("a")).ConfigureAwait(false);
            host.SubmitIntent(m_context, null, Move("b", BufferModeEnum.BlendingNext));
            Assert.That(FindOperation("a")!.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Executing));

            blendGate.Release();
            IntentOperationState first = await WaitForTerminalAsync("a").ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(first.Result!.Value!.HasAchievedPose, Is.True);
                Assert.That(first.Result.Value.AchievedPose.Position[0], Is.EqualTo(2));
            });
            m_executor.Gate.Release(2);
        }

        [Test]
        public async Task SingleOrHardWorkDoesNotBeginWhileAnotherIntentExecutes()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_host.SubmitIntent(m_context, null, Move("a"));
            IntentAdmission hard = m_host.SubmitIntent(
                m_context,
                null,
                new LinearMoveIntentDataType
                {
                    IntentId = "b",
                    Target = Pose(),
                    BufferMode = BufferModeEnum.Buffered,
                    BlockingMode = BlockingModeEnum.Hard
                });

            Assert.That(hard.Accepted, Is.True);
            await WaitAsync(() => m_executor.Started.Contains("a")).ConfigureAwait(false);
            Assert.That(m_executor.Started, Does.Not.Contain("b"));
            m_executor.Gate.Release(2);
        }

        [Test]
        public async Task CancellingQueuedWorkTerminatesItWithoutRunningIt()
        {
            m_executor.Gate = new SemaphoreSlim(0);

            m_host.SubmitIntent(m_context, null, Move("a"));
            m_host.SubmitIntent(m_context, null, Move("b", BufferModeEnum.Buffered));
            await WaitAsync(() => m_executor.Started.Contains("a")).ConfigureAwait(false);

            Assert.That(m_host.CancelIntent(m_context, null, "b"), Is.True);

            IntentOperationState cancelled = await WaitForTerminalAsync("b").ConfigureAwait(false);
            IntentResultDataType result = cancelled.Result!.Value!;
            IntentResultDataType final = ReadFinalResult(cancelled);
            Assert.Multiple(() =>
            {
                Assert.That(cancelled.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(result.State, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(result.Failure, Is.EqualTo(IntentFailureEnum.None));
                Assert.That(result.StartTime, Is.Not.EqualTo(default(DateTime)));
                Assert.That(result.EndTime, Is.GreaterThanOrEqualTo(result.StartTime));
                Assert.That(result.HasAchievedPose, Is.False);
                Assert.That(final.State, Is.EqualTo(result.State));
                Assert.That(final.Failure, Is.EqualTo(result.Failure));
                Assert.That(cancelled.QueuePosition!.Value, Is.Zero);
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

            Assert.That(m_host.CancelIntent(m_context, null, "a", StopModeEnum.QuickStop), Is.True);

            IntentOperationState node = await WaitForTerminalAsync("a").ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(node.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(node.Result!.Value!.Failure, Is.EqualTo(IntentFailureEnum.None));
                Assert.That(m_executor.LastStopMode, Is.EqualTo(StopModeEnum.QuickStop));
            });
        }

        [Test]
        public async Task CancelAllReturnsTheNumberOfCancelledOperations()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_host.SubmitIntent(m_context, null, Move("a"));
            m_host.SubmitIntent(m_context, null, Move("b", BufferModeEnum.Buffered));
            await WaitAsync(() => m_executor.Started.Contains("a")).ConfigureAwait(false);

            uint cancelled = m_host.CancelAll(m_context, null, StopModeEnum.ProcessStop);

            Assert.That(cancelled, Is.EqualTo(2));
            m_executor.Gate.Release(2);
        }

        [Test]
        public async Task SupersededExecutingIntentSeesQuickStop()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_executor.HonourCancellation = true;

            m_host.SubmitIntent(m_context, null, Move("a"));
            await WaitAsync(() => m_executor.Started.Contains("a")).ConfigureAwait(false);

            m_host.SubmitIntent(m_context, null, Move("b"));
            IntentOperationState first = await WaitForTerminalAsync("a").ConfigureAwait(false);
            m_executor.Gate.Release();
            await WaitForTerminalAsync("b").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(first.Result!.Value!.Failure, Is.EqualTo(IntentFailureEnum.Superseded));
                Assert.That(m_executor.LastStopMode, Is.EqualTo(StopModeEnum.QuickStop));
            });
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

        [Test]
        public async Task ProgramDiagnosticRecordsSubmissionAndTransitions()
        {
            var session = new NodeId("session", 1);

            IntentAdmission admission = m_host.SubmitIntent(m_context, session, Move("diag"));
            IntentOperationState node = await WaitForTerminalAsync(admission.IntentId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(node.ProgramDiagnostic, Is.Not.Null);
                Assert.That(node.ProgramDiagnostic!.CreateSessionId!.Value, Is.EqualTo(session));
                Assert.That(node.ProgramDiagnostic.CreateClientName!.Value, Is.Not.Null);
                Assert.That(node.ProgramDiagnostic.InvocationCreationTime!.Value, Is.Not.EqualTo(DateTime.MinValue));
                Assert.That(node.ProgramDiagnostic.LastTransitionTime!.Value, Is.Not.EqualTo(DateTime.MinValue));
                Assert.That(node.ProgramDiagnostic.LastMethodCall!.Value, Is.EqualTo("SubmitIntent"));
                Assert.That(node.ProgramDiagnostic.LastMethodReturnStatus!.Value, Is.EqualTo(StatusCodes.Good));
            });
        }

        [Test]
        public async Task ProgramTransitionEventIsReportedOnStateChange()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            IntentAdmission admission = m_host.SubmitIntent(m_context, null, Move("evented"));
            await WaitAsync(() => m_executor.Started.Contains("evented")).ConfigureAwait(false);
            IntentOperationState node = FindOperation(admission.IntentId)!;
            var events = new List<IFilterTarget>();
            node.OnReportEvent = (_, _, e) =>
            {
                lock (events)
                {
                    events.Add(e);
                }
            };
            node.SetAreEventsMonitored(m_context, true, true);

            m_executor.Gate.Release();
            await WaitAsync(
                () =>
                {
                    lock (events)
                    {
                        return events.OfType<TransitionEventState>().Any();
                    }
                },
                "transition event to be reported").ConfigureAwait(false);
        }

        [Test]
        public async Task RetentionPrunesOldTerminalOperationsBeyondTheConfiguredCount()
        {
            var removed = new List<NodeState>();
            IntentControllerHostOptions options = Options();
            options.RetainedTerminalOperations = 1;
            using IntentControllerHost host = NewHost(options, removed);

            host.SubmitIntent(m_context, null, Move("a"));
            await WaitForTerminalAsync("a").ConfigureAwait(false);
            host.SubmitIntent(m_context, null, Move("b"));
            await WaitForTerminalAsync("b").ConfigureAwait(false);

            await WaitAsync(
                () =>
                {
                    lock (removed)
                    {
                        return removed.OfType<IntentOperationState>().Count() == 1;
                    }
                },
                "one retained terminal operation to be pruned").ConfigureAwait(false);
        }

        [Test]
        public void NullSessionCannotAcquireAuthority()
        {
            using IntentControllerHost host = NewHost(Options(requireAuthority: true));

            bool granted = host.RequestControl(m_context, null, out NodeId? owner);

            Assert.Multiple(() =>
            {
                Assert.That(granted, Is.False);
                Assert.That(owner, Is.Null);
            });
        }

        [Test]
        public void SubmissionAfterDisposeIsRefused()
        {
            using IntentControllerHost host = NewHost(Options());
            host.Dispose();

            IntentAdmission admission = host.SubmitIntent(m_context, null, Move());

            Assert.That(admission.Accepted, Is.False);
        }

        [Test]
        public async Task DisposeAsyncCancelsAndDrainsExecutingWork()
        {
            m_executor.HonourCancellation = true;
            IntentControllerHost host = NewHost(Options());
            host.SubmitIntent(m_context, null, Move("running"));
            await WaitAsync(() => m_executor.Started.Contains("running")).ConfigureAwait(false);

            await host.DisposeAsync().ConfigureAwait(false);

            Assert.That(host.SubmitIntent(m_context, null, Move()).Accepted, Is.False);
        }

        [Test]
        public async Task DisposeAsyncIsBoundedWhenExecutorDoesNotReturn()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            IntentControllerHostOptions options = Options();
            options.ExecutorShutdownTimeoutMs = 50;
            IntentControllerHost host = NewHost(options);
            host.SubmitIntent(m_context, null, Move("hung"));
            await WaitAsync(() => m_executor.Started.Contains("hung")).ConfigureAwait(false);

            try
            {
                Task dispose = host.DisposeAsync().AsTask();
                Task completed = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

                Assert.That(completed, Is.SameAs(dispose));
            }
            finally
            {
                m_executor.Gate.Release();
            }
        }

        [Test]
        public async Task QueuedMissionStepCancellationAdvancesTheMission()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            MissionAdmission mission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1, released: true), Step("s2", 2, released: true) }
            });
            await WaitAsync(() => m_executor.Started.Contains("s1")).ConfigureAwait(false);

            Assert.That(m_host.Pause(m_context, null), Is.True);
            m_executor.Gate.Release();
            string queuedIntentId = string.Empty;
            await WaitAsync(() =>
            {
                IntentOperationState? queued;
                lock (m_addedLock)
                {
                    queued = m_added
                        .OfType<IntentOperationState>()
                        .FirstOrDefault(n => n.IntentId?.Value is { } id && id != "intent-1");
                }
                queuedIntentId = queued?.IntentId?.Value ?? string.Empty;
                return queuedIntentId.Length > 0;
            }).ConfigureAwait(false);

            Assert.That(m_host.CancelIntent(m_context, null, queuedIntentId), Is.True);

            await WaitAsync(() =>
            {
                lock (m_addedLock)
                {
                    return m_added.OfType<MissionObjectState>()
                        .Any(n => n.NodeId == mission.Operation &&
                            n.CurrentStepId?.Value.Length == 0 &&
                            n.ExecutionState?.Value == ExecutionStateEnum.Cancelled);
                }
            }).ConfigureAwait(false);
        }

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
        public void AMissionUpdateThatWouldAlterAReleasedIntentIsRefused()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                MissionUpdateId = 0,
                Steps = new[] { Step("s1", 1, released: true), Step("s2", 2, released: false) }
            });
            MissionStepDataType changed = Step("s1", 1, released: true);
            changed.Intent = new LinearMoveIntentDataType { IntentId = "changed", Target = Pose(9, 0, 0) };

            MissionUpdateOutcome outcome = m_host.UpdateMission(m_context, null, "m1", 1, new[]
            {
                changed,
                Step("s2", 2, released: false)
            });

            Assert.That(outcome.Result, Is.EqualTo(MissionUpdateResultEnum.BaseConflict));
            m_executor.Gate.Release(4);
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

        private IntentControllerHost NewHost(
            IntentControllerHostOptions options,
            List<NodeState>? removed = null)
        {
            var controller = new IntentControllerState(null);
            controller.Create(
                m_context,
                new NodeId(Guid.NewGuid().ToString(), 1),
                new QualifiedName("Controller", 1),
                new LocalizedText("Controller"),
                true);
            var host = new IntentControllerHost(
                controller,
                m_executor,
                (node, _) =>
                {
                    lock (m_addedLock)
                    {
                        m_added.Add(node);
                    }
                    return default;
                },
                options,
                removed == null
                    ? null
                    : (node, _) =>
                    {
                        lock (removed)
                        {
                            removed.Add(node);
                        }
                        return default;
                    });
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
            lock (m_addedLock)
            {
                return m_added
                    .OfType<IntentOperationState>()
                    .FirstOrDefault(n => n.IntentId?.Value == intentId);
            }
        }

        private IntentResultDataType ReadFinalResult(IntentOperationState node)
        {
            BaseDataVariableState result = (BaseDataVariableState)node.FinalResultData!.FindChild(
                m_context,
                new QualifiedName(nameof(IntentOperationState.Result), node.BrowseName.NamespaceIndex))!;
            Assert.That(result.Value.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out IEncodeable? encodeable), Is.True);
            return (IntentResultDataType)encodeable!;
        }

        private static Task WaitAsync(Func<bool> condition, int timeoutMs = 5000)
        {
            return WaitAsync(condition, "the expected condition", timeoutMs);
        }

        private static async Task WaitAsync(
            Func<bool> condition,
            string conditionDescription,
            int timeoutMs = 5000)
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
            Assert.Fail($"timed out waiting for {conditionDescription}");
        }

        private ServiceMessageContext m_messageContext = null!;
        private SystemContext m_context = null!;
        private IntentControllerState m_controller = null!;
        private ScriptedExecutor m_executor = null!;
        private IntentControllerHost m_host = null!;
        private readonly Lock m_addedLock = new();
        private readonly List<NodeState> m_added = [];

        /// <summary>
        /// A stand-in for the robot. It records what it was asked to do, can be held
        /// open on a gate so a test can observe the queue, and can refuse a cancel.
        /// </summary>
        private sealed class ScriptedExecutor : IIntentExecutor
        {
            public ConcurrentQueue<string> StartedQueue { get; } = new();
            public string[] Started => [.. StartedQueue];
            public SemaphoreSlim? Gate { get; set; }
            public bool RefuseCancel { get; set; }
            public bool HonourCancellation { get; set; }
            public IntentOutcome Outcome { get; set; } = IntentOutcome.Success;
            public Action<IntentExecution>? OnExecute { get; set; }
            public StopModeEnum LastStopMode { get; private set; }

            public async ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution, CancellationToken cancellationToken)
            {
                StartedQueue.Enqueue(execution.Intent.IntentId ?? execution.IntentId);
                execution.Progress.ReportProgress(0);
                OnExecute?.Invoke(execution);

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
                        LastStopMode = execution.StopMode;
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
                        LastStopMode = execution.StopMode;
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
