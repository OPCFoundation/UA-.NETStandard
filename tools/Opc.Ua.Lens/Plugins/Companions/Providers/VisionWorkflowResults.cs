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
using Opc.Ua;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace UaLens.Plugins.Companions.Providers
{
    internal static class VisionWorkflowResults
    {
        public static async Task<NodeId> FindAsync(
            CompanionContext context, NodeId pipeline, string resultId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(resultId) || resultId.Length > 256)
            {
                throw new ArgumentException("A bounded, exact Vision result ID is required.", nameof(resultId));
            }
            NodeId folder = await IndustrialCompanionAccess.ResolveChildAsync(
                context, pipeline, Opc.Ua.Vision.Namespaces.Vision, "Results", false, cancellationToken)
                .ConfigureAwait(false);
            NodeId found = NodeId.Null;
            int count = 0;
            var budget = new IndustrialBrowseBudget(context);
            await foreach (ReferenceDescription reference in IndustrialCompanionAccess.BrowseAsync(
                context, folder, BrowseDirection.Forward, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                NodeClass.Object, budget, cancellationToken).ConfigureAwait(false))
            {
                CellCompanionSupport.CheckCount(++count, Math.Min(context.MaxTargets, 32), "Vision result candidates");
                NodeId node = IndustrialCompanionAccess.LocalId(context, reference.NodeId);
                if (node.IsNull ||
                    await IndustrialCompanionAccess.ClassifyAsync(
                        context, reference.TypeDefinition, sResults, budget, cancellationToken)
                        .ConfigureAwait(false) is null)
                {
                    continue;
                }
                ArrayOf<CompanionValue> identity = await VisionWorkflowAccess.PropertiesAsync(
                    context, node, ["ResultId", "Pipeline"], cancellationToken).ConfigureAwait(false);
                if (VisionWorkflowAccess.Text(identity[0].Value) != resultId)
                {
                    continue;
                }
                if (VisionWorkflowAccess.Node(identity[1].Value) != pipeline || !found.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "The returned Vision result identity is ambiguous or belongs to a different pipeline.");
                }
                found = node;
            }
            if (found.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound,
                    "No result has the exact returned ResultId. BrowseName suffixes are not correlation evidence.");
            }
            return found;
        }

        public static async Task<CompanionOperationResult> ReadAsync(
            CompanionContext context, NodeId resultNode, string resultId, NodeId sensor, NodeId pipeline,
            CancellationToken cancellationToken)
        {
            await RequireResultAsync(context, resultNode, cancellationToken).ConfigureAwait(false);
            var client = new VisionClient(context.Session, context.Telemetry);
            VisionResultKind kind = await client.Inference().DetermineResultKindAsync(resultNode, cancellationToken)
                .ConfigureAwait(false);
            var fields = new CellCompanionFields(context.MaxFields);
            fields.Add("Result node", Variant.From(resultNode));
            fields.AddText("Result kind", kind.ToString());
            VisionResultReader reader = client.Result(resultNode);
            switch (kind)
            {
                case VisionResultKind.Detection:
                    VisionDetectionResultSnapshot detection = await reader.ReadDetectionAsync(cancellationToken)
                        .ConfigureAwait(false);
                    ValidateDetection(detection);
                    RequireIdentity(detection.ResultId, detection.SensorId, detection.PipelineId,
                        detection.CreationTime, resultId, sensor, pipeline);
                    AddIdentity(fields, detection.ResultId!, detection.SensorId, detection.PipelineId,
                        detection.CreationTime, detection.Frame);
                    fields.AddText("Frame ID", detection.FrameId);
                    fields.AddText("Model version", detection.ModelVersionUsed);
                    fields.Add("Detections", Variant.FromStructure(detection.Detections));
                    break;
                case VisionResultKind.Inspection:
                    VisionInspectionResultSnapshot inspection = await reader.ReadInspectionAsync(cancellationToken)
                        .ConfigureAwait(false);
                    RequireIdentity(inspection.ResultId, inspection.SensorId, inspection.PipelineId,
                        inspection.CreationTime, resultId, sensor, pipeline);
                    RequireCount(inspection.Characteristics);
                    VisionWorkflowValues.ValidateCharacteristics(inspection.Characteristics);
                    if (!Enum.IsDefined(inspection.Evaluation) || inspection.Characteristics.IsEmpty)
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Incomplete Vision inspection.");
                    }
                    AddIdentity(fields, inspection.ResultId!, inspection.SensorId, inspection.PipelineId,
                        inspection.CreationTime, inspection.Frame);
                    fields.AddText("Evaluation", inspection.Evaluation.ToString());
                    fields.Add("Characteristics", Variant.FromStructure(inspection.Characteristics));
                    break;
                case VisionResultKind.Segmentation:
                    VisionSegmentationResultSnapshot segmentation = await reader
                        .ReadSegmentationAsync(cancellationToken)
                        .ConfigureAwait(false);
                    RequireIdentity(segmentation.ResultId, segmentation.SensorId, segmentation.PipelineId,
                        segmentation.CreationTime, resultId, sensor, pipeline);
                    RequireCount(segmentation.LabelClasses);
                    if (segmentation.Mask is null || segmentation.LabelClasses.IsEmpty)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTypeMismatch, "Incomplete Vision segmentation.");
                    }
                    foreach (string label in segmentation.LabelClasses)
                    {
                        _ = VisionWorkflowAccess.Text(Variant.From(label));
                    }
                    AddIdentity(fields, segmentation.ResultId!, segmentation.SensorId, segmentation.PipelineId,
                        segmentation.CreationTime, segmentation.Frame);
                    VisionWorkflowValues.ValidateImage(segmentation.Mask, default);
                    fields.Add("Label classes", Variant.From(segmentation.LabelClasses));
                    fields.AddText("Mask SHA-256", Convert.ToHexString(segmentation.Mask.Digest.Span));
                    break;
                default:
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "This published Vision result subtype is not supported.");
            }
            ArrayOf<CompanionValue> values = fields.ToArray();
            using (var buffer = new IndustrialDocumentBuffer(VisionWorkflowValues.MaximumBytes))
            using (var encoder = new BinaryEncoder(buffer, context.Session.MessageContext, leaveOpen: true))
            {
                foreach (CompanionValue value in values)
                {
                    encoder.WriteVariant(null, value.Value);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new CompanionOperationResult(
                "Read the exact returned ResultId, sensor and pipeline through the typed Vision reader. " +
                "No image or mask URI was fetched, and no inference request was replayed.",
                CompanionInputContract.Snapshot(values, context.Session.MessageContext));
        }

        public static async Task<CompanionOperationResult> ReadMediaAsync(
            CompanionContext context, NodeId sensor, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = CellCompanionSupport.BeginOperation(
                context, Opc.Ua.Vision.Namespaces.Vision, cancellationToken);
            CancellationToken token = lifetime.Token;
            NodeId media = await IndustrialCompanionAccess.ResolveChildAsync(
                context, sensor, Opc.Ua.Vision.Namespaces.Vision, "Media", false, token).ConfigureAwait(false);
            var fields = new CellCompanionFields(context.MaxFields);
            int count = 0;
            foreach (string name in new[] { "ClipEndpoints", "StreamEndpoints" })
            {
                NodeId folder = await IndustrialCompanionAccess.ResolveChildAsync(
                    context, media, Opc.Ua.Vision.Namespaces.Vision, name, true, token).ConfigureAwait(false);
                if (folder.IsNull)
                {
                    continue;
                }
                await foreach (ReferenceDescription reference in IndustrialCompanionAccess.BrowseAsync(
                    context, folder, BrowseDirection.Forward, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                    NodeClass.Object, new IndustrialBrowseBudget(context), token).ConfigureAwait(false))
                {
                    CellCompanionSupport.CheckCount(
                        ++count, Math.Min(context.MaxTargets, 32), "Vision media endpoints");
                    NodeId endpoint = IndustrialCompanionAccess.LocalId(context, reference.NodeId);
                    await VisionWorkflowAccess.RequireEndpointAsync(
                        context, media, endpoint, name == "ClipEndpoints", token).ConfigureAwait(false);
                    ArrayOf<CompanionValue> values = await VisionWorkflowAccess.PropertiesAsync(
                        context, endpoint, ["EndpointId", "State", "SecureTransport"], token).ConfigureAwait(false);
                    string prefix = $"Endpoint {count}";
                    fields.Add(prefix + " node", Variant.From(endpoint));
                    fields.AddText(prefix + " kind", name);
                    fields.AddText(prefix + " ID", VisionWorkflowAccess.Text(values[0].Value));
                    fields.AddText(prefix + " state",
                        VisionWorkflowAccess.EnumValue<VisionEndpointStateEnum>(values[1].Value).ToString());
                    fields.Add(prefix + " confidential", Variant.From(VisionWorkflowAccess.Boolean(values[2].Value)));
                }
            }
            token.ThrowIfCancellationRequested();
            return new CompanionOperationResult(
                $"Read {count} media endpoint(s). No frame, lease or media transport was requested; URIs are omitted.",
                fields.ToArray());
        }

        public static async Task<CompanionOperationResult> ExportOverlayAsync(
            CompanionContext context, CompanionTarget target, string? destination, CancellationToken cancellationToken)
        {
            string path = VisionOverlayExport.ValidateDestination(destination);
            using CancellationTokenSource lifetime = CellCompanionSupport.BeginOperation(
                context, Opc.Ua.Vision.Namespaces.Vision, cancellationToken);
            CancellationToken token = lifetime.Token;
            var evidence = new CompanionOperationDraft(target,
                new CompanionOperation("export-overlay", "Export overlay", CompanionOperationSafety.LocalFile),
                path, context.Session, TimeProvider.System.GetUtcNow().AddMinutes(5));
            await RequireResultAsync(context, target.NodeId, token).ConfigureAwait(false);
            var client = new VisionClient(context.Session, context.Telemetry);
            if (await client.Inference().DetermineResultKindAsync(target.NodeId, token).ConfigureAwait(false) !=
                VisionResultKind.Detection)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "A detection result is required.");
            }
            VisionDetectionResultSnapshot snapshot = await client.Result(target.NodeId).ReadDetectionAsync(token)
                .ConfigureAwait(false);
            ValidateDetection(snapshot);
            RequireIdentity(snapshot.ResultId, snapshot.SensorId, snapshot.PipelineId, snapshot.CreationTime,
                snapshot.ResultId ?? string.Empty, snapshot.SensorId, snapshot.PipelineId);
            ByteString svg = VisionOverlayExport.Render(snapshot);
            await VisionOverlayExport.WriteAsync(path, svg, () =>
            {
                if (!evidence.Matches(context.Session, TimeProvider.System.GetUtcNow()))
                {
                    throw new InvalidOperationException("The source session changed before overlay export completed.");
                }
            }, token).ConfigureAwait(false);
            return new CompanionOperationResult(
                "Created a managed SVG overlay from published pixel-space boxes, with source IDs and frame digest. " +
                "No media URI, image pixels, external asset, script, GPU or 3D reprojection is included.",
                [new("Local SVG", Variant.From(path)), new("Result ID", Variant.From(snapshot.ResultId!)),
                    new("SVG bytes", Variant.From(svg.Length))]);
        }

        public static void AddImage(CellCompanionFields fields, VisionImageReferenceDataType image)
        {
            VisionWorkflowValues.ValidateImage(image, default);
            fields.Add("Frame width", Variant.From(image.Width));
            fields.Add("Frame height", Variant.From(image.Height));
            fields.Add("Frame bytes", Variant.From(image.SizeBytes));
            fields.AddText("Frame format", image.Format.ToString());
            fields.Add("Frame acquisition time", Variant.From(image.Timestamp));
            fields.AddText("Frame SHA-256", Convert.ToHexString(image.Digest.Span));
        }

        public static void ValidateDetection(VisionDetectionResultSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            RequireCount(snapshot.Detections);
            VisionWorkflowValues.ValidateDetections(snapshot.Detections);
            foreach (VisionDetectionDataType detection in snapshot.Detections)
            {
                if (detection.HasPose &&
                    (string.IsNullOrWhiteSpace(snapshot.FrameId) ||
                        snapshot.FrameId.Length > 256 ||
                        detection.Pose.FrameId != snapshot.FrameId))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "Detection poses must name the result's coordinate frame.");
                }
            }
        }

        private static ValueTask RequireResultAsync(
            CompanionContext context, NodeId node, CancellationToken cancellationToken)
        {
            return IndustrialCompanionAccess.RequireTargetAsync(context,
                new CompanionTarget("vision", node, "Published result", "VisionResult"), "vision",
                sResults, cancellationToken);
        }

        private static void RequireIdentity(
            string? actualId, NodeId actualSensor, NodeId actualPipeline, DateTimeUtc created,
            string resultId, NodeId sensor, NodeId pipeline)
        {
            if (string.IsNullOrWhiteSpace(actualId) ||
                actualId.Length > 256 ||
                actualId != resultId ||
                actualSensor.IsNull ||
                actualSensor != sensor ||
                actualPipeline.IsNull ||
                actualPipeline != pipeline ||
                created == default)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "The published Vision result does not match the expected identity, sensor and pipeline.");
            }
        }

        private static void AddIdentity(
            CellCompanionFields fields, string id, NodeId sensor, NodeId pipeline, DateTimeUtc created,
            VisionImageReferenceDataType? frame)
        {
            fields.AddText("Result ID", id);
            fields.Add("Sensor", Variant.From(sensor));
            fields.Add("Pipeline", Variant.From(pipeline));
            fields.Add("Result creation time", Variant.From(created));
            if (frame is not null)
            {
                AddImage(fields, frame);
            }
        }

        private static void RequireCount<T>(ArrayOf<T> items)
        {
            if (items.IsNull || items.Count > VisionWorkflowValues.MaximumItems)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "Vision result arrays must be present and contain at most 32 entries.");
            }
        }

        private static readonly ArrayOf<IndustrialCompanionType> sResults =
            [new(Opc.Ua.Vision.ObjectTypeIds.VisionResultType, "VisionResult")];
    }
}
