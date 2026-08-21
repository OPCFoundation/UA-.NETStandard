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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Vision;
using Opc.Ua.Vision.OpenUsd;
using Opc.Ua.Vision.Server;
using Opc.Ua.Vision.Server.Builders;

namespace Vision.BinPickingCell
{
    /// <summary>
    /// Vision side of the bin-picking cell. Materialises the frame tree,
    /// the eye-in-hand camera sensor twin, the intrinsic and hand-eye
    /// calibrations, and the media endpoints from the OPC UA
    /// Robotics-Vision Addendum's worked example.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The concrete values (focal lengths, distortion coefficients,
    /// hand-eye pose, residual errors) are the ones the addendum ships;
    /// they identify this cell as the reference example rather than an
    /// arbitrary rig. The sensor is registered as a
    /// <see cref="VisionRealityKindEnum.Simulated"/> twin: it renders
    /// from a USD stage via the OpenUSD offscreen capture provider, and
    /// carries an <c>IVisionSimulatedType</c> interface pointing at the
    /// stage and camera prim so a client can see the twin metadata.
    /// </para>
    /// <para>
    /// The frame identifiers <c>world</c>, <c>robot_base</c>,
    /// <c>flange</c>, <c>gripper_tcp</c> and <c>camera_eih</c> match the
    /// addendum and the frame names published by
    /// <see cref="BinPickingRobotCell"/> — a client can walk from the
    /// vision-side calibration to the robot-side frame without any
    /// translation table.
    /// </para>
    /// <para>
    /// The vision-side <c>flange</c> transform is authored at the "scan
    /// pose" the arm would hold to point the eye-in-hand camera at the
    /// bin. In a live cell the flange frame is dynamic and reflects the
    /// current joint state; for this static-sample demo it is pinned to
    /// the scan pose so a consumer composing
    /// camera → flange → robot_base → world lands on the parts'
    /// authored world positions — which is exactly what the
    /// <see cref="BinPickingGroundTruthInferenceProvider"/> reports for
    /// each detection's <c>Pose</c>.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1812",
        Justification = "Instantiated by the DI container via AddSingleton.")]
    internal sealed class BinPickingVisionCell
    {
        public BinPickingVisionCell(
            ILogger<BinPickingVisionCell> logger,
            BinPickingMediaProvider mediaProvider,
            BinPickingCellStage stage,
            BinPickingGroundTruthInferenceProvider inferenceProvider,
            BinPickingAgentInferenceProvider agentProvider,
            BinPickingCellOptions options)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_mediaProvider = mediaProvider ?? throw new ArgumentNullException(nameof(mediaProvider));
            m_stage = stage ?? throw new ArgumentNullException(nameof(stage));
            m_inferenceProvider = inferenceProvider
                ?? throw new ArgumentNullException(nameof(inferenceProvider));
            m_agentProvider = agentProvider ?? throw new ArgumentNullException(nameof(agentProvider));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Configures the Vision node manager for this cell.
        /// </summary>
        public async ValueTask ConfigureAsync(IVisionBuildContext context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            AddFrames(context);
            AddSensor(context);
            AddPipeline(context);
            await AttachSimulatedTwinAsync(context, cancellationToken).ConfigureAwait(false);
            await FinalizePipelineAsync(context, cancellationToken).ConfigureAwait(false);
            m_logger.VisionCellReady(
                m_frames.Count,
                m_stage.CellStagePath,
                m_mediaProvider.Backend,
                m_options.InferenceLocation);
        }

        private void AddFrames(IVisionBuildContext context)
        {
            IVisionNodeBuilder nodes = context.Nodes;
            nodes.AddFrame("World", frame => frame
                .WithFrameId(BinPickingRobotCell.WorldFrameId)
                .WithRole(VisionFrameRoleEnum.World)
                .WithTransform(Pose(BinPickingRobotCell.WorldFrameId, 0.0, 0.0, 0.0)));
            m_frames.Add(BinPickingRobotCell.WorldFrameId);

            nodes.AddFrame("RobotBase", frame => frame
                .WithFrameId(BinPickingRobotCell.RobotBaseFrameId)
                .WithRole(VisionFrameRoleEnum.Base)
                .WithParent(BinPickingRobotCell.WorldFrameId)
                .WithTransform(Pose(
                    BinPickingRobotCell.WorldFrameId,
                    0.0,
                    0.0,
                    BinPickingRobotCell.RobotBaseHeightMetres)));
            m_frames.Add(BinPickingRobotCell.RobotBaseFrameId);

            nodes.AddFrame("Flange", frame => frame
                .WithFrameId(BinPickingRobotCell.FlangeFrameId)
                .WithRole(VisionFrameRoleEnum.MechanicalInterface)
                .WithParent(BinPickingRobotCell.RobotBaseFrameId)
                .WithTransform(FlangeScanPose()));
            m_frames.Add(BinPickingRobotCell.FlangeFrameId);

            nodes.AddFrame("GripperTcp", frame => frame
                .WithFrameId(BinPickingRobotCell.ToolFrameId)
                .WithRole(VisionFrameRoleEnum.Tool)
                .WithParent(BinPickingRobotCell.FlangeFrameId)
                .WithTransform(Pose(
                    BinPickingRobotCell.FlangeFrameId,
                    BinPickingPalletizerGeometry.FlangeToTcpMetres,
                    0.0,
                    0.0)));
            m_frames.Add(BinPickingRobotCell.ToolFrameId);

            nodes.AddFrame("CameraEih", frame => frame
                .WithFrameId(BinPickingRobotCell.CameraFrameId)
                .WithRole(VisionFrameRoleEnum.Camera)
                .WithParent(BinPickingRobotCell.FlangeFrameId)
                .WithTransform(HandEyeTransform()));
            m_frames.Add(BinPickingRobotCell.CameraFrameId);
        }

        private void AddPipeline(IVisionBuildContext context)
        {
            NodeId deployment = new NodeId(DeploymentBrowseName, context.InstanceNamespaceIndex);
            bool offServer = m_options.InferenceLocation == BinPickingInferenceLocation.EdgeOffServer;
            context.Nodes.AddPipeline(PipelineBrowseName, pipe =>
            {
                pipe.WithPipelineId(PipelineId)
                    .WithSensor(FindSensor(context, SensorTwinBrowseName)?.NodeId ?? NodeId.Null)
                    .WithDeployment(deployment);
                if (offServer)
                {
                    // Off-server perception: publish the OffServer facet, and register the same
                    // agent object as both provider (so RunInference explains the mode with
                    // BadNotSupported) and feedback sink (so SubmitDetections/Correction arrive
                    // at a single owner). The ground-truth provider is not wired — the two
                    // paths never run at the same time.
                    pipe.UseInferenceProvider(m_agentProvider, onServer: false)
                        .UseFeedbackSink(m_agentProvider);
                }
                else
                {
                    // On-server ground truth: publish the OnServer facet and register the
                    // deterministic detector. No feedback sink — a client cannot submit
                    // detections when nothing on the Server side is designed to consume them.
                    pipe.UseInferenceProvider(m_inferenceProvider, onServer: true);
                }
            });
        }

        private async ValueTask FinalizePipelineAsync(
            IVisionBuildContext context, CancellationToken cancellationToken)
        {
            InferencePipelineState pipeline = FindPipeline(context, PipelineBrowseName)
                ?? throw new InvalidOperationException(
                    "Pipeline '" + PipelineBrowseName + "' was not registered on the Vision node manager.");
            ImageSensorState sensor = FindSensor(context, SensorTwinBrowseName)
                ?? throw new InvalidOperationException(
                    "Sensor '" + SensorTwinBrowseName + "' was not registered on the Vision node manager.");
            // The Vision builder creates and registers the Results folder for any
            // pipeline that has an inference provider, so the cell only looks it up.
            FolderState results = pipeline.Results
                ?? throw new InvalidOperationException(
                    "The Vision builder must create the pipeline's Results folder.");
            await ValueTask.CompletedTask.ConfigureAwait(false);
            VisionIntrinsicsDataType intrinsics = BuildIntrinsics();
            NodeId deployment = new NodeId(DeploymentBrowseName, context.InstanceNamespaceIndex);
            var target = new BinPickingInferenceTarget(
                context.Manager,
                context.Context,
                context.InstanceNamespaceIndex,
                pipeline.NodeId,
                sensor.NodeId,
                deployment,
                results,
                BinPickingRobotCell.CameraFrameId,
                PixelFormat,
                intrinsics.Fx,
                intrinsics.Fy,
                intrinsics.Cx,
                intrinsics.Cy,
                intrinsics.Width,
                intrinsics.Height,
                CameraInWorldPose());
            if (m_options.InferenceLocation == BinPickingInferenceLocation.EdgeOffServer)
            {
                m_agentProvider.Attach(target);
            }
            else
            {
                m_inferenceProvider.Attach(target);
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
            foreach (BaseInstanceState child in children)
            {
                if (child is InferencePipelineState pipeline && pipeline.BrowseName == qualified)
                {
                    return pipeline;
                }
            }
            return null;
        }

        private void AddSensor(IVisionBuildContext context)
        {
            IVisionNodeBuilder nodes = context.Nodes;
            VisionIntrinsicsDataType intrinsics = BuildIntrinsics();
            nodes.AddImageSensor(SensorTwinBrowseName, sensor => sensor
                .WithSensorId("cam-eih-01")
                .WithModality(VisionSensorModalityEnum.Area2D)
                .WithRealityKind(VisionRealityKindEnum.Simulated)
                .WithManufacturer("OPC Foundation")
                .WithModel("Simulated Eye-in-Hand Camera")
                .WithSerialNumber("SIM-EIH-2448-2048-0001")
                .WithDeviceUri("opcua-openusd://binpicking-cell/cameras/camera_eih")
                .WithFrameId(BinPickingRobotCell.CameraFrameId)
                .WithResolution(SensorWidth, SensorHeight)
                .WithPixelFormat(PixelFormat)
                .WithIntrinsics(intrinsics)
                .WithOptics(optics => optics
                    .WithFocalLength(0.01224)
                    .WithAperture(2.8)
                    .WithWorkingDistance(0.35)
                    .WithLensType("Fixed C-mount")
                    .WithMountType("C"))
                .AddIntrinsicCalibration(IntrinsicCalibrationBrowseName, calibration => calibration
                    .WithCalibrationId("intr-cam-eih-01-612")
                    .WithMethod("Zhang")
                    .WithResidualError(0.21)
                    .WithIntrinsics(intrinsics))
                .AddExtrinsicCalibration(HandEyeCalibrationBrowseName, calibration => calibration
                    .WithCalibrationId("hand-eye-cam-eih-01")
                    .WithResidualError(0.0008)
                    .WithMount(VisionCalibrationMountEnum.EyeInHand)
                    .WithFrames(BinPickingRobotCell.CameraFrameId, BinPickingRobotCell.FlangeFrameId)
                    .WithTransform(HandEyeTransform()))
                .AddStreamEndpoint(StreamEndpointBrowseName, endpoint => endpoint
                    .WithEndpointId("stream-live")
                    .WithEndpointUri("rtsp://simulated-eih.local:554/main")
                    .WithProtocol(VisionStreamProtocolEnum.Rtsp)
                    .WithCodec(VisionVideoCodecEnum.H264)
                    .WithResolution(SensorWidth, SensorHeight)
                    .WithFrameRate(15.0)
                    .WithBitrate(24_000_000u)
                    .WithDefaultProfileName("main"))
                .AddClipEndpoint(ClipEndpointBrowseName, endpoint => endpoint
                    .WithEndpointId("clip-pick-frames")
                    .WithEndpointUri("opcua-inline://binpicking-cell/clips")
                    .WithClipFormat(VisionClipFormatEnum.Png)
                    .WithQuality(90u)
                    .WithResolution(SensorWidth, SensorHeight)

                    // The PNG this cell renders is well under a megabyte, but allow
                    // headroom for a busier scene rather than have the Server refuse its
                    // own frames. Note this ceiling was never what refused GetClip - the
                    // frame was already under it - see MaxInlineClipBytes.
                    .WithInlineDelivery(enabled: true, maxInlineClipSize: MaxInlineClipBytes)
                    .WithDefaultProfileName("PickFrames"))
                .UseMediaProvider(m_mediaProvider));
        }

        private async ValueTask AttachSimulatedTwinAsync(
            IVisionBuildContext context, CancellationToken cancellationToken)
        {
            ImageSensorState? sensor = FindSensor(context, SensorTwinBrowseName);
            if (sensor == null)
            {
                throw new InvalidOperationException(
                    "Sensor '" + SensorTwinBrowseName + "' was not registered on the Vision node manager.");
            }
            IVisionSimulatedState simulated = OpcUaVisionExtensions.CreateInstanceOfIVisionSimulatedType(
                context.Context,
                sensor,
                new QualifiedName("Simulated", context.VisionNamespaceIndex));
            simulated.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasInterface;
            simulated.NodeId = context.Context.RequireNodeIdFactory().New(context.Context, simulated);
            simulated.CreateOrReplaceSimulatorUri(context.Context, null!).Value =
                "opcua-openusd://binpicking-cell";
            simulated.CreateOrReplaceStageIdentifier(context.Context, null!).Value = m_stage.CellStagePath;
            simulated.CreateOrReplacePrimPath(context.Context, null!).Value = CameraPrimPath;
            simulated.CreateOrReplaceGroundTruthAvailable(context.Context, null!).Value = true;
            sensor.AddChild(simulated);
            await context.Manager.AddPredefinedNodeAsync(simulated, cancellationToken).ConfigureAwait(false);
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
            foreach (BaseInstanceState child in children)
            {
                if (child is ImageSensorState imageSensor && imageSensor.BrowseName == qualified)
                {
                    return imageSensor;
                }
            }
            return null;
        }

        private static VisionIntrinsicsDataType BuildIntrinsics()
        {
            // Calibrated on the native 2448x2048 grid, then scaled to the binned grid the
            // camera actually delivers. Binning is an exact integer decimation, so the
            // focal lengths and principal point divide by the bin factor and the
            // Brown-Conrady coefficients - which are expressed in normalised image
            // coordinates - carry over unchanged.
            const double bin = SensorBinning;
            return new VisionIntrinsicsDataType
            {
                Fx = 2140.5 / bin,
                Fy = 2139.8 / bin,
                Cx = 1223.1 / bin,
                Cy = 1021.7 / bin,
                Skew = 0.0,
                DistortionModel = VisionDistortionModelEnum.BrownConrady,
                DistortionCoefficients = new[]
                {
                    -0.1721,
                    0.0934,
                    0.0002,
                    -0.0001,
                    -0.0188
                }.ToArrayOf(),
                Width = SensorWidth,
                Height = SensorHeight
            };
        }

        private static VisionPose3DDataType Pose(string frameId, double x, double y, double z)
        {
            return new VisionPose3DDataType
            {
                FrameId = frameId,
                Position = new[] { x, y, z }.ToArrayOf(),
                Orientation = s_identityOrientation.ToArrayOf(),
                Covariance = ArrayOf<double>.Empty
            };
        }

        private static VisionPose3DDataType HandEyeTransform()
        {
            return new VisionPose3DDataType
            {
                FrameId = BinPickingRobotCell.FlangeFrameId,
                Position = s_handEyePosition.ToArrayOf(),
                Orientation = s_handEyeOrientation.ToArrayOf(),
                Covariance = ArrayOf<double>.Empty
            };
        }

        private static VisionPose3DDataType FlangeScanPose()
        {
            return new VisionPose3DDataType
            {
                FrameId = BinPickingRobotCell.RobotBaseFrameId,
                Position = s_flangeScanPosition.ToArrayOf(),
                Orientation = s_flangeScanOrientation.ToArrayOf(),
                Covariance = ArrayOf<double>.Empty
            };
        }

        private static VisionPose3DDataType CameraInWorldPose()
        {
            return new VisionPose3DDataType
            {
                FrameId = BinPickingRobotCell.WorldFrameId,
                Position = s_cameraInWorldPosition.ToArrayOf(),
                Orientation = s_cameraInWorldOrientation.ToArrayOf(),
                Covariance = ArrayOf<double>.Empty
            };
        }

        private static readonly double[] s_identityOrientation = [0.0, 0.0, 0.0, 1.0];
        private static readonly double[] s_handEyePosition = [0.020, 0.0, 0.160];
        private static readonly double[] s_handEyeOrientation = [RootHalf, 0.0, RootHalf, 0.0];
        private static readonly double[] s_cameraInWorldPosition =
            [BinPickingPartsCatalog.BinCentreX, -0.160, 1.405];

        // The palletizer flange is tool-down and rolled 90 degrees at the scan pose.
        // Composing the authored hand-eye transform places the camera 160 mm toward -Y
        // and 20 mm below the flange, looking straight down with a -90 degree image roll.
        private static readonly double[] s_cameraInWorldOrientation =
            [RootHalf, -RootHalf, 0.0, 0.0];

        // Analytic palletizer home: TCP (0.60, 0, 0.32), wrist/flange 185 mm above it.
        private static readonly double[] s_flangeScanPosition = [0.600, 0.0, 0.505];
        private static readonly double[] s_flangeScanOrientation =
            [0.5, 0.5, -0.5, 0.5];

        // A 90-degree rotation, to full double precision. Writing it as 0.7071 leaves the
        // quaternion with a norm of 0.99999041, which is 9.6e-6 off unit - ten times the
        // 1e-6 tolerance the pose validator enforces - so composing a detection through
        // these frames fails with BadOutOfRange.
        private const double RootHalf = 0.70710678118654752;

        internal const string SensorTwinBrowseName = "BinPickingCameraTwin";

        // The simulated device is a 2448x2048 area-scan camera, but it is operated with
        // 4x4 binning, which is what an industrial camera does when a bin-picking cycle
        // needs frame rate more than it needs pixels. Every resolution the model reports -
        // sensor, stream endpoint, clip endpoint, intrinsics - is the binned one, because
        // that is what the device delivers. The native size survives only in the model and
        // serial number, where it identifies the hardware rather than the image.
        //
        // These used to disagree: the sensor declared 2448x2048, the clip endpoint
        // 1280x1024, and the renderer produced 640x512. The detector projects through the
        // declared intrinsics, so its boxes landed in 2448x2048 space while an agent was
        // handed a 640x512 picture - the coordinates pointed off the image it could see.
        // 640x512 was not even the same aspect ratio as the camera it claimed to be.
        internal const uint NativeSensorWidth = 2448u;
        internal const uint NativeSensorHeight = 2048u;
        internal const uint SensorBinning = 4u;
        internal const uint SensorWidth = NativeSensorWidth / SensorBinning;
        internal const uint SensorHeight = NativeSensorHeight / SensorBinning;

        // The clip endpoint serves 612x512 PNGs. Allow headroom for a busier scene
        // rather than have the Server refuse its own frames.
        internal const uint MaxInlineClipBytes = 32u * 1024u * 1024u;
        internal const string IntrinsicCalibrationBrowseName = "Intrinsics612x512";
        internal const string HandEyeCalibrationBrowseName = "HandEye";
        internal const string StreamEndpointBrowseName = "LiveRtsp";
        internal const string ClipEndpointBrowseName = "PickFrames";
        internal const string PipelineBrowseName = "BinPickingPipeline";
        internal const string PipelineId = "pipe-onserver-groundtruth";
        internal const string DeploymentBrowseName = "OnServerDeployment";
        internal const string PixelFormat = "BayerRG8";
        internal const string CameraPrimPath =
            "/World/Robot/Palletizer/Base/J1/J2/J3/Leveling/J4/Flange/Camera";

        private readonly ILogger<BinPickingVisionCell> m_logger;
        private readonly BinPickingMediaProvider m_mediaProvider;
        private readonly BinPickingCellStage m_stage;
        private readonly BinPickingGroundTruthInferenceProvider m_inferenceProvider;
        private readonly BinPickingAgentInferenceProvider m_agentProvider;
        private readonly BinPickingCellOptions m_options;
        private readonly List<string> m_frames = [];
    }

    internal static partial class BinPickingVisionCellLog
    {
        [LoggerMessage(EventId = BinPickingCellEventIds.Configurator + 10,
            Level = LogLevel.Information,
            Message = "Vision side of BinPickingCell ready ({FrameCount} frames, " +
                "stage {StageIdentifier}, backend {Backend}, InferenceLocation={InferenceLocation}).")]
        public static partial void VisionCellReady(
            this ILogger<BinPickingVisionCell> logger,
            int frameCount, string stageIdentifier, SceneCameraCaptureBackend backend,
            BinPickingInferenceLocation inferenceLocation);
    }
}
