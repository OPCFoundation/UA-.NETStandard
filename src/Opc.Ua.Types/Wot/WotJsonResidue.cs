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
using System.IO;
using System.Linq;
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
        private const string DocumentLocalePointer = "/@context/1/@language";

        private sealed class Entry
        {
            public required string Pointer { get; init; }

            public required string Json { get; init; }

            public string? LinkRel { get; init; }

            public string? LinkHref { get; init; }

            public string? LinkRefId { get; init; }

            public string? LinkRefName { get; init; }
        }

        private readonly record struct RawValue(string Json, JsonNode Snapshot);

        /// <summary>
        /// Reads retained selection metadata before readable localized values are generated.
        /// </summary>
        internal static bool TryGetDocumentLocale(
            UANodeSet nodeSet,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            out string? locale)
        {
            locale = null;
            if (!(nodeSet.Extensions ?? []).Any(extension => IsResidue(extension) &&
                extension.ChildNodes.OfType<System.Xml.XmlElement>().Any(member =>
                    member.GetAttribute("Pointer") == DocumentLocalePointer)))
            {
                return false;
            }
            foreach (Entry entry in ReadEntries(nodeSet, options, diagnostics))
            {
                if (entry.Pointer != DocumentLocalePointer)
                {
                    continue;
                }
                try
                {
                    using var value = JsonDocument.Parse(
                        entry.Json, new JsonDocumentOptions { MaxDepth = options.MaxJsonDepth });
                    if (value.RootElement.ValueKind is JsonValueKind.String or JsonValueKind.Null)
                    {
                        locale = value.RootElement.GetString();
                        return true;
                    }
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error, WotDiagnosticCode.ResidueInvalid,
                        "The preserved document selection locale must be a string or null.",
                        WotLocation.FromPointer(DocumentLocalePointer)));
                }
                catch (JsonException exception)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error, WotDiagnosticCode.ResidueInvalid,
                        "The preserved document selection locale is invalid JSON: " + exception.Message,
                        WotLocation.FromPointer(DocumentLocalePointer)));
                }
            }
            return false;
        }

        public static void Replace(
            UANodeSet nodeSet,
            WotDocument document,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            Func<JsonElement, string?>? resolvedDefinitionBinding = null)
        {
            List<Entry> entries = Capture(document, nodeSet, diagnostics, resolvedDefinitionBinding);
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
                            "Unmapped WoT residue exceeds the configured " +
                            $"{options.MaxJsonDocumentSize} byte limit."));
                        return;
                    }
                }
                extensions.Add(CreateExtension(entries));
            }

            nodeSet.Extensions = extensions.Count == 0 ? null : [.. extensions];
        }

        internal static void RemoveDocumentSetLinks(
            UANodeSet nodeSet,
            WotDocument document,
            WotDocumentSet documents,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            List<Entry> entries = ReadEntries(nodeSet, options, diagnostics);
            var retained = new List<Entry>();
            foreach (Entry entry in entries)
            {
                using var value = JsonDocument.Parse(
                    entry.Json, new JsonDocumentOptions { MaxDepth = options.MaxJsonDepth });
                if (entry.Pointer == "/data" &&
                    WotNodeSetConverter.IsGeneratedEventDefinitionData(
                        document, nodeSet, value.RootElement, options.MaxJsonDepth))
                {
                    continue;
                }
                if (entry.Pointer.StartsWith("/events/", StringComparison.Ordinal) &&
                    entry.Pointer.EndsWith("/tm:ref", StringComparison.Ordinal) &&
                    value.RootElement.ValueKind == JsonValueKind.String &&
                    documents.TryGetDocument(value.RootElement.GetString()!, out _))
                {
                    continue;
                }
                string? rel = entry.LinkRel ?? GetString(value.RootElement, "rel");
                string? href = entry.LinkHref ?? GetString(value.RootElement, "href");
                if (rel is WotNodeSetConverter.ComponentOfRel or WotNodeSetConverter.ComponentOfAliasRel &&
                    href is not null &&
                    documents.TryGetDocument(href, out _) &&
                    IsDocumentSetParentLink(value.RootElement))
                {
                    continue;
                }
                retained.Add(entry);
            }
            if (retained.Count == entries.Count)
            {
                return;
            }
            var extensions = new List<System.Xml.XmlElement>();
            foreach (System.Xml.XmlElement extension in nodeSet.Extensions ?? [])
            {
                if (!IsResidue(extension))
                {
                    extensions.Add(extension);
                }
            }
            if (retained.Count > 0)
            {
                extensions.Add(CreateExtension(retained));
            }
            nodeSet.Extensions = extensions.Count == 0 ? null : [.. extensions];
        }

        private static bool IsDocumentSetParentLink(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            foreach (JsonProperty member in value.EnumerateObject())
            {
                if (member.Name is "rel" or "href")
                {
                    continue;
                }
                if (member.Name != "type" ||
                    member.Value.ValueKind != JsonValueKind.String ||
                    member.Value.GetString() != "application/td+json")
                {
                    return false;
                }
            }
            return true;
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
                    "The generated WoT document could not be parsed before " +
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

            var rawValues = new Dictionary<JsonNode, RawValue>();
            using var generatedDocument = WotDocument.FromOwnedBytes(generatedJson, options);
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
                JsonNode? applied = entry.LinkRel is not null
                    ? ApplyLinkEntry(root, entry, value, diagnostics)
                    : ApplyEntry(root, entry.Pointer, value, diagnostics, nodeSet, generatedDocument, options);
                using var original = JsonDocument.Parse(
                    entry.Json, new JsonDocumentOptions { MaxDepth = options.MaxJsonDepth });
                string[] entryTokens = ParsePointer(entry.Pointer);
                bool opaque = entryTokens.Contains("@context") ||
                    WotBindingConformance.OpaqueMembers.Contains(entryTokens[^1]) ||
                    (WotNodeSetConverter.IsLiteralSchemaMember(entryTokens[^1]) &&
                        (entryTokens.Length == 1 ||
                            !WotNodeSetConverter.IsSchemaDeclarationMap(entryTokens[^2]))) ||
                    (entry.Pointer == "/uav:nodes" &&
                        WotNativeProjection.HasUnsupportedProfile(original.RootElement));
                CaptureRawValues(applied, value, original.RootElement, opaque, rawValues);
            }

            try
            {
                using var output = new MemoryStream();
                using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions
                {
                    Indented = true
                }))
                {
                    WriteWithRawValues(writer, root, string.Empty, rawValues, diagnostics);
                }
                byte[] json = output.ToArray();
                using var validated = JsonDocument.Parse(
                    json, new JsonDocumentOptions { MaxDepth = options.MaxJsonDepth });
                return json;
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

        private static void CaptureRawValues(
            JsonNode? target,
            JsonNode? source,
            JsonElement original,
            bool opaque,
            Dictionary<JsonNode, RawValue> rawValues,
            bool indexMap = false)
        {
            if (target is null || source is null)
            {
                return;
            }
            if (opaque)
            {
                if (JsonEquals(target, source))
                {
                    rawValues[target] = new RawValue(original.GetRawText(), target.DeepClone());
                }
                return;
            }
            if (original.ValueKind == JsonValueKind.Object &&
                target is JsonObject targetObject &&
                source is JsonObject sourceObject)
            {
                foreach (JsonProperty property in original.EnumerateObject())
                {
                    CaptureRawValues(
                        targetObject[property.Name], sourceObject[property.Name], property.Value,
                        !indexMap &&
                        (property.Name == "@context" ||
                            WotBindingConformance.OpaqueMembers.Contains(property.Name) ||
                            WotNodeSetConverter.IsLiteralSchemaMember(property.Name)),
                        rawValues,
                        !indexMap && WotNodeSetConverter.IsSchemaDeclarationMap(property.Name));
                }
            }
            else if (original.ValueKind == JsonValueKind.Array &&
                target is JsonArray targetArray &&
                source is JsonArray sourceArray)
            {
                int index = 0;
                foreach (JsonElement item in original.EnumerateArray())
                {
                    if (index >= targetArray.Count || index >= sourceArray.Count)
                    {
                        break;
                    }
                    CaptureRawValues(targetArray[index], sourceArray[index], item, false, rawValues);
                    index++;
                }
            }
        }

        private static void WriteWithRawValues(
            Utf8JsonWriter writer,
            JsonNode? node,
            string pointer,
            Dictionary<JsonNode, RawValue> rawValues,
            List<WotDiagnostic> diagnostics)
        {
            if (node is null)
            {
                writer.WriteNullValue();
                return;
            }
            if (rawValues.TryGetValue(node, out RawValue raw))
            {
                if (JsonEquals(node, raw.Snapshot))
                {
                    // The complete output is parsed below to enforce combined nesting depth.
                    writer.WriteRawValue(raw.Json, skipInputValidation: true);
                    return;
                }
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResidueConflict,
                    "An opaque residue value was changed by another residue entry.",
                    WotLocation.FromPointer(pointer)));
            }
            if (node is JsonObject objectValue)
            {
                writer.WriteStartObject();
                foreach (KeyValuePair<string, JsonNode?> property in objectValue)
                {
                    writer.WritePropertyName(property.Key);
                    WriteWithRawValues(writer, property.Value, pointer + "/" + Escape(property.Key),
                        rawValues, diagnostics);
                }
                writer.WriteEndObject();
            }
            else if (node is JsonArray array)
            {
                writer.WriteStartArray();
                for (int index = 0; index < array.Count; index++)
                {
                    WriteWithRawValues(writer, array[index],
                        pointer + "/" + index.ToString(CultureInfo.InvariantCulture), rawValues, diagnostics);
                }
                writer.WriteEndArray();
            }
            else
            {
                node.WriteTo(writer);
            }
        }

        private static List<Entry> Capture(
            WotDocument document,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            Func<JsonElement, string?>? resolvedDefinitionBinding)
        {
            JsonElement root = document.RootElement;
            var entries = new List<Entry>();
            if (root.ValueKind != JsonValueKind.Object)
            {
                return entries;
            }
            var schemas = new HashSet<JsonElement>();
            foreach (JsonElement schema in WotNodeSetConverter.ReadDataSchemaOccurrences(document))
            {
                schemas.Add(schema);
            }
            var units = new HashSet<JsonElement>();
            foreach (IReadOnlyDictionary<string, JsonElement> map in new[]
                {
                    document.Properties, document.Actions, document.Events
                })
            {
                foreach (JsonElement affordance in map.Values)
                {
                    schemas.Add(affordance);
                    if (affordance.ValueKind == JsonValueKind.Object &&
                        affordance.TryGetProperty(WotNodeSetConverter.EngineeringUnitsTerm, out JsonElement unit))
                    {
                        units.Add(unit);
                    }
                }
            }
            foreach (JsonProperty property in root.EnumerateObject())
            {
                string pointer = "/" + Escape(property.Name);
                switch (property.Name)
                {
                    case "@context" when IsRegeneratedLocalizedTextContext(root, property.Value):
                        break;
                    case "@context":
                        CaptureContext(root, property.Value, pointer, entries);
                        break;
                    case "properties":
                    case "actions":
                    case "events":
                        CaptureAffordanceMap(
                            document, root, property.Value, pointer, property.Name, entries,
                            resolvedDefinitionBinding ?? ResolveDefinitionBinding, ResolveLocalizedContext);
                        break;
                    case "links":
                        CaptureLinks(property.Value, pointer, entries);
                        break;
                    case "@type":
                        CaptureTypeAnnotations(root, pointer, entries);
                        break;
                    case "title":
                    case "description":
                    case "uav:browseName":
                    case "uav:id":
                    case "uav:hasComponent":
                    case "uav:componentOf":
                    case "uav:nodeSet":
                    case "uav:dataTypeDefinitions":
                        break;
                    case "uav:isAbstract":
                        if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                            !WotDocument.ReadStringTokens(root, "@type").Exists(token =>
                                token is "tm:ThingModel" or "uav:objectType" or "uav:variableType"))
                        {
                            Add(entries, pointer, property.Value);
                        }
                        break;
                    case "uav:nodes":
                        if (WotNativeProjection.HasUnsupportedProfile(property.Value))
                        {
                            entries.Add(new Entry { Pointer = pointer, Json = property.Value.GetRawText() });
                        }
                        break;
                    case WotBindingConformance.BindingVersionTerm:
                        // Section 4.1 makes a generator state the revision it
                        // emitted, so this library stamps its own revision on
                        // every generated document. A claim that agrees with
                        // that stamp is re-derived rather than carried; one
                        // that names another revision - an author's
                        // forward-compatible claim, which a consumer preserves
                        // rather than rejects - is kept verbatim so the round
                        // trip restates what the author wrote.
                        if (property.Value.ValueKind != JsonValueKind.String ||
                            !string.Equals(
                                property.Value.GetString(),
                                WotBindingConformance.CurrentRevision,
                                StringComparison.Ordinal))
                        {
                            Add(entries, pointer, property.Value);
                        }
                        break;
                    case WotNodeSetConverter.InverseNameTerm:
                    case WotNodeSetConverter.SymmetricTerm:
                        // OPC 10000-3 gives a ReferenceType an InverseName and
                        // a Symmetric flag, and both map onto the projected
                        // Node's own Attributes, so both come back from it.
                        break;
                    case WotNodeSetConverter.TitlesMember:
                    case WotNodeSetConverter.DescriptionsMember:
                        // Section 9.1.1 maps every locale of the root's
                        // DisplayName and Description onto one LocalizedText of
                        // the projected Node.
                        if (!WotNodeSetConverter.MapsLocalizedText(root, property.Name))
                        {
                            Add(entries, pointer, property.Value);
                        }
                        break;
                    default:
                        Add(entries, pointer, property.Value);
                        break;
                }
            }
            CaptureDataTypeLiteralMembers(
                document, nodeSet, entries, diagnostics, resolvedDefinitionBinding ?? ResolveDefinitionBinding);
            if (WotNodeSetConverter.TryGetUnrepresentedDocumentLocale(document, nodeSet, out string? locale))
            {
                entries.Add(new Entry
                {
                    Pointer = DocumentLocalePointer,
                    Json = locale is null ? "null" : "\"" + JsonEncodedText.Encode(locale) + "\""
                });
            }
            return entries;

            string? ResolveLocalizedContext(JsonElement owner)
            {
                if (units.Contains(owner))
                {
                    return PreserveLocalizedContext(document, owner, unit: true);
                }
                return schemas.Contains(owner) ? PreserveLocalizedContext(document, owner, unit: false) : null;
            }

            string? ResolveDefinitionBinding(JsonElement definition)
            {
                if (GetString(definition, "uav:dataTypeName") is null &&
                    GetString(definition, "@id") is { } identity)
                {
                    foreach (JsonElement candidate in WotNodeSetConverter.ReadDataTypeDefinitionOccurrences(root))
                    {
                        if (GetString(candidate, "@id") == identity &&
                            GetString(candidate, "uav:dataTypeName") is not null)
                        {
                            definition = candidate;
                            break;
                        }
                    }
                }
                var identities = new UANodeSet
                {
                    NamespaceUris = nodeSet.NamespaceUris is null ? null : (string[])nodeSet.NamespaceUris.Clone()
                };
                string? dataType = WotNodeSetConverter.ResolveDataTypeIdentity(
                    document, definition, identities, diagnostics);
                return WotNodeSetConverter.ToPortableNodeId(dataType, identities.NamespaceUris);
            }
        }

        private static void CaptureDataTypeLiteralMembers(
            WotDocument document,
            UANodeSet nodeSet,
            List<Entry> entries,
            List<WotDiagnostic> diagnostics,
            Func<JsonElement, string?> resolveDefinitionBinding)
        {
            ArrayOf<UADataType> generatedTypes = WotNodeSetConverter.CollectDataTypeNodes(nodeSet);
            foreach ((JsonElement definition, string pointer) in
                WotNodeSetConverter.ReadDataTypeDefinitionLocations(document.RootElement))
            {
                JsonProperty[] literals = [.. definition.EnumerateObject().Where(property =>
                    WotBindingConformance.OpaqueMembers.Contains(property.Name) ||
                    WotNodeSetConverter.IsLiteralSchemaMember(property.Name))];
                if (literals.Length == 0)
                {
                    continue;
                }
                string? identity = resolveDefinitionBinding(definition);
                int index = -1;
                if (identity is not null)
                {
                    for (int candidate = 0; candidate < generatedTypes.Count; candidate++)
                    {
                        if (string.Equals(
                            WotNodeSetConverter.ToPortableNodeId(
                                generatedTypes[candidate].NodeId, nodeSet.NamespaceUris),
                            identity, StringComparison.Ordinal))
                        {
                            index = candidate;
                            break;
                        }
                    }
                }
                if (index < 0)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error, WotDiagnosticCode.ResidueInvalid,
                        "A DataType literal has no corresponding generated definition.",
                        WotLocation.FromPointer(pointer)));
                    continue;
                }
                string generatedPointer = "/uav:dataTypeDefinitions/" + index.ToString(CultureInfo.InvariantCulture);
                if (definition.TryGetProperty("@context", out JsonElement context))
                {
                    Add(entries, generatedPointer + "/@context", context);
                }
                foreach (JsonProperty literal in literals)
                {
                    entries.Add(new Entry
                    {
                        Pointer = generatedPointer + "/" + Escape(literal.Name),
                        Json = literal.Value.GetRawText()
                    });
                }
            }
        }

        private static void CaptureContext(
            JsonElement owner,
            JsonElement context,
            string pointer,
            List<Entry> entries)
        {
            if (IsGeneratedContextReference(context) || CaptureBindingContext(context, pointer, entries))
            {
                return;
            }
            if (context.ValueKind == JsonValueKind.Object)
            {
                // Generated contexts are arrays; an authored object remains an additional lexical scope.
                Add(entries, pointer + "/-", context);
                return;
            }
            if (context.ValueKind != JsonValueKind.Array)
            {
                Add(entries, pointer, context);
                return;
            }

            foreach (JsonElement item in context.EnumerateArray())
            {
                if (IsGeneratedContextReference(item))
                {
                    // Both context identities are re-derived by the forward
                    // direction, which names them on every document it writes.
                    continue;
                }
                if (CaptureBindingContext(item, pointer, entries))
                {
                    continue;
                }
                if (item.ValueKind == JsonValueKind.Object &&
                    IsRegeneratedLocalizedTextContext(owner, item))
                {
                    // The override is derived from the projected Nodes' own
                    // LocalizedText - it is written exactly where some text
                    // states no entry for the document's default locale - so
                    // carrying it as residue as well would state it twice.
                    continue;
                }
                Add(entries, pointer + "/-", item);
            }
        }

        private static bool IsGeneratedContextReference(JsonElement context)
        {
            return context.ValueKind == JsonValueKind.String &&
                context.GetString() is WotVocabulary.WotContext or WotVocabulary.BindingContext;
        }

        private static bool CaptureBindingContext(JsonElement context, string pointer, List<Entry> entries)
        {
            if (context.ValueKind != JsonValueKind.Object ||
                !context.TryGetProperty("uav", out JsonElement uav) ||
                uav.ValueKind != JsonValueKind.String ||
                !string.Equals(uav.GetString(), WotVocabulary.VocabularyNamespace, StringComparison.Ordinal))
            {
                return false;
            }
            foreach (JsonProperty property in context.EnumerateObject())
            {
                if (!IsGeneratedContextBinding(property))
                {
                    Add(entries, pointer + "/1/" + Escape(property.Name), property.Value);
                }
            }
            return true;
        }

        private static bool IsGeneratedContextBinding(JsonProperty property)
        {
            if (property.Name is "uav" or "ua")
            {
                return true;
            }

            // The effective selection locale is captured separately when native root text cannot reproduce it.
            if (property.Name is "@language")
            {
                return property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null;
            }
            if (!property.Name.StartsWith("ns", StringComparison.Ordinal) ||
                property.Name.Length == 2)
            {
                return false;
            }
            for (int ii = 2; ii < property.Name.Length; ii++)
            {
                if (!char.IsDigit(property.Name[ii]))
                {
                    return false;
                }
            }
            return property.Value.ValueKind == JsonValueKind.String;
        }

        private static void CaptureAffordanceMap(
            WotDocument document,
            JsonElement root,
            JsonElement map,
            string pointer,
            string kind,
            List<Entry> entries,
            Func<JsonElement, string?> resolveDefinitionBinding,
            Func<JsonElement, string?> resolveLocalizedContext)
        {
            if (map.ValueKind != JsonValueKind.Object)
            {
                Add(entries, pointer, map);
                return;
            }
            bool isAction = string.Equals(kind, "actions", StringComparison.Ordinal);
            bool isEvent = string.Equals(kind, "events", StringComparison.Ordinal);
            bool isProperty = !isAction && !isEvent;
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty affordance in map.EnumerateObject())
            {
                string projectedName = WotPortableIdentity.AllocateName(
                    WotPortableIdentity.AffordanceName(affordance.Value, affordance.Name), used);
                string affordancePointer = pointer + "/" + Escape(projectedName);
                if (affordance.Value.ValueKind != JsonValueKind.Object)
                {
                    Add(entries, affordancePointer, affordance.Value);
                    continue;
                }
                if (ContainsUnmappedContext(affordance.Value))
                {
                    var mappedMembers = new List<string>();
                    if (isProperty && WotNodeSetConverter.MapsUnitProperty(root, affordance.Value))
                    {
                        mappedMembers.Add(WotNodeSetConverter.UnitPropertyTerm);
                    }
                    if (isProperty && WotNodeSetConverter.MapsUnit(root, affordance.Value))
                    {
                        mappedMembers.Add(WotNodeSetConverter.UnitMember);
                    }
                    Add(entries, affordancePointer, affordance.Value,
                        resolveDefinitionBinding, mappedMembers.ToArrayOf(), resolveLocalizedContext,
                        kind == "properties" ? "uav:variable" : kind == "actions" ? "uav:method" : "uav:eventType");
                    continue;
                }
                foreach (JsonProperty property in affordance.Value.EnumerateObject())
                {
                    switch (property.Name)
                    {
                        case "@type":
                            CaptureTypeAnnotations(affordance.Value, affordancePointer + "/@type", entries);
                            break;
                        case "@context" when IsRegeneratedLocalizedTextContext(affordance.Value, property.Value):
                            break;
                        case "title":
                        case "description":
                        case "uav:browseName":
                        case "uav:id":
                        case "uav:modellingRule":
                        case "uav:mapToType":
                        case "uav:dataTypeDefinition":
                            break;
                        case "const":
                        case "default":
                            if (!isProperty || !WotNodeSetConverter.MapsVariableValue(affordance.Value))
                            {
                                Add(entries, affordancePointer + "/" + Escape(property.Name), property.Value);
                            }
                            break;
                        case "items":
                        case "oneOf":
                        case "format":
                        case "contentEncoding":
                            if (!isProperty || !WotNodeSetConverter.MapsRankedJsonType(affordance.Value, property.Name))
                            {
                                string location = affordancePointer + "/" + Escape(property.Name);
                                if (!isProperty ||
                                    property.Name != "items" ||
                                    !CaptureRankedItems(affordance.Value, property.Value, location, entries))
                                {
                                    Add(entries, location, property.Value);
                                }
                            }
                            break;
                        case "type":
                        case "readOnly":
                        case "writeOnly":
                        case "observable":
                            // §9.1 reads a DataSchema's json type and its three
                            // access flags off the Variable a property affordance
                            // projects. A Method and an EventType have no Value
                            // Attribute for them to describe, so on an action or
                            // an event they name no OPC UA fact and are kept
                            // verbatim rather than dropped.
                            if (!isProperty)
                            {
                                Add(
                                    entries,
                                    affordancePointer + "/" + Escape(property.Name),
                                    property.Value);
                            }
                            break;
                        case WotNodeSetConverter.ValueRankTerm:
                        case WotNodeSetConverter.ArrayDimensionsTerm:
                            // §9.1 maps a Variable's ValueRank and
                            // ArrayDimensions onto the Attributes of the same
                            // name, so both come back from the Node itself.
                            // Only a Variable has those Attributes: on an
                            // action or an event the terms are outside the
                            // mapped domain and are kept verbatim.
                            if (!isProperty)
                            {
                                Add(
                                    entries,
                                    affordancePointer + "/" + Escape(property.Name),
                                    property.Value);
                            }
                            break;
                        case "uav:componentOf":
                            // §9.1 maps the term onto the inverse component
                            // Reference that says which Variable holds this
                            // one, and the forward direction restates it from
                            // there. An action's Method and an event's
                            // EventType are placed by their own rules
                            // (Sections 13.2 and 13.4) and never from this
                            // term, so on those kinds it is kept verbatim.
                            if (!isProperty ||
                                !WotNodeSetConverter.MapsComponentOf(affordance.Value))
                            {
                                Add(
                                    entries,
                                    affordancePointer + "/" + Escape(property.Name),
                                    property.Value);
                            }
                            break;
                        case WotNodeSetConverter.UnitMember:
                            // Section 6.4 takes the engineering unit from the
                            // EUInformation of the Property the unit pointer
                            // names, so a unit that agrees with it is derived
                            // rather than carried. One that names no such
                            // Property, or disagrees with it, is kept - and so
                            // is one on an affordance kind that projects no
                            // Variable to carry it.
                            if (!isProperty ||
                                !WotNodeSetConverter.MapsUnit(root, affordance.Value))
                            {
                                Add(
                                    entries,
                                    affordancePointer + "/" + Escape(property.Name),
                                    property.Value);
                            }
                            break;
                        case WotNodeSetConverter.UnitPropertyTerm:
                            if (!isProperty ||
                                !WotNodeSetConverter.MapsUnitProperty(root, affordance.Value))
                            {
                                Add(
                                    entries,
                                    affordancePointer + "/" + Escape(property.Name),
                                    property.Value);
                            }
                            break;
                        case WotNodeSetConverter.TitlesMember:
                        case WotNodeSetConverter.DescriptionsMember:
                            // Section 9.1.1 maps every locale onto one
                            // LocalizedText of the Node's DisplayName or
                            // Description. A plural member that is not a map of
                            // language tags to strings is not mapped and is
                            // kept, so an invalid document keeps what it said.
                            if (!WotNodeSetConverter.MapsLocalizedText(
                                affordance.Value, property.Name))
                            {
                                Add(
                                    entries,
                                    affordancePointer + "/" + Escape(property.Name),
                                    property.Value);
                            }
                            break;
                        case WotNodeSetConverter.MinimumMember:
                        case WotNodeSetConverter.MaximumMember:
                            // Section 6.4.1 maps the pair onto the Variable's
                            // own EURange Property, so carrying them here as
                            // well would state one interval twice. A lone or
                            // reversed bound is not mapped and is kept, and
                            // neither is a bound on an affordance kind that
                            // projects no Variable.
                            if (!isProperty ||
                                !WotNodeSetConverter.MapsEuRange(affordance.Value))
                            {
                                Add(
                                    entries,
                                    affordancePointer + "/" + Escape(property.Name),
                                    property.Value);
                            }
                            break;
                        case WotNodeSetConverter.InstrumentRangeTerm:
                            if (!isProperty ||
                                !WotNodeSetConverter.MapsInstrumentRange(affordance.Value))
                            {
                                Add(
                                    entries,
                                    affordancePointer + "/" + Escape(property.Name),
                                    property.Value);
                            }
                            break;
                        case WotNodeSetConverter.EngineeringUnitsTerm:
                            // Section 6.4.1 maps the object onto the
                            // EUInformation the EngineeringUnits Property
                            // holds, and the forward direction reads it back
                            // from that value.
                            if (!isProperty ||
                                !WotNodeSetConverter.MapsEngineeringUnits(affordance.Value))
                            {
                                Add(
                                    entries,
                                    affordancePointer + "/" + Escape(property.Name),
                                    property.Value);
                            }
                            else
                            {
                                CaptureUnitTranslations(
                                    document, property.Value, affordancePointer + "/" + Escape(property.Name), entries);
                            }
                            break;
                        case WotNodeSetConverter.InputMember:
                        case WotNodeSetConverter.OutputMember:
                            // §9.1 maps an action's argument schemas onto the
                            // Method's InputArguments and OutputArguments
                            // Properties. A schema the converter cannot map is
                            // kept verbatim instead, which is what keeps a
                            // reported failure from also being a silent loss.
                            if (!isAction ||
                                !WotNodeSetConverter.MapsArgumentSchema(
                                    affordance.Value, property.Name))
                            {
                                Add(
                                    entries,
                                    affordancePointer + "/" + Escape(property.Name),
                                    property.Value);
                            }
                            else
                            {
                                WotConversionResult<WotMethodArgumentLayout> layout =
                                    WotNodeSetConverter.GetMethodArgumentLayout(affordance.Value, property.Name);
                                if (layout.Value?.Kind == WotMethodArgumentLayoutKind.Single)
                                {
                                    Add(entries, affordancePointer + "/" + Escape(property.Name),
                                        property.Value, resolveDefinitionBinding,
                                        resolveLocalizedContext: resolveLocalizedContext);
                                }
                                else
                                {
                                    CaptureArgumentAnnotations(
                                        document, property.Value, affordancePointer + "/" + Escape(property.Name), entries);
                                }
                            }
                            break;
                        case WotNodeSetConverter.DataMember:
                            // Section 13.3 maps an event's data schema onto the
                            // fields of the EventType, so carrying it here as
                            // well would state the same fields twice - once as
                            // the field Nodes the NodeSet gained and once as an
                            // Extension re-applied over the document generated
                            // from it. A data member that is not a schema at
                            // all is not mapped and is kept.
                            if (!isEvent ||
                                !WotNodeSetConverter.MapsEventDataSchema(affordance.Value))
                            {
                                Add(
                                    entries,
                                    affordancePointer + "/" + Escape(property.Name),
                                    property.Value);
                            }
                            break;
                        case WotNodeSetConverter.ConditionTypeTerm:
                        case WotNodeSetConverter.ConditionTypeIdTerm:
                            // Section 13.2 maps the ConditionType onto the
                            // supertype of the projected EventType, and the
                            // forward direction restates both terms from it.
                            // That only holds for the four ConditionTypes
                            // Section 13.1 scopes; a companion type pinned by
                            // ExpandedNodeId is not re-derivable, so it is kept.
                            if (!isEvent ||
                                !WotNodeSetConverter.MapsConditionType(affordance.Value))
                            {
                                Add(
                                    entries,
                                    affordancePointer + "/" + Escape(property.Name),
                                    property.Value);
                            }
                            break;
                        case WotNodeSetConverter.ConditionActionTerm:
                        case WotNodeSetConverter.ActsOnTerm:
                            // Section 13.4 maps the pairing onto the Method
                            // declaration the instance carries and onto the
                            // EventType that owns the Method, and the forward
                            // direction reads both back from there. A value
                            // outside the closed set of Section 13.2 is not
                            // mapped and is kept, so an invalid document is
                            // reported without losing what it said.
                            if (!isAction ||
                                !WotNodeSetConverter.MapsConditionAction(affordance.Value))
                            {
                                Add(
                                    entries,
                                    affordancePointer + "/" + Escape(property.Name),
                                    property.Value);
                            }
                            break;
                        case "links":
                            // Section 5.2.1 puts the definitive type-binding
                            // link on an affordance as well as on the Thing, so
                            // an affordance's links are vocabulary the converter
                            // maps. Treating them as opaque residue would round
                            // them back into the NodeSet as an Extensions
                            // fragment on top of the reference they already
                            // produced.
                            CaptureLinks(property.Value, affordancePointer + "/links", entries);
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

        private static bool IsRegeneratedLocalizedTextContext(JsonElement owner, JsonElement context)
        {
            if (!WotNodeSetConverter.IsGeneratedLocalizedTextOverride(context) &&
                !WotNodeSetConverter.IsGeneratedUnitLocalizedTextOverride(context))
            {
                return false;
            }
            foreach (JsonProperty term in context.EnumerateObject())
            {
                string plural = term.Name switch
                {
                    WotNodeSetConverter.TitleMember => WotNodeSetConverter.TitlesMember,
                    "displayName" => "displayNames",
                    _ => WotNodeSetConverter.DescriptionsMember
                };
                if (!WotNodeSetConverter.MapsLocalizedText(owner, plural) &&
                    GetString(owner, term.Name) is null)
                {
                    return false;
                }
            }
            return true;
        }

        private static string? PreserveLocalizedContext(WotDocument document, JsonElement owner, bool unit)
        {
            if (owner.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            owner.TryGetProperty("@context", out JsonElement context);
            var overrides = new List<(string Name, string? Language, JsonElement Definition, string Iri)>();
            foreach ((string singular, string plural, string iri) in unit
                ? s_unitLocalizedTerms : s_nodeLocalizedTerms)
            {
                if (GetString(owner, singular) is null)
                {
                    continue;
                }
                string? language = WotNodeSetConverter.PreservedTextLanguage(document, owner, singular, plural);
                if (WotDocument.TryGetLocalContextTerm(context, singular, out JsonElement local) &&
                    ((local.ValueKind == JsonValueKind.Null && language is null) ||
                        (local.ValueKind == JsonValueKind.Object &&
                            local.TryGetProperty("@language", out JsonElement declared) &&
                            (declared.ValueKind == JsonValueKind.Null
                                ? language is null
                                : declared.ValueKind == JsonValueKind.String && declared.GetString() == language))))
                {
                    continue;
                }
                document.TryGetContextTerm(singular, out JsonElement definition, owner);
                overrides.Add((singular, language, definition, iri));
            }
            if (overrides.Count == 0)
            {
                return null;
            }
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                bool hasContext = context.ValueKind != JsonValueKind.Undefined;
                if (hasContext)
                {
                    writer.WriteStartArray();
                    if (context.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement entry in context.EnumerateArray())
                        {
                            writer.WriteRawValue(entry.GetRawText(), skipInputValidation: true);
                        }
                    }
                    else
                    {
                        writer.WriteRawValue(context.GetRawText(), skipInputValidation: true);
                    }
                }
                writer.WriteStartObject();
                foreach ((string name, string? language, JsonElement definition, string iri) in overrides)
                {
                    writer.WritePropertyName(name);
                    writer.WriteStartObject();
                    bool identityWritten = false;
                    if (definition.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty member in definition.EnumerateObject())
                        {
                            if (member.Name != "@language")
                            {
                                member.WriteTo(writer);
                                identityWritten |= member.Name == "@id";
                            }
                        }
                    }
                    if (!identityWritten)
                    {
                        writer.WriteString("@id", definition.ValueKind == JsonValueKind.String
                            ? definition.GetString() : iri);
                    }
                    writer.WriteString("@language", language);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
                if (hasContext)
                {
                    writer.WriteEndArray();
                }
            }
            return Encoding.UTF8.GetString(output.ToArray());
        }

        private static bool ContainsUnmappedContext(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("@context", out JsonElement context) &&
                    !IsRegeneratedLocalizedTextContext(element, context))
                {
                    return true;
                }
                foreach (JsonProperty member in element.EnumerateObject())
                {
                    if (!WotDocument.IsSemanticBoundary(member.Name) && ContainsUnmappedContext(member.Value))
                    {
                        return true;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (ContainsUnmappedContext(item))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Keeps item constraints at their canonical rank depth without replacing regenerated schema facts.
        /// </summary>
        private static bool CaptureRankedItems(
            JsonElement schema,
            JsonElement items,
            string pointer,
            List<Entry> entries)
        {
            int rank = WotNodeSetConverter.ReadValueRank(schema);
            if (rank < 1 || GetString(schema, "type") != "array" || items.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            using JsonDocument? generated = WotNodeSetConverter.CreateRankedJsonType(schema);
            CaptureItemSchema(items, ItemsOf((generated?.RootElement) ?? default), rank - 1, pointer);
            return true;

            void CaptureItemSchema(JsonElement authored, JsonElement expected, int dimensions, string location)
            {
                // A flat items schema describes the matrix element; the native rank supplies its array layers.
                if (GetString(authored, "type") != "array")
                {
                    while (dimensions > 0)
                    {
                        location += "/items";
                        expected = ItemsOf(expected);
                        dimensions--;
                    }
                }
                foreach (JsonProperty member in authored.EnumerateObject())
                {
                    string memberPointer = location + "/" + Escape(member.Name);
                    if (member.Name == "items" && dimensions > 0 && member.Value.ValueKind == JsonValueKind.Object)
                    {
                        CaptureItemSchema(member.Value, ItemsOf(expected), dimensions - 1, memberPointer);
                    }
                    else if (member.Name == "type" &&
                        dimensions > 0 &&
                        member.Value.ValueKind == JsonValueKind.String &&
                        member.Value.GetString() == "array")
                    {
                        continue;
                    }
                    else if (expected.ValueKind != JsonValueKind.Object ||
                        !expected.TryGetProperty(member.Name, out JsonElement mapped) ||
                        !JsonElement.DeepEquals(member.Value, mapped))
                    {
                        Add(entries, memberPointer, member.Value);
                    }
                }
            }

            static JsonElement ItemsOf(JsonElement element)
            {
                return element.ValueKind == JsonValueKind.Object &&
                    element.TryGetProperty("items", out JsonElement items)
                    ? items
                    : default;
            }
        }

        /// <summary>
        /// Preserves unit translations that cannot all fit in one native EUInformation value.
        /// </summary>
        private static void CaptureUnitTranslations(
            WotDocument document, JsonElement units, string pointer, List<Entry> entries)
        {
            CaptureAdditionalTranslations(document, units, "displayName", "displayNames", pointer, entries);
            CaptureAdditionalTranslations(document, units, "description", "descriptions", pointer, entries);
        }

        private static void CaptureAdditionalTranslations(
            WotDocument document, JsonElement owner, string singular, string plural, string pointer, List<Entry> entries)
        {
            if (!owner.TryGetProperty(plural, out JsonElement translations) ||
                translations.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            string? selected = WotNodeSetConverter.ReadSelectedLocalizedTextLocale(document, owner, singular, plural);
            foreach (JsonProperty translation in translations.EnumerateObject())
            {
                if (translation.Name != selected)
                {
                    Add(entries, pointer + "/" + plural + "/" + Escape(translation.Name), translation.Value);
                }
            }
        }

        /// <summary>
        /// Preserves schema annotations alongside the value shape carried by Argument variables.
        /// </summary>
        private static void CaptureArgumentAnnotations(
            WotDocument document,
            JsonElement schema,
            string pointer,
            List<Entry> entries)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            foreach (JsonProperty member in schema.EnumerateObject())
            {
                switch (member.Name)
                {
                    case "@context" when WotNodeSetConverter.IsGeneratedLocalizedTextOverride(member.Value):
                        break;
                    case "type":
                    case "format":
                    case "contentEncoding":
                    case "title":
                    case "description":
                    case "uav:browseName":
                    case "uav:mapToType":
                    case "uav:dataTypeId":
                    case "uav:dataTypeName":
                    case "uav:dataTypeDefinition":
                    case "uav:fieldOrder":
                    case "required":
                    case WotNodeSetConverter.ValueRankTerm:
                    case WotNodeSetConverter.ArrayDimensionsTerm:
                        break;
                    case WotNodeSetConverter.DescriptionsMember:
                        CaptureAdditionalTranslations(
                            document, schema, "description", "descriptions", pointer, entries);
                        break;
                    case "uav:argumentLayout" when
                        member.Value.ValueKind == JsonValueKind.String && member.Value.GetString() == "named":
                        break;
                    case "properties" when member.Value.ValueKind == JsonValueKind.Object:
                        foreach (JsonProperty argument in member.Value.EnumerateObject())
                        {
                            CaptureArgumentAnnotations(
                                document, argument.Value, pointer + "/properties/" + Escape(argument.Name), entries);
                        }
                        break;
                    case "items" when member.Value.ValueKind == JsonValueKind.Object:
                        CaptureArgumentAnnotations(document, member.Value, pointer + "/items", entries);
                        break;
                    default:
                        Add(entries, pointer + "/" + Escape(member.Name), member.Value);
                        break;
                }
            }
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
                if (!IsMappedLink(rel, link))
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
                        LinkRefId = GetString(link, "uav:refId"),
                        LinkRefName = GetString(link, "uav:refName")
                    });
                }
            }
        }

        private static string GetLinkExtras(JsonElement link, out bool hasExtras)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                hasExtras = false;
                foreach (JsonProperty property in link.EnumerateObject())
                {
                    if (property.Name is "rel" or "href" or "uav:refId" or
                        "uav:refName")
                    {
                        continue;
                    }
                    if (property.Name == "uav:declaration" &&
                        WotNodeSetConverter.MapsComponentDeclaration(property.Value))
                    {
                        continue;
                    }
                    hasExtras = true;
                    writer.WritePropertyName(property.Name);
                    writer.WriteRawValue(property.Value.GetRawText(), skipInputValidation: true);
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void CaptureTypeAnnotations(JsonElement owner, string pointer, List<Entry> entries)
        {
            List<string> annotations = ReadTypeAnnotations(owner);
            if (annotations.Count == 0)
            {
                return;
            }
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                WriteTypeAnnotations(writer, annotations);
            }
            entries.Add(new Entry { Pointer = pointer, Json = Encoding.UTF8.GetString(output.ToArray()) });
        }

        private static List<string> ReadTypeAnnotations(JsonElement owner, string? nativeType = null)
        {
            var annotations = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string token in WotDocument.ReadStringTokens(owner, "@type"))
            {
                bool mapped = nativeType is null
                    ? token == WotVocabulary.ThingModelType || WotNodeSetConverter.IsNodeClassAnnotation(token)
                    : token == nativeType;
                if (!mapped &&
                    seen.Add(token))
                {
                    annotations.Add(token);
                }
            }
            return annotations;
        }

        private static void WriteTypeAnnotations(Utf8JsonWriter writer, List<string> annotations)
        {
            writer.WriteStartArray();
            foreach (string annotation in annotations)
            {
                writer.WriteStringValue(annotation);
            }
            writer.WriteEndArray();
        }

        /// <summary>
        /// Gets whether a link's <c>rel</c> is one the readable mapping already
        /// expresses, so it need not be preserved as residue.
        /// </summary>
        /// <remarks>
        /// The prefixes tested here are fixed, not context-bound, so an ordinal
        /// comparison against the literal is exact. WoT Binding Section 4
        /// requires a conforming document to bind <c>uav</c> to the Binding
        /// namespace and forbids rebinding it; Section 6.5.1 reserves <c>ua</c>
        /// for <c>http://opcfoundation.org/UA/</c>; and <c>tm</c> is fixed by
        /// the W3C WoT Thing Description 1.1 context. JSON-LD terms are
        /// case-sensitive, so the comparison must be ordinal and never
        /// ignore-case.
        /// </remarks>
        private static bool IsMappedLink(string? rel, JsonElement link)
        {
            if (rel is "tm:extends")
            {
                return true;
            }
            if (rel is null || rel.StartsWith("uav:", StringComparison.Ordinal))
            {
                return false;
            }
            if (rel.StartsWith("ua:", StringComparison.Ordinal) &&
                WotVocabulary.TryResolveReferenceTypeName(rel[3..], out string referenceType, out _) &&
                NodeSetStandardAliases.IsAbstractReferenceType(referenceType))
            {
                return false;
            }
            return rel.StartsWith("ua:", StringComparison.Ordinal) ||
                StartsWithGeneratedNamespacePrefix(rel) ||
                link.TryGetProperty("uav:refId", out _);
        }

        private static bool StartsWithGeneratedNamespacePrefix(string rel)
        {
            if (!rel.StartsWith("ns", StringComparison.Ordinal))
            {
                return false;
            }
            int ii = 2;
            while (ii < rel.Length && char.IsDigit(rel[ii]))
            {
                ii++;
            }
            return ii > 2 && ii < rel.Length && rel[ii] == ':';
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
            JsonElement value,
            Func<JsonElement, string?>? resolveDefinitionBinding = null,
            ArrayOf<string> mappedMembers = default,
            Func<JsonElement, string?>? resolveLocalizedContext = null,
            string? nativeType = null)
        {
            entries.Add(new Entry
            {
                Pointer = pointer,
                Json = WotBindingConformance.OpaqueMembers.Contains(ParsePointer(pointer)[^1])
                    ? value.GetRawText()
                    : WriteWithoutMappedTerms(value, resolveDefinitionBinding, mappedMembers, resolveLocalizedContext, nativeType)
            });
        }

        /// <summary>
        /// Serializes a residue value with the terms the converter maps removed.
        /// </summary>
        /// <remarks>
        /// Residue exists for what the mapping does not understand. A term it
        /// does understand must not travel here as well, or the round trip
        /// restores it on top of what the mapping already produced and the
        /// document states the same fact twice. An unrecognized value is stored
        /// whole, so a mapped term nested inside one has to be removed on the
        /// way in rather than merely skipped at the top level. Explicit opaque
        /// members remain traversal boundaries.
        /// </remarks>
        private static string WriteWithoutMappedTerms(
            JsonElement value,
            Func<JsonElement, string?>? resolveDefinitionBinding,
            ArrayOf<string> mappedMembers,
            Func<JsonElement, string?>? resolveLocalizedContext,
            string? nativeType)
        {
            if (mappedMembers.Count == 0 && resolveLocalizedContext is null && !ContainsMappedTerm(value))
            {
                return value.GetRawText();
            }
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                WriteStripped(writer, value, resolveDefinitionBinding, mappedMembers, resolveLocalizedContext, nativeType);
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        private static bool ContainsMappedTerm(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty member in value.EnumerateObject())
                    {
                        if (WotDocument.IsSemanticBoundary(member.Name))
                        {
                            continue;
                        }
                        if (s_mappedNestedTerms.Contains(member.Name) ||
                            ContainsMappedTerm(member.Value))
                        {
                            return true;
                        }
                    }
                    return false;
                case JsonValueKind.Array:
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        if (ContainsMappedTerm(item))
                        {
                            return true;
                        }
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static void WriteStripped(
            Utf8JsonWriter writer,
            JsonElement value,
            Func<JsonElement, string?>? resolveDefinitionBinding,
            ArrayOf<string> mappedMembers = default,
            Func<JsonElement, string?>? resolveLocalizedContext = null,
            string? nativeType = null)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    string? localizedContext = resolveLocalizedContext?.Invoke(value);
                    if (localizedContext is not null)
                    {
                        writer.WritePropertyName("@context");
                        writer.WriteRawValue(localizedContext, skipInputValidation: true);
                    }
                    if (resolveDefinitionBinding is not null &&
                        !value.TryGetProperty("uav:mapToType", out _) &&
                        !value.TryGetProperty("uav:dataTypeId", out _) &&
                        value.TryGetProperty("uav:dataTypeDefinition", out JsonElement definition) &&
                        resolveDefinitionBinding(definition) is { } dataType)
                    {
                        writer.WriteString("uav:mapToType", dataType);
                    }
                    foreach (JsonProperty member in value.EnumerateObject())
                    {
                        if (nativeType is not null &&
                            member.Name == "@type" &&
                            (member.Value.ValueKind == JsonValueKind.String ||
                                (member.Value.ValueKind == JsonValueKind.Array &&
                                    member.Value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String))))
                        {
                            List<string> annotations = ReadTypeAnnotations(value, nativeType);
                            if (annotations.Count > 0)
                            {
                                writer.WritePropertyName("@type");
                                WriteTypeAnnotations(writer, annotations);
                            }
                            continue;
                        }
                        if (s_mappedNestedTerms.Contains(member.Name) ||
                            mappedMembers.Contains(member.Name) ||
                            (member.Name == "@context" && localizedContext is not null))
                        {
                            continue;
                        }
                        writer.WritePropertyName(member.Name);
                        if (WotDocument.IsSemanticBoundary(member.Name))
                        {
                            writer.WriteRawValue(member.Value.GetRawText(), skipInputValidation: true);
                        }
                        else
                        {
                            WriteStripped(writer, member.Value, resolveDefinitionBinding,
                                resolveLocalizedContext: resolveLocalizedContext);
                        }
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        WriteStripped(writer, item, resolveDefinitionBinding,
                            resolveLocalizedContext: resolveLocalizedContext);
                    }
                    writer.WriteEndArray();
                    break;
                default:
                    value.WriteTo(writer);
                    break;
            }
        }

        private static readonly (string Singular, string Plural, string Iri)[] s_nodeLocalizedTerms =
        [
            ("title", "titles", "https://www.w3.org/2019/wot/td#title"),
            ("description", "descriptions", "https://www.w3.org/2019/wot/td#description")
        ];

        private static readonly (string Singular, string Plural, string Iri)[] s_unitLocalizedTerms =
        [
            ("displayName", "displayNames", "uav:unitDisplayName"),
            ("description", "descriptions", "uav:unitDescription")
        ];

        private static readonly HashSet<string> s_mappedNestedTerms =
            new(StringComparer.Ordinal)
            {
                "uav:dataTypeDefinition"
            };

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
                member.SetAttribute(
                    "Sha256",
                    CoreUtils.ToHexString(ComputeSha256(bytes)).ToLowerInvariant());
                SetOptionalAttribute(member, "LinkRel", entry.LinkRel);
                SetOptionalAttribute(member, "LinkHref", entry.LinkHref);
                SetOptionalAttribute(member, "LinkRefId", entry.LinkRefId);
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
                            "WoT residue exceeds the configured " +
                            $"{options.MaxJsonDocumentSize} byte limit."));
                        return entries;
                    }
                    string digest = member.GetAttribute("Sha256");
                    if (!string.Equals(
                        digest,
                        CoreUtils.ToHexString(ComputeSha256(bytes)).ToLowerInvariant(),
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
                        LinkRefId = OptionalAttribute(member, "LinkRefId"),
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

        private static JsonNode? ApplyEntry(
            JsonNode root,
            string pointer,
            JsonNode? value,
            List<WotDiagnostic> diagnostics,
            UANodeSet nodeSet,
            WotDocument generatedDocument,
            WotNodeSetConverterOptions options)
        {
            string[] tokens = ParsePointer(pointer);
            // Defensive: every caller validates the pointer with IsJsonPointer
            // first, which rejects null, empty and anything not starting with
            // '/', and string.Split never yields an empty array, so this cannot
            // currently be entered. The guard keeps the root from being
            // overwritten if pointer parsing ever changes.
            if (tokens.Length == 0)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResidueInvalid,
                    "The document root cannot be a residue target.",
                    WotLocation.FromPointer(pointer)));
                return null;
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
                    return null;
                }
            }

            string leaf = tokens[^1];
            if (current is JsonObject targetObject)
            {
                if (tokens.Length > 2 && tokens[^2] is "displayNames" or "descriptions")
                {
                    AddSelectedNativeTranslation(targetObject, pointer, tokens[^2], generatedDocument);
                }
                if (leaf == "@type" &&
                    (tokens.Length == 1 ||
                        (tokens.Length == 3 &&
                            tokens[0] is "properties" or "actions" or "events")))
                {
                    ApplyTypeAnnotations(targetObject, value, pointer, diagnostics);
                    return targetObject[leaf];
                }
                if (tokens.Length == 3 && tokens[0] == "uav:dataTypeDefinitions" && leaf == "@context")
                {
                    return ApplyDataTypeContext(targetObject, value, pointer, generatedDocument, diagnostics);
                }
                JsonNode? existing = targetObject[leaf];
                if (existing is not null)
                {
                    if (tokens.Length == 3 &&
                        tokens[0] == "actions" &&
                        leaf is WotNodeSetConverter.InputMember or WotNodeSetConverter.OutputMember &&
                        value is JsonObject authoredSchema &&
                        TryApplySingleArgumentSchema(
                            root, targetObject, tokens[1], leaf, authoredSchema, nodeSet,
                            generatedDocument, options, diagnostics))
                    {
                        return targetObject[leaf];
                    }
                    if (tokens.Length == 2 &&
                        tokens[0] is "properties" or "actions" or "events" &&
                        value is JsonObject affordance &&
                        TryApplyContextualAffordance(
                            root, targetObject, tokens[0], leaf, affordance, nodeSet, options, diagnostics))
                    {
                        return targetObject[leaf];
                    }
                    if (IsGeneratedDefaultPointer(pointer))
                    {
                        // The generated value is this library's own default and
                        // not a fact read from the NodeSet, so the authored
                        // claim the residue carries replaces it rather than
                        // disagreeing with it (WoT Binding Sections 4.1 and
                        // 10.2).
                        targetObject[leaf] = value;
                        return value;
                    }
                    if (!JsonEquals(existing, value))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.ResidueConflict,
                            $"Residue at '{pointer}' conflicts with a value " +
                            "reconstructed from OPC UA model facts.",
                            WotLocation.FromPointer(pointer)));
                        return null;
                    }
                    return existing;
                }
                targetObject[leaf] = value;
                return value;
            }
            if (current is JsonArray targetArray)
            {
                if (string.Equals(leaf, "-", StringComparison.Ordinal))
                {
                    targetArray.Add(value);
                    return value;
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
                        return null;
                    }
                    return targetArray[index];
                }
            }
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.ResidueInvalid,
                $"Residue target '{pointer}' is invalid.",
                WotLocation.FromPointer(pointer)));
            return null;
        }

        private static bool TryApplySingleArgumentSchema(
            JsonNode root,
            JsonObject action,
            string actionName,
            string member,
            JsonObject schema,
            UANodeSet nodeSet,
            WotDocument generatedDocument,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            if (!generatedDocument.Actions.TryGetValue(actionName, out JsonElement generatedAction))
            {
                return false;
            }
            JsonNode candidate = root.DeepClone();
            candidate["actions"]![actionName]![member] = schema.DeepClone();
            using WotDocument? authoredDocument = ReadResidueCandidate(
                candidate, "/actions/" + Escape(actionName) + "/" + member, options, diagnostics);
            if (authoredDocument is null)
            {
                return false;
            }
            JsonElement authoredAction = authoredDocument.Actions[actionName];
            WotConversionResult<WotMethodArgumentLayout> authored =
                WotNodeSetConverter.GetMethodArgumentLayout(authoredAction, member);
            WotConversionResult<WotMethodArgumentLayout> generated =
                WotNodeSetConverter.GetMethodArgumentLayout(generatedAction, member);
            if (!authored.Success ||
                !generated.Success ||
                authored.Value!.Kind != WotMethodArgumentLayoutKind.Single ||
                generated.Value!.ArgumentCount != 1)
            {
                return false;
            }
            var identities = new UANodeSet
            {
                NamespaceUris = nodeSet.NamespaceUris is null ? null : (string[])nodeSet.NamespaceUris.Clone()
            };
            INodeSetAliasResolver aliases = NodeSetDeclaredAliases.FromNodeSet(nodeSet, WotNodeSetAliases.Instance);
            if (!WotNodeSetConverter.PreservedArgumentSchemasMatch(
                authoredDocument, authoredAction, generatedDocument, generatedAction,
                member, identities, aliases, diagnostics, "/actions/" + Escape(actionName)))
            {
                return false;
            }
            JsonObject replacement = schema.DeepClone().AsObject();
            if (generated.Value.Kind == WotMethodArgumentLayoutKind.Named &&
                !replacement.ContainsKey("uav:browseName") &&
                !replacement.ContainsKey("title"))
            {
                string name = generated.Value.FieldOrder[0];
                string fallback = member == WotNodeSetConverter.InputMember ? "Input" : "Output";
                if (name != fallback)
                {
                    replacement["uav:browseName"] = "nsu=" + WotVocabulary.OpcUaNamespace + ";" + name;
                }
            }
            action[member] = replacement;
            return true;
        }

        private static JsonNode? ApplyDataTypeContext(
            JsonObject definition,
            JsonNode? context,
            string pointer,
            WotDocument generatedDocument,
            List<WotDiagnostic> diagnostics)
        {
            string definitionPointer = pointer[..pointer.LastIndexOf('/')];
            if (!generatedDocument.TryEvaluatePointer(definitionPointer, out JsonElement generated))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error, WotDiagnosticCode.ResidueInvalid,
                    "A DataType context has no corresponding generated definition.",
                    WotLocation.FromPointer(pointer)));
                return null;
            }
            string? name = GetString(generated, "uav:dataTypeName");
            if (name is not null)
            {
                if (!WotPortableIdentity.TryResolveQualifiedName(
                    name, generatedDocument, generated, out WotBrowsePathElement qualifiedName))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error, WotDiagnosticCode.ResidueInvalid,
                        "The generated DataType name cannot be resolved before applying its preserved context.",
                        WotLocation.FromPointer(pointer)));
                    return null;
                }
                // A preserved scope may reset or rebind the generator's namespace prefixes.
                name = "nsu=" + CoreUtils.EscapeUri(qualifiedName.NamespaceUri!) + ";" + qualifiedName.Name;
            }
            bool hasGeneratedContext = generated.TryGetProperty("@context", out JsonElement originalContext);
            JsonNode? generatedContext = hasGeneratedContext
                ? JsonNode.Parse(originalContext.GetRawText()) : null;
            JsonNode? restoredContext = context;
            if (generatedContext is not null && !JsonEquals(generatedContext, context))
            {
                var combined = new JsonArray();
                if (context is JsonArray entries)
                {
                    foreach (JsonNode? entry in entries)
                    {
                        combined.Add(entry?.DeepClone());
                    }
                }
                else
                {
                    combined.Add(context?.DeepClone());
                }
                combined.Add(generatedContext);
                restoredContext = combined;
            }
            if (definition.TryGetPropertyValue("@context", out JsonNode? existing) &&
                (!hasGeneratedContext || !JsonEquals(existing, generatedContext)) &&
                !JsonEquals(existing, restoredContext))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error, WotDiagnosticCode.ResidueConflict,
                    $"Residue at '{pointer}' conflicts with an already restored DataType context.",
                    WotLocation.FromPointer(pointer)));
                return null;
            }
            if (name is not null)
            {
                definition["uav:dataTypeName"] = name;
            }
            definition["@context"] = restoredContext;
            return restoredContext;
        }

        private static bool TryApplyContextualAffordance(
            JsonNode root,
            JsonObject affordances,
            string collection,
            string name,
            JsonObject affordance,
            UANodeSet nodeSet,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            JsonObject replacement = affordance.DeepClone().AsObject();
            if (affordances[name] is JsonObject generated)
            {
                foreach (string member in new[] { WotNodeSetConverter.UnitPropertyTerm, WotNodeSetConverter.UnitMember })
                {
                    if (!replacement.ContainsKey(member) && generated.TryGetPropertyValue(member, out JsonNode? value))
                    {
                        replacement[member] = value?.DeepClone();
                    }
                }
            }
            JsonNode candidate = root.DeepClone();
            candidate[collection]![name] = replacement.DeepClone();
            using WotDocument? document = ReadResidueCandidate(
                candidate, "/" + collection + "/" + Escape(name), options, diagnostics);
            if (document is null ||
                !ContainsUnmappedContext(document.RootElement.GetProperty(collection).GetProperty(name)))
            {
                return false;
            }
            var conflicts = new List<WotDiagnostic>();
            WotNodeSetConverter.ValidatePreservedReadableFacts(document, nodeSet, options, conflicts);
            diagnostics.AddRange(conflicts);
            if (conflicts.Any(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error))
            {
                return false;
            }
            if (affordances[name] is JsonObject generatedAffordance &&
                generatedAffordance.TryGetPropertyValue("@type", out JsonNode? generatedTypes))
            {
                JsonNode? authoredTypes = replacement["@type"]?.DeepClone();
                replacement["@type"] = generatedTypes?.DeepClone();
                if (authoredTypes is not null and not JsonArray { Count: 0 })
                {
                    conflicts.Clear();
                    ApplyTypeAnnotations(
                        replacement, authoredTypes, "/" + collection + "/" + Escape(name) + "/@type", conflicts);
                    diagnostics.AddRange(conflicts);
                    if (conflicts.Any(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error))
                    {
                        return false;
                    }
                }
            }
            affordances[name] = replacement;
            return true;
        }

        private static void AddSelectedNativeTranslation(
            JsonObject translations, string pointer, string plural, WotDocument generatedDocument)
        {
            int localeSeparator = pointer.LastIndexOf('/');
            int pluralSeparator = pointer.LastIndexOf('/', localeSeparator - 1);
            if (pluralSeparator < 0 ||
                !generatedDocument.TryEvaluatePointer(pointer[..pluralSeparator], out JsonElement owner))
            {
                return;
            }
            string singular = plural == "displayNames" ? "displayName" : "description";
            string? selected = WotNodeSetConverter.ReadSelectedLocalizedTextLocale(
                generatedDocument, owner, singular, plural);
            if (!string.IsNullOrEmpty(selected) &&
                !translations.ContainsKey(selected) &&
                GetString(owner, singular) is { } text)
            {
                translations[selected] = text;
            }
        }

        private static WotDocument? ReadResidueCandidate(
            JsonNode candidate,
            string pointer,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            using var output = new MemoryStream();
            using var writer = new Utf8JsonWriter(
                output, new JsonWriterOptions { MaxDepth = options.MaxJsonDepth });
            try
            {
                candidate.WriteTo(writer);
                writer.Flush();
                if (output.Length > options.MaxJsonDocumentSize)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error, WotDiagnosticCode.ResidueInvalid,
                        "The restored readable declaration exceeds the JSON document size limit.",
                        WotLocation.FromPointer(pointer)));
                    return null;
                }
                return WotDocument.FromOwnedBytes(output.ToArray(), options);
            }
            catch (InvalidOperationException exception) when (writer.CurrentDepth >= options.MaxJsonDepth)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error, WotDiagnosticCode.ResidueInvalid,
                    "The restored readable declaration exceeds the combined JSON depth: " + exception.Message,
                    WotLocation.FromPointer(pointer)));
                return null;
            }
            catch (JsonException exception)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error, WotDiagnosticCode.ResidueInvalid,
                    "The restored readable declaration is not valid within the JSON limits: " + exception.Message,
                    WotLocation.FromPointer(pointer)));
                return null;
            }
        }

        private static void ApplyTypeAnnotations(
            JsonObject owner, JsonNode? value, string pointer, List<WotDiagnostic> diagnostics)
        {
            var existing = new List<string>();
            var annotations = new List<string>();
            if (!ReadTypeTokens(owner["@type"], existing) || !ReadTypeTokens(value, annotations) || annotations.Count == 0)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResidueInvalid,
                    "Semantic type residue requires string tokens.",
                    WotLocation.FromPointer(pointer)));
                return;
            }
            foreach (string token in annotations)
            {
                if ((token == WotVocabulary.ThingModelType || WotNodeSetConverter.IsNodeClassAnnotation(token)) &&
                    !existing.Contains(token))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ResidueConflict,
                        "Semantic type residue cannot introduce a document kind or NodeClass annotation.",
                        WotLocation.FromPointer(pointer)));
                    return;
                }
            }
            var seen = new HashSet<string>(existing, StringComparer.Ordinal);
            var combined = new JsonArray();
            foreach (string token in existing)
            {
                combined.Add(JsonValue.Create(token));
            }
            foreach (string token in annotations)
            {
                if (seen.Add(token))
                {
                    combined.Add(JsonValue.Create(token));
                }
            }
            owner["@type"] = combined;
        }

        private static bool ReadTypeTokens(JsonNode? value, List<string> tokens)
        {
            if (value is null)
            {
                return true;
            }
            if (value is JsonValue scalar && scalar.TryGetValue(out string? token) && token is not null)
            {
                tokens.Add(token);
                return true;
            }
            if (value is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    if (item is not JsonValue element || !element.TryGetValue(out string? text) || text is null)
                    {
                        return false;
                    }
                    tokens.Add(text);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets whether a residue pointer targets a member whose generated
        /// value is this library's own default rather than a fact read from the
        /// NodeSet.
        /// </summary>
        /// <remarks>
        /// Two members are of this kind. <c>uav:bindingVersion</c> is stamped
        /// on every generated document by Section 4.1, so the author's own
        /// claim - a revision this library preserves rather than rejects - has
        /// to win over the stamp instead of being reported as a conflict with
        /// it. An event affordance's <c>data</c> object is the other: the
        /// generator always writes one, because Section 6.1 selects the eight
        /// mandatory <c>BaseEventType</c> fields where a document states no
        /// select clauses, and a residue entry exists for it only where the
        /// authored schema materialized nothing at all. Restoring the authored
        /// value is what keeps "preserved, not replaced" true of a <c>data</c>
        /// member the converter could not map.
        /// </remarks>
        private static bool IsGeneratedDefaultPointer(string pointer)
        {
            return string.Equals(
                pointer,
                "/" + WotBindingConformance.BindingVersionTerm,
                StringComparison.Ordinal) ||
                IsEventDataPointer(pointer);
        }

        /// <summary>
        /// Gets whether a pointer names the <c>data</c> member of an event
        /// affordance, that is <c>/events/&lt;name&gt;/data</c> exactly.
        /// </summary>
        private static bool IsEventDataPointer(string pointer)
        {
            const string prefix = "/events/";
            const string suffix = "/" + WotNodeSetConverter.DataMember;
            if (!pointer.StartsWith(prefix, StringComparison.Ordinal) ||
                !pointer.EndsWith(suffix, StringComparison.Ordinal) ||
                pointer.Length <= prefix.Length + suffix.Length)
            {
                return false;
            }
            // A member name containing '/' is escaped as '~1' by the capture, so
            // a further separator can only be a further pointer token.
            return pointer.IndexOf(
                '/', prefix.Length, pointer.Length - prefix.Length - suffix.Length) < 0;
        }

        private static JsonObject? ApplyLinkEntry(
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
                return null;
            }

            string[] tokens = ParsePointer(entry.Pointer);
            if (tokens.Length < 2 || tokens[^2] != "links" || tokens[^1] != "-")
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResidueInvalid,
                    "A link residue selector must address a links array.",
                    WotLocation.FromPointer(entry.Pointer)));
                return null;
            }
            JsonNode owner = rootObject;
            for (int index = 0; index < tokens.Length - 2; index++)
            {
                if (owner is not JsonObject parent || parent[tokens[index]] is not JsonNode child)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ResidueInvalid,
                        "The owning node of a link residue selector does not exist.",
                        WotLocation.FromPointer(entry.Pointer)));
                    return null;
                }
                owner = child;
            }
            if (owner is not JsonObject linkOwner)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResidueInvalid,
                    "A link residue selector requires an object owner.",
                    WotLocation.FromPointer(entry.Pointer)));
                return null;
            }

            JsonArray links;
            if (linkOwner["links"] is JsonArray existingLinks)
            {
                links = existingLinks;
            }
            else if (linkOwner["links"] is null)
            {
                links = [];
                linkOwner["links"] = links;
            }
            else
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ResidueConflict,
                    "Link residue conflicts with a non-array links member.",
                    WotLocation.FromPointer(entry.Pointer[..^2])));
                return null;
            }

            JsonObject? target = FindLink(links, entry, requireExactRel: true);
            bool exact = target is not null;
            target ??= FindLink(links, entry, requireExactRel: false);
            if (target is null)
            {
                target = [];
                SetString(target, "rel", entry.LinkRel);
                SetString(target, "href", entry.LinkHref);
                SetString(target, "uav:refId", entry.LinkRefId);
                SetString(target, "uav:refName", entry.LinkRefName);
                links.Add(target);
            }
            else
            {
                if (exact)
                {
                    MergeString(target, "rel", entry.LinkRel, entry.Pointer, diagnostics);
                }
                MergeString(target, "href", entry.LinkHref, entry.Pointer, diagnostics);
                MergeString(
                    target,
                    "uav:refId",
                    entry.LinkRefId,
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
            return target;
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
                if (TryGetStandardLinkReference(entry.LinkRel, out string referenceType, out bool isForward) &&
                    link["rel"] is JsonValue relationValue &&
                    relationValue.TryGetValue(out string? relation) &&
                    TryGetStandardLinkReference(relation, out string candidateType, out bool candidateForward))
                {
                    if (referenceType == candidateType && isForward == candidateForward)
                    {
                        return link;
                    }
                    continue;
                }
                if (entry.LinkRefId is not null &&
                    StringNodeEquals(link["uav:refId"], entry.LinkRefId))
                {
                    return link;
                }
            }
            return null;
        }

        private static bool TryGetStandardLinkReference(
            string? relation,
            out string referenceType,
            out bool isForward)
        {
            if (relation == "tm:extends")
            {
                referenceType = WotVocabulary.HasSubtype;
                isForward = false;
                return true;
            }
            if (relation?.StartsWith("ua:", StringComparison.Ordinal) == true)
            {
                return WotVocabulary.TryResolveReferenceTypeName(
                    relation[3..], out referenceType, out isForward);
            }
            referenceType = string.Empty;
            isForward = true;
            return false;
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

        /// <summary>
        /// Determines whether a residue entry holds the same JSON value as the
        /// member the readable mapping already produced (WoT Binding Section
        /// 9.4).
        /// </summary>
        /// <remarks>
        /// Equality is the RFC 8785 one, so a reordered object, an equivalent
        /// string escape and <c>1.0</c> beside <c>1</c> are the same value
        /// rather than a conflict. Where a value cannot be canonicalized - a
        /// number outside the interoperable domain of RFC 8259 Section 6 - the
        /// two are compared as written instead, which can report a conflict
        /// that JCS would not, but never reports two different values as one.
        /// The retained-bytes digest of a residue member is untouched by this:
        /// it is taken over the bytes the producer encoded, and nothing here
        /// reformats them.
        /// </remarks>
        private static bool JsonEquals(JsonNode? left, JsonNode? right)
        {
            if (WotJsonCanonicalizer.TryEquals(left, right, out bool equal, out _))
            {
                return equal;
            }
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
            string[] tokens = pointer[1..].Split('/');
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
            string[] tokens = pointer[1..].Split('/');
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
                builder.Append(source, start, index - start)
                    .Append(newValue);
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
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(data);
#endif
        }
    }
}
