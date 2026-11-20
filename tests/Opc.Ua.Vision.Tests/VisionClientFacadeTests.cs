/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.Perception;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Covers the <see cref="VisionClient"/> namespace short-circuit
    /// (server has no Vision namespace ⇒ every enumeration returns empty
    /// and root NodeIds report <see cref="NodeId.IsNull"/>), the sub-client
    /// factory methods on <see cref="VisionClient"/>, and the argument
    /// guards on every facade constructor reached from the client factory
    /// methods (<see cref="VisionClient.Sensor"/>, <see cref="VisionClient.Media"/>
    /// and friends).
    /// </summary>
    [TestFixture]
    public sealed class VisionClientFacadeTests
    {
        [Test]
        public void ConstructorThrowsArgumentNullExceptionForNullSession()
        {
            Assert.That(
                () => new VisionClient(null!, new Mock<ITelemetryContext>().Object),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorThrowsArgumentNullExceptionForNullTelemetry()
        {
            Mock<ISession> session = NewSessionMock();

            Assert.That(
                () => new VisionClient(session.Object, null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void IsVisionNamespaceAvailableReturnsFalseWhenSessionDoesNotHaveVisionNamespace()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            Assert.That(client.IsVisionNamespaceAvailable, Is.False);
        }

        [Test]
        public void VisionRootIdIsNullWhenSessionDoesNotHaveVisionNamespace()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            Assert.That(client.VisionRootId.IsNull, Is.True);
        }

        [Test]
        public void SensorsFolderIdIsNullWhenSessionDoesNotHaveVisionNamespace()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            Assert.That(client.SensorsFolderId.IsNull, Is.True);
        }

        [Test]
        public async Task DiscoverSensorsReturnsEmptyWhenSessionDoesNotHaveVisionNamespace()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            ArrayOf<NodeId> nodes = await client.DiscoverSensorsAsync().ConfigureAwait(false);

            Assert.That(nodes.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task GetPipelinesFolderIdIsNullWhenSessionDoesNotHaveVisionNamespace()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            NodeId nodeId = await client.GetPipelinesFolderIdAsync().ConfigureAwait(false);

            Assert.That(nodeId.IsNull, Is.True);
        }

        [Test]
        public async Task GetFramesFolderIdIsNullWhenSessionDoesNotHaveVisionNamespace()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            NodeId nodeId = await client.GetFramesFolderIdAsync().ConfigureAwait(false);

            Assert.That(nodeId.IsNull, Is.True);
        }

        [Test]
        public async Task EnumerateSensorsYieldsNoEntriesWhenSessionDoesNotHaveVisionNamespace()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            var entries = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in client.EnumerateSensorsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries, Is.Empty);
        }

        [Test]
        public async Task DiscoverPipelinesReturnsEmptyWhenSessionDoesNotHaveVisionNamespace()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            ArrayOf<NodeId> nodes = await client.DiscoverPipelinesAsync().ConfigureAwait(false);

            Assert.That(nodes.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task DiscoverFramesReturnsEmptyWhenSessionDoesNotHaveVisionNamespace()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            ArrayOf<NodeId> nodes = await client.DiscoverFramesAsync().ConfigureAwait(false);

            Assert.That(nodes.Count, Is.EqualTo(0));
        }

        [Test]
        public void SessionAndTelemetryPropertiesReturnConstructorArguments()
        {
            Mock<ISession> session = NewSessionMock();
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;

            var client = new VisionClient(session.Object, telemetry);

            Assert.Multiple(() =>
            {
                Assert.That(client.Session, Is.SameAs(session.Object));
                Assert.That(client.Telemetry, Is.SameAs(telemetry));
            });
        }

        [Test]
        public void SensorFactoryRejectsNullNodeIdWithArgumentException()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            Assert.That(() => client.Sensor(NodeId.Null),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void MediaFactoryRejectsNullNodeIdWithArgumentException()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            Assert.That(() => client.Media(NodeId.Null),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void PipelineFactoryRejectsNullNodeIdWithArgumentException()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            Assert.That(() => client.Pipeline(NodeId.Null),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void FeedbackFactoryRejectsNullNodeIdWithArgumentException()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            Assert.That(() => client.Feedback(NodeId.Null),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ResultFactoryRejectsNullNodeIdWithArgumentException()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            Assert.That(() => client.Result(NodeId.Null),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void SensorFactoryReturnsClientBoundToRequestedNodeId()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();
            NodeId sensorId = new(1234, 3);

            VisionSensorClient sensor = client.Sensor(sensorId);

            Assert.That(sensor.SensorNodeId, Is.EqualTo(sensorId));
        }

        [Test]
        public void MediaFactoryReturnsClientBoundToRequestedNodeId()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();
            NodeId mediaId = new(555, 3);

            VisionMediaClient media = client.Media(mediaId);

            Assert.That(media.MediaNodeId, Is.EqualTo(mediaId));
        }

        [Test]
        public void FramesFactoryReturnsGraphInstance()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();

            VisionFrameGraph graph = client.Frames();

            Assert.That(graph, Is.Not.Null);
        }

        [Test]
        public void FrameGraphComposeAsyncThrowsArgumentNullExceptionForNullPose()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();
            VisionFrameGraph graph = client.Frames();

            Assert.That(
                () => graph.ComposeAsync(null!, new NodeId(1, 3), new NodeId(2, 3)),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void FrameGraphComposeAsyncThrowsArgumentExceptionWhenFromFrameIsNull()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();
            VisionFrameGraph graph = client.Frames();
            var pose = new VisionPose3DDataType
            {
                FrameId = "a",
                Position = new double[] { 0, 0, 0 },
                Orientation = new double[] { 0, 0, 0, 1 },
                Covariance = []
            };

            Assert.That(
                () => graph.ComposeAsync(pose, NodeId.Null, new NodeId(2, 3)),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void FrameGraphComposeAsyncThrowsArgumentExceptionWhenToFrameIsNull()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();
            VisionFrameGraph graph = client.Frames();
            var pose = new VisionPose3DDataType
            {
                FrameId = "a",
                Position = new double[] { 0, 0, 0 },
                Orientation = new double[] { 0, 0, 0, 1 },
                Covariance = []
            };

            Assert.That(
                () => graph.ComposeAsync(pose, new NodeId(1, 3), NodeId.Null),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void FrameGraphComposeTransformAsyncRejectsBothNullFrameIds()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();
            VisionFrameGraph graph = client.Frames();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => graph.ComposeTransformAsync(NodeId.Null, new NodeId(2, 3)),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(
                    () => graph.ComposeTransformAsync(new NodeId(1, 3), NodeId.Null),
                    Throws.TypeOf<ArgumentException>());
            });
        }

        [Test]
        public void FrameGraphReadAsyncThrowsArgumentExceptionForNullNodeId()
        {
            VisionClient client = BuildClientWithoutVisionNamespace();
            VisionFrameGraph graph = client.Frames();

            Assert.That(() => graph.ReadAsync(NodeId.Null),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task VisionClientRegistersVisionEncodeableTypesOnConstruction()
        {
            Mock<ISession> session = NewSessionMock();
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;

            _ = new VisionClient(session.Object, telemetry);

            IEncodeableFactory factory = session.Object.Factory;
            var probe = new VisionPose3DDataType();
            Assert.That(factory.TryGetEncodeableType(probe.BinaryEncodingId, out _), Is.True,
                "VisionClient must register the Vision encodeable types with the session so it can decode Vision structures.");
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static VisionClient BuildClientWithoutVisionNamespace()
        {
            Mock<ISession> session = NewSessionMock();
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
            return new VisionClient(session.Object, telemetry);
        }

        private static Mock<ISession> NewSessionMock()
        {
            var session = new Mock<ISession>();
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
            var messageContext = ServiceMessageContext.Create(telemetry);
            session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            session.SetupGet(s => s.MessageContext).Returns(messageContext);
            session.SetupGet(s => s.Factory).Returns(messageContext.Factory);
            return session;
        }
    }
}
