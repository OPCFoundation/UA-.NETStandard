// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.Security.Certificates.Tests
{
    [TestFixture]
    [Category("CertificateManager")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class DefaultCertificateIssuerTests
    {
        [Test]
        public void IssueCertificateSignsWithIssuer()
        {
            var issuer = new DefaultCertificateIssuer();

            using Certificate caCert = CertificateBuilder
                .Create("CN=TestCA")
                .SetCAConstraint()
                .CreateForRSA();

            ICertificateBuilder appBuilder = CertificateBuilder
                .Create("CN=TestApp");

            using Certificate appCert = issuer.IssueCertificate(appBuilder, caCert);

            Assert.That(appCert, Is.Not.Null);
            Assert.That(appCert.Issuer, Is.EqualTo(caCert.Subject));
        }

        [Test]
        public void RevokeCertificatesCreatesCRL()
        {
            var issuer = new DefaultCertificateIssuer();

            using Certificate caCert = CertificateBuilder
                .Create("CN=TestCA")
                .SetCAConstraint()
                .CreateForRSA();

            ICertificateBuilder appBuilder = CertificateBuilder
                .Create("CN=RevokeTest");

            using Certificate appCert = issuer.IssueCertificate(appBuilder, caCert);

            using var revokedCerts = new CertificateCollection { appCert };

            X509CRL crl = issuer.RevokeCertificates(
                caCert,
                existingCrls: null!,
                revokedCerts);

            Assert.That(crl, Is.Not.Null);
            Assert.That(crl.RevokedCertificates, Is.Not.Empty);
            Assert.That(
                crl.RevokedCertificates.Any(
                    rc => rc.SerialNumber == appCert.SerialNumber),
                Is.True);
        }
    }
}
