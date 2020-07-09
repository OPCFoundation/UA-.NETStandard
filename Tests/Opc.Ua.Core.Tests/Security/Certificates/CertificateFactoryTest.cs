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
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;

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
        public class KeyHashPair : IFormattable
        {
            public KeyHashPair(ushort keySize, ushort hashSize)
            {
                KeySize = keySize;
                HashSize = hashSize;
            }

            public ushort KeySize;
            public ushort HashSize;

            public string ToString(string format, IFormatProvider formatProvider)
            {
                return $"{KeySize}-{HashSize}";
            }
        }

        public class KeyHashPairCollection : List<KeyHashPair>
        {
            public KeyHashPairCollection() { }
            public KeyHashPairCollection(IEnumerable<KeyHashPair> collection) : base(collection) { }
            public KeyHashPairCollection(int capacity) : base(capacity) { }
            public static KeyHashPairCollection ToJsonValidationDataCollection(KeyHashPair[] values)
            {
                return values != null ? new KeyHashPairCollection(values) : new KeyHashPairCollection();
            }

            public void Add(ushort keySize, ushort hashSize)
            {
                Add(new KeyHashPair(keySize, hashSize));
            }
        }

        [DatapointSource]
        public KeyHashPair[] KeyHashPairs = new KeyHashPairCollection { { 1024, 160 }, { 2048, 256 }, { 3072, 384 }, { 4096, 512 } }.ToArray();
        #endregion

        #region Test Setup
        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
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
            var cert = CertificateFactory.CreateCertificate(null, null, null,
                app.ApplicationUri, app.ApplicationName, app.Subject,
                app.DomainNames, keyHashPair.KeySize, DateTime.UtcNow,
                CertificateFactory.DefaultLifeTime, keyHashPair.HashSize);
            Assert.NotNull(cert);
            Assert.NotNull(cert.RawData);
            Assert.True(cert.HasPrivateKey);
            var plainCert = new X509Certificate2(cert.RawData);
            Assert.NotNull(plainCert);
            VerifySelfSignedApplicationCert(app, plainCert);
            CertificateFactory.VerifySelfSigned(cert);
            CertificateFactory.VerifyRSAKeyPair(cert, cert);
        }

        /// <summary>
        /// Verify CA signed app certs.
        /// </summary>
        [Theory]
        public void VerifyCACerts(
            KeyHashPair keyHashPair
            )
        {
            var subject = "CN=CA Test Cert";
            int pathLengthConstraint = (keyHashPair.KeySize / 512) - -1;
            var cert = CertificateFactory.CreateCertificate(
                null, null, null,
                null, null, subject,
                null, keyHashPair.KeySize,
                DateTime.UtcNow, 25 * 12,
                keyHashPair.HashSize,
                isCA: true,
                pathLengthConstraint: pathLengthConstraint);

            Assert.NotNull(cert);
            Assert.NotNull(cert.RawData);
            Assert.True(cert.HasPrivateKey);
            var plainCert = new X509Certificate2(cert.RawData);
            Assert.NotNull(plainCert);
            VerifyCACert(plainCert, subject, pathLengthConstraint);
            CertificateFactory.VerifySelfSigned(cert);
            CertificateFactory.VerifyRSAKeyPair(cert, cert);
        }
        #endregion

        #region Private Methods
        public static void VerifySelfSignedApplicationCert(ApplicationTestData testApp, X509Certificate2 cert)
        {
            Assert.NotNull(cert);
            Assert.False(cert.HasPrivateKey);
            Assert.True(Utils.CompareDistinguishedName(testApp.Subject, cert.Subject));
            Assert.True(Utils.CompareDistinguishedName(testApp.Subject, cert.Issuer));

            // test basic constraints
            var constraints = FindExtension<X509BasicConstraintsExtension>(cert);
            Assert.NotNull(constraints);
            Assert.True(constraints.Critical);
            Assert.True(constraints.CertificateAuthority);
            Assert.True(constraints.HasPathLengthConstraint);
            Assert.AreEqual(0, constraints.PathLengthConstraint);

            // key usage
            var keyUsage = FindExtension<X509KeyUsageExtension>(cert);
            Assert.NotNull(keyUsage);
            Assert.True(keyUsage.Critical);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.CrlSign) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.DataEncipherment) == X509KeyUsageFlags.DataEncipherment);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.DecipherOnly) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.DigitalSignature) == X509KeyUsageFlags.DigitalSignature);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.EncipherOnly) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.KeyAgreement) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.KeyCertSign) == X509KeyUsageFlags.KeyCertSign);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.KeyEncipherment) == X509KeyUsageFlags.KeyEncipherment);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.NonRepudiation) == X509KeyUsageFlags.NonRepudiation);

            // enhanced key usage
            X509EnhancedKeyUsageExtension enhancedKeyUsage = FindExtension<X509EnhancedKeyUsageExtension>(cert);
            Assert.NotNull(enhancedKeyUsage);
            Assert.True(enhancedKeyUsage.Critical);

            // test for authority key
            X509AuthorityKeyIdentifierExtension authority = FindAuthorityKeyIdentifier(cert);
            Assert.NotNull(authority);
            Assert.NotNull(authority.SerialNumber);
            Assert.NotNull(authority.KeyId);
            Assert.NotNull(authority.AuthorityNames);

            // verify authority key in signed cert
            X509SubjectKeyIdentifierExtension subjectKeyId = FindExtension<X509SubjectKeyIdentifierExtension>(cert);
            Assert.AreEqual(subjectKeyId.SubjectKeyIdentifier, authority.KeyId);
            Assert.AreEqual(cert.SerialNumber, authority.SerialNumber);

            X509SubjectAltNameExtension subjectAlternateName = FindSubjectAltName(cert);
            Assert.NotNull(subjectAlternateName);
            Assert.False(subjectAlternateName.Critical);
            var domainNames = Utils.GetDomainsFromCertficate(cert);
            foreach (var domainName in testApp.DomainNames)
            {
                Assert.True(domainNames.Contains(domainName, StringComparer.OrdinalIgnoreCase));
            }
            Assert.True(subjectAlternateName.Uris.Count == 1);
            var applicationUri = Utils.GetApplicationUriFromCertificate(cert);
            Assert.AreEqual(testApp.ApplicationUri, applicationUri);
        }

        public static void VerifyCACert(X509Certificate2 cert, string subject, int pathLengthConstraint)
        {
            Assert.NotNull(cert);
            Assert.False(cert.HasPrivateKey);
            Assert.True(Utils.CompareDistinguishedName(subject, cert.Subject));
            Assert.True(Utils.CompareDistinguishedName(subject, cert.Issuer));

            // test basic constraints
            var constraints = FindExtension<X509BasicConstraintsExtension>(cert);
            Assert.NotNull(constraints);
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
            var keyUsage = FindExtension<X509KeyUsageExtension>(cert);
            Assert.NotNull(keyUsage);
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
            X509EnhancedKeyUsageExtension enhancedKeyUsage = FindExtension<X509EnhancedKeyUsageExtension>(cert);
            Assert.Null(enhancedKeyUsage);

            // test for authority key
            X509AuthorityKeyIdentifierExtension authority = FindAuthorityKeyIdentifier(cert);
            Assert.NotNull(authority);
            Assert.NotNull(authority.SerialNumber);
            Assert.NotNull(authority.KeyId);
            Assert.NotNull(authority.AuthorityNames);

            // verify authority key in signed cert
            X509SubjectKeyIdentifierExtension subjectKeyId = FindExtension<X509SubjectKeyIdentifierExtension>(cert);
            Assert.AreEqual(subjectKeyId.SubjectKeyIdentifier, authority.KeyId);
            Assert.AreEqual(cert.SerialNumber, authority.SerialNumber);

            X509SubjectAltNameExtension subjectAlternateName = FindSubjectAltName(cert);
            Assert.Null(subjectAlternateName);
        }

        private static T FindExtension<T>(X509Certificate2 certificate) where T : class
        {
            for (int ii = 0; ii < certificate.Extensions.Count; ii++)
            {
                T extension = certificate.Extensions[ii] as T;
                if (extension != null)
                {
                    return extension;
                }
            }
            return null;
        }

        private static X509AuthorityKeyIdentifierExtension FindAuthorityKeyIdentifier(X509Certificate2 certificate)
        {
            for (int ii = 0; ii < certificate.Extensions.Count; ii++)
            {
                X509Extension extension = certificate.Extensions[ii];

                switch (extension.Oid.Value)
                {
                    case X509AuthorityKeyIdentifierExtension.AuthorityKeyIdentifierOid:
                    case X509AuthorityKeyIdentifierExtension.AuthorityKeyIdentifier2Oid:
                    {
                        return new X509AuthorityKeyIdentifierExtension(extension, extension.Critical);
                    }
                }
            }

            return null;
        }

        private static X509SubjectAltNameExtension FindSubjectAltName(X509Certificate2 certificate)
        {
            foreach (var extension in certificate.Extensions)
            {
                if (extension.Oid.Value == X509SubjectAltNameExtension.SubjectAltNameOid ||
                    extension.Oid.Value == X509SubjectAltNameExtension.SubjectAltName2Oid)
                {
                    return new X509SubjectAltNameExtension(extension, extension.Critical);
                }
            }
            return null;
        }
        #endregion

        #region Private Fields
        #endregion
    }

}
