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
using System.Buffers;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Eth
{
    /// <summary>
    /// Ethernet (Layer 2) datagram <see cref="IPubSubTransport"/>
    /// implementation. One instance corresponds to one
    /// <see cref="PubSubConnectionDataType"/> bound to an
    /// <c>opc.eth://</c> address: it owns an
    /// <see cref="IEthernetFrameChannel"/>, builds and parses Ethernet II
    /// frames (EtherType 0xB62C, optional 802.1Q tag), and drives the
    /// receive loop.
    /// </summary>
    /// <remarks>
    /// Implements the OPC UA Part 14 Ethernet mapping. The transport
    /// owns framing while the injected <see cref="IEthernetFrameChannel"/>
    /// owns the platform I/O, so the same transport works over the native
    /// AF_PACKET / BPF backends, the SharpPcap backend, and the in-memory
    /// loopback backend without change.
    /// </remarks>
    public sealed class EthernetDatagramTransport
        : IPubSubTransport, IPubSubDiscoveryAnnouncementTransport
    {
        private readonly PubSubConnectionDataType m_connection;
        private readonly EthEndpoint m_endpoint;
        private readonly PubSubTransportDirection m_direction;
        private readonly IEthernetFrameChannel m_channel;
        private readonly EthTransportOptions m_options;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly byte[] m_destinationMac;
        private readonly byte[] m_discoveryMac;
        private readonly System.Threading.Lock m_sync = new();

        private Channel<PubSubTransportFrame>? m_frameChannel;
        private CancellationTokenSource? m_receiveLoopCts;
        private Task? m_receiveLoopTask;
        private bool m_isConnected;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="EthernetDatagramTransport"/>.
        /// </summary>
        /// <param name="connection">PubSubConnection configuration.</param>
        /// <param name="endpoint">Parsed Ethernet endpoint.</param>
        /// <param name="direction">Direction the transport services.</param>
        /// <param name="channel">The frame channel (not yet open).</param>
        /// <param name="options">Transport tunables.</param>
        /// <param name="telemetry">Telemetry context for logging.</param>
        /// <param name="timeProvider">Clock for receive timestamps.</param>
        public EthernetDatagramTransport(
            PubSubConnectionDataType connection,
            EthEndpoint endpoint,
            PubSubTransportDirection direction,
            IEthernetFrameChannel channel,
            EthTransportOptions options,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            m_connection = connection ?? throw new ArgumentNullException(nameof(connection));
            m_channel = channel ?? throw new ArgumentNullException(nameof(channel));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (!endpoint.IsValid)
            {
                throw new ArgumentException("Ethernet endpoint is not valid.", nameof(endpoint));
            }
            m_endpoint = endpoint;
            m_direction = direction;
            m_logger = telemetry.CreateLogger<EthernetDatagramTransport>();
            m_destinationMac = endpoint.Address.GetAddressBytes();
            m_discoveryMac = ResolveDiscoveryMac(options.DiscoveryMulticastAddress, m_destinationMac);
        }

        /// <inheritdoc/>
        public string TransportProfileUri => EthProfiles.PubSubEthUadpTransport;

        /// <inheritdoc/>
        public PubSubTransportDirection Direction => m_direction;

        /// <inheritdoc/>
        public bool IsConnected
        {
            get
            {
                lock (m_sync)
                {
                    return m_isConnected;
                }
            }
        }

        /// <inheritdoc/>
        public uint DiscoveryAnnounceRate => m_options.DiscoveryAnnounceRate;

        /// <inheritdoc/>
        public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged;

        /// <inheritdoc/>
        public async ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(EthernetDatagramTransport));
                }
                if (m_isConnected)
                {
                    return;
                }
            }
            await m_channel.OpenAsync(cancellationToken).ConfigureAwait(false);
            lock (m_sync)
            {
                if (HasReceiveDirection)
                {
                    m_frameChannel = Channel.CreateBounded<PubSubTransportFrame>(
                        new BoundedChannelOptions(Math.Max(1, m_options.ReceiveQueueCapacity))
                        {
                            FullMode = BoundedChannelFullMode.DropOldest,
                            SingleReader = false,
                            SingleWriter = true
                        });
                    m_receiveLoopCts = CancellationTokenSource.CreateLinkedTokenSource(
                        CancellationToken.None);
                    CancellationToken loopToken = m_receiveLoopCts.Token;
                    m_receiveLoopTask = Task.Run(
                        () => ReceiveLoopAsync(loopToken), CancellationToken.None);
                }
                m_isConnected = true;
            }
            m_logger.LogInformation(
                "Ethernet transport opened: connection='{Connection}' destination={Mac} direction={Direction}",
                m_connection.Name,
                m_endpoint.Address,
                m_direction);
            RaiseStateChanged(true, StatusCodes.Good, null);
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            CancellationTokenSource? loopCts;
            Task? loopTask;
            Channel<PubSubTransportFrame>? channel;
            bool wasConnected;
            lock (m_sync)
            {
                loopCts = m_receiveLoopCts;
                loopTask = m_receiveLoopTask;
                channel = m_frameChannel;
                wasConnected = m_isConnected;
                m_receiveLoopCts = null;
                m_receiveLoopTask = null;
                m_frameChannel = null;
                m_isConnected = false;
            }
            loopCts?.Cancel();
            await m_channel.CloseAsync(cancellationToken).ConfigureAwait(false);
            if (loopTask is not null)
            {
                try
                {
                    await loopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    m_logger.LogDebug(ex,
                        "Ethernet receive loop terminated with exception for connection '{Connection}'.",
                        m_connection.Name);
                }
            }
            channel?.Writer.TryComplete();
            loopCts?.Dispose();
            if (wasConnected)
            {
                RaiseStateChanged(false, StatusCodes.Good, "Transport closed.");
            }
        }

        /// <inheritdoc/>
        public ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            string? topic = null,
            CancellationToken cancellationToken = default)
        {
            _ = topic;
            cancellationToken.ThrowIfCancellationRequested();
            EnsureConnected();
            return SendToAsync(m_destinationMac, payload, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask SendDiscoveryAnnouncementAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureConnected();
            return SendToAsync(m_discoveryMac, payload, cancellationToken);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Channel<PubSubTransportFrame>? channel;
            lock (m_sync)
            {
                channel = m_frameChannel;
            }
            if (channel is null)
            {
                yield break;
            }
            await foreach (PubSubTransportFrame frame in channel.Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return frame;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await CloseAsync().ConfigureAwait(false);
            await m_channel.DisposeAsync().ConfigureAwait(false);
            lock (m_sync)
            {
                m_disposed = true;
            }
        }

        private async ValueTask SendToAsync(
            byte[] destinationMac,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            ushort? vlanId = m_endpoint.VlanId ?? m_options.DefaultVlanId;
            byte? priority = m_endpoint.Priority ?? m_options.DefaultPriority;
            bool tagged = vlanId.HasValue || priority.HasValue;
            int required = EthernetFrameCodec.GetRequiredLength(payload.Length, tagged);
            if (required > m_options.MaxFrameSize)
            {
                throw new InvalidOperationException(
                    $"Encoded Ethernet frame ({required} octets) exceeds MaxFrameSize " +
                    $"({m_options.MaxFrameSize}). Enable UADP chunking or raise the MTU / MaxFrameSize.");
            }
            byte[] buffer = ArrayPool<byte>.Shared.Rent(required);
            try
            {
                int written = EthernetFrameCodec.Build(
                    buffer,
                    destinationMac,
                    m_channel.InterfaceAddress.GetAddressBytes(),
                    vlanId,
                    priority,
                    payload.Span);
                await m_channel
                    .SendFrameAsync(buffer.AsMemory(0, written), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (ReadOnlyMemory<byte> raw in m_channel
                    .ReceiveFramesAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (!EthernetFrameCodec.TryParse(raw.Span, out int payloadOffset, out _, out _))
                    {
                        continue;
                    }
                    byte[] copy = raw.Slice(payloadOffset).ToArray();
                    var frame = new PubSubTransportFrame(
                        copy,
                        topic: null,
                        receivedAt: new DateTimeUtc(m_timeProvider.GetUtcNow().UtcDateTime),
                        sourceEndpoint: null);
                    Channel<PubSubTransportFrame>? channel;
                    lock (m_sync)
                    {
                        channel = m_frameChannel;
                    }
                    channel?.Writer.TryWrite(frame);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(ex,
                    "Ethernet receive loop failed for connection '{Connection}'.",
                    m_connection.Name);
            }
        }

        private void EnsureConnected()
        {
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(EthernetDatagramTransport));
                }
                if (!m_isConnected)
                {
                    throw new InvalidOperationException("Ethernet transport is not open.");
                }
            }
        }

        private bool HasReceiveDirection => (m_direction & PubSubTransportDirection.Receive) != 0;

        private void RaiseStateChanged(bool connected, StatusCode status, string? reason)
        {
            EventHandler<PubSubTransportStateChangedEventArgs>? handler = StateChanged;
            handler?.Invoke(this, new PubSubTransportStateChangedEventArgs(connected, status, reason));
        }

        private static byte[] ResolveDiscoveryMac(string? configured, byte[] fallback)
        {
            if (string.IsNullOrEmpty(configured))
            {
                return fallback;
            }
            try
            {
                EthEndpoint parsed = EthEndpointParser.Parse(
                    string.Concat("opc.eth://", configured));
                return parsed.Address.GetAddressBytes();
            }
            catch (FormatException)
            {
                return fallback;
            }
        }
    }
}
