/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Manages the server side of a UA TCP channel.
    /// </summary>
    public partial class UaSCUaBinaryChannel
    {
        #region IUaTcpSecureChannel Members
        /// <summary>
        /// Returns the endpoint description selected by the client.
        /// </summary>
        public EndpointDescription EndpointDescription
        {
            get
            {
                lock (DataLock)
                {
                    return m_selectedEndpoint;
                }
            }

            protected set
            {
                lock (DataLock)
                {
                    m_selectedEndpoint = value;
                }
            }
        }
        #endregion

        #region General Cryptographic Methods and Properties
        /// <summary>
        /// The certificate for the server.
        /// </summary>
        protected X509Certificate2 ServerCertificate => m_serverCertificate;

        /// <summary>
        /// The server certificate chain.
        /// </summary>
        protected X509Certificate2Collection ServerCertificateChain
        {
            get { return m_serverCertificateChain; }
            set { m_serverCertificateChain = value; }
        }

        /// <summary>
        /// The security mode used with the channel.
        /// </summary>
        protected MessageSecurityMode SecurityMode => m_securityMode;

        /// <summary>
        /// The security policy used with the channel.
        /// </summary>
        protected string SecurityPolicyUri => m_securityPolicyUri;

        /// <summary>
        /// Whether the channel is restricted to discovery operations.
        /// </summary>
        protected bool DiscoveryOnly => m_discoveryOnly;

        /// <summary>
        /// The certificate for the client.
        /// </summary>
        protected X509Certificate2 ClientCertificate
        {
            get { return m_clientCertificate; }
            set { m_clientCertificate = value; }
        }

        /// <summary>
        /// The client certificate chain.
        /// </summary>
        internal X509Certificate2Collection ClientCertificateChain
        {
            get { return m_clientCertificateChain; }
            set { m_clientCertificateChain = value; }
        }

        /// <summary>
        /// Creates a new nonce.
        /// </summary>
        protected byte[] CreateNonce()
        {
            uint length = GetNonceLength();
            if (length > 0)
            {
                return Utils.Nonce.CreateNonce(length);
            }
            return null;
        }

        /// <summary>
        /// Returns the thumbprint as a uppercase string.
        /// </summary>
        protected static string GetThumbprintString(byte[] thumbprint)
        {
            if (thumbprint == null)
            {
                return null;
            }

            StringBuilder builder = new StringBuilder(thumbprint.Length * 2);

            for (int ii = 0; ii < thumbprint.Length; ii++)
            {
                builder.AppendFormat("{0:X2}", thumbprint[ii]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Returns the thumbprint as a uppercase string.
        /// </summary>
        protected static byte[] GetThumbprintBytes(string thumbprint)
        {
            if (thumbprint == null)
            {
                return null;
            }

            byte[] bytes = new byte[thumbprint.Length / 2];

            for (int ii = 0; ii < thumbprint.Length - 1; ii += 2)
            {
                bytes[ii / 2] = Convert.ToByte(thumbprint.Substring(ii, 2), 16);
            }

            return bytes;
        }

        /// <summary>
        /// Compares two certificates.
        /// </summary>
        protected static void CompareCertificates(X509Certificate2 expected, X509Certificate2 actual, bool allowNull)
        {
            bool equal = true;

            if (expected == null)
            {
                equal = actual == null;

                // accept everything if no expected certificate and nulls are allowed.
                if (allowNull)
                {
                    equal = true;
                }
            }
            else if (actual == null)
            {
                equal = allowNull;
            }
            else if (!Utils.IsEqual(expected.RawData, actual.RawData))
            {
                equal = false;
            }

            if (!equal)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadCertificateInvalid,
                    "Certificate mismatch. Expecting '{0}'/{1},. Received '{2}'/{3}.",
                    (expected != null) ? expected.Subject : "(null)",
                    (expected != null) ? expected.Thumbprint : "(null)",
                    (actual != null) ? actual.Subject : "(null)",
                    (actual != null) ? actual.Thumbprint : "(null)");
            }
        }
        #endregion

        #region Asymmetric Cryptography Functions
        /// <summary>
        /// Returns the length of the symmetric encryption key.
        /// </summary>
        protected uint GetNonceLength()
        {
            return Utils.Nonce.GetNonceLength(SecurityPolicyUri);
        }

        /// <summary>
        /// Validates the nonce.
        /// </summary>
        protected bool ValidateNonce(byte[] nonce)
        {
            return Utils.Nonce.ValidateNonce(nonce, SecurityMode, SecurityPolicyUri);
        }

        /// <summary>
        /// Returns the plain text block size for key in the specified certificate.
        /// </summary>
        protected int GetPlainTextBlockSize(X509Certificate2 receiverCertificate)
        {
            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                    {
                        return RsaUtils.GetPlainTextBlockSize(receiverCertificate, RsaUtils.Padding.OaepSHA1);
                    }

                case SecurityPolicies.Aes256_Sha256_RsaPss:
                    {
                        return RsaUtils.GetPlainTextBlockSize(receiverCertificate, RsaUtils.Padding.OaepSHA256);
                    }

                case SecurityPolicies.Basic128Rsa15:
                    {
                        return RsaUtils.GetPlainTextBlockSize(receiverCertificate, RsaUtils.Padding.Pkcs1);
                    }

                default:
                case SecurityPolicies.None:
                    {
                        return 1;
                    }
            }
        }

        /// <summary>
        /// Returns the cipher text block size for key in the specified certificate.
        /// </summary>
        protected int GetCipherTextBlockSize(X509Certificate2 receiverCertificate)
        {
            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                    {
                        return RsaUtils.GetCipherTextBlockSize(receiverCertificate, RsaUtils.Padding.OaepSHA1);
                    }

                case SecurityPolicies.Aes256_Sha256_RsaPss:
                    {
                        return RsaUtils.GetCipherTextBlockSize(receiverCertificate, RsaUtils.Padding.OaepSHA256);
                    }

                case SecurityPolicies.Basic128Rsa15:
                    {
                        return RsaUtils.GetCipherTextBlockSize(receiverCertificate, RsaUtils.Padding.Pkcs1);
                    }

                default:
                case SecurityPolicies.None:
                    {
                        return 1;
                    }
            }
        }

        /// <summary>
        /// Calculates the size of the asymmetric security header.
        /// </summary>
        protected int GetAsymmetricHeaderSize(
            string securityPolicyUri,
            X509Certificate2 senderCertificate)
        {
            int headerSize = 0;

            headerSize += TcpMessageLimits.BaseHeaderSize;
            headerSize += TcpMessageLimits.StringLengthSize;

            if (securityPolicyUri != null)
            {
                headerSize += new UTF8Encoding().GetByteCount(securityPolicyUri);
            }

            headerSize += TcpMessageLimits.StringLengthSize;
            headerSize += TcpMessageLimits.StringLengthSize;

            if (SecurityMode != MessageSecurityMode.None)
            {
                headerSize += senderCertificate.RawData.Length;
                headerSize += TcpMessageLimits.CertificateThumbprintSize;
            }

            if (headerSize >= SendBufferSize - TcpMessageLimits.SequenceHeaderSize - GetAsymmetricSignatureSize(senderCertificate) - 1)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInternalError,
                    "AsymmetricSecurityHeader is {0} bytes which is too large for the send buffer size of {1} bytes.",
                    headerSize,
                    SendBufferSize);
            }

            return headerSize;
        }

        /// <summary>
        /// Get asymmetric header size
        /// </summary>
        protected int GetAsymmetricHeaderSize(
            string securityPolicyUri,
            X509Certificate2 senderCertificate,
            int senderCertificateSize)
        {
            int headerSize = 0;

            headerSize += TcpMessageLimits.BaseHeaderSize;
            headerSize += TcpMessageLimits.StringLengthSize;

            if (securityPolicyUri != null)
            {
                headerSize += new UTF8Encoding().GetByteCount(securityPolicyUri);
            }

            headerSize += TcpMessageLimits.StringLengthSize;
            headerSize += TcpMessageLimits.StringLengthSize;

            if (SecurityMode != MessageSecurityMode.None)
            {
                headerSize += senderCertificateSize;
                headerSize += TcpMessageLimits.CertificateThumbprintSize;
            }

            if (headerSize >= SendBufferSize - TcpMessageLimits.SequenceHeaderSize - GetAsymmetricSignatureSize(senderCertificate) - 1)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInternalError,
                    "AsymmetricSecurityHeader is {0} bytes which is too large for the send buffer size of {1} bytes.",
                    headerSize,
                    SendBufferSize);
            }

            return headerSize;
        }

        /// <summary>
        /// Calculates the size of the footer with an asymmetric signature.
        /// </summary>
        protected int GetAsymmetricSignatureSize(X509Certificate2 senderCertificate)
        {
            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                    {
                        return RsaUtils.GetSignatureLength(senderCertificate);
                    }

                default:
                case SecurityPolicies.None:
                    {
                        return 0;
                    }
            }
        }

        /// <summary>
        /// Writes the asymmetric security header to the buffer.
        /// </summary>
        protected void WriteAsymmetricMessageHeader(
            BinaryEncoder encoder,
            uint messageType,
            uint secureChannelId,
            string securityPolicyUri,
            X509Certificate2 senderCertificate,
            X509Certificate2 receiverCertificate)
        {
            int senderCertificateSize = 0;

            WriteAsymmetricMessageHeader(
                encoder,
                messageType,
                secureChannelId,
                securityPolicyUri,
                senderCertificate,
                null,
                receiverCertificate,
                out senderCertificateSize);
        }

        /// <summary>
        /// Writes the asymmetric security header to the buffer.
        /// </summary>
        protected void WriteAsymmetricMessageHeader(
            BinaryEncoder encoder,
            uint messageType,
            uint secureChannelId,
            string securityPolicyUri,
            X509Certificate2 senderCertificate,
            X509Certificate2Collection senderCertificateChain,
            X509Certificate2 receiverCertificate,
            out int senderCertificateSize)
        {
            int start = encoder.Position;
            senderCertificateSize = 0;

            encoder.WriteUInt32(null, messageType);
            encoder.WriteUInt32(null, 0);
            encoder.WriteUInt32(null, secureChannelId);
            encoder.WriteString(null, securityPolicyUri);

            if (SecurityMode != MessageSecurityMode.None)
            {
                if (senderCertificateChain != null && senderCertificateChain.Count > 0)
                {
                    X509Certificate2 currentCertificate = senderCertificateChain[0];
                    int maxSenderCertificateSize = GetMaxSenderCertificateSize(currentCertificate, securityPolicyUri);
                    List<byte> senderCertificateList = new List<byte>(currentCertificate.RawData);
                    senderCertificateSize = currentCertificate.RawData.Length;

                    for (int i = 1; i < senderCertificateChain.Count; i++)
                    {
                        currentCertificate = senderCertificateChain[i];
                        senderCertificateSize += currentCertificate.RawData.Length;

                        if (senderCertificateSize < maxSenderCertificateSize)
                        {
                            senderCertificateList.AddRange(currentCertificate.RawData);
                        }
                        else
                        {
                            senderCertificateSize -= currentCertificate.RawData.Length;
                            break;
                        }
                    }

                    encoder.WriteByteString(null, senderCertificateList.ToArray());
                }
                else
                {
                    encoder.WriteByteString(null, senderCertificate.RawData);
                }

                encoder.WriteByteString(null, GetThumbprintBytes(receiverCertificate.Thumbprint));
            }
            else
            {
                encoder.WriteByteString(null, null);
                encoder.WriteByteString(null, null);
            }

            if (encoder.Position - start > SendBufferSize)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInternalError,
                    "AsymmetricSecurityHeader is {0} bytes which is too large for the send buffer size of {1} bytes.",
                    encoder.Position - start,
                    SendBufferSize);
            }
        }

        private int GetMaxSenderCertificateSize(X509Certificate2 senderCertificate, string securityPolicyUri)
        {
            int occupiedSize = TcpMessageLimits.BaseHeaderSize //base header size
                + TcpMessageLimits.StringLengthSize;           //security policy uri length

            if (securityPolicyUri != null)
            {
                occupiedSize += new UTF8Encoding().GetByteCount(securityPolicyUri);   //security policy uri size
            }

            occupiedSize += TcpMessageLimits.StringLengthSize; //SenderCertificateLength
            occupiedSize += TcpMessageLimits.StringLengthSize; //ReceiverCertificateThumbprintLength

            occupiedSize += TcpMessageLimits.CertificateThumbprintSize; //ReceiverCertificateThumbprint

            occupiedSize += TcpMessageLimits.SequenceHeaderSize; //SequenceHeader size
            occupiedSize += TcpMessageLimits.MinBodySize;        //Minimum body size

            occupiedSize += GetAsymmetricSignatureSize(senderCertificate);

            return SendBufferSize - occupiedSize;
        }

        /// <summary>
        /// Sends a OpenSecureChannel request.
        /// </summary>
        protected BufferCollection WriteAsymmetricMessage(
            uint messageType,
            uint requestId,
            X509Certificate2 senderCertificate,
            X509Certificate2 receiverCertificate,
            ArraySegment<byte> messageBody)
        {
            return WriteAsymmetricMessage(messageType, requestId, senderCertificate, null, receiverCertificate, messageBody);
        }

        /// <summary>
        /// Sends a OpenSecureChannel request.
        /// </summary>
        protected BufferCollection WriteAsymmetricMessage(
            uint messageType,
            uint requestId,
            X509Certificate2 senderCertificate,
            X509Certificate2Collection senderCertificateChain,
            X509Certificate2 receiverCertificate,
            ArraySegment<byte> messageBody)
        {
            bool success = false;
            BufferCollection chunksToSend = new BufferCollection();

            byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "WriteAsymmetricMessage");

            try
            {
                BinaryEncoder encoder = new BinaryEncoder(buffer, 0, SendBufferSize, Quotas.MessageContext);
                int headerSize = 0;

                if (senderCertificateChain != null && senderCertificateChain.Count > 0)
                {
                    int senderCertificateSize = 0;

                    WriteAsymmetricMessageHeader(
                        encoder,
                        messageType | TcpMessageType.Intermediate,
                        ChannelId,
                        SecurityPolicyUri,
                        senderCertificate,
                        senderCertificateChain,
                        receiverCertificate,
                        out senderCertificateSize);

                    headerSize = GetAsymmetricHeaderSize(SecurityPolicyUri, senderCertificate, senderCertificateSize);
                }
                else
                {
                    WriteAsymmetricMessageHeader(
                        encoder,
                        messageType | TcpMessageType.Intermediate,
                        ChannelId,
                        SecurityPolicyUri,
                        senderCertificate,
                        receiverCertificate);

                    headerSize = GetAsymmetricHeaderSize(SecurityPolicyUri, senderCertificate);
                }

                int signatureSize = GetAsymmetricSignatureSize(senderCertificate);

                // save the header.
                ArraySegment<byte> header = new ArraySegment<byte>(buffer, 0, headerSize);

                // calculate the space available.
                int plainTextBlockSize = GetPlainTextBlockSize(receiverCertificate);
                int cipherTextBlockSize = GetCipherTextBlockSize(receiverCertificate);
                int maxCipherTextSize = SendBufferSize - headerSize;
                int maxCipherBlocks = maxCipherTextSize / cipherTextBlockSize;
                int maxPlainTextSize = maxCipherBlocks * plainTextBlockSize;
                int maxPayloadSize = maxPlainTextSize - signatureSize - 1 - TcpMessageLimits.SequenceHeaderSize;

                int bytesToWrite = messageBody.Count;
                int startOfBytes = messageBody.Offset;

                while (bytesToWrite > 0)
                {
                    encoder.WriteUInt32(null, GetNewSequenceNumber());
                    encoder.WriteUInt32(null, requestId);

                    int payloadSize = bytesToWrite;

                    if (payloadSize > maxPayloadSize)
                    {
                        payloadSize = maxPayloadSize;
                    }
                    else
                    {
                        UpdateMessageType(buffer, 0, messageType | TcpMessageType.Final);
                    }

                    // write the message body.
                    encoder.WriteRawBytes(messageBody.Array, messageBody.Offset + startOfBytes, payloadSize);

                    // calculate the amount of plain text to encrypt.
                    int plainTextSize = encoder.Position - headerSize + signatureSize;

                    // calculate the padding.
                    int padding = 0;

                    if (SecurityMode != MessageSecurityMode.None)
                    {
                        if (X509Utils.GetRSAPublicKeySize(receiverCertificate) <= TcpMessageLimits.KeySizeExtraPadding)
                        {
                            // need to reserve one byte for the padding.
                            plainTextSize++;

                            if (plainTextSize % plainTextBlockSize != 0)
                            {
                                padding = plainTextBlockSize - (plainTextSize % plainTextBlockSize);
                            }

                            encoder.WriteByte(null, (byte)padding);
                            for (int ii = 0; ii < padding; ii++)
                            {
                                encoder.WriteByte(null, (byte)padding);
                            }
                        }
                        else
                        {
                            // need to reserve one byte for the padding.
                            plainTextSize++;
                            // need to reserve one byte for the extrapadding.
                            plainTextSize++;

                            if (plainTextSize % plainTextBlockSize != 0)
                            {
                                padding = plainTextBlockSize - (plainTextSize % plainTextBlockSize);
                            }

                            byte paddingSize = (byte)(padding & 0xff);
                            byte extraPaddingByte = (byte)((padding >> 8) & 0xff);

                            encoder.WriteByte(null, paddingSize);
                            for (int ii = 0; ii < padding; ii++)
                            {
                                encoder.WriteByte(null, (byte)paddingSize);
                            }
                            encoder.WriteByte(null, extraPaddingByte);
                        }

                        // update the plaintext size with the padding size.
                        plainTextSize += padding;
                    }

                    // calculate the number of block to encrypt.
                    int encryptedBlocks = plainTextSize / plainTextBlockSize;

                    // calculate the size of the encrypted data.
                    int cipherTextSize = encryptedBlocks * cipherTextBlockSize;

                    // put the message size after encryption into the header.
                    UpdateMessageSize(buffer, 0, cipherTextSize + headerSize);

                    // write the signature.
                    byte[] signature = Sign(new ArraySegment<byte>(buffer, 0, encoder.Position), senderCertificate);

                    if (signature != null)
                    {
                        encoder.WriteRawBytes(signature, 0, signature.Length);
                    }

                    int messageSize = encoder.Close();

                    // encrypt the data.
                    ArraySegment<byte> encryptedBuffer = Encrypt(
                        new ArraySegment<byte>(buffer, headerSize, messageSize - headerSize),
                        header,
                        receiverCertificate);

                    // check for math errors due to code bugs.
                    if (encryptedBuffer.Count != cipherTextSize + headerSize)
                    {
                        throw new InvalidDataException("Actual message size is not the same as the predicted message size.");
                    }

                    // save chunk.
                    chunksToSend.Add(encryptedBuffer);

                    bytesToWrite -= payloadSize;
                    startOfBytes += payloadSize;

                    // reset the encoder to write the plaintext for the next chunk into the same buffer.
                    if (bytesToWrite > 0)
                    {
                        MemoryStream ostrm = new MemoryStream(buffer, 0, SendBufferSize);
                        ostrm.Seek(header.Count, SeekOrigin.Current);
                        encoder = new BinaryEncoder(ostrm, Quotas.MessageContext);
                    }
                }

                // ensure the buffers don't get clean up on exit.
                success = true;
                return chunksToSend;
            }
            catch (Exception ex)
            {
                throw new Exception("Could not write async message", ex);
            }
            finally
            {
                BufferManager.ReturnBuffer(buffer, "WriteAsymmetricMessage");

                if (!success)
                {
                    chunksToSend.Release(BufferManager, "WriteAsymmetricMessage");
                }
            }
        }

        /// <summary>
        /// Reads the asymmetric security header to the buffer.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "messageType"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "messageSize")]
        protected void ReadAsymmetricMessageHeader(
            BinaryDecoder decoder,
            X509Certificate2 receiverCertificate,
            out uint secureChannelId,
            out X509Certificate2Collection senderCertificateChain,
            out string securityPolicyUri)
        {
            senderCertificateChain = null;

            uint messageType = decoder.ReadUInt32(null);
            uint messageSize = decoder.ReadUInt32(null);

            // decode security header.
            byte[] certificateData = null;
            byte[] thumbprintData = null;

            try
            {
                secureChannelId = decoder.ReadUInt32(null);
                securityPolicyUri = decoder.ReadString(null, TcpMessageLimits.MaxSecurityPolicyUriSize);
                certificateData = decoder.ReadByteString(null, TcpMessageLimits.MaxCertificateSize);
                thumbprintData = decoder.ReadByteString(null, TcpMessageLimits.CertificateThumbprintSize);
            }
            catch (Exception e)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    e,
                    "The asymmetric security header could not be parsed.");
            }

            // verify sender certificate chain.
            if (certificateData != null && certificateData.Length > 0)
            {
                senderCertificateChain = Utils.ParseCertificateChainBlob(certificateData);

                try
                {
                    string thumbprint = senderCertificateChain[0].Thumbprint;

                    if (thumbprint == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadCertificateInvalid, "Invalid certificate thumbprint.");
                    }
                }
                catch (Exception e)
                {
                    throw ServiceResultException.Create(StatusCodes.BadCertificateInvalid, e, "The sender's certificate could not be parsed.");
                }
            }
            else
            {
                if (securityPolicyUri != SecurityPolicies.None)
                {
                    throw ServiceResultException.Create(StatusCodes.BadCertificateInvalid, "The sender's certificate was not specified.");
                }
            }

            // verify receiver thumbprint.
            if (thumbprintData != null && thumbprintData.Length > 0)
            {
                if (receiverCertificate.Thumbprint.ToUpperInvariant() != GetThumbprintString(thumbprintData))
                {
                    throw ServiceResultException.Create(StatusCodes.BadCertificateInvalid, "The receiver's certificate thumbprint is not valid.");
                }
            }
            else
            {
                if (securityPolicyUri != SecurityPolicies.None)
                {
                    throw ServiceResultException.Create(StatusCodes.BadCertificateInvalid, "The receiver's certificate thumbprint was not specified.");
                }
            }
        }

        /// <summary>
        /// Checks if it is possible to revise the security mode.
        /// </summary>
        protected void ReviseSecurityMode(bool firstCall, MessageSecurityMode requestedMode)
        {
            bool supported = false;

            // server may support multiple security modes - check if the one the client used is supported.
            if (firstCall && !m_discoveryOnly)
            {
                foreach (EndpointDescription endpoint in m_endpoints)
                {
                    if (endpoint.SecurityMode == requestedMode)
                    {
                        if (requestedMode == MessageSecurityMode.None ||
                            endpoint.SecurityPolicyUri == m_securityPolicyUri)
                        {
                            m_securityMode = endpoint.SecurityMode;
                            m_selectedEndpoint = endpoint;
                            supported = true;
                            break;
                        }
                    }
                }
            }

            if (!supported)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityModeRejected, "Security mode is not acceptable to the server.");
            }
        }

        /// <summary>
        /// Sets to endpoint according to the endpoint url.
        /// </summary>
        protected virtual bool SetEndpointUrl(string endpointUrl)
        {
            Uri url = Utils.ParseUri(endpointUrl);

            if (url == null)
            {
                return false;
            }

            foreach (EndpointDescription endpoint in m_endpoints)
            {
                Uri expectedUrl = Utils.ParseUri(endpoint.EndpointUrl);

                if (expectedUrl == null)
                {
                    continue;
                }

                if (expectedUrl.Scheme != url.Scheme)
                {
                    continue;
                }

                m_securityMode = endpoint.SecurityMode;
                m_securityPolicyUri = endpoint.SecurityPolicyUri;
                m_selectedEndpoint = endpoint;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Processes an OpenSecureChannel request message.
        /// </summary>
        protected ArraySegment<byte> ReadAsymmetricMessage(
            ArraySegment<byte> buffer,
            X509Certificate2 receiverCertificate,
            out uint channelId,
            out X509Certificate2 senderCertificate,
            out uint requestId,
            out uint sequenceNumber)
        {
            BinaryDecoder decoder = new BinaryDecoder(buffer.Array, buffer.Offset, buffer.Count, Quotas.MessageContext);

            string securityPolicyUri = null;
            X509Certificate2Collection senderCertificateChain;

            // parse the security header.
            ReadAsymmetricMessageHeader(
                decoder,
                receiverCertificate,
                out channelId,
                out senderCertificateChain,
                out securityPolicyUri);

            if (senderCertificateChain != null && senderCertificateChain.Count > 0)
            {
                senderCertificate = senderCertificateChain[0];
            }
            else
            {
                senderCertificate = null;
            }

            // validate the sender certificate.
            if (senderCertificate != null && Quotas.CertificateValidator != null && securityPolicyUri != SecurityPolicies.None)
            {
                CertificateValidator certificateValidator = Quotas.CertificateValidator as CertificateValidator;

                if (certificateValidator != null)
                {
                    certificateValidator.Validate(senderCertificateChain);
                }
                else
                {
                    Quotas.CertificateValidator.Validate(senderCertificate);
                }
            }

            // check if this is the first open secure channel request.
            if (!m_uninitialized)
            {
                if (securityPolicyUri != m_securityPolicyUri)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityPolicyRejected, "Cannot change the security policy after creating the channnel.");
                }
            }
            else
            {
                // find a matching endpoint description.
                if (m_endpoints != null)
                {
                    foreach (EndpointDescription endpoint in m_endpoints)
                    {
                        // There may be multiple endpoints with the same securityPolicyUri.
                        // Just choose the first one that matches. This choice will be re-examined
                        // When the OpenSecureChannel request body is processed.
                        if (endpoint.SecurityPolicyUri == securityPolicyUri || (securityPolicyUri == SecurityPolicies.None && endpoint.SecurityMode == MessageSecurityMode.None))
                        {
                            m_securityMode = endpoint.SecurityMode;
                            m_securityPolicyUri = securityPolicyUri;
                            m_discoveryOnly = false;
                            m_uninitialized = false;
                            m_selectedEndpoint = endpoint;

                            // recalculate the key sizes.
                            CalculateSymmetricKeySizes();
                            break;
                        }
                    }
                }

                // allow a discovery only channel with no security if policy not suppported
                if (m_uninitialized)
                {
                    if (securityPolicyUri != SecurityPolicies.None)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadSecurityPolicyRejected, "The security policy is not supported.");
                    }

                    m_securityMode = MessageSecurityMode.None;
                    m_securityPolicyUri = SecurityPolicies.None;
                    m_discoveryOnly = true;
                    m_uninitialized = false;
                    m_selectedEndpoint = null;
                }
            }

            int headerSize = decoder.Position;

            // decrypt the body.
            ArraySegment<byte> plainText = Decrypt(
                new ArraySegment<byte>(buffer.Array, buffer.Offset + headerSize, buffer.Count - headerSize),
                new ArraySegment<byte>(buffer.Array, buffer.Offset, headerSize),
                receiverCertificate);

            // extract signature.
            int signatureSize = GetAsymmetricSignatureSize(senderCertificate);

            byte[] signature = new byte[signatureSize];

            for (int ii = 0; ii < signatureSize; ii++)
            {
                signature[ii] = plainText.Array[plainText.Offset + plainText.Count - signatureSize + ii];
            }

            // verify the signature.
            ArraySegment<byte> dataToVerify = new ArraySegment<byte>(plainText.Array, plainText.Offset, plainText.Count - signatureSize);

            if (!Verify(dataToVerify, signature, senderCertificate))
            {
                Utils.Trace("Could not verify signature on message.");
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Could not verify the signature on the message.");
            }

            // verify padding.
            int paddingCount = 0;

            if (SecurityMode != MessageSecurityMode.None)
            {
                int paddingEnd = -1;
                if (X509Utils.GetRSAPublicKeySize(receiverCertificate) > TcpMessageLimits.KeySizeExtraPadding)
                {
                    paddingEnd = plainText.Offset + plainText.Count - signatureSize - 1;
                    paddingCount = plainText.Array[paddingEnd - 1] + plainText.Array[paddingEnd] * 256;

                    //parse until paddingStart-1; the last one is actually the extrapaddingsize
                    for (int ii = paddingEnd - paddingCount; ii < paddingEnd; ii++)
                    {
                        if (plainText.Array[ii] != plainText.Array[paddingEnd - 1])
                        {
                            throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Could not verify the padding in the message.");
                        }
                    }
                }
                else
                {
                    paddingEnd = plainText.Offset + plainText.Count - signatureSize - 1;
                    paddingCount = plainText.Array[paddingEnd];

                    for (int ii = paddingEnd - paddingCount; ii < paddingEnd; ii++)
                    {
                        if (plainText.Array[ii] != plainText.Array[paddingEnd])
                        {
                            throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Could not verify the padding in the message.");
                        }
                    }
                }

                paddingCount++;
            }

            // decode message.
            decoder = new BinaryDecoder(
                plainText.Array,
                plainText.Offset + headerSize,
                plainText.Count - headerSize,
                Quotas.MessageContext);

            sequenceNumber = decoder.ReadUInt32(null);
            requestId = decoder.ReadUInt32(null);

            headerSize += decoder.Position;
            decoder.Close();

            Utils.Trace("Security Policy: {0}", SecurityPolicyUri);
            Utils.Trace("Sender Certificate: {0}", (senderCertificate != null) ? senderCertificate.Subject : "(none)");

            // return the body.
            return new ArraySegment<byte>(
                plainText.Array,
                plainText.Offset + headerSize,
                plainText.Count - headerSize - signatureSize - paddingCount);
        }

        /// <summary>
        /// Adds an asymmetric signature to the end of the buffer.
        /// </summary>
        /// <remarks>
        /// Start and count specify the block of data to be signed.
        /// The padding and signature must be written to the stream wrapped by the encoder.
        /// </remarks>
        protected byte[] Sign(
            ArraySegment<byte> dataToSign,
            X509Certificate2 senderCertificate)
        {
            switch (SecurityPolicyUri)
            {
                default:
                case SecurityPolicies.None:
                    {
                        return null;
                    }

                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic128Rsa15:
                    {
                        return Rsa_Sign(dataToSign, senderCertificate, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
                    }

                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Basic256Sha256:
                    {
                        return Rsa_Sign(dataToSign, senderCertificate, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    }

                case SecurityPolicies.Aes256_Sha256_RsaPss:
                    {
                        return Rsa_Sign(dataToSign, senderCertificate, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                    }
            }
        }

        /// <summary>
        /// Verifies an asymmetric signature at the end of the buffer.
        /// </summary>
        /// <remarks>
        /// Start and count specify the block of data including the signature and padding.
        /// The current security policy uri and sender certificate specify the size of the signature.
        /// This call also verifies that the padding is correct.
        /// </remarks>
        protected bool Verify(
            ArraySegment<byte> dataToVerify,
            byte[] signature,
            X509Certificate2 senderCertificate)
        {
            // verify signature.
            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.None:
                    {
                        return true;
                    }

                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                    {
                        return Rsa_Verify(dataToVerify, signature, senderCertificate, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
                    }

                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Basic256Sha256:
                    {
                        return Rsa_Verify(dataToVerify, signature, senderCertificate, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    }

                case SecurityPolicies.Aes256_Sha256_RsaPss:
                    {
                        return Rsa_Verify(dataToVerify, signature, senderCertificate, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                    }

                default:
                    {
                        return false;
                    }
            }
        }

        /// <summary>
        /// Encrypts the buffer using asymmetric encryption.
        /// </summary>
        /// <remarks>
        /// Start and count specify the block of data to be encrypted.
        /// The caller must ensure that count is a multiple of the input block size for the current cipher.
        /// The header specifies unencrypted data that must be copied to the output.
        /// </remarks>
        protected ArraySegment<byte> Encrypt(
            ArraySegment<byte> dataToEncrypt,
            ArraySegment<byte> headerToCopy,
            X509Certificate2 receiverCertificate)
        {
            switch (SecurityPolicyUri)
            {
                default:
                case SecurityPolicies.None:
                    {
                        byte[] encryptedBuffer = BufferManager.TakeBuffer(SendBufferSize, "Encrypt");

                        Array.Copy(headerToCopy.Array, headerToCopy.Offset, encryptedBuffer, 0, headerToCopy.Count);
                        Array.Copy(dataToEncrypt.Array, dataToEncrypt.Offset, encryptedBuffer, headerToCopy.Count, dataToEncrypt.Count);

                        return new ArraySegment<byte>(encryptedBuffer, 0, dataToEncrypt.Count + headerToCopy.Count);
                    }

                case SecurityPolicies.Basic256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Basic256Sha256:
                    {
                        return Rsa_Encrypt(dataToEncrypt, headerToCopy, receiverCertificate, RsaUtils.Padding.OaepSHA1);
                    }

                case SecurityPolicies.Aes256_Sha256_RsaPss:
                    {
                        return Rsa_Encrypt(dataToEncrypt, headerToCopy, receiverCertificate, RsaUtils.Padding.OaepSHA256);
                    }

                case SecurityPolicies.Basic128Rsa15:
                    {
                        return Rsa_Encrypt(dataToEncrypt, headerToCopy, receiverCertificate, RsaUtils.Padding.Pkcs1);
                    }
            }
        }

        /// <summary>
        /// Decrypts the buffer using asymmetric encryption.
        /// </summary>
        /// <remarks>
        /// Start and count specify the block of data to be decrypted.
        /// The header specifies unencrypted data that must be copied to the output.
        /// </remarks>
        protected ArraySegment<byte> Decrypt(
            ArraySegment<byte> dataToDecrypt,
            ArraySegment<byte> headerToCopy,
            X509Certificate2 receiverCertificate)
        {
            switch (SecurityPolicyUri)
            {
                default:
                case SecurityPolicies.None:
                    {
                        byte[] decryptedBuffer = BufferManager.TakeBuffer(SendBufferSize, "Decrypt");

                        Array.Copy(headerToCopy.Array, headerToCopy.Offset, decryptedBuffer, 0, headerToCopy.Count);
                        Array.Copy(dataToDecrypt.Array, dataToDecrypt.Offset, decryptedBuffer, headerToCopy.Count, dataToDecrypt.Count);

                        return new ArraySegment<byte>(decryptedBuffer, 0, dataToDecrypt.Count + headerToCopy.Count);
                    }

                case SecurityPolicies.Basic256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Basic256Sha256:
                    {
                        return Rsa_Decrypt(dataToDecrypt, headerToCopy, receiverCertificate, RsaUtils.Padding.OaepSHA1);
                    }

                case SecurityPolicies.Aes256_Sha256_RsaPss:
                    {
                        return Rsa_Decrypt(dataToDecrypt, headerToCopy, receiverCertificate, RsaUtils.Padding.OaepSHA256);
                    }

                case SecurityPolicies.Basic128Rsa15:
                    {
                        return Rsa_Decrypt(dataToDecrypt, headerToCopy, receiverCertificate, RsaUtils.Padding.Pkcs1);
                    }
            }
        }
        #endregion

        #region Private Fields
        private EndpointDescriptionCollection m_endpoints;
        private MessageSecurityMode m_securityMode;
        private string m_securityPolicyUri;
        private bool m_discoveryOnly;
        private EndpointDescription m_selectedEndpoint;
        private X509Certificate2 m_serverCertificate;
        private X509Certificate2Collection m_serverCertificateChain;
        private X509Certificate2 m_clientCertificate;
        private X509Certificate2Collection m_clientCertificateChain;
        private bool m_uninitialized;
        #endregion
    }
}
