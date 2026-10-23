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
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    internal sealed partial class XRegistryModelRules
    {
        public static string DocumentRepresentation(JsonObject definition, string? contentType)
        {
            string type = (contentType ?? string.Empty).Split(';')[0].Trim();
            var map =
                definition["typemap"] is JsonObject declared ? (JsonObject)declared.DeepClone() : new JsonObject();
            map["application/json"] ??= "json";
            map["*+json"] ??= "json";
            map["text/plain"] ??= "string";
            string[] matches = [.. map.Where(pair => XRegistryQueryPath.WildcardMatch(type, pair.Key))
                .Select(pair => Text(pair.Value).ToLowerInvariant()).Distinct(StringComparer.Ordinal)];
            return matches.Length == 1 ? matches[0] : "binary";
        }

        internal static JsonObject? EffectiveAttributes(JsonObject? definitions, JsonObject value)
        {
            if (definitions is null)
            {
                return null;
            }
            if (!definitions.Any(pair => pair.Value?["ifvalues"] is not null))
            {
                return definitions;
            }
            var effective = (JsonObject)definitions.DeepClone();
            var pending = new Queue<string>(effective.Select(pair => pair.Key));
            while (pending.Count != 0)
            {
                string name = pending.Dequeue();
                JsonObject rule = Object(effective[name]);
                JsonNode? actual = value[name] ?? rule["default"];
                if (actual is null || rule["ifvalues"] is not JsonObject conditions)
                {
                    continue;
                }
                string key = actual.GetValueKind() == JsonValueKind.String ? Text(actual) : actual.ToJsonString();
                foreach ((string expected, JsonNode? branch) in conditions)
                {
                    if (!string.Equals(key, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    JsonObject siblings = Object(branch?["siblingattributes"]);
                    foreach ((string sibling, JsonNode? definition) in siblings)
                    {
                        if (effective.ContainsKey(sibling))
                        {
                            throw new XRegistryRejectionException("invalid_attribute",
                                "Active conditional attributes have conflicting names.");
                        }
                        effective[sibling] = definition?.DeepClone();
                        pending.Enqueue(sibling);
                        if (effective.Count > 4096)
                        {
                            throw new XRegistryRejectionException(
                                "invalid_model", "The expanded attribute limit was exceeded.");
                        }
                    }
                }
            }
            return effective;
        }

        private static void ValidateModelMembers(JsonObject model, HashSet<string> allowed)
        {
            foreach ((string name, _) in model)
            {
                if (!allowed.Contains(name))
                {
                    throw new XRegistryRejectionException("invalid_model",
                        $"Unsupported model-language member '{name}'.");
                }
            }
        }

        private static JsonObject Prospective(JsonObject old, JsonObject input, bool patch, JsonObject? definitions)
        {
            var value = patch ? (JsonObject)old.DeepClone() : new JsonObject();
            foreach ((string name, JsonNode? item) in input)
            {
                if (!Boolean(definitions?[name]?["readonly"]) && !Boolean(definitions?[name]?["immutable"]))
                {
                    value[name] = item?.DeepClone();
                }
            }
            foreach ((string name, JsonNode? item) in old)
            {
                if (Boolean(definitions?[name]?["readonly"]) || Boolean(definitions?[name]?["immutable"]))
                {
                    value[name] = item?.DeepClone();
                }
            }
            return value;
        }

        private static void ValidateAttributeDefinition(
            string name, JsonObject rule, JsonObject siblings, int depth, bool extendedNames)
        {
            ValidateModelMembers(rule, s_attributeModelMembers);
            string type = Text(rule["type"]);
            if (!s_types.Contains(type))
            {
                throw new XRegistryRejectionException("invalid_model", "An attribute uses an unknown type.");
            }
            if ((rule["attributes"] is not null && type != "object") ||
                (rule["item"] is not null && type is not ("array" or "map")) ||
                rule["enum"] is not (null or JsonArray))
            {
                throw new XRegistryRejectionException(
                    "invalid_model", "An attribute member is not valid for its declared type.");
            }
            if (rule["name"] is JsonNode declared && Text(declared) != name)
            {
                throw new XRegistryRejectionException("invalid_model", "The attribute name differs from its key.");
            }
            if (name == "*" && (Boolean(rule["required"]) || Boolean(rule["readonly"]) || rule["ifvalues"] is not null))
            {
                throw new XRegistryRejectionException(
                    "invalid_model", "Wildcard attributes cannot be required, readonly or conditional.");
            }
            if (rule["namecharset"] is JsonNode charset &&
                (type != "object" || Text(charset).ToLowerInvariant() is not ("strict" or "extended")))
            {
                throw new XRegistryRejectionException(
                    "model_error", "namecharset requires an object and a known character set.");
            }
            if (depth > 0 && rule.ContainsKey("immutable"))
            {
                throw new XRegistryRejectionException("model_error", "Extension attributes cannot use immutable.");
            }
            if (rule["target"] is JsonNode target)
            {
                if (type is not ("uri" or "url" or "xid"))
                {
                    throw new XRegistryRejectionException(
                        "invalid_model", "target is only valid for uri, url and xid.");
                }
                string template = Text(target);
                string address = template.EndsWith("[/versions]", StringComparison.Ordinal)
                    ? template[..^11] : template;
                string[] segments = address.Split('/');
                if (segments.Length is < 2 or > 4 ||
                    segments[0].Length != 0 ||
                    (segments.Length == 4 && segments[3] != "versions") ||
                    segments.Skip(1).Any(string.IsNullOrEmpty))
                {
                    throw new XRegistryRejectionException("invalid_model", "The target template is invalid.");
                }
            }
            if (rule["enum"] is JsonArray values)
            {
                if (type is "object" or "map" or "array" or "any")
                {
                    throw new XRegistryRejectionException("invalid_model", "enum requires a scalar type.");
                }
                var scalar = (JsonObject)rule.DeepClone();
                scalar.Remove("enum");
                foreach (JsonNode? value in values)
                {
                    ValidateValue(value ?? throw new XRegistryRejectionException("invalid_model", "Null enum value."),
                        scalar, name);
                }
            }
            if (rule["ifvalues"] is JsonNode conditionNode)
            {
                if (type is "object" or "map" or "array" or "any")
                {
                    throw new XRegistryRejectionException(
                        "invalid_model", "Conditional definitions require scalar attributes.");
                }
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach ((string key, JsonNode? branch) in Object(conditionNode))
                {
                    if (key.Length == 0 || key.StartsWith('^') || !keys.Add(key))
                    {
                        throw new XRegistryRejectionException(
                            "model_error", "Conditional values must be distinct ignoring case.");
                    }
                    if (rule["enum"] is JsonArray choices &&
                        choices.Count > 0 &&
                        Boolean(rule["strict"], true) &&
                        !choices.Any(choice => choice is not null &&
                            string.Equals(
                            choice.GetValueKind() == JsonValueKind.String ? Text(choice) : choice.ToJsonString(), key,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new XRegistryRejectionException(
                            "model_error", "Conditional values must belong to the strict enum.");
                    }
                    JsonObject conditional = Object(branch?["siblingattributes"]);
                    if (conditional.Any(pair => siblings.ContainsKey(pair.Key)))
                    {
                        throw new XRegistryRejectionException(
                            "model_error", "A conditional name duplicates a static attribute.");
                    }
                    ValidateAttributes(conditional, depth + 1, extendedNames);
                    ValidateMatchVersions(conditional, false, true);
                }
            }
        }

        private static void ValidateTarget(JsonNode value, JsonObject definition)
        {
            if (definition["target"] is not JsonNode target)
            {
                return;
            }
            string address = Text(value);
            if (Text(definition["type"]) != "xid" && !address.StartsWith('/'))
            {
                return;
            }
            string template = Text(target);
            bool versionOptional = template.EndsWith("[/versions]", StringComparison.Ordinal);
            if (versionOptional)
            {
                template = template[..^11];
            }
            string[] names = template.Split('/');
            ArrayOf<string> parts = XRegistryPath.GetSegments(address);
            bool valid = names.Length == 2
                ? parts.Count == 2 && parts[0] == names[1]
                : parts.Count >= 4 &&
                    parts[0] == names[1] &&
                    parts[2] == names[2] &&
                    (names.Length == 4 ? parts.Count == 6 && parts[4] == "versions" :
                    versionOptional ? parts.Count == 4 || (parts.Count == 6 && parts[4] == "versions")
                        : parts.Count == 4);
            if (!valid)
            {
                throw new XRegistryRejectionException(
                    "invalid_attribute", "The reference does not match its target entity type.");
            }
        }

        private static void ValidateMatchVersions(JsonObject? attributes, bool versioned, bool dynamic)
        {
            if (attributes is null)
            {
                return;
            }
            foreach ((string name, JsonNode? node) in attributes)
            {
                JsonObject rule = Object(node);
                string type = Text(rule["type"]);
                if (Boolean(rule["matchversions"]) &&
                    (!versioned || dynamic || name == "*" || type is "object" or "map" or "array" or "any"))
                {
                    throw new XRegistryRejectionException(
                        "model_error", "matchversions requires a static versioned scalar attribute.");
                }
                if (type == "object")
                {
                    ValidateMatchVersions(rule["attributes"] as JsonObject, versioned, dynamic);
                }
                if (type is "array" or "map")
                {
                    JsonObject item = Object(rule["item"]);
                    ValidateMatchVersions(item["attributes"] as JsonObject, false, dynamic);
                }
            }
        }

        private void ImportResources(JsonObject model, JsonObject groups)
        {
            var active = new HashSet<string>(StringComparer.Ordinal);
            var done = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in groups.Select(pair => pair.Key).ToArray())
            {
                Visit(name);
            }
            void Visit(string name)
            {
                if (done.Contains(name))
                {
                    return;
                }
                if (!active.Add(name) || active.Count > 64)
                {
                    throw new XRegistryRejectionException(
                        "model_error", "Resource imports contain a cycle or exceed the depth bound.");
                }
                JsonObject group = Object(groups[name]);
                group["resources"] ??= new JsonObject();
                JsonObject resources = Object(group["resources"]);
                foreach ((string collection, _) in resources)
                {
                    string origin = XRegistryPath.FromSegments([name, collection]);
                    ResourceOrigins[origin] = origin;
                }
                if (group["ximportresources"] is JsonNode imports)
                {
                    if (imports is not JsonArray array)
                    {
                        throw new XRegistryRejectionException("model_error", "ximportresources must be an array.");
                    }
                    foreach (JsonNode? value in array)
                    {
                        ArrayOf<string> target = XRegistryPath.GetSegments(Text(value));
                        if (target.Count != 2 || target[0] == name || !groups.ContainsKey(target[0]))
                        {
                            throw new XRegistryRejectionException(
                                "model_error", "An imported Resource type is invalid.");
                        }
                        Visit(target[0]);
                        JsonNode resource = model["groups"]?[target[0]]?["resources"]?[target[1]]
                            ?? throw new XRegistryRejectionException(
                                "model_error", "The imported Resource type is absent.");
                        if (resources.ContainsKey(target[1]) ||
                            resources.Any(item => Text(item.Value?["singular"]) == Text(resource["singular"])))
                        {
                            throw new XRegistryRejectionException(
                                "model_error", "Imported resource names must be unique.");
                        }
                        resources[target[1]] = resource.DeepClone();
                        ResourceOrigins[XRegistryPath.FromSegments([name, target[1]])] =
                            ResourceOrigins[XRegistryPath.FromSegments(target)]!.DeepClone();
                    }
                    group.Remove("ximportresources");
                }
                active.Remove(name);
                done.Add(name);
            }
        }

        private static void ValidateTypeMap(JsonNode? map)
        {
            if (map is null)
            {
                return;
            }
            foreach ((string key, JsonNode? value) in Object(map))
            {
                if (key.Length == 0 ||
                    key.Count(character => character == '*') > 1 ||
                    Text(value).ToLowerInvariant() is not ("binary" or "json" or "string"))
                {
                    throw new XRegistryRejectionException(
                        "model_error", "The typemap key or representation is invalid.");
                }
            }
        }

        private static readonly HashSet<string> s_types = new(StringComparer.Ordinal)
        {
            "any", "object", "array", "map", "boolean", "integer", "uinteger", "decimal", "string", "timestamp",
            "uri", "url", "uriabsolute", "urlabsolute", "urirelative", "urlrelative", "uritemplate", "xid", "xidtype"
        };

        private static readonly HashSet<string> s_registryModelMembers = new(StringComparer.Ordinal)
        {
            "description", "documentation", "labels", "attributes", "groups"
        };

        private static readonly HashSet<string> s_groupModelMembers = new(StringComparer.Ordinal)
        {
            "plural", "singular", "description", "documentation", "icon", "labels", "modelversion",
            "modelcompatiblewith", "attributes", "ximportresources", "constraints", "resources"
        };

        private static readonly HashSet<string> s_resourceModelMembers = new(StringComparer.Ordinal)
        {
            "plural", "singular", "description", "documentation", "icon", "labels", "modelversion",
            "modelcompatiblewith", "maxversions", "setversionid", "hasdocument", "versionmode",
            "singleversionroot", "validateformat", "validatecompatibility", "strictvalidation", "typemap",
            "attributes", "resourceattributes", "metaattributes"
        };

        private static readonly HashSet<string> s_attributeModelMembers = new(StringComparer.Ordinal)
        {
            "name", "type", "target", "namecharset", "description", "enum", "strict", "matchversions",
            "readonly", "immutable", "required", "default", "attributes", "item", "ifvalues"
        };
    }
}
