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
#if NET8_0_OR_GREATER
using System.Text.Json.Serialization;
#endif

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// The kind of result a Vision pipeline produced, determined from the type
    /// definition of the published result node.
    /// </summary>
#if NET8_0_OR_GREATER
    [JsonConverter(typeof(JsonStringEnumConverter<VisionResultKind>))]
#endif
    public enum VisionResultKind
    {
        /// <summary>
        /// The result node could not be resolved or its type definition could
        /// not be determined.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// A <c>DetectionResultType</c> (§7.3) — bounding boxes and poses.
        /// </summary>
        Detection = 1,

        /// <summary>
        /// An <c>InspectionResultType</c> (§7.2) — pass/fail verdicts.
        /// </summary>
        Inspection = 2,

        /// <summary>
        /// A <c>SegmentationResultType</c> (§7.4) — per-pixel labels.
        /// </summary>
        Segmentation = 3
    }

    /// <summary>
    /// The kind of result expected from a Vision inference request.
    /// </summary>
#if NET8_0_OR_GREATER
    [JsonConverter(typeof(JsonStringEnumConverter<VisionExpectedResultKind>))]
#endif
    public enum VisionExpectedResultKind
    {
        /// <summary>
        /// Accept any result kind.
        /// </summary>
        Auto = 0,

        /// <summary>
        /// Require a <c>DetectionResultType</c> result.
        /// </summary>
        Detection = 1,

        /// <summary>
        /// Require an <c>InspectionResultType</c> result.
        /// </summary>
        Inspection = 2,

        /// <summary>
        /// Require a <c>SegmentationResultType</c> result.
        /// </summary>
        Segmentation = 3
    }

    /// <summary>
    /// Controls how much detail the inference summary contains.
    /// </summary>
#if NET8_0_OR_GREATER
    [JsonConverter(typeof(JsonStringEnumConverter<VisionResultDetail>))]
