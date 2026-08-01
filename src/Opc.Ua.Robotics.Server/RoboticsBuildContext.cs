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
using System.Threading;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Robotics.Server
{
    internal interface IRoboticsBuildCoordinator
    {
        bool IsSealed { get; }

        IDisposable AcquireBuildLease();

        bool ReleasesNodeIdReservations { get; }

        void ReleaseNodeIdReservation(NodeState node);

        IDisposable ReserveRootBrowseName(
            NodeState parent,
            QualifiedName browseName);
    }

    internal sealed class RoboticsBuildContext :
        IRoboticsBuildContext,
        IRoboticsBuildCoordinator
    {
        public RoboticsBuildContext(
            DiNodeManager manager,
            RoboticsServerOptions options,
            CancellationToken cancellationToken,
            IDiPostSetupContext? postSetupContext = null)
        {
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            options = RoboticsModelProviderUtilities.ValidateOptions(options);
            CancellationToken = cancellationToken;
            m_postSetupContext = postSetupContext;

            Context = manager.SystemContext;
            if (Context.NodeIdFactory is not IRoboticsNodeIdFactory)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The custom DI node manager '{0}' cannot build Robotics instances because " +
                    "Context.NodeIdFactory does not implement {1}. Implement the marker on the " +
                    "manager with a thread-safe allocator for the configured instance namespace, " +
                    "or assign such a factory before ConfigureRoboticsFor/CreateRoboticsBuildContext.",
                    manager.GetType().FullName ?? manager.GetType().Name,
                    nameof(IRoboticsNodeIdFactory));
            }
            m_releasesNodeIdReservations =
                manager is RoboticsNodeManager &&
                ReferenceEquals(Context.NodeIdFactory, manager);

            int iaNamespaceIndex = Context.NamespaceUris.GetIndex(
                Opc.Ua.IA.Namespaces.IA);
            if (iaNamespaceIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The IA namespace '{0}' is not registered.",
                    Opc.Ua.IA.Namespaces.IA);
            }

            int roboticsNamespaceIndex = Context.NamespaceUris.GetIndex(
                Opc.Ua.Robotics.Namespaces.Robotics);
            if (roboticsNamespaceIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Robotics namespace '{0}' is not registered.",
                    Opc.Ua.Robotics.Namespaces.Robotics);
            }

            int instanceNamespaceIndex = Context.NamespaceUris.GetIndex(
                options.InstanceNamespaceUri);
            if (instanceNamespaceIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Robotics instance namespace '{0}' is not registered.",
                    options.InstanceNamespaceUri);
            }
            InstanceNamespaceIndex = (ushort)instanceNamespaceIndex;
            m_managerCoordinator = RoboticsBuildCoordinator.Get(manager);

            var deviceSetId = NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                DiNodeManager.DiNamespaceUri,
                Context.NamespaceUris);
            DeviceSet = manager.FindPredefinedNode(deviceSetId) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The DI DeviceSet node is not available in the Robotics address space.");

            var motionDeviceSystemTypeId = NodeId.Create(
                Opc.Ua.Robotics.ObjectTypes.MotionDeviceSystemType,
                Opc.Ua.Robotics.Namespaces.Robotics,
                Context.NamespaceUris);
            _ = manager.FindPredefinedNode(motionDeviceSystemTypeId) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Robotics MotionDeviceSystemType is not available in the address space.");

            m_nodes = manager.CreateFluentBuilder(InstanceNamespaceIndex);
        }

        public DiNodeManager Manager { get; }

        public ISystemContext Context { get; }

        public INodeManagerBuilder Nodes => m_nodes;

        public ushort InstanceNamespaceIndex { get; }

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

        public T GetRequiredService<T>() where T : notnull
        {
            if (m_postSetupContext == null)
            {
                throw new InvalidOperationException(
                    "Application services are unavailable for a directly created Robotics build context.");
            }
            return m_postSetupContext.GetRequiredService<T>();
        }

        public void Seal()
        {
            lock (m_stateLock)
            {
                if (m_sealed)
                {
                    return;
                }
                if (m_activeBuildLeaseCount != 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The Robotics build context cannot be sealed while a build is active.");
                }

                m_sealed = true;
                m_nodes.Seal();
            }
        }

        IDisposable IRoboticsBuildCoordinator.AcquireBuildLease()
        {
            lock (m_stateLock)
            {
                if (m_sealed)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The Robotics build context is sealed.");
                }
                m_activeBuildLeaseCount++;
            }
            return new BuildLease(this);
        }

        void IRoboticsBuildCoordinator.ReleaseNodeIdReservation(NodeState node)
        {
            if (m_releasesNodeIdReservations)
            {
                m_managerCoordinator.ReleaseNodeId(node);
            }
        }

        IDisposable IRoboticsBuildCoordinator.ReserveRootBrowseName(
            NodeState parent,
            QualifiedName browseName)
        {
            return m_managerCoordinator.ReserveRootBrowseName(
                Context,
                parent,
                browseName);
        }

        bool IRoboticsBuildCoordinator.IsSealed => IsSealed;

        bool IRoboticsBuildCoordinator.ReleasesNodeIdReservations =>
            m_releasesNodeIdReservations;

        private void ReleaseBuildLease()
        {
            lock (m_stateLock)
            {
                if (m_activeBuildLeaseCount == 0)
                {
                    throw new InvalidOperationException(
                        "The Robotics build context has no active build lease to release.");
                }
                m_activeBuildLeaseCount--;
            }
        }

        private readonly NodeManagerBuilder m_nodes;
        private readonly IDiPostSetupContext? m_postSetupContext;
        private readonly RoboticsBuildCoordinator m_managerCoordinator;
        private readonly Lock m_stateLock = new();
        private readonly bool m_releasesNodeIdReservations;
        private int m_activeBuildLeaseCount;
        private bool m_sealed;

        private sealed class BuildLease : IDisposable
        {
            public BuildLease(RoboticsBuildContext context)
            {
                m_context = context;
            }

            public void Dispose()
            {
                RoboticsBuildContext? context = Interlocked.Exchange(ref m_context, null);
                context?.ReleaseBuildLease();
            }

            private RoboticsBuildContext? m_context;
        }
    }
}
