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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Di.Server
{
    /// <summary>
    /// OPC UA node manager that exposes the Device Integration (DI) model
    /// (OPC 10000-100) predefined nodes plus the fluent
    /// <see cref="IDeviceBuilder{TDevice}"/> surface for materialising new
    /// devices at runtime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The static model nodes — all type definitions, reference types, and
    /// well-known instances defined by the DI companion specification — are
    /// loaded from the source-generated <c>AddOpcUaDi</c> extension.
    /// Subclasses can override
    /// <see cref="AsyncCustomNodeManager.AddBehaviourToPredefinedNodeAsync"/>
    /// to attach live behaviour to specific predefined nodes.
    /// </para>
    /// <para>
    /// Programmatic device creation goes through
    /// <see cref="CreateDeviceAsync(QualifiedName, NodeState?, CancellationToken)"/>
    /// (and its generic overload). The returned
    /// <see cref="IDeviceBuilder{TDevice}"/> writes nameplate properties,
    /// adds functional groups, and stamps topology references; the manager
    /// itself owns the registration through <c>AddPredefinedNodeAsync</c>
    /// so the new device participates fully in browse, read, subscribe,
    /// and event delivery.
    /// </para>
    /// <para>
    /// <c>DiNodeManager</c> inherits from
    /// <see cref="FluentNodeManagerBase"/> (rather than directly from
    /// <see cref="AsyncCustomNodeManager"/>) so DI users gain the
    /// fluent event-source and simulation registries out of the box.
    /// Subclasses overriding
    /// <see cref="AsyncCustomNodeManager.Dispose(bool)"/> or
    /// <see cref="FluentNodeManagerBase.OnSubscribeToEventsAsync"/>
    /// must still call <c>base</c>.
    /// </para>
    /// </remarks>
    public class DiNodeManager : FluentNodeManagerBase, INodeIdFactory
    {
        /// <summary>
        /// The DI namespace URI (<c>http://opcfoundation.org/UA/DI/</c>).
        /// </summary>
        public const string DiNamespaceUri = Opc.Ua.Di.Namespaces.OpcUaDi;

        private NodeManagerBuilder? m_builder;

        /// <summary>
        /// Initialises a new <see cref="DiNodeManager"/> without DI-
        /// hosting integration. Use this constructor for manual
        /// (non-<c>AddOpcUaDi</c>) wiring.
        /// </summary>
        public DiNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : this(server, configuration, postSetupRunner: null)
        {
        }

        /// <summary>
        /// Initialises a new <see cref="DiNodeManager"/> with an
        /// optional post-setup runner. The runner is invoked
        /// automatically at the end of
        /// <see cref="CreateAddressSpaceAsync"/> (after
        /// <see cref="OnAddressSpaceReadyAsync"/> returns), for both
        /// the base <see cref="DiNodeManager"/> and every subclass.
        /// </summary>
        public DiNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            Opc.Ua.Di.Server.Hosting.IDiPostSetupRunner? postSetupRunner)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<DiNodeManager>(),
                  DiNamespaceUri)
        {
            SystemContext.NodeIdFactory = this;
            PostSetupRunner = postSetupRunner;
        }

        /// <summary>
        /// Initialises a new <see cref="DiNodeManager"/> that registers
        /// additional namespaces in addition to DI. Used by subclasses
        /// (e.g. machinery/pump managers) that own the same namespace
        /// set as the DI manager.
        /// </summary>
        /// <param name="server">The hosting server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="additionalNamespaceUris">
        /// Additional namespace URIs to register before DI. The DI
        /// namespace is always appended last.
        /// </param>
        protected DiNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            params string[] additionalNamespaceUris)
            : this(server, configuration, postSetupRunner: null, additionalNamespaceUris)
        {
        }

        /// <summary>
        /// Initialises a new <see cref="DiNodeManager"/> that registers
        /// additional namespaces in addition to DI and accepts an
        /// optional post-setup runner. Subclasses pass the runner
        /// injected through their factory.
        /// </summary>
        protected DiNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            Opc.Ua.Di.Server.Hosting.IDiPostSetupRunner? postSetupRunner,
            params string[] additionalNamespaceUris)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<DiNodeManager>(),
                  CombineNamespaces(additionalNamespaceUris))
        {
            SystemContext.NodeIdFactory = this;
            PostSetupRunner = postSetupRunner;
        }

        /// <summary>
        /// Optional post-setup runner injected through the
        /// Device Integration (DI) hosting pipeline. The base class
        /// auto-invokes it from <see cref="CreateAddressSpaceAsync"/>
        /// after <see cref="OnAddressSpaceReadyAsync"/> returns, so
        /// subclasses do not need to (and should not) invoke it
        /// manually. Read-only on subclasses; exposed mostly for
        /// diagnostic / test inspection.
        /// </summary>
        protected Opc.Ua.Di.Server.Hosting.IDiPostSetupRunner? PostSetupRunner { get; }

        private static string[] CombineNamespaces(string[] additional)
        {
            if (additional == null || additional.Length == 0)
            {
                return new[] { DiNamespaceUri };
            }

            var combined = new string[additional.Length + 1];
            additional.CopyTo(combined, 0);
            combined[^1] = DiNamespaceUri;
            return combined;
        }

        /// <summary>
        /// The namespace index of the DI model.
        /// </summary>
        public ushort DiNamespaceIndex =>
            (ushort)Server.NamespaceUris.GetIndex(DiNamespaceUri);

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState instance &&
                instance.Parent != null)
            {
                string parentId = instance.Parent.NodeId.IdentifierAsString;

                return new NodeId(
                    $"{parentId}_{instance.SymbolicName}",
                    instance.Parent.NodeId.NamespaceIndex);
            }

            return node.NodeId;
        }

        /// <summary>
        /// Adds an instance node under the Machinery <c>Machines</c> folder
        /// if it is present in the address space. Returns <see langword="true"/>
        /// when the folder was found and the reference was added.
        /// </summary>
        protected bool TryAddToMachinesFolder(BaseInstanceState instance)
        {
            // The well-known Machines folder BrowseName defined by the
            // Machinery companion specification (OPC 40001).
            const string machinesFolderBrowseName = "Machines";

            foreach (NodeState root in PredefinedNodes.Values)
            {
                if (root.BrowseName.Name == machinesFolderBrowseName &&
                    root is BaseObjectState folder)
                {
                    folder.AddChild(instance);
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<NodeStateCollection>(
                new NodeStateCollection().AddOpcUaDi(context));
        }

        /// <summary>
        /// Constructs the address space and runs the
        /// <c>IDiPostSetupRunner</c>-based hosting hooks. The flow is:
        /// <list type="number">
        ///   <item><description>
        ///     The framework's <c>base.CreateAddressSpaceAsync</c> loads
        ///     predefined nodes and wires the type tree.
        ///   </description></item>
        ///   <item><description>
        ///     <see cref="OnAddressSpaceReadyAsync"/> runs (subclasses
        ///     override this to materialise additional instances and
        ///     drive the fluent <c>INodeManagerBuilder</c>).
        ///   </description></item>
        ///   <item><description>
        ///     The DI hosting <see cref="PostSetupRunner"/>, if any, is
        ///     invoked with <c>this</c>; configurators registered via
        ///     <c>ConfigureDevicesFor&lt;TNodeManager&gt;</c> see the
        ///     fully wired manager.
        ///   </description></item>
        /// </list>
        /// Subclasses should override <see cref="OnAddressSpaceReadyAsync"/>
        /// rather than <c>CreateAddressSpaceAsync</c> so the post-setup
        /// runner fires automatically.
        /// </summary>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(
                externalReferences, cancellationToken).ConfigureAwait(false);

            await OnAddressSpaceReadyAsync(cancellationToken).ConfigureAwait(false);

            if (PostSetupRunner != null)
            {
                await PostSetupRunner.RunAsync(this, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Extension point invoked after
        /// <see cref="AsyncCustomNodeManager.CreateAddressSpaceAsync"/>
        /// has populated <c>PredefinedNodes</c> but before the
        /// <see cref="PostSetupRunner"/> fires. Subclasses materialise
        /// additional instances (e.g. companion-spec device factories)
        /// and drive the fluent <c>INodeManagerBuilder</c> from here.
        /// </summary>
        /// <remarks>
        /// The default implementation is a no-op. Implementers do not
        /// need to invoke <c>base.OnAddressSpaceReadyAsync</c>.
        /// </remarks>
        protected virtual ValueTask OnAddressSpaceReadyAsync(
            CancellationToken cancellationToken)
        {
            return default;
        }

        /// <summary>
        /// Returns the well-known parent under which
        /// <see cref="CreateDeviceAsync(QualifiedName, NodeState?, CancellationToken)"/>
        /// attaches newly created devices when no explicit parent is
        /// supplied. The default implementation returns the DI
        /// <c>DeviceSet</c> object; subclasses such as machinery-aware
        /// managers may override this to return the Machinery
        /// <c>Machines</c> folder (or any other container).
        /// </summary>
        /// <returns>
        /// The default parent <see cref="NodeState"/>, or
        /// <see langword="null"/> if no DI <c>DeviceSet</c> instance is
        /// present in the address space.
        /// </returns>
        protected virtual NodeState? ResolveDefaultDeviceParent()
        {
            NodeId deviceSetId = NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                DiNamespaceUri,
                Server.NamespaceUris);

            return PredefinedNodes.TryGetValue(deviceSetId, out NodeState? parent)
                ? parent
                : null;
        }

        /// <summary>
        /// Creates a new <see cref="DeviceState"/> instance under the
        /// resolved <paramref name="parent"/> (or the manager's default
        /// device parent when <paramref name="parent"/> is
        /// <see langword="null"/>) and registers it with the manager.
        /// </summary>
        /// <param name="browseName">Browse name of the new device.</param>
        /// <param name="parent">
        /// Optional explicit parent; when <see langword="null"/>, the
        /// manager's <see cref="ResolveDefaultDeviceParent"/> is used.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A fluent builder for further configuration.</returns>
        public ValueTask<IDeviceBuilder<DeviceState>> CreateDeviceAsync(
            QualifiedName browseName,
            NodeState? parent = null,
            CancellationToken cancellationToken = default)
        {
            return CreateDeviceAsync(
                browseName,
                NodeId.Create(
                    Opc.Ua.Di.ObjectTypes.DeviceType,
                    DiNamespaceUri,
                    Server.NamespaceUris),
                static p => new DeviceState(p),
                parent,
                cancellationToken);
        }

        /// <summary>
        /// Creates a new device of type <typeparamref name="TDevice"/>
        /// under the resolved parent and registers it with the manager.
        /// </summary>
        /// <typeparam name="TDevice">Concrete device state class.</typeparam>
        /// <param name="browseName">Browse name of the new device.</param>
        /// <param name="typeDefinitionId">
        /// Spec-defined <c>TypeDefinitionId</c> stamped onto the new
        /// instance. Required so clients see the correct object type
        /// (e.g. <c>PumpType</c>) rather than a generic
        /// <c>DeviceType</c>.
        /// </param>
        /// <param name="factory">
        /// Factory that constructs the state instance. Typically a
        /// generator-emitted <c>new PumpTypeState(parent)</c>-style
        /// lambda.
        /// </param>
        /// <param name="parent">
        /// Optional explicit parent; when <see langword="null"/>, the
        /// manager's <see cref="ResolveDefaultDeviceParent"/> is used.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<IDeviceBuilder<TDevice>> CreateDeviceAsync<TDevice>(
            QualifiedName browseName,
            NodeId typeDefinitionId,
            System.Func<NodeState, TDevice> factory,
            NodeState? parent = null,
            CancellationToken cancellationToken = default)
            where TDevice : ComponentState
        {
            if (browseName.IsNull)
            {
                throw new System.ArgumentNullException(nameof(browseName));
            }
            if (typeDefinitionId.IsNull)
            {
                throw new System.ArgumentNullException(nameof(typeDefinitionId));
            }
            if (factory == null)
            {
                throw new System.ArgumentNullException(nameof(factory));
            }
            NodeState effectiveParent = parent ?? ResolveDefaultDeviceParent()
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "No default device parent could be resolved. Override DiNodeManager.ResolveDefaultDeviceParent or supply an explicit parent.");

            // Fail fast on duplicate browse names under the parent.
            NodeState? existing = effectiveParent.FindChild(SystemContext, browseName);
            if (existing != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "Parent '{0}' already has a child '{1}'.",
                    effectiveParent.BrowseName,
                    browseName);
            }

            TDevice device = factory(effectiveParent)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Factory returned null for device '{0}'.",
                    browseName);

            device.SymbolicName = browseName.Name ?? string.Empty;
            device.BrowseName = browseName;
            device.DisplayName = new LocalizedText(browseName.Name);
            device.TypeDefinitionId = typeDefinitionId;
            device.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            device.NodeId = SystemContext.NodeIdFactory.New(SystemContext, device);

            effectiveParent.AddChild(device);

            await AddPredefinedNodeAsync(SystemContext, device, cancellationToken)
                .ConfigureAwait(false);

            return new DeviceBuilder<TDevice>(this, device, GetOrCreateBuilder());
        }

        /// <summary>
        /// Wraps an existing device (e.g. one loaded from a NodeSet2
        /// XML) with the fluent
        /// <see cref="IDeviceBuilder{TDevice}"/> surface so that the
        /// same configuration code can run against loaded and
        /// programmatically created devices alike.
        /// </summary>
        /// <typeparam name="TDevice">Concrete device state type.</typeparam>
        public IDeviceBuilder<TDevice> Device<TDevice>(TDevice device)
            where TDevice : ComponentState
        {
            if (device == null)
            {
                throw new System.ArgumentNullException(nameof(device));
            }
            return new DeviceBuilder<TDevice>(this, device, GetOrCreateBuilder());
        }

        /// <summary>
        /// Returns a fluent builder for the device with the supplied
        /// <paramref name="nodeId"/>. Throws if the node is not present
        /// in the manager's predefined nodes or is not assignable to
        /// <typeparamref name="TDevice"/>.
        /// </summary>
        /// <typeparam name="TDevice">Concrete device state type.</typeparam>
        public IDeviceBuilder<TDevice> Device<TDevice>(NodeId nodeId)
            where TDevice : ComponentState
        {
            if (nodeId.IsNull)
            {
                throw new System.ArgumentNullException(nameof(nodeId));
            }
            if (!PredefinedNodes.TryGetValue(nodeId, out NodeState? state))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "No predefined node with NodeId '{0}'.",
                    nodeId);
            }

            if (state is not TDevice typed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Node '{0}' is of type {1}, which is not assignable to {2}.",
                    nodeId,
                    state.GetType().Name,
                    typeof(TDevice).Name);
            }

            return new DeviceBuilder<TDevice>(this, typed, GetOrCreateBuilder());
        }

        /// <summary>
        /// Resolves an existing device by browse name under a parent
        /// (defaulting to <see cref="ResolveDefaultDeviceParent"/>) and
        /// returns a fluent builder over it.
        /// </summary>
        /// <typeparam name="TDevice">Concrete device state type.</typeparam>
        public IDeviceBuilder<TDevice> DeviceByBrowseName<TDevice>(
            QualifiedName browseName,
            NodeState? parent = null)
            where TDevice : ComponentState
        {
            if (browseName.IsNull)
            {
                throw new System.ArgumentNullException(nameof(browseName));
            }
            NodeState effectiveParent = parent ?? ResolveDefaultDeviceParent()
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "No default device parent could be resolved.");

            NodeState? child = effectiveParent.FindChild(SystemContext, browseName);
            if (child is not TDevice typed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "No child '{0}' of type {1} on '{2}'.",
                    browseName,
                    typeof(TDevice).Name,
                    effectiveParent.BrowseName);
            }

            return new DeviceBuilder<TDevice>(this, typed, GetOrCreateBuilder());
        }

        /// <summary>
        /// Public lookup over the manager's predefined-node dictionary.
        /// Returns <see langword="null"/> when no node with
        /// <paramref name="nodeId"/> is registered. Subclasses already
        /// have direct access via <c>PredefinedNodes</c>; this method
        /// exists so co-resident helpers (topology accessors,
        /// software-update adapters) can resolve well-known DI nodes
        /// without making the dictionary public.
        /// </summary>
        public NodeState? FindPredefinedNode(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                return null;
            }
            return PredefinedNodes.TryGetValue(nodeId, out NodeState? node) ? node : null;
        }

        /// <summary>
        /// Returns (and lazily creates) the fluent
        /// <see cref="NodeManagerBuilder"/> used internally by the
        /// device-builder pipeline to construct
        /// <see cref="INodeBuilder"/> views.
        /// </summary>
        internal NodeManagerBuilder GetOrCreateBuilder()
        {
            if (m_builder != null)
            {
                return m_builder;
            }
            m_builder = new NodeManagerBuilder(
                SystemContext,
                this,
                DiNamespaceIndex,
                browseName =>
                {
                    foreach (NodeState root in PredefinedNodes.Values)
                    {
                        if (root.BrowseName == browseName)
                        {
                            return root;
                        }
                    }
                    return null!;
                },
                nodeId => PredefinedNodes.TryGetValue(nodeId, out NodeState? node)
                    ? node
                    : null!,
                typeId =>
                {
                    var results = new List<NodeState>();
                    foreach (NodeState node in PredefinedNodes.Values)
                    {
                        if (node is BaseInstanceState instance &&
                            instance.TypeDefinitionId == typeId)
                        {
                            results.Add(node);
                        }
                    }
                    return results;
                });

            AttachToBuilder(m_builder);
            return m_builder;
        }
    }
}

