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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.RobotIntent;
using Robotics.IntentEnabledRobot.Kinematics;

namespace Robotics.IntentEnabledRobot.Simulation
{
    /// <summary>
    /// Immutable observable state of the simulated arm.
    /// </summary>
    public sealed class SimulatedArmSnapshot
    {
        /// <summary>
        /// Initializes a snapshot.
        /// </summary>
        public SimulatedArmSnapshot(
            ArrayOf<double> jointAngles,
            Pose3DDataType toolPose,
            ArrayOf<Pose3DDataType> jointFramePoses,
            double gripperOpening,
            bool hasObject,
            string toolName,
            ArrayOf<double> heldPartPosition,
            ArrayOf<bool> stackSlotsFilled,
            string heldObjectClass = "")
        {
            JointAngles = jointAngles;
            ToolPose = toolPose;
            JointFramePoses = jointFramePoses;
            GripperOpening = gripperOpening;
            HasObject = hasObject;
            ToolName = toolName;
            HeldPartPosition = heldPartPosition;
            StackSlotsFilled = stackSlotsFilled;
            HeldObjectClass = heldObjectClass;
        }

        /// <summary>
        /// Gets joint angles in radians, ordered J1 through J6.
        /// </summary>
        public ArrayOf<double> JointAngles { get; }

        /// <summary>
        /// Gets the tool-centre-point pose in the robot base frame.
        /// </summary>
        public Pose3DDataType ToolPose { get; }

        /// <summary>
        /// Gets the six joint-frame poses used to drive the USD chain.
        /// </summary>
        public ArrayOf<Pose3DDataType> JointFramePoses { get; }

        /// <summary>
        /// Gets the parallel gripper opening in metres.
        /// </summary>
        public double GripperOpening { get; }

        /// <summary>
        /// Gets a value indicating whether the simulated gripper carries an object.
        /// </summary>
        public bool HasObject { get; }

        /// <summary>
        /// Gets the fitted simulated tool name.
        /// </summary>
        public string ToolName { get; }

        /// <summary>
        /// Gets the visible position of the part currently carried by the gripper.
        /// </summary>
        public ArrayOf<double> HeldPartPosition { get; }

        /// <summary>
        /// Gets the class label of the object the gripper carries, empty when it carries
        /// nothing. <see cref="HasObject"/> says that something is held; this says what,
        /// which is what a host needs to move the right item in its own world model.
        /// </summary>
        public string HeldObjectClass { get; }

        /// <summary>
        /// Gets a value for each pallet slot indicating whether that slot has been filled.
        /// </summary>
        public ArrayOf<bool> StackSlotsFilled { get; }
    }

    /// <summary>
    /// Provides deterministic simulation delays.
    /// </summary>
    public interface ISimulatedArmClock
    {
        /// <summary>
        /// Delays the simulated arm by the requested duration.
        /// </summary>
        ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Executes simulation delays using the wall clock.
    /// </summary>
    public sealed class RealTimeSimulatedArmClock : ISimulatedArmClock
    {
        /// <summary>
        /// Gets the shared real-time clock.
        /// </summary>
        public static RealTimeSimulatedArmClock Shared { get; } = new();

        /// <inheritdoc/>
        public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes Robot Intent structures against the simulated UR5e-style arm.
    /// </summary>
    public sealed class SimulatedArmExecutor : IIntentExecutor
    {
        /// <summary>
        /// Initializes a simulated executor with a new kinematics instance.
        /// </summary>
        public SimulatedArmExecutor()
            : this(new SimulatedArmKinematics())
        {
        }

        /// <summary>
        /// Initializes a simulated executor.
        /// </summary>
        public SimulatedArmExecutor(SimulatedArmKinematics kinematics)
            : this(kinematics, RealTimeSimulatedArmClock.Shared)
        {
        }

        /// <summary>
        /// Initializes a simulated executor over a kinematics provider.
        /// </summary>
        public SimulatedArmExecutor(ISimulatedArmKinematics kinematics)
            : this(kinematics, RealTimeSimulatedArmClock.Shared)
        {
        }

        /// <summary>
        /// Initializes a simulated executor.
        /// </summary>
        public SimulatedArmExecutor(SimulatedArmKinematics kinematics, ISimulatedArmClock clock)
            : this((ISimulatedArmKinematics)kinematics, clock)
        {
        }

        /// <summary>
        /// Initializes a simulated executor over a kinematics provider.
        /// </summary>
        public SimulatedArmExecutor(ISimulatedArmKinematics kinematics, ISimulatedArmClock clock)
        {
            m_kinematics = kinematics ?? throw new ArgumentNullException(nameof(kinematics));
            m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
            m_jointAngles = m_kinematics.InitialJointAngles.Span.ToArray();
            if (m_jointAngles.Length != m_kinematics.AxisCount)
            {
                throw new ArgumentException(
                    "The initial joint configuration does not match the kinematics axis count.",
                    nameof(kinematics));
            }
            PublishCurrentPoseLocked();
        }

        /// <summary>
        /// Raised after the observable snapshot changes.
        /// </summary>
        public event EventHandler<SimulatedArmSnapshot>? SnapshotChanged;

        /// <summary>
        /// Gets how far below the tool centre point a grasped part hangs.
        /// </summary>
        /// <remarks>
        /// A host that decides where a part should come to rest needs this to work back
        /// from the part's resting height to the tool pose that leaves it there.
        /// </remarks>
        public const double HeldPartTcpOffset = 0.035;

        /// <summary>
        /// Resolves a Location NodeId to the position, in this arm's base frame, that a
        /// Pick or Place should travel to before actuating the gripper. Optional: leave it
        /// unset and both intents actuate the gripper where the arm already stands.
        /// </summary>
        public LocationPositionResolver? ResolveLocationPosition { get; set; }

        /// <summary>
        /// Resolves a Location NodeId to a complete tool pose. When set, this takes
        /// precedence over <see cref="ResolveLocationPosition"/>.
        /// </summary>
        /// <remarks>
        /// A position-only resolver makes every move inherit whatever orientation the
        /// previous one left behind. A cell that knows how its tool should approach a bin
        /// or fixture supplies this instead, so one grasp cannot strand the next move at a
        /// yaw the arm cannot retract from.
        /// </remarks>
        public LocationPoseResolver? ResolveLocationPose { get; set; }

        /// <summary>
        /// Resolves a Pick to the current pose of the selected workpiece.
        /// </summary>
        public PickPoseResolver? ResolvePickPose { get; set; }

        /// <summary>
        /// Notifies the host after a Pick attempt finishes, whether it succeeded or failed.
        /// </summary>
        public Action<string>? PickAttemptFinished { get; set; }

        /// <summary>
        /// Gets or sets a host decision on whether the final approach to a Location should
        /// be a straight Cartesian descent. Optional: when unset, the executor uses a
        /// collision-checked joint path.
        /// </summary>
        /// <remarks>
        /// A cell knows the difference between descending inside an open bin and descending
        /// onto a fixture. The shared executor does not. Letting the host state that
        /// difference keeps cell coordinates out of this type while allowing a vertical
        /// descent where crossing a bin wall on a joint interpolation would be wrong.
        /// </remarks>
        public Func<NodeId, bool>? PreferCartesianDescent { get; set; }

        /// <summary>
        /// Gets or sets an optional diagnostic sink for travel planning decisions.
        /// </summary>
        internal Action<string>? Diagnostic { get; set; }

        /// <summary>
        /// Gets the latest observable state without exposing synchronization primitives.
        /// </summary>
        public SimulatedArmSnapshot CurrentSnapshot { get; private set; }

