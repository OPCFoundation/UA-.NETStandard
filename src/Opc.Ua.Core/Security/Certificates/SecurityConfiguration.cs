// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// The security configuration for the application.
    /// </summary>
    public partial class SecurityConfiguration
    {
        /// <summary>
        /// The security profiles which are supported for this configuration.
        /// </summary>
        public ArrayOf<string> SupportedSecurityPolicies { get; private set; }

        /// <summary>
        /// Get the provider which is invoked when a password
        /// for a private key is requested.
        /// </summary>
        public ICertificatePasswordProvider? CertificatePasswordProvider { get; set; }

        /// <summary>
        /// Adds a certificate as a trusted peer.
        /// </summary>
        public void AddTrustedPeer(byte[] certificate)
        {
            TrustedPeerCertificates.TrustedCertificates =
                TrustedPeerCertificates.TrustedCertificates.AddItem(
                    new CertificateIdentifier { RawData = certificate });
        }

        /// <summary>
        /// Validates the security configuration.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void Validate(ITelemetryContext telemetry)
        {
            Validate(telemetry, null);
        }

        /// <summary>
        /// Validates security settings and configured stores through an optional instance-scoped resolver.
        /// </summary>
        internal void Validate(ITelemetryContext telemetry, ICertificateStoreResolver? storeResolver)
        {
            if (m_rejectedCertificateTypes.Count > 0)
            {
                ILogger<SecurityConfiguration> logger = telemetry
                    .CreateLogger<SecurityConfiguration>();
                logger.UnsupportedApplicationCertificateTypes(
                    m_rejectedCertificateTypes.Count,
                    string.Join(", ", m_rejectedCertificateTypes));
            }

            if (m_applicationCertificates.IsNull || m_applicationCertificates.Count == 0)
            {
                if (m_rejectedCertificateTypes.Count > 0)
                {
                    throw ServiceResultException.ConfigurationError(
                        "No supported application certificate configured: {0} certificate identifier(s) " +
                        "were rejected because their CertificateType is not supported ({1}).",
                        m_rejectedCertificateTypes.Count,
                        string.Join(", ", m_rejectedCertificateTypes));
                }

                throw ServiceResultException.ConfigurationError(
                    "ApplicationCertificate must be specified.");
            }
            // ensure mandatory stores are valid
            ValidateStore(TrustedIssuerCertificates, nameof(TrustedIssuerCertificates), telemetry, storeResolver);
            ValidateStore(TrustedPeerCertificates, nameof(TrustedPeerCertificates), telemetry, storeResolver);

            //ensure optional stores are valid if specified
            if (TrustedHttpsCertificates != null)
            {
                ValidateStore(TrustedHttpsCertificates, nameof(TrustedHttpsCertificates), telemetry, storeResolver);
            }
            if (HttpsIssuerCertificates != null)
            {
                ValidateStore(HttpsIssuerCertificates, nameof(HttpsIssuerCertificates), telemetry, storeResolver);
            }
            if (TrustedUserCertificates != null)
            {
                ValidateStore(TrustedUserCertificates, nameof(TrustedUserCertificates), telemetry, storeResolver);
            }
            if (UserIssuerCertificates != null)
            {
                ValidateStore(UserIssuerCertificates, nameof(UserIssuerCertificates), telemetry, storeResolver);
            }

            if ((TrustedHttpsCertificates != null && HttpsIssuerCertificates == null) ||
                (HttpsIssuerCertificates != null && TrustedHttpsCertificates == null))
            {
                throw ServiceResultException.ConfigurationError(
                    "Either none or both of HttpsIssuerCertificates & TrustedHttpsCertificates stores must be specified.");
            }

            if ((TrustedUserCertificates != null && UserIssuerCertificates == null) ||
                (UserIssuerCertificates != null && TrustedUserCertificates == null))
            {
                throw ServiceResultException.ConfigurationError(
                    "Either none or both of UserIssuerCertificates & TrustedUserCertificates stores must be specified.");
            }

            // replace subjectName DC=localhost with DC=hostname
            foreach (CertificateIdentifier applicationCertificate in m_applicationCertificates)
            {
                applicationCertificate.SubjectName = Utils.ReplaceDCLocalhost(
                    applicationCertificate.SubjectName);
            }
        }

        /// <summary>
        /// Validate if the specified store can be opened
        /// throws ServiceResultException
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateStore(
            CertificateTrustList? storeIdentifier,
            string storeName,
            ITelemetryContext telemetry,
            ICertificateStoreResolver? storeResolver)
        {
            if (string.IsNullOrEmpty(storeIdentifier?.StorePath))
            {
                throw ServiceResultException.ConfigurationError(
                    "{0} StorePath must be specified.", storeName);
            }
            try
            {
                using ICertificateStore store = (storeResolver == null
                    ? storeIdentifier!.OpenStore(telemetry)
                    : storeResolver.OpenCertificateStore(storeIdentifier!.StorePath, storeIdentifier.StoreType)) ??
                    throw ServiceResultException.ConfigurationError(
                        "Failed to open {0} store", storeName);
            }
            catch (Exception ex)
            {
                ILogger<SecurityConfiguration> logger = telemetry.CreateLogger<SecurityConfiguration>();
                logger.SecurityConfigurationLogMessage0(ex, storeName);
                throw ServiceResultException.ConfigurationError("{0} store is invalid.", storeName);
            }
        }

        /// <summary>
        /// Find application certificate for a security policy.
        /// </summary>
        public Task<Certificate?> FindApplicationCertificateAsync(
            string securityPolicy,
            bool privateKey,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            return FindApplicationCertificateAsync(
                securityPolicy,
                privateKey,
                telemetry,
                loadedKeys: null,
                loadedKeysApplicationUri: null,
                ct);
        }

        /// <summary>
        /// Find application certificate for a security policy, reusing a
        /// private key <paramref name="loadedKeys"/> loaded from the
        /// configured store when the store lookup would load the same
        /// certificate.
        /// </summary>
        /// <param name="securityPolicy">The security policy URI.</param>
        /// <param name="privateKey">Whether the private key is required.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="loadedKeys">The certificate manager that loaded this
        /// configuration's keys, or <see langword="null"/>.</param>
        /// <param name="loadedKeysApplicationUri">The application URI the
        /// manager loaded the keys with.</param>
        /// <param name="ct">The cancellation token.</param>
        internal async Task<Certificate?> FindApplicationCertificateAsync(
            string securityPolicy,
            bool privateKey,
            ITelemetryContext telemetry,
            CertificateManager? loadedKeys,
            string? loadedKeysApplicationUri,
            CancellationToken ct)
        {
            foreach (NodeId certType in CertificateIdentifier.MapSecurityPolicyToCertificateTypes(
                securityPolicy))
            {
                CertificateIdentifier? id = (ApplicationCertificates.ToArray() ?? []).FirstOrDefault(certId =>
                    certId.CertificateType == certType);
                if (id == null)
                {
                    if (certType == ObjectTypeIds.RsaSha256ApplicationCertificateType)
                    {
                        // Fallback to old behavior of looking for an entry with no certificate type specified
                        // that will keep old configs working.

                        // undefined certificate type as RsaSha256
                        id = (ApplicationCertificates.ToArray() ?? []).FirstOrDefault(
                            certId => certId.CertificateType.IsNull);
                    }
                }

                if (id != null)
                {
                    if (privateKey)
                    {
                        return loadedKeys?.TryAcquireLoadedApplicationKeyForConfiguredStore(
                                id,
                                CertificatePasswordProvider,
                                loadedKeysApplicationUri)
                            ?? await CertificateIdentifierResolver
                            .LoadPrivateKeyAsync(
                                id,
                                CertificatePasswordProvider,
                                applicationUri: null,
                                telemetry,
                                ct)
                            .ConfigureAwait(false);
                    }

                    return await CertificateIdentifierResolver
                        .ResolveAsync(
                            id,
                            registry: null,
                            needPrivateKey: false,
                            applicationUri: null,
                            telemetry,
                            ct)
                        .ConfigureAwait(false);
                }

                Certificate? certificate = await FindAbstractApplicationCertificateAsync(
                    certType,
                    privateKey,
                    telemetry,
                    ct).ConfigureAwait(false);
                if (certificate != null)
                {
                    return certificate;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a concrete certificate from entries configured with the abstract ApplicationCertificateType.
        /// </summary>
        private async Task<Certificate?> FindAbstractApplicationCertificateAsync(
            NodeId requiredConcreteCertificateType,
            bool privateKey,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            foreach (CertificateIdentifier id in (ApplicationCertificates.ToArray() ?? []).Where(certId =>
                certId.CertificateType == ObjectTypeIds.ApplicationCertificateType))
            {
                var candidateId = new CertificateIdentifier
                {
                    StoreType = id.StoreType,
                    StorePath = id.StorePath,
                    SubjectName = id.SubjectName,
                    Thumbprint = id.Thumbprint,
                    RawData = id.RawData,
                    ValidationOptions = id.ValidationOptions,
                    CertificateType = NodeId.Null
                };

                Certificate? certificate = privateKey
                    ? await CertificateIdentifierResolver
                        .LoadPrivateKeyAsync(
                            candidateId,
                            CertificatePasswordProvider,
                            applicationUri: null,
                            telemetry,
                            ct)
                        .ConfigureAwait(false)
                    : await CertificateIdentifierResolver
                        .ResolveAsync(
                            candidateId,
                            registry: null,
                            needPrivateKey: false,
                            applicationUri: null,
                            telemetry,
                            ct)
                        .ConfigureAwait(false);

                if (certificate != null &&
                    CertificateIdentifier.ValidateCertificateType(
                        certificate,
                        requiredConcreteCertificateType))
                {
                    return certificate;
                }

                certificate?.Dispose();
            }

            return null;
        }

        /// <summary>
        /// Use the list of application certificates to build a list
        /// of supported security policies.
        /// </summary>
        private ArrayOf<string> BuildSupportedSecurityPolicies()
        {
            var securityPolicies = new List<string> { SecurityPolicies.None };
            foreach (CertificateIdentifier applicationCertificate in m_applicationCertificates)
            {
                securityPolicies.AddRange(
                    SecurityPolicies.Default.GetSupportedUrisForCertificateType(applicationCertificate.CertificateType));
            }

            return securityPolicies.Distinct().ToArrayOf();
        }
    }

    /// <summary>
    /// Source-generated log messages for SecurityConfiguration.
    /// </summary>
    internal static partial class SecurityConfigurationLog
    {
        [LoggerMessage(EventId = CoreEventIds.SecurityConfiguration + 0, Level = LogLevel.Error,
            Message = "Failed to open {StoreName} store")]
        public static partial void SecurityConfigurationLogMessage0(
            this ILogger logger,
            Exception? exception,
            string storeName);

        [LoggerMessage(EventId = CoreEventIds.SecurityConfiguration + 1, Level = LogLevel.Warning,
            Message = "{Count} application certificate identifier(s) were dropped from " +
                "ApplicationCertificates because their CertificateType is not supported: {CertificateTypes}")]
        public static partial void UnsupportedApplicationCertificateTypes(
            this ILogger logger,
            int count,
            string certificateTypes);
    }
}
