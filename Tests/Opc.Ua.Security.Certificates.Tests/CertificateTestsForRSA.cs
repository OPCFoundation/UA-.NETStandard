/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Tests for the CertificateBuilder class.
    /// </summary>
    [TestFixture, Category("Certificate"), Category("RSA")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CertificateTestsForRSA
    {
        #region DataPointSources
        public const string Subject = "CN=Test Cert Subject, C=US, S=Arizona, O=OPC Foundation";

        [DatapointSource]
        public static readonly CertificateAsset[] CertificateTestCases = new AssetCollection<CertificateAsset>(TestUtils.EnumerateTestAssets("*.?er")).ToArray();

        [DatapointSource]
        public static readonly KeyHashPair[] KeyHashPairs = new KeyHashPairCollection {
            { 2048, HashAlgorithmName.SHA256 },
            { 3072, HashAlgorithmName.SHA384 },
            { 4096, HashAlgorithmName.SHA512 } }.ToArray();
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
                using (var cert = builder
                    .SetHashAlgorithm(keyHash.HashAlgorithmName)
                    .SetRSAKeySize(keyHash.KeySize)
                    .CreateForRSA())
                {
                    Assert.NotNull(cert);
                    WriteCertificate(cert, $"Default cert with RSA {keyHash.KeySize} {keyHash.HashAlgorithmName} signature.");
                    Assert.AreEqual(keyHash.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
                    // ensure serial numbers are different
                    Assert.AreNotEqual(previousSerialNumber, cert.GetSerialNumber());
                    X509PfxUtils.VerifyRSAKeyPair(cert, cert, true);
                    Assert.True(X509Utils.VerifySelfSigned(cert));
                    Assert.AreEqual(cert.SubjectName.Name, cert.IssuerName.Name);
                    Assert.AreEqual(cert.SubjectName.RawData, cert.IssuerName.RawData);
                    CheckPEMWriter(cert);
                }
            }
        }

        /// <summary>
        /// Create the default RSA certificate.
        /// </summary>
        [Test]
        public void CreateSelfSignedForRSADefaultTest()
        {
            // default cert
            X509Certificate2 cert = CertificateBuilder.Create(Subject).CreateForRSA();
            Assert.NotNull(cert);
            WriteCertificate(cert, "Default RSA cert");
            using (var privateKey = cert.GetRSAPrivateKey())
            {
                Assert.NotNull(privateKey);
                privateKey.ExportParameters(false);
                privateKey.ExportParameters(true);
            }
            using (var publicKey = cert.GetRSAPublicKey())
            {
                Assert.NotNull(publicKey);
                Assert.AreEqual(X509Defaults.RSAKeySize, publicKey.KeySize);
                publicKey.ExportParameters(false);
            }
            Assert.AreEqual(cert.SubjectName.Name, cert.IssuerName.Name);
            Assert.AreEqual(cert.SubjectName.RawData, cert.IssuerName.RawData);
            Assert.AreEqual(X509Defaults.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            Assert.GreaterOrEqual(DateTime.UtcNow, cert.NotBefore);
            Assert.GreaterOrEqual(DateTime.UtcNow.AddMonths(X509Defaults.LifeTime), cert.NotAfter.ToUniversalTime());
            var basicConstraintsExtension = X509Extensions.FindExtension<X509BasicConstraintsExtension>(cert.Extensions);
            Assert.NotNull(basicConstraintsExtension);
            Assert.True(basicConstraintsExtension.CertificateAuthority);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert));
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
            Assert.AreEqual(X509Defaults.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            var basicConstraintsExtension = X509Extensions.FindExtension<X509BasicConstraintsExtension>(cert.Extensions);
            Assert.NotNull(basicConstraintsExtension);
            Assert.True(basicConstraintsExtension.CertificateAuthority);
            Assert.AreEqual(cert.SubjectName.Name, cert.IssuerName.Name);
            Assert.AreEqual(cert.SubjectName.RawData, cert.IssuerName.RawData);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert));
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
            Assert.AreEqual(X509Defaults.RSAKeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(keyHashPair.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            var basicConstraintsExtension = X509Extensions.FindExtension<X509BasicConstraintsExtension>(cert.Extensions);
            Assert.NotNull(basicConstraintsExtension);
            Assert.True(basicConstraintsExtension.CertificateAuthority);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert));
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
            using (var privateKey = cert.GetRSAPrivateKey())
            {
                Assert.NotNull(privateKey);
                privateKey.ExportParameters(false);
                privateKey.ExportParameters(true);
            }
            using (var publicKey = cert.GetRSAPublicKey())
            {
                Assert.NotNull(publicKey);
                publicKey.ExportParameters(false);
            }
            Assert.AreEqual(keyHashPair.KeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(keyHashPair.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            var basicConstraintsExtension = X509Extensions.FindExtension<X509BasicConstraintsExtension>(cert.Extensions);
            Assert.NotNull(basicConstraintsExtension);
            Assert.True(basicConstraintsExtension.CertificateAuthority);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert));
            CheckPEMWriter(cert);
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
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert));
            CheckPEMWriter(cert);
        }

        [Test]
        public void CreateRSADefaultWithSerialTest()
        {
            // default cert
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    CertificateBuilder.Create(Subject)
                    .SetSerialNumberLength(0)
                    .CreateForRSA();
                }
            );
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    CertificateBuilder.Create(Subject)
                    .SetSerialNumberLength(X509Defaults.SerialNumberLengthMax + 1)
                    .CreateForRSA();
                }
            );
            var builder = CertificateBuilder.Create(Subject)
                .SetSerialNumberLength(X509Defaults.SerialNumberLengthMax);

            // ensure every cert has a different serial number
            var cert1 = builder.CreateForRSA();
            var cert2 = builder.CreateForRSA();
            WriteCertificate(cert1, "Cert1 with max length serial number");
            WriteCertificate(cert2, "Cert2 with max length serial number");
            Assert.GreaterOrEqual(X509Defaults.SerialNumberLengthMax, cert1.GetSerialNumber().Length);
            Assert.GreaterOrEqual(X509Defaults.SerialNumberLengthMax, cert2.GetSerialNumber().Length);
            Assert.AreNotEqual(cert1.SerialNumber, cert2.SerialNumber);
        }

        [Test]
        public void CreateRSAManualSerialTest()
        {
            // default cert
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    CertificateBuilder.Create(Subject)
                    .SetSerialNumber(Array.Empty<byte>())
                    .CreateForRSA();
                }
            );
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    CertificateBuilder.Create(Subject)
                    .SetSerialNumber(new byte[X509Defaults.SerialNumberLengthMax + 1])
                    .CreateForRSA();
                }
            );
            var serial = new byte[X509Defaults.SerialNumberLengthMax];
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
            TestContext.Out.WriteLine($"Serial: {serial.ToHexString(true)}");
            Assert.AreEqual(serial, cert1.GetSerialNumber());
            Assert.AreEqual(X509Defaults.SerialNumberLengthMax, cert1.GetSerialNumber().Length);

            // clear sign bit
            builder.SetSerialNumberLength(X509Defaults.SerialNumberLengthMax);

            var cert2 = builder.CreateForRSA();
            WriteCertificate(cert2, "Cert2 with max length serial number");
            TestContext.Out.WriteLine($"Serial: {cert2.SerialNumber}");
            Assert.GreaterOrEqual(X509Defaults.SerialNumberLengthMax, cert2.GetSerialNumber().Length);
            Assert.AreNotEqual(cert1.SerialNumber, cert2.SerialNumber);
        }

        [Test]
        public void CreateIssuerRSAWithSuppliedKeyPair()
        {
            X509Certificate2 issuer = null;
            using (RSA rsaKeyPair = RSA.Create())
            {
                // create cert with supplied keys
                var generator = X509SignatureGenerator.CreateForRSA(rsaKeyPair, RSASignaturePadding.Pkcs1);
                using (var cert = CertificateBuilder.Create("CN=Root Cert")
                    .SetCAConstraint(-1)
                    .SetRSAPublicKey(rsaKeyPair)
                    .CreateForRSA(generator))
                {
                    Assert.NotNull(cert);
                    issuer = new X509Certificate2(cert.RawData);
                    WriteCertificate(cert, "Default root cert with supplied RSA cert");
                    CheckPEMWriter(cert);
                }

                // now sign a cert with supplied private key
                using (var appCert = CertificateBuilder.Create("CN=App Cert")
                    .SetIssuer(issuer)
                    .CreateForRSA(generator))
                {
                    Assert.NotNull(appCert);
                    WriteCertificate(appCert, "Signed RSA app cert");
                    CheckPEMWriter(appCert);
                }
            }
        }

