// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Identifies how a ticket reached the registrar.
    /// </summary>
    public enum OnboardingTicketSource
    {
        /// <summary>
        /// An administrator supplied the ticket independently of the device.
        /// </summary>
        OutOfBand,

        /// <summary>
        /// The device supplied the ticket during identity selection.
        /// </summary>
        Device
    }

    /// <summary>
    /// Validates a ticket authority against managed trust lists.
    /// </summary>
    public interface IOnboardingTicketTrustProvider
    {
        /// <summary>
        /// Checks certificate trust, validity, signing usage and revocation.
        /// Device-supplied tickets require explicit trust in the signer itself;
        /// out-of-band tickets may instead trust an issuing authority.
        /// The certificate collection is borrowed for this call.
        /// </summary>
        ValueTask<bool> IsTrustedAsync(
            CertificateCollection certificates,
            OnboardingTicketSource source,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Decodes a Part 21 ticket only after verifying a trusted signature.
    /// </summary>
    public interface IOnboardingTicketValidator
    {
        /// <summary>
        /// Validates the general JWS serialization and reversible JSON payload.
        /// Throws BadTicketInvalid when no trusted signature validates the ticket.
        /// </summary>
        ValueTask<ValidatedOnboardingTicket> ValidateAsync(
            string encodedTicket,
            OnboardingTicketSource source,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Carries a verified ticket and the composite identity bound by its signature.
    /// </summary>
    public sealed class ValidatedOnboardingTicket
    {
        /// <summary>
        /// Creates a result after envelope and authority validation.
        /// </summary>
        internal ValidatedOnboardingTicket(BaseTicketType ticket, string compositeInstanceUri)
        {
            m_ticket = ticket;
            CompositeInstanceUri = compositeInstanceUri;
        }

        /// <summary>
        /// Gets an independent copy of the validated payload.
        /// </summary>
        public BaseTicketType Ticket => (BaseTicketType)m_ticket.Clone();

        /// <summary>
        /// Gets the composite URI from the accepted protected header, or an empty string.
        /// </summary>
        public string CompositeInstanceUri { get; }

        /// <summary>
        /// Gets metadata suitable for indexing the accepted ticket.
        /// </summary>
        public TicketMetadata Metadata => new(
            m_ticket is DeviceIdentityTicketType ? TicketKind.DeviceIdentity : TicketKind.CompositeIdentity,
            m_ticket.ManufacturerName ??
                throw new ServiceResultException(StatusCodes.BadTicketInvalid),
            m_ticket.ModelName ?? string.Empty,
            m_ticket.SerialNumber ?? string.Empty,
            m_ticket switch
            {
                DeviceIdentityTicketType device => device.ProductInstanceUri,
                CompositeIdentityTicketType composite => composite.CompositeInstanceUri,
                _ => string.Empty
            } ?? throw new ServiceResultException(StatusCodes.BadTicketInvalid));

        /// <summary>
        /// The independently owned validated payload.
        /// </summary>
        private readonly BaseTicketType m_ticket;
    }
}
