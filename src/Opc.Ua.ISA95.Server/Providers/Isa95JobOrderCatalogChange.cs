// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// The kind of change applied to a job order in the catalog projected onto the
    /// <c>JobOrderList</c>. Job order additions and life-cycle state changes are
    /// carried by the status source (<see cref="IIsa95JobStatusSourceV2"/>); this
    /// enumeration only describes the catalog mutations for which an OPC UA status
    /// event would not be a semantically valid representation.
    /// </summary>
    public enum Isa95JobOrderCatalogChangeKind
    {
        /// <summary>
        /// The stored job order content was updated without a life-cycle state
        /// change (the Job Control V2 <c>Update</c> operation).
        /// </summary>
        Updated,

        /// <summary>
        /// The job order was removed from the catalog (the V2 <c>Cancel</c> and
        /// <c>Clear</c> operations and the V1 <c>Stop</c> command).
        /// </summary>
        Removed
    }

    /// <summary>
    /// A single committed change to the job order catalog that projects onto the
    /// server's <c>JobOrderList</c> but is not a life-cycle state change. It exists
    /// so that a projection layer can refresh the list without a stale read and
    /// without misrepresenting the mutation as an OPC UA status event.
    /// </summary>
    public readonly record struct Isa95JobOrderCatalogChange
    {
        /// <summary>
        /// The identifier of the job order that changed.
        /// </summary>
        public required string JobOrderId { get; init; }

        /// <summary>
        /// The kind of catalog change.
        /// </summary>
        public required Isa95JobOrderCatalogChangeKind Kind { get; init; }

        /// <summary>
        /// The job order and its current state after the change for
        /// <see cref="Isa95JobOrderCatalogChangeKind.Updated"/>, or <c>null</c> for
        /// <see cref="Isa95JobOrderCatalogChangeKind.Removed"/>.
        /// </summary>
        public V2.ISA95JobOrderAndStateDataType? Order { get; init; }

        /// <summary>
        /// A monotonically increasing sequence number assigned to the change
        /// within the catalog-change stream.
        /// </summary>
        public required ulong SequenceNumber { get; init; }

        /// <summary>
        /// The time at which the change was committed, as reported by the injected
        /// <see cref="System.TimeProvider"/>.
        /// </summary>
        public required DateTimeUtc Timestamp { get; init; }
    }
}
