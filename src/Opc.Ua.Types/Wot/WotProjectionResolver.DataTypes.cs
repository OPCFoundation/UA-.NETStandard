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
                    if (!ValidateDefinitionMembers(definition, pointer))
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
                    if (!index.Names.TryAdd(entry.Name, entry))
                    {
                        index.Names[entry.Name] = null;
                    }
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

            private bool ValidateDefinitionMembers(JsonElement value, string pointer)
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    var members = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JsonProperty member in value.EnumerateObject())
                    {
                        string location = pointer + "/" + EscapePointer(member.Name);
                        if (!members.Add(member.Name))
                        {
                            DataTypeError("A complete DataType definition contains a duplicate member.", location);
                            return false;
                        }
                        if (!ValidateDefinitionMembers(member.Value, location))
                        {
                            return false;
                        }
                    }
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    int index = 0;
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        if (!ValidateDefinitionMembers(
                            item, pointer + "/" + index.ToString(CultureInfo.InvariantCulture)))
                        {
                            return false;
                        }
                        index++;
                    }
                }
                return true;
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
                DataTypeEntry? definition;
                if (name)
                {
                    if (!WotPortableIdentity.TryResolveQualifiedName(identity, carriage.Owner.Document,
                        OriginalElement(carriage), out WotBrowsePathElement qualified))
                    {
                        return;
                    }
                    identity = QualifiedTypeName(qualified);
                    if (index.Names.TryGetValue(identity, out definition))
                    {
                        if (definition is null)
                        {
                            definition = term == "uav:dataTypeSubtypeOf"
                                ? null
                                : FindDefinitiveCarriageType(carriage, index);
                            if (definition is null || definition.Name != identity)
                            {
                                DataTypeError("The DataType name is ambiguous without a matching definitive identity.",
                                    carriage.SourcePointer);
                                return;
                            }
                        }
                        CarryDataType(definition);
                    }
                }
                else
                {
                    identity = WotNodeSetConverter.NormalizeExpandedNodeId(identity);
                    if (index.NativeIds.TryGetValue(identity, out definition))
                    {
                        CarryDataType(definition);
                    }
                }
            }

            private DataTypeEntry? FindDefinitiveCarriageType(ReferenceCarriage carriage, DataTypeIndex index)
            {
                if (index.Locations.TryGetValue(carriage.SourcePointer, out DataTypeEntry? located))
                {
                    return located;
                }
                foreach (string term in s_nativeDataTypeReferences)
                {
                    if (ReadString(carriage.Value[term]) is string nativeId &&
                        index.NativeIds.TryGetValue(
                            WotNodeSetConverter.NormalizeExpandedNodeId(nativeId), out DataTypeEntry? definition))
                    {
                        return definition;
                    }
                }
                JsonElement original = OriginalElement(carriage);
                foreach (string term in s_dataTypeReferences)
                {
                    if (original.ValueKind == JsonValueKind.Object &&
                        original.TryGetProperty(term, out JsonElement reference) &&
                        ElementString(reference, "@id") is string graphId)
                    {
                        DataTypeEntry? definition = FindGraphDefinition(
                            carriage.Owner, reference, graphId, carriage.SourcePointer, out _);
                        if (definition is not null)
                        {
                            return definition;
                        }
                    }
                }
                return null;
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
                else
                {
                    if (!WotNodeSetConverter.ValidateStandardDataTypeReference(owner.Document, original, m_diagnostics))
                    {
                        return;
                    }
                    if (name is not null && index.Names.ContainsKey(name))
                    {
                        DataTypeError("The base DataType name is ambiguous without a definitive identity.", pointer);
                    }
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
                        CloneOwnedObject(definition.Owner.Document, definition.Definition, definition.Owner.Href),
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
                JsonObject value = CloneOwnedObject(
                    definition.Owner.Document, definition.Definition, definition.Owner.Href);
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
                var result = (JsonObject)ResolveFacts(
                    entry.Definition, entry.Definition, null, string.Empty, graphReference: true)!;
                result["@id"] = entry.GraphId;
                result["uav:dataTypeId"] = entry.NativeId;
                return new JsonObject
                {
                    ["facts"] = result,
                    ["scopes"] = scopes
                };

                JsonNode? ResolveFacts(
                    JsonElement value, JsonElement context, string? term, string pointer,
                    bool indexMap = false, bool graphReference = false)
                {
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        var resolved = new JsonObject();
                        var languages = new JsonObject();
                        bool unknownTerms = false;
                        foreach (JsonProperty member in value.EnumerateObject())
                        {
                            string location = pointer + "/" + EscapePointer(member.Name);
                            if (indexMap)
                            {
                                resolved[member.Name] = ResolveFacts(member.Value, value, null, location);
                                continue;
                            }
                            if (member.Name == "@context")
                            {
                                continue;
                            }
                            bool localized = member.Name is "title" or "description" or "uav:fieldDescription" or
                                "uav:enumDisplayName" or "uav:enumDescription";
                            resolved[member.Name] = WotDocument.IsSemanticBoundary(member.Name) ||
                                WotNodeSetConverter.IsLiteralSchemaMember(member.Name) ||
                                localized ||
                                member.Name is "titles" or "descriptions"
                                ? CloneNode(member.Value)
                                : ResolveFacts(member.Value, value, member.Name, location,
                                    WotNodeSetConverter.IsSchemaDeclarationMap(member.Name),
                                    member.Name == "@id" ? graphReference :
                                        Array.IndexOf(s_dataTypeReferences, member.Name) >= 0);
                            if (localized)
                            {
                                languages[member.Name] = JsonValue.Create(
                                    WotNodeSetConverter.GetDeclaredLocale(entry.Owner.Document, value, member.Name));
                            }
                            unknownTerms |= !WotDocument.IsSemanticBoundary(member.Name) &&
                                !IsKnownDefinitionFact(member.Name);
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
                                pointer + "/" + index.ToString(CultureInfo.InvariantCulture),
                                graphReference: graphReference));
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
                        if (term == "@id" && graphReference)
                        {
                            FindGraphDefinition(
                                entry.Owner, context, text, entry.Pointer + pointer, out string graphId);
                            return JsonValue.Create(graphId);
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

            private static bool IsKnownDefinitionFact(string term)
            {
                return term is "@id" or "@type" or "title" or "titles" or "description" or "descriptions" or
                    "uav:dataTypeName" or "uav:dataTypeId" or "uav:dataTypeSubtypeOf" or "uav:dataTypeDefinition" or
                    "uav:fieldDataTypeDefinition" or "uav:fieldDataTypeId" or "uav:fieldDataTypeName" or
                    "uav:isAbstract" or "uav:structureType" or "uav:fields" or "uav:fieldName" or
                    "uav:fieldDescription" or "uav:enumFields" or "uav:enumName" or "uav:enumValue" or
                    "uav:enumDisplayName" or "uav:enumDescription" or "uav:isOptionSet" or "uav:isOptional" or
                    "uav:allowSubtypes" or "uav:maxStringLength" or "uav:valueRank" or "uav:arrayDimensions" or
                    "uav:fieldOrder" or "uav:hasDefaultEncoding" or "uav:defaultEncodings" or
                    "uav:defaultEncodingId" or "uav:binaryEncodingId" or "uav:xmlEncodingId" or "uav:jsonEncodingId" or
                    "uav:externalSchema" or "$ref" or "tm:ref" or "type" or "format" or "contentEncoding" or
                    "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum" or "multipleOf" or
                    "minLength" or "maxLength" or "pattern" or "items" or "minItems" or "maxItems" or "uniqueItems" or
                    "properties" or "required" or "additionalProperties" or "minProperties" or "maxProperties" or
                    "oneOf" or "anyOf" or "allOf" or "not" or "if" or "then" or "else" or
                    "const" or "default" or "enum" or "examples" or "readOnly" or "writeOnly";
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
                public Dictionary<string, DataTypeEntry?> Names { get; } = new(StringComparer.Ordinal);
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

            private static readonly string[] s_nativeDataTypeReferences = ["uav:dataTypeId", "uav:fieldDataTypeId"];
        }
    }
}
