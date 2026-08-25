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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.Robotics.Server;
using Opc.Ua.RobotIntent;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Robotics.IntentEnabledRobot;
using Robotics.IntentEnabledRobot.Kinematics;
using Robotics.IntentEnabledRobot.Simulation;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Robotics.Intent.Tests
{
    /// <summary>
    /// Exercises the public Robotics MCP tool surface against the pallet payload sample.
    /// </summary>
    [TestFixture]
    [Category("RobotIntent")]
    [Category("Integration")]
    [Category("Mcp")]
    [NonParallelizable]
    public sealed class RobotIntentMcpPalletScenarioTests
    {
        [Test]
        public async Task PalletStackingScenarioUsesMcpToolsAndPublishesPayloadMotion()
        {
            await using IntentSampleFixture fixture = await IntentSampleFixture.StartAsync().ConfigureAwait(false);
            using OpcUaSessionManager sessionManager = CreateSessionManager();
            await ConnectWithRetryAsync(sessionManager, kAgentSession, fixture.ServerUrl).ConfigureAwait(false);
            await ConnectWithRetryAsync(sessionManager, kOperatorSession, fixture.ServerUrl).ConfigureAwait(false);
            var robotics = new RoboticsIntentManager(sessionManager);

            RobotIntentNodeLookupEntry controller = await DiscoverControllerAsync(robotics).ConfigureAwait(false);
            string controllerId = controller.NodeId.ToString();
            RobotIntentControllerInfo info = await RoboticsDiscoveryTools
                .ReadControllerAsync(robotics, controllerId, kAgentSession, CancellationToken.None)
                .ConfigureAwait(false);
            RobotIntentControllerState initialState = await RoboticsMonitoringTools
                .ReadStateAsync(robotics, controllerId, kAgentSession, CancellationToken.None)
                .ConfigureAwait(false);
            AssertAgentCanPlanStacking(info, initialState);

            CommandAuthorityOutcome operatorAuthority = await RoboticsControlTools
                .RequestControlAsync(robotics, controllerId, kOperatorSession, CancellationToken.None)
                .ConfigureAwait(false);
            IntentSubmissionResult refusedWithoutAuthority = await RoboticsControlTools
                .SubmitLinearMoveAsync(
                    robotics,
                    controllerId,
                    LinearMoveInput("refused-without-authority", 0.12, 0.16, 0.28),
                    kAgentSession,
                    CancellationToken.None)
                .ConfigureAwait(false);
            await RoboticsControlTools
                .ReleaseControlAsync(robotics, controllerId, kOperatorSession, CancellationToken.None)
                .ConfigureAwait(false);
            CommandAuthorityOutcome agentAuthority = await RoboticsControlTools
                .RequestControlAsync(robotics, controllerId, kAgentSession, CancellationToken.None)
                .ConfigureAwait(false);

            RobotIntentNodeLookupEntry tool = Find(info.Lookups.Tools, "ParallelGripper");
            RobotIntentNodeLookupEntry bin = Find(info.Lookups.Locations, "Bin");
            RobotIntentNodeLookupEntry fixtureLocation = Find(info.Lookups.Locations, "Fixture");
            RobotIntentNodeLookupEntry heldPartPosition = Find(info.Lookups.Outputs, "HeldPartPosition");
            RobotIntentNodeLookupEntry heldPartVisible = Find(info.Lookups.Outputs, "HeldPartVisible");
            RobotIntentNodeLookupEntry slot01 = Find(info.Lookups.Outputs, "PayloadSlot01Filled");
            RobotIntentNodeLookupEntry slot02 = Find(info.Lookups.Outputs, "PayloadSlot02Filled");

            IntentSubmissionResult pick = await RoboticsControlTools
                .SubmitPickAsync(
                    robotics,
                    controllerId,
                    PickInput("direct-pick-slot-01", bin.NodeId, tool.NodeId),
                    kAgentSession,
                    CancellationToken.None)
                .ConfigureAwait(false);
            IntentOperationWaitResult picked = await WaitForCompletionAsync(
                robotics,
                controllerId,
                pick,
                kAgentSession).ConfigureAwait(false);
            double[] heldBeforeMove = await ReadDoubleArrayOutputAsync(
                sessionManager.GetSessionOrThrow(kAgentSession),
                heldPartPosition.NodeId).ConfigureAwait(false);
            IntentSubmissionResult moveHeld = await RoboticsControlTools
                .SubmitJointMoveAsync(
                    robotics,
                    controllerId,
                    new JointMoveIntentInput
                    {
                        IntentId = "direct-stack-slot-01",
                        JointTargets = [0.1, -1.0, 1.5, -0.9, 0.75, 0.0],
                        BlockingMode = BlockingModeEnum.None
                    },
                    info.AxisCount,
                    kAgentSession,
                    CancellationToken.None)
                .ConfigureAwait(false);
            IntentOperationWaitResult movedHeld = await WaitForCompletionAsync(
                robotics,
                controllerId,
                moveHeld,
                kAgentSession).ConfigureAwait(false);
            double[] heldAfterMove = await ReadDoubleArrayOutputAsync(
                sessionManager.GetSessionOrThrow(kAgentSession),
                heldPartPosition.NodeId).ConfigureAwait(false);
            bool visibleWhileHeld = await ReadBooleanOutputAsync(
                sessionManager.GetSessionOrThrow(kAgentSession),
                heldPartVisible.NodeId).ConfigureAwait(false);
            IntentSubmissionResult place = await RoboticsControlTools
                .SubmitPlaceAsync(
                    robotics,
                    controllerId,
                    PlaceInput("direct-place-slot-01", fixtureLocation.NodeId, tool.NodeId),
                    kAgentSession,
                    CancellationToken.None)
                .ConfigureAwait(false);
            IntentOperationWaitResult placed = await WaitForCompletionAsync(
                robotics,
                controllerId,
                place,
                kAgentSession).ConfigureAwait(false);
            bool firstSlotFilled = await WaitForBooleanOutputAsync(
                sessionManager.GetSessionOrThrow(kAgentSession),
                slot01.NodeId).ConfigureAwait(false);

            IntentSubmissionResult pausable = await RoboticsControlTools
                .SubmitWaitAsync(
                    robotics,
                    controllerId,
                    new WaitIntentInput
                    {
                        IntentId = "pause-resume-cancel",
                        Duration = 1500,
                        BlockingMode = BlockingModeEnum.None
                    },
                    kAgentSession,
                    CancellationToken.None)
                .ConfigureAwait(false);
            await WaitUntilExecutingAsync(robotics, controllerId, pausable, kAgentSession).ConfigureAwait(false);
            IntentCommandOutcome pause = await RoboticsControlTools
                .PauseAsync(robotics, controllerId, kAgentSession, CancellationToken.None)
                .ConfigureAwait(false);
            OperationListResult operationsWhilePaused = await RoboticsMonitoringTools
                .ListOperationsAsync(
                    robotics,
                    controllerId,
                    query: null,
                    kAgentSession,
                    CancellationToken.None)
                .ConfigureAwait(false);
            IntentCommandOutcome resume = await RoboticsControlTools
                .ResumeAsync(robotics, controllerId, kAgentSession, CancellationToken.None)
                .ConfigureAwait(false);
            IntentCommandOutcome cancelIntent = await RoboticsControlTools
                .CancelIntentAsync(
                    robotics,
                    controllerId,
                    pausable.IntentId,
                    StopModeEnum.QuickStop,
                    kAgentSession,
                    CancellationToken.None)
                .ConfigureAwait(false);
            IntentOperationWaitResult cancelled = await WaitForCompletionAsync(
                robotics,
                controllerId,
                pausable,
                kAgentSession).ConfigureAwait(false);
            IntentSubmissionResult retryRefusal = await RoboticsControlTools
                .RetryAsync(
                    robotics,
                    controllerId,
                    refusedWithoutAuthority.IntentId,
                    kAgentSession,
                    CancellationToken.None)
                .ConfigureAwait(false);

            MissionSubmissionResult stackMission = await RoboticsMissionTools
                .SubmitMissionAsync(
                    robotics,
                    controllerId,
                    "stack-slot-02",
                    0,
                    StackingMissionSteps(bin.NodeId, fixtureLocation.NodeId, tool.NodeId),
                    null,
                    "stack pallet slot 02",
                    kAgentSession,
                    CancellationToken.None)
                .ConfigureAwait(false);
            bool secondSlotFilled = await WaitForBooleanOutputAsync(
                sessionManager.GetSessionOrThrow(kAgentSession),
                slot02.NodeId).ConfigureAwait(false);
            MissionSubmissionResult missionToCancel = await RoboticsMissionTools
                .SubmitMissionAsync(
                    robotics,
                    controllerId,
                    "cancelled-pallet-demo",
                    0,
                    [
                        WaitStep(
                            "wait",
                            "cancelled-pallet-demo-wait",
                            duration: 1000,
                            released: true)
                    ],
                    null,
                    "cancelled pallet demonstration",
                    kAgentSession,
                    CancellationToken.None)
                .ConfigureAwait(false);
            MissionListResult missionsBeforeCancel = await RoboticsMonitoringTools
                .ListMissionsAsync(
                    robotics,
                    controllerId,
                    query: null,
                    kAgentSession,
                    CancellationToken.None)
                .ConfigureAwait(false);
            MissionUpdateOutcome update = await RoboticsMissionTools
                .UpdateMissionAsync(
                    robotics,
                    controllerId,
                    "cancelled-pallet-demo",
                    1,
                    [
                        WaitStep(
                            "wait",
                            "cancelled-pallet-demo-wait",
                            duration: 1000,
                            released: true),
                        WaitStep(
                            "replacement",
                            "cancelled-pallet-demo-replacement",
                            duration: 100,
                            released: false)
                    ],
                    kAgentSession,
                    CancellationToken.None)
                .ConfigureAwait(false);
            IntentCommandOutcome cancelMission = await RoboticsMissionTools
                .CancelMissionAsync(
                    robotics,
                    controllerId,
                    "cancelled-pallet-demo",
                    StopModeEnum.QuickStop,
                    kAgentSession,
                    CancellationToken.None)
                .ConfigureAwait(false);
            uint cancelAll = await RoboticsControlTools
                .CancelAllAsync(robotics, controllerId, StopModeEnum.QuickStop, kAgentSession, CancellationToken.None)
                .ConfigureAwait(false);
            RobotIntentControllerState finalState = await RoboticsMonitoringTools
                .ReadStateAsync(robotics, controllerId, kAgentSession, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(operatorAuthority.Granted, Is.True);
                Assert.That(refusedWithoutAuthority.Accepted, Is.False);
                Assert.That(refusedWithoutAuthority.Failure, Is.EqualTo(IntentFailureEnum.ControlNotOwned));
                Assert.That(agentAuthority.Granted, Is.True);
                Assert.That(pick.Accepted, Is.True);
                Assert.That(picked.Result.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(moveHeld.Accepted, Is.True);
                Assert.That(movedHeld.Result.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(visibleWhileHeld, Is.True);
                Assert.That(Distance(heldBeforeMove, heldAfterMove), Is.GreaterThan(0.05));
                Assert.That(place.Accepted, Is.True);
                Assert.That(placed.Result.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(firstSlotFilled, Is.True);
                Assert.That(pause.Accepted, Is.True);
                Assert.That(operationsWhilePaused.Returned, Is.GreaterThan(0));
                Assert.That(resume.Accepted, Is.True);
                Assert.That(cancelIntent.Accepted, Is.True);
                Assert.That(cancelled.Result.State, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(retryRefusal.Accepted, Is.False);
                Assert.That(missionToCancel.Accepted, Is.True);
                Assert.That(missionsBeforeCancel.Total, Is.GreaterThan(0));
                Assert.That(update.Result, Is.EqualTo(MissionUpdateResultEnum.Accepted));
                Assert.That(cancelMission.Accepted, Is.True);
                Assert.That(cancelAll, Is.GreaterThanOrEqualTo(0));
                Assert.That(stackMission.Accepted, Is.True);
                Assert.That(secondSlotFilled, Is.True);
                Assert.That(finalState.Ready.Value, Is.True);
            });
        }

        private static void AssertAgentCanPlanStacking(
            RobotIntentControllerInfo info,
            RobotIntentControllerState state)
        {
            string[] supportedFacets = info.SupportedFacets.ToArray()!;
            uint[] supportedIntentTypes = info.SupportedIntents
                .ToArray()!
                .Where(static capability => capability.IntentType.TryGetValue(out uint _))
                .Select(static capability =>
                {
                    capability.IntentType.TryGetValue(out uint identifier);
                    return identifier;
                })
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(
                    info.Lookups.Locations.ToArray()!.Select(entry => entry.Name),
                    Does.Contain("Bin"));
                Assert.That(
                    info.Lookups.Locations.ToArray()!.Select(entry => entry.Name),
                    Does.Contain("Fixture"));
                Assert.That(
                    info.Lookups.Outputs.ToArray()!.Select(entry => entry.Name),
                    Does.Contain("HeldPartPosition"));
                Assert.That(
                    info.Lookups.Outputs.ToArray()!.Select(entry => entry.Name),
                    Does.Contain("PayloadSlot01Filled"));
                Assert.That(supportedFacets, Does.Contain("RI-Mission"));
                Assert.That(supportedFacets, Does.Contain("RI-Mission-Horizon"));
                Assert.That(supportedIntentTypes, Does.Contain(Opc.Ua.RobotIntent.DataTypes.LinearMoveIntentDataType));
                Assert.That(supportedIntentTypes, Does.Contain(Opc.Ua.RobotIntent.DataTypes.PickIntentDataType));
                Assert.That(supportedIntentTypes, Does.Contain(Opc.Ua.RobotIntent.DataTypes.PlaceIntentDataType));
                Assert.That(state.Ready.Value, Is.True);
            });
        }

        private static async ValueTask<RobotIntentNodeLookupEntry> DiscoverControllerAsync(
            RoboticsIntentManager robotics)
        {
            ArrayOf<RobotIntentNodeLookupEntry> controllers = await RoboticsDiscoveryTools
                .ListControllersAsync(robotics, kAgentSession, CancellationToken.None)
                .ConfigureAwait(false);
            RobotIntentNodeLookupEntry[] entries = controllers.ToArray()!;
            return entries.Single(c => c.Name == "UR5eIntentController");
        }

        private static ValueTask<IntentOperationWaitResult> WaitForCompletionAsync(
            RoboticsIntentManager robotics,
            string controllerId,
            IntentSubmissionResult submission,
            string sessionName)
        {
            Assert.That(submission.Operation.IsNull, Is.False, "Accepted submissions must return an operation.");
            return WaitForOperationAsync(
                robotics,
                controllerId,
                submission.IntentId,
                submission.Operation.ToString(),
                sessionName,
                static result => result.Completed && RobotIntentRules.IsTerminal(result.Result.State));
        }

        private static async ValueTask WaitUntilExecutingAsync(
            RoboticsIntentManager robotics,
            string controllerId,
            IntentSubmissionResult submission,
            string sessionName)
        {
            await WaitForOperationAsync(
                robotics,
                controllerId,
                submission.IntentId,
                submission.Operation.ToString(),
                sessionName,
                static result => result.Current.ExecutionState == ExecutionStateEnum.Executing).ConfigureAwait(false);
        }

        private static async ValueTask<IntentOperationWaitResult> WaitForOperationAsync(
            RoboticsIntentManager robotics,
            string controllerId,
            string intentId,
            string operation,
            string sessionName,
            Func<IntentOperationWaitResult, bool> predicate)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            IntentOperationWaitResult result;
            do
            {
                result = await RoboticsMonitoringTools
                    .WaitOperationAsync(
                        robotics,
                        controllerId,
                        intentId,
                        operation,
                        200,
                        sessionName,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (predicate(result))
                {
                    return result;
                }
            }
            while (DateTime.UtcNow < deadline);

            Assert.Fail($"Timed out waiting for operation {intentId}.");
            return result;
        }

        private static async ValueTask<bool> ReadBooleanOutputAsync(ClientSession session, NodeId output)
        {
            DataValue value = await ReadOutputValueAsync(session, output).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out bool result), Is.True);
            return result;
        }

        private static async ValueTask<bool> WaitForBooleanOutputAsync(ClientSession session, NodeId output)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (await ReadBooleanOutputAsync(session, output).ConfigureAwait(false))
                {
                    return true;
                }
                await Task.Delay(50).ConfigureAwait(false);
            }
            Assert.Fail("Timed out waiting for the output to become true.");
            return false;
        }

        private static async ValueTask<double[]> ReadDoubleArrayOutputAsync(ClientSession session, NodeId output)
        {
            DataValue value = await ReadOutputValueAsync(session, output).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out ArrayOf<double> result), Is.True);
            return result.ToArray()!;
        }

        private static async ValueTask<DataValue> ReadOutputValueAsync(ClientSession session, NodeId output)
        {
            NodeId valueNode = await BrowseChildByNameAsync(session, output, "Value").ConfigureAwait(false);
            Assert.That(valueNode.IsNull, Is.False, "Output Value must be browsable.");
            DataValue value = await session.ReadValueAsync(valueNode).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, "Output Value must be readable.");
            return value;
        }

        private static async ValueTask<NodeId> BrowseChildByNameAsync(
            ClientSession session,
            NodeId root,
            string browseName)
        {
            BrowseResponse response = await session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new()
                    {
                        NodeId = root,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            if (response.Results.Count == 0)
            {
                return NodeId.Null;
            }
            for (int ii = 0; ii < response.Results[0].References.Count; ii++)
            {
                ReferenceDescription reference = response.Results[0].References[ii];
                if (reference.BrowseName.Name == browseName)
                {
                    return ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }

        private static RobotIntentNodeLookupEntry Find(
            ArrayOf<RobotIntentNodeLookupEntry> entries,
            string name)
        {
            RobotIntentNodeLookupEntry[] snapshot = entries.ToArray()!;
            RobotIntentNodeLookupEntry? entry = snapshot.SingleOrDefault(e => e.Name == name);
            Assert.That(entry, Is.Not.Null, $"Expected lookup entry '{name}'.");
            return entry!;
        }

        private static LinearMoveIntentInput LinearMoveInput(
            string intentId,
            double x,
            double y,
            double z)
        {
            return new LinearMoveIntentInput
            {
                IntentId = intentId,
                Target = new PoseDto
                {
                    Position = new PosePositionDto { X = x, Y = y, Z = z },
                    Orientation = new QuaternionDto { W = 1.0 },
                    FrameId = "world"
                },
                Constraints = new MotionConstraintsDto { CartesianSpeed = 0.25 },
                BlockingMode = BlockingModeEnum.None
            };
        }

        private static PickIntentInput PickInput(string intentId, NodeId source, NodeId tool)
        {
            return new PickIntentInput
            {
                IntentId = intentId,
                Source = source.ToString(),
                Tool = tool.ToString(),
                BlockingMode = BlockingModeEnum.None
            };
        }

        private static PlaceIntentInput PlaceInput(
            string intentId,
            NodeId destination,
            NodeId tool)
        {
            return new PlaceIntentInput
            {
                IntentId = intentId,
                Destination = destination.ToString(),
                Tool = tool.ToString(),
                BlockingMode = BlockingModeEnum.None
            };
        }

        private static MissionStepInput[] StackingMissionSteps(
            NodeId bin,
            NodeId fixture,
            NodeId tool)
        {
            return
            [
                new MissionStepInput
                {
                    StepId = "pick-slot-02",
                    Released = true,
                    Intent = new MissionIntentInput
                    {
                        Kind = IntentKind.Pick,
                        Pick = PickInput("mission-pick-slot-02", bin, tool)
                    }
                },
                new MissionStepInput
                {
                    StepId = "place-slot-02",
                    Released = true,
                    Intent = new MissionIntentInput
                    {
                        Kind = IntentKind.Place,
                        Place = PlaceInput("mission-place-slot-02", fixture, tool)
                    }
                }
            ];
        }

        private static MissionStepInput WaitStep(
            string stepId,
            string intentId,
            double duration,
            bool released)
        {
            return new MissionStepInput
            {
                StepId = stepId,
                Released = released,
                Intent = new MissionIntentInput
                {
                    Kind = IntentKind.Wait,
                    Wait = new WaitIntentInput
                    {
                        IntentId = intentId,
                        Duration = duration,
                        BlockingMode = BlockingModeEnum.None
                    }
                }
            };
        }

        private static double Distance(double[] left, double[] right)
        {
            Assert.That(left, Has.Length.EqualTo(3));
            Assert.That(right, Has.Length.EqualTo(3));
            double dx = left[0] - right[0];
            double dy = left[1] - right[1];
            double dz = left[2] - right[2];
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private static OpcUaSessionManager CreateSessionManager()
        {
            ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            return new OpcUaSessionManager(
                NullLogger<OpcUaSessionManager>.Instance,
                services,
                new OpcUaClientOptions(),
                DefaultTelemetry.Create(static _ => { }));
        }

        private static async ValueTask ConnectWithRetryAsync(
            OpcUaSessionManager sessionManager,
            string sessionName,
            string serverUrl)
        {
            Exception? lastException = null;
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    await sessionManager.ConnectAsync(
                        sessionName,
                        serverUrl,
                        securityMode: null,
                        securityPolicy: null,
                        authType: "Anonymous",
                        username: null,
                        password: null,
                        autoAcceptCerts: true,
                        CancellationToken.None).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    await Task.Delay(100).ConfigureAwait(false);
                }
            }
            throw new InvalidOperationException("Timed out connecting to the sample server.", lastException);
        }

        private const string kAgentSession = "pallet-agent";
        private const string kOperatorSession = "pallet-operator";

        private sealed class IntentSampleFixture : IAsyncDisposable
        {
            private IntentSampleFixture(IHost host, string serverUrl)
            {
                m_host = host;
                ServerUrl = serverUrl;
            }

            public string ServerUrl { get; }

            public static async ValueTask<IntentSampleFixture> StartAsync()
            {
                int port = GetFreeTcpPort();
                string serverUrl = $"opc.tcp://localhost:{port}/IntentEnabledRobot";
                HostApplicationBuilder builder = Host.CreateApplicationBuilder();
                builder.Logging.ClearProviders();
                builder.Logging.SetMinimumLevel(LogLevel.Warning);
                var clock = new ScaledSimulatedArmClock(TimeSpan.FromMilliseconds(10));
                var executor = new SimulatedArmExecutor(new SimulatedArmKinematics(), clock);
                builder.Services.TryAddEnumerable(
                    ServiceDescriptor.Singleton<IRobotIntentModelProvider, OpenUsdIntentModelProvider>());
                builder.Services.AddSingleton(executor);
                builder.Services.AddSingleton<SampleSafetySource>();
                builder.Services.AddSingleton<IntentRobotCell>();
                builder.Services
                    .AddOpcUa()
                    .AddServer(options =>
                    {
                        options.ApplicationName = "IntentEnabledRobotMcpPalletTest";
                        options.ApplicationUri = "urn:localhost:OPCFoundation:IntentEnabledRobotMcpPalletTest";
                        options.ProductUri = "uri:opcfoundation.org:IntentEnabledRobotMcpPalletTest";
                        options.AutoAcceptUntrustedCertificates = true;
                        options.EndpointUrls.Add(serverUrl);
                        options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
                        {
                            TokenType = UserTokenType.Anonymous
                        });
                    })
                    .ConfigureRoles(options => options.Roles.Add(new RoleDefinitionOptions
                    {
                        Name = global::Opc.Ua.BrowseNames.WellKnownRole_Operator,
                        Identities =
                        {
                            new RoleIdentityMappingOptions
                            {
                                CriteriaType = IdentityCriteriaType.Anonymous
                            }
                        }
                    }))
                    .AddRobotIntent()
                    .AddRobotIntentExecutor("UR5eIntentController", executor)
                    .ConfigureRobotIntent(async (context, cancellationToken) =>
                        await context.GetRequiredService<IntentRobotCell>()
                            .ConfigureAsync(context, cancellationToken).ConfigureAwait(false));
                IHost host = builder.Build();
                await host.StartAsync().ConfigureAwait(false);
                return new IntentSampleFixture(host, serverUrl);
            }

            public async ValueTask DisposeAsync()
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await m_host.StopAsync(cts.Token).ConfigureAwait(false);
                m_host.Dispose();
            }

            private static int GetFreeTcpPort()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }

            private readonly IHost m_host;
        }

        private sealed class ScaledSimulatedArmClock : ISimulatedArmClock
        {
            public ScaledSimulatedArmClock(TimeSpan tickDelay)
            {
                m_tickDelay = tickDelay;
            }

            public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                TimeSpan scaled = delay <= TimeSpan.Zero
                    ? TimeSpan.Zero
                    : TimeSpan.FromMilliseconds(Math.Min(m_tickDelay.TotalMilliseconds, delay.TotalMilliseconds));
                await Task.Delay(scaled, cancellationToken).ConfigureAwait(false);
            }

            private readonly TimeSpan m_tickDelay;
        }
    }
}
#endif
