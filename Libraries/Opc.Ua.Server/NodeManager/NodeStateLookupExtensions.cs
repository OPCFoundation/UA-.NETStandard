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
using System.Collections.Generic;

namespace Opc.Ua.Server.NodeManager
{
    /// <summary>
    /// Linear-scan + dictionary lookups over a node-manager's predefined
    /// node collection. Surfaces the three patterns that node-manager
    /// subclasses repeatedly hand-roll against
    /// <c>CustomNodeManager.PredefinedNodes</c> /
    /// <c>AsyncCustomNodeManager.PredefinedNodes</c>:
    /// browse-name root lookup, NodeId lookup, and TypeDefinitionId scan.
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
            if (nodes == null) { throw new ArgumentNullException(nameof(nodes)); }

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
        /// <see cref="NodeIdDictionary{T}"/>).
        /// </param>
        /// <param name="nodeId">NodeId to look up.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="nodes"/> is <see langword="null"/>.
        /// </exception>
        public static NodeState? FindById(
            this IDictionary<NodeId, NodeState> nodes,
            NodeId nodeId)
        {
            if (nodes == null) { throw new ArgumentNullException(nameof(nodes)); }

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
            if (nodes == null) { throw new ArgumentNullException(nameof(nodes)); }

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
    }
}
