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
        /// <summary>
        /// Gets the shared singleton instance of <see cref="DefaultCertificateFactory"/>.
        /// </summary>
        /// <remarks>
        /// Use this singleton when no dependency-injected <see cref="ICertificateFactory"/>
        /// is available. The default factory is stateless and safe to share
        /// across threads.
        /// </remarks>
        public static ICertificateFactory Instance { get; } = new DefaultCertificateFactory();

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
                var cert = Certificate.FromRawData(certBlob.ToArray());
                try
                {
                    collection.Add(cert);
                    offset += certBlob.Length;
                }
                finally
                {
                    cert.Dispose();
                }
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
                string[] applicationUris = applicationUri != null
                    ? [applicationUri]
                    : [];
                builder.AddExtension(
                    new X509SubjectAltNameExtension(
                        applicationUris,
                        domainNames ?? []));
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
                    Oids.GetHashAlgorithmName(certificate.SignatureAlgorithm.Value ??
                        throw new CryptographicException("Signature algorithm OID value is null.")),
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
                    Oids.GetHashAlgorithmName(certificate.SignatureAlgorithm.Value ??
                        throw new CryptographicException("Signature algorithm OID value is null.")));
            }

            // Collect domain names from the existing certificate.
            List<string> domainNameList = domainNames != null
                ? [.. domainNames]
                : [];

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
                ?? [];

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
                var generator =
                    X509SignatureGenerator.CreateForRSA(
                        rsa, RSASignaturePadding.Pkcs1);
                return request.CreateSigningRequest(generator);
            }
            else
            {
                using ECDsa key = certificate.GetECDsaPrivateKey()
                    ?? throw new NotSupportedException(
                        "The certificate does not contain an ECDsa private key.");
                var generator =
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
            // CA2000: ownership of the CopyWithPrivateKey result transfers
            // directly to DetachFromSourceKey, which disposes it (via its
            // own `using`) once the independent PFX round-trip copy has
            // been produced; the analyzer cannot see across that boundary.
#pragma warning disable CA2000
            if (X509PfxUtils.IsECDsaSignature(certificate))
            {
                using ECDsa ecdsaPrivateKey =
                    PEMReader.ImportECDsaPrivateKeyFromPEM(
                        pemDataBlob, password);
                using var cert = Certificate.FromRawData(certificate.RawData);
                return DetachFromSourceKey(cert.CopyWithPrivateKey(ecdsaPrivateKey));
            }

            using RSA rsaPrivateKey =
                PEMReader.ImportRsaPrivateKeyFromPEM(pemDataBlob, password);
            using var rsaCert = Certificate.FromRawData(certificate.RawData);
            return DetachFromSourceKey(rsaCert.CopyWithPrivateKey(rsaPrivateKey));
#pragma warning restore CA2000
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

            // CA2000: ownership of the CopyWithPrivateKey result transfers
            // directly to DetachFromSourceKey, which disposes it (via its
            // own `using`) once the independent PFX round-trip copy has
            // been produced; the analyzer cannot see across that boundary.
#pragma warning disable CA2000
            // Some platforms (observed on .NET Framework) restrict raw
            // private-key parameter export on ephemeral CNG keys, which
            // CopyWithPrivateKey needs internally. Re-importing the
            // caller-supplied source certificate through a PFX round trip
            // first — via AddRef so the caller's own reference is
            // untouched — guarantees the extracted key supports export
            // regardless of how certificateWithPrivateKey was originally
            // loaded.
            using Certificate normalizedSource = DetachFromSourceKey(certificateWithPrivateKey.AddRef());

            if (X509PfxUtils.IsECDsaSignature(certificate))
            {
                if (!X509PfxUtils.VerifyECDsaKeyPair(
                    certificate, normalizedSource))
                {
                    throw new NotSupportedException(
                        "The public and the private key pair doesn't match.");
                }

                using ECDsa privateKey =
                    normalizedSource.GetECDsaPrivateKey()
                    ?? throw new NotSupportedException(
                        "The certificate does not contain an ECDsa private key.");
                return DetachFromSourceKey(certificate.CopyWithPrivateKey(privateKey));
            }
            else
            {
                if (!X509PfxUtils.VerifyRSAKeyPair(
                    certificate, normalizedSource))
                {
                    throw new NotSupportedException(
                        "The public and the private key pair doesn't match.");
                }

                using RSA privateKey =
                    normalizedSource.GetRSAPrivateKey()
                    ?? throw new NotSupportedException(
                        "The certificate does not contain an RSA private key.");
                return DetachFromSourceKey(certificate.CopyWithPrivateKey(privateKey));
            }
#pragma warning restore CA2000
        }

        /// <summary>
        /// Detaches a certificate's private key from whatever RSA/ECDsa key
        /// object was combined into it via <c>X509Certificate2.CopyWithPrivateKey</c>
        /// (or the ECDsa overload).
        /// </summary>
        /// <remarks>
        /// <c>CopyWithPrivateKey</c> does not deep-copy the supplied key:
        /// the returned certificate can share the underlying (possibly
        /// ephemeral) native key handle with the caller-supplied key
        /// object. Since every caller of <see cref="CreateWithPrivateKey"/>
        /// and <see cref="CreateWithPEMPrivateKey"/> disposes that source
        /// key immediately (typically via a <c>using</c> statement around
        /// the call), the combined certificate's private key can otherwise
        /// fail with a "Keyset does not exist" <see cref="CryptographicException"/>
        /// the moment it is used — most commonly observed on Windows with
        /// ephemeral CNG keys. Round-tripping through an in-memory PFX
        /// export/import while the source key is still alive produces a
        /// certificate whose private key is fully independent, mirroring
        /// the technique <c>X509Utils.CreateCopyWithPrivateKey</c> already
        /// uses for the same reason.
        /// </remarks>
        private static Certificate DetachFromSourceKey(Certificate combined)
        {
            using (combined)
            {
                char[] passcode = CreateTransientPassword();
                try
                {
                    // CA2000: ownership of the loaded X509Certificate2 transfers
                    // to the Certificate wrapper returned here; it is disposed
                    // together with that wrapper by the caller.
#pragma warning disable CA2000
                    return Certificate.From(X509CertificateLoader.LoadPkcs12(
                        combined.Export(X509ContentType.Pfx, passcode),
                        passcode,
                        X509KeyStorageFlags.Exportable));
#pragma warning restore CA2000
                }
                finally
                {
                    Array.Clear(passcode, 0, passcode.Length);
                }
            }
        }

        /// <summary>
        /// Creates a short-lived random passphrase used only to shepherd a
        /// certificate through the in-memory PFX round trip performed by
        /// <see cref="DetachFromSourceKey"/>. The passphrase never leaves
        /// this process and is cleared immediately after use.
        /// </summary>
        private static char[] CreateTransientPassword()
        {
            const int length = 18;
            byte[] tokenBuffer = new byte[length];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(tokenBuffer);
            }

            char[] passcode = Convert.ToBase64String(tokenBuffer).ToCharArray();
            Array.Clear(tokenBuffer, 0, tokenBuffer.Length);
            return passcode;
        }
    }
}
