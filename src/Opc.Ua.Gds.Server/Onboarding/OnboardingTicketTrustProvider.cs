// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Checks ticket signers against the certificate manager's TicketAuthorities trust list.
    /// </summary>
    public sealed class OnboardingTicketTrustProvider : IOnboardingTicketTrustProvider
    {
        /// <summary>
        /// Creates a provider which borrows the application's certificate manager.
        /// </summary>
        public OnboardingTicketTrustProvider(ICertificateManager certificateManager)
        {
            m_certificateManager = certificateManager ??
                throw new ArgumentNullException(nameof(certificateManager));
        }

        /// <summary>
        /// Gets the managed trust-list identifier for ticket authorities.
        /// </summary>
        public static TrustListIdentifier TicketAuthorities { get; } = new("TicketAuthorities");

        /// <inheritdoc/>
        public async ValueTask<bool> IsTrustedAsync(
            CertificateCollection certificates,
            OnboardingTicketSource source,
            CancellationToken ct = default)
        {
            if (certificates == null)
            {
                throw new ArgumentNullException(nameof(certificates));
            }
            if (source is not OnboardingTicketSource.OutOfBand and not OnboardingTicketSource.Device)
            {
                throw new ArgumentOutOfRangeException(nameof(source));
            }
            ct.ThrowIfCancellationRequested();
            if (certificates.Count == 0)
            {
                return false;
            }
            if (source == OnboardingTicketSource.Device)
            {
                using TrustListData trustList = await m_certificateManager.ReadTrustListAsync(
                    TicketAuthorities, TrustListMasks.TrustedCertificates, ct).ConfigureAwait(false);
                bool explicitlyTrusted = false;
                foreach (Certificate trusted in trustList.TrustedCertificates)
                {
                    if (trusted.RawData.AsSpan().SequenceEqual(certificates[0].RawData))
                    {
                        explicitlyTrusted = true;
                        break;
                    }
                }
                if (!explicitlyTrusted)
                {
                    return false;
                }
            }
            CertificateValidationResult result = await m_certificateManager.ValidateAsync(
                certificates,
                TicketAuthorities,
                new ValidationOptions
                {
                    ValidateForSigning = true,
                    AutoAcceptUntrustedCertificates = false,
                    RejectSHA1SignedCertificates = true,
                    RejectUnknownRevocationStatus = true,
                    MinimumCertificateKeySize = 2048,
                    AcceptError = static (_, _) => false
                },
                ct).ConfigureAwait(false);
            return result.IsValid;
        }

        /// <summary>
        /// The borrowed manager owns all certificate stores and validation engines.
        /// </summary>
        private readonly ICertificateManager m_certificateManager;
    }
}
