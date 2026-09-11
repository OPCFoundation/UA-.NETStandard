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
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    internal sealed class XRegistryModelRules
    {
        public XRegistryModelRules(JsonObject model)
        {
            Model = model;
            if (model["groups"] is not JsonObject groups)
            {
                throw new XRegistryRejectionException("invalid_model", "A registry model requires a groups map.");
            }
            ValidateAttributes(model["attributes"]);
            RejectExternalDefinitions(model);
            foreach ((string name, JsonNode? value) in groups)
            {
                JsonObject group = Object(value);
                ValidateType(name, group);
                ValidateAttributes(group["attributes"]);
                RejectExternalDefinitions(group);
                if (group["resources"] is not JsonObject resources)
                {
                    group["resources"] = new JsonObject();
                    continue;
                }
                foreach ((string resourceName, JsonNode? resourceValue) in resources)
                {
                    JsonObject resource = Object(resourceValue);
                    ValidateType(resourceName, resource);
                    RejectExternalDefinitions(resource);
                    ValidateAttributes(resource["attributes"]);
                    ValidateAttributes(resource["metaattributes"]);
                    resource["maxversions"] ??= 0;
                    _ = Unsigned(resource["maxversions"]);
                    resource["hasdocument"] ??= true;
                    resource["setversionid"] ??= true;
                    resource["singleversionroot"] ??= false;
                    resource["versionmode"] ??= "manual";
                    string mode = Text(resource["versionmode"]).ToLowerInvariant();
                    if (mode is not ("manual" or "createdat" or "modifiedat"))
                    {
                        throw new XRegistryRejectionException("invalid_model",
                            "This provider supports manual, createdat and modifiedat version ordering.");
                    }
                    resource["versionmode"] = mode;
                    if (mode != "manual")
                    {
                        resource["singleversionroot"] = true;
                    }
                    foreach (string flag in new[] { "hasdocument", "setversionid", "singleversionroot" })
                    {
                        _ = Boolean(resource[flag]);
                    }
                    if (Boolean(resource["validateformat"]) ||
                        Boolean(resource["validatecompatibility"]) ||
                        Boolean(resource["strictvalidation"]))
                    {
                        throw new XRegistryRejectionException("invalid_model",
                            "Format/compatibility validation requires a qualified domain endpoint.");
                    }
                }
            }
        }

        public JsonObject Model { get; }

        public XRegistryTarget Resolve(string path)
        {
            ArrayOf<string> parts = XRegistryPath.GetSegments(path);
            if (parts.Count == 0)
            {
                return new(path, XRegistryEntityKind.Registry, Model, "registry");
            }
            if (parts.Count == 1 &&
                parts[0] is
                    "model" or "modelsource" or "capabilities" or "capabilitiesoffered" or "export")
            {
                return new(path, XRegistryEntityKind.Special, Model, parts[0]);
            }
            if (Model["groups"]?[parts[0]] is not JsonObject group)
            {
                throw new XRegistryRejectionException("not_found", "The group collection is not in the model.", 404);
            }
            string singular = Text(group["singular"]);
            if (parts.Count <= 2)
            {
                return new(path, parts.Count == 1 ? XRegistryEntityKind.Groups : XRegistryEntityKind.Group,
                    group, singular);
            }
            if (group["resources"]?[parts[2]] is not JsonObject resource)
            {
                throw new XRegistryRejectionException(
                    "not_found", "The resource collection is not in the model.", 404);
            }
            singular = Text(resource["singular"]);
            if (parts.Count <= 4)
            {
                return new(path, parts.Count == 3 ? XRegistryEntityKind.Resources : XRegistryEntityKind.Resource,
                    resource, singular);
            }
            if (parts.Count == 5 && parts[4] == "meta")
            {
                return new(path, XRegistryEntityKind.Meta, resource, singular);
            }
            if (parts[4] != "versions" || parts.Count > 6)
            {
                throw new XRegistryRejectionException(
                    "not_found", "The path does not address a registry entity.", 404);
            }
            return new(path, parts.Count == 5 ? XRegistryEntityKind.Versions : XRegistryEntityKind.Version,
                resource, singular);
        }

        public static JsonObject Apply(
            JsonObject old, JsonObject input, JsonObject? definitions, bool patch,
            string idName, string id, string kind, HashSet<string>? excluded = null)
        {
            if (input.TryGetPropertyValue(idName, out JsonNode? givenId) && givenId is not null && Text(givenId) != id)
            {
                throw new XRegistryRejectionException(
                    "mismatched_id", "The entity identifier differs from its address.");
            }
            JsonObject result = patch ? (JsonObject)old.DeepClone() : [];
            foreach ((string name, JsonNode? value) in old)
            {
                if (IsManaged(name) ||
                    name == idName ||
                    Boolean(definitions?[name]?["readonly"]) ||
                    Boolean(definitions?[name]?["immutable"]))
                {
                    result[name] = value?.DeepClone();
                }
            }
            foreach ((string name, JsonNode? value) in input)
            {
                if (name == idName || IsManaged(name) || excluded?.Contains(name) == true)
                {
                    continue;
                }
                JsonObject? definition = definitions?[name] as JsonObject ?? definitions?["*"] as JsonObject;
                if (Boolean(definition?["readonly"]) || Boolean(definition?["immutable"]))
                {
                    continue;
                }
                if (definition is null && !IsStandard(name, kind))
                {
                    throw new XRegistryRejectionException(
                        "invalid_attribute", $"Attribute '{name}' is not in the model.");
                }
                if (value is JsonObject nested && definition?["type"]?.GetValue<string>() == "object")
                {
                    result[name] = ApplyNestedObject(result[name] as JsonObject ?? [],
                        nested, definition, patch);
                }
                else if (patch && value is JsonObject map && result[name] is JsonObject existing)
                {
                    result[name] = MergeObject(existing, map, definition);
                }
                else if (value is null)
                {
                    result.Remove(name);
                }
                else
                {
                    result[name] = value.DeepClone();
                }
            }
            result[idName] = id;
            ValidateObject(result, definitions, kind);
            return result;
        }

        public static void ValidateObject(JsonObject value, JsonObject? definitions, string kind)
        {
            if (definitions is not null)
            {
                foreach ((string name, JsonNode? rule) in definitions)
                {
                    if (name == "*")
                    {
                        continue;
                    }
                    JsonObject definition = Object(rule);
                    if (value[name] is null && definition["default"] is JsonNode defaultValue)
                    {
                        value[name] = defaultValue.DeepClone();
                    }
                    if (value[name] is null &&
                        Boolean(definition["required"]) &&
                        !IsManaged(name) &&
                        !Boolean(definition["readonly"]))
                    {
                        throw new XRegistryRejectionException(
                            "invalid_attribute", $"Required attribute '{name}' is missing.");
                    }
                }
            }
            foreach ((string name, JsonNode? node) in value.ToArray())
            {
                if (node is null)
                {
                    value.Remove(name);
                    continue;
                }
                JsonObject? definition = definitions?[name] as JsonObject ?? definitions?["*"] as JsonObject;
                if (definition is null && kind == "extension")
                {
                    throw new XRegistryRejectionException("invalid_attribute",
                        $"Nested attribute '{name}' is not defined in the model.");
                }
                if (definition is not null && (!IsManaged(name) || kind == "extension"))
                {
                    ValidateValue(node, definition, name);
                    if (Text(definition["type"]) == "timestamp" &&
                        TryTimestamp(Text(node), out DateTimeOffset timestamp))
                    {
                        value[name] = timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
                    }
                }
                else if (name is "name" or "description" or "documentation" or "icon" or "contenttype"
                    or "format" or "ancestorid" or "defaultversionid" or "xref")
                {
                    _ = Text(node);
                }
                else if (name is "createdat" or "modifiedat")
                {
                    if (!TryTimestamp(Text(node), out DateTimeOffset timestamp))
                    {
                        throw new XRegistryRejectionException(
                            "invalid_attribute", $"Attribute '{name}' must be a timestamp.");
                    }
                    value[name] = timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
                }
                else if (name == "labels")
                {
                    foreach ((string key, JsonNode? label) in Object(node))
                    {
                        ValidateMapKey(key);
                        _ = Text(label);
                    }
                }
                else if (name == "defaultversionsticky")
                {
                    _ = Boolean(node);
                }
            }
        }

        public static bool Boolean(JsonNode? value, bool fallback = false)
        {
            if (value is null)
            {
                return fallback;
            }
            if (value is JsonValue scalar && scalar.TryGetValue(out bool result))
            {
                return result;
            }
            throw new XRegistryRejectionException("invalid_attribute", "Expected a boolean.");
        }

        public static string Text(JsonNode? value)
        {
            if (value is JsonValue scalar && scalar.TryGetValue(out string? result) && result is not null)
            {
                return result;
            }
            throw new XRegistryRejectionException("invalid_attribute", "Expected a non-null string.");
        }

        public static BigInteger Unsigned(JsonNode? value)
        {
            if (value is null ||
                !BigInteger.TryParse(value.ToJsonString(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out BigInteger result))
            {
                throw new XRegistryRejectionException(
                    "invalid_attribute", "An epoch or limit must be an unsigned integer.");
            }
            return result;
        }

        public static JsonObject Object(JsonNode? value)
        {
            return value as JsonObject ??
                throw new XRegistryRejectionException("bad_request", "Expected a JSON object.");
        }

        public static bool IsManaged(string name)
        {
            return name is "epoch" or "self" or "shortself" or "xid" or "specversion" or "isdefault"
                or "metaurl" or "versionsurl" or "versionscount" or "defaultversionurl" or "readonly"
                or "formatvalidated" or "formatvalidatedreason" or "compatibilityvalidated"
                or "compatibilityvalidatedreason";
        }

        public static bool IsStandard(string name, string kind)
        {
            return name is "name" or "description" or "documentation" or "icon" or "labels" or "createdat"
                or "modifiedat" or "deprecated" ||
                (kind == "version" && name is "contenttype" or "format" or "ancestorid") ||
                (kind == "group" && name == "constraints") ||
                (kind == "meta" && name is "defaultversionid" or "defaultversionsticky" or "xref" or "compatibility");
        }

        private static JsonObject ApplyNestedObject(
            JsonObject old, JsonObject input, JsonObject definition, bool patch)
        {
            var attributes = definition["attributes"] as JsonObject;
            JsonObject result = patch ? (JsonObject)old.DeepClone() : [];
            foreach ((string name, JsonNode? value) in old)
            {
                if (Boolean(attributes?[name]?["readonly"]) || Boolean(attributes?[name]?["immutable"]))
                {
                    result[name] = value?.DeepClone();
                }
            }
            foreach ((string name, JsonNode? value) in input)
            {
                JsonObject? rule = (attributes?[name] as JsonObject ?? attributes?["*"] as JsonObject) ??
                    throw new XRegistryRejectionException(
                        "invalid_attribute", $"Undefined nested attribute '{name}'.");
                if (Boolean(rule["readonly"]) || Boolean(rule["immutable"]))
                {
                    continue;
                }
                if (value is null)
                {
                    result.Remove(name);
                }
                else if (value is JsonObject nested && Text(rule["type"]) == "object")
                {
                    result[name] = ApplyNestedObject(old[name] as JsonObject ?? [], nested, rule, patch);
                }
                else
                {
                    result[name] = value.DeepClone();
                }
            }
            ValidateObject(result, attributes, "extension");
            return result;
        }

        private static JsonObject MergeObject(JsonObject previous, JsonObject input, JsonObject? definition)
        {
            var result = (JsonObject)previous.DeepClone();
            foreach ((string name, JsonNode? value) in input)
            {
                if (Boolean(definition?["attributes"]?[name]?["readonly"]))
                {
                    continue;
                }
                if (value is null)
                {
                    result.Remove(name);
                }
                else if (value is JsonObject nested && result[name] is JsonObject existing)
                {
                    result[name] = MergeObject(existing, nested, definition?["attributes"]?[name] as JsonObject);
                }
                else
                {
                    result[name] = value.DeepClone();
                }
            }
            return result;
        }

        private static void ValidateValue(JsonNode node, JsonObject definition, string name)
        {
            string type = Text(definition["type"]);
            bool valid;
            switch (type)
            {
                case "any":
                    return;
                case "object":
                    ValidateObject(Object(node), definition["attributes"] as JsonObject, "extension");
                    valid = true;
                    break;
                case "map":
                    foreach ((string key, JsonNode? child) in Object(node))
                    {
                        ValidateMapKey(key);
                        ValidateValue(
                            child ?? throw new XRegistryRejectionException("invalid_attribute", "Null map value."),
                            Object(definition["item"]), key);
                    }
                    valid = true;
                    break;
                case "array":
                    if (node is not JsonArray array)
                    {
                        throw new XRegistryRejectionException(
                            "invalid_attribute", $"Attribute '{name}' must be an array.");
                    }
                    foreach (JsonNode? child in array)
                    {
                        ValidateValue(
                            child ?? throw new XRegistryRejectionException("invalid_attribute", "Null array value."),
                            Object(definition["item"]), name);
                    }
                    valid = true;
                    break;
                case "boolean":
                    _ = Boolean(node);
                    valid = true;
                    break;
                case "uinteger":
                    _ = Unsigned(node);
                    valid = true;
                    break;
                case "integer":
                    valid = BigInteger.TryParse(node.ToJsonString(), NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out _);
                    break;
                case "decimal":
                    valid = node.GetValueKind() == JsonValueKind.Number;
                    break;
                case "timestamp":
                    valid = TryTimestamp(Text(node), out _);
                    break;
                case "uri":
                case "url":
                case "uriabsolute":
                case "urlabsolute":
                case "urirelative":
                case "urlrelative":
                    valid = Uri.TryCreate(Text(node),
                        type.EndsWith("absolute", StringComparison.Ordinal)
                            ? UriKind.Absolute
                            : type.EndsWith("relative", StringComparison.Ordinal)
                                ? UriKind.Relative
                                : UriKind.RelativeOrAbsolute,
                        out _);
                    break;
                case "xid":
                    _ = XRegistryPath.Normalize(Text(node));
                    valid = true;
                    break;
                case "string":
                case "uritemplate":
                case "xidtype":
                    _ = Text(node);
                    valid = true;
                    break;
                default:
                    throw new XRegistryRejectionException("invalid_model", $"Unknown attribute type '{type}'.");
            }
            if (!valid ||
                (Boolean(definition["strict"], true) &&
                    definition["enum"] is JsonArray choices &&
                    choices.Count != 0 &&
                    !choices.Any(choice => JsonNode.DeepEquals(choice, node))))
            {
                throw new XRegistryRejectionException("invalid_attribute", $"Attribute '{name}' violates its model.");
            }
        }

        private static void ValidateType(string key, JsonObject type)
        {
            ValidateMapKey(key);
            if (type["plural"] is not null && Text(type["plural"]) != key)
            {
                throw new XRegistryRejectionException("invalid_model", "A collection key must match its plural name.");
            }
            type["plural"] = key;
            ValidateMapKey(Text(type["singular"]));
        }

        private static bool TryTimestamp(string text, out DateTimeOffset result)
        {
            result = default;
            if (text.Length < 20 ||
                text[4] != '-' ||
                text[7] != '-' ||
                text[10] is not ('T' or 't') ||
                text[13] != ':' ||
                text[16] != ':')
            {
                return false;
            }
            for (int index = 0; index < 19; index++)
            {
                if (index is not (4 or 7 or 10 or 13 or 16) && text[index] is not (>= '0' and <= '9'))
                {
                    return false;
                }
            }
            bool utc = text[^1] is 'Z' or 'z';
            int zone = text.Length - (utc ? 1 : 6);
            if (zone < 19)
            {
                return false;
            }
            if (zone != 19)
            {
                if (zone == 20 || text[19] != '.')
                {
                    return false;
                }
                for (int index = 20; index < zone; index++)
                {
                    if (text[index] is not (>= '0' and <= '9'))
                    {
                        return false;
                    }
                }
            }
            if (!utc)
            {
                if (text[zone] is not ('+' or '-') || text[zone + 3] != ':')
                {
                    return false;
                }
                for (int index = zone + 1; index < text.Length; index++)
                {
                    if (index != zone + 3 && text[index] is not (>= '0' and <= '9'))
                    {
                        return false;
                    }
                }
            }
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }

        private static void ValidateAttributes(JsonNode? rules)
        {
            if (rules is null)
            {
                return;
            }
            foreach ((string name, JsonNode? value) in Object(rules))
            {
                JsonObject definition = Object(value);
                string type = Text(definition["type"]);
                if (name != "*")
                {
                    ValidateMapKey(name);
                }
                if (definition["ifvalues"] is not null || definition["target"] is not null)
                {
                    throw new XRegistryRejectionException("invalid_model",
                        "Conditional attributes and typed-reference constraints require a qualified domain endpoint.");
                }
                if (definition["default"] is JsonNode defaultValue)
                {
                    if (type is "array" or "map" or "object" or "any")
                    {
                        throw new XRegistryRejectionException(
                            "model_scalar_default", "Only scalar defaults are supported.");
                    }
                    if (!Boolean(definition["required"]))
                    {
                        throw new XRegistryRejectionException(
                            "model_required_true", "A default requires required=true.");
                    }
                    ValidateValue(defaultValue, definition, name);
                }
                if (type == "object")
                {
                    ValidateAttributes(definition["attributes"]);
                }
                if (type is "array" or "map" && definition["item"] is not JsonObject)
                {
                    throw new XRegistryRejectionException("invalid_model", "Collections require an item definition.");
                }
            }
        }

        private static void RejectExternalDefinitions(JsonObject model)
        {
            foreach (string name in new[] { "ximportresources", "ximport", "xinclude", "includes", "typemap" })
            {
                if (model[name] is not null)
                {
                    throw new XRegistryRejectionException("invalid_model",
                        "External/imported definitions must be materialized by a qualified model resolver.");
                }
            }
        }

        private static void ValidateMapKey(string name)
        {
            if (name.Length is < 1 or > 63 ||
                !IsLowerAlphaNumeric(name[0]) ||
                name.Any(character => !IsLowerAlphaNumeric(character) && character is not ('.' or ':' or '-' or '_')))
            {
                throw new XRegistryRejectionException("invalid_attribute", "Invalid model attribute or map key.");
            }
        }

        private static bool IsLowerAlphaNumeric(char character)
        {
            return character is (>= 'a' and <= 'z') or (>= '0' and <= '9');
        }
    }

    internal enum XRegistryEntityKind
    {
        Registry,
        Groups,
        Group,
        Resources,
        Resource,
        Meta,
        Versions,
        Version,
        Special
    }

    internal sealed record XRegistryTarget(
        string Path, XRegistryEntityKind Kind, JsonObject Definition, string Singular)
    {
        public bool IsCollection => Kind is XRegistryEntityKind.Groups or XRegistryEntityKind.Resources
            or XRegistryEntityKind.Versions;
    }

    /// <summary>
    /// A known core/model processing rejection. The endpoint translates this into
    /// a typed registry error before publication, not an indeterminate transport failure.
    /// </summary>
    public sealed class XRegistryRejectionException : Exception
    {
        /// <summary>
        /// Creates an invalid-request rejection.
        /// </summary>
        public XRegistryRejectionException()
            : this("bad_request", "The registry request is invalid.")
        {
        }

        /// <summary>
        /// Creates an invalid-request rejection with a diagnostic.
        /// </summary>
        public XRegistryRejectionException(string message)
            : this("bad_request", message)
        {
        }

        /// <summary>
        /// Creates an invalid-request rejection with its parsing cause.
        /// </summary>
        public XRegistryRejectionException(string message, Exception innerException)
            : base(message, innerException)
        {
            Code = "bad_request";
            StatusCode = 400;
        }

        /// <summary>
        /// Creates a rejection using the specification's error code and status.
        /// </summary>
        public XRegistryRejectionException(string code, string message, int statusCode = 400)
            : base(message)
        {
            Code = code;
            StatusCode = statusCode;
        }

        /// <summary>
        /// Specification error identifier.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Corresponding HTTP-binding status.
        /// </summary>
        public int StatusCode { get; }
    }
}
