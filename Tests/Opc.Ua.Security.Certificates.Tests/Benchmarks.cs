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
using BenchmarkDotNet.Jobs;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Benchmarks for the CertificateFactory class.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(RuntimeMoniker.Net462, baseline: true)]
    [SimpleJob(RuntimeMoniker.NetCoreApp31)]
    [SimpleJob(RuntimeMoniker.NetCoreApp21)]
    public class Benchmarks
    {
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
            m_certificate = CertificateBuilder.Create("CN=TestCert")
                .SetNotBefore(DateTime.Today.AddDays(-1))
                .AddExtension(
                    new X509SubjectAltNameExtension("urn:opcfoundation.org:mypc", new string[] { "mypc", "mypc.opcfoundation.org", "192.168.1.100" }))
                .CreateForRSA();

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
        /// Find a specific cert extension.
        /// </summary>
        [Benchmark]
        public void FindExtension()
        {
            _ = X509Extensions.FindExtension<X509BasicConstraintsExtension>(m_certificate.Extensions);
        }
    }
}
