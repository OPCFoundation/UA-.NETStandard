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
    /// Tests for the store-instance cache on
    /// <see cref="CertificateStoreIdentifier"/> and its explicit release via
    /// <see cref="CertificateStoreIdentifier.DisposeCachedStore"/> — the
    /// shutdown hook long-lived identifier owners use to free the
    /// parsed-certificate cache that
    /// <see cref="ICertificateStore.Close"/> deliberately retains.
    /// </summary>
    [TestFixture]
    [Category("CertificateStore")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.All)]
    public sealed class CertificateStoreIdentifierCachedStoreTests
    {
        [Test]
        public void OpenStoreReturnsTheCachedInstance()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string dir = NewTempDir();
            try
            {
                var identifier = new CertificateStoreIdentifier(dir);
                ICertificateStore first = identifier.OpenStore(telemetry);
                ICertificateStore second = identifier.OpenStore(telemetry);
                try
                {
                    Assert.That(second, Is.SameAs(first));
                }
                finally
                {
                    identifier.DisposeCachedStore();
                }
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public async Task DisposeCachedStoreReleasesAndRecreatesTheStoreAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string dir = NewTempDir();
            try
            {
                using Certificate certificate = CertificateBuilder
                    .Create("CN=Cached Store Test")
                    .SetRSAKeySize(2048)
                    .CreateForRSA();

                var identifier = new CertificateStoreIdentifier(dir);
                ICertificateStore first = identifier.OpenStore(telemetry);
                await first.AddAsync(certificate).ConfigureAwait(false);
                using (CertificateCollection certs = await first.EnumerateAsync().ConfigureAwait(false))
                {
                    Assert.That(certs, Has.Count.EqualTo(1));
                }

                // Close() deliberately retains the parsed certificates for
                // reuse; only DisposeCachedStore releases them.
                first.Close();
                identifier.DisposeCachedStore();

                // A fresh, functional store instance is created on demand.
                ICertificateStore second = identifier.OpenStore(telemetry);
                try
                {
                    Assert.That(second, Is.Not.SameAs(first));
                    using CertificateCollection reloaded = await second.EnumerateAsync()
                        .ConfigureAwait(false);
                    Assert.That(reloaded, Has.Count.EqualTo(1));
                    Assert.That(reloaded[0].Thumbprint, Is.EqualTo(certificate.Thumbprint));
                }
                finally
                {
                    identifier.DisposeCachedStore();
                }
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public void DisposeCachedStoreWithoutOpenIsANoOp()
        {
            var identifier = new CertificateStoreIdentifier(NewTempDir());
            try
            {
                Assert.DoesNotThrow(identifier.DisposeCachedStore);
                Assert.DoesNotThrow(identifier.DisposeCachedStore);
            }
            finally
            {
                Directory.Delete(identifier.StorePath!, recursive: true);
            }
        }

        [Test]
        public async Task SecurityConfigurationDisposeCachedStoresReleasesTrustListStoresAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string trustedDir = NewTempDir();
            string issuerDir = NewTempDir();
            try
            {
                var configuration = new SecurityConfiguration
                {
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = trustedDir
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = issuerDir
                    }
                };

                // Populate the trusted store and pull its content through the
                // configuration identifier so the cached store retains the
                // parsed certificate across Close().
                using Certificate certificate = CertificateBuilder
                    .Create("CN=SecurityConfiguration Cached Store Test")
                    .SetRSAKeySize(2048)
                    .CreateForRSA();
                ICertificateStore cached =
                    configuration.TrustedPeerCertificates.OpenStore(telemetry);
                await cached.AddAsync(certificate).ConfigureAwait(false);
                using (CertificateCollection certs =
                    await cached.EnumerateAsync().ConfigureAwait(false))
                {
                    Assert.That(certs, Has.Count.EqualTo(1));
                }
                cached.Close();

                configuration.DisposeCachedStores();

                ICertificateStore recreated =
                    configuration.TrustedPeerCertificates.OpenStore(telemetry);
                try
                {
                    Assert.That(recreated, Is.Not.SameAs(cached));
                }
                finally
                {
                    configuration.DisposeCachedStores();
                }
            }
            finally
            {
                Directory.Delete(trustedDir, recursive: true);
                Directory.Delete(issuerDir, recursive: true);
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
