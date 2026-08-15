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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.RobotIntent;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.Server;
using Robotics.IntentEnabledRobot.Kinematics;
using Robotics.IntentEnabledRobot.Simulation;
using ThreeDCartesianCoordinates = Opc.Ua.ThreeDCartesianCoordinates;
using ThreeDFrame = Opc.Ua.ThreeDFrame;
using ThreeDOrientation = Opc.Ua.ThreeDOrientation;

namespace Vision.BinPickingCell
{
    /// <summary>
    /// Robot Intent side of the bin-picking cell. Reuses the simulated arm
    /// executor from the <c>IntentEnabledRobot</c> sample and exposes the
    /// controller with the frame identifiers from the OPC UA Robotics-Vision
    /// Addendum (§4 worked example) so the Vision node manager can name its
    /// coordinate frames the same way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The frame names <c>world</c>, <c>robot_base</c>, <c>flange</c> and
    /// <c>gripper_tcp</c> match the addendum's frame tree and the
    /// <c>FrameId</c> values used by the vision-side calibrations. Publishing
    /// the same names from both node managers lets a client cross-reference
    /// the intent side ("go to <c>gripper_tcp</c>") with the vision side
    /// ("<c>HandEye</c> calibrates <c>camera_eih</c> to <c>flange</c>")
    /// without any translation step.
    /// </para>
    /// <para>
    /// The controller carries only the two locations the demo actually uses
    /// (<c>Bin</c> and <c>Fixture</c>); the extra "Inspect" and "Handoff"
    /// stops from <c>IntentEnabledRobot</c> are omitted here because the
    /// bin-picking demo picks from the bin and places on the fixture and
    /// nothing else.
    /// </para>
    /// </remarks>
    internal sealed partial class BinPickingRobotCell : IDisposable
    {
        public BinPickingRobotCell(
            ILogger<BinPickingRobotCell> logger,
            SimulatedArmExecutor executor,
            BinPickingWorldState worldState)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_executor = executor ?? throw new ArgumentNullException(nameof(executor));
            m_worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            m_executor.SnapshotChanged += OnSnapshotChanged;
            m_executor.ResolveLocationPosition = TryResolveLocationPosition;
        }

        internal ServerSystemContext SystemContext => m_systemContext ??
            throw new InvalidOperationException(
                "BinPickingRobotCell has not been attached to a Robot Intent build context.");

        internal AsyncCustomNodeManager Manager => m_manager ??
            throw new InvalidOperationException(
                "BinPickingRobotCell has not been attached to a Robot Intent build context.");

        internal IIntentControllerBuilder Controller => m_controller ??
            throw new InvalidOperationException(
                "The bin-picking intent controller has not been materialised.");

        internal IEnumerable<global::Opc.Ua.RobotIntent.AxisState> Axes => m_axes;

        internal ushort InstanceNamespaceIndex => m_instanceNamespaceIndex;

        internal IReadOnlyDictionary<string, NodeId> LocationNodes => m_locationNodes;

        /// <summary>
        /// Configures the Robot Intent controller and OpenUSD nodes for the cell.
        /// </summary>
        public async ValueTask ConfigureAsync(
            IRobotIntentBuildContext context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            m_manager = context.Manager;
            m_systemContext = context.Manager.SystemContext;
            m_instanceNamespaceIndex = context.InstanceNamespaceIndex;
            await MaterialiseOpenUsdFacilityAsync(cancellationToken).ConfigureAwait(false);
            m_controller = await context.AddIntentControllerAsync(
                "BinPickingController",
                ConfigureController,
                cancellationToken).ConfigureAwait(false);
            await MaterialiseRepresentationsAsync(cancellationToken).ConfigureAwait(false);
            PublishSnapshot(m_executor.CurrentSnapshot);
            ArrayOf<string> facets = m_controller.ComputeFacets();
            m_logger.RobotCellReady(m_axes.Count, m_locations.Count, facets);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_executor.SnapshotChanged -= OnSnapshotChanged;
        }

