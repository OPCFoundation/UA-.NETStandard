// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;
using Opc.Ua.Redundancy;
using Opc.Ua.Security.Certificates;
using X509AuthorityKeyIdentifierExtension = Opc.Ua.Security.Certificates.X509AuthorityKeyIdentifierExtension;

namespace Opc.Ua
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an <see cref="ICertificateStore"/> that persists trusted, issuer and
    /// rejected certificates and their CRLs in a shared <see cref="ISharedKeyValueStore"/>, so a
    /// <c>RedundantServerSet</c> shares one trust list, issuer list and rejected store across replicas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distributed stores require an authenticating <see cref="IRecordProtector"/> and a
    /// <see cref="FreshnessProtectedKeyValueStore"/> with an explicitly enrolled authority epoch. A missing
    /// or unverifiable committed record is a security failure, not an empty result. Records in a legacy raw
    /// backend are not read automatically. Only the process-local <see cref="InMemorySharedKeyValueStore"/>
    /// can be used without the freshness view and with the no-op <see cref="NullRecordProtector"/>.
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
        /// <exception cref="ArgumentException">
        /// A backend other than <see cref="InMemorySharedKeyValueStore"/> lacks freshness protection
        /// or an authenticating record protector.
        /// </exception>
        public SharedKeyValueCertificateStore(
            ISharedKeyValueStore store,
            IRecordProtector? protector,
            ITelemetryContext telemetry)
        {
            m_protector = RequireProtection(store, protector);
            m_store = store;
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
        /// <exception cref="ServiceResultException">Committed records could not be verified.</exception>
        public async Task<CertificateCollection> EnumerateAsync(CancellationToken ct = default)
        {
            var certificates = new CertificateCollection();
            try
            {
                await foreach (KeyValuePair<string, ByteString> entry in m_store
                    .ScanAsync(CertPrefix, ct)
                    .ConfigureAwait(false))
                {
                    using Certificate certificate = DecodeCertificate(entry.Key, entry.Value);
                    certificates.Add(certificate);
                }
                return certificates;
            }
            catch (Exception ex)
            {
                certificates.Dispose();
                if (ShouldWrapReadFailure(ex, ct))
                {
                    throw UnavailableRecords(ex);
                }
                throw;
            }
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
            if (!await m_store.CompareAndSwapAsync(key, default, EncodeCertificate(key, certificate), ct)
                .ConfigureAwait(false))
            {
                throw new ArgumentException(
                    "A certificate with the same thumbprint is already in the store.");
            }
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

            // A negative maximum disables new rejected-certificate storage.
            if (maxCertificates < 0)
            {
                return;
            }

            foreach (Certificate certificate in certificates)
            {
                await m_store
                    .SetAsync(
                        CertKey(certificate.Thumbprint),
                        EncodeCertificate(CertKey(certificate.Thumbprint), certificate),
                        ct)
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

        /// <inheritdoc/>
        /// <exception cref="ServiceResultException">The committed record could not be verified.</exception>
        public async Task<CertificateCollection> FindByThumbprintAsync(
            string thumbprint,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(thumbprint))
            {
                return new CertificateCollection();
            }

            try
            {
                string key = CertKey(thumbprint);
                (bool found, ByteString value) = await m_store.TryGetAsync(key, ct).ConfigureAwait(false);
                if (!found)
                {
                    return new CertificateCollection();
                }

                using Certificate certificate = DecodeCertificate(key, value);
                return new CertificateCollection { certificate };
            }
            catch (Exception ex) when (ShouldWrapReadFailure(ex, ct))
            {
                throw UnavailableRecords(ex);
            }
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
        /// <exception cref="ServiceResultException">The committed revocation data could not be verified.</exception>
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

            bool hasCurrentCrl = false;
            DateTime now = DateTime.UtcNow;
            foreach (X509CRL crl in await EnumerateCRLsAsync(issuer, validateUpdateTime: false, ct)
                .ConfigureAwait(false))
            {
                if (crl.IsRevoked(certificate))
                {
                    return StatusCodes.BadCertificateRevoked;
                }
                hasCurrentCrl |= IsCurrent(crl, now);
            }

            return hasCurrentCrl ? StatusCodes.Good : StatusCodes.BadCertificateRevocationUnknown;
        }

        /// <inheritdoc/>
        /// <exception cref="ServiceResultException">Committed records could not be verified.</exception>
        public async Task<X509CRLCollection> EnumerateCRLsAsync(CancellationToken ct = default)
        {
            var crls = new X509CRLCollection();
            try
            {
                await foreach (KeyValuePair<string, ByteString> entry in m_store
                    .ScanAsync(CrlPrefix, ct)
                    .ConfigureAwait(false))
                {
                    crls.Add(DecodeCrl(entry.Key, entry.Value));
                }
                return crls;
            }
            catch (Exception ex) when (ShouldWrapReadFailure(ex, ct))
            {
                throw UnavailableRecords(ex);
            }
        }

        /// <inheritdoc/>
        /// <exception cref="ServiceResultException">Committed records could not be verified.</exception>
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
            DateTime now = DateTime.UtcNow;
            try
            {
                foreach (X509CRL crl in await EnumerateCRLsAsync(ct).ConfigureAwait(false))
                {
                    if (AppliesToIssuer(crl, issuer) && (!validateUpdateTime || IsCurrent(crl, now)))
                    {
                        crls.Add(crl);
                    }
                }
                return crls;
            }
            catch (Exception ex) when (ShouldWrapReadFailure(ex, ct))
            {
                throw UnavailableRecords(ex);
            }
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

            string key = CrlKey(crl.RawData);
            await m_store
                .SetAsync(
                    key,
                    m_protector.Protect(
                        RecordProtectionContext.Create("certificate-store", key),
                        new ByteString(crl.RawData)),
                    ct)
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

        /// <summary>
        /// Checks the backend and protector for both direct and provider-based construction.
        /// </summary>
        internal static IRecordProtector RequireProtection(ISharedKeyValueStore store, IRecordProtector? protector)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }
            if (store is InMemorySharedKeyValueStore)
            {
                return protector ?? NullRecordProtector.Instance;
            }
            if (store is not FreshnessProtectedKeyValueStore)
            {
                throw new ArgumentException(
                    "Distributed certificates require a freshness-protected store.", nameof(store));
            }
            if (protector is null or NullRecordProtector)
            {
                throw new ArgumentException(
                    "Distributed certificates require an authenticating protector.", nameof(protector));
            }
            return protector;
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
                if (TryUnprotect(entry.Key, entry.Value, out ByteString plaintext) &&
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

        private ByteString EncodeCertificate(string recordKey, Certificate certificate)
        {
            byte[] der = certificate.RawData;
            byte[] key = Encoding.UTF8.GetBytes(recordKey);
            byte[] plaintext = new byte[TimestampLength + sizeof(int) + key.Length + der.Length];
            BinaryPrimitives.WriteInt64LittleEndian(plaintext, DateTime.UtcNow.Ticks);
            BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(TimestampLength), key.Length);
            key.CopyTo(plaintext.AsSpan(TimestampLength + sizeof(int)));
            der.CopyTo(plaintext.AsSpan(TimestampLength + sizeof(int) + key.Length));
            return m_protector.Protect(
                RecordProtectionContext.Create("certificate-store", recordKey),
                new ByteString(plaintext));
        }

        private Certificate DecodeCertificate(string recordKey, ByteString value)
        {
            if (!TryUnprotect(recordKey, value, out ByteString plaintext) ||
                plaintext.Span.Length <= TimestampLength + sizeof(int))
            {
                throw InvalidRecord();
            }

            ReadOnlySpan<byte> data = plaintext.Span;
            int keyLength = BinaryPrimitives.ReadInt32LittleEndian(data[TimestampLength..]);
            byte[] expectedKey = Encoding.UTF8.GetBytes(recordKey);
            const int keyOffset = TimestampLength + sizeof(int);
            if (keyLength != expectedKey.Length ||
                keyLength >= data.Length - keyOffset ||
                !CryptoUtils.FixedTimeEquals(data.Slice(keyOffset, keyLength), expectedKey))
            {
                throw InvalidRecord();
            }

            Certificate certificate = Certificate.FromRawData(data[(keyOffset + keyLength)..].ToArray());
            try
            {
                if (!string.Equals(recordKey, CertKey(certificate.Thumbprint), StringComparison.Ordinal))
                {
                    throw InvalidRecord();
                }
                return certificate;
            }
            catch
            {
                certificate.Dispose();
                throw;
            }
        }

        private X509CRL DecodeCrl(string recordKey, ByteString value)
        {
            if (!TryUnprotect(recordKey, value, out ByteString plaintext) || plaintext.Span.IsEmpty)
            {
                throw InvalidRecord();
            }
            byte[] der = plaintext.Span.ToArray();
            if (!string.Equals(recordKey, CrlKey(der), StringComparison.Ordinal))
            {
                throw InvalidRecord();
            }
            return new X509CRL(der);
        }

        private bool TryUnprotect(string recordKey, ByteString value, out ByteString plaintext)
        {
            if (m_protector.TryUnprotect(
                RecordProtectionContext.Create("certificate-store", recordKey),
                value,
                out plaintext))
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

        private static bool AppliesToIssuer(X509CRL crl, Certificate issuer)
        {
            if (!X509Utils.CompareDistinguishedName(crl.IssuerName, issuer.SubjectName))
            {
                return false;
            }
            if (crl.VerifySignature(issuer, false))
            {
                return true;
            }

            // A different key identifier distinguishes same-name issuers, such as a CA rollover.
            string? crlKey = crl.CrlExtensions.FindExtension<X509AuthorityKeyIdentifierExtension>()?.KeyIdentifier;
            string? issuerKey = issuer.FindExtension<X509SubjectKeyIdentifierExtension>()?.SubjectKeyIdentifier;
            if (!string.IsNullOrEmpty(crlKey) && !string.IsNullOrEmpty(issuerKey) &&
                !string.Equals(crlKey, issuerKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            throw new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed,
                "Could not verify the signature of a committed revocation list for this issuer.");
        }

        private static bool IsCurrent(X509CRL crl, DateTime now)
        {
            return crl.ThisUpdate <= now && (crl.NextUpdate == DateTime.MinValue || crl.NextUpdate >= now);
        }

        private static bool ShouldWrapReadFailure(Exception error, CancellationToken ct)
        {
            return (error is not OperationCanceledException || !ct.IsCancellationRequested) &&
                (error is not ServiceResultException serviceError ||
                    serviceError.StatusCode != StatusCodes.BadSecurityChecksFailed);
        }

        private static ServiceResultException UnavailableRecords(Exception error)
        {
            return new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed,
                "Could not read the committed shared certificate store records.",
                error);
        }

        private static ServiceResultException InvalidRecord()
        {
            return new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed,
                "A committed shared certificate store record could not be verified.");
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
            var builder = new StringBuilder(bytes.Length * 2);
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
            [ApplicationIdentity] string? thumbprint,
            [FilePath] string? storePath);

        [LoggerMessage(EventId = CoreEventIds.SharedKeyValueCertificateStore + 1, Level = LogLevel.Debug,
            Message = "CRL for issuer {Issuer} added to shared store {StorePath}.")]
        public static partial void SharedKeyValueStoreLog1(
            this ILogger logger,
            [ApplicationIdentity] string? issuer,
            [FilePath] string? storePath);

        [LoggerMessage(EventId = CoreEventIds.SharedKeyValueCertificateStore + 4, Level = LogLevel.Warning,
            Message = "Rejected a certificate record that failed integrity verification in shared store {StorePath}.")]
        public static partial void SharedKeyValueStoreLog4(this ILogger logger, [FilePath] string? storePath);
    }
}
