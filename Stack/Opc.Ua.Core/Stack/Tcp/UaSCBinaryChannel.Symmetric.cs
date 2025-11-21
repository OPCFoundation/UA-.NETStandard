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
        /// <exception cref="ServiceResultException"></exception>
        protected void CalculateSymmetricKeySizes()
        {
            var securityPolicyUri = SecurityPolicyUri;
            if (securityPolicyUri.StartsWith(SecurityPolicies.BaseUri, StringComparison.Ordinal))
            {
                securityPolicyUri = securityPolicyUri.Substring(SecurityPolicies.BaseUri.Length);
            }

            SecurityPolicyInfo info = SecurityPolicies.GetInfo(securityPolicyUri)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    SecurityPolicyUri);

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

        /// <summary>
        /// Computes the keys for a token.
        /// </summary>
        protected void ComputeKeys(ChannelToken token)
        {
            // Strip BaseUri prefix to get short name for dictionary lookup
            var securityPolicyUri = SecurityPolicyUri;
            if (securityPolicyUri.StartsWith(SecurityPolicies.BaseUri, StringComparison.Ordinal))
            {
                securityPolicyUri = securityPolicyUri.Substring(SecurityPolicies.BaseUri.Length);
            }

            token.SecurityPolicy = SecurityPolicies.GetInfo(securityPolicyUri);

            if (token.SecurityPolicy == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    SecurityPolicyUri);
            }

            if (SecurityMode == MessageSecurityMode.None)
            {
                return;
            }

            byte[] serverSecret = token.ServerNonce;
            byte[] clientSecret = token.ClientNonce;

            m_logger?.LogInformation(
                "[ComputeKeys] KeyDerivationAlgorithm: {Algo}",
                token.SecurityPolicy.KeyDerivationAlgorithm);

            if (token.SecurityPolicy.KeyDerivationAlgorithm == KeyDerivationAlgorithm.PSha1 ||
                token.SecurityPolicy.KeyDerivationAlgorithm == KeyDerivationAlgorithm.PSha256)
            {
                HashAlgorithmName algorithmName = token.SecurityPolicy.GetKeyDerivationHashAlgorithmName();
                DeriveKeysWithPSHA(algorithmName, serverSecret, clientSecret, token, false);
                DeriveKeysWithPSHA(algorithmName, clientSecret, serverSecret, token, true);
            }
            else
            {
                byte[] keyData = SecurityMode == MessageSecurityMode.Sign
                    ? token.SecurityPolicy.KeyDataLength
                    : token.SecurityPolicy.KeyDataLength;

                byte[] serverSalt = Utils.Append(
                    keyData,
                    s_hkdfServerLabel,
                    serverSecret,
                    clientSecret);
                byte[] clientSalt = Utils.Append(
                    keyData,
                    s_hkdfClientLabel,
                    clientSecret,
                    serverSecret);

#if DEBUG
                m_logger.LogDebug("KeyData={KeyData}", Utils.ToHexString(keyData));
                m_logger.LogDebug("ClientSecret={ClientSecret}", Utils.ToHexString(clientSecret));
                m_logger.LogDebug("ServerSecret={ServerSecret}", Utils.ToHexString(serverSecret));
                m_logger.LogDebug("ServerSalt={ServerSalt}", Utils.ToHexString(serverSalt));
                m_logger.LogDebug("ClientSalt={ClientSalt}", Utils.ToHexString(clientSalt));
#endif

                DeriveKeysWithHKDF(token, serverSalt, true);
                DeriveKeysWithHKDF(token, clientSalt, false);
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
                int maxPayloadSize =
                    maxPlainTextSize -
                    SymmetricSignatureSize -
                    1 -
                    TcpMessageLimits.SequenceHeaderSize;
                const int headerSize = TcpMessageLimits.SymmetricHeaderSize +
                    TcpMessageLimits.SequenceHeaderSize;

                // no padding byte for authenticated encryption.
                if (token.SecurityPolicy.NoSymmetricEncryptionPadding)
                {
                    maxPayloadSize++;
                }

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

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
                Span<byte> paddingBuffer = stackalloc byte[EncryptionBlockSize];
#endif
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
                    count += SymmetricSignatureSize;

                    // calculate the padding.
                    int padding = 0;

                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                        !token.SecurityPolicy.NoSymmetricEncryptionPadding)
                    {
                        // reserve one byte for the padding size.
                        count++;

                        // use padding as helper to calc the real padding
                        padding = count % EncryptionBlockSize;
                        if (padding != 0)
                        {
                            padding = EncryptionBlockSize - padding;
                        }

                        count += padding;
                    }

                    count += TcpMessageLimits.SymmetricHeaderSize;

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

                    // write padding.
                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                        !token.SecurityPolicy.NoSymmetricEncryptionPadding)
                    {
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
                        if (padding > 1)
                        {
                            Span<byte> buffer = paddingBuffer[..(padding + 1)];
                            buffer.Fill((byte)padding);
                            encoder.WriteRawBytes(buffer);
                        }
                        else
#endif
                        {
                            for (int jj = 0; jj <= padding; jj++)
                            {
                                encoder.WriteByte(null, (byte)padding);
                            }
                        }
                    }

                    // calculate and write signature.
                    if (SecurityMode != MessageSecurityMode.None)
                    {
                        if (token.SecurityPolicy.NoSymmetricEncryptionPadding)
                        {
                            strm.Seek(SymmetricSignatureSize, SeekOrigin.Current);
                        }
                        else
                        {
                            byte[] signature = Sign(
                                token,
                                new ArraySegment<byte>(chunkToProcess.Array, 0, encoder.Position),
                                isRequest);

                            if (signature != null)
                            {
                                encoder.WriteRawBytes(signature, 0, signature.Length);
                            }
                        }
                    }

                    if ((SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                        !token.SecurityPolicy.NoSymmetricEncryptionPadding) ||
                        (SecurityMode != MessageSecurityMode.None && token.SecurityPolicy.NoSymmetricEncryptionPadding))
                    {
                        // encrypt the data.
                        var dataToEncrypt = new ArraySegment<byte>(
                            chunkToProcess.Array,
                            TcpMessageLimits.SymmetricHeaderSize,
                            encoder.Position - TcpMessageLimits.SymmetricHeaderSize);
                        Encrypt(token, dataToEncrypt, isRequest);
                    }

                    // add the header into chunk.
                    chunksToSend.Add(
                        new ArraySegment<byte>(chunkToProcess.Array, 0, encoder.Position));
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
                    "ChannelId {Id}: Token #{TokenId} activated forced.",
                    Id,
                    CurrentToken.TokenId);
            }

            // check for valid token.
            ChannelToken currentToken =
                CurrentToken ??
                throw ServiceResultException.Create(
                    StatusCodes.BadSecureChannelClosed,
                    "Channel{0}: Token missing to read symmetric messagee.", Id);

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

            int decryptedCount = buffer.Count - headerSize;
            if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
            {
                // decrypt the message.
                decryptedCount = Decrypt(
                    token,
                    new ArraySegment<byte>(
                        buffer.Array,
                        buffer.Offset + headerSize,
                        buffer.Count - headerSize),
                    isRequest);
            }

            int paddingCount = 0;
            if (SecurityMode != MessageSecurityMode.None &&
                !token.SecurityPolicy.NoSymmetricEncryptionPadding)
            {
                int signatureStart =
                    buffer.Offset +
                    headerSize +
                    decryptedCount -
                    SymmetricSignatureSize;

                // extract signature.
                byte[] signature = new byte[SymmetricSignatureSize];
                Array.Copy(buffer.Array, signatureStart, signature, 0, signature.Length);

                // verify the signature.
                if (!Verify(
                        token,
                        signature,
                        new ArraySegment<byte>(
                            buffer.Array,
                            buffer.Offset,
                            headerSize + decryptedCount - SymmetricSignatureSize),
                        isRequest))
                {
                    m_logger.LogError("ChannelId {Id}: Could not verify signature on message.", Id);
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Could not verify the signature on the message.");
                }

                if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
                {
                    // verify padding.
                    int paddingStart = signatureStart - 1;
                    paddingCount = buffer.Array[paddingStart];

                    for (int ii = paddingStart - paddingCount; ii < paddingStart; ii++)
                    {
                        if (buffer.Array[ii] != paddingCount)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadSecurityChecksFailed,
                                "Could not verify the padding in the message.");
                        }
                    }

                    // add byte for size.
                    paddingCount++;
                }
            }
            else if (SecurityMode != MessageSecurityMode.None)
            {
                // AEAD algorithms are verified during decrypt.
                paddingCount = 0;
            }

            // extract request id and sequence number.
            sequenceNumber = decoder.ReadUInt32(null);
            requestId = decoder.ReadUInt32(null);

            // return an the data contained in the message.
            int startOfBody =
                buffer.Offset +
                TcpMessageLimits.SymmetricHeaderSize +
                TcpMessageLimits.SequenceHeaderSize;
            int sizeOfBody =
                decryptedCount -
                TcpMessageLimits.SequenceHeaderSize -
                paddingCount -
                (SecurityMode != MessageSecurityMode.None &&
                 !token.SecurityPolicy.NoSymmetricEncryptionPadding
                    ? SymmetricSignatureSize
                    : 0);

            return new ArraySegment<byte>(buffer.Array, startOfBody, sizeOfBody);
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

        /// <summary>
        /// Encrypts and signs the data in a buffer using symmetric encryption.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        protected void Encrypt(
            ChannelToken token,
            ArraySegment<byte> dataToEncrypt,
            bool useClientKeys)
        {
            byte[] encryptingKey = useClientKeys ? token.ClientEncryptingKey : token.ServerEncryptingKey;
            byte[] iv = useClientKeys ? token.ClientInitializationVector : token.ServerInitializationVector;
            byte[] signingKey = useClientKeys ? token.ClientSigningKey : token.ServerSigningKey;

            bool signOnly = SecurityMode == MessageSecurityMode.Sign;

            if (SecurityPolicyUri == SecurityPolicies.None)
            {
                return;
            }

            // For CBC based policies the caller already applied padding and signatures.
            if (token.SecurityPolicy.SymmetricEncryptionAlgorithm is SymmetricEncryptionAlgorithm.Aes128Cbc
                or SymmetricEncryptionAlgorithm.Aes256Cbc)
            {
                if (signOnly)
                {
                    return;
                }

                using var aes = Aes.Create();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = encryptingKey;
                aes.IV = iv;

                using ICryptoTransform encryptor = aes.CreateEncryptor();
                encryptor.TransformBlock(
                    dataToEncrypt.Array,
                    dataToEncrypt.Offset,
                    dataToEncrypt.Count,
                    dataToEncrypt.Array,
                    dataToEncrypt.Offset);
                return;
            }

            ArraySegment<byte> result = EccUtils.SymmetricEncryptAndSign(
                dataToEncrypt,
                token.SecurityPolicy,
                encryptingKey,
                iv,
                signingKey,
                signOnly);

            // Copy result back to original buffer if different
            if (result.Array != dataToEncrypt.Array || result.Offset != dataToEncrypt.Offset)
            {
                Buffer.BlockCopy(result.Array, result.Offset, dataToEncrypt.Array, dataToEncrypt.Offset, result.Count);
            }
        }

        /// <summary>
        /// Decrypts and verifies the data in a buffer using symmetric encryption.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        protected int Decrypt(
            ChannelToken token,
            ArraySegment<byte> dataToDecrypt,
            bool useClientKeys)
        {
            if (SecurityPolicyUri == SecurityPolicies.None)
            {
                return dataToDecrypt.Count;
            }

            byte[] encryptingKey = useClientKeys ? token.ClientEncryptingKey : token.ServerEncryptingKey;
            byte[] iv = useClientKeys ? token.ClientInitializationVector : token.ServerInitializationVector;
            byte[] signingKey = useClientKeys ? token.ClientSigningKey : token.ServerSigningKey;

            bool signOnly = SecurityMode == MessageSecurityMode.Sign;

            // For CBC based policies the caller will verify signatures and remove padding.
            if (token.SecurityPolicy.SymmetricEncryptionAlgorithm is SymmetricEncryptionAlgorithm.Aes128Cbc
                or SymmetricEncryptionAlgorithm.Aes256Cbc)
            {
                if (signOnly)
                {
                    return dataToDecrypt.Count;
                }

                using var aes = Aes.Create();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = encryptingKey;
                aes.IV = iv;

                using ICryptoTransform decryptor = aes.CreateDecryptor();
                decryptor.TransformBlock(
                    dataToDecrypt.Array,
                    dataToDecrypt.Offset,
                    dataToDecrypt.Count,
                    dataToDecrypt.Array,
                    dataToDecrypt.Offset);

                return dataToDecrypt.Count;
            }

            ArraySegment<byte> result = EccUtils.SymmetricDecryptAndVerify(
                dataToDecrypt,
                token.SecurityPolicy,
                encryptingKey,
                iv,
                signingKey,
                signOnly);

            // Copy result back to original buffer if different
            if (result.Array != dataToDecrypt.Array || result.Offset != dataToDecrypt.Offset)
            {
                Buffer.BlockCopy(result.Array, result.Offset, dataToDecrypt.Array, dataToDecrypt.Offset, result.Count);
            }

            // return the decrypted size (without authentication tag/padding)
            return result.Count - dataToDecrypt.Offset;
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
            byte[] signingKey = useClientKeys ? token.ClientSigningKey : token.ServerSigningKey;

            using HMAC hmac = token.SecurityPolicy.CreateSignatureHmac(signingKey);

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
            byte[] signingKey = useClientKeys ? token.ClientSigningKey : token.ServerSigningKey;

            using HMAC hmac = token.SecurityPolicy.CreateSignatureHmac(signingKey);
            
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
            byte[] signingKey = useClientKeys ? token.ClientSigningKey : token.ServerSigningKey;

            using HMAC hmac = token.SecurityPolicy.CreateSignatureHmac(signingKey);

            // compute hash.
            int hashSizeInBytes = hmac.HashSize >> 3;
            Span<byte> computedSignature = stackalloc byte[hashSizeInBytes];
            bool result = hmac.TryComputeHash(
                dataToVerify,
                computedSignature,
                out int bytesWritten);
            System.Diagnostics.Debug.Assert(bytesWritten == hashSizeInBytes);
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
            byte[] signingKey = useClientKeys ? token.ClientSigningKey : token.ServerSigningKey;

            using HMAC hmac = token.SecurityPolicy.CreateSignatureHmac(signingKey);

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



#if CURVE25519
        /// <summary>
        /// Encrypts a message using a symmetric algorithm.
        /// </summary>
        private static void SymmetricEncryptWithChaCha20Poly1305(
            ChannelToken token,
            uint lastSequenceNumber,
            ArraySegment<byte> dataToEncrypt,
            bool useClientKeys)
        {
            var signingKey = (useClientKeys) ? token.ClientSigningKey : token.ServerSigningKey;

            if (signingKey == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Token missing symmetric key object.");
            }

            var encryptingKey = (useClientKeys) ? token.ClientEncryptingKey : token.ServerEncryptingKey;

            if (encryptingKey == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Token missing symmetric key object.");
            }

            var iv = (useClientKeys) ? token.ClientInitializationVector : token.ServerInitializationVector;

            if (iv == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Token missing symmetric key object.");
            }

            // Utils.Trace($"EncryptKey={Utils.ToHexString(encryptingKey)}");
            // Utils.Trace($"EncryptIV1={Utils.ToHexString(iv)}");
            ApplyChaCha20Poly1305Mask(token, lastSequenceNumber, iv);
            // Utils.Trace($"EncryptIV2={Utils.ToHexString(iv)}");

            int signatureLength = 16;

            var plaintext = dataToEncrypt.Array;
            int headerSize = dataToEncrypt.Offset;
            int plainTextLength = dataToEncrypt.Offset + dataToEncrypt.Count - signatureLength;

            // Utils.Trace($"OUT={headerSize}|{plainTextLength}|{signatureLength}|[{plainTextLength + signatureLength}]");

            AeadParameters parameters = new AeadParameters(
                new KeyParameter(encryptingKey),
                signatureLength * 8,
                iv,
                null);

            ChaCha20Poly1305 encryptor = new ChaCha20Poly1305();
            encryptor.Init(true, parameters);
            encryptor.ProcessAadBytes(plaintext, 0, headerSize);

            byte[] ciphertext = new byte[encryptor.GetOutputSize(plainTextLength - headerSize) + headerSize];
            Buffer.BlockCopy(plaintext, 0, ciphertext, 0, headerSize);
            int length = encryptor.ProcessBytes(
                plaintext,
                headerSize,
                plainTextLength - headerSize,
                ciphertext,
                headerSize);
            length += encryptor.DoFinal(ciphertext, length + headerSize);

            if (ciphertext.Length - headerSize != length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    $"Cipher text not the expected size. [{ciphertext.Length - headerSize} != {length}]");
            }

            Buffer.BlockCopy(ciphertext, 0, plaintext, 0, plainTextLength + signatureLength);

            // byte[] mac = new byte[16];
            // Buffer.BlockCopy(plaintext, plainTextLength, mac, 0, signatureLength);
            // Utils.Trace($"EncryptMAC1={Utils.ToHexString(encryptor.GetMac())}");
            // Utils.Trace($"EncryptMAC2={Utils.ToHexString(mac)}");
        }

        /// <summary>
        /// Encrypts a message using a symmetric algorithm.
        /// </summary>
        private static void SymmetricSignWithPoly1305(
            ChannelToken token,
            uint lastSequenceNumber,
            ArraySegment<byte> dataToEncrypt,
            bool useClientKeys)
        {
            var signingKey = (useClientKeys) ? token.ClientSigningKey : token.ServerSigningKey;

            if (signingKey == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Token missing symmetric key object.");
            }

            ApplyChaCha20Poly1305Mask(token, lastSequenceNumber, signingKey);

            using (var hash = SHA256.Create())
            {
                signingKey = hash.ComputeHash(signingKey);
            }

            // Utils.Trace($"SigningKey={Utils.ToHexString(signingKey)}");

            int signatureLength = 16;

            var plaintext = dataToEncrypt.Array;
            int headerSize = dataToEncrypt.Offset;
            int plainTextLength = dataToEncrypt.Offset + dataToEncrypt.Count - signatureLength;

            // Utils.Trace($"OUT={headerSize}|{plainTextLength}|{signatureLength}|[{plainTextLength + signatureLength}]");

            Poly1305 poly = new Poly1305();

            poly.Init(new KeyParameter(signingKey, 0, signingKey.Length));
            poly.BlockUpdate(plaintext, 0, plainTextLength);
            int length = poly.DoFinal(plaintext, plainTextLength);

            if (signatureLength != length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    $"Signed data not the expected size. [{plainTextLength + signatureLength} != {length}]");
            }
        }

        /// <summary>
        /// Decrypts a message using a symmetric algorithm.
        /// </summary>
        private static void SymmetricDecryptWithChaCha20Poly1305(
            ChannelToken token,
            uint lastSequenceNumber,
            ArraySegment<byte> dataToDecrypt,
            bool useClientKeys)
        {
            var encryptingKey = (useClientKeys) ? token.ClientEncryptingKey : token.ServerEncryptingKey;

            if (encryptingKey == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Token missing symmetric key object.");
            }

            var iv = (useClientKeys) ? token.ClientInitializationVector : token.ServerInitializationVector;

            if (iv == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Token missing symmetric key object.");
            }

            // Utils.Trace($"DecryptKey={Utils.ToHexString(encryptingKey)}");
            // Utils.Trace($"DecryptIV1={Utils.ToHexString(iv)}");
            ApplyChaCha20Poly1305Mask(token, lastSequenceNumber, iv);
            // Utils.Trace($"DecryptIV2={Utils.ToHexString(iv)}");

            int signatureLength = 16;

            var ciphertext = dataToDecrypt.Array;
            int headerSize = dataToDecrypt.Offset;
            int cipherTextLength = dataToDecrypt.Offset + dataToDecrypt.Count - signatureLength;

            // Utils.Trace($"OUT={headerSize}|{cipherTextLength}|{signatureLength}|[{cipherTextLength + signatureLength}]");

            byte[] mac = new byte[16];
            Buffer.BlockCopy(ciphertext, cipherTextLength, mac, 0, signatureLength);
            // Utils.Trace($"DecryptMAC={Utils.ToHexString(mac)}");

            AeadParameters parameters = new AeadParameters(
                new KeyParameter(encryptingKey),
                signatureLength * 8,
                iv,
                null);

            ChaCha20Poly1305 decryptor = new ChaCha20Poly1305();
            decryptor.Init(false, parameters);
            decryptor.ProcessAadBytes(ciphertext, 0, headerSize);

            var plaintext = new byte[
                decryptor.GetOutputSize(cipherTextLength + signatureLength - headerSize) + headerSize
            ];
            Buffer.BlockCopy(ciphertext, headerSize, plaintext, 0, headerSize);

            int length = decryptor.ProcessBytes(
                ciphertext,
                headerSize,
                cipherTextLength + signatureLength - headerSize,
                plaintext,
                headerSize);
            length += decryptor.DoFinal(plaintext, length + headerSize);

            if (plaintext.Length - headerSize != length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    $"Plain text not the expected size. [{plaintext.Length - headerSize} != {length}]");
            }

            Buffer.BlockCopy(plaintext, 0, ciphertext, 0, cipherTextLength);
        }

        /// <summary>
        /// Encrypts a message using a symmetric algorithm.
        /// </summary>
        private static void SymmetricVerifyWithPoly1305(
            ChannelToken token,
            uint lastSequenceNumber,
            ArraySegment<byte> dataToDecrypt,
            bool useClientKeys)
        {
            var signingKey = (useClientKeys) ? token.ClientSigningKey : token.ServerSigningKey;

            if (signingKey == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Token missing symmetric key object.");
            }

            ApplyChaCha20Poly1305Mask(token, lastSequenceNumber, signingKey);
            // Utils.Trace($"SigningKey={Utils.ToHexString(signingKey)}");

            using (var hash = SHA256.Create())
            {
                signingKey = hash.ComputeHash(signingKey);
            }

            int signatureLength = 16;

            var plaintext = dataToDecrypt.Array;
            int headerSize = dataToDecrypt.Offset;
            int plainTextLength = dataToDecrypt.Offset + dataToDecrypt.Count - signatureLength;

            // Utils.Trace($"OUT={headerSize}|{plainTextLength}|{signatureLength}|[{plainTextLength + signatureLength}]");

            Poly1305 poly = new Poly1305();

            poly.Init(new KeyParameter(signingKey, 0, signingKey.Length));
            poly.BlockUpdate(plaintext, 0, plainTextLength);

            byte[] mac = new byte[poly.GetMacSize()];
            int length = poly.DoFinal(mac, 0);

            if (signatureLength != length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    $"Signed data not the expected size. [{plainTextLength + signatureLength} != {length}]");
            }

            for (int ii = 0; ii < mac.Length; ii++)
            {
                if (mac[ii] != plaintext[plainTextLength + ii])
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, $"Invaid MAC on data.");
                }
            }
        }

        private static void ApplyChaCha20Poly1305Mask(ChannelToken token, uint lastSequenceNumber, byte[] iv)
        {
            iv[0] ^= (byte)((token.TokenId & 0x000000FF));
            iv[1] ^= (byte)((token.TokenId & 0x0000FF00) >> 8);
            iv[2] ^= (byte)((token.TokenId & 0x00FF0000) >> 16);
            iv[3] ^= (byte)((token.TokenId & 0xFF000000) >> 24);
            iv[4] ^= (byte)((lastSequenceNumber & 0x000000FF));
            iv[5] ^= (byte)((lastSequenceNumber & 0x0000FF00) >> 8);
            iv[6] ^= (byte)((lastSequenceNumber & 0x00FF0000) >> 16);
            iv[7] ^= (byte)((lastSequenceNumber & 0xFF000000) >> 24);
        }
#endif

        private static readonly byte[] s_hkdfClientLabel = Encoding.UTF8.GetBytes("opcua-client");
        private static readonly byte[] s_hkdfServerLabel = Encoding.UTF8.GetBytes("opcua-server");
        private static readonly byte[] s_hkdfAes128SignOnlyKeyLength = BitConverter.GetBytes(
            (ushort)32);
        private static readonly byte[] s_hkdfAes256SignOnlyKeyLength = BitConverter.GetBytes(
            (ushort)48);
        private static readonly byte[] s_hkdfAes128SignAndEncryptKeyLength = BitConverter.GetBytes(
            (ushort)64);
        private static readonly byte[] s_hkdfAes256SignAndEncryptKeyLength = BitConverter.GetBytes(
            (ushort)96);
        private static readonly byte[] s_hkdfChaCha20Poly1305KeyLength = BitConverter.GetBytes(
            (ushort)76);
        private int m_signatureKeySize;
        private int m_encryptionKeySize;
    }
}