        private void ConfigureController(IIntentControllerBuilder controller)
        {
            controller
                .WithOperationalMode(OperationalModeEnum.AutomaticExternal)
                .WithReady(true)
                .WithMaxQueueDepth(16)
                .Accepts<JointMoveIntentDataType>(cancelSupported: true)
                .Accepts<LinearMoveIntentDataType>(cancelSupported: true, pauseSupported: true)
                .Accepts<PickIntentDataType>(cancelSupported: false)
                .Accepts<PlaceIntentDataType>(cancelSupported: true)
                .Accepts<GraspIntentDataType>(cancelSupported: false)
                .Accepts<ReleaseIntentDataType>(cancelSupported: true)
                .Accepts<WaitIntentDataType>(cancelSupported: true, pauseSupported: true);

            controller.State.Capabilities!.MissionsSupported!.Value = true;
            controller.State.Capabilities.BlendingSupported!.Value = false;
            controller.State.Capabilities.ForceControlSupported!.Value = false;
            controller.State.Capabilities.MaxTrajectoryPoints!.Value = 64u;

            IIntentFrameBuilder world = controller.AddFrame(
                "World",
                WorldFrameId,
                FrameRoleEnum.World,
                Pose(WorldFrameId, 0.0, 0.0, 0.0));
            IIntentFrameBuilder @base = controller.AddFrame(
                "RobotBase",
                RobotBaseFrameId,
                FrameRoleEnum.Base,
                Pose(WorldFrameId, 0.0, 0.0, 0.829),
                frame => frame.WithParent(world));
            IIntentFrameBuilder flange = controller.AddFrame(
                "Flange",
                FlangeFrameId,
                FrameRoleEnum.MechanicalInterface,
                Pose(RobotBaseFrameId, 0.0, 0.0, 0.1625),
                frame => frame.WithParent(@base));
            IIntentFrameBuilder tool = controller.AddFrame(
                "GripperTcp",
                ToolFrameId,
                FrameRoleEnum.Tool,
                Pose(FlangeFrameId, 0.0, 0.0, 0.115),
                frame => frame.WithParent(flange));
            controller.AddTool("ParallelGripper", tool, fitted: true);

            for (uint index = 0; index < s_axes.Length; index++)
            {
                IIntentAxisBuilder axis = controller.AddAxis(s_axes[index], index, AxisKindEnum.Revolute);

                // Degrees, to match Position: PublishSnapshot converts the simulator's
                // radians before publishing, so limits expressed in radians would tell a
                // client the axis spans +/-6.28 while it reports values up to +/-360.
                double limit = index == 2 ? HalfTurnDegrees : FullTurnDegrees;
                ConfigureAxis(axis.State, -limit, limit);
                m_axes.Add(axis.State);
            }

            // Publish where the arm actually is. Leaving Position unset reports 0 for every
            // axis, which this arm is never in: at all-zeros the elbow and forearm hang
            // straight down through the bench. A client - and the OpenUSD live binding that
            // renders from these very nodes - would faithfully show a pose the robot never
            // held, so seed them from the simulator's own starting configuration.
            PublishSnapshot(m_executor.CurrentSnapshot);

            foreach ((string name, double x, double y, double z, double rz) in s_locations)
            {
                uint capacity = string.Equals(name, "Bin", StringComparison.Ordinal) ? PayloadSlotCount : 1u;
                bool occupied = string.Equals(name, "Bin", StringComparison.Ordinal);
                IIntentLocationBuilder location = controller.AddLocation(
                    name,
                    Pose(WorldFrameId, x, y, z, rz),
                    builder => builder.WithOccupancy(occupied, capacity));
                m_locations.Add(location.State);
                m_locationNodes[name] = location.State.NodeId;
            }

            controller.WithDescription(description => description
                .WithKinematicChain(CreateKinematicChain())
                .WithLimits(
                    SimulatedArmKinematics.Reach,
                    payloadLimit: 2.0,
                    maxCartesianSpeed: 0.25,
                    maxCartesianAcceleration: 0.7));
        }

