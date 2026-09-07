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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Core.TestFramework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Drives the whole store to channel path with a certificate whose private
    /// key lives in a simulated hardware token and can never be extracted.
    /// </summary>
    /// <remarks>
    /// This is the cross platform counterpart to the Windows CNG and TPM store:
    /// it exercises the detached key model that PKCS#11 tokens, cloud key
    /// services and any other provider outside the platform key storage
    /// providers have to use, on every operating system the stack supports.
    /// </remarks>
    [TestFixture]
    [Category("CertificateStore")]
    [Category("NonExportableKey")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class SimulatedHardwareStoreTests
    {
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_provider = new SimulatedHardwareCertificateStoreProvider();
            m_storePath = SimulatedHardwareCertificateStore.StoreScheme +
                "token/" + Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void TearDown()
        {
            // The provider owns the cached tokens and the certificates they hold.
            m_provider?.Dispose();
            m_provider = null;
        }

        [Test]
        public void ProviderRecognisesItsOwnScheme()
        {
            Assert.Multiple(() =>
            {
                Assert.That(m_provider.SupportsStorePath(m_storePath), Is.True);
                Assert.That(m_provider.SupportsStorePath("/tmp/pki/own"), Is.False);
                Assert.That(
                    m_provider.StoreTypeName,
                    Is.EqualTo(SimulatedHardwareCertificateStore.StoreTypeName));
            });
        }

        [Test]
        public void ProviderAwareStoreTypeDetectionFindsRegisteredProvider()
        {
            // Without providers the path is unrecognised and falls back to Directory.
            Assert.That(
                CertificateStoreIdentifier.DetermineStoreType(m_storePath),
                Is.EqualTo(CertificateStoreType.Directory),
                "Auto-detection cannot see a DI registered store type.");

            // With the provider supplied it resolves correctly.
            Assert.That(
                CertificateStoreIdentifier.DetermineStoreType(m_storePath, [m_provider]),
                Is.EqualTo(SimulatedHardwareCertificateStore.StoreTypeName));

            // A path the provider does not claim still falls through.
            Assert.That(
                CertificateStoreIdentifier.DetermineStoreType("/tmp/pki/own", [m_provider]),
                Is.EqualTo(CertificateStoreType.Directory));
        }

        [Test]
        public async Task LoadPrivateKeyReturnsDetachedKeyCertificateAsync()
        {
            SimulatedHardwareCertificateStore token = m_provider.GetStore(m_storePath);
            using Certificate generated = token.CreateRsaCertificate("CN=SimHwRsa");

            using ICertificateStore store = m_provider.CreateStore(m_telemetry);
            store.Open(m_storePath, false);

            using Certificate loaded = await store
                .LoadPrivateKeyAsync(generated.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            Assert.That(loaded, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(loaded.HasPrivateKey, Is.True);
                Assert.That(
                    loaded.HasDetachedPrivateKey,
                    Is.True,
                    "A token key must be detached; it cannot be owned by the certificate.");
                Assert.That(loaded.Thumbprint, Is.EqualTo(generated.Thumbprint));
            });
        }

        /// <summary>
        /// The whole point: a certificate that came out of the token can carry
        /// the secure channel handshake without the key ever being extracted.
        /// </summary>
        [Test]
        [TestCase(SecurityPolicies.Basic256Sha256)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss)]
        public async Task TokenCertificateCompletesChannelCryptoAsync(string securityPolicyUri)
        {
            if (SecurityPolicies.Default.GetInfo(securityPolicyUri) == null)
            {
                Assert.Ignore($"{securityPolicyUri} is not supported on this platform.");
            }

            SimulatedHardwareCertificateStore token = m_provider.GetStore(m_storePath);
            using Certificate generated = token.CreateRsaCertificate("CN=SimHwChannel");

            using ICertificateStore store = m_provider.CreateStore(m_telemetry);
            store.Open(m_storePath, false);
            using Certificate certificate = await store
                .LoadPrivateKeyAsync(generated.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            byte[] dataToSign = new byte[128];
            for (int ii = 0; ii < dataToSign.Length; ii++)
            {
                dataToSign[ii] = (byte)(ii * 3);
            }

            byte[] signature = CryptoUtils.Sign(
                new ArraySegment<byte>(dataToSign), certificate, securityPolicyUri);

            Assert.That(signature, Is.Not.Null.And.Not.Empty);
            Assert.That(
                CryptoUtils.Verify(
                    new ArraySegment<byte>(dataToSign), signature, certificate, securityPolicyUri),
                Is.True);

            byte[] secret = [10, 20, 30, 40];
            EncryptedData encrypted = SecurityPolicies.Default.Encrypt(
                certificate, securityPolicyUri, secret);
            byte[] decrypted = SecurityPolicies.Default.Decrypt(
                certificate, securityPolicyUri, encrypted);

            Assert.That(decrypted, Is.EqualTo(secret), "The token must be able to unwrap the peer secret.");
        }

        [Test]
        public async Task EccTokenCertificateCompletesChannelCryptoAsync()
        {
            if (SecurityPolicies.Default.GetInfo(SecurityPolicies.ECC_nistP256) == null)
            {
                Assert.Ignore("ECC_nistP256 is not supported on this platform.");
            }

            SimulatedHardwareCertificateStore token = m_provider.GetStore(m_storePath);
            using Certificate generated = token.CreateEcdsaCertificate(
                "CN=SimHwEcc", ECCurve.NamedCurves.nistP256);

            using ICertificateStore store = m_provider.CreateStore(m_telemetry);
            store.Open(m_storePath, false);
            using Certificate certificate = await store
                .LoadPrivateKeyAsync(generated.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);

            byte[] dataToSign = [1, 1, 2, 3, 5, 8, 13];
            byte[] signature = CryptoUtils.Sign(
                new ArraySegment<byte>(dataToSign), certificate, SecurityPolicies.ECC_nistP256);

            Assert.That(
                CryptoUtils.Verify(
                    new ArraySegment<byte>(dataToSign),
                    signature,
                    certificate,
                    SecurityPolicies.ECC_nistP256),
                Is.True);
        }

        /// <summary>
        /// A token cannot import key material it did not generate. The stack must
        /// not depend on writing a key back into the store.
        /// </summary>
        [Test]
        public async Task StoreRefusesToPersistPrivateKeyAsync()
        {
            SimulatedHardwareCertificateStore token = m_provider.GetStore(m_storePath);
            using Certificate generated = token.CreateRsaCertificate("CN=SimHwNoWrite");

            await token.AddAsync(generated).ConfigureAwait(false);

            Assert.That(
                token.RejectedPrivateKeyWrites,
                Is.GreaterThan(0),
                "Offering a private key to a token must be recorded as rejected, not performed.");

            CertificateCollection enumerated = await token.EnumerateAsync().ConfigureAwait(false);
            Assert.That(enumerated, Is.Not.Empty);
            foreach (Certificate certificate in enumerated)
            {
                Assert.That(
                    certificate.HasPrivateKey,
                    Is.False,
                    "Enumeration exposes public certificates only.");
                certificate.Dispose();
            }
        }

        /// <summary>
        /// Every handle opened on the same path must see the same token, the way
        /// two components opening the same slot would.
        /// </summary>
        [Test]
        public async Task StoreHandlesShareTheSameTokenAsync()
        {
            SimulatedHardwareCertificateStore token = m_provider.GetStore(m_storePath);
            using Certificate generated = token.CreateRsaCertificate("CN=SimHwShared");

            using (ICertificateStore first = m_provider.CreateStore(m_telemetry))
            {
                first.Open(m_storePath, false);
            }

            using ICertificateStore second = m_provider.CreateStore(m_telemetry);
            second.Open(m_storePath, false);

            CertificateCollection found = await second
                .FindByThumbprintAsync(generated.Thumbprint)
                .ConfigureAwait(false);

            Assert.That(
                found,
                Is.Not.Empty,
                "Disposing one handle must not take the token down with it.");
            foreach (Certificate certificate in found)
            {
                certificate.Dispose();
            }
        }

        private SimulatedHardwareCertificateStoreProvider m_provider;
        private string m_storePath;
        private ITelemetryContext m_telemetry;
    }
}
