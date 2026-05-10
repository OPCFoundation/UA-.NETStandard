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

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// Test certificate helpers used by Security conformance tests. Mints
    /// self-signed user certs, CA roots, intermediate-issued chains, and CRLs
    /// with controllable validity / key-usage / subject so the tests can drive
    /// the server-side CertificateValidator down each rejection / acceptance
    /// path.
    /// </summary>
    /// <remarks>
    /// Uses System.Security.Cryptography directly so the helpers stay
    /// dependency-free and safe to call from any conformance test fixture.
    /// </remarks>
    internal static class TestCertificates
    {
        /// <summary>
        /// Creates a self-signed RSA-2048 user certificate.
        /// </summary>
        public static X509Certificate2 CreateSelfSignedUserCert(
            string cn = "CN=TestUser, O=OPC Foundation",
            DateTimeOffset? notBefore = null,
            DateTimeOffset? notAfter = null,
            X509KeyUsageFlags keyUsage =
                X509KeyUsageFlags.DigitalSignature |
                X509KeyUsageFlags.NonRepudiation |
                X509KeyUsageFlags.DataEncipherment |
                X509KeyUsageFlags.KeyEncipherment,
            int keySize = 2048)
        {
            using var rsa = RSA.Create(keySize);
            var certReq = new CertificateRequest(
                cn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            certReq.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));
            certReq.CertificateExtensions.Add(
                new X509KeyUsageExtension(keyUsage, false));

            DateTimeOffset nb = notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-5);
            DateTimeOffset na = notAfter ?? DateTimeOffset.UtcNow.AddYears(1);

            X509Certificate2 cert = certReq.CreateSelfSigned(nb, na);
            return ReimportWithExportablePrivateKey(cert);
        }

        /// <summary>
        /// Creates a self-signed RSA CA root certificate.
        /// </summary>
        public static X509Certificate2 CreateRootCa(
            string cn = "CN=TestRootCA, O=OPC Foundation",
            DateTimeOffset? notBefore = null,
            DateTimeOffset? notAfter = null,
            int keySize = 2048)
        {
            using var rsa = RSA.Create(keySize);
            var certReq = new CertificateRequest(
                cn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            certReq.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(true, true, 2, true));
            certReq.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                    true));
            certReq.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(certReq.PublicKey, false));

            DateTimeOffset nb = notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-5);
            DateTimeOffset na = notAfter ?? DateTimeOffset.UtcNow.AddYears(10);

            X509Certificate2 cert = certReq.CreateSelfSigned(nb, na);
            return ReimportWithExportablePrivateKey(cert);
        }

        /// <summary>
        /// Creates an intermediate CA certificate signed by <paramref name="rootCa"/>.
        /// </summary>
        public static X509Certificate2 CreateIntermediateCa(
            X509Certificate2 rootCa,
            string cn = "CN=TestIntermediateCA, O=OPC Foundation",
            DateTimeOffset? notBefore = null,
            DateTimeOffset? notAfter = null,
            int keySize = 2048)
        {
            using var rsa = RSA.Create(keySize);
            var certReq = new CertificateRequest(
                cn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            certReq.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(true, true, 1, true));
            certReq.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                    true));
            certReq.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(certReq.PublicKey, false));

            DateTimeOffset nb = notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-5);
            DateTimeOffset na = notAfter ?? DateTimeOffset.UtcNow.AddYears(5);

            byte[] serial = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(serial);
            }

            X509Certificate2 issued = certReq.Create(rootCa, nb, na, serial);
            X509Certificate2 withKey = issued.CopyWithPrivateKey(rsa);
            issued.Dispose();
            return ReimportWithExportablePrivateKey(withKey);
        }

        /// <summary>
        /// Creates a user certificate signed by the given <paramref name="issuerCa"/>.
        /// </summary>
        public static X509Certificate2 CreateUserCertSignedBy(
            X509Certificate2 issuerCa,
            string cn = "CN=TestUserChained, O=OPC Foundation",
            DateTimeOffset? notBefore = null,
            DateTimeOffset? notAfter = null,
            X509KeyUsageFlags keyUsage =
                X509KeyUsageFlags.DigitalSignature |
                X509KeyUsageFlags.NonRepudiation |
                X509KeyUsageFlags.DataEncipherment |
                X509KeyUsageFlags.KeyEncipherment,
            int keySize = 2048)
        {
            using var rsa = RSA.Create(keySize);
            var certReq = new CertificateRequest(
                cn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            certReq.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));
            certReq.CertificateExtensions.Add(
                new X509KeyUsageExtension(keyUsage, false));

            DateTimeOffset nb = notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-5);
            DateTimeOffset na = notAfter ?? DateTimeOffset.UtcNow.AddYears(1);

            byte[] serial = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(serial);
            }

            X509Certificate2 issued = certReq.Create(issuerCa, nb, na, serial);
            X509Certificate2 withKey = issued.CopyWithPrivateKey(rsa);
            issued.Dispose();
            return ReimportWithExportablePrivateKey(withKey);
        }

        private static X509Certificate2 ReimportWithExportablePrivateKey(X509Certificate2 cert)
        {
            // Reimporting via PKCS#12 ensures the private key is fully accessible
            // and detaches the cert from any per-thread CSP that might lock it.
            byte[] pfx = cert.Export(X509ContentType.Pfx, "test");
            cert.Dispose();
            return X509CertificateLoader.LoadPkcs12(pfx, "test",
                X509KeyStorageFlags.Exportable);
        }
    }
}
