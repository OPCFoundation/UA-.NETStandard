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

namespace Opc.Ua.Server.Distributed.Crdt
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
