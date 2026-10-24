// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Real native peers with ephemeral in-memory certificate-manager identities and explicit resource policy.
    /// The same fixture is usable by the NativeAOT smoke host; no certificate/key files are generated.
    /// </summary>
    internal sealed class WebTransportTestEnvironment : IAsyncDisposable
    {
        /// <summary>
        /// Owns independent application registries/budgets, not a paired or simulated transport.
        /// </summary>
        private WebTransportTestEnvironment(
            INativeQuicBackend backend,
            NativeHttp3Library http3Library,
            DataChannelOptions? coreOptions,
            WebTransportDataChannelOptions? options)
        {
            Backend = backend;
            Http3Library = http3Library;
            ClientBackend = new RecordingQuicBackend(backend);
            ServerBackend = new RecordingQuicBackend(backend);
            Telemetry = new TestTelemetry();
            ClientManager = new CertificateManager(Telemetry, [m_stores], -1, null, TimeProvider.System);
            ServerManager = new CertificateManager(Telemetry, [m_stores], -1, null, TimeProvider.System);
            CoreOptions = coreOptions ?? new DataChannelOptions
            {
                MaxFlows = 4,
                MaxMessageSize = 4096,
                MaxReceiveBytes = 8192,
                MaxContextBytes = 65536,
                MaxContextMessages = 32,
                MaxQueueMessages = 8,
                MaxStreamHistory = 128,
                OpenTimeout = TimeSpan.FromSeconds(5)
            };
            Options = options ?? new WebTransportDataChannelOptions(WebTransportOriginPolicy.AbsentOnly)
            {
                EstablishmentTimeout = TimeSpan.FromSeconds(5),
                Native = new NativeQuicConnectionSettings
                {
                    MaxStreams = 16,
                    MaxPendingOperations = 48,
                    OperationTimeout = TimeSpan.FromSeconds(5),
                    HandshakeTimeout = TimeSpan.FromSeconds(5),
                    CertificateRecheckInterval = TimeSpan.FromSeconds(1)
                }
            };
            ClientFactory = Factory(ClientBudget);
            ServerFactory = Factory(ServerBudget);
        }

        /// <summary>
        /// Gets the real reviewed backend.
        /// </summary>
        internal INativeQuicBackend Backend { get; }

        /// <summary>
        /// Gets the reviewed production H3 library.
        /// </summary>
        internal NativeHttp3Library Http3Library { get; }

        /// <summary>
        /// Gets client-side observations of actual native submissions/results.
        /// </summary>
        internal RecordingQuicBackend ClientBackend { get; }

        /// <summary>
        /// Gets server-side observations of actual native submissions/results.
        /// </summary>
        internal RecordingQuicBackend ServerBackend { get; }

        /// <summary>
        /// Gets the isolated telemetry provider.
        /// </summary>
        internal ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets the actual client certificate registry.
        /// </summary>
        internal CertificateManager ClientManager { get; }

        /// <summary>
        /// Gets the actual server certificate registry.
        /// </summary>
        internal CertificateManager ServerManager { get; }

        /// <summary>
        /// Gets client-wide native/H3/Core admission accounting.
        /// </summary>
        internal DataChannelApplicationBudget ClientBudget { get; } = new();

        /// <summary>
        /// Gets server-wide native/H3/Core admission accounting.
        /// </summary>
        internal DataChannelApplicationBudget ServerBudget { get; } = new();

        /// <summary>
        /// Gets explicit common runtime limits.
        /// </summary>
        internal DataChannelOptions CoreOptions { get; }

        /// <summary>
        /// Gets the binding policy and finite transport limits.
        /// </summary>
        internal WebTransportDataChannelOptions Options { get; }

        /// <summary>
        /// Gets the client's default configured-resource factory.
        /// </summary>
        internal IDataChannelContextFactory ClientFactory { get; }

        /// <summary>
        /// Gets the server's default configured-resource factory.
        /// </summary>
        internal IDataChannelContextFactory ServerFactory { get; }

        /// <summary>
        /// Gets the client certificate-manager bridge.
        /// </summary>
        internal NativeQuicCertificateProvider ClientCredentials => m_clientCredentials!;

        /// <summary>
        /// Gets the server certificate-manager bridge.
        /// </summary>
        internal NativeQuicCertificateProvider ServerCredentials => m_serverCredentials!;

        /// <summary>
        /// Gets explicit server-authenticated TLS credentials for a baseline-only connection.
        /// </summary>
        internal NativeQuicCredentials TlsServerCredentials
        {
            get
            {
                if (m_tlsServerCredentials == null)
                {
                    using RSA rsa = m_serverCertificate!.GetRSAPrivateKey() ??
                        throw new InvalidOperationException("The test server certificate must have an RSA key.");
                    byte[] exported = rsa.ExportPkcs8PrivateKey();
                    try
                    {
                        m_tlsServerKey = new NativeQuicPrivateKey(exported);
                        m_tlsServerCredentials = NativeQuicCredentials.ForTlsServer(
                            new ByteString(m_serverCertificate.RawData),
                            new ByteString(m_root!.RawData),
                            m_tlsServerKey);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(exported);
                    }
                }
                return m_tlsServerCredentials;
            }
        }

        /// <summary>
        /// Gets trust-only client credentials that never request a client certificate.
        /// </summary>
        internal NativeQuicCredentials TlsClientCredentials =>
            NativeQuicCredentials.ForTlsClient(new ByteString(m_root!.RawData));

        /// <summary>
        /// Loads exact caller-selected reviewed assets; missing assets fail rather than skip native acceptance.
        /// </summary>
        internal static async ValueTask<(INativeQuicBackend Backend, NativeHttp3Library Http3)> LoadAsync()
        {
            string quicPath = Environment.GetEnvironmentVariable("OPCUA_QUIC_NATIVE_PATH") ??
                throw new InvalidOperationException(
                    "OPCUA_QUIC_NATIVE_PATH must select the reviewed native QUIC image.");
            string h3Path = Environment.GetEnvironmentVariable("OPCUA_HTTP3_NATIVE_PATH") ??
                throw new InvalidOperationException(
                    "OPCUA_HTTP3_NATIVE_PATH must select the reviewed native HTTP/3 image.");
            string h3Hash = Environment.GetEnvironmentVariable("OPCUA_HTTP3_NATIVE_SHA256") ??
                throw new InvalidOperationException("OPCUA_HTTP3_NATIVE_SHA256 must contain the reviewed digest.");
            NativeQuicLibrary quic = await NativeQuicLibrary.FromDirectoryAsync(
                Path.GetDirectoryName(Path.GetFullPath(quicPath))!).ConfigureAwait(false);
            var h3 = new NativeHttp3Library(Path.GetFullPath(h3Path), new ByteString(Convert.FromHexString(h3Hash)));
            await h3.ValidateAsync().ConfigureAwait(false);
            return (new NativeQuicBackend(quic), h3);
        }

        /// <summary>
        /// Initializes owned ephemeral identities through production certificate stores and registry APIs.
        /// </summary>
        internal static async ValueTask<WebTransportTestEnvironment> CreateAsync(
            INativeQuicBackend backend,
            NativeHttp3Library http3Library,
            DataChannelOptions? coreOptions = null,
            WebTransportDataChannelOptions? options = null)
        {
            var environment = new WebTransportTestEnvironment(backend, http3Library, coreOptions, options);
            try
            {
                await environment.InitializeAsync().ConfigureAwait(false);
                return environment;
            }
            catch
            {
                await environment.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Creates a direct real client, permitting a test-specific Core/endpoint policy.
        /// </summary>
        internal WebTransportDataChannelTransport Client(
            WebTransportDataChannelOptions? options = null,
            IDataChannelContextFactory? factory = null,
            INativeQuicCredentialProvider? credentials = null)
        {
            return new WebTransportDataChannelTransport(
                ClientBackend, Http3Library, credentials ?? ClientCredentials, factory ?? ClientFactory,
                ClientBudget, options ?? Options);
        }

        /// <summary>
        /// Creates a direct real listener factory with explicit peer/resource authorization.
        /// </summary>
        internal WebTransportDataChannelTransport Server(
            WebTransportDataChannelOptions? options = null,
            IDataChannelContextFactory? factory = null,
            IWebTransportConnectAuthorizer? authorizer = null)
        {
            return new WebTransportDataChannelTransport(
                ServerBackend, Http3Library, ServerCredentials, factory ?? ServerFactory,
                ServerBudget, options ?? Options, authorizer ?? new ConnectPolicy(true));
        }

        /// <summary>
        /// Creates a complete common context without Server/Session services.
        /// </summary>
        internal IDataChannelContextFactory Factory(DataChannelApplicationBudget budget, bool reservedLane = false)
        {
            return new DataChannelContextFactory(
                new ResourcePolicy(reservedLane), CoreOptions, budget, TimeProvider.System, Telemetry);
        }

        /// <summary>
        /// Replaces the actual selected client key through the production certificate registry.
        /// </summary>
        internal async ValueTask RotateClientCertificateAsync()
        {
            using Certificate replacement = CreateCertificate(ClientUri);
            using var issuers = new CertificateCollection { m_root! };
            await ClientManager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType, replacement, issuers).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates independent native listening/connecting peers and drains both sides on any failure.
        /// </summary>
        internal async ValueTask<WebTransportPair> ConnectPairAsync(
            WebTransportDataChannelOptions? clientOptions = null,
            WebTransportDataChannelOptions? serverOptions = null,
            IDataChannelContextFactory? clientFactory = null,
            IDataChannelContextFactory? serverFactory = null)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            WebTransportDataChannelListener listener = await Server(serverOptions, serverFactory)
                .ListenAsync(new Uri("opc.wt://127.0.0.1:0/flow/v1"), deadline.Token).ConfigureAwait(false);
            Task<WebTransportDataChannelConnection> accepted = listener.AcceptAsync(deadline.Token).AsTask();
            WebTransportDataChannelConnection? client = null;
            try
            {
                client = await Client(clientOptions, clientFactory).ConnectAsync(listener.EndpointUrl, deadline.Token)
                    .ConfigureAwait(false);
                WebTransportDataChannelConnection server = await accepted.ConfigureAwait(false);
                return new WebTransportPair(listener, client, server);
            }
            catch
            {
                deadline.Cancel();
                await listener.DisposeAsync().ConfigureAwait(false);
                if (client != null)
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
                try
                {
                    await using WebTransportDataChannelConnection server = await accepted.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Retain the original failed establishment after draining the other endpoint.
                }
                throw;
            }
        }

        /// <summary>
        /// Creates an explicit ordinary opaque resource descriptor.
        /// </summary>
        internal static DataChannelParametersDataType Parameters(
            DataChannelDeliveryMode delivery = DataChannelDeliveryMode.Ordered)
        {
            return new DataChannelParametersDataType
            {
                ResourceUri = ResourceUri,
                Protocol = DataChannelProfiles.OpaquePayload,
                ContentType = "application/octet-stream",
                Direction = DataChannelDirection.Duplex,
                Delivery = delivery,
                Protection = DataChannelProtectionMode.Confidential,
                MaxMessageSize = 1024,
                MaxReceiveBytes = 8192,
                MaxRate = delivery == DataChannelDeliveryMode.Datagram ? 1000000 : 0,
                Priority = 1,
                AllowQueueExpiry = false
            };
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_clientCredentials?.Dispose();
            m_serverCredentials?.Dispose();
            m_tlsServerKey?.Dispose();
            m_serverCertificate?.Dispose();
            await ClientManager.DisposeAsync().ConfigureAwait(false);
            await ServerManager.DisposeAsync().ConfigureAwait(false);
            m_root?.Dispose();
            m_stores.Dispose();
        }

        /// <summary>
        /// Generates only in-memory, short-lived test identities and explicit trusted issuer entries.
        /// </summary>
        private async ValueTask InitializeAsync()
        {
            m_root = DefaultCertificateFactory.Instance.CreateCertificate("CN=WebTransport tests root")
                .SetCAConstraint()
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-5))
                .SetNotAfter(DateTime.UtcNow.AddHours(2))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate client = CreateCertificate(ClientUri);
            using Certificate server = CreateCertificate(ServerUri);
            using var issuers = new CertificateCollection { m_root };
            ClientManager.RegisterTrustList(TrustListIdentifier.Peers, "InMemory:WtClient:" + Guid.NewGuid());
            ServerManager.RegisterTrustList(TrustListIdentifier.Peers, "InMemory:WtServer:" + Guid.NewGuid());
            using (ICertificateStore trusted = ClientManager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await trusted.AddAsync(m_root).ConfigureAwait(false);
            }
            using (ICertificateStore trusted = ServerManager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await trusted.AddAsync(m_root).ConfigureAwait(false);
            }
            await ClientManager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType, client, issuers).ConfigureAwait(false);
            await ServerManager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType, server, issuers).ConfigureAwait(false);
            m_serverCertificate = server.AddRef();
            m_clientCredentials = new NativeQuicCertificateProvider(ClientManager,
                new NativeQuicCertificateOptions(ClientUri, ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    [new NativeQuicPeer(ServerUri, NativeQuicCertificateProvider.GetPublicKeySha256(server))]));
            m_serverCredentials = new NativeQuicCertificateProvider(ServerManager,
                new NativeQuicCertificateOptions(ServerUri, ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    [new NativeQuicPeer(ClientUri, NativeQuicCertificateProvider.GetPublicKeySha256(client))]));
        }

        /// <summary>
        /// Creates one leaf under the fixture's ephemeral issuer.
        /// </summary>
        private Certificate CreateCertificate(string uri)
        {
            string[] domains =
            [
                "localhost", "127.0.0.1", "::1", Utils.GetHostName(), Utils.GetFullQualifiedDomainName()
            ];
            return DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                uri, "WebTransport test", "CN=WebTransport test",
                domains.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-5))
                .SetNotAfter(DateTime.UtcNow.AddHours(1))
                .SetIssuer(m_root!)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// Explicit test CONNECT authorization, evaluated against verified application identities.
        /// </summary>
        internal sealed class ConnectPolicy(bool allow) : IWebTransportConnectAuthorizer
        {
            /// <inheritdoc/>
            public ValueTask<bool> AuthorizeAsync(
                DataChannelPeerIdentity identity,
                Uri endpointUrl,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(allow && identity.PeerApplicationUri == ClientUri &&
                    endpointUrl.AbsolutePath == "/flow/v1");
            }
        }

        /// <summary>
        /// Explicit configured policy; the reserved lane is a separately selected resource.
        /// </summary>
        private sealed class ResourcePolicy(bool reservedLane) : IDataChannelAuthorizer
        {
            /// <inheritdoc/>
            public ValueTask<DataChannelAuthorization> AuthorizeAsync(
                DataChannelAuthorizationRequest request,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DataChannelParametersDataType parameters = request.Parameters;
                bool resource = parameters.ResourceUri == ResourceUri ||
                    (reservedLane && parameters.ResourceUri == DataChannelProfiles.ClientServerResource &&
                        parameters.Direction == DataChannelDirection.Duplex &&
                        parameters.Delivery == DataChannelDeliveryMode.Ordered &&
                        parameters.Protection == DataChannelProtectionMode.Confidential &&
                        !parameters.AllowQueueExpiry);
                bool protocol = parameters.Protocol is DataChannelProfiles.OpaquePayload or
                    DataChannelProfiles.UadpPayload or DataChannelProfiles.UadpAvApplicationPayload or
                    DataChannelProfiles.UadpOpaqueApplicationPayload;
                return ValueTask.FromResult(request.Mode == DataChannelAuthorizationMode.Configured &&
                    resource && protocol
                    ? new DataChannelAuthorization(DataChannelResult.Good, parameters)
                    : new DataChannelAuthorization(DataChannelResult.NotAuthorized));
            }
        }

        /// <summary>
        /// Uses the existing telemetry abstraction without process-global logger mutation.
        /// </summary>
        private sealed class TestTelemetry() : TelemetryContextBase(NullLoggerFactory.Instance);

        /// <summary>Explicit client identity, not a credential.</summary>
        internal const string ClientUri = "urn:opcfoundation:wt-tests:client";

        /// <summary>Explicit server identity, not a credential.</summary>
        internal const string ServerUri = "urn:opcfoundation:wt-tests:server";

        /// <summary>Explicit ordinary test resource.</summary>
        internal const string ResourceUri = "urn:opcfoundation:wt-tests:payload";

        private readonly InMemoryStoreProvider m_stores = new();
        private Certificate? m_root;
        private Certificate? m_serverCertificate;
        private NativeQuicPrivateKey? m_tlsServerKey;
        private NativeQuicCredentials? m_tlsServerCredentials;
        private NativeQuicCertificateProvider? m_clientCredentials;
        private NativeQuicCertificateProvider? m_serverCredentials;
    }

    /// <summary>
    /// Owns a real listening endpoint and both independently authenticated connections.
    /// </summary>
    internal sealed class WebTransportPair(
        WebTransportDataChannelListener listener,
        WebTransportDataChannelConnection client,
        WebTransportDataChannelConnection server) : IAsyncDisposable
    {
        /// <summary>
        /// Gets the actual bound listener.
        /// </summary>
        internal WebTransportDataChannelListener Listener { get; } = listener;

        /// <summary>
        /// Gets the initiating native peer.
        /// </summary>
        internal WebTransportDataChannelConnection Client { get; } = client;

        /// <summary>
        /// Gets the accepting native peer.
        /// </summary>
        internal WebTransportDataChannelConnection Server { get; } = server;

        /// <summary>
        /// Returns both common facades only after actual OPEN/ACCEPT/READY exchange on bound WT streams.
        /// </summary>
        internal async ValueTask<(DataChannel Opener, DataChannel Acceptor)> OpenAsync(
            bool clientOpens = true,
            DataChannelParametersDataType? parameters = null)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            WebTransportDataChannelConnection opener = clientOpens ? Client : Server;
            WebTransportDataChannelConnection acceptor = clientOpens ? Server : Client;
            Task<DataChannel?> accepted = acceptor.Context.AcceptAsync(deadline.Token).AsTask();
            DataChannel opened = await opener.Context.OpenAsync(
                parameters ?? WebTransportTestEnvironment.Parameters(), cancellationToken: deadline.Token)
                .ConfigureAwait(false);
            return (opened, await accepted.ConfigureAwait(false) ??
                throw new InvalidOperationException("The peer closed before READY."));
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await Listener.DisposeAsync().ConfigureAwait(false);
            await Task.WhenAll(Client.DisposeAsync().AsTask(), Server.DisposeAsync().AsTask()).ConfigureAwait(false);
        }
    }
}
