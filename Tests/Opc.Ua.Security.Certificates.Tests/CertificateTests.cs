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
        public CertificateAsset[] CertificateTestCases = new AssetCollection<CertificateAsset>(TestUtils.EnumerateTestAssets("*.?er")).ToArray();

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
        /// <summary>
        /// Verify self signed app certs. Use one builder to create multiple certs.
        /// </summary>
        [Test]
        public void VerifyOneSelfSignedAppCertForAll()
        {
            var builder = CertificateBuilder.Create(Subject)
                .SetNotBefore(DateTime.Today.AddYears(-1))
                .SetNotAfter(DateTime.Today.AddYears(25))
                .AddExtension(new X509SubjectAltNameExtension("urn:opcfoundation.org:mypc", new string[] { "mypc", "mypc.opcfoundation.org", "192.168.1.100" }));
            byte[] previousSerialNumber = null;
            foreach (var keyHash in KeyHashPairs)
            {
                var cert = builder
                    .SetHashAlgorithm(keyHash.HashAlgorithmName)
                    .SetRSAKeySize(keyHash.KeySize)
                    .CreateForRSA();
                Assert.NotNull(cert);
                WriteCertificate(cert, $"Default cert with RSA {keyHash.KeySize} {keyHash.HashAlgorithmName} signature.");
                Assert.AreEqual(keyHash.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
                // ensure serial numbers are different
                Assert.AreNotEqual(previousSerialNumber, cert.GetSerialNumber());
            }
        }

        /// <summary>
        /// 
        /// </summary>
        [Test]
        public void CreateSelfSignedForRSADefaultTest()
        {
            // default cert
            X509Certificate2 cert = CertificateBuilder.Create(Subject).CreateForRSA();
            Assert.NotNull(cert);
            WriteCertificate(cert, "Default RSA cert");
            Assert.NotNull(cert.GetRSAPrivateKey());
            var publicKey = cert.GetRSAPublicKey();
            Assert.NotNull(publicKey);
            Assert.AreEqual(Defaults.RSAKeySize, publicKey.KeySize);
            Assert.AreEqual(Defaults.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            Assert.AreEqual(DateTime.UtcNow.AddDays(-1).Date, cert.NotBefore.ToUniversalTime());
            Assert.AreEqual(cert.NotBefore.ToUniversalTime().AddMonths(Defaults.LifeTime), cert.NotAfter.ToUniversalTime());
            var basicConstraintsExtension = X509Extensions.FindExtension<X509BasicConstraintsExtension>(cert.Extensions);
            Assert.NotNull(basicConstraintsExtension);
            Assert.True(basicConstraintsExtension.CertificateAuthority);
            X509Utils.VerifyRSAKeyPair(cert, cert);
            X509Utils.VerifySelfSigned(cert);
        }

        [Theory]
        public void CreateSelfSignedForRSADefaultHashCustomKey(
            KeyHashPair keyHashPair
            )
        {
            // default cert with custom key
            X509Certificate2 cert = CertificateBuilder.Create(Subject)
                .SetRSAKeySize(keyHashPair.KeySize)
                .CreateForRSA();
            WriteCertificate(cert, $"Default RSA {keyHashPair.KeySize} cert");
            Assert.AreEqual(Subject, cert.Subject);
            Assert.AreEqual(keyHashPair.KeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(Defaults.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            var basicConstraintsExtension = X509Extensions.FindExtension<X509BasicConstraintsExtension>(cert.Extensions);
            Assert.NotNull(basicConstraintsExtension);
            Assert.True(basicConstraintsExtension.CertificateAuthority);
            X509Utils.VerifyRSAKeyPair(cert, cert);
            X509Utils.VerifySelfSigned(cert);
        }

        [Theory]
        public void CreateSelfSignedForRSACustomHashDefaultKey(
            KeyHashPair keyHashPair
            )
        {
            // default cert with custom HashAlgorithm
            var cert = CertificateBuilder.Create(Subject)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .CreateForRSA();
            Assert.NotNull(cert);
            WriteCertificate(cert, $"Default RSA {keyHashPair.HashAlgorithmName} cert");
            Assert.AreEqual(Subject, cert.Subject);
            Assert.AreEqual(Defaults.RSAKeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(keyHashPair.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            var basicConstraintsExtension = X509Extensions.FindExtension<X509BasicConstraintsExtension>(cert.Extensions);
            Assert.NotNull(basicConstraintsExtension);
            Assert.True(basicConstraintsExtension.CertificateAuthority);
            X509Utils.VerifyRSAKeyPair(cert, cert);
            X509Utils.VerifySelfSigned(cert);
        }

        [Theory]
        public void CreateSelfSignedForRSAAllFields(
            KeyHashPair keyHashPair
            )
        {
            // set dates and extension
            var applicationUri = "urn:opcfoundation.org:mypc";
            var domains = new string[] { "mypc", "mypc.opcfoundation.org", "192.168.1.100" };
            var cert = CertificateBuilder.Create(Subject)
                .SetNotBefore(DateTime.Today.AddYears(-1))
                .SetNotAfter(DateTime.Today.AddYears(25))
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, domains))
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .SetRSAKeySize(keyHashPair.KeySize)
                .CreateForRSA();
            Assert.NotNull(cert);
            WriteCertificate(cert, $"Default cert RSA {keyHashPair.KeySize} with modified lifetime and alt name extension");
            Assert.AreEqual(Subject, cert.Subject);
            Assert.AreEqual(keyHashPair.KeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(keyHashPair.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            var basicConstraintsExtension = X509Extensions.FindExtension<X509BasicConstraintsExtension>(cert.Extensions);
            Assert.NotNull(basicConstraintsExtension);
            Assert.True(basicConstraintsExtension.CertificateAuthority);
            X509Utils.VerifyRSAKeyPair(cert, cert);
            X509Utils.VerifySelfSigned(cert);
        }

        [Theory]
        public void CreateCACertForRSA(
            KeyHashPair keyHashPair
            )
        {
            // create a CA cert
            var cert = CertificateBuilder.Create(Subject)
                .SetCAConstraint(-1)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .AddExtension(X509Extensions.BuildX509CRLDistributionPoints("http://myca/mycert.crl"))
                .SetRSAKeySize(keyHashPair.KeySize)
                .CreateForRSA();
            Assert.NotNull(cert);
            WriteCertificate(cert, "Default cert with RSA {keyHashPair.KeySize} {keyHashPair.HashAlgorithmName} and CRL distribution points");
            Assert.AreEqual(keyHashPair.KeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(keyHashPair.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            var basicConstraintsExtension = X509Extensions.FindExtension<X509BasicConstraintsExtension>(cert.Extensions);
            Assert.NotNull(basicConstraintsExtension);
            Assert.True(basicConstraintsExtension.CertificateAuthority);
            Assert.False(basicConstraintsExtension.HasPathLengthConstraint);
            X509Utils.VerifyRSAKeyPair(cert, cert);
            X509Utils.VerifySelfSigned(cert);
        }

        [Test]
        public void CreateRSADefaultWithSerialTest()
        {
            // default cert
            Assert.Throws<ArgumentOutOfRangeException>(
                () => {
                    var cert = CertificateBuilder.Create(Subject)
                    .SetSerialNumberLength(0)
                    .CreateForRSA();
                }
            );
            Assert.Throws<ArgumentOutOfRangeException>(
                () => {
                    var cert = CertificateBuilder.Create(Subject)
                    .SetSerialNumberLength(Defaults.SerialNumberLengthMax + 1)
                    .CreateForRSA();
                }
            );
            var builder = CertificateBuilder.Create(Subject)
                .SetSerialNumberLength(Defaults.SerialNumberLengthMax);

            // ensure every cert has a different serial number
            var cert1 = builder.CreateForRSA();
            var cert2 = builder.CreateForRSA();
            WriteCertificate(cert1, "Cert1 with max length serial number");
            WriteCertificate(cert2, "Cert2 with max length serial number");
            Assert.AreEqual(Defaults.SerialNumberLengthMax, cert1.GetSerialNumber().Length);
            Assert.AreEqual(cert1.SerialNumber.Length, cert2.SerialNumber.Length);
            Assert.AreEqual(cert1.GetSerialNumber().Length, cert2.GetSerialNumber().Length);
            Assert.AreNotEqual(cert1.SerialNumber, cert2.SerialNumber);
        }

        [Test]
        public void CreateRSAManualSerialTest()
        {
            // default cert
            Assert.Throws<ArgumentOutOfRangeException>(
                () => {
                    var cert = CertificateBuilder.Create(Subject)
                    .SetSerialNumber(new byte[0])
                    .CreateForRSA();
                }
            );
            Assert.Throws<ArgumentOutOfRangeException>(
                () => {
                    var cert = CertificateBuilder.Create(Subject)
                    .SetSerialNumber(new byte[Defaults.SerialNumberLengthMax + 1])
                    .CreateForRSA();
                }
            );
            var serial = new byte[Defaults.SerialNumberLengthMax];
            for (int i = 0; i < serial.Length; i++)
            {
                serial[i] = (byte)((i + 1) | 0x80);
            }

            // test if sign bit is cleared
            var builder = CertificateBuilder.Create(Subject)
                .SetSerialNumber(serial);
            serial[serial.Length - 1] &= 0x7f;
            Assert.AreEqual(serial, builder.GetSerialNumber());
            var cert1 = builder.CreateForRSA();
            WriteCertificate(cert1, "Cert1 with max length serial number");

            // clear sign bit
            builder.SetSerialNumber(serial);
            Assert.AreEqual(serial, builder.GetSerialNumber());

            var cert2 = builder.CreateForRSA();
            WriteCertificate(cert2, "Cert2 with max length serial number");
            TestContext.Out.WriteLine($"Serial: {serial.ToHexString(true)}");

            Assert.AreEqual(Defaults.SerialNumberLengthMax, cert1.GetSerialNumber().Length);
            Assert.AreEqual(cert1.SerialNumber.Length, cert2.SerialNumber.Length);
            Assert.AreEqual(cert1.SerialNumber, cert2.SerialNumber);
            Assert.AreEqual(Defaults.SerialNumberLengthMax, cert2.GetSerialNumber().Length);
            Assert.AreEqual(serial, cert1.GetSerialNumber());
            Assert.AreEqual(serial, cert2.GetSerialNumber());
        }


#if ECC_SUPPORT
        [Theory]
        public void CreateSelfSignedForECDsaTests(ECCurve eccurve)
        {
            // default cert
            X509Certificate2 cert = CertificateBuilder.Create(Subject).SetECCurve(eccurve).CreateForECDsa();
            WriteCertificate(cert, "Default ECDsa cert");
            // set dates
            cert = CertificateBuilder.Create(Subject)
                .SetNotBefore(DateTime.Today.AddYears(-1))
                .SetNotAfter(DateTime.Today.AddYears(25))
                .AddExtension(new X509SubjectAltNameExtension("urn:opcfoundation.org:mypc", new string[] { "mypc", "mypc.opcfoundation.org", "192.168.1.100" }))
                .SetECCurve(eccurve)
                .CreateForECDsa();
            WriteCertificate(cert, "Default cert with modified lifetime and alt name extension");
            // set hash alg
            cert = CertificateBuilder.Create(Subject)
                .SetHashAlgorithm(HashAlgorithmName.SHA512)
                .SetECCurve(eccurve)
                .CreateForECDsa();
            WriteCertificate(cert, "Default cert with SHA512 signature.");
            // set CA constraints
            cert = CertificateBuilder.Create(Subject)
                .SetCAConstraint(-1)
                .AddExtension(X509Extensions.BuildX509CRLDistributionPoints("http://myca/mycert.crl"))
                .SetECCurve(eccurve)
                .CreateForECDsa();
            WriteCertificate(cert, "Default cert with CA constraints None and CRL distribution points");
        }
#endif

        [Theory]
        public void CreateForRSAWithGeneratorTest(
            KeyHashPair keyHashPair
            )
        {
            // default signing cert with custom key
            X509Certificate2 signingCert = CertificateBuilder.Create(Subject)
                .SetCAConstraint()
                .SetHashAlgorithm(HashAlgorithmName.SHA512)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            WriteCertificate(signingCert, $"Signing RSA {signingCert.GetRSAPublicKey().KeySize} cert");

            using (RSA rsaPrivateKey = signingCert.GetRSAPrivateKey())
            {
                var generator = X509SignatureGenerator.CreateForRSA(rsaPrivateKey, RSASignaturePadding.Pkcs1);
                var cert = CertificateBuilder.Create("CN=App Cert")
                    .SetIssuer(new X509Certificate2(signingCert.RawData))
                    .CreateForRSA(generator);
                Assert.NotNull(cert);
                WriteCertificate(cert, $"Default signed RSA cert");
            }

            // TODO: use case public key
            using (RSA rsaPrivateKey = signingCert.GetRSAPrivateKey())
            using (RSA rsaPublicKey = signingCert.GetRSAPublicKey())
            {
                var generator = X509SignatureGenerator.CreateForRSA(rsaPrivateKey, RSASignaturePadding.Pkcs1);
                var cert = CertificateBuilder.Create("CN=App Cert")
                    .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                    .SetIssuer(new X509Certificate2(signingCert.RawData))
                    .SetRSAPublicKey(rsaPublicKey)
                    .CreateForRSA(generator);
                Assert.NotNull(cert);
                WriteCertificate(cert, $"Default signed RSA cert with Public Key");
            }

            using (RSA rsaPrivateKey = signingCert.GetRSAPrivateKey())
            {
                var generator = X509SignatureGenerator.CreateForRSA(rsaPrivateKey, RSASignaturePadding.Pkcs1);
                var cert = CertificateBuilder.Create("CN=App Cert")
                    .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                    .SetIssuer(new X509Certificate2(signingCert.RawData))
                    .SetRSAKeySize(keyHashPair.KeySize)
                    .CreateForRSA(generator);
                Assert.NotNull(cert);
                WriteCertificate(cert, $"Default signed RSA cert");
            }

            // ensure invalid path throws argument exception
            Assert.Throws<NotSupportedException>(() => {
                using (RSA rsaPrivateKey = signingCert.GetRSAPrivateKey())
                {
                    var generator = X509SignatureGenerator.CreateForRSA(rsaPrivateKey, RSASignaturePadding.Pkcs1);
                    var cert = CertificateBuilder.Create("CN=App Cert")
                        .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                        .SetRSAKeySize(keyHashPair.KeySize)
                        .CreateForRSA(generator);
                }
            });
        }
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
