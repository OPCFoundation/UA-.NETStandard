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
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using X509AuthorityKeyIdentifierExtension = Opc.Ua.Security.Certificates.X509AuthorityKeyIdentifierExtension;

namespace Opc.Ua.Gds.Tests
{
    public static class X509TestUtils
    {
        public static async Task VerifyApplicationCertIntegrityAsync(
            byte[] certificate,
            byte[] privateKey,
            char[] privateKeyPassword,
            string privateKeyFormat,
            byte[][] issuerCertificates,
            ITelemetryContext telemetry)
        {
            X509Certificate2 newCert = CertificateFactory.Create(certificate);
            Assert.That(newCert, Is.Not.Null);
            X509Certificate2 newPrivateKeyCert = null;
            if (privateKeyFormat == "PFX")
            {
                newPrivateKeyCert = X509Utils.CreateCertificateFromPKCS12(
                    privateKey,
                    privateKeyPassword);
            }
            else if (privateKeyFormat == "PEM")
            {
                newPrivateKeyCert = CertificateFactory.CreateCertificateWithPEMPrivateKey(
                    newCert,
                    privateKey,
                    privateKeyPassword);
            }
            else
            {
                Assert.Fail("Invalid private key format");
            }
            Assert.That(newPrivateKeyCert, Is.Not.Null);
            // verify the public cert matches the private key
            Assert.That(X509Utils.VerifyKeyPair(newCert, newPrivateKeyCert, true), Is.True);
            Assert.That(X509Utils.VerifyKeyPair(newPrivateKeyCert, newPrivateKeyCert, true), Is.True);
            var issuerCertIdCollection = new CertificateIdentifierCollection();
            foreach (byte[] issuer in issuerCertificates)
            {
                X509Certificate2 issuerCert = CertificateFactory.Create(issuer);
                Assert.That(issuerCert, Is.Not.Null);
                issuerCertIdCollection.Add(new CertificateIdentifier(issuerCert));
            }

            // verify cert with issuer chain
            var certValidator = new CertificateValidator(telemetry);
            var issuerStore = new CertificateTrustList();
            var trustedStore = new CertificateTrustList
            {
                TrustedCertificates = issuerCertIdCollection
            };
            certValidator.Update(trustedStore, issuerStore, null);
            Assert.That(async () => await certValidator.ValidateAsync(newCert, CancellationToken.None).ConfigureAwait(false), Throws.Exception);
            issuerStore.TrustedCertificates = issuerCertIdCollection;
            certValidator.Update(issuerStore, trustedStore, null);
            await certValidator.ValidateAsync(newCert, CancellationToken.None).ConfigureAwait(false);
        }

