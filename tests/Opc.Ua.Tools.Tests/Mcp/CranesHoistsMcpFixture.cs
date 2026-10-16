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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.CranesHoists;
using Opc.Ua.CranesHoists.Server;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;
using ISubscription = Opc.Ua.Client.Subscriptions.ISubscription;
using ISubscriptionManager = Opc.Ua.Client.Subscriptions.ISubscriptionManager;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Owns real crane equipment simulation, opc.tcp sessions and short test-local certificate stores.
    /// </summary>
    internal sealed class CranesHoistsMcpFixture : IAsyncDisposable
    {
        /// <summary>
        /// Creates a stock Machinery owner with the released crane model, not a fake service endpoint.
        /// </summary>
        internal CranesHoistsMcpFixture()
        {
            m_server = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
                server.AddNodeManager(m_factory);
                return server;
            })
            {
                AutoAccept = true,
                SecurityNone = true
            };
            m_authorizer.Setup(authorizer => authorizer.AuthorizeAsync(
                It.IsAny<ISystemContext>(), It.IsAny<CraneLimitRequest>(), It.IsAny<CancellationToken>()))
                .Returns((ISystemContext context, CraneLimitRequest _, CancellationToken _) =>
                    new ValueTask<bool>(context is SessionSystemContext caller &&
                        caller.SessionId == Session.SessionId));
            m_limits.Setup(provider => provider.RequestAsync(
                It.IsAny<CraneLimitRequest>(), It.IsAny<CancellationToken>()))
                .Returns((CraneLimitRequest request, CancellationToken cancellationToken) => RefuseRequests
                    ? new ValueTask<StatusCode>(StatusCodes.BadOutOfService)
                    : Provider.RequestAsync(request, cancellationToken));
        }

        /// <summary>
        /// Gets the original deterministic acquisition timestamp.
        /// </summary>
        internal static DateTimeUtc SourceTime { get; } =
            new(new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc));

        /// <summary>
        /// Gets the authorized, real managed session with the existing V2 subscription engine.
        /// </summary>
        internal ISession Session { get; private set; } = null!;

        /// <summary>
        /// Gets a second real session which server policy does not authorize for limit writes.
        /// </summary>
        internal ISession UnprivilegedSession { get; private set; } = null!;

        /// <summary>
        /// Gets the full simulated crane.
        /// </summary>
        internal CraneSystemHandle Crane { get; private set; } = null!;

        /// <summary>
        /// Gets a distinct mandatory-only system.
        /// </summary>
        internal CraneSystemHandle Minimal { get; private set; } = null!;

        /// <summary>
        /// Gets the explicit acquired-data and request-acceptance simulator.
        /// </summary>
        internal SimulatedCraneProvider Provider { get; } = new(Samples("McpCrane", true), true);

        /// <summary>
        /// Gets or sets the equipment provider's refusal policy, independently from user authorization.
        /// </summary>
        internal bool RefuseRequests { get; set; }

        /// <summary>
        /// Gets the actual vendor subtype identifier assigned to the full system.
        /// </summary>
        internal NodeId VendorType { get; private set; }

        /// <summary>
        /// Gets the diagnostic event subtype registered on the real server.
        /// </summary>
        internal NodeId EventType { get; private set; }

        /// <summary>
        /// Gets the typed API's subscription manager, used to verify isolation and await actual item creation.
        /// </summary>
        internal ISubscriptionManager Subscriptions
        {
            get
            {
                return Session.TryGetSubscriptionManager(out ISubscriptionManager? manager)
                    ? manager
                    : throw new InvalidOperationException("The test session has no subscription manager.");
            }
        }

        /// <summary>
        /// Starts the real endpoint and composes full/minimal native instances into one existing manager.
        /// </summary>
        internal async Task StartAsync()
        {
            string configured = Environment.GetEnvironmentVariable("OPCUA_CRANE_MCP_TEST_PKI") ??
                Path.Combine(Environment.CurrentDirectory, ".artifacts", "crane-mcp-pki");
            m_pki = Path.GetFullPath(Path.Combine(configured, Guid.NewGuid().ToString("N")[..8]));
            await m_server.LoadConfigurationAsync(Path.Combine(m_pki, "s")).ConfigureAwait(false);
            await m_server.StartAsync().ConfigureAwait(false);
            MachineryNodeManager manager = m_factory.Manager!;
            var context = new CranesHoistsBuildContext(
                manager.CreateRoboticsBuildContext(new RoboticsServerOptions
                {
                    InstanceNamespaceUri = kInstanceNamespace
                }),
                manager.CreateMachineryBuildContext(new MachineryServerOptions
                {
                    Parts = MachineryParts.BuildingBlocks,
                    InstanceNamespaceUri = kInstanceNamespace
                }));
            Crane = await Define(context, "McpCrane", Provider, full: true).BuildAsync().ConfigureAwait(false);
            Minimal = await Define(context, "McpMinimal", m_minimalProvider, full: false).BuildAsync()
                .ConfigureAwait(false);
            VendorType = new NodeId("VendorCraneType", Crane.NodeId.NamespaceIndex);
            await manager.AddPredefinedNodeAsync(new BaseObjectTypeState
            {
                NodeId = VendorType,
                BrowseName = new QualifiedName("VendorCraneType", Crane.NodeId.NamespaceIndex),
                DisplayName = new LocalizedText("Vendor crane"),
                SuperTypeId = NodeId.Create(CranesHoists.ObjectTypes.CraneMotionDeviceSystemType,
                    CranesHoistsModel.NamespaceUri, manager.SystemContext.NamespaceUris),
                IsAbstract = false,
                IsPartOfTypeHierarchy = true
            }).ConfigureAwait(false);
            Crane.State.TypeDefinitionId = VendorType;
            EventType = new NodeId("CraneDiagnosticEventType", Crane.NodeId.NamespaceIndex);
            await manager.AddPredefinedNodeAsync(new BaseObjectTypeState
            {
                NodeId = EventType,
                BrowseName = new QualifiedName("CraneDiagnosticEventType", Crane.NodeId.NamespaceIndex),
                DisplayName = new LocalizedText("Simulated crane diagnostic"),
                SuperTypeId = Opc.Ua.ObjectTypeIds.BaseEventType,
                IsAbstract = false,
                IsPartOfTypeHierarchy = true
            }).ConfigureAwait(false);
            Session = await ConnectAsync("a").ConfigureAwait(false);
            UnprivilegedSession = await ConnectAsync("b").ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes a real standard event with a known identity, source time, text and severity.
        /// </summary>
        internal ValueTask PublishEventAsync(string message = "Simulated overload diagnostic")
        {
            BaseEventState notification = m_factory.Manager!.SystemContext.CreateInstanceOfBaseEventType();
            notification.TypeDefinitionId = EventType;
            notification.EventType!.Value = EventType;
            notification.EventId!.Value = ByteString.From(new byte[] { 7, 8, 9 });
            notification.SourceNode!.Value = Crane.NodeId;
            notification.SourceName!.Value = "McpCrane";
            notification.Time!.Value = SourceTime;
            notification.ReceiveTime!.Value = DateTimeUtc.Now;
            notification.Message!.Value = new LocalizedText("en", message);
            notification.Severity!.Value = 200;
            return Crane.PublishEventAsync(notification);
        }

        /// <summary>
        /// Waits on the actual monitored-item state, rather than a guessed sleep or model-presence check.
        /// </summary>
        internal async Task WaitForObservationAsync(ISubscription? unrelated = null)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!Subscriptions.Items.Any(subscription => subscription != unrelated &&
                subscription.MonitoredItems.Items.Any(item => item.Created)))
            {
                await Task.Delay(20, deadline.Token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            foreach (ISession session in m_sessions)
            {
                if (session.Connected)
                {
                    await session.CloseAsync(1000, true).ConfigureAwait(false);
                }
                if (session is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    session.Dispose();
                }
            }
            foreach (ClientFixture client in m_clients)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            if (Minimal != null)
            {
                await Minimal.RemoveAsync().ConfigureAwait(false);
            }
            if (Crane != null)
            {
                await Crane.RemoveAsync().ConfigureAwait(false);
            }
            await Provider.DisposeAsync().ConfigureAwait(false);
            await m_minimalProvider.DisposeAsync().ConfigureAwait(false);
            await m_server.StopAsync().ConfigureAwait(false);
            if (m_pki != null && Directory.Exists(m_pki))
            {
                Directory.Delete(m_pki, recursive: true);
            }
        }

        /// <summary>
        /// Builds a valid inherited topology through the public fluent API.
        /// </summary>
        private CraneSystemBuilder Define(
            CranesHoistsBuildContext context,
            string name,
            ICraneDataProvider provider,
            bool full)
        {
            CraneSystemBuilder builder = context.AddCraneSystem(name).WithProvider(provider)
                .WithIdentification(data => Identify(data, name))
                .AddController("Controller", controller =>
                {
                    controller.WithIdentification(data =>
                    {
                        data.Manufacturer = new LocalizedText("Simulated manufacturer");
                        data.Model = new LocalizedText("Controller");
                        data.ProductCode = "Controller-model";
                        data.SerialNumber = "Controller-1";
                    });
                    controller.WithCurrentUser(user => user.WithLevel("Observer").WithName("MCP test"));
                    controller.AddSoftware("Runtime", software => software.WithIdentification(data =>
                    {
                        data.Manufacturer = new LocalizedText("Simulated manufacturer");
                        data.Model = new LocalizedText("Runtime");
                        data.SoftwareRevision = "1.0";
                    }));
                    controller.AddTaskControl("Main");
                })
                .AddSafetyState("Safety")
                .AddDevice("Hoist", CraneMotionDeviceCategoryEnum.HOIST, device =>
                {
                    device.WithIdentification(data => Identify(data, name + "-hoist"));
                    if (full)
                    {
                        device.WithWorkingPeriod().WithComponents();
                    }
                    device.AddAxis("Lift", axis =>
                    {
                        if (full)
                        {
                            axis.WithPositionCapability(true, AxisMotionProfileEnumeration.LINEAR)
                                .WithAxisFunctions().WithMovingState().WithSpeedLimits()
                                .WithRatedSpeed(2.5, new EUInformation
                                {
                                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                                    UnitId = 5067859,
                                    DisplayName = new LocalizedText("m/s")
                                })
                                .WithActualLoad(static (load, systemContext) =>
                                    load.AddCenterOfMass(systemContext).AddInertia(systemContext))
                                .AddBackupBrake("BackupBrake", data => Identify(data, name + "-brake"));
                        }
                        axis.AddMotor("LiftMotor", motor =>
                        {
                            if (full)
                            {
                                motor.WithServiceBrake().WithOverheated()
                                    .WithIdentification(data => Identify(data, name + "-motor"));
                            }
                        });
                    });
                });
            if (full)
            {
                builder.WithExternalControl().WithGlobalSpeedLimit().WithEvents()
                    .WithFunctionalGroups().WithComponents()
                    .WithLimitRequests(m_limits.Object, m_authorizer.Object)
                    .AddProtectiveFunction("Overload", ["Lift"], protection => protection.WithStateMonitoring()
                        .WithDescription(ProtectiveFunctionEnumeration.FORCE_LIMITER, "Overload", true)
                        .WithDirections(true, true, false, true)
                        .WithIdentification(data => Identify(data, name + "-protection")));
            }
            return builder;
        }

        /// <summary>
        /// Creates a managed session through the existing client framework and subscription engine.
        /// </summary>
        private async Task<ISession> ConnectAsync(string name)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var client = new ClientFixture(telemetry);
            m_clients.Add(client);
            client.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            client.SessionFactory = new ManagedSessionFactory(telemetry)
                .WithSubscriptionEngine(DefaultSubscriptionEngineFactory.Instance);
            await client.LoadClientConfigurationAsync(Path.Combine(m_pki!, name), "CraneMcpTest" + name)
                .ConfigureAwait(false);
            ISession session = await client.ConnectAsync(
                new Uri(m_server.Config.ServerConfiguration!.BaseAddresses[0]), SecurityPolicies.None)
                .ConfigureAwait(false);
            m_sessions.Add(session);
            return session;
        }

        /// <summary>
        /// Supplies deterministic acquired values independently from requested settings.
        /// </summary>
        private static ArrayOf<CraneSample> Samples(string name, bool full)
        {
            var values = new List<CraneSample>
            {
                Sample(name, CraneSignal.SystemOn, Variant.From(true)),
                Sample(name, CraneSignal.OperationalMode, Variant.From((int)CraneOperationalModeEnumeration.MANUAL)),
                Sample("Hoist", CraneSignal.SpeedOverride, Variant.From(83.25)),
                Sample("LiftMotor", CraneSignal.MotorTemperature, Variant.From(35.5)),
                Sample("Safety", CraneSignal.EmergencyStop, Variant.From(false)),
                Sample("Safety", CraneSignal.ProtectiveStop, Variant.From(false)),
                Sample("Safety", CraneSignal.SafetyOperationalMode,
                    Variant.From((int)OperationalModeEnumeration.AUTOMATIC))
            };
            if (full)
            {
                values.AddRange(
                [
                    Sample(name, CraneSignal.ExternalControlRequest,
                        Variant.From((int)ExternalControlRequestEnumeration.REQUESTED_AND_CONTROL_ACTIVE)),
                    Sample("Hoist", CraneSignal.DesignedWorkingPeriod, Variant.From(92.5)),
                    Sample("Lift", CraneSignal.ActualPosition, Variant.From(12.75), StatusCodes.Uncertain),
                    Sample("Lift", CraneSignal.AxisFunction, Variant.From(ArrayOf.Wrapped<ushort>([0, 2, 4]))),
                    Sample("Lift", CraneSignal.IsMoving, Variant.From(true)),
                    Sample("Lift", CraneSignal.SpeedLimitDirMinus, Variant.From(27.5)),
                    Sample("Lift", CraneSignal.SpeedLimitDirPlus, Variant.From(31.5)),
                    Sample("Lift", CraneSignal.LoadMass, Variant.From(153.0)),
                    Sample("Lift", CraneSignal.LoadInertia,
                        Variant.FromStructure(new ThreeDVector { X = 1, Y = 2, Z = 3 })),
                    Sample("Lift", CraneSignal.LoadCenterOfMass, Variant.FromStructure(new ThreeDFrame
                    {
                        CartesianCoordinates = new ThreeDCartesianCoordinates { X = 4, Y = 5, Z = 6 },
                        Orientation = new ThreeDOrientation { A = 10, B = 20, C = 30 }
                    }), StatusCodes.Uncertain),
                    Sample("LiftMotor", CraneSignal.Overheated, Variant.From(false)),
                    Sample("LiftMotor", CraneSignal.BrakeReleased, Variant.From(true)),
                    Sample("BackupBrake", CraneSignal.BrakeReleased, Variant.From(false)),
                    Sample("Overload", CraneSignal.Enabled, Variant.From(true)),
                    Sample("Overload", CraneSignal.Active, Variant.From(false))
                ]);
            }
            return values.ToArrayOf();
        }

        /// <summary>
        /// Creates a source-timestamped sample with explicit wire type and quality.
        /// </summary>
        private static CraneSample Sample(
            string component, CraneSignal signal, Variant value, StatusCode status = default)
        {
            return new CraneSample(new CraneSignalAddress(component, signal), new DataValue(value, status, SourceTime));
        }

        /// <summary>
        /// Configures actual Machinery identification fields.
        /// </summary>
        private static void Identify(MachineryIdentificationData data, string serialNumber)
        {
            data.Manufacturer = new LocalizedText("Simulated manufacturer");
            data.SerialNumber = serialNumber;
            data.ProductInstanceUri = "urn:crane:mcp:simulation:" + serialNumber;
            data.Model = new LocalizedText("Simulated crane");
            data.ProductCode = "Simulation-only";
        }

        /// <summary>
        /// Uses the stock namespace owner with reversed namespace allocation to catch hard-coded indices.
        /// </summary>
        private sealed class Factory : IAsyncNodeManagerFactory
        {
            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris =>
                new CranesHoistsModelProvider().NamespaceUris.ToArray()!
                    .Append(kInstanceNamespace).Reverse().ToArrayOf();

            /// <summary>
            /// Gets the real manager created during server startup.
            /// </summary>
            internal MachineryNodeManager? Manager { get; private set; }

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Manager = new MachineryNodeManager(server, configuration, [new CranesHoistsModelProvider()],
                    new MachineryServerOptions
                    {
                        Parts = MachineryParts.BuildingBlocks,
                        InstanceNamespaceUri = kInstanceNamespace
                    });
                return new ValueTask<IAsyncNodeManager>(Manager);
            }
        }

        /// <summary>The test-owned application namespace, independent of standard namespace allocation.</summary>
        private const string kInstanceNamespace = "urn:crane:mcp:tests";

        /// <summary>The stock-manager factory owned by this fixture.</summary>
        private readonly Factory m_factory = new();

        /// <summary>The real server fixture.</summary>
        private readonly ServerFixture<StandardServer> m_server;

        /// <summary>The independent simulator for the mandatory-only crane.</summary>
        private readonly SimulatedCraneProvider m_minimalProvider = new(Samples("McpMinimal", false));

        /// <summary>The actual-caller authorization policy exercised through opc.tcp writes.</summary>
        private readonly Mock<ICraneWriteAuthorizer> m_authorizer = new(MockBehavior.Strict);

        /// <summary>The independently controlled equipment-acceptance policy.</summary>
        private readonly Mock<ICraneLimitProvider> m_limits = new(MockBehavior.Strict);

        /// <summary>The client framework instances owned by this fixture.</summary>
        private readonly List<ClientFixture> m_clients = [];

        /// <summary>The real sessions owned by this fixture, but borrowed by MCP.</summary>
        private readonly List<ISession> m_sessions = [];

        /// <summary>The short certificate directory created by this fixture.</summary>
        private string? m_pki;
    }
}
#endif
