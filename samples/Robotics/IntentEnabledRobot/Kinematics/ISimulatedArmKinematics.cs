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
