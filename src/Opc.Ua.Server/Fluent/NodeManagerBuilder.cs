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
    /// Default implementation of <see cref="INodeManagerBuilder"/>,
    /// <see cref="INodeGraphBuilder"/>, and <see cref="IFluentDispatcher"/>.
    /// Built and owned by the source-generated <c>NodeManagerBase</c> (or by a
    /// hand-written manager that wants to opt in to the fluent surface).
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
    public sealed class NodeManagerBuilder :
        INodeGraphBuilder,
        IFluentDispatcher
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

        /// <summary>
        /// Gets the namespace index used by unqualified fluent authoring helpers.
        /// </summary>
        internal ushort DefaultNamespaceIndex => m_defaultNamespaceIndex;

        /// <inheritdoc/>
        public IFluentDispatcher Dispatcher => this;

        INodeBuilder<TState> INodeGraphBuilder.Add<TState>(
            TState node,
            NodeId parentId)
        {
            return AddNode(node, parentId, attachDefaultParent: true);
        }

        INodeBuilder<TState> INodeGraphBuilder.Add<TState>(
            Func<NodeState?, TState> factory,
            NodeId parentId)
        {
            ThrowIfGraphAuthoringUnavailable();
            ThrowIfSealed();
            ThrowIfGraphFinalizationStarted();
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            NodeId effectiveParentId = parentId.IsNull
                ? ObjectIds.ObjectsFolder
                : parentId;
            NodeState? parent = ResolveAuthoredOrRegisteredNode(effectiveParentId);
            if (parent is null && IsOwnedNamespace(effectiveParentId.NamespaceIndex))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "Parent '{0}' belongs to this source but has not been authored.",
                    effectiveParentId);
            }

            NodeState factoryParent = parent ??
                new ExternalParentState(effectiveParentId);
            TState node = factory(factoryParent) ??
                throw new InvalidOperationException(
                    "The graph node factory returned null.");
            if (parent is null &&
                node is BaseInstanceState instance &&
                ReferenceEquals(instance.Parent, factoryParent))
            {
                instance.Parent = null;
            }

            NodeBuilder<TState> result = AddNode(
                node,
                parentId,
                attachDefaultParent: true);
            MarkFactoryAuthoredSubtree(node);
            return result;
        }

        INodeBuilder<TState> INodeGraphBuilder.AddRoot<TState>(TState node)
        {
            NodeBuilder<TState> result = AddNode(
                node,
                NodeId.Null,
                attachDefaultParent: false);
            MarkFactoryAuthoredSubtree(node);
            return result;
        }

        bool INodeGraphBuilder.TryGetNode(
            NodeId nodeId,
            out NodeState? node)
        {
            ThrowIfGraphAuthoringUnavailable();
            ThrowIfSealed();
            if (nodeId.IsNull)
            {
                node = null;
                return false;
            }
            if (m_nodeSetImporter?.TryGetNode(nodeId, out node) == true)
            {
                return true;
            }
            if (m_authoredNodes.TryGetValue(nodeId, out node))
            {
                return true;
            }

            node = m_nodeIdResolver(nodeId);
            return node is not null;
        }

        void INodeGraphBuilder.Import(UANodeSet nodeSet)
        {
            ThrowIfGraphAuthoringUnavailable();
            ThrowIfSealed();
            ThrowIfGraphFinalizationStarted();
            if (nodeSet is null)
            {
                throw new ArgumentNullException(nameof(nodeSet));
            }

            m_nodeSetImporter ??= new NodeSetImporter(
                Context,
                m_importFactoryProvider);
            ArrayOf<NodeState> imported = m_nodeSetImporter.Import(nodeSet);
            for (int i = 0; i < imported.Count; i++)
            {
                NodeState node = imported[i];
                ValidateImportedNode(node);
                SnapshotImportedReferences(node);
            }
        }

        private NodeBuilder<TState> AddNode<TState>(
            TState node,
            NodeId parentId,
            bool attachDefaultParent = true)
            where TState : NodeState
        {
            ThrowIfGraphAuthoringUnavailable();
            ThrowIfSealed();
            ThrowIfGraphFinalizationStarted();
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            AttachToParent(node, parentId, attachDefaultParent);
            PrepareNodeIds(node);
            IndexAuthoredSubtree(node);

            if (!HasAuthoredAncestor(node))
            {
                AddAuthoredRoot(node);
            }

            return new NodeBuilder<TState>(this, node);
        }

        INodeBuilder<FolderState> INodeGraphBuilder.AddFolder(
            string browseName,
            NodeId parentId)
        {
            return AddFolder(CreateDefaultBrowseName(browseName), parentId);
        }

        INodeBuilder<FolderState> INodeGraphBuilder.AddFolder(
            QualifiedName browseName,
            NodeId parentId)
        {
            return AddFolder(ValidateExplicitBrowseName(browseName), parentId);
        }

        private NodeBuilder<FolderState> AddFolder(
            QualifiedName browseName,
            NodeId parentId)
        {
            string symbolicName = browseName.Name!;
            var folder = new FolderState(null)
            {
                SymbolicName = symbolicName,
                BrowseName = browseName,
                DisplayName = new LocalizedText(symbolicName),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType
            };
            return AddNode(folder, parentId);
        }

        INodeBuilder<BaseObjectState> INodeGraphBuilder.AddObject(
            string browseName,
            NodeId parentId,
            NodeId typeDefinitionId)
        {
            return AddObject(
                CreateDefaultBrowseName(browseName),
                parentId,
                typeDefinitionId);
        }

        INodeBuilder<BaseObjectState> INodeGraphBuilder.AddObject(
            QualifiedName browseName,
            NodeId parentId,
            NodeId typeDefinitionId)
        {
            return AddObject(
                ValidateExplicitBrowseName(browseName),
                parentId,
                typeDefinitionId);
        }

        private NodeBuilder<BaseObjectState> AddObject(
            QualifiedName browseName,
            NodeId parentId,
            NodeId typeDefinitionId)
        {
            string symbolicName = browseName.Name!;
            var instance = new BaseObjectState(null)
            {
                SymbolicName = symbolicName,
                BrowseName = browseName,
                DisplayName = new LocalizedText(symbolicName),
                TypeDefinitionId = typeDefinitionId.IsNull
                    ? ObjectTypeIds.BaseObjectType
                    : typeDefinitionId
            };
            return AddNode(instance, parentId);
        }

        IVariableBuilder<TValue> INodeGraphBuilder.AddVariable<TValue>(
            string browseName,
            NodeId parentId)
        {
            return AddVariable<TValue>(
                CreateDefaultBrowseName(browseName),
                parentId);
        }

        IVariableBuilder<TValue> INodeGraphBuilder.AddVariable<TValue>(
            QualifiedName browseName,
            NodeId parentId)
        {
            return AddVariable<TValue>(
                ValidateExplicitBrowseName(browseName),
                parentId);
        }

        private VariableBuilder<TValue> AddVariable<TValue>(
            QualifiedName browseName,
            NodeId parentId)
        {
            string symbolicName = browseName.Name!;
            var variable = new BaseDataVariableState(null)
            {
                SymbolicName = symbolicName,
                BrowseName = browseName,
                DisplayName = new LocalizedText(symbolicName),
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = TypeInfo.GetDataTypeId(typeof(TValue), Context.NamespaceUris),
                ValueRank = TypeInfo.GetValueRank(typeof(TValue)),
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead
            };
            NodeBuilder<BaseDataVariableState> nodeBuilder =
                AddNode(variable, parentId);
            return ToVariableBuilder<TValue>(nodeBuilder.Node, browseName.ToString());
        }

        INodeBuilder<MethodState> INodeGraphBuilder.AddMethod(
            string browseName,
            NodeId parentId)
        {
            return AddMethod(CreateDefaultBrowseName(browseName), parentId);
        }

        INodeBuilder<MethodState> INodeGraphBuilder.AddMethod(
            QualifiedName browseName,
            NodeId parentId)
        {
            return AddMethod(ValidateExplicitBrowseName(browseName), parentId);
        }

        private NodeBuilder<MethodState> AddMethod(
            QualifiedName browseName,
            NodeId parentId)
        {
            string symbolicName = browseName.Name!;
            var method = new MethodState(null)
            {
                SymbolicName = symbolicName,
                BrowseName = browseName,
                DisplayName = new LocalizedText(symbolicName),
                Executable = true,
                UserExecutable = true
            };
            return AddNode(method, parentId);
        }

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

        internal void SealGraphAuthoring()
        {
            m_sealed = true;
        }

        internal void StartSimulations()
        {
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

        internal async ValueTask RegisterAuthoredNodesAsync(
            Func<NodeState, CancellationToken, ValueTask> register,
            CancellationToken cancellationToken = default)
        {
            ThrowIfGraphAuthoringUnavailable();
            ThrowIfSealed();
            if (register is null)
            {
                throw new ArgumentNullException(nameof(register));
            }
            if (m_authoredNodesRegistered)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The authored node graph has already been registered.");
            }

            CompleteImportedNodes();
            m_authoredNodesRegistered = true;
            foreach (NodeState root in m_authoredRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await register(
                    root,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        internal void EnableGraphAuthoring(
            INodeSetImportFactoryProvider? importFactoryProvider = null,
            Action<NodeState>? importedNodeValidator = null)
        {
            ThrowIfSealed();
            if (m_graphAuthoringEnabled)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Node graph authoring has already been enabled.");
            }

            m_importFactoryProvider = importFactoryProvider;
            m_importedNodeValidator = importedNodeValidator;
            m_graphAuthoringEnabled = true;
        }

        /// <summary>
        /// Gets the nodes contributed through NodeSet imports.
        /// </summary>
        internal NodeStateCollection ImportedNodes =>
            m_nodeSetImporter?.ImportedNodes ?? [];

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

        internal void MarkConfigured(NodeState node)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            m_configuredNodes.Add(node);
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

        private static void AddLookupCandidate(
            List<NodeState> candidates,
            NodeState candidate)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (ReferenceEquals(candidates[i], candidate))
                {
                    return;
                }
            }
            candidates.Add(candidate);
        }

        private bool IsImportedReplacementPlaceholder(NodeState candidate)
        {
            return m_factoryAuthoredNodes.Contains(candidate) &&
                candidate is BaseInstanceState { Parent: { } parent } instance &&
                parent.IsExplicitlyDefinedChild(Context, instance) &&
                !candidate.NodeId.IsNull &&
                ((m_nodeSetImporter?.TryGetNode(
                        candidate.NodeId,
                        out NodeState? imported) == true &&
                    !ReferenceEquals(candidate, imported)) ||
                m_nodeSetImporter?.TryGetTypedReplacement(
                    candidate,
                    m_factoryAuthoredNodes.Contains,
                    out _) == true);
        }

        private NodeState ResolveByTypeDefinition(NodeId typeDefinitionId, QualifiedName browseName)
        {
            if (typeDefinitionId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "TypeDefinitionId is null or empty.");
            }

            var candidates = new List<NodeState>();
            NodeStateCollection importedNodes = ImportedNodes;
            for (int i = 0; i < importedNodes.Count; i++)
            {
                if (importedNodes[i] is BaseInstanceState importedInstance &&
                    importedInstance.TypeDefinitionId == typeDefinitionId)
                {
                    AddLookupCandidate(
                        candidates,
                        importedInstance);
                }
            }
            foreach (NodeState authored in m_authoredNodes.Values)
            {
                if (authored is BaseInstanceState instance &&
                    instance.TypeDefinitionId == typeDefinitionId &&
                    !IsImportedReplacementPlaceholder(authored))
                {
                    AddLookupCandidate(candidates, authored);
                }
            }
            IReadOnlyList<NodeState> resolved =
                m_typeIdResolver(typeDefinitionId) ?? [];
            for (int i = 0; i < resolved.Count; i++)
            {
                AddLookupCandidate(candidates, resolved[i]);
            }

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

            var candidates = new List<NodeState>();
            NodeStateCollection importedNodes = ImportedNodes;
            for (int i = 0; i < importedNodes.Count; i++)
            {
                if (importedNodes[i] is BaseVariableState importedVariable &&
                    importedVariable.DataType == dataTypeId)
                {
                    AddLookupCandidate(
                        candidates,
                        importedVariable);
                }
            }
            foreach (NodeState authored in m_authoredNodes.Values)
            {
                if (authored is BaseVariableState variable &&
                    variable.DataType == dataTypeId &&
                    !IsImportedReplacementPlaceholder(authored))
                {
                    AddLookupCandidate(candidates, authored);
                }
            }
            ArrayOf<NodeState> resolved = m_dataTypeIdResolver(dataTypeId);
            for (int i = 0; i < resolved.Count; i++)
            {
                AddLookupCandidate(candidates, resolved[i]);
            }

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

        private NodeState ResolveRoot(QualifiedName browseName)
        {
            foreach (NodeState root in m_authoredRoots)
            {
                if (root.BrowseName == browseName)
                {
                    return root;
                }
            }
            return m_rootResolver(browseName);
        }

        private void AttachToParent(
            NodeState node,
            NodeId parentId,
            bool attachDefaultParent)
        {
            if (node is not BaseInstanceState instance)
            {
                if (!parentId.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeClassInvalid,
                        "Node '{0}' is not an instance and cannot be attached to parent '{1}'.",
                        node.BrowseName,
                        parentId);
                }
                return;
            }

            bool useDefaultReferenceType = instance.ReferenceTypeId.IsNull;
            NodeState? parent = instance.Parent;
            NodeId effectiveParentId = parentId;
            if (parent != null)
            {
                if (!parentId.IsNull && parent.NodeId != parentId)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidArgument,
                        "Node '{0}' is already attached to parent '{1}', not '{2}'.",
                        node.BrowseName,
                        parent.NodeId,
                        parentId);
                }
                effectiveParentId = parent.NodeId;
                if (effectiveParentId.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdInvalid,
                        "The existing parent of node '{0}' has no NodeId.",
                        node.BrowseName);
                }

                bool knownParent = IsStagedNode(parent) ||
                    ReferenceEquals(m_nodeIdResolver(effectiveParentId), parent);
                if (!knownParent)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdUnknown,
                        "The existing parent '{0}' of node '{1}' is not part of this graph or address space.",
                        effectiveParentId,
                        node.BrowseName);
                }

            }
            else
            {
                if (effectiveParentId.IsNull && attachDefaultParent)
                {
                    effectiveParentId = ObjectIds.ObjectsFolder;
                }

                if (!effectiveParentId.IsNull)
                {
                    parent = ResolveAuthoredOrRegisteredNode(effectiveParentId);
                }

                if (parent == null &&
                    IsOwnedNamespace(effectiveParentId.NamespaceIndex))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdUnknown,
                        "Parent '{0}' belongs to this source but has not been authored.",
                        effectiveParentId);
                }
            }

            if (useDefaultReferenceType && !effectiveParentId.IsNull)
            {
                instance.ReferenceTypeId =
                    effectiveParentId == ObjectIds.ObjectsFolder ||
                    instance is FolderState
                        ? ReferenceTypeIds.Organizes
                        : ReferenceTypeIds.HasComponent;
            }

            if (parent != null)
            {
                AddChildIfMissing(parent, instance);
                return;
            }

            if (!effectiveParentId.IsNull)
            {
                if (NodeManager is NodeSourceNodeManager sourceManager)
                {
                    sourceManager.SetExternalParent(node, effectiveParentId);
                }
                instance.AddReferenceIfMissing(
                    instance.ReferenceTypeId,
                    true,
                    effectiveParentId);
            }
        }

        private void PrepareNodeIds(NodeState node)
        {
            if (node.BrowseName.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameInvalid,
                    "A node added to the graph must have a browse name.");
            }
            if (NodeManager is not AsyncCustomNodeManager manager)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Node graph creation requires an AsyncCustomNodeManager-backed builder.");
            }

            manager.PrepareAuthoredNodeIdsForRegistration(node);
            if (node.NodeId.IsNull)
            {
                node.NodeId = Context.RequireNodeIdFactory().New(Context, node);
            }
            if (node.NodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The NodeId factory did not assign an id to node '{0}'.",
                    node.BrowseName);
            }
            ValidateAuthoredNodeId(manager, node);

            var descendants = new List<BaseInstanceState>();
            var children = new List<BaseInstanceState>();
            node.GetChildren(Context, descendants);
            for (int i = 0; i < descendants.Count; i++)
            {
                BaseInstanceState child = descendants[i];
                manager.PrepareAuthoredNodeIdsForRegistration(child);
                ValidateAuthoredNodeId(manager, child);
                children.Clear();
                child.GetChildren(Context, children);
                descendants.AddRange(children);
            }
        }

        private NodeState? ResolveAuthoredOrRegisteredNode(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                return null;
            }
            if (m_nodeSetImporter?.TryGetNode(nodeId, out NodeState? imported) == true)
            {
                return imported;
            }
            if (m_authoredNodes.TryGetValue(nodeId, out NodeState? authored))
            {
                return authored;
            }
            return m_nodeIdResolver(nodeId);
        }

        private void MarkFactoryAuthoredSubtree(NodeState root)
        {
            var nodes = new List<NodeState> { root };
            var children = new List<BaseInstanceState>();
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeState node = nodes[i];
                m_factoryAuthoredNodes.Add(node);
                children.Clear();
                node.GetChildren(Context, children);
                nodes.AddRange(children);
            }
        }

        private void IndexAuthoredSubtree(NodeState root)
        {
            var nodes = new List<NodeState> { root };
            var children = new List<BaseInstanceState>();
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeState node = nodes[i];
                IndexAuthoredNode(node);

                children.Clear();
                node.GetChildren(Context, children);
                nodes.AddRange(children);
            }
        }

        private void IndexAuthoredNode(NodeState node)
        {
            if (node.NodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The NodeId factory did not assign an id to node '{0}'.",
                    node.BrowseName);
            }

            if (m_authoredNodes.TryGetValue(
                node.NodeId,
                out NodeState? existing))
            {
                if (!ReferenceEquals(existing, node))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdExists,
                        "NodeId '{0}' is already used by node '{1}'.",
                        node.NodeId,
                        existing.BrowseName);
                }
                return;
            }

            NodeState? predefined = m_nodeIdResolver(node.NodeId);
            if (predefined != null && !ReferenceEquals(predefined, node))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdExists,
                    "NodeId '{0}' is already used by a predefined node.",
                    node.NodeId);
            }
            m_authoredNodes.Add(node.NodeId, node);
        }

        private void CompleteImportedNodes()
        {
            if (m_nodeSetImporter is null)
            {
                return;
            }

            m_nodeSetImporter.Complete(
                m_authoredNodes,
                m_factoryAuthoredNodes.Contains,
                (replaced, replacement) =>
                    m_importedReplacements.Add((replaced, replacement)));
            NodeStateCollection importedNodes = m_nodeSetImporter.ImportedNodes;
            for (int i = 0; i < importedNodes.Count; i++)
            {
                NodeState node = importedNodes[i];
                if (node is BaseInstanceState { Parent: null } instance &&
                    UANodeSet.TryGetUnresolvedParentNodeId(
                        instance,
                        out NodeId parentNodeId) &&
                    IsOwnedNamespace(parentNodeId.NamespaceIndex))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdUnknown,
                        "Imported node '{0}' references missing parent '{1}' " +
                        "in a namespace owned by this source.",
                        node.NodeId,
                        parentNodeId);
                }
                if (node is not BaseInstanceState { Parent: not null })
                {
                    AddAuthoredRoot(node);
                }
            }
            ApplyImportedReplacementMappings();
            RemoveOmittedImportedSubtrees();
            IndexImportedNodes();
            PruneUnreachableAuthoredNodes();
            m_factoryAuthoredNodes.Clear();
            m_omittedImportedNodeIds.Clear();
            m_omittedImportedNodes.Clear();
        }

        private void IndexImportedNodes()
        {
            NodeStateCollection importedNodes = m_nodeSetImporter!.ImportedNodes;
            for (int i = 0; i < importedNodes.Count; i++)
            {
                if (!m_omittedImportedNodes.Contains(importedNodes[i]))
                {
                    IndexAuthoredNode(importedNodes[i]);
                }
            }
        }

        private void ApplyImportedReplacementMappings()
        {
            if (m_importedReplacements.Count == 0)
            {
                return;
            }

            var mappings = new Dictionary<NodeId, NodeId>();
            var replacements = new List<(NodeState Replaced, NodeState Replacement)>();
            for (int i = 0; i < m_importedReplacements.Count; i++)
            {
                replacements.Add(m_importedReplacements[i]);
            }
            var replacedChildren = new List<BaseInstanceState>();
            for (int i = 0; i < replacements.Count; i++)
            {
                (NodeState replaced, NodeState replacement) = replacements[i];
                ThrowIfConfiguredImportedPlaceholder(replaced, replacement);
                if (!replaced.NodeId.IsNull)
                {
                    m_authoredNodes.Remove(replaced.NodeId);
                }
                m_factoryAuthoredNodes.Remove(replaced);
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
                        replacements.Add((replacedChild, replacementChild));
                    }
                    else
                    {
                        ThrowIfConfiguredImportedSubtree(replacedChild);
                    }
                }
            }

            if (mappings.Count > 0)
            {
                UpdateAuthoredReferenceTargets(mappings);
            }
            m_importedReplacements.Clear();
        }

        private void UpdateAuthoredReferenceTargets(
            Dictionary<NodeId, NodeId> mappings)
        {
            var nodes = new List<NodeState>(m_authoredRoots);
            var children = new List<BaseInstanceState>();
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeState node = nodes[i];
                if (IsImportedNode(node))
                {
                    node.UpdateOwnReferenceTargets(
                        Context,
                        mappings,
                        reference => !IsOriginalImportedReference(
                            node,
                            reference));
                }
                else
                {
                    node.UpdateOwnReferenceTargets(Context, mappings);
                }
                children.Clear();
                node.GetChildren(Context, children);
                nodes.AddRange(children);
            }
        }

        private void SnapshotImportedReferences(NodeState node)
        {
            var references = new List<IReference>();
            node.GetReferences(Context, references);
            var snapshot = new HashSet<ReferenceIdentity>();
            for (int i = 0; i < references.Count; i++)
            {
                snapshot.Add(ReferenceIdentity.From(references[i]));
            }
            m_importedReferenceSnapshots[node] = snapshot;
        }

        private bool IsOriginalImportedReference(
            NodeState node,
            IReference reference)
        {
            return m_importedReferenceSnapshots.TryGetValue(
                    node,
                    out HashSet<ReferenceIdentity>? snapshot) &&
                snapshot.Contains(ReferenceIdentity.From(reference));
        }

        private bool IsImportedNode(NodeState node)
        {
            return !node.NodeId.IsNull &&
                m_nodeSetImporter?.TryGetNode(
                    node.NodeId,
                    out NodeState? imported) == true &&
                ReferenceEquals(imported, node);
        }

        private void ThrowIfConfiguredImportedSubtree(NodeState root)
        {
            var nodes = new List<NodeState> { root };
            var children = new List<BaseInstanceState>();
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeState node = nodes[i];
                NodeState? survivingImportedNode = null;
                if (!node.NodeId.IsNull &&
                    m_nodeSetImporter?.TryGetNode(
                        node.NodeId,
                        out NodeState? imported) == true &&
                    !ReferenceEquals(imported, node))
                {
                    survivingImportedNode = imported;
                }
                ThrowIfConfiguredImportedPlaceholder(
                    node,
                    survivingImportedNode);
                if (!node.NodeId.IsNull)
                {
                    m_omittedImportedNodeIds.Add(node.NodeId);
                }
                m_omittedImportedNodes.Add(node);
                children.Clear();
                node.GetChildren(Context, children);
                nodes.AddRange(children);
            }
        }

        private void RemoveOmittedImportedSubtrees()
        {
            if (m_omittedImportedNodeIds.Count == 0)
            {
                return;
            }

            var vacatedNodeIds = new HashSet<NodeId>();
            foreach (NodeId nodeId in m_omittedImportedNodeIds)
            {
                bool idSurvives =
                    m_nodeSetImporter?.TryGetNode(
                        nodeId,
                        out NodeState? imported) == true &&
                    !m_omittedImportedNodes.Contains(imported!);
                if (m_authoredNodes.TryGetValue(nodeId, out NodeState? omitted))
                {
                    m_authoredNodes.Remove(nodeId);
                    m_factoryAuthoredNodes.Remove(omitted);
                    m_configuredNodes.Remove(omitted);
                }
                if (!idSurvives)
                {
                    m_historyRead.Remove(nodeId);
                    m_historyUpdate.Remove(nodeId);
                    m_monitoredItemCreated.Remove(nodeId);
                    m_nodeAdded.Remove(nodeId);
                    m_nodeRemoved.Remove(nodeId);
                    if (NodeManager is AsyncCustomNodeManager manager)
                    {
                        manager.MultiConsumerNodeIds.Remove(nodeId);
                    }
                    vacatedNodeIds.Add(nodeId);
                }
            }

            for (int i = m_authoredRoots.Count - 1; i >= 0; i--)
            {
                if (m_omittedImportedNodes.Contains(m_authoredRoots[i]))
                {
                    m_authoredRoots.RemoveAt(i);
                }
            }

            if (vacatedNodeIds.Count > 0)
            {
                var nodes = new List<NodeState>(m_authoredRoots);
                var children = new List<BaseInstanceState>();
                var references = new List<IReference>();
                for (int i = 0; i < nodes.Count; i++)
                {
                    NodeState node = nodes[i];
                    references.Clear();
                    node.GetReferences(Context, references);
                    for (int ii = 0; ii < references.Count; ii++)
                    {
                        IReference reference = references[ii];
                        NodeId targetId = ExpandedNodeId.ToNodeId(
                            reference.TargetId,
                            Context.NamespaceUris);
                        if (!targetId.IsNull &&
                            vacatedNodeIds.Contains(targetId))
                        {
                            node.RemoveReference(
                                reference.ReferenceTypeId,
                                reference.IsInverse,
                                reference.TargetId);
                        }
                    }

                    children.Clear();
                    node.GetChildren(Context, children);
                    nodes.AddRange(children);
                }
            }
        }

        private void ThrowIfConfiguredImportedPlaceholder(
            NodeState node,
            NodeState? replacement)
        {
            bool replacementOwnsSameNodeIdConfiguration =
                replacement is not null &&
                replacement.NodeId == node.NodeId &&
                m_configuredNodes.Contains(replacement) &&
                !m_configuredNodes.Contains(node);
            bool keyedConfiguration =
                m_historyRead.ContainsKey(node.NodeId) ||
                m_historyUpdate.ContainsKey(node.NodeId) ||
                m_monitoredItemCreated.ContainsKey(node.NodeId) ||
                m_nodeAdded.ContainsKey(node.NodeId) ||
                m_nodeRemoved.ContainsKey(node.NodeId) ||
                EventSources?.ContainsSource(node.NodeId) == true;
            if (replacementOwnsSameNodeIdConfiguration)
            {
                keyedConfiguration = false;
            }
            bool configured = m_configuredNodes.Contains(node) ||
                node.HasRuntimeCallbacks() ||
                keyedConfiguration;
            if (node is BaseVariableState variable)
            {
                configured = configured ||
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
            if (node is MethodState method)
            {
                configured = configured ||
                    method.OnReadExecutable is not null ||
                    method.OnWriteExecutable is not null ||
                    method.OnReadUserExecutable is not null ||
                    method.OnWriteUserExecutable is not null ||
                    method.OnCallMethod is not null ||
                    method.OnCallMethod2 is not null ||
                    method.OnCallMethod2Async is not null;
            }
            if (node is BaseObjectState objectState)
            {
                configured = configured ||
                    objectState.OnReadEventNotifier is not null ||
                    objectState.OnWriteEventNotifier is not null;
            }
            if (NodeManager is AsyncCustomNodeManager manager &&
                manager.MultiConsumerNodeIds.ContainsKey(node.NodeId) &&
                !replacementOwnsSameNodeIdConfiguration)
            {
                configured = true;
            }
            if (configured)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Imported node '{0}' replaces configured generated child '{1}'. " +
                    "Import the NodeSet before wiring that child.",
                    node.BrowseName,
                    node.NodeId);
            }
        }

        private void PruneUnreachableAuthoredNodes()
        {
            var reachableNodeIds = new HashSet<NodeId>();
            var nodes = new List<NodeState>(m_authoredRoots);
            var children = new List<BaseInstanceState>();
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeState node = nodes[i];
                if (node.NodeId.IsNull || !reachableNodeIds.Add(node.NodeId))
                {
                    continue;
                }
                children.Clear();
                node.GetChildren(Context, children);
                nodes.AddRange(children);
            }

            var unreachableNodeIds = new List<NodeId>();
            foreach (NodeId nodeId in m_authoredNodes.Keys)
            {
                if (!reachableNodeIds.Contains(nodeId))
                {
                    unreachableNodeIds.Add(nodeId);
                }
            }
            for (int i = 0; i < unreachableNodeIds.Count; i++)
            {
                NodeId nodeId = unreachableNodeIds[i];
                m_authoredNodes.Remove(nodeId);
                m_historyRead.Remove(nodeId);
                m_historyUpdate.Remove(nodeId);
                m_monitoredItemCreated.Remove(nodeId);
                m_nodeAdded.Remove(nodeId);
                m_nodeRemoved.Remove(nodeId);
            }
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

            if (m_importedNodeValidator is not null)
            {
                m_importedNodeValidator(node);
                return;
            }

            if (NodeManager is AsyncCustomNodeManager manager)
            {
                ValidateAuthoredNodeId(manager, node);
            }
        }

        private bool HasAuthoredAncestor(NodeState node)
        {
            for (NodeState? current = node is BaseInstanceState instance
                    ? instance.Parent
                    : null;
                current != null;
                current = current is BaseInstanceState parentInstance
                    ? parentInstance.Parent
                    : null)
            {
                if (!current.NodeId.IsNull &&
                    IsStagedNode(current))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsStagedNode(NodeState node)
        {
            if (m_authoredNodes.TryGetValue(
                node.NodeId,
                out NodeState? authored) &&
                ReferenceEquals(authored, node))
            {
                return true;
            }
            return m_nodeSetImporter?.TryGetNode(
                node.NodeId,
                out NodeState? imported) == true &&
                ReferenceEquals(imported, node);
        }

        private void AddAuthoredRoot(NodeState node)
        {
            for (int i = 0; i < m_authoredRoots.Count; i++)
            {
                if (ReferenceEquals(m_authoredRoots[i], node))
                {
                    return;
                }
            }
            m_authoredRoots.Add(node);
        }

        private void AddChildIfMissing(
            NodeState parent,
            BaseInstanceState child)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(Context, children);
            for (int i = 0; i < children.Count; i++)
            {
                if (ReferenceEquals(children[i], child))
                {
                    return;
                }
            }
            parent.AddChild(child);
        }

        private bool IsOwnedNamespace(ushort namespaceIndex)
        {
            if (NodeManager is not AsyncCustomNodeManager manager)
            {
                return namespaceIndex == m_defaultNamespaceIndex;
            }

            for (int i = 0; i < manager.NamespaceIndexes.Count; i++)
            {
                if (manager.NamespaceIndexes[i] == namespaceIndex)
                {
                    return true;
                }
            }
            return false;
        }

        private static void ValidateAuthoredNodeId(
            AsyncCustomNodeManager manager,
            NodeState node)
        {
            if (node.NodeId.NamespaceIndex == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "Authored node '{0}' cannot use namespace index 0.",
                    node.BrowseName);
            }

            for (int i = 0; i < manager.NamespaceIndexes.Count; i++)
            {
                if (manager.NamespaceIndexes[i] == node.NodeId.NamespaceIndex)
                {
                    return;
                }
            }

            throw ServiceResultException.Create(
                StatusCodes.BadNodeIdInvalid,
                "Authored node '{0}' uses namespace index {1}, which is not owned by this source.",
                node.BrowseName,
                node.NodeId.NamespaceIndex);
        }

        private QualifiedName CreateDefaultBrowseName(string browseName)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameInvalid,
                    "Browse name is null or empty.");
            }
            return new QualifiedName(browseName, m_defaultNamespaceIndex);
        }

        private static QualifiedName ValidateExplicitBrowseName(QualifiedName browseName)
        {
            if (browseName.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameInvalid,
                    "Browse name is null or empty.");
            }
            if (browseName.NamespaceIndex == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameInvalid,
                    "A QualifiedName browse name must specify a nonzero namespace index. " +
                    "Use the string overload for the source's first namespace.");
            }
            return browseName;
        }

        private void ThrowIfSealed()
        {
            if (m_sealed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Cannot wire or add nodes after the builder has been sealed. " +
                    "All calls must occur inside the Configure or BuildAsync delegate.");
            }
        }

        private void ThrowIfGraphFinalizationStarted()
        {
            if (m_authoredNodesRegistered)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Cannot add nodes after graph finalization has begun. " +
                    "All graph authoring calls must occur inside BuildAsync.");
            }
        }

        private void ThrowIfGraphAuthoringUnavailable()
        {
            if (!m_graphAuthoringEnabled)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Node creation is only available while an INodeSource is building its graph.");
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

        private sealed class ExternalParentState : BaseObjectState
        {
            public ExternalParentState(NodeId nodeId)
                : base(parent: null)
            {
                NodeId = nodeId;
            }
        }

        private readonly record struct ReferenceIdentity(
            NodeId ReferenceTypeId,
            bool IsInverse,
            ExpandedNodeId TargetId)
        {
            public static ReferenceIdentity From(IReference reference)
            {
                return new ReferenceIdentity(
                    reference.ReferenceTypeId,
                    reference.IsInverse,
                    reference.TargetId);
            }
        }

        private sealed class NodeStateReferenceComparer :
            IEqualityComparer<NodeState>
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
        private readonly Dictionary<NodeId, NodeState> m_authoredNodes = [];
        private readonly HashSet<NodeState> m_factoryAuthoredNodes =
            new(NodeStateReferenceComparer.Instance);
        private readonly HashSet<NodeState> m_configuredNodes =
            new(NodeStateReferenceComparer.Instance);
        private readonly Dictionary<NodeState, HashSet<ReferenceIdentity>>
            m_importedReferenceSnapshots =
                new(NodeStateReferenceComparer.Instance);
        private readonly List<(
            BaseInstanceState Replaced,
            BaseInstanceState Replacement)> m_importedReplacements = [];
        private readonly HashSet<NodeId> m_omittedImportedNodeIds = [];
        private readonly HashSet<NodeState> m_omittedImportedNodes =
            new(NodeStateReferenceComparer.Instance);
        private readonly List<NodeState> m_authoredRoots = [];
        private INodeSetImportFactoryProvider? m_importFactoryProvider;
        private Action<NodeState>? m_importedNodeValidator;
        private NodeSetImporter? m_nodeSetImporter;
        private bool m_sealed;
        private bool m_graphAuthoringEnabled;
        private bool m_authoredNodesRegistered;
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
