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

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Mcp.Tools
{
    // CLR arrays are intentional at this JSON boundary: the MCP schema generator exposes
    // ArrayOf<T> as its backing memory object and cannot bind an incoming JSON array to it.
    // The same projection applies on the way out, so the result DTOs below publish plain
    // JSON arrays and the manager projects the OPC UA ArrayOf<T> values into them.

    /// <summary>
    /// Deterministic policy used to select one detection from the filtered set.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<VisionPickSelectionPolicy>))]
    public enum VisionPickSelectionPolicy
    {
        /// <summary>
        /// Select the detection with the highest confidence. Ties are broken by
        /// ordinal DetectionId order and then by the original result order, so the
        /// same detection set always selects the same detection.
        /// </summary>
        HighestConfidence
    }

    /// <summary>
    /// Discriminator for the single piece of work a vision-guided pick submitted.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<VisionPickSubmissionKind>))]
    public enum VisionPickSubmissionKind
    {
        /// <summary>
        /// A single Pick intent was submitted because no destination was requested.
        /// </summary>
        Pick,

        /// <summary>
        /// A two-step Pick/Place mission was submitted because a destination was requested.
        /// </summary>
        Mission
    }

    /// <summary>
    /// Structured input for <c>robotics_vision_pick</c>.
    /// </summary>
    public sealed class VisionGuidedPickRequest
    {
        /// <summary>
        /// Gets or sets the controller selector.
        /// </summary>
        [Description("Controller selector: unique display name or BrowseName (e.g. 'Controller1') or " +
            "OPC UA NodeId string. Matched with exact ordinal comparison after trimming.")]
        public required string Controller { get; set; }

        /// <summary>
        /// Gets or sets the Vision pipeline selector.
        /// </summary>
        [Description("Vision pipeline selector: an exact BrowseName, DisplayName, or NodeId string " +
            "(e.g. 'BinPickingPipeline'). Resolved on the same OPC UA session as the controller.")]
        public required string Pipeline { get; set; }

        /// <summary>
        /// Gets or sets the source location selector for the Pick intent.
        /// </summary>
        [Description("Source location name or NodeId the Pick intent takes the workpiece from.")]
        public required string Source { get; set; }

        /// <summary>
        /// Gets or sets the tool selector for the submitted intents.
        /// </summary>
        [Description("Tool name or NodeId used for the Pick and, when requested, the Place intent.")]
        public required string Tool { get; set; }

        /// <summary>
        /// Gets or sets the optional destination location selector.
        /// </summary>
        [Description("Optional destination location name or NodeId. When set, a two-step Pick/Place " +
            "mission is submitted instead of a single Pick intent.")]
        public string? Destination { get; set; }

        /// <summary>
        /// Gets or sets the exact DetectionId filter.
        /// </summary>
        [Description("Optional exact DetectionId filter, compared with ordinal equality.")]
        public string? DetectionId { get; set; }

        /// <summary>
        /// Gets or sets the exact class label filter.
        /// </summary>
        [Description("Optional exact detection ClassLabel filter, compared with ordinal equality.")]
        public string? ClassLabel { get; set; }

        /// <summary>
        /// Gets or sets the inclusive minimum confidence filter.
        /// </summary>
        [Range(0.0, 1.0)]
        [Description("Optional inclusive minimum detection confidence within [0, 1].")]
        public double? MinimumConfidence { get; set; }

        /// <summary>
        /// Gets or sets the deterministic selection policy.
        /// </summary>
        [DefaultValue(VisionPickSelectionPolicy.HighestConfidence)]
        [Description("Deterministic selection policy applied to the filtered detections. " +
            "HighestConfidence breaks ties by ordinal DetectionId and then original order.")]
        public VisionPickSelectionPolicy Selection { get; set; } = VisionPickSelectionPolicy.HighestConfidence;

        /// <summary>
        /// Gets or sets the object class override for the Pick intent.
        /// </summary>
        [Description("Optional ObjectClass override for the Pick intent. Defaults to the selected " +
            "detection's ClassLabel.")]
        public string? ObjectClass { get; set; }

        /// <summary>
        /// Gets or sets the IntentId of the Pick intent.
        /// </summary>
        [Description("Optional IntentId for the Pick intent.")]
        public string? PickIntentId { get; set; }

        /// <summary>
        /// Gets or sets the IntentId of the Place intent.
        /// </summary>
        [Description("Optional IntentId for the Place intent. Requires destination.")]
        public string? PlaceIntentId { get; set; }

        /// <summary>
        /// Gets or sets the localized label applied to both intents.
        /// </summary>
        [Description("Optional localized label applied to the submitted intents and mission.")]
        public string? Label { get; set; }

        /// <summary>
        /// Gets or sets the buffer mode applied to both intents.
        /// </summary>
        [Description("Optional buffer mode applied to the submitted intents.")]
        public BufferModeEnum? BufferMode { get; set; }

        /// <summary>
        /// Gets or sets the blocking mode applied to both intents.
        /// </summary>
        [Description("Optional blocking mode applied to the submitted intents.")]
        public BlockingModeEnum? BlockingMode { get; set; }

        /// <summary>
        /// Gets or sets the MissionId of the submitted mission.
        /// </summary>
        [Description("Optional MissionId for the Pick/Place mission. Requires destination.")]
        public string? MissionId { get; set; }

        /// <summary>
        /// Gets or sets the MissionUpdateId of the submitted mission.
        /// </summary>
        [Description("Optional MissionUpdateId for the Pick/Place mission. Requires destination. Default 1.")]
        public uint? MissionUpdateId { get; set; }

        /// <summary>
        /// Gets or sets the session name.
        /// </summary>
        [Description("Session name to use; defaults to the only active session. The Vision pipeline and " +
            "the Robot Intent controller are always resolved on the same session.")]
        public string? SessionName { get; set; }
    }

    /// <summary>
    /// The closed result of one vision-guided pick. Exactly one of
    /// <see cref="PickSubmission"/> and <see cref="MissionSubmission"/> is populated,
    /// selected by <see cref="Kind"/>.
    /// </summary>
    public sealed class VisionGuidedPickResult
    {
        /// <summary>
        /// Gets or sets the kind of work that was submitted.
        /// </summary>
        [Description("Which submission was made: Pick or Mission.")]
        public VisionPickSubmissionKind Kind { get; set; }

        /// <summary>
        /// Gets or sets the perception provenance of the selected detection.
        /// </summary>
        [Description("Perception provenance: the Vision result, pipeline, sensor, model, frame, and the " +
            "selected detection the submitted work was derived from.")]
        public VisionPickProvenance Provenance { get; set; } = new();

        /// <summary>
        /// Gets or sets the authoritative single-intent submission outcome.
        /// </summary>
        [Description("Authoritative IntentSubmissionResult when kind is 'Pick'; null otherwise.")]
        public VisionPickIntentSubmission? PickSubmission { get; set; }

        /// <summary>
        /// Gets or sets the authoritative mission submission outcome.
        /// </summary>
        [Description("Authoritative MissionSubmissionResult when kind is 'Mission'; null otherwise.")]
        public VisionPickMissionSubmission? MissionSubmission { get; set; }

        /// <summary>
        /// Gets or sets the immediate mission step to operation mapping.
        /// </summary>
        [Description("Immediate mission step-to-operation mapping read once after an accepted mission " +
            "submission. Empty for a Pick submission or a refused mission.")]
        public VisionPickMissionStep[] Steps { get; set; } = [];
    }

    /// <summary>
    /// Refusal-shaped outcome of the single Pick intent submission.
    /// </summary>
    public sealed class VisionPickIntentSubmission
    {
        /// <summary>
        /// Gets or sets a value indicating whether the server accepted the intent.
        /// </summary>
        [Description("Whether the server accepted the Pick intent.")]
        public bool Accepted { get; set; }

        /// <summary>
        /// Gets or sets the IntentId the server acknowledged.
        /// </summary>
        [Description("IntentId the server acknowledged.")]
        public string IntentId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the operation NodeId, or null when none was returned.
        /// </summary>
        [Description("Operation NodeId, or null when the server returned none.")]
        public string? Operation { get; set; }

        /// <summary>
        /// Gets or sets the authoritative failure reason.
        /// </summary>
        [Description("Authoritative IntentFailureEnum reported by the server.")]
        public IntentFailureEnum Failure { get; set; }

        /// <summary>
        /// Gets or sets the authoritative refusal message.
        /// </summary>
        [Description("Authoritative refusal message reported by the server, or null.")]
        public string? Message { get; set; }
    }

    /// <summary>
    /// Refusal-shaped outcome of the Pick/Place mission submission.
    /// </summary>
    public sealed class VisionPickMissionSubmission
    {
        /// <summary>
        /// Gets or sets a value indicating whether the server accepted the mission.
        /// </summary>
        [Description("Whether the server accepted the mission.")]
        public bool Accepted { get; set; }

        /// <summary>
        /// Gets or sets the MissionId the server acknowledged.
        /// </summary>
        [Description("MissionId the server acknowledged.")]
        public string MissionId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the submitted MissionUpdateId.
        /// </summary>
        [Description("MissionUpdateId that was submitted.")]
        public uint MissionUpdateId { get; set; }

        /// <summary>
        /// Gets or sets the mission NodeId, or null when none was returned.
        /// </summary>
        [Description("Mission NodeId, or null when the server returned none.")]
        public string? Operation { get; set; }

        /// <summary>
        /// Gets or sets the authoritative failure reason.
        /// </summary>
        [Description("Authoritative IntentFailureEnum reported by the server.")]
        public IntentFailureEnum Failure { get; set; }

        /// <summary>
        /// Gets or sets the authoritative refusal message.
        /// </summary>
        [Description("Authoritative refusal message reported by the server, or null.")]
        public string? Message { get; set; }

        /// <summary>
        /// Gets or sets the StepId of the generated Pick step.
        /// </summary>
        [Description("StepId of the generated Pick step.")]
        public string PickStepId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the StepId of the generated Place step.
        /// </summary>
        [Description("StepId of the generated Place step.")]
        public string PlaceStepId { get; set; } = string.Empty;
    }

    /// <summary>
    /// One mission step mapped to the operation the server created for it.
    /// </summary>
    public sealed class VisionPickMissionStep
    {
        /// <summary>
        /// Gets or sets the step identifier.
        /// </summary>
        [Description("Step identifier.")]
        public string StepId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the intent identifier.
        /// </summary>
        [Description("Intent identifier.")]
        public string IntentId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the operation NodeId, or null if not yet executing.
        /// </summary>
        [Description("Operation NodeId, or null if the server has not created one yet.")]
        public string? Operation { get; set; }

        /// <summary>
        /// Gets or sets the step execution state.
        /// </summary>
        [Description("Step execution state.")]
        public ExecutionStateEnum State { get; set; }
    }

    /// <summary>
    /// The perception provenance of a vision-guided pick: which Vision result the
    /// submitted work was derived from and which detection was selected.
    /// </summary>
    public sealed class VisionPickProvenance
    {
        /// <summary>
        /// Gets or sets the ResultId the Vision server assigned to the run.
        /// </summary>
        [Description("ResultId the Vision server assigned to the inference run.")]
        public string ResultId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the NodeId of the published result.
        /// </summary>
        [Description("NodeId of the published Vision result.")]
        public string ResultNodeId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the NodeId of the pipeline that was asked to run.
        /// </summary>
        [Description("NodeId of the Vision pipeline the inference was requested on.")]
        public string RequestedPipelineNodeId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the published name of the requested pipeline.
        /// </summary>
        [Description("Published name of the requested Vision pipeline, when available.")]
        public string? RequestedPipelineName { get; set; }

        /// <summary>
        /// Gets or sets the PipelineId the result published.
        /// </summary>
        [Description("Pipeline NodeId published by the result, when available.")]
        public string? PipelineId { get; set; }

        /// <summary>
        /// Gets or sets the sensor NodeId the result published.
        /// </summary>
        [Description("Sensor NodeId that produced the frame, when available.")]
        public string? SensorId { get; set; }

        /// <summary>
        /// Gets or sets the model version the server reported.
        /// </summary>
        [Description("Model version used by the pipeline, when reported.")]
        public string? ModelVersionUsed { get; set; }

        /// <summary>
        /// Gets or sets the result creation time.
        /// </summary>
        [Description("Result creation time in ISO 8601, when available.")]
        public string? CreationTime { get; set; }

        /// <summary>
        /// Gets or sets the frame identifier the pose is expressed in.
        /// </summary>
        [Description("Frame identifier the selected pose is expressed in, when available.")]
        public string? FrameId { get; set; }

        /// <summary>
        /// Gets or sets the total number of detections the result published.
        /// </summary>
        [Description("Total number of detections in the published result.")]
        public int TotalDetections { get; set; }

        /// <summary>
        /// Gets or sets the number of detections that survived the filters.
        /// </summary>
        [Description("Number of detections that matched the requested filters.")]
        public int MatchedDetections { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the full detection snapshot was read.
        /// </summary>
        [Description("Whether the full detection snapshot was read because the bounded summary " +
            "did not carry every detection.")]
        public bool FullResultRead { get; set; }

        /// <summary>
        /// Gets or sets the selected detection.
        /// </summary>
        [Description("The detection the submitted work was derived from.")]
        public VisionPickDetection SelectedDetection { get; set; } = new();
    }

    /// <summary>
    /// The detection a vision-guided pick selected, with its pose.
    /// </summary>
    public sealed class VisionPickDetection
    {
        /// <summary>
        /// Gets or sets the detection identifier.
        /// </summary>
        [Description("Detection identifier.")]
        public string DetectionId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the detection class label.
        /// </summary>
        [Description("Detection class label.")]
        public string ClassLabel { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the detection class identifier.
        /// </summary>
        [Description("Detection class identifier.")]
        public uint ClassId { get; set; }

        /// <summary>
        /// Gets or sets the detection confidence.
        /// </summary>
        [Description("Detection confidence within [0, 1].")]
        public double Confidence { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the detection carries a pose.
        /// </summary>
        [Description("Whether the detection carries a pose.")]
        public bool HasPose { get; set; }

        /// <summary>
        /// Gets or sets the frame identifier of the pose.
        /// </summary>
        [Description("Frame identifier the pose is expressed in, or null when the detection has no pose.")]
        public string? PoseFrameId { get; set; }

        /// <summary>
        /// Gets or sets the pose position as [x, y, z].
        /// </summary>
        [Description("Pose position as [x, y, z] in metres, or null when the detection has no pose. " +
            "The pose covariance is deliberately not published here.")]
        public double[]? PosePosition { get; set; }

        /// <summary>
        /// Gets or sets the pose orientation quaternion as [x, y, z, w].
        /// </summary>
        [Description("Pose orientation quaternion as [x, y, z, w], or null when the detection has no pose.")]
        public double[]? PoseOrientation { get; set; }
    }
}
