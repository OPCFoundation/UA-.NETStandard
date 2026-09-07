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

#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Net.Pkcs11Interop.Common;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Security.Pkcs11.Tests
{
    /// <summary>
    /// Drives the token, the key wrappers and the store against a module that
    /// behaves like a device but needs none.
    /// </summary>
    /// <remarks>
    /// The module signs and decrypts with a real software key, so a signature
    /// produced here has to verify against the certificate's public key. That
    /// makes these tests of the PKCS#11 translation - DigestInfo prefixes, PSS
    /// salt and MGF1, OAEP parameters - rather than of the mock.
    /// </remarks>
    [TestFixture]
    [Category("Pkcs11")]
    [NonParallelizable]
    [SetCulture("en-us")]
    public class Pkcs11TokenBackedTests
    {
        [Test]
        public void TokenOpensLogsInAndSelectsBySlot()
        {
            using var module = new FakePkcs11Module();

            Pkcs11TokenOptions options = module.CreateOptions();
            options.TokenLabel = null;
            options.SlotId = FakePkcs11Module.DefaultSlotId;

            using Pkcs11Token token = module.OpenToken(options);

            Assert.Multiple(() =>
            {
                Assert.That(module.LoadedModulePath, Is.EqualTo("/fake/module.so"));
                Assert.That(module.Logins, Is.EqualTo(1), "a PIN means a login");
                Assert.That(token.Options, Is.SameAs(options));
            });
        }

        [Test]
        public void TokenSelectsBySerial()
        {
            using var module = new FakePkcs11Module();

            Pkcs11TokenOptions options = module.CreateOptions();
            options.TokenLabel = null;
            options.TokenSerial = FakePkcs11Module.DefaultSerial;

            using Pkcs11Token token = module.OpenToken(options);

            Assert.That(token.Options.TokenSerial, Is.EqualTo(FakePkcs11Module.DefaultSerial));
        }

        [Test]
        public void TokenWithoutAPinOpensAPublicSession()
        {
            using var module = new FakePkcs11Module();

            using Pkcs11Token token = module.OpenToken(module.CreateOptions(pin: null));

            Assert.That(module.Logins, Is.Zero, "no PIN means no login");
        }

        [Test]
        [TestCase("other-label", null, null)]
        [TestCase(null, "9999999999", null)]
        public void TokenFailsWhenNothingMatches(string? label, string? serial, string? unused)
        {
            using var module = new FakePkcs11Module();

            Pkcs11TokenOptions options = module.CreateOptions();
            options.TokenLabel = label;
            options.TokenSerial = serial;

            CryptographicException error = Assert.Throws<CryptographicException>(
                () => module.OpenToken(options))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.Message, Does.Contain("No PKCS#11 token matched"));
                Assert.That(
                    module.LibraryDisposed,
                    Is.True,
                    "a failed open must not leave the module loaded");
            });
        }

        [Test]
        public void TokenFailsWhenTheModuleReportsNoSlots()
        {
            using var module = new FakePkcs11Module { NoSlots = true };

            Assert.Throws<CryptographicException>(() => module.OpenToken());
        }

        [Test]
        public void TokenRejectsMissingOptionsAndLoader()
        {
            using var module = new FakePkcs11Module();

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() => new Pkcs11Token(null!, module));
                Assert.Throws<ArgumentNullException>(
                    () => new Pkcs11Token(module.CreateOptions(), null!));
                Assert.Throws<ArgumentException>(
                    () => module.OpenToken(new Pkcs11TokenOptions { TokenLabel = "x" }));
            });
        }

        [Test]
        public void TokenFindsCertificatesAndTheirIds()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11Token token = module.OpenToken();

            IReadOnlyList<byte[]> certificates = token.FindCertificates();
            IReadOnlyList<KeyValuePair<byte[], byte[]>> withIds = token.FindCertificatesWithIds();

            Assert.Multiple(() =>
            {
                Assert.That(certificates, Has.Count.EqualTo(2), "one RSA and one ECC identity");
                Assert.That(withIds, Has.Count.EqualTo(2));
                Assert.That(withIds[0].Value, Is.Not.Empty, "the CKA_ID links a cert to its key");
            });
        }

        [Test]
        public void TokenFindsAPrivateKeyByIdAndKeyType()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11Token token = module.OpenToken();

            Assert.Multiple(() =>
            {
                Assert.That(token.FindPrivateKey(CKK.CKK_RSA, module.RsaId), Is.Not.Null);
                Assert.That(token.FindPrivateKey(CKK.CKK_EC, module.EccId), Is.Not.Null);
                Assert.That(
                    token.FindPrivateKey(CKK.CKK_RSA, module.EccId),
                    Is.Null,
                    "the key type and the id must both match");
            });
        }

        [Test]
        public void TokenLogsOutAndDisposesOnce()
        {
            using var module = new FakePkcs11Module();
            var token = module.OpenToken();

            token.Dispose();
            token.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(module.Logouts, Is.EqualTo(1), "disposal is idempotent");
                Assert.That(module.SessionDisposed, Is.True);
                Assert.That(module.LibraryDisposed, Is.True);
            });
        }

        /// <summary>
        /// A device that was pulled out fails the logout, and the token has to
        /// finish tearing down anyway.
        /// </summary>
        [Test]
        public void TokenToleratesAFailedLogout()
        {
            using var module = new FakePkcs11Module { ThrowOnLogout = true };
            var token = module.OpenToken();

            Assert.DoesNotThrow(token.Dispose);
            Assert.That(module.LibraryDisposed, Is.True);
        }

        /// <summary>
        /// The reference count is what keeps a key usable after the store that
        /// produced it is disposed.
        /// </summary>
        [Test]
        public void TokenSessionOutlivesTheFirstHolder()
        {
            using var module = new FakePkcs11Module();
            var token = module.OpenToken();

            token.AddRef();
            token.Dispose();

            Assert.That(
                module.SessionDisposed,
                Is.False,
                "a second holder still needs the session");

            token.Dispose();

            Assert.That(module.SessionDisposed, Is.True, "the last release closes it");
        }

        [Test]
        [TestCase("SHA256")]
        [TestCase("SHA384")]
        [TestCase("SHA512")]
        public async Task RsaKeySignsPkcs1ThatVerifiesAsync(string hashName)
        {
            var algorithm = new HashAlgorithmName(hashName);

            using Certificate certificate = await LoadRsaAsync().ConfigureAwait(false);
            using RSA key = certificate.GetRSAPrivateKey()!;

            byte[] hash = Hash(algorithm);
            byte[] signature = key.SignHash(hash, algorithm, RSASignaturePadding.Pkcs1);

            Assert.That(
                key.VerifyHash(hash, signature, algorithm, RSASignaturePadding.Pkcs1),
                Is.True,
                "a wrong DigestInfo prefix would produce a signature that does not verify");
        }

        [Test]
        [TestCase("SHA256")]
        [TestCase("SHA384")]
        [TestCase("SHA512")]
        public async Task RsaKeySignsPssThatVerifiesAsync(string hashName)
        {
            var algorithm = new HashAlgorithmName(hashName);

            using Certificate certificate = await LoadRsaAsync().ConfigureAwait(false);
            using RSA key = certificate.GetRSAPrivateKey()!;

            byte[] hash = Hash(algorithm);
            byte[] signature = key.SignHash(hash, algorithm, RSASignaturePadding.Pss);

            Assert.That(
                key.VerifyHash(hash, signature, algorithm, RSASignaturePadding.Pss),
                Is.True,
                "a mismatched MGF1 or salt length would not verify");
        }

        [Test]
        public async Task RsaKeyDecryptsOaepAndPkcs1Async()
        {
            using Certificate certificate = await LoadRsaAsync().ConfigureAwait(false);
            using RSA key = certificate.GetRSAPrivateKey()!;

            byte[] plaintext = [1, 2, 3, 4, 5, 6, 7, 8];

            Assert.Multiple(() =>
            {
                Assert.That(
                    key.Decrypt(
                        key.Encrypt(plaintext, RSAEncryptionPadding.OaepSHA256),
                        RSAEncryptionPadding.OaepSHA256),
                    Is.EqualTo(plaintext));
                Assert.That(
                    key.Decrypt(
                        key.Encrypt(plaintext, RSAEncryptionPadding.Pkcs1),
                        RSAEncryptionPadding.Pkcs1),
                    Is.EqualTo(plaintext));
            });
        }

        [Test]
        public async Task RsaKeyRefusesUnsupportedPaddingAsync()
        {
            using Certificate certificate = await LoadRsaAsync().ConfigureAwait(false);
            using RSA key = certificate.GetRSAPrivateKey()!;

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => key.SignHash([1], HashAlgorithmName.SHA256, null!));
                Assert.Throws<ArgumentNullException>(
                    () => key.Decrypt(null!, RSAEncryptionPadding.Pkcs1));
                Assert.Throws<CryptographicException>(
                    () => key.SignHash(
                        Hash(HashAlgorithmName.SHA1), HashAlgorithmName.SHA1,
                        RSASignaturePadding.Pkcs1),
                    "SHA-1 is not offered for new signatures");
            });
        }

        [Test]
        public async Task RsaKeyKeepsThePrivateKeyOnTheTokenAsync()
        {
            using Certificate certificate = await LoadRsaAsync().ConfigureAwait(false);
            using RSA key = certificate.GetRSAPrivateKey()!;

            Assert.Multiple(() =>
            {
                Assert.Throws<CryptographicException>(() => key.ExportParameters(true));
                Assert.Throws<NotSupportedException>(() => key.ImportParameters(default));
                Assert.DoesNotThrow(() => key.ExportParameters(false));
                Assert.That(key.KeySize, Is.EqualTo(2048));
                Assert.That(key.SignatureAlgorithm, Is.EqualTo("RSA"));
                Assert.That(key.KeyExchangeAlgorithm, Is.EqualTo("RSA"));
            });
        }

        [Test]
        public async Task RsaKeySignsDataThroughTheBaseClassAsync()
        {
            using Certificate certificate = await LoadRsaAsync().ConfigureAwait(false);
            using RSA key = certificate.GetRSAPrivateKey()!;

            byte[] data = [9, 9, 9, 9, 9];

            // Exercises the HashData overrides the base class routes through.
            byte[] signature = key.SignData(
                data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            Assert.That(
                key.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                Is.True);
        }

        [Test]
        public async Task EccKeySignsAndVerifiesAsync()
        {
            using Certificate certificate = await LoadEccAsync().ConfigureAwait(false);
            using ECDsa key = certificate.GetECDsaPrivateKey()!;

            byte[] hash = Hash(HashAlgorithmName.SHA256);
            byte[] signature = key.SignHash(hash);

            Assert.Multiple(() =>
            {
                Assert.That(key.VerifyHash(hash, signature), Is.True);
                Assert.That(key.KeySize, Is.EqualTo(256));
                Assert.That(key.SignatureAlgorithm, Is.EqualTo("ECDsa"));
            });
        }

        [Test]
        public async Task EccKeySignsDataThroughTheBaseClassAsync()
        {
            using Certificate certificate = await LoadEccAsync().ConfigureAwait(false);
            using ECDsa key = certificate.GetECDsaPrivateKey()!;

            byte[] data = [3, 1, 4, 1, 5];
            byte[] signature = key.SignData(data, HashAlgorithmName.SHA256);

            Assert.That(key.VerifyData(data, signature, HashAlgorithmName.SHA256), Is.True);
        }

        [Test]
        public async Task EccKeyKeepsThePrivateKeyOnTheTokenAsync()
        {
            using Certificate certificate = await LoadEccAsync().ConfigureAwait(false);
            using ECDsa key = certificate.GetECDsaPrivateKey()!;

            Assert.Multiple(() =>
            {
                Assert.Throws<CryptographicException>(() => key.ExportParameters(true));
                Assert.Throws<CryptographicException>(() => key.ExportExplicitParameters(true));
                Assert.Throws<NotSupportedException>(() => key.ImportParameters(default));
                Assert.Throws<NotSupportedException>(
                    () => key.GenerateKey(ECCurve.NamedCurves.nistP384));
                Assert.DoesNotThrow(() => key.ExportParameters(false));
                Assert.Throws<ArgumentNullException>(() => key.SignHash(null!));
            });
        }

        [Test]
        public async Task StoreEnumeratesWhatTheTokenHoldsAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);

            using CertificateCollection certificates = await store
                .EnumerateAsync()
                .ConfigureAwait(false);

            Assert.That(certificates, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task StoreFindsByThumbprintAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate expected = Certificate.FromRawData(module.RsaCertificate);

            using CertificateCollection found = await store
                .FindByThumbprintAsync(expected.Thumbprint)
                .ConfigureAwait(false);

            using CertificateCollection missing = await store
                .FindByThumbprintAsync("00112233445566778899AABBCCDDEEFF00112233")
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(found, Has.Count.EqualTo(1));
                Assert.That(missing, Is.Empty);
            });
        }

        [Test]
        public async Task StoreLoadsByThumbprintAndBySubjectAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate expected = Certificate.FromRawData(module.RsaCertificate);

            using Certificate? byThumbprint = await store
                .LoadPrivateKeyAsync(expected.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            using Certificate? bySubject = await store
                .LoadPrivateKeyAsync(null!, "CN=FakeTokenRsa", null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(byThumbprint, Is.Not.Null);
                Assert.That(byThumbprint!.HasDetachedPrivateKey, Is.True);
                Assert.That(bySubject, Is.Not.Null);
                Assert.That(bySubject!.Thumbprint, Is.EqualTo(expected.Thumbprint));
            });
        }

        /// <summary>
        /// The security fix: a subject must match as a distinguished name, not
        /// as a substring, or CN=Server would select CN=ServerBackup.
        /// </summary>
        [Test]
        public async Task StoreDoesNotMatchASubjectByPrefixAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(null!, "CN=FakeToken", null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.That(
                loaded,
                Is.Null,
                "a prefix of the subject must not select the certificate");
        }

        [Test]
        public async Task StoreHonoursTheCertificateTypeAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate rsa = Certificate.FromRawData(module.RsaCertificate);

            using Certificate? wrongType = await store
                .LoadPrivateKeyAsync(
                    rsa.Thumbprint,
                    null,
                    null,
                    ObjectTypeIds.EccNistP256ApplicationCertificateType,
                    null)
                .ConfigureAwait(false);

            Assert.That(
                wrongType,
                Is.Null,
                "an RSA certificate must not satisfy a request for an ECC type");
        }

        [Test]
        public async Task StoreLoadsAnEccIdentityAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate expected = Certificate.FromRawData(module.EccCertificate);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(expected.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.That(loaded, Is.Not.Null);

            using ECDsa key = loaded!.GetECDsaPrivateKey()!;

            Assert.That(key, Is.Not.Null, "an ECC certificate must yield an ECDsa");
        }

        /// <summary>
        /// The key has to keep working after the store that produced it is gone,
        /// which is what the resolver does.
        /// </summary>
        [Test]
        public async Task KeyOutlivesTheStoreThatProducedItAsync()
        {
            using var module = new FakePkcs11Module();

            Certificate? certificate;

            using (Pkcs11CertificateStore store = OpenStore(module))
            {
                using Certificate expected = Certificate.FromRawData(module.RsaCertificate);
                certificate = await store
                    .LoadPrivateKeyAsync(expected.Thumbprint, null, null, NodeId.Null, null)
                    .ConfigureAwait(false);
            }

            Assert.That(certificate, Is.Not.Null);

            using (certificate)
            {
                using RSA key = certificate!.GetRSAPrivateKey()!;

                byte[] hash = Hash(HashAlgorithmName.SHA256);

                Assert.That(
                    key.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                    Is.Not.Empty,
                    "the session must outlive the store, or the resolver breaks every key");
            }
        }

        private static byte[] Hash(HashAlgorithmName algorithm)
        {
            byte[] data = [1, 2, 3, 4, 5];

            // CA5350: SHA-1 appears only so the test can prove the provider
            // refuses it. Nothing here signs with it.
#pragma warning disable CA5350
            using HashAlgorithm hash = algorithm.Name switch
            {
                "SHA384" => SHA384.Create(),
                "SHA512" => SHA512.Create(),
                "SHA1" => (HashAlgorithm)SHA1.Create(),
                _ => SHA256.Create()
            };
#pragma warning restore CA5350

            return hash.ComputeHash(data);
        }

        private static Pkcs11CertificateStore OpenStore(FakePkcs11Module module)
        {
            var store = new Pkcs11CertificateStore(
                NUnitTelemetryContext.Create(), module.CreateOptions(), module);

            store.Open("pkcs11:token=" + FakePkcs11Module.DefaultTokenLabel, noPrivateKeys: false);

            return store;
        }

        private static async Task<Certificate> LoadRsaAsync()
        {
            var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate expected = Certificate.FromRawData(module.RsaCertificate);

            Certificate? loaded = await store
                .LoadPrivateKeyAsync(expected.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.That(loaded, Is.Not.Null);
            return loaded!;
        }

        private static async Task<Certificate> LoadEccAsync()
        {
            var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate expected = Certificate.FromRawData(module.EccCertificate);

            Certificate? loaded = await store
                .LoadPrivateKeyAsync(expected.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.That(loaded, Is.Not.Null);
            return loaded!;
        }
    }
}
