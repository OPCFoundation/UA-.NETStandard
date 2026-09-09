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
    /// One type-keyed behavior registration.
    /// </summary>
    /// <remarks>
    /// <paramref name="IncludeSubtypes"/> controls whether the factory also matches
    /// instances of subtypes of its declared type definition. Exact-type registrations
    /// participate only at their own level of the type chain.
    /// </remarks>
    internal readonly record struct NodeBehaviorRegistration(
        INodeBehaviorFactory Factory,
        bool IncludeSubtypes);

    /// <summary>
    /// Resolves namespace-stable registrations once and builds type factory chains.
    /// </summary>
    /// <remarks>
    /// More than one factory may target the same type definition. Factories registered
    /// for one type activate in registration order, and the chain as a whole runs from
    /// the base type down to the derived type, so a base-type behavior is live before a
    /// derived-type behavior on the same node.
    /// </remarks>
    internal sealed class NodeBehaviorRegistry
    {
        /// <summary>
        /// Initializes a registry for one activation pass.
        /// </summary>
        public NodeBehaviorRegistry(
            IReadOnlyList<NodeBehaviorRegistration> registrations,
            NamespaceTable namespaceUris,
            ITypeTable typeTree)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            m_typeTree = typeTree ?? throw new ArgumentNullException(nameof(typeTree));
            m_registrations = [];
            m_resolvedChains = [];

            if (registrations is null)
            {
                return;
            }

            for (int i = 0; i < registrations.Count; i++)
            {
                NodeBehaviorRegistration registration = registrations[i];
                if (registration.Factory is null)
                {
                    throw new InvalidOperationException(
                        $"Node behavior factory at index {i} is null.");
                }

                NodeId typeDefinitionId = ResolveTypeDefinitionId(
                    registration.Factory.TypeDefinitionId,
                    namespaceUris);
                if (!m_registrations.TryGetValue(
                    typeDefinitionId,
                    out List<NodeBehaviorRegistration>? forType))
                {
                    forType = [];
                    m_registrations.Add(typeDefinitionId, forType);
                }

                forType.Add(registration);
            }
        }

        /// <summary>
        /// Gets whether the registry contains behavior factories.
        /// </summary>
        public bool IsEmpty => m_registrations.Count == 0;

        /// <summary>
        /// Resolves the matching factories from base type to derived type.
        /// </summary>
        /// <remarks>
        /// Factories registered for the same type keep their registration order. Only
        /// the node's own type definition contributes exact-type registrations;
        /// supertypes contribute only registrations that opted into subtype matching.
        /// </remarks>
        public ArrayOf<INodeBehaviorFactory> ResolveFactories(NodeId typeDefinitionId)
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

            // Walk derived-to-base collecting one bucket per level, then emit the
            // buckets in reverse so the chain runs base-to-derived.
            var levels = new List<List<INodeBehaviorFactory>>();
            var visited = new HashSet<NodeId>();
            NodeId current = typeDefinitionId;
            bool isOwnType = true;

            while (!current.IsNull)
            {
                if (!visited.Add(current))
                {
                    throw new InvalidOperationException(
                        $"The type hierarchy contains a cycle at '{current}'.");
                }
                if (m_registrations.TryGetValue(
                    current,
                    out List<NodeBehaviorRegistration>? forType))
                {
                    var level = new List<INodeBehaviorFactory>(forType.Count);
                    for (int i = 0; i < forType.Count; i++)
                    {
                        NodeBehaviorRegistration registration = forType[i];
                        if (isOwnType || registration.IncludeSubtypes)
                        {
                            level.Add(registration.Factory);
                        }
                    }
                    if (level.Count > 0)
                    {
                        levels.Add(level);
                    }
                }

                current = m_typeTree.FindSuperType(current);
                isOwnType = false;
            }

            var factories = new List<INodeBehaviorFactory>();
            for (int i = levels.Count - 1; i >= 0; i--)
            {
                factories.AddRange(levels[i]);
            }

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
                    $"Node behavior type definition '{typeDefinitionId}' targets a " +
                    "remote server.");
            }
            if (string.IsNullOrEmpty(typeDefinitionId.NamespaceUri) &&
                typeDefinitionId.InnerNodeId.NamespaceIndex != 0)
            {
                throw new InvalidOperationException(
                    $"Node behavior type definition '{typeDefinitionId}' is " +
                    "namespace-index dependent. Use an ExpandedNodeId with a " +
                    "namespace URI.");
            }

            NodeId resolved = ExpandedNodeId.ToNodeId(typeDefinitionId, namespaceUris);
            if (resolved.IsNull)
            {
                throw new InvalidOperationException(
                    $"Node behavior type definition '{typeDefinitionId}' could not be " +
                    "resolved in the server namespace table.");
            }
            return resolved;
        }

        private readonly ITypeTable m_typeTree;
        private readonly Dictionary<NodeId, List<NodeBehaviorRegistration>> m_registrations;
        private readonly Dictionary<NodeId, ArrayOf<INodeBehaviorFactory>> m_resolvedChains;
    }
}
