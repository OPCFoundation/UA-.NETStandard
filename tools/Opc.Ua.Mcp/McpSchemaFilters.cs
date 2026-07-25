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
                    tool.InputSchema = JsonSerializer.SerializeToElement(schema);
                }

                return result;
            };
        }
    }
}
