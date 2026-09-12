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
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    public sealed partial class WotProjectionResolver
    {
        private sealed partial class SchemaReferenceClosure
        {
            private void InitializeDataTypeCarriage()
            {
                IndexDataTypes(m_host);
                foreach (ResolvedAffordance member in m_selection.Members)
                {
                    IndexDataTypes(new ReferenceOwner(
                        member.Source.Document, member.Source.DocumentHref, member.Source.Source.SourceName));
                }
                bool retainsDefinitions = m_root.ContainsKey(DataTypesTerm);
                m_root.Remove(DataTypesTerm);
                if (retainsDefinitions)
                {
                    m_root[DataTypesTerm] = m_dataTypeDefinitions;
                    if (m_host.Document.RootElement.TryGetProperty(DataTypesTerm, out JsonElement definitions))
                    {
                        if (definitions.ValueKind != JsonValueKind.Array)
                        {
                            DataTypeError("The DataType definition collection must be an array.", "/" + DataTypesTerm);
                            return;
                        }
                        foreach (JsonElement definition in definitions.EnumerateArray())
                        {
                            CarryDataTypeReference(m_host, definition, "/" + DataTypesTerm);
                        }
                    }
                }
            }

            private void IndexDataTypes(ReferenceOwner owner)
            {
                if (m_dataTypeIndexes.ContainsKey(owner.Document))
                {
                    return;
                }
                var index = new DataTypeIndex();
                m_dataTypeIndexes.Add(owner.Document, index);
                foreach (JsonProperty member in owner.Document.RootElement.EnumerateObject())
                {
                    if (member.Name != DataTypesTerm)
                    {
                        continue;
                    }
                    if (member.Value.ValueKind != JsonValueKind.Array)
                    {
                        DataTypeError("The DataType definition collection must be an array.", "/" + DataTypesTerm);
                        continue;
                    }
                    foreach (JsonElement definition in member.Value.EnumerateArray())
                    {
                        if (definition.ValueKind != JsonValueKind.Object)
                        {
                            DataTypeError("Every DataType definition must be an object.", "/" + DataTypesTerm);
                        }
                    }
                }
                foreach ((JsonElement definition, string pointer) in
                    WotNodeSetConverter.ReadDataTypeDefinitionLocations(owner.Document.RootElement))
                {
                    string? authoredId = ElementString(definition, "@id");
                    if (string.IsNullOrEmpty(authoredId))
                    {
                        DataTypeError("A DataType definition must identify its graph node with @id.", pointer);
                        continue;
                    }
                    string graphId = ExpandGraphIdentity(owner, definition, authoredId!);
                    if (WotNodeSetConverter.IsReferenceOnlyDefinition(definition))
                    {
                        continue;
                    }
                    if (index.GraphIds.ContainsKey(graphId))
                    {
                        DataTypeError("A DataType graph node has more than one complete definition.", pointer);
                        continue;
                    }
                    var nodeSet = new UANodeSet();
                    string? local = WotNodeSetConverter.ResolveDataTypeIdentity(
                        owner.Document, definition, nodeSet, m_diagnostics);
                    if (local is null)
                    {
                        continue;
                    }
                    string nativeId = WotNodeSetConverter.NormalizeExpandedNodeId(
                        WotNodeSetConverter.ToPortableNodeId(local, nodeSet.NamespaceUris) ?? local);
                    if (!WotPortableIdentity.TryResolveQualifiedName(
                        ElementString(definition, "uav:dataTypeName"), owner.Document, definition,
                        out WotBrowsePathElement name))
                    {
                        DataTypeError("A DataType name has no resolved namespace.", pointer);
                        continue;
                    }
                    var entry = new DataTypeEntry(
                        owner, definition, pointer, graphId, nativeId, QualifiedTypeName(name));
                    if (!index.Locations.TryAdd(pointer, entry))
                    {
                        DataTypeError("More than one complete DataType occupies the same source location.", pointer);
                        continue;
                    }
                    index.GraphIds.Add(graphId, entry);
                    AddIdentity(index.NativeIds, nativeId, entry, pointer);
                    AddIdentity(index.Names, QualifiedTypeName(name), entry, pointer);
                }
            }

            private void AddIdentity(
                Dictionary<string, DataTypeEntry> identities, string key, DataTypeEntry entry, string pointer)
            {
                if (!identities.TryAdd(key, entry))
                {
                    DataTypeError(
                        "More than one DataType definition claims the same native identity or name.", pointer);
                }
            }

            private void RewriteDataTypeReferences(ReferenceCarriage carriage)
            {
                if (!m_dataTypeIndexes.TryGetValue(carriage.Owner.Document, out DataTypeIndex? index))
                {
                    return;
                }
                foreach (string term in s_dataTypeReferences)
                {
                    if (!carriage.Value.TryGetPropertyValue(term, out JsonNode? value))
                    {
                        continue;
                    }
                    if (value is JsonObject reference)
                    {
                        string sourcePointer = carriage.SourcePointer + "/" + EscapePointer(term);
                        if (!WotDocument.TryEvaluatePointer(
                            carriage.Owner.Document.RootElement, sourcePointer, out JsonElement original))
                        {
                            DataTypeError("A carried DataType reference lost its original location.", sourcePointer);
                            continue;
                        }
                        if (term == "uav:dataTypeSubtypeOf")
                        {
                            CarryBaseReference(carriage.Owner, original, reference, index, sourcePointer);
                            continue;
                        }
                        string? graphId = CarryDataTypeReference(carriage.Owner, original, sourcePointer);
                        if (graphId is not null)
                        {
                            carriage.Value[term] = new JsonObject { ["@id"] = graphId };
                        }
                    }
                    else if (term != "uav:dataTypeSubtypeOf" || ReadString(value) is null)
                    {
                        DataTypeError("A DataType definition reference must be an object.",
                            carriage.SourcePointer + "/" + EscapePointer(term));
                    }
                }
                CarryKnownIdentity(carriage, index, "uav:dataTypeId", false);
                CarryKnownIdentity(carriage, index, "uav:fieldDataTypeId", false);
                CarryKnownIdentity(carriage, index, "uav:dataTypeName", true);
                CarryKnownIdentity(carriage, index, "uav:fieldDataTypeName", true);

                if (ReadString(carriage.Value["uav:dataTypeSubtypeOf"]) is string subtype)
                {
                    JsonElement original = OriginalElement(carriage);
                    DataTypeEntry? graph = FindGraphDefinition(
                        carriage.Owner, original, subtype, carriage.SourcePointer, out string graphId);
                    if (graph is not null)
                    {
                        CarryDataType(graph);
                        carriage.Value["uav:dataTypeSubtypeOf"] = graphId;
                    }
                    else
                    {
                        CarryKnownIdentity(carriage, index, "uav:dataTypeSubtypeOf",
                            !WotPortableIdentity.IsPortableNodeId(subtype));
                    }
                }
                if (carriage.Value["uav:fields"] is JsonArray fields)
                {
                    for (int position = 0; position < fields.Count; position++)
                    {
                        if (fields[position] is JsonObject field)
                        {
                            string suffix = "/uav:fields/" + position.ToString(CultureInfo.InvariantCulture);
                            m_pending.Enqueue(new ReferenceCarriage(field, carriage.Owner,
                                carriage.SourcePointer + suffix, carriage.Destination + suffix));
                        }
                    }
                }
            }

            private void CarryKnownIdentity(
                ReferenceCarriage carriage, DataTypeIndex index, string term, bool name)
            {
                string? identity = ReadString(carriage.Value[term]);
                if (identity is null)
                {
                    return;
                }
                Dictionary<string, DataTypeEntry> identities;
                if (name)
                {
                    if (!WotPortableIdentity.TryResolveQualifiedName(identity, carriage.Owner.Document,
                        OriginalElement(carriage), out WotBrowsePathElement qualified))
                    {
                        return;
                    }
                    identity = QualifiedTypeName(qualified);
                    identities = index.Names;
                }
                else
                {
                    identity = WotNodeSetConverter.NormalizeExpandedNodeId(identity);
                    identities = index.NativeIds;
                }
                if (identities.TryGetValue(identity, out DataTypeEntry? definition))
                {
                    CarryDataType(definition);
                }
            }

            private void CarryBaseReference(
                ReferenceOwner owner, JsonElement original, JsonObject reference, DataTypeIndex index, string pointer)
            {
                string? graphId = null;
                string? nativeId = null;
                string? name = null;
                DataTypeEntry? selected = null;
                foreach (JsonProperty member in original.EnumerateObject())
                {
                    if (member.Name is not ("@id" or "uav:dataTypeId" or "uav:dataTypeName"))
                    {
                        continue;
                    }
                    if (member.Value.ValueKind != JsonValueKind.String ||
                        string.IsNullOrEmpty(member.Value.GetString()))
                    {
                        DataTypeError("A supplied base DataType identity must be a non-empty string.", pointer);
                        return;
                    }
                    string text = member.Value.GetString()!;
                    DataTypeEntry? candidate;
                    switch (member.Name)
                    {
                        case "@id":
                            candidate = FindGraphDefinition(owner, original, text, pointer, out graphId);
                            reference["@id"] = graphId;
                            break;
                        case "uav:dataTypeId":
                            if (!WotPortableIdentity.IsPortableNodeId(text))
                            {
                                DataTypeError("A base DataType identity must be a portable NodeId.", pointer);
                                return;
                            }
                            nativeId = WotNodeSetConverter.NormalizeExpandedNodeId(text);
                            index.NativeIds.TryGetValue(nativeId, out candidate);
                            break;
                        default:
                            if (!WotPortableIdentity.TryResolveQualifiedName(
                                text, owner.Document, original, out WotBrowsePathElement qualified))
                            {
                                DataTypeError("A base DataType name must have a resolved namespace.", pointer);
                                return;
                            }
                            name = QualifiedTypeName(qualified);
                            index.Names.TryGetValue(name, out candidate);
                            break;
                    }
                    if (candidate is not null)
                    {
                        if (selected is not null && selected != candidate)
                        {
                            DataTypeError("Supplied base DataType identities name different definitions.", pointer);
                            return;
                        }
                        selected = candidate;
                    }
                }
                if (graphId is null && nativeId is null && name is null)
                {
                    DataTypeError("A base DataType reference has no graph identity, native identity or name.", pointer);
                    return;
                }
                if (selected is not null)
                {
                    if ((graphId is not null && graphId != selected.GraphId) ||
                        (nativeId is not null && nativeId != selected.NativeId) ||
                        (name is not null && name != selected.Name))
                    {
                        DataTypeError("Supplied base DataType identities disagree with the known definition.", pointer);
                        return;
                    }
                    CarryDataType(selected);
                }
            }

            private string? CarryDataTypeReference(ReferenceOwner owner, JsonElement reference, string pointer)
            {
                string? authored = ElementString(reference, "@id");
                if (string.IsNullOrEmpty(authored))
                {
                    DataTypeError("A DataType reference must identify its graph node with @id.", pointer);
                    return null;
                }
                DataTypeEntry? definition = FindGraphDefinition(
                    owner, reference, authored!, pointer, out string graphId);
                if (definition is not null)
                {
                    CarryDataType(definition);
                }
                return graphId;
            }

            private DataTypeEntry? FindGraphDefinition(
                ReferenceOwner owner, JsonElement context, string identity, string pointer, out string graphId)
            {
                graphId = ExpandGraphIdentity(owner, context, identity);
                DataTypeIndex index = m_dataTypeIndexes[owner.Document];
                if (index.GraphIds.TryGetValue(graphId, out DataTypeEntry? definition))
                {
                    return definition;
                }
                string localPointer = SplitPointer(graphId);
                if (localPointer.StartsWith('/') &&
                    string.Equals(SplitDocumentPart(graphId), owner.Href, StringComparison.Ordinal))
                {
                    if (IsCanonicalPointer(localPointer) && index.Locations.TryGetValue(localPointer, out definition))
                    {
                        graphId = definition.GraphId;
                        return definition;
                    }
                    DataTypeError("A local DataType pointer must identify a complete semantic definition.", pointer);
                }
                return null;
            }

            private void CarryDataType(DataTypeEntry definition)
            {
                if (!m_checkedDataTypeOccurrences.Add((definition.Owner.Document, definition.Pointer)))
                {
                    return;
                }
                if (m_carriedDataTypes.TryGetValue(definition.GraphId, out DataTypeEntry? previous))
                {
                    if (definition.NativeId != previous.NativeId ||
                        !EquivalentDataTypeFacts(previous, definition))
                    {
                        DataTypeError(
                            "A carried DataType graph identity has conflicting or incomparable resolved facts.",
                            definition.Pointer);
                        return;
                    }
                    string existingDestination = m_locations[
                        (previous.Owner.SourceName is null, previous.Owner.Href, previous.Pointer)];
                    m_locations.TryAdd(
                        (definition.Owner.SourceName is null, definition.Owner.Href, definition.Pointer),
                        existingDestination);
                    m_pending.Enqueue(new ReferenceCarriage(
                        CloneUriVariableSchema(definition.Owner.Document, definition.Definition),
                        definition.Owner, definition.Pointer, existingDestination));
                    return;
                }
                if (m_carriedNativeTypes.TryGetValue(definition.NativeId, out DataTypeEntry? claimed))
                {
                    DataTypeError(
                        $"DataType graph nodes '{claimed.GraphId}' and '{definition.GraphId}' " +
                        "claim one native identity.",
                        definition.Pointer);
                    return;
                }
                if ((long)m_selection.Members.Count +
                    (m_definitions?.Count ?? 0) +
                    m_uriVariableCount +
                    m_dataTypeCount >= m_options.MaxNodeCount)
                {
                    BudgetError();
                    return;
                }
                JsonObject value = CloneUriVariableSchema(definition.Owner.Document, definition.Definition);
                value["@id"] = definition.GraphId;
                string destination = "/" + DataTypesTerm + "/" + m_dataTypeCount.ToString(CultureInfo.InvariantCulture);
                m_dataTypeDefinitions.Add(value);
                m_dataTypeCount++;
                if (!m_root.ContainsKey(DataTypesTerm))
                {
                    m_root[DataTypesTerm] = m_dataTypeDefinitions;
                }
                m_carriedDataTypes.Add(definition.GraphId, definition);
                m_carriedNativeTypes.Add(definition.NativeId, definition);
                m_locations.TryAdd((definition.Owner.SourceName is null, definition.Owner.Href, definition.Pointer),
                    destination);
                m_pending.Enqueue(new ReferenceCarriage(value, definition.Owner, definition.Pointer, destination));
            }

            private bool EquivalentDataTypeFacts(DataTypeEntry first, DataTypeEntry second)
            {
                using var left = JsonDocument.Parse(Serialize(ResolvedDefinition(first)));
                using var right = JsonDocument.Parse(Serialize(ResolvedDefinition(second)));
                return WotJsonCanonicalizer.TryCanonicalize(left.RootElement, out string leftKey, out _) &&
                    WotJsonCanonicalizer.TryCanonicalize(right.RootElement, out string rightKey, out _) &&
                    string.Equals(leftKey, rightKey, StringComparison.Ordinal);
            }

            private JsonObject ResolvedDefinition(DataTypeEntry entry)
            {
                var scopes = new JsonObject();
                var result = (JsonObject)ResolveFacts(entry.Definition, entry.Definition, null, string.Empty)!;
                result["@id"] = entry.GraphId;
                result["uav:dataTypeId"] = entry.NativeId;
                return new JsonObject
                {
                    ["facts"] = result,
                    ["scopes"] = scopes
                };

                JsonNode? ResolveFacts(JsonElement value, JsonElement context, string? term, string pointer)
                {
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        var resolved = new JsonObject();
                        var languages = new JsonObject();
                        bool unknownTerms = false;
                        foreach (JsonProperty member in value.EnumerateObject())
                        {
                            if (member.Name == "@context")
                            {
                                continue;
                            }
                            bool localized = member.Name is "title" or "description" or "uav:fieldDescription" or
                                "uav:enumDisplayName" or "uav:enumDescription";
                            resolved[member.Name] = WotDocument.IsSemanticBoundary(member.Name) ||
                                localized ||
                                member.Name is "titles" or "descriptions"
                                ? CloneNode(member.Value)
                                : ResolveFacts(member.Value, value, member.Name,
                                    pointer + "/" + EscapePointer(member.Name));
                            if (localized)
                            {
                                languages[member.Name] = JsonValue.Create(
                                    WotNodeSetConverter.GetDeclaredLocale(entry.Owner.Document, value, member.Name));
                            }
                            unknownTerms |= !member.Name.StartsWith("uav:", StringComparison.Ordinal) &&
                                member.Name is not ("@id" or "@type" or "title" or "description" or
                                    "titles" or "descriptions");
                        }
                        if (unknownTerms || languages.Count != 0)
                        {
                            var scope = new JsonObject();
                            if (unknownTerms)
                            {
                                var sequence = new JsonArray();
                                foreach (JsonElement ownerContext in entry.Owner.Document.GetContextSequence(value))
                                {
                                    AppendContext(sequence, ownerContext);
                                }
                                scope["context"] = sequence;
                            }
                            if (languages.Count != 0)
                            {
                                scope["languages"] = languages;
                            }
                            scopes[pointer] = scope;
                        }
                        return resolved;
                    }
                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        var resolved = new JsonArray();
                        int index = 0;
                        foreach (JsonElement item in value.EnumerateArray())
                        {
                            resolved.Add(ResolveFacts(item, context, term,
                                pointer + "/" + index.ToString(CultureInfo.InvariantCulture)));
                            index++;
                        }
                        return resolved;
                    }
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        string text = value.GetString()!;
                        if (term is "uav:dataTypeName" or "uav:fieldDataTypeName" &&
                            WotPortableIdentity.TryResolveQualifiedName(
                                text, entry.Owner.Document, context, out WotBrowsePathElement name))
                        {
                            return JsonValue.Create(QualifiedTypeName(name));
                        }
                        if (term is "@id" or "@type")
                        {
                            return JsonValue.Create(ExpandGraphIdentity(entry.Owner, context, text));
                        }
                        if (term is not null && Array.IndexOf(s_locationReferences, term) >= 0)
                        {
                            return JsonValue.Create(ResolveHref(entry.Owner.Href, text));
                        }
                        if (term is "uav:dataTypeId" or "uav:fieldDataTypeId" or "uav:binaryEncodingId" or
                            "uav:xmlEncodingId" or "uav:jsonEncodingId" or "uav:defaultEncodingId")
                        {
                            return JsonValue.Create(WotNodeSetConverter.NormalizeExpandedNodeId(text));
                        }
                        if (term == "uav:dataTypeSubtypeOf")
                        {
                            return JsonValue.Create(WotPortableIdentity.IsPortableNodeId(text)
                                ? WotNodeSetConverter.NormalizeExpandedNodeId(text)
                                : ExpandGraphIdentity(entry.Owner, context, text));
                        }
                    }
                    return CloneNode(value);
                }
            }

            private JsonElement OriginalElement(ReferenceCarriage carriage)
            {
                if (WotDocument.TryEvaluatePointer(
                    carriage.Owner.Document.RootElement, carriage.SourcePointer, out JsonElement original))
                {
                    return original;
                }
                DataTypeError(
                    "A carried DataType annotation lost its original semantic owner.", carriage.SourcePointer);
                return default;
            }

            private static string ExpandGraphIdentity(ReferenceOwner owner, JsonElement context, string identity)
            {
                int colon = identity.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0 && owner.Document.TryGetContextPrefix(identity[..colon], out string prefix, context))
                {
                    return prefix + identity[(colon + 1)..];
                }
                return ResolveHref(owner.Href, identity);
            }

            private static string QualifiedTypeName(WotBrowsePathElement name)
            {
                return (name.NamespaceUri ?? WotVocabulary.OpcUaNamespace) + "\0" + name.Name;
            }

            private static string? ElementString(JsonElement element, string term)
            {
                return element.ValueKind == JsonValueKind.Object &&
                    element.TryGetProperty(term, out JsonElement value) &&
                    value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            }

            private void DataTypeError(string message, string pointer)
            {
                AddError(m_diagnostics, WotDiagnosticCode.DataTypeDefinitionInvalid, message, pointer);
            }

            private sealed class DataTypeIndex
            {
                public Dictionary<string, DataTypeEntry> GraphIds { get; } = new(StringComparer.Ordinal);
                public Dictionary<string, DataTypeEntry> Locations { get; } = new(StringComparer.Ordinal);
                public Dictionary<string, DataTypeEntry> NativeIds { get; } = new(StringComparer.Ordinal);
                public Dictionary<string, DataTypeEntry> Names { get; } = new(StringComparer.Ordinal);
            }

            private sealed record DataTypeEntry(
                ReferenceOwner Owner, JsonElement Definition, string Pointer, string GraphId, string NativeId,
                string Name);

            private readonly Dictionary<WotDocument, DataTypeIndex> m_dataTypeIndexes = [];
            private readonly Dictionary<string, DataTypeEntry> m_carriedDataTypes = new(StringComparer.Ordinal);
            private readonly Dictionary<string, DataTypeEntry> m_carriedNativeTypes = new(StringComparer.Ordinal);
            private readonly HashSet<(WotDocument Document, string Pointer)> m_checkedDataTypeOccurrences = [];
            private readonly JsonArray m_dataTypeDefinitions = [];
            private int m_dataTypeCount;
            private const string DataTypesTerm = "uav:dataTypeDefinitions";

            private static readonly string[] s_dataTypeReferences =
                ["uav:dataTypeDefinition", "uav:fieldDataTypeDefinition", "uav:dataTypeSubtypeOf"];
        }
    }
}
