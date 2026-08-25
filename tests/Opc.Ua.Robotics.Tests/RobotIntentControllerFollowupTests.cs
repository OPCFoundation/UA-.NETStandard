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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;
using Opc.Ua.Tests;

namespace Opc.Ua.Robotics.Client.Tests
{
    [TestFixture]
    [Category("Robotics")]
    public sealed class RobotIntentControllerFollowupTests
    {
        [Test]
        public void ADefaultControllerStateExposesNullNodeIdsRatherThanNullReferences()
        {
            var state = new RobotIntentControllerState();

            Assert.That(state.ControlOwner.Available, Is.False);
            Assert.That(state.ActiveIntent.Available, Is.False);
            Assert.That(state.ActiveMission.Available, Is.False);
            Assert.That(state.ControlOwner.Value.IsNull, Is.True);
            Assert.That(state.ActiveIntent.Value.IsNull, Is.True);
            Assert.That(state.ActiveMission.Value.IsNull, Is.True);
        }

        [Test]
        public void ADefaultSafetyStateExposesANullLocalizedTextRatherThanANullReference()
        {
            var safety = new RobotIntentSafetyStateSnapshot();

            Assert.That(safety.LastStopReason.Available, Is.False);
            Assert.That(safety.LastStopReason.Value.IsNull, Is.True);
        }

        [Test]
        public void ADefaultOptionalValueMatchesTheUnavailableSingleton()
        {
            RobotIntentOptionalValue<NodeId> fromDefault = default;

            Assert.That(fromDefault.Available, Is.False);
            Assert.That(fromDefault.Value.IsNull, Is.True);
            Assert.That(fromDefault, Is.EqualTo(RobotIntentOptionalValue<NodeId>.Unavailable));
        }

