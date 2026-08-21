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
    public partial class UaSCUaBinaryChannel
    {
        /// <summary>
        /// Returns the endpoint description selected by the client.
        /// </summary>
        /// <remarks>
        /// Deliberately lock-free. This publishes a single reference, so a
        /// volatile read and write give the same visibility the gate did, and
        /// taking the gate here made every gate-holding caller that reads the
        /// property a re-entrant one — <c>ConnectAsync</c> reads it four times
        /// while holding it.
        /// </remarks>
        public EndpointDescription? EndpointDescription
        {
            get => Volatile.Read(ref m_selectedEndpoint);
            protected set => Volatile.Write(ref m_selectedEndpoint, value);
        }

        /// <summary>
        /// The certificate for the server.
        /// </summary>
        internal Certificate? ServerCertificate { get; private set; }

        /// <summary>
        /// The server certificate chain.
        /// </summary>
        protected CertificateCollection? ServerCertificateChain { get; set; }

        /// <summary>
        /// The security mode used with the channel.
        /// </summary>
        protected MessageSecurityMode SecurityMode { get; private set; }

        /// <summary>
        /// The security policy used with the channel.
        /// </summary>
        protected string SecurityPolicyUri
        {
            get => SecurityPolicy?.Uri ?? string.Empty;
            private set => SecurityPolicy = SecurityPolicies.Default.GetInfo(value);
        }

        /// <summary>
        /// The security policy used with the channel.
        /// </summary>
        protected SecurityPolicyInfo? SecurityPolicy { get; private set; }

        /// <summary>
        /// Whether the channel is restricted to discovery operations.
        /// </summary>
        protected bool DiscoveryOnly { get; private set; }

        /// <summary>
        /// The certificate for the client.
        /// </summary>
        internal Certificate? ClientCertificate { get; set; }

        /// <summary>
        /// The client certificate chain.
        /// </summary>
        internal CertificateCollection? ClientCertificateChain { get; set; }

        /// <summary>
        /// Builds a new owned collection holding the entry's certificate
        /// followed by its issuer chain (<c>[leaf, ...issuers]</c>) for wire
        /// transmission. The caller owns and must dispose the result.
        /// </summary>
        private static CertificateCollection BuildServerCertificateChain(CertificateEntry entry)
        {
            var chain = new CertificateCollection { entry.Certificate };
            foreach (Certificate issuer in entry.IssuerChain)
            {
                chain.Add(issuer);
            }
            return chain;
        }

        /// <summary>
        /// Returns the thumbprint as a uppercase string.
        /// </summary>
        protected static string GetThumbprintString(ByteString thumbprint)
        {
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
        protected static byte[]? GetThumbprintBytes(string? thumbprint)
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
            Certificate? expected,
            Certificate? actual,
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
        protected byte[]? CreateNonce(Certificate? certificate)
        {
            SecurityPolicyInfo? securityPolicy = SecurityPolicy;
            if (securityPolicy == null)
            {
                return null;
            }

            switch (securityPolicy.CertificateKeyFamily)
            {
                case CertificateKeyFamily.RSA:
                    if (securityPolicy.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.RSADH)
                    {
                        m_localNonce = Nonce.CreateNonce(securityPolicy);
                        return m_localNonce!.Data;
                    }
                    // Basic128Rsa15 is the only RSA based security policy that allows nonces
                    // with a length less than 32 bytes for compatibility reasons.
                    bool enforceMinimumLength = !securityPolicy.Uri.Equals(
                        SecurityPolicies.Basic128Rsa15,
                        StringComparison.Ordinal);
                    return Nonce.CreateRandomNonceData(
                        securityPolicy.SecureChannelNonceLength,
                        enforceMinimumLength);
                case CertificateKeyFamily.ECC:
                    m_localNonce = Nonce.CreateNonce(securityPolicy);
                    return m_localNonce!.Data;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Validates the nonce.
        /// </summary>
        protected bool ValidateNonce(Certificate? certificate, byte[] nonce)
        {
            // no nonce needed for no security.
            if (SecurityMode == MessageSecurityMode.None)
            {
                return true;
            }

            SecurityPolicyInfo? securityPolicy = SecurityPolicy;
            if (securityPolicy == null)
            {
                return false;
            }

            // check the length.
            if (nonce == null || nonce.Length != securityPolicy.SecureChannelNonceLength)
            {
                return false;
            }

            switch (securityPolicy.CertificateKeyFamily)
            {
                case CertificateKeyFamily.RSA:
                    if (securityPolicy.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.RSADH)
                    {
                        m_remoteNonce = Nonce.CreateNonce(securityPolicy, nonce);
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
                    m_remoteNonce = Nonce.CreateNonce(securityPolicy, nonce);
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the plain text block size for key in the specified certificate.
        /// </summary>
        protected int GetPlainTextBlockSize(Certificate? receiverCertificate)
        {
            SecurityPolicyInfo? securityPolicy = SecurityPolicy;
            if (securityPolicy == null)
            {
                return 1;
            }

            if (securityPolicy.AsymmetricSignatureAlgorithm == AsymmetricSignatureAlgorithm.None ||
                securityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
            {
                return 1;
            }

            switch (securityPolicy.AsymmetricEncryptionAlgorithm)
            {
                case AsymmetricEncryptionAlgorithm.RsaOaepSha1:
                    return RsaUtils.GetPlainTextBlockSize(
                        receiverCertificate!,
                        RsaUtils.Padding.OaepSHA1);
                case AsymmetricEncryptionAlgorithm.RsaOaepSha256:
                    return RsaUtils.GetPlainTextBlockSize(
                        receiverCertificate!,
                        RsaUtils.Padding.OaepSHA256);
                case AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1:
                    return RsaUtils.GetPlainTextBlockSize(
                        receiverCertificate!,
                        RsaUtils.Padding.Pkcs1);
                default:
                    return 1;
            }
        }

        /// <summary>
        /// Returns the cipher text block size for key in the specified certificate.
        /// </summary>
        protected int GetCipherTextBlockSize(Certificate? receiverCertificate)
        {
            SecurityPolicyInfo? securityPolicy = SecurityPolicy;
            if (securityPolicy == null)
            {
                return 1;
            }

            if (securityPolicy.AsymmetricSignatureAlgorithm == AsymmetricSignatureAlgorithm.None ||
                securityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
            {
                return 1;
            }

            switch (securityPolicy.AsymmetricEncryptionAlgorithm)
            {
                case AsymmetricEncryptionAlgorithm.RsaOaepSha1:
                case AsymmetricEncryptionAlgorithm.RsaOaepSha256:
                case AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1:
                    return RsaUtils.GetCipherTextBlockSize(receiverCertificate!);
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
            Certificate? senderCertificate)
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
                headerSize += senderCertificate?.RawData.Length ?? 0;
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
            Certificate? senderCertificate,
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
        protected int GetAsymmetricSignatureSize(Certificate? senderCertificate)
        {
            SecurityPolicyInfo? securityPolicy = SecurityPolicy;
            if (securityPolicy == null)
            {
                return 0;
            }

            switch (securityPolicy.AsymmetricSignatureAlgorithm)
            {
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha1:
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha256:
                case AsymmetricSignatureAlgorithm.RsaPssSha256:
                    return RsaUtils.GetSignatureLength(senderCertificate!);
                case AsymmetricSignatureAlgorithm.EcdsaSha256:
                case AsymmetricSignatureAlgorithm.EcdsaSha384:
                case AsymmetricSignatureAlgorithm.EcdsaPure25519:
                case AsymmetricSignatureAlgorithm.EcdsaPure448:
                    return CryptoUtils.GetSignatureLength(senderCertificate!);
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
            Certificate? senderCertificate,
            Certificate? receiverCertificate)
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
            Certificate? senderCertificate,
            CertificateCollection? senderCertificateChain,
            Certificate? receiverCertificate,
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
                    Certificate currentCertificate = senderCertificateChain[0];
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

                    encoder.WriteByteString(null, senderCertificateList.ToByteString());
                }
                else
                {
                    encoder.WriteByteString(null, senderCertificate?.RawData.ToByteString() ?? default);
                }

                encoder.WriteByteString(null, GetThumbprintBytes(receiverCertificate?.Thumbprint));
            }
            else
            {
                encoder.WriteByteString(null, (byte[]?)null);
                encoder.WriteByteString(null, (byte[]?)null);
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
            Certificate senderCertificate,
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
        /// <param name="messageType">The UA TCP message type (for example, Open or OpenFinal).</param>
        /// <param name="requestId">The request identifier used in the sequence header.</param>
        /// <param name="senderCertificate">The certificate used to sign the asymmetric message.</param>
        /// <param name="senderCertificateChain">The optional sender certificate chain to include in the message header.</param>
        /// <param name="receiverCertificate">The receiver certificate used for asymmetric encryption.</param>
        /// <param name="messageBody">The encoded message body to send.</param>
        /// <param name="oscRequestSignature">The signature from the OpenSecureChannel request.</param>
        /// <param name="signature">Returns the signature generated for the message being written.</param>
        /// <param name="sendTicket">Returns the FIFO send gate ticket for the secured chunks.</param>
        /// <exception cref="InvalidDataException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        /// <remarks>
        /// Not part of the surface a channel outside this assembly can use:
        /// <see cref="WriteAsymmetricMessageAsync"/> is, and it does not occupy a
        /// thread while a private key served over a network signs the message.
        /// This remains only for the fault and reconnect paths inside this
        /// assembly, which are reached from call sites that cannot await.
        /// </remarks>
        private protected BufferCollection WriteAsymmetricMessage(
            uint messageType,
            uint requestId,
            Certificate? senderCertificate,
            CertificateCollection? senderCertificateChain,
            Certificate? receiverCertificate,
            ArraySegment<byte> messageBody,
            byte[]? oscRequestSignature,
            out byte[] signature,
            out SendGateTicket sendTicket)
        {
            signature = null!;
            sendTicket = null!;

            bool success = false;
            var chunksToSend = new BufferCollection();

            byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "WriteAsymmetricMessage");
            BinaryEncoder? encoder = null;

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
                    sendTicket ??= TakeSendTicket();
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
                        messageBody.GetArray(),
                        messageBody.Offset + startOfBytes,
                        payloadSize);

                    // calculate the amount of plain text to encrypt.
                    int plainTextSize = encoder.Position - headerSize + signatureSize;

                    // calculate the padding.
                    int padding = 0;

                    if (SecurityMode != MessageSecurityMode.None)
                    {
                        if (SecurityPolicy!.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None &&
                            receiverCertificate!.GetRSAPublicKey() != null)
                        {
                            if (X509Utils.GetRSAPublicKeySize(receiverCertificate!) <=
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

                    if (oscRequestSignature != null && SecurityPolicy!.SecureChannelEnhancements)
                    {
                        // copy OpenSecureChannel request signature if provided before verifying.
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
                    signature = Sign(dataToSign, senderCertificate!);

                    if (signature != null)
                    {
                        encoder.WriteRawBytes(signature, 0, signature.Length);
                    }

                    int messageSize = encoder.Close();

                    // encrypt the data.
                    ArraySegment<byte> encryptedBuffer = Encrypt(
                        new ArraySegment<byte>(buffer, headerSize, messageSize - headerSize),
                        header,
                        receiverCertificate!);

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
                        encoder.Dispose();
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
                encoder?.Dispose();

                BufferManager.ReturnBuffer(buffer, "WriteAsymmetricMessage");

                if (!success)
                {
                    if (sendTicket != null)
                    {
                        ReleaseSendTicket(sendTicket);
                    }

                    chunksToSend.Release(BufferManager, "WriteAsymmetricMessage");
                }
            }
        }

        /// <summary>
        /// Sends an OpenSecureChannel message, without occupying the calling
        /// thread when the private key is served over a network.
        /// </summary>
        /// <param name="messageType">The UA TCP message type (for example, Open or OpenFinal).</param>
        /// <param name="requestId">The request identifier used in the sequence header.</param>
        /// <param name="senderCertificate">The certificate used to sign the asymmetric message.</param>
        /// <param name="senderCertificateChain">The optional sender certificate chain to include in the message header.</param>
        /// <param name="receiverCertificate">The receiver certificate used for asymmetric encryption.</param>
        /// <param name="messageBody">The encoded message body to send.</param>
        /// <param name="oscRequestSignature">The signature from the OpenSecureChannel request.</param>
        /// <param name="ct">Cancels the operation.</param>
        /// <returns>The chunks to send and the signature that was generated.</returns>
        /// <exception cref="InvalidDataException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        /// <remarks>
        /// Only the signature is awaited, and only when the private key declares
        /// <see cref="IAsyncRsaKey"/> or <see cref="IAsyncEcdsaKey"/>. The
        /// encryption uses the receiver's public key, which is always local.
        /// </remarks>
        protected async ValueTask<AsymmetricWriteResult> WriteAsymmetricMessageAsync(
            uint messageType,
            uint requestId,
            Certificate? senderCertificate,
            CertificateCollection? senderCertificateChain,
            Certificate? receiverCertificate,
            ArraySegment<byte> messageBody,
            byte[]? oscRequestSignature,
            CancellationToken ct)
        {
            byte[] signature = null!;

            bool success = false;
            var chunksToSend = new BufferCollection();

            byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "WriteAsymmetricMessage", ct);
            BinaryEncoder? encoder = null;

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
                        messageBody.GetArray(),
                        messageBody.Offset + startOfBytes,
                        payloadSize);

                    // calculate the amount of plain text to encrypt.
                    int plainTextSize = encoder.Position - headerSize + signatureSize;

                    // calculate the padding.
                    int padding = 0;

                    if (SecurityMode != MessageSecurityMode.None)
                    {
                        if (SecurityPolicy!.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None &&
                            receiverCertificate!.GetRSAPublicKey() != null)
                        {
                            if (X509Utils.GetRSAPublicKeySize(receiverCertificate!) <=
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

                    if (oscRequestSignature != null && SecurityPolicy!.SecureChannelEnhancements)
                    {
                        // copy OpenSecureChannel request signature if provided before verifying.
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
                    signature = await SignAsync(dataToSign, senderCertificate!, ct).ConfigureAwait(false);

                    if (signature != null)
                    {
                        encoder.WriteRawBytes(signature, 0, signature.Length);
                    }

                    int messageSize = encoder.Close();

                    // encrypt the data.
                    ArraySegment<byte> encryptedBuffer = Encrypt(
                        new ArraySegment<byte>(buffer, headerSize, messageSize - headerSize),
                        header,
                        receiverCertificate!);

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
                        encoder.Dispose();
                        // ostrm is disposed by the encoder.
                        var ostrm = new MemoryStream(buffer, 0, SendBufferSize);
                        ostrm.Seek(header.Count, SeekOrigin.Current);
                        encoder = new BinaryEncoder(ostrm, Quotas.MessageContext, false);
                    }
                }

                // ensure the buffers don't get clean up on exit.
                success = true;

                return new AsymmetricWriteResult(chunksToSend, signature!);
            }
            catch (Exception ex)
            {
                throw new ServiceResultException("Could not write async message", ex);
            }
            finally
            {
                encoder?.Dispose();

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
            ref Certificate? receiverCertificate,
            out uint secureChannelId,
            out CertificateCollection? senderCertificateChain,
            out string securityPolicyUri)
        {
            senderCertificateChain = null;

            _ = decoder.ReadUInt32(null);
            _ = decoder.ReadUInt32(null);

            // decode security header.
            ByteString certificateData;
            ByteString thumbprintData;
            try
            {
                secureChannelId = decoder.ReadUInt32(null);
                securityPolicyUri = decoder.ReadString(
                    null,
                    TcpMessageLimits.MaxSecurityPolicyUriSize) ??
                    SecurityPolicies.None;
                certificateData = decoder.ReadByteString(
                    TcpMessageLimits.MaxCertificateSize);
                thumbprintData = decoder.ReadByteString(
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
            if (certificateData.Length > 0)
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
            if (thumbprintData.Length > 0)
            {
                // TODO: client should use the proider too!
                if (m_serverCertificates != null)
                {
                    // Replace the channel-owned instance certificate (and its
                    // issuer chain) with independent handles on the registry's
                    // current entry.
                    using (CertificateEntry? receiverEntry =
                        m_serverCertificates.AcquireApplicationCertificateBySecurityPolicy(securityPolicyUri))
                    {
                        ServerCertificate?.Dispose();
                        ServerCertificate = receiverEntry?.Certificate.AddRef();
                        ServerCertificateChain?.Dispose();
                        ServerCertificateChain = receiverEntry == null
                            ? null
                            : BuildServerCertificateChain(receiverEntry);
                    }
                    receiverCertificate = ServerCertificate;
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
                            Volatile.Write(ref m_selectedEndpoint, endpoint);
                            using (CertificateEntry? instanceEntry =
                                m_serverCertificates!
                                    .AcquireApplicationCertificateBySecurityPolicy(SecurityPolicyUri))
                            {
                                ServerCertificate?.Dispose();
                                ServerCertificate = instanceEntry?.Certificate.AddRef();
                                ServerCertificateChain?.Dispose();
                                ServerCertificateChain = instanceEntry == null
                                    ? null
                                    : BuildServerCertificateChain(instanceEntry);
                            }
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
            Uri? url = Utils.ParseUri(endpointUrl);

            if (url == null)
            {
                return false;
            }

            foreach (EndpointDescription endpoint in m_endpoints)
            {
                Uri? expectedUrl = Utils.ParseUri(endpoint.EndpointUrl);

                if (expectedUrl == null)
                {
                    continue;
                }

                if (expectedUrl.Scheme != url.Scheme)
                {
                    continue;
                }

                SecurityMode = endpoint.SecurityMode;
                SecurityPolicyUri = endpoint.SecurityPolicyUri!;
                using (CertificateEntry? instanceEntry =
                    m_serverCertificates!.AcquireApplicationCertificateBySecurityPolicy(
                        SecurityPolicyUri!))
                {
                    ServerCertificate?.Dispose();
                    ServerCertificate = instanceEntry?.Certificate.AddRef();
                    ServerCertificateChain?.Dispose();
                    ServerCertificateChain = instanceEntry == null
                        ? null
                        : BuildServerCertificateChain(instanceEntry);
                }
                Volatile.Write(ref m_selectedEndpoint, endpoint);
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
            Certificate? receiverCertificate,
            out uint channelId,
            out Certificate? senderCertificate,
            out uint requestId,
            out uint sequenceNumber,
            byte[]? oscRequestSignature,
            out byte[] signature)
        {
            int headerSize;
            using (var decoder = new BinaryDecoder(buffer, Quotas.MessageContext))
            {
                CertificateCollection? senderCertificateChain = ReadAsymmetricMessageSender(
                    decoder,
                    ref receiverCertificate,
                    out channelId,
                    out senderCertificate,
                    out string securityPolicyUri);

                using (senderCertificateChain)
                {
                    // validate the sender certificate.
                    if (senderCertificate != null &&
                        Quotas.CertificateValidator != null &&
                        securityPolicyUri != SecurityPolicies.None)
                    {
#pragma warning disable CA2025 // Do not pass 'IDisposable' instances into unawaited tasks
                        CertificateValidationResult validationResult = Quotas.CertificateValidator
                            .ValidateAsync(senderCertificateChain!, ct: default)
                            .GetAwaiter()
                            .GetResult();
#pragma warning restore CA2025 // Do not pass 'IDisposable' instances into unawaited tasks
                        if (!validationResult.IsValid)
                        {
                            throw new ServiceResultException(validationResult.StatusCode);
                        }
                    }
                }

                SelectEndpointForAsymmetricMessage(securityPolicyUri);

                headerSize = decoder.Position;
            }

            // decrypt the body.
            ArraySegment<byte> plainText = Decrypt(
                new ArraySegment<byte>(
                    buffer.GetArray(),
                    buffer.Offset + headerSize,
                    buffer.Count - headerSize),
                new ArraySegment<byte>(buffer.GetArray(), buffer.Offset, headerSize),
                receiverCertificate!);

            return FinishReadAsymmetricMessage(
                plainText,
                headerSize,
                receiverCertificate,
                senderCertificate,
                oscRequestSignature,
                out requestId,
                out sequenceNumber,
                out signature);
        }

        /// <summary>
        /// Processes an OpenSecureChannel request message, without occupying the
        /// calling thread when the private key is served over a network.
        /// </summary>
        /// <param name="buffer">The message chunk.</param>
        /// <param name="receiverCertificate">
        /// The certificate whose key decrypts the message.
        /// </param>
        /// <param name="oscRequestSignature">
        /// The signature from the OpenSecureChannel request, when the security
        /// policy binds it.
        /// </param>
        /// <param name="onSenderCertificateParsed">
        /// Invoked with the sender's certificate as soon as it has been parsed,
        /// before anything that can reject the message. Ownership transfers to
        /// the callback, which must dispose it.
        /// </param>
        /// <param name="ct">Cancels the operation.</param>
        /// <returns>The message body and everything parsed alongside it.</returns>
        /// <exception cref="ServiceResultException"></exception>
        /// <remarks>
        /// This performs the same steps as
        /// <see cref="ReadAsymmetricMessage"/> and shares its header and body
        /// handling; only the certificate validation and the decryption are
        /// awaited. With a software key neither suspends.
        /// <para>
        /// The callback exists because this returns its results rather than
        /// writing them to <c>out</c> parameters, which an asynchronous method
        /// cannot have. An <c>out</c> parameter reaches the caller even when the
        /// method goes on to throw; a return value does not. Without it the
        /// certificate of a *rejected* sender would never reach the caller — so
        /// it would neither be disposed nor reported to the audit, which is
        /// exactly the case the audit exists for.
        /// </para>
        /// </remarks>
        protected async ValueTask<AsymmetricMessage> ReadAsymmetricMessageAsync(
            ArraySegment<byte> buffer,
            Certificate? receiverCertificate,
            byte[]? oscRequestSignature,
            Action<Certificate?>? onSenderCertificateParsed,
            CancellationToken ct)
        {
            int headerSize;
            uint channelId;
            Certificate? senderCertificate;

            using (var decoder = new BinaryDecoder(buffer, Quotas.MessageContext))
            {
                CertificateCollection? senderCertificateChain = ReadAsymmetricMessageSender(
                    decoder,
                    ref receiverCertificate,
                    out channelId,
                    out senderCertificate,
                    out string securityPolicyUri);

                onSenderCertificateParsed?.Invoke(senderCertificate);

                using (senderCertificateChain)
                {
                    // validate the sender certificate.
                    if (senderCertificate != null &&
                        Quotas.CertificateValidator != null &&
                        securityPolicyUri != SecurityPolicies.None)
                    {
                        CertificateValidationResult validationResult = await Quotas
                            .CertificateValidator
                            .ValidateAsync(senderCertificateChain!, ct: ct)
                            .ConfigureAwait(false);

                        if (!validationResult.IsValid)
                        {
                            throw new ServiceResultException(validationResult.StatusCode);
                        }
                    }
                }

                SelectEndpointForAsymmetricMessage(securityPolicyUri);

                headerSize = decoder.Position;
            }

            // decrypt the body.
            ArraySegment<byte> plainText = await DecryptAsync(
                new ArraySegment<byte>(
                    buffer.GetArray(),
                    buffer.Offset + headerSize,
                    buffer.Count - headerSize),
                new ArraySegment<byte>(buffer.GetArray(), buffer.Offset, headerSize),
                receiverCertificate!,
                ct).ConfigureAwait(false);

            ArraySegment<byte> body = FinishReadAsymmetricMessage(
                plainText,
                headerSize,
                receiverCertificate,
                senderCertificate,
                oscRequestSignature,
                out uint requestId,
                out uint sequenceNumber,
                out byte[] signature);

            return new AsymmetricMessage(
                body, channelId, senderCertificate, requestId, sequenceNumber, signature);
        }

        /// <summary>
        /// Reads the header of an asymmetric message and resolves the sender.
        /// </summary>
        /// <returns>
        /// The sender's certificate chain, which the caller owns and must
        /// dispose.
        /// </returns>
        private CertificateCollection? ReadAsymmetricMessageSender(
            BinaryDecoder decoder,
            ref Certificate? receiverCertificate,
            out uint channelId,
            out Certificate? senderCertificate,
            out string securityPolicyUri)
        {
            ReadAsymmetricMessageHeader(
                decoder,
                ref receiverCertificate,
                out channelId,
                out CertificateCollection? senderCertificateChain,
                out securityPolicyUri);

            senderCertificate = senderCertificateChain != null && senderCertificateChain.Count > 0
                ? senderCertificateChain[0].AddRef()
                : null;

            return senderCertificateChain;
        }

        /// <summary>
        /// Binds the channel to an endpoint the first time a message arrives.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void SelectEndpointForAsymmetricMessage(string securityPolicyUri)
        {
            // check if this is the first open secure channel request.
            if (!m_uninitialized)
            {
                if (securityPolicyUri != SecurityPolicyUri)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityPolicyRejected,
                        "Cannot change the security policy after creating the channnel.");
                }

                return;
            }

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
                        Volatile.Write(ref m_selectedEndpoint, endpoint);

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
                Volatile.Write(ref m_selectedEndpoint, null);
            }
        }

        /// <summary>
        /// Verifies the signature and padding of a decrypted asymmetric message
        /// and returns its body.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private ArraySegment<byte> FinishReadAsymmetricMessage(
            ArraySegment<byte> plainText,
            int headerSize,
            Certificate? receiverCertificate,
            Certificate? senderCertificate,
            byte[]? oscRequestSignature,
            out uint requestId,
            out uint sequenceNumber,
            out byte[] signature)
        {
            // extract signature.
            int signatureSize = GetAsymmetricSignatureSize(senderCertificate);

            signature = new byte[signatureSize];

            for (int ii = 0; ii < signatureSize; ii++)
            {
                signature[ii] = plainText.GetArray()[plainText.Offset + plainText.Count - signatureSize + ii];
            }

            ArraySegment<byte> dataToVerify;

            if (oscRequestSignature != null && SecurityPolicy!.SecureChannelEnhancements)
            {
                // copy OpenSecureChannel request signature if provided before verifying.
                dataToVerify = new ArraySegment<byte>(
                    plainText.GetArray(),
                    plainText.Offset,
                    plainText.Count - signatureSize + oscRequestSignature.Length);

                Array.Copy(
                    oscRequestSignature,
                    dataToVerify.Offset,
                    dataToVerify.GetArray(),
                    dataToVerify.Count - oscRequestSignature.Length,
                    oscRequestSignature.Length);
            }
            else
            {
                dataToVerify = new ArraySegment<byte>(
                    plainText.GetArray(),
                    plainText.Offset,
                    plainText.Count - signatureSize);
            }

            // verify the signature.
            if (!Verify(dataToVerify, signature, senderCertificate!))
            {
                m_logger.UaSCChannelLog0();

                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Could not verify the signature on the message.");
            }

            // verify padding.
            int paddingCount = 0;

            if (SecurityMode != MessageSecurityMode.None &&
                SecurityPolicy!.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None &&
                receiverCertificate!.GetRSAPublicKey() != null)
            {
                int paddingEnd;
                byte[] plainTextArray = plainText.GetArray();
                if (X509Utils.GetRSAPublicKeySize(receiverCertificate!) > TcpMessageLimits
                    .KeySizeExtraPadding)
                {
                    paddingEnd = plainText.Offset + plainText.Count - signatureSize - 1;
                    paddingCount = plainTextArray[paddingEnd - 1] +
                        (plainTextArray[paddingEnd] * 256);

                    //parse until paddingStart-1; the last one is actually the extrapaddingsize
                    for (int ii = paddingEnd - paddingCount; ii < paddingEnd; ii++)
                    {
                        if (plainTextArray[ii] != plainTextArray[paddingEnd - 1])
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
                    paddingCount = plainTextArray[paddingEnd];

                    for (int ii = paddingEnd - paddingCount; ii < paddingEnd; ii++)
                    {
                        if (plainTextArray[ii] != plainTextArray[paddingEnd])
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
                    plainText.GetArray(),
                    plainText.Offset + headerSize,
                    plainText.Count - headerSize,
                    Quotas.MessageContext))
            {
                sequenceNumber = decoder.ReadUInt32(null);
                requestId = decoder.ReadUInt32(null);
                headerSize += decoder.Position;
            }

            m_logger.UaSCChannelLog1(SecurityPolicyUri);
            m_logger.UaSCChannelLog2(senderCertificate);

            // return the body.
            return new ArraySegment<byte>(
                plainText.GetArray(),
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
        protected byte[] Sign(ArraySegment<byte> dataToSign, Certificate senderCertificate)
        {
            return CryptoUtils.Sign(dataToSign, senderCertificate, SecurityPolicyUri!)!;
        }

        /// <summary>
        /// Adds an asymmetric signature to the end of the buffer, without
        /// occupying the calling thread when the private key is served over a
        /// network.
        /// </summary>
        /// <param name="dataToSign">The block of data to be signed.</param>
        /// <param name="senderCertificate">The certificate whose key signs.</param>
        /// <param name="ct">Cancels the operation.</param>
        /// <returns>The signature.</returns>
        /// <remarks>
        /// The returned task completes synchronously unless the private key
        /// declares <see cref="IAsyncRsaKey"/> or <see cref="IAsyncEcdsaKey"/>.
        /// </remarks>
        protected async ValueTask<byte[]> SignAsync(
            ArraySegment<byte> dataToSign,
            Certificate senderCertificate,
            CancellationToken ct)
        {
            SecurityPolicyInfo policy = SecurityPolicies.Default.GetInfo(SecurityPolicyUri!)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    SecurityPolicyUri!);

            return (await CryptoUtils
                .SignAsync(dataToSign, senderCertificate, policy.AsymmetricSignatureAlgorithm, ct)
                .ConfigureAwait(false))!;
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
            Certificate senderCertificate)
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
            Certificate receiverCertificate)
        {
            SecurityPolicyInfo policy = SecurityPolicy!;
            if (policy.AsymmetricSignatureAlgorithm == AsymmetricSignatureAlgorithm.None ||
                policy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
            {
                byte[] encryptedBuffer = BufferManager.TakeBuffer(SendBufferSize, "Encrypt");

                Array.Copy(
                    headerToCopy.GetArray(),
                    headerToCopy.Offset,
                    encryptedBuffer,
                    0,
                    headerToCopy.Count);
                Array.Copy(
                    dataToEncrypt.GetArray(),
                    dataToEncrypt.Offset,
                    encryptedBuffer,
                    headerToCopy.Count,
                    dataToEncrypt.Count);

                return new ArraySegment<byte>(
                    encryptedBuffer,
                    0,
                    dataToEncrypt.Count + headerToCopy.Count);
            }

            switch (policy.AsymmetricEncryptionAlgorithm)
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
            Certificate receiverCertificate)
        {
            SecurityPolicyInfo policy = SecurityPolicy!;
            if (policy.AsymmetricSignatureAlgorithm == AsymmetricSignatureAlgorithm.None ||
                policy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
            {
                byte[] decryptedBuffer = BufferManager.TakeBuffer(SendBufferSize, "Decrypt");

                Array.Copy(
                    headerToCopy.GetArray(),
                    headerToCopy.Offset,
                    decryptedBuffer,
                    0,
                    headerToCopy.Count);
                Array.Copy(
                    dataToDecrypt.GetArray(),
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
                case SecurityPolicies.Basic128Rsa15:
                    return Rsa_Decrypt(
                        dataToDecrypt,
                        headerToCopy,
                        receiverCertificate,
                        RsaUtils.Padding.Pkcs1);
                default:
                    return Rsa_Decrypt(
                        dataToDecrypt,
                        headerToCopy,
                        receiverCertificate,
                        RsaUtils.Padding.OaepSHA256);
            }
        }

        /// <summary>
        /// Decrypts the buffer, without occupying the calling thread when the
        /// private key is served over a network.
        /// </summary>
        /// <param name="dataToDecrypt">The block of data to be decrypted.</param>
        /// <param name="headerToCopy">
        /// Unencrypted data that must be copied to the output.
        /// </param>
        /// <param name="receiverCertificate">
        /// The certificate whose key decrypts.
        /// </param>
        /// <param name="ct">Cancels the operation.</param>
        /// <returns>The plain text, preceded by the copied header.</returns>
        /// <remarks>
        /// The returned task completes synchronously unless the private key
        /// declares <see cref="IAsyncRsaKey"/>.
        /// </remarks>
        protected ValueTask<ArraySegment<byte>> DecryptAsync(
            ArraySegment<byte> dataToDecrypt,
            ArraySegment<byte> headerToCopy,
            Certificate receiverCertificate,
            CancellationToken ct)
        {
            SecurityPolicyInfo policy = SecurityPolicy!;
            if (policy.AsymmetricSignatureAlgorithm == AsymmetricSignatureAlgorithm.None ||
                policy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
            {
                // No asymmetric decryption is performed, so there is no key to
                // wait for and the existing path is taken unchanged.
                return new ValueTask<ArraySegment<byte>>(
                    Decrypt(dataToDecrypt, headerToCopy, receiverCertificate));
            }

            RsaUtils.Padding padding = SecurityPolicyUri switch
            {
                SecurityPolicies.Basic256 or
                SecurityPolicies.Aes128_Sha256_RsaOaep or
                SecurityPolicies.Basic256Sha256 => RsaUtils.Padding.OaepSHA1,
                SecurityPolicies.Basic128Rsa15 => RsaUtils.Padding.Pkcs1,
                _ => RsaUtils.Padding.OaepSHA256
            };

            return Rsa_DecryptAsync(
                dataToDecrypt, headerToCopy, receiverCertificate, padding, ct);
        }

        private readonly List<EndpointDescription> m_endpoints;
        private EndpointDescription? m_selectedEndpoint;
        private readonly ICertificateRegistry? m_serverCertificates;
        private bool m_uninitialized;
        private Nonce? m_localNonce;
        private Nonce? m_remoteNonce;
    }
}
