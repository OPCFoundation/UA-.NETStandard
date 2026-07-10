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

// CS0618: obsolete single-URI helper is tested to preserve backwards compatibility.
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    [TestFixture]
    [Category("Certificate")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class X509UtilsAdditionalTests
    {
        [Test]
        public void DomainsIncludeSubjectDcAlternateNamesAndIpAddresses()
        {
            using Certificate cert = DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    "urn:test:app",
                    "TestApp",
                    "CN=TestApp, DC=example, DC=com",
                    new List<string> { "example.com", "alternate.example.com", "127.0.0.1" })
                .SetRSAKeySize(2048)
                .CreateForRSA();

            ArrayOf<string> domains = X509Utils.GetDomainsFromCertificate(cert);

            Assert.That(domains.Contains("EXAMPLE.COM"), Is.True);
            Assert.That(domains.Contains("ALTERNATE.EXAMPLE.COM"), Is.True);
            Assert.That(domains.Contains("127.0.0.1"), Is.True);
            Assert.That(X509Utils.DoesUrlMatchCertificate(cert, new Uri("opc.tcp://alternate.example.com:4840")), Is.True);
            Assert.That(X509Utils.DoesUrlMatchCertificate(null, new Uri("opc.tcp://alternate.example.com:4840")), Is.False);
            Assert.That(X509Utils.DoesUrlMatchCertificate(cert, null), Is.False);
        }

        [Test]
        public void ApplicationUriHelpersHandleMissingMatchingAndUrnValues()
        {
            using Certificate plainCert = CertificateBuilder
                .Create("CN=NoApplicationUri")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            Assert.That(X509Utils.GetApplicationUriFromCertificate(plainCert), Is.EqualTo(string.Empty));
            Assert.That(X509Utils.GetApplicationUrisFromCertificate(plainCert), Is.Empty);
            Assert.That(X509Utils.CompareApplicationUriWithCertificate(plainCert, string.Empty), Is.False);
            Assert.That(X509Utils.HasApplicationURN(plainCert), Is.False);

            using Certificate appCert = DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    "urn:test:application",
                    "ApplicationUriApp",
                    "CN=ApplicationUriApp",
                    new List<string> { "localhost" })
                .SetRSAKeySize(2048)
                .CreateForRSA();

            Assert.That(X509Utils.GetApplicationUriFromCertificate(appCert), Is.EqualTo("urn:test:application"));
            Assert.That(
                X509Utils.CompareApplicationUriWithCertificate(
                    appCert,
                    "urn:test:application",
                    out IReadOnlyList<string> applicationUris),
                Is.True);
            Assert.That(applicationUris, Does.Contain("urn:test:application"));
            Assert.That(X509Utils.CompareApplicationUriWithCertificate(appCert, "urn:other"), Is.False);
            Assert.That(X509Utils.HasApplicationURN(appCert), Is.True);
        }

        [Test]
        public void CertificateCapabilityHelpersCoverRsaEcdsaAndCaBranches()
        {
            using Certificate rsaCert = CertificateBuilder
                .Create("CN=Rsa")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            Assert.That(X509Utils.GetRSAPublicKeySize(rsaCert), Is.EqualTo(2048));
            Assert.That(X509Utils.GetPublicKeySize(rsaCert), Is.EqualTo(2048));
            Assert.That(X509Utils.IsIssuerAllowed(rsaCert), Is.False);
            Assert.That(X509Utils.IsCertificateAuthority(rsaCert), Is.False);

            using Certificate caCert = CertificateBuilder
                .Create("CN=CA")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            Assert.That(X509Utils.IsIssuerAllowed(caCert), Is.True);
            Assert.That(X509Utils.IsCertificateAuthority(caCert), Is.True);
            using Certificate copy = X509Utils.CreateCopyWithPrivateKey(rsaCert, persisted: false);
            Assert.That(copy, Is.Not.Null);

            using Certificate ecdsaCert = CertificateBuilder
                .Create("CN=Ecdsa")
                .SetECCurve(ECCurve.NamedCurves.nistP256)
                .CreateForECDsa();
            Assert.That(X509Utils.GetRSAPublicKeySize(ecdsaCert), Is.EqualTo(-1));
            Assert.That(X509Utils.GetPublicKeySize(ecdsaCert), Is.GreaterThan(0));
        }

        [Test]
        public void DistinguishedNameParserHandlesQuotedDelimitersAndComparisons()
        {
            List<string> fields = X509Utils.ParseDistinguishedName("CN=\"A/B,C=Q\", O=OPC Foundation, DC=LOCALHOST");

            Assert.That(fields, Does.Contain("CN=\"A/B,C=Q\""));
            Assert.That(fields, Does.Contain("O=OPC Foundation"));
            Assert.That(fields, Does.Contain("DC=LOCALHOST"));
            Assert.That(X509Utils.ParseDistinguishedName(string.Empty), Is.Empty);
            Assert.That(X509Utils.CompareDistinguishedName("CN=Same", "CN=Same"), Is.True);
            Assert.That(X509Utils.CompareDistinguishedName("CN=One", "CN=One,O=Org"), Is.False);
            Assert.That(X509Utils.CompareDistinguishedName("S=Arizona,CN=Name", "ST=Arizona,CN=Name"), Is.True);
            Assert.That(X509Utils.CompareDistinguishedName("DC=LOCALHOST,CN=Name", "DC=localhost,CN=Name"), Is.True);
            Assert.That(X509Utils.CompareDistinguishedName("S=Arizona,CN=Name", "S=Utah,CN=Name"), Is.False);
        }

        [Test]
        public void HashAlgorithmAndPasscodeHelpersReturnExpectedValues()
        {
            Assert.That(X509Utils.GetRSAHashAlgorithmName(160), Is.EqualTo(HashAlgorithmName.SHA1));
            Assert.That(X509Utils.GetRSAHashAlgorithmName(256), Is.EqualTo(HashAlgorithmName.SHA256));
            Assert.That(X509Utils.GetRSAHashAlgorithmName(384), Is.EqualTo(HashAlgorithmName.SHA384));
            Assert.That(X509Utils.GetRSAHashAlgorithmName(521), Is.EqualTo(HashAlgorithmName.SHA512));

            char[] passcode = X509Utils.GeneratePasscode();
            try
            {
                Assert.That(passcode, Is.Not.Empty);
                Assert.That(passcode, Has.None.EqualTo('\0'));
            }
            finally
            {
                Array.Clear(passcode, 0, passcode.Length);
            }
        }
    }
}
