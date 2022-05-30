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
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Manages the server side of a UA TCP channel.
    /// </summary>
    public partial class UaTcpChannelSerializer : IDisposable
    {
        #region Private Fields
        private object m_lock = new object();
        private BufferManager m_bufferManager;
        private ChannelQuotas m_quotas;

        private int m_receiveBufferSize;
        private int m_sendBufferSize;
        private int m_maxRequestMessageSize;
        private int m_maxResponseMessageSize;
        private int m_maxRequestChunkCount;
        private int m_maxResponseChunkCount;

        private uint m_channelId;
        private long m_lastTokenId;
        private long m_sequenceNumber;
        private uint m_remoteSequenceNumber;
        private bool m_sequenceRollover;
        private uint m_partialRequestId;
        private BufferCollection m_partialMessageChunks;
        private ChannelToken m_requestedToken;

        private X509Certificate2 m_serverCertificate;
        private X509Certificate2 m_clientCertificate;
        private EndpointDescriptionCollection m_endpoints;
        private EndpointDescription m_selectedEndpoint;
        private MessageSecurityMode m_securityMode;
        private string m_securityPolicyUri;
        private bool m_discoveryOnly;
        private RNGCryptoServiceProvider m_random;
        private bool m_uninitialized;
        #endregion

        #region Constructors
        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public UaTcpChannelSerializer(
            BufferManager bufferManager,
            ChannelQuotas quotas,
            X509Certificate2 serverCertificate,
            X509Certificate2 clientCertificate,
            EndpointDescriptionCollection endpoints)
        {
            if (bufferManager == null) throw new ArgumentNullException("bufferManager");
            if (quotas == null) throw new ArgumentNullException("quotas");

            m_bufferManager = bufferManager;
            m_quotas = quotas;
            m_serverCertificate = serverCertificate;
            m_clientCertificate = clientCertificate;
            m_discoveryOnly = false;
            m_uninitialized = true;
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

            if (endpoints != null)
            {
                m_selectedEndpoint = endpoints[0];
                m_securityMode = endpoints[0].SecurityMode;
                m_securityPolicyUri = endpoints[0].SecurityPolicyUri;

                foreach (var endpoint in endpoints)
                {
                    if (endpoint.SecurityMode != MessageSecurityMode.None)
                    {
                        if (serverCertificate == null) throw new ArgumentNullException("serverCertificate");

                        if (serverCertificate.RawData.Length > TcpMessageLimits.MaxCertificateSize)
                        {
                            throw new ArgumentException(
                                Utils.Format("The DER encoded certificate may not be more than {0} bytes.", TcpMessageLimits.MaxCertificateSize),
                                "serverCertificate");
                        }

                        if (clientCertificate != null)
                        {
                            if (clientCertificate.RawData.Length > TcpMessageLimits.MaxCertificateSize)
                            {
                                throw new ArgumentException(
                                    Utils.Format("The DER encoded certificate may not be more than {0} bytes.", TcpMessageLimits.MaxCertificateSize),
                                    "clientCertificate");
                            }
                        }

                        m_selectedEndpoint = endpoint;
                        m_securityMode = endpoint.SecurityMode;
                        m_securityPolicyUri = endpoint.SecurityPolicyUri;
                    }

                    if (new UTF8Encoding().GetByteCount(endpoint.SecurityPolicyUri) > TcpMessageLimits.MaxSecurityPolicyUriSize)
                    {
                        throw new ArgumentException(
                            Utils.Format("UTF-8 form of the security policy URI may not be more than {0} bytes.", TcpMessageLimits.MaxSecurityPolicyUriSize),
                            "securityPolicyUri");
                    }
                }

                m_endpoints = endpoints;
            }

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
                return m_channelId;
            }
        }
        #endregion

        #region Channel State Functions
        /// <summary>
        /// Gets the channel identifier.
        /// </summary>
        /// <value>
        /// The channel identifier.
        /// </value>
        public uint ChannelId
        {
            get { return (uint)m_channelId; }
            set { m_channelId = value; }
        }

        /// <summary>
        /// Gets the sequence number.
        /// </summary>
        /// <value>
        /// The sequence number.
        /// </value>
        public uint SequenceNumber
        {
            get { return (uint)m_sequenceNumber; }
        }

        /// <summary>
        /// The buffer manager for the channel.
        /// </summary>
        public BufferManager BufferManager
        {
            get { return m_bufferManager; }
        }

        /// <summary>
        /// Returns a new token id.
        /// </summary>
        protected uint GetNewTokenId()
        {
            return (uint)Utils.IncrementIdentifier(ref m_lastTokenId);
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

            Utils.Trace("{0}: Channel {1} - Duplicate sequence number: {2} <= {3}", context, this.ChannelId, sequenceNumber, m_remoteSequenceNumber);
            return false;
        }

        private void SaveIntermediateChunk(uint requestId, ArraySegment<byte> chunk)
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

                // m_partialMessageChunks.Release(BufferManager, "SaveIntermediateChunk");
            }

            if (requestId != 0)
            {
                m_partialRequestId = requestId;
                m_partialMessageChunks.Add(chunk);
            }
        }

        private BufferCollection GetSavedChunks(uint requestId, ArraySegment<byte> chunk)
        {
            SaveIntermediateChunk(requestId, chunk);
            BufferCollection savedChunks = m_partialMessageChunks;
            m_partialMessageChunks = null;
            return savedChunks;
        }
        #endregion

        #region IMessageSink Members
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
            // defined by the sub-class. 
        }
        #endregion

        /// <summary>
        /// Handles a receive error.
        /// </summary>
        public virtual void OnReceiveError(TcpMessageSocket source, ServiceResult result)
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
            // defined by the sub-class. 
        }
        #endregion

        #region Outgoing Message Support Functions
        /// <remarks/>
        public ArraySegment<byte> ConstructHelloMessage()
        {
            byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "ConstructHelloMessage");

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

                byte[] endpointUrl = new UTF8Encoding().GetBytes(m_selectedEndpoint.EndpointUrl);

                if (endpointUrl.Length > TcpMessageLimits.MaxEndpointUrlLength)
                {
                    byte[] truncatedUrl = new byte[TcpMessageLimits.MaxEndpointUrlLength];
                    Array.Copy(endpointUrl, truncatedUrl, TcpMessageLimits.MaxEndpointUrlLength);
                    endpointUrl = truncatedUrl;
                }

                encoder.WriteByteString(null, endpointUrl);

                int size = encoder.Close();
                UpdateMessageSize(buffer, 0, size);

                var result = new ArraySegment<byte>(buffer, 0, size);
                buffer = null;
                return result;
            }
            finally
            {
                if (buffer != null)
                {
                    BufferManager.ReturnBuffer(buffer, "ConstructHelloMessage");
                }
            }
        }

        /// <remarks/>
        public ServiceResult ProcessHelloMessage(ArraySegment<byte> buffer)
        {
            using (BinaryDecoder decoder = new BinaryDecoder(buffer.Array, buffer.Offset, buffer.Count, Quotas.MessageContext))
            {
                uint messageType = decoder.ReadUInt32(null);

                if (messageType != TcpMessageType.Hello)
                {
                    return StatusCodes.BadTcpMessageTypeInvalid;
                }

                uint messageSize = decoder.ReadUInt32(null);

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
                        return StatusCodes.BadTcpEndpointUrlInvalid;
                    }

                    byte[] endpointUrl = new byte[length];

                    for (int ii = 0; ii < endpointUrl.Length; ii++)
                    {
                        endpointUrl[ii] = decoder.ReadByte(null);
                    }

                    if (!SetEndpointUrl(new UTF8Encoding().GetString(endpointUrl)))
                    {
                        return StatusCodes.BadTcpEndpointUrlInvalid;
                    }
                }

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

                return StatusCodes.Good;
            }
        }

        /// <remarks/>
        public ArraySegment<byte> ConstructAcknowledgeMessage()
        {
            // send acknowledge.
            byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "ConstructAcknowledgeMessage");

            try
            {
                using (BinaryEncoder encoder = new BinaryEncoder(buffer, 0, SendBufferSize, Quotas.MessageContext))
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

                    var result = new ArraySegment<byte>(buffer, 0, size);
                    buffer = null;
                    return result;
                }
            }
            finally
            {
                if (buffer != null)
                {
                    BufferManager.ReturnBuffer(buffer, "ConstructAcknowledgeMessage");
                }
            }
        }

        /// <remarks/>
        public void ProcessAcknowledgeMessage(ArraySegment<byte> buffer)
        {
            BinaryDecoder decoder = new BinaryDecoder(buffer.Array, buffer.Offset, buffer.Count, Quotas.MessageContext);

            uint messageType = decoder.ReadUInt32(null);
            uint messageSize = decoder.ReadUInt32(null);

            if (messageType != TcpMessageType.Acknowledge)
            {
                if (messageType == TcpMessageType.Error)
                {
                    throw new ServiceResultException(ReadErrorMessageBody(decoder));
                }

                throw new ServiceResultException(StatusCodes.BadTcpMessageTypeInvalid);
            }

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
                throw new ServiceResultException(ServiceResult.Create(StatusCodes.BadTcpNotEnoughResources, "Server receive buffer size is too small ({0} bytes).", ReceiveBufferSize));
            }

            if (SendBufferSize < TcpMessageLimits.MinBufferSize)
            {
                throw new ServiceResultException(ServiceResult.Create(StatusCodes.BadTcpNotEnoughResources, "Server send buffer size is too small ({0} bytes).", SendBufferSize));
            }
        }

        /// <remarks/>
        public ArraySegment<byte> ConstructOpenSecureChannelRequest(bool renew)
        {
            BufferCollection chunksToSend = null;

            try
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
                chunksToSend = WriteAsymmetricMessage(
                    TcpMessageType.Open,
                    GetNewSequenceNumber(),
                    ClientCertificate,
                    ServerCertificate,
                    new ArraySegment<byte>(buffer, 0, buffer.Length));

                // save token.
                m_requestedToken = token;

                var result = chunksToSend[0];
                chunksToSend = null;
                return result;
            }
            finally
            {
                if (chunksToSend != null)
                {
                    chunksToSend.Release(BufferManager, "SendOpenSecureChannelRequest");
                }
            }
        }

        /// <remarks/>
        public uint ProcessOpenSecureChannelRequest(ArraySegment<byte> buffer)
        {
            using (BinaryDecoder decoder = new BinaryDecoder(buffer.Array, buffer.Offset, buffer.Count, Quotas.MessageContext))
            {
                uint messageType = decoder.ReadUInt32(null);

                if (messageType != (uint)(TcpMessageType.Open | TcpMessageType.Final))
                {
                    throw new ServiceResultException(StatusCodes.BadTcpMessageTypeInvalid);
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
                        buffer,
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

                    if (innerException != null && (
                        innerException.StatusCode == StatusCodes.BadCertificateTimeInvalid ||
                        innerException.StatusCode == StatusCodes.BadCertificateIssuerTimeInvalid ||
                        innerException.StatusCode == StatusCodes.BadCertificateHostNameInvalid ||
                        innerException.StatusCode == StatusCodes.BadCertificateUriInvalid ||
                        innerException.StatusCode == StatusCodes.BadCertificateUseNotAllowed ||
                        innerException.StatusCode == StatusCodes.BadCertificateIssuerUseNotAllowed ||
                        innerException.StatusCode == StatusCodes.BadCertificateRevocationUnknown ||
                        innerException.StatusCode == StatusCodes.BadCertificateIssuerRevocationUnknown ||
                        innerException.StatusCode == StatusCodes.BadCertificateRevoked ||
                        innerException.StatusCode == StatusCodes.BadCertificateIssuerRevoked))
                    {
                        throw new ServiceResultException(ServiceResult.Create(innerException, innerException.StatusCode, e.Message));
                    }
                    else
                    {
                        throw new ServiceResultException(ServiceResult.Create(e, StatusCodes.BadSecurityChecksFailed, "Could not verify security on OpenSecureChannel request."));
                    }
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
                        m_clientCertificate = clientCertificate;
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

                    ActivateToken(token);

                    return requestId;
                }
                catch (Exception e)
                {
                    throw new ServiceResultException(ServiceResult.Create(e, StatusCodes.BadTcpInternalError, "Unexpected error processing OpenSecureChannel request."));
                }
                finally
                {
                    if (chunksToProcess != null)
                    {
                        chunksToProcess.Release(BufferManager, "ProcessOpenSecureChannelRequest");
                    }
                }

            }
        }

        /// <remarks/>
        public ArraySegment<byte> ConstructOpenSecureChannelResponse(uint requestId)
        {
            BufferCollection chunksToSend = null;

            try
            {
                OpenSecureChannelResponse response = new OpenSecureChannelResponse();

                response.ResponseHeader.RequestHandle = requestId;
                response.ResponseHeader.Timestamp = DateTime.UtcNow;

                response.SecurityToken.ChannelId = m_currentToken.ChannelId;
                response.SecurityToken.TokenId = m_currentToken.TokenId;
                response.SecurityToken.CreatedAt = m_currentToken.CreatedAt;
                response.SecurityToken.RevisedLifetime = (uint)m_currentToken.Lifetime;
                response.ServerNonce = m_currentToken.ServerNonce;

                byte[] buffer = BinaryEncoder.EncodeMessage(response, Quotas.MessageContext);

                chunksToSend = WriteAsymmetricMessage(
                    TcpMessageType.Open,
                    requestId,
                    ServerCertificate,
                    ClientCertificate,
                    new ArraySegment<byte>(buffer, 0, buffer.Length));

                var result = chunksToSend[0];
                chunksToSend = null;
                return result;
            }
            finally
            {
                if (chunksToSend != null)
                {
                    chunksToSend.Release(BufferManager, "SendOpenSecureChannelRequest");
                }
            }
        }

        /// <remarks/>
        public void ProcessOpenSecureChannelResponse(ArraySegment<byte> buffer)
        {
            using (BinaryDecoder decoder = new BinaryDecoder(buffer.Array, buffer.Offset, buffer.Count, Quotas.MessageContext))
            {
                uint messageType = decoder.ReadUInt32(null);

                if (messageType != (uint)(TcpMessageType.Open | TcpMessageType.Final))
                {
                    if (messageType == TcpMessageType.Error)
                    {
                        decoder.ReadUInt32(null);
                        throw new ServiceResultException(ReadErrorMessageBody(decoder));
                    }

                    throw new ServiceResultException(StatusCodes.BadTcpMessageTypeInvalid);
                }

                // parse the security header.
                uint channelId = 0;
                X509Certificate2 serverCertificate = null;
                uint requestId = 0;
                uint sequenceNumber = 0;
                ArraySegment<byte> messageBody = new ArraySegment<byte>();

                try
                {
                    messageBody = ReadAsymmetricMessage(
                        buffer,
                        ClientCertificate,
                        out channelId,
                        out serverCertificate,
                        out requestId,
                        out sequenceNumber);
                }
                catch (Exception e)
                {
                    BufferManager.ReturnBuffer(messageBody.Array, "ProcessOpenSecureChannelResponse");
                    throw new ServiceResultException(ServiceResult.Create(e, StatusCodes.BadSecurityChecksFailed, "Could not verify security on OpenSecureChannel response."));
                }

                BufferCollection chunksToProcess = null;

                try
                {
                    // verify server certificate.
                    CompareCertificates(ServerCertificate, serverCertificate, true);

                    // verify sequence number.
                    ResetSequenceNumber(sequenceNumber);

                    // get the chunks to process.
                    chunksToProcess = new BufferCollection();
                    chunksToProcess.Add(messageBody);

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

                    ChannelId = m_requestedToken.ChannelId = channelId;
                    ActivateToken(m_requestedToken);
                    m_requestedToken = null;
                }
                finally
                {
                    if (chunksToProcess != null)
                    {
                        chunksToProcess.Release(BufferManager, "ProcessOpenSecureChannelResponse");
                    }
                }
            }
        }

        /// <remarks/>
        public ArraySegment<byte> ConstructCloseSecureChannelRequest(uint requestId)
        {
            // check for valid token.
            ChannelToken currentToken = CurrentToken;

            if (currentToken == null)
            {
                throw new ServiceResultException(StatusCodes.BadSecureChannelClosed);
            }

            BufferCollection buffers = null;

            try
            {
                // check for valid token.
                ChannelToken token = CurrentToken;

                if (token == null)
                {
                    throw new ServiceResultException(StatusCodes.BadSecureChannelClosed);
                }

                CloseSecureChannelRequest request = new CloseSecureChannelRequest();
                request.RequestHeader.Timestamp = DateTime.UtcNow;

                // limits should never be exceeded sending a close message.
                bool limitsExceeded = false;

                // construct the message.
                buffers = WriteSymmetricMessage(
                    TcpMessageType.Close,
                    requestId,
                    currentToken,
                    request,
                    true,
                    out limitsExceeded);

                if (limitsExceeded)
                {
                    throw new ServiceResultException(StatusCodes.BadRequestTooLarge);
                }

                var result = buffers;
                buffers = null;
                return result[0];
            }
            catch (Exception e)
            {
                throw new ServiceResultException(ServiceResult.Create(e, StatusCodes.BadRequestInterrupted, "Could not send request to server."));
            }
            finally
            {
                if (buffers != null)
                {
                    buffers.Release(BufferManager, "ConstructRequest");
                }
            }
        }

        /// <remarks/>
        public bool ProcessCloseSecureChannelRequest(ArraySegment<byte> buffer)
        {
            // validate security on the message.
            ChannelToken token = null;
            uint requestId = 0;
            uint sequenceNumber = 0;

            ArraySegment<byte> messageBody;

            try
            {
                messageBody = ReadSymmetricMessage(buffer, true, out token, out requestId, out sequenceNumber);

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
                var request = (CloseSecureChannelRequest)BinaryDecoder.DecodeMessage(new MemoryStream(messageBody.Array, messageBody.Offset, messageBody.Count), typeof(CloseSecureChannelRequest), Quotas.MessageContext);

                if (request == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadStructureMissing, "Could not parse CloseSecureChannel request body.");
                }
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
            }

            return true;
        }

        /// <remarks/>
        public BufferCollection ConstructRequest(uint requestId, IServiceRequest request)
        {
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
                    requestId,
                    token,
                    request,
                    true,
                    out limitsExceeded);

                if (limitsExceeded)
                {
                    throw new ServiceResultException(StatusCodes.BadRequestTooLarge);
                }

                var result = buffers;
                buffers = null;

                return result;
            }
            catch (Exception e)
            {
                throw new ServiceResultException(ServiceResult.Create(e, StatusCodes.BadRequestInterrupted, "Could not send request to server."));
            }
            finally
            {
                if (buffers != null)
                {
                    buffers.Release(BufferManager, "ConstructRequest");
                }
            }
        }

        /// <remarks/>
        public IServiceRequest ProcessRequest(ArraySegment<byte> buffer, out uint requestId)
        {
            using (BinaryDecoder decoder = new BinaryDecoder(buffer.Array, buffer.Offset, buffer.Count, Quotas.MessageContext))
            {
                uint messageType = decoder.ReadUInt32(null);

                if ((messageType & TcpMessageType.MessageTypeMask) != TcpMessageType.Message)
                {
                    throw new ServiceResultException(StatusCodes.BadTcpMessageTypeInvalid);
                }

                // validate security on the message.
                ChannelToken token = null;
                uint sequenceNumber = 0;

                ArraySegment<byte> messageBody;

                try
                {
                    messageBody = ReadSymmetricMessage(buffer, true, out token, out requestId, out sequenceNumber);

                    // check for replay attacks.
                    if (!VerifySequenceNumber(sequenceNumber, "ProcessRequestMessage"))
                    {
                        throw new ServiceResultException(StatusCodes.BadSequenceNumberInvalid);
                    }

                    if (token == CurrentToken && PreviousToken != null && !PreviousToken.Expired)
                    {
                        PreviousToken.Lifetime = 0;
                    }
                }
                catch (Exception e)
                {
                    throw new ServiceResultException(ServiceResult.Create(e, StatusCodes.BadSecurityChecksFailed, "Could not verify security on incoming request."));
                }

                BufferCollection chunksToProcess = null;

                try
                {
                    // check for an abort.
                    if (TcpMessageType.IsAbort(messageType))
                    {
                        chunksToProcess = GetSavedChunks(requestId, messageBody);
                        return null;
                    }

                    // check if it is necessary to wait for more chunks.
                    if (!TcpMessageType.IsFinal(messageType))
                    {
                        SaveIntermediateChunk(requestId, messageBody);
                        return null;
                    }

                    // Utils.Trace("Channel {0}: ProcessRequestMessage {1}", GroupId, requestId);

                    // get the chunks to process.
                    chunksToProcess = GetSavedChunks(requestId, messageBody);

                    // decode the request.
                    IServiceRequest request = BinaryDecoder.DecodeMessage(new ArraySegmentStream(chunksToProcess), null, Quotas.MessageContext) as IServiceRequest;

                    if (request == null)
                    {
                        throw new ServiceResultException(ServiceResult.Create(StatusCodes.BadStructureMissing, "Could not parse request body."));
                    }

                    // ensure that only discovery requests come through unsecured.
                    if (DiscoveryOnly)
                    {
                        if (!(request is GetEndpointsRequest || request is FindServersRequest))
                        {
                            throw new ServiceResultException(ServiceResult.Create(StatusCodes.BadSecurityPolicyRejected, "Channel can only be used for discovery."));
                        }
                    }

                    return request;
                }
                catch (Exception e)
                {
                    throw new ServiceResultException(ServiceResult.Create(e, StatusCodes.BadTcpInternalError, "Unexpected error processing request."));
                }
            }
        }

        /// <remarks/>
        public BufferCollection ConstructResponse(uint requestId, IServiceResponse response)
        {
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
                    requestId,
                    token,
                    response,
                    false,
                    out limitsExceeded);

                if (limitsExceeded)
                {
                    throw new ServiceResultException(StatusCodes.BadRequestTooLarge);
                }

                var result = buffers;
                buffers = null;
                return result;
            }
            catch (Exception e)
            {
                throw new ServiceResultException(ServiceResult.Create(e, StatusCodes.BadRequestInterrupted, "Could not send request to server."));
            }
            finally
            {
                if (buffers != null)
                {
                    buffers.Release(BufferManager, "ConstructRequest");
                }
            }
        }

        /// <summary>
        /// Processes a response message.
        /// </summary>
        public ServiceResult ProcessError(ArraySegment<byte> buffer)
        {
            using (BinaryDecoder decoder = new BinaryDecoder(buffer.Array, buffer.Offset, buffer.Count, Quotas.MessageContext))
            {
                uint messageType = decoder.ReadUInt32(null);

                if (messageType != TcpMessageType.Error)
                {
                    throw new ServiceResultException(StatusCodes.BadTcpMessageTypeInvalid);
                }

                decoder.ReadUInt32(null);

                return ReadErrorMessageBody(decoder);
            }
        }

        /// <summary>
        /// Processes a response message.
        /// </summary>
        public IServiceResponse ProcessResponse(ArraySegment<byte> buffer, out uint requestId)
        {
            using (BinaryDecoder decoder = new BinaryDecoder(buffer.Array, buffer.Offset, buffer.Count, Quotas.MessageContext))
            {
                uint messageType = decoder.ReadUInt32(null);

                if ((messageType & TcpMessageType.MessageTypeMask) != TcpMessageType.Message)
                {
                    throw new ServiceResultException(StatusCodes.BadTcpMessageTypeInvalid);
                }

                // validate security on the message.
                ChannelToken token = null;
                uint sequenceNumber = 0;

                ArraySegment<byte> messageBody;

                try
                {
                    messageBody = ReadSymmetricMessage(buffer, false, out token, out requestId, out sequenceNumber);
                }
                catch (Exception e)
                {
                    throw new ServiceResultException(ServiceResult.Create(e, StatusCodes.BadSecurityChecksFailed, "Could not verify security on response."));
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
                        chunksToProcess = GetSavedChunks(requestId, messageBody);
                        return null;
                    }

                    // check if it is necessary to wait for more chunks.
                    if (!TcpMessageType.IsFinal(messageType))
                    {
                        SaveIntermediateChunk(requestId, messageBody);
                        return null;
                    }

                    // get the chunks to process.
                    chunksToProcess = GetSavedChunks(requestId, messageBody);

                    // get response.
                    return ParseResponse(chunksToProcess);
                }
                catch (Exception e)
                {
                    throw new ServiceResultException(ServiceResult.Create(e, StatusCodes.BadUnknownResponse, "Unexpected error processing response."));
                }
            }
        }

        /// <remarks/>
        public ArraySegment<byte> ConstructErrorMessage(ServiceResult error)
        {
            byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "ConstructErrorMessage");

            try
            {
                using (BinaryEncoder encoder = new BinaryEncoder(buffer, 0, SendBufferSize, Quotas.MessageContext))
                {
                    encoder.WriteUInt32(null, TcpMessageType.Error);
                    encoder.WriteUInt32(null, 0);

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

                    int size = encoder.Close();
                    UpdateMessageSize(buffer, 0, size);

                    var result = new ArraySegment<byte>(buffer, 0, size);
                    buffer = null;
                    return result;
                }
            }
            finally
            {
                if (buffer != null)
                {
                    BufferManager.ReturnBuffer(buffer, "ConstructErrorMessage");
                }
            }
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

                reason = new UTF8Encoding().GetString(reasonBytes);
            }

            // Utils.Trace("Channel {0}: Read = {1}", GroupId, reason);

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
        public static void UpdateMessageType(byte[] buffer, int offset, uint messageType)
        {
            buffer[offset++] = (byte)((messageType & 0x000000FF));
            buffer[offset++] = (byte)((messageType & 0x0000FF00) >> 8);
            buffer[offset++] = (byte)((messageType & 0x00FF0000) >> 16);
            buffer[offset++] = (byte)((messageType & 0xFF000000) >> 24);
        }

        /// <summary>
        /// Updates the message size stored in the message header.
        /// </summary>
        public static void UpdateMessageSize(byte[] buffer, int offset, int messageSize)
        {
            if (offset >= Int32.MaxValue - 4)
            {
                throw new ArgumentOutOfRangeException("offset");
            }

            offset += 4;

            buffer[offset++] = (byte)((messageSize & 0x000000FF));
            buffer[offset++] = (byte)((messageSize & 0x0000FF00) >> 8);
            buffer[offset++] = (byte)((messageSize & 0x00FF0000) >> 16);
            buffer[offset++] = (byte)((messageSize & 0xFF000000) >> 24);
        }

        private IServiceResponse ParseResponse(BufferCollection chunksToProcess)
        {
            BinaryDecoder decoder = new BinaryDecoder(new ArraySegmentStream(chunksToProcess), Quotas.MessageContext);

            try
            {
                IServiceResponse response = BinaryDecoder.DecodeMessage(new ArraySegmentStream(chunksToProcess), null, Quotas.MessageContext) as IServiceResponse;

                if (response == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadStructureMissing, "Could not parse response body.");
                }

                return response;
            }
            finally
            {
                decoder.Close();
            }
        }
        #endregion

        #region Protected Properties
        /// <summary>
        /// The synchronization object for the channel.
        /// </summary>
        protected object DataLock
        {
            get { return m_lock; }
        }

        /// <summary>
        /// The resource quotas for the channel.
        /// </summary>
        protected ChannelQuotas Quotas
        {
            get { return m_quotas; }
        }

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
        #endregion
    }

    /// <summary>
    /// The possible channel states.
    /// </summary>
    public enum UaTcpChannelState
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
}
