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

// CA2000: test code manages disposables at fixture scope or transfers ownership deliberately.
#pragma warning disable CA2000
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Deterministic coverage tests for <see cref="EncryptedSecret"/> RSAEncryptedSecret
    /// encryption, decryption and the guard branches around them.
    /// </summary>
    [TestFixture]
    [Category("EncryptedSecret")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class EncryptedSecretTests
    {
        private IServiceMessageContext m_context;
        private Certificate m_certificate;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            m_certificate = CertificateBuilder
                .Create("CN=EncryptedSecret Test Subject, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_certificate?.Dispose();
        }

        [TestCase(SecurityPolicies.Basic256Sha256)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss)]
        public void EncryptRsaThenTryDecryptRsaReturnsOriginalSecret(string policyUri)
        {
            EncryptedSecret encryptedSecret = CreateRsa(policyUri);
            byte[] secret = SecretBytes();
            byte[] nonce = NonceBytes();

            byte[] encoded = encryptedSecret.EncryptRsa(secret, nonce);
            bool ok = encryptedSecret.TryDecryptRsa(encoded, nonce, out byte[] decrypted);

            Assert.That(ok, Is.True);
            Assert.That(decrypted, Is.EqualTo(secret));
        }

        [Test]
        public void EncryptThenTryDecryptReturnsOriginalSecret()
        {
            EncryptedSecret encryptedSecret = CreateRsa();
            byte[] secret = SecretBytes();
            byte[] nonce = NonceBytes();

            byte[] encoded = encryptedSecret.Encrypt(secret, nonce);
            bool ok = encryptedSecret.TryDecrypt(encoded, nonce, out byte[] decrypted);

            Assert.That(ok, Is.True);
            Assert.That(decrypted, Is.EqualTo(secret));
        }

        [Test]
        public async Task TryDecryptAsyncReturnsOriginalRsaSecretAsync()
        {
            EncryptedSecret encryptedSecret = CreateRsa();
            byte[] secret = SecretBytes();
            byte[] nonce = NonceBytes();

            byte[] encoded = encryptedSecret.EncryptRsa(secret, nonce);
            (bool success, byte[] decrypted) = await encryptedSecret
                .TryDecryptAsync(encoded, nonce)
                .ConfigureAwait(false);

            Assert.That(success, Is.True);
            Assert.That(decrypted, Is.EqualTo(secret));
        }

        [Test]
        public void TryDecryptRsaThrowsBadNonceInvalidWhenNonceMismatched()
        {
            EncryptedSecret encryptedSecret = CreateRsa();
            byte[] encoded = encryptedSecret.EncryptRsa(SecretBytes(), NonceBytes());
            byte[] wrongNonce = [0xAA, 0xBB, 0xCC, 0xDD];

            Assert.That(
                () => encryptedSecret.TryDecryptRsa(encoded, wrongNonce, out _),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNonceInvalid));
        }

        [Test]
        public void TryDecryptRsaThrowsBadSecurityChecksFailedWhenSignatureTampered()
        {
            EncryptedSecret encryptedSecret = CreateRsa();
            byte[] nonce = NonceBytes();
            byte[] encoded = encryptedSecret.EncryptRsa(SecretBytes(), nonce);
            encoded[^1] ^= 0xFF;

            Assert.That(
                () => encryptedSecret.TryDecryptRsa(encoded, nonce, out _),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void TryDecryptRsaReturnsFalseWhenDataTooShort()
        {
            EncryptedSecret encryptedSecret = CreateRsa();

            bool ok = encryptedSecret.TryDecryptRsa([1, 2, 3, 4], NonceBytes(), out byte[] decrypted);

            Assert.That(ok, Is.False);
            Assert.That(decrypted, Is.Null);
        }

        [Test]
        public void TryDecryptRsaReturnsFalseWhenTypeIdIsNotRsaEncryptedSecret()
        {
            EncryptedSecret encryptedSecret = CreateRsa();

            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteNodeId(null, DataTypeIds.EccEncryptedSecret);
            encoder.WriteByte(null, (byte)ExtensionObjectEncoding.Binary);
            encoder.WriteUInt32(null, 0);
            byte[] wrongTyped = encoder.CloseAndReturnBuffer();

            bool ok = encryptedSecret.TryDecryptRsa(wrongTyped, NonceBytes(), out byte[] decrypted);

            Assert.That(ok, Is.False);
            Assert.That(decrypted, Is.Null);
        }

        [Test]
        public void TryDecryptRsaThrowsBadDataEncodingUnsupportedWhenEncodingNotBinary()
        {
            EncryptedSecret encryptedSecret = CreateRsa();

            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteNodeId(null, DataTypeIds.RsaEncryptedSecret);
            encoder.WriteByte(null, (byte)ExtensionObjectEncoding.Xml);
            encoder.WriteUInt32(null, 0);
            encoder.WriteByte(null, 0);
            byte[] encoded = encoder.CloseAndReturnBuffer();

            Assert.That(
                () => encryptedSecret.TryDecryptRsa(encoded, NonceBytes(), out _),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDataEncodingUnsupported));
        }

        [Test]
        public void TryDecryptRsaThrowsBadDecodingErrorWhenLengthExceedsBuffer()
        {
            EncryptedSecret encryptedSecret = CreateRsa();

            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteNodeId(null, DataTypeIds.RsaEncryptedSecret);
            encoder.WriteByte(null, (byte)ExtensionObjectEncoding.Binary);
            encoder.WriteUInt32(null, 0x00FFFFFF);
            byte[] encoded = encoder.CloseAndReturnBuffer();

            Assert.That(
                () => encryptedSecret.TryDecryptRsa(encoded, NonceBytes(), out _),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void TryDecryptRsaThrowsBadSecurityPolicyRejectedWhenPolicyUriMismatched()
        {
            byte[] nonce = NonceBytes();
            byte[] encoded = CreateRsa(SecurityPolicies.Aes128_Sha256_RsaOaep)
                .EncryptRsa(SecretBytes(), nonce);
            EncryptedSecret decryptor = CreateRsa(SecurityPolicies.Basic256Sha256);

            Assert.That(
                () => decryptor.TryDecryptRsa(encoded, nonce, out _),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityPolicyRejected));
        }

        [Test]
        public void TryDecryptRsaThrowsBadCertificateInvalidWhenCertificateHashMismatched()
        {
            byte[] nonce = NonceBytes();
            byte[] encoded = CreateRsa().EncryptRsa(SecretBytes(), nonce);

            using Certificate otherCertificate = CertificateBuilder
                .Create("CN=EncryptedSecret Other Subject, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            var decryptor = EncryptedSecret.CreateForRsa(
                m_context,
                SecurityPolicies.Basic256Sha256,
                otherCertificate);

            Assert.That(
                () => decryptor.TryDecryptRsa(encoded, nonce, out _),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadCertificateInvalid));
        }

        [Test]
        public void CreateForRsaThrowsArgumentExceptionForUnknownPolicy()
        {
            Assert.That(
                () => EncryptedSecret.CreateForRsa(
                    m_context,
                    "http://opcfoundation.org/UA/SecurityPolicy#DoesNotExist",
                    m_certificate),
                Throws.TypeOf<ArgumentException>()
                    .With.Property(nameof(ArgumentException.ParamName))
                    .EqualTo("securityPolicyUri"));
        }

        [Test]
        public void TryDecryptReturnsFalseForNullInput()
        {
            EncryptedSecret encryptedSecret = CreateRsa();

            bool ok = encryptedSecret.TryDecrypt(null!, NonceBytes(), out byte[] decrypted);

            Assert.That(ok, Is.False);
            Assert.That(decrypted, Is.Null);
        }

        [Test]
        public async Task TryDecryptAsyncReturnsFalseForNullInputAsync()
        {
            EncryptedSecret encryptedSecret = CreateRsa();

            (bool success, byte[] decrypted) = await encryptedSecret
                .TryDecryptAsync(null!, NonceBytes())
                .ConfigureAwait(false);

            Assert.That(success, Is.False);
            Assert.That(decrypted, Is.Null);
        }

        [TestCase(SecurityPolicies.ECC_nistP256)]
        [TestCase(SecurityPolicies.ECC_nistP384)]
        [Category("EncryptedSecretCoverage")]
        public void EccEncryptThenTryDecryptReturnsOriginalSecret(string policyUri)
        {
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            byte[] secret = SecretBytes();
            byte[] nonce = NonceBytes();

            EncryptedSecret encryptor = CreateEccEncryptor(
                policyUri, senderCertificate, receiverCertificate, receiverEphemeralKey, senderEphemeralKey);
            byte[] encoded = encryptor.Encrypt(secret, nonce);

            EncryptedSecret decryptor = CreateEccDecryptor(policyUri, receiverCertificate, receiverEphemeralKey);
            bool ok = decryptor.TryDecrypt(encoded, nonce, out byte[] decrypted);

            Assert.That(ok, Is.True);
            Assert.That(decrypted, Is.EqualTo(secret));
            decryptor.SenderCertificate?.Dispose();
        }

        [TestCase(SecurityPolicies.ECC_nistP256)]
        [TestCase(SecurityPolicies.ECC_nistP384)]
        [Category("EncryptedSecretCoverage")]
        public async Task EccEncryptThenTryDecryptAsyncReturnsOriginalSecretAsync(string policyUri)
        {
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            byte[] secret = SecretBytes();
            byte[] nonce = NonceBytes();

            EncryptedSecret encryptor = CreateEccEncryptor(
                policyUri, senderCertificate, receiverCertificate, receiverEphemeralKey, senderEphemeralKey);
            byte[] encoded = encryptor.Encrypt(secret, nonce);

            EncryptedSecret decryptor = CreateEccDecryptor(policyUri, receiverCertificate, receiverEphemeralKey);
            (bool success, byte[] decrypted) = await decryptor
                .TryDecryptAsync(encoded, nonce)
                .ConfigureAwait(false);

            Assert.That(success, Is.True);
            Assert.That(decrypted, Is.EqualTo(secret));
            decryptor.SenderCertificate?.Dispose();
        }

        [TestCase(SecurityPolicies.ECC_nistP256)]
        [TestCase(SecurityPolicies.ECC_nistP384)]
        [Category("EncryptedSecretCoverage")]
        public void TryDecryptEccAcceptsSpecCompliantCbcPayload(string policyUri)
        {
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            byte[] secret = SecretBytes();
            byte[] nonce = NonceBytes();
            byte[] encoded = CreateSpecCompliantEccEncryptedSecret(
                policyUri,
                senderCertificate,
                receiverCertificate,
                receiverEphemeralKey,
                senderEphemeralKey,
                secret,
                nonce);

            EncryptedSecret decryptor = CreateEccDecryptor(policyUri, receiverCertificate, receiverEphemeralKey);
            bool ok = decryptor.TryDecrypt(encoded, nonce, out byte[] decrypted);

            Assert.That(ok, Is.True);
            Assert.That(decrypted, Is.EqualTo(secret));
            decryptor.SenderCertificate?.Dispose();
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void EccEncryptThenTryDecryptReturnsOriginalSecretWhenSenderCertificateNotEncoded()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            byte[] secret = SecretBytes();
            byte[] nonce = NonceBytes();

            EncryptedSecret encryptor = CreateEccEncryptor(
                policyUri,
                senderCertificate,
                receiverCertificate,
                receiverEphemeralKey,
                senderEphemeralKey,
                doNotEncodeSenderCertificate: true);
            byte[] encoded = encryptor.Encrypt(secret, nonce);

            EncryptedSecret decryptor = CreateEccDecryptor(
                policyUri, receiverCertificate, receiverEphemeralKey, senderCertificate);
            bool ok = decryptor.TryDecrypt(encoded, nonce, out byte[] decrypted);

            Assert.That(ok, Is.True);
            Assert.That(decrypted, Is.EqualTo(secret));
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void CreateForEccSetsExpectedProperties()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);

            EncryptedSecret encryptor = CreateEccEncryptor(
                policyUri, senderCertificate, receiverCertificate, receiverEphemeralKey, senderEphemeralKey);

            Assert.Multiple(() =>
            {
                Assert.That(encryptor.SenderCertificate, Is.SameAs(senderCertificate));
                Assert.That(encryptor.ReceiverCertificate, Is.SameAs(receiverCertificate));
                Assert.That(encryptor.SenderNonce, Is.SameAs(senderEphemeralKey));
                Assert.That(encryptor.ReceiverNonce, Is.SameAs(receiverEphemeralKey));
                Assert.That(encryptor.SenderIssuerCertificates, Is.Empty);
                Assert.That(encryptor.DoNotEncodeSenderCertificate, Is.False);
                Assert.That(encryptor.Validator, Is.Null);
                Assert.That(encryptor.SecurityPolicy.Uri, Is.EqualTo(policyUri));
                Assert.That(encryptor.Context, Is.SameAs(m_context));
            });
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void CreateForRsaSetsExpectedProperties()
        {
            var encryptor = EncryptedSecret.CreateForRsa(
                m_context, SecurityPolicies.Basic256Sha256, m_certificate);

            Assert.Multiple(() =>
            {
                Assert.That(encryptor.ReceiverCertificate, Is.SameAs(m_certificate));
                Assert.That(encryptor.SenderCertificate, Is.Null);
                Assert.That(encryptor.SenderNonce, Is.Null);
                Assert.That(encryptor.ReceiverNonce, Is.Null);
                Assert.That(encryptor.SenderIssuerCertificates, Is.Null);
                Assert.That(encryptor.DoNotEncodeSenderCertificate, Is.False);
                Assert.That(encryptor.Validator, Is.Null);
                Assert.That(encryptor.SecurityPolicy.Uri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
                Assert.That(encryptor.Context, Is.SameAs(m_context));
            });
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void DecryptEccThrowsBadNonceInvalidWhenNonceMismatched()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            EncryptedSecret encryptor = CreateEccEncryptor(
                policyUri, senderCertificate, receiverCertificate, receiverEphemeralKey, senderEphemeralKey);
            byte[] encoded = encryptor.Encrypt(SecretBytes(), NonceBytes());
            EncryptedSecret decryptor = CreateEccDecryptor(policyUri, receiverCertificate, receiverEphemeralKey);
            byte[] wrongNonce = [0xAA, 0xBB, 0xCC, 0xDD];

            Assert.That(
                () => DecryptEcc(decryptor, wrongNonce, encoded),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNonceInvalid));
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public Task DecryptAsyncEccThrowsBadNonceInvalidWhenNonceMismatchedAsync()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            EncryptedSecret encryptor = CreateEccEncryptor(
                policyUri, senderCertificate, receiverCertificate, receiverEphemeralKey, senderEphemeralKey);
            byte[] encoded = encryptor.Encrypt(SecretBytes(), NonceBytes());
            EncryptedSecret decryptor = CreateEccDecryptor(policyUri, receiverCertificate, receiverEphemeralKey);
            byte[] wrongNonce = [0xAA, 0xBB, 0xCC, 0xDD];

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await decryptor
                    .DecryptAsync(
                        EarliestValidSigningTime(),
                        wrongNonce,
                        encoded,
                        0,
                        encoded.Length,
                        decryptor.Context.Telemetry)
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode.Code, Is.EqualTo(StatusCodes.BadNonceInvalid));
            decryptor.SenderCertificate?.Dispose();
            return Task.CompletedTask;
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void DecryptEccThrowsBadInvalidTimestampWhenSigningTimeBeforeEarliestTime()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            EncryptedSecret encryptor = CreateEccEncryptor(
                policyUri, senderCertificate, receiverCertificate, receiverEphemeralKey, senderEphemeralKey);
            byte[] encoded = encryptor.Encrypt(SecretBytes(), NonceBytes());
            EncryptedSecret decryptor = CreateEccDecryptor(policyUri, receiverCertificate, receiverEphemeralKey);
            DateTime earliestTimeInFuture = DateTime.UtcNow.AddDays(1);

            Assert.That(
                () => decryptor.Decrypt(
                    earliestTimeInFuture,
                    NonceBytes(),
                    encoded,
                    0,
                    encoded.Length,
                    decryptor.Context.Telemetry),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadInvalidTimestamp));
            decryptor.SenderCertificate?.Dispose();
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void DecryptEccThrowsBadDataTypeIdUnknownWhenTypeIdNotEcc()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            EncryptedSecret decryptor = CreateEccDecryptor(policyUri, receiverCertificate, receiverEphemeralKey);

            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteNodeId(null, DataTypeIds.RsaEncryptedSecret);
            encoder.WriteByte(null, (byte)ExtensionObjectEncoding.Binary);
            encoder.WriteUInt32(null, 0);
            byte[] blob = encoder.CloseAndReturnBuffer();

            Assert.That(
                () => DecryptEcc(decryptor, NonceBytes(), blob),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDataTypeIdUnknown));
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void DecryptEccThrowsBadDataEncodingUnsupportedWhenEncodingNotBinary()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            EncryptedSecret decryptor = CreateEccDecryptor(policyUri, receiverCertificate, receiverEphemeralKey);

            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteNodeId(null, DataTypeIds.EccEncryptedSecret);
            encoder.WriteByte(null, (byte)ExtensionObjectEncoding.Xml);
            encoder.WriteUInt32(null, 0);
            byte[] blob = encoder.CloseAndReturnBuffer();

            Assert.That(
                () => DecryptEcc(decryptor, NonceBytes(), blob),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDataEncodingUnsupported));
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void DecryptEccThrowsBadSecurityPolicyRejectedWhenHeaderPolicyIsRsa()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            EncryptedSecret decryptor = CreateEccDecryptor(policyUri, receiverCertificate, receiverEphemeralKey);

            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteNodeId(null, DataTypeIds.EccEncryptedSecret);
            encoder.WriteByte(null, (byte)ExtensionObjectEncoding.Binary);
            encoder.WriteUInt32(null, 0);
            encoder.WriteString(null, SecurityPolicies.Basic256Sha256);
            byte[] blob = encoder.CloseAndReturnBuffer();

            Assert.That(
                () => DecryptEcc(decryptor, NonceBytes(), blob),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityPolicyRejected));
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void DecryptEccThrowsBadSecurityPolicyRejectedWhenHeaderPolicyUnknown()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            EncryptedSecret decryptor = CreateEccDecryptor(policyUri, receiverCertificate, receiverEphemeralKey);

            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteNodeId(null, DataTypeIds.EccEncryptedSecret);
            encoder.WriteByte(null, (byte)ExtensionObjectEncoding.Binary);
            encoder.WriteUInt32(null, 0);
            encoder.WriteString(null, "http://opcfoundation.org/UA/SecurityPolicy#DoesNotExist");
            byte[] blob = encoder.CloseAndReturnBuffer();

            Assert.That(
                () => DecryptEcc(decryptor, NonceBytes(), blob),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityPolicyRejected));
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void DecryptEccThrowsBadCertificateInvalidWhenSenderCertificateMissing()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            EncryptedSecret encryptor = CreateEccEncryptor(
                policyUri,
                senderCertificate,
                receiverCertificate,
                receiverEphemeralKey,
                senderEphemeralKey,
                doNotEncodeSenderCertificate: true);
            byte[] encoded = encryptor.Encrypt(SecretBytes(), NonceBytes());
            EncryptedSecret decryptor = CreateEccDecryptor(policyUri, receiverCertificate, receiverEphemeralKey);

            Assert.That(
                () => DecryptEcc(decryptor, NonceBytes(), encoded),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadCertificateInvalid));
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void DecryptEccThrowsBadSecurityChecksFailedWhenSignatureTampered()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            EncryptedSecret encryptor = CreateEccEncryptor(
                policyUri, senderCertificate, receiverCertificate, receiverEphemeralKey, senderEphemeralKey);
            byte[] encoded = encryptor.Encrypt(SecretBytes(), NonceBytes());
            encoded[^1] ^= 0xFF;
            EncryptedSecret decryptor = CreateEccDecryptor(policyUri, receiverCertificate, receiverEphemeralKey);

            Assert.That(
                () => DecryptEcc(decryptor, NonceBytes(), encoded),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void DecryptEccThrowsBadDecodingErrorWhenReceiverNonceMismatched()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce otherReceiverEphemeralKey = CreateEphemeralKey(policyUri);
            EncryptedSecret encryptor = CreateEccEncryptor(
                policyUri, senderCertificate, receiverCertificate, receiverEphemeralKey, senderEphemeralKey);
            byte[] encoded = encryptor.Encrypt(SecretBytes(), NonceBytes());
            EncryptedSecret decryptor = CreateEccDecryptor(
                policyUri, receiverCertificate, otherReceiverEphemeralKey);

            Assert.That(
                () => DecryptEcc(decryptor, NonceBytes(), encoded),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void TryDecryptEccReturnsFalseWhenSignatureTampered()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            EncryptedSecret encryptor = CreateEccEncryptor(
                policyUri, senderCertificate, receiverCertificate, receiverEphemeralKey, senderEphemeralKey);
            byte[] encoded = encryptor.Encrypt(SecretBytes(), NonceBytes());
            encoded[^1] ^= 0xFF;
            EncryptedSecret decryptor = CreateEccDecryptor(policyUri, receiverCertificate, receiverEphemeralKey);

            bool ok = decryptor.TryDecrypt(encoded, NonceBytes(), out byte[] decrypted);

            Assert.That(ok, Is.False);
            Assert.That(decrypted, Is.Null);
            decryptor.SenderCertificate?.Dispose();
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public async Task TryDecryptAsyncEccReturnsFalseWhenSignatureTamperedAsync()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            EncryptedSecret encryptor = CreateEccEncryptor(
                policyUri, senderCertificate, receiverCertificate, receiverEphemeralKey, senderEphemeralKey);
            byte[] encoded = encryptor.Encrypt(SecretBytes(), NonceBytes());
            encoded[^1] ^= 0xFF;
            EncryptedSecret decryptor = CreateEccDecryptor(policyUri, receiverCertificate, receiverEphemeralKey);

            (bool success, byte[] decrypted) = await decryptor
                .TryDecryptAsync(encoded, NonceBytes())
                .ConfigureAwait(false);

            Assert.That(success, Is.False);
            Assert.That(decrypted, Is.Null);
            decryptor.SenderCertificate?.Dispose();
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void EncryptEccThrowsBadCertificateInvalidWhenSenderCertificateNull()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            var encryptor = EncryptedSecret.CreateForEcc(
                m_context,
                policyUri,
                [],
                receiverCertificate,
                receiverEphemeralKey,
                null!,
                senderEphemeralKey,
                validator: null);

            Assert.That(
                () => encryptor.Encrypt(SecretBytes(), NonceBytes()),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadCertificateInvalid));
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void EncryptEccThrowsBadArgumentsMissingWhenReceiverNonceMissing()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            var encryptor = EncryptedSecret.CreateForEcc(
                m_context,
                policyUri,
                [],
                senderCertificate,
                null!,
                senderCertificate,
                senderEphemeralKey,
                validator: null);

            Assert.That(
                () => encryptor.Encrypt(SecretBytes(), NonceBytes()),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadArgumentsMissing));
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void EncryptEccThrowsBadArgumentsMissingWhenSenderNonceMissing()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            var encryptor = EncryptedSecret.CreateForEcc(
                m_context,
                policyUri,
                [],
                senderCertificate,
                receiverEphemeralKey,
                senderCertificate,
                null!,
                validator: null);

            Assert.That(
                () => encryptor.Encrypt(SecretBytes(), NonceBytes()),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadArgumentsMissing));
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void EncryptRsaThrowsBadSecurityPolicyRejectedForEccPolicy()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            var encryptedSecret = EncryptedSecret.CreateForRsa(
                m_context, policyUri, m_certificate);

            Assert.That(
                () => encryptedSecret.EncryptRsa(SecretBytes(), NonceBytes()),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityPolicyRejected));
        }

        [Test]
        [Category("EncryptedSecretCoverage")]
        public void TryDecryptRsaReturnsFalseForEccPolicy()
        {
            const string policyUri = SecurityPolicies.ECC_nistP256;
            RequireEccPolicy(policyUri);
            var encryptedSecret = EncryptedSecret.CreateForRsa(
                m_context, policyUri, m_certificate);

            bool ok = encryptedSecret.TryDecryptRsa([1, 2, 3, 4, 5, 6, 7, 8], NonceBytes(), out byte[] decrypted);

            Assert.That(ok, Is.False);
            Assert.That(decrypted, Is.Null);
        }

        private EncryptedSecret CreateRsa(string policyUri = SecurityPolicies.Basic256Sha256)
        {
            return EncryptedSecret.CreateForRsa(m_context, policyUri, m_certificate);
        }

        /// <summary>
        /// The ECC secret is decrypted in place, over the buffer the caller
        /// handed in, and only the extracted key is copied out. The plain text
        /// left in that buffer is wiped - including on the failure path, which
        /// is the one that matters: the decryption runs before the signature and
        /// padding are verified, so a rejected message has the secret written
        /// out and nothing returned.
        /// </summary>
        [TestCase(SecurityPolicies.ECC_nistP256)]
        [TestCase(SecurityPolicies.ECC_nistP384)]
        [Category("EncryptedSecretCoverage")]
        public void EccDecryptWipesThePlainTextFromTheCallersBufferOnFailure(string policyUri)
        {
            RequireEccPolicy(policyUri);
            ECCurve curve = CurveForPolicy(policyUri);
            using Certificate senderCertificate = CreateEccCertificate(curve);
            using Certificate receiverCertificate = CreateEccCertificate(curve);
            using Nonce receiverEphemeralKey = CreateEphemeralKey(policyUri);
            using Nonce senderEphemeralKey = CreateEphemeralKey(policyUri);
            byte[] secret = SecretBytes();
            byte[] nonce = NonceBytes();

            EncryptedSecret encryptor = CreateEccEncryptor(
                policyUri,
                senderCertificate,
                receiverCertificate,
                receiverEphemeralKey,
                senderEphemeralKey);
            byte[] encoded = encryptor.Encrypt(secret, nonce);

            int longestZeroRunBefore = LongestZeroRun(encoded);

            EncryptedSecret decryptor = CreateEccDecryptor(
                policyUri, receiverCertificate, receiverEphemeralKey);

            bool accepted;

            try
            {
                // a nonce the message was not built for: rejected after the body
                // has already been decrypted in place.
                accepted = decryptor.TryDecrypt(encoded, [0xAA, 0xBB, 0xCC, 0xDD], out _);
            }
            catch (ServiceResultException)
            {
                accepted = false;
            }
            finally
            {
                decryptor.SenderCertificate?.Dispose();
            }

            Assert.That(accepted, Is.False);

            Assert.That(
                LongestZeroRun(encoded),
                Is.GreaterThan(longestZeroRunBefore + secret.Length),
                "the decrypted secret was left behind in the caller's buffer.");
        }

        private static int LongestZeroRun(byte[] buffer)
        {
            int longest = 0;
            int current = 0;

            foreach (byte value in buffer)
            {
                current = value == 0 ? current + 1 : 0;

                if (current > longest)
                {
                    longest = current;
                }
            }

            return longest;
        }

        private static byte[] SecretBytes()
        {
            return System.Text.Encoding.UTF8.GetBytes("opc-ua-encrypted-secret-roundtrip-value");
        }

        private static byte[] NonceBytes()
        {
            return [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        }

        private static void RequireEccPolicy(string policyUri)
        {
            if (SecurityPolicies.Default.GetInfo(policyUri) == null)
            {
                Assert.Ignore($"Security policy '{policyUri}' is not supported on this platform.");
            }
        }

        private static ECCurve CurveForPolicy(string policyUri)
        {
            return policyUri == SecurityPolicies.ECC_nistP384
                ? ECCurve.NamedCurves.nistP384
                : ECCurve.NamedCurves.nistP256;
        }

        private static Certificate CreateEccCertificate(ECCurve curve)
        {
            return CertificateBuilder
                .Create("CN=EncryptedSecret ECC Subject, O=OPC Foundation")
                .SetECCurve(curve)
                .CreateForECDsa();
        }

        private byte[] CreateSpecCompliantEccEncryptedSecret(
            string policyUri,
            Certificate senderCertificate,
            Certificate receiverCertificate,
            Nonce receiverEphemeralKey,
            Nonce senderEphemeralKey,
            byte[] secret,
            byte[] nonce)
        {
            SecurityPolicyInfo securityPolicy = SecurityPolicies.Default.GetInfo(policyUri)
                ?? throw new InvalidOperationException($"Security policy '{policyUri}' is not supported.");

            if (securityPolicy.SymmetricEncryptionAlgorithm is not
                (SymmetricEncryptionAlgorithm.Aes128Cbc or SymmetricEncryptionAlgorithm.Aes256Cbc))
            {
                throw new NotSupportedException("The test helper supports ECC security policies with CBC encryption only.");
            }

            byte[] senderNonce = senderEphemeralKey.Data ?? throw new InvalidOperationException("Missing sender nonce.");
            byte[] receiverNonce = receiverEphemeralKey.Data ?? throw new InvalidOperationException("Missing receiver nonce.");
            byte[] secretMaterial = senderEphemeralKey.GenerateSecret(receiverEphemeralKey, null)
                ?? throw new InvalidOperationException("Failed to derive the shared secret.");
            byte[] derivedKeyData = null;
            byte[] encryptingKey = null;
            byte[] iv = null;
            byte[] payload = null;
            byte[] encryptedPayload = null;

            try
            {
                byte[] keyLength = BitConverter.GetBytes(
                    (ushort)(securityPolicy.SymmetricEncryptionKeyLength + securityPolicy.InitializationVectorLength));
                byte[] salt = Utils.Append(keyLength, System.Text.Encoding.UTF8.GetBytes("opcua-secret"), senderNonce, receiverNonce);
                derivedKeyData = senderEphemeralKey.DeriveKeyData(
                    secretMaterial,
                    salt,
                    securityPolicy.KeyDerivationAlgorithm,
                    securityPolicy.SymmetricEncryptionKeyLength + securityPolicy.InitializationVectorLength);
                encryptingKey = new byte[securityPolicy.SymmetricEncryptionKeyLength];
                iv = new byte[securityPolicy.InitializationVectorLength];
                Buffer.BlockCopy(derivedKeyData, 0, encryptingKey, 0, encryptingKey.Length);
                Buffer.BlockCopy(derivedKeyData, encryptingKey.Length, iv, 0, iv.Length);

                using (var payloadEncoder = new BinaryEncoder(m_context))
                {
                    int startOfPayload = payloadEncoder.Position;
                    payloadEncoder.WriteByteString(null, nonce);
                    payloadEncoder.WriteByteString(null, secret);
                    int paddingCount = GetSpecPaddingCount(
                        securityPolicy.InitializationVectorLength,
                        secret.Length,
                        payloadEncoder.Position - startOfPayload);

                    for (int ii = 0; ii < paddingCount; ii++)
                    {
                        payloadEncoder.WriteByte(null, (byte)paddingCount);
                    }

                    payloadEncoder.WriteUInt16(null, (ushort)paddingCount);
                    payload = payloadEncoder.CloseAndReturnBuffer();
                }

                encryptedPayload = EncryptCbcWithoutPadding(payload!, encryptingKey, iv);
                int signatureLength = CryptoUtils.GetSignatureLength(senderCertificate);

                using var encoder = new BinaryEncoder(m_context);
                encoder.WriteNodeId(null, DataTypeIds.EccEncryptedSecret);
                encoder.WriteByte(null, (byte)ExtensionObjectEncoding.Binary);
                int lengthPosition = encoder.Position;
                encoder.WriteUInt32(null, 0);
                encoder.WriteString(null, securityPolicy.Uri);
                encoder.WriteByteString(null, senderCertificate.RawData);
                encoder.WriteDateTime(null, DateTime.UtcNow);
                encoder.WriteUInt16(null, (ushort)(senderNonce.Length + receiverNonce.Length + 8));
                encoder.WriteByteString(null, senderNonce);
                encoder.WriteByteString(null, receiverNonce);

                foreach (byte value in encryptedPayload)
                {
                    encoder.WriteByte(null, value);
                }

                for (int ii = 0; ii < signatureLength; ii++)
                {
                    encoder.WriteByte(null, 0);
                }

                byte[] encoded = encoder.CloseAndReturnBuffer()!;
                int length = encoded.Length - lengthPosition - 4;
                encoded[lengthPosition++] = (byte)(length & 0xFF);
                encoded[lengthPosition++] = (byte)((length >> 8) & 0xFF);
                encoded[lengthPosition++] = (byte)((length >> 16) & 0xFF);
                encoded[lengthPosition] = (byte)((length >> 24) & 0xFF);

                byte[] signature = CryptoUtils.Sign(
                    new ArraySegment<byte>(encoded, 0, encoded.Length - signatureLength),
                    senderCertificate,
                    securityPolicy.AsymmetricSignatureAlgorithm)
                    ?? throw new InvalidOperationException("Failed to sign the encrypted secret.");
                Buffer.BlockCopy(signature, 0, encoded, encoded.Length - signatureLength, signatureLength);
                return encoded;
            }
            finally
            {
                CryptoUtils.ZeroMemory(secretMaterial);
                CryptoUtils.ZeroMemory(derivedKeyData);
                CryptoUtils.ZeroMemory(encryptingKey);
                CryptoUtils.ZeroMemory(iv);
                CryptoUtils.ZeroMemory(payload);
                CryptoUtils.ZeroMemory(encryptedPayload);
            }
        }

        private static Nonce CreateEphemeralKey(string policyUri)
        {
            return Nonce.CreateNonce(SecurityPolicies.Default.GetInfo(policyUri)!);
        }

        private static byte[] EncryptCbcWithoutPadding(byte[] payload, byte[] encryptingKey, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = encryptingKey;
            aes.IV = iv;
            // Protocol vectors require the supplied derived IV. TODO: remove when CA5401 tracks nonce-derived IVs.
#pragma warning disable CA5401
            using ICryptoTransform encryptor = aes.CreateEncryptor();
#pragma warning restore CA5401
            return encryptor.TransformFinalBlock(payload, 0, payload.Length);
        }

        private static int GetSpecPaddingCount(int blockSize, int secretLength, int dataLength)
        {
            dataLength += 2;
            int paddingCount = dataLength % blockSize == 0
                ? 0
                : blockSize - (dataLength % blockSize);

            if (paddingCount + secretLength < blockSize)
            {
                paddingCount += blockSize;
            }

            return paddingCount;
        }

        private static DateTime EarliestValidSigningTime()
        {
            return DateTime.UtcNow.AddHours(-1);
        }

        private EncryptedSecret CreateEccEncryptor(
            string policyUri,
            Certificate senderCertificate,
            Certificate receiverCertificate,
            Nonce receiverEphemeralKey,
            Nonce senderEphemeralKey,
            bool doNotEncodeSenderCertificate = false)
        {
            return EncryptedSecret.CreateForEcc(
                m_context,
                policyUri,
                [],
                receiverCertificate,
                receiverEphemeralKey,
                senderCertificate,
                senderEphemeralKey,
                validator: null,
                doNotEncodeSenderCertificate: doNotEncodeSenderCertificate);
        }

        private EncryptedSecret CreateEccDecryptor(
            string policyUri,
            Certificate receiverCertificate,
            Nonce receiverEphemeralKey,
            Certificate senderCertificate = null)
        {
            return EncryptedSecret.CreateForEcc(
                m_context,
                policyUri,
                [],
                receiverCertificate,
                receiverEphemeralKey,
                senderCertificate!,
                null!,
                validator: null);
        }

        private byte[] DecryptEcc(EncryptedSecret decryptor, byte[] expectedNonce, byte[] data)
        {
            try
            {
                return decryptor.Decrypt(
                    EarliestValidSigningTime(), expectedNonce, data, 0, data.Length, decryptor.Context.Telemetry);
            }
            finally
            {
                // VerifyHeaderForEcc parses the sender certificate from the header and hands
                // ownership (an AddRef'd handle) to the decryptor; release it to avoid a leak.
                decryptor.SenderCertificate?.Dispose();
            }
        }
    }
}
