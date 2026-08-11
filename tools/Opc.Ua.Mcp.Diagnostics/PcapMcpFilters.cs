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
using Opc.Ua.Pcap.Capture;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Normalizes packet-diagnostics schemas and errors at the MCP boundary.
    /// </summary>
    internal static class PcapMcpFilters
    {
        /// <summary>
        /// Returns actionable packet-diagnostics errors instead of the MCP
        /// SDK's generic invocation failure.
        /// </summary>
        public static McpRequestHandler<CallToolRequestParams, CallToolResult> SurfaceDiagnosticsErrors(
            McpRequestHandler<CallToolRequestParams, CallToolResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return async (request, ct) =>
            {
                try
                {
                    return await next(request, ct).ConfigureAwait(false);
                }
                catch (PcapDiagnosticsException exception)
                {
                    return new CallToolResult
                    {
                        IsError = true,
                        Content = [new TextContentBlock { Text = exception.Message }]
                    };
                }
            };
        }

        /// <summary>
        /// Restores string-enum constraints that the JSON schema exporter
        /// cannot infer from the alias-aware custom converters.
        /// </summary>
        public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> AddCanonicalEnumSchemas(
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return async (request, ct) =>
            {
                ListToolsResult result = await next(request, ct).ConfigureAwait(false);
                foreach (Tool tool in result.Tools)
                {
                    JsonObject? schema = tool.InputSchema.ValueKind == JsonValueKind.Object
                        ? JsonNode.Parse(tool.InputSchema.GetRawText()) as JsonObject
                        : null;
                    if (schema is null)
                    {
                        continue;
                    }

                    bool changed = tool.Name switch
                    {
                        "start_capture" => SetStringEnum(
                            schema,
                            ["properties", "request", "properties", "source"],
                            kCaptureSourceNames,
                            "inproc-client"),
                        "capture_now" => SetCaptureNowEnums(schema),
                        _ => false
                    };
                    if (!changed)
                    {
                        continue;
                    }

                    using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
                    tool.InputSchema = document.RootElement.Clone();
                }

                return result;
            };
        }

        private static bool SetCaptureNowEnums(JsonObject schema)
        {
            bool sourceChanged = SetStringEnum(
                schema,
                ["properties", "request", "properties", "start", "properties", "source"],
                kCaptureSourceNames,
                "inproc-client");
            bool formatChanged = SetStringEnum(
                schema,
                ["properties", "request", "properties", "format"],
                kFormatNames,
                "service-timeline");
            return sourceChanged || formatChanged;
        }

        private static bool SetStringEnum(
            JsonObject schema,
            IReadOnlyList<string> path,
            IReadOnlyList<string> values,
            string defaultValue)
        {
            JsonNode? node = schema;
            foreach (string segment in path)
            {
                if (node is not JsonObject current ||
                    current[segment] is not JsonNode child)
                {
                    return false;
                }

                node = child;
            }

            if (node is not JsonObject property)
            {
                return false;
            }

            var enumValues = new JsonArray();
            foreach (string value in values)
            {
                enumValues.Add(value);
            }

            property["type"] = "string";
            property["enum"] = enumValues;
            property["default"] = defaultValue;
            return true;
        }

        private static readonly string[] kCaptureSourceNames =
            ["nic", "inproc-client", "inproc-server", "replay"];

        private static readonly string[] kFormatNames =
            ["pcap", "pcapng", "json", "csv", "text", "service-timeline"];
    }
}
