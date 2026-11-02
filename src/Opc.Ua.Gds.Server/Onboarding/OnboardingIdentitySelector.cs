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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Matches device subjectAltName identities against trusted device and composite tickets.
    /// </summary>
    public sealed class OnboardingIdentitySelector : IOnboardingIdentitySelector
    {
        /// <summary>
        /// Creates a selector using managed tickets and the host's identity-validation policy.
        /// </summary>
        public OnboardingIdentitySelector(
            ITicketStore tickets,
            IOnboardingTicketValidator validator,
            IOnboardingIdentityTrustProvider trustProvider)
        {
            m_tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
            m_validator = validator ?? throw new ArgumentNullException(nameof(validator));
            m_trustProvider = trustProvider ?? throw new ArgumentNullException(nameof(trustProvider));
        }

        /// <inheritdoc/>
        public async ValueTask<OnboardingIdentitySelection> SelectAsync(
            ArrayOf<ByteString> identities,
            ArrayOf<ByteString> issuers,
            ArrayOf<string> tickets,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var identityCertificates = new CertificateCollection();
            var candidates = new List<IdentityCandidate>();
            for (int i = 0; i < identities.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (identities[i].IsEmpty)
                {
                    continue;
                }
                try
                {
                    using Certificate identity = Certificate.FromRawData(identities[i].ToArray());
                    if (!identity.RawData.AsSpan().SequenceEqual(identities[i].Span))
                    {
                        continue;
                    }
                    X509SubjectAltNameExtension? extension = identity.FindExtension<X509SubjectAltNameExtension>();
                    if (extension == null)
                    {
                        continue;
                    }
                    var uris = new HashSet<string>(extension.Uris, StringComparer.Ordinal);
                    uris.RemoveWhere(value => !Uri.TryCreate(value, UriKind.Absolute, out _));
                    if (uris.Count > 0)
                    {
                        identityCertificates.Add(identity);
                        candidates.Add(new IdentityCandidate(
                            identityCertificates[identityCertificates.Count - 1], uris));
                    }
                }
                catch (CryptographicException)
                {
                    // Part 21 permits ignoring an individual malformed identity.
                }
            }
            if (candidates.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }

            using var issuerCertificates = new CertificateCollection();
            for (int i = 0; i < issuers.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (issuers[i].IsEmpty)
                {
                    continue;
                }
                try
                {
                    using Certificate issuer = Certificate.FromRawData(issuers[i].ToArray());
                    if (issuer.RawData.AsSpan().SequenceEqual(issuers[i].Span))
                    {
                        issuerCertificates.Add(issuer);
                    }
                }
                catch (CryptographicException)
                {
                    // Unverifiable issuer entries do not invalidate independent chains.
                }
            }

            List<AcceptedTicket> accepted = await LoadTicketsAsync(tickets, cancellationToken).ConfigureAwait(false);
            var memberships = new Dictionary<string, (HashSet<string> Roots, bool IsMember)>(StringComparer.Ordinal);
            bool matchingProduct = false;
            foreach (AcceptedTicket ticket in accepted)
            {
                if (ticket.Payload is not DeviceIdentityTicketType device)
                {
                    continue;
                }
                foreach (IdentityCandidate candidate in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string productUri = device.ProductInstanceUri ??
                        throw new ServiceResultException(StatusCodes.BadTicketInvalid);
                    if (!candidate.Uris.Contains(productUri))
                    {
                        continue;
                    }
                    matchingProduct = true;
                    if (!memberships.TryGetValue(productUri, out var membership))
                    {
                        HashSet<string> roots = FindOutermostComposites(
                            productUri, accepted, cancellationToken, out bool belongsToComposite);
                        membership = (roots, belongsToComposite);
                        memberships.Add(productUri, membership);
                    }
                    AcceptedTicket? containingComposite = null;
                    if (membership.IsMember)
                    {
                        foreach (AcceptedTicket context in accepted)
                        {
                            if (context.Payload is not CompositeIdentityTicketType composite)
                            {
                                continue;
                            }
                            string compositeUri = composite.CompositeInstanceUri ??
                                throw new ServiceResultException(StatusCodes.BadTicketInvalid);
                            if (membership.Roots.Contains(compositeUri) &&
                                candidate.Uris.Contains(compositeUri) &&
                                (context.CompositeInstanceUri.Length == 0 ||
                                    candidate.Uris.Contains(context.CompositeInstanceUri)))
                            {
                                containingComposite = context;
                                break;
                            }
                        }
                        if (containingComposite == null)
                        {
                            continue;
                        }
                    }
                    if (ticket.CompositeInstanceUri.Length > 0 &&
                        !candidate.Uris.Contains(ticket.CompositeInstanceUri))
                    {
                        continue;
                    }
                    if (await m_trustProvider.IsTrustedAsync(
                        candidate.Certificate, issuerCertificates, device, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        return new OnboardingIdentitySelection(
                            ByteString.From(candidate.Certificate.RawData.AsSpan()),
                            device,
                            productUri,
                            ticket.Encoded,
                            containingComposite?.Encoded ?? string.Empty);
                    }
                }
            }
            throw new ServiceResultException(matchingProduct
                ? StatusCodes.BadCertificateInvalid
                : tickets.IsEmpty ? StatusCodes.BadTicketRequired : StatusCodes.BadNotFound);
        }

        /// <summary>
        /// Retains out-of-band precedence while checking all supplied composite constraints.
        /// </summary>
        private async ValueTask<List<AcceptedTicket>> LoadTicketsAsync(
            ArrayOf<string> tickets,
            CancellationToken cancellationToken)
        {
            var accepted = new List<AcceptedTicket>();
            await foreach (TicketRecord record in m_tickets.ListAsync(cancellationToken).ConfigureAwait(false))
            {
                string encoded;
                try
                {
                    encoded = s_utf8.GetString(record.EncodedTicket);
                }
                catch (DecoderFallbackException)
                {
                    // Corrupted persisted bytes cannot contribute an authentication ticket.
                    continue;
                }
                await AddTicketAsync(
                    encoded, OnboardingTicketSource.OutOfBand, accepted, cancellationToken).ConfigureAwait(false);
            }
            for (int i = 0; i < tickets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (tickets[i] != null)
                {
                    await AddTicketAsync(
                        tickets[i], OnboardingTicketSource.Device, accepted, cancellationToken).ConfigureAwait(false);
                }
            }
            return accepted;
        }

        /// <summary>
        /// Ignores invalid tickets, but propagates cancellation and provider failures.
        /// </summary>
        private async ValueTask AddTicketAsync(
            string encoded,
            OnboardingTicketSource source,
            List<AcceptedTicket> accepted,
            CancellationToken cancellationToken)
        {
            try
            {
                ValidatedOnboardingTicket ticket = await m_validator.ValidateAsync(
                    encoded, source, cancellationToken).ConfigureAwait(false);
                accepted.Add(new AcceptedTicket(ticket.Ticket, ticket.CompositeInstanceUri, encoded));
            }
            catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadTicketInvalid)
            {
                // Section 9.2.3 explicitly permits ignoring tickets that cannot be verified.
            }
        }

        /// <summary>
        /// Follows composite membership to its outermost roots without recursively traversing input.
        /// Cycles with no outermost root cannot authorize a product-only identity.
        /// </summary>
        private static HashSet<string> FindOutermostComposites(
            string productUri,
            List<AcceptedTicket> tickets,
            CancellationToken cancellationToken,
            out bool belongsToComposite)
        {
            belongsToComposite = false;
            var roots = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(productUri);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = pending.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }
                bool hasParent = false;
                foreach (AcceptedTicket ticket in tickets)
                {
                    if (ticket.Payload is not CompositeIdentityTicketType composite ||
                        (!Contains(composite.Devices, current) && !Contains(composite.Composites, current)))
                    {
                        continue;
                    }
                    belongsToComposite = true;
                    hasParent = true;
                    pending.Push(composite.CompositeInstanceUri ??
                        throw new ServiceResultException(StatusCodes.BadTicketInvalid));
                }
                if (!hasParent && !string.Equals(current, productUri, StringComparison.Ordinal))
                {
                    roots.Add(current);
                }
            }
            return roots;
        }

        /// <summary>
        /// Compares identity URIs without normalizing their case-sensitive values.
        /// </summary>
        private static bool Contains(ArrayOf<string> values, string value)
        {
            foreach (string candidate in values)
            {
                if (string.Equals(candidate, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A certificate and the absolute URI names it advertises.
        /// </summary>
        private sealed record IdentityCandidate(Certificate Certificate, HashSet<string> Uris);

        /// <summary>
        /// A validated payload and the composite bound by its accepted signature.
        /// </summary>
        private sealed record AcceptedTicket(BaseTicketType Payload, string CompositeInstanceUri, string Encoded);

        /// <summary>
        /// Strict decoding of the store's UTF-8 ticket strings.
        /// </summary>
        private static readonly UTF8Encoding s_utf8 = new(false, true);

        /// <summary>
        /// The administrator-managed out-of-band tickets.
        /// </summary>
        private readonly ITicketStore m_tickets;

        /// <summary>
        /// The envelope and ticket-authority validator.
        /// </summary>
        private readonly IOnboardingTicketValidator m_validator;

        /// <summary>
        /// The selected identity's authority and revocation policy.
        /// </summary>
        private readonly IOnboardingIdentityTrustProvider m_trustProvider;
    }
}
