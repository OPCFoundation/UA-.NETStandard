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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using Opc.Ua;
using Opc.Ua.RobotIntent;
using Opc.Ua.Vision;

namespace Vision.BinPickingCell
{
    internal readonly record struct BinPickingTarget(
        string ClassLabel,
        double WorldX,
        double WorldY,
        double WorldZ,
        DateTime TimestampUtc,
        string ResultId,
        string SourceFrameId);

    internal interface IBinPickingTargetProvider
    {
        void PublishWorldState(
            string resultId,
            DateTimeUtc timestamp,
            IReadOnlyList<BinPickingPartSnapshot> parts);

        void PublishDetections(
            string resultId,
            DateTimeUtc timestamp,
            ArrayOf<VisionDetectionDataType> detections,
            VisionPose3DDataType cameraInWorld,
            string cameraFrameId);

        bool TryResolve(string classLabel, out BinPickingTarget target);
    }

    /// <summary>
    /// Provides the pose a Pick should target for the class selected by Vision.
    /// </summary>
    internal sealed class BinPickingTargetProvider : IBinPickingTargetProvider
    {
        public BinPickingTargetProvider(
            BinPickingWorldState worldState,
            BinPickingCellOptions options)
        {
            m_worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void PublishWorldState(
            string resultId,
            DateTimeUtc timestamp,
            IReadOnlyList<BinPickingPartSnapshot> parts)
        {
            DateTime created = timestamp.IsNull ? DateTime.UtcNow : timestamp.ToDateTime();
            for (int ii = 0; ii < parts.Count; ii++)
            {
                BinPickingPartSnapshot part = parts[ii];
                if (part.Location != BinPickingPartLocation.InBin)
                {
                    continue;
                }
                m_targets[part.Part.ClassLabel] = new BinPickingTarget(
                    part.Part.ClassLabel,
                    part.WorldX,
                    part.WorldY,
                    part.WorldZ,
                    created,
                    resultId,
                    WorldFrameId);
            }
        }

        public void PublishDetections(
            string resultId,
            DateTimeUtc timestamp,
            ArrayOf<VisionDetectionDataType> detections,
            VisionPose3DDataType cameraInWorld,
            string cameraFrameId)
        {
            if (string.IsNullOrWhiteSpace(cameraFrameId))
            {
                throw new ArgumentException("A camera frame id is required.", nameof(cameraFrameId));
            }
            ReadOnlySpan<double> cameraPosition = cameraInWorld.Position.Span;
            ReadOnlySpan<double> cameraOrientation = cameraInWorld.Orientation.Span;
            if (cameraPosition.Length < 3 || cameraOrientation.Length < 4)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "The camera-in-world calibration must carry a 3D position and quaternion.");
            }
            DateTime created = timestamp.IsNull ? DateTime.UtcNow : timestamp.ToDateTime();
            IReadOnlyList<BinPickingPartSnapshot> parts = m_worldState.Snapshot();
            ReadOnlySpan<VisionDetectionDataType> values = detections.Span;
            var acceptedTargets = new List<BinPickingTarget>(values.Length);
            for (int ii = 0; ii < values.Length; ii++)
            {
                VisionDetectionDataType detection = values[ii];
                if (!detection.HasPose || string.IsNullOrWhiteSpace(detection.ClassLabel))
                {
                    continue;
                }
                if (!string.Equals(
                    detection.Pose.FrameId,
                    cameraFrameId,
                    StringComparison.Ordinal))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        $"Detection {ii} Pose.FrameId '{detection.Pose.FrameId}' does not match " +
                        $"the calibrated camera frame '{cameraFrameId}'.");
                }
                ReadOnlySpan<double> local = detection.Pose.Position.Span;
                if (local.Length < 3)
                {
                    continue;
                }
                ArrayOf<double> rotated = PoseMath.RotateVector(cameraOrientation, local);
                ReadOnlySpan<double> offset = rotated.Span;
                double worldX = cameraPosition[0] + offset[0];
                double worldY = cameraPosition[1] + offset[1];
                double worldZ = cameraPosition[2] + offset[2];
                BinPickingPartSnapshot? part = FindPart(parts, detection.ClassLabel);
                if (part == null || part.Location == BinPickingPartLocation.Held)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        FormattableString.Invariant(
                            $"Detection {ii} class '{detection.ClassLabel}' is not available to pick."));
                }
                double residual = Distance(part, worldX, worldY, worldZ);
                if (residual > MaximumWorldResidualMetres)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Detection {0} class '{1}' is {2:F3} m from the simulated part, " +
                            "above the {3:F3} m limit.",
                            ii,
                            detection.ClassLabel,
                            residual,
                            MaximumWorldResidualMetres));
                }
                acceptedTargets.Add(new BinPickingTarget(
                    detection.ClassLabel,
                    worldX,
                    worldY,
                    worldZ,
                    created,
                    resultId,
                    cameraFrameId));
            }
            for (int ii = 0; ii < acceptedTargets.Count; ii++)
            {
                BinPickingTarget target = acceptedTargets[ii];
                m_targets[target.ClassLabel] = target;
            }
        }

        public bool TryResolve(string classLabel, out BinPickingTarget target)
        {
            if (m_targets.TryGetValue(classLabel, out target) &&
                DateTime.UtcNow - target.TimestampUtc <= TargetLifetime)
            {
                return true;
            }
            _ = m_targets.TryRemove(classLabel, out _);
            if (m_options.InferenceLocation != BinPickingInferenceLocation.OnServer)
            {
                target = default;
                return false;
            }

            IReadOnlyList<BinPickingPartSnapshot> parts = m_worldState.Snapshot();
            for (int ii = 0; ii < parts.Count; ii++)
            {
                BinPickingPartSnapshot part = parts[ii];
                if (string.Equals(part.Part.ClassLabel, classLabel, StringComparison.Ordinal))
                {
                    target = new BinPickingTarget(
                        classLabel,
                        part.WorldX,
                        part.WorldY,
                        part.WorldZ,
                        DateTime.UtcNow,
                        "simulation-world-state",
                        WorldFrameId);
                    return true;
                }
            }
            target = default;
            return false;
        }

        private static BinPickingPartSnapshot? FindPart(
            IReadOnlyList<BinPickingPartSnapshot> parts,
            string classLabel)
        {
            for (int ii = 0; ii < parts.Count; ii++)
            {
                if (string.Equals(
                    parts[ii].Part.ClassLabel,
                    classLabel,
                    StringComparison.Ordinal))
                {
                    return parts[ii];
                }
            }
            return null;
        }

        private static double Distance(
            BinPickingPartSnapshot part,
            double worldX,
            double worldY,
            double worldZ)
        {
            double x = part.WorldX - worldX;
            double y = part.WorldY - worldY;
            double z = part.WorldZ - worldZ;
            return Math.Sqrt((x * x) + (y * y) + (z * z));
        }

        private const string WorldFrameId = "world";
        private const double MaximumWorldResidualMetres = 0.08;
        private static readonly TimeSpan TargetLifetime = TimeSpan.FromSeconds(10);
        private readonly BinPickingWorldState m_worldState;
        private readonly BinPickingCellOptions m_options;

        private readonly ConcurrentDictionary<string, BinPickingTarget> m_targets =
            new(StringComparer.Ordinal);
    }
}
