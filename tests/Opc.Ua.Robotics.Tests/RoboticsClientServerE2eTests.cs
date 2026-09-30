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

        private async ValueTask ConfigureCellAsync(IRoboticsBuildContext context, CancellationToken cancellationToken)
        {
            try
            {
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
                    .WithActualAcceleration(0));
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

        private readonly TaskCompletionSource<bool> m_cellReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ServiceProvider? m_provider;
        private List<IHostedService> m_hostedServices = [];
        private ApplicationConfiguration m_clientConfiguration = null!;
        private ISession m_session = null!;
        private StreamingSubscription m_streaming = null!;
        private RoboticsClient m_client = null!;
        private NodeId m_taskControlId;
        private int m_resets;
        private string m_serverUrl = string.Empty;
    }
}
