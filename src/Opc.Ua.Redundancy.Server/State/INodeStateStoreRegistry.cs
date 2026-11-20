// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: server-wide registry of <see cref="INodeStateStore"/> instances,
    /// resolved per node with the same three-scope precedence as the
    /// historian registry: exact NodeId, then namespace, then a single
    /// default fallback.
    /// </summary>
    public interface INodeStateStoreRegistry
    {
        /// <summary>
        /// Adds a NodeId-scoped binding.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="store">The store to bind.</param>
        void RegisterForNode(NodeId nodeId, INodeStateStore store);

        /// <summary>
        /// Adds a namespace-scoped binding.
        /// </summary>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <param name="store">The store to bind.</param>
        void RegisterForNamespace(string namespaceUri, INodeStateStore store);

        /// <summary>
        /// Sets the default fallback store.
        /// </summary>
        /// <param name="store">The store to use as fallback.</param>
        void RegisterDefault(INodeStateStore store);

        /// <summary>
        /// Removes the NodeId-scoped binding (no-op when absent).
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns><c>true</c> when a binding was removed.</returns>
        bool UnregisterForNode(NodeId nodeId);

        /// <summary>
        /// Removes the namespace-scoped binding (no-op when absent).
        /// </summary>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <returns><c>true</c> when a binding was removed.</returns>
        bool UnregisterForNamespace(string namespaceUri);

        /// <summary>
        /// Clears the default store.
        /// </summary>
        void ClearDefault();

        /// <summary>
        /// Resolves the store for a node, or <c>null</c> when no binding
        /// matches.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        INodeStateStore? Resolve(NodeId nodeId);

        /// <summary>
        /// Returns a snapshot of every registered store.
        /// </summary>
        IReadOnlyCollection<INodeStateStore> Stores { get; }
    }
}
