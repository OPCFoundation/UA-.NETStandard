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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Export;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.RuntimeNodeSet
{
    /// <summary>
    /// Public <see cref="IAsyncNodeManagerFactory"/> that loads one or more
    /// NodeSet2 documents and exposes them through a single
    /// <see cref="RuntimeNodeSetNodeManager"/> at server startup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Construct this factory from a <see cref="RuntimeNodeSetOptions"/>
    /// instance that lists one or more <see cref="RuntimeNodeSetSource"/>
    /// objects. File-backed sources scan their <c>Models</c> metadata
    /// immediately on construction so the factory can populate
    /// <see cref="NamespacesUris"/> before the server calls
    /// <see cref="CreateAsync"/>.
    /// </para>
    /// <para>
    /// During <see cref="CreateAsync"/> all sources are parsed in full;
    /// their inter-source <c>RequiredModel</c> dependencies are resolved
    /// and the sources are imported in topological order. Missing external
    /// dependencies (models not provided by any source in the collection)
    /// are silently allowed; cycles among the <em>included</em> sources
    /// cause <see cref="InvalidOperationException"/>. Duplicate
    /// <c>ModelUri</c> values across sources also raise
    /// <see cref="InvalidOperationException"/>.
    /// </para>
    /// <para>
    /// Register via the <c>AddRuntimeNodeSet</c> extension methods on
    /// <see cref="Hosting.IOpcUaServerBuilder"/>
    /// or construct directly and pass to
    /// <see cref="Hosting.IOpcUaServerBuilder.AddNodeManager{TFactory}"/>.
    /// </para>
    /// </remarks>
    public sealed class RuntimeNodeSetNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Initializes the factory from the supplied
        /// <paramref name="options"/>. File-backed sources in
        /// <see cref="RuntimeNodeSetOptions.Sources"/> are scanned
        /// immediately to populate <see cref="NamespacesUris"/>.
        /// </summary>
        /// <param name="options">
        /// Configuration describing the NodeSet2 sources and optional
        /// fluent <c>Configure</c> callback.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="options"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// A source declares no model namespace URIs, or two sources
        /// declare the same model namespace URI.
        /// </exception>
        public RuntimeNodeSetNodeManagerFactory(RuntimeNodeSetOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Sources.IsNull || options.Sources.Count == 0)
            {
                throw new InvalidOperationException(
                    "At least one RuntimeNodeSetSource must be configured.");
            }

            m_sources = [.. options.Sources];
            m_defaultNamespaceUri = options.DefaultNamespaceUri;
            m_configure = options.Configure;
            NamespacesUris = BuildNamespacesUris(m_sources);

            if (!string.IsNullOrEmpty(m_defaultNamespaceUri) &&
                !NamespacesUris.Contains(m_defaultNamespaceUri))
            {
                throw new InvalidOperationException(
                    $"The default namespace URI '{m_defaultNamespaceUri}' is not owned " +
                    "by any configured RuntimeNodeSetSource.");
            }
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris { get; }

        /// <inheritdoc/>
        public async ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            ILogger logger = server.Telemetry.CreateLogger<RuntimeNodeSetNodeManager>();

            // Parse all sources, validate, build dependency graph, and sort.
            ParsedNodeSetDocument[] sorted = await ParseAndSortAsync(
                m_sources,
                logger,
                cancellationToken).ConfigureAwait(false);

            // Resolve the default namespace URI for the fluent builder.
            string? defaultNs = ResolveDefaultNamespace(
                sorted,
                m_defaultNamespaceUri,
                m_configure is not null);

            string[] modelUris = new string[NamespacesUris.Count];

            for (int i = 0; i < NamespacesUris.Count; i++)
            {
                modelUris[i] = NamespacesUris[i];
            }

#pragma warning disable CA2000 // Ownership transfers to the master node manager.
            return new RuntimeNodeSetNodeManager(
                server,
                configuration,
                logger,
                modelUris,
                sorted,
                defaultNs,
                m_configure);
#pragma warning restore CA2000
        }

        /// <summary>
        /// Scans all sources and builds the deduped, ordered namespace URI list
        /// used by <see cref="NamespacesUris"/>.
        /// </summary>
        private static ArrayOf<string> BuildNamespacesUris(
            ArrayOf<RuntimeNodeSetSource> sources)
        {
            if (sources.IsNull || sources.Count == 0)
            {
                return [];
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();

            for (int i = 0; i < sources.Count; i++)
            {
                RuntimeNodeSetSource source = sources[i];

                if (source is null)
                {
                    throw new InvalidOperationException(
                        $"Sources[{i}] is null.");
                }

                ArrayOf<string> modelUris = source.ModelNamespaceUris;
                if (modelUris.IsNull || modelUris.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Sources[{i}] does not declare any owned model namespace URIs.");
                }

                for (int j = 0; j < modelUris.Count; j++)
                {
                    string uri = modelUris[j];

                    if (string.IsNullOrEmpty(uri))
                    {
                        throw new InvalidOperationException(
                            $"Sources[{i}] declares a null or empty model namespace URI at index {j}.");
                    }

                    if (!seen.Add(uri))
                    {
                        throw new InvalidOperationException(
                            $"Model namespace URI '{uri}' is declared by more than one source.");
                    }

                    result.Add(uri);
                }
            }

            return [.. result];
        }

        /// <summary>
        /// Opens each source stream, parses the UANodeSet, validates it, and
        /// returns the documents sorted in topological dependency order.
        /// </summary>
        private static async Task<ParsedNodeSetDocument[]> ParseAndSortAsync(
            ArrayOf<RuntimeNodeSetSource> sources,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var documents = new ParsedNodeSetDocument[sources.Count];
            var modelUriToIndex = new Dictionary<string, int>(StringComparer.Ordinal);

            // Parse all sources and build the model-URI-to-index map.
            for (int i = 0; i < sources.Count; i++)
            {
                RuntimeNodeSetSource source = sources[i];
                string sourceName = GetSourceName(source, i);

                logger.LogInformation(
                    "RuntimeNodeSet: parsing source '{Source}'.", sourceName);

                Stream stream = await source.OpenReadAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (stream is null)
                {
                    throw new InvalidOperationException(
                        $"The NodeSet2 source '{sourceName}' returned a null stream.");
                }

                UANodeSet? nodeSet;
                try
                {
                    nodeSet = UANodeSet.Read(stream);
                }
                finally
                {
                    stream.Dispose();
                }

                if (nodeSet is null)
                {
                    throw new InvalidOperationException(
                        $"The NodeSet2 source '{sourceName}' did not produce a valid " +
                        "UANodeSet document. The file may be empty or malformed.");
                }

                // Determine owned model URIs from the parsed document.
                ArrayOf<string> ownedUris = RuntimeNodeSetSource.ExtractModelUris(
                    nodeSet,
                    source.ModelNamespaceUris);

                // Validate each owned URI against the pre-scanned metadata
                // (for file sources the scan already ran, for stream sources
                // the declared URIs are authoritative).
                ValidateOwnedUris(ownedUris, source.ModelNamespaceUris, sourceName);

                documents[i] = new ParsedNodeSetDocument(nodeSet, ownedUris, sourceName);

                for (int j = 0; j < ownedUris.Count; j++)
                {
                    string uri = ownedUris[j];

                    if (modelUriToIndex.ContainsKey(uri))
                    {
                        throw new InvalidOperationException(
                            $"Model namespace URI '{uri}' is defined by more than one " +
                            "included source. Each model URI must appear in exactly one source.");
                    }

                    modelUriToIndex[uri] = i;
                }
            }

            // Topological sort using Kahn's algorithm.
            return TopologicalSort(documents, modelUriToIndex);
        }

        /// <summary>
        /// Validates that each URI declared in the parsed document's Models
        /// section is consistent with the pre-scanned metadata for file sources.
        /// For stream sources the declared URIs are authoritative, so
        /// no cross-check is needed.
        /// </summary>
        private static void ValidateOwnedUris(
            ArrayOf<string> parsedUris,
            ArrayOf<string> declaredUris,
            string sourceName)
        {
            if (parsedUris.Count != declaredUris.Count)
            {
                throw new InvalidOperationException(
                    $"Source '{sourceName}' declares {declaredUris.Count} owned model " +
                    $"namespace URI(s), but the parsed NodeSet defines {parsedUris.Count}.");
            }

            var declared = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < declaredUris.Count; i++)
            {
                declared.Add(declaredUris[i]);
            }

            for (int i = 0; i < parsedUris.Count; i++)
            {
                if (!declared.Contains(parsedUris[i]))
                {
                    throw new InvalidOperationException(
                        $"Source '{sourceName}' defines model URI '{parsedUris[i]}', which " +
                        "is not present in its declared owned namespaces.");
                }
            }
        }

        /// <summary>
        /// Performs a topological sort of <paramref name="documents"/>
        /// based on their <c>RequiredModel</c> dependency declarations.
        /// Dependencies on models not present in the included sources are
        /// silently ignored (external dependencies). Cycles among the
        /// included sources raise <see cref="InvalidOperationException"/>.
        /// </summary>
        private static ParsedNodeSetDocument[] TopologicalSort(
            ParsedNodeSetDocument[] documents,
            Dictionary<string, int> modelUriToIndex)
        {
            int n = documents.Length;
            int[] inDegree = new int[n];
            var adjacency = new List<int>[n];
            var edges = new HashSet<(int Dependency, int Dependent)>();

            for (int i = 0; i < n; i++)
            {
                adjacency[i] = [];
            }

            // Build the dependency edges: document[i] depends on document[j]
            // means j must be imported before i  →  edge j → i.
            for (int i = 0; i < n; i++)
            {
                ParsedNodeSetDocument doc = documents[i];

                if (doc.NodeSet.Models is null)
                {
                    continue;
                }

                foreach (ModelTableEntry model in doc.NodeSet.Models)
                {
                    if (model.RequiredModel is null)
                    {
                        continue;
                    }

                    foreach (ModelTableEntry req in model.RequiredModel)
                    {
                        string? reqUri = req.ModelUri;

                        if (string.IsNullOrEmpty(reqUri))
                        {
                            continue;
                        }

                        // Skip the OPC UA base namespace — it is always external.
                        if (StringComparer.Ordinal.Equals(reqUri, "http://opcfoundation.org/UA/"))
                        {
                            continue;
                        }

                        if (!modelUriToIndex.TryGetValue(reqUri!, out int depIndex))
                        {
                            // External dependency — permitted.
                            continue;
                        }

                        // Edge: depIndex (dependency) must come before i.
                        if (edges.Add((depIndex, i)))
                        {
                            adjacency[depIndex].Add(i);
                            inDegree[i]++;
                        }
                    }
                }
            }

            // Kahn's BFS-based topological sort.
            var queue = new Queue<int>();

            for (int i = 0; i < n; i++)
            {
                if (inDegree[i] == 0)
                {
                    queue.Enqueue(i);
                }
            }

            var sorted = new List<ParsedNodeSetDocument>(n);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                sorted.Add(documents[current]);

                foreach (int neighbor in adjacency[current])
                {
                    if (--inDegree[neighbor] == 0)
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            if (sorted.Count != n)
            {
                // Build a helpful error message listing the cycle participants.
                var cycleNames = new StringBuilder();

                for (int i = 0; i < n; i++)
                {
                    if (inDegree[i] > 0)
                    {
                        if (cycleNames.Length > 0)
                        {
                            cycleNames.Append(", ");
                        }

                        cycleNames.Append('\'');
                        cycleNames.Append(documents[i].SourceName);
                        cycleNames.Append('\'');
                    }
                }

                throw new InvalidOperationException(
                    $"A circular dependency was detected among the following included " +
                    $"NodeSet2 sources: {cycleNames}. Verify the RequiredModel declarations " +
                    "in each source and ensure there are no cycles.");
            }

            return [.. sorted];
        }

        /// <summary>
        /// Determines the default namespace URI for the fluent builder.
        /// Returns <c>null</c> when no <c>Configure</c> callback is set
        /// (the builder is not used).
        /// </summary>
        private static string? ResolveDefaultNamespace(
            ParsedNodeSetDocument[] sorted,
            string? explicitUri,
            bool configureIsSet)
        {
            if (!configureIsSet)
            {
                // No fluent builder required; default namespace is not needed.
                return explicitUri;
            }

            if (!string.IsNullOrEmpty(explicitUri))
            {
                return explicitUri;
            }

            // Infer: collect all model URIs that are required by at least
            // one other included source. Leaf models (not depended on by
            // any other included source) are candidates.
            var requiredByOthers = new HashSet<string>(StringComparer.Ordinal);

            foreach (ParsedNodeSetDocument doc in sorted)
            {
                if (doc.NodeSet.Models is null)
                {
                    continue;
                }

                foreach (ModelTableEntry model in doc.NodeSet.Models)
                {
                    if (model.RequiredModel is null)
                    {
                        continue;
                    }

                    foreach (ModelTableEntry req in model.RequiredModel)
                    {
                        if (!string.IsNullOrEmpty(req.ModelUri))
                        {
                            requiredByOthers.Add(req.ModelUri!);
                        }
                    }
                }
            }

            var candidates = new List<string>();

            foreach (ParsedNodeSetDocument doc in sorted)
            {
                for (int i = 0; i < doc.OwnedModelUris.Count; i++)
                {
                    string uri = doc.OwnedModelUris[i];

                    if (!requiredByOthers.Contains(uri))
                    {
                        candidates.Add(uri);
                    }
                }
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (candidates.Count == 0 && sorted.Length == 1)
            {
                // Single source with no leaf detection — use the first owned URI.
                if (sorted[0].OwnedModelUris.Count > 0)
                {
                    return sorted[0].OwnedModelUris[0];
                }
            }

            throw new InvalidOperationException(
                "Cannot infer the default namespace URI for the RuntimeNodeSet fluent builder. " +
                $"Found {candidates.Count} candidate leaf model(s). " +
                "Set RuntimeNodeSetOptions.DefaultNamespaceUri explicitly, or remove the " +
                "Configure callback if no fluent builder is needed.");
        }

        /// <summary>
        /// Returns a human-readable name for a source, used in error messages.
        /// </summary>
        private static string GetSourceName(RuntimeNodeSetSource source, int index)
        {
            return string.IsNullOrWhiteSpace(source.Name)
                ? $"source[{index}]"
                : source.Name;
        }

        private readonly ArrayOf<RuntimeNodeSetSource> m_sources;
        private readonly string? m_defaultNamespaceUri;
        private readonly Action<INodeManagerBuilder>? m_configure;

        /// <summary>
        /// Internal representation of a parsed NodeSet2 document and its metadata.
        /// </summary>
        internal sealed class ParsedNodeSetDocument
        {
            public ParsedNodeSetDocument(
                UANodeSet nodeSet,
                ArrayOf<string> ownedModelUris,
                string sourceName)
            {
                NodeSet = nodeSet;
                OwnedModelUris = ownedModelUris;
                SourceName = sourceName;
            }

            public UANodeSet NodeSet { get; }
            public ArrayOf<string> OwnedModelUris { get; }
            public string SourceName { get; }
        }
    }
}
