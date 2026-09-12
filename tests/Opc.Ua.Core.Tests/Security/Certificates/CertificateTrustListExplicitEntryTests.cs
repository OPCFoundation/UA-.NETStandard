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
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Ownership regressions for explicitly configured trust-list entries.
    /// </summary>
    [TestFixture]
    [Category("CertificateTrustList")]
    [NonParallelizable]
    public sealed class CertificateTrustListExplicitEntryTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_first = CreateCertificate("CN=Explicit Trust Entry One");
            m_second = CreateCertificate("CN=Explicit Trust Entry Two");
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_first.Dispose();
            m_second.Dispose();
            (m_telemetry as IDisposable)?.Dispose();
        }

        [Test]
        public async Task ExplicitTrustListResolutionBalancesHandlesAsync()
        {
            var trustList = new CertificateTrustList
            {
                TrustedCertificates =
                [
                    new CertificateIdentifier { RawData = m_first.RawData },
                    new CertificateIdentifier { RawData = m_second.RawData }
                ]
            };
            long createdBefore = Certificate.InstancesCreated;
            long disposedBefore = Certificate.InstancesDisposed;

            for (int i = 0; i < 3; i++)
            {
                using CertificateCollection certificates = await trustList
                    .GetCertificatesAsync(m_telemetry)
                    .ConfigureAwait(false);

                Assert.That(certificates, Has.Count.EqualTo(2));
                Assert.That(certificates[0].RawData, Is.EqualTo(m_first.RawData));
                Assert.That(certificates[1].RawData, Is.EqualTo(m_second.RawData));
                using RSA key = certificates[0].GetRSAPublicKey();
                Assert.That(key.KeySize, Is.EqualTo(2048));
            }

            long created = Certificate.InstancesCreated - createdBefore;
            Assert.That(created, Is.GreaterThanOrEqualTo(6));
            Assert.That(Certificate.InstancesDisposed - disposedBefore, Is.EqualTo(created),
                "Disposing each returned list must also release every temporary explicit-entry handle.");
        }

        [Test]
        public async Task ReturnedExplicitCertificatesHaveIndependentOwnershipAsync()
        {
            var trustList = new CertificateTrustList
            {
                TrustedCertificates = [new CertificateIdentifier { RawData = m_first.RawData }]
            };
            long createdBefore = Certificate.InstancesCreated;
            long disposedBefore = Certificate.InstancesDisposed;

            using (CertificateCollection first = await trustList.GetCertificatesAsync(m_telemetry)
                .ConfigureAwait(false))
            {
                using CertificateCollection second = await trustList.GetCertificatesAsync(m_telemetry)
                    .ConfigureAwait(false);
                using Certificate borrowed = first[0].AddRef();
                first.Dispose();

                Assert.That(second[0].RawData, Is.EqualTo(m_first.RawData));
                second.Dispose();

                Assert.That(borrowed.RawData, Is.EqualTo(m_first.RawData));
                using RSA key = borrowed.GetRSAPublicKey();
                Assert.That(key.KeySize, Is.EqualTo(2048));
            }

            AssertBalanced(createdBefore, disposedBefore);
        }

        [Test]
        public async Task NullExplicitResolutionDoesNotDiscardOtherEntriesAsync()
        {
            var trustList = new CertificateTrustList
            {
                TrustedCertificates =
                [
                    new CertificateIdentifier { RawData = m_first.RawData },
                    new CertificateIdentifier(),
                    new CertificateIdentifier { RawData = m_second.RawData }
                ]
            };
            long createdBefore = Certificate.InstancesCreated;
            long disposedBefore = Certificate.InstancesDisposed;

            using (CertificateCollection certificates = await trustList.GetCertificatesAsync(m_telemetry)
                .ConfigureAwait(false))
            {
                Assert.That(certificates, Has.Count.EqualTo(2));
                Assert.That(certificates[0].RawData, Is.EqualTo(m_first.RawData));
                Assert.That(certificates[1].RawData, Is.EqualTo(m_second.RawData));
            }

            AssertBalanced(createdBefore, disposedBefore);
        }

        [Test]
        public async Task MixedStoreAndExplicitEntriesOwnTheirCertificatesAsync()
        {
            string path = CreateStorePath();
            try
            {
                var trustList = new CertificateTrustList
                {
                    StorePath = path,
                    StoreType = CertificateStoreType.Directory,
                    TrustedCertificates = [new CertificateIdentifier { RawData = m_first.RawData }]
                };
                await AddPublicCertificateAsync(trustList, m_second).ConfigureAwait(false);
                long createdBefore = Certificate.InstancesCreated;
                long disposedBefore = Certificate.InstancesDisposed;

                using (CertificateCollection certificates = await trustList.GetCertificatesAsync(m_telemetry)
                    .ConfigureAwait(false))
                {
                    string[] expected = [m_first.Thumbprint, m_second.Thumbprint];
                    Assert.That(certificates.Select(certificate => certificate.Thumbprint),
                        Is.EquivalentTo(expected));
                }

                AssertBalanced(createdBefore, disposedBefore);
            }
            finally
            {
                Directory.Delete(path, recursive: true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitResolutionFailureDisposesPartialCollectionAsync(bool includeStore)
        {
            string path = CreateStorePath();
            try
            {
                byte[] damagedData = m_second.RawData;
                var damagedIdentifier = new CertificateIdentifier { RawData = damagedData };
                damagedData[0] = 0;
                var trustList = new CertificateTrustList
                {
                    StorePath = includeStore ? path : null,
                    StoreType = CertificateStoreType.Directory,
                    TrustedCertificates =
                    [
                        new CertificateIdentifier { RawData = m_first.RawData },
                        damagedIdentifier
                    ]
                };
                if (includeStore)
                {
                    await AddPublicCertificateAsync(trustList, m_second).ConfigureAwait(false);
                }
                long createdBefore = Certificate.InstancesCreated;
                long disposedBefore = Certificate.InstancesDisposed;

                Assert.ThrowsAsync<CryptographicException>(async () =>
                {
                    using CertificateCollection certificates = await trustList.GetCertificatesAsync(m_telemetry)
                        .ConfigureAwait(false);
                });

                Assert.That(Certificate.InstancesCreated - createdBefore, Is.GreaterThan(0));
                AssertBalanced(createdBefore, disposedBefore);
            }
            finally
            {
                Directory.Delete(path, recursive: true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancelledExplicitResolutionPropagatesWithoutLeakingAsync(bool includeStore)
        {
            string path = CreateStorePath();
            try
            {
                var trustList = new CertificateTrustList
                {
                    StorePath = includeStore ? path : null,
                    StoreType = CertificateStoreType.Directory,
                    TrustedCertificates = [new CertificateIdentifier { RawData = m_first.RawData }]
                };
                if (includeStore)
                {
                    await AddPublicCertificateAsync(trustList, m_second).ConfigureAwait(false);
                }
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                long createdBefore = Certificate.InstancesCreated;
                long disposedBefore = Certificate.InstancesDisposed;

                OperationCanceledException exception = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                {
                    using CertificateCollection certificates = await trustList
                        .GetCertificatesAsync(m_telemetry, cancellation.Token)
                        .ConfigureAwait(false);
                });

                Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
                AssertBalanced(createdBefore, disposedBefore);
            }
            finally
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private async Task AddPublicCertificateAsync(CertificateTrustList trustList, Certificate certificate)
        {
            using var publicCertificate = Certificate.FromRawData(certificate.RawData);
            using ICertificateStore store = trustList.OpenStore(m_telemetry);
            await store.AddAsync(publicCertificate).ConfigureAwait(false);
        }

        private static void AssertBalanced(long createdBefore, long disposedBefore)
        {
            Assert.That(Certificate.InstancesDisposed - disposedBefore,
                Is.EqualTo(Certificate.InstancesCreated - createdBefore));
        }

        private static string CreateStorePath()
        {
            string path = Path.Combine(Path.GetTempPath(), "opcua-explicit-entry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static Certificate CreateCertificate(string subject)
        {
            return CertificateBuilder
                .Create(subject)
                .SetNotBefore(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private ITelemetryContext m_telemetry;
        private Certificate m_first;
        private Certificate m_second;
    }
}
