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
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Security.Pkcs11.Tests
{
    /// <summary>
    /// Exercises a private key that genuinely never leaves a PKCS#11 token.
    /// </summary>
    /// <remarks>
    /// This is the test the whole feature exists for. Everything else covers the
    /// code paths with a substitute; here the key really is on a device, and the
    /// assertions are that the stack can still sign and decrypt with it and that
    /// nothing ever gets the key material out.
    /// <para>
    /// The tests skip when no module is configured. CI installs SoftHSM2 and
    /// provisions a token, so the coverage is real there. See
    /// <see cref="Pkcs11TestEnvironment"/> for the configuration.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Pkcs11")]
    [Category("Hardware")]
    [NonParallelizable]
    public class Pkcs11TokenTests
    {
        [SetUp]
        public void SetUp()
        {
            if (!Pkcs11TestEnvironment.IsAvailable)
            {
                Assert.Ignore(Pkcs11TestEnvironment.SkipReason);
            }

            RequireProvisionedToken();
        }

        /// <summary>
        /// Skips when the module is present but the token holds nothing to test
        /// against.
        /// </summary>
        /// <remarks>
        /// A module being installed is not the same as a token being usable. An
        /// agent can easily have SoftHSM2 without a provisioned token, and that
        /// should cost coverage rather than break the build - CI is what
        /// guarantees the token is there, and its setup step fails loudly if the
        /// provisioning does not work.
        /// </remarks>
        private static void RequireProvisionedToken()
        {
            if (s_provisioned.HasValue)
            {
                if (!s_provisioned.Value)
                {
                    Assert.Ignore(kUnprovisionedReason);
                }

                return;
            }

            bool provisioned = false;

            try
            {
                using var store = new Pkcs11CertificateStore(
                    NUnitTelemetryContext.Create(),
                    Pkcs11TestEnvironment.CreateOptions());

                store.Open(Pkcs11TestEnvironment.CreateStorePath(), noPrivateKeys: false);

                using CertificateCollection certificates = store.EnumerateAsync()
                    .GetAwaiter()
                    .GetResult();

                provisioned = certificates.Count > 0;
            }
            catch (Exception ex) when (ex is CryptographicException or InvalidOperationException)
            {
                // No matching token, or the module refused to open it.
                provisioned = false;
            }

            s_provisioned = provisioned;

            if (!provisioned)
            {
                Assert.Ignore(kUnprovisionedReason);
            }
        }

        private const string kUnprovisionedReason =
            "A PKCS#11 module is available but the token holds no certificate. " +
            "Provision it (see the SoftHSM2 setup in .github/workflows/buildandtest.yml) " +
            "to run the token backed tests.";

        private static bool? s_provisioned;

        [Test]
        public async Task EnumerateReturnsTheCertificatesOnTheTokenAsync()
        {
            using Pkcs11CertificateStore store = OpenStore();

            CertificateCollection certificates = await store.EnumerateAsync()
                .ConfigureAwait(false);

            Assert.That(
                certificates,
                Is.Not.Empty,
                "the token must be provisioned with at least one certificate");
        }

        [Test]
        public async Task LoadPrivateKeyReturnsAUsableKeyAsync()
        {
            using Pkcs11CertificateStore store = OpenStore();

            using Certificate? certificate = await LoadFirstKeyAsync(store)
                .ConfigureAwait(false);

            Assert.That(certificate, Is.Not.Null, "no certificate on the token has a private key");

            Assert.That(
                certificate!.HasPrivateKey,
                Is.True,
                "the token key must be attached to the certificate");
        }

        [Test]
        public async Task PrivateKeyRefusesToBeExportedAsync()
        {
            using Pkcs11CertificateStore store = OpenStore();

            using Certificate? certificate = await LoadFirstKeyAsync(store)
                .ConfigureAwait(false);

            Assert.That(certificate, Is.Not.Null);

            using RSA? key = certificate!.GetRSAPrivateKey();

            Assert.That(key, Is.Not.Null, "the token key must surface as an RSA");

            Assert.Multiple(() =>
            {
                Assert.Throws<CryptographicException>(
                    () => key!.ExportParameters(true),
                    "private key material must never leave the token");
                Assert.DoesNotThrow(
                    () => key!.ExportParameters(false),
                    "the public key must still be readable");
            });
        }

        [Test]
        public async Task SignWithPkcs1RoundTripsAsync()
        {
            await AssertSignRoundTripAsync(RSASignaturePadding.Pkcs1).ConfigureAwait(false);
        }

        [Test]
        public async Task SignWithPssRoundTripsAsync()
        {
            await AssertSignRoundTripAsync(RSASignaturePadding.Pss).ConfigureAwait(false);
        }

        [Test]
        public async Task DecryptWithOaepRoundTripsAsync()
        {
            using Pkcs11CertificateStore store = OpenStore();

            using Certificate? certificate = await LoadFirstKeyAsync(store)
                .ConfigureAwait(false);

            Assert.That(certificate, Is.Not.Null);

            using RSA? key = certificate!.GetRSAPrivateKey();

            Assert.That(key, Is.Not.Null);

            byte[] plaintext = [1, 2, 3, 4, 5, 6, 7, 8];
            byte[] encrypted = key!.Encrypt(plaintext, RSAEncryptionPadding.OaepSHA256);

            byte[] decrypted = key.Decrypt(encrypted, RSAEncryptionPadding.OaepSHA256);

            Assert.That(decrypted, Is.EqualTo(plaintext));
        }

        [Test]
        public async Task StoreResolvesThroughTheProviderAsync()
        {
            var provider = new Pkcs11StoreProvider();

            Assert.That(provider.SupportsStorePath(Pkcs11TestEnvironment.CreateStorePath()), Is.True);

            using ICertificateStore store = provider.CreateStore(NUnitTelemetryContext.Create());

            store.Open(Pkcs11TestEnvironment.CreateStorePath(), noPrivateKeys: false);

            CertificateCollection certificates = await store.EnumerateAsync()
                .ConfigureAwait(false);

            Assert.That(certificates, Is.Not.Null);
        }

        private static async Task AssertSignRoundTripAsync(RSASignaturePadding padding)
        {
            using Pkcs11CertificateStore store = OpenStore();

            using Certificate? certificate = await LoadFirstKeyAsync(store)
                .ConfigureAwait(false);

            Assert.That(certificate, Is.Not.Null);

            using RSA? key = certificate!.GetRSAPrivateKey();

            Assert.That(key, Is.Not.Null);

            byte[] data = [9, 8, 7, 6, 5, 4, 3, 2, 1];
#if NET6_0_OR_GREATER
            byte[] hash = SHA256.HashData(data);
#else
            byte[] hash;

            using (SHA256 sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(data);
            }
#endif

            byte[] signature = key!.SignHash(hash, HashAlgorithmName.SHA256, padding);

            Assert.That(
                key.VerifyHash(hash, signature, HashAlgorithmName.SHA256, padding),
                Is.True,
                $"a signature produced on the token with {padding} must verify against the " +
                "certificate's public key");
        }

        private static async Task<Certificate?> LoadFirstKeyAsync(Pkcs11CertificateStore store)
        {
            CertificateCollection certificates = await store.EnumerateAsync()
                .ConfigureAwait(false);

            foreach (Certificate certificate in certificates)
            {
                Certificate? withKey = await store.LoadPrivateKeyAsync(
                        certificate.Thumbprint,
                        null,
                        null,
                        NodeId.Null,
                        null)
                    .ConfigureAwait(false);

                if (withKey != null)
                {
                    return withKey;
                }
            }

            return null;
        }

        private static Pkcs11CertificateStore OpenStore()
        {
            var store = new Pkcs11CertificateStore(
                NUnitTelemetryContext.Create(),
                Pkcs11TestEnvironment.CreateOptions());

            store.Open(Pkcs11TestEnvironment.CreateStorePath(), noPrivateKeys: false);

            return store;
        }
    }
}
