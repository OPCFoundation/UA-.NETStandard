/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{

    /// <summary>
    /// Manages the server side of a UA TCP channel.
    /// </summary>
    public partial class UaSCUaBinaryChannel : IMessageSink, IDisposable
    {
        #region Constructors

        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public UaSCUaBinaryChannel(
            string contextId,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            X509Certificate2 serverCertificate,
            EndpointDescriptionCollection endpoints,
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        :
            this(contextId, bufferManager, quotas, serverCertificate, null, endpoints, securityMode, securityPolicyUri)
        { }

        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public UaSCUaBinaryChannel(
            string contextId,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            X509Certificate2 serverCertificate,
            X509Certificate2Collection serverCertificateChain,
            EndpointDescriptionCollection endpoints,
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            if (bufferManager == null) throw new ArgumentNullException(nameof(bufferManager));
            if (quotas == null) throw new ArgumentNullException(nameof(quotas));

            // create a unique contex if none provided.
            m_contextId = contextId;

            if (String.IsNullOrEmpty(m_contextId))
            {
                m_contextId = Guid.NewGuid().ToString();
            }

            // secuirty turned off if message security mode is set to none.
            if (securityMode == MessageSecurityMode.None)
            {
                securityPolicyUri = SecurityPolicies.None;
            }

            if (securityMode != MessageSecurityMode.None)
            {
                if (serverCertificate == null) throw new ArgumentNullException(nameof(serverCertificate));

                if (serverCertificate.RawData.Length > TcpMessageLimits.MaxCertificateSize)
                {
                    throw new ArgumentException(
                        Utils.Format("The DER encoded certificate may not be more than {0} bytes.", TcpMessageLimits.MaxCertificateSize),
                            nameof(serverCertificate));
                }
            }

            if (new UTF8Encoding().GetByteCount(securityPolicyUri) > TcpMessageLimits.MaxSecurityPolicyUriSize)
            {
                throw new ArgumentException(
                    Utils.Format("UTF-8 form of the security policy URI may not be more than {0} bytes.", TcpMessageLimits.MaxSecurityPolicyUriSize),
                        nameof(securityPolicyUri));
            }

            m_bufferManager = bufferManager;
            m_quotas = quotas;
            m_serverCertificate = serverCertificate;
            m_serverCertificateChain = serverCertificateChain;
            m_endpoints = endpoints;
            m_securityMode = securityMode;
            m_securityPolicyUri = securityPolicyUri;
            m_discoveryOnly = false;
            m_uninitialized = true;

            m_state = TcpChannelState.Closed;
            m_receiveBufferSize = quotas.MaxBufferSize;
            m_sendBufferSize = quotas.MaxBufferSize;

            if (m_receiveBufferSize < TcpMessageLimits.MinBufferSize)
            {
                m_receiveBufferSize = TcpMessageLimits.MinBufferSize;
            }

            if (m_receiveBufferSize > TcpMessageLimits.MaxBufferSize)
            {
                m_receiveBufferSize = TcpMessageLimits.MaxBufferSize;
            }

            if (m_sendBufferSize < TcpMessageLimits.MinBufferSize)
            {
                m_sendBufferSize = TcpMessageLimits.MinBufferSize;
            }

            if (m_sendBufferSize > TcpMessageLimits.MaxBufferSize)
            {
                m_sendBufferSize = TcpMessageLimits.MaxBufferSize;
            }

            m_maxRequestMessageSize = quotas.MaxMessageSize;
            m_maxResponseMessageSize = quotas.MaxMessageSize;

            CalculateSymmetricKeySizes();
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
                // nothing to do.
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// The identifier assigned to the channel by the server.
        /// </summary>
        public uint Id
        {
            get
            {
                lock (m_lock)
                {
                    return m_channelId;
                }
            }
        }

        /// <summary>
        /// The globally unique identifier assigned to the channel by the server.
        /// </summary>
        public string GlobalChannelId
        {
            get
            {
                lock (m_lock)
                {
                    return m_globalChannelId;
                }
            }
        }

        /// <summary>
        /// Raised when the state of the channel changes.
        /// </summary>
        public void SetStateChangedCallback(TcpChannelStateEventHandler callback)
        {
            lock (m_lock)
            {
                m_StateChanged = callback;
            }
        }
        #endregion

        #region Channel State Functions
        /// <summary>
        /// Reports that the channel state has changed (in another thread).
        /// </summary>
        protected void ChannelStateChanged(TcpChannelState state, ServiceResult reason)
        {
            Task.Run(() => {
                m_StateChanged?.Invoke(this, state, reason);
            });
        }

        /// <summary>
        /// Returns a new sequence number.
        /// </summary>
        protected uint GetNewSequenceNumber()
        {
            return Utils.IncrementIdentifier(ref m_sequenceNumber);
        }

        /// <summary>
        /// Resets the sequence number after a connect.
        /// </summary>
        protected void ResetSequenceNumber(uint sequenceNumber)
        {
            m_remoteSequenceNumber = sequenceNumber;
        }

        /// <summary>
        /// Checks if the sequence number is valid.
        /// </summary>
        protected bool VerifySequenceNumber(uint sequenceNumber, string context)
        {
            // everything ok if new number is greater.
            if (sequenceNumber > m_remoteSequenceNumber)
            {
                m_remoteSequenceNumber = sequenceNumber;
                return true;
            }

            // check for a valid rollover.
            if (m_remoteSequenceNumber > TcpMessageLimits.MinSequenceNumber && sequenceNumber < TcpMessageLimits.MaxRolloverSequenceNumber)
            {
                // only one rollover per token is allowed.
                if (!m_sequenceRollover)
                {
                    m_sequenceRollover = true;
                    m_remoteSequenceNumber = sequenceNumber;
                    return true;
                }
            }

            Utils.Trace((int)Utils.TraceMasks.Error, "{0}: Channel {1} - Duplicate sequence number: {2} <= {3}", context, this.ChannelId, sequenceNumber, m_remoteSequenceNumber);
            return false;
        }

        /// <summary>
        /// Saves an intermediate chunk for an incoming message.
        /// </summary>
        protected void SaveIntermediateChunk(uint requestId, ArraySegment<byte> chunk)
        {
            if (m_partialMessageChunks == null)
            {
                m_partialMessageChunks = new BufferCollection();
            }

            if (m_partialRequestId != requestId)
            {
                if (m_partialMessageChunks.Count > 0)
                {
                    Utils.Trace("WARNING - Discarding unprocessed message chunks for Request #{0}", m_partialRequestId);
                }

                m_partialMessageChunks.Release(BufferManager, "SaveIntermediateChunk");
            }

            if (requestId != 0)
            {
                m_partialRequestId = requestId;
                m_partialMessageChunks.Add(chunk);
            }
        }

        /// <summary>
        /// Returns the chunks saved for message.
        /// </summary>
        protected BufferCollection GetSavedChunks(uint requestId, ArraySegment<byte> chunk)
        {
            SaveIntermediateChunk(requestId, chunk);
            BufferCollection savedChunks = m_partialMessageChunks;
            m_partialMessageChunks = null;
            return savedChunks;
        }
        #endregion

        #region IMessageSink Members
        /// <summary>
        /// Processes an incoming message.
        /// </summary>
        public virtual void OnMessageReceived(IMessageSocket source, ArraySegment<byte> message)
        {
            lock (DataLock)
            {
                try
                {
                    uint messageType = BitConverter.ToUInt32(message.Array, message.Offset);

                    Utils.TraceDebug("{1} Message Received: {0} bytes", message.Count, messageType);

                    if (!HandleIncomingMessage(messageType, message))
                    {
                        BufferManager.ReturnBuffer(message.Array, "OnMessageReceived");
                    }
                }
                catch (Exception e)
                {
                    HandleMessageProcessingError(e, StatusCodes.BadTcpInternalError, "An error occurred receiving a message.");
                    BufferManager.ReturnBuffer(message.Array, "OnMessageReceived");
                }
            }
        }

        #region Incoming Message Support Functions
        /// <summary>
        /// Processes an incoming message.
        /// </summary>
        /// <returns>True if the implementor takes ownership of the buffer.</returns>
        protected virtual bool HandleIncomingMessage(uint messageType, ArraySegment<byte> messageChunk)
        {
            return false;
        }

        /// <summary>
        /// Handles an error parsing or verifying a message.
        /// </summary>
        protected void HandleMessageProcessingError(Exception e, uint defaultCode, string format, params object[] args)
        {
            HandleMessageProcessingError(ServiceResult.Create(e, defaultCode, format, args));
        }

        /// <summary>
        /// Handles an error parsing or verifying a message.
        /// </summary>
        protected void HandleMessageProcessingError(uint statusCode, string format, params object[] args)
        {
            HandleMessageProcessingError(ServiceResult.Create(statusCode, format, args));
        }

        /// <summary>
        /// Handles an error parsing or verifying a message.
        /// </summary>
        protected virtual void HandleMessageProcessingError(ServiceResult result)
        {
        }
        #endregion

        /// <summary>
        /// Handles a receive error.
        /// </summary>
        public virtual void OnReceiveError(IMessageSocket source, ServiceResult result)
        {
            lock (DataLock)
            {
                HandleSocketError(result);
            }
        }

        /// <summary>
        /// Handles a socket error.
        /// </summary>
        protected virtual void HandleSocketError(ServiceResult result)
        {
        }
        #endregion

        #region Outgoing Message Support Functions
        /// <summary>
        /// Handles a write complete event.
        /// </summary>
        protected virtual void OnWriteComplete(object sender, IMessageSocketAsyncEventArgs e)
        {
            lock (DataLock)
            {
                ServiceResult error = ServiceResult.Good;
                try
                {
                    Utils.TraceDebug("Bytes written: {0}", e.BytesTransferred);

                    if (e.BytesTransferred == 0)
                    {
                        error = ServiceResult.Create(StatusCodes.BadConnectionClosed, "The socket was closed by the remote application.");
                    }
                    if (e.Buffer != null)
                    {
                        BufferManager.ReturnBuffer(e.Buffer, "OnWriteComplete");
                    }
                    HandleWriteComplete((BufferCollection)e.BufferList, e.UserToken, e.BytesTransferred, error);
                }
                catch (Exception ex)
                {
                    if (ex is InvalidOperationException)
                    {
                        // suppress chained exception in HandleWriteComplete/ReturnBuffer
                        e.BufferList = null;
                    }
                    error = ServiceResult.Create(ex, StatusCodes.BadTcpInternalError, "Unexpected error during write operation.");
                    HandleWriteComplete((BufferCollection)e.BufferList, e.UserToken, e.BytesTransferred, error);
                }
            }

            e.Dispose();
        }

        /// <summary>
        /// Queues a write request.
        /// </summary>
        protected void BeginWriteMessage(ArraySegment<byte> buffer, object state)
        {
            ServiceResult error = ServiceResult.Good;
            IMessageSocketAsyncEventArgs args = m_socket.MessageSocketEventArgs();

            try
            {
                args.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);
                args.Completed += OnWriteComplete;
                args.UserToken = state;
                if (!m_socket.SendAsync(args))
                {
                    // I/O completed synchronously
                    if (args.IsSocketError || (args.BytesTransferred < buffer.Count))
                    {
                        error = ServiceResult.Create(StatusCodes.BadConnectionClosed, args.SocketErrorString);
                        HandleWriteComplete(null, state, args.BytesTransferred, error);
                        args.Dispose();
                    }
                    else
                    {
                        // success, call Complete
                        OnWriteComplete(null, args);
                    }
                }
            }
            catch (Exception ex)
            {
                error = ServiceResult.Create(ex, StatusCodes.BadTcpInternalError, "Unexpected error during write operation.");
                HandleWriteComplete(null, state, args.BytesTransferred, error);
                args.Dispose();
            }
        }

        /// <summary>
        /// Queues a write request.
        /// </summary>
        protected void BeginWriteMessage(BufferCollection buffers, object state)
        {
            ServiceResult error = ServiceResult.Good;
            IMessageSocketAsyncEventArgs args = m_socket.MessageSocketEventArgs();

            try
            {
                args.BufferList = buffers;
                args.Completed += OnWriteComplete;
                args.UserToken = state;
                if (m_socket == null ||
                    !m_socket.SendAsync(args))
                {
                    // I/O completed synchronously
                    if (args.IsSocketError || (args.BytesTransferred < buffers.TotalSize))
                    {
                        error = ServiceResult.Create(StatusCodes.BadConnectionClosed, args.SocketErrorString);
                        HandleWriteComplete(buffers, state, args.BytesTransferred, error);
                        args.Dispose();
                    }
                    else
                    {
                        OnWriteComplete(null, args);
                    }
                }
            }
            catch (Exception ex)
            {
                error = ServiceResult.Create(ex, StatusCodes.BadTcpInternalError, "Unexpected error during write operation.");
                HandleWriteComplete(buffers, state, args.BytesTransferred, error);
                args.Dispose();
            }
        }

        /// <summary>
        /// Called after a write operation completes.
        /// </summary>
        protected virtual void HandleWriteComplete(BufferCollection buffers, object state, int bytesWritten, ServiceResult result)
        {
            if (buffers != null)
            {
                buffers.Release(BufferManager, "WriteOperation");
            }
        }

        /// <summary>
        /// Writes an error to a stream.
        /// </summary>
        protected static void WriteErrorMessageBody(BinaryEncoder encoder, ServiceResult error)
        {
            string reason = (error.LocalizedText != null) ? error.LocalizedText.Text : null;

            // check that length is not exceeded.
            if (reason != null)
            {
                UTF8Encoding encoding = new UTF8Encoding();

                if (encoding.GetByteCount(reason) > TcpMessageLimits.MaxErrorReasonLength)
                {
                    reason = reason.Substring(0, TcpMessageLimits.MaxErrorReasonLength / encoding.GetMaxByteCount(1));
                }
            }

            encoder.WriteStatusCode(null, error.StatusCode);
            encoder.WriteString(null, reason);
        }

        /// <summary>
        /// Reads an error from a stream.
        /// </summary>
        protected static ServiceResult ReadErrorMessageBody(BinaryDecoder decoder)
        {
            // read the status code.
            uint statusCode = decoder.ReadUInt32(null);

            string reason = null;

            // ensure the reason does not exceed the limits in the protocol.
            int reasonLength = decoder.ReadInt32(null);

            if (reasonLength > 0 && reasonLength < TcpMessageLimits.MaxErrorReasonLength)
            {
                byte[] reasonBytes = new byte[reasonLength];

                for (int ii = 0; ii < reasonLength; ii++)
                {
                    reasonBytes[ii] = decoder.ReadByte(null);
                }

                reason = new UTF8Encoding().GetString(reasonBytes, 0, reasonLength);
            }

            return ServiceResult.Create(statusCode, "Error received from remote host: {0}", reason);
        }

        /// <summary>
        /// Checks if the message limits have been exceeded.
        /// </summary>
        protected bool MessageLimitsExceeded(bool isRequest, int messageSize, int chunkCount)
        {
            if (isRequest)
            {
                if (this.MaxRequestChunkCount > 0 && this.MaxRequestChunkCount <= chunkCount)
                {
                    return true;
                }

                if (this.MaxRequestMessageSize > 0 && this.MaxRequestMessageSize < messageSize)
                {
                    return true;
                }
            }
            else
            {
                if (this.MaxResponseChunkCount > 0 && this.MaxResponseChunkCount <= chunkCount)
                {
                    return true;
                }

                if (this.MaxResponseMessageSize > 0 && this.MaxResponseMessageSize < messageSize)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Updates the message type stored in the message header.
        /// </summary>
        protected static void UpdateMessageType(byte[] buffer, int offset, uint messageType)
        {
            buffer[offset++] = (byte)((messageType & 0x000000FF));
            buffer[offset++] = (byte)((messageType & 0x0000FF00) >> 8);
            buffer[offset++] = (byte)((messageType & 0x00FF0000) >> 16);
            buffer[offset] = (byte)((messageType & 0xFF000000) >> 24);
        }

        /// <summary>
        /// Updates the message size stored in the message header.
        /// </summary>
        protected static void UpdateMessageSize(byte[] buffer, int offset, int messageSize)
        {
            if (offset >= Int32.MaxValue - 4)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            
            offset += 4;

            buffer[offset++] = (byte)((messageSize & 0x000000FF));
            buffer[offset++] = (byte)((messageSize & 0x0000FF00) >> 8);
            buffer[offset++] = (byte)((messageSize & 0x00FF0000) >> 16);
            buffer[offset] = (byte)((messageSize & 0xFF000000) >> 24);
        }
        #endregion

        #region Protected Properties
        /// <summary>
        /// The synchronization object for the channel.
        /// </summary>
        protected object DataLock => m_lock;

        /// <summary>
        /// The socket for the channel.
        /// </summary>
        protected internal IMessageSocket Socket
        {
            get { return m_socket; }
            set
            {
                m_socket = value;
            }
        }

        /// <summary>
        /// The buffer manager for the channel.
        /// </summary>
        protected BufferManager BufferManager => m_bufferManager;

        /// <summary>
        /// The resource quotas for the channel.
        /// </summary>
        protected ChannelQuotas Quotas => m_quotas;

        /// <summary>
        /// The size of the receive buffer.
        /// </summary>
        protected int ReceiveBufferSize
        {
            get { return m_receiveBufferSize; }
            set { m_receiveBufferSize = value; }
        }

        /// <summary>
        /// The size of the send buffer.
        /// </summary>
        protected int SendBufferSize
        {
            get { return m_sendBufferSize; }
            set { m_sendBufferSize = value; }
        }

        /// <summary>
        /// The maximum size for a request message.
        /// </summary>
        protected int MaxRequestMessageSize
        {
            get { return m_maxRequestMessageSize; }
            set { m_maxRequestMessageSize = value; }
        }

        /// <summary>
        /// The maximum number of chunks per request message.
        /// </summary>
        protected int MaxRequestChunkCount
        {
            get { return m_maxRequestChunkCount; }
            set { m_maxRequestChunkCount = value; }
        }

        /// <summary>
        /// The maximum size for a response message.
        /// </summary>
        protected int MaxResponseMessageSize
        {
            get { return m_maxResponseMessageSize; }
            set { m_maxResponseMessageSize = value; }
        }

        /// <summary>
        /// The maximum number of chunks per response message.
        /// </summary>
        protected int MaxResponseChunkCount
        {
            get { return m_maxResponseChunkCount; }
            set { m_maxResponseChunkCount = value; }
        }

        /// <summary>
        /// The state of the channel.
        /// </summary>
        protected TcpChannelState State
        {
            get { return m_state; }

            set
            {
                if (m_state != value)
                {
                    Utils.Trace("Channel {0} in {1} state.", ChannelId, value);
                }

                m_state = value;
            }
        }

        /// <summary>
        /// The identifier assigned to the channel by the server.
        /// </summary>
        protected uint ChannelId
        {
            get
            {
                return m_channelId;
            }

            set
            {
                m_channelId = value;
                m_globalChannelId = Utils.Format("{0}-{1}", m_contextId, m_channelId);
            }
        }
        #endregion

        #region WriteOperation Class
        /// <summary>
        /// A class that stores the state for a write operation.
        /// </summary>
        protected class WriteOperation : ChannelAsyncOperation<int>
        {
            /// <summary>
            /// Initializes the object with a callback
            /// </summary>
            public WriteOperation(int timeout, AsyncCallback callback, object asyncState)
            :
                base(timeout, callback, asyncState)
            {
            }

            /// <summary>
            /// The request id associated with the operation.
            /// </summary>
            public uint RequestId
            {
                get { return m_requestId; }
                set { m_requestId = value; }
            }

            /// <summary>
            /// The body of the request or response associated with the operation.
            /// </summary>
            public IEncodeable MessageBody
            {
                get { return m_messageBody; }
                set { m_messageBody = value; }
            }

            #region Private Fields
            private uint m_requestId;
            private IEncodeable m_messageBody;
            #endregion
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private IMessageSocket m_socket;
        private BufferManager m_bufferManager;
        private ChannelQuotas m_quotas;
        private int m_receiveBufferSize;
        private int m_sendBufferSize;
        private int m_maxRequestMessageSize;
        private int m_maxResponseMessageSize;
        private int m_maxRequestChunkCount;
        private int m_maxResponseChunkCount;
        private string m_contextId;

        private TcpChannelState m_state;
        private uint m_channelId;
        private string m_globalChannelId;
        private long m_sequenceNumber;
        private uint m_remoteSequenceNumber;
        private bool m_sequenceRollover;
        private uint m_partialRequestId;
        private BufferCollection m_partialMessageChunks;

        private TcpChannelStateEventHandler m_StateChanged;
        #endregion
    }

    /// <summary>
    /// The possible channel states.
    /// </summary>
    public enum TcpChannelState
    {
        /// <summary>
        /// The channel is closed.
        /// </summary>
        Closed,

        /// <summary>
        /// The channel is closing.
        /// </summary>
        Closing,

        /// <summary>
        /// The channel establishing a network connection.
        /// </summary>
        Connecting,

        /// <summary>
        /// The channel negotiating security parameters.
        /// </summary>
        Opening,

        /// <summary>
        /// The channel is open and accepting messages.
        /// </summary>
        Open,

        /// <summary>
        /// The channel is in a error state.
        /// </summary>
        Faulted
    }

    /// <summary>
    /// Used to report changes to the channel state.
    /// </summary>
    public delegate void TcpChannelStateEventHandler(UaSCUaBinaryChannel channel, TcpChannelState state, ServiceResult error);
}
