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
        [Description("Requests Pause. If the server refuses because the mode, safety state, or control ownership " +
            "does not permit it, that refusal is returned by the client API and is never retried here.")]
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
        [Description("Requests Resume. If the server refuses because mode, safety state, or control ownership does " +
            "not permit it, that refusal is returned by the client API and is never retried here.")]
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
            return await manager.OpenController(controllerId, sessionName).RetryAsync(intentId, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_joint_move")]
        [Description("Submits a JointMove intent from JSON. Refusals such as NotPermittedInMode, " +
            "SafetyLimitExceeded, ControlNotOwned, CapabilityNotSupported, ParameterInvalid, or QueueFull are " +
            "reported verbatim with the server message; this tool never retries or requests authority implicitly.")]
        public static Task<IntentSubmissionResult> SubmitJointMoveAsync(
            RoboticsIntentManager manager,
            string controllerId,
            [Description("Intent JSON with jointTargets or targetPose plus optional common fields.")] string intentJson,
            [Description("Axis count used for builder validation; 0 disables local count validation.")] uint axisCount = 0,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "jointMove", intentJson, axisCount, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_linear_move")]
        [Description("Submits a LinearMove intent from JSON. Server refusals are returned verbatim with " +
            "IntentFailureEnum and message; no retry or implicit command-authority request is performed.")]
        public static Task<IntentSubmissionResult> SubmitLinearMoveAsync(
            RoboticsIntentManager manager,
            string controllerId,
            [Description("Intent JSON with target pose and optional constraints/blend/common fields.")] string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "linearMove", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_circular_move")]
        [Description("Submits a CircularMove intent from JSON. Server refusals are returned verbatim; no retry or " +
            "implicit command-authority request is performed.")]
        public static Task<IntentSubmissionResult> SubmitCircularMoveAsync(
            RoboticsIntentManager manager,
            string controllerId,
            [Description("Intent JSON with viaPoint, target, and optional constraints/blend/common fields.")] string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "circularMove", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_trajectory")]
        [Description("Submits a Trajectory intent from JSON. CapabilityNotSupported and safety/mode/authority " +
            "refusals are reported exactly as returned by the server; the MCP layer does not simplify them.")]
        public static Task<IntentSubmissionResult> SubmitTrajectoryAsync(
            RoboticsIntentManager manager,
            string controllerId,
            [Description("Intent JSON with points array and optional constraints/blend/common fields.")] string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "trajectory", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_cartesian_path")]
        [Description("Submits a CartesianPath intent from JSON. Server refusals remain refusal results with the " +
            "specific IntentFailureEnum; no client-side retry is attempted.")]
        public static Task<IntentSubmissionResult> SubmitCartesianPathAsync(
            RoboticsIntentManager manager,
            string controllerId,
            [Description("Intent JSON with waypoints array and optional constraints/blend/common fields.")] string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "cartesianPath", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_force")]
        [Description("Submits a Force intent from JSON. SafetyLimitExceeded and other server refusals are returned " +
            "verbatim and are never retried by this tool.")]
        public static Task<IntentSubmissionResult> SubmitForceAsync(
            RoboticsIntentManager manager,
            string controllerId,
            [Description("Intent JSON with direction, contactForce, and optional motion/common fields.")] string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "force", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_arc_weld")]
        [Description("Submits an ArcWeld intent from JSON. The server's refusal enum and message are preserved; no " +
            "authority is requested implicitly.")]
        public static Task<IntentSubmissionResult> SubmitArcWeldAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "arcWeld", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_spot_weld")]
        [Description("Submits a SpotWeld intent from JSON. The server's refusal enum and message are preserved; no " +
            "client-side retry is attempted.")]
        public static Task<IntentSubmissionResult> SubmitSpotWeldAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "spotWeld", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_dispense")]
        [Description("Submits a Dispense intent from JSON and reports any server refusal verbatim.")]
        public static Task<IntentSubmissionResult> SubmitDispenseAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "dispense", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_fasten")]
        [Description("Submits a Fasten intent from JSON and reports any server refusal verbatim.")]
        public static Task<IntentSubmissionResult> SubmitFastenAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "fasten", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_palletise")]
        [Description("Submits a Palletise intent from JSON and reports any server refusal verbatim.")]
        public static Task<IntentSubmissionResult> SubmitPalletiseAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "palletise", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_surface_finish")]
        [Description("Submits a SurfaceFinish intent from JSON and reports any server refusal verbatim.")]
        public static Task<IntentSubmissionResult> SubmitSurfaceFinishAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "surfaceFinish", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_grasp")]
        [Description("Submits a Grasp intent from JSON. ControlNotOwned and safety/mode refusals are reported " +
            "verbatim; the tool does not request authority implicitly.")]
        public static Task<IntentSubmissionResult> SubmitGraspAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "grasp", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_release")]
        [Description("Submits a Release intent from JSON and reports server refusals verbatim.")]
        public static Task<IntentSubmissionResult> SubmitReleaseAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "release", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_pick")]
        [Description("Submits a Pick intent from JSON and reports server refusals verbatim.")]
        public static Task<IntentSubmissionResult> SubmitPickAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "pick", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_place")]
        [Description("Submits a Place intent from JSON and reports server refusals verbatim.")]
        public static Task<IntentSubmissionResult> SubmitPlaceAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "place", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_tool_change")]
        [Description("Submits a ToolChange intent from JSON and reports server refusals verbatim.")]
        public static Task<IntentSubmissionResult> SubmitToolChangeAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "toolChange", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_set_output")]
        [Description("Submits a SetOutput intent from JSON. The server validates the output and value; refusals are " +
            "reported verbatim and are never retried.")]
        public static Task<IntentSubmissionResult> SubmitSetOutputAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "setOutput", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_call_program")]
        [Description("Submits a CallProgram intent from JSON and reports server refusals verbatim.")]
        public static Task<IntentSubmissionResult> SubmitCallProgramAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "callProgram", intentJson, 0, sessionName, ct);
        }

        /// <summary>
        /// Executes a Robot Intent direct-control MCP tool.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_wait")]
        [Description("Submits a Wait intent from JSON. If the signal or duration is not permitted, the server " +
            "refusal is reported verbatim and never retried.")]
        public static Task<IntentSubmissionResult> SubmitWaitAsync(
            RoboticsIntentManager manager,
            string controllerId,
            string intentJson,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return SubmitIntentAsync(manager, controllerId, "wait", intentJson, 0, sessionName, ct);
        }

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
