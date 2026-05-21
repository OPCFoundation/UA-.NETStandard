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
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a transport channel with UA-TCP transport, UA-SC security and UA Binary encoding
    /// </summary>
    public class TcpTransportChannel : UaSCUaBinaryTransportChannel
    {
        /// <summary>
        /// Create a Tcp transport channel.
        /// </summary>
        public TcpTransportChannel(ITelemetryContext telemetry)
            : base(new TcpMessageSocketFactory(telemetry), telemetry)
        {
        }
    }

    /// <summary>
    /// Creates a new TcpTransportChannel with ITransportChannel interface.
    /// </summary>
    public class TcpTransportChannelFactory : ITransportChannelFactory
    {
        /// <summary>
        /// The protocol supported by the channel.
        /// </summary>
        public string UriScheme => Utils.UriSchemeOpcTcp;

        /// <summary>
        /// The method creates a new instance of a TCP transport channel
        /// </summary>
        /// <returns>The transport channel</returns>
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            return new TcpTransportChannel(telemetry);
        }
    }

    /// <summary>
    /// Handles async event callbacks from a socket
    /// </summary>
    public class TcpMessageSocketAsyncEventArgs : IMessageSocketAsyncEventArgs
    {
        /// <summary>
        /// Create the event args for the async TCP message socket.
        /// </summary>
        public TcpMessageSocketAsyncEventArgs()
        {
            Args = new SocketAsyncEventArgs { UserToken = this };
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Overrideable version of the Dispose method.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Args.Dispose();
            }
        }

        /// <inheritdoc/>
        public object? UserToken { get; set; }

        /// <inheritdoc/>
        public void SetBuffer(byte[] buffer, int offset, int count)
        {
            Args.SetBuffer(buffer, offset, count);
        }

        /// <inheritdoc/>
        public bool IsSocketError => Args.SocketError != SocketError.Success;

        /// <inheritdoc/>
        public string SocketErrorString => Args.SocketError.ToString();

        /// <inheritdoc/>
        public event EventHandler<IMessageSocketAsyncEventArgs> Completed
        {
            add
            {
                m_InternalComplete += value;
                Args.Completed += OnComplete;
            }
            remove
            {
                m_InternalComplete -= value;
                Args.Completed -= OnComplete;
            }
        }

        /// <inheritdoc/>
        protected void OnComplete(object? sender, SocketAsyncEventArgs e)
        {
            if (e.UserToken == null)
            {
                return;
            }

            m_InternalComplete?.Invoke(this, e.UserToken as IMessageSocketAsyncEventArgs);
        }

        /// <inheritdoc/>
        public int BytesTransferred => Args.BytesTransferred;

        /// <inheritdoc/>
        public byte[]? Buffer => Args.Buffer;

        /// <inheritdoc/>
        public BufferCollection? BufferList
        {
            get => Args.BufferList as BufferCollection;
            set => Args.BufferList = value;
        }

        /// <summary>
        /// The socket event args.
        /// </summary>
        public SocketAsyncEventArgs Args { get; }

        private event EventHandler<IMessageSocketAsyncEventArgs?>? m_InternalComplete;
    }

    /// <summary>
    /// Creates a new TcpMessageSocket with IMessageSocket interface.
    /// </summary>
    public class TcpMessageSocketFactory : IMessageSocketFactory
    {
        /// <summary>
        /// Create a socket factory
        /// </summary>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public TcpMessageSocketFactory(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
        }

        /// <summary>
        /// The method creates a new instance of a UA-TCP message socket
        /// </summary>
        /// <returns> the message socket</returns>
        public IMessageSocket Create(
            IMessageSink sink,
            BufferManager bufferManager,
            int receiveBufferSize)
        {
            return new TcpMessageSocket(
                sink,
                bufferManager,
                receiveBufferSize,
                m_telemetry);
        }

        /// <summary>
        /// Gets the implementation description.
        /// </summary>
        /// <value>The implementation string.</value>
        public string Implementation => "UA-TCP";

        private readonly ITelemetryContext m_telemetry;
    }

    /// <summary>
    /// Handles reading and writing of message chunks over a socket.
    /// </summary>
    public class TcpMessageSocket : IMessageSocket
    {
        /// <summary>
        /// Creates an unconnected socket.
        /// </summary>
        public TcpMessageSocket(
            IMessageSink sink,
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
        {
            m_logger = telemetry.CreateLogger<TcpMessageSocket>();
            m_sink = sink;
            m_socket = null;
            m_bufferManager = bufferManager ??
                throw new ArgumentNullException(nameof(bufferManager));
            m_receiveBufferSize = receiveBufferSize;
        }

        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public TcpMessageSocket(
            IMessageSink sink,
            Socket socket,
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
        {
            m_logger = telemetry.CreateLogger<TcpMessageSocket>();
            m_sink = sink;
            m_socket = socket ?? throw new ArgumentNullException(nameof(socket));
            m_bufferManager = bufferManager ??
                throw new ArgumentNullException(nameof(bufferManager));
            m_receiveBufferSize = receiveBufferSize;
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_socket?.Dispose();
            }
        }

        /// <inheritdoc/>
        public int Handle => m_socket?.GetHashCode() ?? -1;

        /// <inheritdoc/>
        public EndPoint? LocalEndpoint => m_socket?.LocalEndPoint;

        /// <inheritdoc/>
        public EndPoint? RemoteEndpoint => m_socket?.RemoteEndPoint;

        /// <inheritdoc/>
        public TransportChannelFeatures MessageSocketFeatures =>
            TransportChannelFeatures.ReverseConnect | TransportChannelFeatures.Reconnect;

        /// <inheritdoc/>
        public async Task ConnectAsync(Uri endpointUrl, CancellationToken ct)
        {
            if (endpointUrl == null)
            {
                throw new ArgumentNullException(nameof(endpointUrl));
            }

            if (m_socket != null)
            {
                throw new InvalidOperationException("The socket is already connected.");
            }

            // Get port
            int port = endpointUrl.Port;
            if (port is <= 0 or > ushort.MaxValue)
            {
                port = Utils.UaTcpDefaultPort;
            }

            var endpoint = new DnsEndPoint(endpointUrl.IdnHost, port);

            // Work around for macOS container name dns resolution issue
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                endpointUrl.HostNameType == UriHostNameType.Dns &&
                endpointUrl.IdnHost.Equals(
                    Utils.GetHostName(), StringComparison.OrdinalIgnoreCase))
            {
                // Use hosts file lookup
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
                await socket.ConnectAsync(endpoint).ConfigureAwait(false);
#endif

                ct.ThrowIfCancellationRequested();
                lock (m_socketLock)
                {
#pragma warning disable CA1508 // Avoid dead conditional code
                    if (!m_closed && m_socket == null)
                    {
                        m_socket = socket;
                        socket = null;
                    }
#pragma warning restore CA1508 // Avoid dead conditional code
                }
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(
                    ex,
                    "Failed to connect socket to {IdnHost}:{Port}.",
                    endpointUrl.IdnHost,
                    port);
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

        /// <summary>
        /// Forcefully closes the socket.
        /// </summary>
        public void Close()
        {
            Socket? socket;
            lock (m_socketLock)
            {
                socket = m_socket;
                m_socket = null;
                m_closed = true;
            }

            // Shutdown the socket.
            if (socket != null)
            {
                ShutdownAndDispose(socket);
            }
        }

        /// <summary>
        /// Starts reading messages from the socket.
        /// </summary>
        public async Task ReadNextMessageAsync(CancellationToken ct = default)
        {
            byte[]? receiveBuffer = null;
            try
            {
                while (true)
                {
                    receiveBuffer = m_bufferManager.TakeBuffer(
                        m_receiveBufferSize,
                        "ReadNextMessageAsync");

                    // Read the fixed-size message header (type + size = 8 bytes).
                    int headerRead = await ReceiveExactAsync(
                        receiveBuffer,
                        0,
                        TcpMessageLimits.MessageTypeAndSize,
                        ct).ConfigureAwait(false);

                    if (headerRead == 0)
                    {
                        m_bufferManager.ReturnBuffer(receiveBuffer, "ReadNextMessageAsync");
                        receiveBuffer = null;
                        m_sink?.OnReceiveError(this,
                            ServiceResult.Create(
                                StatusCodes.BadConnectionClosed,
                                "Remote side closed connection."));
                        return;
                    }

                    // Validate the message type.
                    uint messageType = BitConverter.ToUInt32(receiveBuffer, 0);
                    if (!TcpMessageType.IsValid(messageType))
                    {
                        m_bufferManager.ReturnBuffer(receiveBuffer, "ReadNextMessageAsync");
                        receiveBuffer = null;
                        m_sink?.OnReceiveError(this,
                            ServiceResult.Create(
                                StatusCodes.BadTcpMessageTypeInvalid,
                                "Message type {0:X8} is invalid.",
                                messageType));
                        return;
                    }

                    // Validate the declared message size.
                    int messageSize = BitConverter.ToInt32(receiveBuffer, 4);
                    if (messageSize <= 0 || messageSize > m_receiveBufferSize)
                    {
                        m_bufferManager.ReturnBuffer(receiveBuffer, "ReadNextMessageAsync");
                        receiveBuffer = null;
                        m_sink?.OnReceiveError(this,
                            ServiceResult.Create(
                                StatusCodes.BadTcpMessageTooLarge,
                                "Messages size {0} bytes is too large for buffer of size {1}.",
                                messageSize,
                                m_receiveBufferSize));
                        return;
                    }

                    // Read the remainder of the message body.
                    int remaining = messageSize - TcpMessageLimits.MessageTypeAndSize;
                    if (remaining > 0)
                    {
                        int bodyRead = await ReceiveExactAsync(
                            receiveBuffer,
                            TcpMessageLimits.MessageTypeAndSize,
                            remaining,
                            ct).ConfigureAwait(false);

                        if (bodyRead == 0)
                        {
                            m_bufferManager.ReturnBuffer(receiveBuffer, "ReadNextMessageAsync");
                            receiveBuffer = null;
                            m_sink?.OnReceiveError(this,
                                ServiceResult.Create(
                                    StatusCodes.BadConnectionClosed,
                                    "Remote side closed connection."));
                            return;
                        }
                    }

                    // Deliver the complete message chunk to the sink.
                    IMessageSink? sink = m_sink;
                    if (sink != null)
                    {
                        var messageChunk = new ArraySegment<byte>(receiveBuffer, 0, messageSize);
                        receiveBuffer = null; // sink now owns the buffer
                        try
                        {
                            sink.OnMessageReceived(this, messageChunk);
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogError(
                                ex,
                                "Unexpected error invoking OnMessageReceived callback.");
                        }
                    }
                    else
                    {
                        m_bufferManager.ReturnBuffer(receiveBuffer, "ReadNextMessageAsync");
                        receiveBuffer = null;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Normal cancellation - loop exits silently.
            }
            catch (Exception ex)
            {
                m_sink?.OnReceiveError(this,
                    ServiceResult.Create(
                        ex,
                        StatusCodes.BadTcpInternalError,
                        "Unexpected error receiving data."));
            }
            finally
            {
                if (receiveBuffer != null)
                {
                    m_bufferManager.ReturnBuffer(receiveBuffer, "ReadNextMessageAsync");
                }
            }
        }

        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes starting at
        /// <paramref name="offset"/> in <paramref name="buffer"/>.
        /// Returns 0 if the remote side closed the connection before any bytes were read.
        /// </summary>
        private async Task<int> ReceiveExactAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken ct)
        {
            int totalReceived = 0;
            while (totalReceived < count)
            {
                int received = await ReceiveAsync(
                    buffer,
                    offset + totalReceived,
                    count - totalReceived,
                    ct).ConfigureAwait(false);

                if (received == 0)
                {
                    // Connection closed without sending all bytes.
                    return 0;
                }

                totalReceived += received;
            }

            return totalReceived;
        }

#if NETFRAMEWORK
        /// <summary>
        /// Single async receive call wrapped in a Task (legacy .NET Framework path).
        /// </summary>
        private Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            Socket? socket;
            lock (m_socketLock)
            {
                socket = m_socket;
            }

            if (socket == null || !socket.Connected)
            {
                return Task.FromResult(0);
            }

            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var args = new SocketAsyncEventArgs();
            args.SetBuffer(buffer, offset, count);
            CancellationTokenRegistration registration = ct.Register(
                static state => ((TaskCompletionSource<int>)state!).TrySetCanceled(),
                tcs);

            args.Completed += (_, e) =>
            {
                registration.Dispose();
                if (e.SocketError != SocketError.Success)
                {
                    tcs.TrySetException(new SocketException((int)e.SocketError));
                }
                else
                {
                    tcs.TrySetResult(e.BytesTransferred);
                }

                e.Dispose();
            };

            try
            {
                if (!socket.ReceiveAsync(args))
                {
                    // Completed synchronously.
                    registration.Dispose();
                    SocketError socketError = args.SocketError;
                    int bytesTransferred = args.BytesTransferred;
                    args.Dispose();

                    if (socketError != SocketError.Success)
                    {
                        return Task.FromException<int>(new SocketException((int)socketError));
                    }

                    return Task.FromResult(bytesTransferred);
                }
            }
            catch (Exception ex)
            {
                registration.Dispose();
                args.Dispose();
                return Task.FromException<int>(ex);
            }

            return tcs.Task;
        }
#else
        /// <summary>
        /// Single async receive call using the modern <see cref="Memory{T}"/> API.
        /// </summary>
        private ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            Socket? socket;
            lock (m_socketLock)
            {
                socket = m_socket;
            }

            if (socket == null || !socket.Connected)
            {
                return new ValueTask<int>(0);
            }

            return socket.ReceiveAsync(buffer.AsMemory(offset, count), SocketFlags.None, ct);
        }
#endif

        /// <summary>
        /// Changes the sink used to report reads.
        /// </summary>
        public void ChangeSink(IMessageSink sink)
        {
            m_sink = sink;
        }

        /// <inheritdoc/>
        public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            if (m_socket == null)
            {
                throw new InvalidOperationException("The socket is not connected.");
            }

            return SendAllAsync(buffer, ct);
        }

        /// <inheritdoc/>
        public ValueTask SendAsync(IList<ArraySegment<byte>> buffers, CancellationToken ct = default)
        {
            if (m_socket == null)
            {
                throw new InvalidOperationException("The socket is not connected.");
            }

            return SendBufferListAsync(buffers, ct);
        }

        private async ValueTask SendAllAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            while (data.Length > 0)
            {
                int sent = await SendOnceAsync(data, ct).ConfigureAwait(false);
                if (sent == 0)
                {
                    throw new SocketException((int)SocketError.ConnectionReset);
                }

                data = data.Slice(sent);
            }
        }

        private async ValueTask SendBufferListAsync(IList<ArraySegment<byte>> buffers, CancellationToken ct)
        {
            foreach (ArraySegment<byte> segment in buffers)
            {
                await SendAllAsync(
                    new ReadOnlyMemory<byte>(segment.Array, segment.Offset, segment.Count),
                    ct).ConfigureAwait(false);
            }
        }

