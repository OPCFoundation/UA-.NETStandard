/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.Native;
using Opc.Ua.Client;
using Opc.Ua.Client.DataChannels;
using Opc.Ua.Configuration;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.DataChannels;
using Opc.Ua.Server.Hosting;
using ClientSession = Opc.Ua.Client.ISession;
using ServerSession = Opc.Ua.Server.ISession;

namespace ConsoleDataChannelStreaming
{
    /// <summary>
    /// Composes real authenticated carrier factories and the common complete-message facade.
    /// It contains application policy and ownership, not negotiation, framing or channel registries.
    /// </summary>
    internal sealed class StreamingHarness : IAsyncDisposable
    {
        /// <summary>
        /// Creates application-scoped budgets shared by all each application's contexts and carriers.
        /// </summary>
        private StreamingHarness(SampleOptions options, ITelemetryContext telemetry)
        {
            m_options = options;
            m_telemetry = telemetry;
        }

        /// <summary>
        /// Gets the ready sender, independently of the transport initiator and flow opener.
        /// </summary>
        public DataChannel Source => m_source ?? throw new InvalidOperationException("The flow is not ready.");

        /// <summary>
        /// Gets the ready receiver.
        /// </summary>
        public DataChannel Sink => m_sink ?? throw new InvalidOperationException("The flow is not ready.");

        /// <summary>
        /// Gets the actual listener port for process-level cleanup verification.
        /// </summary>
        public int Port { get; private set; }

        /// <summary>
        /// Gets the actual authenticated context identity shared by both peers.
        /// </summary>
        public Guid ContextId { get; private set; }

        /// <summary>
        /// Gets the actual UASC identifier, or zero for native-only transports.
        /// </summary>
        public uint SecureChannelId { get; private set; }

        /// <summary>
        /// Gets the ordinary Service connection's actual authenticated UASC identifier.
        /// </summary>
        public uint ServiceSecureChannelId { get; private set; }

        /// <summary>
        /// Gets whether ordinary Services and flow share the identical transport object.
        /// </summary>
        public bool SameServiceTransport { get; private set; }

        /// <summary>
        /// Gets whether the optional generated model was actually installed.
        /// </summary>
        public bool ModelEnabled { get; private set; }

        /// <summary>
        /// Gets the number of Sessions the actual server created.
        /// </summary>
        public int Sessions => m_sessions.Count;

        /// <summary>
        /// Gets actual ordinary Read/Publish notification observations.
        /// </summary>
        public ServiceTraffic? Services { get; private set; }

        /// <summary>
        /// Gets whether all application and native ownership counters returned to zero.
        /// </summary>
        public bool ResourcesReleased { get; private set; }

