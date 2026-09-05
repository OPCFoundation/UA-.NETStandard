/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Collections.Generic;

namespace Opc.Ua.Server.Nodes
{
    /// <summary>
    /// Resolves namespace-stable registrations once and builds type factory chains.
    /// </summary>
    internal sealed class NodeBehaviorRegistry
    {
        /// <summary>
        /// Initializes a registry for one prepared manager generation.
        /// </summary>
        public NodeBehaviorRegistry(
            ArrayOf<INodeBehaviorFactory> factories,
            NamespaceTable namespaceUris,
            ITypeTable typeTree)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            m_typeTree = typeTree ?? throw new ArgumentNullException(nameof(typeTree));
            m_factories = [];
            m_resolvedChains = [];

            if (factories.IsNull)
            {
                return;
            }

            for (int i = 0; i < factories.Count; i++)
            {
                INodeBehaviorFactory factory = factories[i] ??
                    throw new InvalidOperationException(
                        $"Node behavior factory at index {i} is null.");
                NodeId typeDefinitionId = ResolveTypeDefinitionId(
                    factory.TypeDefinitionId,
                    namespaceUris);
                if (!m_factories.TryAdd(typeDefinitionId, factory))
                {
                    throw new InvalidOperationException(
                        $"A node behavior factory is already registered for " +
                        $"type definition '{typeDefinitionId}'.");
                }
            }
        }

        /// <summary>
        /// Gets whether the registry contains behavior factories.
        /// </summary>
        public bool IsEmpty => m_factories.Count == 0;

        /// <summary>
        /// Resolves the matching factories from base type to derived type.
        /// </summary>
        public ArrayOf<INodeBehaviorFactory> ResolveFactories(
            NodeId typeDefinitionId)
        {
            if (typeDefinitionId.IsNull)
            {
                return [];
            }
            if (m_resolvedChains.TryGetValue(
                typeDefinitionId,
                out ArrayOf<INodeBehaviorFactory> cached))
            {
                return cached;
            }

            var factories = new List<INodeBehaviorFactory>();
            var visited = new HashSet<NodeId>();
            NodeId current = typeDefinitionId;
            while (!current.IsNull)
            {
                if (!visited.Add(current))
                {
                    throw new InvalidOperationException(
                        $"The type hierarchy contains a cycle at '{current}'.");
                }
                if (m_factories.TryGetValue(
                    current,
                    out INodeBehaviorFactory? factory))
                {
                    factories.Add(factory);
                }
                current = m_typeTree.FindSuperType(current);
            }

            factories.Reverse();
            var resolved = new ArrayOf<INodeBehaviorFactory>(factories.ToArray());
            m_resolvedChains.Add(typeDefinitionId, resolved);
            return resolved;
        }

        private static NodeId ResolveTypeDefinitionId(
            ExpandedNodeId typeDefinitionId,
            NamespaceTable namespaceUris)
        {
            if (typeDefinitionId.IsNull)
            {
                throw new InvalidOperationException(
                    "A node behavior factory must declare a target type definition.");
            }
            if (typeDefinitionId.ServerIndex != 0)
            {
                throw new InvalidOperationException(
                    $"Node behavior type definition '{typeDefinitionId}' targets a remote server.");
            }
            if (string.IsNullOrEmpty(typeDefinitionId.NamespaceUri) &&
                typeDefinitionId.InnerNodeId.NamespaceIndex != 0)
            {
                throw new InvalidOperationException(
                    $"Node behavior type definition '{typeDefinitionId}' is namespace-index " +
                    "dependent. Use an ExpandedNodeId with a namespace URI.");
            }

            NodeId resolved = ExpandedNodeId.ToNodeId(
                typeDefinitionId,
                namespaceUris);
            if (resolved.IsNull)
            {
                throw new InvalidOperationException(
                    $"Node behavior type definition '{typeDefinitionId}' could not be " +
                    "resolved in the server namespace table.");
            }
            return resolved;
        }

        private readonly ITypeTable m_typeTree;
        private readonly Dictionary<NodeId, INodeBehaviorFactory> m_factories;
        private readonly Dictionary<NodeId, ArrayOf<INodeBehaviorFactory>> m_resolvedChains;
    }
}
