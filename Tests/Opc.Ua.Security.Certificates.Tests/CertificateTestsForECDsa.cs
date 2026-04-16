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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Tests;

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

                using Certificate cert = builder
                    .SetHashAlgorithm(eCCurveHash.HashAlgorithmName)
                    .SetECCurve(eCCurveHash.Curve)
                    .CreateForECDsa();
                Assert.That(cert, Is.Not.Null);
                WriteCertificate(
                    cert,
                    $"Default cert with ECDsa {eCCurveHash.Curve.Oid.FriendlyName} {eCCurveHash.HashAlgorithmName} signature.");
                Assert.That(
                    Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value),
                    Is.EqualTo(eCCurveHash.HashAlgorithmName));
                // ensure serial numbers are different
                Assert.That(cert.GetSerialNumber(), Is.Not.EqualTo(previousSerialNumber));
                X509PfxUtils.VerifyECDsaKeyPair(cert, cert, true);
                Assert.That(X509Utils.VerifySelfSigned(cert), Is.True);
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
            Certificate cert = CertificateBuilder
                .Create(Subject)
                .SetECCurve(eccurveHashPair.Curve)
                .CreateForECDsa();
            Assert.That(cert, Is.Not.Null);
            WriteCertificate(cert, "Default ECDsa cert");
            using (ECDsa privateKey = cert.GetECDsaPrivateKey())
            {
                Assert.That(privateKey, Is.Not.Null);
                privateKey.ExportParameters(false);
                privateKey.ExportParameters(true);
            }
            using (ECDsa publicKey = cert.GetECDsaPublicKey())
            {
                Assert.That(publicKey, Is.Not.Null);
                publicKey.ExportParameters(false);
            }
            Assert.That(
                Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value),
                Is.EqualTo(eccurveHashPair.HashAlgorithmName));
            Assert.That(DateTime.UtcNow, Is.GreaterThanOrEqualTo(cert.NotBefore));
            Assert.That(
                DateTime.UtcNow.AddMonths(X509Defaults.LifeTime),
                Is.GreaterThanOrEqualTo(cert.NotAfter.ToUniversalTime()));
            TestUtils.ValidateSelSignedBasicConstraints(cert);
            X509KeyUsageExtension keyUsage = cert.Extensions.FindExtension<X509KeyUsageExtension>();
            Assert.That(keyUsage, Is.Not.Null);
            X509PfxUtils.VerifyECDsaKeyPair(cert, cert, true);
            Assert.That(X509Utils.VerifySelfSigned(cert), Is.True, "Verify self signed.");
            CheckPEMWriterReader(cert);
        }

        [Theory]
        [Repeat(10)]
        public void CreateSelfSignedForECDsaAllFields(ECCurveHashPair ecCurveHashPair)
        {
            // set dates and extension
            const string applicationUri = "urn:opcfoundation.org:mypc";
            string[] domains = ["mypc", "mypc.opcfoundation.org", "192.168.1.100"];
            Certificate cert = CertificateBuilder
                .Create(Subject)
                .SetNotBefore(DateTime.Today.AddYears(-1))
                .SetNotAfter(DateTime.Today.AddYears(25))
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, domains))
                .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                .SetECCurve(ecCurveHashPair.Curve)
                .CreateForECDsa();
            Assert.That(cert, Is.Not.Null);
            WriteCertificate(
                cert,
                $"Default cert ECDsa {ecCurveHashPair.Curve.Oid.FriendlyName} with modified lifetime and alt name extension");
            Assert.That(cert.Subject, Is.EqualTo(Subject));
            using (ECDsa privateKey = cert.GetECDsaPrivateKey())
            {
                Assert.That(privateKey, Is.Not.Null);
                privateKey.ExportParameters(false);
                privateKey.ExportParameters(true);
            }
            using (ECDsa publicKey = cert.GetECDsaPublicKey())
            {
                Assert.That(publicKey, Is.Not.Null);
                publicKey.ExportParameters(false);
            }
            Assert.That(
                Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value),
                Is.EqualTo(ecCurveHashPair.HashAlgorithmName));
            TestUtils.ValidateSelSignedBasicConstraints(cert);
            X509PfxUtils.VerifyECDsaKeyPair(cert, cert, true);
            Assert.That(X509Utils.VerifySelfSigned(cert), Is.True);
            CheckPEMWriterReader(cert);
        }

        [Theory]
        [Repeat(10)]
        public void CreateCACertForECDsa(ECCurveHashPair ecCurveHashPair)
        {
            // create a CA cert
            Certificate cert = CertificateBuilder
                .Create(Subject)
                .SetCAConstraint()
                .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                .AddExtension(s_distributionPoints.BuildX509CRLDistributionPoints())
                .SetECCurve(ecCurveHashPair.Curve)
                .CreateForECDsa();
            Assert.That(cert, Is.Not.Null);
            WriteCertificate(
                cert,
                "Default cert with RSA {keyHashPair.KeySize} {keyHashPair.HashAlgorithmName} and CRL distribution points");
            Assert.That(
                Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value),
                Is.EqualTo(ecCurveHashPair.HashAlgorithmName));
            X509BasicConstraintsExtension basicConstraintsExtension =
                cert.Extensions.FindExtension<X509BasicConstraintsExtension>();
            Assert.That(basicConstraintsExtension, Is.Not.Null);
            Assert.That(basicConstraintsExtension.CertificateAuthority, Is.True);
            Assert.That(basicConstraintsExtension.HasPathLengthConstraint, Is.False);
            X509PfxUtils.VerifyECDsaKeyPair(cert, cert, true);
            Assert.That(X509Utils.VerifySelfSigned(cert), Is.True);
            CheckPEMWriterReader(cert);
        }

        [Test]
        public void CreateECDsaDefaultWithSerialTest()
        {
            ECCurve eccurve = ECCurve.NamedCurves.nistP256;
            // default cert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CertificateBuilder.Create(Subject).SetSerialNumberLength(0).SetECCurve(eccurve)
                    .CreateForECDsa());
            Assert.Throws<ArgumentOutOfRangeException>(() => CertificateBuilder
                .Create(Subject)
                .SetSerialNumberLength(X509Defaults.SerialNumberLengthMax + 1)
                .SetECCurve(eccurve)
                .CreateForECDsa());
            ICertificateBuilderCreateForECDsaAny builder = CertificateBuilder
                .Create(Subject)
                .SetSerialNumberLength(X509Defaults.SerialNumberLengthMax)
                .SetECCurve(eccurve);

            // ensure every cert has a different serial number
            Certificate cert1 = builder.CreateForECDsa();
            Certificate cert2 = builder.CreateForECDsa();
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
        public void CreateECDsaManualSerialTest()
        {
            ECCurve eccurve = ECCurve.NamedCurves.nistP256;
            // default cert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CertificateBuilder.Create(Subject).SetSerialNumber([]).SetECCurve(eccurve)
                    .CreateForECDsa());
            Assert.Throws<ArgumentOutOfRangeException>(() => CertificateBuilder
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
            Assert.That(builder.GetSerialNumber(), Is.EqualTo(serial));
            Certificate cert1 = builder.SetECCurve(eccurve).CreateForECDsa();
            WriteCertificate(cert1, "Cert1 with max length serial number");
            TestContext.Out.WriteLine($"Serial: {serial.ToHexString(true)}");
            Assert.That(cert1.GetSerialNumber(), Is.EqualTo(serial));
            Assert.That(cert1.GetSerialNumber(), Has.Length.EqualTo(X509Defaults.SerialNumberLengthMax));

            // clear sign bit
            builder.SetSerialNumberLength(X509Defaults.SerialNumberLengthMax);

            Certificate cert2 = builder.SetECCurve(eccurve).CreateForECDsa();
            WriteCertificate(cert2, "Cert2 with max length serial number");
            TestContext.Out.WriteLine($"Serial: {cert2.SerialNumber}");
            Assert.That(
                X509Defaults.SerialNumberLengthMax,
                Is.GreaterThanOrEqualTo(cert2.GetSerialNumber().Length));
            Assert.That(cert2.SerialNumber, Is.Not.EqualTo(cert1.SerialNumber));
        }

        [Theory]
        public void CreateForECDsaWithGeneratorTest(ECCurveHashPair ecCurveHashPair)
        {
            // default signing cert with custom key
            Certificate signingCert = CertificateBuilder
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
                Certificate cert = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetIssuer(CertificateFactory.Create(signingCert.RawData))
                    .CreateForRSA(generator);
                Assert.That(cert, Is.Not.Null);
                WriteCertificate(cert, "Default signed ECDsa cert");
            }

            using (ECDsa ecdsaPrivateKey = signingCert.GetECDsaPrivateKey())
            using (ECDsa ecdsaPublicKey = signingCert.GetECDsaPublicKey())
            {
                var generator = X509SignatureGenerator.CreateForECDsa(ecdsaPrivateKey);
                Certificate cert = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                    .SetIssuer(CertificateFactory.Create(signingCert.RawData))
                    .SetECDsaPublicKey(ecdsaPublicKey)
                    .CreateForECDsa(generator);
                Assert.That(cert, Is.Not.Null);
                WriteCertificate(cert, "Default signed ECDsa cert with Public Key");
            }

            using (ECDsa ecdsaPrivateKey = signingCert.GetECDsaPrivateKey())
            {
                var generator = X509SignatureGenerator.CreateForECDsa(ecdsaPrivateKey);
                Certificate cert = CertificateBuilder
                    .Create("CN=App Cert")
                    .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                    .SetIssuer(CertificateFactory.Create(signingCert.RawData))
                    .SetECCurve(ecCurveHashPair.Curve)
                    .CreateForECDsa(generator);
                Assert.That(cert, Is.Not.Null);
                WriteCertificate(cert, "Default signed RSA cert");
                CheckPEMWriterReader(cert);
            }

            // ensure invalid path throws argument exception
            Assert.Throws<NotSupportedException>(() =>
            {
                using ECDsa ecdsaPrivateKey = signingCert.GetECDsaPrivateKey();
                var generator = X509SignatureGenerator.CreateForECDsa(ecdsaPrivateKey);
                Certificate cert = CertificateBuilder
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
            Certificate signingCert = CertificateBuilder
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
            Certificate cert = CertificateBuilder
                .Create("CN=App Cert")
                .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                .SetIssuer(CertificateFactory.Create(signingCert.RawData))
                .SetECDsaPublicKey(pubKeyBytes)
                .CreateForECDsa(generator);
            Assert.That(cert, Is.Not.Null);
            WriteCertificate(cert, "Default signed ECDsa cert with Public Key");
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

        private static void CheckPEMWriterReader(
            Certificate certificate,
            ReadOnlySpan<char> password = default)
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
                    key?.Dispose();
                }
                i++;
            }

            return [.. result];
        }
    }
}
