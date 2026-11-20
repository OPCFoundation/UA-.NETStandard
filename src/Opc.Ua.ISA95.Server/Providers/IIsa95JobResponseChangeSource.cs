// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Publishes job responses as the provider receives and drops them, so
    /// that a projection layer can keep the <c>JobOrderResponseList</c> that
    /// <see cref="IIsa95JobResponseCatalog"/> feeds current.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A response usually arrives without a life-cycle state change — a
    /// machine reports progress such as runs completed while the order keeps
    /// running — so neither <see cref="IIsa95JobStatusSourceV2"/> nor
    /// <see cref="IIsa95JobOrderCatalogChangeSource"/> sees it, and a list
    /// refreshed only from those two stays stale until the order next changes
    /// state.
    /// </para>
    /// <para>
    /// Each subscriber receives exactly one change per response received after
    /// it subscribes, and one per response the provider drops afterwards (for
    /// example once it outlived its retention); subscribers are independent
    /// and cancellation or disposal of one does not affect others.
    /// </para>
    /// </remarks>
    public interface IIsa95JobResponseChangeSource
    {
        /// <summary>
        /// Subscribes to received job responses.
        /// </summary>
        /// <param name="cancellationToken">
        /// A token that ends the subscription when cancelled.
        /// </param>
        /// <returns>
        /// An asynchronous stream of response changes.
        /// </returns>
        IAsyncEnumerable<Isa95JobResponseChange> SubscribeResponseChangesAsync(
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One job response a provider received or dropped.
    /// </summary>
    public readonly record struct Isa95JobResponseChange
    {
        /// <summary>
        /// Whether the provider dropped the response, rather than received it.
        /// </summary>
        public bool Removed { get; init; }

        /// <summary>
        /// The identifier of the received job response.
        /// </summary>
        public required string JobResponseId { get; init; }

        /// <summary>
        /// The identifier of the job order the response belongs to.
        /// </summary>
        public required string JobOrderId { get; init; }

        /// <summary>
        /// A monotonically increasing sequence number assigned to the change
        /// within the response-change stream.
        /// </summary>
        public required ulong SequenceNumber { get; init; }

        /// <summary>
        /// The time at which the response was received or dropped, as reported
        /// by the injected <see cref="System.TimeProvider"/>.
        /// </summary>
        public required DateTimeUtc Timestamp { get; init; }
    }
}
