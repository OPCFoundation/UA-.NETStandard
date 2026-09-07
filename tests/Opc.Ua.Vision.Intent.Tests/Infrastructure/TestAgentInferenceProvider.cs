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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Intent.Tests.Infrastructure
{
    /// <summary>
    /// Off-server perception path used by the loop tests.
    /// <c>RunInference</c> refuses with <see cref="StatusCodes.BadNotSupported"/>
    /// because the Server has no local model; results arrive through
    /// <see cref="IVisionFeedbackSink"/> submissions which are validated
    /// against the test parts catalog and the sensor's declared image
    /// extents before publishing a <c>DetectionResultType</c>.
    /// </summary>
    internal sealed class TestAgentInferenceProvider
        : IVisionInferenceProvider, IVisionFeedbackSink
    {
        public bool IsAttached => m_target != null;

        public NodeId PipelineNodeId => m_target?.PipelineNodeId ?? NodeId.Null;

        /// <summary>
        /// Result identifier of the most recent accepted
        /// <c>SubmitDetections</c>. Empty until one is accepted.
        /// </summary>
        public string LastPublishedResultId => m_lastPublishedResultId;

        public void Attach(TestInferenceTarget target, double imageWidth, double imageHeight)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (Interlocked.CompareExchange(ref m_target, target, null) != null)
            {
                throw new InvalidOperationException(
                    "TestAgentInferenceProvider has already been attached.");
            }
            m_imageWidth = imageWidth;
            m_imageHeight = imageHeight;
        }

        public bool TryGetResult(string resultId, out DetectionResultState state)
        {
            return m_results.TryGetValue(resultId, out state!);
        }

        public ValueTask<VisionInferenceRunResult> RunInferenceAsync(
            VisionInferenceRunRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new VisionInferenceRunResult(
                new ServiceResult(
                    StatusCodes.BadNotSupported,
                    LocalizedText.From(
                        "This pipeline is configured for off-server perception. " +
                        "Publish results by calling SubmitDetections on the pipeline's Feedback object.")),
                string.Empty);
            return new ValueTask<VisionInferenceRunResult>(result);
        }

        public ValueTask<ServiceResult> StartContinuousAsync(NodeId pipeline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ServiceResult>(new ServiceResult(
                StatusCodes.BadNotSupported,
                LocalizedText.From("Continuous inference is not supported off-server.")));
        }

        public ValueTask<ServiceResult> StopAsync(NodeId pipeline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        public async ValueTask<ServiceResult> SubmitDetectionsAsync(
            VisionSubmitDetectionsRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            TestInferenceTarget target = RequireTarget();
            if (!request.Pipeline.Equals(target.PipelineNodeId))
            {
                return Refuse(
                    StatusCodes.BadNodeIdUnknown,
                    "Pipeline id does not match the attached off-server pipeline.");
            }
            ServiceResult? refusal = ValidateDetections(request.Detections, allowEmpty: true);
            if (refusal != null)
            {
                return refusal;
            }
            string resultId = "det-agent-" + Guid.NewGuid().ToString("N");
            DateTimeUtc timestamp = DateTimeUtc.From(DateTime.UtcNow);
            string modelVersion = FormattableString.Invariant(
                $"agent-off-server:{ResolveModelTag(request.FrameReference)}");
            string explanation = FormattableString.Invariant(
                $"{ExplanationUri}?purpose={request.Purpose}");
            await PublishAsync(
                target, resultId, timestamp, request.Detections, modelVersion, explanation, cancellationToken)
                .ConfigureAwait(false);
            m_lastPublishedResultId = resultId;
            return ServiceResult.Good;
        }

        public ValueTask<ServiceResult> SubmitInspectionResultAsync(
            VisionSubmitInspectionResultRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ServiceResult>(new ServiceResult(
                StatusCodes.BadNotSupported,
                LocalizedText.From("The test pipeline publishes DetectionResultType only.")));
        }

        public async ValueTask<ServiceResult> SubmitCorrectionAsync(
            VisionSubmitCorrectionRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            TestInferenceTarget target = RequireTarget();
            if (!request.Pipeline.Equals(target.PipelineNodeId))
            {
                return Refuse(
                    StatusCodes.BadNodeIdUnknown,
                    "Pipeline id does not match the attached off-server pipeline.");
            }
            if (string.IsNullOrEmpty(request.ResultId))
            {
                return Refuse(
                    StatusCodes.BadInvalidArgument,
                    "ResultId is required and must name the result being corrected.");
            }
            if (!m_results.ContainsKey(request.ResultId))
            {
                return Refuse(
                    StatusCodes.BadNodeIdUnknown,
                    FormattableString.Invariant(
                        $"ResultId '{request.ResultId}' does not name a result on this pipeline."));
            }
            if (request.CorrectedCharacteristics.Count > 0)
            {
                return Refuse(
                    StatusCodes.BadNotSupported,
                    "Corrections must carry CorrectedDetections, not CorrectedCharacteristics.");
            }
            ServiceResult? refusal = ValidateDetections(request.CorrectedDetections, allowEmpty: true);
            if (refusal != null)
            {
                return refusal;
            }
            string resultId = "det-agent-correction-" + Guid.NewGuid().ToString("N");
            DateTimeUtc timestamp = DateTimeUtc.From(DateTime.UtcNow);
            const string modelVersion = "agent-off-server:correction";
            string explanation = FormattableString.Invariant(
                $"{ExplanationUri}?corrects={Uri.EscapeDataString(request.ResultId)}");
            await PublishAsync(
                target,
                resultId,
                timestamp,
                request.CorrectedDetections,
                modelVersion,
                explanation,
                cancellationToken).ConfigureAwait(false);
            return ServiceResult.Good;
        }

        public ValueTask<ServiceResult> SubmitImageReferenceAsync(
            VisionSubmitImageReferenceRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        private TestInferenceTarget RequireTarget()
        {
            return m_target ??
                throw new InvalidOperationException(
                    "TestAgentInferenceProvider has not been attached.");
        }

        private ServiceResult? ValidateDetections(
            ArrayOf<VisionDetectionDataType> detections, bool allowEmpty)
        {
            if (!allowEmpty && detections.Count == 0)
            {
                return Refuse(
                    StatusCodes.BadInvalidArgument,
                    "At least one detection is required.");
            }
            for (int ii = 0; ii < detections.Count; ii++)
            {
                VisionDetectionDataType detection = detections[ii];
                if (string.IsNullOrEmpty(detection.ClassLabel))
                {
                    return Refuse(
                        StatusCodes.BadInvalidArgument,
                        FormattableString.Invariant($"Detection {ii} has no ClassLabel."));
                }
                if (TestPartsCatalog.TryGet(detection.ClassLabel) == null)
                {
                    return Refuse(
                        StatusCodes.BadInvalidArgument,
                        FormattableString.Invariant(
                            $"Detection {ii} class '{detection.ClassLabel}' is not a known part in this cell."));
                }
                double confidence = detection.Confidence;
                if (double.IsNaN(confidence) || confidence < 0.0 || confidence > 1.0)
                {
                    return Refuse(
                        StatusCodes.BadInvalidArgument,
                        FormattableString.Invariant(
                            $"Detection {ii} confidence {confidence.ToString(CultureInfo.InvariantCulture)} is outside [0, 1]."));
                }
                if (detection.HasBoundingBox2D)
                {
                    ServiceResult? boxRefusal = ValidateBox(ii, detection.BoundingBox2D);
                    if (boxRefusal != null)
                    {
                        return boxRefusal;
                    }
                }
                if (detection.HasPose)
                {
                    ServiceResult? poseRefusal = ValidatePose(ii, detection.Pose);
                    if (poseRefusal != null)
                    {
                        return poseRefusal;
                    }
                }
            }
            return null;
        }

        private ServiceResult? ValidateBox(int index, VisionBoundingBox2DDataType box)
        {
            if (double.IsNaN(box.CenterX) ||
                double.IsNaN(box.CenterY) ||
                double.IsNaN(box.Width) ||
                double.IsNaN(box.Height))
            {
                return Refuse(
                    StatusCodes.BadInvalidArgument,
                    FormattableString.Invariant($"Detection {index} BoundingBox2D contains NaN."));
            }
            if (box.Width <= 0.0 || box.Height <= 0.0)
            {
                return Refuse(
                    StatusCodes.BadInvalidArgument,
                    FormattableString.Invariant(
                        $"Detection {index} BoundingBox2D has non-positive extents."));
            }
            double halfW = box.Width * 0.5;
            double halfH = box.Height * 0.5;
            double minU = box.CenterX - halfW;
            double maxU = box.CenterX + halfW;
            double minV = box.CenterY - halfH;
            double maxV = box.CenterY + halfH;
            if (maxU <= 0.0 || minU >= m_imageWidth || maxV <= 0.0 || minV >= m_imageHeight)
            {
                return Refuse(
                    StatusCodes.BadInvalidArgument,
                    FormattableString.Invariant(
                        $"Detection {index} BoundingBox2D lies entirely outside the {m_imageWidth}x{m_imageHeight} image."));
            }
            return null;
        }

        private ServiceResult? ValidatePose(int index, VisionPose3DDataType pose)
        {
            ReadOnlySpan<double> orientation = pose.Orientation.Span;
            if (orientation.Length < 4)
            {
                return Refuse(
                    StatusCodes.BadInvalidArgument,
                    FormattableString.Invariant(
                        $"Detection {index} Pose.Orientation must carry four components (x, y, z, w)."));
            }
            double normSq = (orientation[0] * orientation[0]) +
                (orientation[1] * orientation[1]) +
                (orientation[2] * orientation[2]) +
                (orientation[3] * orientation[3]);
            if (normSq <= 0.0)
            {
                return Refuse(
                    StatusCodes.BadInvalidArgument,
                    FormattableString.Invariant(
                        $"Detection {index} Pose.Orientation has zero norm."));
            }
            return null;
        }

        private static ServiceResult Refuse(StatusCode statusCode, string reason)
        {
            return new ServiceResult(statusCode, LocalizedText.From(reason));
        }

        private static string ResolveModelTag(VisionImageReferenceDataType? frameReference)
        {
            if (frameReference == null || string.IsNullOrEmpty(frameReference.Uri))
            {
                return "unspecified";
            }
            return frameReference.Uri;
        }

        private async Task PublishAsync(
            TestInferenceTarget target,
            string resultId,
            DateTimeUtc timestamp,
            ArrayOf<VisionDetectionDataType> detections,
            string modelVersion,
            string explanationUri,
            CancellationToken cancellationToken)
        {
            ISystemContext context = target.SystemContext;
            var qualifiedName = new QualifiedName(resultId, target.InstanceNamespaceIndex);
            DetectionResultState state = context.CreateInstanceOfDetectionResultType(
                target.ResultsFolder, qualifiedName);
            state.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.Organizes;
            if (state.ResultId != null)
            {
                state.ResultId.Value = resultId;
            }
            if (state.CreationTime != null)
            {
                state.CreationTime.Value = timestamp;
            }
            state.CreateOrReplaceSensor(context, null).Value = target.SensorNodeId;
            state.CreateOrReplacePipeline(context, null).Value = target.PipelineNodeId;
            state.CreateOrReplaceModelVersionUsed(context, null).Value = modelVersion;
            state.CreateOrReplaceConfidence(context, null).Value = 0.9;
            state.CreateOrReplaceExplanationUri(context, null).Value = explanationUri;
            BaseDataVariableState<VisionImageReferenceDataType> frame =
                state.CreateOrReplaceFrame(context, null);
            frame.Value = new VisionImageReferenceDataType
            {
                Uri = FormattableString.Invariant(
                    $"opcua-inline://test-cell/frames/{resultId}"),
                Digest = ByteString.Empty,
                DigestAlgorithm = string.Empty,
                Format = VisionClipFormatEnum.Png,
                PixelFormat = "Mono8",
                Width = 640u,
                Height = 480u,
                SizeBytes = 0u,
                Timestamp = timestamp
            };
            if (state.Detections != null)
            {
                state.Detections.Value = detections;
            }
            state.AddFrameId(context, NodeId.Null);
            if (state.FrameId != null)
            {
                state.FrameId.Value = target.CameraFrameId;
            }
            state.NodeId = context.RequireNodeIdFactory().New(context, state);
            context.AssignInstanceChildNodeIds(state, state.NodeId);
            target.ResultsFolder.AddChild(state);
            await target.NodeManager.AddPredefinedNodeAsync(state, cancellationToken).ConfigureAwait(false);
            m_results[resultId] = state;
        }

        private const string ExplanationUri = "urn:opcfoundation:tests:vision:agent";

        private readonly ConcurrentDictionary<string, DetectionResultState> m_results = new(StringComparer.Ordinal);
        private TestInferenceTarget? m_target;
        private string m_lastPublishedResultId = string.Empty;
        private double m_imageWidth;
        private double m_imageHeight;
    }
}
