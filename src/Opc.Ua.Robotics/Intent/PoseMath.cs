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

namespace Opc.Ua.RobotIntent
{
    /// <summary>
    /// Provides the normative Robot Intent pose, quaternion and OPC UA ThreeDFrame conversions.
    /// </summary>
    public static class PoseMath
    {
        /// <summary>
        /// Converts a Robot Intent pose to a core OPC UA <see cref="ThreeDFrame"/>.
        /// </summary>
        /// <param name="pose">
        /// The Robot Intent pose whose position is in metres and orientation is a unit quaternion.
        /// </param>
        /// <returns>
        /// The equivalent OPC UA <see cref="ThreeDFrame"/> using ISO 9787 A/B/C angles in radians.
        /// </returns>
        public static ThreeDFrame ToThreeDFrame(in Pose3DDataType pose)
        {
            if (pose is null)
            {
                throw new ArgumentNullException(nameof(pose));
            }

            ReadOnlySpan<double> position = GetPosition(pose);
            ArrayOf<double> normalized = Normalize(GetOrientation(pose));
            ReadOnlySpan<double> q = normalized.Span;

            double x = q[0];
            double y = q[1];
            double z = q[2];
            double w = q[3];

            double roll = Math.Atan2(
                2.0 * ((w * x) + (y * z)),
                1.0 - (2.0 * ((x * x) + (y * y))));
            double pitch = Math.Asin(Clamp(2.0 * ((w * y) - (z * x)), -1.0, 1.0));
            double yaw = Math.Atan2(
                2.0 * ((w * z) + (x * y)),
                1.0 - (2.0 * ((y * y) + (z * z))));

            return new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates
                {
                    X = position[0],
                    Y = position[1],
                    Z = position[2]
                },
                Orientation = new ThreeDOrientation
                {
                    A = roll,
                    B = pitch,
                    C = yaw
                }
            };
        }

