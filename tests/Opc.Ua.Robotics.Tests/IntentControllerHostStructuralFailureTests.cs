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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Robotics.Server;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Tests;
using RiDataTypeIds = Opc.Ua.RobotIntent.DataTypeIds;
using RiNamespaces = Opc.Ua.RobotIntent.Namespaces;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Verifies host refusals that protect narrowed Robot Intent interoperability claims.
    /// </summary>
    [TestFixture]
    public class IntentControllerHostStructuralFailureTests
    {
        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.Append(RiNamespaces.RobotIntent);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
            m_executor = new RecordingExecutor();
        }

        [Test]
        public void SubmitIntentRejectsFastenJointNarrowingBeforeExecution()
        {
            using IntentControllerHost host = NewHost();

            IntentAdmission admission = host.SubmitIntent(m_context, null, new FastenIntentDataType
            {
                Joint = new NodeId("joint", 1),
                TargetTorque = 1.0
            });

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
                Assert.That(admission.Message, Does.Contain("OPC 40450/40451"));
                Assert.That(m_executor.Started.ToArray(), Is.Empty);
            });
        }

        [Test]
        public void SubmitMissionRejectsFastenJointNarrowingBeforeCreatingMission()
        {
            using IntentControllerHost host = NewHost();

            MissionAdmission admission = host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[]
                {
                    Step("s1", 1, new FastenIntentDataType
                    {
                        Joint = new NodeId("joint", 1),
                        TargetTorque = 1.0
                    })
                }
            });

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
                Assert.That(admission.Message, Does.Contain("OPC 40450/40451"));
                Assert.That(m_executor.Started.ToArray(), Is.Empty);
            });
        }

        [Test]
        public async Task MissionFailsWithNoTransitionWhenSelectedTransitionTargetsMissingStep()
        {
            IntentControllerHostOptions options = Options();
            options.MissionBranchingSupported = true;
            using IntentControllerHost host = NewHost(options);

            m_executor.Gate = new SemaphoreSlim(0);
            var missionData = new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1, Move("s1")), Step("s2", 2, Move("s2")) },
                Transitions = new[] { Transition("s1", "s2") }
            };

            MissionAdmission admission = host.SubmitMission(m_context, null, missionData);

            Assert.That(admission.Accepted, Is.True, admission.Message);
            await WaitAsync(() => !m_executor.Started.IsEmpty).ConfigureAwait(false);
            missionData.Steps = new[] { missionData.Steps[0] }.ToArrayOf();
            m_executor.Gate.Release();
            await WaitAsync(() => MissionReached(ExecutionStateEnum.Failed)).ConfigureAwait(false);

            MissionObjectState mission = m_executor.Added.OfType<MissionObjectState>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(m_executor.Started.ToArray(), Is.EqualTo(s_stringValues1));
                Assert.That(mission.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Failed));
                Assert.That(MissionFailure(mission), Is.EqualTo(IntentFailureEnum.NoTransition));
            });
        }

        [Test]
        public async Task GraphedMissionSucceedsWhenFinalStepHasNoOutgoingTransition()
        {
            IntentControllerHostOptions options = Options();
            options.MissionBranchingSupported = true;
            using IntentControllerHost host = NewHost(options);

            MissionAdmission admission = host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1, Move("s1")), Step("s2", 2, Move("s2")) },
                Transitions = new[] { Transition("s1", "s2") }
            });

            Assert.That(admission.Accepted, Is.True, admission.Message);
            await WaitAsync(() => MissionReached(ExecutionStateEnum.Succeeded)).ConfigureAwait(false);

            MissionObjectState mission = m_executor.Added.OfType<MissionObjectState>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(m_executor.Started.ToArray(), Is.EqualTo(s_stringValues2));
                Assert.That(mission.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(MissionFailure(mission), Is.EqualTo(IntentFailureEnum.None));
            });
        }

        [Test]
        public async Task BranchPointFailsWithNoTransitionWhenNoConditionMatches()
        {
            IntentControllerHostOptions options = Options();
            options.MissionBranchingSupported = true;
            using IntentControllerHost host = NewHost(options);

            MissionAdmission admission = host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1, Move("s1")), Step("s2", 2, Move("s2")) },
                Transitions = new[] { Transition("s1", "s2", NonEmptyFilter()) }
            });

            Assert.That(admission.Accepted, Is.True, admission.Message);
            await WaitAsync(() => MissionReached(ExecutionStateEnum.Failed)).ConfigureAwait(false);

            MissionObjectState mission = m_executor.Added.OfType<MissionObjectState>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(m_executor.Started.ToArray(), Is.EqualTo(s_stringValues1));
                Assert.That(mission.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Failed));
                Assert.That(MissionFailure(mission), Is.EqualTo(IntentFailureEnum.NoTransition));
            });
        }

        [Test]
        public async Task UngraphedMissionStillSucceedsSequentially()
        {
            using IntentControllerHost host = NewHost();

            MissionAdmission admission = host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1, Move("s1")), Step("s2", 2, Move("s2")) }
            });

            Assert.That(admission.Accepted, Is.True, admission.Message);
            await WaitAsync(() => MissionReached(ExecutionStateEnum.Succeeded)).ConfigureAwait(false);

            MissionObjectState mission = m_executor.Added.OfType<MissionObjectState>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(m_executor.Started.ToArray(), Is.EqualTo(s_stringValues2));
                Assert.That(mission.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(MissionFailure(mission), Is.EqualTo(IntentFailureEnum.None));
            });
        }

        private IntentControllerHost NewHost(IntentControllerHostOptions? options = null)
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
                    m_executor.Added.Enqueue(node);
                    return default;
                },
                options ?? Options());
            host.Start(m_context);
            return host;
        }

        private bool MissionReached(ExecutionStateEnum state)
        {
            return m_executor.Added
                .OfType<MissionObjectState>()
                .Any(mission => mission.ExecutionState?.Value == state);
        }

        private static IntentControllerHostOptions Options()
        {
            var options = new IntentControllerHostOptions
            {
                RequireControlAuthority = false,
                AxisCount = 6,
                MaxQueueDepth = 8
            };
            options.Accept(RiDataTypeIds.LinearMoveIntentDataType);
            options.Accept(RiDataTypeIds.FastenIntentDataType);
            return options;
        }

        private static MissionStepDataType Step(string id, uint sequence, IntentDataType intent)
        {
            intent.IntentId = id;
            return new MissionStepDataType
            {
                StepId = id,
                SequenceId = sequence,
                Intent = intent
            };
        }

        private static LinearMoveIntentDataType Move(string id)
        {
            return new LinearMoveIntentDataType
            {
                IntentId = id,
                Target = new Pose3DDataType
                {
                    FrameId = "base",
                    Position = new[] { 1.0, 0.0, 0.0 },
                    Orientation = new[] { 0.0, 0.0, 0.0, 1.0 }
                }
            };
        }

        private static MissionTransitionDataType Transition(
            string from,
            string to,
            ContentFilter? condition = null)
        {
            return new MissionTransitionDataType
            {
                FromStepId = from,
                ToStepId = to,
                DivergenceKind = DivergenceKindEnum.Alternative,
                Condition = condition ?? new ContentFilter()
            };
        }

        private IntentFailureEnum MissionFailure(MissionObjectState mission)
        {
            Assert.That(mission.FinalResultData, Is.Not.Null);
            NodeState? child = mission.FinalResultData!.FindChild(
                m_context,
                new QualifiedName(nameof(IntentResultDataType.Failure), mission.BrowseName.NamespaceIndex));
            Assert.That(child, Is.InstanceOf<BaseDataVariableState>());
            BaseDataVariableState failure = (BaseDataVariableState)child!;
            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.Value, Is.TypeOf<Variant>());
            var value = (Variant)failure.Value;
            Assert.That(value.TryGetValue(out int result), Is.True);
            return (IntentFailureEnum)result;
        }

        private static ContentFilter NonEmptyFilter()
        {
            return new ContentFilter
            {
                Elements = new[] { new ContentFilterElement { FilterOperator = FilterOperator.IsNull } }
            };
        }

        private static async Task WaitAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
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

        private SystemContext m_context = null!;
        private RecordingExecutor m_executor = null!;

        private sealed class RecordingExecutor : IIntentExecutor
        {
            public ConcurrentQueue<NodeState> Added { get; } = new();
            public ConcurrentQueue<string> Started { get; } = new();
            public SemaphoreSlim? Gate { get; set; }

            public ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution,
                CancellationToken cancellationToken)
            {
                Started.Enqueue(execution.Intent.IntentId ?? execution.IntentId);
                if (Gate != null)
                {
                    return AwaitGateAsync(Gate, cancellationToken);
                }
                return new ValueTask<IntentOutcome>(IntentOutcome.Success);
            }

            private static async ValueTask<IntentOutcome> AwaitGateAsync(
                SemaphoreSlim gate,
                CancellationToken cancellationToken)
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                return IntentOutcome.Success;
            }

            public bool CanCancel(IntentExecution execution)
            {
                return true;
            }
        }

        private static readonly string[] s_stringValues1 = ["s1"];
        private static readonly string[] s_stringValues2 = ["s1", "s2"];
    }
}
