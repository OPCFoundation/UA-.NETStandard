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
        /// Disposes the store instances cached on the configured trust-list
        /// and rejected-store identifiers. A store opened through a
        /// <see cref="CertificateStoreIdentifier"/> deliberately retains its
        /// parsed certificates across <see cref="ICertificateStore.Close"/>
        /// for reuse, so the application-lifetime identifiers held by this
        /// configuration keep those resources alive until they are released
        /// here. Called by <c>ApplicationInstance.DisposeAsync</c>; hosts
        /// that own an <see cref="ApplicationConfiguration"/> without an
        /// application instance should call it at shutdown themselves. Any
        /// later <see cref="CertificateStoreIdentifier.OpenStore()"/>
        /// re-creates the store.
        /// </summary>
        public void DisposeCachedStores()
        {
            TrustedIssuerCertificates?.DisposeCachedStore();
            TrustedPeerCertificates?.DisposeCachedStore();
            HttpsIssuerCertificates?.DisposeCachedStore();
            TrustedHttpsCertificates?.DisposeCachedStore();
            UserIssuerCertificates?.DisposeCachedStore();
            TrustedUserCertificates?.DisposeCachedStore();
            RejectedCertificateStore?.DisposeCachedStore();
        }

        /// <summary>
        /// Validates the security configuration.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void Validate(ITelemetryContext telemetry)
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
            ValidateStore(TrustedIssuerCertificates, nameof(TrustedIssuerCertificates), telemetry);
            ValidateStore(TrustedPeerCertificates, nameof(TrustedPeerCertificates), telemetry);

            //ensure optional stores are valid if specified
            if (TrustedHttpsCertificates != null)
            {
                ValidateStore(TrustedHttpsCertificates, nameof(TrustedHttpsCertificates), telemetry);
            }
            if (HttpsIssuerCertificates != null)
            {
                ValidateStore(HttpsIssuerCertificates, nameof(HttpsIssuerCertificates), telemetry);
            }
            if (TrustedUserCertificates != null)
            {
                ValidateStore(TrustedUserCertificates, nameof(TrustedUserCertificates), telemetry);
            }
            if (UserIssuerCertificates != null)
            {
                ValidateStore(UserIssuerCertificates, nameof(UserIssuerCertificates), telemetry);
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
            ITelemetryContext telemetry)
        {
            if (string.IsNullOrEmpty(storeIdentifier?.StorePath))
            {
                throw ServiceResultException.ConfigurationError(
                    "{0} StorePath must be specified.", storeName);
            }
            try
            {
                ICertificateStore store = storeIdentifier!.OpenStore(telemetry) ??
                    throw ServiceResultException.ConfigurationError(
                        "Failed to open {0} store", storeName);
                store.Close();
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
        public async Task<Certificate?> FindApplicationCertificateAsync(
            string securityPolicy,
            bool privateKey,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
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
                        return await CertificateIdentifierResolver
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
