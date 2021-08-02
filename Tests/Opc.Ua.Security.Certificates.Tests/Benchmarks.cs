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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Benchmarks for the CertificateFactory class.
    /// </summary>
    [MemoryDiagnoser]
    public class Benchmarks
    {
        X509Certificate2 m_issuerCert;
        IX509CRL m_issuerCrl;
        X509CRL m_x509Crl;
        X509Certificate2 m_certificate;
        byte[] m_randomByteArray;
        byte[] m_encryptedByteArray;
        byte[] m_signature;
        RSA m_rsaPrivateKey;
        RSA m_rsaPublicKey;

        /// <summary>
        /// Setup variables for running benchmarks.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            m_issuerCert = CertificateBuilder.Create("CN=Root CA")
                            .SetCAConstraint()
                            .CreateForRSA();
            m_certificate = CertificateBuilder.Create("CN=TestCert")
                .SetNotBefore(DateTime.Today.AddDays(-1))
                .AddExtension(
                    new X509SubjectAltNameExtension("urn:opcfoundation.org:mypc",
                    new string[] { "mypc", "mypc.opcfoundation.org", "192.168.1.100" }))
                .CreateForRSA();

            var crlBuilder = CrlBuilder.Create(m_issuerCert.SubjectName, HashAlgorithmName.SHA256)
                           .SetThisUpdate(DateTime.UtcNow.Date)
                           .SetNextUpdate(DateTime.UtcNow.Date.AddDays(30));
            var revokedarray = new RevokedCertificate(m_certificate.SerialNumber);
            crlBuilder.RevokedCertificates.Add(revokedarray);
            crlBuilder.CrlExtensions.Add(X509Extensions.BuildCRLNumber(1));
            crlBuilder.CrlExtensions.Add(X509Extensions.BuildAuthorityKeyIdentifier(m_issuerCert));
            m_issuerCrl = crlBuilder.CreateForRSA(m_issuerCert);
            m_x509Crl = new X509CRL(m_issuerCrl.RawData);

            var random = new Random();
            m_rsaPrivateKey = m_certificate.GetRSAPrivateKey();
            m_rsaPublicKey = m_certificate.GetRSAPublicKey();

            // blob size for RSA padding OaepSHA256
            int blobSize = m_rsaPublicKey.KeySize / 8 - 66;
            m_randomByteArray = new byte[blobSize];
            random.NextBytes(m_randomByteArray);

            m_encryptedByteArray = m_rsaPublicKey.Encrypt(m_randomByteArray, RSAEncryptionPadding.OaepSHA256);
            m_signature = m_rsaPrivateKey.SignData(m_randomByteArray, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        /// <summary>
        /// Cleanup variables used in benchmarks.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
            m_issuerCert?.Dispose();
            m_certificate?.Dispose();
            m_rsaPrivateKey?.Dispose();
            m_rsaPublicKey?.Dispose();
        }


        /// <summary>
        /// Create a certificate and dispose.
        /// </summary>
        [Benchmark]
        public void CreateCertificate()
        {
            using (X509Certificate2 cert = CertificateBuilder.Create("CN=Create").CreateForRSA()) { }
        }

        /// <summary>
        /// Get the private key from a certificate and dispose it.
        /// </summary>
        [Benchmark]
        public void GetPrivateKey()
        {
            using (var privateKey = m_certificate.GetRSAPrivateKey()) { }
        }

        /// <summary>
        /// Get the private key from a certificate, export parameters and dispose it.
        /// </summary>
        [Benchmark]
        public void GetPrivateKeyAndExport()
        {
            using (var privateKey = m_certificate.GetRSAPrivateKey())
            {
                privateKey.ExportParameters(true);
            }
        }

        /// <summary>
        /// Get the public key from a certificate and dispose it.
        /// </summary>
        [Benchmark]
        public void GetPublicKey()
        {
            using (var publicKey = m_certificate.GetRSAPublicKey()) { }
        }

        /// <summary>
        /// Get the public key from a certificate, export parameters and dispose it.
        /// </summary>
        [Benchmark]
        public void GetPublicKeyAndExport()
        {
            using (var publicKey = m_certificate.GetRSAPublicKey())
            {
                publicKey.ExportParameters(false);
            }
        }

        /// <summary>
        /// Encrypt one blob with padding OAEP SHA256.
        /// </summary>
        [Benchmark]
        public void EncryptOAEPSHA256()
        {
            _ = m_rsaPublicKey.Encrypt(m_randomByteArray, RSAEncryptionPadding.OaepSHA256);
        }

        /// <summary>
        /// Decrypt one blob with padding OAEP SHA256.
        /// </summary>
        [Benchmark]
        public void DecryptOAEPSHA256()
        {
            _ = m_rsaPrivateKey.Decrypt(m_encryptedByteArray, RSAEncryptionPadding.OaepSHA256);
        }

        /// <summary>
        /// Verify signature of a random byte blob using SHA256 / PKCS#1.
        /// </summary>
        [Benchmark]
        public void VerifySHA256PKCS1()
        {
            _ = m_rsaPublicKey.VerifyData(m_randomByteArray, m_signature,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        /// <summary>
        /// Sign a random byte blob using SHA256 / PKCS#1.
        /// </summary>
        [Benchmark]
        public void SignSHA256PKCS1()
        {
            _ = m_rsaPrivateKey.SignData(m_randomByteArray,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        /// <summary>
        /// Verify a self signed signature.
        /// </summary>
        [Benchmark]
        public void VerifySignature()
        {
            var signature = new X509Signature(m_certificate.RawData);
            _ = signature.Verify(m_certificate);
        }

        /// <summary>
        /// Create a CRL.
        /// </summary>
        [Benchmark]
        public void CreateCRL()
        {
            // little endian byte array as serial number?
            byte[] serial = new byte[] { 1, 2, 3 };
            var revokedarray = new RevokedCertificate(serial);

            var crlBuilder = CrlBuilder.Create(m_issuerCert.SubjectName, HashAlgorithmName.SHA256)
                .SetThisUpdate(DateTime.UtcNow.Date)
                .SetNextUpdate(DateTime.UtcNow.Date.AddDays(30));
            crlBuilder.RevokedCertificates.Add(revokedarray);
            crlBuilder.CrlExtensions.Add(X509Extensions.BuildCRLNumber(1));
            crlBuilder.CrlExtensions.Add(X509Extensions.BuildAuthorityKeyIdentifier(m_issuerCert));
            _ = crlBuilder.CreateForRSA(m_issuerCert);
        }

        /// <summary>
        /// Decode a CRL.
        /// </summary>
        [Benchmark]
        public void DecodeCRLSignature()
        {
            _ = new X509CRL(m_issuerCrl.RawData);
        }

        /// <summary>
        /// Verify signature of a CRL.
        /// </summary>
        [Benchmark]
        public void VerifyCRLSignature()
        {
            _ = m_x509Crl.VerifySignature(m_issuerCert, true);
        }

        /// <summary>
        /// Find a specific cert extension.
        /// </summary>
        [Benchmark]
        public void FindExtension()
        {
            _ = X509Extensions.FindExtension<X509BasicConstraintsExtension>(m_certificate.Extensions);
        }

        /// <summary>
        /// Export certificate as PEM.
        /// </summary>
        [Benchmark]
        public void ExportCertificateAsPEM()
        {
            _ = PEMWriter.ExportCertificateAsPEM(m_certificate);
        }

        /// <summary>
        /// Export private key as PEM.
        /// </summary>
        [Benchmark]
        public void ExportPrivateKeyAsPEM()
        {
            _ = PEMWriter.ExportPrivateKeyAsPEM(m_certificate);
        }
    }
}
