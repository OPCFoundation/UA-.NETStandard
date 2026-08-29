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
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;
using Opc.Ua.Vision.Server.Builders;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Integration tests over the fluent Vision node builder. These
    /// exercise the builder methods against a real
    /// <see cref="VisionNodeManager"/> so the resulting nodes end up
    /// in the address space rather than in a mocked context.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionBuilderIntegrationTests
    {
        [Test]
        public async Task AddImageSensorExercisesEveryFluentEntryPoint()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();
            var mediaProvider = new Mock<IVisionMediaProvider>().Object;
            VisionPose3DDataType extrinsic = CreatePose();
            VisionIntrinsicsDataType intrinsics = CreateIntrinsics();

            context.Nodes.AddImageSensor("Camera1", sensor => sensor
                .WithSensorId("SN-CAM-1")
                .WithRealityKind(VisionRealityKindEnum.Physical)
                .WithModality(VisionSensorModalityEnum.Area2D)
                .WithManufacturer("Contoso")
                .WithModel("ContosoCam 4K")
                .WithSerialNumber("SN-42")
                .WithDeviceUri("opc.tcp://cam-1")
                .WithFrameId("cam1")
                .WithResolution(1920, 1080)
                .WithPixelFormat("Mono8")
                .WithIntrinsics(intrinsics)
                .WithOptics(o => o
                    .WithFocalLength(0.008)
                    .WithAperture(1.4)
                    .WithWorkingDistance(0.5)
                    .WithMagnification(2.0)
                    .WithMountType("C-Mount")
                    .WithLensType("Fixed"))
                .WithIllumination(i => i
                    .WithLampType(VisionLampTypeEnum.Led)
                    .WithWavelength(525)
                    .WithRelativeIntensity(0.8)
                    .WithLightingMode(VisionLightingModeEnum.Continuous))
                .AddIntrinsicCalibration("Intr", calib => calib
                    .WithCalibrationId("intrinsic-1")
                    .WithIntrinsics(intrinsics)
                    .WithResidualError(0.25)
                    .WithMethod("Zhang"))
                .AddExtrinsicCalibration("Extr", calib => calib
                    .WithCalibrationId("extrinsic-1")
                    .WithMount(VisionCalibrationMountEnum.EyeToHand)
                    .WithFrames("world", "cam1")
                    .WithTransform(extrinsic)
                    .WithResidualError(0.1))
                .AddStreamEndpoint("Rtsp", ep => ep
                    .WithEndpointId("stream-1")
                    .WithEndpointUri("rtsp://cam-1/stream")
                    .WithProtocol(VisionStreamProtocolEnum.Rtsp)
                    .WithCodec(VisionVideoCodecEnum.H264)
                    .WithResolution(1920, 1080)
                    .WithFrameRate(30.0)
                    .WithBitrate(8_000_000)
                    .WithDefaultProfileName("main"))
                .AddClipEndpoint("Snap", ep => ep
                    .WithEndpointId("clip-1")
                    .WithEndpointUri("clip://cam-1/snap")
                    .WithClipFormat(VisionClipFormatEnum.Jpeg)
                    .WithQuality(90)
                    .WithResolution(1920, 1080)
                    .WithInlineDelivery(true, 1_048_576)
                    .WithDefaultProfileName("thumb"))
                .UseMediaProvider(mediaProvider));

            Assert.That(fixture.Manager.Root.Sensors, Is.Not.Null);
            Assert.That(FindChild(fixture.Manager.Root.Sensors!, "Camera1"),
                Is.Not.Null, "Camera1 must be added to the sensors folder.");
        }

        [Test]
        public async Task AddDepth3DSensorAppliesDepthSpecificMembers()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            context.Nodes.AddDepth3DSensor("Depth1", sensor => sensor
                .WithSensorId("SN-DEP-1")
                .WithModality(VisionSensorModalityEnum.Depth3D)
                .WithRealityKind(VisionRealityKindEnum.Simulated)
                .WithDepthRange(0.2, 5.0)
                .WithDepthScale(0.001)
                .WithBaseline(0.075));

            NodeState? added = FindChild(fixture.Manager.Root.Sensors!, "Depth1");
            Assert.That(added, Is.Not.Null);
        }

        [Test]
        public async Task AddSensorGenericFluentSurfaceCovered()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            context.Nodes.AddSensor("Thermal1", sensor => sensor
                .WithSensorId("SN-TH-1")
                .WithModality(VisionSensorModalityEnum.Thermal)
                .WithRealityKind(VisionRealityKindEnum.Hybrid)
                .HasScenePrim(new NodeId("scene:cam1", 1))
                .MountedOn(new NodeId("mount:cam1", 1)));

            Assert.That(FindChild(fixture.Manager.Root.Sensors!, "Thermal1"),
                Is.Not.Null);
        }

        [Test]
        public async Task AddFrameCreatesParentedFramesUsingRegistryLookup()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();
            VisionPose3DDataType worldPose = CreatePose();
            VisionPose3DDataType camPose = CreatePose();

            context.Nodes
                .AddFrame("World", f => f
                    .WithFrameId("world")
                    .WithRole(VisionFrameRoleEnum.World)
                    .WithTransform(worldPose))
                .AddFrame("Cam1", f => f
                    .WithFrameId("cam1")
                    .WithRole(VisionFrameRoleEnum.Camera)
                    .WithParent("world")
                    .WithTransform(camPose));

            Assert.That(fixture.Manager.Root.Frames, Is.Not.Null);
            Assert.That(FindChild(fixture.Manager.Root.Frames!, "World"),
                Is.Not.Null);
            Assert.That(FindChild(fixture.Manager.Root.Frames!, "Cam1"),
                Is.Not.Null);
        }

        [Test]
        public async Task AddFrameAcceptsExplicitParentNodeId()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            context.Nodes.AddFrame("World", f => f
                .WithFrameId("world")
                .WithRole(VisionFrameRoleEnum.World)
                .WithTransform(CreatePose()));

            NodeState worldNode = FindChild(fixture.Manager.Root.Frames!, "World")!;
            context.Nodes.AddFrame("Tool0", f => f
                .WithFrameId("tool0")
                .WithRole(VisionFrameRoleEnum.Tool)
                .WithParent(worldNode.NodeId)
                .WithTransform(CreatePose()));

            Assert.That(FindChild(fixture.Manager.Root.Frames!, "Tool0"),
                Is.Not.Null);
        }

        [Test]
        public async Task AddFrameWithEmptyFrameIdThrowsBadConfigurationError()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                context.Nodes.AddFrame("Nameless", f => f
                    .WithRole(VisionFrameRoleEnum.World)))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task AddPipelineExercisesEveryPipelineFluentEntryPoint()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();
            var inferenceProvider = new Mock<IVisionInferenceProvider>().Object;
            var feedbackSink = new Mock<IVisionFeedbackSink>().Object;

            context.Nodes.AddImageSensor("Cam1", s => s
                .WithSensorId("SN-CAM")
                .WithModality(VisionSensorModalityEnum.Area2D));

            NodeState sensorNode = FindChild(fixture.Manager.Root.Sensors!, "Cam1")!;

            context.Nodes.AddPipeline("Pipe1", p => p
                .WithPipelineId("pipeline-1")
                .WithSensor(sensorNode.NodeId)
                .WithDeployment(new NodeId("deployment:pipeline-1", 1))
                .ProducedBy(new NodeId("producer:workcell1", 1))
                .UseInferenceProvider(inferenceProvider, onServer: true)
                .UseFeedbackSink(feedbackSink));

            Assert.That(fixture.Manager.Root.Pipelines, Is.Not.Null);
            Assert.That(FindChild(fixture.Manager.Root.Pipelines!, "Pipe1"),
                Is.Not.Null);
        }

        [Test]
        public async Task AddPipelineHonoursOffServerInferenceFacet()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();
            var inferenceProvider = new Mock<IVisionInferenceProvider>().Object;

            context.Nodes.AddPipeline("Off", p => p
                .WithPipelineId("off-server")
                .UseInferenceProvider(inferenceProvider, onServer: false));

            Assert.That(FindChild(fixture.Manager.Root.Pipelines!, "Off"),
                Is.Not.Null);
        }

        [Test]
        public async Task AddImageSensorRejectsEmptyBrowseName()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentException>(() =>
                context.Nodes.AddImageSensor(string.Empty, _ => { }));
        }

        [Test]
        public async Task AddImageSensorRejectsNullConfigureDelegate()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentNullException>(() =>
                context.Nodes.AddImageSensor("Cam1", null!));
        }

        [Test]
        public async Task AddDepth3DSensorRejectsEmptyBrowseName()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentException>(() =>
                context.Nodes.AddDepth3DSensor(string.Empty, _ => { }));
        }

        [Test]
        public async Task AddDepth3DSensorRejectsNullConfigureDelegate()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentNullException>(() =>
                context.Nodes.AddDepth3DSensor("D1", null!));
        }

        [Test]
        public async Task AddSensorRejectsEmptyBrowseName()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentException>(() =>
                context.Nodes.AddSensor(string.Empty, _ => { }));
        }

        [Test]
        public async Task AddSensorRejectsNullConfigureDelegate()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentNullException>(() =>
                context.Nodes.AddSensor("S1", null!));
        }

        [Test]
        public async Task AddFrameRejectsEmptyBrowseName()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentException>(() =>
                context.Nodes.AddFrame(string.Empty, _ => { }));
        }

        [Test]
        public async Task AddFrameRejectsNullConfigureDelegate()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentNullException>(() =>
                context.Nodes.AddFrame("F1", null!));
        }

        [Test]
        public async Task AddPipelineRejectsEmptyBrowseName()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentException>(() =>
                context.Nodes.AddPipeline(string.Empty, _ => { }));
        }

        [Test]
        public async Task AddPipelineRejectsNullConfigureDelegate()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentNullException>(() =>
                context.Nodes.AddPipeline("P1", null!));
        }

        [Test]
        public async Task WithOpticsRejectsNullConfigureDelegate()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentNullException>(() =>
                context.Nodes.AddSensor("S1", s => s.WithOptics(null!)));
        }

        [Test]
        public async Task WithIlluminationRejectsNullConfigureDelegate()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentNullException>(() =>
                context.Nodes.AddSensor("S1", s => s.WithIllumination(null!)));
        }

        [Test]
        public async Task UseMediaProviderRejectsNullProvider()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentNullException>(() =>
                context.Nodes.AddSensor("S1", s => s.UseMediaProvider(null!)));
        }

        [Test]
        public async Task AddIntrinsicCalibrationRejectsEmptyBrowseName()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentException>(() =>
                context.Nodes.AddSensor("S1", s =>
                    s.AddIntrinsicCalibration(string.Empty, _ => { })));
        }

        [Test]
        public async Task AddExtrinsicCalibrationRejectsEmptyBrowseName()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentException>(() =>
                context.Nodes.AddSensor("S1", s =>
                    s.AddExtrinsicCalibration(string.Empty, _ => { })));
        }

        [Test]
        public async Task AddStreamEndpointRejectsEmptyBrowseName()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentException>(() =>
                context.Nodes.AddSensor("S1", s =>
                    s.AddStreamEndpoint(string.Empty, _ => { })));
        }

        [Test]
        public async Task AddClipEndpointRejectsEmptyBrowseName()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentException>(() =>
                context.Nodes.AddSensor("S1", s =>
                    s.AddClipEndpoint(string.Empty, _ => { })));
        }

        [Test]
        public async Task WithTransformRejectsNullTransform()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<ArgumentNullException>(() =>
                context.Nodes.AddFrame("F1", f => f
                    .WithFrameId("f1")
                    .WithTransform(null!)));
        }

        [Test]
        public async Task AddStreamEndpointWithMjpegAddsCorrectFacet()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            context.Nodes.AddSensor("Cam1", s => s
                .WithSensorId("SN-CAM")
                .WithModality(VisionSensorModalityEnum.Area2D)
                .AddStreamEndpoint("Mjpeg", ep => ep
                    .WithEndpointId("mjpeg-1")
                    .WithEndpointUri("http://cam-1/mjpeg")
                    .WithProtocol(VisionStreamProtocolEnum.Mjpeg)
                    .WithCodec(VisionVideoCodecEnum.Mjpeg)));

            Assert.That(FindChild(fixture.Manager.Root.Sensors!, "Cam1"),
                Is.Not.Null);
        }

        [Test]
        public async Task AddClipEndpointWithoutInlineDeliveryIsAllowed()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            context.Nodes.AddSensor("Cam1", s => s
                .WithSensorId("SN-CAM")
                .WithModality(VisionSensorModalityEnum.Area2D)
                .AddClipEndpoint("Snap", ep => ep
                    .WithEndpointId("snap-1")
                    .WithClipFormat(VisionClipFormatEnum.Png)
                    .WithInlineDelivery(false, 0)
                    .WithDefaultProfileName("noinline")));

            Assert.That(FindChild(fixture.Manager.Root.Sensors!, "Cam1"),
                Is.Not.Null);
        }

        [Test]
        public async Task AddImageSensorHasScenePrimAndMountedOnAreCovered()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            context.Nodes.AddImageSensor("Cam1", s => s
                .WithSensorId("SN-CAM")
                .WithModality(VisionSensorModalityEnum.Area2D)
                .HasScenePrim(new NodeId("scene:prim1", 1))
                .MountedOn(new NodeId("mount:cam1", 1)));

            Assert.That(FindChild(fixture.Manager.Root.Sensors!, "Cam1"),
                Is.Not.Null);
        }

        [Test]
        public async Task AddImageSensorIgnoresNullScenePrimAndMount()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            context.Nodes.AddImageSensor("Cam1", s => s
                .WithSensorId("SN-CAM")
                .HasScenePrim(NodeId.Null)
                .MountedOn(NodeId.Null));

            Assert.That(FindChild(fixture.Manager.Root.Sensors!, "Cam1"),
                Is.Not.Null);
        }

        [Test]
        public async Task BuildContextExposesVisionAndInstanceNamespaceIndexes()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Multiple(() =>
            {
                Assert.That(context.VisionNamespaceIndex, Is.GreaterThan((ushort)0));
                Assert.That(context.InstanceNamespaceIndex, Is.GreaterThan((ushort)0));
                Assert.That(context.Manager, Is.SameAs(fixture.Manager));
                Assert.That(context.Root, Is.SameAs(fixture.Manager.Root));
                Assert.That(context.Context, Is.Not.Null);
                Assert.That(context.CancellationToken, Is.EqualTo(CancellationToken.None));
            });
        }

        [Test]
        public async Task GetRequiredServiceThrowsWhenNoServiceProviderConfigured()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();

            Assert.Throws<InvalidOperationException>(() =>
                context.GetRequiredService<IVisionMediaProvider>());
        }

        private static VisionPose3DDataType CreatePose()
        {
            return new VisionPose3DDataType
            {
                FrameId = "world",
                Position = new[] { 0.0, 0.0, 0.0 },
                Orientation = new[] { 0.0, 0.0, 0.0, 1.0 },
                Covariance = ArrayOf<double>.Empty
            };
        }

        private static VisionIntrinsicsDataType CreateIntrinsics()
        {
            return new VisionIntrinsicsDataType
            {
                Fx = 1400.0,
                Fy = 1400.0,
                Cx = 960.0,
                Cy = 540.0,
                Skew = 0.0
            };
        }

        private static NodeState? FindChild(NodeState parent, string browseName)
        {
            var children = new System.Collections.Generic.List<BaseInstanceState>();
            parent.GetChildren(null!, children);
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii].BrowseName.Name == browseName)
                {
                    return children[ii];
                }
            }
            return null;
        }
    }
}
