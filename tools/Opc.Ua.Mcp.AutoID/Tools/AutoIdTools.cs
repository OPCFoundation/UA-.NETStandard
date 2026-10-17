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
using Opc.Ua.AutoID;
using Opc.Ua.AutoID.Client;
using Opc.Ua.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Nine AutoID tools using a fresh typed client for every invocation. None of them changes a tag.
    /// </summary>
    [McpServerToolType]
    public sealed class AutoIdTools
    {
        /// <summary>
        /// Creates the tool surface over the shared named-session manager.
        /// </summary>
        public AutoIdTools(OpcUaSessionManager sessionManager)
        {
            m_sessions = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        /// <summary>
        /// Discovers readers of the five concrete kinds.
        /// </summary>
        [McpServerTool(Name = "autoid_list_readers", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Discovers OPC 30010 readers (RFID, OCR, optical, optical verifier, RTLS, including vendor " +
            "subtypes) below the Device Integration DeviceSet or below rootNodeId. Returns a live page, not a " +
            "durable snapshot; use nextOffset for the next page.")]
        public Task<CallToolResult> ListReadersAsync(
            [Description("Optional local discovery root NodeId; omit to use the DeviceSet.")]
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
                AutoIdClient client = CreateClient(sessionName);
                return await McpCompanionTools.PageAsync(
                    rootNodeId is null
                        ? client.EnumerateReadersAsync(token)
                        : client.EnumerateReadersUnderAsync(ParseNode(rootNodeId), token),
                    AutoIdJson.Entry, offset, maxResults, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads the state of a reader.
        /// </summary>
        [McpServerTool(Name = "autoid_read_device", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads a reader's name, DeviceStatus (Idle, Error, Scanning, Busy), model version, location, " +
            "antenna names, last scan and nameplate in one pass. Optional properties the device does not " +
            "publish stay null or empty.")]
        public Task<CallToolResult> ReadDeviceAsync(
            [Description("Reader NodeId returned by autoid_list_readers.")]
            string readerNodeId,
            [Description("Include the DI nameplate (manufacturer, model, serial number).")]
            bool includeNameplate = true,
            [Description("Include DeviceInfo, DeviceLocation, DeviceLocationName and AntennaNames.")]
            bool includeOptionalProperties = true,
            [Description("Include LastScanData and LastScanTimestamp.")]
            bool includeLastScan = true,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (AutoIdClient client, ISession session) = CreateClientAndSession(sessionName);
                AutoIdReaderEntry reader = await client.GetReaderAsync(ParseNode(readerNodeId), token)
                    .ConfigureAwait(false);
                AutoIdDeviceSnapshot snapshot = await client.ReadDeviceAsync(
                    reader,
                    new AutoIdReadOptions
                    {
                        IncludeNameplate = includeNameplate,
                        IncludeOptionalProperties = includeOptionalProperties,
                        IncludeLastScan = includeLastScan
                    },
                    token).ConfigureAwait(false);
                return AutoIdJson.Device(snapshot, session.MessageContext);
            }, ct);
        }

        /// <summary>
        /// Runs the synchronous scan.
        /// </summary>
        [McpServerTool(Name = "autoid_scan", ReadOnly = false, Destructive = false, Idempotent = false)]
        [Description("Runs the synchronous Scan method (OPC 30010 6.1.3.4) and returns the typed scan results and " +
            "the device status. The scan ends at durationMs, after cycles scan cycles, or as soon as data is " +
            "available when dataAvailable is true; at least one must be set. A scan while another is active " +
            "fails with Bad_InvalidState. Not available for an optical verifier: use autoid_scan_start and " +
            "autoid_observe_scan_events.")]
        public Task<CallToolResult> ScanAsync(
            [Description("Reader NodeId returned by autoid_list_readers.")]
            string readerNodeId,
            [Description("Scan duration in milliseconds, 0 for no duration limit, at most 600000.")]
            double durationMs = 0,
            [Description("Number of scan cycles, 0 for no cycle limit, at most 1000000.")]
            int cycles = 0,
            [Description("End the scan as soon as scan data is available.")]
            bool dataAvailable = false,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (AutoIdClient client, ISession session) = CreateClientAndSession(sessionName);
                ScanSettings settings = Settings(durationMs, cycles, dataAvailable);
                if (settings.Duration == 0 && settings.Cycles == 0 && !settings.DataAvailable)
                {
                    throw new ArgumentException(
                        "A synchronous scan needs durationMs, cycles or dataAvailable to end it.");
                }
                AutoIdReaderEntry reader = await client.GetReaderAsync(ParseNode(readerNodeId), token)
                    .ConfigureAwait(false);
                return AutoIdJson.Scan(
                    await client.ScanAsync(reader, settings, token).ConfigureAwait(false),
                    session.MessageContext);
            }, ct);
        }

        /// <summary>
        /// Starts the asynchronous scan.
        /// </summary>
        [McpServerTool(Name = "autoid_scan_start", ReadOnly = false, Destructive = false, Idempotent = false)]
        [Description("Starts the asynchronous scan (OPC 30010 6.1.3.5); results arrive as scan events that " +
            "autoid_observe_scan_events collects. With no limit set the scan runs until autoid_scan_stop, or until " +
            "the starting session closes. Returns the status the device reports for the start.")]
        public Task<CallToolResult> ScanStartAsync(
            [Description("Reader NodeId returned by autoid_list_readers.")]
            string readerNodeId,
            [Description("Scan duration in milliseconds, 0 for no duration limit, at most 600000.")]
            double durationMs = 0,
            [Description("Number of scan cycles, 0 for no cycle limit, at most 1000000.")]
            int cycles = 0,
            [Description("End the scan as soon as scan data is available.")]
            bool dataAvailable = false,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                AutoIdClient client = CreateClient(sessionName);
                AutoIdReaderEntry reader = await client.GetReaderAsync(ParseNode(readerNodeId), token)
                    .ConfigureAwait(false);
                AutoIdOperationStatusEnumeration status = await client.ScanStartAsync(
                    reader, Settings(durationMs, cycles, dataAvailable), token).ConfigureAwait(false);
                return new JsonObject { ["status"] = status.ToString() };
            }, ct);
        }

        /// <summary>
        /// Stops the asynchronous scan.
        /// </summary>
        [McpServerTool(Name = "autoid_scan_stop", ReadOnly = false, Destructive = false, Idempotent = false)]
        [Description("Stops an active asynchronous scan (OPC 30010 6.1.3.6). Fails with Bad_InvalidState when no " +
            "scan is active.")]
        public Task<CallToolResult> ScanStopAsync(
            [Description("Reader NodeId returned by autoid_list_readers.")]
            string readerNodeId,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                AutoIdClient client = CreateClient(sessionName);
                AutoIdReaderEntry reader = await client.GetReaderAsync(ParseNode(readerNodeId), token)
                    .ConfigureAwait(false);
                await client.ScanStopAsync(reader, token).ConfigureAwait(false);
                return new JsonObject { ["stopped"] = true };
            }, ct);
        }

        /// <summary>
        /// Collects scan events for a bounded window.
        /// </summary>
        [McpServerTool(Name = "autoid_observe_scan_events", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Subscribes to the scan events of a reader (AutoIdScanEventType and its subtypes) for " +
            "durationMs and returns up to maxItems events with their typed scan results. The monitored item is " +
            "removed when the call ends. stoppedBy tells whether the window or the item limit ended it.")]
        public Task<CallToolResult> ObserveScanEventsAsync(
            [Description("Reader NodeId returned by autoid_list_readers.")]
            string readerNodeId,
            [Description("Observation window in milliseconds, 1..30000.")]
            int durationMs = 1000,
            [Description("Maximum returned events, 1..500.")]
            int maxItems = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (AutoIdClient client, ISession session) = CreateClientAndSession(sessionName);
                AutoIdReaderEntry reader = await client.GetReaderAsync(ParseNode(readerNodeId), token)
                    .ConfigureAwait(false);
                return await McpCompanionTools.ObserveAsync(
                    observeToken => client.ObserveScanEventsAsync(
                        McpCompanionTools.GetStreaming(session), reader, observeToken),
                    record => AutoIdJson.ScanEvent(record, session.MessageContext),
                    durationMs,
                    maxItems,
                    token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads tag memory.
        /// </summary>
        [McpServerTool(Name = "autoid_read_tag", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads an area of the memory of one RFID tag (ReadTag, OPC 30010 6.5.3.7). The tag is " +
            "addressed by the hexadecimal identifier a scan returned. Password protected memory cannot be read " +
            "through this tool. The device status (for example PERMISSON_ERROR or NO_IDENTIFIER) is part of the " +
            "result, not an error.")]
        public Task<CallToolResult> ReadTagAsync(
            [Description("RFID reader NodeId returned by autoid_list_readers.")]
            string readerNodeId,
            [Description("Hexadecimal identifier (EPC) of the tag, 1..62 bytes.")]
            string identifierHex,
            [Description("Memory bank: 0 reserved, 1 EPC, 2 TID, 3 user.")]
            int region = 3,
            [Description("Start address in bytes, 0..65535.")]
            int offset = 0,
            [Description("Number of bytes to read, 1..4096.")]
            int length = 16,
            [Description("Code type of the identifier: UID, EPC or RAW:BYTES.")]
            string codeType = "UID",
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                AutoIdClient client = CreateClient(sessionName);
                ArgumentOutOfRangeException.ThrowIfLessThan(region, 0);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(region, ushort.MaxValue);
                ArgumentOutOfRangeException.ThrowIfLessThan(offset, 0);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, ushort.MaxValue);
                ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(length, 4096);
                ScanData identifier = Identifier(identifierHex);
                AutoIdReaderEntry reader = await client.GetReaderAsync(ParseNode(readerNodeId), token)
                    .ConfigureAwait(false);
                return AutoIdJson.Tag(await client.ReadTagAsync(
                    reader, identifier, codeType, (ushort)region, (uint)offset, (uint)length, default, token)
                    .ConfigureAwait(false));
            }, ct);
        }

        /// <summary>
        /// Lists the location types of an RTLS device.
        /// </summary>
        [McpServerTool(Name = "autoid_get_supported_location_types", ReadOnly = true, Destructive = false,
            Idempotent = true)]
        [Description("Returns the location types (NMEA, LOCAL, WGS84, NAME) an RTLS device supports; the first is " +
            "its default (GetSupportedLocationTypes, OPC 30010 6.6.3.3).")]
        public Task<CallToolResult> GetSupportedLocationTypesAsync(
            [Description("RTLS device NodeId returned by autoid_list_readers.")]
            string readerNodeId,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                AutoIdClient client = CreateClient(sessionName);
                AutoIdReaderEntry reader = await client.GetReaderAsync(ParseNode(readerNodeId), token)
                    .ConfigureAwait(false);
                ArrayOf<LocationTypeEnumeration> types = await client.GetSupportedLocationTypesAsync(reader, token)
                    .ConfigureAwait(false);
                var items = new JsonArray();
                foreach (LocationTypeEnumeration type in types)
                {
                    items.Add(type.ToString());
                }
                return new JsonObject { ["supportedLocationTypes"] = items };
            }, ct);
        }

        /// <summary>
        /// Locates an object.
        /// </summary>
        [McpServerTool(Name = "autoid_get_location", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Returns the location of an object known to an RTLS device (GetLocation, OPC 30010 6.6.3.2) " +
            "in the requested location type. The result's timestamp tells a fresh acquisition from the last " +
            "known location. An unknown object fails with Bad_NotFound.")]
        public Task<CallToolResult> GetLocationAsync(
            [Description("RTLS device NodeId returned by autoid_list_readers.")]
            string readerNodeId,
            [Description("Hexadecimal identifier of the located object, 1..62 bytes.")]
            string identifierHex,
            [Description("Requested location type: NMEA, LOCAL, WGS84 or NAME.")]
            string locationType,
            [Description("Code type of the identifier: UID, EPC or RAW:BYTES.")]
            string codeType = "UID",
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (AutoIdClient client, ISession session) = CreateClientAndSession(sessionName);
                ScanData identifier = Identifier(identifierHex);
                if (!Enum.TryParse(locationType, ignoreCase: true, out LocationTypeEnumeration type) ||
                    !Enum.IsDefined(type))
                {
                    throw new ArgumentException("The location type must be NMEA, LOCAL, WGS84 or NAME.");
                }
                AutoIdReaderEntry reader = await client.GetReaderAsync(ParseNode(readerNodeId), token)
                    .ConfigureAwait(false);
                RtlsLocationResult result = await client.GetLocationAsync(reader, identifier, codeType, type, token)
                    .ConfigureAwait(false);
                return new JsonObject { ["location"] = AutoIdJson.Location(result, session.MessageContext) };
            }, ct);
        }

        /// <summary>
        /// Resolves the current named session and creates a typed client.
        /// </summary>
        private AutoIdClient CreateClient(string? sessionName)
        {
            return CreateClientAndSession(sessionName).Client;
        }

        private (AutoIdClient Client, ISession Session) CreateClientAndSession(string? sessionName)
        {
            ISession session = m_sessions.GetSessionOrThrow(sessionName);
            var client = new AutoIdClient(session, m_sessions.Telemetry);
            if (!client.IsSupported)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The selected session does not publish the AutoID namespace.");
            }
            return (client, session);
        }

        /// <summary>
        /// Rejects missing instance identifiers before sending a service request.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static NodeId ParseNode(string nodeId)
        {
            var parsed = NodeId.Parse(nodeId);
            return parsed.IsNull
                ? throw new ArgumentException("A non-null node ID is required.", nameof(nodeId))
                : parsed;
        }

        /// <summary>
        /// Builds bounded scan settings; a negative or oversized termination condition is refused here.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static ScanSettings Settings(double durationMs, int cycles, bool dataAvailable)
        {
            if (double.IsNaN(durationMs) || durationMs < 0 || durationMs > 600_000)
            {
                throw new ArgumentOutOfRangeException(nameof(durationMs), "The duration must be 0..600000 ms.");
            }
            ArgumentOutOfRangeException.ThrowIfNegative(cycles);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(cycles, 1_000_000);
            return new ScanSettings { Duration = durationMs, Cycles = cycles, DataAvailable = dataAvailable };
        }

        /// <summary>
        /// Builds the identifier of a tag or located object from its hexadecimal form.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static ScanData Identifier(string identifierHex)
        {
            if (string.IsNullOrWhiteSpace(identifierHex))
            {
                throw new ArgumentException("A hexadecimal identifier is required.", nameof(identifierHex));
            }
            byte[] bytes;
            try
            {
                bytes = Convert.FromHexString(identifierHex);
            }
            catch (FormatException)
            {
                throw new ArgumentException("The identifier is not valid hexadecimal.", nameof(identifierHex));
            }
            if (bytes.Length is < 1 or > 62)
            {
                throw new ArgumentException("The identifier must be 1..62 bytes.", nameof(identifierHex));
            }
            return new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = ByteString.From(bytes) };
        }

        /// <summary>
        /// The shared manager, not a cached session or proxy.
        /// </summary>
        private readonly OpcUaSessionManager m_sessions;
    }
}
