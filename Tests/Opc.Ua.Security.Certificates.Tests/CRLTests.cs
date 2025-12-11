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
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Tests for the CRL class.
    /// </summary>
    [TestFixture]
    [Category("CRL")]
    [Parallelizable]
    [TestFixtureSource(nameof(FixtureArgs))]
    [SetCulture("en-us")]
    public class CRLTests
    {
        [DatapointSource]
        public static readonly CRLAsset[] CRLTestCases =
        [
            .. AssetCollection<CRLAsset>.CreateFromFiles(TestUtils.EnumerateTestAssets("*.crl"))
        ];

        [DatapointSource]
        public static readonly KeyHashPair[] KeyHashPairs = new KeyHashPairCollection
        {
            { 2048, HashAlgorithmName.SHA256 },
            { 3072, HashAlgorithmName.SHA384 },
            { 4096, HashAlgorithmName.SHA512 }
        }.ToArray();

        /// <summary>
        /// CertificateTypes to run the Test with
        /// </summary>
        public static readonly object[] FixtureArgs =
        [
            new object[]
            {
                nameof(ObjectTypeIds.RsaSha256ApplicationCertificateType),
                ObjectTypeIds.RsaSha256ApplicationCertificateType
            },
            new object[]
            {
                nameof(ObjectTypeIds.EccNistP256ApplicationCertificateType),
                ObjectTypeIds.EccNistP256ApplicationCertificateType
            },
            new object[]
            {
                nameof(ObjectTypeIds.EccNistP384ApplicationCertificateType),
                ObjectTypeIds.EccNistP384ApplicationCertificateType
            },
            new object[]
            {
                nameof(ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType),
                ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType
            },
            new object[]
            {
                nameof(ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType),
                ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType
            }
        ];

        public CRLTests(string certificateTypeString, NodeId certificateType)
        {
            if (!Utils.IsSupportedCertificateType(certificateType))
            {
                NUnit.Framework.Assert.Ignore(
                    $"Certificate type {certificateTypeString} is not supported on this platform.");
            }

            m_certificateType = certificateType;
        }

        /// <summary>
        /// Set up a an issuer cert to run the tests with
        /// </summary>
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            ECCurve? curve = EccUtils.GetCurveFromCertificateTypeId(m_certificateType);

            if (curve != null)
            {
                m_issuerCert = CertificateBuilder
                    .Create("CN=Root CA, O=OPC Foundation")
                    .SetCAConstraint()
                    .SetECCurve(curve.Value)
                    .CreateForECDsa();
            }
            // RSA Certificate
            else
            {
                m_issuerCert = CertificateBuilder
                    .Create("CN=Root CA, O=OPC Foundation")
                    .SetCAConstraint()
                    .CreateForRSA();
            }
        }

        /// <summary>
        /// Clean up the Test PKI folder
        /// </summary>
        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        /// <summary>
        /// Verify self signed app certs.
        /// </summary>
        [Theory]
        public void DecodeCRLs(CRLAsset crlAsset)
        {
            var x509Crl = new X509CRL(crlAsset.Crl);
            Assert.NotNull(x509Crl);
            TestContext.Out.WriteLine($"CRLAsset:   {x509Crl.Issuer}");
            string crlInfo = WriteCRL(x509Crl);
            TestContext.Out.WriteLine(crlInfo);
        }

        /// <summary>
        /// Validate a CRL Builder and decoder pass.
        /// </summary>
        [Test]
        public void CrlInternalBuilderTest()
        {
            var dname = new X500DistinguishedName("CN=Test, O=OPC Foundation");
            HashAlgorithmName hash = HashAlgorithmName.SHA256;
            CrlBuilder crlBuilder = CrlBuilder
                .Create(dname, hash)
                .SetNextUpdate(DateTime.Today.AddDays(30).ToUniversalTime());
            byte[] serial = [4, 5, 6, 7];
            var revokedarray = new RevokedCertificate(serial);
            crlBuilder.RevokedCertificates.Add(revokedarray);
            const string serstring = "45678910";
            var revokedstring = new RevokedCertificate(serstring);
            crlBuilder.RevokedCertificates.Add(revokedstring);
            crlBuilder.CrlExtensions.Add(X509Extensions.BuildCRLNumber(123));
            byte[] crlEncoded = crlBuilder.Encode();
            ValidateCRL(serial, serstring, hash, crlBuilder, crlEncoded);
        }

        /// <summary>
        /// Validate the full CRL encoder and decoder pass.
        /// </summary>
        [Theory]
        public void CrlBuilderTest(bool empty, bool noExtensions, KeyHashPair keyHashPair)
        {
            CrlBuilder crlBuilder = CrlBuilder
                .Create(m_issuerCert.SubjectName, keyHashPair.HashAlgorithmName)
                .SetThisUpdate(DateTime.UtcNow.Date)
                .SetNextUpdate(DateTime.UtcNow.Date.AddDays(30));

            byte[] serial = [4, 5, 6, 7];
            const string serstring = "123456789101";
            if (!empty)
            {
                // little endian byte array as serial number?
                var revokedarray = new RevokedCertificate(serial)
                {
                    RevocationDate = DateTime.UtcNow.AddDays(30)
                };
                crlBuilder.RevokedCertificates.Add(revokedarray);
                var revokedstring = new RevokedCertificate(serstring);
                crlBuilder.RevokedCertificates.Add(revokedstring);
            }

            if (!noExtensions)
            {
                crlBuilder.CrlExtensions.Add(X509Extensions.BuildCRLNumber(1111));
                crlBuilder.CrlExtensions.Add(m_issuerCert.BuildAuthorityKeyIdentifier());
            }
            IX509CRL i509Crl;
            if (X509PfxUtils.IsECDsaSignature(m_issuerCert))
            {
                i509Crl = crlBuilder.CreateForECDsa(m_issuerCert);
            }
            else
            {
                i509Crl = crlBuilder.CreateForRSA(m_issuerCert);
            }
            var x509Crl = new X509CRL(i509Crl.RawData);
            Assert.NotNull(x509Crl);
            Assert.NotNull(x509Crl.CrlExtensions);
            Assert.NotNull(x509Crl.RevokedCertificates);
            Assert.AreEqual(m_issuerCert.SubjectName.RawData, x509Crl.IssuerName.RawData);
            Assert.AreEqual(crlBuilder.ThisUpdate, x509Crl.ThisUpdate);
            Assert.AreEqual(crlBuilder.NextUpdate, x509Crl.NextUpdate);

            if (empty)
            {
                Assert.AreEqual(0, x509Crl.RevokedCertificates.Count);
            }
            else
            {
                Assert.AreEqual(2, x509Crl.RevokedCertificates.Count);
                Assert.AreEqual(serial, x509Crl.RevokedCertificates[0].UserCertificate);
                Assert.AreEqual(serstring, x509Crl.RevokedCertificates[1].SerialNumber);
            }

            if (noExtensions)
            {
                Assert.AreEqual(0, x509Crl.CrlExtensions.Count);
            }
            else
            {
                Assert.AreEqual(2, x509Crl.CrlExtensions.Count);
            }

            using X509Certificate2 issuerPubKey = CertificateFactory.Create(
                m_issuerCert.RawData);
            Assert.True(x509Crl.VerifySignature(issuerPubKey, true));
        }

        /// <summary>
        /// Validate the full CRL encoder and decoder pass.
        /// </summary>
        [Theory]
        public void CrlBuilderTestWithSignatureGenerator(KeyHashPair keyHashPair)
        {
            CrlBuilder crlBuilder = CrlBuilder
                .Create(m_issuerCert.SubjectName, keyHashPair.HashAlgorithmName)
                .SetThisUpdate(DateTime.UtcNow.Date)
                .SetNextUpdate(DateTime.UtcNow.Date.AddDays(30));

            // little endian byte array as serial number?
            byte[] serial = [4, 5, 6, 7];
            var revokedarray = new RevokedCertificate(serial);
            crlBuilder.RevokedCertificates.Add(revokedarray);

            const string serstring = "709876543210";
            var revokedstring = new RevokedCertificate(serstring);
            crlBuilder.RevokedCertificates.Add(revokedstring);

            crlBuilder.CrlExtensions.Add(X509Extensions.BuildCRLNumber(1111));
            crlBuilder.CrlExtensions.Add(m_issuerCert.BuildAuthorityKeyIdentifier());

            IX509CRL ix509Crl;
            if (X509PfxUtils.IsECDsaSignature(m_issuerCert))
            {
                using ECDsa ecdsa = m_issuerCert.GetECDsaPrivateKey();
                var generator = X509SignatureGenerator.CreateForECDsa(ecdsa);
                ix509Crl = crlBuilder.CreateSignature(generator);
            }
            else
            {
                using RSA rsa = m_issuerCert.GetRSAPrivateKey();
                var generator = X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1);
                ix509Crl = crlBuilder.CreateSignature(generator);
            }
            var x509Crl = new X509CRL(ix509Crl);
            Assert.NotNull(x509Crl);
            Assert.NotNull(x509Crl.CrlExtensions);
            Assert.NotNull(x509Crl.RevokedCertificates);
            Assert.AreEqual(m_issuerCert.SubjectName.RawData, x509Crl.IssuerName.RawData);
            Assert.AreEqual(crlBuilder.ThisUpdate, x509Crl.ThisUpdate);
            Assert.AreEqual(crlBuilder.NextUpdate, x509Crl.NextUpdate);
            Assert.AreEqual(2, x509Crl.RevokedCertificates.Count);
            Assert.AreEqual(serial, x509Crl.RevokedCertificates[0].UserCertificate);
            Assert.AreEqual(serstring, x509Crl.RevokedCertificates[1].SerialNumber);
            Assert.AreEqual(2, x509Crl.CrlExtensions.Count);
            using X509Certificate2 issuerPubKey = CertificateFactory.Create(
                m_issuerCert.RawData);
            Assert.True(x509Crl.VerifySignature(issuerPubKey, true));
        }

        /// <summary>
        /// Validate a CRL Builder and decoder pass on using utc and generalized times.
        /// </summary>
        [Test]
        public void CrlUtcAndGeneralizedTimeTest()
        {
            // Generate a CRL with dates over 2050
            var dname = new X500DistinguishedName("CN=Test, O=OPC Foundation");
            HashAlgorithmName hash = HashAlgorithmName.SHA256;
            var baseYear = new DateTime(2055, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            CrlBuilder crlBuilder = CrlBuilder
                .Create(dname, hash)
                .SetThisUpdate(baseYear)
                .SetNextUpdate(baseYear.AddDays(100));
            byte[] serial = [4, 5, 6, 7];
            var revokedarray = new RevokedCertificate(serial)
            {
                RevocationDate = baseYear.AddDays(1)
            };
            crlBuilder.RevokedCertificates.Add(revokedarray);
            const string serstring = "45678910";
            var revokedstring = new RevokedCertificate(serstring)
            {
                RevocationDate = baseYear.AddDays(1)
            };
            crlBuilder.RevokedCertificates.Add(revokedstring);
            crlBuilder.CrlExtensions.Add(X509Extensions.BuildCRLNumber(123));
            byte[] crlEncoded = crlBuilder.Encode();
            Assert.NotNull(crlEncoded);
            ValidateCRL(serial, serstring, hash, crlBuilder, crlEncoded);

            // Generate a CRL with dates up-to 2050
            baseYear = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            crlBuilder = CrlBuilder.Create(dname, hash).SetThisUpdate(baseYear)
                .SetNextUpdate(baseYear.AddDays(100));
            revokedarray = new RevokedCertificate(serial);
            crlBuilder.RevokedCertificates.Add(revokedarray);
            revokedstring = new RevokedCertificate(serstring)
            {
                RevocationDate = baseYear.AddDays(20)
            };
            crlBuilder.RevokedCertificates.Add(revokedstring);
            crlBuilder.CrlExtensions.Add(X509Extensions.BuildCRLNumber(123));
            crlEncoded = crlBuilder.Encode();
            Assert.NotNull(crlEncoded);
            ValidateCRL(serial, serstring, hash, crlBuilder, crlEncoded);
        }

        private static string WriteCRL(X509CRL x509Crl)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append("Issuer:     ").AppendLine(x509Crl.Issuer)
                .Append("ThisUpdate: ").Append(x509Crl.ThisUpdate).AppendLine()
                .Append("NextUpdate: ").Append(x509Crl.NextUpdate).AppendLine()
                .AppendLine("RevokedCertificates:");
            foreach (RevokedCertificate revokedCert in x509Crl.RevokedCertificates)
            {
                stringBuilder
                    .AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0:20}, ",
                        revokedCert.SerialNumber)
                    .Append(revokedCert.RevocationDate)
                    .Append(", ");
                foreach (X509Extension entryExt in revokedCert.CrlEntryExtensions)
                {
                    stringBuilder.Append(entryExt.Format(false)).Append(' ');
                }
                stringBuilder.AppendLine(string.Empty);
            }
            stringBuilder.AppendLine("Extensions:");
            foreach (X509Extension extension in x509Crl.CrlExtensions)
            {
                stringBuilder.AppendLine(extension.Format(false));
            }
            return stringBuilder.ToString();
        }

        private static void ValidateCRL(
            byte[] serial,
            string serstring,
            HashAlgorithmName hash,
            CrlBuilder crlBuilder,
            byte[] crlEncoded)
        {
            Assert.NotNull(crlEncoded);
            var x509Crl = new X509CRL();
            x509Crl.DecodeCrl(crlEncoded);
            Assert.NotNull(x509Crl);
            Assert.NotNull(x509Crl.CrlExtensions);
            Assert.NotNull(x509Crl.RevokedCertificates);
            Assert.AreEqual(crlBuilder.IssuerName.RawData, x509Crl.IssuerName.RawData);
            NUnit.Framework.Assert.That(
                crlBuilder.ThisUpdate,
                Is.EqualTo(x509Crl.ThisUpdate).Within(TimeSpan.FromSeconds(1)));
            NUnit.Framework.Assert.That(
                crlBuilder.NextUpdate,
                Is.EqualTo(x509Crl.NextUpdate).Within(TimeSpan.FromSeconds(1)));
            Assert.AreEqual(2, x509Crl.RevokedCertificates.Count);
            Assert.AreEqual(serial, x509Crl.RevokedCertificates[0].UserCertificate);
            Assert.AreEqual(serstring, x509Crl.RevokedCertificates[1].SerialNumber);
            Assert.AreEqual(1, x509Crl.CrlExtensions.Count);
            Assert.AreEqual(hash, x509Crl.HashAlgorithmName);
        }

        private X509Certificate2 m_issuerCert;
        private readonly NodeId m_certificateType;
    }
}
