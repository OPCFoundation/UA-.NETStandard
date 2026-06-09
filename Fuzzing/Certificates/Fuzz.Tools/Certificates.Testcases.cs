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

            var crlBuilder = CrlBuilder
                .Create(issuerCertificate.SubjectName)
                .SetThisUpdate(DateTime.UtcNow.AddDays(-1))
                .SetNextUpdate(DateTime.UtcNow.AddDays(7))
                .AddRevokedCertificate(applicationCertificate)
                .AddCRLExtension(new X509CrlNumberExtension(BigInteger.One));
            IX509CRL crl = crlBuilder.CreateForRSA(issuerCertificate);
            byte[] crlDer = crl.RawData;
            WriteTestcase(workPath, "X509CRL", "crl.der", crlDer);

            byte[] csrDer = DefaultCertificateFactory.Instance.CreateSigningRequest(
                applicationCertificate,
                ["localhost"]);
            WriteTestcase(workPath, "X509CSR", "request.der", csrDer);

            byte[] certificatePem = PEMWriter.ExportCertificateAsPEM(applicationCertificate);
            WriteTestcase(workPath, "PEM", "certificate.pem", certificatePem);

            byte[] privateKeyPem = PEMWriter.ExportPrivateKeyAsPEM(applicationCertificate);
            WriteTestcase(workPath, "PEM", "private-key.pem", privateKeyPem);

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
    }
}
