/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Opc.Ua;
using Opc.Ua.Security.Certificates;

public static partial class Testcases
{

    public enum TestCaseEncoders : int
    {
        Binary = 0,
        Json = 1,
        Xml = 2,
        Certificates = 3,
        CRLs = 4
    };

    public static string[] TestcaseEncoderSuffixes = new string[] { ".Binary", ".Json", ".Xml", ".Certificates", ".CRLs" };

    public static void Run(string workPath)
    {
        // Create the Testcases for the binary decoder.
        string pathSuffix = TestcaseEncoderSuffixes[(int)TestCaseEncoders.Binary];
        string pathTarget = workPath + pathSuffix + Path.DirectorySeparatorChar;
        foreach (var messageEncoder in MessageEncoders)
        {
            byte[] message;
            using (var encoder = new BinaryEncoder(MessageContext))
            {
                messageEncoder(encoder);
                message = encoder.CloseAndReturnBuffer();
            }

            // ensure the test case does not throw an exception
            TestNewRawDataMessage(message);

            // ensure it does not throw an exception
            using (var stream = new MemoryStream(message))
            {
                FuzzableCode.FuzzBinaryDecoderCore(stream, true);
            }

            string fileName = Path.Combine(pathTarget, $"{messageEncoder.Method.Name}.bin".ToLowerInvariant());
            File.WriteAllBytes(fileName, message);
        }

        // Create the Testcases for the json decoder.
        pathSuffix = TestcaseEncoderSuffixes[(int)TestCaseEncoders.Json];
        pathTarget = workPath + pathSuffix + Path.DirectorySeparatorChar;
        foreach (var messageEncoder in MessageEncoders)
        {
            byte[] message;
            using (var memoryStream = new MemoryStream(0x1000))
            using (var encoder = new JsonEncoder(MessageContext, true, false, memoryStream))
            {
                messageEncoder(encoder);
                encoder.Close();
                message = memoryStream.ToArray();
            }

            // Test the fuzz targets with the message.
            TestNewUtf8Message(message);

            // ensure the test case does not throw an exception
            string json = Encoding.UTF8.GetString(message);
            FuzzableCode.FuzzJsonDecoderCore(json, true);

            string fileName = Path.Combine(pathTarget, $"{messageEncoder.Method.Name}.json".ToLowerInvariant());
            File.WriteAllBytes(fileName, message);
        }

        // Create the Testcases for the xml decoder.
        pathSuffix = TestcaseEncoderSuffixes[(int)TestCaseEncoders.Xml];
        pathTarget = workPath + pathSuffix + Path.DirectorySeparatorChar;
        foreach (var messageEncoder in MessageEncoders)
        {
            string xml;
            using (var encoder = new XmlEncoder(MessageContext))
            {
                encoder.SetMappingTables(MessageContext.NamespaceUris, MessageContext.ServerUris);
                messageEncoder(encoder);
                xml = encoder.CloseAndReturnText();
            }

            // Test the fuzz targets with the message.
            byte[] message = Encoding.UTF8.GetBytes(xml);
            using (var stream = new MemoryStream(message))
            {
                FuzzableCode.FuzzXmlDecoderCore(stream, true);
            }

            TestNewUtf8Message(message);

            string fileName = Path.Combine(pathTarget, $"{messageEncoder.Method.Name}.xml".ToLowerInvariant());
            File.WriteAllBytes(fileName, Encoding.UTF8.GetBytes(xml));
        }

        // Create the Testcases for the certificate decoder.
        pathSuffix = TestcaseEncoderSuffixes[(int)TestCaseEncoders.Certificates];
        pathTarget = workPath + pathSuffix + Path.DirectorySeparatorChar;
        X509Certificate2 rootCert = null;
        X509Certificate2 certificate = null;
        {
            var crlName = "root.crl";
            var crlServerUrl = "http://127.0.0.1:1234/";
            var crlExtension = X509Extensions.BuildX509CRLDistributionPoints(crlServerUrl + crlName);

            // create the root cert
            rootCert = CertificateFactory.CreateCertificate("CN=Root")
                .AddExtension(crlExtension)
                .SetLifeTime(25 * 12)
                .SetCAConstraint()
                .SetRSAKeySize(4096)
                .CreateForRSA();

            certificate = CertificateFactory.CreateCertificate(
                    "urn:op:ua:application:test",
                    "TestApp",
                    "CN=TestApp",
                    new string[] { "test.com", "192.168.1.111", "c034:777f:9abc::1234:5678" })
                    .SetLifeTime(12)
                    .SetIssuer(rootCert)
                    .SetRSAKeySize(2048)
                    .CreateForRSA();

            // Test the fuzz targets with the message.
            byte[] rawData = certificate.RawData;
            TestNewRawDataMessage(rawData);

            // should not throw an exception
            _ = X509CertificateLoader.LoadCertificate(rawData);
            FuzzableCode.FuzzCertificateChainDecoderCore(rawData,false);
            FuzzableCode.FuzzCertificateChainDecoderCore(rawData, true);

            string fileName = Path.Combine(pathTarget, "certificate.bin");
            File.WriteAllBytes(fileName, rawData);

            // Test the fuzz targets with the message.
            rawData = rootCert.RawData;
            TestNewRawDataMessage(rawData);

            // should not throw an exception
            _ = X509CertificateLoader.LoadCertificate(rawData);
            FuzzableCode.FuzzCertificateChainDecoderCore(rawData, false);
            FuzzableCode.FuzzCertificateChainDecoderCore(rawData, true);

            fileName = Path.Combine(pathTarget, "rootcertificate.bin");
            File.WriteAllBytes(fileName, rawData);

            // Create a binary blob chain.
            rawData = new byte[certificate.RawData.Length + rootCert.RawData.Length];
            certificate.RawData.CopyTo(rawData, 0);
            rootCert.RawData.CopyTo(rawData, certificate.RawData.Length);
            TestNewRawDataMessage(rawData);
            FuzzableCode.FuzzCertificateChainDecoderCore(rawData, false);
            FuzzableCode.FuzzCertificateChainDecoderCore(rawData, true);

            // should not throw an exception
            _ = X509CertificateLoader.LoadCertificate(rawData);
            _ = Utils.ParseCertificateBlob(rawData, false);
            _ = Utils.ParseCertificateBlob(rawData, true);
            _ = Utils.ParseCertificateChainBlob(rawData, false);
            _ = Utils.ParseCertificateChainBlob(rawData, true);

            fileName = Path.Combine(pathTarget, "certificatechain.bin");
            File.WriteAllBytes(fileName, rawData);
        }

        // Create the Testcases for the CRL decoder.
        pathSuffix = TestcaseEncoderSuffixes[(int)TestCaseEncoders.CRLs];
        pathTarget = workPath + pathSuffix + Path.DirectorySeparatorChar;
        {
            // create an empty CRL
            X509CRL rootCrl = CertificateFactory.RevokeCertificate(rootCert, null, null);

            // Test the fuzz targets with the message.
            byte[] rawData = rootCrl.RawData;
            TestNewRawDataMessage(rawData);

            string fileName = Path.Combine(pathTarget, "emptyroot.crl");
            File.WriteAllBytes(fileName, rawData);

            // create a CRL with a revoked certificate
            X509CRL revokedRootCrl = CertificateFactory.RevokeCertificate(rootCert, null, new X509Certificate2Collection(certificate));

            // Test the fuzz targets with the message.
            rawData = revokedRootCrl.RawData;
            TestNewRawDataMessage(rawData);

            fileName = Path.Combine(pathTarget, "root.crl");
            File.WriteAllBytes(fileName, rawData);

            // create a CRL which revokes the leaf certificate
            CrlBuilder crlBuilder = CrlBuilder.Create(certificate.IssuerName);
            IX509CRL x509CRL = crlBuilder
                .AddRevokedCertificate(certificate, CRLReason.PrivilegeWithdrawn)
                .CreateForRSA(certificate);

            // Test the fuzz targets with the message.
            rawData = x509CRL.RawData;
            TestNewRawDataMessage(rawData);

            fileName = Path.Combine(pathTarget, "leaf.crl");
            File.WriteAllBytes(fileName, rawData);
        }
    }

