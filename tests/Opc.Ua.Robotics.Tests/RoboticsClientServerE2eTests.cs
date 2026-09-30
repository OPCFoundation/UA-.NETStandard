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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Configuration;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Robotics.Client.Tests
{
    /// <summary>
    /// Drives a hosted OPC 40010-1 server built with the Robotics builders with the
    /// Robotics client over a real opc.tcp session. Unlike the facade tests against
    /// a scripted session, this decides whether the client finds what a server
    /// actually publishes: namespace indices, the DI ParameterSet, placeholders and
    /// the members of the task control state machine.
    /// </summary>
    [TestFixture]
    [Category("Robotics")]
    [NonParallelizable]
    public sealed class RoboticsClientServerE2eTests
    {
        private ITelemetryContext Telemetry { get; } = NUnitTelemetryContext.Create();

        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_serverUrl = "opc.tcp://localhost:" +
                GetFreeTcpPort().ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "/RoboticsE2e";
            var services = new ServiceCollection();
            services.AddLogging();
            services
                .AddOpcUa()
                .AddServer(ConfigureServer)
                .AddRobotics()
                .ConfigureRobotics(ConfigureCellAsync);
            m_provider = services.BuildServiceProvider();
            m_hostedServices = [.. m_provider.GetServices<IHostedService>()];
            foreach (IHostedService service in m_hostedServices)
            {
                await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
            }
            await WaitForCellAsync().ConfigureAwait(false);

            m_clientConfiguration = await CreateClientConfigurationAsync().ConfigureAwait(false);
            EndpointDescription endpoint = await WaitForEndpointAsync().ConfigureAwait(false);
            var sessionFactory = new DefaultSessionFactory(Telemetry)
            {
                SubscriptionEngineFactory = DefaultSubscriptionEngineFactory.Instance
            };
            m_session = await sessionFactory.CreateAsync(
                m_clientConfiguration,
                new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(m_clientConfiguration)),
                updateBeforeConnect: false,
                sessionName: "robotics-e2e",
                sessionTimeout: 60000,
                identity: new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales: default,
                ct: CancellationToken.None).ConfigureAwait(false);
            Assert.That(m_session.TryGetSubscriptionManager(out ISubscriptionManager? manager), Is.True);
            m_streaming = new StreamingSubscription(manager!);
            m_client = m_session.Robotics(Telemetry);
        }

        [OneTimeTearDown]
        public async Task StopAsync()
        {
            if (m_session != null)
            {
                if (m_session.Connected)
                {
                    await m_session.CloseAsync(1000, true).ConfigureAwait(false);
                }
                if (m_streaming != null)
                {
                    await m_streaming.DisposeAsync().ConfigureAwait(false);
                }
                m_session.Dispose();
            }
            if (m_hostedServices != null)
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                foreach (IHostedService service in m_hostedServices.AsEnumerable().Reverse())
                {
                    await service.StopAsync(stop.Token).ConfigureAwait(false);
                }
            }
            if (m_provider != null)
            {
                await m_provider.DisposeAsync().ConfigureAwait(false);
            }
            (m_clientConfiguration?.CertificateManager as IDisposable)?.Dispose();
        }

        [Test]
        public async Task ReadSystemReturnsTheValuesTheServerPublishesAsync()
        {
            RoboticsTopologySnapshot snapshot = await m_client.ReadSystemAsync(
                await SystemIdAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(snapshot.Controllers, Has.Count.EqualTo(1));
            Assert.That(snapshot.Axes, Has.Count.EqualTo(6));
            Assert.That(snapshot.PowerTrains, Has.Count.EqualTo(6));
            Assert.That(snapshot.SafetyStates, Has.Count.EqualTo(1));
            Assert.That(snapshot.TaskControls, Has.Count.EqualTo(1));
            Assert.That(snapshot.TaskModules, Has.Count.EqualTo(3));

            // Process values below DI:ParameterSet, with a good status.
            for (int ii = 1; ii < s_axes.Length; ii++)
            {
                AxisSnapshot axis = snapshot.Axes.ToArray()!
                    .Single(a => a.Identification.BrowseName.Name == s_axes[ii]);
                Assert.That(StatusCode.IsGood(axis.State.ActualPosition.StatusCode), Is.True, s_axes[ii]);
                Assert.That(axis.State.ActualPosition.WrappedValue.TryGetValue(out double position), Is.True,
                    s_axes[ii]);
                Assert.That(position, Is.EqualTo(s_home[ii]), s_axes[ii]);
                Assert.That(axis.MotionProfile, Is.EqualTo(AxisMotionProfileEnumeration.ROTARY));
            }

            // Identification with DI browse names, members with Robotics browse names.
            MotionDeviceSnapshot robot = snapshot.MotionDevices[0];
            Assert.That(robot.Identification.Manufacturer.Text, Is.EqualTo("Acme Robotics"));
            Assert.That(robot.Identification.SerialNumber, Is.EqualTo("AR6-0001"));
            Assert.That(robot.Identification.Model.Text, Is.EqualTo("AR-6"));
            Assert.That(robot.Category, Is.EqualTo(MotionDeviceCategoryEnumeration.ARTICULATED_ROBOT));
            Assert.That(robot.SpeedOverride.WrappedValue.TryGetValue(out double speed), Is.True);
            Assert.That(speed, Is.EqualTo(100.0));
            Assert.That(snapshot.Controllers[0].Identification.SerialNumber, Is.EqualTo("RC9-0001"));
            LoadSnapshot flange = snapshot.Loads.ToArray()!.Single(l => l.NodeId == robot.FlangeLoadId);
            Assert.That(flange.Mass.WrappedValue.TryGetValue(out double mass), Is.True);
            Assert.That(mass, Is.EqualTo(6.5));

            // Safety and task values below the ParameterSet, function and module names directly.
            SafetyStateSnapshot safety = snapshot.SafetyStates[0];
            Assert.That(safety.EmergencyStop.WrappedValue.TryGetValue(out bool estop), Is.True);
            Assert.That(estop, Is.False);
            Assert.That(safety.OperationalMode.WrappedValue.TryGetValue(out int mode), Is.True);
            Assert.That(mode, Is.EqualTo((int)OperationalModeEnumeration.AUTOMATIC));
            Assert.That(safety.ProtectiveStop.WrappedValue.TryGetValue(out bool protective), Is.True);
            Assert.That(protective, Is.False);
            Assert.That(safety.EmergencyStopFunctions, Has.Count.EqualTo(1));
            Assert.That(safety.EmergencyStopFunctions[0].Name, Is.EqualTo("Pendant"));
            Assert.That(safety.ProtectiveStopFunctions, Has.Count.EqualTo(1));
            Assert.That(safety.ProtectiveStopFunctions[0].Name, Is.EqualTo("Door"));
            TaskControlSnapshot task = snapshot.TaskControls[0];
            Assert.That(task.ExecutionMode.WrappedValue.TryGetValue(out int execution), Is.True);
            Assert.That(execution, Is.EqualTo((int)ExecutionModeEnumeration.CONTINUOUS));
            Assert.That(task.TaskProgramLoaded.WrappedValue.TryGetValue(out bool _), Is.True);
            Assert.That(task.TaskProgramName.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(snapshot.TaskModules.ToArray()!.Select(m => m.Name), Is.EquivalentTo(s_modules));
            Assert.That(snapshot.TaskModules.ToArray()!.Single(m => m.Name == "Main").Version, Is.EqualTo("1.4"));

            // Motors and gears are placeholders: one of each per power train.
            Assert.That(
                snapshot.PowerTrains.ToArray()!.All(p => p.MotorIds.Count == 1 && p.GearIds.Count == 1),
                Is.True);
            Assert.That(snapshot.Motors, Has.Count.EqualTo(6));
            Assert.That(snapshot.Gears, Has.Count.EqualTo(6));
            Assert.That(
                snapshot.Motors.ToArray()!.All(m => m.Identification.Manufacturer.Text == "Acme Robotics"),
                Is.True);
            Assert.That(snapshot.Drives, Has.Count.EqualTo(1), "one drive drives the A1 and A2 motors");
            Assert.That(snapshot.Drives[0].Identification.ProductCode, Is.EqualTo("DR-1"));

            // Relationships are browsed in both directions: each Moves reference is
            // listed from its power train and, inverse, from its axis.
            Assert.That(snapshot.Relationships.Moves.ToArray()!.Count(r => !r.IsInverse), Is.EqualTo(6));
            Assert.That(snapshot.Relationships.IsDrivenBy, Is.Not.Empty);
        }

        [Test]
        public async Task ObserveAxisDeliversChangingPositionsAsync()
        {
            NodeId axisId = await AxisIdAsync("A1").ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var pump = new CancellationTokenSource();
            Task writer = Task.Run(async () =>
            {
                double value = 10;
                while (!pump.IsCancellationRequested)
                {
                    value++;
                    Write(m_a1Position, value);
                    await Task.Delay(150, CancellationToken.None).ConfigureAwait(false);
                }
            });
            var positions = new List<double>();
            try
            {
                await foreach (AxisStateSnapshot state in m_client.ObserveAxisAsync(
                    axisId, m_streaming, timeout.Token).ConfigureAwait(false))
                {
                    Assert.That(StatusCode.IsGood(state.ActualPosition.StatusCode), Is.True);
                    if (state.ActualPosition.WrappedValue.TryGetValue(out double position) &&
                        (positions.Count == 0 || positions[positions.Count - 1] != position))
                    {
                        positions.Add(position);
                    }
                    if (positions.Count >= 3)
                    {
                        break;
                    }
                }
            }
            finally
            {
                pump.Cancel();
                await writer.ConfigureAwait(false);
            }

            Assert.That(positions, Has.Count.GreaterThanOrEqualTo(3));
            Assert.That(positions, Is.Ordered.Ascending);
        }

        [Test]
        public async Task ResetToProgramStartRunsTheServerMethodAsync()
        {
            TaskControlClient task = m_client.TaskControl(m_taskControlId);
            if (await task.ReadStateAsync().ConfigureAwait(false) == RoboticsOperationState.Ready)
            {
                await task.UnloadProgramAsync().ConfigureAwait(false);
            }
            await task.LoadByNameAsync("Main").ConfigureAwait(false);
            Assert.That(await task.ReadStateAsync().ConfigureAwait(false), Is.EqualTo(RoboticsOperationState.Ready));

            int resets = Volatile.Read(ref m_resets);
            await task.ResetToProgramStartAsync().ConfigureAwait(false);

            Assert.That(Volatile.Read(ref m_resets), Is.EqualTo(resets + 1), "the server's ResetToProgramStart ran");
            await task.UnloadProgramAsync().ConfigureAwait(false);
            Assert.That(await task.ReadStateAsync().ConfigureAwait(false), Is.EqualTo(RoboticsOperationState.Idle));
        }

        private async Task<NodeId> SystemIdAsync()
        {
            await foreach (MotionDeviceSystemEntry entry in m_client.EnumerateMotionDeviceSystemsAsync()
                .ConfigureAwait(false))
            {
                return entry.NodeId;
            }
            throw new AssertionException("The server publishes no MotionDeviceSystem.");
        }

        private async Task<NodeId> AxisIdAsync(string name)
        {
            RoboticsTopologySnapshot snapshot = await m_client.ReadSystemAsync(
                await SystemIdAsync().ConfigureAwait(false)).ConfigureAwait(false);
            return snapshot.Axes.ToArray()!
                .Single(a => a.Identification.BrowseName.Name == name).Identification.NodeId;
        }

        private async ValueTask ConfigureCellAsync(IRoboticsBuildContext context, CancellationToken cancellationToken)
        {
            try
            {
                m_serverContext = context.Context;
                ITaskControlBuilder? mainTask = null;
                await context.AddMotionDeviceSystemAsync(
                    "Cell",
                    system =>
                    {
                        system.WithComponentName("Cell");
                        IMotionDeviceBuilder robot = system.AddMotionDevice("Robot", BuildRobot);
                        ISafetyStateBuilder safety = system.AddSafetyState("Safety", state =>
                        {
                            state.WithComponentName("Cell safety")
                                .WithEmergencyStop(false)
                                .WithProtectiveStop(false)
                                .WithOperationalMode(OperationalModeEnumeration.AUTOMATIC);
                            state.AddEmergencyStop("PendantStop", "Pendant", stop => stop.WithActive(false));
                            state.AddProtectiveStop(
                                "DoorStop",
                                "Door",
                                stop => stop.WithEnabled(true).WithActive(false));
                        });
                        system.AddController("Controller", controller =>
                        {
                            controller.WithComponentName("Controller")
                                .WithIdentification(Identity("RC-9", "RC9", "RC9-0001"));
                            controller.AddSoftware("RobotOS", software => software
                                .WithIdentification(Identity("RobotOS", "ROS-7", "ROS7-0001")));
                            mainTask = controller.AddTaskControl("MainTask", task =>
                            {
                                task.WithComponentName("Main task")
                                    .WithExecutionMode(ExecutionModeEnumeration.CONTINUOUS)
                                    .WithTaskProgramLoaded(false)
                                    .WithTaskProgramName(string.Empty)
                                    .Controls(robot);
                                task.AddTaskModule("Main", m => m
                                    .WithName("Main")
                                    .WithVersion("1.4")
                                    .WithIsReferenced(true));
                                task.AddTaskModule("Gripper", m => m
                                    .WithName("Gripper")
                                    .WithVersion("2.0")
                                    .WithIsReferenced(true));
                                task.AddTaskModule("Diagnostics", m => m
                                    .WithName("Diagnostics")
                                    .WithVersion("1.1"));
                                task.AddTaskControlOperation(operation => operation
                                    .WithMotionDevicesUnderControl([robot.State.NodeId])
                                    .OnLoadByName((_, _) =>
                                        new ValueTask<RoboticsProgramResult>(new RoboticsProgramResult()))
                                    .OnUnloadProgram(_ =>
                                        new ValueTask<RoboticsProgramResult>(new RoboticsProgramResult()))
                                    .OnResetToProgramStart(_ =>
                                    {
                                        Interlocked.Increment(ref m_resets);
                                        return new ValueTask<RoboticsProgramResult>(new RoboticsProgramResult());
                                    }));
                            });
                            controller.Controls(robot).UsesSafetyState(safety);
                        });
                    },
                    cancellationToken).ConfigureAwait(false);
                m_taskControlId = mainTask!.State.NodeId;
                m_cellReady.TrySetResult(true);
            }
            catch (Exception exception)
            {
                m_cellReady.TrySetException(exception);
                throw;
            }
        }

        private void BuildRobot(IMotionDeviceBuilder robot)
        {
            EUInformation degree = Unit("DD", "°", "degree");
            robot.WithComponentName("AR-6")
                .WithIdentification(Identity("AR-6", "AR6", "AR6-0001"))
                .WithCategory(MotionDeviceCategoryEnumeration.ARTICULATED_ROBOT)
                .WithSpeedOverride(100.0)
                .WithFlangeLoad(load => load.WithMass(6.5, Unit("KGM", "kg", "kilogram")));
            IDriveBuilder drive = robot.AddDrive("DriveA1", d => d.WithProductCode("DR-1"));
            for (int ii = 0; ii < s_axes.Length; ii++)
            {
                int index = ii;
                string name = s_axes[ii];
                IAxisBuilder axis = robot.AddAxis(name, a => a
                    .WithMotionProfile(AxisMotionProfileEnumeration.ROTARY)
                    .WithActualPosition(s_home[index], degree, new Range(180, -180))
                    .WithActualSpeed(0)
                    .WithActualAcceleration(0)
                    .Configure((node, context) =>
                    {
                        if (index == 0)
                        {
                            m_a1Position = Child(node.ParameterSet!, context, BrowseNames.ActualPosition);
                        }
                    }));
                IPowerTrainBuilder powerTrain = robot.AddPowerTrain($"PT-{name}", pt =>
                {
                    IMotorBuilder motor = pt.AddMotor($"M-{name}", m => m
                        .WithIdentification(Identity("SM-80", "SM80", $"M-{name}"))
                        .WithMotorTemperature(38.5)
                        .WithBrakeReleased(true));
                    // One drive powers the first two motors; the snapshot lists it once.
                    if (index < 2)
                    {
                        motor.IsDrivenBy(drive);
                    }
                    pt.AddGear($"G-{name}", g => g
                        .WithIdentification(Identity("HD-40", "HD40", $"G-{name}"))
                        .WithGearRatio(160, 1));
                });
                powerTrain.Moves(axis);
                axis.Requires(powerTrain);
            }
        }

        private async Task WaitForCellAsync()
        {
            // The hosted service opens the server and runs the configurator in the
            // background; a failure there surfaces as a faulted execute task.
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (!m_cellReady.Task.IsCompleted && DateTime.UtcNow < deadline)
            {
                foreach (IHostedService service in m_hostedServices)
                {
                    if (service is BackgroundService { ExecuteTask: { IsFaulted: true } failed })
                    {
                        throw new InvalidOperationException("The hosted server failed to start.", failed.Exception);
                    }
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            Assert.That(m_cellReady.Task.IsCompleted, Is.True, "the Robotics configurator ran");
            await m_cellReady.Task.ConfigureAwait(false);
        }

        private async Task<EndpointDescription> WaitForEndpointAsync()
        {
            Exception? lastException = null;
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    EndpointDescription? endpoint = await CoreClientUtils.SelectEndpointAsync(
                        m_clientConfiguration,
                        m_serverUrl,
                        useSecurity: false,
                        Telemetry,
                        CancellationToken.None).ConfigureAwait(false);
                    if (endpoint != null)
                    {
                        return endpoint;
                    }
                }
                catch (ServiceResultException exception)
                {
                    lastException = exception;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            throw new TimeoutException($"'{m_serverUrl}' did not become available: {lastException?.Message}");
        }

        private void ConfigureServer(OpcUaServerOptions options)
        {
            string root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(RoboticsClientServerE2eTests),
                Guid.NewGuid().ToString("N"));
            options.ApplicationName = "RoboticsE2eServer";
            options.ApplicationUri = "urn:localhost:OPCFoundation:RoboticsE2eServer";
            options.ProductUri = "uri:opcfoundation.org:RoboticsE2eServer";
            options.PkiRoot = Path.Combine(root, "pki");
            options.AutoAcceptUntrustedCertificates = true;
            options.IncludeUnsecurePolicyNone = true;
            options.EndpointUrls.Clear();
            options.EndpointUrls.Add(m_serverUrl);
            options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.Anonymous });
        }

        private async ValueTask<ApplicationConfiguration> CreateClientConfigurationAsync()
        {
            string pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(RoboticsClientServerE2eTests),
                Guid.NewGuid().ToString("N"),
                "client-pki");
            var configuration = new ApplicationConfiguration(Telemetry)
            {
                ApplicationName = "RoboticsE2eClient",
                ApplicationUri = "urn:localhost:OPCFoundation:RoboticsE2eClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "own"),
                        SubjectName = "CN=RoboticsE2eClient, O=OPC Foundation"
                    },
                    TrustedIssuerCertificates = Store(Path.Combine(pkiRoot, "issuer")),
                    TrustedPeerCertificates = Store(Path.Combine(pkiRoot, "trusted")),
                    RejectedCertificateStore = Store(Path.Combine(pkiRoot, "rejected")),
                    AutoAcceptUntrustedCertificates = true
                },
                TransportQuotas = new TransportQuotas { MaxMessageSize = 4 * 1024 * 1024 },
                ClientConfiguration = new ClientConfiguration(),
                ServerConfiguration = new ServerConfiguration()
            };
            await configuration.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);
            var application = new ApplicationInstance(configuration, Telemetry);
            await application.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false);
            configuration.CertificateManager ??= CertificateManagerFactory.Create(
                configuration.SecurityConfiguration,
                Telemetry);
            configuration.CertificateManager.AcceptError = static (_, _) => true;
            return configuration;
        }

        private void Write(BaseDataVariableState node, double value)
        {
            node.WrappedValue = Variant.From(value);
            node.Timestamp = DateTime.UtcNow;
            node.ClearChangeMasks(m_serverContext, includeChildren: false);
        }

        private static Action<DeviceIdentificationData> Identity(string model, string productCode, string serial)
        {
            return data =>
            {
                data.Manufacturer = new LocalizedText("en", "Acme Robotics");
                data.Model = new LocalizedText("en", model);
                data.ProductCode = productCode;
                data.SerialNumber = serial;
            };
        }

        private static BaseDataVariableState Child(NodeState parent, ISystemContext context, string browseName)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);
            return children.OfType<BaseDataVariableState>().Single(c => c.BrowseName.Name == browseName);
        }

        private static EUInformation Unit(string commonCode, string symbol, string name)
        {
            int unitId = 0;
            foreach (char c in commonCode)
            {
                unitId = (unitId << 8) | c;
            }
            return new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = unitId,
                DisplayName = new LocalizedText("en", symbol),
                Description = new LocalizedText("en", name)
            };
        }

        private static CertificateTrustList Store(string path)
        {
            return new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = path };
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static readonly string[] s_axes = ["A1", "A2", "A3", "A4", "A5", "A6"];
        private static readonly double[] s_home = [0.0, -60.0, 75.0, 0.0, 45.0, 0.0];
        private static readonly string[] s_modules = ["Main", "Gripper", "Diagnostics"];

        private readonly TaskCompletionSource<bool> m_cellReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ServiceProvider? m_provider;
        private List<IHostedService> m_hostedServices = [];
        private ApplicationConfiguration m_clientConfiguration = null!;
        private ISession m_session = null!;
        private StreamingSubscription m_streaming = null!;
        private RoboticsClient m_client = null!;
        private ISystemContext m_serverContext = null!;
        private BaseDataVariableState m_a1Position = null!;
        private NodeId m_taskControlId;
        private int m_resets;
        private string m_serverUrl = string.Empty;
    }
}
