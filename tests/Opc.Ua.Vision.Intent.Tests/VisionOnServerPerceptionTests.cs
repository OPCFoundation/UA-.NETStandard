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
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.Intent.Tests.Infrastructure;

namespace Opc.Ua.Vision.Intent.Tests
{
    /// <summary>
    /// Proves the on-server perception path: a client asks the pipeline
    /// to run inference, the Server publishes a <c>DetectionResultType</c>
    /// as a real address-space node, and composing the resulting pose
    /// through the frame tree lands on the part's authored world
    /// position. The composition residual is the ground truth of "did
    /// the loop work" — the test pins it to machine precision because
    /// the frame tree is authored as identity rotations.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class VisionOnServerPerceptionTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new VisionIntentServerFixture();
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
        public async Task RunInferenceProducesDetectionResultWithLabelledParts()
        {
            await using VisionIntentClientContext context = await m_fixture
                .ConnectAsync("on-server-inference").ConfigureAwait(false);
            NodeId pipelineNodeId = await ResolveSinglePipelineAsync(context)
                .ConfigureAwait(false);
            VisionPipelineClient pipeline = context.Vision.Pipeline(pipelineNodeId);

            string resultId = await pipeline.RunInferenceAsync().ConfigureAwait(false);
            Assert.That(resultId, Is.Not.Null.And.Not.Empty,
                "RunInference must return the stable ResultId of the published result.");

            VisionDetectionResultSnapshot snapshot = await ReadSingleResultAsync(pipeline, context)
                .ConfigureAwait(false);
            List<VisionDetectionDataType> detections = SnapshotDetections(snapshot);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.ResultId, Is.EqualTo(resultId));
                Assert.That(snapshot.ModelVersionUsed, Is.EqualTo("test-groundtruth-1"));
                Assert.That(snapshot.FrameId, Is.EqualTo(TestVisionCell.CameraFrameId));
                Assert.That(detections, Has.Count.EqualTo(2),
                    "Both authored parts should be detected in the initial bin state.");
                Assert.That(detections.Select(d => d.ClassLabel),
                    Is.EquivalentTo(s_stringValues1));
                Assert.That(detections.All(d => d.Confidence >= 0.0 && d.Confidence <= 1.0), Is.True,
                    "All confidences must live in [0, 1].");
                Assert.That(detections.All(d => d.HasBoundingBox2D), Is.True,
                    "The ground-truth detector reports a bounding box for every part.");
                Assert.That(detections.All(d => d.HasPose), Is.True,
                    "Every detection must carry a 6-DoF pose.");
            });
        }

        [Test]
        public async Task ComposingDetectionPoseIntoWorldLandsOnAuthoredPartPosition()
        {
            await using VisionIntentClientContext context = await m_fixture
                .ConnectAsync("on-server-compose").ConfigureAwait(false);
            NodeId pipelineNodeId = await ResolveSinglePipelineAsync(context)
                .ConfigureAwait(false);
            VisionPipelineClient pipeline = context.Vision.Pipeline(pipelineNodeId);
            _ = await pipeline.RunInferenceAsync().ConfigureAwait(false);
            VisionDetectionResultSnapshot snapshot = await ReadSingleResultAsync(pipeline, context)
                .ConfigureAwait(false);

            IReadOnlyDictionary<string, NodeId> framesByName = await BuildFrameMapAsync(context)
                .ConfigureAwait(false);
            NodeId cameraFrameNodeId = framesByName["CameraEih"];
            NodeId worldFrameNodeId = framesByName["World"];
            VisionFrameGraph graph = context.Vision.Frames();

            List<VisionDetectionDataType> detections = SnapshotDetections(snapshot);
            VisionDetectionDataType cubeDetection = detections.Single(d => d.ClassLabel == "TestCube");
            VisionPose3DDataType cubePoseInCamera = cubeDetection.Pose;
            VisionPose3DDataType cubePoseInWorld = await graph.ComposeAsync(
                cubePoseInCamera, cameraFrameNodeId, worldFrameNodeId).ConfigureAwait(false);

            var authored = new[] { 0.700, 0.100, 0.600 };
            double dx = cubePoseInWorld.Position[0] - authored[0];
            double dy = cubePoseInWorld.Position[1] - authored[1];
            double dz = cubePoseInWorld.Position[2] - authored[2];
            double residual = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            string message =
                "On-server pose composition residual for TestCube = " +
                residual.ToString("E3", CultureInfo.InvariantCulture) +
                " m (world pose = (" +
                cubePoseInWorld.Position[0].ToString("F9", CultureInfo.InvariantCulture) +
                ", " +
                cubePoseInWorld.Position[1].ToString("F9", CultureInfo.InvariantCulture) +
                ", " +
                cubePoseInWorld.Position[2].ToString("F9", CultureInfo.InvariantCulture) +
                ")). " +
                "Frames traversed: camera_eih → flange → base → world.";
            TestContext.Progress.WriteLine(message);

            Assert.Multiple(() =>
            {
                Assert.That(cubePoseInWorld.Position, Has.Count.EqualTo(3));
                Assert.That(cubePoseInWorld.Position[0],
                    Is.EqualTo(authored[0]).Within(1e-9),
                    "Composed X must equal the authored world X for TestCube.");
                Assert.That(cubePoseInWorld.Position[1],
                    Is.EqualTo(authored[1]).Within(1e-9),
                    "Composed Y must equal the authored world Y for TestCube.");
                Assert.That(cubePoseInWorld.Position[2],
                    Is.EqualTo(authored[2]).Within(1e-9),
                    "Composed Z must equal the authored world Z for TestCube.");
                Assert.That(residual, Is.LessThan(1e-9),
                    "Total residual must be at machine precision for identity rotations.");
            });
        }

        private static async Task<NodeId> ResolveSinglePipelineAsync(VisionIntentClientContext context)
        {
            ArrayOf<NodeId> pipelines = await context.Vision.DiscoverPipelinesAsync()
                .ConfigureAwait(false);
            Assert.That(pipelines.Count, Is.EqualTo(1),
                "The test cell must expose exactly one pipeline.");
            NodeId nodeId = pipelines[0];
            Assert.That(nodeId.IsNull, Is.False, "Pipeline NodeId must resolve.");
            return nodeId;
        }

        private static async Task<VisionDetectionResultSnapshot> ReadSingleResultAsync(
            VisionPipelineClient pipeline, VisionIntentClientContext context)
        {
            List<VisionNodeEntry> results = new();
            await foreach (VisionNodeEntry entry in pipeline.EnumerateResultsAsync())
            {
                results.Add(entry);
            }
            Assert.That(results, Is.Not.Empty,
                "The pipeline should have published at least one result after RunInference.");
            VisionNodeEntry chosen = results[^1];
            VisionResultReader reader = context.Vision.Result(chosen.NodeId);
            // Keep the awaited result local because RCS1174 does not account for the await foreach above.
            // TODO: Remove when the analyzer tracks preceding asynchronous enumeration.
#pragma warning disable RCS1124
            VisionDetectionResultSnapshot snapshot = await reader.ReadDetectionAsync().ConfigureAwait(false);
            return snapshot;
#pragma warning restore RCS1124
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

        private static async Task<IReadOnlyDictionary<string, NodeId>> BuildFrameMapAsync(
            VisionIntentClientContext context)
        {
            var map = new Dictionary<string, NodeId>(StringComparer.Ordinal);
            await foreach (VisionNodeEntry frame in context.Vision.EnumerateFramesAsync())
            {
                string? key = frame.BrowseName.Name;
                if (!string.IsNullOrEmpty(key))
                {
                    map[key] = frame.NodeId;
                }
            }
            Assert.That(map.Keys, Is.EquivalentTo(s_stringValues2));
            return map;
        }

        private VisionIntentServerFixture m_fixture = null!;

        private static readonly string[] s_stringValues1 = ["TestCube", "TestCylinder"];
        private static readonly string[] s_stringValues2 = ["World", "Base", "Flange", "CameraEih"];
    }
}
