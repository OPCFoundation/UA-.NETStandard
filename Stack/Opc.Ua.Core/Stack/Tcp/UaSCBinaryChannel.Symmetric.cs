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
                Lifetime = Quotas.SecurityTokenLifetime,
                ServerCertificate = ServerCertificate?.RawData,
                ClientCertificate = ClientCertificate?.RawData
            };

            Utils.LogInfo(
                "ChannelId {0}: New Token created. CreatedAt={1:HH:mm:ss.fff}-{2}. Lifetime={3}.",
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

            // create the secure channel secret if required.
            if (CurrentToken == null)
            {
                token.CreateSecureChannelSecret();
            }
            else
            {
                token.SecureChannelSecret = CurrentToken.SecureChannelSecret;
            }

            if (token.SecureChannelSecret != null)
            {
                System.Console.WriteLine($"SecureChannelSecret={Utils.ToHexString(token.SecureChannelSecret).Substring(0, 8)}");
            }

            Utils.SilentDispose(PreviousToken);
            PreviousToken = CurrentToken;
            CurrentToken = token;
            RenewedToken = null;

            OnTokenActivated?.Invoke(token, PreviousToken);

            Utils.LogInfo(
                "ChannelId {0}: Token #{1} activated. CreatedAt={2:HH:mm:ss.fff}-{3}. Lifetime={4}.",
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
            Utils.LogInfo(
                "ChannelId {0}: Renewed Token #{1} set. CreatedAt={2:HH:mm:ss.fff}-{3}. Lifetime={4}.",
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
            var info = SecurityPolicies.GetInfo(SecurityPolicyUri);

            SymmetricSignatureSize = info.SymmetricSignatureLength;
            m_signatureKeySize = info.DerivedSignatureKeyLength;
            m_encryptionKeySize = info.SymmetricEncryptionKeyLength;
            EncryptionBlockSize = (info.InitializationVectorLength != 0) ? info.InitializationVectorLength : 1;
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
                token.Secret,
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

                if (token.ServerSigningKey?.Length > 0)
                {
                    System.Console.WriteLine($"ServerSigningKey {Utils.ToHexString(token.ServerSigningKey).Substring(0, 8)}");
                }
            }
            else
            {
                token.ClientSigningKey = signingKey;
                token.ClientEncryptingKey = encryptingKey;
                token.ClientInitializationVector = iv;

                if (token.ClientSigningKey?.Length > 0)
                {
                    System.Console.WriteLine($"ClientSigningKey {Utils.ToHexString(token.ClientSigningKey).Substring(0, 8)}");
                }
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
                    token.Secret = m_localNonce.GenerateSecret(m_remoteNonce, token.PreviousSecret);

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
                    Utils.LogTrace("Length={0}", Utils.ToHexString(length));
                    Utils.LogTrace("ClientSecret={0}", Utils.ToHexString(clientSecret));
                    Utils.LogTrace("ServerSecret={0}", Utils.ToHexString(clientSecret));
                    Utils.LogTrace("ServerSalt={0}", Utils.ToHexString(serverSalt));
                    Utils.LogTrace("ClientSalt={0}", Utils.ToHexString(clientSalt));
#endif

                    DeriveKeysWithHKDF(token, serverSalt, true);
                    DeriveKeysWithHKDF(token, clientSalt, false);
                    break;
                }
#endif
                default:
                case KeyDerivationAlgorithm.PSha1:
                case KeyDerivationAlgorithm.PSha256:
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
                    (SecurityMode != MessageSecurityMode.SignAndEncrypt || token.SecurityPolicy.NoSymmetricEncryptionPadding)
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
                Utils.LogInfo(
                    "ChannelId {0}: Token #{1} activated forced.",
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
                this.SecurityMode == MessageSecurityMode.Sign);
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
                this.SecurityMode == MessageSecurityMode.Sign);
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

                var message = new StringBuilder();
                message.AppendLine("Channel{0}: Could not validate signature.")
                    .AppendLine("ChannelId={1}, TokenId={2}, MessageType={3}, Length={4}")
                    .AppendLine("ExpectedSignature={5}")
                    .AppendLine("ActualSignature={6}");
                Utils.LogError(
                    message.ToString(),
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
