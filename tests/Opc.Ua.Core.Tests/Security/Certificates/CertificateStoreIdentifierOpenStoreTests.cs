/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the ownership contract of
    /// <see cref="CertificateStoreIdentifier.OpenStore(ITelemetryContext)"/>:
    /// the identifier is only a description of a store — the analogue of a
    /// <see cref="CertificateIdentifier"/> resolving to a
    /// <see cref="Certificate"/> — so every call creates a new store
    /// instance that the caller owns and must dispose.
    /// </summary>
    [TestFixture]
    [Category("CertificateStore")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.All)]
    public sealed class CertificateStoreIdentifierOpenStoreTests
    {
        [Test]
        public async Task OpenStoreReturnsACallerOwnedInstancePerCallAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string dir = NewTempDir();
            try
            {
                using Certificate certificate = CertificateBuilder
                    .Create("CN=OpenStore Ownership Test")
                    .SetRSAKeySize(2048)
                    .CreateForRSA();

                var identifier = new CertificateStoreIdentifier(dir);
                using ICertificateStore first = identifier.OpenStore(telemetry);
                using ICertificateStore second = identifier.OpenStore(telemetry);

                // every call creates a new instance the caller owns
                Assert.That(second, Is.Not.SameAs(first));

                // both instances operate on the same backing store
                await first.AddAsync(certificate).ConfigureAwait(false);
                using (CertificateCollection certs = await second.EnumerateAsync()
                    .ConfigureAwait(false))
                {
                    Assert.That(certs, Has.Count.EqualTo(1));
                }

                // disposing one instance must not affect the other
                first.Dispose();
                using (CertificateCollection certs = await second.EnumerateAsync()
                    .ConfigureAwait(false))
                {
                    Assert.That(certs, Has.Count.EqualTo(1));
                    Assert.That(certs[0].Thumbprint, Is.EqualTo(certificate.Thumbprint));
                }
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public void OpenStoreWithoutStorePathReturnsNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var identifier = new CertificateStoreIdentifier(
                string.Empty,
                CertificateStoreType.Directory);
            Assert.That(identifier.OpenStore(telemetry), Is.Null);
        }

        [Test]
        public void OpenStorePropagatesTheOpenFailure()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // an X509 store path without a store name fails in Open; the
            // just-created store is disposed before the exception surfaces
            var identifier = new CertificateStoreIdentifier(
                "not-a-store-path",
                CertificateStoreType.X509Store);
            Assert.Throws<ServiceResultException>(() => identifier.OpenStore(telemetry));
        }

        [Test]
        public async Task TrustListGetCertificatesReturnsPersistedCertificatesAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string dir = NewTempDir();
            try
            {
                using Certificate certificate = CertificateBuilder
                    .Create("CN=TrustList GetCertificates Test")
                    .SetRSAKeySize(2048)
                    .CreateForRSA();

                var trustList = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = dir
                };
                using (ICertificateStore store = trustList.OpenStore(telemetry))
                {
                    await store.AddAsync(certificate).ConfigureAwait(false);
                }

                using CertificateCollection certs = await trustList
                    .GetCertificatesAsync(telemetry)
                    .ConfigureAwait(false);
                Assert.That(certs, Has.Count.EqualTo(1));
                Assert.That(certs[0].Thumbprint, Is.EqualTo(certificate.Thumbprint));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        private static string NewTempDir()
        {
            string dir = Path.Combine(
                Path.GetTempPath(),
                "opcua-csid-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
