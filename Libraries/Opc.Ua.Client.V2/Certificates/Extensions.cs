// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Certificates
{
    using System;
    using System.Diagnostics;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// Crypto extensions
    /// </summary>
    internal static class Extensions
    {
        /// <summary>
        /// Create ECC certificate
        /// </summary>
        /// <param name="key"></param>
        /// <param name="subject"></param>
        /// <param name="validity"></param>
        /// <param name="hashAlgorithm"></param>
        /// <returns></returns>
        public static X509Certificate2 CreateCertificate(this ECDsa key,
            string subject, TimeSpan validity, HashAlgorithmName hashAlgorithm)
        {
            var request = new CertificateRequest(subject, key, hashAlgorithm);
            return request.CreateSelfSigned(DateTime.UtcNow - TimeSpan.FromHours(1),
                DateTime.UtcNow + validity);
        }

        /// <summary>
        /// Create RSA certificate
        /// </summary>
        /// <param name="key"></param>
        /// <param name="subject"></param>
        /// <param name="validity"></param>
        /// <param name="padding"></param>
        /// <param name="hashAlgorithm"></param>
        /// <returns></returns>
        public static X509Certificate2 CreateCertificate(this RSA key,
            string subject, TimeSpan validity, RSASignaturePadding padding,
            HashAlgorithmName hashAlgorithm)
        {
            var request = new CertificateRequest(subject, key, hashAlgorithm, padding);
            return request.CreateSelfSigned(DateTime.UtcNow - TimeSpan.FromHours(1),
                DateTime.UtcNow + validity);
        }

        /// <summary>
        /// Create self signed certificate
        /// </summary>
        /// <param name="subjectName"></param>
        /// <param name="curve"></param>
        /// <returns></returns>
        public static X509Certificate2 CreateSelfSignedCertificate(
            this X500DistinguishedName subjectName, ECCurve? curve = null)
        {
            return subjectName.CreateCertificate(2048, HashAlgorithmName.SHA256, curve);
        }

        /// <summary>
        /// Create OPC UA certificate for the specified subject. Can create CA or signed
        /// leaf certificates, or self-signed certificates. A public key can be provided
        /// to support renewal. We always create client/server certificates.
        /// </summary>
        /// <param name="subject"></param>
        /// <param name="keySize"></param>
        /// <param name="hashAlgorithmName"></param>
        /// <param name="curve"></param>
        /// <param name="serialNumber"></param>
        /// <param name="issuerCAKeyCert"></param>
        /// <param name="createCACertificate"></param>
        /// <param name="pathLengthConstraint"></param>
        /// <param name="publicKey"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static X509Certificate2 CreateCertificate(this X500DistinguishedName subject,
            int keySize, HashAlgorithmName hashAlgorithmName, ECCurve? curve = null,
            ReadOnlyMemory<byte> serialNumber = default,
            X509Certificate2? issuerCAKeyCert = null, bool createCACertificate = false,
            int pathLengthConstraint = 0, ReadOnlyMemory<byte> publicKey = default)
        {
            var notBefore = DateTime.UtcNow.AddDays(-1).Date;
            var notAfter = notBefore.AddMonths(24);

            if (issuerCAKeyCert != null)
            {
                if (!issuerCAKeyCert.HasPrivateKey)
                {
                    throw new ArgumentException(
                        "The provided issuer cert must have a private key");
                }

                // lifetime must be in range of issuer
                if (notAfter.ToUniversalTime() >
                    issuerCAKeyCert.NotAfter.ToUniversalTime())
                {
                    notAfter = issuerCAKeyCert.NotAfter.ToUniversalTime();
                }
                if (notBefore.ToUniversalTime() <
                    issuerCAKeyCert.NotBefore.ToUniversalTime())
                {
                    notBefore = issuerCAKeyCert.NotBefore.ToUniversalTime();
                }
            }

            var sn = serialNumber.Span;
            if (sn.Length == 0)
            {
                // new serial number
                var buffer = new byte[10];
                using (var rnd = RandomNumberGenerator.Create())
                {
                    rnd.GetNonZeroBytes(buffer);
                }
                // A compliant certificate uses a positive serial number.
                buffer[0] &= 0x7F;
                sn = buffer.AsSpan();
            }

            if (curve != null)
            {
                ECDsa? key = null;
                ECDsa? eccPublicKey = null;
                try
                {
                    if (publicKey.Length > 0)
                    {
                        eccPublicKey = ECDsa.Create();
                        eccPublicKey.ImportSubjectPublicKeyInfo(publicKey.Span, out var bytes);
                    }

                    if (eccPublicKey == null)
                    {
                        eccPublicKey = ECDsa.Create(curve.Value);
                        key = eccPublicKey;
                    }

                    var request = new CertificateRequest(subject, eccPublicKey, hashAlgorithmName);
                    AddOpcUaX509ExtensionsToRequest(request, sn, true);
                    if (key == null)
                    {
                        Debug.Assert(issuerCAKeyCert != null);
                        using var issuerKey = issuerCAKeyCert.GetECDsaPrivateKey();
                        Debug.Assert(issuerKey != null);
                        return request.Create(issuerCAKeyCert.SubjectName,
                            X509SignatureGenerator.CreateForECDsa(issuerKey),
                            notBefore, notAfter, sn);
                    }
                    return request.Create(subject, X509SignatureGenerator.CreateForECDsa(key),
                        notBefore, notAfter, sn).CopyWithPrivateKey(key);
                }
                finally
                {
                    eccPublicKey!.Dispose();
                }
            }
            else
            {
                RSA? rsaKeyPair = null;
                RSA? rsaPublicKey = null;
                try
                {
                    if (publicKey.Length != 0)
                    {
                        rsaPublicKey = RSA.Create();
                        rsaPublicKey.ImportSubjectPublicKeyInfo(publicKey.Span, out var bytes);
                    }
                    if (rsaPublicKey == null)
                    {
                        rsaPublicKey = RSA.Create(keySize == 0 ? 2048 : keySize);
                        rsaKeyPair = rsaPublicKey;
                    }

                    var request = new CertificateRequest(subject, rsaPublicKey, hashAlgorithmName,
                        RSASignaturePadding.Pkcs1);
                    AddOpcUaX509ExtensionsToRequest(request, sn, false);

                    if (rsaKeyPair == null)
                    {
                        Debug.Assert(issuerCAKeyCert != null);
                        using var rsaIssuerKey = issuerCAKeyCert.GetRSAPrivateKey();
                        Debug.Assert(rsaIssuerKey != null);
                        return request.Create(issuerCAKeyCert.SubjectName,
                            X509SignatureGenerator.CreateForRSA(rsaIssuerKey,
                                RSASignaturePadding.Pkcs1),
                            notBefore, notAfter, sn);
                    }

                    return request.Create(subject, X509SignatureGenerator.CreateForRSA(
                            rsaKeyPair, RSASignaturePadding.Pkcs1),
                        notBefore, notAfter, sn).CopyWithPrivateKey(rsaKeyPair);
                }
                finally
                {
                    rsaPublicKey!.Dispose();
                }
            }

            void AddOpcUaX509ExtensionsToRequest(CertificateRequest request,
                ReadOnlySpan<byte> serialNumber, bool isEccCertificate)
            {
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
                    createCACertificate, pathLengthConstraint >= 0, pathLengthConstraint, true));
                request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(
                    request.PublicKey, X509SubjectKeyIdentifierHashAlgorithm.Sha1, false));

                var authorityKeyIdentifier = issuerCAKeyCert != null
                    ? X509AuthorityKeyIdentifierExtension
                        .CreateFromCertificate(issuerCAKeyCert, true, true)
                    : X509AuthorityKeyIdentifierExtension
                        .CreateFromIssuerNameAndSerialNumber(subject, serialNumber);
                request.CertificateExtensions.Add(authorityKeyIdentifier);

                // Key usage extensions
                if (createCACertificate)
                {
                    request.CertificateExtensions.Add(new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature |
                        X509KeyUsageFlags.KeyCertSign |
                        X509KeyUsageFlags.CrlSign, true));
                    return;
                }

                var keyUsageFlags =
                    X509KeyUsageFlags.DigitalSignature |
                    X509KeyUsageFlags.NonRepudiation;
                if (isEccCertificate)
                {
                    keyUsageFlags |=
                        X509KeyUsageFlags.KeyAgreement;
                }
                else
                {
                    keyUsageFlags |=
                        X509KeyUsageFlags.DataEncipherment |
                        X509KeyUsageFlags.KeyEncipherment;
                }
                if (issuerCAKeyCert == null)
                {
                    keyUsageFlags |=
                        X509KeyUsageFlags.KeyCertSign; // self signed
                }
                request.CertificateExtensions.Add(new X509KeyUsageExtension(
                    keyUsageFlags, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    [
                        // The Oid string for TLS server authentication.
                        new Oid("1.3.6.1.5.5.7.3.1"),
                        // The Oid string for TLS client authentication.
                        new Oid("1.3.6.1.5.5.7.3.2")
                    ],
                    true));
            }
        }
    }
}