        private void ConfigureAxis(global::Opc.Ua.RobotIntent.AxisState axis, double min, double max)
        {
            axis.CreateOrReplaceMinPosition(SystemContext, null!).Value = min;
            axis.CreateOrReplaceMaxPosition(SystemContext, null!).Value = max;
            axis.CreateOrReplaceMaxSpeed(SystemContext, null!).Value = MaxAxisSpeedDegreesPerSecond;
        }

        private ArrayOf<KinematicJointDataType> CreateKinematicChain()
        {
            var joints = new KinematicJointDataType[s_axes.Length];
            for (int ii = 0; ii < joints.Length; ii++)
            {
                joints[ii] = new KinematicJointDataType
                {
                    AxisId = s_axes[ii],
                    Kind = AxisKindEnum.Revolute,
                    OriginTransform = Pose(
                        ii == 0 ? RobotBaseFrameId : s_axes[ii - 1], 0.0, 0.0, ii == 0 ? 0.1625 : 0.12),
                    AxisVector = (ii is 0 or 4 ? s_axisZ : s_axisY).ToArrayOf()
                };
            }
            return joints.ToArrayOf();
        }

        private void OnSnapshotChanged(object? sender, SimulatedArmSnapshot snapshot)
        {
            PublishSnapshot(snapshot);
        }

        /// <summary>
        /// Resolves one of this cell's Locations to an approach position in the arm's base
        /// frame, so a Pick or a Place travels to the bin or the fixture instead of
        /// actuating the gripper where it stands.
        /// </summary>
        /// <remarks>
        /// The Locations are authored in the world frame and the kinematics work in the
        /// robot base frame, so the base origin is subtracted. The approach height lifts
        /// the target off the bench: a target on the surface asks the solver for a pose
        /// with the tool exactly at table height, which is both harder to reach and not
        /// what an approach looks like.
        /// </remarks>
        private bool TryResolveLocationPosition(NodeId location, out ArrayOf<double> position)
        {
            foreach ((string name, double x, double y, double z, double _) in s_locations)
            {
                if (m_locationNodes.TryGetValue(name, out NodeId nodeId) && nodeId == location)
                {
                    position = new[] { x, y, (z - RobotBaseHeightMetres) + ApproachHeightMetres }.ToArrayOf();
                    return true;
                }
            }
            position = ArrayOf<double>.Empty;
            return false;
        }

        /// <summary>
        /// Moves the carried part in the cell's world model so the world changes when the
        /// robot changes it, rather than only when a proof service says so.
        /// </summary>
        /// <remarks>
        /// The arm reports the carried position in its own base frame; the world model and
        /// the ground-truth detector work in the world frame, so the base height is added
        /// back. Running on every snapshot is what makes the part travel with the tool
        /// instead of teleporting when the grasp opens.
        /// </remarks>
        private void TrackHeldPart(SimulatedArmSnapshot snapshot)
        {
            ReadOnlySpan<double> carried = snapshot.HeldPartPosition.Span;
            if (carried.Length < 3)
            {
                return;
            }
            double worldX = carried[0];
            double worldY = carried[1];
            double worldZ = carried[2] + RobotBaseHeightMetres;

            if (snapshot.HasObject && snapshot.HeldObjectClass.Length > 0)
            {
                _ = m_worldState.MarkHeld(snapshot.HeldObjectClass, worldX, worldY, worldZ);
                m_carriedClass = snapshot.HeldObjectClass;
                return;
            }
            if (!snapshot.HasObject && m_carriedClass.Length > 0)
            {
                // The gripper opened: the part stays where it was let go, which is what a
                // detector re-running after the Place should now find.
                _ = m_worldState.MarkPlaced(m_carriedClass, worldX, worldY, worldZ);
                m_carriedClass = string.Empty;
            }
        }

