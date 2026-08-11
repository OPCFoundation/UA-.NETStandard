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
    /// Specification-compliance regression tests for the Robot Intent host.
    /// </summary>
    [TestFixture]
    public class IntentHostComplianceTests
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
        }

        [TearDown]
        public void TearDown()
        {
            foreach (HostHarness harness in m_harnesses)
            {
                harness.Dispose();
            }
            m_harnesses.Clear();
        }

        [Test]
        public void SubmitMissionRefusesAuthorityBeforeInspectingParameters()
        {
            HostHarness harness = CreateHost(DefaultOptions(requireAuthority: true));

            MissionAdmission admission = harness.Host.SubmitMission(
                m_context, new NodeId("unauthorised", 1), null);

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ControlNotOwned));
                Assert.That(harness.Added.OfType<MissionObjectState>(), Is.Empty);
            });
        }

        [Test]
        public void SubmitMissionReportsSpecificRefusalFailures()
        {
            HostHarness mode = CreateHost(DefaultOptions(mode: OperationalModeEnum.ManualReducedSpeed));
            HostHarness capability = CreateHost(DefaultOptions(missionsSupported: false));

            MissionAdmission modeRefusal = mode.Host.SubmitMission(m_context, null, Mission());
            MissionAdmission capabilityRefusal = capability.Host.SubmitMission(m_context, null, Mission());

            Assert.Multiple(() =>
            {
                Assert.That(modeRefusal.Failure, Is.EqualTo(IntentFailureEnum.NotPermittedInMode));
                Assert.That(capabilityRefusal.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
            });
        }

        [Test]
        public void SubmitMissionAppliesSafetyGatesBeforeCreatingAMission()
        {
            HostHarness stop = CreateHost(DefaultOptions());
            stop.Host.UpdateSafetyState(m_context, new SafetyStatus { EmergencyStopActive = true });
            HostHarness speed = CreateHost(DefaultOptions());
            speed.Host.UpdateSafetyState(m_context, new SafetyStatus
            {
                SafeSpeedLimitActive = true,
                SafeSpeedLimit = 0.1
            });

            MissionAdmission stopRefusal = stop.Host.SubmitMission(m_context, null, Mission());
            MissionAdmission speedRefusal = speed.Host.SubmitMission(
                m_context, null, Mission(Step("s1", 1, Move(1.0))));

            Assert.Multiple(() =>
            {
                Assert.That(stopRefusal.Accepted, Is.False);
                Assert.That(stopRefusal.Failure, Is.EqualTo(IntentFailureEnum.NotPermittedInMode));
                Assert.That(speedRefusal.Accepted, Is.False);
                Assert.That(speedRefusal.Failure, Is.EqualTo(IntentFailureEnum.SafetyLimitExceeded));
                Assert.That(stop.Added.Concat(speed.Added).OfType<MissionObjectState>(), Is.Empty);
            });
        }

        [Test]
        public async Task AbortingSubmissionSupersedesNonCancelableCurrentIntent()
        {
            IntentControllerHostOptions options = DefaultOptions();
            HostHarness harness = CreateHost(options);
            harness.Executor.Gate = new SemaphoreSlim(0);
            harness.Executor.HonourCancellation = true;

            IntentAdmission first = harness.Host.SubmitIntent(m_context, null, Grasp("grasp"));
            await WaitAsync(() => harness.Executor.StartedContains("grasp")).ConfigureAwait(false);

            IntentAdmission second = harness.Host.SubmitIntent(m_context, null, Move());
            await WaitAsync(() => harness.Executor.CancellationObservedCount == 1).ConfigureAwait(false);
            IntentOperationState firstNode = FindOperation(harness, first.IntentId)!;
            await WaitAsync(() => firstNode.ExecutionState!.Value == ExecutionStateEnum.Cancelled)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(second.Accepted, Is.True);
                Assert.That(firstNode.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(firstNode.Result!.Value.Failure, Is.EqualTo(IntentFailureEnum.Superseded));
            });
        }

        [Test]
        public void SubmitMissionRefusesParallelDivergenceOnSerialHost()
        {
            HostHarness harness = CreateHost(DefaultOptions());
            MissionDataType mission = Mission(Step("s1", 1), Step("s2", 2), Step("s3", 3));
            mission.Transitions = new[]
            {
                Transition("s1", "s2", DivergenceKindEnum.Parallel, new ContentFilter()),
                Transition("s1", "s3", DivergenceKindEnum.Parallel, new ContentFilter())
            };

            MissionAdmission admission = harness.Host.SubmitMission(m_context, null, mission);

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
                Assert.That(harness.Added.OfType<MissionObjectState>(), Is.Empty);
            });
        }

        [Test]
        public async Task UnresolvedMissionBranchTerminatesMissionFailed()
        {
            HostHarness harness = CreateHost(DefaultOptions());
            MissionDataType mission = Mission(Step("s1", 1), Step("s2", 2));
            mission.Transitions = new[]
            {
                Transition("s1", "s2", DivergenceKindEnum.Alternative, NonEmptyFilter())
            };

            MissionAdmission admission = harness.Host.SubmitMission(m_context, null, mission);
            MissionObjectState missionNode = FindMission(harness, admission.Operation)!;
            await WaitAsync(() => missionNode.ExecutionState!.Value == ExecutionStateEnum.Failed)
                .ConfigureAwait(false);

            Assert.That(missionNode.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Failed));
        }

        [Test]
        public async Task CancelMissionReportsRefusalWhenRunningStepCannotCancel()
        {
            HostHarness harness = CreateHost(DefaultOptions());
            harness.Executor.Gate = new SemaphoreSlim(0);
            MissionAdmission admission = harness.Host.SubmitMission(
                m_context, null, Mission(Step("grasp", 1, Grasp("grasp"))));
            MissionObjectState missionNode = FindMission(harness, admission.Operation)!;
            await WaitAsync(() => harness.Executor.StartedContains("grasp")).ConfigureAwait(false);

            bool accepted = harness.Host.CancelMission(m_context, null, "m1", StopModeEnum.QuickStop);

            Assert.Multiple(() =>
            {
                Assert.That(accepted, Is.False);
                Assert.That(missionNode.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Executing));
            });
            harness.Executor.Gate.Release();
        }

        [Test]
        public async Task CancelMissionForwardsRequestedStopMode()
        {
            HostHarness harness = CreateHost(DefaultOptions());
            harness.Executor.Gate = new SemaphoreSlim(0);
            harness.Executor.HonourCancellation = true;
            harness.Host.SubmitMission(m_context, null, Mission());
            await WaitAsync(() => harness.Executor.StartedContains("s1")).ConfigureAwait(false);

            bool accepted = harness.Host.CancelMission(m_context, null, "m1", StopModeEnum.QuickStop);
            await WaitAsync(() => harness.Executor.CancellationObservedCount == 1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(accepted, Is.True);
                Assert.That(harness.Executor.LastStopMode, Is.EqualTo(StopModeEnum.QuickStop));
            });
        }

        [Test]
        public async Task CancelAllLeavesMissionExecutingWhenRunningStepRefusesCancel()
        {
            HostHarness harness = CreateHost(DefaultOptions());
            harness.Executor.Gate = new SemaphoreSlim(0);
            MissionAdmission admission = harness.Host.SubmitMission(
                m_context, null, Mission(Step("grasp", 1, Grasp("grasp"))));
            MissionObjectState missionNode = FindMission(harness, admission.Operation)!;
            await WaitAsync(() => harness.Executor.StartedContains("grasp")).ConfigureAwait(false);

            uint cancelled = harness.Host.CancelAll(m_context, null, StopModeEnum.QuickStop);

            Assert.Multiple(() =>
            {
                Assert.That(cancelled, Is.Zero);
                Assert.That(missionNode.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Executing));
            });
            harness.Executor.Gate.Release();
        }

        [Test]
        public async Task RetryChecksAuthorityAndCapabilityBeforeRetriableState()
        {
            HostHarness authority = CreateHost(DefaultOptions(requireAuthority: true));
            HostHarness capability = CreateHost(DefaultOptions());
            capability.Executor.Outcome = IntentOutcome.Fail(IntentFailureEnum.Other);
            IntentAdmission submitted = capability.Host.SubmitIntent(m_context, null, Grasp("grasp"));
            IntentOperationState node = FindOperation(capability, submitted.IntentId)!;
            await WaitAsync(() => node.ExecutionState!.Value == ExecutionStateEnum.Failed).ConfigureAwait(false);

            IntentAdmission noAuthority = authority.Host.Retry(m_context, new NodeId("s1", 1), "missing");
            IntentAdmission noCapability = capability.Host.Retry(m_context, null, submitted.IntentId);

            Assert.Multiple(() =>
            {
                Assert.That(noAuthority.Failure, Is.EqualTo(IntentFailureEnum.ControlNotOwned));
                Assert.That(noCapability.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
            });
        }

        [Test]
        public void DirectCapabilitiesMustDeclareAbortingAndDefaultPauseFalse()
        {
            var invalid = new IntentControllerHostOptions { RequireControlAuthority = false };
            invalid.Capabilities.Add(new DeclaredCapability
            {
                IntentType = RiDataTypeIds.LinearMoveIntentDataType,
                SupportedBufferModes = new[] { BufferModeEnum.Buffered }
            });
            var defaulted = new IntentControllerHostOptions { RequireControlAuthority = false };
            defaulted.Accept(RiDataTypeIds.LinearMoveIntentDataType);
            HostHarness harness = CreateHost(defaulted);
            var empty = new IntentControllerHostOptions { RequireControlAuthority = false };
            empty.Capabilities.Add(new DeclaredCapability
            {
                IntentType = RiDataTypeIds.LinearMoveIntentDataType,
                SupportedBufferModes = default
            });
            HostHarness emptyHarness = CreateHost(empty);
            LinearMoveIntentDataType blendingIntent = Move();
            blendingIntent.BufferMode = BufferModeEnum.BlendingHigh;
            IntentAdmission blending = emptyHarness.Host.SubmitIntent(m_context, null, blendingIntent);

            Assert.Multiple(() =>
            {
                Assert.Throws<ServiceResultException>(() => CreateHost(invalid));
                Assert.That(defaulted.Capabilities[0].PauseSupported, Is.False);
                Assert.That(harness.Controller.Capabilities!.SupportedIntents!.Value[0].PauseSupported, Is.False);
                Assert.That(blending.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
            });
        }

        [Test]
        public void FastenJointRefusalNamesTheUnsupportedInteropNarrowing()
        {
            IntentControllerHostOptions options = DefaultOptions();
            options.Accept(RiDataTypeIds.FastenIntentDataType);
            HostHarness harness = CreateHost(options);

            IntentAdmission admission = harness.Host.SubmitIntent(m_context, null, new FastenIntentDataType
            {
                Joint = new NodeId("joint", 1),
                TargetTorque = 1.0
            });

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
                Assert.That(admission.Message, Does.Contain("OPC 40450/40451"));
            });
        }

        [Test]
        public async Task ActiveMissionIsPublishedWhenTheNodeExists()
        {
            HostHarness harness = CreateHost(DefaultOptions(), addActiveMission: true);
            harness.Executor.Gate = new SemaphoreSlim(0);

            MissionAdmission admission = harness.Host.SubmitMission(m_context, null, Mission());
            await WaitAsync(() => !Equals(harness.ActiveMission!.Value, NodeId.Null)).ConfigureAwait(false);

            Assert.That(harness.ActiveMission!.Value, Is.EqualTo(admission.Operation));
            harness.Executor.Gate.Release();
        }

        private HostHarness CreateHost(IntentControllerHostOptions options, bool addActiveMission = false)
        {
            var controller = new IntentControllerState(null);
            controller.Create(
                m_context,
                new NodeId(Guid.NewGuid().ToString(), 1),
                new QualifiedName("Controller", 1),
                new LocalizedText("Controller"),
                true);
            BaseDataVariableState? activeMission = null;
            if (addActiveMission)
            {
                activeMission = new BaseDataVariableState(controller)
                {
                    NodeId = new NodeId(Guid.NewGuid().ToString(), 1),
                    BrowseName = new QualifiedName("ActiveMission", 1),
                    DisplayName = new LocalizedText("ActiveMission"),
                    ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent,
                    TypeDefinitionId = global::Opc.Ua.VariableTypeIds.BaseDataVariableType,
                    DataType = global::Opc.Ua.DataTypeIds.NodeId,
                    ValueRank = global::Opc.Ua.ValueRanks.Scalar,
                    Value = NodeId.Null
                };
                controller.AddChild(activeMission);
            }
            var executor = new ScriptedExecutor();
            var added = new List<NodeState>();
            var harness = new HostHarness(controller, executor, added, activeMission);
            harness.Host = new IntentControllerHost(
                controller,
                executor,
                (node, _) =>
                {
                    lock (added)
                    {
                        added.Add(node);
                    }
                    return default;
                },
                options);
            harness.Host.Start(m_context);
            m_harnesses.Add(harness);
            return harness;
        }

        private static IntentControllerHostOptions DefaultOptions(
            OperationalModeEnum mode = OperationalModeEnum.AutomaticExternal,
            bool requireAuthority = false,
            bool missionsSupported = true)
        {
            var options = new IntentControllerHostOptions
            {
                OperationalMode = mode,
                RequireControlAuthority = requireAuthority,
                MissionsSupported = missionsSupported,
                AxisCount = 6,
                MaxQueueDepth = 4
            };
            options.Accept(RiDataTypeIds.LinearMoveIntentDataType);
            options.Accept(RiDataTypeIds.GraspIntentDataType, cancelSupported: false);
            return options;
        }

        private static MissionDataType Mission(params MissionStepDataType[] steps)
        {
            return new MissionDataType
            {
                MissionId = "m1",
                Steps = steps.Length == 0 ? new[] { Step("s1", 1) } : steps
            };
        }

        private static MissionStepDataType Step(string id, uint sequence)
        {
            return Step(id, sequence, Move(0));
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

        private static LinearMoveIntentDataType Move(double cartesianSpeed = 0)
        {
            var intent = new LinearMoveIntentDataType
            {
                BufferMode = BufferModeEnum.Aborting,
                Target = Pose()
            };
            if (cartesianSpeed > 0)
            {
                intent.Constraints = new MotionConstraintsDataType { CartesianSpeed = cartesianSpeed };
            }
            return intent;
        }

        private static GraspIntentDataType Grasp(string id)
        {
            return new GraspIntentDataType
            {
                IntentId = id,
                BufferMode = BufferModeEnum.Aborting
            };
        }

        private static Pose3DDataType Pose()
        {
            return new Pose3DDataType
            {
                Position = new[] { 1.0, 0.0, 0.0 },
                Orientation = new[] { 0.0, 0.0, 0.0, 1.0 }
            };
        }

        private static MissionTransitionDataType Transition(
            string from,
            string to,
            DivergenceKindEnum divergence,
            ContentFilter condition)
        {
            return new MissionTransitionDataType
            {
                FromStepId = from,
                ToStepId = to,
                DivergenceKind = divergence,
                Condition = condition
            };
        }

        private static ContentFilter NonEmptyFilter()
        {
            return new ContentFilter
            {
                Elements = new[] { new ContentFilterElement { FilterOperator = FilterOperator.IsNull } }
            };
        }

        private static IntentOperationState? FindOperation(HostHarness harness, string intentId)
        {
            lock (harness.Added)
            {
                return harness.Added
                    .OfType<IntentOperationState>()
                    .FirstOrDefault(n => n.IntentId?.Value == intentId);
            }
        }

        private static MissionObjectState? FindMission(HostHarness harness, NodeId nodeId)
        {
            lock (harness.Added)
            {
                return harness.Added.OfType<MissionObjectState>().FirstOrDefault(n => n.NodeId == nodeId);
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

        private ServiceMessageContext m_messageContext = null!;
        private SystemContext m_context = null!;
        private readonly List<HostHarness> m_harnesses = [];

        private sealed class HostHarness(
            IntentControllerState controller,
            ScriptedExecutor executor,
            List<NodeState> added,
            BaseDataVariableState? activeMission) : IDisposable
        {
            public IntentControllerState Controller { get; } = controller;
            public ScriptedExecutor Executor { get; } = executor;
            public List<NodeState> Added { get; } = added;
            public BaseDataVariableState? ActiveMission { get; } = activeMission;
            public IntentControllerHost Host { get; set; } = null!;

            public void Dispose()
            {
                Host.Dispose();
            }
        }

        private sealed class ScriptedExecutor : IIntentExecutor
        {
            public List<string> Started { get; } = [];
            public SemaphoreSlim? Gate { get; set; }
            public bool HonourCancellation { get; set; }
            public IntentOutcome Outcome { get; set; } = IntentOutcome.Success;
            public StopModeEnum LastStopMode { get; private set; }
            public int CancellationObservedCount => Volatile.Read(ref m_cancellationObservedCount);

            public bool StartedContains(string intentId)
            {
                lock (Started)
                {
                    return Started.Contains(intentId);
                }
            }

            public async ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution, CancellationToken cancellationToken)
            {
                lock (Started)
                {
                    Started.Add(execution.Intent.IntentId ?? execution.IntentId);
                }

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
                        Interlocked.Increment(ref m_cancellationObservedCount);
                        return new IntentOutcome { State = ExecutionStateEnum.Cancelled };
                    }
                }

                return Outcome;
            }

            public bool CanCancel(IntentExecution execution)
            {
                return true;
            }

            private int m_cancellationObservedCount;
        }
    }
}
