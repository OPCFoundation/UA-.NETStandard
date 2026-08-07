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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Security.Pkcs11.Tests
{
    /// <summary>
    /// Tests the parts of the PKCS#11 store that hold regardless of whether a
    /// token is present.
    /// </summary>
    /// <remarks>
    /// The contract that matters most - that a private key is never written out
    /// and never exported - is testable without hardware, so it is tested here
    /// where it runs on every agent.
    /// </remarks>
    [TestFixture]
    [Category("Pkcs11")]
    [Parallelizable(ParallelScope.All)]
    public class Pkcs11StoreProviderTests
    {
        [Test]
        public void ProviderReportsThePkcs11StoreType()
        {
            var provider = new Pkcs11StoreProvider();

            Assert.That(provider.StoreTypeName, Is.EqualTo(Pkcs11CertificateStore.StoreTypeName));
        }

        [Test]
        [TestCase("pkcs11:token=t", true)]
        [TestCase("pkcs11:token=t?module-path=/tmp/m.so", true)]
        [TestCase("/some/directory", false)]
        [TestCase("LocalMachine\\My", false)]
        public void ProviderClaimsOnlyPkcs11StorePaths(string storePath, bool expected)
        {
            var provider = new Pkcs11StoreProvider();

            Assert.That(provider.SupportsStorePath(storePath), Is.EqualTo(expected));
        }

        [Test]
        public void CreateStoreReturnsAPkcs11Store()
        {
            var provider = new Pkcs11StoreProvider();

            using ICertificateStore store = provider.CreateStore(NUnitTelemetryContext.Create());

            Assert.Multiple(() =>
            {
                Assert.That(store.StoreType, Is.EqualTo(Pkcs11CertificateStore.StoreTypeName));
                Assert.That(store.SupportsLoadPrivateKey, Is.True);
                Assert.That(store.SupportsCRLs, Is.False);
                Assert.That(store.NoPrivateKeys, Is.False);
            });
        }

        [Test]
        public void OpenRejectsAPathThatIsNotAPkcs11Uri()
        {
            using var store = new Pkcs11CertificateStore(NUnitTelemetryContext.Create());

            Assert.Throws<ArgumentException>(() => store.Open("/some/directory"));
        }

        [Test]
        public void OpenAcceptsAPkcs11Uri()
        {
            using var store = new Pkcs11CertificateStore(NUnitTelemetryContext.Create());

            store.Open("pkcs11:token=t?module-path=/tmp/does-not-exist.so");

            Assert.That(store.StorePath, Is.EqualTo("pkcs11:token=t?module-path=/tmp/does-not-exist.so"));
        }

        [Test]
        public async Task AddRefusesToWriteAPrivateKeyAsync()
        {
            using var store = new Pkcs11CertificateStore(
                NUnitTelemetryContext.Create(),
                Pkcs11TestEnvironment.CreateOptions());

            using RSA key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=Pkcs11WriteTest", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

            using Certificate certificate = Certificate.FromRawData(selfSigned.RawData)
                .CopyWithDetachedPrivateKey(key, ownsPrivateKey: false);

            Assume.That(certificate.HasPrivateKey, Is.True);

            await store.AddAsync(certificate).ConfigureAwait(false);

            Assert.That(
                store.RejectedPrivateKeyWrites,
                Is.EqualTo(1),
                "the store must record, and not perform, an attempt to persist key material");
        }

        [Test]
        public void DeleteIsNotSupported()
        {
            using var store = new Pkcs11CertificateStore(
                NUnitTelemetryContext.Create(),
                Pkcs11TestEnvironment.CreateOptions());

            Assert.ThrowsAsync<NotSupportedException>(
                async () => await store.DeleteAsync("abcdef").ConfigureAwait(false));
        }

        [Test]
        public void CrlOperationsAreNotSupported()
        {
            using var store = new Pkcs11CertificateStore(
                NUnitTelemetryContext.Create(),
                Pkcs11TestEnvironment.CreateOptions());

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<NotSupportedException>(
                    async () => await store.AddCRLAsync(null!).ConfigureAwait(false));
                Assert.ThrowsAsync<NotSupportedException>(
                    async () => await store.DeleteCRLAsync(null!).ConfigureAwait(false));
            });
        }

        /// <summary>
        /// With no thumbprint, subject or application uri there is nothing to
        /// match on, and any key on the token would satisfy the request.
        /// </summary>
        /// <remarks>
        /// This is the guard against returning the wrong identity.
        /// <c>CertificateIdentifierResolver</c> makes a last-chance call with the
        /// thumbprint and subject deliberately dropped, relying on the
        /// application uri alone; a store that ignored that argument would hand
        /// back whatever certificate it enumerated first. On a token holding more
        /// than one identity - the reason an HSM is used at all - that is an
        /// impersonation, not a miss.
        /// </remarks>
        [Test]
        public async Task LoadPrivateKeyRefusesToGuessWithNoIdentifiersAsync()
        {
            using var store = new Pkcs11CertificateStore(
                NUnitTelemetryContext.Create(),
                Pkcs11TestEnvironment.CreateOptions());

            // Returns before any token is opened, so this holds without hardware.
            Certificate loaded = await store
                .LoadPrivateKeyAsync(null!, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.That(
                loaded,
                Is.Null,
                "an unconstrained request must not select an arbitrary key");
        }

        [Test]
        public void OpenStripsThePinFromTheReportedStorePath()
        {
            using var store = new Pkcs11CertificateStore(NUnitTelemetryContext.Create());

            store.Open("pkcs11:token=t?module-path=/tmp/m.so&pin-value=supersecret");

            Assert.Multiple(() =>
            {
                Assert.That(
                    store.StorePath,
                    Does.Not.Contain("supersecret"),
                    "the PIN unlocks the token and must not travel with the store path");
                Assert.That(store.StorePath, Does.Contain("token=t"));
                Assert.That(store.StorePath, Does.Contain("module-path=/tmp/m.so"));
            });
        }

        [Test]
        [TestCase("pkcs11:token=t?module-path=/tmp/m.so&pin-value=1234", false)]
        [TestCase("pkcs11:token=t?pin-value=1234&module-path=/tmp/m.so", false)]
        [TestCase("pkcs11:token=t?module-path=/tmp/m.so", true)]
        [TestCase("pkcs11:token=t", true)]
        public void RedactPinRemovesOnlyThePin(string uri, bool unchanged)
        {
            string redacted = Pkcs11TokenOptions.RedactPin(uri);

            Assert.Multiple(() =>
            {
                Assert.That(redacted, Does.Not.Contain("1234"));
                Assert.That(redacted == uri, Is.EqualTo(unchanged));

                // Redaction must not destroy the addressing information.
                Assert.That(Pkcs11TokenOptions.Parse(redacted).TokenLabel, Is.EqualTo("t"));
            });
        }

        [Test]
        public void CryptoProviderDefaultsToUncertified()
        {
            var provider = new Pkcs11CryptoProvider();

            Assert.Multiple(() =>
            {
                Assert.That(provider.Name, Is.EqualTo("PKCS11"));
                Assert.That(
                    provider.Validation.Level,
                    Is.EqualTo(CryptoValidationLevel.Uncertified),
                    "nothing in PKCS#11 reports a validation certificate, so none may be assumed");
                Assert.That(provider.Validation.IsAcceptableForFips, Is.False);
                Assert.That(provider.Capabilities, Is.Not.Empty);
            });
        }

        [Test]
        public void CryptoProviderAcceptsAnAssertedValidation()
        {
            var provider = new Pkcs11CryptoProvider(
                default,
                new CryptoValidationStatus(
                    CryptoValidationLevel.FipsValidated,
                    "Vendor HSM",
                    "CMVP #1234"),
                "vendor-hsm");

            Assert.Multiple(() =>
            {
                Assert.That(provider.Name, Is.EqualTo("vendor-hsm"));
                Assert.That(provider.Validation.CertificateReference, Is.EqualTo("CMVP #1234"));
                Assert.That(provider.Validation.IsAcceptableForFips, Is.True);
            });
        }

        [Test]
        public void CryptoProviderServesTheRequestedPurpose()
        {
            var provider = new Pkcs11CryptoProvider(
                new ArrayOf<CryptoCapability>(
                    new CryptoCapability[] { new(CryptoPurpose.UserIdentityKey) }));

            var registry = new CryptoProviderRegistry();
            registry.RegisterFor(CryptoPurpose.UserIdentityKey, provider);

            ICryptoProvider resolved = registry.Resolve(CryptoPurpose.UserIdentityKey);

            Assert.That(resolved, Is.SameAs(provider));
        }
    }
}
