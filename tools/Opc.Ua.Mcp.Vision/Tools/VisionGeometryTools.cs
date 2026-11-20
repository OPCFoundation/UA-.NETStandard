// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.Perception;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for composing and inspecting Vision coordinate frames.
    /// </summary>
    [McpServerToolType]
    public sealed class VisionGeometryTools
    {
        /// <summary>
        /// Reads a single frame node from the Vision frame graph.
        /// </summary>
        [McpServerTool(Name = "vision_read_frame")]
        [Description("Reads a coordinate frame from the Vision frame graph: FrameId, ParentFrameId, and " +
            "the six-degree-of-freedom Transform relative to the parent. Use this to inspect the tree " +
            "without composing anything. Use vision_compose_pose instead when translating a pose from one " +
            "named frame into another, and vision_compose_transform to obtain only the transform between " +
            "two frames. Reports what the server declares; a null-NodeId parent means the frame is a root. " +
            "Returns a VisionFrameSnapshot.")]
        public static Task<VisionFrameSnapshot> ReadFrameAsync(
            VisionClientAccessor accessor,
            [Description("Frame NodeId, for example ns=2;s=Vision/Frames/RobotBase.")] string frameNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionFrameGraph graph = accessor.OpenFrames(sessionName);
            return graph.ReadAsync(OpcUaJsonHelper.ParseNodeId(frameNodeId), ct);
        }

        /// <summary>
        /// Composes a pose from one Vision frame into another.
        /// </summary>
        [McpServerTool(Name = "vision_compose_pose")]
        [Description("Composes a pose expressed in one Vision frame into a target frame by walking the " +
            "frame graph. Use this to convert a detection expressed in camera coordinates into robot base " +
            "or tool-centre-point coordinates so a controller can act on it. Use vision_compose_transform " +
            "instead when you only need the transform between the two frames. Pose JSON is a single " +
            "object: { frameId, position:[x,y,z], orientation:[x,y,z,w], covariance:[36 doubles or omit] }. " +
            "Reports the server's refusal honestly if the frames are not connected; never invents an " +
            "identity transform. Returns the transformed VisionPose3DDataType.")]
        public static Task<VisionPose3DDataType> ComposePoseAsync(
            VisionClientAccessor accessor,
            [Description("JSON pose in the source frame; the frameId field is optional and, when set, " +
                "must match fromFrameNodeId's FrameId.")]
            string poseJson,
            [Description("Source frame NodeId (the one the input pose is expressed in).")] string fromFrameNodeId,
            [Description("Target frame NodeId (the one the returned pose is expressed in).")] string toFrameNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionPose3DDataType pose = VisionJson.BuildPose(poseJson, nameof(poseJson));
            VisionFrameGraph graph = accessor.OpenFrames(sessionName);
            return graph.ComposeAsync(
                pose,
                OpcUaJsonHelper.ParseNodeId(fromFrameNodeId),
                OpcUaJsonHelper.ParseNodeId(toFrameNodeId),
                ct);
        }

        /// <summary>
        /// Composes the transform between two Vision frames.
        /// </summary>
        [McpServerTool(Name = "vision_compose_transform")]
        [Description("Composes the six-degree-of-freedom transform between two Vision frames by walking " +
            "the frame graph and returns it as a VisionPose3DDataType with FrameId set to the target " +
            "frame. Use vision_compose_pose instead when you also have a pose expressed in the source " +
            "frame that you want transformed. Reports the server's refusal honestly if the frames are not " +
            "connected; never invents an identity transform. Returns the composed transform.")]
        public static Task<VisionPose3DDataType> ComposeTransformAsync(
            VisionClientAccessor accessor,
            [Description("Source frame NodeId.")] string fromFrameNodeId,
            [Description("Target frame NodeId.")] string toFrameNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionFrameGraph graph = accessor.OpenFrames(sessionName);
            return graph.ComposeTransformAsync(
                OpcUaJsonHelper.ParseNodeId(fromFrameNodeId),
                OpcUaJsonHelper.ParseNodeId(toFrameNodeId),
                ct);
        }
    }
}
