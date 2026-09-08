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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Tests for <see cref="VisionInferenceService"/>, <see cref="VisionInferenceResult"/>,
    /// and the summary/handle-only detail semantics.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionInferenceServiceTests
    {
        [Test]
        public void InferenceFactoryReturnsNonNullService()
        {
            var harness = new VisionSessionHarness();
            VisionInferenceService service = harness.Client.Inference();
            Assert.That(service, Is.Not.Null);
        }

        [Test]
        public async Task RunOneShotReturnsHandleOnlyWithoutReadingPayload()
        {
            var harness = new VisionSessionHarness();
            SetupMinimalDetectionPipeline(harness);

            VisionInferenceService service = harness.Client.Inference();
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionInferenceResult result = await service.RunOneShotAsync(
                pipeline,
                "Pipeline1",
                VisionResultDetail.HandleOnly,
                VisionExpectedResultKind.Auto,
                maxItems: 10).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ResultId, Is.EqualTo("result-42"));
                Assert.That(result.Resolved, Is.True);
                Assert.That(result.RequestedPipelineName, Is.EqualTo("Pipeline1"));
                Assert.That(result.RequestedPipelineNodeId, Is.EqualTo(harness.PipelineNodeId));
                Assert.That(result.DetectionSummary, Is.Null,
                    "HandleOnly must not read a summary.");
                Assert.That(result.InspectionSummary, Is.Null);
                Assert.That(result.SegmentationSummary, Is.Null);
            });
        }

        [Test]
        public async Task RunOneShotDetectionSummaryBoundsItems()
        {
            var harness = new VisionSessionHarness();
            SetupMinimalDetectionPipeline(harness);
            SetupDetectionResultChildren(harness);

            VisionInferenceService service = harness.Client.Inference();
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionInferenceResult result = await service.RunOneShotAsync(
                pipeline,
                "Pipeline1",
                VisionResultDetail.Summary,
                VisionExpectedResultKind.Auto,
                maxItems: 3).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ResultKind, Is.EqualTo(VisionResultKind.Detection));
                Assert.That(result.DetectionSummary, Is.Not.Null);
                Assert.That(result.DetectionSummary!.Items.Count, Is.LessThanOrEqualTo(3));
            });
        }

        [Test]
        public async Task RunOneShotDetectionSummaryReadsAllFieldsWhenUnbounded()
        {
            var harness = new VisionSessionHarness();
            SetupMinimalDetectionPipeline(harness);
            SetupDetectionResultChildren(harness);

            VisionInferenceService service = harness.Client.Inference();
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionInferenceResult result = await service.RunOneShotAsync(
                pipeline,
                "Pipeline1",
                VisionResultDetail.Summary,
                VisionExpectedResultKind.Auto,
                maxItems: 100).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ResultId, Is.EqualTo("result-42"));
                Assert.That(result.Resolved, Is.True);
                Assert.That(result.ResultKind, Is.EqualTo(VisionResultKind.Detection));
                Assert.That(result.DetectionSummary, Is.Not.Null);
                Assert.That(result.DetectionSummary!.FrameId, Is.EqualTo("world"));
                Assert.That(result.DetectionSummary.ModelVersionUsed, Is.EqualTo("v1.0"));
                Assert.That(result.RequestedPipelineNodeId, Is.EqualTo(harness.PipelineNodeId));
                Assert.That(result.PipelineId, Is.EqualTo(new NodeId(3002u, 3)));
                Assert.That(result.SensorId, Is.EqualTo(harness.SensorNodeId));
                Assert.That(result.FrameId, Is.EqualTo("world"),
                    "Provenance FrameId propagated to result.");
                Assert.That(result.ModelVersionUsed, Is.EqualTo("v1.0"),
                    "Provenance ModelVersionUsed propagated to result.");
            });
        }

        [Test]
        public void RunOneShotThrowsOnExpectedKindMismatchWhenAuthoritative()
        {
            var harness = new VisionSessionHarness();
            SetupMinimalDetectionPipeline(harness);

            VisionInferenceService service = harness.Client.Inference();
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.RunOneShotAsync(
                    pipeline,
                    "Pipeline1",
                    VisionResultDetail.Summary,
                    VisionExpectedResultKind.Inspection,
                    maxItems: 10).ConfigureAwait(false));

            Assert.That(ex!.Message, Does.Contain("Expected result kind 'Inspection'"));
        }

        [Test]
        public async Task RunOneShotAcceptsMatchingExpectedKind()
        {
            var harness = new VisionSessionHarness();
            SetupMinimalDetectionPipeline(harness);
            SetupDetectionResultChildren(harness);

            VisionInferenceService service = harness.Client.Inference();
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionInferenceResult result = await service.RunOneShotAsync(
                pipeline,
                "Pipeline1",
                VisionResultDetail.Summary,
                VisionExpectedResultKind.Detection,
                maxItems: 10).ConfigureAwait(false);

            Assert.That(result.ResultKind, Is.EqualTo(VisionResultKind.Detection));
        }

        [Test]
        public void RunOneShotRejectsNullPipeline()
        {
            var harness = new VisionSessionHarness();
            VisionInferenceService service = harness.Client.Inference();

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await service.RunOneShotAsync(
                    null!,
                    "Pipeline1",
                    VisionResultDetail.Summary,
                    VisionExpectedResultKind.Auto,
                    maxItems: 10).ConfigureAwait(false));
        }

        [Test]
        public void RunOneShotRejectsNegativeMaxItems()
        {
            var harness = new VisionSessionHarness();
            SetupMinimalDetectionPipeline(harness);

            VisionInferenceService service = harness.Client.Inference();
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await service.RunOneShotAsync(
                    pipeline,
                    "Pipeline1",
                    VisionResultDetail.Summary,
                    VisionExpectedResultKind.Auto,
                    maxItems: -1).ConfigureAwait(false));
        }

        [Test]
        public void RunOneShotRejectsMaxItemsAbove100()
        {
            var harness = new VisionSessionHarness();
            SetupMinimalDetectionPipeline(harness);

            VisionInferenceService service = harness.Client.Inference();
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await service.RunOneShotAsync(
                    pipeline,
                    "Pipeline1",
                    VisionResultDetail.Summary,
                    VisionExpectedResultKind.Auto,
                    maxItems: 101).ConfigureAwait(false));
        }

        [Test]
        public void RunOneShotRejectsUndefinedExpectedKind()
        {
            var harness = new VisionSessionHarness();
            SetupMinimalDetectionPipeline(harness);

            VisionInferenceService service = harness.Client.Inference();
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await service.RunOneShotAsync(
                    pipeline,
                    "Pipeline1",
                    VisionResultDetail.Summary,
                    (VisionExpectedResultKind)99,
                    maxItems: 10).ConfigureAwait(false));
        }

        [Test]
        public void RunOneShotRejectsUndefinedDetail()
        {
            var harness = new VisionSessionHarness();
            SetupMinimalDetectionPipeline(harness);

            VisionInferenceService service = harness.Client.Inference();
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await service.RunOneShotAsync(
                    pipeline,
                    "Pipeline1",
                    (VisionResultDetail)99,
                    VisionExpectedResultKind.Auto,
                    maxItems: 10).ConfigureAwait(false));
        }

        [Test]
        public async Task RunOneShotUnresolvedResultReturnsHandleWithResolvedFalse()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();
            harness.ConfigureCall(StatusCodes.Good, new Variant("result-orphan"));

            VisionInferenceService service = harness.Client.Inference();
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionInferenceResult result = await service.RunOneShotAsync(
                pipeline,
                "Pipeline1",
                VisionResultDetail.Summary,
                VisionExpectedResultKind.Auto,
                maxItems: 10).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ResultId, Is.EqualTo("result-orphan"));
                Assert.That(result.Resolved, Is.False);
                Assert.That(result.ResultNodeId.IsNull, Is.True);
                Assert.That(result.ResultKind, Is.EqualTo(VisionResultKind.Unknown));
                Assert.That(result.DetectionSummary, Is.Null);
            });
        }

        [Test]
        public async Task RunOneShotUnresolvedResultReturnsHandleEvenWithExpectedKind()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();
            harness.ConfigureCall(StatusCodes.Good, new Variant("result-orphan"));

            VisionInferenceService service = harness.Client.Inference();
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionInferenceResult result = await service.RunOneShotAsync(
                pipeline,
                "Pipeline1",
                VisionResultDetail.Summary,
                VisionExpectedResultKind.Detection,
                maxItems: 10).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Resolved, Is.False,
                    "Unresolved result should not throw even with expectedKind set.");
                Assert.That(result.ResultKind, Is.EqualTo(VisionResultKind.Unknown));
                Assert.That(result.DetectionSummary, Is.Null,
                    "Unresolved result returns handle-only.");
            });
        }

        [Test]
        public async Task RunOneShotThrowsWhenResolvedKindCannotBeDeterminedForConcreteExpectedKind()
        {
            var harness = new VisionSessionHarness();
            SetupMinimalDetectionPipeline(harness);
            harness.NodeCache.Reset();
            harness.NodeCache
                .Setup(c => c.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId _, NodeId type, CancellationToken _) =>
                    new ValueTask<bool>(
                        type == new NodeId(
                            ObjectTypes.VisionResultType,
                            harness.VisionNamespaceIndex)));

            VisionInferenceService service = harness.Client.Inference();
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionResultKind kind = await service.DetermineResultKindAsync(
                harness.InferenceResultNodeId).ConfigureAwait(false);
            Assert.That(kind, Is.EqualTo(VisionResultKind.Unknown));
            VisionInferenceResult autoResult = await service.RunOneShotAsync(
                pipeline,
                "Pipeline1",
                VisionResultDetail.HandleOnly,
                VisionExpectedResultKind.Auto,
                maxItems: 10).ConfigureAwait(false);
            Assert.That(autoResult.Resolved, Is.True);
            Assert.That(autoResult.ResultKind, Is.EqualTo(VisionResultKind.Unknown));

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.RunOneShotAsync(
                    pipeline,
                    "Pipeline1",
                    VisionResultDetail.Summary,
                    VisionExpectedResultKind.Detection,
                    maxItems: 10).ConfigureAwait(false));

            Assert.That(ex!.Message, Does.Contain("Cannot determine result kind"));
        }

        [Test]
        public async Task DetermineResultKindReturnsDetection()
        {
            var harness = new VisionSessionHarness();
            SetupResultTypeDefinition(harness, ObjectTypes.DetectionResultType);

            VisionInferenceService service = harness.Client.Inference();
            VisionResultKind kind = await service.DetermineResultKindAsync(
                harness.InferenceResultNodeId).ConfigureAwait(false);

            Assert.That(kind, Is.EqualTo(VisionResultKind.Detection));
        }

        [Test]
        public async Task DetermineResultKindReturnsUnknownForNullNodeId()
        {
            var harness = new VisionSessionHarness();

            VisionInferenceService service = harness.Client.Inference();
            VisionResultKind kind = await service.DetermineResultKindAsync(
                NodeId.Null).ConfigureAwait(false);

            Assert.That(kind, Is.EqualTo(VisionResultKind.Unknown));
        }

        [Test]
        public async Task DetermineResultKindDetectsSubtype()
        {
            var harness = new VisionSessionHarness();
            const uint vendorSubtype = 99999;
            harness.AddBrowse(harness.InferenceResultNodeId,
                [new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(
                        new NodeId(vendorSubtype, harness.VisionNamespaceIndex)),
                    BrowseName = new QualifiedName("VendorDetectionResultType",
                        harness.VisionNamespaceIndex),
                    DisplayName = new LocalizedText("VendorDetectionResultType"),
                    NodeClass = NodeClass.ObjectType,
                    TypeDefinition = ExpandedNodeId.Null,
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasTypeDefinition,
                    IsForward = true
                }]);

            VisionInferenceService service = harness.Client.Inference();
            VisionResultKind kind = await service.DetermineResultKindAsync(
                harness.InferenceResultNodeId).ConfigureAwait(false);

            Assert.That(kind, Is.EqualTo(VisionResultKind.Detection),
                "IsTypeOfAsync returns true for subtypes, so vendor-derived " +
                "types should resolve to the base Vision kind.");
        }

        [Test]
        public async Task DetermineResultKindReturnsUnknownForZeroRefs()
        {
            var harness = new VisionSessionHarness();
            harness.AddBrowse(harness.InferenceResultNodeId, []);

            VisionInferenceService service = harness.Client.Inference();
            VisionResultKind kind = await service.DetermineResultKindAsync(
                harness.InferenceResultNodeId).ConfigureAwait(false);

            Assert.That(kind, Is.EqualTo(VisionResultKind.Unknown));
        }

        [Test]
        public void DetermineResultKindThrowsOnMultipleTypeDefinitions()
        {
            var harness = new VisionSessionHarness();
            harness.AddBrowse(harness.InferenceResultNodeId,
            [
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(
                        new NodeId(ObjectTypes.DetectionResultType,
                            harness.VisionNamespaceIndex)),
                    BrowseName = new QualifiedName("DetectionResultType",
                        harness.VisionNamespaceIndex),
                    DisplayName = new LocalizedText("DetectionResultType"),
                    NodeClass = NodeClass.ObjectType,
                    TypeDefinition = ExpandedNodeId.Null,
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasTypeDefinition,
                    IsForward = true
                },
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(
                        new NodeId(ObjectTypes.InspectionResultType,
                            harness.VisionNamespaceIndex)),
                    BrowseName = new QualifiedName("InspectionResultType",
                        harness.VisionNamespaceIndex),
                    DisplayName = new LocalizedText("InspectionResultType"),
                    NodeClass = NodeClass.ObjectType,
                    TypeDefinition = ExpandedNodeId.Null,
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasTypeDefinition,
                    IsForward = true
                }
            ]);

            VisionInferenceService service = harness.Client.Inference();
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.DetermineResultKindAsync(harness.InferenceResultNodeId)
                    .ConfigureAwait(false));
        }

        [Test]
        public void VisionResultKindEnumHasExpectedValues()
        {
            Assert.Multiple(() =>
            {
                Assert.That((int)VisionResultKind.Unknown, Is.EqualTo(0));
                Assert.That((int)VisionResultKind.Detection, Is.EqualTo(1));
                Assert.That((int)VisionResultKind.Inspection, Is.EqualTo(2));
                Assert.That((int)VisionResultKind.Segmentation, Is.EqualTo(3));
            });
        }

        [Test]
        public void VisionExpectedResultKindEnumHasExpectedValues()
        {
            Assert.Multiple(() =>
            {
                Assert.That((int)VisionExpectedResultKind.Auto, Is.EqualTo(0));
                Assert.That((int)VisionExpectedResultKind.Detection, Is.EqualTo(1));
                Assert.That((int)VisionExpectedResultKind.Inspection, Is.EqualTo(2));
                Assert.That((int)VisionExpectedResultKind.Segmentation, Is.EqualTo(3));
            });
        }

        [Test]
        public void VisionResultDetailEnumHasExpectedValues()
        {
            Assert.Multiple(() =>
            {
                Assert.That((int)VisionResultDetail.Summary, Is.EqualTo(0));
                Assert.That((int)VisionResultDetail.HandleOnly, Is.EqualTo(1));
            });
        }

        [Test]
        public void VisionDetectionSummaryRecordRoundTrips()
        {
            var summary = new VisionDetectionSummary
            {
                CreationTime = new DateTimeUtc(new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc)),
                ModelVersionUsed = "v2.1",
                FrameId = "world",
                TotalDetections = 3,
                Items = new VisionDetectionItem[]
                {
                    new()
                    {
                        DetectionId = "det-1",
                        ClassLabel = "RedCube",
                        ClassId = 1,
                        Confidence = 0.95,
                        HasPose = false
                    }
                }.ToArrayOf()
            };

            Assert.Multiple(() =>
            {
                Assert.That(summary.TotalDetections, Is.EqualTo(3));
                Assert.That(summary.Items.Count, Is.EqualTo(1));
                Assert.That(summary.Items[0].DetectionId, Is.EqualTo("det-1"));
                Assert.That(summary.Items[0].ClassLabel, Is.EqualTo("RedCube"));
                Assert.That(summary.Items[0].Confidence, Is.EqualTo(0.95));
                Assert.That(summary.Items[0].HasPose, Is.False);
                Assert.That(summary.Items[0].Pose, Is.Null);
                Assert.That(summary.ModelVersionUsed, Is.EqualTo("v2.1"));
                Assert.That(summary.FrameId, Is.EqualTo("world"));
            });
        }

        [Test]
        public void VisionDetectionSummaryRetainsFullPoseForDirectConsumers()
        {
            var pose = new VisionPose3DDataType
            {
                FrameId = "world"
            };
            var summary = new VisionDetectionSummary
            {
                Items = new VisionDetectionItem[]
                {
                    new()
                    {
                        DetectionId = "det-pose",
                        HasPose = true,
                        Pose = pose
                    }
                }.ToArrayOf()
            };

            Assert.That(summary.Items[0].Pose, Is.SameAs(pose));
        }

        [Test]
        public void VisionInspectionSummaryRecordRoundTrips()
        {
            var summary = new VisionInspectionSummary
            {
                CreationTime = new DateTimeUtc(new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc)),
                Evaluation = VisionResultEvaluationEnum.Ok,
                PartId = "part-A",
                RecipeId = "recipe-1",
                TotalCharacteristics = 2,
                Items = new VisionCharacteristicItem[]
                {
                    new()
                    {
                        Name = "diameter",
                        Status = VisionToleranceStatusEnum.InTolerance,
                        Deviation = 0.01
                    }
                }.ToArrayOf()
            };

            Assert.Multiple(() =>
            {
                Assert.That(summary.Evaluation, Is.EqualTo(VisionResultEvaluationEnum.Ok));
                Assert.That(summary.PartId, Is.EqualTo("part-A"));
                Assert.That(summary.RecipeId, Is.EqualTo("recipe-1"));
                Assert.That(summary.TotalCharacteristics, Is.EqualTo(2));
                Assert.That(summary.Items.Count, Is.EqualTo(1));
                Assert.That(summary.Items[0].Name, Is.EqualTo("diameter"));
            });
        }

        [Test]
        public void VisionSegmentationSummaryRecordRoundTrips()
        {
            var summary = new VisionSegmentationSummary
            {
                CreationTime = new DateTimeUtc(new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc)),
                LabelClasses = s_stringValues1.ToArrayOf(),
                MaskWidth = 640,
                MaskHeight = 480,
                MaskFormat = "Mono8"
            };

            Assert.Multiple(() =>
            {
                Assert.That(summary.LabelClasses.Count, Is.EqualTo(2));
                Assert.That(summary.MaskWidth, Is.EqualTo(640));
                Assert.That(summary.MaskHeight, Is.EqualTo(480));
                Assert.That(summary.MaskFormat, Is.EqualTo("Mono8"));
            });
        }

        [Test]
        public void VisionInferenceResultRecordRequiresResultIdAndRequestedPipelineNodeId()
        {
            var result = new VisionInferenceResult
            {
                ResultId = "test-result",
                RequestedPipelineNodeId = new NodeId(1u, 2)
            };

            Assert.Multiple(() =>
            {
                Assert.That(result.ResultId, Is.EqualTo("test-result"));
                Assert.That(result.RequestedPipelineNodeId, Is.EqualTo(new NodeId(1u, 2)));
                Assert.That(result.Resolved, Is.False);
                Assert.That(result.ResultKind, Is.EqualTo(VisionResultKind.Unknown));
                Assert.That(result.PipelineId.IsNull, Is.True);
                Assert.That(result.SensorId.IsNull, Is.True);
                Assert.That(result.ModelVersionUsed, Is.Null);
                Assert.That(result.FrameId, Is.Null);
                Assert.That(result.DetectionSummary, Is.Null);
                Assert.That(result.InspectionSummary, Is.Null);
                Assert.That(result.SegmentationSummary, Is.Null);
            });
        }

        [Test]
        public async Task ResolvePipelineByNameReturnsMatchingEntry()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline("BinPickingPipeline");

            VisionNodeEntry entry = await harness.Client.ResolvePipelineAsync(
                "BinPickingPipeline").ConfigureAwait(false);

            Assert.That(entry.BrowseName.Name, Is.EqualTo("BinPickingPipeline"));
        }

        [Test]
        public async Task ResolvePipelineByNodeIdReturnsMatchingEntry()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline("Pipeline1");

            VisionNodeEntry entry = await harness.Client.ResolvePipelineAsync(
                harness.PipelineNodeId.ToString()).ConfigureAwait(false);

            Assert.That(entry.NodeId, Is.EqualTo(harness.PipelineNodeId));
        }

        [Test]
        public async Task ResolvePipelineByDisplayNameReturnsEntry()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline("MyPipeline");

            VisionNodeEntry entry = await harness.Client.ResolvePipelineAsync(
                "MyPipeline").ConfigureAwait(false);

            Assert.That(entry.DisplayName.Text, Is.EqualTo("MyPipeline"));
        }

        [Test]
        public async Task ResolvePipelineTrimsWhitespace()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline("BinPicking");

            VisionNodeEntry entry = await harness.Client.ResolvePipelineAsync(
                "  BinPicking  ").ConfigureAwait(false);

            Assert.That(entry.BrowseName.Name, Is.EqualTo("BinPicking"));
        }

        [Test]
        public void ResolvePipelineIsCaseSensitive()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline("BinPicking");

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await harness.Client.ResolvePipelineAsync("binpicking")
                    .ConfigureAwait(false));
        }

        [Test]
        public void ResolvePipelineRejectsNullOrWhitespace()
        {
            var harness = new VisionSessionHarness();

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await harness.Client.ResolvePipelineAsync(string.Empty)
                    .ConfigureAwait(false));

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await harness.Client.ResolvePipelineAsync("   ")
                    .ConfigureAwait(false));
        }

        [Test]
        public void ResolvePipelineThrowsWhenNotFound()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline("Alpha");

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await harness.Client.ResolvePipelineAsync("Beta")
                    .ConfigureAwait(false));

            Assert.That(ex!.Message, Does.Contain("not found"));
            Assert.That(ex.Message, Does.Contain("Alpha"));
        }

        [Test]
        public void ResolvePipelineErrorsListBrowseNameDisplayNameAndNodeId()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            ReferenceDescription entry = harness.Ref(
                harness.PipelineNodeId,
                "PipelineBrowseName",
                ObjectTypes.InferencePipelineType);
            entry.DisplayName = new LocalizedText("Pipeline Display Name");
            harness.AddBrowse(harness.PipelinesFolderId, [entry]);

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await harness.Client.ResolvePipelineAsync("missing").ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Message, Does.Contain("BrowseName='PipelineBrowseName'"));
                Assert.That(ex.Message, Does.Contain("DisplayName='Pipeline Display Name'"));
                Assert.That(ex.Message, Does.Contain($"NodeId='{harness.PipelineNodeId}'"));
            });
        }

        [Test]
        public void ResolvePipelineThrowsOnAmbiguity()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            var secondPipelineId = new NodeId(3001u, 3);
            harness.AddBrowse(harness.PipelinesFolderId,
            [
                harness.Ref(harness.PipelineNodeId, "Dup",
                    ObjectTypes.InferencePipelineType),
                harness.Ref(secondPipelineId, "Dup",
                    ObjectTypes.InferencePipelineType)
            ]);

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await harness.Client.ResolvePipelineAsync("Dup")
                    .ConfigureAwait(false));

            Assert.That(ex!.Message, Does.Contain("Ambiguous"));
        }

        private static void SetupMinimalDetectionPipeline(VisionSessionHarness harness)
        {
            harness.ConfigureVisionFolders();
            harness.AddPipeline();
            harness.ConfigureCall(StatusCodes.Good, new Variant("result-42"));

            harness.AddChild(harness.PipelineNodeId, BrowseNames.Results,
                harness.ResultsFolderId);
            harness.AddBrowse(harness.ResultsFolderId,
                [harness.Ref(harness.InferenceResultNodeId, "result-42",
                    ObjectTypes.DetectionResultType)]);

            SetupResultTypeDefinition(harness, ObjectTypes.DetectionResultType);
        }

        private static void SetupDetectionResultChildren(
            VisionSessionHarness harness)
        {
            harness.AddValueChild(harness.InferenceResultNodeId, BrowseNames.ResultId,
                new(5000u, 3), "result-42");
            harness.AddValueChild(harness.InferenceResultNodeId, BrowseNames.CreationTime,
                new(5001u, 3), new DateTimeUtc(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            harness.AddValueChild(harness.InferenceResultNodeId, BrowseNames.Sensor,
                new(5004u, 3), harness.SensorNodeId);
            harness.AddValueChild(harness.InferenceResultNodeId, BrowseNames.Pipeline,
                new(5005u, 3), new NodeId(3002u, 3));
            harness.AddValueChild(harness.InferenceResultNodeId, BrowseNames.FrameId,
                new(5002u, 3), "world");
            harness.AddValueChild(harness.InferenceResultNodeId, BrowseNames.ModelVersionUsed,
                new(5003u, 3), "v1.0");
        }

        private static void SetupResultTypeDefinition(
            VisionSessionHarness harness, uint typeId)
        {
            harness.AddBrowse(harness.InferenceResultNodeId,
                [new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(
                        new NodeId(typeId, harness.VisionNamespaceIndex)),
                    BrowseName = new QualifiedName("DetectionResultType",
                        harness.VisionNamespaceIndex),
                    DisplayName = new LocalizedText("DetectionResultType"),
                    NodeClass = NodeClass.ObjectType,
                    TypeDefinition = ExpandedNodeId.Null,
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasTypeDefinition,
                    IsForward = true
                }]);
        }

        private static readonly string[] s_stringValues1 = ["background", "part"];
    }
}
