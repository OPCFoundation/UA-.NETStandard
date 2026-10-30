// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Establishes the Part 32 reliable UACP/UASC baseline over HTTP/3 WebTransport.
    /// TLS authenticates only the server; the ordinary UASC channel separately authenticates applications.
    /// </summary>
    public sealed class WebTransportUascTransport
    {
        /// <summary>
        /// Creates an injectable transport with explicit HTTP/3 policy and application-scoped admission.
        /// </summary>
        public WebTransportUascTransport(
            INativeQuicBackend backend,
            NativeHttp3Library http3Library,
            DataChannelApplicationBudget applicationBudget,
            WebTransportDataChannelOptions options,
            TimeProvider? timeProvider = null)
        {
            m_backend = backend ?? throw new ArgumentNullException(nameof(backend));
            m_http3Library = http3Library ?? throw new ArgumentNullException(nameof(http3Library));
            m_applicationBudget = applicationBudget ?? throw new ArgumentNullException(nameof(applicationBudget));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_options.Validate();
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Opens a fresh HTTPS/H3 connection, completes CONNECT, and reserves the first client-created
        /// WebTransport bidirectional stream for ordinary UACP/UASC.
        /// </summary>
        public ValueTask<WebTransportUascBaselineConnection> ConnectAsync(
            Uri endpoint,
            NativeQuicCredentials tlsCredentials,
            CancellationToken credentialsInvalidated = default,
            CancellationToken cancellationToken = default)
        {
            return ConnectCoreAsync(
                endpoint, tlsCredentials, false, credentialsInvalidated, cancellationToken);
        }

        /// <summary>
        /// Opens a native data session with server-only TLS authentication. Use the returned factory
        /// with UascDataChannelClient to authenticate applications before admitting flows.
        /// </summary>
        public ValueTask<WebTransportUascBaselineConnection> ConnectDataChannelsAsync(
            Uri endpoint,
            NativeQuicCredentials tlsCredentials,
            CancellationToken credentialsInvalidated = default,
            CancellationToken cancellationToken = default)
        {
            return ConnectCoreAsync(
                endpoint, tlsCredentials, true, credentialsInvalidated, cancellationToken);
        }

        /// <summary>
        /// Establishes CONNECT without treating its HTTPS server identity as the OPC UA application identity.
        /// </summary>
        private async ValueTask<WebTransportUascBaselineConnection> ConnectCoreAsync(
            Uri endpoint,
            NativeQuicCredentials tlsCredentials,
            bool managementOnly,
            CancellationToken credentialsInvalidated,
            CancellationToken cancellationToken)
        {
            Uri httpsEndpoint = managementOnly ? WebTransportEndpoint.ToHttps(endpoint) : endpoint;
            WebTransportEndpoint.HttpsAddress(httpsEndpoint);
            ArgumentNullException.ThrowIfNull(tlsCredentials);
            if (!tlsCredentials.IsTransportOnly || !tlsCredentials.CertificateDer.IsEmpty)
            {
                throw new ArgumentException(
                    "The WebTransport client requires explicit server-trust-only TLS credentials.",
                    nameof(tlsCredentials));
            }
            int port = httpsEndpoint.IsDefaultPort ? 443 : httpsEndpoint.Port;
            string target = endpoint.DnsSafeHost.Contains(':', StringComparison.Ordinal)
                ? $"[{endpoint.DnsSafeHost}]:{port}"
                : $"{endpoint.DnsSafeHost}:{port}";
            NativeQuicConnection native = await NativeQuicConnection.ConnectTlsAsync(
                m_backend,
                tlsCredentials,
                target,
                DataChannelProfiles.WebTransportAlpn,
                endpoint.DnsSafeHost,
                m_options.Native,
                m_timeProvider,
                credentialsInvalidated,
                cancellationToken).ConfigureAwait(false);
            WebTransportDataChannelConnection carrier =
                await WebTransportDataChannelConnection.EstablishUascBaselineAsync(
                    native, m_http3Library, m_applicationBudget, httpsEndpoint, m_options,
                    timeProvider: m_timeProvider, managementOnly: managementOnly,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            return new WebTransportUascBaselineConnection(
                carrier, endpoint, isInitiator: true, managementOnly);
        }

        /// <summary>
        /// Listens on the supplied local UDP endpoint while preserving the exact public HTTPS authority/resource.
        /// </summary>
        public ValueTask<WebTransportUascListener> ListenAsync(
            Uri endpoint,
            IPEndPoint localEndpoint,
            NativeQuicCredentials tlsCredentials,
            Func<Uri, CancellationToken, ValueTask<bool>> connectAuthorizer,
            CancellationToken credentialsInvalidated = default,
            CancellationToken cancellationToken = default)
        {
            return ListenCoreAsync(
                endpoint, localEndpoint, tlsCredentials, connectAuthorizer, false,
                credentialsInvalidated, cancellationToken);
        }

        /// <summary>
        /// Listens for native data sessions whose first WebTransport stream authenticates applications
        /// through UASC. CONNECT authorization receives the mapped HTTPS authority and resource.
        /// </summary>
        public ValueTask<WebTransportUascListener> ListenDataChannelsAsync(
            Uri endpoint,
            IPEndPoint localEndpoint,
            NativeQuicCredentials tlsCredentials,
            Func<Uri, CancellationToken, ValueTask<bool>> connectAuthorizer,
            CancellationToken credentialsInvalidated = default,
            CancellationToken cancellationToken = default)
        {
            return ListenCoreAsync(
                endpoint, localEndpoint, tlsCredentials, connectAuthorizer, true,
                credentialsInvalidated, cancellationToken);
        }

        /// <summary>
        /// Retains the selected UA profile separately from the HTTPS CONNECT endpoint.
        /// </summary>
        private async ValueTask<WebTransportUascListener> ListenCoreAsync(
            Uri endpoint,
            IPEndPoint localEndpoint,
            NativeQuicCredentials tlsCredentials,
            Func<Uri, CancellationToken, ValueTask<bool>> connectAuthorizer,
            bool managementOnly,
            CancellationToken credentialsInvalidated,
            CancellationToken cancellationToken)
        {
            Uri httpsEndpoint = managementOnly ? WebTransportEndpoint.ToHttps(endpoint, listen: true) : endpoint;
            WebTransportEndpoint.HttpsAddress(httpsEndpoint);
            ArgumentNullException.ThrowIfNull(localEndpoint);
            ArgumentNullException.ThrowIfNull(tlsCredentials);
            ArgumentNullException.ThrowIfNull(connectAuthorizer);
            if (!tlsCredentials.IsTransportOnly || tlsCredentials.CertificateDer.IsEmpty)
            {
                throw new ArgumentException(
                    "The WebTransport listener requires explicit server certificate/key TLS credentials.",
                    nameof(tlsCredentials));
            }
            NativeQuicListener listener = await NativeQuicListener.ListenTlsAsync(
                m_backend,
                tlsCredentials,
                localEndpoint.ToString(),
                DataChannelProfiles.WebTransportAlpn,
                m_options.Native,
                m_timeProvider,
                credentialsInvalidated,
                cancellationToken).ConfigureAwait(false);
            Uri boundEndpoint = WebTransportEndpoint.WithBoundPort(endpoint, listener.Address);
            return new WebTransportUascListener(
                listener, m_http3Library, m_applicationBudget, boundEndpoint, m_options,
                connectAuthorizer, m_timeProvider, managementOnly);
        }

        /// <summary>
        /// Registers the transport factory; hosts still provide the native backend, budget and TLS snapshots.
        /// </summary>
        public static IOpcUaBuilder AddWebTransportUasc(
            IOpcUaBuilder builder,
            NativeHttp3Library http3Library,
            WebTransportDataChannelOptions options)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(http3Library);
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
            builder.Services.TryAddSingleton(http3Library);
            builder.Services.TryAddSingleton(options);
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.TryAddSingleton<WebTransportUascTransport>();
            return builder;
        }

        private readonly INativeQuicBackend m_backend;
        private readonly NativeHttp3Library m_http3Library;
        private readonly DataChannelApplicationBudget m_applicationBudget;
        private readonly WebTransportDataChannelOptions m_options;
        private readonly TimeProvider m_timeProvider;
    }

    /// <summary>
    /// Accepts individually authorized WebTransport CONNECT sessions; accepted sessions outlive the listener.
    /// </summary>
    public sealed class WebTransportUascListener : IAsyncDisposable
    {
        internal WebTransportUascListener(
            NativeQuicListener listener,
            NativeHttp3Library http3Library,
            DataChannelApplicationBudget applicationBudget,
            Uri endpoint,
            WebTransportDataChannelOptions options,
            Func<Uri, CancellationToken, ValueTask<bool>> connectAuthorizer,
            TimeProvider timeProvider,
            bool managementOnly = false)
        {
            m_listener = listener;
            m_http3Library = http3Library;
            m_applicationBudget = applicationBudget;
            EndpointUrl = endpoint;
            m_options = options;
            m_connectAuthorizer = connectAuthorizer;
            m_timeProvider = timeProvider;
            m_managementOnly = managementOnly;
        }

        /// <summary>
        /// Gets the selected UA endpoint used for UACP HEL validation. Native data endpoints
        /// use opc.wt and map to HTTPS for CONNECT.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <summary>
        /// Accepts one TLS-only connection after actual CONNECT authorization and baseline stream association.
        /// </summary>
        public async ValueTask<WebTransportUascBaselineConnection> AcceptAsync(
            CancellationToken cancellationToken = default)
        {
            NativeQuicConnection native = await m_listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            WebTransportDataChannelConnection carrier =
                await WebTransportDataChannelConnection.EstablishUascBaselineAsync(
                    native, m_http3Library, m_applicationBudget,
                    m_managementOnly ? WebTransportEndpoint.ToHttps(EndpointUrl) : EndpointUrl,
                    m_options, m_connectAuthorizer, m_timeProvider, m_managementOnly,
                    cancellationToken).ConfigureAwait(false);
            return new WebTransportUascBaselineConnection(
                carrier, EndpointUrl, isInitiator: false, m_managementOnly);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => m_listener.DisposeAsync();

        private readonly NativeQuicListener m_listener;
        private readonly NativeHttp3Library m_http3Library;
        private readonly DataChannelApplicationBudget m_applicationBudget;
        private readonly WebTransportDataChannelOptions m_options;
        private readonly Func<Uri, CancellationToken, ValueTask<bool>> m_connectAuthorizer;
        private readonly TimeProvider m_timeProvider;
        private readonly bool m_managementOnly;
    }

    /// <summary>
    /// Owns one established CONNECT and its first client-created bidirectional stream carrying UACP/UASC.
    /// Close the UASC channel before asynchronously disposing this connection.
    /// </summary>
    public sealed class WebTransportUascBaselineConnection :
        IUaSCByteTransportFactory, ITransportChannelFactory, IUaSCTransportProfile,
        IDataChannelDirectBindingProvider, IDataChannelDirectStreamProvider,
        IDataChannelDirectDatagramProvider, IDataChannelUascManagementProvider, IAsyncDisposable
    {
        internal WebTransportUascBaselineConnection(
            WebTransportDataChannelConnection carrier,
            Uri endpoint,
            bool isInitiator,
            bool managementOnly = false)
        {
            m_carrier = carrier;
            EndpointUrl = endpoint;
            m_isInitiator = isInitiator;
            m_managementOnly = managementOnly;
        }

        /// <summary>
        /// Gets the selected UA endpoint used for UACP HEL. Native data endpoints use opc.wt
        /// and map to HTTPS for CONNECT.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <inheritdoc/>
        public string Implementation => "WebTransport-UASC";

        /// <inheritdoc/>
        public string UriScheme => EndpointUrl.Scheme;

        /// <inheritdoc/>
        public string TransportProfileUri => m_managementOnly
            ? DataChannelProfiles.WebTransport : DataChannelProfiles.UascWebTransport;

        /// <inheritdoc/>
        public bool IsAvailable => !m_managementOnly && Volatile.Read(ref m_disposed) == 0;

        /// <inheritdoc/>
        DataChannelContext IDataChannelUascManagementProvider.CreateContext(
            IDataChannelCarrier managementCarrier,
            IDataChannelContextFactory contextFactory)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (!m_managementOnly)
            {
                throw new DataChannelException(DataChannelResult.Unsupported);
            }
            return m_carrier.AttachUascManagement(managementCarrier, contextFactory);
        }

        /// <inheritdoc/>
        public string Capability => DataChannelProfiles.WebTransportDirectCapability;

        /// <inheritdoc/>
        public string Authority => EndpointUrl.Authority;

        /// <inheritdoc/>
        public string Path => EndpointUrl.PathAndQuery;

        /// <inheritdoc/>
        public bool AreDirectStreamsAvailable => IsAvailable;

        /// <inheritdoc/>
        public int MaxDirectDatagramSize => IsAvailable ? m_carrier.MaxDatagramSize : 0;

        /// <inheritdoc/>
        public ValueTask<DataChannelResult> SendDirectDatagramAsync(
            DataChannelWrite write,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            return m_carrier.SendDirectDatagramAsync(write, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<ByteString> ReceiveDirectDatagramAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            return m_carrier.ReceiveDirectDatagramAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<Stream> OpenDirectStreamAsync(
            Guid associationToken,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            return m_carrier.OpenDirectStreamAsync(associationToken, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<Stream> AcceptDirectStreamAsync(
            Guid associationToken,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            return m_carrier.AcceptDirectStreamAsync(associationToken, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> ExportKeyingMaterialAsync(
            ByteString transcriptDigest,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (m_managementOnly)
            {
                throw new DataChannelException(DataChannelResult.Unsupported);
            }
            byte[] tlsLabel = "EXPORTER-WebTransport"u8.ToArray();
            byte[] context = CreateExporterContext(m_carrier.UascBaselineSessionId, transcriptDigest);

            try
            {
                NativeQuicCompletion completion = await m_carrier.NativeConnection
                    .ExportCustomKeyingMaterialAsync(tlsLabel, context, 32, cancellationToken)
                    .ConfigureAwait(false);
                NativeQuicConnection.RequireSuccess(completion);
                if (completion.Payload.IsNull || completion.Payload.Length != 32)
                {
                    throw new InvalidDataException("The WebTransport exporter returned an invalid key length.");
                }
                return completion.Payload;
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(context);
            }
        }

        internal static byte[] CreateExporterContext(ulong sessionId, ByteString transcriptDigest)
        {
            if (transcriptDigest.IsNull || transcriptDigest.Length != 32)
            {
                throw new ArgumentException("A 32-byte transcript digest is required.", nameof(transcriptDigest));
            }

            ReadOnlySpan<byte> applicationLabel = "OPCUA-WEBTRANSPORT-DRAFT"u8;
            byte[] context = new byte[checked(sizeof(ulong) + 1 + applicationLabel.Length + 1 + 32)];
            BinaryPrimitives.WriteUInt64BigEndian(context, sessionId);
            int offset = sizeof(ulong);
            context[offset++] = checked((byte)applicationLabel.Length);
            applicationLabel.CopyTo(context.AsSpan(offset));
            offset += applicationLabel.Length;
            context[offset++] = checked((byte)transcriptDigest.Length);
            transcriptDigest.Memory.Span.CopyTo(context.AsSpan(offset));
            return context;
        }

        /// <inheritdoc/>
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (!m_isInitiator)
            {
                throw new InvalidOperationException("An accepted WebTransport baseline cannot initiate a channel.");
            }
            return new BaselineChannel(this, telemetry);
        }

        /// <inheritdoc/>
        public IUaSCByteTransport Create(
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(bufferManager);
            ArgumentNullException.ThrowIfNull(telemetry);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (receiveBufferSize <= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(receiveBufferSize));
            }
            if (m_isInitiator)
            {
                return new UascStreamByteTransportFactory(
                    ClaimStreamAsync, Implementation, TransportProfileUri,
                    directBindingProvider: m_managementOnly ? null : this,
                    managementProvider: m_managementOnly ? this : null)
                    .Create(bufferManager, receiveBufferSize, telemetry);
            }
            if (Interlocked.Exchange(ref m_streamClaimed, 1) != 0)
            {
                throw new InvalidOperationException("A WebTransport management stream can be claimed only once.");
            }
            return new UascStreamByteTransport(
                m_carrier.UascBaselineByteStream,
                bufferManager,
                receiveBufferSize,
                Implementation,
                directBindingProvider: m_managementOnly ? null : this,
                managementProvider: m_managementOnly ? this : null);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }
            return m_carrier.DisposeAsync();
        }

        private ValueTask<Stream> ClaimStreamAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (!EndpointUrl.Equals(endpoint) || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0)
            {
                throw new ServiceResultException(StatusCodes.BadTcpEndpointUrlInvalid);
            }
            if (Interlocked.Exchange(ref m_streamClaimed, 1) != 0)
            {
                throw new InvalidOperationException("A WebTransport CONNECT baseline can be claimed only once.");
            }
            return ValueTask.FromResult(m_carrier.UascBaselineByteStream);
        }

        private sealed class BaselineChannel(
            WebTransportUascBaselineConnection owner,
            ITelemetryContext telemetry)
            : UaSCUaBinaryTransportChannel(owner, telemetry)
        {
            protected override void OnSettingsSaved(TransportChannelSettings settings, ChannelQuotas quotas)
            {
                if (settings.Description?.TransportProfileUri != owner.TransportProfileUri ||
                    settings.Description.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                    !Uri.TryCreate(settings.Description.EndpointUrl, UriKind.Absolute, out Uri? endpoint) ||
                    !owner.EndpointUrl.Equals(endpoint))
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed,
                        "The WebTransport baseline requires its exact endpoint, profile and SignAndEncrypt UASC.");
                }
            }
        }

        private readonly WebTransportDataChannelConnection m_carrier;
        private readonly bool m_isInitiator;
        private readonly bool m_managementOnly;
        private int m_streamClaimed;
        private int m_disposed;
    }
}
