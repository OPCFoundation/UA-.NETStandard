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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.Intent.Tests.Infrastructure;

namespace Opc.Ua.Vision.Intent.Tests
{
    /// <summary>
    /// Proves the off-server pipeline validates agent submissions before
    /// publishing a result: unknown class labels, confidences outside
    /// [0..1], boxes entirely outside the image and zero-norm quaternions
    /// are refused with a status a client can act on. An empty detection
    /// set is accepted, because "the bin is empty" is a correct
    /// observation. An empty correction is accepted, because that is the
    /// false-positive retraction — the client-side <see cref="VisionFeedbackClient"/>
    /// helper guards that case, so the test bypasses the helper and
    /// invokes the wrapped <see cref="VisionFeedbackTypeClient"/> proxy
    /// directly to prove the Server itself accepts it.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class VisionSubmissionValidationTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new VisionIntentServerFixture(
                new VisionIntentServerFixture.Options { OffServer = true });
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SubmitDetectionsRejectsAnUnknownClassLabelWithBadInvalidArgument()
        {
            await using VisionIntentClientContext context = await m_fixture
                .ConnectAsync("val-unknown-class").ConfigureAwait(false);
            VisionFeedbackClient feedback = await OpenFeedbackAsync(context)
                .ConfigureAwait(false);
            VisionDetectionDataType detection = BuildBaseline();
            detection.ClassLabel = "NotAPart";

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await feedback.SubmitDetectionsAsync(
                    VisionFeedbackPurposeEnum.Reconciliation,
                    new[] { detection }.ToArrayOf(),
                    BuildFrameReference(),
                    ByteString.Empty).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument),
                "An unknown class label must be refused with Bad_InvalidArgument.");
        }

        [Test]
        public async Task SubmitDetectionsRejectsAConfidenceOutsideZeroOneWithBadInvalidArgument()
        {
            await using VisionIntentClientContext context = await m_fixture
                .ConnectAsync("val-confidence").ConfigureAwait(false);
            VisionFeedbackClient feedback = await OpenFeedbackAsync(context)
                .ConfigureAwait(false);
            VisionDetectionDataType detection = BuildBaseline();
            detection.Confidence = 1.5;

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await feedback.SubmitDetectionsAsync(
                    VisionFeedbackPurposeEnum.Reconciliation,
                    new[] { detection }.ToArrayOf(),
                    BuildFrameReference(),
                    ByteString.Empty).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument),
                "A confidence outside [0, 1] must be refused with Bad_InvalidArgument.");
        }

        [Test]
        public async Task SubmitDetectionsRejectsABoxEntirelyOutsideTheImageWithBadInvalidArgument()
        {
            await using VisionIntentClientContext context = await m_fixture
                .ConnectAsync("val-box").ConfigureAwait(false);
            VisionFeedbackClient feedback = await OpenFeedbackAsync(context)
                .ConfigureAwait(false);
            VisionDetectionDataType detection = BuildBaseline();
            detection.BoundingBox2D = new VisionBoundingBox2DDataType
            {
                CenterX = 1000.0,
                CenterY = 240.0,
                Width = 10.0,
                Height = 10.0,
                Rotation = 0.0
            };

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await feedback.SubmitDetectionsAsync(
                    VisionFeedbackPurposeEnum.Reconciliation,
                    new[] { detection }.ToArrayOf(),
                    BuildFrameReference(),
                    ByteString.Empty).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument),
                "A bounding box entirely outside the image must be refused with Bad_InvalidArgument.");
        }

        [Test]
        public async Task SubmitDetectionsRejectsAZeroNormQuaternionWithBadInvalidArgument()
        {
            await using VisionIntentClientContext context = await m_fixture
                .ConnectAsync("val-quat").ConfigureAwait(false);
            VisionFeedbackClient feedback = await OpenFeedbackAsync(context)
                .ConfigureAwait(false);
            VisionDetectionDataType detection = BuildBaseline();
            detection.Pose = new VisionPose3DDataType
            {
                FrameId = TestVisionCell.CameraFrameId,
                Position = s_doubleValues1.ToArrayOf(),
                Orientation = s_doubleValues2.ToArrayOf(),
                Covariance = ArrayOf<double>.Empty
            };

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await feedback.SubmitDetectionsAsync(
                    VisionFeedbackPurposeEnum.Reconciliation,
                    new[] { detection }.ToArrayOf(),
                    BuildFrameReference(),
                    ByteString.Empty).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument),
                "A zero-norm quaternion must be refused with Bad_InvalidArgument.");
        }

        [Test]
        public async Task SubmitDetectionsAcceptsAnEmptySetWhenSceneIsEmptyIsSet()
        {
            await using VisionIntentClientContext context = await m_fixture
                .ConnectAsync("val-empty-set").ConfigureAwait(false);
            NodeId pipelineNodeId = await ResolveSinglePipelineAsync(context).ConfigureAwait(false);
            VisionPipelineClient pipeline = context.Vision.Pipeline(pipelineNodeId);
            VisionFeedbackClient feedback = (await pipeline.OpenFeedbackAsync().ConfigureAwait(false))!;

            Assert.DoesNotThrowAsync(
                async () => await feedback.SubmitDetectionsAsync(
                    VisionFeedbackPurposeEnum.Reconciliation,
                    ArrayOf<VisionDetectionDataType>.Empty,
                    BuildFrameReference(),
                    ByteString.Empty,
                    sceneIsEmpty: true).ConfigureAwait(false),
                "An empty detection set must be accepted: an emptied bin is a correct answer.");
            List<VisionNodeEntry> results = new();
            await foreach (VisionNodeEntry entry in pipeline.EnumerateResultsAsync())
            {
                results.Add(entry);
            }
            Assert.That(results, Has.Count.EqualTo(1),
                "An accepted empty submission must still publish a DetectionResultType.");
            VisionDetectionResultSnapshot snapshot = await context.Vision
                .Result(results[0].NodeId).ReadDetectionAsync().ConfigureAwait(false);
            Assert.That(snapshot.Detections.Count, Is.Zero,
                "The published result must carry zero detections.");
        }

        [Test]
        public async Task SubmitCorrectionAcceptsAnEmptySetWhenRetractAllIsSet()
        {
            // The client helper VisionFeedbackClient.SubmitCorrectionAsync
            // guards this case client-side to keep the fluent API safe by
            // default; we bypass it to prove the Server itself accepts an
            // empty correction, because that is the design's false-positive
            // retraction case.
            await using VisionIntentClientContext context = await m_fixture
                .ConnectAsync("val-empty-correction").ConfigureAwait(false);
            NodeId pipelineNodeId = await ResolveSinglePipelineAsync(context).ConfigureAwait(false);
            VisionPipelineClient pipeline = context.Vision.Pipeline(pipelineNodeId);
            VisionFeedbackClient feedback = (await pipeline.OpenFeedbackAsync().ConfigureAwait(false))!;
            await feedback.SubmitDetectionsAsync(
                VisionFeedbackPurposeEnum.Reconciliation,
                new[] { BuildBaseline() }.ToArrayOf(),
                BuildFrameReference(),
                ByteString.Empty).ConfigureAwait(false);
            List<VisionNodeEntry> results = new();
            await foreach (VisionNodeEntry entry in pipeline.EnumerateResultsAsync())
            {
                results.Add(entry);
            }
            Assert.That(results, Has.Count.EqualTo(1));
            VisionDetectionResultSnapshot published = await context.Vision
                .Result(results[0].NodeId).ReadDetectionAsync().ConfigureAwait(false);
            Assert.That(published.ResultId, Is.Not.Null.And.Not.Empty);
            Assert.DoesNotThrowAsync(
                async () => await feedback.SubmitCorrectionAsync(
                    published.ResultId!,
                    VisionFeedbackPurposeEnum.Reconciliation,
                    ArrayOf<VisionDetectionDataType>.Empty,
                    ArrayOf<VisionCharacteristicDataType>.Empty,
                    LocalizedText.From("The previously published detection was a false positive."),
                    ByteString.Empty,
                    retractAll: true).ConfigureAwait(false),
                "An empty correction with RetractAll must be accepted: it is the " +
                "false-positive retraction, and the client expresses it directly.");
        }

        private static VisionDetectionDataType BuildBaseline()
        {
            return new VisionDetectionDataType
            {
                DetectionId = "det-baseline",
                ClassLabel = "TestCube",
                ClassId = 1u,
                Confidence = 0.9,
                HasBoundingBox2D = true,
                BoundingBox2D = new VisionBoundingBox2DDataType
                {
                    CenterX = 320.0,
                    CenterY = 240.0,
                    Width = 80.0,
                    Height = 60.0,
                    Rotation = 0.0
                },
                HasBoundingBox3D = false,
                HasPose = true,
                Pose = new VisionPose3DDataType
                {
                    FrameId = TestVisionCell.CameraFrameId,
                    Position = s_doubleValues1.ToArrayOf(),
                    Orientation = s_doubleValues3.ToArrayOf(),
                    Covariance = ArrayOf<double>.Empty
                },
                TrackId = "TestCube"
            };
        }

        private static VisionImageReferenceDataType BuildFrameReference()
        {
            return new VisionImageReferenceDataType
            {
                Uri = "urn:test:val:frames:1",
                Format = VisionClipFormatEnum.Png,
                PixelFormat = "Mono8",
                Width = TestVisionCell.ImageWidth,
                Height = TestVisionCell.ImageHeight
            };
        }

        private static async Task<VisionFeedbackClient> OpenFeedbackAsync(
            VisionIntentClientContext context)
        {
            NodeId pipelineNodeId = await ResolveSinglePipelineAsync(context).ConfigureAwait(false);
            VisionPipelineClient pipeline = context.Vision.Pipeline(pipelineNodeId);
            VisionFeedbackClient? feedback = await pipeline.OpenFeedbackAsync().ConfigureAwait(false);
            Assert.That(feedback, Is.Not.Null,
                "An off-server pipeline must expose the Feedback object per §4.4.");
            return feedback!;
        }

        private static async Task<NodeId> ResolveSinglePipelineAsync(VisionIntentClientContext context)
        {
            ArrayOf<NodeId> pipelines = await context.Vision.DiscoverPipelinesAsync()
                .ConfigureAwait(false);
            Assert.That(pipelines.Count, Is.EqualTo(1));
            NodeId nodeId = pipelines[0];
            Assert.That(nodeId.IsNull, Is.False, "Pipeline NodeId must resolve.");
            return nodeId;
        }

        private VisionIntentServerFixture m_fixture = null!;

        private static readonly double[] s_doubleValues1 = [0.2, 0.1, 0.5];
        private static readonly double[] s_doubleValues2 = [0.0, 0.0, 0.0, 0.0];
        private static readonly double[] s_doubleValues3 = [0.0, 0.0, 0.0, 1.0];
    }
}
