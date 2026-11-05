/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Validates the certificate handoff from an external authentication service under OPC 10000-21 section 7.4.
    /// Implementations do not establish possession; callers supply only authenticated channel evidence.
    /// A future FDO adapter can implement this interface without changing registrar operations.
    /// </summary>
    public interface IDeviceRegistrarExternalAuthenticationProvider
    {
        /// <summary>
        /// Returns a trusted and explicitly approved device, or null when the issuer is not accepted.
        /// Approval denial, unavailable validation and cancellation must propagate as errors.
        /// No certificate, candidate, software approval or application relation is persisted by this operation.
        /// </summary>
        ValueTask<DeviceRegistrarExternalAuthenticationResult?> EvaluateAsync(
            ByteString channelCertificate,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Selects and approves a ProductInstanceUri from a trusted external certificate's URI subject alternative names.
    /// This policy must approve the actual DCA application and its software requirements, not only its issuer.
    /// </summary>
    public interface IDeviceRegistrarExternalAuthenticationPolicy
    {
        /// <summary>
        /// Returns the configured device and DCA decision, or throws a service error when approval is absent.
        /// The supplied certificate and names have passed the external provider's certificate validation.
        /// </summary>
        ValueTask<DeviceRegistrarExternalAuthenticationResult> EvaluateAsync(
            ByteString certificate,
            ArrayOf<string> certificateUris,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// An approved external device identity and its ordinary registrar onboarding decision.
    /// </summary>
    public sealed record DeviceRegistrarExternalAuthenticationResult(
        string ProductInstanceUri,
        DeviceRegistrarOnboardingDecision Decision);

    /// <summary>
    /// Validates external DCA certificates using a dedicated managed CA trust list and host approval policy.
    /// It does not implement FDO discovery, vouchers, service-info exchange or certificate issuance.
    /// </summary>
    public sealed class ExternalAuthenticationProvider : IDeviceRegistrarExternalAuthenticationProvider
    {
        /// <summary>
        /// Borrows the certificate manager and the host's explicit device-approval policy.
        /// </summary>
        public ExternalAuthenticationProvider(
            ICertificateManager certificates,
            IDeviceRegistrarExternalAuthenticationPolicy policy)
        {
            m_certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
            m_policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        /// <summary>
        /// Gets the separate trust list containing authentication-service CA anchors, issuers and revocation data.
        /// The normal application peer list and ticket authorities are not external authentication anchors.
        /// </summary>
        public static TrustListIdentifier AuthenticationAuthorities { get; } =
            new("ExternalAuthenticationAuthorities");

        /// <inheritdoc/>
        public async ValueTask<DeviceRegistrarExternalAuthenticationResult?> EvaluateAsync(
            ByteString channelCertificate,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (channelCertificate.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            using Certificate certificate = Certificate.FromRawData(channelCertificate.ToArray());
            if (!certificate.RawData.AsSpan().SequenceEqual(channelCertificate.Span) ||
                X509Utils.IsCertificateAuthority(certificate))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            using TrustListData trust = await m_certificates.ReadTrustListAsync(
                AuthenticationAuthorities, TrustListMasks.All, cancellationToken).ConfigureAwait(false);
            var anchors = new List<ByteString>();
            var issuers = new List<ByteString>();
            var crls = new List<ByteString>();
            foreach (Certificate anchor in trust.TrustedCertificates)
            {
                if (!X509Utils.IsCertificateAuthority(anchor))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "External authentication requires CA trust anchors.");
                }
                anchors.Add(ByteString.From(anchor.RawData.AsSpan()));
            }
            if (anchors.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "No external authentication CA is configured.");
            }
            foreach (Certificate issuer in trust.IssuerCertificates)
            {
                issuers.Add(ByteString.From(issuer.RawData.AsSpan()));
            }
            foreach (X509CRL crl in trust.TrustedCrls)
            {
                crls.Add(ByteString.From(crl.RawData.AsSpan()));
            }
            foreach (X509CRL crl in trust.IssuerCrls)
            {
                crls.Add(ByteString.From(crl.RawData.AsSpan()));
            }
            using var chain = new CertificateCollection { certificate };
            CertificateValidationResult validation = await m_certificates.ValidateAsync(
                chain, AuthenticationAuthorities, new ValidationOptions
                {
                    TrustScope = new CertificateValidationScope(
                        anchors.ToArrayOf(), issuers.ToArrayOf(), crls.ToArrayOf()),
                    AutoAcceptUntrustedCertificates = false,
                    RejectSHA1SignedCertificates = true,
                    RejectUnknownRevocationStatus = true,
                    MinimumCertificateKeySize = 2048,
                    AcceptError = static (_, _) => false,
                    RecordRejectedCertificates = false
                }, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                foreach (ServiceResult error in validation.Errors)
                {
                    if (error.StatusCode != StatusCodes.BadCertificateUntrusted &&
                        error.StatusCode != StatusCodes.BadCertificateChainIncomplete)
                    {
                        throw new ServiceResultException(error);
                    }
                }
                if (validation.StatusCode != StatusCodes.BadCertificateUntrusted &&
                    validation.StatusCode != StatusCodes.BadCertificateChainIncomplete)
                {
                    validation.ThrowIfInvalid();
                }
                return null;
            }
            X509SubjectAltNameExtension? names = certificate.FindExtension<X509SubjectAltNameExtension>();
            var uris = new List<string>();
            if (names != null)
            {
                foreach (string uri in names.Uris)
                {
                    if (Uri.TryCreate(uri, UriKind.Absolute, out _) && !uris.Contains(uri))
                    {
                        uris.Add(uri);
                    }
                }
            }
            if (uris.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
            }
            DeviceRegistrarExternalAuthenticationResult result = await m_policy.EvaluateAsync(
                ByteString.From(channelCertificate.Span), uris.ToArrayOf(), cancellationToken).ConfigureAwait(false);
            ValidateResult(result, uris.ToArrayOf());
            return result;
        }

        /// <summary>
        /// Rejects approval metadata that substitutes an identity not present in the authenticated certificate.
        /// Custom providers must enforce this same boundary.
        /// </summary>
        internal static void ValidateResult(
            DeviceRegistrarExternalAuthenticationResult result,
            ArrayOf<string> certificateUris)
        {
            if (result == null || result.Decision?.Application == null ||
                string.IsNullOrEmpty(result.ProductInstanceUri) ||
                !certificateUris.Contains(result.ProductInstanceUri) ||
                !certificateUris.Contains(result.Decision.Application.ApplicationUri ?? string.Empty) ||
                result.Decision.RequiresSoftwareUpdate != (result.Decision.SoftwareUpdateManager != null) ||
                (result.Decision.SoftwareUpdateManager is { } manager &&
                    (manager.EndpointUrls.IsEmpty || string.IsNullOrWhiteSpace(manager.PurposeUri))))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
        }

        /// <summary>The host-owned certificate validation and trust-list infrastructure.</summary>
        private readonly ICertificateManager m_certificates;

        /// <summary>The host's explicit device and DCA approval boundary.</summary>
        private readonly IDeviceRegistrarExternalAuthenticationPolicy m_policy;
    }
}
