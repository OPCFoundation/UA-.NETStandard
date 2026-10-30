// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Net;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Explicitly selects native QUIC for the Part 31 UACP/UASC baseline without replacing managed QUIC.
    /// Server-authenticated TLS supplies transport security, not OPC UA application authentication.
    /// </summary>
    public sealed class NativeQuicUascTransport
    {
        /// <summary>
        /// Creates an injectable native baseline with finite, application-accounted transport limits.
        /// </summary>
        public NativeQuicUascTransport(
            INativeQuicBackend backend,
            DataChannelApplicationBudget applicationBudget,
            QuicDataChannelOptions options,
            TimeProvider? timeProvider = null)
        {
            m_backend = backend ?? throw new ArgumentNullException(nameof(backend));
            ArgumentNullException.ThrowIfNull(applicationBudget);
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
            m_options = options with { ApplicationBudget = options.ApplicationBudget ?? applicationBudget };
            m_clock = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Establishes exact Part 31 ALPN and reserves the initiator's bidirectional stream zero for UASC.
        /// The first application bytes are supplied by the ordinary UACP pipeline, not a native envelope.
        /// </summary>
        public async ValueTask<NativeQuicUascConnection> ConnectAsync(
            Uri endpoint,
            NativeQuicCredentials tlsCredentials,
            CancellationToken credentialsInvalidated = default,
            CancellationToken cancellationToken = default)
        {
            ValidateEndpoint(endpoint, listen: false);
            ArgumentNullException.ThrowIfNull(tlsCredentials);
            if (!tlsCredentials.IsTransportOnly || !tlsCredentials.CertificateDer.IsEmpty)
            {
                throw new ArgumentException(
                    "The native UASC client requires transport-only server trust.", nameof(tlsCredentials));
            }
            cancellationToken.ThrowIfCancellationRequested();
            credentialsInvalidated.ThrowIfCancellationRequested();
            DataChannelApplicationBudget.DataChannelCarrierReservation? reservation =
                QuicDataChannelConnection.ReserveNativeCapacity(m_options);
            NativeQuicConnection? connection = null;
            try
            {
                string target = endpoint.DnsSafeHost.Contains(':', StringComparison.Ordinal)
                    ? $"[{endpoint.DnsSafeHost}]:{GetPort(endpoint)}"
                    : $"{endpoint.DnsSafeHost}:{GetPort(endpoint)}";
                connection = await NativeQuicConnection.ConnectTlsAsync(
                    m_backend, tlsCredentials, target, DataChannelProfiles.UascQuicAlpn,
                    endpoint.DnsSafeHost, m_options.Native, m_clock,
                    credentialsInvalidated, cancellationToken).ConfigureAwait(false);
                NativeQuicUascConnection baseline = await NativeQuicUascConnection.EstablishAsync(
                    connection, endpoint, reservation, m_options, m_clock, cancellationToken).ConfigureAwait(false);
                reservation = null;
                return baseline;
            }
            catch
            {
                if (connection != null)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
            finally
            {
                reservation?.Dispose();
            }
        }

        /// <summary>
        /// Binds the numeric endpoint using host-supplied TLS credentials; port zero selects an ephemeral port.
        /// Accepted connections remain independently owned when this listener closes.
        /// </summary>
        public async ValueTask<NativeQuicUascListener> ListenAsync(
            Uri endpoint,
            NativeQuicCredentials tlsCredentials,
            CancellationToken credentialsInvalidated = default,
            CancellationToken cancellationToken = default)
        {
            ValidateEndpoint(endpoint, listen: true);
            ArgumentNullException.ThrowIfNull(tlsCredentials);
            NativeQuicListener listener = await NativeQuicListener.ListenTlsAsync(
                m_backend, tlsCredentials,
                new IPEndPoint(IPAddress.Parse(endpoint.DnsSafeHost), GetPort(endpoint)).ToString(),
                DataChannelProfiles.UascQuicAlpn, m_options.Native, m_clock,
                credentialsInvalidated, cancellationToken).ConfigureAwait(false);
            try
            {
                if (!IPEndPoint.TryParse(listener.Address, out IPEndPoint? bound))
                {
                    throw new InvalidDataException("The native listener did not return a numeric bound address.");
                }
                Uri actual = new UriBuilder(endpoint) { Port = bound.Port }.Uri;
                return new NativeQuicUascListener(listener, actual, m_options, m_clock);
            }
            catch
            {
                await listener.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Rejects protocol and authority substitution before native admission.
        /// </summary>
        private static void ValidateEndpoint(Uri endpoint, bool listen)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!endpoint.IsAbsoluteUri || endpoint.Scheme != "opc.quic" ||
                endpoint.Host.Length == 0 ||
                endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 ||
                (!listen && GetPort(endpoint) == 0) ||
                (listen && !IPAddress.TryParse(endpoint.DnsSafeHost, out _)))
            {
                throw new ArgumentException("An explicit opc.quic authority and resource are required.",
                    nameof(endpoint));
            }
        }

        /// <summary>
        /// Uses the OPC UA default only when no explicit port is configured.
        /// </summary>
        private static int GetPort(Uri endpoint) => endpoint.Port < 0 ? 4840 : endpoint.Port;

        /// <summary>The injected native ABI owner; never created implicitly by baseline consumers.</summary>
        private readonly INativeQuicBackend m_backend;

        /// <summary>The immutable local limits and shared application budget.</summary>
        private readonly QuicDataChannelOptions m_options;

        /// <summary>The injected deadline and TLS-policy clock.</summary>
        private readonly TimeProvider m_clock;
    }

    /// <summary>
    /// Accepts exactly one initiator-created UASC baseline per native QUIC connection.
    /// </summary>
    public sealed class NativeQuicUascListener : IAsyncDisposable
    {
        /// <summary>
        /// Takes ownership of a native listener after validating its actual bound endpoint.
        /// </summary>
        internal NativeQuicUascListener(
            NativeQuicListener listener, Uri endpoint, QuicDataChannelOptions options, TimeProvider clock)
        {
            m_listener = listener;
            EndpointUrl = endpoint;
            m_options = options;
            m_clock = clock;
        }

        /// <summary>
        /// Gets the actual authority and configured resource required by UACP HEL.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <summary>
        /// Accepts stream zero within a finite first-stream deadline after transport-only TLS validation.
        /// </summary>
        public async ValueTask<NativeQuicUascConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DataChannelApplicationBudget.DataChannelCarrierReservation? reservation =
                QuicDataChannelConnection.ReserveNativeCapacity(m_options);
            NativeQuicConnection? connection = null;
            try
            {
                connection = await m_listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                NativeQuicUascConnection baseline = await NativeQuicUascConnection.EstablishAsync(
                    connection, EndpointUrl, reservation, m_options, m_clock, cancellationToken).ConfigureAwait(false);
                reservation = null;
                return baseline;
            }
            catch
            {
                if (connection != null)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
            finally
            {
                reservation?.Dispose();
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => m_listener.DisposeAsync();

        /// <summary>The listener owner, independent of accepted connections.</summary>
        private readonly NativeQuicListener m_listener;

        /// <summary>The finite transport limits and shared accounting.</summary>
        private readonly QuicDataChannelOptions m_options;

        /// <summary>The injected first-stream deadline clock.</summary>
        private readonly TimeProvider m_clock;
    }

    /// <summary>
    /// Owns a native QUIC connection and its unique UACP/UASC byte stream.
    /// Independent SignAndEncrypt UASC authentication remains mandatory.
    /// </summary>
    public sealed partial class NativeQuicUascConnection :
        IUaSCByteTransportFactory, ITransportChannelFactory, IUaSCTransportProfile,
        IDataChannelDirectBindingProvider, IDataChannelDirectStreamProvider,
        IDataChannelDirectDatagramProvider, IAsyncDisposable
    {
        /// <summary>
        /// Takes ownership only after exact ALPN, transport role and stream-zero validation.
        /// </summary>
        private NativeQuicUascConnection(
            NativeQuicConnection connection,
            NativeQuicStream stream,
            Uri endpoint,
            DataChannelApplicationBudget.DataChannelCarrierReservation reservation,
            QuicDataChannelOptions options,
            TimeProvider clock)
        {
            m_connection = connection;
            m_stream = new NativeQuicByteStream(stream);
            EndpointUrl = endpoint;
            m_reservation = reservation;
            m_options = options;
            m_clock = clock;
            m_directAcceptTask = Task.WhenAll(
                Task.Run(() => AcceptDirectStreamsAsync(unidirectional: false)),
                Task.Run(() => AcceptDirectStreamsAsync(unidirectional: true)));
            m_baselineClosed = stream.Closed.Register(() => m_connection.RequestClose(0, false));
        }

        /// <summary>
        /// Gets the configured endpoint, including the exact resource and query.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <inheritdoc/>
        public string Implementation => "Native-UASC-QUIC";

        /// <inheritdoc/>
        public string UriScheme => "opc.quic";

        /// <inheritdoc/>
        public string TransportProfileUri => DataChannelProfiles.UascQuic;

        /// <summary>
        /// Binds the sole initiator-created baseline without emitting private negotiation bytes.
        /// Ownership transfers only on success.
        /// </summary>
        internal static async ValueTask<NativeQuicUascConnection> EstablishAsync(
            NativeQuicConnection connection,
            Uri endpoint,
            DataChannelApplicationBudget.DataChannelCarrierReservation reservation,
            QuicDataChannelOptions options,
            TimeProvider clock,
            CancellationToken cancellationToken)
        {
            if (connection.TlsPeer.ApplicationProtocol != DataChannelProfiles.UascQuicAlpn)
            {
                throw new AuthenticationException("The native Part 31 baseline ALPN was not negotiated.");
            }
            using var deadline = new CancellationTokenSource(options.EstablishmentTimeout, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, deadline.Token, connection.Closed);
            NativeQuicStream stream = connection.IsClient
                ? await connection.OpenStreamAsync(cancellationToken: linked.Token).ConfigureAwait(false)
                : await connection.AcceptStreamAsync(cancellationToken: linked.Token).ConfigureAwait(false);
            if (stream.StreamId != 0 || !stream.IsBidirectional || stream.IsLocal != connection.IsClient)
            {
                throw new AuthenticationException("The first native stream must be the initiator's UASC baseline.");
            }
            return new NativeQuicUascConnection(connection, stream, endpoint, reservation, options, clock);
        }

        /// <inheritdoc/>
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (!m_connection.IsClient)
            {
                throw new InvalidOperationException("An accepted native baseline cannot initiate a UASC channel.");
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
            if (m_connection.IsClient)
            {
                return new UascStreamByteTransportFactory(ClaimStreamAsync, Implementation, TransportProfileUri, this)
                    .Create(bufferManager, receiveBufferSize, telemetry);
            }
            return new UascStreamByteTransport(
                ClaimStream(EndpointUrl, default), bufferManager, receiveBufferSize, Implementation,
                directBindingProvider: this);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_gate)
            {
                Volatile.Write(ref m_disposed, 1);
                return new ValueTask(m_close ??= CloseCoreAsync());
            }
        }

        /// <summary>
        /// Transfers the actual byte stream, never a newly opened stream on reconnect.
        /// </summary>
        internal ValueTask<Stream> ClaimStreamAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<Stream>(ClaimStream(endpoint, cancellationToken));
        }

        /// <summary>
        /// Joins every native owner before releasing the application's reserved capacity.
        /// </summary>
        private async Task CloseCoreAsync()
        {
            try
            {
                await m_stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await m_connection.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await m_directAcceptTask.ConfigureAwait(false);
                    }
                    finally
                    {
                        lock (m_gate)
                        {
                            m_directAdmissions.Clear();
                            m_directTokens.Clear();
                        }
                        m_baselineClosed.Dispose();
                        m_directHistoryReservation?.Dispose();
                        m_reservation.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Preserves exact endpoint selection, cancellation and single-baseline ownership.
        /// </summary>
        private NativeQuicByteStream ClaimStream(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (!EndpointUrl.Equals(endpoint) || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0)
            {
                throw new ServiceResultException(StatusCodes.BadTcpEndpointUrlInvalid);
            }
            if (Interlocked.Exchange(ref m_claimed, 1) != 0)
            {
                throw new InvalidOperationException("A native UASC baseline can be claimed only once.");
            }
            return m_stream;
        }

        /// <summary>
        /// Requires independent UASC security and exact profile/endpoint selection before HEL or OPN.
        /// </summary>
        private sealed class BaselineChannel(NativeQuicUascConnection owner, ITelemetryContext telemetry)
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
                        "The native QUIC baseline requires its exact endpoint, profile and SignAndEncrypt UASC.");
                }
            }
        }

        /// <summary>The actual TLS-only QUIC connection owner.</summary>
        private readonly NativeQuicConnection m_connection;

        /// <summary>The sole baseline stream and bounded byte adapter.</summary>
        private readonly NativeQuicByteStream m_stream;

        /// <summary>The application reservation retained until native drainage finishes.</summary>
        private readonly DataChannelApplicationBudget.DataChannelCarrierReservation m_reservation;

        /// <summary>Baseline loss invalidates every direct association on the same connection.</summary>
        private readonly CancellationTokenRegistration m_baselineClosed;

        /// <summary>The finite native and direct-association bounds.</summary>
        private readonly QuicDataChannelOptions m_options;

        /// <summary>The injected direct-admission deadline clock.</summary>
        private readonly TimeProvider m_clock;

        /// <summary>Serializes publication of the shared close completion.</summary>
        private readonly System.Threading.Lock m_gate = new();

        /// <summary>The single asynchronously joined close operation.</summary>
        private Task? m_close;

        /// <summary>The single byte-stream transfer guard.</summary>
        private int m_claimed;

        /// <summary>The asynchronous owner disposal guard.</summary>
        private int m_disposed;
    }

    /// <summary>
    /// Registers explicit native baseline/direct selection without changing managed QUIC registration.
    /// </summary>
    public static class NativeQuicUascBuilderExtensions
    {
        /// <summary>
        /// Adds the injectable native transport; the host supplies its backend, shared budget and TLS credentials.
        /// </summary>
        public static IOpcUaBuilder AddNativeQuicUasc(this IOpcUaBuilder builder, QuicDataChannelOptions options)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
            builder.Services.TryAddSingleton(options);
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.TryAddSingleton<NativeQuicUascTransport>();
            return builder;
        }
    }
}