        [Test]
        public async Task ReadStateReturnsActiveIntentRuntimeMembers()
        {
            NodeId activeIntent = new("active-intent", 2);
            NodeId activeMission = new("active-mission", 2);
            FakeTransport transport = new()
            {
                State = new RobotIntentControllerState
                {
                    ControllerId = ControllerNode,
                    OperationalMode = RobotIntentOptionalValue<OperationalModeEnum>.FromValue(
                        OperationalModeEnum.AutomaticExternal),
                    Ready = RobotIntentOptionalValue<bool>.FromValue(true),
                    ControlOwner = RobotIntentOptionalValue<NodeId>.FromValue(new NodeId("owner", 2)),
                    MaxQueueDepth = RobotIntentOptionalValue<uint>.FromValue(3),
                    ActiveIntent = RobotIntentOptionalValue<NodeId>.FromValue(activeIntent),
                    ActiveMission = RobotIntentOptionalValue<NodeId>.FromValue(activeMission),
                    SafetyState = new RobotIntentSafetyStateSnapshot
                    {
                        Available = true,
                        EmergencyStopActive = RobotIntentOptionalValue<bool>.FromValue(false),
                        ProtectiveStopActive = RobotIntentOptionalValue<bool>.FromValue(false),
                        SafetyControllerOk = RobotIntentOptionalValue<bool>.FromValue(true)
                    }
                }
            };
            RobotIntentControllerClient controller = new(transport);

            RobotIntentControllerState state = await controller.ReadStateAsync();

            Assert.Multiple(() =>
            {
                Assert.That(state.OperationalMode.Available, Is.True);
                Assert.That(state.OperationalMode.Value, Is.EqualTo(OperationalModeEnum.AutomaticExternal));
                Assert.That(state.Ready.Available, Is.True);
                Assert.That(state.Ready.Value, Is.True);
                Assert.That(state.ControlOwner.Value, Is.EqualTo(new NodeId("owner", 2)));
                Assert.That(state.MaxQueueDepth.Value, Is.EqualTo(3));
                Assert.That(state.ActiveIntent.Value, Is.EqualTo(activeIntent));
                Assert.That(state.ActiveMission.Value, Is.EqualTo(activeMission));
                Assert.That(state.SafetyState.Available, Is.True);
                Assert.That(state.SafetyState.SafetyControllerOk.Value, Is.True);
                Assert.That(transport.ReadStateCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ReadStateReturnsNullActiveIntentWhenNoIntentRuns()
        {
            FakeTransport transport = new()
            {
                State = new RobotIntentControllerState
                {
                    ActiveIntent = RobotIntentOptionalValue<NodeId>.FromValue(NodeId.Null),
                    ActiveMission = RobotIntentOptionalValue<NodeId>.FromValue(NodeId.Null)
                }
            };
            RobotIntentControllerClient controller = new(transport);

            RobotIntentControllerState state = await controller.ReadStateAsync();

            Assert.Multiple(() =>
            {
                Assert.That(state.ActiveIntent.Available, Is.True);
                Assert.That(state.ActiveIntent.Value.IsNull, Is.True);
                Assert.That(state.ActiveMission.Available, Is.True);
                Assert.That(state.ActiveMission.Value.IsNull, Is.True);
            });
        }

        [Test]
        public async Task ReadStateReportsAbsentMemberAsUnavailable()
        {
            FakeTransport transport = new()
            {
                State = new RobotIntentControllerState
                {
                    ActiveIntent = RobotIntentOptionalValue<NodeId>.FromValue(new NodeId("active", 2)),
                    ActiveMission = RobotIntentOptionalValue<NodeId>.Unavailable
                }
            };
            RobotIntentControllerClient controller = new(transport);

            RobotIntentControllerState state = await controller.ReadStateAsync();

            Assert.Multiple(() =>
            {
                Assert.That(state.ActiveIntent.Available, Is.True);
                Assert.That(state.ActiveMission.Available, Is.False);
            });
        }

        [Test]
        public async Task ControllerCommandsDelegateToTransport()
        {
            FakeTransport transport = new();
            RobotIntentControllerClient controller = new(transport);

            IntentCommandOutcome cancel = await controller.CancelIntentAsync("intent-1", StopModeEnum.QuickStop);
            uint cancelAll = await controller.CancelAllAsync(StopModeEnum.OnPath);
            IntentCommandOutcome pause = await controller.PauseAsync();
            IntentCommandOutcome resume = await controller.ResumeAsync();
            IntentSubmissionResult retry = await controller.RetryAsync("intent-2");
            await controller.ReleaseControlAsync();

            Assert.Multiple(() =>
            {
                Assert.That(cancel.Accepted, Is.True);
                Assert.That(cancelAll, Is.EqualTo(4));
                Assert.That(pause.Accepted, Is.True);
                Assert.That(resume.Accepted, Is.False);
                Assert.That(retry.IntentId, Is.EqualTo("intent-2"));
                Assert.That(transport.LastCancelIntentId, Is.EqualTo("intent-1"));
                Assert.That(transport.LastCancelStopMode, Is.EqualTo(StopModeEnum.QuickStop));
                Assert.That(transport.LastCancelAllStopMode, Is.EqualTo(StopModeEnum.OnPath));
                Assert.That(transport.PauseCount, Is.EqualTo(1));
                Assert.That(transport.ResumeCount, Is.EqualTo(1));
                Assert.That(transport.LastRetryIntentId, Is.EqualTo("intent-2"));
                Assert.That(transport.ReleaseCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ListOperationsAndMissionsReturnsPublishedWork()
        {
            FakeTransport transport = new()
            {
                Operations =
                [
                    new IntentOperationSnapshot
                    {
                        Operation = new NodeId("op-from-before-connect", 2),
                        IntentId = "intent-foreign",
                        ExecutionState = ExecutionStateEnum.Executing,
                        QueuePosition = 0
                    }
                ],
                Missions =
                [
                    new MissionSnapshot
                    {
                        MissionNode = new NodeId("mission-from-before-connect", 2),
                        MissionId = "mission-foreign",
                        ExecutionState = ExecutionStateEnum.Queued,
                        CurrentStepId = "step-2"
                    }
                ]
            };
            RobotIntentControllerClient controller = new(transport);

            ArrayOf<IntentOperationSnapshot> operations = await controller.ListOperationsAsync();
            ArrayOf<MissionSnapshot> missions = await controller.ListMissionsAsync();

            Assert.Multiple(() =>
            {
                Assert.That(operations, Has.Count.EqualTo(1));
                Assert.That(operations[0].IntentId, Is.EqualTo("intent-foreign"));
                Assert.That(operations[0].ExecutionState, Is.EqualTo(ExecutionStateEnum.Executing));
                Assert.That(missions, Has.Count.EqualTo(1));
                Assert.That(missions[0].MissionId, Is.EqualTo("mission-foreign"));
                Assert.That(missions[0].CurrentStepId, Is.EqualTo("step-2"));
                Assert.That(transport.ListOperationsCount, Is.EqualTo(1));
                Assert.That(transport.ListMissionsCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task WaitForCompletionReturnsResultWhenCompleted()
        {
            FakeTransport transport = new()
            {
                Snapshot = Snapshot(ExecutionStateEnum.Executing)
            };
            RobotIntentControllerClient controller = new(transport);

            await using IntentOperationHandle handle = await controller.TrackOperationAsync("intent-1", OperationNode);
            transport.PublishChange("ExecutionState", Variant.From((int)ExecutionStateEnum.Succeeded));
            transport.PublishChange("Result", Variant.FromStructure(new IntentResultDataType
            {
                IntentId = "intent-1",
                State = ExecutionStateEnum.Succeeded
            }));

            IntentOperationWaitResult result = await handle.WaitForCompletionAsync(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(result.Completed, Is.True);
                Assert.That(result.Result.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(result.Current.ExecutionState, Is.EqualTo(ExecutionStateEnum.Succeeded));
            });
        }

        [Test]
        public async Task WaitForCompletionTimeoutReturnsRefreshedCurrentState()
        {
            FakeTransport transport = new()
            {
                Snapshot = Snapshot(ExecutionStateEnum.Executing)
            };
            RobotIntentControllerClient controller = new(transport);

            await using IntentOperationHandle handle = await controller.TrackOperationAsync("intent-1", OperationNode);
            transport.Snapshot = new IntentOperationSnapshot
            {
                Operation = OperationNode,
                IntentId = "intent-1",
                ExecutionState = ExecutionStateEnum.Suspended,
                Progress = 0.25
            };

            IntentOperationWaitResult result = await handle.WaitForCompletionAsync(TimeSpan.FromMilliseconds(20));

            Assert.Multiple(() =>
            {
                Assert.That(result.Completed, Is.False);
                Assert.That(result.Current.ExecutionState, Is.EqualTo(ExecutionStateEnum.Suspended));
                Assert.That(result.Current.Progress, Is.EqualTo(0.25));
                Assert.That(transport.ReadSnapshotCount, Is.EqualTo(2));
            });
        }

        [Test]
        public async Task RefreshClearsTheQueuePositionWhenTheServerReportsTheOperationIsNoLongerQueued()
        {
            FakeTransport transport = new()
            {
                Snapshot = new IntentOperationSnapshot
                {
                    Operation = OperationNode,
                    IntentId = "intent-1",
                    ExecutionState = ExecutionStateEnum.Queued,
                    QueuePosition = 3,
                    MissionId = "mission-1"
                }
            };
            RobotIntentControllerClient controller = new(transport);

            await using IntentOperationHandle handle = await controller.TrackOperationAsync("intent-1", OperationNode);
            Assert.That(handle.Current.QueuePosition, Is.EqualTo(3u));

            transport.Snapshot = new IntentOperationSnapshot
            {
                Operation = OperationNode,
                IntentId = "intent-1",
                ExecutionState = ExecutionStateEnum.Executing,
                QueuePosition = 0,
                MissionId = "mission-1"
            };
            await handle.RefreshAsync();

            Assert.Multiple(() =>
            {
                Assert.That(handle.Current.ExecutionState, Is.EqualTo(ExecutionStateEnum.Executing));
                Assert.That(handle.Current.QueuePosition, Is.Zero);
            });
        }

        private static IntentOperationSnapshot Snapshot(ExecutionStateEnum state)
        {
            return new IntentOperationSnapshot
            {
                Operation = OperationNode,
                IntentId = "intent-1",
                ExecutionState = state,
                Result = new IntentResultDataType
                {
                    IntentId = "intent-1",
                    State = state
                }
            };
        }

        private const string ExecutionStateName = "ExecutionState";
        private const string ProgressName = "Progress";
        private const string CurrentPoseName = "CurrentPose";
        private const string ResultName = "Result";

        private static readonly NodeId ControllerNode = new("controller", 2);
        private static readonly NodeId OperationNode = new("operation", 2);

        private sealed class FakeTransport : IRobotIntentTransport, IDisposable
        {
            public event RobotIntentReconnectHandler? Reconnected
            {
                add
                {
                }
                remove
                {
                }
            }

            public ILogger Logger { get; } = NullLogger.Instance;

            public NodeId ControllerId { get; } = ControllerNode;

            public NamespaceTable NamespaceUris { get; } = new();

            public IServiceMessageContext MessageContext { get; } =
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create(true));

            public RobotIntentControllerState State { get; set; } = new();

            public ArrayOf<IntentOperationSnapshot> Operations { get; set; } = [];

            public ArrayOf<MissionSnapshot> Missions { get; set; } = [];

            public IntentOperationSnapshot Snapshot { get; set; } = new();

            public int ReadStateCount { get; private set; }

            public int ListOperationsCount { get; private set; }

            public int ListMissionsCount { get; private set; }

            public int PauseCount { get; private set; }

            public int ResumeCount { get; private set; }

            public int ReleaseCount { get; private set; }

            public int ReadSnapshotCount { get; private set; }

            public string LastCancelIntentId { get; private set; } = string.Empty;

            public StopModeEnum LastCancelStopMode { get; private set; }

            public StopModeEnum LastCancelAllStopMode { get; private set; }

            public string LastRetryIntentId { get; private set; } = string.Empty;

            private Queue<RobotIntentDataChange> Changes { get; } = new();

            public void PublishChange(string browseName, Variant value)
            {
                Changes.Enqueue(new RobotIntentDataChange(ChildNode(browseName), value));
                m_available.Release();
            }

            public void Dispose()
            {
                m_available.Dispose();
            }

            public ValueTask<ArrayOf<RobotIntentNodeLookupEntry>> BrowseControllersAsync(
                CancellationToken ct = default)
            {
                return new ValueTask<ArrayOf<RobotIntentNodeLookupEntry>>([]);
            }

            public ValueTask<RobotIntentControllerInfo> ReadControllerAsync(CancellationToken ct = default)
            {
                return new ValueTask<RobotIntentControllerInfo>(new RobotIntentControllerInfo());
            }

            public ValueTask<RobotIntentControllerState> ReadControllerStateAsync(CancellationToken ct = default)
            {
                ReadStateCount++;
                return new ValueTask<RobotIntentControllerState>(State);
            }

            public ValueTask<ArrayOf<IntentOperationSnapshot>> ListOperationsAsync(CancellationToken ct = default)
            {
                ListOperationsCount++;
                return new ValueTask<ArrayOf<IntentOperationSnapshot>>(Operations);
            }

            public ValueTask<ArrayOf<MissionSnapshot>> ListMissionsAsync(CancellationToken ct = default)
            {
                ListMissionsCount++;
                return new ValueTask<ArrayOf<MissionSnapshot>>(Missions);
            }

            public ValueTask<IntentSubmissionResult> SubmitIntentAsync(
                IntentDataType intent,
                CancellationToken ct = default)
            {
                return new ValueTask<IntentSubmissionResult>(new IntentSubmissionResult());
            }

            public ValueTask<IntentCommandOutcome> CancelIntentAsync(
                string intentId,
                StopModeEnum stopMode,
                CancellationToken ct = default)
            {
                LastCancelIntentId = intentId;
                LastCancelStopMode = stopMode;
                return new ValueTask<IntentCommandOutcome>(new IntentCommandOutcome(true));
            }

            public ValueTask<uint> CancelAllAsync(StopModeEnum stopMode, CancellationToken ct = default)
            {
                LastCancelAllStopMode = stopMode;
                return new ValueTask<uint>(4);
            }

            public ValueTask<IntentCommandOutcome> PauseAsync(CancellationToken ct = default)
            {
                PauseCount++;
                return new ValueTask<IntentCommandOutcome>(new IntentCommandOutcome(true));
            }

            public ValueTask<IntentCommandOutcome> ResumeAsync(CancellationToken ct = default)
            {
                ResumeCount++;
                return new ValueTask<IntentCommandOutcome>(new IntentCommandOutcome(false));
            }

            public ValueTask<IntentSubmissionResult> RetryAsync(string intentId, CancellationToken ct = default)
            {
                LastRetryIntentId = intentId;
                return new ValueTask<IntentSubmissionResult>(new IntentSubmissionResult
                {
                    Accepted = true,
                    IntentId = intentId,
                    Operation = new NodeId("retry", 2)
                });
            }

            public ValueTask<MissionSubmissionResult> SubmitMissionAsync(
                MissionDataType mission,
                CancellationToken ct = default)
            {
                return new ValueTask<MissionSubmissionResult>(new MissionSubmissionResult());
            }

            public ValueTask<MissionUpdateOutcome> UpdateMissionAsync(
                string missionId,
                uint missionUpdateId,
                ArrayOf<MissionStepDataType> steps,
                CancellationToken ct = default)
            {
                return new ValueTask<MissionUpdateOutcome>(new MissionUpdateOutcome(
                    MissionUpdateResultEnum.Accepted,
                    LocalizedText.Null));
            }

            public ValueTask<IntentCommandOutcome> CancelMissionAsync(
                string missionId,
                StopModeEnum stopMode,
                CancellationToken ct = default)
            {
                return new ValueTask<IntentCommandOutcome>(new IntentCommandOutcome(true));
            }

            public ValueTask<CommandAuthorityOutcome> RequestControlAsync(CancellationToken ct = default)
            {
                return new ValueTask<CommandAuthorityOutcome>(new CommandAuthorityOutcome(true, NodeId.Null));
            }

            public ValueTask ReleaseControlAsync(CancellationToken ct = default)
            {
                ReleaseCount++;
                return default;
            }

            public ValueTask<RealTimeChannelOpenResult> OpenRealTimeChannelAsync(
                string channelId,
                double requestedLease,
                CancellationToken ct = default)
            {
                return new ValueTask<RealTimeChannelOpenResult>(new RealTimeChannelOpenResult());
            }

            public ValueTask<bool> CloseRealTimeChannelAsync(string channelId, CancellationToken ct = default)
            {
                return new ValueTask<bool>(true);
            }

            public ValueTask<NodeId> ResolveChildAsync(NodeId root, string browseName, CancellationToken ct = default)
            {
                return new ValueTask<NodeId>(ChildNode(browseName));
            }

            public ValueTask<IntentOperationSnapshot> ReadOperationSnapshotAsync(
                NodeId operation,
                CancellationToken ct = default)
            {
                ReadSnapshotCount++;
                return new ValueTask<IntentOperationSnapshot>(Snapshot);
            }

            public ValueTask<MissionSnapshot> ReadMissionSnapshotAsync(
                NodeId mission,
                CancellationToken ct = default)
            {
                return new ValueTask<MissionSnapshot>(new MissionSnapshot { MissionNode = mission });
            }

            public ValueTask<NodeId> ReadControlOwnerAsync(CancellationToken ct = default)
            {
                return new ValueTask<NodeId>(NodeId.Null);
            }

            public async IAsyncEnumerable<RobotIntentDataChange> SubscribeDataChangesAsync(
                ArrayOf<NodeId> nodeIds,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                while (!ct.IsCancellationRequested)
                {
                    await m_available.WaitAsync(ct).ConfigureAwait(false);
                    if (Changes.Count > 0)
                    {
                        yield return Changes.Dequeue();
                    }
                }
            }

            private readonly SemaphoreSlim m_available = new(0);

            private static NodeId ChildNode(string browseName)
            {
                return browseName switch
                {
                    ExecutionStateName => new NodeId(1),
                    ProgressName => new NodeId(2),
                    CurrentPoseName => new NodeId(3),
                    ResultName => new NodeId(4),
                    _ => new NodeId((uint)Math.Abs(browseName.GetHashCode(StringComparison.Ordinal)))
                };
            }
        }
    }
}
