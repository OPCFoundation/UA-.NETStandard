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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;

namespace Opc.Ua.Client
{
    /// <summary>
    /// An implementation of a client side nodecache.
    /// </summary>
    public class NodeCache : INodeCache, IDisposable
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public NodeCache(INodeCacheContext context, ITelemetryContext telemetry)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = telemetry.CreateLogger<NodeCache>();
            m_typeTree = new TypeTable(m_context.NamespaceUris);
            m_nodes = new NodeTable(
                m_context.NamespaceUris,
                m_context.ServerUris,
                m_typeTree);
            m_uaTypesLoaded = false;
            m_cacheLock = new ReaderWriterLockSlim();
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_cacheLock?.Dispose();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris => m_context.NamespaceUris;

        /// <inheritdoc/>
        public StringTable ServerUris => m_context.ServerUris;

        /// <inheritdoc/>
        IAsyncTypeTable IAsyncNodeTable.TypeTree => this;

        /// <summary>
        /// Legacy type table adapter for backwards compatibility.
        /// </summary>
        public ITypeTable TypeTree => this.AsTypeTable();

        /// <inheritdoc/>
        public async ValueTask<INode?> FindAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default)
        {
            // check for null.
            if (nodeId.IsNull)
            {
                return null;
            }

            m_cacheLock.EnterReadLock();
            INode node;
            try
            {
                // check if node already exists.
                node = m_nodes.Find(nodeId);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            if (node != null)
            {
                // do not return temporary nodes created after a Browse().
                if (node.GetType() != typeof(Node))
                {
                    return node;
                }
            }

            // fetch node from server.
            try
            {
                return await FetchNodeAsync(nodeId, ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    "Could not fetch node from server: NodeId={NodeId}, Reason='{Error}'.",
                    nodeId,
                    Redact.Create(e));
                // m_nodes[nodeId] = null;
                return null;
            }
        }

        /// <inheritdoc/>
        public async Task<IList<INode?>> FindAsync(
            IList<ExpandedNodeId> nodeIds,
            CancellationToken ct = default)
        {
            // check for null.
            if (nodeIds == null || nodeIds.Count == 0)
            {
                return [];
            }

            int count = nodeIds.Count;
            var nodes = new List<INode?>(count);
            var fetchNodeIds = new ExpandedNodeIdCollection();

            int ii;
            for (ii = 0; ii < count; ii++)
            {
                INode node;

                m_cacheLock.EnterReadLock();
                try
                {
                    // check if node already exists.
                    node = m_nodes.Find(nodeIds[ii]);
                }
                finally
                {
                    m_cacheLock.ExitReadLock();
                }

                // do not return temporary nodes created after a Browse().
                if (node != null && node.GetType() != typeof(Node))
                {
                    nodes.Add(node);
                }
                else
                {
                    nodes.Add(null);
                    fetchNodeIds.Add(nodeIds[ii]);
                }
            }

            if (fetchNodeIds.Count == 0)
            {
                return nodes;
            }

            // fetch missing nodes from server.
            IList<Node?> fetchedNodes;
            try
            {
                fetchedNodes = await FetchNodesAsync(fetchNodeIds, ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                m_logger.LogError("Could not fetch nodes from server: Reason='{Error}'.", e.Message);
                // m_nodes[nodeId] = null;
                return nodes;
            }

            ii = 0;
            foreach (Node? fetchedNode in fetchedNodes)
            {
                while (ii < count && nodes[ii] != null)
                {
                    ii++;
                }
                if (ii < count && nodes[ii] == null)
                {
                    nodes[ii++] = fetchedNode;
                }
                else
                {
                    m_logger.LogError(
                        "Inconsistency fetching nodes from server. Not all nodes could be assigned.");
                    break;
                }
            }

            return nodes;
        }

        /// <inheritdoc/>
        public async ValueTask<INode?> FindAsync(
            ExpandedNodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            QualifiedName browseName,
            CancellationToken ct = default)
        {
            // find the source.
            if (await FindAsync(sourceId, ct).ConfigureAwait(false)
                is not Node source)
            {
                return null;
            }

            IList<IReference> references;

            m_cacheLock.EnterReadLock();
            try
            {
                // find all references.
                references = source.ReferenceTable
                    .Find(referenceTypeId, isInverse, includeSubtypes, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            foreach (IReference reference in references)
            {
                INode? target = await FindAsync(reference.TargetId, ct)
                    .ConfigureAwait(false);

                if (target == null)
                {
                    continue;
                }

                if (target.BrowseName == browseName)
                {
                    return target;
                }
            }

            // target not found.
            return null;
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> FindSuperTypeAsync(
            ExpandedNodeId typeId,
            CancellationToken ct = default)
        {
            INode? type = await FindAsync(typeId, ct).ConfigureAwait(false);

            if (type == null)
            {
                return NodeId.Null;
            }

            m_cacheLock.EnterReadLock();
            try
            {
                return m_typeTree.FindSuperType(typeId);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<IList<INode>> FindAsync(
            ExpandedNodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            CancellationToken ct = default)
        {
            var hits = new List<INode>();

            // find the source.
            if (await FindAsync(sourceId, ct).ConfigureAwait(false)
                is not Node source)
            {
                return hits;
            }

            IList<IReference> references;
            m_cacheLock.EnterReadLock();
            try
            {
                // find all references.
                references = source.ReferenceTable
                    .Find(referenceTypeId, isInverse, includeSubtypes, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            foreach (IReference reference in references)
            {
                INode? target =
                    await FindAsync(reference.TargetId, ct).ConfigureAwait(false);

                if (target == null)
                {
                    continue;
                }

                hits.Add(target);
            }

            return hits;
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> FindSuperTypeAsync(
            NodeId typeId,
            CancellationToken ct = default)
        {
            INode? type = await FindAsync(typeId, ct).ConfigureAwait(false);

            if (type == null)
            {
                return NodeId.Null;
            }

            m_cacheLock.EnterReadLock();
            try
            {
                return m_typeTree.FindSuperType(typeId);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }
        }

        /// <inheritdoc/>
        public async Task<Node?> FetchNodeAsync(ExpandedNodeId nodeId, CancellationToken ct)
        {
            var localId = ExpandedNodeId.ToNodeId(nodeId, m_context.NamespaceUris);

            if (localId.IsNullNodeId)
            {
                return null;
            }

            // fetch node from server.
            Node source = await m_context.FetchNodeAsync(null, localId, ct: ct).ConfigureAwait(false);

            try
            {
                // fetch references from server.
                ReferenceDescriptionCollection references = await m_context
                    .FetchReferencesAsync(null, localId, ct)
                    .ConfigureAwait(false);

                m_cacheLock.EnterUpgradeableReadLock();
                try
                {
                    foreach (ReferenceDescription reference in references)
                    {
                        // create a placeholder for the node if it does not already exist.
                        if (!m_nodes.Exists(reference.NodeId))
                        {
                            // transform absolute identifiers.
                            if (!reference.NodeId.IsNull && reference.NodeId.IsAbsolute)
                            {
                                reference.NodeId = ExpandedNodeId.ToNodeId(
                                    reference.NodeId,
                                    NamespaceUris);
                            }

                            var target = new Node(reference);

                            InternalWriteLockedAttach(target);
                        }

                        // add the reference.
                        source.ReferenceTable
                            .Add(reference.ReferenceTypeId, !reference.IsForward, reference.NodeId);
                    }
                }
                finally
                {
                    m_cacheLock.ExitUpgradeableReadLock();
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    "Could not fetch references for valid node with NodeId = {NodeId}. Error = {Error}",
                    nodeId,
                    Redact.Create(e));
            }

            InternalWriteLockedAttach(source);

            return source;
        }

        /// <inheritdoc/>
        public async Task<IList<Node?>> FetchNodesAsync(
            IList<ExpandedNodeId> nodeIds,
            CancellationToken ct)
        {
            int count = nodeIds.Count;
            if (count == 0)
            {
                return [];
            }

            var localIds = new NodeIdCollection(
                nodeIds.Select(nodeId => ExpandedNodeId.ToNodeId(nodeId, m_context.NamespaceUris)));

            // fetch nodes and references from server.
            (IReadOnlyList<Node> sourceNodes, IReadOnlyList<ServiceResult> readErrors) = await m_context
                .FetchNodesAsync(null, localIds, NodeClass.Unspecified, ct: ct)
                .ConfigureAwait(false);
            (IReadOnlyList<ReferenceDescriptionCollection> referenceCollectionList, IReadOnlyList<ServiceResult> fetchErrors) =
                await m_context.FetchReferencesAsync(null, localIds, ct).ConfigureAwait(false);

            int ii = 0;
            for (ii = 0; ii < count; ii++)
            {
                if (ServiceResult.IsBad(readErrors[ii]))
                {
                    continue;
                }

                if (!ServiceResult.IsBad(fetchErrors[ii]))
                {
                    // fetch references from server.
                    foreach (ReferenceDescription reference in referenceCollectionList[ii])
                    {
                        m_cacheLock.EnterUpgradeableReadLock();
                        try
                        {
                            // create a placeholder for the node if it does not already exist.
                            if (!m_nodes.Exists(reference.NodeId))
                            {
                                // transform absolute identifiers.
                                if (!reference.NodeId.IsNull && reference.NodeId.IsAbsolute)
                                {
                                    reference.NodeId = ExpandedNodeId.ToNodeId(
                                        reference.NodeId,
                                        NamespaceUris);
                                }

                                var target = new Node(reference);

                                InternalWriteLockedAttach(target);
                            }
                        }
                        finally
                        {
                            m_cacheLock.ExitUpgradeableReadLock();
                        }

                        // add the reference.
                        sourceNodes[ii]
                            .ReferenceTable.Add(
                                reference.ReferenceTypeId,
                                !reference.IsForward,
                                reference.NodeId);
                    }
                }

                InternalWriteLockedAttach(sourceNodes[ii]);
            }

            return [.. sourceNodes];
        }

        /// <inheritdoc/>
        public async Task<IList<INode>> FindReferencesAsync(
            ExpandedNodeId nodeId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            CancellationToken ct)
        {
            IList<INode> targets = [];

            if (await FindAsync(nodeId, ct).ConfigureAwait(false) is not Node source)
            {
                return targets;
            }

            IList<IReference> references;

            m_cacheLock.EnterReadLock();
            try
            {
                references = source.ReferenceTable
                    .Find(referenceTypeId, isInverse, includeSubtypes, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            var targetIds = new ExpandedNodeIdCollection(
                references.Select(reference => reference.TargetId));

            IList<INode?> result = await FindAsync(targetIds, ct).ConfigureAwait(false);

            foreach (INode? target in result)
            {
                if (target != null)
                {
                    targets.Add(target);
                }
            }
            return targets;
        }

        /// <inheritdoc/>
        public async Task<IList<INode>> FindReferencesAsync(
            IList<ExpandedNodeId> nodeIds,
            IList<NodeId> referenceTypeIds,
            bool isInverse,
            bool includeSubtypes,
            CancellationToken ct)
        {
            IList<INode> targets = [];
            if (nodeIds.Count == 0 || referenceTypeIds.Count == 0)
            {
                return targets;
            }
            var targetIds = new ExpandedNodeIdCollection();
            IList<INode?> sources = await FindAsync(nodeIds, ct).ConfigureAwait(false);
            foreach (INode? source in sources)
            {
                if (source is not Node node)
                {
                    continue;
                }

                foreach (NodeId referenceTypeId in referenceTypeIds)
                {
                    IList<IReference> references;

                    m_cacheLock.EnterReadLock();
                    try
                    {
                        references = node.ReferenceTable
                            .Find(referenceTypeId, isInverse, includeSubtypes, m_typeTree);
                    }
                    finally
                    {
                        m_cacheLock.ExitReadLock();
                    }

                    targetIds.AddRange(references.Select(reference => reference.TargetId));
                }
            }

            IList<INode?> result = await FindAsync(targetIds, ct).ConfigureAwait(false);
            foreach (INode? target in result)
            {
                if (target != null)
                {
                    targets.Add(target);
                }
            }

            return targets;
        }

        /// <inheritdoc/>
        public async Task FetchSuperTypesAsync(ExpandedNodeId nodeId, CancellationToken ct)
        {
            // find the target node,
            if (await FindAsync(nodeId, ct).ConfigureAwait(false) is not ILocalNode source)
            {
                return;
            }

            // follow the tree.
            ILocalNode? subType = source;

            while (subType != null)
            {
                ILocalNode? superType = null;

                // Get super type (should be 1 or none)
                IList<INode> references = await FindReferencesAsync(
                    subType.NodeId,
                    ReferenceTypeIds.HasSubtype,
                    true,
                    true,
                    ct).ConfigureAwait(false);

                if (references != null && references.Count > 0)
                {
                    superType = references[0] as ILocalNode;
                }

                subType = superType;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> ExistsAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default)
        {
            return await FindAsync(nodeId, ct).ConfigureAwait(false) != null;
        }

        /// <inheritdoc/>
        public async ValueTask<bool> IsKnownAsync(
            ExpandedNodeId typeId,
            CancellationToken ct = default)
        {
            INode? type = await FindAsync(typeId, ct).ConfigureAwait(false);

            if (type == null)
            {
                return false;
            }

            m_cacheLock.EnterReadLock();
            try
            {
                return m_typeTree.IsKnown(typeId);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> IsKnownAsync(
            NodeId typeId,
            CancellationToken ct = default)
        {
            INode? type = await FindAsync(typeId, ct).ConfigureAwait(false);

            if (type == null)
            {
                return false;
            }

            m_cacheLock.EnterReadLock();
            try
            {
                return m_typeTree.IsKnown(typeId);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<IList<NodeId>> FindSubTypesAsync(
            ExpandedNodeId typeId,
            CancellationToken ct = default)
        {
            var subtypes = new List<NodeId>();

            if (await FindAsync(typeId, ct).ConfigureAwait(false)
                is not ILocalNode type)
            {
                return subtypes;
            }

            IList<IReference> references;

            m_cacheLock.EnterReadLock();
            try
            {
                references = type.References
                    .Find(ReferenceTypeIds.HasSubtype, false, true, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            foreach (IReference reference in references)
            {
                if (!reference.TargetId.IsAbsolute)
                {
                    subtypes.Add((NodeId)reference.TargetId);
                }
            }

            return subtypes;
        }

        /// <inheritdoc/>
        public async ValueTask<bool> IsTypeOfAsync(
            ExpandedNodeId subTypeId,
            ExpandedNodeId superTypeId,
            CancellationToken ct = default)
        {
            if (subTypeId == superTypeId)
            {
                return true;
            }

            if (await FindAsync(subTypeId, ct).ConfigureAwait(false)
                is not ILocalNode subtype)
            {
                return false;
            }

            ILocalNode? supertype = subtype;

            while (supertype != null)
            {
                ExpandedNodeId currentId;

                m_cacheLock.EnterReadLock();
                try
                {
                    currentId = supertype.References
                        .FindTarget(ReferenceTypeIds.HasSubtype, true, true, m_typeTree, 0);
                }
                finally
                {
                    m_cacheLock.ExitReadLock();
                }

                if (currentId == superTypeId)
                {
                    return true;
                }

                supertype = await FindAsync(currentId, ct).ConfigureAwait(false) as ILocalNode;
            }

            return false;
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

            if (await FindAsync(subTypeId, ct).ConfigureAwait(false)
                is not ILocalNode subtype)
            {
                return false;
            }

            ILocalNode? supertype = subtype;

            while (supertype != null)
            {
                ExpandedNodeId currentId;

                m_cacheLock.EnterReadLock();
                try
                {
                    currentId = supertype.References
                        .FindTarget(ReferenceTypeIds.HasSubtype, true, true, m_typeTree, 0);
                }
                finally
                {
                    m_cacheLock.ExitReadLock();
                }

                if (currentId == superTypeId)
                {
                    return true;
                }

                supertype = await FindAsync(currentId, ct).ConfigureAwait(false) as
                    ILocalNode;
            }

            return false;
        }

        /// <inheritdoc/>
        public ValueTask<QualifiedName> FindReferenceTypeNameAsync(
            NodeId referenceTypeId,
            CancellationToken ct = default)
        {
            QualifiedName typeName;
            m_cacheLock.EnterReadLock();
            try
            {
                typeName = m_typeTree.FindReferenceTypeName(referenceTypeId);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }
            return new ValueTask<QualifiedName>(typeName);
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> FindReferenceTypeAsync(
            QualifiedName browseName,
            CancellationToken ct = default)
        {
            NodeId referenceType;
            m_cacheLock.EnterReadLock();
            try
            {
                referenceType = m_typeTree.FindReferenceType(browseName);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }
            return new ValueTask<NodeId>(referenceType);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> IsEncodingOfAsync(
            ExpandedNodeId encodingId,
            ExpandedNodeId datatypeId,
            CancellationToken ct = default)
        {
            if (await FindAsync(encodingId, ct).ConfigureAwait(false)
                is not ILocalNode encoding)
            {
                return false;
            }

            IList<IReference> references;

            m_cacheLock.EnterReadLock();
            try
            {
                references = encoding.References
                    .Find(ReferenceTypeIds.HasEncoding, true, true, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            foreach (IReference reference in references)
            {
                if (reference.TargetId == datatypeId)
                {
                    return true;
                }
            }

            // no match.
            return false;
        }

        /// <inheritdoc/>
        public async ValueTask<bool> IsEncodingForAsync(
            NodeId expectedTypeId,
            ExtensionObject value,
            CancellationToken ct = default)
        {
            // no match on null values.
            if (value.IsNull)
            {
                return false;
            }

            // check for exact match.
            if (expectedTypeId == value.TypeId)
            {
                return true;
            }

            // find the encoding.
            if (await FindAsync(value.TypeId, ct).ConfigureAwait(false)
                is not ILocalNode encoding)
            {
                return false;
            }

            IList<IReference> references;

            m_cacheLock.EnterReadLock();
            try
            {
                references = encoding.References
                    .Find(ReferenceTypeIds.HasEncoding, true, true, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            // find data type.
            foreach (IReference reference in references)
            {
                if (reference.TargetId == expectedTypeId)
                {
                    return true;
                }
            }

            // no match.
            return false;
        }

        /// <inheritdoc/>
        public async ValueTask<bool> IsEncodingForAsync(
            NodeId expectedTypeId,
            object value,
            CancellationToken ct = default)
        {
            // null actual datatype matches nothing.
            if (value == null)
            {
                return false;
            }

            // null expected datatype matches everything.
            if (expectedTypeId.IsNullNodeId)
            {
                return true;
            }

            // get the actual datatype.
            NodeId actualTypeId = TypeInfo.GetDataTypeId(value, m_context.NamespaceUris);

            // value is valid if the expected datatype is same as or a supertype of the actual datatype
            // for example: expected datatype of 'Integer' matches an actual datatype of 'UInt32'.
            if (await IsTypeOfAsync(actualTypeId, expectedTypeId, ct).ConfigureAwait(false))
            {
                return true;
            }

            // allow matches non-structure values where the actual datatype
            // is a supertype of the expected datatype.
            // for example: expected datatype of 'UtcTime' matches an actual
            // datatype of 'DateTime'.
            if (actualTypeId != DataTypes.Structure)
            {
                return await IsTypeOfAsync(
                    expectedTypeId,
                    actualTypeId,
                    ct).ConfigureAwait(false);
            }

            // for structure types must try to determine the subtype.

            if (value is ExtensionObject extension)
            {
                return await IsEncodingForAsync(
                    expectedTypeId,
                    extension,
                    ct).ConfigureAwait(false);
            }

            // every element in an array must match.

            if (value is ExtensionObject[] extensions)
            {
                for (int ii = 0; ii < extensions.Length; ii++)
                {
                    if (!await IsEncodingForAsync(
                        expectedTypeId,
                        extensions[ii],
                        ct).ConfigureAwait(false))
                    {
                        return false;
                    }
                }

                return true;
            }

            // can only get here if the value is an unrecognized data type.
            return false;
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> FindDataTypeIdAsync(
            ExpandedNodeId encodingId,
            CancellationToken ct = default)
        {
            if (await FindAsync(encodingId, ct).ConfigureAwait(false)
                is not ILocalNode encoding)
            {
                return NodeId.Null;
            }

            IList<IReference> references;

            m_cacheLock.EnterReadLock();
            try
            {
                references = encoding.References
                    .Find(ReferenceTypeIds.HasEncoding, true, true, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            if (references.Count > 0)
            {
                return ExpandedNodeId.ToNodeId(references[0].TargetId, m_context.NamespaceUris);
            }

            return NodeId.Null;
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> FindDataTypeIdAsync(
            NodeId encodingId,
            CancellationToken ct = default)
        {
            if (await FindAsync(encodingId, ct).ConfigureAwait(false)
                is not ILocalNode encoding)
            {
                return NodeId.Null;
            }

            IList<IReference> references;

            m_cacheLock.EnterReadLock();
            try
            {
                references = encoding.References
                    .Find(ReferenceTypeIds.HasEncoding, true, true, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            if (references.Count > 0)
            {
                return ExpandedNodeId.ToNodeId(references[0].TargetId, m_context.NamespaceUris);
            }

            return NodeId.Null;
        }

        /// <inheritdoc/>
        public void LoadUaDefinedTypes(ISystemContext context)
        {
            if (m_uaTypesLoaded)
            {
                return;
            }

            NodeStateCollection predefinedNodes = new NodeStateCollection().AddOpcUa(context);

            m_cacheLock.EnterWriteLock();
            try
            {
                for (int ii = 0; ii < predefinedNodes.Count; ii++)
                {
                    if (predefinedNodes[ii] is not BaseTypeState type)
                    {
                        continue;
                    }

                    type.Export(context, m_nodes);
                }
            }
            finally
            {
                m_cacheLock.ExitWriteLock();
            }
            m_uaTypesLoaded = true;
        }

        /// <inheritdoc/>
        public void Clear()
        {
            m_cacheLock.EnterWriteLock();
            try
            {
                m_uaTypesLoaded = false;
                m_nodes.Clear();
            }
            finally
            {
                m_cacheLock.ExitWriteLock();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<string?> GetDisplayTextAsync(
            INode node,
            CancellationToken ct = default)
        {
            // check for null.
            if (node == null)
            {
                return string.Empty;
            }

            // check for remote node.
            if (node is not Node target)
            {
                return node.ToString();
            }

            string? displayText = null;

            // use the modelling rule to determine which parent to follow.
            NodeId modellingRule = target.ModellingRule;

            IList<IReference> references;

            m_cacheLock.EnterReadLock();
            try
            {
                references = target.ReferenceTable
                    .Find(ReferenceTypeIds.Aggregates, true, true, m_typeTree);
            }
            finally
            {
                m_cacheLock.ExitReadLock();
            }

            foreach (IReference reference in references)
            {
                var parent = await FindAsync(
                    reference.TargetId,
                    ct).ConfigureAwait(false) as Node;

                // use the first parent if modelling rule is new.
                if (modellingRule == Objects.ModellingRule_Mandatory)
                {
                    displayText = parent == null ? null : await GetDisplayTextAsync(
                        parent,
                        ct).ConfigureAwait(false);
                    break;
                }

                // use the type node as the parent for other modelling rules.
                if (parent is VariableTypeNode or ObjectTypeNode)
                {
                    displayText = await GetDisplayTextAsync(
                        parent,
                        ct).ConfigureAwait(false);
                    break;
                }
            }

            // prepend the parent display name.
            if (displayText != null)
            {
                return Utils.Format("{0}.{1}", displayText, node);
            }

            // simply use the node name.
            return node.ToString();
        }

        /// <inheritdoc/>
        public async ValueTask<string?> GetDisplayTextAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default)
        {
            if (nodeId.IsNull)
            {
                return string.Empty;
            }

            INode? node = await FindAsync(
                nodeId,
                ct).ConfigureAwait(false);

            if (node != null)
            {
                return await GetDisplayTextAsync(
                    node,
                    ct).ConfigureAwait(false);
            }

            return Utils.Format("{0}", nodeId);
        }

        /// <inheritdoc/>
        public async ValueTask<string?> GetDisplayTextAsync(
            ReferenceDescription reference,
            CancellationToken ct = default)
        {
            if (reference == null || reference.NodeId.IsNull)
            {
                return string.Empty;
            }

            INode? node = await FindAsync(
                reference.NodeId,
                ct).ConfigureAwait(false);

            if (node != null)
            {
                return await GetDisplayTextAsync(
                    node,
                    ct).ConfigureAwait(false);
            }

            return reference.ToString();
        }

        private void InternalWriteLockedAttach(ILocalNode node)
        {
            m_cacheLock.EnterWriteLock();
            try
            {
                // add to cache.
                m_nodes.Attach(node);
            }
            finally
            {
                m_cacheLock.ExitWriteLock();
            }
        }

        private readonly ReaderWriterLockSlim m_cacheLock = new();
        private readonly ILogger m_logger;
        private readonly INodeCacheContext m_context;
        private readonly TypeTable m_typeTree;
        private readonly NodeTable m_nodes;
        private bool m_uaTypesLoaded;
    }
}
