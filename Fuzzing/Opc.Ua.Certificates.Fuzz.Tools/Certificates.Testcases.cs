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
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Fuzzing
{
    public static partial class Testcases
    {
        /// <summary>
        /// Run the certificate test cases.
        /// </summary>
        /// <param name="workPath">The base testcase work path.</param>
        /// <param name="telemetry">The telemetry context to use to create observability instruments.</param>
        public static void Run(string workPath, ITelemetryContext telemetry)
        {
            _ = telemetry;

            using Certificate issuerCertificate = CertificateBuilder
                .Create("CN=Fuzzing Test Root, O=OPC Foundation")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(30))
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();

            ICertificateBuilder applicationCertificateBuilder = CertificateBuilder
                .Create("CN=Fuzzing Test Application, O=OPC Foundation")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(10))
                .AddExtension(
                    new X509SubjectAltNameExtension(
                        "urn:opcfoundation.org:fuzzing",
                        ["localhost", "127.0.0.1"]));
            applicationCertificateBuilder.SetIssuer(issuerCertificate);

            using Certificate applicationCertificate = applicationCertificateBuilder
                .SetRSAKeySize(2048)
                .CreateForRSA();

            byte[] certificateDer = applicationCertificate.RawData;
            WriteTestcase(workPath, "X509Cert", "certificate.der", certificateDer);

            // Expired RSA certificate: NotAfter in the past — exercises validity-window logic.
            using Certificate expiredCertificate = CertificateBuilder
                .Create("CN=Fuzzing Test Expired, O=OPC Foundation")
                .SetNotBefore(DateTime.UtcNow.AddDays(-30))
                .SetNotAfter(DateTime.UtcNow.AddDays(-1))
                .AddExtension(
                    new X509SubjectAltNameExtension(
                        "urn:opcfoundation.org:fuzzing:expired",
                        ["localhost"]))
                .SetIssuer(issuerCertificate)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            WriteTestcase(workPath, "X509Cert", "certificate-expired.der", expiredCertificate.RawData);

            // Self-signed RSA certificate: no separate issuer — exercises the
            // self-signature decoder path that skips chain construction.
            using Certificate selfSignedCertificate = CertificateBuilder
                .Create("CN=Fuzzing Test SelfSigned, O=OPC Foundation")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(10))
                .AddExtension(
                    new X509SubjectAltNameExtension(
                        "urn:opcfoundation.org:fuzzing:selfsigned",
                        ["localhost", "127.0.0.1", "::1"]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            WriteTestcase(workPath, "X509Cert", "certificate-selfsigned.der", selfSignedCertificate.RawData);

            // ECC P-256 application certificate + matching PEM private key.
            // Exercises the ECDsa decode branches in cert + PEM parsers.
            // A dedicated ECC issuer is required because CertificateBuilder.CreateForECDsa
            // signs with the issuer's ECDsa private key — an RSA-only issuer is rejected.
            using Certificate eccIssuerCertificate = CertificateBuilder
                .Create("CN=Fuzzing Test ECC Root, O=OPC Foundation")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(30))
                .SetCAConstraint()
                .SetECCurve(ECCurve.NamedCurves.nistP256)
                .CreateForECDsa();

            ICertificateBuilder eccApplicationBuilder = CertificateBuilder
                .Create("CN=Fuzzing Test ECC, O=OPC Foundation")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(10))
                .AddExtension(
                    new X509SubjectAltNameExtension(
                        "urn:opcfoundation.org:fuzzing:ecc",
                        ["localhost"]));
            eccApplicationBuilder.SetIssuer(eccIssuerCertificate);

            using Certificate eccApplicationCertificate = eccApplicationBuilder
                .SetECCurve(ECCurve.NamedCurves.nistP256)
                .CreateForECDsa();
            WriteTestcase(workPath, "X509Cert", "certificate-ecc.der", eccApplicationCertificate.RawData);

            // CRL with a single revoked entry (original).
            var crlBuilder = CrlBuilder
                .Create(issuerCertificate.SubjectName)
                .SetThisUpdate(DateTime.UtcNow.AddDays(-1))
                .SetNextUpdate(DateTime.UtcNow.AddDays(7))
                .AddRevokedCertificate(applicationCertificate)
                .AddCRLExtension(new X509CrlNumberExtension(BigInteger.One));
            IX509CRL crl = crlBuilder.CreateForRSA(issuerCertificate);
            byte[] crlDer = crl.RawData;
            WriteTestcase(workPath, "X509CRL", "crl.der", crlDer);

            // CRL with multiple revoked entries (exercises the revoked-list loop in EnsureDecoded).
            var crlMultiBuilder = CrlBuilder
                .Create(issuerCertificate.SubjectName)
                .SetThisUpdate(DateTime.UtcNow.AddDays(-2))
                .SetNextUpdate(DateTime.UtcNow.AddDays(14))
                .AddRevokedCertificate(applicationCertificate)
                .AddRevokedCertificate(expiredCertificate)
                .AddRevokedCertificate(selfSignedCertificate)
                .AddCRLExtension(new X509CrlNumberExtension(new BigInteger(42)));
            IX509CRL crlMulti = crlMultiBuilder.CreateForRSA(issuerCertificate);
            WriteTestcase(workPath, "X509CRL", "crl-multi-revoked.der", crlMulti.RawData);

            // Empty CRL (no revoked entries) — boundary case for the loop.
            var crlEmptyBuilder = CrlBuilder
                .Create(issuerCertificate.SubjectName)
                .SetThisUpdate(DateTime.UtcNow.AddDays(-1))
                .SetNextUpdate(DateTime.UtcNow.AddDays(7))
                .AddCRLExtension(new X509CrlNumberExtension(BigInteger.Zero));
            IX509CRL crlEmpty = crlEmptyBuilder.CreateForRSA(issuerCertificate);
            WriteTestcase(workPath, "X509CRL", "crl-empty.der", crlEmpty.RawData);

            // RSA CSR (original).
            byte[] csrDer = DefaultCertificateFactory.Instance.CreateSigningRequest(
                applicationCertificate,
                ["localhost"]);
            WriteTestcase(workPath, "X509CSR", "request.der", csrDer);

            // ECC CSR — exercises the ECDsa-signed PKCS#10 decode branch.
            byte[] eccCsrDer = DefaultCertificateFactory.Instance.CreateSigningRequest(
                eccApplicationCertificate,
                ["localhost"]);
            WriteTestcase(workPath, "X509CSR", "request-ecc.der", eccCsrDer);

            // CSR with multiple SAN entries — exercises the extension-request decode loop.
            byte[] csrMultiSanDer = DefaultCertificateFactory.Instance.CreateSigningRequest(
                applicationCertificate,
                ["localhost", "127.0.0.1", "::1", "fuzz.example.com"]);
            WriteTestcase(workPath, "X509CSR", "request-multi-san.der", csrMultiSanDer);

            byte[] certificatePem = PEMWriter.ExportCertificateAsPEM(applicationCertificate);
            WriteTestcase(workPath, "PEM", "certificate.pem", certificatePem);

            byte[] privateKeyPem = PEMWriter.ExportPrivateKeyAsPEM(applicationCertificate);
            WriteTestcase(workPath, "PEM", "private-key.pem", privateKeyPem);

            // ECC PEM cert + key.
            byte[] eccCertificatePem = PEMWriter.ExportCertificateAsPEM(eccApplicationCertificate);
            WriteTestcase(workPath, "PEM", "certificate-ecc.pem", eccCertificatePem);

            byte[] eccPrivateKeyPem = PEMWriter.ExportPrivateKeyAsPEM(eccApplicationCertificate);
            WriteTestcase(workPath, "PEM", "private-key-ecc.pem", eccPrivateKeyPem);

            byte[] subjectAltName = new X509SubjectAltNameExtension(
                "urn:opcfoundation.org:fuzzing",
                ["localhost", "127.0.0.1"]).RawData;
            WriteTestcase(workPath, "ASN1", "subject-alt-name.der", subjectAltName);

            byte[] authorityKeyIdentifier = new Opc.Ua.Security.Certificates.X509AuthorityKeyIdentifierExtension(
                [0x01, 0x02, 0x03, 0x04]).RawData;
            WriteTestcase(workPath, "ASN1", "authority-key-identifier.der", authorityKeyIdentifier);

            byte[] crlNumber = new X509CrlNumberExtension(BigInteger.One).RawData;
            WriteTestcase(workPath, "ASN1", "crl-number.der", crlNumber);
            WriteTestcase(workPath, "ASN1", "sequence.der", CreateSequenceSeed());

            // Additional ASN.1 boundary seeds — exercise the AsnReader entry points
            // with structural variants beyond the canonical extension layouts.
            WriteTestcase(workPath, "ASN1", "nested-sequence.der", CreateNestedSequenceSeed());
            WriteTestcase(workPath, "ASN1", "large-integer.der", CreateLargeIntegerSeed());
            WriteTestcase(workPath, "ASN1", "subject-alt-name-many.der",
                new X509SubjectAltNameExtension(
                    "urn:opcfoundation.org:fuzzing:many",
                    ["host-a", "host-b", "host-c", "host-d", "host-e",
                     "127.0.0.1", "::1", "10.0.0.1", "10.0.0.2"]).RawData);

            FuzzableCode.FuzzX509CRLCore(crlDer);
            FuzzableCode.FuzzX509SubjectAltNameExtensionCore(subjectAltName);
            FuzzableCode.FuzzX509AuthorityKeyIdentifierExtensionCore(authorityKeyIdentifier);
            FuzzableCode.FuzzX509CrlNumberExtensionCore(crlNumber);
            FuzzableCode.FuzzPemImportCertificateCore(certificatePem);
            FuzzableCode.FuzzPemImportPrivateKeyCore(privateKeyPem);
            FuzzableCode.FuzzPkcs10CertificationRequestCore(csrDer);
            FuzzableCode.FuzzAsnUtilsX509BlobCore(certificateDer);
            FuzzableCode.FuzzAsnReaderSequenceCore(CreateSequenceSeed());
            FuzzableCode.FuzzAsnReaderIntegerCore(crlNumber);
        }

        private static void WriteTestcase(
            string workPath,
            string testcaseName,
            string fileName,
            byte[] content)
        {
            string pathTarget = workPath + "." + testcaseName + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(pathTarget);
            File.WriteAllBytes(Path.Combine(pathTarget, fileName), content);
        }

        private static byte[] CreateSequenceSeed()
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.WriteInteger(1);
            writer.WriteObjectIdentifier(X509SubjectAltNameExtension.SubjectAltName2Oid);
            writer.WriteNull();
            writer.PopSequence();
            return writer.Encode();
        }

        private static byte[] CreateNestedSequenceSeed()
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.WriteInteger(2);
            writer.PushSequence();
            writer.WriteObjectIdentifier(X509SubjectAltNameExtension.SubjectAltName2Oid);
            writer.PushOctetString();
            writer.WriteInteger(0x7fffffff);
            writer.PopOctetString();
            writer.PopSequence();
            writer.PopSequence();
            return writer.Encode();
        }

        private static byte[] CreateLargeIntegerSeed()
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            // 256-bit BigInteger — exercises AsnReader.ReadInteger past the
            // 32 / 64-bit fast paths into the multi-limb branch.
            var big = (BigInteger.One << 255) - BigInteger.One;
            writer.WriteInteger(big);
            return writer.Encode();
        }
    }
}
