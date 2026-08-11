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

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// Focused client over a single <c>VisionFeedbackType</c> instance — typically
    /// the <c>Feedback</c> object of a pipeline. Wraps <c>SubmitDetections</c>,
    /// <c>SubmitInspectionResult</c>, <c>SubmitCorrection</c> and
    /// <c>SubmitImageReference</c> so an off-Server model can publish results the
    /// Server did not compute (§9).
    /// </summary>
    /// <remarks>
    /// §9 requires a Server that does not permit a purpose (typically
    /// <c>GroundTruthLabel</c>) to refuse the call with <c>Bad_NotSupported</c>.
    /// This client surfaces refusals as <see cref="ServiceResultException"/> rather
    /// than swallowing them.
    /// </remarks>
    public sealed class VisionFeedbackClient
    {
        private readonly VisionFeedbackTypeClient m_proxy;

        internal VisionFeedbackClient(
            VisionClientOperations operations, NodeId feedbackNodeId)
        {
            if (operations is null)
            {
                throw new ArgumentNullException(nameof(operations));
            }
            if (feedbackNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Feedback NodeId must not be null.", nameof(feedbackNodeId));
            }
            FeedbackNodeId = feedbackNodeId;
            m_proxy = new VisionFeedbackTypeClient(
                operations.Session, feedbackNodeId, operations.Telemetry);
        }

        /// <summary>
        /// Gets the feedback object NodeId.
        /// </summary>
        public NodeId FeedbackNodeId { get; }

        /// <summary>
        /// Submits a set of detections for the given purpose (§9.2).
        /// </summary>
        /// <param name="purpose">
        /// The purpose the detections are submitted for — for example
        /// <c>InferredResult</c> from an off-Server model or
        /// <c>GroundTruthLabel</c> to feed a learning job.
        /// </param>
        /// <param name="detections">
        /// The detections to publish. Must be non-empty.
        /// </param>
        /// <param name="frameReference">
        /// The image the detections apply to, or <c>null</c> when the Server does
        /// not require a frame reference.
        /// </param>
        /// <param name="inlineImage">
        /// The optional inline image bytes; must fit
        /// <c>MaxInlineFeedbackImageSize</c>. Pass an empty <see cref="ByteString"/>
        /// to omit inline delivery.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task SubmitDetectionsAsync(
            VisionFeedbackPurposeEnum purpose,
            ArrayOf<VisionDetectionDataType> detections,
            VisionImageReferenceDataType? frameReference,
            ByteString inlineImage,
            CancellationToken cancellationToken = default)
        {
            if (detections.Count == 0)
            {
                // Part 9.5 requires Bad_InvalidArgument for an empty Detections array, so
                // refuse here rather than let the caller discover it at the Server. Note the
                // consequence: "I looked and the bin is empty" cannot be expressed at all,
                // which is raised upstream rather than deviated from locally.
                throw new ArgumentException(
                    "Detections must be non-empty.", nameof(detections));
            }
            return m_proxy.SubmitDetectionsAsync(
                purpose,
                detections,
                frameReference ?? new VisionImageReferenceDataType(),
                inlineImage,
                cancellationToken).AsTask();
        }

        /// <summary>
        /// Submits a completed inspection result (§9.3).
        /// </summary>
        /// <param name="resultId">
        /// The stable identifier of the inspection.
        /// </param>
        /// <param name="evaluation">
        /// The overall evaluation.
        /// </param>
        /// <param name="characteristics">
        /// The measured characteristics; must be non-empty.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task SubmitInspectionResultAsync(
            string resultId,
            VisionResultEvaluationEnum evaluation,
            ArrayOf<VisionCharacteristicDataType> characteristics,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(resultId))
            {
                throw new ArgumentException(
                    "ResultId must be non-empty.", nameof(resultId));
            }
            if (characteristics.Count == 0)
            {
                throw new ArgumentException(
                    "At least one characteristic must be supplied.",
                    nameof(characteristics));
            }
            return m_proxy.SubmitInspectionResultAsync(
                resultId, evaluation, characteristics, cancellationToken).AsTask();
        }

        /// <summary>
        /// Submits a correction against an existing result identified by
        /// <paramref name="resultId"/> (§9.4). §9.4.1 requires that exactly one of
        /// <paramref name="correctedDetections"/> or
        /// <paramref name="correctedCharacteristics"/> is non-empty.
        /// </summary>
        /// <param name="resultId">
        /// The stable identifier of the result being corrected.
        /// </param>
        /// <param name="purpose">
        /// The purpose the correction is submitted for.
        /// </param>
        /// <param name="correctedDetections">
        /// The corrected detections for a <c>DetectionResultType</c>.
        /// </param>
        /// <param name="correctedCharacteristics">
        /// The corrected characteristics for an <c>InspectionResultType</c>.
        /// </param>
        /// <param name="reason">
        /// A human-readable explanation.
        /// </param>
        /// <param name="inlineImage">
        /// The optional inline image bytes; must fit
        /// <c>MaxInlineFeedbackImageSize</c>.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task SubmitCorrectionAsync(
            string resultId,
            VisionFeedbackPurposeEnum purpose,
            ArrayOf<VisionDetectionDataType> correctedDetections,
            ArrayOf<VisionCharacteristicDataType> correctedCharacteristics,
            LocalizedText reason,
            ByteString inlineImage,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(resultId))
            {
                throw new ArgumentException(
                    "ResultId must be non-empty.", nameof(resultId));
            }
            bool hasDetections = correctedDetections.Count > 0;
            bool hasCharacteristics = correctedCharacteristics.Count > 0;
            if (hasDetections == hasCharacteristics)
            {
                throw new ArgumentException(
                    "Exactly one of correctedDetections and correctedCharacteristics " +
                    "must be non-empty.",
                    nameof(correctedDetections));
            }
            return m_proxy.SubmitCorrectionAsync(
                resultId,
                purpose,
                correctedDetections,
                correctedCharacteristics,
                reason.IsNull ? LocalizedText.Null : reason,
                inlineImage,
                cancellationToken).AsTask();
        }

        /// <summary>
        /// Submits an image reference for the given purpose (§9.5).
        /// </summary>
        /// <param name="purpose">
        /// The purpose the image is submitted for.
        /// </param>
        /// <param name="image">
        /// The image descriptor. Must be non-null.
        /// </param>
        /// <param name="resultId">
        /// The stable identifier of the target result, or an empty string.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task SubmitImageReferenceAsync(
            VisionFeedbackPurposeEnum purpose,
            VisionImageReferenceDataType image,
            string resultId,
            CancellationToken cancellationToken = default)
        {
            if (image is null)
            {
                throw new ArgumentNullException(nameof(image));
            }
            return m_proxy.SubmitImageReferenceAsync(
                purpose, image, resultId ?? string.Empty, cancellationToken).AsTask();
        }
    }
}