#if NETFRAMEWORK || NETCOREAPP3_1
        [Test]
        public void CreateIssuerRSACngWithSuppliedKeyPair()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Cng provider only available on windows");
            }
            X509Certificate2 issuer = null;
            CngKey cngKey = CngKey.Create(CngAlgorithm.Rsa);
            using (RSA rsaKeyPair = new RSACng(cngKey))
            {
                // create cert with supplied keys
                var generator = X509SignatureGenerator.CreateForRSA(rsaKeyPair, RSASignaturePadding.Pkcs1);
                using (var cert = CertificateBuilder.Create("CN=Root Cert")
                    .SetCAConstraint(-1)
                    .SetRSAPublicKey(rsaKeyPair)
                    .CreateForRSA(generator))
                {
                    Assert.NotNull(cert);
                    issuer = new X509Certificate2(cert.RawData);
                    WriteCertificate(cert, "Default root cert with supplied RSA cert");
                    CheckPEMWriter(cert);
                }

                // now sign a cert with supplied private key
                using (var appCert = CertificateBuilder.Create("CN=App Cert")
                    .SetIssuer(issuer)
                    .CreateForRSA(generator))
                {
                    Assert.NotNull(appCert);
                    Assert.AreEqual(issuer.SubjectName.Name, appCert.IssuerName.Name);
                    Assert.AreEqual(issuer.SubjectName.RawData, appCert.IssuerName.RawData);
                    WriteCertificate(appCert, "Signed RSA app cert");
                    CheckPEMWriter(appCert);
                }
            }
        }
