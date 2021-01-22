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

#if ECC_SUPPORT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Tests for the CertificateBuilder class.
    /// </summary>
    [TestFixture, Category("Certificate"), Category("ECDsa")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CertificateTestsForECDsa
    {
        #region DataPointSources
        public const string Subject = "CN=Test Cert Subject";

        [DatapointSource]
        public CertificateAsset[] CertificateTestCases =
            new AssetCollection<CertificateAsset>(TestUtils.EnumerateTestAssets("*.?er")).ToArray();

        [DatapointSource]
        public ECCurveHashPair[] ECCurveHashPairs = GetECCurveHashPairs();
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
            foreach (var eCCurveHash in ECCurveHashPairs)
            {
                if (!eCCurveHash.Curve.IsNamed) continue;
                using (var cert = builder
                    .SetHashAlgorithm(eCCurveHash.HashAlgorithmName)
                    .SetECCurve(eCCurveHash.Curve)
                    .CreateForECDsa())
                {
                    Assert.NotNull(cert);
                    WriteCertificate(cert, $"Default cert with ECDsa {eCCurveHash.Curve.Oid.FriendlyName} {eCCurveHash.HashAlgorithmName} signature.");
                    Assert.AreEqual(eCCurveHash.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
                    // ensure serial numbers are different
                    Assert.AreNotEqual(previousSerialNumber, cert.GetSerialNumber());
                    X509PfxUtils.VerifyECDsaKeyPair(cert, cert, true);
                    Assert.True(X509Utils.VerifySelfSigned(cert));
                }
            }
        }

        /// <summary>
        /// Create the default RSA certificate.
        /// </summary>
        [Theory]
        public void CreateSelfSignedForECDsaDefaultTest(ECCurveHashPair eccurveHashPair)
        {
            // default cert
            X509Certificate2 cert = CertificateBuilder.Create(Subject)
                .SetECCurve(eccurveHashPair.Curve)
                .CreateForECDsa();
            Assert.NotNull(cert);
            WriteCertificate(cert, "Default ECDsa cert");
            using (var privateKey = cert.GetECDsaPrivateKey())
            {
                Assert.NotNull(privateKey);
                privateKey.ExportParameters(false);
                privateKey.ExportParameters(true);
            }
            using (var publicKey = cert.GetECDsaPublicKey())
            {
                Assert.NotNull(publicKey);
                publicKey.ExportParameters(false);
            }
            Assert.AreEqual(X509Defaults.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            Assert.GreaterOrEqual(DateTime.UtcNow, cert.NotBefore);
            Assert.GreaterOrEqual(DateTime.UtcNow.AddMonths(X509Defaults.LifeTime), cert.NotAfter.ToUniversalTime());
            var basicConstraintsExtension = X509Extensions.FindExtension<X509BasicConstraintsExtension>(cert.Extensions);
            Assert.NotNull(basicConstraintsExtension);
            Assert.True(basicConstraintsExtension.CertificateAuthority);
            Assert.AreEqual(0, basicConstraintsExtension.PathLengthConstraint);
            var keyUsage = X509Extensions.FindExtension<X509KeyUsageExtension>(cert.Extensions);
            Assert.NotNull(keyUsage);
            X509PfxUtils.VerifyECDsaKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert), "Verify self signed.");
        }

        [Theory]
        public void CreateSelfSignedForECDsaAllFields(
            ECCurveHashPair ecCurveHashPair
            )
        {
            // set dates and extension
            var applicationUri = "urn:opcfoundation.org:mypc";
            var domains = new string[] { "mypc", "mypc.opcfoundation.org", "192.168.1.100" };
            var cert = CertificateBuilder.Create(Subject)
                .SetNotBefore(DateTime.Today.AddYears(-1))
                .SetNotAfter(DateTime.Today.AddYears(25))
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, domains))
                .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                .SetECCurve(ecCurveHashPair.Curve)
                .CreateForECDsa();
            Assert.NotNull(cert);
            WriteCertificate(cert, $"Default cert ECDsa {ecCurveHashPair.Curve.Oid.FriendlyName} with modified lifetime and alt name extension");
            Assert.AreEqual(Subject, cert.Subject);
            using (var privateKey = cert.GetECDsaPrivateKey())
            {
                Assert.NotNull(privateKey);
                privateKey.ExportParameters(false);
                privateKey.ExportParameters(true);
            }
            using (var publicKey = cert.GetECDsaPublicKey())
            {
                Assert.NotNull(publicKey);
                publicKey.ExportParameters(false);
            }
            Assert.AreEqual(ecCurveHashPair.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            var basicConstraintsExtension = X509Extensions.FindExtension<X509BasicConstraintsExtension>(cert.Extensions);
            Assert.NotNull(basicConstraintsExtension);
            Assert.True(basicConstraintsExtension.CertificateAuthority);
            X509PfxUtils.VerifyECDsaKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert));
        }

        [Theory]
        public void CreateCACertForECDsa(
            ECCurveHashPair ecCurveHashPair
            )
        {
            // create a CA cert
            var cert = CertificateBuilder.Create(Subject)
                .SetCAConstraint()
                .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                .AddExtension(X509Extensions.BuildX509CRLDistributionPoints("http://myca/mycert.crl"))
                .SetECCurve(ecCurveHashPair.Curve)
                .CreateForECDsa();
            Assert.NotNull(cert);
            WriteCertificate(cert, "Default cert with RSA {keyHashPair.KeySize} {keyHashPair.HashAlgorithmName} and CRL distribution points");
            Assert.AreEqual(ecCurveHashPair.HashAlgorithmName, Oids.GetHashAlgorithmName(cert.SignatureAlgorithm.Value));
            var basicConstraintsExtension = X509Extensions.FindExtension<X509BasicConstraintsExtension>(cert.Extensions);
            Assert.NotNull(basicConstraintsExtension);
            Assert.True(basicConstraintsExtension.CertificateAuthority);
            Assert.False(basicConstraintsExtension.HasPathLengthConstraint);
            X509PfxUtils.VerifyECDsaKeyPair(cert, cert, true);
            Assert.True(X509Utils.VerifySelfSigned(cert));
        }

        [Test]
        public void CreateECDsaDefaultWithSerialTest()
        {
            var eccurve = ECCurve.NamedCurves.nistP256;
            // default cert
            Assert.Throws<ArgumentOutOfRangeException>(
                () => {
                    CertificateBuilder.Create(Subject)
                    .SetSerialNumberLength(0)
                    .SetECCurve(eccurve)
                    .CreateForECDsa();
                }
            );
            Assert.Throws<ArgumentOutOfRangeException>(
                () => {
                    CertificateBuilder.Create(Subject)
                    .SetSerialNumberLength(X509Defaults.SerialNumberLengthMax + 1)
                    .SetECCurve(eccurve)
                    .CreateForECDsa();
                }
            );
            var builder = CertificateBuilder.Create(Subject)
                .SetSerialNumberLength(X509Defaults.SerialNumberLengthMax)
                .SetECCurve(eccurve);

            // ensure every cert has a different serial number
            var cert1 = builder.CreateForECDsa();
            var cert2 = builder.CreateForECDsa();
            WriteCertificate(cert1, "Cert1 with max length serial number");
            WriteCertificate(cert2, "Cert2 with max length serial number");
            Assert.GreaterOrEqual(X509Defaults.SerialNumberLengthMax, cert1.GetSerialNumber().Length);
            Assert.GreaterOrEqual(X509Defaults.SerialNumberLengthMax, cert2.GetSerialNumber().Length);
            Assert.AreNotEqual(cert1.SerialNumber, cert2.SerialNumber);
        }

        [Test]
        public void CreateECDsaManualSerialTest()
        {
            var eccurve = ECCurve.NamedCurves.nistP256;
            // default cert
            Assert.Throws<ArgumentOutOfRangeException>(
                () => {
                    CertificateBuilder.Create(Subject)
                    .SetSerialNumber(new byte[0])
                    .SetECCurve(eccurve)
                    .CreateForECDsa();
                }
            );
            Assert.Throws<ArgumentOutOfRangeException>(
                () => {
                    CertificateBuilder.Create(Subject)
                    .SetSerialNumber(new byte[X509Defaults.SerialNumberLengthMax + 1])
                    .SetECCurve(eccurve)
                    .CreateForECDsa();
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
            var cert1 = builder.SetECCurve(eccurve).CreateForECDsa();
            WriteCertificate(cert1, "Cert1 with max length serial number");
            TestContext.Out.WriteLine($"Serial: {serial.ToHexString(true)}");
            Assert.AreEqual(serial, cert1.GetSerialNumber());
            Assert.AreEqual(X509Defaults.SerialNumberLengthMax, cert1.GetSerialNumber().Length);

            // clear sign bit
            builder.SetSerialNumberLength(X509Defaults.SerialNumberLengthMax);

            var cert2 = builder.SetECCurve(eccurve).CreateForECDsa();
            WriteCertificate(cert2, "Cert2 with max length serial number");
            TestContext.Out.WriteLine($"Serial: {cert2.SerialNumber}");
            Assert.GreaterOrEqual(X509Defaults.SerialNumberLengthMax, cert2.GetSerialNumber().Length);
            Assert.AreNotEqual(cert1.SerialNumber, cert2.SerialNumber);
        }

        [Theory]
        public void CreateForECDsaWithGeneratorTest(
            ECCurveHashPair ecCurveHashPair
            )
        {
            // default signing cert with custom key
            X509Certificate2 signingCert = CertificateBuilder.Create(Subject)
                .SetCAConstraint()
                .SetHashAlgorithm(HashAlgorithmName.SHA512)
                .SetECCurve(ecCurveHashPair.Curve)
                .CreateForECDsa();

            WriteCertificate(signingCert, $"Signing ECDsa {signingCert.GetECDsaPublicKey().KeySize} cert");

            using (ECDsa ecdsaPrivateKey = signingCert.GetECDsaPrivateKey())
            {
                var generator = X509SignatureGenerator.CreateForECDsa(ecdsaPrivateKey);
                var cert = CertificateBuilder.Create("CN=App Cert")
                    .SetIssuer(new X509Certificate2(signingCert.RawData))
                    .CreateForRSA(generator);
                Assert.NotNull(cert);
                WriteCertificate(cert, $"Default signed ECDsa cert");
            }

            using (ECDsa ecdsaPrivateKey = signingCert.GetECDsaPrivateKey())
            using (ECDsa ecdsaPublicKey = signingCert.GetECDsaPublicKey())
            {
                var generator = X509SignatureGenerator.CreateForECDsa(ecdsaPrivateKey);
                var cert = CertificateBuilder.Create("CN=App Cert")
                    .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                    .SetIssuer(new X509Certificate2(signingCert.RawData))
                    .SetECDsaPublicKey(ecdsaPublicKey)
                    .CreateForECDsa(generator);
                Assert.NotNull(cert);
                WriteCertificate(cert, $"Default signed ECDsa cert with Public Key");
            }

            using (ECDsa ecdsaPrivateKey = signingCert.GetECDsaPrivateKey())
            {
                var generator = X509SignatureGenerator.CreateForECDsa(ecdsaPrivateKey);
                var cert = CertificateBuilder.Create("CN=App Cert")
                    .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                    .SetIssuer(new X509Certificate2(signingCert.RawData))
                    .SetECCurve(ecCurveHashPair.Curve)
                    .CreateForECDsa(generator);
                Assert.NotNull(cert);
                WriteCertificate(cert, $"Default signed RSA cert");
            }

            // ensure invalid path throws argument exception
            Assert.Throws<NotSupportedException>(() => {
                using (ECDsa ecdsaPrivateKey = signingCert.GetECDsaPrivateKey())
                {
                    var generator = X509SignatureGenerator.CreateForECDsa(ecdsaPrivateKey);
                    var cert = CertificateBuilder.Create("CN=App Cert")
                        .SetHashAlgorithm(ecCurveHashPair.HashAlgorithmName)
                        .SetECCurve(ecCurveHashPair.Curve)
                        .CreateForECDsa(generator);
                }
            });
        }
        #endregion

        #region Private Methods
        private static ECCurveHashPair[] GetECCurveHashPairs()
        {
            var result = new ECCurveHashPairCollection {
                { ECCurve.NamedCurves.nistP256, HashAlgorithmName.SHA256 },
                { ECCurve.NamedCurves.nistP384, HashAlgorithmName.SHA384 } };
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                result.AddRange(new ECCurveHashPairCollection {
                { ECCurve.NamedCurves.brainpoolP256r1, HashAlgorithmName.SHA256 },
                { ECCurve.NamedCurves.brainpoolP384r1, HashAlgorithmName.SHA384 }});
            }
            return result.ToArray();
        }

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
#endif

