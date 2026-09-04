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
using System.Threading;
using System.Threading.Tasks;

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
        /// <param name="dataTypeIdResolver">
        /// Delegate that returns every <see cref="BaseVariableState"/> whose
        /// <c>DataType</c> matches the supplied <see cref="NodeId"/>.
        /// Typically a generated walk over the manager's predefined nodes.
        /// When <c>null</c>, DataType lookups always resolve to no
        /// candidates (as if no variable declared that DataType).
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/>, <paramref name="nodeManager"/>,
        /// <paramref name="rootResolver"/>, <paramref name="nodeIdResolver"/>,
        /// or <paramref name="typeIdResolver"/> is null.
        /// </exception>
        public NodeManagerBuilder(
            ISystemContext context,
            IAsyncNodeManager nodeManager,
            ushort defaultNamespaceIndex,
            Func<QualifiedName, NodeState> rootResolver,
            Func<NodeId, NodeState> nodeIdResolver,
            Func<NodeId, IReadOnlyList<NodeState>> typeIdResolver,
            Func<NodeId, ArrayOf<NodeState>>? dataTypeIdResolver = null)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            NodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
            m_defaultNamespaceIndex = defaultNamespaceIndex;
            m_rootResolver = rootResolver ?? throw new ArgumentNullException(nameof(rootResolver));
            m_nodeIdResolver = nodeIdResolver ?? throw new ArgumentNullException(nameof(nodeIdResolver));
            m_typeIdResolver = typeIdResolver ?? throw new ArgumentNullException(nameof(typeIdResolver));
            m_dataTypeIdResolver = dataTypeIdResolver ?? (static _ => []);
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
            Simulations?.Start();
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
            NodeState node = ResolveByTypeDefinition(typeDefinitionId, (QualifiedName)null!);
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
            NodeState node = ResolveByTypeDefinition(typeDefinitionId, (QualifiedName)null!);
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
        public IVariableBuilder<TValue> Variable<TValue>(string browsePath)
        {
            ThrowIfSealed();
            NodeState node = BrowsePathResolver.Resolve(
                Context,
                browsePath,
                m_defaultNamespaceIndex,
                m_rootResolver);
            return ToVariableBuilder<TValue>(node, browsePath);
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> Variable<TValue>(NodeId nodeId)
        {
            ThrowIfSealed();
            NodeState node = ResolveNodeId(nodeId);
            return ToVariableBuilder<TValue>(node, FormatNodeId(nodeId));
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> VariableFromTypeId<TValue>(NodeId typeDefinitionId)
        {
            ThrowIfSealed();
            NodeState node = ResolveByTypeDefinition(typeDefinitionId, (QualifiedName)null!);
            return ToVariableBuilder<TValue>(node, FormatNodeId(typeDefinitionId));
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> VariableFromTypeId<TValue>(NodeId typeDefinitionId, QualifiedName browseName)
        {
            ThrowIfSealed();
            NodeState node = ResolveByTypeDefinition(typeDefinitionId, browseName);
            return ToVariableBuilder<TValue>(
                node,
                CoreUtils.Format(
                    "{0} (browse name '{1}')",
                    FormatNodeId(typeDefinitionId),
                    browseName));
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> VariableFromDataTypeId<TValue>(NodeId dataTypeId)
        {
            ThrowIfSealed();
            NodeState node = ResolveByDataType(dataTypeId, (QualifiedName)null!);
            return ToVariableBuilder<TValue>(node, FormatNodeId(dataTypeId));
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> VariableFromDataTypeId<TValue>(NodeId dataTypeId, QualifiedName browseName)
        {
            ThrowIfSealed();
            NodeState node = ResolveByDataType(dataTypeId, browseName);
            return ToVariableBuilder<TValue>(
                node,
                CoreUtils.Format(
                    "{0} (browse name '{1}')",
                    FormatNodeId(dataTypeId),
                    browseName));
        }

        internal VariableBuilder<TValue> ToVariableBuilder<TValue>(NodeState node, string lookupHint)
        {
            if (node is not BaseVariableState variable)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Lookup '{0}' resolved to {1}, which is not a BaseVariableState.",
                    lookupHint,
                    node.GetType().Name);
            }
            return new VariableBuilder<TValue>(this, variable);
        }

        internal IVirtualNodeBuilder RegisterVirtualNodes(
            VirtualNodeIdPredicate predicate,
            VirtualNodeResolver resolver)
        {
            ThrowIfSealed();
            var registration = new VirtualNodeRegistration(this, predicate, resolver);
            m_virtualNodes.Add(registration);
            return registration;
        }

        internal NodeHandle? CreateVirtualNodeHandle(NodeId nodeId)
        {
            VirtualNodeRegistration? registration = FindVirtualNodeRegistration(nodeId);
            if (registration == null)
            {
                return null;
            }

            return new NodeHandle
            {
                NodeId = nodeId,
                ParsedNodeId = registration,
                Validated = false
            };
        }

        internal VirtualNodeRegistration? FindVirtualNodeRegistration(NodeId nodeId)
        {
            VirtualNodeRegistration? match = null;
            foreach (VirtualNodeRegistration registration in m_virtualNodes)
            {
                if (!registration.Predicate(nodeId))
                {
                    continue;
                }

                if (match != null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "NodeId '{0}' matches more than one virtual-node family.",
                        nodeId);
                }
                match = registration;
            }
            return match;
        }

        internal bool HasMonitoredItemCreatingHandler(NodeId nodeId)
        {
            return m_monitoredItemCreating.ContainsKey(nodeId) ||
                FindVirtualNodeRegistration(nodeId)?.MonitoredItemCreating != null;
        }

        internal bool HasMonitoredItemCreatedHandler(NodeId nodeId)
        {
            return m_monitoredItemCreated.ContainsKey(nodeId) ||
                FindVirtualNodeRegistration(nodeId)?.MonitoredItemCreated != null;
        }

        internal bool HasMonitoredItemModifiedHandler(NodeId nodeId)
        {
            return m_monitoredItemModified.ContainsKey(nodeId) ||
                FindVirtualNodeRegistration(nodeId)?.MonitoredItemModified != null;
        }

        internal bool HasMonitoredItemDeletedHandler(NodeId nodeId)
        {
            return m_monitoredItemDeleted.ContainsKey(nodeId) ||
                FindVirtualNodeRegistration(nodeId)?.MonitoredItemDeleted != null;
        }

        internal bool HasMonitoringModeChangedHandler(NodeId nodeId)
        {
            return m_monitoringModeChanged.ContainsKey(nodeId) ||
                FindVirtualNodeRegistration(nodeId)?.MonitoringModeChanged != null;
        }

        /// <summary>
        /// Event-source registry owned by the
        /// <see cref="FluentNodeManagerBase"/>; populated via
        /// <see cref="AttachEventSources"/> immediately after the
        /// builder is constructed and before <c>Configure</c> runs.
        /// </summary>
        /// <remarks>
        /// Hand-written managers that derive from
        /// <see cref="CustomNodeManager2"/> rather than
        /// <see cref="FluentNodeManagerBase"/> leave this property
        /// <c>null</c>; the <c>Publish</c> extensions surface a
        /// targeted error in that case.
        /// </remarks>
        internal EventSourceRegistry? EventSources { get; private set; }

        /// <summary>
        /// Wires the supplied registry into this builder so the
        /// <c>Publish</c> extensions can route source registrations to
        /// the owning manager. Called once by
        /// <see cref="FluentNodeManagerBase"/>; subsequent calls throw.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// An <see cref="EventSourceRegistry"/> is already attached to
        /// this builder.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="registry"/> is null.
        /// </exception>
        internal void AttachEventSources(EventSourceRegistry registry)
        {
            if (EventSources != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "An EventSourceRegistry is already attached to this builder.");
            }

            EventSources = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// Simulation-loop registry owned by the
        /// <see cref="FluentNodeManagerBase"/>; populated via
        /// <see cref="AttachSimulations"/> immediately after the
        /// builder is constructed and before <c>Configure</c> runs.
        /// </summary>
        /// <remarks>
        /// Hand-written managers that derive from
        /// <see cref="CustomNodeManager2"/> rather than
        /// <see cref="FluentNodeManagerBase"/> leave this property
        /// <c>null</c>; the <c>Simulation</c> extension surfaces a
        /// targeted error in that case.
        /// </remarks>
        internal SimulationRegistry? Simulations { get; private set; }

        internal MonitoredSourceRegistry? MonitoredSources { get; private set; }

        internal FluentNodeManagerBase? FluentOwner { get; private set; }

        /// <summary>
        /// Wires the supplied registry into this builder so the
        /// <see cref="SimulationBuilderExtensions.Simulation(INodeManagerBuilder, TimeSpan)"/>
        /// extension can route loop registrations to the owning manager.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        internal void AttachSimulations(SimulationRegistry registry)
        {
            if (Simulations != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "A SimulationRegistry is already attached to this builder.");
            }
            Simulations = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        internal void AttachMonitoredSources(MonitoredSourceRegistry registry)
        {
            if (MonitoredSources != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "A MonitoredSourceRegistry is already attached to this builder.");
            }
            MonitoredSources = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        internal void AttachOwner(FluentNodeManagerBase owner)
        {
            if (FluentOwner != null && !ReferenceEquals(FluentOwner, owner))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "A different fluent node manager already owns this builder.");
            }
            FluentOwner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        private static string FormatNodeId(NodeId nodeId)
        {
            // NodeId is a readonly struct so the caller may pass `default`;
            // .IsNull guards both the default-struct case and a constructed
            // NodeId with no identifier.
            return nodeId.IsNull ? "(null)" : nodeId.ToString();
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
            if (node != null &&
                m_historyRead.TryGetValue(node.NodeId, out HistoryReadHandler? handler))
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

            if (node != null &&
                FindVirtualNodeRegistration(node.NodeId)?.HistoryRead is { } virtualHandler)
            {
                status = virtualHandler(
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
            if (node != null &&
                m_historyUpdate.TryGetValue(node.NodeId, out HistoryUpdateHandler? handler))
            {
                status = handler(context, node, nodeToUpdate, result);
                return true;
            }

            if (node != null &&
                FindVirtualNodeRegistration(node.NodeId)?.HistoryUpdate is { } virtualHandler)
            {
                status = virtualHandler(context, node, nodeToUpdate, result);
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
            if (source != null &&
                m_monitoredItemCreated.TryGetValue(source.NodeId, out MonitoredItemCreatedHandler? handler))
            {
                handler(context, source, monitoredItem);
                return;
            }

            if (source != null &&
                FindVirtualNodeRegistration(source.NodeId)?.MonitoredItemCreated is { } virtualHandler)
            {
                virtualHandler(context, source, monitoredItem);
            }
        }

        /// <inheritdoc/>
        public ValueTask<MonitoredItemCreateDecision> GetMonitoredItemCreateDecisionAsync(
            MonitoredItemCreateContext context,
            CancellationToken cancellationToken)
        {
            NodeState source = context.Source;
            if (m_monitoredItemCreating.TryGetValue(
                source.NodeId,
                out MonitoredItemCreatingHandler? handler))
            {
                return handler(context, cancellationToken);
            }

            if (FindVirtualNodeRegistration(source.NodeId)?.MonitoredItemCreating is
                { } virtualHandler)
            {
                return virtualHandler(context, cancellationToken);
            }

            return new ValueTask<MonitoredItemCreateDecision>(
                MonitoredItemCreateDecision.UseDefault());
        }

        /// <inheritdoc/>
        public ValueTask NotifyMonitoredItemModifiedAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken)
        {
            if (source != null &&
                m_monitoredItemModified.TryGetValue(
                    source.NodeId,
                    out MonitoredItemModifiedHandler? handler))
            {
                return handler(context, source, monitoredItem, cancellationToken);
            }

            if (source != null &&
                FindVirtualNodeRegistration(source.NodeId)?.MonitoredItemModified is
                    { } virtualHandler)
            {
                return virtualHandler(
                    context,
                    source,
                    monitoredItem,
                    cancellationToken);
            }

            return default;
        }

        /// <inheritdoc/>
        public ValueTask NotifyMonitoredItemDeletedAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken)
        {
            if (source != null &&
                m_monitoredItemDeleted.TryGetValue(
                    source.NodeId,
                    out MonitoredItemDeletedHandler? handler))
            {
                return handler(context, source, monitoredItem, cancellationToken);
            }

            if (source != null &&
                FindVirtualNodeRegistration(source.NodeId)?.MonitoredItemDeleted is
                    { } virtualHandler)
            {
                return virtualHandler(
                    context,
                    source,
                    monitoredItem,
                    cancellationToken);
            }

            return default;
        }

        /// <inheritdoc/>
        public ValueTask NotifyMonitoringModeChangedAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode previousMode,
            MonitoringMode monitoringMode,
            CancellationToken cancellationToken)
        {
            if (source != null &&
                m_monitoringModeChanged.TryGetValue(
                    source.NodeId,
                    out MonitoringModeChangedHandler? handler))
            {
                return handler(
                    context,
                    source,
                    monitoredItem,
                    previousMode,
                    monitoringMode,
                    cancellationToken);
            }

            if (source != null &&
                FindVirtualNodeRegistration(source.NodeId)?.MonitoringModeChanged is
                    { } virtualHandler)
            {
                return virtualHandler(
                    context,
                    source,
                    monitoredItem,
                    previousMode,
                    monitoringMode,
                    cancellationToken);
            }

            return default;
        }

        /// <inheritdoc/>
        public ValueTask NotifyMonitoredItemsCreatedAsync(
            ISystemContext context,
            ArrayOf<IMonitoredItem> monitoredItems,
            CancellationToken cancellationToken)
        {
            return m_monitoredItemsCreated?.Invoke(
                context,
                monitoredItems,
                cancellationToken) ?? default;
        }

        /// <inheritdoc/>
        public ValueTask NotifyMonitoredItemsDeletedAsync(
            ISystemContext context,
            ArrayOf<IMonitoredItem> monitoredItems,
            CancellationToken cancellationToken)
        {
            return m_monitoredItemsDeleted?.Invoke(
                context,
                monitoredItems,
                cancellationToken) ?? default;
        }

        /// <inheritdoc/>
        public void NotifyNodeAdded(ISystemContext context, NodeState node)
        {
            if (node != null &&
                m_nodeAdded.TryGetValue(node.NodeId, out NodeLifecycleHandler? handler))
            {
                handler(context, node);
            }
        }

        /// <inheritdoc/>
        public void NotifyNodeRemoved(ISystemContext context, NodeState node)
        {
            if (node != null &&
                m_nodeRemoved.TryGetValue(node.NodeId, out NodeLifecycleHandler? handler))
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

        internal void RegisterMonitoredItemCreating(
            NodeState node,
            MonitoredItemCreatingHandler handler)
        {
            ThrowIfDuplicate(
                m_monitoredItemCreating,
                node,
                "OnCreateMonitoredItem");
            m_monitoredItemCreating[node.NodeId] = handler;
        }

        internal void RegisterMonitoredItemModified(
            NodeState node,
            MonitoredItemModifiedHandler handler)
        {
            ThrowIfDuplicate(
                m_monitoredItemModified,
                node,
                "OnMonitoredItemModified");
            m_monitoredItemModified[node.NodeId] = handler;
        }

        internal void RegisterMonitoredItemDeleted(
            NodeState node,
            MonitoredItemDeletedHandler handler)
        {
            ThrowIfDuplicate(
                m_monitoredItemDeleted,
                node,
                "OnMonitoredItemDeleted");
            m_monitoredItemDeleted[node.NodeId] = handler;
        }

        internal void RegisterMonitoringModeChanged(
            NodeState node,
            MonitoringModeChangedHandler handler)
        {
            ThrowIfDuplicate(
                m_monitoringModeChanged,
                node,
                "OnMonitoringModeChanged");
            m_monitoringModeChanged[node.NodeId] = handler;
        }

        internal void RegisterMonitoredItemsCreated(MonitoredItemsBatchHandler handler)
        {
            ThrowIfSealed();
            if (m_monitoredItemsCreated != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "A monitored-items-created batch handler is already registered.");
            }
            m_monitoredItemsCreated = handler;
        }

        internal void RegisterMonitoredItemsDeleted(MonitoredItemsBatchHandler handler)
        {
            ThrowIfSealed();
            if (m_monitoredItemsDeleted != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "A monitored-items-deleted batch handler is already registered.");
            }
            m_monitoredItemsDeleted = handler;
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

        internal void RegisterMultiConsumerNode(NodeState node, bool enable)
        {
            if (NodeManager is not AsyncCustomNodeManager acnm)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "AllowMultipleEventConsumers requires the node manager to derive from AsyncCustomNodeManager. " +
                    "Manager type '{0}' does not qualify.",
                    NodeManager?.GetType().FullName ?? "(unknown)");
            }

            if (enable)

            {
                acnm.MultiConsumerNodeIds[node.NodeId] = true;
            }
            else
            {
                acnm.MultiConsumerNodeIds.Remove(node.NodeId);
            }
        }

        private NodeState ResolveNodeId(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "NodeId is null or empty.");
            }

            return m_nodeIdResolver(nodeId) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "NodeId '{0}' did not resolve to a predefined node.",
                    nodeId);
        }

        private NodeState ResolveByTypeDefinition(NodeId typeDefinitionId, QualifiedName browseName)
        {
            if (typeDefinitionId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "TypeDefinitionId is null or empty.");
            }

            IReadOnlyList<NodeState> candidates = m_typeIdResolver(typeDefinitionId)
                ?? [];

            if (candidates.Count == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "No predefined node has TypeDefinitionId '{0}'.",
                    typeDefinitionId);
            }

            if (browseName.IsNull)
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

            NodeState? match = null;
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

        private NodeState ResolveByDataType(NodeId dataTypeId, QualifiedName browseName)
        {
            if (dataTypeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "DataTypeId is null or empty.");
            }

            ArrayOf<NodeState> candidates = m_dataTypeIdResolver(dataTypeId);

            if (candidates.Count == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "No predefined variable has DataType '{0}'.",
                    dataTypeId);
            }

            if (browseName.IsNull)
            {
                if (candidates.Count > 1)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameDuplicated,
                        "DataType '{0}' is ambiguous: {1} matching variables found. " +
                        "Pass a QualifiedName disambiguator to VariableFromDataTypeId.",
                        dataTypeId,
                        candidates.Count);
                }
                return candidates[0];
            }

            NodeState? match = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].BrowseName == browseName)
                {
                    if (match != null)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadBrowseNameDuplicated,
                            "DataType '{0}' has multiple variables with browse name '{1}'.",
                            dataTypeId,
                            browseName);
                    }
                    match = candidates[i];
                }
            }

            if (match == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "DataType '{0}' has no variable with browse name '{1}'.",
                    dataTypeId,
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
                    "Cannot wire additional nodes after the builder has been sealed. " +
                    "All Node(...) calls must occur inside the Configure delegate.");
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
        private readonly Func<NodeId, ArrayOf<NodeState>> m_dataTypeIdResolver;
        private bool m_sealed;
        private readonly Dictionary<NodeId, HistoryReadHandler> m_historyRead = [];
        private readonly Dictionary<NodeId, HistoryUpdateHandler> m_historyUpdate = [];
        private readonly Dictionary<NodeId, MonitoredItemCreatingHandler> m_monitoredItemCreating = [];
        private readonly Dictionary<NodeId, MonitoredItemCreatedHandler> m_monitoredItemCreated = [];
        private readonly Dictionary<NodeId, MonitoredItemModifiedHandler> m_monitoredItemModified = [];
        private readonly Dictionary<NodeId, MonitoredItemDeletedHandler> m_monitoredItemDeleted = [];
        private readonly Dictionary<NodeId, MonitoringModeChangedHandler> m_monitoringModeChanged = [];
        private readonly Dictionary<NodeId, NodeLifecycleHandler> m_nodeAdded = [];
        private readonly Dictionary<NodeId, NodeLifecycleHandler> m_nodeRemoved = [];
        private readonly List<VirtualNodeRegistration> m_virtualNodes = [];
        private MonitoredItemsBatchHandler? m_monitoredItemsCreated;
        private MonitoredItemsBatchHandler? m_monitoredItemsDeleted;
    }
}
