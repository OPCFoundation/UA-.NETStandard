/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

// CA2000: test code; certificates are disposed through using declarations.
#pragma warning disable CA2000
using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Identity
{
    /// <summary>
    /// The legacy RSA token encryption decrypts one RSA block after another, so a
    /// client that only has to open a session could make the server run thousands
    /// of private-key operations per ActivateSession with a 1 MB token secret.
    /// These tests pin the block limit that rejects such a secret before the first
    /// private-key operation, and that the longest legitimate secrets still work.
    /// </summary>
    [TestFixture]
    [Category("Security")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class IdentityTokenSecretLimitTests
    {
        /// <summary>
        /// 1 MB of 256-byte blocks: the largest legacy secret a default message
        /// context accepts, ~2 s of RSA work for a 2048-bit key without the limit.
        /// </summary>
        private const int kOversizedBlockCount = 4095;

        private static readonly string[] s_rsaPolicies =
        [
            SecurityPolicies.Basic256Sha256,
            SecurityPolicies.Aes128_Sha256_RsaOaep,
            SecurityPolicies.Aes256_Sha256_RsaPss
        ];

        [Test]
        [TestCaseSource(nameof(s_rsaPolicies))]
        public async Task LegacyPasswordOfMaximumLengthIsDecryptedAsync(string policyUri)
        {
            IServiceMessageContext context = CreateContext();
            using Certificate certificate = CreateCertificate();
            byte[] nonce = CreateNonce(policyUri);
            byte[] password = RandomBytes(IdentityTokenLimits.MaxPasswordLength);

            UserNameIdentityTokenHandler handler = CreateLegacyUserNameHandler(
                certificate, policyUri, password, nonce);
            await handler.DecryptAsync(certificate, CreateNonce(policyUri, nonce), policyUri, context)
                .ConfigureAwait(false);

            Assert.That(handler.DecryptedPassword, Is.EqualTo(password));
        }

        [Test]
        [TestCaseSource(nameof(s_rsaPolicies))]
        public async Task LegacyPasswordAboveMaximumLengthIsRejectedAsync(string policyUri)
        {
            IServiceMessageContext context = CreateContext();
            using Certificate certificate = CreateCertificate();
            byte[] nonce = CreateNonce(policyUri);

            UserNameIdentityTokenHandler handler = CreateLegacyUserNameHandler(
                certificate, policyUri, RandomBytes(IdentityTokenLimits.MaxPasswordLength + 1), nonce);

            await AssertIdentityTokenInvalidAsync(async () => await handler
                .DecryptAsync(certificate, CreateNonce(policyUri, nonce), policyUri, context)
                .ConfigureAwait(false));
        }

        [Test]
        public async Task RsaEncryptedSecretPasswordIsBoundedTooAsync()
        {
            const string policyUri = SecurityPolicies.Basic256Sha256;
            IServiceMessageContext context = CreateContext();
            using Certificate certificate = CreateCertificate();
            byte[] nonce = CreateNonce(policyUri);

            // passwords above 64 bytes are sent as an RSAEncryptedSecret.
            byte[] password = RandomBytes(IdentityTokenLimits.MaxPasswordLength);
            var sender = new UserNameIdentityTokenHandler("user", password);
            await sender.EncryptAsync(certificate, nonce, policyUri, context).ConfigureAwait(false);
            Assert.That(((UserNameIdentityToken)sender.Token).EncryptionAlgorithm, Is.Null.Or.Empty);
            var receiver = new UserNameIdentityTokenHandler((UserNameIdentityToken)sender.Token);
            await receiver.DecryptAsync(certificate, CreateNonce(policyUri, nonce), policyUri, context)
                .ConfigureAwait(false);
            Assert.That(receiver.DecryptedPassword, Is.EqualTo(password));

            var tooLong = new UserNameIdentityTokenHandler(
                "user", RandomBytes(IdentityTokenLimits.MaxPasswordLength + 1));
            await tooLong.EncryptAsync(certificate, nonce, policyUri, context).ConfigureAwait(false);
            var tooLongReceiver = new UserNameIdentityTokenHandler((UserNameIdentityToken)tooLong.Token);
            await AssertIdentityTokenInvalidAsync(async () => await tooLongReceiver
                .DecryptAsync(certificate, CreateNonce(policyUri, nonce), policyUri, context)
                .ConfigureAwait(false));
        }

        /// <summary>
        /// The private key counts its decryptions, so a rejection with no
        /// decryption proves that the oversized secret is rejected before any
        /// private-key operation.
        /// </summary>
        [Test]
        public async Task OversizedLegacyPasswordIsRejectedBeforeThePrivateKeyIsUsedAsync()
        {
            const string policyUri = SecurityPolicies.Basic256Sha256;
            IServiceMessageContext context = CreateContext();
            using Certificate certificate = CreateCertificate();
            using var counter = new CountingRsa(certificate.GetRSAPrivateKey()!);
            using Certificate counting = certificate.CopyWithDetachedPrivateKey(counter, ownsPrivateKey: false);
            byte[] nonce = CreateNonce(policyUri);

            var token = new UserNameIdentityToken
            {
                UserName = "user",
                Password = RepeatBlock(certificate, kOversizedBlockCount).ToByteString(),
                EncryptionAlgorithm = SecurityAlgorithms.RsaOaep
            };
            var handler = new UserNameIdentityTokenHandler(token);

            ServiceResultException sre = await AssertIdentityTokenInvalidAsync(async () => await handler
                .DecryptAsync(counting, CreateNonce(policyUri, nonce), policyUri, context)
                .ConfigureAwait(false));
            Assert.That(sre.Message, Does.Contain("RSA blocks"));
            Assert.That(counter.DecryptCount, Is.Zero);
        }

        /// <summary>
        /// A password is bounded by the password limit, not by the larger limit
        /// the registry applies to any token: 10 blocks of a 2048-bit key would
        /// fit an issued token but are more than a 1 KiB password needs (5).
        /// </summary>
        [Test]
        public async Task LegacyPasswordUsesThePasswordBlockLimitAsync()
        {
            const string policyUri = SecurityPolicies.Basic256Sha256;
            IServiceMessageContext context = CreateContext();
            using Certificate certificate = CreateCertificate();
            using var counter = new CountingRsa(certificate.GetRSAPrivateKey()!);
            using Certificate counting = certificate.CopyWithDetachedPrivateKey(counter, ownsPrivateKey: false);
            byte[] nonce = CreateNonce(policyUri);

            var handler = new UserNameIdentityTokenHandler(new UserNameIdentityToken
            {
                UserName = "user",
                Password = RepeatBlock(certificate, 10).ToByteString(),
                EncryptionAlgorithm = SecurityAlgorithms.RsaOaep
            });

            ServiceResultException sre = await AssertIdentityTokenInvalidAsync(async () => await handler
                .DecryptAsync(counting, CreateNonce(policyUri, nonce), policyUri, context)
                .ConfigureAwait(false));
            Assert.That(sre.Message, Does.Contain("5 RSA blocks"));
            Assert.That(counter.DecryptCount, Is.Zero);
        }

        [Test]
        public async Task OversizedLegacyPasswordIsRejectedQuicklyAsync()
        {
            const string policyUri = SecurityPolicies.Basic256Sha256;
            IServiceMessageContext context = CreateContext();
            using Certificate certificate = CreateCertificate();
            byte[] nonce = CreateNonce(policyUri);

            var token = new UserNameIdentityToken
            {
                UserName = "user",
                Password = RepeatBlock(certificate, kOversizedBlockCount).ToByteString(),
                EncryptionAlgorithm = SecurityAlgorithms.RsaOaep
            };
            var handler = new UserNameIdentityTokenHandler(token);

            var stopwatch = Stopwatch.StartNew();
            await AssertIdentityTokenInvalidAsync(async () => await handler
                .DecryptAsync(certificate, CreateNonce(policyUri, nonce), policyUri, context)
                .ConfigureAwait(false));
            stopwatch.Stop();

            // decrypting all blocks takes ~2 s; the check needs no RSA work at all.
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(500));
        }

        [Test]
        [TestCaseSource(nameof(s_rsaPolicies))]
        public async Task LegacyIssuedTokenOfMaximumLengthIsDecryptedAsync(string policyUri)
        {
            IServiceMessageContext context = CreateContext();
            using Certificate certificate = CreateCertificate();
            byte[] nonce = CreateNonce(policyUri);
            byte[] tokenData = RandomBytes(IdentityTokenLimits.MaxIssuedTokenDataLength);

            var sender = new IssuedIdentityTokenHandler(Profiles.JwtUserToken, tokenData);
            await sender.EncryptAsync(certificate, nonce, policyUri, context).ConfigureAwait(false);
            Assert.That(((IssuedIdentityToken)sender.Token).EncryptionAlgorithm, Is.Not.Null.And.Not.Empty);

            var receiver = new IssuedIdentityTokenHandler((IssuedIdentityToken)sender.Token);
            await receiver.DecryptAsync(certificate, CreateNonce(policyUri, nonce), policyUri, context)
                .ConfigureAwait(false);

            Assert.That(receiver.DecryptedTokenData, Is.EqualTo(tokenData));
        }

        [Test]
        public async Task LegacyIssuedTokenAboveMaximumLengthIsRejectedAsync()
        {
            const string policyUri = SecurityPolicies.Basic256Sha256;
            IServiceMessageContext context = CreateContext();
            using Certificate certificate = CreateCertificate();
            byte[] nonce = CreateNonce(policyUri);

            var sender = new IssuedIdentityTokenHandler(
                Profiles.JwtUserToken,
                RandomBytes(IdentityTokenLimits.MaxIssuedTokenDataLength + 1));
            await sender.EncryptAsync(certificate, nonce, policyUri, context).ConfigureAwait(false);

            var receiver = new IssuedIdentityTokenHandler((IssuedIdentityToken)sender.Token);
            await AssertIdentityTokenInvalidAsync(async () => await receiver
                .DecryptAsync(certificate, CreateNonce(policyUri, nonce), policyUri, context)
                .ConfigureAwait(false));
        }

        [Test]
        public async Task OversizedLegacyIssuedTokenIsRejectedBeforeThePrivateKeyIsUsedAsync()
        {
            const string policyUri = SecurityPolicies.Basic256Sha256;
            IServiceMessageContext context = CreateContext();
            using Certificate certificate = CreateCertificate();
            using var counter = new CountingRsa(certificate.GetRSAPrivateKey()!);
            using Certificate counting = certificate.CopyWithDetachedPrivateKey(counter, ownsPrivateKey: false);
            byte[] nonce = CreateNonce(policyUri);

            var token = new IssuedIdentityToken
            {
                PolicyId = Profiles.JwtUserToken,
                TokenData = RepeatBlock(certificate, kOversizedBlockCount).ToByteString(),
                EncryptionAlgorithm = SecurityAlgorithms.RsaOaep
            };
            var handler = new IssuedIdentityTokenHandler(token);

            ServiceResultException sre = await AssertIdentityTokenInvalidAsync(async () => await handler
                .DecryptAsync(counting, CreateNonce(policyUri, nonce), policyUri, context)
                .ConfigureAwait(false));
            Assert.That(sre.Message, Does.Contain("RSA blocks"));
            Assert.That(counter.DecryptCount, Is.Zero);
        }

        /// <summary>
        /// The registry decrypts token secrets for any caller, so it bounds the
        /// blocks on its own as well (sync and async).
        /// </summary>
        [Test]
        public async Task SecurityPoliciesDecryptRejectsOversizedCipherTextBeforeThePrivateKeyIsUsedAsync()
        {
            using Certificate certificate = CreateCertificate();
            using var counter = new CountingRsa(certificate.GetRSAPrivateKey()!);
            using Certificate counting = certificate.CopyWithDetachedPrivateKey(counter, ownsPrivateKey: false);
            var encryptedData = new EncryptedData
            {
                Data = RepeatBlock(certificate, kOversizedBlockCount),
                Algorithm = SecurityAlgorithms.RsaOaep
            };

            ServiceResultException sre = await AssertIdentityTokenInvalidAsync(async () => await SecurityPolicies.Default
                .DecryptAsync(counting, SecurityPolicies.Basic256Sha256, encryptedData)
                .ConfigureAwait(false));
            Assert.That(sre.Message, Does.Contain("RSA blocks"));

            sre = Assert.Throws<ServiceResultException>(() => SecurityPolicies.Default
                .Decrypt(counting, SecurityPolicies.Basic256Sha256, encryptedData))!;
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
            Assert.That(sre.Message, Does.Contain("RSA blocks"));
            Assert.That(counter.DecryptCount, Is.Zero);

            // a secret within the limit reaches the private key (and fails its framing).
            encryptedData.Data = RepeatBlock(certificate, 2);
            Assert.Catch(() => SecurityPolicies.Default
                .Decrypt(counting, SecurityPolicies.Basic256Sha256, encryptedData));
            Assert.That(counter.DecryptCount, Is.EqualTo(2));
        }

        /// <summary>
        /// The encoder always appends a block (length / block size + 1), so a
        /// block-aligned plaintext of the maximum length takes one block more than
        /// it fills. The limit allows that block and nothing more.
        /// </summary>
        [Test]
        public void BlockAlignedMaximumPlainTextAllowsTheEncodersExtraBlock()
        {
            const int cipherBlock = 256;
            const int plainBlock = 214; // 2048-bit key, OAEP-SHA1
            const int maxPlainText = 4 * plainBlock;

            Assert.DoesNotThrow(() => IdentityTokenLimits.ThrowIfRsaCipherTextTooLong(
                5 * cipherBlock, cipherBlock, plainBlock, maxPlainText));
            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => IdentityTokenLimits.ThrowIfRsaCipherTextTooLong(
                    6 * cipherBlock, cipherBlock, plainBlock, maxPlainText))!;
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        /// <summary>
        /// A block-aligned plaintext of exactly the maximum length round-trips
        /// through the encoder and the bounded decoder (sync and async).
        /// </summary>
        [TestCase(0, TestName = "BlockAlignedMaximumPlainTextRoundTrips(Pkcs1)")]
        [TestCase(1, TestName = "BlockAlignedMaximumPlainTextRoundTrips(OaepSHA1)")]
        [TestCase(2, TestName = "BlockAlignedMaximumPlainTextRoundTrips(OaepSHA256)")]
        public async Task BlockAlignedMaximumPlainTextRoundTripsAsync(int paddingValue)
        {
            // RsaUtils.Padding is internal, so the cases pass its value.
            var padding = (RsaUtils.Padding)paddingValue;
            using Certificate certificate = CreateCertificate();
            ILogger logger = NUnitTelemetryContext.Create().CreateLogger<IdentityTokenSecretLimitTests>();
            int plainBlock = RsaUtils.GetPlainTextBlockSize(certificate, padding);
            int maxPlainText = 4 * plainBlock;
            byte[] data = RandomBytes(maxPlainText - sizeof(int));

            byte[] cipherText = RsaUtils.Encrypt(data, certificate, padding, logger);
            Assert.That(cipherText, Has.Length.EqualTo(5 * RsaUtils.GetCipherTextBlockSize(certificate)));

            byte[] decrypted = RsaUtils.Decrypt(
                new ArraySegment<byte>(cipherText), certificate, padding, logger, maxPlainText);
            Assert.That(decrypted, Is.EqualTo(data));

            decrypted = await RsaUtils.DecryptAsync(
                new ArraySegment<byte>(cipherText), certificate, padding, logger, maxPlainText)
                .ConfigureAwait(false);
            Assert.That(decrypted, Is.EqualTo(data));
        }

        /// <summary>
        /// An HSM or remote key (<see cref="IAsyncRsaKey"/>) gets the same limit:
        /// an oversized secret makes no key call, one within the limit round-trips.
        /// </summary>
        [Test]
        public async Task AsyncRsaKeyRejectsOversizedCipherTextBeforeTheKeyIsUsedAsync()
        {
            using Certificate certificate = CreateCertificate();
            ILogger logger = NUnitTelemetryContext.Create().CreateLogger<IdentityTokenSecretLimitTests>();

            var oversizedKey = new CountingAsyncRsa(certificate.GetRSAPrivateKey()!);
            ServiceResultException sre = await AssertIdentityTokenInvalidAsync(async () =>
                await RsaUtils.DecryptWithAsyncRsaAsync(
                    oversizedKey,
                    oversizedKey,
                    new ArraySegment<byte>(RepeatBlock(certificate, kOversizedBlockCount)),
                    RsaUtils.Padding.OaepSHA1,
                    logger,
                    IdentityTokenLimits.MaxLegacyPlainTextLength,
                    CancellationToken.None).ConfigureAwait(false));
            Assert.That(sre.Message, Does.Contain("RSA blocks"));
            Assert.That(oversizedKey.DecryptCount, Is.Zero);

            int maxPlainText = 4 * RsaUtils.GetPlainTextBlockSize(certificate, RsaUtils.Padding.OaepSHA1);
            byte[] data = RandomBytes(maxPlainText - sizeof(int));
            byte[] cipherText = RsaUtils.Encrypt(data, certificate, RsaUtils.Padding.OaepSHA1, logger);

            var key = new CountingAsyncRsa(certificate.GetRSAPrivateKey()!);
            byte[] decrypted = await RsaUtils.DecryptWithAsyncRsaAsync(
                key,
                key,
                new ArraySegment<byte>(cipherText),
                RsaUtils.Padding.OaepSHA1,
                logger,
                maxPlainText,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(decrypted, Is.EqualTo(data));
            Assert.That(key.DecryptCount, Is.EqualTo(5));
        }

        private static async Task<ServiceResultException> AssertIdentityTokenInvalidAsync(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid), sre.Message);
                return sre;
            }

            Assert.Fail("Expected Bad_IdentityTokenInvalid.");
            return null!;
        }

        private static UserNameIdentityTokenHandler CreateLegacyUserNameHandler(
            Certificate certificate,
            string policyUri,
            byte[] password,
            byte[] nonce)
        {
            byte[] dataToEncrypt = Utils.Append(password, nonce);
            EncryptedData encryptedData = SecurityPolicies.Default.Encrypt(certificate, policyUri, dataToEncrypt);

            return new UserNameIdentityTokenHandler(new UserNameIdentityToken
            {
                UserName = "user",
                Password = encryptedData.Data.ToByteString(),
                EncryptionAlgorithm = encryptedData.Algorithm
            });
        }

        private static byte[] RepeatBlock(Certificate certificate, int blockCount)
        {
            using RSA publicKey = certificate.GetRSAPublicKey()!;
            byte[] block = publicKey.Encrypt([1, 2, 3, 4], RSAEncryptionPadding.OaepSHA1);
            byte[] cipherText = new byte[block.Length * blockCount];
            for (int ii = 0; ii < blockCount; ii++)
            {
                Buffer.BlockCopy(block, 0, cipherText, ii * block.Length, block.Length);
            }
            return cipherText;
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }

        private static Certificate CreateCertificate()
        {
            return CertificateBuilder
                .Create("CN=Identity Token Secret Limit, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static byte[] CreateNonce(string policyUri)
        {
            SecurityPolicyInfo info = SecurityPolicies.Default.GetInfo(policyUri)!;
            return Nonce.CreateNonce(info.SecureChannelNonceLength).Data!;
        }

        private static Nonce CreateNonce(string policyUri, byte[] nonce)
        {
            return Nonce.CreateNonce(SecurityPolicies.Default.GetInfo(policyUri)!, nonce);
        }

        private static byte[] RandomBytes(int length)
        {
            byte[] bytes = new byte[length];
            using RandomNumberGenerator rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return bytes;
        }

        /// <summary>
        /// Forwards to a real key and counts the decryptions made through
        /// <see cref="IAsyncRsaKey"/> (an HSM or remote key).
        /// </summary>
        private sealed class CountingAsyncRsa : RSA, IAsyncRsaKey
        {
            private readonly RSA m_key;
            private int m_decryptCount;

            public CountingAsyncRsa(RSA key)
            {
                m_key = key;
                KeySizeValue = key.KeySize;
                LegalKeySizesValue = key.LegalKeySizes;
            }

            public int DecryptCount => Volatile.Read(ref m_decryptCount);

            public ValueTask<byte[]> SignHashAsync(
                ReadOnlyMemory<byte> hash,
                HashAlgorithmName hashAlgorithm,
                RSASignaturePadding padding,
                CancellationToken ct = default)
            {
                return new ValueTask<byte[]>(m_key.SignHash(hash.ToArray(), hashAlgorithm, padding));
            }

            public async ValueTask<byte[]> DecryptAsync(
                ReadOnlyMemory<byte> data,
                RSAEncryptionPadding padding,
                CancellationToken ct = default)
            {
                Interlocked.Increment(ref m_decryptCount);
                await Task.Yield();
                return m_key.Decrypt(data.ToArray(), padding);
            }

            public override RSAParameters ExportParameters(bool includePrivateParameters)
            {
                return m_key.ExportParameters(includePrivateParameters);
            }

            public override void ImportParameters(RSAParameters parameters)
            {
                throw new NotSupportedException();
            }

            public override byte[] Decrypt(byte[] data, RSAEncryptionPadding padding)
            {
                throw new NotSupportedException("Use DecryptAsync.");
            }

            public override byte[] Encrypt(byte[] data, RSAEncryptionPadding padding)
            {
                return m_key.Encrypt(data, padding);
            }

            public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
            {
                return m_key.SignHash(hash, hashAlgorithm, padding);
            }

            public override bool VerifyHash(
                byte[] hash,
                byte[] signature,
                HashAlgorithmName hashAlgorithm,
                RSASignaturePadding padding)
            {
                return m_key.VerifyHash(hash, signature, hashAlgorithm, padding);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_key.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Forwards to a real key and counts the private-key decryptions.
        /// </summary>
        private sealed class CountingRsa : RSA
        {
            private readonly RSA m_key;
            private int m_decryptCount;

            public CountingRsa(RSA key)
            {
                m_key = key;
                KeySizeValue = key.KeySize;
                LegalKeySizesValue = key.LegalKeySizes;
            }

            public int DecryptCount => Volatile.Read(ref m_decryptCount);

            public override RSAParameters ExportParameters(bool includePrivateParameters)
            {
                return m_key.ExportParameters(includePrivateParameters);
            }

            public override void ImportParameters(RSAParameters parameters)
            {
                throw new NotSupportedException();
            }

            public override byte[] Decrypt(byte[] data, RSAEncryptionPadding padding)
            {
                Interlocked.Increment(ref m_decryptCount);
                return m_key.Decrypt(data, padding);
            }

            public override byte[] Encrypt(byte[] data, RSAEncryptionPadding padding)
            {
                return m_key.Encrypt(data, padding);
            }

            public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
            {
                return m_key.SignHash(hash, hashAlgorithm, padding);
            }

            public override bool VerifyHash(
                byte[] hash,
                byte[] signature,
                HashAlgorithmName hashAlgorithm,
                RSASignaturePadding padding)
            {
                return m_key.VerifyHash(hash, signature, hashAlgorithm, padding);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_key.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
