/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: options for selecting the consistency model of the shared store used by a
    /// <c>RedundantServerSet</c> (<see cref="RedundancyConsistencyBuilderExtensions"/>).
    /// </summary>
    public sealed class RedundancyConsistencyOptions
    {
        /// <summary>
        /// Gets or sets the consistency model. <see cref="RedundancyConsistencyMode.Eventual"/> (the default) serves
        /// bulk state from a CRDT and complements it with Raft for the linearizable primitives;
        /// <see cref="RedundancyConsistencyMode.Strong"/> serves all state from Raft.
        /// </summary>
        public RedundancyConsistencyMode Mode { get; set; } = RedundancyConsistencyMode.Eventual;

        /// <summary>
        /// Gets or sets this replica's Raft node id. Ignored when
        /// <see cref="RaftConsensusFactory"/> supplies the consensus replica.
        /// </summary>
        public ulong NodeId { get; set; } = 1;

        /// <summary>
        /// Gets or sets a factory that creates the Raft consensus replica. When <c>null</c>, a single-node
        /// <see cref="InProcessRaftConsensus"/> is used (suitable for single-process deployments and tests; the
        /// external multi-node Raft engine plugs in here).
        /// </summary>
        public Func<IServiceProvider, IRaftConsensus>? RaftConsensusFactory { get; set; }

        /// <summary>
        /// Gets or sets a factory that creates the eventually-consistent bulk store used in
        /// <see cref="RedundancyConsistencyMode.Eventual"/> mode (typically a <see cref="ReplicatedSharedKeyValueStore"/>).
        /// When <c>null</c>, a singleton <see cref="InMemorySharedKeyValueStore"/> is used.
        /// </summary>
        public Func<IServiceProvider, ISharedKeyValueStore>? BulkStoreFactory { get; set; }

        /// <summary>
        /// Gets or sets the key prefixes routed to the linearizable Raft store in
        /// <see cref="RedundancyConsistencyMode.Eventual"/> mode. When empty, the defaults
        /// (<c>nonce/</c>, <c>lease/</c>, <c>election/</c>) are used.
        /// </summary>
        public ArrayOf<string> StrongKeyPrefixes { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the <see cref="ILeaderElection"/> is registered as a native
        /// <see cref="RaftLeaderElection"/> (the default). Set to <c>false</c> to keep a separately-registered
        /// election (for example the lease-based <see cref="SharedStoreLeaseElection"/>).
        /// </summary>
        public bool UseRaftLeaderElection { get; set; } = true;

        /// <summary>
        /// Gets or sets the number of applied commands after which the Raft store snapshots its state into the
        /// consensus engine, which then discards the log behind the snapshot. Zero (the default) never compacts.
        /// A positive value needs a consensus that implements <see cref="IRaftSnapshotConsensus"/>, such as
        /// <see cref="DefaultRaftConsensus"/>.
        /// </summary>
        public int RaftCompactionInterval { get; set; }
    }
}
