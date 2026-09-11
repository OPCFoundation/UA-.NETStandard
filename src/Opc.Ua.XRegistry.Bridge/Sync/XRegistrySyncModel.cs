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
#if NETFRAMEWORK || NETSTANDARD2_0
using System.Collections.Generic;
#endif
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    internal sealed class XRegistrySyncModel
    {
        public XRegistrySyncModel(JsonElement model, XRegistrySyncOptions options)
        {
            m_options = options;
            JsonObject normalized = XRegistrySyncJson.Object(model);
            normalized["attributes"] ??= new JsonObject();
            JsonObject groups = RequireObject(normalized["groups"]);
            foreach ((string name, JsonNode? value) in groups)
            {
                JsonObject group = RequireObject(value);
                NormalizeCollection(name, group);
                group["resources"] ??= new JsonObject();
                foreach ((string resourceName, JsonNode? resourceValue) in RequireObject(group["resources"]))
                {
                    JsonObject resource = RequireObject(resourceValue);
                    NormalizeCollection(resourceName, resource);
                    resource["metaattributes"] ??= new JsonObject();
                    resource["hasdocument"] ??= true;
                    resource["setversionid"] ??= true;
                    resource["maxversions"] ??= 0;
                    resource["versionmode"] ??= "manual";
                    resource["singleversionroot"] ??= false;
                    _ = Boolean(resource, "hasdocument");
                    _ = Boolean(resource, "setversionid");
                    _ = Boolean(resource, "singleversionroot");
                }
            }
            Document = XRegistrySyncJson.Element(normalized, options.MaximumInventoryBytes, options.MaximumJsonDepth);
            Signature = XRegistrySyncJson.Fingerprint(
                normalized, options.MaximumInventoryBytes, options.MaximumJsonDepth);
        }

        public JsonElement Document { get; }

        public string Signature { get; }

        public ArrayOf<string> GroupCollections =>
        [
            .. Document.GetProperty("groups").EnumerateObject()
                .Select(p => p.Name).OrderBy(p => p, StringComparer.Ordinal)
        ];

        public ArrayOf<string> ResourceCollections(string groupPath)
        {
            ArrayOf<string> segments = XRegistryPath.GetSegments(groupPath);
            return
            [
                .. Document.GetProperty("groups").GetProperty(segments[0]).GetProperty("resources")
                    .EnumerateObject().Select(p => p.Name).OrderBy(p => p, StringComparer.Ordinal)
            ];
        }

        public XRegistrySyncDefinition Resolve(string path)
        {
            ArrayOf<string> segments = XRegistryPath.GetSegments(path);
            if (segments.Count == 0)
            {
                return new XRegistrySyncDefinition(XRegistrySyncEntityKind.Registry, Document, "registry", path);
            }
            if (segments.Count < 2 ||
                !Document.GetProperty("groups").TryGetProperty(segments[0], out JsonElement group))
            {
                throw new JsonException("The entity is outside the effective model.");
            }
            if (segments.Count == 2)
            {
                return new XRegistrySyncDefinition(XRegistrySyncEntityKind.Group, group,
                    XRegistrySyncJson.String(group, "singular"), path);
            }
            if (segments.Count < 4 ||
                !group.GetProperty("resources").TryGetProperty(segments[2], out JsonElement resource))
            {
                throw new JsonException("The resource is outside the effective model.");
            }
            XRegistrySyncEntityKind kind = segments.Count switch
            {
                5 when segments[4] == "meta" => XRegistrySyncEntityKind.ResourceMeta,
                6 when segments[4] == "versions" => XRegistrySyncEntityKind.Version,
                _ => throw new JsonException("Only Resource Meta and exact Version identities are synchronized.")
            };
            return new XRegistrySyncDefinition(kind, resource,
                XRegistrySyncJson.String(resource, "singular"), path);
        }

        public XRegistrySyncObservation Observe(string path, JsonElement metadata, ByteString bytes)
        {
            XRegistrySyncDefinition definition = Resolve(path);
            string epoch = XRegistrySyncJson.Epoch(metadata);
            JsonObject meaningful = XRegistrySyncJson.Object(metadata);
            ValidateIdentity(definition, meaningful);
            foreach (string name in s_generated)
            {
                meaningful.Remove(name);
            }
            meaningful.Remove(definition.Singular + "id");
            if (definition.Kind == XRegistrySyncEntityKind.Registry)
            {
                foreach (string name in new[]
                {
                    "registryid", "model", "modelsource", "capabilities", "capabilitiesoffered"
                })
                {
                    meaningful.Remove(name);
                }
                RemoveCollections(meaningful, GroupCollections);
            }
            else if (definition.Kind == XRegistrySyncEntityKind.Group)
            {
                RemoveCollections(meaningful, ResourceCollections(path));
            }
            else if (definition.Kind == XRegistrySyncEntityKind.Version)
            {
                meaningful.Remove("versionid");
                meaningful.Remove(definition.Singular);
                meaningful.Remove(definition.Singular + "base64");
                meaningful.Remove("meta");
                meaningful.Remove("versions");
            }
            if (bytes.Length > m_options.MaximumDocumentBytes)
            {
                throw new JsonException("A document exceeds the synchronization byte limit.");
            }
            JsonElement canonical = XRegistrySyncJson.Element(
                meaningful, m_options.MaximumInventoryBytes, m_options.MaximumJsonDepth);
            return new XRegistrySyncObservation(
                path, definition.Kind,
                Fingerprint(path, definition.Kind, canonical, bytes, m_options.MaximumInventoryBytes,
                    m_options.MaximumJsonDepth),
                epoch, canonical, bytes);
        }

        public static string Fingerprint(
            string path,
            XRegistrySyncEntityKind kind,
            JsonElement metadata,
            ByteString document,
            int maximumBytes = 67_108_864,
            int maximumDepth = 64)
        {
            var value = new JsonObject
            {
                ["path"] = path,
                ["kind"] = (int)kind,
                ["metadata"] = XRegistrySyncJson.Object(metadata),
                ["document"] = document.IsNull ? null : Convert.ToBase64String(document.ToArray())
            };
            return XRegistrySyncJson.Fingerprint(value, maximumBytes, maximumDepth);
        }

        public static string? UnsupportedWrite(
            XRegistrySyncDefinition definition,
            XRegistrySyncObservation source,
            XRegistrySyncObservation? destination)
        {
            if (definition.Kind is XRegistrySyncEntityKind.ResourceMeta or XRegistrySyncEntityKind.Version)
            {
                JsonElement model = definition.Model;
                if (XRegistrySyncJson.String(model, "versionmode") != "manual" ||
                    model.GetProperty("maxversions").GetRawText() != "0")
                {
                    return "Automatic version ordering or retention needs a qualified compound operation.";
                }
                if (source.Metadata.TryGetProperty("xref", out _) ||
                    source.Metadata.TryGetProperty(definition.Singular + "url", out _))
                {
                    return "External resource/document references are not synchronized.";
                }
                if (destination is null && !model.GetProperty("setversionid").GetBoolean())
                {
                    return "Server-assigned version identities cannot be safely recreated.";
                }
                if (destination is not null &&
                    definition.Kind == XRegistrySyncEntityKind.Version &&
                    !AttributeEqual(source.Metadata, destination.Metadata, "ancestorid"))
                {
                    return "Changing ancestry needs guarded Resource Meta and descendant operations.";
                }
            }
            string attributes = definition.Kind == XRegistrySyncEntityKind.ResourceMeta
                ? "metaattributes" : "attributes";
            if (definition.Model.TryGetProperty(attributes, out JsonElement rules) &&
                HasProtectedAttribute(source.Metadata, rules))
            {
                return "Read-only or immutable model attributes need a qualified domain mapping.";
            }
            return null;
        }

        public static bool AttributeEqual(JsonElement first, JsonElement second, string name)
        {
            bool hasFirst = first.TryGetProperty(name, out JsonElement a);
            bool hasSecond = second.TryGetProperty(name, out JsonElement b);
            if (hasFirst != hasSecond)
            {
                return false;
            }
            if (!hasFirst)
            {
                return true;
            }
            var left = new JsonObject { ["value"] = XRegistrySyncJson.Canonicalize(a) };
            var right = new JsonObject { ["value"] = XRegistrySyncJson.Canonicalize(b) };
            return XRegistrySyncJson.Fingerprint(left) == XRegistrySyncJson.Fingerprint(right);
        }

        public static string Child(string parent, string id)
        {
            return XRegistryPath.FromSegments([.. XRegistryPath.GetSegments(parent), id]);
        }

        public static string Parent(string path)
        {
            ArrayOf<string> segments = XRegistryPath.GetSegments(path);
            return XRegistryPath.FromSegments(segments.Span[..(segments.Count - 1)].ToArray());
        }

        public static string ResourcePath(string path)
        {
            ArrayOf<string> segments = XRegistryPath.GetSegments(path);
            return XRegistryPath.FromSegments(segments.Span[..4].ToArray());
        }

        public static bool Overlaps(string first, string second)
        {
            string a = HierarchyPath(first);
            string b = HierarchyPath(second);
            return a == b ||
                a == "/" ||
                b == "/" ||
                a.StartsWith(b + "/", StringComparison.Ordinal) ||
                b.StartsWith(a + "/", StringComparison.Ordinal);
        }

        private static string HierarchyPath(string path)
        {
            ArrayOf<string> segments = XRegistryPath.GetSegments(path);
            return segments.Count == 5 && segments[4] == "meta" ? Parent(path) : path;
        }

        private static void NormalizeCollection(string name, JsonObject definition)
        {
            _ = XRegistryPath.FromSegments([name]);
            if (definition["plural"] is not null && definition["plural"]!.GetValue<string>() != name)
            {
                throw new JsonException("A model collection key differs from its plural identity.");
            }
            string singular = definition["singular"]?.GetValue<string>() ??
                throw new JsonException("A model collection requires a singular identity.");
            _ = XRegistryPath.FromSegments([singular]);
            definition["plural"] = name;
            definition["attributes"] ??= new JsonObject();
        }

        private static bool Boolean(JsonObject definition, string name)
        {
            return definition[name]?.GetValue<bool>() ?? throw new JsonException($"Expected boolean '{name}'.");
        }

        private static JsonObject RequireObject(JsonNode? node)
        {
            return node as JsonObject ?? throw new JsonException("The model requires an object.");
        }

        private static void RemoveCollections(JsonObject metadata, ArrayOf<string> names)
        {
            foreach (string name in names)
            {
                metadata.Remove(name);
                metadata.Remove(name + "url");
                metadata.Remove(name + "count");
            }
        }

        private static void ValidateIdentity(XRegistrySyncDefinition definition, JsonObject metadata)
        {
            if (definition.Kind == XRegistrySyncEntityKind.Registry)
            {
                return;
            }
            ArrayOf<string> segments = XRegistryPath.GetSegments(definition.Path);
            string idName = definition.Kind == XRegistrySyncEntityKind.Version
                ? "versionid" : definition.Singular + "id";
            string id = definition.Kind switch
            {
                XRegistrySyncEntityKind.Group => segments[1],
                XRegistrySyncEntityKind.ResourceMeta => segments[3],
                _ => segments[5]
            };
            if (metadata[idName]?.GetValue<string>() != id)
            {
                throw new JsonException("An entity's returned identity differs from its full collection path.");
            }
            if (definition.Kind == XRegistrySyncEntityKind.Version &&
                metadata[definition.Singular + "id"] is JsonNode resourceId &&
                resourceId.GetValue<string>() != segments[3])
            {
                throw new JsonException("A Version belongs to another resource.");
            }
        }

        private static bool HasProtectedAttribute(JsonElement metadata, JsonElement rules)
        {
            if (rules.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Attribute rules must be a model object.");
            }
            foreach (JsonProperty property in metadata.EnumerateObject())
            {
                if (!rules.TryGetProperty(property.Name, out JsonElement rule) &&
                    !rules.TryGetProperty("*", out rule))
                {
                    continue;
                }
                if ((rule.TryGetProperty("readonly", out JsonElement readOnly) && readOnly.GetBoolean()) ||
                    (rule.TryGetProperty("immutable", out JsonElement immutable) && immutable.GetBoolean()))
                {
                    return true;
                }
                if (property.Value.ValueKind == JsonValueKind.Object &&
                    rule.TryGetProperty("attributes", out JsonElement nested) &&
                    HasProtectedAttribute(property.Value, nested))
                {
                    return true;
                }
            }
            return false;
        }

        private readonly XRegistrySyncOptions m_options;

        private static readonly string[] s_generated =
        [
            "epoch", "createdat", "modifiedat", "self", "shortself", "xid", "specversion",
            "xregcorrelationid", "isdefault", "metaurl", "versionsurl", "versionscount",
            "defaultversionurl", "readonly", "formatvalidated", "formatvalidatedreason",
            "compatibilityvalidated", "compatibilityvalidatedreason"
        ];
    }

    internal sealed record XRegistrySyncDefinition(
        XRegistrySyncEntityKind Kind,
        JsonElement Model,
        string Singular,
        string Path)
    {
        public bool HasDocument =>
            Kind == XRegistrySyncEntityKind.Version && Model.GetProperty("hasdocument").GetBoolean();
    }
}
