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
using System.Globalization;
using System.Text.Json;
using Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Validates closed WMTP JSON unions and exact numeric inputs before obtaining a session.
    /// </summary>
    internal static class WmtpMcpParameters
    {
        /// <summary>
        /// Validates the shared-session selection and peripheral identifier without accessing the network.
        /// </summary>
        internal static NodeId Target(string nodeId, string? sessionName)
        {
            if (sessionName != null)
            {
                MachineToolParameters.Text(sessionName, 128);
            }
            return MachineToolParameters.Node(nodeId);
        }

        /// <summary>
        /// Selects one explicit record store without case folding or fallback.
        /// </summary>
        internal static bool WorkDataset(string dataset)
        {
            return dataset switch
            {
                "work" => true,
                "service" => false,
                _ => throw new ArgumentException("The dataset must be work or service.", nameof(dataset))
            };
        }

        /// <summary>
        /// Accepts only the released query shapes appropriate to the requested operation.
        /// </summary>
        internal static WmtpRecordQuery Query(JsonElement value, bool allowIndex, bool allowEdges)
        {
            JsonElement selection = Required(value, "selection");
            string name = Text(selection, 5);
            switch (name)
            {
                case "all":
                case "first" when allowEdges:
                case "last" when allowEdges:
                    Shape(value, ["selection"]);
                    return new WmtpRecordQuery
                    {
                        Selection = name == "all" ? WmtpRecordSelection.All :
                            name == "first" ? WmtpRecordSelection.First : WmtpRecordSelection.Last
                    };
                case "index" when allowIndex:
                    Shape(value, ["selection", "fromIndex", "toIndex"]);
                    uint from = Index(value.GetProperty("fromIndex"));
                    uint to = Index(value.GetProperty("toIndex"));
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(from, to);
                    return new WmtpRecordQuery
                    {
                        Selection = WmtpRecordSelection.Index, FromIndex = from, ToIndex = to
                    };
                case "time":
                    Shape(value, ["selection", "fromTime", "toTime"]);
                    DateTimeUtc start = Utc(value.GetProperty("fromTime"));
                    DateTimeUtc end = Utc(value.GetProperty("toTime"));
                    if (start > end)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value), "The time range is reversed.");
                    }
                    return new WmtpRecordQuery
                    {
                        Selection = WmtpRecordSelection.Time, FromTime = start, ToTime = end
                    };
                default:
                    throw new ArgumentException("The selection is not supported by this operation.", nameof(value));
            }
        }

        /// <summary>
        /// Keeps the trigger's Duration wire type and the delta's explicit Number type distinct.
        /// </summary>
        internal static (double Trigger, Variant Delta) MeasurementSetting(string setting, JsonElement value)
        {
            if (setting == "trigger")
            {
                double duration = NonnegativeNumber(value);
                if (duration > uint.MaxValue || Math.Truncate(duration) != duration)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Whole UInt32 milliseconds are required.");
                }
                return (duration, default);
            }
            if (setting != "delta")
            {
                throw new ArgumentException("The measurement setting must be trigger or delta.", nameof(setting));
            }
            Shape(value, ["type", "value"]);
            string type = Text(value.GetProperty("type"), 6);
            JsonElement number = value.GetProperty("value");
            if (type is "Int64" or "UInt64" && number.ValueKind == JsonValueKind.String)
            {
                string digits = Text(number, 20);
                if (type == "Int64" && long.TryParse(digits, NumberStyles.None,
                    CultureInfo.InvariantCulture, out long signed))
                {
                    return (0, Variant.From(signed));
                }
                if (type == "UInt64" && ulong.TryParse(digits, NumberStyles.None,
                    CultureInfo.InvariantCulture, out ulong unsigned))
                {
                    return (0, Variant.From(unsigned));
                }
                throw new ArgumentOutOfRangeException(nameof(value), "An exact nonnegative integer is required.");
            }
            if (number.ValueKind != JsonValueKind.Number)
            {
                throw new ArgumentException("A native scalar numeric value is required.", nameof(value));
            }
            Variant delta = type switch
            {
                "SByte" when number.TryGetSByte(out sbyte item) && item >= 0 => Variant.From(item),
                "Byte" when number.TryGetByte(out byte item) => Variant.From(item),
                "Int16" when number.TryGetInt16(out short item) && item >= 0 => Variant.From(item),
                "UInt16" when number.TryGetUInt16(out ushort item) => Variant.From(item),
                "Int32" when number.TryGetInt32(out int item) && item >= 0 => Variant.From(item),
                "UInt32" when number.TryGetUInt32(out uint item) => Variant.From(item),
                "Int64" when number.TryGetInt64(out long item) && item >= 0 => Variant.From(item),
                "UInt64" when number.TryGetUInt64(out ulong item) => Variant.From(item),
                "Float" when number.TryGetSingle(out float item) && float.IsFinite(item) && item >= 0 =>
                    Variant.From(item),
                "Double" => Variant.From(NonnegativeNumber(number)),
                _ => throw new ArgumentOutOfRangeException(nameof(value),
                    "The exact nonnegative UA scalar numeric type and value are required; Decimal is not coerced.")
            };
            return (0, delta);
        }

        /// <summary>
        /// Requires the actual TimeZoneDataType fields instead of assuming the host's local time zone.
        /// </summary>
        internal static (bool Calibration, DateTimeUtc Time, TimeZoneDataType? Zone) PeripheralSetting(
            string setting, JsonElement value)
        {
            if (setting == "calibration")
            {
                return (Boolean(value), default, null);
            }
            if (setting != "time")
            {
                throw new ArgumentException("The peripheral setting must be calibration or time.", nameof(setting));
            }
            Shape(value, ["utcTime", "timeZone"]);
            DateTimeUtc utc = Utc(value.GetProperty("utcTime"));
            JsonElement zone = value.GetProperty("timeZone");
            Shape(zone, ["offset", "daylightSavingInOffset"]);
            JsonElement offset = zone.GetProperty("offset");
            if (offset.ValueKind != JsonValueKind.Number || !offset.TryGetInt16(out short minutes) ||
                minutes is < -840 or > 840)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The time-zone offset must be -840..840 minutes.");
            }
            return (false, utc, new TimeZoneDataType
            {
                Offset = minutes,
                DaylightSavingInOffset = Boolean(zone.GetProperty("daylightSavingInOffset"))
            });
        }

        /// <summary>
        /// Rejects unknown transfer kinds rather than guessing a firmware or license operation.
        /// </summary>
        internal static WmtpTransferKind TransferKind(string kind)
        {
            return kind switch
            {
                "firmware" => WmtpTransferKind.Firmware,
                "license" => WmtpTransferKind.License,
                _ => throw new ArgumentException("The transfer kind must be firmware or license.", nameof(kind))
            };
        }

        /// <summary>
        /// Rejects oversized, missing, duplicate and extraneous properties in native JSON objects.
        /// </summary>
        private static void Shape(JsonElement value, ArrayOf<string> fields)
        {
            RequireObject(value);
            var remaining = new HashSet<string>(StringComparer.Ordinal);
            foreach (string field in fields)
            {
                remaining.Add(field);
            }
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!remaining.Remove(property.Name))
                {
                    throw new ArgumentException("Duplicate or extraneous JSON properties are not accepted.");
                }
            }
            if (remaining.Count != 0)
            {
                throw new ArgumentException("Every property for this JSON selection is required.");
            }
        }

        /// <summary>
        /// Validates the object size before traversing its union discriminator.
        /// </summary>
        private static void RequireObject(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object || value.GetRawText().Length > 4096)
            {
                throw new ArgumentException("A native JSON object of at most 4096 characters is required.");
            }
        }

        /// <summary>
        /// Obtains a required property without leaking its input value in exception messages.
        /// </summary>
        private static JsonElement Required(JsonElement value, string name)
        {
            RequireObject(value);
            return value.TryGetProperty(name, out JsonElement result) ? result :
                throw new ArgumentException("The selection property is required.");
        }

        /// <summary>
        /// Accepts a bounded native JSON string rather than converting numbers or nulls to text.
        /// </summary>
        private static string Text(JsonElement value, int maximumLength)
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException("A native JSON string is required.");
            }
            string text = value.GetString()!;
            MachineToolParameters.Text(text, maximumLength);
            return text;
        }

        /// <summary>
        /// Parses UTC without culture-sensitive or machine-local date inference.
        /// </summary>
        private static DateTimeUtc Utc(JsonElement value)
        {
            string text = Text(value, 32);
            if (!text.EndsWith('Z') || !value.TryGetDateTimeOffset(out DateTimeOffset time))
            {
                throw new ArgumentException("An ISO 8601 UTC timestamp ending in Z is required.");
            }
            DateTimeUtc utc = time;
            if (utc.IsNull)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "An initialized OPC UA timestamp is required.");
            }
            return utc;
        }

        /// <summary>
        /// Reads an exact UInt32 index, never a rounded floating-point range.
        /// </summary>
        private static uint Index(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint index) ? index :
                throw new ArgumentOutOfRangeException(nameof(value), "An exact UInt32 index is required.");
        }

        /// <summary>
        /// Requires a native Boolean and rejects numeric/string truthiness.
        /// </summary>
        private static bool Boolean(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new ArgumentException("A native JSON Boolean is required.")
            };
        }

        /// <summary>
        /// Reads an explicitly floating-point wire value without accepting infinities or negative thresholds.
        /// </summary>
        private static double NonnegativeNumber(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.Number && value.GetRawText().Length <= 4096 &&
                value.TryGetDouble(out double number) &&
                double.IsFinite(number) && number >= 0 ? number :
                throw new ArgumentOutOfRangeException(nameof(value), "A finite nonnegative JSON number is required.");
        }
    }
}
