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
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

#pragma warning disable CS0618 // Tests exercise obsolete methods intentionally

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the <see cref="TrustListTransaction"/> accessed through
    /// <see cref="CertificateManager.BeginUpdateAsync"/>.
    /// </summary>
    [TestFixture]
    [Category("TrustListTransaction")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class TrustListTransactionTests
    {
        private ITelemetryContext _telemetry;
        private readonly List<string> _tempDirs = [];

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _telemetry = NUnitTelemetryContext.Create();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            (_telemetry as IDisposable)?.Dispose();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string dir in _tempDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, true);
                    }
                }
                catch (IOException)
                {
                    // best effort cleanup
                }
            }

            _tempDirs.Clear();
        }

        [Test]
        public async Task CommitAsyncAddsCertificateToStore()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate cert = CertificateBuilder
                .Create("CN=CommitAdd")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            ITrustListTransaction transaction = await manager
                .BeginUpdateAsync(TrustListIdentifier.Peers).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await transaction.AddTrustedCertificateAsync(cert).ConfigureAwait(false);
                await transaction.CommitAsync().ConfigureAwait(false);
            }

            using ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers);
            using CertificateCollection certs = await store.EnumerateAsync().ConfigureAwait(false);

            Assert.That(certs, Has.Count.EqualTo(1));
            Assert.That(certs[0].Thumbprint, Is.EqualTo(cert.Thumbprint));
        }

        [Test]
        public async Task CommitAsyncRemovesCertificateFromStore()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate cert = CertificateBuilder
                .Create("CN=CommitRemove")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // Pre-populate the store with the certificate.
            using (ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await store.AddAsync(cert).ConfigureAwait(false);
            }

            ITrustListTransaction transaction = await manager
                .BeginUpdateAsync(TrustListIdentifier.Peers).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await transaction.RemoveTrustedCertificateAsync(cert.Thumbprint)
                    .ConfigureAwait(false);
                await transaction.CommitAsync().ConfigureAwait(false);
            }

            using ICertificateStore verifyStore = manager.OpenTrustedStore(TrustListIdentifier.Peers);
            using CertificateCollection remaining = await verifyStore.EnumerateAsync()
                .ConfigureAwait(false);

            Assert.That(remaining, Is.Empty);
        }

        [Test]
        public async Task DisposeWithoutCommitDoesNotModifyStore()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate originalCert = CertificateBuilder
                .Create("CN=Original")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // Pre-populate the store.
            using (ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await store.AddAsync(originalCert).ConfigureAwait(false);
            }

            using Certificate extraCert = CertificateBuilder
                .Create("CN=Extra")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // Begin transaction, add a cert, but do NOT commit.
            ITrustListTransaction transaction = await manager
                .BeginUpdateAsync(TrustListIdentifier.Peers).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await transaction.AddTrustedCertificateAsync(extraCert).ConfigureAwait(false);
                // intentionally no CommitAsync
            }

            using ICertificateStore verifyStore = manager.OpenTrustedStore(TrustListIdentifier.Peers);
            using CertificateCollection certs = await verifyStore.EnumerateAsync()
                .ConfigureAwait(false);

            Assert.That(certs, Has.Count.EqualTo(1));
            Assert.That(certs[0].Thumbprint, Is.EqualTo(originalCert.Thumbprint));
        }

        [Test]
        public async Task CommitAsyncThrowsWhenDisposed()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            ITrustListTransaction transaction = await manager
                .BeginUpdateAsync(TrustListIdentifier.Peers).ConfigureAwait(false);
            await transaction.DisposeAsync().ConfigureAwait(false);

            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await transaction.CommitAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task AddCrlAndCommit()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            // Create a self-signed CA certificate for CRL signing.
            using Certificate caCert = CertificateBuilder
                .Create("CN=TestCA")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // The issuer certificate must be in the store before adding a CRL.
            using (ICertificateStore setupStore = manager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await setupStore.AddAsync(caCert).ConfigureAwait(false);
            }

            // Build a CRL signed by the CA certificate.
            X509CRL crl = CertificateFactory.RevokeCertificate(caCert, null, null);

            ITrustListTransaction transaction = await manager
                .BeginUpdateAsync(TrustListIdentifier.Peers).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await transaction.AddCrlAsync(crl).ConfigureAwait(false);
                await transaction.CommitAsync().ConfigureAwait(false);
            }

            using ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers);
            if (store.SupportsCRLs)
            {
                X509CRLCollection crls = await store.EnumerateCRLsAsync().ConfigureAwait(false);
                Assert.That(crls, Has.Count.EqualTo(1));
            }
        }

        #region Helpers

        private string CreateTempDir()
        {
            string dir = Path.Combine(
                Path.GetTempPath(),
                "opcua-tlt-test-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            _tempDirs.Add(dir);
            return dir;
        }

        #endregion
    }
}
