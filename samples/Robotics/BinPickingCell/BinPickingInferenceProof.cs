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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;

namespace Vision.BinPickingCell
{
    /// <summary>
    /// Hosted service that exercises the on-server ground-truth
    /// perception path end-to-end, once the Vision pipeline is
    /// available. Runs one inference, dumps every detection to the
    /// console (class label, confidence, 2-D box in pixels and 6-DoF
    /// grasp pose in <c>camera_eih</c>), composes the RedCube pose to
    /// the <c>world</c> frame and cross-checks it against the authored
    /// USD position, then simulates a pick of RedCube and re-runs the
    /// inference to prove the detector tracks the world (the picked
    /// class disappears from the next result).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This diagnostic is the sample-side answer to "does the loop
    /// work"? It never fails the host on a mismatch — it just logs
    /// what it saw. The pass/fail decision is left to the reader of
    /// the console output.
    /// </para>
    /// </remarks>
    internal sealed class BinPickingInferenceProof : BackgroundService
    {
        public BinPickingInferenceProof(
            BinPickingGroundTruthInferenceProvider provider,
            BinPickingWorldState worldState,
            ILogger<BinPickingInferenceProof> logger)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            m_logger.ProofWaiting();
            for (int ii = 0; ii < AttachAttempts; ii++)
            {
                if (m_provider.IsAttached)
                {
                    break;
                }
                await Task.Delay(AttachPollInterval, stoppingToken).ConfigureAwait(false);
            }
            if (!m_provider.IsAttached)
            {
                m_logger.ProofProviderNotAttached();
                return;
            }

            m_logger.ProofBanner("on-server ground-truth inference");

            VisionInferenceRunRequest request = BuildRequest();
            VisionInferenceRunResult first = await m_provider
                .RunInferenceAsync(request, stoppingToken)
                .ConfigureAwait(false);
            if (!ServiceResult.IsGood(first.ServiceResult))
            {
                m_logger.ProofRunFailed(first.ServiceResult.ToString());
                return;
            }

            if (!m_provider.TryGetResult(first.ResultId, out DetectionResultState resultState) ||
                resultState == null ||
                resultState.Detections == null)
            {
                m_logger.ProofResultUnavailable(first.ResultId);
                return;
            }

            ArrayOf<VisionDetectionDataType> detections = resultState.Detections.Value;
            m_logger.ProofResultHeader(first.ResultId, detections.Count);
            var detectionSnapshot = new VisionDetectionDataType[detections.Count];
            for (int ii = 0; ii < detections.Count; ii++)
            {
                detectionSnapshot[ii] = detections[ii];
            }
            foreach (VisionDetectionDataType detection in detectionSnapshot)
            {
                LogDetection(detection);
            }

            VisionDetectionDataType? redCube = TryFindDetection(detectionSnapshot, RedCubeClass);
            if (redCube != null && redCube.HasPose)
            {
                LogComposedPose(redCube);
            }
            else
            {
                m_logger.ProofNoRedCube();
            }

            m_logger.ProofPicking(RedCubeClass);
            const double gripperCarryX = 0.30;
            const double gripperCarryY = 0.05;
            const double gripperCarryZ = 0.95;
            const double fixtureX = 0.10;
            const double fixtureY = 0.20;
            const double fixtureZ = 0.82;
            m_worldState.MarkHeld(RedCubeClass, gripperCarryX, gripperCarryY, gripperCarryZ);
            m_worldState.MarkPlaced(RedCubeClass, fixtureX, fixtureY, fixtureZ);

            VisionInferenceRunResult second = await m_provider
                .RunInferenceAsync(BuildRequest(), stoppingToken)
                .ConfigureAwait(false);
            if (!ServiceResult.IsGood(second.ServiceResult))
            {
                m_logger.ProofRunFailed(second.ServiceResult.ToString());
                return;
            }
            if (!m_provider.TryGetResult(second.ResultId, out DetectionResultState secondState) ||
                secondState == null ||
                secondState.Detections == null)
            {
                m_logger.ProofResultUnavailable(second.ResultId);
                return;
            }

            ArrayOf<VisionDetectionDataType> afterPick = secondState.Detections.Value;
            m_logger.ProofPostPickHeader(second.ResultId, afterPick.Count);
            var afterSnapshot = new VisionDetectionDataType[afterPick.Count];
            bool redCubeStillPresent = false;
            for (int ii = 0; ii < afterPick.Count; ii++)
            {
                VisionDetectionDataType det = afterPick[ii];
                afterSnapshot[ii] = det;
                LogDetection(det);
                if (string.Equals(det.ClassLabel, RedCubeClass, StringComparison.Ordinal))
                {
                    redCubeStillPresent = true;
                }
            }

            if (redCubeStillPresent)
            {
                m_logger.ProofPickFailed(RedCubeClass);
            }
            else
            {
                m_logger.ProofPickSucceeded(RedCubeClass);
            }

