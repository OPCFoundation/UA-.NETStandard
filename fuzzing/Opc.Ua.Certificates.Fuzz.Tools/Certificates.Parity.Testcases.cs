/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Fuzzing
{
    public static partial class Testcases
    {
        private static void WriteCertificateChainTestcases(
            string workPath,
            Certificate issuer,
            Certificate application,
            Certificate eccIssuer,
            Certificate eccApplication)
        {
            WriteCertificateChainTestcase(workPath, "chain-single.der", application);
            WriteCertificateChainTestcase(workPath, "chain-rsa.der", application, issuer);
            WriteCertificateChainTestcase(workPath, "chain-ecc.der", eccApplication, eccIssuer);

            using Certificate intermediate = CertificateBuilder
                .Create("CN=Fuzzing Test Intermediate, O=OPC Foundation")
                .SetNotBefore(s_certificateFixtureTime.AddDays(-1))
                .SetNotAfter(s_certificateFixtureTime.AddYears(10))
                .SetCAConstraint()
                .SetIssuer(issuer)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=Fuzzing Test Chain Leaf, O=OPC Foundation")
                .SetNotBefore(s_certificateFixtureTime)
                .SetNotAfter(s_certificateFixtureTime.AddYears(5))
                .SetIssuer(intermediate)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            WriteCertificateChainTestcase(workPath, "chain-three.der", leaf, intermediate, issuer);

            var extensionWriter = new AsnWriter(AsnEncodingRules.DER);
            extensionWriter.WriteOctetString(new byte[65536]);
            using Certificate longCertificate = CertificateBuilder
                .Create("CN=Fuzzing Test Long DER Length, O=OPC Foundation")
                .SetNotBefore(s_certificateFixtureTime)
                .SetNotAfter(s_certificateFixtureTime.AddYears(5))
                .AddExtension(new X509Extension("1.2.3.4", extensionWriter.Encode(), false))
                .SetIssuer(issuer)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            WriteCertificateTestcase(workPath, "certificate-long-length.der", longCertificate.RawData);
            WriteCertificateChainTestcase(workPath, "chain-long-length.der", longCertificate, issuer);
        }

        private static void WriteCrlParityTestcases(
            string workPath,
            Certificate issuer,
            Certificate eccIssuer)
        {
            CrlBuilder minimal = CrlBuilder.Create(issuer.SubjectName)
                .SetThisUpdate(s_certificateFixtureTime);
            WriteCrlTestcase(workPath, "crl-minimal.der", minimal.CreateForRSA(issuer), issuer);

            CrlBuilder ecc = CrlBuilder.Create(eccIssuer.SubjectName)
                .SetThisUpdate(s_certificateFixtureTime)
                .SetNextUpdate(s_certificateFixtureTime.AddDays(7))
                .AddRevokedCertificate(new RevokedCertificate([0x80, 0x00], CRLReason.KeyCompromise)
                {
                    RevocationDate = s_certificateFixtureTime.AddDays(-1)
                })
                .AddCRLExtension(new X509CrlNumberExtension(new BigInteger(128)));
            WriteCrlTestcase(workPath, "crl-ecc.der", ecc.CreateForECDsa(eccIssuer), eccIssuer);

            var firstEntry = new RevokedCertificate([0x01, 0x00, 0x80, 0x00], CRLReason.KeyCompromise)
            {
                RevocationDate = s_certificateFixtureTime.AddDays(-1)
            };
            firstEntry.CrlEntryExtensions.Add(new X509Extension("1.2.3.4", [0x04, 0x01, 0x2A], true));
            CrlBuilder noNextUpdate = CrlBuilder.Create(issuer.SubjectName)
                .SetThisUpdate(s_certificateFixtureTime)
                .AddRevokedCertificate(firstEntry)
                .AddRevokedCertificate(new RevokedCertificate([0x02])
                {
                    RevocationDate = s_certificateFixtureTime.AddDays(-2)
                })
                .AddCRLExtension(new X509CrlNumberExtension(new BigInteger(256)))
                .AddCRLExtension(new X509Extension("1.2.3.5", [0x02, 0x01, 0x07], true));
            WriteCrlTestcase(
                workPath, "crl-no-next-update.der", noNextUpdate.CreateForRSA(issuer), issuer);

            CrlBuilder timeBoundaries = CrlBuilder.Create(issuer.SubjectName)
                .SetThisUpdate(new DateTime(2049, 12, 31, 12, 0, 0, DateTimeKind.Utc))
                .SetNextUpdate(new DateTime(2050, 1, 1, 12, 0, 0, DateTimeKind.Utc))
                .AddRevokedCertificate(new RevokedCertificate([0x01])
                {
                    RevocationDate = new DateTime(1949, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                })
                .AddRevokedCertificate(new RevokedCertificate([0x02])
                {
                    RevocationDate = new DateTime(1950, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                });
            WriteCrlTestcase(
                workPath, "crl-time-boundaries.der", timeBoundaries.CreateForRSA(issuer), issuer);

            foreach (HashAlgorithmName hash in new[] { HashAlgorithmName.SHA384, HashAlgorithmName.SHA512 })
            {
                CrlBuilder builder = CrlBuilder.Create(issuer.SubjectName, hash)
                    .SetThisUpdate(s_certificateFixtureTime)
                    .SetNextUpdate(s_certificateFixtureTime.AddDays(7));
                WriteCrlTestcase(
                    workPath,
                    "crl-" + hash.Name.ToLowerInvariant() + ".der",
                    builder.CreateForRSA(issuer),
                    issuer);
            }

            WriteCrlTestcase(workPath, "crl-version-one.der", CreateVersionOneCrl(issuer), issuer);
        }

        private static X509CRL CreateVersionOneCrl(Certificate issuer)
        {
            using RSA key = issuer.GetRSAPrivateKey()
                ?? throw new CryptographicException("The generated CRL issuer has no RSA private key.");
            var generator = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
            byte[] algorithm = generator.GetSignatureAlgorithmIdentifier(HashAlgorithmName.SHA256);
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.WriteEncodedValue(algorithm);
            writer.WriteEncodedValue(issuer.SubjectName.RawData);
            writer.WriteUtcTime(s_certificateFixtureTime);
            writer.PopSequence();
            byte[] tbs = writer.Encode();
            byte[] signature = generator.SignData(tbs, HashAlgorithmName.SHA256);
            return new X509CRL(new X509Signature(tbs, signature, algorithm).Encode());
        }

        private static RevokedCertificate CreateRevokedCertificate(
            Certificate certificate,
            DateTime revocationDate,
            CRLReason reason)
        {
            return new RevokedCertificate(certificate.SerialNumber, reason)
            {
                RevocationDate = revocationDate
            };
        }

        private static void WriteCertificateTestcase(string workPath, string fileName, byte[] certificate)
        {
            if (!FuzzableCode.FuzzCertificateDecoderCore(certificate))
            {
                throw new InvalidOperationException("The generated certificate was rejected: " + fileName);
            }
            WriteTestcase(workPath, "X509Cert", fileName, certificate);
        }

        private static void WriteCertificateChainTestcase(
            string workPath,
            string fileName,
            params Certificate[] certificates)
        {
            using var chain = new CertificateCollection(certificates);
            byte[] blob = Utils.CreateCertificateChainBlob(chain);
            if (!FuzzableCode.FuzzCertificateChainDecoderCore(blob, useAsnParser: false) ||
                !FuzzableCode.FuzzCertificateChainDecoderCore(blob, useAsnParser: true))
            {
                throw new InvalidOperationException("The generated certificate chain was rejected: " + fileName);
            }
            WriteTestcase(workPath, "X509Chain", fileName, blob);
        }

        private static void WriteCrlTestcase(
            string workPath,
            string fileName,
            IX509CRL crl,
            Certificate issuer)
        {
            var decoded = new X509CRL(crl.RawData);
            _ = decoded.VerifySignature(issuer, throwOnError: true);
            if (!FuzzableCode.FuzzX509CRLCore(crl.RawData) ||
                !FuzzableCode.FuzzCRLEncoderCore(crl.RawData, roundTrip: false) ||
                !FuzzableCode.FuzzCRLEncoderCore(crl.RawData, roundTrip: true))
            {
                throw new InvalidOperationException("The generated CRL was rejected: " + fileName);
            }
            WriteTestcase(workPath, "X509CRL", fileName, crl.RawData);
        }

        private static readonly DateTime s_certificateFixtureTime = new(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
    }
}
