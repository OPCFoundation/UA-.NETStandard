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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// The sibling-document part of the WoT Binding Section 5.1.5 local
    /// context, over the documents held in a registry snapshot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 5.1.5 consults the other documents being converted alongside
    /// this one <em>before</em> a loaded AddressSpace, so that a set of
    /// documents authored together resolves to itself and loading an unrelated
    /// companion model can never change what an existing document projects to.
    /// Compose this ahead of an AddressSpace-backed resolver with
    /// <see cref="WotCompositeNodeResolver"/> to get that order.
    /// </para>
    /// <para>
    /// Thing Models are indexed as types, and a document describing a
    /// ReferenceType additionally as a relation the local context can name. A
    /// Thing Model projects its root as an ObjectType and is therefore what a
    /// Section 5.2.1 type binding can name; a Thing Description projects an
    /// instance and is never a type-binding target.
    /// </para>
    /// <para>
    /// The index is built once, on first use, and is not rebuilt: a snapshot is
    /// immutable, so a conversion sees one consistent set of siblings for its
    /// whole run.
    /// </para>
    /// </remarks>
    public sealed class SnapshotWotNodeResolver
        : IWotNodeResolver, IWotReferenceTypeResolver, IWotTypeDeclarationResolver
    {
        /// <summary>
        /// Initializes a resolver over the documents in a registry snapshot.
        /// </summary>
        /// <param name="snapshot">The snapshot holding the sibling documents.</param>
        /// <param name="contents">The document contents, keyed by digest.</param>
        /// <param name="options">
        /// The parser options to read the siblings with, so that the bounds a
        /// conversion runs under also bound the indexing.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="snapshot"/> or <paramref name="contents"/> is
        /// <c>null</c>.
        /// </exception>
        public SnapshotWotNodeResolver(
            WotRegistrySnapshot snapshot,
            IReadOnlyDictionary<string, ByteString> contents,
            WotNodeSetConverterOptions? options = null)
        {
            m_snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            m_contents = contents ?? throw new ArgumentNullException(nameof(contents));
            m_options = options;
            m_maxDocuments = options?.MaxResolverDocuments ?? 256;
            m_maxTotalBytes = options?.MaxResolverTotalBytes ?? 128L * 1024 * 1024;
        }

        /// <summary>
        /// Gets the snapshot this resolver indexes, so a caller can tell
        /// whether an existing instance still applies.
        /// </summary>
        public WotRegistrySnapshot Snapshot => m_snapshot;

        /// <inheritdoc/>
        public ValueTask<bool> HoldsNamespaceAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(namespaceUri))
            {
                return new ValueTask<bool>(false);
            }
            return new ValueTask<bool>(Index().Namespaces.Contains(namespaceUri));
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
            string namespaceUri,
            string browseName,
            WotExpectedNodeClass expected,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(namespaceUri) || string.IsNullOrEmpty(browseName))
            {
                return new ValueTask<ArrayOf<WotResolvedNode>>(ArrayOf<WotResolvedNode>.Empty);
            }

            if (!Index().ByBrowseName.TryGetValue(
                Key(namespaceUri, browseName), out ArrayOf<WotResolvedNode> found))
            {
                return new ValueTask<ArrayOf<WotResolvedNode>>(ArrayOf<WotResolvedNode>.Empty);
            }
            if (expected == WotExpectedNodeClass.Any)
            {
                return new ValueTask<ArrayOf<WotResolvedNode>>(found);
            }

            // Section 5.2.1 makes a resolved type of the wrong NodeClass an
            // invalid document, so a match of a NodeClass the caller did not
            // ask for is not offered at all.
            var accepted = new List<WotResolvedNode>(found.Count);
            foreach (WotResolvedNode node in found)
            {
                if (node.NodeClass == expected)
                {
                    accepted.Add(node);
                }
            }
            return new ValueTask<ArrayOf<WotResolvedNode>>(
                new ArrayOf<WotResolvedNode>(accepted.ToArray()));
        }

        /// <inheritdoc/>
        public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
            string expandedNodeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WotResolvedNode? match =
                !string.IsNullOrEmpty(expandedNodeId) &&
                Index().ByNodeId.TryGetValue(expandedNodeId, out WotResolvedNode found)
                    ? found
                    : null;
            return new ValueTask<WotResolvedNode?>(match);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A registry holds the ReferenceTypes of a companion model as
        /// documents of their own, and a document describing one carries both
        /// of the names OPC 10000-3 gives it. Reading them here is what lets a
        /// sibling state a relation of that model in either direction and have
        /// it resolve against the registry rather than against a loaded
        /// AddressSpace, which is the precedence Section 5.1.5 fixes.
        /// </remarks>
        public ValueTask<ArrayOf<WotResolvedReferenceType>> ResolveReferenceTypesAsync(
            string namespaceUri,
            string name,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(namespaceUri) || string.IsNullOrEmpty(name))
            {
                return new ValueTask<ArrayOf<WotResolvedReferenceType>>(
                    ArrayOf<WotResolvedReferenceType>.Empty);
            }
            ArrayOf<WotResolvedReferenceType> matches =
                Index().ReferenceTypes.TryGetValue(
                    Key(namespaceUri, name),
                    out ArrayOf<WotResolvedReferenceType> found)
                    ? found
                    : ArrayOf<WotResolvedReferenceType>.Empty;
            return new ValueTask<ArrayOf<WotResolvedReferenceType>>(matches);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A registry holds the Thing Models a document binds to, and a Thing
        /// Model states its declarations as affordances and names what it
        /// extends with <c>tm:extends</c>. Answering here rather than from the
        /// AddressSpace is what keeps Section 5.1.5's precedence intact for the
        /// declaration view as well as for name resolution: a set of documents
        /// loaded together describes itself, and a companion model the Server
        /// happens to hold cannot change what one of them declares.
        /// </remarks>
        public ValueTask<WotTypeDeclarationSet?> ResolveDeclarationsAsync(
            string typeNodeId,
            WotDeclarationScope scope,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<WotTypeDeclarationSet?>(
                Index().Declarations.Resolve(typeNodeId, scope));
        }

        /// <summary>
        /// Builds the index on first use.
        /// </summary>
        private SnapshotIndex Index()
        {
            SnapshotIndex? index = m_index;
            if (index is not null)
            {
                return index;
            }

            lock (m_lock)
            {
                if (m_index is not null)
                {
                    return m_index;
                }

                var built = new SnapshotIndex();
                var buckets = new Dictionary<string, List<WotResolvedNode>>(
                    StringComparer.Ordinal);
                var relations = new Dictionary<string, List<WotResolvedReferenceType>>(
                    StringComparer.Ordinal);
                long budget = 0;
                int indexed = 0;
                foreach (WotResource resource in m_snapshot.AllResources())
                {
                    // Only a Thing Model projects a type, so a Thing
                    // Description's bytes are never read. Filtering on the
                    // registry's own Kind avoids parsing a document only to
                    // discard it.
                    if (resource.Kind != WoTDocumentKindEnum.ThingModel)
                    {
                        continue;
                    }

                    WotResourceVersion? version = resource.DefaultVersion;
                    if (version is null ||
                        !m_contents.TryGetValue(version.DigestHex, out ByteString content))
                    {
                        continue;
                    }

                    // The same budget the rest of a conversion runs under also
                    // bounds the indexing, so a large registry cannot turn one
                    // conversion into unbounded work.
                    if (indexed >= m_maxDocuments || budget > m_maxTotalBytes)
                    {
                        break;
                    }
                    indexed++;
                    budget += content.Length;

                    // A sibling that cannot be indexed simply does not
                    // contribute a name. Its own conversion reports why, and
                    // one unreadable document must never abort the indexing of
                    // every other document in the registry.
                    try
                    {
                        using WotDocument document = WotDocument.Parse(
                            content.Span.ToArray(), m_options);
                        IndexReferenceType(document, built, relations);

                        // A tm:extends href in a registry names the resource by
                        // its own identifiers, so the declaration index is told
                        // every name the reference can use.
                        built.Declarations.Add(
                            document,
                            [resource.Xid, resource.ResourceId, version.DigestHex]);
                        if (!WotNodeSetConverter.TryDescribeProjectedType(
                            document,
                            out string namespaceUri,
                            out string browseName,
                            out string nodeId) ||
                            namespaceUri.Length == 0 ||
                            browseName.Length == 0)
                        {
                            continue;
                        }

                        WotExpectedNodeClass projectedClass = ProjectedNodeClass(document);
                        var node = new WotResolvedNode(nodeId, projectedClass);
                        built.Namespaces.Add(namespaceUri);
                        built.ByNodeId[nodeId] = node;

                        string key = Key(namespaceUri, browseName);
                        if (!buckets.TryGetValue(key, out List<WotResolvedNode>? bucket))
                        {
                            bucket = [];
                            buckets[key] = bucket;
                        }

                        // One entry per indexed document, never deduplicated.
                        // Two siblings projecting the same qualified name make
                        // it ambiguous even when they also claim the same
                        // identity - two documents claiming one type is exactly
                        // the conflict the caller has to be told about, so
                        // collapsing them would resolve the name uniquely and
                        // hide it.
                        bucket.Add(node);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        continue;
                    }
                }

                // Frozen before publication: the index is shared by every
                // conversion of this snapshot, so no caller may hold a
                // reference it could mutate.
                foreach (KeyValuePair<string, List<WotResolvedNode>> bucket in buckets)
                {
                    built.ByBrowseName[bucket.Key] = new ArrayOf<WotResolvedNode>(
                        bucket.Value.ToArray());
                }
                foreach (KeyValuePair<string, List<WotResolvedReferenceType>> relation in relations)
                {
                    built.ReferenceTypes[relation.Key] =
                        new ArrayOf<WotResolvedReferenceType>(relation.Value.ToArray());
                }

                m_index = built;
                return built;
            }
        }

        /// <summary>
        /// Indexes a document describing a ReferenceType under both of the
        /// names OPC 10000-3 gives it.
        /// </summary>
        /// <remarks>
        /// A symmetric ReferenceType has one name for both directions, so only
        /// its BrowseName is added: a second entry would make every use of the
        /// name ambiguous and require a <c>uav:refId</c> to state a relation
        /// that was never in doubt.
        /// </remarks>
        private static void IndexReferenceType(
            WotDocument document,
            SnapshotIndex index,
            Dictionary<string, List<WotResolvedReferenceType>> relations)
        {
            if (!WotNodeSetConverter.TryDescribeProjectedReferenceType(
                document,
                out string namespaceUri,
                out string browseName,
                out string inverseName,
                out bool isSymmetric,
                out string nodeId) ||
                namespaceUri.Length == 0 ||
                browseName.Length == 0)
            {
                return;
            }
            index.Namespaces.Add(namespaceUri);
            AddRelation(relations, namespaceUri, browseName, nodeId, browseName, true);
            if (!isSymmetric &&
                inverseName.Length != 0 &&
                !string.Equals(inverseName, browseName, StringComparison.Ordinal))
            {
                AddRelation(relations, namespaceUri, inverseName, nodeId, inverseName, false);
            }
        }

        private static void AddRelation(
            Dictionary<string, List<WotResolvedReferenceType>> relations,
            string namespaceUri,
            string name,
            string nodeId,
            string matchedName,
            bool isForward)
        {
            string key = Key(namespaceUri, name);
            if (!relations.TryGetValue(key, out List<WotResolvedReferenceType>? matches))
            {
                matches = [];
                relations[key] = matches;
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

        /// <summary>
        /// Gets the NodeClass a document projects, which is what a caller
        /// requiring a particular one is matched against.
        /// </summary>
        private static WotExpectedNodeClass ProjectedNodeClass(WotDocument document)
        {
            foreach (string token in document.TypeTokens)
            {
                if (string.Equals(token, "uav:referenceType", StringComparison.Ordinal))
                {
                    return WotExpectedNodeClass.ReferenceType;
                }
                if (string.Equals(token, "uav:variableType", StringComparison.Ordinal))
                {
                    return WotExpectedNodeClass.VariableType;
                }
            }
            return WotExpectedNodeClass.ObjectType;
        }

        private static string Key(string namespaceUri, string browseName)
        {
            return namespaceUri + "\u0000" + browseName;
        }

        private sealed class SnapshotIndex
        {
            public HashSet<string> Namespaces { get; } = new(StringComparer.Ordinal);

            public Dictionary<string, ArrayOf<WotResolvedNode>> ByBrowseName { get; } =
                new(StringComparer.Ordinal);

            public Dictionary<string, WotResolvedNode> ByNodeId { get; } =
                new(StringComparer.Ordinal);

            public Dictionary<string, ArrayOf<WotResolvedReferenceType>> ReferenceTypes { get; } =
                new(StringComparer.Ordinal);

            public WotDocumentDeclarationIndex Declarations { get; } = new();
        }

        private readonly WotRegistrySnapshot m_snapshot;
        private readonly IReadOnlyDictionary<string, ByteString> m_contents;
        private readonly WotNodeSetConverterOptions? m_options;
        private readonly int m_maxDocuments;
        private readonly long m_maxTotalBytes;
        private readonly System.Threading.Lock m_lock = new();
        private volatile SnapshotIndex? m_index;
    }
}
