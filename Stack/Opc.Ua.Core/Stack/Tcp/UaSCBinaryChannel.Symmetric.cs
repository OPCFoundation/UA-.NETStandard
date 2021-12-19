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
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Bindings
{
    public partial class UaSCUaBinaryChannel
    {
        #region Token Handling Members
        /// <summary>
        /// Returns the current security token.
        /// </summary>
        protected internal ChannelToken CurrentToken => m_currentToken;

        /// <summary>
        /// Returns the current security token.
        /// </summary>
        protected ChannelToken PreviousToken => m_previousToken;

        /// <summary>
        /// Returns the renewed but not yet activated token.
        /// </summary>
        protected ChannelToken RenewedToken => m_renewedToken;

        /// <summary>
        /// Creates a new token.
        /// </summary>
        protected ChannelToken CreateToken()
        {
            ChannelToken token = new ChannelToken();

            token.ChannelId = m_channelId;
            token.TokenId = 0;
            token.CreatedAt = DateTime.UtcNow;
            token.Lifetime = (int)Quotas.SecurityTokenLifetime;

            Utils.LogInfo("ChannelId {0}: Token #{1} created. CreatedAt={2:HH:mm:ss.fff}. Lifetime={3}.",
                Id, token.TokenId, token.CreatedAt, token.Lifetime);

            return token;
        }

        /// <summary>
        /// Activates a new token.
        /// </summary>
        protected void ActivateToken(ChannelToken token)
        {
            // compute the keys for the token.
            ComputeKeys(token);

            m_previousToken = m_currentToken;
            m_currentToken = token;
            m_renewedToken = null;

            Utils.LogInfo("ChannelId {0}: Token #{1} activated. CreatedAt={2:HH:mm:ss.fff}. Lifetime={3}.", Id, token.TokenId, token.CreatedAt, token.Lifetime);
        }

        /// <summary>
        /// Sets the renewed token
        /// </summary>
        protected void SetRenewedToken(ChannelToken token)
        {
            m_renewedToken = token;
            Utils.LogInfo("ChannelId {0}: Renewed Token #{1} set. CreatedAt={2:HH:mm:ss.fff}. Lifetime ={3}.", Id, token.TokenId, token.CreatedAt, token.Lifetime);
        }

        /// <summary>
        /// Discards the tokens.
        /// </summary>
        protected void DiscardTokens()
        {
            m_previousToken = null;
            m_currentToken = null;
        }
        #endregion

        #region Symmetric Cryptography Functions
        /// <summary>
        /// Indicates that an explicit signature is not present.
        /// </summary>        
        private bool UseAuthenticatedEncryption
        {
            get; set;
        }

        /// <summary>
        /// The byte length of the MAC (a.k.a signature) attached to each message.
        /// </summary>
        private int SymmetricSignatureSize => m_hmacHashSize;

        /// <summary>
        /// The byte length the encryption blocks.
        /// </summary>
        private int EncryptionBlockSize => m_encryptionBlockSize;

        /// <summary>
        /// Calculates the symmetric key sizes based on the current security policy.
        /// </summary>
        protected void CalculateSymmetricKeySizes()
        {
            UseAuthenticatedEncryption = false;

            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.Basic128Rsa15:
                {
                    m_hmacHashSize = 20;
                    m_signatureKeySize = 16;
                    m_encryptionKeySize = 16;
                    m_encryptionBlockSize = 16;
                    break;
                }

                case SecurityPolicies.Basic256:
                {
                    m_hmacHashSize = 20;
                    m_signatureKeySize = 24;
                    m_encryptionKeySize = 32;
                    m_encryptionBlockSize = 16;
                    break;
                }

                case SecurityPolicies.Basic256Sha256:
                {
                    m_hmacHashSize = 32;
                    m_signatureKeySize = 32;
                    m_encryptionKeySize = 32;
                    m_encryptionBlockSize = 16;
                    break;
                }

                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                {
                    m_hmacHashSize = 32;
                    m_signatureKeySize = 32;
                    m_encryptionKeySize = 16;
                    m_encryptionBlockSize = 16;
                    break;
                }

                case SecurityPolicies.Aes256_Sha256_RsaPss:
                {
                    m_hmacHashSize = 32;
                    m_signatureKeySize = 32;
                    m_encryptionKeySize = 32;
                    m_encryptionBlockSize = 16;
                    break;
                }

                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_brainpoolP256r1:
                {
                    m_hmacHashSize = 32;
                    m_signatureKeySize = 32;
                    m_encryptionKeySize = 16;
                    m_encryptionBlockSize = 16;
                    break;
                }

                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
                {
                    UseAuthenticatedEncryption = true;
                    m_hmacHashSize = 16;
                    m_signatureKeySize = 32;
                    m_encryptionKeySize = 32;
                    m_encryptionBlockSize = 12;
                    break;
                }

                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                {
                    m_hmacHashSize = 48;
                    m_signatureKeySize = 48;
                    m_encryptionKeySize = 32;
                    m_encryptionBlockSize = 16;
                    break;
                }

                default:
                case SecurityPolicies.None:
                {
                    m_hmacHashSize = 0;
                    m_signatureKeySize = 0;
                    m_encryptionKeySize = 0;
                    m_encryptionBlockSize = 1;
                    break;
                }
            }
        }
        private void DeriveKeysWithPSHA(HashAlgorithmName algorithmName, byte[] secret, byte[] seed, ChannelToken token, bool isServer)
        {
            int length = m_signatureKeySize + m_encryptionKeySize + m_encryptionBlockSize;

            using (var hmac = Utils.CreateHMAC(algorithmName, secret))
            {
                var output = Utils.PSHA(hmac, null, seed, 0, length);

                var signingKey = new byte[m_signatureKeySize];
                var encryptingKey = new byte[m_encryptionKeySize];
                var iv = new byte[m_encryptionBlockSize];

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
        }

#if ECC_SUPPORT
        private void DeriveKeysWithHKDF(HashAlgorithmName algorithmName, byte[] salt, ChannelToken token, bool isServer)
        {
            int length = m_signatureKeySize + m_encryptionKeySize + m_encryptionBlockSize;

            var output = m_localNonce.DeriveKey(m_remoteNonce, salt, algorithmName, length);

            var signingKey = new byte[m_signatureKeySize];
            var encryptingKey = new byte[m_encryptionKeySize];
            var iv = new byte[m_encryptionBlockSize];

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

        static readonly byte[] s_HkdfClientLabel = new UTF8Encoding().GetBytes("opcua-client");
        static readonly byte[] s_HkdfServerLabel = new UTF8Encoding().GetBytes("opcua-server");
        static readonly byte[] s_HkdfAes128SignOnlyKeyLength = BitConverter.GetBytes((ushort)32);
        static readonly byte[] s_HkdfAes256SignOnlyKeyLength = BitConverter.GetBytes((ushort)48);
        static readonly byte[] s_HkdfAes128SignAndEncryptKeyLength = BitConverter.GetBytes((ushort)64);
        static readonly byte[] s_HkdfAes256SignAndEncryptKeyLength = BitConverter.GetBytes((ushort)96);
        static readonly byte[] s_HkdfChaCha20Poly1305KeyLength = BitConverter.GetBytes((ushort)76);
#endif

        /// <summary>
        /// Computes the keys for a token.
        /// </summary>
        [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
            Justification = "SHA1 required for deprecated profiles")]
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
                default:
                {
                    DeriveKeysWithPSHA(algorithmName, serverSecret, clientSecret, token, false);
                    DeriveKeysWithPSHA(algorithmName, clientSecret, serverSecret, token, true);
                    break;
                }
#if ECC_SUPPORT
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_brainpoolP256r1:
                {
                    algorithmName = HashAlgorithmName.SHA256;
                    var length = (SecurityMode == MessageSecurityMode.Sign) ? s_HkdfAes128SignOnlyKeyLength : s_HkdfAes128SignAndEncryptKeyLength;
                    var serverSalt = Utils.Append(length, s_HkdfServerLabel, serverSecret, clientSecret);
                    var clientSalt = Utils.Append(length, s_HkdfClientLabel, clientSecret, serverSecret);

                    Utils.LogTrace("Length={0}", Utils.ToHexString(length));
                    Utils.LogTrace("ClientSecret={0}", Utils.ToHexString(clientSecret));
                    Utils.LogTrace("ServerSecret={0}", Utils.ToHexString(clientSecret));
                    Utils.LogTrace("ServerSalt={0}", Utils.ToHexString(serverSalt));
                    Utils.LogTrace("ClientSalt={0}", Utils.ToHexString(clientSalt));

                    DeriveKeysWithHKDF(algorithmName, serverSalt, token, true);
                    DeriveKeysWithHKDF(algorithmName, clientSalt, token, false);
                    break;
                }

                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                {
                    algorithmName = HashAlgorithmName.SHA384;
                    var length = (SecurityMode == MessageSecurityMode.Sign) ? s_HkdfAes256SignOnlyKeyLength : s_HkdfAes256SignAndEncryptKeyLength;
                    var serverSalt = Utils.Append(length, s_HkdfServerLabel, serverSecret, clientSecret);
                    var clientSalt = Utils.Append(length, s_HkdfClientLabel, clientSecret, serverSecret);

                    Utils.LogTrace("Length={0}", Utils.ToHexString(length));
                    Utils.LogTrace("ClientSecret={0}", Utils.ToHexString(clientSecret));
                    Utils.LogTrace("ServerSecret={0}", Utils.ToHexString(clientSecret));
                    Utils.LogTrace("ServerSalt={0}", Utils.ToHexString(serverSalt));
                    Utils.LogTrace("ClientSalt={0}", Utils.ToHexString(clientSalt));

                    DeriveKeysWithHKDF(algorithmName, serverSalt, token, true);
                    DeriveKeysWithHKDF(algorithmName, clientSalt, token, false);
                    break;
                }

                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
                {
                    algorithmName = HashAlgorithmName.SHA256;
                    var length = s_HkdfChaCha20Poly1305KeyLength;
                    var serverSalt = Utils.Append(length, s_HkdfServerLabel, serverSecret, clientSecret);
                    var clientSalt = Utils.Append(length, s_HkdfClientLabel, clientSecret, serverSecret);

                    Utils.LogTrace("Length={0}", Utils.ToHexString(length));
                    Utils.LogTrace("ClientSecret={0}", Utils.ToHexString(clientSecret));
                    Utils.LogTrace("ServerSecret={0}", Utils.ToHexString(clientSecret));
                    Utils.LogTrace("ServerSalt={0}", Utils.ToHexString(serverSalt));
                    Utils.LogTrace("ClientSalt={0}", Utils.ToHexString(clientSalt));

                    DeriveKeysWithHKDF(algorithmName, serverSalt, token, true);
                    DeriveKeysWithHKDF(algorithmName, clientSalt, token, false);
                    break;
                }
#endif
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                {
                    algorithmName = HashAlgorithmName.SHA1;
                    DeriveKeysWithPSHA(algorithmName, serverSecret, clientSecret, token, false);
                    DeriveKeysWithPSHA(algorithmName, clientSecret, serverSecret, token, true);
                    break;
                }
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
                {
                    // create encryptors. 
                    SymmetricAlgorithm AesCbcEncryptorProvider = Aes.Create();
                    AesCbcEncryptorProvider.Mode = CipherMode.CBC;
                    AesCbcEncryptorProvider.Padding = PaddingMode.None;
                    AesCbcEncryptorProvider.Key = token.ClientEncryptingKey;
                    AesCbcEncryptorProvider.IV = token.ClientInitializationVector;
                    token.ClientEncryptor = AesCbcEncryptorProvider;

                    SymmetricAlgorithm AesCbcDecryptorProvider = Aes.Create();
                    AesCbcDecryptorProvider.Mode = CipherMode.CBC;
                    AesCbcDecryptorProvider.Padding = PaddingMode.None;
                    AesCbcDecryptorProvider.Key = token.ServerEncryptingKey;
                    AesCbcDecryptorProvider.IV = token.ServerInitializationVector;
                    token.ServerEncryptor = AesCbcDecryptorProvider;
                    break;
                }

                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
                {
                    break;
                }

                default:
                case SecurityPolicies.None:
                {
                    break;
                }
            }

            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                {
                    token.ServerHmac = new HMACSHA1(token.ServerSigningKey);
                    token.ClientHmac = new HMACSHA1(token.ClientSigningKey);
                    break;
                }

                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_brainpoolP256r1:
                {
                    token.ServerHmac = new HMACSHA256(token.ServerSigningKey);
                    token.ClientHmac = new HMACSHA256(token.ClientSigningKey);
                    break;
                }

                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                {
                    token.ServerHmac = new HMACSHA384(token.ServerSigningKey);
                    token.ClientHmac = new HMACSHA384(token.ClientSigningKey);
                    break;
                }

                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
                {
                    break;
                }

                default:
                case SecurityPolicies.None:
                {
                    break;
                }
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
                int maxPayloadSize = maxPlainTextSize - SymmetricSignatureSize - 1 - TcpMessageLimits.SequenceHeaderSize;
                int headerSize = TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize;

                // no padding byte.
                if (UseAuthenticatedEncryption)
                {
                    maxPayloadSize++;
                }

                // write the body to stream.
                ArraySegmentStream ostrm = new ArraySegmentStream(
                    BufferManager,
                    SendBufferSize,
                    headerSize,
                    maxPayloadSize);

                // check for encodeable body.
                IEncodeable encodeable = messageBody as IEncodeable;

                if (encodeable != null)
                {
                    // debug code used to verify that message aborts are handled correctly.
                    // int maxMessageSize = Quotas.MessageContext.MaxMessageSize;
                    // Quotas.MessageContext.MaxMessageSize = Int32.MaxValue;

                    BinaryEncoder.EncodeMessage(encodeable, ostrm, Quotas.MessageContext);

                    // Quotas.MessageContext.MaxMessageSize = maxMessageSize;
                }

                // check for raw bytes.
                ArraySegment<byte>? rawBytes = messageBody as ArraySegment<byte>?;

                if (rawBytes != null)
                {
                    BinaryEncoder encoder = new BinaryEncoder(ostrm, Quotas.MessageContext);
                    encoder.WriteRawBytes(rawBytes.Value.Array, rawBytes.Value.Offset, rawBytes.Value.Count);
                    encoder.Close();
                }

                chunksToProcess = ostrm.GetBuffers("WriteSymmetricMessage");

                // ensure there is at least one chunk.
                if (chunksToProcess.Count == 0)
                {
                    byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "WriteSymmetricMessage");
                    chunksToProcess.Add(new ArraySegment<byte>(buffer, 0, 0));
                }

                BufferCollection chunksToSend = new BufferCollection(chunksToProcess.Capacity);

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

                    MemoryStream strm = new MemoryStream(chunkToProcess.Array, 0, SendBufferSize);
                    BinaryEncoder encoder = new BinaryEncoder(strm, Quotas.MessageContext);

                    // check if the message needs to be aborted.
                    if (MessageLimitsExceeded(isRequest, messageSize + chunkToProcess.Count - headerSize, ii + 1))
                    {
                        encoder.WriteUInt32(null, messageType | TcpMessageType.Abort);

                        // replace the body in the chunk with an error message.
                        BinaryEncoder errorEncoder = new BinaryEncoder(
                            chunkToProcess.Array,
                            chunkToProcess.Offset,
                            chunkToProcess.Count,
                            Quotas.MessageContext);

                        WriteErrorMessageBody(errorEncoder, (isRequest) ? StatusCodes.BadRequestTooLarge : StatusCodes.BadResponseTooLarge);

                        int size = errorEncoder.Close();
                        chunkToProcess = new ArraySegment<byte>(chunkToProcess.Array, chunkToProcess.Offset, size);

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

                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt && !UseAuthenticatedEncryption)
                    {
                        // reserve one byte for the padding size.
                        count++;

                        if (count % EncryptionBlockSize != 0)
                        {
                            padding = EncryptionBlockSize - (count % EncryptionBlockSize);
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
                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt && !UseAuthenticatedEncryption)
                    {
                        for (int jj = 0; jj <= padding; jj++)
                        {
                            encoder.WriteByte(null, (byte)padding);
                        }
                    }

                    // calculate and write signature.
                    if (SecurityMode != MessageSecurityMode.None)
                    {
                        if (UseAuthenticatedEncryption)
                        {
                            strm.Seek(SymmetricSignatureSize, SeekOrigin.Current);
                        }
                        else
                        {
                            byte[] signature = Sign(token, new ArraySegment<byte>(chunkToProcess.Array, 0, encoder.Position), isRequest);

                            if (signature != null)
                            {
                                encoder.WriteRawBytes(signature, 0, signature.Length);
                            }
                        }
                    }

                    if ((SecurityMode == MessageSecurityMode.SignAndEncrypt && !UseAuthenticatedEncryption) ||
                        (SecurityMode != MessageSecurityMode.None && UseAuthenticatedEncryption))
                    {
                        // encrypt the data.
                        ArraySegment<byte> dataToEncrypt = new ArraySegment<byte>(chunkToProcess.Array, TcpMessageLimits.SymmetricHeaderSize, encoder.Position - TcpMessageLimits.SymmetricHeaderSize);
                        Encrypt(token, dataToEncrypt, isRequest);
                    }

                    // add the header into chunk.
                    chunksToSend.Add(new ArraySegment<byte>(chunkToProcess.Array, 0, encoder.Position));
                }

                // ensure the buffers don't get cleaned up on exit.
                success = true;
                return chunksToSend;
            }
            finally
            {
                if (!success)
                {
                    if (chunksToProcess != null)
                    {
                        chunksToProcess.Release(BufferManager, "WriteSymmetricMessage");
                    }
                }
            }
        }

        /// <summary>
        /// Decrypts and verifies a message chunk.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "messageType"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "messageSize")]
        protected ArraySegment<byte> ReadSymmetricMessage(
            ArraySegment<byte> buffer,
            bool isRequest,
            out ChannelToken token,
            out uint requestId,
            out uint sequenceNumber)
        {
            BinaryDecoder decoder = new BinaryDecoder(buffer.Array, buffer.Offset, buffer.Count, Quotas.MessageContext);

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
            if (RenewedToken != null && CurrentToken.ActivationRequired)
            {
                ActivateToken(RenewedToken);

                Utils.LogInfo("ChannelId {0}: Token #{1} activated forced.", Id, CurrentToken.TokenId);
            }

            // check for valid token.
            ChannelToken currentToken = CurrentToken;

            if (currentToken == null)
            {
                throw new ServiceResultException(StatusCodes.BadSecureChannelClosed);
            }

            // find the token.
            if (currentToken.TokenId != tokenId && PreviousToken != null && PreviousToken.TokenId != tokenId)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "Channel{0}: TokenId is not known. ChanneId={1}, TokenId={2}, CurrentTokenId={3}, PreviousTokenId={4}",
                    Id, channelId,
                    tokenId, currentToken.TokenId,
                    (PreviousToken != null) ? (int)PreviousToken.TokenId : -1);
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
                throw ServiceResultException.Create(StatusCodes.BadTcpSecureChannelUnknown,
                    "Channel{0}: Token #{1} has expired. Lifetime={2:HH:mm:ss.fff}",
                    Id, token.TokenId, token.CreatedAt);
            }

            int headerSize = decoder.Position;

            if ((SecurityMode == MessageSecurityMode.SignAndEncrypt && !UseAuthenticatedEncryption) ||
                (SecurityMode != MessageSecurityMode.None && UseAuthenticatedEncryption))
            {
                // decrypt the message.
                Decrypt(token, new ArraySegment<byte>(buffer.Array, buffer.Offset + headerSize, buffer.Count - headerSize), isRequest);
            }

            if (SecurityMode != MessageSecurityMode.None && !UseAuthenticatedEncryption)
            {
                // extract signature.
                byte[] signature = new byte[SymmetricSignatureSize];

                for (int ii = 0; ii < SymmetricSignatureSize; ii++)
                {
                    signature[ii] = buffer.Array[buffer.Offset + buffer.Count - SymmetricSignatureSize + ii];
                }

                // verify the signature.
                if (!Verify(token, signature, new ArraySegment<byte>(buffer.Array, buffer.Offset, buffer.Count - SymmetricSignatureSize), isRequest))
                {
                    Utils.LogError("ChannelId {0}: Could not verify signature on message.", Id);
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Could not verify the signature on the message.");
                }
            }

            int paddingCount = 0;

            if (SecurityMode == MessageSecurityMode.SignAndEncrypt && !UseAuthenticatedEncryption)
            {
                // verify padding.
                int paddingStart = buffer.Offset + buffer.Count - SymmetricSignatureSize - 1;
                paddingCount = buffer.Array[paddingStart];

                for (int ii = paddingStart - paddingCount; ii < paddingStart; ii++)
                {
                    if (buffer.Array[ii] != paddingCount)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Could not verify the padding in the message.");
                    }
                }

                // add byte for size.
                paddingCount++;
            }

            // extract request id and sequence number.
            sequenceNumber = decoder.ReadUInt32(null);
            requestId = decoder.ReadUInt32(null);

            // return an the data contained in the message.
            int startOfBody = buffer.Offset + TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize;
            int sizeOfBody = buffer.Count - TcpMessageLimits.SymmetricHeaderSize - TcpMessageLimits.SequenceHeaderSize - paddingCount - SymmetricSignatureSize;

            return new ArraySegment<byte>(buffer.Array, startOfBody, sizeOfBody);
        }

        /// <summary>
        /// Returns the symmetric signature for the data.
        /// </summary>
        protected byte[] Sign(ChannelToken token, ArraySegment<byte> dataToSign, bool useClientKeys)
        {
            switch (SecurityPolicyUri)
            {
                default:
                case SecurityPolicies.None:
                {
                    return null;
                }

                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP256r1:
                case SecurityPolicies.ECC_brainpoolP384r1:
                case SecurityPolicies.ECC_nistP256:
                {
                    return SymmetricSign(token, dataToSign, useClientKeys);
                }

#if GCMMODE
                case SecurityPolicies.ECC_nistP256:
                //case SecurityPolicies.Aes128_Gcm256_RsaOaep:
                {
                    return SymmetricSignWithAESGCM(token, dataToSign, useClientKeys);
                }
#endif
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
                {
                    return true;
                }

                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP256r1:
                case SecurityPolicies.ECC_brainpoolP384r1:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                case SecurityPolicies.ECC_nistP256:
                {
                    return SymmetricVerify(token, signature, dataToVerify, useClientKeys);
                }
#if GCMMODE
                case SecurityPolicies.ECC_nistP256:
                //case SecurityPolicies.Aes128_Gcm256_RsaOaep:
                {
                    return SymmetricVerifyWithAESGCM(token, signature, dataToVerify, useClientKeys);
                }
#endif
                default:
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Decrypts the data in a buffer using symmetric encryption.
        /// </summary>
        protected void Encrypt(ChannelToken token, ArraySegment<byte> dataToEncrypt, bool useClientKeys)
        {
            switch (SecurityPolicyUri)
            {
                default:
                case SecurityPolicies.None:
                {
                    break;
                }

                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP256r1:
                case SecurityPolicies.ECC_brainpoolP384r1:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                {
                    SymmetricEncrypt(token, dataToEncrypt, useClientKeys);
                    break;
                }
#if CURVE25519
                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
                {
                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
                    {
                        SymmetricEncryptWithChaCha20Poly1305(token, m_localSequenceNumber, dataToEncrypt, useClientKeys);
                        break;
                    }

                    SymmetricSignWithPoly1305(token, m_localSequenceNumber, dataToEncrypt, useClientKeys);
                    break;
                }
#endif
                case SecurityPolicies.ECC_nistP256:
                //case SecurityPolicies.Aes128_Gcm256_RsaOaep:
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Decrypts the data in a buffer using symmetric encryption.
        /// </summary>
        protected void Decrypt(ChannelToken token, ArraySegment<byte> dataToDecrypt, bool useClientKeys)
        {
            switch (SecurityPolicyUri)
            {
                default:
                case SecurityPolicies.None:
                {
                    break;
                }

                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP256r1:
                case SecurityPolicies.ECC_brainpoolP384r1:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                {
                    SymmetricDecrypt(token, dataToDecrypt, useClientKeys);
                    break;
                }
#if CURVE25519
                case SecurityPolicies.ECC_curve25519:
                case SecurityPolicies.ECC_curve448:
                {
                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
                    {
                        SymmetricDecryptWithChaCha20Poly1305(token, m_remoteSequenceNumber, dataToDecrypt, useClientKeys);
                        break;
                    }

                    SymmetricVerifyWithPoly1305(token, m_remoteSequenceNumber, dataToDecrypt, useClientKeys);
                    break;
                }
#endif
                case SecurityPolicies.ECC_nistP256:
                //case SecurityPolicies.Aes128_Gcm256_RsaOaep:
                {
                    break;
                }
            }
        }


        /// <summary>
        /// Signs the message using SHA1 HMAC
        /// </summary>
        private static byte[] SymmetricSign(ChannelToken token, ArraySegment<byte> dataToSign, bool useClientKeys)
        {
            // get HMAC object.
            HMAC hmac = (useClientKeys) ? token.ClientHmac : token.ServerHmac;

            // compute hash.
            MemoryStream istrm = new MemoryStream(dataToSign.Array, dataToSign.Offset, dataToSign.Count, false);
            byte[] signature = hmac.ComputeHash(istrm);
            istrm.Dispose();

            // return signature.
            return signature;
        }
#if GCMMODE
        /// <summary>
        /// Signs the message using SHA1 HMAC
        /// </summary>
        private static byte[] SymmetricSignWithAESGCM(ChannelToken token, ArraySegment<byte> dataToSign, bool useClientKeys)
        {
            try
            {
                var key = (useClientKeys) ? token.ClientEncryptingKey : token.ServerSigningKey;
                var nonce = (useClientKeys) ? token.ClientInitializationVector : token.ServerInitializationVector;
                var macSize = (useClientKeys) ? token.ClientSigningKey.Length : token.ServerSigningKey.Length;

                var header = new byte[TcpMessageLimits.SymmetricHeaderSize];
                Buffer.BlockCopy(dataToSign.Array, 0, header, 0, header.Length);

                var cipher = new GcmBlockCipher(new AesEngine());
                var parameters = new AeadParameters(new KeyParameter(key), macSize, nonce, header);
                cipher.Init(true, parameters);

                var plainTextLength = dataToSign.Count - TcpMessageLimits.SymmetricHeaderSize;
                var cipherLength = cipher.GetOutputSize(plainTextLength);
                var cipherText = new byte[cipherLength];

                var length = cipher.ProcessBytes(
                    dataToSign.Array, 
                    TcpMessageLimits.SymmetricHeaderSize,
                    plainTextLength, 
                    cipherText, 
                    0);

                cipher.DoFinal(cipherText, length);

                Buffer.BlockCopy(cipherText, 0, dataToSign.Array, TcpMessageLimits.SymmetricHeaderSize, cipherText.Length);

                // return signature.
                var signature = new byte[cipherLength + header.Length - dataToSign.Count];
                Buffer.BlockCopy(dataToSign.Array, dataToSign.Count, signature, 0, signature.Length);

                int eod = dataToSign.Count + signature.Length;
                int siz = BitConverter.ToInt32(dataToSign.Array, 4);

                //if (!SymmetricVerifyWithAESGCM(token, signature, dataToSign, useClientKeys))
                //{
                //    int x = 0;
                //}

                return signature;
            }
            catch (Exception)
            {
                throw;
            }
        }
#endif
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
            HMAC hmac = (useClientKeys) ? token.ClientHmac : token.ServerHmac;

            // compute hash.
            MemoryStream istrm = new MemoryStream(dataToVerify.Array, dataToVerify.Offset, dataToVerify.Count, false);
            byte[] computedSignature = hmac.ComputeHash(istrm);
            istrm.Dispose();

            // compare signatures.
            for (int ii = 0; ii < signature.Length; ii++)
            {
                if (computedSignature[ii] != signature[ii])
                {
                    string messageType = new UTF8Encoding().GetString(dataToVerify.Array, dataToVerify.Offset, 4);
                    int messageLength = BitConverter.ToInt32(dataToVerify.Array, dataToVerify.Offset + 4);
                    string expectedSignature = Utils.ToHexString(computedSignature);
                    string actualSignature = Utils.ToHexString(signature);

                    var message = new StringBuilder();
                    message.AppendLine("Channel{0}: Could not validate signature.");
                    message.AppendLine("ChannelId={1}, TokenId={2}, MessageType={3}, Length={4}");
                    message.AppendLine("ExpectedSignature={5}");
                    message.AppendLine("ActualSignature={6}");
                    Utils.LogError(message.ToString(), Id, token.ChannelId, token.TokenId,
                        messageType, messageLength, expectedSignature, actualSignature);

                    return false;
                }
            }

            return true;
        }
#if GCMMODE
        /// <summary>
        /// Verifies a HMAC for a message.
        /// </summary>
        private static bool SymmetricVerifyWithAESGCM(
            ChannelToken token,
            byte[] signature,
            ArraySegment<byte> dataToVerify,
            bool useClientKeys)
        {
            try
            {
                var key = (useClientKeys) ? token.ClientEncryptingKey : token.ServerSigningKey;
                var nonce = (useClientKeys) ? token.ClientInitializationVector : token.ServerInitializationVector;
                var macSize = (useClientKeys) ? token.ClientSigningKey.Length : token.ServerSigningKey.Length;

                using (var cipherStream = new MemoryStream(dataToVerify.Array, dataToVerify.Offset, dataToVerify.Count + signature.Length))
                {
                    using (var cipherReader = new BinaryReader(cipherStream))
                    {
                        var header = cipherReader.ReadBytes(TcpMessageLimits.SymmetricHeaderSize);

                        var cipher = new GcmBlockCipher(new AesEngine());
                        var parameters = new AeadParameters(new KeyParameter(key), macSize, nonce, header);
                        cipher.Init(false, parameters);

                        var cipherText = cipherReader.ReadBytes(dataToVerify.Count - header.Length + signature.Length);
                        var plainTextLength = cipher.GetOutputSize(cipherText.Length);
                        var plainText = new byte[plainTextLength];

                        try
                        {
                            var length = cipher.ProcessBytes(cipherText, 0, cipherText.Length, plainText, 0);
                            cipher.DoFinal(plainText, length);
                        }
                        catch (InvalidCipherTextException e)
                        {
                            // validation failed.
                            return false;
                        }

                        Buffer.BlockCopy(plainText, 0, dataToVerify.Array, dataToVerify.Offset + header.Length, plainText.Length);
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                throw;
            }
        }
#endif
        /// <summary>
        /// Encrypts a message using a symmetric algorithm.
        /// </summary>
        private static void SymmetricEncrypt(
            ChannelToken token,
            ArraySegment<byte> dataToEncrypt,
            bool useClientKeys)
        {
            SymmetricAlgorithm encryptingKey = (useClientKeys) ? token.ClientEncryptor : token.ServerEncryptor;

            if (encryptingKey == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Token missing symmetric key object.");
            }

            using (ICryptoTransform encryptor = encryptingKey.CreateEncryptor())
            {
                byte[] blockToEncrypt = dataToEncrypt.Array;

                int start = dataToEncrypt.Offset;
                int count = dataToEncrypt.Count;

                if (count % encryptor.InputBlockSize != 0)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Input data is not an even number of encryption blocks.");
                }

                encryptor.TransformBlock(blockToEncrypt, start, count, blockToEncrypt, start);
            }
        }

        /// <summary>
        /// Decrypts a message using a symmetric algorithm.
        /// </summary>
        private static void SymmetricDecrypt(
            ChannelToken token,
            ArraySegment<byte> dataToDecrypt,
            bool useClientKeys)
        {
            // get the decrypting key.
            SymmetricAlgorithm decryptingKey = (useClientKeys) ? token.ClientEncryptor : token.ServerEncryptor;

            if (decryptingKey == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Token missing symmetric key object.");
            }

            using (ICryptoTransform decryptor = decryptingKey.CreateDecryptor())
            {
                byte[] blockToDecrypt = dataToDecrypt.Array;

                int start = dataToDecrypt.Offset;
                int count = dataToDecrypt.Count;

                if (count % decryptor.InputBlockSize != 0)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Input data is not an even number of encryption blocks.");
                }

                decryptor.TransformBlock(blockToDecrypt, start, count, blockToDecrypt, start);
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
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Token missing symmetric key object.");
            }

            var encryptingKey = (useClientKeys) ? token.ClientEncryptingKey : token.ServerEncryptingKey;

            if (encryptingKey == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Token missing symmetric key object.");
            }

            var iv = (useClientKeys) ? token.ClientInitializationVector : token.ServerInitializationVector;

            if (iv == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Token missing symmetric key object.");
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
            int length = encryptor.ProcessBytes(plaintext, headerSize, plainTextLength - headerSize, ciphertext, headerSize);
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
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Token missing symmetric key object.");
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
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Token missing symmetric key object.");
            }

            var iv = (useClientKeys) ? token.ClientInitializationVector : token.ServerInitializationVector;

            if (iv == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Token missing symmetric key object.");
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

            var plaintext = new byte[decryptor.GetOutputSize(cipherTextLength + signatureLength - headerSize) + headerSize];
            Buffer.BlockCopy(ciphertext, headerSize, plaintext, 0, headerSize);

            int length = decryptor.ProcessBytes(ciphertext, headerSize, cipherTextLength + signatureLength - headerSize, plaintext, headerSize);
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
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Token missing symmetric key object.");
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
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        $"Invaid MAC on data.");
                }
            }
        }
#endif
        #endregion

        #region Private Fields
        private ChannelToken m_currentToken;
        private ChannelToken m_previousToken;
        private ChannelToken m_renewedToken;
        private int m_hmacHashSize;
        private int m_signatureKeySize;
        private int m_encryptionKeySize;
        private int m_encryptionBlockSize;
        #endregion
    }
}
