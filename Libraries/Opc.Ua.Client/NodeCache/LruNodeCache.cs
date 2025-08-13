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

#if NET8_0_OR_GREATER

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BitFaster.Caching;
using BitFaster.Caching.Lfu;
using BitFaster.Caching.Lru;

namespace Opc.Ua.Client
{
    /// <summary>
    /// This node cache is a real cache and consists of three LRU caches.
    /// These caches contain nodes and references (Read and browse).
    /// Cache entries expire after not being accessed for a certain time.
    /// The cache is thread-safe and can be accessed by multiple readers.
    /// It is also limited to a capacity at which point least recently used
    /// entries will be evicted.
    /// </summary>
    public sealed class LruNodeCache : ILruNodeCache
    {
        /// <summary>
        /// Create cache
        /// </summary>
        public LruNodeCache(
            ISession session,
            TimeSpan? cacheExpiry = null,
            int capacity = 4096,
            bool withMetrics = false)
        {
            cacheExpiry ??= TimeSpan.FromMinutes(5);

            Session = session;
            BitFaster.Caching.Lru.Builder.AtomicAsyncConcurrentLruBuilder<NodeId, INode> nodesBuilder =
                new ConcurrentLruBuilder<NodeId, INode>()
                    .WithAtomicGetOrAdd()
                    .AsAsyncCache()
                    .WithCapacity(capacity)
                    .WithKeyComparer(Comparers.Instance)
                    .WithExpireAfterAccess(cacheExpiry.Value);
            BitFaster.Caching.Lru.Builder.AtomicAsyncConcurrentLruBuilder<
                NodeId,
                List<ReferenceDescription>
            > refsBuilder = new ConcurrentLruBuilder<NodeId, List<ReferenceDescription>>()
                .WithAtomicGetOrAdd()
                .AsAsyncCache()
                .WithCapacity(capacity)
                .WithKeyComparer(Comparers.Instance)
                .WithExpireAfterAccess(cacheExpiry.Value);
            BitFaster.Caching.Lru.Builder.AtomicAsyncConcurrentLruBuilder<NodeId, DataValue> valuesBuilder =
                new ConcurrentLruBuilder<NodeId, DataValue>()
                    .WithAtomicGetOrAdd()
                    .AsAsyncCache()
                    .WithCapacity(capacity)
                    .WithKeyComparer(Comparers.Instance)
                    .WithExpireAfterAccess(cacheExpiry.Value);
            if (withMetrics)
            {
                nodesBuilder = nodesBuilder.WithMetrics();
                valuesBuilder = valuesBuilder.WithMetrics();
                refsBuilder = refsBuilder.WithMetrics();
            }
            m_nodes = nodesBuilder.Build();
            m_values = valuesBuilder.Build();
            m_refs = refsBuilder.Build();
        }

        /// <summary>
        /// Node metrics
        /// </summary>
        public ICacheMetrics? NodesMetrics => m_nodes.Metrics.Value;

        /// <summary>
        /// Value metrics
        /// </summary>
        public ICacheMetrics? ValuesMetrics => m_values.Metrics.Value;

        /// <summary>
        /// References metrics
        /// </summary>
        public ICacheMetrics? ReferencesMetrics => m_refs.Metrics.Value;

        /// <inheritdoc/>
        public ISession Session { get; }

        /// <inheritdoc/>
        public ValueTask<INode> GetNodeAsync(NodeId nodeId, CancellationToken ct)
        {
            return m_nodes.TryGet(nodeId, out INode? node)
                ? ValueTask.FromResult(node)
                : FindAsyncCore(nodeId, ct);
            ValueTask<INode> FindAsyncCore(NodeId nodeId, CancellationToken ct)
            {
                return m_nodes.GetOrAddAsync(
                    nodeId,
                    async (nodeId, context) =>
                        await context.session.ReadNodeAsync(nodeId, context.ct)
                            .ConfigureAwait(false),
                    (session: Session, ct));
            }
        }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<INode>> GetNodesAsync(
            IReadOnlyList<NodeId> nodeIds,
            CancellationToken ct)
        {
            int count = nodeIds.Count;
            var result = new List<INode?>(nodeIds.Count);
            if (count != 0)
            {
                var notFound = new List<NodeId>();
                foreach (NodeId nodeId in nodeIds)
                {
                    if (m_nodes.TryGet(nodeId, out INode? node))
                    {
                        result.Add(node);
                        continue;
                    }
                    notFound.Add(nodeId);
                    result.Add(null);
                }
                if (notFound.Count != 0)
                {
                    return FetchRemainingAsync(notFound, result, ct);
                }
            }
            Debug.Assert(!result.Any(r => r == null)); // None now should be null
            return ValueTask.FromResult<IReadOnlyList<INode>>(result!);
        }

