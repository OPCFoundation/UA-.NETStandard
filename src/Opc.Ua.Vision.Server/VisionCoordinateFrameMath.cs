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
using Opc.Ua.Vision;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Composes rigid transforms across a coordinate-frame tree following
    /// the conventions in specification §5.12.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Poses are represented as <see cref="VisionPose3DDataType"/> values:
    /// translation in metres, orientation as a unit quaternion in the
    /// (x, y, z, w) ordering the specification mandates. Composition
    /// applies the child transform then the parent transform, so
    /// <c>Compose(parent, child) = parent ∘ child</c> — first the child's
    /// pose in its parent, then the parent in its parent, and so on up to
    /// the root.
    /// </para>
    /// <para>
    /// Covariance is not composed; the spec's sentinel rules mean the
    /// composed covariance is only accurate when every intermediate pose
    /// carries a full 6×6 matrix in the same order as its position and
    /// orientation. Callers can inspect
    /// <see cref="ArrayOf{T}.IsNull"/> on
    /// <see cref="VisionPose3DDataType.Covariance"/> to decide whether to
    /// suppress the composed covariance.
    /// </para>
    /// </remarks>
    public static class VisionCoordinateFrameMath
    {
        /// <summary>
        /// Position component length as defined by §5.12.
        /// </summary>
        public const int PositionLength = 3;

        /// <summary>
        /// Orientation component length: (x, y, z, w).
        /// </summary>
        public const int OrientationLength = 4;

        /// <summary>
        /// Snapshot of one coordinate frame, containing the frame id, the
        /// optional parent frame id and the transform from this frame to
        /// its parent.
        /// </summary>
        /// <remarks>
        /// <see cref="Transform"/>'s <c>FrameId</c> is the parent frame
        /// per §5.12 — the specification's frame-precedence rule states
        /// that the transform's <c>FrameId</c> equals the target frame's
        /// identifier.
        /// </remarks>
        public sealed record CoordinateFrameSnapshot(
            string FrameId,
            VisionFrameRoleEnum Role,
            string ParentFrameId,
            VisionPose3DDataType Transform);

        /// <summary>
        /// Returns the identity pose in <paramref name="frameId"/>.
        /// </summary>
        public static VisionPose3DDataType Identity(string frameId)
        {
            return new VisionPose3DDataType
            {
                FrameId = frameId ?? string.Empty,
                Position = new double[] { 0.0, 0.0, 0.0 },
                Orientation = new double[] { 0.0, 0.0, 0.0, 1.0 },
                Covariance = ArrayOf<double>.Empty
            };
        }

        /// <summary>
        /// Composes <paramref name="parent"/> ∘ <paramref name="child"/>
        /// where the child's pose is expressed in the parent's frame and
        /// the result is the child's pose in <paramref name="parent"/>'s
        /// parent.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// One of the poses has malformed position or orientation.
        /// </exception>
        public static VisionPose3DDataType Compose(
            in VisionPose3DDataType parent,
            in VisionPose3DDataType child)
        {
            ReadOnlySpan<double> pp = ExtractPosition(parent, nameof(parent));
            ReadOnlySpan<double> pq = ExtractOrientation(parent, nameof(parent));
            ReadOnlySpan<double> cp = ExtractPosition(child, nameof(child));
            ReadOnlySpan<double> cq = ExtractOrientation(child, nameof(child));

            Span<double> rotated = stackalloc double[3];
            RotateVector(pq, cp, rotated);
            double[] position = new double[]
            {
                pp[0] + rotated[0],
                pp[1] + rotated[1],
                pp[2] + rotated[2]
            };

            Span<double> composed = stackalloc double[4];
            MultiplyQuaternions(pq, cq, composed);
            NormalizeQuaternion(composed);
            double[] orientation = new double[]
            {
                composed[0],
                composed[1],
                composed[2],
                composed[3]
            };
            return new VisionPose3DDataType
            {
                FrameId = parent.FrameId ?? string.Empty,
                Position = position,
                Orientation = orientation,
                Covariance = ArrayOf<double>.Empty
            };
        }

        /// <summary>
        /// Returns the inverse of <paramref name="pose"/>. Assumes the
        /// orientation is a unit quaternion in (x, y, z, w) order.
        /// </summary>
        public static VisionPose3DDataType Invert(in VisionPose3DDataType pose)
        {
            ReadOnlySpan<double> p = ExtractPosition(pose, nameof(pose));
            ReadOnlySpan<double> q = ExtractOrientation(pose, nameof(pose));
            Span<double> qInv = stackalloc double[4] { -q[0], -q[1], -q[2], q[3] };
            Span<double> negatedP = stackalloc double[3] { -p[0], -p[1], -p[2] };
            Span<double> rotated = stackalloc double[3];
            RotateVector(qInv, negatedP, rotated);
            return new VisionPose3DDataType
            {
                FrameId = pose.FrameId ?? string.Empty,
                Position = new double[] { rotated[0], rotated[1], rotated[2] },
                Orientation = new double[] { qInv[0], qInv[1], qInv[2], qInv[3] },
                Covariance = ArrayOf<double>.Empty
            };
        }

        /// <summary>
        /// Walks <paramref name="frames"/> starting from
        /// <paramref name="sourceFrameId"/> up to <paramref name="targetFrameId"/>
        /// and composes the transforms it visits into the pose of
        /// <paramref name="sourceFrameId"/> expressed in
        /// <paramref name="targetFrameId"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// One of the required arguments is null.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The source and target frames are not connected in the tree, or
        /// a cycle is detected, or a frame identifier violates §5.12's
        /// non-empty rule.
        /// </exception>
        public static VisionPose3DDataType TransformFromTo(
            IReadOnlyDictionary<string, CoordinateFrameSnapshot> frames,
            string sourceFrameId,
            string targetFrameId)
        {
            if (frames == null)
            {
                throw new ArgumentNullException(nameof(frames));
            }
            if (string.IsNullOrEmpty(sourceFrameId))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Coordinate frame identifiers must be non-empty (§5.12).");
            }
            if (string.IsNullOrEmpty(targetFrameId))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Coordinate frame identifiers must be non-empty (§5.12).");
            }
            if (string.Equals(sourceFrameId, targetFrameId, StringComparison.Ordinal))
            {
                return Identity(targetFrameId);
            }

            List<CoordinateFrameSnapshot> sourceToRoot = WalkToRoot(frames, sourceFrameId);
            List<CoordinateFrameSnapshot> targetToRoot = WalkToRoot(frames, targetFrameId);

            int commonSourceIndex = sourceToRoot.Count - 1;
            int commonTargetIndex = targetToRoot.Count - 1;
            while (commonSourceIndex > 0 &&
                   commonTargetIndex > 0 &&
                   string.Equals(
                       sourceToRoot[commonSourceIndex].FrameId,
                       targetToRoot[commonTargetIndex].FrameId,
                       StringComparison.Ordinal))
            {
                commonSourceIndex--;
                commonTargetIndex--;
            }

            VisionPose3DDataType pose = Identity(sourceToRoot[0].FrameId);
            for (int ii = 0; ii <= commonSourceIndex; ii++)
            {
                pose = Compose(sourceToRoot[ii].Transform, pose);
            }
            for (int ii = commonTargetIndex; ii >= 0; ii--)
            {
                pose = Compose(Invert(targetToRoot[ii].Transform), pose);
            }
            pose = new VisionPose3DDataType
            {
                FrameId = targetFrameId,
                Position = pose.Position,
                Orientation = pose.Orientation,
                Covariance = ArrayOf<double>.Empty
            };
            return pose;
        }

        private static List<CoordinateFrameSnapshot> WalkToRoot(
            IReadOnlyDictionary<string, CoordinateFrameSnapshot> frames,
            string frameId)
        {
            var chain = new List<CoordinateFrameSnapshot>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string current = frameId;
            while (!string.IsNullOrEmpty(current))
            {
                if (!frames.TryGetValue(current, out CoordinateFrameSnapshot? snapshot))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdUnknown,
                        "Coordinate frame '{0}' is not registered in the tree.",
                        current);
                }
                if (!visited.Add(current))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidArgument,
                        "Coordinate frame '{0}' participates in a cycle.",
                        current);
                }
                chain.Add(snapshot);
                current = snapshot.ParentFrameId;
            }
            return chain;
        }

        private static ReadOnlySpan<double> ExtractPosition(
            in VisionPose3DDataType pose,
            string parameterName)
        {
            ReadOnlySpan<double> span = pose.Position.Span;
            if (span.Length != PositionLength)
            {
                throw new ArgumentException(
                    "Position must be a length-3 vector (§5.12).",
                    parameterName);
            }
            return span;
        }

        private static ReadOnlySpan<double> ExtractOrientation(
            in VisionPose3DDataType pose,
            string parameterName)
        {
            ReadOnlySpan<double> span = pose.Orientation.Span;
            if (span.Length != OrientationLength)
            {
                throw new ArgumentException(
                    "Orientation must be a length-4 quaternion (x, y, z, w) per §5.12.",
                    parameterName);
            }
            return span;
        }

        private static void RotateVector(
            ReadOnlySpan<double> quaternion,
            ReadOnlySpan<double> vector,
            Span<double> result)
        {
            double qx = quaternion[0];
            double qy = quaternion[1];
            double qz = quaternion[2];
            double qw = quaternion[3];
            double vx = vector[0];
            double vy = vector[1];
            double vz = vector[2];
            double tx = 2.0 * ((qy * vz) - (qz * vy));
            double ty = 2.0 * ((qz * vx) - (qx * vz));
            double tz = 2.0 * ((qx * vy) - (qy * vx));
            result[0] = vx + (qw * tx) + ((qy * tz) - (qz * ty));
            result[1] = vy + (qw * ty) + ((qz * tx) - (qx * tz));
            result[2] = vz + (qw * tz) + ((qx * ty) - (qy * tx));
        }

        private static void MultiplyQuaternions(
            ReadOnlySpan<double> left,
            ReadOnlySpan<double> right,
            Span<double> result)
        {
            double lx = left[0];
            double ly = left[1];
            double lz = left[2];
            double lw = left[3];
            double rx = right[0];
            double ry = right[1];
            double rz = right[2];
            double rw = right[3];
            result[0] = (lw * rx) + (lx * rw) + (ly * rz) - (lz * ry);
            result[1] = (lw * ry) - (lx * rz) + (ly * rw) + (lz * rx);
            result[2] = (lw * rz) + (lx * ry) - (ly * rx) + (lz * rw);
            result[3] = (lw * rw) - (lx * rx) - (ly * ry) - (lz * rz);
        }

        private static void NormalizeQuaternion(Span<double> quaternion)
        {
            double norm = Math.Sqrt(
                (quaternion[0] * quaternion[0]) +
                (quaternion[1] * quaternion[1]) +
                (quaternion[2] * quaternion[2]) +
                (quaternion[3] * quaternion[3]));

            // A zero-norm quaternion carries no orientation. Substituting the identity would
            // compose a pose that looks plausible and points the wrong way, which for a grasp
            // is worse than a refusal, so it is reported like every other malformed input here.
            if (norm <= 0.0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "An orientation quaternion has zero norm and does not describe a rotation.");
            }

            quaternion[0] /= norm;
            quaternion[1] /= norm;
            quaternion[2] /= norm;
            quaternion[3] /= norm;
        }
    }
}
