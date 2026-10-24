// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

#if NET8_0_OR_GREATER
using System;
using System.IO;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Establishes the Part 31 reliable baseline using managed QUIC, independently of direct delivery.
    /// TLS options must come from the host's certificate-store configuration. UASC still authenticates
    /// the application pair; this transport neither requires TLS client authentication nor authorizes flows.
    /// </summary>
#if NET8_0
    [RequiresPreviewFeatures("System.Net.Quic is a preview API on .NET 8.")]
#endif
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    public sealed class QuicUascTransport
    {
        /// <summary>
        /// Establishes one initiator-created bidirectional baseline stream. The caller owns the result
        /// and must dispose it asynchronously after closing its UASC channel.
        /// </summary>
        public async ValueTask<QuicUascConnection> ConnectAsync(
            Uri endpoint,
            SslClientAuthenticationOptions tls,
            CancellationToken cancellationToken = default)
        {
            ValidateEndpoint(endpoint, listen: false);
            ArgumentNullException.ThrowIfNull(tls);
            ValidateTls(tls.ApplicationProtocols, tls.EnabledSslProtocols);
            if (!string.Equals(tls.TargetHost, endpoint.DnsSafeHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("TLS TargetHost must match the configured QUIC authority.", nameof(tls));
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(HandshakeTimeout);
            QuicConnection connection = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
            {
                RemoteEndPoint = new DnsEndPoint(endpoint.DnsSafeHost, GetPort(endpoint)),
                ClientAuthenticationOptions = tls,
                MaxInboundBidirectionalStreams = 0,
                MaxInboundUnidirectionalStreams = 0,
                DefaultCloseErrorCode = BaselineError,
                DefaultStreamErrorCode = BaselineError,
#if NET9_0_OR_GREATER
                HandshakeTimeout = HandshakeTimeout,
#endif
                IdleTimeout = s_idleTimeout
            }, deadline.Token).ConfigureAwait(false);
            try
            {
                ValidateProtocol(connection);
                QuicStream stream = await connection.OpenOutboundStreamAsync(
                    QuicStreamType.Bidirectional, deadline.Token).ConfigureAwait(false);
                return new QuicUascConnection(connection, stream, endpoint, isInitiator: true);
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Listens on a numeric local authority with one inbound baseline stream per connection.
        /// Port zero requests an ephemeral UDP port. The returned listener does not own accepted connections.
        /// </summary>
        public async ValueTask<QuicUascListener> ListenAsync(
            Uri endpoint,
            SslServerAuthenticationOptions tls,
            CancellationToken cancellationToken = default)
        {
            ValidateEndpoint(endpoint, listen: true);
            ArgumentNullException.ThrowIfNull(tls);
            ValidateTls(tls.ApplicationProtocols, tls.EnabledSslProtocols);
            QuicListener listener = await QuicListener.ListenAsync(new QuicListenerOptions
            {
                ListenEndPoint = new IPEndPoint(IPAddress.Parse(endpoint.DnsSafeHost), GetPort(endpoint)),
                ListenBacklog = 16,
                ApplicationProtocols = [s_protocol],
                ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions
                {
                    ServerAuthenticationOptions = tls,
                    MaxInboundBidirectionalStreams = 1,
                    MaxInboundUnidirectionalStreams = 0,
                    DefaultCloseErrorCode = BaselineError,
                    DefaultStreamErrorCode = BaselineError,
#if NET9_0_OR_GREATER
                    HandshakeTimeout = HandshakeTimeout,
#endif
                    IdleTimeout = s_idleTimeout
                })
            }, cancellationToken).ConfigureAwait(false);
            var bound = new UriBuilder(endpoint) { Port = listener.LocalEndPoint.Port };
            return new QuicUascListener(listener, bound.Uri);
        }

        /// <summary>
        /// Refuses protocol fallback even after a successful TLS handshake.
        /// </summary>
        internal static void ValidateProtocol(QuicConnection connection)
        {
            if (connection.NegotiatedApplicationProtocol != s_protocol)
            {
                throw new AuthenticationException("The Part 31 baseline ALPN was not negotiated.");
            }
        }

        /// <summary>
        /// Validates the configured authority without substituting another transport or endpoint.
        /// </summary>
        private static void ValidateEndpoint(Uri endpoint, bool listen)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!endpoint.IsAbsoluteUri || endpoint.Scheme != "opc.quic" ||
                endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 ||
                (!listen && GetPort(endpoint) == 0) ||
                (listen && !IPAddress.TryParse(endpoint.DnsSafeHost, out _)))
            {
                throw new ArgumentException(
                    "An explicit opc.quic authority and resource are required.", nameof(endpoint));
            }
            if (!QuicConnection.IsSupported || !QuicListener.IsSupported)
            {
                throw new PlatformNotSupportedException("Managed QUIC is unavailable on this platform.");
            }
        }

        /// <summary>
        /// Requires exact draft selection and refuses a TLS version downgrade.
        /// </summary>
        private static void ValidateTls(
            System.Collections.Generic.List<SslApplicationProtocol>? protocols,
            SslProtocols versions)
        {
            if (protocols is not { Count: 1 } || protocols[0] != s_protocol ||
                versions != SslProtocols.None)
            {
                throw new ArgumentException(
                    "The Part 31 baseline requires opcua-uasc-draft and operating-system-selected QUIC TLS.");
            }
        }

        /// <summary>
        /// Gets the explicitly selected port or the OPC UA default.
        /// </summary>
        private static int GetPort(Uri endpoint)
        {
            return endpoint.Port < 0 ? 4840 : endpoint.Port;
        }

        /// <summary>The baseline transport error used for stream and connection termination.</summary>
        internal const long BaselineError = 0x100;

        /// <summary>The finite TLS and first-stream establishment deadline.</summary>
        internal static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);

        /// <summary>The finite transport idle deadline.</summary>
        private static readonly TimeSpan s_idleTimeout = TimeSpan.FromMinutes(1);

        /// <summary>The exact pinned Part 31 application protocol.</summary>
        private static readonly SslApplicationProtocol s_protocol = new(DataChannelProfiles.UascQuicAlpn);
    }

    /// <summary>
    /// Accepts one reliable baseline per QUIC connection without treating TLS as application authentication.
    /// </summary>
