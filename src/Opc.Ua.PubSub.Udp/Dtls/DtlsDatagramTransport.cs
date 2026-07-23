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
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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

        /// <inheritdoc/>
        public async ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClearAuthenticatedPeer();
            IDtlsContext context = await m_contextFactory.CreateAsync(
                m_connection,
                Endpoint,
                Profile,
                m_telemetry,
                m_timeProvider,
                cancellationToken).ConfigureAwait(false);
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
        /// Opens the UDP socket and runs the subscriber-side DTLS 1.3 handshake.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public ValueTask AcceptAsync(CancellationToken cancellationToken = default)
        {
            if ((Direction & PubSubTransportDirection.Receive) != PubSubTransportDirection.Receive)
            {
                throw new InvalidOperationException("DTLS AcceptAsync requires a receive-capable PubSub transport.");
            }

            return OpenAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
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
            IDtlsContext context = GetContext();
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
            IDtlsContext? context = m_context;
            m_context = null;
            ClearAuthenticatedPeer();
            context?.Dispose();
            await m_innerTransport.DisposeAsync().ConfigureAwait(false);
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
        private IDtlsContext? m_context;
        private IPEndPoint? m_authenticatedPeer;
    }
}
