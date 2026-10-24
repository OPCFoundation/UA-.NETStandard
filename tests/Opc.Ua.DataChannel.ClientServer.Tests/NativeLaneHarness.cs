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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.Native;
using Opc.Ua.Bindings.WebTransport.Tests;
using Opc.Ua.Client;
using Opc.Ua.Native.QuicGo;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.DataChannels;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.DataChannels.ClientServer.Tests
{
    /// <summary>
    /// Runs actual native peers, production resource routing and a normal StandardServer.
    /// There is no ordinary TCP listener, local bridge or direct Service invocation.
    /// </summary>
    internal sealed class NativeLaneHarness : IAsyncDisposable
    {
        /// <summary>
        /// Builds native-independent server/client composition over fixture-owned certificate managers.
        /// </summary>
        private NativeLaneHarness(
            WebTransportTestEnvironment environment,
            DataChannelCarrierKind kind,
            MessageSecurityMode mode,
            DataChannelClientServerOptions options,
            bool routeLanes,
            bool sessionControlIntegration)
        {
            Environment = environment;
            Kind = kind;
            Mode = mode;
            Options = options;
            var sources = new DataChannelSourceRegistry();
            sources.Register(Source(DataChannelProfiles.ClientServerResource, Rechecks));
            sources.Register(Source(WebTransportTestEnvironment.ResourceUri, Rechecks));
            Provider = new ServerDataChannelProvider(
                sources, environment.CoreOptions, environment.ServerBudget, TimeProvider.System, environment.Telemetry);
            Listener = new DataChannelClientServerListener(
                kind, environment.Telemetry, options, Buffers, source: routeLanes ? Provider : null);
            ServiceEndpoint = new Uri($"{Listener.UriScheme}://127.0.0.1:4840/ua");
            ServerConfiguration = Configuration(true);
            ClientConfiguration = Configuration(false);
            ITransportBindingRegistry bindings;
            if (sessionControlIntegration)
            {
                Client = new DataChannelClientServerClient(environment.Telemetry, options, Buffers);
                var direct = new DefaultTransportBindingRegistry();
                direct.RegisterListenerFactory(new DataChannelClientServerListenerFactory(Listener));
                bindings = direct;
            }
            else
            {
                var services = new ServiceCollection();
                services.AddSingleton(environment.Telemetry);
                services.AddSingleton<IBufferManagerFactory>(Buffers);
                services.AddOpcUa().AddDataChannelClientServer(options).AddDataChannelClientServerListener(Listener);
                m_services = services.BuildServiceProvider();
                Client = m_services.GetRequiredService<IDataChannelClientServerClient>();
                bindings = m_services.GetRequiredService<ITransportBindingRegistry>();
            }
            Server = new LaneTestServer(
                environment.ServerManager, environment.Telemetry, CertificateAudits, Sends, Responses)
            {
                TransportBindings = bindings,
                DataChannelProvider = sessionControlIntegration ? Provider : null,
                EnableDataChannelModel = false
            };
        }

        /// <summary>
        /// Gets the real certificate-store/native fixture.
        /// </summary>
        internal WebTransportTestEnvironment Environment { get; }

        /// <summary>
        /// Gets the selected actual carrier.
        /// </summary>
        internal DataChannelCarrierKind Kind { get; }

        /// <summary>
        /// Gets the independently selected inner UASC mode.
        /// </summary>
        internal MessageSecurityMode Mode { get; }

        /// <summary>
        /// Gets the lane reservation limits.
        /// </summary>
        internal DataChannelClientServerOptions Options { get; }

        /// <summary>
        /// Gets the shared production resource router.
        /// </summary>
        internal ServerDataChannelProvider Provider { get; }

        /// <summary>
        /// Gets the virtual ordinary listener.
        /// </summary>
        internal DataChannelClientServerListener Listener { get; }

        /// <summary>
        /// Gets the explicitly enabled lane client.
        /// </summary>
        internal IDataChannelClientServerClient Client { get; }

        /// <summary>
        /// Gets the real Service server.
        /// </summary>
        internal StandardServer Server { get; }

        /// <summary>
        /// Gets the Service URL, independent of the native CONNECT endpoint.
        /// </summary>
        internal Uri ServiceEndpoint { get; }

        /// <summary>
        /// Gets the server application configuration.
        /// </summary>
        internal ApplicationConfiguration ServerConfiguration { get; }

        /// <summary>
        /// Gets the client application configuration.
        /// </summary>
        internal ApplicationConfiguration ClientConfiguration { get; }

        /// <summary>
        /// Gets actual UASC pool ownership observations.
        /// </summary>
        internal LaneBufferFactory Buffers { get; } = new();

        /// <summary>
        /// Gets the optional one-shot positive authorization pause.
        /// </summary>
        internal LaneRecheckPolicy Rechecks { get; } = new();

        /// <summary>
        /// Gets observations of the actual UASC send boundary.
        /// </summary>
        internal LaneSendObservation Sends { get; } = new();

        /// <summary>
        /// Gets the optional pause after real Service dispatch, before normal response production.
        /// </summary>
        internal LaneResponsePause Responses { get; } = new();

        /// <summary>
        /// Gets actual ordinary UASC certificate-audit callbacks, before transport error/reset races.
        /// </summary>
        internal ConcurrentQueue<CertificateAudit> CertificateAudits { get; } = new();

        /// <summary>
        /// Gets actual client context termination reasons for bounded failure diagnostics.
        /// </summary>
        internal ConcurrentQueue<DataChannelResult> ClientClosures { get; } = new();

        /// <summary>
        /// Gets actual server context termination reasons for bounded failure diagnostics.
        /// </summary>
        internal ConcurrentQueue<DataChannelResult> ServerClosures { get; } = new();

        /// <summary>
        /// Gets the original client WebTransport failure before it becomes a common context-closed outcome.
        /// </summary>
        internal Exception? ClientTransportFailure => m_webPair?.Client.Failure;

        /// <summary>
        /// Gets the original server WebTransport failure before it becomes a common context-closed outcome.
        /// </summary>
        internal Exception? ServerTransportFailure => m_webPair?.Server.Failure;

        /// <summary>
        /// Gets the initiating ready native context.
        /// </summary>
        internal DataChannelContext ClientContext { get; private set; } = null!;

        /// <summary>
        /// Gets the accepting ready native context.
        /// </summary>
        internal DataChannelContext ServerContext { get; private set; } = null!;

        /// <summary>
        /// Gets actual initiating native certificate/handshake evidence.
        /// </summary>
        internal NativeQuicConnection NativeClient { get; private set; } = null!;

        /// <summary>
        /// Gets actual accepting native certificate/handshake evidence.
        /// </summary>
        internal NativeQuicConnection NativeServer { get; private set; } = null!;

        /// <summary>
        /// Gets the deadline used by all fixture operations.
        /// </summary>
        internal CancellationToken Token => m_deadline.Token;

        /// <summary>
        /// Starts no lane automatically; only the native contexts and normal Server dispatcher are initialized.
        /// </summary>
        internal static async Task<NativeLaneHarness> CreateAsync(
            INativeQuicBackend backend,
            NativeHttp3Library http3,
            DataChannelCarrierKind kind,
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt,
            DataChannelClientServerOptions? options = null,
            bool routeLanes = true,
            bool sessionControlIntegration = true)
        {
            var core = new DataChannelOptions
            {
                MaxFlows = 8,
                MaxMessageSize = 32768,
                MaxReceiveBytes = 262144,
                MaxContextBytes = 1048576,
                MaxQueueMessages = 64,
                MaxContextMessages = 256,
                MaxStreamHistory = 128,
                OpenTimeout = TimeSpan.FromSeconds(5)
            };
            WebTransportTestEnvironment environment =
                await WebTransportTestEnvironment.CreateAsync(backend, http3, core).ConfigureAwait(false);
            var harness = new NativeLaneHarness(
                environment, kind, mode, options ?? new DataChannelClientServerOptions(),
                routeLanes, sessionControlIntegration);
            try
            {
                await harness.Server.StartAsync(harness.ServerConfiguration, harness.Token).ConfigureAwait(false);
                harness.m_serverStarted = true;
                await harness.ConnectNativeAsync().ConfigureAwait(false);
                harness.ClientContext.Closed += harness.ClientClosures.Enqueue;
                harness.ServerContext.Closed += harness.ServerClosures.Enqueue;
                return harness;
            }
            catch
            {
                await harness.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Explicitly opens the optional lane using real inner application certificates.
        /// </summary>
        internal async Task<DataChannelClientServerConnection> OpenLaneAsync(
            IDataChannelClientServerClient? client = null,
            string? expectedApplicationUri = null,
            CancellationToken cancellationToken = default)
        {
            using CertificateEntry own = Environment.ClientManager.AcquireApplicationCertificateBySecurityPolicy(
                SecurityPolicies.Basic256Sha256)!;
            using CertificateEntry peer = Environment.ServerManager.AcquireApplicationCertificateBySecurityPolicy(
                SecurityPolicies.Basic256Sha256)!;
            ServiceMessageContext context = ClientConfiguration.CreateMessageContext();
            var settings = new TransportChannelSettings
            {
                Description = Description(peer.Certificate),
                Configuration = EndpointConfiguration.Create(ClientConfiguration),
                ClientCertificate = own.Certificate,
                ServerCertificate = peer.Certificate,
                CertificateValidator = Environment.ClientManager,
                Factory = context.Factory,
                NamespaceUris = context.NamespaceUris
            };
            if (expectedApplicationUri != null)
            {
                settings.Description.Server.ApplicationUri = expectedApplicationUri;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(Token, cancellationToken);
            DataChannelClientServerConnection lane = await (client ?? Client).ConnectAsync(
                ClientContext, ServiceEndpoint, settings, linked.Token).ConfigureAwait(false);
            m_clientLanes.Add(lane);
            return lane;
        }

        /// <summary>
        /// Creates and activates an actual standard Client Session over the lane.
        /// </summary>
        internal async Task<ClientSession> OpenSessionAsync(DataChannelClientServerConnection lane)
        {
            using CertificateEntry own = Environment.ClientManager.AcquireApplicationCertificateBySecurityPolicy(
                SecurityPolicies.Basic256Sha256)!;
            var endpoint = new ConfiguredEndpoint(
                null, lane.Services.EndpointDescription, lane.Services.EndpointConfiguration);
            ClientSession session = new DefaultSessionFactory(Environment.Telemetry)
                .WithSubscriptionEngine(DefaultSubscriptionEngineFactory.Instance)
                .Create(lane.Services, ClientConfiguration, endpoint, own.Certificate.AddRef());
            var initialKeepAlive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            KeepAliveEventHandler? keepAlive = null;
            if (Options.MaxPendingRequests == 1)
            {
                session.KeepAliveInterval = 60000;
                keepAlive = (_, args) =>
                {
                    ServiceResult? status = args.Status;
                    if (status is null || ServiceResult.IsGood(status))
                    {
                        initialKeepAlive.TrySetResult(true);
                    }
                    else
                    {
                        initialKeepAlive.TrySetException(new ServiceResultException(status));
                    }
                };
                session.KeepAlive += keepAlive;
            }
            try
            {
                await session.OpenAsync(
                    "Native lane", 60000, new UserIdentity(), [], checkDomain: true, closeChannel: false, Token)
                    .ConfigureAwait(false);
                if (keepAlive != null)
                {
                    await initialKeepAlive.Task.WaitAsync(Token).ConfigureAwait(false);
                }
                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
            finally
            {
                if (keepAlive != null)
                {
                    session.KeepAlive -= keepAlive;
                }
            }
        }

        /// <summary>
        /// Opens an independently authorized resource through the same context and sole library router.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal async Task<(DataChannel Client, DataChannel Server)> OpenIndependentAsync(bool laneSized = false)
        {
            Task<DataChannel?> accepted = Provider.AcceptAsync(
                WebTransportTestEnvironment.ResourceUri, DataChannelProfiles.OpaquePayload, Kind, Token).AsTask();
            DataChannelParametersDataType parameters = WebTransportTestEnvironment.Parameters();
            if (laneSized)
            {
                parameters.MaxMessageSize = 32768;
                parameters.MaxReceiveBytes = 262144;
            }
            DataChannel flow = await ClientContext.OpenAsync(parameters, cancellationToken: Token).ConfigureAwait(false);
            return (flow, await accepted.ConfigureAwait(false) ?? throw new InvalidOperationException("Context closed."));
        }

        /// <summary>
        /// Describes only the ordinary inner Service endpoint and its independently selected security.
        /// </summary>
        internal EndpointDescription Description(Certificate certificate)
        {
            return new EndpointDescription
            {
                EndpointUrl = ServiceEndpoint.AbsoluteUri,
                TransportProfileUri = Profiles.UaTcpTransport,
                SecurityMode = Mode,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                ServerCertificate = new ByteString(certificate.RawData),
                Server = new ApplicationDescription
                {
                    ApplicationUri = WebTransportTestEnvironment.ServerUri,
                    ApplicationName = new LocalizedText("Native lane server"),
                    ApplicationType = ApplicationType.Server
                },
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "anonymous",
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]
            };
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            try
            {
                foreach (DataChannelClientServerConnection lane in m_clientLanes)
                {
                    await lane.DisposeAsync().ConfigureAwait(false);
                }
                if (m_serverStarted)
                {
                    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await Server.StopAsync(shutdown.Token).ConfigureAwait(false);
                }
                await Listener.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (m_webPair != null)
                    {
                        await m_webPair.DisposeAsync().ConfigureAwait(false);
                    }
                    if (m_quicListener != null)
                    {
                        await m_quicListener.DisposeAsync().ConfigureAwait(false);
                    }
                    await Task.WhenAll(
                        m_quicClient?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                        m_quicServer?.DisposeAsync().AsTask() ?? Task.CompletedTask).ConfigureAwait(false);
                }
                finally
                {
                    await Provider.DisposeAsync().ConfigureAwait(false);
                    Server.Dispose();
                    if (m_services != null)
                    {
                        await m_services.DisposeAsync().ConfigureAwait(false);
                    }
                    await Environment.DisposeAsync().ConfigureAwait(false);
                    m_deadline.Dispose();
                }
            }
        }

        /// <summary>
        /// Builds the exact resource policy on top of actual native peer identity validation.
        /// </summary>
        private static DataChannelSource Source(string resource, IDataChannelSourcePolicy policy)
        {
            return new DataChannelSource(resource, policy)
            {
                SupportedProtocols = [DataChannelProfiles.OpaquePayload],
                SupportedDirections = [DataChannelDirection.Duplex],
                SupportedDeliveryModes = [DataChannelDeliveryMode.Ordered],
                SupportedProtectionModes = [DataChannelProtectionMode.Confidential],
                ContentType = "application/octet-stream",
                MaxMessageSize = 32768,
                MaxReceiveBytes = 262144,
                MaxFlows = 8,
                AllowQueueExpiry = false
            };
        }

        /// <summary>
        /// Builds bounded ordinary application settings, borrowing the already initialized certificate manager.
        /// </summary>
        private ApplicationConfiguration Configuration(bool server)
        {
            return new ApplicationConfiguration(Environment.Telemetry)
            {
                ApplicationUri = server ? WebTransportTestEnvironment.ServerUri : WebTransportTestEnvironment.ClientUri,
                ApplicationName = server ? "Native lane server" : "Native lane client",
                ApplicationType = server ? ApplicationType.Server : ApplicationType.Client,
                ProductUri = "urn:opcfoundation:native-lane-tests",
                CertificateManager = server ? Environment.ServerManager : Environment.ClientManager,
                SecurityConfiguration = new SecurityConfiguration
                {
                    AutoAcceptUntrustedCertificates = false,
                    RejectSHA1SignedCertificates = true,
                    MinimumCertificateKeySize = 2048
                },
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = 10000,
                    MaxBufferSize = 32768,
                    MaxMessageSize = 1048576,
                    MaxArrayLength = 65536,
                    MaxByteStringLength = 1048576,
                    MaxStringLength = 65536,
                    ChannelLifetime = 60000,
                    SecurityTokenLifetime = 60000
                },
                ServerConfiguration = new ServerConfiguration
                {
                    BaseAddresses = [ServiceEndpoint.AbsoluteUri],
                    SecurityPolicies =
                    [
                        new ServerSecurityPolicy { SecurityMode = Mode, SecurityPolicyUri = SecurityPolicies.Basic256Sha256 }
                    ],
                    UserTokenPolicies =
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "anonymous",
                            TokenType = UserTokenType.Anonymous,
                            SecurityPolicyUri = SecurityPolicies.None
                        }
                    ],
                    MaxChannelCount = 16,
                    MaxSessionCount = 8,
                    MinPublishingInterval = 20,
                    MaxPublishingInterval = 10000,
                    DiagnosticsEnabled = true
                },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 }
            };
        }

        /// <summary>
        /// Establishes actual socket peers; the production provider remains the sole server accept consumer.
        /// </summary>
        private async Task ConnectNativeAsync()
        {
            IDataChannelContextFactory clientFactory = Environment.Factory(Environment.ClientBudget, reservedLane: true);
            if (Kind == DataChannelCarrierKind.WebTransport)
            {
                m_webPair = await Environment.ConnectPairAsync(
                    clientFactory: clientFactory, serverFactory: Provider).ConfigureAwait(false);
                ClientContext = m_webPair.Client.Context;
                ServerContext = m_webPair.Server.Context;
                NativeClient = m_webPair.Client.NativeConnection;
                NativeServer = m_webPair.Server.NativeConnection;
                return;
            }
            var server = new QuicDataChannelTransport(
                Environment.ServerBackend, Environment.ServerCredentials, Provider,
                applicationBudget: Environment.ServerBudget);
            var client = new QuicDataChannelTransport(
                Environment.ClientBackend, Environment.ClientCredentials, clientFactory,
                applicationBudget: Environment.ClientBudget);
            m_quicListener = await server.ListenAsync(new Uri("opc.quic://127.0.0.1:0"), Token).ConfigureAwait(false);
            Task<QuicDataChannelConnection> accepted = m_quicListener.AcceptAsync(Token).AsTask();
            try
            {
                m_quicClient = await client.ConnectAsync(m_quicListener.EndpointUrl, Token).ConfigureAwait(false);
                m_quicServer = await accepted.ConfigureAwait(false);
            }
            catch
            {
                await m_deadline.CancelAsync().ConfigureAwait(false);
                await m_quicListener.DisposeAsync().ConfigureAwait(false);
                try
                {
                    m_quicServer = await accepted.ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is
                    OperationCanceledException or ObjectDisposedException or
                    NativeQuicTransportException or DataChannelException)
                {
                    // Preserve the initial establishment failure after observing the other peer.
                }
                throw;
            }
            ClientContext = m_quicClient.Context;
            ServerContext = m_quicServer.Context;
            NativeClient = m_quicClient.NativeConnection;
            NativeServer = m_quicServer.NativeConnection;
        }

        /// <summary>
        /// Injects an initialized real certificate manager through the server's existing protected ownership seam.
        /// All application and Service validation remains the normal StandardServer implementation.
        /// </summary>
        private sealed class LaneTestServer : StandardServer
        {
            /// <summary>
            /// Borrows the same application identity used by actual native mTLS.
            /// </summary>
            internal LaneTestServer(
                CertificateManager certificates,
                ITelemetryContext telemetry,
                ConcurrentQueue<CertificateAudit> audits,
                LaneSendObservation sends,
                LaneResponsePause responses)
                : base(telemetry)
            {
                CertificateManager = certificates;
                m_audits = audits;
                m_sends = sends;
                m_responses = responses;
            }

            /// <inheritdoc/>
            public override void ReportAuditCertificateEvent(Certificate clientCertificate, Exception exception)
            {
                m_audits.Enqueue(new CertificateAudit(
                    DataChannelCertificateIdentity.GetKeyThumbprint(clientCertificate),
                    new ServiceResult(exception).StatusCode));
                base.ReportAuditCertificateEvent(clientCertificate, exception);
            }

            /// <inheritdoc/>
            public override async ValueTask<FindServersResponse> FindServersAsync(
                SecureChannelContext secureChannelContext,
                RequestHeader? requestHeader,
                string? endpointUrl,
                ArrayOf<string> localeIds,
                ArrayOf<string> serverUris,
                RequestLifetime requestLifetime)
            {
                await m_responses.WaitAsync(requestLifetime.CancellationToken).ConfigureAwait(false);
                return await base.FindServersAsync(
                    secureChannelContext, requestHeader, endpointUrl, localeIds, serverUris, requestLifetime)
                    .ConfigureAwait(false);
            }

            /// <inheritdoc/>
            protected override void ConfigureTransportListenerSettings(TransportListenerSettings settings, Uri endpointUri)
            {
                base.ConfigureTransportListenerSettings(settings, endpointUri);
                Func<IUaSCByteTransport, IUaSCByteTransport>? existing = settings.AcceptedTransportDecorator;
                settings.AcceptedTransportDecorator = transport => m_sends.Decorate(existing?.Invoke(transport) ?? transport);
            }

            private readonly ConcurrentQueue<CertificateAudit> m_audits;
            private readonly LaneSendObservation m_sends;
            private readonly LaneResponsePause m_responses;
        }

        private readonly CancellationTokenSource m_deadline = new(TimeSpan.FromSeconds(45));
        private readonly ServiceProvider? m_services;
        private readonly List<DataChannelClientServerConnection> m_clientLanes = [];
        private WebTransportPair? m_webPair;
        private QuicDataChannelListener? m_quicListener;
        private QuicDataChannelConnection? m_quicClient;
        private QuicDataChannelConnection? m_quicServer;
        private bool m_serverStarted;
        private int m_disposed;
    }

    /// <summary>
    /// Captures public key identity and failure status, never private material.
    /// </summary>
    internal readonly record struct CertificateAudit(ByteString Key, StatusCode Status);
}
