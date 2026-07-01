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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redundancy;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: authoritative, shareable store of a node manager's address-space
    /// state — node topology and variable values — used to replicate state
    /// across server replicas for high availability.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The local <c>NodeState</c> graph remains the in-process serving
    /// cache; this store is the durable/replicated copy. The
    /// <c>AddressSpaceSynchronizer</c> bridges the two: outbound
    /// write-through of committed local mutations and inbound live apply of
    /// topology changes from other replicas.
    /// </para>
    /// <para>
    /// The default implementation
    /// (<see cref="InMemoryNodeStateStore"/>) is layered on an
    /// <see cref="ISharedKeyValueStore"/>; Redis / CRDT backends implement
    /// the same contract.
    /// </para>
    /// </remarks>
    public interface INodeStateStore
    {
        /// <summary>
        /// Creates or replaces the stored node.
        /// </summary>
        /// <param name="node">The serialized node.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask UpsertNodeAsync(IStoredNode node, CancellationToken ct = default);

        /// <summary>
        /// Removes the stored node.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>true</c> when a node was removed.</returns>
        ValueTask<bool> DeleteNodeAsync(NodeId nodeId, CancellationToken ct = default);

        /// <summary>
        /// Reads the stored node, or <c>null</c> when absent.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<IStoredNode?> TryGetNodeAsync(NodeId nodeId, CancellationToken ct = default);

        /// <summary>
        /// Enumerates every stored node (used for hydration).
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        IAsyncEnumerable<IStoredNode> EnumerateAsync(CancellationToken ct = default);

        /// <summary>
        /// Writes the current value of a variable node.
        /// </summary>
        /// <param name="nodeId">The variable node identifier.</param>
        /// <param name="value">The value to store.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask WriteValueAsync(NodeId nodeId, in DataValue value, CancellationToken ct = default);

        /// <summary>
        /// Reads the last stored value of a variable node.
        /// </summary>
        /// <param name="nodeId">The variable node identifier.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>Found = true</c> and the stored value when present; otherwise
        /// <c>Found = false</c> and a null <see cref="DataValue"/>.
        /// </returns>
        ValueTask<(bool Found, DataValue Value)> TryReadValueAsync(NodeId nodeId, CancellationToken ct = default);

        /// <summary>
        /// Streams every stored variable value in a single pass (used for hydration).
        /// </summary>
        /// <remarks>
        /// A standby hydrates in two streamed passes — <see cref="EnumerateAsync"/>
        /// for topology, then this for the latest values — instead of one value
        /// read per node, so hydrating a large address space costs a bounded number
        /// of round trips against a networked backend rather than one per variable.
        /// </remarks>
        /// <param name="ct">Cancellation token.</param>
        IAsyncEnumerable<(NodeId NodeId, DataValue Value)> EnumerateValuesAsync(
            CancellationToken ct = default);

        /// <summary>
        /// Streams topology and value changes until <paramref name="ct"/> is
        /// cancelled. Only changes that occur after the call are observed.
        /// </summary>
        /// <param name="ct">Cancellation token that stops the subscription.</param>
        IAsyncEnumerable<NodeStateChange> SubscribeChangesAsync(CancellationToken ct = default);
    }
}
