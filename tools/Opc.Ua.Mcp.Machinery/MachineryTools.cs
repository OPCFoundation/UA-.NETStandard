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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Di;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Mcp.Serialization;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Nineteen Machinery tools with per-invocation session resolution and explicit control boundaries.
    /// </summary>
    [McpServerToolType]
    public sealed class MachineryTools
    {
        /// <summary>
        /// Creates tools using the shared session manager and host-owned file policy.
        /// </summary>
        public MachineryTools(OpcUaSessionManager sessionManager, McpFileTransfers transfers)
        {
            m_sessions = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            m_transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
        }

        /// <summary>
        /// Lists machines in the standard Machines folder.
        /// </summary>
        [McpServerTool(Name = "machinery_list_machines", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Lists OPC 40001 machines from Machines, not DeviceSet. Returns a live page of actual node IDs.")]
        public Task<CallToolResult> ListMachinesAsync(
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
                await McpCompanionTools.PageAsync(
                    CreateClient(sessionName).EnumerateMachinesAsync(token),
                    MachineryJson.Entry, offset, maxResults, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Lists a machine's components.
        /// </summary>
        [McpServerTool(Name = "machinery_list_components", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Lists a machine's Components add-in as a live page. An absent add-in returns an empty page.")]
        public Task<CallToolResult> ListComponentsAsync(
            [Description("Local machine NodeId returned by machinery_list_machines.")]
            string machineNodeId,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
                await McpCompanionTools.PageAsync(
                    CreateClient(sessionName).EnumerateComponentsAsync(MachineryJson.Node(machineNodeId), token),
                    MachineryJson.Entry, offset, maxResults, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Lists advertised building blocks without assuming a machine type.
        /// </summary>
        [McpServerTool(Name = "machinery_list_building_blocks",
            ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Lists actual objects organized by MachineryBuildingBlocks for a machine or component.")]
        public Task<CallToolResult> ListBuildingBlocksAsync(
            [Description("Machinery machine or component NodeId returned by discovery.")]
            string itemNodeId,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
                await McpCompanionTools.PageAsync(
                    CreateClient(sessionName).EnumerateBuildingBlocksAsync(MachineryJson.Node(itemNodeId), token),
                    MachineryJson.Entry, offset, maxResults, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Reads one explicitly supported machinery-item facet.
        /// </summary>
        [McpServerTool(Name = "machinery_read", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads Identification, Health, OperationCounters, ItemState or OperationMode. " +
            "State and mode are server-driven: this tool never changes them. Missing optional blocks are null.")]
        public Task<CallToolResult> ReadAsync(
            [Description("Machinery machine or component NodeId returned by discovery.")]
            string itemNodeId,
            MachineryReadFacet facet,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                MachineryClient client = CreateClient(sessionName);
                NodeId item = MachineryJson.Node(itemNodeId);
                switch (facet)
                {
                    case MachineryReadFacet.Identification:
                        return MachineryJson.Identification(
                            await client.ReadIdentificationAsync(item, token).ConfigureAwait(false));
                    case MachineryReadFacet.Health:
                        DeviceHealthEnumeration? health = await client.ReadDeviceHealthAsync(item, token)
                            .ConfigureAwait(false);
                        return new JsonObject
                        {
                            ["deviceHealth"] = health.HasValue ? (int)health.Value : null,
                            ["name"] = health?.ToString()
                        };
                    case MachineryReadFacet.OperationCounters:
                        return MachineryJson.Counters(
                            await client.ReadOperationCountersAsync(item, token).ConfigureAwait(false),
                            client.Session.MessageContext);
                    case MachineryReadFacet.ItemState:
                        return MachineryJson.State(
                            await client.GetItemStateAsync(item, token).ConfigureAwait(false));
                    case MachineryReadFacet.OperationMode:
                        return MachineryJson.State(
                            await client.GetOperationModeAsync(item, token).ConfigureAwait(false));
                    default:
                        throw new ArgumentOutOfRangeException(nameof(facet));
                }
            }, ct);
        }

        /// <summary>
        /// Lists lifetime counters and their thresholds.
        /// </summary>
        [McpServerTool(Name = "machinery_list_lifetime_counters",
            ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads a live page of remaining-life, start, limit and warning values for a machinery item.")]
        public Task<CallToolResult> ListLifetimeCountersAsync(
            [Description("Machinery machine or component NodeId returned by discovery.")]
            string itemNodeId,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
                await McpCompanionTools.PageAsync(
                    CreateClient(sessionName).ReadLifetimeCountersAsync(MachineryJson.Node(itemNodeId), token),
                    MachineryJson.Lifetime, offset, maxResults, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Lists equipment with its identity and remaining life.
        /// </summary>
        [McpServerTool(Name = "machinery_list_equipment", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads a live page of MachineryEquipment items, type IDs, serial numbers and EquipmentLife.")]
        public Task<CallToolResult> ListEquipmentAsync(
            [Description("Local machine NodeId returned by machinery_list_machines.")]
            string machineNodeId,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
                await McpCompanionTools.PageAsync(
                    CreateClient(sessionName).EnumerateEquipmentAsync(MachineryJson.Node(machineNodeId), token),
                    MachineryJson.Equipment, offset, maxResults, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Discovers process values on an item and its Monitoring add-in.
        /// </summary>
        [McpServerTool(Name = "machinery_list_process_values",
            ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Finds OPC 40001-2 process values, including vendor subtypes, " +
            "on an item or its Monitoring block.")]
        public Task<CallToolResult> ListProcessValuesAsync(
            [Description("Machinery machine or component NodeId returned by discovery.")]
            string itemNodeId,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
                await McpCompanionTools.PageAsync(
                    CreateClient(sessionName).EnumerateProcessValuesAsync(MachineryJson.Node(itemNodeId), token),
                    MachineryJson.Entry, offset, maxResults, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Reads a process value and its published metadata.
        /// </summary>
        [McpServerTool(Name = "machinery_read_process_value",
            ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads a process value, setpoint, limits, status, alarm suppression, units and range. " +
            "Non-finite values remain explicit UA JSON strings; absent optional fields remain null.")]
        public Task<CallToolResult> ReadProcessValueAsync(
            [Description("Actual ProcessValue NodeId returned by machinery_list_process_values.")]
            string processValueNodeId,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                MachineryClient client = CreateClient(sessionName);
                return MachineryJson.ProcessValue(
                    await client.ReadProcessValueAsync(MachineryJson.Node(processValueNodeId), token)
                        .ConfigureAwait(false),
                    client.Session.MessageContext);
            }, ct);
        }

        /// <summary>
        /// Explicitly invokes a process value's zero-point adjustment.
        /// </summary>
        [McpServerTool(Name = "machinery_zero_point_adjustment", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: invokes ZeroPointAdjustment on a discovered process value. " +
            "This changes calibration; call only when the operator intends it. Preserves the server status code.")]
        public Task<CallToolResult> ZeroPointAdjustmentAsync(
            [Description("Actual ProcessValue NodeId returned by machinery_list_process_values.")]
            string processValueNodeId,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                StatusCode status = await CreateClient(sessionName).ZeroPointAdjustmentAsync(
                    MachineryJson.Node(processValueNodeId), token).ConfigureAwait(false);
                return new JsonObject
                {
                    ["error"] = !StatusCode.IsGood(status),
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(status),
                    ["statusCodeValue"] = status.Code
                };
            }, ct);
        }

        /// <summary>
        /// Lists one level of the Machinery energy hierarchy.
        /// </summary>
        [McpServerTool(Name = "machinery_list_energy", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Lists Resources under an item, MeteringPoints under a resource, " +
            "or SubMeters linked by Contains. " +
            "Use the discovered IDs for the next level. Results are live pages.")]
        public Task<CallToolResult> ListEnergyAsync(
            [Description("Local NodeId matching the selected operation; use a discovery result.")]
            string nodeId,
            MachineryEnergyScope scope,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                MachineryClient client = CreateClient(sessionName);
                NodeId node = MachineryJson.Node(nodeId);
                return await McpCompanionTools.PageAsync(scope switch
                {
                    MachineryEnergyScope.Resources => client.EnumerateEnergyResourcesAsync(node, token),
                    MachineryEnergyScope.MeteringPoints => client.EnumerateMeteringPointsAsync(node, token),
                    MachineryEnergyScope.SubMeters => client.EnumerateSubMetersAsync(node, token),
                    _ => throw new ArgumentOutOfRangeException(nameof(scope))
                }, MachineryJson.Entry, offset, maxResults, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads a discovered metering point or a resource's Main point.
        /// </summary>
        [McpServerTool(Name = "machinery_read_meter", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads a metering point's application tag and typed measurement values. " +
            "Set mainOfResource=true only when nodeId identifies a resource folder rather than a meter.")]
        public Task<CallToolResult> ReadMeterAsync(
            [Description("Local NodeId matching the selected operation; use a discovery result.")]
            string nodeId,
            [Description("Treat nodeId as an energy resource and read its main metering point.")]
            bool mainOfResource = false,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                MachineryClient client = CreateClient(sessionName);
                NodeId node = MachineryJson.Node(nodeId);
                MachineryMeteringPoint? point = mainOfResource
                    ? await client.ReadMainMeteringPointAsync(node, token).ConfigureAwait(false)
                    : await client.ReadMeteringPointAsync(node, token).ConfigureAwait(false);
                return MachineryJson.Meter(point, client.Session.MessageContext);
            }, ct);
        }

        /// <summary>
        /// Lists orders or responses through the published Machinery variables.
        /// </summary>
        [McpServerTool(Name = "machinery_list_jobs", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads a live page of published Orders with state or Responses. " +
            "Preserves complete ISA-95 structures and typed parameter values.")]
        public Task<CallToolResult> ListJobsAsync(
            [Description("Local machine NodeId returned by machinery_list_machines.")]
            string machineNodeId,
            MachineryJobList list,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                MachineryClient client = CreateClient(sessionName);
                NodeId machine = MachineryJson.Node(machineNodeId);
                IServiceMessageContext context = client.Session.MessageContext;
                return list switch
                {
                    MachineryJobList.Orders => McpCompanionTools.Page(
                        await client.ReadJobOrdersAsync(machine, token).ConfigureAwait(false),
                        value => McpCompanionJson.Encode(value, context), offset, maxResults),
                    MachineryJobList.Responses => McpCompanionTools.Page(
                        await client.ReadJobResponsesAsync(machine, token).ConfigureAwait(false),
                        value => McpCompanionJson.Encode(value, context), offset, maxResults),
                    _ => throw new ArgumentOutOfRangeException(nameof(list))
                };
            }, ct);
        }

        /// <summary>
        /// Reads predefined parameters without discarding unknown typed parameters.
        /// </summary>
        [McpServerTool(Name = "machinery_read_job_parameters",
            ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads typed OPC 40001-3 parameters for an order or its queried response. " +
            "Response queries use only the actual response provider and preserve its UInt64 return status.")]
        public Task<CallToolResult> ReadJobParametersAsync(
            [Description("Local machine NodeId returned by machinery_list_machines.")]
            string machineNodeId,
            [Description("Exact job-order identifier, not a NodeId; e.g. job-1.")]
            string jobOrderId,
            MachineryJobList list = MachineryJobList.Orders,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(jobOrderId);
                MachineryClient client = CreateClient(sessionName);
                NodeId machine = MachineryJson.Node(machineNodeId);
                if (list == MachineryJobList.Orders)
                {
                    return MachineryJson.OrderParameters(
                        await client.ReadJobOrderParametersAsync(machine, jobOrderId, token).ConfigureAwait(false),
                        client.Session.MessageContext);
                }
                if (list != MachineryJobList.Responses)
                {
                    throw new ArgumentOutOfRangeException(nameof(list));
                }
                MachineryJobManagementEndpoints endpoints = await client.GetJobManagementEndpointsAsync(machine, token)
                    .ConfigureAwait(false);
                if (endpoints.JobResponseProviderId.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported, "No job response provider is published.");
                }
                var provider = new V2.ISA95JobResponseProviderObjectTypeClient(
                    client.Session, endpoints.JobResponseProviderId, client.Telemetry);
                (V2.ISA95JobResponseDataType response, ulong status) = await provider
                    .RequestJobResponseByJobOrderIDAsync(jobOrderId, token).ConfigureAwait(false);
                return new JsonObject
                {
                    ["error"] = status != 1UL,
                    ["returnStatus"] = status,
                    ["parameters"] = response is null ? null : MachineryJson.ResponseParameters(
                        MachineryJobResponseParameters.FromJobResponse(response), client.Session.MessageContext)
                };
            }, ct);
        }

        /// <summary>
        /// Returns only real job-management endpoint roles.
        /// </summary>
        [McpServerTool(Name = "machinery_get_job_endpoints",
            ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Resolves JobManagement, the JobOrderControl order receiver " +
            "and optional JobOrderResults provider. " +
            "Missing roles have null NodeIds. Machinery defines no response receiver. " +
            "Use the isa95_v2_* tools with the returned role-specific IDs for explicit job control.")]
        public Task<CallToolResult> GetJobEndpointsAsync(
            [Description("Local machine NodeId returned by machinery_list_machines.")]
            string machineNodeId,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token => MachineryJson.Endpoints(
                await CreateClient(sessionName).GetJobManagementEndpointsAsync(
                    MachineryJson.Node(machineNodeId), token).ConfigureAwait(false)), ct);
        }

        /// <summary>
        /// Queries a bounded, filtered result-ID list and releases the server handle.
        /// </summary>
        [McpServerTool(Name = "machinery_list_results", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Queries result IDs by optional exact job/part IDs and inclusive creation-time bounds, " +
            "ordered ascending by CreationTime, ResultId or JobId. Maximum 500 results. limitReached means the " +
            "query may be incomplete; narrow the filters. This is not a persistent cursor. " +
            "Server handles are released.")]
        public Task<CallToolResult> ListResultsAsync(
            [Description("Local machine NodeId returned by machinery_list_machines.")]
            string machineNodeId,
            [Description("Exact job identifier to filter results; omit for all jobs.")]
            string? jobId = null,
            [Description("Exact part identifier to filter results; omit for all parts.")]
            string? partId = null,
            [Description("Inclusive ISO 8601 lower bound with Z or an explicit offset; omit for no bound.")]
            DateTime? createdAfter = null,
            [Description("Inclusive ISO 8601 upper bound with Z or an explicit offset; omit for no bound.")]
            DateTime? createdBefore = null,
            MachineryResultOrder orderBy = MachineryResultOrder.CreationTime,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResults, 500);
                MachineryClient client = CreateClient(sessionName);
                (ContentFilter filter, ArrayOf<RelativePath> order) = MachineryJson.ResultFilter(
                    client.Session.NamespaceUris, jobId, partId, createdAfter, createdBefore, orderBy);
                ResultManagementTypeClient proxy = await ResultsAsync(client, machineNodeId, token)
                    .ConfigureAwait(false);
                (uint handle, ArrayOf<string> ids, int error) = await proxy.GetResultIdListFilteredAsync(
                    filter, order, (uint)maxResults, 0, token).ConfigureAwait(false);
                try
                {
                    return new JsonObject
                    {
                        ["error"] = error != 0,
                        ["returnCode"] = error,
                        ["resultIds"] = MachineryJson.Array(ids, value => JsonValue.Create(value)),
                        ["limitReached"] = ids.Count >= maxResults,
                        ["consistency"] = "live"
                    };
                }
                finally
                {
                    await ReleaseAsync(proxy, handle).ConfigureAwait(false);
                }
            }, ct);
        }

        /// <summary>
        /// Reads metadata or a complete result, releasing the server handle afterward.
        /// </summary>
        [McpServerTool(Name = "machinery_read_result", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads a result by ID, or the latest result when resultId is omitted. " +
            "metadataOnly defaults to true; false includes typed content. Timeout is bounded to 30 seconds. " +
            "Preserves the model return code and releases the server result handle.")]
        public Task<CallToolResult> ReadResultAsync(
            [Description("Local machine NodeId returned by machinery_list_machines.")]
            string machineNodeId,
            [Description("Exact result identifier returned by machinery_list_results.")]
            string? resultId = null,
            [Description("Return result metadata without its result-content payload.")]
            bool metadataOnly = true,
            [Description("Server-side fetch timeout in milliseconds; e.g. 1000.")]
            int timeoutMs = 1000,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMs, 0);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(timeoutMs, 30_000);
                MachineryClient client = CreateClient(sessionName);
                ResultManagementTypeClient proxy = await ResultsAsync(client, machineNodeId, token)
                    .ConfigureAwait(false);
                if (resultId is not null)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(resultId);
                }
                (uint handle, ResultDataType result, int error) = resultId is null
                    ? await proxy.GetLatestResultAsync(timeoutMs, token).ConfigureAwait(false)
                    : await proxy.GetResultByIdAsync(resultId, timeoutMs, token).ConfigureAwait(false);
                try
                {
                    return new JsonObject
                    {
                        ["error"] = error != 0,
                        ["returnCode"] = error,
                        ["result"] = MachineryJson.Result(result, metadataOnly, client.Session.MessageContext)
                    };
                }
                finally
                {
                    await ReleaseAsync(proxy, handle).ConfigureAwait(false);
                }
            }, ct);
        }

        /// <summary>
        /// Reads published result variables as a live page.
        /// </summary>
        [McpServerTool(Name = "machinery_list_published_results",
            ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads the Results folder's published result variables with full typed content and metadata.")]
        public Task<CallToolResult> ListPublishedResultsAsync(
            [Description("Local machine NodeId returned by machinery_list_machines.")]
            string machineNodeId,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                MachineryClient client = CreateClient(sessionName);
                return await McpCompanionTools.PageAsync(
                    client.ReadPublishedResultsAsync(MachineryJson.Node(machineNodeId), token),
                    value => McpCompanionJson.Encode(value, client.Session.MessageContext),
                    offset, maxResults, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Streams a result into a bounded, host-rooted output file.
        /// </summary>
        [McpServerTool(Name = "machinery_download_result", ReadOnly = false, Destructive = false)]
        [Description("Streams a result via TemporaryFileTransfer into a new file beneath the host TransferRoot. " +
            "Does not overwrite files or buffer the payload. Host byte limits apply. " +
            "Remote result content is read-only; " +
            "this tool creates a local artifact. Failed or cancelled transfers are removed.")]
        public Task<CallToolResult> DownloadResultAsync(
            [Description("Local machine NodeId returned by machinery_list_machines.")]
            string machineNodeId,
            [Description("Exact result identifier returned by machinery_list_results.")]
            string resultId,
            [Description("Transfer-root-relative file name; e.g. result.bin. Never a URL.")]
            string filePath,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(resultId);
                MachineryClient client = CreateClient(sessionName);
                NodeId machine = MachineryJson.Node(machineNodeId);
                return await m_transfers.DownloadAsync(
                    filePath, (destination, transferToken) =>
                        client.DownloadResultAsync(machine, resultId, destination, transferToken), token)
                    .ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Collects a finite window of state transitions or typed events.
        /// </summary>
        [McpServerTool(Name = "machinery_observe", ReadOnly = true, Destructive = false)]
        [Description("Observes ItemState, OperationMode, Results or Notifications for a machine/item, " +
            "or ZeroPointAdjustments for a process value. Maximum 30 seconds and 500 items. " +
            "Reports why collection stopped, disposes monitored-item enumerators, and never changes state or mode.")]
        public Task<CallToolResult> ObserveAsync(
            [Description("Local NodeId matching the selected operation; use a discovery result.")]
            string nodeId,
            MachineryObservationKind kind,
            [Description("Observation window in milliseconds, 1..30000.")]
            int durationMs = 1000,
            [Description("Maximum observed items, 1..500.")]
            int maxItems = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                MachineryClient client = CreateClient(sessionName);
                NodeId node = MachineryJson.Node(nodeId);
                var streaming = McpCompanionTools.GetStreaming(client.Session);
                return kind switch
                {
                    MachineryObservationKind.ItemState => await McpCompanionTools.ObserveAsync(
                        inner => client.ObserveItemStateAsync(node, streaming, cancellationToken: inner),
                        MachineryJson.State, durationMs, maxItems, token).ConfigureAwait(false),
                    MachineryObservationKind.OperationMode => await McpCompanionTools.ObserveAsync(
                        inner => client.ObserveOperationModeAsync(node, streaming, cancellationToken: inner),
                        MachineryJson.State, durationMs, maxItems, token).ConfigureAwait(false),
                    MachineryObservationKind.Results => await McpCompanionTools.ObserveAsync(
                        inner => client.ObserveResultsAsync(node, streaming, cancellationToken: inner),
                        value => MachineryJson.ResultEvent(value, client.Session.MessageContext),
                        durationMs, maxItems, token).ConfigureAwait(false),
                    MachineryObservationKind.Notifications => await McpCompanionTools.ObserveAsync(
                        inner => client.ObserveNotificationsAsync(node, streaming, cancellationToken: inner),
                        MachineryJson.Event, durationMs, maxItems, token).ConfigureAwait(false),
                    MachineryObservationKind.ZeroPointAdjustments => await McpCompanionTools.ObserveAsync(
                        inner => client.ObserveZeroPointAdjustmentsAsync(node, streaming, cancellationToken: inner),
                        MachineryJson.AdjustmentEvent,
                        durationMs, maxItems, token).ConfigureAwait(false),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };
            }, ct);
        }

        /// <summary>
        /// Resolves an optional result block for a tool that requires it.
        /// </summary>
        private static async ValueTask<ResultManagementTypeClient> ResultsAsync(
            MachineryClient client,
            string machineNodeId,
            CancellationToken ct)
        {
            return await client.ResultManagementAsync(MachineryJson.Node(machineNodeId), ct).ConfigureAwait(false)
                ?? throw new ServiceResultException(StatusCodes.BadNotSupported, "No ResultManagement is published.");
        }

        /// <summary>
        /// Releases an acquired result even when the request was cancelled.
        /// </summary>
        private static async ValueTask ReleaseAsync(ResultManagementTypeClient proxy, uint handle)
        {
            if (handle != 0)
            {
                int error = await proxy.ReleaseResultHandleAsync(handle, CancellationToken.None).ConfigureAwait(false);
                if (error != 0)
                {
                    throw new InvalidOperationException($"ReleaseResultHandle returned model error {error}.");
                }
            }
        }

        /// <summary>
        /// Creates a lightweight client over the current selected session, never a cached endpoint.
        /// </summary>
        private MachineryClient CreateClient(string? sessionName)
        {
            return new MachineryClient(m_sessions.GetSessionOrThrow(sessionName), m_sessions.Telemetry);
        }

        private readonly OpcUaSessionManager m_sessions;
        private readonly McpFileTransfers m_transfers;
    }
}
