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
