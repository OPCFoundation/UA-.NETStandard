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
using System.Linq;
using System.Text.Json;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Tests.Samples
{
    internal static partial class WotAggregationDocumentGenerator
    {
        private sealed class DocumentDependencyIndex
        {
            public DocumentDependencyIndex(ArrayOf<SampleDocument> documents)
            {
                foreach (SampleDocument document in documents)
                {
                    using var parsed = WotDocument.Parse(document.Json.ToArray(), CreateLargeDocumentOptions());
                    JsonElement root = parsed.RootElement.Clone();
                    var localNodes = new HashSet<string>(StringComparer.Ordinal);
                    AddDeclaredNodes(root, localNodes);
                    var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (root.TryGetProperty("@context", out JsonElement context))
                    {
                        ReadPrefixes(context, prefixes);
                    }
                    m_documents.Add(document.ResourceId, new DependencyDocument(root, localNodes, prefixes));
                    AddDocumentAlias(document.ResourceId, document.ResourceId);
                    AddDocumentAlias(document.Path, document.ResourceId);
                    foreach (string member in s_documentIdentityTerms)
                    {
                        if (ReadString(root, member) is { Length: > 0 } identity)
                        {
                            AddDocumentAlias(identity, document.ResourceId);
                        }
                    }

                    string? nodeId = ReadString(root, "uav:id");
                    if (nodeId is null || !TryNormalizeNodeId(nodeId, out string normalized))
                    {
                        continue;
                    }
                    bool isType = HasType(root, "uav:objectType") || HasType(root, "uav:variableType") ||
                        HasType(root, "uav:dataType") || HasType(root, "uav:referenceType") ||
                        HasType(root, "uav:eventType");
                    var owner = new NodeOwner(document.ResourceId, isType,
                        HasType(root, "uav:referenceType"), isRoot: true);
                    if (!m_nodeOwners.TryAdd(normalized, owner))
                    {
                        throw new InvalidOperationException(
                            $"Multiple root documents own the local node '{normalized}'.");
                    }
                    if (isType && WotNodeSetConverter.TryDescribeProjectedType(
                        parsed, out string namespaceUri, out string browseName, out _))
                    {
                        AddName(namespaceUri, browseName, normalized);
                        if (WotNodeSetConverter.TryDescribeProjectedReferenceType(
                            parsed, out _, out _, out string inverseName, out _, out _) &&
                            inverseName.Length != 0)
                        {
                            AddName(namespaceUri, inverseName, normalized);
                        }
                    }
                }

                foreach ((string resourceId, DependencyDocument document) in m_documents)
                {
                    foreach (string mapName in s_ownedAffordanceMaps)
                    {
                        if (document.Root.TryGetProperty(mapName, out JsonElement map))
                        {
                            IndexAffordances(map, resourceId);
                        }
                    }
                }
            }

            public ArrayOf<string> GetDependencies(string resourceId)
            {
                DependencyDocument document = m_documents[resourceId];
                var dependencies = new SortedSet<string>(StringComparer.Ordinal);
                Collect(document.Root);
                return dependencies.ToArrayOf();

                void Collect(JsonElement value)
                {
                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in value.EnumerateArray())
                        {
                            Collect(item);
                        }
                        return;
                    }
                    if (value.ValueKind != JsonValueKind.Object)
                    {
                        return;
                    }
                    string? relation = ReadString(value, "rel");
                    bool pinnedType = value.TryGetProperty("uav:typeDefinitionId", out _) ||
                        HasTypeDefinitionLink(value);
                    foreach (JsonProperty property in value.EnumerateObject())
                    {
                        if (property.Name is "forms" or "@context" or "const" or "default" or "enum" or "examples")
                        {
                            continue;
                        }
                        if (property.Name is "href" or "tm:ref" or "uav:componentOf" or "uav:propertyOf")
                        {
                            bool requireNode = property.Name != "href" ||
                                relation is "uav:componentOf" or "ua:ComponentOf" or "tm:extends" or
                                    "ua:HasTypeDefinition" or "ua:HasInterface";
                            foreach (string reference in Strings(property.Value))
                            {
                                AddReference(reference, requireNode);
                            }
                        }
                        else if (s_nodeReferenceTerms.Contains(property.Name))
                        {
                            foreach (string reference in Strings(property.Value))
                            {
                                AddNodeReference(reference, requireNode: true);
                            }
                        }
                        else if (property.Name == "@type" && !pinnedType ||
                            property.Name == "rel" && !value.TryGetProperty("uav:refId", out _))
                        {
                            foreach (string name in Strings(property.Value))
                            {
                                AddNamedReference(name, property.Name == "rel");
                            }
                        }
                        Collect(property.Value);
                    }
                }

                void AddReference(string reference, bool requireNode)
                {
                    // A string NodeId may itself contain '#'. Only document
                    // references are split into a location and JSON Pointer.
                    if (TryNormalizeNodeId(reference, out _))
                    {
                        AddNodeReference(reference, requireNode);
                        return;
                    }
                    if (m_documentAliases.TryGetValue(reference, out string? exact))
                    {
                        AddDependency(exact);
                        return;
                    }
                    int fragment = reference.IndexOf('#', StringComparison.Ordinal);
                    string href = fragment < 0 ? reference : reference[..fragment];
                    if (href.StartsWith("./", StringComparison.Ordinal))
                    {
                        href = href[2..];
                    }
                    if (href.Length == 0)
                    {
                        return;
                    }
                    if (m_documentAliases.TryGetValue(href, out string? owner))
                    {
                        AddDependency(owner);
                    }
                    else if (!Uri.TryCreate(href, UriKind.Absolute, out _))
                    {
                        throw new InvalidOperationException(
                            $"'{resourceId}' references missing sample document '{href}'.");
                    }
                }

                void AddNodeReference(string reference, bool requireNode)
                {
                    if (!TryNormalizeNodeId(reference, out string nodeId) ||
                        document.LocalNodes.Contains(nodeId))
                    {
                        return;
                    }
                    if (!m_nodeOwners.TryGetValue(nodeId, out NodeOwner? owner))
                    {
                        if (requireNode && ExpandedNodeId.TryParse(nodeId, out ExpandedNodeId target) &&
                            !string.IsNullOrEmpty(target.NamespaceUri))
                        {
                            throw new InvalidOperationException(
                                $"'{resourceId}' references node '{nodeId}' without a document owner.");
                        }
                        return;
                    }
                    if (!requireNode)
                    {
                        return;
                    }
                    // Only type bindings and parent resolution require the
                    // target first. Inverse interface/subtype edges are ordinary
                    // references, not dependencies back onto their consumers.
                    if (owner.Resources.Count != 1)
                    {
                        throw new InvalidOperationException($"Ambiguous document owner for node '{nodeId}'.");
                    }
                    AddDependency(owner.Resources.Single());
                }

                void AddNamedReference(string name, bool referenceType)
                {
                    int separator = name.IndexOf(':', StringComparison.Ordinal);
                    if (separator <= 0 ||
                        !document.Prefixes.TryGetValue(name[..separator], out string? namespaceUri) ||
                        !m_names.TryGetValue((namespaceUri, name[(separator + 1)..]), out HashSet<string>? matches))
                    {
                        return;
                    }
                    string[] candidates = [.. matches.Where(node =>
                        m_nodeOwners[node].IsReferenceType == referenceType)];
                    if (candidates.Length > 1)
                    {
                        throw new InvalidOperationException(
                            $"'{resourceId}' has an ambiguous model reference '{name}'.");
                    }
                    if (candidates.Length == 1)
                    {
                        AddNodeReference(candidates[0], requireNode: true);
                    }
                }

                void AddDependency(string owner)
                {
                    if (!string.Equals(owner, resourceId, StringComparison.Ordinal))
                    {
                        dependencies.Add(owner);
                    }
                }
            }

            private void AddDocumentAlias(string alias, string resourceId)
            {
                if (m_documentAliases.TryGetValue(alias, out string? previous) && previous != resourceId)
                {
                    throw new InvalidOperationException($"Ambiguous sample document alias '{alias}'.");
                }
                m_documentAliases[alias] = resourceId;
            }

            private void AddName(string namespaceUri, string name, string nodeId)
            {
                if (!m_names.TryGetValue((namespaceUri, name), out HashSet<string>? nodes))
                {
                    nodes = new HashSet<string>(StringComparer.Ordinal);
                    m_names.Add((namespaceUri, name), nodes);
                }
                nodes.Add(nodeId);
            }

            private void IndexAffordances(JsonElement map, string resourceId)
            {
                if (map.ValueKind != JsonValueKind.Object)
                {
                    return;
                }
                foreach (JsonProperty member in map.EnumerateObject())
                {
                    if (member.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    if (ReadString(member.Value, "uav:id") is { } id &&
                        TryNormalizeNodeId(id, out string nodeId))
                    {
                        if (!m_nodeOwners.TryGetValue(nodeId, out NodeOwner? owner))
                        {
                            m_nodeOwners.Add(nodeId, new NodeOwner(
                                resourceId, isType: false, isReferenceType: false, isRoot: false));
                        }
                        else if (!owner.IsRoot)
                        {
                            owner.Resources.Add(resourceId);
                        }
                    }
                    if (member.Value.TryGetProperty("properties", out JsonElement nested))
                    {
                        IndexAffordances(nested, resourceId);
                    }
                }
            }

            private static void AddDeclaredNodes(JsonElement root, HashSet<string> nodes)
            {
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return;
                }
                AddIdentity(ReadString(root, "uav:id"));
                foreach (string mapName in s_ownedAffordanceMaps)
                {
                    if (root.TryGetProperty(mapName, out JsonElement map) && map.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty member in map.EnumerateObject())
                        {
                            AddDeclaredNodes(member.Value, nodes);
                        }
                    }
                }
                if (root.TryGetProperty("uav:dataTypeDefinitions", out JsonElement definitions) &&
                    definitions.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement definition in definitions.EnumerateArray())
                    {
                        foreach (string term in s_dataTypeDeclarationTerms)
                        {
                            AddIdentity(ReadString(definition, term));
                        }
                    }
                }

                void AddIdentity(string? id)
                {
                    if (id is not null && TryNormalizeNodeId(id, out string normalized))
                    {
                        nodes.Add(normalized);
                    }
                }
            }

            private static void ReadPrefixes(JsonElement context, Dictionary<string, string> prefixes)
            {
                if (context.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in context.EnumerateArray())
                    {
                        ReadPrefixes(item, prefixes);
                    }
                }
                else if (context.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in context.EnumerateObject())
                    {
                        string? value = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : ReadString(property.Value, "@id");
                        if (value is not null)
                        {
                            prefixes[property.Name] = value;
                        }
                    }
                }
            }

            private static bool HasTypeDefinitionLink(JsonElement value)
            {
                return value.TryGetProperty("links", out JsonElement links) &&
                    links.ValueKind == JsonValueKind.Array &&
                    links.EnumerateArray().Any(link => ReadString(link, "rel") == "ua:HasTypeDefinition");
            }

            private static bool HasType(JsonElement value, string type)
            {
                return value.TryGetProperty("@type", out JsonElement types) && Strings(types).Contains(type);
            }

            private static IEnumerable<string> Strings(JsonElement value)
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    yield return value.GetString()!;
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            yield return item.GetString()!;
                        }
                    }
                }
            }

            private static string? ReadString(JsonElement value, string name)
            {
                return value.ValueKind == JsonValueKind.Object &&
                    value.TryGetProperty(name, out JsonElement member) && member.ValueKind == JsonValueKind.String
                        ? member.GetString()
                        : null;
            }

            private static bool TryNormalizeNodeId(string value, out string normalized)
            {
                normalized = string.Empty;
                if (!ExpandedNodeId.TryParse(value, out ExpandedNodeId nodeId))
                {
                    return false;
                }
                if (nodeId.NamespaceIndex != 0 && string.IsNullOrEmpty(nodeId.NamespaceUri))
                {
                    throw new InvalidOperationException($"Sample reference '{value}' is not a portable NodeId.");
                }
                normalized = nodeId.NamespaceUri == Namespaces.OpcUa
                    ? nodeId.WithNamespaceUri(null).ToString()
                    : nodeId.ToString();
                return true;
            }

            private sealed record DependencyDocument(
                JsonElement Root,
                HashSet<string> LocalNodes,
                Dictionary<string, string> Prefixes);

            private sealed class NodeOwner(string resourceId, bool isType, bool isReferenceType, bool isRoot)
            {
                public HashSet<string> Resources { get; } = new(StringComparer.Ordinal) { resourceId };
                public bool IsType { get; } = isType;
                public bool IsReferenceType { get; } = isReferenceType;
                public bool IsRoot { get; } = isRoot;
            }

            private static readonly string[] s_documentIdentityTerms = ["id", "@id"];
            private static readonly string[] s_ownedAffordanceMaps = ["properties", "actions"];
            private static readonly string[] s_dataTypeDeclarationTerms =
            [
                "uav:dataTypeId", "uav:binaryEncodingId", "uav:xmlEncodingId", "uav:jsonEncodingId"
            ];
            private static readonly HashSet<string> s_nodeReferenceTerms = new(StringComparer.Ordinal)
            {
                "uav:mapToType", "uav:typeDefinitionId", "uav:conditionTypeId", "uav:refId",
                "uav:dataTypeId", "uav:fieldDataTypeId", "uav:binaryEncodingId", "uav:xmlEncodingId",
                "uav:jsonEncodingId"
            };
            private readonly Dictionary<string, DependencyDocument> m_documents = new(StringComparer.Ordinal);
            private readonly Dictionary<string, string> m_documentAliases = new(StringComparer.Ordinal);
            private readonly Dictionary<string, NodeOwner> m_nodeOwners = new(StringComparer.Ordinal);
            private readonly Dictionary<(string NamespaceUri, string BrowseName), HashSet<string>> m_names = [];
        }
    }
}
