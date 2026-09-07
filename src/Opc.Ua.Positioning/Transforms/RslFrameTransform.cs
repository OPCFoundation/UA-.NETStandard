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

namespace Opc.Ua.Positioning
{
    /// <summary>
    /// An immutable rigid body transform expressed with the Relative Spatial
    /// Location (RSL, OPC 10000-210) frame conventions.
    /// <para>
    /// The coordinate system is right handed. The three orientation angles of a
    /// <see cref="ThreeDOrientation"/> follow the RSL Annex B definition:
    /// <c>A</c> is the roll about the X axis, <c>B</c> is the pitch about the Y
    /// axis and <c>C</c> is the yaw about the Z axis. Orientation is an intrinsic
    /// z-y'-x'' rotation which, for column vectors, is the matrix product
    /// <c>Rz(C) * Ry(B) * Rx(A)</c>. A point is transformed as
    /// <c>p' = R * p + t</c>.
    /// </para>
    /// The type is a readonly value; every operation returns a new transform and
    /// never mutates <c>this</c>.
    /// </summary>
    public readonly struct RslFrameTransform : IEquatable<RslFrameTransform>
    {
        private readonly Matrix3x3 m_rotation;
        private readonly Vector3 m_translation;
        private readonly bool m_initialized;

        internal RslFrameTransform(Matrix3x3 rotation, Vector3 translation)
        {
            m_rotation = rotation;
            m_translation = translation;
            m_initialized = true;
        }

        /// <summary>
        /// The identity transform (no rotation, no translation).
        /// </summary>
        public static RslFrameTransform Identity
            => new(Matrix3x3.Identity, Vector3.Zero);

        /// <summary>
        /// Builds a transform from an RSL <see cref="ThreeDFrame"/>. The frame
        /// carries the translation in its <see cref="ThreeDFrame.CartesianCoordinates"/>
        /// and the orientation angles in its <see cref="ThreeDFrame.Orientation"/>.
        /// </summary>
        /// <param name="frame">
        /// The frame to convert.
        /// </param>
        /// <param name="angleUnit">
        /// The unit the orientation angles are expressed in.
        /// </param>
        public static RslFrameTransform FromFrame(ThreeDFrame frame, AngleUnit angleUnit)
        {
            frame.ThrowIfNull(nameof(frame));
            return FromComponents(frame.Orientation, frame.CartesianCoordinates, angleUnit);
        }

        /// <summary>
        /// Builds a transform from an explicit orientation and translation.
        /// </summary>
        /// <param name="orientation">
        /// The RSL orientation angles (roll A, pitch B, yaw C).
        /// </param>
        /// <param name="translation">
        /// The translation of the frame origin.
        /// </param>
        /// <param name="angleUnit">
        /// The unit the orientation angles are expressed in.
        /// </param>
        public static RslFrameTransform FromComponents(
            ThreeDOrientation orientation,
            ThreeDCartesianCoordinates translation,
            AngleUnit angleUnit)
        {
            orientation.ThrowIfNull(nameof(orientation));
            translation.ThrowIfNull(nameof(translation));

            double a = AngleMath.ToRadians(orientation.A, angleUnit);
            double b = AngleMath.ToRadians(orientation.B, angleUnit);
            double c = AngleMath.ToRadians(orientation.C, angleUnit);

            Matrix3x3 rotation = RotationFromEuler(a, b, c);
            var t = new Vector3(translation.X, translation.Y, translation.Z);
            return new RslFrameTransform(rotation, t);
        }

        /// <summary>
        /// Converts the transform back to an RSL <see cref="ThreeDFrame"/>. The
        /// orientation angles are recovered from the rotation using the RSL
        /// z-y'-x'' convention. At the gimbal lock singularity (pitch of plus or
        /// minus ninety degrees) the roll is deterministically resolved to zero
        /// and the yaw absorbs the coupled rotation.
        /// </summary>
        /// <param name="angleUnit">
        /// The unit the returned orientation angles are expressed in.
        /// </param>
        public ThreeDFrame ToFrame(AngleUnit angleUnit)
        {
            EulerFromRotation(Rotation, out double a, out double b, out double c);
            return new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates
                {
                    X = m_translation.X,
                    Y = m_translation.Y,
                    Z = m_translation.Z
                },
                Orientation = new ThreeDOrientation
                {
                    A = AngleMath.FromRadians(a, angleUnit),
                    B = AngleMath.FromRadians(b, angleUnit),
                    C = AngleMath.FromRadians(c, angleUnit)
                }
            };
        }

        /// <summary>
        /// Transforms a point by applying the rotation and then the translation.
        /// </summary>
        /// <param name="point">
        /// The point to transform.
        /// </param>
        public ThreeDCartesianCoordinates TransformPoint(ThreeDCartesianCoordinates point)
        {
            point.ThrowIfNull(nameof(point));
            Vector3 p = Rotation.Transform(new Vector3(point.X, point.Y, point.Z)) + m_translation;
            return new ThreeDCartesianCoordinates { X = p.X, Y = p.Y, Z = p.Z };
        }

