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
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Opc.Ua.Utils;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Manages the server side of a UA TCP channel.
    /// </summary>
    public class TcpServerChannel : TcpListenerChannel
    {
        #region Constructors
        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public TcpServerChannel(
            string contextId,
            ITcpChannelListener listener,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            X509Certificate2 serverCertificate,
            EndpointDescriptionCollection endpoints)
        :
            this(contextId, listener, bufferManager, quotas, serverCertificate, null, endpoints)
        {
            m_queuedResponses = new SortedDictionary<uint, IServiceResponse>();
        }

        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public TcpServerChannel(
            string contextId,
            ITcpChannelListener listener,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            X509Certificate2 serverCertificate,
            X509Certificate2Collection serverCertificateChain,
            EndpointDescriptionCollection endpoints)
        :
            base(contextId, listener, bufferManager, quotas, serverCertificate, serverCertificateChain, endpoints)
        {
            m_queuedResponses = new SortedDictionary<uint, IServiceResponse>();
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_cleanupTimer")]
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// The channel name used in trace output.
        /// </summary>
        public override string ChannelName => "TCPSERVERCHANNEL";

        /// <summary>
        /// The URL used to establish a connection to the client.
        /// </summary>
        public Uri ReverseConnectionUrl { get; internal set; }

        /// <summary>
        /// Raised when the connection status changes.
        /// </summary>
        public event TcpChannelStatusEventHandler StatusChanged;

        private class ReverseConnectAsyncResult : AsyncResultBase
        {
            public ReverseConnectAsyncResult(AsyncCallback callback, object callbackData, int timeout) :
                base(callback, callbackData, timeout)
            {
            }
            public IMessageSocket Socket;
        }

        /// <summary>
        /// Begin a reverse connect.
        /// </summary>
        public IAsyncResult BeginReverseConnect(uint channelId, Uri endpointUrl, AsyncCallback callback, object callbackData, int timeout)
        {
            ChannelId = channelId;
            ReverseConnectionUrl = endpointUrl;
            SetEndpointUrl(Listener.EndpointUrl.ToString());

            var ar = new ReverseConnectAsyncResult(callback, callbackData, timeout);

            var tcpMessageSocketFactory = new TcpMessageSocketFactory();
            ar.Socket = Socket = tcpMessageSocketFactory.Create(this, BufferManager, ReceiveBufferSize);

            var connectComplete = new EventHandler<IMessageSocketAsyncEventArgs>(OnReverseConnectComplete);
            Task t = Task.Run(async () => await Socket.BeginConnect(endpointUrl, connectComplete, ar, ar.CancellationToken).ConfigureAwait(false));

            return ar;
        }

        /// <summary>
        /// End the reverse connect.
        /// </summary>
        public void EndReverseConnect(IAsyncResult result)
        {
            var ar = result as ReverseConnectAsyncResult;

            if (ar == null)
            {
                throw new ArgumentException("EndReverseConnect is called with invalid IAsyncResult.", nameof(result));
            }

            if (!ar.WaitForComplete())
            {
                throw new TimeoutException();
            }
        }

        /// <summary>
        /// Reverse client is connected, send reverse hello message.
        /// </summary>
        private void OnReverseConnectComplete(object sender, IMessageSocketAsyncEventArgs result)
        {
            var ar = (ReverseConnectAsyncResult)result.UserToken;

            if (ar == null || m_pendingReverseHello != null)
            {
                return;
            }

            if (result.IsSocketError)
            {
                ar.Exception = new ServiceResultException(StatusCodes.BadNotConnected, result.SocketErrorString);
                ar.OperationCompleted();
                return;
            }

            byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "OnReverseConnectConnectComplete");

            try
            {
                // start reading messages.
                ar.Socket.ReadNextMessage();

                // send reverse hello message.
                BinaryEncoder encoder = new BinaryEncoder(buffer, 0, SendBufferSize, Quotas.MessageContext);
                encoder.WriteUInt32(null, TcpMessageType.ReverseHello);
                encoder.WriteUInt32(null, 0);
                encoder.WriteString(null, EndpointDescription.Server.ApplicationUri);
                encoder.WriteString(null, EndpointDescription.EndpointUrl);
                int size = encoder.Close();
                UpdateMessageSize(buffer, 0, size);

                // set state to waiting for hello.
                State = TcpChannelState.Connecting;
                m_pendingReverseHello = ar;

                BeginWriteMessage(new ArraySegment<byte>(buffer, 0, size), null);
                buffer = null;
            }
            catch (Exception e)
            {
                ar.Exception = e;
                ar.OperationCompleted();
            }
            finally
            {
                if (buffer != null)
                {
                    BufferManager.ReturnBuffer(buffer, "OnReverseConnectComplete");
                }
            }
        }

        /// <summary>
        /// Handles a reconnect request.
        /// </summary>
        public override void Reconnect(
            IMessageSocket socket,
            uint requestId,
            uint sequenceNumber,
            X509Certificate2 clientCertificate,
            ChannelToken token,
            OpenSecureChannelRequest request)
        {
            if (socket == null) throw new ArgumentNullException(nameof(socket));

            lock (DataLock)
            {
                // make sure the same client certificate is being used.
                CompareCertificates(ClientCertificate, clientCertificate, false);

                // check for replay attacks.
                if (!VerifySequenceNumber(sequenceNumber, "Reconnect"))
                {
                    throw new ServiceResultException(StatusCodes.BadSequenceNumberInvalid);
                }

                try
                {
                    Utils.LogInfo("{0} SOCKET RECONNECTED: {1:X8}, ChannelId={2}", ChannelName, socket.Handle, ChannelId);

                    // replace the socket.
                    Socket = socket;
                    Socket.ChangeSink(this);

                    // need to assign a new token id.
                    token.TokenId = GetNewTokenId();

                    // put channel back in open state.
                    ActivateToken(token);
                    State = TcpChannelState.Open;

                    // no need to cleanup.
                    CleanupTimer();

                    // send response.
                    SendOpenSecureChannelResponse(requestId, token, request);

                    // send any queued responses.
                    ResetQueuedResponses(OnChannelReconnected);
                }
                catch (Exception e)
                {
                    SendServiceFault(token, requestId, ServiceResult.Create(e, StatusCodes.BadTcpInternalError, "Unexpected error processing request."));
                }
            }
        }
        #endregion

        #region Socket Event Handlers
        /// <summary>
        /// Processes an incoming message.
        /// </summary>
        /// <returns>True if the implementor takes ownership of the buffer.</returns>
        protected override bool HandleIncomingMessage(uint messageType, ArraySegment<byte> messageChunk)
        {
            lock (DataLock)
            {
                SetResponseRequired(true);

                try
                {
                    // process a response.
                    if (TcpMessageType.IsType(messageType, TcpMessageType.Message))
                    {
                        Utils.LogTrace(TraceMasks.ServiceDetail, "ChannelId {0}: ProcessRequestMessage", ChannelId);
                        return ProcessRequestMessage(messageType, messageChunk);
                    }

                    // check for hello.
                    if (messageType == TcpMessageType.Hello)
                    {
                        Utils.LogTrace(TraceMasks.ServiceDetail, "ChannelId {0}: ProcessHelloMessage", ChannelId);
                        return ProcessHelloMessage(messageChunk);
                    }

                    // process open secure channel repsonse.
                    if (TcpMessageType.IsType(messageType, TcpMessageType.Open))
                    {
                        Utils.LogTrace(TraceMasks.ServiceDetail, "ChannelId {0}: ProcessOpenSecureChannelRequest", ChannelId);
                        return ProcessOpenSecureChannelRequest(messageType, messageChunk);
                    }

                    // process close secure channel response.
                    if (TcpMessageType.IsType(messageType, TcpMessageType.Close))
                    {
                        Utils.LogTrace(TraceMasks.ServiceDetail, "ChannelId {0}: ProcessCloseSecureChannelRequest", ChannelId);
                        return ProcessCloseSecureChannelRequest(messageType, messageChunk);
                    }

                    // invalid message type - must close socket and reconnect.
                    ForceChannelFault(
                        StatusCodes.BadTcpMessageTypeInvalid,
                        "The server does not recognize the message type: {0:X8}.",
                        messageType);

                    return false;
                }
                finally
                {
                    SetResponseRequired(false);
                }
            }
        }
        #endregion

        #region Error Handling Functions
        /// <summary>
        /// Called to send queued responses after a reconnect.
        /// </summary>
        private void OnChannelReconnected(object state)
        {
            SortedDictionary<uint, IServiceResponse> responses = state as SortedDictionary<uint, IServiceResponse>;

            if (responses == null)
            {
                return;
            }

            foreach (KeyValuePair<uint, IServiceResponse> response in responses)
            {
                try
                {
                    SendResponse(response.Key, response.Value);
                }
                catch (Exception e)
                {
                    Utils.LogError(e, "Unexpected error re-sending request (ID={0}).", response.Key);
                }
            }
        }
        #endregion

        #region Connect/Reconnect Sequence
        /// <summary>
        /// Processes a Hello message from the client.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "protocolVersion")]
        private bool ProcessHelloMessage(ArraySegment<byte> messageChunk)
        {
            // validate the channel state.
            if (State != TcpChannelState.Connecting)
            {
                ForceChannelFault(StatusCodes.BadTcpMessageTypeInvalid, "Client sent an unexpected Hello message.");
                return false;
            }

            try
            {
                MemoryStream istrm = new MemoryStream(messageChunk.Array, messageChunk.Offset, messageChunk.Count, false);
                BinaryDecoder decoder = new BinaryDecoder(istrm, Quotas.MessageContext);
                istrm.Seek(TcpMessageLimits.MessageTypeAndSize, SeekOrigin.Current);

                // read requested buffer sizes.
                uint protocolVersion = decoder.ReadUInt32(null);
                uint receiveBufferSize = decoder.ReadUInt32(null);
                uint sendBufferSize = decoder.ReadUInt32(null);
                uint maxMessageSize = decoder.ReadUInt32(null);
                uint maxChunkCount = decoder.ReadUInt32(null);

                // read the endpoint url.
                int length = decoder.ReadInt32(null);

                if (length > 0)
                {
                    if (length > TcpMessageLimits.MaxEndpointUrlLength)
                    {
                        ForceChannelFault(StatusCodes.BadTcpEndpointUrlInvalid);
                        return false;
                    }

                    byte[] endpointUrl = new byte[length];

                    for (int ii = 0; ii < endpointUrl.Length; ii++)
                    {
                        endpointUrl[ii] = decoder.ReadByte(null);
                    }

                    if (!SetEndpointUrl(new UTF8Encoding().GetString(endpointUrl, 0, endpointUrl.Length)))
                    {
                        ForceChannelFault(StatusCodes.BadTcpEndpointUrlInvalid);
                        return false;
                    }
                }

                decoder.Close();

                // update receive buffer size.
                if (receiveBufferSize < ReceiveBufferSize)
                {
                    ReceiveBufferSize = (int)receiveBufferSize;
                }

                if (ReceiveBufferSize < TcpMessageLimits.MinBufferSize)
                {
                    ReceiveBufferSize = TcpMessageLimits.MinBufferSize;
                }

                // update send buffer size.
                if (sendBufferSize < SendBufferSize)
                {
                    SendBufferSize = (int)sendBufferSize;
                }

                if (SendBufferSize < TcpMessageLimits.MinBufferSize)
                {
                    SendBufferSize = TcpMessageLimits.MinBufferSize;
                }

                // update the max message size.
                if (maxMessageSize > 0 && maxMessageSize < MaxResponseMessageSize)
                {
                    MaxResponseMessageSize = (int)maxMessageSize;
                }

                if (MaxResponseMessageSize < SendBufferSize)
                {
                    MaxResponseMessageSize = SendBufferSize;
                }

                // update the max chunk count.
                MaxResponseChunkCount = CalculateChunkCount(MaxResponseMessageSize, SendBufferSize);

                if (maxChunkCount > 0 && maxChunkCount < MaxResponseChunkCount)
                {
                    MaxResponseChunkCount = (int)maxChunkCount;
                }

                MaxRequestChunkCount = CalculateChunkCount(MaxRequestMessageSize, ReceiveBufferSize);

                // send acknowledge.
                byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "ProcessHelloMessage");

                try
                {
                    using (MemoryStream ostrm = new MemoryStream(buffer, 0, SendBufferSize))
                    using (BinaryEncoder encoder = new BinaryEncoder(ostrm, Quotas.MessageContext))
                    {
                        encoder.WriteUInt32(null, TcpMessageType.Acknowledge);
                        encoder.WriteUInt32(null, 0);
                        encoder.WriteUInt32(null, 0); // ProtocolVersion
                        encoder.WriteUInt32(null, (uint)ReceiveBufferSize);
                        encoder.WriteUInt32(null, (uint)SendBufferSize);
                        encoder.WriteUInt32(null, (uint)MaxRequestMessageSize);
                        encoder.WriteUInt32(null, (uint)MaxRequestChunkCount);

                        int size = encoder.Close();
                        UpdateMessageSize(buffer, 0, size);

                        // now ready for the open or bind request.
                        State = TcpChannelState.Opening;

                        BeginWriteMessage(new ArraySegment<byte>(buffer, 0, size), null);
                    }
                    buffer = null;
                }
                finally
                {
                    if (buffer != null)
                    {
                        BufferManager.ReturnBuffer(buffer, "ProcessHelloMessage");
                    }
                }
            }
            catch (Exception e)
            {
                ForceChannelFault(e, StatusCodes.BadTcpInternalError, "Unexpected error while processing a Hello message.");
            }

            return false;
        }

        /// <summary>
        /// Processes an OpenSecureChannel request message.
        /// </summary>
        private bool ProcessOpenSecureChannelRequest(uint messageType, ArraySegment<byte> messageChunk)
        {
            // validate the channel state.
            if (State != TcpChannelState.Opening && State != TcpChannelState.Open)
            {
                ForceChannelFault(StatusCodes.BadTcpMessageTypeInvalid, "Client sent an unexpected OpenSecureChannel message.");
                return false;
            }

            // parse the security header.
            uint channelId = 0;
            X509Certificate2 clientCertificate = null;
            uint requestId = 0;
            uint sequenceNumber = 0;

            ArraySegment<byte> messageBody;

            try
            {
                messageBody = ReadAsymmetricMessage(
                    messageChunk,
                    ServerCertificate,
                    out channelId,
                    out clientCertificate,
                    out requestId,
                    out sequenceNumber);

                // check for replay attacks.
                if (!VerifySequenceNumber(sequenceNumber, "ProcessOpenSecureChannelRequest"))
                {
                    throw new ServiceResultException(StatusCodes.BadSequenceNumberInvalid);
                }
            }
            catch (Exception e)
            {
                const string errorSecurityChecksFailed = "Could not verify security on OpenSecureChannel request.";
                ServiceResultException innerException = e.InnerException as ServiceResultException;

                // report the audit event for open secure channel
                ReportAuditOpenSecureChannelEvent?.Invoke(this, null, clientCertificate, e);

                // report the audit event for open certificate error
                ReportAuditCertificateEvent?.Invoke(clientCertificate, e);

                // If the certificate structure, signature and trust list checks pass,
                // return the other specific validation errors instead of BadSecurityChecksFailed
                if (innerException != null)
                {
                    if (innerException.StatusCode == StatusCodes.BadCertificateUntrusted ||
                        innerException.StatusCode == StatusCodes.BadCertificateChainIncomplete ||
                        innerException.StatusCode == StatusCodes.BadCertificateRevoked ||
                        innerException.StatusCode == StatusCodes.BadCertificateInvalid ||
                        innerException.StatusCode == StatusCodes.BadCertificatePolicyCheckFailed ||
                        (innerException.InnerResult != null && innerException.InnerResult.StatusCode == StatusCodes.BadCertificateUntrusted))
                    {
                        ForceChannelFault(StatusCodes.BadSecurityChecksFailed, errorSecurityChecksFailed);
                        return false;
                    }
                    else if (innerException.StatusCode == StatusCodes.BadCertificateTimeInvalid ||
                        innerException.StatusCode == StatusCodes.BadCertificateIssuerTimeInvalid ||
                        innerException.StatusCode == StatusCodes.BadCertificateHostNameInvalid ||
                        innerException.StatusCode == StatusCodes.BadCertificateUriInvalid ||
                        innerException.StatusCode == StatusCodes.BadCertificateUseNotAllowed ||
                        innerException.StatusCode == StatusCodes.BadCertificateIssuerUseNotAllowed ||
                        innerException.StatusCode == StatusCodes.BadCertificateRevocationUnknown ||
                        innerException.StatusCode == StatusCodes.BadCertificateIssuerRevocationUnknown ||
                        innerException.StatusCode == StatusCodes.BadCertificateIssuerRevoked)
                    {
                        ForceChannelFault(innerException, innerException.StatusCode, e.Message);
                        return false;
                    }
                }

                ForceChannelFault(StatusCodes.BadSecurityChecksFailed, errorSecurityChecksFailed);
                return false;
            }

            BufferCollection chunksToProcess = null;
            OpenSecureChannelRequest request = null;
            try
            {
                bool firstCall = ClientCertificate == null;

                // must ensure the same certificate was used.
                if (ClientCertificate != null)
                {
                    CompareCertificates(ClientCertificate, clientCertificate, false);
                }
                else
                {
                    ClientCertificate = clientCertificate;
                }

                // check if it is necessary to wait for more chunks.
                if (!TcpMessageType.IsFinal(messageType))
                {
                    SaveIntermediateChunk(requestId, messageBody, true);
                    return false;
                }
                // get the chunks to process.
                chunksToProcess = GetSavedChunks(requestId, messageBody, true);

                request = (OpenSecureChannelRequest)BinaryDecoder.DecodeMessage(
                    new ArraySegmentStream(chunksToProcess),
                    typeof(OpenSecureChannelRequest),
                    Quotas.MessageContext);

                if (request == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadStructureMissing, "Could not parse OpenSecureChannel request body.");
                }

                // check the security mode.
                if (request.SecurityMode != SecurityMode)
                {
                    ReviseSecurityMode(firstCall, request.SecurityMode);
                }

                // create a new token.
                ChannelToken token = CreateToken();

                token.TokenId = GetNewTokenId();
                token.ServerNonce = CreateNonce();
                // check the client nonce.
                token.ClientNonce = request.ClientNonce;

                if (!ValidateNonce(token.ClientNonce))
                {
                    throw ServiceResultException.Create(StatusCodes.BadNonceInvalid, "Client nonce is not the correct length or not random enough.");
                }

                // choose the lifetime.
                int lifetime = (int)request.RequestedLifetime;

                if (lifetime < TcpMessageLimits.MinSecurityTokenLifeTime)
                {
                    lifetime = TcpMessageLimits.MinSecurityTokenLifeTime;
                }

                if (lifetime > 0 && lifetime < token.Lifetime)
                {
                    token.Lifetime = lifetime;
                }

                // check the request type.
                SecurityTokenRequestType requestType = request.RequestType;

                if (requestType == SecurityTokenRequestType.Issue && State != TcpChannelState.Opening)
                {
                    throw ServiceResultException.Create(StatusCodes.BadRequestTypeInvalid, "Cannot request a new token for an open channel.");
                }

                if (requestType == SecurityTokenRequestType.Renew && State != TcpChannelState.Open)
                {
                    // may be reconnecting to a dropped channel.
                    if (State == TcpChannelState.Opening)
                    {
                        // tell the listener to find the channel that can process the request.
                        Listener.ReconnectToExistingChannel(
                            Socket,
                            requestId,
                            sequenceNumber,
                            channelId,
                            ClientCertificate,
                            token,
                            request);

                        Utils.LogInfo(
                            "{0} ReconnectToExistingChannel Socket={1:X8}, ChannelId={2}, TokenId={3}",
                            ChannelName,
                            (Socket != null) ? Socket.Handle : 0,
                            (CurrentToken != null) ? CurrentToken.ChannelId : 0,
                            (CurrentToken != null) ? CurrentToken.TokenId : 0);

                        // close the channel.
                        ChannelClosed();

                        // nothing more to do.
                        return false;
                    }

                    throw ServiceResultException.Create(StatusCodes.BadRequestTypeInvalid, "Cannot request to renew a token for a channel that has not been opened.");
                }

                // check the channel id.
                if (requestType == SecurityTokenRequestType.Renew && channelId != ChannelId)
                {
                    throw ServiceResultException.Create(StatusCodes.BadTcpSecureChannelUnknown, "Do not recognize the secure channel id provided.");
                }

                // log security information.
                if (requestType == SecurityTokenRequestType.Issue)
                {
                    Opc.Ua.Security.Audit.SecureChannelCreated(
                        m_ImplementationString,
                        Listener.EndpointUrl.ToString(),
                        Utils.Format("{0}", ChannelId),
                        EndpointDescription,
                        ClientCertificate,
                        ServerCertificate,
                        BinaryEncodingSupport.Required);
                }
                else
                {
                    Opc.Ua.Security.Audit.SecureChannelRenewed(
                        m_ImplementationString,
                        Utils.Format("{0}", ChannelId));
                }

                if (requestType == SecurityTokenRequestType.Renew)
                {
                    SetRenewedToken(token);
                }
                else
                {
                    ActivateToken(token);
                }

                State = TcpChannelState.Open;

                // send the response.
                SendOpenSecureChannelResponse(requestId, token, request);

                // notify reverse 
                CompleteReverseHello(null);

                // notify any monitors.
                NotifyMonitors(ServiceResult.Good, false);

                if (requestType == SecurityTokenRequestType.Issue)
                {
                    // always report the audit event for open secure channel with RequestType = Issue
                    ReportAuditOpenSecureChannelEvent?.Invoke(this, request, ClientCertificate, null);
                }

                return false;
            }
            catch (Exception e)
            {
                // report the audit event for open secure channel
                ReportAuditOpenSecureChannelEvent?.Invoke(this, request, ClientCertificate, e);

                SendServiceFault(requestId, ServiceResult.Create(e, StatusCodes.BadTcpInternalError, "Unexpected error processing OpenSecureChannel request."));
                CompleteReverseHello(e);
                return false;
            }
            finally
            {
                if (chunksToProcess != null)
                {
                    chunksToProcess.Release(BufferManager, "ProcessOpenSecureChannelRequest");
                }
            }
        }

        /// <inheritdoc/>
        protected override void NotifyMonitors(ServiceResult status, bool closed)
        {
            try
            {
                StatusChanged?.Invoke(this, status, closed);
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Error raising StatusChanged event.");
            }
        }

        /// <inheritdoc/>
        protected override void CompleteReverseHello(Exception e)
        {
            var ar = m_pendingReverseHello;
            if (ar != null && ar == Interlocked.CompareExchange(ref m_pendingReverseHello, null, ar))
            {
                ar.Exception = e;
                ar.OperationCompleted();
            }
        }

        /// <summary>
        /// Sends an OpenSecureChannel response.
        /// </summary>
        private void SendOpenSecureChannelResponse(uint requestId, ChannelToken token, OpenSecureChannelRequest request)
        {
            Utils.LogTrace("ChannelId {0}: SendOpenSecureChannelResponse()", ChannelId);

            OpenSecureChannelResponse response = new OpenSecureChannelResponse();

            response.ResponseHeader.RequestHandle = request.RequestHeader.RequestHandle;
            response.ResponseHeader.Timestamp = DateTime.UtcNow;

            response.SecurityToken.ChannelId = token.ChannelId;
            response.SecurityToken.TokenId = token.TokenId;
            response.SecurityToken.CreatedAt = token.CreatedAt;
            response.SecurityToken.RevisedLifetime = (uint)token.Lifetime;
            response.ServerNonce = token.ServerNonce;

            byte[] buffer = BinaryEncoder.EncodeMessage(response, Quotas.MessageContext);

            BufferCollection chunksToSend = WriteAsymmetricMessage(
                TcpMessageType.Open,
                requestId,
                ServerCertificate,
                ServerCertificateChain,
                ClientCertificate,
                new ArraySegment<byte>(buffer, 0, buffer.Length));

            // write the message to the server.
            try
            {
                BeginWriteMessage(chunksToSend, null);
                chunksToSend = null;
            }
            finally
            {
                if (chunksToSend != null)
                {
                    chunksToSend.Release(BufferManager, "SendOpenSecureChannelResponse");
                }
            }
        }

        /// <summary>
        /// Processes an CloseSecureChannel request message.
        /// </summary>
        private bool ProcessCloseSecureChannelRequest(uint messageType, ArraySegment<byte> messageChunk)
        {
            // validate security on the message.
            ChannelToken token = null;
            uint requestId = 0;
            uint sequenceNumber = 0;

            ArraySegment<byte> messageBody;

            try
            {
                messageBody = ReadSymmetricMessage(messageChunk, true, out token, out requestId, out sequenceNumber);

                // check for replay attacks.
                if (!VerifySequenceNumber(sequenceNumber, "ProcessCloseSecureChannelRequest"))
                {
                    throw new ServiceResultException(StatusCodes.BadSequenceNumberInvalid, "Could not verify security on CloseSecureChannel request.");
                }
            }
            catch (Exception e)
            {
                // report the audit event for close secure channel
                ReportAuditCloseSecureChannelEvent?.Invoke(this, e);

                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, e, "Could not verify security on CloseSecureChannel request.");
            }

            BufferCollection chunksToProcess = null;

            try
            {
                // check if it is necessary to wait for more chunks.
                if (!TcpMessageType.IsFinal(messageType))
                {
                    SaveIntermediateChunk(requestId, messageBody, true);
                    return false;
                }

                // get the chunks to process.
                chunksToProcess = GetSavedChunks(requestId, messageBody, true);

                CloseSecureChannelRequest request = BinaryDecoder.DecodeMessage(
                    new ArraySegmentStream(chunksToProcess),
                    typeof(CloseSecureChannelRequest),
                    Quotas.MessageContext) as CloseSecureChannelRequest;

                if (request == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadStructureMissing, "Could not parse CloseSecureChannel request body.");
                }

                // send the response.
                // SendCloseSecureChannelResponse(requestId, token, request);

                // report the audit event for close secure channel
                ReportAuditCloseSecureChannelEvent?.Invoke(this, null);
            }
            catch (Exception e)
            {
                // report the audit event for close secure channel
                ReportAuditCloseSecureChannelEvent?.Invoke(this, e);

                Utils.LogError(e, "Unexpected error processing CloseSecureChannel request.");
            }
            finally
            {
                if (chunksToProcess != null)
                {
                    chunksToProcess.Release(BufferManager, "ProcessCloseSecureChannelRequest");
                }

                Utils.LogInfo(
                    "{0} ProcessCloseSecureChannelRequest success, ChannelId={1}, TokenId={2}, Socket={3:X8}",
                    ChannelName, CurrentToken?.ChannelId, CurrentToken?.TokenId, Socket?.Handle);

                // close the channel.
                ChannelClosed();
            }

            // return false would double free the buffer
            return true;
        }
        #endregion

        #region Message Processing
        /// <summary>
        /// Processes a request message.
        /// </summary>
        private bool ProcessRequestMessage(uint messageType, ArraySegment<byte> messageChunk)
        {
            // validate the channel state.
            if (State != TcpChannelState.Open)
            {
                ForceChannelFault(StatusCodes.BadTcpMessageTypeInvalid, "Client sent an unexpected request message.");
                return false;
            }

            // validate security on the message.
            ChannelToken token = null;
            uint requestId = 0;
            uint sequenceNumber = 0;

            ArraySegment<byte> messageBody;

            try
            {
                messageBody = ReadSymmetricMessage(messageChunk, true, out token, out requestId, out sequenceNumber);

                // check for replay attacks.
                if (!VerifySequenceNumber(sequenceNumber, "ProcessRequestMessage"))
                {
                    throw new ServiceResultException(StatusCodes.BadSequenceNumberInvalid);
                }

                if (token == CurrentToken && PreviousToken != null && !PreviousToken.Expired)
                {
                    Utils.LogInfo("ChannelId {0}: Server Current Token #{1}, Revoked Token #{2}.",
                        PreviousToken.ChannelId, CurrentToken.TokenId, PreviousToken.TokenId);
                    PreviousToken.Lifetime = 0;
                }
            }
            catch (Exception e)
            {
                ForceChannelFault(e, StatusCodes.BadSecurityChecksFailed, "Could not verify security on incoming request.");
                return false;
            }

            const int ChannelCloseCount = 5;
            int countForDisconnect = ChannelCloseCount;
            while (ChannelFull && countForDisconnect > 0)
            {
                Utils.LogInfo("Channel {0}: full -- delay processing.", Id);

                // delay reading from channel
                Thread.Sleep(1000);

                if (--countForDisconnect == 0 && ChannelFull)
                {
                    Utils.LogWarning("Channel {0}: break socket connection.", Id);
                    ChannelClosed();
                    return false;
                }
            }

            BufferCollection chunksToProcess = null;

            try
            {
                // check for an abort.
                if (TcpMessageType.IsAbort(messageType))
                {
                    Utils.LogWarning(TraceMasks.ServiceDetail, "ChannelId {0}: ProcessRequestMessage RequestId {1} was aborted.", ChannelId, requestId);
                    chunksToProcess = GetSavedChunks(requestId, messageBody, true);
                    return true;
                }

                // check if it is necessary to wait for more chunks.
                if (!TcpMessageType.IsFinal(messageType))
                {
                    bool firstChunk = SaveIntermediateChunk(requestId, messageBody, true);

                    // validate the type is allowed with a discovery channel
                    if (DiscoveryOnly)
                    {
                        if (firstChunk)
                        {
                            if (!ValidateDiscoveryServiceCall(token, requestId, messageBody, out chunksToProcess))
                            {
                                ChannelClosed();
                            }
                        }
                        else if (GetSavedChunksTotalSize() > TcpMessageLimits.DefaultDiscoveryMaxMessageSize)
                        {
                            chunksToProcess = GetSavedChunks(0, messageBody, true);
                            SendServiceFault(token, requestId, ServiceResult.Create(StatusCodes.BadSecurityPolicyRejected, "Discovery Channel message size exceeded."));
                            ChannelClosed();
                        }
                    }

                    return true;
                }

                // Utils.LogTrace("ChannelId {0}: ProcessRequestMessage RequestId {1}", ChannelId, requestId);
                if (DiscoveryOnly && GetSavedChunksTotalSize() == 0)
                {
                    if (!ValidateDiscoveryServiceCall(token, requestId, messageBody, out chunksToProcess))
                    {
                        return true;
                    }
                }

                // get the chunks to process.
                chunksToProcess = GetSavedChunks(requestId, messageBody, true);

                // decode the request.
                IServiceRequest request = BinaryDecoder.DecodeMessage(new ArraySegmentStream(chunksToProcess), null, Quotas.MessageContext) as IServiceRequest;

                if (request == null)
                {
                    SendServiceFault(token, requestId, ServiceResult.Create(StatusCodes.BadStructureMissing, "Could not parse request body."));
                    return true;
                }

                // ensure that only discovery requests come through unsecured.
                if (DiscoveryOnly)
                {
                    if (!(request is GetEndpointsRequest || request is FindServersRequest || request is FindServersOnNetworkRequest))
                    {
                        SendServiceFault(token, requestId, ServiceResult.Create(StatusCodes.BadSecurityPolicyRejected, "Channel can only be used for discovery."));
                        return true;
                    }
                }

                // hand the request to the server.
                RequestReceived?.Invoke(this, requestId, request);

                return true;
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Unexpected error processing request.");
                SendServiceFault(token, requestId, ServiceResult.Create(e, StatusCodes.BadTcpInternalError, "Unexpected error processing request."));
                return true;
            }
            finally
            {
                if (chunksToProcess != null)
                {
                    chunksToProcess.Release(BufferManager, "ProcessRequestMessage");
                }
            }
        }

        /// <summary>
        /// Sends the response for the specified request.
        /// </summary>
        public void SendResponse(uint requestId, IServiceResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

            lock (DataLock)
            {
                // must queue the response if the channel is in the faulted state.
                if (State == TcpChannelState.Faulted)
                {
                    m_queuedResponses[requestId] = response;
                    return;
                }

                Utils.EventLog.SendResponse((int)ChannelId, (int)requestId);

                BufferCollection buffers = null;

                try
                {
                    // note that the server does nothing if the message limits are exceeded.
                    bool limitsExceeded = false;

                    buffers = WriteSymmetricMessage(
                        TcpMessageType.Message,
                        requestId,
                        CurrentToken,
                        response,
                        false,
                        out limitsExceeded);

                }
                catch (Exception e)
                {
                    SendServiceFault(
                        CurrentToken,
                        requestId,
                        ServiceResult.Create(e, StatusCodes.BadEncodingError, "Could not encode outgoing message."));

                    return;
                }

                try
                {
                    BeginWriteMessage(buffers, null);
                    buffers = null;
                }
                catch (Exception)
                {
                    if (buffers != null)
                    {
                        buffers.Release(BufferManager, "SendResponse");
                    }

                    m_queuedResponses[requestId] = response;
                    return;
                }
            }
        }

        /// <summary>
        /// Reset the sorted dictionary of queued responses after reconnect.
        /// </summary>
        private void ResetQueuedResponses(Action<object> action)
        {
            Task.Factory.StartNew(action, m_queuedResponses);
            m_queuedResponses = new SortedDictionary<uint, IServiceResponse>();
        }

        /// <summary>
        /// Closes the channel in case the message limits have been exceeded
        /// </summary>
        protected override void DoMessageLimitsExceeded()
        {
            base.DoMessageLimitsExceeded();
            ChannelClosed();
        }

        /// <summary>
        /// Validate the type of message before it is decoded.
        /// </summary>
        private bool ValidateDiscoveryServiceCall(ChannelToken token, uint requestId, ArraySegment<byte> messageBody, out BufferCollection chunksToProcess)
        {
            chunksToProcess = null;
            using (var decoder = new BinaryDecoder(messageBody.AsMemory().ToArray(), Quotas.MessageContext))
            {
                // read the type of the message before more chunks are processed.
                NodeId typeId = decoder.ReadNodeId(null);

                if (typeId != ObjectIds.GetEndpointsRequest_Encoding_DefaultBinary &&
                    typeId != ObjectIds.FindServersRequest_Encoding_DefaultBinary &&
                    typeId != ObjectIds.FindServersOnNetworkRequest_Encoding_DefaultBinary)
                {
                    chunksToProcess = GetSavedChunks(0, messageBody, true);
                    SendServiceFault(token, requestId, ServiceResult.Create(StatusCodes.BadSecurityPolicyRejected, "Channel can only be used for discovery."));
                    return false;
                }
                return true;
            }
        }

        #endregion

        #region Private Fields
        private SortedDictionary<uint, IServiceResponse> m_queuedResponses;
        private readonly string m_ImplementationString = ".NET Standard ServerChannel UA-TCP " + Utils.GetAssemblyBuildNumber();
        private ReverseConnectAsyncResult m_pendingReverseHello;
        #endregion
    }
}
