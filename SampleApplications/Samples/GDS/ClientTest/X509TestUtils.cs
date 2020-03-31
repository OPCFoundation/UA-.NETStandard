/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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


namespace Opc.Ua.Gds.Test
{

    public class X509TestUtils
    {
        public static void VerifyApplicationCertIntegrity(byte[] certificate, byte[] privateKey, string privateKeyPassword, string privateKeyFormat, byte[][] issuerCertificates)
        {
            X509Certificate2 newCert = new X509Certificate2(certificate);
            Assert.IsNotNull(newCert);
            X509Certificate2 newPrivateKeyCert = null;
            if (privateKeyFormat == "PFX")
            {
                newPrivateKeyCert = CertificateFactory.CreateCertificateFromPKCS12(privateKey, privateKeyPassword);
            }
            else if (privateKeyFormat == "PEM")
            {
                newPrivateKeyCert = CertificateFactory.CreateCertificateWithPEMPrivateKey(newCert, privateKey, privateKeyPassword);
            }
            else
            {
                Assert.Fail("Invalid private key format");
            }
            Assert.IsNotNull(newPrivateKeyCert);
            // verify the public cert matches the private key
            Assert.IsTrue(CertificateFactory.VerifyRSAKeyPair(newCert, newPrivateKeyCert, true));
            Assert.IsTrue(CertificateFactory.VerifyRSAKeyPair(newPrivateKeyCert, newPrivateKeyCert, true));
            CertificateIdentifierCollection issuerCertIdCollection = new CertificateIdentifierCollection();
            foreach (var issuer in issuerCertificates)
            {
                var issuerCert = new X509Certificate2(issuer);
                Assert.IsNotNull(issuerCert);
                issuerCertIdCollection.Add(new CertificateIdentifier(issuerCert));
            }

            // verify cert with issuer chain
            CertificateValidator certValidator = new CertificateValidator();
            CertificateTrustList issuerStore = new CertificateTrustList();
            CertificateTrustList trustedStore = new CertificateTrustList();
            trustedStore.TrustedCertificates = issuerCertIdCollection;
            certValidator.Update(trustedStore, issuerStore, null);
            Assert.That(() =>
            {
                certValidator.Validate(newCert);
            }, Throws.Exception);
            issuerStore.TrustedCertificates = issuerCertIdCollection;
            certValidator.Update(issuerStore, trustedStore, null);
            certValidator.Validate(newCert);
        }

        public static void VerifySignedApplicationCert(ApplicationTestData testApp, byte[] rawSignedCert, byte[][] rawIssuerCerts)
        {
            X509Certificate2 signedCert = new X509Certificate2(rawSignedCert);
            X509Certificate2 issuerCert = new X509Certificate2(rawIssuerCerts[0]);

            Assert.NotNull(signedCert);
            Assert.False(signedCert.HasPrivateKey);
            Assert.True(Utils.CompareDistinguishedName(testApp.Subject, signedCert.Subject));
            Assert.False(Utils.CompareDistinguishedName(signedCert.Issuer, signedCert.Subject));
            Assert.True(Utils.CompareDistinguishedName(signedCert.Issuer, issuerCert.Subject));

            // test basic constraints
            var constraints = FindBasicConstraintsExtension(signedCert);
            Assert.NotNull(constraints);
            Assert.True(constraints.Critical);
            Assert.False(constraints.CertificateAuthority);
            Assert.False(constraints.HasPathLengthConstraint);

            // key usage
            var keyUsage = FindKeyUsageExtension(signedCert);
            Assert.NotNull(keyUsage);
            Assert.True(keyUsage.Critical);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.CrlSign) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.DataEncipherment) == X509KeyUsageFlags.DataEncipherment);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.DecipherOnly) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.DigitalSignature) == X509KeyUsageFlags.DigitalSignature);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.EncipherOnly) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.KeyAgreement) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.KeyCertSign) == 0);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.KeyEncipherment) == X509KeyUsageFlags.KeyEncipherment);
            Assert.True((keyUsage.KeyUsages & X509KeyUsageFlags.NonRepudiation) == X509KeyUsageFlags.NonRepudiation);

            // enhanced key usage
            var enhancedKeyUsage = FindEnhancedKeyUsageExtension(signedCert);
            Assert.NotNull(enhancedKeyUsage);
            Assert.True(enhancedKeyUsage.Critical);

            // test for authority key
            X509AuthorityKeyIdentifierExtension authority = FindAuthorityKeyIdentifier(signedCert);
            Assert.NotNull(authority);
            Assert.NotNull(authority.SerialNumber);
            Assert.NotNull(authority.KeyId);
            Assert.NotNull(authority.AuthorityNames);

            // verify authority key in signed cert
            X509SubjectKeyIdentifierExtension subjectKeyId = FindSubjectKeyIdentifierExtension(issuerCert);
            Assert.AreEqual(subjectKeyId.SubjectKeyIdentifier, authority.KeyId);
            Assert.AreEqual(issuerCert.SerialNumber, authority.SerialNumber);

            X509SubjectAltNameExtension subjectAlternateName = FindSubjectAltName(signedCert);
            Assert.NotNull(subjectAlternateName);
            Assert.False(subjectAlternateName.Critical);
            var domainNames = Utils.GetDomainsFromCertficate(signedCert);
            foreach (var domainName in testApp.DomainNames)
            {
                Assert.True(domainNames.Contains(domainName, StringComparer.OrdinalIgnoreCase));
            }
            Assert.True(subjectAlternateName.Uris.Count == 1);
            var applicationUri = Utils.GetApplicationUriFromCertificate(signedCert);
            Assert.True(testApp.ApplicationRecord.ApplicationUri == applicationUri);
        }

        private static X509BasicConstraintsExtension FindBasicConstraintsExtension(X509Certificate2 certificate)
        {
            for (int ii = 0; ii < certificate.Extensions.Count; ii++)
            {
                X509BasicConstraintsExtension extension = certificate.Extensions[ii] as X509BasicConstraintsExtension;
                if (extension != null)
                {
                    return extension;
                }
            }
            return null;
        }

        private static X509KeyUsageExtension FindKeyUsageExtension(X509Certificate2 certificate)
        {
            for (int ii = 0; ii < certificate.Extensions.Count; ii++)
            {
                X509KeyUsageExtension extension = certificate.Extensions[ii] as X509KeyUsageExtension;
                if (extension != null)
                {
                    return extension;
                }
            }
            return null;
        }
        private static X509EnhancedKeyUsageExtension FindEnhancedKeyUsageExtension(X509Certificate2 certificate)
        {
            for (int ii = 0; ii < certificate.Extensions.Count; ii++)
            {
                X509EnhancedKeyUsageExtension extension = certificate.Extensions[ii] as X509EnhancedKeyUsageExtension;
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

        private static X509SubjectKeyIdentifierExtension FindSubjectKeyIdentifierExtension(X509Certificate2 certificate)
        {
            for (int ii = 0; ii < certificate.Extensions.Count; ii++)
            {
                X509SubjectKeyIdentifierExtension extension = certificate.Extensions[ii] as X509SubjectKeyIdentifierExtension;
                if (extension != null)
                {
                    return extension;
                }
            }
            return null;
        }


    }

}
