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

#nullable enable

using System;
using System.Diagnostics;
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
            m_incomingMessageSize = -1;
            m_readComplete = OnReadComplete;
            m_readState = ReadState.Ready;
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
            m_incomingMessageSize = -1;
            m_readComplete = OnReadComplete;
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
                await socket.ConnectAsync(endpoint).ConfigureAwait(false);

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
        public void ReadNextMessage()
        {
            lock (m_readLock)
            {
                do
                {
                    // allocate a buffer large enough to a message chunk.
                    m_receiveBuffer ??= m_bufferManager.TakeBuffer(
                        m_receiveBufferSize,
                        "ReadNextMessage");

                    // read the first 8 bytes of the message which contains the message size.
                    m_bytesReceived = 0;
                    m_bytesToReceive = TcpMessageLimits.MessageTypeAndSize;
                    m_incomingMessageSize = -1;

                    do
                    {
                        ReadNextBlock();
                    } while (m_readState == ReadState.ReadNextBlock);
                } while (m_readState == ReadState.ReadNextMessage);
            }
        }

        /// <summary>
        /// Changes the sink used to report reads.
        /// </summary>
        public void ChangeSink(IMessageSink sink)
        {
            lock (m_readLock)
            {
                m_sink = sink;
            }
        }

        /// <summary>
        /// Handles a read complete event.
        /// </summary>
        private void OnReadComplete(object? sender, SocketAsyncEventArgs e)
        {
            lock (m_readLock)
            {
                ServiceResult? error = null;

                try
                {
                    bool innerCall = m_readState == ReadState.ReadComplete;
                    error = DoReadComplete(e);
                    // to avoid recursion, inner calls of OnReadComplete return
                    // after processing the ReadComplete and let the outer call handle it
                    if (!innerCall && !ServiceResult.IsBad(error))
                    {
                        while (ReadNext())
                        {
                        }
                    }
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex, "Unexpected error during OnReadComplete,");
                    error = ServiceResult.Create(ex, StatusCodes.BadTcpInternalError, ex.Message);
                }
                finally
                {
                    e?.Dispose();
                }

                if (m_readState == ReadState.NotConnected && ServiceResult.IsGood(error))
                {
                    error = ServiceResult.Create(
                        StatusCodes.BadConnectionClosed,
                        "Remote side closed connection.");
                }

                if (ServiceResult.IsBad(error))
                {
                    if (m_receiveBuffer != null)
                    {
                        m_bufferManager.ReturnBuffer(m_receiveBuffer, "OnReadComplete");
                        m_receiveBuffer = null;
                    }

                    m_sink?.OnReceiveError(this, error);
                }
            }
        }

        /// <summary>
        /// Handles a read complete event.
        /// </summary>
        private ServiceResult DoReadComplete(SocketAsyncEventArgs e)
        {
            // complete operation.
            int bytesRead = e.BytesTransferred;
            m_readState = ReadState.Ready;

            lock (m_socketLock)
            {
                if (m_receiveBuffer != null)
                {
                    BufferManager.UnlockBuffer(m_receiveBuffer);
                }
            }

            if (bytesRead == 0)
            {
                // Remote end has closed the connection

                // free the empty receive buffer.
                if (m_receiveBuffer != null)
                {
                    m_bufferManager.ReturnBuffer(m_receiveBuffer, "DoReadComplete");
                    m_receiveBuffer = null;
                }

                m_readState = ReadState.Error;
                return ServiceResult.Create(
                    StatusCodes.BadConnectionClosed,
                    "Remote side closed connection");
            }

            m_bytesReceived += bytesRead;

            // check if more data left to read.
            if (m_bytesReceived < m_bytesToReceive)
            {
                m_readState = ReadState.ReadNextBlock;
                return ServiceResult.Good;
            }

            // start reading the message body.
            if (m_receiveBuffer != null)
            {
                if (m_incomingMessageSize < 0)
                {
                    uint messageType = BitConverter.ToUInt32(m_receiveBuffer, 0);
                    if (!TcpMessageType.IsValid(messageType))
                    {
                        m_readState = ReadState.Error;

                        return ServiceResult.Create(
                            StatusCodes.BadTcpMessageTypeInvalid,
                            "Message type {0:X8} is invalid.",
                            messageType);
                    }

                    m_incomingMessageSize = BitConverter.ToInt32(m_receiveBuffer, 4);
                    if (m_incomingMessageSize <= 0 || m_incomingMessageSize > m_receiveBufferSize)
                    {
                        m_readState = ReadState.Error;

                        return ServiceResult.Create(
                            StatusCodes.BadTcpMessageTooLarge,
                            "Messages size {0} bytes is too large for buffer of size {1}.",
                            m_incomingMessageSize,
                            m_receiveBufferSize);
                    }

                    // set up buffer for reading the message body.
                    m_bytesToReceive = m_incomingMessageSize;

                    m_readState = ReadState.ReadNextBlock;

                    return ServiceResult.Good;
                }

                // notify the sink.
                IMessageSink sink = m_sink;
                if (sink != null)
                {
                    try
                    {
                        // send notification (implementor responsible for freeing buffer) on success.
                        var messageChunk = new ArraySegment<byte>(
                            m_receiveBuffer,
                            0,
                            m_incomingMessageSize);

                        // must allocate a new buffer for the next message.
                        m_receiveBuffer = null;

                        sink.OnMessageReceived(this, messageChunk);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(ex, "Unexpected error invoking OnMessageReceived callback.");
                    }
                }
            }

            // free the receive buffer.
            if (m_receiveBuffer != null)
            {
                m_bufferManager.ReturnBuffer(m_receiveBuffer, "DoReadComplete");
                m_receiveBuffer = null;
            }

            // start receiving next message.
            m_readState = ReadState.ReadNextMessage;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Reads the next block of data from the socket.
        /// </summary>
        private void ReadNextBlock()
        {
            Socket? socket;

            // check if already closed.
            lock (m_socketLock)
            {
                socket = m_socket;

                if (socket == null || !socket.Connected)
                {
                    // buffer is returned in calling code
                    m_readState = ReadState.NotConnected;
                    return;
                }
            }

            BufferManager.LockBuffer(m_receiveBuffer);

            var args = new SocketAsyncEventArgs();
            try
            {
                m_readState = ReadState.Receive;
                args.SetBuffer(
                    m_receiveBuffer,
                    m_bytesReceived,
                    m_bytesToReceive - m_bytesReceived);
                args.Completed += m_readComplete;
                if (!socket.ReceiveAsync(args))
                {
                    // I/O completed synchronously
                    if (args.SocketError != SocketError.Success)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadTcpInternalError,
                            args.SocketError.ToString());
                    }
                    // set state to inner complete
                    m_readState = ReadState.ReadComplete;
                    m_readComplete(null, args);
                }
            }
            catch (ServiceResultException)
            {
                args?.Dispose();
                BufferManager.UnlockBuffer(m_receiveBuffer);
                throw;
            }
            catch (Exception ex)
            {
                args?.Dispose();
                BufferManager.UnlockBuffer(m_receiveBuffer);
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpInternalError,
                    ex,
                    "BeginReceive failed.");
            }
        }

        /// <summary>
        /// Helper to read next block or message based on current state.
        /// </summary>
        private bool ReadNext()
        {
            switch (m_readState)
            {
                case ReadState.ReadNextBlock:
                    ReadNextBlock();
                    return true;
                case ReadState.ReadNextMessage:
                    ReadNextMessage();
                    return true;
                case ReadState.Ready:
                case ReadState.Receive:
                case ReadState.ReadComplete:
                case ReadState.NotConnected:
                case ReadState.Error:
                    return false;
                default:
                    Debug.Fail("Unexpected read state.");
                    return false;
            }
        }

        /// <summary>
        /// Sends a buffer.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public bool Send(IMessageSocketAsyncEventArgs args)
        {
            if (args is not TcpMessageSocketAsyncEventArgs eventArgs)
            {
                throw new ArgumentNullException(nameof(args));
            }
            if (m_socket == null)
            {
                throw new InvalidOperationException("The socket is not connected.");
            }
            eventArgs.Args.SocketError = SocketError.NotConnected;
            return m_socket.SendAsync(eventArgs.Args);
        }

        /// <summary>
        /// Create event args for TcpMessageSocket.
        /// </summary>
        public IMessageSocketAsyncEventArgs MessageSocketEventArgs()
        {
            return new TcpMessageSocketAsyncEventArgs();
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
        private readonly EventHandler<SocketAsyncEventArgs> m_readComplete;

        private readonly Lock m_socketLock = new();
        private Socket? m_socket;
        private bool m_closed;

        /// <summary>
        /// States for the nested read handler.
        /// </summary>
        private enum ReadState
        {
            Ready = 0,
            ReadNextMessage = 1,
            ReadNextBlock = 2,
            Receive = 3,
            ReadComplete = 4,
            NotConnected = 5,
            Error = 0xff
        }

        private readonly Lock m_readLock = new();
        private byte[]? m_receiveBuffer;
        private int m_bytesReceived;
        private int m_bytesToReceive;
        private int m_incomingMessageSize;
        private ReadState m_readState;
    }
}
