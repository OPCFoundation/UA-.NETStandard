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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Advertises the exact DTO-array shapes consumed by the source-generated input converters.
    /// </summary>
    internal static class Isa95McpFilters
    {
        /// <summary>
        /// Replaces custom-converter placeholders with bounded, recursively typed JSON schemas.
        /// </summary>
        public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> AddInputSchemas(
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);
            return async (request, ct) =>
            {
                ListToolsResult result = await next(request, ct).ConfigureAwait(false);
                foreach (Tool tool in result.Tools)
                {
                    if (tool.Name.StartsWith("isa95_", StringComparison.Ordinal))
                    {
                        tool.InputSchema = Normalize(tool.InputSchema);
                    }
                }
                return result;
            };
        }

        /// <summary>
        /// Normalizes one ISA-95 tool schema without changing unrelated arguments.
        /// </summary>
        internal static JsonElement Normalize(JsonElement input)
        {
            var root = JsonNode.Parse(input.GetRawText()) as JsonObject
                ?? throw new InvalidOperationException("Missing ISA-95 tool schema.");
            var properties = root["properties"] as JsonObject
                ?? throw new InvalidOperationException("Missing ISA-95 tool properties.");
            if (!properties.ContainsKey("jobOrder") && !properties.ContainsKey("response") &&
                !properties.ContainsKey("comment") && !properties.ContainsKey("query"))
            {
                return input;
            }
            JsonObject definitions = root["$defs"] as JsonObject ?? new JsonObject();
            if (definitions.Parent is null)
            {
                root["$defs"] = definitions;
            }
            foreach ((string key, JsonNode? value) in Definitions())
            {
                definitions[key] = value?.DeepClone();
            }
            if (properties.ContainsKey("jobOrder"))
            {
                ReplaceProperty(properties, "jobOrder", JobOrder());
            }
            if (properties.ContainsKey("response"))
            {
                ReplaceProperty(properties, "response", Response());
            }
            if (properties.ContainsKey("comment"))
            {
                ReplaceProperty(properties, "comment", Object(new JsonObject { ["texts"] = Array("isa95Text") }));
            }
            if (properties.ContainsKey("query"))
            {
                ReplaceProperty(properties, "query", Object(new JsonObject
                {
                    ["jobOrderId"] = String(),
                    ["states"] = Array("isa95State")
                }));
                properties["query"]!["oneOf"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["required"] = new JsonArray("jobOrderId"),
                        ["not"] = new JsonObject { ["required"] = new JsonArray("states") }
                    },
                    new JsonObject
                    {
                        ["required"] = new JsonArray("states"),
                        ["not"] = new JsonObject { ["required"] = new JsonArray("jobOrderId") }
                    }
                };
            }
            RemoveUnusedDefinitions(properties, definitions);
            using JsonDocument document = JsonDocument.Parse(root.ToJsonString());
            return document.RootElement.Clone();
        }

        /// <summary>
        /// Keeps the tool parameter's description when supplying its concrete converter schema.
        /// </summary>
        private static void ReplaceProperty(JsonObject properties, string name, JsonObject replacement)
        {
            if (properties[name] is JsonObject previous && previous["description"] is JsonNode description)
            {
                replacement["description"] = description.DeepClone();
            }
            properties[name] = replacement;
        }

        /// <summary>
        /// Keeps only definitions reachable from this tool, including recursive parameter references.
        /// </summary>
        private static void RemoveUnusedDefinitions(JsonObject properties, JsonObject definitions)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            Visit(properties);
            foreach (string name in definitions.Select(entry => entry.Key).ToArray())
            {
                if (name.StartsWith("isa95", StringComparison.Ordinal) && !used.Contains(name))
                {
                    definitions.Remove(name);
                }
            }

            void Visit(JsonNode? node)
            {
                if (node is JsonObject value)
                {
                    if (value["$ref"] is JsonValue reference &&
                        reference.TryGetValue(out string? path) &&
                        path is not null &&
                        path.StartsWith("#/$defs/", StringComparison.Ordinal))
                    {
                        string name = path["#/$defs/".Length..];
                        if (used.Add(name))
                        {
                            Visit(definitions[name]);
                        }
                    }
                    foreach ((string key, JsonNode? child) in value)
                    {
                        if (key != "$defs")
                        {
                            Visit(child);
                        }
                    }
                }
                else if (node is JsonArray array)
                {
                    foreach (JsonNode? child in array)
                    {
                        Visit(child);
                    }
                }
            }
        }

        /// <summary>
        /// Describes the complete job-order input.
        /// </summary>
        private static JsonObject JobOrder()
        {
            JsonObject properties = Resources();
            properties["jobOrderId"] = String();
            properties["description"] = Array("isa95Text");
            properties["workMasters"] = Array("isa95WorkMaster");
            properties["startTime"] = Timestamp();
            properties["endTime"] = Timestamp();
            properties["priority"] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = short.MinValue,
                ["maximum"] = short.MaxValue
            };
            return Object(properties, "jobOrderId");
        }

        /// <summary>
        /// Describes the complete response input.
        /// </summary>
        private static JsonObject Response()
        {
            JsonObject properties = Resources();
            properties["jobResponseId"] = String();
            properties["jobOrderId"] = String();
            properties["description"] = Reference("isa95Text");
            properties["startTime"] = Timestamp();
            properties["endTime"] = Timestamp();
            properties["states"] = Array("isa95State");
            properties["v1State"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(
                    "Undefined", "Waiting", "Ready", "Loaded", "Running", "Completed",
                    "Aborted", "Held", "Suspended", "Closed", "Error")
            };
            return Object(properties, "jobResponseId", "jobOrderId");
        }

        /// <summary>
        /// Describes parameter and resource lists shared by orders and responses.
        /// </summary>
        private static JsonObject Resources()
        {
            return new JsonObject
            {
                ["parameters"] = Array("isa95Parameter"),
                ["personnel"] = Array("isa95Resource"),
                ["equipment"] = Array("isa95Resource"),
                ["physicalAssets"] = Array("isa95Resource"),
                ["materials"] = Array("isa95Material")
            };
        }

        /// <summary>
        /// Defines each concrete nested input, including the recursive parameter tree.
        /// </summary>
        private static JsonObject Definitions()
        {
            JsonObject resourceProperties = Metadata();
            resourceProperties["id"] = String();
            resourceProperties["use"] = String();
            resourceProperties["quantity"] = String();
            resourceProperties["properties"] = Array("isa95Parameter");
            JsonObject materialProperties = Metadata();
            materialProperties["materialClassId"] = String();
            materialProperties["materialDefinitionId"] = String();
            materialProperties["materialLotId"] = String();
            materialProperties["materialSublotId"] = String();
            materialProperties["use"] = String();
            materialProperties["quantity"] = String();
            materialProperties["properties"] = Array("isa95Parameter");
            JsonObject parameterProperties = Metadata();
            parameterProperties["id"] = String();
            parameterProperties["value"] = Reference("isa95Value");
            parameterProperties["children"] = Array("isa95Parameter");
            return new JsonObject
            {
                ["isa95Text"] = Object(new JsonObject
                {
                    ["text"] = String(),
                    ["locale"] = String()
                }, "text"),
                ["isa95Value"] = Object(new JsonObject
                {
                    ["dataType"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray(
                            "Null", "Boolean", "SByte", "Byte", "Int16", "UInt16", "Int32", "UInt32",
                            "Int64", "UInt64", "Float", "Double", "String", "DateTime", "Guid", "ByteString",
                            "XmlElement", "NodeId", "ExpandedNodeId", "StatusCode", "QualifiedName",
                            "LocalizedText", "ExtensionObject", "DataValue", "Variant", "DiagnosticInfo")
                    },
                    ["isArray"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                    ["value"] = new JsonObject
                    {
                        ["description"] = "The strict UA JSON body for dataType. " +
                            "For example UInt32: 0, Boolean: false, UInt64: \"18446744073709551615\". " +
                            "Arrays have at most 500 elements. Null requires dataType Null."
                    }
                }, "dataType", "value"),
                ["isa95Unit"] = Object(new JsonObject
                {
                    ["namespaceUri"] = String(),
                    ["unitId"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = int.MinValue,
                        ["maximum"] = int.MaxValue
                    },
                    ["displayName"] = Reference("isa95Text"),
                    ["description"] = Reference("isa95Text")
                }, "namespaceUri"),
                ["isa95Parameter"] = Object(parameterProperties, "id", "value"),
                ["isa95Resource"] = Object(resourceProperties, "id"),
                ["isa95Material"] = Object(materialProperties),
                ["isa95WorkMaster"] = Object(new JsonObject
                {
                    ["id"] = String(),
                    ["description"] = Reference("isa95Text"),
                    ["parameters"] = Array("isa95Parameter")
                }, "id"),
                ["isa95State"] = Object(new JsonObject
                {
                    ["browsePath"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = String(),
                        ["minItems"] = 0,
                        ["maxItems"] = 500,
                        ["description"] = "Qualified browse-name segments followed through HasSubStateMachine. " +
                            "An empty array selects the root state-machine scope."
                    },
                    ["stateNumber"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 0,
                        ["maximum"] = uint.MaxValue
                    },
                    ["stateText"] = Reference("isa95Text")
                }, "stateNumber")
            };
        }

        /// <summary>
        /// Describes metadata shared by parameters and resources.
        /// </summary>
        private static JsonObject Metadata()
        {
            return new JsonObject
            {
                ["description"] = Array("isa95Text"),
                ["engineeringUnits"] = Reference("isa95Unit"),
                ["unitOfMeasure"] = String()
            };
        }

        /// <summary>
        /// Describes a concrete object with explicit required properties.
        /// </summary>
        private static JsonObject Object(JsonObject properties, params string[] required)
        {
            var requiredNames = new JsonArray();
            foreach (string name in required)
            {
                requiredNames.Add(name);
            }
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = requiredNames,
                ["additionalProperties"] = false
            };
        }

        /// <summary>
        /// Describes an immutable DTO array without its CLR memory representation.
        /// </summary>
        private static JsonObject Array(string element)
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = Reference(element),
                ["maxItems"] = 500
            };
        }

        /// <summary>
        /// References a concrete module-owned definition.
        /// </summary>
        private static JsonObject Reference(string name)
        {
            return new JsonObject { ["$ref"] = $"#/$defs/{name}" };
        }

        /// <summary>
        /// Describes a JSON string.
        /// </summary>
        private static JsonObject String()
        {
            return new JsonObject { ["type"] = "string" };
        }

        /// <summary>
        /// Describes a timestamp with a zone designation.
        /// </summary>
        private static JsonObject Timestamp()
        {
            return new JsonObject { ["type"] = "string", ["format"] = "date-time" };
        }
    }
}
