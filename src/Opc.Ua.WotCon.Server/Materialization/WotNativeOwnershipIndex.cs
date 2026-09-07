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
using Opc.Ua.Export;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Bounded ownership evidence shared across partitions. Type definitions
    /// and forward references are not ownership ancestry.
    /// </summary>
    internal sealed class WotNativeOwnershipIndex
    {
        public WotNativeOwnershipIndex(int maxNodes)
        {
            if (maxNodes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxNodes));
            }
            m_maxNodes = maxNodes;
            m_maxEdges = (long)maxNodes * 8;
        }

        public ExpandedNodeId Add(UANodeSet nodeSet, string? rootIdentifier, ExpandedNodeId fallbackRoot)
        {
            if (nodeSet.Aliases is { Length: var aliasCount } && aliasCount > m_maxNodes)
            {
                throw LimitExceeded();
            }
            var identities = new PartitionIdentities(nodeSet);
            m_referenceKinds.Clear();
            foreach (UANode node in nodeSet.Items ?? [])
            {
                ExpandedNodeId id = identities.Resolve(node.NodeId);
                if (id.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "A native ownership node has no id.");
                }
                NodeClass nodeClass = ClassOf(node);
                if (!m_nodes.TryGetValue(id, out NodeEvidence? evidence))
                {
                    if (m_nodes.Count >= m_maxNodes)
                    {
                        throw LimitExceeded();
                    }
                    evidence = new NodeEvidence { NodeClass = nodeClass };
                    m_nodes.Add(id, evidence);
                }
                else if (evidence.NodeClass != nodeClass)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "Native ownership partitions disagree on a NodeClass.");
                }
                if (node is UAInstance instance && !string.IsNullOrEmpty(instance.ParentNodeId))
                {
                    AddEdge(evidence.Parents, new OwnershipEdge(
                        identities.Resolve(instance.ParentNodeId), ExpandedNodeId.Null));
                }
                foreach (Reference reference in node.References ?? [])
                {
                    if (reference.IsForward)
                    {
                        continue;
                    }
                    ExpandedNodeId referenceType = identities.Resolve(reference.ReferenceType);
                    if (node is UAReferenceType && referenceType == Ua.ReferenceTypeIds.HasSubtype)
                    {
                        AddEdge(evidence.ReferenceParents, new OwnershipEdge(
                            identities.Resolve(reference.Value), ExpandedNodeId.Null));
                    }
                    else if (!string.IsNullOrEmpty(referenceType.NamespaceUri) ||
                        referenceType == Ua.ReferenceTypeIds.HasComponent ||
                        referenceType == Ua.ReferenceTypeIds.HasProperty ||
                        referenceType == Ua.ReferenceTypeIds.HasOrderedComponent)
                    {
                        AddEdge(evidence.Parents, new OwnershipEdge(
                            identities.Resolve(reference.Value), referenceType));
                    }
                }
            }
            return string.IsNullOrEmpty(rootIdentifier)
                ? identities.Resolve(fallbackRoot.ToString())
                : identities.Resolve(rootIdentifier);
        }

        public bool IsDeclaration(ExpandedNodeId root)
        {
            if (root.IsNull)
            {
                return false;
            }
            var pending = new Stack<ExpandedNodeId>();
            var visited = new HashSet<ExpandedNodeId>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                ExpandedNodeId current = pending.Pop();
                if (!visited.Add(current) || !m_nodes.TryGetValue(current, out NodeEvidence? node))
                {
                    continue;
                }
                if (node.NodeClass is NodeClass.ObjectType or NodeClass.VariableType)
                {
                    return true;
                }
                foreach (OwnershipEdge edge in node.Parents)
                {
                    if (edge.ReferenceType.IsNull || IsOwnershipReference(edge.ReferenceType))
                    {
                        pending.Push(edge.Target);
                    }
                }
            }
            return false;
        }

        private bool IsOwnershipReference(ExpandedNodeId referenceType)
        {
            if (m_referenceKinds.TryGetValue(referenceType, out bool known))
            {
                return known;
            }
            var pending = new Stack<ExpandedNodeId>();
            var visited = new HashSet<ExpandedNodeId>();
            pending.Push(referenceType);
            while (pending.Count > 0)
            {
                ExpandedNodeId current = pending.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }
                if (current == Ua.ReferenceTypeIds.HasComponent ||
                    current == Ua.ReferenceTypeIds.HasProperty ||
                    current == Ua.ReferenceTypeIds.HasOrderedComponent)
                {
                    m_referenceKinds[referenceType] = true;
                    return true;
                }
                if (m_nodes.TryGetValue(current, out NodeEvidence? node))
                {
                    foreach (OwnershipEdge edge in node.ReferenceParents)
                    {
                        pending.Push(edge.Target);
                    }
                }
            }
            m_referenceKinds[referenceType] = false;
            return false;
        }

        private static NodeClass ClassOf(UANode node)
        {
            return node switch
            {
                UAObject => NodeClass.Object,
                UAVariable => NodeClass.Variable,
                UAMethod => NodeClass.Method,
                UAObjectType => NodeClass.ObjectType,
                UAVariableType => NodeClass.VariableType,
                UAReferenceType => NodeClass.ReferenceType,
                UADataType => NodeClass.DataType,
                UAView => NodeClass.View,
                _ => NodeClass.Unspecified
            };
        }

        private void AddEdge(HashSet<OwnershipEdge> edges, OwnershipEdge edge)
        {
            if (edges.Contains(edge))
            {
                return;
            }
            if (m_edgeCount >= m_maxEdges)
            {
                throw LimitExceeded();
            }
            edges.Add(edge);
            m_edgeCount++;
        }

        private static ServiceResultException LimitExceeded()
        {
            return new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded, "The native ownership index exceeded its configured bounds.");
        }

        private sealed class NodeEvidence
        {
            public NodeClass NodeClass { get; init; }
            public HashSet<OwnershipEdge> Parents { get; } = [];
            public HashSet<OwnershipEdge> ReferenceParents { get; } = [];
        }

        private readonly record struct OwnershipEdge(ExpandedNodeId Target, ExpandedNodeId ReferenceType);

        private sealed class PartitionIdentities
        {
            public PartitionIdentities(UANodeSet nodeSet)
            {
                foreach (string uri in nodeSet.NamespaceUris ?? [])
                {
                    m_namespaces.Append(uri);
                }
                foreach (NodeIdAlias alias in nodeSet.Aliases ?? [])
                {
                    if (string.IsNullOrEmpty(alias.Alias) || string.IsNullOrEmpty(alias.Value))
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "An ownership alias is empty.");
                    }
                    if (m_aliases.TryGetValue(alias.Alias, out string? previous) && previous != alias.Value)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeIdInvalid, "An ownership alias has conflicting definitions.");
                    }
                    m_aliases[alias.Alias] = alias.Value;
                }
            }

            public ExpandedNodeId Resolve(string? identifier)
            {
                if (string.IsNullOrEmpty(identifier))
                {
                    return ExpandedNodeId.Null;
                }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (m_aliases.TryGetValue(identifier, out string? target))
                {
                    if (!seen.Add(identifier))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeIdInvalid, "Ownership aliases form a cycle.");
                    }
                    identifier = target;
                }
                if (s_referenceAliases.TryGetValue(identifier, out NodeId standard))
                {
                    return standard;
                }
                ExpandedNodeId expanded = ExpandedNodeId.Parse(identifier);
                if (expanded.ServerIndex != 0 ||
                    (string.IsNullOrEmpty(expanded.NamespaceUri) && expanded.NamespaceIndex >= m_namespaces.Count))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "An ownership identifier has no local namespace mapping.");
                }
                if (!string.IsNullOrEmpty(expanded.NamespaceUri) && expanded.NamespaceUri != Ua.Namespaces.OpcUa)
                {
                    return expanded;
                }
                NodeId local = ExpandedNodeId.ToNodeId(expanded, m_namespaces);
                return local.NamespaceIndex == 0
                    ? new ExpandedNodeId(local)
                    : new ExpandedNodeId(local, m_namespaces.GetString(local.NamespaceIndex));
            }

            private readonly NamespaceTable m_namespaces = new();
            private readonly Dictionary<string, string> m_aliases = new(StringComparer.Ordinal);
            private static readonly Dictionary<string, NodeId> s_referenceAliases = new(StringComparer.Ordinal)
            {
                ["HasComponent"] = Ua.ReferenceTypeIds.HasComponent,
                ["HasOrderedComponent"] = Ua.ReferenceTypeIds.HasOrderedComponent,
                ["HasProperty"] = Ua.ReferenceTypeIds.HasProperty,
                ["HasSubtype"] = Ua.ReferenceTypeIds.HasSubtype,
                ["HasTypeDefinition"] = Ua.ReferenceTypeIds.HasTypeDefinition,
                ["Organizes"] = Ua.ReferenceTypeIds.Organizes,
                ["HasModellingRule"] = Ua.ReferenceTypeIds.HasModellingRule,
                ["HasEncoding"] = Ua.ReferenceTypeIds.HasEncoding,
                ["HasDescription"] = Ua.ReferenceTypeIds.HasDescription,
                ["HasEventSource"] = Ua.ReferenceTypeIds.HasEventSource,
                ["HasNotifier"] = Ua.ReferenceTypeIds.HasNotifier,
                ["GeneratesEvent"] = Ua.ReferenceTypeIds.GeneratesEvent,
                ["HasCondition"] = Ua.ReferenceTypeIds.HasCondition,
                ["HasInterface"] = Ua.ReferenceTypeIds.HasInterface,
                ["HasAddIn"] = Ua.ReferenceTypeIds.HasAddIn
            };
        }

        private readonly int m_maxNodes;
        private readonly long m_maxEdges;
        private readonly Dictionary<ExpandedNodeId, NodeEvidence> m_nodes = [];
        private readonly Dictionary<ExpandedNodeId, bool> m_referenceKinds = [];
        private long m_edgeCount;
    }
}
