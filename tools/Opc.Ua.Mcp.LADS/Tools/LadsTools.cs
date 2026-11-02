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
 *
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
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.LADS;
using Opc.Ua.LADS.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Bounded LADS discovery, typed snapshots, commands, programs, results, files and maintenance.
    /// </summary>
    [McpServerToolType]
    public sealed class LadsTools
    {
        /// <summary>
        /// Creates LADS tools over the host-owned session and file-transfer policies.
        /// </summary>
        public LadsTools(LadsClientAccessor accessor, McpFileTransfers transfers)
        {
            m_accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
            m_transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
        }

        /// <summary>
        /// Discovers LADS devices.
        /// </summary>
        [McpServerTool(Name = "lads_list_devices", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Discovers unique OPC 30500-1 LADS devices below DI DeviceSet and Machinery Machines. " +
            "Returns a live page; use nextOffset for the next page.")]
        public Task<CallToolResult> ListDevicesAsync(
            [Description("Zero-based live result offset, 0..1000000.")] int offset = 0,
            [Description("Maximum returned items per page, 1..500.")] int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (LadsClient client, _) = m_accessor.CreateClient(sessionName);
                return McpCompanionTools.Page(
                    await client.GetDevicesAsync(token).ConfigureAwait(false), LadsJson.DeviceEntry, offset, maxResults);
            }, ct);
        }

        /// <summary>
        /// Reads a device snapshot.
        /// </summary>
        [McpServerTool(Name = "lads_read_device", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads LADS and DI identification, health, device and Machinery states, and functional-unit names.")]
        public Task<CallToolResult> ReadDeviceAsync(
            [Description("Device NodeId returned by lads_list_devices.")] string deviceNodeId,
            [Description("Named OPC UA session; omit only when exactly one is connected.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (LadsClient client, ISession session) = m_accessor.CreateClient(sessionName);
                LadsDeviceSnapshot value = await client.ReadDeviceAsync(ParseNode(deviceNodeId), token)
                    .ConfigureAwait(false);
                return LadsJson.Device(value, session.MessageContext);
            }, ct);
        }

        /// <summary>
        /// Reads a functional unit snapshot.
        /// </summary>
        [McpServerTool(Name = "lads_read_functional_unit", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads functional-unit state, active-program progress and its nested control and sensor " +
            "function snapshots. Set includeFunctions=false to omit the nested function reads.")]
        public Task<CallToolResult> ReadFunctionalUnitAsync(
            [Description("FunctionalUnit NodeId from a LADS device.")] string functionalUnitNodeId,
            [Description("Read the nested function snapshots as well as unit state.")]
            bool includeFunctions = true,
            [Description("Named OPC UA session; omit only when exactly one is connected.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (LadsClient client, ISession session) = m_accessor.CreateClient(sessionName);
                LadsFunctionalUnitSnapshot value = await client.ReadFunctionalUnitAsync(
                    ParseNode(functionalUnitNodeId), includeFunctions, token).ConfigureAwait(false);
                return LadsJson.FunctionalUnit(value, session.MessageContext);
            }, ct);
        }

        /// <summary>
        /// Reads one function snapshot.
        /// </summary>
        [McpServerTool(Name = "lads_read_function", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads one LADS function's standard type, state, enablement and published value variables.")]
        public Task<CallToolResult> ReadFunctionAsync(
            [Description("Function NodeId from a functional-unit snapshot.")] string functionNodeId,
            [Description("Named OPC UA session; omit only when exactly one is connected.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (LadsClient client, ISession session) = m_accessor.CreateClient(sessionName);
                LadsFunctionSnapshot value = await client.ReadFunctionAsync(ParseNode(functionNodeId), token)
                    .ConfigureAwait(false);
                return LadsJson.Function(value, session.MessageContext);
            }, ct);
        }

        /// <summary>
        /// Lists templates for one functional unit.
        /// </summary>
        [McpServerTool(Name = "lads_list_program_templates", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Lists the templates exposed by a functional unit's ProgramManager. Pass templateId from " +
            "this result to lads_start_program.")]
        public Task<CallToolResult> ListProgramTemplatesAsync(
            [Description("FunctionalUnit NodeId.")] string functionalUnitNodeId,
            [Description("Zero-based live result offset, 0..1000000.")] int offset = 0,
            [Description("Maximum returned items per page, 1..500.")] int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (LadsClient client, _) = m_accessor.CreateClient(sessionName);
                ArrayOf<LadsProgramTemplate> values = await client.GetProgramTemplatesAsync(
                    ParseNode(functionalUnitNodeId), token).ConfigureAwait(false);
                return McpCompanionTools.Page(values, LadsJson.Template, offset, maxResults);
            }, ct);
        }

        /// <summary>
        /// Starts a program using a published template.
        /// </summary>
        [McpServerTool(Name = "lads_start_program", ReadOnly = false, Destructive = true, Idempotent = false)]
        [Description("Starts the specified published program template on a functional unit. This sends a device " +
            "command; the returned run ID is assigned by the device and does not imply completion.")]
        public Task<CallToolResult> StartProgramAsync(
            [Description("FunctionalUnit NodeId.")] string functionalUnitNodeId,
            [Description("Template ID from lads_list_program_templates.")] string templateId,
            [Description("Optional supervisory job identifier to include in the request.")]
            string? supervisoryJobId = null,
            [Description("Optional supervisory task identifier to include in the request.")]
            string? supervisoryTaskId = null,
            [Description("Named OPC UA session; omit only when exactly one is connected.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
                (LadsClient client, _) = m_accessor.CreateClient(sessionName);
                LadsProgramRequest request = LadsProgramRequest.For(templateId)
                    .WithJob(supervisoryJobId)
                    .WithTask(supervisoryTaskId);
                string runId = await client.StartProgramAsync(
                    ParseNode(functionalUnitNodeId), request, token).ConfigureAwait(false);
                return new JsonObject { ["accepted"] = true, ["deviceProgramRunId"] = runId };
            }, ct);
        }

        /// <summary>
        /// Sends a LADS state-machine or function command.
        /// </summary>
        [McpServerTool(Name = "lads_command", ReadOnly = false, Destructive = true, Idempotent = false)]
        [Description("Sends one explicit LADS device, functional-unit, control-function, cover, or maintenance " +
            "state-machine command. Valid command names are listed in the tool description. Server authorization " +
            "and state-machine preconditions still apply. Device commands: goto-operate, goto-shutdown, goto-sleep, " +
            "goto-processing, goto-maintenance, goto-setup. Functional-unit commands: stop, abort, clear, hold, " +
            "unhold, suspend, unsuspend, reset, complete, start. Function commands: start, start-target, stop, " +
            "abort, modify-target-by, reset-totalizer, open, close, lock, unlock, reset-cover. Supply targetValue " +
            "for start-target, modify-target-by and reset-totalizer.")]
        public Task<CallToolResult> CommandAsync(
            [Description("Command target NodeId.")] string nodeId,
            [Description("Target kind: device, functionalUnit, or function.")] string targetKind,
            [Description("Explicit command name from the applicable command list.")] string command,
            [Description("Finite numeric target for commands that require it.")] double? targetValue = null,
            [Description("Named OPC UA session; omit only when exactly one is connected.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (LadsClient client, _) = m_accessor.CreateClient(sessionName);
                NodeId target = ParseNode(nodeId);
                await ExecuteCommandAsync(client, target, targetKind, command, targetValue, token).ConfigureAwait(false);
                return new JsonObject { ["accepted"] = true, ["targetNodeId"] = target.ToString(), ["command"] = command };
            }, ct);
        }

        /// <summary>
        /// Lists results of one functional unit.
        /// </summary>
        [McpServerTool(Name = "lads_read_results", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads a live page of the functional unit's results, including metadata and file references.")]
        public Task<CallToolResult> ReadResultsAsync(
            [Description("FunctionalUnit NodeId.")] string functionalUnitNodeId,
            [Description("Zero-based live result offset, 0..1000000.")] int offset = 0,
            [Description("Maximum returned items per page, 1..500.")] int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (LadsClient client, _) = m_accessor.CreateClient(sessionName);
                ArrayOf<LadsResult> values = await client.GetResultsAsync(
                    ParseNode(functionalUnitNodeId), token).ConfigureAwait(false);
                return McpCompanionTools.Page(values, LadsJson.Result, offset, maxResults);
            }, ct);
        }

        /// <summary>
        /// Reads the files published by one result.
        /// </summary>
        [McpServerTool(Name = "lads_list_result_files", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Lists result-file metadata and download capability without fetching file contents.")]
        public Task<CallToolResult> ListResultFilesAsync(
            [Description("Result NodeId from lads_read_results.")] string resultNodeId,
            [Description("Zero-based live result offset, 0..1000000.")] int offset = 0,
            [Description("Maximum returned items per page, 1..500.")] int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (LadsClient client, _) = m_accessor.CreateClient(sessionName);
                ArrayOf<LadsResultFile> values = await client.GetResultFilesAsync(ParseNode(resultNodeId), token)
                    .ConfigureAwait(false);
                return McpCompanionTools.Page(values, LadsJson.ResultFile, offset, maxResults);
            }, ct);
        }

        /// <summary>
        /// Downloads a result file beneath the host's configured transfer root.
        /// </summary>
        [McpServerTool(Name = "lads_download_result_file", ReadOnly = false, Destructive = false, Idempotent = true)]
        [Description("Downloads one result file through its OPC UA File object into the host-configured " +
            "OPCUA_MCP_TRANSFER_ROOT. The destination must be beneath that root, its directory must exist, " +
            "the destination must not exist, and the host byte limit applies.")]
        public Task<CallToolResult> DownloadResultFileAsync(
            [Description("Result NodeId from lads_read_results.")] string resultNodeId,
            [Description("Result-file NodeId from lads_list_result_files.")] string resultFileNodeId,
            [Description("New path beneath the host's configured transfer root.")] string destination,
            [Description("Named OPC UA session; omit only when exactly one is connected.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                NodeId resultId = ParseNode(resultNodeId);
                NodeId fileId = ParseNode(resultFileNodeId);
                (LadsClient client, _) = m_accessor.CreateClient(sessionName);
                ArrayOf<LadsResultFile> files = await client.GetResultFilesAsync(resultId, token)
                    .ConfigureAwait(false);
                LadsResultFile? selected = null;
                for (int index = 0; index < files.Count; index++)
                {
                    if (files[index].NodeId == fileId)
                    {
                        selected = files[index];
                        break;
                    }
                }
                if (selected is null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotFound, "The result does not contain the selected result file.");
                }
                JsonObject result = await m_transfers.DownloadAsync(
                    destination,
                    async (stream, cancellation) =>
                    {
                        _ = await client.DownloadResultFileAsync(selected, stream, cancellation).ConfigureAwait(false);
                    },
                    token).ConfigureAwait(false);
                result["resultNodeId"] = resultId.ToString();
                result["resultFileNodeId"] = fileId.ToString();
                return result;
            }, ct);
        }

        /// <summary>
        /// Reads maintenance tasks on one device.
        /// </summary>
        [McpServerTool(Name = "lads_read_maintenance_tasks", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads a live page of LADS maintenance tasks and their AMB MaintenanceState.")]
        public Task<CallToolResult> ReadMaintenanceTasksAsync(
            [Description("Device NodeId returned by lads_list_devices.")] string deviceNodeId,
            [Description("Zero-based live result offset, 0..1000000.")] int offset = 0,
            [Description("Maximum returned items per page, 1..500.")] int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (LadsClient client, _) = m_accessor.CreateClient(sessionName);
                ArrayOf<LadsMaintenanceTask> values = await client.GetMaintenanceTasksAsync(
                    ParseNode(deviceNodeId), token).ConfigureAwait(false);
                return McpCompanionTools.Page(values, LadsJson.Maintenance, offset, maxResults);
            }, ct);
        }

        /// <summary>
        /// Starts, stops or resets a maintenance task.
        /// </summary>
        [McpServerTool(Name = "lads_maintenance_command", ReadOnly = false, Destructive = true, Idempotent = false)]
        [Description("Sends an explicit maintenance task command: start, stop, or reset. Stop requires a result " +
            "from the LADS MaintenanceTaskResultEnum. Server authorization and state-machine preconditions apply.")]
        public Task<CallToolResult> MaintenanceCommandAsync(
            [Description("Maintenance task NodeId from lads_read_maintenance_tasks.")] string taskNodeId,
            [Description("Command: start, stop, or reset.")] string command,
            [Description("Required result for stop; for example Success or Failure.")] string? result = null,
            [Description("Optional operator comment recorded when stopping the task.")]
            string? comment = null,
            [Description("Named OPC UA session; omit only when exactly one is connected.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                NodeId task = ParseNode(taskNodeId);
                (LadsClient client, _) = m_accessor.CreateClient(sessionName);
                switch (command)
                {
                    case "start":
                        await client.StartMaintenanceTaskAsync(task, token).ConfigureAwait(false);
                        break;
                    case "stop":
                        if (!Enum.TryParse(result, ignoreCase: true, out MaintenanceTaskResultEnum outcome) ||
                            !Enum.IsDefined(outcome))
                        {
                            throw new ArgumentException("stop requires a defined maintenance task result.", nameof(result));
                        }
                        await client.StopMaintenanceTaskAsync(task, outcome, comment, token).ConfigureAwait(false);
                        break;
                    case "reset":
                        if (result is not null || comment is not null)
                        {
                            throw new ArgumentException("reset does not accept a result or comment.");
                        }
                        await client.ResetMaintenanceTaskAsync(task, token).ConfigureAwait(false);
                        break;
                    default:
                        throw new ArgumentException("The command must be start, stop, or reset.", nameof(command));
                }
                return new JsonObject { ["accepted"] = true, ["taskNodeId"] = task.ToString(), ["command"] = command };
            }, ct);
        }

        private static async ValueTask ExecuteCommandAsync(
            LadsClient client, NodeId target, string targetKind, string command, double? targetValue,
            CancellationToken cancellationToken)
        {
            if (targetValue is { } number && !double.IsFinite(number))
            {
                throw new ArgumentOutOfRangeException(nameof(targetValue), "The target value must be finite.");
            }
            if (targetKind == "device")
            {
                RequireNoTargetValue(targetValue);
                switch (command)
                {
                    case "goto-operate": await client.GotoOperateAsync(target, cancellationToken).ConfigureAwait(false); return;
                    case "goto-shutdown": await client.GotoShutdownAsync(target, cancellationToken).ConfigureAwait(false); return;
                    case "goto-sleep": await client.GotoSleepAsync(target, cancellationToken).ConfigureAwait(false); return;
                    case "goto-processing": await client.GotoProcessingAsync(target, cancellationToken).ConfigureAwait(false); return;
                    case "goto-maintenance": await client.GotoMaintenanceAsync(target, cancellationToken).ConfigureAwait(false); return;
                    case "goto-setup": await client.GotoSetupAsync(target, cancellationToken).ConfigureAwait(false); return;
                }
            }
            else if (targetKind == "functionalUnit")
            {
                RequireNoTargetValue(targetValue);
                switch (command)
                {
                    case "stop": await client.StopAsync(target, cancellationToken).ConfigureAwait(false); return;
                    case "abort": await client.AbortAsync(target, cancellationToken).ConfigureAwait(false); return;
                    case "clear": await client.ClearAsync(target, cancellationToken).ConfigureAwait(false); return;
                    case "hold": await client.HoldAsync(target, cancellationToken).ConfigureAwait(false); return;
                    case "unhold": await client.UnholdAsync(target, cancellationToken).ConfigureAwait(false); return;
                    case "suspend": await client.SuspendAsync(target, cancellationToken).ConfigureAwait(false); return;
                    case "unsuspend": await client.UnsuspendAsync(target, cancellationToken).ConfigureAwait(false); return;
                    case "reset": await client.ResetAsync(target, cancellationToken).ConfigureAwait(false); return;
                    case "complete": await client.ToCompleteAsync(target, cancellationToken).ConfigureAwait(false); return;
                    case "start": await client.StartAsync(target, cancellationToken: cancellationToken).ConfigureAwait(false); return;
                }
            }
            else if (targetKind == "function")
            {
                switch (command)
                {
                    case "start":
                        RequireNoTargetValue(targetValue);
                        await client.StartFunctionAsync(target, cancellationToken).ConfigureAwait(false);
                        return;
                    case "start-target":
                        await client.StartFunctionWithTargetValueAsync(target, TargetVariant(targetValue), cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    case "stop":
                        RequireNoTargetValue(targetValue);
                        await client.StopFunctionAsync(target, cancellationToken).ConfigureAwait(false);
                        return;
                    case "abort":
                        RequireNoTargetValue(targetValue);
                        await client.AbortFunctionAsync(target, cancellationToken).ConfigureAwait(false);
                        return;
                    case "modify-target-by":
                        await client.ModifyTargetValueByAsync(target, RequireTargetValue(targetValue), cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    case "reset-totalizer":
                        await client.ResetTotalizerAsync(target, targetValue ?? 0, cancellationToken).ConfigureAwait(false);
                        return;
                    case "open":
                        RequireNoTargetValue(targetValue);
                        await client.OpenCoverAsync(target, cancellationToken).ConfigureAwait(false);
                        return;
                    case "close":
                        RequireNoTargetValue(targetValue);
                        await client.CloseCoverAsync(target, cancellationToken).ConfigureAwait(false);
                        return;
                    case "lock":
                        RequireNoTargetValue(targetValue);
                        await client.LockCoverAsync(target, cancellationToken).ConfigureAwait(false);
                        return;
                    case "unlock":
                        RequireNoTargetValue(targetValue);
                        await client.UnlockCoverAsync(target, cancellationToken).ConfigureAwait(false);
                        return;
                    case "reset-cover":
                        RequireNoTargetValue(targetValue);
                        await client.ResetCoverAsync(target, cancellationToken).ConfigureAwait(false);
                        return;
                }
            }
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, "Command '{0}' is not valid for target kind '{1}'.",
                    command, targetKind));
        }

        private static double RequireTargetValue(double? value)
        {
            return value is { } number && double.IsFinite(number)
                ? number
                : throw new ArgumentException("This command requires a finite targetValue.", nameof(value));
        }

        private static Variant TargetVariant(double? value)
        {
            return Variant.From(RequireTargetValue(value));
        }

        private static void RequireNoTargetValue(double? value)
        {
            if (value is not null)
            {
                throw new ArgumentException("This command does not accept targetValue.", nameof(value));
            }
        }

        /// <summary>
        /// Parses a non-null OPC UA NodeId.
        /// </summary>
        private static NodeId ParseNode(string nodeId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
            NodeId parsed = NodeId.Parse(nodeId);
            return parsed.IsNull
                ? throw new ArgumentException("A non-null NodeId is required.", nameof(nodeId))
                : parsed;
        }

        private readonly LadsClientAccessor m_accessor;
        private readonly McpFileTransfers m_transfers;
    }
}
