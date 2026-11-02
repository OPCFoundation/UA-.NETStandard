/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace OnboardingClientSample
{
    /// <summary>
    /// Installs the single sample DCA's RSA credential using its existing certificate-store key.
    /// The caller supplies an explicitly trusted registrar; this provider does not approve discovered peers.
    /// </summary>
    internal sealed class OnboardingDeviceCredentials : IPullDcaIdentityProvider, IPullDcaCredentialProvider
    {
        /// <summary>
        /// Borrows the configuration and certificate manager owned by the client host.
        /// Certificate-store creation consults the supplied host providers before the built-in store types.
        /// </summary>
        internal OnboardingDeviceCredentials(
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry,
            ArrayOf<ICertificateStoreProvider> storeProviders = default)
        {
            m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_manager = configuration.CertificateManager ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            m_storeProviders = storeProviders;
        }

        /// <inheritdoc/>
        public async ValueTask ActivateAsync(
            ByteString selectedIdentity,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Certificate selected = Certificate.FromRawData(selectedIdentity);
            CertificateIdentifier current = GetIdentifier();
            var identifier = new CertificateIdentifier
            {
                StoreType = current.StoreType,
                StorePath = current.StorePath,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType,
                Thumbprint = selected.Thumbprint
            };
            using Certificate? local = await m_manager.CertificateProvider.GetPrivateKeyCertificateAsync(
                identifier, m_configuration.SecurityConfiguration.CertificatePasswordProvider,
                ct: cancellationToken).ConfigureAwait(false);
            if (local == null || !local.HasPrivateKey || !local.RawData.AsSpan().SequenceEqual(selectedIdentity.Span))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The selected identity's private key is not in this DCA.");
            }
            await m_manager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType, local, ct: cancellationToken).ConfigureAwait(false);
            current.RawData = null;
            current.Thumbprint = local.Thumbprint;
            current.SubjectName = local.Subject;
        }

        /// <inheritdoc/>
        public ValueTask<ByteString> CreateSigningRequestAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            CancellationToken cancellationToken = default)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (applicationId.IsNull || profile.CertificateGroupId.IsNull ||
                profile.CertificateTypeId != ObjectTypeIds.RsaSha256ApplicationCertificateType ||
                (!m_applicationId.IsNull && (m_applicationId != applicationId || m_profile != profile)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadRequestNotAllowed, "This sample credential provider serves only one RSA DCA.");
            }
            using CertificateEntry entry = m_manager.AcquireApplicationCertificateByType(
                profile.CertificateTypeId) ?? throw new ServiceResultException(StatusCodes.BadConfigurationError);
            if (!entry.Certificate.HasPrivateKey)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            ByteString csr = ByteString.From(
                DefaultCertificateFactory.Instance.CreateSigningRequest(entry.Certificate));
            m_applicationId = applicationId;
            m_profile = profile;
            return new ValueTask<ByteString>(csr);
        }

        /// <summary>
        /// Restores the host-retained request binding only after proving that its original key remains available.
        /// The checkpoint must originate from this device, not from a remote registrar or discovery response.
        /// This does not establish that a certificate returned by GetCertificates is new for that request.
        /// </summary>
        internal ValueTask RestoreSigningRequestAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            ByteString signingKeyCertificate,
            CancellationToken cancellationToken = default)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (applicationId.IsNull || profile.CertificateGroupId.IsNull ||
                profile.CertificateTypeId != ObjectTypeIds.RsaSha256ApplicationCertificateType ||
                (!m_applicationId.IsNull && (m_applicationId != applicationId || m_profile != profile)))
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
            using Certificate original = Certificate.FromRawData(signingKeyCertificate);
            if (!X509Utils.GetApplicationUrisFromCertificate(original).Contains(m_configuration.ApplicationUri))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
            }
            using CertificateEntry key = m_manager.AcquireApplicationCertificateByType(
                profile.CertificateTypeId) ?? throw new ServiceResultException(StatusCodes.BadConfigurationError);
            if (!key.Certificate.HasPrivateKey)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            using Certificate proof = DefaultCertificateFactory.Instance.CreateWithPrivateKey(
                original, key.Certificate);
            if (!proof.HasPrivateKey)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            m_applicationId = applicationId;
            m_profile = profile;
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask InstallAsync(
            NodeId applicationId,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            if (credentials == null)
            {
                throw new ArgumentNullException(nameof(credentials));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (applicationId.IsNull || applicationId != m_applicationId)
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
            using Certificate issued = Certificate.FromRawData(credentials.Certificate);
            if (!X509Utils.GetApplicationUrisFromCertificate(issued).Contains(m_configuration.ApplicationUri))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
            }
            using CertificateEntry key = m_manager.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType) ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            using Certificate combined = DefaultCertificateFactory.Instance.CreateWithPrivateKey(
                issued, key.Certificate);
            using var chain = new CertificateCollection { issued };
            for (int i = 0; i < credentials.IssuerCertificates.Count; i++)
            {
                using Certificate issuer = Certificate.FromRawData(credentials.IssuerCertificates[i]);
                chain.Add(issuer);
            }
            TrustListDataType received = credentials.TrustList;
            if (received.SpecifiedLists != (uint)TrustListMasks.All)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "Onboarding requires a complete DCA trust list.");
            }
            using TrustListData trust = DecodeTrustList(received);
            CertificateValidationResult validation = await m_manager.ValidateAsync(
                chain, TrustListIdentifier.Peers, new ValidationOptions
                {
                    TrustScope = new CertificateValidationScope(
                        received.TrustedCertificates, received.IssuerCertificates,
                        received.TrustedCrls.ToArray().Concat(received.IssuerCrls.ToArray()).ToArray().ToArrayOf()),
                    RejectUnknownRevocationStatus = true,
                    RejectSHA1SignedCertificates = true,
                    MinimumCertificateKeySize = 2048,
                    AutoAcceptUntrustedCertificates = false,
                    AcceptError = static (_, _) => false,
                    RecordRejectedCertificates = false
                }, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                throw new ServiceResultException(validation.StatusCode);
            }
            CertificateIdentifier identifier = GetIdentifier();
            if (string.IsNullOrEmpty(identifier.StorePath) || string.IsNullOrEmpty(identifier.StoreType))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            using ICertificateStore store = CertificateStoreIdentifier.CreateStore(
                identifier.StoreType, m_telemetry, m_storeProviders.ToArray());
            store.Open(identifier.StorePath, noPrivateKeys: false);
            char[] password = m_configuration.SecurityConfiguration.CertificatePasswordProvider?
                .GetPassword(identifier) ?? [];
            try
            {
                await store.AddAsync(combined, password, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Array.Clear(password, 0, password.Length);
            }
            await m_manager.WriteTrustListAsync(
                TrustListIdentifier.Peers, trust, TrustListMasks.All, cancellationToken).ConfigureAwait(false);
            using var issuers = new CertificateCollection();
            for (int i = 1; i < chain.Count; i++)
            {
                issuers.Add(chain[i]);
            }
            await m_manager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType, combined, issuers, cancellationToken)
                .ConfigureAwait(false);
            identifier.RawData = null;
            identifier.Thumbprint = combined.Thumbprint;
            identifier.SubjectName = combined.Subject;
        }

        /// <summary>
        /// Parses the complete received trust list before changing any trust-store content.
        /// </summary>
        private static TrustListData DecodeTrustList(TrustListDataType received)
        {
            var result = new TrustListData();
            try
            {
                foreach (ByteString encoded in received.TrustedCertificates)
                {
                    using Certificate certificate = Certificate.FromRawData(encoded);
                    result.TrustedCertificates.Add(certificate);
                }
                foreach (ByteString encoded in received.IssuerCertificates)
                {
                    using Certificate certificate = Certificate.FromRawData(encoded);
                    result.IssuerCertificates.Add(certificate);
                }
                foreach (ByteString encoded in received.TrustedCrls)
                {
                    result.TrustedCrls.Add(new X509CRL(encoded.ToArray()));
                }
                foreach (ByteString encoded in received.IssuerCrls)
                {
                    result.IssuerCrls.Add(new X509CRL(encoded.ToArray()));
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Returns the explicit local RSA certificate-store configuration.
        /// </summary>
        private CertificateIdentifier GetIdentifier()
        {
            foreach (CertificateIdentifier identifier in m_configuration.SecurityConfiguration.ApplicationCertificates)
            {
                if (identifier.CertificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType)
                {
                    return identifier;
                }
            }
            throw new ServiceResultException(StatusCodes.BadConfigurationError);
        }

        /// <summary>The host-owned application configuration.</summary>
        private readonly ApplicationConfiguration m_configuration;
        /// <summary>The host-owned certificate manager.</summary>
        private readonly ICertificateManager m_manager;
        /// <summary>Telemetry used to open the configured certificate store.</summary>
        private readonly ITelemetryContext m_telemetry;
        /// <summary>Host-registered store providers, consulted before built-in store types.</summary>
        private readonly ArrayOf<ICertificateStoreProvider> m_storeProviders;
        /// <summary>The one DCA application whose key was used for the CSR.</summary>
        private NodeId m_applicationId;
        /// <summary>The original certificate group/type selected for the retained DCA request.</summary>
        private PullDcaCertificateProfile? m_profile;
    }
}
