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
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Describes the closed native JSON unions without reflection-based serialization or ArrayOf internals.
    /// </summary>
    internal static class WmtpMcpFilters
    {
        /// <summary>
        /// Enriches the bundled WMTP catalog; the owning package registers this filter once.
        /// </summary>
        internal static McpRequestHandler<ListToolsRequestParams, ListToolsResult> AddSchemas(
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);
            return async (request, ct) =>
            {
                ListToolsResult result = await next(request, ct).ConfigureAwait(false);
                Apply(result.Tools);
                return result;
            };
        }

        /// <summary>
        /// Changes only known WMTP schemas, leaving other selected families untouched.
        /// </summary>
        internal static void Apply(IEnumerable<Tool> tools)
        {
            foreach (Tool tool in tools)
            {
                if (!tool.Name.StartsWith("wmtp_", StringComparison.Ordinal) ||
                    JsonNode.Parse(tool.InputSchema.GetRawText()) is not JsonObject schema ||
                    schema["properties"] is not JsonObject properties)
                {
                    continue;
                }
                if (properties["dataset"] is JsonObject dataset)
                {
                    dataset["enum"] = new JsonArray("work", "service");
                }
                switch (tool.Name)
                {
                    case "wmtp_count_records":
                    case "wmtp_report_records":
                    case "wmtp_delete_records":
                        properties["query"] = QuerySchema(tool.Name != "wmtp_count_records",
                            tool.Name == "wmtp_report_records");
                        break;
                    case "wmtp_configure_measurement":
                        properties["setting"]!["enum"] = new JsonArray("trigger", "delta");
                        JsonObject measurement = NumberSchema();
                        measurement["type"] = new JsonArray("integer", "object");
                        measurement["minimum"] = 0;
                        measurement["maximum"] = uint.MaxValue;
                        ConfigureValue(properties, measurement);
                        break;
                    case "wmtp_configure_peripheral":
                        properties["setting"]!["enum"] = new JsonArray("calibration", "time");
                        JsonObject peripheral = TimeSchema();
                        peripheral["type"] = new JsonArray("boolean", "object");
                        ConfigureValue(properties, peripheral);
                        break;
                    case "wmtp_transfer":
                        properties["kind"]!["enum"] = new JsonArray("firmware", "license");
                        properties["payloadBase64"]!["contentEncoding"] = "base64";
                        properties["payloadBase64"]!["pattern"] =
                            "^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$";
                        break;
                }
                using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
                tool.InputSchema = document.RootElement.Clone();
            }
        }

        /// <summary>
        /// Keeps count, range reports, edge reports and deletion observably distinct.
        /// </summary>
        private static JsonObject QuerySchema(bool index, bool edges)
        {
            var alternatives = new JsonArray(Selection("all"));
            alternatives.Add((JsonNode)new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray("selection", "fromTime", "toTime"),
                ["properties"] = new JsonObject
                {
                    ["selection"] = new JsonObject { ["const"] = "time" },
                    ["fromTime"] = UtcSchema(),
                    ["toTime"] = UtcSchema()
                }
            });
            if (index)
            {
                alternatives.Add((JsonNode)new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("selection", "fromIndex", "toIndex"),
                    ["properties"] = new JsonObject
                    {
                        ["selection"] = new JsonObject { ["const"] = "index" },
                        ["fromIndex"] = new JsonObject
                        {
                            ["type"] = "integer", ["minimum"] = 0, ["maximum"] = uint.MaxValue
                        },
                        ["toIndex"] = new JsonObject
                        {
                            ["type"] = "integer", ["minimum"] = 0, ["maximum"] = uint.MaxValue
                        }
                    }
                });
            }
            if (edges)
            {
                alternatives.Add((JsonNode)Selection("first"));
                alternatives.Add((JsonNode)Selection("last"));
            }
            return new JsonObject
            {
                ["description"] = "Native selection, at most 4096 characters. Ordered, inclusive ranges; " +
                    "missing, duplicate or extra fields fail.",
                ["oneOf"] = alternatives
            };
        }

        /// <summary>
        /// Describes a no-range selection rather than accepting and ignoring unused endpoints.
        /// </summary>
        private static JsonObject Selection(string selection)
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray("selection"),
                ["properties"] = new JsonObject { ["selection"] = new JsonObject { ["const"] = selection } }
            };
        }

        /// <summary>
        /// Describes explicitly UTC timestamps without accepting local or offset date inference.
        /// </summary>
        private static JsonObject UtcSchema()
        {
            return new JsonObject
            {
                ["type"] = "string", ["format"] = "date-time", ["maxLength"] = 32, ["pattern"] = "Z$"
            };
        }

        /// <summary>
        /// Adds each native shape once; unchanged runtime validation enforces its setting and exact numeric width.
        /// </summary>
        private static void ConfigureValue(JsonObject properties, JsonObject value)
        {
            value["description"] = properties["value"]?["description"]?.DeepClone();
            properties["value"] = value;
        }

        /// <summary>
        /// Documents the actual TimeZoneDataType, including mandatory daylight-saving semantics.
        /// </summary>
        private static JsonObject TimeSchema()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray("utcTime", "timeZone"),
                ["properties"] = new JsonObject
                {
                    ["utcTime"] = UtcSchema(),
                    ["timeZone"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray("offset", "daylightSavingInOffset"),
                        ["properties"] = new JsonObject
                        {
                            ["offset"] = new JsonObject
                            {
                                ["type"] = "integer", ["minimum"] = -840, ["maximum"] = 840
                            },
                            ["daylightSavingInOffset"] = new JsonObject { ["type"] = "boolean" }
                        }
                    }
                }
            };
        }

        /// <summary>
        /// Declares supported numeric widths and exact digit strings without repeating the object for every type.
        /// </summary>
        private static JsonObject NumberSchema()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray("type", "value"),
                ["properties"] = new JsonObject
                {
                    ["type"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray(
                            "SByte", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Float", "Double")
                    },
                    ["value"] = new JsonObject
                    {
                        ["type"] = new JsonArray("number", "string"),
                        ["minimum"] = 0,
                        ["maximum"] = double.MaxValue,
                        ["pattern"] = "^[0-9]+$",
                        ["minLength"] = 1,
                        ["maxLength"] = 20,
                        ["description"] = "Nonnegative scalar matching type; only Int64/UInt64 accept digit strings."
                    }
                }
            };
        }
    }
}
