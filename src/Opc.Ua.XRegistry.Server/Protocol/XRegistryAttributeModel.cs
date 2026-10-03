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

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    /// <summary>
    /// Reuses transactional model rules for binding-level value conversion without a registry mutation.
    /// </summary>
    public static class XRegistryAttributeModel
    {
        /// <summary>
        /// Resolves a declared or active conditional attribute path. Unknown paths are not guessed.
        /// </summary>
        public static bool TryResolve(
            JsonElement attributes, JsonElement metadata, ArrayOf<string> path, out JsonElement definition)
        {
            definition = default;
            if (attributes.ValueKind != JsonValueKind.Object || path.Count == 0)
            {
                return false;
            }
            JsonObject? rules = JsonNode.Parse(attributes.GetRawText())!.AsObject();
            JsonNode? value = metadata.ValueKind == JsonValueKind.Object ? JsonNode.Parse(metadata.GetRawText()) : null;
            JsonObject? rule = null;
            for (int index = 0; index < path.Count; index++)
            {
                if (rules is not null)
                {
                    JsonObject active = XRegistryModelRules.EffectiveAttributes(rules, value as JsonObject ?? [])!;
                    rule = (active[path[index]] ?? active["*"]) as JsonObject;
                }
                else if (rule?["type"]?.GetValue<string>() == "map")
                {
                    rule = rule["item"] as JsonObject;
                }
                else
                {
                    return false;
                }
                if (rule is null)
                {
                    return false;
                }
                value = (value as JsonObject)?[path[index]];
                rules = rule["attributes"] as JsonObject;
            }
            using var document = JsonDocument.Parse(rule!.ToJsonString());
            definition = document.RootElement.Clone();
            return true;
        }

        /// <summary>
        /// Validates one present logical value, preserving exact numeric and compound types.
        /// Undefined values are absent, not successful empty data.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        public static void Validate(JsonElement value, JsonElement definition)
        {
            if (definition.ValueKind != JsonValueKind.Object || value.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidDataException("A mapped attribute requires its declared model and a present value.");
            }
            try
            {
                XRegistryModelRules.ValidateMappedValue(value, definition);
            }
            catch (XRegistryRejectionException exception)
            {
                throw new InvalidDataException(exception.Message, exception);
            }
        }

        /// <summary>
        /// Resolves an inactive declared path for native shape construction. Conflicting branch types are rejected.
        /// The caller must not treat an inactive definition as permission to read or write an attribute.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        public static bool TryResolveDeclared(
            JsonElement attributes, ArrayOf<string> path, out JsonElement definition)
        {
            definition = default;
            if (attributes.ValueKind != JsonValueKind.Object || path.Count == 0)
            {
                return false;
            }
            var pending = new List<JsonObject> { JsonNode.Parse(attributes.GetRawText())!.AsObject() };
            List<JsonObject> candidates = [];
            for (int index = 0; index < path.Count; index++)
            {
                candidates = [];
                foreach (JsonObject rules in pending)
                {
                    FindDeclared(rules, path[index], candidates, 0);
                }
                if (candidates.Count == 0)
                {
                    return false;
                }
                pending = [];
                if (index + 1 < path.Count)
                {
                    foreach (JsonObject rule in candidates)
                    {
                        if (rule["type"]?.GetValue<string>() == "map" && rule["item"] is JsonObject item)
                        {
                            pending.Add(new JsonObject { [path[index + 1]] = item.DeepClone() });
                        }
                        else if (rule["attributes"] is JsonObject nested)
                        {
                            pending.Add(nested);
                        }
                    }
                }
            }
            string type = candidates[0]["type"]!.GetValue<string>();
            foreach (JsonObject candidate in candidates)
            {
                if (candidate["type"]?.GetValue<string>() != type)
                {
                    throw new InvalidDataException(
                        "Inactive conditional native mappings have ambiguous logical types.");
                }
            }
            using var document = JsonDocument.Parse(candidates[0].ToJsonString());
            definition = document.RootElement.Clone();
            return true;
        }

        /// <summary>
        /// Compares finite JSON numbers without narrowing their precision or exponent.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        public static bool NumbersEqual(string first, string second)
        {
            first.ThrowIfNull(nameof(first));
            second.ThrowIfNull(nameof(second));
            using var a = JsonDocument.Parse(first);
            using var b = JsonDocument.Parse(second);
            if (a.RootElement.ValueKind != JsonValueKind.Number || b.RootElement.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidDataException("Both values must be JSON numbers.");
            }
            return XRegistryQueryPath.CompareNumbers(first, second) == 0;
        }

        private static void FindDeclared(JsonObject attributes, string name, List<JsonObject> matches, int depth)
        {
            if (depth > 64 || matches.Count > 4096)
            {
                throw new InvalidDataException("The declared attribute expansion limit was exceeded.");
            }
            if ((attributes[name] ?? attributes["*"]) is JsonObject rule)
            {
                matches.Add(rule);
            }
            foreach (KeyValuePair<string, JsonNode?> attribute in attributes)
            {
                if (attribute.Value?["ifvalues"] is not JsonObject branches)
                {
                    continue;
                }
                foreach (KeyValuePair<string, JsonNode?> branch in branches)
                {
                    if (branch.Value?["siblingattributes"] is JsonObject siblings)
                    {
                        FindDeclared(siblings, name, matches, depth + 1);
                    }
                }
            }
        }
    }

    internal sealed partial class XRegistryModelRules
    {
        internal static void ValidateMappedValue(JsonElement value, JsonElement definition)
        {
            JsonObject rule = Object(JsonNode.Parse(definition.GetRawText()));
            var node = JsonNode.Parse(value.GetRawText());
            if (node is null)
            {
                if (Text(rule["type"]) != "any")
                {
                    throw new XRegistryRejectionException(
                        "invalid_attribute", "Null is not a value of this model type.");
                }
                return;
            }
            if (node is JsonValue)
            {
                ValidateScalarSize(node, rule["name"] is JsonNode name ? Text(name) : string.Empty);
            }
            ValidateValue(node, rule, "mapped attribute");
        }
    }
}
