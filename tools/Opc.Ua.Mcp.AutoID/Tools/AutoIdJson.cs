/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Text.Json.Nodes;
using Opc.Ua.AutoID;
using Opc.Ua.AutoID.Client;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Explicit projections of AutoID records, preserving typed UA values without reflection.
    /// </summary>
    internal static class AutoIdJson
    {
        /// <summary>
        /// Projects a discovered reader.
        /// </summary>
        internal static JsonObject Entry(AutoIdReaderEntry entry)
        {
            return new JsonObject
            {
                ["nodeId"] = entry.NodeId.ToString(),
                ["browseName"] = entry.BrowseName.ToString(),
                ["displayName"] = entry.DisplayName,
                ["typeDefinitionId"] = entry.TypeDefinitionId.ToString(),
                ["kind"] = entry.Kind.ToString()
            };
        }

        /// <summary>
        /// Projects the state of a reader device.
        /// </summary>
        internal static JsonObject Device(AutoIdDeviceSnapshot value, IServiceMessageContext context)
        {
            return new JsonObject
            {
                ["nodeId"] = value.NodeId.ToString(),
                ["deviceStatusId"] = Id(value.DeviceStatusId),
                ["lastScanDataId"] = Id(value.LastScanDataId),
                ["lastScanTimestampId"] = Id(value.LastScanTimestampId),
                ["deviceLocationId"] = Id(value.DeviceLocationId),
                ["methods"] = Methods(value.Methods),
                ["deviceName"] = value.DeviceName,
                ["deviceStatus"] = value.DeviceStatus.ToString(),
                ["modelVersion"] = value.ModelVersion,
                ["deviceInfo"] = value.DeviceInfo,
                ["deviceLocationName"] = value.DeviceLocationName,
                ["deviceLocation"] = value.DeviceLocation is null
                    ? null : McpCompanionJson.Encode(value.DeviceLocation, context),
                ["antennaNames"] = Antennas(value.AntennaNames),
                ["lastScanData"] = value.LastScanData.IsNull
                    ? null : McpCompanionJson.Variant(value.LastScanData, context),
                ["lastScanStatusCode"] = value.LastScanStatus.Code,
                ["lastScanTimestamp"] = value.LastScanTimestamp.IsNull ? null : value.LastScanTimestamp.ToDateTime(),
                ["manufacturer"] = Text(value.Manufacturer),
                ["model"] = Text(value.Model),
                ["serialNumber"] = value.SerialNumber
            };
        }

        /// <summary>
        /// Projects the response of a synchronous scan.
        /// </summary>
        internal static JsonObject Scan(AutoIdScanResponse response, IServiceMessageContext context)
        {
            var results = new JsonArray();
            foreach (ScanResult result in response.Results)
            {
                results.Add(McpCompanionJson.Encode(result, context));
            }
            return new JsonObject
            {
                ["status"] = response.Status.ToString(),
                ["results"] = results
            };
        }

        /// <summary>
        /// Projects a scan event.
        /// </summary>
        internal static JsonObject ScanEvent(AutoIdScanEventTypeRecord record, IServiceMessageContext context)
        {
            var results = new JsonArray();
            if (record.ScanResult is { } scanResults)
            {
                foreach (ScanResult result in scanResults)
                {
                    results.Add(McpCompanionJson.Encode(result, context));
                }
            }
            return new JsonObject
            {
                ["eventType"] = record.EventType.ToString(),
                ["time"] = record.Time,
                ["deviceName"] = record.DeviceName,
                ["results"] = results
            };
        }

        /// <summary>
        /// Projects a tag read.
        /// </summary>
        internal static JsonObject Tag(AutoIdTagReadResponse response)
        {
            return new JsonObject
            {
                ["status"] = response.Status.ToString(),
                ["dataHex"] = response.Data.IsNull ? null : Convert.ToHexString(response.Data.Span)
            };
        }

        /// <summary>
        /// Projects an RTLS location result.
        /// </summary>
        internal static JsonNode? Location(RtlsLocationResult? value, IServiceMessageContext context)
        {
            return value is null ? null : McpCompanionJson.Encode(value, context);
        }

        private static JsonArray Antennas(ArrayOf<AntennaNameIdPair> antennas)
        {
            var items = new JsonArray();
            foreach (AntennaNameIdPair pair in antennas)
            {
                items.Add((JsonNode)new JsonObject { ["antennaId"] = pair.AntennaId, ["antennaName"] = pair.AntennaName });
            }
            return items;
        }

        private static string? Id(NodeId value)
        {
            return value.IsNull ? null : value.ToString();
        }

        private static JsonArray Methods(ArrayOf<QualifiedName> methods)
        {
            var items = new JsonArray();
            foreach (QualifiedName method in methods)
            {
                items.Add((JsonNode?)JsonValue.Create(method.Name));
            }
            return items;
        }

        private static JsonObject? Text(LocalizedText value)
        {
            return value.IsNull ? null : new JsonObject { ["text"] = value.Text, ["locale"] = value.Locale };
        }
    }
}
