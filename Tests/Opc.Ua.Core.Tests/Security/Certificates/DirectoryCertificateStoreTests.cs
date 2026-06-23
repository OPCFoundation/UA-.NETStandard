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
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    [TestFixture]
    [Category("DirectoryCertificateStore")]
    [NonParallelizable]
    public class DirectoryCertificateStoreTests
    {
        private ITelemetryContext m_telemetry;
        private string m_tempDir;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_tempDir = Path.Combine(Path.GetTempPath(), "OpcUaDirStoreTest_" + Guid.NewGuid().ToString("N"));
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            (m_telemetry as IDisposable)?.Dispose();

            if (Directory.Exists(m_tempDir))
            {
                Directory.Delete(m_tempDir, true);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_tempDir))
            {
                Directory.Delete(m_tempDir, true);
            }
        }

        [Test]
        public void ConstructorCreatesInstance()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            Assert.That(store, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithNoSubDirsCreatesInstance()
        {
            using var store = new DirectoryCertificateStore(true, m_telemetry);
            Assert.That(store, Is.Not.Null);
        }

        [Test]
        public void StoreTypeReturnsDirectory()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            Assert.That(store.StoreType, Is.EqualTo(CertificateStoreType.Directory));
        }

        [Test]
        public void SupportsLoadPrivateKeyReturnsTrue()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            Assert.That(store.SupportsLoadPrivateKey, Is.True);
        }

        [Test]
        public void SupportsCRLsReturnsTrue()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            Assert.That(store.SupportsCRLs, Is.True);
        }

        [Test]
        public void OpenSetsStorePathAndDirectory()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            Assert.That(store.StorePath, Is.EqualTo(m_tempDir));
            Assert.That(store.Directory, Is.Not.Null);
            Assert.That(store.NoPrivateKeys, Is.False);
        }

        [Test]
        public void OpenWithNoPrivateKeysSetsFlag()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir, true);

            Assert.That(store.NoPrivateKeys, Is.True);
        }

        [Test]
        public void OpenTwiceWithSamePathDoesNotReset()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);
            store.Open(m_tempDir);

            Assert.That(store.StorePath, Is.EqualTo(m_tempDir));
        }

        [Test]
        public void CloseDoesNotThrow()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);
            Assert.That(store.Close, Throws.Nothing);
        }

        [Test]
        public async Task EnumerateAsyncOnEmptyStoreReturnsEmptyCollectionAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using CertificateCollection certs = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(certs, Is.Not.Null);
            Assert.That(certs, Has.Count.Zero);
        }

        [Test]
        public async Task FindByThumbprintAsyncWithNonExistentThumbprintReturnsEmptyAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using CertificateCollection certs = await store.FindByThumbprintAsync("0000000000000000000000000000000000000000")
                .ConfigureAwait(false);
            Assert.That(certs, Is.Not.Null);
            Assert.That(certs, Has.Count.Zero);
        }

        [Test]
        public void GetPublicKeyFilePathWithNonExistentThumbprintReturnsNull()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            string path = store.GetPublicKeyFilePath("0000000000000000000000000000000000000000");
            Assert.That(path, Is.Null);
        }

        [Test]
        public void GetPrivateKeyFilePathWithNonExistentThumbprintReturnsNull()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            string path = store.GetPrivateKeyFilePath("0000000000000000000000000000000000000000");
            Assert.That(path, Is.Null);
        }

        [Test]
        public async Task DeleteAsyncWithNonExistentThumbprintReturnsFalseAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            bool deleted = await store.DeleteAsync("0000000000000000000000000000000000000000")
                .ConfigureAwait(false);
            Assert.That(deleted, Is.False);
        }

        [Test]
        public async Task AddAsyncAndEnumerateAsyncRoundTripAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using Certificate cert = CertificateBuilder
                .Create("CN=DirStoreTestCert")
                .SetLifeTime(365)
                .CreateForRSA();

            using var publicKey = Certificate.FromRawData(cert.RawData);
            await store.AddAsync(publicKey).ConfigureAwait(false);

            using CertificateCollection certs = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(certs, Has.Count.EqualTo(1));
            Assert.That(certs[0].Thumbprint, Is.EqualTo(publicKey.Thumbprint));
        }

        [Test]
        public async Task AddAsyncAndFindByThumbprintAsyncReturnsMatchAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using Certificate cert = CertificateBuilder
                .Create("CN=DirStoreFindTest")
                .SetLifeTime(365)
                .CreateForRSA();

            using var publicKey = Certificate.FromRawData(cert.RawData);
            await store.AddAsync(publicKey).ConfigureAwait(false);

            using CertificateCollection found = await store.FindByThumbprintAsync(publicKey.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Thumbprint, Is.EqualTo(publicKey.Thumbprint));
        }

        [Test]
        public async Task AddAsyncAndGetPublicKeyFilePathReturnsPathAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using Certificate cert = CertificateBuilder
                .Create("CN=DirStorePathTest")
                .SetLifeTime(365)
                .CreateForRSA();

            using var publicKey = Certificate.FromRawData(cert.RawData);
            await store.AddAsync(publicKey).ConfigureAwait(false);

            string path = store.GetPublicKeyFilePath(publicKey.Thumbprint);
            Assert.That(path, Is.Not.Null);
            Assert.That(File.Exists(path), Is.True);
        }

        [Test]
        public async Task AddAsyncAndDeleteAsyncRemovesCertificateAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using Certificate cert = CertificateBuilder
                .Create("CN=DirStoreDeleteTest")
                .SetLifeTime(365)
                .CreateForRSA();

            using var publicKey = Certificate.FromRawData(cert.RawData);
            await store.AddAsync(publicKey).ConfigureAwait(false);

            bool deleted = await store.DeleteAsync(publicKey.Thumbprint).ConfigureAwait(false);
            Assert.That(deleted, Is.True);

            using CertificateCollection remaining = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(remaining, Has.Count.Zero);
        }

        [Test]
        public async Task AddAsyncWithPrivateKeyAndDeleteAsyncRemovesBothAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using Certificate cert = CertificateBuilder
                .Create("CN=DirStorePfxTest")
                .SetLifeTime(365)
                .CreateForRSA();

            await store.AddAsync(cert, "password".ToCharArray()).ConfigureAwait(false);

            string privateKeyPath = store.GetPrivateKeyFilePath(cert.Thumbprint);
            Assert.That(privateKeyPath, Is.Not.Null);
            Assert.That(File.Exists(privateKeyPath), Is.True);

            bool deleted = await store.DeleteAsync(cert.Thumbprint).ConfigureAwait(false);
            Assert.That(deleted, Is.True);
        }

        [Test]
        public async Task EnumerateCRLsAsyncOnEmptyStoreReturnsEmptyAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            X509CRLCollection crls = await store.EnumerateCRLsAsync().ConfigureAwait(false);
            Assert.That(crls, Is.Not.Null);
            Assert.That(crls, Has.Count.Zero);
        }

        [Test]
        public async Task EnumerateAsyncOnEmptyStoreWithNoSubDirsReturnsEmptyAsync()
        {
            using var store = new DirectoryCertificateStore(true, m_telemetry);
            store.Open(m_tempDir);

            using CertificateCollection certs = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(certs, Is.Not.Null);
            Assert.That(certs, Has.Count.Zero);
        }

        [Test]
        public async Task AddRejectedAsyncWithMaxCertificatesLimitsStoreSizeAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using var certs = new CertificateCollection();
            for (int i = 0; i < 3; i++)
            {
                using Certificate cert = CertificateBuilder
                    .Create($"CN=RejectedCert{i}")
                    .SetLifeTime(365)
                    .CreateForRSA();
                using var publicKey = Certificate.FromRawData(cert.RawData);
                certs.Add(publicKey);
            }

            await store.AddRejectedAsync(certs, 5).ConfigureAwait(false);

            // use a separate store to verify; the original store's entries
            // hold AddRef'd references that share objects with certs above
            using var verifyStore = new DirectoryCertificateStore(m_telemetry);
            verifyStore.Open(m_tempDir);
            using CertificateCollection found = await verifyStore.EnumerateAsync().ConfigureAwait(false);
            Assert.That(found, Has.Count.EqualTo(3));
        }

        [Test]
        public void DisposeDoesNotThrow()
        {
            var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);
            Assert.That(store.Dispose, Throws.Nothing);
        }

        [Test]
        public void DisposeWithoutOpenDoesNotThrow()
        {
            var store = new DirectoryCertificateStore(m_telemetry);
            Assert.That(store.Dispose, Throws.Nothing);
        }

        [Test]
        public async Task OpenWithDifferentPathReloadsStoreAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            string tempDir2 = m_tempDir + "_alt";

            try
            {
                store.Open(m_tempDir);

                using Certificate cert = CertificateBuilder
                    .Create("CN=ReloadTest")
                    .SetLifeTime(365)
                    .CreateForRSA();

                using var publicKey = Certificate.FromRawData(cert.RawData);
                await store.AddAsync(publicKey).ConfigureAwait(false);

                store.Open(tempDir2);
                using CertificateCollection certs = await store.EnumerateAsync().ConfigureAwait(false);
                Assert.That(certs, Has.Count.Zero);
            }
            finally
            {
                if (Directory.Exists(tempDir2))
                {
                    Directory.Delete(tempDir2, true);
                }
            }
        }

        [Test]
        public async Task IsRevokedAsyncReflectsCrlAddedAfterCacheWarmupAsync()
        {
            // Part C regression: IsRevokedAsync caches parsed CRLs. A CRL added
            // after the cache is warmed must be observed on the next check so a
            // newly revoked certificate is not accepted from a stale cache.
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using Certificate issuer = CertificateBuilder
                .Create("CN=CrlCacheIssuer")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=CrlCacheLeaf")
                .SetIssuer(issuer)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // AddCRLAsync requires the issuer cert to already be in the store.
            await store.AddAsync(issuer).ConfigureAwait(false);

            // Warm the CRL cache while no CRL is present.
            StatusCode before = await store.IsRevokedAsync(issuer, leaf).ConfigureAwait(false);
            Assert.That(before.Code, Is.EqualTo(StatusCodes.BadCertificateRevocationUnknown));

            // Add a CRL that revokes the leaf.
            var crl = new X509CRL(CrlBuilder
                .Create(issuer.SubjectName)
                .AddRevokedCertificate(leaf)
                .CreateForRSA(issuer));
            await store.AddCRLAsync(crl).ConfigureAwait(false);

            // The cache must be invalidated so the new CRL is observed.
            StatusCode after = await store.IsRevokedAsync(issuer, leaf).ConfigureAwait(false);
            Assert.That(after.Code, Is.EqualTo(StatusCodes.BadCertificateRevoked),
                "A CRL added after the cache was warmed must revoke the certificate.");
        }

        [Test]
        public async Task IsRevokedAsyncReflectsCrlRemovedAfterCacheWarmupAsync()
        {
            // Part C regression: removing a CRL must also invalidate the cache.
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using Certificate issuer = CertificateBuilder
                .Create("CN=CrlRemoveIssuer")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=CrlRemoveLeaf")
                .SetIssuer(issuer)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            await store.AddAsync(issuer).ConfigureAwait(false);

            var crl = new X509CRL(CrlBuilder
                .Create(issuer.SubjectName)
                .AddRevokedCertificate(leaf)
                .CreateForRSA(issuer));
            await store.AddCRLAsync(crl).ConfigureAwait(false);

            // Warm the cache with the revoking CRL present.
            StatusCode before = await store.IsRevokedAsync(issuer, leaf).ConfigureAwait(false);
            Assert.That(before.Code, Is.EqualTo(StatusCodes.BadCertificateRevoked));

            // Remove the CRL; the next check must no longer see it.
            bool deleted = await store.DeleteCRLAsync(crl).ConfigureAwait(false);
            Assert.That(deleted, Is.True);

            StatusCode after = await store.IsRevokedAsync(issuer, leaf).ConfigureAwait(false);
            Assert.That(after.Code, Is.EqualTo(StatusCodes.BadCertificateRevocationUnknown),
                "A CRL removed after the cache was warmed must no longer be observed.");
        }

        [Test]
        public async Task IsRevokedAsyncReflectsCrlAddedAndRemovedViaFileSystemAsync()
        {
            // Part C: the CRL cache must also detect CRLs added / removed
            // directly on the file system (out of band — NOT via
            // AddCRLAsync / DeleteCRLAsync), via the per-file freshness
            // signature rather than the explicit cache invalidation.
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using Certificate issuer = CertificateBuilder
                .Create("CN=CrlFsIssuer")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=CrlFsLeaf")
                .SetIssuer(issuer)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            await store.AddAsync(issuer).ConfigureAwait(false);

            // Warm the cache with no CRL present.
            StatusCode before = await store.IsRevokedAsync(issuer, leaf).ConfigureAwait(false);
            Assert.That(before.Code, Is.EqualTo(StatusCodes.BadCertificateRevocationUnknown));

            // Write a revoking CRL straight onto the file system, bypassing the
            // store's AddCRLAsync cache invalidation.
            var crl = new X509CRL(CrlBuilder
                .Create(issuer.SubjectName)
                .AddRevokedCertificate(leaf)
                .CreateForRSA(issuer));
            string crlDir = Path.Combine(m_tempDir, "crl");
            Directory.CreateDirectory(crlDir);
            string crlFile = Path.Combine(crlDir, "external.crl");
            File.WriteAllBytes(crlFile, crl.RawData);

            StatusCode afterAdd = await store.IsRevokedAsync(issuer, leaf).ConfigureAwait(false);
            Assert.That(afterAdd.Code, Is.EqualTo(StatusCodes.BadCertificateRevoked),
                "A CRL written directly to the file system must be observed.");

            // Remove the CRL file directly.
            File.Delete(crlFile);

            StatusCode afterRemove = await store.IsRevokedAsync(issuer, leaf).ConfigureAwait(false);
            Assert.That(afterRemove.Code, Is.EqualTo(StatusCodes.BadCertificateRevocationUnknown),
                "A CRL removed from the file system must no longer be observed.");
        }

        [Test]
        public async Task IsRevokedAsyncCrlCacheIsBoundedByMaxAgeAsync()
        {
            // Security (F2): even when the CRL file's (name, length, last-write-
            // time) signature is unchanged, the cache must re-read from disk
            // once the snapshot exceeds its max age, so an out-of-band, same-
            // signature in-place CRL replacement cannot keep serving a stale
            // revocation result indefinitely.
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            using var store = new DirectoryCertificateStore(noSubDirs: false, m_telemetry, time);
            store.Open(m_tempDir);

            using Certificate issuer = CertificateBuilder
                .Create("CN=CrlTtlIssuer")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=CrlTtlLeaf")
                .SetIssuer(issuer)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            await store.AddAsync(issuer).ConfigureAwait(false);

            // Write a CRL that revokes the leaf, directly on disk.
            var crl = new X509CRL(CrlBuilder
                .Create(issuer.SubjectName)
                .AddRevokedCertificate(leaf)
                .CreateForRSA(issuer));
            string crlDir = Path.Combine(m_tempDir, "crl");
            Directory.CreateDirectory(crlDir);
            string crlFile = Path.Combine(crlDir, "ttl.crl");
            File.WriteAllBytes(crlFile, crl.RawData);
            var fileInfo = new FileInfo(crlFile);
            long length = fileInfo.Length;
            DateTime writeTimeUtc = fileInfo.LastWriteTimeUtc;

            // Warm the cache: the leaf is revoked.
            StatusCode warm = await store.IsRevokedAsync(issuer, leaf).ConfigureAwait(false);
            Assert.That(warm.Code, Is.EqualTo(StatusCodes.BadCertificateRevoked));

            // Replace the CRL content in place with same-length bytes and reset
            // the last-write-time so the (name, length, mtime) signature is
            // unchanged. The new content is not a valid CRL.
            var sameLengthGarbage = new byte[length];
            Array.Fill(sameLengthGarbage, (byte)0xEE);
            File.WriteAllBytes(crlFile, sameLengthGarbage);
            File.SetLastWriteTimeUtc(crlFile, writeTimeUtc);

            // Within the max age the unchanged signature keeps serving the
            // cached (stale) CRL.
            StatusCode stale = await store.IsRevokedAsync(issuer, leaf).ConfigureAwait(false);
            Assert.That(stale.Code, Is.EqualTo(StatusCodes.BadCertificateRevoked),
                "within the max age, the unchanged signature serves the cached CRL");

            // After the max age the cache re-reads: the now-invalid CRL no
            // longer revokes the leaf.
            time.Advance(TimeSpan.FromSeconds(31));
            StatusCode refreshed = await store.IsRevokedAsync(issuer, leaf).ConfigureAwait(false);
            Assert.That(refreshed.Code, Is.EqualTo(StatusCodes.BadCertificateRevocationUnknown),
                "after the max age the cache must re-read the CRL from disk");
        }
    }
}
