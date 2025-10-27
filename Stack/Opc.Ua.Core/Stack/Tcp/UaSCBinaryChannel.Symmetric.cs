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
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    public partial class UaSCUaBinaryChannel
    {
        /// <summary>
        /// Returns the current security token.
        /// </summary>
        protected internal ChannelToken CurrentToken { get; private set; }

        /// <summary>
        /// Returns the current security token.
        /// </summary>
        protected ChannelToken PreviousToken { get; private set; }

        /// <summary>
        /// Returns the renewed but not yet activated token.
        /// </summary>
        protected ChannelToken RenewedToken { get; private set; }

        /// <summary>
        /// Called when the token changes
        /// </summary>
        protected internal Action<ChannelToken, ChannelToken> OnTokenActivated { get; set; }

        /// <summary>
        /// Creates a new token.
        /// </summary>
        protected ChannelToken CreateToken()
        {
            var token = new ChannelToken
            {
                ChannelId = Id,
                TokenId = 0,
                CreatedAt = DateTime.UtcNow,
                CreatedAtTickCount = HiResClock.TickCount,
                Lifetime = Quotas.SecurityTokenLifetime
            };

            m_logger.LogInformation(
                "ChannelId {ChannelId}: New Token created. CreatedAt={CreatedAt:HH:mm:ss.fff}-{CreatedAtTickCount}. Lifetime={Lifetime}.",
                Id,
                token.CreatedAt,
                token.CreatedAtTickCount,
                token.Lifetime);

            return token;
        }

        /// <summary>
        /// Activates a new token.
        /// </summary>
        protected void ActivateToken(ChannelToken token)
        {
            // compute the keys for the token.
            ComputeKeys(token);

            Utils.SilentDispose(PreviousToken);
            PreviousToken = CurrentToken;
            CurrentToken = token;
            RenewedToken = null;

            OnTokenActivated?.Invoke(token, PreviousToken);

            m_logger.LogInformation(
                "ChannelId {Id}: Token #{TokenId} activated. CreatedAt={CreatedAt:HH:mm:ss.fff}-{CreatedAtTickCount}. Lifetime={Lifetime}.",
                Id,
                token.TokenId,
                token.CreatedAt,
                token.CreatedAtTickCount,
                token.Lifetime);
        }

        /// <summary>
        /// Sets the renewed token.
        /// </summary>
        protected void SetRenewedToken(ChannelToken token)
        {
            Utils.SilentDispose(RenewedToken);
            RenewedToken = token;
            m_logger.LogInformation(
                "ChannelId {Id}: Renewed Token #{TokenId} set. CreatedAt={CreatedAt:HH:mm:ss.fff}-{CreatedAtTickCount}. Lifetime={Lifetime}.",
                Id,
                token.TokenId,
                token.CreatedAt,
                token.CreatedAtTickCount,
                token.Lifetime);
        }

        /// <summary>
        /// Discards the tokens.
        /// </summary>
        protected void DiscardTokens()
        {
            Utils.SilentDispose(PreviousToken);
            PreviousToken = null;
            Utils.SilentDispose(CurrentToken);
            CurrentToken = null;
            Utils.SilentDispose(RenewedToken);
            RenewedToken = null;

            OnTokenActivated?.Invoke(null, null);
        }

        /// <summary>
        /// The byte length of the MAC (a.k.a signature) attached to each message.
        /// </summary>
        private int SymmetricSignatureSize { get; set; }

        /// <summary>
        /// The byte length the encryption blocks.
        /// </summary>
        private int EncryptionBlockSize { get; set; }

        /// <summary>
        /// Calculates the symmetric key sizes based on the current security policy.
        /// </summary>
        protected void CalculateSymmetricKeySizes()
        {
            SecurityPolicyInfo info = SecurityPolicies.GetInfo(SecurityPolicyUri);

            SymmetricSignatureSize = info.SymmetricSignatureLength;
            m_signatureKeySize = info.DerivedSignatureKeyLength;
            m_encryptionKeySize = info.SymmetricEncryptionKeyLength;
            EncryptionBlockSize = info.InitializationVectorLength != 0 ? info.InitializationVectorLength : 1;
        }

        private void DeriveKeysWithPSHA(
            HashAlgorithmName algorithmName,
            byte[] secret,
            byte[] seed,
            ChannelToken token,
            bool isServer)
        {
            int length = m_signatureKeySize + m_encryptionKeySize + EncryptionBlockSize;

            using HMAC hmac = Utils.CreateHMAC(algorithmName, secret);
            byte[] output = Utils.PSHA(hmac, null, seed, 0, length);

            byte[] signingKey = new byte[m_signatureKeySize];
            byte[] encryptingKey = new byte[m_encryptionKeySize];
            byte[] iv = new byte[EncryptionBlockSize];

            Buffer.BlockCopy(output, 0, signingKey, 0, signingKey.Length);
            Buffer.BlockCopy(output, m_signatureKeySize, encryptingKey, 0, encryptingKey.Length);
            Buffer.BlockCopy(output, m_signatureKeySize + m_encryptionKeySize, iv, 0, iv.Length);

            if (isServer)
            {
                token.ServerSigningKey = signingKey;
                token.ServerEncryptingKey = encryptingKey;
                token.ServerInitializationVector = iv;
            }
            else
            {
                token.ClientSigningKey = signingKey;
                token.ClientEncryptingKey = encryptingKey;
                token.ClientInitializationVector = iv;
            }
        }

#if ECC_SUPPORT
        private void DeriveKeysWithHKDF(
            ChannelToken token,
            byte[] salt,
            bool isServer)
        {
            int length =
                token.SecurityPolicy.DerivedSignatureKeyLength +
                token.SecurityPolicy.SymmetricEncryptionKeyLength +
                token.SecurityPolicy.InitializationVectorLength;

            byte[] prk = m_localNonce.DeriveKey(
                m_remoteNonce,
                salt,
                token.SecurityPolicy.GetKeyDerivationHashAlgorithmName(),
                length);

            byte[] signingKey = new byte[m_signatureKeySize];
            byte[] encryptingKey = new byte[m_encryptionKeySize];
            byte[] iv = new byte[EncryptionBlockSize];

            Buffer.BlockCopy(prk, 0, signingKey, 0, signingKey.Length);
            Buffer.BlockCopy(prk, m_signatureKeySize, encryptingKey, 0, encryptingKey.Length);
            Buffer.BlockCopy(prk, m_signatureKeySize + m_encryptionKeySize, iv, 0, iv.Length);

            if (isServer)
            {
                token.ServerSigningKey = signingKey;
                token.ServerEncryptingKey = encryptingKey;
                token.ServerInitializationVector = iv;
            }
            else
            {
                token.ClientSigningKey = signingKey;
                token.ClientEncryptingKey = encryptingKey;
                token.ClientInitializationVector = iv;
            }
        }
#endif

        /// <summary>
        /// Computes the keys for a token.
        /// </summary>
        protected void ComputeKeys(ChannelToken token)
        {
            token.SecurityPolicy = SecurityPolicies.GetInfo(SecurityPolicyUri);

            if (SecurityMode == MessageSecurityMode.None)
            {
                return;
            }

            byte[] serverSecret = token.ServerNonce;
            byte[] clientSecret = token.ClientNonce;

            switch (token.SecurityPolicy.KeyDerivationAlgorithm)
            {
#if ECC_SUPPORT
                case KeyDerivationAlgorithm.HKDFSha256:
                case KeyDerivationAlgorithm.HKDFSha384:
                {
                    byte[] length = token.SecurityPolicy.KeyDataLength;

                    byte[] serverSalt = Utils.Append(
                        length,
                        s_hkdfServerLabel,
                        serverSecret,
                        clientSecret);

                    byte[] clientSalt = Utils.Append(
                        length,
                        s_hkdfClientLabel,
                        clientSecret,
                        serverSecret);

#if DEBUG
                    m_logger.LogTrace("Length={Length}", Utils.ToHexString(length));
                    m_logger.LogTrace("ClientSecret={ClientSecret}", Utils.ToHexString(clientSecret));
                    m_logger.LogTrace("ServerSecret={ServerSecret}", Utils.ToHexString(serverSecret));
                    m_logger.LogTrace("ServerSalt={ServerSalt}", Utils.ToHexString(serverSalt));
                    m_logger.LogTrace("ClientSalt={ClientSalt}", Utils.ToHexString(clientSalt));
#endif

                    DeriveKeysWithHKDF(token, serverSalt, true);
                    DeriveKeysWithHKDF(token, clientSalt, false);
                    break;
                }
#endif
                case KeyDerivationAlgorithm.PSha1:
                case KeyDerivationAlgorithm.PSha256:
                default:
                    HashAlgorithmName algorithmName = token.SecurityPolicy.GetKeyDerivationHashAlgorithmName();
                    DeriveKeysWithPSHA(algorithmName, serverSecret, clientSecret, token, false);
                    DeriveKeysWithPSHA(algorithmName, clientSecret, serverSecret, token, true);
                    break;
            }
        }

        /// <summary>
        /// Secures the message using the security token.
        /// </summary>
        protected BufferCollection WriteSymmetricMessage(
            uint messageType,
            uint requestId,
            ChannelToken token,
            object messageBody,
            bool isRequest,
            out bool limitsExceeded)
        {
            limitsExceeded = false;
            bool success = false;
            BufferCollection chunksToProcess = null;

            try
            {
                // calculate chunk sizes.
                int maxCipherTextSize = SendBufferSize - TcpMessageLimits.SymmetricHeaderSize;
                int maxCipherBlocks = maxCipherTextSize / EncryptionBlockSize;
                int maxPlainTextSize = maxCipherBlocks * EncryptionBlockSize;

                int paddingCountSize =
                    SecurityMode != MessageSecurityMode.SignAndEncrypt || token.SecurityPolicy.NoSymmetricEncryptionPadding
                    ? 0
                    : 1;

                int maxPayloadSize =
                    maxPlainTextSize -
                    SymmetricSignatureSize -
                    TcpMessageLimits.SequenceHeaderSize -
                    paddingCountSize;

                const int headerSize = TcpMessageLimits.SymmetricHeaderSize +
                    TcpMessageLimits.SequenceHeaderSize;

                // write the body to stream.
                var ostrm = new ArraySegmentStream(
                    BufferManager,
                    SendBufferSize,
                    headerSize,
                    maxPayloadSize);

                // check for encodeable body.
                if (messageBody is IEncodeable encodeable)
                {
                    // debug code used to verify that message aborts are handled correctly.
                    // int maxMessageSize = Quotas.MessageContext.MaxMessageSize;
                    // Quotas.MessageContext.MaxMessageSize = Int32.MaxValue;

                    BinaryEncoder.EncodeMessage(encodeable, ostrm, Quotas.MessageContext, true);

                    // Quotas.MessageContext.MaxMessageSize = maxMessageSize;
                }

                // check for raw bytes.
                var rawBytes = messageBody as ArraySegment<byte>?;

                if (rawBytes != null)
                {
                    using var encoder = new BinaryEncoder(ostrm, Quotas.MessageContext, true);
                    encoder.WriteRawBytes(
                        rawBytes.Value.Array,
                        rawBytes.Value.Offset,
                        rawBytes.Value.Count);
                }

                chunksToProcess = ostrm.GetBuffers("WriteSymmetricMessage");

                // ensure there is at least one chunk.
                if (chunksToProcess.Count == 0)
                {
                    byte[] buffer = BufferManager.TakeBuffer(
                        SendBufferSize,
                        "WriteSymmetricMessage");

                    chunksToProcess.Add(new ArraySegment<byte>(buffer, 0, 0));
                }

                var chunksToSend = new BufferCollection(chunksToProcess.Capacity);

                int messageSize = 0;

                for (int ii = 0; ii < chunksToProcess.Count; ii++)
                {
                    ArraySegment<byte> chunkToProcess = chunksToProcess[ii];

                    // nothing more to do if limits exceeded.
                    if (limitsExceeded)
                    {
                        BufferManager.ReturnBuffer(chunkToProcess.Array, "WriteSymmetricMessage");
                        continue;
                    }

                    var strm = new MemoryStream(chunkToProcess.Array, 0, SendBufferSize);
                    using var encoder = new BinaryEncoder(strm, Quotas.MessageContext, false);

                    // check if the message needs to be aborted.
                    if (MessageLimitsExceeded(
                        isRequest,
                        messageSize + chunkToProcess.Count - headerSize,
                        ii + 1))
                    {
                        encoder.WriteUInt32(null, messageType | TcpMessageType.Abort);

                        // replace the body in the chunk with an error message.
                        using (
                            var errorEncoder = new BinaryEncoder(
                                chunkToProcess.Array,
                                chunkToProcess.Offset,
                                chunkToProcess.Count,
                                Quotas.MessageContext))
                        {
                            WriteErrorMessageBody(
                                errorEncoder,
                                isRequest
                                    ? StatusCodes.BadRequestTooLarge
                                    : StatusCodes.BadResponseTooLarge);
                            int size = errorEncoder.Close();
                            chunkToProcess = new ArraySegment<byte>(
                                chunkToProcess.Array,
                                chunkToProcess.Offset,
                                size);
                        }

                        limitsExceeded = true;
                    }

                    // check if the message is complete.
                    else if (ii == chunksToProcess.Count - 1)
                    {
                        encoder.WriteUInt32(null, messageType | TcpMessageType.Final);
                    }
                    // more chunks to follow.
                    else
                    {
                        encoder.WriteUInt32(null, messageType | TcpMessageType.Intermediate);
                    }

                    int count = 0;

                    count += TcpMessageLimits.SequenceHeaderSize;
                    count += chunkToProcess.Count;
                    count += paddingCountSize;

                    int padding = 0;

                    if (paddingCountSize > 0)
                    {
                        padding = EncryptionBlockSize - (count % EncryptionBlockSize);

                        if (padding < EncryptionBlockSize)
                        {
                            count += padding;
                        }
                    }

                    count += TcpMessageLimits.SymmetricHeaderSize;
                    count += SymmetricSignatureSize;

                    encoder.WriteUInt32(null, (uint)count);
                    encoder.WriteUInt32(null, ChannelId);
                    encoder.WriteUInt32(null, token.TokenId);

                    uint sequenceNumber = GetNewSequenceNumber();
                    encoder.WriteUInt32(null, sequenceNumber);
                    encoder.WriteUInt32(null, requestId);

                    // skip body.
                    strm.Seek(chunkToProcess.Count, SeekOrigin.Current);

                    // update message size count.
                    messageSize += chunkToProcess.Count;

                    ArraySegment<byte> dataToSend;

                    if (SecurityMode != MessageSecurityMode.None)
                    {
                        dataToSend = new ArraySegment<byte>(
                            chunkToProcess.Array,
                            TcpMessageLimits.SymmetricHeaderSize,
                            encoder.Position - TcpMessageLimits.SymmetricHeaderSize);

                        dataToSend = EncryptAndSign(token, dataToSend, isRequest);
                    }
                    else
                    {
                        dataToSend = new ArraySegment<byte>(
                            chunkToProcess.Array,
                            0,
                            encoder.Position);
                    }

                    // add the header into chunk.
                    chunksToSend.Add(dataToSend);
                }

                // ensure the buffers don't get cleaned up on exit.
                success = true;
                return chunksToSend;
            }
            finally
            {
                if (!success)
                {
                    chunksToProcess?.Release(BufferManager, "WriteSymmetricMessage");
                }
            }
        }

        /// <summary>
        /// Decrypts and verifies a message chunk.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected ArraySegment<byte> ReadSymmetricMessage(
            ArraySegment<byte> buffer,
            bool isRequest,
            out ChannelToken token,
            out uint requestId,
            out uint sequenceNumber)
        {
            using var decoder = new BinaryDecoder(buffer, Quotas.MessageContext);
            uint messageType = decoder.ReadUInt32(null);
            uint messageSize = decoder.ReadUInt32(null);
            uint channelId = decoder.ReadUInt32(null);
            uint tokenId = decoder.ReadUInt32(null);

            // ensure the channel is valid.
            if (channelId != ChannelId)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "SecureChannelId is not known. ChanneId={0}, CurrentChannelId={1}",
                    channelId,
                    ChannelId);
            }

            // check for a message secured with the new token.
            if (RenewedToken != null && RenewedToken.TokenId == tokenId)
            {
                ActivateToken(RenewedToken);
            }
            // check if activation of the new token should be forced.
            else if (RenewedToken != null && CurrentToken.ActivationRequired)
            {
                ActivateToken(RenewedToken);
                m_logger.LogInformation(
                    "ChannelId {ChannelId}: Token #{TokenId} activated forced.",
                    Id,
                    CurrentToken.TokenId);
            }

            // check for valid token.
            ChannelToken currentToken =
                CurrentToken ??
                throw new ServiceResultException(StatusCodes.BadSecureChannelClosed);

            // find the token.
            if (currentToken.TokenId != tokenId &&
                (PreviousToken == null || PreviousToken.TokenId != tokenId))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "Channel{0}: TokenId is not known. ChanneId={1}, TokenId={2}, CurrentTokenId={3}, PreviousTokenId={4}",
                    Id,
                    channelId,
                    tokenId,
                    currentToken.TokenId,
                    PreviousToken != null ? (int)PreviousToken.TokenId : -1);
            }

            token = currentToken;

            // check for a message secured with the token before it expired.
            if (PreviousToken != null && PreviousToken.TokenId == tokenId)
            {
                token = PreviousToken;
            }

            // check if token has expired.
            if (token.Expired)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "Channel{0}: Token #{1} has expired. Lifetime={2:HH:mm:ss.fff}-{3}",
                    Id,
                    token.TokenId,
                    token.CreatedAt,
                    token.CreatedAtTickCount);
            }

            int headerSize = decoder.Position;

            var dataToProcess = new ArraySegment<byte>(
                buffer.Array,
                buffer.Offset,
                buffer.Count);

            if (SecurityMode != MessageSecurityMode.None)
            {
                Debug.Assert(buffer.Offset == 0, "Code assumes buffer.Offset == 0");

                dataToProcess = new ArraySegment<byte>(
                    buffer.Array,
                    buffer.Offset + headerSize,
                    buffer.Count - headerSize);

                dataToProcess = DecryptAndVerify(
                    token,
                    dataToProcess,
                    isRequest);
            }

            // extract request id and sequence number.
            sequenceNumber = decoder.ReadUInt32(null);
            requestId = decoder.ReadUInt32(null);

            headerSize += TcpMessageLimits.SequenceHeaderSize;

            // return only the data contained in the message.
            return new ArraySegment<byte>(
                dataToProcess.Array,
                dataToProcess.Offset + headerSize,
                dataToProcess.Count - headerSize);
        }

        /// <summary>
        /// Decrypts the data in a buffer using symmetric encryption.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        protected ArraySegment<byte> EncryptAndSign(
            ChannelToken token,
            ArraySegment<byte> dataToEncrypt,
            bool useClientKeys)
        {
            return EccUtils.SymmetricEncryptAndSign(
                dataToEncrypt,
                token.SecurityPolicy,
                useClientKeys ? token.ClientEncryptingKey : token.ServerEncryptingKey,
                useClientKeys ? token.ClientInitializationVector : token.ServerInitializationVector,
                useClientKeys ? token.ClientSigningKey : token.ServerSigningKey,
                SecurityMode == MessageSecurityMode.Sign);
        }

        /// <summary>
        /// Decrypts the data in a buffer using symmetric encryption.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        protected ArraySegment<byte> DecryptAndVerify(
            ChannelToken token,
            ArraySegment<byte> dataToDecrypt,
            bool useClientKeys)
        {
            return EccUtils.SymmetricDecryptAndVerify(
                dataToDecrypt,
                token.SecurityPolicy,
                useClientKeys ? token.ClientEncryptingKey : token.ServerEncryptingKey,
                useClientKeys ? token.ClientInitializationVector : token.ServerInitializationVector,
                useClientKeys ? token.ClientSigningKey : token.ServerSigningKey,
                SecurityMode == MessageSecurityMode.Sign);
        }

        /// <summary>
        /// Returns the symmetric signature for the data.
        /// </summary>
        protected byte[] Sign(ChannelToken token, ArraySegment<byte> dataToSign, bool useClientKeys)
        {
            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP256r1:
                case SecurityPolicies.ECC_brainpoolP384r1:
                case SecurityPolicies.ECC_nistP256:
                    return SymmetricSign(token, dataToSign, useClientKeys);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Returns the symmetric signature for the data.
        /// </summary>
        protected bool Verify(
            ChannelToken token,
            byte[] signature,
            ArraySegment<byte> dataToVerify,
            bool useClientKeys)
        {
            // verify signature.
            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.None:
                    return true;
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP256r1:
                case SecurityPolicies.ECC_brainpoolP384r1:
                    return SymmetricVerify(token, signature, dataToVerify, useClientKeys);
                default:
                    return false;
            }
        }

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        /// <summary>
        /// Signs the message using HMAC.
        /// </summary>
        private static byte[] SymmetricSign(
            ChannelToken token,
            ReadOnlySpan<byte> dataToSign,
            bool useClientKeys)
        {
            // get HMAC object.
            using HMAC hmac = token.SecurityPolicy.CreateSignatureHmac(useClientKeys ? token.ClientSigningKey : token.ServerSigningKey);

            // compute hash.
            int hashSizeInBytes = hmac.HashSize >> 3;
            byte[] signature = new byte[hashSizeInBytes];
            bool result = hmac.TryComputeHash(dataToSign, signature, out int bytesWritten);

            // check result
            if (!result || bytesWritten != hashSizeInBytes)
            {
                ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "The computed hash doesn't match the expected size.");
            }

            // return signature.
            return signature;
        }
