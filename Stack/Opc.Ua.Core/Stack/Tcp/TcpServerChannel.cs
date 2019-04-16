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
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{

    /// <summary>
    /// Manages the server side of a UA TCP channel.
    /// </summary>
    public class TcpServerChannel : UaSCUaBinaryChannel
    {
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
        }

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
            X509Certificate2Collection serverCertificateChain,
            EndpointDescriptionCollection endpoints)
        :
            base(contextId, bufferManager, quotas, serverCertificate, serverCertificateChain, endpoints, MessageSecurityMode.None, SecurityPolicies.None)
        {
            m_listener = listener;
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
            if (disposing)
            {
                Utils.SilentDispose(m_cleanupTimer);
                m_cleanupTimer = null;
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Sets the callback used to receive notifications of new events.
        /// </summary>
        public void SetRequestReceivedCallback(TcpChannelRequestEventHandler callback)
        {
            lock (DataLock)
            {
                m_RequestReceived = callback;
            }
        }

        /// <summary>
        /// Attaches the channel to an existing socket.
        /// </summary>
        public void Attach(uint channelId, Socket socket)
        {
            if (socket == null)
            {
                throw new ArgumentNullException(nameof(socket));
            }

            lock (DataLock)
            {
                // check for existing socket.
                if (Socket != null)
                {
                    throw new InvalidOperationException("Channel is already attached to a socket.");
                }

                ChannelId = channelId;
                State = TcpChannelState.Connecting;

                Socket = new TcpMessageSocket(this, socket, BufferManager, Quotas.MaxBufferSize);
                Utils.Trace("TCPSERVERCHANNEL SOCKET ATTACHED: {0:X8}, ChannelId={1}", Socket.Handle, ChannelId);
                Socket.ReadNextMessage();

                // automatically clean up the channel if no hello received.
                StartCleanupTimer(StatusCodes.BadTimeout);
            }
        }

        /// <summary>
        /// Handles a reconnect request.
        /// </summary>
        public void Reconnect(
            IMessageSocket socket,
            uint requestId,
            uint sequenceNumber,
            X509Certificate2 clientCertificate,
            ChannelToken token,
            OpenSecureChannelRequest request)
        {
            if (socket == null)
            {
                throw new ArgumentNullException(nameof(socket));
            }

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
                    // replace the socket.
                    Socket = socket;
                    Utils.Trace("TCPSERVERCHANNEL SOCKET RECONNECTED: {0:X8}, ChannelId={1}", Socket.Handle, ChannelId);
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

                    // send any queue responses.
                    Task.Factory.StartNew(OnChannelReconnected, m_queuedResponses);
                    m_queuedResponses = new SortedDictionary<uint, IServiceResponse>();
                }
                catch (Exception e)
                {
                    SendServiceFault(token, requestId, ServiceResult.Create(e, StatusCodes.BadTcpInternalError, "Unexpected error processing request."));
                }
            }
        }

        /// <summary>
        /// Sends the response for the specified request.
        /// </summary>
        public void SendResponse(uint requestId, IServiceResponse response)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            lock (DataLock)
            {
                // must queue the response if the channel is in the faulted state.
                if (State == TcpChannelState.Faulted)
                {
                    m_queuedResponses[requestId] = response;
                    return;
                }

                Utils.Trace("Channel {0}: SendResponse {1}", ChannelId, requestId);

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
                m_responseRequired = true;

                try
                {
                    // process a response.
                    if (TcpMessageType.IsType(messageType, TcpMessageType.Message))
                    {
                        //Utils.Trace("Channel {0}: ProcessRequestMessage", ChannelId);
                        return ProcessRequestMessage(messageType, messageChunk);
                    }

                    // check for hello.
                    if (messageType == TcpMessageType.Hello)
                    {
                        //Utils.Trace("Channel {0}: ProcessHelloMessage", ChannelId);
                        return ProcessHelloMessage(messageChunk);
                    }

                    // process open secure channel repsonse.
                    if (TcpMessageType.IsType(messageType, TcpMessageType.Open))
                    {
                        //Utils.Trace("Channel {0}: ProcessOpenSecureChannelRequest", ChannelId);
                        return ProcessOpenSecureChannelRequest(messageType, messageChunk);
                    }

                    // process close secure channel response.
                    if (TcpMessageType.IsType(messageType, TcpMessageType.Close))
                    {
                        //Utils.Trace("Channel {0}: ProcessCloseSecureChannelRequest", ChannelId);
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
                    m_responseRequired = false;
                }
            }
        }
        #endregion

        #region Error Handling Functions
        /// <summary>
        /// Handles a socket error.
        /// </summary>
        protected override void HandleSocketError(ServiceResult result)
        {
            lock (DataLock)
            {
                // channel fault.
                if (ServiceResult.IsBad(result))
                {
                    ForceChannelFault(result);
                    return;
                }

                // gracefully shutdown the channel.
                ChannelClosed();
            }
        }

        /// <summary>
        /// Forces the channel into a faulted state as a result of a fatal error.
        /// </summary>
        protected void ForceChannelFault(uint statusCode, string format, params object[] args)
        {
            ForceChannelFault(ServiceResult.Create(statusCode, format, args));
        }

        /// <summary>
        /// Forces the channel into a faulted state as a result of a fatal error.
        /// </summary>
        protected void ForceChannelFault(Exception exception, uint defaultCode, string format, params object[] args)
        {
            ForceChannelFault(ServiceResult.Create(exception, defaultCode, format, args));
        }

        /// <summary>
        /// Forces the channel into a faulted state as a result of a fatal error.
        /// </summary>
        protected void ForceChannelFault(ServiceResult reason)
        {
            lock (DataLock)
            {
                Utils.Trace(
                    "TCPSERVERCHANNEL ForceChannelFault Socket={0:X8}, ChannelId={1}, TokenId={2}, Reason={3}",
                    (Socket != null) ? Socket.Handle : 0,
                    (CurrentToken != null) ? CurrentToken.ChannelId : 0,
                    (CurrentToken != null) ? CurrentToken.TokenId : 0,
                    reason.ToLongString());

                // nothing to do if channel already in a faulted state.
                if (State == TcpChannelState.Faulted)
                {
                    return;
                }

                // send error and close response.
                if (Socket != null)
                {
                    if (m_responseRequired)
                    {
                        SendErrorMessage(reason);
                    }

                }

                State = TcpChannelState.Faulted;
                m_responseRequired = false;

                // ensure the channel will be cleaned up if the client does not reconnect.
                StartCleanupTimer(reason);
            }
        }

        /// <summary>
        /// Starts a timer that will clean up the channel if it is not opened/re-opened.
        /// </summary>
        protected void StartCleanupTimer(ServiceResult reason)
        {
            CleanupTimer();
            m_cleanupTimer = new Timer(new TimerCallback(OnCleanup), reason, Quotas.ChannelLifetime, Timeout.Infinite);
        }

        /// <summary>
        /// Cleans up a timer that will clean up the channel if it is not opened/re-opened.
        /// </summary>
        private void CleanupTimer()
        {
            if (m_cleanupTimer != null)
            {
                m_cleanupTimer.Dispose();
                m_cleanupTimer = null;
            }
        }

        /// <summary>
        /// Called when the channel needs to be cleaned up.
        /// </summary>
        private void OnCleanup(object state)
        {
            lock (DataLock)
            {
                CleanupTimer();
                // nothing to do if the channel is now open or closed.
                if (State == TcpChannelState.Closed || State == TcpChannelState.Open)
                {
                    return;
                }

                // get reason for cleanup.
                ServiceResult reason = state as ServiceResult;

                if (reason == null)
                {
                    reason = new ServiceResult(StatusCodes.BadTimeout);
                }

                Utils.Trace(
                    "TCPSERVERCHANNEL Cleanup Socket={0:X8}, ChannelId={1}, TokenId={2}, Reason={3}",
                    (Socket != null) ? Socket.Handle : 0,
                    (CurrentToken != null) ? CurrentToken.ChannelId : 0,
                    (CurrentToken != null) ? CurrentToken.TokenId : 0,
                    reason.ToLongString());

                // close channel.
                ChannelClosed();
            }
        }

        /// <summary>
        /// Closes the channel and releases resources.
        /// </summary>
        private void ChannelClosed()
        {
            try
            {
                if (Socket != null)
                {
                    Socket.Close();
                }
            }
            finally
            {
                State = TcpChannelState.Closed;
                m_listener.ChannelClosed(ChannelId);
                CleanupTimer();
            }
        }

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
                    Utils.Trace(e, "Unexpected error re-sending request (ID={0}).", response.Key);
                }
            }
        }

        /// <summary>
        /// Sends an error message over the socket.
        /// </summary>
        protected void SendErrorMessage(ServiceResult error)
        {
            Utils.Trace("Channel {0}: SendErrorMessage()", ChannelId);

            byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "SendErrorMessage");

            try
            {
                BinaryEncoder encoder = new BinaryEncoder(buffer, 0, SendBufferSize, Quotas.MessageContext);

                encoder.WriteUInt32(null, TcpMessageType.Error);
                encoder.WriteUInt32(null, 0);

                WriteErrorMessageBody(encoder, error);

                int size = encoder.Close();
                UpdateMessageSize(buffer, 0, size);

                BeginWriteMessage(new ArraySegment<byte>(buffer, 0, size), null);
                buffer = null;
            }
            finally
            {
                if (buffer != null)
                {
                    BufferManager.ReturnBuffer(buffer, "SendErrorMessage");
                }
            }
        }

        /// <summary>
        /// Sends a fault response secured with the symmetric keys.
        /// </summary>
        private void SendServiceFault(ChannelToken token, uint requestId, ServiceResult fault)
        {
            Utils.Trace("Channel {0} Request {1}: SendServiceFault()", ChannelId, requestId);

            BufferCollection buffers = null;

            try
            {
                // construct fault.
                ServiceFault response = new ServiceFault();

                response.ResponseHeader.ServiceResult = fault.Code;

                StringTable stringTable = new StringTable();

                response.ResponseHeader.ServiceDiagnostics = new DiagnosticInfo(
                    fault,
                    DiagnosticsMasks.NoInnerStatus,
                    true,
                    stringTable);

                response.ResponseHeader.StringTable = stringTable.ToArray();

                // the limits should never be exceeded when sending a fault.
                bool limitsExceeded = false;

                // secure message.
                buffers = WriteSymmetricMessage(
                    TcpMessageType.Message,
                    requestId,
                    token,
                    response,
                    false,
                    out limitsExceeded);

                // send message.
                BeginWriteMessage(buffers, null);
                buffers = null;
            }
            catch (Exception e)
            {
                if (buffers != null)
                {
                    buffers.Release(BufferManager, "SendServiceFault");
                }

                ForceChannelFault(ServiceResult.Create(e, StatusCodes.BadTcpInternalError, "Unexpected error sending a service fault."));
            }
        }

        /// <summary>
        /// Sends a fault response secured with the asymmetric keys.
        /// </summary>
        private void SendServiceFault(uint requestId, ServiceResult fault)
        {
            Utils.Trace("Channel {0} Request {1}: SendServiceFault()", ChannelId, requestId);

            BufferCollection chunksToSend = null;

            try
            {
                // construct fault.
                ServiceFault response = new ServiceFault();

                response.ResponseHeader.ServiceResult = fault.Code;

                StringTable stringTable = new StringTable();

                response.ResponseHeader.ServiceDiagnostics = new DiagnosticInfo(
                    fault,
                    DiagnosticsMasks.NoInnerStatus,
                    true,
                    stringTable);

                response.ResponseHeader.StringTable = stringTable.ToArray();

                // serialize fault.
                byte[] buffer = BinaryEncoder.EncodeMessage(response, Quotas.MessageContext);

                // secure message.
                chunksToSend = WriteAsymmetricMessage(
                    TcpMessageType.Open,
                    requestId,
                    ServerCertificate,
                    ClientCertificate,
                    new ArraySegment<byte>(buffer, 0, buffer.Length));

                // write the message to the server.
                BeginWriteMessage(chunksToSend, null);
                chunksToSend = null;
            }
            catch (Exception e)
            {
                if (chunksToSend != null)
                {
                    chunksToSend.Release(BufferManager, "SendServiceFault");
                }

                ForceChannelFault(ServiceResult.Create(e, StatusCodes.BadTcpInternalError, "Unexpected error sending a service fault."));
            }
        }
        #endregion

        #region Connect/Reconnect Sequence
        /// <summary>
        /// Returns a new token id.
        /// </summary>
        protected uint GetNewTokenId()
        {
            return Utils.IncrementIdentifier(ref m_lastTokenId);
        }

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
                if (maxChunkCount > 0 && maxChunkCount < MaxResponseChunkCount)
                {
                    MaxResponseChunkCount = (int)maxChunkCount;
                }

                // send acknowledge.
                byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "ProcessHelloMessage");

                try
                {
                    MemoryStream ostrm = new MemoryStream(buffer, 0, SendBufferSize);
                    BinaryEncoder encoder = new BinaryEncoder(ostrm, Quotas.MessageContext);

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
                ServiceResultException innerException = e.InnerException as ServiceResultException;

                // If the certificate structre, signare and trust list checks pass, we return the other specific validation errors instead of BadSecurityChecksFailed
                if (innerException != null)
                {
                    if (innerException.StatusCode == StatusCodes.BadCertificateTimeInvalid ||
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
                    else if (innerException.StatusCode == StatusCodes.BadCertificateUntrusted ||
                        innerException.StatusCode == StatusCodes.BadCertificateChainIncomplete ||
                        innerException.StatusCode == StatusCodes.BadCertificateRevoked)
                    {
                        ForceChannelFault(StatusCodes.BadSecurityChecksFailed, e.Message);
                        return false;
                    }
                }

                ForceChannelFault(e, StatusCodes.BadSecurityChecksFailed, "Could not verify security on OpenSecureChannel request.");
                return false;
            }

            BufferCollection chunksToProcess = null;

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
                    SaveIntermediateChunk(requestId, messageBody);
                    return false;
                }

                // create a new token.
                ChannelToken token = CreateToken();

                token.TokenId = GetNewTokenId();
                token.ServerNonce = CreateNonce();

                // get the chunks to process.
                chunksToProcess = GetSavedChunks(requestId, messageBody);

                OpenSecureChannelRequest request = (OpenSecureChannelRequest)BinaryDecoder.DecodeMessage(
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
                        m_listener.ReconnectToExistingChannel(
                            Socket,
                            requestId,
                            sequenceNumber,
                            channelId,
                            ClientCertificate,
                            token,
                            request);

                        Utils.Trace(
                            "TCPSERVERCHANNEL ReconnectToExistingChannel Socket={0:X8}, ChannelId={1}, TokenId={2}",
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
                        g_ImplementationString,
                        this.m_listener.EndpointUrl.ToString(),
                        Utils.Format("{0}", this.ChannelId),
                        this.EndpointDescription,
                        this.ClientCertificate,
                        this.ServerCertificate,
                        BinaryEncodingSupport.Required);
                }
                else
                {
                    Opc.Ua.Security.Audit.SecureChannelRenewed(
                        g_ImplementationString,
                        Utils.Format("{0}", this.ChannelId));
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
                return false;
            }
            catch (Exception e)
            {
                SendServiceFault(requestId, ServiceResult.Create(e, StatusCodes.BadTcpInternalError, "Unexpected error processing OpenSecureChannel request."));
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

        /// <summary>
        /// Sends an OpenSecureChannel response.
        /// </summary>
        private void SendOpenSecureChannelResponse(uint requestId, ChannelToken token, OpenSecureChannelRequest request)
        {
            Utils.Trace("Channel {0}: SendOpenSecureChannelResponse()", ChannelId);

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
                    throw new ServiceResultException(StatusCodes.BadSequenceNumberInvalid);
                }
            }
            catch (Exception e)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, e, "Could not verify security on CloseSecureChannel request.");
            }

            BufferCollection chunksToProcess = null;

            try
            {
                // check if it is necessary to wait for more chunks.
                if (!TcpMessageType.IsFinal(messageType))
                {
                    SaveIntermediateChunk(requestId, messageBody);
                    return false;
                }

                // get the chunks to process.
                chunksToProcess = GetSavedChunks(requestId, messageBody);

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
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error processing OpenSecureChannel request.");
            }
            finally
            {
                if (chunksToProcess != null)
                {
                    chunksToProcess.Release(BufferManager, "ProcessCloseSecureChannelRequest");
                }

                Utils.Trace(
                    "TCPSERVERCHANNEL ProcessCloseSecureChannelRequest Socket={0:X8}, ChannelId={1}, TokenId={2}",
                    (Socket != null) ? Socket.Handle : 0,
                    (CurrentToken != null) ? CurrentToken.ChannelId : 0,
                    (CurrentToken != null) ? CurrentToken.TokenId : 0);

                // close the channel.
                ChannelClosed();
            }

            // return false would double free the buffer
            return true;
        }


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
                    Utils.Trace("Server Revoked Token. ChannelId={1}, TokenId={0}", PreviousToken.TokenId, PreviousToken.ChannelId, DateTime.UtcNow);
                    PreviousToken.Lifetime = 0;
                }
            }
            catch (Exception e)
            {
                Utils.Trace("Could not verify security on incoming request.");
                ForceChannelFault(e, StatusCodes.BadSecurityChecksFailed, "Could not verify security on incoming request.");
                return false;
            }

            BufferCollection chunksToProcess = null;

            try
            {
                // check for an abort.
                if (TcpMessageType.IsAbort(messageType))
                {
                    Utils.Trace("Request was aborted.");
                    chunksToProcess = GetSavedChunks(requestId, messageBody);
                    return true;
                }

                // check if it is necessary to wait for more chunks.
                if (!TcpMessageType.IsFinal(messageType))
                {
                    SaveIntermediateChunk(requestId, messageBody);
                    return true;
                }

                Utils.Trace("Channel {0}: ProcessRequestMessage {1}", ChannelId, requestId);

                // get the chunks to process.
                chunksToProcess = GetSavedChunks(requestId, messageBody);

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
                    if (!(request is GetEndpointsRequest || request is FindServersRequest))
                    {
                        SendServiceFault(token, requestId, ServiceResult.Create(StatusCodes.BadSecurityPolicyRejected, "Channel can only be used for discovery."));
                        return true;
                    }
                }

                // hand the request to the server.
                if (m_RequestReceived != null)
                {
                    m_RequestReceived(this, requestId, request);
                }

                return true;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error processing request.");
                SendServiceFault(token, requestId, ServiceResult.Create(e, StatusCodes.BadTcpInternalError, "Unexpected error processing request."));
                return false;
            }
            finally
            {
                if (chunksToProcess != null)
                {
                    chunksToProcess.Release(BufferManager, "ProcessRequestMessage");
                }
            }
        }
        #endregion

        #region Private Fields
        private TcpChannelRequestEventHandler m_RequestReceived;
        private long m_lastTokenId;
        private ITcpChannelListener m_listener;
        private SortedDictionary<uint, IServiceResponse> m_queuedResponses;
        private Timer m_cleanupTimer;
        private bool m_responseRequired;
        private string g_ImplementationString = ".NetStandard ServerChannel UA-TCP " + Utils.GetAssemblyBuildNumber();
        #endregion
    }

    /// <summary>
    /// Used to report an incoming request.
    /// </summary>
    public delegate void TcpChannelRequestEventHandler(TcpServerChannel channel, uint requestId, IServiceRequest request);
}
