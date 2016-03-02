/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// An interface to an object that received messages from the socket.
    /// </summary>
    public interface IMessageSink
    {
        /// <summary>
        /// Called when a new message arrives.
        /// </summary>
        void OnMessageReceived(TcpMessageSocket source, ArraySegment<byte> message);

        /// <summary>
        /// Called when an error occurs during a read.
        /// </summary>
        void OnReceiveError(TcpMessageSocket source, ServiceResult result);
    }

    /// <summary>
    /// Handles reading and writing of message chunks over a socket.
    /// </summary>
    public class TcpMessageSocket : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Creates an unconnected socket.
        /// </summary>
        public TcpMessageSocket(
            IMessageSink  sink, 
            BufferManager bufferManager, 
            int           receiveBufferSize)
        {
            if (bufferManager == null) throw new ArgumentNullException("bufferManager");
            
            m_sink = sink;
            m_socket = null;
            m_socketV4 = null;
            m_socketV6 = null;
            m_bufferManager = bufferManager;
            m_receiveBufferSize = receiveBufferSize;
            m_incomingMessageSize = -1;
            m_ReadComplete = new EventHandler<SocketAsyncEventArgs>(OnReadComplete);
        }

        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public TcpMessageSocket(
            IMessageSink  sink, 
            Socket  socket, 
            BufferManager bufferManager, 
            int           receiveBufferSize)
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
        public async Task<bool> BeginConnect(Uri endpointUrl, EventHandler<SocketAsyncEventArgs> callback, object state)
        {
            if (endpointUrl == null) throw new ArgumentNullException("endpointUrl");

            if (m_socket != null)
            {
                throw new InvalidOperationException("The socket is already connected.");
            }

            bool ipV6Required = false;
            bool ipV4Required = false;

            // need to check if an IP address was provided.
            IPAddress address = null;

            if (IPAddress.TryParse(endpointUrl.DnsSafeHost, out address))
            {
                ipV6Required = address.AddressFamily == AddressFamily.InterNetworkV6;
                ipV4Required = address.AddressFamily == AddressFamily.InterNetwork;
            }

            TaskCompletionSource<SocketError> tcs = new TaskCompletionSource<SocketError>();
            SocketAsyncEventArgs argsV4 = new SocketAsyncEventArgs();
            SocketAsyncEventArgs argsV6 = new SocketAsyncEventArgs();
            argsV4.UserToken = argsV6.UserToken = state;
            m_socketResponses = 0;

            lock (m_socketLock)
            {
                // force sockets if IP address was provided
                if (!ipV4Required)
                {
                    m_socketV6 = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
                    m_socketResponses++;
                }
                if (!ipV6Required)
                {
                    m_socketV4 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    m_socketResponses++;
                }

                // ensure a valid port.
                int port = endpointUrl.Port;

                if (port <= 0 || port > UInt16.MaxValue)
                {
                    port = Utils.UaTcpDefaultPort;
                }

                if (address != null)
                {
                    argsV4.RemoteEndPoint = new IPEndPoint(address, port);
                    argsV6.RemoteEndPoint = new IPEndPoint(address, port);
                }
                else
                {
                    argsV4.RemoteEndPoint = new DnsEndPoint(endpointUrl.DnsSafeHost, port);
                    argsV6.RemoteEndPoint = new DnsEndPoint(endpointUrl.DnsSafeHost, port);
                }

                argsV4.Completed += (o, e) =>
                {
                    lock (m_socketLock)
                    {
                        m_socketResponses--;
                        if (m_socketV4 != null)
                        {
                            if (m_socket == null &&
                            (m_socketResponses == 0 || e.SocketError == SocketError.Success))
                            {
                                m_socket = m_socketV4;
                                tcs.SetResult(e.SocketError);
                            }
                            else
                            {
                                m_socketV4.Dispose();
                                e.UserToken = null;
                            }
                            m_socketV4 = null;
                        }
                        else
                        {
                            e.UserToken = null;
                        }
                    }
                };
                argsV4.Completed += callback;

                argsV6.Completed += (o, e) =>
                {
                    lock (m_socketLock)
                    {
                        m_socketResponses--;
                        if (m_socketV6 != null)
                        {
                            if (m_socket == null &&
                                (m_socketResponses == 0 || e.SocketError == SocketError.Success))
                            {
                                m_socket = m_socketV6;
                                tcs.SetResult(e.SocketError);
                            }
                            else
                            {
                                m_socketV6.Dispose();
                                e.UserToken = null;
                            }
                            m_socketV6 = null;
                        }
                        else
                        {
                            e.UserToken = null;
                        }
                    }
                };
                argsV6.Completed += callback;

                bool connectV6Sync = true;
                bool connectV4Sync = true;
                if (m_socketV6 != null)
                {
                    connectV6Sync = !m_socketV6.ConnectAsync(argsV6);
                }
                if (m_socketV4 != null)
                {
                    connectV4Sync = !m_socketV4.ConnectAsync(argsV4);
                }

                if (connectV4Sync && connectV6Sync)
                {
                    // I/O completed synchronously
                    callback(this, argsV4);
                    return (argsV4.SocketError == SocketError.Success) ? true : false;
                }
            }

            SocketError error = await tcs.Task;

            return (error == SocketError.Success) ? true : false;
        }

        /// <summary>
        /// Forcefully closes the socket.
        /// </summary>
        public void Close()
        {
            // get the socket.
            Socket socket = null;
            Socket socketV4 = null;
            Socket socketV6 = null;

            lock (m_socketLock)
            {
                socket = m_socket;
                socketV4 = m_socketV4;
                socketV6 = m_socketV6;
                m_socket = null;
                m_socketV6 = null;
                m_socketV4 = null;
            }

            // shutdown sockets which may still be active
            // due to a timeout during ConnectAsync
            if (socketV4 != null)
            {
                socketV4.Dispose();
            }

            if (socketV6 != null)
            {
                socketV6.Dispose();
            }

            // shutdown the socket.
            if (socket != null)
            {
                try
                {
                    if (socket.Connected)
                    {
                        socket.Shutdown(SocketShutdown.Both);
                    }
                    
                    socket.Dispose();
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error closing socket.");
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
             
            if (bytesRead == 0)
            {
                // free the empty receive buffer.
                if (m_receiveBuffer != null)
                {
                    m_bufferManager.ReturnBuffer(m_receiveBuffer, "DoReadComplete");
                    m_receiveBuffer = null;
                }

                return ServiceResult.Good;
            }

            m_bytesReceived += bytesRead;

            // check if more data left to read.
            if (m_bytesReceived < m_bytesToReceive)
            {
                ReadNextBlock();
                
#if TRACK_MEMORY
                int cookie = BitConverter.ToInt32(m_receiveBuffer, 0);

                if (cookie < 0)
                {
                    Utils.Trace("BufferCookieError (ReadNextBlock): Cookie={0:X8}", cookie);
                }
#endif

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
                    if ((args.SocketError != SocketError.Success) || (args.BytesTransferred < (m_bytesToReceive - m_bytesReceived)))
                    {
                        BufferManager.UnlockBuffer(m_receiveBuffer);
                        throw ServiceResultException.Create(StatusCodes.BadTcpInternalError, null, args.SocketError.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                BufferManager.UnlockBuffer(m_receiveBuffer);
                throw ServiceResultException.Create(StatusCodes.BadTcpInternalError, ex, "BeginReceive failed.");
            }
        }
#endregion
        
        private IMessageSink m_sink; 
        private BufferManager m_bufferManager;
        private int m_receiveBufferSize;
        private EventHandler<SocketAsyncEventArgs> m_ReadComplete;
        
        private object m_socketLock = new object();
        public Socket m_socket;

        private object m_readLock = new object();
        private byte[] m_receiveBuffer;
        private int m_bytesReceived;
        private int m_bytesToReceive;
        private int m_incomingMessageSize;
        private Socket m_socketV4;
        private Socket m_socketV6;
        private int m_socketResponses;
    }
}
