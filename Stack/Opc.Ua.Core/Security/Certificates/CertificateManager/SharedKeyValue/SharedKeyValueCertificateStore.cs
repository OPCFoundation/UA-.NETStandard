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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redundancy;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an <see cref="ICertificateStore"/> that persists trusted, issuer and
    /// rejected certificates and their CRLs in a shared <see cref="ISharedKeyValueStore"/>, so a
    /// <c>RedundantServerSet</c> shares one trust list, issuer list and rejected store across replicas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every record is written through an <see cref="IRecordProtector"/> for integrity: a forged or tampered
    /// record fails the authenticity check on read (fail-closed) and is skipped, so an attacker with write
    /// access to the shared store cannot inject a trusted certificate. An in-memory single-process store may use
    /// the no-op <see cref="NullRecordProtector"/>; an external, network-reachable store must use an
    /// authenticating protector.
    /// </para>
    /// <para>
    /// This store holds public certificates only: <see cref="NoPrivateKeys"/> is <c>true</c> and
    /// <see cref="SupportsLoadPrivateKey"/> is <c>false</c>. Distributing the application instance certificate
    /// (with its private key) is a separate, future capability.
    /// </para>
    /// </remarks>
    public sealed class SharedKeyValueCertificateStore : ICertificateStore
    {
        /// <summary>
        /// The store type name reported by <see cref="StoreType"/> and handled
        /// by the shared key/value store provider.
        /// </summary>
        public const string StoreTypeName = CertificateStoreType.SharedKeyValue;

        /// <summary>
        /// Creates a certificate store over a shared key/value backend.
        /// </summary>
        /// <param name="store">The shared key/value backend.</param>
        /// <param name="protector">
        /// The record protector applied to every stored record. Defaults to the
        /// no-op <see cref="NullRecordProtector"/> (safe only for an in-memory,
        /// single-process store).
        /// </param>
        /// <param name="telemetry">The telemetry context for logging.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="store"/> or <paramref name="telemetry"/> is <c>null</c>.
        /// </exception>
        public SharedKeyValueCertificateStore(
            ISharedKeyValueStore store,
            IRecordProtector? protector,
            ITelemetryContext telemetry)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_protector = protector ?? NullRecordProtector.Instance;
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_logger = telemetry.CreateLogger<SharedKeyValueCertificateStore>();
            m_storePath = string.Empty;
        }

        /// <inheritdoc/>
        public string StoreType => StoreTypeName;

        /// <inheritdoc/>
        public string StorePath => m_storePath;

        /// <inheritdoc/>
        public bool NoPrivateKeys => true;

        /// <inheritdoc/>
        public bool SupportsLoadPrivateKey => false;

        /// <inheritdoc/>
        public bool SupportsCRLs => true;

        /// <inheritdoc/>
        /// <remarks>
        /// The location is the shared-store key namespace for this store (for
        /// example <c>kv:pki/trusted</c>); it is used as a key prefix.
        /// </remarks>
        public void Open(string location, bool noPrivateKeys = true)
        {
            _ = noPrivateKeys;
            m_storePath = location ?? string.Empty;
        }

        /// <inheritdoc/>
        public void Close()
        {
            // Nothing to release; the shared store outlives this handle.
        }

        /// <inheritdoc/>
        public async Task<CertificateCollection> EnumerateAsync(CancellationToken ct = default)
        {
            var certificates = new CertificateCollection();
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(CertPrefix, ct)
                .ConfigureAwait(false))
            {
                if (TryDecodeCertificate(entry.Value, out Certificate? certificate))
                {
                    // Add takes its own reference (AddRef); release the local one.
                    certificates.Add(certificate);
                    certificate.Dispose();
                }
            }
            return certificates;
        }

        /// <inheritdoc/>
        public async Task AddAsync(
            Certificate certificate,
            char[]? password = null,
            CancellationToken ct = default)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }
            _ = password;

            string key = CertKey(certificate.Thumbprint);
            (bool found, _) = await m_store.TryGetAsync(key, ct).ConfigureAwait(false);
            if (found)
            {
                throw new ArgumentException(
                    "A certificate with the same thumbprint is already in the store.");
            }

            await m_store
                .SetAsync(key, EncodeCertificate(certificate), ct)
                .ConfigureAwait(false);
            if (m_logger.IsEnabled(LogLevel.Debug))
            {
                m_logger.SharedKeyValueStoreLog0(certificate.Thumbprint, m_storePath);
            }
        }

        /// <inheritdoc/>
        public async Task AddRejectedAsync(
            CertificateCollection certificates,
            int maxCertificates,
            CancellationToken ct = default)
        {
            if (certificates == null)
            {
                throw new ArgumentNullException(nameof(certificates));
            }

            // A negative maximum keeps no rejected history.
            if (maxCertificates < 0)
            {
                return;
            }

            foreach (Certificate certificate in certificates)
            {
                await m_store
                    .SetAsync(CertKey(certificate.Thumbprint), EncodeCertificate(certificate), ct)
                    .ConfigureAwait(false);
            }

            // A zero maximum is unlimited; otherwise trim the oldest by the
            // stored insertion timestamp (best-effort; the rejected list is
            // advisory and not security-critical).
            if (maxCertificates > 0)
            {
                await TrimRejectedAsync(maxCertificates, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public Task<bool> DeleteAsync(string thumbprint, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(thumbprint))
            {
                return Task.FromResult(false);
            }
            return DeleteInternalAsync(thumbprint, ct);
        }

        // CA2000: the decoded certificate is handed to the returned CertificateCollection, whose caller owns
        // and disposes it; the local reference is released immediately after Add.
        /// <inheritdoc/>
        public async Task<CertificateCollection> FindByThumbprintAsync(
            string thumbprint,
            CancellationToken ct = default)
        {
            var certificates = new CertificateCollection();
            if (string.IsNullOrEmpty(thumbprint))
            {
                return certificates;
            }

            (bool found, ByteString value) = await m_store
                .TryGetAsync(CertKey(thumbprint), ct)
                .ConfigureAwait(false);
            if (found && TryDecodeCertificate(value, out Certificate? certificate))
            {
                // Add takes its own reference (AddRef); release the local one.
                certificates.Add(certificate);
                certificate.Dispose();
            }
            return certificates;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The shared key/value store holds public certificates only and never
        /// returns a private key.
        /// </remarks>
        public Task<Certificate?> LoadPrivateKeyAsync(
            string thumbprint,
            string? subjectName,
            string? applicationUri,
            NodeId certificateType,
            char[]? password,
            CancellationToken ct = default)
        {
            return Task.FromResult<Certificate?>(null);
        }

        /// <inheritdoc/>
        public async Task<StatusCode> IsRevokedAsync(
            Certificate issuer,
            Certificate certificate,
            CancellationToken ct = default)
        {
            if (issuer == null)
            {
                throw new ArgumentNullException(nameof(issuer));
            }
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            bool crlExpired = true;
            foreach (X509CRL crl in await EnumerateCRLsAsync(ct).ConfigureAwait(false))
            {
                if (!X509Utils.CompareDistinguishedName(crl.IssuerName, issuer.SubjectName))
                {
                    continue;
                }
                if (!crl.VerifySignature(issuer, false))
                {
                    continue;
                }
                if (crl.IsRevoked(certificate))
                {
                    return StatusCodes.BadCertificateRevoked;
                }
                if (crl.ThisUpdate <= DateTime.UtcNow &&
                    (crl.NextUpdate == DateTime.MinValue || crl.NextUpdate >= DateTime.UtcNow))
                {
                    crlExpired = false;
                }
            }

            if (!crlExpired)
            {
                return StatusCodes.Good;
            }
            return StatusCodes.BadCertificateRevocationUnknown;
        }

        /// <inheritdoc/>
        public async Task<X509CRLCollection> EnumerateCRLsAsync(CancellationToken ct = default)
        {
            var crls = new X509CRLCollection();
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(CrlPrefix, ct)
                .ConfigureAwait(false))
            {
                if (TryDecodeCrl(entry.Value, out X509CRL? crl))
                {
                    crls.Add(crl);
                }
            }
            return crls;
        }

        /// <inheritdoc/>
        public async Task<X509CRLCollection> EnumerateCRLsAsync(
            Certificate issuer,
            bool validateUpdateTime = true,
            CancellationToken ct = default)
        {
            if (issuer == null)
            {
                throw new ArgumentNullException(nameof(issuer));
            }

            var crls = new X509CRLCollection();
            foreach (X509CRL crl in await EnumerateCRLsAsync(ct).ConfigureAwait(false))
            {
                if (!X509Utils.CompareDistinguishedName(crl.IssuerName, issuer.SubjectName))
                {
                    continue;
                }
                if (!crl.VerifySignature(issuer, false))
                {
                    continue;
                }
                if (!validateUpdateTime ||
                    (crl.ThisUpdate <= DateTime.UtcNow &&
                        (crl.NextUpdate == DateTime.MinValue || crl.NextUpdate >= DateTime.UtcNow)))
                {
                    crls.Add(crl);
                }
            }
            return crls;
        }

        /// <inheritdoc/>
        public async Task AddCRLAsync(X509CRL crl, CancellationToken ct = default)
        {
            if (crl == null)
            {
                throw new ArgumentNullException(nameof(crl));
            }

            bool issuerFound = false;
            using (CertificateCollection certificates = await EnumerateAsync(ct).ConfigureAwait(false))
            {
                foreach (Certificate certificate in certificates)
                {
                    if (X509Utils.CompareDistinguishedName(certificate.SubjectName, crl.IssuerName) &&
                        crl.VerifySignature(certificate, false))
                    {
                        issuerFound = true;
                        break;
                    }
                }
            }

            if (!issuerFound)
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid,
                    "Could not find issuer of the CRL.");
            }

            await m_store
                .SetAsync(CrlKey(crl.RawData), m_protector.Protect(new ByteString(crl.RawData)), ct)
                .ConfigureAwait(false);
            if (m_logger.IsEnabled(LogLevel.Debug))
            {
                m_logger.SharedKeyValueStoreLog1(
                    crl.IssuerName.ToString(),
                    m_storePath);
            }
        }

        /// <inheritdoc/>
        public Task<bool> DeleteCRLAsync(X509CRL crl, CancellationToken ct = default)
        {
            if (crl == null)
            {
                throw new ArgumentNullException(nameof(crl));
            }
            return DeleteCrlInternalAsync(crl, ct);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Nothing to release; the shared store outlives this handle.
        }

        private async Task<bool> DeleteInternalAsync(string thumbprint, CancellationToken ct)
        {
            return await m_store.DeleteAsync(CertKey(thumbprint), ct).ConfigureAwait(false);
        }

        private async Task<bool> DeleteCrlInternalAsync(X509CRL crl, CancellationToken ct)
        {
            return await m_store.DeleteAsync(CrlKey(crl.RawData), ct).ConfigureAwait(false);
        }

        private async Task TrimRejectedAsync(int maxCertificates, CancellationToken ct)
        {
            var entries = new List<(string Key, long Ticks)>();
            var invalidKeys = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in m_store
                .ScanAsync(CertPrefix, ct)
                .ConfigureAwait(false))
            {
                if (TryUnprotect(entry.Value, out ByteString plaintext) &&
                    plaintext.Span.Length >= TimestampLength)
                {
                    long ticks = BinaryPrimitives.ReadInt64LittleEndian(plaintext.Span);
                    entries.Add((entry.Key, ticks));
                    continue;
                }

                invalidKeys.Add(entry.Key);
            }

            foreach (string invalidKey in invalidKeys)
            {
                await m_store.DeleteAsync(invalidKey, ct).ConfigureAwait(false);
            }

            if (entries.Count <= maxCertificates)
            {
                return;
            }

            // Keep the newest maxCertificates; delete the oldest remainder.
            entries.Sort(static (left, right) => right.Ticks.CompareTo(left.Ticks));
            for (int index = maxCertificates; index < entries.Count; index++)
            {
                await m_store.DeleteAsync(entries[index].Key, ct).ConfigureAwait(false);
            }
        }

        private ByteString EncodeCertificate(Certificate certificate)
        {
            byte[] der = certificate.RawData;
            byte[] plaintext = new byte[TimestampLength + der.Length];
            BinaryPrimitives.WriteInt64LittleEndian(plaintext, DateTime.UtcNow.Ticks);
            der.CopyTo(plaintext.AsSpan(TimestampLength));
            return m_protector.Protect(new ByteString(plaintext));
        }

        private bool TryDecodeCertificate(ByteString value, [NotNullWhen(true)] out Certificate? certificate)
        {
            certificate = null;
            if (!TryUnprotect(value, out ByteString plaintext) ||
                plaintext.Span.Length <= TimestampLength)
            {
                return false;
            }

            try
            {
                certificate = Certificate.FromRawData(
                    plaintext.Span[TimestampLength..].ToArray());
                return true;
            }
            catch (Exception ex)
            {
                if (m_logger.IsEnabled(LogLevel.Warning))
                {
                    m_logger.SharedKeyValueStoreLog2(ex, m_storePath);
                }
                return false;
            }
        }

        private bool TryDecodeCrl(ByteString value, [NotNullWhen(true)] out X509CRL? crl)
        {
            crl = null;
            if (!TryUnprotect(value, out ByteString plaintext) || plaintext.Span.Length == 0)
            {
                return false;
            }

            try
            {
                crl = new X509CRL(plaintext.Span.ToArray());
                return true;
            }
            catch (Exception ex)
            {
                if (m_logger.IsEnabled(LogLevel.Warning))
                {
                    m_logger.SharedKeyValueStoreLog3(ex, m_storePath);
                }
                return false;
            }
        }

        private bool TryUnprotect(ByteString value, out ByteString plaintext)
        {
            if (m_protector.TryUnprotect(value, out plaintext))
            {
                return true;
            }

            // Fail-closed: a record that fails the integrity check is never
            // trusted. This defends the shared trust list against forgery.
            if (m_logger.IsEnabled(LogLevel.Warning))
            {
                m_logger.SharedKeyValueStoreLog4(m_storePath);
            }
            return false;
        }

        private string CertPrefix => m_storePath + "/cert/";

        private string CrlPrefix => m_storePath + "/crl/";

        private string CertKey(string thumbprint)
        {
            return CertPrefix + thumbprint;
        }

        private string CrlKey(byte[] rawData)
        {
#if NET5_0_OR_GREATER
            byte[] hash = SHA256.HashData(rawData);
#else
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(rawData);
#endif
            return CrlPrefix + ToHex(hash);
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new System.Text.StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private const int TimestampLength = sizeof(long);

        private readonly ISharedKeyValueStore m_store;
        private readonly IRecordProtector m_protector;
        private readonly ILogger m_logger;
        private string m_storePath;
    }

    /// <summary>
    /// Source-generated log messages for SharedKeyValueCertificateStore.
    /// </summary>
    internal static partial class SharedKeyValueCertificateStoreLog
    {
        [LoggerMessage(EventId = CoreEventIds.SharedKeyValueCertificateStore + 0, Level = LogLevel.Debug,
            Message = "Certificate {Thumbprint} added to shared store {StorePath}.")]
        public static partial void SharedKeyValueStoreLog0(
            this ILogger logger,
            string? thumbprint,
            string? storePath);

        [LoggerMessage(EventId = CoreEventIds.SharedKeyValueCertificateStore + 1, Level = LogLevel.Debug,
            Message = "CRL for issuer {Issuer} added to shared store {StorePath}.")]
        public static partial void SharedKeyValueStoreLog1(
            this ILogger logger,
            string? issuer,
            string? storePath);

        [LoggerMessage(EventId = CoreEventIds.SharedKeyValueCertificateStore + 2, Level = LogLevel.Warning,
            Message = "Skipping an undecodable certificate record in shared store {StorePath}.")]
        public static partial void SharedKeyValueStoreLog2(
            this ILogger logger,
            global::System.Exception? exception,
            string? storePath);

        [LoggerMessage(EventId = CoreEventIds.SharedKeyValueCertificateStore + 3, Level = LogLevel.Warning,
            Message = "Skipping an undecodable CRL record in shared store {StorePath}.")]
        public static partial void SharedKeyValueStoreLog3(
            this ILogger logger,
            global::System.Exception? exception,
            string? storePath);

        [LoggerMessage(EventId = CoreEventIds.SharedKeyValueCertificateStore + 4, Level = LogLevel.Warning,
            Message = "Rejected a certificate record that failed integrity verification in shared store {StorePath}.")]
        public static partial void SharedKeyValueStoreLog4(this ILogger logger, string? storePath);
    }

}
