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
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for reading Vision sensor and pipeline state.
    /// </summary>
    [McpServerToolType]
    public sealed class VisionMonitoringTools
    {
        /// <summary>
        /// Reads sensor identity, imaging members, optics and mount frame.
        /// </summary>
        [McpServerTool(Name = "vision_read_sensor")]
        [Description("Reads a Vision sensor's identity (SensorId, RealityKind, Modality, manufacturer, " +
            "model, serial number, device URI, frame id), image members (Width, Height, PixelFormat, " +
            "ExposureTime, Gain, AcquisitionFrameRate, intrinsics) when it is an ImageSensorType, and the " +
            "mounted frame NodeId when declared via MountedOn. Use this after vision_list_sensors to obtain " +
            "the details needed for image-space work; use vision_read_extrinsic_calibration when you need " +
            "the camera-to-robot transform for a hand-eye lookup. Reports only what the server declares; " +
            "members the sensor does not carry come back as null. Returns a VisionSensorSnapshot.")]
        public static async Task<VisionSensorSnapshot> ReadSensorAsync(
            VisionClientAccessor accessor,
            [Description("Sensor NodeId, for example ns=2;s=Vision/Sensors/Camera1.")] string sensorNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionSensorClient sensor = accessor.OpenSensor(sensorNodeId, sessionName);
            VisionSensorIdentity identity = await sensor.ReadIdentityAsync(ct).ConfigureAwait(false);
            VisionImageSensorSnapshot? image = await sensor.ReadImageMembersAsync(ct).ConfigureAwait(false);
            VisionDepth3DSensorSnapshot? depth = await sensor.ReadDepthMembersAsync(ct).ConfigureAwait(false);
            VisionOpticsSnapshot? optics = await sensor.ReadOpticsAsync(ct).ConfigureAwait(false);
            VisionIlluminationSnapshot? illumination = await sensor.ReadIlluminationAsync(ct)
                .ConfigureAwait(false);
            NodeId mounted = await sensor.GetMountedFrameIdAsync(ct).ConfigureAwait(false);
            return new VisionSensorSnapshot
            {
                Identity = identity,
                Image = image,
                Depth = depth,
                Optics = optics,
                Illumination = illumination,
                MountedFrameId = mounted
            };
        }

        /// <summary>
        /// Reads an extrinsic calibration.
        /// </summary>
        [McpServerTool(Name = "vision_read_extrinsic_calibration")]
        [Description("Reads a Vision extrinsic-calibration snapshot: the camera-to-target 6-DoF transform, " +
            "mount arrangement (EyeInHand, EyeToHand, Fixed), source and target frame NodeIds, residual " +
            "error, method, and validity. Use this to obtain the hand-eye transform needed to convert " +
            "detection poses into robot base or tool-centre-point coordinates. Use vision_list_calibrations " +
            "first to enumerate the available calibration NodeIds. Reports only what the server declares; " +
            "an invalid calibration should be treated as unusable rather than substituting a default. " +
            "Returns a VisionExtrinsicCalibrationSnapshot.")]
        public static Task<VisionExtrinsicCalibrationSnapshot> ReadExtrinsicCalibrationAsync(
            VisionClientAccessor accessor,
            [Description("Sensor NodeId the calibration is attached to.")] string sensorNodeId,
            [Description("Extrinsic calibration NodeId.")] string calibrationNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionSensorClient sensor = accessor.OpenSensor(sensorNodeId, sessionName);
            NodeId parsed = Serialization.OpcUaJsonHelper.ParseNodeId(calibrationNodeId);
            return sensor.ReadExtrinsicCalibrationAsync(parsed, ct);
        }

        /// <summary>
        /// Reads an inference pipeline's current state.
        /// </summary>
        [McpServerTool(Name = "vision_read_pipeline")]
        [Description("Reads a Vision inference pipeline's live state: PipelineId, current EndpointState, " +
            "Continuous flag, bound Sensor NodeId, Deployment NodeId, and any LearningJob NodeId. Use this " +
            "before calling vision_run_inference or vision_start_continuous_inference to check the " +
            "pipeline is ready and to record its Deployment NodeId. When the Server also implements OPC UA " +
            "AI Model Management, the Deployment NodeId points at the AI Model Management deployment whose " +
            "InferenceLocation says where inference physically runs. Use vision_read_result instead when " +
            "you want a specific published result. Reports server state only, never infers pipeline state, " +
            "and never requests authority. Returns a VisionPipelineSnapshot.")]
        public static Task<VisionPipelineSnapshot> ReadPipelineAsync(
            VisionClientAccessor accessor,
            [Description("Pipeline NodeId, for example ns=2;s=Vision/Pipelines/BinPickingPipeline.")] string pipelineNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionPipelineClient pipeline = accessor.OpenPipeline(pipelineNodeId, sessionName);
            return pipeline.ReadAsync(ct);
        }

        /// <summary>
        /// Reads a Vision detection result.
        /// </summary>
        [McpServerTool(Name = "vision_read_detection_result")]
        [Description("Reads a Vision detection result (DetectionResultType) into its snapshot: ResultId, " +
            "CreationTime, sensor and pipeline NodeIds, ModelVersionUsed, the acquisition Frame image " +
            "reference, and the array of detections with class label, confidence, and 2D or 3D geometry. " +
            "Use this after vision_run_inference or when observing the pipeline's Results folder. Use " +
            "vision_read_inspection_result instead when the pipeline publishes an InspectionResultType, " +
            "and vision_read_segmentation_result for a SegmentationResultType. Reports server state only, " +
            "never fabricates detections. Returns a VisionDetectionResultSnapshot.")]
        public static Task<VisionDetectionResultSnapshot> ReadDetectionResultAsync(
            VisionClientAccessor accessor,
            [Description("Result NodeId of a DetectionResultType instance.")] string resultNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionResultReader reader = accessor.OpenResult(resultNodeId, sessionName);
            return reader.ReadDetectionAsync(ct);
        }

        /// <summary>
        /// Reads a Vision inspection result.
        /// </summary>
        [McpServerTool(Name = "vision_read_inspection_result")]
        [Description("Reads a Vision inspection result (InspectionResultType) into its snapshot: ResultId, " +
            "CreationTime, sensor and pipeline NodeIds, ModelVersionUsed, the acquisition Frame image " +
            "reference, overall Evaluation (Ok, NotOk, NotDecidable, Undefined), PartId, RecipeId, and the " +
            "measured characteristics. Use this when the pipeline publishes verdicts against tolerances. " +
            "Use vision_read_detection_result instead for DetectionResultType and " +
            "vision_read_segmentation_result for SegmentationResultType. Reports server state only. " +
            "Returns a VisionInspectionResultSnapshot.")]
        public static Task<VisionInspectionResultSnapshot> ReadInspectionResultAsync(
            VisionClientAccessor accessor,
            [Description("Result NodeId of an InspectionResultType instance.")] string resultNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionResultReader reader = accessor.OpenResult(resultNodeId, sessionName);
            return reader.ReadInspectionAsync(ct);
        }

        /// <summary>
        /// Reads a Vision segmentation result.
        /// </summary>
        [McpServerTool(Name = "vision_read_segmentation_result")]
        [Description("Reads a Vision segmentation result (SegmentationResultType) into its snapshot: " +
            "ResultId, CreationTime, sensor and pipeline NodeIds, the acquisition Frame image reference, " +
            "the label class names, and the mask image reference. Use this when the pipeline publishes " +
            "per-pixel segmentation labels. Use vision_read_detection_result for detections and " +
            "vision_read_inspection_result for verdicts. Reports server state only. Returns a " +
            "VisionSegmentationResultSnapshot.")]
        public static Task<VisionSegmentationResultSnapshot> ReadSegmentationResultAsync(
            VisionClientAccessor accessor,
            [Description("Result NodeId of a SegmentationResultType instance.")] string resultNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionResultReader reader = accessor.OpenResult(resultNodeId, sessionName);
            return reader.ReadSegmentationAsync(ct);
        }
    }

    /// <summary>
    /// Combined sensor snapshot returned by the vision_read_sensor MCP tool. It
    /// bundles identity, imaging members, depth members (when the sensor is a
    /// Depth3D sensor), optics, illumination, and the mounted frame NodeId so an
    /// agent gets everything a single round-trip in the tool call.
    /// </summary>
    public sealed record VisionSensorSnapshot
    {
        /// <summary>
        /// Sensor identity nameplate.
        /// </summary>
        public required VisionSensorIdentity Identity { get; init; }

        /// <summary>
        /// Imaging members when the sensor is an ImageSensorType, or null.
        /// </summary>
        public VisionImageSensorSnapshot? Image { get; init; }

        /// <summary>
        /// Depth members when the sensor is a Depth3DSensorType, or null.
        /// </summary>
        public VisionDepth3DSensorSnapshot? Depth { get; init; }

        /// <summary>
        /// Optics description when the sensor declares one, or null.
        /// </summary>
        public VisionOpticsSnapshot? Optics { get; init; }

        /// <summary>
        /// Illumination description when the sensor declares one, or null.
        /// </summary>
        public VisionIlluminationSnapshot? Illumination { get; init; }

        /// <summary>
        /// The NodeId of the frame the sensor is mounted on, or a null NodeId
        /// when the sensor does not declare a MountedOn frame.
        /// </summary>
        public NodeId MountedFrameId { get; init; } = NodeId.Null;
    }
}
