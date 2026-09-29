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
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// The coordination a <see cref="MachineryBuildScope"/> needs from its
    /// build context beyond the public <see cref="IMachineryBuildContext"/>:
    /// the sealed state and build leases, the root browse-name and NodeId
    /// reservations, and the sink for the facets a build materialised.
    /// </summary>
    internal interface IMachineryBuildCoordinator
    {
        /// <summary>
        /// Gets a value indicating whether the context is sealed, after which
        /// no machine can be started or changed through it.
        /// </summary>
        bool IsSealed { get; }

        /// <summary>
        /// Gets the validated server options the context was created with.
        /// </summary>
        MachineryServerOptions Options { get; }

        /// <summary>
        /// Marks a machine build as active, so the context cannot be sealed
        /// until the returned lease is disposed.
        /// </summary>
        /// <returns>The lease; disposing it ends the build.</returns>
        /// <exception cref="ServiceResultException">
        /// The context is already sealed.
        /// </exception>
        IDisposable AcquireBuildLease();

        /// <summary>
        /// Gets a value indicating whether
        /// <see cref="ReleaseNodeIdReservation(NodeState)"/> has any effect.
        /// Only a <see cref="MachineryNodeManager"/> that is also the context's
        /// NodeId factory reserves NodeIds that need releasing.
        /// </summary>
        bool ReleasesNodeIdReservations { get; }

        /// <summary>
        /// Releases every NodeId reserved for <paramref name="node"/>, so a
        /// node that never reached the address space does not keep its
        /// identifier.
        /// </summary>
        /// <param name="node">The node whose reservations are released.</param>
        void ReleaseNodeIdReservation(NodeState node);

        /// <summary>
        /// Reserves <paramref name="browseName"/> for a new child of
        /// <paramref name="parent"/> while a machine is being built, so two
        /// concurrent builds cannot create siblings with the same name.
        /// </summary>
        /// <param name="parent">The node the machine is created below.</param>
        /// <param name="browseName">The browse name to reserve.</param>
        /// <returns>The reservation; disposing it releases the name.</returns>
        /// <exception cref="ServiceResultException">
        /// <paramref name="parent"/> already has, or is already creating, a
        /// child with that name.
        /// </exception>
        IDisposable ReserveRootBrowseName(NodeState parent, QualifiedName browseName);

        /// <summary>
        /// Records a facet a registered build materialised, so the server
        /// advertises the conformance units that depend on it.
        /// </summary>
        /// <param name="facet">The facet to record.</param>
        void RecordFacet(MachineryFacet facet);

        /// <summary>
        /// Hands the resources a registered build created to the node
        /// manager's coordinator, which releases them when the manager is
        /// disposed.
        /// </summary>
        /// <param name="resources">The resources to hand over.</param>
        void AdoptResources(IReadOnlyList<IAsyncDisposable> resources);
    }

    /// <summary>
    /// Implements <see cref="IMachineryBuildContext"/> over a Device
    /// Integration node manager: validates the configured parts and their
    /// namespaces, resolves the <c>Machines</c> folder and <c>DeviceSet</c>,
    /// and starts the machine builders. As the
    /// <see cref="IMachineryBuildCoordinator"/> it tracks the build leases and
    /// delegates reservations to the manager's
    /// <see cref="MachineryBuildCoordinator"/>.
    /// </summary>
    internal sealed class MachineryBuildContext :
        IMachineryBuildContext,
        IMachineryBuildCoordinator
    {
        public MachineryBuildContext(
            DiNodeManager manager,
            MachineryServerOptions options,
            CancellationToken cancellationToken,
            IDiPostSetupContext? postSetupContext = null)
        {
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            Options = MachineryModelProviderUtilities.ValidateOptions(options);
            Parts = Options.Parts;
            CancellationToken = cancellationToken;
            m_postSetupContext = postSetupContext;

            Context = manager.SystemContext;
            if (Context.NodeIdFactory is not IMachineryNodeIdFactory)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The custom DI node manager '{0}' cannot build Machinery instances " +
                    "because Context.NodeIdFactory does not implement {1}. Implement the " +
                    "marker on the manager with a thread-safe allocator for the configured " +
                    "instance namespace, or assign such a factory before " +
                    "ConfigureMachineryFor/CreateMachineryBuildContext.",
                    manager.GetType().FullName ?? manager.GetType().Name,
                    nameof(IMachineryNodeIdFactory));
            }
            m_releasesNodeIdReservations =
                manager is MachineryNodeManager &&
                ReferenceEquals(Context.NodeIdFactory, manager);

            RequireNamespace(Opc.Ua.Machinery.Namespaces.Machinery, MachineryParts.BuildingBlocks);
            RequireNamespace(
                Opc.Ua.Machinery.ProcessValues.Namespaces.MachineryProcessValues,
                MachineryParts.ProcessValues);
            RequireNamespace(
                Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs,
                MachineryParts.Jobs);
            RequireNamespace(
                Opc.Ua.Machinery.Energy.Namespaces.MachineryEnergy,
                MachineryParts.Energy);
            RequireNamespace(
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                MachineryParts.Result);

            int instanceNamespaceIndex = Context.NamespaceUris.GetIndex(
                Options.InstanceNamespaceUri);
            if (instanceNamespaceIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Machinery instance namespace '{0}' is not registered.",
                    Options.InstanceNamespaceUri);
            }
            InstanceNamespaceIndex = (ushort)instanceNamespaceIndex;
            m_managerCoordinator = MachineryBuildCoordinator.Get(manager);

            var deviceSetId = NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                DiNodeManager.DiNamespaceUri,
                Context.NamespaceUris);
            DeviceSet = manager.FindPredefinedNode(deviceSetId) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The DI DeviceSet node is not available in the Machinery address space.");

            if (Parts.HasFlag(MachineryParts.BuildingBlocks))
            {
                var machinesId = NodeId.Create(
                    Opc.Ua.Machinery.Objects.Machines,
                    Opc.Ua.Machinery.Namespaces.Machinery,
                    Context.NamespaceUris);
                MachinesFolder = manager.FindPredefinedNode(machinesId) ??
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "The Machinery Machines folder is not available in the address space.");
            }
            else
            {
                MachinesFolder = DeviceSet;
            }

            m_nodes = manager.CreateFluentBuilder(InstanceNamespaceIndex);
        }

        public DiNodeManager Manager { get; }

        public ISystemContext Context { get; }

        public INodeManagerBuilder Nodes => m_nodes;

        public ushort InstanceNamespaceIndex { get; }

        public MachineryParts Parts { get; }

        public MachineryServerOptions Options { get; }

        public NodeState MachinesFolder { get; }

        public NodeState DeviceSet { get; }

        public CancellationToken CancellationToken { get; }

        internal bool IsSealed
        {
            get
            {
                lock (m_stateLock)
                {
                    return m_sealed;
                }
            }
        }

        public IMachineBuilder<BaseObjectState> AddMachine(QualifiedName browseName)
        {
            QualifiedName resolved = QualifyInstanceName(browseName);
            return MachineBuilder.CreatePlain(this, resolved);
        }

        public IMachineBuilder<TState> AddMachine<TState>(
            TState machine,
            QualifiedName browseName = default)
            where TState : BaseObjectState
        {
            if (machine == null)
            {
                throw new ArgumentNullException(nameof(machine));
            }
            QualifiedName resolved = browseName.IsNull
                ? machine.BrowseName
                : QualifyInstanceName(browseName);
            if (resolved.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "A machine adopted from a companion model must carry a browse name, " +
                    "or one must be supplied.");
            }
            return MachineBuilder.Adopt(this, machine, resolved);
        }

        public T GetRequiredService<T>() where T : notnull
        {
            if (m_postSetupContext == null)
            {
                throw new InvalidOperationException(
                    "Application services are unavailable for a directly created " +
                    "Machinery build context.");
            }
            return m_postSetupContext.GetRequiredService<T>();
        }

        public T? GetService<T>() where T : class
        {
            return m_postSetupContext?.GetService<T>();
        }

        public ValueTask SealAsync(CancellationToken cancellationToken = default)
        {
            // The state transition stays under the lock; the builder's own
            // asynchronous completion work runs outside it, so no await ever
            // happens while the context lock is held.
            lock (m_stateLock)
            {
                if (m_sealed)
                {
                    return default;
                }
                if (m_activeBuildLeaseCount != 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The Machinery build context cannot be sealed while a build is active.");
                }

                m_sealed = true;
            }

            return m_nodes.SealAsync(cancellationToken);
        }

        IDisposable IMachineryBuildCoordinator.AcquireBuildLease()
        {
            lock (m_stateLock)
            {
                if (m_sealed)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The Machinery build context is sealed.");
                }
                m_activeBuildLeaseCount++;
            }
            return new BuildLease(this);
        }

        void IMachineryBuildCoordinator.ReleaseNodeIdReservation(NodeState node)
        {
            if (m_releasesNodeIdReservations)
            {
                m_managerCoordinator.ReleaseNodeId(node);
            }
        }

        IDisposable IMachineryBuildCoordinator.ReserveRootBrowseName(
            NodeState parent,
            QualifiedName browseName)
        {
            return m_managerCoordinator.ReserveRootBrowseName(Context, parent, browseName);
        }

        void IMachineryBuildCoordinator.RecordFacet(MachineryFacet facet)
        {
            (Manager as IMachineryFacetSink)?.RecordFacet(facet);
        }

        void IMachineryBuildCoordinator.AdoptResources(IReadOnlyList<IAsyncDisposable> resources)
        {
            m_managerCoordinator.AdoptResources(resources);
        }

        bool IMachineryBuildCoordinator.IsSealed => IsSealed;

        bool IMachineryBuildCoordinator.ReleasesNodeIdReservations =>
            m_releasesNodeIdReservations;

        internal QualifiedName QualifyInstanceName(QualifiedName browseName)
        {
            if (browseName.IsNull || string.IsNullOrEmpty(browseName.Name))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "A Machinery browse name must not be empty.");
            }
            return browseName.NamespaceIndex == 0
                ? new QualifiedName(browseName.Name, InstanceNamespaceIndex)
                : browseName;
        }

        private void RequireNamespace(string namespaceUri, MachineryParts part)
        {
            if (!Parts.HasFlag(part))
            {
                return;
            }
            if (Context.NamespaceUris.GetIndex(namespaceUri) < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The namespace '{0}' required by MachineryParts.{1} is not registered.",
                    namespaceUri,
                    part);
            }
        }

        private void ReleaseBuildLease()
        {
            lock (m_stateLock)
            {
                if (m_activeBuildLeaseCount == 0)
                {
                    throw new InvalidOperationException(
                        "The Machinery build context has no active build lease to release.");
                }
                m_activeBuildLeaseCount--;
            }
        }

        private readonly NodeManagerBuilder m_nodes;
        private readonly IDiPostSetupContext? m_postSetupContext;
        private readonly MachineryBuildCoordinator m_managerCoordinator;
        private readonly Lock m_stateLock = new();
        private readonly bool m_releasesNodeIdReservations;
        private int m_activeBuildLeaseCount;
        private bool m_sealed;

        private sealed class BuildLease : IDisposable
        {
            public BuildLease(MachineryBuildContext context)
            {
                m_context = context;
            }

            public void Dispose()
            {
                MachineryBuildContext? context = Interlocked.Exchange(ref m_context, null);
                context?.ReleaseBuildLease();
            }

            private MachineryBuildContext? m_context;
        }
    }
}
