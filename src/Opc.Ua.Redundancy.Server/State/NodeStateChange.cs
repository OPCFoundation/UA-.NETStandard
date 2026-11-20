// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: the kind of node-state change reported by an
    /// <see cref="INodeStateStore"/> change-feed.
    /// </summary>
    public enum NodeStateChangeKind
    {
        /// <summary>
        /// A node was created or its topology/attributes were updated.
        /// </summary>
        Upsert,

        /// <summary>
        /// A node was removed.
        /// </summary>
        Delete,

        /// <summary>
        /// A variable value (Value/StatusCode/Timestamp) changed.
        /// </summary>
        Value
    }

    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a single change observed on an <see cref="INodeStateStore"/>
    /// change-feed. Topology changes (<see cref="NodeStateChangeKind.Upsert"/>
    /// / <see cref="NodeStateChangeKind.Delete"/>) are applied live to every
    /// replica; <see cref="NodeStateChangeKind.Value"/> changes carry the
    /// new <see cref="Value"/>.
    /// </summary>
    public sealed record NodeStateChange
    {
        /// <summary>
        /// The kind of change.
        /// </summary>
        public NodeStateChangeKind Kind { get; init; }

        /// <summary>
        /// The affected node identifier.
        /// </summary>
        public NodeId NodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The serialized node for <see cref="NodeStateChangeKind.Upsert"/>
        /// changes; otherwise <c>null</c>.
        /// </summary>
        public IStoredNode? Node { get; init; }

        /// <summary>
        /// The new value for <see cref="NodeStateChangeKind.Value"/> changes;
        /// otherwise a null <see cref="DataValue"/>
        /// (<see cref="DataValue.IsNull"/>).
        /// </summary>
        public DataValue Value { get; init; } = DataValue.Null;

        /// <summary>
        /// The monotonic write sequence of this change, assigned by the single
        /// writer. Used to order and de-duplicate changes across the snapshot,
        /// delta-log, and live-feed apply paths. <c>0</c> when the backing store
        /// does not track sequences.
        /// </summary>
        public ulong Sequence { get; init; }
    }
}
