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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Default implementation of <see cref="INodeManagerBuilder"/> and
    /// <see cref="IFluentDispatcher"/>. Built and owned by the source-generated
    /// <c>NodeManagerBase</c> (or by a hand-written manager that wants to opt
    /// in to the fluent surface).
    /// </summary>
    /// <remarks>
    /// <para>
    /// All wiring happens during the user's <c>Configure</c> delegate, which
    /// runs once per manager activation immediately after
    /// <c>LoadPredefinedNodes</c> populates the address space. After
    /// <see cref="Seal"/> is called the builder rejects further <c>Node(...)</c>
    /// calls; the dispatcher remains live and fields per-node lookups during
    /// runtime.
    /// </para>
    /// <para>
    /// Threading: <c>Configure</c> runs synchronously on the thread that
    /// activates the manager; the dispatcher's dictionaries are populated
    /// once and read-only thereafter, so no synchronization is needed at
    /// dispatch time.
    /// </para>
    /// </remarks>
    public sealed class NodeManagerBuilder : INodeManagerBuilder, IFluentDispatcher
    {
        /// <summary>
        /// Creates a new builder for the supplied <paramref name="nodeManager"/>.
        /// </summary>
        /// <param name="context">
        /// System context that flows through <c>Configure</c>; typically the
        /// manager's <c>SystemContext</c>.
        /// </param>
        /// <param name="nodeManager">The node manager being wired.</param>
        /// <param name="defaultNamespaceIndex">
        /// Namespace index used when a browse-path segment omits an explicit
        /// <c>ns=N;</c> prefix. Typically the manager's first registered
        /// namespace.
        /// </param>
        /// <param name="rootResolver">
        /// Delegate that locates a root <see cref="NodeState"/> for a given
        /// <see cref="QualifiedName"/>. Typically backed by the manager's
        /// <c>PredefinedNodes</c> dictionary.
        /// </param>
        /// <param name="nodeIdResolver">
        /// Delegate that locates a <see cref="NodeState"/> by absolute
        /// <see cref="NodeId"/>. Typically backed by the manager's
        /// <c>PredefinedNodes</c> dictionary.
        /// </param>
        /// <param name="typeIdResolver">
        /// Delegate that returns every <see cref="NodeState"/> whose
        /// <c>TypeDefinitionId</c> matches the supplied <see cref="NodeId"/>.
        /// Typically a generated walk over the manager's predefined nodes.
        /// </param>
        public NodeManagerBuilder(
            ISystemContext context,
            IAsyncNodeManager nodeManager,
            ushort defaultNamespaceIndex,
            Func<QualifiedName, NodeState> rootResolver,
            Func<NodeId, NodeState> nodeIdResolver,
            Func<NodeId, IReadOnlyList<NodeState>> typeIdResolver)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            NodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
            m_defaultNamespaceIndex = defaultNamespaceIndex;
            m_rootResolver = rootResolver ?? throw new ArgumentNullException(nameof(rootResolver));
            m_nodeIdResolver = nodeIdResolver ?? throw new ArgumentNullException(nameof(nodeIdResolver));
            m_typeIdResolver = typeIdResolver ?? throw new ArgumentNullException(nameof(typeIdResolver));
        }

        /// <inheritdoc/>
        public ISystemContext Context { get; }

        /// <inheritdoc/>
        public IAsyncNodeManager NodeManager { get; }

        /// <inheritdoc/>
        public IFluentDispatcher Dispatcher => this;

        /// <summary>
        /// Marks the builder as no longer accepting new <c>Node(...)</c>
        /// lookups. Existing per-node builders remain functional but the
        /// generator-emitted manager calls this once <c>Configure</c>
        /// returns to fail-fast on stray late wiring attempts.
        /// </summary>
        public void Seal()
        {
            m_sealed = true;
        }

        /// <inheritdoc/>
        public INodeBuilder Node(string browsePath)
        {
            ThrowIfSealed();
            NodeState node = BrowsePathResolver.Resolve(
                Context,
                browsePath,
                m_defaultNamespaceIndex,
                m_rootResolver);

            return new NodeBuilder(this, node);
        }

        /// <inheritdoc/>
        public INodeBuilder<TState> Node<TState>(string browsePath)
            where TState : NodeState
        {
            ThrowIfSealed();
            NodeState node = BrowsePathResolver.Resolve(
                Context,
                browsePath,
                m_defaultNamespaceIndex,
                m_rootResolver);

            if (node is not TState typed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Browse path '{0}' resolved to {1}, which is not assignable to {2}.",
                    browsePath,
                    node.GetType().Name,
                    typeof(TState).Name);
            }

            return new NodeBuilder<TState>(this, typed);
        }

        /// <inheritdoc/>
        public INodeBuilder Node(NodeId nodeId)
        {
            ThrowIfSealed();
            NodeState node = ResolveNodeId(nodeId);
            return new NodeBuilder(this, node);
        }

        /// <inheritdoc/>
        public INodeBuilder<TState> Node<TState>(NodeId nodeId)
            where TState : NodeState
        {
            ThrowIfSealed();
            NodeState node = ResolveNodeId(nodeId);
            if (node is not TState typed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "NodeId '{0}' resolved to {1}, which is not assignable to {2}.",
                    nodeId,
                    node.GetType().Name,
                    typeof(TState).Name);
            }

            return new NodeBuilder<TState>(this, typed);
        }

        /// <inheritdoc/>
        public INodeBuilder NodeFromTypeId(NodeId typeDefinitionId)
        {
            ThrowIfSealed();
            NodeState node = ResolveByTypeDefinition(typeDefinitionId, (QualifiedName)null);
            return new NodeBuilder(this, node);
        }

        /// <inheritdoc/>
        public INodeBuilder NodeFromTypeId(NodeId typeDefinitionId, QualifiedName browseName)
        {
            ThrowIfSealed();
            NodeState node = ResolveByTypeDefinition(typeDefinitionId, browseName);
            return new NodeBuilder(this, node);
        }

        /// <inheritdoc/>
        public INodeBuilder<TState> NodeFromTypeId<TState>(NodeId typeDefinitionId)
            where TState : NodeState
        {
            ThrowIfSealed();
            NodeState node = ResolveByTypeDefinition(typeDefinitionId, (QualifiedName)null);
            if (node is not TState typed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "TypeDefinitionId '{0}' resolved to {1}, which is not assignable to {2}.",
                    typeDefinitionId,
                    node.GetType().Name,
                    typeof(TState).Name);
            }
            return new NodeBuilder<TState>(this, typed);
        }

        /// <inheritdoc/>
        public INodeBuilder<TState> NodeFromTypeId<TState>(NodeId typeDefinitionId, QualifiedName browseName)
            where TState : NodeState
        {
            ThrowIfSealed();
            NodeState node = ResolveByTypeDefinition(typeDefinitionId, browseName);
            if (node is not TState typed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "TypeDefinitionId '{0}' (browse name '{1}') resolved to {2}, which is not assignable to {3}.",
                    typeDefinitionId,
                    browseName,
                    node.GetType().Name,
                    typeof(TState).Name);
            }
            return new NodeBuilder<TState>(this, typed);
        }

        /// <inheritdoc/>
        public bool TryHandleHistoryRead(
            ISystemContext context,
            NodeState node,
            HistoryReadDetails details,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueId nodeToRead,
            HistoryReadResult result,
            out ServiceResult status)
        {
            if (node != null
                && m_historyRead.TryGetValue(node.NodeId, out HistoryReadHandler handler))
            {
                status = handler(
                    context,
                    node,
                    details,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodeToRead,
                    result);
                return true;
            }

            status = ServiceResult.Good;
            return false;
        }

        /// <inheritdoc/>
        public bool TryHandleHistoryUpdate(
            ISystemContext context,
            NodeState node,
            HistoryUpdateDetails nodeToUpdate,
            HistoryUpdateResult result,
            out ServiceResult status)
        {
            if (node != null
                && m_historyUpdate.TryGetValue(node.NodeId, out HistoryUpdateHandler handler))
            {
                status = handler(context, node, nodeToUpdate, result);
                return true;
            }

            status = ServiceResult.Good;
            return false;
        }

        /// <inheritdoc/>
        public void NotifyMonitoredItemCreated(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            if (source != null
                && m_monitoredItemCreated.TryGetValue(source.NodeId, out MonitoredItemCreatedHandler handler))
            {
                handler(context, source, monitoredItem);
            }
        }

        /// <inheritdoc/>
        public void NotifyNodeAdded(ISystemContext context, NodeState node)
        {
            if (node != null
                && m_nodeAdded.TryGetValue(node.NodeId, out NodeLifecycleHandler handler))
            {
                handler(context, node);
            }
        }

        /// <inheritdoc/>
        public void NotifyNodeRemoved(ISystemContext context, NodeState node)
        {
            if (node != null
                && m_nodeRemoved.TryGetValue(node.NodeId, out NodeLifecycleHandler handler))
            {
                handler(context, node);
            }
        }

        internal void RegisterHistoryRead(NodeState node, HistoryReadHandler handler)
        {
            ThrowIfDuplicate(m_historyRead, node, "OnHistoryRead");
            m_historyRead[node.NodeId] = handler;
        }

        internal void RegisterHistoryUpdate(NodeState node, HistoryUpdateHandler handler)
        {
            ThrowIfDuplicate(m_historyUpdate, node, "OnHistoryUpdate");
            m_historyUpdate[node.NodeId] = handler;
        }

        internal void RegisterMonitoredItemCreated(NodeState node, MonitoredItemCreatedHandler handler)
        {
            ThrowIfDuplicate(m_monitoredItemCreated, node, "OnMonitoredItemCreated");
            m_monitoredItemCreated[node.NodeId] = handler;
        }

        internal void RegisterNodeAdded(NodeState node, NodeLifecycleHandler handler)
        {
            ThrowIfDuplicate(m_nodeAdded, node, "OnNodeAdded");
            m_nodeAdded[node.NodeId] = handler;
        }

        internal void RegisterNodeRemoved(NodeState node, NodeLifecycleHandler handler)
        {
            ThrowIfDuplicate(m_nodeRemoved, node, "OnNodeRemoved");
            m_nodeRemoved[node.NodeId] = handler;
        }

        private NodeState ResolveNodeId(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "NodeId is null or empty.");
            }

            NodeState node = m_nodeIdResolver(nodeId) ?? throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "NodeId '{0}' did not resolve to a predefined node.",
                    nodeId);

            return node;
        }

        private NodeState ResolveByTypeDefinition(NodeId typeDefinitionId, QualifiedName browseName)
        {
            if (typeDefinitionId == null || typeDefinitionId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "TypeDefinitionId is null or empty.");
            }

            IReadOnlyList<NodeState> candidates = m_typeIdResolver(typeDefinitionId)
                ?? Array.Empty<NodeState>();

            if (candidates.Count == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "No predefined node has TypeDefinitionId '{0}'.",
                    typeDefinitionId);
            }

            if (browseName == null)
            {
                if (candidates.Count > 1)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameDuplicated,
                        "TypeDefinitionId '{0}' is ambiguous: {1} matching instances found. " +
                        "Pass a QualifiedName disambiguator to NodeFromTypeId.",
                        typeDefinitionId,
                        candidates.Count);
                }
                return candidates[0];
            }

            NodeState match = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].BrowseName == browseName)
                {
                    if (match != null)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadBrowseNameDuplicated,
                            "TypeDefinitionId '{0}' has multiple instances with browse name '{1}'.",
                            typeDefinitionId,
                            browseName);
                    }
                    match = candidates[i];
                }
            }

            if (match == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "TypeDefinitionId '{0}' has no instance with browse name '{1}'.",
                    typeDefinitionId,
                    browseName);
            }

            return match;
        }

        private void ThrowIfSealed()
        {
            if (m_sealed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Cannot wire additional nodes after the builder has been sealed. "
                    + "All Node(...) calls must occur inside the Configure delegate.");
            }
        }

        private static void ThrowIfDuplicate<T>(
            Dictionary<NodeId, T> map,
            NodeState node,
            string what)
        {
            if (map.ContainsKey(node.NodeId))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Node '{0}' (id '{1}') already has a {2} handler registered.",
                    node.BrowseName,
                    node.NodeId,
                    what);
            }
        }

        private readonly ushort m_defaultNamespaceIndex;
        private readonly Func<QualifiedName, NodeState> m_rootResolver;
        private readonly Func<NodeId, NodeState> m_nodeIdResolver;
        private readonly Func<NodeId, IReadOnlyList<NodeState>> m_typeIdResolver;
        private bool m_sealed;
        private readonly Dictionary<NodeId, HistoryReadHandler> m_historyRead = [];
        private readonly Dictionary<NodeId, HistoryUpdateHandler> m_historyUpdate = [];
        private readonly Dictionary<NodeId, MonitoredItemCreatedHandler> m_monitoredItemCreated = [];
        private readonly Dictionary<NodeId, NodeLifecycleHandler> m_nodeAdded = [];
        private readonly Dictionary<NodeId, NodeLifecycleHandler> m_nodeRemoved = [];
    }
}
