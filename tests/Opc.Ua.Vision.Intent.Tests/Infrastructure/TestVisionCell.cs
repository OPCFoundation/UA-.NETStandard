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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Vision.Server;
using Opc.Ua.Vision.Server.Builders;

namespace Opc.Ua.Vision.Intent.Tests.Infrastructure
{
    /// <summary>
    /// Materialises the test Vision cell — frames, sensor, pipeline —
    /// on the standalone Vision node manager. The pose and calibration
    /// numbers are chosen so the loop tests can pin the frame
    /// composition residual to machine precision.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The frame tree is intentionally minimal but multi-hop
    /// (world → base → flange → camera_eih) so the loop test can
    /// prove that <c>VisionFrameGraph.ComposeAsync</c> walks the tree
    /// end-to-end — a single-hop tree would not exercise composition.
    /// </para>
    /// <para>
    /// All rotations are identity so the composition reduces to a
    /// sum of translations. That keeps the test's expected value
    /// exact regardless of platform floating-point behaviour.
    /// </para>
    /// </remarks>
    internal sealed class TestVisionCell
    {
        public TestVisionCell(
            TestGroundTruthInferenceProvider groundTruth,
            TestAgentInferenceProvider agent,
            TestMediaProvider media,
            bool offServer,
            bool inlineClipsEnabled)
        {
            m_groundTruth = groundTruth ?? throw new ArgumentNullException(nameof(groundTruth));
            m_agent = agent ?? throw new ArgumentNullException(nameof(agent));
            m_media = media ?? throw new ArgumentNullException(nameof(media));
            m_offServer = offServer;
            m_inlineClipsEnabled = inlineClipsEnabled;
        }

        public const string WorldFrameId = "world";
        public const string BaseFrameId = "robot_base";
        public const string FlangeFrameId = "flange";
        public const string CameraFrameId = "camera_eih";

        public const string SensorBrowseName = "TestCameraTwin";
        public const string PipelineBrowseName = "TestPipeline";
        public const string DeploymentBrowseName = "TestDeployment";
        public const string IntrinsicCalibrationBrowseName = "Intrinsics";
        public const string HandEyeCalibrationBrowseName = "HandEye";
        public const string StreamEndpointBrowseName = "LiveRtsp";
        public const string ClipEndpointBrowseName = "PickFrames";

        public const double CameraWorldX = 0.500;
        public const double CameraWorldY = 0.000;
        public const double CameraWorldZ = 1.100;

        public const uint ImageWidth = 640u;
        public const uint ImageHeight = 480u;

        public async ValueTask ConfigureAsync(
            IVisionBuildContext context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            AddFrames(context);
            AddSensor(context);
            AddPipeline(context);
            await FinalizePipelineAsync(context, cancellationToken).ConfigureAwait(false);
        }


        private static void AddFrames(IVisionBuildContext context)
        {
            IVisionNodeBuilder nodes = context.Nodes;
            nodes.AddFrame("World", frame => frame
                .WithFrameId(WorldFrameId)
                .WithRole(VisionFrameRoleEnum.World)
                .WithTransform(Pose(WorldFrameId, 0.0, 0.0, 0.0)));

            nodes.AddFrame("Base", frame => frame
                .WithFrameId(BaseFrameId)
                .WithRole(VisionFrameRoleEnum.Base)
                .WithParent(WorldFrameId)
                .WithTransform(Pose(WorldFrameId, 0.100, 0.000, 0.400)));

            nodes.AddFrame("Flange", frame => frame
                .WithFrameId(FlangeFrameId)
                .WithRole(VisionFrameRoleEnum.MechanicalInterface)
                .WithParent(BaseFrameId)
                .WithTransform(Pose(BaseFrameId, 0.200, 0.000, 0.500)));

            // Camera-in-flange offset carries the remainder so
            // camera composed to world = (0.500, 0.000, 1.100).
            nodes.AddFrame("CameraEih", frame => frame
                .WithFrameId(CameraFrameId)
                .WithRole(VisionFrameRoleEnum.Camera)
                .WithParent(FlangeFrameId)
                .WithTransform(Pose(FlangeFrameId, 0.200, 0.000, 0.200)));
        }

        private void AddSensor(IVisionBuildContext context)
        {
            IVisionNodeBuilder nodes = context.Nodes;
            VisionIntrinsicsDataType intrinsics = BuildIntrinsics();
            nodes.AddImageSensor(SensorBrowseName, sensor => sensor
                .WithSensorId("test-cam-eih-01")
                .WithModality(VisionSensorModalityEnum.Area2D)
                .WithRealityKind(VisionRealityKindEnum.Simulated)
                .WithManufacturer("OPC Foundation")
                .WithModel("Test Eye-in-Hand Camera")
                .WithSerialNumber("TEST-EIH-640-480-0001")
                .WithDeviceUri("opcua-test://test-cell/cameras/camera_eih")
                .WithFrameId(CameraFrameId)
                .WithResolution(ImageWidth, ImageHeight)
                .WithPixelFormat("Mono8")
                .WithIntrinsics(intrinsics)
                .AddIntrinsicCalibration(IntrinsicCalibrationBrowseName, calibration => calibration
                    .WithCalibrationId("intr-test-cam-01")
                    .WithMethod("Zhang")
                    .WithResidualError(0.25)
                    .WithIntrinsics(intrinsics))
                .AddExtrinsicCalibration(HandEyeCalibrationBrowseName, calibration => calibration
                    .WithCalibrationId("hand-eye-test-cam-01")
                    .WithResidualError(0.0009)
                    .WithMount(VisionCalibrationMountEnum.EyeInHand)
                    .WithFrames(CameraFrameId, FlangeFrameId)
                    .WithTransform(Pose(FlangeFrameId, 0.200, 0.000, 0.200)))
                .AddStreamEndpoint(StreamEndpointBrowseName, endpoint => endpoint
                    .WithEndpointId("stream-live")
                    .WithEndpointUri("rtsp://test-cell.local:554/main")
                    .WithProtocol(VisionStreamProtocolEnum.Rtsp)
                    .WithCodec(VisionVideoCodecEnum.H264)
                    .WithResolution(ImageWidth, ImageHeight)
                    .WithFrameRate(15.0)
                    .WithBitrate(4_000_000u)
                    .WithDefaultProfileName("main"))
                .AddClipEndpoint(ClipEndpointBrowseName, endpoint => endpoint
                    .WithEndpointId("clip-pick-frames")
                    .WithEndpointUri("opcua-inline://test-cell/clips")
                    .WithClipFormat(VisionClipFormatEnum.Png)
                    .WithQuality(90u)
                    .WithResolution(ImageWidth, ImageHeight)
                    .WithInlineDelivery(m_inlineClipsEnabled, maxInlineClipSize: 8_388_608u)
                    .WithDefaultProfileName("PickFrames"))
                .UseMediaProvider(m_media));
        }

