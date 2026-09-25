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
        private const string DefaultEncodingsTerm = "uav:defaultEncodings";

        [Flags]
        private enum EncodingPresence
        {
            None = 0,
            Binary = 1,
            Xml = 2,
            Json = 4,
            All = Binary | Xml | Json
        }

        /// <summary>
        /// Collects DataType nodes in the order used by the generated definition array.
        /// </summary>
        internal static ArrayOf<UADataType> CollectDataTypeNodes(UANodeSet nodeSet)
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
            return dataTypes.ToArrayOf();
        }

        /// <summary>
        /// Allocates identities for the document's DataType definition closure before consumers are created.
        /// </summary>
        private static DataTypeDefinitionContext CreateDataTypeDefinitionContext(
            WotDocument document,
            UANodeSet nodeSet,
            ArrayOf<WotDataTypeDefinitionSource> sources,
            ArrayOf<WotDocument> sharedOwners,
            List<WotDiagnostic> diagnostics,
            Dictionary<(string NamespaceUri, string Name), string?>? resolvedNames = null)
        {
            Dictionary<string, JsonElement> complete = CollectAllDataTypeDefinitions(
                document, diagnostics);
            var owners = new Dictionary<string, WotDocument>(StringComparer.Ordinal);
            foreach (string graphId in complete.Keys)
            {
                owners.Add(graphId, document);
            }
            foreach (WotDocument owner in sharedOwners)
            {
                if (TakesRestorePath(owner))
                {
                    continue;
                }
                foreach (JsonElement definition in ReadDataTypeDefinitionOccurrences(owner.RootElement))
                {
                    AddSource(owner, definition);
                }
            }
            foreach (WotDataTypeDefinitionSource source in sources)
            {
                AddSource(source.Document, source.Definition);
                foreach (JsonElement nested in ReadDataTypeDefinitionOccurrences(source.Definition))
                {
                    AddSource(source.Document, nested);
                }
            }
            var identities = new Dictionary<string, string>(StringComparer.Ordinal);
            var claimed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonElement> entry in complete)
            {
                string? identity = ResolveDataTypeIdentity(
                    owners[entry.Key], entry.Value, nodeSet, diagnostics);
                if (identity is null)
                {
                    continue;
                }
                identity = NormalizeExpandedNodeId(ToPortableNodeId(identity, nodeSet.NamespaceUris) ?? identity);
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
            var emissionOwners = new HashSet<WotDocument> { document };
            foreach (WotDocument owner in sharedOwners)
            {
                emissionOwners.Add(owner);
            }
            foreach (WotDataTypeDefinitionSource source in sources)
            {
                if (source.ProjectedSeparately)
                {
                    emissionOwners.Add(source.Document);
                }
            }
            var context = new DataTypeDefinitionContext(complete, owners, identities, emissionOwners, document);
            foreach (KeyValuePair<string, JsonElement> entry in complete)
            {
                if (identities.TryGetValue(entry.Key, out string? identity) &&
                    GetElementString(entry.Value, "uav:dataTypeName") is { } name)
                {
                    context.AddName(owners[entry.Key], entry.Value, name, identity);
                }
            }
            if (resolvedNames is not null)
            {
                foreach (KeyValuePair<(string NamespaceUri, string Name), string?> entry in resolvedNames)
                {
                    if (!context.NamedIdentities.ContainsKey(entry.Key))
                    {
                        context.NamedIdentities.Add(entry.Key, entry.Value is { } identity
                            ? NormalizeExpandedNodeId(identity)
                            : null);
                    }
                }
            }
            foreach (WotDocument owner in emissionOwners)
            {
                if (TakesRestorePath(owner))
                {
                    continue;
                }
                foreach (JsonElement schema in ReadDataSchemaOccurrences(owner))
                {
                    string? name = GetElementString(schema, "uav:dataTypeName");
                    if (name is null ||
                        schema.TryGetProperty("uav:mapToType", out _) ||
                        schema.TryGetProperty("uav:dataTypeId", out _) ||
                        schema.TryGetProperty("uav:dataTypeDefinition", out _))
                    {
                        continue;
                    }
                    bool known = TryResolveDataTypeName(owner, name, context, schema, out _);
                    string? identity = known ||
                        (TrySplitCompactName(owner, name, out string namespaceUri, out _, schema) &&
                            namespaceUri == WotVocabulary.OpcUaNamespace)
                        ? ResolveDataTypeName(owner, name, nodeSet, diagnostics, schema, context)
                        : DeriveDataTypeNodeId(owner, name, nodeSet, diagnostics, schema);
                    if (identity is null)
                    {
                        continue;
                    }
                    identity = NormalizeExpandedNodeId(ToPortableNodeId(identity, nodeSet.NamespaceUris) ?? identity);
                    context.SchemaIdentities[schema] = identity;
                    if (!known)
                    {
                        context.AddName(owner, schema, name, identity);
                        context.InferredSchemas.Add(identity, [(owner, schema)]);
                    }
                    else if (context.InferredSchemas.TryGetValue(
                        identity, out List<(WotDocument Document, JsonElement Schema)>? candidates) &&
                        (schema.TryGetProperty("properties", out _) ||
                            schema.TryGetProperty("items", out _) ||
                            schema.TryGetProperty("oneOf", out _) ||
                            schema.TryGetProperty("uav:dataTypeSubtypeOf", out _)))
                    {
                        candidates.Add((owner, schema));
                    }
                }
            }
            InitializeDataTypeValidation(context, nodeSet, diagnostics);
            return context;

            void AddSource(WotDocument owner, JsonElement definition)
            {
                string? graphId = ReadDataTypeGraphId(owner, definition);
                if (graphId is null || IsReferenceOnlyDefinition(definition))
                {
                    return;
                }
                if (complete.TryGetValue(graphId, out JsonElement existing) && existing.Equals(definition))
                {
                    return;
                }
                int count = complete.Count;
                AddDataTypeDefinition(owner, definition, complete, diagnostics);
                if (complete.Count != count)
                {
                    owners[graphId] = owner;
                }
            }
        }

        internal static ArrayOf<JsonElement> ReadDataSchemaOccurrences(WotDocument document)
        {
            var schemas = new List<JsonElement>();
            Visit(document.RootElement);
            foreach (KeyValuePair<string, JsonElement> action in document.Actions)
            {
                if (action.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                if (action.Value.TryGetProperty(InputMember, out JsonElement input))
                {
                    Visit(input);
                }
                if (action.Value.TryGetProperty(OutputMember, out JsonElement output))
                {
                    Visit(output);
                }
            }
            foreach (KeyValuePair<string, JsonElement> eventAffordance in document.Events)
            {
                if (eventAffordance.Value.ValueKind == JsonValueKind.Object &&
                    eventAffordance.Value.TryGetProperty(DataMember, out JsonElement data))
                {
                    VisitProperties(data);
                }
            }
            return schemas.ToArrayOf();

            void Visit(JsonElement schema)
            {
                if (schema.ValueKind != JsonValueKind.Object)
                {
                    return;
                }
                schemas.Add(schema);
                VisitProperties(schema);
                if (schema.TryGetProperty("items", out JsonElement items))
                {
                    Visit(items);
                }
            }

            void VisitProperties(JsonElement schema)
            {
                if (schema.ValueKind == JsonValueKind.Object &&
                    schema.TryGetProperty("properties", out JsonElement properties) &&
                    properties.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in properties.EnumerateObject())
                    {
                        Visit(property.Value);
                    }
                }
            }
        }

        private static List<(JsonElement Node, string Name)> ReadDataTypeNameOccurrences(JsonElement root)
        {
            var names = new List<(JsonElement Node, string Name)>();
            Visit(root);
            return names;

            void Visit(JsonElement element, bool indexMap = false)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty member in element.EnumerateObject())
                    {
                        if (indexMap)
                        {
                            Visit(member.Value);
                            continue;
                        }
                        if (WotDocument.IsSemanticBoundary(member.Name) || IsLiteralSchemaMember(member.Name))
                        {
                            continue;
                        }
                        if (member.Value.ValueKind == JsonValueKind.String &&
                            member.Name is "uav:dataTypeName" or "uav:fieldDataTypeName" or "uav:dataTypeSubtypeOf")
                        {
                            names.Add((element, member.Value.GetString()!));
                        }
                        Visit(member.Value, IsSchemaDeclarationMap(member.Name));
                    }
                }
                else if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        Visit(item);
                    }
                }
            }
        }

        private static void ValidateDataTypeClosureOwnership(
            WotDocument document,
            UANodeSet nodeSet,
            List<UANode> items,
            DataTypeDefinitionContext context,
            List<WotDiagnostic> diagnostics)
        {
            foreach (UANode node in items)
            {
                if (ToPortableNodeId(node.NodeId, nodeSet.NamespaceUris) is not { } portable)
                {
                    continue;
                }
                string identity = NormalizeExpandedNodeId(portable);
                if (context.ProducedNodes.TryGetValue(identity, out (WotDocument Document, UANode Node) previous) &&
                    !ReferenceEquals(previous.Document, document) &&
                    (IsDataTypeAllocation(node) || IsDataTypeAllocation(previous.Node)))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"The DataType or encoding identity '{identity}' is owned by distinct document nodes.",
                        new WotLocation(nodeId: identity)));
                }
                context.ProducedNodes.TryAdd(identity, (document, node));
            }

            static bool IsDataTypeAllocation(UANode node)
            {
                if (node is UADataType)
                {
                    return true;
                }
                foreach (Reference reference in node.References ?? [])
                {
                    if (!reference.IsForward && reference.ReferenceType is "HasEncoding" or "i=38")
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private static void SynthesizeDataTypeDefinitions(
            WotDocument document,
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<UANode> items,
            HashSet<string> nestedOnly,
            List<WotDiagnostic> diagnostics)
        {
            foreach (KeyValuePair<string, JsonElement> entry in context.Definitions)
            {
                if (context.Identities.TryGetValue(entry.Key, out string? portable))
                {
                    string identity = ToNodeSetNodeId(portable, nodeSet, diagnostics);
                    if (!context.IsEmissionOwner(document, context.Owners[entry.Key]))
                    {
                        if (IsEncodingSuppressed(entry.Value) &&
                            !GetElementBool(entry.Value, "uav:isAbstract") &&
                            !IsEnumerationKind(GetElementString(entry.Value, "@type") ?? "uav:StructureDefinition") &&
                            GetElementString(entry.Value, "@type") != "uav:SimpleDataType")
                        {
                            nestedOnly.Add(identity);
                        }
                        continue;
                    }
                    SynthesizeDataType(
                        context.Owners[entry.Key], entry.Value, identity, context, nodeSet, items,
                        nestedOnly, diagnostics);
                }
            }
            ValidateEncodingIdentities(context, nodeSet, diagnostics);
            ValidateInheritedFieldPrefixes(context, nodeSet, diagnostics);
        }

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
        private static EncodingPresence WriteEncodingIdentities(
            Utf8JsonWriter writer,
            UADataType dataType,
            UANodeSet nodeSet,
            out bool complete)
        {
            complete = true;
            if (nodeSet.Items is null || string.IsNullOrEmpty(dataType.NodeId))
            {
                return EncodingPresence.None;
            }
            var aliases = NodeSetDeclaredAliases.FromNodeSet(nodeSet, WotNodeSetAliases.Instance);
            var referenceTypes = WotReferenceTypeNames.Build(nodeSet);
            Dictionary<string, UANode> nodes = BuildIndex(nodeSet);
            EncodingPresence presence = EncodingPresence.None;
            foreach (Reference reference in referenceTypes.GetReferences(dataType))
            {
                if (!reference.IsForward ||
                    ResolveArchivedAlias(reference.ReferenceType, aliases) != "i=38" ||
                    reference.Value is null)
                {
                    continue;
                }
                if (!nodes.TryGetValue(ResolveArchivedAlias(reference.Value, aliases), out UANode? node) ||
                    node is not UAObject encoding ||
                    string.IsNullOrEmpty(encoding.NodeId))
                {
                    complete = false;
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
                    presence |= PresenceForEncodingTerm(term);
                }
            }
            return presence;
        }

        private static string? EncodingTermFor(string? browseName)
        {
            if (IsBaseNamespaceBrowseName(browseName, "Default Binary"))
            {
                return "uav:binaryEncodingId";
            }
            if (IsBaseNamespaceBrowseName(browseName, "Default XML"))
            {
                return "uav:xmlEncodingId";
            }
            if (IsBaseNamespaceBrowseName(browseName, "Default JSON"))
            {
                return "uav:jsonEncodingId";
            }
            return null;
        }

        private static EncodingPresence PresenceForEncodingTerm(string term)
        {
            return term switch
            {
                "uav:binaryEncodingId" or "uav:defaultEncodingId" => EncodingPresence.Binary,
                "uav:xmlEncodingId" => EncodingPresence.Xml,
                "uav:jsonEncodingId" => EncodingPresence.Json,
                _ => EncodingPresence.None
            };
        }

        private static bool IsEncodingSuppressed(JsonElement definition)
        {
            return definition.TryGetProperty("uav:hasDefaultEncoding", out JsonElement declared) &&
                declared.ValueKind == JsonValueKind.False;
        }

        private static IEnumerable<(WotDocument Document, JsonElement Definition, string Kind)>
            ReadEncodingDefinitions(DataTypeDefinitionContext context)
        {
            foreach (KeyValuePair<string, JsonElement> entry in context.Definitions)
            {
                if (context.Identities.ContainsKey(entry.Key))
                {
                    yield return (context.Owners[entry.Key], entry.Value,
                        GetElementString(entry.Value, "@type") ?? "uav:StructureDefinition");
                }
            }
            foreach (List<(WotDocument Document, JsonElement Schema)> candidates in context.InferredSchemas.Values)
            {
                (WotDocument owner, JsonElement schema) = candidates[0];
                JsonElement element = ReadDataTypeElementSchema(schema);
                string kind =
                    element.TryGetProperty("oneOf", out JsonElement branches) && IsEnumerationBranches(branches)
                    ? "uav:EnumDefinition"
                    : GetElementString(element, "type") == "object" ? "uav:StructureDefinition" : "uav:SimpleDataType";
                yield return (owner, schema, kind);
            }
        }

        /// <summary>
        /// Checks the encoding identities across all definitions.
        /// </summary>
        /// <remarks>
        /// Two types sharing one encoding Object would make a value ambiguous
        /// to decode. DefaultEncodingId must identify the normalized Binary
        /// encoding, not an XML or JSON encoding.
        /// </remarks>
        private static void ValidateEncodingIdentities(
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            var claimed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((WotDocument document, JsonElement definition, string kind) in ReadEncodingDefinitions(context))
            {
                string name = GetElementString(definition, "uav:dataTypeName")!;
                EncodingPresence presence = ReadEncodingPresence(
                    definition, name, GetElementBool(definition, "uav:isAbstract"),
                    kind, !IsEncodingSuppressed(definition), diagnostics);
                if (presence == EncodingPresence.None)
                {
                    continue;
                }
                string? encodingRoot = DeriveDataTypeNodeId(document, name, nodeSet, diagnostics, definition);
                if (encodingRoot is null)
                {
                    continue;
                }
                (string binary, string xml, string json) = ResolveEncodingIdentities(
                    definition, encodingRoot, nodeSet, diagnostics);

                foreach ((EncodingPresence flag, string encoding) in new[]
                {
                    (EncodingPresence.Binary, binary),
                    (EncodingPresence.Xml, xml),
                    (EncodingPresence.Json, json)
                })
                {
                    if ((presence & flag) == 0)
                    {
                        continue;
                    }
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

                string? declaredDefault = GetElementString(definition, "uav:defaultEncodingId");
                if (declaredDefault is not null &&
                    NormalizeExpandedNodeId(ToNodeSetNodeId(declaredDefault, nodeSet, diagnostics)) != binary)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"The DataType '{name}' defaults to the encoding " +
                        $"'{declaredDefault}', which does not identify its Default Binary encoding.",
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
            foreach (JsonElement definition in ReadDataTypeDefinitionOccurrences(document.RootElement))
            {
                AddDataTypeDefinition(document, definition, complete, diagnostics);
            }
            return complete;
        }

        /// <summary>
        /// Returns borrowed complete DataType definitions from their original owning document.
        /// Reference-only occurrences are not definitions and are omitted.
        /// </summary>
        /// <param name="document">The document whose lifetime owns the returned elements.</param>
        /// <returns>The complete declaration inputs; no content is acquired or validated.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static ArrayOf<WotDataTypeDefinitionSource> ReadDataTypeDefinitions(WotDocument document)
        {
            _ = document ?? throw new ArgumentNullException(nameof(document));
            var sources = new List<WotDataTypeDefinitionSource>();
            foreach (JsonElement definition in ReadDataTypeDefinitionOccurrences(document.RootElement))
            {
                if (!IsReferenceOnlyDefinition(definition))
                {
                    sources.Add(new WotDataTypeDefinitionSource(document, definition));
                }
            }
            return sources.ToArrayOf();
        }

        internal static ArrayOf<JsonElement> ReadDataTypeDefinitionOccurrences(JsonElement root)
        {
            return ReadDataTypeDefinitionLocations(root).ToArrayOf(entry => entry.Definition);
        }

        internal static ArrayOf<(JsonElement Definition, string Pointer)> ReadDataTypeDefinitionLocations(
            JsonElement root)
        {
            var definitions = new List<(JsonElement Definition, string Pointer)>();
            Visit(root, string.Empty);
            return definitions.ToArrayOf();

            void Visit(JsonElement element, string pointer, bool indexMap = false)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty member in element.EnumerateObject())
                    {
                        string location = pointer + "/" + EscapePointerToken(member.Name);
                        if (indexMap)
                        {
                            Visit(member.Value, location);
                            continue;
                        }
                        if (WotDocument.IsSemanticBoundary(member.Name) || IsLiteralSchemaMember(member.Name))
                        {
                            continue;
                        }
                        if (member.Name is "uav:dataTypeDefinition" or "uav:fieldDataTypeDefinition" &&
                            member.Value.ValueKind == JsonValueKind.Object)
                        {
                            definitions.Add((member.Value, location));
                        }
                        else if (member.Name == "uav:dataTypeDefinitions" &&
                            member.Value.ValueKind == JsonValueKind.Array)
                        {
                            int index = 0;
                            foreach (JsonElement definition in member.Value.EnumerateArray())
                            {
                                if (definition.ValueKind == JsonValueKind.Object)
                                {
                                    definitions.Add((definition, location +
                                        "/" +
                                        index.ToString(CultureInfo.InvariantCulture)));
                                }
                                index++;
                            }
                        }
                        else if (member.Name == "uav:dataTypeSubtypeOf" &&
                            member.Value.ValueKind == JsonValueKind.Object &&
                            IsReferenceOnlyDefinition(member.Value))
                        {
                            definitions.Add((member.Value, location));
                        }
                        Visit(member.Value, location, IsSchemaDeclarationMap(member.Name));
                    }
                }
                else if (element.ValueKind == JsonValueKind.Array)
                {
                    int index = 0;
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        Visit(item, pointer + "/" + index.ToString(CultureInfo.InvariantCulture));
                        index++;
                    }
                }
            }
        }

        internal static bool IsSchemaDeclarationMap(string member)
        {
            return member is "properties" or "actions" or "events" or "schemaDefinitions" or "uriVariables" or
                "$defs" or "definitions" or "patternProperties";
        }

        internal static bool IsLiteralSchemaMember(string member)
        {
            return member is "const" or "default" or "enum" or "examples";
        }

        private static void AddDataTypeDefinition(
            WotDocument document,
            JsonElement definition,
            Dictionary<string, JsonElement> complete,
            List<WotDiagnostic> diagnostics)
        {
            if (definition.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            string? graphId = ReadDataTypeGraphId(document, definition);
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

        private static string? ReadDataTypeGraphId(WotDocument document, JsonElement definition)
        {
            string? raw = GetElementString(definition, "@id");
            if (raw is null)
            {
                return null;
            }
            if (ExpandedNodeId.TryParse(raw, out _))
            {
                return raw;
            }
            return WotProjectionResolver.TryExpandSemanticIdentity(
                raw, document, definition, document.Id ?? string.Empty, false, out string identity)
                    ? identity
                    : throw new FormatException($"The DataType graph identity '{raw}' cannot be expanded in its context.");
        }

        internal static bool IsReferenceOnlyDefinition(JsonElement definition)
        {
            foreach (JsonProperty member in definition.EnumerateObject())
            {
                if (member.Name is not ("@id" or "@context"))
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
        /// <summary>
        /// Applies the shared definition identity rule for synthesis and document indexing.
        /// </summary>
        internal static string? ResolveDataTypeIdentity(
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
            return DeriveDataTypeNodeId(document, name, nodeSet, diagnostics, definition);
        }

        internal static bool ValidateStandardDataTypeReference(
            WotDocument document,
            JsonElement reference,
            List<WotDiagnostic> diagnostics)
        {
            string? id = GetElementString(reference, "uav:dataTypeId");
            string? name = GetElementString(reference, "uav:dataTypeName");
            if (id is null ||
                name is null ||
                !TryResolveDataTypeName(document, name, null, reference, out string? standardId) ||
                standardId is null ||
                NormalizeExpandedNodeId(id) == NormalizeExpandedNodeId(standardId))
            {
                return true;
            }
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.DataTypeDefinitionInvalid,
                $"The standard DataType name '{name}' identifies '{standardId}', not the supplied identity '{id}'.",
                new WotLocation(reference: name)));
            return false;
        }

        private static string? ResolveDataTypeName(
            WotDocument document,
            string name,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            JsonElement carryingNode = default,
            DataTypeDefinitionContext? context = null)
        {
            if (TryResolveDataTypeName(document, name, context, carryingNode, out string? known))
            {
                if (known is null)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"More than one DataType has the qualified name '{name}'.",
                        new WotLocation(reference: name)));
                }
                return known is null ? null : ToNodeSetNodeId(known, nodeSet, diagnostics);
            }
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.DataTypeDefinitionInvalid,
                $"The DataType name '{name}' is not resolved by the conversion context.",
                new WotLocation(reference: name)));
            return null;
        }

        private static bool TryResolveDataTypeName(
            WotDocument document,
            string name,
            DataTypeDefinitionContext? context,
            JsonElement carryingNode,
            out string? identity)
        {
            identity = null;
            if (!TrySplitCompactName(document, name, out string namespaceUri, out string local, carryingNode))
            {
                return false;
            }
            if (namespaceUri == WotVocabulary.OpcUaNamespace &&
                NodeSetStandardAliases.TryGetDataTypeNodeId(local, out string known))
            {
                identity = known;
                return true;
            }
            return context?.NamedIdentities.TryGetValue((namespaceUri, local), out identity) == true;
        }

        /// <summary>
        /// Derives the namespace-scoped String NodeId of §6.11.1 from a name.
        /// </summary>
        private static string? DeriveDataTypeNodeId(
            WotDocument document,
            string name,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            JsonElement carryingNode = default)
        {
            if (!TryDeriveDataTypeNodeId(document, name, out ExpandedNodeId identity, carryingNode))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The DataType name '{name}' does not resolve to a namespace, " +
                    "so §6.11.1 cannot derive an identity for it.",
                    new WotLocation(reference: name)));
                return null;
            }
            return ToNodeSetNodeId(identity.ToString(), nodeSet, diagnostics);
        }

        /// <summary>
        /// Derives the portable DataType identity from its qualified name using the owning context.
        /// Explicitly authored identities take precedence over this derivation.
        /// </summary>
        /// <param name="document">The document owning the name.</param>
        /// <param name="name">The authored qualified DataType name.</param>
        /// <param name="identity">The derived identity, or Null when the name cannot be expanded.</param>
        /// <param name="carryingNode">The element supplying scoped context.</param>
        /// <returns>Whether the qualified name determines an identity.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static bool TryDeriveDataTypeNodeId(
            WotDocument document, string name, out ExpandedNodeId identity, JsonElement carryingNode = default)
        {
            if (!TrySplitCompactName(document, name, out string namespaceUri, out string local, carryingNode))
            {
                identity = ExpandedNodeId.Null;
                return false;
            }
            identity = new ExpandedNodeId(new NodeId(DataTypeIdPrefix + local, 0), namespaceUri);
            return true;
        }

        /// <summary>
        /// Separates a URI-qualified or compact model name using its owning document context.
        /// This does not resolve a Node or assert that the namespace is loaded.
        /// </summary>
        /// <param name="document">The document owning the name.</param>
        /// <param name="name">A compact name or an nsu-qualified name.</param>
        /// <param name="namespaceUri">The expanded namespace URI.</param>
        /// <param name="local">The local name.</param>
        /// <param name="carryingNode">The element supplying scoped context; default uses the document context.</param>
        /// <returns>Whether the name could be separated.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static bool TrySplitCompactName(
            WotDocument document,
            string name,
            out string namespaceUri,
            out string local,
            JsonElement carryingNode = default)
        {
            _ = document ?? throw new ArgumentNullException(nameof(document));
            _ = name ?? throw new ArgumentNullException(nameof(name));
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
                local = name[(delimiter + 1)..];
                return true;
            }
            int separator = name.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator + 1 >= name.Length)
            {
                return false;
            }
            string prefix = name[..separator];
            if (!TryGetContextNamespace(document, prefix, out namespaceUri, carryingNode))
            {
                return false;
            }
            local = name[(separator + 1)..];
            return true;
        }

        /// <summary>
        /// Materializes one definition as a DataType Node.
        /// </summary>
        private static void SynthesizeDataType(
            WotDocument document,
            JsonElement definition,
            string identity,
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<UANode> items,
            HashSet<string> nestedOnly,
            List<WotDiagnostic> diagnostics)
        {
            string name = GetElementString(definition, "uav:dataTypeName")!;
            string kind = GetElementString(definition, "@type") ?? "uav:StructureDefinition";
            bool isAbstract = GetElementBool(definition, "uav:isAbstract");
            string browseName = ToNodeSetQualifiedName(document, name, nodeSet, diagnostics, definition);
            if (!TryGetMatchingDataTypeRoot(
                document, identity, browseName, isAbstract, items, diagnostics, out UADataType? root))
            {
                return;
            }
            UADataType dataType = root ??
                new UADataType
                {
                    NodeId = identity,
                    BrowseName = browseName
                };
            dataType.IsAbstract = isAbstract;
            ApplyDataTypeText(document, dataType, definition);

            var references = new List<Reference>(dataType.References ?? []);
            SetSuperType(references, ResolveBaseDataType(
                document, definition, kind, context, nodeSet, diagnostics));

            dataType.Definition = string.Equals(kind, "uav:SimpleDataType", StringComparison.Ordinal)
                ? null
                : BuildDataTypeDefinition(
                    document, definition, kind, name, context, nodeSet, diagnostics);

            // The declaration is checked before the kind is, because a kind
            // with no encodings to begin with may not state anything about
            // them: short-circuiting past the check would make the term
            // silently ignored exactly where it is meaningless.
            bool declared = ExposesDefaultEncoding(
                definition, name, isAbstract, kind, diagnostics);
            EncodingPresence presence = ReadEncodingPresence(
                definition, name, isAbstract, kind, declared, diagnostics);
            bool exposesEncodings = presence != EncodingPresence.None &&
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
                    DeriveDataTypeNodeId(document, name, nodeSet, diagnostics, definition) ?? identity;
                AppendEncodings(
                    definition, encodingRoot, identity, references, items,
                    nodeSet, diagnostics, presence);
            }
            else if (isAbstract)
            {
                RejectEncodingIdsOnAbstractType(definition, diagnostics, name);
            }
            if (!declared && !isAbstract && dataType.Definition is not null && !IsEnumerationKind(kind))
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
            if (root is null)
            {
                items.Add(dataType);
            }
        }

        private static bool TryGetMatchingDataTypeRoot(
            WotDocument document,
            string identity,
            string browseName,
            bool isAbstract,
            List<UANode> items,
            List<WotDiagnostic> diagnostics,
            out UADataType? root)
        {
            root = items.Count > 0 &&
                items[0] is UADataType candidate &&
                candidate.NodeId is { } rootId &&
                AreSameExpandedNodeId(rootId, identity)
                ? candidate
                : null;
            if (root is not null &&
                (NormalizeArchivedBrowseName(root.BrowseName) != NormalizeArchivedBrowseName(browseName) ||
                    root.Definition is not null ||
                    (document.RootElement.TryGetProperty("uav:isAbstract", out _) && root.IsAbstract != isAbstract)))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The DataType definition '{browseName}' conflicts with the root owning '{identity}'.",
                    new WotLocation(nodeId: identity)));
                return false;
            }
            return true;
        }

        /// <summary>
        /// Reads whether a definition has a non-null Binary default encoding.
        /// </summary>
        /// <remarks>
        /// The legacy flag defaults to true for a concrete Structure or Union.
        /// False forbids Binary but does not discard explicitly supplied XML or
        /// JSON identities. The separate presence policy determines which
        /// encoding Objects are materialized.
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

        private static EncodingPresence ReadEncodingPresence(
            JsonElement definition,
            string name,
            bool isAbstract,
            string kind,
            bool hasDefault,
            List<WotDiagnostic> diagnostics)
        {
            bool exact = definition.TryGetProperty(DefaultEncodingsTerm, out JsonElement declared);
            bool structure = !IsEnumerationKind(kind) && kind != "uav:SimpleDataType";
            if (!structure)
            {
                if (exact)
                {
                    Report("Only a Structure or Union can declare an encoding presence set.");
                }
                foreach (string term in s_encodingTerms)
                {
                    if (definition.TryGetProperty(term, out _))
                    {
                        Report($"Only a Structure or Union can declare the encoding identity {term}.");
                    }
                }
                return EncodingPresence.None;
            }
            EncodingPresence presence = !isAbstract && hasDefault ? EncodingPresence.All : EncodingPresence.None;
            if (exact)
            {
                presence = EncodingPresence.None;
                if (declared.ValueKind != JsonValueKind.Array)
                {
                    Report("The encoding presence must be an array of Binary, XML and JSON names.");
                    return EncodingPresence.None;
                }
                foreach (JsonElement item in declared.EnumerateArray())
                {
                    EncodingPresence flag = item.ValueKind == JsonValueKind.String
                        ? item.GetString() switch
                        {
                            "Binary" => EncodingPresence.Binary,
                            "XML" => EncodingPresence.Xml,
                            "JSON" => EncodingPresence.Json,
                            _ => EncodingPresence.None
                        }
                        : EncodingPresence.None;
                    if (flag == EncodingPresence.None || (presence & flag) != 0)
                    {
                        Report("The encoding presence set contains an unknown or duplicate name.");
                        return EncodingPresence.None;
                    }
                    presence |= flag;
                }
                if (isAbstract
                    ? presence != EncodingPresence.None
                    : (presence & EncodingPresence.Binary) != 0 != hasDefault)
                {
                    Report("Binary presence must agree with the permitted DefaultEncodingId state.");
                    return EncodingPresence.None;
                }
            }
            else if (!isAbstract && !hasDefault)
            {
                if (definition.TryGetProperty("uav:xmlEncodingId", out _))
                {
                    presence |= EncodingPresence.Xml;
                }
                if (definition.TryGetProperty("uav:jsonEncodingId", out _))
                {
                    presence |= EncodingPresence.Json;
                }
            }
            foreach (string term in s_encodingTerms)
            {
                if (definition.TryGetProperty(term, out _) &&
                    (presence & PresenceForEncodingTerm(term)) == 0 &&
                    !isAbstract)
                {
                    Report($"The identity in {term} selects an encoding omitted by the presence policy.");
                }
            }
            return presence;

            void Report(string message)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The DataType '{name}': {message}",
                    new WotLocation(reference: name)));
            }
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
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (definition.TryGetProperty("uav:dataTypeSubtypeOf", out JsonElement declared))
            {
                string? resolved = ResolveDataTypeReference(
                    document, declared, context, nodeSet, diagnostics, definition);
                if (resolved is not null)
                {
                    ValidateOptionSetBase(definition, kind, resolved, context, nodeSet, diagnostics);
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
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (!IsEnumerationKind(kind) || !GetElementBool(definition, "uav:isOptionSet"))
            {
                return;
            }
            string identity = NormalizeDataTypeValidationIdentity(resolvedBase, nodeSet);
            bool terminalResolved = TryGetSimpleTerminal(identity, context, out string terminal);
            BuiltInType builtIn = GetValidationBuiltInType(terminal);
            string widthIdentity = "i=" + ((int)builtIn).ToString(CultureInfo.InvariantCulture);
            if (!terminalResolved || !s_optionSetBaseWidths.TryGetValue(widthIdentity, out int width))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"An OptionSet subtypes '{resolvedBase}' with terminal '{terminal}'. §6.11.5 requires " +
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
            string? structureType = GetElementString(definition, "uav:structureType") ??
                GetElementString(ReadDataTypeElementSchema(definition), "uav:structureType");
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
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            JsonElement carryingNode = default)
        {
            if (reference.ValueKind == JsonValueKind.String)
            {
                string value = reference.GetString()!;
                JsonElement owner = carryingNode.ValueKind == JsonValueKind.Undefined
                    ? document.RootElement : carryingNode;
                string graphIdentity = WotProjectionResolver.TryExpandSemanticIdentity(
                    value, document, owner, document.Id ?? string.Empty, false, out string expanded) ? expanded : value;
                if (context.Identities.TryGetValue(graphIdentity, out string? identity))
                {
                    return ToNodeSetNodeId(identity, nodeSet, diagnostics);
                }
                return WotPortableIdentity.IsPortableNodeId(value) || IsSessionLocalNodeId(value)
                    ? ToNodeSetNodeId(value, nodeSet, diagnostics)
                    : ResolveDataTypeName(document, value, nodeSet, diagnostics, carryingNode, context);
            }
            if (reference.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            if (!ReferencesKnownNamedDataType(document, reference, context, nodeSet) &&
                !ValidateStandardDataTypeReference(document, reference, diagnostics))
            {
                return null;
            }
            string? graphId = ReadDataTypeGraphId(document, reference);
            if (graphId is not null && context.Identities.TryGetValue(graphId, out string? resolved))
            {
                return ToNodeSetNodeId(resolved, nodeSet, diagnostics);
            }
            string? id = GetElementString(reference, "uav:dataTypeId");
            if (id is not null)
            {
                return ToNodeSetNodeId(id, nodeSet, diagnostics);
            }
            string? name = GetElementString(reference, "uav:dataTypeName");
            if (name is not null)
            {
                return ResolveDataTypeName(document, name, nodeSet, diagnostics, reference, context);
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

        private static bool ReferencesKnownNamedDataType(
            WotDocument document,
            JsonElement reference,
            DataTypeDefinitionContext context,
            UANodeSet nodeSet)
        {
            string? id = GetElementString(reference, "uav:dataTypeId");
            string? name = GetElementString(reference, "uav:dataTypeName");
            if (id is null ||
                name is null ||
                !TrySplitCompactName(document, name, out string namespaceUri, out string localName, reference))
            {
                return false;
            }
            string identity = NormalizeDataTypeValidationIdentity(id, nodeSet);
            if (context.NamedIdentities.TryGetValue((namespaceUri, localName), out string? named) && named == identity)
            {
                return true;
            }
            return context.ValidationTypes.TryGetValue(identity, out DataTypeValidationNode? definition) &&
                TrySplitCompactName(definition.Document, definition.Name,
                    out string declaredNamespace, out string declaredName, definition.Source) &&
                namespaceUri == declaredNamespace &&
                localName == declaredName;
        }

        /// <summary>
        /// Builds the Definition attribute from the readable field lists.
        /// </summary>
        private static Export.DataTypeDefinition? BuildDataTypeDefinition(
            WotDocument document,
            JsonElement definition,
            string kind,
            string name,
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            var result = new Export.DataTypeDefinition
            {
                Name = ToNodeSetQualifiedName(document, name, nodeSet, diagnostics, definition)
            };
            if (IsEnumerationKind(kind))
            {
                result.IsOptionSet = GetElementBool(definition, "uav:isOptionSet");
                result.Field = BuildEnumFields(
                    document, definition, result.IsOptionSet, name, diagnostics);
                return result;
            }
            result.IsUnion = IsUnionStructure(definition);
            result.Field = BuildStructureFields(
                document, definition, context, nodeSet, diagnostics);
            return result;
        }

        /// <summary>
        /// Builds the ordered enumeration or OptionSet fields of §6.11.5.
        /// </summary>
        private static DataTypeField[] BuildEnumFields(
            WotDocument document,
            JsonElement definition,
            bool isOptionSet,
            string typeName,
            List<WotDiagnostic> diagnostics)
        {
            var fields = new List<DataTypeField>();
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
                var entry = new DataTypeField
                {
                    Name = fieldName,
                    Value = value
                };
                ApplyFieldText(document, entry, field);
                fields.Add(entry);
            }
            return [.. fields];
        }

        /// <summary>
        /// Builds the ordered structure fields of §6.11.3.
        /// </summary>
        private static DataTypeField[] BuildStructureFields(
            WotDocument document,
            JsonElement definition,
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            var fields = new List<DataTypeField>();
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
                var entry = new DataTypeField
                {
                    Name = fieldName,
                    DataType = ResolveFieldDataType(
                        document, field, context, nodeSet, diagnostics),
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
                ApplyFieldText(document, entry, field);
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
            DataTypeField entry,
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
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (field.TryGetProperty("uav:fieldDataTypeDefinition", out JsonElement nested))
            {
                string? resolved = ResolveDataTypeReference(
                    document, nested, context, nodeSet, diagnostics, field);
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
                return ResolveDataTypeName(document, name, nodeSet, diagnostics, field, context)
                    ?? WotVocabulary.BaseDataType;
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
            string encodingRoot,
            string dataTypeId,
            List<Reference> references,
            List<UANode> items,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            EncodingPresence presence = EncodingPresence.All)
        {
            (string binary, string xml, string json) = ResolveEncodingIdentities(
                definition, encodingRoot, nodeSet, diagnostics);
            if ((presence & EncodingPresence.Binary) != 0)
            {
                AppendEncoding(binary, "Default Binary", dataTypeId, references, items);
            }
            if ((presence & EncodingPresence.Xml) != 0)
            {
                AppendEncoding(xml, "Default XML", dataTypeId, references, items);
            }
            if ((presence & EncodingPresence.Json) != 0)
            {
                AppendEncoding(json, "Default JSON", dataTypeId, references, items);
            }
        }

        private static (string Binary, string Xml, string Json) ResolveEncodingIdentities(
            JsonElement definition,
            string encodingRoot,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            string? binary = GetElementString(definition, "uav:binaryEncodingId") ??
                GetElementString(definition, "uav:defaultEncodingId");
            string? xml = GetElementString(definition, "uav:xmlEncodingId");
            string? json = GetElementString(definition, "uav:jsonEncodingId");
            return (
                NormalizeExpandedNodeId(binary is null
                    ? encodingRoot + BinaryEncodingSuffix
                    : ToNodeSetNodeId(binary, nodeSet, diagnostics)),
                NormalizeExpandedNodeId(xml is null
                    ? encodingRoot + XmlEncodingSuffix
                    : ToNodeSetNodeId(xml, nodeSet, diagnostics)),
                NormalizeExpandedNodeId(json is null
                    ? encodingRoot + JsonEncodingSuffix
                    : ToNodeSetNodeId(json, nodeSet, diagnostics)));
        }

        private static void AppendEncoding(
            string encodingId,
            string name,
            string dataTypeId,
            List<Reference> references,
            List<UANode> items)
        {
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
            WotDocument document,
            UADataType dataType,
            JsonElement definition)
        {
            Export.LocalizedText[]? title = ReadTitle(document, definition);
            if (title is not null)
            {
                dataType.DisplayName = title;
            }
            Export.LocalizedText[]? description =
                ReadDescription(document, definition);
            if (description is not null)
            {
                dataType.Description = description;
            }
        }

        private static void ApplyFieldText(
            WotDocument document,
            DataTypeField field,
            JsonElement declared)
        {
            Export.LocalizedText[]? title = ReadTitle(document, declared);
            if (title is not null)
            {
                field.DisplayName = title;
            }
            Export.LocalizedText[]? description =
                ReadDescription(document, declared);
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
        /// Emits the document's complete readable DataType definitions of §6.11.
        /// </summary>
        /// <remarks>
        /// This is the completeness contract of §6.11.8. Before it, a DataType
        /// could only reach a document through the native projection, so a
        /// Structure or an Enumeration was on its own enough to force
        /// <c>uav:nodes</c> onto a document that needed nothing else from it.
        /// In a document set, each DataType root owns its complete definition
        /// so other partitions do not repeat that graph declaration.
        /// </remarks>
        private static void WriteDataTypeDefinitions(
            Utf8JsonWriter writer,
            UANodeSet nodeSet,
            string defaultLocale,
            UANode? documentOwner = null)
        {
            ArrayOf<UADataType> dataTypes = documentOwner is null
                ? CollectDataTypeNodes(nodeSet)
                : documentOwner is UADataType owner ? [owner] : [];
            if (dataTypes.Count == 0)
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

        private static void WriteDataTypeDefinition(
            Utf8JsonWriter writer,
            UADataType dataType,
            UANodeSet nodeSet,
            string defaultLocale)
        {
            Export.DataTypeDefinition? definition = dataType.Definition;
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
            EncodingPresence presence = WriteEncodingIdentities(writer, dataType, nodeSet, out bool completeEncodings);
            if (!dataType.IsAbstract && definition is not null && !isEnumeration && completeEncodings)
            {
                if ((presence & EncodingPresence.Binary) == 0)
                {
                    writer.WriteBoolean("uav:hasDefaultEncoding", false);
                }
                if (presence != EncodingPresence.All)
                {
                    writer.WritePropertyName(DefaultEncodingsTerm);
                    writer.WriteStartArray();
                    if ((presence & EncodingPresence.Binary) != 0)
                    {
                        writer.WriteStringValue("Binary");
                    }
                    if ((presence & EncodingPresence.Xml) != 0)
                    {
                        writer.WriteStringValue("XML");
                    }
                    if ((presence & EncodingPresence.Json) != 0)
                    {
                        writer.WriteStringValue("JSON");
                    }
                    writer.WriteEndArray();
                }
            }
            WriteBaseDataType(writer, dataType, nodeSet);
            WriteLocalizedTextContext(
                writer, dataType.DisplayName, dataType.Description, defaultLocale, inheritedLanguageMayDiffer: true);
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
        private static bool HasEnumFields(Export.DataTypeDefinition definition)
        {
            if (definition.IsOptionSet)
            {
                return true;
            }
            if (definition.Field is null || definition.Field.Length == 0)
            {
                return false;
            }
            foreach (DataTypeField field in definition.Field)
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
            Export.DataTypeDefinition? definition,
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
        private static string StructureTypeName(Export.DataTypeDefinition definition)
        {
            bool allowsSubtypes = false;
            bool hasOptional = false;
            if (definition.Field is not null)
            {
                foreach (DataTypeField field in definition.Field)
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
            Export.DataTypeDefinition definition,
            string defaultLocale)
        {
            writer.WritePropertyName("uav:enumFields");
            writer.WriteStartArray();
            if (definition.Field is not null)
            {
                foreach (DataTypeField field in definition.Field)
                {
                    writer.WriteStartObject();
                    writer.WriteString("@type", "uav:EnumField");
                    writer.WriteString("uav:enumName", field.Name);
                    writer.WriteNumber("uav:enumValue", field.Value);
                    WriteLocalizedTextContext(
                        writer, field.DisplayName, field.Description, defaultLocale, inheritedLanguageMayDiffer: true);
                    WriteLocalizedTitle(writer, field.DisplayName, defaultLocale);
                    WriteLocalizedDescription(writer, field.Description, defaultLocale);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
        }

        private static void WriteStructureFields(
            Utf8JsonWriter writer,
            Export.DataTypeDefinition definition,
            UANodeSet nodeSet,
            string defaultLocale)
        {
            writer.WritePropertyName("uav:fields");
            writer.WriteStartArray();
            if (definition.Field is not null)
            {
                foreach (DataTypeField field in definition.Field)
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
                    WriteLocalizedTextContext(
                        writer, field.DisplayName, field.Description, defaultLocale, inheritedLanguageMayDiffer: true);
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
                    NumberStyles.Integer,
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
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<UANode> items,
            HashSet<string> nestedOnly,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            foreach (KeyValuePair<string, List<(WotDocument Document, JsonElement Schema)>> entry in
                context.InferredSchemas)
            {
                (WotDocument owner, JsonElement schema) = entry.Value[0];
                string identity = ToNodeSetNodeId(entry.Key, nodeSet, diagnostics);
                if (IsEncodingSuppressed(schema) &&
                    GetElementString(ReadDataTypeElementSchema(schema), "type") == "object" &&
                    !GetElementBool(schema, "uav:isAbstract"))
                {
                    nestedOnly.Add(identity);
                }
                if (!context.IsEmissionOwner(document, owner))
                {
                    continue;
                }
                var inferred = new List<UANode>();
                InferDataType(
                    owner, schema, identity, context, nodeSet, inferred, diagnostics);
                for (int index = 1; index < entry.Value.Count; index++)
                {
                    (WotDocument candidateOwner, JsonElement candidateSchema) = entry.Value[index];
                    var candidate = new List<UANode>();
                    InferDataType(
                        candidateOwner, candidateSchema, identity, context, nodeSet, candidate, diagnostics);
                    if (!NodeSetComparer.CompareEquivalent(
                        new UANodeSet { NamespaceUris = nodeSet.NamespaceUris, Items = [.. inferred] },
                        new UANodeSet { NamespaceUris = nodeSet.NamespaceUris, Items = [.. candidate] },
                        options.ToComparisonOptions()).AreEquivalent)
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.DataTypeDefinitionInvalid,
                            $"The inferred DataType '{GetElementString(schema, "uav:dataTypeName")}' " +
                            "has conflicting materialized definitions.",
                            new WotLocation(reference: entry.Key)));
                    }
                }
                if (inferred.Find(node => node is UADataType) is UADataType produced)
                {
                    if (!TryGetMatchingDataTypeRoot(
                        document, identity, produced.BrowseName!, produced.IsAbstract,
                        items, diagnostics, out UADataType? root))
                    {
                        continue;
                    }
                    if (root is not null)
                    {
                        root.IsAbstract = produced.IsAbstract;
                        root.Definition = produced.Definition;
                        ApplyDataTypeText(owner, root, schema);
                        var references = new List<Reference>(root.References ?? []);
                        foreach (Reference reference in produced.References ?? [])
                        {
                            if (reference.ReferenceType == "HasSubtype" && !reference.IsForward)
                            {
                                SetSuperType(references, reference.Value!);
                            }
                            else
                            {
                                references.Add(reference);
                            }
                        }
                        root.References = [.. references];
                        inferred.Remove(produced);
                    }
                }
                items.AddRange(inferred);
            }
            ValidateAuthoritativeDataSchemas(document, context, nodeSet, diagnostics, options.MaxJsonDepth);
        }

        private static void InferDataType(
            WotDocument document,
            JsonElement schema,
            string identity,
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<UANode> items,
            List<WotDiagnostic> diagnostics)
        {
            string name = GetElementString(schema, "uav:dataTypeName")!;
            JsonElement elementSchema = ReadDataTypeElementSchema(schema);
            bool isEnumeration = elementSchema.TryGetProperty("oneOf", out JsonElement branches) &&
                IsEnumerationBranches(branches);
            bool isStructure = string.Equals(
                GetElementString(elementSchema, "type"), "object", StringComparison.Ordinal);
            if (!isEnumeration && !isStructure)
            {
                InferSimpleDataType(document, schema, name, identity, context, nodeSet, items, diagnostics);
                return;
            }

            var dataType = new UADataType
            {
                NodeId = identity,
                BrowseName = ToNodeSetQualifiedName(document, name, nodeSet, diagnostics, schema),
                IsAbstract = GetElementBool(schema, "uav:isAbstract")
            };
            ApplyDataTypeText(document, dataType, schema);
            string kind = isEnumeration ? "uav:EnumDefinition" : "uav:StructureDefinition";
            var references = new List<Reference>
            {
                new()
                {
                    ReferenceType = "HasSubtype",
                    IsForward = false,
                    Value = ResolveBaseDataType(document, schema, kind, context, nodeSet, diagnostics)
                }
            };

            dataType.Definition = isEnumeration
                ? BuildInferredEnumeration(
                    document, elementSchema, dataType.BrowseName!, branches, diagnostics)
                : BuildInferredStructure(
                    document, elementSchema, dataType.BrowseName!, name, context, nodeSet, diagnostics,
                    union: IsUnionStructure(schema));
            if (dataType.Definition is null)
            {
                return;
            }
            bool declared = ExposesDefaultEncoding(schema, name, dataType.IsAbstract, kind, diagnostics);
            EncodingPresence presence = ReadEncodingPresence(
                schema, name, dataType.IsAbstract, kind, declared, diagnostics);
            if (!isEnumeration && !dataType.IsAbstract && presence != EncodingPresence.None)
            {
                AppendEncodings(schema, identity, identity, references, items, nodeSet, diagnostics, presence);
            }
            else if (dataType.IsAbstract)
            {
                RejectEncodingIdsOnAbstractType(schema, diagnostics, name);
            }
            dataType.References = [.. references];
            items.Add(dataType);
        }

        private static JsonElement ReadDataTypeElementSchema(JsonElement schema)
        {
            while (GetElementString(schema, "type") == "array" &&
                schema.TryGetProperty("items", out JsonElement items) &&
                items.ValueKind == JsonValueKind.Object)
            {
                schema = items;
            }
            return schema;
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
            DataTypeDefinitionContext context,
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
                document, declared, context, nodeSet, diagnostics, schema);
            if (baseType is null)
            {
                return;
            }
            var dataType = new UADataType
            {
                NodeId = identity,
                BrowseName = ToNodeSetQualifiedName(document, name, nodeSet, diagnostics, schema),
                IsAbstract = GetElementBool(schema, "uav:isAbstract"),
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
            ApplyDataTypeText(document, dataType, schema);
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

        private static Export.DataTypeDefinition BuildInferredEnumeration(
            WotDocument document,
            JsonElement schema,
            string browseName,
            JsonElement branches,
            List<WotDiagnostic> diagnostics)
        {
            var fields = new List<DataTypeField>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var values = new HashSet<int>();
            int index = 0;
            foreach (JsonElement branch in branches.EnumerateArray())
            {
                string? name = GetElementString(branch, "uav:enumName");
                int? value = GetElementInt32(branch, "const");
                if (string.IsNullOrEmpty(name) || !names.Add(name) || value is null || !values.Add(value.Value))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"The inferred enumeration '{browseName}' requires distinct non-empty names and " +
                        "distinct Int32 const values for every oneOf branch.",
                        DataTypeValidationLocation(document, schema,
                            "oneOf/" + index.ToString(CultureInfo.InvariantCulture), browseName)));
                    index++;
                    continue;
                }
                var field = new DataTypeField
                {
                    Name = name,
                    Value = value.Value
                };
                ApplyFieldText(document, field, branch);
                fields.Add(field);
                index++;
            }
            return new Export.DataTypeDefinition
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
        private static Export.DataTypeDefinition? BuildInferredStructure(
            WotDocument document,
            JsonElement schema,
            string browseName,
            string name,
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics,
            bool? union = null)
        {
            if (!schema.TryGetProperty("properties", out JsonElement properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The named DataType '{name}' has no object property schemas from which to infer its fields.",
                    new WotLocation(reference: name)));
                return null;
            }
            List<string>? order = ReadFieldOrder(document, schema, properties, name, diagnostics);
            if (order is null)
            {
                return null;
            }
            HashSet<string> required = ReadRequiredFields(schema);
            bool isUnion = union ?? IsUnionStructure(schema);
            if (schema.TryGetProperty("required", out JsonElement requiredMembers))
            {
                bool valid = requiredMembers.ValueKind == JsonValueKind.Array &&
                    requiredMembers.GetArrayLength() == required.Count;
                foreach (string field in required)
                {
                    valid &= properties.TryGetProperty(field, out _);
                }
                if (!valid)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"The required fields of '{name}' must be distinct declared properties.",
                        DataTypeValidationLocation(document, schema, "required", name)));
                    return null;
                }
            }
            var fields = new List<DataTypeField>();
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
                    document, fieldSchema, name, fieldName, context, nodeSet, diagnostics);
                if (fieldDataType is null)
                {
                    return null;
                }
                var field = new DataTypeField
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
                ApplyFieldText(document, field, fieldSchema);
                fields.Add(field);
            }
            return new Export.DataTypeDefinition
            {
                Name = browseName,
                IsUnion = isUnion,
                Field = [.. fields]
            };
        }

        private static List<string>? ReadFieldOrder(
            WotDocument document,
            JsonElement schema,
            JsonElement properties,
            string name,
            List<WotDiagnostic> diagnostics)
        {
            var declared = new List<string>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    Report($"contains the repeated property '{property.Name}'");
                    return null;
                }
            }
            if (schema.TryGetProperty("uav:fieldOrder", out JsonElement order))
            {
                if (order.ValueKind != JsonValueKind.Array)
                {
                    Report("is not an array");
                    return null;
                }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement entry in order.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.String ||
                        entry.GetString() is not { Length: > 0 } fieldName)
                    {
                        Report("contains a member that is not a non-empty field name");
                        return null;
                    }
                    if (!names.Contains(fieldName))
                    {
                        Report($"names '{fieldName}', which the schema does not define");
                        return null;
                    }
                    if (!seen.Add(fieldName))
                    {
                        Report($"repeats the property '{fieldName}'");
                        return null;
                    }
                    declared.Add(fieldName);
                }
                if (seen.Count != names.Count)
                {
                    Report($"omits {names.Count - seen.Count} declared property name(s)");
                    return null;
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

            void Report(string detail)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The uav:fieldOrder of '{name}' {detail}; it shall list every property exactly once.",
                    DataTypeValidationLocation(document, schema, "uav:fieldOrder", name)));
            }
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
            DataTypeDefinitionContext context,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (NamesDataType(fieldSchema))
            {
                return MapJsonSchemaToDataType(document, fieldSchema, nodeSet, diagnostics, context);
            }
            if (GetElementString(fieldSchema, "type") == "array" &&
                fieldSchema.TryGetProperty("items", out JsonElement element) &&
                element.ValueKind == JsonValueKind.Object)
            {
                return InferFieldDataType(document, element, name, fieldName, context, nodeSet, diagnostics);
            }
            string? jsonType = GetElementString(fieldSchema, "type");
            if (jsonType is "integer" or "number")
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

        /// <summary>
        /// Holds closure-wide portable allocations that each output localizes in its own namespace table.
        /// </summary>
        private sealed class DataTypeDefinitionContext(
            Dictionary<string, JsonElement> definitions,
            Dictionary<string, WotDocument> owners,
            Dictionary<string, string> identities,
            HashSet<WotDocument> emissionOwners,
            WotDocument primaryOwner)
        {
            public Dictionary<string, JsonElement> Definitions { get; } = definitions;

            public Dictionary<string, WotDocument> Owners { get; } = owners;

            public Dictionary<string, string> Identities { get; } = identities;

            public Dictionary<JsonElement, string> SchemaIdentities { get; } = [];

            public Dictionary<string, List<(WotDocument Document, JsonElement Schema)>> InferredSchemas { get; } =
                new(StringComparer.Ordinal);

            public Dictionary<(string NamespaceUri, string Name), string?> NamedIdentities { get; } = [];

            public Dictionary<string, (WotDocument Document, UANode Node)> ProducedNodes { get; } =
                new(StringComparer.Ordinal);

            public Dictionary<string, DataTypeValidationNode> ValidationTypes { get; } =
                new(StringComparer.Ordinal);

            public Dictionary<string, (bool Valid, string Terminal)> ScalarTerminals { get; } =
                new(StringComparer.Ordinal);

            public bool IsEmissionOwner(WotDocument document, WotDocument definitionOwner)
            {
                return ReferenceEquals(
                    document, emissionOwners.Contains(definitionOwner) ? definitionOwner : primaryOwner);
            }

            public void AddName(WotDocument document, JsonElement schema, string name, string identity)
            {
                if (TrySplitCompactName(document, name, out string namespaceUri, out string local, schema))
                {
                    (string namespaceUri, string local) key = (namespaceUri, local);
                    NamedIdentities[key] =
                        NamedIdentities.TryGetValue(key, out string? existing) && existing != identity
                        ? null
                        : identity;
                }
            }
        }
    }
}
