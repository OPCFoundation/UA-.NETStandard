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
            SimulatedArmKinematics kinematics,
            BinPickingWorldState worldState)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_executor = executor ?? throw new ArgumentNullException(nameof(executor));
            m_kinematics = kinematics ?? throw new ArgumentNullException(nameof(kinematics));
            m_worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            m_toolDownOrientation = executor.CurrentSnapshot.ToolPose.Orientation;
            m_executor.SnapshotChanged += OnSnapshotChanged;
            m_executor.ResolveLocationPosition = TryResolveLocationPosition;
            m_executor.ResolveLocationPose = TryResolveLocationPose;
            m_executor.PreferCartesianDescent = PreferCartesianDescent;
            m_executor.Diagnostic = message => m_logger.ArmTravel(message);
            UpdateMovingObstacles();
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
            await MaterialisePartStateAsync(cancellationToken).ConfigureAwait(false);
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
                Pose(WorldFrameId, 0.0, 0.0, RobotBaseHeightMetres),
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

            IIntentOutputSignalBuilder gripperLeft = controller.AddOutput(
                "GripperLeftSlide",
                Opc.Ua.DataTypeIds.ThreeDCartesianCoordinates,
                ToVariant(GripperSlide(m_executor.CurrentSnapshot.GripperOpening, 1.0)));
            IIntentOutputSignalBuilder gripperRight = controller.AddOutput(
                "GripperRightSlide",
                Opc.Ua.DataTypeIds.ThreeDCartesianCoordinates,
                ToVariant(GripperSlide(m_executor.CurrentSnapshot.GripperOpening, -1.0)));
            m_gripperLeftSlideValue = gripperLeft.State.Value;
            m_gripperRightSlideValue = gripperRight.State.Value;

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
                bool isBin = string.Equals(name, BinLocationName, StringComparison.Ordinal);

                // A home slot starts occupied because its part starts there, and the bin
                // starts occupied because all of them do. Occupied is kept true to the
                // world from here on by UpdateLocationOccupancy: a slot that stayed
                // "occupied" after the robot emptied it would be a node reporting
                // something the cell can see is no longer the case.
                uint capacity = isBin ? PayloadSlotCount : 1u;
                bool occupied = isBin || name.StartsWith(HomeLocationPrefix, StringComparison.Ordinal);
                IIntentLocationBuilder location = controller.AddLocation(
                    name,
                    Pose(WorldFrameId, x, y, z, rz),
                    builder => builder.WithOccupancy(occupied, capacity));
                m_locations.Add(location.State);
                m_locationNodes[name] = location.State.NodeId;
                m_locationStates[name] = location.State;
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
                    // Carrying something means this is the move before a Place, because a
                    // Pick travels with the gripper empty and closes on arrival while a
                    // Place travels holding the part and opens. So the tool can descend to
                    // just above the height that leaves the part on its support. The small
                    // clearance keeps the wrist and jaws out of an accumulated stack; the
                    // support model settles the released part the remaining 17 mm instead
                    // of leaving it floating or driving the tool into what is already there.
                    double toolWorldZ = m_carriedClass.Length > 0
                        ? RestingCentreHeight(m_carriedClass, x, y)
                            + SimulatedArmExecutor.HeldPartTcpOffset
                            + PlaceReleaseClearanceMetres
                        : z + ApproachHeightMetres;
                    position = new[] { x, y, toolWorldZ - RobotBaseHeightMetres }.ToArrayOf();
                    return true;
                }
            }
            position = ArrayOf<double>.Empty;
            return false;
        }

        /// <summary>
        /// Resolves a Location to a deliberate tool-down pose rather than inheriting the
        /// yaw left behind by the previous grasp.
        /// </summary>
        private bool TryResolveLocationPose(NodeId location, out Pose3DDataType pose)
        {
            if (TryResolveLocationPosition(location, out ArrayOf<double> position))
            {
                foreach ((string name, double _, double _, double _, double _) in s_locations)
                {
                    if (m_locationNodes.TryGetValue(name, out NodeId nodeId) && nodeId == location)
                    {
                        pose = new Pose3DDataType
                        {
                            FrameId = RobotBaseFrameId,
                            Position = position,
                            // The Location's Rz describes how a part or fixture is authored,
                            // not a mandatory wrist yaw. Applying Fixture's 25 degrees here
                            // made the first place succeed and left no retract path for the
                            // next pick. Hold one solved tool-down orientation everywhere;
                            // the executor's deterministic yaw search still turns it when
                            // the standard pose itself has no clear solution.
                            Orientation = m_toolDownOrientation
                        };
                        return true;
                    }
                }
            }
            pose = new Pose3DDataType();
            return false;
        }

        /// <summary>
        /// Gets whether a Location is inside the open bin, where the final approach should
        /// be vertical rather than a joint interpolation that can sweep through a wall.
        /// </summary>
        private bool PreferCartesianDescent(NodeId location)
        {
            foreach ((string name, NodeId nodeId) in m_locationNodes)
            {
                if (nodeId == location)
                {
                    // Empty-gripper picks are made Cartesian by the executor regardless of
                    // this value. Loaded placements into a home slot are Cartesian too, and
                    // the executor records every joint sample so the next command can replay
                    // the exact approach in reverse. The fixture keeps the short local joint
                    // approach because its final Cartesian branch is less reliable.
                    return string.Equals(name, BinLocationName, StringComparison.Ordinal)
                        || name.StartsWith(HomeLocationPrefix, StringComparison.Ordinal);
                }
            }
            return false;
        }

        /// <summary>
        /// Gets whether the named part is lying under the tool, which is the only place a
        /// grasp can pick it up from.
        /// </summary>
        /// <remarks>
        /// A Pick names an object class and a source Location. Without this check the cell
        /// hands over whatever class the intent names no matter where that part actually
        /// is, so picking from an empty bin still produces a part in the gripper - and a
        /// loop that keeps picking and placing walks every part onto one spot and stacks
        /// them into the air, each Place resting the part on the pile the last one left.
        /// The test is against the tool's own position rather than the Location the intent
        /// named: the tool has already travelled there by the time it closes, and a field
        /// remembering the last resolved Location goes stale as soon as intents queue
        /// back to back - which made every grasp fail and the robot stop moving at all.
        /// </remarks>
        private bool CanGrasp(string classLabel, double toolX, double toolY)
        {
            IReadOnlyList<BinPickingPartSnapshot> parts = m_worldState.Snapshot();
            for (int ii = 0; ii < parts.Count; ii++)
            {
                BinPickingPartSnapshot part = parts[ii];
                if (!string.Equals(part.Part.ClassLabel, classLabel, StringComparison.Ordinal))
                {
                    continue;
                }
                bool within = Math.Abs(part.WorldX - toolX) <= GraspReachRadiusMetres
                    && Math.Abs(part.WorldY - toolY) <= GraspReachRadiusMetres;
                if (!within)
                {
                    m_logger.GraspFoundNothing(classLabel, FormatPosition(toolX, toolY));
                }
                return within;
            }
            return false;
        }

        /// <summary>
        /// Formats a position for a log message.
        /// </summary>
        private static string FormatPosition(double x, double y)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture, $"({x:F3}, {y:F3})");
        }

        /// <summary>
        /// Gets the height a part's centre comes to rest at over a spot on the bench, given
        /// everything else that is standing there.
        /// </summary>
        /// <remarks>
        /// This is what makes a second part placed on the same spot end up on top of the
        /// first rather than inside it, and it is why a placed part stops floating: the
        /// part used to be left wherever the tool centre point was, which for a Place at the
        /// approach height was 165 mm above the bench.
        /// </remarks>
        private double RestingCentreHeight(string classLabel, double x, double y)
        {
            BinPickingPart? part = BinPickingPartsCatalog.TryGet(classLabel);
            if (part == null)
            {
                return BenchTopMetres;
            }
            return m_support.RestingCentreHeight(
                x, y, part.Size[0], part.Size[1], part.Size[2], SupportingParts(classLabel));
        }

        /// <summary>
        /// Gets the other parts as solids that can hold something up, leaving out the one
        /// being placed and anything currently in the gripper.
        /// </summary>
        private ArrayOf<SimulatedSupportSolid> SupportingParts(string exclude)
        {
            IReadOnlyList<BinPickingPartSnapshot> parts = m_worldState.Snapshot();
            var solids = new List<SimulatedSupportSolid>(parts.Count);
            for (int ii = 0; ii < parts.Count; ii++)
            {
                BinPickingPartSnapshot snapshot = parts[ii];
                if (string.Equals(snapshot.Part.ClassLabel, exclude, StringComparison.Ordinal)
                    || snapshot.Location == BinPickingPartLocation.Held)
                {
                    continue;
                }
                solids.Add(new SimulatedSupportSolid(
                    snapshot.Part.ClassLabel,
                    snapshot.WorldX,
                    snapshot.WorldY,
                    snapshot.Part.Size[0],
                    snapshot.Part.Size[1],
                    snapshot.WorldZ + (snapshot.Part.Size[2] * 0.5)));
            }
            return ArrayOf.Create(solids.ToArray().AsSpan());
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
                if (m_carriedClass.Length == 0
                    && !CanGrasp(snapshot.HeldObjectClass, worldX, worldY))
                {
                    // The gripper closed where the part is not. Attaching it anyway is
                    // what let a Pick teleport a part out of a stack on the far side of
                    // the bench into the tool: the cell would then carry it off and set it
                    // down somewhere it was never taken from. Closing on nothing is the
                    // honest outcome, and it leaves the part where it lies.
                    return;
                }
                _ = m_worldState.MarkHeld(snapshot.HeldObjectClass, worldX, worldY, worldZ);
                m_carriedClass = snapshot.HeldObjectClass;
                PublishPartPosition(snapshot.HeldObjectClass, worldX, worldY, worldZ);
                UpdateLocationOccupancy();
                UpdateMovingObstacles();
                return;
            }
            if (!snapshot.HasObject && m_carriedClass.Length > 0)
            {
                // The gripper opened. The part settles onto whatever is under it rather
                // than staying at the tool centre point: released at the approach height it
                // would hang in the air, and released over another part it would stand
                // inside it. The tool has normally already descended to the resting height
                // by this point, so this is a few millimetres of settle, not a fall.
                double restingZ = m_support.ClampAboveSupport(
                    worldX,
                    worldY,
                    PartSize(m_carriedClass, 0),
                    PartSize(m_carriedClass, 1),
                    PartSize(m_carriedClass, 2),
                    worldZ,
                    SupportingParts(m_carriedClass));
                _ = m_worldState.MarkPlaced(m_carriedClass, worldX, worldY, restingZ);
                PublishPartPosition(m_carriedClass, worldX, worldY, restingZ);
                m_carriedClass = string.Empty;
                UpdateLocationOccupancy();
                UpdateMovingObstacles();
            }
        }

        /// <summary>
        /// Republishes moving workpieces as solids the arm has to keep out of.
        /// </summary>
        /// <remarks>
        /// The bench and the bin walls never move, so they are declared once. The fixture
        /// plate is deliberately not an arm obstacle: the tool works immediately above it,
        /// and treating the thin plate as a full-radius solid leaves valid final poses but
        /// no connected final approach. The support model still uses the plate and pegs as
        /// solids for placing parts. Whatever the gripper is carrying is left out, because
        /// a part travelling with the tool cannot be in its way.
        /// </remarks>
        private void UpdateMovingObstacles()
        {
            SimulatedCollisionModel? collisions = m_kinematics?.Collisions;
            if (collisions == null)
            {
                return;
            }
            IReadOnlyList<BinPickingPartSnapshot> parts = m_worldState.Snapshot();
            var solids = new List<SimulatedObstacleBox>(parts.Count);

            // The parts themselves are deliberately left out for now, and this is measured
            // rather than assumed: with them in, the loop got five operations into a cycle
            // before a Place onto the fixture was refused, against nine of ten with only
            // the furniture. The reason is the same wrist geometry that made the bench a
            // half-space rather than a solid - with the tool vertical this arm's J4 sits
            // about 34 mm below the tool centre point, so placing a part on top of a stack
            // puts J4 roughly a millimetre above the part underneath, inside its clearance.
            // Including them needs the wrist to stop being a fat capsule near the workpiece
            // (a per-link radius) or a longer tool; until then, declaring them would refuse
            // the stacking the cell exists to do.
            for (int ii = 0; ii < parts.Count && IncludePartsAsObstacles; ii++)
            {
                BinPickingPartSnapshot part = parts[ii];
                if (part.Location == BinPickingPartLocation.Held
                    || string.Equals(part.Part.ClassLabel, m_carriedClass, StringComparison.Ordinal))
                {
                    continue;
                }
                double halfHeight = part.Part.Size[2] * 0.5;
                solids.Add(new SimulatedObstacleBox(
                    part.Part.ClassLabel,
                    part.WorldX,
                    part.WorldY,
                    part.Part.Size[0],
                    part.Part.Size[1],
                    part.WorldZ - halfHeight - RobotBaseHeightMetres,
                    part.WorldZ + halfHeight - RobotBaseHeightMetres));
            }
            collisions.MovingObstacles = ArrayOf.Create(solids.ToArray().AsSpan());
        }

        /// <summary>
        /// Republishes each Location's Occupied flag from where the parts actually are.
        /// </summary>
        /// <remarks>
        /// Occupancy is declarative in the Robot Intent model - nothing enforces it - which
        /// makes it easy to author once and leave wrong. A client asking "is the bin empty
        /// yet" would then be told "no" forever. A Location counts as occupied when a part
        /// that is not in the gripper is standing within its slot radius.
        /// </remarks>
        private void UpdateLocationOccupancy()
        {
            if (m_systemContext == null || m_locationStates.Count == 0)
            {
                return;
            }
            IReadOnlyList<BinPickingPartSnapshot> parts = m_worldState.Snapshot();
            foreach ((string name, double x, double y, double _, double _) in s_locations)
            {
                if (!m_locationStates.TryGetValue(name, out global::Opc.Ua.RobotIntent.LocationState? state)
                    || state.Occupied == null)
                {
                    continue;
                }
                double radius = string.Equals(name, BinLocationName, StringComparison.Ordinal)
                    ? BinSlotRadiusMetres
                    : HomeSlotRadiusMetres;
                bool occupied = false;
                for (int ii = 0; ii < parts.Count && !occupied; ii++)
                {
                    BinPickingPartSnapshot part = parts[ii];
                    occupied = part.Location != BinPickingPartLocation.Held
                        && Math.Abs(part.WorldX - x) <= radius
                        && Math.Abs(part.WorldY - y) <= radius;
                }
                if (state.Occupied.Value == occupied)
                {
                    continue;
                }
                state.Occupied.Value = occupied;
                state.Occupied.ClearChangeMasks(m_systemContext, false);
            }
        }

        /// <summary>
        /// Gets one extent of a part, or zero when the catalogue does not know it.
        /// </summary>
        private static double PartSize(string classLabel, int axis)
        {
            BinPickingPart? part = BinPickingPartsCatalog.TryGet(classLabel);
            return part == null ? 0.0 : part.Size[axis];
        }

        /// <summary>
        /// Pushes a part's world position onto its variable so the OpenUSD live binding, and
        /// any other subscriber, follows it.
        /// </summary>
        /// <remarks>
        /// The value is a <see cref="ThreeDCartesianCoordinates"/> rather than a bare
        /// <c>double[3]</c> on purpose. §5.8 of the OpenUSD companion specification defines
        /// a translation source as a structured 3D coordinate, and the connector's
        /// translation profile fails closed on anything else - so a plain array left every
        /// part's translation unresolved and the parts never moved in the viewport, however
        /// faithfully the server tracked them.
        /// </remarks>
        private void PublishPartPosition(string classLabel, double worldX, double worldY, double worldZ)
        {
            if (m_systemContext == null
                || !m_partPositionNodes.TryGetValue(classLabel, out BaseDataVariableState? node))
            {
                return;
            }
            node.Value = new Variant(new ExtensionObject(new ThreeDCartesianCoordinates
            {
                X = worldX,
                Y = worldY,
                Z = worldZ
            }));
            node.ClearChangeMasks(m_systemContext, false);
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
            if (m_gripperLeftSlideValue != null)
            {
                m_gripperLeftSlideValue.Value = ToVariant(
                    GripperSlide(snapshot.GripperOpening, 1.0));
                m_gripperLeftSlideValue.ClearChangeMasks(m_systemContext, true);
            }
            if (m_gripperRightSlideValue != null)
            {
                m_gripperRightSlideValue.Value = ToVariant(
                    GripperSlide(snapshot.GripperOpening, -1.0));
                m_gripperRightSlideValue.ClearChangeMasks(m_systemContext, true);
            }
        }

        /// <summary>
        /// Converts the jaw opening into one slide's USD translation.
        /// </summary>
        private static ThreeDCartesianCoordinates GripperSlide(double opening, double direction)
        {
            double centre = Math.Clamp(opening * 0.5, GripperClosedHalfGap, GripperOpenHalfGap);
            return new ThreeDCartesianCoordinates
            {
                X = 0.0,
                Y = direction * centre,
                Z = 0.0
            };
        }

        private static Variant ToVariant(ThreeDCartesianCoordinates value)
        {
            return new Variant(new ExtensionObject(value));
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

        /// <summary>
        /// Gets the name of the Location that holds a part's own starting spot in the bin.
        /// </summary>
        internal static string HomeLocationName(string classLabel)
        {
            return HomeLocationPrefix + classLabel;
        }

        /// <summary>
        /// Builds this cell's Locations: the bin, the fixture, and one home slot per part.
        /// </summary>
        /// <remarks>
        /// A Place intent names a LocationType and carries no pose, so "the bin" is the
        /// only way back unless each part has a Location of its own - and placing every
        /// part at the bin puts them all on the one spot, which the support model then
        /// stacks in the middle of it. The home slots are the authored scattered
        /// positions, so a Place can return a part to where the cell first had it.
        /// The bin and fixture coordinates are where those two actually stand in
        /// Cell.usda; they used to be somewhere else entirely - the Bin at y = -0.28 when
        /// the bin spans +/-0.12, the Fixture at (0.48, 0.26) when the fixture stood at
        /// (-0.32, 0) - so "place it on the fixture" put the part down on bare bench a long
        /// way from the fixture, and the render disagreed with the model about where the
        /// cell's own furniture was. The Z is the surface a part stands on there.
        /// </remarks>
        private static (string Name, double X, double Y, double Z, double Rz)[] BuildLocations()
        {
            IReadOnlyList<BinPickingPart> parts = BinPickingPartsCatalog.Parts;
            var locations = new List<(string Name, double X, double Y, double Z, double Rz)>(parts.Count + 2)
            {
                (BinLocationName, BinPickingPartsCatalog.BinCentreX, BinPickingPartsCatalog.BinCentreY,
                    BenchTopMetres, 0.0),
                (FixtureLocationName, BinPickingCellGeometry.FixtureCentreX, 0.0,
                    FixturePlateTopMetres, 25.0)
            };
            for (int ii = 0; ii < parts.Count; ii++)
            {
                BinPickingPart part = parts[ii];
                locations.Add((
                    HomeLocationName(part.ClassLabel),
                    part.InitialWorldPosition[0],
                    part.InitialWorldPosition[1],
                    BenchTopMetres,
                    part.RotationZDegrees));
            }
            return [.. locations];
        }

        internal const string WorldFrameId = "world";
        internal const string RobotBaseFrameId = "robot_base";
        internal const string FlangeFrameId = "flange";
        internal const string ToolFrameId = "gripper_tcp";
        internal const string CameraFrameId = "camera_eih";

        /// <summary>
        /// The Location a part is picked from and returned to as a group.
        /// </summary>
        internal const string BinLocationName = "Bin";

        /// <summary>
        /// The Location parts are stacked on.
        /// </summary>
        internal const string FixtureLocationName = "Fixture";

        private const string HomeLocationPrefix = "Home";
        private const double FullTurnDegrees = 360.0;
        private const double HalfTurnDegrees = 180.0;

        // The robot stands on a 200 mm riser above the lowered work surface. Keeping these
        // in the shared geometry class makes the USD scene, frame tree, collision model and
        // support model describe one cell.
        internal const double RobotBaseHeightMetres = BinPickingCellGeometry.RobotBaseHeightMetres;
        internal const double BenchTopMetres = BinPickingCellGeometry.BenchTopMetres;
        private const double FixturePlateTopMetres = BinPickingCellGeometry.FixturePlateTopMetres;
        private const double FixturePegTopMetres = BinPickingCellGeometry.FixturePegTopMetres;

        // How far above a Location the tool travels to when it is going to pick something
        // up. Far enough to read as an approach rather than a collision. A Place does not
        // use this: it descends to the height that leaves the part resting on whatever is
        // under it, so releasing does not drop the part from the approach height.
        private const double ApproachHeightMetres = 0.20;
        private const double PlaceReleaseClearanceMetres = 0.017;
        private const double GripperClosedHalfGap = 0.009;
        private const double GripperOpenHalfGap = 0.040;

        // How close a part has to be to a Location to count as standing in it. The bin is a
        // tray parts are scattered across, so it reuses the catalogue's footprint; a home
        // slot is one part's own spot, so its radius only has to cover the millimetre-scale
        // settle a release leaves behind.
        private const double BinSlotRadiusMetres = BinPickingPartsCatalog.BinHalfExtent;
        private const double HomeSlotRadiusMetres = 0.02;

        // How far a part may be from the tool and still be grasped by it. Wide enough to
        // cover a Location's footprint, since a Pick travels to the Location rather than to
        // the part, and far narrower than the 0.70 m between the bin and the fixture, so a
        // grasp can never reach across the bench for something.
        private const double GraspReachRadiusMetres = 0.12;

        // Whether the workpieces are declared as obstacles. See UpdateMovingObstacles for
        // the measurement behind this being off.
        private const bool IncludePartsAsObstacles = false;

        // The simulator's DefaultJointSpeed is 0.9 rad/s; Position and the limits are
        // published in degrees, so the speed limit is too.
        private const double MaxAxisSpeedDegreesPerSecond = 0.9 * 180.0 / Math.PI;
        private const uint PayloadSlotCount = 8u;

        private static readonly (string Name, double X, double Y, double Z, double Rz)[] s_locations =
            BuildLocations();

        // The solids in this cell that never move and that a part can come to rest on, in
        // the world frame. Sizes are full extents, matching Cell.usda.
        private static readonly SimulatedSupportSolid[] s_supportFixtures =
        [
            new("Bench", 0.0, 0.0, 1.4, 0.9, BenchTopMetres),
            new("FixturePlate", BinPickingCellGeometry.FixtureCentreX, 0.0,
                0.14, 0.14, FixturePlateTopMetres),
            new("FixturePegA", BinPickingCellGeometry.FixtureCentreX - 0.032, 0.03,
                0.018, 0.018, FixturePegTopMetres),
            new("FixturePegB", BinPickingCellGeometry.FixtureCentreX + 0.032, 0.03,
                0.018, 0.018, FixturePegTopMetres),
            new("FixturePegC", BinPickingCellGeometry.FixtureCentreX, -0.03,
                0.018, 0.018, FixturePegTopMetres)
        ];

        private static readonly string[] s_axes = ["J1", "J2", "J3", "J4", "J5", "J6"];
        private static readonly double[] s_axisZ = [0.0, 0.0, 1.0];
        private static readonly double[] s_axisY = [0.0, 1.0, 0.0];

        private readonly ILogger<BinPickingRobotCell> m_logger;
        private readonly SimulatedArmExecutor m_executor;
        private readonly SimulatedArmKinematics m_kinematics;
        private readonly BinPickingWorldState m_worldState;
        private readonly ArrayOf<double> m_toolDownOrientation;
        private readonly SimulatedSupportModel m_support =
            new(ArrayOf.Create(s_supportFixtures.AsSpan()), BenchTopMetres);
        private string m_carriedClass = string.Empty;
        private readonly List<global::Opc.Ua.RobotIntent.AxisState> m_axes = [];
        private readonly List<global::Opc.Ua.RobotIntent.LocationState> m_locations = [];
        private readonly Dictionary<string, NodeId> m_locationNodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, global::Opc.Ua.RobotIntent.LocationState> m_locationStates =
            new(StringComparer.Ordinal);
        private BaseVariableState? m_gripperLeftSlideValue;
        private BaseVariableState? m_gripperRightSlideValue;
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

        [LoggerMessage(EventId = BinPickingCellEventIds.Configurator + 2,
            Level = LogLevel.Warning,
            Message = "Grasp at {ToolPosition} found no {ClassLabel} under the tool; " +
                "the gripper closed on nothing.")]
        public static partial void GraspFoundNothing(
            this ILogger<BinPickingRobotCell> logger,
            string classLabel, string toolPosition);

        [LoggerMessage(EventId = BinPickingCellEventIds.Configurator + 3,
            Level = LogLevel.Information,
            Message = "Arm travel: {Message}.")]
        public static partial void ArmTravel(
            this ILogger<BinPickingRobotCell> logger,
            string message);
    }
}
