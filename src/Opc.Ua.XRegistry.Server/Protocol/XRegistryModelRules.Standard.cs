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
using System.Text.Json.Nodes;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    internal sealed partial class XRegistryModelRules
    {
        public JsonObject CompleteModel()
        {
            var result = (JsonObject)Model.DeepClone();
            JsonObject registry = CommonAttributes("registryid");
            registry["registryid"]!["readonly"] = true;
            registry["specversion"] = Scalar("specversion", "string", required: true, readOnly: true);
            registry["specversion"]!["default"] = "1.0-rc4";
            registry["model"] = OpenObject("model", readOnly: true);
            registry["modelsource"] = OpenObject("modelsource");
            registry["capabilities"] = OpenObject("capabilities", readOnly: true);
            foreach ((string name, JsonNode? value) in Object(result["groups"]))
            {
                JsonObject group = Object(value);
                AddCollection(registry, name);
                JsonObject groupAttributes = CommonAttributes(Text(group["singular"]) + "id");
                groupAttributes["constraints"] = new JsonObject
                {
                    ["name"] = "constraints",
                    ["type"] = "map",
                    ["item"] = OpenObject("constraint")
                };
                foreach ((string collection, JsonNode? resourceValue) in Object(group["resources"]))
                {
                    JsonObject resource = Object(resourceValue);
                    AddCollection(groupAttributes, collection);
                    CompleteResource(resource);
                }
                group["attributes"] = Overlay(groupAttributes, group["attributes"] as JsonObject);
            }
            result["attributes"] = Overlay(registry, result["attributes"] as JsonObject);
            NormalizeDefinitionNames(result);
            return result;
        }

        private static void NormalizeDefinitionNames(JsonNode node)
        {
            if (node is not JsonObject value)
            {
                return;
            }
            if (value["attributes"] is JsonObject attributes)
            {
                foreach ((string name, JsonNode? definition) in attributes)
                {
                    if (definition is JsonObject rule)
                    {
                        rule["name"] = name;
                    }
                }
            }
            if (value["item"] is JsonObject item)
            {
                item.Remove("name");
            }
            foreach ((_, JsonNode? child) in value)
            {
                if (child is not null)
                {
                    NormalizeDefinitionNames(child);
                }
            }
        }

        private static void CompleteResource(JsonObject resource)
        {
            string singular = Text(resource["singular"]);
            JsonObject version = CommonAttributes("versionid");
            version[singular + "id"] = Scalar(singular + "id", "string", required: true, immutable: true);
            version["isdefault"] = Scalar("isdefault", "boolean", required: true, readOnly: true);
            version["isdefault"]!["default"] = false;
            version["ancestorid"] = Scalar("ancestorid", "string", required: true);
            version["ancestorid"]!["readonly"] = Text(resource["versionmode"]) != "manual";
            version["contenttype"] = Scalar("contenttype", "string");
            version["format"] = Scalar("format", "string");
            foreach (string name in new[] { "formatvalidated", "compatibilityvalidated" })
            {
                version[name] = Scalar(name, "boolean", readOnly: true);
                version[name + "reason"] = Scalar(name + "reason", "string", readOnly: true);
            }
            if (Boolean(resource["hasdocument"], true))
            {
                version[singular] = Scalar(singular, "any");
                version[singular + "base64"] = Scalar(singular + "base64", "string");
                version[singular + "url"] = Scalar(singular + "url", "url");
            }
            JsonObject navigation = IdentityAttributes(singular + "id");
            navigation["meta"] = OpenObject("meta");
            navigation["metaurl"] = Scalar("metaurl", "url", required: true, readOnly: true, immutable: true);
            AddCollection(navigation, "versions");

            JsonObject meta = CommonAttributes(singular + "id");
            meta["xref"] = Scalar("xref", "xid");
            meta["readonly"] = Scalar("readonly", "boolean", required: true, readOnly: true);
            meta["readonly"]!["default"] = false;
            meta["compatibility"] = Scalar("compatibility", "string");
            meta["defaultversionid"] = Scalar("defaultversionid", "string", required: true);
            meta["defaultversionurl"] = Scalar("defaultversionurl", "url", required: true, readOnly: true);
            meta["defaultversionsticky"] = Scalar("defaultversionsticky", "boolean", required: true);
            meta["defaultversionsticky"]!["default"] = false;

            resource["attributes"] = Overlay(version, resource["attributes"] as JsonObject,
                reserved: new HashSet<string>(navigation.Select(pair => pair.Key), StringComparer.Ordinal));
            resource["resourceattributes"] = Overlay(navigation, resource["resourceattributes"] as JsonObject,
                allowExtensions: false);
            resource["metaattributes"] = Overlay(meta, resource["metaattributes"] as JsonObject);
            resource["validateformat"] ??= false;
            resource["validatecompatibility"] ??= false;
            resource["strictvalidation"] ??= false;
        }

        private static JsonObject CommonAttributes(string id)
        {
            JsonObject attributes = IdentityAttributes(id);
            attributes["epoch"] = Scalar("epoch", "uinteger", required: true, readOnly: true);
            attributes["name"] = Scalar("name", "string");
            attributes["description"] = Scalar("description", "string");
            attributes["documentation"] = Scalar("documentation", "url");
            attributes["icon"] = Scalar("icon", "url");
            attributes["labels"] = new JsonObject
            {
                ["name"] = "labels",
                ["type"] = "map",
                ["item"] = new JsonObject { ["type"] = "string" }
            };
            attributes["createdat"] = Scalar("createdat", "timestamp", required: true);
            attributes["modifiedat"] = Scalar("modifiedat", "timestamp", required: true);
            attributes["deprecated"] = new JsonObject
            {
                ["name"] = "deprecated",
                ["type"] = "object",
                ["attributes"] = new JsonObject
                {
                    ["alternative"] = Scalar("alternative", "url"),
                    ["documentation"] = Scalar("documentation", "url"),
                    ["effective"] = Scalar("effective", "timestamp"),
                    ["removal"] = Scalar("removal", "timestamp"),
                    ["*"] = Scalar("*", "any")
                }
            };
            return attributes;
        }

        private static JsonObject IdentityAttributes(string id)
        {
            return new JsonObject
            {
                [id] = Scalar(id, "string", required: true, immutable: true),
                ["self"] = Scalar("self", "url", required: true, readOnly: true, immutable: true),
                ["shortself"] = Scalar("shortself", "url", readOnly: true, immutable: true),
                ["xid"] = Scalar("xid", "xid", required: true, readOnly: true, immutable: true)
            };
        }

        private static void AddCollection(JsonObject attributes, string name)
        {
            if (attributes.ContainsKey(name) ||
                attributes.ContainsKey(name + "url") ||
                attributes.ContainsKey(name + "count"))
            {
                throw new XRegistryRejectionException(
                    "model_error", "A collection conflicts with a standard attribute.");
            }
            attributes[name] = new JsonObject
            {
                ["name"] = name,
                ["type"] = "map",
                ["item"] = OpenObject("entity")
            };
            attributes[name + "url"] = Scalar(name + "url", "url", required: true, readOnly: true, immutable: true);
            attributes[name + "count"] = Scalar(name + "count", "uinteger", required: true, readOnly: true);
        }

        private static JsonObject Scalar(
            string name, string type, bool required = false, bool readOnly = false, bool immutable = false)
        {
            var rule = new JsonObject { ["name"] = name, ["type"] = type };
            if (required)
            {
                rule["required"] = true;
            }
            if (readOnly)
            {
                rule["readonly"] = true;
            }
            if (immutable)
            {
                rule["immutable"] = true;
            }
            return rule;
        }

        private static JsonObject OpenObject(string name, bool readOnly = false)
        {
            JsonObject rule = Scalar(name, "object", readOnly: readOnly);
            rule["attributes"] = new JsonObject { ["*"] = Scalar("*", "any") };
            return rule;
        }

        private static JsonObject Overlay(JsonObject standard, JsonObject? declared,
            bool allowExtensions = true, HashSet<string>? reserved = null)
        {
            if (declared is null)
            {
                return standard;
            }
            foreach ((string name, JsonNode? node) in declared)
            {
                JsonObject rule = Object(node);
                if (standard[name] is not JsonObject original)
                {
                    if (!allowExtensions || reserved?.Contains(name) == true || rule.ContainsKey("immutable"))
                    {
                        throw new XRegistryRejectionException("model_error",
                            "The extension conflicts with protected model semantics.");
                    }
                    standard[name] = rule.DeepClone();
                    continue;
                }
                standard[name] = OverlayRule(original, rule);
            }
            return standard;
        }

        private static JsonObject OverlayRule(JsonObject original, JsonObject rule)
        {
            if (Text(original["type"]) != Text(rule["type"]))
            {
                throw new XRegistryRejectionException("model_error", "A standard attribute's type cannot be changed.");
            }
            foreach (string flag in new[] { "readonly", "required", "immutable" })
            {
                if (Boolean(original[flag]) && rule.ContainsKey(flag) && !Boolean(rule[flag]))
                {
                    throw new XRegistryRejectionException(
                        "model_error", "A protected attribute aspect cannot be relaxed.");
                }
            }
            if (original["default"] is not null && rule.ContainsKey("default") && rule["default"] is null)
            {
                throw new XRegistryRejectionException("model_error", "A standard default cannot be removed.");
            }
            foreach ((string name, JsonNode? value) in rule)
            {
                if (name == "attributes" && original[name] is JsonObject attributes)
                {
                    original[name] = Overlay((JsonObject)attributes.DeepClone(), Object(value));
                }
                else if (name == "item" && original[name] is JsonObject item)
                {
                    original[name] = OverlayRule((JsonObject)item.DeepClone(), Object(value));
                }
                else
                {
                    original[name] = value?.DeepClone();
                }
            }
            return original;
        }
    }
}
