/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Manages the server side of a UA TCP channel.
    /// </summary>
    public partial class UaSCUaBinaryChannel
    {
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

        /// <summary>
        /// The certificate for the server.
        /// </summary>
        internal X509Certificate2 ServerCertificate { get; private set; }

        /// <summary>
        /// The server certificate chain.
        /// </summary>
        protected X509Certificate2Collection ServerCertificateChain { get; set; }

        /// <summary>
        /// The security mode used with the channel.
        /// </summary>
        protected MessageSecurityMode SecurityMode { get; private set; }

        /// <summary>
        /// The security policy used with the channel.
        /// </summary>
        protected string SecurityPolicyUri
        {
            get => SecurityPolicy.Uri;

            private set
            {
                SecurityPolicy = SecurityPolicies.GetInfo(value);
            }
        }

        /// <summary>
        /// The security policy used with the channel.
        /// </summary>
        protected SecurityPolicyInfo SecurityPolicy { get; private set; }

        /// <summary>
        /// Whether the channel is restricted to discovery operations.
        /// </summary>
        protected bool DiscoveryOnly { get; private set; }

        /// <summary>
        /// The certificate for the client.
        /// </summary>
        internal X509Certificate2 ClientCertificate { get; set; }

        /// <summary>
        /// The client certificate chain.
        /// </summary>
        internal X509Certificate2Collection ClientCertificateChain { get; set; }

        /// <summary>
        /// Returns the thumbprint as a uppercase string.
        /// </summary>
        protected static string GetThumbprintString(byte[] thumbprint)
        {
            if (thumbprint == null)
            {
                return null;
            }

            var builder = new StringBuilder(thumbprint.Length * 2);

            for (int ii = 0; ii < thumbprint.Length; ii++)
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}", thumbprint[ii]);
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
        /// <exception cref="ServiceResultException"></exception>
        protected static void CompareCertificates(
            X509Certificate2 expected,
            X509Certificate2 actual,
            bool allowNull)
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
                    expected != null ? expected.Subject : "(null)",
                    expected != null ? expected.Thumbprint : "(null)",
                    actual != null ? actual.Subject : "(null)",
                    actual != null ? actual.Thumbprint : "(null)");
            }
        }

        /// <summary>
        /// Validates the nonce.
        /// </summary>
        protected byte[] CreateNonce(X509Certificate2 certificate)
        {
            switch (SecurityPolicy.CertificateKeyFamily)
            {
                case CertificateKeyFamily.RSA:
                    if (SecurityPolicy.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.RSADH)
                    {
                        m_localNonce = Nonce.CreateNonce(SecurityPolicy);
                        return m_localNonce.Data;
                    }
                    // Basic128Rsa15 is the only RSA based security policy that allows nonces 
                    // with a length less than 32 bytes for compatibility reasons. 
                    bool enforceMinimumLength = !SecurityPolicy.Uri.Equals(
                        SecurityPolicies.Basic128Rsa15,
                        StringComparison.Ordinal);
                    return Nonce.CreateRandomNonceData(
                        SecurityPolicy.SecureChannelNonceLength,
                        enforceMinimumLength);
                case CertificateKeyFamily.ECC:
                    m_localNonce = Nonce.CreateNonce(SecurityPolicy);
                    return m_localNonce.Data;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Validates the nonce.
        /// </summary>
        protected bool ValidateNonce(X509Certificate2 certificate, byte[] nonce)
        {
            // no nonce needed for no security.
            if (SecurityMode == MessageSecurityMode.None)
            {
                return true;
            }

            // check the length.
            if (nonce == null || nonce.Length != SecurityPolicy.SecureChannelNonceLength)
            {
                return false;
            }

            switch (SecurityPolicy.CertificateKeyFamily)
            {
                case CertificateKeyFamily.RSA:
                    if (SecurityPolicy.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.RSADH)
                    {
                        m_remoteNonce = Nonce.CreateNonce(SecurityPolicy, nonce);
                        return true;
                    }

                    // try to catch programming errors by rejecting nonces with all zeros.
                    for (int ii = 0; ii < nonce.Length; ii++)
                    {
                        if (nonce[ii] != 0)
                        {
                            return true;
                        }
                    }
                    break;
                case CertificateKeyFamily.ECC:
                    m_remoteNonce = Nonce.CreateNonce(SecurityPolicy, nonce);
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the plain text block size for key in the specified certificate.
        /// </summary>
        protected int GetPlainTextBlockSize(X509Certificate2 receiverCertificate)
        {
            if (SecurityPolicy.AsymmetricSignatureAlgorithm == AsymmetricSignatureAlgorithm.None ||
                SecurityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
            {
                return 1;
            }

            switch (SecurityPolicy.AsymmetricEncryptionAlgorithm)
            {
                case AsymmetricEncryptionAlgorithm.RsaOaepSha1:
                    return RsaUtils.GetPlainTextBlockSize(
                        receiverCertificate,
                        RsaUtils.Padding.OaepSHA1);
                case AsymmetricEncryptionAlgorithm.RsaOaepSha256:
                    return RsaUtils.GetPlainTextBlockSize(
                        receiverCertificate,
                        RsaUtils.Padding.OaepSHA256);
                case AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1:
                    return RsaUtils.GetPlainTextBlockSize(
                        receiverCertificate,
                        RsaUtils.Padding.Pkcs1);
                default:
                    return 1;
            }
        }

        /// <summary>
        /// Returns the cipher text block size for key in the specified certificate.
        /// </summary>
        protected int GetCipherTextBlockSize(X509Certificate2 receiverCertificate)
        {
            if (SecurityPolicy.AsymmetricSignatureAlgorithm == AsymmetricSignatureAlgorithm.None ||
                SecurityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
            {
                return 1;
            }

            switch (SecurityPolicy.AsymmetricEncryptionAlgorithm)
            {
                case AsymmetricEncryptionAlgorithm.RsaOaepSha1:
                case AsymmetricEncryptionAlgorithm.RsaOaepSha256:
                case AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1:
                    return RsaUtils.GetCipherTextBlockSize(receiverCertificate);
                default:
                    return 1;
            }
        }

        /// <summary>
        /// Calculates the size of the asymmetric security header.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected int GetAsymmetricHeaderSize(
            string securityPolicyUri,
            X509Certificate2 senderCertificate)
        {
            int headerSize = 0;

            headerSize += TcpMessageLimits.BaseHeaderSize;
            headerSize += TcpMessageLimits.StringLengthSize;

            if (securityPolicyUri != null)
            {
                headerSize += Encoding.UTF8.GetByteCount(securityPolicyUri);
            }

            headerSize += TcpMessageLimits.StringLengthSize;
            headerSize += TcpMessageLimits.StringLengthSize;

            if (SecurityMode != MessageSecurityMode.None)
            {
                headerSize += senderCertificate.RawData.Length;
                headerSize += TcpMessageLimits.CertificateThumbprintSize;
            }

            if (headerSize >=
                SendBufferSize -
                TcpMessageLimits.SequenceHeaderSize -
                GetAsymmetricSignatureSize(senderCertificate) -
                1)
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
        /// <exception cref="ServiceResultException"></exception>
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
                headerSize += Encoding.UTF8.GetByteCount(securityPolicyUri);
            }

            headerSize += TcpMessageLimits.StringLengthSize;
            headerSize += TcpMessageLimits.StringLengthSize;

            if (SecurityMode != MessageSecurityMode.None)
            {
                headerSize += senderCertificateSize;
                headerSize += TcpMessageLimits.CertificateThumbprintSize;
            }

            if (headerSize >=
                SendBufferSize -
                TcpMessageLimits.SequenceHeaderSize -
                GetAsymmetricSignatureSize(senderCertificate) -
                1)
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
            switch (SecurityPolicy.AsymmetricSignatureAlgorithm)
            {
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha1:
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha256:
                case AsymmetricSignatureAlgorithm.RsaPssSha256:
                    return RsaUtils.GetSignatureLength(senderCertificate);
                case AsymmetricSignatureAlgorithm.EcdsaSha256:
                case AsymmetricSignatureAlgorithm.EcdsaSha384:
                case AsymmetricSignatureAlgorithm.EcdsaPure25519:
                case AsymmetricSignatureAlgorithm.EcdsaPure448:
                    return CryptoUtils.GetSignatureLength(senderCertificate);
                default:
                    return 0;
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
            WriteAsymmetricMessageHeader(
                encoder,
                messageType,
                secureChannelId,
                securityPolicyUri,
                senderCertificate,
                null,
                receiverCertificate,
                out _);
        }

        /// <summary>
        /// Writes the asymmetric security header to the buffer.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
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
                    int maxSenderCertificateSize = GetMaxSenderCertificateSize(
                        currentCertificate,
                        securityPolicyUri);
                    var senderCertificateList = new List<byte>(currentCertificate.RawData);
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

                    encoder.WriteByteString(null, [.. senderCertificateList]);
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

        private int GetMaxSenderCertificateSize(
            X509Certificate2 senderCertificate,
            string securityPolicyUri)
        {
            int occupiedSize =
                TcpMessageLimits.BaseHeaderSize //base header size
                + TcpMessageLimits.StringLengthSize; //security policy uri length

            if (securityPolicyUri != null)
            {
                occupiedSize += Encoding.UTF8.GetByteCount(securityPolicyUri); //security policy uri size
            }

            occupiedSize += TcpMessageLimits.StringLengthSize; //SenderCertificateLength
            occupiedSize += TcpMessageLimits.StringLengthSize; //ReceiverCertificateThumbprintLength

            occupiedSize += TcpMessageLimits.CertificateThumbprintSize; //ReceiverCertificateThumbprint

            occupiedSize += TcpMessageLimits.SequenceHeaderSize; //SequenceHeader size
            occupiedSize += TcpMessageLimits.MinBodySize; //Minimum body size

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
            byte[] unused = null;
            return WriteAsymmetricMessage(
                messageType,
                requestId,
                senderCertificate,
                null,
                receiverCertificate,
                messageBody,
                null,
                out unused);
        }

        /// <summary>
        /// Sends a OpenSecureChannel request.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        protected BufferCollection WriteAsymmetricMessage(
            uint messageType,
            uint requestId,
            X509Certificate2 senderCertificate,
            X509Certificate2Collection senderCertificateChain,
            X509Certificate2 receiverCertificate,
            ArraySegment<byte> messageBody,
            byte[] oscRequestSignature,
            out byte[] signature)
        {
            signature = null;

            bool success = false;
            var chunksToSend = new BufferCollection();

            byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "WriteAsymmetricMessage");
            BinaryEncoder encoder = null;

            try
            {
                encoder = new BinaryEncoder(buffer, 0, SendBufferSize, Quotas.MessageContext);

                int headerSize = 0;
                if (senderCertificateChain != null && senderCertificateChain.Count > 0)
                {
                    WriteAsymmetricMessageHeader(
                        encoder,
                        messageType | TcpMessageType.Intermediate,
                        ChannelId,
                        SecurityPolicyUri,
                        senderCertificate,
                        senderCertificateChain,
                        receiverCertificate,
                        out int senderCertificateSize);

                    headerSize = GetAsymmetricHeaderSize(
                        SecurityPolicyUri,
                        senderCertificate,
                        senderCertificateSize);
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
                var header = new ArraySegment<byte>(buffer, 0, headerSize);

                // calculate the space available.
                int plainTextBlockSize = GetPlainTextBlockSize(receiverCertificate);
                int cipherTextBlockSize = GetCipherTextBlockSize(receiverCertificate);
                int maxCipherTextSize = SendBufferSize - headerSize;
                int maxCipherBlocks = maxCipherTextSize / cipherTextBlockSize;
                int maxPlainTextSize = maxCipherBlocks * plainTextBlockSize;
                int maxPayloadSize = maxPlainTextSize -
                    signatureSize -
                    1 -
                    TcpMessageLimits.SequenceHeaderSize;

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
                    encoder.WriteRawBytes(
                        messageBody.Array,
                        messageBody.Offset + startOfBytes,
                        payloadSize);

                    // calculate the amount of plain text to encrypt.
                    int plainTextSize = encoder.Position - headerSize + signatureSize;

                    // calculate the padding.
                    int padding = 0;

                    if (SecurityMode != MessageSecurityMode.None)
                    {
                        if (SecurityPolicy.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None &&
                            receiverCertificate.GetRSAPublicKey() != null)
                        {
                            if (X509Utils.GetRSAPublicKeySize(receiverCertificate) <=
                                TcpMessageLimits.KeySizeExtraPadding)
                            {
                                // need to reserve one byte for the padding.
                                plainTextSize++;

                                if (plainTextSize % plainTextBlockSize != 0)
                                {
                                    padding = plainTextBlockSize -
                                        (plainTextSize % plainTextBlockSize);
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
                                    padding = plainTextBlockSize -
                                        (plainTextSize % plainTextBlockSize);
                                }

                                byte paddingSize = (byte)(padding & 0xff);
                                byte extraPaddingByte = (byte)((padding >> 8) & 0xff);

                                encoder.WriteByte(null, paddingSize);
                                for (int ii = 0; ii < padding; ii++)
                                {
                                    encoder.WriteByte(null, paddingSize);
                                }
                                encoder.WriteByte(null, extraPaddingByte);
                            }
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

                    ArraySegment<byte> dataToSign;

                    if (oscRequestSignature != null && SecurityPolicy.SecureChannelEnhancements)
                    {
                        // copy osc request signature if provided before verifying.
                        dataToSign = new ArraySegment<byte>(
                            buffer,
                            0,
                            encoder.Position + oscRequestSignature.Length);

                        Array.Copy(
                            oscRequestSignature,
                            0,
                            buffer,
                            encoder.Position,
                            oscRequestSignature.Length);
                    }
                    else
                    {
                        dataToSign = new ArraySegment<byte>(buffer, 0, encoder.Position);
                    }

                    // write the signature.
                    signature = Sign(dataToSign, senderCertificate);

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
                        throw new InvalidDataException(
                            "Actual message size is not the same as the predicted message size.");
                    }

                    // save chunk.
                    chunksToSend.Add(encryptedBuffer);

                    bytesToWrite -= payloadSize;
                    startOfBytes += payloadSize;

                    // reset the encoder to write the plaintext for the next chunk into the same buffer.
                    if (bytesToWrite > 0)
                    {
                        Utils.SilentDispose(encoder);
                        // ostrm is disposed by the encoder.
                        var ostrm = new MemoryStream(buffer, 0, SendBufferSize);
                        ostrm.Seek(header.Count, SeekOrigin.Current);
                        encoder = new BinaryEncoder(ostrm, Quotas.MessageContext, false);
                    }
                }

                // ensure the buffers don't get clean up on exit.
                success = true;

                return chunksToSend;
            }
            catch (Exception ex)
            {
                throw new ServiceResultException("Could not write async message", ex);
            }
            finally
            {
                Utils.SilentDispose(encoder);

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
        /// <exception cref="ServiceResultException"></exception>
        protected void ReadAsymmetricMessageHeader(
            BinaryDecoder decoder,
            ref X509Certificate2 receiverCertificate,
            out uint secureChannelId,
            out X509Certificate2Collection senderCertificateChain,
            out string securityPolicyUri)
        {
            senderCertificateChain = null;

            _ = decoder.ReadUInt32(null);
            _ = decoder.ReadUInt32(null);

            // decode security header.
            byte[] certificateData;

            byte[] thumbprintData;
            try
            {
                secureChannelId = decoder.ReadUInt32(null);
                securityPolicyUri = decoder.ReadString(
                    null,
                    TcpMessageLimits.MaxSecurityPolicyUriSize);
                certificateData = decoder.ReadByteString(null, TcpMessageLimits.MaxCertificateSize);
                thumbprintData = decoder.ReadByteString(
                    null,
                    TcpMessageLimits.CertificateThumbprintSize);
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
                senderCertificateChain = Utils.ParseCertificateChainBlob(
                    certificateData,
                    Telemetry);

                try
                {
                    string thumbprint =
                        senderCertificateChain[0].Thumbprint
                        ?? throw ServiceResultException.Create(
                            StatusCodes.BadCertificateInvalid,
                            "Invalid certificate thumbprint.");
                }
                catch (Exception e)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadCertificateInvalid,
                        e,
                        "The sender's certificate could not be parsed.");
                }
            }
            else if (securityPolicyUri != SecurityPolicies.None)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadCertificateInvalid,
                    "The sender's certificate was not specified.");
            }

            // verify receiver thumbprint.
            if (thumbprintData != null && thumbprintData.Length > 0)
            {
                bool loadChain = false;
                // TODO: client should use the proider too!
                if (m_serverCertificateTypesProvider != null)
                {
                    receiverCertificate = m_serverCertificateTypesProvider.GetInstanceCertificate(
                        securityPolicyUri);
                    ServerCertificate = receiverCertificate;
                    loadChain = true;
                }

                if (receiverCertificate == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadCertificateInvalid,
                        "The receiver has no matching certificate for the selected profile.");
                }

                if (!receiverCertificate.Thumbprint.Equals(
                        GetThumbprintString(thumbprintData),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadCertificateInvalid,
                        "The receiver's certificate thumbprint is not valid.");
                }

                if (loadChain)
                {
                    ServerCertificateChain = m_serverCertificateTypesProvider?.LoadCertificateChain(
                        receiverCertificate);
                }
            }
            else if (securityPolicyUri != SecurityPolicies.None)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadCertificateInvalid,
                    "The receiver's certificate thumbprint was not specified.");
            }
        }

        /// <summary>
        /// Checks if it is possible to revise the security mode.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected void ReviseSecurityMode(bool firstCall, MessageSecurityMode requestedMode)
        {
            bool supported = false;

            // server may support multiple security modes - check if the one the client used is supported.
            if (firstCall && !DiscoveryOnly)
            {
                foreach (EndpointDescription endpoint in m_endpoints)
                {
                    if (endpoint.SecurityMode == requestedMode)
                    {
                        if (requestedMode == MessageSecurityMode.None ||
                            endpoint.SecurityPolicyUri == SecurityPolicyUri)
                        {
                            SecurityMode = endpoint.SecurityMode;
                            m_selectedEndpoint = endpoint;
                            ServerCertificate = m_serverCertificateTypesProvider
                                .GetInstanceCertificate(
                                    SecurityPolicyUri);
                            ServerCertificateChain = m_serverCertificateTypesProvider
                                .LoadCertificateChain(
                                    ServerCertificate);
                            supported = true;
                            break;
                        }
                    }
                }
            }

            if (!supported)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityModeRejected,
                    "Security mode is not acceptable to the server.");
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

                SecurityMode = endpoint.SecurityMode;
                SecurityPolicyUri = endpoint.SecurityPolicyUri;
                ServerCertificate = m_serverCertificateTypesProvider.GetInstanceCertificate(
                    SecurityPolicyUri);
                ServerCertificateChain = m_serverCertificateTypesProvider
                    .LoadCertificateChainAsync(ServerCertificate)
                    .GetAwaiter()
                    .GetResult();
                m_selectedEndpoint = endpoint;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Processes an OpenSecureChannel request message.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected ArraySegment<byte> ReadAsymmetricMessage(
            ArraySegment<byte> buffer,
            X509Certificate2 receiverCertificate,
            out uint channelId,
            out X509Certificate2 senderCertificate,
            out uint requestId,
            out uint sequenceNumber,
            byte[] oscRequestSignature,
            out byte[] signature)
        {
            signature = null;

            int headerSize;
            using (var decoder = new BinaryDecoder(buffer, Quotas.MessageContext))
            {
                // parse the security header.
                ReadAsymmetricMessageHeader(
                    decoder,
                    ref receiverCertificate,
                    out channelId,
                    out X509Certificate2Collection senderCertificateChain,
                    out string securityPolicyUri);

                if (senderCertificateChain != null && senderCertificateChain.Count > 0)
                {
                    senderCertificate = senderCertificateChain[0];
                }
                else
                {
                    senderCertificate = null;
                }

                // validate the sender certificate.
                if (senderCertificate != null &&
                    Quotas.CertificateValidator != null &&
                    securityPolicyUri != SecurityPolicies.None)
                {
                    if (Quotas.CertificateValidator is CertificateValidator certificateValidator)
                    {
                        certificateValidator.ValidateAsync(senderCertificateChain, default).GetAwaiter().GetResult();
                    }
                    else
                    {
                        Quotas.CertificateValidator.ValidateAsync(senderCertificate, default).GetAwaiter().GetResult();
                    }
                }

                // check if this is the first open secure channel request.
                if (!m_uninitialized)
                {
                    if (securityPolicyUri != SecurityPolicyUri)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadSecurityPolicyRejected,
                            "Cannot change the security policy after creating the channnel.");
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
                            if (endpoint.SecurityPolicyUri == securityPolicyUri ||
                                (
                                    securityPolicyUri == SecurityPolicies.None &&
                                    endpoint.SecurityMode == MessageSecurityMode.None))
                            {
                                SecurityMode = endpoint.SecurityMode;
                                SecurityPolicyUri = securityPolicyUri;
                                DiscoveryOnly = false;
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
                            throw ServiceResultException.Create(
                                StatusCodes.BadSecurityPolicyRejected,
                                "The security policy is not supported.");
                        }

                        SecurityMode = MessageSecurityMode.None;
                        SecurityPolicyUri = SecurityPolicies.None;
                        DiscoveryOnly = true;
                        m_uninitialized = false;
                        m_selectedEndpoint = null;
                    }
                }

                headerSize = decoder.Position;
            }

            // decrypt the body.
            ArraySegment<byte> plainText = Decrypt(
                new ArraySegment<byte>(
                    buffer.Array,
                    buffer.Offset + headerSize,
                    buffer.Count - headerSize),
                new ArraySegment<byte>(buffer.Array, buffer.Offset, headerSize),
                receiverCertificate);

            // extract signature.
            int signatureSize = GetAsymmetricSignatureSize(senderCertificate);

            signature = new byte[signatureSize];

            for (int ii = 0; ii < signatureSize; ii++)
            {
                signature[ii] = plainText.Array[plainText.Offset + plainText.Count - signatureSize + ii];
            }

            ArraySegment<byte> dataToVerify;

            if (oscRequestSignature != null && SecurityPolicy.SecureChannelEnhancements)
            {
                // copy osc request signature if provided before verifying.
                dataToVerify = new ArraySegment<byte>(
                    plainText.Array,
                    plainText.Offset,
                    plainText.Count - signatureSize + oscRequestSignature.Length);

                Array.Copy(
                    oscRequestSignature,
                    dataToVerify.Offset,
                    dataToVerify.Array,
                    dataToVerify.Count - oscRequestSignature.Length,
                    oscRequestSignature.Length);
            }
            else
            {
                dataToVerify = new ArraySegment<byte>(
                    plainText.Array,
                    plainText.Offset,
                    plainText.Count - signatureSize);
            }

            // verify the signature.
            if (!Verify(dataToVerify, signature, senderCertificate))
            {
                m_logger.LogWarning("Could not verify signature on message.");

                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Could not verify the signature on the message.");
            }

            // verify padding.
            int paddingCount = 0;

            if (SecurityMode != MessageSecurityMode.None &&
                SecurityPolicy.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None &&
                receiverCertificate.GetRSAPublicKey() != null)
            {
                int paddingEnd;
                if (X509Utils.GetRSAPublicKeySize(receiverCertificate) > TcpMessageLimits
                    .KeySizeExtraPadding)
                {
                    paddingEnd = plainText.Offset + plainText.Count - signatureSize - 1;
                    paddingCount = plainText.Array[paddingEnd - 1] +
                        (plainText.Array[paddingEnd] * 256);

                    //parse until paddingStart-1; the last one is actually the extrapaddingsize
                    for (int ii = paddingEnd - paddingCount; ii < paddingEnd; ii++)
                    {
                        if (plainText.Array[ii] != plainText.Array[paddingEnd - 1])
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadSecurityChecksFailed,
                                "Could not verify the padding in the message.");
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
                            throw ServiceResultException.Create(
                                StatusCodes.BadSecurityChecksFailed,
                                "Could not verify the padding in the message.");
                        }
                    }
                }

                paddingCount++;
            }

            // decode message.
            using (
                var decoder = new BinaryDecoder(
                    plainText.Array,
                    plainText.Offset + headerSize,
                    plainText.Count - headerSize,
                    Quotas.MessageContext))
            {
                sequenceNumber = decoder.ReadUInt32(null);
                requestId = decoder.ReadUInt32(null);
                headerSize += decoder.Position;
            }

            m_logger.LogInformation("Security Policy: {SecurityPolicyUri}", SecurityPolicyUri);
            m_logger.LogInformation("Sender Certificate {Certificate}", senderCertificate.AsLogSafeString());

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
        protected byte[] Sign(ArraySegment<byte> dataToSign, X509Certificate2 senderCertificate)
        {
            return CryptoUtils.Sign(dataToSign, senderCertificate, SecurityPolicyUri);
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
            return CryptoUtils.Verify(
                dataToVerify,
                signature,
                senderCertificate,
                SecurityPolicyUri);
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
            if (SecurityPolicy.AsymmetricSignatureAlgorithm == AsymmetricSignatureAlgorithm.None ||
                SecurityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
            {
                byte[] encryptedBuffer = BufferManager.TakeBuffer(SendBufferSize, "Encrypt");

                Array.Copy(
                    headerToCopy.Array,
                    headerToCopy.Offset,
                    encryptedBuffer,
                    0,
                    headerToCopy.Count);
                Array.Copy(
                    dataToEncrypt.Array,
                    dataToEncrypt.Offset,
                    encryptedBuffer,
                    headerToCopy.Count,
                    dataToEncrypt.Count);

                return new ArraySegment<byte>(
                    encryptedBuffer,
                    0,
                    dataToEncrypt.Count + headerToCopy.Count);
            }

            switch (SecurityPolicy.AsymmetricEncryptionAlgorithm)
            {
                case AsymmetricEncryptionAlgorithm.RsaOaepSha1:
                    return Rsa_Encrypt(
                        dataToEncrypt,
                        headerToCopy,
                        receiverCertificate,
                        RsaUtils.Padding.OaepSHA1);
                case AsymmetricEncryptionAlgorithm.RsaOaepSha256:
                    return Rsa_Encrypt(
                        dataToEncrypt,
                        headerToCopy,
                        receiverCertificate,
                        RsaUtils.Padding.OaepSHA256);
                default:
                case AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1:
                    return Rsa_Encrypt(
                        dataToEncrypt,
                        headerToCopy,
                        receiverCertificate,
                        RsaUtils.Padding.Pkcs1);
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
            if (SecurityPolicy.AsymmetricSignatureAlgorithm == AsymmetricSignatureAlgorithm.None ||
                SecurityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
            {
                byte[] decryptedBuffer = BufferManager.TakeBuffer(SendBufferSize, "Decrypt");

                Array.Copy(
                    headerToCopy.Array,
                    headerToCopy.Offset,
                    decryptedBuffer,
                    0,
                    headerToCopy.Count);
                Array.Copy(
                    dataToDecrypt.Array,
                    dataToDecrypt.Offset,
                    decryptedBuffer,
                    headerToCopy.Count,
                    dataToDecrypt.Count);

                return new ArraySegment<byte>(
                    decryptedBuffer,
                    0,
                    dataToDecrypt.Count + headerToCopy.Count);
            }

            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Basic256Sha256:
                    return Rsa_Decrypt(
                        dataToDecrypt,
                        headerToCopy,
                        receiverCertificate,
                        RsaUtils.Padding.OaepSHA1);
                default:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                case SecurityPolicies.RSA_DH_AesGcm:
                case SecurityPolicies.RSA_DH_ChaChaPoly:
                    return Rsa_Decrypt(
                        dataToDecrypt,
                        headerToCopy,
                        receiverCertificate,
                        RsaUtils.Padding.OaepSHA256);
                case SecurityPolicies.Basic128Rsa15:
                    return Rsa_Decrypt(
                        dataToDecrypt,
                        headerToCopy,
                        receiverCertificate,
                        RsaUtils.Padding.Pkcs1);
            }
        }

        private readonly EndpointDescriptionCollection m_endpoints;
        private EndpointDescription m_selectedEndpoint;
        private readonly CertificateTypesProvider m_serverCertificateTypesProvider;
        private bool m_uninitialized;
        private Nonce m_localNonce;
        private Nonce m_remoteNonce;
    }
}