        /// <inheritdoc/>
        public ValueTask<DataValue> GetValueAsync(NodeId nodeId, CancellationToken ct)
        {
            return m_values.TryGet(nodeId, out DataValue? dataValue)
                ? ValueTask.FromResult(dataValue)
                : FindAsyncCore(nodeId, ct);
            ValueTask<DataValue> FindAsyncCore(NodeId nodeId, CancellationToken ct)
            {
                return m_values.GetOrAddAsync(
                    nodeId,
                    (nodeId, context) => context.session.ReadValueAsync(nodeId, context.ct),
                    (session: Session, ct));
            }
        }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<DataValue>> GetValuesAsync(
            IReadOnlyList<NodeId> nodeIds,
            CancellationToken ct)
        {
            int count = nodeIds.Count;
            var result = new List<DataValue?>(nodeIds.Count);
            if (count != 0)
            {
                var notFound = new List<NodeId>();
                foreach (NodeId nodeId in nodeIds)
                {
                    if (m_values.TryGet(nodeId, out DataValue? dataValue))
                    {
                        result.Add(dataValue);
                        continue;
                    }
                    notFound.Add(nodeId);
                    result.Add(null);
                }
                if (notFound.Count != 0)
                {
                    return FetchRemainingAsync(notFound, result, ct);
                }
            }
            Debug.Assert(!result.Any(r => r == null)); // None now should be null
            return ValueTask.FromResult<IReadOnlyList<DataValue>>(result!);
        }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<INode>> GetReferencesAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            CancellationToken ct)
        {
            return
                (!includeSubtypes || IsTypeHierarchyLoaded([referenceTypeId])) &&
                m_refs.TryGet(nodeId, out List<ReferenceDescription>? references)
                ? GetNodesAsync(
                    FilterNodes(references, isInverse, referenceTypeId, includeSubtypes),
                    ct)
                : FindReferencesAsyncCore(nodeId, referenceTypeId, isInverse, includeSubtypes, ct);

            async ValueTask<IReadOnlyList<INode>> FindReferencesAsyncCore(
                NodeId nodeId,
                NodeId referenceTypeId,
                bool isInverse,
                bool includeSubtypes,
                CancellationToken ct)
            {
                if (includeSubtypes)
                {
                    await LoadTypeHierarchyAsync([referenceTypeId], ct).ConfigureAwait(false);
                }
                List<ReferenceDescription> references = await GetOrAddReferencesAsync(nodeId, ct)
                    .ConfigureAwait(false);
                return await GetNodesAsync(
                    FilterNodes(references, isInverse, referenceTypeId, includeSubtypes),
                    ct)
                    .ConfigureAwait(false);
            }

            List<NodeId> FilterNodes(
                IEnumerable<ReferenceDescription> references,
                bool isInverse,
                NodeId refTypeId,
                bool includeSubtypes)
            {
                return
                [
                    .. references
                        .Where(r =>
                            r.IsForward == !isInverse &&
                            (
                                r.ReferenceTypeId == refTypeId ||
                                (includeSubtypes && IsTypeOf(r.ReferenceTypeId, refTypeId))))
                        .Select(r => ToNodeId(r.NodeId))
                        .Where(n => !NodeId.IsNull(n))
                ];
            }
        }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<INode>> GetReferencesAsync(
            IReadOnlyList<NodeId> nodeIds,
            IReadOnlyList<NodeId> referenceTypeIds,
            bool isInverse,
            bool includeSubtypes,
            CancellationToken ct)
        {
            var targetIds = new List<NodeId>();
            var notFound = new List<NodeId>();
            if (includeSubtypes && !IsTypeHierarchyLoaded(referenceTypeIds))
            {
                return FindReferencesAsyncCore(
                    notFound,
                    referenceTypeIds,
                    isInverse,
                    includeSubtypes,
                    targetIds,
                    ct);
            }
            foreach (NodeId nodeId in nodeIds)
            {
                if (NodeId.IsNull(nodeId))
                {
                    continue;
                }
                if (m_refs.TryGet(nodeId, out List<ReferenceDescription>? references))
                {
                    targetIds.AddRange(
                        FilterNodes(references, isInverse, referenceTypeIds, includeSubtypes));
                }
                else
                {
                    notFound.Add(nodeId);
                }
            }
            return notFound.Count != 0
                ? FindReferencesAsyncCore(
                    notFound,
                    referenceTypeIds,
                    isInverse,
                    includeSubtypes,
                    targetIds,
                    ct)
                : GetNodesAsync(targetIds, ct);

            async ValueTask<IReadOnlyList<INode>> FindReferencesAsyncCore(
                IReadOnlyList<NodeId> nodeIds,
                IReadOnlyList<NodeId> referenceTypeIds,
                bool isInverse,
                bool includeSubtypes,
                List<NodeId> targetIds,
                CancellationToken ct)
            {
                if (includeSubtypes)
                {
                    await LoadTypeHierarchyAsync(referenceTypeIds, ct).ConfigureAwait(false);
                }
                foreach (NodeId nodeId in nodeIds)
                {
                    List<ReferenceDescription> references = await GetOrAddReferencesAsync(
                        nodeId,
                        ct)
                        .ConfigureAwait(false);
                    targetIds.AddRange(
                        FilterNodes(references, isInverse, referenceTypeIds, includeSubtypes));
                }
                return await GetNodesAsync(targetIds, ct).ConfigureAwait(false);
            }
            List<NodeId> FilterNodes(
                IEnumerable<ReferenceDescription> references,
                bool isInverse,
                IReadOnlyList<NodeId> referenceTypeIds,
                bool includeSubtypes)
            {
                return
                [
                    .. references
                        .Where(r =>
                            r.IsForward == !isInverse &&
                            referenceTypeIds.Any(refTypeId =>
                                r.ReferenceTypeId == refTypeId ||
                                (includeSubtypes && IsTypeOf(r.ReferenceTypeId, refTypeId))))
                        .Select(r => ToNodeId(r.NodeId))
                        .Where(n => !NodeId.IsNull(n))
                ];
            }
        }

