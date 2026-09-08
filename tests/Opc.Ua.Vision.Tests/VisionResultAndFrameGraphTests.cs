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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Tests for <see cref="VisionResultReader"/> — snapshot reads for
    /// detection, inspection and segmentation results.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionResultReaderTests
    {
        [Test]
        public async Task ReadInspectionReturnsPopulatedSnapshot()
        {
            var harness = new VisionSessionHarness();
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.ResultId,
                new(3210u, 3), "insp-1");
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.Evaluation,
                new(3211u, 3), (int)VisionResultEvaluationEnum.Ok);
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.PartId,
                new(3212u, 3), "part-A");
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.RecipeId,
                new(3213u, 3), "recipe-1");
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.CreationTime,
                new(3214u, 3), new DateTimeUtc(new DateTime(2024, 1, 1)));

            VisionResultReader reader = harness.Client.Result(harness.ResultNodeId);
            VisionInspectionResultSnapshot snapshot = await reader.ReadInspectionAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.ResultId, Is.EqualTo("insp-1"));
                Assert.That(snapshot.Evaluation, Is.EqualTo(VisionResultEvaluationEnum.Ok));
                Assert.That(snapshot.PartId, Is.EqualTo("part-A"));
                Assert.That(snapshot.RecipeId, Is.EqualTo("recipe-1"));
                Assert.That(snapshot.NodeId, Is.EqualTo(harness.ResultNodeId));
            });
        }

        [Test]
        public async Task ReadDetectionReturnsPopulatedSnapshot()
        {
            var harness = new VisionSessionHarness();
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.ResultId,
                new(3220u, 3), "det-1");
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.FrameId,
                new(3221u, 3), "frame-1");
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.CreationTime,
                new(3222u, 3), new DateTimeUtc(new DateTime(2024, 1, 1)));

            VisionResultReader reader = harness.Client.Result(harness.ResultNodeId);
            VisionDetectionResultSnapshot snapshot = await reader.ReadDetectionAsync()
                .ConfigureAwait(false);

            Assert.That(snapshot.ResultId, Is.EqualTo("det-1"));
            Assert.That(snapshot.FrameId, Is.EqualTo("frame-1"));
        }

        [Test]
        public async Task ReadSegmentationReturnsPopulatedSnapshot()
        {
            var harness = new VisionSessionHarness();
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.ResultId,
                new(3230u, 3), "seg-1");
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.CreationTime,
                new(3231u, 3), new DateTimeUtc(new DateTime(2024, 1, 1)));
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.LabelClasses,
                new(3232u, 3), new Variant(s_stringValues1));

            VisionResultReader reader = harness.Client.Result(harness.ResultNodeId);
            VisionSegmentationResultSnapshot snapshot = await reader.ReadSegmentationAsync()
                .ConfigureAwait(false);

            Assert.That(snapshot.ResultId, Is.EqualTo("seg-1"));
            Assert.That(snapshot.LabelClasses.Count, Is.EqualTo(2));
            Assert.That(snapshot.LabelClasses[0], Is.EqualTo("background"));
        }

        [Test]
        public void ConstructorRejectsNullResultNodeId()
        {
            var harness = new VisionSessionHarness();

            Assert.Throws<ArgumentException>(() =>
                harness.Client.Result(NodeId.Null));
        }

        [Test]
        public void ObserveDetectionsRejectsNullStreaming()
        {
            var harness = new VisionSessionHarness();
            VisionResultReader reader = harness.Client.Result(harness.ResultNodeId);

            Assert.Throws<ArgumentNullException>(() =>
                reader.ObserveDetectionsAsync(null!));
        }

        private static readonly string[] s_stringValues1 = ["background", "part"];
    }

    /// <summary>
    /// Tests for <see cref="VisionFrameGraph"/> — frame read, compose,
    /// walk-to-root guards.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionFrameGraphTests
    {
        [Test]
        public async Task ReadReturnsSnapshotWithFrameIdAndTransform()
        {
            var harness = new VisionSessionHarness();
            var identityTransform = new VisionPose3DDataType
            {
                FrameId = "root",
                Position = s_doubleValues1.ToArrayOf(),
                Orientation = s_doubleValues2.ToArrayOf()
            };
            harness.AddValueChild(harness.FrameNodeId, BrowseNames.FrameId,
                new(4010u, 3), "root");
            harness.AddValueChild(harness.FrameNodeId, BrowseNames.Role,
                new(4011u, 3), (int)VisionFrameRoleEnum.World);
            harness.AddValueChild(harness.FrameNodeId, BrowseNames.Transform,
                new(4013u, 3), Variant.FromStructure(identityTransform));

            VisionFrameGraph graph = harness.Client.Frames();
            VisionFrameSnapshot snapshot = await graph.ReadAsync(harness.FrameNodeId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.FrameId, Is.EqualTo("root"));
                Assert.That(snapshot.Role, Is.EqualTo(VisionFrameRoleEnum.World));
                Assert.That(snapshot.NodeId, Is.EqualTo(harness.FrameNodeId));
                Assert.That(snapshot.Transform, Is.Not.Null);
            });
        }

        [Test]
        public void ReadRejectsNullFrameNodeId()
        {
            var harness = new VisionSessionHarness();
            VisionFrameGraph graph = harness.Client.Frames();

            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await graph.ReadAsync(NodeId.Null).ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("frameNodeId"));
        }

        [Test]
        public void ComposeRejectsNullPose()
        {
            var harness = new VisionSessionHarness();
            VisionFrameGraph graph = harness.Client.Frames();

            var ex = Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await graph.ComposeAsync(null!, harness.FrameNodeId,
                    harness.FrameNodeId).ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("pose"));
        }

        [Test]
        public void ComposeRejectsNullFromFrameId()
        {
            var harness = new VisionSessionHarness();
            VisionFrameGraph graph = harness.Client.Frames();
            var pose = new VisionPose3DDataType();

            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await graph.ComposeAsync(pose, NodeId.Null, harness.FrameNodeId)
                    .ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("fromFrameId"));
        }

        [Test]
        public void ComposeRejectsNullToFrameId()
        {
            var harness = new VisionSessionHarness();
            VisionFrameGraph graph = harness.Client.Frames();
            var pose = new VisionPose3DDataType();

            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await graph.ComposeAsync(pose, harness.FrameNodeId, NodeId.Null)
                    .ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("toFrameId"));
        }

        [Test]
        public void ComposeTransformRejectsNullFromFrameId()
        {
            var harness = new VisionSessionHarness();
            VisionFrameGraph graph = harness.Client.Frames();

            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await graph.ComposeTransformAsync(NodeId.Null, harness.FrameNodeId)
                    .ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("fromFrameId"));
        }

        [Test]
        public void ComposeTransformRejectsNullToFrameId()
        {
            var harness = new VisionSessionHarness();
            VisionFrameGraph graph = harness.Client.Frames();

            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await graph.ComposeTransformAsync(harness.FrameNodeId, NodeId.Null)
                    .ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("toFrameId"));
        }

        [Test]
        public async Task ComposeTransformReturnsIdentityWhenFromEqualsTo()
        {
            var harness = new VisionSessionHarness();
            harness.AddValueChild(harness.FrameNodeId, BrowseNames.FrameId,
                new(4020u, 3), "root");

            VisionFrameGraph graph = harness.Client.Frames();
            VisionPose3DDataType transform = await graph.ComposeTransformAsync(
                harness.FrameNodeId, harness.FrameNodeId).ConfigureAwait(false);

            Assert.That(transform, Is.Not.Null);
            Assert.That(transform.Orientation.Count, Is.GreaterThanOrEqualTo(4),
                "An identity quaternion has four components.");
        }

        private static readonly double[] s_doubleValues1 = [0.0, 0.0, 0.0];
        private static readonly double[] s_doubleValues2 = [0.0, 0.0, 0.0, 1.0];
    }

    /// <summary>
    /// Tests for <see cref="VisionClientFactory"/> and
    /// <see cref="SessionVisionExtensions"/>.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionClientFactoryTests
    {
        [Test]
        public void ConstructorRejectsNullSessionFactory()
        {
            var telemetry = new Moq.Mock<ITelemetryContext>().Object;

            var ex = Assert.Throws<ArgumentNullException>(() =>
                new VisionClientFactory(null!, telemetry));

            Assert.That(ex!.ParamName, Is.EqualTo("sessionFactory"));
        }

        [Test]
        public void ConstructorRejectsNullTelemetry()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new VisionClientFactory(
                    _ => Task.FromResult<Opc.Ua.Client.ManagedSession>(null!),
                    null!));

            Assert.That(ex!.ParamName, Is.EqualTo("telemetry"));
        }

        [Test]
        public void SessionVisionRejectsNullSession()
        {
            var telemetry = new Moq.Mock<ITelemetryContext>().Object;

            var ex = Assert.Throws<ArgumentNullException>(() =>
                SessionVisionExtensions.Vision(null!, telemetry));

            Assert.That(ex!.ParamName, Is.EqualTo("session"));
        }

        [Test]
        public void SessionVisionRejectsNullTelemetry()
        {
            var harness = new VisionSessionHarness();

            var ex = Assert.Throws<ArgumentNullException>(() =>
                harness.Session.Object.Vision(null!));

            Assert.That(ex!.ParamName, Is.EqualTo("telemetry"));
        }

        [Test]
        public void SessionVisionReturnsVisionClientBoundToSession()
        {
            var harness = new VisionSessionHarness();

            VisionClient client = harness.Session.Object.Vision(harness.Telemetry);

            Assert.That(client, Is.Not.Null);
            Assert.That(client.Session, Is.SameAs(harness.Session.Object));
        }
    }
}
