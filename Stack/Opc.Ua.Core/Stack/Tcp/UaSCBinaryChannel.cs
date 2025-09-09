/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Manages the server side of a UA TCP channel.
    /// </summary>
    public partial class UaSCUaBinaryChannel : IMessageSink, IDisposable
    {
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
            string securityPolicyUri,
            ITelemetryContext telemetry)
            : this(
                contextId,
                bufferManager,
                quotas,
                null,
                serverCertificate,
                endpoints,
                securityMode,
                securityPolicyUri,
                telemetry)
        {
        }

        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public UaSCUaBinaryChannel(
            string contextId,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            CertificateTypesProvider serverCertificateTypesProvider,
            EndpointDescriptionCollection endpoints,
            MessageSecurityMode securityMode,
            string securityPolicyUri,
            ITelemetryContext telemetry)
            : this(
                contextId,
                bufferManager,
                quotas,
                serverCertificateTypesProvider,
                null,
                endpoints,
                securityMode,
                securityPolicyUri,
                telemetry)
        {
        }

        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        private UaSCUaBinaryChannel(
            string contextId,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            CertificateTypesProvider serverCertificateTypesProvider,
            X509Certificate2 serverCertificate,
            EndpointDescriptionCollection endpoints,
            MessageSecurityMode securityMode,
            string securityPolicyUri,
            ITelemetryContext telemetry)
        {
            // create a unique contex if none provided.
            m_contextId = contextId;
            Telemetry = telemetry;
            m_logger = telemetry.CreateLogger<UaSCUaBinaryChannel>();

            if (string.IsNullOrEmpty(m_contextId))
            {
                m_contextId = Guid.NewGuid().ToString();
            }

            // secuirty turned off if message security mode is set to none.
            if (securityMode == MessageSecurityMode.None)
            {
                securityPolicyUri = SecurityPolicies.None;
            }

            X509Certificate2Collection serverCertificateChain = null;
            if (serverCertificateTypesProvider != null && securityMode != MessageSecurityMode.None)
            {
                serverCertificate = serverCertificateTypesProvider.GetInstanceCertificate(
                    securityPolicyUri);

                if (serverCertificate == null)
                {
                    throw new ArgumentNullException(nameof(serverCertificate));
                }

                if (serverCertificate.RawData.Length > TcpMessageLimits.MaxCertificateSize)
                {
                    throw new ArgumentException(
                        Utils.Format(
                            "The DER encoded certificate may not be more than {0} bytes.",
                            TcpMessageLimits.MaxCertificateSize
                        ),
                        nameof(serverCertificate));
                }

                serverCertificateChain = serverCertificateTypesProvider
                    .LoadCertificateChainAsync(serverCertificate)
                    .GetAwaiter()
                    .GetResult();
            }

            if (Encoding.UTF8.GetByteCount(securityPolicyUri) > TcpMessageLimits
                .MaxSecurityPolicyUriSize)
            {
                throw new ArgumentException(
                    Utils.Format(
                        "UTF-8 form of the security policy URI may not be more than {0} bytes.",
                        TcpMessageLimits.MaxSecurityPolicyUriSize
                    ),
                    nameof(securityPolicyUri));
            }

            BufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
            Quotas = quotas ?? throw new ArgumentNullException(nameof(quotas));
            m_serverCertificateTypesProvider = serverCertificateTypesProvider;
            ServerCertificate = serverCertificate;
            ServerCertificateChain = serverCertificateChain;
            m_endpoints = endpoints;
            SecurityMode = securityMode;
            SecurityPolicyUri = securityPolicyUri;
            DiscoveryOnly = false;
            m_uninitialized = true;

            m_state = (int)TcpChannelState.Closed;
            ReceiveBufferSize = quotas.MaxBufferSize;
            SendBufferSize = quotas.MaxBufferSize;
            m_activeWriteRequests = 0;

            if (ReceiveBufferSize < TcpMessageLimits.MinBufferSize)
            {
                ReceiveBufferSize = TcpMessageLimits.MinBufferSize;
            }

            if (ReceiveBufferSize > TcpMessageLimits.MaxBufferSize)
            {
                ReceiveBufferSize = TcpMessageLimits.MaxBufferSize;
            }

            if (SendBufferSize < TcpMessageLimits.MinBufferSize)
            {
                SendBufferSize = TcpMessageLimits.MinBufferSize;
            }

            if (SendBufferSize > TcpMessageLimits.MaxBufferSize)
            {
                SendBufferSize = TcpMessageLimits.MaxBufferSize;
            }

            MaxRequestMessageSize = quotas.MaxMessageSize;
            MaxResponseMessageSize = quotas.MaxMessageSize;

            MaxRequestChunkCount = CalculateChunkCount(
                MaxRequestMessageSize,
                TcpMessageLimits.MinBufferSize);
            MaxResponseChunkCount = CalculateChunkCount(
                MaxResponseMessageSize,
                TcpMessageLimits.MinBufferSize);

            CalculateSymmetricKeySizes();
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
                DiscardTokens();
#if ECC_SUPPORT
                if (m_localNonce != null)
                {
                    m_localNonce.Dispose();
                    m_localNonce = null;
                }

                if (m_remoteNonce != null)
                {
                    m_remoteNonce.Dispose();
                    m_remoteNonce = null;
                }
#endif
            }
        }

        /// <summary>
        /// Telemetry context for the channel
        /// </summary>
        protected ITelemetryContext Telemetry { get; }

        /// <summary>
        /// The identifier assigned to the channel by the server.
        /// </summary>
        public uint Id { get; private set; }

        /// <summary>
        /// The globally unique identifier assigned to the channel by the server.
        /// </summary>
        public string GlobalChannelId { get; private set; }

        /// <summary>
        /// Raised when the state of the channel changes.
        /// </summary>
        public void SetStateChangedCallback(TcpChannelStateEventHandler callback)
        {
            lock (DataLock)
            {
                m_stateChanged = callback;
            }
        }

        /// <summary>
        /// The tickcount in milliseconds when the channel received/sent the last message.
        /// </summary>
        protected int LastActiveTickCount { get; private set; } = HiResClock.TickCount;

        /// <summary>
        /// Reports that the channel state has changed (in another thread).
        /// </summary>
        protected void ChannelStateChanged(TcpChannelState state, ServiceResult reason)
        {
            TcpChannelStateEventHandler stateChanged = m_stateChanged;
            if (stateChanged != null)
            {
                Task.Run(() => stateChanged?.Invoke(this, state, reason));
            }
        }

        /// <summary>
        /// Returns a new sequence number.
        /// </summary>
        protected uint GetNewSequenceNumber()
        {
            bool isLegacy = !EccUtils.IsEccPolicy(SecurityPolicyUri);

            long newSeqNumber = Interlocked.Increment(ref m_sequenceNumber);
            bool maxValueOverflow = isLegacy
                ? newSeqNumber > kMaxValueLegacyTrue
                : newSeqNumber > kMaxValueLegacyFalse;

            // LegacySequenceNumbers are TRUE for non ECC profiles
            // https://reference.opcfoundation.org/Core/Part6/v105/docs/6.7.2.4
            if (isLegacy)
            {
                if (maxValueOverflow)
                {
                    // First number after wrap around shall be less than 1024
                    // 1 for legaccy reasons
                    Interlocked.Exchange(ref m_sequenceNumber, 1);
                    return 1;
                }
                return (uint)newSeqNumber;
            }
            uint retVal = (uint)newSeqNumber - 1;
            if (maxValueOverflow)
            {
                // First number after wrap around and as initial value shall be 0
                Interlocked.Exchange(ref m_sequenceNumber, 0);
                Interlocked.Exchange(ref m_localSequenceNumber, 0);
                return retVal;
            }
            Interlocked.Exchange(ref m_localSequenceNumber, retVal);

            return retVal;
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
            // Accept the first sequence number depending on security policy
            if (m_firstReceivedSequenceNumber &&
                (
                    !EccUtils.IsEccPolicy(SecurityPolicyUri) ||
                    (EccUtils.IsEccPolicy(SecurityPolicyUri) && (sequenceNumber == 0))))
            {
                m_remoteSequenceNumber = sequenceNumber;
                m_firstReceivedSequenceNumber = false;
                return true;
            }

            // everything ok if new number is greater.
            if (sequenceNumber > m_remoteSequenceNumber)
            {
                m_remoteSequenceNumber = sequenceNumber;
                return true;
            }

            // check for a valid rollover.
            if (m_remoteSequenceNumber > TcpMessageLimits.MinSequenceNumber &&
                sequenceNumber < TcpMessageLimits.MaxRolloverSequenceNumber)
            {
                // only one rollover per token is allowed and with valid values depending on security policy
                if (!m_sequenceRollover &&
                    (
                        !EccUtils.IsEccPolicy(SecurityPolicyUri) ||
                        (EccUtils.IsEccPolicy(SecurityPolicyUri) && (sequenceNumber == 0))))
                {
                    m_sequenceRollover = true;
                    m_remoteSequenceNumber = sequenceNumber;
                    return true;
                }
            }

            m_logger.LogError(
                "ChannelId {ChannelId}: {Context} - Duplicate sequence number: {SequenceNumber} <= {RemoteSequenceNumber}",
                ChannelId,
                context,
                sequenceNumber,
                m_remoteSequenceNumber);
            return false;
        }

        /// <summary>
        /// Saves an intermediate chunk for an incoming message.
        /// </summary>
        protected bool SaveIntermediateChunk(
            uint requestId,
            ArraySegment<byte> chunk,
            bool isServerContext)
        {
            bool firstChunk = false;
            if (m_partialMessageChunks == null)
            {
                firstChunk = true;
                m_partialMessageChunks = [];
            }

            bool chunkOrSizeLimitsExceeded = MessageLimitsExceeded(
                isServerContext,
                m_partialMessageChunks.TotalSize,
                m_partialMessageChunks.Count);

            if ((m_partialRequestId != requestId) || chunkOrSizeLimitsExceeded)
            {
                if (m_partialMessageChunks.Count > 0)
                {
                    m_logger.LogWarning(
                        "WARNING - Discarding unprocessed message chunks for Request #{PartialRequestId}",
                        m_partialRequestId);
                }

                m_partialMessageChunks.Release(BufferManager, "SaveIntermediateChunk");
            }

            if (chunkOrSizeLimitsExceeded)
            {
                DoMessageLimitsExceeded();
                return firstChunk;
            }

            if (requestId != 0)
            {
                m_partialRequestId = requestId;
                m_partialMessageChunks.Add(chunk);
            }

            return firstChunk;
        }

        /// <summary>
        /// Returns the chunks saved for message.
        /// </summary>
        protected BufferCollection GetSavedChunks(
            uint requestId,
            ArraySegment<byte> chunk,
            bool isServerContext)
        {
            SaveIntermediateChunk(requestId, chunk, isServerContext);
            BufferCollection savedChunks = m_partialMessageChunks;
            m_partialMessageChunks = null;
            return savedChunks;
        }

        /// <summary>
        /// Returns total length of the chunks saved for message.
        /// </summary>
        protected int GetSavedChunksTotalSize()
        {
            return m_partialMessageChunks?.TotalSize ?? 0;
        }

        /// <summary>
        /// Code executed when the message limits are exceeded.
        /// </summary>
        protected virtual void DoMessageLimitsExceeded()
        {
            m_logger.LogError(
                "ChannelId {ChannelId}: - Message limits exceeded while building up message. Channel will be closed.",
                ChannelId);
        }

        /// <inheritdoc/>
        public virtual bool ChannelFull => m_activeWriteRequests > 100;

        /// <inheritdoc/>
        public virtual void OnMessageReceived(IMessageSocket source, ArraySegment<byte> message)
        {
            try
            {
                uint messageType = BitConverter.ToUInt32(message.Array, message.Offset);

                if (!HandleIncomingMessage(messageType, message))
                {
                    BufferManager.ReturnBuffer(message.Array, "OnMessageReceived");
                }
            }
            catch (Exception e)
            {
                HandleMessageProcessingError(
                    e,
                    StatusCodes.BadTcpInternalError,
                    "An error occurred receiving a message.");
                BufferManager.ReturnBuffer(message.Array, "OnMessageReceived");
            }
        }

        /// <summary>
        /// Processes an incoming message.
        /// </summary>
        /// <returns>True if the implementor takes ownership of the buffer.</returns>
        protected virtual bool HandleIncomingMessage(
            uint messageType,
            ArraySegment<byte> messageChunk)
        {
            return false;
        }

        /// <summary>
        /// Handles an error parsing or verifying a message.
        /// </summary>
        protected void HandleMessageProcessingError(
            Exception e,
            uint defaultCode,
            string format,
            params object[] args)
        {
            HandleMessageProcessingError(ServiceResult.Create(e, defaultCode, format, args));
        }

        /// <summary>
        /// Handles an error parsing or verifying a message.
        /// </summary>
        protected void HandleMessageProcessingError(
            uint statusCode,
            string format,
            params object[] args)
        {
            HandleMessageProcessingError(ServiceResult.Create(statusCode, format, args));
        }

        /// <summary>
        /// Handles an error parsing or verifying a message.
        /// </summary>
        protected virtual void HandleMessageProcessingError(ServiceResult result)
        {
        }

        /// <inheritdoc/>
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

        /// <summary>
        /// Handles a write complete event.
        /// </summary>
        protected virtual void OnWriteComplete(object sender, IMessageSocketAsyncEventArgs e)
        {
            ServiceResult error = ServiceResult.Good;
            try
            {
                if (e.BytesTransferred == 0)
                {
                    error = ServiceResult.Create(
                        StatusCodes.BadConnectionClosed,
                        "The socket was closed by the remote application.");
                }
                if (e.Buffer != null)
                {
                    BufferManager.ReturnBuffer(e.Buffer, "OnWriteComplete");
                }
                HandleWriteComplete(e.BufferList, e.UserToken, e.BytesTransferred, error);
            }
            catch (Exception ex)
            {
                if (ex is InvalidOperationException)
                {
                    // suppress chained exception in HandleWriteComplete/ReturnBuffer
                    e.BufferList = null;
                }
                error = ServiceResult.Create(
                    ex,
                    StatusCodes.BadTcpInternalError,
                    "Unexpected error during write operation.");
                HandleWriteComplete(e.BufferList, e.UserToken, e.BytesTransferred, error);
            }

            e.Dispose();
        }

        /// <summary>
        /// Queues a write request.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected void BeginWriteMessage(ArraySegment<byte> buffer, object state)
        {
            ServiceResult error = ServiceResult.Good;
            IMessageSocketAsyncEventArgs args =
                (Socket?.MessageSocketEventArgs())
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadConnectionClosed,
                    "The socket was closed by the remote application.");

            try
            {
                Interlocked.Increment(ref m_activeWriteRequests);
                args.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);
                args.Completed += OnWriteComplete;
                args.UserToken = state;
                if (!Socket.Send(args))
                {
                    // I/O completed synchronously
                    if (args.IsSocketError || (args.BytesTransferred < buffer.Count))
                    {
                        error = ServiceResult.Create(
                            StatusCodes.BadConnectionClosed,
                            args.SocketErrorString);
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
                error = ServiceResult.Create(
                    ex,
                    StatusCodes.BadTcpInternalError,
                    "Unexpected error during write operation.");
                if (args != null)
                {
                    HandleWriteComplete(null, state, args.BytesTransferred, error);
                    args.Dispose();
                }
            }
        }

        /// <summary>
        /// Queues a write request.
        /// </summary>
        protected void BeginWriteMessage(BufferCollection buffers, object state)
        {
            ServiceResult error = ServiceResult.Good;
            IMessageSocketAsyncEventArgs args = Socket.MessageSocketEventArgs();

            try
            {
                Interlocked.Increment(ref m_activeWriteRequests);
                args.BufferList = buffers;
                args.Completed += OnWriteComplete;
                args.UserToken = state;
                IMessageSocket socket = Socket;
                if (socket == null || !socket.Send(args))
                {
                    // I/O completed synchronously
                    if (args.IsSocketError || (args.BytesTransferred < buffers.TotalSize))
                    {
                        error = ServiceResult.Create(
                            StatusCodes.BadConnectionClosed,
                            args.SocketErrorString);
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
                error = ServiceResult.Create(
                    ex,
                    StatusCodes.BadTcpInternalError,
                    "Unexpected error during write operation.");
                HandleWriteComplete(buffers, state, args.BytesTransferred, error);
                args.Dispose();
            }
        }

        /// <summary>
        /// Called after a write operation completes.
        /// </summary>
        protected virtual void HandleWriteComplete(
            BufferCollection buffers,
            object state,
            int bytesWritten,
            ServiceResult result)
        {
            // Communication is active on the channel
            UpdateLastActiveTime();

            buffers?.Release(BufferManager, "WriteOperation");
            Interlocked.Decrement(ref m_activeWriteRequests);
        }

        /// <summary>
        /// Writes an error to a stream.
        /// </summary>
        protected static void WriteErrorMessageBody(BinaryEncoder encoder, ServiceResult error)
        {
            string reason = error.LocalizedText?.Text;

            // check that length is not exceeded.
            if (reason != null &&
                Encoding.UTF8.GetByteCount(reason) > TcpMessageLimits.MaxErrorReasonLength)
            {
                reason = reason[
                    ..(TcpMessageLimits.MaxErrorReasonLength / Encoding.UTF8.GetMaxByteCount(1))];
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

            if (reasonLength is > 0 and < TcpMessageLimits.MaxErrorReasonLength)
            {
                byte[] reasonBytes = new byte[reasonLength];

                for (int ii = 0; ii < reasonLength; ii++)
                {
                    reasonBytes[ii] = decoder.ReadByte(null);
                }

                reason = Encoding.UTF8.GetString(reasonBytes, 0, reasonLength);
            }

            reason ??= new ServiceResult(statusCode).ToString();

            return ServiceResult.Create(statusCode, "Error received from remote host: {0}", reason);
        }

        /// <summary>
        /// Checks if the message limits have been exceeded.
        /// </summary>
        protected bool MessageLimitsExceeded(bool isRequest, int messageSize, int chunkCount)
        {
            if (isRequest)
            {
                if (MaxRequestChunkCount > 0 && MaxRequestChunkCount < chunkCount)
                {
                    return true;
                }

                if (MaxRequestMessageSize > 0 && MaxRequestMessageSize < messageSize)
                {
                    return true;
                }
            }
            else
            {
                if (MaxResponseChunkCount > 0 && MaxResponseChunkCount < chunkCount)
                {
                    return true;
                }

                if (MaxResponseMessageSize > 0 && MaxResponseMessageSize < messageSize)
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
            buffer[offset++] = (byte)(messageType & 0x000000FF);
            buffer[offset++] = (byte)((messageType & 0x0000FF00) >> 8);
            buffer[offset++] = (byte)((messageType & 0x00FF0000) >> 16);
            buffer[offset] = (byte)((messageType & 0xFF000000) >> 24);
        }

        /// <summary>
        /// Updates the message size stored in the message header.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        protected static void UpdateMessageSize(byte[] buffer, int offset, int messageSize)
        {
            if (offset >= int.MaxValue - 4)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            offset += 4;

            buffer[offset++] = (byte)(messageSize & 0x000000FF);
            buffer[offset++] = (byte)((messageSize & 0x0000FF00) >> 8);
            buffer[offset++] = (byte)((messageSize & 0x00FF0000) >> 16);
            buffer[offset] = (byte)((messageSize & 0xFF000000) >> 24);
        }

        /// <summary>
        /// The synchronization object for the channel.
        /// </summary>
        protected object DataLock { get; } = new();

        /// <summary>
        /// The socket for the channel.
        /// </summary>
        protected internal IMessageSocket Socket { get; set; }

        /// <summary>
        /// Whether the client channel uses a reverse hello socket.
        /// </summary>
        protected internal bool ReverseSocket { get; set; }

        /// <summary>
        /// The buffer manager for the channel.
        /// </summary>
        protected BufferManager BufferManager { get; }

        /// <summary>
        /// The resource quotas for the channel.
        /// </summary>
        protected ChannelQuotas Quotas { get; }

        /// <summary>
        /// The size of the receive buffer.
        /// </summary>
        protected int ReceiveBufferSize { get; set; }

        /// <summary>
        /// The size of the send buffer.
        /// </summary>
        protected int SendBufferSize { get; set; }

        /// <summary>
        /// The maximum size for a request message.
        /// </summary>
        protected int MaxRequestMessageSize { get; set; }

        /// <summary>
        /// The maximum number of chunks per request message.
        /// </summary>
        protected int MaxRequestChunkCount { get; set; }

        /// <summary>
        /// The maximum size for a response message.
        /// </summary>
        protected int MaxResponseMessageSize { get; set; }

        /// <summary>
        /// The maximum number of chunks per response message.
        /// </summary>
        protected int MaxResponseChunkCount { get; set; }

        /// <summary>
        /// The state of the channel.
        /// </summary>
        protected TcpChannelState State
        {
            get => (TcpChannelState)m_state;
            set
            {
                if (Interlocked.Exchange(ref m_state, (int)value) != (int)value)
                {
                    m_logger.LogTrace("ChannelId {ChannelId}: in {State} state.", ChannelId, value);
                }
            }
        }

        /// <summary>
        /// The identifier assigned to the channel by the server.
        /// </summary>
        protected uint ChannelId
        {
            get => Id;
            set
            {
                Id = value;
                GlobalChannelId = Utils.Format("{0}-{1}", m_contextId, Id);
            }
        }

        /// <summary>
        /// A class that stores the state for a write operation.
        /// </summary>
        protected class WriteOperation : ChannelAsyncOperation<int>
        {
            /// <summary>
            /// Initializes the object with a callback
            /// </summary>
            public WriteOperation(int timeout, AsyncCallback callback, object asyncState)
                : base(timeout, callback, asyncState)
            {
            }

            /// <summary>
            /// The request id associated with the operation.
            /// </summary>
            public uint RequestId { get; set; }

            /// <summary>
            /// The body of the request or response associated with the operation.
            /// </summary>
            public IEncodeable MessageBody { get; set; }
        }

        /// <summary>
        /// Calculate the chunk count which can be used for messages based on buffer size.
        /// </summary>
        /// <param name="messageSize">The message size to be used.</param>
        /// <param name="bufferSize">The buffer available for a message.</param>
        /// <returns>The chunk count.</returns>
        protected static int CalculateChunkCount(int messageSize, int bufferSize)
        {
            if (bufferSize > 0)
            {
                int chunkCount = messageSize / bufferSize;
                if (chunkCount * bufferSize < messageSize)
                {
                    chunkCount++;
                }
                return chunkCount;
            }
            return 1;
        }

        /// <summary>
        /// Check the MessageType and size against the content and size of the stream.
        /// </summary>
        /// <param name="decoder">The decoder of the stream.</param>
        /// <param name="expectedMessageType">The message type to be checked.</param>
        /// <param name="count">The length of the message.</param>
        /// <exception cref="ServiceResultException"></exception>
        protected static void ReadAndVerifyMessageTypeAndSize(
            IDecoder decoder,
            uint expectedMessageType,
            int count)
        {
            uint messageType = decoder.ReadUInt32(null);
            if (messageType != expectedMessageType)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpMessageTypeInvalid,
                    "Expected message type {0:X8} instead of {0:X8}.",
                    expectedMessageType,
                    messageType);
            }
            int messageSize = decoder.ReadInt32(null);
            if (messageSize > count)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpMessageTooLarge,
                    "Messages size {0} is larger than buffer size {1}.",
                    messageSize,
                    count);
            }
        }

        /// <summary>
        /// Update the last time that communication has occured on the channel.
        /// </summary>
        public void UpdateLastActiveTime()
        {
            LastActiveTickCount = HiResClock.TickCount;
        }

        private int m_activeWriteRequests;
        private readonly string m_contextId;
        private readonly ILogger m_logger;
        /// <summary>
        /// treat TcpChannelState as int to use Interlocked
        /// </summary>
        private int m_state;
        private long m_sequenceNumber;
        private long m_localSequenceNumber;
        private uint m_remoteSequenceNumber;
        private bool m_sequenceRollover;
        private bool m_firstReceivedSequenceNumber = true;
        private uint m_partialRequestId;
        private BufferCollection m_partialMessageChunks;

        private TcpChannelStateEventHandler m_stateChanged;
        private const uint kMaxValueLegacyTrue = TcpMessageLimits.MinSequenceNumber;
        private const uint kMaxValueLegacyFalse = uint.MaxValue;
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
    public delegate void TcpChannelStateEventHandler(
        UaSCUaBinaryChannel channel,
        TcpChannelState state,
        ServiceResult error);
}
