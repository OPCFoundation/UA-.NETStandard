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
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Udp
{
    /// <summary>
    /// UDP datagram <see cref="IPubSubTransport"/> implementation.
    /// One instance corresponds to one
    /// <see cref="PubSubConnectionDataType"/> bound to an
    /// <c>opc.udp://</c> address: it owns the underlying
    /// <see cref="Socket"/>, the receive loop, and the optional
    /// send-side <see cref="UdpMessageRepeater"/>.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.2">
    /// Part 14 §7.3.2 UDP datagram transport</see> with the
    /// multicast / broadcast / unicast branches required by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.2.2">
    /// §7.3.2.2</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.2.3">
    /// §7.3.2.3</see>. Async-first using <c>Socket.ReceiveFromAsync</c> /
    /// <c>Socket.SendToAsync</c>; no APM, no sync-over-async. Per-packet
    /// buffers are rented from <see cref="ArrayPool{T}.Shared"/> so the
    /// steady-state receive loop is allocation-free.
    /// </remarks>
    public sealed class UdpDatagramTransport : IPubSubTransport, IPubSubDiscoveryAnnouncementTransport
    {
        private const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);
        private const string LocalSendStateLabel = "send-only";
        private const int StandardDiscoveryPort = 4840;

        private static readonly byte[] s_disableConnReset = [0, 0, 0, 0];

        private readonly PubSubConnectionDataType m_connection;
        private readonly NetworkInterface? m_networkInterface;
        private readonly TimeProvider m_timeProvider;
        private readonly UdpTransportOptions m_options;
        private readonly ILogger m_logger;
        private readonly IPubSubDiagnostics? m_diagnostics;
        private readonly UdpMessageRepeater m_repeater;
        private readonly Lock m_sync = new();
        private readonly DatagramV2Settings m_v2Settings;

        private Socket? m_socket;
        private CancellationTokenSource? m_receiveLoopCts;
        private Task? m_receiveLoopTask;
        private Channel<PubSubTransportFrame>? m_channel;
        private bool m_isConnected;
        private bool m_disposed;
        private IPEndPoint? m_sendDestination;
        private bool m_socketIsConnected;
        private readonly bool m_useConnectedUnicastClient;
        private readonly bool m_trackLastSeenUnicastPeer;

        /// <summary>
        /// Initializes a new <see cref="UdpDatagramTransport"/>.
        /// </summary>
        /// <param name="connection">
        /// PubSubConnection configuration the transport is bound to.
        /// </param>
        /// <param name="endpoint">
        /// Parsed UDP endpoint from
        /// <see cref="UdpEndpointParser.Parse"/>.
        /// </param>
        /// <param name="direction">
        /// Direction the transport services. Determines whether the
        /// receive loop starts on <see cref="OpenAsync"/>.
        /// </param>
        /// <param name="networkInterface">
        /// Optional <see cref="NetworkInterface"/> used to scope
        /// multicast joins and source-address selection.
        /// </param>
        /// <param name="telemetry">
        /// Telemetry context for per-instance logger creation. Must
        /// not be <see langword="null"/>.
        /// </param>
        /// <param name="timeProvider">
        /// Clock used for receive timestamps and inter-repeat
        /// scheduling. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="options">
        /// Transport tunables; must not be <see langword="null"/>.
        /// </param>
        /// <param name="diagnostics">
        /// Optional diagnostics sink; counters are incremented per
        /// inbound / outbound frame when non-null.
        /// </param>
        public UdpDatagramTransport(
            PubSubConnectionDataType connection,
            UdpEndpoint endpoint,
            PubSubTransportDirection direction,
            NetworkInterface? networkInterface,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            UdpTransportOptions options,
            IPubSubDiagnostics? diagnostics = null)
            : this(
                connection,
                endpoint,
                direction,
                networkInterface,
                telemetry,
                timeProvider,
                options,
                diagnostics,
                useConnectedUnicastClient: false,
                trackLastSeenUnicastPeer: true)
        {
        }

        internal UdpDatagramTransport(
            PubSubConnectionDataType connection,
            UdpEndpoint endpoint,
            PubSubTransportDirection direction,
            NetworkInterface? networkInterface,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            UdpTransportOptions options,
            IPubSubDiagnostics? diagnostics,
            bool useConnectedUnicastClient,
            bool trackLastSeenUnicastPeer)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            if (!endpoint.IsValid)
            {
                throw new ArgumentException(
                    "Endpoint is not valid (address null or port out of range).",
                    nameof(endpoint));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            m_connection = connection;
            Endpoint = endpoint;
            Direction = direction;
            m_networkInterface = networkInterface;
            m_timeProvider = timeProvider;
            m_options = options;
            m_diagnostics = diagnostics;
            m_useConnectedUnicastClient = useConnectedUnicastClient;
            m_trackLastSeenUnicastPeer = trackLastSeenUnicastPeer;
            m_logger = telemetry.CreateLogger<UdpDatagramTransport>();
            m_repeater = new UdpMessageRepeater(
                options.MessageRepeatCount,
                options.MessageRepeatDelay,
                timeProvider);
            m_v2Settings = ReadV2Settings(connection);
        }

        /// <inheritdoc/>
        public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

        /// <inheritdoc/>
        public PubSubTransportDirection Direction { get; }

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

        /// <summary>
        /// Parsed endpoint the transport is bound to. Exposed so
        /// integration tests can confirm port selection without
        /// re-parsing.
        /// </summary>
        public UdpEndpoint Endpoint { get; }

        internal IPEndPoint? RemoteEndpoint
        {
            get
            {
                lock (m_sync)
                {
                    return m_sendDestination;
                }
            }
        }

        internal void SetAuthenticatedRemoteEndpoint(IPEndPoint remoteEndpoint)
        {
            if (remoteEndpoint is null)
            {
                throw new ArgumentNullException(nameof(remoteEndpoint));
            }

            lock (m_sync)
            {
                if (m_socketIsConnected &&
                    m_sendDestination is not null &&
                    !m_sendDestination.Equals(remoteEndpoint))
                {
                    return;
                }

                m_sendDestination = new IPEndPoint(remoteEndpoint.Address, remoteEndpoint.Port);
            }
        }

        /// <summary>
        /// DiscoveryAnnounceRate value (milliseconds) honoured from the
        /// <c>DatagramConnectionTransport2DataType</c> per
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/6.4.1.2.7">
        /// Part 14 §6.4.1.2.7</see>. Zero means disabled.
        /// </summary>
        public uint DiscoveryAnnounceRate => m_v2Settings.DiscoveryAnnounceRate;

        /// <summary>
        /// Standard IPv4 discovery multicast destination from Part 14 §7.3.2.1.
        /// </summary>
        public static IPEndPoint StandardDiscoveryEndpoint { get; } = new(
            IPAddress.Parse("224.0.2.14"),
            StandardDiscoveryPort);

        /// <summary>
        /// DiscoveryMaxMessageSize cap (bytes) honoured from the
        /// <c>DatagramConnectionTransport2DataType</c> per
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/6.4.1.2.7">
        /// Part 14 §6.4.1.2.7</see>. Zero means no cap.
        /// </summary>
        public uint DiscoveryMaxMessageSize => m_v2Settings.DiscoveryMaxMessageSize;

        /// <summary>
        /// Negotiated QosCategory string from the
        /// <c>DatagramConnectionTransport2DataType</c>; mapped to a
        /// DSCP / TOS byte per
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/A.4">
        /// Part 14 Annex A.4</see>.
        /// </summary>
        public string QosCategory => m_v2Settings.QosCategory ?? string.Empty;

        /// <inheritdoc/>
        public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged;

        /// <inheritdoc/>
        public ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(UdpDatagramTransport));
                }
                if (m_socket is not null)
                {
                    return default;
                }
                Socket socket = new(
                    Endpoint.Address.AddressFamily,
                    SocketType.Dgram,
                    ProtocolType.Udp);
                try
                {
                    ConfigureSocket(socket);
                    BindAndJoin(socket);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
                m_socket = socket;
                if (HasReceiveDirection)
                {
                    m_channel = Channel.CreateBounded<PubSubTransportFrame>(
                        new BoundedChannelOptions(GetReceiveQueueCapacity())
                        {
                            FullMode = BoundedChannelFullMode.DropOldest,
                            SingleReader = false,
                            SingleWriter = true
                        });
                    m_receiveLoopCts = CancellationTokenSource.CreateLinkedTokenSource(
                        CancellationToken.None);
                    CancellationToken loopToken = m_receiveLoopCts.Token;
                    m_receiveLoopTask = Task.Run(() => ReceiveLoopAsync(loopToken), CancellationToken.None);
                }
                m_isConnected = true;
                m_logger.UdpTransportOpened(m_connection.Name, Endpoint, Direction);
            }
            RaiseStateChanged(true, StatusCodes.Good, null);
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            Socket? socket;
            CancellationTokenSource? loopCts;
            Task? loopTask;
            Channel<PubSubTransportFrame>? channel;
            bool wasConnected;
            lock (m_sync)
            {
                socket = m_socket;
                loopCts = m_receiveLoopCts;
                loopTask = m_receiveLoopTask;
                channel = m_channel;
                wasConnected = m_isConnected;
                m_socket = null;
                m_receiveLoopCts = null;
                m_receiveLoopTask = null;
                m_channel = null;
                m_isConnected = false;
                m_socketIsConnected = false;
            }
            if (loopCts is not null)
            {
                try
                {
                    loopCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
            if (socket is not null)
            {
                try
                {
                    DropMembershipsIfNeeded(socket);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    if (m_logger.IsEnabled(LogLevel.Debug))
                    {
                        m_logger.MulticastDropOnCloseRaised(ex, m_connection.Name, ex.GetType().Name);
                    }
                }
                try
                {
                    socket.Close();
                }
                catch (SocketException ex)
                {
                    m_logger.SocketCloseRaisedSocketException(ex, m_connection.Name);
                }
                socket.Dispose();
            }
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
                    m_logger.ReceiveLoopTerminatedWithException(ex, m_connection.Name);
                }
            }
            channel?.Writer.TryComplete();
            loopCts?.Dispose();
            if (wasConnected)
            {
                RaiseStateChanged(false, StatusCodes.Good, "Transport closed.");
            }
            await Task.CompletedTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <inheritdoc/>
        public ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            string? topic = null,
            CancellationToken cancellationToken = default)
        {
            _ = topic;
            cancellationToken.ThrowIfCancellationRequested();
            Socket? socket;
            IPEndPoint? destination;
            bool isConnectedSocket;
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(UdpDatagramTransport));
                }
                socket = m_socket;
                destination = m_sendDestination;
                isConnectedSocket = m_socketIsConnected;
            }
            if (socket is null)
            {
                throw new InvalidOperationException(
                    "UDP transport must be opened before sending.");
            }
            if (payload.Length > m_options.MaxFrameSize)
            {
                throw new ArgumentException(
                    $"Payload size {payload.Length} exceeds MaxFrameSize {m_options.MaxFrameSize}.",
                    nameof(payload));
            }
            return m_repeater.SendWithRepeatsAsync(
                ct => SendOnceAsync(socket, destination, isConnectedSocket, payload, ct),
                cancellationToken);
        }

        /// <summary>
        /// Sends one datagram to an explicit destination endpoint, falling back to the last-seen
        /// unicast peer when none is supplied. Used by the DTLS transport to route a handshake reply
        /// to the specific source that sent the corresponding ClientHello.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ArgumentException"></exception>
        internal ValueTask SendToAsync(
            ReadOnlyMemory<byte> payload,
            IPEndPoint? destination,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Socket? socket;
            IPEndPoint? target;
            bool isConnectedSocket;
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(UdpDatagramTransport));
                }
                socket = m_socket;
                target = destination ?? m_sendDestination;
                isConnectedSocket = m_socketIsConnected;
            }
            if (socket is null)
            {
                throw new InvalidOperationException(
                    "UDP transport must be opened before sending.");
            }
            if (payload.Length > m_options.MaxFrameSize)
            {
                throw new ArgumentException(
                    $"Payload size {payload.Length} exceeds MaxFrameSize {m_options.MaxFrameSize}.",
                    nameof(payload));
            }
            return m_repeater.SendWithRepeatsAsync(
                ct => SendOnceAsync(socket, target, isConnectedSocket, payload, ct),
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask SendDiscoveryAnnouncementAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Socket? socket;
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(UdpDatagramTransport));
                }
                socket = m_socket;
            }
            if (socket is null)
            {
                throw new InvalidOperationException(
                    "UDP transport must be opened before sending discovery announcements.");
            }
            if (payload.Length > m_options.MaxFrameSize)
            {
                throw new ArgumentException(
                    $"Payload size {payload.Length} exceeds MaxFrameSize {m_options.MaxFrameSize}.",
                    nameof(payload));
            }
            EnforceDiscoveryLimit(payload);
            return m_repeater.SendWithRepeatsAsync(
                ct => SendDiscoveryOnceAsync(socket, payload, ct),
                cancellationToken);
        }

        private async ValueTask SendDiscoveryOnceAsync(
            Socket socket,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            if (socket.AddressFamily == AddressFamily.InterNetwork)
            {
                await SendOnceAsync(
                    socket,
                    StandardDiscoveryEndpoint,
                    isConnectedSocket: false,
                    payload,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            using Socket discoverySocket = new(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp);
            ConfigureSocket(discoverySocket);
            discoverySocket.Bind(new IPEndPoint(IPAddress.Any, 0));
            await SendOnceAsync(
                discoverySocket,
                StandardDiscoveryEndpoint,
                isConnectedSocket: false,
                payload,
                cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Channel<PubSubTransportFrame>? channel;
            lock (m_sync)
            {
                channel = m_channel;
            }
            if (channel is null)
            {
                yield break;
            }
            ChannelReader<PubSubTransportFrame> reader = channel.Reader;
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out PubSubTransportFrame frame))
                {
                    yield return frame;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            bool alreadyDisposed;
            lock (m_sync)
            {
                alreadyDisposed = m_disposed;
                m_disposed = true;
            }
            if (alreadyDisposed)
            {
                return;
            }
            await CloseAsync().ConfigureAwait(false);
        }

        private async ValueTask SendOnceAsync(
            Socket socket,
            IPEndPoint? destination,
            bool isConnectedSocket,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            try
            {
                if (isConnectedSocket)
                {
#if NET8_0_OR_GREATER
                    await socket.SendAsync(payload, SocketFlags.None, cancellationToken)
                        .ConfigureAwait(false);
#else
                    cancellationToken.ThrowIfCancellationRequested();
                    ArraySegment<byte> segment = ToSegment(payload);
                    await socket.SendAsync(segment, SocketFlags.None).ConfigureAwait(false);
#endif
                }
                else
                {
                    if (destination is null)
                    {
                        throw new InvalidOperationException(
                            "UDP transport has no send destination configured.");
                    }
#if NET8_0_OR_GREATER
                    await socket.SendToAsync(payload, SocketFlags.None, destination, cancellationToken)
                        .ConfigureAwait(false);
#else
                    cancellationToken.ThrowIfCancellationRequested();
                    ArraySegment<byte> segment = ToSegment(payload);
                    await socket.SendToAsync(segment, SocketFlags.None, destination)
                        .ConfigureAwait(false);
#endif
                }
                m_diagnostics?.Increment(PubSubDiagnosticsCounterKind.SentNetworkMessages);
            }
            catch (SocketException ex)
            {
                m_logger.UdpSendFailed(
                    ex,
                    m_connection.Name,
                    destination?.ToString() ?? LocalSendStateLabel);
                throw;
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            Socket? socket;
            Channel<PubSubTransportFrame>? channel;
            lock (m_sync)
            {
                socket = m_socket;
                channel = m_channel;
            }
            if (socket is null || channel is null)
            {
                return;
            }
            ChannelWriter<PubSubTransportFrame> writer = channel.Writer;
            int maxFrameSize = m_options.MaxFrameSize;
            EndPoint anyEndPoint = Endpoint.Address.AddressFamily == AddressFamily.InterNetworkV6
                ? new IPEndPoint(IPAddress.IPv6Any, 0)
                : new IPEndPoint(IPAddress.Any, 0);
            byte[] receiveBuffer = ArrayPool<byte>.Shared.Rent(maxFrameSize);
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    SocketReceiveFromResult result;
                    try
                    {
#if NET8_0_OR_GREATER
                        result = await socket.ReceiveFromAsync(
                            receiveBuffer,
                            SocketFlags.None,
                            anyEndPoint,
                            cancellationToken).ConfigureAwait(false);
#else
                        result = await socket.ReceiveFromAsync(
                            new ArraySegment<byte>(receiveBuffer, 0, maxFrameSize),
                            SocketFlags.None,
                            anyEndPoint).ConfigureAwait(false);
#endif
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode is SocketError.OperationAborted or
                        SocketError.Interrupted)
                    {
                        break;
                    }
                    catch (SocketException ex)
                    {
                        m_logger.UdpReceiveRaisedSocketError(ex, m_connection.Name, ex.SocketErrorCode);
                        continue;
                    }
                    if (result.ReceivedBytes <= 0)
                    {
                        continue;
                    }
                    if (result.ReceivedBytes > maxFrameSize)
                    {
                        m_diagnostics?.Increment(
                            PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                        continue;
                    }
                    byte[] copy = new byte[result.ReceivedBytes];
                    Buffer.BlockCopy(receiveBuffer, 0, copy, 0, result.ReceivedBytes);
                    var sourceEndpoint = result.RemoteEndPoint as IPEndPoint;
                    var frame = new PubSubTransportFrame(
                        new ReadOnlyMemory<byte>(copy),
                        topic: null,
                        receivedAt: new DateTimeUtc(m_timeProvider.GetUtcNow().UtcDateTime),
                        sourceEndpoint: sourceEndpoint);
                    if (Endpoint.AddressType == UdpAddressType.Unicast &&
                        m_trackLastSeenUnicastPeer &&
                        sourceEndpoint is not null)
                    {
                        lock (m_sync)
                        {
                            m_sendDestination = sourceEndpoint;
                        }
                    }

                    m_diagnostics?.Increment(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages);
                    try
                    {
                        await writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                    }
                    catch (ChannelClosedException)
                    {
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(receiveBuffer);
                writer.TryComplete();
            }
        }

        private void ConfigureSocket(Socket socket)
        {
            try
            {
                socket.SendBufferSize = m_options.SendBufferSize;
            }
            catch (SocketException ex)
            {
                m_logger.SettingSendBufferFailed(ex, m_connection.Name);
            }
            try
            {
                socket.ReceiveBufferSize = m_options.ReceiveBufferSize;
            }
            catch (SocketException ex)
            {
                m_logger.SettingReceiveBufferFailed(ex, m_connection.Name);
            }
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }
            catch (SocketException ex)
            {
                m_logger.SettingReuseAddressFailed(ex, m_connection.Name);
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    socket.IOControl(SIO_UDP_CONNRESET, s_disableConnReset, null);
                }
                catch (SocketException ex)
                {
                    m_logger.DisableUdpConnResetFailed(ex, m_connection.Name);
                }
            }
            if (Endpoint.AddressType is UdpAddressType.Broadcast or UdpAddressType.SubnetBroadcast)
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                }
                catch (SocketException ex)
                {
                    m_logger.SettingBroadcastFailed(ex, m_connection.Name);
                }
            }
            if (Endpoint.Address.AddressFamily == AddressFamily.InterNetwork)
            {
                try
                {
                    socket.SetSocketOption(
                        SocketOptionLevel.IP,
                        SocketOptionName.MulticastTimeToLive,
                        m_options.Ttl);
                    socket.SetSocketOption(SocketOptionLevel.IP,
                        SocketOptionName.IpTimeToLive,
                        m_options.Ttl);
                }
                catch (SocketException ex)
                {
                    m_logger.SettingIpv4TtlFailed(ex, m_connection.Name);
                }
            }
            else if (Endpoint.Address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                try
                {
                    socket.SetSocketOption(
                        SocketOptionLevel.IPv6,
                        SocketOptionName.MulticastTimeToLive,
                        m_options.Ttl);
                    socket.SetSocketOption(SocketOptionLevel.IPv6,
                        SocketOptionName.HopLimit,
                        m_options.Ttl);
                }
                catch (SocketException ex)
                {
                    m_logger.SettingIpv6HopLimitFailed(ex, m_connection.Name);
                }
            }
            try
            {
                socket.MulticastLoopback = m_options.MulticastLoopback;
            }
            catch (SocketException ex)
            {
                m_logger.SettingMulticastLoopbackFailed(ex, m_connection.Name);
            }
            ApplyQosCategory(socket);
        }

        private void ApplyQosCategory(Socket socket)
        {
            if (string.IsNullOrEmpty(m_v2Settings.QosCategory))
            {
                return;
            }
            int tos = MapQosCategoryToTos(m_v2Settings.QosCategory);
            try
            {
                if (Endpoint.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    socket.SetSocketOption(
                        SocketOptionLevel.IP,
                        SocketOptionName.TypeOfService,
                        tos);
                }
                else if (Endpoint.Address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.SetSocketOption(
                        SocketOptionLevel.IPv6,
                        SocketOptionName.TypeOfService,
                        tos);
                }
                m_logger.AppliedQosCategory(m_v2Settings.QosCategory, tos, m_connection.Name);
            }
            catch (SocketException ex)
            {
                m_logger.SettingIpTosFailed(ex, m_v2Settings.QosCategory, m_connection.Name);
            }
        }

        private void BindAndJoin(Socket socket)
        {
            switch (Endpoint.AddressType)
            {
                case UdpAddressType.Multicast:
                    BindForMulticast(socket);
                    JoinMulticastGroup(socket);
                    JoinStandardDiscoveryGroupIfNeeded(socket);
                    m_sendDestination = new IPEndPoint(Endpoint.Address, Endpoint.Port);
                    break;
                case UdpAddressType.Broadcast:
                case UdpAddressType.SubnetBroadcast:
                    BindForBroadcast(socket);
                    JoinStandardDiscoveryGroupIfNeeded(socket);
                    m_sendDestination = new IPEndPoint(Endpoint.Address, Endpoint.Port);
                    break;
                default:
                    BindForUnicast(socket);
                    JoinStandardDiscoveryGroupIfNeeded(socket);
                    break;
            }
        }

        private void BindForMulticast(Socket socket)
        {
            EndPoint bindEndPoint = Endpoint.Address.AddressFamily == AddressFamily.InterNetworkV6
                ? new IPEndPoint(IPAddress.IPv6Any, Endpoint.Port)
                : new IPEndPoint(IPAddress.Any, Endpoint.Port);
            socket.Bind(bindEndPoint);
        }

        private void BindForBroadcast(Socket socket)
        {
            EndPoint bindEndPoint = new IPEndPoint(IPAddress.Any, Endpoint.Port);
            socket.Bind(bindEndPoint);
        }

        private void BindForUnicast(Socket socket)
        {
            if ((HasSendDirection && !HasReceiveDirection) || (m_useConnectedUnicastClient && HasSendDirection))
            {
                EndPoint bindEndPoint = Endpoint.Address.AddressFamily == AddressFamily.InterNetworkV6
                    ? new IPEndPoint(IPAddress.IPv6Any, 0)
                    : new IPEndPoint(IPAddress.Any, 0);
                socket.Bind(bindEndPoint);
                IPEndPoint remote = new(Endpoint.Address, Endpoint.Port);
                socket.Connect(remote);
                m_sendDestination = remote;
                m_socketIsConnected = true;
            }
            else
            {
                EndPoint bindEndPoint = Endpoint.Address.AddressFamily == AddressFamily.InterNetworkV6
                    ? new IPEndPoint(IPAddress.IPv6Any, Endpoint.Port)
                    : new IPEndPoint(IPAddress.Any, Endpoint.Port);
                socket.Bind(bindEndPoint);
                m_sendDestination = new IPEndPoint(Endpoint.Address, Endpoint.Port);
            }
        }

        private void JoinMulticastGroup(Socket socket)
        {
            if (Endpoint.Address.AddressFamily == AddressFamily.InterNetwork)
            {
                IPAddress localAddress = SelectLocalIPv4(m_networkInterface) ?? IPAddress.Any;
                var option = new MulticastOption(Endpoint.Address, localAddress);
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, option);
            }
            else
            {
                int interfaceIndex = SelectIPv6InterfaceIndex(m_networkInterface);
                var option = new IPv6MulticastOption(Endpoint.Address, interfaceIndex);
                socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, option);
            }
        }

        private void JoinStandardDiscoveryGroupIfNeeded(Socket socket)
        {
            if (!ShouldJoinStandardDiscoveryGroup(Endpoint, Direction))
            {
                return;
            }

            IPAddress localAddress = SelectLocalIPv4(m_networkInterface) ?? IPAddress.Any;
            var option = new MulticastOption(StandardDiscoveryEndpoint.Address, localAddress);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, option);
        }

        private void DropMembershipsIfNeeded(Socket socket)
        {
            if (Endpoint.AddressType == UdpAddressType.Multicast)
            {
                if (Endpoint.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    IPAddress localAddress = SelectLocalIPv4(m_networkInterface) ?? IPAddress.Any;
                    var option = new MulticastOption(Endpoint.Address, localAddress);
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DropMembership, option);
                }
                else
                {
                    int interfaceIndex = SelectIPv6InterfaceIndex(m_networkInterface);
                    var option = new IPv6MulticastOption(Endpoint.Address, interfaceIndex);
                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.DropMembership, option);
                }
            }
            if (!ShouldJoinStandardDiscoveryGroup(Endpoint, Direction))
            {
                return;
            }

            IPAddress standardAddress = StandardDiscoveryEndpoint.Address;
            IPAddress localDiscoveryAddress = SelectLocalIPv4(m_networkInterface) ?? IPAddress.Any;
            var discoveryOption = new MulticastOption(standardAddress, localDiscoveryAddress);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DropMembership, discoveryOption);
        }

        internal static bool ShouldJoinStandardDiscoveryGroup(
            UdpEndpoint endpoint,
            PubSubTransportDirection direction)
        {
            if ((direction & PubSubTransportDirection.Receive) != PubSubTransportDirection.Receive)
            {
                return false;
            }
            if (endpoint.Port != StandardDiscoveryPort)
            {
                return false;
            }
            if (endpoint.Address is null)
            {
                return false;
            }
            if (endpoint.Address.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }
            return !endpoint.Address.Equals(StandardDiscoveryEndpoint.Address);
        }

        private static IPAddress? SelectLocalIPv4(NetworkInterface? networkInterface)
        {
            if (networkInterface is null)
            {
                return null;
            }
            try
            {
                IPInterfaceProperties props = networkInterface.GetIPProperties();
                foreach (UnicastIPAddressInformation info in props.UnicastAddresses)
                {
                    if (info.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return info.Address;
                    }
                }
            }
            catch (NetworkInformationException)
            {
            }
            return null;
        }

        private static int SelectIPv6InterfaceIndex(NetworkInterface? networkInterface)
        {
            if (networkInterface is null)
            {
                return 0;
            }
            try
            {
                IPInterfaceProperties props = networkInterface.GetIPProperties();
                IPv6InterfaceProperties? ipv6 = props.GetIPv6Properties();
                return ipv6?.Index ?? 0;
            }
            catch (NetworkInformationException)
            {
                return 0;
            }
        }

        private int GetReceiveQueueCapacity()
        {
            int capacity = m_options.ReceiveQueueCapacity;
            return capacity > 0 ? capacity : 1;
        }

        private bool HasReceiveDirection
            => (Direction & PubSubTransportDirection.Receive) == PubSubTransportDirection.Receive;

        private bool HasSendDirection
            => (Direction & PubSubTransportDirection.Send) == PubSubTransportDirection.Send;

