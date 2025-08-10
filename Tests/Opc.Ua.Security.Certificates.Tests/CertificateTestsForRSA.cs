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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
        public const string Subject = "CN=Test Cert Subject, C=US, S=Arizona, O=OPC Foundation";

        [DatapointSource]
        public static readonly CertificateAsset[] CertificateTestCases =
        [
            .. AssetCollection<CertificateAsset>.CreateFromFiles(TestUtils.EnumerateTestAssets("*.?er")),
        ];

        [DatapointSource]
        public static readonly KeyHashPair[] KeyHashPairs = new KeyHashPairCollection
        {
            { 2048, HashAlgorithmName.SHA256 },
            { 3072, HashAlgorithmName.SHA384 },
            { 4096, HashAlgorithmName.SHA512 },
        }.ToArray();
        private static readonly string[] s_domainNames = ["mypc", "mypc.opcfoundation.org", "192.168.1.100"];

        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        protected void OneTimeSetUp() { }

        /// <summary>
        /// Clean up the Test PKI folder
        /// </summary>
        [OneTimeTearDown]
        protected void OneTimeTearDown() { }

        /// <summary>
        /// Verify self signed app certs. Use one builder to create multiple certs.
        /// </summary>
        [Test]
        public void VerifyOneSelfSignedAppCertForAll()
        {
            ICertificateBuilder builder = CertificateBuilder
                .Create(Subject)
                .SetNotBefore(DateTime.Today.AddYears(-1))
                .SetNotAfter(DateTime.Today.AddYears(25))
                .AddExtension(new X509SubjectAltNameExtension("urn:opcfoundation.org:mypc", s_domainNames));
            byte[] previousSerialNumber = null;
            foreach (KeyHashPair keyHash in KeyHashPairs)
            {
                using X509Certificate2 cert = builder
                    .SetHashAlgorithm(keyHash.HashAlgorithmName)
                    .SetRSAKeySize(keyHash.KeySize)
                    .CreateForRSA();
                Assert.NotNull(cert);
                WriteCertificate(
                    cert,
                    $"Default cert with RSA {keyHash.KeySize} {keyHash.HashAlgorithmName} signature."
                );
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

        /// <summary>
        /// Create the default RSA certificate.
        /// </summary>
        [Test]
        public void CreateSelfSignedForRSADefaultTest()
        {
            // default cert
            using X509Certificate2 cert = CertificateBuilder.Create(Subject).CreateForRSA();
            Assert.NotNull(cert);
            WriteCertificate(cert, "Default RSA cert");
            using (RSA privateKey = cert.GetRSAPrivateKey())
            {
                Assert.NotNull(privateKey);
                privateKey.ExportParameters(false);
                privateKey.ExportParameters(true);
            }
            using (RSA publicKey = cert.GetRSAPublicKey())
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
            TestUtils.ValidateSelSignedBasicConstraints(cert);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert));
        }

        [Theory]
        public void CreateSelfSignedForRSADefaultHashCustomKey(KeyHashPair keyHashPair, bool signOnly)
        {
            // default cert with custom key
            ICertificateBuilder builder = CertificateBuilder.Create(Subject);

            if (signOnly)
            {
                // Key usage for sign only
                const X509KeyUsageFlags keyUsageFlags =
                    X509KeyUsageFlags.KeyEncipherment
                    | X509KeyUsageFlags.DigitalSignature
                    | X509KeyUsageFlags.NonRepudiation;
                builder.AddExtension(new X509KeyUsageExtension(keyUsageFlags, true));
            }

            using X509Certificate2 cert = builder.SetRSAKeySize(keyHashPair.KeySize).CreateForRSA();
            WriteCertificate(cert, $"Default RSA {keyHashPair.KeySize} cert");

            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.AreEqual(Subject, cert.Subject);
            Assert.AreEqual(keyHashPair.KeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(X509Defaults.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            TestUtils.ValidateSelSignedBasicConstraints(cert);
            Assert.AreEqual(cert.SubjectName.Name, cert.IssuerName.Name);
            Assert.AreEqual(cert.SubjectName.RawData, cert.IssuerName.RawData);
            Assert.True(X509Utils.VerifySelfSigned(cert));
        }

        [Theory]
        public void CreateSelfSignedForRSACustomHashDefaultKey(KeyHashPair keyHashPair)
        {
            // default cert with custom HashAlgorithm
            X509Certificate2 cert = CertificateBuilder
                .Create(Subject)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .CreateForRSA();
            Assert.NotNull(cert);
            WriteCertificate(cert, $"Default RSA {keyHashPair.HashAlgorithmName} cert");
            Assert.AreEqual(Subject, cert.Subject);
            Assert.AreEqual(X509Defaults.RSAKeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(keyHashPair.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            TestUtils.ValidateSelSignedBasicConstraints(cert);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert));
        }

        [Theory]
        public void CreateSelfSignedForRSAAllFields(KeyHashPair keyHashPair)
        {
            // set dates and extension
            const string applicationUri = "urn:opcfoundation.org:mypc";
            string[] domains = ["mypc", "mypc.opcfoundation.org", "192.168.1.100"];
            X509Certificate2 cert = CertificateBuilder
                .Create(Subject)
                .SetNotBefore(DateTime.Today.AddYears(-1))
                .SetNotAfter(DateTime.Today.AddYears(25))
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, domains))
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .SetRSAKeySize(keyHashPair.KeySize)
                .CreateForRSA();
            Assert.NotNull(cert);
            WriteCertificate(
                cert,
                $"Default cert RSA {keyHashPair.KeySize} with modified lifetime and alt name extension"
            );
            Assert.AreEqual(Subject, cert.Subject);
            using (RSA privateKey = cert.GetRSAPrivateKey())
            {
                Assert.NotNull(privateKey);
                privateKey.ExportParameters(false);
                privateKey.ExportParameters(true);
            }
            using (RSA publicKey = cert.GetRSAPublicKey())
            {
                Assert.NotNull(publicKey);
                publicKey.ExportParameters(false);
            }
            Assert.AreEqual(keyHashPair.KeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(keyHashPair.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            TestUtils.ValidateSelSignedBasicConstraints(cert);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert));
            CheckPEMWriter(cert);
        }

        [Theory]
        public void CreateCACertForRSA(KeyHashPair keyHashPair)
        {
            // create a CA cert
            using X509Certificate2 cert = CertificateBuilder
                .Create(Subject)
                .SetCAConstraint(-1)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .AddExtension(X509Extensions.BuildX509CRLDistributionPoints("http://myca/mycert.crl"))
                .SetRSAKeySize(keyHashPair.KeySize)
                .CreateForRSA();
            Assert.NotNull(cert);
            WriteCertificate(
                cert,
                "Default cert with RSA {keyHashPair.KeySize} {keyHashPair.HashAlgorithmName} and CRL distribution points"
            );
            Assert.AreEqual(keyHashPair.KeySize, cert.GetRSAPublicKey().KeySize);
            Assert.AreEqual(keyHashPair.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            X509BasicConstraintsExtension basicConstraintsExtension =
                cert.Extensions.FindExtension<X509BasicConstraintsExtension>();
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
            NUnit.Framework.Assert.Throws<ArgumentOutOfRangeException>(
                () => CertificateBuilder.Create(Subject).SetSerialNumberLength(0).CreateForRSA());
            NUnit.Framework.Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                CertificateBuilder
                    .Create(Subject)
                    .SetSerialNumberLength(X509Defaults.SerialNumberLengthMax + 1)
                    .CreateForRSA();
            });
            ICertificateBuilder builder = CertificateBuilder
                .Create(Subject)
                .SetSerialNumberLength(X509Defaults.SerialNumberLengthMax);

            // ensure every cert has a different serial number
            X509Certificate2 cert1 = builder.CreateForRSA();
            X509Certificate2 cert2 = builder.CreateForRSA();
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
            NUnit.Framework.Assert.Throws<ArgumentOutOfRangeException>(
                () => CertificateBuilder.Create(Subject).SetSerialNumber([]).CreateForRSA());
            NUnit.Framework.Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                CertificateBuilder
                    .Create(Subject)
                    .SetSerialNumber(new byte[X509Defaults.SerialNumberLengthMax + 1])
                    .CreateForRSA();
            });
            byte[] serial = new byte[X509Defaults.SerialNumberLengthMax];
            for (int i = 0; i < serial.Length; i++)
            {
                serial[i] = (byte)((i + 1) | 0x80);
            }

            // test if sign bit is cleared
            ICertificateBuilder builder = CertificateBuilder.Create(Subject).SetSerialNumber(serial);
            serial[^1] &= 0x7f;
            Assert.AreEqual(serial, builder.GetSerialNumber());
            X509Certificate2 cert1 = builder.CreateForRSA();
            WriteCertificate(cert1, "Cert1 with max length serial number");
            TestContext.Out.WriteLine($"Serial: {serial.ToHexString(true)}");
            Assert.AreEqual(serial, cert1.GetSerialNumber());
            Assert.AreEqual(X509Defaults.SerialNumberLengthMax, cert1.GetSerialNumber().Length);

            // clear sign bit
            builder.SetSerialNumberLength(X509Defaults.SerialNumberLengthMax);

            X509Certificate2 cert2 = builder.CreateForRSA();
            WriteCertificate(cert2, "Cert2 with max length serial number");
            TestContext.Out.WriteLine($"Serial: {cert2.SerialNumber}");
            Assert.GreaterOrEqual(X509Defaults.SerialNumberLengthMax, cert2.GetSerialNumber().Length);
            Assert.AreNotEqual(cert1.SerialNumber, cert2.SerialNumber);
        }

        [Test]
        public void CreateIssuerRSAWithSuppliedKeyPair()
        {
            X509Certificate2 issuer = null;
            using var rsaKeyPair = RSA.Create();
            // create cert with supplied keys
            var generator = X509SignatureGenerator.CreateForRSA(rsaKeyPair, RSASignaturePadding.Pkcs1);
            using (
                X509Certificate2 cert = CertificateBuilder
                    .Create("CN=Root Cert")
                    .SetCAConstraint(-1)
                    .SetRSAPublicKey(rsaKeyPair)
                    .CreateForRSA(generator)
            )
            {
                Assert.NotNull(cert);
                issuer = X509CertificateLoader.LoadCertificate(cert.RawData);
                WriteCertificate(cert, "Default root cert with supplied RSA cert");
                CheckPEMWriter(cert);
            }

            // now sign a cert with supplied private key
            using X509Certificate2 appCert = CertificateBuilder
                .Create("CN=App Cert")
                .SetIssuer(issuer)
                .CreateForRSA(generator);
            Assert.NotNull(appCert);
            WriteCertificate(appCert, "Signed RSA app cert");
            CheckPEMWriter(appCert);
        }

