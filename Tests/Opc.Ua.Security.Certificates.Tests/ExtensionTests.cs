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


using System.IO;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Tests for the CertificateFactory class.
    /// </summary>
    [TestFixture, Category("X509Extensions")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class ExtensionTests
    {
        #region DataPointSources
        [DatapointSource]
        public CertificateAsset[] CertificateTestCases = new AssetCollection<CertificateAsset>(TestUtils.EnumerateTestAssets("*.?er")).ToArray();
        #endregion

        #region Test Methods
        [Theory]
        public void DecodeExtensions(
            CertificateAsset certAsset
            )
        {
            using (var x509Cert = new X509Certificate2(certAsset.Cert))
            {
                Assert.NotNull(x509Cert);
                TestContext.Out.WriteLine($"CertificateAsset:");
                TestContext.Out.WriteLine(x509Cert);
                var altName = X509Extensions.FindExtension<X509SubjectAltNameExtension>(x509Cert);
                if (altName != null)
                {
                    TestContext.Out.WriteLine($"X509SubjectAltNameExtension:");
                    TestContext.Out.WriteLine(altName?.Format(true));
                    var ext = new X509Extension(altName.Oid, altName.RawData, altName.Critical);
                    TestContext.Out.WriteLine(ext.Format(true));
                }
                var authority = X509Extensions.FindExtension<X509AuthorityKeyIdentifierExtension>(x509Cert);
                if (authority != null)
                {
                    TestContext.Out.WriteLine($"X509AuthorityKeyIdentifierExtension:");
                    TestContext.Out.WriteLine(authority?.Format(true));
                    var ext = new X509Extension(authority.Oid, authority.RawData, authority.Critical);
                    TestContext.Out.WriteLine(ext.Format(true));
                }
                TestContext.Out.WriteLine($"All extensions:");
                foreach (var extension in x509Cert.Extensions)
                {
                    TestContext.Out.WriteLine(extension.Format(true));
                }
            }
        }

        /// <summary>
        /// Verify encode and decode of authority key identifier.
        /// </summary>
        [Test]
        public void VerifyX509AuthorityKeyIdentifierExtension()
        {
            var authorityName = new X500DistinguishedName("CN=Test,O=OPC Foundation,DC=localhost");
            byte[] serialNumber = new byte[] { 9, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            byte[] subjectKeyIdentifier = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var aki = new X509AuthorityKeyIdentifierExtension(subjectKeyIdentifier, authorityName, serialNumber);
            Assert.NotNull(aki);
            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(aki.Format(true));
            Assert.AreEqual(authorityName, aki.Issuer);
            Assert.AreEqual(serialNumber, aki.GetSerialNumber());
            Assert.AreEqual(AsnUtils.ToHexString(serialNumber, true), aki.SerialNumber);
            Assert.AreEqual(subjectKeyIdentifier, aki.GetKeyIdentifier());
            var akidecoded = new X509AuthorityKeyIdentifierExtension(aki.Oid, aki.RawData, aki.Critical);
            TestContext.Out.WriteLine("Decoded:");
            TestContext.Out.WriteLine(akidecoded.Format(true));
            Assert.AreEqual(aki.RawData, akidecoded.RawData);
            Assert.AreEqual(authorityName.ToString(), akidecoded.Issuer.ToString());
            Assert.AreEqual(serialNumber, akidecoded.GetSerialNumber());
            Assert.AreEqual(AsnUtils.ToHexString(serialNumber, true), akidecoded.SerialNumber);
            Assert.AreEqual(subjectKeyIdentifier, akidecoded.GetKeyIdentifier());
            akidecoded = new X509AuthorityKeyIdentifierExtension(aki.Oid.Value, aki.RawData, aki.Critical);
            TestContext.Out.WriteLine("Decoded2:");
            TestContext.Out.WriteLine(akidecoded.Format(true));
            Assert.AreEqual(aki.RawData, akidecoded.RawData);
            Assert.AreEqual(authorityName.ToString(), akidecoded.Issuer.ToString());
            Assert.AreEqual(serialNumber, akidecoded.GetSerialNumber());
            Assert.AreEqual(AsnUtils.ToHexString(serialNumber, true), akidecoded.SerialNumber);
            Assert.AreEqual(subjectKeyIdentifier, akidecoded.GetKeyIdentifier());
        }

        /// <summary>
        /// Verify authority Key Identifier API. Only Key ID.
        /// </summary>
        [Test]
        public void VerifyX509AuthorityKeyIdentifierExtensionOnlyKeyID()
        {
            byte[] subjectKeyIdentifier = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var aki = new X509AuthorityKeyIdentifierExtension(subjectKeyIdentifier);
            Assert.NotNull(aki);
            TestContext.Out.WriteLine("Encoded:");
            TestContext.Out.WriteLine(aki.Format(true));
            Assert.Null(aki.Issuer);
            Assert.Null(aki.GetSerialNumber());
            Assert.AreEqual(string.Empty, aki.SerialNumber);
            Assert.AreEqual(subjectKeyIdentifier, aki.GetKeyIdentifier());
            var akidecoded = new X509AuthorityKeyIdentifierExtension(aki.Oid, aki.RawData, aki.Critical);
            TestContext.Out.WriteLine("Decoded:");
            TestContext.Out.WriteLine(akidecoded.Format(true));
            Assert.AreEqual(aki.RawData, akidecoded.RawData);
            Assert.Null(aki.Issuer);
            Assert.Null(aki.GetSerialNumber());
            Assert.AreEqual(string.Empty, aki.SerialNumber);
            Assert.AreEqual(subjectKeyIdentifier, akidecoded.GetKeyIdentifier());
            akidecoded = new X509AuthorityKeyIdentifierExtension(aki.Oid.Value, aki.RawData, aki.Critical);
            TestContext.Out.WriteLine("Decoded2:");
            TestContext.Out.WriteLine(akidecoded.Format(true));
            Assert.AreEqual(aki.RawData, akidecoded.RawData);
            Assert.Null(aki.Issuer);
            Assert.Null(aki.GetSerialNumber());
            Assert.AreEqual(string.Empty, aki.SerialNumber);
            Assert.AreEqual(subjectKeyIdentifier, akidecoded.GetKeyIdentifier());
        }


        /// <summary>
        /// Verify encode and decode of authority key identifier.
        /// </summary>
        [Test]
        public void VerifyX509SubjectAlternateNameExtension()
        {
            string applicationUri = "urn:opcfoundation.org";
            string[] domainNames = { "mypc.mydomain.com", "192.168.100.100", "1234:5678::1" };
            TestContext.Out.WriteLine("Encoded:");
            var san = new X509SubjectAltNameExtension(applicationUri, domainNames);
            TestContext.Out.WriteLine(san.Format(true));
            var decodedsan = new X509SubjectAltNameExtension(san.Oid.Value, san.RawData, san.Critical);
            Assert.NotNull(decodedsan);
            TestContext.Out.WriteLine("Decoded:");
            TestContext.Out.WriteLine(decodedsan.Format(true));
            Assert.NotNull(decodedsan.DomainNames);
            Assert.NotNull(decodedsan.IPAddresses);
            Assert.NotNull(decodedsan.Uris);
            Assert.AreEqual(1, decodedsan.Uris.Count);
            Assert.AreEqual(1, decodedsan.DomainNames.Count);
            Assert.AreEqual(2, decodedsan.IPAddresses.Count);
            Assert.AreEqual(decodedsan.Oid.Value, san.Oid.Value);
            Assert.AreEqual(decodedsan.Critical, san.Critical);
            Assert.AreEqual(applicationUri, decodedsan.Uris[0]);
            Assert.AreEqual(domainNames[0], decodedsan.DomainNames[0]);
            Assert.AreEqual(domainNames[1], decodedsan.IPAddresses[0]);
            Assert.AreEqual(domainNames[2], decodedsan.IPAddresses[1]);
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
            var decodednumber = new X509CrlNumberExtension(number.Oid.Value, number.RawData, number.Critical);
            Assert.NotNull(decodednumber);
            TestContext.Out.WriteLine("Decoded:");
            TestContext.Out.WriteLine(decodednumber.Format(true));
            Assert.AreEqual(crlNumber, decodednumber.CrlNumber);
        }

        #endregion

        #region Private Methods
        #endregion

        #region Private Fields
        #endregion
    }

}
