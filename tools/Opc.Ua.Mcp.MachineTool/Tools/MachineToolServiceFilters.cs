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

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Advertises exact service selectors and native UA JSON boundaries without reflecting generated model types.
    /// </summary>
    internal static class MachineToolServiceFilters
    {
        /// <summary>
        /// Enriches only this bounded service catalog after the SDK generates its primitive parameter schemas.
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
        /// Applies native object/array schemas, retaining descriptions and all SDK-generated primitive bounds.
        /// </summary>
        internal static void Apply(IEnumerable<Tool> tools)
        {
            foreach (Tool tool in tools)
            {
                if (tool.Name is not ("machinetool_query_jobs" or "machinetool_control_job" or
                    "machinetool_list_files" or "machinetool_read_file" or "machinetool_change_file") ||
                    JsonNode.Parse(tool.InputSchema.GetRawText()) is not JsonObject schema ||
                    schema["properties"] is not JsonObject properties)
                {
                    continue;
                }
                if (properties["root"] is JsonObject root)
                {
                    root["enum"] = JsonNode.Parse("""["FileSystem","WorkMasters"]""");
                }
                switch (tool.Name)
                {
                    case "machinetool_query_jobs":
                        properties["query"]!["enum"] =
                            JsonNode.Parse("""["Orders","ResponseById","ResponsesByState"]""");
                        NativeProperty(properties, "states", """
                            {
                              "type": ["array", "null"], "minItems": 1, "maxItems": 16,
                              "items": {
                                "type": "object", "required": ["StateNumber"],
                                "properties": {
                                  "StateNumber": { "type": "integer", "minimum": 0, "maximum": 4294967295 }
                                }
                              },
                              "examples": [[{ "StateNumber": 1 }]]
                            }
                            """);
                        break;
                    case "machinetool_control_job":
                        properties["operation"]!["enum"] = JsonNode.Parse("""
                            ["Store","StoreAndStart","Start","Stop","Abort","Pause","Resume","Clear","Cancel",
                             "Update","RevokeStart"]
                            """);
                        NativeProperty(properties, "jobOrder", """
                            {
                              "type": ["object", "null"], "required": ["JobOrderID"],
                              "properties": {
                                "JobOrderID": { "type": "string", "minLength": 1, "maxLength": 1024 },
                                "EncodingMask": { "type": "integer", "minimum": 0, "maximum": 1023 },
                                "Priority": { "type": "integer", "minimum": -32768, "maximum": 32767 }
                              },
                              "examples": [{ "JobOrderID": "Job-1", "EncodingMask": 0 }]
                            }
                            """);
                        NativeProperty(properties, "comments", """
                            {
                              "type": ["array", "null"], "minItems": 0, "maxItems": 8,
                              "items": { "type": ["object", "string", "null"], "maxLength": 1024 },
                              "examples": [[{ "Text": "approved", "Locale": "en" }]]
                            }
                            """);
                        break;
                    case "machinetool_change_file":
                        properties["operation"]!["enum"] = JsonNode.Parse(
                            """["Write","CreateFile","CreateDirectory","Delete","Move","Copy"]""");
                        NativeProperty(properties, "options", """
                            {
                              "type": ["object", "null"], "additionalProperties": false,
                              "properties": {
                                "destination": {
                                  "type": ["string", "null"], "minLength": 1, "maxLength": 1024,
                                  "description": "Move/Copy: absent same-root path; existing parent, unqualified leaf."
                                },
                                "contentBase64": {
                                  "type": ["string", "null"], "maxLength": 699052,
                                  "description": "Write: replacement base64, no whitespace; empty clears the file."
                                },
                                "maximumBytes": {
                                  "type": "integer", "minimum": 1, "maximum": 524288, "default": 65536,
                                  "description": "Decoded Write byte cap; checked before any file mutation."
                                },
                                "recursive": {
                                  "type": "boolean", "default": false,
                                  "description": "Delete only: authorize nonempty-directory removal."
                                }
                              },
                              "examples": [{ "contentBase64": "AQI=", "maximumBytes": 2 }]
                            }
                            """);
                        break;
                }
                using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
                tool.InputSchema = document.RootElement.Clone();
            }
        }

        /// <summary>
        /// Replaces opaque JsonElement metadata while retaining the wire parameter's explanatory description.
        /// </summary>
        private static void NativeProperty(JsonObject properties, string name, string schema)
        {
            JsonNode value = JsonNode.Parse(schema)!;
            value["description"] = properties[name]?["description"]?.DeepClone();
            properties[name] = value;
        }
    }
}
