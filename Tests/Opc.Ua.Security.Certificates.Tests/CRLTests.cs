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
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Tests for the CRL class.
    /// </summary>
    [TestFixture, Category("CRL")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CRLTests
    {
        #region DataPointSources
        [DatapointSource]
        public CRLAsset[] CRLTestCases = new AssetCollection<CRLAsset>(Directory.EnumerateFiles("./Assets", "*.crl")).ToArray();
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
        public void DecodeCRLs(
            CRLAsset crlAsset
            )
        {
            var x509Crl = new X509CRL(crlAsset.Crl);
            Assert.NotNull(x509Crl);
            TestContext.Out.WriteLine($"CRLAsset:   {x509Crl.Issuer}");
            var crlInfo = WriteCRL(x509Crl);
            TestContext.Out.WriteLine(crlInfo);
        }

        /// <summary>
        /// Validate a CRL Builder and decoder pass.
        /// </summary>
        [Test]
        public void CrlInternalBuilderTest()
        {
            var dname = new X500DistinguishedName("CN=Test");
            var hash = HashAlgorithmName.SHA256;
            var crlBuilder = new CrlBuilder(dname, hash)
                .SetNextUpdate(DateTime.Today.AddDays(30));
            byte[] serial = new byte[] { 4, 5, 6, 7 };
            var revokedarray = new RevokedCertificate(serial);
            crlBuilder.RevokedCertificates.Add(revokedarray);
            string serstring = "45678910";
            var revokedstring = new RevokedCertificate(serstring);
            crlBuilder.RevokedCertificates.Add(revokedstring);
            crlBuilder.CrlExtensions.Add(X509Extensions.BuildCRLNumber(123));
            var crlEncoded = crlBuilder.Encode();
            Assert.NotNull(crlEncoded);
            var x509Crl = new X509CRL();
            x509Crl.DecodeCrl(crlEncoded);
            Assert.NotNull(x509Crl);
            Assert.NotNull(x509Crl.CrlExtensions);
            Assert.NotNull(x509Crl.RevokedCertificates);
            Assert.AreEqual(dname.RawData, x509Crl.IssuerName.RawData);
            //Assert.AreEqual(crlBuilder.ThisUpdate, x509Crl.ThisUpdate);
            //Assert.AreEqual(crlBuilder.NextUpdate, x509Crl.NextUpdate);
            Assert.AreEqual(2, x509Crl.RevokedCertificates.Count);
            Assert.AreEqual(serial, x509Crl.RevokedCertificates[0].UserCertificate);
            Assert.AreEqual(serstring, x509Crl.RevokedCertificates[1].SerialNumber);
            Assert.AreEqual(1, x509Crl.CrlExtensions.Count);
            Assert.AreEqual(hash, x509Crl.HashAlgorithmName);
        }

        /// <summary>
        /// Validate the internal CRL encoder and decoder pass.
        /// </summary>
        [Test]
        public void CrlBuilderTest()
        {
            var dname = new X500DistinguishedName("CN=Test");
            var hash = HashAlgorithmName.SHA256;
            var crlBuilder = new CrlBuilder(dname, hash)
                .SetNextUpdate(DateTime.Today.AddDays(30));
            byte[] serial = new byte[] { 4, 5, 6, 7 };
            var revokedarray = new RevokedCertificate(serial);
            crlBuilder.RevokedCertificates.Add(revokedarray);
            string serstring = "45678910";
            var revokedstring = new RevokedCertificate(serstring);
            crlBuilder.RevokedCertificates.Add(revokedstring);
            crlBuilder.CrlExtensions.Add(X509Extensions.BuildCRLNumber(123));
            var crlEncoded = crlBuilder.Encode();
            Assert.NotNull(crlEncoded);
            var x509Crl = new X509CRL();
            x509Crl.DecodeCrl(crlEncoded);
            Assert.NotNull(x509Crl);
            Assert.NotNull(x509Crl.CrlExtensions);
            Assert.NotNull(x509Crl.RevokedCertificates);
            Assert.AreEqual(dname.RawData, x509Crl.IssuerName.RawData);
            //Assert.AreEqual(crlBuilder.ThisUpdate, x509Crl.ThisUpdate);
            //Assert.AreEqual(crlBuilder.NextUpdate, x509Crl.NextUpdate);
            Assert.AreEqual(2, x509Crl.RevokedCertificates.Count);
            Assert.AreEqual(serial, x509Crl.RevokedCertificates[0].UserCertificate);
            Assert.AreEqual(serstring, x509Crl.RevokedCertificates[1].SerialNumber);
            Assert.AreEqual(1, x509Crl.CrlExtensions.Count);
            Assert.AreEqual(hash, x509Crl.HashAlgorithmName);
        }

        #endregion

        #region Private Methods
        private string WriteCRL(X509CRL x509Crl)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine($"Issuer:     {x509Crl.Issuer}");
            stringBuilder.AppendLine($"ThisUpdate: {x509Crl.ThisUpdate}");
            stringBuilder.AppendLine($"NextUpdate: {x509Crl.NextUpdate}");
            stringBuilder.AppendLine($"RevokedCertificates:");
            foreach (var revokedCert in x509Crl.RevokedCertificates)
            {
                stringBuilder.Append($"{revokedCert.SerialNumber:20}, {revokedCert.RevocationDate}, ");
                foreach (var entryExt in revokedCert.CrlEntryExtensions)
                {
                    stringBuilder.Append($"{entryExt.Format(false)} ");
                }
                stringBuilder.AppendLine("");
            }
            stringBuilder.AppendLine($"Extensions:");
            foreach (var extension in x509Crl.CrlExtensions)
            {
                stringBuilder.AppendLine($"{extension.Format(false)}");
            }
            return stringBuilder.ToString();
        }
        #endregion

        #region Private Fields
        #endregion
    }

}
