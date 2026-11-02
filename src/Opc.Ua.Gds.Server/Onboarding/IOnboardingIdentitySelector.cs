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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Selects a device identity using validated tickets and their identity authorities.
    /// </summary>
    public interface IOnboardingIdentitySelector
    {
        /// <summary>
        /// Selects an identity according to OPC 10000-21 sections 5.3 and 7.1.
        /// Selection alone does not establish possession of the selected private key.
        /// </summary>
        ValueTask<OnboardingIdentitySelection> SelectAsync(
            ArrayOf<ByteString> identities,
            ArrayOf<ByteString> issuers,
            ArrayOf<string> tickets,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Validates device certificates against the authorities in an accepted ticket.
    /// </summary>
    public interface IOnboardingIdentityTrustProvider
    {
        /// <summary>
        /// Checks certificate validity, authority chains, application usage and revocation.
        /// All arguments are borrowed for the duration of this call.
        /// </summary>
        ValueTask<bool> IsTrustedAsync(
            Certificate identity,
            CertificateCollection issuers,
            BaseTicketType matchingTicket,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Carries a selected certificate and its independently owned matching ticket.
    /// </summary>
    public sealed class OnboardingIdentitySelection
    {
        /// <summary>
        /// Creates a selection after identity, ticket and authority validation.
        /// </summary>
        public OnboardingIdentitySelection(
            ByteString selectedIdentity,
            BaseTicketType matchingTicket,
            string productInstanceUri)
            : this(selectedIdentity, matchingTicket, productInstanceUri, string.Empty, string.Empty)
        {
        }

        /// <summary>
        /// Creates a selection retaining the exact signed envelopes required by the onboarding audit event.
        /// </summary>
        public OnboardingIdentitySelection(
            ByteString selectedIdentity,
            BaseTicketType matchingTicket,
            string productInstanceUri,
            string encodedTicket,
            string encodedComposite)
        {
            if (selectedIdentity.IsEmpty)
            {
                throw new ArgumentException("A selected identity certificate is required.", nameof(selectedIdentity));
            }
            if (matchingTicket == null)
            {
                throw new ArgumentNullException(nameof(matchingTicket));
            }
            if (string.IsNullOrWhiteSpace(productInstanceUri) ||
                !Uri.TryCreate(productInstanceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException(
                    "An absolute product instance URI is required.", nameof(productInstanceUri));
            }
            m_selectedIdentity = ByteString.From(selectedIdentity.Span);
            m_matchingTicket = (BaseTicketType)matchingTicket.Clone();
            ProductInstanceUri = productInstanceUri;
            EncodedTicket = encodedTicket ?? throw new ArgumentNullException(nameof(encodedTicket));
            EncodedComposite = encodedComposite ?? throw new ArgumentNullException(nameof(encodedComposite));
        }

        /// <summary>
        /// Gets an independent copy of the selected DER certificate.
        /// </summary>
        public ByteString SelectedIdentity => ByteString.From(m_selectedIdentity.Span);

        /// <summary>
        /// Gets an independent copy of the matching ticket.
        /// </summary>
        public BaseTicketType MatchingTicket => (BaseTicketType)m_matchingTicket.Clone();

        /// <summary>
        /// Gets the product URI matched in the certificate's subjectAltName extension.
        /// </summary>
        public string ProductInstanceUri { get; }

        /// <summary>
        /// Gets the exact signed ticket envelope accepted for the selected identity.
        /// </summary>
        public string EncodedTicket { get; }

        /// <summary>
        /// Gets the signed containing composite ticket, or an empty string for a standalone device.
        /// </summary>
        public string EncodedComposite { get; }

        /// <summary>
        /// The independently owned selected certificate bytes.
        /// </summary>
        private readonly ByteString m_selectedIdentity;

        /// <summary>
        /// The independently owned matching ticket.
        /// </summary>
        private readonly BaseTicketType m_matchingTicket;
    }
}
