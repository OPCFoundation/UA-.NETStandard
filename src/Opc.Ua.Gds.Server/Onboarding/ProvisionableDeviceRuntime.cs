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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Persists configured registrar descriptions without granting trust to their certificates.
    /// Implementations serialize candidate changes. The device security provider owns installed-trust constraints.
    /// </summary>
    public interface IProvisionableDeviceRegistrarStore
    {
        /// <summary>
        /// Returns detached persisted discovery candidates without granting certificate trust.
        /// Missing configuration returns an empty collection; unreadable or corrupt state must be reported.
        /// </summary>
        ValueTask<ArrayOf<ApplicationDescription>> ReadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Replaces the configured discovery candidates, including clearing them with an empty collection.
        /// Returns only after publication succeeds; a failure must preserve the preceding configuration.
        /// </summary>
        ValueTask ReplaceAsync(
            ArrayOf<ApplicationDescription> registrars,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implements Part 21 ticket retrieval and registrar configuration independently of Resources placement.
    /// </summary>
    public interface IProvisionableDeviceRuntime
    {
        /// <summary>
        /// Gets whether the DCA and operational server are the same application.
        /// </summary>
        bool IsSingleton { get; }

        /// <summary>
        /// Returns only the device's explicitly configured signed tickets without changing their envelopes.
        /// </summary>
        ValueTask<ArrayOf<string>> RequestTicketsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Requires SecurityAdmin on an encrypted authenticated channel before persisting registrar candidates.
        /// Configuring a candidate does not approve it as the device's registrar.
        /// </summary>
        ValueTask SetRegistrarEndpointsAsync(
            ISystemContext context,
            ArrayOf<ApplicationDescription> registrars,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Uses the existing ticket store and validation engine, with host-owned persistent registrar configuration.
    /// </summary>
    public sealed class ProvisionableDeviceRuntime : IProvisionableDeviceRuntime
    {
        /// <summary>
        /// Creates a runtime with a detached list of this device's ticket identifiers.
        /// A shared ticket store is not enumerated, so unrelated devices' tickets cannot be disclosed.
        /// When supplied, the security provider gates registrar changes against pending ownership and installed trust.
        /// </summary>
        public ProvisionableDeviceRuntime(
            bool isSingleton,
            ArrayOf<string> ticketIds,
            ITicketStore tickets,
            IOnboardingTicketValidator validator,
            IProvisionableDeviceRegistrarStore registrars,
            IProvisionableDeviceSecurity? security = null)
        {
            m_tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
            m_validator = validator ?? throw new ArgumentNullException(nameof(validator));
            m_registrars = registrars ?? throw new ArgumentNullException(nameof(registrars));
            m_security = security;
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ticketIds)
            {
                if (string.IsNullOrWhiteSpace(id) || !identifiers.Add(id))
                {
                    throw new ArgumentException("Ticket identifiers must be nonempty and unique.", nameof(ticketIds));
                }
            }
            m_ticketIds = ticketIds.ToArray().ToArrayOf();
            IsSingleton = isSingleton;
        }

        /// <inheritdoc/>
        public bool IsSingleton { get; }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<string>> RequestTicketsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var encoded = new string[m_ticketIds.Count];
            for (int i = 0; i < m_ticketIds.Count; i++)
            {
                TicketRecord? record = await m_tickets.GetAsync(
                    m_ticketIds[i], cancellationToken).ConfigureAwait(false);
                if (record == null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "A configured device ticket is missing.");
                }
                string ticket;
                try
                {
                    ticket = s_utf8.GetString(record.EncodedTicket);
                }
                catch (DecoderFallbackException error)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTicketInvalid, "A configured device ticket is not UTF-8.", error);
                }
                // This is a local integrity check, not the remote registrar's source-specific trust decision.
                await m_validator.ValidateAsync(ticket, OnboardingTicketSource.OutOfBand, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                encoded[i] = ticket;
            }
            return encoded;
        }

        /// <inheritdoc/>
        public async ValueTask SetRegistrarEndpointsAsync(
            ISystemContext context,
            ArrayOf<ApplicationDescription> registrars,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            cancellationToken.ThrowIfCancellationRequested();
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);
            AuthorizationHelper.GetClientCertificateFingerprint(context);
            if (context is not ISessionSystemContext { SessionId.IsNull: false, UserIdentity: not null })
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            AuthorizationHelper.HasAuthorization(context, [Role.SecurityAdmin]);
            ArrayOf<ApplicationDescription> detached = SnapshotRegistrars(registrars);
            if (m_security is IProvisionableDeviceRegistrarConfiguration configuration)
            {
                await configuration.ReplaceRegistrarEndpointsAsync(context, m_registrars, detached, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                if (m_security != null)
                {
                    await m_security.RequireRegistrarAsync(context, cancellationToken).ConfigureAwait(false);
                }
                await m_registrars.ReplaceAsync(detached, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Validates and detaches discovery candidates without conferring certificate trust.
        /// </summary>
        internal static ArrayOf<ApplicationDescription> SnapshotRegistrars(ArrayOf<ApplicationDescription> registrars)
        {
            var detached = new ApplicationDescription[registrars.Count];
            for (int i = 0; i < registrars.Count; i++)
            {
                ApplicationDescription registrar = registrars[i];
                if (registrar == null ||
                    !Uri.TryCreate(registrar.ApplicationUri, UriKind.Absolute, out _) ||
                    (registrar.ApplicationType != ApplicationType.Server &&
                        registrar.ApplicationType != ApplicationType.ClientAndServer) ||
                    registrar.DiscoveryUrls.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                foreach (string endpoint in registrar.DiscoveryUrls)
                {
                    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                    }
                }
                detached[i] = new ApplicationDescription
                {
                    ApplicationUri = registrar.ApplicationUri,
                    ProductUri = registrar.ProductUri,
                    ApplicationName = registrar.ApplicationName,
                    ApplicationType = registrar.ApplicationType,
                    GatewayServerUri = registrar.GatewayServerUri,
                    DiscoveryProfileUri = registrar.DiscoveryProfileUri,
                    DiscoveryUrls = registrar.DiscoveryUrls.ToArray().ToArrayOf()
                };
            }
            return detached;
        }

        /// <summary>The explicitly selected device ticket identifiers.</summary>
        private readonly ArrayOf<string> m_ticketIds;

        /// <summary>The borrowed shared ticket store.</summary>
        private readonly ITicketStore m_tickets;

        /// <summary>The borrowed local ticket integrity policy.</summary>
        private readonly IOnboardingTicketValidator m_validator;

        /// <summary>The host's persistent registrar configuration.</summary>
        private readonly IProvisionableDeviceRegistrarStore m_registrars;

        /// <summary>The optional host-selected device trust and reset gate.</summary>
        private readonly IProvisionableDeviceSecurity? m_security;

        /// <summary>The strict decoder preserves signed envelopes without replacement characters.</summary>
        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }
}
