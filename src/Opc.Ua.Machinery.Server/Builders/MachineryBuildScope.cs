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
            QualifiedName browseName)
        {
            BuildContext = buildContext ??
                throw new ArgumentNullException(nameof(buildContext));
            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
            m_buildCoordinator = GetBuildCoordinator(buildContext);
            EnsureContextMutable(buildContext);
            Context = buildContext.Context;
            BrowseName = browseName;
            m_rootBrowseNameReservation =
                m_buildCoordinator.ReserveRootBrowseName(parent, browseName);
        }

        internal IMachineryBuildContext BuildContext { get; }

        internal ISystemContext Context { get; }

        internal NodeState Parent { get; }

        internal QualifiedName BrowseName { get; }

        internal NodeState Root { get; private set; } = null!;

        /// <summary>
        /// Asynchronous work that must run after the completed tree has been
        /// registered with the node manager — binding a result store to the
        /// transfer object, for example.
        /// </summary>
        internal List<Func<CancellationToken, ValueTask>> PostRegistrationActions { get; } = [];

        /// <summary>
        /// Resources created during registration that must be released when the
        /// build is rolled back.
        /// </summary>
        internal List<IAsyncDisposable> RegisteredResources { get; } = [];

        internal bool IsRegistered { get; private set; }

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
            if (m_registering || IsRegistered)
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
                if (indexedNode != null && !ReferenceEquals(indexedNode, node))
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

            if (Root == null)
            {
                return;
            }
            try
            {
                if (!Root.NodeId.IsNull &&
                    BuildContext.Manager.FindPredefinedNode(Root.NodeId) != null)
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

        private readonly List<MachineryNodeBuilder> m_builders = [];
        private readonly List<MachineryFacet> m_facets = [];
        private readonly IMachineryBuildCoordinator m_buildCoordinator;
        private IDisposable? m_rootBrowseNameReservation;
        private bool m_registering;
    }
}