        /// <summary>
        /// Converts a core OPC UA <see cref="ThreeDFrame"/> to a Robot Intent pose.
        /// </summary>
        /// <param name="frame">
        /// The core frame whose Cartesian coordinates are metres and whose A/B/C angles are radians.
        /// </param>
        /// <param name="frameId">
        /// The Robot Intent frame identifier to assign to the returned pose.
        /// </param>
        /// <returns>
        /// The equivalent Robot Intent pose with a unit quaternion ordered x, y, z, w.
        /// </returns>
        public static Pose3DDataType FromThreeDFrame(ThreeDFrame frame, string frameId)
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }
            if (frameId is null)
            {
                throw new ArgumentNullException(nameof(frameId));
            }

            ThreeDCartesianCoordinates coordinates = frame.CartesianCoordinates ??
                throw new ArgumentException("The frame CartesianCoordinates field is required.", nameof(frame));
            ThreeDOrientation orientation = frame.Orientation ??
                throw new ArgumentException("The frame Orientation field is required.", nameof(frame));

            double halfRoll = orientation.A / 2.0;
            double halfPitch = orientation.B / 2.0;
            double halfYaw = orientation.C / 2.0;

            double cr = Math.Cos(halfRoll);
            double sr = Math.Sin(halfRoll);
            double cp = Math.Cos(halfPitch);
            double sp = Math.Sin(halfPitch);
            double cy = Math.Cos(halfYaw);
            double sy = Math.Sin(halfYaw);

            return new Pose3DDataType
            {
                FrameId = frameId,
                Position = ArrayOf.Create([coordinates.X, coordinates.Y, coordinates.Z]),
                Orientation = Normalize([
                    (sr * cp * cy) - (cr * sp * sy),
                    (cr * sp * cy) + (sr * cp * sy),
                    (cr * cp * sy) - (sr * sp * cy),
                    (cr * cp * cy) + (sr * sp * sy)
                ])
            };
        }

        /// <summary>
        /// Normalizes a quaternion and returns the representative whose w component is non-negative.
        /// </summary>
        /// <param name="orientation">
        /// The quaternion ordered x, y, z, w.
        /// </param>
        /// <returns>
        /// A unit quaternion ordered x, y, z, w.
        /// </returns>
        public static ArrayOf<double> Normalize(ReadOnlySpan<double> orientation)
        {
            RequireLength(orientation, 4, nameof(orientation));

            double norm = Norm(orientation);
            if (norm == 0.0 || double.IsNaN(norm))
            {
                throw new ArgumentException("The quaternion norm must be non-zero.", nameof(orientation));
            }

            double sign = orientation[3] < 0.0 ? -1.0 : 1.0;
            return ArrayOf.Create([
                sign * orientation[0] / norm,
                sign * orientation[1] / norm,
                sign * orientation[2] / norm,
                sign * orientation[3] / norm
            ]);
        }

        /// <summary>
        /// Determines whether an orientation is a unit quaternion within a tolerance.
        /// </summary>
        /// <param name="orientation">
        /// The quaternion ordered x, y, z, w.
        /// </param>
        /// <param name="tolerance">
        /// The maximum allowed absolute difference between the quaternion norm and one.
        /// </param>
        /// <returns>
        /// <c>true</c> if the orientation has four components and its norm is within the tolerance.
        /// </returns>
        public static bool IsUnitQuaternion(ReadOnlySpan<double> orientation, double tolerance = 1e-6)
        {
            return orientation.Length == 4 && Math.Abs(Norm(orientation) - 1.0) <= tolerance;
        }

        /// <summary>
        /// Multiplies two quaternions.
        /// </summary>
        /// <param name="left">
        /// The left quaternion ordered x, y, z, w.
        /// </param>
        /// <param name="right">
        /// The right quaternion ordered x, y, z, w.
        /// </param>
        /// <returns>
        /// The normalized Hamilton product ordered x, y, z, w.
        /// </returns>
        public static ArrayOf<double> Multiply(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
        {
            RequireLength(left, 4, nameof(left));
            RequireLength(right, 4, nameof(right));

            double lx = left[0];
            double ly = left[1];
            double lz = left[2];
            double lw = left[3];
            double rx = right[0];
            double ry = right[1];
            double rz = right[2];
            double rw = right[3];

            return Normalize([
                (lw * rx) + (lx * rw) + (ly * rz) - (lz * ry),
                (lw * ry) - (lx * rz) + (ly * rw) + (lz * rx),
                (lw * rz) + (lx * ry) - (ly * rx) + (lz * rw),
                (lw * rw) - (lx * rx) - (ly * ry) - (lz * rz)
            ]);
        }

        /// <summary>
        /// Returns the conjugate of a quaternion.
        /// </summary>
        /// <param name="orientation">
        /// The quaternion ordered x, y, z, w.
        /// </param>
        /// <returns>
        /// The normalized quaternion conjugate ordered x, y, z, w.
        /// </returns>
        public static ArrayOf<double> Conjugate(ReadOnlySpan<double> orientation)
        {
            ArrayOf<double> normalized = Normalize(orientation);
            ReadOnlySpan<double> q = normalized.Span;
            return ArrayOf.Create([-q[0], -q[1], -q[2], q[3]]);
        }

        /// <summary>
        /// Rotates a three-dimensional vector by a quaternion.
        /// </summary>
        /// <param name="orientation">
        /// The quaternion ordered x, y, z, w.
        /// </param>
        /// <param name="vector">
        /// The vector ordered x, y, z.
        /// </param>
        /// <returns>
        /// The rotated vector ordered x, y, z.
        /// </returns>
        public static ArrayOf<double> RotateVector(ReadOnlySpan<double> orientation, ReadOnlySpan<double> vector)
        {
            RequireLength(vector, 3, nameof(vector));
            ArrayOf<double> normalized = Normalize(orientation);
            ReadOnlySpan<double> q = normalized.Span;

            double qx = q[0];
            double qy = q[1];
            double qz = q[2];
            double qw = q[3];
            double vx = vector[0];
            double vy = vector[1];
            double vz = vector[2];

            double tx = 2.0 * ((qy * vz) - (qz * vy));
            double ty = 2.0 * ((qz * vx) - (qx * vz));
            double tz = 2.0 * ((qx * vy) - (qy * vx));

            return ArrayOf.Create([
                vx + (qw * tx) + ((qy * tz) - (qz * ty)),
                vy + (qw * ty) + ((qz * tx) - (qx * tz)),
                vz + (qw * tz) + ((qx * ty) - (qy * tx))
            ]);
        }

        /// <summary>
        /// Composes a child pose expressed within a parent pose.
        /// </summary>
        /// <param name="parent">
        /// The parent pose to apply first.
        /// </param>
        /// <param name="child">
        /// The child pose expressed in the parent pose frame.
        /// </param>
        /// <returns>
        /// The composed pose in the parent pose frame.
        /// </returns>
        public static Pose3DDataType Compose(Pose3DDataType parent, Pose3DDataType child)
        {
            if (parent is null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            if (child is null)
            {
                throw new ArgumentNullException(nameof(child));
            }

            ReadOnlySpan<double> parentPosition = GetPosition(parent);
            ReadOnlySpan<double> childPosition = GetPosition(child);
            ArrayOf<double> rotated = RotateVector(GetOrientation(parent), childPosition);
            ArrayOf<double> orientation = Multiply(GetOrientation(parent), GetOrientation(child));

            return new Pose3DDataType
            {
                FrameId = parent.FrameId,
                Position = ArrayOf.Create([
                    parentPosition[0] + rotated[0],
                    parentPosition[1] + rotated[1],
                    parentPosition[2] + rotated[2]
                ]),
                Orientation = orientation
            };
        }

        /// <summary>
        /// Inverts a pose.
        /// </summary>
        /// <param name="pose">
        /// The pose to invert.
        /// </param>
        /// <returns>
        /// The inverse pose.
        /// </returns>
        public static Pose3DDataType Invert(Pose3DDataType pose)
        {
            if (pose is null)
            {
                throw new ArgumentNullException(nameof(pose));
            }

            ArrayOf<double> inverseOrientation = Conjugate(GetOrientation(pose));
            ReadOnlySpan<double> position = GetPosition(pose);
            ArrayOf<double> inversePosition = RotateVector(
                inverseOrientation.Span,
                [-position[0], -position[1], -position[2]]);

            return new Pose3DDataType
            {
                FrameId = pose.FrameId,
                Position = inversePosition,
                Orientation = inverseOrientation
            };
        }

        /// <summary>
        /// Validates a Robot Intent pose shape and unit quaternion norm.
        /// </summary>
        /// <param name="pose">
        /// The pose to validate.
        /// </param>
        /// <param name="tolerance">
        /// The maximum allowed absolute difference between the quaternion norm and one.
        /// </param>
        /// <param name="error">
        /// The validation failure reason, or <c>null</c> when validation succeeds.
        /// </param>
        /// <returns>
        /// <c>true</c> if the pose is valid.
        /// </returns>
        public static bool TryValidate(Pose3DDataType? pose, double tolerance, out string? error)
        {
            if (pose is null)
            {
                error = "The pose is required.";
                return false;
            }
            if (pose.Position.Count != 3)
            {
                error = "The pose position must contain exactly three components.";
                return false;
            }
            if (pose.Orientation.Count != 4)
            {
                error = "The pose orientation must contain exactly four components.";
                return false;
            }
            if (!IsUnitQuaternion(pose.Orientation.Span, tolerance))
            {
                error = "The pose orientation quaternion norm is outside the allowed tolerance.";
                return false;
            }

            error = null;
            return true;
        }

        internal static Pose3DDataType Identity(string frameId)
        {
            return new Pose3DDataType
            {
                FrameId = frameId,
                Position = ArrayOf.Create([0.0, 0.0, 0.0]),
                Orientation = ArrayOf.Create([0.0, 0.0, 0.0, 1.0])
            };
        }

        internal static Pose3DDataType CopyPose(Pose3DDataType pose)
        {
            if (pose is null)
            {
                throw new ArgumentNullException(nameof(pose));
            }

            return new Pose3DDataType
            {
                FrameId = pose.FrameId,
                Position = ArrayOf.Create(pose.Position.Span),
                Orientation = ArrayOf.Create(pose.Orientation.Span)
            };
        }

        private static ReadOnlySpan<double> GetPosition(Pose3DDataType pose)
        {
            if (pose.Position.Count != 3)
            {
                throw new ArgumentException("The pose position must contain exactly three components.", nameof(pose));
            }

            return pose.Position.Span;
        }

        private static ReadOnlySpan<double> GetOrientation(Pose3DDataType pose)
        {
            if (pose.Orientation.Count != 4)
            {
                throw new ArgumentException("The pose orientation must contain exactly four components.", nameof(pose));
            }

            return pose.Orientation.Span;
        }

        private static void RequireLength(ReadOnlySpan<double> values, int length, string parameterName)
        {
            if (values.Length != length)
            {
                throw new ArgumentException($"The value must contain exactly {length} components.", parameterName);
            }
        }

        private static double Norm(ReadOnlySpan<double> values)
        {
            double sum = 0.0;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i] * values[i];
            }

            return Math.Sqrt(sum);
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }
            if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        /// <summary>
        /// The normative unit for Cartesian position values is metres.
        /// </summary>
        public const string PositionUnit = "m";

        /// <summary>
        /// The normative unit for revolute joint targets is radians.
        /// </summary>
        public const string RevoluteJointTargetUnit = "rad";

        /// <summary>
        /// The normative unit for prismatic joint targets is metres.
        /// </summary>
        public const string PrismaticJointTargetUnit = "m";

        /// <summary>
        /// The normative unit for force values is newtons.
        /// </summary>
        public const string ForceUnit = "N";

        /// <summary>
        /// The normative unit for duration values is milliseconds.
        /// </summary>
        public const string DurationUnit = "ms";
    }
}
