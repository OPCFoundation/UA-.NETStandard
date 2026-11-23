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
    /// Advertises the concrete JSON shape consumed by the bounded predecessor converter.
    /// </summary>
    internal static class ScalesMcpFilters
    {
        /// <summary>
        /// Adds the recipe predecessor schema without exposing ArrayOf internals.
        /// </summary>
        public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> AddRecipeSchema(
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);
            return async (request, ct) =>
            {
                ListToolsResult result = await next(request, ct).ConfigureAwait(false);
                foreach (Tool tool in result.Tools)
                {
                    if (tool.Name != "scales_add_recipe_element")
                    {
                        continue;
                    }
                    JsonNode schema = JsonNode.Parse(tool.InputSchema.GetRawText()) ??
                        throw new InvalidOperationException("Missing scale recipe schema.");
                    JsonObject properties = schema["properties"]?["input"]?["properties"] as JsonObject ??
                        throw new InvalidOperationException("Missing scale recipe input properties.");
                    properties["previousElements"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string" },
                        ["minItems"] = 1,
                        ["maxItems"] = 500,
                        ["description"] = "Predecessor NodeIds; use the recipe NodeId for a starting element."
                    };
                    using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
                    tool.InputSchema = document.RootElement.Clone();
                }
                return result;
            };
        }
    }
}
