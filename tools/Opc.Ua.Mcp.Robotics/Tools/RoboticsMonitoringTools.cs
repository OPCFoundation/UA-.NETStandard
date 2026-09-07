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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Robotics.Client.Intent;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for reading Robot Intent live state and outstanding work.
    /// </summary>
    [McpServerToolType]
    public sealed class RoboticsMonitoringTools
    {
        /// <summary>
        /// Reads one controller's current runtime state.
        /// </summary>
        [McpServerTool(Name = "robotics_read_state")]
        [Description("Reads live Robot Intent controller state now: operational mode, Ready, control owner, active " +
            "intent/mission, safety state, and queue listings when published. This monitoring tool does not infer " +
            "state from capabilities and does not request command authority.")]
        public static async Task<RobotIntentControllerState> ReadStateAsync(
            RoboticsIntentManager manager,
            [Description(RoboticsControlTools.ControllerDescription)] string controller,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RobotIntentControllerClient resolved = await manager.ResolveControllerAsync(
                controller, sessionName, ct).ConfigureAwait(false);
            return await resolved.ReadStateAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Lists outstanding intent operations with filtering and paging.
        /// </summary>
        [McpServerTool(Name = "robotics_list_operations")]
        [Description("Lists Robot Intent operation snapshots published by the controller: active, queued, " +
            "completed-retained, or failed intent-operation snapshots with their IntentId and operation NodeId. Use " +
            "this after submitting or retrying one intent, or before robotics_wait_operation. Use " +
            "robotics_list_missions instead when you need mission containers and MissionIds rather than per-intent " +
            "operations. It reports server state only, never invents work, and never requests command authority. " +
            "Returns a paged OperationListResult with summaries or full snapshots.")]
        public static async Task<OperationListResult> ListOperationsAsync(
            RoboticsIntentManager manager,
            [Description(RoboticsControlTools.ControllerDescription)] string controller,
            [Description("Optional query: filter by intentId, missionId, executionState, work selector; choose " +
                "summary or full detail; page with pageSize (default 20, max 100) and cursor.")]
            OperationListQuery? query = null,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RobotIntentControllerClient resolved = await manager.ResolveControllerAsync(
                controller, sessionName, ct).ConfigureAwait(false);
            ArrayOf<IntentOperationSnapshot> all = await resolved.ListOperationsAsync(ct).ConfigureAwait(false);
            return RoboticsListPaging.PageOperations(all, query);
        }

        /// <summary>
        /// Lists outstanding missions with filtering and paging.
        /// </summary>
        [McpServerTool(Name = "robotics_list_missions")]
        [Description("Lists outstanding Robot Intent mission containers from the controller, including MissionId, " +
            "update state, and mission-level progress when the server exposes it. Use this after submitting, " +
            "updating, or cancelling a mission. Use robotics_list_operations instead to inspect the individual " +
            "intent operations spawned by a mission or single-intent submission. It reports server state only, " +
            "never invents mission state, and never requests command authority. Returns a paged MissionListResult " +
            "with summaries or full snapshots.")]
        public static async Task<MissionListResult> ListMissionsAsync(
            RoboticsIntentManager manager,
            [Description(RoboticsControlTools.ControllerDescription)] string controller,
            [Description("Optional query: filter by missionId, executionState, work selector; choose summary or " +
                "full detail; page with pageSize (default 20, max 100) and cursor.")]
            MissionListQuery? query = null,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RobotIntentControllerClient resolved = await manager.ResolveControllerAsync(
                controller, sessionName, ct).ConfigureAwait(false);
            ArrayOf<MissionSnapshot> all = await resolved.ListMissionsAsync(ct).ConfigureAwait(false);
            return RoboticsListPaging.PageMissions(all, query);
        }

        /// <summary>
        /// Waits for an existing operation with a bounded timeout.
        /// </summary>
        [McpServerTool(Name = "robotics_wait_operation")]
        [Description("Waits for an intent operation to complete up to timeoutMs. Timeout is not an error: the " +
            "result has completed=false and includes the current operation snapshot refreshed from the server. This " +
            "tool does not retry refusals or resubmit work.")]
        public static async Task<IntentOperationWaitResult> WaitOperationAsync(
            RoboticsIntentManager manager,
            [Description(RoboticsControlTools.ControllerDescription)] string controller,
            [Description("IntentId associated with the operation.")] string intentId,
            [Description("IntentOperation NodeId to observe.")] string operationNodeId,
            [Description("Maximum wait in milliseconds; <=0 performs a poll/refresh.")] int timeoutMs = 2000,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RobotIntentControllerClient resolved = await manager.ResolveControllerAsync(
                controller, sessionName, ct).ConfigureAwait(false);
            return await WaitOperationCoreAsync(resolved, intentId, operationNodeId, timeoutMs, ct)
                .ConfigureAwait(false);
        }

        internal static async Task<IntentOperationWaitResult> WaitOperationCoreAsync(
            RobotIntentControllerClient controller,
            string intentId,
            string operationNodeId,
            int timeoutMs,
            CancellationToken ct)
        {
            NodeId operation = OpcUaJsonHelper.ParseNodeId(operationNodeId);
            IntentOperationHandle handle = await controller.TrackOperationAsync(
                intentId,
                operation,
                ct).ConfigureAwait(false);
            try
            {
                var timeout = TimeSpan.FromMilliseconds(timeoutMs <= 0 ? 0 : timeoutMs);
                return await handle.WaitForCompletionAsync(timeout, ct).ConfigureAwait(false);
            }
            finally
            {
                await handle.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Waits for an existing mission with a bounded timeout.
        /// </summary>
        [McpServerTool(Name = "robotics_wait_mission")]
        [Description("Waits for a Robot Intent mission to reach a terminal state, up to timeoutMs. The wait is " +
            "always bounded and observes the mission node the server published; it never polls " +
            "robotics_list_missions, never retries, and never resubmits or updates the mission. Timeout is not an " +
            "error: the result has completed=false with the mission snapshot refreshed from the server. Command " +
            "authority is never acquired as a side effect. Returns MissionWaitResult.")]
        public static async Task<MissionWaitResult> WaitMissionAsync(
            RoboticsIntentManager manager,
            [Description(RoboticsControlTools.ControllerDescription)] string controller,
            [Description("MissionId associated with the mission operation.")] string missionId,
            [Description("Mission operation NodeId to observe, as returned by robotics_submit_mission or " +
                "robotics_list_missions.")]
            string missionNodeId,
            [Description("Maximum wait in milliseconds; <=0 performs a poll/refresh. Bounded by " +
                "MaxWaitMilliseconds.")]
            int timeoutMs = 2000,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RobotIntentControllerClient resolved = await manager.ResolveControllerAsync(
                controller, sessionName, ct).ConfigureAwait(false);
            return await WaitMissionCoreAsync(resolved, missionId, missionNodeId, timeoutMs, ct)
                .ConfigureAwait(false);
        }

        internal static async Task<MissionWaitResult> WaitMissionCoreAsync(
            RobotIntentControllerClient controller,
            string missionId,
            string missionNodeId,
            int timeoutMs,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(controller);
            ArgumentException.ThrowIfNullOrWhiteSpace(missionId);

            if (timeoutMs > MaxWaitMilliseconds)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'timeoutMs' must not exceed {MaxWaitMilliseconds} but was {timeoutMs}."),
                    nameof(timeoutMs));
            }

            NodeId missionNode = OpcUaJsonHelper.ParseNodeId(missionNodeId);
            MissionHandle handle = await controller.TrackMissionAsync(
                missionId,
                missionNode,
                ct).ConfigureAwait(false);
            try
            {
                var timeout = TimeSpan.FromMilliseconds(timeoutMs <= 0 ? 0 : timeoutMs);
                return await handle.WaitForCompletionAsync(timeout, ct).ConfigureAwait(false);
            }
            finally
            {
                await handle.DisposeAsync().ConfigureAwait(false);
            }
        }

        private const int MaxWaitMilliseconds = 600000;
    }
}
