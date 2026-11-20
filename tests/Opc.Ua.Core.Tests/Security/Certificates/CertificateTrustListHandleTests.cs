// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Certificate handle ownership in
    /// <see cref="CertificateTrustList.GetCertificatesAsync(ITelemetryContext, System.Threading.CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// The resolver hands back an owning handle and the collection takes a
    /// reference of its own, so the resolved handle has to be released - it was
    /// not, which cost one handle per configured trusted certificate on every
    /// read of the list. And a list whose entries cannot all be read left the
    /// collection, along with everything already read from the store into it,
    /// with nothing to release it.
    /// </remarks>
    [TestFixture]
    [Category("CertificateStore")]
    [NonParallelizable]
    public sealed class CertificateTrustListHandleTests
    {
        [Test]
        public async Task GetCertificatesAsyncReleasesTheResolvedHandlesAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using Certificate certificate = CertificateBuilder
                .Create("CN=TrustListHandle")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            var trustList = new CertificateTrustList
            {
                TrustedCertificates =
                [
                    new CertificateIdentifier { RawData = certificate.RawData }
                ]
            };

            long before = Certificate.InstancesLeaked;

            using (CertificateCollection certificates = await trustList
                .GetCertificatesAsync(telemetry)
                .ConfigureAwait(false))
            {
                Assert.That(certificates, Has.Count.EqualTo(1));
            }

            Assert.That(
                Certificate.InstancesLeaked,
                Is.LessThanOrEqualTo(before),
                "reading the trust list left a certificate handle behind.");
        }

        /// <summary>
        /// A read that stops part way through the list releases what it had
        /// already collected. Cancellation is used to stop it, which is the
        /// reachable version of "an entry could not be read".
        /// </summary>
        [Test]
        public void GetCertificatesAsyncReleasesWhatItReadWhenTheReadIsStopped()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using Certificate certificate = CertificateBuilder
                .Create("CN=TrustListHandleFailure")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            string storePath = Path.Combine(
                Path.GetTempPath(),
                "OpcUaTrustListHandleTest_" + Guid.NewGuid().ToString("N"));

            try
            {
                var trustList = new CertificateTrustList
                {
                    TrustedCertificates =
                    [
                        new CertificateIdentifier { RawData = certificate.RawData },
                        // resolved from a store, so the cancellation below stops
                        // the loop after the entry above is in the collection.
                        new CertificateIdentifier
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = storePath,
                            Thumbprint = certificate.Thumbprint
                        }
                    ]
                };

                using var cts = new CancellationTokenSource();
                cts.Cancel();

                long before = Certificate.InstancesLeaked;

                Assert.That(
                    () => trustList.GetCertificatesAsync(telemetry, cts.Token),
                    Throws.InstanceOf<OperationCanceledException>());

                Assert.That(
                    Certificate.InstancesLeaked,
                    Is.LessThanOrEqualTo(before),
                    "the partially built collection was abandoned without being released.");
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
