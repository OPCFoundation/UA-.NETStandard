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
using Opc.Ua;
using Opc.Ua.RobotIntent;
using Robotics.IntentEnabledRobot.Simulation;

namespace Robotics.IntentEnabledRobot.Kinematics
{
    /// <summary>
    /// Classifies why a target cannot be reached by the simulated arm.
    /// </summary>
    public enum SimulatedArmKinematicFailure
    {
        /// <summary>
        /// No failure was detected.
        /// </summary>
        None,

        /// <summary>
        /// The target is outside the arm workspace.
        /// </summary>
        Unreachable,

        /// <summary>
        /// No kinematic solution exists, or the target is singular for this solver.
        /// </summary>
        Kinematics,

        /// <summary>
        /// Every geometric solution violates at least one joint limit.
        /// </summary>
        JointLimit,

        /// <summary>
        /// Every geometric solution drives a link through the work surface the arm is
        /// mounted on.
        /// </summary>
        WorkSurface
    }

    /// <summary>
    /// Resolves a Location NodeId to a position in the arm's base frame.
    /// </summary>
    /// <param name="location">The Location NodeId carried by a Pick or Place intent.</param>
    /// <param name="position">The resolved position (x, y, z) in metres.</param>
    /// <returns><c>true</c> when the location is known to the host.</returns>
    public delegate bool LocationPositionResolver(NodeId location, out ArrayOf<double> position);

    /// <summary>
    /// Resolves a Location NodeId to a complete tool pose in the arm's base frame.
    /// </summary>
    /// <param name="location">The Location NodeId carried by a Pick or Place intent.</param>
    /// <param name="pose">The resolved tool pose.</param>
    /// <returns><c>true</c> when the location is known to the host.</returns>
    public delegate bool LocationPoseResolver(NodeId location, out Pose3DDataType pose);

    /// <summary>
    /// Resolves a named workpiece at a Pick source to a tool pose.
    /// </summary>
    public delegate bool PickPoseResolver(
        NodeId source,
        string objectClass,
        out Pose3DDataType pose);

    /// <summary>
    /// One inverse-kinematic solution for the simulated arm.
    /// </summary>
    public sealed class SimulatedArmIkSolution
    {
        /// <summary>
        /// Initializes a solution.
        /// </summary>
        /// <param name="jointAngles">
        /// Joint angles in radians, ordered J1 through J6.
        /// </param>
        /// <param name="travelCost">
        /// Weighted travel from the reference configuration.
        /// </param>
        public SimulatedArmIkSolution(ArrayOf<double> jointAngles, double travelCost)
        {
            JointAngles = jointAngles;
            TravelCost = travelCost;
        }

        /// <summary>
        /// Gets joint angles in radians, ordered J1 through J6.
        /// </summary>
        public ArrayOf<double> JointAngles { get; }

        /// <summary>
        /// Gets the weighted travel from the reference configuration.
        /// </summary>
        public double TravelCost { get; }
    }

    /// <summary>
    /// Result of an inverse-kinematics solve.
    /// </summary>
    public sealed class SimulatedArmIkResult
    {
        /// <summary>
        /// Initializes a result.
        /// </summary>
        public SimulatedArmIkResult(
            SimulatedArmKinematicFailure failure,
            string message,
            ArrayOf<SimulatedArmIkSolution> solutions)
        {
            Failure = failure;
            Message = message;
            Solutions = solutions;
        }

        /// <summary>
        /// Gets the failure classification.
        /// </summary>
        public SimulatedArmKinematicFailure Failure { get; }

        /// <summary>
        /// Gets a human-readable diagnostic.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the valid joint-limit-filtered solutions.
        /// </summary>
        public ArrayOf<SimulatedArmIkSolution> Solutions { get; }

        /// <summary>
        /// Gets a value indicating whether at least one solution is available.
        /// </summary>
        public bool Succeeded => !Solutions.IsEmpty;
    }

    /// <summary>
    /// Forward-kinematic pose of the simulated arm.
    /// </summary>
    public sealed class SimulatedArmForwardPose
    {
        /// <summary>
        /// Initializes a forward-kinematic pose.
        /// </summary>
        public SimulatedArmForwardPose(Pose3DDataType toolPose, ArrayOf<Pose3DDataType> jointFramePoses)
        {
            ToolPose = toolPose;
            JointFramePoses = jointFramePoses;
        }

        /// <summary>
        /// Gets the tool-centre-point pose in the arm base frame.
        /// </summary>
        public Pose3DDataType ToolPose { get; }

        /// <summary>
        /// Gets the six USD joint-frame poses in base coordinates.
        /// </summary>
        public ArrayOf<Pose3DDataType> JointFramePoses { get; }
    }