#if NETFRAMEWORK || NETCOREAPP3_1_OR_GREATER
        [Test]
        [SuppressMessage(
            "Interoperability",
            "CA1416: Validate platform compatibility",
            Justification = "Test is ignored."
        )]
        public void CreateIssuerRSACngWithSuppliedKeyPair()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NUnit.Framework.Assert.Ignore("Cng provider only available on windows");
            }
            X509Certificate2 issuer = null;
            var cngKey = CngKey.Create(CngAlgorithm.Rsa);
            using RSA rsaKeyPair = new RSACng(cngKey);
            // create cert with supplied keys
            var generator = X509SignatureGenerator.CreateForRSA(rsaKeyPair, RSASignaturePadding.Pkcs1);
            using (
                X509Certificate2 cert = CertificateBuilder
                    .Create("CN=Root Cert")
                    .SetCAConstraint(-1)
                    .SetRSAPublicKey(rsaKeyPair)
                    .CreateForRSA(generator)
            )
            {
                Assert.NotNull(cert);
                issuer = X509CertificateLoader.LoadCertificate(cert.RawData);
                WriteCertificate(cert, "Default root cert with supplied RSA cert");
                CheckPEMWriter(cert);
            }

            // now sign a cert with supplied private key
            using X509Certificate2 appCert = CertificateBuilder
                .Create("CN=App Cert")
                .SetIssuer(issuer)
                .CreateForRSA(generator);
            Assert.NotNull(appCert);
            Assert.AreEqual(issuer.SubjectName.Name, appCert.IssuerName.Name);
            Assert.AreEqual(issuer.SubjectName.RawData, appCert.IssuerName.RawData);
            WriteCertificate(appCert, "Signed RSA app cert");
            CheckPEMWriter(appCert);
        }
