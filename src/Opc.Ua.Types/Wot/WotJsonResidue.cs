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
 *
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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Preserves only WoT members that have no OPC UA model representation as
    /// pointer-addressed JSON values in a standard NodeSet Extension.
    /// </summary>
    internal static class WotJsonResidue
    {
        private const string ResidueElement = "WoTJsonResidue";
        private const string MemberElement = "Member";
        private const string Version = "1.0";

        private sealed class Entry
        {
            public required string Pointer { get; init; }

            public required string Json { get; init; }

            public string? LinkRel { get; init; }

            public string? LinkHref { get; init; }

            public string? LinkRefType { get; init; }

            public string? LinkRefName { get; init; }
        }

        public static void Replace(
            UANodeSet nodeSet,
            WotDocument document,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            List<Entry> entries = Capture(document.RootElement);
            var extensions = new List<System.Xml.XmlElement>();
            if (nodeSet.Extensions is not null)
            {
                foreach (System.Xml.XmlElement extension in nodeSet.Extensions)
                {
                    if (!IsResidue(extension))
                    {
                        extensions.Add(extension);
                    }
                }
            }

            if (entries.Count > 0)
            {
                int total = 0;
                foreach (Entry entry in entries)
                {
                    total += Encoding.UTF8.GetByteCount(entry.Json);
                    if (total > options.MaxJsonDocumentSize)
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.JsonDocumentTooLarge,
                            $"Unmapped WoT residue exceeds the configured " +
                            $"{options.MaxJsonDocumentSize} byte limit."));
                        return;
                    }
                }
                extensions.Add(CreateExtension(entries));
            }

            nodeSet.Extensions = extensions.Count == 0 ? null : [.. extensions];
        }

        public static byte[] Apply(
            byte[] generatedJson,
            UANodeSet nodeSet,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            List<Entry> entries = ReadEntries(nodeSet, options, diagnostics);
            if (entries.Count == 0)
            {
                return generatedJson;
            }

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(
                    Encoding.UTF8.GetString(generatedJson),
                    nodeOptions: null,
                    documentOptions: new JsonDocumentOptions
                    {
                        MaxDepth = options.MaxJsonDepth,
                        CommentHandling = JsonCommentHandling.Disallow
                    });
            }
            catch (JsonException ex)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResidueInvalid,
                    $"The generated WoT document could not be parsed before " +
                    $"applying residue: {ex.Message}"));
                return generatedJson;
            }
            if (root is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResidueInvalid,
                    "The generated WoT document could not be parsed before applying residue."));
                return generatedJson;
            }

            foreach (Entry entry in entries)
            {
                JsonNode? value;
                try
                {
                    value = JsonNode.Parse(
                        entry.Json,
                        nodeOptions: null,
                        documentOptions: new JsonDocumentOptions
                        {
                            MaxDepth = options.MaxJsonDepth,
                            CommentHandling = JsonCommentHandling.Disallow
                        });
                }
                catch (JsonException ex)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ResidueInvalid,
                        $"Residue at '{entry.Pointer}' is not valid JSON: {ex.Message}",
                        WotLocation.FromPointer(entry.Pointer)));
                    continue;
                }
                if (value is null && !string.Equals(entry.Json, "null", StringComparison.Ordinal))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ResidueInvalid,
                        $"Residue at '{entry.Pointer}' could not be parsed.",
                        WotLocation.FromPointer(entry.Pointer)));
                    continue;
                }
                if (entry.LinkRel is not null)
                {
                    ApplyLinkEntry(root, entry, value, diagnostics);
                }
                else
                {
                    ApplyEntry(root, entry.Pointer, value, diagnostics);
                }
            }

            try
            {
                return Encoding.UTF8.GetBytes(root.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        MaxDepth = options.MaxJsonDepth
                    }));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResidueInvalid,
                    $"The WoT residue exceeds the configured JSON depth: {ex.Message}"));
                return generatedJson;
            }
        }

        private static List<Entry> Capture(JsonElement root)
        {
            var entries = new List<Entry>();
            if (root.ValueKind != JsonValueKind.Object)
            {
                return entries;
            }
            foreach (JsonProperty property in root.EnumerateObject())
            {
                string pointer = "/" + Escape(property.Name);
                switch (property.Name)
                {
                    case "@context":
                        CaptureContext(property.Value, pointer, entries);
                        break;
                    case "properties":
                    case "actions":
                    case "events":
                        CaptureAffordanceMap(property.Value, pointer, entries);
                        break;
                    case "links":
                        CaptureLinks(property.Value, pointer, entries);
                        break;
                    case "@type":
                    case "title":
                    case "description":
                    case "uav:browseName":
                    case "uav:id":
                    case "uav:isEvent":
                    case "uav:hasComponent":
                    case "uav:componentOf":
                    case "uav:nodeSet":
                    case "uav:nodes":
                        break;
                    default:
                        Add(entries, pointer, property.Value);
                        break;
                }
            }
            return entries;
        }

        private static void CaptureContext(
            JsonElement context,
            string pointer,
            List<Entry> entries)
        {
            if (context.ValueKind != JsonValueKind.Array)
            {
                Add(entries, pointer, context);
                return;
            }

            foreach (JsonElement item in context.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String &&
                    string.Equals(
                        item.GetString(),
                        WotVocabulary.WotContext,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("uav", out JsonElement uav) &&
                    uav.ValueKind == JsonValueKind.String &&
                    string.Equals(
                        uav.GetString(),
                        WotVocabulary.VocabularyNamespace,
                        StringComparison.Ordinal))
                {
                    foreach (JsonProperty property in item.EnumerateObject())
                    {
                        if (!string.Equals(property.Name, "uav", StringComparison.Ordinal))
                        {
                            Add(
                                entries,
                                pointer + "/1/" + Escape(property.Name),
                                property.Value);
                        }
                    }
                    continue;
                }
                Add(entries, pointer + "/-", item);
            }
        }

        private static void CaptureAffordanceMap(
            JsonElement map,
            string pointer,
            List<Entry> entries)
        {
            if (map.ValueKind != JsonValueKind.Object)
            {
                Add(entries, pointer, map);
                return;
            }
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty affordance in map.EnumerateObject())
            {
                string projectedName = affordance.Name;
                if (affordance.Value.ValueKind == JsonValueKind.Object &&
                    affordance.Value.TryGetProperty(
                        "uav:browseName",
                        out JsonElement browseName) &&
                    browseName.ValueKind == JsonValueKind.String &&
                    LocalName(browseName.GetString()) is { Length: > 0 } localName)
                {
                    projectedName = localName;
                }
                projectedName = UniqueKey(projectedName, used);
                string affordancePointer = pointer + "/" + Escape(projectedName);
                if (affordance.Value.ValueKind != JsonValueKind.Object)
                {
                    Add(entries, affordancePointer, affordance.Value);
                    continue;
                }
                foreach (JsonProperty property in affordance.Value.EnumerateObject())
                {
                    switch (property.Name)
                    {
                        case "@type":
                        case "title":
                        case "description":
                        case "uav:browseName":
                        case "uav:id":
                        case "uav:isEvent":
                        case "uav:modellingRule":
                        case "type":
                        case "readOnly":
                        case "writeOnly":
                        case "observable":
                            break;
                        default:
                            Add(
                                entries,
                                affordancePointer + "/" + Escape(property.Name),
                                property.Value);
                            break;
                    }
                }
            }
        }

        private static string UniqueKey(string candidate, HashSet<string> used)
        {
            if (used.Add(candidate))
            {
                return candidate;
            }
            int suffix = 2;
            string unique = candidate + "_" +
                suffix.ToString(CultureInfo.InvariantCulture);
            while (!used.Add(unique))
            {
                suffix++;
                unique = candidate + "_" +
                    suffix.ToString(CultureInfo.InvariantCulture);
            }
            return unique;
        }

        private static void CaptureLinks(
            JsonElement links,
            string pointer,
            List<Entry> entries)
        {
            if (links.ValueKind != JsonValueKind.Array)
            {
                Add(entries, pointer, links);
                return;
            }
            foreach (JsonElement link in links.EnumerateArray())
            {
                string? rel = link.ValueKind == JsonValueKind.Object &&
                    link.TryGetProperty("rel", out JsonElement relElement) &&
                    relElement.ValueKind == JsonValueKind.String
                    ? relElement.GetString()
                    : null;
                if (!IsMappedLink(rel))
                {
                    Add(entries, pointer + "/-", link);
                    continue;
                }
                string extras = GetLinkExtras(link, out bool hasExtras);
                if (hasExtras)
                {
                    entries.Add(new Entry
                    {
                        Pointer = pointer + "/-",
                        Json = extras,
                        LinkRel = rel,
                        LinkHref = GetString(link, "href"),
                        LinkRefType = GetString(link, "uav:refType"),
                        LinkRefName = GetString(link, "uav:refName")
                    });
                }
            }
        }

        private static string GetLinkExtras(JsonElement link, out bool hasExtras)
        {
            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                hasExtras = false;
                foreach (JsonProperty property in link.EnumerateObject())
                {
                    if (property.Name is "rel" or "href" or "uav:refType" or "uav:refName")
                    {
                        continue;
                    }
                    hasExtras = true;
                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static bool IsMappedLink(string? rel)
        {
            return rel is "tm:extends" or
                "uav:typedReference" or
                "uav:reference" or
                "uav:componentModel" or
                "uav:capability";
        }

        private static string? LocalName(string? browseName)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                return null;
            }
            int separator = -1;
            for (int ii = 0; ii < browseName.Length; ii++)
            {
                if (browseName[ii] == ':')
                {
                    separator = ii;
                    break;
                }
            }
            return separator >= 0 && separator + 1 < browseName.Length
                ? browseName.Substring(separator + 1)
                : browseName;
        }

        private static string? GetString(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static void Add(
            List<Entry> entries,
            string pointer,
            JsonElement value)
        {
            entries.Add(new Entry
            {
                Pointer = pointer,
                Json = value.GetRawText()
            });
        }

        private static System.Xml.XmlElement CreateExtension(List<Entry> entries)
        {
            var document = new XmlDocument { XmlResolver = null };
            System.Xml.XmlElement root = document.CreateElement(
                "uav",
                ResidueElement,
                WotVocabulary.VocabularyNamespace);
            root.SetAttribute("Version", Version);
            document.AppendChild(root);

            foreach (Entry entry in entries)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(entry.Json);
                System.Xml.XmlElement member = document.CreateElement(
                    "uav",
                    MemberElement,
                    WotVocabulary.VocabularyNamespace);
                member.SetAttribute("Pointer", entry.Pointer);
                member.SetAttribute("Encoding", WotVocabulary.Base64Encoding);
                member.SetAttribute("Sha256", ToLowerHex(ComputeSha256(bytes)));
                SetOptionalAttribute(member, "LinkRel", entry.LinkRel);
                SetOptionalAttribute(member, "LinkHref", entry.LinkHref);
                SetOptionalAttribute(member, "LinkRefType", entry.LinkRefType);
                SetOptionalAttribute(member, "LinkRefName", entry.LinkRefName);
                member.InnerText = Convert.ToBase64String(bytes);
                root.AppendChild(member);
            }
            return root;
        }

        private static void SetOptionalAttribute(
            System.Xml.XmlElement element,
            string name,
            string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                element.SetAttribute(name, value);
            }
        }

        private static List<Entry> ReadEntries(
            UANodeSet nodeSet,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            var entries = new List<Entry>();
            if (nodeSet.Extensions is null)
            {
                return entries;
            }

            int total = 0;
            foreach (System.Xml.XmlElement extension in nodeSet.Extensions)
            {
                if (!IsResidue(extension))
                {
                    continue;
                }
                if (!string.Equals(
                    extension.GetAttribute("Version"),
                    Version,
                    StringComparison.Ordinal))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ResidueInvalid,
                        $"Unsupported {ResidueElement} Version " +
                        $"'{extension.GetAttribute("Version")}'."));
                    continue;
                }
                foreach (XmlNode child in extension.ChildNodes)
                {
                    if (child is not System.Xml.XmlElement member ||
                        !string.Equals(
                            member.LocalName,
                            MemberElement,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            member.NamespaceURI,
                            WotVocabulary.VocabularyNamespace,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string pointer = member.GetAttribute("Pointer");
                    if (!IsJsonPointer(pointer, options.MaxJsonDepth))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.ResidueInvalid,
                            $"Residue pointer '{pointer}' is not an RFC 6901 JSON Pointer " +
                            $"within the configured depth of {options.MaxJsonDepth}."));
                        continue;
                    }
                    if (!string.Equals(
                        member.GetAttribute("Encoding"),
                        WotVocabulary.Base64Encoding,
                        StringComparison.Ordinal))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.ResidueInvalid,
                            $"Residue at '{pointer}' does not use base64 encoding.",
                            WotLocation.FromPointer(pointer)));
                        continue;
                    }

                    byte[] bytes;
                    try
                    {
                        bytes = Convert.FromBase64String(member.InnerText);
                    }
                    catch (FormatException)
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.ResidueInvalid,
                            $"Residue at '{pointer}' is not valid base64.",
                            WotLocation.FromPointer(pointer)));
                        continue;
                    }
                    total += bytes.Length;
                    if (total > options.MaxJsonDocumentSize)
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.JsonDocumentTooLarge,
                            $"WoT residue exceeds the configured " +
                            $"{options.MaxJsonDocumentSize} byte limit."));
                        return entries;
                    }
                    string digest = member.GetAttribute("Sha256");
                    if (!string.Equals(
                        digest,
                        ToLowerHex(ComputeSha256(bytes)),
                        StringComparison.Ordinal))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.ResidueInvalid,
                            $"Residue at '{pointer}' failed its SHA-256 integrity check.",
                            WotLocation.FromPointer(pointer)));
                        continue;
                    }
                    entries.Add(new Entry
                    {
                        Pointer = pointer,
                        Json = Encoding.UTF8.GetString(bytes),
                        LinkRel = OptionalAttribute(member, "LinkRel"),
                        LinkHref = OptionalAttribute(member, "LinkHref"),
                        LinkRefType = OptionalAttribute(member, "LinkRefType"),
                        LinkRefName = OptionalAttribute(member, "LinkRefName")
                    });
                }
            }
            return entries;
        }

        private static string? OptionalAttribute(
            System.Xml.XmlElement element,
            string name)
        {
            string value = element.GetAttribute(name);
            return value.Length == 0 ? null : value;
        }

        private static bool IsResidue(System.Xml.XmlElement element)
        {
            return string.Equals(
                    element.LocalName,
                    ResidueElement,
                    StringComparison.Ordinal) &&
                string.Equals(
                    element.NamespaceURI,
                    WotVocabulary.VocabularyNamespace,
                    StringComparison.Ordinal);
        }

        private static void ApplyEntry(
            JsonNode root,
            string pointer,
            JsonNode? value,
            List<WotDiagnostic> diagnostics)
        {
            string[] tokens = ParsePointer(pointer);
            if (tokens.Length == 0)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResidueInvalid,
                    "The document root cannot be a residue target.",
                    WotLocation.FromPointer(pointer)));
                return;
            }

            JsonNode current = root;
            for (int ii = 0; ii < tokens.Length - 1; ii++)
            {
                string token = tokens[ii];
                string next = tokens[ii + 1];
                if (current is JsonObject obj)
                {
                    JsonNode? child = obj[token];
                    if (child is null)
                    {
                        child = IsArrayToken(next) ? new JsonArray() : new JsonObject();
                        obj[token] = child;
                    }
                    current = child;
                }
                else if (current is JsonArray array &&
                    int.TryParse(
                        token,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int index) &&
                    index >= 0 &&
                    index < array.Count &&
                    array[index] is JsonNode child)
                {
                    current = child;
                }
                else
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ResidueInvalid,
                        $"Residue parent '{pointer}' does not resolve.",
                        WotLocation.FromPointer(pointer)));
                    return;
                }
            }

            string leaf = tokens[^1];
            if (current is JsonObject targetObject)
            {
                JsonNode? existing = targetObject[leaf];
                if (existing is not null)
                {
                    if (!JsonEquals(existing, value))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.ResidueConflict,
                            $"Residue at '{pointer}' conflicts with a value " +
                            "reconstructed from OPC UA model facts.",
                            WotLocation.FromPointer(pointer)));
                    }
                    return;
                }
                targetObject[leaf] = value;
                return;
            }
            if (current is JsonArray targetArray)
            {
                if (string.Equals(leaf, "-", StringComparison.Ordinal))
                {
                    targetArray.Add(value);
                    return;
                }
                if (int.TryParse(
                    leaf,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int index) &&
                    index >= 0 &&
                    index <= targetArray.Count)
                {
                    if (index == targetArray.Count)
                    {
                        targetArray.Add(value);
                    }
                    else if (targetArray[index] is null)
                    {
                        targetArray[index] = value;
                    }
                    else if (!JsonEquals(targetArray[index], value))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.ResidueConflict,
                            $"Residue at '{pointer}' conflicts with an existing array item.",
                            WotLocation.FromPointer(pointer)));
                    }
                    return;
                }
            }
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.ResidueInvalid,
                $"Residue target '{pointer}' is invalid.",
                WotLocation.FromPointer(pointer)));
        }

        private static void ApplyLinkEntry(
            JsonNode root,
            Entry entry,
            JsonNode? value,
            List<WotDiagnostic> diagnostics)
        {
            if (root is not JsonObject rootObject || value is not JsonObject extras)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResidueInvalid,
                    "A link residue selector requires an object value.",
                    WotLocation.FromPointer(entry.Pointer)));
                return;
            }

            JsonArray links;
            if (rootObject["links"] is JsonArray existingLinks)
            {
                links = existingLinks;
            }
            else if (rootObject["links"] is null)
            {
                links = new JsonArray();
                rootObject["links"] = links;
            }
            else
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResidueConflict,
                    "Link residue conflicts with a non-array links member.",
                    WotLocation.FromPointer("/links")));
                return;
            }

            JsonObject? target = FindLink(links, entry, requireExactRel: true);
            bool exact = target is not null;
            target ??= FindLink(links, entry, requireExactRel: false);
            if (target is null)
            {
                target = new JsonObject();
                SetString(target, "rel", entry.LinkRel);
                SetString(target, "href", entry.LinkHref);
                SetString(target, "uav:refType", entry.LinkRefType);
                SetString(target, "uav:refName", entry.LinkRefName);
                links.Add(target);
            }
            else if (exact)
            {
                MergeString(target, "rel", entry.LinkRel, entry.Pointer, diagnostics);
                MergeString(target, "href", entry.LinkHref, entry.Pointer, diagnostics);
                MergeString(
                    target,
                    "uav:refType",
                    entry.LinkRefType,
                    entry.Pointer,
                    diagnostics);
                MergeString(
                    target,
                    "uav:refName",
                    entry.LinkRefName,
                    entry.Pointer,
                    diagnostics);
            }

            foreach (KeyValuePair<string, JsonNode?> property in extras)
            {
                JsonNode? existing = target[property.Key];
                if (existing is not null)
                {
                    if (!JsonEquals(existing, property.Value))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.ResidueConflict,
                            $"Link residue member '{property.Key}' conflicts with " +
                            "a regenerated value.",
                            WotLocation.FromPointer(entry.Pointer)));
                    }
                    continue;
                }
                target[property.Key] = CloneNode(property.Value);
            }
        }

        private static JsonObject? FindLink(
            JsonArray links,
            Entry entry,
            bool requireExactRel)
        {
            foreach (JsonNode? item in links)
            {
                if (item is not JsonObject link ||
                    !StringNodeEquals(link["href"], entry.LinkHref))
                {
                    continue;
                }
                if (requireExactRel)
                {
                    if (StringNodeEquals(link["rel"], entry.LinkRel))
                    {
                        return link;
                    }
                    continue;
                }
                if (entry.LinkRefType is not null &&
                    StringNodeEquals(link["uav:refType"], entry.LinkRefType))
                {
                    return link;
                }
            }
            return null;
        }

        private static bool StringNodeEquals(JsonNode? node, string? value)
        {
            return node is JsonValue jsonValue &&
                jsonValue.TryGetValue(out string? text) &&
                string.Equals(text, value, StringComparison.Ordinal);
        }

        private static void SetString(
            JsonObject target,
            string name,
            string? value)
        {
            if (value is not null)
            {
                target[name] = value;
            }
        }

        private static void MergeString(
            JsonObject target,
            string name,
            string? value,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            if (value is null)
            {
                return;
            }
            JsonNode? existing = target[name];
            if (existing is null)
            {
                target[name] = value;
            }
            else if (!StringNodeEquals(existing, value))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResidueConflict,
                    $"Link residue selector '{name}' conflicts with a regenerated value.",
                    WotLocation.FromPointer(pointer)));
            }
        }

        private static JsonNode? CloneNode(JsonNode? value)
        {
            return value?.DeepClone();
        }

        private static bool JsonEquals(JsonNode? left, JsonNode? right)
        {
            return string.Equals(
                left?.ToJsonString() ?? "null",
                right?.ToJsonString() ?? "null",
                StringComparison.Ordinal);
        }

        private static bool IsArrayToken(string token)
        {
            return string.Equals(token, "-", StringComparison.Ordinal) ||
                int.TryParse(
                    token,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _);
        }

        private static bool IsJsonPointer(string pointer, int maxDepth)
        {
            if (string.IsNullOrEmpty(pointer) || pointer[0] != '/')
            {
                return false;
            }
            string[] tokens = pointer.Substring(1).Split('/');
            if (tokens.Length >= maxDepth)
            {
                return false;
            }
            foreach (string token in tokens)
            {
                for (int ii = 0; ii < token.Length; ii++)
                {
                    if (token[ii] == '~' &&
                        (ii + 1 >= token.Length ||
                         token[ii + 1] is not ('0' or '1')))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static string[] ParsePointer(string pointer)
        {
            string[] tokens = pointer.Substring(1).Split('/');
            for (int ii = 0; ii < tokens.Length; ii++)
            {
                tokens[ii] = ReplaceOrdinal(
                    ReplaceOrdinal(tokens[ii], "~1", "/"),
                    "~0",
                    "~");
            }
            return tokens;
        }

        private static string Escape(string token)
        {
            return ReplaceOrdinal(
                ReplaceOrdinal(token, "~", "~0"),
                "/",
                "~1");
        }

        private static string ReplaceOrdinal(
            string source,
            string oldValue,
            string newValue)
        {
            int index = source.IndexOf(oldValue, StringComparison.Ordinal);
            if (index < 0)
            {
                return source;
            }
            var builder = new StringBuilder(source.Length);
            int start = 0;
            while (index >= 0)
            {
                builder.Append(source, start, index - start);
                builder.Append(newValue);
                start = index + oldValue.Length;
                index = source.IndexOf(oldValue, start, StringComparison.Ordinal);
            }
            builder.Append(source, start, source.Length - start);
            return builder.ToString();
        }

        private static byte[] ComputeSha256(byte[] data)
        {
#if NET6_0_OR_GREATER
            return SHA256.HashData(data);
#else
            using SHA256 sha256 = SHA256.Create();
            return sha256.ComputeHash(data);
#endif
        }

        private static string ToLowerHex(byte[] data)
        {
            var builder = new StringBuilder(data.Length * 2);
            foreach (byte value in data)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }
    }
}
