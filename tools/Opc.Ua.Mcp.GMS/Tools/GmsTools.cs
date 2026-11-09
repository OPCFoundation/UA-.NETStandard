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
using Opc.Ua.GMS;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.MachineTool;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Six bounded GMS workflows using native clients, generated proxies and borrowed shared sessions.
    /// </summary>
    [McpServerToolType]
    public sealed class GmsTools
    {
        /// <summary>
        /// Discovers real native or vendor-derived GMS instances without treating model presence as a capability.
        /// </summary>
        [McpServerTool(Name = "gms_list_systems", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Lists actual GMSType instances and vendor subtypes through shared Machinery/DI discovery. " +
            "Does not include base MachineTools or WMTP peripherals merely because their namespaces are loaded. " +
            "For per-instance capabilities use gms_read_system section=Capabilities. Overflow fails, not truncates. " +
            "No jobs, measurements, equipment settings or acknowledgements are changed.")]
        public static Task<CallToolResult> ListSystemsAsync(
            GmsClientAccessor accessor,
            [Description("Maximum discovered inventory elements and returned systems, 1-100; overflow is an error.")]
            [Range(1, 100)] int maximumResults = 50,
            [Description("Maximum complete UTF-8 JSON response bytes, 1024-1048576; default 128 KiB.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Connected OPC UA session name; omit only when exactly one session is connected.")]
            [StringLength(128, MinimumLength = 1)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return GmsJson.ExecuteAsync(async () =>
            {
                GmsParameters.Bound(maximumResults, 1, 100);
                GmsParameters.ResponseLimit(maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                GmsClient client = accessor.CreateClient(sessionName, maximumResults, maximumResponseBytes);
                await using var lifetime = client.ConfigureAwait(false);
                ArrayOf<GmsDeviceEntry> systems = await client.DiscoverAsync(ct).ConfigureAwait(false);
                if (systems.Count > maximumResults)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                        "The GMS inventory exceeds maximumResults.");
                }
                return GmsJson.Result(new JsonObject
                {
                    ["modelPresent"] = client.Session.NamespaceUris.GetIndex(GmsModel.NamespaceUri) >= 0,
                    ["systems"] = GmsJson.Array(systems, GmsJson.Entry)
                }, maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Reads one native system section without flattening optional absence or bad-quality samples.
        /// </summary>
        [McpServerTool(Name = "gms_read_system", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Reads an actual GMS section: System (geometry/loading/program), Equipment (sensors, " +
            "qualification, accessories, additional sensors), Calibration, Production (retained jobs/parts), " +
            "or Capabilities (actual optional surfaces). Preserves native value types, quality and timestamps. " +
            "Undefined GMS_ProductionStatisticsType is never invented or claimed. " +
            "Returns the selected section as JSON with native readings and explicit optional presence. " +
            "Read-only; no control or acknowledgement.")]
        public static Task<CallToolResult> ReadSystemAsync(
            GmsClientAccessor accessor,
            [Description("Actual GMS instance NodeId returned by gms_list_systems; vendor subtypes are accepted.")]
            [StringLength(1024, MinimumLength = 1)] string systemNodeId,
            [Description("Exact section: System, Equipment, Calibration, Production, or Capabilities.")]
            string section = "System",
            [Description("Aggregate native element limit, 1-100; the host may impose a smaller bound.")]
            [Range(1, 100)] int maximumElements = 100,
            [Description("Decoded metadata and complete UTF-8 JSON response budget, 1024-1048576 bytes.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Connected OPC UA session name; omit only when exactly one session is connected.")]
            [StringLength(128, MinimumLength = 1)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return GmsJson.ExecuteAsync(async () =>
            {
                NodeId node = GmsParameters.Node(systemNodeId);
                GmsParameters.Select(section, "System", "Equipment", "Calibration", "Production", "Capabilities");
                GmsParameters.Bound(maximumElements, 1, 100);
                GmsParameters.ResponseLimit(maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                GmsClient client = accessor.CreateClient(sessionName, maximumElements, maximumResponseBytes);
                await using var lifetime = client.ConfigureAwait(false);
                var json = new GmsJson(client.Session.MessageContext);
                JsonObject value = section switch
                {
                    "System" => json.System(await client.ReadSnapshotAsync(node, ct).ConfigureAwait(false)),
                    "Equipment" => await ReadEquipmentAsync(client, node, ct).ConfigureAwait(false),
                    "Calibration" => json.Calibration(
                        await client.ReadCalibrationAsync(node, ct).ConfigureAwait(false)),
                    "Production" => json.Production(await client.ReadProductionAsync(node, ct).ConfigureAwait(false)),
                    _ => await GmsCapabilities.ReadAsync(client, node, ct).ConfigureAwait(false)
                };
                return GmsJson.Result(value, maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Queries metadata, reads acquired ring slots or inspects calculated corrections without acknowledging them.
        /// </summary>
        [McpServerTool(Name = "gms_read_results", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Reads GMS results using Latest, ById, Query, Published, or Corrections. Query uses standard " +
            "Machinery metadata equality filters and optional ResultId ordering; one extra ID detects overflow. " +
            "Published preserves original sample quality and UsedTools. Corrections reports relative/absolute values " +
            "and engineering units, never applies them. Returns JSON result metadata/content, result IDs, acquired " +
            "slots, or corrections for the selected operation. " +
            "Metadata handles are released; nothing is acknowledged.")]
        public static Task<CallToolResult> ReadResultsAsync(
            GmsClientAccessor accessor,
            [Description("Actual GMS instance NodeId, not its ResultManagement child.")]
            [StringLength(1024, MinimumLength = 1)] string systemNodeId,
            [Description("Exact operation: Latest, ById, Query, Published, or Corrections.")]
            string operation = "Latest",
            [Description("Result identifier, required only for ById; omit for other operations.")]
            [StringLength(1024, MinimumLength = 1)] string? resultId = null,
            [Description("Maximum IDs, published slots or corrections, 1-100; excess data fails without truncation.")]
            [Range(1, 100)] int maximumResults = 50,
            [Description("Native query selection object with filterField, filterValue and orderBy; " +
                "e.g. {\"filterField\":\"JobId\",\"filterValue\":\"J-1\",\"orderBy\":\"ResultId\"}. " +
                "Omit for no filter and ascending ResultId order; nondefault selection requires Query.")]
            JsonElement? query = null,
            [Description("Decoded metadata and complete UTF-8 JSON response budget, 1024-1048576 bytes.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Connected OPC UA session name; omit only when exactly one session is connected.")]
            [StringLength(128, MinimumLength = 1)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return GmsJson.ExecuteAsync(async () =>
            {
                NodeId node = GmsParameters.Node(systemNodeId);
                GmsParameters.Select(operation, "Latest", "ById", "Query", "Published", "Corrections");
                GmsParameters.Bound(maximumResults, 1, 100);
                (string filterField, JsonElement selectedFilter, string orderBy) =
                    GmsParameters.Query(query.GetValueOrDefault());
                if (operation == "ById")
                {
                    GmsParameters.Text(resultId!);
                }
                else if (resultId != null)
                {
                    throw new ArgumentException("resultId is valid only for ById.", nameof(resultId));
                }
                if (operation != "Query" &&
                    (filterField != "None" || selectedFilter.ValueKind != JsonValueKind.Undefined ||
                        orderBy != "ResultId"))
                {
                    throw new ArgumentException("Filtering and ordering are valid only for Query.", nameof(operation));
                }
                GmsResultLimits limits = GmsParameters.Results(
                    operation == "Query" ? maximumResults + 1 : maximumResults, maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                GmsClient client = accessor.CreateClient(sessionName, maximumPayloadBytes: maximumResponseBytes);
                await using var lifetime = client.ConfigureAwait(false);
                _ = await client.ReadSnapshotAsync(node, ct).ConfigureAwait(false);
                GmsResultClient results = client.GetResults(node, limits);
                var json = new GmsJson(client.Session.MessageContext);
                JsonObject value = operation switch
                {
                    "Latest" => json.ResultData(await results.GetLatestAsync(ct).ConfigureAwait(false)),
                    "ById" => json.ResultData(await results.GetByIdAsync(resultId!, ct).ConfigureAwait(false)),
                    "Published" => json.Published(await results.ReadPublishedResultsAsync(ct).ConfigureAwait(false)),
                    "Corrections" => json.Corrections(await results.ReadCorrectionsAsync(ct).ConfigureAwait(false)),
                    _ => await QueryAsync(results, filterField, selectedFilter, orderBy, maximumResults, ct)
                        .ConfigureAwait(false)
                };
                return GmsJson.Result(value, maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Downloads a bounded inline base64 payload through the standard result/file client.
        /// </summary>
        [McpServerTool(Name = "gms_download_result", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Downloads one GMS result using its standard metadata and temporary-file transfer services. " +
            "Returns bounded inline base64, never a host path. Byte or deadline exceedance is an error, not partial " +
            "success. Releases only this call's metadata/file handles, including on failure/cancellation; " +
            "no acknowledgement.")]
        public static Task<CallToolResult> DownloadResultAsync(
            GmsClientAccessor accessor,
            [Description("Actual GMS instance NodeId whose optional result-file transfer is enabled.")]
            [StringLength(1024, MinimumLength = 1)] string systemNodeId,
            [Description("Exact stored result identifier; does not name a host file or URL.")]
            [StringLength(1024, MinimumLength = 1)] string resultId,
            [Description("Maximum raw downloaded bytes, 1-524288; base64 and envelope must also fit the reply budget.")]
            [Range(1, 524288)] int maximumDownloadBytes = 65536,
            [Description("Total download deadline including metadata and setup, 1-60 seconds.")]
            [Range(1, 60)] int timeoutSeconds = 30,
            [Description("Maximum complete UTF-8 JSON response bytes including base64, 1024-1048576.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Connected OPC UA session name; omit only when exactly one session is connected.")]
            [StringLength(128, MinimumLength = 1)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return GmsJson.ExecuteAsync(async () =>
            {
                NodeId node = GmsParameters.Node(systemNodeId);
                GmsParameters.Text(resultId);
                GmsParameters.Bound(timeoutSeconds, 1, 60);
                GmsResultLimits limits = GmsParameters.Results(1, maximumResponseBytes, maximumDownloadBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                using CancellationTokenSource timeout = accessor.TimeProvider.CreateCancellationTokenSource(
                    TimeSpan.FromSeconds(timeoutSeconds));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                GmsClient client = accessor.CreateClient(sessionName, maximumPayloadBytes: maximumResponseBytes);
                await using var lifetime = client.ConfigureAwait(false);
                ByteString bytes;
                try
                {
                    _ = await client.ReadSnapshotAsync(node, linked.Token).ConfigureAwait(false);
                    bytes = await client.GetResults(node, limits).DownloadAsync(resultId, linked.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException error) when (
                    timeout.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        "The result download deadline elapsed; no partial file is returned.", error);
                }
                catch (ServiceResultException error) when (
                    timeout.IsCancellationRequested && !ct.IsCancellationRequested &&
                    (error.StatusCode == StatusCodes.BadRequestInterrupted ||
                        error.StatusCode == StatusCodes.BadRequestCancelledByClient))
                {
                    throw new TimeoutException(
                        "The result download deadline elapsed; no partial file is returned.", error);
                }
                ct.ThrowIfCancellationRequested();
                return GmsJson.Result(new JsonObject
                {
                    ["resultId"] = resultId,
                    ["byteCount"] = bytes.Length,
                    ["contentBase64"] = Convert.ToBase64String(bytes.Span)
                }, maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Observes native data, events, retained conditions or intermediate-result characteristics.
        /// </summary>
        [McpServerTool(Name = "gms_observe", ReadOnly = true, Destructive = false,
            Idempotent = false, OpenWorld = true)]
        [Description("Observes GMS Data, Events, Conditions, or IntermediateResults within a bounded window. " +
            "Conditions requests standard initial ConditionRefresh and reports completion; no acknowledgement or " +
            "confirmation is sent. Native sensor warnings and qualified intermediate characteristics are decoded. " +
            "Returns JSON changes or decoded events, completion/limit and loss state, " +
            "and retained-refresh completion. " +
            "Loss/incomplete refresh is an error. Owns only this call's subscription; never changes equipment.")]
        public static Task<CallToolResult> ObserveAsync(
            GmsClientAccessor accessor,
            [Description("Actual GMS instance NodeId; the typed client resolves its native notifiers and values.")]
            [StringLength(1024, MinimumLength = 1)] string systemNodeId,
            [Description("Exact observation kind: Data, Events, Conditions, or IntermediateResults.")]
            string kind = "Data",
            [Description("Total observation window including native setup, 1-60 seconds; default 5 seconds.")]
            [Range(1, 60)] int durationSeconds = 5,
            [Description("Maximum retained notifications, 1-100; reaching the bound ends observation.")]
            [Range(1, 100)] int maximumNotifications = 50,
            [Description("IntermediateResults-only array of at most 32 {name, namespaceIndex} placeholder selections.")]
            JsonElement? characteristics = null,
            [Description("Decoded metadata and complete UTF-8 JSON response budget, 1024-1048576 bytes.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Connected OPC UA session name; omit only when exactly one session is connected.")]
            [StringLength(128, MinimumLength = 1)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return GmsJson.ExecuteAsync(async () =>
            {
                NodeId node = GmsParameters.Node(systemNodeId);
                GmsParameters.Select(kind, "Data", "Events", "Conditions", "IntermediateResults");
                MachineToolObservationOptions options = GmsParameters.Observation(
                    durationSeconds, maximumNotifications, maximumResponseBytes);
                JsonElement selectedCharacteristics = characteristics.GetValueOrDefault();
                ArrayOf<QualifiedName> names = GmsParameters.Characteristics(selectedCharacteristics);
                if (kind != "IntermediateResults" && selectedCharacteristics.ValueKind != JsonValueKind.Undefined)
                {
                    throw new ArgumentException("characteristics is valid only for IntermediateResults.",
                        nameof(characteristics));
                }
                ArgumentNullException.ThrowIfNull(accessor);
                GmsClient client = accessor.CreateClient(sessionName, maximumPayloadBytes: maximumResponseBytes);
                await using var lifetime = client.ConfigureAwait(false);
                var json = new GmsJson(client.Session.MessageContext);
                if (kind == "IntermediateResults")
                {
                    return await GmsObservation.IntermediateAsync(
                        client, node, names, options, maximumResponseBytes, accessor.TimeProvider, ct)
                        .ConfigureAwait(false);
                }
                if (kind == "Data")
                {
                    MachineToolDataObservation values = await client.ObserveDataAsync(node, options, ct)
                        .ConfigureAwait(false);
                    return GmsJson.Result(json.Observation(values), maximumResponseBytes,
                        isError: values.DroppedNotifications != 0);
                }
                MachineToolEventObservation events = kind == "Conditions"
                    ? await client.ObserveConditionsAsync(node, options, ct).ConfigureAwait(false)
                    : await client.ObserveEventsAsync(node, options, ct).ConfigureAwait(false);
                return GmsJson.Result(json.Events(events), maximumResponseBytes,
                    isError: events.DroppedNotifications != 0 || (kind == "Conditions" && !events.RefreshCompleted));
            }, ct);
        }

        /// <summary>
        /// Acknowledges only explicitly commanded result identities through the standard typed method.
        /// </summary>
        [McpServerTool(Name = "gms_acknowledge_results", ReadOnly = false, Destructive = true,
            Idempotent = false, OpenWorld = true)]
        [Description("STATE-CHANGING result acknowledgement: calls standard AcknowledgeResults only when " +
            "confirmAcknowledgement=true is explicitly supplied. Server authorization still applies. Returns each " +
            "unmodified domain error, separate from UA service status. " +
            "Never acknowledges conditions, confirms alarms, " +
            "starts a job/measurement, or applies corrections. No automatic retries.")]
        public static Task<CallToolResult> AcknowledgeResultsAsync(
            GmsClientAccessor accessor,
            [Description("Actual GMS instance NodeId exposing standard result management.")]
            [StringLength(1024, MinimumLength = 1)] string systemNodeId,
            [Description("Native JSON array of 1-100 distinct result identifier strings; each 1-1024 characters.")]
            JsonElement resultIds,
            [Description("Must be explicitly true to authorize this result acknowledgement attempt.")]
            bool confirmAcknowledgement = false,
            [Description("Decoded metadata and complete UTF-8 JSON response budget, 1024-1048576 bytes.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Connected OPC UA session name; omit only when exactly one session is connected.")]
            [StringLength(128, MinimumLength = 1)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return GmsJson.ExecuteAsync(async () =>
            {
                if (!confirmAcknowledgement)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                        "Explicit confirmAcknowledgement=true is required before acknowledging results.");
                }
                NodeId node = GmsParameters.Node(systemNodeId);
                ArrayOf<string> identifiers = GmsParameters.Identifiers(resultIds);
                GmsResultLimits limits = GmsParameters.Results(identifiers.Count, maximumResponseBytes);
                var results = GmsJson.Array(identifiers, id => new JsonObject
                {
                    ["resultId"] = id,
                    ["domainError"] = int.MinValue,
                    ["acknowledged"] = false
                });
                var response = new JsonObject { ["results"] = results };
                // Refuse before mutation if even the worst-case acknowledgement reply cannot fit.
                _ = GmsJson.Result(response, maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                GmsClient client = accessor.CreateClient(sessionName, maximumPayloadBytes: maximumResponseBytes);
                await using var lifetime = client.ConfigureAwait(false);
                _ = await client.ReadSnapshotAsync(node, ct).ConfigureAwait(false);
                ArrayOf<int> errors = await client.GetResults(node, limits).AcknowledgeAsync(identifiers, ct)
                    .ConfigureAwait(false);
                bool failed = false;
                for (int i = 0; i < errors.Count; i++)
                {
                    failed |= errors[i] != 0;
                    results[i]!["domainError"] = errors[i];
                    results[i]!["acknowledged"] = errors[i] == 0;
                }
                return GmsJson.Result(response, maximumResponseBytes, isError: failed);
            }, ct);
        }

        /// <summary>
        /// Keeps absent optional equipment folders distinct from explicitly selected empty inventories.
        /// </summary>
        private static async Task<JsonObject> ReadEquipmentAsync(
            GmsClient client, NodeId node, CancellationToken ct)
        {
            GmsEquipmentReadSnapshot snapshot = await client.ReadEquipmentAsync(node, ct).ConfigureAwait(false);
            GMSEquipmentTypeClient equipment = await client.GetProxy(node)
                .GetEquipmentAsync(client.Telemetry, ct).ConfigureAwait(false) ??
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The required GMS equipment is absent.");
            var json = new GmsJson(client.Session.MessageContext);
            JsonObject result = json.Equipment(snapshot);
            result["accessoriesPresent"] =
                await equipment.GetAccessoriesAsync(client.Telemetry, ct).ConfigureAwait(false) != null;
            result["additionalSensorsPresent"] =
                await equipment.GetAdditionalSensorAsync(client.Telemetry, ct).ConfigureAwait(false) != null;
            return result;
        }

        /// <summary>
        /// Uses the standard query's count bound plus one to distinguish a complete response from truncation.
        /// </summary>
        private static async Task<JsonObject> QueryAsync(
            GmsResultClient client, string field, JsonElement filterValue, string orderBy,
            int maximum, CancellationToken ct)
        {
            (ContentFilter filter, ArrayOf<RelativePath> order) = GmsParameters.Filter(
                client.Session.NamespaceUris, field, filterValue, orderBy);
            ArrayOf<string> values = await client.QueryAsync(maximum + 1, filter, order, ct).ConfigureAwait(false);
            if (values.Count > maximum)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "Matching results exceed maximumResults; select a narrower metadata filter.");
            }
            return new JsonObject
            {
                ["resultIds"] = GmsJson.Array(values, value => JsonValue.Create(value)),
                ["filterField"] = field,
                ["orderBy"] = orderBy
            };
        }
    }
}
