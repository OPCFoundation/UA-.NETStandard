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
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Bounded OPC 40505 workflows in the MachineTool catalog, using borrowed sessions and generated proxies.
    /// </summary>
    [McpServerToolType]
    public sealed class WmtpMcpTools
    {
        /// <summary>
        /// Reads device status and optionally the complete bounded measurement inventory.
        /// </summary>
        [McpServerTool(Name = "wmtp_read_peripheral", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Reads a wireless peripheral. Returns status and optional measurements with UA types, quality, " +
            "timestamps, units and absence. Use valueNodeId with machinetool_observe_values; " +
            "object nodeId configures the sensor. Overflow fails; no writes.")]
        public static Task<CallToolResult> ReadPeripheralAsync(
            MachineToolClientAccessor accessor,
            [Description("Wireless peripheral NodeId from machinetool_list_devices.")]
            [StringLength(1024, MinimumLength = 1)] string peripheralNodeId,
            [Description("Include measurements; false reads status only.")]
            bool includeMeasurements = true,
            [Description("Whole sensor inventory limit; overflow fails.")]
            [Range(1, 256)] int maximumMeasurements = 32,
            [Description("Whole UTF-8 reply byte limit; overflow fails.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Local deadline in seconds; never sends wire Abort.")]
            [Range(1, 60)] int timeoutSeconds = 30,
            [Description("Connected session name; required when several are connected.")]
            [StringLength(128, MinimumLength = 1)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return WmtpMcpJson.ExecuteAsync(async token =>
            {
                NodeId node = WmtpMcpParameters.Target(peripheralNodeId, sessionName);
                MachineToolParameters.Bound(maximumMeasurements, 1, 256);
                MachineToolParameters.ResponseLimit(maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient owner = accessor.CreateClient(sessionName);
                await using var lifetime = owner.ConfigureAwait(false);
                WmtpPeripheralClient peripheral = owner.GetWirelessPeripheral(node);
                WmtpPeripheralSnapshot snapshot = await peripheral.ReadSnapshotAsync(token).ConfigureAwait(false);
                JsonObject result = WmtpMcpJson.Snapshot(owner, snapshot);
                if (includeMeasurements)
                {
                    ArrayOf<WmtpMeasurementReading> readings =
                        await peripheral.ReadMeasurementsAsync(maximumMeasurements, token).ConfigureAwait(false);
                    result["measurements"] = MachineToolJson.Array(readings,
                        reading => WmtpMcpJson.Measurement(owner, reading));
                }
                result["measurementsIncluded"] = includeMeasurements;
                return MachineToolJson.Result(result, maximumResponseBytes);
            }, timeoutSeconds, ct);
        }

        /// <summary>
        /// Counts either all stored records or one inclusive acquisition-time interval.
        /// </summary>
        [McpServerTool(Name = "wmtp_count_records", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Counts retained work/service records without consuming them. Returns the selected count, " +
            "not complete history. Supports all or inclusive UTC-time selection, not index/first/last.")]
        public static Task<CallToolResult> CountRecordsAsync(
            MachineToolClientAccessor accessor,
            [Description("Wireless peripheral NodeId from machinetool_list_devices.")]
            [StringLength(1024, MinimumLength = 1)] string peripheralNodeId,
            [Description("Independent work or service record store.")]
            [StringLength(7, MinimumLength = 4)] string dataset,
            [Description("Native all/time selection; ranges use fromTime/toTime UTC strings ending Z.")]
            JsonElement query,
            [Description("Local deadline in seconds; never sends wire Abort.")]
            [Range(1, 60)] int timeoutSeconds = 30,
            [Description("Connected session name; required when several are connected.")]
            [StringLength(128, MinimumLength = 1)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return WmtpMcpJson.ExecuteAsync(async token =>
            {
                NodeId node = WmtpMcpParameters.Target(peripheralNodeId, sessionName);
                bool work = WmtpMcpParameters.WorkDataset(dataset);
                WmtpRecordQuery selection = WmtpMcpParameters.Query(query, allowIndex: false, allowEdges: false);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient owner = accessor.CreateClient(sessionName);
                await using var lifetime = owner.ConfigureAwait(false);
                WmtpRecordClient records = await RecordsAsync(owner, node, work, token).ConfigureAwait(false);
                uint count = await records.CountAsync(selection, token).ConfigureAwait(false);
                return MachineToolJson.Result(new JsonObject
                {
                    ["nodeId"] = node.ToString(),
                    ["dataset"] = dataset,
                    ["selection"] = selection.Selection.ToString(),
                    ["count"] = count,
                    ["consumed"] = false,
                    ["completeHardwareHistory"] = false
                }, 16384);
            }, timeoutSeconds, ct);
        }

        /// <summary>
        /// Copies the whole explicit record selection or fails at the record or byte limit.
        /// </summary>
        [McpServerTool(Name = "wmtp_report_records", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Copies stored work/service records. Returns the whole all/time/index/first/last selection " +
            "and original metadata; no consumption or complete-history claim. Bounds fail, never truncate. " +
            "Ranges are inclusive and first/last follow stored order.")]
        public static Task<CallToolResult> ReportRecordsAsync(
            MachineToolClientAccessor accessor,
            [Description("Wireless peripheral NodeId from machinetool_list_devices.")]
            [StringLength(1024, MinimumLength = 1)] string peripheralNodeId,
            [Description("Independent work or service record store.")]
            [StringLength(7, MinimumLength = 4)] string dataset,
            [Description("Native all/first/last/index/time selection with UInt32 or UTC endpoints.")]
            JsonElement query,
            [Description("Whole selected report limit; overflow fails.")]
            [Range(1, 4096)] int maximumRecords = 100,
            [Description("Whole UTF-8 reply byte limit; overflow fails.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Local deadline in seconds; never sends wire Abort.")]
            [Range(1, 60)] int timeoutSeconds = 30,
            [Description("Connected session name; required when several are connected.")]
            [StringLength(128, MinimumLength = 1)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return WmtpMcpJson.ExecuteAsync(async token =>
            {
                NodeId node = WmtpMcpParameters.Target(peripheralNodeId, sessionName);
                bool work = WmtpMcpParameters.WorkDataset(dataset);
                WmtpRecordQuery selection = WmtpMcpParameters.Query(query, allowIndex: true, allowEdges: true);
                MachineToolParameters.Bound(maximumRecords, 1, 4096);
                MachineToolParameters.ResponseLimit(maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient owner = accessor.CreateClient(sessionName);
                await using var lifetime = owner.ConfigureAwait(false);
                WmtpRecordClient records = await RecordsAsync(owner, node, work, token).ConfigureAwait(false);
                ArrayOf<WMTPOutputDataType> report =
                    await records.ReadAsync(selection, maximumRecords, token).ConfigureAwait(false);
                return MachineToolJson.Result(WmtpMcpJson.Report(owner, node, dataset, selection.Selection, report),
                    maximumResponseBytes);
            }, timeoutSeconds, ct);
        }

        /// <summary>
        /// Configures one exact measurement setting through its generated proxy after explicit consent.
        /// </summary>
        [McpServerTool(Name = "wmtp_configure_measurement", ReadOnly = false, Destructive = true,
            Idempotent = false, OpenWorld = true)]
        [Description("Configures a peripheral measurement trigger or typed delta. Returns method acceptance, " +
            "not physical verification. Both settings cannot be zero; the server rejects type/precision loss. " +
            "No implicit abort, retry or compensation.")]
        public static Task<CallToolResult> ConfigureMeasurementAsync(
            MachineToolClientAccessor accessor,
            [Description("Wireless peripheral NodeId containing this measurement.")]
            [StringLength(1024, MinimumLength = 1)] string peripheralNodeId,
            [Description("Measurement object nodeId from wmtp_read_peripheral, not valueNodeId.")]
            [StringLength(1024, MinimumLength = 1)] string measurementNodeId,
            [Description("Select the trigger interval or typed delta threshold.")]
            [StringLength(7, MinimumLength = 5)] string setting,
            [Description("Trigger: UInt32 milliseconds (wire Double). Delta: {type,value}, " +
                "e.g. {\"type\":\"UInt64\",\"value\":\"8\"}. Must match the sensor type; Decimal is unsupported.")]
            JsonElement value,
            [Description("Explicit consent; server authorization still applies.")]
            bool confirm = false,
            [Description("Local deadline in seconds; never sends wire Abort.")]
            [Range(1, 60)] int timeoutSeconds = 30,
            [Description("Connected session name; required when several are connected.")]
            [StringLength(128, MinimumLength = 1)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return WmtpMcpJson.ExecuteAsync(async token =>
            {
                NodeId node = WmtpMcpParameters.Target(peripheralNodeId, sessionName);
                NodeId measurement = MachineToolParameters.Node(measurementNodeId);
                (double trigger, Variant delta) = WmtpMcpParameters.MeasurementSetting(setting, value);
                MachineToolParameters.Consent(confirm);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient owner = accessor.CreateClient(sessionName);
                await using var lifetime = owner.ConfigureAwait(false);
                WmtpPeripheralClient peripheral = owner.GetWirelessPeripheral(node);
                ArrayOf<WmtpMeasurementReading> readings =
                    await peripheral.ReadMeasurementsAsync(256, token).ConfigureAwait(false);
                bool found = false;
                foreach (WmtpMeasurementReading reading in readings)
                {
                    found |= reading.NodeId == measurement;
                }
                if (!found)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }
                WMTPMeasurementTypeClient proxy = peripheral.GetMeasurementProxy(measurement);
                if (setting == "trigger")
                {
                    await proxy.SetTriggerSettingsAsync(trigger, token).ConfigureAwait(false);
                }
                else
                {
                    await proxy.SetDeltaConditionAsync(delta, token).ConfigureAwait(false);
                }
                return WmtpMcpJson.Accepted(measurement, setting);
            }, timeoutSeconds, ct);
        }

        /// <summary>
        /// Requests calibration or explicit UTC time and TimeZoneDataType through the typed peripheral workflow.
        /// </summary>
        [McpServerTool(Name = "wmtp_configure_peripheral", ReadOnly = false, Destructive = true,
            Idempotent = false, OpenWorld = true)]
        [Description("Configures a wireless peripheral's calibration or UTC clock with TimeZoneDataType. " +
            "Returns provider acceptance, not physical verification. Requires confirmation and server authorization; " +
            "never uses the host time zone.")]
        public static Task<CallToolResult> ConfigurePeripheralAsync(
            MachineToolClientAccessor accessor,
            [Description("Wireless peripheral NodeId from machinetool_list_devices.")]
            [StringLength(1024, MinimumLength = 1)] string peripheralNodeId,
            [Description("Select peripheral calibration or UTC clock configuration.")]
            [StringLength(11, MinimumLength = 4)] string setting,
            [Description("Calibration: Boolean. Time: {utcTime,timeZone:{offset,daylightSavingInOffset}}; " +
                "UTC ends in Z and offset is minutes. All fields are required; no extras.")]
            JsonElement value,
            [Description("Explicit consent; server authorization still applies.")]
            bool confirm = false,
            [Description("Local deadline in seconds; never sends wire Abort.")]
            [Range(1, 60)] int timeoutSeconds = 30,
            [Description("Connected session name; required when several are connected.")]
            [StringLength(128, MinimumLength = 1)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return WmtpMcpJson.ExecuteAsync(async token =>
            {
                NodeId node = WmtpMcpParameters.Target(peripheralNodeId, sessionName);
                (bool calibration, DateTimeUtc utc, TimeZoneDataType? zone) =
                    WmtpMcpParameters.PeripheralSetting(setting, value);
                MachineToolParameters.Consent(confirm);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient owner = accessor.CreateClient(sessionName);
                await using var lifetime = owner.ConfigureAwait(false);
                WmtpPeripheralClient peripheral = owner.GetWirelessPeripheral(node);
                if (setting == "calibration")
                {
                    await peripheral.SetCalibrationAsync(calibration, token).ConfigureAwait(false);
                }
                else
                {
                    await peripheral.SetDeviceTimeAsync(utc, zone!, token).ConfigureAwait(false);
                }
                return WmtpMcpJson.Accepted(node, setting);
            }, timeoutSeconds, ct);
        }

        /// <summary>
        /// Deletes only the explicit independent record selection, never as part of a read or cancellation.
        /// </summary>
        [McpServerTool(Name = "wmtp_delete_records", ReadOnly = false, Destructive = true,
            Idempotent = false, OpenWorld = true)]
        [Description("Deletes an explicit work/service record selection. Returns acceptance; Boolean refusal and " +
            "UA errors fail. Supports all or inclusive index/time ranges, not first/last. " +
            "Confirmation never bypasses server policy; no retry or compensation.")]
        public static Task<CallToolResult> DeleteRecordsAsync(
            MachineToolClientAccessor accessor,
            [Description("Wireless peripheral NodeId from machinetool_list_devices.")]
            [StringLength(1024, MinimumLength = 1)] string peripheralNodeId,
            [Description("Independent work or service record store.")]
            [StringLength(7, MinimumLength = 4)] string dataset,
            [Description("Native all/index/time selection with inclusive UInt32 or UTC endpoints.")]
            JsonElement query,
            [Description("Explicit deletion consent; server authorization still applies.")]
            bool confirm = false,
            [Description("Local deadline in seconds; never sends wire Abort.")]
            [Range(1, 60)] int timeoutSeconds = 30,
            [Description("Connected session name; required when several are connected.")]
            [StringLength(128, MinimumLength = 1)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return WmtpMcpJson.ExecuteAsync(async token =>
            {
                NodeId node = WmtpMcpParameters.Target(peripheralNodeId, sessionName);
                bool work = WmtpMcpParameters.WorkDataset(dataset);
                WmtpRecordQuery selection = WmtpMcpParameters.Query(query, allowIndex: true, allowEdges: false);
                MachineToolParameters.Consent(confirm);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient owner = accessor.CreateClient(sessionName);
                await using var lifetime = owner.ConfigureAwait(false);
                WmtpRecordClient records = await RecordsAsync(owner, node, work, token).ConfigureAwait(false);
                await records.DeleteAsync(selection, token).ConfigureAwait(false);
                return WmtpMcpJson.Accepted(node, "delete", dataset);
            }, timeoutSeconds, ct);
        }

        /// <summary>
        /// Explicitly invokes the selected record proxy's peripheral-scoped wire abort.
        /// </summary>
        [McpServerTool(Name = "wmtp_abort_operation", ReadOnly = false, Destructive = true,
            Idempotent = false, OpenWorld = true)]
        [Description("Aborts the selected peripheral's active operation. Returns method acceptance. " +
            "The effect is peripheral-wide and may affect another caller only if server policy permits. " +
            "Other tools never invoke this implicitly.")]
        public static Task<CallToolResult> AbortOperationAsync(
            MachineToolClientAccessor accessor,
            [Description("Wireless peripheral NodeId from machinetool_list_devices.")]
            [StringLength(1024, MinimumLength = 1)] string peripheralNodeId,
            [Description("Work/service object exposing AbortOperation; scope is peripheral-wide.")]
            [StringLength(7, MinimumLength = 4)] string dataset,
            [Description("Explicit wire-abort consent; server authorization still applies.")]
            bool confirm = false,
            [Description("Local deadline in seconds; never sends an extra abort or retry.")]
            [Range(1, 60)] int timeoutSeconds = 30,
            [Description("Connected session name; required when several are connected.")]
            [StringLength(128, MinimumLength = 1)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return WmtpMcpJson.ExecuteAsync(async token =>
            {
                NodeId node = WmtpMcpParameters.Target(peripheralNodeId, sessionName);
                bool work = WmtpMcpParameters.WorkDataset(dataset);
                MachineToolParameters.Consent(confirm);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient owner = accessor.CreateClient(sessionName);
                await using var lifetime = owner.ConfigureAwait(false);
                WmtpRecordClient records = await RecordsAsync(owner, node, work, token).ConfigureAwait(false);
                await records.AbortAsync(token).ConfigureAwait(false);
                return WmtpMcpJson.Accepted(node, "peripheral-wide abort", dataset);
            }, timeoutSeconds, ct);
        }

        /// <summary>
        /// Transfers bounded explicit bytes, clearing the call-owned copy after provider completion or failure.
        /// </summary>
        [McpServerTool(Name = "wmtp_transfer", ReadOnly = false, Destructive = true,
            Idempotent = false, OpenWorld = true)]
        [Description("Transfers firmware/license bytes. Returns provider acceptance and byte count, " +
            "not installation, activation or validity. No paths/URLs or payload echo; owned byte copies are cleared. " +
            "Confirmation and server authorization are required; no automatic retry or compensation.")]
        public static Task<CallToolResult> TransferAsync(
            MachineToolClientAccessor accessor,
            [Description("Wireless peripheral NodeId from machinetool_list_devices.")]
            [StringLength(1024, MinimumLength = 1)] string peripheralNodeId,
            [Description("Firmware or license transfer purpose.")]
            [StringLength(8, MinimumLength = 7)] string kind,
            [Description("Nonempty padded base64 without whitespace, bounded by maximumBytes; no paths/URLs.")]
            [StringLength(1398104, MinimumLength = 4)] string payloadBase64,
            [Description("Decoded transfer byte limit, checked before session access.")]
            [Range(1, 1048576)] int maximumBytes = 1048576,
            [Description("Explicit transfer consent; server authorization still applies.")]
            bool confirm = false,
            [Description("Local deadline in seconds; never sends wire Abort.")]
            [Range(1, 60)] int timeoutSeconds = 30,
            [Description("Connected session name; required when several are connected.")]
            [StringLength(128, MinimumLength = 1)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return WmtpMcpJson.ExecuteAsync(async token =>
            {
                NodeId node = WmtpMcpParameters.Target(peripheralNodeId, sessionName);
                WmtpTransferKind transfer = WmtpMcpParameters.TransferKind(kind);
                MachineToolParameters.Bound(maximumBytes, 1, 1048576);
                MachineToolParameters.Consent(confirm);
                byte[] copy = DecodePayload(payloadBase64, maximumBytes);
                try
                {
                    ArgumentNullException.ThrowIfNull(accessor);
                    MachineToolClient owner = accessor.CreateClient(sessionName);
                    await using var lifetime = owner.ConfigureAwait(false);
                    await owner.GetWirelessPeripheral(node).TransferAsync(
                        transfer, ByteString.From(copy), maximumBytes, token).ConfigureAwait(false);
                    return MachineToolJson.Result(new JsonObject
                    {
                        ["nodeId"] = node.ToString(),
                        ["kind"] = kind,
                        ["acceptedByProvider"] = true,
                        ["transferredBytes"] = copy.Length,
                        ["installationVerified"] = false,
                        ["activationVerified"] = false
                    }, 16384);
                }
                finally
                {
                    Array.Clear(copy, 0, copy.Length);
                }
            }, timeoutSeconds, ct);
        }

        /// <summary>
        /// Selects an independent dataset through the existing typed client rather than constructing UA calls.
        /// </summary>
        private static ValueTask<WmtpRecordClient> RecordsAsync(
            MachineToolClient owner, NodeId peripheral, bool work, CancellationToken cancellationToken)
        {
            WmtpPeripheralClient client = owner.GetWirelessPeripheral(peripheral);
            return work ? client.GetWorkRecordsAsync(cancellationToken) :
                client.GetServiceRecordsAsync(cancellationToken);
        }

        /// <summary>
        /// Allocates only the exact, pre-bounded decoded size and never includes input content in diagnostics.
        /// </summary>
        private static byte[] DecodePayload(string payload, int maximumBytes)
        {
            MachineToolParameters.Text(payload, 1398104);
            if (payload.Length % 4 != 0)
            {
                throw new FormatException("A padded base64 payload is required.");
            }
            foreach (char character in payload)
            {
                if (char.IsWhiteSpace(character))
                {
                    throw new FormatException("Whitespace is not permitted in a base64 payload.");
                }
            }
            int padding = payload.EndsWith("==", StringComparison.Ordinal) ? 2 : payload[^1] == '=' ? 1 : 0;
            int length = (payload.Length / 4 * 3) - padding;
            MachineToolParameters.Bound(length, 1, maximumBytes);
            byte[] result = new byte[length];
            if (!Convert.TryFromBase64String(payload, result, out int written) || written != length)
            {
                Array.Clear(result, 0, result.Length);
                throw new FormatException("The base64 payload is invalid.");
            }
            return result;
        }
    }
}
