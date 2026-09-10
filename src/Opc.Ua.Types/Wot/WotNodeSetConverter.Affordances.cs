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
using System.Text.Json;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// Maps authored interactions to their actual converted local Nodes.
        /// Shared identity allocation supplies candidates; the produced NodeSet
        /// must confirm the expected NodeClass. An unauthored identity can also
        /// resolve through one unambiguous qualified declaration owned by the root.
        /// </summary>
        public static ArrayOf<WotConvertedAffordance> ResolveAffordanceNodes(
            WotDocument document,
            UANodeSet nodeSet,
            ExpandedNodeId rootNodeId = default)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            if (nodeSet is null)
            {
                throw new ArgumentNullException(nameof(nodeSet));
            }
            if (rootNodeId.IsNull)
            {
                rootNodeId = TrySelectProjectionRoot(nodeSet);
            }
            else
            {
                rootNodeId = ReadIdentity(rootNodeId.ToString());
            }
            var nodes = new Dictionary<ExpandedNodeId, UANode>();
            foreach (UANode node in nodeSet.Items ?? [])
            {
                ExpandedNodeId identity = ReadIdentity(node.NodeId);
                if (!nodes.TryAdd(identity, node))
                {
                    throw new FormatException("The converted NodeSet contains duplicate identities.");
                }
            }
            string modelUri = GeneratedNamespaceUri(nodeSet);
            string rootLocal = LocalName(GetUavString(document, "browseName")) ??
                SanitizeName(document.Title) ?? "Thing";
            WotReferenceTypeNames references = WotReferenceTypeNames.Build(nodeSet);
            var payloadDiagnostics = new List<WotDiagnostic>();
            DataTypeDefinitionContext? payloadTypes = null;
            var mapped = new List<WotConvertedAffordance>();
            Add(document.Properties, WotAffordanceKind.Property, "properties");
            Add(document.Actions, WotAffordanceKind.Action, "actions");
            Add(document.Events, WotAffordanceKind.Event, "events");
            return mapped.ToArrayOf();

            ExpandedNodeId ReadIdentity(string? identity)
            {
                string? portable = ToPortableNodeId(identity, nodeSet.NamespaceUris);
                if (string.IsNullOrEmpty(portable) ||
                    !ExpandedNodeId.TryParse(portable, out ExpandedNodeId result) || result.IsNull ||
                    result.ServerIndex != 0)
                {
                    throw new FormatException("A converted Node has no valid local identity.");
                }
                return result.NamespaceUri == WotVocabulary.OpcUaNamespace && result.ServerIndex == 0
                    ? new ExpandedNodeId(result.InnerNodeId.WithNamespaceIndex(0))
                    : result;
            }

            void Add(IReadOnlyDictionary<string, JsonElement> affordances, WotAffordanceKind kind, string collection)
            {
                foreach (KeyValuePair<string, JsonElement> member in affordances)
                {
                    JsonElement schema = member.Value;
                    if (schema.ValueKind != JsonValueKind.Object ||
                        (kind == WotAffordanceKind.Event && schema.TryGetProperty("@id", out _)))
                    {
                        continue;
                    }
                    (string namespaceUri, string local) = ResolveDeclarationName(
                        document, schema, member.Key, modelUri);
                    ExpandedNodeId candidate = ReadIdentity(
                        DeclarationNodeId(document, schema, modelUri, rootLocal, local));
                    string pointer = "/" + collection + "/" + EscapePointerToken(member.Key);
                    nodes.TryGetValue(candidate, out UANode? node);
                    if (node is null && GetElementString(schema, "uav:id") is null)
                    {
                        node = FindOwnedNode(namespaceUri, local, kind, pointer);
                        if (node is not null)
                        {
                            candidate = ReadIdentity(node.NodeId);
                        }
                    }
                    if (node is null || !MatchesKind(node, kind))
                    {
                        throw new FormatException(
                            $"The converted NodeSet does not contain the expected interaction at '{pointer}'.");
                    }
                    ExpandedNodeId owner = node is UAInstance { ParentNodeId: { Length: > 0 } parent }
                        ? ReadIdentity(parent) : rootNodeId;
                    if (!document.TryGetPayloadSchema(kind, schema, out WotPayloadSchema? payload))
                    {
                        payloadTypes ??= CreateDataTypeDefinitionContext(
                            document, nodeSet, [], [], payloadDiagnostics);
                        payload = CapturePayloadSchema(
                            document, kind, schema, nodeSet, payloadTypes, new List<WotDiagnostic>(payloadDiagnostics));
                    }
                    mapped.Add(new WotConvertedAffordance(kind, member.Key, pointer, candidate, owner, schema)
                    {
                        PayloadSchema = payload
                    });
                }
            }

            UANode? FindOwnedNode(
                string namespaceUri, string name, WotAffordanceKind kind, string pointer)
            {
                UANode? found = null;
                foreach (UANode node in nodes.Values)
                {
                    if (!MatchesKind(node, kind) || string.IsNullOrEmpty(node.BrowseName))
                    {
                        continue;
                    }
                    QualifiedName qualified = QualifiedName.Parse(node.BrowseName);
                    string? nodeNamespace = qualified.NamespaceIndex == 0
                        ? WotVocabulary.OpcUaNamespace
                        : nodeSet.NamespaceUris is { } uris && qualified.NamespaceIndex <= uris.Length
                            ? uris[qualified.NamespaceIndex - 1] : null;
                    if (qualified.Name != name || nodeNamespace != namespaceUri || !IsOwned(node, kind))
                    {
                        continue;
                    }
                    if (found is not null)
                    {
                        throw new FormatException($"More than one converted Node matches '{pointer}'.");
                    }
                    found = node;
                }
                return found;
            }

            bool IsOwned(UANode node, WotAffordanceKind kind)
            {
                if (node is UAInstance { ParentNodeId: { Length: > 0 } parent } &&
                    ReadIdentity(parent) == rootNodeId)
                {
                    return true;
                }
                if (!nodes.TryGetValue(rootNodeId, out UANode? root))
                {
                    return false;
                }
                ExpandedNodeId identity = ReadIdentity(node.NodeId);
                foreach (Reference reference in references.GetReferences(root))
                {
                    if (reference.IsForward && ReadIdentity(reference.Value) == identity &&
                        (kind == WotAffordanceKind.Event
                            ? references.TryGetIdentifier(reference.ReferenceType, out string referenceId) &&
                                referenceId == WotVocabulary.GeneratesEvent
                            : references.IsOwnershipReference(reference.ReferenceType)))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private static bool MatchesKind(UANode node, WotAffordanceKind kind)
        {
            return kind switch
            {
                WotAffordanceKind.Property => node is UAVariable,
                WotAffordanceKind.Action => node is UAMethod,
                WotAffordanceKind.Event => node is UAObjectType,
                _ => false
            };
        }
    }
}
