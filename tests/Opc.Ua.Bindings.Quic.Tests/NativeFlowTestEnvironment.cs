// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.Quic.Tests
{
    /// <summary>
    /// Test-only in-memory identities acquired through the production certificate-manager bridge.
    /// No certificates, keys or machine trust are written.
    /// </summary>
    internal sealed class NativeFlowTestEnvironment : IAsyncDisposable
    {
        /// <summary>
        /// Creates owned certificate managers and application-scoped common budgets.
        /// </summary>
        private NativeFlowTestEnvironment(
            INativeQuicBackend backend,
            DataChannelOptions? options,
            QuicDataChannelOptions? bindingOptions,
            TimeSpan? certificateLifetime)
        {
            Backend = backend;
            m_certificateLifetime = certificateLifetime ?? TimeSpan.FromHours(1);
            Telemetry = new TestTelemetry();
            ClientManager = new CertificateManager(Telemetry, [m_stores], -1, null, TimeProvider.System);
            ServerManager = new CertificateManager(Telemetry, [m_stores], -1, null, TimeProvider.System);
            Options = options ?? new DataChannelOptions
            {
                MaxFlows = 6,
                MaxMessageSize = 4096,
                MaxReceiveBytes = 8192,
                MaxContextBytes = 65536,
                MaxContextMessages = 32,
                MaxQueueMessages = 8,
                MaxStreamHistory = 128,
                OpenTimeout = TimeSpan.FromSeconds(3)
            };
            BindingOptions = bindingOptions ?? new QuicDataChannelOptions
            {
                EstablishmentTimeout = TimeSpan.FromSeconds(3),
                Native = new NativeQuicConnectionSettings
                {
                    OperationTimeout = TimeSpan.FromSeconds(2),
                    HandshakeTimeout = TimeSpan.FromSeconds(3),
                    CertificateRecheckInterval = TimeSpan.FromSeconds(1)
                }
            };
            ClientFactory = CreateContextFactory(ClientBudget);
            ServerFactory = CreateContextFactory(ServerBudget);
        }

        /// <summary>
        /// Gets the real production native backend.
        /// </summary>
        internal INativeQuicBackend Backend { get; }

        /// <summary>
        /// Gets test logging context.
        /// </summary>
        internal ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets the client certificate registry and trust store.
        /// </summary>
        internal CertificateManager ClientManager { get; }

        /// <summary>
        /// Gets the server certificate registry and trust store.
        /// </summary>
        internal CertificateManager ServerManager { get; }

        /// <summary>
        /// Gets client application resource accounting.
        /// </summary>
        internal DataChannelApplicationBudget ClientBudget { get; } = new();

        /// <summary>
        /// Gets server application resource accounting.
        /// </summary>
        internal DataChannelApplicationBudget ServerBudget { get; } = new();

        /// <summary>
        /// Gets the client common runtime factory.
        /// </summary>
        internal IDataChannelContextFactory ClientFactory { get; }

        /// <summary>
        /// Gets the server common runtime factory.
        /// </summary>
        internal IDataChannelContextFactory ServerFactory { get; }

        /// <summary>
        /// Gets common limits used by this fixture.
        /// </summary>
        internal DataChannelOptions Options { get; }

        /// <summary>
        /// Gets finite native binding settings used by this fixture.
        /// </summary>
        internal QuicDataChannelOptions BindingOptions { get; }

        /// <summary>
        /// Gets the client production credential provider.
        /// </summary>
        internal NativeQuicCertificateProvider ClientCredentials => m_clientCredentials!;

        /// <summary>
        /// Gets the server production credential provider.
        /// </summary>
        internal NativeQuicCertificateProvider ServerCredentials => m_serverCredentials!;

        /// <summary>
        /// Gets the expected client identity and SPKI rule.
        /// </summary>
        internal NativeQuicPeer ClientPeer => m_clientPeer!;

        /// <summary>
        /// Gets the expected server identity and SPKI rule.
        /// </summary>
        internal NativeQuicPeer ServerPeer => m_serverPeer!;

        /// <summary>
        /// Gets the ephemeral test CA for controlled rotation tests.
        /// </summary>
        internal Certificate Root => m_root!;

        /// <summary>
        /// Creates real certificate-manager material without test-only production bypasses.
        /// </summary>
        internal static async ValueTask<NativeFlowTestEnvironment> CreateAsync(
            INativeQuicBackend backend,
            DataChannelOptions? options = null,
            QuicDataChannelOptions? bindingOptions = null,
            TimeSpan? certificateLifetime = null)
        {
            var environment = new NativeFlowTestEnvironment(backend, options, bindingOptions, certificateLifetime);
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
        /// Loads the explicitly selected production native asset, checked against its adjacent SHA256 receipt.
        /// Missing assets fail this native-specific test project rather than silently skipping acceptance.
        /// </summary>
        internal static async ValueTask<INativeQuicBackend> LoadBackendAsync(string? nativeLibrary = null)
        {
            string path = nativeLibrary ?? Environment.GetEnvironmentVariable("OPCUA_QUIC_NATIVE_PATH") ??
                throw new InvalidOperationException("Set OPCUA_QUIC_NATIVE_PATH to the reviewed native library.");
            NativeQuicLibrary library = await NativeQuicLibrary.FromDirectoryAsync(
                Path.GetDirectoryName(Path.GetFullPath(path))!).ConfigureAwait(false);
            return new NativeQuicBackend(library);
        }

        /// <summary>
        /// Creates a client using actual native transport and optional application-supplied factory/policy.
        /// </summary>
        internal QuicDataChannelTransport CreateClient(
            INativeQuicCredentialProvider? credentials = null,
            IDataChannelContextFactory? factory = null)
        {
            return new QuicDataChannelTransport(
                Backend, credentials ?? ClientCredentials, factory ?? ClientFactory, BindingOptions,
                applicationBudget: ClientBudget);
        }

        /// <summary>
        /// Creates a server using actual native transport and optional application-supplied factory/policy.
        /// </summary>
        internal QuicDataChannelTransport CreateServer(
            INativeQuicCredentialProvider? credentials = null,
            IDataChannelContextFactory? factory = null)
        {
            return new QuicDataChannelTransport(
                Backend, credentials ?? ServerCredentials, factory ?? ServerFactory, BindingOptions,
                applicationBudget: ServerBudget);
        }

        /// <summary>
        /// Creates a replacement explicit client/server peer policy through the same real registry/stores.
        /// </summary>
        internal NativeQuicCertificateProvider CreateCredentials(
            bool client,
            ArrayOf<NativeQuicPeer> peers,
            TrustListIdentifier? trustList = null)
        {
            return new NativeQuicCertificateProvider(
                client ? ClientManager : ServerManager,
                new NativeQuicCertificateOptions(
                    client ? ClientUri : ServerUri,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    peers,
                    trustList));
        }

        /// <summary>
        /// Establishes independent loopback native-only endpoints before any application DATA.
        /// </summary>
        internal async ValueTask<NativeFlowPair> ConnectPairAsync(
            IDataChannelContextFactory? clientFactory = null,
            IDataChannelContextFactory? serverFactory = null,
            INativeQuicCredentialProvider? clientCredentials = null,
            INativeQuicCredentialProvider? serverCredentials = null,
            Uri? listenEndpoint = null)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            QuicDataChannelListener listener = await CreateServer(serverCredentials, serverFactory)
                .ListenAsync(listenEndpoint ?? new Uri("opc.quic://127.0.0.1:0"), deadline.Token).ConfigureAwait(false);
            Task<QuicDataChannelConnection> accepted = listener.AcceptAsync(deadline.Token).AsTask();
            QuicDataChannelConnection? client = null;
            try
            {
                client = await CreateClient(clientCredentials, clientFactory).ConnectAsync(listener.EndpointUrl, deadline.Token)
                    .ConfigureAwait(false);
                QuicDataChannelConnection server = await accepted.ConfigureAwait(false);
                return new NativeFlowPair(listener, client, server);
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
                    await using QuicDataChannelConnection server = await accepted.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The original establishment failure is retained after draining the other endpoint.
                }
                throw;
            }
        }

        /// <summary>
        /// Creates an explicit confined resource descriptor, independent of optional Session/model services.
        /// </summary>
        internal static DataChannelParametersDataType Parameters(
            DataChannelDeliveryMode delivery = DataChannelDeliveryMode.Ordered,
            DataChannelDirection direction = DataChannelDirection.Duplex)
        {
            return new DataChannelParametersDataType
            {
                ResourceUri = ResourceUri,
                Protocol = DataChannelProfiles.OpaquePayload,
                ContentType = "application/octet-stream",
                Direction = direction,
                Delivery = delivery,
                Protection = DataChannelProtectionMode.Confidential,
                MaxMessageSize = 1024,
                MaxReceiveBytes = 8192,
                MaxRate = delivery == DataChannelDeliveryMode.Datagram ? 1000000 : 0,
                Priority = 1,
                AllowQueueExpiry = false
            };
        }

        /// <summary>
        /// Waits for asynchronous socket cleanup, asserting every actual native ownership counter.
        /// </summary>
        internal static async Task AssertNoNativeOwnershipAsync(INativeQuicBackend backend)
        {
            NativeQuicCounts counts = default;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                counts = await backend.GetCountsAsync().ConfigureAwait(false);
                if (counts == default)
                {
                    break;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
            if (counts != default)
            {
                throw new InvalidOperationException("Native ownership was not drained: " + counts);
            }
        }

        /// <summary>
        /// Creates a short-lived test-only application identity using the stack certificate factory.
        /// </summary>
        internal Certificate CreateApplicationCertificate(string applicationUri)
        {
            DateTime now = DateTime.UtcNow;
            ICertificateBuilder builder = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                applicationUri, "Native flow test", "CN=Native flow test", ["localhost", "127.0.0.1", "::1"])
                .SetNotBefore(now.AddMinutes(-5))
                .SetNotAfter(now.Add(m_certificateLifetime));
            builder.SetIssuer(Root);
            return builder.SetRSAKeySize(2048).CreateForRSA();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_clientCredentials?.Dispose();
            m_serverCredentials?.Dispose();
            await ClientManager.DisposeAsync().ConfigureAwait(false);
            await ServerManager.DisposeAsync().ConfigureAwait(false);
            m_root?.Dispose();
            m_stores.Dispose();
        }

        /// <summary>
        /// Creates a common factory with explicit resource policy.
        /// </summary>
        private DataChannelContextFactory CreateContextFactory(DataChannelApplicationBudget budget)
        {
            return new DataChannelContextFactory(
                new TestAuthorizer(), Options, budget, TimeProvider.System, Telemetry);
        }

        /// <summary>
        /// Populates only the fixture's isolated manager stores/registries.
        /// </summary>
        private async Task InitializeAsync()
        {
            m_root = DefaultCertificateFactory.Instance.CreateCertificate("CN=Native flow tests root")
                .SetCAConstraint()
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-5))
                .SetNotAfter(DateTime.UtcNow.Add(m_certificateLifetime).AddHours(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate client = CreateApplicationCertificate(ClientUri);
            using Certificate server = CreateApplicationCertificate(ServerUri);
            using var issuers = new CertificateCollection { Root };
            ClientManager.RegisterTrustList(TrustListIdentifier.Peers, "InMemory:NativeClient:" + Guid.NewGuid());
            ServerManager.RegisterTrustList(TrustListIdentifier.Peers, "InMemory:NativeServer:" + Guid.NewGuid());
            using (ICertificateStore trusted = ClientManager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await trusted.AddAsync(Root).ConfigureAwait(false);
            }
            using (ICertificateStore trusted = ServerManager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await trusted.AddAsync(Root).ConfigureAwait(false);
            }
            await ClientManager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType, client, issuers).ConfigureAwait(false);
            await ServerManager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType, server, issuers).ConfigureAwait(false);
            m_clientPeer = new NativeQuicPeer(ClientUri, NativeQuicCertificateProvider.GetPublicKeySha256(client));
            m_serverPeer = new NativeQuicPeer(ServerUri, NativeQuicCertificateProvider.GetPublicKeySha256(server));
            m_clientCredentials = CreateCredentials(true, [ServerPeer]);
            m_serverCredentials = CreateCredentials(false, [ClientPeer]);
        }

        /// <summary>
        /// Accepts only the test's explicit configured resource, never a Session or unmatched resource.
        /// </summary>
        private sealed class TestAuthorizer : IDataChannelAuthorizer
        {
            /// <inheritdoc/>
            public ValueTask<DataChannelAuthorization> AuthorizeAsync(
                DataChannelAuthorizationRequest request,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DataChannelParametersDataType parameters = request.Parameters;
                return ValueTask.FromResult(
                    request.Mode == DataChannelAuthorizationMode.Configured &&
                    parameters.ResourceUri == ResourceUri && parameters.Protocol == DataChannelProfiles.OpaquePayload
                        ? new DataChannelAuthorization(DataChannelResult.Good, parameters)
                        : new DataChannelAuthorization(DataChannelResult.NotAuthorized));
            }
        }

        /// <summary>
        /// Uses the existing telemetry base without a test-transport mock or process-global logging changes.
        /// </summary>
        private sealed class TestTelemetry() : TelemetryContextBase(NullLoggerFactory.Instance);

        /// <summary>Explicit test client URI.</summary>
        internal const string ClientUri = "urn:opcfoundation:flow-binding-tests:client";

        /// <summary>Explicit test server URI.</summary>
        internal const string ServerUri = "urn:opcfoundation:flow-binding-tests:server";

        /// <summary>Explicit test-only resource policy.</summary>
        internal const string ResourceUri = "urn:opcfoundation:flow-binding-tests:payload";

        private readonly InMemoryStoreProvider m_stores = new();
        private readonly TimeSpan m_certificateLifetime;
        private Certificate? m_root;
        private NativeQuicPeer? m_clientPeer;
        private NativeQuicPeer? m_serverPeer;
        private NativeQuicCertificateProvider? m_clientCredentials;
        private NativeQuicCertificateProvider? m_serverCredentials;
    }

    /// <summary>
    /// Owns both independently connected native-only contexts and their listening endpoint.
    /// </summary>
    internal sealed class NativeFlowPair(
        QuicDataChannelListener listener,
        QuicDataChannelConnection client,
        QuicDataChannelConnection server) : IAsyncDisposable
    {
        /// <summary>
        /// Gets the actual native listener.
        /// </summary>
        internal QuicDataChannelListener Listener { get; } = listener;

        /// <summary>
        /// Gets the actual TLS-initiating data context.
        /// </summary>
        internal QuicDataChannelConnection Client { get; } = client;

        /// <summary>
        /// Gets the actual TLS-accepting data context.
        /// </summary>
        internal QuicDataChannelConnection Server { get; } = server;

        /// <summary>
        /// Completes actual common OPEN/ACCEPT/READY before any DATA is sent.
        /// </summary>
        internal async ValueTask<(DataChannel Opener, DataChannel Acceptor)> OpenAsync(
            bool clientOpens = true,
            DataChannelParametersDataType? parameters = null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            QuicDataChannelConnection opener = clientOpens ? Client : Server;
            QuicDataChannelConnection acceptor = clientOpens ? Server : Client;
            Task<DataChannel?> accepted = acceptor.Context.AcceptAsync(timeout.Token).AsTask();
            DataChannel opened = await opener.Context.OpenAsync(
                parameters ?? NativeFlowTestEnvironment.Parameters(), cancellationToken: timeout.Token)
                .ConfigureAwait(false);
            DataChannel received = await accepted.ConfigureAwait(false) ??
                throw new InvalidOperationException("The peer closed before READY.");
            return (opened, received);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await Listener.DisposeAsync().ConfigureAwait(false);
            await Task.WhenAll(Client.DisposeAsync().AsTask(), Server.DisposeAsync().AsTask()).ConfigureAwait(false);
        }
    }
}
