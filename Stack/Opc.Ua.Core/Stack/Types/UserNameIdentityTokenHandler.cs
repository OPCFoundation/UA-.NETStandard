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
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// The UserIdentityToken class.
    /// </summary>
    public sealed class UserNameIdentityTokenHandler : IUserIdentityTokenHandler
    {
        private const int RsaEncryptedSecretPasswordThreshold = 64;
        private static readonly TimeSpan RsaEncryptedSecretMaxClockSkew = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RsaEncryptedSecretMaxTokenAge = TimeSpan.FromHours(1);

        /// <summary>
        /// Create token handler
        /// </summary>
        public UserNameIdentityTokenHandler(UserNameIdentityToken token)
        {
            DecryptedPassword = null;
            m_token = token;
        }

        /// <summary>
        /// Create token handler
        /// </summary>
        public UserNameIdentityTokenHandler(
            string username,
            ReadOnlySpan<byte> password)
        {
            DecryptedPassword = password.ToArray();
            m_token = new UserNameIdentityToken
            {
                UserName = username,
                Password = password.ToByteString()
            };
        }

        /// <inheritdoc/>
        public UserIdentityToken Token => m_token;

        /// <inheritdoc/>
        public string DisplayName => m_token.UserName;

        /// <inheritdoc/>
        public UserTokenType TokenType => UserTokenType.UserName;

        /// <summary>
        /// The decrypted password associated with the token.
        /// </summary>
        public byte[] DecryptedPassword { get; set; }

        /// <summary>
        /// User name in the token.
        /// </summary>
        public string UserName => m_token.UserName;

        /// <inheritdoc/>
        public void UpdatePolicy(UserTokenPolicy userTokenPolicy)
        {
            m_token.PolicyId = userTokenPolicy.PolicyId;
        }

        /// <inheritdoc/>
        public void Encrypt(
            X509Certificate2 receiverCertificate,
            byte[] receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            Nonce receiverEphemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            bool doNotEncodeSenderCertificate = false)
        {
            if (DecryptedPassword == null)
            {
                m_token.Password = default;
                return;
            }

            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                m_token.Password = DecryptedPassword.ToByteString();
                m_token.EncryptionAlgorithm = null;
                return;
            }

            // handle RSA encryption.
            var securityPolicy = SecurityPolicies.GetInfo(securityPolicyUri);

            if (securityPolicy.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None)
            {
                if (DecryptedPassword.Length > RsaEncryptedSecretPasswordThreshold)
                {
                    m_token.Password = EncryptRsaEncryptedSecret(
                        receiverCertificate,
                        receiverNonce,
                        securityPolicyUri,
                        context).ToByteString();
                    m_token.EncryptionAlgorithm = null;
                    return;
                }

                byte[] dataToEncrypt = Utils.Append(DecryptedPassword, receiverNonce);

                ILogger logger = context.Telemetry.CreateLogger<UserNameIdentityToken>();
                EncryptedData encryptedData = SecurityPolicies.Encrypt(
                    receiverCertificate,
                    securityPolicyUri,
                    dataToEncrypt,
                    logger);

                m_token.Password = encryptedData.Data.ToByteString();
                m_token.EncryptionAlgorithm = encryptedData.Algorithm;
                Array.Clear(dataToEncrypt, 0, dataToEncrypt.Length);
            }

            // handle ECC and RSADH encryption.
            else
            {
                // check if the complete chain is included in the sender issuers.
                if (senderIssuerCertificates != null &&
                    senderIssuerCertificates.Count > 0 &&
                    senderIssuerCertificates[0].Thumbprint == senderCertificate.Thumbprint)
                {
                    var issuers = new X509Certificate2Collection();

                    for (int ii = 1; ii < senderIssuerCertificates.Count; ii++)
                    {
                        issuers.Add(senderIssuerCertificates[ii]);
                    }

                    senderIssuerCertificates = issuers;
                }

                var secret = new EncryptedSecret(
                    context,
                    securityPolicyUri,
                    senderIssuerCertificates,
                    receiverCertificate,
                    receiverEphemeralKey,
                    senderCertificate,
                    Nonce.CreateNonce(securityPolicy),
                    null,
                    doNotEncodeSenderCertificate);

                m_token.Password = secret.Encrypt(DecryptedPassword, receiverNonce).ToByteString();
                m_token.EncryptionAlgorithm = null;
            }
        }

        private byte[] EncryptRsaEncryptedSecret(
            X509Certificate2 receiverCertificate,
            byte[] receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context)
        {
            SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(securityPolicyUri);
            byte[] signingKey = null;
            byte[] encryptingKey = null;
            byte[] iv = null;
            byte[] keyData = null;
            byte[] encryptedPayload = null;

            try
            {
                signingKey = Nonce.CreateRandomNonceData(securityPolicy.DerivedSignatureKeyLength, false);
                encryptingKey = Nonce.CreateRandomNonceData(securityPolicy.SymmetricEncryptionKeyLength, false);
                iv = Nonce.CreateRandomNonceData(securityPolicy.InitializationVectorLength, false);
                keyData = Utils.Append(signingKey, encryptingKey, iv);

                ILogger logger = context.Telemetry.CreateLogger<UserNameIdentityToken>();
                byte[] encryptedKeyData = SecurityPolicies.Encrypt(
                    receiverCertificate,
                    securityPolicyUri,
                    keyData,
                    logger).Data;

                using var payloadEncoder = new BinaryEncoder(context);
                payloadEncoder.WriteByteString(null, receiverNonce ?? []);
                payloadEncoder.WriteByteString(null, DecryptedPassword);
                byte[] payload = payloadEncoder.CloseAndReturnBuffer();

                int blockSize = securityPolicy.InitializationVectorLength;
                int paddingByteSize = blockSize > byte.MaxValue ? 2 : 1;
                int paddingSize = blockSize - ((payload.Length + paddingByteSize) % blockSize);
                paddingSize %= blockSize;

                encryptedPayload = new byte[payload.Length + paddingSize + paddingByteSize];
                Buffer.BlockCopy(payload, 0, encryptedPayload, 0, payload.Length);

                for (int ii = payload.Length; ii < payload.Length + paddingSize; ii++)
                {
                    encryptedPayload[ii] = (byte)(paddingSize & 0xFF);
                }

                encryptedPayload[payload.Length + paddingSize] = (byte)(paddingSize & 0xFF);
                if (paddingByteSize > 1)
                {
                    encryptedPayload[payload.Length + paddingSize + 1] = (byte)((paddingSize >> 8) & 0xFF);
                }

#pragma warning disable CA5401 // Symmetric encryption uses non-default initialization vector
                using Aes aes = Aes.Create();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = encryptingKey;
                aes.IV = iv;
#pragma warning restore CA5401
                using ICryptoTransform encryptor = aes.CreateEncryptor();
                int bytesEncrypted = encryptor.TransformBlock(
                    encryptedPayload,
                    0,
                    encryptedPayload.Length,
                    encryptedPayload,
                    0);
                if (bytesEncrypted != encryptedPayload.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingError);
                }

                ZeroMemory(payload);

                using var encoder = new BinaryEncoder(context);
                encoder.WriteNodeId(null, DataTypeIds.RsaEncryptedSecret);
                encoder.WriteByte(null, (byte)ExtensionObjectEncoding.Binary);
                int lengthPosition = encoder.Position;
                encoder.WriteUInt32(null, 0);
                encoder.WriteString(null, securityPolicyUri);
#pragma warning disable CA5350 // SHA1 is required by OPC UA RsaEncryptedSecret certificate hash field.
                encoder.WriteByteString(null, ComputeSha1Hash(receiverCertificate.RawData));
#pragma warning restore CA5350
                encoder.WriteDateTime(null, DateTime.UtcNow);
                encoder.WriteUInt16(null, (ushort)encryptedKeyData.Length);

                // KeyData is a raw byte block with length encoded separately, not a ByteString field.
                for (int ii = 0; ii < encryptedKeyData.Length; ii++)
                {
                    encoder.WriteByte(null, encryptedKeyData[ii]);
                }

                // Payload bytes are encoded as raw data according to RsaEncryptedSecret binary layout.
                for (int ii = 0; ii < encryptedPayload.Length; ii++)
                {
                    encoder.WriteByte(null, encryptedPayload[ii]);
                }

                for (int ii = 0; ii < securityPolicy.SymmetricSignatureLength; ii++)
                {
                    encoder.WriteByte(null, 0);
                }

                byte[] encodedSecret = encoder.CloseAndReturnBuffer();
                int extensionObjectLength = encodedSecret.Length - lengthPosition - 4;
                encodedSecret[lengthPosition++] = (byte)(extensionObjectLength & 0xFF);
                encodedSecret[lengthPosition++] = (byte)((extensionObjectLength >> 8) & 0xFF);
                encodedSecret[lengthPosition++] = (byte)((extensionObjectLength >> 16) & 0xFF);
                encodedSecret[lengthPosition] = (byte)((extensionObjectLength >> 24) & 0xFF);

                int signatureStart = encodedSecret.Length - securityPolicy.SymmetricSignatureLength;
                if (securityPolicy.SymmetricSignatureLength > 0)
                {
                    using HMAC hmac = securityPolicy.CreateSignatureHmac(signingKey) ??
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "The security policy does not support symmetric signatures required for RSAEncryptedSecret creation.");
                    byte[] signature = hmac.ComputeHash(encodedSecret, 0, signatureStart);
                    Buffer.BlockCopy(
                        signature,
                        0,
                        encodedSecret,
                        signatureStart,
                        Math.Min(signature.Length, securityPolicy.SymmetricSignatureLength));
                }

                return encodedSecret;
            }
            finally
            {
                if (signingKey != null)
                {
                    ZeroMemory(signingKey);
                }

                if (encryptingKey != null)
                {
                    ZeroMemory(encryptingKey);
                }

                if (iv != null)
                {
                    ZeroMemory(iv);
                }

                if (keyData != null)
                {
                    ZeroMemory(keyData);
                }

                if (encryptedPayload != null)
                {
                    ZeroMemory(encryptedPayload);
                }
            }
        }

        /// <inheritdoc/>
        public void Decrypt(
            X509Certificate2 certificate,
            Nonce receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            Nonce ephemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            CertificateValidator validator = null)
        {
            //zero out existing password
            if (DecryptedPassword != null)
            {
                Array.Clear(DecryptedPassword, 0, DecryptedPassword.Length);
            }

            // handle no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                DecryptedPassword = new byte[m_token.Password.Length];
                Array.Copy(m_token.Password.ToArray(), DecryptedPassword, m_token.Password.Length);
                return;
            }

            // handle RSA encryption.
            var securityPolicy = SecurityPolicies.GetInfo(securityPolicyUri);

            if (securityPolicy.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None)
            {
                if (TryDecryptRsaEncryptedSecret(certificate, receiverNonce, securityPolicyUri, context, out byte[] decryptedSecret))
                {
                    DecryptedPassword = decryptedSecret;
                    return;
                }

                var encryptedData = new EncryptedData
                {
                    Data = m_token.Password.ToArray(),
                    Algorithm = m_token.EncryptionAlgorithm
                };

                ILogger logger = context.Telemetry.CreateLogger<UserNameIdentityTokenHandler>();
                byte[] decryptedPassword = SecurityPolicies.Decrypt(
                    certificate,
                    securityPolicyUri,
                    encryptedData,
                    logger);

                if (decryptedPassword == null)
                {
                    DecryptedPassword = null;
                    return;
                }

                // verify the sender's nonce.
                int startOfNonce = decryptedPassword.Length;
                if (receiverNonce != null)
                {
                    startOfNonce -= receiverNonce.Data.Length;

                    int result = 0;
                    for (int ii = 0; ii < receiverNonce.Data.Length; ii++)
                    {
                        result |= receiverNonce.Data[ii] ^ decryptedPassword[ii + startOfNonce];
                    }

                    if (result != 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadIdentityTokenInvalid);
                    }
                }

                // copy result to m_decrypted password field
                DecryptedPassword = new byte[startOfNonce];
                Array.Copy(decryptedPassword, DecryptedPassword, startOfNonce);
                Array.Clear(decryptedPassword, 0, decryptedPassword.Length);
            }

            // handle ECC and RSADH encryption.
            else
            {
                var secret = new EncryptedSecret(
                    context,
                    securityPolicyUri,
                    senderIssuerCertificates,
                    certificate,
                    ephemeralKey,
                    senderCertificate,
                    null,
                    validator);

                DecryptedPassword = secret.Decrypt(
                    DateTime.UtcNow.AddHours(-1),
                    receiverNonce.Data,
                    m_token.Password.ToArray(),
                    0,
                    m_token.Password.Length,
                    context.Telemetry);
            }
        }

        private bool TryDecryptRsaEncryptedSecret(
            X509Certificate2 certificate,
            Nonce receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            out byte[] decryptedPassword)
        {
            decryptedPassword = null;

            // RSAEncryptedSecret stores algorithm details in the ExtensionObject body and
            // therefore is only valid when EncryptionAlgorithm is not explicitly set.
            if (certificate == null || !string.IsNullOrEmpty(m_token.EncryptionAlgorithm))
            {
                return false;
            }

            byte[] encodedSecret = m_token.Password.ToArray();
            if (encodedSecret == null || encodedSecret.Length < 8)
            {
                return false;
            }

            using var decoder = new BinaryDecoder(encodedSecret, context);
            NodeId typeId = decoder.ReadNodeId(null);

            if (typeId != DataTypeIds.RsaEncryptedSecret)
            {
                return false;
            }

            var encoding = (ExtensionObjectEncoding)decoder.ReadByte(null);
            if (encoding != ExtensionObjectEncoding.Binary)
            {
                throw new ServiceResultException(StatusCodes.BadDataEncodingUnsupported);
            }

            int endOfSecret = checked((int)decoder.ReadUInt32(null) + decoder.Position);
            if (endOfSecret > encodedSecret.Length || endOfSecret <= decoder.Position)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }

            string encryptedSecretPolicyUri = decoder.ReadString(null);
            if (!string.Equals(encryptedSecretPolicyUri, securityPolicyUri, StringComparison.Ordinal))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unexpected encrypted secret security policy: {0}",
                    encryptedSecretPolicyUri);
            }

            ByteString certificateHash = decoder.ReadByteString(null);
            if (certificateHash.Length > 0)
            {
#pragma warning disable CA5350 // SHA1 is required by OPC UA RsaEncryptedSecret certificate hash field.
                byte[] actualCertificateHash = ComputeSha1Hash(certificate.RawData);
#pragma warning restore CA5350
                if (!Utils.IsEqual(certificateHash.ToArray(), actualCertificateHash))
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                }
            }

            DateTime signingTime = (DateTime)decoder.ReadDateTime(null);
            DateTime now = DateTime.UtcNow;
            // Accept tokens from the recent past to account for transit/processing delays while
            // only allowing a small future clock skew to prevent replay with future-dated tokens.
            if (signingTime < now - RsaEncryptedSecretMaxTokenAge || signingTime > now + RsaEncryptedSecretMaxClockSkew)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidTimestamp);
            }
            ushort keyDataLength = decoder.ReadUInt16(null);
            if (keyDataLength == 0 || decoder.Position + keyDataLength > endOfSecret)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }

            int keyDataStart = decoder.Position;
            _ = decoder.BaseStream.Seek(keyDataLength, SeekOrigin.Current);

            SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(securityPolicyUri);
            if (securityPolicy == null || securityPolicy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected);
            }

            int signatureLength = securityPolicy.SymmetricSignatureLength;
            int signatureStart = endOfSecret - signatureLength;
            if (signatureStart <= decoder.Position)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }

            byte[] keyData = null;
            byte[] signingKey = null;
            byte[] encryptingKey = null;
            byte[] iv = null;
            byte[] encryptedPayload = null;
            byte[] payload = null;

            try
            {
                ILogger logger = context.Telemetry.CreateLogger<UserNameIdentityTokenHandler>();
                keyData = RsaUtils.Decrypt(
                    new ArraySegment<byte>(encodedSecret, keyDataStart, keyDataLength),
                    certificate,
                    securityPolicy.AsymmetricEncryptionAlgorithm switch
                    {
                        AsymmetricEncryptionAlgorithm.RsaOaepSha1 => RsaUtils.Padding.OaepSHA1,
                        AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1 => RsaUtils.Padding.Pkcs1,
                        _ => RsaUtils.Padding.OaepSHA256
                    },
                    logger);

                int expectedKeyDataLength =
                    securityPolicy.DerivedSignatureKeyLength +
                    securityPolicy.SymmetricEncryptionKeyLength +
                    securityPolicy.InitializationVectorLength;

                if (keyData.Length < expectedKeyDataLength)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError);
                }

                signingKey = new byte[securityPolicy.DerivedSignatureKeyLength];
                encryptingKey = new byte[securityPolicy.SymmetricEncryptionKeyLength];
                iv = new byte[securityPolicy.InitializationVectorLength];
                int keyMaterialOffset = signingKey.Length + encryptingKey.Length;
                if (keyMaterialOffset + iv.Length > keyData.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError);
                }

                Buffer.BlockCopy(keyData, 0, signingKey, 0, signingKey.Length);
                Buffer.BlockCopy(keyData, signingKey.Length, encryptingKey, 0, encryptingKey.Length);
                Buffer.BlockCopy(keyData, keyMaterialOffset, iv, 0, iv.Length);

                if (signatureLength > 0)
                {
                    using HMAC hmac = securityPolicy.CreateSignatureHmac(signingKey) ??
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "The security policy does not support symmetric signatures required for RSAEncryptedSecret validation.");

                    byte[] expectedSignature = hmac.ComputeHash(encodedSecret, 0, signatureStart);
                    int notValid = expectedSignature.Length == signatureLength ? 0 : 1;

                    for (int ii = 0; ii < signatureLength; ii++)
                    {
                        byte expectedByte = ii < expectedSignature.Length ? expectedSignature[ii] : (byte)0;
                        notValid |= expectedByte ^ encodedSecret[signatureStart + ii];
                    }

                    if (notValid != 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                    }
                }

                int encryptedPayloadStart = decoder.Position;
                int encryptedPayloadLength = signatureStart - encryptedPayloadStart;
                encryptedPayload = new byte[encryptedPayloadLength];
                Buffer.BlockCopy(encodedSecret, encryptedPayloadStart, encryptedPayload, 0, encryptedPayloadLength);
                ArraySegment<byte> plainText = CryptoUtils.SymmetricDecryptAndVerify(
                    new ArraySegment<byte>(encryptedPayload),
                    securityPolicy,
                    encryptingKey,
                    iv);
                payload = new byte[plainText.Count];
                Buffer.BlockCopy(plainText.Array, plainText.Offset, payload, 0, payload.Length);

                using var payloadDecoder = new BinaryDecoder(payload, context);

                ByteString actualNonce = payloadDecoder.ReadByteString(null);
                if (receiverNonce?.Data != null && !Utils.IsEqual(actualNonce.ToArray(), receiverNonce.Data))
                {
                    throw new ServiceResultException(StatusCodes.BadNonceInvalid);
                }

                decryptedPassword = payloadDecoder.ReadByteString(null).ToArray();
                return true;
            }
            finally
            {
                if (keyData != null)
                {
                    ZeroMemory(keyData);
                }

                if (signingKey != null)
                {
                    ZeroMemory(signingKey);
                }

                if (encryptingKey != null)
                {
                    ZeroMemory(encryptingKey);
                }

                if (iv != null)
                {
                    ZeroMemory(iv);
                }

                if (encryptedPayload != null)
                {
                    ZeroMemory(encryptedPayload);
                }

                if (payload != null)
                {
                    ZeroMemory(payload);
                }
            }
        }

        /// <inheritdoc/>
        public SignatureData Sign(
            byte[] dataToSign,
            string securityPolicyUri)
        {
            return new SignatureData();
        }

        /// <inheritdoc/>
        public bool Verify(
            byte[] dataToVerify,
            SignatureData signatureData,
            string securityPolicyUri)
        {
            return true;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (DecryptedPassword != null)
            {
                Array.Clear(DecryptedPassword, 0, DecryptedPassword.Length);
                DecryptedPassword = null;
            }

            // Array.Clear(m_token.Password, 0, m_token.Password.Length);
            m_token.Password = default;
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return new UserNameIdentityTokenHandler(CoreUtils.Clone(m_token))
            {
                DecryptedPassword = DecryptedPassword == null ? null : [.. DecryptedPassword]
            };
        }

        /// <inheritdoc/>
        public bool Equals(IUserIdentityTokenHandler other)
        {
            if (other is not UserNameIdentityTokenHandler tokenHandler)
            {
                return false;
            }
            if (!string.Equals(UserName, tokenHandler.UserName, StringComparison.Ordinal))
            {
                return false;
            }
            // TODO: Should compare password too?
            return true;
        }

        private static byte[] ComputeSha1Hash(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using SHA1 sha1 = SHA1.Create();
            return sha1.ComputeHash(data);
        }

        private static void ZeroMemory(byte[] buffer)
        {
            if (buffer == null)
            {
                return;
            }
#if NET8_0_OR_GREATER
            CryptographicOperations.ZeroMemory(buffer);
#else
            Array.Clear(buffer, 0, buffer.Length);
#endif
        }

        private readonly UserNameIdentityToken m_token;
    }
}