        public static void VerifySignedApplicationCert(
            ApplicationTestData testApp,
            byte[] rawSignedCert,
            byte[][] rawIssuerCerts)
        {
            X509Certificate2 signedCert = CertificateFactory.Create(rawSignedCert);
            X509Certificate2 issuerCert = CertificateFactory.Create(rawIssuerCerts[0]);

            TestContext.Out.WriteLine($"Signed cert: {signedCert}");
            TestContext.Out.WriteLine($"Issuer cert: {issuerCert}");

            Assert.That(signedCert, Is.Not.Null);
            Assert.That(signedCert.HasPrivateKey, Is.False);
            Assert.That(X509Utils.CompareDistinguishedName(testApp.Subject, signedCert.Subject), Is.True);
            Assert.That(X509Utils.CompareDistinguishedName(signedCert.Issuer, signedCert.Subject), Is.False);
            Assert.That(
                X509Utils.CompareDistinguishedName(signedCert.IssuerName, signedCert.SubjectName),
                Is.False);
            Assert.That(X509Utils.CompareDistinguishedName(signedCert.Issuer, issuerCert.Subject), Is.True);
            Assert.That(
                X509Utils.CompareDistinguishedName(signedCert.IssuerName, issuerCert.SubjectName),
                Is.True);
            TestContext.Out.WriteLine($"Signed Subject: {signedCert.Subject}");
            TestContext.Out.WriteLine($"Issuer Subject: {issuerCert.Subject}");

            // test basic constraints
            X509BasicConstraintsExtension constraints = signedCert
                .FindExtension<X509BasicConstraintsExtension>();
            Assert.That(constraints, Is.Not.Null);
            TestContext.Out.WriteLine($"Constraints: {constraints.Format(true)}");
            Assert.That(constraints.Critical, Is.True);
            Assert.That(constraints.CertificateAuthority, Is.False);
            Assert.That(constraints.HasPathLengthConstraint, Is.False);

            // key usage
            X509KeyUsageExtension keyUsage = signedCert.FindExtension<X509KeyUsageExtension>();
            Assert.That(keyUsage, Is.Not.Null);
            TestContext.Out.WriteLine($"KeyUsage: {keyUsage.Format(true)}");
            Assert.That(keyUsage.Critical, Is.True);
            Assert.That((keyUsage.KeyUsages & X509KeyUsageFlags.CrlSign) == 0, Is.True);
            Assert.That((keyUsage.KeyUsages & X509KeyUsageFlags.DecipherOnly) == 0, Is.True);
            Assert.That(
                (keyUsage.KeyUsages &
                    X509KeyUsageFlags.DigitalSignature) == X509KeyUsageFlags.DigitalSignature,
                Is.True);
            Assert.That((keyUsage.KeyUsages & X509KeyUsageFlags.EncipherOnly) == 0, Is.True);
            Assert.That((keyUsage.KeyUsages & X509KeyUsageFlags.KeyCertSign) == 0, Is.True);
            Assert.That((keyUsage.KeyUsages &
                X509KeyUsageFlags.NonRepudiation) == X509KeyUsageFlags.NonRepudiation, Is.True);

            //ECC
            if (X509PfxUtils.IsECDsaSignature(signedCert))
            {
                Assert.That((keyUsage.KeyUsages & X509KeyUsageFlags.DataEncipherment) == 0, Is.True);
                Assert.That((keyUsage.KeyUsages & X509KeyUsageFlags.KeyEncipherment) == 0, Is.True);
                Assert.That((keyUsage.KeyUsages &
                    X509KeyUsageFlags.KeyAgreement) == X509KeyUsageFlags.KeyAgreement, Is.True);
            }
            //RSA
            else
            {
                Assert.That(
                    (keyUsage.KeyUsages &
                        X509KeyUsageFlags.DataEncipherment) == X509KeyUsageFlags.DataEncipherment,
                    Is.True);
                Assert.That(
                    (keyUsage.KeyUsages &
                        X509KeyUsageFlags.KeyEncipherment) == X509KeyUsageFlags.KeyEncipherment,
                    Is.True);
                Assert.That((keyUsage.KeyUsages & X509KeyUsageFlags.KeyAgreement) == 0, Is.True);

                // enhanced key usage
                X509EnhancedKeyUsageExtension enhancedKeyUsage =
                    signedCert.FindExtension<X509EnhancedKeyUsageExtension>();
                Assert.That(enhancedKeyUsage, Is.Not.Null);
                TestContext.Out.WriteLine($"Enhanced Key Usage: {enhancedKeyUsage.Format(true)}");
                Assert.That(enhancedKeyUsage.Critical, Is.True);
            }

            // test for authority key

            X509AuthorityKeyIdentifierExtension authority =
                signedCert.FindExtension<X509AuthorityKeyIdentifierExtension>();
            Assert.That(authority, Is.Not.Null);
            TestContext.Out.WriteLine($"Authority Key Identifier: {authority.Format(true)}");
            Assert.That(authority.SerialNumber, Is.Not.Null);
            Assert.That(authority.KeyIdentifier, Is.Not.Null);
            Assert.That(authority.Issuer, Is.Not.Null);
            Assert.That(authority.Issuer.RawData, Is.EqualTo(issuerCert.SubjectName.RawData));
            Assert.That(authority.Issuer.RawData, Is.EqualTo(issuerCert.SubjectName.RawData));

            // verify authority key in signed cert
            X509SubjectKeyIdentifierExtension subjectKeyId =
                issuerCert.FindExtension<X509SubjectKeyIdentifierExtension>();
            TestContext.Out.WriteLine($"Issuer Subject Key Identifier: {subjectKeyId.SubjectKeyIdentifier}");
            Assert.That(authority.KeyIdentifier, Is.EqualTo(subjectKeyId.SubjectKeyIdentifier));
            Assert.That(authority.SerialNumber, Is.EqualTo(issuerCert.SerialNumber));

            X509SubjectAltNameExtension subjectAlternateName = signedCert
                .FindExtension<X509SubjectAltNameExtension>();
            Assert.That(subjectAlternateName, Is.Not.Null);
            TestContext.Out.WriteLine($"Issuer Subject Alternate Name: {subjectAlternateName.Oid.FriendlyName}");
            Assert.That(subjectAlternateName.Critical, Is.False);
            ArrayOf<string> domainNames = X509Utils.GetDomainsFromCertificate(signedCert);
            foreach (string domainName in testApp.DomainNames)
            {
                Assert.That(domainNames.Contains(domainName, StringComparer.OrdinalIgnoreCase), Is.True);
            }
            Assert.That(subjectAlternateName.Uris.Count == 1, Is.True);
            IReadOnlyList<string> applicationUris = X509Utils.GetApplicationUrisFromCertificate(signedCert);
            string applicationUri = applicationUris.Count > 0 ? applicationUris[0] : null;
            Assert.That(testApp.ApplicationRecord.ApplicationUri == applicationUri, Is.True);
        }
    }
}
