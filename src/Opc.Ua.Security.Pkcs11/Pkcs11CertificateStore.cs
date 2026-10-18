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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Security.Pkcs11
{
    /// <summary>
    /// A certificate store backed by a PKCS#11 token, where private keys are
    /// used but never extracted.
    /// </summary>
    /// <remarks>
    /// The store is addressed by an RFC 7512 <c>pkcs11:</c> URI, so an existing
    /// OPC UA configuration file can point at a token by changing the store path
    /// and nothing else.
    /// <para>
    /// <see cref="LoadPrivateKeyAsync"/> binds the token key to the certificate
    /// with <c>Certificate.CopyWithDetachedPrivateKey</c> rather than
    /// <c>X509Certificate2.CopyWithPrivateKey</c>. That is not a preference. On
    /// Windows, <c>CopyWithPrivateKey</c> has fast paths only for
    /// <c>RSACng</c> and <c>RSACryptoServiceProvider</c> and otherwise falls back
    /// to exporting the private parameters, so it throws for any token backed
    /// key. The detached form works on every platform because it never asks the
    /// certificate to take ownership of the key.
    /// </para>
    /// </remarks>
    public sealed class Pkcs11CertificateStore : ICertificateStore
    {
        /// <summary>
        /// The store type name used to select this store.
        /// </summary>
        public const string StoreTypeName = "PKCS11";

        /// <summary>
        /// Initializes a new instance that opens the token lazily.
        /// </summary>
        /// <param name="telemetry">The telemetry context, used for logging.</param>
        /// <param name="options">
        /// The token to open, or <c>null</c> to take it from the store path.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="telemetry"/> is <c>null</c>.
        /// </exception>
        public Pkcs11CertificateStore(
            ITelemetryContext telemetry,
            Pkcs11TokenOptions? options = null)
            : this(telemetry, options, DefaultPkcs11LibraryLoader.Instance)
        {
        }

        /// <summary>
        /// Initializes a store that binds its module through a loader.
        /// </summary>
        /// <param name="telemetry">The telemetry context, used for logging.</param>
        /// <param name="options">
        /// The token to open, or <c>null</c> to take it from the store path.
        /// </param>
        /// <param name="loader">Binds the PKCS#11 module.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="telemetry"/> or <paramref name="loader"/> is <c>null</c>.
        /// </exception>
        internal Pkcs11CertificateStore(
            ITelemetryContext telemetry,
            Pkcs11TokenOptions? options,
            IPkcs11LibraryLoader loader)
        {
            m_logger = (telemetry ?? throw new ArgumentNullException(nameof(telemetry)))
                .CreateLogger<Pkcs11CertificateStore>();
            m_options = options;
            m_loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        /// <inheritdoc/>
        public string StoreType => StoreTypeName;

        /// <inheritdoc/>
        public string StorePath => m_storePath;

        /// <inheritdoc/>
        public bool NoPrivateKeys => false;

        /// <inheritdoc/>
        public bool SupportsLoadPrivateKey => true;

        /// <inheritdoc/>
        /// <remarks>
        /// Revocation lists are not held on a token. Point the trusted issuer
        /// store at a directory store for those.
        /// </remarks>
        public bool SupportsCRLs => false;

        /// <summary>
        /// Gets the number of times a caller attempted to persist a private key.
        /// </summary>
        /// <remarks>
        /// A token will not import key material that it did not generate, so
        /// these attempts are recorded rather than performed.
        /// </remarks>
        public int RejectedPrivateKeyWrites => m_rejectedPrivateKeyWrites;

        /// <inheritdoc/>
        /// <exception cref="ArgumentException">
        /// The location is not a usable PKCS#11 URI and no options were supplied.
        /// </exception>
        public void Open(string location, bool noPrivateKeys = true)
        {
            if (m_options == null && Pkcs11TokenOptions.IsPkcs11Uri(location))
            {
                m_options = Pkcs11TokenOptions.Parse(location);
            }

            // The PIN may arrive as a pin-value attribute. It is kept in the
            // options and stripped from the path this store then reports, so it
            // cannot travel into configuration, diagnostics or the address space.
            m_storePath = location == null
                ? string.Empty
                : Pkcs11TokenOptions.RedactPin(location);

            if (m_options == null)
            {
                throw new ArgumentException(
                    $"'{m_storePath}' is not a PKCS#11 URI and no token options were supplied.",
                    nameof(location));
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
            lock (m_lock)
            {
                m_token?.Dispose();
                m_token = null;
            }
        }

        /// <inheritdoc/>
        public Task<CertificateCollection> EnumerateAsync(CancellationToken ct = default)
        {
            return Task.FromResult(Enumerate());
        }

        private CertificateCollection Enumerate()
        {
            var results = new CertificateCollection();

            foreach (byte[] encoded in GetToken().FindCertificates())
            {
                // Add takes its own reference, so this handle has to be released.
                using Certificate candidate = Certificate.FromRawData(encoded);
                results.Add(candidate);
            }

            return results;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The certificate is written to the token as a certificate object. Its
        /// CKA_ID is the one of the key on the token that the certificate
        /// belongs to, found through a certificate on the token with the same
        /// public key or through the public value of the key, so that
        /// <see cref="LoadPrivateKeyAsync"/> pairs a renewed certificate with
        /// the key it was issued for. A certificate the token already holds is
        /// not written again.
        /// <para>
        /// A private key offered here is never written: a token does not accept
        /// key material it did not generate. Only the certificate is stored and
        /// the attempt is counted in <see cref="RejectedPrivateKeyWrites"/>.
        /// </para>
        /// </remarks>
        public Task AddAsync(
            Certificate certificate,
            char[]? password = null,
            CancellationToken ct = default)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            if (certificate.HasPrivateKey)
            {
                Interlocked.Increment(ref m_rejectedPrivateKeyWrites);

                m_logger.Pkcs11CertificateStoreLog1(
                    certificate.Thumbprint,
                    m_options?.TokenLabel ?? "<any>");
            }

            Add(certificate);

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task AddRejectedAsync(
            CertificateCollection certificates,
            int maxCertificates,
            CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Destroys the certificate objects with the thumbprint. The key a
        /// certificate belongs to stays on the token.
        /// </remarks>
        public Task<bool> DeleteAsync(string thumbprint, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(thumbprint))
            {
                return Task.FromResult(false);
            }

            int destroyed = GetToken().DestroyCertificates(encoded =>
            {
                using Certificate candidate = Certificate.FromRawData(encoded);
                return string.Equals(
                    candidate.Thumbprint,
                    thumbprint,
                    StringComparison.OrdinalIgnoreCase);
            });

            if (destroyed > 0)
            {
                m_logger.Pkcs11CertificateStoreLog3(
                    thumbprint,
                    m_options?.TokenLabel ?? "<any>");
            }

            return Task.FromResult(destroyed > 0);
        }

        /// <inheritdoc/>
        public Task<CertificateCollection> FindByThumbprintAsync(
            string thumbprint,
            CancellationToken ct = default)
        {
            return Task.FromResult(FindByThumbprint(thumbprint));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Returns the certificate with the token key attached in detached form.
        /// The key material never enters this process.
        /// </remarks>
        public Task<Certificate?> LoadPrivateKeyAsync(
            string thumbprint,
            string? subjectName,
            string? applicationUri,
            NodeId certificateType,
            char[]? password,
            CancellationToken ct = default)
        {
            return Task.FromResult(
                LoadPrivateKey(thumbprint, subjectName, applicationUri, certificateType));
        }

        private void Add(Certificate certificate)
        {
            Pkcs11Token token = GetToken();
            byte[] encoded = certificate.RawData;
            IReadOnlyList<KeyValuePair<byte[], byte[]?>> onToken = token.FindCertificatesWithIds();

            foreach (KeyValuePair<byte[], byte[]?> entry in onToken)
            {
                if (entry.Key.AsSpan().SequenceEqual(encoded))
                {
                    return;
                }
            }

            token.CreateCertificate(
                encoded,
                SelectObjectId(token, certificate, onToken),
                m_options?.ObjectLabel ?? certificate.Subject,
                certificate.SubjectName.RawData,
                certificate.IssuerName.RawData,
                EncodeSerialNumber(certificate.GetSerialNumber()));

            m_logger.Pkcs11CertificateStoreLog2(
                certificate.Thumbprint,
                m_options?.TokenLabel ?? "<any>");
        }

        /// <summary>
        /// Selects the CKA_ID for a certificate written to the token: the id
        /// the options select objects by, else the id of the key the
        /// certificate belongs to, else the SHA-1 hash of the public key, the
        /// usual CKA_ID of an object without a key on the token.
        /// </summary>
        private byte[] SelectObjectId(
            Pkcs11Token token,
            Certificate certificate,
            IReadOnlyList<KeyValuePair<byte[], byte[]?>> onToken)
        {
            if (m_options != null && !m_options.ObjectId.IsNull && m_options.ObjectId.Length > 0)
            {
                return m_options.ObjectId.ToArray();
            }

            byte[] publicKey = certificate.PublicKey.EncodedKeyValue.RawData;

            foreach (KeyValuePair<byte[], byte[]?> entry in onToken)
            {
                if (entry.Value is not { Length: > 0 } candidateId)
                {
                    continue;
                }

                using Certificate candidate = Certificate.FromRawData(entry.Key);

                if (HasSamePublicKey(candidate, certificate))
                {
                    return candidateId;
                }
            }

            byte[]? keyId = FindKeyId(token, certificate);

            if (keyId != null)
            {
                return keyId;
            }

#pragma warning disable CA5350 // SHA-1 is the conventional CKA_ID, not a security function here.
#if NET6_0_OR_GREATER
            return SHA1.HashData(publicKey);
#else
            using SHA1 sha1 = SHA1.Create();
            return sha1.ComputeHash(publicKey);
#endif
#pragma warning restore CA5350
        }

        /// <summary>
        /// Whether two certificates carry the same public key: the key
        /// algorithm, its encoded parameters (for an EC key the curve) and the
        /// encoded key value must all be equal.
        /// </summary>
        private static bool HasSamePublicKey(Certificate a, Certificate b)
        {
            if (!string.Equals(a.PublicKey.Oid.Value, b.PublicKey.Oid.Value, StringComparison.Ordinal))
            {
                return false;
            }

            // A key algorithm without parameters compares as empty parameters.
            byte[]? parametersA = a.PublicKey.EncodedParameters?.RawData;
            byte[]? parametersB = b.PublicKey.EncodedParameters?.RawData;
            return parametersA.AsSpan().SequenceEqual(parametersB.AsSpan()) &&
                a.PublicKey.EncodedKeyValue.RawData.AsSpan()
                    .SequenceEqual(b.PublicKey.EncodedKeyValue.RawData);
        }

        /// <summary>
        /// Finds the CKA_ID of the key on the token the certificate belongs
        /// to: an RSA key with the same modulus and public exponent, or an EC
        /// key with the same point on the same curve.
        /// </summary>
        private static byte[]? FindKeyId(Pkcs11Token token, Certificate certificate)
        {
            using (RSA? rsa = certificate.GetRSAPublicKey())
            {
                if (rsa != null)
                {
                    RSAParameters rsaParameters = rsa.ExportParameters(false);
                    (CKA, byte[])[] rsaMatch =
                    [
                        (CKA.CKA_MODULUS, rsaParameters.Modulus!),
                        (CKA.CKA_PUBLIC_EXPONENT, rsaParameters.Exponent!)
                    ];

                    return token.FindKeyId(CKO.CKO_PRIVATE_KEY, CKK.CKK_RSA, rsaMatch) ??
                        token.FindKeyId(CKO.CKO_PUBLIC_KEY, CKK.CKK_RSA, rsaMatch);
                }
            }

            using ECDsa? ecdsa = certificate.GetECDsaPublicKey();

            if (ecdsa == null)
            {
                return null;
            }

            ECParameters parameters = ecdsa.ExportParameters(false);
            byte[] point = new byte[1 + parameters.Q.X!.Length + parameters.Q.Y!.Length];
            point[0] = 0x04;
            parameters.Q.X.CopyTo(point, 1);
            parameters.Q.Y.CopyTo(point, 1 + parameters.Q.X.Length);

            // CKA_EC_PARAMS holds the DER encoded curve, the same encoding as
            // the parameters of the certificate's public key. Without them the
            // curve is unknown, so no key on the token can be matched.
            byte[]? curve = certificate.PublicKey.EncodedParameters?.RawData;
            if (curve == null || curve.Length == 0)
            {
                return null;
            }

            // CKA_EC_POINT holds the DER encoded OCTET STRING; some modules
            // store the bare point instead.
            return token.FindKeyId(
                    CKO.CKO_PUBLIC_KEY,
                    CKK.CKK_EC,
                    (CKA.CKA_EC_POINT, EncodeOctetString(point)),
                    (CKA.CKA_EC_PARAMS, curve)) ??
                token.FindKeyId(
                    CKO.CKO_PUBLIC_KEY,
                    CKK.CKK_EC,
                    (CKA.CKA_EC_POINT, point),
                    (CKA.CKA_EC_PARAMS, curve));
        }

        /// <summary>
        /// Encodes a serial number, as returned little endian by
        /// <see cref="Certificate.GetSerialNumber"/>, as the DER INTEGER that
        /// CKA_SERIAL_NUMBER holds.
        /// </summary>
        private static byte[] EncodeSerialNumber(byte[] littleEndian)
        {
            int length = littleEndian.Length;
            bool pad = length == 0 || (littleEndian[length - 1] & 0x80) != 0;
            byte[] content = new byte[length + (pad ? 1 : 0)];

            for (int ii = 0; ii < length; ii++)
            {
                content[content.Length - 1 - ii] = littleEndian[ii];
            }

            return EncodeTagged(0x02, content);
        }

        private static byte[] EncodeOctetString(byte[] value)
        {
            return EncodeTagged(0x04, value);
        }

        private static byte[] EncodeTagged(byte tag, byte[] content)
        {
            var encoded = new List<byte>(content.Length + 4) { tag };

            if (content.Length < 0x80)
            {
                encoded.Add((byte)content.Length);
            }
            else if (content.Length <= 0xFF)
            {
                encoded.Add(0x81);
                encoded.Add((byte)content.Length);
            }
            else
            {
                encoded.Add(0x82);
                encoded.Add((byte)(content.Length >> 8));
                encoded.Add((byte)content.Length);
            }

            encoded.AddRange(content);
            return [.. encoded];
        }

        private CertificateCollection FindByThumbprint(string thumbprint)
        {
            var results = new CertificateCollection();

            if (string.IsNullOrEmpty(thumbprint))
            {
                return results;
            }

            foreach (byte[] encoded in GetToken().FindCertificates())
            {
                Certificate candidate = Certificate.FromRawData(encoded);

                try
                {
                    if (string.Equals(
                            candidate.Thumbprint,
                            thumbprint,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        // Add takes its own reference; this handle is released below.
                        results.Add(candidate);
                    }
                }
                finally
                {
                    candidate.Dispose();
                }
            }

            return results;
        }

        private Certificate? LoadPrivateKey(
            string thumbprint,
            string? subjectName,
            string? applicationUri,
            NodeId certificateType)
        {
            // Refuse to guess. With nothing to match on, any certificate on the
            // token would satisfy the request, and a token that holds more than
            // one identity is the normal case for an HSM or a smart card. The
            // directory store makes the same refusal.
            if (string.IsNullOrEmpty(thumbprint) &&
                string.IsNullOrEmpty(subjectName) &&
                string.IsNullOrEmpty(applicationUri))
            {
                return null;
            }

            Pkcs11Token token = GetToken();

            foreach (KeyValuePair<byte[], byte[]?> entry in token.FindCertificatesWithIds())
            {
                Certificate candidate = Certificate.FromRawData(entry.Key);

                try
                {
                    // A certificate whose CKA_ID cannot be read cannot be linked
                    // to its key.
                    if (entry.Value == null ||
                        !Matches(candidate, thumbprint, subjectName, applicationUri, certificateType))
                    {
                        continue;
                    }

                    Certificate? withKey = entry.Value.Length > 0
                        ? AttachKey(token, candidate, entry.Value, provePossession: false)
                        : AttachKeyWithoutId(token, candidate);

                    if (withKey != null)
                    {
                        return withKey;
                    }
                }
                finally
                {
                    candidate.Dispose();
                }
            }

            return null;
        }

        /// <inheritdoc/>
        public Task<StatusCode> IsRevokedAsync(
            Certificate issuer,
            Certificate certificate,
            CancellationToken ct = default)
        {
            return Task.FromResult((StatusCode)StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLsAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new X509CRLCollection());
        }

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLsAsync(
            Certificate issuer,
            bool validateUpdateTime = true,
            CancellationToken ct = default)
        {
            return Task.FromResult(new X509CRLCollection());
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public Task AddCRLAsync(X509CRL crl, CancellationToken ct = default)
        {
            throw new NotSupportedException("A PKCS#11 token does not hold revocation lists.");
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public Task<bool> DeleteCRLAsync(X509CRL crl, CancellationToken ct = default)
        {
            throw new NotSupportedException("A PKCS#11 token does not hold revocation lists.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Close();
        }

        private static bool Matches(
            Certificate candidate,
            string? thumbprint,
            string? subjectName,
            string? applicationUri,
            NodeId certificateType)
        {
            if (!string.IsNullOrEmpty(thumbprint) &&
                !string.Equals(
                    candidate.Thumbprint,
                    thumbprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Distinguished name comparison only. A substring test would let a
            // request for CN=Server match CN=ServerBackup.
            if (!string.IsNullOrEmpty(subjectName) &&
                !X509Utils.CompareDistinguishedName(candidate.Subject, subjectName))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(applicationUri) &&
                !X509Utils.CompareApplicationUriWithCertificate(candidate, applicationUri!))
            {
                return false;
            }

            // Fully qualified: Opc.Ua.Security also declares a CertificateIdentifier,
            // which shadows the one carrying this check from inside this namespace.
            return Opc.Ua.CertificateIdentifier.ValidateCertificateType(candidate, certificateType);
        }

        /// <summary>
        /// Whether the options select the objects of the token by CKA_LABEL or CKA_ID.
        /// </summary>
        private static bool HasObjectFilter(Pkcs11TokenOptions options)
        {
            return !string.IsNullOrEmpty(options.ObjectLabel) ||
                (!options.ObjectId.IsNull && options.ObjectId.Length > 0);
        }

        /// <summary>
        /// Attaches a key to a copy of a certificate without a CKA_ID. Only a key
        /// that signs a challenge the certificate's public key verifies is
        /// attached: neither the CKA_ID of the key object that carries the
        /// certificate's public key nor the configured object label or id
        /// establishes the pairing alone, because several keys may share them.
        /// The candidates are the keys with that CKA_ID, then the keys the
        /// configured object filters select; without a filter, only the former.
        /// </summary>
        private static Certificate? AttachKeyWithoutId(Pkcs11Token token, Certificate certificate)
        {
            byte[]? keyId = FindKeyId(token, certificate);
            Certificate? withKey = keyId == null
                ? null
                : AttachKey(token, certificate, keyId, provePossession: true);

            if (withKey == null && HasObjectFilter(token.Options))
            {
                withKey = AttachKey(token, certificate, [], provePossession: true);
            }

            return withKey;
        }

        /// <summary>
        /// Attaches the private key with the CKA_ID to a copy of the certificate.
        /// </summary>
        /// <param name="token">The token holding the key.</param>
        /// <param name="certificate">The certificate.</param>
        /// <param name="ckaId">
        /// The CKA_ID of the key, or an empty value for the key the object filters select.
        /// </param>
        /// <param name="provePossession">
        /// Whether the key must sign a challenge that the public key of the
        /// certificate verifies, because nothing else links it to the certificate.
        /// Every key the CKA_ID or the filters select is tried; otherwise the
        /// first one is attached.
        /// </param>
        /// <returns>The certificate with its key, or <c>null</c> when there is none.</returns>
        private static Certificate? AttachKey(
            Pkcs11Token token,
            Certificate certificate,
            byte[] ckaId,
            bool provePossession)
        {
            using X509Certificate2 x509 = certificate.AsX509Certificate2();

            // CA2000: ownership of the key passes to the returned Certificate,
            // which disposes it. Disposing it here would break the caller.
#pragma warning disable CA2000
            using (RSA? publicRsa = x509.GetRSAPublicKey())
            {
                if (publicRsa != null)
                {
                    RSAParameters parameters = publicRsa.ExportParameters(false);

                    foreach (IObjectHandle handle in CandidateKeys(token, CKK.CKK_RSA, ckaId, provePossession))
                    {
                        var rsa = new Pkcs11Rsa(token, handle, parameters);

                        if (!provePossession || SignsFor(rsa))
                        {
                            return certificate.CopyWithDetachedPrivateKey(rsa);
                        }

                        rsa.Dispose();
                    }

                    return null;
                }
            }

            using ECDsa? publicEcdsa = x509.GetECDsaPublicKey();

            if (publicEcdsa != null)
            {
                ECParameters parameters = publicEcdsa.ExportParameters(false);

                foreach (IObjectHandle handle in CandidateKeys(token, CKK.CKK_EC, ckaId, provePossession))
                {
                    var ecdsa = new Pkcs11ECDsa(token, handle, parameters);

                    if (!provePossession || SignsFor(ecdsa))
                    {
                        return certificate.CopyWithDetachedPrivateKey(ecdsa);
                    }

                    ecdsa.Dispose();
                }
            }
#pragma warning restore CA2000

            return null;
        }

        /// <summary>
        /// The keys to try: every key the CKA_ID or the filters select when
        /// possession is proven, else the first one.
        /// </summary>
        private static List<IObjectHandle> CandidateKeys(
            Pkcs11Token token,
            CKK keyType,
            byte[] ckaId,
            bool provePossession)
        {
            if (provePossession)
            {
                return token.FindPrivateKeys(keyType, ckaId);
            }

            IObjectHandle? first = token.FindPrivateKey(keyType, ckaId);
            return first == null ? [] : [first];
        }

        /// <summary>
        /// Whether the RSA key on the token signs a random challenge that the
        /// certificate's public key verifies, with PKCS#1 v1.5 or, for a key the
        /// token permits only for PSS, with PSS. A key the token refuses to sign
        /// with does not.
        /// </summary>
        private static bool SignsFor(Pkcs11Rsa key)
        {
            byte[] challenge = CreateChallenge();

            foreach (RSASignaturePadding padding in s_challengePaddings)
            {
                try
                {
                    byte[] signature = key.SignHash(challenge, HashAlgorithmName.SHA256, padding);

                    // Pkcs11Rsa verifies with the public key it was created from,
                    // which is the certificate's.
                    return key.VerifyHash(challenge, signature, HashAlgorithmName.SHA256, padding);
                }
                catch (Pkcs11Exception)
                {
                    // The token refused the mechanism for this key; it may permit the next one.
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the EC key on the token signs a random challenge that the
        /// certificate's public key verifies. A key the token refuses to sign
        /// with does not.
        /// </summary>
        private static bool SignsFor(Pkcs11ECDsa key)
        {
            byte[] challenge = CreateChallenge();

            try
            {
                byte[] signature = key.SignHash(challenge);

                // Pkcs11ECDsa verifies with the public key it was created from,
                // which is the certificate's.
                return key.VerifyHash(challenge, signature);
            }
            catch (Pkcs11Exception)
            {
                return false;
            }
        }

        private static readonly RSASignaturePadding[] s_challengePaddings =
        [
            RSASignaturePadding.Pkcs1,
            RSASignaturePadding.Pss
        ];

        /// <summary>
        /// A random value with the length of a SHA-256 hash.
        /// </summary>
        private static byte[] CreateChallenge()
        {
            byte[] challenge = new byte[32];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(challenge);
            }
            return challenge;
        }

        private Pkcs11Token GetToken()
        {
            lock (m_lock)
            {
                if (m_token != null)
                {
                    return m_token;
                }

                if (m_options == null)
                {
                    throw new InvalidOperationException(
                        "The PKCS#11 store has not been opened. Call Open with a " +
                        "pkcs11: URI, or supply Pkcs11TokenOptions.");
                }

                m_token = new Pkcs11Token(m_options, m_loader);

                m_logger.Pkcs11CertificateStoreLog0(
                    m_options.ModulePath ?? string.Empty,
                    m_options.TokenLabel ?? "<any>");

                return m_token;
            }
        }

        private readonly System.Threading.Lock m_lock = new();
        private readonly ILogger m_logger;
        private readonly IPkcs11LibraryLoader m_loader;
        private Pkcs11TokenOptions? m_options;
        private Pkcs11Token? m_token;
        private string m_storePath = string.Empty;
        private int m_rejectedPrivateKeyWrites;
    }

    /// <summary>
    /// Log messages for the PKCS#11 certificate store.
    /// </summary>
    internal static partial class Pkcs11CertificateStoreLog
    {
        [LoggerMessage(
            EventId = SecurityPkcs11EventIds.Pkcs11CertificateStore + 0,
            Level = LogLevel.Information,
            Message = "Opened PKCS#11 token '{TokenLabel}' using module {ModulePath}.")]
        public static partial void Pkcs11CertificateStoreLog0(
            this ILogger logger,
            string modulePath,
            string tokenLabel);

        [LoggerMessage(
            EventId = SecurityPkcs11EventIds.Pkcs11CertificateStore + 1,
            Level = LogLevel.Warning,
            Message = "Refused to write the private key of certificate {Thumbprint} to the " +
                "PKCS#11 token '{TokenLabel}'. A token does not accept key material it did not " +
                "generate; the public certificate is unaffected.")]
        public static partial void Pkcs11CertificateStoreLog1(
            this ILogger logger,
            string thumbprint,
            string tokenLabel);

        [LoggerMessage(
            EventId = SecurityPkcs11EventIds.Pkcs11CertificateStore + 2,
            Level = LogLevel.Information,
            Message = "Wrote certificate {Thumbprint} to the PKCS#11 token '{TokenLabel}'.")]
        public static partial void Pkcs11CertificateStoreLog2(
            this ILogger logger,
            string thumbprint,
            string tokenLabel);

        [LoggerMessage(
            EventId = SecurityPkcs11EventIds.Pkcs11CertificateStore + 3,
            Level = LogLevel.Information,
            Message = "Deleted certificate {Thumbprint} from the PKCS#11 token '{TokenLabel}'.")]
        public static partial void Pkcs11CertificateStoreLog3(
            this ILogger logger,
            string thumbprint,
            string tokenLabel);
    }
}
