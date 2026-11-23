// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// DTLS wrapper around the UDP datagram transport for Part 14 §7.3.2.4 unicast PubSub.
    /// </summary>
    public sealed class DtlsDatagramTransport :
        IPubSubTransport,
        IDtlsDatagramChannel,
        IDtlsAuthenticatedPeerChannel
    {
        /// <summary>
        /// Initializes a new <see cref="DtlsDatagramTransport"/>.
        /// </summary>
        public DtlsDatagramTransport(
            PubSubConnectionDataType connection,
            UdpEndpoint endpoint,
            PubSubTransportDirection direction,
            NetworkInterface? networkInterface,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            UdpTransportOptions udpOptions,
            IPubSubDiagnostics? diagnostics,
            IDtlsContextFactory contextFactory,
            DtlsProfile profile)
        {
            if (udpOptions is null)
            {
                throw new ArgumentNullException(nameof(udpOptions));
            }

            m_connection = connection ?? throw new ArgumentNullException(nameof(connection));
            Direction = direction;
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            m_logger = telemetry.CreateLogger<DtlsDatagramTransport>();
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            PubSubTransportDirection innerDirection = direction == PubSubTransportDirection.Send
                ? PubSubTransportDirection.SendReceive
                : direction | PubSubTransportDirection.Send;
            m_innerTransport = new UdpDatagramTransport(
                connection,
                endpoint,
                innerDirection,
                networkInterface,
                telemetry,
                timeProvider,
                udpOptions,
                diagnostics,
                useConnectedUnicastClient: direction == PubSubTransportDirection.Send,
                trackLastSeenUnicastPeer: false);
        }

        /// <inheritdoc/>
        public string TransportProfileUri => m_innerTransport.TransportProfileUri;

        /// <inheritdoc/>
        public PubSubTransportDirection Direction { get; }

        /// <inheritdoc/>
        public bool IsConnected => m_innerTransport.IsConnected;

        /// <summary>
        /// Parsed DTLS endpoint.
        /// </summary>
        public UdpEndpoint Endpoint => m_innerTransport.Endpoint;

        /// <inheritdoc/>
        public IPEndPoint? RemoteEndpoint => m_innerTransport.RemoteEndpoint;

        /// <summary>
        /// Resolved DTLS profile.
        /// </summary>
        public DtlsProfile Profile { get; }

        /// <inheritdoc/>
        public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
        {
            add => m_innerTransport.StateChanged += value;
            remove => m_innerTransport.StateChanged -= value;
        }

        /// <summary>
        /// Opens the UDP socket. On the DTLS client (publisher) side the handshake runs
        /// before this method returns. On the DTLS server (subscriber) side the handshake
        /// is not run here: it runs when <see cref="ReceiveAsync"/> is first enumerated and
        /// is retried until it succeeds or the receive is cancelled, so a publisher that is
        /// not running yet, or a host on the network that sends stray datagrams or stalls a
        /// handshake, cannot fail the start of the connection and its application.
        /// </summary>
        public async ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClearAuthenticatedPeer();
            m_closed = false;
            if (DefaultDtlsContextFactory.DetermineRole(m_connection) == DtlsEndpointRole.Server)
            {
                await m_innerTransport.OpenAsync(cancellationToken).ConfigureAwait(false);
                m_acceptPending = true;
                return;
            }

            IDtlsContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
            m_context = context;
            await m_innerTransport.OpenAsync(cancellationToken).ConfigureAwait(false);
            await context.OpenAsync(this, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens the UDP socket and runs the publisher-side DTLS 1.3 handshake.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            if ((Direction & PubSubTransportDirection.Send) != PubSubTransportDirection.Send)
            {
                throw new InvalidOperationException("DTLS ConnectAsync requires a send-capable PubSub transport.");
            }

            return OpenAsync(cancellationToken);
        }

        /// <summary>
        /// Opens the UDP socket and runs one subscriber-side DTLS 1.3 handshake. Unlike the
        /// handshake that <see cref="ReceiveAsync"/> runs, a failed attempt is not retried:
        /// the exception is thrown to the caller.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask AcceptAsync(CancellationToken cancellationToken = default)
        {
            if ((Direction & PubSubTransportDirection.Receive) != PubSubTransportDirection.Receive)
            {
                throw new InvalidOperationException("DTLS AcceptAsync requires a receive-capable PubSub transport.");
            }

            await OpenAsync(cancellationToken).ConfigureAwait(false);
            if (m_acceptPending)
            {
                await AcceptOnceAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            m_closed = true;
            m_acceptPending = false;
            IDtlsContext? context = m_context;
            m_context = null;
            ClearAuthenticatedPeer();
            context?.Dispose();
            await m_innerTransport.CloseAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            string? topic = null,
            CancellationToken cancellationToken = default)
        {
            IDtlsContext context = GetContext();
            ReadOnlyMemory<byte> record = await context.ProtectAsync(payload, cancellationToken).ConfigureAwait(false);
            await m_innerTransport.SendAsync(record, topic, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            IDtlsContext context = m_acceptPending
                ? await AcceptWithRetryAsync(cancellationToken).ConfigureAwait(false)
                : GetContext();
            await foreach (PubSubTransportFrame frame in m_innerTransport.ReceiveAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (!IsAuthenticatedPeer(frame.SourceEndpoint))
                {
                    // RFC 9147 §5.1: without a negotiated connection ID, a
                    // record from another address is not part of this association.
                    // Filter before record protection so redirected ciphertext
                    // cannot consume the authenticated peer's replay sequence.
                    continue;
                }

                ReadOnlyMemory<byte> payload;
                try
                {
                    payload = await context.UnprotectAsync(frame.Payload, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    // RFC 9147 §4.5.2: malformed, forged or replayed application records are
                    // silently dropped so a forged datagram cannot tear down the transport.
                    continue;
                }
                yield return new PubSubTransportFrame(
                    payload,
                    frame.Topic,
                    frame.ReceivedAt,
                    frame.SourceEndpoint);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_closed = true;
            m_acceptPending = false;
            IDtlsContext? context = m_context;
            m_context = null;
            ClearAuthenticatedPeer();
            context?.Dispose();
            await m_innerTransport.DisposeAsync().ConfigureAwait(false);
        }

        private ValueTask<IDtlsContext> CreateContextAsync(CancellationToken cancellationToken)
        {
            return m_contextFactory.CreateAsync(
                m_connection,
                Endpoint,
                Profile,
                m_telemetry,
                m_timeProvider,
                cancellationToken);
        }

        /// <summary>
        /// Runs one server-side handshake with a fresh context and installs the context
        /// when the handshake completes.
        /// </summary>
        private async ValueTask<IDtlsContext> AcceptOnceAsync(CancellationToken cancellationToken)
        {
            ClearAuthenticatedPeer();
            IDtlsContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await context.OpenAsync(this, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                ClearAuthenticatedPeer();
                context.Dispose();
                throw;
            }

            if (m_closed)
            {
                context.Dispose();
                throw new ObjectDisposedException(nameof(DtlsDatagramTransport));
            }

            m_context = context;
            m_acceptPending = false;
            return context;
        }

        /// <summary>
        /// Runs server-side handshakes until one completes. A failed or timed-out
        /// handshake is logged and the server listens again after a back-off, so
        /// neither a missing publisher nor a host that disturbs the handshake ends
        /// the receive path for good.
        /// </summary>
        private async ValueTask<IDtlsContext> AcceptWithRetryAsync(CancellationToken cancellationToken)
        {
            int failures = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (m_closed)
                {
                    throw new ObjectDisposedException(nameof(DtlsDatagramTransport));
                }

                try
                {
                    return await AcceptOnceAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (!m_closed)
                {
                    failures++;
                    m_logger.DtlsAcceptFailed(ex, m_connection.Name, failures, GetAcceptRetryDelay(failures));
                }

                await m_timeProvider.Delay(GetAcceptRetryDelay(failures), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private static TimeSpan GetAcceptRetryDelay(int failures)
        {
            if (failures <= 0)
            {
                return TimeSpan.Zero;
            }

            double milliseconds = s_initialAcceptRetryDelay.TotalMilliseconds *
                Math.Pow(2, Math.Min(failures - 1, 16));
            return TimeSpan.FromMilliseconds(Math.Min(milliseconds, s_maxAcceptRetryDelay.TotalMilliseconds));
        }

        private IDtlsContext GetContext()
        {
            return m_context ??
                throw new InvalidOperationException(
                    "DTLS transport must be opened before protected datagrams can flow.");
        }

        /// <inheritdoc/>
        async ValueTask IDtlsDatagramChannel.SendAsync(
            ReadOnlyMemory<byte> datagram,
            IPEndPoint? destination,
            CancellationToken cancellationToken)
        {
            await m_innerTransport.SendToAsync(datagram, destination, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        async ValueTask<DtlsDatagram> IDtlsDatagramChannel.ReceiveAsync(CancellationToken cancellationToken)
        {
            await foreach (PubSubTransportFrame frame in m_innerTransport.ReceiveAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                return new DtlsDatagram(frame.Payload, frame.SourceEndpoint);
            }

            throw new InvalidOperationException("DTLS datagram channel closed while waiting for a handshake datagram.");
        }

        void IDtlsAuthenticatedPeerChannel.SetAuthenticatedPeer(IPEndPoint peer)
        {
            SetAuthenticatedPeer(peer);
        }

        private bool IsAuthenticatedPeer(IPEndPoint? source)
        {
            if (source is null)
            {
                return false;
            }

            lock (m_peerLock)
            {
                return m_authenticatedPeer is not null &&
                    m_authenticatedPeer.Equals(source);
            }
        }

        private void SetAuthenticatedPeer(IPEndPoint peer)
        {
            if (peer is null)
            {
                throw new ArgumentNullException(nameof(peer));
            }

            lock (m_peerLock)
            {
                if (m_authenticatedPeer is not null &&
                    !m_authenticatedPeer.Equals(peer))
                {
                    throw new InvalidOperationException(
                        "A connection-ID-less DTLS association cannot change its authenticated peer endpoint.");
                }

                m_authenticatedPeer ??= new IPEndPoint(peer.Address, peer.Port);
                m_innerTransport.SetAuthenticatedRemoteEndpoint(m_authenticatedPeer);
            }
        }

        private void ClearAuthenticatedPeer()
        {
            lock (m_peerLock)
            {
                m_authenticatedPeer = null;
            }
        }

        /// <summary>
        /// PubSub connection descriptor backing the DTLS transport.
        /// </summary>
        private readonly PubSubConnectionDataType m_connection;
        private readonly UdpDatagramTransport m_innerTransport;
        private readonly IDtlsContextFactory m_contextFactory;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_timeProvider;
        private readonly Lock m_peerLock = new();
        private readonly ILogger m_logger;
        private IDtlsContext? m_context;
        private IPEndPoint? m_authenticatedPeer;
        private volatile bool m_acceptPending;
        private volatile bool m_closed;
        private static readonly TimeSpan s_initialAcceptRetryDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan s_maxAcceptRetryDelay = TimeSpan.FromSeconds(30);
    }

    internal static partial class DtlsDatagramTransportLog
    {
        [LoggerMessage(EventId = PubSubUdpEventIds.DtlsDatagramTransport + 0,
            Level = LogLevel.Warning,
            Message = "DTLS handshake on connection '{Connection}' did not complete (attempt {Attempt}); " +
                "listening again in {RetryDelay}.")]
        public static partial void DtlsAcceptFailed(
            this ILogger logger,
            Exception exception,
            string? connection,
            int attempt,
            TimeSpan retryDelay);
    }
}
