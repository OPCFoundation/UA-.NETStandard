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
    [TestFixture]
    [Category("Certificate")]
    [Category("RSA")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CertificateTestsForRSA
    {
        public const string Subject = "CN=Test Cert Subject, C=US, S=Arizona, O=OPC Foundation";

        [DatapointSource]
        public static readonly CertificateAsset[] CertificateTestCases =
        [
            .. AssetCollection<CertificateAsset>.CreateFromFiles(
                TestUtils.EnumerateTestAssets("*.?er"))
        ];

        [DatapointSource]
        public static readonly KeyHashPair[] KeyHashPairs = new KeyHashPairCollection
        {
            { 2048, HashAlgorithmName.SHA256 },
            { 3072, HashAlgorithmName.SHA384 },
            { 4096, HashAlgorithmName.SHA512 }
        }.ToArray();

        private static readonly string[] s_domainNames
            = ["mypc", "mypc.opcfoundation.org", "192.168.1.100"];

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
            foreach (CertificateAsset asset in CertificateTestCases)
            {
                asset?.Dispose();
            }
        }

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
                .AddExtension(
                    new X509SubjectAltNameExtension("urn:opcfoundation.org:mypc", s_domainNames));
            byte[] previousSerialNumber = null;
            foreach (KeyHashPair keyHash in KeyHashPairs)
            {
                using Certificate cert = builder
                    .SetHashAlgorithm(keyHash.HashAlgorithmName)
                    .SetRSAKeySize(keyHash.KeySize)
                    .CreateForRSA();
                Assert.That(cert, Is.Not.Null);
                WriteCertificate(
                    cert,
                    $"Default cert with RSA {keyHash.KeySize} {keyHash.HashAlgorithmName} signature.");
                Assert.That(
                    Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value),
                    Is.EqualTo(keyHash.HashAlgorithmName));
                // ensure serial numbers are different
                Assert.That(cert.GetSerialNumber(), Is.Not.EqualTo(previousSerialNumber));
                X509PfxUtils.VerifyRSAKeyPair(cert, cert, true);
                Assert.That(X509Utils.VerifySelfSigned(cert), Is.True);
                Assert.That(cert.IssuerName.Name, Is.EqualTo(cert.SubjectName.Name));
                Assert.That(cert.IssuerName.RawData, Is.EqualTo(cert.SubjectName.RawData));
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
            using Certificate cert = CertificateBuilder.Create(Subject).CreateForRSA();
            Assert.That(cert, Is.Not.Null);
            WriteCertificate(cert, "Default RSA cert");
            using (RSA privateKey = cert.GetRSAPrivateKey())
            {
                Assert.That(privateKey, Is.Not.Null);
                privateKey.ExportParameters(false);
                privateKey.ExportParameters(true);
            }
            using (RSA publicKey = cert.GetRSAPublicKey())
            {
                Assert.That(publicKey, Is.Not.Null);
                Assert.That(publicKey.KeySize, Is.EqualTo(X509Defaults.RSAKeySize));
                publicKey.ExportParameters(false);
            }
            Assert.That(cert.IssuerName.Name, Is.EqualTo(cert.SubjectName.Name));
            Assert.That(cert.IssuerName.RawData, Is.EqualTo(cert.SubjectName.RawData));
            Assert.That(
                Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value),
                Is.EqualTo(X509Defaults.HashAlgorithmName));
            Assert.That(DateTime.UtcNow, Is.GreaterThanOrEqualTo(cert.NotBefore));
            Assert.That(
                DateTime.UtcNow.AddMonths(X509Defaults.LifeTime),
                Is.GreaterThanOrEqualTo(cert.NotAfter.ToUniversalTime()));
            TestUtils.ValidateSelSignedBasicConstraints(cert);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.That(X509Utils.VerifySelfSigned(cert), Is.True);
        }

        [Theory]
        public void CreateSelfSignedForRSADefaultHashCustomKey(
            KeyHashPair keyHashPair,
            bool signOnly)
        {
            // default cert with custom key
            ICertificateBuilder builder = CertificateBuilder.Create(Subject);

            if (signOnly)
            {
                // Key usage for sign only
                const X509KeyUsageFlags keyUsageFlags =
                    X509KeyUsageFlags.KeyEncipherment |
                    X509KeyUsageFlags.DigitalSignature |
                    X509KeyUsageFlags.NonRepudiation;
                builder.AddExtension(new X509KeyUsageExtension(keyUsageFlags, true));
            }

            using Certificate cert = builder.SetRSAKeySize(keyHashPair.KeySize).CreateForRSA();
            WriteCertificate(cert, $"Default RSA {keyHashPair.KeySize} cert");

            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.That(cert.Subject, Is.EqualTo(Subject));
            Assert.That(cert.GetRSAPublicKey().KeySize, Is.EqualTo(keyHashPair.KeySize));
            Assert.That(
                Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value),
                Is.EqualTo(X509Defaults.HashAlgorithmName));
            TestUtils.ValidateSelSignedBasicConstraints(cert);
            Assert.That(cert.IssuerName.Name, Is.EqualTo(cert.SubjectName.Name));
            Assert.That(cert.IssuerName.RawData, Is.EqualTo(cert.SubjectName.RawData));
            Assert.That(X509Utils.VerifySelfSigned(cert), Is.True);
        }

        [Theory]
        public void CreateSelfSignedForRSACustomHashDefaultKey(KeyHashPair keyHashPair)
        {
            // default cert with custom HashAlgorithm
            using Certificate cert = CertificateBuilder
                .Create(Subject)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .CreateForRSA();
            Assert.That(cert, Is.Not.Null);
            WriteCertificate(cert, $"Default RSA {keyHashPair.HashAlgorithmName} cert");
            Assert.That(cert.Subject, Is.EqualTo(Subject));
            Assert.That(cert.GetRSAPublicKey().KeySize, Is.EqualTo(X509Defaults.RSAKeySize));
            Assert.That(
                Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value),
                Is.EqualTo(keyHashPair.HashAlgorithmName));
            TestUtils.ValidateSelSignedBasicConstraints(cert);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.That(X509Utils.VerifySelfSigned(cert), Is.True);
        }

        [Theory]
        public void CreateSelfSignedForRSAAllFields(KeyHashPair keyHashPair)
        {
            // set dates and extension
            const string applicationUri = "urn:opcfoundation.org:mypc";
            string[] domains = ["mypc", "mypc.opcfoundation.org", "192.168.1.100"];
            using Certificate cert = CertificateBuilder
                .Create(Subject)
                .SetNotBefore(DateTime.Today.AddYears(-1))
                .SetNotAfter(DateTime.Today.AddYears(25))
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, domains))
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .SetRSAKeySize(keyHashPair.KeySize)
                .CreateForRSA();
            Assert.That(cert, Is.Not.Null);
            WriteCertificate(
                cert,
                $"Default cert RSA {keyHashPair.KeySize} with modified lifetime and alt name extension");
            Assert.That(cert.Subject, Is.EqualTo(Subject));
            using (RSA privateKey = cert.GetRSAPrivateKey())
            {
                Assert.That(privateKey, Is.Not.Null);
                privateKey.ExportParameters(false);
                privateKey.ExportParameters(true);
            }
            using (RSA publicKey = cert.GetRSAPublicKey())
            {
                Assert.That(publicKey, Is.Not.Null);
                publicKey.ExportParameters(false);
            }
            Assert.That(cert.GetRSAPublicKey().KeySize, Is.EqualTo(keyHashPair.KeySize));
            Assert.That(
                Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value),
                Is.EqualTo(keyHashPair.HashAlgorithmName));
            TestUtils.ValidateSelSignedBasicConstraints(cert);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.That(X509Utils.VerifySelfSigned(cert), Is.True);
            CheckPEMWriter(cert);
        }

        [Theory]
        public void CreateCACertForRSA(KeyHashPair keyHashPair)
        {
            // create a CA cert
            using Certificate cert = CertificateBuilder
                .Create(Subject)
                .SetCAConstraint(-1)
                .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                .AddExtension(
                    X509Extensions.BuildX509CRLDistributionPoints("http://myca/mycert.crl"))
                .SetRSAKeySize(keyHashPair.KeySize)
                .CreateForRSA();
            Assert.That(cert, Is.Not.Null);
            WriteCertificate(
                cert,
                "Default cert with RSA {keyHashPair.KeySize} {keyHashPair.HashAlgorithmName} and CRL distribution points");
            Assert.That(cert.GetRSAPublicKey().KeySize, Is.EqualTo(keyHashPair.KeySize));
            Assert.That(
                Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value),
                Is.EqualTo(keyHashPair.HashAlgorithmName));
            X509BasicConstraintsExtension basicConstraintsExtension =
                cert.Extensions.FindExtension<X509BasicConstraintsExtension>();
            Assert.That(basicConstraintsExtension, Is.Not.Null);
            Assert.That(basicConstraintsExtension.CertificateAuthority, Is.True);
            Assert.That(basicConstraintsExtension.HasPathLengthConstraint, Is.False);
            X509Utils.VerifyRSAKeyPair(cert, cert, true);
            Assert.That(X509Utils.VerifySelfSigned(cert), Is.True);
            CheckPEMWriter(cert);
        }

        [Test]
        public void CreateRSADefaultWithSerialTest()
        {
            // default cert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CertificateBuilder.Create(Subject).SetSerialNumberLength(0).CreateForRSA());
            Assert.Throws<ArgumentOutOfRangeException>(() => CertificateBuilder
                .Create(Subject)
                .SetSerialNumberLength(X509Defaults.SerialNumberLengthMax + 1)
                .CreateForRSA());
            ICertificateBuilder builder = CertificateBuilder
                .Create(Subject)
                .SetSerialNumberLength(X509Defaults.SerialNumberLengthMax);

            // ensure every cert has a different serial number
            using Certificate cert1 = builder.CreateForRSA();
            using Certificate cert2 = builder.CreateForRSA();
            WriteCertificate(cert1, "Cert1 with max length serial number");
            WriteCertificate(cert2, "Cert2 with max length serial number");
            Assert.That(
                X509Defaults.SerialNumberLengthMax,
                Is.GreaterThanOrEqualTo(cert1.GetSerialNumber().Length));
            Assert.That(
                X509Defaults.SerialNumberLengthMax,
                Is.GreaterThanOrEqualTo(cert2.GetSerialNumber().Length));
            Assert.That(cert2.SerialNumber, Is.Not.EqualTo(cert1.SerialNumber));
        }

        [Test]
        public void CreateRSAManualSerialTest()
        {
            // default cert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CertificateBuilder.Create(Subject).SetSerialNumber([]).CreateForRSA());
            Assert.Throws<ArgumentOutOfRangeException>(() => CertificateBuilder
                .Create(Subject)
                .SetSerialNumber(new byte[X509Defaults.SerialNumberLengthMax + 1])
                .CreateForRSA());
            byte[] serial = new byte[X509Defaults.SerialNumberLengthMax];
            for (int i = 0; i < serial.Length; i++)
            {
                serial[i] = (byte)((i + 1) | 0x80);
            }

            // test if sign bit is cleared
            ICertificateBuilder builder = CertificateBuilder.Create(Subject)
                .SetSerialNumber(serial);
            serial[^1] &= 0x7f;
            Assert.That(builder.GetSerialNumber(), Is.EqualTo(serial));
            using Certificate cert1 = builder.CreateForRSA();
            WriteCertificate(cert1, "Cert1 with max length serial number");
            TestContext.Out.WriteLine($"Serial: {serial.ToHexString(true)}");
            Assert.That(cert1.GetSerialNumber(), Is.EqualTo(serial));
            Assert.That(cert1.GetSerialNumber(), Has.Length.EqualTo(X509Defaults.SerialNumberLengthMax));

            // clear sign bit
            builder.SetSerialNumberLength(X509Defaults.SerialNumberLengthMax);

            using Certificate cert2 = builder.CreateForRSA();
            WriteCertificate(cert2, "Cert2 with max length serial number");
            TestContext.Out.WriteLine($"Serial: {cert2.SerialNumber}");
            Assert.That(
                X509Defaults.SerialNumberLengthMax,
                Is.GreaterThanOrEqualTo(cert2.GetSerialNumber().Length));
            Assert.That(cert2.SerialNumber, Is.Not.EqualTo(cert1.SerialNumber));
        }

        [Test]
        public void CreateIssuerRSAWithSuppliedKeyPair()
        {
            Certificate issuer = null;
            using var rsaKeyPair = RSA.Create();
            // create cert with supplied keys
            var generator = X509SignatureGenerator.CreateForRSA(
                rsaKeyPair,
                RSASignaturePadding.Pkcs1);
            using (
                Certificate cert = CertificateBuilder
                    .Create("CN=Root Cert")
                    .SetCAConstraint(-1)
                    .SetRSAPublicKey(rsaKeyPair)
                    .CreateForRSA(generator))
            {
                Assert.That(cert, Is.Not.Null);
                issuer = CertificateFactory.Create(cert.RawData);
                WriteCertificate(cert, "Default root cert with supplied RSA cert");
                CheckPEMWriter(cert);
            }

            // now sign a cert with supplied private key
            using Certificate appCert = CertificateBuilder
                .Create("CN=App Cert")
                .SetIssuer(issuer)
                .CreateForRSA(generator);
            Assert.That(appCert, Is.Not.Null);
            WriteCertificate(appCert, "Signed RSA app cert");
            CheckPEMWriter(appCert);
        }

