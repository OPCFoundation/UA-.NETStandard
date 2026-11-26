// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Opc.Ua.Server.NodeManager
{
    /// <summary>
    /// Linear-scan + dictionary lookups over a node-manager's predefined
    /// node collection. Surfaces the patterns that node-manager
    /// subclasses repeatedly hand-roll against
    /// <c>CustomNodeManager.PredefinedNodes</c> /
    /// <c>AsyncCustomNodeManager.PredefinedNodes</c>:
    /// browse-name root lookup, NodeId lookup, TypeDefinitionId scan, and
    /// DataType scan.
    /// </summary>
    public static class NodeStateLookupExtensions
    {
        /// <summary>
        /// Returns the first <see cref="NodeState"/> in
        /// <paramref name="nodes"/> whose <see cref="NodeState.BrowseName"/>
        /// equals <paramref name="browseName"/>, or <see langword="null"/>
        /// when no match exists.
        /// </summary>
        /// <param name="nodes">
        /// The set to scan, typically
        /// <c>PredefinedNodes.Values</c> for a custom node manager.
        /// </param>
        /// <param name="browseName">Browse name to match.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="nodes"/> is <see langword="null"/>.
        /// </exception>
        public static NodeState? FindByBrowseName(
            this IEnumerable<NodeState> nodes,
            QualifiedName browseName)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            foreach (NodeState node in nodes)
            {
                if (node.BrowseName == browseName)
                {
                    return node;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the node indexed by <paramref name="nodeId"/>, or
        /// <see langword="null"/> when no entry exists. Convenience
        /// wrapper around <see cref="IDictionary{TKey,TValue}.TryGetValue"/>
        /// so call sites avoid the boilerplate.
        /// </summary>
        /// <param name="nodes">
        /// The dictionary to query, typically <c>PredefinedNodes</c> on
        /// a custom node manager (which is a
        /// <see cref="PredefinedNodeDictionary"/>).
        /// </param>
        /// <param name="nodeId">NodeId to look up.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="nodes"/> is <see langword="null"/>.
        /// </exception>
        public static NodeState? FindById(
            this IDictionary<NodeId, NodeState> nodes,
            NodeId nodeId)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            return nodes.TryGetValue(nodeId, out NodeState? node) ? node : null;
        }

        /// <summary>
        /// Returns every <see cref="BaseInstanceState"/> in
        /// <paramref name="nodes"/> whose
        /// <see cref="BaseInstanceState.TypeDefinitionId"/> equals
        /// <paramref name="typeDefinitionId"/>. The list is empty when
        /// no match exists; non-instance nodes are skipped.
        /// </summary>
        /// <param name="nodes">
        /// The set to scan, typically <c>PredefinedNodes.Values</c>.
        /// </param>
        /// <param name="typeDefinitionId">
        /// TypeDefinitionId to match (e.g. a known <c>ObjectType</c>).
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="nodes"/> is <see langword="null"/>.
        /// </exception>
        public static List<NodeState> FindByTypeDefinition(
            this IEnumerable<NodeState> nodes,
            NodeId typeDefinitionId)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            var results = new List<NodeState>();
            foreach (NodeState node in nodes)
            {
                if (node is BaseInstanceState instance &&
                    instance.TypeDefinitionId == typeDefinitionId)
                {
                    results.Add(node);
                }
            }
            return results;
        }

        /// <summary>
        /// Returns every <see cref="BaseVariableState"/> in
        /// <paramref name="nodes"/> whose
        /// <see cref="BaseVariableState.DataType"/> equals
        /// <paramref name="dataTypeId"/>. The list is empty when no match
        /// exists; non-variable nodes are skipped.
        /// </summary>
        /// <param name="nodes">
        /// The set to scan, typically <c>PredefinedNodes.Values</c>.
        /// </param>
        /// <param name="dataTypeId">
        /// DataType NodeId to match (e.g. a known <c>DataTypeIds</c>
        /// constant).
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="nodes"/> is <see langword="null"/>.
        /// </exception>
        public static ArrayOf<NodeState> FindByDataType(
            this IEnumerable<NodeState> nodes,
            NodeId dataTypeId)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            var results = new List<NodeState>();
            foreach (NodeState node in nodes)
            {
                if (node is BaseVariableState variable &&
                    variable.DataType == dataTypeId)
                {
                    results.Add(node);
                }
            }
            return new ArrayOf<NodeState>(results.ToArray());
        }
    }
}
