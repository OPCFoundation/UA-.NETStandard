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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Vision;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Receives §9 feedback submissions from off-Server callers.
    /// </summary>
    /// <remarks>
    /// Vision feedback is a first-class publication path — it is how a
    /// remote VLM or a supervising cell publishes results the Server
    /// itself did not compute. A single sink is bound to one pipeline's
    /// <c>Feedback</c> object. Implementations must be thread-safe.
    /// </remarks>
    public interface IVisionFeedbackSink
    {
        /// <summary>
        /// Publishes detections against the pipeline. The sink is
        /// responsible for materialising a <c>DetectionResultType</c>
        /// under <c>Pipeline.Results</c> when appropriate for the
        /// <paramref name="request"/>'s purpose.
        /// </summary>
        ValueTask<ServiceResult> SubmitDetectionsAsync(
            VisionSubmitDetectionsRequest request,
            CancellationToken cancellationToken);

        /// <summary>
        /// Publishes an inspection result, either creating a new result or
        /// annotating an existing one referenced by
        /// <see cref="VisionSubmitInspectionResultRequest.ResultId"/>.
        /// </summary>
        ValueTask<ServiceResult> SubmitInspectionResultAsync(
            VisionSubmitInspectionResultRequest request,
            CancellationToken cancellationToken);

        /// <summary>
        /// Submits a correction to a previously published result.
        /// </summary>
        ValueTask<ServiceResult> SubmitCorrectionAsync(
            VisionSubmitCorrectionRequest request,
            CancellationToken cancellationToken);

        /// <summary>
        /// Registers an out-of-band image reference associated with the
        /// pipeline.
        /// </summary>
        ValueTask<ServiceResult> SubmitImageReferenceAsync(
            VisionSubmitImageReferenceRequest request,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Input to
    /// <see cref="IVisionFeedbackSink.SubmitDetectionsAsync"/>.
    /// </summary>
    public sealed record VisionSubmitDetectionsRequest(
        NodeId Pipeline,
        VisionFeedbackPurposeEnum Purpose,
        ArrayOf<VisionDetectionDataType> Detections,
        VisionImageReferenceDataType FrameReference,
        ByteString InlineImage,
        bool SceneIsEmpty = false);

    /// <summary>
    /// Input to
    /// <see cref="IVisionFeedbackSink.SubmitInspectionResultAsync"/>.
    /// </summary>
    public sealed record VisionSubmitInspectionResultRequest(
        NodeId Pipeline,
        string ResultId,
        VisionResultEvaluationEnum Evaluation,
        ArrayOf<VisionCharacteristicDataType> Characteristics);

    /// <summary>
    /// Input to
    /// <see cref="IVisionFeedbackSink.SubmitCorrectionAsync"/>.
    /// </summary>
    public sealed record VisionSubmitCorrectionRequest(
        NodeId Pipeline,
        string ResultId,
        VisionFeedbackPurposeEnum Purpose,
        ArrayOf<VisionDetectionDataType> CorrectedDetections,
        ArrayOf<VisionCharacteristicDataType> CorrectedCharacteristics,
        LocalizedText Reason,
        ByteString InlineImage,
        bool RetractAll = false);

    /// <summary>
    /// Input to
    /// <see cref="IVisionFeedbackSink.SubmitImageReferenceAsync"/>.
    /// </summary>
    public sealed record VisionSubmitImageReferenceRequest(
        NodeId Pipeline,
        VisionFeedbackPurposeEnum Purpose,
        VisionImageReferenceDataType Image,
        string ResultId);
}