        /// <inheritdoc/>
        public async ValueTask<IntentOutcome> ExecuteAsync(
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            if (execution is null)
            {
                throw new ArgumentNullException(nameof(execution));
            }

            if (execution.Intent is JointMoveIntentDataType
                or LinearMoveIntentDataType
                or CircularMoveIntentDataType
                or TrajectoryIntentDataType
                or CartesianPathIntentDataType
                or ForceIntentDataType)
            {
                InvalidateRecordedApproach();
            }

            return execution.Intent switch
            {
                JointMoveIntentDataType joint => await ExecuteJointMoveAsync(joint, execution, cancellationToken)
                    .ConfigureAwait(false),
                LinearMoveIntentDataType linear => await ExecuteLinearMoveAsync(linear, execution, cancellationToken)
                    .ConfigureAwait(false),
                CircularMoveIntentDataType circular =>
                    await ExecuteCircularMoveAsync(circular, execution, cancellationToken)
                    .ConfigureAwait(false),
                TrajectoryIntentDataType trajectory =>
                    await ExecuteTrajectoryAsync(trajectory, execution, cancellationToken)
                    .ConfigureAwait(false),
                CartesianPathIntentDataType path => await ExecuteCartesianPathAsync(path, execution, cancellationToken)
                    .ConfigureAwait(false),
                ForceIntentDataType force =>
                    await ExecuteForceAsync(force, execution, cancellationToken).ConfigureAwait(false),
                GraspIntentDataType grasp =>
                    await ExecuteGraspAsync(grasp, execution, cancellationToken).ConfigureAwait(false),
                ReleaseIntentDataType release => await ExecuteReleaseAsync(release, execution, cancellationToken)
                    .ConfigureAwait(false),
                PickIntentDataType pick =>
                    await ExecutePickAsync(pick, execution, cancellationToken).ConfigureAwait(false),
                PlaceIntentDataType place =>
                    await ExecutePlaceAsync(place, execution, cancellationToken).ConfigureAwait(false),
                ToolChangeIntentDataType tool => await ExecuteToolChangeAsync(tool, execution, cancellationToken)
                    .ConfigureAwait(false),
                WaitIntentDataType wait =>
                    await ExecuteWaitAsync(wait, execution, cancellationToken).ConfigureAwait(false),
                _ => IntentOutcome.Fail(
                    IntentFailureEnum.CapabilityNotSupported, "The simulated arm does not support this intent.")
            };
        }

        /// <inheritdoc/>
        public bool CanCancel(IntentExecution execution)
        {
            if (execution is null)
            {
                throw new ArgumentNullException(nameof(execution));
            }
            lock (m_lock)
            {
                return m_nonCancellableIntentId.Length == 0 || m_nonCancellableIntentId != execution.IntentId;
            }
        }

        private async ValueTask<IntentOutcome> ExecuteJointMoveAsync(
            JointMoveIntentDataType intent,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            double[] start = GetJoints();
            ArrayOf<double> target;
            if (intent.HasJointTargets)
            {
                if (intent.JointTargets.Count != m_kinematics.AxisCount ||
                    !m_kinematics.IsWithinLimits(intent.JointTargets.Span))
                {
                    return IntentOutcome.Fail(
                        IntentFailureEnum.JointLimit, "Joint targets exceed the simulated limits.");
                }
                target = ArrayOf.Create(intent.JointTargets.Span);
            }
            else if (m_kinematics.TrySelectNearest(
                intent.TargetPose,
                start,
                out SimulatedArmIkSolution? solution,
                out SimulatedArmKinematicFailure failure))
            {
                target = solution.JointAngles;
            }
            else
            {
                return IntentOutcome.Fail(
                    m_kinematics.MapFailure(failure), "The target pose cannot be reached.");
            }

            double distance = JointDistance(start, target.Span);
            var profile = new TrapezoidalVelocityProfile(
                distance, JointSpeed(intent.Constraints), DefaultJointAcceleration);
            IntentOutcome outcome = await FollowProfileAsync(
                profile,
                execution,
                fraction => SetJoints(m_kinematics.InterpolateJoints(start, target.Span, fraction).Span),
                DefaultJointAcceleration,
                cancellationToken).ConfigureAwait(false);
            return outcome.State == ExecutionStateEnum.Succeeded
                ? IntentOutcome.SucceededAt(CurrentSnapshot.ToolPose)
                : outcome;
        }

        private ValueTask<IntentOutcome> ExecuteLinearMoveAsync(
            LinearMoveIntentDataType intent,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            return MoveCartesianAsync(intent.Target, intent.Constraints, execution, cancellationToken);
        }

        private async ValueTask<IntentOutcome> ExecuteCircularMoveAsync(
            CircularMoveIntentDataType intent,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            Pose3DDataType start = CurrentSnapshot.ToolPose;
            var via = new Pose3DDataType
            {
                FrameId = intent.ViaPoint.FrameId,
                Position = ArrayOf.Create(intent.ViaPoint.Position.Span),
                Orientation = ArrayOf.Create(start.Orientation.Span)
            };
            double distance = ArcLength(start.Position.Span, via.Position.Span, intent.Target.Position.Span);
            var profile = new TrapezoidalVelocityProfile(
                distance,
                CartesianSpeed(intent.Constraints),
                CartesianAcceleration(intent.Constraints));
            SimulatedArmKinematicFailure moveFailure = SimulatedArmKinematicFailure.None;
            IntentOutcome outcome = await FollowProfileAsync(
                profile,
                execution,
                fraction =>
                {
                    Pose3DDataType pose = InterpolateArc(start, via, intent.Target, fraction);
                    if (m_kinematics.TrySelectNearestConfiguration(
                        pose,
                        CurrentSnapshot.JointAngles.Span,
                        out SimulatedArmIkSolution? solution,
                        out SimulatedArmKinematicFailure failure))
                    {
                        SetPose(solution.JointAngles.Span);
                    }
                    else
                    {
                        moveFailure = failure;
                    }
                },
                CartesianAcceleration(intent.Constraints),
                cancellationToken,
                () => moveFailure != SimulatedArmKinematicFailure.None).ConfigureAwait(false);
            if (moveFailure != SimulatedArmKinematicFailure.None)
            {
                return IntentOutcome.Fail(
                    m_kinematics.MapFailure(moveFailure), "The circular path is not feasible.");
            }
            return outcome.State == ExecutionStateEnum.Succeeded
                ? IntentOutcome.SucceededAt(CurrentSnapshot.ToolPose)
                : outcome;
        }

        private async ValueTask<IntentOutcome> ExecuteTrajectoryAsync(
            TrajectoryIntentDataType intent,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            if (intent.Points.IsEmpty)
            {
                return IntentOutcome.Fail(IntentFailureEnum.ParameterInvalid, "The trajectory has no points.");
            }
            double finalMs = intent.Points[^1].TimeFromStart;
            double elapsed = 0.0;
            int pointIndex = 0;
            while (elapsed < finalMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (pointIndex + 1 < intent.Points.Count && intent.Points[pointIndex + 1].TimeFromStart <= elapsed)
                {
                    pointIndex++;
                }
                TrajectoryPointDataType a = intent.Points[pointIndex];
                TrajectoryPointDataType b = intent.Points[Math.Min(pointIndex + 1, intent.Points.Count - 1)];
                double fraction = Math.Clamp(
                    (elapsed - a.TimeFromStart) / Math.Max(1.0, b.TimeFromStart - a.TimeFromStart), 0.0, 1.0);
                SetJoints(m_kinematics.InterpolateJoints(a.Positions.Span, b.Positions.Span, fraction).Span);
                double goalDeviation = JointDistance(
                    CurrentSnapshot.JointAngles.Span, intent.Points[^1].Positions.Span);
                execution.Progress.ReportProgress(Math.Clamp(elapsed / finalMs, 0.0, 1.0));
                execution.Progress.ReportPose(CurrentSnapshot.ToolPose);
                execution.Progress.ReportTrajectoryDeviation(0.0, goalDeviation, elapsed, false);
                if (!await DelayTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
                elapsed += TickSeconds * 1000.0;
            }
            SetJoints(intent.Points[^1].Positions.Span);
            execution.Progress.ReportProgress(1.0);
            execution.Progress.ReportPose(CurrentSnapshot.ToolPose);
            execution.Progress.ReportTrajectoryDeviation(0.0, 0.0, finalMs, true);
            return IntentOutcome.SucceededAt(CurrentSnapshot.ToolPose);
        }

        private async ValueTask<IntentOutcome> ExecuteCartesianPathAsync(
            CartesianPathIntentDataType intent,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < intent.Waypoints.Count; i++)
            {
                PathWaypointDataType waypoint = intent.Waypoints[i];
                IntentOutcome outcome = await MoveCartesianAsync(
                    waypoint.Pose,
                    new MotionConstraintsDataType(),
                    execution,
                    cancellationToken).ConfigureAwait(false);
                if (outcome.State != ExecutionStateEnum.Succeeded)
                {
                    return outcome;
                }
            }
            return IntentOutcome.SucceededAt(CurrentSnapshot.ToolPose);
        }

