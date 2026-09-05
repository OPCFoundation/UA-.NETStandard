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
        : IWotNodeResolver, IWotReferenceTypeResolver, IWotTypeDeclarationResolver
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
                if (Accepts(expected, match.NodeClass))
                {
                    accepted.Add(match);
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
                m_byNodeId.TryGetValue(nodeId, out WotResolvedNode match) &&
                Accepts(expected, match.NodeClass))
            {
                return new ValueTask<WotResolvedNode?>(match);
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
            int colon = browseName.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return;
            }
            string local = browseName[(colon + 1)..];
            string namespaceUri = browseName.StartsWith("nsu=", StringComparison.Ordinal)
                ? browseName[4..colon]
                : ResolvePrefix(document, browseName[..colon]);
            if (namespaceUri.Length == 0)
            {
                return;
            }
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

        private void Index(WotDocument document)
        {
            if (document is null)
            {
                return;
            }
            m_declarations.Add(document);
            IndexNode(
                document.RootElement,
                ClassOfTokens(document.TypeTokens),
                document);
            foreach (KeyValuePair<string, JsonElement> entry in document.Properties)
            {
                IndexNode(entry.Value, ClassOfElement(entry.Value), document);
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
            IndexNativeProjection(document);
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
            if (!document.TryGetUav("nodes", out JsonElement projection) ||
                projection.ValueKind != JsonValueKind.Object ||
                !projection.TryGetProperty("nodes", out JsonElement nodes) ||
                nodes.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            var namespaceUris = new List<string>();
            if (projection.TryGetProperty("namespaceUris", out JsonElement uris) &&
                uris.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement uri in uris.EnumerateArray())
                {
                    if (uri.ValueKind == JsonValueKind.String)
                    {
                        string value = uri.GetString() ?? string.Empty;
                        namespaceUris.Add(value);
                        m_namespaces.Add(value);
                    }
                }
            }
            foreach (JsonElement node in nodes.EnumerateArray())
            {
                string? nodeId = ReadString(node, "nodeId");
                if (nodeId is null ||
                    !TryMakePortable(nodeId, namespaceUris, out string portable))
                {
                    continue;
                }
                WotExpectedNodeClass nodeClass = ReadString(node, "nodeClass") switch
                {
                    "ObjectType" => WotExpectedNodeClass.ObjectType,
                    "VariableType" => WotExpectedNodeClass.VariableType,
                    _ => WotExpectedNodeClass.Any
                };
                m_byNodeId[portable] = new WotResolvedNode(portable, nodeClass);
            }
        }

        /// <summary>
        /// Rewrites a projection-local <c>ns=&lt;index&gt;</c> identifier into
        /// the portable <c>nsu=</c> form the vocabulary resolves against.
        /// </summary>
        private static bool TryMakePortable(
            string nodeId, List<string> namespaceUris, out string portable)
        {
            portable = string.Empty;
            if (!nodeId.StartsWith("ns=", StringComparison.Ordinal))
            {
                // No namespace prefix means namespace zero, which is the OPC UA
                // namespace and is never a companion type.
                return false;
            }
            int separator = nodeId.IndexOf(';', StringComparison.Ordinal);
            if (separator < 0 ||
                !int.TryParse(
#if NETSTANDARD2_0 || NET472 || NET48
                    nodeId.Substring(3, separator - 3),
#else
                    nodeId.AsSpan(3, separator - 3),
#endif
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int index) ||
                index < 1 ||
                index > namespaceUris.Count)
            {
                return false;
            }
            portable = "nsu=" + namespaceUris[index - 1] + ";" + nodeId[(separator + 1)..];
            return true;
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
                m_byNodeId[nodeId] = new WotResolvedNode(nodeId, nodeClass);
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
            int colon = browseName.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return;
            }
            string prefix = browseName[..colon];
            string local = browseName[(colon + 1)..];
            string namespaceUri = browseName.StartsWith("nsu=", StringComparison.Ordinal)
                ? browseName[4..colon]
                : ResolvePrefix(document, prefix);
            if (namespaceUri.Length == 0)
            {
                return;
            }
            m_namespaces.Add(namespaceUri);
            string key = namespaceUri + "|" + local;
            if (!m_byBrowseName.TryGetValue(key, out List<WotResolvedNode>? matches))
            {
                matches = [];
                m_byBrowseName[key] = matches;
            }
            matches.Add(new WotResolvedNode(nodeId, nodeClass));
        }

        private static string ResolvePrefix(WotDocument document, string prefix)
        {
            if (!document.TryGetContext(out JsonElement context))
            {
                return string.Empty;
            }
            if (context.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement member in context.EnumerateArray())
                {
                    string found = ReadString(member, prefix) ?? string.Empty;
                    if (found.Length != 0)
                    {
                        return found;
                    }
                }
                return string.Empty;
            }
            return ReadString(context, prefix) ?? string.Empty;
        }

        private static WotExpectedNodeClass ClassOfElement(JsonElement element)
        {
            string? token = ReadString(element, "@type");
            return token switch
            {
                "uav:variableType" => WotExpectedNodeClass.VariableType,
                "uav:objectType" => WotExpectedNodeClass.ObjectType,
                _ => WotExpectedNodeClass.Any
            };
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
    }
}
