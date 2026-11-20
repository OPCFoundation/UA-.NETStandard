/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Discovery and enumeration tests over <see cref="VisionClient"/> with a
    /// populated Vision address space. Complements
    /// <see cref="VisionClientFacadeTests"/>, which pins the empty-namespace
    /// short-circuit path.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionClientDiscoveryTests
    {
        [Test]
        public void IsVisionNamespaceAvailableReturnsTrueWhenNamespacePresent()
        {
            var harness = new VisionSessionHarness();

            Assert.That(harness.Client.IsVisionNamespaceAvailable, Is.True);
        }

        [Test]
        public void VisionRootIdResolvesToWellKnownVisionObject()
        {
            var harness = new VisionSessionHarness();

            NodeId root = harness.Client.VisionRootId;

            Assert.That(root.IsNull, Is.False);
            Assert.That(root, Is.EqualTo(harness.VisionRootId));
        }

        [Test]
        public void SensorsFolderIdResolvesToVisionSensorsFolder()
        {
            var harness = new VisionSessionHarness();

            NodeId sensors = harness.Client.SensorsFolderId;

            Assert.That(sensors.IsNull, Is.False);
            Assert.That(sensors, Is.EqualTo(harness.SensorsFolderId));
        }

        [Test]
        public async Task GetPipelinesFolderIdReturnsBrowseTargetWhenPresent()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();

            NodeId pipelines = await harness.Client.GetPipelinesFolderIdAsync()
                .ConfigureAwait(false);

            Assert.That(pipelines, Is.EqualTo(harness.PipelinesFolderId));
        }

        [Test]
        public async Task GetFramesFolderIdReturnsBrowseTargetWhenPresent()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();

            NodeId frames = await harness.Client.GetFramesFolderIdAsync()
                .ConfigureAwait(false);

            Assert.That(frames, Is.EqualTo(harness.FramesFolderId));
        }

        [Test]
        public async Task DiscoverSensorsReturnsBrowsedSensorInstances()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(Perception.ObjectTypes.ImageSensorType, "Cam1");

            ArrayOf<NodeId> nodes = await harness.Client.DiscoverSensorsAsync()
                .ConfigureAwait(false);

            Assert.That(nodes.Count, Is.EqualTo(1));
            Assert.That(nodes[0], Is.EqualTo(harness.SensorNodeId));
        }

        [Test]
        public async Task DiscoverPipelinesReturnsBrowsedPipelineInstances()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline("Pipe1");

            ArrayOf<NodeId> nodes = await harness.Client.DiscoverPipelinesAsync()
                .ConfigureAwait(false);

            Assert.That(nodes.Count, Is.EqualTo(1));
            Assert.That(nodes[0], Is.EqualTo(harness.PipelineNodeId));
        }

        [Test]
        public async Task DiscoverFramesReturnsBrowsedCoordinateFrameInstances()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddFrame("F1");

            ArrayOf<NodeId> nodes = await harness.Client.DiscoverFramesAsync()
                .ConfigureAwait(false);

            Assert.That(nodes.Count, Is.EqualTo(1));
            Assert.That(nodes[0], Is.EqualTo(harness.FrameNodeId));
        }

        [Test]
        public async Task EnumerateSensorsYieldsEntriesWithBrowseNameAndTypeDefinition()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(Perception.ObjectTypes.ImageSensorType, "Cam1");

            var entries = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in harness.Client.EnumerateSensorsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].NodeId, Is.EqualTo(harness.SensorNodeId));
            Assert.That(entries[0].BrowseName.Name, Is.EqualTo("Cam1"));
            Assert.That(entries[0].DisplayName.Text, Is.EqualTo("Cam1"));
        }

        [Test]
        public async Task EnumeratePipelinesYieldsEntriesWithTypeDefinition()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddPipeline("Pipe1");

            var entries = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in harness.Client.EnumeratePipelinesAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].NodeId, Is.EqualTo(harness.PipelineNodeId));
        }

        [Test]
        public async Task EnumerateFramesYieldsEntriesWithTypeDefinition()
        {
            var harness = new VisionSessionHarness();
            harness.ConfigureVisionFolders();
            harness.AddFrame("F1");

            var entries = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in harness.Client.EnumerateFramesAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].NodeId, Is.EqualTo(harness.FrameNodeId));
        }

        [Test]
        public async Task DiscoverSensorsReturnsEmptyWhenSensorTypeMismatched()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(Perception.ObjectTypes.ImageSensorType, "Cam1");
            harness.NodeCache
                .Setup(c => c.IsTypeOfAsync(
                    Moq.It.IsAny<NodeId>(),
                    Moq.It.IsAny<NodeId>(),
                    Moq.It.IsAny<System.Threading.CancellationToken>()))
                .Returns(new ValueTask<bool>(false));

            ArrayOf<NodeId> nodes = await harness.Client.DiscoverSensorsAsync()
                .ConfigureAwait(false);

            Assert.That(nodes.Count, Is.EqualTo(0),
                "IsTypeOfAsync returning false must filter out non-Vision instances.");
        }
    }
}
