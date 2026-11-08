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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
    internal static class XRegistryConstraintRules
    {
        public static void ValidateGroup(JsonObject group, JsonObject? instance = null)
        {
            if (group["constraints"] is not (null or JsonObject))
            {
                throw new XRegistryRejectionException("constraint_failure", "Group constraints must be an object.");
            }
            JsonObject resources = XRegistryModelRules.Object(group["resources"]);
            foreach (JsonObject? source in new[] { group["constraints"] as JsonObject, instance })
            {
                if (source is null)
                {
                    continue;
                }
                foreach ((string name, _) in source)
                {
                    List<XRegistryQueryStep> path = XRegistryQueryPath.Parse(name, "constraint_failure");
                    if (path[0].Array || path[0].Wildcard || !resources.ContainsKey(path[0].Name))
                    {
                        throw new XRegistryRejectionException(
                            "constraint_failure", "The constrained Resource type is undefined.");
                    }
                }
            }
            foreach ((string name, JsonNode? definition) in resources)
            {
                _ = Apply(group, instance, name, definition?["attributes"] as JsonObject);
            }
        }

        public static JsonObject? Apply(JsonObject group, JsonObject? instance, string resource, JsonObject? attributes)
        {
            JsonObject full = XRegistryModelRules.Object(group["resources"]?[resource]?["attributes"]);
            JsonObject constraints = Effective(group["constraints"] as JsonObject, instance, resource);
            JsonObject? result = attributes is null ? null : (JsonObject)attributes.DeepClone();
            foreach ((string path, JsonNode? value) in constraints)
            {
                JsonObject constraint = XRegistryModelRules.Object(value);
                if (constraint.Any(pair => pair.Key is not ("default" or "enum" or "equals")) ||
                    constraint["enum"] is not (null or JsonArray))
                {
                    throw new XRegistryRejectionException(
                        "constraint_failure", "A constraint contains unsupported members or an invalid enum.");
                }
                JsonObject rule = (JsonObject)StaticRule(full, path).DeepClone();
                if (constraint["enum"] is JsonArray choices && choices.Count != 0)
                {
                    foreach (JsonNode? choice in choices)
                    {
                        XRegistryModelRules.ValidateConstraintValue(choice
                            ?? throw new XRegistryRejectionException(
                                "constraint_failure", "Null is not a constraint enum value."),
                            rule, path);
                    }
                    rule["enum"] = choices.DeepClone();
                    rule["strict"] = true;
                }
                if ((constraint["default"] ?? rule["default"]) is JsonNode defaultValue)
                {
                    XRegistryModelRules.ValidateConstraintValue(defaultValue, rule, path);
                }
                if (constraint["default"] is JsonNode replacement)
                {
                    if (XRegistryModelRules.Boolean(rule["readonly"]) || XRegistryModelRules.Boolean(rule["immutable"]))
                    {
                        throw new XRegistryRejectionException(
                            "constraint_failure", "A constraint cannot default a server-controlled value.");
                    }
                    result ??= [];
                    XRegistryQueryStep first = XRegistryQueryPath.Parse(path, "constraint_failure")[0];
                    result[first.Name] ??= full[first.Name]!.DeepClone();
                    JsonObject overridden = StaticRule(result, path);
                    overridden["default"] = replacement.DeepClone();
                    overridden["required"] = true;
                }
                if (constraint["equals"] is JsonNode reference && XRegistryModelRules.Text(reference).Length != 0)
                {
                    JsonObject other = StaticRule(XRegistryModelRules.Object(group["attributes"]),
                        XRegistryModelRules.Text(reference));
                    if (XRegistryModelRules.Text(other["type"]) != XRegistryModelRules.Text(rule["type"]))
                    {
                        throw new XRegistryRejectionException(
                            "model_error", "Equality constraints require matching scalar types.");
                    }
                }
            }
            return result;
        }

        public static JsonObject Effective(JsonObject? declared, JsonObject? instance, string resource)
        {
            var result = new JsonObject();
            foreach (JsonObject? source in new[] { declared, instance })
            {
                if (source is null)
                {
                    continue;
                }
                foreach ((string key, JsonNode? node) in source)
                {
                    if (key == resource && node is JsonObject legacy)
                    {
                        foreach ((string path, JsonNode? rule) in legacy)
                        {
                            Merge(result, CanonicalPath(XRegistryQueryPath.Parse(path, "constraint_failure")),
                                XRegistryModelRules.Object(rule));
                        }
                        continue;
                    }
                    List<XRegistryQueryStep> steps = XRegistryQueryPath.Parse(key, "constraint_failure");
                    if (steps.Count < 2 || steps[0].Name != resource)
                    {
                        continue;
                    }
                    if (steps.Any(step => step.Array || step.Wildcard))
                    {
                        throw new XRegistryRejectionException(
                            "constraint_failure", "Constraint paths must be static object members.");
                    }
                    string pathKey = CanonicalPath(steps.Skip(1));
                    Merge(result, pathKey, XRegistryModelRules.Object(node));
                }
            }
            return result;
        }

        private static string CanonicalPath(IEnumerable<XRegistryQueryStep> steps)
        {
            return string.Concat(steps.Select(step => "[\"" + step.Name + "\"]"));
        }

        private static JsonObject StaticRule(JsonObject attributes, string path)
        {
            List<XRegistryQueryStep> steps = XRegistryQueryPath.Parse(path, "constraint_failure");
            JsonObject scope = attributes;
            for (int index = 0; index < steps.Count; index++)
            {
                XRegistryQueryStep step = steps[index];
                if (step.Array || step.Wildcard || scope[step.Name] is not JsonObject rule)
                {
                    throw new XRegistryRejectionException(
                        "constraint_failure", "A constraint requires a statically defined attribute.");
                }
                string type = XRegistryModelRules.Text(rule["type"]);
                if (index == steps.Count - 1)
                {
                    return type is "object" or "map" or "array" or "any"
                        ? throw new XRegistryRejectionException(
                            "constraint_failure", "A constraint requires a scalar attribute.")
                        : rule;
                }
                if (type != "object")
                {
                    throw new XRegistryRejectionException(
                        "constraint_failure", "A constraint cannot cross a map, array or scalar.");
                }
                scope = XRegistryModelRules.Object(rule["attributes"]);
            }
            throw new XRegistryRejectionException("constraint_failure", "A constraint path is empty.");
        }

        private static void Merge(JsonObject result, string path, JsonObject rule)
        {
            if (result[path] is not JsonObject previous)
            {
                result[path] = rule.DeepClone();
                return;
            }
            if (previous["enum"] is JsonArray prior &&
                prior.Count != 0 &&
                rule["enum"] is JsonArray next &&
                (next.Count == 0 || next.Any(value => !prior.Any(old => JsonNode.DeepEquals(value, old)))))
            {
                throw new XRegistryRejectionException(
                    "constraint_failure", "An instance constraint cannot widen model values.");
            }
            foreach ((string name, JsonNode? value) in rule)
            {
                if (value is null && name == "enum" && previous["enum"] is JsonArray { Count: > 0 })
                {
                    throw new XRegistryRejectionException(
                        "constraint_failure", "A model enumeration cannot be removed.");
                }
                previous[name] = value?.DeepClone();
            }
        }
    }
}
