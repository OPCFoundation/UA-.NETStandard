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

using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Tests for the CertificateFactory class.
    /// </summary>
    [TestFixture]
    [Category("X509Extensions")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class ExtensionTests
    {
        [DatapointSource]
        public CertificateAsset[] CertificateTestCases =
        [
            .. AssetCollection<CertificateAsset>.CreateFromFiles(
                TestUtils.EnumerateTestAssets("*.?er"))
        ];

        [Theory]
        public void DecodeExtensions(CertificateAsset certAsset)
        {
            using Certificate x509Cert = CertificateFactory.Create(certAsset.Cert);
            Assert.That(x509Cert, Is.Not.Null);
            TestContext.Out.WriteLine("CertificateAsset:");
            TestContext.Out.WriteLine(x509Cert);
            X509SubjectAltNameExtension altName = x509Cert
                .FindExtension<X509SubjectAltNameExtension>();
            if (altName != null)
            {
                TestContext.Out.WriteLine("X509SubjectAltNameExtension:");
                TestContext.Out.WriteLine(altName.Format(true));
                var ext = new X509Extension(altName.Oid, altName.RawData, altName.Critical);
                TestContext.Out.WriteLine(ext.Format(true));
            }
            X509AuthorityKeyIdentifierExtension authority =
                x509Cert.FindExtension<X509AuthorityKeyIdentifierExtension>();
            if (authority != null)
            {
                TestContext.Out.WriteLine("X509AuthorityKeyIdentifierExtension:");
                TestContext.Out.WriteLine(authority.Format(true));
                var ext = new X509Extension(authority.Oid, authority.RawData, authority.Critical);
                TestContext.Out.WriteLine(ext.Format(true));
            }
            TestContext.Out.WriteLine("All extensions:");
            foreach (X509Extension extension in x509Cert.Extensions)
            {
                TestContext.Out.WriteLine(extension.Format(true));
            }
        }

        /// <summary>
        /// Verify encode and decode of authority key identifier.
        /// </summary>
        [Test]
        public void VerifyX509AuthorityKeyIdentifierExtension()
        {
            var authorityName = new X500DistinguishedName("CN=Test, O=OPC Foundation, DC=localhost");
            byte[] serialNumber = [9, 1, 2, 3, 4, 5, 6, 7, 8, 9];
            byte[] subjectKeyIdentifier = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
            var aki = new X509AuthorityKeyIdentifierExtension(
                subjectKeyIdentifier,
                authorityName,
                serialNumber);
            Assert.That(aki, Is.Not.Null);
            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(aki.Format(true));
            Assert.That(aki.Issuer, Is.EqualTo(authorityName));
            Assert.That(aki.GetSerialNumber(), Is.EqualTo(serialNumber));
            Assert.That(aki.SerialNumber, Is.EqualTo(serialNumber.ToHexString(true)));
            Assert.That(aki.GetKeyIdentifier(), Is.EqualTo(subjectKeyIdentifier));
            var akidecoded = new X509AuthorityKeyIdentifierExtension(
                aki.Oid,
                aki.RawData,
                aki.Critical);
            TestContext.Out.WriteLine("Decoded:");
            TestContext.Out.WriteLine(akidecoded.Format(true));
            Assert.That(akidecoded.RawData, Is.EqualTo(aki.RawData));
            Assert.That(akidecoded.Issuer.Name, Is.EqualTo(authorityName.Name));
            Assert.That(akidecoded.GetSerialNumber(), Is.EqualTo(serialNumber));
            Assert.That(akidecoded.SerialNumber, Is.EqualTo(serialNumber.ToHexString(true)));
            Assert.That(akidecoded.GetKeyIdentifier(), Is.EqualTo(subjectKeyIdentifier));
            akidecoded = new X509AuthorityKeyIdentifierExtension(
                aki.Oid.Value,
                aki.RawData,
                aki.Critical);
            TestContext.Out.WriteLine("Decoded2:");
            TestContext.Out.WriteLine(akidecoded.Format(true));
            Assert.That(akidecoded.RawData, Is.EqualTo(aki.RawData));
            Assert.That(akidecoded.Issuer.Name, Is.EqualTo(authorityName.Name));
            Assert.That(akidecoded.GetSerialNumber(), Is.EqualTo(serialNumber));
            Assert.That(akidecoded.SerialNumber, Is.EqualTo(serialNumber.ToHexString(true)));
            Assert.That(akidecoded.GetKeyIdentifier(), Is.EqualTo(subjectKeyIdentifier));
        }

        /// <summary>
        /// Verify authority Key Identifier API. Only Key ID.
        /// </summary>
        [Test]
        public void VerifyX509AuthorityKeyIdentifierExtensionOnlyKeyID()
        {
            byte[] subjectKeyIdentifier = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
            var aki = new X509AuthorityKeyIdentifierExtension(subjectKeyIdentifier);
            Assert.That(aki, Is.Not.Null);
            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(aki.Format(true));
            Assert.That(aki.Issuer, Is.Null);
            Assert.That(aki.GetSerialNumber(), Is.Null);
            Assert.That(aki.SerialNumber, Is.Empty);
            Assert.That(aki.GetKeyIdentifier(), Is.EqualTo(subjectKeyIdentifier));
            var akidecoded = new X509AuthorityKeyIdentifierExtension(
                aki.Oid,
                aki.RawData,
                aki.Critical);
            TestContext.Out.WriteLine("Decoded:");
            TestContext.Out.WriteLine(akidecoded.Format(true));
            Assert.That(akidecoded.RawData, Is.EqualTo(aki.RawData));
            Assert.That(aki.Issuer, Is.Null);
            Assert.That(aki.GetSerialNumber(), Is.Null);
            Assert.That(aki.SerialNumber, Is.Empty);
            Assert.That(akidecoded.GetKeyIdentifier(), Is.EqualTo(subjectKeyIdentifier));
            akidecoded = new X509AuthorityKeyIdentifierExtension(
                aki.Oid.Value,
                aki.RawData,
                aki.Critical);
            TestContext.Out.WriteLine("Decoded2:");
            TestContext.Out.WriteLine(akidecoded.Format(true));
            Assert.That(akidecoded.RawData, Is.EqualTo(aki.RawData));
            Assert.That(aki.Issuer, Is.Null);
            Assert.That(aki.GetSerialNumber(), Is.Null);
            Assert.That(aki.SerialNumber, Is.Empty);
            Assert.That(akidecoded.GetKeyIdentifier(), Is.EqualTo(subjectKeyIdentifier));
        }

        /// <summary>
        /// Verify encode and decode of authority key identifier.
        /// </summary>
        [Test]
        public void VerifyX509SubjectAlternateNameExtension()
        {
            const string applicationUri = "urn:opcfoundation.org";
            string[] domainNames = ["mypc.mydomain.com", "192.168.100.100", "1234:5678::1"];
            TestContext.Out.WriteLine("Encoded:");
            var san = new X509SubjectAltNameExtension(applicationUri, domainNames);
            TestContext.Out.WriteLine(san.Format(true));
            var decodedsan = new X509SubjectAltNameExtension(
                san.Oid.Value,
                san.RawData,
                san.Critical);
            Assert.That(decodedsan, Is.Not.Null);
            TestContext.Out.WriteLine("Decoded:");
            TestContext.Out.WriteLine(decodedsan.Format(true));
            Assert.That(decodedsan.DomainNames, Is.Not.Null);
            Assert.That(decodedsan.IPAddresses, Is.Not.Null);
            Assert.That(decodedsan.Uris, Is.Not.Null);
            Assert.That(decodedsan.Uris, Has.Count.EqualTo(1));
            Assert.That(decodedsan.DomainNames, Has.Count.EqualTo(1));
            Assert.That(decodedsan.IPAddresses, Has.Count.EqualTo(2));
            Assert.That(san.Oid.Value, Is.EqualTo(decodedsan.Oid.Value));
            Assert.That(san.Critical, Is.EqualTo(decodedsan.Critical));
            Assert.That(decodedsan.Uris[0], Is.EqualTo(applicationUri));
            Assert.That(decodedsan.DomainNames[0], Is.EqualTo(domainNames[0]));
            Assert.That(decodedsan.IPAddresses[0], Is.EqualTo(domainNames[1]));
            Assert.That(decodedsan.IPAddresses[1], Is.EqualTo(domainNames[2]));
        }

        /// <summary>
        /// Verify encode and decode of CRL Number.
        /// </summary>
        [Test]
        public void VerifyCRLNumberExtension()
        {
            BigInteger crlNumber = 123456789;
            TestContext.Out.WriteLine("Encoded:");
            var number = new X509CrlNumberExtension(crlNumber);
            TestContext.Out.WriteLine(number.Format(true));
            var decodednumber = new X509CrlNumberExtension(
                number.Oid.Value,
                number.RawData,
                number.Critical);
            Assert.That(decodednumber, Is.Not.Null);
            TestContext.Out.WriteLine("Decoded:");
            TestContext.Out.WriteLine(decodednumber.Format(true));
            Assert.That(decodednumber.CrlNumber, Is.EqualTo(crlNumber));
        }
    }
}
