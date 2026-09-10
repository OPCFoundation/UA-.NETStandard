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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// Compares a reconstructed linked document set with its source,
        /// ignoring only the order of top-level Node records.
        /// </summary>
        /// <remarks>
        /// Partitioning groups each owner's Nodes together, whereas a NodeSet
        /// may interleave unrelated Nodes. Node identity, all header and node
        /// facts, Reference direction, Values and ordered definition fields are
        /// still compared by <see cref="NodeSetComparer.CompareEquivalent"/>.
        /// Neither input is modified.
        /// </remarks>
        /// <param name="source">The original NodeSet.</param>
        /// <param name="reconstructed">The reconstructed NodeSet.</param>
        /// <param name="options">Comparison bounds and alias policy.</param>
        /// <returns>The complete comparison result.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="source"/> or <paramref name="reconstructed"/> is null.
        /// </exception>
        public static NodeSetComparisonResult CompareDocumentSet(
            UANodeSet source,
            UANodeSet reconstructed,
            WotNodeSetConverterOptions? options = null)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (reconstructed is null)
            {
                throw new ArgumentNullException(nameof(reconstructed));
            }
            options ??= new WotNodeSetConverterOptions();
            options.Validate();
            return NodeSetComparer.CompareEquivalent(
                OrderDocumentSetNodes(source), OrderDocumentSetNodes(reconstructed), options.ToComparisonOptions());
        }

        /// <summary>
        /// Merges already converted partitions of one linked model using the
        /// same ownership and header rules as document-set conversion.
        /// The input NodeSets are not modified.
        /// </summary>
        public static WotConversionResult<UANodeSet> MergeNodeSetPartitions(
            WotDocumentSet documents,
            ArrayOf<UANodeSet> partitions,
            WotNodeSetConverterOptions? options = null)
        {
            if (documents is null)
            {
                throw new ArgumentNullException(nameof(documents));
            }
            if (partitions.Count != documents.Entries.Count || partitions.IsEmpty)
            {
                throw new ArgumentException("Each document must have one converted partition.", nameof(partitions));
            }
            options ??= new WotNodeSetConverterOptions();
            options.Validate();
            var diagnostics = new List<WotDiagnostic>();
            var copies = new List<WotConversionResult<UANodeSet>>(partitions.Count);
            long totalNodes = 0;
            long totalBytes = 0;
            foreach (UANodeSet partition in partitions)
            {
                if (partition is null)
                {
                    throw new ArgumentException("A converted partition is null.", nameof(partitions));
                }
                totalNodes += partition.Items?.Length ?? 0;
                if (totalNodes > options.MaxNodeCount)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error, WotDiagnosticCode.NodeCountExceeded,
                        "The combined partitions exceed the configured Node count."));
                    return new WotConversionResult<UANodeSet>(null, diagnostics);
                }
                using var xml = new MemoryStream();
                partition.Write(xml);
                totalBytes += xml.Length;
                if (xml.Length > options.MaxNodeSetSize || totalBytes > options.MaxResolverTotalBytes)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error, WotDiagnosticCode.NodeSetTooLarge,
                        "The converted partitions exceed the configured byte bound."));
                    return new WotConversionResult<UANodeSet>(null, diagnostics);
                }
                xml.Position = 0;
                copies.Add(new WotConversionResult<UANodeSet>(
                    UANodeSet.Read(xml) ?? throw new InvalidOperationException("A converted partition is invalid."),
                    []));
            }
            List<UANodeSet> filtered = FilterDocumentSetParts(documents, copies, options, diagnostics);
            UANodeSet merged = MergeDocumentSetParts(documents, filtered, options, diagnostics);
            return new WotConversionResult<UANodeSet>(HasErrors(diagnostics) ? null : merged, diagnostics);
        }

        private static UANodeSet OrderDocumentSetNodes(UANodeSet source)
        {
            UANodeSet result = CopyDocumentSetHeader(source);
            UANode[] nodes = source.Items is null ? [] : (UANode[])source.Items.Clone();
            Array.Sort(nodes, static (left, right) => string.CompareOrdinal(left.NodeId, right.NodeId));
            result.Items = nodes;
            return result;
        }

        private static UANodeSet CopyDocumentSetHeader(UANodeSet source, bool extensions = true)
        {
            return new UANodeSet
            {
                NamespaceUris = source.NamespaceUris,
                ServerUris = source.ServerUris,
                Models = source.Models,
                Aliases = source.Aliases,
                Extensions = extensions ? source.Extensions : null,
                LastModified = source.LastModified,
                LastModifiedSpecified = source.LastModifiedSpecified
            };
        }

        private static List<UANodeSet>? PartitionDocumentSetSource(
            UANodeSet source,
            WotDocumentSet documents,
            List<WotDiagnostic> diagnostics)
        {
            INodeSetAliasResolver aliases = NodeSetDeclaredAliases.FromNodeSet(source, WotNodeSetAliases.Instance);
            var nodes = new Dictionary<string, UANode>(StringComparer.Ordinal);
            var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (UANode node in source.Items ?? [])
            {
                string id = ResolveArchivedAlias(node.NodeId, aliases);
                if (id.Length == 0 || !nodes.TryAdd(id, node))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NativeProjectionConflict,
                        "Document-set partitions require unique, non-empty source NodeIds.",
                        WotLocation.FromNode(node.NodeId)));
                    return null;
                }
                if (node is UAInstance instance && instance.ParentNodeId is { Length: > 0 } parent)
                {
                    AddChild(ResolveArchivedAlias(parent, aliases), id);
                }
                foreach (Reference reference in node.References ?? [])
                {
                    if (reference.Value is null ||
                        !IsComponentReference(ResolveArchivedAlias(reference.ReferenceType, aliases)))
                    {
                        continue;
                    }
                    string target = ResolveArchivedAlias(reference.Value, aliases);
                    AddChild(reference.IsForward ? id : target, reference.IsForward ? target : id);
                }
            }

            var identities = new UANodeSet { NamespaceUris = source.NamespaceUris };
            var owners = new Dictionary<string, int>(StringComparer.Ordinal);
            var roots = new List<string>();
            for (int index = 0; index < documents.Entries.Count; index++)
            {
                string? portable = GetUavString(documents.Entries[index].Document, "id");
                string id = portable is null
                    ? string.Empty
                    : ToNodeSetNodeId(portable, identities, diagnostics);
                if (!nodes.ContainsKey(id) || !owners.TryAdd(id, index))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NativeProjectionConflict,
                        "A document root does not identify a unique source Node.",
                        new WotLocation(reference: documents.Entries[index].Href, nodeId: id)));
                    return null;
                }
                roots.Add(id);
            }
            foreach (KeyValuePair<string, UANode> entry in nodes)
            {
                if (owners.ContainsKey(entry.Key))
                {
                    continue;
                }
                UANode current = entry.Value;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (current is UAInstance instance && instance.ParentNodeId is { Length: > 0 } parent &&
                    visited.Add(current.NodeId ?? string.Empty))
                {
                    string parentId = ResolveArchivedAlias(parent, aliases);
                    if (owners.TryGetValue(parentId, out int owner))
                    {
                        owners[entry.Key] = owner;
                        break;
                    }
                    if (!nodes.TryGetValue(parentId, out UANode? parentNode))
                    {
                        break;
                    }
                    current = parentNode;
                }
            }
            for (int index = 0; index < roots.Count; index++)
            {
                var pending = new Queue<string>();
                var visited = new HashSet<string>(StringComparer.Ordinal);
                pending.Enqueue(roots[index]);
                while (pending.Count > 0)
                {
                    string parent = pending.Dequeue();
                    if (!visited.Add(parent) || !children.TryGetValue(parent, out List<string>? owned))
                    {
                        continue;
                    }
                    foreach (string child in owned)
                    {
                        if (!nodes.ContainsKey(child) ||
                            (owners.TryGetValue(child, out int previous) && previous != index))
                        {
                            continue;
                        }
                        owners[child] = index;
                        pending.Enqueue(child);
                    }
                }
            }

            var partitions = new List<UANodeSet>();
            var members = new List<List<UANode>>();
            for (int index = 0; index < roots.Count; index++)
            {
                partitions.Add(CopyDocumentSetHeader(source, extensions: index == 0));
                members.Add([]);
            }
            foreach (UANode node in source.Items ?? [])
            {
                string id = ResolveArchivedAlias(node.NodeId, aliases);
                int owner = owners.TryGetValue(id, out int known) ? known : 0;
                members[owner].Add(node);
            }
            for (int index = 0; index < partitions.Count; index++)
            {
                partitions[index].Items = [.. members[index]];
            }
            return partitions;

            void AddChild(string parent, string child)
            {
                if (!children.TryGetValue(parent, out List<string>? owned))
                {
                    owned = [];
                    children.Add(parent, owned);
                }
                owned.Add(child);
            }
        }

        private static async ValueTask<WotConversionResult<WotDocumentSet>> PreserveDocumentPartitionsAsync(
            UANodeSet source,
            WotDocumentSet readable,
            WotNodeSetConverterOptions options,
            IWotNodeResolver? nodeResolver,
            CancellationToken cancellationToken)
        {
            var diagnostics = new List<WotDiagnostic>();
            List<UANodeSet>? partitions = PartitionDocumentSetSource(source, readable, diagnostics);
            if (partitions is null)
            {
                return new WotConversionResult<WotDocumentSet>(null, diagnostics);
            }
            List<WotConversionResult<UANodeSet>> reconstructions = await ReadDocumentSetPartsAsync(
                readable, options, nodeResolver, cancellationToken).ConfigureAwait(false);
            List<UANodeSet> parts = FilterDocumentSetParts(readable, reconstructions, options, []);
            UANodeSet merged = MergeDocumentSetParts(readable, parts, options, []);
            bool preserveHeader = !NodeSetComparer.CompareEquivalent(
                CopyDocumentSetHeader(source),
                CopyDocumentSetHeader(merged),
                options.ToComparisonOptions()).AreEquivalent;
            var entries = new List<WotDocumentSetEntry>();
            WotDocumentSet? preserved = null;
            try
            {
                for (int index = 0; index < partitions.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WotDocumentSetEntry original = readable.Entries[index];
                    UANodeSet partition = partitions[index];
                    NodeSetComparisonResult comparison = ComparePartitionNodes(
                        partition, parts[index], options);
                    bool native = !reconstructions[index].Success || !comparison.AreEquivalent ||
                        (index == 0 && preserveHeader);
                    byte[] json = original.Document.Utf8Json.ToArray();
                    if (native || options.PreservationMode == WotNodeSetPreservationMode.Always)
                    {
                        json = WriteDocumentPartition(
                            original.Document, partition, native, options, diagnostics);
                        if (HasErrors(diagnostics))
                        {
                            return new WotConversionResult<WotDocumentSet>(null, diagnostics);
                        }
                        if (native)
                        {
                            diagnostics.Add(new WotDiagnostic(
                                WotDiagnosticSeverity.Warning,
                                WotDiagnosticCode.NativeProjectionIncomplete,
                                $"Document '{original.Href}' retains its complete source partition in uav:nodes.",
                                new WotLocation(reference: original.Href)));
                        }
                    }
                    json = WotJsonResidue.Apply(json, partition, options, diagnostics);
                    if (HasErrors(diagnostics) || json.Length > options.MaxJsonDocumentSize)
                    {
                        if (json.Length > options.MaxJsonDocumentSize)
                        {
                            diagnostics.Add(new WotDiagnostic(
                                WotDiagnosticSeverity.Error,
                                WotDiagnosticCode.JsonDocumentTooLarge,
                                $"Document '{original.Href}' exceeds the configured JSON byte limit.",
                                new WotLocation(reference: original.Href)));
                        }
                        return new WotConversionResult<WotDocumentSet>(null, diagnostics);
                    }
                    WotDocumentSetEntry? entry = null;
                    try
                    {
                        entry = CreateDocumentSetEntry(original.Href, json, options);
                        entries.Add(entry);
                        entry = null;
                    }
                    finally
                    {
                        entry?.Dispose();
                    }
                }
                preserved = new WotDocumentSet(readable.RootHref, entries.ToArrayOf());
                entries.Clear();
                string? difference = await DocumentSetDifferenceAsync(
                    source, preserved, options, nodeResolver, cancellationToken).ConfigureAwait(false);
                if (difference is not null)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NativeProjectionIncomplete,
                        "The linked source partitions did not reproduce the complete source NodeSet. " + difference));
                    return new WotConversionResult<WotDocumentSet>(null, diagnostics);
                }
                var result = new WotConversionResult<WotDocumentSet>(preserved, diagnostics);
                preserved = null;
                return result;
            }
            finally
            {
                preserved?.Dispose();
                foreach (WotDocumentSetEntry entry in entries)
                {
                    entry.Dispose();
                }
            }
        }

        private static WotDocumentSetEntry CreateDocumentSetEntry(
            string href,
            byte[] json,
            WotNodeSetConverterOptions options)
        {
            WotDocument? document = null;
            try
            {
                document = WotDocument.FromOwnedBytes(json, options);
                var entry = new WotDocumentSetEntry(href, document);
                document = null;
                return entry;
            }
            finally
            {
                document?.Dispose();
            }
        }

        private static NodeSetComparisonResult ComparePartitionNodes(
            UANodeSet expected,
            UANodeSet actual,
            WotNodeSetConverterOptions options)
        {
            return CompareDocumentSet(
                new UANodeSet
                {
                    NamespaceUris = expected.NamespaceUris,
                    Aliases = expected.Aliases,
                    Items = expected.Items
                },
                new UANodeSet
                {
                    NamespaceUris = actual.NamespaceUris,
                    Aliases = actual.Aliases,
                    Items = actual.Items
                },
                options);
        }

        private static byte[] WriteDocumentPartition(
            WotDocument readable,
            UANodeSet partition,
            bool native,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            byte[]? projection = null;
            if (native)
            {
                projection = WotNativeProjection.Write(partition, options, diagnostics);
                if (HasErrors(diagnostics))
                {
                    return [];
                }
                using JsonDocument encoded = JsonDocument.Parse(
                    projection, new JsonDocumentOptions { MaxDepth = options.MaxJsonDepth });
                UANodeSet? restored = WotNativeProjection.Read(encoded.RootElement, options, diagnostics);
                if (restored is null || HasErrors(diagnostics) ||
                    !NodeSetComparer.Compare(partition, restored, options.ToComparisonOptions()).AreEquivalent)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NativeProjectionIncomplete,
                        "The structured partition did not reproduce its complete source."));
                    return [];
                }
            }
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (JsonProperty member in readable.RootElement.EnumerateObject())
                {
                    if (member.Name is not ("uav:nodes" or "uav:nodeSet"))
                    {
                        member.WriteTo(writer);
                    }
                }
                if (projection is not null)
                {
                    writer.WritePropertyName("uav:nodes");
                    writer.WriteRawValue(projection, skipInputValidation: true);
                }
                if (options.PreservationMode == WotNodeSetPreservationMode.Always)
                {
                    using var xml = new MemoryStream();
                    partition.Write(xml);
                    byte[] bytes = xml.ToArray();
                    writer.WritePropertyName("uav:nodeSet");
                    writer.WriteStartObject();
                    writer.WriteString("@type", WotVocabulary.EnvelopeType);
                    writer.WriteString("contentType", WotVocabulary.NodeSetContentType);
                    writer.WriteString("encoding", WotVocabulary.Base64Encoding);
                    writer.WriteString("sha256", CoreUtils.ToHexString(ComputeSha256(bytes)).ToLowerInvariant());
                    writer.WriteString("data", Convert.ToBase64String(bytes));
                    writer.WriteString("profileVersion", WotVocabulary.ProfileVersion);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            return output.ToArray();
        }

        private static async ValueTask<List<WotConversionResult<UANodeSet>>> ReadDocumentSetPartsAsync(
            WotDocumentSet documents,
            WotNodeSetConverterOptions options,
            IWotNodeResolver? nodeResolver,
            CancellationToken cancellationToken)
        {
            var resolver = new DocumentSetThingResolver(documents);
            IWotNodeResolver context = ComposeSetLocalContext(documents, nodeResolver);
            var results = new List<WotConversionResult<UANodeSet>>();
            var archived = new List<UANode>();
            UANodeSet? archiveHeader = null;
            foreach (WotDocumentSetEntry entry in documents.Entries)
            {
                UANodeSet? part = null;
                var ignored = new List<WotDiagnostic>();
                if (entry.Document.TryGetEnvelope(out JsonElement envelope))
                {
                    part = RestoreFromEnvelope(envelope, options, ignored);
                }
                else if (entry.Document.TryGetNativeProjection(out JsonElement projection) &&
                    !WotNativeProjection.HasUnsupportedProfile(projection))
                {
                    part = WotNativeProjection.Read(projection, options, ignored);
                }
                if (part is not null && !HasErrors(ignored))
                {
                    archiveHeader ??= part;
                    archived.AddRange(part.Items ?? []);
                }
            }
            if (archiveHeader is not null)
            {
                resolver.ArchiveContext = CopyDocumentSetHeader(archiveHeader);
                resolver.ArchiveContext.Items = [.. archived];
            }
            for (int index = 0; index < documents.Entries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await ToNodeSetResultAsync(
                    documents.Entries[index].Document, options, resolver, null, context,
                    cancellationToken).ConfigureAwait(false));
            }
            return results;
        }

        private static List<UANodeSet> FilterDocumentSetParts(
            WotDocumentSet documents,
            List<WotConversionResult<UANodeSet>> results,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            var parts = new List<UANodeSet>();
            var owners = new Dictionary<string, int>(StringComparer.Ordinal);
            var nativeNodes = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < documents.Entries.Count; index++)
            {
                UANodeSet part = results[index].Value ?? new UANodeSet();
                parts.Add(part);
                string? root = GetUavString(documents.Entries[index].Document, "id");
                if (root is not null)
                {
                    owners[root] = index;
                }
                if (TakesRestorePath(documents.Entries[index].Document))
                {
                    foreach (UANode node in part.Items ?? [])
                    {
                        string id = ToPortableNodeId(node.NodeId, part.NamespaceUris) ?? string.Empty;
                        if (owners.TryGetValue(id, out int previous) && previous != index)
                        {
                            diagnostics.Add(new WotDiagnostic(
                                WotDiagnosticSeverity.Error,
                                WotDiagnosticCode.NativeProjectionConflict,
                                "Two document partitions own the same Node.",
                                WotLocation.FromNode(id)));
                        }
                        owners[id] = index;
                        nativeNodes.Add(id);
                    }
                }
            }
            for (int index = 0; index < parts.Count; index++)
            {
                UANodeSet part = parts[index];
                if (TakesRestorePath(documents.Entries[index].Document))
                {
                    continue;
                }
                INodeSetAliasResolver aliases = NodeSetDeclaredAliases.FromNodeSet(part, WotNodeSetAliases.Instance);
                var nodes = new Dictionary<string, UANode>(StringComparer.Ordinal);
                foreach (UANode node in part.Items ?? [])
                {
                    nodes[ResolveArchivedAlias(node.NodeId, aliases)] = node;
                }
                var owned = new List<UANode>();
                foreach (UANode node in part.Items ?? [])
                {
                    var visited = new HashSet<string>(StringComparer.Ordinal);
                    UANode? current = node;
                    bool external = false;
                    while (current is not null && visited.Add(current.NodeId ?? string.Empty))
                    {
                        string id = ToPortableNodeId(current.NodeId, part.NamespaceUris) ?? string.Empty;
                        if (owners.TryGetValue(id, out int owner))
                        {
                            external = owner != index;
                            if (external && TakesRestorePath(documents.Entries[owner].Document) &&
                                !nativeNodes.Contains(
                                    ToPortableNodeId(node.NodeId, part.NamespaceUris) ?? string.Empty) &&
                                IsDocumentSetAffordance(documents.Entries[index].Document, node, part))
                            {
                                ReportPartitionOverlay(node, documents.Entries[index].Href, diagnostics);
                            }
                            break;
                        }
                        string? parent = (current as UAInstance)?.ParentNodeId;
                        foreach (Reference reference in current.References ?? [])
                        {
                            if (parent is null && !reference.IsForward &&
                                IsComponentReference(ResolveArchivedAlias(reference.ReferenceType, aliases)))
                            {
                                parent = reference.Value;
                            }
                        }
                        if (parent is not null &&
                            nodes.TryGetValue(ResolveArchivedAlias(parent, aliases), out UANode? found))
                        {
                            current = found;
                        }
                        else
                        {
                            string parentId = ToPortableNodeId(parent, part.NamespaceUris) ?? string.Empty;
                            if (owners.TryGetValue(parentId, out int parentOwner) && parentOwner != index &&
                                TakesRestorePath(documents.Entries[parentOwner].Document))
                            {
                                external = true;
                                ReportPartitionOverlay(node, documents.Entries[index].Href, diagnostics);
                            }
                            current = null;
                        }
                    }
                    if (!external)
                    {
                        owned.Add(node);
                    }
                }
                part.Items = [.. owned];
            }
            for (int index = 0; index < parts.Count; index++)
            {
                WotJsonResidue.RemoveDocumentSetLinks(
                    parts[index], documents.Entries[index].Document, documents, options, diagnostics);
            }
            return parts;
        }

        private static bool IsDocumentSetAffordance(WotDocument document, UANode node, UANodeSet context)
        {
            string id = ToPortableNodeId(node.NodeId, context.NamespaceUris) ?? string.Empty;
            return Contains(document.Properties) || Contains(document.Actions);

            bool Contains(IReadOnlyDictionary<string, JsonElement> affordances)
            {
                foreach (KeyValuePair<string, JsonElement> affordance in affordances)
                {
                    string? declared = GetElementString(affordance.Value, "uav:id");
                    if (declared == id ||
                        (declared is null &&
                            (LocalName(GetElementString(affordance.Value, "uav:browseName")) ?? affordance.Key) ==
                                LocalName(node.BrowseName)))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private static void ReportPartitionOverlay(UANode node, string href, List<WotDiagnostic> diagnostics)
        {
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.NativeProjectionConflict,
                "A readable document cannot add a Node to another document's authoritative native partition.",
                new WotLocation(nodeId: node.NodeId, reference: href)));
        }

        private static UANodeSet MergeDocumentSetParts(
            WotDocumentSet documents,
            List<UANodeSet> parts,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            int headerIndex = 0;
            for (int index = 0; index < parts.Count; index++)
            {
                if (TakesRestorePath(documents.Entries[index].Document))
                {
                    headerIndex = index;
                    break;
                }
            }
            UANodeSet result = CopyDocumentSetHeader(parts.Count == 0 ? new UANodeSet() : parts[headerIndex]);
            var nodes = new List<UANode>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var extensions = new List<System.Xml.XmlElement>();
            for (int index = 0; index < parts.Count; index++)
            {
                UANodeSet part = parts[index];
                if (!EqualDocumentSetNamespaces(result.NamespaceUris, part.NamespaceUris))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NativeProjectionConflict,
                        "Document-set partitions do not share a coherent namespace table.",
                        new WotLocation(reference: documents.Entries[index].Href)));
                }
                if (index != headerIndex && TakesRestorePath(documents.Entries[index].Document) &&
                    !NodeSetComparer.CompareEquivalent(
                        CopyDocumentSetHeader(result, extensions: false),
                        CopyDocumentSetHeader(part, extensions: false),
                        options.ToComparisonOptions()).AreEquivalent)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NativeProjectionConflict,
                        "Authoritative document-set headers disagree.",
                        new WotLocation(reference: documents.Entries[index].Href)));
                }
                INodeSetAliasResolver aliases = NodeSetDeclaredAliases.FromNodeSet(part, WotNodeSetAliases.Instance);
                foreach (UANode node in part.Items ?? [])
                {
                    if (!seen.Add(node.NodeId ?? string.Empty))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.NativeProjectionConflict,
                            "Document-set conversion produced duplicate owned Nodes.",
                            WotLocation.FromNode(node.NodeId)));
                        continue;
                    }
                    ResolveDocumentSetNodeAliases(node, aliases);
                    nodes.Add(node);
                }
                extensions.AddRange(part.Extensions ?? []);
            }
            result.Items = [.. nodes];
            result.Extensions = extensions.Count == 0 ? null : [.. extensions];
            return NodeSetAliasCompleter.Complete(result, WotNodeSetAliases.Instance)!;
        }

        private static bool EqualDocumentSetNamespaces(string[]? first, string[]? second)
        {
            if ((first?.Length ?? 0) != (second?.Length ?? 0))
            {
                return false;
            }
            for (int index = 0; index < (first?.Length ?? 0); index++)
            {
                if (first![index] != second![index])
                {
                    return false;
                }
            }
            return true;
        }

        private static void ResolveDocumentSetNodeAliases(UANode node, INodeSetAliasResolver aliases)
        {
            foreach (Reference reference in node.References ?? [])
            {
                reference.ReferenceType = ResolveArchivedAlias(reference.ReferenceType, aliases);
                if (reference.Value is not null)
                {
                    reference.Value = ResolveArchivedAlias(reference.Value, aliases);
                }
            }
            if (node is UAVariable variable)
            {
                if (variable.DataType is not null)
                {
                    variable.DataType = ResolveArchivedAlias(variable.DataType, aliases);
                }
            }
            else if (node is UAVariableType variableType)
            {
                if (variableType.DataType is not null)
                {
                    variableType.DataType = ResolveArchivedAlias(variableType.DataType, aliases);
                }
            }
            else if (node is UADataType dataType)
            {
                foreach (Export.DataTypeField field in dataType.Definition?.Field ?? [])
                {
                    if (field.DataType is not null)
                    {
                        field.DataType = ResolveArchivedAlias(field.DataType, aliases);
                    }
                }
            }
        }

        internal static bool IsGeneratedEventDefinitionData(
            WotDocument document,
            UANodeSet nodeSet,
            JsonElement data,
            int maxDepth)
        {
            if (!HasEventTypeAnnotation(document))
            {
                return false;
            }
            UANode? root = SelectRootNode(nodeSet);
            if (root is null)
            {
                return false;
            }
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                WriteEventTypeDefinitionData(
                    writer, root, nodeSet.NamespaceUris, nodeSet, BuildIndex(nodeSet), GetDocumentLocale(document));
                writer.WriteEndObject();
            }
            using JsonDocument expected = JsonDocument.Parse(
                output.ToArray(), new JsonDocumentOptions { MaxDepth = maxDepth });
            return expected.RootElement.TryGetProperty(DataMember, out JsonElement generated) &&
                IsArchivedJsonSubset(data, generated) && IsArchivedJsonSubset(generated, data);
        }
    }
}
