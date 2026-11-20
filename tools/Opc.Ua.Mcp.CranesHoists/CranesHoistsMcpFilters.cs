// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Describes the small JSON union and array boundaries without exposing ArrayOf backing-memory details.
    /// </summary>
    internal static class CranesHoistsMcpFilters
    {
        /// <summary>
        /// Adds exact request-name, native-value and bounded NodeId-array schemas to the listed tools.
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
        /// Enriches only crane schemas, leaving nonselected package tools unchanged.
        /// </summary>
        internal static void Apply(IEnumerable<Tool> tools)
        {
            foreach (Tool tool in tools)
            {
                if (tool.Name is not ("craneshoists_observe_values" or "craneshoists_configure_limit") ||
                    JsonNode.Parse(tool.InputSchema.GetRawText()) is not JsonObject schema ||
                    schema["properties"] is not JsonObject properties)
                {
                    continue;
                }
                if (tool.Name == "craneshoists_observe_values")
                {
                    properties["nodeIds"] = new JsonObject
                    {
                        ["description"] = "Array of 1-64 distinct signal NodeIds from crane snapshots; " +
                            "no null NodeIds.",
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["maxItems"] = 64,
                        ["uniqueItems"] = true,
                        ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024 }
                    };
                }
                else
                {
                    var names = new JsonArray(
                        "SpeedLimitDirMinusSetpoint", "SpeedLimitDirPlusSetpoint",
                        "SpeedLimitEnabledDirMinus", "SpeedLimitEnabledDirPlus", "SpeedLimitGlobalEnabled");
                    properties["signal"]!["enum"] = names;
                    properties["value"] = new JsonObject
                    {
                        ["description"] = "Finite JSON number 0-100 for setpoints; JSON Boolean for enable requests.",
                        ["anyOf"] = new JsonArray(
                            new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 100 },
                            new JsonObject { ["type"] = "boolean" })
                    };
                    schema["allOf"] = JsonNode.Parse("""
                        [
                          {
                            "if": {
                              "properties": {
                                "signal": {
                                  "enum": ["SpeedLimitDirMinusSetpoint", "SpeedLimitDirPlusSetpoint"]
                                }
                              }
                            },
                            "then": {
                              "properties": {
                                "value": { "type": "number", "minimum": 0, "maximum": 100 }
                              }
                            },
                            "else": {
                              "properties": { "value": { "type": "boolean" } }
                            }
                          }
                        ]
                        """);
                }
                using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
                tool.InputSchema = document.RootElement.Clone();
            }
        }
    }
}