#if NETFRAMEWORK || NET5_0_OR_GREATER
        [Test]
        public void CreateIssuerRSACngWithSuppliedKeyPair()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Cng provider only available on windows");
            }
            Certificate issuer = null;
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA1416 // Validate platform compatibility
#pragma warning restore IDE0079 // Remove unnecessary suppression
            var cngKey = CngKey.Create(CngAlgorithm.Rsa);
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning restore CA1416 // Validate platform compatibility
#pragma warning restore IDE0079 // Remove unnecessary suppression
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA1416 // Validate platform compatibility
#pragma warning restore IDE0079 // Remove unnecessary suppression
            using RSA rsaKeyPair = new RSACng(cngKey);
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning restore CA1416 // Validate platform compatibility
#pragma warning restore IDE0079 // Remove unnecessary suppression
            // create cert with supplied keys
            var generator = X509SignatureGenerator.CreateForRSA(
                rsaKeyPair,
                RSASignaturePadding.Pkcs1);
            using (
                Certificate cert = CertificateBuilder
                    .Create("CN=Root Cert")
                    .SetCAConstraint(-1)
                    .SetRSAPublicKey(rsaKeyPair)
                    .CreateForRSA(generator))
            {
                Assert.That(cert, Is.Not.Null);
                issuer = CertificateFactory.Create(cert.RawData);
                WriteCertificate(cert, "Default root cert with supplied RSA cert");
                CheckPEMWriter(cert);
            }

            // now sign a cert with supplied private key
            using Certificate appCert = CertificateBuilder
                .Create("CN=App Cert")
                .SetIssuer(issuer)
                .CreateForRSA(generator);
            Assert.That(appCert, Is.Not.Null);
            Assert.That(appCert.IssuerName.Name, Is.EqualTo(issuer.SubjectName.Name));
            Assert.That(appCert.IssuerName.RawData, Is.EqualTo(issuer.SubjectName.RawData));
            WriteCertificate(appCert, "Signed RSA app cert");
            CheckPEMWriter(appCert);
        }
