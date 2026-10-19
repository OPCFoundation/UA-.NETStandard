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
            IReadOnlyList<KeyValuePair<byte[], byte[]?>> withIds = token.FindCertificatesWithIds();

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
        public async Task StoreAddWritesATrustedCertificateToTheTokenAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate peer = CreatePeerCertificate("CN=TrustedPeer");

            await store.AddAsync(peer).ConfigureAwait(false);

            using CertificateCollection found = await store
                .FindByThumbprintAsync(peer.Thumbprint)
                .ConfigureAwait(false);
            using CertificateCollection all = await store.EnumerateAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(found, Has.Count.EqualTo(1));
                Assert.That(all, Has.Count.EqualTo(3));
                Assert.That(module.CreatedObjects, Is.EqualTo(1));
                Assert.That(
                    module.CertificateAttribute(peer.RawData, CKA.CKA_SUBJECT),
                    Is.EqualTo(peer.SubjectName.RawData));
                Assert.That(
                    module.CertificateAttribute(peer.RawData, CKA.CKA_ISSUER),
                    Is.EqualTo(peer.IssuerName.RawData));
                Assert.That(
                    module.CertificateAttribute(peer.RawData, CKA.CKA_SERIAL_NUMBER),
                    Is.EqualTo(DerInteger(peer.GetSerialNumber())));
            });
        }

        [Test]
        public async Task StoreAddDoesNotLinkACertificateWithAForeignRsaExponentToTheKeyAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            byte[] encoded = module.IssueRsaCertificateWithForeignExponent("CN=ForeignExponent");
            using Certificate foreign = Certificate.FromRawData(encoded);

            await store.AddAsync(foreign).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(module.CertificateId(encoded), Is.Not.Null);
                Assert.That(module.CertificateId(encoded), Is.Not.EqualTo(module.RsaId));
            });
        }

        [Test]
        public async Task CertificateWithoutIdIsNotPairedWithAnUnrelatedKeyAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate peer = CreatePeerCertificate("CN=PeerWithoutId");
            await store.AddAsync(peer).ConfigureAwait(false);
            module.SetCertificateId(peer.RawData, []);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(peer.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.That(loaded, Is.Null, "the token's keys belong to other certificates");
        }

        [Test]
        public async Task CertificateWithoutIdIsPairedWithTheKeyOfItsPublicKeyAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate identity = Certificate.FromRawData(module.RsaCertificate);
            module.SetCertificateId(module.RsaCertificate, []);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(identity.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded?.HasPrivateKey, Is.True);
            });
        }

        [Test]
        public async Task CertificateWithoutIdIsPairedWithTheKeyTheObjectLabelSelectsAsync()
        {
            using var module = new FakePkcs11Module();
            module.SetIdsOfLabel("ecc", []);
            Pkcs11TokenOptions options = module.CreateOptions();
            options.ObjectLabel = "ecc";
            using var store = new Pkcs11CertificateStore(NUnitTelemetryContext.Create(), options, module);
            store.Open("pkcs11:token=" + FakePkcs11Module.DefaultTokenLabel, noPrivateKeys: false);
            using Certificate identity = Certificate.FromRawData(module.EccCertificate);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(identity.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded?.HasPrivateKey, Is.True);
            });
        }

        [Test]
        public async Task CertificateWithoutIdIsNotPairedWithoutAnObjectFilterWhenNoKeyHasItsPublicKeyAsync()
        {
            using var module = new FakePkcs11Module();
            module.SetIdsOfLabel("ecc", []);
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate identity = Certificate.FromRawData(module.EccCertificate);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(identity.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.That(loaded, Is.Null, "no key on the token is linked to the certificate");
        }

        [Test]
        public async Task CertificateWithoutIdIsNotPairedWithAKeyThatOnlySharesItsLabelAsync()
        {
            using var module = new FakePkcs11Module();
            Pkcs11TokenOptions options = module.CreateOptions();
            options.ObjectLabel = "rsa";
            using var store = new Pkcs11CertificateStore(NUnitTelemetryContext.Create(), options, module);
            store.Open("pkcs11:token=" + FakePkcs11Module.DefaultTokenLabel, noPrivateKeys: false);
            using Certificate unrelated = CreatePeerCertificate("CN=SharesTheLabel");
            await store.AddAsync(unrelated).ConfigureAwait(false);
            module.SetCertificateId(unrelated.RawData, []);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(unrelated.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.That(loaded, Is.Null, "the key with the label does not sign for the certificate");
        }

        [Test]
        public async Task CertificateWithoutIdIsNotPairedWithAnotherKeyOfTheInferredIdAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate peer = CreatePeerCertificate("CN=SharesTheKeyId");
            using (RSA peerKey = peer.GetRSAPublicKey()!)
            {
                // A second key object with the token key's CKA_ID states the peer's public key.
                module.AddRsaPrivateKey(module.RsaId, "rsa", peerKey.ExportParameters(false));
            }
            await store.AddAsync(peer).ConfigureAwait(false);
            module.SetCertificateId(peer.RawData, []);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(peer.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.That(loaded, Is.Null, "no key with the inferred CKA_ID signs for the certificate");
        }

        [Test]
        public async Task CertificateWithoutIdIsPairedWithTheRsaKeyOfTheInferredIdThatSignsForItAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using RSA peerKey = RSA.Create(2048);
            using Certificate peer = CreatePeerCertificate("CN=SecondKeyOfTheId", peerKey);
            // The token's own key comes first among the keys with this CKA_ID.
            module.AddRsaPrivateKey(module.RsaId, "rsa", peerKey.ExportParameters(false), peerKey);
            await store.AddAsync(peer).ConfigureAwait(false);
            module.SetCertificateId(peer.RawData, []);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(peer.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            AssertAttachedKeySignsFor(loaded, peerKey);
        }

        [Test]
        public async Task CertificateWithoutIdIsPairedWithTheEcKeyOfTheInferredIdThatSignsForItAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using ECDsa peerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using Certificate peer = CreatePeerCertificate("CN=SecondEcKeyOfTheId", peerKey);
            // The token's own key comes first among the keys with this CKA_ID.
            module.AddEcKeyPair(module.EccId, "ecc", peerKey);
            await store.AddAsync(peer).ConfigureAwait(false);
            module.SetCertificateId(peer.RawData, []);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(peer.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            AssertAttachedKeySignsFor(loaded, peerKey);
        }

        [Test]
        public async Task CertificateWithoutIdIsPairedWithTheRsaKeyOfTheObjectLabelThatSignsForItAsync()
        {
            using var module = new FakePkcs11Module();
            Pkcs11TokenOptions options = module.CreateOptions();
            options.ObjectLabel = "rsa";
            using var store = new Pkcs11CertificateStore(NUnitTelemetryContext.Create(), options, module);
            store.Open("pkcs11:token=" + FakePkcs11Module.DefaultTokenLabel, noPrivateKeys: false);
            using RSA peerKey = RSA.Create(2048);
            using Certificate peer = CreatePeerCertificate("CN=SecondKeyOfTheLabel", peerKey);
            // Without a CKA_ID the key is found by the label only, after the token's own key.
            module.AddRsaPrivateKey([], "rsa", peerKey.ExportParameters(false), peerKey);
            await store.AddAsync(peer).ConfigureAwait(false);
            module.SetCertificateId(peer.RawData, []);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(peer.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            AssertAttachedKeySignsFor(loaded, peerKey);
        }

        [Test]
        public async Task CertificateWithoutIdIsPairedWithTheEcKeyOfTheObjectLabelThatSignsForItAsync()
        {
            using var module = new FakePkcs11Module();
            Pkcs11TokenOptions options = module.CreateOptions();
            options.ObjectLabel = "ecc";
            using var store = new Pkcs11CertificateStore(NUnitTelemetryContext.Create(), options, module);
            store.Open("pkcs11:token=" + FakePkcs11Module.DefaultTokenLabel, noPrivateKeys: false);
            using ECDsa peerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using Certificate peer = CreatePeerCertificate("CN=SecondEcKeyOfTheLabel", peerKey);
            // Without a CKA_ID the key is found by the label only, after the token's own key.
            module.AddEcKeyPair([], "ecc", peerKey);
            await store.AddAsync(peer).ConfigureAwait(false);
            module.SetCertificateId(peer.RawData, []);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(peer.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            AssertAttachedKeySignsFor(loaded, peerKey);
        }

        [Test]
        public async Task CertificateWithoutIdIsPairedByTheObjectLabelWhenItsInferredIdSelectsNoPrivateKeyAsync()
        {
            using var module = new FakePkcs11Module();
            // Only the public key object keeps its CKA_ID.
            module.SetIdsOfLabel("ecc", [], CKO.CKO_CERTIFICATE);
            module.SetIdsOfLabel("ecc", [], CKO.CKO_PRIVATE_KEY);
            Pkcs11TokenOptions options = module.CreateOptions();
            options.ObjectLabel = "ecc";
            using var store = new Pkcs11CertificateStore(NUnitTelemetryContext.Create(), options, module);
            store.Open("pkcs11:token=" + FakePkcs11Module.DefaultTokenLabel, noPrivateKeys: false);
            using Certificate identity = Certificate.FromRawData(module.EccCertificate);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(identity.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded?.HasPrivateKey, Is.True);
            });
        }

        [Test]
        public async Task CertificateWithoutIdIsPairedWithAnRsaKeyThatSignsOnlyWithPssAsync()
        {
            using var module = new FakePkcs11Module();
            // Only the object label links the certificate to its key, so the key must prove possession.
            module.SetIdsOfLabel("rsa", []);
            module.RefuseRsaPkcs1Signatures = true;
            Pkcs11TokenOptions options = module.CreateOptions();
            options.ObjectLabel = "rsa";
            using var store = new Pkcs11CertificateStore(NUnitTelemetryContext.Create(), options, module);
            store.Open("pkcs11:token=" + FakePkcs11Module.DefaultTokenLabel, noPrivateKeys: false);
            using Certificate identity = Certificate.FromRawData(module.RsaCertificate);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(identity.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded?.HasPrivateKey, Is.True);
            });
        }

        [Test]
        public async Task CertificateWhoseIdCannotBeReadIsNotPairedWithAnUnrelatedKeyAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate peer = CreatePeerCertificate("CN=UnrelatedPeer");
            await store.AddAsync(peer).ConfigureAwait(false);
            module.UnreadableAttributes.Add((ulong)CKA.CKA_ID);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(peer.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);
            using CertificateCollection found = await store
                .FindByThumbprintAsync(peer.Thumbprint)
                .ConfigureAwait(false);
            using Certificate other = CreatePeerCertificate("CN=AnotherPeer");
            await store.AddAsync(other).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.Null, "the token's RSA key belongs to another certificate");
                Assert.That(found, Has.Count.EqualTo(1), "the certificate itself is still found");
                Assert.That(module.CertificateId(other.RawData), Is.Not.Null, "adding still works");
            });
        }

        [Test]
        public async Task StoreAddSkipsAKeyWhoseExponentCannotBeReadAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate identity = Certificate.FromRawData(module.RsaCertificate);
            Assert.That(await store.DeleteAsync(identity.Thumbprint).ConfigureAwait(false), Is.True);
            module.UnreadableAttributes.Add((ulong)CKA.CKA_PUBLIC_EXPONENT);
            byte[] encoded = module.IssueRsaCertificate("CN=RenewedUnreadable");
            using Certificate renewed = Certificate.FromRawData(encoded);

            await store.AddAsync(renewed).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(module.CertificateId(encoded), Is.Not.Null, "the certificate is still written");
                Assert.That(
                    module.CertificateId(encoded),
                    Is.Not.EqualTo(module.RsaId),
                    "a key that cannot prove the whole public key is not paired");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StoreAddLinksAnEccCertificateOnlyToAKeyOnTheSameCurveAsync(bool otherCurve)
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate identity = Certificate.FromRawData(module.EccCertificate);
            // Without the certificate on the token the key can only be found
            // through the public key object.
            Assert.That(await store.DeleteAsync(identity.Thumbprint).ConfigureAwait(false), Is.True);
            if (otherCurve)
            {
                // secp384r1
                module.SetEccPublicKeyCurve([0x06, 0x05, 0x2B, 0x81, 0x04, 0x00, 0x22]);
            }
            byte[] encoded = module.IssueEccCertificate("CN=RenewedOnCurve");
            using Certificate renewed = Certificate.FromRawData(encoded);

            await store.AddAsync(renewed).ConfigureAwait(false);

            if (otherCurve)
            {
                Assert.That(module.CertificateId(encoded), Is.Not.EqualTo(module.EccId));
            }
            else
            {
                Assert.That(module.CertificateId(encoded), Is.EqualTo(module.EccId));
            }
        }

        [Test]
        public async Task StoreAddIsIdempotentAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate peer = CreatePeerCertificate("CN=TrustedPeer");

            await store.AddAsync(peer).ConfigureAwait(false);
            await store.AddAsync(peer).ConfigureAwait(false);

            Assert.That(module.CreatedObjects, Is.EqualTo(1));
        }

        [TestCase("RSA")]
        [TestCase("ECC")]
        public async Task StoreAddLinksARenewedCertificateToTheKeyOnTheTokenAsync(string keyType)
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            byte[] encoded = keyType == "RSA"
                ? module.IssueRsaCertificate("CN=Renewed")
                : module.IssueEccCertificate("CN=Renewed");
            using Certificate renewed = Certificate.FromRawData(encoded);

            await store.AddAsync(renewed).ConfigureAwait(false);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(renewed.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    module.CertificateId(encoded),
                    Is.EqualTo(keyType == "RSA" ? module.RsaId : module.EccId));
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.HasPrivateKey, Is.True);
                Assert.That(module.CountObjects(CKO.CKO_PRIVATE_KEY), Is.EqualTo(2));
            });
        }

        [Test]
        public async Task StoreAddLinksAnRsaCertificateToAKeyWhoseModulusHasLeadingZerosAsync()
        {
            using var module = new FakePkcs11Module();
            module.PadRsaModuli();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate previous = Certificate.FromRawData(module.RsaCertificate);
            Assert.That(await store.DeleteAsync(previous.Thumbprint).ConfigureAwait(false), Is.True);
            byte[] encoded = module.IssueRsaCertificate("CN=PaddedModulus");
            using Certificate renewed = Certificate.FromRawData(encoded);

            await store.AddAsync(renewed).ConfigureAwait(false);

            using Certificate? loaded = await store
                .LoadPrivateKeyAsync(renewed.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(module.CertificateId(encoded), Is.EqualTo(module.RsaId));
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.HasPrivateKey, Is.True);
            });
        }

        [Test]
        public async Task StoreAddWritesOnlyTheCertificateOfASoftwareIdentityAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using var key = RSA.Create(2048);
            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                "CN=SoftwareIdentity", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using Certificate identity = Certificate.From(request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1)));
            Assert.That(identity.HasPrivateKey, Is.True);

            await store.AddAsync(identity).ConfigureAwait(false);

            using CertificateCollection found = await store
                .FindByThumbprintAsync(identity.Thumbprint)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(found, Has.Count.EqualTo(1));
                Assert.That(found[0].HasPrivateKey, Is.False);
                Assert.That(store.RejectedPrivateKeyWrites, Is.EqualTo(1));
                Assert.That(module.CountObjects(CKO.CKO_PRIVATE_KEY), Is.EqualTo(2));
            });
        }

        [Test]
        public async Task StoreDeleteRemovesTheCertificateFromTheTokenAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate peer = CreatePeerCertificate("CN=TrustedPeer");
            await store.AddAsync(peer).ConfigureAwait(false);

            bool deleted = await store.DeleteAsync(peer.Thumbprint).ConfigureAwait(false);

            using CertificateCollection found = await store
                .FindByThumbprintAsync(peer.Thumbprint)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(deleted, Is.True);
                Assert.That(found, Is.Empty);
                Assert.That(module.DestroyedObjects, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task StoreDeleteOfAnIdentityKeepsItsKeyOnTheTokenAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);
            using Certificate identity = Certificate.FromRawData(module.RsaCertificate);

            bool deleted = await store.DeleteAsync(identity.Thumbprint).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(deleted, Is.True);
                Assert.That(module.CountObjects(CKO.CKO_CERTIFICATE), Is.EqualTo(1));
                Assert.That(module.CountObjects(CKO.CKO_PRIVATE_KEY), Is.EqualTo(2));
            });
        }

        [Test]
        public async Task StoreDeleteOfAnUnknownThumbprintReturnsFalseAsync()
        {
            using var module = new FakePkcs11Module();
            using Pkcs11CertificateStore store = OpenStore(module);

            bool deleted = await store
                .DeleteAsync("00112233445566778899AABBCCDDEEFF00112233")
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(deleted, Is.False);
                Assert.That(module.DestroyedObjects, Is.Zero);
            });
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

        private static Certificate CreatePeerCertificate(string subject)
        {
            using var key = RSA.Create(2048);
            return CreatePeerCertificate(subject, key);
        }

        private static Certificate CreatePeerCertificate(string subject, RSA key)
        {
            return CreateSelfSigned(new System.Security.Cryptography.X509Certificates.CertificateRequest(
                subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }

        private static Certificate CreatePeerCertificate(string subject, ECDsa key)
        {
            return CreateSelfSigned(new System.Security.Cryptography.X509Certificates.CertificateRequest(
                subject, key, HashAlgorithmName.SHA256));
        }

        private static Certificate CreateSelfSigned(System.Security.Cryptography.X509Certificates.CertificateRequest request)
        {
            using System.Security.Cryptography.X509Certificates.X509Certificate2 certificate =
                request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return Certificate.FromRawData(certificate.RawData);
        }

        /// <summary>
        /// Asserts that the key attached to the loaded certificate signs for the given public key.
        /// </summary>
        private static void AssertAttachedKeySignsFor(Certificate? loaded, RSA expected)
        {
            Assert.That(loaded, Is.Not.Null);
            using RSA attached = loaded!.GetRSAPrivateKey()!;
            byte[] hash = Hash(HashAlgorithmName.SHA256);
            byte[] signature = attached.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Assert.That(
                expected.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                Is.True,
                "the attached key is the certificate's");
        }

        /// <summary>
        /// Asserts that the key attached to the loaded certificate signs for the given public key.
        /// </summary>
        private static void AssertAttachedKeySignsFor(Certificate? loaded, ECDsa expected)
        {
            Assert.That(loaded, Is.Not.Null);
            using ECDsa attached = loaded!.GetECDsaPrivateKey()!;
            byte[] hash = Hash(HashAlgorithmName.SHA256);
            Assert.That(expected.VerifyHash(hash, attached.SignHash(hash)), Is.True, "the attached key is the certificate's");
        }

        private static byte[] DerInteger(byte[] littleEndianSerial)
        {
            var value = new List<byte>(littleEndianSerial);
            value.Reverse();
            if ((value[0] & 0x80) != 0)
            {
                value.Insert(0, 0);
            }
            value.InsertRange(0, [0x02, (byte)value.Count]);
            return [.. value];
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
