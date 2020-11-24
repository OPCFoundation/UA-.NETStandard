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
using System.Linq;
using System.Text;
using NUnit.Framework;
using Org.BouncyCastle.X509;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Tests for the CertificateFactory class.
    /// </summary>
    [TestFixture, Category("CRL")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CertificateFactoryTest
    {
        #region DataPointSources
        public class CRLAsset : IFormattable
        {
            public CRLAsset(byte[] crl, string path)
            {
                Path = path;
                Crl = crl;
                try
                {
                    X509CrlParser parser = new X509CrlParser();
                    X509Crl = parser.ReadCrl(crl);
                }
                catch
                { }
            }

            public string Path;
            public byte[] Crl;
            public X509Crl X509Crl;

            public string ToString(string format, IFormatProvider formatProvider)
            {
                var file = System.IO.Path.GetFileName(Path);
                return $"{file}";
            }
        }

        public class CRLAssetCollection : List<CRLAsset>
        {
            public CRLAssetCollection() { }
            public CRLAssetCollection(IEnumerable<CRLAsset> collection) : base(collection) { }
            public CRLAssetCollection(int capacity) : base(capacity) { }
            public static CRLAssetCollection ToCLRAssetCollection(CRLAsset[] values)
            {
                return values != null ? new CRLAssetCollection(values) : new CRLAssetCollection();
            }

            public CRLAssetCollection(IEnumerable<string> filelist) : base()
            {
                foreach (var file in filelist)
                {
                    Add(file);
                }
            }

            public void Add(string path)
            {
                byte[] crl = File.ReadAllBytes(path);
                Add(new CRLAsset(crl, path));
            }
        }

        [DatapointSource]
        public CRLAsset[] CRLTestCases = new CRLAssetCollection(Directory.EnumerateFiles("./Asset", "*.crl")).ToArray();
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
            TestContext.Out.WriteLine($"CRLAsset:   {GetIssuer(crlAsset.X509Crl)}");
            var crlInfo = WriteCRL(x509Crl);
            TestContext.Out.WriteLine(crlInfo);
        }
        #endregion

        #region Private Methods
        private string WriteCRL(X509CRL x509Crl)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine($"Issuer:     {x509Crl.Issuer}");
            stringBuilder.AppendLine($"ThisUpdate: {x509Crl.ThisUpdate}");
            stringBuilder.AppendLine($"NextUpdate: {x509Crl.NextUpdate}");
#if NETCOREAPP3_1
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
#endif
            return stringBuilder.ToString();
        }

        private static string GetIssuer(X509Crl crl)
        {
            // a few conversions to match System.Security conventions
            string issuerDN = crl.IssuerDN.ToString();
            // replace state ST= with S= 
            issuerDN = issuerDN.Replace("ST=", "S=");
            // reverse DN order to match System.Security
            List<string> issuerList = X509Utils.ParseDistinguishedName(issuerDN);
            issuerList.Reverse();
            return string.Join(", ", issuerList);
        }
        #endregion

        #region Private Fields
        #endregion
    }

}
