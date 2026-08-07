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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Core.TestFramework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Configuration
{
    /// <summary>
    /// Covers staging a regenerated private key that lives in a hardware token,
    /// which is the flow Part 12 uses between CreateSigningRequest and
    /// UpdateCertificate.
    /// </summary>
    [TestFixture]
    [Category("CryptoProvider")]
    [Category("NonExportableKey")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class HardwarePendingCertificateKeyStoreTests
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
        public async Task DeviceHeldKeyIsStagedAndRecoveredAsync()
        {
            SimulatedHardwareCertificateStore token = m_provider.GetStore(m_storePath);
            using Certificate generated = token.CreateRsaCertificate("CN=PendingHardware");

            var store = new HardwarePendingCertificateKeyStore(m_provider);
            PendingCertificateKeyContext context = CreateContext();

            bool saved = await store.SaveAsync(context, generated).ConfigureAwait(false);
            Assert.That(saved, Is.True, "A device held key needs no export to be durable.");

            using Certificate recovered = await store.TryTakeAsync(context).ConfigureAwait(false);

            Assert.That(recovered, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(recovered.Thumbprint, Is.EqualTo(generated.Thumbprint));
                Assert.That(recovered.HasPrivateKey, Is.True);
                Assert.That(
                    recovered.HasDetachedPrivateKey,
                    Is.True,
                    "The key must come back still held by the device.");
            });
        }

        /// <summary>
        /// The pending key is consumed exactly once, so a replayed
        /// UpdateCertificate cannot pick it up again.
        /// </summary>
        [Test]
        public async Task StagedKeyIsConsumedOnceAsync()
        {
            SimulatedHardwareCertificateStore token = m_provider.GetStore(m_storePath);
            using Certificate generated = token.CreateRsaCertificate("CN=PendingOnce");

            var store = new HardwarePendingCertificateKeyStore(m_provider);
            PendingCertificateKeyContext context = CreateContext();

            await store.SaveAsync(context, generated).ConfigureAwait(false);

            using (Certificate first = await store.TryTakeAsync(context).ConfigureAwait(false))
            {
                Assert.That(first, Is.Not.Null);
            }

            Certificate second = await store.TryTakeAsync(context).ConfigureAwait(false);
            Assert.That(second, Is.Null, "The staged key must not be handed out twice.");
        }

        [Test]
        public async Task RemoveDiscardsTheStagedKeyAsync()
        {
            SimulatedHardwareCertificateStore token = m_provider.GetStore(m_storePath);
            using Certificate generated = token.CreateRsaCertificate("CN=PendingRemoved");

            var store = new HardwarePendingCertificateKeyStore(m_provider);
            PendingCertificateKeyContext context = CreateContext();

            await store.SaveAsync(context, generated).ConfigureAwait(false);
            await store.RemoveAsync(context).ConfigureAwait(false);

            Certificate taken = await store.TryTakeAsync(context).ConfigureAwait(false);
            Assert.That(taken, Is.Null);
        }

        /// <summary>
        /// A software key must be declined so the caller falls back to a store
        /// that knows how to protect exportable key material.
        /// </summary>
        [Test]
        public async Task SoftwareKeyIsDeclinedAsync()
        {
            using RSA key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=SoftwarePending", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            using Certificate software = Certificate.From(
                X509CertificateLoader.LoadPkcs12(
                    selfSigned.Export(X509ContentType.Pfx, string.Empty),
                    string.Empty,
                    X509KeyStorageFlags.Exportable));

            var store = new HardwarePendingCertificateKeyStore(m_provider);

            bool saved = await store
                .SaveAsync(CreateContext(), software)
                .ConfigureAwait(false);

            Assert.That(saved, Is.False);
        }

        private PendingCertificateKeyContext CreateContext()
        {
            return new PendingCertificateKeyContext(
                new CertificateStoreIdentifier(
                    m_storePath, SimulatedHardwareCertificateStore.StoreTypeName, false),
                new NodeId(1000),
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                null,
                m_telemetry);
        }

        private SimulatedHardwareCertificateStoreProvider m_provider;
        private string m_storePath;
        private ITelemetryContext m_telemetry;
    }
}
