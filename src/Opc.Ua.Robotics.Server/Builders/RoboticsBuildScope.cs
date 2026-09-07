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

namespace Opc.Ua.Robotics.Server.Builders
{
    internal enum RoboticsSemanticReference
    {
        Controls,
        HasSafetyStates,
        Moves,
        Requires,
        HasSlave,
        IsDrivenBy,
        IsConnectedTo
    }

    internal sealed class RoboticsBuildScope
    {
        private readonly List<RoboticsNodeBuilder> m_builders = [];
        private readonly IRoboticsBuildCoordinator m_buildCoordinator;
        private readonly List<SemanticReference> m_semanticReferences = [];
        private IDisposable? m_rootBrowseNameReservation;
        private bool m_registering;

        public RoboticsBuildScope(
            IRoboticsBuildContext buildContext,
            QualifiedName browseName)
        {
            BuildContext = buildContext ??
                throw new ArgumentNullException(nameof(buildContext));
            m_buildCoordinator = GetBuildCoordinator(buildContext);
            EnsureContextMutable(buildContext);
            Context = buildContext.Context;

            m_rootBrowseNameReservation = m_buildCoordinator.ReserveRootBrowseName(
                BuildContext.DeviceSet,
                browseName);
            try
            {
                Root = Context.CreateInstanceOfMotionDeviceSystemType(
                    BuildContext.DeviceSet,
                    browseName);
                Root.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
                RootBuilder = new MotionDeviceSystemBuilder(this, Root);
            }
            catch
            {
                ReleaseRootBrowseNameReservation();
                throw;
            }
        }

        internal IRoboticsBuildContext BuildContext { get; }

        internal ISystemContext Context { get; }

        internal MotionDeviceSystemState Root { get; }

        internal MotionDeviceSystemBuilder RootBuilder { get; }

        internal List<ControllerBuilder> Controllers { get; } = [];

        internal List<MotionDeviceBuilder> MotionDevices { get; } = [];

        internal List<SafetyStateBuilder> SafetyStates { get; } = [];

        internal List<AxisBuilder> Axes { get; } = [];

        internal List<PowerTrainBuilder> PowerTrains { get; } = [];

        internal List<GearBuilder> Gears { get; } = [];

        internal List<EmergencyStopBuilder> EmergencyStops { get; } = [];

        internal List<ProtectiveStopBuilder> ProtectiveStops { get; } = [];

        internal List<TaskModuleBuilder> TaskModules { get; } = [];

        /// <summary>
        /// Asynchronous work that must run after the completed tree has been
        /// registered with the node manager, for example binding a Controller
        /// Programs directory to a file-system provider.
        /// </summary>
        internal List<Func<CancellationToken, ValueTask>> PostRegistrationActions { get; } = [];

        /// <summary>
        /// Resources created during registration that must be released when the
        /// build is rolled back.
        /// </summary>
        internal List<IAsyncDisposable> RegisteredResources { get; } = [];

        internal bool IsRegistered { get; private set; }

        internal void Abort()
        {
            ReleaseReservations();
        }

        internal static void EnsureContextMutable(IRoboticsBuildContext buildContext)
        {
            IRoboticsBuildCoordinator coordinator = GetBuildCoordinator(buildContext);
            if (coordinator.IsSealed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The Robotics build context is sealed.");
            }
        }