#if NET8_0
    [RequiresPreviewFeatures("System.Net.Quic is a preview API on .NET 8.")]
#endif
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    public sealed class QuicUascListener : IAsyncDisposable
    {
        /// <summary>
        /// Takes ownership of an established managed QUIC listener.
        /// </summary>
        internal QuicUascListener(QuicListener listener, Uri endpoint)
        {
            m_listener = listener;
            EndpointUrl = endpoint;
        }

        /// <summary>
        /// Gets the actual bound authority and configured resource for UACP HEL validation.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <summary>
        /// Accepts an initiator-created stream zero within a finite first-stream deadline.
        /// </summary>
        public async ValueTask<QuicUascConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            QuicConnection connection = await m_listener.AcceptConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                QuicUascTransport.ValidateProtocol(connection);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(QuicUascTransport.HandshakeTimeout);
                QuicStream stream = await connection.AcceptInboundStreamAsync(deadline.Token).ConfigureAwait(false);
                if (stream.Id != 0 || stream.Type != QuicStreamType.Bidirectional)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                    throw new AuthenticationException("The first QUIC stream must be the initiator's UASC baseline.");
                }
                return new QuicUascConnection(connection, stream, EndpointUrl, isInitiator: false);
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_listener.DisposeAsync();
        }

        /// <summary>The owned transport listener, independent of already accepted connections.</summary>
        private readonly QuicListener m_listener;
    }

    /// <summary>
    /// Owns a QUIC connection and its sole UASC baseline stream. Use as the external listener's byte
    /// factory or as a client channel factory. Close the channel before asynchronously disposing this owner.
    /// Direct streams, datagrams and exporter binding are deliberately not advertised by this baseline.
    /// </summary>
#if NET8_0
    [RequiresPreviewFeatures("System.Net.Quic is a preview API on .NET 8.")]