#else
        /// <summary>
        /// Signs the message using HMAC.
        /// </summary>
        private static byte[] SymmetricSign(
            ChannelToken token,
            ArraySegment<byte> dataToSign,
            bool useClientKeys)
        {
            // get HMAC object.
            using HMAC hmac = token.SecurityPolicy.CreateSignatureHmac(useClientKeys ? token.ClientSigningKey : token.ServerSigningKey);
            // compute hash.
            var istrm = new MemoryStream(
                dataToSign.Array,
                dataToSign.Offset,
                dataToSign.Count,
                false);
            byte[] signature = hmac.ComputeHash(istrm);
            istrm.Dispose();

            // return signature.
            return signature;
        }
#endif

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        /// <summary>
        /// Verifies a HMAC for a message.
        /// </summary>
        private bool SymmetricVerify(
            ChannelToken token,
            ReadOnlySpan<byte> signature,
            ReadOnlySpan<byte> dataToVerify,
            bool useClientKeys)
        {
            // get HMAC object.
            using HMAC hmac = token.SecurityPolicy.CreateSignatureHmac(useClientKeys ? token.ClientSigningKey : token.ServerSigningKey);

            // compute hash.
            int hashSizeInBytes = hmac.HashSize >> 3;
            Span<byte> computedSignature = stackalloc byte[hashSizeInBytes];
            bool result = hmac.TryComputeHash(
                dataToVerify,
                computedSignature,
                out int bytesWritten);
            Debug.Assert(bytesWritten == hashSizeInBytes);
            // compare signatures.
            if (!result || !computedSignature.SequenceEqual(signature))
            {
                string expectedSignature = Utils.ToHexString(computedSignature.ToArray());
                string messageType = Encoding.UTF8.GetString(dataToVerify[..4]);
                int messageLength = BitConverter.ToInt32(dataToVerify[4..]);
                string actualSignature = Utils.ToHexString(signature);
#else
        /// <summary>
        /// Verifies a HMAC for a message.
        /// </summary>
        private bool SymmetricVerify(
            ChannelToken token,
            byte[] signature,
            ArraySegment<byte> dataToVerify,
            bool useClientKeys)
        {
            // get HMAC object.
            using HMAC hmac = token.SecurityPolicy.CreateSignatureHmac(useClientKeys ? token.ClientSigningKey : token.ServerSigningKey);

            var istrm = new MemoryStream(
                dataToVerify.Array,
                dataToVerify.Offset,
                dataToVerify.Count,
                false);
            byte[] computedSignature = hmac.ComputeHash(istrm);
            istrm.Dispose();
            // compare signatures.
            if (!Utils.IsEqual(computedSignature, signature))
            {
                string expectedSignature = Utils.ToHexString(computedSignature);
                string messageType = Encoding.UTF8
                    .GetString(dataToVerify.Array, dataToVerify.Offset, 4);
                int messageLength = BitConverter.ToInt32(
                    dataToVerify.Array,
                    dataToVerify.Offset + 4);
                string actualSignature = Utils.ToHexString(signature);
#endif
                m_logger.LogError(
                    "Channel{Id}: Could not validate signature. ChannelId={ChannelId}, TokenId={TokenId}, MessageType={MessageType}, Length={Length} ExpectedSignature={ExpectedSignature} ActualSignature={ActualSignature}",
                    Id,
                    token.ChannelId,
                    token.TokenId,
                    messageType,
                    messageLength,
                    expectedSignature,
                    actualSignature);

                return false;
            }

            return true;
        }

#if ECC_SUPPORT
        private static readonly byte[] s_hkdfClientLabel = Encoding.UTF8.GetBytes("opcua-client");
        private static readonly byte[] s_hkdfServerLabel = Encoding.UTF8.GetBytes("opcua-server");
#endif
        private int m_signatureKeySize;
        private int m_encryptionKeySize;
    }
}
