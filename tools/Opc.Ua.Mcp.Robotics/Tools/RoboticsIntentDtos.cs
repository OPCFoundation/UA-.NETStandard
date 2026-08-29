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

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Mcp.Tools
{
    // CLR arrays are intentional at this JSON boundary: the MCP schema generator exposes
    // ArrayOf<T> as its backing memory object and cannot bind an incoming JSON array to it.
    // The converter immediately projects these values into ArrayOf<T> for OPC UA APIs.

    /// <summary>
    /// Detail level for list operations.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<DetailLevel>))]
    public enum DetailLevel
    {
        /// <summary>
        /// Return concise summaries (default).
        /// </summary>
        Summary,

        /// <summary>
        /// Return full snapshots.
        /// </summary>
        Full
    }

    /// <summary>
    /// Work selector for filtering operations or missions.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<WorkSelector>))]
    public enum WorkSelector
    {
        /// <summary>
        /// Return all operations/missions regardless of state.
        /// </summary>
        All,

        /// <summary>
        /// Return only active (non-terminal) operations/missions.
        /// </summary>
        Active,

        /// <summary>
        /// Return only terminal (succeeded/cancelled/failed) operations/missions.
        /// </summary>
        Terminal
    }

    /// <summary>
    /// Discriminator for intent kinds in missions and submit tools.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<IntentKind>))]
    public enum IntentKind
    {
        /// <summary>
        /// Point-to-point joint motion.
        /// </summary>
        JointMove,

        /// <summary>
        /// Straight Cartesian segment.
        /// </summary>
        LinearMove,

        /// <summary>
        /// Circular arc through a via point.
        /// </summary>
        CircularMove,

        /// <summary>
        /// Time-parameterised joint trajectory.
        /// </summary>
        Trajectory,

        /// <summary>
        /// Multi-waypoint Cartesian path.
        /// </summary>
        CartesianPath,

        /// <summary>
        /// Force-controlled contact motion.
        /// </summary>
        Force,

        /// <summary>
        /// Continuous arc welding.
        /// </summary>
        ArcWeld,

        /// <summary>
        /// Discrete spot welding.
        /// </summary>
        SpotWeld,

        /// <summary>
        /// Material dispensing.
        /// </summary>
        Dispense,

        /// <summary>
        /// Fastening/joining.
        /// </summary>
        Fasten,

        /// <summary>
        /// Pattern-based palletising.
        /// </summary>
        Palletise,

        /// <summary>
        /// Surface finishing (sanding, polishing).
        /// </summary>
        SurfaceFinish,

        /// <summary>
        /// Close or activate a gripper/tool.
        /// </summary>
        Grasp,

        /// <summary>
        /// Open or deactivate a gripper/tool.
        /// </summary>
        Release,

        /// <summary>
        /// Inbound pick from a source location.
        /// </summary>
        Pick,

        /// <summary>
        /// Outbound place at a destination location.
        /// </summary>
        Place,

        /// <summary>
        /// Fit or release a docked tool.
        /// </summary>
        ToolChange,

        /// <summary>
        /// Write a controller output signal.
        /// </summary>
        SetOutput,

        /// <summary>
        /// Call a server-side program.
        /// </summary>
        CallProgram,

        /// <summary>
        /// Time or signal wait.
        /// </summary>
        Wait
    }

    /// <summary>
    /// A typed named value: name + dataType tag + JSON payload.
    /// </summary>
    public sealed class NamedTypedValueDto
    {
        /// <summary>
        /// Gets or sets the attribute or argument name.
        /// </summary>
        [Description("Attribute or argument name.")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the OPC UA data type name.
        /// </summary>
        [Description("OPC UA data type: Boolean, Int32, UInt32, Double, String, Float, " +
            "Int16, UInt16, Int64, UInt64.")]
        public string? DataType { get; set; }

        /// <summary>
        /// Gets or sets the value as a JSON element.
        /// </summary>
        [Description("The value.")]
        public JsonElement Value { get; set; }
    }

    /// <summary>
    /// A 3-D position as [x, y, z] in metres.
    /// </summary>
    public sealed class PosePositionDto
    {
        /// <summary>
        /// Gets or sets the X coordinate in metres.
        /// </summary>
        [Description("X coordinate in metres.")]
        public double X { get; set; }

        /// <summary>
        /// Gets or sets the Y coordinate in metres.
        /// </summary>
        [Description("Y coordinate in metres.")]
        public double Y { get; set; }

        /// <summary>
        /// Gets or sets the Z coordinate in metres.
        /// </summary>
        [Description("Z coordinate in metres.")]
        public double Z { get; set; }
    }

    /// <summary>
    /// A quaternion orientation [x, y, z, w].
    /// </summary>
    public sealed class QuaternionDto
    {
        /// <summary>
        /// Gets or sets the X quaternion component.
        /// </summary>
        [Description("Quaternion X component.")]
        public double X { get; set; }

        /// <summary>
        /// Gets or sets the Y quaternion component.
        /// </summary>
        [Description("Quaternion Y component.")]
        public double Y { get; set; }

        /// <summary>
        /// Gets or sets the Z quaternion component.
        /// </summary>
        [Description("Quaternion Z component.")]
        public double Z { get; set; }

        /// <summary>
        /// Gets or sets the W quaternion component.
        /// </summary>
        [Description("Quaternion W component.")]
        public double W { get; set; }
    }

    /// <summary>
    /// A 3-D pose: position in metres and quaternion orientation, with optional frame reference.
    /// </summary>
    public sealed class PoseDto
    {
        /// <summary>
        /// Gets or sets the position in metres.
        /// </summary>
        [Description("Position in metres. Required.")]
        public PosePositionDto? Position { get; set; }

        /// <summary>
        /// Gets or sets the quaternion orientation.
        /// </summary>
        [Description("Unit quaternion orientation [x, y, z, w]. Required.")]
        public QuaternionDto? Orientation { get; set; }

        /// <summary>
        /// Gets or sets the optional frame name or NodeId the pose is expressed in.
        /// </summary>
        [Description("Optional frame selector: published FrameId, frame name/BrowseName, or NodeId.")]
        public string? FrameId { get; set; }
    }

    /// <summary>
    /// Motion constraints shared by all motion intents.
    /// </summary>
    public sealed class MotionConstraintsDto
    {
        /// <summary>
        /// Gets or sets the speed fraction (0..1].
        /// </summary>
        [Description("Speed fraction within [0, 1]; 0 leaves the controller default.")]
        public double SpeedFraction { get; set; }

        /// <summary>
        /// Gets or sets the Cartesian speed limit in m/s.
        /// </summary>
        [Description("Cartesian speed limit in m/s.")]
        public double CartesianSpeed { get; set; }

        /// <summary>
        /// Gets or sets the Cartesian acceleration limit in m/s².
        /// </summary>
        [Description("Cartesian acceleration limit in m/s².")]
        public double CartesianAcceleration { get; set; }

        /// <summary>
        /// Gets or sets the jerk limit.
        /// </summary>
        [Description("Jerk limit.")]
        public double Jerk { get; set; }
    }

    /// <summary>
    /// Blend/termination mode for motion transitions.
    /// </summary>
    public sealed class BlendDto
    {
        /// <summary>
        /// Gets or sets the termination mode: Exact or Blend.
        /// </summary>
        [Description("Termination mode: Exact or Blend.")]
        public TerminationModeEnum Termination { get; set; } = TerminationModeEnum.Exact;

        /// <summary>
        /// Gets or sets the blend radius in metres.
        /// </summary>
        [Description("Blend radius in metres.")]
        public double Radius { get; set; }
    }

    /// <summary>
    /// Common fields shared by all intent inputs.
    /// </summary>
    public abstract class IntentCommonDto
    {
        /// <summary>
        /// Gets or sets the optional intent identifier.
        /// </summary>
        [Description("Optional intent identifier.")]
        public string? IntentId { get; set; }

        /// <summary>
        /// Gets or sets the optional human-readable label.
        /// </summary>
        [Description("Optional human-readable label.")]
        public string? Label { get; set; }

        /// <summary>
        /// Gets or sets the buffer mode: Immediate, Buffered, or Aborting.
        /// </summary>
        [Description("Buffer mode: Immediate, Buffered, or Aborting.")]
        public BufferModeEnum? BufferMode { get; set; }

        /// <summary>
        /// Gets or sets the blocking mode: NonBlocking or Single.
        /// </summary>
        [Description("Blocking mode: NonBlocking or Single.")]
        public BlockingModeEnum? BlockingMode { get; set; }
    }

    /// <summary>
    /// Common fields for all motion intents.
    /// </summary>
    public abstract class MotionIntentDto : IntentCommonDto
    {
        /// <summary>
        /// Gets or sets the tool frame name or NodeId.
        /// </summary>
        [Description("Tool frame selector: frame name/BrowseName or NodeId.")]
        public string? ToolFrame { get; set; }

        /// <summary>
        /// Gets or sets the motion constraints.
        /// </summary>
        [Description("Motion constraints.")]
        public MotionConstraintsDto? Constraints { get; set; }

        /// <summary>
        /// Gets or sets the blend/termination for this motion.
        /// </summary>
        [Description("Blend/termination for this motion.")]
        public BlendDto? Blend { get; set; }

        /// <summary>
        /// Gets or sets the speed fraction as a shorthand for constraints.speedFraction.
        /// </summary>
        [Description("Shorthand for constraints.speedFraction, within [0, 1].")]
        public double SpeedFraction { get; set; }

        /// <summary>
        /// Gets or sets the Cartesian speed as a shorthand for constraints.cartesianSpeed.
        /// </summary>
        [Description("Shorthand for constraints.cartesianSpeed.")]
        public double CartesianSpeed { get; set; }
    }

    /// <summary>
    /// Typed input for a JointMove intent.
    /// </summary>
    public sealed class JointMoveIntentInput : MotionIntentDto
    {
        /// <summary>
        /// Gets or sets the joint target positions in radians.
        /// </summary>
        [Description("Joint target positions in radians. Provide this or targetPose.")]
        public double[]? JointTargets { get; set; }

        /// <summary>
        /// Gets or sets the target pose when joint targets are not supplied.
        /// </summary>
        [Description("Target pose (IK solved by controller). Provide this or jointTargets.")]
        public PoseDto? TargetPose { get; set; }
    }

    /// <summary>
    /// Typed input for a LinearMove intent.
    /// </summary>
    public sealed class LinearMoveIntentInput : MotionIntentDto
    {
        /// <summary>
        /// Gets or sets the target pose.
        /// </summary>
        [Description("Target pose: position in metres, unit quaternion orientation. Required.")]
        public PoseDto? Target { get; set; }
    }

    /// <summary>
    /// Typed input for a CircularMove intent.
    /// </summary>
    public sealed class CircularMoveIntentInput : MotionIntentDto
    {
        /// <summary>
        /// Gets or sets the intermediate via point on the arc.
        /// </summary>
        [Description("Intermediate via point on the arc. Required.")]
        public PoseDto? ViaPoint { get; set; }

        /// <summary>
        /// Gets or sets the end target of the arc.
        /// </summary>
        [Description("End target of the arc. Required.")]
        public PoseDto? Target { get; set; }
    }

    /// <summary>
    /// A single trajectory point.
    /// </summary>
    public sealed class TrajectoryPointDto
    {
        /// <summary>
        /// Gets or sets the time from start in seconds.
        /// </summary>
        [Description("Time from start in seconds.")]
        public double TimeFromStart { get; set; }

        /// <summary>
        /// Gets or sets the joint positions in radians.
        /// </summary>
        [Description("Joint positions in radians.")]
        public double[] Positions { get; set; } = [];

        /// <summary>
        /// Gets or sets optional joint velocities.
        /// </summary>
        [Description("Optional joint velocities.")]
        public double[]? Velocities { get; set; }

        /// <summary>
        /// Gets or sets optional joint accelerations.
        /// </summary>
        [Description("Optional joint accelerations.")]
        public double[]? Accelerations { get; set; }
    }

    /// <summary>
    /// Typed input for a Trajectory intent.
    /// </summary>
    public sealed class TrajectoryIntentInput : MotionIntentDto
    {
        /// <summary>
        /// Gets or sets the trajectory points.
        /// </summary>
        [Description("Trajectory points with timeFromStart and positions.")]
        public TrajectoryPointDto[] Points { get; set; } = [];
    }

    /// <summary>
    /// A single Cartesian path waypoint.
    /// </summary>
    public sealed class CartesianWaypointDto
    {
        /// <summary>
        /// Gets or sets the waypoint pose.
        /// </summary>
        [Description("Waypoint pose. Required.")]
        public PoseDto? Pose { get; set; }

        /// <summary>
        /// Gets or sets optional per-waypoint blend.
        /// </summary>
        [Description("Optional per-waypoint blend.")]
        public BlendDto? Blend { get; set; }
    }

    /// <summary>
    /// Typed input for a CartesianPath intent.
    /// </summary>
    public sealed class CartesianPathIntentInput : MotionIntentDto
    {
        /// <summary>
        /// Gets or sets the waypoints.
        /// </summary>
        [Description("Cartesian waypoints.")]
        public CartesianWaypointDto[] Waypoints { get; set; } = [];
    }

    /// <summary>
    /// Typed input for a Force intent.
    /// </summary>
    public sealed class ForceIntentInput : MotionIntentDto
    {
        /// <summary>
        /// Gets or sets the force direction as a 3-element unit vector.
        /// </summary>
        [Description("Force direction as 3-element unit vector.")]
        public double[] Direction { get; set; } = [];

        /// <summary>
        /// Gets or sets the contact force threshold in newtons.
        /// </summary>
        [Description("Contact force threshold in newtons.")]
        public double ContactForce { get; set; }

        /// <summary>
        /// Gets or sets the optional reference frame name or NodeId.
        /// </summary>
        [Description("Optional frame selector: published FrameId, frame name/BrowseName, or NodeId.")]
        public string? FrameId { get; set; }

        /// <summary>
        /// Gets or sets the maximum contact-search distance in metres.
        /// </summary>
        [Description("Maximum contact-search distance in metres.")]
        public double MaxDistance { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to hold force after contact.
        /// </summary>
        [Description("Whether to hold force after contact.")]
        public bool HoldForce { get; set; }
    }

    /// <summary>
    /// A typed variant value: dataType tag plus JSON payload.
    /// </summary>
    public sealed class TypedValueDto
    {
        /// <summary>
        /// Gets or sets the OPC UA data type name.
        /// </summary>
        [Description("OPC UA data type: Boolean, Int32, UInt32, Double, String, Float, " +
            "Int16, UInt16, Int64, UInt64.")]
        public string? DataType { get; set; }

        /// <summary>
        /// Gets or sets the value as a JSON element.
        /// </summary>
        [Description("The value to write.")]
        public JsonElement Value { get; set; }
    }

    /// <summary>
    /// Common fields for all process intents.
    /// </summary>
    public abstract class ProcessIntentDto : IntentCommonDto
    {
        /// <summary>
        /// Gets or sets the optional process program name or NodeId.
        /// </summary>
        [Description("Optional process program name or NodeId.")]
        public string? ProcessProgram { get; set; }

        /// <summary>
        /// Gets or sets optional key-value attributes as a JSON object.
        /// </summary>
        [Description("Optional typed key-value attributes.")]
        public NamedTypedValueDto[]? Attributes { get; set; }
    }

    /// <summary>
    /// Typed input for an ArcWeld process intent.
    /// </summary>
    public sealed class ArcWeldIntentInput : ProcessIntentDto
    {
        /// <summary>
        /// Gets or sets the welding voltage.
        /// </summary>
        [Description("Welding voltage.")]
        public double Voltage { get; set; }

        /// <summary>
        /// Gets or sets the wire feed speed.
        /// </summary>
        [Description("Wire feed speed.")]
        public double WireFeedSpeed { get; set; }

        /// <summary>
        /// Gets or sets the travel speed.
        /// </summary>
        [Description("Travel speed.")]
        public double TravelSpeed { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether seam tracking is enabled.
        /// </summary>
        [Description("Whether seam tracking is enabled.")]
        public bool SeamTrackingEnabled { get; set; }

        /// <summary>
        /// Gets or sets the weld procedure reference.
        /// </summary>
        [Description("Weld procedure reference.")]
        public string? WeldProcedureRef { get; set; }
    }

    /// <summary>
    /// Typed input for a SpotWeld process intent.
    /// </summary>
    public sealed class SpotWeldIntentInput : ProcessIntentDto
    {
        /// <summary>
        /// Gets or sets the weld schedule number.
        /// </summary>
        [Description("Weld schedule number.")]
        public uint WeldSchedule { get; set; }

        /// <summary>
        /// Gets or sets the gun force in newtons.
        /// </summary>
        [Description("Gun force in newtons.")]
        public double GunForce { get; set; }
    }

    /// <summary>
    /// Typed input for a Dispense process intent.
    /// </summary>
    public sealed class DispenseIntentInput : ProcessIntentDto
    {
        /// <summary>
        /// Gets or sets the flow rate.
        /// </summary>
        [Description("Flow rate.")]
        public double FlowRate { get; set; }

        /// <summary>
        /// Gets or sets the bead width in metres.
        /// </summary>
        [Description("Bead width in metres.")]
        public double BeadWidth { get; set; }

        /// <summary>
        /// Gets or sets the purge cycles count.
        /// </summary>
        [Description("Purge cycles count.")]
        public uint PurgeCycles { get; set; }
    }

    /// <summary>
    /// Typed input for a Fasten process intent.
    /// </summary>
    public sealed class FastenIntentInput : ProcessIntentDto
    {
        /// <summary>
        /// Gets or sets the joining-model node.
        /// </summary>
        [Description("Joining-model NodeId. Robot axes are not fastening joints.")]
        public string? Joint { get; set; }

        /// <summary>
        /// Gets or sets the program number.
        /// </summary>
        [Description("Program number.")]
        public uint ProgramNumber { get; set; }

        /// <summary>
        /// Gets or sets the target torque in newton-metres.
        /// </summary>
        [Description("Target torque in Nm.")]
        public double TargetTorque { get; set; }
    }

    /// <summary>
    /// Typed input for a Palletise process intent.
    /// </summary>
    public sealed class PalletiseIntentInput : ProcessIntentDto
    {
        /// <summary>
        /// Gets or sets the pattern name or NodeId.
        /// </summary>
        [Description("Pattern location name or NodeId.")]
        public string? Pattern { get; set; }

        /// <summary>
        /// Gets or sets the layer index.
        /// </summary>
        [Description("Layer index.")]
        public uint Layer { get; set; }

        /// <summary>
        /// Gets or sets the row index.
        /// </summary>
        [Description("Row index.")]
        public uint Row { get; set; }

        /// <summary>
        /// Gets or sets the column index.
        /// </summary>
        [Description("Column index.")]
        public uint Column { get; set; }
    }

    /// <summary>
    /// Typed input for a SurfaceFinish process intent.
    /// </summary>
    public sealed class SurfaceFinishIntentInput : ProcessIntentDto
    {
        /// <summary>
        /// Gets or sets the contact force in newtons.
        /// </summary>
        [Description("Contact force in newtons.")]
        public double ContactForce { get; set; }

        /// <summary>
        /// Gets or sets the feed rate.
        /// </summary>
        [Description("Feed rate.")]
        public double FeedRate { get; set; }

        /// <summary>
        /// Gets or sets the tool speed.
        /// </summary>
        [Description("Tool speed.")]
        public double ToolSpeed { get; set; }

        /// <summary>
        /// Gets or sets the step-over distance in metres.
        /// </summary>
        [Description("Step-over distance in metres.")]
        public double StepOver { get; set; }
    }

    /// <summary>
    /// Typed input for a Grasp intent.
    /// </summary>
    public sealed class GraspIntentInput : IntentCommonDto
    {
        /// <summary>
        /// Gets or sets the tool name or NodeId.
        /// </summary>
        [Description("Tool name or NodeId to activate.")]
        public string Tool { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the grasp force in newtons.
        /// </summary>
        [Description("Grasp force in newtons.")]
        public double Force { get; set; }
    }

    /// <summary>
    /// Typed input for a Release intent.
    /// </summary>
    public sealed class ReleaseIntentInput : IntentCommonDto
    {
        /// <summary>
        /// Gets or sets the tool name or NodeId to deactivate.
        /// </summary>
        [Description("Tool name or NodeId to deactivate.")]
        public string Tool { get; set; } = string.Empty;
    }

    /// <summary>
    /// Typed input for a Pick intent.
    /// </summary>
    public sealed class PickIntentInput : IntentCommonDto
    {
        /// <summary>
        /// Gets or sets the source location name or NodeId.
        /// </summary>
        [Description("Source location name or NodeId.")]
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the tool name or NodeId.
        /// </summary>
        [Description("Tool name or NodeId.")]
        public string Tool { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the object class label of the workpiece being taken.
        /// </summary>
        [Description("Class label of the workpiece being taken.")]
        public string? ObjectClass { get; set; }
    }

    /// <summary>
    /// Typed input for a Place intent.
    /// </summary>
    public sealed class PlaceIntentInput : IntentCommonDto
    {
        /// <summary>
        /// Gets or sets the destination location name or NodeId.
        /// </summary>
        [Description("Destination location name or NodeId.")]
        public string Destination { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the tool name or NodeId.
        /// </summary>
        [Description("Tool name or NodeId.")]
        public string Tool { get; set; } = string.Empty;
    }

    /// <summary>
    /// Typed input for a ToolChange intent.
    /// </summary>
    public sealed class ToolChangeIntentInput : IntentCommonDto
    {
        /// <summary>
        /// Gets or sets the tool name or NodeId to fit, or null to release.
        /// </summary>
        [Description("Tool name or NodeId to fit, or null to release.")]
        public string? Tool { get; set; }

        /// <summary>
        /// Gets or sets the dock station name or NodeId.
        /// </summary>
        [Description("Dock station name or NodeId.")]
        public string? DockStation { get; set; }
    }

    /// <summary>
    /// Typed input for a SetOutput intent.
    /// </summary>
    public sealed class SetOutputIntentInput : IntentCommonDto
    {
        /// <summary>
        /// Gets or sets the output signal name or NodeId.
        /// </summary>
        [Description("Output signal name or NodeId.")]
        public string Output { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the value to write with data type guidance.
        /// </summary>
        [Description("Value to write. Required, with an explicit dataType.")]
        public TypedValueDto? Value { get; set; }
    }

    /// <summary>
    /// Typed input for a CallProgram intent.
    /// </summary>
    public sealed class CallProgramIntentInput : IntentCommonDto
    {
        /// <summary>
        /// Gets or sets the program name or NodeId.
        /// </summary>
        [Description("Program name or NodeId.")]
        public string Program { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets optional arguments as a JSON object of name/value pairs.
        /// </summary>
        [Description("Optional typed arguments.")]
        public NamedTypedValueDto[]? Arguments { get; set; }
    }

    /// <summary>
    /// Typed input for a Wait intent.
    /// </summary>
    public sealed class WaitIntentInput : IntentCommonDto
    {
        /// <summary>
        /// Gets or sets the wait duration in seconds.
        /// </summary>
        [Description("Wait duration in seconds.")]
        public double Duration { get; set; }

        /// <summary>
        /// Gets or sets the signal name or NodeId to wait for.
        /// </summary>
        [Description("Signal name or NodeId to wait for.")]
        public string? Signal { get; set; }
    }

    /// <summary>
    /// Typed input for a mission step, carrying a discriminated intent.
    /// </summary>
    public sealed class MissionStepInput
    {
        /// <summary>
        /// Gets or sets the step identifier.
        /// </summary>
        [Description("Step identifier.")]
        public string StepId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the optional sequence identifier.
        /// </summary>
        [Description("Optional sequence identifier.")]
        public uint? SequenceId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the step is released for execution.
        /// </summary>
        [Description("Whether the step is released for execution.")]
        public bool Released { get; set; }

        /// <summary>
        /// Gets or sets the error policy: Abort or Skip.
        /// </summary>
        [Description("Error policy: Abort or Skip.")]
        public ErrorPolicyEnum? ErrorPolicy { get; set; }

        /// <summary>
        /// Gets or sets the fallback step identifier on error.
        /// </summary>
        [Description("Fallback step identifier on error.")]
        public string? FallbackStepId { get; set; }

        /// <summary>
        /// Gets or sets the intent for this step.
        /// </summary>
        [Description("The intent for this step. Required.")]
        public MissionIntentInput? Intent { get; set; }
    }

    /// <summary>
    /// A flat discriminated union for mission step intents.
    /// </summary>
    public sealed class MissionIntentInput
    {
        /// <summary>
        /// Gets or sets the intent kind discriminator.
        /// </summary>
        [Description("Intent kind discriminator.")]
        public IntentKind Kind { get; set; }

        /// <summary>
        /// Gets or sets the optional intent identifier.
        /// </summary>
        [Description("Optional intent identifier.")]
        public string? IntentId { get; set; }

        /// <summary>
        /// Gets or sets the optional human-readable label.
        /// </summary>
        [Description("Optional human-readable label.")]
        public string? Label { get; set; }

        /// <summary>
        /// Gets or sets the buffer mode.
        /// </summary>
        [Description("Buffer mode: Immediate, Buffered, or Aborting.")]
        public BufferModeEnum? BufferMode { get; set; }

        /// <summary>
        /// Gets or sets the blocking mode.
        /// </summary>
        [Description("Blocking mode: NonBlocking or Single.")]
        public BlockingModeEnum? BlockingMode { get; set; }

        /// <summary>
        /// Gets or sets the motion tool frame.
        /// </summary>
        [Description("Motion tool frame name or NodeId.")]
        public string? ToolFrame { get; set; }

        /// <summary>
        /// Gets or sets the motion constraints.
        /// </summary>
        [Description("Motion constraints.")]
        public MotionConstraintsDto? Constraints { get; set; }

        /// <summary>
        /// Gets or sets the blend parameters.
        /// </summary>
        [Description("Motion blend parameters.")]
        public BlendDto? Blend { get; set; }

        /// <summary>
        /// Gets or sets the speed fraction shorthand.
        /// </summary>
        [Description("Optional speed fraction within [0, 1].")]
        public double? SpeedFraction { get; set; }

        /// <summary>
        /// Gets or sets the Cartesian speed shorthand.
        /// </summary>
        [Description("Optional Cartesian speed in m/s.")]
        public double? CartesianSpeed { get; set; }

        /// <summary>
        /// Gets or sets the process program.
        /// </summary>
        [Description("Optional process program name or NodeId.")]
        public string? ProcessProgram { get; set; }

        /// <summary>
        /// Gets or sets the process attributes.
        /// </summary>
        [Description("Optional typed process attributes.")]
        public NamedTypedValueDto[]? Attributes { get; set; }

        /// <summary>
        /// Gets or sets joint targets for JointMove.
        /// </summary>
        [Description("JointMove targets in radians.")]
        public double[]? JointTargets { get; set; }

        /// <summary>
        /// Gets or sets the target pose for JointMove, LinearMove, or CircularMove.
        /// </summary>
        [Description("Target pose for the selected motion kind.")]
        public PoseDto? Target { get; set; }

        /// <summary>
        /// Gets or sets the intermediate pose for CircularMove.
        /// </summary>
        [Description("CircularMove intermediate pose.")]
        public PoseDto? ViaPoint { get; set; }

        /// <summary>
        /// Gets or sets trajectory points.
        /// </summary>
        [Description("Trajectory points.")]
        public TrajectoryPointDto[]? Points { get; set; }

        /// <summary>
        /// Gets or sets Cartesian path waypoints.
        /// </summary>
        [Description("Cartesian path waypoints.")]
        public CartesianWaypointDto[]? Waypoints { get; set; }

        /// <summary>
        /// Gets or sets the Force direction.
        /// </summary>
        [Description("Force direction as a three-element unit vector.")]
        public double[]? Direction { get; set; }

        /// <summary>
        /// Gets or sets the contact force.
        /// </summary>
        [Description("Contact force in newtons.")]
        public double? ContactForce { get; set; }

        /// <summary>
        /// Gets or sets the Force reference frame.
        /// </summary>
        [Description("Force reference frame name or NodeId.")]
        public string? FrameId { get; set; }

        /// <summary>
        /// Gets or sets the maximum Force search distance.
        /// </summary>
        [Description("Maximum Force search distance in metres.")]
        public double? MaxDistance { get; set; }

        /// <summary>
        /// Gets or sets whether Force holds after contact.
        /// </summary>
        [Description("Whether Force holds after contact.")]
        public bool? HoldForce { get; set; }

        /// <summary>
        /// Gets or sets the ArcWeld voltage.
        /// </summary>
        [Description("ArcWeld voltage.")]
        public double? Voltage { get; set; }

        /// <summary>
        /// Gets or sets the ArcWeld wire feed speed.
        /// </summary>
        [Description("ArcWeld wire feed speed.")]
        public double? WireFeedSpeed { get; set; }

        /// <summary>
        /// Gets or sets the ArcWeld travel speed.
        /// </summary>
        [Description("ArcWeld travel speed.")]
        public double? TravelSpeed { get; set; }

        /// <summary>
        /// Gets or sets whether ArcWeld seam tracking is enabled.
        /// </summary>
        [Description("Whether ArcWeld seam tracking is enabled.")]
        public bool? SeamTrackingEnabled { get; set; }

        /// <summary>
        /// Gets or sets the ArcWeld procedure reference.
        /// </summary>
        [Description("ArcWeld procedure reference.")]
        public string? WeldProcedureRef { get; set; }

        /// <summary>
        /// Gets or sets the SpotWeld schedule.
        /// </summary>
        [Description("SpotWeld schedule.")]
        public uint? WeldSchedule { get; set; }

        /// <summary>
        /// Gets or sets the SpotWeld gun force.
        /// </summary>
        [Description("SpotWeld gun force in newtons.")]
        public double? GunForce { get; set; }

        /// <summary>
        /// Gets or sets the Dispense flow rate.
        /// </summary>
        [Description("Dispense flow rate.")]
        public double? FlowRate { get; set; }

        /// <summary>
        /// Gets or sets the Dispense bead width.
        /// </summary>
        [Description("Dispense bead width in metres.")]
        public double? BeadWidth { get; set; }

        /// <summary>
        /// Gets or sets the Dispense purge cycles.
        /// </summary>
        [Description("Dispense purge cycles.")]
        public uint? PurgeCycles { get; set; }

        /// <summary>
        /// Gets or sets the Fasten joining node.
        /// </summary>
        [Description("Fasten joining-model NodeId.")]
        public string? Joint { get; set; }

        /// <summary>
        /// Gets or sets the Fasten program number.
        /// </summary>
        [Description("Fasten program number.")]
        public uint? ProgramNumber { get; set; }

        /// <summary>
        /// Gets or sets the Fasten target torque.
        /// </summary>
        [Description("Fasten target torque in Nm.")]
        public double? TargetTorque { get; set; }

        /// <summary>
        /// Gets or sets the Palletise pattern.
        /// </summary>
        [Description("Palletise pattern name or NodeId.")]
        public string? Pattern { get; set; }

        /// <summary>
        /// Gets or sets the Palletise layer.
        /// </summary>
        [Description("Palletise layer index.")]
        public uint? Layer { get; set; }

        /// <summary>
        /// Gets or sets the Palletise row.
        /// </summary>
        [Description("Palletise row index.")]
        public uint? Row { get; set; }

        /// <summary>
        /// Gets or sets the Palletise column.
        /// </summary>
        [Description("Palletise column index.")]
        public uint? Column { get; set; }

        /// <summary>
        /// Gets or sets the SurfaceFinish feed rate.
        /// </summary>
        [Description("SurfaceFinish feed rate.")]
        public double? FeedRate { get; set; }

        /// <summary>
        /// Gets or sets the SurfaceFinish tool speed.
        /// </summary>
        [Description("SurfaceFinish tool speed.")]
        public double? ToolSpeed { get; set; }

        /// <summary>
        /// Gets or sets the SurfaceFinish step-over.
        /// </summary>
        [Description("SurfaceFinish step-over in metres.")]
        public double? StepOver { get; set; }

        /// <summary>
        /// Gets or sets the tool used by Grasp, Release, Pick, Place, or ToolChange.
        /// </summary>
        [Description("Tool name or NodeId.")]
        public string? Tool { get; set; }

        /// <summary>
        /// Gets or sets the Grasp force.
        /// </summary>
        [Description("Grasp force in newtons.")]
        public double? Force { get; set; }

        /// <summary>
        /// Gets or sets the Pick source.
        /// </summary>
        [Description("Pick source location name or NodeId.")]
        public string? Source { get; set; }

        /// <summary>
        /// Gets or sets the Pick object class.
        /// </summary>
        [Description("Pick object class label.")]
        public string? ObjectClass { get; set; }

        /// <summary>
        /// Gets or sets the Place destination.
        /// </summary>
        [Description("Place destination location name or NodeId.")]
        public string? Destination { get; set; }

        /// <summary>
        /// Gets or sets the ToolChange dock station.
        /// </summary>
        [Description("ToolChange dock station name or NodeId.")]
        public string? DockStation { get; set; }

        /// <summary>
        /// Gets or sets the SetOutput signal.
        /// </summary>
        [Description("SetOutput signal name or NodeId.")]
        public string? Output { get; set; }

        /// <summary>
        /// Gets or sets the SetOutput value.
        /// </summary>
        [Description("SetOutput typed value.")]
        public TypedValueDto? Value { get; set; }

        /// <summary>
        /// Gets or sets the program to call.
        /// </summary>
        [Description("Program name or NodeId.")]
        public string? Program { get; set; }

        /// <summary>
        /// Gets or sets the program arguments.
        /// </summary>
        [Description("Optional typed program arguments.")]
        public NamedTypedValueDto[]? Arguments { get; set; }

        /// <summary>
        /// Gets or sets the Wait duration.
        /// </summary>
        [Description("Wait duration in seconds.")]
        public double? Duration { get; set; }

        /// <summary>
        /// Gets or sets the Wait signal.
        /// </summary>
        [Description("Wait signal name or NodeId.")]
        public string? Signal { get; set; }
    }

    /// <summary>
    /// A mission transition between steps.
    /// </summary>
    public sealed class MissionTransitionInput
    {
        /// <summary>
        /// Gets or sets the source step identifier.
        /// </summary>
        [Description("Source step identifier.")]
        public string FromStepId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target step identifier.
        /// </summary>
        [Description("Target step identifier.")]
        public string ToStepId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the divergence kind: Alternative or Parallel.
        /// </summary>
        [Description("Divergence kind: Alternative or Parallel.")]
        public DivergenceKindEnum? DivergenceKind { get; set; }
    }

    /// <summary>
    /// Query parameters for listing operations with paging.
    /// </summary>
    public sealed class OperationListQuery
    {
        /// <summary>
        /// Gets or sets an optional intent identifier to filter by.
        /// </summary>
        [Description("Optional intent identifier to filter by.")]
        public string? IntentId { get; set; }

        /// <summary>
        /// Gets or sets an optional mission identifier to filter by.
        /// </summary>
        [Description("Optional mission identifier to filter by.")]
        public string? MissionId { get; set; }

        /// <summary>
        /// Gets or sets an optional execution state filter.
        /// </summary>
        [Description("Execution state filter.")]
        public ExecutionStateEnum? ExecutionState { get; set; }

        /// <summary>
        /// Gets or sets the work selector: All, Active, or Terminal.
        /// </summary>
        [Description("Work selector: All (default), Active, or Terminal.")]
        public WorkSelector Work { get; set; } = WorkSelector.All;

        /// <summary>
        /// Gets or sets the detail level: Summary or Full.
        /// </summary>
        [Description("Detail level: Summary (default) or Full.")]
        public DetailLevel Detail { get; set; } = DetailLevel.Summary;

        /// <summary>
        /// Gets or sets the maximum page size (default 20, max 100).
        /// </summary>
        [Description("Maximum page size (default 20, max 100).")]
        public int? PageSize { get; set; }

        /// <summary>
        /// Gets or sets the opaque cursor for the next page.
        /// </summary>
        [Description("Opaque cursor for continuation.")]
        public string? Cursor { get; set; }
    }

    /// <summary>
    /// A summary operation snapshot, omitting pose and full output.
    /// </summary>
    public sealed class OperationSummary
    {
        /// <summary>
        /// Gets or sets the operation NodeId string.
        /// </summary>
        [Description("Operation NodeId.")]
        public string Operation { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the intent identifier.
        /// </summary>
        [Description("Intent identifier.")]
        public string IntentId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the execution state name.
        /// </summary>
        [Description("Execution state.")]
        public ExecutionStateEnum ExecutionState { get; set; }

        /// <summary>
        /// Gets or sets the progress fraction, or -1 when unknown.
        /// </summary>
        [Description("Progress fraction, or -1 when unknown.")]
        public double Progress { get; set; } = -1;

        /// <summary>
        /// Gets or sets the queue position.
        /// </summary>
        [Description("Queue position (1=next, 0=not queued).")]
        public uint QueuePosition { get; set; }

        /// <summary>
        /// Gets or sets the failure reason.
        /// </summary>
        [Description("Failure classification when in a terminal state.")]
        public IntentFailureEnum? Failure { get; set; }

        /// <summary>
        /// Gets or sets a human-readable message.
        /// </summary>
        [Description("Human-readable message.")]
        public string? Message { get; set; }

        /// <summary>
        /// Gets or sets the mission identifier.
        /// </summary>
        [Description("Mission identifier, if applicable.")]
        public string? MissionId { get; set; }
    }

    /// <summary>
    /// Paged result for operation listing.
    /// </summary>
    public sealed class OperationListResult
    {
        /// <summary>
        /// Gets or sets the total matching operations.
        /// </summary>
        [Description("Total number of matching operations.")]
        public int Total { get; set; }

        /// <summary>
        /// Gets or sets the returned count.
        /// </summary>
        [Description("Number of items on this page.")]
        public int Returned { get; set; }

        /// <summary>
        /// Gets or sets the next cursor, or null if no more pages.
        /// </summary>
        [Description("Opaque cursor for the next page, or null.")]
        public string? NextCursor { get; set; }

        /// <summary>
        /// Gets or sets summaries when detail is 'summary'.
        /// </summary>
        [Description("Operation summaries.")]
        public OperationSummary[]? Summaries { get; set; }

        /// <summary>
        /// Gets or sets full snapshots when detail is 'full'.
        /// </summary>
        [Description("Full operation snapshots.")]
        public IntentOperationSnapshot[]? Operations { get; set; }
    }

    /// <summary>
    /// Query parameters for listing missions with paging.
    /// </summary>
    public sealed class MissionListQuery
    {
        /// <summary>
        /// Gets or sets an optional mission identifier to filter by.
        /// </summary>
        [Description("Optional mission identifier to filter by.")]
        public string? MissionId { get; set; }

        /// <summary>
        /// Gets or sets an optional execution state filter.
        /// </summary>
        [Description("Execution state filter.")]
        public ExecutionStateEnum? ExecutionState { get; set; }

        /// <summary>
        /// Gets or sets the work selector: All, Active, or Terminal.
        /// </summary>
        [Description("Work selector: All (default), Active, or Terminal.")]
        public WorkSelector Work { get; set; } = WorkSelector.All;

        /// <summary>
        /// Gets or sets the detail level: Summary or Full.
        /// </summary>
        [Description("Detail level: Summary (default) or Full.")]
        public DetailLevel Detail { get; set; } = DetailLevel.Summary;

        /// <summary>
        /// Gets or sets the maximum page size (default 20, max 100).
        /// </summary>
        [Description("Maximum page size (default 20, max 100).")]
        public int? PageSize { get; set; }

        /// <summary>
        /// Gets or sets the opaque cursor for the next page.
        /// </summary>
        [Description("Opaque cursor for continuation.")]
        public string? Cursor { get; set; }
    }

    /// <summary>
    /// A concise mission summary.
    /// </summary>
    public sealed class MissionSummary
    {
        /// <summary>
        /// Gets or sets the mission NodeId string.
        /// </summary>
        [Description("Mission NodeId.")]
        public string MissionNode { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the mission identifier.
        /// </summary>
        [Description("Mission identifier.")]
        public string MissionId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the mission update identifier.
        /// </summary>
        [Description("Mission update identifier.")]
        public uint MissionUpdateId { get; set; }

        /// <summary>
        /// Gets or sets the execution state name.
        /// </summary>
        [Description("Execution state.")]
        public ExecutionStateEnum ExecutionState { get; set; }

        /// <summary>
        /// Gets or sets the currently executing step identifier.
        /// </summary>
        [Description("Currently executing step.")]
        public string? CurrentStepId { get; set; }

        /// <summary>
        /// Gets or sets the failure reason.
        /// </summary>
        [Description("Failure classification when in a terminal state.")]
        public IntentFailureEnum? Failure { get; set; }

        /// <summary>
        /// Gets or sets a human-readable message.
        /// </summary>
        [Description("Human-readable message.")]
        public string? Message { get; set; }

        /// <summary>
        /// Gets or sets the number of released steps.
        /// </summary>
        [Description("Number of released steps.")]
        public uint ReleasedStepCount { get; set; }

        /// <summary>
        /// Gets or sets the per-step operation summaries.
        /// </summary>
        [Description("Per-step operation summaries: stepId, intentId, operation NodeId, state.")]
        public MissionStepOperationSummary[] Steps { get; set; } = [];
    }

    /// <summary>
    /// A concise per-step summary within a mission.
    /// </summary>
    public sealed class MissionStepOperationSummary
    {
        /// <summary>
        /// Gets or sets the step identifier.
        /// </summary>
        [Description("Step identifier.")]
        public string StepId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the intent identifier.
        /// </summary>
        [Description("Intent identifier.")]
        public string IntentId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the operation NodeId string, or null if not yet executing.
        /// </summary>
        [Description("Operation NodeId, or null if not yet executing.")]
        public string? Operation { get; set; }

        /// <summary>
        /// Gets or sets the step execution state.
        /// </summary>
        [Description("Step execution state.")]
        public ExecutionStateEnum State { get; set; }
    }

    /// <summary>
    /// Paged result for mission listing.
    /// </summary>
    public sealed class MissionListResult
    {
        /// <summary>
        /// Gets or sets the total matching missions.
        /// </summary>
        [Description("Total number of matching missions.")]
        public int Total { get; set; }

        /// <summary>
        /// Gets or sets the returned count.
        /// </summary>
        [Description("Number of items on this page.")]
        public int Returned { get; set; }

        /// <summary>
        /// Gets or sets the next cursor, or null if no more pages.
        /// </summary>
        [Description("Opaque cursor for the next page, or null.")]
        public string? NextCursor { get; set; }

        /// <summary>
        /// Gets or sets summaries when detail is 'summary'.
        /// </summary>
        [Description("Mission summaries.")]
        public MissionSummary[]? Summaries { get; set; }

        /// <summary>
        /// Gets or sets full snapshots when detail is 'full'.
        /// </summary>
        [Description("Full mission snapshots.")]
        public MissionSnapshot[]? Missions { get; set; }
    }
}
