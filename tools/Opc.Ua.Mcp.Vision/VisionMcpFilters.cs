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
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Normalizes the generated Vision inference request schema at the MCP boundary.
    /// </summary>
    internal static class VisionMcpFilters
    {
        /// <summary>
        /// Adds the defaults and bounds required by the structured
        /// <c>vision_run_inference</c> request contract.
        /// </summary>
        public static McpRequestHandler<ListToolsRequestParams, ListToolsResult>
            AddInferenceRequestSchema(
                McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return async (request, ct) =>
            {
                ListToolsResult result = await next(request, ct).ConfigureAwait(false);
                foreach (Tool tool in result.Tools)
                {
                    if (tool.Name != "vision_run_inference" ||
                        tool.InputSchema.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    JsonObject? schema = JsonNode.Parse(tool.InputSchema.GetRawText()) as JsonObject;
                    if (schema is null || !SetInferenceRequestContract(schema))
                    {
                        continue;
                    }

                    using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
                    tool.InputSchema = document.RootElement.Clone();
                }

                return result;
            };
        }

        private static bool SetInferenceRequestContract(JsonObject schema)
        {
            return SetStringEnum(
                schema,
                ["properties", "request", "properties", "expectedKind"],
                kExpectedResultKinds,
                "Auto") |
                SetStringEnum(
                    schema,
                    ["properties", "request", "properties", "detail"],
                    kResultDetails,
                    "Summary") |
                SetIntegerRange(
                    schema,
                    ["properties", "request", "properties", "maxItems"],
                    0,
                    100,
                    20);
        }

        private static bool SetStringEnum(
            JsonObject schema,
            string[] path,
            string[] values,
            string defaultValue)
        {
            if (!TryGetProperty(schema, path, out JsonObject? property))
            {
                return false;
            }

            var enumValues = new JsonArray();
            for (int i = 0; i < values.Length; i++)
            {
                enumValues.Add(values[i]);
            }

            property!["type"] = "string";
            property["enum"] = enumValues;
            property["default"] = defaultValue;
            return true;
        }

        private static bool SetIntegerRange(
            JsonObject schema,
            string[] path,
            int minimum,
            int maximum,
            int defaultValue)
        {
            if (!TryGetProperty(schema, path, out JsonObject? property))
            {
                return false;
            }

            property!["type"] = "integer";
            property["minimum"] = minimum;
            property["maximum"] = maximum;
            property["default"] = defaultValue;
            return true;
        }

        private static bool TryGetProperty(
            JsonObject schema,
            string[] path,
            out JsonObject? property)
        {
            JsonNode? current = schema;
            for (int i = 0; i < path.Length; i++)
            {
                if (current is not JsonObject currentObject ||
                    currentObject[path[i]] is not JsonNode child)
                {
                    property = null;
                    return false;
                }

                current = child;
            }

            property = current as JsonObject;
            return property is not null;
        }

        private static readonly string[] kExpectedResultKinds =
            ["Auto", "Detection", "Inspection", "Segmentation"];

        private static readonly string[] kResultDetails =
            ["Summary", "HandleOnly"];
    }
}
