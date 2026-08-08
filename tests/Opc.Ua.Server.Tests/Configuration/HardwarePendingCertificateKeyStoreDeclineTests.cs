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
    /// Covers the paths where the hardware pending key store declines rather
    /// than proceeds.
    /// </summary>
    /// <remarks>
    /// The contract requires a store that cannot durably persist to return
    /// <c>false</c> rather than claim success, so the declines matter as much as
    /// the happy path - a wrong answer here means a pending key silently lost
    /// between CreateSigningRequest and UpdateCertificate.
    /// </remarks>
    [TestFixture]
    [Category("CryptoProvider")]
    [Category("NonExportableKey")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class HardwarePendingCertificateKeyStoreDeclineTests
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
            m_provider?.Dispose();
            m_provider = null;
        }

        [Test]
        public void SaveRejectsNullArguments()
        {
            var store = new HardwarePendingCertificateKeyStore(m_provider!);

            using Certificate software = CreateSoftwareCertificate();

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await store.SaveAsync(null!, software)
                        .ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await store.SaveAsync(CreateContext(), null!)
                        .ConfigureAwait(false));
            });
        }

        [Test]
        public void TryTakeAndRemoveRejectNullContext()
        {
            var store = new HardwarePendingCertificateKeyStore(m_provider!);

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await store.TryTakeAsync(null!).ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await store.RemoveAsync(null!).ConfigureAwait(false));
            });
        }

        [Test]
        public void ConstructorRejectsANullProvider()
        {
            Assert.Throws<ArgumentNullException>(
                () => new HardwarePendingCertificateKeyStore(null!));
        }

        /// <summary>
        /// A key the caller could export is not this store's business: it should
        /// fall back to one that knows how to protect exportable material.
        /// </summary>
        [Test]
        public async Task SoftwareKeyIsDeclinedAsync()
        {
            var store = new HardwarePendingCertificateKeyStore(m_provider!);

            using Certificate software = CreateSoftwareCertificate();

            bool saved = await store.SaveAsync(CreateContext(), software).ConfigureAwait(false);

            Assert.That(saved, Is.False);
        }

        [Test]
        public async Task TakingFromAnEmptyScopeReturnsNullAsync()
        {
            var store = new HardwarePendingCertificateKeyStore(m_provider!);

            Certificate? taken = await store.TryTakeAsync(CreateContext()).ConfigureAwait(false);

            Assert.That(taken, Is.Null, "nothing was staged, so nothing comes back");
        }

        [Test]
        public async Task RemovingFromAnEmptyScopeIsHarmlessAsync()
        {
            var store = new HardwarePendingCertificateKeyStore(m_provider!);

            Assert.DoesNotThrowAsync(
                async () => await store.RemoveAsync(CreateContext()).ConfigureAwait(false));

            Certificate? taken = await store.TryTakeAsync(CreateContext()).ConfigureAwait(false);

            Assert.That(taken, Is.Null);
        }

        /// <summary>
        /// Two scopes must not see each other's staged key.
        /// </summary>
        [Test]
        public async Task ScopesAreIsolatedAsync()
        {
            SimulatedHardwareCertificateStore token = m_provider!.GetStore(m_storePath!);
            using Certificate generated = token.CreateRsaCertificate("CN=PendingScoped");

            var store = new HardwarePendingCertificateKeyStore(m_provider);

            PendingCertificateKeyContext first = CreateContext();
            PendingCertificateKeyContext second = CreateContext(groupId: 2000u);

            bool saved = await store.SaveAsync(first, generated).ConfigureAwait(false);
            Assert.That(saved, Is.True);

            Certificate? fromOther = await store.TryTakeAsync(second).ConfigureAwait(false);

            Assert.That(
                fromOther,
                Is.Null,
                "a different certificate group must not consume this scope's key");

            using Certificate? fromOwn = await store.TryTakeAsync(first).ConfigureAwait(false);

            Assert.That(fromOwn, Is.Not.Null);
        }

        private static Certificate CreateSoftwareCertificate()
        {
            using RSA key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=SoftwarePending", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            return Certificate.From(
                X509CertificateLoader.LoadPkcs12(
                    selfSigned.Export(X509ContentType.Pfx, string.Empty),
                    string.Empty,
                    X509KeyStorageFlags.Exportable));
        }

        private PendingCertificateKeyContext CreateContext(uint groupId = 1000)
        {
            return new PendingCertificateKeyContext(
                new CertificateStoreIdentifier(
                    m_storePath!, SimulatedHardwareCertificateStore.StoreTypeName, false),
                new NodeId(groupId),
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                null,
                m_telemetry!);
        }

        private SimulatedHardwareCertificateStoreProvider? m_provider;
        private string? m_storePath;
        private ITelemetryContext? m_telemetry;
    }
}
