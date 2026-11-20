// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: selects how a <c>RedundantServerSet</c> replicates shared state.
    /// </summary>
    public enum RedundancyConsistencyMode
    {
        /// <summary>
        /// Eventual consistency (AP): bulk replicated state is served by the leaderless, gossip-based
        /// <see cref="ReplicatedSharedKeyValueStore"/> and is <b>complemented</b> by a strongly-consistent Raft layer for the
        /// linearizable primitives that need it (compare-and-swap, change-feed, single-use nonces, lease/leader
        /// election). This is the default and preserves today's behaviour and performance for the common case.
        /// </summary>
        Eventual,

        /// <summary>
        /// Strong consistency (CP): all shared state is served by the linearizable
        /// <see cref="RaftSharedKeyValueStore"/>; no CRDT is used. Choose this when every replicated value must be
        /// linearizable at the cost of requiring a quorum for writes.
        /// </summary>
        Strong
    }
}
