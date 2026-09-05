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
        private static Dictionary<string, string> SynthesizeDataTypeDefinitions(
            WotDocument document,
            UANodeSet nodeSet,
            List<UANode> items,
            HashSet<string> nestedOnly,
            List<WotDiagnostic> diagnostics)
        {
            var empty = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, JsonElement> complete = CollectAllDataTypeDefinitions(
                document, diagnostics);
            if (complete.Count == 0)
            {
                return empty;
            }

            // Two passes: the identity of every definition has to be known
            // before any field can point at one, because §6.11.3 lets a field
            // name a sibling definition by its JSON-LD @id and that @id is not
            // itself a NodeId.
            var identities = new Dictionary<string, string>(StringComparer.Ordinal);
            var claimed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonElement> entry in complete)
            {
                string? identity = ResolveDataTypeIdentity(
                    document, entry.Value, nodeSet, diagnostics);
                if (identity is null)
                {
                    continue;
                }

                // Two definitions on one NodeId would materialize as one Node
                // silently overwriting the other, so the collision is refused
                // rather than resolved by document order.
                if (claimed.TryGetValue(identity, out string? owner))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"The DataTypes '{owner}' and " +
                        $"'{GetElementString(entry.Value, "uav:dataTypeName")}' both " +
                        $"claim the identity '{identity}'.",
                        new WotLocation(reference: identity)));
                    continue;
                }
                claimed[identity] =
                    GetElementString(entry.Value, "uav:dataTypeName") ?? entry.Key;
                identities[entry.Key] = identity;
            }

            foreach (KeyValuePair<string, JsonElement> entry in complete)
            {
                if (identities.TryGetValue(entry.Key, out string? identity))
                {
                    SynthesizeDataType(
                        document, entry.Value, identity, identities, nodeSet, items,
                        nestedOnly, diagnostics);
                }
            }
            ValidateEncodingIdentities(complete, identities, diagnostics);
            ValidateInheritedFieldPrefixes(complete, diagnostics);
            ValidateSubtypeGraph(complete, diagnostics);
            return identities;
        }

        /// <summary>
        /// Checks that the subtype graph is acyclic and kind-compatible.
        /// </summary>
        /// <remarks>
        /// §6.11.2 was tightened by the specification PR: a Structure or Union
        /// subtypes one of the same Union/non-Union family, and an Enumeration
        /// subtypes a non-OptionSet Enumeration. A cycle is worse than wrong —
        /// resolving the inherited prefix or the terminal base would not
        /// terminate — so it is caught before anything walks the graph.
        /// </remarks>
        private static void ValidateSubtypeGraph(
            Dictionary<string, JsonElement> complete,
            List<WotDiagnostic> diagnostics)
        {
            foreach (KeyValuePair<string, JsonElement> entry in complete)
            {
                string name = GetElementString(entry.Value, "uav:dataTypeName") ?? entry.Key;
                var seen = new HashSet<string>(StringComparer.Ordinal) { entry.Key };
                string current = entry.Key;
                JsonElement definition = entry.Value;

                while (TryGetLocalBase(definition, complete, out string? baseId, out JsonElement baseType))
                {
                    if (!seen.Add(baseId!))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.DataTypeDefinitionInvalid,
                            $"The DataType '{name}' is its own ancestor. §6.11.2 " +
                            "requires the subtype graph to be acyclic, and a " +
                            "cycle leaves the inherited fields undefinable.",
                            new WotLocation(reference: name)));
                        break;
                    }
                    ValidateSubtypeKinds(definition, baseType, name, diagnostics);
                    current = baseId!;
                    definition = baseType;
                }
                _ = current;
            }
        }

        private static bool TryGetLocalBase(
            JsonElement definition,
            Dictionary<string, JsonElement> complete,
            out string? baseId,
            out JsonElement baseType)
        {
            baseId = null;
            baseType = default;
            if (!definition.TryGetProperty("uav:dataTypeSubtypeOf", out JsonElement declared) ||
                declared.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            baseId = GetElementString(declared, "@id");
            return baseId is not null && complete.TryGetValue(baseId, out baseType);
        }

        private static void ValidateSubtypeKinds(
            JsonElement definition,
            JsonElement baseType,
            string name,
            List<WotDiagnostic> diagnostics)
        {
            string kind = GetElementString(definition, "@type") ?? "uav:StructureDefinition";
            string baseKind = GetElementString(baseType, "@type") ?? "uav:StructureDefinition";
            if (!string.Equals(kind, baseKind, StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The {KindLabel(kind)} '{name}' subtypes the " +
                    $"{KindLabel(baseKind)} " +
                    $"'{GetElementString(baseType, "uav:dataTypeName")}'. §6.11.2 " +
                    "keeps a DataType within its own kind.",
                    new WotLocation(reference: name)));
                return;
            }
            if (IsEnumerationKind(kind))
            {
                if (GetElementBool(baseType, "uav:isOptionSet"))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"'{name}' subtypes an OptionSet. §6.11.2 lets an " +
                        "Enumeration subtype only a non-OptionSet Enumeration, " +
                        "because an OptionSet's values are bit numbers.",
                        new WotLocation(reference: name)));
                }
                return;
            }
            if (IsUnionStructure(definition) != IsUnionStructure(baseType))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"'{name}' and its base disagree on whether they are Unions. " +
                    "§6.11.2 keeps a Structure or Union within its own family, " +
                    "because the two encode a value differently.",
                    new WotLocation(reference: name)));
            }
        }

        private static string KindLabel(string kind)
        {
            return kind switch
            {
                "uav:EnumDefinition" => "enumeration",
                "uav:SimpleDataType" => "SimpleDataType",
                _ => "structure"
            };
        }

        /// <summary>
        /// Checks that a subtype repeats its base's fields, in order, unchanged.
        /// </summary>
        /// <remarks>
        /// §6.11.3 states inherited fields first. That is not a formatting
        /// preference: the encoding writes the base's fields before the
        /// subtype's own, so renaming, reordering or dropping one silently
        /// shifts every field after it and the value decodes as something else.
        /// </remarks>
        private static void ValidateInheritedFieldPrefixes(
            Dictionary<string, JsonElement> complete,
            List<WotDiagnostic> diagnostics)
        {
            foreach (KeyValuePair<string, JsonElement> entry in complete)
            {
                if (!entry.Value.TryGetProperty("uav:dataTypeSubtypeOf", out JsonElement baseRef) ||
                    baseRef.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                string? baseId = GetElementString(baseRef, "@id");
                if (baseId is null || !complete.TryGetValue(baseId, out JsonElement baseType))
                {
                    continue;
                }
                string name = GetElementString(entry.Value, "uav:dataTypeName") ?? entry.Key;
                List<string> inherited = ReadFieldNames(baseType);
                List<string> declared = ReadFieldNames(entry.Value);
                if (inherited.Count == 0)
                {
                    continue;
                }
                if (declared.Count < inherited.Count)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"'{name}' states {declared.Count} field(s) but inherits " +
                        $"{inherited.Count}. §6.11.3 states inherited fields " +
                        "first, and dropping one shifts every field after it.",
                        new WotLocation(reference: name)));
                    continue;
                }
                for (int ii = 0; ii < inherited.Count; ii++)
                {
                    if (!string.Equals(declared[ii], inherited[ii], StringComparison.Ordinal))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.DataTypeDefinitionInvalid,
                            $"'{name}' states '{declared[ii]}' where it inherits " +
                            $"'{inherited[ii]}'. §6.11.3 requires the inherited " +
                            "fields first and unchanged, because the encoding " +
                            "writes them before the subtype's own.",
                            new WotLocation(reference: name)));
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Reports whether the NodeSet gives this DataType any encoding Object.
        /// </summary>
        /// <remarks>
        /// The link may be written from either end, and real companion models
        /// write it from the Object: the DI NodeSet, for instance, carries no
        /// forward Reference on the DataType at all and declares each encoding
        /// as an Object referring back. Looking only one way concludes that a
        /// perfectly ordinary Structure has no encodings.
        /// </remarks>
        /// <summary>
        /// States the identities of the encoding Objects the NodeSet actually
        /// gives this DataType.
        /// </summary>
        /// <remarks>
        /// §6.11.7 derives an encoding identity from the type's own only when
        /// the author omits it, and preserves an explicit one. A NodeSet
        /// virtually always allocates its own — the DI model numbers them in
        /// its own range — so a converter that says nothing here loses the real
        /// Objects and invents three differently named ones in their place. The
        /// address space then has the right shape and the wrong identities,
        /// which is worse than an obvious gap because everything still browses.
        /// </remarks>
        private static void WriteEncodingIdentities(
            Utf8JsonWriter writer,
            UADataType dataType,
            UANodeSet nodeSet)
        {
            if (nodeSet.Items is null || string.IsNullOrEmpty(dataType.NodeId))
            {
                return;
            }
            foreach (UANode node in nodeSet.Items)
            {
                if (node is not UAObject encoding ||
                    encoding.References is null ||
                    string.IsNullOrEmpty(encoding.NodeId))
                {
                    continue;
                }
                bool belongsHere = false;
                foreach (Reference reference in encoding.References)
                {
                    if (string.Equals(
                            reference.ReferenceType, "HasEncoding", StringComparison.Ordinal) &&
                        string.Equals(reference.Value, dataType.NodeId, StringComparison.Ordinal))
                    {
                        belongsHere = true;
                        break;
                    }
                }
                if (!belongsHere)
                {
                    continue;
                }
                string? term = EncodingTermFor(encoding.BrowseName);
                if (term is null)
                {
                    continue;
                }
                string? portable = ToPortableNodeId(encoding.NodeId, nodeSet.NamespaceUris);
                if (!string.IsNullOrEmpty(portable))
                {
                    writer.WriteString(term, portable);
                }
            }
        }

        private static string? EncodingTermFor(string? browseName)
        {
            string local = LocalName(browseName) ?? string.Empty;
            return local switch
            {
                "Default Binary" => "uav:binaryEncodingId",
                "Default XML" => "uav:xmlEncodingId",
                "Default JSON" => "uav:jsonEncodingId",
                _ => null
            };
        }

        private static bool HasEncoding(UADataType dataType, UANodeSet nodeSet)
        {
            if (dataType.References is not null)
            {
                foreach (Reference reference in dataType.References)
                {
                    if (string.Equals(
                            reference.ReferenceType, "HasEncoding", StringComparison.Ordinal) &&
                        reference.IsForward)
                    {
                        return true;
                    }
                }
            }
            if (nodeSet.Items is null || string.IsNullOrEmpty(dataType.NodeId))
            {
                return false;
            }
            foreach (UANode node in nodeSet.Items)
            {
                if (node is not UAObject encoding || encoding.References is null)
                {
                    continue;
                }
                foreach (Reference reference in encoding.References)
                {
                    if (string.Equals(
                            reference.ReferenceType, "HasEncoding", StringComparison.Ordinal) &&
                        string.Equals(reference.Value, dataType.NodeId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsEncodingSuppressed(JsonElement definition)
        {
            return definition.TryGetProperty("uav:hasDefaultEncoding", out JsonElement declared) &&
                declared.ValueKind == JsonValueKind.False;
        }

        private static List<string> ReadFieldNames(JsonElement definition)
        {
            var names = new List<string>();
            if (definition.TryGetProperty("uav:fields", out JsonElement fields) &&
                fields.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement field in fields.EnumerateArray())
                {
                    string? name = GetElementString(field, "uav:fieldName");
                    if (name is not null)
                    {
                        names.Add(name);
                    }
                }
            }
            return names;
        }

        /// <summary>
        /// Checks the encoding identities across all definitions.
        /// </summary>
        /// <remarks>
        /// Two types sharing one encoding Object would make a value ambiguous
        /// to decode, and a default encoding that names none of the three
        /// points at an Object the type does not have.
        /// </remarks>
        private static void ValidateEncodingIdentities(
            Dictionary<string, JsonElement> complete,
            Dictionary<string, string> identities,
            List<WotDiagnostic> diagnostics)
        {
            var claimed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonElement> entry in complete)
            {
                if (!identities.TryGetValue(entry.Key, out string? identity) ||
                    GetElementBool(entry.Value, "uav:isAbstract") ||
                    IsEncodingSuppressed(entry.Value))
                {
                    continue;
                }
                string name = GetElementString(entry.Value, "uav:dataTypeName") ?? entry.Key;
                string binary = GetElementString(entry.Value, "uav:binaryEncodingId") ??
                    identity + BinaryEncodingSuffix;
                string xml = GetElementString(entry.Value, "uav:xmlEncodingId") ??
                    identity + XmlEncodingSuffix;
                string json = GetElementString(entry.Value, "uav:jsonEncodingId") ??
                    identity + JsonEncodingSuffix;

                foreach (string encoding in new[] { binary, xml, json })
                {
                    if (claimed.TryGetValue(encoding, out string? owner))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.DataTypeDefinitionInvalid,
                            $"The DataTypes '{owner}' and '{name}' both claim the " +
                            $"encoding '{encoding}', which would leave a value of " +
                            "either ambiguous to decode.",
                            new WotLocation(reference: encoding)));
                        continue;
                    }
                    claimed[encoding] = name;
                }

                string? declaredDefault = GetElementString(entry.Value, "uav:defaultEncodingId");
                if (declaredDefault is not null &&
                    !string.Equals(declaredDefault, binary, StringComparison.Ordinal) &&
                    !string.Equals(declaredDefault, xml, StringComparison.Ordinal) &&
                    !string.Equals(declaredDefault, json, StringComparison.Ordinal))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"The DataType '{name}' defaults to the encoding " +
                        $"'{declaredDefault}', which is none of the three it " +
                        "exposes; §6.11.7 gives it no fourth encoding to name.",
                        new WotLocation(reference: name)));
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
        /// <summary>
        /// Collects every DataType definition the document states, wherever it
        /// states it.
        /// </summary>
        /// <remarks>
        /// §6.11.1 lets a definition sit in the Thing root's
        /// <c>uav:dataTypeDefinitions</c> or inline as a DataSchema's
        /// <c>uav:dataTypeDefinition</c>, and says both identify the same graph
        /// node. So the two have to be gathered together before anything is
        /// resolved, or an affordance that names a definition stated inline
        /// somewhere else in the document cannot find it.
        /// </remarks>
        private static Dictionary<string, JsonElement> CollectAllDataTypeDefinitions(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            var complete = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (document.RootElement.TryGetProperty(
                    "uav:dataTypeDefinitions", out JsonElement declared) &&
                declared.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement definition in declared.EnumerateArray())
                {
                    AddDataTypeDefinition(definition, complete, diagnostics);
                }
            }
            CollectInlineDataTypeDefinitions(document.RootElement, complete, diagnostics);
            return complete;
        }

        private static void CollectInlineDataTypeDefinitions(
            JsonElement element,
            Dictionary<string, JsonElement> complete,
            List<WotDiagnostic> diagnostics)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty member in element.EnumerateObject())
                    {
                        if (string.Equals(
                            member.Name, "uav:dataTypeDefinition", StringComparison.Ordinal))
                        {
                            AddDataTypeDefinition(member.Value, complete, diagnostics);
                            continue;
                        }
                        if (string.Equals(
                            member.Name, "uav:dataTypeDefinitions", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        CollectInlineDataTypeDefinitions(member.Value, complete, diagnostics);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        CollectInlineDataTypeDefinitions(item, complete, diagnostics);
                    }
                    break;
            }
        }

        private static void AddDataTypeDefinition(
            JsonElement definition,
            Dictionary<string, JsonElement> complete,
            List<WotDiagnostic> diagnostics)
        {
            if (definition.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            string? graphId = GetElementString(definition, "@id");
            if (graphId is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    "A DataType definition carries no @id, so nothing can " +
                    "reference it and it cannot be checked for duplication."));
                return;
            }
            if (IsReferenceOnlyDefinition(definition))
            {
                return;
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
                return;
            }
            complete.Add(graphId, definition);
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
            string portable = "nsu=" +
                CoreUtils.EscapeUri(namespaceUri) +
                ";s=" +
                DataTypeIdPrefix +
                local;
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
            HashSet<string> nestedOnly,
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
            ApplyDataTypeText(dataType, definition, GetDeclaredLocale(document));

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

            // The declaration is checked before the kind is, because a kind
            // with no encodings to begin with may not state anything about
            // them: short-circuiting past the check would make the term
            // silently ignored exactly where it is meaningless.
            bool declared = ExposesDefaultEncoding(
                definition, name, isAbstract, kind, diagnostics);
            bool exposesEncodings = declared &&
                dataType.Definition is not null &&
                !isAbstract &&
                !IsEnumerationKind(kind);
            if (exposesEncodings)
            {
                // §6.11.7 derives an encoding identity from the name-derived
                // String NodeId, deliberately independent of an explicit
                // numeric, GUID or opaque uav:dataTypeId, so that appending
                // "/Default Binary" always yields a valid String NodeId rather
                // than something glued onto a numeric identifier.
                string encodingRoot =
                    DeriveDataTypeNodeId(document, name, nodeSet, diagnostics) ?? identity;
                AppendEncodings(
                    definition, encodingRoot, dataType.BrowseName!, references, items,
                    nodeSet, diagnostics);
            }
            else if (isAbstract)
            {
                RejectEncodingIdsOnAbstractType(definition, diagnostics, name);
            }
            else if (!declared && dataType.Definition is not null && !IsEnumerationKind(kind))
            {
                // A concrete Structure or Union that states it has no default
                // encoding is reachable only from inside another Structure: it
                // has a null DefaultEncodingId, so nothing can put a value of
                // it into an ExtensionObject on its own. Remembering it here is
                // what lets closure validation refuse a Variable, argument or
                // Event field that selects it directly.
                nestedOnly.Add(identity);
            }

            dataType.References = [.. references];
            items.Add(dataType);
        }

        /// <summary>
        /// Decides whether a definition exposes the three default encodings.
        /// </summary>
        /// <remarks>
        /// §6.11.7 defaults <c>uav:hasDefaultEncoding</c> to true for a
        /// non-abstract Structure or Union. A concrete type used only inside
        /// other Structures, never directly in an ExtensionObject, may set it
        /// false: it is never encoded on its own, so generating encodings for
        /// it would advertise Objects nothing can reach. Only a kind that can
        /// carry the term may state it at all.
        /// </remarks>
        private static bool ExposesDefaultEncoding(
            JsonElement definition,
            string name,
            bool isAbstract,
            string kind,
            List<WotDiagnostic> diagnostics)
        {
            if (!definition.TryGetProperty("uav:hasDefaultEncoding", out JsonElement declared))
            {
                return true;
            }
            if (isAbstract ||
                IsEnumerationKind(kind) ||
                string.Equals(kind, "uav:SimpleDataType", StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"'{name}' states uav:hasDefaultEncoding, which §6.11.7 " +
                    "allows only on a non-abstract Structure or Union; every " +
                    "other kind has no encoding Objects to begin with.",
                    new WotLocation(reference: name)));
                return false;
            }
            return declared.ValueKind != JsonValueKind.False;
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
                    ValidateOptionSetBase(definition, kind, resolved, diagnostics);
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

        /// <summary>
        /// Checks an OptionSet's base against §6.11.5.
        /// </summary>
        /// <remarks>
        /// The PR narrowed this from "an unsigned integer" to the four concrete
        /// unsigned types, and ruled out the abstract UInteger: the base has to
        /// say how many bits exist, and an abstract type says only that there
        /// are some. The highest authored bit has to fit in it for the same
        /// reason.
        /// </remarks>
        private static void ValidateOptionSetBase(
            JsonElement definition,
            string kind,
            string resolvedBase,
            List<WotDiagnostic> diagnostics)
        {
            if (!IsEnumerationKind(kind) || !GetElementBool(definition, "uav:isOptionSet"))
            {
                return;
            }
            if (!s_optionSetBaseWidths.TryGetValue(resolvedBase, out int width))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"An OptionSet subtypes '{resolvedBase}'. §6.11.5 requires " +
                    "the concrete Byte, UInt16, UInt32 or UInt64; an abstract " +
                    "base does not say how many bits exist.",
                    new WotLocation(reference: resolvedBase)));
                return;
            }
            if (!definition.TryGetProperty("uav:enumFields", out JsonElement fields) ||
                fields.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            foreach (JsonElement field in fields.EnumerateArray())
            {
                int bit = GetElementInt32(field, "uav:enumValue") ?? -1;
                if (bit >= width)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        "The OptionSet field " +
                        $"'{GetElementString(field, "uav:enumName")}' numbers bit " +
                        $"{bit}, which its {width}-bit base cannot represent.",
                        new WotLocation(reference: resolvedBase)));
                }
            }
        }

        private static readonly Dictionary<string, int> s_optionSetBaseWidths =
            new(StringComparer.Ordinal)
            {
                ["i=3"] = 8,
                ["i=5"] = 16,
                ["i=7"] = 32,
                ["i=9"] = 64
            };

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
                result.Field = BuildEnumFields(
                    definition, result.IsOptionSet, name, GetDeclaredLocale(document),
                    diagnostics);
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
            bool isOptionSet,
            string typeName,
            string? defaultLocale,
            List<WotDiagnostic> diagnostics)
        {
            var fields = new List<Opc.Ua.Export.DataTypeField>();
            var values = new Dictionary<int, string>();
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
                int value = GetElementInt32(field, "uav:enumValue") ?? -1;

                // Two names on one value cannot be told apart on the way back,
                // so the value would no longer say which field it is.
                if (values.TryGetValue(value, out string? owner))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"The fields '{owner}' and '{fieldName}' of '{typeName}' " +
                        $"share the value {value}, so the value no longer says " +
                        "which field it is.",
                        new WotLocation(reference: typeName)));
                    continue;
                }
                values[value] = fieldName;

                // §6.11.5 makes an OptionSet value a bit number rather than a
                // mask, and there is no negative bit.
                if (isOptionSet && value < 0)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"The OptionSet field '{fieldName}' of '{typeName}' " +
                        $"numbers bit {value}, and there is no negative bit.",
                        new WotLocation(reference: fieldName)));
                    continue;
                }
                var entry = new Opc.Ua.Export.DataTypeField
                {
                    Name = fieldName,
                    Value = value
                };
                ApplyFieldText(entry, field, defaultLocale);
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
                ValidateFieldKind(definition, field, entry, fieldName, diagnostics);
                uint? maxStringLength = GetElementUInt32(field, "uav:maxStringLength");
                if (maxStringLength is { } length)
                {
                    entry.MaxStringLength = length;
                }
                string? arrayDimensions = ReadArrayDimensions(field, fieldName, diagnostics);
                if (arrayDimensions is not null)
                {
                    entry.ArrayDimensions = arrayDimensions;
                }
                ApplyFieldText(entry, field, GetDeclaredLocale(document));
                fields.Add(entry);
            }
            return [.. fields];
        }

        /// <summary>
        /// Rejects a field whose facets contradict the kind its definition
        /// declares, or whose dimensions contradict its rank.
        /// </summary>
        /// <remarks>
        /// Both would otherwise materialize silently into a malformed OPC UA
        /// definition. A plain Structure has no room for an absent field, and
        /// a subtyped value needs a kind that admits one, so §6.11.2 pairs each
        /// facet with the kinds that can carry it. ArrayDimensions describes one
        /// bound per dimension, so its length is the rank by construction.
        /// </remarks>
        private static void ValidateFieldKind(
            JsonElement definition,
            JsonElement field,
            Opc.Ua.Export.DataTypeField entry,
            string fieldName,
            List<WotDiagnostic> diagnostics)
        {
            string structureType = GetElementString(definition, "uav:structureType") ?? "Structure";
            if (entry.IsOptional &&
                !structureType.Contains("Optional", StringComparison.Ordinal) &&
                !structureType.StartsWith("Union", StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The field '{fieldName}' is optional but its definition is " +
                    $"a '{structureType}', which has no room for an absent " +
                    "field; §6.11.2 needs StructureWithOptionalFields for that.",
                    new WotLocation(reference: fieldName)));
            }
            if (entry.AllowSubTypes &&
                !structureType.Contains("SubtypedValues", StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The field '{fieldName}' allows subtype values but its " +
                    $"definition is a '{structureType}'; §6.11.2 needs a " +
                    "subtyped-value kind to carry one.",
                    new WotLocation(reference: fieldName)));
            }
            if (!field.TryGetProperty("uav:arrayDimensions", out JsonElement dimensions) ||
                dimensions.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            int count = dimensions.GetArrayLength();
            if (count != 0 && count != entry.ValueRank)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The field '{fieldName}' states {count} array dimension(s) " +
                    $"against a ValueRank of {entry.ValueRank}. ArrayDimensions " +
                    "carries one bound per dimension, so its length is the rank.",
                    new WotLocation(reference: fieldName)));
            }
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

            // §6.11.3 lets a field state its type through the ordinary WoT
            // members, and requires them to agree with the DataType. The one
            // reading §6.11.4 refuses is a bare integer or number, which inside
            // a Structure does not say which concrete type is meant.
            string? jsonType = GetElementString(field, "type");
            if (jsonType is not null)
            {
                string? fieldName = GetElementString(field, "uav:fieldName");
                if (jsonType is "integer" or "number")
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"The field '{fieldName}' states a bare '{jsonType}'. " +
                        "§6.11.4 makes that ambiguous inside a Structure, " +
                        "because permitting subtype values would need a " +
                        "subtyped-value kind; state a concrete DataType instead.",
                        new WotLocation(reference: fieldName)));
                    return WotVocabulary.BaseDataType;
                }
                return WotVocabulary.MapJsonTypeToDataType(
                    jsonType,
                    GetElementString(field, "contentEncoding"),
                    GetElementString(field, "format"));
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

        /// <summary>
        /// Reads an authored <c>uav:arrayDimensions</c> as the NodeSet
        /// attribute, rejecting anything OPC 10000-3 cannot express.
        /// </summary>
        /// <remarks>
        /// A dimension is a <c>UInt32</c>: OPC 10000-3 uses zero for a bound
        /// that is not fixed and has no way to say "minus one" or "two and a
        /// half". Dropping an entry that is none of those would silently change
        /// the rank the remaining entries describe - three authored dimensions
        /// of which one is <c>-1</c> would materialize as a two-dimensional
        /// array the author never wrote - so a malformed entry rejects the
        /// whole term and the document with it.
        /// </remarks>
        private static string? ReadArrayDimensions(
            JsonElement field,
            string where,
            List<WotDiagnostic> diagnostics)
        {
            if (!field.TryGetProperty("uav:arrayDimensions", out JsonElement declared))
            {
                return null;
            }
            if (declared.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidValueRank,
                    "The uav:arrayDimensions term shall be an ordered array of " +
                    "non-negative integers, one per dimension (WoT Binding Section 7).",
                    new WotLocation(reference: where)));
                return null;
            }
            var parts = new List<string>();
            int index = 0;
            foreach (JsonElement dimension in declared.EnumerateArray())
            {
                if (dimension.ValueKind != JsonValueKind.Number ||
                    !IsIntegerLiteral(dimension) ||
                    !dimension.TryGetUInt32(out uint value))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidValueRank,
                        $"The uav:arrayDimensions entry at index {index} of '{where}' is " +
                        $"'{dimension.GetRawText()}'. A dimension is an OPC 10000-3 UInt32, " +
                        "which has no negative, fractional, textual or out-of-range value; " +
                        "an entry that is none of those is rejected rather than dropped, " +
                        "because dropping one changes the rank the rest describe " +
                        "(WoT Binding Section 7).",
                        new WotLocation(reference: where)));
                    return null;
                }
                parts.Add(value.ToString(CultureInfo.InvariantCulture));
                index++;
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
            List<UANode> items,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            AppendEncoding(
                definition, "uav:binaryEncodingId", identity + BinaryEncodingSuffix,
                "Default Binary", identity, references, items, nodeSet, diagnostics);
            AppendEncoding(
                definition, "uav:xmlEncodingId", identity + XmlEncodingSuffix,
                "Default XML", identity, references, items, nodeSet, diagnostics);
            AppendEncoding(
                definition, "uav:jsonEncodingId", identity + JsonEncodingSuffix,
                "Default JSON", identity, references, items, nodeSet, diagnostics);
            _ = browseName;
        }

        private static void AppendEncoding(
            JsonElement definition,
            string term,
            string derivedId,
            string name,
            string dataTypeId,
            List<Reference> references,
            List<UANode> items,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            // An authored identity is portable; a NodeSet attribute is not. It
            // has to be resolved here or the encoding Object lands beside the
            // one it was meant to be.
            string? authored = GetElementString(definition, term);
            string encodingId = authored is null
                ? derivedId
                : ToNodeSetNodeId(authored, nodeSet, diagnostics);
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

        private static void ApplyDataTypeText(
            UADataType dataType,
            JsonElement definition,
            string? defaultLocale = null)
        {
            Opc.Ua.Export.LocalizedText[]? title = ReadTitle(definition, defaultLocale);
            if (title is not null)
            {
                dataType.DisplayName = title;
            }
            Opc.Ua.Export.LocalizedText[]? description =
                ReadDescription(definition, defaultLocale);
            if (description is not null)
            {
                dataType.Description = description;
            }
        }

        private static void ApplyFieldText(
            Opc.Ua.Export.DataTypeField field,
            JsonElement declared,
            string? defaultLocale = null)
        {
            Opc.Ua.Export.LocalizedText[]? title = ReadTitle(declared, defaultLocale);
            if (title is not null)
            {
                field.DisplayName = title;
            }
            Opc.Ua.Export.LocalizedText[]? description =
                ReadDescription(declared, defaultLocale);
            if (description is not null)
            {
                field.Description = description;
            }
        }

        /// <summary>
        /// Reads a whole-number member of a DataSchema.
        /// </summary>
        /// <remarks>
        /// The kind of the carrier is checked first. A DataSchema may be
        /// written as something other than an object - a Thing Model that
        /// states <c>"Speed": 7</c> is malformed but parses - and asking a
        /// number for a member throws rather than answering, which would turn a
        /// document defect into an exception out of a describe-and-report call.
        /// </remarks>
        private static int? GetElementInt32(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out JsonElement value) &&
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
        private static void WriteDataTypeDefinitions(
            Utf8JsonWriter writer,
            UANodeSet nodeSet,
            string defaultLocale)
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
                WriteDataTypeDefinition(writer, dataType, nodeSet, defaultLocale);
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
            UANodeSet nodeSet,
            string defaultLocale)
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
            else if (definition is not null && !isEnumeration && !HasEncoding(dataType, nodeSet))
            {
                // §6.11.7: a concrete Structure reached only through other
                // Structures has no encodings. Saying so is the only way the
                // way back does not generate the three it never had.
                writer.WriteBoolean("uav:hasDefaultEncoding", false);
            }
            WriteEncodingIdentities(writer, dataType, nodeSet);
            WriteBaseDataType(writer, dataType, nodeSet);
            WriteLocalizedTitle(writer, dataType.DisplayName, defaultLocale);
            WriteLocalizedDescription(writer, dataType.Description, defaultLocale);

            if (definition is null)
            {
                writer.WriteEndObject();
                return;
            }
            if (isEnumeration)
            {
                writer.WriteBoolean("uav:isOptionSet", definition.IsOptionSet);
                WriteEnumFields(writer, definition, defaultLocale);
            }
            else
            {
                writer.WriteString("uav:structureType", StructureTypeName(definition));
                WriteStructureFields(writer, definition, nodeSet, defaultLocale);
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

        /// <summary>
        /// Names the structure kind of §6.11.2 from the facets its fields carry.
        /// </summary>
        /// <remarks>
        /// A NodeSet states only <c>IsUnion</c>, so the rest of the kind has to
        /// be read back off the fields: a field that may be absent means the
        /// optional-field kind, and one that admits a subtype means a
        /// subtyped-value kind. Reading only optionality would silently demote
        /// a subtyped-value structure to a plain one.
        /// </remarks>
        private static string StructureTypeName(Opc.Ua.Export.DataTypeDefinition definition)
        {
            bool allowsSubtypes = false;
            bool hasOptional = false;
            if (definition.Field is not null)
            {
                foreach (Opc.Ua.Export.DataTypeField field in definition.Field)
                {
                    allowsSubtypes |= field.AllowSubTypes;
                    hasOptional |= field.IsOptional;
                }
            }
            if (definition.IsUnion)
            {
                return allowsSubtypes ? "UnionWithSubtypedValues" : "Union";
            }
            if (allowsSubtypes)
            {
                return "StructureWithSubtypedValues";
            }
            return hasOptional ? "StructureWithOptionalFields" : "Structure";
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
            Opc.Ua.Export.DataTypeDefinition definition,
            string defaultLocale)
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
                    WriteLocalizedTitle(writer, field.DisplayName, defaultLocale);
                    WriteLocalizedDescription(writer, field.Description, defaultLocale);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
        }

        private static void WriteStructureFields(
            Utf8JsonWriter writer,
            Opc.Ua.Export.DataTypeDefinition definition,
            UANodeSet nodeSet,
            string defaultLocale)
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
                    WriteLocalizedTitle(writer, field.DisplayName, defaultLocale);
                    WriteLocalizedDescription(writer, field.Description, defaultLocale);
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
            if (NodeSetDeclaredAliases.FromNodeSet(nodeSet).TryResolve(value!, out string declared))
            {
                return ToPortableNodeId(declared, nodeSet.NamespaceUris);
            }
            return ToPortableNodeId(value, nodeSet.NamespaceUris);
        }

        /// <summary>
        /// Infers a DataType definition from a DataSchema alone, per §6.11.4
        /// and §6.11.5.
        /// </summary>
        /// <remarks>
        /// Inference only runs where the schema determines every required fact.
        /// Where it does not, it fails and says so rather than guessing: a
        /// wrong DataType is worse than a missing one, because it is silently
        /// wrong at every later read.
        /// </remarks>
        private static void SynthesizeInferredDataTypes(
            WotDocument document,
            Dictionary<string, string> identities,
            UANodeSet nodeSet,
            List<UANode> items,
            List<WotDiagnostic> diagnostics)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonElement> affordance in document.Properties)
            {
                InferDataType(document, affordance.Value, identities, nodeSet, items, seen, diagnostics);
            }
        }

        private static void InferDataType(
            WotDocument document,
            JsonElement schema,
            Dictionary<string, string> identities,
            UANodeSet nodeSet,
            List<UANode> items,
            HashSet<string> seen,
            List<WotDiagnostic> diagnostics)
        {
            string? name = GetElementString(schema, "uav:dataTypeName");
            if (name is null ||
                name.StartsWith("ua:", StringComparison.Ordinal) ||
                schema.TryGetProperty("uav:mapToType", out _))
            {
                return;
            }
            string? identity = DeriveDataTypeNodeId(document, name, nodeSet, diagnostics);
            if (identity is null || !seen.Add(identity))
            {
                return;
            }

            bool isEnumeration = schema.TryGetProperty("oneOf", out JsonElement branches) &&
                IsEnumerationBranches(branches);
            bool isStructure = string.Equals(
                GetElementString(schema, "type"), "object", StringComparison.Ordinal);
            if (!isEnumeration && !isStructure)
            {
                InferSimpleDataType(document, schema, name, identity, identities, nodeSet, items, diagnostics);
                return;
            }

            var dataType = new UADataType
            {
                NodeId = identity,
                BrowseName = ToNodeSetQualifiedName(document, name, nodeSet, diagnostics)
            };
            ApplyDataTypeText(dataType, schema, GetDeclaredLocale(document));
            var references = new List<Reference>
            {
                new()
                {
                    ReferenceType = "HasSubtype",
                    IsForward = false,
                    Value = isEnumeration
                        ? WotVocabulary.Enumeration
                        : IsUnionStructure(schema) ? WotVocabulary.Union : WotVocabulary.Structure
                }
            };

            dataType.Definition = isEnumeration
                ? BuildInferredEnumeration(
                    dataType.BrowseName!, branches, GetDeclaredLocale(document), diagnostics)
                : BuildInferredStructure(
                    document, schema, dataType.BrowseName!, name, nodeSet, diagnostics);
            if (dataType.Definition is null)
            {
                return;
            }
            if (!isEnumeration)
            {
                AppendEncodings(schema, identity, dataType.BrowseName!, references, items, nodeSet, diagnostics);
            }
            dataType.References = [.. references];
            items.Add(dataType);
        }

        /// <summary>
        /// A SimpleDataType is inferred only where a concrete base is named:
        /// §6.11.4 forbids a custom type subtyping the abstract Integer or
        /// Number, so a bare numeric schema states no usable base.
        /// </summary>
        private static void InferSimpleDataType(
            WotDocument document,
            JsonElement schema,
            string name,
            string identity,
            Dictionary<string, string> identities,
            UANodeSet nodeSet,
            List<UANode> items,
            List<WotDiagnostic> diagnostics)
        {
            if (!schema.TryGetProperty("uav:dataTypeSubtypeOf", out JsonElement declared))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The DataType '{name}' is authored by name alone. §6.11.4 " +
                    "requires uav:dataTypeSubtypeOf naming a concrete base, " +
                    "because a custom type shall not subtype the abstract " +
                    "Integer or Number.",
                    new WotLocation(reference: name)));
                return;
            }
            string? baseType = ResolveDataTypeReference(
                document, declared, identities, nodeSet, diagnostics);
            if (baseType is null)
            {
                return;
            }
            var dataType = new UADataType
            {
                NodeId = identity,
                BrowseName = ToNodeSetQualifiedName(document, name, nodeSet, diagnostics),
                References =
                [
                    new Reference
                    {
                        ReferenceType = "HasSubtype",
                        IsForward = false,
                        Value = baseType
                    }
                ]
            };
            ApplyDataTypeText(dataType, schema, GetDeclaredLocale(document));
            items.Add(dataType);
        }

        /// <summary>
        /// An enumeration is inferred from <c>oneOf</c> branches that each
        /// carry a <c>const</c> and a name. §6.11.5 refuses to infer one from a
        /// bare <c>enum</c> array, which states values but never names them.
        /// </summary>
        private static bool IsEnumerationBranches(JsonElement branches)
        {
            if (branches.ValueKind != JsonValueKind.Array ||
                branches.GetArrayLength() == 0)
            {
                return false;
            }
            foreach (JsonElement branch in branches.EnumerateArray())
            {
                if (branch.ValueKind != JsonValueKind.Object ||
                    !branch.TryGetProperty("const", out _) ||
                    GetElementString(branch, "uav:enumName") is null)
                {
                    return false;
                }
            }
            return true;
        }

        private static Opc.Ua.Export.DataTypeDefinition BuildInferredEnumeration(
            string browseName,
            JsonElement branches,
            string? defaultLocale,
            List<WotDiagnostic> diagnostics)
        {
            var fields = new List<Opc.Ua.Export.DataTypeField>();
            foreach (JsonElement branch in branches.EnumerateArray())
            {
                var field = new Opc.Ua.Export.DataTypeField
                {
                    Name = GetElementString(branch, "uav:enumName"),
                    Value = GetElementInt32(branch, "const") ?? -1
                };
                ApplyFieldText(field, branch, defaultLocale);
                fields.Add(field);
            }
            _ = diagnostics;
            return new Opc.Ua.Export.DataTypeDefinition
            {
                Name = browseName,
                Field = [.. fields]
            };
        }

        /// <summary>
        /// Builds inferred structure fields, in the order §6.11.4 requires the
        /// schema to state.
        /// </summary>
        /// <remarks>
        /// JSON member order carries no meaning, so beyond a single property
        /// the schema shall carry <c>uav:fieldOrder</c>. Without it the
        /// encoding order of the fields is unknowable and inference fails.
        /// </remarks>
        private static Opc.Ua.Export.DataTypeDefinition? BuildInferredStructure(
            WotDocument document,
            JsonElement schema,
            string browseName,
            string name,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (!schema.TryGetProperty("properties", out JsonElement properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            List<string>? order = ReadFieldOrder(schema, properties, name, diagnostics);
            if (order is null)
            {
                return null;
            }
            HashSet<string> required = ReadRequiredFields(schema);
            bool isUnion = IsUnionStructure(schema);
            var fields = new List<Opc.Ua.Export.DataTypeField>();
            foreach (string fieldName in order)
            {
                if (!properties.TryGetProperty(fieldName, out JsonElement fieldSchema))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"The uav:fieldOrder of '{name}' names '{fieldName}', " +
                        "which the schema does not define.",
                        new WotLocation(reference: name)));
                    return null;
                }
                string? fieldDataType = InferFieldDataType(
                    document, fieldSchema, name, fieldName, nodeSet, diagnostics);
                if (fieldDataType is null)
                {
                    return null;
                }
                var field = new Opc.Ua.Export.DataTypeField
                {
                    Name = fieldName,
                    DataType = fieldDataType,
                    IsOptional = !isUnion && !required.Contains(fieldName)
                };
                int? valueRank = GetElementInt32(fieldSchema, "uav:valueRank");
                if (valueRank is { } rank)
                {
                    field.ValueRank = rank;
                }
                string? dimensions = ReadArrayDimensions(fieldSchema, fieldName, diagnostics);
                if (dimensions is not null)
                {
                    field.ArrayDimensions = dimensions;
                }
                ApplyFieldText(field, fieldSchema, GetDeclaredLocale(document));
                fields.Add(field);
            }
            return new Opc.Ua.Export.DataTypeDefinition
            {
                Name = browseName,
                IsUnion = isUnion,
                Field = [.. fields]
            };
        }

        private static List<string>? ReadFieldOrder(
            JsonElement schema,
            JsonElement properties,
            string name,
            List<WotDiagnostic> diagnostics)
        {
            var declared = new List<string>();
            if (schema.TryGetProperty("uav:fieldOrder", out JsonElement order) &&
                order.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in order.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        declared.Add(entry.GetString()!);
                    }
                }
                return declared;
            }
            int count = 0;
            string? single = null;
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                single = property.Name;
                count++;
            }
            if (count > 1)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The inferred DataType '{name}' has {count} properties but " +
                    "states no uav:fieldOrder. JSON member order carries no " +
                    "meaning, so §6.11.4 makes the order mandatory beyond one " +
                    "property.",
                    new WotLocation(reference: name)));
                return null;
            }
            if (single is not null)
            {
                declared.Add(single);
            }
            return declared;
        }

        private static HashSet<string> ReadRequiredFields(JsonElement schema)
        {
            var required = new HashSet<string>(StringComparer.Ordinal);
            if (schema.TryGetProperty("required", out JsonElement declared) &&
                declared.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in declared.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        required.Add(entry.GetString()!);
                    }
                }
            }
            return required;
        }

        /// <summary>
        /// Resolves an inferred field's DataType, refusing the ambiguous cases
        /// §6.11.4 names.
        /// </summary>
        /// <remarks>
        /// A bare integer or number is honest about a scalar Variable, where
        /// the abstract type permits subtype values. Inside a Structure field
        /// it is not: accepting subtype values there would require a subtyped
        /// -value Structure kind, which the schema has not asked for. So the
        /// field states a concrete type or inference fails.
        /// </remarks>
        private static string? InferFieldDataType(
            WotDocument document,
            JsonElement fieldSchema,
            string name,
            string fieldName,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            string? id = GetElementString(fieldSchema, "uav:dataTypeId");
            if (id is not null)
            {
                return ToNodeSetNodeId(id, nodeSet, diagnostics);
            }
            string? typeName = GetElementString(fieldSchema, "uav:dataTypeName");
            if (typeName is not null && !typeName.StartsWith("ua:", StringComparison.Ordinal))
            {
                return DeriveDataTypeNodeId(document, typeName, nodeSet, diagnostics);
            }
            string? jsonType = GetElementString(fieldSchema, "type");
            if (typeName is null &&
                jsonType is "integer" or "number")
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The field '{fieldName}' of '{name}' states a bare " +
                    $"'{jsonType}'. §6.11.4 makes that ambiguous inside a " +
                    "Structure, because permitting subtype values would need a " +
                    "subtyped-value kind; state a concrete DataType instead.",
                    new WotLocation(reference: name)));
                return null;
            }
            return WotVocabulary.MapJsonTypeToDataType(
                jsonType,
                GetElementString(fieldSchema, "contentEncoding"),
                GetElementString(fieldSchema, "format"));
        }
    }
}
