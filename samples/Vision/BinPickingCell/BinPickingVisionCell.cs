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
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1812",
        Justification = "Instantiated by the DI container via AddSingleton.")]
    internal sealed class BinPickingVisionCell
    {
        public BinPickingVisionCell(
            ILogger<BinPickingVisionCell> logger,
            BinPickingMediaProvider mediaProvider,
            BinPickingCellStage stage)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_mediaProvider = mediaProvider ?? throw new ArgumentNullException(nameof(mediaProvider));
            m_stage = stage ?? throw new ArgumentNullException(nameof(stage));
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
            await AttachSimulatedTwinAsync(context, cancellationToken).ConfigureAwait(false);
            m_logger.VisionCellReady(
                m_frames.Count,
                m_stage.CellStagePath,
                m_mediaProvider.Backend);
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
                .WithTransform(Pose(BinPickingRobotCell.WorldFrameId, 0.0, 0.0, 0.829)));
            m_frames.Add(BinPickingRobotCell.RobotBaseFrameId);

            nodes.AddFrame("Flange", frame => frame
                .WithFrameId(BinPickingRobotCell.FlangeFrameId)
                .WithRole(VisionFrameRoleEnum.MechanicalInterface)
                .WithParent(BinPickingRobotCell.RobotBaseFrameId)
                .WithTransform(Pose(BinPickingRobotCell.RobotBaseFrameId, 0.0, 0.0, 0.1625)));
            m_frames.Add(BinPickingRobotCell.FlangeFrameId);

            nodes.AddFrame("GripperTcp", frame => frame
                .WithFrameId(BinPickingRobotCell.ToolFrameId)
                .WithRole(VisionFrameRoleEnum.Tool)
                .WithParent(BinPickingRobotCell.FlangeFrameId)
                .WithTransform(Pose(BinPickingRobotCell.FlangeFrameId, 0.0, 0.0, 0.115)));
            m_frames.Add(BinPickingRobotCell.ToolFrameId);

            nodes.AddFrame("CameraEih", frame => frame
                .WithFrameId(BinPickingRobotCell.CameraFrameId)
                .WithRole(VisionFrameRoleEnum.Camera)
                .WithParent(BinPickingRobotCell.FlangeFrameId)
                .WithTransform(HandEyeTransform()));
            m_frames.Add(BinPickingRobotCell.CameraFrameId);
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
                .WithResolution(2448u, 2048u)
                .WithPixelFormat(PixelFormat)
                .WithIntrinsics(intrinsics)
                .WithOptics(optics => optics
                    .WithFocalLength(0.01224)
                    .WithAperture(2.8)
                    .WithWorkingDistance(0.35)
                    .WithLensType("Fixed C-mount")
                    .WithMountType("C"))
                .AddIntrinsicCalibration(IntrinsicCalibrationBrowseName, calibration => calibration
                    .WithCalibrationId("intr-cam-eih-01-2448")
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
                    .WithResolution(2448u, 2048u)
                    .WithFrameRate(15.0)
                    .WithBitrate(24_000_000u)
                    .WithProfileName("main"))
                .AddClipEndpoint(ClipEndpointBrowseName, endpoint => endpoint
                    .WithEndpointId("clip-pick-frames")
                    .WithEndpointUri("opcua-inline://binpicking-cell/clips")
                    .WithClipFormat(VisionClipFormatEnum.Png)
                    .WithQuality(90u)
                    .WithResolution(1280u, 1024u)
                    .WithInlineDelivery(enabled: true, maxInlineClipSize: 8_388_608u)
                    .WithProfileName("PickFrames"))
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
            return new VisionIntrinsicsDataType
            {
                Fx = 2140.5,
                Fy = 2139.8,
                Cx = 1223.1,
                Cy = 1021.7,
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
                Width = 2448u,
                Height = 2048u
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

        private static readonly double[] s_identityOrientation = [0.0, 0.0, 0.0, 1.0];
        private static readonly double[] s_handEyePosition = [0.062, -0.031, 0.115];
        private static readonly double[] s_handEyeOrientation = [0.0, 0.0, 0.7071, 0.7071];

        internal const string SensorTwinBrowseName = "BinPickingCameraTwin";
        internal const string IntrinsicCalibrationBrowseName = "Intrinsics2448x2048";
        internal const string HandEyeCalibrationBrowseName = "HandEye";
        internal const string StreamEndpointBrowseName = "LiveRtsp";
        internal const string ClipEndpointBrowseName = "PickFrames";
        internal const string PixelFormat = "BayerRG8";
        internal const string CameraPrimPath =
            "/World/Robot/Arm/Base/J1/J2/J3/J4/J5/J6/Flange/Camera";

        private readonly ILogger<BinPickingVisionCell> m_logger;
        private readonly BinPickingMediaProvider m_mediaProvider;
        private readonly BinPickingCellStage m_stage;
        private readonly List<string> m_frames = [];
    }

    internal static partial class BinPickingVisionCellLog
    {
        [LoggerMessage(EventId = BinPickingCellEventIds.Configurator + 10,
            Level = LogLevel.Information,
            Message = "Vision side of BinPickingCell ready ({FrameCount} frames, " +
                "stage {StageIdentifier}, backend {Backend}).")]
        public static partial void VisionCellReady(
            this ILogger<BinPickingVisionCell> logger,
            int frameCount, string stageIdentifier, SceneCameraCaptureBackend backend);
    }
}
