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
using System.Text.Json;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Indexes the instance declarations a set of Thing Models states, and
    /// answers <see cref="IWotTypeDeclarationResolver"/> over them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both document-backed parts of the WoT Binding Section 5.1.5 local
    /// context - a fixed set of sibling documents and the documents held in a
    /// registry snapshot - derive declarations the same way, from the
    /// affordances of a Thing Model and from the <c>tm:extends</c> links that
    /// say what it extends. Sharing one index is what keeps the two from
    /// drifting, and keeps the bounded, cycle-checked supertype walk written
    /// once.
    /// </para>
    /// <para>
    /// An instance of this type is mutated only while it is being built. It is
    /// safe to share for reading once building has finished.
    /// </para>
    /// </remarks>
    public sealed class WotDocumentDeclarationIndex
    {
        /// <summary>
        /// Indexes one document. A document that is not a Thing Model declares
        /// nothing and is ignored.
        /// </summary>
        /// <param name="document">The document to index.</param>
        /// <param name="aliases">
        /// Additional names a <c>tm:extends</c> href may use to name this
        /// document - a registry resource identifier, for instance. The
        /// document's own <c>id</c> and the identity of the type it projects
        /// are always indexed.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public void Add(WotDocument document, IEnumerable<string>? aliases = null)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            AddNativeTypes(document);
            foreach (JsonElement definition in WotNodeSetConverter.ReadDataTypeDefinitionOccurrences(
                document.RootElement))
            {
                if (WotNodeSetConverter.IsReferenceOnlyDefinition(definition) ||
                    !definition.TryGetProperty("@id", out JsonElement identity) ||
                    identity.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                string graphId = identity.GetString()!;
                if (!m_dataTypes.TryGetValue(graphId, out List<WotDataTypeDefinitionSource>? sources))
                {
                    sources = [];
                    m_dataTypes.Add(graphId, sources);
                }
                if (!sources.Exists(source => ReferenceEquals(source.Document, document) &&
                    source.Definition.Equals(definition)))
                {
                    sources.Add(new WotDataTypeDefinitionSource(document, definition));
                }
            }
            if (!WotNodeSetConverter.TryDescribeProjectedType(
                    document, out _, out _, out string typeNodeId) ||
                typeNodeId.Length == 0 ||
                !WotNodeSetConverter.TryDescribeTypeDeclarations(
                    document,
                    out ArrayOf<WotTypeDeclaration> declarations,
                    out ArrayOf<string> supertypes))
            {
                return;
            }

            // Two documents claiming one identity is a conflict the conversion
            // reports through the ordinary name resolution; the first one
            // indexed keeps the entry so the declaration view never depends on
            // enumeration order.
            if (!m_types.ContainsKey(typeNodeId))
            {
                // Section 6.8: a type document that states
                // uav:includeInherited: true has already listed the
                // declarations it inherits, so walking its supertypes would
                // ask for a second copy of what it already says. One that
                // states false, or says nothing, lists only its own.
                m_types[typeNodeId] = new Entry(
                    declarations, supertypes,
                    IncludesInherited: WotNodeSetConverter.ReadIncludeInherited(document) == true);
            }
            AddAlias(typeNodeId, typeNodeId);
            AddAlias(document.Id, typeNodeId);
            if (aliases is not null)
            {
                foreach (string alias in aliases)
                {
                    AddAlias(alias, typeNodeId);
                }
            }
        }

        /// <summary>
        /// Reports the declarations of a type this index holds.
        /// </summary>
        /// <param name="typeNodeId">
        /// The type's identity, as a portable ExpandedNodeId string.
        /// </param>
        /// <param name="scope">Which declarations are wanted.</param>
        /// <returns>
        /// The declarations, or <c>null</c> when this index does not hold the
        /// type.
        /// </returns>
        public WotTypeDeclarationSet? Resolve(string typeNodeId, WotDeclarationScope scope)
        {
            if (string.IsNullOrEmpty(typeNodeId) ||
                !m_types.TryGetValue(typeNodeId, out Entry entry))
            {
                return null;
            }
            if (scope == WotDeclarationScope.Direct)
            {
                return new WotTypeDeclarationSet
                {
                    TypeNodeId = typeNodeId,
                    Declarations = entry.Declarations,
                    IsComplete = entry.Detail is null,
                    Detail = entry.Detail
                };
            }
            return BuildEffective(typeNodeId, entry);
        }

        /// <summary>
        /// Gets the direct source ancestry independently of declaration scope.
        /// A null array means the index does not hold the type.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="typeNodeId"/> is <c>null</c>.
        /// </exception>
        public ArrayOf<string> GetDirectSupertypes(string typeNodeId)
        {
            if (typeNodeId is null)
            {
                throw new ArgumentNullException(nameof(typeNodeId));
            }
            if (!m_types.TryGetValue(typeNodeId, out Entry entry))
            {
                return default;
            }
            var parents = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string reference in entry.Supertypes)
            {
                string identity = m_aliases.TryGetValue(TrimFragment(reference), out string? aliased)
                    ? aliased
                    : WotNodeSetConverter.NormalizeExpandedNodeId(reference);
                if (seen.Add(identity))
                {
                    parents.Add(identity);
                }
            }
            return parents.ToArrayOf();
        }

        /// <summary>
        /// Gets the authoritative native types already held by this declaration
        /// index, including non-root types, for the owning node-resolution adapter.
        /// </summary>
        public ArrayOf<(WotResolvedNode Node, WotBrowsePathElement BrowseName)> GetNativeTypes()
        {
            var types = new List<(WotResolvedNode Node, WotBrowsePathElement BrowseName)>();
            foreach (Entry entry in m_types.Values)
            {
                if (entry.NativeNode is { } node)
                {
                    types.Add((node, entry.NativeBrowseName));
                }
            }
            return types.ToArrayOf();
        }

        /// <summary>
        /// Gets complete DataType definitions for a graph identity, retaining their owning contexts.
        /// </summary>
        public ArrayOf<WotDataTypeDefinitionSource> ResolveDataTypeDefinitions(string graphId)
        {
            if (graphId is null)
            {
                throw new ArgumentNullException(nameof(graphId));
            }
            return m_dataTypes.TryGetValue(graphId, out List<WotDataTypeDefinitionSource>? sources)
                ? sources.ToArrayOf()
                : [];
        }

        private void AddNativeTypes(WotDocument document)
        {
            if (!document.TryGetEnvelope(out _) &&
                (!document.TryGetNativeProjection(out JsonElement projection) ||
                    WotNativeProjection.HasUnsupportedProfile(projection)))
            {
                return;
            }
            WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ReadAuthoritativeTypeContext(document);
            if (!restored.Success || restored.Value is null)
            {
                throw new FormatException(
                    "The authoritative native type context could not be restored. " +
                    (restored.Diagnostics.Count == 0 ? string.Empty : restored.Diagnostics[0].Message));
            }
            UANodeSet nodeSet = restored.Value;
            var references = WotReferenceTypeNames.Build(nodeSet);
            var nodes = new Dictionary<string, UANode>(StringComparer.Ordinal);
            foreach (UANode node in nodeSet.Items ?? [])
            {
                nodes.Add(node.NodeId!, node);
            }
            foreach (UANode node in nodes.Values)
            {
                if (node is not UAType)
                {
                    continue;
                }
                WotResolvedNode resolved = WotNodeSetConverter.DescribeNativeType(node, nodeSet);
                var parents = new List<string>();
                foreach (Reference reference in references.GetReferences(node))
                {
                    if (!reference.IsForward && reference.ReferenceType == WotVocabulary.HasSubtype)
                    {
                        parents.Add(WotNodeSetConverter.NormalizeExpandedNodeId(
                            WotNodeSetConverter.ToPortableNodeId(reference.Value, nodeSet.NamespaceUris)!));
                    }
                }
                ArrayOf<WotTypeDeclaration> declarations = WotNodeSetConverter.DescribeNativeTypeDeclarations(
                    node, nodeSet, references, nodes, out string? detail);
                var name = QualifiedName.Parse(node.BrowseName ??
                    throw new FormatException($"The native type '{resolved.NodeId}' has no BrowseName."));
                string namespaceUri = name.NamespaceIndex == 0
                    ? WotVocabulary.OpcUaNamespace
                    : nodeSet.NamespaceUris![name.NamespaceIndex - 1];
                m_types.TryAdd(resolved.NodeId, new Entry(
                    declarations, parents.ToArrayOf(), NativeNode: resolved,
                    NativeBrowseName: new WotBrowsePathElement(namespaceUri, name.Name!), Detail: detail));
                AddAlias(resolved.NodeId, resolved.NodeId);
            }
        }

        /// <summary>
        /// Walks the supertype chain, letting a subtype's declaration hide a
        /// supertype's declaration of the same qualified name and kind.
        /// </summary>
        /// <remarks>
        /// The walk is bounded by
        /// <see cref="WotTypeDeclarations.MaxSupertypeDepth"/> and refuses to
        /// visit a type twice, so a hierarchy that loops - which a document set
        /// can state, because nothing stops two Thing Models extending each
        /// other - stops with an incomplete answer rather than running forever.
        /// A supertype the index does not hold also makes the answer
        /// incomplete: a member that matches nothing here may still match a
        /// declaration of the type that could not be read.
        /// </remarks>
        private WotTypeDeclarationSet BuildEffective(string typeNodeId, Entry entry)
        {
            var byName = new Dictionary<string, WotTypeDeclaration>(StringComparer.Ordinal);
            var supertypes = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { typeNodeId };
            string? detail = entry.Detail;

            Merge(byName, entry.Declarations, inherited: false);

            var pending = new Queue<ArrayOf<string>>();
            pending.Enqueue(entry.IncludesInherited ? [] : entry.Supertypes);
            while (pending.Count > 0)
            {
                ArrayOf<string> hrefs = pending.Dequeue();
                if (hrefs.Count > 1)
                {
                    detail ??= "The type states multiple supertypes rather than a single inheritance chain.";
                }
                foreach (string href in hrefs)
                {
                    if (!m_aliases.TryGetValue(TrimFragment(href), out string? nextNodeId))
                    {
                        detail ??=
                            $"The supertype '{href}' is not held by this part of the " +
                            "local context, so its declarations are unknown.";
                        continue;
                    }
                    if (!visited.Add(nextNodeId))
                    {
                        detail ??=
                            $"The supertype chain revisits '{nextNodeId}', so it is a " +
                            "cycle rather than a hierarchy.";
                        continue;
                    }
                    if (supertypes.Count >= WotTypeDeclarations.MaxSupertypeDepth)
                    {
                        detail ??=
                            "The supertype chain exceeded the maximum of " +
                            $"{WotTypeDeclarations.MaxSupertypeDepth} types.";
                        return Build(typeNodeId, byName, supertypes, detail);
                    }
                    supertypes.Add(nextNodeId);
                    Entry next = m_types[nextNodeId];
                    detail ??= next.Detail;
                    Merge(byName, next.Declarations, inherited: true);
                    pending.Enqueue(next.IncludesInherited ? [] : next.Supertypes);
                }
            }
            return Build(typeNodeId, byName, supertypes, detail);
        }

        private static WotTypeDeclarationSet Build(
            string typeNodeId,
            Dictionary<string, WotTypeDeclaration> byName,
            List<string> supertypes,
            string? detail)
        {
            var ordered = new List<WotTypeDeclaration>(byName.Values);
            ordered.Sort(WotTypeDeclarations.Compare);
            return new WotTypeDeclarationSet
            {
                TypeNodeId = typeNodeId,
                Declarations = ordered.ToArrayOf(),
                Supertypes = supertypes.ToArrayOf(),
                IsComplete = detail is null,
                Detail = detail
            };
        }

        private static void Merge(
            Dictionary<string, WotTypeDeclaration> byName,
            ArrayOf<WotTypeDeclaration> declarations,
            bool inherited)
        {
            foreach (WotTypeDeclaration declaration in declarations)
            {
                string key = declaration.NamespaceUri + "\u0000" + declaration.BrowseName +
                    "\u0000" + ((int)declaration.Kind).ToString(
                        System.Globalization.CultureInfo.InvariantCulture);

                // The nearest declaration wins: a subtype that redeclares a
                // name states the version an instance has to populate, and the
                // supertype's is the one it replaced.
                if (byName.ContainsKey(key))
                {
                    continue;
                }
                byName[key] = inherited
                    ? declaration with { IsInherited = true }
                    : declaration;
            }
        }

        private void AddAlias(string? alias, string typeNodeId)
        {
            if (string.IsNullOrEmpty(alias))
            {
                return;
            }
            string key = TrimFragment(alias!);
            if (key.Length != 0 && !m_aliases.ContainsKey(key))
            {
                m_aliases[key] = typeNodeId;
            }
        }

        private static string TrimFragment(string href)
        {
            if (WotPortableIdentity.IsPortableNodeId(href))
            {
                return WotNodeSetConverter.NormalizeExpandedNodeId(href);
            }
            int hash = href.IndexOf('#', StringComparison.Ordinal);
            return hash < 0 ? href : href[..hash];
        }

        private readonly record struct Entry(
            ArrayOf<WotTypeDeclaration> Declarations,
            ArrayOf<string> Supertypes,
            bool IncludesInherited = false,
            WotResolvedNode? NativeNode = null,
            WotBrowsePathElement NativeBrowseName = default,
            string? Detail = null);

        private readonly Dictionary<string, Entry> m_types = new(StringComparer.Ordinal);

        private readonly Dictionary<string, string> m_aliases = new(StringComparer.Ordinal);

        private readonly Dictionary<string, List<WotDataTypeDefinitionSource>> m_dataTypes =
            new(StringComparer.Ordinal);
    }
}