#if NETFRAMEWORK
        private Task<int> SendOnceAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            Socket? socket;
            lock (m_socketLock)
            {
                socket = m_socket;
            }

            if (socket == null)
            {
                return Task.FromResult(0);
            }

            if (!MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment))
            {
                segment = new ArraySegment<byte>(data.ToArray());
            }

            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var args = new SocketAsyncEventArgs();
            args.SetBuffer(segment.Array, segment.Offset, segment.Count);
            CancellationTokenRegistration registration = ct.Register(
                static state => ((TaskCompletionSource<int>)state!).TrySetCanceled(),
                tcs);

            args.Completed += (_, e) =>
            {
                registration.Dispose();
                if (e.SocketError != SocketError.Success)
                {
                    tcs.TrySetException(new SocketException((int)e.SocketError));
                }
                else
                {
                    tcs.TrySetResult(e.BytesTransferred);
                }

                e.Dispose();
            };

            try
            {
                if (!socket.SendAsync(args))
                {
                    registration.Dispose();
                    SocketError socketError = args.SocketError;
                    int bytesTransferred = args.BytesTransferred;
                    args.Dispose();

                    if (socketError != SocketError.Success)
                    {
                        return Task.FromException<int>(new SocketException((int)socketError));
                    }

                    return Task.FromResult(bytesTransferred);
                }
            }
            catch (Exception ex)
            {
                registration.Dispose();
                args.Dispose();
                return Task.FromException<int>(ex);
            }

            return tcs.Task;
        }
#else
        private ValueTask<int> SendOnceAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            Socket? socket;
            lock (m_socketLock)
            {
                socket = m_socket;
            }

            if (socket == null)
            {
                return new ValueTask<int>(0);
            }

            return socket.SendAsync(data, SocketFlags.None, ct);
        }
#endif

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
                // socket.Shutdown may throw but can be ignored
                m_logger.LogDebug(e, "Unexpected error closing socket.");
            }
            finally
            {
                socket.Dispose();
            }
        }

        private IMessageSink m_sink;
        private readonly ILogger m_logger;
        private readonly BufferManager m_bufferManager;
        private readonly int m_receiveBufferSize;

        private readonly Lock m_socketLock = new();
        private Socket? m_socket;
        private bool m_closed;
    }
}
