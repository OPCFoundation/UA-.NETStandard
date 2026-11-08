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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    internal static class XRegistryNativeAttributePlan
    {
        public static void Validate(ArrayOf<XRegistryNativeAttributeMapping> mappings)
        {
            if (mappings.Count > 256)
            {
                throw new ArgumentException("Native mapping profiles are limited to 256 properties.");
            }
            for (int index = 0; index < mappings.Count; index++)
            {
                XRegistryNativeAttributeMapping mapping = mappings[index].ThrowIfNull(nameof(mappings));
                if (mapping.Encoding is
                        < XRegistryNativeAttributeEncoding.Typed or
                        > XRegistryNativeAttributeEncoding.CanonicalString ||
                    (mapping.NativeType is < BuiltInType.Null or > BuiltInType.DateTime &&
                        !(mapping.NativeType == BuiltInType.ExtensionObject && mapping.StructureType is not null)) ||
                    (mapping.Encoding == XRegistryNativeAttributeEncoding.CanonicalString &&
                        (mapping.NativeType is not (BuiltInType.Null or BuiltInType.String) ||
                            mapping.StructureType is not null)) ||
                    (mapping.StructureType is not null &&
                        (mapping.StructureTypeId.IsNull ||
                            mapping.StructureTypeId.ServerIndex != 0 ||
                            (string.IsNullOrEmpty(mapping.StructureTypeId.NamespaceUri) &&
                                mapping.StructureTypeId.InnerNodeId.NamespaceIndex != 0) ||
                            mapping.NativeType is not (BuiltInType.Null or BuiltInType.ExtensionObject))) ||
                    (mapping.StructureType is null && !mapping.StructureTypeId.IsNull) ||
                    s_identity.Contains(mapping.AttributePath[0]))
                {
                    throw new ArgumentException("A native mapping uses a reserved identity or unsupported encoding.");
                }
                for (int previous = 0; previous < index; previous++)
                {
                    XRegistryNativeAttributeMapping other = mappings[previous];
                    bool sameOwner =
                        other.Scope == mapping.Scope ||
                        (other.Scope is XRegistryNativeAttributeScope.Meta or XRegistryNativeAttributeScope.Resource &&
                            mapping.Scope is
                                XRegistryNativeAttributeScope.Meta or XRegistryNativeAttributeScope.Resource);
                    if (sameOwner &&
                        other.ModelPath == mapping.ModelPath &&
                        ((other.Scope == mapping.Scope && Overlaps(mapping.AttributePath, other.AttributePath)) ||
                            NativeKey(mapping).StartsWith(NativeKey(other), StringComparison.Ordinal) ||
                            NativeKey(other).StartsWith(NativeKey(mapping), StringComparison.Ordinal)))
                    {
                        throw new ArgumentException(
                            "Native mapping profiles have overlapping logical or native paths.");
                    }
                }
            }
        }

        public static JsonElement Attributes(JsonElement entity, XRegistryNativeAttributeScope scope)
        {
            string name = scope == XRegistryNativeAttributeScope.Meta ? "metaattributes" : "attributes";
            return entity.TryGetProperty(name, out JsonElement value) ? value : default;
        }

        public static JsonElement Rule(
            XRegistryNativeAttributeMapping mapping, JsonElement entity, JsonElement metadata)
        {
            if (TryActiveRule(mapping, entity, metadata, out JsonElement rule) ||
                XRegistryAttributeModel.TryResolveDeclared(
                    Attributes(entity, mapping.Scope), mapping.AttributePath, out rule))
            {
                return rule;
            }
            throw new InvalidDataException("The native mapping has no model-defined logical attribute.");
        }

        public static bool TryActiveRule(
            XRegistryNativeAttributeMapping mapping, JsonElement entity, JsonElement metadata, out JsonElement rule)
        {
            if (entity.TryGetProperty("singular", out JsonElement singular) &&
                mapping.AttributePath[0] == singular.GetString() + "id")
            {
                throw new InvalidDataException("A mapping cannot override the entity's structural identity.");
            }
            if (XRegistryAttributeModel.TryResolve(Attributes(entity, mapping.Scope), metadata, mapping.AttributePath,
                out rule))
            {
                return true;
            }
            if (mapping.AttributePath.Count == 2 && mapping.AttributePath[0] == "labels")
            {
                using var label = JsonDocument.Parse("""{"type":"string"}""");
                rule = label.RootElement.Clone();
                return true;
            }
            return false;
        }

        public static JsonElement Value(JsonElement metadata, ArrayOf<string> path)
        {
            foreach (string part in path)
            {
                if (metadata.ValueKind != JsonValueKind.Object || !metadata.TryGetProperty(part, out metadata))
                {
                    return default;
                }
            }
            return metadata;
        }

        public static void SetValue(JsonObject metadata, ArrayOf<string> path, JsonElement value)
        {
            JsonObject parent = metadata;
            for (int index = 0; index < path.Count - 1; index++)
            {
                if (!parent.TryGetPropertyValue(path[index], out JsonNode? child))
                {
                    var created = new JsonObject();
                    parent.Add(path[index], created);
                    parent = created;
                }
                else
                {
                    parent = child as JsonObject
                        ?? throw new InvalidDataException("Native mappings conflict on a logical object path.");
                }
            }
            parent[path[^1]] = JsonNode.Parse(value.GetRawText());
        }

        public static void Preflight(
            JsonElement metadata,
            JsonElement definition,
            string path,
            ArrayOf<XRegistryNativeAttributeMapping> mappings)
        {
            ArrayOf<XRegistryNativeAttributeMapping> relevant =
                [.. mappings.ToList().Where(item => item.Matches(path))];
            foreach (string name in relevant.ToList().Select(item => item.AttributePath[0])
                .Distinct(StringComparer.Ordinal))
            {
                if (name != "labels" &&
                    metadata.TryGetProperty(name, out JsonElement value) &&
                    !CoversValue(value, [name], relevant))
                {
                    throw new InvalidDataException(
                        "The native leaf mappings cannot preserve the complete logical value.");
                }
            }
            foreach (XRegistryNativeAttributeMapping mapping in mappings)
            {
                if (!mapping.Matches(path))
                {
                    continue;
                }
                bool active = TryActiveRule(mapping, definition, metadata, out JsonElement rule);
                if (!active)
                {
                    rule = Rule(mapping, definition, metadata);
                }
                if (mapping.AttributePath[0] != "labels" &&
                    mapping.BrowsePath.Count == 2 &&
                    mapping.BrowsePath[0].NamespaceUri == XRegistryWellKnown.XRegistryNamespaceUri &&
                    mapping.BrowsePath[0].Name is "Labels" or "MetaLabels" &&
                    metadata.TryGetProperty("labels", out JsonElement labels) &&
                    labels.ValueKind == JsonValueKind.Object &&
                    labels.TryGetProperty(mapping.BrowsePath[1].Name, out _))
                {
                    throw new InvalidDataException(
                        "A native label key conflicts with an explicitly mapped logical attribute.");
                }
                JsonElement value = Value(metadata, mapping.AttributePath);
                if (!active)
                {
                    if (value.ValueKind != JsonValueKind.Undefined)
                    {
                        throw new InvalidDataException(
                            "An inactive conditional attribute cannot be projected as live data.");
                    }
                    continue;
                }
                if (value.ValueKind == JsonValueKind.Undefined)
                {
                    if (Required(rule))
                    {
                        throw new InvalidDataException("A required mapped native attribute is absent.");
                    }
                    continue;
                }
                _ = XRegistryNativeAttributeCodec.Encode(value, rule, mapping);
            }
        }

        public static bool CoversDefinition(
            JsonElement definition, ArrayOf<string> path, ArrayOf<XRegistryNativeAttributeMapping> mappings)
        {
            if (TargetsWholeValue(path, mappings))
            {
                return true;
            }
            return path.Count < 32 &&
                definition.TryGetProperty("type", out JsonElement type) &&
                type.GetString() == "object" &&
                definition.TryGetProperty("attributes", out JsonElement attributes) &&
                CoversAttributes(attributes, path, mappings);
        }

        public static bool Required(JsonElement rule)
        {
            return rule.TryGetProperty("required", out JsonElement required) &&
                required.ValueKind == JsonValueKind.True;
        }

        public static IEnumerable<(string Path, JsonElement Metadata, JsonElement Definition)> Entities(
            XRegistryNativeSnapshot snapshot)
        {
            yield return ("/", snapshot.Root, snapshot.Description.Model);
            foreach (XRegistryNativeGroup group in snapshot.NativeGroups.ToList())
            {
                yield return (group.Path, group.Metadata, group.Definition);
                foreach (XRegistryNativeResource resource in group.NativeResources.ToList())
                {
                    if (resource.HasVersion)
                    {
                        yield return (resource.VersionPath, resource.Metadata, resource.Definition);
                    }
                    if (resource.IsDefaultVersion || !resource.HasVersion)
                    {
                        yield return (resource.Path, resource.Metadata, resource.Definition);
                        yield return (resource.Path + "/meta", resource.Meta, resource.Definition);
                    }
                }
            }
        }

        public static string NativeKey(XRegistryNativeAttributeMapping mapping)
        {
            return string.Concat(mapping.BrowsePath.ToList().Select(part =>
                Uri.EscapeDataString(part.NamespaceUri) + ":" + Uri.EscapeDataString(part.Name) + "/"));
        }

        private static bool CoversAttributes(
            JsonElement attributes, ArrayOf<string> path,
            ArrayOf<XRegistryNativeAttributeMapping> mappings, bool allowEmpty = false)
        {
            if (attributes.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            bool any = false;
            foreach (JsonProperty attribute in attributes.EnumerateObject())
            {
                any = true;
                if (attribute.Name == "*" ||
                    !CoversDefinition(attribute.Value, [.. path, attribute.Name], mappings))
                {
                    return false;
                }
                if (attribute.Value.TryGetProperty("ifvalues", out JsonElement branches))
                {
                    foreach (JsonProperty branch in branches.EnumerateObject())
                    {
                        if (branch.Value.TryGetProperty("siblingattributes", out JsonElement siblings) &&
                            !CoversAttributes(siblings, path, mappings, allowEmpty: true))
                        {
                            return false;
                        }
                    }
                }
            }
            return any || allowEmpty;
        }

        private static bool CoversValue(
            JsonElement value, ArrayOf<string> path, ArrayOf<XRegistryNativeAttributeMapping> mappings)
        {
            if (TargetsWholeValue(path, mappings))
            {
                return true;
            }
            if (path.Count >= 32 || value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            bool any = false;
            foreach (JsonProperty child in value.EnumerateObject())
            {
                any = true;
                if (!CoversValue(child.Value, [.. path, child.Name], mappings))
                {
                    return false;
                }
            }
            return any;
        }

        private static bool TargetsWholeValue(
            ArrayOf<string> path, ArrayOf<XRegistryNativeAttributeMapping> mappings)
        {
            foreach (XRegistryNativeAttributeMapping mapping in mappings)
            {
                if (mapping.AttributePath.Span.SequenceEqual(path.Span))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Overlaps(ArrayOf<string> first, ArrayOf<string> second)
        {
            int length = Math.Min(first.Count, second.Count);
            return first.Span[..length].SequenceEqual(second.Span[..length]);
        }

        private static readonly HashSet<string> s_identity = new(StringComparer.Ordinal)
        {
            "registryid", "versionid", "epoch", "self", "shortself", "xid", "specversion"
        };
    }
}
