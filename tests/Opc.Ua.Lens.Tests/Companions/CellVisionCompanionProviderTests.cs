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
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class CellVisionCompanionProviderTests
{
    [Test]
    public async Task DefaultProviderReadsSensorDataThroughTheVisionClientAsync()
    {
        var session = new CellProviderTestSession(Opc.Ua.Vision.Namespaces.Vision);
        session.AddValue(s_sensor, "SensorId", Variant.From("camera-17"));
        session.AddValue(s_sensor, "RealityKind", Variant.From((int)VisionRealityKindEnum.Simulated));
        session.AddValue(s_sensor, "Modality", Variant.From((int)VisionSensorModalityEnum.Area2D));
        session.AddValue(s_sensor, "FrameId", Variant.From("camera-frame"));
        session.AddValue(s_sensor, "Width", Variant.From(640u));
        session.AddValue(s_sensor, "Height", Variant.From(480u));
        session.AddValue(s_sensor, "PixelFormat", Variant.From("Mono8"));
        var provider = new VisionCompanionProvider();

        CompanionInspection inspection = await provider.InspectAsync(
            session.Context, Target(s_sensor, "VisionSensor"), CancellationToken.None).ConfigureAwait(false);

        Assert.That(CellProviderTestSession.Field(inspection.Values, "Sensor ID").TryGetValue(out string? id), Is.True);
        Assert.That(id, Is.EqualTo("camera-17"));
        Assert.That(CellProviderTestSession.Field(inspection.Values, "Reality kind")
            .TryGetValue(out string? reality), Is.True);
        Assert.That(reality, Is.EqualTo("Simulated"));
        Assert.That(CellProviderTestSession.Field(inspection.Values, "Image width")
            .TryGetValue(out uint width), Is.True);
        Assert.That(width, Is.EqualTo(640u));
        Assert.That(provider.Descriptor.Maturity, Does.Contain("Draft"));
        Assert.That(inspection.Summary, Does.Contain("No acquisition"));
        session.VerifyNoMutationOrSessionOwnership();
    }

    [Test]
    public async Task DiscoveryReturnsTypedInstancesAndOnlyAlreadyPublishedResultsAsync()
    {
        var session = new CellProviderTestSession(Opc.Ua.Vision.Namespaces.Vision);
        var reader = new TestReader
        {
            Sensors = new CellProviderTestEntries<VisionNodeEntry>([Entry(s_sensor, "Camera")]),
            Frames = new CellProviderTestEntries<VisionNodeEntry>([Entry(s_frame, "World")]),
            Pipelines = new CellProviderTestEntries<VisionNodeEntry>([Entry(s_pipeline, "Inspection")]),
            Results = new CellProviderTestEntries<VisionNodeEntry>([Entry(s_result, "Published result")])
        };
        var provider = new VisionCompanionProvider(_ => reader);

        ArrayOf<CompanionTarget> targets = await provider.DiscoverAsync(session.Context, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(targets, Has.Count.EqualTo(4));
        Assert.That(targets[0].NodeId, Is.EqualTo(s_sensor));
        Assert.That(targets[1].TypeName, Is.EqualTo("CoordinateFrame"));
        Assert.That(targets[2].TypeName, Is.EqualTo("InferencePipeline"));
        Assert.That(targets[3].TypeName, Is.EqualTo("VisionResult"));
        Assert.That(reader.ResultPipeline, Is.EqualTo(s_pipeline));
        Assert.That(reader.Reads, Is.Zero);
        Assert.That(reader.Sensors.Disposals, Is.EqualTo(1));
        Assert.That(reader.Results.Disposals, Is.EqualTo(1));
        session.VerifyNoMutationOrSessionOwnership();
    }

    [Test]
    public async Task PublishedResultListingIsAReadTaskNotAnInferenceRequestAsync()
    {
        var session = new CellProviderTestSession(Opc.Ua.Vision.Namespaces.Vision);
        var reader = new TestReader
        {
            Results = new CellProviderTestEntries<VisionNodeEntry>([Entry(s_result, "Existing")]),
            Kind = VisionResultKind.Detection
        };
        var provider = new VisionCompanionProvider(_ => reader);

        CompanionOperationResult result = await provider.ExecuteAsync(
            session.Context, Target(s_pipeline, "InferencePipeline"), "results", null, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.Summary, Does.Contain("1 already-published").And.Contain("not started"));
        Assert.That(CellProviderTestSession.Field(result.Values, "Result 1 kind")
            .TryGetValue(out string? kind), Is.True);
        Assert.That(kind, Is.EqualTo("Detection"));
        Assert.That(reader.ResultNode, Is.EqualTo(s_result));
        Assert.That(reader.ResultPipeline, Is.EqualTo(s_pipeline));
        Assert.That(reader.Reads, Is.EqualTo(1));
    }

    [TestCase(VisionResultKind.Detection, "Detection 1 confidence")]
    [TestCase(VisionResultKind.Inspection, "Characteristic 1 deviation")]
    [TestCase(VisionResultKind.Segmentation, "Mask width")]
    public async Task TypedResultReadersReturnTheActualPublishedDataAsync(VisionResultKind kind, string field)
    {
        var session = new CellProviderTestSession(Opc.Ua.Vision.Namespaces.Vision);
        var reader = new TestReader
        {
            Kind = kind,
            Detection = new VisionDetectionResultSnapshot
            {
                NodeId = s_result,
                ResultId = "existing-5",
                SensorId = s_sensor,
                PipelineId = s_pipeline,
                Detections = [new VisionDetectionDataType { DetectionId = "part-5", Confidence = 0.875 }]
            },
            Inspection = new VisionInspectionResultSnapshot
            {
                NodeId = s_result,
                ResultId = "existing-5",
                Evaluation = VisionResultEvaluationEnum.Ok,
                Characteristics = [new VisionCharacteristicDataType { Name = "Length", Deviation = 0.875 }]
            },
            Segmentation = new VisionSegmentationResultSnapshot
            {
                NodeId = s_result,
                ResultId = "existing-5",
                LabelClasses = ["background", "part"],
                Mask = new VisionImageReferenceDataType { Width = 640, Height = 480, Uri = "never-fetch://mask" }
            }
        };
        var provider = new VisionCompanionProvider(_ => reader);

        CompanionInspection inspection = await provider.InspectAsync(
            session.Context, Target(s_result, "VisionResult"), CancellationToken.None).ConfigureAwait(false);

        Assert.That(reader.ResultNode, Is.EqualTo(s_result));
        Assert.That(reader.Reads, Is.EqualTo(2));
        Assert.That(CellProviderTestSession.Field(inspection.Values, "Result ID").TryGetValue(out string? id), Is.True);
        Assert.That(id, Is.EqualTo("existing-5"));
        if (kind == VisionResultKind.Segmentation)
        {
            Assert.That(CellProviderTestSession.Field(inspection.Values, field).TryGetValue(out uint width), Is.True);
            Assert.That(width, Is.EqualTo(640u));
        }
        else
        {
            Assert.That(CellProviderTestSession.Field(inspection.Values, field).TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(0.875));
        }
        Assert.That(inspection.Operations[0].Safety, Is.EqualTo(CompanionOperationSafety.ReadOnly));
        session.VerifyNoMutationOrSessionOwnership();
    }

    [Test]
    public async Task FrameInspectionPreservesTypedPositionAndQuaternionAsync()
    {
        var session = new CellProviderTestSession(Opc.Ua.Vision.Namespaces.Vision);
        var reader = new TestReader
        {
            Frame = new VisionFrameSnapshot
            {
                NodeId = s_frame,
                FrameId = "camera",
                ParentFrameId = new NodeId("world", 2),
                Transform = new VisionPose3DDataType
                {
                    FrameId = "world",
                    Position = [1d, 2d, 3d],
                    Orientation = [0d, 0d, 0d, 1d]
                }
            }
        };
        var provider = new VisionCompanionProvider(_ => reader);

        CompanionInspection inspection = await provider.InspectAsync(
            session.Context, Target(s_frame, "CoordinateFrame"), CancellationToken.None).ConfigureAwait(false);

        Assert.That(CellProviderTestSession.Field(inspection.Values, "Transform position metres")
            .TryGetValue(out ArrayOf<double> position), Is.True);
        Assert.That(position.ToArray(), Is.EqualTo(s_expectedPosition));
        Assert.That(CellProviderTestSession.Field(inspection.Values, "Transform quaternion XYZW")
            .TryGetValue(out ArrayOf<double> orientation), Is.True);
        Assert.That(orientation.ToArray(), Is.EqualTo(s_expectedOrientation));
    }

    [Test]
    public void MalformedPoseDoesNotLookLikeAValidCoordinateFrame()
    {
        var session = new CellProviderTestSession(Opc.Ua.Vision.Namespaces.Vision);
        var reader = new TestReader
        {
            Frame = new VisionFrameSnapshot
            {
                NodeId = s_frame,
                Transform = new VisionPose3DDataType { Position = [1d, 2d], Orientation = [0d, 0d, 0d, 1d] }
            }
        };
        var provider = new VisionCompanionProvider(_ => reader);
        Assert.That(
            async () => await provider.InspectAsync(
                session.Context, Target(s_frame, "CoordinateFrame"), CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>());
    }

    [Test]
    public void ResultCollectionsAndFieldsAreBounded()
    {
        var session = new CellProviderTestSession(Opc.Ua.Vision.Namespaces.Vision, maxFields: 2);
        var reader = new TestReader
        {
            Kind = VisionResultKind.Detection,
            Detection = new VisionDetectionResultSnapshot
            {
                NodeId = s_result,
                Detections =
                [
                    new VisionDetectionDataType(),
                    new VisionDetectionDataType(),
                    new VisionDetectionDataType()
                ]
            }
        };
        var provider = new VisionCompanionProvider(_ => reader);
        Assert.That(
            async () => await provider.InspectAsync(
                session.Context, Target(s_result, "VisionResult"), CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>());
    }

    [Test]
    public void DiscoveryLimitStopsBeforePipelinesOrAnyResultWork()
    {
        var session = new CellProviderTestSession(Opc.Ua.Vision.Namespaces.Vision, maxTargets: 1);
        var reader = new TestReader
        {
            Sensors = new CellProviderTestEntries<VisionNodeEntry>(
                [Entry(s_sensor, "One"), Entry(new NodeId("second", 2), "Two")])
        };
        var provider = new VisionCompanionProvider(_ => reader);
        Assert.That(
            async () => await provider.DiscoverAsync(session.Context, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>());
        Assert.That(reader.Sensors.Disposals, Is.EqualTo(1));
        Assert.That(reader.Pipelines.Visited, Is.Zero);
        Assert.That(reader.Reads, Is.Zero);
    }

    [Test]
    public async Task CancellationIsForwardedIntoThePendingPipelineReadAsync()
    {
        var session = new CellProviderTestSession(Opc.Ua.Vision.Namespaces.Vision);
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<VisionPipelineSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new TestReader
        {
            ReadPipeline = token =>
            {
                started.SetResult();
                return release.Task.WaitAsync(token);
            }
        };
        var provider = new VisionCompanionProvider(_ => reader);
        Task<CompanionInspection> inspection = provider.InspectAsync(
            session.Context, Target(s_pipeline, "InferencePipeline"), cancellation.Token).AsTask();
        await started.Task.ConfigureAwait(false);
        cancellation.Cancel();

        await Assert.ThatAsync(() => inspection, Throws.InstanceOf<OperationCanceledException>())
            .ConfigureAwait(false);
        session.VerifyNoMutationOrSessionOwnership();
    }

    [Test]
    public void AnUnknownResultKindIsAnExplicitFailure()
    {
        var session = new CellProviderTestSession(Opc.Ua.Vision.Namespaces.Vision);
        var reader = new TestReader { Kind = VisionResultKind.Unknown };
        var provider = new VisionCompanionProvider(_ => reader);
        Assert.That(
            async () => await provider.InspectAsync(
                session.Context, Target(s_result, "VisionResult"), CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>());
        Assert.That(reader.Reads, Is.EqualTo(1));
    }

    [TestCase("RunInference")]
    [TestCase("StartContinuous")]
    [TestCase("calibrate")]
    public void DiscoveryDoesNotGrantAnyAcquisitionOrMutationVerb(string operation)
    {
        var session = new CellProviderTestSession(Opc.Ua.Vision.Namespaces.Vision);
        int created = 0;
        var provider = new VisionCompanionProvider(_ =>
        {
            created++;
            return new TestReader();
        });
        Assert.That(
            async () => await provider.ExecuteAsync(
                session.Context, Target(s_pipeline, "InferencePipeline"), operation, null, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>());
        Assert.That(created, Is.Zero);
    }

    private static CompanionTarget Target(NodeId nodeId, string kind)
    {
        return new CompanionTarget("vision", nodeId, kind, kind);
    }

    private static VisionNodeEntry Entry(NodeId nodeId, string name)
    {
        return new VisionNodeEntry(nodeId, new QualifiedName(name), new LocalizedText(name), new NodeId(1));
    }

    private sealed class TestReader : IVisionCompanionReader
    {
        public CellProviderTestEntries<VisionNodeEntry> Sensors { get; init; } = new([]);

        public CellProviderTestEntries<VisionNodeEntry> Frames { get; init; } = new([]);

        public CellProviderTestEntries<VisionNodeEntry> Pipelines { get; init; } = new([]);

        public CellProviderTestEntries<VisionNodeEntry> Results { get; init; } = new([]);

        public VisionResultKind Kind { get; init; }

        public VisionFrameSnapshot Frame { get; init; } = new() { NodeId = s_frame };

        public VisionDetectionResultSnapshot Detection { get; init; } = new() { NodeId = s_result };

        public VisionInspectionResultSnapshot Inspection { get; init; } = new() { NodeId = s_result };

        public VisionSegmentationResultSnapshot Segmentation { get; init; } = new() { NodeId = s_result };

        public Func<CancellationToken, Task<VisionPipelineSnapshot>>? ReadPipeline { get; init; }

        public NodeId ResultPipeline { get; private set; }

        public NodeId ResultNode { get; private set; }

        public int Reads { get; private set; }

        public IAsyncEnumerable<VisionNodeEntry> EnumerateSensorsAsync(CancellationToken cancellationToken) => Sensors;

        public IAsyncEnumerable<VisionNodeEntry> EnumerateFramesAsync(CancellationToken cancellationToken) => Frames;

        public IAsyncEnumerable<VisionNodeEntry> EnumeratePipelinesAsync(CancellationToken cancellationToken)
        {
            return Pipelines;
        }

        public IAsyncEnumerable<VisionNodeEntry> EnumerateResultsAsync(
            NodeId pipeline,
            CancellationToken cancellationToken)
        {
            ResultPipeline = pipeline;
            return Results;
        }

        public Task<VisionSensorIdentity> ReadSensorAsync(NodeId sensor, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(new VisionSensorIdentity { NodeId = sensor });
        }

        public Task<VisionImageSensorSnapshot?> ReadImageMembersAsync(
            NodeId sensor,
            CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<VisionImageSensorSnapshot?>(null);
        }

        public Task<VisionDepth3DSensorSnapshot?> ReadDepthMembersAsync(
            NodeId sensor,
            CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<VisionDepth3DSensorSnapshot?>(null);
        }

        public Task<VisionFrameSnapshot> ReadFrameAsync(NodeId frame, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Frame);
        }

        public Task<VisionPipelineSnapshot> ReadPipelineAsync(NodeId pipeline, CancellationToken cancellationToken)
        {
            Reads++;
            return ReadPipeline?.Invoke(cancellationToken) ??
                Task.FromResult(new VisionPipelineSnapshot { NodeId = pipeline });
        }

        public Task<VisionResultKind> DetermineResultKindAsync(NodeId result, CancellationToken cancellationToken)
        {
            Reads++;
            ResultNode = result;
            return Task.FromResult(Kind);
        }

        public Task<VisionDetectionResultSnapshot> ReadDetectionAsync(
            NodeId result,
            CancellationToken cancellationToken)
        {
            Reads++;
            ResultNode = result;
            return Task.FromResult(Detection);
        }

        public Task<VisionInspectionResultSnapshot> ReadInspectionAsync(
            NodeId result,
            CancellationToken cancellationToken)
        {
            Reads++;
            ResultNode = result;
            return Task.FromResult(Inspection);
        }

        public Task<VisionSegmentationResultSnapshot> ReadSegmentationAsync(
            NodeId result, CancellationToken cancellationToken)
        {
            Reads++;
            ResultNode = result;
            return Task.FromResult(Segmentation);
        }
    }

    private static readonly double[] s_expectedPosition = [1d, 2d, 3d];
    private static readonly double[] s_expectedOrientation = [0d, 0d, 0d, 1d];
    private static readonly NodeId s_sensor = new("sensor", 2);
    private static readonly NodeId s_frame = new("frame", 2);
    private static readonly NodeId s_pipeline = new("pipeline", 2);
    private static readonly NodeId s_result = new("result", 2);
}
