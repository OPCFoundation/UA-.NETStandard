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
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Tests for the CertificateFactory class.
    /// </summary>
    [TestFixture, Category("Certificate")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CertificateTests
    {
        #region DataPointSources
        public const string Subject = "CN=Test Cert Subject";

        [DatapointSource]
        public CertificateAsset[] CertificateTestCases = new AssetCollection<CertificateAsset>(Directory.EnumerateFiles("./Assets", "*.der")).ToArray();

        [DatapointSource]
        public KeyHashPair[] KeyHashPairs = new KeyHashPairCollection {
            { 1024, HashAlgorithmName.SHA256 /* SHA-1 is deprecated HashAlgorithmName.SHA1*/ },
            { 2048, HashAlgorithmName.SHA256 },
            { 3072, HashAlgorithmName.SHA384 },
            { 4096, HashAlgorithmName.SHA512 } }.ToArray();

#if !NET462
        [DatapointSource]
        public ECCurve[] NamedCurves = typeof(ECCurve.NamedCurves).GetProperties(BindingFlags.Public | BindingFlags.Static).Select(x => ECCurve.CreateFromFriendlyName(x.Name)).ToArray();
#endif
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
#if NETCOREAPP3_1
        /// <summary>
        /// Verify self signed app certs.
        /// </summary>
        [Test]
        public void VerifyOneSelfSignedAppCertForAll()
        {
            var builder = new CertificateBuilder(Subject)
                .SetNotBefore(DateTime.Today.AddYears(-1))
                .SetNotAfter(DateTime.Today.AddYears(25))
                .AddExtension(new X509SubjectAltNameExtension("urn:opcfoundation.org:mypc", new string[] { "mypc", "mypc.opcfoundation.org", "192.168.1.100" }));
            foreach (var keyHash in KeyHashPairs)
            {
                var cert = builder
                .SetHashAlgorithm(keyHash.HashAlgorithmName)
                .CreateForRSA(keyHash.KeySize);
                Assert.NotNull(cert);
                WriteCertificate(cert, $"Default cert with RSA {keyHash.KeySize} {keyHash.HashAlgorithmName} signature.");
                Assert.AreEqual(keyHash.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            }
        }

        [Theory]
        public void CreateSelfSignedForRSATests(
            KeyHashPair keyHashPair
            )
        {
            // default cert with custom key
            X509Certificate2 cert = new CertificateBuilder(Subject)
                .CreateForRSA(keyHashPair.KeySize);
            WriteCertificate(cert, $"Default RSA {keyHashPair.KeySize} cert");
            Assert.AreEqual(keyHashPair.KeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(Defaults.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));

            // default cert with custom HashAlgorithm
            cert = new CertificateBuilder(Subject)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .CreateForRSA();
            Assert.NotNull(cert);
            WriteCertificate(cert, $"Default RSA {keyHashPair.HashAlgorithmName} cert");
            Assert.AreEqual(Defaults.RSAKeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(keyHashPair.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));

            // set dates
            cert = new CertificateBuilder(Subject)
                .SetNotBefore(DateTime.Today.AddYears(-1))
                .SetNotAfter(DateTime.Today.AddYears(25))
                .AddExtension(new X509SubjectAltNameExtension("urn:opcfoundation.org:mypc", new string[] { "mypc", "mypc.opcfoundation.org", "192.168.1.100" }))
                .CreateForRSA(keyHashPair.KeySize);
            Assert.NotNull(cert);
            WriteCertificate(cert, $"Default cert RSA {keyHashPair.KeySize} with modified lifetime and alt name extension");
            Assert.AreEqual(keyHashPair.KeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(Defaults.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));

            // set hash algorithm
            cert = new CertificateBuilder(Subject)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .CreateForRSA(keyHashPair.KeySize);
            Assert.NotNull(cert);
            WriteCertificate(cert, $"Default cert with RSA {keyHashPair.KeySize} {keyHashPair.HashAlgorithmName} signature.");
            Assert.AreEqual(keyHashPair.KeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(keyHashPair.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            cert = new CertificateBuilder(Subject)
                .SetCAConstraint(-1)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .AddExtension(X509Extensions.BuildX509CRLDistributionPoints("http://myca/mycert.crl"))
                .CreateForRSA(keyHashPair.KeySize);
            Assert.NotNull(cert);
            WriteCertificate(cert, "Default cert with RSA {keyHashPair.KeySize} {keyHashPair.HashAlgorithmName} and CRL distribution points");
            Assert.AreEqual(keyHashPair.KeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(keyHashPair.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
        }

        [Test]
        public void CreateSelfSignedForRSADefaultTest()
        {
            // default cert
            X509Certificate2 cert = new CertificateBuilder(Subject).CreateForRSA();
            Assert.NotNull(cert);
            WriteCertificate(cert, "Default RSA cert");
            Assert.NotNull(cert.GetRSAPrivateKey());
            var publicKey = cert.GetRSAPublicKey();
            Assert.NotNull(publicKey);
            Assert.AreEqual(Defaults.RSAKeySize, publicKey.KeySize);
            Assert.AreEqual(Defaults.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            Assert.AreEqual(DateTime.UtcNow.AddDays(-1).Date, cert.NotBefore.ToUniversalTime());
            Assert.AreEqual(cert.NotBefore.ToUniversalTime().AddMonths(Defaults.LifeTime), cert.NotAfter.ToUniversalTime());
            X509Utils.VerifyRSAKeyPair(cert, cert);
            X509Utils.VerifySelfSigned(cert);
        }

        [Test]
        public void CreateRSADefaultWithSerialTest()
        {
            // default cert
            Assert.Throws<ArgumentOutOfRangeException>(
                () => {
                    var cert = new CertificateBuilder(Subject)
                    .SetSerialNumberLength(0)
                    .CreateForRSA();
                }
            );
            Assert.Throws<ArgumentOutOfRangeException>(
                () => {
                    var cert = new CertificateBuilder(Subject)
                    .SetSerialNumberLength(Defaults.SerialNumberLengthMax + 1)
                    .CreateForRSA();
                }
            );
            var builder = new CertificateBuilder(Subject)
                .SetSerialNumberLength(Defaults.SerialNumberLengthMax);

            // ensure every cert has a different serial number
            var cert1 = builder.CreateForRSA();
            var cert2 = builder.CreateForRSA();

            Assert.AreEqual(cert1.GetSerialNumber().Length, Defaults.SerialNumberLengthMax);
            Assert.AreEqual(cert1.SerialNumber.Length, cert2.SerialNumber.Length);
            Assert.AreEqual(cert1.GetSerialNumber().Length, cert2.GetSerialNumber().Length);
            Assert.AreNotEqual(cert1.SerialNumber, cert2.SerialNumber);
        }

        [Theory]
        public void CreateSelfSignedForECDsaTests(ECCurve eccurve)
        {
            // default cert
            X509Certificate2 cert = new CertificateBuilder(Subject).CreateForECDsa(eccurve);
            WriteCertificate(cert, "Default ECDsa cert");
            // set dates
            cert = new CertificateBuilder(Subject)
                .SetNotBefore(DateTime.Today.AddYears(-1))
                .SetNotAfter(DateTime.Today.AddYears(25))
                .AddExtension(new X509SubjectAltNameExtension("urn:opcfoundation.org:mypc", new string[] { "mypc", "mypc.opcfoundation.org", "192.168.1.100" }))
                .CreateForECDsa(eccurve);
            WriteCertificate(cert, "Default cert with modified lifetime and alt name extension");
            // set hash alg
            cert = new CertificateBuilder(Subject)
                .SetHashAlgorithm(HashAlgorithmName.SHA512)
                .CreateForECDsa(eccurve);
            WriteCertificate(cert, "Default cert with SHA512 signature.");
            // set CA constraints
            cert = new CertificateBuilder(Subject)
                .SetCAConstraint(-1)
                .AddExtension(X509Extensions.BuildX509CRLDistributionPoints("http://myca/mycert.crl"))
                .CreateForECDsa(eccurve);
            WriteCertificate(cert, "Default cert with CA constraints None and CRL distribution points");
        }

#endif
        #endregion

        #region Private Methods
        private void WriteCertificate(X509Certificate2 cert, string message)
        {
            TestContext.Out.WriteLine(message);
            TestContext.Out.WriteLine(cert);
            foreach (var ext in cert.Extensions)
            {
                TestContext.Out.WriteLine(ext.Format(false));
            }
        }
        #endregion

        #region Private Fields
        #endregion
    }

}
