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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Manages the client side of a UA TCP channel.
    /// </summary>
    public class UaSCUaBinaryClientChannel : UaSCUaBinaryChannel
    {
        #region Constructors
        /// <summary>
        /// Creates a channel for for a client.
        /// </summary>
        public UaSCUaBinaryClientChannel(
            string contextId,
            BufferManager bufferManager,
            IMessageSocketFactory socketFactory,
            ChannelQuotas quotas,
            X509Certificate2 clientCertificate,
            X509Certificate2 serverCertificate,
            EndpointDescription endpoint)
         :
            this(contextId, bufferManager, socketFactory, quotas, clientCertificate, null, serverCertificate, endpoint)
        {
        }

        /// <summary>
        /// Creates a channel for for a client.
        /// </summary>
        public UaSCUaBinaryClientChannel(
            string contextId,
            BufferManager bufferManager,
            IMessageSocketFactory socketFactory,
            ChannelQuotas quotas,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            X509Certificate2 serverCertificate,
            EndpointDescription endpoint)
        :
            base(
                contextId,
                bufferManager,
                quotas,
                serverCertificate,
                (endpoint != null) ? new EndpointDescriptionCollection(new EndpointDescription[] { endpoint }) : null,
                (endpoint != null) ? endpoint.SecurityMode : MessageSecurityMode.None,
                (endpoint != null) ? endpoint.SecurityPolicyUri : SecurityPolicies.None)
        {
            if (endpoint != null && endpoint.SecurityMode != MessageSecurityMode.None)
            {
                if (clientCertificate == null) throw new ArgumentNullException(nameof(clientCertificate));

                if (clientCertificate.RawData.Length > TcpMessageLimits.MaxCertificateSize)
                {
                    throw new ArgumentException(
                        Utils.Format("The DER encoded certificate may not be more than {0} bytes.", TcpMessageLimits.MaxCertificateSize),
                        nameof(clientCertificate));
                }

                ClientCertificate = clientCertificate;
                ClientCertificateChain = clientCertificateChain;
            }

            m_requests = new Dictionary<uint, WriteOperation>();
            m_lastRequestId = 0;
            m_ConnectCallback = new EventHandler<IMessageSocketAsyncEventArgs>(OnConnectComplete);
            m_startHandshake = new TimerCallback(OnScheduledHandshake);
            m_handshakeComplete = new AsyncCallback(OnHandshakeComplete);
            m_socketFactory = socketFactory;

            // save the endpoint.
            EndpointDescription = endpoint;
            m_url = new Uri(endpoint.EndpointUrl);
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_handshakeTimer")]
        protected override void Dispose(bool disposing)
        {
            m_waitBetweenReconnects = Timeout.Infinite;

            if (disposing)
            {
                Utils.SilentDispose(m_handshakeTimer);
                m_handshakeTimer = null;
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Creates a connection with the server.
        /// </summary>
        public IAsyncResult BeginConnect(Uri url, int timeout, AsyncCallback callback, object state)
        {
            if (url == null) throw new ArgumentNullException(nameof(url));
            if (timeout <= 0) throw new ArgumentException("Timeout must be greater than zero.", nameof(timeout));

            Task task;
            lock (DataLock)
            {
                if (State != TcpChannelState.Closed)
                {
                    throw new InvalidOperationException("Channel is already connected.");
                }

                m_url = url;
                m_via = url;

                // check if configured to use a proxy.
                if (EndpointDescription != null && EndpointDescription.ProxyUrl != null)
                {
                    m_via = EndpointDescription.ProxyUrl;
                }

                // do not attempt reconnect on failure.
                m_waitBetweenReconnects = Timeout.Infinite;

                WriteOperation operation = BeginOperation(timeout, callback, state);
                m_handshakeOperation = operation;

                State = TcpChannelState.Connecting;
                if (ReverseSocket)
                {
                    if (Socket != null)
                    {
                        // send the hello message as response to the reverse hello message.
                        SendHelloMessage(operation);
                    }
                }
                else
                {
                    Socket = m_socketFactory.Create(this, BufferManager, Quotas.MaxBufferSize);
                    task = Task.Run(async () =>
                        await (Socket?.BeginConnect(
                            m_via, m_ConnectCallback, operation,
                            new CancellationTokenSource(timeout).Token) ?? Task.FromResult(false)).ConfigureAwait(false));
                }
            }

            return m_handshakeOperation;
        }

        /// <summary>
        /// Finishes a connect operation.
        /// </summary>
        public void EndConnect(IAsyncResult result)
        {
            var operation = result as WriteOperation;
            if (operation == null) throw new ArgumentNullException(nameof(result));

            try
            {
                operation.End(Int32.MaxValue);
                Utils.LogInfo("CLIENTCHANNEL SOCKET CONNECTED: {0:X8}, ChannelId={1}", Socket.Handle, ChannelId);
            }
            catch (Exception e)
            {
                Shutdown(ServiceResult.Create(e, StatusCodes.BadTcpInternalError, "Fatal error during connect."));
                throw;
            }
            finally
            {
                OperationCompleted(operation);
            }
        }

        /// <summary>
        /// Closes a connection with the server.
        /// </summary>
        public void Close(int timeout)
        {
            WriteOperation operation = null;

            lock (DataLock)
            {
                // nothing to do if the connection is already closed.
                if (State == TcpChannelState.Closed)
                {
                    return;
                }

                // check if a handshake is in progress.
                if (m_handshakeOperation != null && !m_handshakeOperation.IsCompleted)
                {
                    m_handshakeOperation.Fault(ServiceResult.Create(StatusCodes.BadConnectionClosed, "Channel was closed by the user."));
                }

                Utils.LogTrace("ChannelId {0}: Close", ChannelId);

                // attempt a graceful shutdown.
                if (State == TcpChannelState.Open)
                {
                    State = TcpChannelState.Closing;
                    operation = BeginOperation(timeout, null, null);
                    SendCloseSecureChannelRequest(operation);
                }
            }

            // wait for the close to succeed.
            if (operation != null)
            {
                try
                {
                    operation.End(timeout, false);
                }
                catch (ServiceResultException e)
                {
                    switch (e.StatusCode)
                    {
                        case StatusCodes.BadRequestInterrupted:
                        case StatusCodes.BadSecureChannelClosed:
                        {
                            break;
                        }

                        default:
                        {
                            Utils.LogWarning(e, "ChannelId {0}: Could not gracefully close the channel. Reason={1}", ChannelId, e.Result.StatusCode);
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Utils.LogError(e, "ChannelId {0}: Could not gracefully close the channel.", ChannelId);
                }
            }

            // shutdown.
            Shutdown(StatusCodes.BadConnectionClosed);
        }

        /// <summary>
        /// Sends a request to the server.
        /// </summary>
        public IAsyncResult BeginSendRequest(IServiceRequest request, int timeout, AsyncCallback callback, object state)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (timeout <= 0)
            {
                throw new ArgumentException("Timeout must be greater than zero.", nameof(timeout));
            }

            lock (DataLock)
            {
                bool firstCall = false;
                WriteOperation operation = null;

                // check if this is the first call.
                if (State == TcpChannelState.Closed)
                {
                    if (m_queuedOperations == null)
                    {
                        firstCall = true;
                        m_queuedOperations = new List<QueuedOperation>();
                    }
                }

                // queue operations until connect completes.
                if (m_queuedOperations != null)
                {
                    operation = BeginOperation(timeout, callback, state);
                    m_queuedOperations.Add(new QueuedOperation(operation, timeout, request));

                    if (firstCall)
                    {
                        BeginConnect(m_url, timeout, OnConnectOnDemandComplete, null);
                    }

                    return operation;
                }

                if (State != TcpChannelState.Open)
                {
                    throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                }

                Utils.LogTrace("ChannelId {0}: BeginSendRequest()", ChannelId);

                if (m_reconnecting)
                {
                    throw ServiceResultException.Create(StatusCodes.BadRequestInterrupted, "Attempting to reconnect to the server.");
                }

                // send request.
                operation = BeginOperation(timeout, callback, state);
                SendRequest(operation, timeout, request);
                return operation;
            }
        }

        /// <summary>
        /// Returns the response to a previously sent request.
        /// </summary>
        public IServiceResponse EndSendRequest(IAsyncResult result)
        {
            WriteOperation operation = result as WriteOperation;

            if (operation == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            try
            {
                operation.End(Int32.MaxValue);
            }
            finally
            {
                OperationCompleted(operation);
            }

            return operation.MessageBody as IServiceResponse;
        }
        #endregion

        #region Connect/Reconnect Sequence
        /// <summary>
        /// Sends a Hello message.
        /// </summary>
        private void SendHelloMessage(WriteOperation operation)
        {
            Utils.LogTrace("ChannelId {0}: SendHelloMessage()", ChannelId);

            byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "SendHelloMessage");

            try
            {
                MemoryStream ostrm = new MemoryStream(buffer, 0, SendBufferSize);
                BinaryEncoder encoder = new BinaryEncoder(ostrm, Quotas.MessageContext);

                encoder.WriteUInt32(null, TcpMessageType.Hello);
                encoder.WriteUInt32(null, 0);
                encoder.WriteUInt32(null, 0); // ProtocolVersion
                encoder.WriteUInt32(null, (uint)ReceiveBufferSize);
                encoder.WriteUInt32(null, (uint)SendBufferSize);
                encoder.WriteUInt32(null, (uint)MaxResponseMessageSize);
                encoder.WriteUInt32(null, (uint)MaxResponseChunkCount);

                byte[] endpointUrl = new UTF8Encoding().GetBytes(m_url.ToString());

                if (endpointUrl.Length > TcpMessageLimits.MaxEndpointUrlLength)
                {
                    byte[] truncatedUrl = new byte[TcpMessageLimits.MaxEndpointUrlLength];
                    Array.Copy(endpointUrl, truncatedUrl, TcpMessageLimits.MaxEndpointUrlLength);
                    endpointUrl = truncatedUrl;
                }

                encoder.WriteByteString(null, endpointUrl);

                int size = encoder.Close();
                UpdateMessageSize(buffer, 0, size);

                BeginWriteMessage(new ArraySegment<byte>(buffer, 0, size), operation);
                buffer = null;
            }
            finally
            {
                if (buffer != null)
                {
                    BufferManager.ReturnBuffer(buffer, "SendHelloMessage");
                }
            }
        }

        /// <summary>
        /// Processes an Acknowledge message.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "protocolVersion")]
        private bool ProcessAcknowledgeMessage(ArraySegment<byte> messageChunk)
        {
            Utils.LogTrace("ChannelId {0}: ProcessAcknowledgeMessage()", ChannelId);

            // check state.
            if (State != TcpChannelState.Connecting)
            {
                ForceReconnect(ServiceResult.Create(StatusCodes.BadTcpMessageTypeInvalid, "Server sent an unexpected acknowledge message."));
                return false;
            }

            // check if operation was abandoned.
            if (m_handshakeOperation == null)
            {
                return false;
            }

            // read buffer sizes.
            MemoryStream istrm = new MemoryStream(messageChunk.Array, messageChunk.Offset, messageChunk.Count);
            BinaryDecoder decoder = new BinaryDecoder(istrm, Quotas.MessageContext);

            istrm.Seek(TcpMessageLimits.MessageTypeAndSize, SeekOrigin.Current);

            try
            {
                uint protocolVersion = decoder.ReadUInt32(null);
                SendBufferSize = (int)decoder.ReadUInt32(null);
                ReceiveBufferSize = (int)decoder.ReadUInt32(null);
                int maxMessageSize = (int)decoder.ReadUInt32(null);
                int maxChunkCount = (int)decoder.ReadUInt32(null);

                // update the max message size.
                if (maxMessageSize > 0 && maxMessageSize < MaxRequestMessageSize)
                {
                    MaxRequestMessageSize = (int)maxMessageSize;
                }

                if (MaxRequestMessageSize < SendBufferSize)
                {
                    MaxRequestMessageSize = SendBufferSize;
                }

                // update the max chunk count.
                MaxRequestChunkCount = CalculateChunkCount(MaxRequestMessageSize, SendBufferSize);
                if (maxChunkCount > 0 && maxChunkCount < MaxRequestChunkCount)
                {
                    MaxRequestChunkCount = (int)maxChunkCount;
                }
            }
            finally
            {
                decoder.Close();
            }

            // valdiate buffer sizes.
            if (ReceiveBufferSize < TcpMessageLimits.MinBufferSize)
            {
                m_handshakeOperation.Fault(StatusCodes.BadTcpNotEnoughResources, "Server receive buffer size is too small ({0} bytes).", ReceiveBufferSize);
                return false;
            }

            if (SendBufferSize < TcpMessageLimits.MinBufferSize)
            {
                m_handshakeOperation.Fault(StatusCodes.BadTcpNotEnoughResources, "Server send buffer size is too small ({0} bytes).", SendBufferSize);
                return false;
            }

            // ready to open the channel.
            State = TcpChannelState.Opening;

            try
            {
                // check if reconnecting after a socket failure.
                if (CurrentToken != null)
                {
                    SendOpenSecureChannelRequest(true);
                    return false;
                }

                // open a new connection.
                SendOpenSecureChannelRequest(false);
            }
            catch (Exception e)
            {
                m_handshakeOperation.Fault(e, StatusCodes.BadTcpInternalError, "Could not send an Open Secure Channel request.");
            }

            return false;
        }

        /// <summary>
        /// Sends an OpenSecureChannel request.
        /// </summary>
        private void SendOpenSecureChannelRequest(bool renew)
        {
            // create a new token.
            ChannelToken token = CreateToken();
            token.ClientNonce = CreateNonce();

            // construct the request.
            OpenSecureChannelRequest request = new OpenSecureChannelRequest();
            request.RequestHeader.Timestamp = DateTime.UtcNow;

            request.RequestType = (renew) ? SecurityTokenRequestType.Renew : SecurityTokenRequestType.Issue;
            request.SecurityMode = SecurityMode;
            request.ClientNonce = token.ClientNonce;
            request.RequestedLifetime = (uint)Quotas.SecurityTokenLifetime;

            // encode the request.
            byte[] buffer = BinaryEncoder.EncodeMessage(request, Quotas.MessageContext);

            // write the asymmetric message.
            BufferCollection chunksToSend = WriteAsymmetricMessage(
                TcpMessageType.Open,
                m_handshakeOperation.RequestId,
                ClientCertificate,
                ClientCertificateChain,
                ServerCertificate,
                new ArraySegment<byte>(buffer, 0, buffer.Length));

            // save token.
            m_requestedToken = token;

            // write the message to the server.
            try
            {
                BeginWriteMessage(chunksToSend, m_handshakeOperation);
                chunksToSend = null;
            }
            finally
            {
                if (chunksToSend != null)
                {
                    chunksToSend.Release(BufferManager, "SendOpenSecureChannelRequest");
                }
            }
        }

        /// <summary>
        /// Processes an OpenSecureChannel response message.
        /// </summary>
        private bool ProcessOpenSecureChannelResponse(uint messageType, ArraySegment<byte> messageChunk)
        {
            Utils.LogTrace("ChannelId {0}: ProcessOpenSecureChannelResponse()", ChannelId);

            // validate the channel state.            
            if (State != TcpChannelState.Opening && State != TcpChannelState.Open)
            {
                ForceReconnect(ServiceResult.Create(StatusCodes.BadTcpMessageTypeInvalid, "Server sent an unexpected OpenSecureChannel response."));
                return false;
            }

            // check if operation was abandoned.
            if (m_handshakeOperation == null)
            {
                return false;
            }

            // parse the security header.
            uint channelId = 0;
            X509Certificate2 serverCertificate = null;
            uint requestId = 0;
            uint sequenceNumber = 0;

            ArraySegment<byte> messageBody;

            try
            {
                messageBody = ReadAsymmetricMessage(
                    messageChunk,
                    ClientCertificate,
                    out channelId,
                    out serverCertificate,
                    out requestId,
                    out sequenceNumber);
            }
            catch (Exception e)
            {
                ForceReconnect(ServiceResult.Create(e, StatusCodes.BadSecurityChecksFailed, "Could not verify security on OpenSecureChannel response."));
                return false;
            }

            BufferCollection chunksToProcess = null;

            try
            {
                // verify server certificate.
                CompareCertificates(ServerCertificate, serverCertificate, true);

                // verify sequence number.
                ResetSequenceNumber(sequenceNumber);

                // check if it is necessary to wait for more chunks.
                if (!TcpMessageType.IsFinal(messageType))
                {
                    SaveIntermediateChunk(requestId, messageBody, false);
                    return false;
                }

                // get the chunks to process.
                chunksToProcess = GetSavedChunks(requestId, messageBody, false);

                // read message body.
                OpenSecureChannelResponse response = ParseResponse(chunksToProcess) as OpenSecureChannelResponse;

                if (response == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "Server did not return a valid OpenSecureChannelResponse.");
                }

                // the client needs to use the creation time assigned when it sent 
                // the request and ignores the creation time in the response because
                // the server and client clocks may not be synchronized.

                // update token.
                m_requestedToken.TokenId = response.SecurityToken.TokenId;
                m_requestedToken.Lifetime = (int)response.SecurityToken.RevisedLifetime;
                m_requestedToken.ServerNonce = response.ServerNonce;

                string implementation = String.Format(g_ImplementationString, m_socketFactory.Implementation);

                // log security information.
                if (State == TcpChannelState.Opening)
                {
                    Opc.Ua.Security.Audit.SecureChannelCreated(
                        implementation,
                        this.m_url.ToString(),
                        Utils.Format("{0}", channelId),
                        this.EndpointDescription,
                        this.ClientCertificate,
                        serverCertificate,
                        BinaryEncodingSupport.Required);
                }
                else
                {
                    Opc.Ua.Security.Audit.SecureChannelRenewed(
                        implementation,
                        Utils.Format("{0}", channelId));
                }

                ChannelId = m_requestedToken.ChannelId = channelId;
                ActivateToken(m_requestedToken);
                m_requestedToken = null;

                // ready to send requests.
                State = TcpChannelState.Open;
                m_reconnecting = false;

                // enable reconnects. DO NOT USE! 
                // m_waitBetweenReconnects = TcpMessageLimits.MinTimeBetweenReconnects;
                m_waitBetweenReconnects = Timeout.Infinite;

                // schedule reconnect before token expires.
                ScheduleTokenRenewal(CurrentToken);

                // connect finally complete.
                m_handshakeOperation.Complete(0);
            }
            catch (Exception e)
            {
                m_handshakeOperation.Fault(e, StatusCodes.BadTcpInternalError, "Could not process OpenSecureChannelResponse.");
            }
            finally
            {
                if (chunksToProcess != null)
                {
                    chunksToProcess.Release(BufferManager, "ProcessOpenSecureChannelResponse");
                }
            }

            return false;
        }

        /// <summary>
        /// Closes the channel in case the message limits have been exceeded
        /// </summary>
        protected override void DoMessageLimitsExceeded()
        {
            base.DoMessageLimitsExceeded();
            Shutdown(new ServiceResult(StatusCodes.BadResponseTooLarge));
        }

        #endregion

        #region Event Handlers
        /// <summary>
        /// Handles a socket error.
        /// </summary>
        protected override void HandleSocketError(ServiceResult result)
        {
            ForceReconnect(result);
        }

        /// <summary>
        /// Called when a write operation completes.
        /// </summary>
        protected override void HandleWriteComplete(BufferCollection buffers, object state, int bytesWritten, ServiceResult result)
        {
            lock (DataLock)
            {
                WriteOperation operation = state as WriteOperation;

                if (operation != null)
                {
                    if (ServiceResult.IsBad(result))
                    {
                        operation.Fault(new ServiceResult(StatusCodes.BadSecurityChecksFailed, result));
                    }
                }
            }

            base.HandleWriteComplete(buffers, state, bytesWritten, result);
        }

        /// <summary>
        /// Processes an incoming message.
        /// </summary>
        /// <returns>True if the function takes ownership of the buffer.</returns>
        protected override bool HandleIncomingMessage(uint messageType, ArraySegment<byte> messageChunk)
        {
            lock (DataLock)
            {
                // process a response.
                if (TcpMessageType.IsType(messageType, TcpMessageType.Message))
                {
                    //Utils.LogTrace("ChannelId {0}: ProcessResponseMessage", ChannelId);
                    return ProcessResponseMessage(messageType, messageChunk);
                }

                // check for acknowledge.
                else if (messageType == TcpMessageType.Acknowledge)
                {
                    //Utils.LogTrace("ChannelId {0}: ProcessAcknowledgeMessage", ChannelId);
                    return ProcessAcknowledgeMessage(messageChunk);
                }

                // check for error.
                else if (messageType == TcpMessageType.Error)
                {
                    //Utils.LogTrace("ChannelId {0}: ProcessErrorMessage", ChannelId);
                    return ProcessErrorMessage(messageType, messageChunk);
                }

                // process open secure channel repsonse.
                else if (TcpMessageType.IsType(messageType, TcpMessageType.Open))
                {
                    //Utils.LogTrace("ChannelId {0}: ProcessOpenSecureChannelResponse", ChannelId);
                    return ProcessOpenSecureChannelResponse(messageType, messageChunk);
                }

                // process a response to a close request.
                else if (TcpMessageType.IsType(messageType, TcpMessageType.Close))
                {
                    //Utils.LogTrace("ChannelId {0}: ProcessResponseMessage (close)", ChannelId);
                    return ProcessResponseMessage(messageType, messageChunk);
                }

                // invalid message type - must close socket and reconnect.
                ForceReconnect(ServiceResult.Create(StatusCodes.BadTcpMessageTypeInvalid, "The client does not recognize the message type: {0:X8}.", messageType));
                return false;
            }
        }

        /// <summary>
        /// Called when the socket is connected.
        /// </summary>
        private void OnConnectComplete(object sender, IMessageSocketAsyncEventArgs e)
        {
            WriteOperation operation = (WriteOperation)e.UserToken;

            // dual stack ConnectAsync may call in with null UserToken if 
            // one connection attempt timed out but the other succeeded
            if (operation == null)
            {
                return;
            }

            if (e.IsSocketError)
            {
                operation.Fault(StatusCodes.BadNotConnected);
                return;
            }

            lock (DataLock)
            {
                try
                {
                    // check for closed socket.
                    if (Socket == null)
                    {
                        operation.Fault(StatusCodes.BadSecureChannelClosed);
                        return;
                    }

                    // start reading messages.
                    Socket.ReadNextMessage();

                    // send the hello message.
                    SendHelloMessage(operation);
                }
                catch (Exception ex)
                {
                    ServiceResult fault = ServiceResult.Create(
                        ex,
                        StatusCodes.BadTcpInternalError,
                        "An unexpected error occurred while connecting to the server.");

                    operation.Fault(fault);
                }
            }
        }

        /// <summary>
        /// Called when it is time to do a handshake.
        /// </summary>
        private void OnScheduledHandshake(object state)
        {
            try
            {
                Utils.LogInfo("ChannelId {0}: Scheduled Handshake Starting: TokenId={1}", ChannelId, CurrentToken?.TokenId);

                Task task;
                lock (DataLock)
                {
                    // check if renewing a token.
                    ChannelToken token = state as ChannelToken;

                    if (token == CurrentToken)
                    {
                        Utils.LogInfo("ChannelId {0}: Attempting Renew Token Now: TokenId={1}", ChannelId, token?.TokenId);

                        // do nothing if not connected.
                        if (State != TcpChannelState.Open)
                        {
                            return;
                        }

                        // begin the operation.
                        m_handshakeOperation = BeginOperation(Int32.MaxValue, m_handshakeComplete, token);

                        // send the request.
                        SendOpenSecureChannelRequest(true);
                        return;
                    }

                    // must be reconnecting - check if successfully reconnected.
                    if (!m_reconnecting)
                    {
                        return;
                    }

                    Utils.LogInfo("ChannelId {0}: Attempting Reconnect Now.", ChannelId);

                    // cancel any previous attempt.
                    if (m_handshakeOperation != null)
                    {
                        m_handshakeOperation.Fault(StatusCodes.BadTimeout);
                        m_handshakeOperation = null;
                    }

                    // close the socket and reconnect.
                    State = TcpChannelState.Closed;

                    if (Socket != null)
                    {
                        Utils.LogInfo("ChannelId {0}: CLIENTCHANNEL SOCKET CLOSED: {1:X8}", ChannelId, Socket.Handle);
                        Socket.Close();
                        Socket = null;
                    }

                    if (!ReverseSocket)
                    {
                        // create an operation.
                        m_handshakeOperation = BeginOperation(Int32.MaxValue, m_handshakeComplete, null);

                        State = TcpChannelState.Connecting;
                        Socket = m_socketFactory.Create(this, BufferManager, Quotas.MaxBufferSize);
                        task = Task.Run(async () =>
                            await (Socket?.BeginConnect(
                                m_via, m_ConnectCallback, m_handshakeOperation,
                                CancellationToken.None) ?? Task.FromResult(false)).ConfigureAwait(false));
                    }
                }
            }
            catch (Exception e)
            {
                Utils.LogError("ChannelId {0}: Reconnect Failed {1}.", ChannelId, e.Message);
                ForceReconnect(ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error reconnecting or renewing a token."));
            }
        }

        /// <summary>
        /// Called when a token is renewed.
        /// </summary>
        private void OnHandshakeComplete(IAsyncResult result)
        {
            lock (DataLock)
            {
                try
                {
                    if (m_handshakeOperation == null)
                    {
                        return;
                    }

                    Utils.LogTrace("ChannelId {0}: OnHandshakeComplete", ChannelId);

                    m_handshakeOperation.End(Int32.MaxValue);
                    m_handshakeOperation = null;
                    m_reconnecting = false;
                }
                catch (Exception e)
                {
                    Utils.LogError(e, "ChannelId {0}: Handshake Failed {1}", ChannelId, e.Message);

                    m_handshakeOperation = null;
                    m_reconnecting = false;

                    ServiceResult error = ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error reconnecting or renewing a token.");

                    // check for expired channel or token.
                    if (error.Code == StatusCodes.BadTcpSecureChannelUnknown || error.Code == StatusCodes.BadSecurityChecksFailed)
                    {
                        Utils.LogError("ChannelId {0}: Cannot Recover Channel", ChannelId);
                        Shutdown(error);
                        return;
                    }

                    ForceReconnect(ServiceResult.Create(e, StatusCodes.BadUnexpectedError, "Unexpected error reconnecting or renewing a token."));
                }
            }
        }

        /// <summary>
        /// Sends a request to the server.
        /// </summary>
        private void SendRequest(WriteOperation operation, int timeout, IServiceRequest request)
        {
            bool success = false;
            BufferCollection buffers = null;

            try
            {
                // check for valid token.
                ChannelToken token = CurrentToken;

                if (token == null)
                {
                    throw new ServiceResultException(StatusCodes.BadSecureChannelClosed);
                }

                // must return an error to the client if limits are exceeded.
                bool limitsExceeded = false;

                buffers = WriteSymmetricMessage(
                    TcpMessageType.Message,
                    operation.RequestId,
                    token,
                    request,
                    true,
                    out limitsExceeded);

                BeginWriteMessage(buffers, operation);
                buffers = null;
                success = true;

                if (limitsExceeded)
                {
                    throw new ServiceResultException(StatusCodes.BadRequestTooLarge);
                }
            }
            catch (Exception e)
            {
                operation.Fault(e, StatusCodes.BadRequestInterrupted, "Could not send request to server.");
            }
            finally
            {
                if (buffers != null)
                {
                    buffers.Release(BufferManager, "SendRequest");
                }

                if (!success)
                {
                    OperationCompleted(operation);
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Parses the response return from the server.
        /// </summary>
        private IServiceResponse ParseResponse(BufferCollection chunksToProcess)
        {
            IServiceResponse response = BinaryDecoder.DecodeMessage(new ArraySegmentStream(chunksToProcess), null, Quotas.MessageContext) as IServiceResponse;
            if (response == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadStructureMissing, "Could not parse response body.");
            }
            return response;
        }

        /// <summary>
        /// Cancels all pending requests and closes the channel.
        /// </summary>
        private void Shutdown(ServiceResult reason)
        {
            lock (DataLock)
            {
                // channel may already be closed
                if (State == TcpChannelState.Closed)
                {
                    return;
                }

                // clear an unprocessed chunks.
                SaveIntermediateChunk(0, new ArraySegment<byte>(), false);

                // halt any scheduled tasks.
                if (m_handshakeTimer != null)
                {
                    m_handshakeTimer.Dispose();
                    m_handshakeTimer = null;
                }

                // halt any existing handshake.
                if (m_handshakeOperation != null && !m_handshakeOperation.IsCompleted)
                {
                    m_handshakeOperation.Fault(reason);
                }

                // cancel all requests.
                List<WriteOperation> operations = new List<WriteOperation>(m_requests.Values);

                foreach (WriteOperation operation in operations)
                {
                    operation.Fault(new ServiceResult(StatusCodes.BadSecureChannelClosed, reason));
                }

                m_requests.Clear();

                uint channelId = ChannelId;

                // close the socket.
                State = TcpChannelState.Closed;

                // dispose of the tokens.
                ChannelId = 0;
                DiscardTokens();

                // clear the handshake state.
                m_handshakeOperation = null;
                m_requestedToken = null;
                m_reconnecting = false;

                if (Socket != null)
                {
                    Utils.LogInfo("ChannelId {0}: CLIENTCHANNEL SOCKET CLOSED: {1:X8}", channelId, Socket.Handle);
                    Socket.Close();
                    Socket = null;
                }

                // set the state.       
                ChannelStateChanged(TcpChannelState.Closed, reason);
            }
        }

        /// <summary>
        /// Closes the channel and attempts to reconnect.
        /// </summary>
        private void ForceReconnect(ServiceResult reason)
        {
            lock (DataLock)
            {
                // check if reconnect already started.
                if (m_reconnecting)
                {
                    return;
                }

                Utils.LogWarning("ChannelId {0}: Force reconnect reason={1}", Id, reason);

                // check if reconnects are disabled.
                if (State == TcpChannelState.Closing || m_waitBetweenReconnects == Timeout.Infinite)
                {
                    Shutdown(reason);
                    return;
                }

                // cancel all requests.
                List<WriteOperation> operations = new List<WriteOperation>(m_requests.Values);

                foreach (WriteOperation operation in operations)
                {
                    operation.Fault(new ServiceResult(StatusCodes.BadSecureChannelClosed, reason));
                }

                m_requests.Clear();

                // halt any existing handshake.
                if (m_handshakeOperation != null && !m_handshakeOperation.IsCompleted)
                {
                    m_handshakeOperation.Fault(reason);
                    return;
                }

                // clear an unprocessed chunks.
                SaveIntermediateChunk(0, new ArraySegment<byte>(), false);

                // halt any scheduled tasks.
                if (m_handshakeTimer != null)
                {
                    m_handshakeTimer.Dispose();
                    m_handshakeTimer = null;
                }

                // clear the handshake state.
                m_handshakeOperation = null;
                m_requestedToken = null;
                m_reconnecting = true;

                // close the socket.
                State = TcpChannelState.Faulted;

                // schedule a reconnect.
                Utils.LogInfo("ChannelId {0}: Attempting Reconnect in {1} ms. Reason: {2}", ChannelId, m_waitBetweenReconnects, reason.ToLongString());
                m_handshakeTimer = new Timer(m_startHandshake, null, m_waitBetweenReconnects, Timeout.Infinite);

                // set next reconnect period.
                m_waitBetweenReconnects *= 2;

                if (m_waitBetweenReconnects <= TcpMessageLimits.MinTimeBetweenReconnects)
                {
                    m_waitBetweenReconnects = TcpMessageLimits.MinTimeBetweenReconnects + 1000;
                }

                if (m_waitBetweenReconnects > TcpMessageLimits.MaxTimeBetweenReconnects)
                {
                    m_waitBetweenReconnects = TcpMessageLimits.MaxTimeBetweenReconnects;
                }

                ChannelStateChanged(TcpChannelState.Faulted, reason);
            }
        }

        /// <summary>
        /// Schedules the renewal of a token.
        /// </summary>
        private void ScheduleTokenRenewal(ChannelToken token)
        {
            // can't renew if not connected.
            if (State != TcpChannelState.Open)
            {
                return;
            }

            // cancel any outstanding renew operations.
            if (m_handshakeTimer != null)
            {
                m_handshakeTimer.Dispose();
                m_handshakeTimer = null;
            }

            // calculate renewal timing based on token lifetime.
            DateTime expiryTime = token.CreatedAt.AddMilliseconds(token.Lifetime);

            double timeToRenewal = ((expiryTime.Ticks - DateTime.UtcNow.Ticks) / TimeSpan.TicksPerMillisecond) * TcpMessageLimits.TokenRenewalPeriod;

            if (timeToRenewal < 0)
            {
                timeToRenewal = 0;
            }

            Utils.LogInfo("ChannelId {0}: Token Expiry {1}, renewal scheduled in {2} ms.", ChannelId, expiryTime, (int)timeToRenewal);

            m_handshakeTimer = new Timer(m_startHandshake, token, (int)timeToRenewal, Timeout.Infinite);
        }

        /// <summary>
        /// Creates a object to manage the state of an asynchronous operation. 
        /// </summary>
        private WriteOperation BeginOperation(int timeout, AsyncCallback callback, object state)
        {
            WriteOperation operation = new WriteOperation(timeout, callback, state);
            operation.RequestId = Utils.IncrementIdentifier(ref m_lastRequestId);
            m_requests.Add(operation.RequestId, operation);
            return operation;
        }

        /// <summary>
        /// Cleans up after an asychronous operation completes.
        /// </summary>
        private void OperationCompleted(WriteOperation operation)
        {
            if (operation == null)
            {
                return;
            }

            lock (DataLock)
            {
                if (m_handshakeOperation == operation)
                {
                    m_handshakeOperation = null;
                }

                m_requests.Remove(operation.RequestId);
            }
        }

        /// <summary>
        /// Stores the state of a operation that was queued while waiting for the channel to connect.
        /// </summary>
        private struct QueuedOperation
        {
            public QueuedOperation(WriteOperation operation, int timeout, IServiceRequest request)
            {
                Operation = operation;
                Timeout = timeout;
                Request = request;
            }

            public WriteOperation Operation;
            public int Timeout;
            public IServiceRequest Request;
        }


        /// <summary>
        /// Called when the connect operation completes.
        /// </summary>
        /// <param name="state">The state.</param>
        private void OnConnectOnDemandComplete(object state)
        {
            lock (DataLock)
            {
                WriteOperation operation = (WriteOperation)state;

                for (int ii = 0; ii < m_queuedOperations.Count; ii++)
                {
                    QueuedOperation request = m_queuedOperations[ii];

                    // have to check for error on connect.
                    if (ii == 0)
                    {
                        try
                        {
                            operation.End(request.Timeout);
                        }
                        catch (Exception e)
                        {
                            request.Operation.Fault(e, StatusCodes.BadNoCommunication, "Error establishing a connection: " + e.Message);
                            break;
                        }
                    }

                    if (this.CurrentToken == null)
                    {
                        request.Operation.Fault(StatusCodes.BadConnectionClosed, "Could not send request because connection is closed.");
                    }

                    try
                    {
                        SendRequest(request.Operation, request.Timeout, request.Request);
                    }
                    catch (Exception e)
                    {
                        request.Operation.Fault(e, StatusCodes.BadCommunicationError, "Could not send request.");
                    }
                }

                m_queuedOperations = null;
            }
        }
        #endregion 

        #region Message Processing
        /// <summary>
        /// Processes an Error message received over the socket.
        /// </summary>
        protected bool ProcessErrorMessage(uint messageType, ArraySegment<byte> messageChunk)
        {
            // read request buffer sizes.            
            MemoryStream istrm = new MemoryStream(messageChunk.Array, messageChunk.Offset, messageChunk.Count, false);
            BinaryDecoder decoder = new BinaryDecoder(istrm, Quotas.MessageContext);

            istrm.Seek(TcpMessageLimits.MessageTypeAndSize, SeekOrigin.Current);

            try
            {
                ServiceResult error = ReadErrorMessageBody(decoder);

                Utils.LogTrace("ChannelId {0}: ProcessErrorMessage({1})", ChannelId, error);

                // check if a handshake is in progress
                if (m_handshakeOperation != null)
                {
                    m_handshakeOperation.Fault(error);
                    return false;
                }

                // handle the fatal error.
                ForceReconnect(error);
                return false;
            }
            finally
            {
                decoder.Close();
            }
        }

        /// <summary>
        /// Sends an CloseSecureChannel request message.
        /// </summary>
        private void SendCloseSecureChannelRequest(WriteOperation operation)
        {
            Utils.LogTrace("ChannelId {0}: SendCloseSecureChannelRequest()", ChannelId);

            // supress reconnects if an error occurs.
            m_waitBetweenReconnects = Timeout.Infinite;

            // check for valid token.
            ChannelToken currentToken = CurrentToken;

            if (currentToken == null)
            {
                throw new ServiceResultException(StatusCodes.BadSecureChannelClosed);
            }

            CloseSecureChannelRequest request = new CloseSecureChannelRequest();
            request.RequestHeader.Timestamp = DateTime.UtcNow;

            // limits should never be exceeded sending a close message.
            bool limitsExceeded = false;

            // construct the message.
            BufferCollection buffers = WriteSymmetricMessage(
                TcpMessageType.Close,
                operation.RequestId,
                currentToken,
                request,
                true,
                out limitsExceeded);

            // send the message.
            try
            {
                BeginWriteMessage(buffers, operation);
                buffers = null;
            }
            finally
            {
                if (buffers != null)
                {
                    buffers.Release(BufferManager, "SendCloseSecureChannelRequest");
                }
            }
        }

        /// <summary>
        /// Processes a response message.
        /// </summary>
        private bool ProcessResponseMessage(uint messageType, ArraySegment<byte> messageChunk)
        {
            Utils.LogTrace("ChannelId {0}: ProcessResponseMessage()", ChannelId);

            // validate security on the message.
            ChannelToken token = null;
            uint requestId = 0;
            uint sequenceNumber = 0;

            ArraySegment<byte> messageBody;

            try
            {
                messageBody = ReadSymmetricMessage(messageChunk, false, out token, out requestId, out sequenceNumber);
            }
            catch (Exception e)
            {
                ForceReconnect(ServiceResult.Create(e, StatusCodes.BadSecurityChecksFailed, "Could not verify security on response."));
                return false;
            }

            // check if operation is still available.
            WriteOperation operation = null;

            if (!m_requests.TryGetValue(requestId, out operation))
            {
                return false;
            }

            BufferCollection chunksToProcess = null;

            // check for replay attacks.
            if (!VerifySequenceNumber(sequenceNumber, "ProcessResponseMessage"))
            {
                throw new ServiceResultException(StatusCodes.BadSequenceNumberInvalid);
            }

            try
            {
                // check for an abort.
                if (TcpMessageType.IsAbort(messageType))
                {
                    // get the chunks to process.
                    chunksToProcess = GetSavedChunks(requestId, messageBody, false);

                    // decoder reason.
                    MemoryStream istrm = new MemoryStream(messageBody.Array, messageBody.Offset, messageBody.Count, false);
                    BinaryDecoder decoder = new BinaryDecoder(istrm, Quotas.MessageContext);
                    ServiceResult error = ReadErrorMessageBody(decoder);
                    decoder.Close();

                    // report a fault.
                    operation.Fault(true, error);
                    return true;
                }

                // check if it is necessary to wait for more chunks.
                if (!TcpMessageType.IsFinal(messageType))
                {
                    SaveIntermediateChunk(requestId, messageBody, false);
                    return true;
                }

                // get the chunks to process.
                chunksToProcess = GetSavedChunks(requestId, messageBody, false);

                // get response.
                operation.MessageBody = ParseResponse(chunksToProcess);

                if (operation.MessageBody == null)
                {
                    operation.Fault(true, StatusCodes.BadStructureMissing, "Could not parse response body.");
                    return true;
                }

                // is complete.
                operation.Complete(true, 0);
                return true;
            }
            catch (Exception e)
            {
                // log a callstack to get a hint on where the decoder failed.
                Utils.LogError(e, "Unexpected error processing response.");
                operation.Fault(true, e, StatusCodes.BadUnknownResponse, "Unexpected error processing response.");
                return true;
            }
            finally
            {
                if (chunksToProcess != null)
                {
                    chunksToProcess.Release(BufferManager, "ProcessResponseMessage");
                }
            }
        }
        #endregion

        #region Private Fields
        private Uri m_url;
        private Uri m_via;
        private long m_lastRequestId;
        private Dictionary<uint, WriteOperation> m_requests;
        private WriteOperation m_handshakeOperation;
        private ChannelToken m_requestedToken;
        private Timer m_handshakeTimer;
        private bool m_reconnecting;
        private int m_waitBetweenReconnects;
        private EventHandler<IMessageSocketAsyncEventArgs> m_ConnectCallback;
        private IMessageSocketFactory m_socketFactory;
        private TimerCallback m_startHandshake;
        private AsyncCallback m_handshakeComplete;
        private List<QueuedOperation> m_queuedOperations;
        private readonly string g_ImplementationString = ".NET Standard ClientChannel {0} " + Utils.GetAssemblyBuildNumber();
        #endregion
    }
}
