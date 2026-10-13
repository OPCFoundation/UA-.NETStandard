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
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Pumps.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// The finite read facets of an OPC 40223 pump.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PumpReadFacet>))]
    public enum PumpReadFacet
    {
        /// <summary>
        /// Design, implementation and system requirements.
        /// </summary>
        Configuration,

        /// <summary>
        /// All operational groups, including read-only actuation values.
        /// </summary>
        Operational,

        /// <summary>
        /// Live process measurements.
        /// </summary>
        Measurements,

        /// <summary>
        /// Discrete input signals.
        /// </summary>
        Signals,

        /// <summary>
        /// The pump's role and settings in a multi-pump group.
        /// </summary>
        MultiPump,

        /// <summary>
        /// All seven supervision groups.
        /// </summary>
        Supervision,

        /// <summary>
        /// All four maintenance groups.
        /// </summary>
        Maintenance,

        /// <summary>
        /// Published documentation links.
        /// </summary>
        Documentation
    }

    /// <summary>
    /// Eight read-only Pumps tools using a fresh typed client for every invocation.
    /// </summary>
    [McpServerToolType]
    public sealed class PumpsTools
    {
        /// <summary>
        /// Creates the tool surface over the shared named-session manager.
        /// </summary>
        public PumpsTools(OpcUaSessionManager sessionManager)
        {
            m_sessions = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        /// <summary>
        /// Discovers pumps, including vendor subtypes and de-duplicated dual-folder entries.
        /// </summary>
        [McpServerTool(Name = "pumps_list_pumps", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Discovers OPC 40223 pumps in DeviceSet and Machines, de-duplicating both paths. " +
            "Includes vendor subtypes, not MultiPump functional groups. Supply rootNodeId to browse one folder. " +
            "Returns a live page, not a durable snapshot; use nextOffset for the next page.")]
        public Task<CallToolResult> ListPumpsAsync(
            [Description("Optional local discovery root NodeId; omit to use the standard root.")]
            string? rootNodeId = null,
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
                PumpsClient client = CreateClient(sessionName);
                return await McpCompanionTools.PageAsync(
                    rootNodeId is null
                        ? client.EnumeratePumpsAsync(token)
                        : client.EnumeratePumpsUnderAsync(ParseNode(rootNodeId), token),
                    PumpsJson.Entry, offset, maxResults, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads the complete typed nameplate.
        /// </summary>
        [McpServerTool(Name = "pumps_read_nameplate", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads the pump Identification nameplate, including manufacturer, serial, construction, " +
            "physical address and markings-folder ID. Missing optional values remain null.")]
        public Task<CallToolResult> ReadNameplateAsync(
            [Description("Local pump NodeId returned by pumps_list_pumps.")]
            string pumpNodeId,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                PumpsClient client = CreateClient(sessionName);
                return PumpsJson.Nameplate(
                    await client.ReadNameplateAsync(ParseNode(pumpNodeId), token).ConfigureAwait(false),
                    client.Session.MessageContext);
            }, ct);
        }

        /// <summary>
        /// Lists the pump's vendor-defined marking objects.
        /// </summary>
        [McpServerTool(Name = "pumps_list_markings", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Lists published marking objects below MarkingsFolderId returned by pumps_read_nameplate, " +
            "without interpreting vendor-specific kinds. " +
            "Returns a live page of node IDs, browse names, display names and type definitions.")]
        public Task<CallToolResult> ListMarkingsAsync(
            [Description("MarkingsFolderId returned by pumps_read_nameplate, not the pump NodeId.")]
            string markingsFolderNodeId,
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
                PumpsClient client = CreateClient(sessionName);
                return McpCompanionTools.Page(
                    await client.ReadMarkingsAsync(ParseNode(markingsFolderNodeId), token).ConfigureAwait(false),
                    PumpsJson.Entry, offset, maxResults);
            }, ct);
        }

        /// <summary>
        /// Reads one supported semantic pump facet without performing any control operation.
        /// </summary>
        [McpServerTool(Name = "pumps_read_group", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads one finite OPC 40223 facet: Configuration, Operational, Measurements, Signals, " +
            "MultiPump, Supervision, Maintenance or Documentation. Even control/actuation fields are only read. " +
            "Preserves typed values, quality, engineering units, ranges and source timestamps.")]
        public Task<CallToolResult> ReadGroupAsync(
            [Description("Local pump NodeId returned by pumps_list_pumps.")]
            string pumpNodeId,
            PumpReadFacet facet,
            [Description("Include engineering units and ranges beside measurements.")]
            bool includeMetadata = true,
            [Description("Include source timestamps; false omits timestamp reads.")]
            bool includeTimestamps = true,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                PumpsClient client = CreateClient(sessionName);
                NodeId pump = ParseNode(pumpNodeId);
                PumpReadOptions options = Options(includeMetadata, includeTimestamps);
                IServiceMessageContext context = client.Session.MessageContext;
                return facet switch
                {
                    PumpReadFacet.Configuration => PumpsJson.Configuration(
                        await client.ReadConfigurationAsync(pump, options, token).ConfigureAwait(false), context),
                    PumpReadFacet.Operational => PumpsJson.Operational(
                        await client.ReadOperationalAsync(pump, options, token).ConfigureAwait(false), context),
                    PumpReadFacet.Measurements => PumpsJson.ValueSet(
                        await client.ReadMeasurementsAsync(pump, options, token).ConfigureAwait(false), context),
                    PumpReadFacet.Signals => PumpsJson.ValueSet(
                        await client.ReadSignalsAsync(pump, options, token).ConfigureAwait(false), context),
                    PumpReadFacet.MultiPump => PumpsJson.MultiPump(
                        await client.ReadMultiPumpAsync(pump, token).ConfigureAwait(false)),
                    PumpReadFacet.Supervision => PumpsJson.Supervision(
                        await client.ReadSupervisionAsync(pump, options, token).ConfigureAwait(false), context),
                    PumpReadFacet.Maintenance => PumpsJson.Maintenance(
                        await client.ReadMaintenanceAsync(pump, options, token).ConfigureAwait(false), context),
                    PumpReadFacet.Documentation => PumpsJson.ValueSet(
                        await client.ReadDocumentationAsync(pump, token).ConfigureAwait(false), context),
                    _ => throw new ArgumentOutOfRangeException(nameof(facet))
                };
            }, ct);
        }

        /// <summary>
        /// Lists typed ports and their published groups.
        /// </summary>
        [McpServerTool(Name = "pumps_list_ports", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Lists pump ports with Drive, InletConnection, OutletConnection or Unknown kind, direction, " +
            "category and their design/measurement groups. Vendor ports retain their actual type definition. " +
            "Returns a live page.")]
        public Task<CallToolResult> ListPortsAsync(
            [Description("Local pump NodeId returned by pumps_list_pumps.")]
            string pumpNodeId,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Include engineering units and ranges beside measurements.")]
            bool includeMetadata = true,
            [Description("Include source timestamps; false omits timestamp reads.")]
            bool includeTimestamps = true,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                PumpsClient client = CreateClient(sessionName);
                return await McpCompanionTools.PageAsync(
                    client.EnumeratePortsAsync(
                        ParseNode(pumpNodeId), Options(includeMetadata, includeTimestamps), token),
                    port => PumpsJson.Port(port, client.Session.MessageContext),
                    offset, maxResults, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads an open value set and exposes nested group IDs for subsequent reads.
        /// </summary>
        [McpServerTool(Name = "pumps_read_value_set", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads values of a discovered Pumps functional-group node. Returns a live values page plus " +
            "nested group IDs; call again on a nested ID to inspect its values. Does not stringify variants.")]
        public Task<CallToolResult> ReadValueSetAsync(
            [Description("Local functional-group NodeId returned by discovery.")]
            string groupNodeId,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Include engineering units and ranges beside measurements.")]
            bool includeMetadata = true,
            [Description("Include source timestamps; false omits timestamp reads.")]
            bool includeTimestamps = true,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                PumpsClient client = CreateClient(sessionName);
                Ua.Pumps.PumpValueSet values = await client.ReadValueSetAsync(
                    ParseNode(groupNodeId), Options(includeMetadata, includeTimestamps), token).ConfigureAwait(false);
                JsonObject page = McpCompanionTools.Page(
                    values.ToArrayOf(), value => PumpsJson.Value(value, client.Session.MessageContext),
                    offset, maxResults);
                var groups = new JsonObject();
                foreach (var group in values.Groups)
                {
                    groups[group.Key] = group.Value.NodeId.ToString();
                }
                page["nodeId"] = values.NodeId.ToString();
                page["groups"] = groups;
                return page;
            }, ct);
        }

        /// <summary>
        /// Reads the complete semantic pump snapshot.
        /// </summary>
        [McpServerTool(Name = "pumps_read_snapshot", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads the whole pump: nameplate, configuration, operation, supervision, maintenance, " +
            "documentation and ports. More expensive than pumps_read_group; values are a live read, not an atomic " +
            "plant snapshot. This tool never writes or commands a pump.")]
        public Task<CallToolResult> ReadSnapshotAsync(
            [Description("Local pump NodeId returned by pumps_list_pumps.")]
            string pumpNodeId,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Include engineering units and ranges beside measurements.")]
            bool includeMetadata = true,
            [Description("Include source timestamps; false omits timestamp reads.")]
            bool includeTimestamps = true,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                PumpsClient client = CreateClient(sessionName);
                return PumpsJson.Snapshot(await client.ReadPumpAsync(
                    ParseNode(pumpNodeId), Options(includeMetadata, includeTimestamps), token).ConfigureAwait(false),
                    client.Session.MessageContext, offset, maxResults);
            }, ct);
        }

        /// <summary>
        /// Classifies an exact or vendor-derived pump type.
        /// </summary>
        [McpServerTool(Name = "pumps_classify_type", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Tests whether a type definition is PumpType or a vendor subtype. MultiPumpType is not a " +
            "pump. Supply a type-definition ID from discovery, not an instance ID.")]
        public Task<CallToolResult> ClassifyTypeAsync(
            [Description("Type-definition NodeId from discovery, not an instance NodeId.")]
            string typeDefinitionNodeId,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                PumpsClient client = CreateClient(sessionName);
                NodeId type = ParseNode(typeDefinitionNodeId);
                return new JsonObject
                {
                    ["typeDefinitionId"] = type.ToString(),
                    ["isPump"] = await client.IsPumpAsync(type, token).ConfigureAwait(false)
                };
            }, ct);
        }

        /// <summary>
        /// Resolves the current named session and creates a typed client.
        /// </summary>
        private PumpsClient CreateClient(string? sessionName)
        {
            var client = new PumpsClient(m_sessions.GetSessionOrThrow(sessionName), m_sessions.Telemetry);
            if (!client.IsSupported)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The selected session does not publish the Pumps namespace.");
            }
            return client;
        }

        /// <summary>
        /// Rejects missing instance identifiers before sending a service request.
        /// </summary>
        private static NodeId ParseNode(string nodeId)
        {
            NodeId parsed = NodeId.Parse(nodeId);
            return parsed.IsNull
                ? throw new ArgumentException("A non-null node ID is required.", nameof(nodeId))
                : parsed;
        }

        /// <summary>
        /// Builds the typed read options.
        /// </summary>
        private static PumpReadOptions Options(bool metadata, bool timestamps)
        {
            return new PumpReadOptions
            {
                IncludeEngineeringUnits = metadata,
                IncludeRanges = metadata,
                IncludeTimestamps = timestamps
            };
        }

        /// <summary>
        /// The shared manager, not a cached session or proxy.
        /// </summary>
        private readonly OpcUaSessionManager m_sessions;
    }
}
