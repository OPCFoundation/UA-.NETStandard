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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua
{
    /// <summary>
    /// Convenience extensions on <see cref="INodeCache"/> for callers
    /// that haven't yet resolved the namespace index, plus higher-level
    /// helpers (display text, browse-path traversal, built-in type
    /// resolution) that compose primitives.
    /// </summary>
    public static class NodeCacheExtensions
    {
        /// <summary>
        /// Get node from cache
        /// </summary>
        public static ValueTask<INode> GetNodeAsync(
            this INodeCache cache,
            ExpandedNodeId expandedNodeId,
            CancellationToken ct = default)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, cache.NamespaceUris);
            return cache.GetNodeAsync(nodeId, ct);
        }

        /// <summary>
        /// Get nodes from cache
        /// </summary>
        public static ValueTask<ArrayOf<INode>> GetNodesAsync(
            this INodeCache cache,
            ArrayOf<ExpandedNodeId> expandedNodeIds,
            CancellationToken ct = default)
        {
            ArrayOf<NodeId> nodeIds = expandedNodeIds
                .ConvertAll(expandedNodeId => ExpandedNodeId.ToNodeId(
                    expandedNodeId,
                    cache.NamespaceUris));
            return cache.GetNodesAsync(nodeIds, ct);
        }

        /// <summary>
        /// Get value of a node from cache
        /// </summary>
        public static ValueTask<DataValue> GetValueAsync(
            this INodeCache cache,
            ExpandedNodeId expandedNodeId,
            CancellationToken ct = default)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, cache.NamespaceUris);
            return cache.GetValueAsync(nodeId, ct);
        }

        /// <summary>
        /// Get values of nodes from cache
        /// </summary>
        public static ValueTask<ArrayOf<DataValue>> GetValuesAsync(
            this INodeCache cache,
            ArrayOf<ExpandedNodeId> expandedNodeIds,
            CancellationToken ct = default)
        {
            ArrayOf<NodeId> nodeIds = expandedNodeIds
                .ConvertAll(expandedNodeId => ExpandedNodeId.ToNodeId(
                    expandedNodeId,
                    cache.NamespaceUris));
            return cache.GetValuesAsync(nodeIds, ct);
        }

        /// <summary>
        /// Get references for a node from cache
        /// </summary>
        public static ValueTask<ArrayOf<INode>> GetReferencesAsync(
            this INodeCache cache,
            ExpandedNodeId expandedNodeId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes = true,
            CancellationToken ct = default)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, cache.NamespaceUris);
            return cache.GetReferencesAsync(
                nodeId,
                referenceTypeId,
                isInverse,
                includeSubtypes,
                ct);
        }

        /// <summary>
        /// Get references for a collection of nodes from cache
        /// </summary>
        public static ValueTask<ArrayOf<INode>> GetReferencesAsync(
            this INodeCache cache,
            ArrayOf<ExpandedNodeId> expandedNodeIds,
            ArrayOf<NodeId> referenceTypeIds,
            bool isInverse,
            bool includeSubtypes = true,
            CancellationToken ct = default)
        {
            ArrayOf<NodeId> nodeIds = expandedNodeIds
                .ConvertAll(expandedNodeId => ExpandedNodeId.ToNodeId(
                    expandedNodeId,
                    cache.NamespaceUris));
            return cache.GetReferencesAsync(
                nodeIds,
                referenceTypeIds,
                isInverse,
                includeSubtypes,
                ct);
        }

        /// <summary>
        /// Get references for a collection of nodes from cache (single
        /// reference type id).
        /// </summary>
        public static ValueTask<ArrayOf<INode>> GetReferencesAsync(
            this INodeCache cache,
            ArrayOf<ExpandedNodeId> expandedNodeIds,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes = true,
            CancellationToken ct = default)
        {
            ArrayOf<NodeId> nodeIds = expandedNodeIds
                .ConvertAll(expandedNodeId => ExpandedNodeId.ToNodeId(
                    expandedNodeId,
                    cache.NamespaceUris));
            return cache.GetReferencesAsync(
                nodeIds,
                [referenceTypeId],
                isInverse,
                includeSubtypes,
                ct);
        }

        /// <summary>
        /// Find a set of nodes by ExpandedNodeId. Each entry is
        /// <c>null</c> when the corresponding node could not be
        /// resolved.
        /// </summary>
        public static async ValueTask<ArrayOf<INode?>> FindAsync(
            this INodeCache cache,
            ArrayOf<ExpandedNodeId> nodeIds,
            CancellationToken ct = default)
        {
            if (nodeIds.IsEmpty)
            {
                return [];
            }
            var result = new List<INode?>(nodeIds.Count);
            foreach (ExpandedNodeId nodeId in nodeIds.ToList())
            {
                result.Add(await cache.FindAsync(nodeId, ct).ConfigureAwait(false));
            }
            return result;
        }

        /// <summary>
        /// Returns the references of the specified node that meet the
        /// criteria specified. Convenience alias that delegates to the
        /// inherited <see cref="IAsyncNodeTable.FindAsync(ExpandedNodeId,NodeId,bool,bool,CancellationToken)"/>.
        /// </summary>
        public static ValueTask<ArrayOf<INode>> FindReferencesAsync(
            this INodeCache cache,
            ExpandedNodeId nodeId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            CancellationToken ct = default)
        {
            return cache.FindAsync(
                nodeId,
                referenceTypeId,
                isInverse,
                includeSubtypes,
                ct);
        }

        /// <summary>
        /// Returns the references of the specified nodes that meet the
        /// criteria specified.
        /// </summary>
        public static ValueTask<ArrayOf<INode>> FindReferencesAsync(
            this INodeCache cache,
            ArrayOf<ExpandedNodeId> nodeIds,
            ArrayOf<NodeId> referenceTypeIds,
            bool isInverse,
            bool includeSubtypes,
            CancellationToken ct = default)
        {
            ArrayOf<NodeId> localIds = nodeIds.ConvertAll(
                expandedNodeId => ExpandedNodeId.ToNodeId(
                    expandedNodeId,
                    cache.NamespaceUris));
            return cache.GetReferencesAsync(
                localIds,
                referenceTypeIds,
                isInverse,
                includeSubtypes,
                ct);
        }

        /// <summary>
        /// Walk all supertypes of <paramref name="nodeId"/> so they end
        /// up in the cache. Composes <see cref="IAsyncTypeTable.FindSuperTypeAsync(ExpandedNodeId,CancellationToken)"/>.
        /// </summary>
        public static async ValueTask FetchSuperTypesAsync(
            this INodeCache cache,
            ExpandedNodeId nodeId,
            CancellationToken ct = default)
        {
            NodeId current = ExpandedNodeId.ToNodeId(nodeId, cache.NamespaceUris);
            while (!current.IsNull)
            {
                current = await cache.FindSuperTypeAsync(current, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Walks a browse path from the given node and returns the
        /// terminal node (or <c>null</c> if the path cannot be
        /// resolved). The walker traverses hierarchical references and
        /// climbs supertypes when a name is not found at the current
        /// level.
        /// </summary>
        public static async ValueTask<INode?> GetNodeWithBrowsePathAsync(
            this INodeCache cache,
            NodeId nodeId,
            ArrayOf<QualifiedName> browsePath,
            CancellationToken ct = default)
        {
            INode? found = null;
            foreach (QualifiedName browseName in browsePath.ToList())
            {
                found = null;
                while (true)
                {
                    if (nodeId.IsNull)
                    {
                        return null;
                    }

                    ArrayOf<INode> references = await cache.GetReferencesAsync(
                            nodeId,
                            ReferenceTypeIds.HierarchicalReferences,
                            false,
                            true,
                            ct)
                        .ConfigureAwait(false);
                    foreach (INode target in references)
                    {
                        if (target.BrowseName == browseName)
                        {
                            nodeId = ExpandedNodeId.ToNodeId(target.NodeId, cache.NamespaceUris);
                            if (!nodeId.IsNull)
                            {
                                found = target;
                            }
                            break;
                        }
                    }

                    if (found != null)
                    {
                        break;
                    }
                    nodeId = await cache.FindSuperTypeAsync(nodeId, ct).ConfigureAwait(false);
                }
                nodeId = ExpandedNodeId.ToNodeId(found.NodeId, cache.NamespaceUris);
            }
            return found;
        }

        /// <summary>
        /// Resolve the built-in type a data-type ultimately maps to by
        /// climbing the supertype chain.
        /// </summary>
        public static async ValueTask<BuiltInType> GetBuiltInTypeAsync(
            this INodeCache cache,
            NodeId datatypeId,
            CancellationToken ct = default)
        {
            NodeId typeId = datatypeId;
            while (!typeId.IsNull)
            {
                if (typeId.NamespaceIndex == 0 && typeId.TryGetIdentifier(out uint numericId))
                {
                    var id = (BuiltInType)(int)numericId;
                    if (id is > BuiltInType.Null and <= BuiltInType.Enumeration and not BuiltInType.DiagnosticInfo)
                    {
                        return id;
                    }
                }
                typeId = await cache.FindSuperTypeAsync(typeId, ct).ConfigureAwait(false);
            }
            return BuiltInType.Null;
        }

        /// <summary>
        /// Returns a display name for a node.
        /// </summary>
        public static ValueTask<string?> GetDisplayTextAsync(
            this INodeCache cache,
            INode? node,
            CancellationToken ct = default)
        {
            _ = cache;
            _ = ct;
            if (node == null)
            {
                return new ValueTask<string?>(string.Empty);
            }
            string? text = node.DisplayName.Text ?? node.BrowseName.Name ?? string.Empty;
            return new ValueTask<string?>(text);
        }

        /// <summary>
        /// Returns a display name for a node identified by
        /// <see cref="ExpandedNodeId"/>.
        /// </summary>
        public static async ValueTask<string?> GetDisplayTextAsync(
            this INodeCache cache,
            ExpandedNodeId nodeId,
            CancellationToken ct = default)
        {
            if (nodeId.IsNull)
            {
                return string.Empty;
            }
            INode? node = await cache.FindAsync(nodeId, ct).ConfigureAwait(false);
            if (node != null)
            {
                return await cache.GetDisplayTextAsync(node, ct).ConfigureAwait(false);
            }
            return Utils.Format("{0}", nodeId);
        }

        /// <summary>
        /// Returns a display name for the target of a reference.
        /// </summary>
        public static ValueTask<string?> GetDisplayTextAsync(
            this INodeCache cache,
            ReferenceDescription? reference,
            CancellationToken ct = default)
        {
            _ = cache;
            _ = ct;
            if (reference == null || reference.NodeId.IsNull)
            {
                return new ValueTask<string?>(string.Empty);
            }
            string? text = reference.DisplayName.Text
                ?? reference.BrowseName.Name
                ?? string.Empty;
            return new ValueTask<string?>(text);
        }
    }
}