#if !NET8_0_OR_GREATER
        private static ArraySegment<byte> ToSegment(ReadOnlyMemory<byte> payload)
        {
            if (MemoryMarshal.TryGetArray(payload, out ArraySegment<byte> segment))
            {
                return segment;
            }
            byte[] copy = payload.ToArray();
            return new ArraySegment<byte>(copy);
        }
#endif

        private void RaiseStateChanged(bool connected, StatusCode status, string? reason)
        {
            EventHandler<PubSubTransportStateChangedEventArgs>? handler = StateChanged;
            if (handler is null)
            {
                return;
            }
            try
            {
                handler.Invoke(this, new PubSubTransportStateChangedEventArgs(connected, status, reason));
            }
            catch (Exception ex)
            {
                m_logger.StateChangedHandlerThrew(ex, m_connection.Name);
            }
        }

        /// <summary>
        /// Enforces the <c>DiscoveryMaxMessageSize</c> cap defined by
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/6.4.1.2.7">
        /// Part 14 §6.4.1.2.7</see>. Throws
        /// <see cref="ServiceResultException"/> with status
        /// <see cref="StatusCodes.BadEncodingLimitsExceeded"/> when the
        /// payload exceeds the cap.
        /// </summary>
        /// <param name="payload">Discovery payload to be sent.</param>
        /// <exception cref="ServiceResultException"></exception>
        public void EnforceDiscoveryLimit(ReadOnlyMemory<byte> payload)
        {
            uint cap = m_v2Settings.DiscoveryMaxMessageSize;
            if (cap == 0)
            {
                return;
            }
            if ((uint)payload.Length > cap)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded,
                    $"Discovery payload size {payload.Length} exceeds the " +
                    $"DiscoveryMaxMessageSize cap of {cap} bytes.");
            }
        }

        private static DatagramV2Settings ReadV2Settings(
            PubSubConnectionDataType connection)
        {
            if (connection.TransportSettings.IsNull)
            {
                return new DatagramV2Settings
                {
                    DiscoveryMaxMessageSize = 4096
                };
            }
            if (!connection.TransportSettings.TryGetValue(
                    out DatagramConnectionTransport2DataType? v2) ||
                v2 is null)
            {
                return new DatagramV2Settings
                {
                    DiscoveryMaxMessageSize = 4096
                };
            }
            return new DatagramV2Settings
            {
                DiscoveryAnnounceRate = v2.DiscoveryAnnounceRate,
                DiscoveryMaxMessageSize = v2.DiscoveryMaxMessageSize == 0
                    ? 4096
                    : v2.DiscoveryMaxMessageSize,
                QosCategory = v2.QosCategory ?? string.Empty
            };
        }

        /// <summary>
        /// Maps a QosCategory string from
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/6.4.1.2.7">
        /// Part 14 §6.4.1.2.7</see> to the DSCP-encoded TOS byte
        /// (Part 14 Annex A.4).
        /// </summary>
        /// <param name="category">QosCategory string.</param>
        /// <returns>Encoded TOS byte (DSCP &lt;&lt; 2), or 0 when
        /// <paramref name="category"/> is empty / unknown.</returns>
        internal static int MapQosCategoryToTos(string category)
        {
            return category switch
            {
                "Reliable" => 0x48,
                "BestEffort" => 0x00,
                "ExpeditedForwarding" => 0xB8,
                _ => 0
            };
        }

        private readonly record struct DatagramV2Settings
        {
            public uint DiscoveryAnnounceRate { get; init; }
            public uint DiscoveryMaxMessageSize { get; init; }
            public string QosCategory { get; init; }
        }
    }

    /// <summary>
    /// Source-generated log messages for UdpDatagramTransport.
    /// </summary>
    internal static partial class UdpDatagramTransportLog
    {
        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 0, Level = LogLevel.Information,
            Message = "UDP transport opened: connection='{Connection}' endpoint={Endpoint} direction={Direction}")]
        public static partial void UdpTransportOpened(
            this ILogger logger,
            string? connection,
            UdpEndpoint endpoint,
            PubSubTransportDirection direction);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 1, Level = LogLevel.Debug,
            Message = "Multicast drop on close for connection '{Connection}' raised {Type}.")]
        public static partial void MulticastDropOnCloseRaised(
            this ILogger logger,
            Exception exception,
            string? connection,
            string type);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 2, Level = LogLevel.Debug,
            Message = "Socket close for connection '{Connection}' raised SocketException.")]
        public static partial void SocketCloseRaisedSocketException(
            this ILogger logger,
            Exception exception,
            string? connection);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 3, Level = LogLevel.Debug,
            Message = "Receive loop terminated with exception for connection '{Connection}'.")]
        public static partial void ReceiveLoopTerminatedWithException(
            this ILogger logger,
            Exception exception,
            string? connection);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 4, Level = LogLevel.Warning,
            Message = "UDP send failed on connection '{Connection}' to {Endpoint}.")]
        public static partial void UdpSendFailed(
            this ILogger logger,
            Exception exception,
            string? connection,
            string endpoint);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 5, Level = LogLevel.Warning,
            Message = "UDP receive on connection '{Connection}' raised {Code}; continuing.")]
        public static partial void UdpReceiveRaisedSocketError(
            this ILogger logger,
            Exception exception,
            string? connection,
            SocketError code);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 6, Level = LogLevel.Debug,
            Message = "Setting SO_SNDBUF failed for connection '{Connection}'.")]
        public static partial void SettingSendBufferFailed(
            this ILogger logger,
            Exception exception,
            string? connection);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 7, Level = LogLevel.Debug,
            Message = "Setting SO_RCVBUF failed for connection '{Connection}'.")]
        public static partial void SettingReceiveBufferFailed(
            this ILogger logger,
            Exception exception,
            string? connection);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 8, Level = LogLevel.Debug,
            Message = "Setting SO_REUSEADDR failed for connection '{Connection}'.")]
        public static partial void SettingReuseAddressFailed(
            this ILogger logger,
            Exception exception,
            string? connection);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 9, Level = LogLevel.Debug,
            Message = "SIO_UDP_CONNRESET disable failed for connection '{Connection}'.")]
        public static partial void DisableUdpConnResetFailed(
            this ILogger logger,
            Exception exception,
            string? connection);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 10, Level = LogLevel.Debug,
            Message = "Setting SO_BROADCAST failed for connection '{Connection}'.")]
        public static partial void SettingBroadcastFailed(this ILogger logger, Exception exception, string? connection);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 11, Level = LogLevel.Debug,
            Message = "Setting IPv4 TTL failed for connection '{Connection}'.")]
        public static partial void SettingIpv4TtlFailed(this ILogger logger, Exception exception, string? connection);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 12, Level = LogLevel.Debug,
            Message = "Setting IPv6 hop limit failed for connection '{Connection}'.")]
        public static partial void SettingIpv6HopLimitFailed(
            this ILogger logger,
            Exception exception,
            string? connection);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 13, Level = LogLevel.Debug,
            Message = "Setting IP_MULTICAST_LOOP failed for connection '{Connection}'.")]
        public static partial void SettingMulticastLoopbackFailed(
            this ILogger logger,
            Exception exception,
            string? connection);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 14, Level = LogLevel.Information,
            Message = "Applied QosCategory '{QosCategory}' (TOS={Tos:X2}) on connection '{Connection}' " +
                "per Part 14 §6.4.1.2.7 / Annex A.4.")]
        public static partial void AppliedQosCategory(
            this ILogger logger,
            string? qosCategory,
            int tos,
            string? connection);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 15, Level = LogLevel.Debug,
            Message = "Setting IP_TOS for QosCategory '{QosCategory}' failed for connection '{Connection}'.")]
        public static partial void SettingIpTosFailed(
            this ILogger logger,
            Exception exception,
            string? qosCategory,
            string? connection);

        [LoggerMessage(EventId = PubSubUdpEventIds.UdpDatagramTransport + 16, Level = LogLevel.Debug,
            Message = "StateChanged handler threw for connection '{Connection}'.")]
        public static partial void StateChangedHandlerThrew(
            this ILogger logger,
            Exception exception,
            string? connection);
    }

}