#endif

        [Theory]
        public void CreateForRSAWithGeneratorTest(
            KeyHashPair keyHashPair
            )
        {
            // default signing cert with custom key
            using (X509Certificate2 signingCert = CertificateBuilder.Create(Subject)
                .SetCAConstraint()
                .SetHashAlgorithm(HashAlgorithmName.SHA512)
                .SetRSAKeySize(2048)
                .CreateForRSA())
            {
                WriteCertificate(signingCert, $"Signing RSA {signingCert.GetRSAPublicKey().KeySize} cert");

                using (RSA rsaPrivateKey = signingCert.GetRSAPrivateKey())
                {
                    var generator = X509SignatureGenerator.CreateForRSA(rsaPrivateKey, RSASignaturePadding.Pkcs1);
                    using (var issuer = new X509Certificate2(signingCert.RawData))
                    using (var cert = CertificateBuilder.Create("CN=App Cert")
                        .SetIssuer(issuer)
                        .CreateForRSA(generator))
                    {
                        Assert.NotNull(cert);
                        Assert.AreEqual(issuer.SubjectName.Name, cert.IssuerName.Name);
                        Assert.AreEqual(issuer.SubjectName.RawData, cert.IssuerName.RawData);
                        WriteCertificate(cert, "Default signed RSA cert");
                        CheckPEMWriter(cert);
                    }
                }

                using (RSA rsaPrivateKey = signingCert.GetRSAPrivateKey())
                using (RSA rsaPublicKey = signingCert.GetRSAPublicKey())
                {
                    var generator = X509SignatureGenerator.CreateForRSA(rsaPrivateKey, RSASignaturePadding.Pkcs1);
                    using (var issuer = new X509Certificate2(signingCert.RawData))
                    using (var cert = CertificateBuilder.Create("CN=App Cert")
                        .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                        .SetIssuer(issuer)
                        .SetRSAPublicKey(rsaPublicKey)
                        .CreateForRSA(generator))
                    {
                        Assert.NotNull(cert);
                        WriteCertificate(cert, "Default signed RSA cert with Public Key");
                        Assert.AreEqual(issuer.SubjectName.Name, cert.IssuerName.Name);
                        Assert.AreEqual(issuer.SubjectName.RawData, cert.IssuerName.RawData);
                        CheckPEMWriter(cert);
                    }
                }

                using (RSA rsaPrivateKey = signingCert.GetRSAPrivateKey())
                {
                    var generator = X509SignatureGenerator.CreateForRSA(rsaPrivateKey, RSASignaturePadding.Pkcs1);
                    using (var issuer = new X509Certificate2(signingCert.RawData))
                    using (var cert = CertificateBuilder.Create("CN=App Cert")
                        .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                        .SetIssuer(issuer)
                        .SetRSAKeySize(keyHashPair.KeySize)
                        .CreateForRSA(generator))
                    {
                        Assert.NotNull(cert);
                        WriteCertificate(cert, "Default signed RSA cert");
                        Assert.AreEqual(issuer.SubjectName.Name, cert.IssuerName.Name);
                        Assert.AreEqual(issuer.SubjectName.RawData, cert.IssuerName.RawData);
                        CheckPEMWriter(cert);
                    }
                }

                // ensure invalid path throws argument exception
                Assert.Throws<NotSupportedException>(() =>
                {
                    using (RSA rsaPrivateKey = signingCert.GetRSAPrivateKey())
                    {
                        var generator = X509SignatureGenerator.CreateForRSA(rsaPrivateKey, RSASignaturePadding.Pkcs1);
                        _ = CertificateBuilder.Create("CN=App Cert")
                            .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                            .SetRSAKeySize(keyHashPair.KeySize)
                            .CreateForRSA(generator);
                    }
                });

                CheckPEMWriter(signingCert, password: "123");
            }
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

        private void CheckPEMWriter(X509Certificate2 certificate, string password = null)
        {
            PEMWriter.ExportCertificateAsPEM(certificate);
            if (certificate.HasPrivateKey)
            {
#if NETFRAMEWORK || NETCOREAPP2_1
                // The implementation based on bouncy castle has no support to export with password
                password = null;
#endif
                PEMWriter.ExportPrivateKeyAsPEM(certificate, password);
#if NETCOREAPP3_1_OR_GREATER
                PEMWriter.ExportRSAPrivateKeyAsPEM(certificate);
#endif
            }
        }
        #endregion

        #region Private Fields
        #endregion
    }
}