        /// <inheritdoc/>
        public async ValueTask LoadTypeHierarchyAsync(
            IReadOnlyList<NodeId> typeIds,
            CancellationToken ct)
        {
            IReadOnlyList<INode> nodes = await GetReferencesAsync(
                    typeIds,
                    [ReferenceTypeIds.HasSubtype],
                    false,
                    false,
                    ct)
                .ConfigureAwait(false);
            if (nodes.Count > 0)
            {
                if (nodes is not List<INode> subTypes)
                {
                    subTypes = [.. nodes];
                }
                await LoadTypeHierarchyAsync(subTypes.ConvertAll(n => ToNodeId(n.NodeId)), ct)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public bool IsTypeOf(NodeId subTypeId, NodeId superTypeId)
        {
            if (subTypeId == superTypeId)
            {
                return true;
            }
            if (!m_refs.TryGet(subTypeId, out List<ReferenceDescription>? references))
            {
                // block - we can throw here but user should load
                references = GetOrAddReferencesAsync(subTypeId, default).AsTask().GetAwaiter()
                    .GetResult();
            }
            subTypeId = GetSuperTypeFromReferences(references);
            return !NodeId.IsNull(subTypeId) && IsTypeOf(subTypeId, superTypeId);
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> GetSuperTypeAsync(NodeId typeId, CancellationToken ct)
        {
            return m_refs.TryGet(typeId, out List<ReferenceDescription>? references)
                ? ValueTask.FromResult(GetSuperTypeFromReferences(references))
                : FindSuperTypeAsyncCore(typeId, ct);

            async ValueTask<NodeId> FindSuperTypeAsyncCore(NodeId typeId, CancellationToken ct)
            {
                List<ReferenceDescription> references = await GetOrAddReferencesAsync(typeId, ct)
                    .ConfigureAwait(false);
                return GetSuperTypeFromReferences(references);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<BuiltInType> GetBuiltInTypeAsync(
            NodeId datatypeId,
            CancellationToken ct)
        {
            NodeId typeId = datatypeId;
            while (!NodeId.IsNull(typeId))
            {
                if (typeId.NamespaceIndex == 0 && typeId.IdType == IdType.Numeric)
                {
                    var id = (BuiltInType)(int)(uint)typeId.Identifier;
                    if (id is > BuiltInType.Null and <= BuiltInType.Enumeration and not BuiltInType.DiagnosticInfo)
                    {
                        return id;
                    }
                }
                typeId = await GetSuperTypeAsync(typeId, ct).ConfigureAwait(false);
            }
            return BuiltInType.Null;
        }

        /// <inheritdoc/>
        public async ValueTask<INode?> GetNodeWithBrowsePathAsync(
            NodeId nodeId,
            QualifiedNameCollection browsePath,
            CancellationToken ct)
        {
            INode? found = null;
            foreach (QualifiedName? browseName in browsePath)
            {
                found = null;
                while (true)
                {
                    if (NodeId.IsNull(nodeId))
                    {
                        // Nothing can be found since there is no
                        return null;
                    }

                    //
                    // Get all hierarchical references of the node and
                    // match browse name
                    //
                    IReadOnlyList<INode> references = await GetReferencesAsync(
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
                            nodeId = ToNodeId(target.NodeId);
                            if (!NodeId.IsNull(nodeId))
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
                    // Try find name in super type
                    nodeId = await GetSuperTypeAsync(nodeId, ct).ConfigureAwait(false);
                }
                nodeId = ToNodeId(found.NodeId);
            }
            return found;
        }

        /// <inheritdoc/>
        public void Clear()
        {
            m_nodes.Clear();
            m_values.Clear();
            m_refs.Clear();
        }

        /// <summary>
        /// Get or add references to cache
        /// </summary>
        private ValueTask<List<ReferenceDescription>> GetOrAddReferencesAsync(
            NodeId nodeId,
            CancellationToken ct)
        {
            Debug.Assert(!NodeId.IsNull(nodeId));
            return m_refs.GetOrAddAsync(
                nodeId,
                async (nodeId, context) =>
                {
                    ReferenceDescriptionCollection references = await context
                        .session.FetchReferencesAsync(nodeId, context.ct)
                        .ConfigureAwait(false);
                    foreach (ReferenceDescription? reference in references)
                    {
                        // transform absolute identifiers.
                        if (reference.NodeId?.IsAbsolute == true)
                        {
                            reference.NodeId = ExpandedNodeId.ToNodeId(
                                reference.NodeId,
                                context.session.NamespaceUris);
                        }
                    }
                    return references;
                },
                (session: Session, ct));
        }

        /// <summary>
        /// Fetch remaining nodes not yet in the result list
        /// </summary>
        private async ValueTask<IReadOnlyList<INode>> FetchRemainingAsync(
            List<NodeId> remainingIds,
            List<INode?> result,
            CancellationToken ct)
        {
            Debug.Assert(result.Count(r => r == null) == remainingIds.Count);

            // fetch nodes and references from server.
            var localIds = new NodeIdCollection(remainingIds);
            (IList<Node>? nodes, IList<ServiceResult>? readErrors) = await Session
                .ReadNodesAsync(localIds, NodeClass.Unspecified, ct: ct)
                .ConfigureAwait(false);

            Debug.Assert(nodes.Count == localIds.Count);
            Debug.Assert(readErrors.Count == localIds.Count);
            int resultMissingIndex = 0;
            for (int index = 0; index < localIds.Count; index++)
            {
                if (!ServiceResult.IsBad(readErrors[index]))
                {
                    m_nodes.AddOrUpdate(remainingIds[index], nodes[index]);
                }
                while (result[resultMissingIndex] != null)
                {
                    resultMissingIndex++;
                    Debug.Assert(resultMissingIndex < result.Count);
                }
                result[resultMissingIndex] = nodes[index];
            }
            Debug.Assert(!result.Any(r => r == null)); // None now should be null
            return result!;
        }

        /// <summary>
        /// Fetch remaining nodes not yet in the result list
        /// </summary>
        private async ValueTask<IReadOnlyList<DataValue>> FetchRemainingAsync(
            List<NodeId> remainingIds,
            List<DataValue?> result,
            CancellationToken ct)
        {
            Debug.Assert(result.Count(r => r == null) == remainingIds.Count);

            // fetch nodes and references from server.
            (DataValueCollection? values, IList<ServiceResult>? readErrors) = await Session
                .ReadValuesAsync(remainingIds, ct: ct)
                .ConfigureAwait(false);

            Debug.Assert(values.Count == remainingIds.Count);
            Debug.Assert(readErrors.Count == remainingIds.Count);
            int resultMissingIndex = 0;
            for (int index = 0; index < remainingIds.Count; index++)
            {
                if (ServiceResult.IsBad(readErrors[index]))
                {
                    values[index].StatusCode = readErrors[index].StatusCode;
                }

                if (StatusCode.IsGood(values[index].StatusCode))
                {
                    // Add to cache
                    m_values.AddOrUpdate(remainingIds[index], values[index]);
                }
                while (result[resultMissingIndex] != null)
                {
                    resultMissingIndex++;
                    Debug.Assert(resultMissingIndex < result.Count);
                }
                result[resultMissingIndex] = values[index];
            }
            Debug.Assert(!result.Any(r => r == null)); // None now should be null
            return result!;
        }

        /// <summary>
        /// Check whether type hierarchy is loaded
        /// </summary>
        private bool IsTypeHierarchyLoaded(IEnumerable<NodeId> typeIds)
        {
            var types = new Queue<NodeId>(typeIds.Where(nodeId => !NodeId.IsNull(nodeId)));
            while (types.TryDequeue(out NodeId? typeId))
            {
                if (!m_refs.TryGet(typeId, out List<ReferenceDescription>? references))
                {
                    return false;
                }
                foreach (ReferenceDescription reference in references)
                {
                    if (reference.ReferenceTypeId == ReferenceTypeIds.HasSubtype &&
                        reference.IsForward &&
                        !reference.NodeId.IsAbsolute)
                    {
                        types.Enqueue(ToNodeId(reference.NodeId));
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Get supertype from references
        /// </summary>
        private NodeId GetSuperTypeFromReferences(List<ReferenceDescription> references)
        {
            return references
                .Where(r => !r.IsForward && r.ReferenceTypeId == ReferenceTypeIds.HasSubtype)
                .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris))
                .DefaultIfEmpty(NodeId.Null)
                .First();
        }

        /// <summary>
        /// Convert to node id from expanded node id if the expanded node id
        /// is not absolute. Otherwise return a null node id
        /// </summary>
        private NodeId ToNodeId(ExpandedNodeId expandedNodeId)
        {
            return expandedNodeId.IsAbsolute
                ? NodeId.Null
                : ExpandedNodeId.ToNodeId(expandedNodeId, Session.NamespaceUris);
        }

        /// <summary>
        /// Node id comparer
        /// </summary>
        internal class Comparers : IEqualityComparer<ExpandedNodeId>, IEqualityComparer<NodeId>
        {
            /// <summary>
            /// Get singleton comparer
            /// </summary>
            public static Comparers Instance { get; } = new Comparers();

            /// <inheritdoc/>
            public bool Equals(ExpandedNodeId? x, ExpandedNodeId? y)
            {
                return ReferenceEquals(x, y) || x == y;
            }

            /// <inheritdoc/>
            public int GetHashCode(ExpandedNodeId obj)
            {
                return obj.GetHashCode();
            }

            /// <inheritdoc/>
            public bool Equals(NodeId? x, NodeId? y)
            {
                return ReferenceEquals(x, y) || x == y;
            }

            /// <inheritdoc/>
            public int GetHashCode(NodeId obj)
            {
                return (obj?.GetHashCode()) ?? 0;
            }
        }

        private readonly IAsyncCache<NodeId, INode> m_nodes;
        private readonly IAsyncCache<NodeId, List<ReferenceDescription>> m_refs;
        private readonly IAsyncCache<NodeId, DataValue> m_values;
    }
}
#endif
