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
using System.Security.Cryptography;
using NUnit.Framework;

#pragma warning disable CS0618 // Tests exercise obsolete CertificateFactory methods intentionally

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Tests for the Pkcs10CertificationRequest class.
    /// </summary>
    [TestFixture]
    [Category("Certificate")]
    [Category("PKCS10")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class Pkcs10CertificationRequestTests
    {
        /// <summary>
        /// Test parsing a valid RSA CSR from file.
        /// </summary>
        [Test]
        public void ParseValidRsaCsrFromFile()
        {
            // Load the test CSR file
            string csrPath = Path.Combine(Utils.GetAbsoluteDirectoryPath("Assets", true, false, false), "test_rsa.csr");
            byte[] csrData = File.ReadAllBytes(csrPath);

            // Parse the CSR
            var csr = new Pkcs10CertificationRequest(csrData);

            // Verify subject
            Assert.That(csr.Subject, Is.Not.Null);
            Assert.That(csr.Subject.Name, Is.Not.Empty);

            // Verify public key info
            Assert.That(csr.SubjectPublicKeyInfo, Is.Not.Null);
            Assert.That(csr.SubjectPublicKeyInfo, Is.Not.Empty);

            // Verify signature
            bool isValid = csr.Verify();
            Assert.That(isValid, Is.True, "CSR signature should be valid");
        }

        /// <summary>
        /// Test creating and parsing an RSA CSR.
        /// </summary>
        [Test]
        public void CreateAndParseRsaCsr()
        {
            const string subject = "CN=Test RSA CSR, O=OPC Foundation";
            const string applicationUri = "urn:localhost:opcfoundation.org:TestRsaCsr";
            string[] domainNames = ["localhost", "127.0.0.1"];

            // Create a certificate to generate CSR from
            using Certificate certificate = CertificateBuilder.Create(subject)
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(TimeSpan.FromDays(30))
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, domainNames))
                .CreateForRSA();

            // Create CSR
            byte[] csrData = CertificateFactory.CreateSigningRequest(certificate, domainNames);
            Assert.That(csrData, Is.Not.Null);
            Assert.That(csrData, Is.Not.Empty);

            // Parse the CSR
            var csr = new Pkcs10CertificationRequest(csrData);

            // Verify subject
            Assert.That(csr.Subject, Is.Not.Null);
            Assert.That(csr.Subject.Name, Does.Contain("CN=Test RSA CSR"));

            // Verify signature
            bool isValid = csr.Verify();
            Assert.That(isValid, Is.True, "CSR signature should be valid");

            // Verify SubjectPublicKeyInfo
            Assert.That(csr.SubjectPublicKeyInfo, Is.Not.Null);
            Assert.That(csr.SubjectPublicKeyInfo, Is.Not.Empty);
        }

        /// <summary>
        /// Test creating and parsing an ECDSA CSR (P-256).
        /// </summary>
        [Test]
        public void CreateAndParseEcdsaCsrP256()
        {
            const string subject = "CN=Test ECDSA P256 CSR, O=OPC Foundation";
            const string applicationUri = "urn:localhost:opcfoundation.org:TestEcdsaCsr";
            string[] domainNames = ["localhost", "127.0.0.1"];

            // Create a certificate to generate CSR from
            using Certificate certificate = CertificateBuilder.Create(subject)
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(TimeSpan.FromDays(30))
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, domainNames))
                .SetECCurve(ECCurve.NamedCurves.nistP256)
                .CreateForECDsa();

            // Create CSR
            byte[] csrData = CertificateFactory.CreateSigningRequest(certificate, domainNames);
            Assert.That(csrData, Is.Not.Null);
            Assert.That(csrData, Is.Not.Empty);

            // Parse the CSR
            var csr = new Pkcs10CertificationRequest(csrData);

            // Verify subject
            Assert.That(csr.Subject, Is.Not.Null);
            Assert.That(csr.Subject.Name, Does.Contain("CN=Test ECDSA P256 CSR"));

            // Verify SubjectPublicKeyInfo
            Assert.That(csr.SubjectPublicKeyInfo, Is.Not.Null);
            Assert.That(csr.SubjectPublicKeyInfo, Is.Not.Empty);

            // Verify signature
#if NET6_0_OR_GREATER && !SKIP_ECC_CERTIFICATE_REQUEST_SIGNING
            bool isValid = csr.Verify();
            Assert.That(isValid, Is.True, "ECDSA CSR signature should be valid");
#else
            // ECDSA verification not supported on older frameworks
            Assert.Throws<NotSupportedException>(() => csr.Verify());
