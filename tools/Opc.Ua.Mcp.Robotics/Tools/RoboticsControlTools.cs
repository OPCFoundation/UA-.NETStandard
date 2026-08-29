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
            [Description(ControllerDescription)] string controller,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RobotIntentControllerClient resolved = await manager.ResolveControllerAsync(
                controller, sessionName, ct).ConfigureAwait(false);
            return await resolved.Transport.RequestControlAsync(ct)
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
            [Description(ControllerDescription)] string controller,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RobotIntentControllerClient resolved = await manager.ResolveControllerAsync(
                controller, sessionName, ct).ConfigureAwait(false);
            await resolved.ReleaseControlAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_cancel_intent")]
        [Description("Requests cancellation of one intent. A server refusal such as NotPermittedInMode or " +
            "ControlNotOwned is returned by the client API; this tool never retries or turns it into a resubmit.")]
        public static async Task<IntentCommandOutcome> CancelIntentAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("IntentId to cancel.")] string intentId,
            [Description("Stop mode requested from the server.")] StopModeEnum stopMode = StopModeEnum.QuickStop,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RobotIntentControllerClient resolved = await manager.ResolveControllerAsync(
                controller, sessionName, ct).ConfigureAwait(false);
            return await resolved.CancelIntentAsync(intentId, stopMode, ct)
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
            [Description(ControllerDescription)] string controller,
            [Description("Stop mode requested from the server.")] StopModeEnum stopMode = StopModeEnum.QuickStop,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RobotIntentControllerClient resolved = await manager.ResolveControllerAsync(
                controller, sessionName, ct).ConfigureAwait(false);
            return await resolved.CancelAllAsync(stopMode, ct)
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
            [Description(ControllerDescription)] string controller,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RobotIntentControllerClient resolved = await manager.ResolveControllerAsync(
                controller, sessionName, ct).ConfigureAwait(false);
            return await resolved.PauseAsync(ct).ConfigureAwait(false);
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
            [Description(ControllerDescription)] string controller,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RobotIntentControllerClient resolved = await manager.ResolveControllerAsync(
                controller, sessionName, ct).ConfigureAwait(false);
            return await resolved.ResumeAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_retry")]
        [Description("Requests Retry for an existing intent. The admission result preserves the exact server " +
            "IntentFailureEnum and message when refused; this tool performs no client-side retry loop.")]
        public static async Task<IntentSubmissionResult> RetryAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("IntentId to retry.")] string intentId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RobotIntentControllerClient resolved = await manager.ResolveControllerAsync(
                controller, sessionName, ct).ConfigureAwait(false);
            return await resolved.RetryAsync(intentId, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Submits a JointMove motion intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_joint_move")]
        [Description("Submits a JointMove motion intent: point-to-point joint motion to jointTargets in radians " +
            "or to a targetPose solved by the controller. Use this for robot-axis positioning, not Cartesian path " +
            "following. Refusals such as NotPermittedInMode, SafetyLimitExceeded, ControlNotOwned, " +
            "CapabilityNotSupported, ParameterInvalid, or QueueFull are reported with their IntentFailureEnum and " +
            "are never retried. Command authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitJointMoveAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("JointMove input: set jointTargets (radians) or targetPose with position/orientation.")]
            JointMoveIntentInput input,
            [Description("Axis count for local validation. Default 0 disables and leaves validation to the server.")]
            uint axisCount = 0,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertJointMove(input, axisCount, scope),
                ct);
        }

        /// <summary>
        /// Submits a LinearMove motion intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_linear_move")]
        [Description("Submits a LinearMove motion intent: a straight Cartesian segment to target. " +
            "Use this for a single linear tool-centre-point move; use robotics_submit_circular_move for an arc via " +
            "a viaPoint and robotics_submit_cartesian_path for multiple taught waypoints. Server refusals are " +
            "returned with IntentFailureEnum and are never retried. Command authority is never acquired as a side " +
            "effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitLinearMoveAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("LinearMove input with target pose: position in metres, quaternion orientation.")]
            LinearMoveIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertLinearMove(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a CircularMove motion intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_circular_move")]
        [Description("Submits a CircularMove motion intent: a Cartesian arc through viaPoint to " +
            "target. Use this when the intermediate point defines circular geometry; use robotics_submit_linear_move " +
            "for a straight segment and robotics_submit_cartesian_path for a multi-waypoint path. Server refusals " +
            "are returned with IntentFailureEnum and never retried. Command authority is never acquired as a side " +
            "effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitCircularMoveAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("CircularMove input with viaPoint and target poses.")]
            CircularMoveIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertCircularMove(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a Trajectory motion intent.
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
            [Description(ControllerDescription)] string controller,
            [Description("Trajectory input with points array (timeFromStart, positions, optional " +
                "velocities/accelerations).")]
            TrajectoryIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertTrajectory(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a CartesianPath motion intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_cartesian_path")]
        [Description("Submits a CartesianPath motion intent: taught Cartesian waypoints with optional " +
            "per-waypoint blending, planned by the controller. Use this for multi-waypoint Cartesian paths; use " +
            "robotics_submit_trajectory for a timed joint trajectory supplied as points. Server refusals remain " +
            "results with the specific IntentFailureEnum and are never retried. Command authority is never acquired " +
            "as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitCartesianPathAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("CartesianPath input with waypoints array, each with pose and optional blend.")]
            CartesianPathIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertCartesianPath(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a Force motion intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_force")]
        [Description("Submits a Force motion intent: move along direction until contactForce is reached, optionally " +
            "limited by maxDistance and holdForce. Use this for compliant contact search or force regulation, not " +
            "for surface-finishing process parameters. SafetyLimitExceeded and other refusals are returned with " +
            "IntentFailureEnum and are never retried. Command authority is never acquired as a side effect. Returns " +
            "IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitForceAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("Force input with direction (3-element unit vector), contactForce, optional frameId, " +
                "maxDistance, holdForce.")]
            ForceIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertForce(input, scope),
                ct);
        }

        /// <summary>
        /// Submits an ArcWeld process intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_arc_weld")]
        [Description("Submits an ArcWeld process intent for continuous welding along the motion path, using optional " +
            "processProgram plus voltage, wireFeedSpeed, travelSpeed, seamTrackingEnabled, and weldProcedureRef. " +
            "Use this for arc-welding process execution; use robotics_submit_spot_weld for discrete weld spots. " +
            "Server refusals are returned with IntentFailureEnum and are never retried. Command authority is never " +
            "acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitArcWeldAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("ArcWeld input with optional processProgram, voltage, wireFeedSpeed, travelSpeed, " +
                "seamTrackingEnabled, weldProcedureRef.")]
            ArcWeldIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertArcWeld(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a SpotWeld process intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_spot_weld")]
        [Description("Submits a SpotWeld process intent for discrete resistance weld points, with processProgram " +
            "plus weldSchedule and gunForce. Use this for spot-welding cycles; use robotics_submit_arc_weld for a " +
            "continuous weld seam. Server refusals are returned with IntentFailureEnum and are never retried. " +
            "Command authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitSpotWeldAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("SpotWeld input with optional processProgram, weldSchedule, gunForce.")]
            SpotWeldIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertSpotWeld(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a Dispense process intent.
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
            [Description(ControllerDescription)] string controller,
            [Description("Dispense input with optional processProgram, flowRate, beadWidth, purgeCycles.")]
            DispenseIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertDispense(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a Fasten process intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_fasten")]
        [Description("Submits a Fasten process intent for tightening or joining at a fastener/joint, using optional " +
            "processProgram plus joint NodeId, programNumber, and targetTorque. Use this for fastening operations; " +
            "use robotics_submit_dispense for material flow and robotics_submit_surface_finish for abrasive or " +
            "finishing contact. Server refusals are returned with IntentFailureEnum and are never retried. Command " +
            "authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitFastenAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("Fasten input with optional joint NodeId, programNumber, targetTorque, processProgram.")]
            FastenIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertFasten(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a Palletise process intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_palletise")]
        [Description("Submits a Palletise process intent that places workpieces using a controller-defined pattern " +
            "LocationType, with layer, row, and column indexes selecting the cell. Use this for pallet pattern " +
            "placement; use robotics_submit_place for a single explicit destination and robotics_submit_dispense for " +
            "material deposition. Server refusals are returned with IntentFailureEnum and are never retried. Command " +
            "authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitPalletiseAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("Palletise input with optional pattern NodeId, layer, row, column.")]
            PalletiseIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertPalletise(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a SurfaceFinish process intent.
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
            [Description(ControllerDescription)] string controller,
            [Description("SurfaceFinish input with optional processProgram, contactForce, feedRate, " +
                "toolSpeed, stepOver.")]
            SurfaceFinishIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertSurfaceFinish(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a Grasp intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_grasp")]
        [Description("Submits a Grasp intent to close or activate a tool on an object, with tool NodeId and " +
            "force. Use this to acquire a workpiece using an end effector; use robotics_submit_release to open or " +
            "deactivate the tool, and robotics_submit_pick when source motion plus grasp should be one pick intent. " +
            "Control, safety, mode, capability, or parameter refusals are returned with IntentFailureEnum and are " +
            "never retried. Command authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitGraspAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("Grasp input with tool NodeId and force in newtons.")]
            GraspIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertGrasp(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a Release intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_release")]
        [Description("Submits a Release intent to open or deactivate a gripper/tool by tool NodeId. Use this to " +
            "let go of a held object; use robotics_submit_grasp to acquire it, robotics_submit_place when " +
            "motion plus release should be one intent, and robotics_submit_tool_change to fit or remove a tool at a " +
            "dock. Server refusals are returned with IntentFailureEnum and are never retried. Command authority is " +
            "never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitReleaseAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("Release input with tool NodeId.")]
            ReleaseIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertRelease(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a Pick intent.
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
            [Description(ControllerDescription)] string controller,
            [Description("Pick input with source NodeId, tool NodeId, and optional objectClass label.")]
            PickIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertPick(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a Place intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_place")]
        [Description("Submits a Place intent for outbound material handling: carry the held workpiece to the " +
            "destination LocationType and let it go with the selected tool. Choose it when the robot must deposit or " +
            "unload something held. Do not call it to collect from a source or close the gripper; that is " +
            "robotics_submit_pick or robotics_submit_grasp. Refusals are surfaced with their IntentFailureEnum, " +
            "without retrying or silently taking command authority. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitPlaceAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("Place input with destination NodeId and tool NodeId.")]
            PlaceIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertPlace(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a ToolChange intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_tool_change")]
        [Description("Submits a ToolChange intent to fit a docked tool or release the current tool when tool is " +
            "null. Use this for changing the robot end effector; use robotics_submit_release only to open/deactivate " +
            "a gripper, and robotics_submit_call_program only to run a server program. Server refusals are returned " +
            "with IntentFailureEnum and are never retried. Command authority is never acquired as a side effect. " +
            "Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitToolChangeAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("ToolChange input with optional tool NodeId and dockStation NodeId.")]
            ToolChangeIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertToolChange(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a SetOutput intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_set_output")]
        [Description("Submits a SetOutput intent that writes a controller OutputSignalType to a supplied value. " +
            "Use it for typed outputs, not robot motion or program execution. The server " +
            "validates the output NodeId and value DataType; refusals are returned with IntentFailureEnum and are " +
            "never retried. Command authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitSetOutputAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("SetOutput input with output NodeId and typed value.")]
            SetOutputIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertSetOutput(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a CallProgram intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_call_program")]
        [Description("Submits a CallProgram intent that starts a server ProgramType by program NodeId with optional " +
            "arguments. Use it only for a controller program; use robotics_submit_place for " +
            "placing objects and robotics_submit_tool_change for changing end effectors. A program NodeId naming " +
            "anything else is refused. Server refusals are returned with IntentFailureEnum and are never retried. " +
            "Command authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitCallProgramAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("CallProgram input with program NodeId and optional arguments.")]
            CallProgramIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertCallProgram(input, scope),
                ct);
        }

        /// <summary>
        /// Submits a Wait intent.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_wait")]
        [Description("Submits a Wait intent that delays for a duration in seconds or until a signal/Boolean node " +
            "under the controller satisfies the server's wait condition. Use this to insert timing or signal waits " +
            "in a queue or mission; it does not poll the MCP client, watch an existing operation, or command motion. " +
            "Signal, duration, mode, safety, or authority refusals are returned with IntentFailureEnum and are never " +
            "retried. Command authority is never acquired as a side effect. Returns IntentSubmissionResult.")]
        public static Task<IntentSubmissionResult> SubmitWaitAsync(
            RoboticsIntentManager manager,
            [Description(ControllerDescription)] string controller,
            [Description("Wait input with duration in seconds and optional signal NodeId.")]
            WaitIntentInput input,
            [Description(SessionNameDescription)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitAsync(
                manager,
                controller,
                sessionName,
                scope => RoboticsIntentDtoConverter.ConvertWait(input, scope),
                ct);
        }

        internal const string ControllerDescription =
            "Controller selector: unique display name or BrowseName (e.g. 'Controller1') or OPC UA " +
            "NodeId string (e.g. ns=2;s=RobotIntent/Controllers/Controller1). Matched with exact " +
            "ordinal comparison after trimming. Fails with available names and NodeIds when zero " +
            "or multiple controllers match. Names of frames, tools, locations, outputs, programs, " +
            "and axes inside the request are resolved against this controller's published lookup " +
            "tables; full NodeIds are always accepted and validated by the server.";

        private const string SessionNameDescription =
            "Optional MCP OPC UA session name. Omit it only when exactly one OPC UA session is active; provide the " +
            "name returned by the session-management tools when multiple sessions are connected. If the name is " +
            "missing, ambiguous, or unknown, the tool fails before sending any robot command.";

        private static async Task<IntentSubmissionResult> SubmitAsync(
            RoboticsIntentManager manager,
            string controller,
            string? sessionName,
            Func<RoboticsScopeResolver, IntentDataType> convert,
            CancellationToken ct)
        {
            RoboticsResolutionContext context = await RoboticsResolutionContext.CreateAsync(
                manager, controller, sessionName, ct).ConfigureAwait(false);
            IntentDataType intent = convert(context.Scope);
            return await context.Client.TrySubmitIntentAsync(intent, ct).ConfigureAwait(false);
        }
    }
}
