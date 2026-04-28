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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Default implementation of <see cref="ICertificateFactory"/> that
    /// delegates to the types available in the Security.Certificates library.
    /// </summary>
    public sealed class DefaultCertificateFactory : ICertificateFactory
    {
        /// <inheritdoc/>
        public Certificate CreateFromRawData(ReadOnlyMemory<byte> encodedData)
        {
            return Certificate.FromRawData(encodedData.ToArray());
        }

        /// <inheritdoc/>
        public CertificateCollection ParseChainBlob(ReadOnlyMemory<byte> chainBlob)
        {
            var collection = new CertificateCollection();
            int offset = 0;

            while (offset < chainBlob.Length)
            {
                ReadOnlyMemory<byte> remaining = chainBlob[offset..];
                ReadOnlyMemory<byte> certBlob = AsnUtils.ParseX509Blob(remaining);
                collection.Add(Certificate.FromRawData(certBlob.ToArray()));
                offset += certBlob.Length;
            }

            return collection;
        }

        /// <inheritdoc/>
        public ICertificateBuilder CreateCertificate(string subjectName)
        {
            return CertificateBuilder.Create(subjectName);
        }

        /// <inheritdoc/>
        public ICertificateBuilder CreateApplicationCertificate(
            string applicationUri,
            string applicationName,
            string subjectName,
            IReadOnlyList<string>? domainNames = null)
        {
            ICertificateBuilder builder = CertificateBuilder.Create(subjectName);

            if (applicationUri != null || (domainNames != null && domainNames.Count > 0))
            {
                var applicationUris = applicationUri != null
                    ? [applicationUri]
                    : Array.Empty<string>();
                builder.AddExtension(
                    new X509SubjectAltNameExtension(
                        applicationUris,
                        domainNames ?? Array.Empty<string>()));
            }

            return builder;
        }

        /// <inheritdoc/>
        public byte[] CreateSigningRequest(
            Certificate certificate,
            IReadOnlyList<string>? domainNames = null)
        {
            if (!certificate.HasPrivateKey)
            {
                throw new NotSupportedException(
                    "Need a certificate with a private key.");
            }

            bool isECDsa = X509PfxUtils.IsECDsaSignature(certificate);
            CertificateRequest request;

            if (!isECDsa)
            {
                RSA rsaPublicKey = certificate.GetRSAPublicKey()
                    ?? throw new NotSupportedException(
                        "The certificate does not contain an RSA public key.");
                request = new CertificateRequest(
                    certificate.SubjectName,
                    rsaPublicKey,
                    Oids.GetHashAlgorithmName(
                        certificate.SignatureAlgorithm.Value
                            ?? throw new CryptographicException("Signature algorithm OID value is null.")),
                    RSASignaturePadding.Pkcs1);
            }
            else
            {
                ECDsa ecDsaPublicKey = certificate.GetECDsaPublicKey()
                    ?? throw new NotSupportedException(
                        "The certificate does not contain an ECDsa public key.");
                request = new CertificateRequest(
                    certificate.SubjectName,
                    ecDsaPublicKey,
                    Oids.GetHashAlgorithmName(
                        certificate.SignatureAlgorithm.Value
                            ?? throw new CryptographicException("Signature algorithm OID value is null.")));
            }

            // Collect domain names from the existing certificate.
            List<string> domainNameList = domainNames != null
                ? new List<string>(domainNames)
                : new List<string>();

            X509SubjectAltNameExtension? alternateName =
                certificate.FindExtension<X509SubjectAltNameExtension>();

            if (alternateName != null)
            {
                foreach (string name in alternateName.DomainNames)
                {
                    if (!domainNameList.Any(
                        s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        domainNameList.Add(name);
                    }
                }

                foreach (string ipAddress in alternateName.IPAddresses)
                {
                    if (!domainNameList.Any(
                        s => s.Equals(
                            ipAddress, StringComparison.OrdinalIgnoreCase)))
                    {
                        domainNameList.Add(ipAddress);
                    }
                }
            }

            // Collect application URIs from the existing certificate.
            IReadOnlyList<string> applicationUris = alternateName?.Uris
                ?? (IReadOnlyList<string>)Array.Empty<string>();

            // Subject Alternative Name
            var subjectAltName = new X509SubjectAltNameExtension(
                applicationUris, domainNameList);
            request.CertificateExtensions.Add(
                new X509Extension(subjectAltName, false));

            if (!isECDsa)
            {
                using RSA rsa = certificate.GetRSAPrivateKey()
                    ?? throw new NotSupportedException(
                        "The certificate does not contain an RSA private key.");
                X509SignatureGenerator generator =
                    X509SignatureGenerator.CreateForRSA(
                        rsa, RSASignaturePadding.Pkcs1);
                return request.CreateSigningRequest(generator);
            }
            else
            {
                using ECDsa key = certificate.GetECDsaPrivateKey()
                    ?? throw new NotSupportedException(
                        "The certificate does not contain an ECDsa private key.");
                X509SignatureGenerator generator =
                    X509SignatureGenerator.CreateForECDsa(key);
                return request.CreateSigningRequest(generator);
            }
        }

        /// <inheritdoc/>
        public Certificate CreateWithPEMPrivateKey(
            Certificate certificate,
            byte[] pemDataBlob,
            ReadOnlySpan<char> password = default)
        {
            if (X509PfxUtils.IsECDsaSignature(certificate))
            {
                using ECDsa ecdsaPrivateKey =
                    PEMReader.ImportECDsaPrivateKeyFromPEM(
                        pemDataBlob, password);
                using Certificate cert = Certificate.FromRawData(certificate.RawData);
                return cert.CopyWithPrivateKey(ecdsaPrivateKey);
            }

            using RSA rsaPrivateKey =
                PEMReader.ImportRsaPrivateKeyFromPEM(pemDataBlob, password);
            using Certificate rsaCert = Certificate.FromRawData(certificate.RawData);
            return rsaCert.CopyWithPrivateKey(rsaPrivateKey);
        }

        /// <inheritdoc/>
        public Certificate CreateWithPrivateKey(
            Certificate certificate,
            Certificate certificateWithPrivateKey)
        {
            if (!certificateWithPrivateKey.HasPrivateKey)
            {
                throw new NotSupportedException(
                    "Need a certificate with a private key.");
            }

            if (X509PfxUtils.IsECDsaSignature(certificate))
            {
                if (!X509PfxUtils.VerifyECDsaKeyPair(
                    certificate, certificateWithPrivateKey))
                {
                    throw new NotSupportedException(
                        "The public and the private key pair doesn't match.");
                }

                using ECDsa privateKey =
                    certificateWithPrivateKey.GetECDsaPrivateKey()
                    ?? throw new NotSupportedException(
                        "The certificate does not contain an ECDsa private key.");
                return certificate.CopyWithPrivateKey(privateKey);
            }
            else
            {
                if (!X509PfxUtils.VerifyRSAKeyPair(
                    certificate, certificateWithPrivateKey))
                {
                    throw new NotSupportedException(
                        "The public and the private key pair doesn't match.");
                }

                using RSA privateKey =
                    certificateWithPrivateKey.GetRSAPrivateKey()
                    ?? throw new NotSupportedException(
                        "The certificate does not contain an RSA private key.");
                return certificate.CopyWithPrivateKey(privateKey);
            }
        }
    }
}
