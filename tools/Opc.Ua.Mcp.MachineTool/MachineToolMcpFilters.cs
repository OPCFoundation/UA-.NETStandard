// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Describes the actual JSON array boundary without leaking the OPC UA collection representation.
    /// </summary>
    internal static class MachineToolMcpFilters
    {
        /// <summary>
        /// Adds exact size and uniqueness constraints to the explicit variable selection.
        /// </summary>
        internal static McpRequestHandler<ListToolsRequestParams, ListToolsResult> AddSchemas(
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);
            return async (request, token) =>
            {
                ListToolsResult result = await next(request, token).ConfigureAwait(false);
                foreach (Tool tool in result.Tools)
                {
                    if (tool.Name == "machinetool_observe_values" &&
                        JsonNode.Parse(tool.InputSchema.GetRawText()) is JsonObject schema &&
                        schema["properties"] is JsonObject properties)
                    {
                        properties["nodeIds"] = new JsonObject
                        {
                            ["description"] = "One through 256 distinct, non-null native variable NodeIds " +
                                "from MachineTool or WMTP discovery and reads.",
                            ["type"] = "array",
                            ["minItems"] = 1,
                            ["maxItems"] = 256,
                            ["uniqueItems"] = true,
                            ["items"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["minLength"] = 1,
                                ["maxLength"] = 1024
                            }
                        };
                        using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
                        tool.InputSchema = document.RootElement.Clone();
                    }
                }
                return result;
            };
        }
    }
}
