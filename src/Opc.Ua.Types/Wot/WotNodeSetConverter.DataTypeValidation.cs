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
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    public static partial class WotNodeSetConverter
    {
        private static void InitializeDataTypeValidation(
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            foreach (KeyValuePair<string, JsonElement> entry in context.Definitions)
            {
                if (context.Identities.TryGetValue(entry.Key, out string? identity))
                {
                    context.ValidationTypes.Add(identity, new DataTypeValidationNode(
                        context.Owners[entry.Key], entry.Value, identity, false));
                }
            }
            foreach (KeyValuePair<string, List<(WotDocument Document, JsonElement Schema)>> entry in
                context.InferredSchemas)
            {
                (WotDocument document, JsonElement schema) = entry.Value[0];
                if (!context.ValidationTypes.TryAdd(entry.Key, new DataTypeValidationNode(
                    document, schema, entry.Key, true)))
                {
                    ReportDataTypeContractError(context.ValidationTypes[entry.Key], "uav:dataTypeId",
                        $"The inferred DataType also claims the existing identity '{entry.Key}'.", diagnostics);
                }
            }
            foreach (DataTypeValidationNode node in context.ValidationTypes.Values)
            {
                if (node.Source.TryGetProperty("uav:dataTypeSubtypeOf", out JsonElement declared))
                {
                    string? resolved = ResolveDataTypeReference(
                        node.Document, declared, context, nodeSet, diagnostics, node.Source);
                    if (resolved is null)
                    {
                        ReportDataTypeContractError(node, "uav:dataTypeSubtypeOf",
                            "The declared subtype reference could not be resolved.", diagnostics);
                    }
                    else
                    {
                        node.BaseIdentity = NormalizeDataTypeValidationIdentity(resolved, nodeSet);
                    }
                }
                else
                {
                    node.BaseIdentity = node.Kind switch
                    {
                        ValidationDataTypeKind.Structure => WotVocabulary.Structure,
                        ValidationDataTypeKind.Union => WotVocabulary.Union,
                        ValidationDataTypeKind.Enumeration => WotVocabulary.Enumeration,
                        _ => null
                    };
                }
                if (node.Kind == ValidationDataTypeKind.Union &&
                    (GetElementBool(node.Source, "uav:isAbstract") ||
                        GetElementBool(ReadDataTypeElementSchema(node.Source), "uav:isAbstract")))
                {
                    ReportDataTypeContractError(node, "uav:isAbstract",
                        "A custom Union subtype shall be concrete, not abstract.", diagnostics,
                        node.Source.TryGetProperty("uav:isAbstract", out _)
                            ? node.Source
                            : ReadDataTypeElementSchema(node.Source));
                }
            }

            var completed = new HashSet<string>(StringComparer.Ordinal);
            foreach (DataTypeValidationNode start in context.ValidationTypes.Values)
            {
                var path = new List<DataTypeValidationNode>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                DataTypeValidationNode? current = start;
                while (current is not null && !completed.Contains(current.Identity))
                {
                    if (!seen.Add(current.Identity))
                    {
                        ReportDataTypeContractError(current, "uav:dataTypeSubtypeOf",
                            $"The DataType '{current.Name}' is its own ancestor; the resolved subtype graph " +
                            "contains a cycle and shall be acyclic.", diagnostics);
                        foreach (DataTypeValidationNode member in path)
                        {
                            member.Invalid = true;
                        }
                        break;
                    }
                    path.Add(current);
                    current = current.BaseIdentity is { } parent &&
                        context.ValidationTypes.TryGetValue(parent, out DataTypeValidationNode? next)
                        ? next
                        : null;
                }
                foreach (DataTypeValidationNode member in path)
                {
                    completed.Add(member.Identity);
                }
            }
            foreach (DataTypeValidationNode node in context.ValidationTypes.Values)
            {
                if (node.Invalid || node.BaseIdentity is not { } parent)
                {
                    continue;
                }
                if (context.ValidationTypes.TryGetValue(parent, out DataTypeValidationNode? baseType))
                {
                    bool compatible = node.Kind switch
                    {
                        ValidationDataTypeKind.OptionSet => baseType.Kind == ValidationDataTypeKind.Simple,
                        _ => node.Kind == baseType.Kind
                    };
                    if (!compatible)
                    {
                        ReportDataTypeContractError(node, "uav:dataTypeSubtypeOf",
                            $"The {node.Kind} '{node.Name}' subtypes the {baseType.Kind} '{baseType.Name}'. " +
                            "A subtype shall remain within its own kind and family; an OptionSet may use " +
                            "SimpleDataType aliases " +
                            "but cannot inherit another OptionSet.", diagnostics);
                    }
                    continue;
                }
                bool terminalCompatible = node.Kind switch
                {
                    ValidationDataTypeKind.Structure => parent == WotVocabulary.Structure,
                    ValidationDataTypeKind.Union => parent == WotVocabulary.Union,
                    ValidationDataTypeKind.Enumeration => parent == WotVocabulary.Enumeration,
                    ValidationDataTypeKind.Simple => IsConcreteScalarTerminal(parent),
                    ValidationDataTypeKind.OptionSet => true,
                    _ => false
                };
                if (!terminalCompatible)
                {
                    ReportDataTypeContractError(node, "uav:dataTypeSubtypeOf",
                        $"The {node.Kind} '{node.Name}' has the unresolved or kind-incompatible terminal " +
                        $"'{parent}'. SimpleDataTypes require a concrete scalar built-in, not " +
                        "Number, Integer, UInteger, BaseDataType, Structure, Union or Enumeration.", diagnostics);
                }
            }
            foreach (DataTypeValidationNode node in context.ValidationTypes.Values)
            {
                if (!node.Invalid &&
                    node.Kind == ValidationDataTypeKind.Simple &&
                    node.BaseIdentity is { } parent &&
                    !TryGetSimpleTerminal(parent, context, out string terminal))
                {
                    ReportDataTypeContractError(node, "uav:dataTypeSubtypeOf",
                        $"The SimpleDataType '{node.Name}' does not terminate at a concrete scalar built-in " +
                        $"through SimpleDataType aliases; its terminal is '{terminal}'.", diagnostics);
                }
            }
        }

        private static string NormalizeDataTypeValidationIdentity(string identity, UANodeSet nodeSet)
        {
            return NormalizeExpandedNodeId(ToPortableNodeId(identity, nodeSet.NamespaceUris) ?? identity);
        }

        private static BuiltInType GetValidationBuiltInType(string identity)
        {
            return NodeId.TryParse(NormalizeExpandedNodeId(identity), out NodeId nodeId)
                ? TypeInfo.GetBuiltInType(nodeId)
                : BuiltInType.Null;
        }

        private static bool IsConcreteScalarTerminal(string identity)
        {
            BuiltInType builtIn = GetValidationBuiltInType(identity);
            return builtIn is not (BuiltInType.Null or BuiltInType.ExtensionObject or
                BuiltInType.Variant or BuiltInType.Enumeration);
        }

        private static bool TryGetSimpleTerminal(
            string identity,
            DataTypeDefinitionContext context,
            out string terminal)
        {
            terminal = identity;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool valid;
            if (context.ScalarTerminals.TryGetValue(identity, out (bool Valid, string Terminal) cached))
            {
                terminal = cached.Terminal;
                return cached.Valid;
            }
            while (context.ValidationTypes.TryGetValue(terminal, out DataTypeValidationNode? node))
            {
                if (context.ScalarTerminals.TryGetValue(terminal, out cached))
                {
                    terminal = cached.Terminal;
                    valid = cached.Valid;
                    Cache(valid, terminal);
                    return valid;
                }
                if (!seen.Add(terminal) ||
                    node.Invalid ||
                    node.Kind != ValidationDataTypeKind.Simple ||
                    node.BaseIdentity is not { } parent)
                {
                    valid = false;
                    Cache(valid, terminal);
                    return false;
                }
                terminal = parent;
            }
            valid = IsConcreteScalarTerminal(terminal);
            Cache(valid, terminal);
            return valid;

            void Cache(bool resolved, string last)
            {
                foreach (string visited in seen)
                {
                    context.ScalarTerminals[visited] = (resolved, last);
                }
            }
        }

        private static void ValidateInheritedFieldPrefixes(
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            foreach (DataTypeValidationNode node in context.ValidationTypes.Values)
            {
                if (node.Invalid ||
                    node.BaseIdentity is not { } parent ||
                    !context.ValidationTypes.TryGetValue(parent, out DataTypeValidationNode? baseType) ||
                    baseType.Invalid ||
                    node.Kind != baseType.Kind ||
                    node.Kind is ValidationDataTypeKind.Simple or ValidationDataTypeKind.OptionSet)
                {
                    continue;
                }
                ArrayOf<DataTypeField> inherited = GetValidationFields(baseType, context, nodeSet, diagnostics);
                ArrayOf<DataTypeField> declared = GetValidationFields(node, context, nodeSet, diagnostics);
                if (declared.Count < inherited.Count)
                {
                    ReportDataTypeContractError(node, "uav:fields",
                        $"'{node.Name}' states {declared.Count} field(s) but inherits {inherited.Count}; " +
                        "the inherited prefix shall not omit fields, because dropping one shifts " +
                        "every field after it.",
                        diagnostics);
                    continue;
                }
                for (int index = 0; index < inherited.Count; index++)
                {
                    string? difference = InheritedFieldDifference(inherited[index], declared[index], nodeSet);
                    if (difference is not null)
                    {
                        string term = node.Kind == ValidationDataTypeKind.Enumeration
                            ? "uav:enumFields"
                            : "uav:fields";
                        ReportDataTypeContractError(node,
                            term + "/" + index.ToString(CultureInfo.InvariantCulture),
                            $"'{node.Name}' states '{declared[index].Name}' where it inherits " +
                            $"'{inherited[index].Name}' and changes " +
                            $"'{difference}'; inherited fields shall be stated first and unchanged.", diagnostics);
                        break;
                    }
                }
            }
        }

        private static ArrayOf<DataTypeField> GetValidationFields(
            DataTypeValidationNode node,
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (!node.FieldsLoaded)
            {
                node.FieldsLoaded = true;
                Export.DataTypeDefinition? definition;
                if (node.Inferred)
                {
                    JsonElement schema = ReadDataTypeElementSchema(node.Source);
                    definition = node.Kind == ValidationDataTypeKind.Enumeration &&
                        schema.TryGetProperty("oneOf", out JsonElement branches)
                        ? BuildInferredEnumeration(
                            node.Document, schema, node.Name, branches, diagnostics)
                        : node.Kind is ValidationDataTypeKind.Structure or ValidationDataTypeKind.Union
                            ? BuildInferredStructure(
                                node.Document, schema, node.Name, node.Name, context, nodeSet, diagnostics,
                                union: node.Kind == ValidationDataTypeKind.Union)
                            : null;
                }
                else
                {
                    definition = BuildDataTypeDefinition(
                        node.Document, node.Source,
                        GetElementString(node.Source, "@type") ?? "uav:StructureDefinition",
                        node.Name, context, nodeSet, diagnostics);
                }
                node.Fields = new ArrayOf<DataTypeField>(definition?.Field ?? []);
            }
            return node.Fields;
        }

        private static string? InheritedFieldDifference(
            DataTypeField inherited,
            DataTypeField declared,
            UANodeSet nodeSet)
        {
            if (inherited.Name != declared.Name)
            {
                return "Name";
            }
            if (NormalizeDataTypeValidationIdentity(inherited.DataType ?? WotVocabulary.BaseDataType, nodeSet) !=
                NormalizeDataTypeValidationIdentity(declared.DataType ?? WotVocabulary.BaseDataType, nodeSet))
            {
                return "DataType";
            }
            if (inherited.ValueRank != declared.ValueRank)
            {
                return "ValueRank";
            }
            if ((inherited.ArrayDimensions ?? string.Empty) != (declared.ArrayDimensions ?? string.Empty))
            {
                return "ArrayDimensions";
            }
            if (inherited.IsOptional != declared.IsOptional)
            {
                return "IsOptional";
            }
            if (inherited.AllowSubTypes != declared.AllowSubTypes)
            {
                return "AllowSubTypes";
            }
            if (inherited.MaxStringLength != declared.MaxStringLength)
            {
                return "MaxStringLength";
            }
            if (inherited.Value != declared.Value)
            {
                return "Value";
            }
            if (inherited.SymbolicName != declared.SymbolicName)
            {
                return "SymbolicName";
            }
            if (!SameFieldText(inherited.DisplayName, declared.DisplayName))
            {
                return "DisplayName";
            }
            return SameFieldText(inherited.Description, declared.Description) ? null : "Description";
        }

        private static bool SameFieldText(Export.LocalizedText[]? first, Export.LocalizedText[]? second)
        {
            if ((first?.Length ?? 0) != (second?.Length ?? 0))
            {
                return false;
            }
            var counts = new Dictionary<(string Locale, string Value), int>();
            foreach (Export.LocalizedText text in first ?? [])
            {
                (string, string) key = ((text.Locale ?? string.Empty).ToUpperInvariant(), text.Value ?? string.Empty);
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;
            }
            foreach (Export.LocalizedText text in second ?? [])
            {
                (string, string) key = ((text.Locale ?? string.Empty).ToUpperInvariant(), text.Value ?? string.Empty);
                if (!counts.TryGetValue(key, out int count) || count == 0)
                {
                    return false;
                }
                counts[key] = count - 1;
            }
            return true;
        }

        private static void ReportDataTypeContractError(
            DataTypeValidationNode node,
            string term,
            string message,
            List<WotDiagnostic> diagnostics,
            JsonElement source = default)
        {
            node.Invalid = true;
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.DataTypeDefinitionInvalid,
                message,
                DataTypeValidationLocation(
                    node.Document, source.ValueKind == JsonValueKind.Undefined ? node.Source : source,
                    term, node.Name, node.Identity)));
        }

        private static WotLocation DataTypeValidationLocation(
            WotDocument document,
            JsonElement source,
            string term,
            string reference,
            string? identity = null)
        {
            string? pointer = null;
            var pending = new Stack<(JsonElement Element, string Pointer)>();
            pending.Push((document.RootElement, string.Empty));
            while (pending.Count > 0)
            {
                (JsonElement element, string path) = pending.Pop();
                if (element.Equals(source))
                {
                    pointer = term.Length == 0 ? path : path + "/" + term;
                    break;
                }
                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty member in element.EnumerateObject())
                    {
                        pending.Push((member.Value, path + "/" + EscapeJsonPointerToken(member.Name)));
                    }
                }
                else if (element.ValueKind == JsonValueKind.Array)
                {
                    int index = 0;
                    foreach (JsonElement entry in element.EnumerateArray())
                    {
                        pending.Push((entry, path + "/" + index.ToString(CultureInfo.InvariantCulture)));
                        index++;
                    }
                }
            }
            return new WotLocation(jsonPointer: pointer, nodeId: identity, reference: reference);
        }

        private static void ValidateAuthoritativeDataSchemas(
            WotDocument document,
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            int maxDepth)
        {
            var validated = new HashSet<(JsonElement Schema, string Identity, int Rank, bool AuthoritativeRank)>();
            foreach (JsonElement schema in ReadDataSchemaOccurrences(document))
            {
                string? identity = GetElementString(schema, "uav:mapToType");
                if (identity is null && schema.TryGetProperty("uav:dataTypeDefinition", out JsonElement definition))
                {
                    identity = ResolveDataTypeReference(document, definition, context, nodeSet, diagnostics, schema);
                }
                identity ??= GetElementString(schema, "uav:dataTypeId");
                if (identity is null)
                {
                    context.SchemaIdentities.TryGetValue(schema, out identity);
                }
                if (identity is null)
                {
                    continue;
                }
                ValidateValueSchema(document, schema, NormalizeDataTypeValidationIdentity(identity, nodeSet),
                    ReadValueRank(schema), context, nodeSet, diagnostics, validated, 0, maxDepth);
            }
            foreach (DataTypeValidationNode node in context.ValidationTypes.Values)
            {
                if (node.Invalid ||
                    node.Inferred ||
                    !node.Source.TryGetProperty("uav:fields", out JsonElement fields) ||
                    fields.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                foreach (JsonElement field in fields.EnumerateArray())
                {
                    if (field.ValueKind != JsonValueKind.Object ||
                        (!field.TryGetProperty("type", out _) &&
                            !field.TryGetProperty("properties", out _) &&
                            !field.TryGetProperty("items", out _) &&
                            !field.TryGetProperty("oneOf", out _)))
                    {
                        continue;
                    }
                    string identity = ResolveFieldDataType(node.Document, field, context, nodeSet, diagnostics);
                    ValidateValueSchema(node.Document, field, NormalizeDataTypeValidationIdentity(identity, nodeSet),
                        GetElementInt32(field, "uav:valueRank") ?? -1,
                        context, nodeSet, diagnostics, validated, 0, maxDepth, authoritativeRank: true);
                }
            }
        }

        private static void ValidateValueSchema(
            WotDocument document,
            JsonElement schema,
            string identity,
            int rank,
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            HashSet<(JsonElement Schema, string Identity, int Rank, bool AuthoritativeRank)> validated,
            int depth,
            int maxDepth,
            bool authoritativeRank = false)
        {
            if (schema.ValueKind != JsonValueKind.Object ||
                !validated.Add((schema, identity, rank, authoritativeRank)))
            {
                return;
            }
            if (depth > maxDepth)
            {
                Report("type", $"Semantic validation exceeds its depth limit of {maxDepth}.");
                return;
            }
            context.ValidationTypes.TryGetValue(identity, out DataTypeValidationNode? definition);
            if (definition?.Invalid == true)
            {
                return;
            }
            if (authoritativeRank)
            {
                int arrayDepth = 0;
                JsonElement supplied = schema;
                while (GetElementString(supplied, "type") == "array")
                {
                    arrayDepth++;
                    if (!supplied.TryGetProperty("items", out JsonElement items) ||
                        items.ValueKind != JsonValueKind.Object)
                    {
                        break;
                    }
                    supplied = items;
                }
                if (arrayDepth > 0 &&
                    (rank == -1 ||
                        (rank == -3 && arrayDepth > 1) ||
                        (rank > 0 && arrayDepth > rank)))
                {
                    Report("type",
                        $"The explicit array shape has {arrayDepth} dimension(s), which contradicts the " +
                        $"authoritative field ValueRank {rank}.");
                    return;
                }
            }
            bool definingInferredSchema = definition?.Inferred == true &&
                definition.Source.Equals(schema) &&
                definition.Kind != ValidationDataTypeKind.Simple;
            if (definingInferredSchema && definition!.Kind != ValidationDataTypeKind.Union)
            {
                return;
            }
            string effectiveIdentity = identity;
            if (definition?.Kind == ValidationDataTypeKind.Simple &&
                !TryGetSimpleTerminal(identity, context, out effectiveIdentity))
            {
                return;
            }
            JsonElement elementSchema = ReadDataTypeElementSchema(schema);
            string? jsonType = definition?.Kind switch
            {
                ValidationDataTypeKind.Structure or ValidationDataTypeKind.Union => "object",
                ValidationDataTypeKind.Enumeration or ValidationDataTypeKind.OptionSet => "integer",
                _ => ValidationJsonType(effectiveIdentity, elementSchema)
            };
            var canonical = new JsonObject();
            if (jsonType is not null)
            {
                canonical["type"] = jsonType;
                BuiltInType builtIn = GetValidationBuiltInType(effectiveIdentity);
                if (TypeInfo.IsEncodingNullableType(builtIn) &&
                    builtIn != BuiltInType.Null &&
                    elementSchema.TryGetProperty("type", out JsonElement types) &&
                    types.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement type in types.EnumerateArray())
                    {
                        if (type.ValueKind == JsonValueKind.String && type.GetString() == "null")
                        {
                            canonical["type"] = new JsonArray(JsonValue.Create(jsonType), JsonValue.Create("null"));
                            break;
                        }
                    }
                }
            }
            ArrayOf<DataTypeField> fields = [];
            if (definition?.Kind is ValidationDataTypeKind.Structure or ValidationDataTypeKind.Union)
            {
                fields = GetValidationFields(definition, context, nodeSet, diagnostics);
                var properties = new JsonObject();
                var required = new JsonArray();
                var order = new JsonArray();
                foreach (DataTypeField field in fields)
                {
                    var fieldSchema = new JsonObject
                    {
                        ["uav:mapToType"] = NormalizeDataTypeValidationIdentity(
                            field.DataType ?? WotVocabulary.BaseDataType, nodeSet),
                        ["uav:valueRank"] = field.ValueRank
                    };
                    if (field.MaxStringLength != 0)
                    {
                        fieldSchema["maxLength"] = field.MaxStringLength;
                    }
                    if (!string.IsNullOrEmpty(field.ArrayDimensions))
                    {
                        var dimensions = new JsonArray();
                        foreach (string dimension in field.ArrayDimensions.Split(','))
                        {
                            dimensions.Add(uint.Parse(dimension, CultureInfo.InvariantCulture));
                        }
                        fieldSchema["uav:arrayDimensions"] = dimensions;
                    }
                    properties[field.Name!] = fieldSchema;
                    order.Add(field.Name);
                    if (!field.IsOptional && definition.Kind != ValidationDataTypeKind.Union)
                    {
                        required.Add(field.Name);
                    }
                }
                canonical["properties"] = properties;
                canonical["required"] = required;
                canonical["uav:fieldOrder"] = order;
                canonical["additionalProperties"] = false;
                if (definition.Kind == ValidationDataTypeKind.Union)
                {
                    canonical["minProperties"] = 0;
                    canonical["maxProperties"] = 1;
                    ValidateUnionValues();
                }
            }
            else if (definition?.Kind == ValidationDataTypeKind.Enumeration)
            {
                fields = GetValidationFields(definition, context, nodeSet, diagnostics);
                var values = new JsonArray();
                foreach (DataTypeField field in fields)
                {
                    values.Add(field.Value);
                }
                canonical["enum"] = values;
            }
            if (definingInferredSchema)
            {
                return;
            }
            int dimensionsToWrap = 0;
            JsonElement arraySchema = schema;
            bool unspecifiedItems = false;
            while (GetElementString(arraySchema, "type") == "array")
            {
                dimensionsToWrap++;
                if (!arraySchema.TryGetProperty("items", out JsonElement items) ||
                    items.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    unspecifiedItems = true;
                    break;
                }
                if (items.ValueKind != JsonValueKind.Object)
                {
                    Report("items", "An array items contract must be a DataSchema or a Boolean schema.");
                    return;
                }
                arraySchema = items;
            }
            if (dimensionsToWrap > maxDepth)
            {
                Report("uav:valueRank", $"ValueRank {rank} exceeds the schema depth limit of {maxDepth}.");
                return;
            }
            if (unspecifiedItems)
            {
                canonical = new JsonObject { ["type"] = "array" };
                dimensionsToWrap--;
            }
            for (int dimension = 0; dimension < dimensionsToWrap; dimension++)
            {
                canonical = new JsonObject { ["type"] = "array", ["items"] = canonical };
            }
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                canonical.WriteTo(writer);
            }
            using var expected = JsonDocument.Parse(
                stream.ToArray(), new JsonDocumentOptions { MaxDepth = Math.Max(maxDepth, 1) });
            string? difference = WotExternalSchemaResolver.FindIncompatibility(
                schema, expected.RootElement, identity, out string mismatchPointer, maxDepth);
            if (difference is not null)
            {
                Report(mismatchPointer.TrimStart('/'),
                    $"The supplied value schema contradicts authoritative DataType '{identity}'. {difference}");
            }
            if (definition?.Kind is ValidationDataTypeKind.Structure or ValidationDataTypeKind.Union &&
                elementSchema.TryGetProperty("properties", out JsonElement declaredFields) &&
                declaredFields.ValueKind == JsonValueKind.Object)
            {
                foreach (DataTypeField field in fields)
                {
                    if (declaredFields.TryGetProperty(field.Name!, out JsonElement declared))
                    {
                        ValidateValueSchema(document, declared,
                            NormalizeDataTypeValidationIdentity(field.DataType ?? WotVocabulary.BaseDataType, nodeSet),
                            field.ValueRank, context, nodeSet, diagnostics, validated, depth + 1, maxDepth,
                            authoritativeRank: true);
                    }
                }
            }

            void Report(string term, string message)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ValidationError,
                    message,
                    DataTypeValidationLocation(document, schema, term, identity, identity)));
            }

            void ValidateUnionValues()
            {
                foreach (string term in s_unionValueTerms)
                {
                    if (!schema.TryGetProperty(term, out JsonElement value))
                    {
                        continue;
                    }
                    var pending = new Stack<(JsonElement Value, string Pointer)>();
                    pending.Push((value, term));
                    while (pending.Count > 0)
                    {
                        (JsonElement item, string pointer) = pending.Pop();
                        if (pointer == term && rank >= 0 && item.ValueKind != JsonValueKind.Array)
                        {
                            Report(pointer, $"A Union value with ValueRank {rank} requires an array.");
                            continue;
                        }
                        if (item.ValueKind == JsonValueKind.Array && rank != -1)
                        {
                            int index = 0;
                            foreach (JsonElement element in item.EnumerateArray())
                            {
                                pending.Push((element, pointer + "/" + index.ToString(CultureInfo.InvariantCulture)));
                                index++;
                            }
                            continue;
                        }
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            Report(pointer, "A Union with no selected field is {}, not JSON null or a scalar.");
                            continue;
                        }
                        int selected = 0;
                        foreach (JsonProperty member in item.EnumerateObject())
                        {
                            selected++;
                            DataTypeField? field = null;
                            foreach (DataTypeField candidate in fields)
                            {
                                if (candidate.Name == member.Name)
                                {
                                    field = candidate;
                                    break;
                                }
                            }
                            if (field is null)
                            {
                                Report(pointer, $"The Union selects the undeclared field '{member.Name}'.");
                            }
                            else if (member.Value.ValueKind == JsonValueKind.Null)
                            {
                                string selectedType = NormalizeDataTypeValidationIdentity(
                                    field.DataType ?? WotVocabulary.BaseDataType, nodeSet);
                                if (TryGetSimpleTerminal(selectedType, context, out string terminal))
                                {
                                    selectedType = terminal;
                                }
                                BuiltInType builtIn = GetValidationBuiltInType(selectedType);
                                if (builtIn != BuiltInType.Null && !TypeInfo.IsEncodingNullableType(builtIn))
                                {
                                    Report(pointer, $"The selected Union field '{member.Name}' is not nullable.");
                                }
                            }
                        }
                        if (selected > 1)
                        {
                            Report(pointer, "A Union value cannot select more than one field.");
                        }
                    }
                }
            }
        }

        private static string? ValidationJsonType(string identity, JsonElement schema)
        {
            if (identity == WotVocabulary.Number)
            {
                return GetElementString(schema, "type") == "integer" ? "integer" : "number";
            }
            if (identity is WotVocabulary.Integer or "i=28")
            {
                return "integer";
            }
            if (identity is WotVocabulary.Structure or WotVocabulary.Union)
            {
                return "object";
            }
            return GetValidationBuiltInType(identity) switch
            {
                BuiltInType.Boolean => "boolean",
                >= BuiltInType.SByte and <= BuiltInType.UInt64 => "integer",
                BuiltInType.Float or BuiltInType.Double => "number",
                BuiltInType.String or BuiltInType.DateTime or BuiltInType.Guid or BuiltInType.ByteString => "string",
                BuiltInType.Enumeration => "integer",
                _ => null
            };
        }

        private enum ValidationDataTypeKind
        {
            Simple,
            Enumeration,
            OptionSet,
            Structure,
            Union
        }

        private static readonly string[] s_unionValueTerms = ["const", "default"];

        private sealed class DataTypeValidationNode
        {
            public DataTypeValidationNode(WotDocument document, JsonElement source, string identity, bool inferred)
            {
                Document = document;
                Source = source;
                Identity = identity;
                Inferred = inferred;
                Name = GetElementString(source, "uav:dataTypeName") ?? identity;
                JsonElement schema = inferred ? ReadDataTypeElementSchema(source) : source;
                bool enumeration = inferred
                    ? schema.TryGetProperty("oneOf", out JsonElement branches) && IsEnumerationBranches(branches)
                    : IsEnumerationKind(GetElementString(source, "@type") ?? string.Empty);
                Kind = enumeration
                    ? GetElementBool(source, "uav:isOptionSet")
                        ? ValidationDataTypeKind.OptionSet
                        : ValidationDataTypeKind.Enumeration
                    : (inferred && GetElementString(schema, "type") != "object") ||
                        (!inferred && GetElementString(source, "@type") == "uav:SimpleDataType")
                        ? ValidationDataTypeKind.Simple
                        : IsUnionStructure(source) || IsUnionStructure(schema)
                            ? ValidationDataTypeKind.Union
                            : ValidationDataTypeKind.Structure;
            }

            public WotDocument Document { get; }

            public JsonElement Source { get; }

            public string Identity { get; }

            public string Name { get; }

            public bool Inferred { get; }

            public ValidationDataTypeKind Kind { get; }

            public string? BaseIdentity { get; set; }

            public bool Invalid { get; set; }

            public bool FieldsLoaded { get; set; }

            public ArrayOf<DataTypeField> Fields { get; set; }
        }
    }
}
