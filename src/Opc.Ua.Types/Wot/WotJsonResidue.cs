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

            public string? LinkRefId { get; init; }

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
                            "Unmapped WoT residue exceeds the configured " +
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
                        CaptureAffordanceMap(
                            root, property.Value, pointer, property.Name, entries);
                        break;
                    case "links":
                        CaptureLinks(property.Value, pointer, entries);
                        break;
                    case "@type":
                    case "title":
                    case "description":
                    case "uav:browseName":
                    case "uav:id":
                    case "uav:hasComponent":
                    case "uav:componentOf":
                    case "uav:nodeSet":
                    case "uav:nodes":
                    case "uav:dataTypeDefinitions":
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
                    (string.Equals(
                            item.GetString(),
                            WotVocabulary.WotContext,
                            StringComparison.Ordinal) ||
                        string.Equals(
                            item.GetString(),
                            WotVocabulary.BindingContext,
                            StringComparison.Ordinal)))
                {
                    // Both context identities are re-derived by the forward
                    // direction, which names them on every document it writes.
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
                        if (!IsGeneratedContextBinding(property))
                        {
                            Add(
                                entries,
                                pointer + "/1/" + Escape(property.Name),
                                property.Value);
                        }
                    }
                    continue;
                }
                if (item.ValueKind == JsonValueKind.Object &&
                    WotNodeSetConverter.IsGeneratedLocalizedTextOverride(item))
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

        private static bool IsGeneratedContextBinding(JsonProperty property)
        {
            if (property.Name is "uav" or "ua")
            {
                return true;
            }

            // Section 9.1.1 makes @language the document's default locale, and
            // the forward direction derives it from the locale the projected
            // Node's own LocalizedText carries - which the reverse direction
            // writes back onto it. So the declaration is re-derivable from the
            // NodeSet and carrying it as residue as well would state the same
            // language twice.
            if (property.Name is "@language")
            {
                return property.Value.ValueKind == JsonValueKind.String;
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
            JsonElement root,
            JsonElement map,
            string pointer,
            string kind,
            List<Entry> entries)
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
                        case "uav:modellingRule":
                        case "uav:mapToType":
                        case "uav:dataTypeDefinition":
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

        private static string UniqueKey(string candidate, HashSet<string> used)
        {
            if (used.Add(candidate))
            {
                return candidate;
            }
            int suffix = 2;
            string unique = candidate +
                "_" +
                suffix.ToString(CultureInfo.InvariantCulture);
            while (!used.Add(unique))
            {
                suffix++;
                unique = candidate +
                    "_" +
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
            using var stream = new System.IO.MemoryStream();
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
                    hasExtras = true;
                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
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

        private static string? LocalName(string? browseName)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                return null;
            }
            if (browseName!.StartsWith("nsu=", StringComparison.Ordinal))
            {
                for (int ii = 4; ii < browseName.Length; ii++)
                {
                    if (browseName[ii] == ';')
                    {
                        return ii + 1 < browseName.Length
                            ? browseName.Substring(ii + 1)
                            : null;
                    }
                }
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
                Json = WriteWithoutMappedTerms(value)
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
        /// way in rather than merely skipped at the top level.
        /// </remarks>
        private static string WriteWithoutMappedTerms(JsonElement value)
        {
            if (!ContainsMappedTerm(value))
            {
                return value.GetRawText();
            }
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                WriteStripped(writer, value);
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

        private static void WriteStripped(Utf8JsonWriter writer, JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (JsonProperty member in value.EnumerateObject())
                    {
                        if (s_mappedNestedTerms.Contains(member.Name))
                        {
                            continue;
                        }
                        writer.WritePropertyName(member.Name);
                        WriteStripped(writer, member.Value);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        WriteStripped(writer, item);
                    }
                    writer.WriteEndArray();
                    break;
                default:
                    value.WriteTo(writer);
                    break;
            }
        }

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

        private static void ApplyEntry(
            JsonNode root,
            string pointer,
            JsonNode? value,
            List<WotDiagnostic> diagnostics)
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
                    if (IsGeneratedDefaultPointer(pointer))
                    {
                        // The generated value is this library's own default and
                        // not a fact read from the NodeSet, so the authored
                        // claim the residue carries replaces it rather than
                        // disagreeing with it (WoT Binding Sections 4.1 and
                        // 10.2).
                        targetObject[leaf] = value;
                        return;
                    }
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
                SetString(target, "uav:refId", entry.LinkRefId);
                SetString(target, "uav:refName", entry.LinkRefName);
                links.Add(target);
            }
            else if (exact)
            {
                MergeString(target, "rel", entry.LinkRel, entry.Pointer, diagnostics);
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
                if (entry.LinkRefId is not null &&
                    StringNodeEquals(link["uav:refId"], entry.LinkRefId))
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
            using SHA256 sha256 = SHA256.Create();
            return sha256.ComputeHash(data);
#endif
        }
    }
}