    private static void TestNewRawDataMessage(byte[] message)
    {
        FuzzableCode.LibfuzzBinaryDecoder(message);
        FuzzableCode.LibfuzzBinaryEncoder(message);
        using (var stream = new MemoryStream(message))
        {
            FuzzableCode.AflfuzzBinaryDecoder(stream);
        }
        using (var stream = new MemoryStream(message))
        {
            FuzzableCode.AflfuzzBinaryEncoder(stream);
        }
        using (var stream = new MemoryStream(message))
        {
            FuzzableCode.FuzzBinaryDecoderCore(stream);
        }
        FuzzableCode.LibfuzzXmlDecoder(message);
        FuzzableCode.LibfuzzXmlEncoder(message);
        FuzzableCode.LibfuzzCertificateDecoder(message);
        FuzzableCode.LibfuzzCertificateChainDecoder(message);
        FuzzableCode.LibfuzzCertificateChainDecoderCustom(message);
        FuzzableCode.LibfuzzCRLDecoder(message);
        FuzzableCode.LibfuzzCRLEncoder(message);
    }

    private static void TestNewUtf8Message(byte[] message)
    {
        FuzzableCode.LibfuzzJsonDecoder(message);
        FuzzableCode.LibfuzzJsonEncoder(message);
        FuzzableCode.LibfuzzXmlDecoder(message);
        FuzzableCode.LibfuzzXmlEncoder(message);
        FuzzableCode.LibfuzzCertificateDecoder(message);
        FuzzableCode.LibfuzzCertificateChainDecoder(message);
        FuzzableCode.LibfuzzCertificateChainDecoderCustom(message);
        FuzzableCode.LibfuzzCRLDecoder(message);
        FuzzableCode.LibfuzzCRLEncoder(message);

        // string targets
        string json = Encoding.UTF8.GetString(message);
        FuzzableCode.AflfuzzJsonDecoder(json);
        FuzzableCode.AflfuzzJsonEncoder(json);
        FuzzableCode.FuzzJsonDecoderCore(json);
    }
}
