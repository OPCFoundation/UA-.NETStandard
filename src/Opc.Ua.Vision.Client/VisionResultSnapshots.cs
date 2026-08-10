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

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// A snapshot of a <c>CoordinateFrameType</c> instance (§5.8).
    /// </summary>
    public sealed record VisionFrameSnapshot
    {
        /// <summary>
        /// The frame's own NodeId.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The Server-stable frame identifier string. §5.12 requires this to be
        /// non-empty wherever a pose is published.
        /// </summary>
        public string? FrameId { get; init; }

        /// <summary>
        /// The ISO 9787 frame role played by this frame — for example <c>World</c>,
        /// <c>Base</c>, <c>MechanicalInterface</c>, <c>Tool</c>, <c>Object</c>, or the
        /// non-ISO <c>Camera</c>.
        /// </summary>
        public VisionFrameRoleEnum Role { get; init; }

        /// <summary>
        /// The NodeId of the parent frame, or a null NodeId when this is a root.
        /// </summary>
        public NodeId ParentFrameId { get; init; } = NodeId.Null;

        /// <summary>
        /// The transform from this frame to <see cref="ParentFrameId"/>, or
        /// <c>null</c> when the Server did not report it. <c>Position</c> is in
        /// metres, <c>Orientation</c> is a unit quaternion ordered (x, y, z, w).
        /// </summary>
        public VisionPose3DDataType? Transform { get; init; }
    }

    /// <summary>
    /// A snapshot of an <c>InferencePipelineType</c> instance (§8.3).
    /// </summary>
    public sealed record VisionPipelineSnapshot
    {
        /// <summary>
        /// The pipeline NodeId.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The Server-stable pipeline identifier.
        /// </summary>
        public string? PipelineId { get; init; }

        /// <summary>
        /// The NodeId of the bound sensor, or a null NodeId when not reported.
        /// </summary>
        public NodeId SensorId { get; init; } = NodeId.Null;

        /// <summary>
        /// The NodeId of the deployment executing inference, or a null NodeId when the
        /// pipeline is not bound to a described deployment.
        /// </summary>
        public NodeId DeploymentId { get; init; } = NodeId.Null;

        /// <summary>
        /// The current pipeline state (§6.6).
        /// </summary>
        public VisionEndpointStateEnum State { get; init; }

        /// <summary>
        /// Whether the pipeline is currently running continuous inference.
        /// </summary>
        public bool Continuous { get; init; }

        /// <summary>
        /// The NodeId of the associated learning job, or a null NodeId when the
        /// Server retains no ground-truth corrections.
        /// </summary>
        public NodeId LearningJobId { get; init; } = NodeId.Null;
    }

    /// <summary>
    /// A snapshot of a <c>DetectionResultType</c> instance (§7.3).
    /// </summary>
    public sealed record VisionDetectionResultSnapshot
    {
        /// <summary>
        /// The result NodeId.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The result identifier string.
        /// </summary>
        public string? ResultId { get; init; }

        /// <summary>
        /// The time the result was published.
        /// </summary>
        public DateTimeUtc CreationTime { get; init; }

        /// <summary>
        /// The sensor that produced the frame the result was computed from.
        /// </summary>
        public NodeId SensorId { get; init; } = NodeId.Null;

        /// <summary>
        /// The pipeline that computed the result.
        /// </summary>
        public NodeId PipelineId { get; init; } = NodeId.Null;

        /// <summary>
        /// The model version used to compute the result, when reported.
        /// </summary>
        public string? ModelVersionUsed { get; init; }

        /// <summary>
        /// A descriptor for the image the detections apply to, when reported.
        /// </summary>
        public VisionImageReferenceDataType? Frame { get; init; }

        /// <summary>
        /// The frame that detection poses are expressed in. §7.3 requires this to be
        /// non-empty whenever any detection has <c>HasPose = true</c>.
        /// </summary>
        public string? FrameId { get; init; }

        /// <summary>
        /// The detected instances.
        /// </summary>
        public ArrayOf<VisionDetectionDataType> Detections { get; init; }
    }

    /// <summary>
    /// A snapshot of an <c>InspectionResultType</c> instance (§7.2).
    /// </summary>
    public sealed record VisionInspectionResultSnapshot
    {
        /// <summary>
        /// The result NodeId.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The result identifier string.
        /// </summary>
        public string? ResultId { get; init; }

        /// <summary>
        /// The time the result was published.
        /// </summary>
        public DateTimeUtc CreationTime { get; init; }

        /// <summary>
        /// The sensor that produced the frame the result was computed from.
        /// </summary>
        public NodeId SensorId { get; init; } = NodeId.Null;

        /// <summary>
        /// The pipeline that computed the result.
        /// </summary>
        public NodeId PipelineId { get; init; } = NodeId.Null;

        /// <summary>
        /// The model version used to compute the result, when reported.
        /// </summary>
        public string? ModelVersionUsed { get; init; }

        /// <summary>
        /// A descriptor for the image the inspection was computed from, when reported.
        /// </summary>
        public VisionImageReferenceDataType? Frame { get; init; }

        /// <summary>
        /// The overall inspection evaluation.
        /// </summary>
        public VisionResultEvaluationEnum Evaluation { get; init; }

        /// <summary>
        /// The identifier of the inspected part, when reported.
        /// </summary>
        public string? PartId { get; init; }

        /// <summary>
        /// The identifier of the inspection recipe, when reported.
        /// </summary>
        public string? RecipeId { get; init; }

        /// <summary>
        /// The measured characteristics.
        /// </summary>
        public ArrayOf<VisionCharacteristicDataType> Characteristics { get; init; }
    }

    /// <summary>
    /// A snapshot of a <c>SegmentationResultType</c> instance (§7.4).
    /// </summary>
    public sealed record VisionSegmentationResultSnapshot
    {
        /// <summary>
        /// The result NodeId.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The result identifier string.
        /// </summary>
        public string? ResultId { get; init; }

        /// <summary>
        /// The time the result was published.
        /// </summary>
        public DateTimeUtc CreationTime { get; init; }

        /// <summary>
        /// The sensor that produced the frame the result was computed from.
        /// </summary>
        public NodeId SensorId { get; init; } = NodeId.Null;

        /// <summary>
        /// The pipeline that computed the result.
        /// </summary>
        public NodeId PipelineId { get; init; } = NodeId.Null;

        /// <summary>
        /// A descriptor for the image the mask applies to, when reported.
        /// </summary>
        public VisionImageReferenceDataType? Frame { get; init; }

        /// <summary>
        /// The class labels that pixel indices of <see cref="Mask"/> refer to.
        /// </summary>
        public ArrayOf<string> LabelClasses { get; init; }

        /// <summary>
        /// A reference to the mask image (§7.4). Masks follow the media rules of §6 and
        /// are never inlined into the result.
        /// </summary>
        public VisionImageReferenceDataType? Mask { get; init; }
    }
}