            // Put the bin back. This proof runs at startup, before any client connects, and
            // the world it mutates is the one the paired client's demo then works against.
            // Leaving RedCube picked meant the demo's default target was already gone, so it
            // reported success for a part it never touched.
            m_worldState.Reset();

            m_logger.ProofCompleted();
        }

        private VisionInferenceRunRequest BuildRequest()
        {
            return new VisionInferenceRunRequest(
                m_provider.PipelineNodeId,
                m_provider.SensorNodeId,
                m_provider.DeploymentNodeId,
                DateTimeUtc.From(DateTime.UtcNow));
        }

        private static VisionDetectionDataType? TryFindDetection(
            VisionDetectionDataType[] detections, string classLabel)
        {
            foreach (VisionDetectionDataType detection in detections)
            {
                if (string.Equals(detection.ClassLabel, classLabel, StringComparison.Ordinal))
                {
                    return detection;
                }
            }
            return null;
        }

        private void LogDetection(VisionDetectionDataType detection)
        {
            CultureInfo culture = CultureInfo.InvariantCulture;
            string boxSummary = string.Empty;
            if (detection.HasBoundingBox2D)
            {
                VisionBoundingBox2DDataType box = detection.BoundingBox2D;
                boxSummary = string.Format(
                    culture,
                    "cx={0:0.0} cy={1:0.0} w={2:0.0} h={3:0.0}",
                    box.CenterX, box.CenterY, box.Width, box.Height);
            }
            string poseSummary = string.Empty;
            if (detection.HasPose)
            {
                VisionPose3DDataType pose = detection.Pose;
                (double px, double py, double pz) = ReadVec3(pose.Position);
                (double qx, double qy, double qz, double qw) = ReadQuat(pose.Orientation);
                poseSummary = string.Format(
                    culture,
                    "frame='{0}' pos=({1:0.000},{2:0.000},{3:0.000}) " +
                    "quat=({4:0.000},{5:0.000},{6:0.000},{7:0.000})",
                    pose.FrameId, px, py, pz, qx, qy, qz, qw);
            }
            m_logger.ProofDetection(
                detection.ClassLabel ?? "<no class label>",
                detection.Confidence,
                boxSummary,
                poseSummary);
        }

        private void LogComposedPose(VisionDetectionDataType detection)
        {
            VisionPose3DDataType poseCam = detection.Pose;
            (double px, double py, double pz) = ReadVec3(poseCam.Position);
            VisionPose3DDataType cameraInWorld = m_provider.CameraInWorldPose;
            (double cx, double cy, double cz) = ReadVec3(cameraInWorld.Position);
            (double qx, double qy, double qz, double qw) = ReadQuat(cameraInWorld.Orientation);
            (double rx, double ry, double rz) = QuaternionRotate(qx, qy, qz, qw, px, py, pz);
            double worldX = cx + rx;
            double worldY = cy + ry;
            double worldZ = cz + rz;
            var authored = BinPickingPartsCatalog.TryGet(RedCubeClass);
            if (authored != null)
            {
                double ax = authored.InitialWorldPosition[0];
                double ay = authored.InitialWorldPosition[1];
                double az = authored.InitialWorldPosition[2];
                double dx = worldX - ax;
                double dy = worldY - ay;
                double dz = worldZ - az;
                double error = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                m_logger.ProofComposedPose(
                    RedCubeClass,
                    worldX, worldY, worldZ,
                    ax, ay, az,
                    error);
            }
        }

        private static (double X, double Y, double Z) ReadVec3(ArrayOf<double> vec)
        {
            System.ReadOnlySpan<double> span = vec.Span;
            if (span.Length < 3)
            {
                return (0.0, 0.0, 0.0);
            }
            return (span[0], span[1], span[2]);
        }

        private static (double X, double Y, double Z, double W) ReadQuat(ArrayOf<double> vec)
        {
            System.ReadOnlySpan<double> span = vec.Span;
            if (span.Length < 4)
            {
                return (0.0, 0.0, 0.0, 1.0);
            }
            return (span[0], span[1], span[2], span[3]);
        }

        private static (double X, double Y, double Z) QuaternionRotate(
            double qx, double qy, double qz, double qw,
            double vx, double vy, double vz)
        {
            double tx = (qy * vz) - (qz * vy);
            double ty = (qz * vx) - (qx * vz);
            double tz = (qx * vy) - (qy * vx);
            double rx = vx + (2.0 * ((qw * tx) + (qy * tz) - (qz * ty)));
            double ry = vy + (2.0 * ((qw * ty) + (qz * tx) - (qx * tz)));
            double rz = vz + (2.0 * ((qw * tz) + (qx * ty) - (qy * tx)));
            return (rx, ry, rz);
        }

        private const string RedCubeClass = "RedCube";
        private const int AttachAttempts = 300;
        private static readonly TimeSpan AttachPollInterval = TimeSpan.FromMilliseconds(100);

