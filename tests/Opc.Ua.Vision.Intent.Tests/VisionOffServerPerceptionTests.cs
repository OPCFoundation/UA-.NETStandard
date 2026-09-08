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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.Intent.Tests.Infrastructure;

namespace Opc.Ua.Vision.Intent.Tests
{
    /// <summary>
    /// Proves the off-server perception path: <c>RunInference</c> is
    /// refused because no local model is loaded, an external agent
    /// posts detections via <c>SubmitDetections</c>, and a downstream
    /// client reads the resulting <c>DetectionResultType</c> using the
    /// exact same client API it used against the on-server pipeline.
    /// The design claim the tests pin down: one contract, two producers.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class VisionOffServerPerceptionTests
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
        public async Task RunInferenceIsRefusedByOffServerPipeline()
        {
            await using VisionIntentClientContext context = await m_fixture
                .ConnectAsync("off-server-refused").ConfigureAwait(false);
            NodeId pipelineNodeId = await ResolveSinglePipelineAsync(context)
                .ConfigureAwait(false);
            VisionPipelineClient pipeline = context.Vision.Pipeline(pipelineNodeId);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await pipeline.RunInferenceAsync().ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported),
                "An off-server pipeline must refuse RunInference with Bad_NotSupported.");
        }

        [Test]
        public async Task SubmitDetectionsPublishesAResultReadableByTheSameClientContract()
        {
            await using VisionIntentClientContext context = await m_fixture
                .ConnectAsync("off-server-submit").ConfigureAwait(false);
            NodeId pipelineNodeId = await ResolveSinglePipelineAsync(context)
                .ConfigureAwait(false);
            VisionPipelineClient pipeline = context.Vision.Pipeline(pipelineNodeId);
            VisionFeedbackClient? feedback = await pipeline.OpenFeedbackAsync()
                .ConfigureAwait(false);
            Assert.That(feedback, Is.Not.Null,
                "An off-server pipeline must expose the Feedback object per §4.4.");

            VisionDetectionDataType detection = BuildValidDetection("TestCube", 1u);
            var detections = new[] { detection }.ToArrayOf();
            var frameReference = new VisionImageReferenceDataType
            {
                Uri = "urn:test:off-server:frames:1",
                Format = VisionClipFormatEnum.Png,
                PixelFormat = "Mono8",
                Width = TestVisionCell.ImageWidth,
                Height = TestVisionCell.ImageHeight
            };
            await feedback!.SubmitDetectionsAsync(
                VisionFeedbackPurposeEnum.Reconciliation,
                detections,
                frameReference,
                ByteString.Empty).ConfigureAwait(false);

            List<VisionNodeEntry> results = new();
            await foreach (VisionNodeEntry entry in pipeline.EnumerateResultsAsync())
            {
                results.Add(entry);
            }
            Assert.That(results, Has.Count.EqualTo(1),
                "The off-server pipeline should have exactly one result after one submission.");
            VisionResultReader reader = context.Vision.Result(results[0].NodeId);
            VisionDetectionResultSnapshot snapshot = await reader.ReadDetectionAsync()
                .ConfigureAwait(false);
            List<VisionDetectionDataType> readBack = SnapshotDetections(snapshot);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.ResultId, Is.Not.Null.And.Not.Empty);
                Assert.That(snapshot.ModelVersionUsed,
                    Does.StartWith("agent-off-server:"),
                    "Provenance must record that the model ran off the Server.");
                Assert.That(snapshot.FrameId, Is.EqualTo(TestVisionCell.CameraFrameId),
                    "The result's FrameId must match the sensor's camera frame.");
                Assert.That(readBack, Has.Count.EqualTo(1));
                Assert.That(readBack[0].ClassLabel, Is.EqualTo("TestCube"));
                Assert.That(readBack[0].Confidence, Is.EqualTo(detection.Confidence));
                Assert.That(readBack[0].HasPose, Is.True);
            });
        }

        [Test]
        public async Task PipelineExposesTheOffServerFacetSoClientsCanTellItApartFromOnServer()
        {
            await using VisionIntentClientContext context = await m_fixture
                .ConnectAsync("off-server-facet").ConfigureAwait(false);
            List<string> profileNames = await ReadServerProfileArrayAsync(context.Session)
                .ConfigureAwait(false);
            string advertised = string.Join(", ", profileNames);

            Assert.That(profileNames, Is.Not.Empty,
                "The Server must advertise Vision facets in ServerCapabilities.ServerProfileArray.");
            Assert.That(
                profileNames.Any(p => p.Contains("Inference-OffServer",
                    System.StringComparison.OrdinalIgnoreCase)),
                Is.True,
                FormattableString.Invariant($"An off-server pipeline must advertise VIS-Inference-OffServer. Advertised: {advertised}."));
            Assert.That(
                profileNames.Any(p => p.Contains("Inference-OnServer",
                    System.StringComparison.OrdinalIgnoreCase)),
                Is.False,
                FormattableString.Invariant($"An off-server pipeline must NOT advertise VIS-Inference-OnServer. Advertised: {advertised}."));
        }

        private static async Task<List<string>> ReadServerProfileArrayAsync(
            Opc.Ua.Client.ISession session)
        {
            NodeId profileArrayNodeId = global::Opc.Ua.VariableIds
                .Server_ServerCapabilities_ServerProfileArray;
            ArrayOf<ReadValueId> toRead = new ReadValueId[]
            {
                new()
                {
                    NodeId = profileArrayNodeId,
                    AttributeId = Attributes.Value
                }
            }.ToArrayOf();
            ReadResponse response = await session.ReadAsync(
                null,
                0.0,
                TimestampsToReturn.Neither,
                toRead,
                System.Threading.CancellationToken.None).ConfigureAwait(false);
            var profiles = new List<string>();
            if (response.Results.Count == 0 ||
                !StatusCode.IsGood(response.Results[0].StatusCode))
            {
                return profiles;
            }
            if (response.Results[0].WrappedValue.TryGetValue(out ArrayOf<string> array))
            {
                for (int ii = 0; ii < array.Count; ii++)
                {
                    string? entry = array[ii];
                    if (!string.IsNullOrEmpty(entry))
                    {
                        profiles.Add(entry);
                    }
                }
            }
            return profiles;
        }

        private static VisionDetectionDataType BuildValidDetection(string classLabel, uint classId)
        {
            return new VisionDetectionDataType
            {
                DetectionId = "det-off-01",
                ClassLabel = classLabel,
                ClassId = classId,
                Confidence = 0.87,
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
                    Orientation = s_doubleValues2.ToArrayOf(),
                    Covariance = ArrayOf<double>.Empty
                },
                TrackId = classLabel
            };
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

        private static List<VisionDetectionDataType> SnapshotDetections(
            VisionDetectionResultSnapshot snapshot)
        {
            var buffer = new List<VisionDetectionDataType>(snapshot.Detections.Count);
            for (int ii = 0; ii < snapshot.Detections.Count; ii++)
            {
                buffer.Add(snapshot.Detections[ii]);
            }
            return buffer;
        }

        private VisionIntentServerFixture m_fixture = null!;

        private static readonly double[] s_doubleValues1 = [0.2, 0.1, 0.5];
        private static readonly double[] s_doubleValues2 = [0.0, 0.0, 0.0, 1.0];
    }
}
