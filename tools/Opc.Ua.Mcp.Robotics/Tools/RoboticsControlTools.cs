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
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for direct Robot Intent control.
    /// </summary>
    [McpServerToolType]
    public sealed class RoboticsControlTools
    {
        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_request_control")]
        [Description("Requests command authority explicitly. If the server refuses because another owner holds " +
            "authority, the current owner is returned; this tool never synthesizes authority as a side effect.")]
        public static async Task<CommandAuthorityOutcome> RequestControlAsync(
            RoboticsIntentManager manager,
            [Description("Controller NodeId.")] string controllerId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return await manager.OpenController(controllerId, sessionName).Transport.RequestControlAsync(ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_release_control")]
        [Description("Releases command authority held by this session. It does not cancel work and does not infer " +
            "ownership; server-side errors are returned as OPC UA call errors.")]
        public static async Task ReleaseControlAsync(
            RoboticsIntentManager manager,
            [Description("Controller NodeId.")] string controllerId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            await manager.OpenController(controllerId, sessionName).ReleaseControlAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_cancel_intent")]
        [Description("Requests cancellation of one intent. A server refusal such as NotPermittedInMode or " +
            "ControlNotOwned is returned by the client API; this tool never retries or turns it into a resubmit.")]
        public static async Task<IntentCommandOutcome> CancelIntentAsync(
            RoboticsIntentManager manager,
            [Description("Controller NodeId.")] string controllerId,
            [Description("IntentId to cancel.")] string intentId,
            [Description("Stop mode requested from the server.")] StopModeEnum stopMode = StopModeEnum.QuickStop,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return await manager.OpenController(controllerId, sessionName).CancelIntentAsync(intentId, stopMode, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_cancel_all")]
        [Description("Requests cancellation of all outstanding work. The returned count is the server's answer; " +
            "the MCP layer does not maintain its own outstanding-work list or retry refusals.")]
        public static async Task<uint> CancelAllAsync(
            RoboticsIntentManager manager,
            [Description("Controller NodeId.")] string controllerId,
            [Description("Stop mode requested from the server.")] StopModeEnum stopMode = StopModeEnum.QuickStop,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return await manager.OpenController(controllerId, sessionName).CancelAllAsync(stopMode, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_pause")]
        [Description("Requests controller pause: active Robot Intent work stops while queued " +
            "operations remain queued on the server. Use this to hold execution without releasing or resubmitting " +
            "the queue; use robotics_resume to continue a paused controller. Mode, safety, or ownership refusals " +
            "are returned with their IntentFailureEnum and are never retried. Command authority is never acquired " +
            "as a side effect. Returns IntentCommandOutcome.")]
        public static async Task<IntentCommandOutcome> PauseAsync(
            RoboticsIntentManager manager,
            [Description("Controller NodeId.")] string controllerId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return await manager.OpenController(controllerId, sessionName).PauseAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_resume")]
        [Description("Requests that a paused controller continue its active operation and queued " +
            "work. Use this only after a pause or paused state; it does not submit new work, rebuild the queue, or " +
            "release authority. Mode, safety, or ownership refusals are returned with their IntentFailureEnum and " +
            "are never retried. Command authority is never acquired as a side effect. Returns IntentCommandOutcome.")]
        public static async Task<IntentCommandOutcome> ResumeAsync(
            RoboticsIntentManager manager,
            [Description("Controller NodeId.")] string controllerId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return await manager.OpenController(controllerId, sessionName).ResumeAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_retry")]
        [Description("Requests Retry for an existing intent. The admission result preserves the exact server " +
            "IntentFailureEnum and message when refused; this tool performs no client-side retry loop.")]
        public static async Task<IntentSubmissionResult> RetryAsync(
            RoboticsIntentManager manager,
            [Description("Controller NodeId.")] string controllerId,
            [Description("IntentId to retry.")] string intentId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return await manager.OpenController(controllerId, sessionName).RetryAsync(intentId, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_joint_move")]
        [Description("Submits a JointMove motion intent: point-to-point joint motion to jointTargets in radians " +
            "or to a targetPose solved by the controller. Use this for robot-axis positioning, not Cartesian path " +
            "following. Refusals such as NotPermittedInMode, SafetyLimitExceeded, ControlNotOwned, " +
            "CapabilityNotSupported, ParameterInvalid, or QueueFull are reported with their IntentFailureEnum and " +
            "are never retried. Command authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitJointMoveAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description("Required JSON object for JointMove: either jointTargets as an array of joint positions " +
                "in radians, or targetPose with position metres and quaternion orientation [x,y,z,w]. Optional " +
                "common fields include intentId, label, bufferMode, blockingMode, toolFrame, constraints, and blend. " +
                "Malformed JSON, wrong array lengths, or NodeIds outside the controller fail before or during " +
                "submission as ParameterInvalid or an argument error.")]
            string intentJson,
            [Description("Axis count used only for local JointMove builder validation. Use the controller's declared " +
                "AxisCount; the default 0 disables local count validation and leaves validation to the server.")]
            uint axisCount = 0,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "jointMove", intentJson, axisCount, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_linear_move")]
        [Description("Submits a LinearMove motion intent: a straight Cartesian segment to target. " +
            "Use this for a single linear tool-centre-point move; use robotics_submit_circular_move for an arc via " +
            "a viaPoint and robotics_submit_cartesian_path for multiple taught waypoints. Server refusals are " +
            "returned with IntentFailureEnum and are never retried. Command authority is never acquired as a side " +
            "effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitLinearMoveAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description("Required JSON object for LinearMove with target pose: position is [x,y,z] in metres and " +
                "orientation is quaternion [x,y,z,w], plus optional speedFraction, constraints, blend, toolFrame, " +
                "intentId, label, bufferMode, and blockingMode. Missing target, malformed pose arrays, or invalid " +
                "NodeIds fail before or during submission as ParameterInvalid or an argument error.")]
            string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "linearMove", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_circular_move")]
        [Description("Submits a CircularMove motion intent: a Cartesian arc through viaPoint to " +
            "target. Use this when the intermediate point defines circular geometry; use robotics_submit_linear_move " +
            "for a straight segment and robotics_submit_cartesian_path for a multi-waypoint path. Server refusals " +
            "are returned with IntentFailureEnum and never retried. Command authority is never acquired as a side " +
            "effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitCircularMoveAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description("Required JSON object for CircularMove with viaPoint and target poses. Each pose uses " +
                "position metres and quaternion orientation [x,y,z,w]; optional common motion fields are " +
                "constraints, blend, toolFrame, intentId, label, bufferMode, and blockingMode. Missing poses or " +
                "malformed arrays fail before or during submission as ParameterInvalid or an argument error.")]
            string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "circularMove", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_trajectory")]
        [Description("Submits a Trajectory motion intent: a complete time-parameterised joint path made of points " +
            "with timeFromStart, positions, and optional velocities or accelerations. Use this when the client has " +
            "planned the whole timed path; use robotics_submit_cartesian_path for controller-planned Cartesian " +
            "waypoints. CapabilityNotSupported and safety, mode, authority, or parameter refusals are returned with " +
            "their IntentFailureEnum and are never retried. Command authority is never acquired as a side effect. " +
            "Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitTrajectoryAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description("Required JSON object for Trajectory with points array. Each point has timeFromStart in " +
                "seconds, positions as joint values in radians, and optional velocities and accelerations arrays. " +
                "Optional common fields include intentId, label, bufferMode, blockingMode, constraints, blend, and " +
                "toolFrame. Missing points or inconsistent arrays fail before or during submission as " +
                "ParameterInvalid or an argument error.")]
            string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "trajectory", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_cartesian_path")]
        [Description("Submits a CartesianPath motion intent: taught Cartesian waypoints with optional " +
            "per-waypoint blending, planned by the controller. Use this for multi-waypoint Cartesian paths; use " +
            "robotics_submit_trajectory for a timed joint trajectory supplied as points. Server refusals remain " +
            "results with the specific IntentFailureEnum and are never retried. Command authority is never acquired " +
            "as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitCartesianPathAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description("Required JSON object for CartesianPath with waypoints array. Each waypoint has pose with " +
                "position metres and quaternion orientation [x,y,z,w], plus optional blend; the intent also accepts " +
                "constraints, toolFrame, intentId, label, bufferMode, and blockingMode. Missing waypoints or invalid " +
                "poses fail before or during submission as ParameterInvalid or an argument error.")]
            string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "cartesianPath", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_force")]
        [Description("Submits a Force motion intent: move along direction until contactForce is reached, optionally " +
            "limited by maxDistance and holdForce. Use this for compliant contact search or force regulation, not " +
            "for surface-finishing process parameters. SafetyLimitExceeded and other refusals are returned with " +
            "IntentFailureEnum and are never retried. Command authority is never acquired as a side effect. Returns " +
            "IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitForceAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description("Required JSON object for Force with direction array, contactForce in newtons, and optional " +
                "frameId, maxDistance in metres, holdForce, constraints, blend, toolFrame, intentId, label, " +
                "bufferMode, and blockingMode. Wrong units, malformed arrays, or frame NodeIds outside the " +
                "controller are refused as ParameterInvalid or fail as argument errors.")]
            string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "force", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_arc_weld")]
        [Description("Submits an ArcWeld process intent for continuous welding along the motion path, using optional " +
            "processProgram plus voltage, wireFeedSpeed, travelSpeed, seamTrackingEnabled, and weldProcedureRef. " +
            "Use this for arc-welding process execution; use robotics_submit_spot_weld for discrete weld spots. " +
            "Server refusals are returned with IntentFailureEnum and are never retried. Command authority is never " +
            "acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitArcWeldAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description(ArcWeldIntentJsonDescription)] string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "arcWeld", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_spot_weld")]
        [Description("Submits a SpotWeld process intent for discrete resistance weld points, with processProgram " +
            "plus weldSchedule and gunForce. Use this for spot-welding cycles; use robotics_submit_arc_weld for a " +
            "continuous weld seam. Server refusals are returned with IntentFailureEnum and are never retried. " +
            "Command authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitSpotWeldAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description(SpotWeldIntentJsonDescription)] string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "spotWeld", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_dispense")]
        [Description("Submits a Dispense process intent for applying material along a " +
            "path, using optional processProgram plus flowRate, beadWidth, and purgeCycles. Use this for material " +
            "deposition; use robotics_submit_fasten for tightening, robotics_submit_palletise for pattern placement, " +
            "and robotics_submit_surface_finish for sanding or polishing contact processing. Server refusals are " +
            "returned with IntentFailureEnum and are never retried. Command authority is never acquired as a side " +
            "effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitDispenseAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description(DispenseIntentJsonDescription)] string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "dispense", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_fasten")]
        [Description("Submits a Fasten process intent for tightening or joining at a fastener/joint, using optional " +
            "processProgram plus joint NodeId, programNumber, and targetTorque. Use this for fastening operations; " +
            "use robotics_submit_dispense for material flow and robotics_submit_surface_finish for abrasive or " +
            "finishing contact. Server refusals are returned with IntentFailureEnum and are never retried. Command " +
            "authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitFastenAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description(FastenIntentJsonDescription)] string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "fasten", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_palletise")]
        [Description("Submits a Palletise process intent that places workpieces using a controller-defined pattern " +
            "LocationType, with layer, row, and column indexes selecting the cell. Use this for pallet pattern " +
            "placement; use robotics_submit_place for a single explicit destination and robotics_submit_dispense for " +
            "material deposition. Server refusals are returned with IntentFailureEnum and are never retried. Command " +
            "authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitPalletiseAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description(PalletiseIntentJsonDescription)] string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "palletise", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_surface_finish")]
        [Description("Submits a SurfaceFinish process intent for sanding, polishing, deburring, or finishing, " +
            "using optional processProgram plus contactForce, feedRate, toolSpeed, and stepOver. Use this when the " +
            "process removes or finishes material; use robotics_submit_dispense to add material and " +
            "robotics_submit_force for force-controlled motion without process parameters. Server refusals are " +
            "returned with IntentFailureEnum and are never retried. Command authority is never acquired as a side " +
            "effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitSurfaceFinishAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description(SurfaceFinishIntentJsonDescription)] string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "surfaceFinish", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_grasp")]
        [Description("Submits a Grasp intent to close or activate a tool on an object, with tool NodeId and " +
            "force. Use this to acquire a workpiece using an end effector; use robotics_submit_release to open or " +
            "deactivate the tool, and robotics_submit_pick when source motion plus grasp should be one pick intent. " +
            "Control, safety, mode, capability, or parameter refusals are returned with IntentFailureEnum and are " +
            "never retried. Command authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitGraspAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description(GraspIntentJsonDescription)] string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "grasp", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_release")]
        [Description("Submits a Release intent to open or deactivate a gripper/tool by tool NodeId. Use this to " +
            "let go of a held object; use robotics_submit_grasp to acquire it, robotics_submit_place when " +
            "motion plus release should be one intent, and robotics_submit_tool_change to fit or remove a tool at a " +
            "dock. Server refusals are returned with IntentFailureEnum and are never retried. Command authority is " +
            "never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitReleaseAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description(ReleaseIntentJsonDescription)] string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "release", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_pick")]
        [Description("Submits a Pick intent for inbound material handling: approach the source LocationType " +
            "and take possession with the selected tool. Choose it when the robot must acquire a workpiece from a " +
            "source. Do not call it to deposit, unload, or release at a destination; that is robotics_submit_place. " +
            "Refusals such as ParameterInvalid, CapabilityNotSupported, SafetyLimitExceeded, ControlNotOwned, or " +
            "QueueFull are reported with IntentFailureEnum and never retried. Command authority is never acquired as " +
            "a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitPickAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description(PickIntentJsonDescription)] string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "pick", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_place")]
        [Description("Submits a Place intent for outbound material handling: carry the held workpiece to the " +
            "destination LocationType and let it go with the selected tool. Choose it when the robot must deposit or " +
            "unload something held. Do not call it to collect from a source or close the gripper; that is " +
            "robotics_submit_pick or robotics_submit_grasp. Refusals are surfaced with their IntentFailureEnum, " +
            "without retrying or silently taking command authority. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitPlaceAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description(PlaceIntentJsonDescription)] string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "place", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_tool_change")]
        [Description("Submits a ToolChange intent to fit a docked tool or release the current tool when tool is " +
            "null. Use this for changing the robot end effector; use robotics_submit_release only to open/deactivate " +
            "a gripper, and robotics_submit_call_program only to run a server program. Server refusals are returned " +
            "with IntentFailureEnum and are never retried. Command authority is never acquired as a side effect. " +
            "Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitToolChangeAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description(ToolChangeIntentJsonDescription)] string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "toolChange", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_set_output")]
        [Description("Submits a SetOutput intent that writes a controller OutputSignalType to a supplied value. " +
            "Use it for typed outputs, not robot motion or program execution. The server " +
            "validates the output NodeId and value DataType; refusals are returned with IntentFailureEnum and are " +
            "never retried. Command authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitSetOutputAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description(SetOutputIntentJsonDescription)] string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "setOutput", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_call_program")]
        [Description("Submits a CallProgram intent that starts a server ProgramType by program NodeId with optional " +
            "arguments. Use it only for a controller program; use robotics_submit_place for " +
            "placing objects and robotics_submit_tool_change for changing end effectors. A program NodeId naming " +
            "anything else is refused. Server refusals are returned with IntentFailureEnum and are never retried. " +
            "Command authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitCallProgramAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description(CallProgramIntentJsonDescription)] string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "callProgram", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_wait")]
        [Description("Submits a Wait intent that delays for a duration in seconds or until a signal/Boolean node " +
            "under the controller satisfies the server's wait condition. Use this to insert timing or signal waits " +
            "in a queue or mission; it does not poll the MCP client, watch an existing operation, or command motion. " +
            "Signal, duration, mode, safety, or authority refusals are returned with IntentFailureEnum and are never " +
            "retried. Command authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitWaitAsync(
            RoboticsIntentManager manager,
            [Description(ControllerIdDescription)] string controllerId,
            [Description(WaitIntentJsonDescription)] string intentJson,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "wait", intentJson, 0, sessionName, ct);
        }

        private const string ControllerIdDescription =
            "Required OPC UA NodeId string for the Robot Intent controller to command, for example " +
            "ns=2;s=RobotIntent/Controllers/Controller1. It must identify a controller already discovered in the " +
            "selected session; malformed NodeIds or nodes that are not Robot Intent controllers fail before any " +
            "robot command is submitted.";

        private const string SessionNameDescription =
            "Optional MCP OPC UA session name. Omit it only when exactly one OPC UA session is active; provide the " +
            "name returned by the session-management tools when multiple sessions are connected. If the name is " +
            "missing, ambiguous, or unknown, the tool fails before sending any robot command.";

        private const string ArcWeldIntentJsonDescription =
            "Required JSON object for ArcWeld. Optional fields are processProgram as a ProgramType NodeId under the " +
            "controller, voltage, wireFeedSpeed, travelSpeed, seamTrackingEnabled, weldProcedureRef, attributes, and " +
            "common motion fields such as intentId, label, bufferMode, and blockingMode. Wrong NodeIds or malformed " +
            "numbers are refused as ParameterInvalid or fail as argument errors.";

        private const string SpotWeldIntentJsonDescription =
            "Required JSON object for SpotWeld. Optional fields are processProgram as a ProgramType NodeId under the " +
            "controller, weldSchedule number, gunForce in newtons, attributes, intentId, label, bufferMode, and " +
            "blockingMode. Wrong NodeIds or malformed numeric values are refused as ParameterInvalid or fail as " +
            "argument errors.";

        private const string DispenseIntentJsonDescription =
            "Required JSON object for Dispense. Optional fields are processProgram ProgramType NodeId, flowRate, " +
            "beadWidth in metres, purgeCycles, attributes, intentId, label, bufferMode, and blockingMode. Use only " +
            "controller-published process programs; malformed values or foreign NodeIds are refused as " +
            "ParameterInvalid or fail as argument errors.";

        private const string FastenIntentJsonDescription =
            "Required JSON object for Fasten. Optional fields are joint as a controller joining-model NodeId, " +
            "programNumber, targetTorque in newton-metres, processProgram, attributes, intentId, label, bufferMode, " +
            "and blockingMode. A joint outside the controller or unsupported joining model is refused as " +
            "ParameterInvalid or CapabilityNotSupported.";

        private const string PalletiseIntentJsonDescription =
            "Required JSON object for Palletise. Optional fields are pattern as a LocationType NodeId under the " +
            "controller, layer, row, column indexes, processProgram, attributes, intentId, label, bufferMode, and " +
            "blockingMode. Pattern NodeIds outside the controller or malformed indexes are refused as " +
            "ParameterInvalid or fail as argument errors.";

        private const string SurfaceFinishIntentJsonDescription =
            "Required JSON object for SurfaceFinish. Optional fields are processProgram, contactForce in newtons, " +
            "feedRate, toolSpeed, stepOver in metres, attributes, intentId, label, bufferMode, and blockingMode. Use " +
            "only when the controller declares the SurfaceFinish/Force capability; unsupported or malformed values " +
            "are returned as CapabilityNotSupported, ParameterInvalid, or argument errors.";

        private const string GraspIntentJsonDescription =
            "Required JSON object for Grasp with controller ToolType NodeId and force in newtons. " +
            "Optional common fields are intentId, label, bufferMode, and blockingMode. A missing/foreign tool NodeId " +
            "or malformed force is refused as ParameterInvalid or fails as an argument error.";

        private const string ReleaseIntentJsonDescription =
            "Required JSON object for Release with tool as ToolType NodeId to open/deactivate. Optional common " +
            "fields are intentId, label, bufferMode, and blockingMode. A missing, malformed, or foreign tool NodeId " +
            "is refused as ParameterInvalid or fails as an argument error.";

        private const string PickIntentJsonDescription =
            "Required JSON object for Pick with source as a LocationType NodeId under the controller and tool as the " +
            "ToolType NodeId used to acquire the object. Optional common fields are intentId, label, bufferMode, and " +
            "blockingMode. Foreign or wrong-type NodeIds are refused as ParameterInvalid.";

        private const string PlaceIntentJsonDescription =
            "Required JSON object for Place with destination LocationType NodeId and tool as " +
            "the ToolType NodeId used to release the object. Optional common fields are intentId, label, bufferMode, " +
            "and blockingMode. Foreign or wrong-type NodeIds are refused as ParameterInvalid.";

        private const string ToolChangeIntentJsonDescription =
            "Required JSON object for ToolChange with tool as the ToolType NodeId to fit, or null/omitted to release " +
            "the current tool; dockStation identifies the dock LocationType or station NodeId when required by the " +
            "server. Optional common fields are intentId, label, bufferMode, and blockingMode. Invalid NodeIds are " +
            "refused as ParameterInvalid.";

        private const string SetOutputIntentJsonDescription =
            "Required JSON object for SetOutput with controller OutputSignalType NodeId, value " +
            "as the JSON value to write, and optional dataType to guide Variant conversion. Optional common fields " +
            "are intentId, label, bufferMode, and blockingMode. Wrong output NodeIds or values that do not match the " +
            "signal DataType are refused as ParameterInvalid.";

        private const string CallProgramIntentJsonDescription =
            "Required JSON object for CallProgram with program as controller ProgramType NodeId " +
            "and optional arguments object of name/value pairs. Optional common fields are intentId, label, " +
            "bufferMode, and blockingMode. A NodeId that names anything other than a controller ProgramType is " +
            "refused as ParameterInvalid.";

        private const string WaitIntentJsonDescription =
            "Required JSON object for Wait with duration in seconds and/or signal as an OutputSignalType or Boolean " +
            "Variable NodeId under the controller. Optional common fields are intentId, label, bufferMode, and " +
            "blockingMode. Invalid signal NodeIds or unsupported wait semantics are refused as ParameterInvalid or " +
            "CapabilityNotSupported.";

        internal static async Task<IntentSubmissionResult> SubmitIntentAsync(
            RobotIntentControllerClient controller,
            string intentKind,
            string? intentJson,
            uint axisCount,
            CancellationToken ct)
        {
            IntentDataType intent = RoboticsIntentJson.BuildIntent(intentKind, intentJson, axisCount);
            return await controller.TrySubmitIntentAsync(intent, ct).ConfigureAwait(false);
        }

        private static Task<IntentSubmissionResult> SubmitIntentAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentKind,
            string? intentJson,
            uint axisCount,
            string? sessionName,
            CancellationToken ct)
        {
            RobotIntentControllerClient controller = manager.OpenController(controllerId, sessionName);
            return SubmitIntentAsync(controller, intentKind, intentJson, axisCount, ct);
        }
    }
}
