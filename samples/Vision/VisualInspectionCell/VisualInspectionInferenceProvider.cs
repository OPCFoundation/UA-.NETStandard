// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Vision.Perception;
using Opc.Ua.Vision.Server;

namespace Vision.VisualInspectionCell
{
    internal sealed class VisualInspectionInferenceProvider : IVisionInferenceProvider
    {
        public VisualInspectionInferenceProvider(
            VisualInspectionAnalysisService analysis,
            VisualInspectionResultPublisher publisher,
            OperatorDialogController operatorDialog,
            ILogger<VisualInspectionInferenceProvider> logger)
        {
            m_analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
            m_publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            m_operatorDialog = operatorDialog ?? throw new ArgumentNullException(nameof(operatorDialog));
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
                throw new InvalidOperationException("The inference provider is already attached.");
            }
            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.InferenceAttached(target.PipelineNodeId.ToString());
            }
        }

        public async ValueTask<VisionInferenceRunResult> RunInferenceAsync(
            VisionInferenceRunRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VisualInspectionTarget target = RequireTarget();
            if (request.Timestamp.IsNull)
            {
                return new VisionInferenceRunResult(
                    new ServiceResult(StatusCodes.BadInvalidArgument,
                        LocalizedText.From("RunInference requires a timestamp so retries use a stable result id.")),
                    string.Empty);
            }

            DateTimeUtc timestamp = request.Timestamp;
            InspectionAnalysis analysis = m_analysis.AnalyzeForCycle(timestamp);
            string resultId = FormattableString.Invariant(
                $"insp-{PathSafeFixtureName(analysis.FixtureName)}-{timestamp.Value}");
            VisionImageReferenceDataType frameReference =
                m_analysis.CreateImageReference(analysis.FixtureName, timestamp);
            PublishedInspectionResult published = await m_publisher.PublishAsync(
                target,
                resultId,
                timestamp,
                analysis.Verdict,
                analysis.Characteristics,
                ModelVersion,
                frameReference,
                analysis.FixtureName,
                cancellationToken).ConfigureAwait(false);
            if (analysis.Verdict == VisionResultEvaluationEnum.NotDecidable)
            {
                m_operatorDialog.RequestDisposition(published);
            }
            m_logger.InferencePublished(resultId, analysis.FixtureName, analysis.Verdict);
            return new VisionInferenceRunResult(ServiceResult.Good, resultId);
        }

        public ValueTask<ServiceResult> StartContinuousAsync(NodeId pipeline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ServiceResult(StatusCodes.BadNotSupported,
                LocalizedText.From("This sample is externally driven; call RunInference for each inspection.")));
        }

        public ValueTask<ServiceResult> StopAsync(NodeId pipeline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ServiceResult.Good);
        }

        private VisualInspectionTarget RequireTarget()
        {
            return m_target ?? throw new InvalidOperationException("The inference provider is not attached.");
        }

        private static string PathSafeFixtureName(string fixtureName)
        {
            return fixtureName.Replace(".png", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace('-', '_');
        }

        private const string ModelVersion = "visual-inspection-analyser-1";
        private readonly VisualInspectionAnalysisService m_analysis;
        private readonly VisualInspectionResultPublisher m_publisher;
        private readonly OperatorDialogController m_operatorDialog;
        private readonly ILogger<VisualInspectionInferenceProvider> m_logger;
        private VisualInspectionTarget? m_target;
    }

    internal static partial class VisualInspectionInferenceProviderLog
    {
        [LoggerMessage(EventId = VisualInspectionCellEventIds.Inference + 1,
            Level = LogLevel.Information,
            Message = "Visual inspection inference attached to pipeline {PipelineNodeId}.")]
        public static partial void InferenceAttached(
            this ILogger<VisualInspectionInferenceProvider> logger,
            string pipelineNodeId);

        [LoggerMessage(EventId = VisualInspectionCellEventIds.Inference + 2,
            Level = LogLevel.Information,
            Message = "Published inspection result {ResultId} from {FixtureName}: {Verdict}.")]
        public static partial void InferencePublished(
            this ILogger<VisualInspectionInferenceProvider> logger,
            string resultId,
            string fixtureName,
            VisionResultEvaluationEnum verdict);
    }
}
