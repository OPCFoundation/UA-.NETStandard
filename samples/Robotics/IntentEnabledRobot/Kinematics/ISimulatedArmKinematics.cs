/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Diagnostics.CodeAnalysis;
using Opc.Ua;
using Opc.Ua.RobotIntent;

namespace Robotics.IntentEnabledRobot.Kinematics
{
    /// <summary>
    /// Kinematics operations consumed by <c>SimulatedArmExecutor</c>.
    /// </summary>
    public interface ISimulatedArmKinematics
    {
        /// <summary>
        /// Gets the number of commanded axes.
        /// </summary>
        int AxisCount { get; }

        /// <summary>
        /// Gets the maximum advertised Cartesian reach in metres.
        /// </summary>
        double MaximumReach { get; }

        /// <summary>
        /// Gets the configuration the simulated arm starts in.
        /// </summary>
        ArrayOf<double> InitialJointAngles { get; }

        /// <summary>
        /// Computes the tool and joint-frame poses for one configuration.
        /// </summary>
        SimulatedArmForwardPose Forward(ReadOnlySpan<double> jointAngles);

        /// <summary>
        /// Gets whether all axes are within their configured limits.
        /// </summary>
        bool IsWithinLimits(ReadOnlySpan<double> jointAngles);

        /// <summary>
        /// Selects the nearest clear solution, including its joint-space path.
        /// </summary>
        bool TrySelectNearest(
            Pose3DDataType target,
            ReadOnlySpan<double> currentJointAngles,
            [NotNullWhen(true)] out SimulatedArmIkSolution? solution,
            out SimulatedArmKinematicFailure failure);

        /// <summary>
        /// Selects the nearest clear configuration when the caller samples the path.
        /// </summary>
        bool TrySelectNearestConfiguration(
            Pose3DDataType target,
            ReadOnlySpan<double> currentJointAngles,
            [NotNullWhen(true)] out SimulatedArmIkSolution? solution,
            out SimulatedArmKinematicFailure failure);

        /// <summary>
        /// Interpolates two joint configurations.
        /// </summary>
        ArrayOf<double> InterpolateJoints(
            ReadOnlySpan<double> start,
            ReadOnlySpan<double> end,
            double fraction);

        /// <summary>
        /// Interpolates two Cartesian poses.
        /// </summary>
        Pose3DDataType InterpolateCartesian(
            Pose3DDataType start,
            Pose3DDataType end,
            double fraction);

        /// <summary>
        /// Gets whether every sampled configuration along a joint-space path is clear.
        /// </summary>
        bool ClearsPath(ReadOnlySpan<double> start, ReadOnlySpan<double> target);

        /// <summary>
        /// Maps a kinematic refusal to the Robot Intent failure model.
        /// </summary>
        IntentFailureEnum MapFailure(SimulatedArmKinematicFailure failure);
    }
}
