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
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for publishing off-server detections, inspections, corrections
    /// and image references back to a Vision server via its Feedback object.
    /// </summary>
    [McpServerToolType]
    public sealed class VisionFeedbackTools
    {
        /// <summary>
        /// Submits detections to a Vision Feedback object.
        /// </summary>
        [McpServerTool(Name = "vision_submit_detections")]
        [Description("Publishes a batch of off-server detections to a Vision Feedback object. Use this " +
            "when the language model itself produced the detections and wants the server to persist them " +
            "as first-class results. Use vision_submit_correction instead when correcting an existing " +
            "result, and vision_submit_image_reference to publish only the frame the detections apply to. " +
            "Detections are JSON: an array of objects with detectionId, classLabel, classId, confidence, " +
            "optional boundingBox2D { centerX, centerY, width, height, rotation }, optional boundingBox3D " +
            "{ center pose, size[3] }, optional pose { frameId, position[3], orientation[4], covariance[36] } " +
            "and optional trackId. To report that a frame was examined and contains nothing — the " +
            "terminating condition of a pick-and-place task, and a valid negative training label — pass " +
            "an empty array and set sceneIsEmpty. Reports the server's refusal honestly; never retries " +
            "silently and never acquires authority as a side effect.")]
        public static Task SubmitDetectionsAsync(
            VisionClientAccessor accessor,
            [Description("Pipeline NodeId whose Feedback object should receive the detections.")] string pipelineNodeId,
            [Description("Purpose the detections are submitted for: Overlay, Reconciliation, GroundTruthLabel " +
                "or Trigger.")]
            VisionFeedbackPurposeEnum purpose,
            [Description("JSON array of detections as documented on this tool.")] string detectionsJson,
            [Description("Set when the frame was examined and found to contain nothing. Required for an " +
                "empty detections array, and rejected when detections are present.")]
            bool sceneIsEmpty = false,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            ArrayOf<VisionDetectionDataType> detections = VisionJson.BuildDetections(detectionsJson);
            return SubmitDetectionsCoreAsync(
                accessor, pipelineNodeId, purpose, detections, sceneIsEmpty, sessionName, ct);
        }

        /// <summary>
        /// Submits an inspection result to a Vision Feedback object.
        /// </summary>
        [McpServerTool(Name = "vision_submit_inspection_result")]
        [Description("Publishes an off-server inspection verdict to a Vision Feedback object. Use this " +
            "when the language model evaluated the sensor's part against a recipe and wants the server to " +
            "persist the verdict and its measured characteristics. Use vision_submit_detections instead " +
            "for detection payloads and vision_submit_correction to correct an existing published result. " +
            "Characteristics are JSON: an array of objects with characteristicId, name, nominal, actual, " +
            "deviation, lowerTolerance, upperTolerance, uncertainty, optional unit NodeId string and status " +
            "(Ok, NotOk, NotDecidable, Undefined). Reports the server's refusal honestly; never retries " +
            "silently.")]
        public static Task SubmitInspectionResultAsync(
            VisionClientAccessor accessor,
            [Description("Pipeline NodeId whose Feedback object should receive the inspection result.")] string pipelineNodeId,
            [Description("Stable identifier of the inspection result.")] string resultId,
            [Description("Overall evaluation: Ok, NotOk, NotDecidable or Undefined.")]
            VisionResultEvaluationEnum evaluation,
            [Description("JSON array of measured characteristics as documented on this tool.")] string characteristicsJson,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            ArrayOf<VisionCharacteristicDataType> characteristics = VisionJson.BuildCharacteristics(
                characteristicsJson);
            return SubmitInspectionCoreAsync(
                accessor, pipelineNodeId, resultId, evaluation, characteristics, sessionName, ct);
        }

        /// <summary>
        /// Submits a correction against an existing result.
        /// </summary>
        [McpServerTool(Name = "vision_submit_correction")]
        [Description("Publishes a correction to an existing Vision result identified by its ResultId. Use " +
            "this when the language model disagrees with a server-published detection or inspection and " +
            "wants to attach a corrected version. Use vision_submit_detections instead to publish fresh " +
            "detections without a target result, and vision_submit_inspection_result for a fresh verdict. " +
            "At most one of correctedDetectionsJson or correctedCharacteristicsJson may be provided. To " +
            "retract a false positive — asserting the result should contain nothing at all, which is the " +
            "error class an operator can label most confidently — omit both and set retractAll. Reports " +
            "the server's refusal honestly; never retries silently.")]
        public static Task SubmitCorrectionAsync(
            VisionClientAccessor accessor,
            [Description("Pipeline NodeId whose Feedback object should receive the correction.")] string pipelineNodeId,
            [Description("Stable identifier of the result being corrected.")] string resultId,
            [Description("Purpose the correction is submitted for: Overlay, Reconciliation, GroundTruthLabel " +
                "or Trigger.")]
            VisionFeedbackPurposeEnum purpose,
            [Description("Optional JSON array of corrected detections; mutually exclusive with " +
                "correctedCharacteristicsJson.")]
            string? correctedDetectionsJson = null,
            [Description("Optional JSON array of corrected characteristics; mutually exclusive with " +
                "correctedDetectionsJson.")]
            string? correctedCharacteristicsJson = null,
            [Description("Human-readable reason attached to the correction.")]
            string? reason = null,
            [Description("Set to retract the referenced result entirely, asserting it should contain " +
                "nothing. Requires both corrected arrays to be omitted.")]
            bool retractAll = false,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            bool hasDetections = !string.IsNullOrWhiteSpace(correctedDetectionsJson);
            bool hasCharacteristics = !string.IsNullOrWhiteSpace(correctedCharacteristicsJson);
            if (retractAll)
            {
                if (hasDetections || hasCharacteristics)
                {
                    throw new ArgumentException(
                        "Both corrected arrays must be omitted when retractAll is set.",
                        nameof(correctedDetectionsJson));
                }
            }
            else if (hasDetections == hasCharacteristics)
            {
                throw new ArgumentException(
                    "Provide exactly one of correctedDetectionsJson and correctedCharacteristicsJson, " +
                    "or set retractAll to retract the result entirely.",
                    nameof(correctedDetectionsJson));
            }
            ArrayOf<VisionDetectionDataType> detections = hasDetections
                ? VisionJson.BuildDetections(correctedDetectionsJson!)
                : ArrayOf<VisionDetectionDataType>.Empty;
            ArrayOf<VisionCharacteristicDataType> characteristics = hasCharacteristics
                ? VisionJson.BuildCharacteristics(correctedCharacteristicsJson!)
                : ArrayOf<VisionCharacteristicDataType>.Empty;
            LocalizedText localizedReason = string.IsNullOrEmpty(reason)
                ? LocalizedText.Null
                : new LocalizedText(reason);
            return SubmitCorrectionCoreAsync(
                accessor,
                pipelineNodeId,
                resultId,
                purpose,
                detections,
                characteristics,
                localizedReason,
                retractAll,
                sessionName,
                ct);
        }

        /// <summary>
        /// Submits an image reference to a Vision Feedback object.
        /// </summary>
        [McpServerTool(Name = "vision_submit_image_reference")]
        [Description("Publishes an image reference to a Vision Feedback object. Use this when the model " +
            "wants the server to persist the frame that a detection or correction was reasoned about, or " +
            "to attach a ground-truth image without any detections. Use vision_submit_detections when you " +
            "also want to publish detections against the frame. The image JSON is a single object with " +
            "uri, format (Jpeg, Png, ...), pixelFormat, width, height, sizeBytes, timestamp (ISO 8601), " +
            "digest (base64) and digestAlgorithm. Reports the server's refusal honestly; never retries " +
            "silently.")]
        public static Task SubmitImageReferenceAsync(
            VisionClientAccessor accessor,
            [Description("Pipeline NodeId whose Feedback object should receive the image reference.")] string pipelineNodeId,
            [Description("Purpose the image is submitted for: Overlay, Reconciliation, GroundTruthLabel " +
                "or Trigger.")]
            VisionFeedbackPurposeEnum purpose,
            [Description("JSON object describing the image reference as documented on this tool.")] string imageJson,
            [Description("Stable identifier of the target result, or an empty string.")] string resultId = "",
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionImageReferenceDataType image = VisionJson.BuildImageReference(imageJson, nameof(imageJson));
            return SubmitImageReferenceCoreAsync(
                accessor, pipelineNodeId, purpose, image, resultId, sessionName, ct);
        }

        private static async Task SubmitDetectionsCoreAsync(
            VisionClientAccessor accessor,
            string pipelineNodeId,
            VisionFeedbackPurposeEnum purpose,
            ArrayOf<VisionDetectionDataType> detections,
            bool sceneIsEmpty,
            string? sessionName,
            CancellationToken ct)
        {
            VisionFeedbackClient feedback = await accessor.OpenPipelineFeedbackAsync(
                pipelineNodeId, sessionName, ct).ConfigureAwait(false);
            await feedback.SubmitDetectionsAsync(
                purpose, detections, frameReference: null, inlineImage: ByteString.Empty,
                sceneIsEmpty, ct)
                .ConfigureAwait(false);
        }

        private static async Task SubmitInspectionCoreAsync(
            VisionClientAccessor accessor,
            string pipelineNodeId,
            string resultId,
            VisionResultEvaluationEnum evaluation,
            ArrayOf<VisionCharacteristicDataType> characteristics,
            string? sessionName,
            CancellationToken ct)
        {
            VisionFeedbackClient feedback = await accessor.OpenPipelineFeedbackAsync(
                pipelineNodeId, sessionName, ct).ConfigureAwait(false);
            await feedback.SubmitInspectionResultAsync(resultId, evaluation, characteristics, ct)
                .ConfigureAwait(false);
        }

        private static async Task SubmitCorrectionCoreAsync(
            VisionClientAccessor accessor,
            string pipelineNodeId,
            string resultId,
            VisionFeedbackPurposeEnum purpose,
            ArrayOf<VisionDetectionDataType> detections,
            ArrayOf<VisionCharacteristicDataType> characteristics,
            LocalizedText reason,
            bool retractAll,
            string? sessionName,
            CancellationToken ct)
        {
            VisionFeedbackClient feedback = await accessor.OpenPipelineFeedbackAsync(
                pipelineNodeId, sessionName, ct).ConfigureAwait(false);
            await feedback.SubmitCorrectionAsync(
                resultId,
                purpose,
                detections,
                characteristics,
                reason,
                inlineImage: ByteString.Empty,
                retractAll,
                ct).ConfigureAwait(false);
        }

        private static async Task SubmitImageReferenceCoreAsync(
            VisionClientAccessor accessor,
            string pipelineNodeId,
            VisionFeedbackPurposeEnum purpose,
            VisionImageReferenceDataType image,
            string resultId,
            string? sessionName,
            CancellationToken ct)
        {
            VisionFeedbackClient feedback = await accessor.OpenPipelineFeedbackAsync(
                pipelineNodeId, sessionName, ct).ConfigureAwait(false);
            await feedback.SubmitImageReferenceAsync(purpose, image, resultId, ct)
                .ConfigureAwait(false);
        }
    }
}
