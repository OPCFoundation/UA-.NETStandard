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
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    public static partial class WotDependencyGraph
    {
        /// <summary>
        /// Indexes semantic dependencies without changing the original document or performing I/O.
        /// This is dependency metadata, not a substitute for format or native admission validation.
        /// </summary>
        public static WotResourceDependencies ReadMetadata(ByteString content, int maxJsonDepth)
        {
            var references = new List<WotResourceReference>();
            var defined = new HashSet<string>(StringComparer.Ordinal);
            var definitions = new HashSet<string>(StringComparer.Ordinal);
            var definitionNames = new HashSet<string>(StringComparer.Ordinal);
            var owners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var required = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            string error = string.Empty;
            try
            {
                using WotDocument document = WotDocument.Parse(content.Memory, new WotNodeSetConverterOptions
                {
                    MaxJsonDepth = maxJsonDepth,
                    MaxJsonDocumentSize = Math.Max(content.Length, 1)
                });
                CollectSemanticReferences(
                    document, document.RootElement, references, defined, definitions, definitionNames);
                CollectModelMembership(string.Empty, content.Memory, maxJsonDepth, owners, required);
            }
            catch (Exception exception) when (exception is JsonException or FormatException)
            {
                error = exception.Message;
            }
            return new WotResourceDependencies(
                WotContentDigest.Compute(content),
                references.ToArrayOf(),
                owners.Keys.OrderBy(uri => uri, StringComparer.Ordinal).ToArrayOf(),
                required.Values.SelectMany(uris => uris).Distinct(StringComparer.Ordinal)
                    .OrderBy(uri => uri, StringComparer.Ordinal).ToArrayOf(),
                defined.OrderBy(identifier => identifier, StringComparer.Ordinal).ToArrayOf(),
                error,
                definitions.OrderBy(identifier => identifier, StringComparer.Ordinal).ToArrayOf(),
                definitionNames.OrderBy(name => name, StringComparer.Ordinal).ToArrayOf());
        }

        private static void CollectSemanticReferences(
            WotDocument document,
            JsonElement element,
            List<WotResourceReference> references,
            HashSet<string> defined,
            HashSet<string> definitions,
            HashSet<string> definitionNames,
            string referenceKind = "tm:ref")
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                {
                    CollectSemanticReferences(
                        document, child, references, defined, definitions, definitionNames, referenceKind);
                }
                return;
            }
            if (element.ValueKind != JsonValueKind.Object ||
                element.TryGetProperty("@value", out _))
            {
                return;
            }
            bool declaresDataType = IsDataTypeDefinition(document, element);
            if (declaresDataType)
            {
                string? declaredName = null;
                bool explicitIdentity = false;
                if (element.TryGetProperty("@id", out JsonElement identity) && identity.ValueKind == JsonValueKind.String)
                {
                    definitions.Add(ExpandReference(document, identity.GetString()!, element));
                }
                foreach (JsonProperty member in element.EnumerateObject())
                {
                    string term = SemanticTerm(document, member.Name, element);
                    if (term == "uav:dataTypeName" && member.Value.ValueKind == JsonValueKind.String)
                    {
                        declaredName = member.Value.GetString();
                        if (TryDataTypeName(document, element, member.Value, out string name))
                        {
                            definitionNames.Add(name);
                        }
                    }
                    else if (term == "uav:dataTypeId")
                    {
                        explicitIdentity = true;
                        if (TryDataTypeNodeId(document, element, member.Value, out string nodeId))
                        {
                            defined.Add(nodeId);
                        }
                    }
                }
                if (!explicitIdentity && declaredName is not null &&
                    WotNodeSetConverter.TryDeriveDataTypeNodeId(
                        document, declaredName, out ExpandedNodeId derived, element))
                {
                    defined.Add(derived.ToString());
                }
            }
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name == "@context" ||
                    WotBindingConformance.OpaqueMembers.Contains(property.Name))
                {
                    continue;
                }
                string term = SemanticTerm(document, property.Name, element);
                if (term is "uav:nodes" or "uav:nodeSet" ||
                    WotBindingConformance.OpaqueMembers.Contains(term) ||
                    (document.TryGetContextTerm(property.Name, out JsonElement definition, element) &&
                        definition.ValueKind == JsonValueKind.Object &&
                        definition.TryGetProperty("@type", out JsonElement type) &&
                        type.ValueKind == JsonValueKind.String && type.GetString() == "@json"))
                {
                    continue;
                }
                switch (term)
                {
                    case "uav:id":
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            defined.Add(ExpandReference(document, property.Value.GetString()!, element));
                        }
                        break;
                    case "links":
                        CollectSemanticLinks(document, property.Value, references);
                        break;
                    case "tm:extends":
                    case "uav:componentOf":
                        AddSemanticTargets(document, element, property.Value, term, references);
                        break;
                    case "tm:ref":
                        AddSemanticTargets(document, element, property.Value, referenceKind, references);
                        break;
                    case "uav:dataTypeName":
                    case "uav:fieldDataTypeName":
                        if (!declaresDataType &&
                            TryDataTypeName(document, element, property.Value, out string qualified))
                        {
                            references.Add(new WotResourceReference(
                                property.Value.GetString()!, qualified, term, false,
                                WotResourceReferenceLookup.DataTypeName));
                        }
                        break;
                    case "uav:dataTypeId":
                    case "uav:fieldDataTypeId":
                        if (!declaresDataType &&
                            TryDataTypeNodeId(document, element, property.Value, out string portable))
                        {
                            references.Add(new WotResourceReference(
                                property.Value.GetString()!, portable, term, false,
                                WotResourceReferenceLookup.DataTypeNodeId));
                        }
                        break;
                    case "uav:dataTypeDefinition":
                    case "uav:fieldDataTypeDefinition":
                        if (property.Value.ValueKind == JsonValueKind.Object &&
                            property.Value.TryGetProperty("@id", out JsonElement definitionId) &&
                            property.Value.EnumerateObject().All(member => member.Name is "@id" or "@context"))
                        {
                            AddSemanticTargets(document, property.Value, definitionId, term, references);
                        }
                        else
                        {
                            CollectSemanticReferences(
                                document, property.Value, references, defined, definitions,
                                definitionNames, referenceKind);
                        }
                        break;
                    case "uav:dataTypeSubtypeOf":
                        AddDataTypeBaseReference(document, element, property.Value, references);
                        break;
                    case "uav:projects":
                        if (property.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement entry in property.Value.EnumerateArray())
                            {
                                if (entry.ValueKind == JsonValueKind.Object &&
                                    entry.TryGetProperty("href", out JsonElement href))
                                {
                                    AddSemanticTargets(document, entry, href, term, references);
                                }
                            }
                        }
                        break;
                    default:
                        CollectSemanticReferences(
                            document, property.Value, references, defined, definitions, definitionNames,
                            term == "events" ? EventTypeRefType :
                            term == "uav:eventSelectClauses" ? EventSelectClauseRefType : referenceKind);
                        break;
                }
            }
        }

        private static void AddDataTypeBaseReference(
            WotDocument document, JsonElement owner, JsonElement value, List<WotResourceReference> references)
        {
            const string relation = "uav:dataTypeSubtypeOf";
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty member in value.EnumerateObject())
                {
                    string term = SemanticTerm(document, member.Name, value);
                    if (term == "@id")
                    {
                        AddSemanticTargets(document, value, member.Value, relation, references);
                    }
                    else if (term is "uav:dataTypeName" or "uav:dataTypeId")
                    {
                        AddDataTypeBaseReference(document, value, member.Value, references);
                    }
                }
                return;
            }
            if (value.ValueKind != JsonValueKind.String)
            {
                return;
            }
            if (TryDataTypeNodeId(document, owner, value, out string nodeId))
            {
                references.Add(new WotResourceReference(
                    value.GetString()!, nodeId, relation, true, WotResourceReferenceLookup.DataTypeNodeId));
            }
            else if (TryDataTypeName(document, owner, value, out string name))
            {
                references.Add(new WotResourceReference(
                    value.GetString()!, name, relation, true, WotResourceReferenceLookup.DataTypeName));
            }
            else
            {
                AddSemanticTargets(document, owner, value, relation, references);
            }
        }

        private static bool TryDataTypeName(
            WotDocument document, JsonElement owner, JsonElement value, out string name)
        {
            if (value.ValueKind == JsonValueKind.String &&
                WotNodeSetConverter.TrySplitCompactName(
                    document, value.GetString()!, out string namespaceUri, out string local, owner))
            {
                name = "nsu=" + CoreUtils.EscapeUri(namespaceUri) + ";" + local;
                return true;
            }
            name = string.Empty;
            return false;
        }

        private static bool TryDataTypeNodeId(
            WotDocument document, JsonElement owner, JsonElement value, out string nodeId)
        {
            if (value.ValueKind == JsonValueKind.String &&
                ExpandedNodeId.TryParse(ExpandReference(document, value.GetString()!, owner), out ExpandedNodeId id))
            {
                nodeId = id.ToString();
                return true;
            }
            nodeId = string.Empty;
            return false;
        }

        private static bool IsDataTypeDefinition(WotDocument document, JsonElement element)
        {
            if (!element.TryGetProperty("@type", out JsonElement type))
            {
                return false;
            }
            bool IsDefinition(JsonElement value)
            {
                return value.ValueKind == JsonValueKind.String &&
                    SemanticTerm(document, value.GetString()!, element) is
                        "uav:StructureDefinition" or "uav:EnumDefinition" or "uav:SimpleDataType";
            }
            return type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Any(IsDefinition) : IsDefinition(type);
        }

        private static void CollectSemanticLinks(
            WotDocument document, JsonElement links, List<WotResourceReference> references)
        {
            if (links.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            foreach (JsonElement link in links.EnumerateArray())
            {
                if (link.ValueKind != JsonValueKind.Object ||
                    !link.TryGetProperty("href", out JsonElement href) ||
                    !link.TryGetProperty("rel", out JsonElement relation) ||
                    relation.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                string term = SemanticTerm(document, relation.GetString()!, link);
                if (term is "type" or "collection" or "item" or "tm:extends" or "tm:submodel" ||
                    term.StartsWith("ua:", StringComparison.Ordinal) || IsContainmentRelation(term) ||
                    Uri.TryCreate(term, UriKind.Absolute, out _) ||
                    link.EnumerateObject().Any(member => SemanticTerm(document, member.Name, link) == "uav:refId"))
                {
                    AddSemanticTargets(document, link, href, term, references);
                }
            }
        }

        private static void AddSemanticTargets(
            WotDocument document,
            JsonElement carryingNode,
            JsonElement target,
            string relation,
            List<WotResourceReference> references)
        {
            if (target.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in target.EnumerateArray())
                {
                    AddSemanticTargets(document, carryingNode, item, relation, references);
                }
                return;
            }
            if (target.ValueKind == JsonValueKind.Object &&
                target.TryGetProperty("href", out JsonElement href))
            {
                AddSemanticTargets(document, target, href, relation, references);
                return;
            }
            if (target.ValueKind != JsonValueKind.String)
            {
                return;
            }
            string raw = target.GetString()!;
            string lookup = ExpandReference(document, raw, carryingNode);
            if (ExpandedNodeId.TryParse(lookup, out _))
            {
                return;
            }
            references.Add(new WotResourceReference(raw, lookup, relation, IsOrderingReference(relation)));
        }

        private static bool IsOrderingReference(string relation)
        {
            return relation is "tm:extends" or "tm:submodel" or "type" or
                "ua:HasSubtype" or "ua:HasTypeDefinition" or "uav:projects" or
                "uav:dataTypeSubtypeOf" or EventTypeRefType or EventSelectClauseRefType;
        }

        private static string SemanticTerm(WotDocument document, string name, JsonElement carryingNode)
        {
            string expanded = ExpandTerm(document, name, carryingNode);
            if (expanded.StartsWith(TmNamespace, StringComparison.Ordinal))
            {
                return "tm:" + expanded[TmNamespace.Length..];
            }
            if (expanded.StartsWith(TdNamespace, StringComparison.Ordinal))
            {
                return expanded[TdNamespace.Length..];
            }
            if (expanded.StartsWith(BindingNamespace, StringComparison.Ordinal))
            {
                return "uav:" + expanded[BindingNamespace.Length..];
            }
            if (expanded.StartsWith(Ua.Namespaces.OpcUa, StringComparison.Ordinal))
            {
                return "ua:" + expanded[Ua.Namespaces.OpcUa.Length..];
            }
            return expanded;
        }

        private static string ExpandTerm(WotDocument document, string value, JsonElement carryingNode)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (seen.Add(value))
            {
                if (document.TryGetContextTerm(value, out JsonElement definition, carryingNode))
                {
                    string? identity = definition.ValueKind == JsonValueKind.String
                        ? definition.GetString()
                        : definition.ValueKind == JsonValueKind.Object &&
                            definition.TryGetProperty("@id", out JsonElement id) &&
                            id.ValueKind == JsonValueKind.String ? id.GetString() : null;
                    if (identity is null && definition.ValueKind == JsonValueKind.Object &&
                        !definition.TryGetProperty("@id", out _))
                    {
                        return value;
                    }
                    if (identity is null)
                    {
                        return string.Empty;
                    }
                    if (identity != value)
                    {
                        value = identity;
                        continue;
                    }
                }
                int colon = value.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0)
                {
                    string prefix = value[..colon];
                    if (document.TryGetContextPrefix(prefix, out string namespaceUri, carryingNode))
                    {
                        string expanded = namespaceUri + value[(colon + 1)..];
                        if (expanded != value)
                        {
                            value = expanded;
                            continue;
                        }
                    }
                    else if (prefix == "tm" && !document.TryGetContextTerm(prefix, out _, carryingNode))
                    {
                        return TmNamespace + value[(colon + 1)..];
                    }
                    else if (document.TryGetContextTerm(prefix, out _, carryingNode))
                    {
                        return string.Empty;
                    }
                }
                return value;
            }
            throw new FormatException("The dependency's context contains a cyclic identifier definition.");
        }

        private static string ExpandReference(WotDocument document, string value, JsonElement carryingNode)
        {
            string expanded = ExpandTerm(document, value, carryingNode);
            if (expanded.Length == 0 || expanded[0] is '#' or '/' ||
                ExpandedNodeId.TryParse(expanded, out _) ||
                Uri.TryCreate(expanded, UriKind.Absolute, out _))
            {
                return expanded;
            }
            string? basis = document.Id;
            foreach (JsonElement context in document.GetActiveContexts(carryingNode))
            {
                ApplyBase(context, ref basis);
            }
            return Uri.TryCreate(basis, UriKind.Absolute, out Uri? baseUri) &&
                Uri.TryCreate(expanded, UriKind.RelativeOrAbsolute, out Uri? relativeUri) &&
                Uri.TryCreate(baseUri, relativeUri, out Uri? resolved)
                    ? resolved.AbsoluteUri
                    : expanded;
        }

        private static void ApplyBase(JsonElement context, ref string? basis)
        {
            if (context.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in context.EnumerateArray())
                {
                    ApplyBase(child, ref basis);
                }
            }
            else if (context.ValueKind == JsonValueKind.Object &&
                context.TryGetProperty("@base", out JsonElement declared))
            {
                if (declared.ValueKind == JsonValueKind.Null)
                {
                    basis = null;
                }
                else if (declared.ValueKind == JsonValueKind.String)
                {
                    string value = declared.GetString()!;
                    basis = Uri.TryCreate(value, UriKind.Absolute, out _) ? value :
                        Uri.TryCreate(basis, UriKind.Absolute, out Uri? parent) &&
                        Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out Uri? relativeUri) &&
                        Uri.TryCreate(parent, relativeUri, out Uri? resolved) ? resolved.AbsoluteUri : value;
                }
            }
        }

        private const string TmNamespace = "https://www.w3.org/2019/wot/tm#";
        private const string TdNamespace = "https://www.w3.org/2019/wot/td#";
        private const string BindingNamespace = "http://opcfoundation.org/UA/WoT-Binding/";
    }
}
