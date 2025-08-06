/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua
{
    /// <summary>
    /// Extensions for lru node cache
    /// </summary>
    public static class LruNodeCacheExtensions
    {
        /// <summary>
        /// Get node from cache
        /// </summary>
        public static ValueTask<INode> GetNodeAsync(this ILruNodeCache cache, ExpandedNodeId expandedNodeId,
            CancellationToken ct = default)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, cache.Session.NamespaceUris);
            return cache.GetNodeAsync(nodeId, ct);
        }

        /// <summary>
        /// Get nodes from cache
        /// </summary>
        public static ValueTask<IReadOnlyList<INode>> GetNodesAsync(this ILruNodeCache cache, IReadOnlyList<ExpandedNodeId> expandedNodeIds,
            CancellationToken ct = default)
        {
            var nodeIds = expandedNodeIds.Select(expandedNodeId => ExpandedNodeId.ToNodeId(expandedNodeId, cache.Session.NamespaceUris)).ToList();
            return cache.GetNodesAsync(nodeIds, ct);
        }

        /// <summary>
        /// Get node from cache
        /// </summary>
        public static ValueTask<DataValue> GetValueAsync(this ILruNodeCache cache, ExpandedNodeId expandedNodeId,
            CancellationToken ct = default)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, cache.Session.NamespaceUris);
            return cache.GetValueAsync(nodeId, ct);
        }

        /// <summary>
        /// Get nodes from cache
        /// </summary>
        public static ValueTask<IReadOnlyList<DataValue>> GetValuesAsync(this ILruNodeCache cache, IReadOnlyList<ExpandedNodeId> expandedNodeIds,
            CancellationToken ct = default)
        {
            var nodeIds = expandedNodeIds.Select(expandedNodeId => ExpandedNodeId.ToNodeId(expandedNodeId, cache.Session.NamespaceUris)).ToList();
            return cache.GetValuesAsync(nodeIds, ct);
        }

        /// <summary>
        /// Get references from cache
        /// </summary>
        public static ValueTask<IReadOnlyList<INode>> GetReferencesAsync(this ILruNodeCache cache, ExpandedNodeId expandedNodeId, NodeId referenceTypeId,
            bool isInverse, bool includeSubtypes = true, CancellationToken ct = default)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, cache.Session.NamespaceUris);
            return cache.GetReferencesAsync(nodeId, referenceTypeId, isInverse, includeSubtypes, ct);
        }

        /// <summary>
        /// Get references from cache
        /// </summary>
        public static ValueTask<IReadOnlyList<INode>> GetReferencesAsync(this ILruNodeCache cache, IReadOnlyList<ExpandedNodeId> expandedNodeIds,
            IReadOnlyList<NodeId> referenceTypeIds, bool isInverse, bool includeSubtypes = true, CancellationToken ct = default)
        {
            var nodeIds = expandedNodeIds.Select(expandedNodeId => ExpandedNodeId.ToNodeId(expandedNodeId, cache.Session.NamespaceUris)).ToList();
            return cache.GetReferencesAsync(nodeIds, referenceTypeIds, isInverse, includeSubtypes, ct);
        }

        /// <summary>
        /// Get references from cache
        /// </summary>
        public static ValueTask<IReadOnlyList<INode>> GetReferencesAsync(this ILruNodeCache cache, IReadOnlyList<ExpandedNodeId> expandedNodeIds,
            NodeId referenceTypeId, bool isInverse, bool includeSubtypes = true, CancellationToken ct = default)
        {
            var nodeIds = expandedNodeIds.Select(expandedNodeId => ExpandedNodeId.ToNodeId(expandedNodeId, cache.Session.NamespaceUris)).ToList();
            return cache.GetReferencesAsync(nodeIds, [referenceTypeId], isInverse, includeSubtypes, ct);
        }

        /// <summary>
        /// Get super type from cache
        /// </summary>
        public static ValueTask<NodeId> GetSuperTypeAsync(this ILruNodeCache cache, ExpandedNodeId expandedNodeId,
            CancellationToken ct = default)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expandedNodeId, cache.Session.NamespaceUris);
            return cache.GetSuperTypeAsync(nodeId, ct);
        }

        /// <summary>
        /// Is the subTypeId a subtype of the superTypeId?
        /// </summary>
        public static bool IsTypeOf(this ILruNodeCache cache, ExpandedNodeId subTypeId, NodeId superTypeId)
        {
            var nodeId = ExpandedNodeId.ToNodeId(subTypeId, cache.Session.NamespaceUris);
            return cache.IsTypeOf(nodeId, superTypeId);
        }

        /// <summary>
        /// Returns the BuiltInType type for the DataTypeId.
        /// </summary>
        public static async Task<BuiltInType> GetBuiltInTypeAsync(this ILruNodeCache cache, NodeId datatypeId, CancellationToken ct = default)
        {
            NodeId typeId = datatypeId;
            while (!Opc.Ua.NodeId.IsNull(typeId))
            {
                if (typeId != null && typeId.NamespaceIndex == 0 && typeId.IdType == Opc.Ua.IdType.Numeric)
                {
                    var id = (BuiltInType)(int)(uint)typeId.Identifier;
                    if (id is > BuiltInType.Null and <= BuiltInType.Enumeration and not BuiltInType.DiagnosticInfo)
                    {
                        return id;
                    }
                }
                typeId = await cache.GetSuperTypeAsync(typeId, ct).ConfigureAwait(false);
            }
            return BuiltInType.Null;
        }
    }
}
