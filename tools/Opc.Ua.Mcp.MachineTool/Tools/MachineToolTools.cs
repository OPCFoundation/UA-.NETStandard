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
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Bounded passive MachineTool workflows, including independent wireless-peripheral discovery.
    /// </summary>
    [McpServerToolType]
    public sealed class MachineToolTools
    {
        /// <summary>
        /// Discovers actual machine and peripheral instances using the typed subtype-aware inventory.
        /// </summary>
        [McpServerTool(Name = "machinetool_list_devices", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Returns MachineTool and WMTP identities, including vendor subtypes, under Machines/DeviceSet. " +
            "Works on WMTP-only endpoints; namespaces do not prove capabilities. Overflow fails. No device changes.")]
        public static Task<CallToolResult> ListDevicesAsync(
            MachineToolClientAccessor accessor,
            [Description("Complete device limit, 1-100; overflow fails.")]
            [Range(1, 100)] int maximumResults = 50,
            [Description("Whole UTF-8 reply limit, 1024-1048576 bytes; overflow fails.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Named connection; omit only with one connected session.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return MachineToolJson.ExecuteAsync(async () =>
            {
                MachineToolParameters.Bound(maximumResults, 1, 100);
                MachineToolParameters.ResponseLimit(maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient client = accessor.CreateClient(sessionName);
                await using var lifetime = client.ConfigureAwait(false);
                ArrayOf<MachineToolDeviceEntry> entries = await client.DiscoverAsync(maximumResults, ct)
                    .ConfigureAwait(false);
                return MachineToolJson.Result(new JsonObject
                {
                    ["machineToolModelPresent"] = (client.AvailableParts & MachineToolParts.MachineTool) != 0,
                    ["wmtpModelPresent"] = (client.AvailableParts & MachineToolParts.Wmtp) != 0,
                    ["devices"] = MachineToolJson.Array(entries, MachineToolJson.Entry)
                }, maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Reads mandatory monitoring and the actual optional per-instance capability declaration.
        /// </summary>
        [McpServerTool(Name = "machinetool_read_machine", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Returns operation/active-program DataValues, instance capabilities and actual Jobs/file " +
            "root IDs. Preserves quality, timestamps and absent declarations. Read-only; no commands.")]
        public static Task<CallToolResult> ReadMachineAsync(
            MachineToolClientAccessor accessor,
            [Description("MachineTool instance NodeId from discovery; subtypes supported.")]
            [StringLength(1024, MinimumLength = 1)] string machineNodeId,
            [Description("Whole UTF-8 reply limit, 1024-1048576 bytes; overflow fails.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Named connection; omit only with one connected session.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return MachineToolJson.ExecuteAsync(async () =>
            {
                NodeId node = MachineToolParameters.Node(machineNodeId);
                MachineToolParameters.ResponseLimit(maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient client = accessor.CreateClient(sessionName);
                await using var lifetime = client.ConfigureAwait(false);
                MachineToolReadSnapshot snapshot = await client.ReadSnapshotAsync(node, ct).ConfigureAwait(false);
                MachineToolCapabilityReadSnapshot capabilities = await client.GetCapabilitiesAsync(node, ct)
                    .ConfigureAwait(false);
                Machinery.Jobs.JobManagementTypeClient? jobs = await client.GetJobManagementAsync(node, ct)
                    .ConfigureAwait(false);
                FileDirectoryTypeClient? files = await client.GetFileSystemAsync(node, ct).ConfigureAwait(false);
                FileDirectoryTypeClient? masters = await client.GetWorkMastersAsync(node, ct).ConfigureAwait(false);
                return MachineToolJson.Result(new JsonObject
                {
                    ["nodeId"] = node.ToString(),
                    ["monitoring"] = MachineToolJson.Readings(client,
                        ("operationMode", snapshot.OperationMode), ("feedOverride", snapshot.FeedOverride),
                        ("isWarmUp", snapshot.IsWarmUp), ("activeProgramName", snapshot.ActiveProgramName),
                        ("activeProgramState", snapshot.ActiveProgramState)),
                    ["capabilities"] = MachineToolJson.Capabilities(client, capabilities),
                    ["jobManagementNodeId"] = jobs?.ObjectId.ToString(),
                    ["fileSystemNodeId"] = files?.ObjectId.ToString(),
                    ["workMastersNodeId"] = masters?.ObjectId.ToString()
                }, maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Reads the bounded tool, multitool and independent tool-life inventories.
        /// </summary>
        [McpServerTool(Name = "machinetool_read_equipment", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Returns bounded tools/multitools, locations, controller IDs, locks and independent life " +
            "values. Preserves identities, numeric types, quality, time and optional absence. No writes or actuation.")]
        public static Task<CallToolResult> ReadEquipmentAsync(
            MachineToolClientAccessor accessor,
            [Description("MachineTool instance NodeId, not a tool or life variable.")]
            [StringLength(1024, MinimumLength = 1)] string machineNodeId,
            [Description("Complete tool limit, 1-1024, including multitool children.")]
            [Range(1, 1024)] int maximumTools = 128,
            [Description("Complete independent life-entry limit, 1-4096.")]
            [Range(1, 4096)] int maximumLifeEntries = 512,
            [Description("Whole UTF-8 reply limit, 1024-1048576 bytes; overflow fails.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Named connection; omit only with one connected session.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return MachineToolJson.ExecuteAsync(async () =>
            {
                NodeId node = MachineToolParameters.Node(machineNodeId);
                MachineToolParameters.Bound(maximumTools, 1, 1024);
                MachineToolParameters.Bound(maximumLifeEntries, 1, 4096);
                MachineToolParameters.ResponseLimit(maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient client = accessor.CreateClient(sessionName);
                await using var lifetime = client.ConfigureAwait(false);
                MachineToolEquipmentReadSnapshot snapshot = await client.ReadEquipmentAsync(
                    node, maximumTools, maximumLifeEntries, ct).ConfigureAwait(false);
                return MachineToolJson.Result(MachineToolJson.Equipment(client, snapshot), maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Reads retained jobs, programs, parts, state transitions and counters without executing jobs.
        /// </summary>
        [McpServerTool(Name = "machinetool_read_production", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Returns retained jobs/programs/parts, state/LastTransition and distinct run/lifetime counters. " +
            "Monitoring only, not ISA95 commands or complete history. Whole-graph bounds fail without truncation.")]
        public static Task<CallToolResult> ReadProductionAsync(
            MachineToolClientAccessor accessor,
            [Description("MachineTool instance NodeId; retained production is read-only.")]
            [StringLength(1024, MinimumLength = 1)] string machineNodeId,
            [Description("Whole production-element limit, 1-4096, including nested parts.")]
            [Range(1, 4096)] int maximumElements = 256,
            [Description("Whole browse-reference limit, 1-16384, including vendor members.")]
            [Range(1, 16384)] int maximumReferences = 4096,
            [Description("Decoded payload and UTF-8 reply limit, 1024-1048576 bytes.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Named connection; omit only with one connected session.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return MachineToolJson.ExecuteAsync(async () =>
            {
                NodeId node = MachineToolParameters.Node(machineNodeId);
                MachineToolParameters.Bound(maximumElements, 1, 4096);
                MachineToolParameters.Bound(maximumReferences, 1, 16384);
                MachineToolParameters.ResponseLimit(maximumResponseBytes);
                var options = new MachineToolProductionReadOptions
                {
                    MaximumElements = maximumElements,
                    MaximumReferences = maximumReferences,
                    MaximumPayloadBytes = maximumResponseBytes
                };
                options.Validate();
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient client = accessor.CreateClient(sessionName);
                await using var lifetime = client.ConfigureAwait(false);
                MachineToolProductionReadSnapshot snapshot = await client.ReadProductionAsync(node, options, ct)
                    .ConfigureAwait(false);
                return MachineToolJson.Result(MachineToolJson.Production(client, snapshot), maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Reads forecasts without inventing predictions or crossing machine-instance boundaries.
        /// </summary>
        [McpServerTool(Name = "machinetool_read_prognoses", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Returns actual forecasts and affected-entity IDs for maintenance, activities, parts, jobs, " +
            "tools and utilities. Retains subtype, quality, time and absence. No estimates or equipment actions.")]
        public static Task<CallToolResult> ReadPrognosesAsync(
            MachineToolClientAccessor accessor,
            [Description("MachineTool instance NodeId owning the forecast inventory.")]
            [StringLength(1024, MinimumLength = 1)] string machineNodeId,
            [Description("Complete forecast limit, 1-128; overflow fails.")]
            [Range(1, 128)] int maximumPrognoses = 64,
            [Description("Whole UTF-8 reply limit, 1024-1048576 bytes; overflow fails.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Named connection; omit only with one connected session.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return MachineToolJson.ExecuteAsync(async () =>
            {
                NodeId node = MachineToolParameters.Node(machineNodeId);
                MachineToolParameters.Bound(maximumPrognoses, 1, 128);
                MachineToolParameters.ResponseLimit(maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient client = accessor.CreateClient(sessionName);
                await using var lifetime = client.ConfigureAwait(false);
                MachineToolPrognosisReadSnapshot snapshot = await client.ReadPrognosesAsync(node, maximumPrognoses, ct)
                    .ConfigureAwait(false);
                return MachineToolJson.Result(MachineToolJson.Prognoses(client, snapshot), maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Observes selected variables through the facade's owned bounded subscription.
        /// </summary>
        [McpServerTool(Name = "machinetool_observe_values", ReadOnly = true, Destructive = false,
            Idempotent = false, OpenWorld = true)]
        [Description("Returns bounded original DataValues with quality, timestamps, publish order and explicit " +
            "limit/loss indicators. Uses and closes one private subscription. No writes or complete-history claim.")]
        public static Task<CallToolResult> ObserveValuesAsync(
            MachineToolClientAccessor accessor,
            [Description("Native array of 1-256 distinct, non-null variable NodeIds.")]
            JsonElement nodeIds,
            [Description("Observation including setup: 1-60 seconds.")]
            [Range(1, 60)] int durationSeconds = 5,
            [Description("Retained notifications: 1-100; limit ends the call.")]
            [Range(1, 100)] int maximumNotifications = 50,
            [Description("Whole UTF-8 reply limit, 1024-1048576 bytes; decoded cap also 1 MiB.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Named connection; omit only with one connected session.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return MachineToolJson.ExecuteAsync(async () =>
            {
                ArrayOf<NodeId> nodes = MachineToolParameters.Nodes(nodeIds);
                MachineToolObservationOptions options = MachineToolParameters.Observation(
                    durationSeconds, maximumNotifications, maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient client = accessor.CreateClient(sessionName);
                await using var lifetime = client.ConfigureAwait(false);
                MachineToolDataObservation observation = await client.ObserveValuesAsync(nodes, options, ct)
                    .ConfigureAwait(false);
                return MachineToolJson.Result(MachineToolJson.Observation(client, observation),
                    maximumResponseBytes, isError: observation.DroppedNotifications != 0);
            }, ct);
        }

        /// <summary>
        /// Observes the mandatory monitoring and active program without discovering variable paths manually.
        /// </summary>
        [McpServerTool(Name = "machinetool_observe_machine", ReadOnly = true, Destructive = false,
            Idempotent = false, OpenWorld = true)]
        [Description("Returns operation/active-program DataValues and publish order with explicit limits/losses. " +
            "Closes its private subscription on every path. No commands, acknowledgment or persistent handles.")]
        public static Task<CallToolResult> ObserveMachineAsync(
            MachineToolClientAccessor accessor,
            [Description("MachineTool instance NodeId, not a variable or peripheral.")]
            [StringLength(1024, MinimumLength = 1)] string machineNodeId,
            [Description("Observation including setup: 1-60 seconds.")]
            [Range(1, 60)] int durationSeconds = 5,
            [Description("Retained notifications: 1-100; limit ends the call.")]
            [Range(1, 100)] int maximumNotifications = 50,
            [Description("Whole UTF-8 reply limit, 1024-1048576 bytes; decoded cap also 1 MiB.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Named connection; omit only with one connected session.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return MachineToolJson.ExecuteAsync(async () =>
            {
                NodeId node = MachineToolParameters.Node(machineNodeId);
                MachineToolObservationOptions options = MachineToolParameters.Observation(
                    durationSeconds, maximumNotifications, maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient client = accessor.CreateClient(sessionName);
                await using var lifetime = client.ConfigureAwait(false);
                MachineToolDataObservation observation = await client.ObserveDataAsync(node, options, ct)
                    .ConfigureAwait(false);
                return MachineToolJson.Result(MachineToolJson.Observation(client, observation),
                    maximumResponseBytes, isError: observation.DroppedNotifications != 0);
            }, ct);
        }

        /// <summary>
        /// Observes passive events or explicitly requests a bounded, non-consuming retained-condition refresh.
        /// </summary>
        [McpServerTool(Name = "machinetool_observe_events", ReadOnly = true, Destructive = false,
            Idempotent = false, OpenWorld = true)]
        [Description("Returns qualified event fields or retained conditions with publish order and refresh/loss " +
            "status. Retained mode awaits RefreshEnd. No acknowledge, enable or control; its subscription closes.")]
        public static Task<CallToolResult> ObserveEventsAsync(
            MachineToolClientAccessor accessor,
            [Description("Actual machine event-notifier NodeId; server permissions apply.")]
            [StringLength(1024, MinimumLength = 1)] string notifierNodeId,
            [Description("Request retained ConditionRefresh instead of new events; never acknowledge.")]
            bool retainedConditions = false,
            [Description("Observation including setup/refresh: 1-60 seconds.")]
            [Range(1, 60)] int durationSeconds = 5,
            [Description("Retained notifications: 1-100; incomplete refresh is explicit.")]
            [Range(1, 100)] int maximumNotifications = 50,
            [Description("Whole UTF-8 reply limit, 1024-1048576 bytes; decoded cap also 1 MiB.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Named connection; omit only with one connected session.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return MachineToolJson.ExecuteAsync(async () =>
            {
                NodeId node = MachineToolParameters.Node(notifierNodeId);
                MachineToolObservationOptions options = MachineToolParameters.Observation(
                    durationSeconds, maximumNotifications, maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient client = accessor.CreateClient(sessionName);
                await using var lifetime = client.ConfigureAwait(false);
                MachineToolEventObservation observation = retainedConditions
                    ? await client.ObserveConditionsAsync(node, options, ct).ConfigureAwait(false)
                    : await client.ObserveEventsAsync(node, options, ct).ConfigureAwait(false);
                return MachineToolJson.Result(MachineToolJson.Events(client, observation), maximumResponseBytes,
                    isError: observation.DroppedNotifications != 0 ||
                        (retainedConditions && !observation.RefreshCompleted));
            }, ct);
        }
    }
}
