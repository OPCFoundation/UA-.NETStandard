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