#endif

        [Theory]
        public void CreateForRSAWithGeneratorTest(KeyHashPair keyHashPair)
        {
            // default signing cert with custom key
            using Certificate signingCert = CertificateBuilder
                .Create(Subject)
                .SetCAConstraint()
                .SetHashAlgorithm(HashAlgorithmName.SHA512)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            WriteCertificate(
                signingCert,
                $"Signing RSA {signingCert.GetRSAPublicKey().KeySize} cert");

            using (RSA rsaPrivateKey = signingCert.GetRSAPrivateKey())
            {
                var generator = X509SignatureGenerator.CreateForRSA(
                    rsaPrivateKey,
                    RSASignaturePadding.Pkcs1);
                using Certificate issuer = CertificateFactory.Create(
                    signingCert.RawData);
                using Certificate cert = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetIssuer(issuer)
                    .CreateForRSA(generator);
                Assert.That(cert, Is.Not.Null);
                Assert.That(cert.IssuerName.Name, Is.EqualTo(issuer.SubjectName.Name));
                Assert.That(cert.IssuerName.RawData, Is.EqualTo(issuer.SubjectName.RawData));
                WriteCertificate(cert, "Default signed RSA cert");
                CheckPEMWriter(cert);
            }

            using (RSA rsaPrivateKey = signingCert.GetRSAPrivateKey())
            using (RSA rsaPublicKey = signingCert.GetRSAPublicKey())
            {
                var generator = X509SignatureGenerator.CreateForRSA(
                    rsaPrivateKey,
                    RSASignaturePadding.Pkcs1);
                using Certificate issuer = CertificateFactory.Create(
                    signingCert.RawData);
                using Certificate cert = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                    .SetIssuer(issuer)
                    .SetRSAPublicKey(rsaPublicKey)
                    .CreateForRSA(generator);
                Assert.That(cert, Is.Not.Null);
                WriteCertificate(cert, "Default signed RSA cert with Public Key");
                Assert.That(cert.IssuerName.Name, Is.EqualTo(issuer.SubjectName.Name));
                Assert.That(cert.IssuerName.RawData, Is.EqualTo(issuer.SubjectName.RawData));
                CheckPEMWriter(cert);
            }

            using (RSA rsaPrivateKey = signingCert.GetRSAPrivateKey())
            {
                var generator = X509SignatureGenerator.CreateForRSA(
                    rsaPrivateKey,
                    RSASignaturePadding.Pkcs1);
                using Certificate issuer = CertificateFactory.Create(
                    signingCert.RawData);
                using Certificate cert = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                    .SetIssuer(issuer)
                    .SetRSAKeySize(keyHashPair.KeySize)
                    .CreateForRSA(generator);
                Assert.That(cert, Is.Not.Null);
                WriteCertificate(cert, "Default signed RSA cert");
                Assert.That(cert.IssuerName.Name, Is.EqualTo(issuer.SubjectName.Name));
                Assert.That(cert.IssuerName.RawData, Is.EqualTo(issuer.SubjectName.RawData));
                CheckPEMWriter(cert);
            }

            // ensure invalid path throws argument exception
            Assert.Throws<NotSupportedException>(() =>
            {
                using RSA rsaPrivateKey = signingCert.GetRSAPrivateKey();
                var generator = X509SignatureGenerator.CreateForRSA(
                    rsaPrivateKey,
                    RSASignaturePadding.Pkcs1);
                _ = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetHashAlgorithm(keyHashPair.HashAlgorithmName)
                    .SetRSAKeySize(keyHashPair.KeySize)
                    .CreateForRSA(generator);
            });

            CheckPEMWriter(signingCert, password: "123".ToCharArray());
        }

        private static void WriteCertificate(Certificate cert, string message)
        {
            TestContext.Out.WriteLine(message);
            TestContext.Out.WriteLine(cert);
            foreach (X509Extension ext in cert.Extensions)
            {
                TestContext.Out.WriteLine(ext.Format(false));
            }
        }

        private static void CheckPEMWriter(Certificate certificate, ReadOnlySpan<char> password = default)
        {
            PEMWriter.ExportCertificateAsPEM(certificate);
            if (certificate.HasPrivateKey)
            {
#if NETFRAMEWORK
                // The implementation based on bouncy castle has no support to export with password
                password = null;
#endif
                PEMWriter.ExportPrivateKeyAsPEM(certificate, password);
#if NET5_0_OR_GREATER
                PEMWriter.ExportRSAPrivateKeyAsPEM(certificate);
#endif
            }
        }
    }
}