#endif
    public enum VisionResultDetail
    {
        /// <summary>
        /// Return a concise typed summary including bounded items.
        /// </summary>
        Summary = 0,

        /// <summary>
        /// Return only the handle (resultId, resultNodeId, resolved, kind) with
        /// no payload read.
        /// </summary>
        HandleOnly = 1
    }

    /// <summary>
    /// Concise detection summary returned when the result is a
    /// <c>DetectionResultType</c>.
    /// </summary>
    public sealed record VisionDetectionSummary
    {
        /// <summary>
        /// Result creation time.
        /// </summary>
        public DateTimeUtc CreationTime { get; init; }

        /// <summary>
        /// Model version used, when reported.
        /// </summary>
        public string? ModelVersionUsed { get; init; }

        /// <summary>
        /// Frame identifier the poses are expressed in.
        /// </summary>
        public string? FrameId { get; init; }

        /// <summary>
        /// Total number of detections in the result.
        /// </summary>
        public int TotalDetections { get; init; }

        /// <summary>
        /// Bounded subset of detection items.
        /// </summary>
        public ArrayOf<VisionDetectionItem> Items { get; init; }
    }

    /// <summary>
    /// One detection in a concise summary.
    /// </summary>
    public sealed record VisionDetectionItem
    {
        /// <summary>
        /// Detection identifier.
        /// </summary>
        public string DetectionId { get; init; } = string.Empty;

        /// <summary>
        /// Class label.
        /// </summary>
        public string ClassLabel { get; init; } = string.Empty;

        /// <summary>
        /// Class numeric identifier.
        /// </summary>
        public uint ClassId { get; init; }

        /// <summary>
        /// Confidence score.
        /// </summary>
        public double Confidence { get; init; }

        /// <summary>
        /// Whether the detection has a 3-D pose.
        /// </summary>
        public bool HasPose { get; init; }

        /// <summary>
        /// Concise pose (position + orientation) when available and small.
        /// </summary>
        public VisionPose3DDataType? Pose { get; init; }
    }

    /// <summary>
    /// Concise inspection summary returned when the result is an
    /// <c>InspectionResultType</c>.
    /// </summary>
    public sealed record VisionInspectionSummary
    {
        /// <summary>
        /// Result creation time.
        /// </summary>
        public DateTimeUtc CreationTime { get; init; }

        /// <summary>
        /// Overall evaluation.
        /// </summary>
        public VisionResultEvaluationEnum Evaluation { get; init; }

        /// <summary>
        /// Part identifier, when reported.
        /// </summary>
        public string? PartId { get; init; }

        /// <summary>
        /// Recipe identifier, when reported.
        /// </summary>
        public string? RecipeId { get; init; }

        /// <summary>
        /// Total number of characteristics.
        /// </summary>
        public int TotalCharacteristics { get; init; }

        /// <summary>
        /// Bounded subset of characteristic items.
        /// </summary>
        public ArrayOf<VisionCharacteristicItem> Items { get; init; }
    }

    /// <summary>
    /// One characteristic in a concise inspection summary.
    /// </summary>
    public sealed record VisionCharacteristicItem
    {
        /// <summary>
        /// Characteristic name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Tolerance status.
        /// </summary>
        public VisionToleranceStatusEnum Status { get; init; }

        /// <summary>
        /// Deviation from nominal.
        /// </summary>
        public double Deviation { get; init; }
    }

    /// <summary>
    /// Concise segmentation summary returned when the result is a
    /// <c>SegmentationResultType</c>.
    /// </summary>
    public sealed record VisionSegmentationSummary
    {
        /// <summary>
        /// Result creation time.
        /// </summary>
        public DateTimeUtc CreationTime { get; init; }

        /// <summary>
        /// Label class names.
        /// </summary>
        public ArrayOf<string> LabelClasses { get; init; }

        /// <summary>
        /// Mask image width, when reported.
        /// </summary>
        public uint MaskWidth { get; init; }

        /// <summary>
        /// Mask image height, when reported.
        /// </summary>
        public uint MaskHeight { get; init; }

        /// <summary>
        /// Mask image format name, when reported.
        /// </summary>
        public string? MaskFormat { get; init; }
    }

    /// <summary>
    /// The complete result of a one-shot inference execution with optional
    /// concise summary. Reusable from any consumer — MCP tools, Robotics,
    /// or direct application code.
    /// </summary>
    public sealed record VisionInferenceResult
    {
        /// <summary>
        /// The ResultId the server assigned.
        /// </summary>
        public required string ResultId { get; init; }

        /// <summary>
        /// The NodeId the result was published at, or a null NodeId.
        /// </summary>
        public NodeId ResultNodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// Whether the result node was resolved and is addressable.
        /// </summary>
        public bool Resolved { get; init; }

        /// <summary>
        /// The Pipeline NodeId requested to run this inference.
        /// </summary>
        public required NodeId RequestedPipelineNodeId { get; init; }

        /// <summary>
        /// The requested pipeline's published name (BrowseName.Name), when available.
        /// </summary>
        public string? RequestedPipelineName { get; init; }

        /// <summary>
        /// The Pipeline NodeId published by the result, when available.
        /// </summary>
        public NodeId PipelineId { get; init; } = NodeId.Null;

        /// <summary>
        /// The sensor NodeId that produced the frame, when available.
        /// </summary>
        public NodeId SensorId { get; init; } = NodeId.Null;

        /// <summary>
        /// The model version used to compute the result, when reported.
        /// </summary>
        public string? ModelVersionUsed { get; init; }

        /// <summary>
        /// The result creation time, when available.
        /// </summary>
        public DateTimeUtc CreationTime { get; init; }

        /// <summary>
        /// The frame identifier detection poses are expressed in, when available.
        /// </summary>
        public string? FrameId { get; init; }

        /// <summary>
        /// The detected result kind, determined from the type definition.
        /// </summary>
        public VisionResultKind ResultKind { get; init; }

        /// <summary>
        /// Concise detection summary, populated when <see cref="ResultKind"/>
        /// is <see cref="VisionResultKind.Detection"/> and detail is
        /// <see cref="VisionResultDetail.Summary"/>.
        /// </summary>
        public VisionDetectionSummary? DetectionSummary { get; init; }

        /// <summary>
        /// Concise inspection summary, populated when <see cref="ResultKind"/>
        /// is <see cref="VisionResultKind.Inspection"/> and detail is
        /// <see cref="VisionResultDetail.Summary"/>.
        /// </summary>
        public VisionInspectionSummary? InspectionSummary { get; init; }

        /// <summary>
        /// Concise segmentation summary, populated when <see cref="ResultKind"/>
        /// is <see cref="VisionResultKind.Segmentation"/> and detail is
        /// <see cref="VisionResultDetail.Summary"/>.
        /// </summary>
        public VisionSegmentationSummary? SegmentationSummary { get; init; }
    }

    /// <summary>
    /// Reusable service that runs one-shot inference on a pipeline, resolves the
    /// published result's type definition, and optionally reads a bounded concise
    /// summary. Consumable from MCP tools, Robotics pick-and-place, or direct
    /// application code without coupling to any tool framework.
    /// </summary>
    public sealed class VisionInferenceService
    {
        private readonly VisionClientOperations m_operations;

        internal VisionInferenceService(VisionClientOperations operations)
        {
            m_operations = operations
                ?? throw new ArgumentNullException(nameof(operations));
        }

        /// <summary>
        /// Runs a one-shot inference, resolves the result, determines its type,
        /// and optionally builds a concise summary.
        /// </summary>
        /// <param name="pipeline">
        /// The pipeline client to run inference on.
        /// </param>
        /// <param name="pipelineName">
        /// Optional pipeline display/browse name for provenance in the result.
        /// </param>
        /// <param name="detail">
        /// Whether to read a summary or return handle-only.
        /// </param>
        /// <param name="expectedKind">
        /// When set to a concrete kind, the service throws if the result kind
        /// cannot be determined or does not match. Use
        /// <see cref="VisionExpectedResultKind.Auto"/> to accept any kind.
        /// Unresolved results always return handle-only without enforcement.
        /// </param>
        /// <param name="maxItems">
        /// Maximum number of items (detections/characteristics) in the summary.
        /// Must be between 0 and 100 inclusive.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionInferenceResult> RunOneShotAsync(
            VisionPipelineClient pipeline,
            string? pipelineName,
            VisionResultDetail detail,
            VisionExpectedResultKind expectedKind,
            int maxItems,
            CancellationToken cancellationToken = default)
        {
            if (pipeline is null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            ValidateRequest(detail, expectedKind, maxItems);

            string resultId = await pipeline.RunInferenceAsync(
                timestamp: default, cancellationToken).ConfigureAwait(false);
            NodeId resultNodeId = await pipeline.ResolveResultNodeIdAsync(
                resultId, cancellationToken).ConfigureAwait(false);
            bool resolved = !resultNodeId.IsNull;

            if (!resolved)
            {
                return new VisionInferenceResult
                {
                    ResultId = resultId,
                    ResultNodeId = resultNodeId,
                    Resolved = false,
                    RequestedPipelineNodeId = pipeline.PipelineNodeId,
                    RequestedPipelineName = pipelineName,
                    ResultKind = VisionResultKind.Unknown
                };
            }

            VisionResultKind kind = await DetermineResultKindAsync(
                resultNodeId, cancellationToken).ConfigureAwait(false);

            if (expectedKind != VisionExpectedResultKind.Auto &&
                kind == VisionResultKind.Unknown)
            {
                throw new InvalidOperationException(
                    $"Cannot determine result kind for resolved result node '{resultNodeId}' " +
                    $"while expectedKind is '{expectedKind}'.");
            }

            if (expectedKind != VisionExpectedResultKind.Auto &&
                kind != (VisionResultKind)expectedKind)
            {
                throw new InvalidOperationException(
                    $"Expected result kind '{expectedKind}' but the pipeline produced '{kind}'.");
            }

            if (detail == VisionResultDetail.HandleOnly)
            {
                return new VisionInferenceResult
                {
                    ResultId = resultId,
                    ResultNodeId = resultNodeId,
                    Resolved = true,
                    RequestedPipelineNodeId = pipeline.PipelineNodeId,
                    RequestedPipelineName = pipelineName,
                    ResultKind = kind
                };
            }

            var reader = new VisionResultReader(m_operations, resultNodeId);
            return kind switch
            {
                VisionResultKind.Detection => await BuildDetectionResultAsync(
                    reader, resultId, resultNodeId, pipeline, pipelineName, maxItems,
                    cancellationToken).ConfigureAwait(false),
                VisionResultKind.Inspection => await BuildInspectionResultAsync(
                    reader, resultId, resultNodeId, pipeline, pipelineName, maxItems,
                    cancellationToken).ConfigureAwait(false),
                VisionResultKind.Segmentation => await BuildSegmentationResultAsync(
                    reader, resultId, resultNodeId, pipeline, pipelineName,
                    cancellationToken).ConfigureAwait(false),
                _ => new VisionInferenceResult
                {
                    ResultId = resultId,
                    ResultNodeId = resultNodeId,
                    Resolved = true,
                    RequestedPipelineNodeId = pipeline.PipelineNodeId,
                    RequestedPipelineName = pipelineName,
                    ResultKind = kind
                }
            };
        }

        private static void ValidateRequest(
            VisionResultDetail detail,
            VisionExpectedResultKind expectedKind,
            int maxItems)
        {
            if (!IsDefined(detail))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(detail),
                    detail,
                    "Invalid detail value.");
            }

            if (!IsDefined(expectedKind))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expectedKind),
                    expectedKind,
                    "Invalid expectedKind value.");
            }

            if (maxItems < 0 || maxItems > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxItems),
                    maxItems,
                    "maxItems must be between 0 and 100 inclusive.");
            }
        }

        private static bool IsDefined(VisionResultDetail detail)
        {
#if NET8_0_OR_GREATER
            return Enum.IsDefined(detail);
#else
            return Enum.IsDefined(typeof(VisionResultDetail), detail);
#endif
        }

        private static bool IsDefined(VisionExpectedResultKind expectedKind)
        {
#if NET8_0_OR_GREATER
            return Enum.IsDefined(expectedKind);
#else
            return Enum.IsDefined(typeof(VisionExpectedResultKind), expectedKind);
#endif
        }

        /// <summary>
        /// Determines the <see cref="VisionResultKind"/> from the type definition
        /// of a resolved result node. Handles zero, one, and multiple
        /// <c>HasTypeDefinition</c> references deterministically.
        /// </summary>
        public async Task<VisionResultKind> DetermineResultKindAsync(
            NodeId resultNodeId,
            CancellationToken cancellationToken = default)
        {
            if (resultNodeId.IsNull)
            {
                return VisionResultKind.Unknown;
            }

            NodeId detectionType = m_operations.VisionNamespaceType(
                ObjectTypes.DetectionResultType);
            NodeId inspectionType = m_operations.VisionNamespaceType(
                ObjectTypes.InspectionResultType);
            NodeId segmentationType = m_operations.VisionNamespaceType(
                ObjectTypes.SegmentationResultType);

            ArrayOf<ReferenceDescription> refs = await m_operations.BrowseAsync(
                resultNodeId,
                Opc.Ua.ReferenceTypeIds.HasTypeDefinition,
                BrowseDirection.Forward,
                (uint)NodeClass.ObjectType,
                cancellationToken).ConfigureAwait(false);

            if (refs.Count == 0)
            {
                return VisionResultKind.Unknown;
            }

            if (refs.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Result node '{resultNodeId}' has {refs.Count} HasTypeDefinition references; " +
                    $"expected exactly one. First: '{refs[0].NodeId}', second: '{refs[1].NodeId}'.");
            }

            NodeId typeDef = ExpandedNodeId.ToNodeId(
                refs[0].NodeId, m_operations.Session.NamespaceUris);
            if (typeDef.IsNull)
            {
                return VisionResultKind.Unknown;
            }

            if (!detectionType.IsNull && await m_operations.Session.NodeCache
                    .IsTypeOfAsync(typeDef, detectionType, cancellationToken)
                    .ConfigureAwait(false))
            {
                return VisionResultKind.Detection;
            }
            if (!inspectionType.IsNull && await m_operations.Session.NodeCache
                    .IsTypeOfAsync(typeDef, inspectionType, cancellationToken)
                    .ConfigureAwait(false))
            {
                return VisionResultKind.Inspection;
            }
            if (!segmentationType.IsNull && await m_operations.Session.NodeCache
                    .IsTypeOfAsync(typeDef, segmentationType, cancellationToken)
                    .ConfigureAwait(false))
            {
                return VisionResultKind.Segmentation;
            }

            return VisionResultKind.Unknown;
        }

        private static async Task<VisionInferenceResult> BuildDetectionResultAsync(
            VisionResultReader reader, string resultId, NodeId resultNodeId,
            VisionPipelineClient pipeline, string? pipelineName, int maxItems,
            CancellationToken ct)
        {
            VisionDetectionResultSnapshot snap =
                await reader.ReadDetectionAsync(ct).ConfigureAwait(false);
            int total = snap.Detections.Count;
            int take = Math.Min(total, maxItems);
            var items = new List<VisionDetectionItem>(take);
            for (int i = 0; i < take; i++)
            {
                VisionDetectionDataType d = snap.Detections[i];
                items.Add(new VisionDetectionItem
                {
                    DetectionId = d.DetectionId ?? string.Empty,
                    ClassLabel = d.ClassLabel ?? string.Empty,
                    ClassId = d.ClassId,
                    Confidence = d.Confidence,
                    HasPose = d.HasPose,
                    Pose = d.HasPose ? d.Pose : null
                });
            }
            return new VisionInferenceResult
            {
                ResultId = resultId,
                ResultNodeId = resultNodeId,
                Resolved = true,
                RequestedPipelineNodeId = pipeline.PipelineNodeId,
                RequestedPipelineName = pipelineName,
                PipelineId = snap.PipelineId,
                SensorId = snap.SensorId,
                ModelVersionUsed = snap.ModelVersionUsed,
                CreationTime = snap.CreationTime,
                FrameId = snap.FrameId,
                ResultKind = VisionResultKind.Detection,
                DetectionSummary = new VisionDetectionSummary
                {
                    CreationTime = snap.CreationTime,
                    ModelVersionUsed = snap.ModelVersionUsed,
                    FrameId = snap.FrameId,
                    TotalDetections = total,
                    Items = items.ToArrayOf()
                }
            };
        }

        private static async Task<VisionInferenceResult> BuildInspectionResultAsync(
            VisionResultReader reader, string resultId, NodeId resultNodeId,
            VisionPipelineClient pipeline, string? pipelineName, int maxItems,
            CancellationToken ct)
        {
            VisionInspectionResultSnapshot snap =
                await reader.ReadInspectionAsync(ct).ConfigureAwait(false);
            int total = snap.Characteristics.Count;
            int take = Math.Min(total, maxItems);
            var items = new List<VisionCharacteristicItem>(take);
            for (int i = 0; i < take; i++)
            {
                VisionCharacteristicDataType c = snap.Characteristics[i];
                items.Add(new VisionCharacteristicItem
                {
                    Name = c.Name ?? string.Empty,
                    Status = c.Status,
                    Deviation = c.Deviation
                });
            }
            return new VisionInferenceResult
            {
                ResultId = resultId,
                ResultNodeId = resultNodeId,
                Resolved = true,
                RequestedPipelineNodeId = pipeline.PipelineNodeId,
                RequestedPipelineName = pipelineName,
                PipelineId = snap.PipelineId,
                SensorId = snap.SensorId,
                ModelVersionUsed = snap.ModelVersionUsed,
                CreationTime = snap.CreationTime,
                ResultKind = VisionResultKind.Inspection,
                InspectionSummary = new VisionInspectionSummary
                {
                    CreationTime = snap.CreationTime,
                    Evaluation = snap.Evaluation,
                    PartId = snap.PartId,
                    RecipeId = snap.RecipeId,
                    TotalCharacteristics = total,
                    Items = items.ToArrayOf()
                }
            };
        }

        private static async Task<VisionInferenceResult> BuildSegmentationResultAsync(
            VisionResultReader reader, string resultId, NodeId resultNodeId,
            VisionPipelineClient pipeline, string? pipelineName,
            CancellationToken ct)
        {
            VisionSegmentationResultSnapshot snap =
                await reader.ReadSegmentationAsync(ct).ConfigureAwait(false);
            return new VisionInferenceResult
            {
                ResultId = resultId,
                ResultNodeId = resultNodeId,
                Resolved = true,
                RequestedPipelineNodeId = pipeline.PipelineNodeId,
                RequestedPipelineName = pipelineName,
                PipelineId = snap.PipelineId,
                SensorId = snap.SensorId,
                CreationTime = snap.CreationTime,
                ResultKind = VisionResultKind.Segmentation,
                SegmentationSummary = new VisionSegmentationSummary
                {
                    CreationTime = snap.CreationTime,
                    LabelClasses = snap.LabelClasses,
                    MaskWidth = snap.Mask?.Width ?? 0,
                    MaskHeight = snap.Mask?.Height ?? 0,
                    MaskFormat = snap.Mask?.Format.ToString()
                }
            };
        }
    }
}