        private async ValueTask<IntentOutcome> ExecuteForceAsync(
            ForceIntentDataType intent,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            Pose3DDataType start = CurrentSnapshot.ToolPose;
            ReadOnlySpan<double> direction = intent.Direction.Span;
            double norm = Math.Sqrt(
                (direction[0] * direction[0]) + (direction[1] * direction[1]) + (direction[2] * direction[2]));
            if (norm == 0.0)
            {
                return IntentOutcome.Fail(IntentFailureEnum.ParameterInvalid, "Force direction is zero.");
            }
            double directionX = direction[0] / norm;
            double directionY = direction[1] / norm;
            double directionZ = direction[2] / norm;
            var profile = new TrapezoidalVelocityProfile(
                intent.MaxDistance, DefaultCartesianSpeed * 0.35, DefaultCartesianAcceleration);
            bool contacted = false;
            SimulatedArmKinematicFailure moveFailure = SimulatedArmKinematicFailure.None;
            IntentOutcome outcome = await FollowProfileAsync(
                profile,
                execution,
                fraction =>
                {
                    double distance = fraction * intent.MaxDistance;
                    Pose3DDataType pose = TranslatePose(start, directionX, directionY, directionZ, distance);
                    contacted = IsContact(pose.Position.Span);
                    if (m_kinematics.TrySelectNearestConfiguration(
                        pose,
                        CurrentSnapshot.JointAngles.Span,
                        out SimulatedArmIkSolution? solution,
                        out SimulatedArmKinematicFailure failure))
                    {
                        SetPose(solution.JointAngles.Span);
                    }
                    else
                    {
                        moveFailure = failure;
                    }
                },
                DefaultCartesianAcceleration,
                cancellationToken,
                () => contacted || moveFailure != SimulatedArmKinematicFailure.None).ConfigureAwait(false);
            if (outcome.State != ExecutionStateEnum.Succeeded)
            {
                return outcome;
            }
            if (moveFailure != SimulatedArmKinematicFailure.None)
            {
                return IntentOutcome.Fail(
                    m_kinematics.MapFailure(moveFailure), "The force path is not feasible.");
            }
            return contacted
                ? IntentOutcome.SucceededAt(CurrentSnapshot.ToolPose)
                : IntentOutcome.Fail(
                    IntentFailureEnum.ObjectNotFound, "No contact occurred before MaxDistance was exhausted.");
        }

        private async ValueTask<IntentOutcome> ExecuteGraspAsync(
            GraspIntentDataType intent,
            IntentExecution execution,
            CancellationToken cancellationToken,
            string objectClass = "")
        {
            SetNonCancellable(execution.IntentId);
            try
            {
                double width = intent.Width > 0.0
                    ? Math.Clamp(intent.Width, GripperClosed, GripperOpen)
                    : GripperClosed;
                await MoveGripperAsync(width, execution, cancellationToken).ConfigureAwait(false);
                lock (m_lock)
                {
                    m_hasObject = true;
                    m_heldObjectClass = objectClass ?? string.Empty;
                    PublishCurrentPoseLocked();
                }
                SnapshotChanged?.Invoke(this, CurrentSnapshot);
                return IntentOutcome.Success;
            }
            finally
            {
                ClearNonCancellable(execution.IntentId);
            }
        }

        private async ValueTask<IntentOutcome> ExecuteReleaseAsync(
            ReleaseIntentDataType intent,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            if (intent.HasTarget)
            {
                IntentOutcome motion = await MoveCartesianAsync(
                    intent.Target,
                    new MotionConstraintsDataType(),
                    execution,
                    cancellationToken).ConfigureAwait(false);
                if (motion.State != ExecutionStateEnum.Succeeded)
                {
                    return motion;
                }
            }
            await MoveGripperAsync(GripperOpen, execution, cancellationToken).ConfigureAwait(false);
            lock (m_lock)
            {
                if (m_hasObject)
                {
                    FillNextStackSlotLocked();
                }
                m_hasObject = false;
                m_heldObjectClass = string.Empty;
                PublishCurrentPoseLocked();
            }
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return IntentOutcome.Success;
        }