        /// <summary>
        /// Establishes a pair over the exact selected profile, then completes OPEN/ACCEPT/READY.
        /// </summary>
        public static async Task<StreamingHarness> CreateAsync(
            SampleOptions options,
            ITelemetryContext telemetry,
            string resource,
            string contentType,
            CancellationToken cancellationToken)
        {
            var harness = new StreamingHarness(options, telemetry);
            try
            {
                await harness.InitializeAsync(resource, contentType, cancellationToken).ConfigureAwait(false);
                return harness;
            }
            catch
            {
                await harness.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Commits one live revision using the opener, even when the listening application is the sender.
        /// </summary>
        public async Task ReviseAsync(CancellationToken cancellationToken)
        {
            DataChannel opener = m_opener ?? throw new InvalidOperationException("No flow opener.");
            DataChannelParametersDataType parameters = opener.Parameters;
            parameters.Priority = 2;
            if (m_control != null)
            {
                DataChannelStatusDataType status = await m_control.ModifyAsync(opener, 1, parameters, cancellationToken)
                    .ConfigureAwait(false);
                if (status.Revision != 2)
                {
                    throw new InvalidOperationException("The grant Call did not complete the data-path revision.");
                }
            }
            else
            {
                RequireGood(await opener.UpdateAsync(parameters, 1, cancellationToken).ConfigureAwait(false));
            }
            if (Source.Revision != 2 || Sink.Revision != 2 ||
                Source.Parameters.Priority != 2 || Sink.Parameters.Priority != 2)
            {
                throw new InvalidOperationException("Both peers must observe the same committed revision.");
            }
        }

        /// <summary>
        /// Reads genuine server counters rather than assuming a successful client API call reached the wire.
        /// </summary>
        public (int Reads, int Publishes, int Calls) ServiceCounters()
        {
            int reads = 0;
            int publishes = 0;
            int calls = 0;
            foreach (ServerSession session in m_sessions)
            {
                reads += session.ReadDiagnostics(data => checked((int)data.ReadCount.TotalCount));
                publishes += session.ReadDiagnostics(data => checked((int)data.PublishCount.TotalCount));
                calls += session.ReadDiagnostics(data => checked((int)data.CallCount.TotalCount));
            }
            return (reads, publishes, calls);
        }

        /// <summary>
        /// Refuses non-successful complete-message operations without discarding their generated result.
        /// </summary>
        public static void RequireGood(DataChannelResult result)
        {
            if (result != DataChannelResult.Good)
            {
                throw new DataChannelException(result);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            try
            {
                await ReleaseNextAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (m_authorizer != null)
                    {
                        await m_authorizer.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (m_provider != null)
                        {
                            await m_provider.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        try
                        {
                            m_clientCredentials?.Dispose();
                            m_serverCredentials?.Dispose();
                        }
                        finally
                        {
                            try
                            {
                                if (m_identities != null)
                                {
                                    await m_identities.DisposeAsync().ConfigureAwait(false);
                                }
                            }
                            finally
                            {
                                m_server?.Dispose();
                                m_lifetime.Cancel();
                                m_lifetime.Dispose();
                            }
                        }
                    }
                }
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (m_nativeLoaded && m_native != null)
            {
                while (await m_native.GetCountsAsync(deadline.Token).ConfigureAwait(false) != default)
                {
                    await Task.Delay(20, deadline.Token).ConfigureAwait(false);
                }
            }
            ResourcesReleased = m_serverBudget.Usage == default && m_clientBudget.Usage == default;
            Console.WriteLine($"CLEANUP port={Port} resourcesReleased={ResourcesReleased}");
            if (!ResourcesReleased)
            {
                throw new InvalidOperationException("Application-owned flow resources remain after shutdown.");
            }
        }

        /// <summary>
        /// Creates exact source policies independently from optional Server/Session installation.
        /// </summary>
        private async Task InitializeAsync(string resource, string contentType, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime.Token);
            CancellationToken token = linked.Token;
            int tcpPort = FreeTcpPort();
            int controlPort;
            do
            {
                controlPort = FreeTcpPort();
            }
            while (controlPort == tcpPort);
            string flowAddress = $"opc.tcp://127.0.0.1:{tcpPort}" +
                (m_options.Shared ? DataChannelProfiles.SharedResource : DataChannelProfiles.DedicatedResource);
            string controlAddress = $"opc.tcp://127.0.0.1:{controlPort}/control";
            bool uasc = m_options.Carrier is DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc;
            ArrayOf<string> addresses = uasc
                ? m_options.Shared || m_options.SessionAuthorization ? [flowAddress, controlAddress] : [flowAddress]
                : [controlAddress];
            m_identities = await SampleIdentityPair.CreateAsync(
                m_options.StateDirectory, addresses, m_telemetry, token).ConfigureAwait(false);
            SampleIdentityPair identities = m_identities;
            ICertificateManager serverManager = SampleIdentityPair.Manager(identities.Server);
            ICertificateManager clientManager = SampleIdentityPair.Manager(identities.Client);
            using CertificateEntry serverIdentity = SampleIdentityPair.OwnCertificate(serverManager);
            using CertificateEntry clientIdentity = SampleIdentityPair.OwnCertificate(clientManager);
            using var serverPublic = new Certificate(serverIdentity.Certificate.RawData);
            using var clientPublic = new Certificate(clientIdentity.Certificate.RawData);
            var parameters = new DataChannelParametersDataType
            {
                ResourceUri = resource,
                Protocol = DataChannelProfiles.OpaquePayload,
                ContentType = contentType,
                Direction = m_options.ClientPublishes
                    ? DataChannelDirection.OpenerToAcceptor : DataChannelDirection.AcceptorToOpener,
                Delivery = DataChannelDeliveryMode.Ordered,
                Protection = DataChannelProtectionMode.Confidential,
                MaxMessageSize = checked((uint)m_options.Size),
                MaxReceiveBytes = checked((uint)Math.Max(65536, (m_options.Size + 17) * 16)),
                Priority = 1
            };
            var serverPolicy = new SamplePolicy(m_options, parameters, SampleIdentityPair.ClientApplicationUri,
                DataChannelCertificateIdentity.GetKeyThumbprint(clientPublic));
            var clientPolicy = new SamplePolicy(m_options, parameters, SampleIdentityPair.ServerApplicationUri,
                DataChannelCertificateIdentity.GetKeyThumbprint(serverPublic));
            var registry = new DataChannelSourceRegistry();
            var sourceId = new ExpandedNodeId("OpaqueSource", SampleIdentityPair.ModelNamespace);
            registry.Register(new DataChannelSource(resource, serverPolicy)
            {
                SourceNodeId = m_options.SessionAuthorization ? sourceId : default,
                SupportedProtocols = [parameters.Protocol],
                SupportedDirections = [parameters.Direction],
                SupportedDeliveryModes = [parameters.Delivery],
                SupportedProtectionModes = [parameters.Protection],
                ContentType = contentType,
                MaxMessageSize = parameters.MaxMessageSize,
                MaxReceiveBytes = parameters.MaxReceiveBytes,
                MaxFlows = 1
            });
            var options = new DataChannelOptions { MaxFlows = 2, MaxMessageSize = 32768 };
            m_provider = new ServerDataChannelProvider(
                registry, options, m_serverBudget, TimeProvider.System, m_telemetry);
            ServerDataChannelProvider provider = m_provider;
            m_authorizer = new SessionDataChannelAuthorizer(clientPolicy, TimeProvider.System, m_telemetry);
            SessionDataChannelAuthorizer authorizer = m_authorizer;
            var clientContexts = new DataChannelContextFactory(
                authorizer, options, m_clientBudget, TimeProvider.System, m_telemetry);
            if (uasc || m_options.SessionAuthorization)
            {
                m_server = new StandardServer(m_telemetry);
                StandardServer server = m_server;
                server.UseDataChannels(provider, enableModel: m_options.SessionAuthorization);
                await identities.Server.StartAsync(server, token).ConfigureAwait(false);
                m_server = null;
                server.CurrentInstance.IdentityRegistry.Register(new X509Authenticator(serverManager));
                void Created(ServerSession session, SessionEventReason reason) => m_sessions.Enqueue(session);
                server.CurrentInstance.SessionManager.SessionCreated += Created;
                m_cleanup.Push(async () =>
                {
                    server.CurrentInstance.SessionManager.SessionCreated -= Created;
                    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await identities.Server.StopAsync(shutdown.Token).ConfigureAwait(false);
                });
                ModelEnabled = await server.CurrentInstance.NodeManager.FindNodeInAddressSpaceAsync(
                    ObjectIds.DataChannelManager, token).ConfigureAwait(false) is DataChannelManagerState;
                if (ModelEnabled != m_options.SessionAuthorization)
                {
                    throw new InvalidOperationException("Optional model installation does not match the selection.");
                }
            }
            DataChannelContext clientContext;
            DataChannelContext serverContext;
            if (uasc)
            {
                Port = tcpPort;
                (clientContext, serverContext) = await ConnectUascAsync(
                    identities, clientIdentity, serverPublic, clientContexts, provider, flowAddress, token)
                    .ConfigureAwait(false);
            }
            else
            {
                (clientContext, serverContext) = await ConnectNativeAsync(
                    serverManager, clientManager, serverPublic, clientPublic, provider, clientContexts,
                    serverPolicy, token).ConfigureAwait(false);
            }
            ContextId = clientContext.Identity.ContextId;
            if (!clientContext.IsReady || !serverContext.IsReady ||
                ContextId == Guid.Empty || ContextId != serverContext.Identity.ContextId ||
                !clientContext.Identity.Peer.IsTransportInitiator || serverContext.Identity.Peer.IsTransportInitiator)
            {
                throw new InvalidOperationException("The actual context pair is not authenticated and HELLO-ready.");
            }
            Console.WriteLine($"LISTENING carrier={m_options.Carrier} port={Port} context={ContextId}");
            if (m_options.Shared || m_options.SessionAuthorization)
            {
                ClientSession session = await OpenSessionAsync(
                    identities, clientIdentity, serverPublic, flowAddress, controlAddress, token)
                    .ConfigureAwait(false);
                m_session = session;
                m_cleanup.Push(async () =>
                {
                    try
                    {
                        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        StatusCode closed = await session.CloseAsync(10000, false, shutdown.Token)
                            .ConfigureAwait(false);
                        if (StatusCode.IsBad(closed))
                        {
                            throw new ServiceResultException(closed);
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (m_uasc != null)
                            {
                                await m_uasc.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            await session.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                });
                ServiceSecureChannelId = (session.TransportChannel as ISecureChannel)?.CurrentToken?.ChannelId ?? 0;
                SameServiceTransport = ReferenceEquals(session.TransportChannel, m_uasc?.Services);
                if (m_options.Shared &&
                    (!SameServiceTransport || SecureChannelId == 0 || ServiceSecureChannelId != SecureChannelId ||
                    session.ConfiguredEndpoint.Description.TransportProfileUri != DataChannelProfiles.SharedUasc))
                {
                    throw new InvalidOperationException("Ordinary traffic did not select the flow's SecureChannel.");
                }
                if (m_options.SessionAuthorization)
                {
                    m_control = new DataChannelSessionControl(session, authorizer, TimeProvider.System);
                }
            }
            m_opener = m_control == null
                ? await clientContext.OpenAsync(parameters, cancellationToken: token).ConfigureAwait(false)
                : await m_control.OpenAsync(clientContext,
                    ExpandedNodeId.ToNodeId(sourceId, m_session!.NamespaceUris), parameters, token)
                    .ConfigureAwait(false);
            m_cleanup.Push(() => m_opener.DisposeAsync());
            DataChannel acceptor = Own(await provider.AcceptAsync(
                resource, DataChannelProfiles.OpaquePayload, m_options.Carrier, token).ConfigureAwait(false) ??
                throw new DataChannelException(DataChannelResult.Closed));
            m_source = m_options.ClientPublishes ? m_opener : acceptor;
            m_sink = m_options.ClientPublishes ? acceptor : m_opener;
            if (m_source.ChannelId != m_sink.ChannelId || m_source.Revision != 1 || m_sink.Revision != 1)
            {
                throw new InvalidOperationException("Flow establishment did not return matching ready channels.");
            }
            if (m_options.Shared && m_options.ServiceLoad)
            {
                Services = Own(new ServiceTraffic(m_session!, m_options));
                await Services.StartAsync(token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Uses the selected UASC profile without concrete transport casts or global transport registries.
        /// </summary>
        private async Task<(DataChannelContext Client, DataChannelContext Server)> ConnectUascAsync(
            SampleIdentityPair identities,
            CertificateEntry own,
            Certificate peer,
            IDataChannelContextFactory contexts,
            ServerDataChannelProvider provider,
            string address,
            CancellationToken cancellationToken)
        {
            ApplicationConfiguration configuration = identities.Client.ApplicationConfiguration!;
            ServiceMessageContext messageContext = configuration.CreateMessageContext();
            var settings = new TransportChannelSettings
            {
                Description = DescribeEndpoint(address, peer, false),
                Configuration = EndpointConfiguration.Create(configuration),
                ClientCertificate = own.Certificate.AddRef(),
                ServerCertificate = peer.AddRef(),
                CertificateValidator = SampleIdentityPair.Manager(identities.Client),
                NamespaceUris = messageContext.NamespaceUris,
                Factory = messageContext.Factory,
                DataChannels = contexts
            };
            var connector = new UascDataChannelClient(new TcpTransportChannelFactory(), m_telemetry);
            try
            {
                m_uasc = await connector.ConnectAsync(new Uri(address), settings, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                settings.ClientCertificate?.Dispose();
                settings.ServerCertificate?.Dispose();
                throw;
            }
            m_cleanup.Push(() => m_uasc.DisposeAsync());
            SecureChannelId = m_uasc.SecureChannelId;
            if (!m_options.Shared && m_uasc.Services != null)
            {
                throw new InvalidOperationException("Dedicated flow endpoints must not expose ordinary Services.");
            }
            DataChannelContext server = await provider.WaitForContextAsync(
                SampleIdentityPair.ClientApplicationUri, m_options.Carrier, cancellationToken).ConfigureAwait(false);
            return (m_uasc.Context, server);
        }

        /// <summary>
        /// Uses native-only library factories, including their real TLS, CONNECT and HELLO handshakes.
        /// </summary>
        private async Task<(DataChannelContext Client, DataChannelContext Server)> ConnectNativeAsync(
            ICertificateManager serverManager,
            ICertificateManager clientManager,
            Certificate serverCertificate,
            Certificate clientCertificate,
            IDataChannelContextFactory serverContexts,
            IDataChannelContextFactory clientContexts,
            IWebTransportConnectAuthorizer connectPolicy,
            CancellationToken cancellationToken)
        {
            m_native = new NativeQuicBackend(new NativeQuicLibrary(
                m_options.QuicLibrary!, new ByteString(Convert.FromHexString(m_options.QuicSha256!))));
            await m_native.GetCountsAsync(cancellationToken).ConfigureAwait(false);
            m_nativeLoaded = true;
            m_serverCredentials = new NativeQuicCertificateProvider(serverManager,
                new NativeQuicCertificateOptions(
                    SampleIdentityPair.ServerApplicationUri, ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    [new NativeQuicPeer(SampleIdentityPair.ClientApplicationUri,
                        DataChannelCertificateIdentity.GetKeyThumbprint(clientCertificate))]));
            NativeQuicCertificateProvider serverCredentials = m_serverCredentials;
            m_clientCredentials = new NativeQuicCertificateProvider(clientManager,
                new NativeQuicCertificateOptions(
                    SampleIdentityPair.ClientApplicationUri, ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    [new NativeQuicPeer(SampleIdentityPair.ServerApplicationUri,
                        DataChannelCertificateIdentity.GetKeyThumbprint(serverCertificate))]));
            NativeQuicCertificateProvider clientCredentials = m_clientCredentials;
            if (m_options.Carrier == DataChannelCarrierKind.Quic)
            {
                var server = new QuicDataChannelTransport(
                    m_native, serverCredentials, serverContexts, applicationBudget: m_serverBudget);
                var client = new QuicDataChannelTransport(
                    m_native, clientCredentials, clientContexts, applicationBudget: m_clientBudget);
                QuicDataChannelListener listener = Own(await server.ListenAsync(
                    new Uri("opc.quic://127.0.0.1:0"), cancellationToken).ConfigureAwait(false));
                Port = listener.EndpointUrl.Port;
                Task<QuicDataChannelConnection> accept = OwnAsync(listener.AcceptAsync(cancellationToken));
                Task<QuicDataChannelConnection> connect = OwnAsync(client.ConnectAsync(
                    listener.EndpointUrl, cancellationToken));
                await FinishPairAsync(accept, connect).ConfigureAwait(false);
                return ((await connect.ConfigureAwait(false)).Context, (await accept.ConfigureAwait(false)).Context);
            }
            var library = new NativeHttp3Library(
                m_options.Http3Library!, new ByteString(Convert.FromHexString(m_options.Http3Sha256!)));
            var options = new WebTransportDataChannelOptions(WebTransportOriginPolicy.AbsentOnly);
            var serverTransport = new WebTransportDataChannelTransport(m_native, library, serverCredentials,
                serverContexts, m_serverBudget, options, connectPolicy);
            var clientTransport = new WebTransportDataChannelTransport(m_native, library, clientCredentials,
                clientContexts, m_clientBudget, options);
            WebTransportDataChannelListener webListener = Own(await serverTransport.ListenAsync(
                new Uri("opc.wt://127.0.0.1:0/flow/v1"), cancellationToken).ConfigureAwait(false));
            Port = webListener.EndpointUrl.Port;
            Task<WebTransportDataChannelConnection> webAccept = OwnAsync(webListener.AcceptAsync(cancellationToken));
            Task<WebTransportDataChannelConnection> webConnect = OwnAsync(clientTransport.ConnectAsync(
                webListener.EndpointUrl, cancellationToken));
            await FinishPairAsync(webAccept, webConnect).ConfigureAwait(false);
            return ((await webConnect.ConfigureAwait(false)).Context, (await webAccept.ConfigureAwait(false)).Context);
        }

        /// <summary>
        /// Starts an ordinary Session on the shared connection, or explicitly on a separate control endpoint.
        /// </summary>
        private async Task<ClientSession> OpenSessionAsync(
            SampleIdentityPair identities,
            CertificateEntry own,
            Certificate peer,
            string sharedAddress,
            string controlAddress,
            CancellationToken cancellationToken)
        {
            ApplicationConfiguration configuration = identities.Client.ApplicationConfiguration!;
            ICertificateManager manager = SampleIdentityPair.Manager(identities.Client);
            CertificateIdentifier identifier = configuration.SecurityConfiguration.ApplicationCertificates[0];
            using Certificate? userCertificate = await manager.CertificateProvider.GetPrivateKeyCertificateAsync(
                identifier, identities.Password, configuration.ApplicationUri, cancellationToken)
                .ConfigureAwait(false) ??
                throw new InvalidOperationException("The certificate manager could not load the user identity.");
            UserIdentity identity = await UserIdentity.CreateAsync(
                identifier, identities.Password, manager.CertificateProvider, cancellationToken).ConfigureAwait(false);
            ISessionFactory factory = new DefaultSessionFactory(m_telemetry)
                .WithSubscriptionEngine(DefaultSubscriptionEngineFactory.Instance);
            var endpoint = new ConfiguredEndpoint(null,
                DescribeEndpoint(m_options.Shared ? sharedAddress : controlAddress, peer, !m_options.Shared),
                EndpointConfiguration.Create(configuration));
            if (!m_options.Shared)
            {
                return await factory.CreateAsync(configuration, endpoint, false, "DataChannelControl", 60000,
                    identity, [], cancellationToken).ConfigureAwait(false);
            }
            ClientSession session = factory.Create(
                m_uasc!.Services!, configuration, endpoint, own.Certificate.AddRef());
            try
            {
                await session.OpenAsync("DataChannelShared", 60000, identity, [],
                    checkDomain: true, closeChannel: false, cancellationToken).ConfigureAwait(false);
                return session;
            }
            catch
            {
                try
                {
                    await m_uasc.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }

        /// <summary>
        /// Pins the selected endpoint profile and the independently trusted server certificate.
        /// </summary>
        private EndpointDescription DescribeEndpoint(string address, Certificate peer, bool control)
        {
            return new EndpointDescription
            {
                EndpointUrl = address,
                TransportProfileUri = control ? Profiles.UaTcpTransport :
                    m_options.Shared ? DataChannelProfiles.SharedUasc : DataChannelProfiles.DedicatedUasc,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                ServerCertificate = new ByteString(peer.RawData),
                Server = new ApplicationDescription
                {
                    ApplicationUri = SampleIdentityPair.ServerApplicationUri,
                    ApplicationName = new LocalizedText("DataChannelSampleServer"),
                    ProductUri = SampleIdentityPair.ModelNamespace,
                    ApplicationType = ApplicationType.Server
                },
                UserIdentityTokens = control || m_options.Shared
                    ?
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "certificate",
                            TokenType = UserTokenType.Certificate,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        }
                    ]
                    : []
            };
        }

        /// <summary>
        /// Retains ownership as soon as an asynchronous factory succeeds, including partial pair failures.
        /// </summary>
        private async Task<T> OwnAsync<T>(ValueTask<T> operation)
            where T : IAsyncDisposable
        {
            return Own(await operation.ConfigureAwait(false));
        }

        /// <summary>
        /// Cancels the other establishment if either side fails, then observes both terminal outcomes.
        /// </summary>
        private async Task FinishPairAsync(Task first, Task second)
        {
            Task completed = await Task.WhenAny(first, second).ConfigureAwait(false);
            if (!completed.IsCompletedSuccessfully)
            {
                m_lifetime.Cancel();
            }
            await Task.WhenAll(first, second).ConfigureAwait(false);
        }

        /// <summary>
        /// Registers reverse acquisition-order asynchronous cleanup.
        /// </summary>
        private T Own<T>(T resource)
            where T : IAsyncDisposable
        {
            m_cleanup.Push(resource.DisposeAsync);
            return resource;
        }

        /// <summary>
        /// Releases every owned resource even if an earlier disposer reports failure.
        /// </summary>
        private async ValueTask ReleaseNextAsync()
        {
            if (m_cleanup.TryPop(out Func<ValueTask>? release))
            {
                try
                {
                    await release().ConfigureAwait(false);
                }
                finally
                {
                    await ReleaseNextAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Selects a free loopback port; only successful real listener startup is treated as readiness.
        /// </summary>
        private static int FreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private readonly SampleOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly DataChannelApplicationBudget m_serverBudget = new();
        private readonly DataChannelApplicationBudget m_clientBudget = new();
        private readonly ConcurrentStack<Func<ValueTask>> m_cleanup = new();
        private readonly ConcurrentQueue<ServerSession> m_sessions = new();
        private readonly CancellationTokenSource m_lifetime = new();
        private UascDataChannelConnection? m_uasc;
        private NativeQuicBackend? m_native;
        private SampleIdentityPair? m_identities;
        private ServerDataChannelProvider? m_provider;
        private SessionDataChannelAuthorizer? m_authorizer;
        private StandardServer? m_server;
        private NativeQuicCertificateProvider? m_clientCredentials;
        private NativeQuicCertificateProvider? m_serverCredentials;
        private ClientSession? m_session;
        private DataChannelSessionControl? m_control;
        private DataChannel? m_opener;
        private DataChannel? m_source;
        private DataChannel? m_sink;
        private bool m_nativeLoaded;
        private bool m_disposed;
    }
}
