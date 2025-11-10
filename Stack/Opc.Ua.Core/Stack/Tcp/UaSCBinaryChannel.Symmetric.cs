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
        /// Indicates that an explicit signature is not present.
        /// </summary>
        private bool AuthenticatedEncryption { get; set; }

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
            AuthenticatedEncryption = false;

            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.Basic128Rsa15:
                    SymmetricSignatureSize = 20;
                    m_signatureKeySize = 16;
                    m_encryptionKeySize = 16;
                    EncryptionBlockSize = 16;
                    break;
                case SecurityPolicies.Basic256:
                    SymmetricSignatureSize = 20;
                    m_signatureKeySize = 24;
                    m_encryptionKeySize = 32;
                    EncryptionBlockSize = 16;
                    break;
                case SecurityPolicies.Basic256Sha256:
                    SymmetricSignatureSize = 32;
                    m_signatureKeySize = 32;
                    m_encryptionKeySize = 32;
                    EncryptionBlockSize = 16;
                    break;
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                    SymmetricSignatureSize = 32;
                    m_signatureKeySize = 32;
                    m_encryptionKeySize = 16;
                    EncryptionBlockSize = 16;
                    break;
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                    SymmetricSignatureSize = 32;
                    m_signatureKeySize = 32;
                    m_encryptionKeySize = 32;
                    EncryptionBlockSize = 16;
                    break;
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_brainpoolP256r1:
                    SymmetricSignatureSize = 32;
                    m_signatureKeySize = 32;
                    m_encryptionKeySize = 16;
                    EncryptionBlockSize = 16;
                    break;
                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
                    AuthenticatedEncryption = true;
                    SymmetricSignatureSize = 16;
                    m_signatureKeySize = 32;
                    m_encryptionKeySize = 32;
                    EncryptionBlockSize = 12;
                    break;
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                    SymmetricSignatureSize = 48;
                    m_signatureKeySize = 48;
                    m_encryptionKeySize = 32;
                    EncryptionBlockSize = 16;
                    break;
                default:
                    SymmetricSignatureSize = 0;
                    m_signatureKeySize = 0;
                    m_encryptionKeySize = 0;
                    EncryptionBlockSize = 1;
                    break;
            }
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
            HashAlgorithmName algorithmName,
            byte[] salt,
            ChannelToken token,
            bool isServer)
        {
            int length = m_signatureKeySize + m_encryptionKeySize + EncryptionBlockSize;

            byte[] output = m_localNonce.DeriveKey(m_remoteNonce, salt, algorithmName, length);

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
#endif

        /// <summary>
        /// Computes the keys for a token.
        /// </summary>
        protected void ComputeKeys(ChannelToken token)
        {
            if (SecurityMode == MessageSecurityMode.None)
            {
                return;
            }

            byte[] serverSecret = token.ServerNonce;
            byte[] clientSecret = token.ClientNonce;

            HashAlgorithmName algorithmName = HashAlgorithmName.SHA256;
            switch (SecurityPolicyUri)
            {
#if ECC_SUPPORT
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_brainpoolP256r1:
                {
                    algorithmName = HashAlgorithmName.SHA256;
                    byte[] length =
                        SecurityMode == MessageSecurityMode.Sign
                            ? s_hkdfAes128SignOnlyKeyLength
                            : s_hkdfAes128SignAndEncryptKeyLength;
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
                    m_logger.LogDebug("Length={Length}", Utils.ToHexString(length));
                    m_logger.LogDebug("ClientSecret={ClientSecret}", Utils.ToHexString(clientSecret));
                    m_logger.LogDebug("ServerSecret={ServerSecret}", Utils.ToHexString(clientSecret));
                    m_logger.LogDebug("ServerSalt={ServerSalt}", Utils.ToHexString(serverSalt));
                    m_logger.LogDebug("ClientSalt={ClientSalt}", Utils.ToHexString(clientSalt));
#endif

                    DeriveKeysWithHKDF(algorithmName, serverSalt, token, true);
                    DeriveKeysWithHKDF(algorithmName, clientSalt, token, false);
                    break;
                }
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                {
                    algorithmName = HashAlgorithmName.SHA384;
                    byte[] length =
                        SecurityMode == MessageSecurityMode.Sign
                            ? s_hkdfAes256SignOnlyKeyLength
                            : s_hkdfAes256SignAndEncryptKeyLength;
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
                    m_logger.LogDebug("Length={Length}", Utils.ToHexString(length));
                    m_logger.LogDebug("ClientSecret={ClientSecret}", Utils.ToHexString(clientSecret));
                    m_logger.LogDebug("ServerSecret={ServerSecret}", Utils.ToHexString(clientSecret));
                    m_logger.LogDebug("ServerSalt={ServerSalt}", Utils.ToHexString(serverSalt));
                    m_logger.LogDebug("ClientSalt={ClientSalt}", Utils.ToHexString(clientSalt));
#endif

                    DeriveKeysWithHKDF(algorithmName, serverSalt, token, true);
                    DeriveKeysWithHKDF(algorithmName, clientSalt, token, false);
                    break;
                }
                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
                {
                    algorithmName = HashAlgorithmName.SHA256;
                    byte[] length = s_hkdfChaCha20Poly1305KeyLength;
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
                    m_logger.LogDebug("Length={Length}", Utils.ToHexString(length));
                    m_logger.LogDebug("ClientSecret={ClientSecret}", Utils.ToHexString(clientSecret));
                    m_logger.LogDebug("ServerSecret={ServerSecret}", Utils.ToHexString(clientSecret));
                    m_logger.LogDebug("ServerSalt={ServerSalt}", Utils.ToHexString(serverSalt));
                    m_logger.LogDebug("ClientSalt={ClientSalt}", Utils.ToHexString(clientSalt));
#endif

                    DeriveKeysWithHKDF(algorithmName, serverSalt, token, true);
                    DeriveKeysWithHKDF(algorithmName, clientSalt, token, false);
                    break;
                }
#endif
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                    algorithmName = HashAlgorithmName.SHA1;
                    goto default;
                default:
                    DeriveKeysWithPSHA(algorithmName, serverSecret, clientSecret, token, false);
                    DeriveKeysWithPSHA(algorithmName, clientSecret, serverSecret, token, true);
                    break;
            }

            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP256r1:
                case SecurityPolicies.ECC_brainpoolP384r1:
                    // create encryptors.
                    var aesCbcEncryptorProvider = Aes.Create();
                    aesCbcEncryptorProvider.Mode = CipherMode.CBC;
                    aesCbcEncryptorProvider.Padding = PaddingMode.None;
                    aesCbcEncryptorProvider.Key = token.ClientEncryptingKey;
                    aesCbcEncryptorProvider.IV = token.ClientInitializationVector;
                    token.ClientEncryptor = aesCbcEncryptorProvider;

                    var aesCbcDecryptorProvider = Aes.Create();
                    aesCbcDecryptorProvider.Mode = CipherMode.CBC;
                    aesCbcDecryptorProvider.Padding = PaddingMode.None;
                    aesCbcDecryptorProvider.Key = token.ServerEncryptingKey;
                    aesCbcDecryptorProvider.IV = token.ServerInitializationVector;
                    token.ServerEncryptor = aesCbcDecryptorProvider;
                    break;
                default:
                    // TODO: is this even legal or should we throw? What are the implications
                    token.ClientEncryptor = null;
                    token.ServerEncryptor = null;
                    break;
            }

            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                    token.ServerHmac = new HMACSHA1(token.ServerSigningKey);
                    token.ClientHmac = new HMACSHA1(token.ClientSigningKey);
                    break;
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_brainpoolP256r1:
                    token.ServerHmac = new HMACSHA256(token.ServerSigningKey);
                    token.ClientHmac = new HMACSHA256(token.ClientSigningKey);
                    break;
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                    token.ServerHmac = new HMACSHA384(token.ServerSigningKey);
                    token.ClientHmac = new HMACSHA384(token.ClientSigningKey);
                    break;
                default:
                    // TODO: is this even legal or should we throw? What are the implications
                    token.ServerHmac = null;
                    token.ClientHmac = null;
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
                int maxPayloadSize =
                    maxPlainTextSize -
                    SymmetricSignatureSize -
                    1 -
                    TcpMessageLimits.SequenceHeaderSize;
                const int headerSize = TcpMessageLimits.SymmetricHeaderSize +
                    TcpMessageLimits.SequenceHeaderSize;

                // no padding byte.
                if (AuthenticatedEncryption)
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
                        !AuthenticatedEncryption)
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
                        !AuthenticatedEncryption)
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
                        if (AuthenticatedEncryption)
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
                        !AuthenticatedEncryption) ||
                        (SecurityMode != MessageSecurityMode.None && AuthenticatedEncryption))
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

            if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
            {
                // decrypt the message.
                Decrypt(
                    token,
                    new ArraySegment<byte>(
                        buffer.Array,
                        buffer.Offset + headerSize,
                        buffer.Count - headerSize),
                    isRequest);
            }

            int paddingCount = 0;
            if (SecurityMode != MessageSecurityMode.None)
            {
                int signatureStart = buffer.Offset + buffer.Count - SymmetricSignatureSize;

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
                            buffer.Count - SymmetricSignatureSize),
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

            // extract request id and sequence number.
            sequenceNumber = decoder.ReadUInt32(null);
            requestId = decoder.ReadUInt32(null);

            // return an the data contained in the message.
            int startOfBody =
                buffer.Offset +
                TcpMessageLimits.SymmetricHeaderSize +
                TcpMessageLimits.SequenceHeaderSize;
            int sizeOfBody =
                buffer.Count -
                TcpMessageLimits.SymmetricHeaderSize -
                TcpMessageLimits.SequenceHeaderSize -
                paddingCount -
                SymmetricSignatureSize;

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
        /// Decrypts the data in a buffer using symmetric encryption.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        protected void Encrypt(
            ChannelToken token,
            ArraySegment<byte> dataToEncrypt,
            bool useClientKeys)
        {
            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.None:
                    break;
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP256r1:
                case SecurityPolicies.ECC_brainpoolP384r1:
                    SymmetricEncrypt(token, dataToEncrypt, useClientKeys);
                    break;

#if CURVE25519
                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
                {
                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
                    {
                        // narowing conversion can safely be done on m_localSequenceNumber
                        SymmetricEncryptWithChaCha20Poly1305(
                            token,
                            (uint)m_localSequenceNumber,
                            dataToEncrypt,
                            useClientKeys);
                        break;
                    }
                    // narowing conversion can safely be done on m_localSequenceNumber
                    SymmetricSignWithPoly1305(token, (uint)m_localSequenceNumber, dataToEncrypt, useClientKeys);
                    break;
                }
#endif
                default:
                    throw new NotSupportedException(SecurityPolicyUri);
            }
        }

        /// <summary>
        /// Decrypts the data in a buffer using symmetric encryption.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        protected void Decrypt(
            ChannelToken token,
            ArraySegment<byte> dataToDecrypt,
            bool useClientKeys)
        {
            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.None:
                    break;
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP256r1:
                case SecurityPolicies.ECC_brainpoolP384r1:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                    SymmetricDecrypt(token, dataToDecrypt, useClientKeys);
                    break;

#if CURVE25519
                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
                {
                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
                    {
                        SymmetricDecryptWithChaCha20Poly1305(
                            token,
                            m_remoteSequenceNumber,
                            dataToDecrypt,
                            useClientKeys);
                        break;
                    }

                    SymmetricVerifyWithPoly1305(token, m_remoteSequenceNumber, dataToDecrypt, useClientKeys);
                    break;
                }
#endif

                default:
                    throw new NotSupportedException(SecurityPolicyUri);
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
            HMAC hmac = useClientKeys ? token.ClientHmac : token.ServerHmac;

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
            HMAC hmac = useClientKeys ? token.ClientHmac : token.ServerHmac;
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
            HMAC hmac = useClientKeys ? token.ClientHmac : token.ServerHmac;

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
            // get HMAC object.
            HMAC hmac = useClientKeys ? token.ClientHmac : token.ServerHmac;

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

        /// <summary>
        /// Encrypts a message using a symmetric algorithm.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void SymmetricEncrypt(
            ChannelToken token,
            ArraySegment<byte> dataToEncrypt,
            bool useClientKeys)
        {
            SymmetricAlgorithm encryptingKey =
                (useClientKeys ? token.ClientEncryptor : token.ServerEncryptor)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Token missing symmetric key object.");

            using ICryptoTransform encryptor = encryptingKey.CreateEncryptor();
            byte[] blockToEncrypt = dataToEncrypt.Array;

            int start = dataToEncrypt.Offset;
            int count = dataToEncrypt.Count;

            if (count % encryptor.InputBlockSize != 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Input data is not an even number of encryption blocks.");
            }
            encryptor.TransformBlock(blockToEncrypt, start, count, blockToEncrypt, start);
        }

        /// <summary>
        /// Decrypts a message using a symmetric algorithm.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void SymmetricDecrypt(
            ChannelToken token,
            ArraySegment<byte> dataToDecrypt,
            bool useClientKeys)
        {
            // get the decrypting key.
            SymmetricAlgorithm decryptingKey =
                (useClientKeys ? token.ClientEncryptor : token.ServerEncryptor)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Token missing symmetric key object.");

            using ICryptoTransform decryptor = decryptingKey.CreateDecryptor();
            byte[] blockToDecrypt = dataToDecrypt.Array;

            int start = dataToDecrypt.Offset;
            int count = dataToDecrypt.Count;

            if (count % decryptor.InputBlockSize != 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Input data is not an even number of encryption blocks.");
            }

            decryptor.TransformBlock(blockToDecrypt, start, count, blockToDecrypt, start);
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

#if ECC_SUPPORT
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
#endif
        private int m_signatureKeySize;
        private int m_encryptionKeySize;
    }
}
