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

namespace Robotics
{
    /// <summary>
    /// Inverse kinematics for the sample arm, used to place the tool centre point over a
    /// table slot.
    /// </summary>
    /// <remarks>
    /// Only the shoulder and elbow are solved. The wrist is then set so the tool points
    /// straight down, which is how a part is picked off and set onto a table. The solution
    /// is checked against <see cref="RobotKinematics"/> by the tests, so the arm provably
    /// reaches the slot rather than merely looking as though it does.
    /// </remarks>
    public static class RobotArmSolver
    {
        private const double UpperArm = 0.6800;
        private const double ForearmX = 0.6700;
        private const double ForearmZ = -0.0350;

        /// <summary>
        /// The distance from the wrist to the tool centre point, in metres.
        /// </summary>
        public const double WristToToolCentrePoint = 0.1580 + RobotKinematics.ToolCentrePointOffsetX;

        /// <summary>
        /// Solves the arm so the tool centre point sits at a target expressed in the robot's
        /// own frame.
        /// </summary>
        /// <param name="forward">
        /// The horizontal distance ahead of the platform, in metres.
        /// </param>
        /// <param name="lateral">The horizontal offset to the left, in metres.</param>
        /// <param name="height">The height above the platform origin, in metres.</param>
        /// <param name="axesDegrees">The six axis positions, in degrees.</param>
        /// <returns><c>true</c> when the target is reachable.</returns>
        public static bool TrySolve(
            double forward,
            double lateral,
            double height,
            Span<double> axesDegrees)
        {
            if (axesDegrees.Length != RobotKinematics.AxisCount)
            {
                throw new ArgumentException(
                    $"Expected {RobotKinematics.AxisCount} axis positions.",
                    nameof(axesDegrees));
            }

            // The shoulder sits above the platform origin; everything below is solved in the
            // vertical plane the first axis swings to.
            double azimuth = Math.Atan2(lateral, forward);
            double planar = Math.Sqrt((forward * forward) + (lateral * lateral));

            const double shoulderHeight = 0.3650 + 0.2900 + 0.3850;
            const double shoulderForward = 0.2600;

            // The wrist has to sit above the target by the tool length, so the tool points
            // straight down onto the slot.
            double u = planar - shoulderForward;
            double v = shoulderHeight - (height + WristToToolCentrePoint);

            double forearm = Math.Sqrt((ForearmX * ForearmX) + (ForearmZ * ForearmZ));
            double beta = Math.Atan2(-ForearmZ, ForearmX);

            double distanceSquared = (u * u) + (v * v);
            double cosGamma = (distanceSquared - (UpperArm * UpperArm) - (forearm * forearm)) /
                (2.0 * UpperArm * forearm);
            if (cosGamma is < -1.0 or > 1.0)
            {
                return false;
            }

            double gamma = Math.Acos(cosGamma);
            double a2 = Math.Atan2(v, u) -
                Math.Atan2(forearm * Math.Sin(gamma), UpperArm + (forearm * Math.Cos(gamma)));
            double a3 = gamma - beta;

            const double toDegrees = 180.0 / Math.PI;
            axesDegrees[0] = azimuth * toDegrees;
            axesDegrees[1] = a2 * toDegrees;
            axesDegrees[2] = a3 * toDegrees;
            axesDegrees[3] = 0.0;
            axesDegrees[4] = 90.0 - ((a2 + a3) * toDegrees);
            axesDegrees[5] = 0.0;
            return true;
        }
    }
}
