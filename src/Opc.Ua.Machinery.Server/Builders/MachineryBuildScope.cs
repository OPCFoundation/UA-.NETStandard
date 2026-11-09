/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Owns one machine under construction: the staged node graph, the
    /// browse-name and NodeId reservations that keep two concurrent builds
    /// apart, and the post-registration work that can only run once the
    /// machine is part of the address space.
    /// </summary>
    internal sealed class MachineryBuildScope
    {
        public MachineryBuildScope(
            IMachineryBuildContext buildContext,
            NodeState parent,
            QualifiedName browseName,
            bool externallyRegistered = false)
        {
            BuildContext = buildContext ??
                throw new ArgumentNullException(nameof(buildContext));
            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
            m_buildCoordinator = GetBuildCoordinator(buildContext);
            EnsureContextMutable(buildContext);
            Context = buildContext.Context;
            BrowseName = browseName;
            IsExternallyRegistered = externallyRegistered;
            m_rootBrowseNameReservation =
                m_buildCoordinator.ReserveRootBrowseName(parent, browseName);
        }

        internal IMachineryBuildContext BuildContext { get; }

        internal ISystemContext Context { get; }

        internal NodeState Parent { get; }

        internal QualifiedName BrowseName { get; }

        internal NodeState Root { get; private set; } = null!;

        /// <summary>
        /// Provider validation that must finish before any staged node is published.
        /// </summary>
        internal List<Func<CancellationToken, ValueTask>> PreRegistrationActions { get; } = [];

        /// <summary>
        /// Asynchronous work that must run after the completed tree has been
        /// registered with the node manager — binding a result store to the
        /// transfer object, for example.
        /// </summary>
        internal List<Func<CancellationToken, ValueTask>> PostRegistrationActions { get; } = [];

        /// <summary>
        /// Resources created during registration. They are released when the
        /// build is rolled back, and handed to the registered root's lifetime.
        /// Actual root removal or manager shutdown awaits their cleanup.
        /// </summary>
        internal List<IAsyncDisposable> RegisteredResources { get; } = [];

        internal bool IsRegistered { get; private set; }

        /// <summary>
        /// Gets whether another companion builder owns publication of this root.
        /// </summary>
        internal bool IsExternallyRegistered { get; }

        internal void AttachRoot(NodeState root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
        }

        internal void Abort()
        {
            ReleaseReservations();
        }

        internal static void EnsureContextMutable(IMachineryBuildContext buildContext)
        {
            IMachineryBuildCoordinator coordinator = GetBuildCoordinator(buildContext);
            if (coordinator.IsSealed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The Machinery build context is sealed.");
            }
        }

        internal static IDisposable AcquireBuildLease(IMachineryBuildContext buildContext)
        {
            return GetBuildCoordinator(buildContext).AcquireBuildLease();
        }

        internal void EnsureMutable()
        {
            EnsureContextMutable(BuildContext);
            if (m_registering || IsRegistered || m_aborted)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Machine '{0}' has already started registration.",
                    BrowseName);
            }
        }

        internal void EnsurePart(MachineryParts part, string what)
        {
            if (!BuildContext.Parts.HasFlag(part))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "{0} requires MachineryParts.{1}, which this server does not expose. " +
                    "Add the part to MachineryServerOptions.Parts.",
                    what,
                    part);
            }
        }

        internal void RegisterBuilder(MachineryNodeBuilder builder)
        {
            m_builders.Add(builder);
        }

        /// <summary>
        /// Records a facet the build materialised. Before registration the
        /// facets are held back and flushed only once the machine is actually
        /// in the address space, so a rolled-back build advertises nothing.
        /// Afterwards — a result-ready event raised at runtime, for example —
        /// they go straight through.
        /// </summary>
        internal void RecordFacet(MachineryFacet facet)
        {
            if (IsRegistered)
            {
                m_buildCoordinator.RecordFacet(facet);
                return;
            }
            m_facets.Add(facet);
        }

        internal async ValueTask RegisterAsync(CancellationToken cancellationToken)
        {
            if (IsExternallyRegistered)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "An externally staged machine is completed by its registration owner, not BuildAsync.");
            }
            EnsureMutable();
            if (Root == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Machine '{0}' has no root node.",
                    BrowseName);
            }
            m_registering = true;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int ii = 0; ii < PreRegistrationActions.Count; ii++)
                {
                    await PreRegistrationActions[ii](cancellationToken).ConfigureAwait(false);
                }
                VerifyNodeIds();
                VerifyEnergyMainGrouping();

                Parent.AddChild((BaseInstanceState)Root);
                await BuildContext.Manager
                    .AddPredefinedNodeAsync(Root, cancellationToken)
                    .ConfigureAwait(false);

                CacheNodeBuilders();

                for (int ii = 0; ii < PostRegistrationActions.Count; ii++)
                {
                    await PostRegistrationActions[ii](cancellationToken).ConfigureAwait(false);
                }

                ArrayOf<IAsyncDisposable> resources = RegisteredResources.ToArrayOf();
                RegisteredResources.Clear();
                await m_buildCoordinator.AdoptResourcesAsync(Root, resources).ConfigureAwait(false);
                IsRegistered = true;
                for (int ii = 0; ii < m_facets.Count; ii++)
                {
                    m_buildCoordinator.RecordFacet(m_facets[ii]);
                }
            }
            catch
            {
                IsRegistered = false;
                await RollbackRegistrationAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                ReleaseReservations();
                m_registering = false;
            }
        }

        /// <summary>
        /// Validates an externally owned graph without publishing it.
        /// </summary>
        internal async ValueTask PrepareExternalAsync(CancellationToken cancellationToken)
        {
            EnsureMutable();
            m_registering = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (BuildContext.Manager.FindPredefinedNode(Root.NodeId) != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "A Machinery registration participant requires an unregistered root.");
            }
            MachineryBuilderUtilities.ClearModellingRules(Context, Root);
            for (int ii = 0; ii < PreRegistrationActions.Count; ii++)
            {
                await PreRegistrationActions[ii](cancellationToken).ConfigureAwait(false);
            }
            VerifyNodeIds();
            VerifyEnergyMainGrouping();
        }

        /// <summary>
        /// Attaches bindings and an owned discovery reference after the owner's registration.
        /// </summary>
        internal async ValueTask BindExternalAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(BuildContext.Manager.FindPredefinedNode(Root.NodeId), Root))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The registration owner has not published this Machinery root.");
            }
            CacheNodeBuilders();
            for (int ii = 0; ii < PostRegistrationActions.Count; ii++)
            {
                await PostRegistrationActions[ii](cancellationToken).ConfigureAwait(false);
            }
            AddDiscoveryReferences();
            m_previousStateChanged = Root.OnStateChangedAsync;
            m_deleteHandler = OnRootChangedAsync;
            Root.OnStateChangedAsync = m_deleteHandler;
            NodeManagerBuilder lifetimeBuilder = BuildContext.Manager.CreateFluentBuilder(
                BuildContext.InstanceNamespaceIndex);
            lifetimeBuilder.Node<BaseObjectState>(Root.NodeId).Attach(
                (_, _, _) => new ValueTask<IAsyncDisposable?>(new ExternalBindingLifetime(this)));
            await lifetimeBuilder.SealAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Records only successfully bound facets; resource lifetime stays with the actual root.
        /// </summary>
        internal void CommitExternal()
        {
            IsRegistered = true;
            for (int ii = 0; ii < m_facets.Count; ii++)
            {
                m_buildCoordinator.RecordFacet(m_facets[ii]);
            }
            ReleaseReservations();
            m_registering = false;
        }

        /// <summary>
        /// Unwinds only this participant, without deleting or reparenting the owning tree.
        /// </summary>
        internal async ValueTask AbortExternalAsync()
        {
            try
            {
                if (!IsRegistered)
                {
                    m_aborted = true;
                    await ReleaseExternalAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                ReleaseReservations();
                m_registering = false;
            }
        }

        private void VerifyNodeIds()
        {
            var nodes = new List<NodeState> { Root };
            var nodeIds = new HashSet<NodeId>();
            var children = new List<BaseInstanceState>();
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                NodeState node = nodes[ii];
                if (node.NodeId.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Machinery descendant '{0}' has a null NodeId after instance assignment.",
                        node.BrowseName);
                }
                if (node.NodeId.NamespaceIndex != BuildContext.InstanceNamespaceIndex)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Machinery descendant '{0}' has NodeId '{1}' outside the " +
                        "instance namespace.",
                        node.BrowseName,
                        node.NodeId);
                }
                if (!nodeIds.Add(node.NodeId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Machinery descendant '{0}' has duplicate NodeId '{1}'.",
                        node.BrowseName,
                        node.NodeId);
                }
                NodeState? indexedNode = BuildContext.Manager.FindPredefinedNode(node.NodeId);
                if (indexedNode != null &&
                    (IsExternallyRegistered || !ReferenceEquals(indexedNode, node)))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Machinery descendant '{0}' has NodeId '{1}', which is already " +
                        "indexed by node '{2}'.",
                        node.BrowseName,
                        node.NodeId,
                        indexedNode.BrowseName);
                }

                children.Clear();
                node.GetChildren(Context, children);
                for (int childIndex = 0; childIndex < children.Count; childIndex++)
                {
                    nodes.Add(children[childIndex]);
                }
            }
        }

        /// <summary>
        /// Verifies the OPC 40001-4 <c>Machinery Energy Main grouping</c> unit.
        /// </summary>
        /// <remarks>
        /// <para>
        /// §8.1 requires every resource folder below <c>Monitoring/Consumption</c>
        /// to carry a <c>Main</c> metering point that groups the resource's
        /// readings. <see cref="MachineryEnergyBuilder"/> always creates
        /// <c>Main</c> itself, so a resource the builder made satisfies the unit
        /// by construction — but an application is free to attach a resource
        /// folder to <c>Consumption</c> directly, and such a folder would make
        /// the server advertise a unit it does not meet.
        /// </para>
        /// <para>
        /// Checked here rather than in the energy builder precisely because the
        /// folders worth checking are the ones the energy builder never saw.
        /// </para>
        /// </remarks>
        private void VerifyEnergyMainGrouping()
        {
            BaseInstanceState? consumption = FindConsumptionFolder();
            if (consumption == null)
            {
                return;
            }

            ushort energyNamespaceIndex = MachineryBuilderUtilities.NamespaceIndex(
                Context,
                Opc.Ua.Machinery.Energy.Namespaces.MachineryEnergy);
            var mainName = new QualifiedName(
                Opc.Ua.Machinery.Energy.BrowseNames.Main,
                energyNamespaceIndex);

            var resources = new List<BaseInstanceState>();
            consumption.GetChildren(Context, resources);
            for (int ii = 0; ii < resources.Count; ii++)
            {
                BaseInstanceState resource = resources[ii];
                if (resource is not FolderState)
                {
                    continue;
                }
                if (resource.FindChild(Context, mainName) is not Opc.Ua.ECM.EnergyMeasurementState)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "OPC 40001-4 requires a Main metering point on every resource " +
                        "folder below Monitoring/Consumption, but '{0}' on machine '{1}' " +
                        "has none. Add it through the energy builder, or do not attach " +
                        "the folder to Consumption.",
                        resource.BrowseName,
                        BrowseName);
                }
            }
        }

        private BaseInstanceState? FindConsumptionFolder()
        {
            ushort machineryNamespaceIndex = MachineryBuilderUtilities.NamespaceIndex(
                Context,
                Opc.Ua.Machinery.Namespaces.Machinery);
            BaseInstanceState? monitoring = Root?.FindChild(
                Context,
                new QualifiedName(
                    Opc.Ua.Machinery.BrowseNames.Monitoring,
                    machineryNamespaceIndex));
            return monitoring?.FindChild(
                Context,
                new QualifiedName(
                    Opc.Ua.Machinery.BrowseNames.Consumption,
                    machineryNamespaceIndex));
        }

        private static IMachineryBuildCoordinator GetBuildCoordinator(
            IMachineryBuildContext buildContext)
        {
            return buildContext as IMachineryBuildCoordinator ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Machinery build context does not provide build coordination.");
        }

        private void CacheNodeBuilders()
        {
            for (int ii = 0; ii < m_builders.Count; ii++)
            {
                m_builders[ii].CacheNodeBuilder();
            }
        }

        private void ReleaseNodeIdReservations()
        {
            if (!m_buildCoordinator.ReleasesNodeIdReservations || Root == null)
            {
                return;
            }

            var nodes = new List<NodeState> { Root };
            var children = new List<BaseInstanceState>();
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                NodeState node = nodes[ii];
                m_buildCoordinator.ReleaseNodeIdReservation(node);

                children.Clear();
                node.GetChildren(Context, children);
                for (int childIndex = 0; childIndex < children.Count; childIndex++)
                {
                    nodes.Add(children[childIndex]);
                }
            }
        }

        private void ReleaseReservations()
        {
            ReleaseNodeIdReservations();
            ReleaseRootBrowseNameReservation();
        }

        private void ReleaseRootBrowseNameReservation()
        {
            m_rootBrowseNameReservation?.Dispose();
            m_rootBrowseNameReservation = null;
        }

        private async ValueTask RollbackRegistrationAsync()
        {
            await ReleaseResourcesAsync().ConfigureAwait(false);

            if (Root == null)
            {
                return;
            }
            try
            {
                if (!Root.NodeId.IsNull &&
                    ReferenceEquals(BuildContext.Manager.FindPredefinedNode(Root.NodeId), Root))
                {
                    await BuildContext.Manager.DeleteNodeAsync(
                        BuildContext.Manager.SystemContext,
                        Root.NodeId,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                Parent.RemoveChild((BaseInstanceState)Root);
            }
        }

        /// <summary>
        /// Releases resources in reverse order without masking the original build failure.
        /// </summary>
        private async ValueTask ReleaseResourcesAsync()
        {
            for (int ii = RegisteredResources.Count - 1; ii >= 0; ii--)
            {
                try
                {
                    await RegisteredResources[ii].DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A resource that fails to release must not mask the
                    // original build failure that triggered the rollback.
                }
            }
            RegisteredResources.Clear();
        }

        /// <summary>
        /// Adds discovery links without changing the root's owning parent or reference type.
        /// </summary>
        private void AddDiscoveryReferences()
        {
            NodeId organizes = Opc.Ua.ReferenceTypeIds.Organizes;
            m_forwardAdded = !Parent.ReferenceExists(organizes, false, Root.NodeId);
            m_inverseAdded = !Root.ReferenceExists(organizes, true, Parent.NodeId);
            if (m_forwardAdded)
            {
                Parent.AddReference(organizes, false, Root.NodeId);
            }
            if (m_inverseAdded)
            {
                Root.AddReference(organizes, true, Parent.NodeId);
            }
        }

        /// <summary>
        /// Releases bindings once, either during rollback or through the manager's attachment ledger.
        /// </summary>
        private async ValueTask ReleaseExternalAsync()
        {
            if (Interlocked.Exchange(ref m_externalDisposed, 1) != 0)
            {
                return;
            }
            if (Root.OnStateChangedAsync == m_deleteHandler)
            {
                Root.OnStateChangedAsync = m_previousStateChanged;
            }
            if (m_forwardAdded)
            {
                Parent.RemoveReference(Opc.Ua.ReferenceTypeIds.Organizes, false, Root.NodeId);
            }
            if (m_inverseAdded)
            {
                Root.RemoveReference(Opc.Ua.ReferenceTypeIds.Organizes, true, Parent.NodeId);
            }
            await ReleaseResourcesAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Releases per-root resources when the existing node lifecycle reports deletion.
        /// </summary>
        private async ValueTask OnRootChangedAsync(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes,
            CancellationToken cancellationToken)
        {
            NodeStateChangedAsyncHandler? previous = m_previousStateChanged;
            if ((changes & NodeStateChangeMasks.Deleted) != 0)
            {
                await ReleaseExternalAsync().ConfigureAwait(false);
            }
            if (previous != null)
            {
                await previous(context, node, changes, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Lets the manager's existing node-attachment ledger own externally bound resources.
        /// </summary>
        private sealed class ExternalBindingLifetime : IAsyncDisposable
        {
            /// <summary>
            /// Creates the lifetime for one staged machine.
            /// </summary>
            public ExternalBindingLifetime(MachineryBuildScope scope)
            {
                m_scope = scope;
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return m_scope.ReleaseExternalAsync();
            }

            private readonly MachineryBuildScope m_scope;
        }

        private readonly List<MachineryNodeBuilder> m_builders = [];
        private readonly List<MachineryFacet> m_facets = [];
        private readonly IMachineryBuildCoordinator m_buildCoordinator;
        private IDisposable? m_rootBrowseNameReservation;
        private bool m_forwardAdded;
        private bool m_inverseAdded;
        private int m_externalDisposed;
        private NodeStateChangedAsyncHandler? m_previousStateChanged;
        private NodeStateChangedAsyncHandler? m_deleteHandler;
        private bool m_registering;
        private bool m_aborted;
    }
}
