/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#nullable enable

using System;
using Opc.Ua.Redundancy;

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
        /// <see cref="RedundancyConsistencyMode.Eventual"/> mode (typically a <see cref="CrdtSharedKeyValueStore"/>).
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
    }
}
