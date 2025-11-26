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

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Manages the client side of a UA TCP channel.
    /// </summary>
    public class UaSCUaBinaryClientChannel : UaSCUaBinaryChannel
    {
        /// <summary>
        /// Creates a channel for a client.
        /// </summary>
        public UaSCUaBinaryClientChannel(
            string contextId,
            BufferManager bufferManager,
            IMessageSocketFactory socketFactory,
            ChannelQuotas quotas,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            X509Certificate2 serverCertificate,
            EndpointDescription endpoint,
            ITelemetryContext telemetry)
            : base(
                contextId,
                bufferManager,
                quotas,
                serverCertificate,
                endpoint != null ? [.. new EndpointDescription[] { endpoint }] : null,
                endpoint != null ? endpoint.SecurityMode : MessageSecurityMode.None,
                endpoint != null ? endpoint.SecurityPolicyUri : SecurityPolicies.None,
                telemetry)
        {
            m_telemetry = telemetry;
            m_logger = m_telemetry.CreateLogger<UaSCUaBinaryClientChannel>();

            if (endpoint == null)
            {
                throw new ArgumentException("Endpoint not specified.", nameof(endpoint));
            }

            if (endpoint.SecurityMode != MessageSecurityMode.None)
            {
                if (clientCertificate == null)
                {
                    throw new ArgumentNullException(nameof(clientCertificate));
                }

                if (clientCertificate.RawData.Length > TcpMessageLimits.MaxCertificateSize)
                {
                    throw new ArgumentException(
                        Utils.Format(
                            "The DER encoded certificate may not be more than {0} bytes.",
                            TcpMessageLimits.MaxCertificateSize
                        ),
                        nameof(clientCertificate));
                }

                ClientCertificate = clientCertificate;
                ClientCertificateChain = clientCertificateChain;
            }

            m_requests = new ConcurrentDictionary<uint, WriteOperation>();
            m_startHandshake = new TimerCallback(OnScheduledHandshake);
            m_handshakeComplete = new AsyncCallback(OnHandshakeComplete);
            m_socketFactory = socketFactory;
            m_implementationString = Utils.Format(
                "UA.NETStandard ClientChannel {0} {1}",
                m_socketFactory.Implementation,
                Utils.GetAssemblyBuildNumber());

            // save the endpoint.
            EndpointDescription = endpoint;
            m_url = new Uri(endpoint.EndpointUrl);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            m_waitBetweenReconnects = Timeout.Infinite;

            if (disposing)
            {
                OnTokenActivated = null;

                Utils.SilentDispose(m_handshakeTimer);
                m_handshakeTimer = null;
                Utils.SilentDispose(m_requestedToken);
                m_requestedToken = null;
                m_requests?.Clear();
                m_handshakeOperation = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Connect the channel
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="url"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask ConnectAsync(Uri url, int timeout, CancellationToken ct)
        {
            using Activity? activity = m_telemetry.StartActivity();
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            if (timeout <= 0)
            {
                throw new ArgumentException("Timeout must be greater than zero.", nameof(timeout));
            }

            WriteOperation operation;
            IMessageSocket? socket;
            lock (DataLock)
            {
                if (State != TcpChannelState.Closed)
                {
                    throw ServiceResultException.Unexpected("Channel is already connected.");
                }

                m_url = url;
                m_via = url;

                // check if configured to use a proxy.
                if (EndpointDescription != null && EndpointDescription.ProxyUrl != null)
                {
                    m_logger.LogInformation(
                        "CLIENTCHANNEL SOCKET CONNECTING to {Url} via {Proxy}: ChannelId={ChannelId}",
                        url,
                        EndpointDescription.ProxyUrl,
                        ChannelId);
                    m_via = url = EndpointDescription.ProxyUrl;
                }
                else
                {
                    m_logger.LogInformation(
                        "CLIENTCHANNEL SOCKET CONNECTING to {Url}: ChannelId={ChannelId}",
                        url,
                        ChannelId);
                }

                // do not attempt reconnect on failure.
                m_waitBetweenReconnects = Timeout.Infinite;

                operation = BeginOperation(timeout, null, null);
                m_handshakeOperation = operation;

                State = TcpChannelState.Connecting;

                // set the state.
                ChannelStateChanged(TcpChannelState.Connecting, ServiceResult.Good);

                if (!ReverseSocket)
                {
                    Socket = m_socketFactory.Create(this, BufferManager, Quotas.MaxBufferSize);
                }
                socket = Socket;
            }
            try
            {
                if (socket == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadNotConnected,
                        "Could not create or get connected socket.");
                }
                else if (ReverseSocket)
                {
                    // send the hello message as response to the reverse hello message.
                    SendHelloMessage(operation);
                }
                else
                {
                    await socket.ConnectAsync(url, ct).ConfigureAwait(false);

                    m_logger.LogInformation(
                        "CLIENTCHANNEL SOCKET CONNECTED via {Url}: {Handle:X8}, ChannelId={ChannelId}",
                        url,
                        Socket?.Handle,
                        ChannelId);

                    CompleteConnect(operation);
                }
                await operation.EndAsync(int.MaxValue, ct: ct).ConfigureAwait(false);

                SendQueuedOperations();
            }
            catch (Exception e)
            {
                m_logger.LogError(e,
                    "CLIENTCHANNEL SOCKET CONNECT FAILED via {Url}: {Handle:X8}, ChannelId={ChannelId}",
                    url,
                    Socket?.Handle,
                    ChannelId);

                operation.Fault(StatusCodes.BadNotConnected);

                Shutdown(ServiceResult.Create(
                    e,
                    StatusCodes.BadTcpInternalError,
                    "Fatal error during connect."));
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
        public async Task CloseAsync(int timeout, CancellationToken ct = default)
        {
            using Activity? activity = m_telemetry.StartActivity();
            WriteOperation? operation = InternalClose(timeout);

            // wait for the close to succeed.
            if (operation != null)
            {
                try
                {
                    _ = await operation.EndAsync(timeout, false, ct).ConfigureAwait(false);
                    ValidateChannelCloseError(operation.Error);
                }
                catch (Exception e)
                {
                    m_logger.LogError(
                        e,
                        "ChannelId {ChannelId}: Could not gracefully close the channel.",
                        ChannelId);
                }
            }

            // shutdown.
            Shutdown(StatusCodes.BadConnectionClosed);
        }

        /// <summary>
        /// Sends a request to the server.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<IServiceResponse> SendRequestAsync(
            IServiceRequest request,
            int timeout,
            CancellationToken ct)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (timeout <= 0)
            {
                throw new ArgumentException("Timeout must be greater than zero.", nameof(timeout));
            }

            using Activity? activity = m_telemetry.StartActivity();
            WriteOperation? operation = null;
            lock (DataLock)
            {
                // Queue the operation while connecting and it will be played once connected.
                if (State == TcpChannelState.Connecting)
                {
                    operation = BeginOperation(timeout, null, null);
                    QueueConnectOperation(operation, timeout, request);
                }
                else
                {
                    if (State != TcpChannelState.Open)
                    {
                        throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                    }

                    m_logger.LogDebug("ChannelId {ChannelId}: BeginSendRequest()", ChannelId);

                    if (m_reconnecting)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadRequestInterrupted,
                            "Attempting to reconnect to the server.");
                    }

                    // send request.
                    operation = BeginOperation(timeout, null, null);
                    SendRequest(operation, request);
                }
            }
            try
            {
                await operation.EndAsync(int.MaxValue, true, ct).ConfigureAwait(false);
            }
            finally
            {
                OperationCompleted(operation);
            }
            if (operation.MessageBody is not IServiceResponse response)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnknownResponse,
                    "Server did not return a valid Service Response.");
            }
            return response;
        }

        /// <summary>
        /// Sends a Hello message.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void SendHelloMessage(WriteOperation operation)
        {
            if (m_url == null)
            {
                throw ServiceResultException.Unexpected("Endpoint not defined.");
            }

            m_logger.LogDebug("ChannelId {ChannelId}: SendHelloMessage()", ChannelId);

            byte[]? buffer = BufferManager.TakeBuffer(SendBufferSize, "SendHelloMessage");

            try
            {
                var ostrm = new MemoryStream(buffer, 0, SendBufferSize);
                using var encoder = new BinaryEncoder(ostrm, Quotas.MessageContext, false);
                encoder.WriteUInt32(null, TcpMessageType.Hello);
                encoder.WriteUInt32(null, 0);
                encoder.WriteUInt32(null, 0); // ProtocolVersion
                encoder.WriteUInt32(null, (uint)ReceiveBufferSize);
                encoder.WriteUInt32(null, (uint)SendBufferSize);
                encoder.WriteUInt32(null, (uint)MaxResponseMessageSize);
                encoder.WriteUInt32(null, (uint)MaxResponseChunkCount);

                byte[] endpointUrl = Encoding.UTF8.GetBytes(m_url.ToString());

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
        private bool ProcessAcknowledgeMessage(ArraySegment<byte> messageChunk)
        {
            m_logger.LogDebug("ChannelId {ChannelId}: ProcessAcknowledgeMessage()", ChannelId);

            // check state.
            if (State != TcpChannelState.Connecting)
            {
                ForceReconnect(
                    ServiceResult.Create(
                        StatusCodes.BadTcpMessageTypeInvalid,
                        "Server sent an unexpected acknowledge message."));
                return false;
            }

            // check if operation was abandoned.
            if (m_handshakeOperation == null)
            {
                return false;
            }

            // read buffer sizes.
            using (var decoder = new BinaryDecoder(messageChunk, Quotas.MessageContext))
            {
                ReadAndVerifyMessageTypeAndSize(
                    decoder,
                    TcpMessageType.Acknowledge,
                    messageChunk.Count);

                uint protocolVersion = decoder.ReadUInt32(null);
                // note: decode of send and receive buffer size are swapped here to reflect the view of the client
                uint sendBufferSize = decoder.ReadUInt32(null);
                uint receiveBufferSize = decoder.ReadUInt32(null);
                uint maxMessageSize = decoder.ReadUInt32(null);
                uint maxChunkCount = decoder.ReadUInt32(null);

                // returned buffer sizes shall not be larger than requested sizes
                if (sendBufferSize > SendBufferSize)
                {
                    m_handshakeOperation.Fault(
                        StatusCodes.BadTcpNotEnoughResources,
                        "Returned client send buffer size is larger than requested size ({0}>{1} bytes).",
                        sendBufferSize,
                        SendBufferSize);
                    return false;
                }

                if (receiveBufferSize > ReceiveBufferSize)
                {
                    m_handshakeOperation.Fault(
                        StatusCodes.BadTcpNotEnoughResources,
                        "Returned client receive buffer size is larger than requested size ({0}>{1} bytes).",
                        receiveBufferSize,
                        ReceiveBufferSize);
                    return false;
                }

                // validate buffer sizes.
                if (receiveBufferSize is < TcpMessageLimits.MinBufferSize or > TcpMessageLimits
                    .MaxBufferSize)
                {
                    m_handshakeOperation.Fault(
                        StatusCodes.BadTcpNotEnoughResources,
                        "Client receive buffer size is out of valid range ({0} bytes).",
                        receiveBufferSize);
                    return false;
                }

                if (sendBufferSize is < TcpMessageLimits.MinBufferSize or > TcpMessageLimits
                    .MaxBufferSize)
                {
                    m_handshakeOperation.Fault(
                        StatusCodes.BadTcpNotEnoughResources,
                        "Client send buffer size is out of valid range ({0} bytes).",
                        sendBufferSize);
                    return false;
                }

                // assign new values once ensured that sizes are within bounds
                SendBufferSize = (int)sendBufferSize;
                ReceiveBufferSize = (int)receiveBufferSize;

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

            // ready to open the channel.
            State = TcpChannelState.Opening;

            // set the state.
            ChannelStateChanged(TcpChannelState.Opening, ServiceResult.Good);

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
                m_handshakeOperation.Fault(
                    e,
                    StatusCodes.BadTcpInternalError,
                    "Could not send an Open Secure Channel request.");
            }

            return false;
        }

        /// <summary>
        /// Sends an OpenSecureChannel request.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void SendOpenSecureChannelRequest(bool renew)
        {
            if (m_handshakeOperation == null)
            {
                throw ServiceResultException.Unexpected(
                    "handshakeOperation not specified.");
            }

            // create a new token.
            ChannelToken token = CreateToken();
            token.ClientNonce = CreateNonce(ClientCertificate);

            if (renew)
            {
                token.PreviousSecret = CurrentToken?.Secret;
            }

            // construct the request.
            var request = new OpenSecureChannelRequest();
            request.RequestHeader.Timestamp = DateTime.UtcNow;

            request.RequestType = renew
                ? SecurityTokenRequestType.Renew
                : SecurityTokenRequestType.Issue;
            request.SecurityMode = SecurityMode;
            request.ClientNonce = token.ClientNonce;
            request.RequestedLifetime = (uint)Quotas.SecurityTokenLifetime;

            // encode the request.
            byte[] buffer = BinaryEncoder.EncodeMessage(request, Quotas.MessageContext);

            ClientChannelCertificate = ClientCertificate?.RawData;
            ServerChannelCertificate = ServerCertificate?.RawData;

            byte[] signature;

            // write the asymmetric message.
            BufferCollection? chunksToSend = WriteAsymmetricMessage(
                TcpMessageType.Open,
                m_handshakeOperation.RequestId,
                ClientCertificate,
                ClientCertificateChain,
                ServerCertificate,
                new ArraySegment<byte>(buffer, 0, buffer.Length),
                m_oscRequestSignature,
                out signature);

            // don't keep signature if secure channel enhancements are not used.
            m_oscRequestSignature = (SecurityPolicy.SecureChannelEnhancements) ? signature : null;

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
                chunksToSend?.Release(BufferManager, "SendOpenSecureChannelRequest");
            }
        }

        /// <summary>
        /// Processes an OpenSecureChannel response message.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private bool ProcessOpenSecureChannelResponse(
            uint messageType,
            ArraySegment<byte> messageChunk)
        {
            m_logger.LogDebug("ChannelId {ChannelId}: ProcessOpenSecureChannelResponse()", ChannelId);

            // validate the channel state.
            if (State is not TcpChannelState.Opening and not TcpChannelState.Open)
            {
                ForceReconnect(
                    ServiceResult.Create(
                        StatusCodes.BadTcpMessageTypeInvalid,
                        "Server sent an unexpected OpenSecureChannel response."));
                return false;
            }

            // check if operation was abandoned.
            if (m_handshakeOperation == null || m_url == null)
            {
                return false;
            }

            ArraySegment<byte> messageBody;

            // parse the security header.
            uint channelId;

            X509Certificate2 serverCertificate;

            uint requestId;

            uint sequenceNumber;
            try
            {
                byte[] signature;

                Console.WriteLine($"OSC IN={TcpMessageType.KeyToString(messageChunk)}");

                messageBody = ReadAsymmetricMessage(
                    messageChunk,
                    ClientCertificate,
                    out channelId,
                    out serverCertificate,
                    out requestId,
                    out sequenceNumber,
                    m_oscRequestSignature,
                    out signature);

                if (PreviousToken == null)
                {
                    ComputeSecureChannelHash(signature);
                }

                Console.WriteLine($"OSC OUT={TcpMessageType.KeyToString(messageBody)}");
                Console.WriteLine($"oscRequestSignature={TcpMessageType.KeyToString(m_oscRequestSignature)}");
                Console.WriteLine($"signature={TcpMessageType.KeyToString(signature)}");
                Console.WriteLine($"State={State}");
            }
            catch (Exception e)
            {
                m_logger.LogDebug(e,
                   "ChannelId {ChannelId}: Could not verify security on OpenSecureChannel response",
                   ChannelId);

                ForceReconnect(
                    ServiceResult.Create(
                        e,
                        StatusCodes.BadSecurityChecksFailed,
                        "Could not verify security on OpenSecureChannel response."));
                return false;
            }

            BufferCollection? chunksToProcess = null;

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

                if (ParseResponse(chunksToProcess) is not OpenSecureChannelResponse response)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeMismatch,
                        "Server did not return a valid OpenSecureChannelResponse.");
                }

                // the client needs to use the creation time assigned when it sent
                // the request and ignores the creation time in the response because
                // the server and client clocks may not be synchronized.

                // update token.
                if (m_requestedToken == null)
                {
                    throw ServiceResultException.Unexpected("requested token invalid.");
                }

                m_requestedToken.TokenId = response.SecurityToken.TokenId;
                m_requestedToken.Lifetime = (int)response.SecurityToken.RevisedLifetime;
                m_requestedToken.ServerNonce = response.ServerNonce;

                if (!ValidateNonce(ServerCertificate, response.ServerNonce))
                {
                    throw new ServiceResultException(StatusCodes.BadNonceInvalid);
                }

                // log security information.
                if (State == TcpChannelState.Opening)
                {
                    m_logger.SecureChannelCreated(
                        m_implementationString,
                        m_url.ToString(),
                        Utils.Format("{0}", channelId),
                        EndpointDescription,
                        ClientCertificate,
                        serverCertificate,
                        BinaryEncodingSupport.Required);
                }
                else
                {
                    m_logger.SecureChannelRenewed(m_implementationString, Utils.Format("{0}", channelId));
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

                // set the state.
                ChannelStateChanged(TcpChannelState.Open, ServiceResult.Good);
            }
            catch (Exception e)
            {
                m_logger.LogError(e,
                   "ChannelId {ChannelId}: Could not process OpenSecureChannelResponse",
                   ChannelId);

                m_handshakeOperation.Fault(
                    e,
                    StatusCodes.BadTcpInternalError,
                    "Could not process OpenSecureChannelResponse.");
            }
            finally
            {
                chunksToProcess?.Release(BufferManager, "ProcessOpenSecureChannelResponse");
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
        protected override void HandleWriteComplete(
            BufferCollection buffers,
            object state,
            int bytesWritten,
            ServiceResult result)
        {
            lock (DataLock)
            {
                if (state is WriteOperation operation && ServiceResult.IsBad(result))
                {
                    operation.Fault(new ServiceResult(StatusCodes.BadSecurityChecksFailed, result));
                }
            }

            base.HandleWriteComplete(buffers, state, bytesWritten, result);
        }

        /// <summary>
        /// Processes an incoming message.
        /// </summary>
        /// <returns>True if the function takes ownership of the buffer.</returns>
        protected override bool HandleIncomingMessage(
            uint messageType,
            ArraySegment<byte> messageChunk)
        {
            //m_logger.LogWarning("IN:{Size}", TcpMessageType.GetTypeAndSize(messageChunk));

            // process a response.
            if (TcpMessageType.IsType(messageType, TcpMessageType.Message))
            {
                m_logger.LogDebug("ChannelId {ChannelId}: ProcessResponseMessage", ChannelId);
                return ProcessResponseMessage(messageType, messageChunk);
            }

            lock (DataLock)
            {
                // check for acknowledge.
                if (messageType == TcpMessageType.Acknowledge)
                {
                    m_logger.LogDebug("ChannelId {ChannelId}: ProcessAcknowledgeMessage", ChannelId);
                    return ProcessAcknowledgeMessage(messageChunk);
                }
                // check for error.
                else if (messageType == TcpMessageType.Error)
                {
                    m_logger.LogDebug("ChannelId {ChannelId}: ProcessErrorMessage", ChannelId);
                    return ProcessErrorMessage(messageChunk);
                }
                // process open secure channel repsonse.
                else if (TcpMessageType.IsType(messageType, TcpMessageType.Open))
                {
                    m_logger.LogDebug("ChannelId {ChannelId}: ProcessOpenSecureChannelResponse", ChannelId);
                    return ProcessOpenSecureChannelResponse(messageType, messageChunk);
                }
                // process a response to a close request.
                else if (TcpMessageType.IsType(messageType, TcpMessageType.Close))
                {
                    m_logger.LogDebug("ChannelId {ChannelId}: ProcessResponseMessage (close)", ChannelId);
                    return ProcessResponseMessage(messageType, messageChunk);
                }

                // invalid message type - must close socket and reconnect.
                ForceReconnect(
                    ServiceResult.Create(
                        StatusCodes.BadTcpMessageTypeInvalid,
                        "The client does not recognize the message type: {0:X8}.",
                        messageType));
                return false;
            }
        }

        /// <summary>
        /// Validates the result of a channel close operation.
        /// </summary>
        private void ValidateChannelCloseError(ServiceResult error)
        {
            if (ServiceResult.IsBad(error))
            {
                StatusCode statusCode = error.StatusCode;
                switch ((uint)statusCode)
                {
                    case StatusCodes.BadRequestInterrupted:
                    case StatusCodes.BadSecureChannelClosed:
                        break;
                    default:
                        m_logger.LogWarning(
                            "ChannelId {ChannelId}: Could not gracefully close the channel. Reason={ServiceResult}",
                            ChannelId,
                            error);
                        break;
                }
            }
        }

        /// <summary>
        /// Queues an operation for sending after the channel is connected.
        /// Inserts operations that create or activate a session or don't require a session first.
        /// </summary>
        /// <returns>true if a valid service call for BeginConnect is queued.</returns>
        /// <exception cref="ServiceResultException"></exception>
        private bool QueueConnectOperation(
            WriteOperation operation,
            int timeout,
            IServiceRequest request)
        {
            m_queuedOperations ??= [];
            var queuedOperation = new QueuedOperation(operation, timeout, request);

            // operations that must be sent first and which allow for a connect.
            if (request.TypeId == DataTypeIds.ActivateSessionRequest ||
                request.TypeId == DataTypeIds.CreateSessionRequest ||
                request.TypeId == DataTypeIds.GetEndpointsRequest ||
                request.TypeId == DataTypeIds.FindServersOnNetworkRequest ||
                request.TypeId == DataTypeIds.FindServersRequest ||
                request.TypeId == DataTypeIds.RegisterServerRequest ||
                request.TypeId == DataTypeIds.RegisterServer2Request)
            {
                m_queuedOperations.Add(queuedOperation);
                return true;
            }

            // fail until a valid service call for BeginConnect is queued.
            if (m_queuedOperations.Count == 0)
            {
                operation.Fault(StatusCodes.BadSecureChannelClosed);
                throw new ServiceResultException(StatusCodes.BadNotConnected);
            }

            m_queuedOperations.Add(queuedOperation);

            return false;
        }

        private void CompleteConnect(WriteOperation operation)
        {
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
                    var fault = ServiceResult.Create(
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
        /// <exception cref="ServiceResultException"></exception>
        private async void OnScheduledHandshake(object? state)
        {
            if (m_via == null)
            {
                throw ServiceResultException.Unexpected("Endpoint not defined.");
            }
            try
            {
                m_logger.LogInformation(
                    "ChannelId {ChannelId}: Scheduled Handshake Starting: TokenId={TokenId}",
                    ChannelId,
                    CurrentToken?.TokenId);

                IMessageSocket? socket = null;
                WriteOperation? operation = null;
                lock (DataLock)
                {
                    // check if renewing a token.
                    var token = state as ChannelToken;

                    if (token == CurrentToken)
                    {
                        m_logger.LogInformation(
                            "ChannelId {ChannelId}: Attempting Renew Token Now: TokenId={TokenId}",
                            ChannelId,
                            token?.TokenId);

                        // do nothing if not connected.
                        if (State != TcpChannelState.Open)
                        {
                            return;
                        }

                        // begin the operation.
                        m_handshakeOperation = BeginOperation(
                            int.MaxValue,
                            m_handshakeComplete,
                            token);

                        // send the request.
                        SendOpenSecureChannelRequest(true);
                        return;
                    }

                    // must be reconnecting - check if successfully reconnected.
                    if (!m_reconnecting)
                    {
                        return;
                    }

                    m_logger.LogInformation("ChannelId {ChannelId}: Attempting Reconnect Now.", ChannelId);

                    // cancel any previous attempt.
                    if (m_handshakeOperation != null)
                    {
                        m_handshakeOperation.Fault(StatusCodes.BadTimeout);
                        m_handshakeOperation = null;
                    }

                    // close the socket and reconnect.
                    State = TcpChannelState.Closed;

                    // Discard the current handshake timer
                    Utils.SilentDispose(m_handshakeTimer);
                    m_handshakeTimer = null;

                    // dispose of the tokens.
                    uint channelId = ChannelId;
                    ChannelId = 0;
                    DiscardTokens();

                    socket = Socket;
                    if (socket != null)
                    {
                        Socket = null;
                        m_logger.LogInformation(
                            "ChannelId {ChannelId}: CLIENTCHANNEL SOCKET CLOSED ON SCHEDULED HANDSHAKE: {Handle:X8}",
                            channelId,
                            socket.Handle);
                        socket.Close();
                        socket = null;
                    }

                    // set the state.
                    ChannelStateChanged(TcpChannelState.Closed, ServiceResult.Good);

                    if (!ReverseSocket)
                    {
                        // create an operation.
                        m_handshakeOperation = BeginOperation(
                            int.MaxValue,
                            m_handshakeComplete,
                            null);

                        State = TcpChannelState.Connecting;
                        socket = m_socketFactory.Create(this, BufferManager, Quotas.MaxBufferSize);

                        operation = m_handshakeOperation;
                        Socket = socket;

                        // set the state.
                        ChannelStateChanged(TcpChannelState.Connecting, ServiceResult.Good);
                    }
                }

                // Reconnect
                if (socket != null && operation != null)
                {
                    try
                    {
                        await socket.ConnectAsync(m_via).ConfigureAwait(false);
                        m_logger.LogInformation(
                            "CLIENTCHANNEL SOCKET RECONNECTED: {Handle:X8}, ChannelId={ChannelId}",
                            socket.Handle,
                            ChannelId);

                        // Complete connect
                        CompleteConnect(operation);

                        // Complete handshake
                        await operation.EndAsync(int.MaxValue).ConfigureAwait(false);

                        SendQueuedOperations();
                    }
                    catch (Exception e)
                    {
                        m_logger.LogError(e,
                            "CLIENTCHANNEL SOCKET RECONNECT FAILED: {Handle:X8}, ChannelId={ChannelId}",
                            Socket?.Handle,
                            ChannelId);

                        operation.Fault(StatusCodes.BadNotConnected);

                        Shutdown(ServiceResult.Create(
                            e,
                            StatusCodes.BadTcpInternalError,
                            "Fatal error during connect."));
                        throw;
                    }
                    finally
                    {
                        OperationCompleted(operation);

                        m_reconnecting = false;
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "ChannelId {ChannelId}: Reconnect Failed.", ChannelId);
                ForceReconnect(
                    ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        "Unexpected error reconnecting or renewing a token."));
            }
        }

        /// <summary>
        /// Called when a token is renewed.
        /// </summary>
        private void OnHandshakeComplete(IAsyncResult? result)
        {
            lock (DataLock)
            {
                ServiceResult? error = null;
                try
                {
                    if (m_handshakeOperation == null)
                    {
                        return;
                    }

                    m_logger.LogDebug("ChannelId {ChannelId}: OnHandshakeComplete", ChannelId);

                    m_handshakeOperation.End(int.MaxValue);

                    return;
                }
                catch (Exception e)
                {
                    m_logger.LogError(e, "ChannelId {ChannelId}: Handshake Failed", ChannelId);

                    error = ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        "Unexpected error reconnecting or renewing a token.");

                    // check for expired channel or token.
                    if (error.Code is
                            StatusCodes.BadTcpSecureChannelUnknown or
                            StatusCodes.BadSecurityChecksFailed)
                    {
                        m_logger.LogError("ChannelId {ChannelId}: Cannot Recover Channel", ChannelId);
                        Shutdown(error);
                        return;
                    }
                }
                finally
                {
                    OperationCompleted(m_handshakeOperation);
                    m_reconnecting = false;
                }

                ForceReconnect(error);
            }
        }

        /// <summary>
        /// Sends a request to the server.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void SendRequest(WriteOperation operation, IServiceRequest request)
        {
            bool success = false;
            BufferCollection? buffers = null;

            try
            {
                // check for valid token.
                ChannelToken token =
                    CurrentToken ??
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecureChannelClosed,
                        "Channel{0}: Token missing to send request on client channel.", Id);

                // must return an error to the client if limits are exceeded.

                buffers = WriteSymmetricMessage(
                    TcpMessageType.Message,
                    operation.RequestId,
                    token,
                    request,
                    true,
                    out bool limitsExceeded);

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
                operation.Fault(
                    e,
                    StatusCodes.BadRequestInterrupted,
                    "Could not send request to server.");
            }
            finally
            {
                buffers?.Release(BufferManager, "SendRequest");

                if (!success)
                {
                    OperationCompleted(operation);
                }
            }
        }

        /// <summary>
        /// Parses the response return from the server.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private IServiceResponse ParseResponse(BufferCollection chunksToProcess)
        {
            if (BinaryDecoder.DecodeMessage(
                    new ArraySegmentStream(chunksToProcess),
                    null,
                    Quotas.MessageContext)
                is not IServiceResponse response)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadStructureMissing,
                    "Could not parse response body.");
            }
            return response;
        }

        /// <summary>
        /// Cancels all pending requests and closes the channel.
        /// </summary>
        private void Shutdown(ServiceResult reason)
        {
            if (State == TcpChannelState.Closed)
            {
                return;
            }

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
                if (m_handshakeOperation?.IsCompleted == false)
                {
                    m_handshakeOperation.Fault(reason);
                }

                // cancel all requests.
                foreach (KeyValuePair<uint, WriteOperation> operation in m_requests.ToArray())
                {
                    operation.Value
                        .Fault(new ServiceResult(StatusCodes.BadSecureChannelClosed, reason));
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
                Utils.SilentDispose(m_requestedToken);
                m_requestedToken = null;
                m_reconnecting = false;

                IMessageSocket socket = Socket;
                if (socket != null)
                {
                    Socket = null;
                    m_logger.LogInformation(
                        "ChannelId {ChannelId}: CLIENTCHANNEL SOCKET CLOSED SHUTDOWN: {Handle:X8}",
                        channelId,
                        socket.Handle);
                    socket.Close();
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

                // check if reconnects are disabled.
                if (State == TcpChannelState.Closing || m_waitBetweenReconnects == Timeout.Infinite)
                {
                    Shutdown(reason);
                    return;
                }

                m_logger.LogWarning("ChannelId {ChannelId}: Force reconnect reason={ServiceResult}", Id, reason);

                // cancel all requests.
                foreach (KeyValuePair<uint, WriteOperation> operation in m_requests.ToArray())
                {
                    operation.Value
                        .Fault(new ServiceResult(StatusCodes.BadSecureChannelClosed, reason));
                }
                m_requests.Clear();

                // halt any existing handshake.
                if (m_handshakeOperation?.IsCompleted == false)
                {
                    m_handshakeOperation.Fault(reason);
                    return;
                }

                // clear an unprocessed chunks.
                SaveIntermediateChunk(0, new ArraySegment<byte>(), false);

                // halt any scheduled tasks.
                if (m_handshakeTimer != null)
                {
                    Utils.SilentDispose(m_handshakeTimer);
                    m_handshakeTimer = null;
                }

                // clear the handshake state.
                m_handshakeOperation = null;
                Utils.SilentDispose(m_requestedToken);
                m_requestedToken = null;
                m_reconnecting = true;

                // close the socket.
                State = TcpChannelState.Faulted;

                // schedule a reconnect.
                m_logger.LogInformation(
                    "ChannelId {ChannelId}: Attempting Reconnect in {Delay} ms. Reason: {ServiceResult}",
                    ChannelId,
                    m_waitBetweenReconnects,
                    reason.ToLongString());
                m_handshakeTimer = new Timer(
                    m_startHandshake,
                    null,
                    m_waitBetweenReconnects,
                    Timeout.Infinite);

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
                Utils.SilentDispose(m_handshakeTimer);
                m_handshakeTimer = null;
            }

            // calculate renewal timing based on token lifetime + jitter. Do not rely on the server time!
            int jitterResolution = (int)Math.Round(
                token.Lifetime * TcpMessageLimits.TokenRenewalJitterPeriod);
            int jitter = UnsecureRandom.Shared.Next(-jitterResolution, jitterResolution);
            int timeToRenewal =
                (int)Math.Round(token.Lifetime * TcpMessageLimits.TokenRenewalPeriod) +
                jitter -
                (HiResClock.TickCount - token.CreatedAtTickCount);
            if (timeToRenewal < 0)
            {
                timeToRenewal = 0;
            }

            m_logger.LogInformation(
                "ChannelId {ChannelId}: Token Expiry {Expiration:HH:mm:ss.fff}, renewal scheduled at {Renewal:HH:mm:ss.fff} in {Duration} ms.",
                ChannelId,
                token.CreatedAt.AddMilliseconds(token.Lifetime),
                HiResClock.UtcTickCount(token.CreatedAtTickCount + timeToRenewal),
                timeToRenewal);

            m_handshakeTimer = new Timer(m_startHandshake, token, timeToRenewal, Timeout.Infinite);
        }

        /// <summary>
        /// Creates an object to manage the state of an asynchronous operation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private WriteOperation BeginOperation(int timeout, AsyncCallback? callback, object? state)
        {
            uint requestId = Utils.IncrementIdentifier(ref m_lastRequestId);
            if (requestId == 0)
            {
                requestId = Utils.IncrementIdentifier(ref m_lastRequestId);
            }
            var operation = new WriteOperation(timeout, callback, state, m_logger)
            {
                RequestId = requestId
            };
            if (!m_requests.TryAdd(operation.RequestId, operation))
            {
                throw ServiceResultException.Unexpected(
                    "Could not add request {0} to list of pending operations.",
                    operation.RequestId);
            }
            return operation;
        }

        /// <summary>
        /// Cleans up after an asychronous operation completes.
        /// </summary>
        private void OperationCompleted(WriteOperation? operation)
        {
            if (operation == null)
            {
                return;
            }

            if (ReferenceEquals(m_handshakeOperation, operation))
            {
                m_handshakeOperation = null;
            }

            if (!m_requests.TryRemove(operation.RequestId, out _))
            {
                m_logger.LogDebug(
                    "Could not remove requestId {RequestId} from list of pending operations.",
                    operation.RequestId);
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
        private void SendQueuedOperations()
        {
            lock (DataLock)
            {
                if (m_queuedOperations == null)
                {
                    // Already completed
                    return;
                }

                for (int ii = 0; ii < m_queuedOperations.Count; ii++)
                {
                    QueuedOperation request = m_queuedOperations[ii];

                    if (CurrentToken == null)
                    {
                        request.Operation.Fault(
                            StatusCodes.BadConnectionClosed,
                            "Could not send request because connection is closed.");
                        continue;
                    }

                    try
                    {
                        SendRequest(request.Operation, request.Request);
                    }
                    catch (Exception e)
                    {
                        request.Operation
                            .Fault(e, StatusCodes.BadCommunicationError, "Could not send request.");
                    }
                }

                m_queuedOperations = null;
            }
        }

        private WriteOperation? InternalClose(int timeout)
        {
            WriteOperation? operation = null;
            lock (DataLock)
            {
                // nothing to do if the connection is already closed.
                if (State == TcpChannelState.Closed)
                {
                    return null;
                }

                // check if a handshake is in progress.
                if (m_handshakeOperation?.IsCompleted == false)
                {
                    m_handshakeOperation.Fault(
                        ServiceResult.Create(
                            StatusCodes.BadConnectionClosed,
                            "Channel was closed by the user."));
                    OperationCompleted(m_handshakeOperation);
                }

                m_logger.LogDebug("ChannelId {ChannelId}: Close", ChannelId);

                // attempt a graceful shutdown.
                if (State == TcpChannelState.Open)
                {
                    State = TcpChannelState.Closing;
                    operation = BeginOperation(timeout, null, null);
                    SendCloseSecureChannelRequest(operation);

                    // set the state.
                    ChannelStateChanged(TcpChannelState.Closing, ServiceResult.Good);
                }
            }

            return operation;
        }

        /// <summary>
        /// Processes an Error message received over the socket.
        /// </summary>
        protected bool ProcessErrorMessage(ArraySegment<byte> messageChunk)
        {
            ServiceResult error;

            // read request buffer sizes.
            using (var decoder = new BinaryDecoder(messageChunk, Quotas.MessageContext))
            {
                ReadAndVerifyMessageTypeAndSize(decoder, TcpMessageType.Error, messageChunk.Count);

                error = ReadErrorMessageBody(decoder);
            }

            m_logger.LogDebug("ChannelId {ChannelId}: ProcessErrorMessage({ServiceResult})", ChannelId, error);

            // check if a handshake is in progress
            if (m_handshakeOperation != null)
            {
                m_handshakeOperation.Fault(error);
                OperationCompleted(m_handshakeOperation);
                return false;
            }

            // handle the fatal error.
            ForceReconnect(error);
            return false;
        }

        /// <summary>
        /// Sends an CloseSecureChannel request message.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void SendCloseSecureChannelRequest(WriteOperation operation)
        {
            m_logger.LogDebug("ChannelId {ChannelId}: SendCloseSecureChannelRequest()", ChannelId);

            // suppress reconnects if an error occurs.
            m_waitBetweenReconnects = Timeout.Infinite;

            // check for valid token.
            ChannelToken currentToken =
                CurrentToken ??
                throw ServiceResultException.Create(
                    StatusCodes.BadSecureChannelClosed,
                    "Channel{0}:Token missing to send close secure channel request on client channel.", Id);

            var request = new CloseSecureChannelRequest();
            request.RequestHeader.Timestamp = DateTime.UtcNow;

            // limits should never be exceeded sending a close message.

            // construct the message.
            BufferCollection? buffers = WriteSymmetricMessage(
                TcpMessageType.Close,
                operation.RequestId,
                currentToken,
                request,
                true,
                out _);

            // send the message.
            try
            {
                BeginWriteMessage(buffers, operation);
                buffers = null;
            }
            finally
            {
                buffers?.Release(BufferManager, "SendCloseSecureChannelRequest");
            }
        }

        /// <summary>
        /// Processes a response message.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private bool ProcessResponseMessage(uint messageType, ArraySegment<byte> messageChunk)
        {
            m_logger.LogDebug("ChannelId {ChannelId}: ProcessResponseMessage()", ChannelId);

            ArraySegment<byte> messageBody;

            uint requestId;

            uint sequenceNumber;
            try
            {
                // validate security on the message.
                messageBody = ReadSymmetricMessage(
                    messageChunk,
                    false,
                    out ChannelToken token,
                    out requestId,
                    out sequenceNumber);
            }
            catch (Exception e)
            {
                ForceReconnect(
                    ServiceResult.Create(
                        e,
                        StatusCodes.BadSecurityChecksFailed,
                        "Could not verify security on response."));
                return false;
            }

            // check if operation is still available.
            if (!m_requests.TryGetValue(requestId, out WriteOperation? operation))
            {
                return false;
            }

            BufferCollection? chunksToProcess = null;

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

                    ServiceResult error;

                    // decode error reason.
                    using (var decoder = new BinaryDecoder(messageBody, Quotas.MessageContext))
                    {
                        error = ReadErrorMessageBody(decoder);
                    }

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
                    operation.Fault(
                        true,
                        StatusCodes.BadStructureMissing,
                        "Could not parse response body.");
                    return true;
                }

                // is complete.
                operation.Complete(true, 0);
                return true;
            }
            catch (Exception e)
            {
                // log a callstack to get a hint on where the decoder failed.
                m_logger.LogError(e, "Unexpected error processing response.");
                operation.Fault(
                    true,
                    e,
                    StatusCodes.BadUnknownResponse,
                    "Unexpected error processing response.");
                return true;
            }
            finally
            {
                chunksToProcess?.Release(BufferManager, "ProcessResponseMessage");
            }
        }

        private Uri? m_url;
        private Uri? m_via;
        private uint m_lastRequestId;
        private readonly ConcurrentDictionary<uint, WriteOperation> m_requests;
        private WriteOperation? m_handshakeOperation;
        private ChannelToken? m_requestedToken;
        private Timer? m_handshakeTimer;
        private bool m_reconnecting;
        private int m_waitBetweenReconnects;
        private readonly IMessageSocketFactory m_socketFactory;
        private readonly string m_implementationString;
        private readonly TimerCallback m_startHandshake;
        private readonly AsyncCallback m_handshakeComplete;
        private List<QueuedOperation>? m_queuedOperations;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;
        private byte[]? m_oscRequestSignature;
    }
}
