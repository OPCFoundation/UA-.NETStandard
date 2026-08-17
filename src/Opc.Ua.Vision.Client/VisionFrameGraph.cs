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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// Traverses the Server's <c>CoordinateFrameType</c> tree and composes transforms
    /// between two named frames. Uses the exact conventions of §5.12: right-handed
    /// frames, orientation as a unit quaternion ordered (x, y, z, w), position in
    /// metres, tolerance 1e-6 for the unit-norm check.
    /// </summary>
    /// <remarks>
    /// The graph is intentionally a facade with no state: every operation re-reads
    /// the relevant frames, so subsequent calls always see the Server's current
    /// values. A pose is composed by walking from the source frame up to a common
    /// ancestor, then down to the target, multiplying transforms in order.
    /// </remarks>
    public sealed class VisionFrameGraph
    {
        private const int MaxChainDepth = 32;

        private const double UnitQuaternionTolerance = 1e-6;

        private readonly VisionClientOperations m_operations;

        internal VisionFrameGraph(VisionClientOperations operations)
        {
            m_operations = operations ?? throw new ArgumentNullException(nameof(operations));
        }

        /// <summary>
        /// Reads a single frame snapshot from its NodeId.
        /// </summary>
        /// <param name="frameNodeId">
        /// The <c>CoordinateFrameType</c> instance NodeId.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionFrameSnapshot> ReadAsync(
            NodeId frameNodeId,
            CancellationToken cancellationToken = default)
        {
            if (frameNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Frame NodeId must not be null.", nameof(frameNodeId));
            }
            string[] members =
            [
                BrowseNames.FrameId,
                BrowseNames.Role,
                BrowseNames.ParentFrame,
                BrowseNames.Transform
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                frameNodeId, members, cancellationToken).ConfigureAwait(false);
            var toRead = new List<NodeId>();
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                if (!nodes[ii].IsNull)
                {
                    toRead.Add(nodes[ii]);
                }
            }
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(
                toRead, cancellationToken).ConfigureAwait(false);
            int cursor = 0;
            string? frameId = null;
            if (!nodes[0].IsNull)
            {
                frameId = VisionClientOperations.ReadString(values[cursor++]);
            }
            VisionFrameRoleEnum role = default;
            if (!nodes[1].IsNull)
            {
                VisionClientOperations.TryReadEnum(values[cursor++], out role);
            }
            NodeId parent = NodeId.Null;
            if (!nodes[2].IsNull)
            {
                VisionClientOperations.TryReadNodeId(values[cursor++], out parent);
            }
            VisionPose3DDataType? transform = null;
            if (!nodes[3].IsNull)
            {
                DataValue value = values[cursor++];
#pragma warning disable CS8600 // TryGetValue uses [MaybeNullWhen(false)] on encodeable overloads.
                if (value.WrappedValue.TryGetValue(
                        out VisionPose3DDataType structure,
                        m_operations.Session.MessageContext))
                {
                    transform = structure;
                }
#pragma warning restore CS8600
            }
            return new VisionFrameSnapshot
            {
                NodeId = frameNodeId,
                FrameId = frameId,
                Role = role,
                ParentFrameId = parent,
                Transform = transform
            };
        }

        /// <summary>
        /// Composes the pose <paramref name="pose"/>, expressed in
        /// <paramref name="fromFrameId"/>, into <paramref name="toFrameId"/>, walking
        /// the <c>ParentFrame</c> chain per §5.12.
        /// </summary>
        /// <param name="pose">
        /// The pose to compose.
        /// </param>
        /// <param name="fromFrameId">
        /// The frame the pose is currently expressed in.
        /// </param>
        /// <param name="toFrameId">
        /// The frame the pose should be expressed in.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Any argument is <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The frame chain cannot be resolved, is longer than <c>32</c>, contains a
        /// cycle, or carries a non-unit quaternion within tolerance <c>1e-6</c>.
        /// </exception>
        public async Task<VisionPose3DDataType> ComposeAsync(
            VisionPose3DDataType pose,
            NodeId fromFrameId,
            NodeId toFrameId,
            CancellationToken cancellationToken = default)
        {
            if (pose is null)
            {
                throw new ArgumentNullException(nameof(pose));
            }
            if (fromFrameId.IsNull)
            {
                throw new ArgumentException(
                    "From-frame NodeId must not be null.", nameof(fromFrameId));
            }
            if (toFrameId.IsNull)
            {
                throw new ArgumentException(
                    "To-frame NodeId must not be null.", nameof(toFrameId));
            }
            VisionPose3DDataType transform = await ComposeTransformAsync(
                fromFrameId, toFrameId, cancellationToken).ConfigureAwait(false);
            return Compose(transform, pose, targetFrameId: null);
        }

        /// <summary>
        /// Composes the identity of frame <paramref name="fromFrameId"/> into
        /// <paramref name="toFrameId"/> — equivalent to a <c>ComposeAsync</c> call
        /// with an origin pose.
        /// </summary>
        /// <param name="fromFrameId">
        /// The source frame NodeId.
        /// </param>
        /// <param name="toFrameId">
        /// The target frame NodeId.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task<VisionPose3DDataType> ComposeTransformAsync(
            NodeId fromFrameId,
            NodeId toFrameId,
            CancellationToken cancellationToken = default)
        {
            if (fromFrameId.IsNull)
            {
                throw new ArgumentException(
                    "From-frame NodeId must not be null.", nameof(fromFrameId));
            }
            if (toFrameId.IsNull)
            {
                throw new ArgumentException(
                    "To-frame NodeId must not be null.", nameof(toFrameId));
            }
            return ComposeTransformCoreAsync(fromFrameId, toFrameId, cancellationToken);
        }

        private async Task<VisionPose3DDataType> ComposeTransformCoreAsync(
            NodeId fromFrameId,
            NodeId toFrameId,
            CancellationToken cancellationToken)
        {
            var fromChain = await WalkToRootAsync(fromFrameId, cancellationToken)
                .ConfigureAwait(false);
            var toChain = await WalkToRootAsync(toFrameId, cancellationToken)
                .ConfigureAwait(false);
            (int fromIndex, int toIndex) = FindCommonAncestor(fromChain, toChain);
            if (fromIndex < 0 || toIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNoMatch,
                    "Frames '{0}' and '{1}' do not share a common ancestor.",
                    fromFrameId,
                    toFrameId);
            }
            VisionPose3DDataType up = Identity(FrameIdOf(fromChain, 0));
            for (int ii = 0; ii < fromIndex; ii++)
            {
                VisionFrameSnapshot frame = fromChain[ii];
                RequireTransform(frame);
                up = Compose(frame.Transform!, up, FrameIdOf(fromChain, ii + 1));
            }
            VisionPose3DDataType downInverse = Identity(FrameIdOf(toChain, toIndex));
            for (int ii = 0; ii < toIndex; ii++)
            {
                VisionFrameSnapshot frame = toChain[ii];
                RequireTransform(frame);
                downInverse = Compose(
                    frame.Transform!,
                    downInverse,
                    FrameIdOf(toChain, ii + 1));
            }
            VisionPose3DDataType down = Invert(downInverse, FrameIdOf(toChain, 0));
            return Compose(down, up, FrameIdOf(toChain, 0));
        }

        private async Task<List<VisionFrameSnapshot>> WalkToRootAsync(
            NodeId startFrameId,
            CancellationToken cancellationToken)
        {
            var chain = new List<VisionFrameSnapshot>(4);
            var visited = new HashSet<NodeId>();
            NodeId current = startFrameId;
            for (int depth = 0; depth < MaxChainDepth; depth++)
            {
                if (!visited.Add(current))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidArgument,
                        "Frame chain starting at '{0}' contains a cycle.",
                        startFrameId);
                }
                VisionFrameSnapshot snapshot = await ReadAsync(current, cancellationToken)
                    .ConfigureAwait(false);
                chain.Add(snapshot);
                if (snapshot.ParentFrameId.IsNull)
                {
                    return chain;
                }
                current = snapshot.ParentFrameId;
            }
            throw ServiceResultException.Create(
                StatusCodes.BadInvalidArgument,
                "Frame chain starting at '{0}' exceeds the {1}-frame limit.",
                startFrameId,
                MaxChainDepth);
        }

        private static (int FromIndex, int ToIndex) FindCommonAncestor(
            List<VisionFrameSnapshot> fromChain,
            List<VisionFrameSnapshot> toChain)
        {
            for (int ii = 0; ii < fromChain.Count; ii++)
            {
                for (int jj = 0; jj < toChain.Count; jj++)
                {
                    if (fromChain[ii].NodeId == toChain[jj].NodeId)
                    {
                        return (ii, jj);
                    }
                }
            }
            return (-1, -1);
        }

        private static string? FrameIdOf(List<VisionFrameSnapshot> chain, int index)
        {
            if (index < 0 || index >= chain.Count)
            {
                return null;
            }
            return chain[index].FrameId;
        }

        private static void RequireTransform(VisionFrameSnapshot frame)
        {
            if (frame.Transform is null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotFound,
                    "Frame '{0}' does not report a Transform to its parent.",
                    frame.NodeId);
            }
        }

        private static VisionPose3DDataType Identity(string? frameId)
        {
            return new VisionPose3DDataType
            {
                FrameId = frameId ?? string.Empty,
                Position = [0.0, 0.0, 0.0],
                Orientation = [0.0, 0.0, 0.0, 1.0],
                Covariance = ArrayOf<double>.Empty
            };
        }

        private static VisionPose3DDataType Compose(
            VisionPose3DDataType outer,
            VisionPose3DDataType inner,
            string? targetFrameId)
        {
            (double outerX, double outerY, double outerZ) = ReadPosition(outer);
            (double outerQx, double outerQy, double outerQz, double outerQw) =
                ReadOrientation(outer);
            (double innerX, double innerY, double innerZ) = ReadPosition(inner);
            (double innerQx, double innerQy, double innerQz, double innerQw) =
                ReadOrientation(inner);

            (double rx, double ry, double rz) = RotateVector(
                outerQx, outerQy, outerQz, outerQw, innerX, innerY, innerZ);

            (double qx, double qy, double qz, double qw) = MultiplyQuaternions(
                outerQx, outerQy, outerQz, outerQw,
                innerQx, innerQy, innerQz, innerQw);

            return new VisionPose3DDataType
            {
                FrameId = targetFrameId ?? inner.FrameId ?? string.Empty,
                Position = [outerX + rx, outerY + ry, outerZ + rz],
                Orientation = [qx, qy, qz, qw],
                Covariance = ArrayOf<double>.Empty
            };
        }

        private static VisionPose3DDataType Invert(
            VisionPose3DDataType pose,
            string? targetFrameId)
        {
            (double x, double y, double z) = ReadPosition(pose);
            (double qx, double qy, double qz, double qw) = ReadOrientation(pose);
            (double invQx, double invQy, double invQz, double invQw) =
                (-qx, -qy, -qz, qw);
            (double rx, double ry, double rz) = RotateVector(
                invQx, invQy, invQz, invQw, -x, -y, -z);
            return new VisionPose3DDataType
            {
                FrameId = targetFrameId ?? string.Empty,
                Position = [rx, ry, rz],
                Orientation = [invQx, invQy, invQz, invQw],
                Covariance = ArrayOf<double>.Empty
            };
        }

        private static (double X, double Y, double Z) ReadPosition(
            VisionPose3DDataType pose)
        {
            ArrayOf<double> position = pose.Position;
            if (position.Count < 3)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadOutOfRange,
                    "Pose has {0} position components, expected 3.",
                    position.Count);
            }
            return (position[0], position[1], position[2]);
        }

        private static (double X, double Y, double Z, double W) ReadOrientation(
            VisionPose3DDataType pose)
        {
            ArrayOf<double> q = pose.Orientation;
            if (q.Count != 4)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadOutOfRange,
                    "Pose orientation has {0} components, expected 4 (x, y, z, w).",
                    q.Count);
            }
            double qx = q[0];
            double qy = q[1];
            double qz = q[2];
            double qw = q[3];
            double norm = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            if (Math.Abs(norm - 1.0) > UnitQuaternionTolerance)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadOutOfRange,
                    "Pose orientation quaternion norm '{0}' is outside the 1e-6 " +
                    "unit-norm tolerance.",
                    norm);
            }
            return (qx, qy, qz, qw);
        }

        private static (double X, double Y, double Z, double W) MultiplyQuaternions(
            double ax, double ay, double az, double aw,
            double bx, double by, double bz, double bw)
        {
            double w = aw * bw - ax * bx - ay * by - az * bz;
            double x = aw * bx + ax * bw + ay * bz - az * by;
            double y = aw * by - ax * bz + ay * bw + az * bx;
            double z = aw * bz + ax * by - ay * bx + az * bw;
            return (x, y, z, w);
        }

        private static (double X, double Y, double Z) RotateVector(
            double qx, double qy, double qz, double qw,
            double vx, double vy, double vz)
        {
            double tx = 2.0 * (qy * vz - qz * vy);
            double ty = 2.0 * (qz * vx - qx * vz);
            double tz = 2.0 * (qx * vy - qy * vx);
            double rx = vx + qw * tx + (qy * tz - qz * ty);
            double ry = vy + qw * ty + (qz * tx - qx * tz);
            double rz = vz + qw * tz + (qx * ty - qy * tx);
            return (rx, ry, rz);
        }
    }
}
