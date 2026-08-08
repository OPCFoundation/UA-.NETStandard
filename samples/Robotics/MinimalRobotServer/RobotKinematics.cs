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
    /// A rigid body transform - a rotation followed by a translation.
    /// </summary>
    /// <remarks>
    /// Applied to column vectors, so a point maps as <c>p' = R p + t</c>. USD authors a
    /// <c>matrix4d</c> in the row vector convention with the translation in the last row,
    /// which <see cref="ToUsdRowMajor"/> produces.
    /// </remarks>
    public readonly struct RigidTransform : IEquatable<RigidTransform>
    {
        /// <summary>
        /// The transform that leaves a point where it is.
        /// </summary>
        public static RigidTransform Identity { get; } = new(
            1.0, 0.0, 0.0,
            0.0, 1.0, 0.0,
            0.0, 0.0, 1.0,
            0.0, 0.0, 0.0);

        /// <summary>
        /// The translation component, in metres.
        /// </summary>
        public (double X, double Y, double Z) Origin => (m_tx, m_ty, m_tz);

        /// <summary>
        /// Creates a pure translation.
        /// </summary>
        /// <param name="x">The X offset, in metres.</param>
        /// <param name="y">The Y offset, in metres.</param>
        /// <param name="z">The Z offset, in metres.</param>
        /// <returns>The transform.</returns>
        public static RigidTransform Translation(double x, double y, double z)
        {
            return new RigidTransform(
                1.0, 0.0, 0.0,
                0.0, 1.0, 0.0,
                0.0, 0.0, 1.0,
                x, y, z);
        }

        /// <summary>
        /// Creates a rotation about the X axis.
        /// </summary>
        /// <param name="degrees">The angle, in degrees.</param>
        /// <returns>The transform.</returns>
        public static RigidTransform RotationX(double degrees)
        {
            double r = degrees * (Math.PI / 180.0);
            double c = Math.Cos(r);
            double s = Math.Sin(r);
            return new RigidTransform(
                1.0, 0.0, 0.0,
                0.0, c, -s,
                0.0, s, c,
                0.0, 0.0, 0.0);
        }

        /// <summary>
        /// Creates a rotation about the Y axis.
        /// </summary>
        /// <param name="degrees">The angle, in degrees.</param>
        /// <returns>The transform.</returns>
        public static RigidTransform RotationY(double degrees)
        {
            double r = degrees * (Math.PI / 180.0);
            double c = Math.Cos(r);
            double s = Math.Sin(r);
            return new RigidTransform(
                c, 0.0, s,
                0.0, 1.0, 0.0,
                -s, 0.0, c,
                0.0, 0.0, 0.0);
        }

        /// <summary>
        /// Creates a rotation about the Z axis.
        /// </summary>
        /// <param name="degrees">The angle, in degrees.</param>
        /// <returns>The transform.</returns>
        public static RigidTransform RotationZ(double degrees)
        {
            double r = degrees * (Math.PI / 180.0);
            double c = Math.Cos(r);
            double s = Math.Sin(r);
            return new RigidTransform(
                c, -s, 0.0,
                s, c, 0.0,
                0.0, 0.0, 1.0,
                0.0, 0.0, 0.0);
        }

        /// <summary>
        /// Applies <paramref name="child"/> in this transform's frame.
        /// </summary>
        /// <param name="child">The transform expressed in this frame.</param>
        /// <returns>The composed transform.</returns>
        public RigidTransform Compose(in RigidTransform child)
        {
            return new RigidTransform(
                (m_r00 * child.m_r00) + (m_r01 * child.m_r10) + (m_r02 * child.m_r20),
                (m_r00 * child.m_r01) + (m_r01 * child.m_r11) + (m_r02 * child.m_r21),
                (m_r00 * child.m_r02) + (m_r01 * child.m_r12) + (m_r02 * child.m_r22),
                (m_r10 * child.m_r00) + (m_r11 * child.m_r10) + (m_r12 * child.m_r20),
                (m_r10 * child.m_r01) + (m_r11 * child.m_r11) + (m_r12 * child.m_r21),
                (m_r10 * child.m_r02) + (m_r11 * child.m_r12) + (m_r12 * child.m_r22),
                (m_r20 * child.m_r00) + (m_r21 * child.m_r10) + (m_r22 * child.m_r20),
                (m_r20 * child.m_r01) + (m_r21 * child.m_r11) + (m_r22 * child.m_r21),
                (m_r20 * child.m_r02) + (m_r21 * child.m_r12) + (m_r22 * child.m_r22),
                (m_r00 * child.m_tx) + (m_r01 * child.m_ty) + (m_r02 * child.m_tz) + m_tx,
                (m_r10 * child.m_tx) + (m_r11 * child.m_ty) + (m_r12 * child.m_tz) + m_ty,
                (m_r20 * child.m_tx) + (m_r21 * child.m_ty) + (m_r22 * child.m_tz) + m_tz);
        }

        /// <summary>
        /// Flattens the transform into the 16 row-major doubles a USD
        /// <c>matrix4d</c> attribute carries, with the translation in the last row.
        /// </summary>
        /// <returns>The flattened matrix.</returns>
        public double[] ToUsdRowMajor()
        {
            return
            [
                m_r00, m_r10, m_r20, 0.0,
                m_r01, m_r11, m_r21, 0.0,
                m_r02, m_r12, m_r22, 0.0,
                m_tx, m_ty, m_tz, 1.0
            ];
        }

        /// <inheritdoc/>
        public bool Equals(RigidTransform other)
        {
            return m_r00.Equals(other.m_r00) && m_r01.Equals(other.m_r01) &&
                m_r02.Equals(other.m_r02) && m_r10.Equals(other.m_r10) &&
                m_r11.Equals(other.m_r11) && m_r12.Equals(other.m_r12) &&
                m_r20.Equals(other.m_r20) && m_r21.Equals(other.m_r21) &&
                m_r22.Equals(other.m_r22) && m_tx.Equals(other.m_tx) &&
                m_ty.Equals(other.m_ty) && m_tz.Equals(other.m_tz);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is RigidTransform other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(m_r00, m_r11, m_r22, m_tx, m_ty, m_tz);
        }

        /// <summary>
        /// Compares two transforms.
        /// </summary>
        /// <param name="left">The first transform.</param>
        /// <param name="right">The second transform.</param>
        /// <returns><c>true</c> when the transforms are equal.</returns>
        public static bool operator ==(RigidTransform left, RigidTransform right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two transforms.
        /// </summary>
        /// <param name="left">The first transform.</param>
        /// <param name="right">The second transform.</param>
        /// <returns><c>true</c> when the transforms differ.</returns>
        public static bool operator !=(RigidTransform left, RigidTransform right)
        {
            return !left.Equals(right);
        }

        private RigidTransform(
            double r00, double r01, double r02,
            double r10, double r11, double r12,
            double r20, double r21, double r22,
            double tx, double ty, double tz)
        {
            m_r00 = r00;
            m_r01 = r01;
            m_r02 = r02;
            m_r10 = r10;
            m_r11 = r11;
            m_r12 = r12;
            m_r20 = r20;
            m_r21 = r21;
            m_r22 = r22;
            m_tx = tx;
            m_ty = ty;
            m_tz = tz;
        }

        private readonly double m_r00;
        private readonly double m_r01;
        private readonly double m_r02;
        private readonly double m_r10;
        private readonly double m_r11;
        private readonly double m_r12;
        private readonly double m_r20;
        private readonly double m_r21;
        private readonly double m_r22;
        private readonly double m_tx;
        private readonly double m_ty;
        private readonly double m_tz;
    }

    /// <summary>
    /// Forward kinematics of the sample robot, derived from the link offsets authored in
    /// <c>robot.usda</c>.
    /// </summary>
    /// <remarks>
    /// The server drives the arm by publishing six axis positions, and the connector maps
    /// each onto the matching <c>xformOp:rotate*</c> in the asset. Computing the tool centre
    /// point from those same six values - rather than from the choreography that produced
    /// them - is what keeps a carried part welded to the gripper instead of drifting away
    /// from it.
    /// </remarks>
    public static class RobotKinematics
    {
        /// <summary>
        /// The number of axes the arm has.
        /// </summary>
        public const int AxisCount = 6;

        /// <summary>
        /// The reach of the arm measured along the link offsets, in metres.
        /// </summary>
        /// <remarks>
        /// The sum of the J2, J3, J4 and flange X offsets. The cell layout keeps two robots
        /// further apart than this whenever both arms are deployed.
        /// </remarks>
        public const double MaximumReach = 0.260 + 0.680 + 0.670 + 0.158;

        /// <summary>
        /// The offset from the flange to the point between the gripper jaws, in metres.
        /// </summary>
        public const double ToolCentrePointOffsetX = 0.150;

        /// <summary>
        /// Computes the pose of the point between the gripper jaws.
        /// </summary>
        /// <param name="mount">The pose of the robot mount prim in cell coordinates.</param>
        /// <param name="axesDegrees">The six axis positions, in degrees.</param>
        /// <returns>The tool centre point pose in cell coordinates.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="axesDegrees"/> does not carry <see cref="AxisCount"/> values.
        /// </exception>
        public static RigidTransform ComputeToolCentrePoint(
            in RigidTransform mount,
            ReadOnlySpan<double> axesDegrees)
        {
            if (axesDegrees.Length != AxisCount)
            {
                throw new ArgumentException(
                    $"Expected {AxisCount} axis positions.",
                    nameof(axesDegrees));
            }

            RigidTransform pose = mount
                .Compose(RigidTransform.Translation(0.0, 0.0, 0.3650))
                .Compose(RigidTransform.Translation(0.0, 0.0, 0.2900))
                .Compose(RigidTransform.RotationZ(axesDegrees[0]))
                .Compose(RigidTransform.Translation(0.2600, 0.0, 0.3850))
                .Compose(RigidTransform.RotationY(axesDegrees[1]))
                .Compose(RigidTransform.Translation(0.6800, 0.0, 0.0))
                .Compose(RigidTransform.RotationY(axesDegrees[2]))
                .Compose(RigidTransform.Translation(0.6700, 0.0, -0.0350))
                .Compose(RigidTransform.RotationX(axesDegrees[3]))
                .Compose(RigidTransform.RotationY(axesDegrees[4]))
                .Compose(RigidTransform.RotationX(axesDegrees[5]))
                .Compose(RigidTransform.Translation(0.1580, 0.0, 0.0));

            return pose.Compose(
                RigidTransform.Translation(ToolCentrePointOffsetX, 0.0, 0.0));
        }

        /// <summary>
        /// Builds the pose of a robot mount from its cell position and heading.
        /// </summary>
        /// <param name="x">The X position, in metres.</param>
        /// <param name="y">The Y position, in metres.</param>
        /// <param name="z">The Z position, in metres.</param>
        /// <param name="headingDegrees">The heading about Z, in degrees.</param>
        /// <returns>The mount pose.</returns>
        public static RigidTransform CreateMountPose(
            double x,
            double y,
            double z,
            double headingDegrees)
        {
            return RigidTransform.Translation(x, y, z)
                .Compose(RigidTransform.RotationZ(headingDegrees));
        }
    }
}
