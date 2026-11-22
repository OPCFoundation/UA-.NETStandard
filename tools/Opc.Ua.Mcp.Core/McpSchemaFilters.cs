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
    /// Normalizes generated MCP tool schemas for explicit client behavior.
    /// </summary>
    internal static class McpSchemaFilters
    {
        /// <summary>
        /// Adds an empty required array to schemas whose parameters are all optional.
        /// </summary>
        public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> AddExplicitRequiredArrays(
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return async (request, ct) =>
            {
                ListToolsResult result = await next(request, ct).ConfigureAwait(false);

                foreach (Tool tool in result.Tools)
                {
                    JsonElement inputSchema = tool.InputSchema;
                    if (inputSchema.ValueKind != JsonValueKind.Object ||
                        inputSchema.TryGetProperty("required", out _))
                    {
                        continue;
                    }

                    JsonObject? schema = JsonNode.Parse(inputSchema.GetRawText()) as JsonObject;
                    if (schema == null)
                    {
                        continue;
                    }

                    schema["required"] = new JsonArray();

                    // JsonSerializer.SerializeToElement would reflect over JsonObject; parsing
                    // the node's own text is reflection-free. Clone detaches the element from
                    // the document so it stays valid after disposal.
                    using (JsonDocument document = JsonDocument.Parse(schema.ToJsonString()))
                    {
                        tool.InputSchema = document.RootElement.Clone();
                    }
                }

                return result;
            };
        }
    }
}