        /// <summary>
        /// Rotates a direction vector without applying the translation.
        /// </summary>
        /// <param name="direction">
        /// The direction to rotate.
        /// </param>
        public ThreeDCartesianCoordinates TransformDirection(ThreeDCartesianCoordinates direction)
        {
            direction.ThrowIfNull(nameof(direction));
            Vector3 d = Rotation.Transform(new Vector3(direction.X, direction.Y, direction.Z));
            return new ThreeDCartesianCoordinates { X = d.X, Y = d.Y, Z = d.Z };
        }

        /// <summary>
        /// Returns the composition <c>this ∘ other</c>. The resulting transform
        /// applies <paramref name="other"/> first and then <c>this</c>, i.e.
        /// <c>Compose(other).TransformPoint(p) == this.TransformPoint(other.TransformPoint(p))</c>.
        /// Composition does not commute.
        /// </summary>
        /// <param name="other">
        /// The transform applied before this one.
        /// </param>
        public RslFrameTransform Compose(in RslFrameTransform other)
        {
            Matrix3x3 rotation = Rotation * other.Rotation;
            Vector3 translation = Rotation.Transform(other.m_translation) + m_translation;
            return new RslFrameTransform(rotation, translation);
        }

        /// <summary>
        /// Returns the inverse transform such that
        /// <c>Inverse().Compose(this)</c> is the identity.
        /// </summary>
        public RslFrameTransform Inverse()
        {
            Matrix3x3 inverseRotation = Rotation.Transpose();
            Vector3 inverseTranslation = inverseRotation.Transform(m_translation) * -1.0;
            return new RslFrameTransform(inverseRotation, inverseTranslation);
        }

        /// <summary>
        /// Returns the composition of two transforms. See <see cref="Compose"/>.
        /// </summary>
        public static RslFrameTransform operator *(RslFrameTransform left, RslFrameTransform right)
        {
            return left.Compose(right);
        }

        /// <summary>
        /// Builds the RSL rotation matrix <c>Rz(C) * Ry(B) * Rx(A)</c> for the
        /// given angles in radians.
        /// </summary>
        internal static Matrix3x3 RotationFromEuler(double a, double b, double c)
        {
            double ca = Math.Cos(a);
            double sa = Math.Sin(a);
            double cb = Math.Cos(b);
            double sb = Math.Sin(b);
            double cc = Math.Cos(c);
            double sc = Math.Sin(c);

            return new Matrix3x3(
                cc * cb, (cc * sb * sa) - (sc * ca), (cc * sb * ca) + (sc * sa),
                sc * cb, (sc * sb * sa) + (cc * ca), (sc * sb * ca) - (cc * sa),
                -sb, cb * sa, cb * ca);
        }

        /// <summary>
        /// Recovers the RSL roll, pitch and yaw (radians) from a rotation matrix
        /// following the z-y'-x'' convention with deterministic gimbal lock
        /// handling.
        /// </summary>
        internal static void EulerFromRotation(Matrix3x3 r, out double a, out double b, out double c)
        {
            double cosPitch = Math.Sqrt((r.M00 * r.M00) + (r.M10 * r.M10));
            const double kGimbalThreshold = 1e-9;

            if (cosPitch > kGimbalThreshold)
            {
                b = Math.Atan2(-r.M20, cosPitch);
                a = Math.Atan2(r.M21, r.M22);
                c = Math.Atan2(r.M10, r.M00);
            }
            else
            {
                // Gimbal lock: pitch is +/- 90 degrees. Fix roll to zero and let
                // yaw absorb the remaining rotation.
                b = r.M20 < 0.0 ? Math.PI / 2.0 : -Math.PI / 2.0;
                a = 0.0;
                c = Math.Atan2(-r.M01, r.M11);
            }
        }

        /// <summary>
        /// The rotation matrix as a flat row-major array of nine elements. The
        /// order is <c>M00 M01 M02 M10 M11 M12 M20 M21 M22</c>.
        /// </summary>
        public ArrayOf<double> RotationMatrix
        {
            get
            {
                Matrix3x3 r = Rotation;
                return ArrayOf.Create(
                [
                    r.M00, r.M01, r.M02,
                    r.M10, r.M11, r.M12,
                    r.M20, r.M21, r.M22
                ]);
            }
        }

        /// <summary>
        /// The translation of the transform.
        /// </summary>
        public ThreeDCartesianCoordinates Translation
            => new() { X = m_translation.X, Y = m_translation.Y, Z = m_translation.Z };

        private Matrix3x3 Rotation
            => m_initialized ? m_rotation : Matrix3x3.Identity;

        /// <inheritdoc/>
        public bool Equals(RslFrameTransform other)
        {
            return Rotation.Equals(other.Rotation) && m_translation.Equals(other.m_translation);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is RslFrameTransform other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Rotation, m_translation);
        }

        /// <summary>
        /// Compares two transforms for equality.
        /// </summary>
        public static bool operator ==(RslFrameTransform left, RslFrameTransform right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two transforms for inequality.
        /// </summary>
        public static bool operator !=(RslFrameTransform left, RslFrameTransform right)
        {
            return !left.Equals(right);
        }
    }
}
