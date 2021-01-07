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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Security.Certificates.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the CertificateFactory class.
    /// </summary>
    [TestFixture, Category("CertificateFactory")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CertificateFactoryTest
    {
        #region DataPointSources
        [DatapointSource]
        public KeyHashPair[] KeyHashPairs = new KeyHashPairCollection {
            { 2048, HashAlgorithmName.SHA256 },
            { 3072, HashAlgorithmName.SHA384 },
            { 4096, HashAlgorithmName.SHA512 }
        }.ToArray();
        #endregion

        #region Test Setup
        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_rootCACertificate = new ConcurrentDictionary<int, X509Certificate2>();
        }

        /// <summary>
        /// Clean up the Test PKI folder
        /// </summary>
        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }
        #endregion

        #region Test Methods
        /// <summary>
        /// Verify self signed app certs.
        /// </summary>
        [Theory]
        public void VerifySelfSignedAppCerts(
            KeyHashPair keyHashPair
            )
        {
            var appTestGenerator = new ApplicationTestDataGenerator(keyHashPair.KeySize);
            ApplicationTestData app = appTestGenerator.ApplicationTestSet(1).First();
            var cert = CertificateFactory.CreateCertificate(app.ApplicationUri, app.ApplicationName, app.Subject, app.DomainNames)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .SetRSAKeySize(keyHashPair.KeySize)
                .CreateForRSA();
            Assert.NotNull(cert);
            Assert.NotNull(cert.RawData);
            Assert.True(cert.HasPrivateKey);
            using (RSA rsa = cert.GetRSAPrivateKey())
            {
                rsa.ExportParameters(true);
            }
            using (RSA rsa = cert.GetRSAPublicKey())
            {
                rsa.ExportParameters(false);
            }
            var plainCert = new X509Certificate2(cert.RawData);
            Assert.NotNull(plainCert);
            VerifyApplicationCert(app, plainCert);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert));
        }

        /// <summary>
        /// Verify signed OPC UA app certs.
        /// </summary>
        [Theory, Order(500)]
        public void VerifySignedAppCerts(
            KeyHashPair keyHashPair
            )
        {
            X509Certificate2 issuerCertificate = GetIssuer(keyHashPair);
            Assert.NotNull(issuerCertificate);
            Assert.NotNull(issuerCertificate.RawData);
            Assert.True(issuerCertificate.HasPrivateKey);
            var appTestGenerator = new ApplicationTestDataGenerator(keyHashPair.KeySize);
            ApplicationTestData app = appTestGenerator.ApplicationTestSet(1).First();
            var cert = CertificateFactory.CreateCertificate(
                app.ApplicationUri, app.ApplicationName, app.Subject, app.DomainNames)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .SetIssuer(issuerCertificate)
                .SetRSAKeySize(keyHashPair.KeySize)
                .CreateForRSA();
            Assert.NotNull(cert);
            Assert.NotNull(cert.RawData);
            Assert.True(cert.HasPrivateKey);
            using (var plainCert = new X509Certificate2(cert.RawData))
            {
                Assert.NotNull(plainCert);
                VerifyApplicationCert(app, plainCert, issuerCertificate);
                X509Utils.VerifyRSAKeyPair(plainCert, cert, true);
            }
        }


        /// <summary>
        /// Verify CA signed app certs.
        /// </summary>
        [Theory, Order(100)]
        public void VerifyCACerts(
            KeyHashPair keyHashPair
            )
        {
            var subject = "CN=CA Test Cert";
            int pathLengthConstraint = (keyHashPair.KeySize / 512) - 3;
            var cert = CertificateFactory.CreateCertificate(subject)
                .SetLifeTime(25 * 12)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .SetCAConstraint(pathLengthConstraint)
                .SetRSAKeySize(keyHashPair.KeySize)
                .CreateForRSA();
            Assert.NotNull(cert);
            Assert.NotNull(cert.RawData);
            Assert.True(cert.HasPrivateKey);
            var plainCert = new X509Certificate2(cert.RawData);
            Assert.NotNull(plainCert);
            VerifyCACert(plainCert, subject, pathLengthConstraint);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert));
            m_rootCACertificate[keyHashPair.KeySize] = cert;
        }

        /// <summary>
        /// Verify CRL for CA signed app certs.
        /// </summary>
        [Theory, Order(400)]
        public void VerifyCrlCerts(
            KeyHashPair keyHashPair
            )
        {
            int pathLengthConstraint = (keyHashPair.KeySize / 512) - 3;
            X509Certificate2 issuerCertificate = GetIssuer(keyHashPair);
            Assert.True(X509Utils.VerifySelfSigned(issuerCertificate));

            var otherIssuerCertificate = CertificateFactory.CreateCertificate(issuerCertificate.Subject)
                .SetLifeTime(TimeSpan.FromDays(180))
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .SetCAConstraint(pathLengthConstraint)
                .CreateForRSA();
            Assert.True(X509Utils.VerifySelfSigned(otherIssuerCertificate));

            X509Certificate2Collection revokedCerts = new X509Certificate2Collection();
            for (int i = 0; i < 10; i++)
            {
                var cert = CertificateFactory.CreateCertificate($"CN=Test Cert {i}")
                    .SetIssuer(issuerCertificate)
                    .SetRSAKeySize(keyHashPair.KeySize <= 2048 ? keyHashPair.KeySize : 2048)
                    .CreateForRSA();
                revokedCerts.Add(cert);
                Assert.False(X509Utils.VerifySelfSigned(cert));
            }

            Assert.NotNull(issuerCertificate);
            Assert.NotNull(issuerCertificate.RawData);
            Assert.True(issuerCertificate.HasPrivateKey);
            using (var rsa = issuerCertificate.GetRSAPrivateKey())
            {
                Assert.NotNull(rsa);
            }

            using (var plainCert = new X509Certificate2(issuerCertificate.RawData))
            {
                Assert.NotNull(plainCert);
                VerifyCACert(plainCert, issuerCertificate.Subject, pathLengthConstraint);
            }
            Assert.True(X509Utils.VerifySelfSigned(issuerCertificate));
            X509Utils.VerifyRSAKeyPair(issuerCertificate, issuerCertificate, true);

            var crl = CertificateFactory.RevokeCertificate(issuerCertificate, null, null);
            Assert.NotNull(crl);
            Assert.True(crl.VerifySignature(issuerCertificate, true));
            var extension = X509Extensions.FindExtension<X509CrlNumberExtension>(crl.CrlExtensions);
            var crlCounter = new BigInteger(1);
            Assert.AreEqual(crlCounter, extension.CrlNumber);
            var revokedList = new List<X509CRL>();
            revokedList.Add(crl);
            foreach (var cert in revokedCerts)
            {
                Assert.Throws<CryptographicException>(() => crl.VerifySignature(otherIssuerCertificate, true));
                Assert.False(crl.IsRevoked(cert));
                var nextCrl = CertificateFactory.RevokeCertificate(issuerCertificate, revokedList, new X509Certificate2Collection(cert));
                crlCounter++;
                Assert.NotNull(nextCrl);
                Assert.True(nextCrl.IsRevoked(cert));
                extension = X509Extensions.FindExtension<X509CrlNumberExtension>(nextCrl.CrlExtensions);
                Assert.AreEqual(crlCounter, extension.CrlNumber);
                Assert.True(crl.VerifySignature(issuerCertificate, true));
                revokedList.Add(nextCrl);
                crl = nextCrl;
            }

            foreach (var cert in revokedCerts)
            {
                Assert.True(crl.IsRevoked(cert));
            }
        }
        #endregion

        #region Public Methods
        private X509Certificate2 GetIssuer(KeyHashPair keyHashPair)
        {
            X509Certificate2 issuerCertificate = null;
            try
            {
                if (!m_rootCACertificate.TryGetValue(keyHashPair.KeySize, out issuerCertificate))
                {
                    VerifyCACerts(keyHashPair);
                    if (!m_rootCACertificate.TryGetValue(keyHashPair.KeySize, out issuerCertificate))
                    {
                        Assert.Ignore($"Could not load Issuer Cert.");
                    }
                }
            }
            catch
            {
                Assert.Ignore($"Could not load create Issuer Cert.");
            }
            return issuerCertificate;
        }

        public static void VerifyApplicationCert(ApplicationTestData testApp, X509Certificate2 cert, X509Certificate2 issuerCert = null)
        {
            bool signedCert = issuerCert != null;
            if (issuerCert == null)
            {
                issuerCert = cert;
            }
            TestContext.Out.WriteLine($"{nameof(VerifyApplicationCert)}:");
            Assert.NotNull(cert);
            TestContext.Out.WriteLine(cert);
            Assert.False(cert.HasPrivateKey);
            Assert.True(X509Utils.CompareDistinguishedName(testApp.Subject, cert.Subject));
            Assert.True(X509Utils.CompareDistinguishedName(issuerCert.Subject, cert.Issuer));

            // test basic constraints
            X509BasicConstraintsExtension constraints = X509Extensions.FindExtension<X509BasicConstraintsExtension>(cert);
            Assert.NotNull(constraints);
            TestContext.Out.WriteLine(constraints.Format(true));
            Assert.True(constraints.Critical);
            if (signedCert)
            {
                Assert.False(constraints.CertificateAuthority);
                Assert.False(constraints.HasPathLengthConstraint);
            }
            else
            {
                Assert.True(constraints.CertificateAuthority);
                Assert.True(constraints.HasPathLengthConstraint);
                Assert.AreEqual(0, constraints.PathLengthConstraint);
            }

            // key usage
            X509KeyUsageExtension keyUsage = X509Extensions.FindExtension<X509KeyUsageExtension>(cert);
            Assert.NotNull(keyUsage);
            TestContext.Out.WriteLine(keyUsage.Format(true));
            Assert.True(keyUsage.Critical);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.CrlSign) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.DataEncipherment) == X509KeyUsageFlags.DataEncipherment);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.DecipherOnly) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.DigitalSignature) == X509KeyUsageFlags.DigitalSignature);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.EncipherOnly) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.KeyAgreement) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.KeyCertSign) == (signedCert ? 0 : X509KeyUsageFlags.KeyCertSign));
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.KeyEncipherment) == X509KeyUsageFlags.KeyEncipherment);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.NonRepudiation) == X509KeyUsageFlags.NonRepudiation);

            // enhanced key usage
            X509EnhancedKeyUsageExtension enhancedKeyUsage = X509Extensions.FindExtension<X509EnhancedKeyUsageExtension>(cert);
            Assert.NotNull(enhancedKeyUsage);
            TestContext.Out.WriteLine(enhancedKeyUsage.Format(true));
            Assert.True(enhancedKeyUsage.Critical);

            // test for authority key
            X509AuthorityKeyIdentifierExtension authority = X509Extensions.FindExtension<X509AuthorityKeyIdentifierExtension>(cert);
            Assert.NotNull(authority);
            TestContext.Out.WriteLine(authority.Format(true));
            Assert.NotNull(authority.SerialNumber);
            Assert.NotNull(authority.KeyIdentifier);
            Assert.NotNull(authority.Issuer);
            if (issuerCert == null)
            {
                Assert.AreEqual(cert.SubjectName.RawData, authority.Issuer.RawData);
                Assert.True(X509Utils.CompareDistinguishedName(cert.SubjectName.Name, authority.Issuer.Name), $"{cert.SubjectName.Name} != {authority.Issuer.Name}");
            }
            else
            {
                Assert.AreEqual(issuerCert.SubjectName.RawData, authority.Issuer.RawData);
                Assert.True(X509Utils.CompareDistinguishedName(issuerCert.SubjectName.Name, authority.Issuer.Name), $"{cert.SubjectName.Name} != {authority.Issuer.Name}");
            }

            // verify authority key in signed cert
            X509SubjectKeyIdentifierExtension subjectKeyId = X509Extensions.FindExtension<X509SubjectKeyIdentifierExtension>(cert);
            TestContext.Out.WriteLine(subjectKeyId.Format(true));
            if (signedCert)
            {
                var caCertSubjectKeyId = X509Extensions.FindExtension<X509SubjectKeyIdentifierExtension>(issuerCert);
                Assert.NotNull(caCertSubjectKeyId);
                Assert.AreEqual(caCertSubjectKeyId.SubjectKeyIdentifier, authority.KeyIdentifier);
            }
            else
            {
                Assert.AreEqual(subjectKeyId.SubjectKeyIdentifier, authority.KeyIdentifier);
            }
            Assert.AreEqual(issuerCert.GetSerialNumber(), authority.GetSerialNumber());
            Assert.AreEqual(issuerCert.SerialNumber, authority.SerialNumber);

            X509SubjectAltNameExtension subjectAlternateName = X509Extensions.FindExtension<X509SubjectAltNameExtension>(cert);
            Assert.NotNull(subjectAlternateName);
            TestContext.Out.WriteLine(subjectAlternateName.Format(true));
            Assert.False(subjectAlternateName.Critical);
            var domainNames = X509Utils.GetDomainsFromCertficate(cert);
            foreach (var domainName in testApp.DomainNames)
            {
                Assert.True(domainNames.Contains(domainName, StringComparer.OrdinalIgnoreCase));
            }
            Assert.True(subjectAlternateName.Uris.Count == 1);
            var applicationUri = X509Utils.GetApplicationUriFromCertificate(cert);
            TestContext.Out.WriteLine("ApplicationUri: ");
            TestContext.Out.WriteLine(applicationUri);
            Assert.AreEqual(testApp.ApplicationUri, applicationUri);
        }

        public static void VerifyCACert(X509Certificate2 cert, string subject, int pathLengthConstraint)
        {
            TestContext.Out.WriteLine($"{nameof(VerifyCACert)}:");

            Assert.NotNull(cert);
            TestContext.Out.WriteLine(cert);
            Assert.False(cert.HasPrivateKey);
            Assert.True(X509Utils.CompareDistinguishedName(subject, cert.Subject));
            Assert.True(X509Utils.CompareDistinguishedName(subject, cert.Issuer));

            // test basic constraints
            var constraints = X509Extensions.FindExtension<X509BasicConstraintsExtension>(cert);
            Assert.NotNull(constraints);
            TestContext.Out.WriteLine(constraints.Format(true));
            Assert.True(constraints.Critical);
            Assert.True(constraints.CertificateAuthority);
            if (pathLengthConstraint < 0)
            {
                Assert.False(constraints.HasPathLengthConstraint);
            }
            else
            {
                Assert.True(constraints.HasPathLengthConstraint);
                Assert.AreEqual(pathLengthConstraint, constraints.PathLengthConstraint);
            }

            // key usage
            var keyUsage = X509Extensions.FindExtension<X509KeyUsageExtension>(cert);
            Assert.NotNull(keyUsage);
            TestContext.Out.WriteLine(keyUsage.Format(true));
            Assert.True(keyUsage.Critical);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.CrlSign) == X509KeyUsageFlags.CrlSign);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.DataEncipherment) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.DecipherOnly) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.DigitalSignature) == X509KeyUsageFlags.DigitalSignature);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.EncipherOnly) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.KeyAgreement) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.KeyCertSign) == X509KeyUsageFlags.KeyCertSign);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.KeyEncipherment) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.NonRepudiation) == 0);

            // enhanced key usage
            X509EnhancedKeyUsageExtension enhancedKeyUsage = X509Extensions.FindExtension<X509EnhancedKeyUsageExtension>(cert);
            Assert.Null(enhancedKeyUsage);

            // test for authority key
            X509AuthorityKeyIdentifierExtension authority = X509Extensions.FindExtension<X509AuthorityKeyIdentifierExtension>(cert);
            Assert.NotNull(authority);
            TestContext.Out.WriteLine(authority.Format(true));
            Assert.NotNull(authority.SerialNumber);
            Assert.NotNull(authority.GetSerialNumber());
            Assert.NotNull(authority.KeyIdentifier);
            Assert.NotNull(authority.Issuer);
            Assert.NotNull(authority.ToString());
            Assert.AreEqual(authority.SerialNumber, Utils.ToHexString(authority.GetSerialNumber(), true));

            // verify authority key in signed cert
            X509SubjectKeyIdentifierExtension subjectKeyId = X509Extensions.FindExtension<X509SubjectKeyIdentifierExtension>(cert);
            TestContext.Out.WriteLine(subjectKeyId.Format(true));
            Assert.AreEqual(subjectKeyId.SubjectKeyIdentifier, authority.KeyIdentifier);
            Assert.AreEqual(cert.SerialNumber, authority.SerialNumber);
            Assert.AreEqual(cert.GetSerialNumber(), authority.GetSerialNumber());

            X509SubjectAltNameExtension subjectAlternateName = X509Extensions.FindExtension<X509SubjectAltNameExtension>(cert);
            Assert.Null(subjectAlternateName);
        }
        #endregion

        #region Private Fields
        private ConcurrentDictionary<int, X509Certificate2> m_rootCACertificate;
        #endregion
    }

}
