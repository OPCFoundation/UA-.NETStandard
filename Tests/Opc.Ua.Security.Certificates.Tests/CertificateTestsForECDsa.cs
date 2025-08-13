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

#if ECC_SUPPORT
using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
#if NETFRAMEWORK
using Org.BouncyCastle.X509;
#endif

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Tests for the CertificateBuilder class.
    /// </summary>
    [TestFixture]
    [Category("Certificate")]
    [Category("ECDsa")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CertificateTestsForECDsa
    {
        public const string Subject = "CN=Test Cert Subject, O=OPC Foundation";

        [DatapointSource]
        public static readonly CertificateAsset[] CertificateTestCases =
        [
            .. AssetCollection<CertificateAsset>.CreateFromFiles(
                TestUtils.EnumerateTestAssets("*.?er"))
        ];

        [DatapointSource]
        public static readonly ECCurveHashPair[] ECCurveHashPairs = GetECCurveHashPairs();

        private static readonly string[] s_domainNames
            = ["mypc", "mypc.opcfoundation.org", "192.168.1.100"];

        private static readonly string[] s_distributionPoints
            = ["http://myca/mycert.crl", "http://myaltca/mycert.crl"];

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
            foreach (ECCurveHashPair eCCurveHash in ECCurveHashPairs)
            {
                if (!eCCurveHash.Curve.IsNamed)
                {
                    continue;
                }

                using X509Certificate2 cert = builder
                    .SetHashAlgorithm(eCCurveHash.HashAlgorithmName)
                    .SetECCurve(eCCurveHash.Curve)
                    .CreateForECDsa();
                Assert.NotNull(cert);
                WriteCertificate(
                    cert,
                    $"Default cert with ECDsa {eCCurveHash.Curve.Oid.FriendlyName} {eCCurveHash.HashAlgorithmName} signature.");
                Assert.AreEqual(
                    eCCurveHash.HashAlgorithmName,
                    Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
                // ensure serial numbers are different
                Assert.AreNotEqual(previousSerialNumber, cert.GetSerialNumber());
                X509PfxUtils.VerifyECDsaKeyPair(cert, cert, true);
                Assert.True(X509Utils.VerifySelfSigned(cert));
                CheckPEMWriterReader(cert);
            }
        }

        /// <summary>
        /// Create the default ECDsa certificate.
        /// </summary>
        [Theory]
        [Repeat(10)]
        public void CreateSelfSignedForECDsaDefaultTest(ECCurveHashPair eccurveHashPair)
        {
            // default cert
            X509Certificate2 cert = CertificateBuilder
                .Create(Subject)
                .SetECCurve(eccurveHashPair.Curve)
                .CreateForECDsa();
            Assert.NotNull(cert);
            WriteCertificate(cert, "Default ECDsa cert");
            using (ECDsa privateKey = cert.GetECDsaPrivateKey())
            {
                Assert.NotNull(privateKey);
                privateKey.ExportParameters(false);
                privateKey.ExportParameters(true);
            }
            using (ECDsa publicKey = cert.GetECDsaPublicKey())
            {
                Assert.NotNull(publicKey);
                publicKey.ExportParameters(false);
            }
            Assert.AreEqual(
                eccurveHashPair.HashAlgorithmName,
                Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            Assert.GreaterOrEqual(DateTime.UtcNow, cert.NotBefore);
            Assert.GreaterOrEqual(
                DateTime.UtcNow.AddMonths(X509Defaults.LifeTime),
                cert.NotAfter.ToUniversalTime());
            TestUtils.ValidateSelSignedBasicConstraints(cert);
            X509KeyUsageExtension keyUsage = cert.Extensions.FindExtension<X509KeyUsageExtension>();
            Assert.NotNull(keyUsage);
            X509PfxUtils.VerifyECDsaKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert), "Verify self signed.");
            CheckPEMWriterReader(cert);
        }

        [Theory]
        [Repeat(10)]
        public void CreateSelfSignedForECDsaAllFields(ECCurveHashPair ecCurveHashPair)
        {
            // set dates and extension
            const string applicationUri = "urn:opcfoundation.org:mypc";
            string[] domains = ["mypc", "mypc.opcfoundation.org", "192.168.1.100"];
            X509Certificate2 cert = CertificateBuilder
                .Create(Subject)
                .SetNotBefore(DateTime.Today.AddYears(-1))
                .SetNotAfter(DateTime.Today.AddYears(25))
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, domains))
                .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                .SetECCurve(ecCurveHashPair.Curve)
                .CreateForECDsa();
            Assert.NotNull(cert);
            WriteCertificate(
                cert,
                $"Default cert ECDsa {ecCurveHashPair.Curve.Oid.FriendlyName} with modified lifetime and alt name extension");
            Assert.AreEqual(Subject, cert.Subject);
            using (ECDsa privateKey = cert.GetECDsaPrivateKey())
            {
                Assert.NotNull(privateKey);
                privateKey.ExportParameters(false);
                privateKey.ExportParameters(true);
            }
            using (ECDsa publicKey = cert.GetECDsaPublicKey())
            {
                Assert.NotNull(publicKey);
                publicKey.ExportParameters(false);
            }
            Assert.AreEqual(
                ecCurveHashPair.HashAlgorithmName,
                Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            TestUtils.ValidateSelSignedBasicConstraints(cert);
            X509PfxUtils.VerifyECDsaKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert));
            CheckPEMWriterReader(cert);
        }

        [Theory]
        [Repeat(10)]
        public void CreateCACertForECDsa(ECCurveHashPair ecCurveHashPair)
        {
            // create a CA cert
            X509Certificate2 cert = CertificateBuilder
                .Create(Subject)
                .SetCAConstraint()
                .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                .AddExtension(s_distributionPoints.BuildX509CRLDistributionPoints())
                .SetECCurve(ecCurveHashPair.Curve)
                .CreateForECDsa();
            Assert.NotNull(cert);
            WriteCertificate(
                cert,
                "Default cert with RSA {keyHashPair.KeySize} {keyHashPair.HashAlgorithmName} and CRL distribution points");
            Assert.AreEqual(
                ecCurveHashPair.HashAlgorithmName,
                Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            X509BasicConstraintsExtension basicConstraintsExtension =
                cert.Extensions.FindExtension<X509BasicConstraintsExtension>();
            Assert.NotNull(basicConstraintsExtension);
            Assert.True(basicConstraintsExtension.CertificateAuthority);
            Assert.False(basicConstraintsExtension.HasPathLengthConstraint);
            X509PfxUtils.VerifyECDsaKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert));
            CheckPEMWriterReader(cert);
        }

        [Test]
        public void CreateECDsaDefaultWithSerialTest()
        {
            ECCurve eccurve = ECCurve.NamedCurves.nistP256;
            // default cert
            NUnit.Framework.Assert.Throws<ArgumentOutOfRangeException>(() =>
                CertificateBuilder.Create(Subject).SetSerialNumberLength(0).SetECCurve(eccurve)
                    .CreateForECDsa());
            NUnit.Framework.Assert.Throws<ArgumentOutOfRangeException>(() => CertificateBuilder
                .Create(Subject)
                .SetSerialNumberLength(X509Defaults.SerialNumberLengthMax + 1)
                .SetECCurve(eccurve)
                .CreateForECDsa());
            ICertificateBuilderCreateForECDsaAny builder = CertificateBuilder
                .Create(Subject)
                .SetSerialNumberLength(X509Defaults.SerialNumberLengthMax)
                .SetECCurve(eccurve);

            // ensure every cert has a different serial number
            X509Certificate2 cert1 = builder.CreateForECDsa();
            X509Certificate2 cert2 = builder.CreateForECDsa();
            WriteCertificate(cert1, "Cert1 with max length serial number");
            WriteCertificate(cert2, "Cert2 with max length serial number");
            Assert.GreaterOrEqual(
                X509Defaults.SerialNumberLengthMax,
                cert1.GetSerialNumber().Length);
            Assert.GreaterOrEqual(
                X509Defaults.SerialNumberLengthMax,
                cert2.GetSerialNumber().Length);
            Assert.AreNotEqual(cert1.SerialNumber, cert2.SerialNumber);
        }

        [Test]
        public void CreateECDsaManualSerialTest()
        {
            ECCurve eccurve = ECCurve.NamedCurves.nistP256;
            // default cert
            NUnit.Framework.Assert.Throws<ArgumentOutOfRangeException>(() =>
                CertificateBuilder.Create(Subject).SetSerialNumber([]).SetECCurve(eccurve)
                    .CreateForECDsa());
            NUnit.Framework.Assert.Throws<ArgumentOutOfRangeException>(() => CertificateBuilder
                    .Create(Subject)
                    .SetSerialNumber(new byte[X509Defaults.SerialNumberLengthMax + 1])
                    .SetECCurve(eccurve)
                    .CreateForECDsa());
            byte[] serial = new byte[X509Defaults.SerialNumberLengthMax];
            for (int i = 0; i < serial.Length; i++)
            {
                serial[i] = (byte)((i + 1) | 0x80);
            }

            // test if sign bit is cleared
            ICertificateBuilder builder = CertificateBuilder.Create(Subject)
                .SetSerialNumber(serial);

            serial[^1] &= 0x7f;
            Assert.AreEqual(serial, builder.GetSerialNumber());
            X509Certificate2 cert1 = builder.SetECCurve(eccurve).CreateForECDsa();
            WriteCertificate(cert1, "Cert1 with max length serial number");
            TestContext.Out.WriteLine($"Serial: {serial.ToHexString(true)}");
            Assert.AreEqual(serial, cert1.GetSerialNumber());
            Assert.AreEqual(X509Defaults.SerialNumberLengthMax, cert1.GetSerialNumber().Length);

            // clear sign bit
            builder.SetSerialNumberLength(X509Defaults.SerialNumberLengthMax);

            X509Certificate2 cert2 = builder.SetECCurve(eccurve).CreateForECDsa();
            WriteCertificate(cert2, "Cert2 with max length serial number");
            TestContext.Out.WriteLine($"Serial: {cert2.SerialNumber}");
            Assert.GreaterOrEqual(
                X509Defaults.SerialNumberLengthMax,
                cert2.GetSerialNumber().Length);
            Assert.AreNotEqual(cert1.SerialNumber, cert2.SerialNumber);
        }

        [Theory]
        public void CreateForECDsaWithGeneratorTest(ECCurveHashPair ecCurveHashPair)
        {
            // default signing cert with custom key
            X509Certificate2 signingCert = CertificateBuilder
                .Create(Subject)
                .SetCAConstraint()
                .SetHashAlgorithm(HashAlgorithmName.SHA512)
                .SetECCurve(ecCurveHashPair.Curve)
                .CreateForECDsa();

            WriteCertificate(
                signingCert,
                $"Signing ECDsa {signingCert.GetECDsaPublicKey().KeySize} cert");

            using (ECDsa ecdsaPrivateKey = signingCert.GetECDsaPrivateKey())
            {
                var generator = X509SignatureGenerator.CreateForECDsa(ecdsaPrivateKey);
                X509Certificate2 cert = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetIssuer(X509CertificateLoader.LoadCertificate(signingCert.RawData))
                    .CreateForRSA(generator);
                Assert.NotNull(cert);
                WriteCertificate(cert, "Default signed ECDsa cert");
            }

            using (ECDsa ecdsaPrivateKey = signingCert.GetECDsaPrivateKey())
            using (ECDsa ecdsaPublicKey = signingCert.GetECDsaPublicKey())
            {
                var generator = X509SignatureGenerator.CreateForECDsa(ecdsaPrivateKey);
                X509Certificate2 cert = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                    .SetIssuer(X509CertificateLoader.LoadCertificate(signingCert.RawData))
                    .SetECDsaPublicKey(ecdsaPublicKey)
                    .CreateForECDsa(generator);
                Assert.NotNull(cert);
                WriteCertificate(cert, "Default signed ECDsa cert with Public Key");
            }

            using (ECDsa ecdsaPrivateKey = signingCert.GetECDsaPrivateKey())
            {
                var generator = X509SignatureGenerator.CreateForECDsa(ecdsaPrivateKey);
                X509Certificate2 cert = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                    .SetIssuer(X509CertificateLoader.LoadCertificate(signingCert.RawData))
                    .SetECCurve(ecCurveHashPair.Curve)
                    .CreateForECDsa(generator);
                Assert.NotNull(cert);
                WriteCertificate(cert, "Default signed RSA cert");
                CheckPEMWriterReader(cert);
            }

            // ensure invalid path throws argument exception
            NUnit.Framework.Assert.Throws<NotSupportedException>(() =>
            {
                using ECDsa ecdsaPrivateKey = signingCert.GetECDsaPrivateKey();
                var generator = X509SignatureGenerator.CreateForECDsa(ecdsaPrivateKey);
                X509Certificate2 cert = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                    .SetECCurve(ecCurveHashPair.Curve)
                    .CreateForECDsa(generator);
            });
        }

        [Theory]
        public void SetECDsaPublicKeyByteArray(ECCurveHashPair ecCurveHashPair)
        {
            // default signing cert with custom key
            X509Certificate2 signingCert = CertificateBuilder
                .Create(Subject)
                .SetCAConstraint()
                .SetHashAlgorithm(HashAlgorithmName.SHA512)
                .SetECCurve(ecCurveHashPair.Curve)
                .CreateForECDsa();

            WriteCertificate(
                signingCert,
                $"Signing ECDsa {signingCert.GetECDsaPublicKey().KeySize} cert");

            using ECDsa ecdsaPrivateKey = signingCert.GetECDsaPrivateKey();
            using ECDsa ecdsaPublicKey = signingCert.GetECDsaPublicKey();
            byte[] pubKeyBytes = GetPublicKey(ecdsaPublicKey);

            var generator = X509SignatureGenerator.CreateForECDsa(ecdsaPrivateKey);
            X509Certificate2 cert = CertificateBuilder
                .Create("CN=App Cert")
                .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                .SetIssuer(X509CertificateLoader.LoadCertificate(signingCert.RawData))
                .SetECDsaPublicKey(pubKeyBytes)
                .CreateForECDsa(generator);
            Assert.NotNull(cert);
            WriteCertificate(cert, "Default signed ECDsa cert with Public Key");
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

        private static void CheckPEMWriterReader(
            X509Certificate2 certificate,
            string password = null)
        {
            PEMWriter.ExportCertificateAsPEM(certificate);
            if (certificate.HasPrivateKey)
            {
#if NET48
                byte[] exportedPrivateKey = PEMWriter.ExportPrivateKeyAsPEM(certificate, password);
                ECDsa ecdsaPrivKey = PEMReader.ImportECDsaPrivateKeyFromPEM(
                    exportedPrivateKey,
                    password);
#endif
#if !NETFRAMEWORK
                PEMWriter.ExportPrivateKeyAsPEM(certificate, password);
#if NETCOREAPP3_1 || NET5_0_OR_GREATER
                byte[] exportedPrivateKey = null;
                ECDsa ecdsaPrivKey = null;
                exportedPrivateKey = PEMWriter.ExportECDsaPrivateKeyAsPEM(certificate);
                ecdsaPrivKey = PEMReader.ImportECDsaPrivateKeyFromPEM(exportedPrivateKey, password);
#endif
#endif
            }
        }

        private static byte[] GetPublicKey(ECDsa ecdsa)
        {
#if NETFRAMEWORK
            Org.BouncyCastle.Crypto.Parameters.ECPublicKeyParameters pubKeyParams =
                BouncyCastle.X509Utils.GetECPublicKeyParameters(ecdsa);
            return SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pubKeyParams)
                .ToAsn1Object()
                .GetDerEncoded();
#elif NETCOREAPP3_1 || NET5_0_OR_GREATER
            return ecdsa.ExportSubjectPublicKeyInfo();
#endif
        }

        public static ECCurveHashPair[] GetECCurveHashPairs()
        {
            var result = new ECCurveHashPairCollection
            {
                { ECCurve.NamedCurves.nistP256, HashAlgorithmName.SHA256 },
                { ECCurve.NamedCurves.nistP384, HashAlgorithmName.SHA384 },
                { ECCurve.NamedCurves.brainpoolP256r1, HashAlgorithmName.SHA256 },
                { ECCurve.NamedCurves.brainpoolP384r1, HashAlgorithmName.SHA384 }
            };

            int i = 0;
            while (i < result.Count)
            {
                ECDsa key = null;

                // test if curve is supported
                try
                {
                    key = ECDsa.Create(result[i].Curve);
                }
                catch
                {
                    result.RemoveAt(i);
                    continue;
                }
                finally
                {
                    Utils.SilentDispose(key);
                }
                i++;
            }

            return [.. result];
        }
    }
}
#endif
