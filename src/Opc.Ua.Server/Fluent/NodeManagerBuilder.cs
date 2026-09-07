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
using Opc.Ua.Export;
using Opc.Ua.Server.Nodes;
using Opc.Ua.Server.RuntimeNodeSet;

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
    public sealed partial class NodeManagerBuilder : INodeManagerBuilder, IFluentDispatcher
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
            SealGraphAuthoring();
            StartSimulations();
        }

        /// <summary>
        /// Closes the builder for further wiring and node authoring without
        /// starting the simulations yet.
        /// </summary>
        /// <remarks>
        /// A manager which replays <c>NotifyNodeAdded</c> after sealing seals
        /// first - so a lifecycle handler cannot author nodes that nothing
        /// would register any more - and starts the simulations only once the
        /// replay is done, so no simulated value change can precede the
        /// <c>OnNodeAdded</c> handler for its own node.
        /// </remarks>
        internal void SealGraphAuthoring()
        {
            if (HasPendingNodeSetImports)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The imported NodeSet documents have not been registered. " +
                    "A manager which imports NodeSets must complete the " +
                    "Configure pass through CompleteConfigureAsync before sealing.");
            }

            m_sealed = true;
        }

        /// <summary>
        /// Starts the simulations registered during the <c>Configure</c> pass.
        /// </summary>
        internal void StartSimulations()
        {
            Simulations?.Start();
        }

        /// <inheritdoc/>
        public void Import(
            UANodeSet nodeSet,
            INodeSetImportFactoryProvider? factoryProvider = null)
        {
            ThrowIfSealed();
            if (nodeSet is null)
            {
                throw new ArgumentNullException(nameof(nodeSet));
            }
            if (m_importsCompleted)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The NodeSet import batch has already been completed.");
            }

            INodeSetImportFactoryProvider? provider = factoryProvider ??
                NodeManager as INodeSetImportFactoryProvider;
            if (m_nodeSetImporter is null)
            {
                m_importFactoryProvider = provider;
                m_nodeSetImporter = new NodeSetImporter(Context, provider);
            }
            else if (!ReferenceEquals(m_importFactoryProvider, provider))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Every document of one import batch must use the same " +
                    "NodeSet import factory provider.");
            }

            ArrayOf<NodeState> imported = m_nodeSetImporter.Import(nodeSet);
            for (int i = 0; i < imported.Count; i++)
            {
                ValidateImportedNode(imported[i]);
            }
        }

        /// <inheritdoc/>
        public INodeBuilder Node(string browsePath)
        {
            ThrowIfSealed();
            NodeState node = BrowsePathResolver.Resolve(
                Context,
                browsePath,
                m_defaultNamespaceIndex,
                ResolveRoot);

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
                ResolveRoot);

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
                ResolveRoot);
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

            if (m_nodeSetImporter?.TryGetNode(nodeId, out NodeState? imported) == true)
            {
                return imported!;
            }
            if (m_authoredNodes.TryGetValue(nodeId, out NodeState? authored))
            {
                return authored;
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

            IReadOnlyList<NodeState> candidates = CollectAuthoredCandidates(
                m_typeIdResolver(typeDefinitionId) ?? [],
                node => node is BaseInstanceState instance &&
                    instance.TypeDefinitionId == typeDefinitionId);

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

            List<NodeState> candidates = CollectAuthoredCandidates(
                m_dataTypeIdResolver(dataTypeId),
                node => node is BaseVariableState variable &&
                    variable.DataType == dataTypeId);

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

        /// <summary>
        /// Gets the nodes contributed through <see cref="Import"/>.
        /// </summary>
        internal NodeStateCollection ImportedNodes =>
            m_nodeSetImporter?.ImportedNodes ?? [];

        /// <summary>
        /// Gets whether an import batch is waiting to be linked and registered.
        /// </summary>
        internal bool HasPendingNodeSetImports =>
            m_nodeSetImporter is not null && !m_importsCompleted;

        /// <summary>
        /// Records that the user's <c>Configure</c> pass wired a node, so a
        /// NodeSet import cannot silently discard that configuration.
        /// </summary>
        internal void MarkConfigured(NodeState node)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            m_configuredNodes.Add(node);
        }

        /// <summary>
        /// Links the import batch collected during <c>Configure</c> and
        /// registers the imported nodes with the owning node manager.
        /// </summary>
        /// <remarks>
        /// Linking happens exactly once for the whole batch, so a child may
        /// declare a parent from another document or a node the manager
        /// already owns. An imported child which lands in an explicitly
        /// defined slot of a generated parent replaces the generated
        /// placeholder; the placeholder and every descendant it does not carry
        /// over are then removed from the manager.
        /// </remarks>
        /// <param name="existingNodes">
        /// The nodes the manager owned before the import, keyed by NodeId.
        /// </param>
        /// <param name="addNodeAsync">Registers an imported node subtree.</param>
        /// <param name="removeNodeAsync">Removes a displaced node subtree.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        internal async ValueTask CompleteNodeSetImportsAsync(
            IReadOnlyDictionary<NodeId, NodeState> existingNodes,
            Func<NodeState, CancellationToken, ValueTask> addNodeAsync,
            Func<NodeState, CancellationToken, ValueTask> removeNodeAsync,
            CancellationToken cancellationToken = default)
        {
            if (existingNodes is null)
            {
                throw new ArgumentNullException(nameof(existingNodes));
            }
            if (addNodeAsync is null)
            {
                throw new ArgumentNullException(nameof(addNodeAsync));
            }
            if (removeNodeAsync is null)
            {
                throw new ArgumentNullException(nameof(removeNodeAsync));
            }
            if (m_nodeSetImporter is null || m_importsCompleted)
            {
                return;
            }

            m_importsCompleted = true;
            var replacements = new List<(NodeState Replaced, BaseInstanceState Replacement)>();
            m_nodeSetImporter.Complete(
                existingNodes,
                node => !node.NodeId.IsNull &&
                    existingNodes.TryGetValue(node.NodeId, out NodeState? existing) &&
                    ReferenceEquals(existing, node),
                (replaced, replacement) =>
                    replacements.Add((replaced, replacement)));

            Dictionary<NodeId, NodeId> mappings = ApplyReplacements(
                replacements,
                out List<NodeState> displacedRoots);

            for (int i = 0; i < displacedRoots.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await removeNodeAsync(displacedRoots[i], cancellationToken)
                    .ConfigureAwait(false);
            }

            NodeStateCollection importedNodes = m_nodeSetImporter.ImportedNodes;
            for (int i = 0; i < importedNodes.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NodeState node = importedNodes[i];
                if (HasImportedParent(node))
                {
                    // Registered together with the imported ancestor.
                    continue;
                }
                await addNodeAsync(node, cancellationToken).ConfigureAwait(false);
            }

            RetargetReplacedReferences(existingNodes, mappings);
        }

        /// <summary>
        /// Rejects displaced placeholders which the Configure pass already
        /// wired and reports the NodeId changes the replacements introduce.
        /// </summary>
        private Dictionary<NodeId, NodeId> ApplyReplacements(
            List<(NodeState Replaced, BaseInstanceState Replacement)> replacements,
            out List<NodeState> displacedRoots)
        {
            var mappings = new Dictionary<NodeId, NodeId>();
            displacedRoots = [];
            if (replacements.Count == 0)
            {
                return mappings;
            }

            for (int i = 0; i < replacements.Count; i++)
            {
                displacedRoots.Add(replacements[i].Replaced);
            }

            var pending = new List<(NodeState Replaced, NodeState Replacement)>();
            for (int i = 0; i < replacements.Count; i++)
            {
                pending.Add((replacements[i].Replaced, replacements[i].Replacement));
            }

            var replacedChildren = new List<BaseInstanceState>();
            for (int i = 0; i < pending.Count; i++)
            {
                (NodeState replaced, NodeState replacement) = pending[i];
                ThrowIfConfiguredPlaceholder(replaced, replacement);
                if (!replaced.NodeId.IsNull &&
                    !replacement.NodeId.IsNull &&
                    replaced.NodeId != replacement.NodeId)
                {
                    mappings[replaced.NodeId] = replacement.NodeId;
                }

                replacedChildren.Clear();
                replaced.GetChildren(Context, replacedChildren);
                for (int ii = 0; ii < replacedChildren.Count; ii++)
                {
                    BaseInstanceState replacedChild = replacedChildren[ii];
                    BaseInstanceState? replacementChild = replacement.FindChild(
                        Context,
                        replacedChild.BrowseName);
                    if (replacementChild is not null)
                    {
                        pending.Add((replacedChild, replacementChild));
                    }
                    else
                    {
                        // The imported node does not carry this descendant, so
                        // it disappears with the placeholder.
                        ThrowIfConfiguredSubtree(replacedChild);
                    }
                }
            }

            return mappings;
        }

        /// <summary>
        /// Rewrites references of the pre-import nodes which still point at a
        /// placeholder that an imported node replaced under a different id.
        /// </summary>
        private void RetargetReplacedReferences(
            IReadOnlyDictionary<NodeId, NodeState> existingNodes,
            Dictionary<NodeId, NodeId> mappings)
        {
            if (mappings.Count == 0)
            {
                return;
            }

            foreach (NodeState node in existingNodes.Values)
            {
                if (node is BaseInstanceState { Parent: not null })
                {
                    // UpdateReferenceTargets already recurses from the root.
                    continue;
                }
                node.UpdateReferenceTargets(Context, mappings);
            }
        }

        private void ThrowIfConfiguredSubtree(NodeState root)
        {
            var nodes = new List<NodeState> { root };
            var children = new List<BaseInstanceState>();
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeState node = nodes[i];
                NodeState? survivor = null;
                if (!node.NodeId.IsNull &&
                    m_nodeSetImporter?.TryGetNode(
                        node.NodeId,
                        out NodeState? imported) == true &&
                    !ReferenceEquals(imported, node))
                {
                    survivor = imported;
                }
                ThrowIfConfiguredPlaceholder(node, survivor);

                children.Clear();
                node.GetChildren(Context, children);
                nodes.AddRange(children);
            }
        }

        /// <summary>
        /// Throws when a node the import displaces carries configuration which
        /// the replacement cannot inherit.
        /// </summary>
        private void ThrowIfConfiguredPlaceholder(
            NodeState node,
            NodeState? replacement)
        {
            // Configuration keyed by NodeId survives when the replacement
            // keeps the same id, because it still addresses the live node.
            bool replacementKeepsNodeId =
                replacement is not null &&
                replacement.NodeId == node.NodeId &&
                m_configuredNodes.Contains(replacement) &&
                !m_configuredNodes.Contains(node);
            bool keyedConfiguration =
                !replacementKeepsNodeId &&
                (m_historyRead.ContainsKey(node.NodeId) ||
                    m_historyUpdate.ContainsKey(node.NodeId) ||
                    m_monitoredItemCreating.ContainsKey(node.NodeId) ||
                    m_monitoredItemCreated.ContainsKey(node.NodeId) ||
                    m_monitoredItemModified.ContainsKey(node.NodeId) ||
                    m_monitoredItemDeleted.ContainsKey(node.NodeId) ||
                    m_monitoringModeChanged.ContainsKey(node.NodeId) ||
                    m_nodeAdded.ContainsKey(node.NodeId) ||
                    m_nodeRemoved.ContainsKey(node.NodeId));

            bool configured = m_configuredNodes.Contains(node) || keyedConfiguration;
            if (!configured && node is BaseVariableState variable)
            {
                configured =
                    variable.OnSimpleReadValue is not null ||
                    variable.OnSimpleWriteValue is not null ||
                    variable.OnReadValue is not null ||
                    variable.OnWriteValue is not null ||
                    variable.OnReadValueAsync is not null ||
                    variable.OnSimpleReadValueAsync is not null ||
                    variable.OnWriteValueAsync is not null ||
                    variable.OnSimpleWriteValueAsync is not null ||
                    variable.OnReadDataType is not null ||
                    variable.OnWriteDataType is not null ||
                    variable.OnReadValueRank is not null ||
                    variable.OnWriteValueRank is not null ||
                    variable.OnReadArrayDimensions is not null ||
                    variable.OnWriteArrayDimensions is not null ||
                    variable.OnReadAccessLevel is not null ||
                    variable.OnWriteAccessLevel is not null ||
                    variable.OnReadUserAccessLevel is not null ||
                    variable.OnWriteUserAccessLevel is not null ||
                    variable.OnReadMinimumSamplingInterval is not null ||
                    variable.OnWriteMinimumSamplingInterval is not null ||
                    variable.OnReadHistorizing is not null ||
                    variable.OnWriteHistorizing is not null ||
                    variable.OnReadAccessLevelEx is not null ||
                    variable.OnWriteAccessLevelEx is not null;
            }
            if (!configured && node is MethodState method)
            {
                configured =
                    method.OnReadExecutable is not null ||
                    method.OnWriteExecutable is not null ||
                    method.OnReadUserExecutable is not null ||
                    method.OnWriteUserExecutable is not null ||
                    method.OnCallMethod is not null ||
                    method.OnCallMethod2 is not null ||
                    method.OnCallMethod2Async is not null;
            }
            if (!configured && node is BaseObjectState objectState)
            {
                configured =
                    objectState.OnReadEventNotifier is not null ||
                    objectState.OnWriteEventNotifier is not null;
            }
            if (!configured &&
                !replacementKeepsNodeId &&
                NodeManager is AsyncCustomNodeManager manager &&
                manager.MultiConsumerNodeIds.ContainsKey(node.NodeId))
            {
                configured = true;
            }

            if (configured)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Imported node '{0}' replaces configured node '{1}'. " +
                    "Import the NodeSet before wiring that node.",
                    node.BrowseName,
                    node.NodeId);
            }
        }

        private bool HasImportedParent(NodeState node)
        {
            return node is BaseInstanceState { Parent: { } parent } &&
                !parent.NodeId.IsNull &&
                m_nodeSetImporter?.TryGetNode(
                    parent.NodeId,
                    out NodeState? importedParent) == true &&
                ReferenceEquals(importedParent, parent);
        }


        /// <summary>
        /// Gets whether a node the manager owns today occupies a generated slot
        /// which an imported node is going to take over.
        /// </summary>
        private bool IsImportedReplacementPlaceholder(NodeState candidate)
        {
            return m_nodeSetImporter is not null &&
                candidate is BaseInstanceState { Parent: { } parent } instance &&
                parent.IsExplicitlyDefinedChild(Context, instance) &&
                !candidate.NodeId.IsNull &&
                ((m_nodeSetImporter.TryGetNode(
                        candidate.NodeId,
                        out NodeState? imported) &&
                    !ReferenceEquals(candidate, imported)) ||
                m_nodeSetImporter.TryGetTypedReplacement(
                    candidate,
                    node => !IsImportedNode(node),
                    out _));
        }

        private bool IsImportedNode(NodeState node)
        {
            return !node.NodeId.IsNull &&
                m_nodeSetImporter?.TryGetNode(
                    node.NodeId,
                    out NodeState? imported) == true &&
                ReferenceEquals(imported, node);
        }

        private void ValidateImportedNode(NodeState node)
        {
            if (node.NodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "Imported node '{0}' has no NodeId.",
                    node.BrowseName);
            }

            if (m_nodeIdResolver(node.NodeId) is { } existing &&
                !ReferenceEquals(existing, node) &&
                existing is not BaseInstanceState { Parent: not null })
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdExists,
                    "Imported node '{0}' uses the NodeId of an existing root node.",
                    node.NodeId);
            }
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

        private sealed class NodeStateReferenceComparer : IEqualityComparer<NodeState>
        {
            public static NodeStateReferenceComparer Instance { get; } = new();

            public bool Equals(NodeState? left, NodeState? right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(NodeState state)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(state);
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
        private readonly HashSet<NodeState> m_configuredNodes = new(
            NodeStateReferenceComparer.Instance);
        private INodeSetImportFactoryProvider? m_importFactoryProvider;
        private NodeSetImporter? m_nodeSetImporter;
        private bool m_importsCompleted;
    }
}