        private void PublishSnapshot(SimulatedArmSnapshot snapshot)
        {
            TrackHeldPart(snapshot);
            if (m_systemContext == null)
            {
                return;
            }
            for (int ii = 0; ii < m_axes.Count && ii < snapshot.JointAngles.Count; ii++)
            {
                global::Opc.Ua.RobotIntent.AxisState axis = m_axes[ii];
                if (axis.Position != null)
                {
                    axis.Position.Value = snapshot.JointAngles[ii] * 180.0 / Math.PI;
                    axis.Position.ClearChangeMasks(m_systemContext, true);
                }
            }
        }

        private static Pose3DDataType Pose(string frameId, double x, double y, double z, double rzDegrees = 0.0)
        {
            return PoseMath.FromThreeDFrame(
                new ThreeDFrame
                {
                    CartesianCoordinates = new ThreeDCartesianCoordinates
                    {
                        X = x,
                        Y = y,
                        Z = z
                    },
                    Orientation = new ThreeDOrientation
                    {
                        C = rzDegrees * Math.PI / 180.0
                    }
                },
                frameId);
        }

        internal const string WorldFrameId = "world";
        internal const string RobotBaseFrameId = "robot_base";
        internal const string FlangeFrameId = "flange";
        internal const string ToolFrameId = "gripper_tcp";
        internal const string CameraFrameId = "camera_eih";
        private const double FullTurnDegrees = 360.0;
        private const double HalfTurnDegrees = 180.0;

        // The robot base sits on the bench at this world height; the Locations are authored
        // in the world frame and the kinematics work relative to the base, so this is the
        // offset between the two. It matches the RobotBase frame the Vision model publishes.
        private const double RobotBaseHeightMetres = 0.829;

        // How far above a Location the tool travels to. Far enough to read as an approach
        // rather than a collision, and it keeps the target clear of the bench surface.
        private const double ApproachHeightMetres = 0.20;

        // The simulator's DefaultJointSpeed is 0.9 rad/s; Position and the limits are
        // published in degrees, so the speed limit is too.
        private const double MaxAxisSpeedDegreesPerSecond = 0.9 * 180.0 / Math.PI;
        private const uint PayloadSlotCount = 8u;

        private static readonly (string Name, double X, double Y, double Z, double Rz)[] s_locations =
        [
            ("Bin", 0.41, -0.28, 0.829, 0.0),
            ("Fixture", 0.48, 0.26, 0.829, 25.0)
        ];

        private static readonly string[] s_axes = ["J1", "J2", "J3", "J4", "J5", "J6"];
        private static readonly double[] s_axisZ = [0.0, 0.0, 1.0];
        private static readonly double[] s_axisY = [0.0, 1.0, 0.0];

        private readonly ILogger<BinPickingRobotCell> m_logger;
        private readonly SimulatedArmExecutor m_executor;
        private readonly BinPickingWorldState m_worldState;
        private string m_carriedClass = string.Empty;
        private readonly List<global::Opc.Ua.RobotIntent.AxisState> m_axes = [];
        private readonly List<global::Opc.Ua.RobotIntent.LocationState> m_locations = [];
        private readonly Dictionary<string, NodeId> m_locationNodes = new(StringComparer.Ordinal);
        private AsyncCustomNodeManager? m_manager;
        private IIntentControllerBuilder? m_controller;
        private ServerSystemContext? m_systemContext;
        private ushort m_instanceNamespaceIndex;
    }

    internal static partial class BinPickingRobotCellLog
    {
        [LoggerMessage(EventId = BinPickingCellEventIds.Configurator + 1,
            Level = LogLevel.Information,
            Message = "Robot Intent side of BinPickingCell ready " +
                "({AxisCount} axes, {LocationCount} locations, facets {Facets}).")]
        public static partial void RobotCellReady(
            this ILogger<BinPickingRobotCell> logger,
            int axisCount, int locationCount, ArrayOf<string> facets);
    }
}
