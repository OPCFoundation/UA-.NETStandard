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
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Compacts generated Robotics schemas without changing their JSON request shape.
    /// </summary>
    internal static class RoboticsMcpFilters
    {
        /// <summary>
        /// Reuses JSON Schema definitions for nested direct intents and emits the
        /// flat discriminated mission contract without repeated type descriptions.
        /// </summary>
        public static McpRequestHandler<ListToolsRequestParams, ListToolsResult>
            AddCompactIntentSchemas(
                McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return async (request, ct) =>
            {
                ListToolsResult result = await next(request, ct).ConfigureAwait(false);
                CompactIntentSchemas(result.Tools);
                return result;
            };
        }

        internal static void CompactIntentSchemas(IEnumerable<Tool> tools)
        {
            foreach (Tool tool in tools)
            {
                if (tool.InputSchema.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                JsonObject? schema = JsonNode.Parse(tool.InputSchema.GetRawText()) as JsonObject;
                if (schema is null || !CompactToolSchema(tool.Name, schema))
                {
                    continue;
                }

                using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
                tool.InputSchema = document.RootElement.Clone();
            }
        }

        private static bool CompactToolSchema(string toolName, JsonObject schema)
        {
            if (kReferencedInputTools.Contains(toolName))
            {
                return MoveParameterToDefinition(schema, "input", "i");
            }

            return toolName switch
            {
                "robotics_submit_mission" => CompactMissionSchema(schema, "steps"),
                "robotics_update_mission" => CompactMissionSchema(schema, "horizonSteps"),
                _ => false
            };
        }

        private static bool MoveParameterToDefinition(
            JsonObject schema,
            string parameterName,
            string definitionName)
        {
            if (!TryGetObject(schema, out JsonObject? properties, "properties") ||
                properties![parameterName] is not JsonObject parameter)
            {
                return false;
            }

            var definition = (JsonObject)parameter.DeepClone();
            RewriteReferences(
                definition,
                "#/properties/" + parameterName,
                "#/$defs/" + definitionName);

            JsonObject definitions = GetOrCreateDefinitions(schema);
            definitions[definitionName] = definition;
            properties[parameterName] = CreateReference(parameter, definitionName);
            return true;
        }

        private static bool CompactMissionSchema(JsonObject schema, string parameterName)
        {
            if (!TryGetObject(schema, out JsonObject? properties, "properties") ||
                properties![parameterName] is not JsonObject parameter)
            {
                return false;
            }

            SetMissionParameterDescriptions(properties);
            RemoveExplicitNulls(schema);

            var definition = (JsonObject)parameter.DeepClone();
            RemoveDescriptions(definition);
            RewriteReferences(
                definition,
                "#/properties/" + parameterName,
                "#/$defs/" + parameterName);

            JsonObject definitions = GetOrCreateDefinitions(schema);
            CompactMissionDefinition(definition, definitions);
            definitions[parameterName] = definition;

            properties[parameterName] = new JsonObject
            {
                ["description"] = kMissionParameterDescriptions[parameterName],
                ["type"] = "array",
                ["$ref"] = "#/$defs/" + parameterName
            };
            return true;
        }

        private static void CompactMissionDefinition(
            JsonObject definition,
            JsonObject definitions)
        {
            if (!TryGetObject(
                    definition,
                    out JsonObject? intentProperties,
                    "items",
                    "properties",
                    "intent",
                    "properties"))
            {
                return;
            }

            JsonObject intent = intentProperties!;
            MoveSharedObject(
                intent,
                "target",
                "p",
                definitions);
            ReplacePropertyWithReference(intent, "viaPoint", "p");
            if (TryGetObject(
                    intent,
                    out JsonObject? waypointProperties,
                    "waypoints",
                    "items",
                    "properties"))
            {
                ReplacePropertyWithReference(waypointProperties!, "pose", "p");
            }

            MoveSharedObject(intent, "blend", "b", definitions);
            if (TryGetObject(
                    intent,
                    out waypointProperties,
                    "waypoints",
                    "items",
                    "properties"))
            {
                ReplacePropertyWithReference(waypointProperties!, "blend", "b");
            }

            MoveSharedObject(intent, "jointTargets", "a", definitions);
            ReplacePropertyWithReference(intent, "direction", "a");
            if (TryGetObject(
                    intent,
                    out JsonObject? trajectoryPointProperties,
                    "points",
                    "items",
                    "properties"))
            {
                JsonObject trajectoryPoint = trajectoryPointProperties!;
                ReplacePropertyWithReference(trajectoryPoint, "positions", "a");
                ReplacePropertyWithReference(trajectoryPoint, "velocities", "a");
                ReplacePropertyWithReference(trajectoryPoint, "accelerations", "a");
            }

            if (TryGetObject(intent, out JsonObject? attributes, "attributes") &&
                attributes!["items"] is JsonObject attribute)
            {
                definitions["v"] = attribute.DeepClone();
                attributes["items"] = CreateReference(attribute, "v");
                if (TryGetObject(intent, out JsonObject? arguments, "arguments"))
                {
                    arguments!["items"] = CreateReference(attribute, "v");
                }
            }
        }

        private static void MoveSharedObject(
            JsonObject properties,
            string propertyName,
            string definitionName,
            JsonObject definitions)
        {
            if (properties[propertyName] is not JsonObject property)
            {
                return;
            }

            definitions[definitionName] = property.DeepClone();
            properties[propertyName] = CreateReference(property, definitionName);
        }

        private static void ReplacePropertyWithReference(
            JsonObject properties,
            string propertyName,
            string definitionName)
        {
            if (properties[propertyName] is JsonObject property)
            {
                properties[propertyName] = CreateReference(property, definitionName);
            }
        }

        private static JsonObject CreateReference(JsonObject source, string definitionName)
        {
            var reference = new JsonObject
            {
                ["$ref"] = "#/$defs/" + definitionName
            };
            if (source["type"] is JsonNode type)
            {
                reference["type"] = type.DeepClone();
            }
            if (source["description"] is JsonNode description)
            {
                reference["description"] = description.DeepClone();
            }
            if (source["default"] is JsonNode defaultValue)
            {
                reference["default"] = defaultValue.DeepClone();
            }
            return reference;
        }

        private static JsonObject GetOrCreateDefinitions(JsonObject schema)
        {
            if (schema["$defs"] is JsonObject definitions)
            {
                return definitions;
            }

            definitions = [];
            schema["$defs"] = definitions;
            return definitions;
        }

        private static void SetMissionParameterDescriptions(JsonObject properties)
        {
            foreach (KeyValuePair<string, string> description in kMissionParameterDescriptions)
            {
                if (properties[description.Key] is JsonObject property)
                {
                    property["description"] = description.Value;
                }
            }
        }

        private static void RemoveDescriptions(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                obj.Remove("description");
                obj.Remove("title");
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    RemoveDescriptions(property.Value);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    RemoveDescriptions(item);
                }
            }
        }

        private static void RemoveExplicitNulls(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                if (obj["type"] is JsonArray types)
                {
                    RemoveNullItems(types);
                    if (types.Count == 1)
                    {
                        obj["type"] = types[0]!.DeepClone();
                    }
                }
                if (obj["enum"] is JsonArray values)
                {
                    RemoveNullItems(values);
                }

                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    RemoveExplicitNulls(property.Value);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    RemoveExplicitNulls(item);
                }
            }
        }

        private static void RemoveNullItems(JsonArray values)
        {
            for (int i = values.Count - 1; i >= 0; i--)
            {
                if (values[i] is null ||
                    values[i] is JsonValue value &&
                    value.TryGetValue(out string? text) &&
                    text == "null")
                {
                    values.RemoveAt(i);
                }
            }
        }

        private static void RewriteReferences(
            JsonNode? node,
            string oldPrefix,
            string newPrefix)
        {
            if (node is JsonObject obj)
            {
                if (obj["$ref"] is JsonValue reference &&
                    reference.TryGetValue(out string? value) &&
                    value != null &&
                    value.StartsWith(oldPrefix, StringComparison.Ordinal))
                {
                    obj["$ref"] = newPrefix + value[oldPrefix.Length..];
                }

                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    RewriteReferences(property.Value, oldPrefix, newPrefix);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    RewriteReferences(item, oldPrefix, newPrefix);
                }
            }
        }

        private static bool TryGetObject(
            JsonObject root,
            out JsonObject? result,
            params string[] path)
        {
            JsonNode? current = root;
            for (int i = 0; i < path.Length; i++)
            {
                if (current is not JsonObject obj || obj[path[i]] is not JsonNode next)
                {
                    result = null;
                    return false;
                }
                current = next;
            }

            result = current as JsonObject;
            return result is not null;
        }

        private static readonly HashSet<string> kReferencedInputTools =
        [
            "robotics_submit_arc_weld",
            "robotics_submit_call_program",
            "robotics_submit_cartesian_path",
            "robotics_submit_circular_move",
            "robotics_submit_dispense",
            "robotics_submit_fasten",
            "robotics_submit_joint_move",
            "robotics_submit_linear_move",
            "robotics_submit_palletise",
            "robotics_submit_spot_weld",
            "robotics_submit_surface_finish",
            "robotics_submit_trajectory"
        ];

        private static readonly Dictionary<string, string> kMissionParameterDescriptions =
            new(StringComparer.Ordinal)
            {
                ["controller"] = "Controller name or NodeId.",
                ["missionId"] = "Mission ID.",
                ["missionUpdateId"] = "Update number.",
                ["steps"] = "Steps, e.g. Pick then Place.",
                ["horizonSteps"] = "Replacement steps, e.g. Pick then Place.",
                ["transitions"] = "Transitions.",
                ["label"] = "Label.",
                ["sessionName"] = "Session name."
            };
    }
}
