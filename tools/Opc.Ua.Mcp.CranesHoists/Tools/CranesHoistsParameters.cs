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
using System.Collections.Generic;
using System.Text.Json;
using Opc.Ua.CranesHoists;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Validates the JSON boundary before a typed client can allocate resources or write configuration.
    /// </summary>
    internal static class CranesHoistsParameters
    {
        /// <summary>
        /// Parses a bounded non-null NodeId using the shared OPC UA parser.
        /// </summary>
        internal static NodeId Node(string nodeId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(nodeId.Length, 1024);
            NodeId parsed = OpcUaJsonHelper.ParseNodeId(nodeId);
            if (parsed.IsNull)
            {
                throw new ArgumentException("A non-null NodeId is required.", nameof(nodeId));
            }
            return parsed;
        }

        /// <summary>
        /// Converts a JSON array to ArrayOf without exposing its backing-memory representation in the MCP schema.
        /// </summary>
        internal static ArrayOf<NodeId> Nodes(JsonElement nodeIds)
        {
            if (nodeIds.ValueKind != JsonValueKind.Array || nodeIds.GetArrayLength() is < 1 or > 64)
            {
                throw new ArgumentException("nodeIds must be a JSON array containing 1-64 NodeId strings.",
                    nameof(nodeIds));
            }
            var values = new List<NodeId>(nodeIds.GetArrayLength());
            var seen = new HashSet<NodeId>();
            foreach (JsonElement element in nodeIds.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    throw new ArgumentException("Every nodeIds entry must be a NodeId string.", nameof(nodeIds));
                }
                NodeId parsed = Node(element.GetString()!);
                if (!seen.Add(parsed))
                {
                    throw new ArgumentException("Monitored NodeIds must be distinct.", nameof(nodeIds));
                }
                values.Add(parsed);
            }
            return values.ToArrayOf();
        }

        /// <summary>
        /// Bounds the serialized response independently of the decoded payload budget.
        /// </summary>
        internal static void ResponseLimit(int maximumResponseBytes)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumResponseBytes, 1024);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumResponseBytes, MaximumResponseBytes);
        }

        /// <summary>
        /// Reuses the typed client's window, notification and decoded-payload validation.
        /// </summary>
        internal static CraneObservationOptions Observation(int seconds, int notifications, int maximumResponseBytes)
        {
            ResponseLimit(maximumResponseBytes);
            var options = new CraneObservationOptions
            {
                Window = TimeSpan.FromSeconds(seconds),
                MaximumNotifications = notifications,
                MaximumPayloadBytes = maximumResponseBytes
            };
            options.Validate();
            return options;
        }

        /// <summary>
        /// Selects only the five standard writable requests, enforcing exact names and native wire types.
        /// </summary>
        internal static (CraneSignal Signal, Variant Value) Limit(string signal, JsonElement value)
        {
            CraneSignal selected = signal switch
            {
                "SpeedLimitDirMinusSetpoint" => CraneSignal.SpeedLimitDirMinusSetpoint,
                "SpeedLimitDirPlusSetpoint" => CraneSignal.SpeedLimitDirPlusSetpoint,
                "SpeedLimitEnabledDirMinus" => CraneSignal.SpeedLimitEnabledDirMinus,
                "SpeedLimitEnabledDirPlus" => CraneSignal.SpeedLimitEnabledDirPlus,
                "SpeedLimitGlobalEnabled" => CraneSignal.SpeedLimitGlobalEnabled,
                _ => throw new ArgumentException("Select an exact standard limit-request name.", nameof(signal))
            };
            if (selected is CraneSignal.SpeedLimitDirMinusSetpoint or CraneSignal.SpeedLimitDirPlusSetpoint)
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) ||
                    !double.IsFinite(number) || number is < 0 or > 100)
                {
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "A setpoint must be a finite JSON number in the inclusive range 0-100.");
                }
                return (selected, Variant.From(number));
            }
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new ArgumentException("An enable request requires a JSON Boolean.", nameof(value));
            }
            return (selected, Variant.From(value.GetBoolean()));
        }

        /// <summary>The absolute serialized response cap, shared by all crane tools.</summary>
        internal const int MaximumResponseBytes = 1048576;
    }
}