        private async ValueTask<IntentOutcome> ExecutePickAsync(
            PickIntentDataType intent,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            string objectClass = intent.ObjectClass ?? string.Empty;
            try
            {
                await m_clock.DelayAsync(TimeSpan.FromMilliseconds(80), cancellationToken).ConfigureAwait(false);
                if (!await MoveToLocationAsync(
                        intent.Source,
                        execution,
                        cancellationToken,
                        objectClass).ConfigureAwait(false))
                {
                    return Unreachable("Pick");
                }
                IntentOutcome grasp = await ExecuteGraspAsync(
                    new GraspIntentDataType { Force = intent.Force, Width = GripperClosed, Tool = intent.Tool },
                    execution,
                    cancellationToken,
                    objectClass).ConfigureAwait(false);
                if (grasp.State != ExecutionStateEnum.Succeeded)
                {
                    return grasp;
                }
                return await RetractAfterActionAsync(
                    "Pick",
                    execution,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                PickAttemptFinished?.Invoke(objectClass);
            }
        }

        private async ValueTask<IntentOutcome> ExecutePlaceAsync(
            PlaceIntentDataType intent,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            await m_clock.DelayAsync(TimeSpan.FromMilliseconds(80), cancellationToken).ConfigureAwait(false);
            if (!await MoveToLocationAsync(intent.Destination, execution, cancellationToken)
                .ConfigureAwait(false))
            {
                return Unreachable("Place");
            }
            IntentOutcome release = await ExecuteReleaseAsync(
                new ReleaseIntentDataType(), execution, cancellationToken).ConfigureAwait(false);
            if (release.State != ExecutionStateEnum.Succeeded)
            {
                return release;
            }
            return await RetractAfterActionAsync(
                "Place",
                execution,
                cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<IntentOutcome> RetractAfterActionAsync(
            string action,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            if (await RetractFromLastApproachAsync(execution, cancellationToken).ConfigureAwait(false))
            {
                return IntentOutcome.Success;
            }
            return IntentOutcome.Fail(
                IntentFailureEnum.Unreachable,
                action + " completed its tool action but could not reverse the local approach.");
        }

        /// <summary>
        /// Reports an intent that could not be carried out because the arm could not get to
        /// the Location it named.
        /// </summary>
        private IntentOutcome Unreachable(string what)
        {
            return IntentOutcome.Fail(
                IntentFailureEnum.Unreachable,
                what + " could not reach the Location: " + LastTravelFailure);
        }

        /// <summary>
        /// Travels to the approach position a host resolved for a Location, so a Pick or a
        /// Place is a move followed by a gripper action rather than a gripper action alone.
        /// </summary>
        /// <remarks>
        /// A Location arrives as a NodeId, which this executor cannot resolve on its own -
        /// it has no address space - so a host that knows its own cell supplies
        /// <see cref="ResolveLocationPosition"/>. The tool keeps its current orientation and
        /// only the position changes, because the current orientation belongs to a
        /// configuration the arm is already in and so keeps the inverse-kinematic solve
        /// well conditioned.
        /// <para>
        /// A host with no resolver gets the gripper action where the arm stands: it has not
        /// told the executor where anything is, so there is nothing to travel to. But a
        /// Location that <i>is</i> resolved and cannot be reached fails the intent. It used
        /// to be best effort - the arm would stay where it was and close the gripper anyway,
        /// reporting success - which reads as "picked from the fixture" while the tool is
        /// still over the bin. A pick that never went anywhere is not a pick, and saying so
        /// is what lets a caller notice.
        /// </para>
        /// </remarks>
        /// <returns>
        /// <c>true</c> when the arm reached the Location, or when no resolver is configured.
        /// </returns>
        private async ValueTask<bool> MoveToLocationAsync(
            NodeId location,
            IntentExecution execution,
            CancellationToken cancellationToken,
            string objectClass = "")
        {
            if (location.IsNull)
            {
                return true;
            }
            if (!await RetractFromLastApproachAsync(execution, cancellationToken).ConfigureAwait(false))
            {
                LastTravelFailure = "could not reverse the last local approach";
                Diagnostic?.Invoke(LastTravelFailure);
                return false;
            }
            Pose3DDataType current = CurrentSnapshot.ToolPose;
            ArrayOf<double> position;
            ArrayOf<double> orientation;
            string frameId;
            if (objectClass.Length > 0
                && ResolvePickPose != null
                && ResolvePickPose(location, objectClass, out Pose3DDataType pickPose))
            {
                position = pickPose.Position;
                orientation = pickPose.Orientation;
                frameId = pickPose.FrameId ?? current.FrameId ?? string.Empty;
            }
            else if (ResolveLocationPose != null
                && ResolveLocationPose(location, out Pose3DDataType resolvedPose))
            {
                position = resolvedPose.Position;
                orientation = resolvedPose.Orientation;
                frameId = resolvedPose.FrameId ?? current.FrameId ?? string.Empty;
            }
            else if (ResolveLocationPosition != null
                && ResolveLocationPosition(location, out position))
            {
                orientation = current.Orientation;
                frameId = current.FrameId ?? string.Empty;
            }
            else
            {
                return true;
            }
            if (position.Count < 3 || orientation.Count < 4)
            {
                return true;
            }
            ReadOnlySpan<double> targetPosition = position.Span;
            ReadOnlySpan<double> currentPosition = current.Position.Span;
            ArrayOf<double> currentOrientation = current.Orientation;
            bool sameWorkPosition =
                Distance2D(currentPosition, targetPosition) <= SameLocationToleranceMetres;

            // Travel over the cell rather than straight at the target. A single joint-space
            // move interpolates between two configurations, and the straight line between
            // "over the bin" and "over the fixture" dips: the arm sweeps a link through the
            // bench on the way, which is what makes it look like it is passing through the
            // table even when both ends of the move are clear. Lifting to a transit height,
            // crossing, and descending is both how a real cell moves and a set of legs whose
            // straight-line paths stay clear.
            double transitZ = Math.Max(
                Math.Max(currentPosition[2], targetPosition[2]), TransitHeightMetres);
            double[] lift = [currentPosition[0], currentPosition[1], transitZ];
            double[] cross = [targetPosition[0], targetPosition[1], transitZ];
            double[] descend = [targetPosition[0], targetPosition[1], targetPosition[2]];
            // An empty gripper picking from any work area should come straight down on the
            // object. A loaded gripper placing onto the fixture may need the short,
            // collision-checked joint approach instead. The host preference still marks
            // bin/home locations as vertical for both directions.
            bool preferCartesianDescent = !CurrentSnapshot.HasObject
                || PreferCartesianDescent?.Invoke(location) == true;
            Diagnostic?.Invoke(string.Create(
                CultureInfo.InvariantCulture,
                $"start=({currentPosition[0]:F3},{currentPosition[1]:F3},{currentPosition[2]:F3}) "
                + $"target=({targetPosition[0]:F3},{targetPosition[1]:F3},{targetPosition[2]:F3}) "
                + $"cartesianDescent={preferCartesianDescent}"));

            if (sameWorkPosition)
            {
                Diagnostic?.Invoke("target is at the current work position; skipping cross-cell traverse");
                bool localDescent = await MovePlannedCartesianAsync(
                    descend,
                    frameId,
                    orientation,
                    execution,
                    recordRetractPath: true,
                    cancellationToken).ConfigureAwait(false);
                if (!localDescent)
                {
                    LastTravelFailure = "could not descend at the current work position";
                    Diagnostic?.Invoke(LastTravelFailure);
                }
                return localDescent;
            }

            // Lift in Cartesian space so the tool moves vertically away from the work.
            // Cross in joint space: a straight Cartesian line between the bin and fixture
            // passes over the base, where the tool sits on the shoulder axis and the inverse
            // kinematics are singular. Descend on a collision-checked joint path too: the
            // final work pose is reachable, but re-solving every point on the vertical line
            // can switch branches and reject the leg before it gets there. Raising the
            // pedestal, lowering the bench and moving both work areas out leaves 9-13 clear
            // candidates at the transit poses, so the arm can retract and traverse instead
            // of making one sweep through the table.
            bool retracted = await MoveToolToAsync(
                lift, frameId, currentOrientation, execution, cancellationToken).ConfigureAwait(false);
            if (!retracted)
            {
                // A low fixture pose can be reachable while the numerical solver has no
                // continuous Cartesian branch straight above it. The destination is still
                // a clear pose, so try the collision-checked joint path before refusing the
                // Pick or Place.
                retracted = await SwingToAsync(
                    lift, frameId, currentOrientation, execution, cancellationToken).ConfigureAwait(false);
            }
            if (!retracted)
            {
                LastTravelFailure = "could not retract vertically from the work";
                Diagnostic?.Invoke(LastTravelFailure);
                return false;
            }
            Diagnostic?.Invoke("retracted to the clear height");
            if (!await SwingToAsync(cross, frameId, orientation, execution, cancellationToken)
                .ConfigureAwait(false))
            {
                Diagnostic?.Invoke("direct clear-height traverse unavailable; trying bypass");
                // The direct interpolation between work areas on opposite sides can sweep
                // through the shoulder axis even though both ends are clear. Route around
                // it at the same safe height, the way a motion planner would choose a
                // waypoint around a keep-out cylinder.
                double[] nearBypass = [-TransitBypassXMetres, TransitBypassYMetres, transitZ];
                double[] farBypass = [TransitBypassXMetres, TransitBypassYMetres, transitZ];
                bool bypassed = await SwingToAsync(
                    nearBypass, frameId, orientation, execution, cancellationToken).ConfigureAwait(false);
                if (!bypassed)
                {
                    bypassed = await MoveToolToAsync(
                        nearBypass, frameId, orientation, execution, cancellationToken).ConfigureAwait(false);
                }
                Diagnostic?.Invoke(bypassed
                    ? "reached the near clear-height bypass"
                    : "could not reach the near clear-height bypass");
                if (bypassed)
                {
                    bypassed = await SwingToAsync(
                        farBypass, frameId, orientation, execution, cancellationToken).ConfigureAwait(false);
                    if (!bypassed)
                    {
                        bypassed = await MoveToolToAsync(
                            farBypass, frameId, orientation, execution, cancellationToken).ConfigureAwait(false);
                    }
                    Diagnostic?.Invoke(bypassed
                        ? "reached the far clear-height bypass"
                        : "could not reach the far clear-height bypass");
                }
                if (bypassed)
                {
                    bypassed = await SwingToAsync(
                        cross, frameId, orientation, execution, cancellationToken).ConfigureAwait(false);
                    if (!bypassed)
                    {
                        bypassed = await MoveToolToAsync(
                            cross, frameId, orientation, execution, cancellationToken).ConfigureAwait(false);
                    }
                    Diagnostic?.Invoke(bypassed
                        ? "reached the far side from the bypass arc"
                        : "could not reach the far side from the bypass arc");
                }
                if (!bypassed)
                {
                    LastTravelFailure = "could not traverse the cell around the base at the clear height";
                    Diagnostic?.Invoke(LastTravelFailure);
                    return false;
                }
            }
            Diagnostic?.Invoke("traversed the cell at the clear height");
            bool descended;
            if (preferCartesianDescent)
            {
                descended = await MovePlannedCartesianAsync(
                    descend,
                    frameId,
                    orientation,
                    execution,
                    recordRetractPath: true,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // First establish a configuration locally above the fixture. Solving the
                // final pose from the far-side transit configuration can leave no connected
                // joint interpolation even though the pose itself has several clear
                // solutions. From 40 mm above, the short descent has the nearby branch a
                // real approach motion needs without standing so high above a tall stack
                // that the final branch disconnects again.
                double[] preApproach =
                    [descend[0], descend[1], descend[2] + LocalApproachHeightMetres];
                descended = await MoveToolToAsync(
                    preApproach, frameId, orientation, execution, cancellationToken)
                    .ConfigureAwait(false);
                if (descended)
                {
                    double[] retractJointAngles = GetJoints();
                    descended = await MovePlannedCartesianAsync(
                        descend,
                        frameId,
                        orientation,
                        execution,
                        recordRetractPath: true,
                        cancellationToken)
                        .ConfigureAwait(false);
                    if (!descended)
                    {
                        descended = await SwingToAsync(
                            descend, frameId, orientation, execution, cancellationToken)
                            .ConfigureAwait(false);
                        if (descended)
                        {
                            m_retractJointAngles = retractJointAngles;
                            m_retractJointEndpoint = GetJoints();
                        }
                    }
                }
            }
            if (!descended)
            {
                // The chosen path can still be unavailable because the numerical IK solver
                // switches branches along a Cartesian line, or because the direct joint
                // interpolation sweeps a link through an obstacle. Try the other path from
                // wherever the first attempt stopped before refusing the intent.
                descended = preferCartesianDescent
                    ? await SwingToAsync(descend, frameId, orientation, execution, cancellationToken)
                        .ConfigureAwait(false)
                    : await MoveToolToAsync(
                        descend,
                        frameId,
                        orientation,
                        execution,
                        cancellationToken,
                        recordRetractPath: true)
                        .ConfigureAwait(false);
            }
            if (!descended)
            {
                LastTravelFailure = "could not descend onto the work from the clear height";
                Diagnostic?.Invoke(LastTravelFailure);
                return false;
            }
            Diagnostic?.Invoke("descended onto the work");
            return true;
        }

        /// <summary>
        /// Replays the reverse of the last short fixture approach.
        /// </summary>
        /// <remarks>
        /// The final fixture pose may require the yaw search to choose an orientation from
        /// which IK cannot independently solve a vertical retract. The path into that pose
        /// was already collision checked, so retaining its start configuration gives an
        /// exact, deterministic path back out instead of asking the numerical solver to
        /// rediscover one after the gripper action.
        /// </remarks>
        private async ValueTask<bool> RetractFromLastApproachAsync(
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            List<double[]>? sampledPath = m_retractCartesianPath;
            if (sampledPath != null)
            {
                m_retractCartesianPath = null;
                double[] current = GetJoints();
                if (!SameJointConfiguration(current, sampledPath[^1]))
                {
                    Diagnostic?.Invoke("discarded a stale Cartesian retract path");
                    return true;
                }
                double[] previous = current;
                for (int ii = sampledPath.Count - 2; ii >= 0; ii--)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double[] next = sampledPath[ii];
                    if (CurrentSnapshot.HasObject &&
                        !m_kinematics.ClearsPath(previous, next))
                    {
                        return false;
                    }
                    SetJoints(next);
                    previous = next;
                    if (!await DelayTickAsync(cancellationToken).ConfigureAwait(false))
                    {
                        return false;
                    }
                }
                Diagnostic?.Invoke("replayed the last Cartesian approach in reverse");
                return true;
            }

            double[]? target = m_retractJointAngles;
            if (target == null)
            {
                return true;
            }
            double[]? endpoint = m_retractJointEndpoint;
            m_retractJointAngles = null;
            m_retractJointEndpoint = null;
            double[] start = GetJoints();
            if (endpoint == null || !SameJointConfiguration(start, endpoint))
            {
                Diagnostic?.Invoke("discarded a stale joint retract path");
                return true;
            }
            if (!m_kinematics.ClearsPath(start, target))
            {
                return false;
            }
            double distance = JointDistance(start, target);
            var profile = new TrapezoidalVelocityProfile(
                distance, JointSpeed(new MotionConstraintsDataType()), DefaultJointAcceleration);
            IntentOutcome outcome = await FollowProfileAsync(
                profile,
                execution,
                fraction => SetJoints(
                    m_kinematics.InterpolateJoints(start, target, fraction).Span),
                DefaultJointAcceleration,
                cancellationToken).ConfigureAwait(false);
            if (outcome.State == ExecutionStateEnum.Succeeded)
            {
                Diagnostic?.Invoke("reversed the last local approach");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Invalidates a recorded local approach when another motion changes the arm pose.
        /// </summary>
        private void InvalidateRecordedApproach()
        {
            m_retractCartesianPath = null;
            m_retractJointAngles = null;
            m_retractJointEndpoint = null;
        }

        /// <summary>
        /// Gets whether two joint configurations agree closely enough to replay a recorded
        /// path from the current pose.
        /// </summary>
        private static bool SameJointConfiguration(double[] left, double[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }
            for (int ii = 0; ii < left.Length; ii++)
            {
                if (Math.Abs(left[ii] - right[ii]) > RetractEndpointToleranceRadians)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Swings the tool to a position in joint space, so the arm rotates around its base
        /// rather than trying to carry the tool across the axis it stands on.
        /// </summary>
        private async ValueTask<bool> SwingToAsync(
            double[] position,
            string frameId,
            ArrayOf<double> orientation,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            double[] start = GetJoints();
            if (!TrySolveWithYawSearch(position, frameId, orientation, start, out SimulatedArmIkSolution? solution))
            {
                return false;
            }
            double distance = JointDistance(start, solution.JointAngles.Span);
            var profile = new TrapezoidalVelocityProfile(
                distance, JointSpeed(new MotionConstraintsDataType()), DefaultJointAcceleration);
            _ = await FollowProfileAsync(
                profile,
                execution,
                fraction => SetJoints(
                    m_kinematics.InterpolateJoints(start, solution.JointAngles.Span, fraction).Span),
                DefaultJointAcceleration,
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Solves for a tool position, turning the tool about the vertical when the
        /// orientation it is holding does not work out.
        /// </summary>
        /// <remarks>
        /// A parallel gripper coming straight down is free to choose its rotation about the
        /// tool axis - the jaws close on a part the same way whichever way round they are -
        /// but the cell never used that freedom: it reused whatever orientation the arm was
        /// left holding, so each Location got exactly one pose and one chance. That is fine
        /// until the solver has to satisfy clearance as well, at which point a single pose
        /// often has no answer while the same position a few degrees round has several.
        /// The requested orientation is tried first so nothing changes when it works, and
        /// the offsets are a fixed sequence so the same target always resolves the same way.
        /// </remarks>
        private bool TrySolveWithYawSearch(
            double[] position,
            string frameId,
            ArrayOf<double> orientation,
            double[] start,
            [NotNullWhen(true)] out SimulatedArmIkSolution? solution)
        {
            ReadOnlySpan<double> requested = orientation.Span;
            foreach (double degrees in s_yawOffsetsDegrees)
            {
                var target = new Pose3DDataType
                {
                    FrameId = frameId,
                    Position = position.ToArrayOf(),
                    Orientation = degrees == 0.0
                        ? orientation
                        : TurnAboutVertical(requested, degrees).ToArrayOf()
                };
                if (m_kinematics.TrySelectNearest(
                    target, start, out solution, out SimulatedArmKinematicFailure _))
                {
                    return true;
                }
            }
            solution = null;
            return false;
        }

        /// <summary>
        /// Turns an orientation about the world vertical.
        /// </summary>
        private static double[] TurnAboutVertical(ReadOnlySpan<double> orientation, double degrees)
        {
            double half = degrees * Math.PI / 360.0;
            double sin = Math.Sin(half);
            double cos = Math.Cos(half);
            return
            [
                (cos * orientation[0]) - (sin * orientation[1]),
                (cos * orientation[1]) + (sin * orientation[0]),
                (cos * orientation[2]) + (sin * orientation[3]),
                (cos * orientation[3]) - (sin * orientation[2])
            ];
        }

        /// <summary>
        /// Gets why the last travel to a Location was refused, for the failure message.
        /// </summary>
        private string LastTravelFailure { get; set; } = string.Empty;

        /// <summary>
        /// Moves the tool centre point to one position along a straight line, keeping its
        /// orientation.
        /// </summary>
        /// <remarks>
        /// The line is followed in Cartesian space and re-solved at each step rather than
        /// solved once and interpolated in joint space. Interpolating in joint space takes
        /// whatever route the two configurations happen to describe, and when the solver
        /// picks a different elbow branch for the far end that route swings a link through
        /// the bench - so with clearance enforced, every candidate gets refused and the arm
        /// simply stops. Re-solving along a straight line keeps each step next to the last
        /// one, which is also what the move looks like on a real cell.
        /// </remarks>
        private async ValueTask<bool> MoveToolToAsync(
            double[] position,
            string frameId,
            ArrayOf<double> orientation,
            IntentExecution execution,
            CancellationToken cancellationToken,
            bool recordRetractPath = false)
        {
            var target = new Pose3DDataType
            {
                FrameId = frameId,
                Position = position.ToArrayOf(),
                Orientation = orientation
            };
            List<double[]>? jointPath = recordRetractPath ? [GetJoints()] : null;
            IntentOutcome outcome = await MoveCartesianAsync(
                target,
                new MotionConstraintsDataType(),
                execution,
                cancellationToken,
                jointPath)
                .ConfigureAwait(false);
            bool succeeded = outcome.State == ExecutionStateEnum.Succeeded;
            if (recordRetractPath)
            {
                m_retractCartesianPath = succeeded && jointPath is { Count: > 1 }
                    ? jointPath
                    : null;
            }
            return succeeded;
        }

        /// <summary>
        /// Plans a complete, locally sampled Cartesian move before executing any of it.
        /// </summary>
        /// <remarks>
        /// Solving while moving can switch into a branch that has no continuation near the
        /// target, even when another yaw has a clear path all the way down. This method
        /// evaluates every sample first, tries the same deterministic yaw spread used by
        /// direct moves, and executes only a sequence that reached the target. The samples
        /// are close enough that checking every configuration is the swept-path
        /// approximation; no unrelated joint interpolation is substituted for it.
        /// </remarks>
        private async ValueTask<bool> MovePlannedCartesianAsync(
            double[] position,
            string frameId,
            ArrayOf<double> orientation,
            IntentExecution execution,
            bool recordRetractPath,
            CancellationToken cancellationToken)
        {
            Pose3DDataType startPose = CurrentSnapshot.ToolPose;
            double[] startingJoints = GetJoints();
            double[] requested = orientation.Span.ToArray();
            foreach (double degrees in s_yawOffsetsDegrees)
            {
                ArrayOf<double> candidateOrientation = degrees == 0.0
                    ? orientation
                    : TurnAboutVertical(requested, degrees).ToArrayOf();
                var target = new Pose3DDataType
                {
                    FrameId = frameId,
                    Position = position.ToArrayOf(),
                    Orientation = candidateOrientation
                };
                var path = new List<double[]>(CartesianPlanningSamples + 1)
                {
                    (double[])startingJoints.Clone()
                };
                double[] reference = (double[])startingJoints.Clone();
                bool complete = true;
                for (int step = 1; step <= CartesianPlanningSamples; step++)
                {
                    double fraction = (double)step / CartesianPlanningSamples;
                    Pose3DDataType pose = m_kinematics.InterpolateCartesian(startPose, target, fraction);
                    if (!m_kinematics.TrySelectNearestConfiguration(
                        pose,
                        reference,
                        out SimulatedArmIkSolution? solution,
                        out SimulatedArmKinematicFailure _))
                    {
                        complete = false;
                        break;
                    }
                    reference = solution.JointAngles.Span.ToArray();
                    path.Add(reference);
                }
                if (!complete)
                {
                    continue;
                }

                double distance = Distance(startPose.Position.Span, target.Position.Span);
                var profile = new TrapezoidalVelocityProfile(
                    distance, DefaultCartesianSpeed, DefaultCartesianAcceleration);
                IntentOutcome outcome = await FollowProfileAsync(
                    profile,
                    execution,
                    fraction =>
                    {
                        int index = Math.Clamp(
                            (int)Math.Round(fraction * (path.Count - 1)),
                            0,
                            path.Count - 1);
                        SetPose(path[index]);
                    },
                    DefaultCartesianAcceleration,
                    cancellationToken).ConfigureAwait(false);
                bool succeeded = outcome.State == ExecutionStateEnum.Succeeded;
                if (recordRetractPath)
                {
                    m_retractCartesianPath = succeeded ? path : null;
                }
                return succeeded;
            }
            if (recordRetractPath)
            {
                m_retractCartesianPath = null;
            }
            return false;
        }

        private async ValueTask<IntentOutcome> ExecuteToolChangeAsync(
            ToolChangeIntentDataType intent,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            SetNonCancellable(execution.IntentId);
            try
            {
                await m_clock.DelayAsync(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                lock (m_lock)
                {
                    m_toolName = intent.Tool.IsNull ? "none" : intent.Tool.ToString();
                    PublishCurrentPoseLocked();
                }
                SnapshotChanged?.Invoke(this, CurrentSnapshot);
                return IntentOutcome.Success;
            }
            finally
            {
                ClearNonCancellable(execution.IntentId);
            }
        }

        private async ValueTask<IntentOutcome> ExecuteWaitAsync(
            WaitIntentDataType intent,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            double duration = intent.Duration > 0.0 ? intent.Duration : 100.0;
            double elapsed = 0.0;
            while (elapsed < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                execution.Progress.ReportProgress(Math.Clamp(elapsed / duration, 0.0, 1.0));
                if (!await DelayTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
                elapsed += TickSeconds * 1000.0;
            }
            execution.Progress.ReportProgress(1.0);
            return IntentOutcome.Success;
        }

        private async ValueTask<IntentOutcome> MoveCartesianAsync(
            Pose3DDataType target,
            MotionConstraintsDataType constraints,
            IntentExecution execution,
            CancellationToken cancellationToken,
            List<double[]>? jointPath = null)
        {
            Pose3DDataType start = CurrentSnapshot.ToolPose;
            double distance = Distance(start.Position.Span, target.Position.Span);
            var profile = new TrapezoidalVelocityProfile(
                distance, CartesianSpeed(constraints), CartesianAcceleration(constraints));
            SimulatedArmKinematicFailure moveFailure = SimulatedArmKinematicFailure.None;
            IntentOutcome outcome = await FollowProfileAsync(
                profile,
                execution,
                fraction =>
                {
                    Pose3DDataType pose = m_kinematics.InterpolateCartesian(start, target, fraction);
                    if (m_kinematics.TrySelectNearestConfiguration(
                        pose,
                        CurrentSnapshot.JointAngles.Span,
                        out SimulatedArmIkSolution? solution,
                        out SimulatedArmKinematicFailure failure))
                    {
                        SetPose(solution.JointAngles.Span);
                        jointPath?.Add(solution.JointAngles.Span.ToArray());
                    }
                    else
                    {
                        moveFailure = failure;
                    }
                },
                CartesianAcceleration(constraints),
                cancellationToken,
                () => moveFailure != SimulatedArmKinematicFailure.None).ConfigureAwait(false);
            if (moveFailure != SimulatedArmKinematicFailure.None)
            {
                return IntentOutcome.Fail(
                    m_kinematics.MapFailure(moveFailure), "The Cartesian path is not feasible.");
            }
            return outcome.State == ExecutionStateEnum.Succeeded
                ? IntentOutcome.SucceededAt(CurrentSnapshot.ToolPose)
                : outcome;
        }

        private async ValueTask<IntentOutcome> FollowProfileAsync(
            TrapezoidalVelocityProfile profile,
            IntentExecution execution,
            Action<double> apply,
            double acceleration,
            CancellationToken cancellationToken,
            Func<bool>? stopEarly = null)
        {
            if (profile.Duration == 0.0)
            {
                apply(1.0);
                execution.Progress.ReportProgress(1.0);
                execution.Progress.ReportPose(CurrentSnapshot.ToolPose);
                return IntentOutcome.SucceededAt(CurrentSnapshot.ToolPose);
            }

            double elapsed = 0.0;
            double totalDistance = Math.Max(profile.PositionAt(profile.Duration), double.Epsilon);
            while (elapsed < profile.Duration)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                double fraction = Math.Clamp(profile.PositionAt(elapsed) / totalDistance, 0.0, 1.0);
                apply(fraction);
                execution.Progress.ReportProgress(fraction);
                execution.Progress.ReportPose(CurrentSnapshot.ToolPose);
                if (stopEarly?.Invoke() == true)
                {
                    break;
                }
                if (!await DelayTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
                elapsed += TickSeconds;
            }
            if (cancellationToken.IsCancellationRequested && stopEarly?.Invoke() != true)
            {
                await ApplyAcceptedStopAsync(
                    profile,
                    execution,
                    apply,
                    acceleration,
                    elapsed,
                    totalDistance).ConfigureAwait(false);
            }
            if (!cancellationToken.IsCancellationRequested && stopEarly?.Invoke() != true)
            {
                apply(1.0);
                execution.Progress.ReportProgress(1.0);
                execution.Progress.ReportPose(CurrentSnapshot.ToolPose);
            }
            return IntentOutcome.SucceededAt(CurrentSnapshot.ToolPose);
        }

        private async ValueTask ApplyAcceptedStopAsync(
            TrapezoidalVelocityProfile profile,
            IntentExecution execution,
            Action<double> apply,
            double acceleration,
            double elapsed,
            double totalDistance)
        {
            StopModeEnum stopMode = execution.StopMode;
            if (stopMode is StopModeEnum.EndOfInstruction or StopModeEnum.EndOfCycle)
            {
                await FinishCurrentInstructionAsync(profile, execution, apply, elapsed, totalDistance)
                    .ConfigureAwait(false);
                return;
            }

            double effectiveAcceleration = StopAcceleration(acceleration, stopMode);
            double currentPosition = profile.PositionAt(elapsed);
            double velocity = profile.VelocityAt(elapsed);
            double remaining = Math.Max(0.0, totalDistance - currentPosition);
            double stopDistance = Math.Min(remaining, (velocity * velocity) / (2.0 * effectiveAcceleration));
            double duration = velocity / effectiveAcceleration;
            double stopElapsed = 0.0;
            while (stopElapsed < duration && stopDistance > 0.0)
            {
                double travelled = Math.Min(
                    stopDistance,
                    (velocity * stopElapsed) - (0.5 * effectiveAcceleration * stopElapsed * stopElapsed));
                double fraction = Math.Clamp((currentPosition + travelled) / totalDistance, 0.0, 1.0);
                apply(fraction);
                execution.Progress.ReportProgress(fraction);
                execution.Progress.ReportPose(CurrentSnapshot.ToolPose);
                await DelayTickAsync(CancellationToken.None).ConfigureAwait(false);
                stopElapsed += TickSeconds;
            }

            double finalFraction = Math.Clamp((currentPosition + stopDistance) / totalDistance, 0.0, 1.0);
            apply(finalFraction);
            execution.Progress.ReportProgress(finalFraction);
            execution.Progress.ReportPose(CurrentSnapshot.ToolPose);
        }

        private async ValueTask FinishCurrentInstructionAsync(
            TrapezoidalVelocityProfile profile,
            IntentExecution execution,
            Action<double> apply,
            double elapsed,
            double totalDistance)
        {
            double instructionElapsed = elapsed;
            while (instructionElapsed < profile.Duration)
            {
                double fraction = Math.Clamp(profile.PositionAt(instructionElapsed) / totalDistance, 0.0, 1.0);
                apply(fraction);
                execution.Progress.ReportProgress(fraction);
                execution.Progress.ReportPose(CurrentSnapshot.ToolPose);
                await DelayTickAsync(CancellationToken.None).ConfigureAwait(false);
                instructionElapsed += TickSeconds;
            }
            apply(1.0);
            execution.Progress.ReportProgress(1.0);
            execution.Progress.ReportPose(CurrentSnapshot.ToolPose);
        }

        private async ValueTask MoveGripperAsync(
            double targetOpening,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            double start;
            lock (m_lock)
            {
                start = m_gripperOpening;
            }
            const int steps = 12;
            for (int i = 1; i <= steps; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double fraction = (double)i / steps;
                lock (m_lock)
                {
                    m_gripperOpening = start + ((targetOpening - start) * fraction);
                    PublishCurrentPoseLocked();
                }
                SnapshotChanged?.Invoke(this, CurrentSnapshot);
                execution.Progress.ReportProgress(fraction);
                await m_clock.DelayAsync(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask<bool> DelayTickAsync(CancellationToken cancellationToken)
        {
            try
            {
                await m_clock.DelayAsync(TimeSpan.FromSeconds(TickSeconds), cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        private double[] GetJoints()
        {
            lock (m_lock)
            {
                return (double[])m_jointAngles.Clone();
            }
        }

        private void SetJoints(ReadOnlySpan<double> jointAngles)
        {
            lock (m_lock)
            {
                jointAngles.CopyTo(m_jointAngles);
                PublishCurrentPoseLocked();
            }
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
        }

        /// <summary>
        /// Publishes a new snapshot from the given joint angles.
        /// </summary>
        /// <remarks>
        /// The tool pose is derived from the joint angles rather than taken from the pose that
        /// was commanded. The two differ by the inverse-kinematics residual, and publishing the
        /// commanded pose alongside joint frames computed from the solved joints let the tool
        /// centre point and the rendered arm disagree - the twin reported a pose the joints did
        /// not produce.
        /// </remarks>
        /// <param name="jointAngles">The joint angles the arm has reached.</param>
        private void SetPose(ReadOnlySpan<double> jointAngles)
        {
            lock (m_lock)
            {
                jointAngles.CopyTo(m_jointAngles);
                SimulatedArmForwardPose forward = m_kinematics.Forward(m_jointAngles);
                CurrentSnapshot = new SimulatedArmSnapshot(
                    ArrayOf.Create(m_jointAngles.AsSpan()),
                    forward.ToolPose,
                    forward.JointFramePoses,
                    m_gripperOpening,
                    m_hasObject,
                    m_toolName,
                    HeldPartPosition(forward.ToolPose),
                    ArrayOf.Create(m_stackSlotsFilled.AsSpan()),
                    m_heldObjectClass);
            }
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
        }

        [MemberNotNull(nameof(CurrentSnapshot))]
        private void PublishCurrentPoseLocked()
        {
            SimulatedArmForwardPose pose = m_kinematics.Forward(m_jointAngles);
            CurrentSnapshot = new SimulatedArmSnapshot(
                ArrayOf.Create(m_jointAngles.AsSpan()),
                pose.ToolPose,
                pose.JointFramePoses,
                m_gripperOpening,
                m_hasObject,
                m_toolName,
                HeldPartPosition(pose.ToolPose),
                ArrayOf.Create(m_stackSlotsFilled.AsSpan()),
                m_heldObjectClass);
        }

        private void FillNextStackSlotLocked()
        {
            for (int ii = 0; ii < m_stackSlotsFilled.Length; ii++)
            {
                if (!m_stackSlotsFilled[ii])
                {
                    m_stackSlotsFilled[ii] = true;
                    return;
                }
            }
        }

        private void SetNonCancellable(string intentId)
        {
            lock (m_lock)
            {
                m_nonCancellableIntentId = intentId;
            }
        }

        private void ClearNonCancellable(string intentId)
        {
            lock (m_lock)
            {
                if (m_nonCancellableIntentId == intentId)
                {
                    m_nonCancellableIntentId = string.Empty;
                }
            }
        }

        private static Pose3DDataType TranslatePose(Pose3DDataType pose, double x, double y, double z, double distance)
        {
            ReadOnlySpan<double> position = pose.Position.Span;
            return new Pose3DDataType
            {
                FrameId = pose.FrameId,
                Position = ArrayOf.Create([
                    position[0] + (x * distance),
                    position[1] + (y * distance),
                    position[2] + (z * distance)
                ]),
                Orientation = ArrayOf.Create(pose.Orientation.Span)
            };
        }

        private Pose3DDataType InterpolateArc(
            Pose3DDataType start, Pose3DDataType via, Pose3DDataType target, double fraction)
        {
            if (!TryCircle(start.Position.Span, via.Position.Span, target.Position.Span, out Circle circle))
            {
                return m_kinematics.InterpolateCartesian(start, target, fraction);
            }
            double angle = circle.StartAngle + (circle.SweepAngle * Math.Clamp(fraction, 0.0, 1.0));
            return new Pose3DDataType
            {
                FrameId = target.FrameId,
                Position = ArrayOf.Create([
                    circle.CenterX +
                    (circle.Radius * Math.Cos(angle) * circle.BasisUx) +
                    (circle.Radius * Math.Sin(angle) * circle.BasisVx),
                    circle.CenterY +
                    (circle.Radius * Math.Cos(angle) * circle.BasisUy) +
                    (circle.Radius * Math.Sin(angle) * circle.BasisVy),
                    circle.CenterZ +
                    (circle.Radius * Math.Cos(angle) * circle.BasisUz) +
                    (circle.Radius * Math.Sin(angle) * circle.BasisVz)
                ]),
                Orientation = m_kinematics.InterpolateCartesian(start, target, fraction).Orientation
            };
        }

        private static bool TryCircle(
            ReadOnlySpan<double> start, ReadOnlySpan<double> via, ReadOnlySpan<double> end, out Circle circle)
        {
            double ax = via[0] - start[0];
            double ay = via[1] - start[1];
            double az = via[2] - start[2];
            double bx = end[0] - start[0];
            double by = end[1] - start[1];
            double bz = end[2] - start[2];
            double nx = (ay * bz) - (az * by);
            double ny = (az * bx) - (ax * bz);
            double nz = (ax * by) - (ay * bx);
            double n2 = (nx * nx) + (ny * ny) + (nz * nz);
            if (n2 < 1e-10)
            {
                circle = default;
                return false;
            }
            double a2 = (ax * ax) + (ay * ay) + (az * az);
            double b2 = (bx * bx) + (by * by) + (bz * bz);
            double cx = start[0] + (((b2 * ((ay * nz) - (az * ny))) - (a2 * ((by * nz) - (bz * ny)))) / (2.0 * n2));
            double cy = start[1] + (((b2 * ((az * nx) - (ax * nz))) - (a2 * ((bz * nx) - (bx * nz)))) / (2.0 * n2));
            double cz = start[2] + (((b2 * ((ax * ny) - (ay * nx))) - (a2 * ((bx * ny) - (by * nx)))) / (2.0 * n2));
            double ux = start[0] - cx;
            double uy = start[1] - cy;
            double uz = start[2] - cz;
            double radius = Math.Sqrt((ux * ux) + (uy * uy) + (uz * uz));
            ux /= radius;
            uy /= radius;
            uz /= radius;
            double nn = Math.Sqrt(n2);
            nx /= nn;
            ny /= nn;
            nz /= nn;
            double vx = (ny * uz) - (nz * uy);
            double vy = (nz * ux) - (nx * uz);
            double vz = (nx * uy) - (ny * ux);
            double endAngle = Math.Atan2(
                ((end[0] - cx) * vx) + ((end[1] - cy) * vy) + ((end[2] - cz) * vz),
                ((end[0] - cx) * ux) + ((end[1] - cy) * uy) + ((end[2] - cz) * uz));
            double viaAngle = Math.Atan2(
                ((via[0] - cx) * vx) + ((via[1] - cy) * vy) + ((via[2] - cz) * vz),
                ((via[0] - cx) * ux) + ((via[1] - cy) * uy) + ((via[2] - cz) * uz));
            if (endAngle < 0.0)
            {
                endAngle += 2.0 * Math.PI;
            }
            if (viaAngle < 0.0)
            {
                viaAngle += 2.0 * Math.PI;
            }
            double sweep = viaAngle < endAngle ? endAngle : endAngle - (2.0 * Math.PI);
            circle = new Circle(cx, cy, cz, ux, uy, uz, vx, vy, vz, radius, 0.0, sweep);
            return true;
        }

        private static double ArcLength(ReadOnlySpan<double> start, ReadOnlySpan<double> via, ReadOnlySpan<double> end)
        {
            return TryCircle(start, via, end, out Circle circle)
                ? Math.Abs(circle.Radius * circle.SweepAngle)
                : Distance(start, end);
        }

        private static bool IsContact(ReadOnlySpan<double> position)
        {
            if (Math.Abs(position[2] - BenchTopZ) > 0.005)
            {
                return false;
            }
            if (Math.Abs(position[0]) <= 0.7 && Math.Abs(position[1]) <= 0.45)
            {
                return true;
            }
            return IsInsideTarget(position, 0.41, -0.28, 0.04) ||
                IsInsideTarget(position, 0.48, 0.26, 0.04) ||
                IsInsideTarget(position, -0.25, 0.30, 0.035) ||
                IsInsideTarget(position, -0.46, -0.26, 0.04);
        }

        private static bool IsInsideTarget(ReadOnlySpan<double> position, double x, double y, double radius)
        {
            double dx = position[0] - x;
            double dy = position[1] - y;
            return ((dx * dx) + (dy * dy)) <= radius * radius;
        }

        private static double CartesianSpeed(MotionConstraintsDataType constraints)
        {
            double speed = constraints?.CartesianSpeed > 0.0 ? constraints.CartesianSpeed : DefaultCartesianSpeed;
            if (constraints?.SpeedFraction > 0.0)
            {
                speed *= Math.Clamp(constraints.SpeedFraction, 0.05, 1.0);
            }
            return speed;
        }

        private static double CartesianAcceleration(MotionConstraintsDataType constraints)
        {
            return constraints?.CartesianAcceleration > 0.0
                ? constraints.CartesianAcceleration
                : DefaultCartesianAcceleration;
        }

        private static double JointSpeed(MotionConstraintsDataType constraints)
        {
            return constraints?.SpeedFraction > 0.0
                ? DefaultJointSpeed * Math.Clamp(constraints.SpeedFraction, 0.05, 1.0)
                : DefaultJointSpeed;
        }

        private static double StopAcceleration(double acceleration, StopModeEnum stopMode)
        {
            double boundedAcceleration = Math.Max(acceleration, double.Epsilon);
            return stopMode switch
            {
                StopModeEnum.QuickStop => boundedAcceleration,
                StopModeEnum.ProcessStop => boundedAcceleration * 0.5,
                StopModeEnum.OnPath => boundedAcceleration * 0.25,
                _ => boundedAcceleration * 0.35
            };
        }

        private double JointDistance(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
        {
            double sum = 0.0;
            for (int i = 0; i < m_kinematics.AxisCount; i++)
            {
                double delta = a[i] - b[i];
                sum += delta * delta;
            }
            return Math.Sqrt(sum);
        }

        private static double Distance(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
        {
            double dx = a[0] - b[0];
            double dy = a[1] - b[1];
            double dz = a[2] - b[2];
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private static double Distance2D(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
        {
            double x = a[0] - b[0];
            double y = a[1] - b[1];
            return Math.Sqrt((x * x) + (y * y));
        }

        private static ArrayOf<double> HeldPartPosition(Pose3DDataType toolPose)
        {
            ReadOnlySpan<double> position = toolPose.Position.Span;
            return ArrayOf.Create([
                position[0],
                position[1],
                position[2] - HeldPartTcpOffset
            ]);
        }

        private const double DefaultCartesianSpeed = 0.25;

        // How high the tool lifts to before crossing the cell, in the arm's base frame.
        // Above the bin walls and above a full stack on the fixture, so a straight
        // joint-space leg at this height clears the furniture between two work positions.
        private const double TransitHeightMetres = 0.32;
        private const double TransitBypassXMetres = 0.20;
        private const double TransitBypassYMetres = -0.35;
        private const double LocalApproachHeightMetres = 0.04;
        private const int CartesianPlanningSamples = 32;
        private const double RetractEndpointToleranceRadians = 1e-5;
        private const double SameLocationToleranceMetres = 0.01;

        // The rotations about the vertical a Pick or Place may use when the orientation the
        // arm is holding has no clear solution. Zero first, so a target that already works
        // resolves exactly as before, then outwards in both directions.
        private static readonly double[] s_yawOffsetsDegrees =
        [
            0.0, 15.0, -15.0, 30.0, -30.0, 45.0, -45.0, 60.0, -60.0, 75.0, -75.0,
            90.0, -90.0, 105.0, -105.0, 120.0, -120.0, 135.0, -135.0, 150.0, -150.0,
            165.0, -165.0, 180.0
        ];
        private const double DefaultCartesianAcceleration = 0.7;
        private const double DefaultJointSpeed = 0.9;
        private const double DefaultJointAcceleration = 2.0;
        private const double TickSeconds = 0.02;
        private const double BenchTopZ = 0.829;
        private const double GripperOpen = 0.08;
        private const double GripperClosed = 0.018;
        private const int StackSlotCount = 8;

        private readonly System.Threading.Lock m_lock = new();
        private readonly ISimulatedArmKinematics m_kinematics;
        private readonly ISimulatedArmClock m_clock;
        // Home configuration, radians. This arm is mounted on a bench in both samples that
        // use it, so the pose has to keep every joint above the work surface, and in the
        // bin-picking cell it also has to aim the eye-in-hand camera: it is solved so the
        // camera prim lands at the world position the Vision model declares for it
        // (0.38, 0, 1.35) looking straight down, which puts the bin 0.50 m away and 1.8
        // degrees off the optical axis - matching the standoff the detections report.
        //
        // Two constraints on the solution are easy to miss and both were violated by
        // earlier attempts:
        //
        //  - It is the elbow-back branch. The elbow-forward solutions reach the same
        //    camera pose but park a link directly under the camera, and the frame comes
        //    back showing the arm's own upper arm instead of the bin.
        //  - The wrist stays 25 degrees clear of J4 and J6 lining up. Aiming a
        //    straight-down camera from a point on the base's own X-Z plane lands exactly
        //    on that singularity, so the camera roll is tilted 15 degrees to get off it.
        //    A singular home pose is not a cosmetic problem: the first IK solve of any
        //    motion away from home fails, so every intent returns Kinematics.
        private readonly double[] m_jointAngles;
        private double[]? m_retractJointAngles;
        private double[]? m_retractJointEndpoint;
        private List<double[]>? m_retractCartesianPath;
        private double m_gripperOpening = GripperOpen;
        private bool m_hasObject;
        private string m_heldObjectClass = string.Empty;
        private readonly bool[] m_stackSlotsFilled = new bool[StackSlotCount];
        private string m_toolName = "parallel-gripper";
        private string m_nonCancellableIntentId = string.Empty;

        private readonly struct Circle
        {
            public Circle(
                double centerX,
                double centerY,
                double centerZ,
                double basisUx,
                double basisUy,
                double basisUz,
                double basisVx,
                double basisVy,
                double basisVz,
                double radius,
                double startAngle,
                double sweepAngle)
            {
                CenterX = centerX;
                CenterY = centerY;
                CenterZ = centerZ;
                BasisUx = basisUx;
                BasisUy = basisUy;
                BasisUz = basisUz;
                BasisVx = basisVx;
                BasisVy = basisVy;
                BasisVz = basisVz;
                Radius = radius;
                StartAngle = startAngle;
                SweepAngle = sweepAngle;
            }

            public double CenterX { get; }

            public double CenterY { get; }

            public double CenterZ { get; }

            public double BasisUx { get; }

            public double BasisUy { get; }

            public double BasisUz { get; }

            public double BasisVx { get; }

            public double BasisVy { get; }

            public double BasisVz { get; }

            public double Radius { get; }

            public double StartAngle { get; }

            public double SweepAngle { get; }
        }
    }
}
