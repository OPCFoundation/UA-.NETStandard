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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BitFaster.Caching;
using BitFaster.Caching.Lfu;
using BitFaster.Caching.Lru;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;

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
    public sealed class LruNodeCache : ILruNodeCache, INodeCache, IDisposable
    {
        /// <summary>
        /// Create cache
        /// </summary>
        public LruNodeCache(
            INodeCacheContext context,
            ITelemetryContext telemetry,
            TimeSpan? cacheExpiry = null,
            int capacity = 4096)
        {
            m_context = context;
            m_logger = telemetry.CreateLogger<LruNodeCache>();
            cacheExpiry ??= TimeSpan.FromMinutes(5);

            BitFaster.Caching.Lru.Builder.AtomicAsyncConcurrentLruBuilder<
                NodeId,
                INode
            > nodesBuilder = new ConcurrentLruBuilder<NodeId, INode>()
                .WithAtomicGetOrAdd()
                .AsAsyncCache()
                .WithCapacity(capacity)
                .WithKeyComparer(NodeIdComparer.Default)
                .WithExpireAfterAccess(cacheExpiry.Value)
                .WithMetrics();
            BitFaster.Caching.Lru.Builder.AtomicAsyncConcurrentLruBuilder<
                NodeId,
                ArrayOf<ReferenceDescription>
            > refsBuilder = new ConcurrentLruBuilder<NodeId, ArrayOf<ReferenceDescription>>()
                .WithAtomicGetOrAdd()
                .AsAsyncCache()
                .WithCapacity(capacity)
                .WithKeyComparer(NodeIdComparer.Default)
                .WithExpireAfterAccess(cacheExpiry.Value)
                .WithMetrics();
            BitFaster.Caching.Lru.Builder.AtomicAsyncConcurrentLruBuilder<
                NodeId,
                DataValue
            > valuesBuilder = new ConcurrentLruBuilder<NodeId, DataValue>()
                .WithAtomicGetOrAdd()
                .AsAsyncCache()
                .WithCapacity(capacity)
                .WithKeyComparer(NodeIdComparer.Default)
                .WithExpireAfterAccess(cacheExpiry.Value)
                .WithMetrics();
            m_nodes = nodesBuilder.Build();
            m_values = valuesBuilder.Build();
            m_refs = refsBuilder.Build();

            m_meter = telemetry.CreateMeter();
            RegisterMetrics(m_meter, "nodes", m_nodes);
            RegisterMetrics(m_meter, "values", m_values);
            RegisterMetrics(m_meter, "references", m_refs);
        }

        /// <summary>
        /// Dispose the underlying meter.
        /// </summary>
        public void Dispose()
        {
            m_meter.Dispose();
        }

        private static void RegisterMetrics<TKey, TValue>(
            Meter meter,
            string cacheTag,
            IAsyncCache<TKey, TValue> cache)
            where TKey : notnull
        {
            KeyValuePair<string, object?>[] tag =
            [
                new KeyValuePair<string, object?>("cache", cacheTag)
            ];

            meter.CreateObservableCounter(
                "opcua.client.nodecache.hits",
                () =>
                {
                    Optional<ICacheMetrics> metrics = cache.Metrics;
                    return new Measurement<long>(metrics.HasValue ? metrics.Value!.Hits : 0, tag);
                },
                description: "Number of cache hits in the OPC UA client node cache.");

            meter.CreateObservableCounter(
                "opcua.client.nodecache.misses",
                () =>
                {
                    Optional<ICacheMetrics> metrics = cache.Metrics;
                    return new Measurement<long>(metrics.HasValue ? metrics.Value!.Misses : 0, tag);
                },
                description: "Number of cache misses in the OPC UA client node cache.");

            meter.CreateObservableCounter(
                "opcua.client.nodecache.evictions",
                () =>
                {
                    Optional<ICacheMetrics> metrics = cache.Metrics;
                    return new Measurement<long>(metrics.HasValue ? metrics.Value!.Evicted : 0, tag);
                },
                description: "Number of evictions from the OPC UA client node cache.");

            meter.CreateObservableGauge(
                "opcua.client.nodecache.size",
                () => new Measurement<long>(cache.Count, tag),
                description: "Current number of entries in the OPC UA client node cache.");
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
        public NamespaceTable NamespaceUris => m_context.NamespaceUris;

        /// <inheritdoc/>
        public ValueTask<INode> GetNodeAsync(NodeId nodeId, CancellationToken ct)
        {
            return m_nodes.TryGet(nodeId, out INode? node)
                ? new ValueTask<INode>(node)
                : FindAsyncCore(nodeId, ct);
            ValueTask<INode> FindAsyncCore(NodeId nodeId, CancellationToken ct)
            {
                return m_nodes.GetOrAddAsync(
                    nodeId,
                    async key =>
                    {
                        Node node = await m_context.FetchNodeAsync(
                            null,
                            key,
                            NodeClass.Unspecified,
                            ct: ct)
                            .ConfigureAwait(false);
                        // Populate the node's ReferenceTable so callers can
                        // introspect references without an extra round trip.
                        // Mirrors legacy NodeCache behavior.
                        await PopulateReferenceTableAsync(key, node, ct)
                            .ConfigureAwait(false);
                        return node;
                    });
            }
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<INode>> GetNodesAsync(
            ArrayOf<NodeId> nodeIds,
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
            return new ValueTask<ArrayOf<INode>>(result.ToArrayOf()!);
        }

        /// <inheritdoc/>
        public ValueTask<DataValue> GetValueAsync(NodeId nodeId, CancellationToken ct)
        {
            return m_values.TryGet(nodeId, out DataValue? dataValue)
                ? new ValueTask<DataValue>(dataValue)
                : FindAsyncCore(nodeId, ct);
            ValueTask<DataValue> FindAsyncCore(NodeId nodeId, CancellationToken ct)
            {
                INodeCacheContext context = m_context;
                return m_values.GetOrAddAsync(
                    nodeId,
                    async key => await context.FetchValueAsync(null, key, ct)
                        .ConfigureAwait(false));
            }
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<DataValue>> GetValuesAsync(
            ArrayOf<NodeId> nodeIds,
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
            return new ValueTask<ArrayOf<DataValue>>(result.ToArrayOf()!);
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<INode>> GetReferencesAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            CancellationToken ct)
        {
            return
                (!includeSubtypes || IsTypeHierarchyLoaded([referenceTypeId])) &&
                m_refs.TryGet(nodeId, out ArrayOf<ReferenceDescription> references)
                ? GetNodesAsync(
                    FilterNodes(references, isInverse, referenceTypeId, includeSubtypes),
                    ct)
                : FindReferencesAsyncCore(nodeId, referenceTypeId, isInverse, includeSubtypes, ct);

            async ValueTask<ArrayOf<INode>> FindReferencesAsyncCore(
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
                ArrayOf<ReferenceDescription> references = await GetOrAddReferencesAsync(nodeId, ct)
                    .ConfigureAwait(false);
                return await GetNodesAsync(
                    FilterNodes(references, isInverse, referenceTypeId, includeSubtypes),
                    ct)
                    .ConfigureAwait(false);
            }

            ArrayOf<NodeId> FilterNodes(
                ArrayOf<ReferenceDescription> references,
                bool isInverse,
                NodeId refTypeId,
                bool includeSubtypes)
            {
                return references
                    .Filter(r =>
                        r.IsForward == !isInverse &&
                        (
                            r.ReferenceTypeId == refTypeId ||
                            (includeSubtypes && IsTypeOf(r.ReferenceTypeId, refTypeId))))
                    .ConvertAll(r => ToNodeId(r.NodeId))
                    .Filter(n => !n.IsNull);
            }
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<INode>> GetReferencesAsync(
            ArrayOf<NodeId> nodeIds,
            ArrayOf<NodeId> referenceTypeIds,
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
                if (nodeId.IsNull)
                {
                    continue;
                }
                if (m_refs.TryGet(nodeId, out ArrayOf<ReferenceDescription> references))
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
                : GetNodesAsync(targetIds.ToArrayOf(), ct);

            async ValueTask<ArrayOf<INode>> FindReferencesAsyncCore(
                List<NodeId> nodeIds,
                ArrayOf<NodeId> referenceTypeIds,
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
                    ArrayOf<ReferenceDescription> references = await GetOrAddReferencesAsync(
                        nodeId,
                        ct)
                        .ConfigureAwait(false);
                    targetIds.AddRange(
                        FilterNodes(references, isInverse, referenceTypeIds, includeSubtypes));
                }
                return await GetNodesAsync(targetIds.ToArrayOf(), ct).ConfigureAwait(false);
            }
            ArrayOf<NodeId> FilterNodes(
                ArrayOf<ReferenceDescription> references,
                bool isInverse,
                ArrayOf<NodeId> referenceTypeIds,
                bool includeSubtypes)
            {
                return references
                    .Filter(r =>
                        r.IsForward == !isInverse &&
                        !referenceTypeIds.Find(refTypeId =>
                            r.ReferenceTypeId == refTypeId ||
                            (includeSubtypes && IsTypeOf(r.ReferenceTypeId, refTypeId))).IsNull)
                    .ConvertAll(r => ToNodeId(r.NodeId))
                    .Filter(n => !n.IsNull);
            }
        }

        /// <inheritdoc/>
        public async ValueTask LoadTypeHierarchyAsync(
            ArrayOf<NodeId> typeIds,
            CancellationToken ct)
        {
            ArrayOf<INode> nodes = await GetReferencesAsync(
                typeIds,
                [ReferenceTypeIds.HasSubtype],
                false,
                false,
                ct)
            .ConfigureAwait(false);
            if (nodes.Count > 0)
            {
                await LoadTypeHierarchyAsync(nodes.ConvertAll(n => ToNodeId(n.NodeId)), ct)
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
            if (!m_refs.TryGet(subTypeId, out ArrayOf<ReferenceDescription> references))
            {
                // block - we can throw here but user should load
                references = GetOrAddReferencesAsync(subTypeId, default).AsTask().GetAwaiter()
                    .GetResult();
            }
            subTypeId = GetSuperTypeFromReferences(references);
            return !subTypeId.IsNull && IsTypeOf(subTypeId, superTypeId);
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> GetSuperTypeAsync(NodeId typeId, CancellationToken ct)
        {
            return m_refs.TryGet(typeId, out ArrayOf<ReferenceDescription> references)
                ? new ValueTask<NodeId>(GetSuperTypeFromReferences(references))
                : FindSuperTypeAsyncCore(typeId, ct);

            async ValueTask<NodeId> FindSuperTypeAsyncCore(NodeId typeId, CancellationToken ct)
            {
                ArrayOf<ReferenceDescription> references = await GetOrAddReferencesAsync(typeId, ct)
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
                typeId = await GetSuperTypeAsync(typeId, ct).ConfigureAwait(false);
            }
            return BuiltInType.Null;
        }

        /// <inheritdoc/>
        public async ValueTask<INode?> GetNodeWithBrowsePathAsync(
            NodeId nodeId,
            ArrayOf<QualifiedName> browsePath,
            CancellationToken ct)
        {
            INode? found = null;
            foreach (QualifiedName browseName in browsePath.ToList())
            {
                found = null;
                while (true)
                {
                    if (nodeId.IsNull)
                    {
                        // Nothing can be found since there is nothing to start
                        return null;
                    }

                    //
                    // Get all hierarchical references of the node and
                    // match browse name
                    //
                    ArrayOf<INode> references = await GetReferencesAsync(
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

        // IAsyncNodeTable / IAsyncTypeTable / INodeCache implementation

        /// <inheritdoc/>
        public StringTable ServerUris => m_context.ServerUris;

        /// <inheritdoc/>
        IAsyncTypeTable IAsyncNodeTable.TypeTree => this;

        /// <summary>
        /// Legacy synchronous type table adapter for backwards compatibility with
        /// callers that expect <see cref="ITypeTable"/>.
        /// </summary>
        public ITypeTable TypeTree => this.AsTypeTable();

        /// <inheritdoc/>
        public async ValueTask<bool> ExistsAsync(ExpandedNodeId nodeId, CancellationToken ct)
        {
            return await FindAsync(nodeId, ct).ConfigureAwait(false) != null;
        }

        /// <inheritdoc/>
        public async ValueTask<INode?> FindAsync(ExpandedNodeId nodeId, CancellationToken ct)
        {
            if (nodeId.IsNull)
            {
                return null;
            }
            var localId = ExpandedNodeId.ToNodeId(nodeId, NamespaceUris);
            if (localId.IsNull)
            {
                return null;
            }
            try
            {
                return await GetNodeAsync(localId, ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    "Could not find node {NodeId}: {Error}",
                    nodeId,
                    Redact.Create(e));
                return null;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<INode?> FindAsync(
            ExpandedNodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            QualifiedName browseName,
            CancellationToken ct)
        {
            NodeId localSourceId = ToNodeId(sourceId);
            if (localSourceId.IsNull)
            {
                return null;
            }
            ArrayOf<INode> nodes = await GetReferencesAsync(
                localSourceId, referenceTypeId, isInverse, includeSubtypes, ct)
                .ConfigureAwait(false);
            foreach (INode node in nodes.ToList())
            {
                if (node.BrowseName == browseName)
                {
                    return node;
                }
            }
            return null;
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<INode>> FindAsync(
            ExpandedNodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            CancellationToken ct)
        {
            NodeId localSourceId = ToNodeId(sourceId);
            if (localSourceId.IsNull)
            {
                return new List<INode>();
            }
            return await GetReferencesAsync(
                localSourceId, referenceTypeId, isInverse, includeSubtypes, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<ArrayOf<INode?>> FindAsync(
            ArrayOf<ExpandedNodeId> nodeIds,
            CancellationToken ct)
        {
            if (nodeIds.IsEmpty)
            {
                return [];
            }
            var result = new List<INode?>(nodeIds.Count);
            foreach (ExpandedNodeId nodeId in nodeIds.ToList())
            {
                result.Add(await FindAsync(nodeId, ct).ConfigureAwait(false));
            }
            return result;
        }

        /// <summary>
        /// Populate a node's <see cref="Node.ReferenceTable"/> from references
        /// fetched from the server. Mirrors legacy NodeCache behavior so callers
        /// can introspect references without an extra round trip.
        /// </summary>
        private async ValueTask PopulateReferenceTableAsync(
            NodeId localId,
            Node node,
            CancellationToken ct)
        {
            try
            {
                ArrayOf<ReferenceDescription> references = await m_context
                    .FetchReferencesAsync(null, localId, ct)
                    .ConfigureAwait(false);
                foreach (ReferenceDescription reference in references)
                {
                    ExpandedNodeId targetId = reference.NodeId;
                    if (targetId.IsAbsolute)
                    {
                        targetId = ExpandedNodeId.ToNodeId(targetId, NamespaceUris);
                    }
                    node.ReferenceTable.Add(
                        reference.ReferenceTypeId,
                        !reference.IsForward,
                        targetId);
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    "Could not fetch references for node {NodeId}: {Error}",
                    localId,
                    Redact.Create(e));
            }
        }

        /// <inheritdoc/>
        public async Task<Node?> FetchNodeAsync(ExpandedNodeId nodeId, CancellationToken ct)
        {
            var localId = ExpandedNodeId.ToNodeId(nodeId, NamespaceUris);
            if (localId.IsNull)
            {
                return null;
            }
            try
            {
                // Force-refresh semantics: fetch directly from the server. We do not
                // update the LRU cache here because BitFaster's atomic-async LRU
                // exhibits hang behavior with sequential AddOrUpdate/GetOrAddAsync.
                Node node = await m_context.FetchNodeAsync(null, localId, ct: ct)
                    .ConfigureAwait(false);
                await PopulateReferenceTableAsync(localId, node, ct).ConfigureAwait(false);
                return node;
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    "Could not fetch node {NodeId}: {Error}",
                    nodeId,
                    Redact.Create(e));
                return null;
            }
        }

        /// <inheritdoc/>
        public async Task<ArrayOf<Node?>> FetchNodesAsync(
            ArrayOf<ExpandedNodeId> nodeIds,
            CancellationToken ct)
        {
            if (nodeIds.IsEmpty)
            {
                return [];
            }
            ArrayOf<NodeId> localIds =
                nodeIds.ConvertAll(id => ExpandedNodeId.ToNodeId(id, NamespaceUris));
            (ArrayOf<Node> nodes, ArrayOf<ServiceResult> errors) =
                await m_context.FetchNodesAsync(null, localIds, ct: ct)
                    .ConfigureAwait(false);

            // Populate each node's ReferenceTable so callers can introspect
            // references without extra round trips. Mirrors legacy NodeCache.
            (ArrayOf<ArrayOf<ReferenceDescription>> referenceList, ArrayOf<ServiceResult> refErrors) =
                await m_context.FetchReferencesAsync(null, localIds, ct).ConfigureAwait(false);

            var result = new List<Node?>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (ServiceResult.IsBad(errors[i]))
                {
                    result.Add(null);
                    continue;
                }
                if (!ServiceResult.IsBad(refErrors[i]))
                {
                    foreach (ReferenceDescription reference in referenceList[i])
                    {
                        ExpandedNodeId targetId = reference.NodeId;
                        if (targetId.IsAbsolute)
                        {
                            targetId = ExpandedNodeId.ToNodeId(targetId, NamespaceUris);
                        }
                        nodes[i].ReferenceTable.Add(
                            reference.ReferenceTypeId,
                            !reference.IsForward,
                            targetId);
                    }
                }
                result.Add(nodes[i]);
            }
            return result;
        }

        /// <inheritdoc/>
        public async Task FetchSuperTypesAsync(ExpandedNodeId nodeId, CancellationToken ct)
        {
            NodeId current = ToNodeId(nodeId);
            while (!current.IsNull)
            {
                current = await GetSuperTypeAsync(current, ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public Task<ArrayOf<INode>> FindReferencesAsync(
            ExpandedNodeId nodeId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            CancellationToken ct)
        {
            NodeId localId = ToNodeId(nodeId);
            if (localId.IsNull)
            {
                return Task.FromResult<ArrayOf<INode>>(new List<INode>());
            }
            return GetReferencesAsync(localId, referenceTypeId, isInverse, includeSubtypes, ct)
                .AsTask();
        }

        /// <inheritdoc/>
        public Task<ArrayOf<INode>> FindReferencesAsync(
            ArrayOf<ExpandedNodeId> nodeIds,
            ArrayOf<NodeId> referenceTypeIds,
            bool isInverse,
            bool includeSubtypes,
            CancellationToken ct)
        {
            ArrayOf<NodeId> localIds = nodeIds.ConvertAll(ToNodeId);
            return GetReferencesAsync(localIds, referenceTypeIds, isInverse, includeSubtypes, ct)
                .AsTask();
        }

        /// <inheritdoc/>
        public void LoadUaDefinedTypes(ISystemContext context)
        {
            // The LRU cache is lazy and populates on demand; no pre-loading is needed.
            m_logger.LogDebug(
                "LoadUaDefinedTypes called; the LRU node cache populates on demand.");
        }

        /// <inheritdoc/>
        public async ValueTask<bool> IsKnownAsync(ExpandedNodeId typeId, CancellationToken ct)
        {
            return await FindAsync(typeId, ct).ConfigureAwait(false) != null;
        }

        /// <inheritdoc/>
        public async ValueTask<bool> IsKnownAsync(NodeId typeId, CancellationToken ct)
        {
            if (typeId.IsNull)
            {
                return false;
            }
            try
            {
                await GetNodeAsync(typeId, ct).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> FindSuperTypeAsync(ExpandedNodeId typeId, CancellationToken ct)
        {
            return GetSuperTypeAsync(ToNodeId(typeId), ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> FindSuperTypeAsync(NodeId typeId, CancellationToken ct)
        {
            return GetSuperTypeAsync(typeId, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<NodeId>> FindSubTypesAsync(
            ExpandedNodeId typeId,
            CancellationToken ct)
        {
            NodeId localId = ToNodeId(typeId);
            if (localId.IsNull)
            {
                return new List<NodeId>();
            }
            ArrayOf<INode> subtypes = await GetReferencesAsync(
                localId, ReferenceTypeIds.HasSubtype, false, false, ct)
                .ConfigureAwait(false);
            return subtypes.ConvertAll(n => ToNodeId(n.NodeId)).Filter(n => !n.IsNull);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> IsTypeOfAsync(
            ExpandedNodeId subTypeId,
            ExpandedNodeId superTypeId,
            CancellationToken ct)
        {
            if (subTypeId == superTypeId)
            {
                return true;
            }
            NodeId localSubTypeId = ToNodeId(subTypeId);
            NodeId localSuperTypeId = ToNodeId(superTypeId);
            if (localSubTypeId.IsNull || localSuperTypeId.IsNull)
            {
                return false;
            }
            return await IsTypeOfAsync(localSubTypeId, localSuperTypeId, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> IsTypeOfAsync(
            NodeId subTypeId,
            NodeId superTypeId,
            CancellationToken ct)
        {
            if (subTypeId == superTypeId)
            {
                return true;
            }
            NodeId current = subTypeId;
            while (!current.IsNull)
            {
                NodeId superType = await GetSuperTypeAsync(current, ct).ConfigureAwait(false);
                if (superType.IsNull)
                {
                    break;
                }
                if (superType == superTypeId)
                {
                    return true;
                }
                current = superType;
            }
            return false;
        }

        /// <inheritdoc/>
        public async ValueTask<QualifiedName> FindReferenceTypeNameAsync(
            NodeId referenceTypeId,
            CancellationToken ct)
        {
            if (referenceTypeId.IsNull)
            {
                return QualifiedName.Null;
            }
            try
            {
                INode node = await GetNodeAsync(referenceTypeId, ct).ConfigureAwait(false);
                return node.BrowseName;
            }
            catch
            {
                return QualifiedName.Null;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> FindReferenceTypeAsync(
            QualifiedName browseName,
            CancellationToken ct)
        {
            if (browseName.IsNull)
            {
                return NodeId.Null;
            }
            return await FindReferenceTypeInHierarchyAsync(
                ReferenceTypeIds.References, browseName, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> IsEncodingOfAsync(
            ExpandedNodeId encodingId,
            ExpandedNodeId datatypeId,
            CancellationToken ct)
        {
            NodeId localEncodingId = ToNodeId(encodingId);
            if (localEncodingId.IsNull)
            {
                return false;
            }
            NodeId localDatatypeId = ToNodeId(datatypeId);
            // DataType --HasEncoding--> Encoding: inverse from encoding gives datatype
            ArrayOf<INode> dataTypes = await GetReferencesAsync(
                localEncodingId, ReferenceTypeIds.HasEncoding, true, false, ct)
                .ConfigureAwait(false);
            foreach (INode dataType in dataTypes.ToList())
            {
                if (ToNodeId(dataType.NodeId) == localDatatypeId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc/>
        public async ValueTask<bool> IsEncodingForAsync(
            NodeId expectedTypeId,
            ExtensionObject value,
            CancellationToken ct)
        {
            if (value.IsNull)
            {
                return false;
            }
            NodeId encodingId = ToNodeId(value.TypeId);
            if (encodingId.IsNull)
            {
                return false;
            }
            if (expectedTypeId == encodingId)
            {
                return true;
            }
            // DataType --HasEncoding--> Encoding: inverse from encoding gives datatype
            ArrayOf<INode> dataTypes = await GetReferencesAsync(
                encodingId, ReferenceTypeIds.HasEncoding, true, false, ct)
                .ConfigureAwait(false);
            foreach (INode dataType in dataTypes.ToList())
            {
                if (ToNodeId(dataType.NodeId) == expectedTypeId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc/>
        public async ValueTask<bool> IsEncodingForAsync(
            NodeId expectedTypeId,
            Variant value,
            CancellationToken ct)
        {
            if (value.IsNull)
            {
                return false;
            }
            if (expectedTypeId.IsNull)
            {
                return true;
            }
            NodeId actualTypeId = TypeInfo.GetDataTypeId(value, m_context.NamespaceUris);
            if (await IsTypeOfAsync(actualTypeId, expectedTypeId, ct).ConfigureAwait(false))
            {
                return true;
            }
            // allow matches for non-structure values where actual type is a supertype of expected
            if (actualTypeId != DataTypes.Structure)
            {
                return await IsTypeOfAsync(expectedTypeId, actualTypeId, ct)
                    .ConfigureAwait(false);
            }
            if (value.TryGet(out ExtensionObject extension))
            {
                return await IsEncodingForAsync(expectedTypeId, extension, ct)
                    .ConfigureAwait(false);
            }
            if (value.TryGet(out ArrayOf<ExtensionObject> extensions))
            {
                foreach (ExtensionObject ext in extensions.ToList())
                {
                    if (!await IsEncodingForAsync(expectedTypeId, ext, ct)
                        .ConfigureAwait(false))
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> FindDataTypeIdAsync(
            ExpandedNodeId encodingId,
            CancellationToken ct)
        {
            NodeId localId = ToNodeId(encodingId);
            if (localId.IsNull)
            {
                return NodeId.Null;
            }
            return await FindDataTypeIdAsync(localId, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> FindDataTypeIdAsync(
            NodeId encodingId,
            CancellationToken ct)
        {
            if (encodingId.IsNull)
            {
                return NodeId.Null;
            }
            // DataType --HasEncoding--> Encoding: inverse from encoding gives datatype
            ArrayOf<INode> dataTypes = await GetReferencesAsync(
                encodingId, ReferenceTypeIds.HasEncoding, true, false, ct)
                .ConfigureAwait(false);
            if (dataTypes.Count > 0)
            {
                return ToNodeId(dataTypes[0].NodeId);
            }
            return NodeId.Null;
        }

        /// <inheritdoc/>
        public ValueTask<string?> GetDisplayTextAsync(INode node, CancellationToken ct)
        {
            if (node == null)
            {
                return new ValueTask<string?>(string.Empty);
            }
            string? text = node.DisplayName.Text ?? node.BrowseName.Name ?? string.Empty;
            return new ValueTask<string?>(text);
        }

        /// <inheritdoc/>
        public async ValueTask<string?> GetDisplayTextAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct)
        {
            if (nodeId.IsNull)
            {
                return string.Empty;
            }
            INode? node = await FindAsync(nodeId, ct).ConfigureAwait(false);
            if (node != null)
            {
                return await GetDisplayTextAsync(node, ct).ConfigureAwait(false);
            }
            return Utils.Format("{0}", nodeId);
        }

        /// <inheritdoc/>
        public ValueTask<string?> GetDisplayTextAsync(
            ReferenceDescription reference,
            CancellationToken ct)
        {
            if (reference == null || reference.NodeId.IsNull)
            {
                return new ValueTask<string?>(string.Empty);
            }
            string? text = reference.DisplayName.Text
                ?? reference.BrowseName.Name
                ?? string.Empty;
            return new ValueTask<string?>(text);
        }

        /// <summary>
        /// Recursively searches the reference type hierarchy starting from
        /// <paramref name="startNodeId"/> for a node whose BrowseName matches
        /// <paramref name="browseName"/>.
        /// </summary>
        private async ValueTask<NodeId> FindReferenceTypeInHierarchyAsync(
            NodeId startNodeId,
            QualifiedName browseName,
            CancellationToken ct)
        {
            INode node;
            try
            {
                node = await GetNodeAsync(startNodeId, ct).ConfigureAwait(false);
            }
            catch
            {
                return NodeId.Null;
            }
            if (node.BrowseName == browseName)
            {
                return startNodeId;
            }
            ArrayOf<INode> subtypes = await GetReferencesAsync(
                startNodeId, ReferenceTypeIds.HasSubtype, false, false, ct)
                .ConfigureAwait(false);
            foreach (INode subtype in subtypes.ToList())
            {
                NodeId subtypeId = ToNodeId(subtype.NodeId);
                if (subtypeId.IsNull)
                {
                    continue;
                }
                NodeId found = await FindReferenceTypeInHierarchyAsync(subtypeId, browseName, ct)
                    .ConfigureAwait(false);
                if (!found.IsNull)
                {
                    return found;
                }
            }
            return NodeId.Null;
        }

        /// <summary>
        /// Get or add references to cache
        /// </summary>
        private ValueTask<ArrayOf<ReferenceDescription>> GetOrAddReferencesAsync(
            NodeId nodeId,
            CancellationToken ct)
        {
            Debug.Assert(!nodeId.IsNull);
            INodeCacheContext context = m_context;
            return m_refs.GetOrAddAsync(
                nodeId,
                async key =>
                {
                    ArrayOf<ReferenceDescription> references =
                        await context.FetchReferencesAsync(null, key, ct)
                            .ConfigureAwait(false);
                    foreach (ReferenceDescription? reference in references)
                    {
                        // transform absolute identifiers.
                        if (reference.NodeId.IsAbsolute)
                        {
                            reference.NodeId = ExpandedNodeId.ToNodeId(
                                reference.NodeId,
                                context.NamespaceUris);
                        }
                    }
                    return references;
                });
        }

        /// <summary>
        /// Fetch remaining nodes not yet in the result list
        /// </summary>
        private async ValueTask<ArrayOf<INode>> FetchRemainingAsync(
            List<NodeId> remainingIds,
            List<INode?> result,
            CancellationToken ct)
        {
            Debug.Assert(result.Count(r => r == null) == remainingIds.Count);

            // fetch nodes and references from server.
            var localIds = new List<NodeId>(remainingIds);
            (ArrayOf<Node> nodes, ArrayOf<ServiceResult> readErrors) =
                await m_context.FetchNodesAsync(null, localIds, ct: ct)
                    .ConfigureAwait(false);

            Debug.Assert(nodes.Count == localIds.Count);
            Debug.Assert(readErrors.Count == localIds.Count);

            int resultMissingIndex = 0;
            for (int index = 0; index < localIds.Count; index++)
            {
                if (!ServiceResult.IsBad(readErrors[index]))
                {
                    // Populate each node's ReferenceTable so callers can
                    // introspect references (e.g. HasTypeDefinition for
                    // leaf-detection on property nodes) without an extra
                    // round trip. Routes through GetOrAddReferencesAsync
                    // so cached references are reused.
                    if (nodes[index].ReferenceTable.Count == 0)
                    {
                        ArrayOf<ReferenceDescription> references =
                            await GetOrAddReferencesAsync(
                                remainingIds[index], ct)
                                .ConfigureAwait(false);
                        foreach (ReferenceDescription reference in references)
                        {
                            ExpandedNodeId targetId = reference.NodeId;
                            if (targetId.IsAbsolute)
                            {
                                targetId = ExpandedNodeId.ToNodeId(
                                    targetId, NamespaceUris);
                            }
                            nodes[index].ReferenceTable.Add(
                                reference.ReferenceTypeId,
                                !reference.IsForward,
                                targetId);
                        }
                    }
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
            return result.ToArrayOf()!;
        }

        /// <summary>
        /// Fetch remaining nodes not yet in the result list
        /// </summary>
        private async ValueTask<ArrayOf<DataValue>> FetchRemainingAsync(
            List<NodeId> remainingIds,
            List<DataValue?> result,
            CancellationToken ct)
        {
            Debug.Assert(result.Count(r => r == null) == remainingIds.Count);

            // fetch nodes and references from server.
            (ArrayOf<DataValue> values, ArrayOf<ServiceResult> readErrors) =
                await m_context.FetchValuesAsync(null, remainingIds.ToArrayOf(), ct: ct)
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
            return result.ToArrayOf()!;
        }

        /// <summary>
        /// Check whether type hierarchy is loaded
        /// </summary>
        private bool IsTypeHierarchyLoaded(ArrayOf<NodeId> typeIds)
        {
            var types = new Queue<NodeId>(typeIds.Filter(nodeId => !nodeId.IsNull).ToList());
            while (types.Count > 0)
            {
                NodeId typeId = types.Dequeue();
                if (!m_refs.TryGet(typeId, out ArrayOf<ReferenceDescription> references))
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
        private NodeId GetSuperTypeFromReferences(ArrayOf<ReferenceDescription> references)
        {
            ArrayOf<NodeId> superTypes = references
                .Filter(r => !r.IsForward && r.ReferenceTypeId == ReferenceTypeIds.HasSubtype)
                .ConvertAll(r => ExpandedNodeId.ToNodeId(r.NodeId, NamespaceUris));
            return superTypes.IsEmpty ? NodeId.Null : superTypes.Span[0];
        }

        /// <summary>
        /// Convert an expanded node id to a node id. Resolves the namespace URI
        /// against <see cref="NamespaceUris"/> for absolute identifiers; returns
        /// <see cref="NodeId.Null"/> if the URI is unknown.
        /// </summary>
        private NodeId ToNodeId(ExpandedNodeId expandedNodeId)
        {
            return ExpandedNodeId.ToNodeId(expandedNodeId, NamespaceUris);
        }

        private readonly IAsyncCache<NodeId, INode> m_nodes;
        private readonly IAsyncCache<NodeId, ArrayOf<ReferenceDescription>> m_refs;
        private readonly IAsyncCache<NodeId, DataValue> m_values;
        private readonly INodeCacheContext m_context;
        private readonly Meter m_meter;
        private readonly ILogger m_logger;
    }
}
