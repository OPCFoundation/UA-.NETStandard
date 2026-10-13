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
using Opc.Ua.CranesHoists;
using Opc.Ua.CranesHoists.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Bounded OPC 40020-1 monitoring and explicit configuration over the existing typed crane client.
    /// </summary>
    [McpServerToolType]
    public sealed class CranesHoistsTools
    {
        /// <summary>
        /// Discovers actual crane systems, preserving vendor subtype identities.
        /// </summary>
        [McpServerTool(Name = "craneshoists_list_systems", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Lists OPC 40020-1 crane systems under DeviceSet or a supplied organizer, including vendor " +
            "subtypes. Model presence is not an instance capability claim. Bounded; overflow fails without " +
            "truncation. No settings, limits, acknowledgements or equipment state are changed.")]
        public static Task<CallToolResult> ListSystemsAsync(
            CranesHoistsClientAccessor accessor,
            [Description("Optional organizer NodeId, such as i=85 or the Machines folder; omit for DeviceSet.")]
            [StringLength(1024, MinimumLength = 1)] string? rootNodeId = null,
            [Description("Maximum returned systems, 1-100; excess results fail, never silently truncate.")]
            [Range(1, 100)] int maximumResults = 50,
            [Description("Maximum UTF-8 JSON response bytes, 1024-1048576; excess data fails without truncation.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Connected OPC UA session name; omit only when exactly one session is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return CranesHoistsJson.ExecuteAsync(async () =>
            {
                CranesHoistsParameters.ResponseLimit(maximumResponseBytes);
                ArgumentOutOfRangeException.ThrowIfLessThan(maximumResults, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumResults, 100);
                NodeId root = rootNodeId == null ? NodeId.Null : CranesHoistsParameters.Node(rootNodeId);
                ArgumentNullException.ThrowIfNull(accessor);
                CranesHoistsClient client = accessor.CreateClient(sessionName);
                ArrayOf<CraneEntry> entries = root.IsNull
                    ? await client.DiscoverAsync(ct).ConfigureAwait(false)
                    : await client.DiscoverAsync(root, ct).ConfigureAwait(false);
                if (entries.Count > maximumResults)
                {
                    throw ServiceResultException.Create(StatusCodes.BadTooManyOperations,
                        "The inventory exceeds maximumResults; choose a narrower organizer.");
                }
                var systems = new JsonArray();
                foreach (CraneEntry entry in entries)
                {
                    systems.Add((JsonNode)CranesHoistsJson.Entry(entry));
                }
                return CranesHoistsJson.Result(new JsonObject
                {
                    ["modelPresent"] = client.SupportsModel,
                    ["systems"] = systems
                }, maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Reads one complete bounded system with its actual selected capabilities and native relationships.
        /// </summary>
        [McpServerTool(Name = "craneshoists_read_system", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Reads one crane's system, devices, axes, loads, motors, brakes and protective functions. " +
            "Includes actual field presence/write access, nameplates, axis-function metadata, native ProtectsAxis " +
            "and physical relationships. Preserves UA quality and source time. Read failures are not absence. " +
            "Does not write limits, acknowledge conditions or control equipment.")]
        public static Task<CallToolResult> ReadSystemAsync(
            CranesHoistsClientAccessor accessor,
            [Description("Crane system NodeId returned by craneshoists_list_systems; vendor subtypes are supported.")]
            [StringLength(1024, MinimumLength = 1)] string systemNodeId,
            [Description("Maximum UTF-8 JSON response bytes, 1024-1048576; " +
                "use read_element if this limit is exceeded.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 1048576,
            [Description("Connected OPC UA session name; omit only when exactly one session is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return CranesHoistsJson.ExecuteAsync(async () =>
            {
                CranesHoistsParameters.ResponseLimit(maximumResponseBytes);
                NodeId node = CranesHoistsParameters.Node(systemNodeId);
                ArgumentNullException.ThrowIfNull(accessor);
                CranesHoistsClient client = accessor.CreateClient(sessionName);
                CraneSystemSnapshot snapshot = await client.ReadSystemAsync(node, ct).ConfigureAwait(false);
                var elements = new JsonArray();
                foreach (CraneElementSnapshot element in snapshot.Elements)
                {
                    elements.Add((JsonNode)CranesHoistsJson.Element(client, element));
                }
                return CranesHoistsJson.Result(new JsonObject
                {
                    ["system"] = CranesHoistsJson.Element(client, snapshot.System),
                    ["elements"] = elements
                }, maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Reads a focused native component snapshot without enumerating the rest of the system.
        /// </summary>
        [McpServerTool(Name = "craneshoists_read_element", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Reads one crane system, device, axis (including load), motor, brake or protective function. " +
            "Reports selected/absent fields, actual write access, full UA DataValues, function metadata and " +
            "native relationships. Accepted request properties remain distinct from reported active limits. " +
            "Read-only; no equipment or condition changes.")]
        public static Task<CallToolResult> ReadElementAsync(
            CranesHoistsClientAccessor accessor,
            [Description("Crane element NodeId returned by craneshoists_read_system; not a variable or limit NodeId.")]
            [StringLength(1024, MinimumLength = 1)] string elementNodeId,
            [Description("Maximum UTF-8 JSON response bytes, 1024-1048576; excess data fails without truncation.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Connected OPC UA session name; omit only when exactly one session is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return CranesHoistsJson.ExecuteAsync(async () =>
            {
                CranesHoistsParameters.ResponseLimit(maximumResponseBytes);
                NodeId node = CranesHoistsParameters.Node(elementNodeId);
                ArgumentNullException.ThrowIfNull(accessor);
                CranesHoistsClient client = accessor.CreateClient(sessionName);
                CraneElementSnapshot snapshot = await client.ReadElementAsync(node, ct).ConfigureAwait(false);
                return CranesHoistsJson.Result(CranesHoistsJson.Element(client, snapshot), maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Observes original data changes using one bounded, call-owned subscription.
        /// </summary>
        [McpServerTool(Name = "craneshoists_observe_values", ReadOnly = true, Destructive = false,
            Idempotent = false, OpenWorld = true)]
        [Description("Observes 1-64 distinct signal NodeIds from crane snapshots for a bounded window. " +
            "Preserves original DataValues, quality and timestamps, reports limit/overflow, and releases only " +
            "this call's subscription on success, failure or cancellation. Never writes limits or controls equipment.")]
        public static Task<CallToolResult> ObserveValuesAsync(
            CranesHoistsClientAccessor accessor,
            [Description("JSON array of 1-64 distinct signal NodeId strings from crane snapshots; no null NodeIds.")]
            JsonElement nodeIds,
            [Description("Total observation window including setup, 1-60 seconds; default 5 seconds.")]
            [Range(1, 60)] int durationSeconds = 5,
            [Description("Maximum retained notifications, 1-100; reaching it ends this call's observation.")]
            [Range(1, 100)] int maximumNotifications = 50,
            [Description("Decoded payload and UTF-8 JSON response budget, 1024-1048576 bytes; overflow is an error.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 1048576,
            [Description("Connected OPC UA session name; omit only when exactly one session is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return CranesHoistsJson.ExecuteAsync(async () =>
            {
                CraneObservationOptions options = CranesHoistsParameters.Observation(
                    durationSeconds, maximumNotifications, maximumResponseBytes);
                ArrayOf<NodeId> nodes = CranesHoistsParameters.Nodes(nodeIds);
                ArgumentNullException.ThrowIfNull(accessor);
                CranesHoistsClient client = accessor.CreateClient(sessionName);
                CraneObservation observation = await client.ObserveValuesAsync(nodes, options, ct)
                    .ConfigureAwait(false);
                return CranesHoistsJson.Result(CranesHoistsJson.Observation(client, observation), maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Observes native events without refresh, acknowledgement, confirmation or persistent subscriptions.
        /// </summary>
        [McpServerTool(Name = "craneshoists_observe_events", ReadOnly = true, Destructive = false,
            Idempotent = false, OpenWorld = true)]
        [Description("Observes standard events from an event-enabled crane system or server notifier for a " +
            "bounded window. Returns original event IDs/types and namespace-qualified selected fields. " +
            "No refresh, acknowledgement, confirmation or writes. Releases only this call's subscription, " +
            "preserving other subscriptions and the shared session.")]
        public static Task<CallToolResult> ObserveEventsAsync(
            CranesHoistsClientAccessor accessor,
            [Description("Event-enabled crane system NodeId or server notifier i=2253; " +
                "server access is authoritative.")]
            [StringLength(1024, MinimumLength = 1)] string notifierNodeId,
            [Description("Total observation window including setup, 1-60 seconds; default 5 seconds.")]
            [Range(1, 60)] int durationSeconds = 5,
            [Description("Maximum retained notifications, 1-100; reaching it ends this call's observation.")]
            [Range(1, 100)] int maximumNotifications = 50,
            [Description("Decoded payload and UTF-8 JSON response budget, 1024-1048576 bytes; overflow is an error.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 1048576,
            [Description("Connected OPC UA session name; omit only when exactly one session is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return CranesHoistsJson.ExecuteAsync(async () =>
            {
                CraneObservationOptions options = CranesHoistsParameters.Observation(
                    durationSeconds, maximumNotifications, maximumResponseBytes);
                NodeId node = CranesHoistsParameters.Node(notifierNodeId);
                ArgumentNullException.ThrowIfNull(accessor);
                CranesHoistsClient client = accessor.CreateClient(sessionName);
                CraneObservation observation = await client.ObserveEventsAsync(node, options, ct).ConfigureAwait(false);
                return CranesHoistsJson.Result(CranesHoistsJson.Observation(client, observation), maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Submits exactly one explicitly authorized standard configuration request without inventing actuation.
        /// </summary>
        [McpServerTool(Name = "craneshoists_configure_limit", ReadOnly = false, Destructive = true,
            Idempotent = false, OpenWorld = true)]
        [Description("DEVICE-CHANGING configuration: writes one standard speed-limit request only after explicit " +
            "confirmConfiguration=true. Requires actual server authorization and equipment-provider acceptance. " +
            "Setpoints are finite Double percentages 0-100; enable requests are Boolean. No retries, motion " +
            "methods or safety bypass. Acceptance is NOT confirmation of the reported active limit; re-read it.")]
        public static Task<CallToolResult> ConfigureLimitAsync(
            CranesHoistsClientAccessor accessor,
            [Description("Crane axis NodeId, or system NodeId only for SpeedLimitGlobalEnabled; " +
                "not a variable NodeId.")]
            [StringLength(1024, MinimumLength = 1)] string elementNodeId,
            [Description("Exact standard request: SpeedLimitDirMinusSetpoint, SpeedLimitDirPlusSetpoint, " +
                "SpeedLimitEnabledDirMinus, SpeedLimitEnabledDirPlus, or SpeedLimitGlobalEnabled.")]
            string signal,
            [Description("JSON number 0-100 for a setpoint, or JSON true/false for an enable request; no strings.")]
            JsonElement value,
            [Description("Must be explicitly true to authorize this configuration attempt; " +
                "never bypasses server policy.")]
            bool confirmConfiguration = false,
            [Description("Connected OPC UA session name; omit only when exactly one session is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return CranesHoistsJson.ExecuteAsync(async () =>
            {
                if (!confirmConfiguration)
                {
                    throw ServiceResultException.Create(StatusCodes.BadUserAccessDenied,
                        "Explicit confirmConfiguration=true is required before attempting a limit write.");
                }
                NodeId node = CranesHoistsParameters.Node(elementNodeId);
                (CraneSignal selected, Variant requested) = CranesHoistsParameters.Limit(signal, value);
                // Echo the native wire value, not a potentially unbounded or higher-precision JSON number token.
                JsonNode requestedValue = requested.TryGetValue(out double percentage)
                    ? JsonValue.Create(percentage)
                    : JsonValue.Create(value.GetBoolean());
                ArgumentNullException.ThrowIfNull(accessor);
                CranesHoistsClient client = accessor.CreateClient(sessionName);
                StatusCode status = await client.ConfigureLimitAsync(node, selected, requested, ct)
                    .ConfigureAwait(false);
                return CranesHoistsJson.Result(new JsonObject
                {
                    ["elementNodeId"] = node.ToString(),
                    ["signal"] = selected.ToString(),
                    ["requestedValue"] = requestedValue,
                    ["statusCode"] = Serialization.OpcUaJsonHelper.StatusCodeToString(status),
                    ["requestAccepted"] = StatusCode.IsGood(status),
                    ["activeValueVerified"] = false,
                    ["note"] = "Acceptance records a request, not a hardware effect. Re-read the reported active limit."
                }, CranesHoistsParameters.MaximumResponseBytes, isError: !StatusCode.IsGood(status));
            }, ct);
        }
    }
}
