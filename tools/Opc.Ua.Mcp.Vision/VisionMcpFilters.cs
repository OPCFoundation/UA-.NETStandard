/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
            bool expectedKindChanged = SetStringEnum(
                schema,
                ["properties", "request", "properties", "expectedKind"],
                kExpectedResultKinds,
                "Auto");
            bool detailChanged = SetStringEnum(
                schema,
                ["properties", "request", "properties", "detail"],
                kResultDetails,
                "Summary");
            bool maxItemsChanged = SetIntegerRange(
                schema,
                ["properties", "request", "properties", "maxItems"],
                0,
                100,
                20);
            return expectedKindChanged || detailChanged || maxItemsChanged;
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
