/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// A list of trusted certificates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Administrators can create a list of trusted certificates by designating all certificates
    /// in a particular certificate store as trusted and/or by explictly specifying a list of
    /// individual certificates.
    /// </para>
    /// <para>
    /// A trust list can contain either instance certificates or certification authority certificates.
    /// If the list contains instance certificates the application will trust peers that use the
    /// instance certificate (provided the ApplicationUri and HostName match the certificate).
    /// </para>
    /// <para>
    /// If the list contains certification authority certificates then the application will trust
    /// peers that have certificates issued by one of the authorities.
    /// </para>
    /// <para>
    /// Any certificate could be revoked by the issuer (CAs may issue certificates for other CAs).
    /// The RevocationMode specifies whether this check should be done each time a certificate
    /// in the list are used.
    /// </para>
    /// </remarks>
    public partial class CertificateTrustList : CertificateStoreIdentifier
    {
        /// <summary>
        /// Returns the certificates in the trust list.
        /// </summary>
        [Obsolete("Use GetCertificatesAsync() instead.")]
        public Task<CertificateCollection> GetCertificates()
        {
            return GetCertificatesAsync(null!);
        }

        /// <summary>
        /// Returns the certificates in the trust list.
        /// </summary>
        /// <returns>
        /// An owning collection that the caller must dispose. Its certificates
        /// are independent of the temporary resolver and store handles.
        /// </returns>
        /// <exception cref="ServiceResultException"></exception>
        public async Task<CertificateCollection> GetCertificatesAsync(
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            CertificateCollection? collection = null;

            try
            {
                ct.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(StorePath))
                {
                    ICertificateStore? store = null;
                    try
                    {
                        store = OpenStore(telemetry) ??
                            throw ServiceResultException.ConfigurationError(
                                "Failed to open certificate store.");

                        collection = await store.EnumerateAsync(ct).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        ILogger<CertificateTrustList> logger = telemetry.CreateLogger<CertificateTrustList>();
                        if (logger.IsEnabled(LogLevel.Error))
                        {
                            logger.CertificateTrustListLogMessage0(StorePath);
                        }
                    }
                    finally
                    {
                        store?.Dispose();
                    }
                }

                collection ??= [];

                for (int i = 0; i < TrustedCertificates.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    CertificateIdentifier trustedCertificate = TrustedCertificates[i];
                    using Certificate? certificate = await CertificateIdentifierResolver
                        .ResolveAsync(
                            trustedCertificate,
                            registry: null,
                            needPrivateKey: false,
                            applicationUri: null,
                            telemetry,
                            ct)
                        .ConfigureAwait(false);

                    if (certificate != null)
                    {
                        collection.Add(certificate);
                    }
                }

                CertificateCollection result = collection;
                collection = null;
                return result;
            }
            finally
            {
                collection?.Dispose();
            }
        }

        /// <summary>
        /// Copies trust-list metadata and inline certificate data for a validator.
        /// </summary>
        internal static CertificateTrustList? CreateSnapshot(CertificateStoreIdentifier? store)
        {
            if (store == null)
            {
                return null;
            }

            ArrayOf<CertificateIdentifier> trustedCertificates = default;
            if (store is CertificateTrustList trustList)
            {
                var entries = new CertificateIdentifier[trustList.TrustedCertificates.Count];
                for (int i = 0; i < entries.Length; i++)
                {
                    CertificateIdentifier identifier = trustList.TrustedCertificates[i];
                    entries[i] = new CertificateIdentifier
                    {
                        RawData = identifier.RawData != null ? identifier.RawData.AsSpan().ToArray() : null,
                        StorePath = identifier.StorePath,
                        StoreType = identifier.StoreType,
                        SubjectName = identifier.SubjectName,
                        Thumbprint = identifier.Thumbprint,
                        CertificateType = identifier.CertificateType,
                        ValidationOptions = identifier.ValidationOptions
                    };
                }
                trustedCertificates = entries.ToArrayOf();
            }

            return new CertificateTrustList
            {
                StorePath = store.StorePath,
                StoreType = store.StoreType,
                ValidationOptions = store.ValidationOptions,
                TrustedCertificates = trustedCertificates
            };
        }
    }

    /// <summary>
    /// Source-generated log messages for CertificateTrustList.
    /// </summary>
    internal static partial class CertificateTrustListLog
    {
        [LoggerMessage(EventId = CoreEventIds.CertificateTrustList + 0, Level = LogLevel.Error,
            Message = "Could not load certificates from store: {StorePath}.")]
        public static partial void CertificateTrustListLogMessage0(this ILogger logger, [FilePath] string? storePath);
    }

}
