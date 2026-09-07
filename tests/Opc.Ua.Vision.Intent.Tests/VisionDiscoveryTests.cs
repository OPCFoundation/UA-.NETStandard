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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.Intent.Tests.Infrastructure;

namespace Opc.Ua.Vision.Intent.Tests
{
    /// <summary>
    /// Proves a client can discover the well-known Vision object and its
    /// sensors, pipelines and frames per §4.2, and that a sensor exposes
    /// the identity, intrinsics and hand-eye extrinsics that the addendum
    /// mandates for a calibrated eye-in-hand deployment.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class VisionDiscoveryTests
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
        public async Task VisionRootAndSubfoldersAreDiscoverableOverSession()
        {
            await using VisionIntentClientContext context = await m_fixture
                .ConnectAsync("discovery-root").ConfigureAwait(false);

            Assert.That(context.Vision.IsVisionNamespaceAvailable, Is.True,
                "The Server must expose the Vision namespace for the addendum tests.");
            NodeId visionRoot = context.Vision.VisionRootId;
            NodeId sensorsFolder = context.Vision.SensorsFolderId;
            NodeId pipelinesFolder = await context.Vision.GetPipelinesFolderIdAsync()
                .ConfigureAwait(false);
            NodeId framesFolder = await context.Vision.GetFramesFolderIdAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(visionRoot.IsNull, Is.False,
                    "The Vision root NodeId must resolve.");
                Assert.That(sensorsFolder.IsNull, Is.False,
                    "The Vision/Sensors folder is mandatory per §4.2.");
                Assert.That(pipelinesFolder.IsNull, Is.False,
                    "The Vision/Pipelines folder is required by the loop tests.");
                Assert.That(framesFolder.IsNull, Is.False,
                    "The Vision/Frames folder is required for pose composition.");
            });
        }

        [Test]
        public async Task DiscoveringSensorsPipelinesAndFramesReturnsTheAuthoredCell()
        {
            await using VisionIntentClientContext context = await m_fixture
                .ConnectAsync("discovery-enumerate").ConfigureAwait(false);

            ArrayOf<NodeId> sensors = await context.DiscoverSensorNodeIdsAsync()
                .ConfigureAwait(false);
            ArrayOf<NodeId> pipelines = await context.Vision.DiscoverPipelinesAsync()
                .ConfigureAwait(false);
            ArrayOf<NodeId> frames = await context.Vision.DiscoverFramesAsync()
                .ConfigureAwait(false);

            List<VisionNodeEntry> pipelineEntries = new();
            await foreach (VisionNodeEntry entry in context.Vision.EnumeratePipelinesAsync())
            {
                pipelineEntries.Add(entry);
            }
            List<VisionNodeEntry> frameEntries = new();
            await foreach (VisionNodeEntry entry in context.Vision.EnumerateFramesAsync())
            {
                frameEntries.Add(entry);
            }

            Assert.Multiple(() =>
            {
                Assert.That(sensors.Count, Is.EqualTo(1),
                    "The test cell hosts exactly one image sensor.");
                Assert.That(pipelines.Count, Is.EqualTo(1),
                    "The test cell hosts exactly one inference pipeline.");
                Assert.That(frames.Count, Is.EqualTo(4),
                    "The test cell defines a four-frame tree: World, Base, Flange, CameraEih.");
                Assert.That(
                    pipelineEntries.Select(e => e.BrowseName.Name),
                    Does.Contain(TestVisionCell.PipelineBrowseName));
                List<string> frameNames = new();
                foreach (VisionNodeEntry entry in frameEntries)
                {
                    string? name = entry.BrowseName.Name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        frameNames.Add(name);
                    }
                }
                Assert.That(frameNames, Does.Contain("World"));
                Assert.That(frameNames, Does.Contain("Base"));
                Assert.That(frameNames, Does.Contain("Flange"));
                Assert.That(frameNames, Does.Contain("CameraEih"));
            });
        }

        [Test]
        public async Task SensorIdentityIntrinsicsAndHandEyeExtrinsicsAreReadable()
        {
            await using VisionIntentClientContext context = await m_fixture
                .ConnectAsync("discovery-sensor").ConfigureAwait(false);
            ArrayOf<NodeId> sensors = await context.DiscoverSensorNodeIdsAsync()
                .ConfigureAwait(false);
            Assert.That(sensors.Count, Is.EqualTo(1));
            NodeId sensorNodeId = sensors[0];
            Assert.That(sensorNodeId.IsNull, Is.False);
            VisionSensorClient sensorClient = context.Vision.Sensor(sensorNodeId);
            VisionSensorIdentity identity = await sensorClient.ReadIdentityAsync()
                .ConfigureAwait(false);
            VisionImageSensorSnapshot? image = await sensorClient.ReadImageMembersAsync()
                .ConfigureAwait(false);
            Assert.That(image, Is.Not.Null);

            List<VisionNodeEntry> calibrations = new();
            await foreach (VisionNodeEntry entry in sensorClient.EnumerateCalibrationsAsync())
            {
                calibrations.Add(entry);
            }
            VisionNodeEntry intrinsicEntry = calibrations.Single(
                c => c.BrowseName.Name == TestVisionCell.IntrinsicCalibrationBrowseName);
            VisionNodeEntry extrinsicEntry = calibrations.Single(
                c => c.BrowseName.Name == TestVisionCell.HandEyeCalibrationBrowseName);
            VisionIntrinsicCalibrationSnapshot intrinsic = await sensorClient
                .ReadIntrinsicCalibrationAsync(intrinsicEntry.NodeId).ConfigureAwait(false);
            VisionExtrinsicCalibrationSnapshot extrinsic = await sensorClient
                .ReadExtrinsicCalibrationAsync(extrinsicEntry.NodeId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(identity.SensorId, Is.EqualTo("test-cam-eih-01"));
                Assert.That(identity.RealityKind, Is.EqualTo(VisionRealityKindEnum.Simulated));
                Assert.That(identity.Modality, Is.EqualTo(VisionSensorModalityEnum.Area2D));
                Assert.That(identity.FrameId, Is.EqualTo(TestVisionCell.CameraFrameId));
                Assert.That(image!.Width, Is.EqualTo(TestVisionCell.ImageWidth));
                Assert.That(image!.Height, Is.EqualTo(TestVisionCell.ImageHeight));
                Assert.That(image!.Intrinsics, Is.Not.Null);
                Assert.That(image!.Intrinsics!.Fx, Is.EqualTo(600.0));
                Assert.That(image!.Intrinsics!.Fy, Is.EqualTo(600.0));
                Assert.That(intrinsic.CalibrationId, Is.EqualTo("intr-test-cam-01"));
                Assert.That(intrinsic.Intrinsics, Is.Not.Null);
                Assert.That(intrinsic.Intrinsics!.Width, Is.EqualTo(TestVisionCell.ImageWidth));
                Assert.That(extrinsic.CalibrationId, Is.EqualTo("hand-eye-test-cam-01"));
                Assert.That(extrinsic.Mount, Is.EqualTo(VisionCalibrationMountEnum.EyeInHand));
                Assert.That(extrinsic.SourceFrameId.IsNull, Is.False,
                    "The hand-eye calibration must reference the camera frame as source.");
                Assert.That(extrinsic.TargetFrameId.IsNull, Is.False,
                    "The hand-eye calibration must reference the flange frame as target.");
                Assert.That(extrinsic.Transform, Is.Not.Null,
                    "The hand-eye calibration must carry a transform.");
                Assert.That(extrinsic.Transform!.FrameId, Is.EqualTo(TestVisionCell.FlangeFrameId));
            });
        }

        private VisionIntentServerFixture m_fixture = null!;
    }
}
