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
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

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
