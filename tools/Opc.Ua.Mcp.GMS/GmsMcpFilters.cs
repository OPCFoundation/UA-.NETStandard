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

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Describes grouped selectors and native arrays without reflection-based generic JSON registration.
    /// </summary>
    internal static class GmsMcpFilters
    {
        /// <summary>
        /// Enriches only tools already contributed by the selected GMS profile.
        /// </summary>
        internal static McpRequestHandler<ListToolsRequestParams, ListToolsResult> AddSchemas(
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);
            return async (request, ct) =>
            {
                ListToolsResult result = await next(request, ct).ConfigureAwait(false);
                Apply(result.Tools);
                return result;
            };
        }

        /// <summary>
        /// Applies exact case-sensitive selectors, bounded arrays and native filter-value types.
        /// </summary>
        internal static void Apply(IEnumerable<Tool> tools)
        {
            foreach (Tool tool in tools)
            {
                if (!tool.Name.StartsWith("gms_", StringComparison.Ordinal) ||
                    JsonNode.Parse(tool.InputSchema.GetRawText()) is not JsonObject schema ||
                    schema["properties"] is not JsonObject properties)
                {
                    continue;
                }
                schema["additionalProperties"] = false;
                switch (tool.Name)
                {
                    case "gms_read_system":
                        properties["section"]!["enum"] =
                            new JsonArray("System", "Equipment", "Calibration", "Production", "Capabilities");
                        break;
                    case "gms_read_results":
                        properties["operation"]!["enum"] =
                            new JsonArray("Latest", "ById", "Query", "Published", "Corrections");
                        var query = new JsonObject
                        {
                            ["description"] = "Native standard-result filter/ordering selection; " +
                                "defaults to no filter and ascending ResultId. Nondefault values require Query.",
                            ["type"] = "object",
                            ["additionalProperties"] = false,
                            ["examples"] = new JsonArray(new JsonObject
                            {
                                ["filterField"] = "JobId", ["filterValue"] = "J-1", ["orderBy"] = "ResultId"
                            }),
                            ["properties"] = new JsonObject
                            {
                                ["filterField"] = new JsonObject
                                {
                                    ["description"] = "Exact equality field: None, ResultId, JobId, or IsPartial.",
                                    ["type"] = "string",
                                    ["enum"] = new JsonArray("None", "ResultId", "JobId", "IsPartial"),
                                    ["default"] = "None"
                                },
                                ["filterValue"] = new JsonObject
                                {
                                    ["description"] = "Exact ResultId/JobId string or native IsPartial Boolean; " +
                                        "omit when filterField is None.",
                                    ["anyOf"] = new JsonArray(
                                        new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024 },
                                        new JsonObject { ["type"] = "boolean" })
                                },
                                ["orderBy"] = new JsonObject
                                {
                                    ["description"] = "Ascending standard metadata ordering: None or ResultId.",
                                    ["type"] = "string",
                                    ["enum"] = new JsonArray("None", "ResultId"),
                                    ["default"] = "ResultId"
                                }
                            }
                        };
                        query["allOf"] = JsonNode.Parse("""
                            [
                              {
                                "if": {
                                  "properties": { "filterField": { "enum": ["ResultId", "JobId"] } },
                                  "required": ["filterField"]
                                },
                                "then": {
                                  "required": ["filterValue"],
                                  "properties": { "filterValue": { "type": "string" } }
                                }
                              },
                              {
                                "if": {
                                  "properties": { "filterField": { "const": "IsPartial" } },
                                  "required": ["filterField"]
                                },
                                "then": {
                                  "required": ["filterValue"],
                                  "properties": { "filterValue": { "type": "boolean" } }
                                }
                              },
                              {
                                "if": { "properties": { "filterField": { "const": "None" } } },
                                "then": { "not": { "required": ["filterValue"] } }
                              }
                            ]
                            """);
                        properties["query"] = query;
                        schema["allOf"] = JsonNode.Parse("""
                            [
                              {
                                "if": { "properties": { "operation": { "const": "ById" } }, "required": ["operation"] },
                                "then": { "required": ["resultId"] },
                                "else": { "not": { "required": ["resultId"] } }
                              },
                              {
                                "if": {
                                  "properties": { "operation": { "const": "Query" } }, "required": ["operation"]
                                },
                                "else": {
                                  "properties": {
                                    "query": {
                                      "properties": {
                                        "filterField": { "const": "None" }, "orderBy": { "const": "ResultId" }
                                      },
                                      "not": { "required": ["filterValue"] }
                                    }
                                  }
                                }
                              }
                            ]
                            """);
                        break;
                    case "gms_observe":
                        properties["kind"]!["enum"] =
                            new JsonArray("Data", "Events", "Conditions", "IntermediateResults");
                        properties["characteristics"] = new JsonObject
                        {
                            ["description"] = "Zero to 32 explicit characteristic placeholder names in their " +
                                "actual instance namespace; only for IntermediateResults.",
                            ["type"] = "array",
                            ["minItems"] = 0,
                            ["maxItems"] = 32,
                            ["uniqueItems"] = true,
                            ["examples"] = new JsonArray(new JsonArray(new JsonObject
                            {
                                ["name"] = "Bore", ["namespaceIndex"] = 12
                            })),
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["additionalProperties"] = false,
                                ["required"] = new JsonArray("name", "namespaceIndex"),
                                ["properties"] = new JsonObject
                                {
                                    ["name"] = new JsonObject
                                    {
                                        ["type"] = "string",
                                        ["minLength"] = 1,
                                        ["maxLength"] = 256,
                                        ["description"] = "Actual instance browse name of the characteristic."
                                    },
                                    ["namespaceIndex"] = new JsonObject
                                    {
                                        ["type"] = "integer",
                                        ["minimum"] = 0,
                                        ["maximum"] = 65535,
                                        ["description"] = "Server namespace index, not assumed to be the GMS index."
                                    }
                                }
                            }
                        };
                        schema["allOf"] = JsonNode.Parse("""
                            [{
                              "if": {
                                "properties": { "kind": { "const": "IntermediateResults" } },
                                "required": ["kind"]
                              },
                              "else": { "not": { "required": ["characteristics"] } }
                            }]
                            """);
                        break;
                    case "gms_acknowledge_results":
                        properties["resultIds"] = new JsonObject
                        {
                            ["description"] = "One to 100 distinct result identifier strings to acknowledge.",
                            ["type"] = "array",
                            ["minItems"] = 1,
                            ["maxItems"] = 100,
                            ["uniqueItems"] = true,
                            ["examples"] = new JsonArray(new JsonArray("R-1", "R-2")),
                            ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024 }
                        };
                        break;
                }
                using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
                tool.InputSchema = document.RootElement.Clone();
            }
        }
    }
}
