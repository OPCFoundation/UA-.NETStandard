/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Abstraction over the in-process node graph that a distributed
    /// address-space synchronizer reads from (outbound capture) and writes to
    /// (inbound apply). A node manager adapts its <c>PredefinedNodes</c>
    /// dictionary to this interface; tests use a dictionary-backed
    /// implementation.
    /// </summary>
    public interface ILocalAddressSpace
    {
        /// <summary>
        /// The system context used to (de)serialize nodes.
        /// </summary>
        ISystemContext Context { get; }

        /// <summary>
        /// A snapshot of the current top-level nodes.
        /// </summary>
        IEnumerable<NodeState> Nodes { get; }

        /// <summary>
        /// Raised after a node is added to the address space.
        /// </summary>
        event Action<NodeState>? NodeAdded;

        /// <summary>
        /// Raised after a node is removed from the address space.
        /// </summary>
        event Action<NodeId>? NodeRemoved;

        /// <summary>
        /// Looks up a node by identifier.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="node">The resolved node when found.</param>
        /// <returns><c>true</c> when the node exists.</returns>
        bool TryGetNode(NodeId nodeId, [NotNullWhen(true)] out NodeState? node);

        /// <summary>
        /// Adds or replaces a node, raising <see cref="NodeAdded"/>.
        /// </summary>
        /// <param name="node">The node to add or replace.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask AddOrUpdateNodeAsync(NodeState node, CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds or replaces many nodes as a single hydration batch.
        /// </summary>
        /// <remarks>
        /// Used only while a standby bulk-hydrates from a snapshot: it does
        /// <b>not</b> raise <see cref="NodeAdded"/> per node (a hydrating standby
        /// does not observe its own additions), so a very large address space can
        /// be materialized without one event and await per node.
        /// </remarks>
        /// <param name="nodes">The nodes to add or replace.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask AddOrUpdateRangeAsync(
            IEnumerable<NodeState> nodes,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a node, raising <see cref="NodeRemoved"/> when present.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns><c>true</c> when a node was removed.</returns>
        ValueTask<bool> RemoveNodeAsync(NodeId nodeId, CancellationToken cancellationToken = default);
    }
}
