// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Storage abstraction for OPC 10000-21 Onboarding tickets.
    /// A ticket is the encoded
    /// identifier-and-vendor data block a device presents during
    /// provisioning; the registrar stores them and matches incoming
    /// device certificates against the bundle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the application-facing facade; the source-generated
    /// <c>BaseTicketType</c> / <c>DeviceIdentityTicketType</c> /
    /// <c>CompositeIdentityTicketType</c> proxies in
    /// <c>Opc.Ua.Onboarding</c> remain the wire-level encoding. The store
    /// persists the bytes carried by the specification's
    /// <c>EncodedTicket</c> String subtype, encoded as UTF-8 for storage.
    /// </para>
    /// <para>
    /// All implementations must be safe for concurrent calls.
    /// </para>
    /// </remarks>
    public interface ITicketStore
    {
        /// <summary>
        /// Adds a ticket to the store. Idempotent on the
        /// <paramref name="ticketId"/> key — calling twice with the
        /// same id replaces the stored payload.
        /// </summary>
        /// <param name="ticketId">
        /// Stable application-assigned identifier (e.g. the
        /// <c>SerialNumber</c> field, or a hash of the encoded
        /// ticket).
        /// </param>
        /// <param name="encodedTicket">
        /// Encoded ticket bytes carried on the wire as the
        /// <c>EncodedTicket</c> String subtype, encoded as UTF-8 for storage.
        /// </param>
        /// <param name="metadata">
        /// Application-supplied metadata for the ticket (kind,
        /// manufacturer, model, etc).
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask AddAsync(
            string ticketId,
            byte[] encodedTicket,
            TicketMetadata metadata,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes the ticket with the supplied id. Returns
        /// <see langword="true"/> when a ticket was removed.
        /// </summary>
        ValueTask<bool> RemoveAsync(
            string ticketId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the encoded ticket bytes and metadata for the
        /// supplied id, or <see langword="null"/> when no such
        /// ticket exists.
        /// </summary>
        ValueTask<TicketRecord?> GetAsync(
            string ticketId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Streams every ticket known to the store.
        /// </summary>
        IAsyncEnumerable<TicketRecord> ListAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the first ticket whose
        /// <see cref="TicketMetadata.ProductInstanceUri"/> matches
        /// <paramref name="productInstanceUri"/>, or
        /// <see langword="null"/> when no match exists. Used by
        /// device-registrar logic to locate the ticket for an
        /// incoming device.
        /// </summary>
        ValueTask<TicketRecord?> FindByProductInstanceUriAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Classifies a ticket as identifying a single device or a
    /// composite. Mirrors the
    /// <see cref="ITicketStore"/>'s
    /// view of the OPC 10000-100 ticket subtypes.
    /// </summary>
    public enum TicketKind
    {
        /// <summary>
        /// Default — kind is not declared.
        /// </summary>
        Unspecified = 0,

        /// <summary>
        /// Maps to <c>DeviceIdentityTicketType</c> — identifies a
        /// single device by <c>ProductInstanceUri</c>.
        /// </summary>
        DeviceIdentity = 1,

        /// <summary>
        /// Maps to <c>CompositeIdentityTicketType</c> — identifies a
        /// composite (collection) by <c>CompositeInstanceUri</c>.
        /// </summary>
        CompositeIdentity = 2
    }

    /// <summary>
    /// Application-supplied metadata describing a stored ticket.
    /// </summary>
    /// <param name="Kind">
    /// The ticket subtype.
    /// </param>
    /// <param name="ManufacturerName">
    /// From <c>BaseTicketType.ManufacturerName</c>.
    /// </param>
    /// <param name="ModelName">
    /// From <c>BaseTicketType.ModelName</c>.
    /// </param>
    /// <param name="SerialNumber">
    /// From <c>BaseTicketType.SerialNumber</c>.
    /// </param>
    /// <param name="ProductInstanceUri">
    /// From <c>DeviceIdentityTicketType.ProductInstanceUri</c> or
    /// <c>CompositeIdentityTicketType.CompositeInstanceUri</c>.
    /// </param>
    public sealed record TicketMetadata(
        TicketKind Kind,
        string ManufacturerName,
        string ModelName,
        string SerialNumber,
        string ProductInstanceUri);

    /// <summary>
    /// A ticket retrieved from the store: id, encoded payload, and
    /// metadata.
    /// </summary>
    public sealed record TicketRecord(
        string TicketId,
        byte[] EncodedTicket,
        TicketMetadata Metadata,
        DateTimeOffset CreatedAt);
}
