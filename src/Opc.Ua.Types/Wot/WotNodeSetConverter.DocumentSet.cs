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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// One document of a <see cref="WotDocumentSet"/>: the href other documents
    /// of the set reach it by, and the document itself.
    /// </summary>
    public sealed class WotDocumentSetEntry : IDisposable
    {
        /// <summary>
        /// Initializes a new entry.
        /// </summary>
        /// <param name="href">The href the set reaches this document by.</param>
        /// <param name="document">The document.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="href"/> or <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public WotDocumentSetEntry(string href, WotDocument document)
        {
            Href = href ?? throw new ArgumentNullException(nameof(href));
            Document = document ?? throw new ArgumentNullException(nameof(document));
        }

        /// <summary>
        /// Gets the href the set reaches this document by. It never contains
        /// <c>/</c>, because the converter reads an href containing one as a
        /// BrowsePath rather than as a document reference.
        /// </summary>
        public string Href { get; }

        /// <summary>
        /// Gets the document.
        /// </summary>
        public WotDocument Document { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            Document.Dispose();
        }
    }

    /// <summary>
    /// The set of Thing Descriptions a NodeSet2 whose Objects nest converts to.
    /// <i>OPC UA — WoT Binding</i> §9.1 maps an OPC UA Object to a "Thing /
    /// nested Thing" and §6.5 states that an Object "can nest", reached by a
    /// link. A Thing Description describes exactly one Thing, so a NodeSet whose
    /// Objects nest converts to one document per Object: the root, and one for
    /// each nested Object carrying a <c>uav:componentOf</c> link to the document
    /// that owns its parent.
    /// </summary>
    /// <remarks>
    /// This exists because the single-document conversion cannot express an
    /// instance of a companion model. A DI/Machinery/Pumps pump holds every one
    /// of its Variables beneath an intermediate Object, and a single document
    /// can only carry the affordances of one Object, so the readable mapping
    /// would be empty and the exceptional <c>uav:nodes</c> projection of §9.2
    /// would be emitted for a document the vocabulary can in fact express.
    /// </remarks>
    public sealed class WotDocumentSet : IDisposable
    {
        /// <summary>
        /// Initializes a new document set.
        /// </summary>
        /// <param name="rootHref">The href of the root document.</param>
        /// <param name="entries">The documents of the set, root included.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="rootHref"/> is <c>null</c>.
        /// </exception>
        public WotDocumentSet(string rootHref, ArrayOf<WotDocumentSetEntry> entries)
        {
            RootHref = rootHref ?? throw new ArgumentNullException(nameof(rootHref));
            Entries = entries.IsNull ? ArrayOf<WotDocumentSetEntry>.Empty : entries;
        }

        /// <summary>
        /// Gets the href of the root document — the one no other document of the
        /// set is the parent of.
        /// </summary>
        public string RootHref { get; }

        /// <summary>
        /// Gets the documents of the set, the root included.
        /// </summary>
        public ArrayOf<WotDocumentSetEntry> Entries { get; }

        /// <summary>
        /// Finds the document reached by an href.
        /// </summary>
        /// <param name="href">The href to find.</param>
        /// <param name="document">Receives the document when found.</param>
        /// <returns><c>true</c> when the set holds a document for the href.</returns>
        public bool TryGetDocument(string href, out WotDocument document)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (string.Equals(Entries[i].Href, href, StringComparison.Ordinal))
                {
                    document = Entries[i].Document;
                    return true;
                }
            }
            document = null!;
            return false;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                Entries[i].Dispose();
            }
        }
    }

    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// Converts a NodeSet2 whose Objects nest into a set of Thing
        /// Descriptions, one per Object, linked by <c>uav:componentOf</c>.
        /// </summary>
        /// <param name="nodeSet">The NodeSet2 to convert.</param>
        /// <param name="rootHref">
        /// The href the root document is reached by. Child hrefs are derived
        /// from it and the BrowseName path, so the set is stable across runs.
        /// </param>
        /// <param name="title">The title of the root document, or <c>null</c>.</param>
        /// <param name="options">The bounded conversion options, or <c>null</c>.</param>
        /// <returns>The converted document set and any diagnostics.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="nodeSet"/> or <paramref name="rootHref"/> is <c>null</c>.
        /// </exception>
        public static WotConversionResult<WotDocumentSet> FromNodeSetDocuments(
            UANodeSet nodeSet,
            string rootHref,
            string? title = null,
            WotNodeSetConverterOptions? options = null)
        {
            if (nodeSet is null)
            {
                throw new ArgumentNullException(nameof(nodeSet));
            }
            if (rootHref is null)
            {
                throw new ArgumentNullException(nameof(rootHref));
            }

            WotNodeSetConverterOptions resolved = options ?? new WotNodeSetConverterOptions();
            resolved.Validate();
            var diagnostics = new List<WotDiagnostic>();
            List<UANode> roots = SelectRootNodes(nodeSet);
            if (roots.Count == 0)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ValidationError,
                    "The NodeSet holds no Node to convert."));
                return new WotConversionResult<WotDocumentSet>(null, diagnostics);
            }

            byte[] nodeSetBytes = [];
            Dictionary<string, UANode> index = BuildIndex(nodeSet);
            var entries = new List<WotDocumentSetEntry>();
            var hrefs = new HashSet<string>(StringComparer.Ordinal);
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                // The first root keeps the caller's href so an existing single
                // -root conversion is unchanged; every further root is a
                // sibling document named after itself.
                bool first = true;
                foreach (UANode root in roots)
                {
                    string href = first
                        ? rootHref
                        : ChildHref(rootHref, LocalName(root.BrowseName) ?? root.NodeId ?? "node", hrefs);
                    string documentTitle = first
                        ? title ?? LocalName(root.BrowseName) ?? rootHref
                        : LocalName(root.BrowseName) ?? href;
                    WriteObjectDocuments(
                        nodeSet, index, root, href, null,
                        documentTitle,
                        nodeSetBytes, resolved, diagnostics, entries, hrefs, emitted);
                    first = false;
                }
            }
            catch
            {
                foreach (WotDocumentSetEntry entry in entries)
                {
                    entry.Dispose();
                }
                throw;
            }

