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
    /// Hosted service that stands in for the connected MCP agent and
    /// exercises the off-server perception path end-to-end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The demo's headline is that a language model over MCP does the
    /// seeing — but the sample must be able to prove the server-side
    /// half without an actual model in the loop. This service is that
    /// proof: it plays the role of an agent that has looked at the
    /// frame and decided what it sees, and calls
    /// <see cref="IVisionFeedbackSink.SubmitDetectionsAsync"/> the way
    /// the MCP tool would. It also drives every validation refusal so
    /// the messages a real agent would see are visible in the log.
    /// </para>
    /// <para>
    /// This service is only wired when the run selects
    /// <c>InferenceLocation=EdgeOffServer</c>, so its evidence
    /// complements — never conflicts with — the on-server proof.
    /// </para>
    /// </remarks>
    internal sealed class BinPickingOffServerProof : BackgroundService
    {
        public BinPickingOffServerProof(
            BinPickingAgentInferenceProvider provider,
            ILogger<BinPickingOffServerProof> logger)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
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

            m_logger.ProofBanner("off-server agent-as-VLM perception");

            await ProveRunInferenceRefusedAsync(stoppingToken).ConfigureAwait(false);
            string? initialResultId = await ProveHappyPathAsync(stoppingToken).ConfigureAwait(false);
            await ProveValidationRefusalsAsync(stoppingToken).ConfigureAwait(false);
            if (initialResultId != null)
            {
                await ProveCorrectionAsync(initialResultId, stoppingToken).ConfigureAwait(false);
                await ProveCorrectionAgainstUnknownResultAsync(stoppingToken).ConfigureAwait(false);
            }

            m_logger.ProofCompleted();
        }

        private async Task ProveRunInferenceRefusedAsync(CancellationToken cancellationToken)
        {
            var request = new VisionInferenceRunRequest(
                m_provider.PipelineNodeId,
                m_provider.SensorNodeId,
                m_provider.DeploymentNodeId,
                DateTimeUtc.From(DateTime.UtcNow));
            VisionInferenceRunResult runResult = await m_provider
                .RunInferenceAsync(request, cancellationToken)
                .ConfigureAwait(false);
            m_logger.ProofRunInferenceRefused(
                runResult.ServiceResult.StatusCode.Code,
                runResult.ServiceResult.LocalizedText.Text ?? string.Empty);
        }

        private async Task<string?> ProveHappyPathAsync(CancellationToken cancellationToken)
        {
            m_logger.ProofBanner("valid submission");
            (VisionDetectionDataType redCube, VisionPose3DDataType poseInCamera) = BuildRedCubeDetection();
            (VisionDetectionDataType greenCylinder, _) = BuildGreenCylinderDetection();
            var detections = new[] { redCube, greenCylinder }.ToArrayOf();
            var frameReference = new VisionImageReferenceDataType
            {
                Uri = "opcua-agent://binpicking-cell/vlm/frames/proof-happy",
                Digest = ByteString.Empty,
                DigestAlgorithm = string.Empty,
                Format = VisionClipFormatEnum.Png,
                PixelFormat = "RGB8",
                Width = (uint)Math.Round(m_provider.ImageWidth),
                Height = (uint)Math.Round(m_provider.ImageHeight),
                SizeBytes = 0u,
                Timestamp = DateTimeUtc.From(DateTime.UtcNow)
            };
            ServiceResult submission = await m_provider
                .SubmitDetectionsAsync(
                    new VisionSubmitDetectionsRequest(
                        m_provider.PipelineNodeId,
                        VisionFeedbackPurposeEnum.Reconciliation,
                        detections,
                        frameReference,
                        ByteString.Empty),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!ServiceResult.IsGood(submission))
            {
                m_logger.ProofSubmissionFailed(submission.StatusCode.Code, submission.ToString());
                return null;
            }
            string resultId = m_provider.LastPublishedResultId;
            if (string.IsNullOrEmpty(resultId) ||
                !m_provider.TryGetResult(resultId, out DetectionResultState state))
            {
                m_logger.ProofResultUnavailable(resultId);
                return null;
            }
            LogResultShape(state);
            LogComposedPose(RedCubeClass, poseInCamera);
            return resultId;
        }

        private async Task ProveValidationRefusalsAsync(CancellationToken cancellationToken)
        {
            m_logger.ProofBanner("validation refusals (the messages an agent would see)");
            await ExpectRefusalAsync(
                "hallucinated class label",
                new VisionSubmitDetectionsRequest(
                    m_provider.PipelineNodeId,
                    VisionFeedbackPurposeEnum.Reconciliation,
                    new[] { BuildDetectionWithClass("PurpleWidget") }.ToArrayOf(),
                    new VisionImageReferenceDataType(),
                    ByteString.Empty),
                cancellationToken).ConfigureAwait(false);
            await ExpectRefusalAsync(
                "confidence outside [0, 1]",
                new VisionSubmitDetectionsRequest(
                    m_provider.PipelineNodeId,
                    VisionFeedbackPurposeEnum.Reconciliation,
                    new[] { BuildDetectionWithConfidence(1.42) }.ToArrayOf(),
                    new VisionImageReferenceDataType(),
                    ByteString.Empty),
                cancellationToken).ConfigureAwait(false);
            await ExpectRefusalAsync(
                "bounding box entirely outside image",
                new VisionSubmitDetectionsRequest(
                    m_provider.PipelineNodeId,
                    VisionFeedbackPurposeEnum.Reconciliation,
                    new[] { BuildDetectionWithBoxOutsideImage() }.ToArrayOf(),
                    new VisionImageReferenceDataType(),
                    ByteString.Empty),
                cancellationToken).ConfigureAwait(false);
            await ExpectRefusalAsync(
                "zero-norm quaternion",
                new VisionSubmitDetectionsRequest(
                    m_provider.PipelineNodeId,
                    VisionFeedbackPurposeEnum.Reconciliation,
                    new[] { BuildDetectionWithZeroNormQuat() }.ToArrayOf(),
                    new VisionImageReferenceDataType(),
                    ByteString.Empty),
                cancellationToken).ConfigureAwait(false);
            await ExpectAcceptedAsync(
                "empty detections report an empty bin",
                new VisionSubmitDetectionsRequest(
                    m_provider.PipelineNodeId,
                    VisionFeedbackPurposeEnum.Reconciliation,
                    ArrayOf<VisionDetectionDataType>.Empty,
                    new VisionImageReferenceDataType(),
                    ByteString.Empty),
                cancellationToken).ConfigureAwait(false);
            await ExpectRefusalAsync(
                "detection count exceeds the cell's plausible ceiling",
                new VisionSubmitDetectionsRequest(
                    m_provider.PipelineNodeId,
                    VisionFeedbackPurposeEnum.Reconciliation,
                    BuildManyDetections(HallucinationCount),
                    new VisionImageReferenceDataType(),
                    ByteString.Empty),
                cancellationToken).ConfigureAwait(false);
            await ExpectRefusalAsync(
                "purpose not a defined enum value",
                new VisionSubmitDetectionsRequest(
                    m_provider.PipelineNodeId,
                    (VisionFeedbackPurposeEnum)77,
                    new[] { BuildBlueSphereDetection() }.ToArrayOf(),
                    new VisionImageReferenceDataType(),
                    ByteString.Empty),
                cancellationToken).ConfigureAwait(false);
        }

        private async Task ProveCorrectionAsync(string originalResultId, CancellationToken cancellationToken)
        {
            m_logger.ProofBanner("submitting a correction (§9 learning path)");
            VisionDetectionDataType corrected = BuildBlueSphereDetection();
            ServiceResult correctionResult = await m_provider
                .SubmitCorrectionAsync(
                    new VisionSubmitCorrectionRequest(
                        m_provider.PipelineNodeId,
                        originalResultId,
                        VisionFeedbackPurposeEnum.GroundTruthLabel,
                        new[] { corrected }.ToArrayOf(),
                        ArrayOf<VisionCharacteristicDataType>.Empty,
                        LocalizedText.From("Original miscalled the class; corrected to BlueSphere."),
                        ByteString.Empty),
                    cancellationToken)
                .ConfigureAwait(false);
            m_logger.ProofCorrectionResult(
                originalResultId,
                correctionResult.StatusCode.Code,
                correctionResult.LocalizedText.Text ?? string.Empty);
        }

        private async Task ProveCorrectionAgainstUnknownResultAsync(CancellationToken cancellationToken)
        {
            m_logger.ProofBanner("correction against an unknown result-id");
            ServiceResult refusal = await m_provider
                .SubmitCorrectionAsync(
                    new VisionSubmitCorrectionRequest(
                        m_provider.PipelineNodeId,
                        "det-agent-never-existed",
                        VisionFeedbackPurposeEnum.GroundTruthLabel,
                        new[] { BuildBlueSphereDetection() }.ToArrayOf(),
                        ArrayOf<VisionCharacteristicDataType>.Empty,
                        LocalizedText.From("Correction referencing a fabricated id."),
                        ByteString.Empty),
                    cancellationToken)
                .ConfigureAwait(false);
            m_logger.ProofCorrectionResult(
                "det-agent-never-existed",
                refusal.StatusCode.Code,
                refusal.LocalizedText.Text ?? string.Empty);
        }

        private async Task ExpectRefusalAsync(
            string scenario, VisionSubmitDetectionsRequest request, CancellationToken cancellationToken)
        {
            ServiceResult result = await m_provider
                .SubmitDetectionsAsync(request, cancellationToken)
                .ConfigureAwait(false);
            m_logger.ProofRefusalScenario(
                scenario, result.StatusCode.Code, result.LocalizedText.Text ?? string.Empty);
        }

        private async Task ExpectAcceptedAsync(
            string scenario, VisionSubmitDetectionsRequest request, CancellationToken cancellationToken)
        {
            ServiceResult result = await m_provider
                .SubmitDetectionsAsync(request, cancellationToken)
                .ConfigureAwait(false);
            m_logger.ProofAcceptedScenario(
                scenario, result.StatusCode.Code, result.LocalizedText.Text ?? string.Empty);
        }

        private (VisionDetectionDataType Detection, VisionPose3DDataType PoseInCamera) BuildRedCubeDetection()
        {
            BinPickingPart? part = BinPickingPartsCatalog.TryGet(RedCubeClass);
            if (part == null)
            {
                throw new InvalidOperationException("RedCube missing from catalog.");
            }
            (double xc, double yc, double zc) = WorldToCamera(
                part.InitialWorldPosition[0],
                part.InitialWorldPosition[1],
                part.InitialWorldPosition[2]);
            var pose = new VisionPose3DDataType
            {
                FrameId = m_provider.CameraFrameId,
                Position = new[] { xc, yc, zc }.ToArrayOf(),
                Orientation = s_identityOrientation.ToArrayOf(),
                Covariance = ArrayOf<double>.Empty
            };
            var detection = new VisionDetectionDataType
            {
                DetectionId = "vlm-red-cube-0",
                ClassLabel = RedCubeClass,
                ClassId = part.ClassId,
                Confidence = 0.94,
                HasBoundingBox2D = true,
                BoundingBox2D = new VisionBoundingBox2DDataType
                {
                    CenterX = m_provider.ImageWidth * 0.5,
                    CenterY = m_provider.ImageHeight * 0.5,
                    Width = m_provider.ImageWidth * 0.10,
                    Height = m_provider.ImageHeight * 0.10,
                    Rotation = 0.0
                },
                HasPose = true,
                Pose = pose,
                TrackId = RedCubeClass
            };
            return (detection, pose);
        }

        private (VisionDetectionDataType Detection, VisionPose3DDataType PoseInCamera) BuildGreenCylinderDetection()
        {
            BinPickingPart? part = BinPickingPartsCatalog.TryGet("GreenCylinder");
            if (part == null)
            {
                throw new InvalidOperationException("GreenCylinder missing from catalog.");
            }
            (double xc, double yc, double zc) = WorldToCamera(
                part.InitialWorldPosition[0],
                part.InitialWorldPosition[1],
                part.InitialWorldPosition[2]);
            var pose = new VisionPose3DDataType
            {
                FrameId = m_provider.CameraFrameId,
                Position = new[] { xc, yc, zc }.ToArrayOf(),
                Orientation = s_identityOrientation.ToArrayOf(),
                Covariance = ArrayOf<double>.Empty
            };
            var detection = new VisionDetectionDataType
            {
                DetectionId = "vlm-green-cyl-1",
                ClassLabel = part.ClassLabel,
                ClassId = part.ClassId,
                Confidence = 0.83,
                HasBoundingBox2D = true,
                BoundingBox2D = new VisionBoundingBox2DDataType
                {
                    CenterX = m_provider.ImageWidth * 0.55,
                    CenterY = m_provider.ImageHeight * 0.50,
                    Width = m_provider.ImageWidth * 0.09,
                    Height = m_provider.ImageHeight * 0.09,
                    Rotation = 0.0
                },
                HasPose = true,
                Pose = pose,
                TrackId = part.ClassLabel
            };
            return (detection, pose);
        }

        private VisionDetectionDataType BuildBlueSphereDetection()
        {
            BinPickingPart part = BinPickingPartsCatalog.TryGet("BlueSphere")
                ?? throw new InvalidOperationException("BlueSphere missing from catalog.");
            return new VisionDetectionDataType
            {
                DetectionId = "vlm-blue-sphere",
                ClassLabel = part.ClassLabel,
                ClassId = part.ClassId,
                Confidence = 0.88,
                HasBoundingBox2D = true,
                BoundingBox2D = new VisionBoundingBox2DDataType
                {
                    CenterX = m_provider.ImageWidth * 0.60,
                    CenterY = m_provider.ImageHeight * 0.50,
                    Width = m_provider.ImageWidth * 0.10,
                    Height = m_provider.ImageHeight * 0.10,
                    Rotation = 0.0
                },
                HasPose = false,
                TrackId = part.ClassLabel
            };
        }

        private VisionDetectionDataType BuildDetectionWithClass(string classLabel)
        {
            return new VisionDetectionDataType
            {
                DetectionId = "vlm-invalid-class",
                ClassLabel = classLabel,
                ClassId = 99u,
                Confidence = 0.61,
                HasBoundingBox2D = true,
                BoundingBox2D = InBoundsBox(),
                HasPose = false,
                TrackId = classLabel
            };
        }

        private VisionDetectionDataType BuildDetectionWithConfidence(double confidence)
        {
            return new VisionDetectionDataType
            {
                DetectionId = "vlm-invalid-confidence",
                ClassLabel = RedCubeClass,
                ClassId = 1u,
                Confidence = confidence,
                HasBoundingBox2D = true,
                BoundingBox2D = InBoundsBox(),
                HasPose = false,
                TrackId = RedCubeClass
            };
        }

        private VisionDetectionDataType BuildDetectionWithBoxOutsideImage()
        {
            return new VisionDetectionDataType
            {
                DetectionId = "vlm-invalid-box",
                ClassLabel = RedCubeClass,
                ClassId = 1u,
                Confidence = 0.5,
                HasBoundingBox2D = true,
                BoundingBox2D = new VisionBoundingBox2DDataType
                {
                    CenterX = m_provider.ImageWidth + 500.0,
                    CenterY = m_provider.ImageHeight + 500.0,
                    Width = 40.0,
                    Height = 40.0,
                    Rotation = 0.0
                },
                HasPose = false,
                TrackId = RedCubeClass
            };
        }

        private VisionDetectionDataType BuildDetectionWithZeroNormQuat()
        {
            return new VisionDetectionDataType
            {
                DetectionId = "vlm-invalid-pose",
                ClassLabel = RedCubeClass,
                ClassId = 1u,
                Confidence = 0.5,
                HasBoundingBox2D = false,
                HasPose = true,
                Pose = new VisionPose3DDataType
                {
                    FrameId = m_provider.CameraFrameId,
                    Position = s_zeroNormPosition.ToArrayOf(),
                    Orientation = s_zeroNormQuat.ToArrayOf(),
                    Covariance = ArrayOf<double>.Empty
                },
                TrackId = RedCubeClass
            };
        }

        private VisionBoundingBox2DDataType InBoundsBox()
        {
            return new VisionBoundingBox2DDataType
            {
                CenterX = m_provider.ImageWidth * 0.5,
                CenterY = m_provider.ImageHeight * 0.5,
                Width = 40.0,
                Height = 40.0,
                Rotation = 0.0
            };
        }

        private ArrayOf<VisionDetectionDataType> BuildManyDetections(int count)
        {
            var detections = new VisionDetectionDataType[count];
            BinPickingPart part = BinPickingPartsCatalog.TryGet(RedCubeClass)!;
            for (int ii = 0; ii < count; ii++)
            {
                detections[ii] = new VisionDetectionDataType
                {
                    DetectionId = FormattableString.Invariant($"vlm-many-{ii}"),
                    ClassLabel = part.ClassLabel,
                    ClassId = part.ClassId,
                    Confidence = 0.5,
                    HasBoundingBox2D = true,
                    BoundingBox2D = InBoundsBox(),
                    HasPose = false,
                    TrackId = part.ClassLabel
                };
            }
            return detections.ToArrayOf();
        }

        private void LogResultShape(DetectionResultState state)
        {
            string? resultId = state.ResultId?.Value;
            string? modelVersion = state.ModelVersionUsed?.Value;
            string? explanationUri = state.ExplanationUri?.Value;
            NodeId sensorId = state.Sensor?.Value ?? NodeId.Null;
            NodeId pipelineId = state.Pipeline?.Value ?? NodeId.Null;
            int detectionCount = state.Detections?.Value.Count ?? 0;
            string? frameId = state.FrameId?.Value;
            m_logger.ProofPublishedResult(
                resultId ?? string.Empty,
                detectionCount,
                sensorId,
                pipelineId,
                modelVersion ?? string.Empty,
                explanationUri ?? string.Empty,
                frameId ?? string.Empty);
        }

        private void LogComposedPose(string classLabel, VisionPose3DDataType poseInCamera)
        {
            System.ReadOnlySpan<double> pos = poseInCamera.Position.Span;
            if (pos.Length < 3)
            {
                return;
            }
            (double x, double y, double z) = QuaternionComposeToWorld(pos[0], pos[1], pos[2]);
            BinPickingPart? authored = BinPickingPartsCatalog.TryGet(classLabel);
            if (authored == null)
            {
                return;
            }
            double ax = authored.InitialWorldPosition[0];
            double ay = authored.InitialWorldPosition[1];
            double az = authored.InitialWorldPosition[2];
            double dx = x - ax;
            double dy = y - ay;
            double dz = z - az;
            double residual = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            m_logger.ProofComposedPose(classLabel, x, y, z, ax, ay, az, residual);
        }

        private (double X, double Y, double Z) WorldToCamera(double x, double y, double z)
        {
            VisionPose3DDataType cameraInWorld = m_provider.CameraInWorldPose;
            System.ReadOnlySpan<double> pos = cameraInWorld.Position.Span;
            System.ReadOnlySpan<double> ori = cameraInWorld.Orientation.Span;
            if (pos.Length < 3 || ori.Length < 4)
            {
                return (0.0, 0.0, 0.0);
            }
            double dx = x - pos[0];
            double dy = y - pos[1];
            double dz = z - pos[2];
            double qx = -ori[0];
            double qy = -ori[1];
            double qz = -ori[2];
            double qw = ori[3];
            double tx = (qy * dz) - (qz * dy);
            double ty = (qz * dx) - (qx * dz);
            double tz = (qx * dy) - (qy * dx);
            double rx = dx + (2.0 * ((qw * tx) + (qy * tz) - (qz * ty)));
            double ry = dy + (2.0 * ((qw * ty) + (qz * tx) - (qx * tz)));
            double rz = dz + (2.0 * ((qw * tz) + (qx * ty) - (qy * tx)));
            return (rx, ry, rz);
        }

        private (double X, double Y, double Z) QuaternionComposeToWorld(double cx, double cy, double cz)
        {
            VisionPose3DDataType cameraInWorld = m_provider.CameraInWorldPose;
            System.ReadOnlySpan<double> pos = cameraInWorld.Position.Span;
            System.ReadOnlySpan<double> ori = cameraInWorld.Orientation.Span;
            if (pos.Length < 3 || ori.Length < 4)
            {
                return (0.0, 0.0, 0.0);
            }
            double qx = ori[0];
            double qy = ori[1];
            double qz = ori[2];
            double qw = ori[3];
            double tx = (qy * cz) - (qz * cy);
            double ty = (qz * cx) - (qx * cz);
            double tz = (qx * cy) - (qy * cx);
            double rx = cx + (2.0 * ((qw * tx) + (qy * tz) - (qz * ty)));
            double ry = cy + (2.0 * ((qw * ty) + (qz * tx) - (qx * tz)));
            double rz = cz + (2.0 * ((qw * tz) + (qx * ty) - (qy * tx)));
            return (pos[0] + rx, pos[1] + ry, pos[2] + rz);
        }

        private const int AttachAttempts = 300;
        private const string RedCubeClass = "RedCube";
        private const int HallucinationCount = 20;
        private static readonly TimeSpan AttachPollInterval = TimeSpan.FromMilliseconds(100);
        private static readonly double[] s_identityOrientation = [0.0, 0.0, 0.0, 1.0];
        private static readonly double[] s_zeroNormPosition = [0.0, 0.0, 0.5];
        private static readonly double[] s_zeroNormQuat = [0.0, 0.0, 0.0, 0.0];

        private readonly BinPickingAgentInferenceProvider m_provider;
        private readonly ILogger<BinPickingOffServerProof> m_logger;
    }

    internal static partial class BinPickingOffServerProofLog
    {
        [LoggerMessage(EventId = BinPickingCellEventIds.OffServerProof + 1,
            Level = LogLevel.Information,
            Message = "Bin-picking off-server perception proof waiting for pipeline to attach.")]
        public static partial void ProofWaiting(this ILogger<BinPickingOffServerProof> logger);

        [LoggerMessage(EventId = BinPickingCellEventIds.OffServerProof + 2,
            Level = LogLevel.Warning,
            Message = "Bin-picking off-server perception proof gave up: agent provider never attached.")]
        public static partial void ProofProviderNotAttached(
            this ILogger<BinPickingOffServerProof> logger);

        [LoggerMessage(EventId = BinPickingCellEventIds.OffServerProof + 3,
            Level = LogLevel.Information,
            Message = "=== Bin-picking demo: {Banner} ===")]
        public static partial void ProofBanner(
            this ILogger<BinPickingOffServerProof> logger, string banner);

        [LoggerMessage(EventId = BinPickingCellEventIds.OffServerProof + 4,
            Level = LogLevel.Information,
            Message = "RunInference refused (as designed for InferenceLocation=EdgeOffServer): " +
                "code=0x{StatusCode:X8} reason='{Reason}'")]
        public static partial void ProofRunInferenceRefused(
            this ILogger<BinPickingOffServerProof> logger,
            uint statusCode, string reason);

        [LoggerMessage(EventId = BinPickingCellEventIds.OffServerProof + 5,
            Level = LogLevel.Information,
            Message = "Published off-server result {ResultId} ({DetectionCount} detections): " +
                "Sensor={SensorId} Pipeline={PipelineId} ModelVersionUsed='{ModelVersion}' " +
                "ExplanationUri='{ExplanationUri}' FrameId='{FrameId}'.")]
        public static partial void ProofPublishedResult(
            this ILogger<BinPickingOffServerProof> logger,
            string resultId, int detectionCount, NodeId sensorId, NodeId pipelineId,
            string modelVersion, string explanationUri, string frameId);

        [LoggerMessage(EventId = BinPickingCellEventIds.OffServerProof + 6,
            Level = LogLevel.Information,
            Message = "Composed submitted {ClassLabel} pose camera_eih -> world = " +
                "({ComposedX:0.000},{ComposedY:0.000},{ComposedZ:0.000}); " +
                "authored=({AuthoredX:0.000},{AuthoredY:0.000},{AuthoredZ:0.000}); " +
                "residual={ResidualMetres:0.0000} m")]
        public static partial void ProofComposedPose(
            this ILogger<BinPickingOffServerProof> logger,
            string classLabel,
            double composedX, double composedY, double composedZ,
            double authoredX, double authoredY, double authoredZ,
            double residualMetres);

        [LoggerMessage(EventId = BinPickingCellEventIds.OffServerProof + 7,
            Level = LogLevel.Warning,
            Message = "Submission was refused when the proof expected it to succeed: " +
                "code=0x{StatusCode:X8} reason='{Reason}'")]
        public static partial void ProofSubmissionFailed(
            this ILogger<BinPickingOffServerProof> logger,
            uint statusCode, string reason);

        [LoggerMessage(EventId = BinPickingCellEventIds.OffServerProof + 8,
            Level = LogLevel.Warning,
            Message = "Result '{ResultId}' was not stored on the provider — cannot inspect.")]
        public static partial void ProofResultUnavailable(
            this ILogger<BinPickingOffServerProof> logger, string resultId);

        [LoggerMessage(EventId = BinPickingCellEventIds.OffServerProof + 9,
            Level = LogLevel.Information,
            Message = "Refusal scenario '{Scenario}' → code=0x{StatusCode:X8} reason='{Reason}'")]
        public static partial void ProofRefusalScenario(
            this ILogger<BinPickingOffServerProof> logger,
            string scenario, uint statusCode, string reason);

        [LoggerMessage(EventId = BinPickingCellEventIds.OffServerProof + 12,
            Level = LogLevel.Information,
            Message = "Accepted scenario '{Scenario}' → code=0x{StatusCode:X8} reason='{Reason}'")]
        public static partial void ProofAcceptedScenario(
            this ILogger<BinPickingOffServerProof> logger,
            string scenario, uint statusCode, string reason);

        [LoggerMessage(EventId = BinPickingCellEventIds.OffServerProof + 10,
            Level = LogLevel.Information,
            Message = "Correction against {OriginalResultId} → code=0x{StatusCode:X8} reason='{Reason}'")]
        public static partial void ProofCorrectionResult(
            this ILogger<BinPickingOffServerProof> logger,
            string originalResultId, uint statusCode, string reason);

        [LoggerMessage(EventId = BinPickingCellEventIds.OffServerProof + 11,
            Level = LogLevel.Information,
            Message = "=== Bin-picking demo: off-server perception proof completed ===")]
        public static partial void ProofCompleted(
            this ILogger<BinPickingOffServerProof> logger);
    }
}