#endif

        [Theory]
        public void CreateForRSAWithGeneratorTest(KeyHashPair keyHashPair)
        {
            // default signing cert with custom key
            using X509Certificate2 signingCert = CertificateBuilder
                .Create(Subject)
                .SetCAConstraint()
                .SetHashAlgorithm(HashAlgorithmName.SHA512)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            WriteCertificate(signingCert, $"Signing RSA {signingCert.GetRSAPublicKey().KeySize} cert");

            using (RSA rsaPrivateKey = signingCert.GetRSAPrivateKey())
            {
                var generator = X509SignatureGenerator.CreateForRSA(rsaPrivateKey, RSASignaturePadding.Pkcs1);
                using X509Certificate2 issuer = X509CertificateLoader.LoadCertificate(signingCert.RawData);
                using X509Certificate2 cert = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetIssuer(issuer)
                    .CreateForRSA(generator);
                Assert.NotNull(cert);
                Assert.AreEqual(issuer.SubjectName.Name, cert.IssuerName.Name);
                Assert.AreEqual(issuer.SubjectName.RawData, cert.IssuerName.RawData);
                WriteCertificate(cert, "Default signed RSA cert");
                CheckPEMWriter(cert);
            }

            using (RSA rsaPrivateKey = signingCert.GetRSAPrivateKey())
            using (RSA rsaPublicKey = signingCert.GetRSAPublicKey())
            {
                var generator = X509SignatureGenerator.CreateForRSA(rsaPrivateKey, RSASignaturePadding.Pkcs1);
                using X509Certificate2 issuer = X509CertificateLoader.LoadCertificate(signingCert.RawData);
                using X509Certificate2 cert = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                    .SetIssuer(issuer)
                    .SetRSAPublicKey(rsaPublicKey)
                    .CreateForRSA(generator);
                Assert.NotNull(cert);
                WriteCertificate(cert, "Default signed RSA cert with Public Key");
                Assert.AreEqual(issuer.SubjectName.Name, cert.IssuerName.Name);
                Assert.AreEqual(issuer.SubjectName.RawData, cert.IssuerName.RawData);
                CheckPEMWriter(cert);
            }

            using (RSA rsaPrivateKey = signingCert.GetRSAPrivateKey())
            {
                var generator = X509SignatureGenerator.CreateForRSA(rsaPrivateKey, RSASignaturePadding.Pkcs1);
                using X509Certificate2 issuer = X509CertificateLoader.LoadCertificate(signingCert.RawData);
                using X509Certificate2 cert = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                    .SetIssuer(issuer)
                    .SetRSAKeySize(keyHashPair.KeySize)
                    .CreateForRSA(generator);
                Assert.NotNull(cert);
                WriteCertificate(cert, "Default signed RSA cert");
                Assert.AreEqual(issuer.SubjectName.Name, cert.IssuerName.Name);
                Assert.AreEqual(issuer.SubjectName.RawData, cert.IssuerName.RawData);
                CheckPEMWriter(cert);
            }

            // ensure invalid path throws argument exception
            NUnit.Framework.Assert.Throws<NotSupportedException>(() =>
            {
                using RSA rsaPrivateKey = signingCert.GetRSAPrivateKey();
                var generator = X509SignatureGenerator.CreateForRSA(rsaPrivateKey, RSASignaturePadding.Pkcs1);
                _ = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                    .SetRSAKeySize(keyHashPair.KeySize)
                    .CreateForRSA(generator);
            });

            CheckPEMWriter(signingCert, password: "123");
        }

        private static void WriteCertificate(X509Certificate2 cert, string message)
        {
            TestContext.Out.WriteLine(message);
            TestContext.Out.WriteLine(cert);
            foreach (X509Extension ext in cert.Extensions)
            {
                TestContext.Out.WriteLine(ext.Format(false));
            }
        }

        private static void CheckPEMWriter(X509Certificate2 certificate, string password = null)
        {
            PEMWriter.ExportCertificateAsPEM(certificate);
            if (certificate.HasPrivateKey)
            {
#if NETFRAMEWORK || NETCOREAPP2_1 || !ECC_SUPPORT
                // The implementation based on bouncy castle has no support to export with password
                password = null;
#endif
                PEMWriter.ExportPrivateKeyAsPEM(certificate, password);
#if NETCOREAPP3_1_OR_GREATER && ECC_SUPPORT
                PEMWriter.ExportRSAPrivateKeyAsPEM(certificate);
#endif
            }
        }
    }
}
