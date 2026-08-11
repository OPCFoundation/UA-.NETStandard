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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Tests for <see cref="VisionPipelineClient"/> and
    /// <see cref="VisionFeedbackClient"/> through the harness.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionPipelineClientTests
    {
        [Test]
        public async Task ReadReturnsSnapshotWithPipelineIdAndState()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();
            harness.AddValueChild(harness.PipelineNodeId, BrowseNames.PipelineId,
                new(3010u, 3), "pipeline-1");
            harness.AddValueChild(harness.PipelineNodeId, BrowseNames.State,
                new(3011u, 3), (int)VisionEndpointStateEnum.Active);
            harness.AddValueChild(harness.PipelineNodeId, BrowseNames.Continuous,
                new(3012u, 3), true);
            harness.AddValueChild(harness.PipelineNodeId, BrowseNames.Sensor,
                new(3013u, 3), harness.SensorNodeId);

            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionPipelineSnapshot snapshot = await pipeline.ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.PipelineId, Is.EqualTo("pipeline-1"));
                Assert.That(snapshot.State, Is.EqualTo(VisionEndpointStateEnum.Active));
                Assert.That(snapshot.Continuous, Is.True);
                Assert.That(snapshot.SensorId, Is.EqualTo(harness.SensorNodeId));
                Assert.That(snapshot.NodeId, Is.EqualTo(harness.PipelineNodeId));
            });
        }

        [Test]
        public async Task ReadStateReturnsCurrentState()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();
            harness.AddValueChild(harness.PipelineNodeId, BrowseNames.State,
                new(3020u, 3), (int)VisionEndpointStateEnum.Ready);

            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionEndpointStateEnum state = await pipeline.ReadStateAsync()
                .ConfigureAwait(false);

            Assert.That(state, Is.EqualTo(VisionEndpointStateEnum.Ready));
        }

        [Test]
        public async Task ReadStateReturnsDefaultWhenStateNodeAbsent()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();

            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionEndpointStateEnum state = await pipeline.ReadStateAsync()
                .ConfigureAwait(false);

            Assert.That(state, Is.EqualTo(default(VisionEndpointStateEnum)));
        }

        [Test]
        public async Task RunInferenceReturnsServerResultId()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();
            harness.ConfigureCall(StatusCodes.Good, new Variant("result-42"));

            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            string resultId = await pipeline.RunInferenceAsync().ConfigureAwait(false);

            Assert.That(resultId, Is.EqualTo("result-42"));
        }

        [Test]
        public async Task StartContinuousDoesNotThrowOnGoodCall()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();
            harness.ConfigureCall(StatusCodes.Good);

            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);

            Assert.DoesNotThrowAsync(async () =>
                await pipeline.StartContinuousAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task StopDoesNotThrowOnGoodCall()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();
            harness.ConfigureCall(StatusCodes.Good);

            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);

            Assert.DoesNotThrowAsync(async () =>
                await pipeline.StopAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task EnumerateResultsYieldsInferenceResultsFromFolder()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();
            harness.AddChild(harness.PipelineNodeId, BrowseNames.Results,
                harness.ResultsFolderId);
            harness.AddBrowse(harness.ResultsFolderId,
                [harness.Ref(harness.InferenceResultNodeId, "R1",
                    ObjectTypes.DetectionResultType)]);

            var entries = new List<VisionNodeEntry>();
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            await foreach (VisionNodeEntry entry in pipeline.EnumerateResultsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].NodeId, Is.EqualTo(harness.InferenceResultNodeId));
        }

        [Test]
        public async Task EnumerateResultsYieldsNothingWhenResultsFolderAbsent()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();

            var entries = new List<VisionNodeEntry>();
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            await foreach (VisionNodeEntry entry in pipeline.EnumerateResultsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task OpenFeedbackReturnsNullWhenNoFeedbackObject()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();

            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionFeedbackClient? feedback = await pipeline.OpenFeedbackAsync()
                .ConfigureAwait(false);

            Assert.That(feedback, Is.Null);
        }

        [Test]
        public async Task OpenFeedbackReturnsClientWhenFeedbackObjectPresent()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();
            harness.AddChild(harness.PipelineNodeId, BrowseNames.Feedback,
                harness.FeedbackNodeId);

            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionFeedbackClient? feedback = await pipeline.OpenFeedbackAsync()
                .ConfigureAwait(false);

            Assert.That(feedback, Is.Not.Null);
            Assert.That(feedback!.FeedbackNodeId, Is.EqualTo(harness.FeedbackNodeId));
        }

        [Test]
        public void ConstructorRejectsNullPipelineNodeId()
        {
            var harness = new VisionSessionHarness();

            Assert.Throws<ArgumentException>(() =>
                harness.Client.Pipeline(NodeId.Null));
        }
    }

    /// <summary>
    /// Tests for <see cref="VisionFeedbackClient"/> pre-flight argument
    /// validation. Post-flight validation (server refusal) is covered by the
    /// dispatcher-level tests.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionFeedbackClientTests
    {
        [Test]
        public async Task SubmitDetectionsRejectsEmptyDetections()
        {
            VisionFeedbackClient feedback = await BuildFeedbackAsync().ConfigureAwait(false);

            // Part 9.5 pairs the array with the flag: an empty array without
            // SceneIsEmpty is a lost payload rather than an observation, so the
            // wrapper refuses it before the call leaves the client.
            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await feedback.SubmitDetectionsAsync(
                    VisionFeedbackPurposeEnum.Overlay,
                    ArrayOf<VisionDetectionDataType>.Empty,
                    null,
                    ByteString.Empty).ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("detections"));
        }

        [Test]
        public async Task SubmitDetectionsRejectsSceneIsEmptyWithDetectionsAttached()
        {
            VisionFeedbackClient feedback = await BuildFeedbackAsync().ConfigureAwait(false);

            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await feedback.SubmitDetectionsAsync(
                    VisionFeedbackPurposeEnum.GroundTruthLabel,
                    new[] { new VisionDetectionDataType { ClassLabel = "Part" } }.ToArrayOf(),
                    null,
                    ByteString.Empty,
                    sceneIsEmpty: true).ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("detections"));
        }

        [Test]
        public async Task SubmitCorrectionRejectsRetractAllWithAReplacementAttached()
        {
            VisionFeedbackClient feedback = await BuildFeedbackAsync().ConfigureAwait(false);

            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await feedback.SubmitCorrectionAsync(
                    "r-1",
                    VisionFeedbackPurposeEnum.GroundTruthLabel,
                    new[] { new VisionDetectionDataType { ClassLabel = "Part" } }.ToArrayOf(),
                    ArrayOf<VisionCharacteristicDataType>.Empty,
                    default,
                    ByteString.Empty,
                    retractAll: true).ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("correctedDetections"));
        }

        [Test]
        public async Task SubmitInspectionResultRejectsEmptyResultId()
        {
            VisionFeedbackClient feedback = await BuildFeedbackAsync().ConfigureAwait(false);
            var characteristics = new List<VisionCharacteristicDataType> { new() }.ToArrayOf();

            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await feedback.SubmitInspectionResultAsync(
                    string.Empty,
                    VisionResultEvaluationEnum.Ok,
                    characteristics).ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("resultId"));
        }

        [Test]
        public async Task SubmitInspectionResultRejectsEmptyCharacteristics()
        {
            VisionFeedbackClient feedback = await BuildFeedbackAsync().ConfigureAwait(false);

            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await feedback.SubmitInspectionResultAsync(
                    "r-1",
                    VisionResultEvaluationEnum.Ok,
                    ArrayOf<VisionCharacteristicDataType>.Empty).ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("characteristics"));
        }

        [Test]
        public async Task SubmitCorrectionRejectsEmptyResultId()
        {
            VisionFeedbackClient feedback = await BuildFeedbackAsync().ConfigureAwait(false);
            var detections = new List<VisionDetectionDataType> { new() }.ToArrayOf();

            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await feedback.SubmitCorrectionAsync(
                    string.Empty,
                    VisionFeedbackPurposeEnum.GroundTruthLabel,
                    detections,
                    ArrayOf<VisionCharacteristicDataType>.Empty,
                    LocalizedText.Null,
                    ByteString.Empty).ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("resultId"));
        }

        [Test]
        public async Task SubmitCorrectionRejectsBothDetectionsAndCharacteristicsSupplied()
        {
            VisionFeedbackClient feedback = await BuildFeedbackAsync().ConfigureAwait(false);
            var detections = new List<VisionDetectionDataType> { new() }.ToArrayOf();
            var characteristics = new List<VisionCharacteristicDataType> { new() }.ToArrayOf();

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await feedback.SubmitCorrectionAsync(
                    "r-1",
                    VisionFeedbackPurposeEnum.GroundTruthLabel,
                    detections,
                    characteristics,
                    LocalizedText.Null,
                    ByteString.Empty).ConfigureAwait(false));
        }

        [Test]
        public async Task SubmitCorrectionRejectsBothDetectionsAndCharacteristicsEmpty()
        {
            VisionFeedbackClient feedback = await BuildFeedbackAsync().ConfigureAwait(false);

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await feedback.SubmitCorrectionAsync(
                    "r-1",
                    VisionFeedbackPurposeEnum.GroundTruthLabel,
                    ArrayOf<VisionDetectionDataType>.Empty,
                    ArrayOf<VisionCharacteristicDataType>.Empty,
                    LocalizedText.Null,
                    ByteString.Empty).ConfigureAwait(false));
        }

        [Test]
        public async Task SubmitImageReferenceRejectsNullImage()
        {
            VisionFeedbackClient feedback = await BuildFeedbackAsync().ConfigureAwait(false);

            var ex = Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await feedback.SubmitImageReferenceAsync(
                    VisionFeedbackPurposeEnum.GroundTruthLabel,
                    null!,
                    "r-1").ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("image"));
        }

        [Test]
        public async Task SubmitDetectionsSuccessfulForwardsCallToServer()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();
            harness.AddChild(harness.PipelineNodeId, BrowseNames.Feedback,
                harness.FeedbackNodeId);
            harness.ConfigureCall(StatusCodes.Good);

            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionFeedbackClient? feedback = await pipeline.OpenFeedbackAsync()
                .ConfigureAwait(false);
            Assert.That(feedback, Is.Not.Null);
            var detections = new List<VisionDetectionDataType> { new() }.ToArrayOf();

            Assert.DoesNotThrowAsync(async () =>
                await feedback!.SubmitDetectionsAsync(
                    VisionFeedbackPurposeEnum.Overlay,
                    detections,
                    null,
                    ByteString.Empty).ConfigureAwait(false));
        }

        private static async Task<VisionFeedbackClient> BuildFeedbackAsync()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline();
            harness.AddChild(harness.PipelineNodeId, BrowseNames.Feedback,
                harness.FeedbackNodeId);
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionFeedbackClient? feedback = await pipeline.OpenFeedbackAsync()
                .ConfigureAwait(false);
            return feedback!;
        }
    }
}
