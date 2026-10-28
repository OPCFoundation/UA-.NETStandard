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
using Microsoft.Extensions.Logging;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.NodeManager;
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
    public class DiNodeManager : FluentNodeManagerBase, INodeIdFactory, IConformanceContributor,
        INodeManagerReadinessNotification
    {
        /// <summary>
        /// The DI namespace URI (<c>http://opcfoundation.org/UA/DI/</c>).
        /// </summary>
        public const string DiNamespaceUri = Opc.Ua.Di.Namespaces.OpcUaDi;

        private NodeManagerBuilder? m_builder;
        private readonly string m_instanceNamespaceUri;

        /// <summary>Additional providers in model loading order.</summary>
        private readonly ArrayOf<IDiModelProvider> m_providers;

        /// <summary>Protects resource registration and batch removal.</summary>
        private readonly System.Threading.Lock m_resourceGate = new();

        /// <summary>Resources retained until the next explicit drain or manager disposal.</summary>
        private readonly List<IAsyncDisposable> m_resources = [];

        /// <summary>The latest resource drain, joined by overlapping drains and shutdown.</summary>
        private Task m_resourceDrain = Task.CompletedTask;

        /// <summary>Whether final shutdown has closed resource registration.</summary>
        private bool m_resourceRegistrationClosed;

        /// <summary>Distinguishes runtime registration from initial model loading.</summary>
        private bool m_modelsLoaded;
        private readonly HashSet<SoftwareLoadingMode> m_softwareUpdateLoadingModes = [];

        /// <summary>
        /// The DI 1.05 server facets with the conformance units the OPC
        /// Foundation profile database marks mandatory for them, those of
        /// the included Software Update Base facet folded in.
        /// </summary>
        private static readonly ArrayOf<DiFacet> s_facets =
        [
            new(ServerProfileUris.DeviceIntegrationHost,
                [ConformanceUnitNames.DeviceTopology, ConformanceUnitNames.Offline]),
            new(ServerProfileUris.Locking, [ConformanceUnitNames.Locking]),
            new(ServerProfileUris.SoftwareUpdateBase, [ConformanceUnitNames.SoftwareUpdate]),
            new(ServerProfileUris.FileSystemLoading,
                [
                    ConformanceUnitNames.SoftwareUpdate,
                    ConformanceUnitNames.SoftwareUpdateFileSystemLoading,
                    ConformanceUnitNames.SoftwareUpdateInstallationForFileSystem
                ]),
            new(ServerProfileUris.DirectLoading,
                [
                    ConformanceUnitNames.SoftwareUpdate,
                    ConformanceUnitNames.SoftwareUpdateDirectLoading,
                    ConformanceUnitNames.SoftwareUpdateUpdateStatus
                ]),
            new(ServerProfileUris.CachedLoading,
                [
                    ConformanceUnitNames.SoftwareUpdate,
                    ConformanceUnitNames.SoftwareUpdateCachedLoading,
                    ConformanceUnitNames.SoftwareUpdateInstallationForCachedLoading,
                    ConformanceUnitNames.SoftwareUpdateUpdateStatus
                ])
        ];

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
        /// automatically during <see cref="INodeManagerReadinessNotification.OnServerReadyAsync"/>, after
        /// publication and before the builder is sealed, for both the base
        /// <see cref="DiNodeManager"/> and every subclass.
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

            // devices live in the server's own instance namespace. Their
            // parents - DeviceSet and the type declarations below it - come
            // from the DI model, and a subclass registers its own model
            // namespaces ahead of the instance one, so neither the first
            // namespace nor the parent's is the right place to mint into.
            NodeIdFactory = NodeIdFactory.WithDefaultNamespaceIndex(InstanceNamespaceIndex);
            PostSetupRunner = postSetupRunner;
        }

        /// <summary>
        /// Initialises a new <see cref="DiNodeManager"/> that loads the
        /// supplied <see cref="IDiModelProvider"/> models after DI. The
        /// namespaces of the providers are registered with the server.
        /// </summary>
        /// <param name="server">The hosting server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="postSetupRunner">The optional post-setup runner.</param>
        /// <param name="providers">
        /// The model providers, normally resolved from the container by
        /// <see cref="DiNodeManagerFactory"/>.
        /// </param>
        public DiNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            Hosting.IDiPostSetupRunner? postSetupRunner,
            ArrayOf<IDiModelProvider> providers)
            : this(server, configuration, postSetupRunner, providers, ArrayOf<string>.Empty)
        {
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
            : this(server, configuration, postSetupRunner, default, (ArrayOf<string>)additionalNamespaceUris)
        {
        }

        /// <summary>
        /// Creates a companion owner with its own namespaces and additional DI model providers.
        /// Providers are loaded after the owner's predefined model collection.
        /// </summary>
        protected DiNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            Hosting.IDiPostSetupRunner? postSetupRunner,
            ArrayOf<IDiModelProvider> providers,
            ArrayOf<string> additionalNamespaceUris)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<DiNodeManager>(),
                  CombineNamespaces(
                      configuration, DiModelProviderUtilities.GetNamespaceUris(providers, additionalNamespaceUris)))
        {
            m_instanceNamespaceUri = GetInstanceNamespaceUri(configuration);
            m_providers = DiModelProviderUtilities.Normalize(providers);
            NodeIdFactory = NodeIdFactory.WithDefaultNamespaceIndex(InstanceNamespaceIndex);
            PostSetupRunner = postSetupRunner;
        }

        /// <summary>
        /// Optional post-setup runner injected through the
        /// Device Integration (DI) hosting pipeline. The base class
        /// auto-invokes it from <see cref="INodeManagerReadinessNotification.OnServerReadyAsync"/>
        /// after publication, so subclasses do not need to (and should not) invoke it
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
            IEnumerable<string>? additional = null)
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

        /// <summary>
        /// The namespace index companion runtimes composed into this manager create their
        /// instances in. It is <see cref="InstanceNamespaceIndex"/> unless a derived owner, such
        /// as the Machinery manager, defines an application-owned instance namespace.
        /// </summary>
        public virtual ushort CompanionInstanceNamespaceIndex => InstanceNamespaceIndex;

        /// <inheritdoc/>
        /// <remarks>
        /// Virtual so a companion-specification subclass can append the units
        /// its own model satisfies; an override is expected to include the
        /// base result rather than replace it.
        /// </remarks>
        public virtual ArrayOf<QualifiedName> ConformanceUnits => BuildConformanceUnits();

        /// <inheritdoc/>
        /// <remarks>
        /// Virtual for the same reason as <see cref="ConformanceUnits"/>.
        /// </remarks>
        public virtual ArrayOf<string> ServerProfiles => BuildServerProfiles();

        /// <summary>
        /// Adds the conformance contribution of a companion composed into this manager.
        /// Duplicate registrations of the same instance have no effect. Contributions are
        /// read afresh when capabilities are published and must not refer back to this manager.
        /// </summary>
        public void RegisterConformanceContributor(IConformanceContributor contributor)
        {
            if (contributor == null)
            {
                throw new ArgumentNullException(nameof(contributor));
            }
            if (ReferenceEquals(contributor, this))
            {
                throw new ArgumentException("A manager cannot contribute to itself.", nameof(contributor));
            }
            lock (m_conformanceGate)
            {
                if (!m_conformanceContributors.Exists(registered => ReferenceEquals(registered, contributor)))
                {
                    m_conformanceContributors.Add(contributor);
                }
            }
        }

        /// <summary>
        /// Gives a companion runtime the lifetime of this manager: it is disposed, newest first,
        /// when the manager is disposed. A failing resource does not keep the others from being
        /// released. A resource registered twice is disposed once.
        /// </summary>
        /// <param name="resource">The resource to release with the manager.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ObjectDisposedException">Final manager shutdown has started.</exception>
        public void RegisterResource(IAsyncDisposable resource)
        {
            if (resource == null)
            {
                throw new ArgumentNullException(nameof(resource));
            }
            lock (m_resourceGate)
            {
                if (m_resourceRegistrationClosed)
                {
                    throw new ObjectDisposedException(nameof(DiNodeManager));
                }
                if (!m_resources.Exists(registered => ReferenceEquals(registered, resource)))
                {
                    m_resources.Add(resource);
                }
            }
        }

        /// <summary>
        /// Adds an <c>Organizes</c> reference from the predefined node
        /// <paramref name="targetId"/> to <paramref name="instance"/>, for example from the
        /// Machinery <c>Machines</c> folder. Returns <see langword="true"/> when the target was
        /// found and the reference was added.
        /// </summary>
        /// <param name="targetId">The NodeId of the organizing node.</param>
        /// <param name="instance">The node to organize.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public bool TryOrganizeUnder(NodeId targetId, BaseInstanceState instance)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }
            if (targetId.IsNull || FindPredefinedNode(targetId) is not BaseObjectState folder)
            {
                return false;
            }
            folder.AddReference(Opc.Ua.Types.ReferenceTypeIds.Organizes, false, instance.NodeId);
            instance.AddReference(Opc.Ua.Types.ReferenceTypeIds.Organizes, true, folder.NodeId);
            return true;
        }

        /// <summary>
        /// References an instance from the Machinery Machines folder when it is loaded.
        /// </summary>
        /// <remarks>
        /// Retained for derived managers. New callers should resolve their model's generated
        /// folder identifier and call <see cref="TryOrganizeUnder"/>.
        /// </remarks>
        protected internal bool TryAddToMachinesFolder(BaseInstanceState instance)
        {
            return TryOrganizeUnder(NodeId.Create(
                1001u, "http://opcfoundation.org/UA/Machinery/", Server.NamespaceUris), instance);
        }

        /// <summary>
        /// Returns whether <paramref name="root"/> reaches <paramref name="target"/> through
        /// forward hierarchical references within this manager, including child relationships.
        /// </summary>
        /// <param name="root">The starting node.</param>
        /// <param name="target">The node to locate.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public bool IsReachableFrom(NodeState root, NodeState target)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            var visited = new HashSet<NodeId> { root.NodeId };
            var pending = new Queue<NodeState>();
            pending.Enqueue(root);
            var children = new List<BaseInstanceState>();
            var references = new List<IReference>();
            while (pending.Count > 0)
            {
                NodeState node = pending.Dequeue();
                if (ReferenceEquals(node, target))
                {
                    return true;
                }
                children.Clear();
                node.GetChildren(SystemContext, children);
                foreach (BaseInstanceState child in children)
                {
                    if (Server.TypeTree.IsTypeOf(
                        child.ReferenceTypeId, Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences))
                    {
                        if (ReferenceEquals(child, target))
                        {
                            return true;
                        }
                        if (visited.Add(child.NodeId))
                        {
                            pending.Enqueue(child);
                        }
                    }
                }
                references.Clear();
                node.GetReferences(SystemContext, references);
                foreach (IReference reference in references)
                {
                    if (reference.IsInverse || reference.TargetId.IsAbsolute ||
                        !Server.TypeTree.IsTypeOf(
                            reference.ReferenceTypeId, Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences))
                    {
                        continue;
                    }
                    NodeId targetId = ExpandedNodeId.ToNodeId(reference.TargetId, SystemContext.NamespaceUris);
                    NodeState? next = FindPredefinedNode(targetId);
                    if (next != null)
                    {
                        if (ReferenceEquals(next, target))
                        {
                            return true;
                        }
                        if (visited.Add(next.NodeId))
                        {
                            pending.Enqueue(next);
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Keeps a device reachable from the DI DeviceSet (OPC 10000-100 4.9). When no hierarchical
        /// path exists, DeviceSet organizes the outermost component in its parent chain rather than
        /// each nested device of a composed device.
        /// </summary>
        /// <param name="device">The device or component to locate.</param>
        /// <returns>
        /// <see langword="true"/> if the device is reachable; <see langword="false"/> if this manager
        /// has no DeviceSet. Repeated calls do not add duplicate references.
        /// </returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool TryEnsureDeviceSetReachability(BaseInstanceState device)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }
            NodeState? deviceSet = FindPredefinedNode(NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet, DiNamespaceUri, Server.NamespaceUris));
            if (deviceSet == null)
            {
                return false;
            }
            BaseInstanceState root = device;
            NodeState ancestor = device;
            var visited = new HashSet<NodeState>();
            while (ancestor is BaseInstanceState instance && instance.Parent != null &&
                Server.TypeTree.IsTypeOf(
                    instance.ReferenceTypeId, Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences) &&
                visited.Add(ancestor))
            {
                ancestor = instance.Parent;
                if (ReferenceEquals(ancestor, deviceSet) ||
                    (ancestor.ReferenceExists(
                        Opc.Ua.Types.ReferenceTypeIds.Organizes, true, deviceSet.NodeId) &&
                    deviceSet.ReferenceExists(
                        Opc.Ua.Types.ReferenceTypeIds.Organizes, false, ancestor.NodeId)))
                {
                    return true;
                }
                if (ancestor is BaseInstanceState component && IsDiComponent(component))
                {
                    root = component;
                }
            }
            if (!IsReachableFrom(deviceSet, root))
            {
                return TryOrganizeUnder(deviceSet.NodeId, root);
            }
            return true;
        }

        /// <summary>
        /// Recognizes both generated states and generic states carrying a DI component type.
        /// </summary>
        private bool IsDiComponent(BaseInstanceState instance)
        {
            return instance is ComponentState || Server.TypeTree.IsTypeOf(
                instance.TypeDefinitionId,
                NodeId.Create(Opc.Ua.Di.ObjectTypes.ComponentType, DiNamespaceUri, Server.NamespaceUris));
        }

        /// <summary>
        /// Releases the registered resources, newest first. Repeated calls release only resources
        /// registered since the previous call, and a failure does not prevent the remaining releases.
        /// Overlapping drains await the preceding batch and include its failures.
        /// </summary>
        /// <exception cref="AggregateException">One or more resources failed to release.</exception>
        public ValueTask DisposeResourcesAsync()
        {
            return DrainResourcesAsync(closeRegistration: false);
        }

        /// <summary>
        /// Releases companion resources before the manager itself, even if a resource fails.
        /// Closes registration atomically with the final snapshot and joins pending explicit drains.
        /// </summary>
        protected override async ValueTask DisposeAsyncCore()
        {
            try
            {
                await DrainResourcesAsync(closeRegistration: true).ConfigureAwait(false);
            }
            finally
            {
                await base.DisposeAsyncCore().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Captures a batch and publishes its completion before invoking any resource callbacks.
        /// </summary>
        private ValueTask DrainResourcesAsync(bool closeRegistration)
        {
            IAsyncDisposable[] resources;
            Task preceding;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (m_resourceGate)
            {
                m_resourceRegistrationClosed |= closeRegistration;
                preceding = m_resourceDrain.IsCompleted ? Task.CompletedTask : m_resourceDrain;
                resources = [.. m_resources];
                m_resources.Clear();
                m_resourceDrain = completion.Task;
            }
            _ = CompleteResourceDrainAsync(preceding, resources, completion);
            return new ValueTask(completion.Task);
        }

        /// <summary>
        /// Awaits the previous batch, attempts every resource, and shares the combined result.
        /// </summary>
        private static async Task CompleteResourceDrainAsync(
            Task preceding,
            IAsyncDisposable[] resources,
            TaskCompletionSource<bool> completion)
        {
            List<Exception>? errors = null;
            try
            {
                await preceding.ConfigureAwait(false);
            }
            catch (AggregateException ex)
            {
                errors = [.. ex.InnerExceptions];
            }
            for (int ii = resources.Length - 1; ii >= 0; ii--)
            {
                try
                {
                    await resources[ii].DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }
            if (errors != null)
            {
                completion.TrySetException(
                    new AggregateException("Releasing the resources of the node manager failed.", errors));
            }
            else
            {
                completion.TrySetResult(true);
            }
        }

        /// <summary>
        /// Keeps runtime-created DI components reachable from DeviceSet without reorganizing
        /// model declarations or the predefined instances loaded during startup.
        /// </summary>
        protected override async ValueTask AddPredefinedNodeAsync(
            ISystemContext context,
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            await base.AddPredefinedNodeAsync(context, node, cancellationToken).ConfigureAwait(false);
            if (m_modelsLoaded && !node.IsPartOfTypeHierarchy &&
                node is BaseInstanceState component && IsDiComponent(component))
            {
                TryEnsureDeviceSetReachability(component);
            }
        }

        /// <summary>
        /// Removes the local DeviceSet link even when this owner is used outside master-manager routing.
        /// </summary>
        protected override async ValueTask RemovePredefinedNodeAsync(
            ISystemContext context,
            NodeState node,
            List<LocalReference> referencesToRemove,
            CancellationToken cancellationToken = default)
        {
            NodeState? deviceSet = FindPredefinedNode(NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet, DiNamespaceUri, Server.NamespaceUris));
            if (deviceSet != null)
            {
                deviceSet.RemoveReference(Opc.Ua.Types.ReferenceTypeIds.Organizes, false, node.NodeId);
                node.RemoveReference(Opc.Ua.Types.ReferenceTypeIds.Organizes, true, deviceSet.NodeId);
            }
            await base.RemovePredefinedNodeAsync(context, node, referencesToRemove, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            NodeStateCollection nodes = new NodeStateCollection().AddOpcUaDi(context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <summary>
        /// Appends neutral providers to the owner's model collection before indexing any nodes.
        /// </summary>
        protected override async ValueTask LoadPredefinedNodesAsync(
            ISystemContext context,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            NodeStateCollection nodes = await LoadPredefinedNodesAsync(context, cancellationToken)
                .ConfigureAwait(false);
            for (int ii = 0; ii < m_providers.Count; ii++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_providers[ii].AddPredefinedNodes(nodes, context);
            }
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                await AddPredefinedNodeAsync(context, nodes[ii], cancellationToken).ConfigureAwait(false);
            }
            await AddReverseReferencesAsync(externalReferences, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Loads predefined nodes, awaits fluent configuration, registers authored nodes and completes references.
        /// When a hosting post-setup runner is configured, its shared-service callbacks and the final builder seal
        /// wait for published readiness. Without a runner the builder is sealed here.
        /// Subclasses should override <see cref="FluentNodeManagerBase.ConfigureAsync"/> rather than this method.
        /// </summary>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await LoadPredefinedNodesAsync(
                SystemContext, externalReferences, cancellationToken).ConfigureAwait(false);
            m_modelsLoaded = true;

            NodeManagerBuilder builder = CreateFluentBuilder(InstanceNamespaceIndex);

            await ConfigureAsync(builder, cancellationToken).ConfigureAwait(false);

            // Register nodes the subclass staged through the builder's Add*
            // methods before the reverse-reference pass runs, so their
            // references to externally owned nodes are mirrored too.
            await RegisterAuthoredNodesAsync(builder, cancellationToken)
                .ConfigureAwait(false);
            await CompleteConfigureAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            if (PostSetupRunner is { } runner)
            {
                DeferConfigurationUntilReady(builder, ct => runner.RunAsync(this, ct));
            }
            await SealConfigurationAsync(builder, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Completes the shared-service setup before sealing the prepared builder.
        /// </summary>
        public ValueTask OnServerReadyAsync(CancellationToken cancellationToken = default)
        {
            return CompletePublishedConfigurationAsync(cancellationToken);
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
        /// <remarks>
        /// A creation that fails while the device is registered is undone:
        /// the device is removed from its parent and from the address space,
        /// so the name can be used again.
        /// </remarks>
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
        /// <remarks>
        /// A creation that fails while the device is registered is undone:
        /// the device is removed from its parent and from the address space,
        /// so the name can be used again.
        /// </remarks>
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

            try
            {
                await AddPredefinedNodeAsync(SystemContext, device, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Left in place, the half-registered device would keep its
                // name taken: a retry fails with BadBrowseNameDuplicated.
                await RemoveUnregisteredDeviceAsync(effectiveParent, device).ConfigureAwait(false);
                throw;
            }

            return new DeviceBuilder<TDevice>(this, device, GetOrCreateBuilder());
        }

        /// <summary>
        /// Undoes a device creation that failed after the device was attached
        /// to its parent: deletes what was registered of it (its nodes and the
        /// references to it) and detaches it from the parent.
        /// </summary>
        /// <remarks>
        /// A failure of the cleanup is logged rather than thrown, so the caller
        /// sees the exception that failed the creation.
        /// </remarks>
        private async ValueTask RemoveUnregisteredDeviceAsync(NodeState parent, ComponentState device)
        {
            try
            {
                // Only the node this call registered is deleted; a node of the
                // same NodeId that belongs to someone else is left alone.
                if (ReferenceEquals(FindPredefinedNode(device.NodeId), device))
                {
                    await DeleteNodeAsync(SystemContext, device.NodeId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                m_logger.DeviceRollbackFailed(ex, device.BrowseName.Name);
            }

            try
            {
                // Deleting a registered device detaches it already; a device
                // that failed before it was registered, or whose deletion
                // failed, is still attached. The parent is caller supplied and
                // RemoveChild is virtual, so a failing detach is caught like a
                // failing delete.
                parent.RemoveChild(device);
            }
            catch (Exception ex)
            {
                m_logger.DeviceRollbackFailed(ex, device.BrowseName.Name);
            }
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
        /// Registers a composed runtime's root notifier and attaches existing event subscriptions.
        /// </summary>
        /// <param name="notifier">The event source owned by this node manager.</param>
        /// <param name="cancellationToken">Cancels notifier registration.</param>
        public ValueTask RegisterRootNotifierAsync(
            NodeState notifier,
            CancellationToken cancellationToken = default)
        {
            return AddRootNotifierAsync(notifier, cancellationToken);
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
                },
                dataTypeId => PredefinedNodes.Values.FindByDataType(dataTypeId));

            AttachToBuilder(m_builder);
            return m_builder;
        }

        private ArrayOf<QualifiedName> BuildConformanceUnits()
        {
            var units = new List<QualifiedName>();
            if (HasDeviceSet())
            {
                // "DI Offline" asks for offline and online representations of
                // the devices and the methods that transfer data between them.
                // This manager builds neither, so a DeviceSet does not meet it.
                units.Add(new QualifiedName(ConformanceUnitNames.DeviceTopology));
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

            // SoftwareLoadingMode.Package builds a PackageLoadingType rather
            // than the FileSystemLoadingType "DI SU FileSystem Loading" asks
            // for, and every Installation offers InstallSoftwarePackage, which
            // "DI SU Installation for File System" rules out, as well as
            // InstallFiles, which "DI SU Installation for Cached Loading"
            // rules out. No SoftwareUpdate carries the UpdateStatus variable
            // "DI SU UpdateStatus" asks for. None of these units is claimed.
            if (m_softwareUpdateLoadingModes.Contains(SoftwareLoadingMode.Direct))
            {
                units.Add(new QualifiedName(ConformanceUnitNames.SoftwareUpdateDirectLoading));
            }
            if (m_softwareUpdateLoadingModes.Contains(SoftwareLoadingMode.Cached))
            {
                units.Add(new QualifiedName(ConformanceUnitNames.SoftwareUpdateCachedLoading));
            }
            foreach (IConformanceContributor contributor in GetConformanceContributors())
            {
                foreach (QualifiedName unit in contributor.ConformanceUnits)
                {
                    if (!units.Contains(unit))
                    {
                        units.Add(unit);
                    }
                }
            }
            return units.ToArrayOf();
        }

        /// <summary>
        /// Advertises each facet whose mandatory conformance units are all advertised.
        /// </summary>
        /// <remarks>
        /// Reads the units through the virtual <see cref="ConformanceUnits"/>,
        /// so a unit a subclass appends counts toward the DI facets as well.
        /// </remarks>
        private ArrayOf<string> BuildServerProfiles()
        {
            var units = new HashSet<QualifiedName>();
            foreach (QualifiedName unit in ConformanceUnits)
            {
                units.Add(unit);
            }

            var profiles = new List<string>();
            foreach (DiFacet facet in s_facets)
            {
                if (facet.IsMetBy(units))
                {
                    profiles.Add(facet.Profile);
                }
            }
            foreach (IConformanceContributor contributor in GetConformanceContributors())
            {
                foreach (string profile in contributor.ServerProfiles)
                {
                    if (!profiles.Contains(profile))
                    {
                        profiles.Add(profile);
                    }
                }
            }
            return profiles.ToArrayOf();
        }

        private IConformanceContributor[] GetConformanceContributors()
        {
            lock (m_conformanceGate)
            {
                return [.. m_conformanceContributors];
            }
        }

        private readonly Lock m_conformanceGate = new();
        private readonly List<IConformanceContributor> m_conformanceContributors = [];

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

        /// <summary>
        /// A server facet and the conformance units it mandates.
        /// </summary>
        /// <param name="Profile">The facet URI.</param>
        /// <param name="MandatoryUnits">The names of the mandatory units.</param>
        private readonly record struct DiFacet(string Profile, ArrayOf<string> MandatoryUnits)
        {
            /// <summary>
            /// Gets whether every mandatory unit is in <paramref name="units"/>.
            /// </summary>
            public bool IsMetBy(HashSet<QualifiedName> units)
            {
                foreach (string unit in MandatoryUnits)
                {
                    if (!units.Contains(new QualifiedName(unit)))
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="DiNodeManager"/>.
    /// </summary>
    internal static partial class DiNodeManagerLog
    {
        /// <summary>
        /// Reports a failure to remove a device during creation rollback.
        /// </summary>
        [LoggerMessage(EventId = DiServerEventIds.DiNodeManager + 0, Level = LogLevel.Error,
            Message = "Could not remove device '{Name}' after its creation failed.")]
        public static partial void DeviceRollbackFailed(this ILogger logger, Exception ex, string? name);
    }
}
