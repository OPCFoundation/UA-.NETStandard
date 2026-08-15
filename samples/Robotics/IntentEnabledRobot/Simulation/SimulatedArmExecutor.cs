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
using System.Diagnostics.CodeAnalysis;
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
            ArrayOf<bool> stackSlotsFilled)
        {
            JointAngles = jointAngles;
            ToolPose = toolPose;
            JointFramePoses = jointFramePoses;
            GripperOpening = gripperOpening;
            HasObject = hasObject;
            ToolName = toolName;
            HeldPartPosition = heldPartPosition;
            StackSlotsFilled = stackSlotsFilled;
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
        /// Initializes a simulated executor.
        /// </summary>
        public SimulatedArmExecutor(SimulatedArmKinematics kinematics, ISimulatedArmClock clock)
        {
            m_kinematics = kinematics ?? throw new ArgumentNullException(nameof(kinematics));
            m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
            PublishCurrentPoseLocked();
        }

        /// <summary>
        /// Raised after the observable snapshot changes.
        /// </summary>
        public event EventHandler<SimulatedArmSnapshot>? SnapshotChanged;

        /// <summary>
        /// Resolves a Location NodeId to the position, in this arm's base frame, that a
        /// Pick or Place should travel to before actuating the gripper. Optional: leave it
        /// unset and both intents actuate the gripper where the arm already stands.
        /// </summary>
        public LocationPositionResolver? ResolveLocationPosition { get; set; }

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
                if (intent.JointTargets.Count != SimulatedArmKinematics.JointCount ||
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
                    SimulatedArmKinematics.ToIntentFailure(failure), "The target pose cannot be reached.");
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
                    if (m_kinematics.TrySelectNearest(
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
                    SimulatedArmKinematics.ToIntentFailure(moveFailure), "The circular path is not feasible.");
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
                    if (m_kinematics.TrySelectNearest(
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
                    SimulatedArmKinematics.ToIntentFailure(moveFailure), "The force path is not feasible.");
            }
            return contacted
                ? IntentOutcome.SucceededAt(CurrentSnapshot.ToolPose)
                : IntentOutcome.Fail(
                    IntentFailureEnum.ObjectNotFound, "No contact occurred before MaxDistance was exhausted.");
        }

        private async ValueTask<IntentOutcome> ExecuteGraspAsync(
            GraspIntentDataType intent,
            IntentExecution execution,
            CancellationToken cancellationToken)
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
            await m_clock.DelayAsync(TimeSpan.FromMilliseconds(80), cancellationToken).ConfigureAwait(false);
            await MoveToLocationAsync(intent.Source, execution, cancellationToken).ConfigureAwait(false);
            return await ExecuteGraspAsync(
                new GraspIntentDataType { Force = intent.Force, Width = GripperClosed, Tool = intent.Tool },
                execution,
                cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<IntentOutcome> ExecutePlaceAsync(
            PlaceIntentDataType intent,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            await m_clock.DelayAsync(TimeSpan.FromMilliseconds(80), cancellationToken).ConfigureAwait(false);
            await MoveToLocationAsync(intent.Destination, execution, cancellationToken).ConfigureAwait(false);
            return await ExecuteReleaseAsync(
                new ReleaseIntentDataType(), execution, cancellationToken).ConfigureAwait(false);
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
        /// The travel is best effort. A host that supplies no resolver, or a Location the
        /// arm cannot reach, still gets the gripper action: the grasp is what the intent
        /// guarantees, and failing a pick because the scenery is out of reach would be a
        /// worse answer than picking where the arm already stands.
        /// </para>
        /// </remarks>
        private async ValueTask MoveToLocationAsync(
            NodeId location,
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            if (ResolveLocationPosition == null
                || location.IsNull
                || !ResolveLocationPosition(location, out ArrayOf<double> position)
                || position.Count < 3)
            {
                return;
            }
            double[] start = GetJoints();
            Pose3DDataType current = CurrentSnapshot.ToolPose;
            var target = new Pose3DDataType
            {
                FrameId = current.FrameId,
                Position = position,
                Orientation = current.Orientation
            };

            // Solve once and travel in joint space. Interpolating in Cartesian space would
            // re-solve at every step and abandon the move the first time the straight line
            // between here and there passes through a pose the arm cannot hold, which for a
            // reach across a bench it usually does.
            if (!m_kinematics.TrySelectNearest(
                target,
                start,
                out SimulatedArmIkSolution? solution,
                out SimulatedArmKinematicFailure _))
            {
                return;
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
            CancellationToken cancellationToken)
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
                    if (m_kinematics.TrySelectNearest(
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
                CartesianAcceleration(constraints),
                cancellationToken,
                () => moveFailure != SimulatedArmKinematicFailure.None).ConfigureAwait(false);
            if (moveFailure != SimulatedArmKinematicFailure.None)
            {
                return IntentOutcome.Fail(
                    SimulatedArmKinematics.ToIntentFailure(moveFailure), "The Cartesian path is not feasible.");
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
                    ArrayOf.Create(m_stackSlotsFilled.AsSpan()));
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
                ArrayOf.Create(m_stackSlotsFilled.AsSpan()));
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

        private static double JointDistance(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
        {
            double sum = 0.0;
            for (int i = 0; i < SimulatedArmKinematics.JointCount; i++)
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
        private const double DefaultCartesianAcceleration = 0.7;
        private const double DefaultJointSpeed = 0.9;
        private const double DefaultJointAcceleration = 2.0;
        private const double TickSeconds = 0.02;
        private const double BenchTopZ = 0.829;
        private const double GripperOpen = 0.08;
        private const double GripperClosed = 0.018;
        private const double HeldPartTcpOffset = 0.035;
        private const int StackSlotCount = 8;

        private readonly System.Threading.Lock m_lock = new();
        private readonly SimulatedArmKinematics m_kinematics;
        private readonly ISimulatedArmClock m_clock;
        // Home configuration, radians. This arm is mounted on a bench in both samples that
        // use it, so the pose has to keep every joint above the work surface: the previous
        // configuration folded the elbow to z = 0.646 in the bin-picking cell, 183 mm under
        // a 0.829 m bench, and the OpenUSD viewport rendered the forearm through the table.
        // This one is the scan pose those cells already document - it puts the flange exactly
        // at the Vision model's authored flange transform, looking down at the bin, with
        // 162 mm of clearance under the lowest joint.
        private readonly double[] m_jointAngles = [-0.1932, 2.0564, 0.0096, 1.3123, 0.4134, 0.0000];
        private double m_gripperOpening = GripperOpen;
        private bool m_hasObject;
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
