// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: optional fast-hydration capability layered on an
    /// <see cref="INodeStateStore"/>. A standby hydrates from one versioned
    /// snapshot (bulk read) plus the bounded delta log of changes that occurred
    /// after the snapshot, instead of streaming and applying every node one at a
    /// time — cutting time-to-ready on startup and failover promotion for very
    /// large address spaces.
    /// </summary>
    /// <remarks>
    /// The capability is optional: an <see cref="INodeStateStore"/> that does not
    /// also implement this interface is hydrated with the streaming
    /// <see cref="INodeStateStore.EnumerateAsync"/> /
    /// <see cref="INodeStateStore.EnumerateValuesAsync"/> path. Every change and
    /// snapshot entry carries a single-writer monotonic
    /// <see cref="NodeStateChange.Sequence"/> so the snapshot, delta log, and
    /// live feed can be applied idempotently and in order.
    /// </remarks>
    public interface INodeStateSnapshotStore
    {
        /// <summary>
        /// The highest write sequence assigned or observed by this store. A
        /// promoted writer continues assigning sequences from this high-water
        /// mark so they never move backward across a failover.
        /// </summary>
        ulong CurrentSequence { get; }

        /// <summary>
        /// Raises the sequence high-water mark to at least
        /// <paramref name="sequence"/>. Called by a standby while hydrating so a
        /// later promotion keeps sequences strictly increasing.
        /// </summary>
        /// <param name="sequence">The observed sequence.</param>
        void ObserveSequence(ulong sequence);

        /// <summary>
        /// Builds and atomically publishes a point-in-time snapshot of the
        /// current node and value state, then trims the delta log up to the
        /// snapshot sequence. Called by the writer (leader) off the hot path.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        ValueTask WriteSnapshotAsync(CancellationToken ct = default);

        /// <summary>
        /// Reads the currently published snapshot, or <c>null</c> when none has
        /// been published yet (in which case the caller falls back to the
        /// streaming hydration path).
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// The snapshot sequence and a deferred stream of its entries, or
        /// <c>null</c>.
        /// </returns>
        ValueTask<NodeStateSnapshot?> TryReadSnapshotAsync(CancellationToken ct = default);

        /// <summary>
        /// Streams delta-log entries with a sequence greater than
        /// <paramref name="fromSequenceExclusive"/>, in ascending sequence order,
        /// so a standby can replay the changes that occurred after a snapshot.
        /// </summary>
        /// <param name="fromSequenceExclusive">The exclusive lower-bound sequence.</param>
        /// <param name="ct">Cancellation token.</param>
        IAsyncEnumerable<NodeStateChange> ReadDeltaLogAsync(
            ulong fromSequenceExclusive,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a published address-space snapshot — the sequence it was
    /// taken at and a deferred stream of its entries (node upserts and variable
    /// values, each carrying its own <see cref="NodeStateChange.Sequence"/>).
    /// </summary>
    public sealed class NodeStateSnapshot
    {
        /// <summary>
        /// Creates a snapshot handle.
        /// </summary>
        /// <param name="sequence">The sequence the snapshot was taken at.</param>
        /// <param name="entries">A deferred stream of the snapshot entries.</param>
        public NodeStateSnapshot(ulong sequence, IAsyncEnumerable<NodeStateChange> entries)
        {
            Sequence = sequence;
            Entries = entries;
        }

        /// <summary>
        /// The write sequence the snapshot includes up to (inclusive). Delta-log
        /// entries with a greater sequence must be replayed on top.
        /// </summary>
        public ulong Sequence { get; }

        /// <summary>
        /// The snapshot entries as a deferred asynchronous stream.
        /// </summary>
        public IAsyncEnumerable<NodeStateChange> Entries { get; }
    }
}
