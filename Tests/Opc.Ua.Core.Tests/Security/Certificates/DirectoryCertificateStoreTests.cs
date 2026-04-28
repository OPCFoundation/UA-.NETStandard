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
using System.IO;
using System.Threading.Tasks;
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
            Assert.That(() => store.Close(), Throws.Nothing);
        }

        [Test]
        public async Task EnumerateAsyncOnEmptyStoreReturnsEmptyCollectionAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using CertificateCollection certs = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(certs, Is.Not.Null);
            Assert.That(certs.Count, Is.Zero);
        }

        [Test]
        public async Task FindByThumbprintAsyncWithNonExistentThumbprintReturnsEmptyAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using CertificateCollection certs = await store.FindByThumbprintAsync("0000000000000000000000000000000000000000")
                .ConfigureAwait(false);
            Assert.That(certs, Is.Not.Null);
            Assert.That(certs.Count, Is.Zero);
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

            using Certificate publicKey = CertificateFactory.Create(cert.RawData);
            await store.AddAsync(publicKey).ConfigureAwait(false);

            using CertificateCollection certs = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(certs.Count, Is.EqualTo(1));
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

            using Certificate publicKey = CertificateFactory.Create(cert.RawData);
            await store.AddAsync(publicKey).ConfigureAwait(false);

            using CertificateCollection found = await store.FindByThumbprintAsync(publicKey.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(found.Count, Is.EqualTo(1));
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

            using Certificate publicKey = CertificateFactory.Create(cert.RawData);
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

            using Certificate publicKey = CertificateFactory.Create(cert.RawData);
            await store.AddAsync(publicKey).ConfigureAwait(false);

            bool deleted = await store.DeleteAsync(publicKey.Thumbprint).ConfigureAwait(false);
            Assert.That(deleted, Is.True);

            using CertificateCollection remaining = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(remaining.Count, Is.Zero);
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
            Assert.That(crls.Count, Is.Zero);
        }

        [Test]
        public async Task EnumerateAsyncOnEmptyStoreWithNoSubDirsReturnsEmptyAsync()
        {
            using var store = new DirectoryCertificateStore(true, m_telemetry);
            store.Open(m_tempDir);

            using CertificateCollection certs = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(certs, Is.Not.Null);
            Assert.That(certs.Count, Is.Zero);
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
                certs.Add(CertificateFactory.Create(cert.RawData));
            }

            await store.AddRejectedAsync(certs, 5).ConfigureAwait(false);

            using CertificateCollection found = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(found.Count, Is.EqualTo(3));
        }

        [Test]
        public void DisposeDoesNotThrow()
        {
            var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);
            Assert.That(() => store.Dispose(), Throws.Nothing);
        }

        [Test]
        public void DisposeWithoutOpenDoesNotThrow()
        {
            var store = new DirectoryCertificateStore(m_telemetry);
            Assert.That(() => store.Dispose(), Throws.Nothing);
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

                using Certificate publicKey = CertificateFactory.Create(cert.RawData);
                await store.AddAsync(publicKey).ConfigureAwait(false);

                store.Open(tempDir2);
                using CertificateCollection certs = await store.EnumerateAsync().ConfigureAwait(false);
                Assert.That(certs.Count, Is.Zero);
            }
            finally
            {
                if (Directory.Exists(tempDir2))
                {
                    Directory.Delete(tempDir2, true);
                }
            }
        }
    }
}