#endif
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    public sealed class QuicUascConnection :
        IUaSCByteTransportFactory, ITransportChannelFactory, IUaSCTransportProfile, IAsyncDisposable
    {
        /// <summary>
        /// Takes ownership after exact protocol and baseline-stream validation.
        /// </summary>
        internal QuicUascConnection(QuicConnection connection, QuicStream stream, Uri endpoint, bool isInitiator)
        {
            m_connection = connection;
            m_stream = stream;
            EndpointUrl = endpoint;
            m_isInitiator = isInitiator;
        }

        /// <summary>
        /// Gets the configured endpoint bound to the baseline HEL.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <inheritdoc/>
        public string Implementation => "UASC-QUIC";

        /// <inheritdoc/>
        public string UriScheme => "opc.quic";

        /// <inheritdoc/>
        public string TransportProfileUri => DataChannelProfiles.UascQuic;

        /// <inheritdoc/>
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (!m_isInitiator)
            {
                throw new InvalidOperationException("An accepted baseline cannot initiate another UASC channel.");
            }
            return new BaselineChannel(this, telemetry);
        }

        /// <inheritdoc/>
        public IUaSCByteTransport Create(
            BufferManager bufferManager, int receiveBufferSize, ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(bufferManager);
            ArgumentNullException.ThrowIfNull(telemetry);
            if (receiveBufferSize <= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(receiveBufferSize));
            }
            if (m_isInitiator)
            {
                return new UascStreamByteTransportFactory(ClaimStreamAsync, Implementation, TransportProfileUri)
                    .Create(bufferManager, receiveBufferSize, telemetry);
            }
            return new UascStreamByteTransport(
                ClaimStream(EndpointUrl, default), bufferManager, receiveBufferSize, Implementation,
                m_connection.LocalEndPoint, m_connection.RemoteEndPoint);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                try
                {
                    await m_stream.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    await m_connection.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Transfers baseline I/O once, without opening another stream on reconnect.
        /// </summary>
        private ValueTask<Stream> ClaimStreamAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<Stream>(ClaimStream(endpoint, cancellationToken));
        }

        /// <summary>
        /// Rejects endpoint substitution, reuse and cancellation before transferring stream ownership.
        /// </summary>
        private QuicStream ClaimStream(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (!EndpointUrl.Equals(endpoint) ||
                endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0)
            {
                throw new ServiceResultException(StatusCodes.BadTcpEndpointUrlInvalid);
            }
            if (Interlocked.Exchange(ref m_claimed, 1) != 0)
            {
                throw new InvalidOperationException("A new SecureChannel connection requires a fresh QUIC baseline.");
            }
            return m_stream;
        }

        /// <summary>
        /// Binds ordinary UASC settings to the exact Part 31 profile before HEL or OPN is sent.
        /// </summary>
        private sealed class BaselineChannel(QuicUascConnection owner, ITelemetryContext telemetry)
            : UaSCUaBinaryTransportChannel(owner, telemetry)
        {
            /// <inheritdoc/>
            protected override void OnSettingsSaved(TransportChannelSettings settings, ChannelQuotas quotas)
            {
                if (settings.Description?.TransportProfileUri != DataChannelProfiles.UascQuic ||
                    settings.Description.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                    !Uri.TryCreate(settings.Description.EndpointUrl, UriKind.Absolute, out Uri? endpoint) ||
                    !owner.EndpointUrl.Equals(endpoint))
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed,
                        "The QUIC baseline requires its exact endpoint, profile and SignAndEncrypt UASC.");
                }
            }
        }

        /// <summary>The independently owned managed QUIC connection.</summary>
        private readonly QuicConnection m_connection;

        /// <summary>The only stream used for UACP, UASC, Services, FLO and FLC.</summary>
        private readonly QuicStream m_stream;

        /// <summary>The transport role, independent of Publisher/Subscriber roles.</summary>
        private readonly bool m_isInitiator;

        /// <summary>The atomic single-baseline ownership guard.</summary>
        private int m_claimed;

        /// <summary>The idempotent asynchronous disposal guard.</summary>
        private int m_disposed;
    }

    /// <summary>
    /// Registers the managed baseline independently of the legacy native direct-flow backend.
    /// </summary>
    public static class QuicUascBuilderExtensions
    {
        /// <summary>
        /// Adds an injectable Part 31 baseline factory without creating credentials or advertising endpoints.
        /// </summary>
#if NET8_0
        [RequiresPreviewFeatures("System.Net.Quic is a preview API on .NET 8.")]
#endif
        [SupportedOSPlatform("windows")]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("osx")]
        public static IOpcUaBuilder AddQuicUasc(this IOpcUaBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.TryAddSingleton<QuicUascTransport>();
            return builder;
        }
    }
}
#endif