#pragma warning disable CA2000 // Ownership of the set transfers to the caller through the result.
            var set = new WotDocumentSet(rootHref, entries.ToArrayOf());
#pragma warning restore CA2000
            return new WotConversionResult<WotDocumentSet>(set, diagnostics);
        }

        /// <summary>
        /// Emits the document for one Object and then, depth-first, the document
        /// for every Object it contains. Depth-first keeps a parent's document in
        /// the set before the children that name it.
        /// </summary>
        private static void WriteObjectDocuments(
            UANodeSet nodeSet,
            Dictionary<string, UANode> index,
            UANode node,
            string href,
            string? parentHref,
            string title,
            byte[] nodeSetBytes,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            List<WotDocumentSetEntry> entries,
            HashSet<string> hrefs,
            HashSet<string> emitted)
        {
            if (!hrefs.Add(href) ||
                (node.NodeId is not null && !emitted.Add(node.NodeId)))
            {
                return;
            }
            byte[] json = WriteReadableDocument(
                nodeSet, node, title, nodeSetBytes,
                nativeProjection: null, emitEnvelope: false,
                options, diagnostics, parentHref);
#pragma warning disable CA2000 // Ownership transfers to the entry, disposed with the set.
            entries.Add(new WotDocumentSetEntry(href, WotDocument.FromOwnedBytes(json, options)));
#pragma warning restore CA2000

            if (node.References is null)
            {
                return;
            }
            foreach (Reference reference in node.References)
            {
                if (reference.Value is null ||
                    !reference.IsForward ||
                    !IsComponentReference(reference.ReferenceType) ||
                    !index.TryGetValue(reference.Value, out UANode? target) ||
                    target is not UAObject)
                {
                    continue;
                }
                string local = LocalName(target.BrowseName) ?? target.NodeId ?? href;
                WriteObjectDocuments(
                    nodeSet, index, target, ChildHref(href, local, hrefs), href,
                    local, nodeSetBytes, options, diagnostics, entries, hrefs, emitted);
            }
        }

        /// <summary>
        /// Derives a child document's href from its parent's and its BrowseName.
        /// The result never contains <c>/</c>, because the converter reads an
        /// href containing one as a BrowsePath rather than as a document
        /// reference.
        /// </summary>
        /// <summary>
        /// Selects every Node that roots a document of its own.
        /// </summary>
        /// <remarks>
        /// A single NodeSet is not a single Thing. A companion model states
        /// many type definitions side by side, and §9.1 gives each of them a
        /// document: an ObjectType is a Thing Model, a VariableType a property
        /// in one. Choosing one root and walking its references leaves
        /// everything else unreachable, which is what forced an entire
        /// companion model into the native projection.
        ///
        /// A Node contained by another Node in the same set is not a root; it
        /// is reached by the walk from the Node that contains it. Everything
        /// else roots a document, in the order the NodeSet states it so the
        /// result is stable.
        /// </remarks>
        private static List<UANode> SelectRootNodes(UANodeSet nodeSet)
        {
            if (nodeSet.Items is null || nodeSet.Items.Length == 0)
            {
                return [];
            }
            HashSet<string> contained = CollectContainedNodes(nodeSet);
            var roots = new List<UANode>();
            foreach (UANode node in nodeSet.Items)
            {
                if (!RootsItsOwnDocument(node))
                {
                    continue;
                }
                if (node.NodeId is not null && contained.Contains(node.NodeId))
                {
                    continue;
                }
                roots.Add(node);
            }
            if (roots.Count == 0)
            {
                UANode? single = SelectRootNode(nodeSet);
                if (single is not null)
                {
                    roots.Add(single);
                }
            }
            return roots;
        }

        /// <summary>
        /// A DataType is carried by <c>uav:dataTypeDefinitions</c> and a
        /// ReferenceType by the compact name a link uses, so neither roots a
        /// document of its own (§9.1).
        /// </summary>
        private static bool RootsItsOwnDocument(UANode node)
        {
            return node is UAObjectType or UAVariableType or UAObject;
        }

        private static HashSet<string> CollectContainedNodes(UANodeSet nodeSet)
        {
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (UANode node in nodeSet.Items!)
            {
                if (node.NodeId is not null)
                {
                    present.Add(node.NodeId);
                }
            }
            var contained = new HashSet<string>(StringComparer.Ordinal);
            foreach (UANode node in nodeSet.Items!)
            {
                if (node.References is null)
                {
                    continue;
                }
                foreach (Reference reference in node.References)
                {
                    if (reference.Value is null)
                    {
                        continue;
                    }
                    if (reference.IsForward && IsComponentReference(reference.ReferenceType))
                    {
                        contained.Add(reference.Value);
                    }
                    else if (!reference.IsForward &&
                        IsComponentReference(reference.ReferenceType) &&
                        node.NodeId is not null &&
                        present.Contains(reference.Value))
                    {
                        // Only a Node whose parent is in this set belongs to
                        // another document. A Node whose parent lives elsewhere
                        // — the namespace metadata Object hangs off the Server
                        // — has no document to belong to, so it must root one
                        // of its own or it is simply lost.
                        contained.Add(node.NodeId);
                    }
                }
            }
            return contained;
        }

        private static string ChildHref(string parentHref, string local, HashSet<string> taken)        {
            var builder = new StringBuilder(parentHref.Length + local.Length + 1);
            builder.Append(parentHref).Append('-');
            foreach (char character in local)
            {
                builder.Append(char.IsLetterOrDigit(character)
                    ? char.ToLowerInvariant(character)
                    : '-');
            }
            string candidate = builder.ToString();
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
            for (int suffix = 2; ; suffix++)
            {
                string next = candidate + "-" +
                    suffix.ToString(CultureInfo.InvariantCulture);
                if (!taken.Contains(next))
                {
                    return next;
                }
            }
        }

        /// <summary>
        /// Converts a document set back to one NodeSet2 by converting every
        /// document and merging the results.
        /// </summary>
        /// <remarks>
        /// Every document of a set is written from one source NodeSet, so all of
        /// them carry the same <c>@context</c> namespace table and their NodeIds
        /// already agree on namespace index. The merge is therefore a union
        /// keyed on NodeId and needs no index remapping. Each nested document's
        /// root Object is placed under its parent by the ordinary
        /// <c>uav:componentOf</c> resolution of §7.3, which reads the parent
        /// document's <c>uav:id</c> through the resolver this method supplies
        /// over the set itself.
        /// </remarks>
        /// <param name="documents">The document set to convert.</param>
        /// <param name="options">The bounded conversion options, or <c>null</c>.</param>
        /// <param name="nodeResolver">
        /// The local context §5.2.1 resolves a type binding against, or
        /// <c>null</c>. An instance of a companion model states
        /// <c>HasTypeDefinition</c> to a type its own NodeSet does not define,
        /// so without one every such binding is unresolved.
        /// </param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The merged NodeSet2 and any diagnostics.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="documents"/> is <c>null</c>.
        /// </exception>
        public static async ValueTask<WotConversionResult<UANodeSet>> ToNodeSetAsync(
            WotDocumentSet documents,
            WotNodeSetConverterOptions? options = null,
            IWotNodeResolver? nodeResolver = null,
            CancellationToken cancellationToken = default)
        {
            if (documents is null)
            {
                throw new ArgumentNullException(nameof(documents));
            }
            WotNodeSetConverterOptions resolved = options ?? new WotNodeSetConverterOptions();
            resolved.Validate();

            var diagnostics = new List<WotDiagnostic>();
            var resolver = new DocumentSetThingResolver(documents);
            var merged = new List<UANode>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string[]? namespaceUris = null;
            ModelTableEntry[]? models = null;

            for (int i = 0; i < documents.Entries.Count; i++)
            {
                WotConversionResult<UANodeSet> part = await ToNodeSetResultAsync(
                    documents.Entries[i].Document, resolved, resolver,
                    resolutionContext: null, nodeResolver, cancellationToken)
                    .ConfigureAwait(false);
                for (int j = 0; j < part.Diagnostics.Count; j++)
                {
                    diagnostics.Add(part.Diagnostics[j]);
                }
                if (part.Value is null)
                {
                    continue;
                }
                namespaceUris ??= part.Value.NamespaceUris;
                models ??= part.Value.Models;
                foreach (UANode node in part.Value.Items ?? [])
                {
                    if (node.NodeId is not null && seen.Add(node.NodeId))
                    {
                        merged.Add(node);
                    }
                }
            }

            var result = new UANodeSet
            {
                NamespaceUris = namespaceUris,
                Models = models,
                Items = merged.ToArray()
            };
            return new WotConversionResult<UANodeSet>(result, diagnostics);
        }

        /// <summary>
        /// Serves the documents of a set to the ordinary reference-resolution
        /// path, so a nested document's <c>uav:componentOf</c> href resolves
        /// without reaching outside the set.
        /// </summary>
        private sealed class DocumentSetThingResolver : IWotThingResolver
        {
            public DocumentSetThingResolver(WotDocumentSet documents)
            {
                m_documents = documents;
            }

            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                WotResolverResult result =
                    m_documents.TryGetDocument(reference, out WotDocument document)
                    ? WotResolverResult.FromBytes(document.Utf8Json.ToArray())
                    : WotResolverResult.NotFound;
                return new ValueTask<WotResolverResult>(result);
            }

            private readonly WotDocumentSet m_documents;
        }
    }
}