    /// <summary>
    /// Interpolates and profiles scalar path distance for the simulated arm.
    /// </summary>
    public sealed class TrapezoidalVelocityProfile
    {
        /// <summary>
        /// Initializes a trapezoidal or triangular profile.
        /// </summary>
        public TrapezoidalVelocityProfile(double distance, double maximumSpeed, double maximumAcceleration)
        {
            if (distance < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(distance));
            }
            if (maximumSpeed <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSpeed));
            }
            if (maximumAcceleration <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumAcceleration));
            }

            m_distance = distance;
            m_acceleration = maximumAcceleration;
            double accelTime = maximumSpeed / maximumAcceleration;
            double accelDistance = 0.5 * maximumAcceleration * accelTime * accelTime;
            if ((2.0 * accelDistance) >= distance)
            {
                m_accelTime = Math.Sqrt(distance / maximumAcceleration);
                m_peakSpeed = maximumAcceleration * m_accelTime;
                m_cruiseTime = 0.0;
            }
            else
            {
                m_accelTime = accelTime;
                m_peakSpeed = maximumSpeed;
                m_cruiseTime = (distance - (2.0 * accelDistance)) / maximumSpeed;
            }
        }

        /// <summary>
        /// Gets the total profile duration in seconds.
        /// </summary>
        public double Duration => (2.0 * m_accelTime) + m_cruiseTime;

        /// <summary>
        /// Samples path position in metres at elapsed seconds.
        /// </summary>
        public double PositionAt(double elapsedSeconds)
        {
            double t = Math.Clamp(elapsedSeconds, 0.0, Duration);
            if (Duration == 0.0)
            {
                return 0.0;
            }
            if (t <= m_accelTime)
            {
                return 0.5 * m_acceleration * t * t;
            }
            double accelDistance = 0.5 * m_acceleration * m_accelTime * m_accelTime;
            if (t <= m_accelTime + m_cruiseTime)
            {
                return accelDistance + (m_peakSpeed * (t - m_accelTime));
            }
            double decelTime = t - m_accelTime - m_cruiseTime;
            return Math.Min(
                m_distance,
                accelDistance +
                (m_peakSpeed * m_cruiseTime) +
                (m_peakSpeed * decelTime) -
                (0.5 * m_acceleration * decelTime * decelTime));
        }

        /// <summary>
        /// Samples path speed in metres per second at elapsed seconds.
        /// </summary>
        public double VelocityAt(double elapsedSeconds)
        {
            double t = Math.Clamp(elapsedSeconds, 0.0, Duration);
            if (t <= m_accelTime)
            {
                return m_acceleration * t;
            }
            if (t <= m_accelTime + m_cruiseTime)
            {
                return m_peakSpeed;
            }
            return Math.Max(0.0, m_peakSpeed - (m_acceleration * (t - m_accelTime - m_cruiseTime)));
        }

        private readonly double m_distance;
        private readonly double m_acceleration;
        private readonly double m_peakSpeed;
        private readonly double m_accelTime;
        private readonly double m_cruiseTime;
    }

    /// <summary>
    /// Kinematics and path helpers for the UR5e-style sample arm.
    /// </summary>
    public sealed class SimulatedArmKinematics : ISimulatedArmKinematics
    {
        /// <inheritdoc/>
        public int AxisCount => JointCount;

        /// <inheritdoc/>
        public double MaximumReach => Reach;

        /// <inheritdoc/>
        public ArrayOf<double> InitialJointAngles => ArrayOf.Create(s_initialJointAngles.AsSpan());

        /// <summary>
        /// Initializes kinematics with the sample joint limits.
        /// </summary>
        public SimulatedArmKinematics()
            : this(ArrayOf.Create(s_defaultMinimumLimits.AsSpan()), ArrayOf.Create(s_defaultMaximumLimits.AsSpan()))
        {
        }

        /// <summary>
        /// Initializes kinematics with explicit joint limits.
        /// </summary>
        public SimulatedArmKinematics(ArrayOf<double> minimumLimits, ArrayOf<double> maximumLimits)
        {
            RequireJointCount(minimumLimits.Span);
            RequireJointCount(maximumLimits.Span);
            m_minimumLimits = minimumLimits.Span.ToArray();
            m_maximumLimits = maximumLimits.Span.ToArray();
        }

        /// <summary>
        /// Computes forward kinematics for joint angles in radians.
        /// </summary>
        public SimulatedArmForwardPose Forward(ReadOnlySpan<double> jointAngles)
        {
            RequireJointCount(jointAngles);
            Transform transform = Transform.Identity;
            var poses = new Pose3DDataType[JointCount];

            transform = transform * Transform.RotateZ(jointAngles[0]) * Transform.Translate(0.0, 0.0, D1);
            poses[0] = ToPose(transform);
            transform *= Transform.RotateY(jointAngles[1]);
            poses[1] = ToPose(transform);
            transform = transform * Transform.Translate(A2, 0.0, 0.0) * Transform.RotateY(jointAngles[2]);
            poses[2] = ToPose(transform);
            transform = transform * Transform.Translate(A3, 0.0, D4) * Transform.RotateY(jointAngles[3]);
            poses[3] = ToPose(transform);
            transform = transform * Transform.Translate(0.0, 0.0, D5) * Transform.RotateZ(jointAngles[4]);
            poses[4] = ToPose(transform);
            transform = transform * Transform.Translate(0.0, 0.0, D6) * Transform.RotateY(jointAngles[5]);
            poses[5] = ToPose(transform);
            transform *= Transform.Translate(FlangeToTcp, 0.0, 0.0);

            return new SimulatedArmForwardPose(ToPose(transform), ArrayOf.Create(poses.AsSpan()));
        }

        /// <summary>
        /// Computes forward kinematics for joint angles in radians.
        /// </summary>
        public SimulatedArmForwardPose Forward(ArrayOf<double> jointAngles)
        {
            return Forward(jointAngles.Span);
        }

        /// <summary>
        /// Computes all valid inverse-kinematic solutions found from the eight UR-style branches.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public SimulatedArmIkResult Inverse(Pose3DDataType target, ReadOnlySpan<double> referenceJointAngles)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            RequireJointCount(referenceJointAngles);
            ReadOnlySpan<double> position = target.Position.Span;
            if (target.Position.Count != 3 || target.Orientation.Count != 4)
            {
                return new SimulatedArmIkResult(
                    SimulatedArmKinematicFailure.Kinematics,
                    "The target pose must carry a 3D position and quaternion orientation.",
                    []);
            }
            double distanceFromShoulder = Norm(position[0], position[1], position[2] - D1);
            if (distanceFromShoulder > Reach + D4 + D5 + D6 + FlangeToTcp)
            {
                return new SimulatedArmIkResult(
                    SimulatedArmKinematicFailure.Unreachable,
                    "The target lies outside the reachable workspace.",
                    []);
            }

            var all = new List<SimulatedArmIkSolution>();
            int geometricCandidates = 0;
            Span<double> seed = stackalloc double[JointCount];
            foreach (double[] template in s_seedTemplates)
            {
                for (int i = 0; i < JointCount; i++)
                {
                    seed[i] = NormalizeNear(template[i], referenceJointAngles[i]);
                }
                double shoulder = Math.Atan2(position[1], position[0]);
                seed[0] = NormalizeNear(shoulder + NormalizeNear(template[0], 0.0), referenceJointAngles[0]);
                if (TryRefine(target, seed, out double[] solution))
                {
                    geometricCandidates++;
                    if (IsWithinLimits(solution))
                    {
                        AddDistinct(all, solution, referenceJointAngles);
                    }
                }
            }

            if (all.Count == 0)
            {
                SimulatedArmKinematicFailure failure = geometricCandidates == 0
                    ? SimulatedArmKinematicFailure.Kinematics
                    : SimulatedArmKinematicFailure.JointLimit;
                string message = failure == SimulatedArmKinematicFailure.JointLimit
                    ? "All kinematic solutions exceed a joint limit."
                    : "No kinematic solution was found, or the target is singular.";
                return new SimulatedArmIkResult(failure, message, []);
            }

            ExpandPeriodicVariants(all, referenceJointAngles);
            all.Sort((left, right) => left.TravelCost.CompareTo(right.TravelCost));
            return new SimulatedArmIkResult(SimulatedArmKinematicFailure.None, string.Empty, all.ToArrayOf());
        }

        /// <summary>
        /// Computes all valid inverse-kinematic solutions found from the eight UR-style branches.
        /// </summary>
        public SimulatedArmIkResult Inverse(Pose3DDataType target, ArrayOf<double> referenceJointAngles)
        {
            return Inverse(target, referenceJointAngles.Span);
        }

        /// <summary>
        /// Selects the solution with minimum weighted joint travel.
        /// </summary>
        public bool TrySelectNearest(
            Pose3DDataType target,
            ReadOnlySpan<double> currentJointAngles,
            [NotNullWhen(true)] out SimulatedArmIkSolution? solution,
            out SimulatedArmKinematicFailure failure)
        {
            return TrySelectNearestCore(
                target,
                currentJointAngles,
                requireClearJointPath: true,
                out solution,
                out failure);
        }

        /// <summary>
        /// Selects the nearest clear configuration without testing a joint-space path to
        /// it.
        /// </summary>
        /// <remarks>
        /// Used while following an explicitly sampled Cartesian path. Each sample is checked
        /// for collision, and selecting the nearest solution to the previous sample keeps
        /// the branch continuous. Testing a second, joint-interpolated path between those
        /// samples rejects valid Cartesian motion and is the wrong path to validate.
        /// </remarks>
        public bool TrySelectNearestConfiguration(
            Pose3DDataType target,
            ReadOnlySpan<double> currentJointAngles,
            [NotNullWhen(true)] out SimulatedArmIkSolution? solution,
            out SimulatedArmKinematicFailure failure)
        {
            return TrySelectNearestCore(
                target,
                currentJointAngles,
                requireClearJointPath: false,
                out solution,
                out failure);
        }

        private bool TrySelectNearestCore(
            Pose3DDataType target,
            ReadOnlySpan<double> currentJointAngles,
            bool requireClearJointPath,
            [NotNullWhen(true)] out SimulatedArmIkSolution? solution,
            out SimulatedArmKinematicFailure failure)
        {
            SimulatedArmIkResult result = Inverse(target, currentJointAngles);
            failure = result.Failure;
            solution = null;
            if (result.Solutions.IsEmpty)
            {
                return false;
            }

            // Solutions come back nearest-first. Take the nearest one that neither reaches
            // through the surface the arm stands on nor into the cell's furniture, and that
            // can be reached without sweeping a link through either when the caller asks us
            // to validate a joint-space path.
            //
            // Ordering these by WristInversionPenalty first - so a shape that holds the
            // wrist the right way up wins over a closer one that doubles it back - was
            // tried and measured worse: the loop failed on its third operation against nine
            // of ten without it. Choosing a different solution changes where the arm starts
            // the next move from, and the tidier shape led into dead ends. The penalty is
            // kept because it names what is wrong with the posture in the close-ups, but
            // preferring it needs the approach and retract poses solved for properly first,
            // so that a tidy choice does not strand the next one.
            ReadOnlySpan<SimulatedArmIkSolution> candidates = result.Solutions.Span;
            for (int ii = 0; ii < candidates.Length; ii++)
            {
                if (ClearsWorkSurface(candidates[ii].JointAngles.Span) &&
                    (!requireClearJointPath ||
                        ClearsPath(currentJointAngles, candidates[ii].JointAngles.Span)))
                {
                    solution = candidates[ii];
                    return true;
                }
            }

            // Refusing is the honest answer. Returning the first solution anyway would put
            // a link through the bench, and a move that cannot be made without doing that
            // is one the arm should decline rather than mime.
            failure = SimulatedArmKinematicFailure.WorkSurface;
            return false;
        }

        /// <summary>
        /// Gets how far a configuration has the wrist the wrong way up.
        /// </summary>
        /// <remarks>
        /// With the tool pointing down the chain should descend from the wrist to the part:
        /// J4 above J5 above J6 above the tool centre point. A configuration that climbs
        /// instead has doubled the wrist back over itself, which reaches the same point and
        /// looks like a fault. Counting the steps that climb gives an order to prefer
        /// between candidates that are otherwise all legal, and zero means the wrist hangs
        /// the way a person would expect.
        /// </remarks>
        /// <param name="jointAngles">
        /// The configuration to score, in radians.
        /// </param>
        public int WristInversionPenalty(ReadOnlySpan<double> jointAngles)
        {
            SimulatedArmForwardPose pose = Forward(jointAngles);
            ReadOnlySpan<Pose3DDataType> frames = pose.JointFramePoses.Span;
            if (frames.Length < JointCount)
            {
                return 0;
            }
            double toolZ = pose.ToolPose.Position.Span[2];
            int penalty = 0;
            for (int ii = 3; ii < JointCount - 1; ii++)
            {
                if (frames[ii + 1].Position.Span[2] > frames[ii].Position.Span[2])
                {
                    penalty++;
                }
            }
            if (toolZ > frames[JointCount - 1].Position.Span[2])
            {
                penalty++;
            }
            return penalty;
        }

        /// <summary>
        /// Gets or sets the lowest height, in the arm's own base frame, that any joint
        /// origin may occupy. Defaults to no constraint.
        /// </summary>
        /// <remarks>
        /// An arm bolted to a bench has the bench at zero in its base frame, so a host that
        /// mounts it that way sets this to zero and the solver stops handing back poses
        /// that pass through the work surface.
        /// </remarks>
        public double MinimumLinkHeight { get; set; } = double.NegativeInfinity;

        /// <summary>
        /// Gets or sets the solids the arm must not move through. Defaults to none.
        /// </summary>
        /// <remarks>
        /// <see cref="MinimumLinkHeight"/> only knows about a horizontal plane and only
        /// samples joint origins, so it cannot see a link crossing the middle of a bench,
        /// and it does not know the bin or the fixture are there at all. A host that
        /// describes its furniture here gets configurations refused for reaching into any
        /// of it.
        /// </remarks>
        public SimulatedCollisionModel? Collisions { get; set; }

        /// <summary>
        /// Gets a value indicating whether a configuration keeps the whole arm out of the
        /// work surface and out of every declared obstacle.
        /// </summary>
        /// <param name="jointAngles">
        /// The configuration to test, in radians.
        /// </param>
        /// <returns>
        /// <c>true</c> when no part of the arm reaches below the work surface or inside a
        /// solid.
        /// </returns>
        public bool ClearsWorkSurface(ReadOnlySpan<double> jointAngles)
        {
            if (double.IsNegativeInfinity(MinimumLinkHeight) && Collisions == null)
            {
                return true;
            }
            SimulatedArmForwardPose pose = Forward(jointAngles);
            ReadOnlySpan<Pose3DDataType> frames = pose.JointFramePoses.Span;
            if (!double.IsNegativeInfinity(MinimumLinkHeight))
            {
                for (int ii = 0; ii < frames.Length; ii++)
                {
                    if (frames[ii].Position.Span[2] < MinimumLinkHeight)
                    {
                        return false;
                    }
                }
                if (pose.ToolPose.Position.Span[2] < MinimumLinkHeight)
                {
                    return false;
                }
            }
            if (Collisions == null)
            {
                return true;
            }

            // The chain starts at the first joint origin, not at the base: the arm is
            // bolted to the bench, so a capsule around the pedestal is inside the bench by
            // construction and would refuse every configuration there is. The tool point
            // closes the chain past the flange - a wrist that clears everything while the
            // gripper is buried in the bin is not a configuration the arm can hold.
            Span<double> points = stackalloc double[(frames.Length + 1) * 3];
            for (int ii = 0; ii < frames.Length; ii++)
            {
                ReadOnlySpan<double> position = frames[ii].Position.Span;
                points[(ii * 3) + 0] = position[0];
                points[(ii * 3) + 1] = position[1];
                points[(ii * 3) + 2] = position[2];
            }
            ReadOnlySpan<double> tool = pose.ToolPose.Position.Span;
            points[(frames.Length * 3) + 0] = tool[0];
            points[(frames.Length * 3) + 1] = tool[1];
            points[(frames.Length * 3) + 2] = tool[2];
            return Collisions.IsClear(points, out _);
        }

        /// <summary>
        /// Gets a value indicating whether every configuration along a joint-space move
        /// stays clear.
        /// </summary>
        /// <remarks>
        /// Filtering the goal alone is not enough: the arm travels by interpolating from
        /// where it is to where it is going, so a start and a goal that both clear the
        /// bench can still be joined by a path that sweeps a link straight through it.
        /// </remarks>
        /// <param name="start">
        /// The configuration the move starts from, in radians.
        /// </param>
        /// <param name="target">
        /// The configuration the move ends at, in radians.
        /// </param>
        public bool ClearsPath(ReadOnlySpan<double> start, ReadOnlySpan<double> target)
        {
            // Only checked when a host has described its furniture. The height plane alone
            // is too blunt for a path: a swing from one side of the cell to the other dips
            // a link below the plane part-way round almost every time, so enforcing it here
            // refuses ordinary moves and the arm stops rather than travels.
            if (Collisions == null)
            {
                return true;
            }
            Span<double> configuration = stackalloc double[start.Length];
            for (int step = 0; step <= PathSampleCount; step++)
            {
                double fraction = (double)step / PathSampleCount;
                for (int ii = 0; ii < start.Length && ii < target.Length; ii++)
                {
                    configuration[ii] = start[ii] + ((target[ii] - start[ii]) * fraction);
                }
                if (!ClearsWorkSurface(configuration))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Interpolates between poses with straight-line position and spherical-linear orientation.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public Pose3DDataType InterpolateCartesian(Pose3DDataType start, Pose3DDataType end, double fraction)
        {
            if (start is null)
            {
                throw new ArgumentNullException(nameof(start));
            }
            if (end is null)
            {
                throw new ArgumentNullException(nameof(end));
            }
            double t = Math.Clamp(fraction, 0.0, 1.0);
            ReadOnlySpan<double> a = start.Position.Span;
            ReadOnlySpan<double> b = end.Position.Span;
            return new Pose3DDataType
            {
                FrameId = string.IsNullOrEmpty(end.FrameId) ? start.FrameId : end.FrameId,
                Position = ArrayOf.Create([
                    Lerp(a[0], b[0], t),
                    Lerp(a[1], b[1], t),
                    Lerp(a[2], b[2], t)
                ]),
                Orientation = Slerp(start.Orientation.Span, end.Orientation.Span, t)
            };
        }

        /// <summary>
        /// Interpolates joint angles in radians.
        /// </summary>
        public ArrayOf<double> InterpolateJoints(
            ReadOnlySpan<double> start,
            ReadOnlySpan<double> end,
            double fraction)
        {
            RequireJointCount(start);
            RequireJointCount(end);
            double t = Math.Clamp(fraction, 0.0, 1.0);
            return ArrayOf.Create([
                Lerp(start[0], end[0], t),
                Lerp(start[1], end[1], t),
                Lerp(start[2], end[2], t),
                Lerp(start[3], end[3], t),
                Lerp(start[4], end[4], t),
                Lerp(start[5], end[5], t)
            ]);
        }

        /// <summary>
        /// Converts a kinematic failure to the Robot Intent failure enum.
        /// </summary>
        public static IntentFailureEnum ToIntentFailure(SimulatedArmKinematicFailure failure)
        {
            return failure switch
            {
                SimulatedArmKinematicFailure.Unreachable => IntentFailureEnum.Unreachable,
                SimulatedArmKinematicFailure.JointLimit => IntentFailureEnum.JointLimit,
                SimulatedArmKinematicFailure.Kinematics => IntentFailureEnum.Kinematics,
                _ => IntentFailureEnum.None
            };
        }

        /// <inheritdoc/>
        public IntentFailureEnum MapFailure(SimulatedArmKinematicFailure failure)
        {
            return ToIntentFailure(failure);
        }

        /// <summary>
        /// Gets a value indicating whether the joint vector is within configured limits.
        /// </summary>
        public bool IsWithinLimits(ReadOnlySpan<double> jointAngles)
        {
            RequireJointCount(jointAngles);
            for (int i = 0; i < JointCount; i++)
            {
                if (jointAngles[i] < m_minimumLimits[i] || jointAngles[i] > m_maximumLimits[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static void AddDistinct(
            List<SimulatedArmIkSolution> solutions,
            ReadOnlySpan<double> candidate,
            ReadOnlySpan<double> reference)
        {
            double[] normalized = new double[JointCount];
            for (int i = 0; i < JointCount; i++)
            {
                normalized[i] = NormalizeNear(candidate[i], reference[i]);
            }
            foreach (SimulatedArmIkSolution existing in solutions)
            {
                double max = 0.0;
                for (int i = 0; i < JointCount; i++)
                {
                    max = Math.Max(
                        max, Math.Abs(NormalizeNear(existing.JointAngles[i], normalized[i]) - normalized[i]));
                }
                if (max < 1e-3)
                {
                    return;
                }
            }
            solutions.Add(new SimulatedArmIkSolution(
                ArrayOf.Create(normalized.AsSpan()), Travel(normalized, reference)));
        }

        private void ExpandPeriodicVariants(List<SimulatedArmIkSolution> solutions, ReadOnlySpan<double> reference)
        {
            foreach (SimulatedArmIkSolution baseSolution in (SimulatedArmIkSolution[])[.. solutions])
            {
                for (int shoulderTurn = -1; shoulderTurn <= 1; shoulderTurn++)
                {
                    for (int wristTurn = -1; wristTurn <= 1; wristTurn++)
                    {
                        if (shoulderTurn == 0 && wristTurn == 0)
                        {
                            continue;
                        }
                        double[] candidate = baseSolution.JointAngles.Span.ToArray();
                        candidate[0] += shoulderTurn * TwoPi;
                        candidate[5] += wristTurn * TwoPi;
                        AddDistinctRaw(solutions, candidate, reference);
                    }
                }
            }
            for (int source = 0; source < solutions.Count && solutions.Count < 8; source++)
            {
                for (int joint = 0; joint < JointCount && solutions.Count < 8; joint++)
                {
                    for (int turn = -1; turn <= 1 && solutions.Count < 8; turn += 2)
                    {
                        double[] candidate = solutions[source].JointAngles.Span.ToArray();
                        candidate[joint] += turn * TwoPi;
                        AddDistinctRaw(solutions, candidate, reference);
                    }
                }
            }
        }

        private void AddDistinctRaw(
            List<SimulatedArmIkSolution> solutions,
            ReadOnlySpan<double> candidate,
            ReadOnlySpan<double> reference)
        {
            if (!IsWithinLimits(candidate))
            {
                return;
            }
            foreach (SimulatedArmIkSolution existing in solutions)
            {
                if (MaxAbsoluteDifference(existing.JointAngles.Span, candidate) < 1e-3)
                {
                    return;
                }
            }
            solutions.Add(new SimulatedArmIkSolution(ArrayOf.Create(candidate), Travel(candidate, reference)));
        }

        private bool TryRefine(Pose3DDataType target, ReadOnlySpan<double> seed, out double[] solution)
        {
            solution = seed.ToArray();
            Span<double> error = stackalloc double[6];
            Span<double> trial = stackalloc double[JointCount];
            Span<double> trialError = stackalloc double[6];
            Span<double> delta = stackalloc double[JointCount];
            const double step = 1e-5;
            const double damping = 1e-4;
            double[,] jacobian = new double[6, JointCount];
            double[,] normal = new double[JointCount, JointCount];
            double[] right = new double[JointCount];

            for (int iteration = 0; iteration < 80; iteration++)
            {
                ComputeError(target, solution, error);
                if (Math.Sqrt((error[0] * error[0]) + (error[1] * error[1]) + (error[2] * error[2])) <
                        PositionTolerance &&
                    Math.Sqrt((error[3] * error[3]) + (error[4] * error[4]) + (error[5] * error[5])) <
                        OrientationTolerance)
                {
                    return !IsWristSingular(solution);
                }

                for (int joint = 0; joint < JointCount; joint++)
                {
                    solution.CopyTo(trial);
                    trial[joint] += step;
                    ComputeError(target, trial, trialError);
                    for (int row = 0; row < 6; row++)
                    {
                        jacobian[row, joint] = (trialError[row] - error[row]) / step;
                    }
                }

                Array.Clear(normal);
                Array.Clear(right);
                for (int row = 0; row < 6; row++)
                {
                    for (int col = 0; col < JointCount; col++)
                    {
                        right[col] -= jacobian[row, col] * error[row];
                        for (int col2 = 0; col2 < JointCount; col2++)
                        {
                            normal[col, col2] += jacobian[row, col] * jacobian[row, col2];
                        }
                    }
                }
                for (int joint = 0; joint < JointCount; joint++)
                {
                    normal[joint, joint] += damping;
                }
                if (!SolveNormalEquation(normal, right, delta))
                {
                    return false;
                }
                for (int joint = 0; joint < JointCount; joint++)
                {
                    solution[joint] = NormalizeNear(
                        solution[joint] + Math.Clamp(delta[joint], -0.35, 0.35), seed[joint]);
                }
            }
            return false;
        }

        private static bool SolveNormalEquation(double[,] matrix, double[] right, Span<double> solution)
        {
            const int n = JointCount;
            double[,] a = new double[n, n + 1];
            for (int row = 0; row < n; row++)
            {
                for (int col = 0; col < n; col++)
                {
                    a[row, col] = matrix[row, col];
                }
                a[row, n] = right[row];
            }

            for (int pivot = 0; pivot < n; pivot++)
            {
                int best = pivot;
                for (int row = pivot + 1; row < n; row++)
                {
                    if (Math.Abs(a[row, pivot]) > Math.Abs(a[best, pivot]))
                    {
                        best = row;
                    }
                }
                if (Math.Abs(a[best, pivot]) < 1e-12)
                {
                    return false;
                }
                if (best != pivot)
                {
                    for (int col = pivot; col <= n; col++)
                    {
                        (a[pivot, col], a[best, col]) = (a[best, col], a[pivot, col]);
                    }
                }
                double scale = a[pivot, pivot];
                for (int col = pivot; col <= n; col++)
                {
                    a[pivot, col] /= scale;
                }
                for (int row = 0; row < n; row++)
                {
                    if (row == pivot)
                    {
                        continue;
                    }
                    double factor = a[row, pivot];
                    for (int col = pivot; col <= n; col++)
                    {
                        a[row, col] -= factor * a[pivot, col];
                    }
                }
            }

            for (int row = 0; row < n; row++)
            {
                solution[row] = a[row, n];
            }
            return true;
        }

        private void ComputeError(Pose3DDataType target, ReadOnlySpan<double> joints, Span<double> error)
        {
            SimulatedArmForwardPose current = Forward(joints);
            ReadOnlySpan<double> targetPosition = target.Position.Span;
            ReadOnlySpan<double> currentPosition = current.ToolPose.Position.Span;
            error[0] = targetPosition[0] - currentPosition[0];
            error[1] = targetPosition[1] - currentPosition[1];
            error[2] = targetPosition[2] - currentPosition[2];

            ArrayOf<double> inverse = PoseMath.Conjugate(current.ToolPose.Orientation.Span);
            ArrayOf<double> delta = PoseMath.Multiply(target.Orientation.Span, inverse.Span);
            ReadOnlySpan<double> q = delta.Span;
            double scale = q[3] < 0.0 ? -2.0 : 2.0;
            error[3] = scale * q[0];
            error[4] = scale * q[1];
            error[5] = scale * q[2];
        }

        private static Pose3DDataType ToPose(Transform transform)
        {
            return new Pose3DDataType
            {
                FrameId = "base",
                Position = ArrayOf.Create([transform.X, transform.Y, transform.Z]),
                Orientation = transform.ToQuaternion()
            };
        }

        private static ArrayOf<double> Slerp(ReadOnlySpan<double> start, ReadOnlySpan<double> end, double fraction)
        {
            ArrayOf<double> normalizedStart = PoseMath.Normalize(start);
            ArrayOf<double> normalizedEnd = PoseMath.Normalize(end);
            ReadOnlySpan<double> a = normalizedStart.Span;
            ReadOnlySpan<double> b = normalizedEnd.Span;
            double dot = (a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2]) + (a[3] * b[3]);
            double sign = 1.0;
            if (dot < 0.0)
            {
                sign = -1.0;
                dot = -dot;
            }
            if (dot > 0.9995)
            {
                return PoseMath.Normalize([
                    Lerp(a[0], sign * b[0], fraction),
                    Lerp(a[1], sign * b[1], fraction),
                    Lerp(a[2], sign * b[2], fraction),
                    Lerp(a[3], sign * b[3], fraction)
                ]);
            }
            double theta = Math.Acos(Math.Clamp(dot, -1.0, 1.0));
            double sinTheta = Math.Sin(theta);
            double wa = Math.Sin((1.0 - fraction) * theta) / sinTheta;
            double wb = Math.Sin(fraction * theta) / sinTheta;
            return PoseMath.Normalize([
                (wa * a[0]) + (wb * sign * b[0]),
                (wa * a[1]) + (wb * sign * b[1]),
                (wa * a[2]) + (wb * sign * b[2]),
                (wa * a[3]) + (wb * sign * b[3])
            ]);
        }

        private static bool IsWristSingular(ReadOnlySpan<double> joints)
        {
            return Math.Abs(Math.Sin(joints[4])) < SingularityTolerance;
        }

        private static double Travel(ReadOnlySpan<double> candidate, ReadOnlySpan<double> reference)
        {
            double sum = 0.0;
            for (int i = 0; i < JointCount; i++)
            {
                double delta = NormalizeNear(candidate[i], reference[i]) - reference[i];
                sum += delta * delta;
            }
            return sum;
        }

        private static double NormalizeNear(double angle, double reference)
        {
            double result = angle;
            while (result - reference > Math.PI)
            {
                result -= TwoPi;
            }
            while (result - reference < -Math.PI)
            {
                result += TwoPi;
            }
            return result;
        }

        private static double Lerp(double start, double end, double fraction)
        {
            return start + ((end - start) * fraction);
        }

        private static double Norm(double x, double y, double z)
        {
            return Math.Sqrt((x * x) + (y * y) + (z * z));
        }

        private static double MaxAbsoluteDifference(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
        {
            double max = 0.0;
            for (int i = 0; i < left.Length; i++)
            {
                max = Math.Max(max, Math.Abs(left[i] - right[i]));
            }
            return max;
        }

        private static void RequireJointCount(ReadOnlySpan<double> jointAngles)
        {
            if (jointAngles.Length != JointCount)
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "Expected {0} joint values.", JointCount),
                    nameof(jointAngles));
            }
        }

        /// <summary>
        /// The number of revolute joints.
        /// </summary>
        public const int JointCount = 6;

        /// <summary>
        /// Reach used by Robot Intent diagnostics, in metres.
        /// </summary>
        public const double Reach = 0.85;

        private const double D1 = 0.1625;
        private const double A2 = -0.425;
        private const double A3 = -0.3922;
        private const double D4 = 0.1333;
        private const double D5 = 0.0997;
        private const double D6 = 0.0996;
        private const double FlangeToTcp = 0.165;
        private const int PathSampleCount = 24;
        private const double PositionTolerance = 1e-5;
        private const double OrientationTolerance = 1e-5;
        private const double SingularityTolerance = 1e-4;
        private const double TwoPi = 2.0 * Math.PI;

        private static readonly double[] s_defaultMinimumLimits =
        [
            -2.0 * Math.PI,
            -2.0 * Math.PI,
            -Math.PI,
            -2.0 * Math.PI,
            -2.0 * Math.PI,
            -2.0 * Math.PI
        ];

        private static readonly double[] s_defaultMaximumLimits =
        [
            2.0 * Math.PI,
            2.0 * Math.PI,
            Math.PI,
            2.0 * Math.PI,
            2.0 * Math.PI,
            2.0 * Math.PI
        ];

        /// <summary>
        /// Eight UR-style branches: elbow up and down, wrist flipped, shoulder forward and
        /// back. Sixteen were tried - the extra eight starting J2 and J4 in other basins to
        /// look for a less contorted shape near the base - and measured: they raised the
        /// distinct-posture count but every shape they added was refused by clearance, so
        /// the number of *usable* postures at the home slots did not move. They were dropped
        /// again because a seed costs a full Newton refinement on every solve, and this
        /// solver runs per step of a Cartesian move.
        /// </summary>
        private static readonly double[][] s_seedTemplates =
        [
            [0.0, -1.2, 1.4, -1.7, 0.8, 0.0],
            [0.0, -1.2, 1.4, -1.7, -0.8, Math.PI],
            [0.0, 0.4, -1.4, 1.0, 0.8, 0.0],
            [0.0, 0.4, -1.4, 1.0, -0.8, Math.PI],
            [Math.PI, -1.2, 1.4, -1.7, 0.8, 0.0],
            [Math.PI, -1.2, 1.4, -1.7, -0.8, Math.PI],
            [Math.PI, 0.4, -1.4, 1.0, 0.8, 0.0],
            [Math.PI, 0.4, -1.4, 1.0, -0.8, Math.PI]
        ];

        private static readonly double[] s_initialJointAngles =
            [-3.0484844, 0.3128706, 0.8261335, 2.0025887, -2.7856466, -1.5707963];

        private readonly double[] m_minimumLimits;
        private readonly double[] m_maximumLimits;

        private readonly struct Transform
        {
            private Transform(
                double m00,
                double m01,
                double m02,
                double x,
                double m10,
                double m11,
                double m12,
                double y,
                double m20,
                double m21,
                double m22,
                double z)
            {
                m_m00 = m00;
                m_m01 = m01;
                m_m02 = m02;
                X = x;
                m_m10 = m10;
                m_m11 = m11;
                m_m12 = m12;
                Y = y;
                m_m20 = m20;
                m_m21 = m21;
                m_m22 = m22;
                Z = z;
            }

            public static Transform Identity { get; } = new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0);

            public double X { get; }

            public double Y { get; }

            public double Z { get; }

            public static Transform Translate(double x, double y, double z)
            {
                return new Transform(1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, z);
            }

            public static Transform RotateY(double angle)
            {
                double c = Math.Cos(angle);
                double s = Math.Sin(angle);
                return new Transform(c, 0, s, 0, 0, 1, 0, 0, -s, 0, c, 0);
            }

            public static Transform RotateZ(double angle)
            {
                double c = Math.Cos(angle);
                double s = Math.Sin(angle);
                return new Transform(c, -s, 0, 0, s, c, 0, 0, 0, 0, 1, 0);
            }

            public static Transform operator *(Transform left, Transform right)
            {
                return new Transform(
                    (left.m_m00 * right.m_m00) + (left.m_m01 * right.m_m10) + (left.m_m02 * right.m_m20),
                    (left.m_m00 * right.m_m01) + (left.m_m01 * right.m_m11) + (left.m_m02 * right.m_m21),
                    (left.m_m00 * right.m_m02) + (left.m_m01 * right.m_m12) + (left.m_m02 * right.m_m22),
                    (left.m_m00 * right.X) + (left.m_m01 * right.Y) + (left.m_m02 * right.Z) + left.X,
                    (left.m_m10 * right.m_m00) + (left.m_m11 * right.m_m10) + (left.m_m12 * right.m_m20),
                    (left.m_m10 * right.m_m01) + (left.m_m11 * right.m_m11) + (left.m_m12 * right.m_m21),
                    (left.m_m10 * right.m_m02) + (left.m_m11 * right.m_m12) + (left.m_m12 * right.m_m22),
                    (left.m_m10 * right.X) + (left.m_m11 * right.Y) + (left.m_m12 * right.Z) + left.Y,
                    (left.m_m20 * right.m_m00) + (left.m_m21 * right.m_m10) + (left.m_m22 * right.m_m20),
                    (left.m_m20 * right.m_m01) + (left.m_m21 * right.m_m11) + (left.m_m22 * right.m_m21),
                    (left.m_m20 * right.m_m02) + (left.m_m21 * right.m_m12) + (left.m_m22 * right.m_m22),
                    (left.m_m20 * right.X) + (left.m_m21 * right.Y) + (left.m_m22 * right.Z) + left.Z);
            }

            public ArrayOf<double> ToQuaternion()
            {
                double trace = m_m00 + m_m11 + m_m22;
                if (trace > 0.0)
                {
                    double s = Math.Sqrt(trace + 1.0) * 2.0;
                    return PoseMath.Normalize(
                        [(m_m21 - m_m12) / s, (m_m02 - m_m20) / s, (m_m10 - m_m01) / s, 0.25 * s]);
                }
                if (m_m00 > m_m11 && m_m00 > m_m22)
                {
                    double s = Math.Sqrt(1.0 + m_m00 - m_m11 - m_m22) * 2.0;
                    return PoseMath.Normalize(
                        [0.25 * s, (m_m01 + m_m10) / s, (m_m02 + m_m20) / s, (m_m21 - m_m12) / s]);
                }
                if (m_m11 > m_m22)
                {
                    double s = Math.Sqrt(1.0 + m_m11 - m_m00 - m_m22) * 2.0;
                    return PoseMath.Normalize(
                        [(m_m01 + m_m10) / s, 0.25 * s, (m_m12 + m_m21) / s, (m_m02 - m_m20) / s]);
                }
                double sz = Math.Sqrt(1.0 + m_m22 - m_m00 - m_m11) * 2.0;
                return PoseMath.Normalize(
                    [(m_m02 + m_m20) / sz, (m_m12 + m_m21) / sz, 0.25 * sz, (m_m10 - m_m01) / sz]);
            }

            private readonly double m_m00;
            private readonly double m_m01;
            private readonly double m_m02;
            private readonly double m_m10;
            private readonly double m_m11;
            private readonly double m_m12;
            private readonly double m_m20;
            private readonly double m_m21;
            private readonly double m_m22;
        }
    }
}
