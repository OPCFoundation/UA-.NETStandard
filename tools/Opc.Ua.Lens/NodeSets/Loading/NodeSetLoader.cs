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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Opc.Ua;
using Opc.Ua.Export;

namespace UaLens.NodeSets.Loading
{
    /// <summary>
    /// Resolves selected documents, neighboring files, the embedded core, and user-supplied dependencies.
    /// Same-revision documents may contribute complementary nodes; the graph validates duplicate NodeIds.
    /// </summary>
    internal sealed class NodeSetLoader : INodeSetLoader
    {
        public NodeSetLoader(Func<Stream>? coreModelFactory = null)
        {
            m_coreModelFactory = coreModelFactory ?? OpenCoreModel;
        }

        public async Task<ArrayOf<NodeSetDocument>> LoadAsync(
            ArrayOf<string> paths,
            INodeSetDependencyResolver resolver,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            cancellationToken.ThrowIfCancellationRequested();
            if (paths.Count is 0 or > MaxDocuments)
            {
                throw new ArgumentException($"Select between 1 and {MaxDocuments} NodeSet2 documents.", nameof(paths));
            }
            var documents = new List<NodeSetDocument>();
            var models = new Dictionary<string, (NodeSetDocument Document, ModelTableEntry Model)>(
                StringComparer.Ordinal);
            var pending = new Queue<(NodeSetDocument Document, int Depth)>();
            var selected = new HashSet<string>(PathComparer);
            var directories = new HashSet<string>(PathComparer);
            int nodeCount = 0;
            long documentBytes = 0;
            foreach (string path in paths.ToArray() ?? [])
            {
                string fullPath = Path.GetFullPath(path);
                if (!selected.Add(fullPath))
                {
                    continue;
                }
                directories.Add(Path.GetDirectoryName(fullPath)!);
                Add(await NodeSetDocument.ReadAsync(fullPath, cancellationToken).ConfigureAwait(false), 0);
            }
            if (!models.ContainsKey(Namespaces.OpcUa))
            {
                using Stream core = m_coreModelFactory();
                NodeSetDocument document = await NodeSetDocumentReader.ReadAsync(
                    core, CoreResourceName, cancellationToken).ConfigureAwait(false);
                NodeSetMetadata.Verify(document, new NodeSetRequirement(Namespaces.OpcUa));
                Add(document, 0);
            }
            string[]? siblings = null;
            var headers = new Dictionary<string, ArrayOf<ModelTableEntry>>(PathComparer);
            while (pending.TryDequeue(out (NodeSetDocument Document, int Depth) entry))
            {
                foreach (NodeSetRequirement requirement in
                    NodeSetMetadata.Requirements(entry.Document, cancellationToken).ToArray() ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (models.TryGetValue(
                        requirement.ModelUri, out (NodeSetDocument Document, ModelTableEntry Model) existing))
                    {
                        NodeSetMetadata.Verify(existing.Document, requirement);
                        continue;
                    }
                    if (entry.Depth >= MaxDependencyDepth)
                    {
                        throw new InvalidDataException(
                            $"Dependency '{requirement.ModelUri}' exceeds the depth limit of {MaxDependencyDepth}.");
                    }
                    siblings ??= await Task.Run(() => FindSiblings(directories, cancellationToken), cancellationToken)
                        .ConfigureAwait(false);
                    NodeSetDocument? dependency = null;
                    foreach (string sibling in siblings)
                    {
                        if (selected.Contains(sibling))
                        {
                            continue;
                        }
                        if (!headers.TryGetValue(sibling, out ArrayOf<ModelTableEntry> header))
                        {
                            header = await ReadLocalHeaderAsync(sibling, requirement.ModelUri, cancellationToken)
                                .ConfigureAwait(false);
                            if (header.Count > 0)
                            {
                                headers.Add(sibling, header);
                            }
                        }
                        if (!(header.ToArray() ?? []).Any(model => NodeSetMetadata.Satisfies(model, requirement)))
                        {
                            continue;
                        }
                        NodeSetDocument candidate = await NodeSetDocument.ReadAsync(sibling, cancellationToken)
                            .ConfigureAwait(false);
                        if (!(NodeSetMetadata.ProvidedModels(candidate).ToArray() ?? [])
                            .Any(model => string.Equals(
                                model.ModelUri, requirement.ModelUri, StringComparison.Ordinal)))
                        {
                            // Legacy NamespaceUris entries are discovery candidates, not proof of model ownership.
                            continue;
                        }
                        dependency = candidate;
                        break;
                    }
                    dependency ??= await resolver.ResolveAsync(requirement, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (dependency is null)
                    {
                        throw new OperationCanceledException(
                            "Opening NodeSet2 documents was cancelled.", cancellationToken);
                    }
                    NodeSetMetadata.Verify(dependency, requirement);
                    Add(dependency, entry.Depth + 1);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return documents.ToArray();

            void Add(NodeSetDocument document, int depth)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (documents.Count >= MaxDocuments ||
                    (nodeCount += document.NodeSet.Items?.Length ?? 0) > NodeSetDocumentReader.MaxNodes ||
                    (documentBytes += document.SizeBytes) > MaxClosureBytes)
                {
                    throw new InvalidDataException(
                        $"The dependency closure exceeds {MaxDocuments} documents or " +
                        $"{NodeSetDocumentReader.MaxNodes} nodes or {MaxClosureBytes} bytes.");
                }
                foreach (ModelTableEntry model in NodeSetMetadata.ProvidedModels(document))
                {
                    if (models.TryGetValue(
                        model.ModelUri!, out (NodeSetDocument Document, ModelTableEntry Model) existing))
                    {
                        if (!NodeSetMetadata.SameRevision(existing.Model, model))
                        {
                            throw new InvalidDataException(
                                $"Model '{model.ModelUri}' has conflicting revisions in " +
                                $"'{existing.Document.Source}' and '{document.Source}'.");
                        }
                        continue;
                    }
                    models.Add(model.ModelUri!, (document, model));
                }
                documents.Add(document);
                pending.Enqueue((document, depth));
            }
        }

        private static Stream OpenCoreModel()
        {
            return typeof(NodeSetLoader).Assembly.GetManifestResourceStream(CoreResourceName) ??
                throw new InvalidOperationException($"The embedded core model '{CoreResourceName}' is unavailable.");
        }

        private static string[] FindSiblings(HashSet<string> directories, CancellationToken cancellationToken)
        {
            var files = new List<string>();
            foreach (string directory in directories)
            {
                foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (files.Count >= MaxSiblingFiles)
                    {
                        throw new InvalidDataException(
                            $"Local dependency discovery exceeds {MaxSiblingFiles} XML files.");
                    }
                    files.Add(path);
                }
            }
            files.Sort(PathComparer);
            return [.. files];
        }

        private static async Task<ArrayOf<ModelTableEntry>> ReadLocalHeaderAsync(
            string path,
            string requiredUri,
            CancellationToken cancellationToken)
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
            using MemoryStream header = await NodeSetDocumentReader.ReadBoundedAsync(
                stream, NodeSetDocumentReader.MaxHeaderBytes, path, true, cancellationToken).ConfigureAwait(false);
            return await Task.Run(() =>
            {
                bool matching = false;
                try
                {
                    return NodeSetMetadata.ReadHeader(
                        header, uri => matching |= string.Equals(uri, requiredUri, StringComparison.Ordinal));
                }
                catch (Exception exception) when (!matching &&
                    exception is XmlException or InvalidDataException or FormatException)
                {
                    // Unrelated XML is not a dependency; do not deserialize or reject the user's directory.
                    return [];
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        internal const string CoreResourceName = "UaLens.NodeSets.Opc.Ua.NodeSet2.xml";
        internal const int MaxDocuments = 128;
        internal const int MaxDependencyDepth = 64;
        internal const int MaxSiblingFiles = 4096;
        internal const int MaxClosureBytes = 256 * 1024 * 1024;

        private static readonly StringComparer PathComparer =
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        private readonly Func<Stream> m_coreModelFactory;
    }
}