        internal static IDisposable AcquireBuildLease(IRoboticsBuildContext buildContext)
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
                    "MotionDeviceSystem '{0}' has already started registration.",
                    Root.BrowseName);
            }
        }

        internal void RegisterBuilder(RoboticsNodeBuilder builder)
        {
            m_builders.Add(builder);
        }

        internal void AddSemanticReference(
            RoboticsSemanticReference reference,
            RoboticsNodeBuilder source,
            RoboticsNodeBuilder target)
        {
            EnsureMutable();
            EnsureSameScope(source);
            EnsureSameScope(target);

            for (int ii = 0; ii < m_semanticReferences.Count; ii++)
            {
                SemanticReference candidate = m_semanticReferences[ii];
                if (candidate.Reference == reference &&
                    ReferenceEquals(candidate.Source, source) &&
                    ReferenceEquals(candidate.Target, target))
                {
                    return;
                }
            }
            m_semanticReferences.Add(new SemanticReference(reference, source, target));
        }

        internal void AddTaskControlRelation(
            TaskControlBuilder taskControl,
            MotionDeviceBuilder motionDevice)
        {
            EnsureMutable();
            EnsureSameScope(taskControl);
            EnsureSameScope(motionDevice);

            AddSemanticReference(
                RoboticsSemanticReference.Controls,
                taskControl,
                motionDevice);
        }

        internal async ValueTask RegisterAsync(CancellationToken cancellationToken)
        {
            EnsureMutable();
            m_registering = true;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Validate();

                VerifyNodeIds();
                ApplySemanticReferences();

                BuildContext.DeviceSet.AddChild(Root);
                await BuildContext.Manager
                    .AddPredefinedNodeAsync(Root, cancellationToken)
                    .ConfigureAwait(false);

                CacheNodeBuilders();

                for (int ii = 0; ii < PostRegistrationActions.Count; ii++)
                {
                    await PostRegistrationActions[ii](cancellationToken).ConfigureAwait(false);
                }

                IsRegistered = true;
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

        private void Validate()
        {
            var errors = new List<string>();
            if (Controllers.Count == 0)
            {
                errors.Add("The motion-device system must contain at least one controller.");
            }
            if (MotionDevices.Count == 0)
            {
                errors.Add("The motion-device system must contain at least one motion device.");
            }
            if (SafetyStates.Count == 0)
            {
                errors.Add("The motion-device system must contain at least one safety state.");
            }

            for (int ii = 0; ii < Controllers.Count; ii++)
            {
                ControllerBuilder controller = Controllers[ii];
                if (controller.Software.Count == 0)
                {
                    errors.Add(
                        $"Controller '{NameOf(controller)}' must contain at least one software instance.");
                }
                if (controller.TaskControls.Count == 0)
                {
                    errors.Add(
                        $"Controller '{NameOf(controller)}' must contain at least one task control.");
                }
                if (controller.State.CurrentUser == null)
                {
                    errors.Add(
                        $"Controller '{NameOf(controller)}' must define mandatory CurrentUser.");
                }
            }

            for (int ii = 0; ii < MotionDevices.Count; ii++)
            {
                MotionDeviceBuilder motionDevice = MotionDevices[ii];
                if (motionDevice.Axes.Count == 0)
                {
                    errors.Add(
                        $"Motion device '{NameOf(motionDevice)}' must contain at least one axis.");
                }
                if (motionDevice.PowerTrains.Count == 0)
                {
                    errors.Add(
                        $"Motion device '{NameOf(motionDevice)}' must contain at least one power train.");
                }
            }

            for (int ii = 0; ii < Axes.Count; ii++)
            {
                AxisBuilder axis = Axes[ii];
                if (!axis.IsVirtual && axis.RequiredPowerTrains.Count == 0)
                {
                    errors.Add(
                        $"Non-virtual axis '{NameOf(axis)}' must Requires at least one power train.");
                }
            }

            for (int ii = 0; ii < PowerTrains.Count; ii++)
            {
                PowerTrainBuilder powerTrain = PowerTrains[ii];
                if (powerTrain.Motors.Count == 0)
                {
                    errors.Add(
                        $"Power train '{NameOf(powerTrain)}' must contain at least one motor.");
                }
            }

            for (int ii = 0; ii < Gears.Count; ii++)
            {
                if (!Gears[ii].HasGearRatio)
                {
                    errors.Add(
                        $"Gear '{NameOf(Gears[ii])}' must define a non-zero gear ratio denominator.");
                }
            }

            for (int ii = 0; ii < EmergencyStops.Count; ii++)
            {
                if (!EmergencyStops[ii].HasName)
                {
                    errors.Add(
                        $"Emergency stop '{NameOf(EmergencyStops[ii])}' must define Name.");
                }
            }

            for (int ii = 0; ii < ProtectiveStops.Count; ii++)
            {
                if (!ProtectiveStops[ii].HasName)
                {
                    errors.Add(
                        $"Protective stop '{NameOf(ProtectiveStops[ii])}' must define Name.");
                }
            }

            for (int ii = 0; ii < TaskModules.Count; ii++)
            {
                if (!TaskModules[ii].HasName)
                {
                    errors.Add(
                        $"Task module '{NameOf(TaskModules[ii])}' must define Name.");
                }
            }

            if (errors.Count > 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "MotionDeviceSystem '{0}' is invalid: {1}",
                    Root.BrowseName,
                    string.Join(" ", errors));
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
                        "Robotics descendant '{0}' has a null NodeId after instance assignment.",
                        node.BrowseName);
                }
                if (node.NodeId.NamespaceIndex != BuildContext.InstanceNamespaceIndex)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Robotics descendant '{0}' has NodeId '{1}' outside the instance namespace.",
                        node.BrowseName,
                        node.NodeId);
                }
                if (!nodeIds.Add(node.NodeId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Robotics descendant '{0}' has duplicate NodeId '{1}'.",
                        node.BrowseName,
                        node.NodeId);
                }
                NodeState? indexedNode =
                    BuildContext.Manager.FindPredefinedNode(node.NodeId);
                if (indexedNode != null && !ReferenceEquals(indexedNode, node))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Robotics descendant '{0}' has NodeId '{1}', which is already indexed " +
                        "by node '{2}'.",
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

        private static IRoboticsBuildCoordinator GetBuildCoordinator(
            IRoboticsBuildContext buildContext)
        {
            return buildContext as IRoboticsBuildCoordinator ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Robotics build context does not provide build coordination.");
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
            if (!m_buildCoordinator.ReleasesNodeIdReservations)
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
                    // A resource that fails to release must not mask the original
                    // build failure that triggered the rollback.
                }
            }
            RegisteredResources.Clear();

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
                BuildContext.DeviceSet.RemoveChild(Root);
            }
        }

        private void ApplySemanticReferences()
        {
            for (int ii = 0; ii < m_semanticReferences.Count; ii++)
            {
                SemanticReference relation = m_semanticReferences[ii];
                NodeId referenceTypeId = ResolveReferenceType(relation.Reference);
                NodeState source = relation.Source.UntypedState;
                NodeState target = relation.Target.UntypedState;

                if (!source.ReferenceExists(referenceTypeId, false, target.NodeId))
                {
                    source.AddReference(referenceTypeId, false, target.NodeId);
                }
                if (!target.ReferenceExists(referenceTypeId, true, source.NodeId))
                {
                    target.AddReference(referenceTypeId, true, source.NodeId);
                }

                if (relation.Reference == RoboticsSemanticReference.Controls &&
                    relation.Source is TaskControlBuilder taskControl &&
                    relation.Target is MotionDeviceBuilder motionDevice &&
                    taskControl.TaskControlOperation != null)
                {
                    motionDevice.SetTaskControlReference(taskControl.TaskControlOperation.State);
                }
            }
        }

        private NodeId ResolveReferenceType(RoboticsSemanticReference reference)
        {
            uint identifier = reference switch
            {
                RoboticsSemanticReference.Controls => ReferenceTypes.Controls,
                RoboticsSemanticReference.HasSafetyStates => ReferenceTypes.HasSafetyStates,
                RoboticsSemanticReference.Moves => ReferenceTypes.Moves,
                RoboticsSemanticReference.Requires => ReferenceTypes.Requires,
                RoboticsSemanticReference.HasSlave => ReferenceTypes.HasSlave,
                RoboticsSemanticReference.IsDrivenBy => ReferenceTypes.IsDrivenBy,
                RoboticsSemanticReference.IsConnectedTo => ReferenceTypes.IsConnectedTo,
                _ => throw new ArgumentOutOfRangeException(nameof(reference))
            };
            return NodeId.Create(
                identifier,
                Namespaces.Robotics,
                Context.NamespaceUris);
        }

        private void EnsureSameScope(RoboticsNodeBuilder builder)
        {
            if (!ReferenceEquals(builder.Scope, this))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Robotics relationship endpoints must belong to the same build scope.");
            }
        }

        private static string NameOf(RoboticsNodeBuilder builder)
        {
            return builder.UntypedState.BrowseName.Name ?? "<unnamed>";
        }

        private sealed class SemanticReference
        {
            public SemanticReference(
                RoboticsSemanticReference reference,
                RoboticsNodeBuilder source,
                RoboticsNodeBuilder target)
            {
                Reference = reference;
                Source = source;
                Target = target;
            }

            public RoboticsSemanticReference Reference { get; }

            public RoboticsNodeBuilder Source { get; }

            public RoboticsNodeBuilder Target { get; }
        }

    }
}
