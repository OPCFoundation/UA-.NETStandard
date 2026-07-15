/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Factory for the TCP-backed <see cref="IUaSCByteTransport"/>.
    /// </summary>
    public sealed class TcpByteTransportFactory : IUaSCByteTransportFactory
    {
        /// <summary>
        /// Creates a factory that uses the supplied telemetry context as the
        /// default for transports created without an explicit context.
        /// </summary>
        public TcpByteTransportFactory(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
        }

        /// <inheritdoc/>
        public string Implementation => "UA-TCP";

        /// <inheritdoc/>
        public IUaSCByteTransport Create(
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
        {
            return new TcpByteTransport(bufferManager, receiveBufferSize, telemetry ?? m_telemetry);
        }

        private readonly ITelemetryContext m_telemetry;
    }

    /// <summary>
    /// Raw TCP socket implementation of <see cref="IUaSCByteTransport"/>.
    /// Maps one UASC <c>MessageChunk</c> (OPC UA Part 6 §6.7.2) to one
    /// contiguous Send/Receive on the underlying socket.
    /// </summary>
    /// <remarks>
    /// Replaces the legacy <c>TcpMessageSocket</c> SAEA-based plumbing with a
    /// pure TAP shape (no Begin/End, no callback events). Buffers used to
    /// receive a chunk are rented from the <see cref="BufferManager"/>
    /// supplied at construction and handed back to the channel via
    /// <see cref="ReceiveChunkAsync"/>; the channel is responsible for
    /// returning them.
    /// </remarks>
    public sealed class TcpByteTransport :
        IUaSCByteTransport,
        IUaSCByteTransportLimits,
        IDisposable
    {
        /// <summary>
        /// Creates an unconnected client transport.
        /// </summary>
        public TcpByteTransport(
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
        {
            m_bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
            m_receiveBufferSize = receiveBufferSize;
            m_logger = telemetry.CreateLogger<TcpByteTransport>();
            m_sendLock = new SemaphoreSlim(1, 1);
        }

        /// <summary>
        /// Wraps an already-accepted server socket.
        /// </summary>
        public TcpByteTransport(
            Socket socket,
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
            : this(bufferManager, receiveBufferSize, telemetry)
        {
            m_socket = socket ?? throw new ArgumentNullException(nameof(socket));
        }

        /// <inheritdoc/>
        public string Implementation => "UA-TCP";

        /// <inheritdoc/>
        public TransportChannelFeatures Features =>
            TransportChannelFeatures.ReverseConnect | TransportChannelFeatures.Reconnect;

        /// <inheritdoc/>
        public EndPoint? LocalEndpoint
        {
            get
            {
                try
                {
                    return m_socket?.LocalEndPoint;
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
            }
        }

        /// <inheritdoc/>
        public EndPoint? RemoteEndpoint
        {
            get
            {
                try
                {
                    return m_socket?.RemoteEndPoint;
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask ConnectAsync(Uri url, CancellationToken ct)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }
            if (m_socket != null)
            {
                throw new InvalidOperationException("The transport is already connected.");
            }

            int port = url.Port;
            if (port is <= 0 or > ushort.MaxValue)
            {
                port = Utils.UaTcpDefaultPort;
            }

            var endpoint = new DnsEndPoint(url.IdnHost, port);

            // Work around for macOS container name dns resolution issue:
            // when the host equals the local machine name, fall back to
            // a hosts-file lookup of "localhost".
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                url.HostNameType == UriHostNameType.Dns &&
                url.IdnHost.Equals(Utils.GetHostName(), StringComparison.OrdinalIgnoreCase))
            {
                endpoint = new DnsEndPoint("localhost", port);
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
                LingerState = new LingerOption(true, 5)
            };

            try
            {
#if NET5_0_OR_GREATER
                await socket.ConnectAsync(endpoint, ct).ConfigureAwait(false);
#else
                using (ct.Register(static s => ((Socket)s!).Dispose(), socket))
                {
                    await socket.ConnectAsync(endpoint).ConfigureAwait(false);
                }
                ct.ThrowIfCancellationRequested();
#endif

                lock (m_socketLock)
                {
                    if (m_closed)
                    {
                        throw new OperationCanceledException();
                    }
                    m_socket = socket;
                    socket = null;
                }
            }
            catch (Exception ex)
            {
                if (m_logger.IsEnabled(LogLevel.Debug))
                {
                    m_logger.TcpByteTransportLogMessage0(ex, url.IdnHost, port);
                }
                throw;
            }
            finally
            {
                if (socket != null)
                {
                    ShutdownAndDispose(socket);
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
        {
            Socket socket = RequireConnectedSocket();
            await m_sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                int sent = 0;
                while (sent < chunk.Length)
                {
                    ReadOnlyMemory<byte> slice = chunk[sent..];
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    int n = await socket
                        .SendAsync(slice, SocketFlags.None, ct)
                        .ConfigureAwait(false);
#else
                    if (!MemoryMarshal.TryGetArray(slice, out ArraySegment<byte> seg))
                    {
                        // Fall back to a copy when the memory does not wrap an array
                        // (rare in this code path: BufferManager always rents arrays).
                        byte[] tmp = slice.ToArray();
                        seg = new ArraySegment<byte>(tmp, 0, tmp.Length);
                    }
                    int n = await socket
                        .SendAsync(seg, SocketFlags.None)
                        .ConfigureAwait(false);
#endif
                    if (n <= 0)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadConnectionClosed,
                            "Remote side closed the connection while sending.");
                    }
                    sent += n;
                }
            }
            finally
            {
                m_sendLock.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
        {
            if (buffers == null)
            {
                throw new ArgumentNullException(nameof(buffers));
            }
            Socket socket = RequireConnectedSocket();
            await m_sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                // Socket.SendAsync(IList<ArraySegment<byte>>) is a vectored send
                // available on all targets but does not accept a CancellationToken,
                // so we use it directly and rely on Close()/Dispose() for cancel.
                int sent = await socket
                    .SendAsync(buffers, SocketFlags.None)
                    .ConfigureAwait(false);
#else
                int sent = await socket
                    .SendAsync(buffers, SocketFlags.None)
                    .ConfigureAwait(false);
#endif
                if (sent < buffers.TotalSize)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConnectionClosed,
                        "Remote side closed the connection while sending (sent {0} of {1} bytes).",
                        sent,
                        buffers.TotalSize);
                }
            }
            finally
            {
                m_sendLock.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
        {
            Socket socket = RequireConnectedSocket();
            int receiveBufferSize = Volatile.Read(ref m_receiveBufferSize);
            byte[] buffer = m_bufferManager.TakeBuffer(
                receiveBufferSize,
                nameof(ReceiveChunkAsync),
                ct);
            try
            {
                m_bufferManager.Lock(buffer);
                try
                {
                    // Read the 8-byte UASC chunk header (message type + chunk size).
                    await ReadExactAsync(socket, buffer, 0, TcpMessageLimits.MessageTypeAndSize, ct)
                        .ConfigureAwait(false);

                    uint messageType = BitConverter.ToUInt32(buffer, 0);
                    if (!TcpMessageType.IsValid(messageType))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadTcpMessageTypeInvalid,
                            "Message type 0x{0:X8} is invalid.",
                            messageType);
                    }

                    int messageSize = BitConverter.ToInt32(buffer, 4);
                    if (messageSize <= TcpMessageLimits.MessageTypeAndSize ||
                        messageSize > receiveBufferSize)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadTcpMessageTooLarge,
                            "Message size {0} bytes is invalid (buffer size {1}).",
                            messageSize,
                            receiveBufferSize);
                    }

                    // Read the remaining bytes (chunk body) directly into the same buffer.
                    await ReadExactAsync(
                            socket,
                            buffer,
                            TcpMessageLimits.MessageTypeAndSize,
                            messageSize - TcpMessageLimits.MessageTypeAndSize,
                            ct)
                        .ConfigureAwait(false);

                    var segment = new ArraySegment<byte>(buffer, 0, messageSize);
                    m_bufferManager.Unlock(buffer);
                    buffer = null!; // ownership transferred to caller
                    return segment;
                }
                catch
                {
                    if (buffer != null)
                    {
                        m_bufferManager.Unlock(buffer);
                    }
                    throw;
                }
            }
            catch
            {
                if (buffer != null)
                {
                    m_bufferManager.ReturnBuffer(buffer, nameof(ReceiveChunkAsync));
                }
                throw;
            }
        }

        void IUaSCByteTransportLimits.SetReceiveBufferSize(int receiveBufferSize)
        {
            if (receiveBufferSize <= TcpMessageLimits.MessageTypeAndSize)
            {
                throw new ArgumentOutOfRangeException(nameof(receiveBufferSize));
            }
            Volatile.Write(ref m_receiveBufferSize, receiveBufferSize);
        }

        /// <inheritdoc/>
        public void Close()
        {
            Socket? socket;
            lock (m_socketLock)
            {
                socket = m_socket;
                m_socket = null;
                if (m_closed)
                {
                    return;
                }
                m_closed = true;
            }
            if (socket != null)
            {
                ShutdownAndDispose(socket);
            }
            m_sendLock.Dispose();
        }

        /// <summary>
        /// Releases the underlying socket and synchronization primitives.
        /// Equivalent to calling <see cref="Close"/>.
        /// </summary>
        public void Dispose()
        {
            Close();
        }

        private Socket RequireConnectedSocket()
        {
            Socket? socket = m_socket;
            if (socket == null || m_closed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConnectionClosed,
                    "The transport is not connected.");
            }
            return socket;
        }

        private static async ValueTask ReadExactAsync(
            Socket socket,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken ct)
        {
            int read = 0;
            while (read < count)
            {
                int n;
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                n = await socket
                    .ReceiveAsync(
                        new Memory<byte>(buffer, offset + read, count - read),
                        SocketFlags.None,
                        ct)
                    .ConfigureAwait(false);
#else
                n = await socket
                    .ReceiveAsync(
                        new ArraySegment<byte>(buffer, offset + read, count - read),
                        SocketFlags.None)
                    .ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
#endif
                if (n <= 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConnectionClosed,
                        "Remote side closed the connection.");
                }
                read += n;
            }
        }

        private void ShutdownAndDispose(Socket socket)
        {
            try
            {
                if (socket.Connected)
                {
                    socket.Shutdown(SocketShutdown.Both);
                }
            }
            catch (Exception e)
            {
                m_logger.TcpByteTransportLogMessage1(e);
            }
            finally
            {
                socket.Dispose();
            }
        }

        private readonly BufferManager m_bufferManager;
        private int m_receiveBufferSize;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_sendLock;
        private readonly Lock m_socketLock = new();
        private Socket? m_socket;
        private bool m_closed;
    }

    /// <summary>
    /// Source-generated log messages for TcpByteTransport.
    /// </summary>
    internal static partial class TcpByteTransportLog
    {
        [LoggerMessage(EventId = CoreEventIds.TcpByteTransport + 0, Level = LogLevel.Debug,
            Message = "Failed to connect socket to {IdnHost}:{Port}.")]
        public static partial void TcpByteTransportLogMessage0(
            this ILogger logger,
            global::System.Exception? exception,
            string? idnHost,
            int port);

        [LoggerMessage(EventId = CoreEventIds.TcpByteTransport + 1, Level = LogLevel.Debug,
            Message = "Unexpected error closing socket.")]
        public static partial void TcpByteTransportLogMessage1(
            this ILogger logger,
            global::System.Exception? exception);
    }

}
