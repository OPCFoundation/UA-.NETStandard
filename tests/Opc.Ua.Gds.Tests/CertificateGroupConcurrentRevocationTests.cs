// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Revocations that run at the same time must all end up on the CRL of
    /// the issuing CA.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CertificateGroupConcurrentRevocationTests
    {
        private const int kRevocations = 16;
        private string m_path = null!;

        [SetUp]
        public void Setup()
        {
            m_path = Path.Combine(Path.GetTempPath(), "OPC", "GDS", "ConcurrentRevocation-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_path))
            {
                Directory.Delete(m_path, true);
            }
        }

        [Test]
        public void EquivalentDirectoryStorePathsShareOneCrlLock()
        {
            string authorities = Path.Combine(m_path, "authorities");
            SemaphoreSlim crlLock = CertificateGroup.GetCrlLock(new CertificateStoreIdentifier(authorities));

            Assert.That(
                CertificateGroup.GetCrlLock(
                    new CertificateStoreIdentifier(Path.Combine(m_path, ".", "authorities"))),
                Is.SameAs(crlLock));
            Assert.That(
                CertificateGroup.GetCrlLock(
                    new CertificateStoreIdentifier(authorities + Path.DirectorySeparatorChar)),
                Is.SameAs(crlLock));
            Assert.That(
                CertificateGroup.GetCrlLock(
                    new CertificateStoreIdentifier(Path.Combine(m_path, "other", "..", "authorities"))),
                Is.SameAs(crlLock));
            Assert.That(
                CertificateGroup.GetCrlLock(new CertificateStoreIdentifier(Path.Combine(m_path, "issuers"))),
                Is.Not.SameAs(crlLock));
        }

        [Test]
        public async Task ParallelRevocationsInOneGroupAreAllOnTheCrlAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var configuration = new CertificateGroupConfiguration
            {
                SubjectName = "CN=GDS Concurrent Revocation CA, O=OPC Foundation",
                BaseStorePath = m_path,
                CertificateTypes = [nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)]
            };
            using var certificateGroup = (CertificateGroup)new CertificateGroup(telemetry).Create(
                m_path + "/authorities",
                configuration);
            using Certificate caCertificate = await certificateGroup
                .CreateCACertificateAsync(configuration.SubjectName, certificateGroup.CertificateTypes[0])
                .ConfigureAwait(false);

            using Certificate signingKey = await certificateGroup
                .LoadSigningKeyAsync(caCertificate, null, telemetry)
                .ConfigureAwait(false);
            var issued = new List<Certificate>();
            try
            {
                for (int ii = 0; ii < kRevocations; ii++)
                {
                    issued.Add(CertificateBuilder.Create("CN=Revoked" + ii)
                        .SetIssuer(signingKey)
                        .CreateForRSA());
                }

                await Task.WhenAll(issued.Select(certificate =>
                    Task.Run(() => certificateGroup.RevokeCertificateAsync(certificate))))
                    .ConfigureAwait(false);

                using ICertificateStore authorities = certificateGroup.AuthoritiesStore.OpenStore(telemetry);
                X509CRLCollection crls = await authorities.EnumerateCRLsAsync(caCertificate, false)
                    .ConfigureAwait(false);
                Assert.That(crls, Is.Not.Empty);
                foreach (Certificate certificate in issued)
                {
                    Assert.That(
                        crls.Any(crl => crl.IsRevoked(certificate)),
                        Is.True,
                        $"{certificate.Subject} is not on the CRL.");
                }

                // The CRL copied into the group trust list is complete as well.
                using ICertificateStore trusted = new CertificateStoreIdentifier(configuration.TrustedListPath)
                    .OpenStore(telemetry);
                X509CRLCollection trustedCrls = await trusted.EnumerateCRLsAsync(caCertificate, false)
                    .ConfigureAwait(false);
                foreach (Certificate certificate in issued)
                {
                    Assert.That(
                        trustedCrls.Any(crl => crl.IsRevoked(certificate)),
                        Is.True,
                        $"{certificate.Subject} is not on the trust list CRL.");
                }
            }
            finally
            {
                foreach (Certificate certificate in issued)
                {
                    certificate.Dispose();
                }
            }
        }
    }
}
