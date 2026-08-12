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
    /// Safety-focused tests for Robot Intent MCP translation.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class OpcUaMcpRoboticsSafetyTests
    {
        [TestCaseSource(nameof(Refusals))]
        public async Task RefusedIntentSubmissionsPreserveFailureAndMessageWithoutSideEffects(
            IntentFailureEnum failure,
            string message)
        {
            var transport = new CountingRobotIntentTransport
            {
                SubmissionResult = new IntentSubmissionResult
                {
                    Accepted = false,
                    Failure = failure,
                    Message = new LocalizedText(message)
                }
            };
            var controller = new RobotIntentControllerClient(transport);

            IntentSubmissionResult result = await RoboticsControlTools.SubmitIntentAsync(
                controller,
                "linearMove",
                kLinearMoveJson,
                0,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.False);
                Assert.That(result.Failure, Is.EqualTo(failure));
                Assert.That(result.Message.Text, Is.EqualTo(message));
                Assert.That(transport.SubmitIntentCallCount, Is.EqualTo(1));
                Assert.That(transport.RequestControlCallCount, Is.Zero);
            });
        }

        [Test]
        public async Task RefusedSubmissionReturnsOutcomeButTransportFailureThrows()
        {
            var refusedTransport = new CountingRobotIntentTransport
            {
                SubmissionResult = new IntentSubmissionResult
                {
                    Accepted = false,
                    Failure = IntentFailureEnum.ControlNotOwned,
                    Message = new LocalizedText("operator owns command authority")
                }
            };
            var failedTransport = new CountingRobotIntentTransport
            {
                SubmitException = new ServiceResultException(StatusCodes.BadSessionClosed, "channel closed")
            };

            IntentSubmissionResult refused = await RoboticsControlTools.SubmitIntentAsync(
                new RobotIntentControllerClient(refusedTransport),
                "linearMove",
                kLinearMoveJson,
                0,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(refused.Accepted, Is.False);
                Assert.That(refused.Failure, Is.EqualTo(IntentFailureEnum.ControlNotOwned));
                Assert.That(refused.Message.Text, Is.EqualTo("operator owns command authority"));
            });

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () =>
                {
                    _ = await RoboticsControlTools.SubmitIntentAsync(
                        new RobotIntentControllerClient(failedTransport),
                        "linearMove",
                        kLinearMoveJson,
                        0,
                        CancellationToken.None).ConfigureAwait(false);
                })!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
        }

        [Test]
        public void RoboticsToolsRegisterOnlyForRoboticsAndFullProfiles()
        {
            McpToolProfile[] profiles = Enum.GetValues<McpToolProfile>();

            Assert.That(
                profiles,
                Is.EquivalentTo(new[]
                {
                    McpToolProfile.Core,
                    McpToolProfile.Services,
                    McpToolProfile.Administration,
                    McpToolProfile.PubSub,
                    McpToolProfile.Diagnostics,
                    McpToolProfile.Robotics,
                    McpToolProfile.Full
                }));

            foreach (McpToolProfile profile in profiles)
            {
                var services = new ServiceCollection();
                services.AddMcpServer().WithOpcUaRoboticsTools(profile);

                HashSet<string> tools = ResolveToolNames(services);
                if (profile is McpToolProfile.Robotics or McpToolProfile.Full)
                {
                    Assert.That(tools, Does.Contain("robotics_submit_linear_move"), profile.ToString());
                    Assert.That(tools, Does.Contain("robotics_request_control"), profile.ToString());
                    Assert.That(tools, Does.Contain("robotics_wait_operation"), profile.ToString());
                }
                else
                {
                    Assert.That(tools, Is.Empty, profile.ToString());
                }
            }
        }

        [Test]
        public async Task BoundedWaitReturnsCurrentStateAndDisposesOperationPumpOnTimeout()
        {
            var operation = new NodeId("operation1", 2);
            var transport = new CountingRobotIntentTransport
            {
                OperationSnapshot = new IntentOperationSnapshot
                {
                    Operation = operation,
                    IntentId = "intent1",
                    ExecutionState = ExecutionStateEnum.Executing,
                    Progress = 0.4
                },
                KeepSubscriptionOpen = true
            };
            var controller = new RobotIntentControllerClient(transport);

            IntentOperationWaitResult result = await RoboticsMonitoringTools.WaitOperationAsync(
                controller,
                "intent1",
                operation.ToString(),
                1,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Completed, Is.False);
                Assert.That(result.Current.Operation.IsNull, Is.False);
                Assert.That(result.Current.Operation, Is.EqualTo(operation));
                Assert.That(result.Current.ExecutionState, Is.EqualTo(ExecutionStateEnum.Executing));
                Assert.That(result.Current.Progress, Is.EqualTo(0.4));
                Assert.That(transport.ReadOperationSnapshotCallCount, Is.GreaterThanOrEqualTo(2));
                Assert.That(transport.ActiveSubscriptionCount, Is.Zero);
                Assert.That(transport.SubscriptionDisposed, Is.True);
            });
        }

        private static IEnumerable<TestCaseData> Refusals()
        {
            yield return Refusal(IntentFailureEnum.ControlNotOwned, "command authority belongs to another session");
            yield return Refusal(IntentFailureEnum.NotPermittedInMode, "manual reduced-speed mode refuses motion");
            yield return Refusal(IntentFailureEnum.CapabilityNotSupported, "force intent is not supported");
            yield return Refusal(IntentFailureEnum.SafetyLimitExceeded, "safety speed limit exceeded");
            yield return Refusal(IntentFailureEnum.ParameterInvalid, "target frame is not in the controller namespace");
            yield return Refusal(IntentFailureEnum.QueueFull, "intent queue is full");
        }

        private static TestCaseData Refusal(IntentFailureEnum failure, string message)
        {
            return new TestCaseData(failure, message).SetName(
                FormattableString.Invariant($"{failure}RefusalSurvivesMcpTranslation"));
        }

        private static HashSet<string> ResolveToolNames(IServiceCollection services)
        {
            using ServiceProvider provider = services.BuildServiceProvider();
            return provider
                .GetServices<McpServerTool>()
                .Select(tool => tool.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);
        }

        private const string kLinearMoveJson =
            "{\"target\":{\"position\":[1,2,3],\"orientation\":[0,0,0,1]},\"speedFraction\":0.2}";

        private sealed class CountingRobotIntentTransport : IRobotIntentTransport
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

            public IntentSubmissionResult SubmissionResult { get; init; } = new() { Accepted = true };

            public Exception? SubmitException { get; init; }

            public IntentOperationSnapshot OperationSnapshot { get; init; } = new();

            public bool KeepSubscriptionOpen { get; init; }

            public int SubmitIntentCallCount { get; private set; }

            public int RequestControlCallCount { get; private set; }

            public int ReadOperationSnapshotCallCount { get; private set; }

            public int ActiveSubscriptionCount { get; private set; }

            public bool SubscriptionDisposed { get; private set; }

            public ValueTask<ArrayOf<RobotIntentNodeLookupEntry>> BrowseControllersAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult<ArrayOf<RobotIntentNodeLookupEntry>>(
                    [new RobotIntentNodeLookupEntry(ControllerId, new QualifiedName("controller1"), "controller1")]);
            }

            public ValueTask<RobotIntentControllerInfo> ReadControllerAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult(new RobotIntentControllerInfo());
            }

            public ValueTask<RobotIntentControllerState> ReadControllerStateAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult(new RobotIntentControllerState { ControllerId = ControllerId });
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
                if (SubmitException != null)
                {
                    throw SubmitException;
                }
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
                return ValueTask.FromResult(new MissionSubmissionResult { Accepted = true });
            }

            public ValueTask<MissionUpdateOutcome> UpdateMissionAsync(
                string missionId,
                uint missionUpdateId,
                ArrayOf<MissionStepDataType> steps,
                CancellationToken ct = default)
            {
                return ValueTask.FromResult(new MissionUpdateOutcome(MissionUpdateResultEnum.Accepted, LocalizedText.Null));
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
                RequestControlCallCount++;
                return ValueTask.FromResult(new CommandAuthorityOutcome(true, ControllerId));
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
                ReadOperationSnapshotCallCount++;
                return ValueTask.FromResult(OperationSnapshot);
            }

            public ValueTask<NodeId> ReadControlOwnerAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult(NodeId.Null);
            }

            public async IAsyncEnumerable<RobotIntentDataChange> SubscribeDataChangesAsync(
                ArrayOf<NodeId> nodeIds,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                ActiveSubscriptionCount++;
                try
                {
                    if (KeepSubscriptionOpen)
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    ActiveSubscriptionCount--;
                    SubscriptionDisposed = true;
                }
                yield break;
            }
        }
    }
}
#endif
