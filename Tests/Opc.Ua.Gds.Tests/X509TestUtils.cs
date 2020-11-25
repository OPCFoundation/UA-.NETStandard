/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Tests
{

    public static class X509TestUtils
    {
        public static void VerifyApplicationCertIntegrity(byte[] certificate, byte[] privateKey, string privateKeyPassword, string privateKeyFormat, byte[][] issuerCertificates)
        {
            X509Certificate2 newCert = new X509Certificate2(certificate);
            Assert.IsNotNull(newCert);
            X509Certificate2 newPrivateKeyCert = null;
            if (privateKeyFormat == "PFX")
            {
                newPrivateKeyCert = X509Utils.CreateCertificateFromPKCS12(privateKey, privateKeyPassword);
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
            Assert.IsTrue(X509Utils.VerifyRSAKeyPair(newCert, newPrivateKeyCert, true));
            Assert.IsTrue(X509Utils.VerifyRSAKeyPair(newPrivateKeyCert, newPrivateKeyCert, true));
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
            Assert.That(() => {
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

            TestContext.Out.WriteLine($"Signed cert: {signedCert}");
            TestContext.Out.WriteLine($"Issuer cert: {issuerCert}");

            Assert.NotNull(signedCert);
            Assert.False(signedCert.HasPrivateKey);
            Assert.True(X509Utils.CompareDistinguishedName(testApp.Subject, signedCert.Subject));
            Assert.False(X509Utils.CompareDistinguishedName(signedCert.Issuer, signedCert.Subject));
            Assert.True(X509Utils.CompareDistinguishedName(signedCert.Issuer, issuerCert.Subject));
            TestContext.Out.WriteLine($"Signed Subject: {signedCert.Subject}");
            TestContext.Out.WriteLine($"Issuer Subject: {issuerCert.Subject}");

            // test basic constraints
            X509BasicConstraintsExtension constraints = X509Extensions.FindExtension<X509BasicConstraintsExtension>(signedCert);
            Assert.NotNull(constraints);
            TestContext.Out.WriteLine($"Constraints: {constraints.Format(true)}");
            Assert.True(constraints.Critical);
            Assert.False(constraints.CertificateAuthority);
            Assert.False(constraints.HasPathLengthConstraint);

            // key usage
            X509KeyUsageExtension keyUsage = X509Extensions.FindExtension<X509KeyUsageExtension>(signedCert);
            Assert.NotNull(keyUsage);
            TestContext.Out.WriteLine($"KeyUsage: {keyUsage.Format(true)}");
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
            X509EnhancedKeyUsageExtension enhancedKeyUsage = X509Extensions.FindExtension<X509EnhancedKeyUsageExtension>(signedCert);
            Assert.NotNull(enhancedKeyUsage);
            TestContext.Out.WriteLine($"Enhanced Key Usage: {enhancedKeyUsage.Format(true)}");
            Assert.True(enhancedKeyUsage.Critical);

            // test for authority key
            X509AuthorityKeyIdentifierExtension authority = X509Extensions.FindExtension<X509AuthorityKeyIdentifierExtension>(signedCert);
            Assert.NotNull(authority);
            TestContext.Out.WriteLine($"Authority Key Identifier: {authority.Format(true)}");
            Assert.NotNull(authority.SerialNumber);
            Assert.NotNull(authority.KeyIdentifier);
            Assert.NotNull(authority.Issuer);
            Assert.AreEqual(issuerCert.SubjectName.RawData, authority.Issuer.RawData);
            Assert.AreEqual(issuerCert.SubjectName.RawData, authority.Issuer.RawData);

            // verify authority key in signed cert
            X509SubjectKeyIdentifierExtension subjectKeyId = X509Extensions.FindExtension<X509SubjectKeyIdentifierExtension>(issuerCert);
            TestContext.Out.WriteLine($"Issuer Subject Key Identifier: {subjectKeyId}");
            Assert.AreEqual(subjectKeyId.SubjectKeyIdentifier, authority.KeyIdentifier);
            Assert.AreEqual(issuerCert.SerialNumber, authority.SerialNumber);

            X509SubjectAltNameExtension subjectAlternateName = X509Extensions.FindExtension<X509SubjectAltNameExtension>(signedCert);
            Assert.NotNull(subjectAlternateName);
            TestContext.Out.WriteLine($"Issuer Subject Alternate Name: {subjectAlternateName}");
            Assert.False(subjectAlternateName.Critical);
            var domainNames = X509Utils.GetDomainsFromCertficate(signedCert);
            foreach (var domainName in testApp.DomainNames)
            {
                Assert.True(domainNames.Contains(domainName, StringComparer.OrdinalIgnoreCase));
            }
            Assert.True(subjectAlternateName.Uris.Count == 1);
            var applicationUri = X509Utils.GetApplicationUriFromCertificate(signedCert);
            Assert.True(testApp.ApplicationRecord.ApplicationUri == applicationUri);
        }
    }
}
