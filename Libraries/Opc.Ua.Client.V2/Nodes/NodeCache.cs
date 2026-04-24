// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes
{
    using Opc.Ua;
    using BitFaster.Caching;
    using BitFaster.Caching.Lfu;
    using BitFaster.Caching.Lru;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Node cache inside the session object. The node cache consists of two
    /// LRU caches. These caches contain nodes and references (Read and browse).
    /// Cache entries expire after not being accessed for a certain time.
    /// The cache is thread-safe and can be accessed by multiple readers.
    /// It is also limited to a capacity at which point least recently used
    /// entries will be evicted.
    /// </summary>
    internal sealed class NodeCache : INodeCache
    {
        /// <summary>
        /// Create cache
        /// </summary>
        /// <param name="session"></param>
        /// <param name="cacheExpiry"></param>
        /// <param name="capacity"></param>
        public NodeCache(INodeCacheContext session, TimeSpan? cacheExpiry = null,
            int capacity = 4096)
        {
            cacheExpiry ??= TimeSpan.FromMinutes(5);

            _session = session;
            _nodes = new ConcurrentLruBuilder<NodeId, INode>()
                   .WithAtomicGetOrAdd()
                   .AsAsyncCache()
                   .WithCapacity(capacity)
                   .WithKeyComparer(Comparers.Instance)
                   .WithExpireAfterAccess(cacheExpiry.Value)
                   .Build();
            _refs = new ConcurrentLruBuilder<NodeId, List<ReferenceDescription>>()
                   .WithAtomicGetOrAdd()
                   .AsAsyncCache()
                   .WithCapacity(capacity)
                   .WithKeyComparer(Comparers.Instance)
                   .WithExpireAfterAccess(cacheExpiry.Value)
                   .Build();
            _values = new ConcurrentLruBuilder<NodeId, DataValue>()
                   .WithAtomicGetOrAdd()
                   .AsAsyncCache()
                   .WithCapacity(capacity)
                   .WithKeyComparer(Comparers.Instance)
                   .WithExpireAfterAccess(cacheExpiry.Value)
                   .Build();
        }

        /// <inheritdoc/>
        public ValueTask<INode> GetNodeAsync(NodeId nodeId, CancellationToken ct)
        {
            if (_nodes.TryGet(nodeId, out var node))
            {
                return ValueTask.FromResult<INode>(node);
            }
            return FindAsyncCore(nodeId, ct);

            ValueTask<INode> FindAsyncCore(NodeId nodeId, CancellationToken ct)
                => _nodes.GetOrAddAsync(nodeId,
                    async (nodeId, context) => await context.session.FetchNodeAsync(
                        null, nodeId, context.ct).ConfigureAwait(false),
                    (session: _session, ct));
        }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<INode>> GetNodesAsync(IReadOnlyList<NodeId> nodeIds,
            CancellationToken ct)
        {
            var count = nodeIds.Count;
            var result = new List<INode?>(nodeIds.Count);
            if (count != 0)
            {
                var notFound = new List<NodeId>();
                foreach (var nodeId in nodeIds)
                {
                    if (_nodes.TryGet(nodeId, out var node))
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
            if (_values.TryGet(nodeId, out var dataValue))
            {
                return ValueTask.FromResult(dataValue);
            }
            return FindAsyncCore(nodeId, ct);

            ValueTask<DataValue> FindAsyncCore(NodeId nodeId, CancellationToken ct)
                => _values.GetOrAddAsync(nodeId,
                    async (nodeId, context) => await context.session.FetchValueAsync(
                        null, nodeId, context.ct).ConfigureAwait(false),
                    (session: _session, ct));
        }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<DataValue>> GetValuesAsync(
            IReadOnlyList<NodeId> nodeIds, CancellationToken ct)
        {
            var count = nodeIds.Count;
            var result = new List<DataValue?>(nodeIds.Count);
            if (count != 0)
            {
                var notFound = new List<NodeId>();
                foreach (var nodeId in nodeIds)
                {
                    if (_values.TryGet(nodeId, out var dataValue))
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
        public ValueTask<IReadOnlyList<INode>> GetReferencesAsync(NodeId nodeId,
            NodeId referenceTypeId, bool isInverse, bool includeSubtypes,
            CancellationToken ct)
        {
            if ((!includeSubtypes || IsTypeHierarchyLoaded([referenceTypeId])) &&
                _refs.TryGet(nodeId, out var references))
            {
                return GetNodesAsync(FilterNodes(references, isInverse, referenceTypeId,
                    includeSubtypes), ct);
            }
            return FindReferencesAsyncCore(nodeId, referenceTypeId, isInverse,
                includeSubtypes, ct);

            async ValueTask<IReadOnlyList<INode>> FindReferencesAsyncCore(NodeId nodeId,
                NodeId referenceTypeId, bool isInverse, bool includeSubtypes,
                CancellationToken ct)
            {
                if (includeSubtypes)
                {
                    await LoadTypeHierarchyAync([referenceTypeId], ct).ConfigureAwait(false);
                }
                var references = await GetOrAddReferencesAsync(nodeId,
                    ct).ConfigureAwait(false);
                return await GetNodesAsync(FilterNodes(references, isInverse, referenceTypeId,
                    includeSubtypes), ct).ConfigureAwait(false);
            }

            List<NodeId> FilterNodes(IEnumerable<ReferenceDescription> references,
                bool isInverse, NodeId refTypeId, bool includeSubtypes)
            {
                return references
                    .Where(r => r.IsForward == !isInverse &&
                        (r.ReferenceTypeId == refTypeId ||
                            (includeSubtypes && IsTypeOf(r.ReferenceTypeId, refTypeId))))
                    .Select(r => ToNodeId(r.NodeId))
                    .Where(n => !NodeId.IsNull(n))
                    .ToList();
            }
        }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<INode>> GetReferencesAsync(
            IReadOnlyList<NodeId> nodeIds, IReadOnlyList<NodeId> referenceTypeIds,
            bool isInverse, bool includeSubtypes, CancellationToken ct)
        {
            var targetIds = new List<NodeId>();
            var notFound = new List<NodeId>();
            if (includeSubtypes && !IsTypeHierarchyLoaded(referenceTypeIds))
            {
                return FindReferencesAsyncCore(notFound, referenceTypeIds, isInverse,
                    includeSubtypes, targetIds, ct);
            }
            foreach (var nodeId in nodeIds)
            {
                if (NodeId.IsNull(nodeId))
                {
                    continue;
                }
                if (_refs.TryGet(nodeId, out var references))
                {
                    targetIds.AddRange(FilterNodes(references, isInverse, referenceTypeIds,
                        includeSubtypes));
                }
                else
                {
                    notFound.Add(nodeId);
                }
            }
            if (notFound.Count != 0)
            {
                return FindReferencesAsyncCore(notFound, referenceTypeIds, isInverse,
                    includeSubtypes, targetIds, ct);
            }
            return GetNodesAsync(targetIds, ct);

            async ValueTask<IReadOnlyList<INode>> FindReferencesAsyncCore(
                IReadOnlyList<NodeId> nodeIds, IReadOnlyList<NodeId> referenceTypeIds,
                bool isInverse, bool includeSubtypes, List<NodeId> targetIds,
                CancellationToken ct)
            {
                if (includeSubtypes)
                {
                    await LoadTypeHierarchyAync(referenceTypeIds, ct).ConfigureAwait(false);
                }
                foreach (var nodeId in nodeIds)
                {
                    var references = await GetOrAddReferencesAsync(nodeId,
                        ct).ConfigureAwait(false);
                    targetIds.AddRange(FilterNodes(references, isInverse, referenceTypeIds,
                        includeSubtypes));
                }
                return await GetNodesAsync(targetIds, ct).ConfigureAwait(false);
            }
            List<NodeId> FilterNodes(IEnumerable<ReferenceDescription> references,
                bool isInverse, IReadOnlyList<NodeId> referenceTypeIds, bool includeSubtypes)
            {
                return references
                    .Where(r => r.IsForward == !isInverse &&
                        referenceTypeIds.Any(refTypeId => r.ReferenceTypeId == refTypeId ||
                            (includeSubtypes && IsTypeOf(r.ReferenceTypeId, refTypeId))))
                    .Select(r => ToNodeId(r.NodeId))
                    .Where(n => !NodeId.IsNull(n))
                    .ToList();
            }
        }

        /// <inheritdoc/>
        public async ValueTask LoadTypeHierarchyAync(IReadOnlyList<NodeId> typeIds,
            CancellationToken ct)
        {
            var nodes = await GetReferencesAsync(typeIds,
            [
                ReferenceTypeIds.HasSubtype
            ], false, false, ct).ConfigureAwait(false);
            if (nodes.Count > 0)
            {
                if (nodes is not List<INode> subTypes)
                {
                    subTypes = [.. nodes];
                }
                await LoadTypeHierarchyAync(subTypes.ConvertAll(n => ToNodeId(n.NodeId)),
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public bool IsTypeOf(NodeId subTypeId, NodeId superTypeId)
        {
            if (subTypeId == superTypeId)
            {
                return true;
            }
            if (!_refs.TryGet(subTypeId, out var references))
            {
                // block - we can throw here but user should load
                references = GetOrAddReferencesAsync(subTypeId, default)
                    .AsTask().GetAwaiter().GetResult();
            }
            subTypeId = GetSuperTypeFromReferences(references);
            if (!NodeId.IsNull(subTypeId))
            {
                return IsTypeOf(subTypeId, superTypeId);
            }
            return false;
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> GetSuperTypeAsync(NodeId typeId,
            CancellationToken ct)
        {
            if (_refs.TryGet(typeId, out var references))
            {
                return ValueTask.FromResult(GetSuperTypeFromReferences(references));
            }
            return FindSuperTypeAsyncCore(typeId, ct);
            async ValueTask<NodeId> FindSuperTypeAsyncCore(NodeId typeId,
                CancellationToken ct)
            {
                var references = await GetOrAddReferencesAsync(typeId,
                    ct).ConfigureAwait(false);
                return GetSuperTypeFromReferences(references);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<BuiltInType> GetBuiltInTypeAsync(NodeId datatypeId,
            CancellationToken ct)
        {
            var typeId = datatypeId;
            while (!Opc.Ua.NodeId.IsNull(typeId))
            {
                if (typeId.NamespaceIndex == 0 && typeId.IdType == Opc.Ua.IdType.Numeric)
                {
                    var id = (BuiltInType)(int)(uint)typeId.Identifier;
                    if (id is > BuiltInType.Null and
                        <= BuiltInType.Enumeration and
                        not BuiltInType.DiagnosticInfo)
                    {
                        return id;
                    }
                }
                typeId = await GetSuperTypeAsync(typeId, ct).ConfigureAwait(false);
            }
            return BuiltInType.Null;
        }

        /// <inheritdoc/>
        public async ValueTask<INode?> GetNodeWithBrowsePathAsync(NodeId nodeId,
            QualifiedNameCollection browsePath, CancellationToken ct)
        {
            INode? found = null;
            foreach (var browseName in browsePath)
            {
                found = null;
                while (true)
                {
                    if (Opc.Ua.NodeId.IsNull(nodeId))
                    {
                        // Nothing can be found since there is no
                        return null;
                    }

                    //
                    // Get all hierarchical references of the node and
                    // match browse name
                    //
                    var references = await GetReferencesAsync(nodeId,
                        ReferenceTypeIds.HierarchicalReferences,
                        false, true, ct).ConfigureAwait(false);
                    foreach (var target in references)
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
                    nodeId = await GetSuperTypeAsync(nodeId,
                        ct).ConfigureAwait(false);
                }
                nodeId = ToNodeId(found.NodeId);
            }
            return found;
        }

        /// <inheritdoc/>
        public void Clear()
        {
            _nodes.Clear();
            _values.Clear();
            _refs.Clear();
        }

        /// <summary>
        /// Get or add references to cache
        /// </summary>
        /// <param name="nodeId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private ValueTask<List<ReferenceDescription>> GetOrAddReferencesAsync(
            NodeId nodeId, CancellationToken ct)
        {
            Debug.Assert(!NodeId.IsNull(nodeId));
            return _refs.GetOrAddAsync(nodeId, async (nodeId, context) =>
            {
                var references = await context.session.FetchReferencesAsync(
                    null, nodeId, context.ct).ConfigureAwait(false);
                foreach (var reference in references)
                {
                    // transform absolute identifiers.
                    if (reference.NodeId?.IsAbsolute == true)
                    {
                        reference.NodeId = ExpandedNodeId.ToNodeId(
                            reference.NodeId, context.session.NamespaceUris);
                    }
                }
                return references;
            }, (session: _session, ct));
        }

        /// <summary>
        /// Fetch remaining nodes not yet in the result list
        /// </summary>
        /// <param name="remainingIds"></param>
        /// <param name="result"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async ValueTask<IReadOnlyList<INode>> FetchRemainingAsync(
            List<NodeId> remainingIds, List<INode?> result, CancellationToken ct)
        {
            Debug.Assert(result.Count(r => r == null) == remainingIds.Count);

            // fetch nodes and references from server.
            var localIds = new NodeIdCollection(remainingIds);
            (var nodes, var readErrors) = await _session.FetchNodesAsync(
                null, localIds, ct).ConfigureAwait(false);

            Debug.Assert(nodes.Count == localIds.Count);
            Debug.Assert(readErrors.Count == localIds.Count);
            var resultMissingIndex = 0;
            for (var index = 0; index < localIds.Count; index++)
            {
                if (!ServiceResult.IsBad(readErrors[index]))
                {
                    _nodes.AddOrUpdate(remainingIds[index], nodes[index]);
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
        /// <param name="remainingIds"></param>
        /// <param name="result"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async ValueTask<IReadOnlyList<DataValue>> FetchRemainingAsync(
            List<NodeId> remainingIds, List<DataValue?> result, CancellationToken ct)
        {
            Debug.Assert(result.Count(r => r == null) == remainingIds.Count);

            // fetch nodes and references from server.
            (var values, var readErrors) = await _session.FetchValuesAsync(null,
                remainingIds, ct).ConfigureAwait(false);

            Debug.Assert(values.Count == remainingIds.Count);
            Debug.Assert(readErrors.Count == remainingIds.Count);
            var resultMissingIndex = 0;
            for (var index = 0; index < remainingIds.Count; index++)
            {
                if (ServiceResult.IsBad(readErrors[index]))
                {
                    values[index].StatusCode = readErrors[index].StatusCode;
                }

                if (StatusCode.IsGood(values[index].StatusCode))
                {
                    // Add to cache
                    _values.AddOrUpdate(remainingIds[index], values[index]);
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
        /// <param name="typeIds"></param>
        /// <returns></returns>
        private bool IsTypeHierarchyLoaded(IEnumerable<NodeId> typeIds)
        {
            var types = new Queue<NodeId>(typeIds.Where(nodeId => !NodeId.IsNull(nodeId)));
            while (types.TryDequeue(out var typeId))
            {
                if (!_refs.TryGet(typeId, out var references))
                {
                    return false;
                }
                foreach (var reference in references)
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
        /// <param name="references"></param>
        /// <returns></returns>
        private NodeId GetSuperTypeFromReferences(List<ReferenceDescription> references)
        {
            return references
                .Where(r => !r.IsForward &&
                    r.ReferenceTypeId == ReferenceTypeIds.HasSubtype)
                .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, _session.NamespaceUris))
                .DefaultIfEmpty(NodeId.Null)
                .First();
        }

        /// <summary>
        /// Convert to node id from expanded node id if the expanded node id
        /// is not absolute. Otherwise return a null node id
        /// </summary>
        /// <param name="expandedNodeId"></param>
        /// <returns></returns>
        private NodeId ToNodeId(ExpandedNodeId expandedNodeId)
        {
            if (expandedNodeId.IsAbsolute)
            {
                return NodeId.Null;
            }
            return ExpandedNodeId.ToNodeId(expandedNodeId, _session.NamespaceUris);
        }

        private readonly INodeCacheContext _session;
        private readonly IAsyncCache<NodeId, INode> _nodes;
        private readonly IAsyncCache<NodeId, List<ReferenceDescription>> _refs;
        private readonly IAsyncCache<NodeId, DataValue> _values;
    }
}
