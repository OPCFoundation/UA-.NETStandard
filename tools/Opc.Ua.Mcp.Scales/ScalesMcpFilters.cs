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
