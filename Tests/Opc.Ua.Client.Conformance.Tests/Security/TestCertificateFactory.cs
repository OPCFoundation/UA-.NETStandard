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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.Conformance.Tests.Security
{
    /// <summary>
    /// Programmatically generates X.509 application instance
    /// certificates with controlled flaws (expired, not-yet-valid,
    /// wrong-CN, weak-key, SHA1, corrupted, CA-without-app-instance,
    /// CA-issued, etc.) for use by the
    /// <see cref="SecurityCertValidationTests"/> and
    /// <see cref="SecurityNoneSession10Tests"/> conformance fixtures.
    /// Backed by the existing <see cref="CertificateBuilder"/>
    /// infrastructure — no third-party dependencies.
    /// </summary>
    internal static class TestCertificateFactory
    {
        /// <summary>
        /// Default RSA key size for valid (non-flawed) certificates.
        /// </summary>
        public const ushort DefaultRsaKeySize = 2048;

        /// <summary>
        /// Default lifetime for a freshly issued certificate.
        /// </summary>
        public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(365);

        /// <summary>
        /// Generates a fully valid RSA application instance certificate
        /// containing the supplied application URI as a UniformResourceIdentifier
        /// SAN entry (and the host name so it passes domain-name checks).
        /// </summary>
        public static X509Certificate2 CreateValidAppInstanceCert(
            string subjectName,
            string applicationUri,
            ushort keySize = DefaultRsaKeySize,
            HashAlgorithmName? hashAlgorithm = null,
            string hostName = "localhost")
        {
            ICertificateBuilder builder = CertificateBuilder.Create(subjectName)
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(DefaultLifetime);
            if (hashAlgorithm.HasValue)
            {
                builder = builder.SetHashAlgorithm(hashAlgorithm.Value);
            }
            return builder
                .AddExtension(BuildSubjectAlternativeName(applicationUri, hostName))
                .SetRSAKeySize(keySize)
                .CreateForRSA();
        }

        /// <summary>
        /// Returns an application instance certificate whose NotAfter
        /// is in the past.
        /// </summary>
        public static X509Certificate2 CreateExpiredAppInstanceCert(
            string subjectName,
            string applicationUri,
            string hostName = "localhost")
        {
            return CertificateBuilder.Create(subjectName)
                .SetNotBefore(DateTime.UtcNow.AddDays(-365))
                .SetNotAfter(DateTime.UtcNow.AddMinutes(-5))
                .AddExtension(BuildSubjectAlternativeName(applicationUri, hostName))
                .SetRSAKeySize(DefaultRsaKeySize)
                .CreateForRSA();
        }

        /// <summary>
        /// Returns an application instance certificate whose NotBefore
        /// is in the future.
        /// </summary>
        public static X509Certificate2 CreateNotYetValidAppInstanceCert(
            string subjectName,
            string applicationUri,
            string hostName = "localhost")
        {
            return CertificateBuilder.Create(subjectName)
                .SetNotBefore(DateTime.UtcNow.AddDays(7))
                .SetNotAfter(DateTime.UtcNow.AddDays(180))
                .AddExtension(BuildSubjectAlternativeName(applicationUri, hostName))
                .SetRSAKeySize(DefaultRsaKeySize)
                .CreateForRSA();
        }

        /// <summary>
        /// Returns an application instance certificate that contains
        /// the BasicConstraints CA:TRUE flag — a misconfiguration the
        /// server should reject because the certificate isn't an
        /// application instance certificate.
        /// </summary>
        public static X509Certificate2 CreateCaCert(
            string subjectName,
            string applicationUri,
            string hostName = "localhost")
        {
            return CertificateBuilder.Create(subjectName)
                .SetCAConstraint(0)
                .SetLifeTime(DefaultLifetime)
                .AddExtension(BuildSubjectAlternativeName(applicationUri, hostName))
                .SetRSAKeySize(DefaultRsaKeySize)
                .CreateForRSA();
        }

        /// <summary>
        /// Returns a self-signed certificate signed with SHA1 and the
        /// supplied key size — used to verify the server rejects weak
        /// crypto.
        /// </summary>
        public static X509Certificate2 CreateSha1AppInstanceCert(
            string subjectName,
            string applicationUri,
            ushort keySize,
            string hostName = "localhost")
        {
            return CertificateBuilder.Create(subjectName)
                .SetLifeTime(DefaultLifetime)
                .SetHashAlgorithm(HashAlgorithmName.SHA1)
                .AddExtension(BuildSubjectAlternativeName(applicationUri, hostName))
                .SetRSAKeySize(keySize)
                .CreateForRSA();
        }

        /// <summary>
        /// Returns a self-signed certificate whose SAN entries point at
        /// a different host name from the one the test will connect
        /// to.
        /// </summary>
        public static X509Certificate2 CreateWrongHostnameAppInstanceCert(
            string subjectName,
            string applicationUri)
        {
            return CertificateBuilder.Create(subjectName)
                .SetLifeTime(DefaultLifetime)
                .AddExtension(BuildSubjectAlternativeName(applicationUri, "remote-host.invalid"))
                .SetRSAKeySize(DefaultRsaKeySize)
                .CreateForRSA();
        }

        /// <summary>
        /// Mutates the signature bytes of the provided DER-encoded
        /// certificate so signature verification fails. The
        /// modification is made on a clone — the input certificate is
        /// not altered.
        /// </summary>
        public static X509Certificate2 CorruptCertSignature(X509Certificate2 cert)
        {
            if (cert == null)
            {
                throw new ArgumentNullException(nameof(cert));
            }
            byte[] der = cert.Export(X509ContentType.Cert);
            // Mutate a byte near the end of the DER blob — the
            // signature value sits at the tail.
            der[^5] ^= 0xFF;
#pragma warning disable SYSLIB0057 // X509Certificate2(byte[]) is obsolete on net9
            return new X509Certificate2(der);
#pragma warning restore SYSLIB0057
        }

        /// <summary>
        /// Returns a CA certificate suitable for issuing application
        /// instance certificates. The CA itself is self-signed.
        /// </summary>
        public static X509Certificate2 CreateIssuingCa(string subjectName)
        {
            return CertificateBuilder.Create(subjectName)
                .SetCAConstraint(0)
                .SetLifeTime(TimeSpan.FromDays(2 * 365))
                .SetRSAKeySize(DefaultRsaKeySize)
                .CreateForRSA();
        }

        /// <summary>
        /// Returns an application instance certificate signed by the
        /// supplied CA. Used to produce both trusted-issued certs and
        /// untrusted-issued certs (the trust state is decided by what
        /// is added to the server's stores).
        /// </summary>
        public static X509Certificate2 CreateCaIssuedAppInstanceCert(
            string subjectName,
            string applicationUri,
            X509Certificate2 issuerCa,
            string hostName = "localhost")
        {
            if (issuerCa == null)
            {
                throw new ArgumentNullException(nameof(issuerCa));
            }
            return CertificateBuilder.Create(subjectName)
                .SetLifeTime(DefaultLifetime)
                .AddExtension(BuildSubjectAlternativeName(applicationUri, hostName))
                .SetIssuer(issuerCa)
                .SetRSAKeySize(DefaultRsaKeySize)
                .CreateForRSA();
        }

        private static X509Extension BuildSubjectAlternativeName(
            string applicationUri,
            string hostName)
        {
            return new X509SubjectAltNameExtension(applicationUri, new[] { hostName });
        }
    }
}