        private void AddPipeline(IVisionBuildContext context)
        {
            NodeId deployment = new NodeId(DeploymentBrowseName, context.InstanceNamespaceIndex);
            context.Nodes.AddPipeline(PipelineBrowseName, pipe =>
            {
                pipe.WithPipelineId("pipe-test")
                    .WithSensor(FindSensor(context, SensorBrowseName)?.NodeId ?? NodeId.Null)
                    .WithDeployment(deployment);
                if (m_offServer)
                {
                    pipe.UseInferenceProvider(m_agent, onServer: false)
                        .UseFeedbackSink(m_agent);
                }
                else
                {
                    pipe.UseInferenceProvider(m_groundTruth, onServer: true);
                }
            });
        }

        private async ValueTask FinalizePipelineAsync(
            IVisionBuildContext context, CancellationToken cancellationToken)
        {
            InferencePipelineState pipeline = FindPipeline(context, PipelineBrowseName)
                ?? throw new InvalidOperationException(
                    "Pipeline '" + PipelineBrowseName + "' was not registered.");
            ImageSensorState sensor = FindSensor(context, SensorBrowseName)
                ?? throw new InvalidOperationException(
                    "Sensor '" + SensorBrowseName + "' was not registered.");
            // Results, Feedback and the Method nodes are created and wired by
            // the Vision builder, so the cell only has to find what it needs.
            FolderState results = pipeline.Results
                ?? throw new InvalidOperationException(
                    "The Vision builder must create the pipeline's Results folder.");
            if (m_offServer && pipeline.Feedback == null)
            {
                throw new InvalidOperationException(
                    "The Vision builder must create the pipeline's Feedback object.");
            }
            await default(ValueTask).ConfigureAwait(false);
            NodeId deployment = new NodeId(DeploymentBrowseName, context.InstanceNamespaceIndex);
            var target = new TestInferenceTarget(
                context.Manager,
                context.Context,
                context.InstanceNamespaceIndex,
                pipeline.NodeId,
                sensor.NodeId,
                deployment,
                results,
                CameraFrameId,
                WorldFrameId,
                new[] { CameraWorldX, CameraWorldY, CameraWorldZ });
            if (m_offServer)
            {
                m_agent.Attach(target, ImageWidth, ImageHeight);
            }
            else
            {
                m_groundTruth.Attach(target);
            }
        }

        private static InferencePipelineState? FindPipeline(IVisionBuildContext context, string browseName)
        {
            FolderState? pipelines = context.Root.Pipelines;
            if (pipelines == null)
            {
                return null;
            }
            var children = new List<BaseInstanceState>();
            pipelines.GetChildren(context.Context, children);
            var qualified = new QualifiedName(browseName, context.InstanceNamespaceIndex);
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii] is InferencePipelineState pipeline && pipeline.BrowseName == qualified)
                {
                    return pipeline;
                }
            }
            return null;
        }

        private static ImageSensorState? FindSensor(IVisionBuildContext context, string browseName)
        {
            FolderState? sensors = context.Root.Sensors;
            if (sensors == null)
            {
                return null;
            }
            var children = new List<BaseInstanceState>();
            sensors.GetChildren(context.Context, children);
            var qualified = new QualifiedName(browseName, context.InstanceNamespaceIndex);
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii] is ImageSensorState imageSensor && imageSensor.BrowseName == qualified)
                {
                    return imageSensor;
                }
            }
            return null;
        }


        private static VisionPose3DDataType Pose(
            string frameId, double x, double y, double z)
        {
            return new VisionPose3DDataType
            {
                FrameId = frameId,
                Position = new[] { x, y, z }.ToArrayOf(),
                Orientation = s_identityOrientation.ToArrayOf(),
                Covariance = ArrayOf<double>.Empty
            };
        }

        private static VisionIntrinsicsDataType BuildIntrinsics()
        {
            return new VisionIntrinsicsDataType
            {
                Fx = 600.0,
                Fy = 600.0,
                Cx = ImageWidth / 2.0,
                Cy = ImageHeight / 2.0,
                Skew = 0.0,
                DistortionModel = VisionDistortionModelEnum.None,
                DistortionCoefficients = ArrayOf<double>.Empty,
                Width = ImageWidth,
                Height = ImageHeight
            };
        }

        private static readonly double[] s_identityOrientation = [0.0, 0.0, 0.0, 1.0];

        private readonly TestGroundTruthInferenceProvider m_groundTruth;
        private readonly TestAgentInferenceProvider m_agent;
        private readonly TestMediaProvider m_media;
        private readonly bool m_offServer;
        private readonly bool m_inlineClipsEnabled;
    }
}
