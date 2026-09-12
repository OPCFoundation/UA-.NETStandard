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
using Opc.Ua;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// Reads the draft Vision model and already-published results without acquiring media or running inference.
/// </summary>
internal sealed class VisionCompanionProvider : ICompanionProvider
{
    public VisionCompanionProvider()
        : this(static context => new VisionCompanionReader(context))
    {
    }

    public VisionCompanionProvider(Func<CompanionContext, IVisionCompanionReader> createReader)
    {
        m_createReader = createReader ?? throw new ArgumentNullException(nameof(createReader));
    }

    public CompanionDescriptor Descriptor { get; } = new(
        "vision", "Vision cell", Opc.Ua.Vision.Namespaces.Vision, "Draft Vision model; experimental, not ratified");

    public async ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
        CompanionContext context,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetime =
            CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
        IVisionCompanionReader reader = m_createReader(context);
        CancellationToken token = lifetime.Token;
        var targets = new List<CompanionTarget>();
        var visited = new HashSet<NodeId>();
        await AppendAsync(reader.EnumerateSensorsAsync(token), "VisionSensor", targets, visited, context, token)
            .ConfigureAwait(false);
        await AppendAsync(reader.EnumerateFramesAsync(token), "CoordinateFrame", targets, visited, context, token)
            .ConfigureAwait(false);
        await foreach (VisionNodeEntry pipeline in reader.EnumeratePipelinesAsync(token)
            .WithCancellation(token).ConfigureAwait(false))
        {
            token.ThrowIfCancellationRequested();
            AddTarget(pipeline, "InferencePipeline", targets, visited, context.MaxTargets);
            await AppendAsync(
                reader.EnumerateResultsAsync(pipeline.NodeId, token),
                "VisionResult",
                targets,
                visited,
                context,
                token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        return [.. targets];
    }

    public async ValueTask<CompanionInspection> InspectAsync(
        CompanionContext context,
        CompanionTarget target,
        CancellationToken cancellationToken)
    {
        ValidateTarget(context, target);
        using CancellationTokenSource lifetime =
            CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
        IVisionCompanionReader reader = m_createReader(context);
        CancellationToken token = lifetime.Token;
        var fields = new CellCompanionFields(context.MaxFields);
        fields.Add("Node", Variant.From(target.NodeId));
        switch (target.TypeName)
        {
            case "VisionSensor":
                await ReadSensorAsync(reader, target.NodeId, fields, token).ConfigureAwait(false);
                break;
            case "CoordinateFrame":
                VisionFrameSnapshot frame = await reader.ReadFrameAsync(target.NodeId, token).ConfigureAwait(false);
                fields.AddText("Frame ID", frame.FrameId);
                fields.AddText("Role", frame.Role.ToString());
                fields.Add("Parent frame", Variant.From(frame.ParentFrameId));
                if (frame.Transform is not null)
                {
                    AddPose(fields, "Transform", frame.Transform);
                }
                break;
            case "InferencePipeline":
                VisionPipelineSnapshot pipeline = await reader.ReadPipelineAsync(target.NodeId, token)
                    .ConfigureAwait(false);
                fields.AddText("Pipeline ID", pipeline.PipelineId);
                fields.AddText("State", pipeline.State.ToString());
                fields.Add("Continuous", Variant.From(pipeline.Continuous));
                fields.Add("Sensor", Variant.From(pipeline.SensorId));
                fields.Add("Deployment", Variant.From(pipeline.DeploymentId));
                fields.Add("Learning job", Variant.From(pipeline.LearningJobId));
                break;
            case "VisionResult":
                await ReadResultAsync(reader, target.NodeId, fields, context.MaxFields, token).ConfigureAwait(false);
                break;
            default:
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Unsupported Vision instance kind.");
        }
        token.ThrowIfCancellationRequested();
        ArrayOf<CompanionOperation> operations = target.TypeName == "InferencePipeline"
            ? [
                new("snapshot", "Read fresh snapshot", CompanionOperationSafety.ReadOnly),
                new("results", "List published results", CompanionOperationSafety.ReadOnly)
            ]
            : [new("snapshot", "Read fresh snapshot", CompanionOperationSafety.ReadOnly)];
        return new CompanionInspection(
            fields.ToArray(),
            operations,
            "Read-only draft Vision snapshot. " +
            "No acquisition, inference, feedback, calibration or media fetch was requested.");
    }

    public async ValueTask<CompanionOperationResult> ExecuteAsync(
        CompanionContext context,
        CompanionTarget target,
        string operationId,
        string? input,
        CancellationToken cancellationToken)
    {
        ValidateTarget(context, target);
        cancellationToken.ThrowIfCancellationRequested();
        CellCompanionSupport.RequireNoInput(input);
        if (operationId == "snapshot")
        {
            CompanionInspection inspection = await InspectAsync(context, target, cancellationToken)
                .ConfigureAwait(false);
            return new CompanionOperationResult(inspection.Summary, inspection.Values);
        }
        if (operationId != "results" || target.TypeName != "InferencePipeline")
        {
            throw new ServiceResultException(
                StatusCodes.BadNotSupported, "This Vision read operation is not supported.");
        }
        using CancellationTokenSource lifetime =
            CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
        IVisionCompanionReader reader = m_createReader(context);
        var fields = new CellCompanionFields(context.MaxFields);
        int count = 0;
        await foreach (VisionNodeEntry entry in reader.EnumerateResultsAsync(target.NodeId, lifetime.Token)
            .WithCancellation(lifetime.Token).ConfigureAwait(false))
        {
            lifetime.Token.ThrowIfCancellationRequested();
            CellCompanionSupport.CheckCount(++count, context.MaxTargets, "Published Vision results");
            fields.Add($"Result {count} node", Variant.From(entry.NodeId));
            fields.AddText($"Result {count} name", entry.DisplayName.Text ?? entry.BrowseName.Name);
            VisionResultKind kind = await reader.DetermineResultKindAsync(entry.NodeId, lifetime.Token)
                .ConfigureAwait(false);
            fields.AddText($"Result {count} kind", kind.ToString());
        }
        lifetime.Token.ThrowIfCancellationRequested();
        return new CompanionOperationResult(
            $"Read {count} already-published result(s). The pipeline was not started or changed.", fields.ToArray());
    }

    private async Task AppendAsync(
        IAsyncEnumerable<VisionNodeEntry> entries,
        string typeName,
        List<CompanionTarget> targets,
        HashSet<NodeId> visited,
        CompanionContext context,
        CancellationToken cancellationToken)
    {
        await foreach (VisionNodeEntry entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddTarget(entry, typeName, targets, visited, context.MaxTargets);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void AddTarget(
        VisionNodeEntry entry,
        string typeName,
        List<CompanionTarget> targets,
        HashSet<NodeId> visited,
        int maximum)
    {
        CellCompanionSupport.AddTarget(
            targets,
            visited,
            new CompanionTarget(
                Descriptor.Id,
                entry.NodeId,
                entry.DisplayName.Text ?? entry.BrowseName.Name ?? entry.NodeId.ToString(),
                typeName),
            maximum);
    }

    private void ValidateTarget(CompanionContext context, CompanionTarget target)
    {
        CellCompanionSupport.ValidateTarget(context, target, Descriptor.Id);
        if (target.TypeName is not ("VisionSensor" or "CoordinateFrame" or "InferencePipeline" or "VisionResult"))
        {
            throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Select a supported Vision instance.");
        }
    }

    private static async Task ReadSensorAsync(
        IVisionCompanionReader reader,
        NodeId nodeId,
        CellCompanionFields fields,
        CancellationToken cancellationToken)
    {
        VisionSensorIdentity identity = await reader.ReadSensorAsync(nodeId, cancellationToken).ConfigureAwait(false);
        fields.AddText("Sensor ID", identity.SensorId);
        fields.AddText("Reality kind", identity.RealityKind.ToString());
        fields.AddText("Modality", identity.Modality.ToString());
        fields.Add("Manufacturer", Variant.From(identity.Manufacturer));
        fields.Add("Model", Variant.From(identity.Model));
        fields.AddText("Serial number", identity.SerialNumber);
        fields.AddText("Frame ID", identity.FrameId);
        VisionImageSensorSnapshot? image = await reader.ReadImageMembersAsync(nodeId, cancellationToken)
            .ConfigureAwait(false);
        if (image is not null)
        {
            fields.Add("Image width", Variant.From(image.Width));
            fields.Add("Image height", Variant.From(image.Height));
            fields.AddText("Pixel format", image.PixelFormat);
            AddOptionalDouble(fields, "Exposure microseconds", image.ExposureTime);
            AddOptionalDouble(fields, "Gain", image.Gain);
            AddOptionalDouble(fields, "Acquisition rate Hz", image.AcquisitionFrameRate);
        }
        VisionDepth3DSensorSnapshot? depth = await reader.ReadDepthMembersAsync(nodeId, cancellationToken)
            .ConfigureAwait(false);
        if (depth is not null)
        {
            fields.Add("Minimum depth metres", Variant.From(depth.MinDepth));
            fields.Add("Maximum depth metres", Variant.From(depth.MaxDepth));
            fields.Add("Depth scale", Variant.From(depth.DepthScale));
            fields.Add("Baseline metres", Variant.From(depth.Baseline));
            fields.Add("Points per frame", Variant.From(depth.PointsPerFrame));
        }
    }

    private static async Task ReadResultAsync(
        IVisionCompanionReader reader,
        NodeId nodeId,
        CellCompanionFields fields,
        int maximum,
        CancellationToken cancellationToken)
    {
        VisionResultKind kind = await reader.DetermineResultKindAsync(nodeId, cancellationToken).ConfigureAwait(false);
        fields.AddText("Result kind", kind.ToString());
        switch (kind)
        {
            case VisionResultKind.Detection:
                VisionDetectionResultSnapshot detection = await reader.ReadDetectionAsync(nodeId, cancellationToken)
                    .ConfigureAwait(false);
                CellCompanionSupport.CheckCount(detection.Detections.Count, maximum, "Detections");
                AddResultIdentity(fields, detection.ResultId, detection.CreationTime, detection.SensorId,
                    detection.PipelineId, detection.Frame);
                fields.AddText("Model version", detection.ModelVersionUsed);
                fields.AddText("Frame ID", detection.FrameId);
                fields.Add("Detection count", Variant.From(detection.Detections.Count));
                for (int index = 0; index < detection.Detections.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    VisionDetectionDataType item = detection.Detections[index];
                    string prefix = $"Detection {index + 1}";
                    fields.AddText($"{prefix} ID", item.DetectionId);
                    fields.AddText($"{prefix} class", item.ClassLabel);
                    fields.Add($"{prefix} confidence", Variant.From(item.Confidence));
                    fields.Add($"{prefix} has pose", Variant.From(item.HasPose));
                    if (item.HasPose && item.Pose is not null)
                    {
                        AddPose(fields, $"{prefix} pose", item.Pose);
                    }
                }
                break;
            case VisionResultKind.Inspection:
                VisionInspectionResultSnapshot inspection = await reader.ReadInspectionAsync(nodeId, cancellationToken)
                    .ConfigureAwait(false);
                CellCompanionSupport.CheckCount(inspection.Characteristics.Count, maximum, "Characteristics");
                AddResultIdentity(fields, inspection.ResultId, inspection.CreationTime, inspection.SensorId,
                    inspection.PipelineId, inspection.Frame);
                fields.AddText("Evaluation", inspection.Evaluation.ToString());
                fields.AddText("Part ID", inspection.PartId);
                fields.AddText("Recipe ID", inspection.RecipeId);
                fields.Add("Characteristic count", Variant.From(inspection.Characteristics.Count));
                for (int index = 0; index < inspection.Characteristics.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    VisionCharacteristicDataType item = inspection.Characteristics[index];
                    string prefix = $"Characteristic {index + 1}";
                    fields.AddText($"{prefix} name", item.Name);
                    fields.AddText($"{prefix} status", item.Status.ToString());
                    fields.Add($"{prefix} actual", Variant.From(item.Actual));
                    fields.Add($"{prefix} deviation", Variant.From(item.Deviation));
                }
                break;
            case VisionResultKind.Segmentation:
                VisionSegmentationResultSnapshot segmentation = await reader
                    .ReadSegmentationAsync(nodeId, cancellationToken).ConfigureAwait(false);
                CellCompanionSupport.CheckCount(segmentation.LabelClasses.Count, maximum, "Segmentation labels");
                AddResultIdentity(fields, segmentation.ResultId, segmentation.CreationTime, segmentation.SensorId,
                    segmentation.PipelineId, segmentation.Frame);
                for (int index = 0; index < segmentation.LabelClasses.Count; index++)
                {
                    fields.AddText($"Label {index + 1}", segmentation.LabelClasses[index]);
                }
                AddImageDescriptor(fields, "Mask", segmentation.Mask);
                break;
            default:
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The published result is not a supported Vision result subtype.");
        }
    }

    private static void AddResultIdentity(
        CellCompanionFields fields,
        string? resultId,
        DateTimeUtc creationTime,
        NodeId sensorId,
        NodeId pipelineId,
        VisionImageReferenceDataType? frame)
    {
        fields.AddText("Result ID", resultId);
        fields.Add("Created", Variant.From(creationTime));
        fields.Add("Sensor", Variant.From(sensorId));
        fields.Add("Pipeline", Variant.From(pipelineId));
        AddImageDescriptor(fields, "Image", frame);
    }

    private static void AddImageDescriptor(
        CellCompanionFields fields,
        string prefix,
        VisionImageReferenceDataType? image)
    {
        if (image is null)
        {
            return;
        }
        fields.Add($"{prefix} width", Variant.From(image.Width));
        fields.Add($"{prefix} height", Variant.From(image.Height));
        fields.Add($"{prefix} byte size", Variant.From(image.SizeBytes));
        fields.AddText($"{prefix} format", image.Format.ToString());
    }

    private static void AddPose(CellCompanionFields fields, string prefix, VisionPose3DDataType pose)
    {
        if (pose.Position.Count != 3 || pose.Orientation.Count != 4 || pose.Covariance.Count is not (0 or 36))
        {
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch, "A Vision pose has invalid array dimensions.");
        }
        fields.AddText($"{prefix} frame ID", pose.FrameId);
        fields.Add($"{prefix} position metres", Variant.From(pose.Position));
        fields.Add($"{prefix} quaternion XYZW", Variant.From(pose.Orientation));
    }

    private static void AddOptionalDouble(CellCompanionFields fields, string name, double? value)
    {
        if (value.HasValue)
        {
            fields.Add(name, Variant.From(value.Value));
        }
    }

    private readonly Func<CompanionContext, IVisionCompanionReader> m_createReader;
}

/// <summary>
/// Narrow read-only seam over sealed Vision clients; no acquisition, inference or media transport can be requested.
/// </summary>
internal interface IVisionCompanionReader
{
    IAsyncEnumerable<VisionNodeEntry> EnumerateSensorsAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<VisionNodeEntry> EnumerateFramesAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<VisionNodeEntry> EnumeratePipelinesAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<VisionNodeEntry> EnumerateResultsAsync(NodeId pipeline, CancellationToken cancellationToken);

    Task<VisionSensorIdentity> ReadSensorAsync(NodeId sensor, CancellationToken cancellationToken);

    Task<VisionImageSensorSnapshot?> ReadImageMembersAsync(NodeId sensor, CancellationToken cancellationToken);

    Task<VisionDepth3DSensorSnapshot?> ReadDepthMembersAsync(NodeId sensor, CancellationToken cancellationToken);

    Task<VisionFrameSnapshot> ReadFrameAsync(NodeId frame, CancellationToken cancellationToken);

    Task<VisionPipelineSnapshot> ReadPipelineAsync(NodeId pipeline, CancellationToken cancellationToken);

    Task<VisionResultKind> DetermineResultKindAsync(NodeId result, CancellationToken cancellationToken);

    Task<VisionDetectionResultSnapshot> ReadDetectionAsync(NodeId result, CancellationToken cancellationToken);

    Task<VisionInspectionResultSnapshot> ReadInspectionAsync(NodeId result, CancellationToken cancellationToken);

    Task<VisionSegmentationResultSnapshot> ReadSegmentationAsync(NodeId result, CancellationToken cancellationToken);
}

/// <summary>
/// Uses the model's generated proxies and focused frame/result readers on the borrowed session.
/// </summary>
internal sealed class VisionCompanionReader : IVisionCompanionReader
{
    public VisionCompanionReader(CompanionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_client = new VisionClient(context.Session, context.Telemetry);
    }

    public IAsyncEnumerable<VisionNodeEntry> EnumerateSensorsAsync(CancellationToken cancellationToken)
    {
        return m_client.EnumerateSensorsAsync(cancellationToken);
    }

    public IAsyncEnumerable<VisionNodeEntry> EnumerateFramesAsync(CancellationToken cancellationToken)
    {
        return m_client.EnumerateFramesAsync(cancellationToken);
    }

    public IAsyncEnumerable<VisionNodeEntry> EnumeratePipelinesAsync(CancellationToken cancellationToken)
    {
        return m_client.EnumeratePipelinesAsync(cancellationToken);
    }

    public IAsyncEnumerable<VisionNodeEntry> EnumerateResultsAsync(NodeId pipeline, CancellationToken cancellationToken)
    {
        return m_client.Pipeline(pipeline).EnumerateResultsAsync(cancellationToken);
    }

    public Task<VisionSensorIdentity> ReadSensorAsync(NodeId sensor, CancellationToken cancellationToken)
    {
        return m_client.Sensor(sensor).ReadIdentityAsync(cancellationToken);
    }

    public Task<VisionImageSensorSnapshot?> ReadImageMembersAsync(NodeId sensor, CancellationToken cancellationToken)
    {
        return m_client.Sensor(sensor).ReadImageMembersAsync(cancellationToken);
    }

    public Task<VisionDepth3DSensorSnapshot?> ReadDepthMembersAsync(NodeId sensor, CancellationToken cancellationToken)
    {
        return m_client.Sensor(sensor).ReadDepthMembersAsync(cancellationToken);
    }

    public Task<VisionFrameSnapshot> ReadFrameAsync(NodeId frame, CancellationToken cancellationToken)
    {
        return m_client.Frames().ReadAsync(frame, cancellationToken);
    }

    public Task<VisionPipelineSnapshot> ReadPipelineAsync(NodeId pipeline, CancellationToken cancellationToken)
    {
        return m_client.Pipeline(pipeline).ReadAsync(cancellationToken);
    }

    public Task<VisionResultKind> DetermineResultKindAsync(NodeId result, CancellationToken cancellationToken)
    {
        return m_client.Inference().DetermineResultKindAsync(result, cancellationToken);
    }

    public Task<VisionDetectionResultSnapshot> ReadDetectionAsync(NodeId result, CancellationToken cancellationToken)
    {
        return m_client.Result(result).ReadDetectionAsync(cancellationToken);
    }

    public Task<VisionInspectionResultSnapshot> ReadInspectionAsync(NodeId result, CancellationToken cancellationToken)
    {
        return m_client.Result(result).ReadInspectionAsync(cancellationToken);
    }

    public Task<VisionSegmentationResultSnapshot> ReadSegmentationAsync(
        NodeId result,
        CancellationToken cancellationToken)
    {
        return m_client.Result(result).ReadSegmentationAsync(cancellationToken);
    }

    private readonly VisionClient m_client;
}