#endif
        }

        /// <summary>
        /// Test parsing CSR with null data throws ArgumentNullException.
        /// </summary>
        [Test]
        public void ParseNullCsrThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new Pkcs10CertificationRequest(null));
        }

        /// <summary>
        /// Test parsing invalid CSR data throws CryptographicException.
        /// </summary>
        [Test]
        public void ParseInvalidCsrThrowsCryptographicException()
        {
            byte[] invalidData = [0x01, 0x02, 0x03, 0x04];
            Assert.Throws<CryptographicException>(() => new Pkcs10CertificationRequest(invalidData));
        }

        /// <summary>
        /// Test parsing CSR with tampered signature fails verification.
        /// </summary>
        [Test]
        public void ParseCsrWithTamperedSignatureFails()
        {
            const string subject = "CN=Test Tampered CSR, O=OPC Foundation";
            const string applicationUri = "urn:localhost:opcfoundation.org:TestTamperedCsr";
            string[] domainNames = ["localhost"];

            // Create a certificate to generate CSR from
            using Certificate certificate = CertificateBuilder.Create(subject)
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(TimeSpan.FromDays(30))
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, domainNames))
                .CreateForRSA();

            // Create CSR
            byte[] csrData = CertificateFactory.CreateSigningRequest(certificate, domainNames);

            // Tamper with the signature (last 10 bytes)
            for (int i = csrData.Length - 10; i < csrData.Length; i++)
            {
                csrData[i] ^= 0xFF;
            }

            // Parse should succeed but verification should fail
            var csr = new Pkcs10CertificationRequest(csrData);
            bool isValid = csr.Verify();
            Assert.That(isValid, Is.False, "Tampered CSR signature should be invalid");
        }

        /// <summary>
        /// Test parsing CSR and extracting Subject Alternative Name.
        /// </summary>
        [Test]
        public void ParseCsrAndExtractSubjectAltName()
        {
            const string subject = "CN=Test SAN CSR, O=OPC Foundation";
            const string applicationUri = "urn:localhost:opcfoundation.org:TestSanCsr";
            string[] domainNames = ["localhost", "testhost.local", "192.168.1.1"];

            // Create a certificate to generate CSR from
            using Certificate certificate = CertificateBuilder.Create(subject)
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(TimeSpan.FromDays(30))
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, domainNames))
                .CreateForRSA();

            // Create CSR
            byte[] csrData = CertificateFactory.CreateSigningRequest(certificate, domainNames);

            // Parse the CSR
            var csr = new Pkcs10CertificationRequest(csrData);

            // Extract Subject Alternative Name
            X509SubjectAltNameExtension sanExtension = Pkcs10Utils.GetSubjectAltNameExtension(csr.Attributes);

            Assert.That(sanExtension, Is.Not.Null);
            Assert.That(sanExtension.Uris, Has.Count.EqualTo(1));
            Assert.That(sanExtension.Uris[0], Is.EqualTo(applicationUri));

            // Verify domain names (may include URIs and domain names)
            int totalNames = sanExtension.DomainNames.Count + sanExtension.IPAddresses.Count;
            Assert.That(totalNames, Is.EqualTo(domainNames.Length));
        }

        /// <summary>
        /// Test parsing CSR with minimal attributes.
        /// </summary>
        [Test]
        public void ParseCsrWithMinimalAttributes()
        {
            const string subject = "CN=Test Minimal Attributes CSR, O=OPC Foundation";

            // Create a simple certificate without explicit SAN
            using Certificate certificate = CertificateBuilder.Create(subject)
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(TimeSpan.FromDays(30))
                .CreateForRSA();

            // Create CSR
            // Note: CertificateFactory.CreateSigningRequest always adds a SAN extension
            byte[] csrData = CertificateFactory.CreateSigningRequest(certificate);

            // Parse the CSR
            var csr = new Pkcs10CertificationRequest(csrData);

            // Extract Subject Alternative Name
            // CertificateFactory always creates a SAN extension, even if empty
            X509SubjectAltNameExtension sanExtension = Pkcs10Utils.GetSubjectAltNameExtension(csr.Attributes);

            // SAN extension should exist (created by CertificateFactory)
            Assert.That(sanExtension, Is.Not.Null);
        }

        /// <summary>
        /// Test GetCertificationRequestInfo returns valid data.
        /// </summary>
        [Test]
        public void GetCertificationRequestInfoReturnsValidData()
        {
            const string subject = "CN=Test Info CSR, O=OPC Foundation";

            using Certificate certificate = CertificateBuilder.Create(subject)
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(TimeSpan.FromDays(30))
                .CreateForRSA();

            byte[] csrData = CertificateFactory.CreateSigningRequest(certificate);
            var csr = new Pkcs10CertificationRequest(csrData);

            byte[] requestInfo = csr.GetCertificationRequestInfo();
            Assert.That(requestInfo, Is.Not.Null);
            Assert.That(requestInfo, Is.Not.Empty);
        }

        private static readonly string[] s_domainNames = ["localhost"];

        /// <summary>
        /// Test parsing multiple CSRs in sequence.
        /// </summary>
        [Test]
        public void ParseMultipleCsrsInSequence()
        {
            const int count = 5;
            var csrList = new System.Collections.Generic.List<Pkcs10CertificationRequest>();

            for (int i = 0; i < count; i++)
            {
                string subject = $"CN=Test CSR {i}, O=OPC Foundation";
                string applicationUri = $"urn:localhost:opcfoundation.org:TestCsr{i}";

                using Certificate certificate = CertificateBuilder.Create(subject)
                    .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                    .SetLifeTime(TimeSpan.FromDays(30))
                    .AddExtension(new X509SubjectAltNameExtension(applicationUri, s_domainNames))
                    .CreateForRSA();

                byte[] csrData = CertificateFactory.CreateSigningRequest(certificate);
                var csr = new Pkcs10CertificationRequest(csrData);

                Assert.That(csr, Is.Not.Null);
                Assert.That(csr.Verify(), Is.True);
                csrList.Add(csr);
            }

            Assert.That(csrList, Has.Count.EqualTo(count));
        }

        /// <summary>
        /// Test that Subject property contains expected DN components.
        /// </summary>
        [Test]
        public void SubjectContainsExpectedDNComponents()
        {
            const string subject = "CN=TestSubject, O=TestOrg, C=US, ST=TestState, L=TestCity";

            using Certificate certificate = CertificateBuilder.Create(subject)
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(TimeSpan.FromDays(30))
                .CreateForRSA();

            byte[] csrData = CertificateFactory.CreateSigningRequest(certificate);
            var csr = new Pkcs10CertificationRequest(csrData);

            string subjectName = csr.Subject.Name;
            Assert.That(subjectName, Does.Contain("CN=TestSubject"));
            Assert.That(subjectName, Does.Contain("O=TestOrg"));
        }
    }
}
