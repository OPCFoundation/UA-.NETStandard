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
        /// Writing a certificate to a token is a provisioning operation, not
        /// something an OPC UA application does at run time, and a private key
        /// offered here can never be accepted. The attempt is recorded and the
        /// call succeeds so that a trust list update does not fail purely because
        /// the store is hardware backed.
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
        /// <exception cref="NotSupportedException">
        /// Always thrown. Objects are removed from a token with the vendor's
        /// provisioning tools.
        /// </exception>
        public Task<bool> DeleteAsync(string thumbprint, CancellationToken ct = default)
        {
            throw new NotSupportedException(
                "Objects cannot be deleted from a PKCS#11 token through this store. " +
                "Use the token vendor's provisioning tools.");
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

            foreach (KeyValuePair<byte[], byte[]> entry in token.FindCertificatesWithIds())
            {
                Certificate candidate = Certificate.FromRawData(entry.Key);

                try
                {
                    if (!Matches(candidate, thumbprint, subjectName, applicationUri, certificateType))
                    {
                        continue;
                    }

                    Certificate? withKey = AttachKey(token, candidate, entry.Value);

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

        private static Certificate? AttachKey(
            Pkcs11Token token,
            Certificate certificate,
            byte[] ckaId)
        {
            using X509Certificate2 x509 = certificate.AsX509Certificate2();

            // CA2000: ownership of the key passes to the returned Certificate,
            // which disposes it. Disposing it here would break the caller.
#pragma warning disable CA2000
            using (RSA? publicRsa = x509.GetRSAPublicKey())
            {
                if (publicRsa != null)
                {
                    IObjectHandle? handle = token.FindPrivateKey(CKK.CKK_RSA, ckaId);

                    return handle == null
                        ? null
                        : certificate.CopyWithDetachedPrivateKey(
                            new Pkcs11Rsa(token, handle, publicRsa.ExportParameters(false)));
                }
            }

            using ECDsa? publicEcdsa = x509.GetECDsaPublicKey();

            if (publicEcdsa != null)
            {
                IObjectHandle? handle = token.FindPrivateKey(CKK.CKK_EC, ckaId);

                return handle == null
                    ? null
                    : certificate.CopyWithDetachedPrivateKey(
                        new Pkcs11ECDsa(token, handle, publicEcdsa.ExportParameters(false)));
            }
#pragma warning restore CA2000

            return null;
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
    }
}
