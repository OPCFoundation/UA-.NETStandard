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
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using ConformanceUnitNames = Opc.Ua.Di.Server.ConformanceUnits;
using ServerProfileUris = Opc.Ua.Di.Server.ServerProfiles;

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
    public class DiNodeManager : FluentNodeManagerBase, INodeIdFactory, IConformanceContributor
    {
        /// <summary>
        /// The DI namespace URI (<c>http://opcfoundation.org/UA/DI/</c>).
        /// </summary>
        public const string DiNamespaceUri = Opc.Ua.Di.Namespaces.OpcUaDi;

        private NodeManagerBuilder? m_builder;
        private readonly string m_instanceNamespaceUri;
        private readonly HashSet<SoftwareLoadingMode> m_softwareUpdateLoadingModes = [];

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
            Hosting.IDiPostSetupRunner? postSetupRunner)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<DiNodeManager>(),
                  CombineNamespaces(configuration))
        {
            m_instanceNamespaceUri = GetInstanceNamespaceUri(configuration);
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
            Hosting.IDiPostSetupRunner? postSetupRunner,
            params string[] additionalNamespaceUris)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<DiNodeManager>(),
                  CombineNamespaces(configuration, additionalNamespaceUris))
        {
            m_instanceNamespaceUri = GetInstanceNamespaceUri(configuration);
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
        protected Hosting.IDiPostSetupRunner? PostSetupRunner { get; }

        private static string GetInstanceNamespaceUri(
            ApplicationConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (string.IsNullOrEmpty(configuration.ApplicationUri))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The DI node manager requires a non-empty application URI to mint server-owned instance NodeIds.");
            }
            return configuration.ApplicationUri;
        }

        private static string[] CombineNamespaces(
            ApplicationConfiguration configuration,
            string[]? additional = null)
        {
            string instanceNamespaceUri = GetInstanceNamespaceUri(configuration);
            var combined = new List<string>();
            if (additional != null)
            {
                foreach (string uri in additional)
                {
                    if (!string.IsNullOrEmpty(uri) &&
                        !combined.Contains(uri))
                    {
                        combined.Add(uri);
                    }
                }
            }
            if (!combined.Contains(instanceNamespaceUri))
            {
                combined.Add(instanceNamespaceUri);
            }
            if (!combined.Contains(DiNamespaceUri))
            {
                combined.Add(DiNamespaceUri);
            }

            return [.. combined];
        }

        /// <summary>
        /// The namespace index of the DI model.
        /// </summary>
        public ushort DiNamespaceIndex =>
            (ushort)Server.NamespaceUris.GetIndex(DiNamespaceUri);

        /// <summary>
        /// The server-owned namespace index used for runtime-created
        /// device and topology instance NodeIds.
        /// </summary>
        public ushort InstanceNamespaceIndex =>
            (ushort)Server.NamespaceUris.GetIndex(m_instanceNamespaceUri);

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits => BuildConformanceUnits();

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles => BuildServerProfiles();

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState instance &&
                instance.Parent != null)
            {
                string parentId = instance.Parent.NodeId.IdentifierAsString;

                return new NodeId(
                    $"{parentId}_{instance.SymbolicName}",
                    InstanceNamespaceIndex);
            }

            return node.NodeId;
        }

        /// <summary>
        /// References an instance node from the Machinery <c>Machines</c> folder
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
                    folder.AddReference(
                        Opc.Ua.Types.ReferenceTypeIds.Organizes,
                        false,
                        instance.NodeId);
                    instance.AddReference(
                        Opc.Ua.Types.ReferenceTypeIds.Organizes,
                        true,
                        folder.NodeId);
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
            var deviceSetId = NodeId.Create(
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
            if (browseName.IsNull)
            {
                throw new System.ArgumentNullException(nameof(browseName));
            }
            NodeState effectiveParent = ResolveAndValidateParent(browseName, parent);

            // Materialise through the source-generated DeviceType factory so
            // the instance carries the type's mandatory children (the eight
            // DI nameplate variables) with correct DI-namespace BrowseNames
            // and the type's HasInterface references — a bare
            // new DeviceState(parent) would omit all of these and fail the
            // DI companion-spec compliance rules (GEN-01, DI-01, DI-05).
            DeviceState device = SystemContext
                .CreateInstanceOfDeviceType(effectiveParent, browseName);

            return RegisterDeviceAsync(device, browseName, effectiveParent, cancellationToken);
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
        /// <exception cref="System.ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public ValueTask<IDeviceBuilder<TDevice>> CreateDeviceAsync<TDevice>(
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
            NodeState effectiveParent = ResolveAndValidateParent(browseName, parent);

            TDevice device = factory(effectiveParent)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Factory returned null for device '{0}'.",
                    browseName);

            device.TypeDefinitionId = typeDefinitionId;

            return RegisterDeviceAsync(device, browseName, effectiveParent, cancellationToken);
        }

        /// <summary>
        /// Resolves the effective device parent (explicit or default) and
        /// fails fast when the browse name is already present under it.
        /// </summary>
        private NodeState ResolveAndValidateParent(
            QualifiedName browseName,
            NodeState? parent)
        {
            NodeState effectiveParent = parent ??
                ResolveDefaultDeviceParent()
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "No default device parent could be resolved. Override DiNodeManager.ResolveDefaultDeviceParent or supply an explicit parent.");

            NodeState? existing = effectiveParent.FindChild(SystemContext, browseName);
            if (existing != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "Parent '{0}' already has a child '{1}'.",
                    effectiveParent.BrowseName,
                    browseName);
            }
            return effectiveParent;
        }

        /// <summary>
        /// Normalises the device's identity, attaches it to the parent,
        /// assigns per-instance NodeIds to the whole subtree, and registers
        /// it with the manager.
        /// </summary>
        /// <typeparam name="TDevice">Concrete device state type.</typeparam>
        private async ValueTask<IDeviceBuilder<TDevice>> RegisterDeviceAsync<TDevice>(
            TDevice device,
            QualifiedName browseName,
            NodeState effectiveParent,
            CancellationToken cancellationToken)
            where TDevice : ComponentState
        {
            device.SymbolicName = browseName.Name ?? string.Empty;
            device.BrowseName = browseName;
            device.DisplayName = new LocalizedText(browseName.Name);
            NodeId deviceSetId = NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                DiNamespaceUri,
                Server.NamespaceUris);
            device.ReferenceTypeId = effectiveParent.NodeId == deviceSetId
                ? Opc.Ua.Types.ReferenceTypeIds.Organizes
                : Opc.Ua.Types.ReferenceTypeIds.HasComponent;
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
        /// <exception cref="System.ArgumentNullException"><paramref name="device"/> is <c>null</c>.</exception>
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
        /// <exception cref="System.ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
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
        /// <exception cref="System.ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public IDeviceBuilder<TDevice> DeviceByBrowseName<TDevice>(
            QualifiedName browseName,
            NodeState? parent = null)
            where TDevice : ComponentState
        {
            if (browseName.IsNull)
            {
                throw new System.ArgumentNullException(nameof(browseName));
            }
            NodeState effectiveParent = parent ??
                ResolveDefaultDeviceParent()
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "No default device parent could be resolved.");

            NodeState? child = effectiveParent.FindChild(SystemContext, browseName);
            if (child == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "No child '{0}' exists on '{1}'.",
                    browseName,
                    effectiveParent.BrowseName);
            }
            if (child is not TDevice typed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Child '{0}' on '{1}' is of type {2}, which is not assignable to {3}.",
                    browseName,
                    effectiveParent.BrowseName,
                    child.GetType().Name,
                    typeof(TDevice).Name);
            }

            return new DeviceBuilder<TDevice>(this, typed, GetOrCreateBuilder());
        }

        /// <summary>
        /// Wraps an existing topology element with the common fluent
        /// topology-element surface.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        /// <param name="element">The topology element to wrap.</param>
        /// <returns>A fluent topology-element builder.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="element"/> is <see langword="null"/>.
        /// </exception>
        public ITopologyElementBuilder<TElement> TopologyElement<TElement>(
            TElement element)
            where TElement : TopologyElementState
        {
            if (element == null)
            {
                throw new System.ArgumentNullException(nameof(element));
            }
            return new TopologyElementBuilder<TElement>(
                this,
                element,
                GetOrCreateBuilder());
        }

        /// <summary>
        /// Resolves a predefined topology element by NodeId.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        /// <param name="nodeId">NodeId of the registered topology element.</param>
        /// <returns>A fluent topology-element builder.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public ITopologyElementBuilder<TElement> TopologyElement<TElement>(
            NodeId nodeId)
            where TElement : TopologyElementState
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
            if (state is not TElement typed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Node '{0}' is of type {1}, which is not assignable to {2}.",
                    nodeId,
                    state.GetType().Name,
                    typeof(TElement).Name);
            }

            return new TopologyElementBuilder<TElement>(
                this,
                typed,
                GetOrCreateBuilder());
        }

        /// <summary>
        /// Resolves an existing topology element by browse name.
        /// </summary>
        /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
        /// <param name="browseName">Browse name below the resolved parent.</param>
        /// <param name="parent">
        /// Optional parent; the default device parent is used when omitted.
        /// </param>
        /// <returns>A fluent topology-element builder.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public ITopologyElementBuilder<TElement> TopologyElementByBrowseName<TElement>(
            QualifiedName browseName,
            NodeState? parent = null)
            where TElement : TopologyElementState
        {
            if (browseName.IsNull)
            {
                throw new System.ArgumentNullException(nameof(browseName));
            }
            NodeState effectiveParent = parent ??
                ResolveDefaultDeviceParent()
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "No default device parent could be resolved.");

            NodeState? child = effectiveParent.FindChild(SystemContext, browseName);
            if (child == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "No child '{0}' exists on '{1}'.",
                    browseName,
                    effectiveParent.BrowseName);
            }
            if (child is not TElement typed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Child '{0}' on '{1}' is of type {2}, which is not assignable to {3}.",
                    browseName,
                    effectiveParent.BrowseName,
                    child.GetType().Name,
                    typeof(TElement).Name);
            }

            return new TopologyElementBuilder<TElement>(
                this,
                typed,
                GetOrCreateBuilder());
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
        /// Synchronously indexes a plain builder-created node subtree.
        /// </summary>
        /// <remarks>
        /// This path intentionally skips asynchronous behavior attachment and
        /// is reserved for plain states configured directly by fluent builders.
        /// </remarks>
        /// <param name="node">The node subtree to index.</param>
        internal void AddPlainPredefinedNodeSynchronously(NodeState node)
        {
            AddPredefinedNodeSynchronously(node);
        }

        internal void RecordSoftwareUpdateFacet(SoftwareLoadingMode mode)
        {
            m_softwareUpdateLoadingModes.Add(mode);
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

        private ArrayOf<QualifiedName> BuildConformanceUnits()
        {
            var units = new List<QualifiedName>();
            if (HasDeviceSet())
            {
                units.Add(new QualifiedName(ConformanceUnitNames.DeviceTopology));
                units.Add(new QualifiedName(ConformanceUnitNames.Offline));
            }
            if (HasWiredLockingService())
            {
                units.Add(new QualifiedName(ConformanceUnitNames.Locking));
                if (HasWiredBreakLockService())
                {
                    units.Add(new QualifiedName(ConformanceUnitNames.BreakLocking));
                }
            }
            if (m_softwareUpdateLoadingModes.Count > 0)
            {
                units.Add(new QualifiedName(ConformanceUnitNames.SoftwareUpdate));
                units.Add(new QualifiedName(ConformanceUnitNames.SoftwareUpdatePrepareForUpdate));
                units.Add(new QualifiedName(ConformanceUnitNames.SoftwareUpdateResumeUpdate));
            }
            if (m_softwareUpdateLoadingModes.Contains(SoftwareLoadingMode.Package))
            {
                units.Add(new QualifiedName(ConformanceUnitNames.SoftwareUpdateFileSystemLoading));
                units.Add(new QualifiedName(
                    ConformanceUnitNames.SoftwareUpdateInstallationForFileSystem));
            }
            if (m_softwareUpdateLoadingModes.Contains(SoftwareLoadingMode.Direct))
            {
                units.Add(new QualifiedName(ConformanceUnitNames.SoftwareUpdateDirectLoading));
                units.Add(new QualifiedName(ConformanceUnitNames.SoftwareUpdateUpdateStatus));
            }
            if (m_softwareUpdateLoadingModes.Contains(SoftwareLoadingMode.Cached))
            {
                units.Add(new QualifiedName(ConformanceUnitNames.SoftwareUpdateCachedLoading));
                units.Add(new QualifiedName(
                    ConformanceUnitNames.SoftwareUpdateInstallationForCachedLoading));
                units.Add(new QualifiedName(ConformanceUnitNames.SoftwareUpdateUpdateStatus));
            }
            return units.ToArrayOf();
        }

        private ArrayOf<string> BuildServerProfiles()
        {
            var profiles = new List<string>();
            if (HasDeviceSet())
            {
                profiles.Add(ServerProfileUris.DeviceIntegrationHost);
            }
            if (HasWiredLockingService())
            {
                profiles.Add(ServerProfileUris.Locking);
            }
            if (m_softwareUpdateLoadingModes.Count > 0)
            {
                profiles.Add(ServerProfileUris.SoftwareUpdateBase);
            }
            if (m_softwareUpdateLoadingModes.Contains(SoftwareLoadingMode.Package))
            {
                profiles.Add(ServerProfileUris.FileSystemLoading);
            }
            if (m_softwareUpdateLoadingModes.Contains(SoftwareLoadingMode.Direct))
            {
                profiles.Add(ServerProfileUris.DirectLoading);
            }
            if (m_softwareUpdateLoadingModes.Contains(SoftwareLoadingMode.Cached))
            {
                profiles.Add(ServerProfileUris.CachedLoading);
            }
            return profiles.ToArrayOf();
        }

        private bool HasDeviceSet()
        {
            NodeId deviceSetId = NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                DiNamespaceUri,
                Server.NamespaceUris);

            return PredefinedNodes.ContainsKey(deviceSetId);
        }

        private bool HasWiredLockingService()
        {
            foreach (NodeState node in PredefinedNodes.Values)
            {
                if (node is LockingServicesState lockState &&
                    (lockState.InitLock?.OnCall != null ||
                        lockState.RenewLock?.OnCall != null ||
                        lockState.ExitLock?.OnCall != null))
                {
                    return true;
                }
            }
            return false;
        }

        private bool HasWiredBreakLockService()
        {
            foreach (NodeState node in PredefinedNodes.Values)
            {
                if (node is LockingServicesState lockState &&
                    lockState.BreakLock?.OnCall != null)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
