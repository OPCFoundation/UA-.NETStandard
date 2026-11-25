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

        [Test]
        public async Task TrustListGetsLatestCrlWhenOutdatedCrlIsRetainedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            const string subjectName = "CN=GDS Outdated Crl CA, O=OPC Foundation";
            CertificateGroupConfiguration configuration = CreateConfiguration(m_path, subjectName);
            using var certificateGroup = new TrustListUpdatingCertificateGroup(
                m_path + "/authorities",
                configuration,
                telemetry);
            using Certificate caCertificate = await certificateGroup
                .CreateCACertificateAsync(subjectName, certificateGroup.CertificateTypes[0])
                .ConfigureAwait(false);
            using Certificate signingKey = await certificateGroup
                .LoadSigningKeyAsync(caCertificate, null, telemetry)
                .ConfigureAwait(false);
            using Certificate first = CertificateBuilder.Create("CN=RevokedFirst").SetIssuer(signingKey).CreateForRSA();
            using Certificate second = CertificateBuilder.Create("CN=RevokedSecond").SetIssuer(signingKey).CreateForRSA();

            X509CRL outdated = await certificateGroup.RevokeCertificateAsync(first).ConfigureAwait(false);
            X509CRL latest = await certificateGroup.RevokeCertificateAsync(second).ConfigureAwait(false);
            Assert.That(CertificateGroup.SelectLatestCrl([outdated, latest]), Is.SameAs(latest));
            Assert.That(CertificateGroup.SelectLatestCrl([latest, outdated]), Is.SameAs(latest));

            // The outdated CRL is still in the authorities store, as after a
            // failed delete. It sorts after the latest one.
            File.WriteAllBytes(
                Path.Combine(m_path, "authorities", "crl", "zzz-outdated.crl"),
                outdated.RawData);

            await certificateGroup.UpdateTrustedListAsync().ConfigureAwait(false);

            using ICertificateStore trusted = new CertificateStoreIdentifier(configuration.TrustedListPath)
                .OpenStore(telemetry);
            X509CRLCollection trustedCrls = await trusted.EnumerateCRLsAsync(caCertificate, false)
                .ConfigureAwait(false);
            Assert.That(trustedCrls.Any(crl => crl.IsRevoked(second)), Is.True);
            Assert.That(trustedCrls.Any(crl => crl.IsRevoked(first)), Is.True);
        }

        [Test]
        public async Task IssuingGroupIsTheGroupOfTheSigningCaAsync()
        {
            // Two groups with the same CA subject name but different CA keys.
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            const string subjectName = "CN=GDS Shared Subject CA, O=OPC Foundation";
            string firstPath = Path.Combine(m_path, "first");
            string secondPath = Path.Combine(m_path, "second");
            CertificateGroupConfiguration firstConfiguration = CreateConfiguration(firstPath, subjectName);
            CertificateGroupConfiguration secondConfiguration = CreateConfiguration(secondPath, subjectName);
            using var firstGroup = (CertificateGroup)new CertificateGroup(telemetry).Create(
                firstPath + "/authorities",
                firstConfiguration);
            using var secondGroup = (CertificateGroup)new CertificateGroup(telemetry).Create(
                secondPath + "/authorities",
                secondConfiguration);
            using Certificate firstCa = await firstGroup
                .CreateCACertificateAsync(subjectName, firstGroup.CertificateTypes[0])
                .ConfigureAwait(false);
            using Certificate secondCa = await secondGroup
                .CreateCACertificateAsync(subjectName, secondGroup.CertificateTypes[0])
                .ConfigureAwait(false);
            using Certificate secondKey = await secondGroup
                .LoadSigningKeyAsync(secondCa, null, telemetry)
                .ConfigureAwait(false);
            using Certificate issued = CertificateBuilder.Create("CN=IssuedBySecond").SetIssuer(secondKey).CreateForRSA();

            Assert.That(
                ApplicationsNodeManager.FindIssuingGroup([firstGroup, secondGroup], issued),
                Is.SameAs(secondGroup));
            Assert.That(
                ApplicationsNodeManager.FindIssuingGroup([secondGroup, firstGroup], issued),
                Is.SameAs(secondGroup));
        }

        private static CertificateGroupConfiguration CreateConfiguration(string basePath, string subjectName)
        {
            return new CertificateGroupConfiguration
            {
                SubjectName = subjectName,
                BaseStorePath = basePath,
                CertificateTypes = [nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)]
            };
        }

        private sealed class TrustListUpdatingCertificateGroup : CertificateGroup
        {
            public TrustListUpdatingCertificateGroup(
                string authoritiesStorePath,
                CertificateGroupConfiguration configuration,
                ITelemetryContext telemetry)
                : base(authoritiesStorePath, configuration, telemetry, null)
            {
            }

            public Task UpdateTrustedListAsync()
            {
                return UpdateAuthorityCertInCertificateStoreAsync(
                    new CertificateStoreIdentifier(Configuration.TrustedListPath));
            }
        }
    }
}
