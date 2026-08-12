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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Configuration;
using Opc.Ua.RobotIntent;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Vision.Intent.Tests.Infrastructure
{
    /// <summary>
    /// The integration-test fixture. Stands up a real OPC UA server
    /// with the Vision node manager (and optionally the Robot Intent
    /// node manager) fully wired through the DI hosting pipeline,
    /// exactly the way an application would run it. The tests connect
    /// over TCP with a real session and drive the wire methods —
    /// nothing is stubbed at the transport level.
    /// </summary>
    internal sealed class VisionIntentServerFixture : IAsyncDisposable
    {
        /// <summary>
        /// Options controlling how the fixture is configured. Kept as
        /// a record so tests can build one and pass it in without
        /// touching the fixture internals.
        /// </summary>
        public sealed record Options
        {
            public bool OffServer { get; init; }

            public bool InlineClipsEnabled { get; init; } = true;

            public bool IncludeRobotIntent { get; init; }
        }

        public VisionIntentServerFixture(Options? options = null)
        {
            m_options = options ?? new Options();
            m_world = new TestBinWorld();
            m_groundTruth = new TestGroundTruthInferenceProvider(m_world);
            m_agent = new TestAgentInferenceProvider();
            m_media = new TestMediaProvider();
            m_cell = new TestVisionCell(
                m_groundTruth, m_agent, m_media,
                offServer: m_options.OffServer,
                inlineClipsEnabled: m_options.InlineClipsEnabled);
            m_executor = new TestBinPickingExecutor(m_world);
        }

        public string ServerUrl { get; private set; } = string.Empty;

        public TestBinWorld World => m_world;

        public TestGroundTruthInferenceProvider GroundTruth => m_groundTruth;

        public TestAgentInferenceProvider Agent => m_agent;

        public ITelemetryContext Telemetry => m_telemetry;

        internal TestVisionCell Cell => m_cell;

        public const string RobotControllerName = "TestRobot";

        public async ValueTask StartAsync()
        {
            int port = GetFreeTcpPort();
            ServerUrl = FormattableString.Invariant(
                $"opc.tcp://localhost:{port}/VisionIntentIntegration");
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            IOpcUaServerBuilder serverBuilder = builder.Services
                .AddOpcUa()
                .AddServer(options =>
                {
                    options.ApplicationName = "VisionIntentIntegrationServer";
                    options.ApplicationUri = "urn:localhost:OPCFoundation:VisionIntentIntegrationServer";
                    options.ProductUri = "uri:opcfoundation.org:VisionIntentIntegrationServer";
                    options.AutoAcceptUntrustedCertificates = true;
                    options.EndpointUrls.Add(ServerUrl);
                    options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
                    {
                        TokenType = UserTokenType.Anonymous
                    });
                })
                .ConfigureRoles(options => options.Roles.Add(new Opc.Ua.Server.RoleDefinitionOptions
                {
                    Name = "Operator",
                    Identities =
                    {
                        new Opc.Ua.Server.RoleIdentityMappingOptions
                        {
                            CriteriaType = IdentityCriteriaType.Anonymous
                        }
                    }
                }))
                .AddVision(options =>
                    options.InstanceNamespaceUri =
                        "urn:opcfoundation:vision-intent-tests:instances")
                .ConfigureVision((context, ct) => m_cell.ConfigureAsync(context, ct));

            if (m_options.IncludeRobotIntent)
            {
                builder.Services.AddSingleton<IIntentExecutor>(m_executor);
                serverBuilder
                    .AddRobotIntent(options =>
                        options.InstanceNamespaceUri =
                            "urn:opcfoundation:vision-intent-tests:robot-intent")
                    .ConfigureRobotIntent(ConfigureRobotIntentAsync);
            }

            m_host = builder.Build();
            await m_host.StartAsync().ConfigureAwait(false);
            if (m_configurationException != null)
            {
                throw new InvalidOperationException(
                    "Vision-intent test server configuration failed.",
                    m_configurationException);
            }
            m_clientConfig = await CreateClientConfigurationAsync().ConfigureAwait(false);
            await WaitForEndpointAsync().ConfigureAwait(false);
        }

        public async ValueTask<VisionIntentClientContext> ConnectAsync(string sessionName)
        {
            EndpointDescription? endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                m_clientConfig,
                ServerUrl,
                useSecurity: false,
                m_telemetry,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(endpointDescription, Is.Not.Null, "Endpoint must be discoverable.");
            var endpoint = new ConfiguredEndpoint(
                null,
                endpointDescription!,
                EndpointConfiguration.Create(m_clientConfig));
            var sessionFactory = new DefaultSessionFactory(m_telemetry)
            {
                SubscriptionEngineFactory = DefaultSubscriptionEngineFactory.Instance
            };
            ISession session = await sessionFactory.CreateAsync(
                m_clientConfig,
                endpoint,
                updateBeforeConnect: false,
                sessionName: sessionName,
                sessionTimeout: 60000,
                identity: new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales: default,
                ct: CancellationToken.None).ConfigureAwait(false);
            if (!session.TryGetSubscriptionManager(
                out Opc.Ua.Client.Subscriptions.ISubscriptionManager? manager))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The integration session did not expose the V2 subscription manager.");
            }
            var streaming = new StreamingSubscription(manager);
            return new VisionIntentClientContext(session, m_telemetry, streaming);
        }

        public async ValueTask DisposeAsync()
        {
            if (m_host != null)
            {
                using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    await m_host.StopAsync(stopCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        private async ValueTask ConfigureRobotIntentAsync(
            IRobotIntentBuildContext context, CancellationToken cancellationToken)
        {
            try
            {
                await context.AddIntentControllerAsync(
                    RobotControllerName,
                    controller =>
                    {
                        controller
                            .WithOperationalMode(OperationalModeEnum.AutomaticExternal)
                            .WithReady(true)
                            .WithMaxQueueDepth(4)
                            .WithSafetyState()
                            .Accepts<LinearMoveIntentDataType>(pauseSupported: true)
                            .Accepts<GraspIntentDataType>(pauseSupported: true);
                        IIntentFrameBuilder world = controller.AddFrame(
                            "World", "world", FrameRoleEnum.World, WorldPose(0, 0, 0));
                        IIntentFrameBuilder toolFrame = controller.AddFrame(
                            "ToolFrame", "tool", FrameRoleEnum.Tool, WorldPose(0, 0, 0),
                            frame => frame.WithParent(world));
                        controller.AddTool("Tool", toolFrame, fitted: true);
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_configurationException = ex;
                throw;
            }
        }

        private static Pose3DDataType WorldPose(double x, double y, double z)
        {
            return new Pose3DDataType
            {
                FrameId = "world",
                Position = [x, y, z],
                Orientation = [0.0, 0.0, 0.0, 1.0]
            };
        }

        private async ValueTask<ApplicationConfiguration> CreateClientConfigurationAsync()
        {
            string pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "pki",
                Guid.NewGuid().ToString("N"));
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "VisionIntentIntegrationClient",
                ApplicationUri = "urn:localhost:OPCFoundation:VisionIntentIntegrationClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "own"),
                        SubjectName = "CN=VisionIntentIntegrationClient, O=OPC Foundation"
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
            await config.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);
            var appInstance = new ApplicationInstance(config, m_telemetry);
            await appInstance.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false);
            config.CertificateManager ??= CertificateManagerFactory.Create(
                config.SecurityConfiguration,
                m_telemetry);
            config.CertificateManager.AcceptError = static (_, _) => true;
            return config;
        }

        private static CertificateTrustList Store(string path)
        {
            return new CertificateTrustList
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = path
            };
        }

        private async ValueTask WaitForEndpointAsync()
        {
            Exception? lastException = null;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    EndpointDescription? endpoint = await CoreClientUtils.SelectEndpointAsync(
                        m_clientConfig,
                        ServerUrl,
                        useSecurity: false,
                        m_telemetry,
                        CancellationToken.None).ConfigureAwait(false);
                    if (endpoint != null)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            throw new TimeoutException(
                $"Server endpoint did not become available at '{ServerUrl}'. " +
                $"Last error: {lastException?.Message}");
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private readonly Options m_options;
        private readonly TestBinWorld m_world;
        private readonly TestGroundTruthInferenceProvider m_groundTruth;
        private readonly TestAgentInferenceProvider m_agent;
        private readonly TestMediaProvider m_media;
        private readonly TestVisionCell m_cell;
        private readonly TestBinPickingExecutor m_executor;
        private readonly ITelemetryContext m_telemetry = DefaultTelemetry.Create(
            builder => builder.SetMinimumLevel(LogLevel.Warning));
        private Exception? m_configurationException;
        private IHost? m_host;
        private ApplicationConfiguration m_clientConfig = null!;
    }
}
