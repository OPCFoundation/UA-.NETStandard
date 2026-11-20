/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: options for CRDT active/active session replication. Mirrored session
    /// entries are gossiped between replicas as a CRDT; the gossip configuration
    /// is inherited from <see cref="ReplicatedGossipOptions"/>.
    /// </summary>
    /// <remarks>
    /// The single-use server nonce is <b>not</b> replicated as a CRDT — it
    /// requires a strongly-consistent compare-and-swap that CRDTs cannot
    /// provide. The session manager keeps the nonce on a separate
    /// strongly-consistent <see cref="ISharedKeyValueStore"/> resolved from the
    /// container (defaulting to an in-process store for development).
    /// CRDT session entries themselves require an <see cref="IRecordProtector"/>
    /// because they are gossiped and contain session nonces and secret material.
    /// Startup fails closed unless a protector is registered; use
    /// <c>GossipTlsOptions</c> separately to protect the gossip transport.
    /// </remarks>
    public sealed class ReplicatedSessionOptions : ReplicatedGossipOptions
    {
        /// <summary>
        /// Gets the distributed session behavior (fast-reconnect opt-in). The
        /// safe default re-authenticates on failover.
        /// </summary>
        public DistributedSessionOptions Session { get; } = new();
    }
}