        private readonly BinPickingGroundTruthInferenceProvider m_provider;
        private readonly BinPickingWorldState m_worldState;
        private readonly ILogger<BinPickingInferenceProof> m_logger;
    }

    internal static partial class BinPickingInferenceProofLog
    {
        [LoggerMessage(EventId = BinPickingCellEventIds.Proof + 1,
            Level = LogLevel.Information,
            Message = "Bin-picking on-server inference proof waiting for pipeline to attach.")]
        public static partial void ProofWaiting(this ILogger<BinPickingInferenceProof> logger);

        [LoggerMessage(EventId = BinPickingCellEventIds.Proof + 2,
            Level = LogLevel.Warning,
            Message = "Bin-picking on-server inference proof gave up: provider never attached.")]
        public static partial void ProofProviderNotAttached(
            this ILogger<BinPickingInferenceProof> logger);

        [LoggerMessage(EventId = BinPickingCellEventIds.Proof + 3,
            Level = LogLevel.Information,
            Message = "=== Bin-picking demo: {Banner} ===")]
        public static partial void ProofBanner(
            this ILogger<BinPickingInferenceProof> logger, string banner);

        [LoggerMessage(EventId = BinPickingCellEventIds.Proof + 4,
            Level = LogLevel.Warning,
            Message = "RunInferenceAsync returned non-good service result: {ServiceResult}")]
        public static partial void ProofRunFailed(
            this ILogger<BinPickingInferenceProof> logger, string serviceResult);

        [LoggerMessage(EventId = BinPickingCellEventIds.Proof + 5,
            Level = LogLevel.Warning,
            Message = "DetectionResult '{ResultId}' was not stored on the provider — cannot read back.")]
        public static partial void ProofResultUnavailable(
            this ILogger<BinPickingInferenceProof> logger, string resultId);

        [LoggerMessage(EventId = BinPickingCellEventIds.Proof + 6,
            Level = LogLevel.Information,
            Message = "Detection result {ResultId}: {DetectionCount} parts in view (initial scan).")]
        public static partial void ProofResultHeader(
            this ILogger<BinPickingInferenceProof> logger,
            string resultId, int detectionCount);

        [LoggerMessage(EventId = BinPickingCellEventIds.Proof + 7,
            Level = LogLevel.Information,
            Message = "  {ClassLabel} (conf={Confidence:0.00}) box2D=[{BoundingBox2D}] pose=[{Pose}]")]
        public static partial void ProofDetection(
            this ILogger<BinPickingInferenceProof> logger,
            string classLabel, double confidence, string boundingBox2D, string pose);

        [LoggerMessage(EventId = BinPickingCellEventIds.Proof + 8,
            Level = LogLevel.Information,
            Message = "Composed {ClassLabel} pose camera_eih -> world = " +
                "({ComposedX:0.000},{ComposedY:0.000},{ComposedZ:0.000}); " +
                "authored=({AuthoredX:0.000},{AuthoredY:0.000},{AuthoredZ:0.000}); " +
                "residual={ResidualMetres:0.0000} m")]
        public static partial void ProofComposedPose(
            this ILogger<BinPickingInferenceProof> logger,
            string classLabel,
            double composedX, double composedY, double composedZ,
            double authoredX, double authoredY, double authoredZ,
            double residualMetres);

        [LoggerMessage(EventId = BinPickingCellEventIds.Proof + 9,
            Level = LogLevel.Warning,
            Message = "The RedCube detection was not present or had no pose; skipping compose step.")]
        public static partial void ProofNoRedCube(
            this ILogger<BinPickingInferenceProof> logger);

        [LoggerMessage(EventId = BinPickingCellEventIds.Proof + 10,
            Level = LogLevel.Information,
            Message = "Simulating pick+place of {ClassLabel} to see whether the detector tracks the world.")]
        public static partial void ProofPicking(
            this ILogger<BinPickingInferenceProof> logger, string classLabel);

        [LoggerMessage(EventId = BinPickingCellEventIds.Proof + 11,
            Level = LogLevel.Information,
            Message = "Detection result {ResultId}: {DetectionCount} parts in view (after pick).")]
        public static partial void ProofPostPickHeader(
            this ILogger<BinPickingInferenceProof> logger,
            string resultId, int detectionCount);

        [LoggerMessage(EventId = BinPickingCellEventIds.Proof + 12,
            Level = LogLevel.Error,
            Message = "{ClassLabel} still visible after the pick — detector is NOT tracking world state.")]
        public static partial void ProofPickFailed(
            this ILogger<BinPickingInferenceProof> logger, string classLabel);

        [LoggerMessage(EventId = BinPickingCellEventIds.Proof + 13,
            Level = LogLevel.Information,
            Message = "{ClassLabel} correctly disappeared from the detection result after the pick — " +
                "the ground-truth path tracks the world.")]
        public static partial void ProofPickSucceeded(
            this ILogger<BinPickingInferenceProof> logger, string classLabel);

        [LoggerMessage(EventId = BinPickingCellEventIds.Proof + 14,
            Level = LogLevel.Information,
            Message = "=== Bin-picking demo: on-server inference proof completed ===")]
        public static partial void ProofCompleted(
            this ILogger<BinPickingInferenceProof> logger);
    }
}
