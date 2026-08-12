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
using System.Text.Json;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Materializes the readable DataType definitions of WoT Binding §6.11 into
    /// DataType Nodes.
    /// </summary>
    /// <remarks>
    /// §6.11 exists so that a Structure, a Union, an Enumeration or an OptionSet
    /// can be stated in the readable vocabulary rather than smuggled through a
    /// native <c>uav:nodes</c> projection. §6.11.8 makes that a contract: a fact
    /// it covers shall be emitted readably and shall not be the reason a
    /// converter falls back to the projection.
    /// </remarks>
    public static partial class WotNodeSetConverter
    {
        private const string DataTypeIdPrefix = "DataTypes/";
        private const string BinaryEncodingSuffix = "/Default Binary";
        private const string XmlEncodingSuffix = "/Default XML";
        private const string JsonEncodingSuffix = "/Default JSON";

        /// <summary>
        /// Materializes every DataType definition the document carries.
        /// </summary>
        private static void SynthesizeDataTypeDefinitions(
            WotDocument document,
            UANodeSet nodeSet,
            List<UANode> items,
            List<WotDiagnostic> diagnostics)
        {
            if (!document.RootElement.TryGetProperty("uav:dataTypeDefinitions", out JsonElement declared) ||
                declared.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            Dictionary<string, JsonElement> complete = CollectDataTypeDefinitions(
                declared, diagnostics);
            if (complete.Count == 0)
            {
                return;
            }

            // Two passes: the identity of every definition has to be known
            // before any field can point at one, because §6.11.3 lets a field
            // name a sibling definition by its JSON-LD @id and that @id is not
            // itself a NodeId.
            var identities = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonElement> entry in complete)
            {
                string? identity = ResolveDataTypeIdentity(
                    document, entry.Value, nodeSet, diagnostics);
                if (identity is not null)
                {
                    identities[entry.Key] = identity;
                }
            }

            foreach (KeyValuePair<string, JsonElement> entry in complete)
            {
                if (identities.TryGetValue(entry.Key, out string? identity))
                {
                    SynthesizeDataType(
                        document, entry.Value, identity, identities, nodeSet, items,
                        diagnostics);
                }
            }
        }

        /// <summary>
        /// Indexes the definitions by graph node, keeping the one complete
        /// occurrence of each and rejecting a second one.
        /// </summary>
        /// <remarks>
        /// §6.11.1 requires the complete definition to occur in exactly one
        /// place and every other occurrence to be an <c>@id</c>-only reference.
        /// Two occurrences that each contribute properties are invalid rather
        /// than merged, because merging two ordered field lists has no defined
        /// answer.
        /// </remarks>
        private static Dictionary<string, JsonElement> CollectDataTypeDefinitions(
            JsonElement declared,
            List<WotDiagnostic> diagnostics)
        {
            var complete = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonElement definition in declared.EnumerateArray())
            {
                if (definition.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                string? graphId = GetElementString(definition, "@id");
                if (graphId is null)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        "A DataType definition carries no @id, so nothing can " +
                        "reference it and it cannot be checked for duplication."));
                    continue;
                }
                if (IsReferenceOnlyDefinition(definition))
                {
                    continue;
                }
                if (complete.ContainsKey(graphId))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"The DataType definition '{graphId}' is stated completely " +
                        "more than once; §6.11.1 permits exactly one complete " +
                        "occurrence and requires every other to be @id-only.",
                        new WotLocation(reference: graphId)));
                    continue;
                }
                complete.Add(graphId, definition);
            }
            return complete;
        }

        private static bool IsReferenceOnlyDefinition(JsonElement definition)
        {
            foreach (JsonProperty member in definition.EnumerateObject())
            {
                if (!string.Equals(member.Name, "@id", StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Determines the NodeId a definition materializes as.
        /// </summary>
        /// <remarks>
        /// §6.11.1 lets <c>uav:dataTypeId</c> state it. Where it is absent the
        /// identity is derived from <c>uav:dataTypeName</c> alone, so that the
        /// same definition read from a differently ordered or differently
        /// nested document still lands on the same Node.
        /// </remarks>
        private static string? ResolveDataTypeIdentity(
            WotDocument document,
            JsonElement definition,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            string? name = GetElementString(definition, "uav:dataTypeName");
            if (name is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    "A DataType definition carries no uav:dataTypeName, which " +
                    "§6.11.1 makes mandatory."));
                return null;
            }
            string? authored = GetElementString(definition, "uav:dataTypeId");
            if (authored is not null)
            {
                return ToNodeSetNodeId(authored, nodeSet, diagnostics);
            }
            return DeriveDataTypeNodeId(document, name, nodeSet, diagnostics);
        }

        /// <summary>
        /// Derives the namespace-scoped String NodeId of §6.11.1 from a name.
        /// </summary>
        private static string? DeriveDataTypeNodeId(
            WotDocument document,
            string name,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (!TrySplitCompactName(document, name, out string namespaceUri, out string local))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The DataType name '{name}' does not resolve to a namespace, " +
                    "so §6.11.1 cannot derive an identity for it.",
                    new WotLocation(reference: name)));
                return null;
            }
            string portable = "nsu=" + CoreUtils.EscapeUri(namespaceUri) +
                ";s=" + DataTypeIdPrefix + local;
            return ToNodeSetNodeId(portable, nodeSet, diagnostics);
        }

        private static bool TrySplitCompactName(
            WotDocument document,
            string name,
            out string namespaceUri,
            out string local)
        {
            namespaceUri = string.Empty;
            local = string.Empty;
            if (name.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int delimiter = name.IndexOf(';', 4);
                if (delimiter < 0 || delimiter + 1 >= name.Length)
                {
                    return false;
                }
                namespaceUri = CoreUtils.UnescapeUri(name.AsSpan(4, delimiter - 4));
                local = name.Substring(delimiter + 1);
                return true;
            }
            int separator = name.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator + 1 >= name.Length)
            {
                return false;
            }
            string prefix = name.Substring(0, separator);
            if (!TryGetContextNamespace(document, prefix, out namespaceUri))
            {
                return false;
            }
            local = name.Substring(separator + 1);
            return true;
        }

        /// <summary>
        /// Materializes one definition as a DataType Node.
        /// </summary>
        private static void SynthesizeDataType(
            WotDocument document,
            JsonElement definition,
            string identity,
            Dictionary<string, string> identities,
            UANodeSet nodeSet,
            List<UANode> items,
            List<WotDiagnostic> diagnostics)
        {
            string name = GetElementString(definition, "uav:dataTypeName")!;
            string kind = GetElementString(definition, "@type") ?? "uav:StructureDefinition";
            bool isAbstract = GetElementBool(definition, "uav:isAbstract");

            var dataType = new UADataType
            {
                NodeId = identity,
                BrowseName = ToNodeSetQualifiedName(document, name, nodeSet, diagnostics),
                IsAbstract = isAbstract
            };
            ApplyDataTypeText(dataType, definition);

            var references = new List<Reference>
            {
                new()
                {
                    ReferenceType = "HasSubtype",
                    IsForward = false,
                    Value = ResolveBaseDataType(
                        document, definition, kind, identities, nodeSet, diagnostics)
                }
            };

            dataType.Definition = string.Equals(kind, "uav:SimpleDataType", StringComparison.Ordinal)
                ? null
                : BuildDataTypeDefinition(
                    document, definition, kind, name, identities, nodeSet, diagnostics);

            if (dataType.Definition is not null && !isAbstract && !IsEnumerationKind(kind))
            {
                AppendEncodings(definition, identity, dataType.BrowseName!, references, items);
            }
            else if (isAbstract)
            {
                RejectEncodingIdsOnAbstractType(definition, diagnostics, name);
            }

            dataType.References = [.. references];
            items.Add(dataType);
        }

        private static bool IsEnumerationKind(string kind)
        {
            return string.Equals(kind, "uav:EnumDefinition", StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolves the immediate base DataType, applying the defaults of §6.11.2.
        /// </summary>
        private static string ResolveBaseDataType(
            WotDocument document,
            JsonElement definition,
            string kind,
            Dictionary<string, string> identities,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (definition.TryGetProperty("uav:dataTypeSubtypeOf", out JsonElement declared))
            {
                string? resolved = ResolveDataTypeReference(
                    document, declared, identities, nodeSet, diagnostics);
                if (resolved is not null)
                {
                    return resolved;
                }
            }
            if (IsEnumerationKind(kind))
            {
                // An OptionSet has no default: §6.11.2 requires it to name the
                // integer type whose bits the fields number.
                if (GetElementBool(definition, "uav:isOptionSet"))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        "An OptionSet shall state uav:dataTypeSubtypeOf; §6.11.2 " +
                        "gives it no default because the base decides how many " +
                        "bits the fields may number."));
                }
                return WotVocabulary.Enumeration;
            }
            if (string.Equals(kind, "uav:SimpleDataType", StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    "A SimpleDataType shall state uav:dataTypeSubtypeOf; §6.11.2 " +
                    "gives it no default."));
                return WotVocabulary.BaseDataType;
            }
            return IsUnionStructure(definition)
                ? WotVocabulary.Union
                : WotVocabulary.Structure;
        }

        private static bool IsUnionStructure(JsonElement definition)
        {
            string? structureType = GetElementString(definition, "uav:structureType");
            return structureType is not null &&
                structureType.StartsWith("Union", StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolves a DataType reference, which §6.11.3 permits to be a sibling
        /// definition, a name, or a NodeId.
        /// </summary>
        /// <remarks>
        /// A JSON-LD <c>@id</c> is a graph identifier and never an OPC UA
        /// NodeId, so it is resolved through the identity table rather than
        /// being read as one.
        /// </remarks>
        private static string? ResolveDataTypeReference(
            WotDocument document,
            JsonElement reference,
            Dictionary<string, string> identities,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (reference.ValueKind == JsonValueKind.String)
            {
                return ToNodeSetNodeId(reference.GetString()!, nodeSet, diagnostics);
            }
            if (reference.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            string? graphId = GetElementString(reference, "@id");
            if (graphId is not null && identities.TryGetValue(graphId, out string? resolved))
            {
                return resolved;
            }
            string? id = GetElementString(reference, "uav:dataTypeId");
            if (id is not null)
            {
                return ToNodeSetNodeId(id, nodeSet, diagnostics);
            }
            string? name = GetElementString(reference, "uav:dataTypeName");
            if (name is not null)
            {
                return DeriveDataTypeNodeId(document, name, nodeSet, diagnostics);
            }
            if (graphId is not null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The DataType definition '{graphId}' is referenced but never " +
                    "stated completely.",
                    new WotLocation(reference: graphId)));
            }
            return null;
        }

        /// <summary>
        /// Builds the Definition attribute from the readable field lists.
        /// </summary>
        private static Opc.Ua.Export.DataTypeDefinition? BuildDataTypeDefinition(
            WotDocument document,
            JsonElement definition,
            string kind,
            string name,
            Dictionary<string, string> identities,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            var result = new Opc.Ua.Export.DataTypeDefinition
            {
                Name = ToNodeSetQualifiedName(document, name, nodeSet, diagnostics)
            };
            if (IsEnumerationKind(kind))
            {
                result.IsOptionSet = GetElementBool(definition, "uav:isOptionSet");
                result.Field = BuildEnumFields(definition, diagnostics);
                return result;
            }
            result.IsUnion = IsUnionStructure(definition);
            result.Field = BuildStructureFields(
                document, definition, identities, nodeSet, diagnostics);
            return result;
        }

        /// <summary>
        /// Builds the ordered enumeration or OptionSet fields of §6.11.5.
        /// </summary>
        private static Opc.Ua.Export.DataTypeField[] BuildEnumFields(
            JsonElement definition,
            List<WotDiagnostic> diagnostics)
        {
            var fields = new List<Opc.Ua.Export.DataTypeField>();
            if (!definition.TryGetProperty("uav:enumFields", out JsonElement declared) ||
                declared.ValueKind != JsonValueKind.Array)
            {
                return [.. fields];
            }
            foreach (JsonElement field in declared.EnumerateArray())
            {
                string? fieldName = GetElementString(field, "uav:enumName");
                if (fieldName is null)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        "An enumeration field carries no uav:enumName, which " +
                        "§6.11.5 makes mandatory."));
                    continue;
                }
                var entry = new Opc.Ua.Export.DataTypeField
                {
                    Name = fieldName,
                    Value = GetElementInt32(field, "uav:enumValue") ?? -1
                };
                ApplyFieldText(entry, field);
                fields.Add(entry);
            }
            return [.. fields];
        }

        /// <summary>
        /// Builds the ordered structure fields of §6.11.3.
        /// </summary>
        private static Opc.Ua.Export.DataTypeField[] BuildStructureFields(
            WotDocument document,
            JsonElement definition,
            Dictionary<string, string> identities,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            var fields = new List<Opc.Ua.Export.DataTypeField>();
            if (!definition.TryGetProperty("uav:fields", out JsonElement declared) ||
                declared.ValueKind != JsonValueKind.Array)
            {
                return [.. fields];
            }
            foreach (JsonElement field in declared.EnumerateArray())
            {
                string? fieldName = GetElementString(field, "uav:fieldName");
                if (fieldName is null)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        "A structure field carries no uav:fieldName, which " +
                        "§6.11.3 makes mandatory."));
                    continue;
                }
                var entry = new Opc.Ua.Export.DataTypeField
                {
                    Name = fieldName,
                    DataType = ResolveFieldDataType(
                        document, field, identities, nodeSet, diagnostics),
                    ValueRank = GetElementInt32(field, "uav:valueRank") ?? -1,
                    IsOptional = GetElementBool(field, "uav:isOptional"),
                    AllowSubTypes = GetElementBool(field, "uav:allowSubtypes")
                };
                uint? maxStringLength = GetElementUInt32(field, "uav:maxStringLength");
                if (maxStringLength is { } length)
                {
                    entry.MaxStringLength = length;
                }
                string? arrayDimensions = ReadArrayDimensions(field);
                if (arrayDimensions is not null)
                {
                    entry.ArrayDimensions = arrayDimensions;
                }
                ApplyFieldText(entry, field);
                fields.Add(entry);
            }
            return [.. fields];
        }

        private static string ResolveFieldDataType(
            WotDocument document,
            JsonElement field,
            Dictionary<string, string> identities,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (field.TryGetProperty("uav:fieldDataTypeDefinition", out JsonElement nested))
            {
                string? resolved = ResolveDataTypeReference(
                    document, nested, identities, nodeSet, diagnostics);
                if (resolved is not null)
                {
                    return resolved;
                }
            }
            string? id = GetElementString(field, "uav:fieldDataTypeId");
            if (id is not null)
            {
                return ToNodeSetNodeId(id, nodeSet, diagnostics);
            }
            string? name = GetElementString(field, "uav:fieldDataTypeName");
            if (name is not null)
            {
                string? derived = DeriveDataTypeNodeId(document, name, nodeSet, diagnostics);
                if (derived is not null)
                {
                    return derived;
                }
            }
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.DataTypeDefinitionInvalid,
                $"The structure field '{GetElementString(field, "uav:fieldName")}' " +
                "states no DataType; §6.11.3 requires one of " +
                "uav:fieldDataTypeDefinition, uav:fieldDataTypeId or " +
                "uav:fieldDataTypeName."));
            return WotVocabulary.BaseDataType;
        }

        private static string? ReadArrayDimensions(JsonElement field)
        {
            if (!field.TryGetProperty("uav:arrayDimensions", out JsonElement declared) ||
                declared.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var parts = new List<string>();
            foreach (JsonElement dimension in declared.EnumerateArray())
            {
                if (dimension.ValueKind == JsonValueKind.Number &&
                    dimension.TryGetUInt32(out uint value))
                {
                    parts.Add(value.ToString(CultureInfo.InvariantCulture));
                }
            }
            return parts.Count == 0 ? null : string.Join(",", parts);
        }

        /// <summary>
        /// Appends the encoding Objects of §6.11.7.
        /// </summary>
        /// <remarks>
        /// Every non-abstract Structure and Union exposes all three standard
        /// encodings, and their identities are derived by extending the type's
        /// own String identity so that a reader can recompute them.
        /// </remarks>
        private static void AppendEncodings(
            JsonElement definition,
            string identity,
            string browseName,
            List<Reference> references,
            List<UANode> items)
        {
            AppendEncoding(
                definition, "uav:binaryEncodingId", identity + BinaryEncodingSuffix,
                "Default Binary", identity, references, items);
            AppendEncoding(
                definition, "uav:xmlEncodingId", identity + XmlEncodingSuffix,
                "Default XML", identity, references, items);
            AppendEncoding(
                definition, "uav:jsonEncodingId", identity + JsonEncodingSuffix,
                "Default JSON", identity, references, items);
            _ = browseName;
        }

        private static void AppendEncoding(
            JsonElement definition,
            string term,
            string derivedId,
            string name,
            string dataTypeId,
            List<Reference> references,
            List<UANode> items)
        {
            string encodingId = GetElementString(definition, term) ?? derivedId;
            references.Add(new Reference
            {
                ReferenceType = "HasEncoding",
                IsForward = true,
                Value = encodingId
            });
            items.Add(new UAObject
            {
                NodeId = encodingId,
                BrowseName = name,
                References =
                [
                    new Reference
                    {
                        ReferenceType = "HasTypeDefinition",
                        IsForward = true,
                        Value = WotVocabulary.DataTypeEncodingType
                    },
                    new Reference
                    {
                        ReferenceType = "HasEncoding",
                        IsForward = false,
                        Value = dataTypeId
                    }
                ]
            });
        }

        private static void RejectEncodingIdsOnAbstractType(
            JsonElement definition,
            List<WotDiagnostic> diagnostics,
            string name)
        {
            foreach (string term in s_encodingTerms)
            {
                if (definition.TryGetProperty(term, out _))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"The abstract DataType '{name}' states {term}; §6.11.7 " +
                        "gives an abstract type a null DefaultEncodingId and no " +
                        "encoding Objects, because no value of it is ever encoded.",
                        new WotLocation(reference: name)));
                }
            }
        }

        private static readonly string[] s_encodingTerms =
        [
            "uav:defaultEncodingId",
            "uav:binaryEncodingId",
            "uav:xmlEncodingId",
            "uav:jsonEncodingId"
        ];

        private static void ApplyDataTypeText(UADataType dataType, JsonElement definition)
        {
            string? title = GetElementString(definition, "title");
            if (title is not null)
            {
                dataType.DisplayName = MakeText(title);
            }
            string? description = GetElementString(definition, "description");
            if (description is not null)
            {
                dataType.Description = MakeText(description);
            }
        }

        private static void ApplyFieldText(Opc.Ua.Export.DataTypeField field, JsonElement declared)
        {
            string? title = GetElementString(declared, "title");
            if (title is not null)
            {
                field.DisplayName = MakeText(title);
            }
            string? description = GetElementString(declared, "description");
            if (description is not null)
            {
                field.Description = MakeText(description);
            }
        }

        private static int? GetElementInt32(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out int result)
                ? result
                : null;
        }

        private static uint? GetElementUInt32(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetUInt32(out uint result)
                ? result
                : null;
        }

        /// <summary>
        /// Emits every DataType the NodeSet defines into the readable
        /// <c>uav:dataTypeDefinitions</c> of §6.11.
        /// </summary>
        /// <remarks>
        /// This is the completeness contract of §6.11.8. Before it, a DataType
        /// could only reach a document through the native projection, so a
        /// Structure or an Enumeration was on its own enough to force
        /// <c>uav:nodes</c> onto a document that needed nothing else from it.
        /// </remarks>
        private static void WriteDataTypeDefinitions(Utf8JsonWriter writer, UANodeSet nodeSet)
        {
            UADataType[] dataTypes = CollectDataTypeNodes(nodeSet);
            if (dataTypes.Length == 0)
            {
                return;
            }
            writer.WritePropertyName("uav:dataTypeDefinitions");
            writer.WriteStartArray();
            foreach (UADataType dataType in dataTypes)
            {
                WriteDataTypeDefinition(writer, dataType, nodeSet);
            }
            writer.WriteEndArray();
        }

        private static UADataType[] CollectDataTypeNodes(UANodeSet nodeSet)
        {
            if (nodeSet.Items is null)
            {
                return [];
            }
            var dataTypes = new List<UADataType>();
            foreach (UANode node in nodeSet.Items)
            {
                if (node is UADataType dataType)
                {
                    dataTypes.Add(dataType);
                }
            }
            return [.. dataTypes];
        }

        private static void WriteDataTypeDefinition(
            Utf8JsonWriter writer,
            UADataType dataType,
            UANodeSet nodeSet)
        {
            Opc.Ua.Export.DataTypeDefinition? definition = dataType.Definition;
            bool isEnumeration = definition is not null && HasEnumFields(definition);

            writer.WriteStartObject();
            string? portableId = ToPortableNodeId(dataType.NodeId, nodeSet.NamespaceUris);
            if (!string.IsNullOrEmpty(portableId))
            {
                writer.WriteString("@id", portableId);
            }
            writer.WriteString("@type", DefinitionKind(definition, isEnumeration));
            string? name = ToPortableQualifiedName(dataType.BrowseName, nodeSet.NamespaceUris);
            if (!string.IsNullOrEmpty(name))
            {
                writer.WriteString("uav:dataTypeName", name);
            }
            if (!string.IsNullOrEmpty(portableId))
            {
                writer.WriteString("uav:dataTypeId", portableId);
            }
            if (dataType.IsAbstract)
            {
                writer.WriteBoolean("uav:isAbstract", true);
            }
            WriteBaseDataType(writer, dataType, nodeSet);
            WriteDescription(writer, dataType.Description);

            if (definition is null)
            {
                writer.WriteEndObject();
                return;
            }
            if (isEnumeration)
            {
                writer.WriteBoolean("uav:isOptionSet", definition.IsOptionSet);
                WriteEnumFields(writer, definition);
            }
            else
            {
                writer.WriteString(
                    "uav:structureType",
                    definition.IsUnion ? "Union" : StructureTypeName(definition));
                WriteStructureFields(writer, definition, nodeSet);
            }
            writer.WriteEndObject();
        }

        /// <summary>
        /// Distinguishes an enumeration definition from a structure one.
        /// </summary>
        /// <remarks>
        /// A NodeSet says which it is only by the shape of its fields: an
        /// enumeration field carries a Value and no DataType, a structure field
        /// the reverse. An OptionSet is an enumeration whose values are bit
        /// numbers, which the flag records rather than the field shape.
        /// </remarks>
        private static bool HasEnumFields(Opc.Ua.Export.DataTypeDefinition definition)
        {
            if (definition.IsOptionSet)
            {
                return true;
            }
            if (definition.Field is null || definition.Field.Length == 0)
            {
                return false;
            }
            foreach (Opc.Ua.Export.DataTypeField field in definition.Field)
            {
                if (!string.IsNullOrEmpty(field.DataType) &&
                    !string.Equals(field.DataType, WotVocabulary.BaseDataType, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static string DefinitionKind(
            Opc.Ua.Export.DataTypeDefinition? definition,
            bool isEnumeration)
        {
            if (definition is null)
            {
                return "uav:SimpleDataType";
            }
            return isEnumeration ? "uav:EnumDefinition" : "uav:StructureDefinition";
        }

        private static string StructureTypeName(Opc.Ua.Export.DataTypeDefinition definition)
        {
            if (definition.Field is not null)
            {
                foreach (Opc.Ua.Export.DataTypeField field in definition.Field)
                {
                    if (field.IsOptional)
                    {
                        return "StructureWithOptionalFields";
                    }
                }
            }
            return "Structure";
        }

        private static void WriteBaseDataType(
            Utf8JsonWriter writer,
            UADataType dataType,
            UANodeSet nodeSet)
        {
            if (dataType.References is null)
            {
                return;
            }
            foreach (Reference reference in dataType.References)
            {
                if (!string.Equals(reference.ReferenceType, "HasSubtype", StringComparison.Ordinal) ||
                    reference.IsForward)
                {
                    continue;
                }
                string? portable = ToPortableDataTypeReference(reference.Value, nodeSet);
                if (string.IsNullOrEmpty(portable))
                {
                    continue;
                }
                writer.WritePropertyName("uav:dataTypeSubtypeOf");
                writer.WriteStartObject();
                writer.WriteString("uav:dataTypeId", portable);
                writer.WriteEndObject();
                return;
            }
        }

        private static void WriteEnumFields(
            Utf8JsonWriter writer,
            Opc.Ua.Export.DataTypeDefinition definition)
        {
            writer.WritePropertyName("uav:enumFields");
            writer.WriteStartArray();
            if (definition.Field is not null)
            {
                foreach (Opc.Ua.Export.DataTypeField field in definition.Field)
                {
                    writer.WriteStartObject();
                    writer.WriteString("@type", "uav:EnumField");
                    writer.WriteString("uav:enumName", field.Name);
                    writer.WriteNumber("uav:enumValue", field.Value);
                    WriteDescription(writer, field.Description);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
        }

        private static void WriteStructureFields(
            Utf8JsonWriter writer,
            Opc.Ua.Export.DataTypeDefinition definition,
            UANodeSet nodeSet)
        {
            writer.WritePropertyName("uav:fields");
            writer.WriteStartArray();
            if (definition.Field is not null)
            {
                foreach (Opc.Ua.Export.DataTypeField field in definition.Field)
                {
                    writer.WriteStartObject();
                    writer.WriteString("@type", "uav:StructureField");
                    writer.WriteString("uav:fieldName", field.Name);
                    string? portable = ToPortableDataTypeReference(field.DataType, nodeSet);
                    if (!string.IsNullOrEmpty(portable))
                    {
                        writer.WriteString("uav:fieldDataTypeId", portable);
                    }
                    writer.WriteNumber("uav:valueRank", field.ValueRank);
                    WriteFieldArrayDimensions(writer, field.ArrayDimensions);
                    if (field.MaxStringLength != 0)
                    {
                        writer.WriteNumber("uav:maxStringLength", field.MaxStringLength);
                    }
                    writer.WriteBoolean("uav:isOptional", field.IsOptional);
                    writer.WriteBoolean("uav:allowSubtypes", field.AllowSubTypes);
                    WriteDescription(writer, field.Description);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
        }

        private static void WriteFieldArrayDimensions(Utf8JsonWriter writer, string? arrayDimensions)
        {
            if (string.IsNullOrEmpty(arrayDimensions))
            {
                return;
            }
            writer.WritePropertyName("uav:arrayDimensions");
            writer.WriteStartArray();
            foreach (string part in arrayDimensions!.Split(','))
            {
                if (uint.TryParse(
                    part.Trim(),
                    System.Globalization.NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint value))
                {
                    writer.WriteNumberValue(value);
                }
            }
            writer.WriteEndArray();
        }

        /// <summary>
        /// Converts a NodeSet DataType attribute into its portable form,
        /// resolving an alias name first.
        /// </summary>
        /// <remarks>
        /// A NodeSet is free to write <c>DataType="Structure"</c> against its
        /// own Aliases table. That name means nothing outside the document, so
        /// it is resolved here rather than emitted as if it were an identifier.
        /// </remarks>
        private static string? ToPortableDataTypeReference(string? value, UANodeSet nodeSet)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }
            if (nodeSet.Aliases is not null)
            {
                foreach (NodeIdAlias alias in nodeSet.Aliases)
                {
                    if (string.Equals(alias.Alias, value, StringComparison.Ordinal))
                    {
                        return ToPortableNodeId(alias.Value, nodeSet.NamespaceUris);
                    }
                }
            }
            return ToPortableNodeId(value, nodeSet.NamespaceUris);
        }
    }
}