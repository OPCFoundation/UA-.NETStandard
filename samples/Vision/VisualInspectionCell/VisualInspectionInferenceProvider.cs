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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Vision;
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
            if (analysis.Verdict == Opc.Ua.Vision.VisionResultEvaluationEnum.NotDecidable)
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
            Opc.Ua.Vision.VisionResultEvaluationEnum verdict);
    }
}
