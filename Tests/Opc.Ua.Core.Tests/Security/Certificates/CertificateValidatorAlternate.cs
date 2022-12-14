/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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

using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the CertificateValidator class with
    /// 
    /// </summary>
    [TestFixture, Category("CertificateValidator")]
    [NonParallelizable]
    [SetCulture("en-us")]

    class CertificateValidatorAlternate
    {
        const string kCaSubject = "CN=Root";
        const string kCaAltSubject = "CN=rOOT";
        private X509Certificate2 m_rootCert;
        private X509Certificate2 m_rootAltCert;
        private X509CRL m_rootCrl;
        private TemporaryCertValidator m_validator;
        private CertificateValidator m_certValidator;

        #region Test Setup
        /// <summary>
        /// Set up a Server fixture.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            // create the root cert
            m_rootCert = CertificateFactory.CreateCertificate(kCaSubject)
                .SetLifeTime(25 * 12)
                .SetCAConstraint()
                .CreateForRSA();

            // default empty root CRL
            m_rootCrl = CertificateFactory.RevokeCertificate(m_rootCert, null, null);

            // a valid app cert
            var appCert = CertificateFactory.CreateCertificate("CN=AppCert")
                .SetIssuer(m_rootCert)
                .CreateForRSA();

            // create cert validator for test, add trusted root cert
            m_validator = TemporaryCertValidator.Create();
            await m_validator.TrustedStore.Add(m_rootCert).ConfigureAwait(false);
            await m_validator.TrustedStore.AddCRL(m_rootCrl).ConfigureAwait(false);
            m_certValidator = m_validator.Update();

            // create a root with same serial number but modified Subject
            m_rootAltCert = CertificateFactory.CreateCertificate(kCaAltSubject)
                .SetLifeTime(25 * 12)
                .SetSerialNumber(m_rootCert.GetSerialNumber())
                .SetCAConstraint()
                .CreateForRSA();
        }

        /// <summary>
        /// Tear down the server fixture.
        /// </summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Utils.SilentDispose(m_rootCert);
            Utils.SilentDispose(m_rootAltCert);
            Utils.SilentDispose(m_certValidator);
        }

        /// <summary>
        /// Create a Setup for a test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
        }

        /// <summary>
        /// Tear down the test.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
        }
        #endregion

        /// <summary>
        /// A signed app cert that has no keyid information.
        /// </summary>
        [Test]
        public void CertificateWithoutKeyID()
        {
            // a valid app cert
            using (var appCert = CertificateFactory.CreateCertificate("CN=AppCert")
                .SetIssuer(m_rootCert)
                .CreateForRSA())
            {
                Assert.NotNull(appCert);

                m_certValidator.RejectUnknownRevocationStatus = true;
                m_certValidator.Validate(appCert);
            }
        }

        /// <summary>
        /// Alternate Root without KeyID.
        /// </summary>
        [Theory]
        public void AlternateRootCertificateWithoutKeyID(bool rejectUnknownRevocationStatus)
        {
            // create app cert from alternate root
            using (var altAppCert = CertificateFactory.CreateCertificate("CN=AlternateAppCert")
                .SetIssuer(m_rootAltCert)
                .CreateForRSA())
            {
                Assert.NotNull(altAppCert);

                m_certValidator.RejectUnknownRevocationStatus = rejectUnknownRevocationStatus;
                var result = Assert.Throws<ServiceResultException>(() => m_certValidator.Validate(altAppCert));

                TestContext.Out.WriteLine(result.Message);
            }
        }

        /// <summary>
        /// Certificate with combinations of optional fields in the AKI.
        /// </summary>
        [Theory]
        public void CertificateWithAuthorityKeyID(bool subjectKeyIdentifier, bool issuerName, bool serialNumber)
        {
            // force exception if SKI is not present
            var ski = m_rootCert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().Single();

            // create a certificate with special key info
            X509Extension authorityKeyIdentifier = new X509AuthorityKeyIdentifierExtension(
                    (byte[])(subjectKeyIdentifier ? ski.SubjectKeyIdentifier.FromHexString() : null),
                    (X500DistinguishedName)(issuerName ? m_rootCert.IssuerName : null),
                    (byte[])(serialNumber ? m_rootCert.GetSerialNumber() : null));

            // a valid app cert
            using (var appCert = CertificateFactory.CreateCertificate("CN=AppCert")
                .AddExtension(authorityKeyIdentifier)
                .SetIssuer(m_rootCert)
                .CreateForRSA())
            {
                m_certValidator.RejectUnknownRevocationStatus = false;

                // ensure behavior is preserved, no chain building without KeyID/serial
                if (!subjectKeyIdentifier && !serialNumber)
                {
                    // should not pass!
                    var result = Assert.Throws<ServiceResultException>(() => m_certValidator.Validate(appCert));
                    TestContext.Out.WriteLine($"{result.Result}: {result.Message}");
                }
                else
                {
                    m_certValidator.Validate(appCert);
                }
            }
        }

        /// <summary>
        /// Create an app certificate from the alternate root,
        /// validate that any combination of AKI is not validated.
        /// </summary>
        [Theory]
        public void AlternateCertificateWithAuthorityKeyID(bool subjectKeyIdentifier, bool issuerName, bool serialNumber)
        {
            // force exception if SKI is not present
            var ski = m_rootAltCert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().Single();

            // create a certificate with special key info / no key id
            X509Extension authorityKeyIdentifier = new X509AuthorityKeyIdentifierExtension(
                    (byte[])(subjectKeyIdentifier ? ski.SubjectKeyIdentifier.FromHexString() : null),
                    (X500DistinguishedName)(issuerName ? m_rootAltCert.IssuerName : null),
                    (byte[])(serialNumber ? m_rootAltCert.GetSerialNumber() : null));

            // create the certificate from the alternate root
            using (var altAppCert = CertificateFactory.CreateCertificate("CN=AltAppCert")
                .AddExtension(authorityKeyIdentifier)
                .SetIssuer(m_rootAltCert)
                .CreateForRSA())
            {
                Assert.NotNull(altAppCert);

                // should not pass!
                m_certValidator.RejectUnknownRevocationStatus = false;
                var result = Assert.Throws<ServiceResultException>(() => m_certValidator.Validate(altAppCert));

                TestContext.Out.WriteLine($"{result.Result}: {result.Message}");
            }
        }

        /// <summary>
        /// Create an app certificate from the alternate root,
        /// with keyID based on the trusted root.
        /// </summary>
        [Theory]
        public void AlternateCertificateWithTrustedAuthorityKeyID(bool subjectKeyIdentifier, bool issuerName, bool serialNumber)
        {
            // force exception if SKI is not present
            var ski = m_rootCert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().Single();

            // create a certificate with special key info / no key id
            X509Extension authorityKeyIdentifier = new X509AuthorityKeyIdentifierExtension(
                    (byte[])(subjectKeyIdentifier ? ski.SubjectKeyIdentifier.FromHexString() : null),
                    (X500DistinguishedName)(issuerName ? m_rootCert.IssuerName : null),
                    (byte[])(serialNumber ? m_rootCert.GetSerialNumber() : null));

            // create the certificate from the alternate root
            using (var altAppCert = CertificateFactory.CreateCertificate("CN=AltAppCert")
                .AddExtension(authorityKeyIdentifier)
                .SetIssuer(m_rootAltCert)
                .CreateForRSA())
            {
                Assert.NotNull(altAppCert);

                // should not pass!
                m_certValidator.RejectUnknownRevocationStatus = false;
                var result = Assert.Throws<ServiceResultException>(() => m_certValidator.Validate(altAppCert));

                TestContext.Out.WriteLine($"{result.Result}: {result.Message}");
            }
        }
    }
}
