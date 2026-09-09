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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Resolves names and identifiers against a fixed collection of WoT
    /// documents — the "sibling documents of the conversion" half of the local
    /// context of <i>OPC UA — WoT Binding</i> §5.1.5.
    /// </summary>
    /// <remarks>
    /// A conversion needs this to honour §5.2.1: a type binding that names a
    /// companion type fails the projection unless the type resolves, and it
    /// resolves only where the conversion is given the documents that define it.
    /// An instance of a companion model is the ordinary case — a pump states
    /// <c>HasTypeDefinition</c> to a type its own NodeSet does not define — so
    /// converting one without this resolver either mistypes every node or
    /// reports every type binding unresolved.
    /// </remarks>
    public sealed class WotDocumentNodeResolver
        : IWotNodeResolver, IWotReferenceTypeResolver, IWotTypeDeclarationResolver, IWotDataTypeDefinitionResolver
    {
        /// <summary>
        /// Initializes a resolver over the supplied documents.
        /// </summary>
        /// <param name="documents">The documents that make up the context.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="documents"/> is <c>null</c>.
        /// </exception>
        public WotDocumentNodeResolver(IEnumerable<WotDocument> documents)
        {
            if (documents is null)
            {
                throw new ArgumentNullException(nameof(documents));
            }
            foreach (WotDocument document in documents)
            {
                Index(document);
            }
        }

        /// <inheritdoc/>
        public ValueTask<bool> HoldsNamespaceAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(
                namespaceUri is not null && m_namespaces.Contains(namespaceUri));
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
            string namespaceUri,
            string browseName,
            WotExpectedNodeClass expected,
            CancellationToken cancellationToken = default)
        {
            string key = namespaceUri + "|" + browseName;
            if (!m_byBrowseName.TryGetValue(key, out List<WotResolvedNode>? matches))
            {
                return new ValueTask<ArrayOf<WotResolvedNode>>(ArrayOf<WotResolvedNode>.Empty);
            }
            var accepted = new List<WotResolvedNode>(matches.Count);
            foreach (WotResolvedNode match in matches)
            {
                WotResolvedNode current = m_byNodeId.TryGetValue(
                    WotNodeSetConverter.NormalizeExpandedNodeId(match.NodeId), out WotResolvedNode authoritative)
                    ? authoritative
                    : match;
                if (Accepts(expected, current.NodeClass))
                {
                    accepted.Add(current with { SupertypeNodeIds = GetSupertypes(current.NodeId) });
                }
            }
            return new ValueTask<ArrayOf<WotResolvedNode>>(accepted.ToArrayOf());
        }

        /// <inheritdoc/>
        public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
            string expandedNodeId,
            CancellationToken cancellationToken = default)
        {
            return ResolveByNodeIdAsync(expandedNodeId, WotExpectedNodeClass.Any, cancellationToken);
        }

        /// <summary>
        /// Resolves an identifier and rejects a match of the wrong NodeClass.
        /// </summary>
        /// <param name="nodeId">The portable ExpandedNodeId to resolve.</param>
        /// <param name="expected">The NodeClass the caller requires.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The matched node, or <c>null</c>.</returns>
        public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
            string nodeId,
            WotExpectedNodeClass expected,
            CancellationToken cancellationToken = default)
        {
            if (nodeId is not null &&
                m_byNodeId.TryGetValue(
                    WotNodeSetConverter.NormalizeExpandedNodeId(nodeId), out WotResolvedNode match) &&
                Accepts(expected, match.NodeClass))
            {
                return new ValueTask<WotResolvedNode?>(match with { SupertypeNodeIds = GetSupertypes(match.NodeId) });
            }
            return new ValueTask<WotResolvedNode?>((WotResolvedNode?)null);
        }

        private static bool Accepts(WotExpectedNodeClass expected, WotExpectedNodeClass actual)
        {
            return expected == WotExpectedNodeClass.Any || expected == actual;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A document set describes the ReferenceTypes of a companion model the
        /// same way it describes its ObjectTypes: one document per Node, with
        /// the BrowseName it is known by and — because OPC 10000-3 gives a
        /// ReferenceType a second name — its InverseName and Symmetric flag.
        /// Reading them here is what lets a document of the set state a
        /// relation of that model in either direction and have it resolve
        /// against its own siblings, before any AddressSpace is consulted.
        /// </remarks>
        public ValueTask<ArrayOf<WotResolvedReferenceType>> ResolveReferenceTypesAsync(
            string namespaceUri,
            string name,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(namespaceUri) ||
                string.IsNullOrEmpty(name) ||
                !m_referenceTypes.TryGetValue(
                    namespaceUri + "|" + name, out List<WotResolvedReferenceType>? matches))
            {
                return new ValueTask<ArrayOf<WotResolvedReferenceType>>(
                    ArrayOf<WotResolvedReferenceType>.Empty);
            }
            return new ValueTask<ArrayOf<WotResolvedReferenceType>>(matches.ToArrayOf());
        }

        /// <summary>
        /// Indexes a document that describes a ReferenceType under both of the
        /// names it answers to.
        /// </summary>
        private void IndexReferenceType(WotDocument document)
        {
            if (ClassOfTokens(document.TypeTokens) != WotExpectedNodeClass.ReferenceType)
            {
                return;
            }
            string? nodeId = ReadString(document.RootElement, "uav:id");
            string? browseName = ReadString(document.RootElement, "uav:browseName");
            if (nodeId is null || browseName is null)
            {
                return;
            }
            nodeId = WotNodeSetConverter.NormalizeExpandedNodeId(nodeId);
            if (!WotPortableIdentity.TryResolveQualifiedName(
                browseName, document, document.RootElement, out WotBrowsePathElement qualifiedName))
            {
                return;
            }
            string local = qualifiedName.Name;
            string namespaceUri = qualifiedName.NamespaceUri!;
            m_namespaces.Add(namespaceUri);
            AddReferenceTypeName(namespaceUri, local, nodeId, local, true);

            // A symmetric ReferenceType has one name for both directions, so
            // its BrowseName already covers the inverse and no second entry is
            // made: adding one would make every use of the name ambiguous.
            bool symmetric =
                document.RootElement.TryGetProperty(
                    WotNodeSetConverter.SymmetricTerm, out JsonElement flag) &&
                flag.ValueKind == JsonValueKind.True;
            string? inverseName = ReadString(
                document.RootElement, WotNodeSetConverter.InverseNameTerm);
            if (!symmetric &&
                inverseName is { Length: > 0 } &&
                !string.Equals(inverseName, local, StringComparison.Ordinal))
            {
                AddReferenceTypeName(namespaceUri, inverseName, nodeId, inverseName, false);
            }
        }

        private void AddReferenceTypeName(
            string namespaceUri,
            string name,
            string nodeId,
            string matchedName,
            bool isForward)
        {
            string key = namespaceUri + "|" + name;
            if (!m_referenceTypes.TryGetValue(
                key, out List<WotResolvedReferenceType>? matches))
            {
                matches = [];
                m_referenceTypes[key] = matches;
            }
            foreach (WotResolvedReferenceType existing in matches)
            {
                if (string.Equals(existing.NodeId, nodeId, StringComparison.Ordinal) &&
                    existing.IsForward == isForward)
                {
                    return;
                }
            }
            matches.Add(new WotResolvedReferenceType(nodeId, matchedName, isForward));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A Thing Model states its declarations as affordances and names what
        /// it extends with <c>tm:extends</c>, so a set of sibling documents can
        /// answer the whole question - including the inherited half - without
        /// any AddressSpace being loaded. That is what lets a Thing Description
        /// converted alongside its own Thing Model populate a declaration the
        /// model states rather than add a second Node beside it.
        /// </remarks>
        public ValueTask<WotTypeDeclarationSet?> ResolveDeclarationsAsync(
            string typeNodeId,
            WotDeclarationScope scope,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<WotTypeDeclarationSet?>(
                m_declarations.Resolve(typeNodeId, scope));
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<WotDataTypeDefinitionSource>> ResolveDataTypeDefinitionsAsync(
            string graphId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ArrayOf<WotDataTypeDefinitionSource>>(
                m_declarations.ResolveDataTypeDefinitions(graphId));
        }

        private void Index(WotDocument document)
        {
            if (document is null)
            {
                return;
            }
            m_declarations.Add(document);
            string? rootId = ReadString(document.RootElement, "uav:id");
            if (rootId is not null)
            {
                rootId = WotNodeSetConverter.NormalizeExpandedNodeId(rootId);
                m_supertypes[rootId] = WotNodeSetConverter.ReadSupertypeReferences(document);
                if (document.Id is { Length: > 0 } documentId)
                {
                    m_documentNodeIds[documentId] = rootId;
                }
            }
            IndexNode(
                document.RootElement,
                ClassOfTokens(document.TypeTokens),
                document);
            foreach (KeyValuePair<string, JsonElement> entry in document.Properties)
            {
                IndexNode(entry.Value, WotExpectedNodeClass.Any, document);
            }
            foreach (KeyValuePair<string, JsonElement> entry in document.Actions)
            {
                IndexNode(entry.Value, WotExpectedNodeClass.Any, document);
            }
            foreach (KeyValuePair<string, JsonElement> entry in document.Events)
            {
                IndexNode(entry.Value, WotExpectedNodeClass.ObjectType, document);
            }
            IndexReferenceType(document);
            IndexDataTypeDefinitions(document);
            IndexNativeProjection(document);
        }

        private void IndexDataTypeDefinitions(WotDocument document)
        {
            var nodeSet = new UANodeSet();
            foreach (JsonElement definition in WotNodeSetConverter.ReadDataTypeDefinitionOccurrences(
                document.RootElement))
            {
                if (WotNodeSetConverter.IsReferenceOnlyDefinition(definition) ||
                    !WotPortableIdentity.TryResolveQualifiedName(
                        ReadString(definition, "uav:dataTypeName"), document, definition,
                        out WotBrowsePathElement name))
                {
                    continue;
                }
                var diagnostics = new List<WotDiagnostic>();
                string? identity = WotNodeSetConverter.ResolveDataTypeIdentity(
                    document, definition, nodeSet, diagnostics);
                if (identity is null ||
                    diagnostics.Exists(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error))
                {
                    continue;
                }
                identity = WotNodeSetConverter.NormalizeExpandedNodeId(
                    WotNodeSetConverter.ToPortableNodeId(identity, nodeSet.NamespaceUris) ?? identity);
                var resolved = new WotResolvedNode(identity, WotExpectedNodeClass.DataType);
                m_byNodeId.TryAdd(identity, resolved);
                AddBrowseName(name.NamespaceUri!, name.Name, resolved);
            }
        }

        /// <summary>
        /// Indexes the node records of a document's <c>uav:nodes</c> projection.
        /// </summary>
        /// <remarks>
        /// A companion model whose own readable mapping is incomplete carries
        /// its types only there — the Pumps model states <c>PumpType</c> as a
        /// node record and not as a readable affordance. Ignoring the projection
        /// would leave an instance of that model unable to resolve the very
        /// type it is an instance of, so the context reads both. The projection
        /// carries its own namespace table, and its node identifiers are indices
        /// into that table rather than into the document's <c>@context</c>.
        /// </remarks>
        private void IndexNativeProjection(WotDocument document)
        {
            if (!document.TryGetNativeProjection(out JsonElement projection) ||
                WotNativeProjection.HasUnsupportedProfile(projection))
            {
                return;
            }
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ReadAuthoritativeTypeContext(document);
            if (!result.Success || result.Value is null)
            {
                throw new FormatException("The authoritative native type context could not be restored.");
            }
            UANodeSet nodeSet = result.Value;
            foreach (string namespaceUri in nodeSet.NamespaceUris ?? [])
            {
                m_namespaces.Add(namespaceUri);
            }
            var aliases = NodeSetDeclaredAliases.FromNodeSet(nodeSet, WotNodeSetAliases.Instance);
            foreach (UANode node in nodeSet.Items ?? [])
            {
                if (WotNodeSetConverter.ToPortableNodeId(node.NodeId, nodeSet.NamespaceUris) is not { } portable)
                {
                    continue;
                }
                portable = WotNodeSetConverter.NormalizeExpandedNodeId(portable);
                WotExpectedNodeClass nodeClass = node switch
                {
                    UAObjectType => WotExpectedNodeClass.ObjectType,
                    UAVariableType => WotExpectedNodeClass.VariableType,
                    UAReferenceType => WotExpectedNodeClass.ReferenceType,
                    UADataType => WotExpectedNodeClass.DataType,
                    _ => WotExpectedNodeClass.Any
                };
                var resolved = new WotResolvedNode(portable, nodeClass)
                {
                    IsAbstract = node is UAType { IsAbstract: true }
                };
                if (node is UAVariableType variableType)
                {
                    resolved = resolved with
                    {
                        DataTypeNodeId = WotNodeSetConverter.ToPortableDataTypeId(variableType.DataType, nodeSet),
                        ValueRank = variableType.ValueRank,
                        ArrayDimensions = ReadNativeDimensions(variableType.ArrayDimensions)
                    };
                }
                m_byNodeId[portable] = resolved;
                if (node is UADataType && node.BrowseName is { } dataTypeName)
                {
                    QualifiedName name = QualifiedName.Parse(dataTypeName);
                    if (name.Name is { Length: > 0 } local)
                    {
                        if (name.NamespaceIndex == 0)
                        {
                            AddBrowseName(WotVocabulary.OpcUaNamespace, local, resolved);
                        }
                        else if (name.NamespaceIndex <= (nodeSet.NamespaceUris?.Length ?? 0))
                        {
                            AddBrowseName(nodeSet.NamespaceUris![name.NamespaceIndex - 1], local, resolved);
                        }
                    }
                }
                var parents = new List<string>();
                foreach (Reference reference in node.References ?? [])
                {
                    string? type = aliases.TryResolve(reference.ReferenceType ?? string.Empty, out string referenceId)
                        ? referenceId
                        : reference.ReferenceType;
                    string? target = aliases.TryResolve(reference.Value ?? string.Empty, out string targetId)
                        ? targetId
                        : reference.Value;
                    if (!reference.IsForward && type == WotVocabulary.HasSubtype &&
                        WotNodeSetConverter.ToPortableNodeId(target, nodeSet.NamespaceUris) is { } parent)
                    {
                        parents.Add(WotNodeSetConverter.NormalizeExpandedNodeId(parent));
                    }
                }
                m_supertypes[portable] = parents.ToArrayOf();
            }
        }

        private static ArrayOf<uint> ReadNativeDimensions(string? dimensions)
        {
            if (string.IsNullOrEmpty(dimensions))
            {
                return ArrayOf<uint>.Empty;
            }
            var values = new List<uint>();
            foreach (string dimension in dimensions.Split(','))
            {
                values.Add(uint.Parse(dimension, NumberStyles.Integer, CultureInfo.InvariantCulture));
            }
            return values.ToArrayOf();
        }

        private void IndexNode(
            JsonElement element, WotExpectedNodeClass nodeClass, WotDocument document)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            string? nodeId = ReadString(element, "uav:id");
            if (nodeId is not null)
            {
                nodeId = WotNodeSetConverter.NormalizeExpandedNodeId(nodeId);
            }
            WotResolvedNode resolved = WotNodeSetConverter.DescribeResolvedNode(
                element, nodeId ?? string.Empty, nodeClass);
            if (nodeId is not null)
            {
                m_byNodeId[nodeId] = resolved;
                int separator = nodeId.IndexOf(';', StringComparison.Ordinal);
                if (nodeId.StartsWith("nsu=", StringComparison.Ordinal) && separator > 4)
                {
                    m_namespaces.Add(nodeId[4..separator]);
                }
            }
            string? browseName = ReadString(element, "uav:browseName");
            if (browseName is null || nodeId is null)
            {
                return;
            }
            if (!WotPortableIdentity.TryResolveQualifiedName(
                browseName, document, element, out WotBrowsePathElement qualifiedName))
            {
                return;
            }
            string local = qualifiedName.Name;
            string namespaceUri = qualifiedName.NamespaceUri!;
            AddBrowseName(namespaceUri, local, resolved);
        }

        private void AddBrowseName(string namespaceUri, string local, WotResolvedNode resolved)
        {
            m_namespaces.Add(namespaceUri);
            string key = namespaceUri + "|" + local;
            if (!m_byBrowseName.TryGetValue(key, out List<WotResolvedNode>? matches))
            {
                matches = [];
                m_byBrowseName[key] = matches;
            }
            if (!matches.Exists(match => match.NodeId == resolved.NodeId && match.NodeClass == resolved.NodeClass))
            {
                matches.Add(resolved);
            }
        }

        private ArrayOf<string> GetSupertypes(string nodeId)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal) { nodeId };
            var pending = new Queue<string>();
            pending.Enqueue(nodeId);
            while (pending.Count > 0)
            {
                if (!m_supertypes.TryGetValue(pending.Dequeue(), out ArrayOf<string> parents))
                {
                    continue;
                }
                foreach (string reference in parents)
                {
                    string parent = m_documentNodeIds.TryGetValue(reference, out string? documentNodeId)
                        ? documentNodeId
                        : WotNodeSetConverter.NormalizeExpandedNodeId(reference);
                    if (!WotPortableIdentity.IsPortableNodeId(parent) || !seen.Add(parent))
                    {
                        continue;
                    }
                    found.Add(parent);
                    pending.Enqueue(parent);
                }
            }
            return found.ToArrayOf();
        }

        private static WotExpectedNodeClass ClassOfTokens(IReadOnlyList<string> tokens)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i], "uav:objectType", StringComparison.Ordinal))
                {
                    return WotExpectedNodeClass.ObjectType;
                }
                if (string.Equals(tokens[i], "uav:variableType", StringComparison.Ordinal))
                {
                    return WotExpectedNodeClass.VariableType;
                }
                if (string.Equals(tokens[i], "uav:referenceType", StringComparison.Ordinal))
                {
                    return WotExpectedNodeClass.ReferenceType;
                }
                if (string.Equals(tokens[i], "uav:dataType", StringComparison.Ordinal))
                {
                    return WotExpectedNodeClass.DataType;
                }
            }
            return WotExpectedNodeClass.Any;
        }

        private static string? ReadString(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private readonly Dictionary<string, WotResolvedNode> m_byNodeId =
            new(StringComparer.Ordinal);

        private readonly WotDocumentDeclarationIndex m_declarations = new();

        private readonly Dictionary<string, List<WotResolvedNode>> m_byBrowseName =
            new(StringComparer.Ordinal);

        private readonly Dictionary<string, List<WotResolvedReferenceType>> m_referenceTypes =
            new(StringComparer.Ordinal);

        private readonly HashSet<string> m_namespaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ArrayOf<string>> m_supertypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> m_documentNodeIds = new(StringComparer.Ordinal);
    }
}
