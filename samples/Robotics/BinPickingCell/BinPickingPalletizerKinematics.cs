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
using Opc.Ua;
using Opc.Ua.RobotIntent;
using Robotics.IntentEnabledRobot.Kinematics;
using Robotics.IntentEnabledRobot.Simulation;

namespace Vision.BinPickingCell
{
    /// <summary>
    /// Analytic kinematics for the bin-picking palletizer.
    /// </summary>
    internal sealed class BinPickingPalletizerKinematics : ISimulatedArmKinematics
    {
        public int AxisCount => BinPickingPalletizerGeometry.AxisCount;

        public double MaximumReach => BinPickingPalletizerGeometry.MaximumReachMetres;

        public ArrayOf<double> InitialJointAngles => ArrayOf.Create(s_initialJointAngles.AsSpan());

        public double MinimumLinkHeight { get; set; } = double.NegativeInfinity;

        public SimulatedCollisionModel? Collisions { get; set; }

        public void SetHeldObjectEnvelope(double sizeX, double sizeY, double sizeZ)
        {
            if (sizeX < 0.0 || sizeY < 0.0 || sizeZ < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sizeX),
                    "Held-object dimensions must be non-negative.");
            }
            m_heldObjectSizeX = sizeX;
            m_heldObjectSizeY = sizeY;
            m_heldObjectSizeZ = sizeZ;
        }

        public void ClearHeldObjectEnvelope()
        {
            m_heldObjectSizeX = 0.0;
            m_heldObjectSizeY = 0.0;
            m_heldObjectSizeZ = 0.0;
        }

        public SimulatedArmForwardPose Forward(ReadOnlySpan<double> jointAngles)
        {
            RequireJointCount(jointAngles);

            double baseYaw = jointAngles[0];
            double shoulder = jointAngles[1];
            double elbow = jointAngles[2];
            double toolRoll = jointAngles[3];
            double forearmPitch = shoulder + elbow;

            double cosBase = Math.Cos(baseYaw);
            double sinBase = Math.Sin(baseYaw);

            double elbowRadius =
                BinPickingPalletizerGeometry.UpperArmLengthMetres * Math.Cos(shoulder);
            double elbowZ =
                BinPickingPalletizerGeometry.ShoulderHeightMetres -
                (BinPickingPalletizerGeometry.UpperArmLengthMetres * Math.Sin(shoulder));

            double wristRadius = elbowRadius +
                (BinPickingPalletizerGeometry.ForearmLengthMetres * Math.Cos(forearmPitch));
            double wristZ = elbowZ -
                (BinPickingPalletizerGeometry.ForearmLengthMetres * Math.Sin(forearmPitch));

            double[] shoulderPosition =
                [0.0, 0.0, BinPickingPalletizerGeometry.ShoulderHeightMetres];
            double[] elbowPosition =
                [elbowRadius * cosBase, elbowRadius * sinBase, elbowZ];
            double[] wristPosition =
                [wristRadius * cosBase, wristRadius * sinBase, wristZ];

            double[] baseOrientation = Orientation(baseYaw, 0.0, 0.0);
            double[] shoulderOrientation = Orientation(baseYaw, shoulder, 0.0);
            double[] elbowOrientation = Orientation(baseYaw, forearmPitch, 0.0);
            double[] toolOrientation = Orientation(baseYaw, HalfTurn, toolRoll);
            ArrayOf<double> toolAxis = PoseMath.RotateVector(toolOrientation, s_axisX);
            ReadOnlySpan<double> axis = toolAxis.Span;
            double[] toolPosition =
            [
                wristPosition[0] + (axis[0] * BinPickingPalletizerGeometry.FlangeToTcpMetres),
                wristPosition[1] + (axis[1] * BinPickingPalletizerGeometry.FlangeToTcpMetres),
                wristPosition[2] + (axis[2] * BinPickingPalletizerGeometry.FlangeToTcpMetres)
            ];

            ArrayOf<Pose3DDataType> frames = ArrayOf.Create(
            [
                Pose(shoulderPosition, baseOrientation),
                Pose(shoulderPosition, shoulderOrientation),
                Pose(elbowPosition, elbowOrientation),
                Pose(wristPosition, toolOrientation)
            ]);
            return new SimulatedArmForwardPose(
                Pose(toolPosition, toolOrientation),
                frames);
        }

        public SimulatedArmIkResult Inverse(
            Pose3DDataType target,
            ReadOnlySpan<double> referenceJointAngles)
        {
            ArgumentNullException.ThrowIfNull(target);
            RequireJointCount(referenceJointAngles);
            if (target.Position.Count < 3 || target.Orientation.Count < 4)
            {
                return new SimulatedArmIkResult(
                    SimulatedArmKinematicFailure.Kinematics,
                    "The target must carry a 3D position and quaternion orientation.",
                    []);
            }

            ArrayOf<double> toolAxis = PoseMath.RotateVector(target.Orientation.Span, s_axisX);
            ReadOnlySpan<double> axis = toolAxis.Span;
            if (Math.Abs(axis[0]) > OrientationTolerance ||
                Math.Abs(axis[1]) > OrientationTolerance ||
                Math.Abs(axis[2] + 1.0) > OrientationTolerance)
            {
                return new SimulatedArmIkResult(
                    SimulatedArmKinematicFailure.Kinematics,
                    "The palletizer supports tool-down targets only.",
                    []);
            }

            ReadOnlySpan<double> targetPosition = target.Position.Span;
            double wristX = targetPosition[0] -
                (axis[0] * BinPickingPalletizerGeometry.FlangeToTcpMetres);
            double wristY = targetPosition[1] -
                (axis[1] * BinPickingPalletizerGeometry.FlangeToTcpMetres);
            double wristZ = targetPosition[2] -
                (axis[2] * BinPickingPalletizerGeometry.FlangeToTcpMetres);
            double radius = Math.Sqrt((wristX * wristX) + (wristY * wristY));
            double verticalDrop =
                BinPickingPalletizerGeometry.ShoulderHeightMetres - wristZ;
            double upper = BinPickingPalletizerGeometry.UpperArmLengthMetres;
            double forearm = BinPickingPalletizerGeometry.ForearmLengthMetres;
            double cosineElbow =
                (((radius * radius) + (verticalDrop * verticalDrop)) -
                    (upper * upper) - (forearm * forearm)) /
                (2.0 * upper * forearm);
            if (cosineElbow < -1.0 - PositionTolerance ||
                cosineElbow > 1.0 + PositionTolerance)
            {
                return new SimulatedArmIkResult(
                    SimulatedArmKinematicFailure.Unreachable,
                    "The target lies outside the palletizer workspace.",
                    []);
            }
            cosineElbow = Math.Clamp(cosineElbow, -1.0, 1.0);

            double baseYaw = NormalizeAngle(Math.Atan2(wristY, wristX));
            if (!TryExtractToolRoll(
                target.Orientation.Span,
                baseYaw,
                out double requestedToolRoll))
            {
                return new SimulatedArmIkResult(
                    SimulatedArmKinematicFailure.Kinematics,
                    "The target yaw cannot be represented by the palletizer wrist.",
                    []);
            }
            requestedToolRoll = NormalizeAngle(requestedToolRoll);

            var solutions = new List<SimulatedArmIkSolution>(2);
            double elbowMagnitude = Math.Acos(cosineElbow);
            AddBranch(
                elbowMagnitude,
                baseYaw,
                requestedToolRoll,
                radius,
                verticalDrop,
                referenceJointAngles,
                solutions);
            if (elbowMagnitude > PositionTolerance)
            {
                AddBranch(
                    -elbowMagnitude,
                    baseYaw,
                    requestedToolRoll,
                    radius,
                    verticalDrop,
                    referenceJointAngles,
                    solutions);
            }
            solutions.Sort(static (left, right) =>
            {
                // Prefer elbow-up when travel is effectively tied; it keeps the elbow and
                // wrist above the work instead of folding toward the table.
                double delta = left.TravelCost - right.TravelCost;
                if (Math.Abs(delta) > 1e-9)
                {
                    return delta < 0.0 ? -1 : 1;
                }
                return left.JointAngles[2].CompareTo(right.JointAngles[2]);
            });

            return solutions.Count == 0
                ? new SimulatedArmIkResult(
                    SimulatedArmKinematicFailure.JointLimit,
                    "All palletizer branches exceed a joint limit.",
                    [])
                : new SimulatedArmIkResult(
                    SimulatedArmKinematicFailure.None,
                    string.Empty,
                    ArrayOf.Create(solutions.ToArray().AsSpan()));
        }

        public bool TrySelectNearest(
            Pose3DDataType target,
            ReadOnlySpan<double> currentJointAngles,
            [NotNullWhen(true)] out SimulatedArmIkSolution? solution,
            out SimulatedArmKinematicFailure failure)
        {
            return TrySelectNearestCore(
                target,
                currentJointAngles,
                requireClearPath: true,
                out solution,
                out failure);
        }

        public bool TrySelectNearestConfiguration(
            Pose3DDataType target,
            ReadOnlySpan<double> currentJointAngles,
            [NotNullWhen(true)] out SimulatedArmIkSolution? solution,
            out SimulatedArmKinematicFailure failure)
        {
            return TrySelectNearestCore(
                target,
                currentJointAngles,
                requireClearPath: false,
                out solution,
                out failure);
        }

        public bool IsWithinLimits(ReadOnlySpan<double> jointAngles)
        {
            RequireJointCount(jointAngles);
            return Math.Abs(jointAngles[0]) <= BinPickingPalletizerGeometry.BaseYawLimitRadians &&
                jointAngles[1] >= BinPickingPalletizerGeometry.ShoulderMinimumRadians &&
                jointAngles[1] <= BinPickingPalletizerGeometry.ShoulderMaximumRadians &&
                jointAngles[2] >= BinPickingPalletizerGeometry.ElbowMinimumRadians &&
                jointAngles[2] <= BinPickingPalletizerGeometry.ElbowMaximumRadians &&
                Math.Abs(jointAngles[3]) <= BinPickingPalletizerGeometry.ToolRollLimitRadians;
        }

        public ArrayOf<double> InterpolateJoints(
            ReadOnlySpan<double> start,
            ReadOnlySpan<double> end,
            double fraction)
        {
            RequireJointCount(start);
            RequireJointCount(end);
            double t = Math.Clamp(fraction, 0.0, 1.0);
            return ArrayOf.Create(
            [
                Lerp(start[0], end[0], t),
                Lerp(start[1], end[1], t),
                Lerp(start[2], end[2], t),
                Lerp(start[3], end[3], t)
            ]);
        }

        public Pose3DDataType InterpolateCartesian(
            Pose3DDataType start,
            Pose3DDataType end,
            double fraction)
        {
            ArgumentNullException.ThrowIfNull(start);
            ArgumentNullException.ThrowIfNull(end);
            double t = Math.Clamp(fraction, 0.0, 1.0);
            ReadOnlySpan<double> a = start.Position.Span;
            ReadOnlySpan<double> b = end.Position.Span;
            return new Pose3DDataType
            {
                FrameId = string.IsNullOrEmpty(end.FrameId) ? start.FrameId : end.FrameId,
                Position = ArrayOf.Create(
                [
                    Lerp(a[0], b[0], t),
                    Lerp(a[1], b[1], t),
                    Lerp(a[2], b[2], t)
                ]),
                Orientation = Nlerp(start.Orientation.Span, end.Orientation.Span, t)
            };
        }

        public bool ClearsPath(ReadOnlySpan<double> start, ReadOnlySpan<double> target)
        {
            RequireJointCount(start);
            RequireJointCount(target);
            Span<double> configuration = stackalloc double[BinPickingPalletizerGeometry.AxisCount];
            for (int step = 0; step <= PathSampleCount; step++)
            {
                double fraction = (double)step / PathSampleCount;
                for (int ii = 0; ii < configuration.Length; ii++)
                {
                    configuration[ii] = Lerp(start[ii], target[ii], fraction);
                }
                if (!ClearsWorkSurface(configuration))
                {
                    return false;
                }
            }
            return true;
        }

        public bool ClearsWorkSurface(ReadOnlySpan<double> jointAngles)
        {
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
            if (!Collisions.IsClear(points, out _))
            {
                return false;
            }
            if (m_heldObjectSizeX <= 0.0 ||
                m_heldObjectSizeY <= 0.0 ||
                m_heldObjectSizeZ <= 0.0)
            {
                return true;
            }
            return Collisions.IsBoxClear(
                tool[0],
                tool[1],
                tool[2] -
                    global::Robotics.IntentEnabledRobot.Simulation.SimulatedArmExecutor
                        .HeldPartTcpOffset,
                m_heldObjectSizeX,
                m_heldObjectSizeY,
                m_heldObjectSizeZ,
                out _);
        }

        public IntentFailureEnum MapFailure(SimulatedArmKinematicFailure failure)
        {
            return SimulatedArmKinematics.ToIntentFailure(failure);
        }

        /// <summary>
        /// Creates a tool-down orientation for one base yaw and jaw roll.
        /// </summary>
        public static ArrayOf<double> ToolDownOrientation(double baseYaw, double toolRoll)
        {
            return Orientation(baseYaw, HalfTurn, toolRoll).ToArrayOf();
        }

        private static void AddBranch(
            double elbow,
            double baseYaw,
            double toolRoll,
            double radius,
            double verticalDrop,
            ReadOnlySpan<double> reference,
            List<SimulatedArmIkSolution> solutions)
        {
            double shoulder = Math.Atan2(verticalDrop, radius) -
                Math.Atan2(
                    BinPickingPalletizerGeometry.ForearmLengthMetres * Math.Sin(elbow),
                    BinPickingPalletizerGeometry.UpperArmLengthMetres +
                    (BinPickingPalletizerGeometry.ForearmLengthMetres * Math.Cos(elbow)));
            double[] candidate = [baseYaw, shoulder, elbow, toolRoll];
            var kinematics = new BinPickingPalletizerKinematics();
            if (!kinematics.IsWithinLimits(candidate))
            {
                return;
            }
            double cost = 0.0;
            for (int ii = 0; ii < candidate.Length; ii++)
            {
                double delta = candidate[ii] - reference[ii];
                cost += delta * delta;
            }
            solutions.Add(new SimulatedArmIkSolution(candidate.ToArrayOf(), cost));
        }

        private bool TrySelectNearestCore(
            Pose3DDataType target,
            ReadOnlySpan<double> currentJointAngles,
            bool requireClearPath,
            [NotNullWhen(true)] out SimulatedArmIkSolution? solution,
            out SimulatedArmKinematicFailure failure)
        {
            SimulatedArmIkResult result = Inverse(target, currentJointAngles);
            failure = result.Failure;
            solution = null;
            ReadOnlySpan<SimulatedArmIkSolution> candidates = result.Solutions.Span;
            for (int ii = 0; ii < candidates.Length; ii++)
            {
                ReadOnlySpan<double> angles = candidates[ii].JointAngles.Span;
                if (ClearsWorkSurface(angles) &&
                    (!requireClearPath || ClearsPath(currentJointAngles, angles)))
                {
                    solution = candidates[ii];
                    return true;
                }
            }
            if (!result.Solutions.IsEmpty)
            {
                failure = SimulatedArmKinematicFailure.WorkSurface;
            }
            return false;
        }

        private static bool TryExtractToolRoll(
            ReadOnlySpan<double> orientation,
            double baseYaw,
            out double toolRoll)
        {
            double[] baseDown = Orientation(baseYaw, HalfTurn, 0.0);
            double[] relative = Multiply(Inverse(baseDown), orientation);
            relative = Normalize(relative);
            if (Math.Abs(relative[1]) > OrientationTolerance ||
                Math.Abs(relative[2]) > OrientationTolerance)
            {
                toolRoll = 0.0;
                return false;
            }
            toolRoll = 2.0 * Math.Atan2(relative[0], relative[3]);
            return true;
        }

        private static Pose3DDataType Pose(double[] position, double[] orientation)
        {
            return new Pose3DDataType
            {
                FrameId = BinPickingPalletizerGeometry.RobotBaseFrameId,
                Position = position.ToArrayOf(),
                Orientation = orientation.ToArrayOf()
            };
        }

        private static double[] Orientation(double yaw, double pitch, double roll)
        {
            return Normalize(
                Multiply(
                    Multiply(
                        [0.0, 0.0, Math.Sin(yaw * 0.5), Math.Cos(yaw * 0.5)],
                        [0.0, Math.Sin(pitch * 0.5), 0.0, Math.Cos(pitch * 0.5)]),
                    [Math.Sin(roll * 0.5), 0.0, 0.0, Math.Cos(roll * 0.5)]));
        }

        private static double[] Multiply(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
        {
            double lx = left[0];
            double ly = left[1];
            double lz = left[2];
            double lw = left[3];
            double rx = right[0];
            double ry = right[1];
            double rz = right[2];
            double rw = right[3];
            return
            [
                (lw * rx) + (lx * rw) + (ly * rz) - (lz * ry),
                (lw * ry) - (lx * rz) + (ly * rw) + (lz * rx),
                (lw * rz) + (lx * ry) - (ly * rx) + (lz * rw),
                (lw * rw) - (lx * rx) - (ly * ry) - (lz * rz)
            ];
        }

        private static double[] Inverse(ReadOnlySpan<double> value)
        {
            return [-value[0], -value[1], -value[2], value[3]];
        }

        private static double[] Normalize(ReadOnlySpan<double> value)
        {
            double norm = Math.Sqrt(
                (value[0] * value[0]) +
                (value[1] * value[1]) +
                (value[2] * value[2]) +
                (value[3] * value[3]));
            return
            [
                value[0] / norm,
                value[1] / norm,
                value[2] / norm,
                value[3] / norm
            ];
        }

        private static ArrayOf<double> Nlerp(
            ReadOnlySpan<double> start,
            ReadOnlySpan<double> end,
            double fraction)
        {
            double dot =
                (start[0] * end[0]) +
                (start[1] * end[1]) +
                (start[2] * end[2]) +
                (start[3] * end[3]);
            double sign = dot < 0.0 ? -1.0 : 1.0;
            double[] value =
            [
                Lerp(start[0], end[0] * sign, fraction),
                Lerp(start[1], end[1] * sign, fraction),
                Lerp(start[2], end[2] * sign, fraction),
                Lerp(start[3], end[3] * sign, fraction)
            ];
            return Normalize(value).ToArrayOf();
        }

        private static double NormalizeAngle(double value)
        {
            while (value > Math.PI)
            {
                value -= TwoPi;
            }
            while (value < -Math.PI)
            {
                value += TwoPi;
            }
            return value;
        }

        private static double Lerp(double start, double end, double fraction)
        {
            return start + ((end - start) * fraction);
        }

        private static void RequireJointCount(ReadOnlySpan<double> jointAngles)
        {
            if (jointAngles.Length != BinPickingPalletizerGeometry.AxisCount)
            {
                throw new ArgumentException(
                    $"Expected {BinPickingPalletizerGeometry.AxisCount} joint angles.",
                    nameof(jointAngles));
            }
        }

        private static readonly double[] s_axisX = [1.0, 0.0, 0.0];
        private static readonly double[] s_initialJointAngles =
            [0.0, 0.4811790135369469, -1.6798993676150382, HalfTurn];
        private const double HalfTurn = Math.PI / 2.0;
        private const double TwoPi = Math.PI * 2.0;
        private const double PositionTolerance = 1e-7;
        private const double OrientationTolerance = 1e-5;
        private const int PathSampleCount = 32;
        private double m_heldObjectSizeX;
        private double m_heldObjectSizeY;
        private double m_heldObjectSizeZ;
    }
}
