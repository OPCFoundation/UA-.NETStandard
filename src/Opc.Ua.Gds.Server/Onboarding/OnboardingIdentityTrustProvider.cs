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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Validates identities against only the certificate authorities embedded in their matching ticket.
    /// </summary>
    public sealed class OnboardingIdentityTrustProvider : IOnboardingIdentityTrustProvider
    {
        /// <summary>
        /// Creates a provider using the application's certificate manager and logger.
        /// </summary>
        public OnboardingIdentityTrustProvider(
            ICertificateManager certificateManager,
            ILogger<OnboardingIdentityTrustProvider> logger)
        {
            m_certificateManager = certificateManager ??
                throw new ArgumentNullException(nameof(certificateManager));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the managed trust-list identifier for device identity authorities.
        /// </summary>
        public static TrustListIdentifier DeviceIdentityAuthorities { get; } =
            new("DeviceIdentityAuthorities");

        /// <inheritdoc/>
        public async ValueTask<bool> IsTrustedAsync(
            Certificate identity,
            CertificateCollection issuers,
            BaseTicketType matchingTicket,
            CancellationToken cancellationToken = default)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }
            if (issuers == null)
            {
                throw new ArgumentNullException(nameof(issuers));
            }
            if (matchingTicket == null)
            {
                throw new ArgumentNullException(nameof(matchingTicket));
            }
            cancellationToken.ThrowIfCancellationRequested();

            var trustedAnchors = new List<ByteString>();
            var issuerMaterial = new List<ByteString>();

            for (int i = 0; i < matchingTicket.Authorities.Count; i++)
            {
                CertificateAuthorityType? authority = matchingTicket.Authorities[i];
                if (authority == null || authority.AuthorityCertificate.IsEmpty ||
                    !IsExactCertificateDer(authority.AuthorityCertificate))
                {
                    return false;
                }

                // Ticket authorities alone establish trust. The managed list is never promoted to anchors.
                trustedAnchors.Add(ByteString.From(authority.AuthorityCertificate.Span));
                for (int j = 0; j < authority.IssuerCertificates.Count; j++)
                {
                    ByteString encodedIssuer = authority.IssuerCertificates[j];
                    if (encodedIssuer.IsEmpty || !IsExactCertificateDer(encodedIssuer))
                    {
                        return false;
                    }
                    issuerMaterial.Add(ByteString.From(encodedIssuer.Span));
                }
            }

            if (trustedAnchors.Count == 0)
            {
                return false;
            }

            // These are borrowed by the caller. Copy their DER before disposing the owned TrustListData snapshot.
            using TrustListData trustList = await m_certificateManager.ReadTrustListAsync(
                DeviceIdentityAuthorities,
                TrustListMasks.All,
                cancellationToken).ConfigureAwait(false);

            foreach (Certificate issuer in issuers)
            {
                issuerMaterial.Add(ByteString.From(issuer.RawData));
            }
            foreach (Certificate certificate in trustList.TrustedCertificates)
            {
                issuerMaterial.Add(ByteString.From(certificate.RawData));
            }
            foreach (Certificate certificate in trustList.IssuerCertificates)
            {
                issuerMaterial.Add(ByteString.From(certificate.RawData));
            }

            var crls = new List<ByteString>();
            foreach (X509CRL crl in trustList.TrustedCrls)
            {
                crls.Add(ByteString.From(crl.RawData));
            }
            foreach (X509CRL crl in trustList.IssuerCrls)
            {
                crls.Add(ByteString.From(crl.RawData));
            }

            bool hasRevocationLocation =
                HasExtension(identity, "2.5.29.31") || // CRL Distribution Points
                HasExtension(identity, "1.3.6.1.5.5.7.1.1"); // Authority Information Access
            if (!hasRevocationLocation)
            {
                m_logger.IdentityRevocationCheckSkipped();
            }

            using var chain = new CertificateCollection { identity };
            var options = new ValidationOptions
            {
                TrustScope = new CertificateValidationScope(
                    trustedAnchors.ToArrayOf(),
                    issuerMaterial.ToArrayOf(),
                    crls.ToArrayOf()),
                AutoAcceptUntrustedCertificates = false,
                RejectSHA1SignedCertificates = true,
                RejectUnknownRevocationStatus = hasRevocationLocation,
                MinimumCertificateKeySize = 2048,
                AcceptError = static (_, _) => false,
                RecordRejectedCertificates = false
            };

            CertificateValidationResult result = await m_certificateManager.ValidateAsync(
                chain,
                DeviceIdentityAuthorities,
                options,
                cancellationToken).ConfigureAwait(false);
            return result.IsValid;
        }

        /// <summary>
        /// Checks that bytes are a canonical certificate DER encoding without retaining a certificate handle.
        /// </summary>
        private static bool IsExactCertificateDer(ByteString encoded)
        {
            try
            {
                using Certificate certificate = Certificate.FromRawData(encoded.ToArray());
                return certificate.RawData.AsSpan().SequenceEqual(encoded.Span);
            }
            catch (CryptographicException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks an X.509 extension by its object identifier.
        /// </summary>
        private static bool HasExtension(Certificate certificate, string oid)
        {
            foreach (System.Security.Cryptography.X509Certificates.X509Extension extension in
                certificate.Extensions)
            {
                if (string.Equals(extension.Oid?.Value, oid, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The borrowed manager owns all certificate stores and validation engines.
        /// </summary>
        private readonly ICertificateManager m_certificateManager;

        /// <summary>
        /// The logger records the narrowly permitted no-revocation-location skip.
        /// </summary>
        private readonly ILogger<OnboardingIdentityTrustProvider> m_logger;
    }

    /// <summary>
    /// Reports identity validation decisions without certificate or device identifiers.
    /// </summary>
    internal static partial class OnboardingIdentityTrustProviderLog
    {
        /// <summary>
        /// Records the permitted unknown-revocation result for identities without retrieval extensions.
        /// </summary>
        [LoggerMessage(
            EventId = GdsServerCommonEventIds.OnboardingIdentityTrustProvider + 0,
            Level = LogLevel.Warning,
            Message = "Identity has no CRL distribution point or authority information access extension; " +
                "unknown revocation status is allowed for this validation.")]
        public static partial void IdentityRevocationCheckSkipped(this ILogger logger);
    }
}
