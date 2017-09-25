/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a transport channel with UA-TCP transport, UA-SC security and UA Binary encoding
    /// </summary>

    public class TcpTransportChannel : UaSCUaBinaryTransportChannel
    {
        public TcpTransportChannel() :
            base(new TcpMessageSocketFactory())
        {
        }
    }

    /// <summary>
    /// Creates a new TcpTransportChannel with ITransportChannel interface.
    /// </summary>
    public class TcpTransportChannelFactory : ITransportChannelFactory
    {
        /// <summary>
        /// The method creates a new instance of a TCP transport channel
        /// </summary>
        /// <returns> the transport channel</returns>
        public ITransportChannel Create()
        {
            return new TcpTransportChannel();
        }
    }

    /// <summary>
    /// Handles async event callbacks from a socket
    /// </summary>
    public class TcpMessageSocketAsyncEventArgs : IMessageSocketAsyncEventArgs
    {
        public TcpMessageSocketAsyncEventArgs()
        {
            m_args = new SocketAsyncEventArgs();
            m_args.UserToken = this;
        }

        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            m_args.Dispose();
        }
        #endregion

        public object UserToken
        {
            get { return m_UserToken; }
            set { m_UserToken = value; }
        }

        public void SetBuffer(byte[] buffer, int offset, int count)
        {
            m_args.SetBuffer(buffer, offset, count);
        }

        public bool IsSocketError
        {
            get { return m_args.SocketError != SocketError.Success; }
        }
        public string SocketErrorString
        {
            get { return m_args.SocketError.ToString(); }
        }

        public event EventHandler<IMessageSocketAsyncEventArgs> Completed
        {
            add
            {
                m_internalComplete += value;
                m_args.Completed += OnComplete;
            }
            remove
            {
                m_internalComplete -= value;
                m_args.Completed -= OnComplete;
            }
        }

        protected void OnComplete(object sender, SocketAsyncEventArgs e)
        {
            if (e.UserToken == null) return;
            m_internalComplete(this, e.UserToken as IMessageSocketAsyncEventArgs);
        }

        public int BytesTransferred
        {
            get { return m_args.BytesTransferred; }
        }

        public byte[] Buffer
        {
            get { return m_args.Buffer; }
        }

        public BufferCollection BufferList
        {
            get { return m_args.BufferList as BufferCollection; }
            set { m_args.BufferList = value; }
        }

        public SocketAsyncEventArgs m_args;
        private object m_UserToken;
        private event EventHandler<IMessageSocketAsyncEventArgs> m_internalComplete;
    }

    /// <summary>
    /// Creates a new TcpMessageSocket with IMessageSocket interface.
    /// </summary>
    public class TcpMessageSocketFactory : IMessageSocketFactory
    {
        /// <summary>
        /// The method creates a new instance of a UA-TCP message socket
        /// </summary>
        /// <returns> the message socket</returns>
        public IMessageSocket Create(
                IMessageSink sink,
                BufferManager bufferManager,
                int receiveBufferSize
            )
        {
            return new TcpMessageSocket(sink, bufferManager, receiveBufferSize);
        }

        /// <summary>
        /// Gets the implementation description.
        /// </summary>
        /// <value>The implementation string.</value>
        public string Implementation { get { return "UA-TCP"; } }

    }


    /// <summary>
    /// Handles reading and writing of message chunks over a socket.
    /// </summary>
    public class TcpMessageSocket : IMessageSocket
    {
        #region Constructors
        /// <summary>
        /// Creates an unconnected socket.
        /// </summary>
        public TcpMessageSocket(
            IMessageSink sink,
            BufferManager bufferManager,
            int receiveBufferSize)
        {
            if (bufferManager == null) throw new ArgumentNullException("bufferManager");

            m_sink = sink;
            m_socket = null;
            m_bufferManager = bufferManager;
            m_receiveBufferSize = receiveBufferSize;
            m_incomingMessageSize = -1;
            m_ReadComplete = new EventHandler<SocketAsyncEventArgs>(OnReadComplete);
        }

        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public TcpMessageSocket(
            IMessageSink sink,
            Socket socket,
            BufferManager bufferManager,
            int receiveBufferSize)
        {
            if (socket == null) throw new ArgumentNullException("socket");
            if (bufferManager == null) throw new ArgumentNullException("bufferManager");

            m_sink = sink;
            m_socket = socket;
            m_bufferManager = bufferManager;
            m_receiveBufferSize = receiveBufferSize;
            m_incomingMessageSize = -1;
            m_ReadComplete = new EventHandler<SocketAsyncEventArgs>(OnReadComplete);
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_socket.Dispose();
            }
        }
        #endregion

        #region Connect/Disconnect Handling
        /// <summary>
        /// Gets the socket handle.
        /// </summary>
        /// <value>The socket handle.</value>
        public int Handle
        {
            get
            {
                if (m_socket != null)
                {
                    return m_socket.GetHashCode();
                }

                return -1;
            }
        }

        /// <summary>
        /// Connects to an endpoint.
        /// </summary>
        public async Task BeginConnect(Uri endpointUrl, EventHandler<IMessageSocketAsyncEventArgs> callback, object state)
        {
            if (endpointUrl == null) throw new ArgumentNullException("endpointUrl");

            if (m_socket != null)
            {
                throw new InvalidOperationException("The socket is already connected.");
            }

            // Get DNS host information
            IPAddress[] hostAdresses = await Dns.GetHostAddressesAsync(endpointUrl.DnsSafeHost);

            // Get IPv4 and IPv6 address
            IPAddress addressV4 = hostAdresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            IPAddress addressV6 = hostAdresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6);

            // Get port
            int port = endpointUrl.Port;
            if (port <= 0 || port > UInt16.MaxValue)
            {
                port = Utils.UaTcpDefaultPort;
            }

            Action doCallback = () => callback(this, new TcpMessageSocketAsyncEventArgs { UserToken = state });

            if (addressV6 != null)
            {
                BeginConnect(addressV6, AddressFamily.InterNetworkV6, port, doCallback);
            }

            if (addressV4 != null && m_socket == null)
            {
                BeginConnect(addressV4, AddressFamily.InterNetwork, port, doCallback);
            }
        }

        /// <summary>
        /// Try to connect to endpoint and do callback if connected successfully
        /// </summary>
        /// <param name="address">Endpoint address</param>
        /// <param name="addressFamily">Endpoint address family</param>
        /// <param name="port">Endpoint port</param>
        /// <param name="callback">Callback that must be executed if the connection would be established</param>
        private void BeginConnect(IPAddress address, AddressFamily addressFamily, int port, Action callback)
        {
            var socket = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
            var args = new SocketAsyncEventArgs()
            {
                UserToken = callback,
                RemoteEndPoint = new IPEndPoint(address, port),
            };
            args.Completed += new EventHandler<SocketAsyncEventArgs>(OnSocketConnected);

            var result = !socket.ConnectAsync(args);
            if (result)
            {
                // I/O completed synchronously
                OnSocketConnected(socket, args);
            }
        }

        /// <summary>
        /// Handle socket connection event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void OnSocketConnected(object sender, SocketAsyncEventArgs args)
        {
            lock (m_socketLock)
            {
                var socket = sender as Socket;
                if (!m_closed && m_socket == null && args.SocketError == SocketError.Success)
                {
                    m_socket = socket;
                    ((Action)args.UserToken)();
                }
                else
                {
                    try
                    {
                        if (socket.Connected)
                        {
                            socket.Shutdown(SocketShutdown.Both);
                        }
                    }
                    finally
                    {
                        socket.Dispose();
                    }
                }
            }
            args.Dispose();
        }

        /// <summary>
        /// Forcefully closes the socket.
        /// </summary>
        public void Close()
        {
            lock (m_socketLock)
            {
                m_closed = true;

                // Shutdown the socket.
                if (m_socket != null)
                {
                    try
                    {
                        if (m_socket.Connected)
                        {
                            m_socket.Shutdown(SocketShutdown.Both);
                        }
                    }
                    catch (Exception e)
                    {
                        Utils.Trace(e, "Unexpected error closing socket.");
                    }
                    finally
                    {
                        m_socket.Dispose();
                        m_socket = null;
                    }
                }
            }
        }
        #endregion

        #region Read Handling
        /// <summary>
        /// Starts reading messages from the socket.
        /// </summary>
        public void ReadNextMessage()
        {
            lock (m_readLock)
            {
                // allocate a buffer large enough to a message chunk.
                if (m_receiveBuffer == null)
                {
                    m_receiveBuffer = m_bufferManager.TakeBuffer(m_receiveBufferSize, "ReadNextMessage");
                }

                // read the first 8 bytes of the message which contains the message size.          
                m_bytesReceived = 0;
                m_bytesToReceive = TcpMessageLimits.MessageTypeAndSize;
                m_incomingMessageSize = -1;

                ReadNextBlock();
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
        private void OnReadComplete(object sender, SocketAsyncEventArgs e)
        {
            lock (m_readLock)
            {
                ServiceResult error = null;

                try
                {
                    error = DoReadComplete(e);
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "Unexpected error during OnReadComplete,");
                    error = ServiceResult.Create(ex, StatusCodes.BadTcpInternalError, ex.Message);
                }
                finally
                {
                    e.Dispose();
                }

                if (ServiceResult.IsBad(error))
                {
                    if (m_receiveBuffer != null)
                    {
                        m_bufferManager.ReturnBuffer(m_receiveBuffer, "OnReadComplete");
                        m_receiveBuffer = null;
                    }

                    if (m_sink != null)
                    {
                        m_sink.OnReceiveError(this, error);
                    }
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

            lock (m_socketLock)
            {
                BufferManager.UnlockBuffer(m_receiveBuffer);
            }

            Utils.TraceDebug("Bytes read: {0}", bytesRead);

            if (bytesRead == 0)
            {
                // Remote end has closed the connection

                // free the empty receive buffer.
                if (m_receiveBuffer != null)
                {
                    m_bufferManager.ReturnBuffer(m_receiveBuffer, "DoReadComplete");
                    m_receiveBuffer = null;
                }

                return ServiceResult.Create(StatusCodes.BadConnectionClosed, "Remote side closed connection");
            }

            m_bytesReceived += bytesRead;

            // check if more data left to read.
            if (m_bytesReceived < m_bytesToReceive)
            {
                ReadNextBlock();

                return ServiceResult.Good;
            }

            // start reading the message body.
            if (m_incomingMessageSize < 0)
            {
                m_incomingMessageSize = BitConverter.ToInt32(m_receiveBuffer, 4);

                if (m_incomingMessageSize <= 0 || m_incomingMessageSize > m_receiveBufferSize)
                {
                    Utils.Trace(
                        "BadTcpMessageTooLarge: BufferSize={0}; MessageSize={1}",
                        m_receiveBufferSize,
                        m_incomingMessageSize);

                    return ServiceResult.Create(
                        StatusCodes.BadTcpMessageTooLarge,
                        "Messages size {1} bytes is too large for buffer of size {0}.",
                        m_receiveBufferSize,
                        m_incomingMessageSize);
                }

                // set up buffer for reading the message body.
                m_bytesToReceive = m_incomingMessageSize;

                ReadNextBlock();

                return ServiceResult.Good;
            }

            // notify the sink.
            if (m_sink != null)
            {
                try
                {
                    // send notification (implementor responsible for freeing buffer) on success.
                    ArraySegment<byte> messageChunk = new ArraySegment<byte>(m_receiveBuffer, 0, m_incomingMessageSize);

                    // must allocate a new buffer for the next message.
                    m_receiveBuffer = null;

                    m_sink.OnMessageReceived(this, messageChunk);
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "Unexpected error invoking OnMessageReceived callback.");
                }
            }

            // free the receive buffer.
            if (m_receiveBuffer != null)
            {
                m_bufferManager.ReturnBuffer(m_receiveBuffer, "DoReadComplete");
                m_receiveBuffer = null;
            }

            // start receiving next message.
            ReadNextMessage();

            return ServiceResult.Good;
        }

        /// <summary>
        /// Reads the next block of data from the socket.
        /// </summary>
        private void ReadNextBlock()
        {
            Socket socket = null;

            // check if already closed.
            lock (m_socketLock)
            {
                if (m_socket == null)
                {
                    if (m_receiveBuffer != null)
                    {
                        m_bufferManager.ReturnBuffer(m_receiveBuffer, "ReadNextBlock");
                        m_receiveBuffer = null;
                    }

                    return;
                }

                socket = m_socket;

                // avoid stale ServiceException when socket is disconnected
                if (!socket.Connected)
                {
                    return;
                }

            }

            BufferManager.LockBuffer(m_receiveBuffer);

            ServiceResult error = ServiceResult.Good;
            SocketAsyncEventArgs args = new SocketAsyncEventArgs();
            try
            {
                args.SetBuffer(m_receiveBuffer, m_bytesReceived, m_bytesToReceive - m_bytesReceived);
                args.Completed += m_ReadComplete;
                if (!socket.ReceiveAsync(args))
                {
                    // I/O completed synchronously
                    if (args.SocketError != SocketError.Success)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadTcpInternalError, args.SocketError.ToString());
                    }
                    else
                    {
                        m_ReadComplete(null, args);
                    }
                }
            }
            catch (ServiceResultException sre)
            {
                args.Dispose();
                BufferManager.UnlockBuffer(m_receiveBuffer);
                throw sre;
            }
            catch (Exception ex)
            {
                args.Dispose();
                BufferManager.UnlockBuffer(m_receiveBuffer);
                throw ServiceResultException.Create(StatusCodes.BadTcpInternalError, ex, "BeginReceive failed.");
            }
        }
        #endregion
        #region Write Handling
        /// <summary>
        /// Sends a buffer.
        /// </summary>
        public bool SendAsync(IMessageSocketAsyncEventArgs args)
        {
            TcpMessageSocketAsyncEventArgs eventArgs = args as TcpMessageSocketAsyncEventArgs;
            if (eventArgs == null)
            {
                throw new ArgumentNullException("args");
            }
            if (m_socket == null)
            {
                throw new InvalidOperationException("The socket is not connected.");
            }
            eventArgs.m_args.SocketError = SocketError.NotConnected;
            return m_socket.SendAsync(eventArgs.m_args);
        }
        #endregion
        #region Event factory
        public IMessageSocketAsyncEventArgs MessageSocketEventArgs()
        {
            return new TcpMessageSocketAsyncEventArgs();
        }
        #endregion
        #region Private Fields
        private IMessageSink m_sink;
        private BufferManager m_bufferManager;
        private int m_receiveBufferSize;
        private EventHandler<SocketAsyncEventArgs> m_ReadComplete;

        private object m_socketLock = new object();
        private Socket m_socket;
        private bool m_closed = false;

        private object m_readLock = new object();
        private byte[] m_receiveBuffer;
        private int m_bytesReceived;
        private int m_bytesToReceive;
        private int m_incomingMessageSize;
        #endregion
    }
}
