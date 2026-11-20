// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Tracks the OPC UA applications that have been onboarded onto
    /// the server. Each managed application corresponds to a device
    /// or composite that the registrar has accepted; it is identified
    /// by its <c>ProductInstanceUri</c> and is materialised as a
    /// stable <see cref="NodeId"/> for client-side correlation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations must be safe for concurrent calls.
    /// </para>
    /// </remarks>
    public interface IManagedApplicationRegistry
    {
        /// <summary>
        /// Registers an application identified by
        /// <paramref name="productInstanceUri"/>. If an application
        /// with the same URI is already registered, the existing
        /// record is replaced and its NodeId is preserved.
        /// </summary>
        /// <param name="productInstanceUri">
        /// The stable product-instance URI from the device ticket.
        /// </param>
        /// <param name="certificate">
        /// The device's onboarding certificate as a DER-encoded
        /// byte array.
        /// </param>
        /// <param name="ticket">
        /// The ticket record this registration was matched against.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// The NodeId assigned to the managed application. Stable
        /// across re-registrations of the same URI.
        /// </returns>
        ValueTask<NodeId> RegisterAsync(
            string productInstanceUri,
            byte[] certificate,
            TicketRecord ticket,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Unregisters the application with the supplied NodeId.
        /// Returns <see langword="true"/> when an application was
        /// removed.
        /// </summary>
        ValueTask<bool> UnregisterAsync(
            NodeId applicationNodeId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the NodeId of the application with the supplied
        /// <paramref name="productInstanceUri"/>, or
        /// <see langword="null"/> when no such application exists.
        /// </summary>
        ValueTask<NodeId?> FindAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Streams every managed application.
        /// </summary>
        IAsyncEnumerable<ManagedApplication> ListAsync(
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A managed-application record returned by
    /// <see cref="IManagedApplicationRegistry.ListAsync"/>.
    /// </summary>
    public sealed record ManagedApplication(
        NodeId NodeId,
        string ProductInstanceUri,
        byte[] Certificate,
        TicketRecord Ticket);
}
