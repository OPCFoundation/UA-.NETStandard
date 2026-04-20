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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Types
{
    [TestFixture]
    [Category("UserNameIdentityTokenHandler")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UserNameIdentityTokenHandlerTests
    {
        private const string kSecurityPolicyUri = SecurityPolicies.Basic256Sha256;
        private const int RsaEncryptedSecretPasswordThreshold = 64;
        private const int TestLegacyPasswordLength = 12;

        [Test]
        public void DecryptSupportsRsaEncryptedSecretFormat()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(telemetry);
            SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(kSecurityPolicyUri);
            byte[] receiverNonce = Nonce.CreateNonce(securityPolicy.SecureChannelNonceLength).Data;
            byte[] expectedPassword = Nonce.CreateNonce(96).Data;

            using X509Certificate2 certificate = CertificateBuilder
                .Create("CN=User Identity Token Test Subject, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            byte[] encryptedSecret = CreateRsaEncryptedSecret(
                context,
                certificate,
                kSecurityPolicyUri,
                expectedPassword,
                receiverNonce);

            var token = new UserNameIdentityToken
            {
                UserName = "user",
                Password = encryptedSecret.ToByteString(),
                EncryptionAlgorithm = null
            };

            using var tokenHandler = new UserNameIdentityTokenHandler(token);
            tokenHandler.Decrypt(
                certificate,
                Nonce.CreateNonce(securityPolicy, receiverNonce),
                kSecurityPolicyUri,
                context);

            Assert.That(tokenHandler.DecryptedPassword, Is.EqualTo(expectedPassword));
        }

        [Test]
        public void DecryptKeepsLegacyRsaEncryptedTokenPath()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(telemetry);
            SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(kSecurityPolicyUri);
            byte[] receiverNonce = Nonce.CreateNonce(securityPolicy.SecureChannelNonceLength).Data;
            byte[] expectedPassword = GetRandomBytes(TestLegacyPasswordLength);

            using X509Certificate2 certificate = CertificateBuilder
                .Create("CN=User Identity Token Legacy Test Subject, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            byte[] dataToEncrypt = Utils.Append(expectedPassword, receiverNonce);
            EncryptedData encryptedData = SecurityPolicies.Encrypt(
                certificate,
                kSecurityPolicyUri,
                dataToEncrypt,
                context.Telemetry.CreateLogger<UserNameIdentityTokenHandlerTests>());

            var token = new UserNameIdentityToken
            {
                UserName = "legacyUser",
                Password = encryptedData.Data.ToByteString(),
                EncryptionAlgorithm = encryptedData.Algorithm
            };

            using var tokenHandler = new UserNameIdentityTokenHandler(token);
            tokenHandler.Decrypt(
                certificate,
                Nonce.CreateNonce(securityPolicy, receiverNonce),
                kSecurityPolicyUri,
                context);

            Assert.That(tokenHandler.DecryptedPassword, Is.EqualTo(expectedPassword));
        }

        [Test]
        public void DecryptThrowsBadIdentityTokenInvalidWhenEccTryDecryptFails()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(telemetry);

            var token = new UserNameIdentityToken
            {
                UserName = "eccUser",
                Password = new byte[] { 0x00, 0x01, 0x02, 0x03 }.ToByteString(),
                EncryptionAlgorithm = null
            };

            using var tokenHandler = new UserNameIdentityTokenHandler(token);
            Assert.That(
                () => tokenHandler.Decrypt(
                    certificate: null,
                    receiverNonce: null,
                    securityPolicyUri: SecurityPolicies.ECC_nistP256,
                    context: context,
                    ephemeralKey: null,
                    senderCertificate: null,
                    senderIssuerCertificates: null,
                    validator: null),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public void EncryptUsesLegacyRsaFormatForShortPassword()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(telemetry);
            SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(kSecurityPolicyUri);
            byte[] receiverNonce = Nonce.CreateNonce(securityPolicy.SecureChannelNonceLength).Data;
            byte[] password = GetRandomBytes(RsaEncryptedSecretPasswordThreshold - 1);

            using X509Certificate2 certificate = CertificateBuilder
                .Create("CN=User Identity Token Encrypt Legacy Subject, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var tokenHandler = new UserNameIdentityTokenHandler("legacyUser", password);
            tokenHandler.Encrypt(certificate, receiverNonce, kSecurityPolicyUri, context);

            Assert.That(tokenHandler.Token, Is.TypeOf<UserNameIdentityToken>());
            var token = (UserNameIdentityToken)tokenHandler.Token;
            Assert.That(token.EncryptionAlgorithm, Is.Not.Null.And.Not.Empty);

            using var decryptHandler = new UserNameIdentityTokenHandler(token);
            decryptHandler.Decrypt(
                certificate,
                Nonce.CreateNonce(securityPolicy, receiverNonce),
                kSecurityPolicyUri,
                context);

            Assert.That(decryptHandler.DecryptedPassword, Is.EqualTo(password));
        }

        [Test]
        public void EncryptUsesLegacyRsaFormatAtThresholdPasswordLength()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(telemetry);
            SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(kSecurityPolicyUri);
            byte[] receiverNonce = Nonce.CreateNonce(securityPolicy.SecureChannelNonceLength).Data;
            byte[] password = GetRandomBytes(RsaEncryptedSecretPasswordThreshold);

            using X509Certificate2 certificate = CertificateBuilder
                .Create("CN=User Identity Token Encrypt Threshold Subject, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var tokenHandler = new UserNameIdentityTokenHandler("thresholdUser", password);
            tokenHandler.Encrypt(certificate, receiverNonce, kSecurityPolicyUri, context);

            Assert.That(tokenHandler.Token, Is.TypeOf<UserNameIdentityToken>());
            var token = (UserNameIdentityToken)tokenHandler.Token;
            Assert.That(token.EncryptionAlgorithm, Is.Not.Null.And.Not.Empty);

            using var decryptHandler = new UserNameIdentityTokenHandler(token);
            decryptHandler.Decrypt(
                certificate,
                Nonce.CreateNonce(securityPolicy, receiverNonce),
                kSecurityPolicyUri,
                context);

            Assert.That(decryptHandler.DecryptedPassword, Is.EqualTo(password));
        }

        [Test]
        public void EncryptUsesRsaEncryptedSecretForLongPassword()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(telemetry);
            SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(kSecurityPolicyUri);
            byte[] receiverNonce = Nonce.CreateNonce(securityPolicy.SecureChannelNonceLength).Data;
            byte[] password = GetRandomBytes(RsaEncryptedSecretPasswordThreshold + 1);

            using X509Certificate2 certificate = CertificateBuilder
                .Create("CN=User Identity Token Encrypt Secret Subject, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var tokenHandler = new UserNameIdentityTokenHandler("secretUser", password);
            tokenHandler.Encrypt(certificate, receiverNonce, kSecurityPolicyUri, context);

            Assert.That(tokenHandler.Token, Is.TypeOf<UserNameIdentityToken>());
            var token = (UserNameIdentityToken)tokenHandler.Token;
            Assert.That(token.EncryptionAlgorithm, Is.Null);

            using var decryptHandler = new UserNameIdentityTokenHandler(token);
            decryptHandler.Decrypt(
                certificate,
                Nonce.CreateNonce(securityPolicy, receiverNonce),
                kSecurityPolicyUri,
                context);

            Assert.That(decryptHandler.DecryptedPassword, Is.EqualTo(password));
        }

        private static byte[] CreateRsaEncryptedSecret(
            IServiceMessageContext context,
            X509Certificate2 receiverCertificate,
            string securityPolicyUri,
            byte[] secret,
            byte[] nonce)
        {
            SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(securityPolicyUri);
            if (securityPolicy.SymmetricEncryptionAlgorithm is not
                (SymmetricEncryptionAlgorithm.Aes128Cbc or SymmetricEncryptionAlgorithm.Aes256Cbc))
            {
                throw new NotSupportedException("The test helper supports RSA security policies with CBC encryption only.");
            }

            byte[] signingKey = GetRandomBytes(securityPolicy.DerivedSignatureKeyLength);
            byte[] encryptingKey = GetRandomBytes(securityPolicy.SymmetricEncryptionKeyLength);
            byte[] iv = GetRandomBytes(securityPolicy.InitializationVectorLength);
            byte[] keyData = Utils.Append(signingKey, encryptingKey, iv);

            byte[] encryptedKeyData = SecurityPolicies.Encrypt(
                receiverCertificate,
                securityPolicyUri,
                keyData,
                context.Telemetry.CreateLogger<UserNameIdentityTokenHandlerTests>()).Data;

            byte[] plainPayload = CreatePayload(context, secret, nonce, securityPolicy.InitializationVectorLength);
            byte[] encryptedPayload = EncryptPayload(plainPayload, encryptingKey, iv);

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

            for (int ii = 0; ii < encryptedKeyData.Length; ii++)
            {
                encoder.WriteByte(null, encryptedKeyData[ii]);
            }

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
            using HMAC hmac = securityPolicy.CreateSignatureHmac(signingKey);
            byte[] signature = hmac.ComputeHash(encodedSecret, 0, signatureStart);
            Buffer.BlockCopy(
                signature,
                0,
                encodedSecret,
                signatureStart,
                Math.Min(signature.Length, securityPolicy.SymmetricSignatureLength));

            return encodedSecret;
        }

        private static byte[] CreatePayload(
            IServiceMessageContext context,
            byte[] secret,
            byte[] nonce,
            int blockSize)
        {
            using var encoder = new BinaryEncoder(context);
            int startOfPayload = encoder.Position;
            encoder.WriteByteString(null, nonce);
            encoder.WriteByteString(null, secret);

            int dataLength = encoder.Position - startOfPayload + 2;
            int paddingCount = dataLength % blockSize == 0 ? 0 : blockSize - dataLength % blockSize;
            if (paddingCount + secret.Length < blockSize)
            {
                paddingCount += blockSize;
            }

            for (int ii = 0; ii < paddingCount; ii++)
            {
                encoder.WriteByte(null, (byte)paddingCount);
            }

            encoder.WriteByte(null, (byte)paddingCount);
            encoder.WriteByte(null, 0);
            return encoder.CloseAndReturnBuffer();
        }

        private static byte[] EncryptPayload(byte[] plainPayload, byte[] encryptingKey, byte[] iv)
        {
            byte[] encryptedPayload = new byte[plainPayload.Length];
            Buffer.BlockCopy(plainPayload, 0, encryptedPayload, 0, plainPayload.Length);

#pragma warning disable CA5401 // Symmetric encryption uses non-default initialization vector
            using Aes aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = encryptingKey;
            aes.IV = iv;
#pragma warning restore CA5401

            using ICryptoTransform encryptor = aes.CreateEncryptor();
            return encryptor.TransformFinalBlock(encryptedPayload, 0, encryptedPayload.Length);
        }

        private static byte[] GetRandomBytes(int count)
        {
            var bytes = new byte[count];
            using RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create();
            randomNumberGenerator.GetBytes(bytes);
            return bytes;
        }

        private static byte[] ComputeSha1Hash(byte[] data)
        {
            using SHA1 sha1 = SHA1.Create();
            return sha1.ComputeHash(data);
        }
    }
}
