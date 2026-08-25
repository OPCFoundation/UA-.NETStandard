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

using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Mcp.Tools
{
    // CLR arrays are intentional on these MCP result DTOs: ArrayOf<T> serializes
    // its backing memory object instead of the JSON array agents need. The Vision
    // client service keeps ArrayOf<T>; this boundary projects it for MCP only.

    /// <summary>
    /// MCP tools for driving Vision inference pipelines.
    /// </summary>
    [McpServerToolType]
    public sealed class VisionInferenceTools
    {
        /// <summary>
        /// Runs a single one-shot inference on a Vision pipeline with structured
        /// result. Accepts a pipeline selector (name or NodeId), expected result
        /// kind, detail level, and bounded items.
        /// </summary>
        [McpServerTool(Name = "vision_run_inference")]
        [Description("Invokes RunInference on a Vision pipeline for a single acquisition. Accepts a " +
            "pipeline selector (exact BrowseName, DisplayName, or NodeId string) so you do not need to " +
            "discover a NodeId before calling. Resolves the result, determines its kind (detection, " +
            "inspection, segmentation), and optionally reads a bounded concise summary. Set detail to " +
            "HandleOnly to return a result handle without reading the result payload. Set expectedKind " +
            "to enforce the produced result kind. Reports the server's refusal honestly; never retries " +
            "silently and never adjusts the pipeline configuration.")]
        public static async Task<VisionInferenceRunResult> RunInferenceAsync(
            VisionClientAccessor accessor,
            [Description("The one-shot inference request.")] VisionInferenceRequest request,
            CancellationToken ct = default)
        {
            System.ArgumentNullException.ThrowIfNull(request);
            ValidateRequest(request);
            System.ArgumentNullException.ThrowIfNull(accessor);

            (VisionNodeEntry entry, VisionPipelineClient pipelineClient) =
                await accessor.ResolvePipelineAsync(
                    request.Pipeline, request.SessionName, ct)
                    .ConfigureAwait(false);

            VisionInferenceService service = accessor.CreateInferenceService(request.SessionName);
            VisionInferenceResult result = await service.RunOneShotAsync(
                pipelineClient,
                entry.BrowseName.Name,
                request.Detail,
                request.ExpectedKind,
                request.MaxItems,
                ct).ConfigureAwait(false);

            return VisionInferenceRunResult.FromServiceResult(result);
        }

        /// <summary>
        /// Starts continuous inference on a Vision pipeline.
        /// </summary>
        [McpServerTool(Name = "vision_start_continuous_inference")]
        [Description("Invokes StartContinuous on a Vision pipeline so the server acquires and publishes " +
            "results repeatedly. Use vision_run_inference for a single one-shot invocation. Use " +
            "vision_stop_inference to halt the loop. Reports the server's refusal honestly if the " +
            "pipeline is not runnable; never retries silently and never asks the server to change mode. " +
            "Returns no value on success.")]
        public static async Task StartContinuousInferenceAsync(
            VisionClientAccessor accessor,
            [Description("Pipeline selector: an exact BrowseName, DisplayName, or NodeId string.")] string pipeline,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            (_, VisionPipelineClient pipelineClient) =
                await accessor.ResolvePipelineAsync(pipeline, sessionName, ct)
                    .ConfigureAwait(false);
            await pipelineClient.StartContinuousAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops continuous or in-progress inference on a Vision pipeline.
        /// </summary>
        [McpServerTool(Name = "vision_stop_inference")]
        [Description("Invokes Stop on a Vision pipeline to halt continuous or in-progress inference. Use " +
            "vision_start_continuous_inference to start it again. Use vision_run_inference to run a " +
            "single pass instead. Reports the server's refusal honestly if the pipeline cannot be " +
            "stopped in its current state; never retries silently. Returns no value on success.")]
        public static async Task StopInferenceAsync(
            VisionClientAccessor accessor,
            [Description("Pipeline selector: an exact BrowseName, DisplayName, or NodeId string.")] string pipeline,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            (_, VisionPipelineClient pipelineClient) =
                await accessor.ResolvePipelineAsync(pipeline, sessionName, ct)
                    .ConfigureAwait(false);
            await pipelineClient.StopAsync(ct).ConfigureAwait(false);
        }

        private static void ValidateRequest(VisionInferenceRequest request)
        {
            System.ArgumentException.ThrowIfNullOrWhiteSpace(request.Pipeline);

            if (!System.Enum.IsDefined(request.ExpectedKind))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(request),
                    request.ExpectedKind,
                    "Invalid expectedKind value.");
            }

            if (!System.Enum.IsDefined(request.Detail))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(request),
                    request.Detail,
                    "Invalid detail value.");
            }

            if (request.MaxItems < 0 || request.MaxItems > 100)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(request),
                    request.MaxItems,
                    "maxItems must be between 0 and 100 inclusive.");
            }
        }
    }

    /// <summary>
    /// Structured input for <c>vision_run_inference</c>.
    /// </summary>
    public sealed record VisionInferenceRequest
    {
        /// <summary>
        /// Exact pipeline BrowseName, DisplayName, or NodeId selector.
        /// </summary>
        [Description("Pipeline selector: an exact BrowseName, DisplayName, or NodeId string " +
            "(e.g. 'BinPickingPipeline' or 'ns=2;s=Vision/Pipelines/BinPickingPipeline').")]
        public required string Pipeline { get; init; }

        /// <summary>
        /// Expected result kind. Auto accepts any recognized result kind.
        /// </summary>
        [Description("Expected result kind: Auto (default) accepts any kind; Detection, Inspection, " +
            "or Segmentation requires a matching result.")]
        [DefaultValue(VisionExpectedResultKind.Auto)]
        public VisionExpectedResultKind ExpectedKind { get; init; } = VisionExpectedResultKind.Auto;

        /// <summary>
        /// Requested payload detail. Summary reads a concise bounded response.
        /// </summary>
        [Description("Detail level: Summary (default) reads a concise bounded summary; HandleOnly " +
            "returns only the result handle.")]
        [DefaultValue(VisionResultDetail.Summary)]
        public VisionResultDetail Detail { get; init; } = VisionResultDetail.Summary;

        /// <summary>
        /// Maximum detection or inspection items in a concise summary.
        /// </summary>
        [Range(0, 100)]
        [Description("Maximum number of detections or characteristics in the concise summary. " +
            "Must be between 0 and 100 inclusive. Default 20.")]
        [DefaultValue(20)]
        public int MaxItems { get; init; } = 20;

        /// <summary>
        /// Session name to use; defaults to the only active session.
        /// </summary>
        [Description("Session name to use; defaults to the only active session.")]
        public string? SessionName { get; init; }
    }

    /// <summary>
    /// Structured result of a single inference run, including handle information and
    /// an optional concise summary. Returned by the vision_run_inference tool.
    /// </summary>
    public sealed record VisionInferenceRunResult
    {
        /// <summary>
        /// The ResultId the Server assigned to this run.
        /// </summary>
        public required string ResultId { get; init; }

        /// <summary>
        /// The NodeId the Server published the result at, which is what the
        /// vision_read_*_result tools take. Empty when it could not be resolved.
        /// </summary>
        public required string ResultNodeId { get; init; }

        /// <summary>
        /// True when <see cref="ResultNodeId"/> is usable.
        /// </summary>
        public bool Resolved { get; init; }

        /// <summary>
        /// The detected result kind.
        /// </summary>
        public VisionResultKind ResultKind { get; init; }

        /// <summary>
        /// The requested pipeline's published name, when available.
        /// </summary>
        public string? RequestedPipelineName { get; init; }

        /// <summary>
        /// The Pipeline NodeId requested to run this inference.
        /// </summary>
        public string? RequestedPipelineNodeId { get; init; }

        /// <summary>
        /// The Pipeline NodeId published by the result, when available.
        /// </summary>
        public string? PipelineId { get; init; }

        /// <summary>
        /// The sensor NodeId that produced the frame, when available.
        /// </summary>
        public string? SensorId { get; init; }

        /// <summary>
        /// The model version used, when reported.
        /// </summary>
        public string? ModelVersionUsed { get; init; }

        /// <summary>
        /// Result creation time (ISO 8601), when available.
        /// </summary>
        public string? CreationTime { get; init; }

        /// <summary>
        /// Frame identifier the poses are expressed in, when available.
        /// </summary>
        public string? FrameId { get; init; }

        /// <summary>
        /// Detection summary, populated when resultKind is Detection and detail
        /// was Summary.
        /// </summary>
        public VisionInferenceDetectionSummary? Detection { get; init; }

        /// <summary>
        /// Inspection summary, populated when resultKind is Inspection and detail
        /// was Summary.
        /// </summary>
        public VisionInferenceInspectionSummary? Inspection { get; init; }

        /// <summary>
        /// Segmentation summary, populated when resultKind is Segmentation and
        /// detail was Summary.
        /// </summary>
        public VisionInferenceSegmentationSummary? Segmentation { get; init; }

        /// <summary>
        /// Creates a run result from the service-level result.
        /// </summary>
        internal static VisionInferenceRunResult FromServiceResult(VisionInferenceResult r)
        {
            return new VisionInferenceRunResult
            {
                ResultId = r.ResultId,
                ResultNodeId = r.ResultNodeId.IsNull ? string.Empty : r.ResultNodeId.ToString(),
                Resolved = r.Resolved,
                ResultKind = r.ResultKind,
                RequestedPipelineName = r.RequestedPipelineName,
                RequestedPipelineNodeId = r.RequestedPipelineNodeId.IsNull
                    ? null
                    : r.RequestedPipelineNodeId.ToString(),
                PipelineId = r.PipelineId.IsNull ? null : r.PipelineId.ToString(),
                SensorId = r.SensorId.IsNull ? null : r.SensorId.ToString(),
                ModelVersionUsed = r.ModelVersionUsed,
                CreationTime = r.CreationTime == default
                    ? null
                    : r.CreationTime.ToString("o", CultureInfo.InvariantCulture),
                FrameId = r.FrameId,
                Detection = ToDetectionSummary(r.DetectionSummary),
                Inspection = ToInspectionSummary(r.InspectionSummary),
                Segmentation = ToSegmentationSummary(r.SegmentationSummary)
            };
        }

        private static VisionInferenceDetectionSummary? ToDetectionSummary(
            VisionDetectionSummary? summary)
        {
            if (summary is null)
            {
                return null;
            }

            var items = new List<VisionInferenceDetectionItem>(summary.Items.Count);
            for (int i = 0; i < summary.Items.Count; i++)
            {
                VisionDetectionItem item = summary.Items[i];
                items.Add(new VisionInferenceDetectionItem
                {
                    DetectionId = item.DetectionId,
                    ClassLabel = item.ClassLabel,
                    ClassId = item.ClassId,
                    Confidence = item.Confidence,
                    HasPose = item.HasPose
                });
            }

            return new VisionInferenceDetectionSummary
            {
                TotalDetections = summary.TotalDetections,
                Items = items.ToArray()
            };
        }

        private static VisionInferenceInspectionSummary? ToInspectionSummary(
            VisionInspectionSummary? summary)
        {
            if (summary is null)
            {
                return null;
            }

            var items = new List<VisionInferenceCharacteristicSummary>(summary.Items.Count);
            for (int i = 0; i < summary.Items.Count; i++)
            {
                VisionCharacteristicItem item = summary.Items[i];
                items.Add(new VisionInferenceCharacteristicSummary
                {
                    Name = item.Name,
                    Status = item.Status,
                    Deviation = item.Deviation
                });
            }

            return new VisionInferenceInspectionSummary
            {
                Evaluation = summary.Evaluation,
                PartId = summary.PartId,
                RecipeId = summary.RecipeId,
                TotalCharacteristics = summary.TotalCharacteristics,
                Items = items.ToArray()
            };
        }

        private static VisionInferenceSegmentationSummary? ToSegmentationSummary(
            VisionSegmentationSummary? summary)
        {
            if (summary is null)
            {
                return null;
            }

            return new VisionInferenceSegmentationSummary
            {
                LabelClasses = summary.LabelClasses.ToArray() ?? [],
                MaskWidth = summary.MaskWidth,
                MaskHeight = summary.MaskHeight,
                MaskFormat = summary.MaskFormat
            };
        }
    }

    /// <summary>
    /// Lean detection summary returned by <c>vision_run_inference</c>.
    /// </summary>
    public sealed record VisionInferenceDetectionSummary
    {
        /// <summary>
        /// Total number of detections in the published result.
        /// </summary>
        public int TotalDetections { get; init; }

        /// <summary>
        /// Bounded detection items without geometry payloads.
        /// </summary>
        public VisionInferenceDetectionItem[] Items { get; init; } = [];
    }

    /// <summary>
    /// Lean detection item returned by <c>vision_run_inference</c>.
    /// </summary>
    public sealed record VisionInferenceDetectionItem
    {
        /// <summary>
        /// Detection identifier.
        /// </summary>
        public string DetectionId { get; init; } = string.Empty;

        /// <summary>
        /// Detection class label.
        /// </summary>
        public string ClassLabel { get; init; } = string.Empty;

        /// <summary>
        /// Detection class identifier.
        /// </summary>
        public uint ClassId { get; init; }

        /// <summary>
        /// Detection confidence.
        /// </summary>
        public double Confidence { get; init; }

        /// <summary>
        /// Whether the source detection has a pose.
        /// </summary>
        public bool HasPose { get; init; }
    }

    /// <summary>
    /// Lean inspection summary returned by <c>vision_run_inference</c>.
    /// </summary>
    public sealed record VisionInferenceInspectionSummary
    {
        /// <summary>
        /// Overall inspection evaluation.
        /// </summary>
        public Vision.VisionResultEvaluationEnum Evaluation { get; init; }

        /// <summary>
        /// Inspected part identifier, when reported.
        /// </summary>
        public string? PartId { get; init; }

        /// <summary>
        /// Inspection recipe identifier, when reported.
        /// </summary>
        public string? RecipeId { get; init; }

        /// <summary>
        /// Total number of characteristics in the result.
        /// </summary>
        public int TotalCharacteristics { get; init; }

        /// <summary>
        /// Bounded characteristic summaries.
        /// </summary>
        public VisionInferenceCharacteristicSummary[] Items { get; init; } = [];
    }

    /// <summary>
    /// Lean characteristic summary returned by <c>vision_run_inference</c>.
    /// </summary>
    public sealed record VisionInferenceCharacteristicSummary
    {
        /// <summary>
        /// Characteristic name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Tolerance status.
        /// </summary>
        public Vision.VisionToleranceStatusEnum Status { get; init; }

        /// <summary>
        /// Deviation from the nominal value.
        /// </summary>
        public double Deviation { get; init; }
    }

    /// <summary>
    /// Lean segmentation summary returned by <c>vision_run_inference</c>.
    /// </summary>
    public sealed record VisionInferenceSegmentationSummary
    {
        /// <summary>
        /// Label classes associated with the mask.
        /// </summary>
        public string[] LabelClasses { get; init; } = [];

        /// <summary>
        /// Mask width, when reported.
        /// </summary>
        public uint MaskWidth { get; init; }

        /// <summary>
        /// Mask height, when reported.
        /// </summary>
        public uint MaskHeight { get; init; }

        /// <summary>
        /// Mask format, when reported.
        /// </summary>
        public string? MaskFormat { get; init; }
    }

}
