// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for <see cref="CertificateManager.CertificateProvider"/>
    /// (the cache-first <see cref="ICertificateProvider"/> wired into
    /// <see cref="CertificateManager"/>).
    /// </summary>
    [TestFixture]
    [Category("CertificateProvider")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CertificateProviderTests
    {
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            (m_telemetry as System.IDisposable)?.Dispose();
        }

        [Test]
        public void TryGetReturnsNullOnMiss()
        {
            using var manager = new CertificateManager(m_telemetry);

            Certificate cert = manager.CertificateProvider
                .TryGetPrivateKeyCertificate("0000000000000000000000000000000000000000")!;

            Assert.That(cert, Is.Null);
        }

        [Test]
        public async Task GetAsyncReturnsNullForUnknownIdentifierAsync()
        {
            using var manager = new CertificateManager(m_telemetry);
            string storePath = Path.Combine(
                Path.GetTempPath(),
                "opcua-certprov-test-" + System.Guid.NewGuid().ToString("N")[..8]);
            try
            {
                Directory.CreateDirectory(storePath);
                var id = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = storePath,
                    Thumbprint = "0000000000000000000000000000000000000000"
                };

                Certificate cert = (await manager.CertificateProvider
                    .GetPrivateKeyCertificateAsync(id)
                    .ConfigureAwait(false))!;

                Assert.That(cert, Is.Null,
                    "Empty store + unknown thumbprint must yield null.");
            }
            finally
            {
                if (Directory.Exists(storePath))
                {
                    Directory.Delete(storePath, true);
                }
            }
        }

        [Test]
        public async Task GetAsyncResolvesAndCachesPrivateKeyCertAsync()
        {
            using var manager = new CertificateManager(m_telemetry);

            // Build a cert with private key and persist as PFX in a
            // directory store so the resolver can load it.
            string storePath = Path.Combine(
                Path.GetTempPath(),
                "opcua-certprov-cache-" + System.Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(storePath);
            try
            {
                using Certificate created = CertificateBuilder
                    .Create("CN=ProviderCacheTest, O=OPC Foundation")
                    .SetRSAKeySize(2048)
                    .CreateForRSA();

                await created.AddToStoreAsync(
                    CertificateStoreType.Directory,
                    storePath,
                    password: null,
                    m_telemetry).ConfigureAwait(false);

                var id = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = storePath,
                    Thumbprint = created.Thumbprint
                };

                // Cold path: first call hits the store.
                using Certificate firstHit = (await manager.CertificateProvider
                    .GetPrivateKeyCertificateAsync(id)
                    .ConfigureAwait(false))!;
                Assert.That(firstHit, Is.Not.Null);
                Assert.That(firstHit.HasPrivateKey, Is.True);

#if NET6_0_OR_GREATER
                // Warm path: TryGet must now succeed synchronously.
                // Pre-.NET 6 the underlying CertificateCache is a
                // no-op passthrough, so this assertion is only valid
                // on net6.0+.
                using Certificate? cached = manager.CertificateProvider
                    .TryGetPrivateKeyCertificate(created.Thumbprint);
                Assert.That(cached, Is.Not.Null,
                    "After GetAsync, TryGet must return the cached private-key cert.");
                Assert.That(cached.HasPrivateKey, Is.True);
                Assert.That(cached.Thumbprint, Is.EqualTo(created.Thumbprint));
#endif
            }
            finally
            {
                if (Directory.Exists(storePath))
                {
                    Directory.Delete(storePath, true);
                }
            }
        }
    }
}
