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
    /// Advertises the concrete string-array shape of the two DI installation requests.
    /// </summary>
    internal static class DiMcpFilters
    {
        /// <summary>
        /// Adds bounded array schemas without exposing the ArrayOf memory representation.
        /// </summary>
        internal static McpRequestHandler<ListToolsRequestParams, ListToolsResult> AddInstallSchemas(
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);
            return async (request, ct) =>
            {
                ListToolsResult result = await next(request, ct).ConfigureAwait(false);
                foreach (Tool tool in result.Tools)
                {
                    bool package = tool.Name == "di_install_software_package";
                    if (!package && tool.Name != "di_install_files")
                    {
                        continue;
                    }
                    JsonNode schema = JsonNode.Parse(tool.InputSchema.GetRawText()) ??
                        throw new InvalidOperationException("Missing DI installation schema.");
                    JsonObject properties = schema["properties"]?["input"]?["properties"] as JsonObject ??
                        throw new InvalidOperationException("Missing DI installation input properties.");
                    properties[package ? "patchIdentifiers" : "fileNodeIds"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string" },
                        ["minItems"] = package ? 0 : 1,
                        ["maxItems"] = 500,
                        ["description"] = package
                            ? "Patch identifiers; an empty array means no patches."
                            : "Local server-side File object NodeIds, not host filesystem paths."
                    };
                    using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
                    tool.InputSchema = document.RootElement.Clone();
                }
                return result;
            };
        }
    }
}
