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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.AI.Server;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;

namespace Vision.VisualInspectionCell
{
    internal sealed partial class VisualInspectionFeedbackSink : IVisionFeedbackSink
    {
        public VisualInspectionFeedbackSink(
            VisualInspectionResultPublisher publisher,
            AiNodeManagerRegistry aiRegistry,
            InspectionVerdictPolicy verdictPolicy,
            TimeProvider timeProvider,
            ILogger<VisualInspectionFeedbackSink> logger)
        {
            m_publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            m_aiRegistry = aiRegistry ?? throw new ArgumentNullException(nameof(aiRegistry));
            m_verdictPolicy = verdictPolicy ?? throw new ArgumentNullException(nameof(verdictPolicy));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Attach(VisualInspectionTarget target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (Interlocked.CompareExchange(ref m_target, target, null) != null)
            {
                throw new InvalidOperationException("The feedback sink is already attached.");
            }
            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.FeedbackAttached(target.PipelineNodeId.ToString());
            }
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
            if (request.SceneIsEmpty && request.Purpose == VisionFeedbackPurposeEnum.GroundTruthLabel)
            {
                await RecordLearningSampleAsync(
                    StableSampleId(request.Pipeline, FrameKey(request.FrameReference, "empty-scene"), "scene-empty"),
                    AiLearningSampleKind.Negative,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (request.Purpose == VisionFeedbackPurposeEnum.GroundTruthLabel)
            {
                await RecordLearningSampleAsync(
                    StableSampleId(request.Pipeline, FrameKey(request.FrameReference, "geometry"), "geometry"),
                    AiLearningSampleKind.Positive,
                    cancellationToken).ConfigureAwait(false);
            }
            return ServiceResult.Good;
        }

        public async ValueTask<ServiceResult> SubmitInspectionResultAsync(
            VisionSubmitInspectionResultRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            VisualInspectionTarget target = RequireTarget();
            if (!request.Pipeline.Equals(target.PipelineNodeId))
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown,
                    LocalizedText.From("The pipeline node id does not match the attached inspection pipeline."));
            }
            string resultId = string.IsNullOrEmpty(request.ResultId)
                ? StableResultId(request.Pipeline, request.Characteristics)
                : request.ResultId;
            if (!TryGetImageReference(resultId, out VisionImageReferenceDataType frameReference))
            {
                return new ServiceResult(StatusCodes.BadInvalidState,
                    LocalizedText.From("SubmitImageReference must provide provenance before publishing a result."));
            }
            VisionResultEvaluationEnum evaluation = m_verdictPolicy.JudgeCharacteristics(request.Characteristics);
            PublishedInspectionResult published = await m_publisher.PublishAsync(
                target,
                resultId,
                TimestampOrNow(frameReference.Timestamp),
                evaluation,
                request.Characteristics,
                "agent-edge-off-server",
                frameReference,
                string.Empty,
                cancellationToken).ConfigureAwait(false);
            if (evaluation == VisionResultEvaluationEnum.NotDecidable)
            {
                // An off-server agent supplied evidence, but production policy still remains external:
                // the server only exposes the dialog and waits for the operator's OPC UA response.
                m_operatorDialog?.RequestDisposition(published);
            }
            m_logger.AgentInspectionPublished(resultId, evaluation);
            return ServiceResult.Good;
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
            VisualInspectionTarget target = RequireTarget();
            if (!request.Pipeline.Equals(target.PipelineNodeId))
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown,
                    LocalizedText.From("The pipeline node id does not match the attached inspection pipeline."));
            }
            AiLearningSampleKind kind = request.RetractAll
                ? AiLearningSampleKind.Negative
                : AiLearningSampleKind.Positive;
            string sampleId = StableSampleId(request.Pipeline, request.ResultId, request.Purpose.ToString());
            await RecordLearningSampleAsync(sampleId, kind, cancellationToken).ConfigureAwait(false);
            if (request.CorrectedCharacteristics.Count > 0)
            {
                string resultId = FormattableString.Invariant($"correction-{Sanitize(request.ResultId)}");
                VisionImageReferenceDataType frameReference = TryGetImageReference(request.ResultId, out var image)
                    ? image
                    : FrameFromPublished(request.ResultId);
                await m_publisher.PublishAsync(
                    target,
                    resultId,
                    TimestampOrNow(frameReference.Timestamp),
                    m_verdictPolicy.JudgeCharacteristics(request.CorrectedCharacteristics),
                    request.CorrectedCharacteristics,
                    "operator-ground-truth",
                    frameReference,
                    FixtureFromPublished(request.ResultId),
                    cancellationToken).ConfigureAwait(false);
            }
            m_logger.CorrectionRecorded(sampleId, kind);
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
            if (string.IsNullOrEmpty(request.ResultId))
            {
                return ValueTask.FromResult<ServiceResult>(new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    LocalizedText.From("An image reference must name the result it belongs to.")));
            }
            lock (m_lock)
            {
                m_imageReferences[request.ResultId] = request.Image;
            }
            return ValueTask.FromResult(ServiceResult.Good);
        }

        public void AttachOperatorDialog(OperatorDialogController operatorDialog)
        {
            m_operatorDialog = operatorDialog ?? throw new ArgumentNullException(nameof(operatorDialog));
        }

        public ValueTask HandleOperatorDispositionAsync(
            PublishedInspectionResult result,
            OperatorDisposition disposition,
            CancellationToken cancellationToken)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            return HandleOperatorDispositionCoreAsync(result, disposition, cancellationToken);
        }

        private async ValueTask RecordLearningSampleAsync(
            string sampleId,
            AiLearningSampleKind kind,
            CancellationToken cancellationToken)
        {
            AiNodeManager? manager = m_aiRegistry.NodeManager;
            if (manager == null)
            {
                m_logger.LearningSampleSkipped(sampleId);
                return;
            }
            bool added = await manager.RecordLearningSampleAsync(sampleId, kind, cancellationToken)
                .ConfigureAwait(false);
            m_logger.LearningSampleRecorded(sampleId, kind, added);
        }

        private VisualInspectionTarget RequireTarget()
        {
            return m_target ?? throw new InvalidOperationException("The feedback sink is not attached.");
        }

        private async ValueTask HandleOperatorDispositionCoreAsync(
            PublishedInspectionResult result,
            OperatorDisposition disposition,
            CancellationToken cancellationToken)
        {
            VisualInspectionTarget target = RequireTarget();
            AiLearningSampleKind kind = disposition == OperatorDisposition.AcceptAsNotOk ||
                disposition == OperatorDisposition.Stop
                ? AiLearningSampleKind.Negative
                : AiLearningSampleKind.Positive;
            string sampleId = StableSampleId(target.PipelineNodeId, result.ResultId, disposition.ToString());
            await RecordLearningSampleAsync(sampleId, kind, cancellationToken).ConfigureAwait(false);
            VisionResultEvaluationEnum evaluation = disposition switch
            {
                OperatorDisposition.AcceptAsOk => VisionResultEvaluationEnum.Ok,
                OperatorDisposition.AcceptAsNotOk => VisionResultEvaluationEnum.NotOk,
                OperatorDisposition.Stop => VisionResultEvaluationEnum.NotOk,
                _ => VisionResultEvaluationEnum.NotDecidable
            };
            string correctionId = FormattableString.Invariant(
                $"operator-{Sanitize(result.ResultId)}-{disposition}");
            await m_publisher.PublishAsync(
                target,
                correctionId,
                TimestampOrNow(result.FrameReference.Timestamp),
                evaluation,
                result.Characteristics,
                "operator-ground-truth",
                result.FrameReference,
                result.FixtureName,
                cancellationToken).ConfigureAwait(false);
            m_logger.OperatorDispositionRecorded(result.ResultId, disposition, sampleId, kind);
        }

        private static string StableSampleId(NodeId pipeline, string resultId, string purpose)
        {
            return FormattableString.Invariant($"{pipeline}|{resultId}|{purpose}");
        }

        private static string StableResultId(
            NodeId pipeline,
            ArrayOf<VisionCharacteristicDataType> characteristics)
        {
            string key = characteristics.Count == 0
                ? "empty"
                : characteristics[0].CharacteristicId + ":" +
                    characteristics[0].Actual.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return FormattableString.Invariant($"agent-insp-{Sanitize(pipeline.ToString())}-{Sanitize(key)}");
        }

        private static string FrameKey(VisionImageReferenceDataType frame, string fallback)
        {
            return string.IsNullOrEmpty(frame?.Uri) ? fallback : frame.Uri;
        }

        private bool TryGetImageReference(string resultId, out VisionImageReferenceDataType image)
        {
            lock (m_lock)
            {
                return m_imageReferences.TryGetValue(resultId, out image);
            }
        }

        private VisionImageReferenceDataType FrameFromPublished(string resultId)
        {
            return m_publisher.TryGetPublished(resultId, out PublishedInspectionResult? published) &&
                published != null
                ? published.FrameReference
                : new VisionImageReferenceDataType { Timestamp = TimestampOrNow(DateTimeUtc.MinValue) };
        }

        private string FixtureFromPublished(string resultId)
        {
            return m_publisher.TryGetPublished(resultId, out PublishedInspectionResult? published) &&
                published != null
                ? published.FixtureName
                : string.Empty;
        }

        private DateTimeUtc TimestampOrNow(DateTimeUtc timestamp)
        {
            return timestamp.IsNull ? DateTimeUtc.From(m_timeProvider.GetUtcNow()) : timestamp;
        }

        private static string Sanitize(string value)
        {
            return string.IsNullOrEmpty(value) ? "unknown" : value.Replace('-', '_');
        }

        private readonly VisualInspectionResultPublisher m_publisher;
        private readonly AiNodeManagerRegistry m_aiRegistry;
        private readonly InspectionVerdictPolicy m_verdictPolicy;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger<VisualInspectionFeedbackSink> m_logger;
        private readonly Lock m_lock = new();
        private readonly Dictionary<string, VisionImageReferenceDataType> m_imageReferences = [];
        private VisualInspectionTarget? m_target;
        private OperatorDialogController? m_operatorDialog;
    }

    internal static partial class VisualInspectionFeedbackSinkLog
    {
        [LoggerMessage(EventId = VisualInspectionCellEventIds.Feedback + 1,
            Level = LogLevel.Information,
            Message = "Visual inspection feedback sink attached to pipeline {PipelineNodeId}.")]
        public static partial void FeedbackAttached(this ILogger<VisualInspectionFeedbackSink> logger, string pipelineNodeId);

        [LoggerMessage(EventId = VisualInspectionCellEventIds.Feedback + 2,
            Level = LogLevel.Information,
            Message = "Published off-server inspection result {ResultId}: {Verdict}.")]
        public static partial void AgentInspectionPublished(
            this ILogger<VisualInspectionFeedbackSink> logger,
            string resultId,
            VisionResultEvaluationEnum verdict);

        [LoggerMessage(EventId = VisualInspectionCellEventIds.Feedback + 3,
            Level = LogLevel.Information,
            Message = "Recorded correction sample {SampleId} ({Kind}).")]
        public static partial void CorrectionRecorded(
            this ILogger<VisualInspectionFeedbackSink> logger,
            string sampleId,
            AiLearningSampleKind kind);

        [LoggerMessage(EventId = VisualInspectionCellEventIds.Feedback + 4,
            Level = LogLevel.Information,
            Message = "Learning sample {SampleId} ({Kind}) added={Added}.")]
        public static partial void LearningSampleRecorded(
            this ILogger<VisualInspectionFeedbackSink> logger,
            string sampleId,
            AiLearningSampleKind kind,
            bool added);

        [LoggerMessage(EventId = VisualInspectionCellEventIds.Feedback + 5,
            Level = LogLevel.Warning,
            Message = "AI node manager not available; learning sample {SampleId} was not counted.")]
        public static partial void LearningSampleSkipped(
            this ILogger<VisualInspectionFeedbackSink> logger,
            string sampleId);

        [LoggerMessage(EventId = VisualInspectionCellEventIds.Feedback + 6,
            Level = LogLevel.Information,
            Message = "Operator disposition {Disposition} for {ResultId} recorded as {SampleId} ({Kind}).")]
        public static partial void OperatorDispositionRecorded(
            this ILogger<VisualInspectionFeedbackSink> logger,
            string resultId,
            OperatorDisposition disposition,
            string sampleId,
            AiLearningSampleKind kind);
    }
}
