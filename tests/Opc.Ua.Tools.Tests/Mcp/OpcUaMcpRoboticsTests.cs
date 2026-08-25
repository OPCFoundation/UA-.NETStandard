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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Tests Robot Intent MCP embedding and refusal-preserving translations.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class OpcUaMcpRoboticsTests
    {
        [TestCase(McpToolProfile.Robotics)]
        [TestCase(McpToolProfile.Full)]
        public void ProfilesThatSelectRoboticsContributeTools(McpToolProfile profile)
        {
            var services = new ServiceCollection();

            services.AddMcpServer().WithOpcUaRoboticsTools(profile);

            HashSet<string> tools = ResolveToolNames(services);

            Assert.That(tools, Does.Contain("robotics_list_controllers"));
            Assert.That(tools, Does.Contain("robotics_read_state"));
            Assert.That(tools, Does.Contain("robotics_submit_linear_move"));
            Assert.That(tools, Does.Contain("robotics_submit_mission"));
        }

        [TestCase(McpToolProfile.Core)]
        [TestCase(McpToolProfile.Services)]
        [TestCase(McpToolProfile.Administration)]
        [TestCase(McpToolProfile.PubSub)]
        [TestCase(McpToolProfile.Diagnostics)]
        public void ProfilesThatDoNotSelectRoboticsContributeNoTools(McpToolProfile profile)
        {
            var services = new ServiceCollection();

            services.AddMcpServer().WithOpcUaRoboticsTools(profile);

            Assert.That(ResolveToolNames(services), Is.Empty);
        }

        [Test]
        public void AddOpcUaMcpRoboticsRegistersManager()
        {
            var services = new ServiceCollection();

            services.AddOpcUaMcpRobotics();

            Assert.That(services.Any(d => d.ServiceType == typeof(RoboticsIntentManager)), Is.True);
        }

        [Test]
        public async Task DirectControlRefusalSurvivesIntact()
        {
            var transport = new FakeRobotIntentTransport
            {
                SubmissionResult = new IntentSubmissionResult
                {
                    Accepted = false,
                    Failure = IntentFailureEnum.SafetyLimitExceeded,
                    Message = new LocalizedText("safe speed limit active")
                }
            };
            var controller = new RobotIntentControllerClient(transport);
            var input = new LinearMoveIntentInput
            {
                Target = new PoseDto
                {
                    Position = new PosePositionDto { X = 1, Y = 2, Z = 3 },
                    Orientation = new QuaternionDto { X = 0, Y = 0, Z = 0, W = 1 }
                },
                SpeedFraction = 0.2
            };
            IntentDataType intent = RoboticsIntentDtoConverter.ConvertLinearMove(input, null);

            IntentSubmissionResult result = await controller.TrySubmitIntentAsync(intent, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Failure, Is.EqualTo(IntentFailureEnum.SafetyLimitExceeded));
            Assert.That(result.Message.Text, Is.EqualTo("safe speed limit active"));
            Assert.That(transport.SubmitIntentCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task BoundedWaitReturnsCurrentStateOnTimeout()
        {
            var operationNode = new NodeId("operation1", 2);
            var transport = new FakeRobotIntentTransport
            {
                OperationSnapshot = new IntentOperationSnapshot
                {
                    Operation = operationNode,
                    IntentId = "intent1",
                    ExecutionState = ExecutionStateEnum.Executing,
                    Progress = 42
                }
            };
            var controller = new RobotIntentControllerClient(transport);

            IntentOperationWaitResult result = await RoboticsMonitoringTools.WaitOperationCoreAsync(
                controller,
                "intent1",
                operationNode.ToString(),
                1,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Completed, Is.False);
            Assert.That(result.Current.Progress, Is.EqualTo(42));
            Assert.That(result.Current.Operation.IsNull, Is.False);
        }

        [Test]
        public async Task MonitoringAndMissionToolsUseFakeTransport()
        {
            var transport = new FakeRobotIntentTransport
            {
                ControllerInfo = new RobotIntentControllerInfo { AxisCount = 6 },
                ControllerState = new RobotIntentControllerState { ControllerId = new NodeId("controller1", 2) },
                MissionResult = new MissionSubmissionResult
                {
                    Accepted = false,
                    Failure = IntentFailureEnum.ControlNotOwned,
                    Message = new LocalizedText("command authority required")
                }
            };
            var controller = new RobotIntentControllerClient(transport);
            MissionStepInput[] steps =
            [
                new MissionStepInput
                {
                    StepId = "s1",
                    Released = true,
                    Intent = new MissionIntentInput
                    {
                        Kind = IntentKind.Wait,
                        Wait = new WaitIntentInput { Duration = 10 }
                    }
                }
            ];

            RobotIntentControllerInfo info = await controller.ReadAsync().ConfigureAwait(false);
            RobotIntentControllerState state = await controller.ReadStateAsync().ConfigureAwait(false);
            MissionDataType mission = RoboticsMissionTools.BuildMission("m1", 1, steps, default, null, null);
            MissionSubmissionResult missionResult = await controller.SubmitMissionAsync(mission).ConfigureAwait(false);

            Assert.That(info.AxisCount, Is.EqualTo(6));
            Assert.That(state.ControllerId.IsNull, Is.False);
            Assert.That(missionResult.Failure, Is.EqualTo(IntentFailureEnum.ControlNotOwned));
            Assert.That(missionResult.Message.Text, Is.EqualTo("command authority required"));
        }

        private static HashSet<string> ResolveToolNames(IServiceCollection services)
        {
            using ServiceProvider provider = services.BuildServiceProvider();
            return provider
                .GetServices<McpServerTool>()
                .Select(tool => tool.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);
        }

        private sealed class FakeRobotIntentTransport : IRobotIntentTransport
        {
            public event RobotIntentReconnectHandler? Reconnected
            {
                add { }
                remove { }
            }

            public ILogger Logger { get; } = NullLogger.Instance;

            public NodeId ControllerId { get; } = new("controller1", 2);

            public NamespaceTable NamespaceUris { get; } = new();

            public IServiceMessageContext MessageContext { get; } = new ServiceMessageContext(
                DefaultTelemetry.Create(static _ => { }),
                EncodeableFactory.Create());

            public RobotIntentControllerInfo ControllerInfo { get; init; } = new();

            public RobotIntentControllerState ControllerState { get; init; } = new();

            public IntentSubmissionResult SubmissionResult { get; init; } = new() { Accepted = true };

            public MissionSubmissionResult MissionResult { get; init; } = new() { Accepted = true };

            public IntentOperationSnapshot OperationSnapshot { get; init; } = new();

            public int SubmitIntentCallCount { get; private set; }

            public ValueTask<ArrayOf<RobotIntentNodeLookupEntry>> BrowseControllersAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult<ArrayOf<RobotIntentNodeLookupEntry>>(
                    [new RobotIntentNodeLookupEntry(ControllerId, new QualifiedName("controller1"), "controller1")]);
            }

            public ValueTask<RobotIntentControllerInfo> ReadControllerAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult(ControllerInfo);
            }

            public ValueTask<RobotIntentControllerState> ReadControllerStateAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult(ControllerState);
            }

            public ValueTask<ArrayOf<IntentOperationSnapshot>> ListOperationsAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult<ArrayOf<IntentOperationSnapshot>>([OperationSnapshot]);
            }

            public ValueTask<ArrayOf<MissionSnapshot>> ListMissionsAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult<ArrayOf<MissionSnapshot>>([]);
            }

            public ValueTask<IntentSubmissionResult> SubmitIntentAsync(
                IntentDataType intent,
                CancellationToken ct = default)
            {
                SubmitIntentCallCount++;
                return ValueTask.FromResult(SubmissionResult);
            }

            public ValueTask<IntentCommandOutcome> CancelIntentAsync(
                string intentId,
                StopModeEnum stopMode,
                CancellationToken ct = default)
            {
                return ValueTask.FromResult(new IntentCommandOutcome(true));
            }

            public ValueTask<uint> CancelAllAsync(StopModeEnum stopMode, CancellationToken ct = default)
            {
                return ValueTask.FromResult<uint>(1);
            }

            public ValueTask<IntentCommandOutcome> PauseAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult(new IntentCommandOutcome(true));
            }

            public ValueTask<IntentCommandOutcome> ResumeAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult(new IntentCommandOutcome(true));
            }

            public ValueTask<IntentSubmissionResult> RetryAsync(string intentId, CancellationToken ct = default)
            {
                return ValueTask.FromResult(SubmissionResult);
            }

            public ValueTask<MissionSubmissionResult> SubmitMissionAsync(
                MissionDataType mission,
                CancellationToken ct = default)
            {
                return ValueTask.FromResult(MissionResult);
            }

            public ValueTask<MissionUpdateOutcome> UpdateMissionAsync(
                string missionId,
                uint missionUpdateId,
                ArrayOf<MissionStepDataType> steps,
                CancellationToken ct = default)
            {
                return ValueTask.FromResult(
                    new MissionUpdateOutcome(MissionUpdateResultEnum.Accepted, LocalizedText.Null));
            }

            public ValueTask<IntentCommandOutcome> CancelMissionAsync(
                string missionId,
                StopModeEnum stopMode,
                CancellationToken ct = default)
            {
                return ValueTask.FromResult(new IntentCommandOutcome(true));
            }

            public ValueTask<CommandAuthorityOutcome> RequestControlAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult(new CommandAuthorityOutcome(true, NodeId.Null));
            }

            public ValueTask ReleaseControlAsync(CancellationToken ct = default)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask<RealTimeChannelOpenResult> OpenRealTimeChannelAsync(
                string channelId,
                double requestedLease,
                CancellationToken ct = default)
            {
                return ValueTask.FromResult(new RealTimeChannelOpenResult { Granted = true });
            }

            public ValueTask<bool> CloseRealTimeChannelAsync(string channelId, CancellationToken ct = default)
            {
                return ValueTask.FromResult(true);
            }

            public ValueTask<NodeId> ResolveChildAsync(NodeId root, string browseName, CancellationToken ct = default)
            {
                return ValueTask.FromResult(new NodeId(browseName, 2));
            }

            public ValueTask<IntentOperationSnapshot> ReadOperationSnapshotAsync(
                NodeId operation,
                CancellationToken ct = default)
            {
                return ValueTask.FromResult(OperationSnapshot);
            }

            public ValueTask<MissionSnapshot> ReadMissionSnapshotAsync(
                NodeId mission,
                CancellationToken ct = default)
            {
                return ValueTask.FromResult(new MissionSnapshot());
            }

            public ValueTask<NodeId> ReadControlOwnerAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult(NodeId.Null);
            }

            public async IAsyncEnumerable<RobotIntentDataChange> SubscribeDataChangesAsync(
                ArrayOf<NodeId> nodeIds,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }
        }
    }
}
#endif
