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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Security.Certificates.Tests;
using Opc.Ua.Tests;
using X509AuthorityKeyIdentifierExtension = Opc.Ua.Security.Certificates.X509AuthorityKeyIdentifierExtension;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the CertificateFactory class.
    /// </summary>
    [TestFixture]
    [Category("CertificateFactory")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CertificateFactoryTest
    {
        [DatapointSource]
        public KeyHashPair[] KeyHashPairs = new KeyHashPairCollection
        {
            { 2048, HashAlgorithmName.SHA256 },
            { 3072, HashAlgorithmName.SHA384 },
            { 4096, HashAlgorithmName.SHA512 }
        }.ToArray();

        /// <summary>
        /// Create a dictionary for certificates.
        /// </summary>
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_rootCACertificate = new ConcurrentDictionary<int, Certificate>();
        }

        /// <summary>
        /// One time cleanup.
        /// </summary>
        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            foreach (Certificate cert in m_rootCACertificate.Values)
            {
                cert?.Dispose();
            }
        }

        /// <summary>
        /// Verify self signed app certs.
        /// </summary>
        [Theory]
        public void VerifySelfSignedAppCerts(KeyHashPair keyHashPair)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var appTestGenerator = new ApplicationTestDataGenerator(keyHashPair.KeySize, telemetry);
            ApplicationTestData app = appTestGenerator.ApplicationTestSet(1).First();
            using Certificate cert = CertificateFactory
                .CreateCertificate(
                    app.ApplicationUri,
                    app.ApplicationName,
                    app.Subject,
                    app.DomainNames)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .SetRSAKeySize(keyHashPair.KeySize)
                .CreateForRSA();
            Assert.That(cert, Is.Not.Null);
            Assert.That(cert.RawData, Is.Not.Null);
            Assert.That(cert.HasPrivateKey, Is.True);
            using (RSA rsa = cert.GetRSAPrivateKey())
            {
                rsa.ExportParameters(true);
            }
            using (RSA rsa = cert.GetRSAPublicKey())
            {
                rsa.ExportParameters(false);
            }
            Certificate plainCert = CertificateFactory.Create(cert.RawData);
            Assert.That(plainCert, Is.Not.Null);
            VerifyApplicationCert(app, plainCert);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.That(X509Utils.VerifySelfSigned(cert), Is.True, "Verify Self signed.");
        }

        /// <summary>
        /// Verify signed OPC UA app certs.
        /// </summary>
        [Theory]
        [Order(500)]
        public void VerifySignedAppCerts(KeyHashPair keyHashPair)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            Certificate issuerCertificate = GetIssuer(keyHashPair);
            Assert.That(issuerCertificate, Is.Not.Null);
            Assert.That(issuerCertificate.RawData, Is.Not.Null);
            Assert.That(issuerCertificate.HasPrivateKey, Is.True);
            var appTestGenerator = new ApplicationTestDataGenerator(keyHashPair.KeySize, telemetry);
            ApplicationTestData app = appTestGenerator.ApplicationTestSet(1).First();
            using Certificate cert = CertificateFactory
                .CreateCertificate(
                    app.ApplicationUri,
                    app.ApplicationName,
                    app.Subject,
                    app.DomainNames)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .SetIssuer(issuerCertificate)
                .SetRSAKeySize(keyHashPair.KeySize)
                .CreateForRSA();
            Assert.That(cert, Is.Not.Null);
            Assert.That(cert.RawData, Is.Not.Null);
            Assert.That(cert.HasPrivateKey, Is.True);
            using Certificate plainCert = CertificateFactory.Create(cert.RawData);
            Assert.That(plainCert, Is.Not.Null);
            VerifyApplicationCert(app, plainCert, issuerCertificate);
            X509Utils.VerifyRSAKeyPair(plainCert, cert, true);
        }

        /// <summary>
        /// Verify CA signed app certs.
        /// </summary>
        [Theory]
        [Order(100)]
        public void VerifyCACerts(KeyHashPair keyHashPair)
        {
            const string subject = "CN=CA Test Cert,O=OPC Foundation,C=US,S=Arizona";
            int pathLengthConstraint = (keyHashPair.KeySize / 512) - 3;
            Certificate cert = CertificateFactory
                .CreateCertificate(subject)
                .SetLifeTime(25 * 12)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .SetCAConstraint(pathLengthConstraint)
                .SetRSAKeySize(keyHashPair.KeySize)
                .CreateForRSA();
            Assert.That(cert, Is.Not.Null);
            Assert.That(cert.RawData, Is.Not.Null);
            Assert.That(cert.HasPrivateKey, Is.True);
            Certificate plainCert = CertificateFactory.Create(cert.RawData);
            Assert.That(plainCert, Is.Not.Null);
            VerifyCACert(plainCert, subject, pathLengthConstraint);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.That(X509Utils.VerifySelfSigned(cert), Is.True);
            m_rootCACertificate[keyHashPair.KeySize] = cert;
        }

        /// <summary>
        /// Verify CRL for CA signed app certs.
        /// </summary>
        [Theory]
        [Order(400)]
        public void VerifyCrlCerts(KeyHashPair keyHashPair)
        {
            int pathLengthConstraint = (keyHashPair.KeySize / 512) - 3;
            Certificate issuerCertificate = GetIssuer(keyHashPair);
            Assert.That(X509Utils.VerifySelfSigned(issuerCertificate), Is.True);

            using Certificate otherIssuerCertificate = CertificateFactory
                .CreateCertificate(issuerCertificate.Subject)
                .SetLifeTime(TimeSpan.FromDays(180))
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .SetCAConstraint(pathLengthConstraint)
                .CreateForRSA();
            Assert.That(X509Utils.VerifySelfSigned(otherIssuerCertificate), Is.True);

            var revokedCerts = new CertificateCollection();
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    Certificate cert = CertificateFactory
                        .CreateCertificate($"CN=Test Cert {i}, O=Contoso")
                        .SetIssuer(issuerCertificate)
                        .SetRSAKeySize(
                            (ushort)(keyHashPair.KeySize <= 2048 ? keyHashPair.KeySize : 2048))
                        .CreateForRSA();
                    revokedCerts.Add(cert);
                    Assert.That(X509Utils.VerifySelfSigned(cert), Is.False);
                }

                Assert.That(issuerCertificate, Is.Not.Null);
                Assert.That(issuerCertificate.RawData, Is.Not.Null);
                Assert.That(issuerCertificate.HasPrivateKey, Is.True);
                using (RSA rsa = issuerCertificate.GetRSAPrivateKey())
                {
                    Assert.That(rsa, Is.Not.Null);
                }

                using (Certificate plainCert = CertificateFactory.Create(
                    issuerCertificate.RawData))
                {
                    Assert.That(plainCert, Is.Not.Null);
                    VerifyCACert(plainCert, issuerCertificate.Subject, pathLengthConstraint);
                }
                Assert.That(X509Utils.VerifySelfSigned(issuerCertificate), Is.True);
                X509Utils.VerifyRSAKeyPair(issuerCertificate, issuerCertificate, true);

                X509CRL crl = CertificateFactory.RevokeCertificate(issuerCertificate, null, null);
                Assert.That(crl, Is.Not.Null);
                Assert.That(crl.VerifySignature(issuerCertificate, true), Is.True);
                X509CrlNumberExtension extension = crl.CrlExtensions
                    .FindExtension<X509CrlNumberExtension>();
                var crlCounter = new BigInteger(1);
                Assert.That(extension.CrlNumber, Is.EqualTo(crlCounter));
                var revokedList = new X509CRLCollection { crl };

                foreach (Certificate cert in revokedCerts)
                {
                    Assert.Throws<CryptographicException>(() =>
                        crl.VerifySignature(otherIssuerCertificate, true));
                    Assert.That(crl.IsRevoked(cert), Is.False);
                    X509CRL nextCrl = CertificateFactory.RevokeCertificate(
                        issuerCertificate,
                        revokedList,
                        new CertificateCollection([cert]));
                    crlCounter++;
                    Assert.That(nextCrl, Is.Not.Null);
                    Assert.That(nextCrl.IsRevoked(cert), Is.True);
                    extension = nextCrl.CrlExtensions.FindExtension<X509CrlNumberExtension>();
                    Assert.That(extension.CrlNumber, Is.EqualTo(crlCounter));
                    Assert.That(crl.VerifySignature(issuerCertificate, true), Is.True);
                    revokedList.Add(nextCrl);
                    crl = nextCrl;
                }

                foreach (Certificate cert in revokedCerts)
                {
                    Assert.That(crl.IsRevoked(cert), Is.True);
                }
            }
            finally
            {
                foreach (Certificate cert in revokedCerts)
                {
                    cert?.Dispose();
                }
            }
        }

        /// <summary>
        /// Parse a certificate blob.
        /// </summary>
        [Test]
        [Order(500)]
        public void ParseCertificateBlob()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // check if complete chain should be sent.
            if (m_rootCACertificate != null && !m_rootCACertificate.IsEmpty)
            {
                Certificate[] certArray = [.. m_rootCACertificate.Values];

                TestContext.Out.WriteLine("testing {0} certificates", certArray.Length);

                byte[] certBlob = Utils.CreateCertificateChainBlob([.. certArray]);

                byte[] singleBlob = AsnUtils.ParseX509Blob(certBlob).ToArray();
                Assert.That(singleBlob, Is.Not.Null);
                Certificate certX = CertificateFactory.Create(singleBlob);
                Assert.That(certX, Is.Not.Null);
                Assert.That(singleBlob, Is.EqualTo(certArray[0].RawData));
                Assert.That(certX.RawData, Is.EqualTo(singleBlob));
                Assert.That(certX.RawData, Is.EqualTo(certArray[0].RawData));

                Certificate cert = Utils.ParseCertificateBlob(certBlob, telemetry);
                Assert.That(cert, Is.Not.Null);
                Assert.That(certArray[0].RawData, Is.EqualTo(cert.RawData));
                CertificateCollection certChain = Utils.ParseCertificateChainBlob(certBlob, telemetry);
                Assert.That(certChain, Is.Not.Null);
                for (int i = 0; i < certArray.Length; i++)
                {
                    TestContext.Out.WriteLine(certChain[i]);
                    Assert.That(certArray[i].RawData, Is.EqualTo(certChain[i].RawData));
                }
            }
            else
            {
                Assert.Ignore("No certificates for blob test");
            }
        }

        [Test]
        [Category("X509Utils")]
        public void CompareDistinguishedNameWithStateAbbreviations()
        {
            // Test that ST= and S= are treated as equivalent for stateOrProvinceName
            // This addresses the Windows behavior where S= is used instead of ST=

            // Test 1: Direct comparison with different state abbreviations
            const string dnWithST = "CN=OPCUA Client, O=MyOrg, ST=California, C=US";
            const string dnWithS = "CN=OPCUA Client, O=MyOrg, S=California, C=US";

            Assert.That(X509Utils.CompareDistinguishedName(dnWithST, dnWithS),
                Is.True,
                "Distinguished names with ST= and S= should be considered equivalent");

            // Test 2: Reverse comparison
            Assert.That(X509Utils.CompareDistinguishedName(dnWithS, dnWithST),
                Is.True,
                "Distinguished names with S= and ST= should be considered equivalent (reversed)");

            // Test 3: With parsed distinguished names
            List<string> parsedDnWithST = X509Utils.ParseDistinguishedName(dnWithST);
            List<string> parsedDnWithS = X509Utils.ParseDistinguishedName(dnWithS);

            Assert.That(parsedDnWithST, Is.Not.Null);
            Assert.That(parsedDnWithS, Is.Not.Null);

            // Test 4: Case sensitivity for DC fields should still work
            const string dnWithDC1 = "CN=Test, DC=example, DC=com";
            const string dnWithDC2 = "CN=Test, DC=EXAMPLE, DC=COM";

            Assert.That(X509Utils.CompareDistinguishedName(dnWithDC1, dnWithDC2),
                Is.True,
                "DC fields should be case-insensitive");

            // Test 5: Different values should still fail
            const string dnDifferentState1 = "CN=OPCUA Client, O=MyOrg, ST=California, C=US";
            const string dnDifferentState2 = "CN=OPCUA Client, O=MyOrg, ST=NewYork, C=US";

            Assert.That(X509Utils.CompareDistinguishedName(dnDifferentState1, dnDifferentState2),
                Is.False,
                "Distinguished names with different state values should not match");

            // Test 6: Case with other fields
            const string dnComplex1 = "CN=Server, OU=Engineering, O=Company, ST=Texas, L=Austin, C=US";
            const string dnComplex2 = "CN=Server, OU=Engineering, O=Company, S=Texas, L=Austin, C=US";

            Assert.That(X509Utils.CompareDistinguishedName(dnComplex1, dnComplex2),
                Is.True,
                "Complex DN with ST= and S= should match");
        }

        private Certificate GetIssuer(KeyHashPair keyHashPair)
        {
            Certificate issuerCertificate = null;
            try
            {
                if (!m_rootCACertificate.TryGetValue(keyHashPair.KeySize, out issuerCertificate))
                {
                    VerifyCACerts(keyHashPair);
                    if (!m_rootCACertificate.TryGetValue(
                        keyHashPair.KeySize,
                        out issuerCertificate))
                    {
                        Assert.Ignore("Could not load Issuer Cert.");
                    }
                }
            }
            catch
            {
                Assert.Ignore("Could not load create Issuer Cert.");
            }
            return issuerCertificate;
        }

        private static void VerifyApplicationCert(
            ApplicationTestData testApp,
            Certificate cert,
            Certificate issuerCert = null)
        {
            bool signedCert = issuerCert != null;
            issuerCert ??= cert;
            TestContext.Out.WriteLine($"{nameof(VerifyApplicationCert)}:");
            Assert.That(cert, Is.Not.Null);
            TestContext.Out.WriteLine(cert);
            Assert.That(cert.HasPrivateKey, Is.False);
            Assert.That(X509Utils.CompareDistinguishedName(testApp.Subject, cert.Subject), Is.True);
            Assert.That(X509Utils.CompareDistinguishedName(issuerCert.Subject, cert.Issuer), Is.True);

            Assert.That(issuerCert.SubjectName.Name, Is.EqualTo(cert.IssuerName.Name));
            Assert.That(issuerCert.SubjectName.RawData, Is.EqualTo(cert.IssuerName.RawData));

            // test basic constraints
            X509BasicConstraintsExtension constraints = cert
                .FindExtension<X509BasicConstraintsExtension>();
            Assert.That(constraints, Is.Not.Null);
            TestContext.Out.WriteLine(constraints.Format(true));
            Assert.That(constraints.Critical, Is.True);
            if (signedCert)
            {
                Assert.That(constraints.CertificateAuthority, Is.False);
                Assert.That(constraints.HasPathLengthConstraint, Is.False);
            }
            else
            {
                Assert.That(constraints.CertificateAuthority, Is.False);
                Assert.That(constraints.HasPathLengthConstraint, Is.False);
            }

            // key usage
            X509KeyUsageExtension keyUsage = cert.FindExtension<X509KeyUsageExtension>();
            Assert.That(keyUsage, Is.Not.Null);
            TestContext.Out.WriteLine(keyUsage.Format(true));
            Assert.That(keyUsage.Critical, Is.True);
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.CrlSign, Is.Zero);
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.DataEncipherment,
                Is.EqualTo((int)X509KeyUsageFlags.DataEncipherment));
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.DecipherOnly, Is.Zero);
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.DigitalSignature,
                Is.EqualTo((int)X509KeyUsageFlags.DigitalSignature));
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.EncipherOnly, Is.Zero);
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.KeyAgreement, Is.Zero);
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.KeyCertSign,
                Is.EqualTo(signedCert ? 0 : (int)X509KeyUsageFlags.KeyCertSign));
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.KeyEncipherment,
                Is.EqualTo((int)X509KeyUsageFlags.KeyEncipherment));
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.NonRepudiation,
                Is.EqualTo((int)X509KeyUsageFlags.NonRepudiation));

            // enhanced key usage
            X509EnhancedKeyUsageExtension enhancedKeyUsage = cert
                .FindExtension<X509EnhancedKeyUsageExtension>();
            Assert.That(enhancedKeyUsage, Is.Not.Null);
            TestContext.Out.WriteLine(enhancedKeyUsage.Format(true));
            Assert.That(enhancedKeyUsage.Critical, Is.True);

            // test for authority key

            X509AuthorityKeyIdentifierExtension authority = cert
                .FindExtension<X509AuthorityKeyIdentifierExtension>();
            Assert.That(authority, Is.Not.Null);
            TestContext.Out.WriteLine(authority.Format(true));
            Assert.That(authority.SerialNumber, Is.Not.Null);
            Assert.That(authority.KeyIdentifier, Is.Not.Null);
            Assert.That(authority.Issuer, Is.Not.Null);
            if (issuerCert == null)
            {
                Assert.That(authority.Issuer.RawData, Is.EqualTo(cert.SubjectName.RawData));
                Assert.That(
                    X509Utils.CompareDistinguishedName(
                        cert.SubjectName.Name,
                        authority.Issuer.Name),
                    Is.True,
                    $"{cert.SubjectName.Name} != {authority.Issuer.Name}");
            }
            else
            {
                Assert.That(authority.Issuer.RawData, Is.EqualTo(issuerCert.SubjectName.RawData));
                Assert.That(
                    X509Utils.CompareDistinguishedName(
                        issuerCert.SubjectName.Name,
                        authority.Issuer.Name),
                    Is.True,
                    $"{cert.SubjectName.Name} != {authority.Issuer.Name}");
            }

            // verify authority key in signed cert
            X509SubjectKeyIdentifierExtension subjectKeyId = cert
                .FindExtension<X509SubjectKeyIdentifierExtension>();
            TestContext.Out.WriteLine(subjectKeyId.Format(true));
            if (signedCert)
            {
                X509SubjectKeyIdentifierExtension caCertSubjectKeyId =
                    issuerCert.FindExtension<X509SubjectKeyIdentifierExtension>();
                Assert.That(caCertSubjectKeyId, Is.Not.Null);
                Assert.That(authority.KeyIdentifier, Is.EqualTo(caCertSubjectKeyId.SubjectKeyIdentifier));
            }
            else
            {
                Assert.That(authority.KeyIdentifier, Is.EqualTo(subjectKeyId.SubjectKeyIdentifier));
            }
            Assert.That(authority.GetSerialNumber(), Is.EqualTo(issuerCert.GetSerialNumber()));
            Assert.That(authority.SerialNumber, Is.EqualTo(issuerCert.SerialNumber));

            X509SubjectAltNameExtension subjectAlternateName = cert
                .FindExtension<X509SubjectAltNameExtension>();
            Assert.That(subjectAlternateName, Is.Not.Null);
            TestContext.Out.WriteLine(subjectAlternateName.Format(true));
            Assert.That(subjectAlternateName.Critical, Is.False);
            ArrayOf<string> domainNames = X509Utils.GetDomainsFromCertificate(cert);
            foreach (string domainName in testApp.DomainNames)
            {
                Assert.That(domainNames.Contains(domainName, StringComparer.OrdinalIgnoreCase), Is.True);
            }
            Assert.That(subjectAlternateName.Uris, Has.Count.EqualTo(1));
            IReadOnlyList<string> applicationUris = X509Utils.GetApplicationUrisFromCertificate(cert);
            string applicationUri = applicationUris.Count > 0 ? applicationUris[0] : null;
            TestContext.Out.WriteLine("ApplicationUris: ");
            TestContext.Out.WriteLine(applicationUri);
            Assert.That(applicationUri, Is.EqualTo(testApp.ApplicationUri));
        }

        private static void VerifyCACert(
            Certificate cert,
            string subject,
            int pathLengthConstraint)
        {
            TestContext.Out.WriteLine($"{nameof(VerifyCACert)}:");

            Assert.That(cert, Is.Not.Null);
            TestContext.Out.WriteLine(cert);
            Assert.That(cert.HasPrivateKey, Is.False);
            Assert.That(X509Utils.CompareDistinguishedName(subject, cert.Subject), Is.True);
            Assert.That(X509Utils.CompareDistinguishedName(subject, cert.Issuer), Is.True);

            Assert.That(cert.Issuer, Is.EqualTo(cert.Subject));
            Assert.That(cert.IssuerName.RawData, Is.EqualTo(cert.SubjectName.RawData));

            // test basic constraints
            X509BasicConstraintsExtension constraints = cert
                .FindExtension<X509BasicConstraintsExtension>();
            Assert.That(constraints, Is.Not.Null);
            TestContext.Out.WriteLine(constraints.Format(true));
            Assert.That(constraints.Critical, Is.True);
            Assert.That(constraints.CertificateAuthority, Is.True);
            if (pathLengthConstraint < 0)
            {
                Assert.That(constraints.HasPathLengthConstraint, Is.False);
            }
            else
            {
                Assert.That(constraints.HasPathLengthConstraint, Is.True);
                Assert.That(constraints.PathLengthConstraint, Is.EqualTo(pathLengthConstraint));
            }

            // key usage
            X509KeyUsageExtension keyUsage = cert.FindExtension<X509KeyUsageExtension>();
            Assert.That(keyUsage, Is.Not.Null);
            TestContext.Out.WriteLine(keyUsage.Format(true));
            Assert.That(keyUsage.Critical, Is.True);
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.CrlSign,
                Is.EqualTo((int)X509KeyUsageFlags.CrlSign));
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.DataEncipherment, Is.Zero);
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.DecipherOnly, Is.Zero);
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.DigitalSignature,
                Is.EqualTo((int)X509KeyUsageFlags.DigitalSignature));
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.EncipherOnly, Is.Zero);
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.KeyAgreement, Is.Zero);
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.KeyCertSign,
                Is.EqualTo((int)X509KeyUsageFlags.KeyCertSign));
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.KeyEncipherment, Is.Zero);
            Assert.That((int)keyUsage.KeyUsages & (int)X509KeyUsageFlags.NonRepudiation, Is.Zero);

            // enhanced key usage
            X509EnhancedKeyUsageExtension enhancedKeyUsage = cert
                .FindExtension<X509EnhancedKeyUsageExtension>();
            Assert.That(enhancedKeyUsage, Is.Null);

            // test for authority key

            X509AuthorityKeyIdentifierExtension authority = cert
                .FindExtension<X509AuthorityKeyIdentifierExtension>();
            Assert.That(authority, Is.Not.Null);
            TestContext.Out.WriteLine(authority.Format(true));
            Assert.That(authority.SerialNumber, Is.Not.Null);
            Assert.That(authority.GetSerialNumber(), Is.Not.Null);
            Assert.That(authority.KeyIdentifier, Is.Not.Null);
            Assert.That(authority.Issuer, Is.Not.Null);
            Assert.That(authority.Issuer.RawData, Is.EqualTo(cert.IssuerName.RawData));
            Assert.That(authority.Issuer.Name, Is.EqualTo(cert.IssuerName.Name));
            Assert.That(
                Utils.ToHexString(authority.GetSerialNumber(), true),
                Is.EqualTo(authority.SerialNumber));

            // verify authority key in signed cert
            X509SubjectKeyIdentifierExtension subjectKeyId = cert
                .FindExtension<X509SubjectKeyIdentifierExtension>();
            TestContext.Out.WriteLine(subjectKeyId.Format(true));
            Assert.That(authority.KeyIdentifier, Is.EqualTo(subjectKeyId.SubjectKeyIdentifier));
            Assert.That(authority.SerialNumber, Is.EqualTo(cert.SerialNumber));
            Assert.That(authority.GetSerialNumber(), Is.EqualTo(cert.GetSerialNumber()));

            X509SubjectAltNameExtension subjectAlternateName = cert
                .FindExtension<X509SubjectAltNameExtension>();
            Assert.That(subjectAlternateName, Is.Null);
        }

        private ConcurrentDictionary<int, Certificate> m_rootCACertificate;
    }
}
