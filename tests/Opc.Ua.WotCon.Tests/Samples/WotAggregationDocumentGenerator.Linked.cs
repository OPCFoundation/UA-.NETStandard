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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Samples
{
    internal static partial class WotAggregationDocumentGenerator
    {
        public static ArrayOf<ModelSource> CompanionModels => s_companionModels;

        /// <summary>
        /// Produces the entire demo closure in memory. Nothing is written until
        /// conversion, source binding and dependency validation have succeeded.
        /// </summary>
        public static async Task<ArrayOf<SampleDocument>> GenerateAggregationDocumentsAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var documents = new List<SampleDocument>();
            IWotNodeResolver nodeResolver = CreateCompanionNodeResolver(repositoryRoot);
            for (int modelIndex = 0; modelIndex < CompanionModels.Count; modelIndex++)
            {
                ModelSource model = CompanionModels[modelIndex];
                ArrayOf<SampleDocument> generated = await GenerateVerifiedDocumentSetAsync(
                    ReadNodeSet(Path.Combine(repositoryRoot, model.SourcePath)),
                    model.ResourceId,
                    null,
                    nodeResolver,
                    cancellationToken).ConfigureAwait(false);
                documents.AddRange(generated);
            }

            string source = Path.Combine(
                repositoryRoot, "samples", "WotCon", "AggregationClient",
                "Documents", "SamplePump.NodeSet2.xml");
            ArrayOf<SampleDocument> pumpDocuments = await GeneratePumpDocumentsAsync(
                ReadNodeSet(source), nodeResolver, cancellationToken).ConfigureAwait(false);
            documents.AddRange(pumpDocuments);
            documents.AddRange(GenerateAssetProjectionDocuments(pumpDocuments));

            ArrayOf<SampleDocument> result = documents.ToArrayOf();
            GetManifestEntries(result);
            return result;
        }

        /// <summary>
        /// Adds the sample's explicit source ownership and Condition pairing to
        /// readable declarations, then proves the final set against its source.
        /// </summary>
        public static async Task<ArrayOf<SampleDocument>> GeneratePumpDocumentsAsync(
            UANodeSet source,
            IWotNodeResolver nodeResolver,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArrayOf<SampleDocument> declarations = await GenerateVerifiedDocumentSetAsync(
                source, PumpModelDirectory, nodeResolver: nodeResolver, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<SampleDocument> documents = await BindPumpDocumentsAsync(
                declarations, cancellationToken).ConfigureAwait(false);
            await AssertPumpBindingsPreserveSourceAsync(
                source, declarations, documents, nodeResolver, cancellationToken).ConfigureAwait(false);
            return documents;
        }

        public static ArrayOf<SampleDocument> GeneratePumpDeclarationDocuments(UANodeSet source)
        {
            using WotDocumentSet set = RequireValue(WotNodeSetConverter.FromNodeSetDocuments(
                source, PumpModelDirectory, title: null, CreateLargeDocumentOptions()),
                PumpModelDirectory);
            return ReadGeneratedDocuments(set, PumpModelDirectory);
        }

        /// <summary>
        /// Generates linked documents and proves reconstruction before returning
        /// them. A failed proof never writes a partial set of demo artifacts.
        /// </summary>
        public static async Task<ArrayOf<SampleDocument>> GenerateVerifiedDocumentSetAsync(
            UANodeSet source,
            string resourcePrefix,
            string? title = null,
            IWotNodeResolver? nodeResolver = null,
            CancellationToken cancellationToken = default)
        {
            WotNodeSetConverterOptions options = CreateLargeDocumentOptions();
            options.PreservationMode = WotNodeSetPreservationMode.Never;
            WotConversionResult<WotDocumentSet> result = await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                source, resourcePrefix, title, options, nodeResolver, cancellationToken).ConfigureAwait(false);
            using WotDocumentSet set = RequireValue(
                result,
                resourcePrefix);
            if (set.Entries.ToList().Any(entry =>
                entry.Document.RootElement.TryGetProperty("uav:nodeSet", out _)))
            {
                throw new InvalidOperationException(
                    $"'{resourcePrefix}' unexpectedly requires a byte archive.");
            }

            return ReadGeneratedDocuments(set, resourcePrefix);
        }

        private static ArrayOf<SampleDocument> ReadGeneratedDocuments(WotDocumentSet set, string resourcePrefix)
        {
            var documents = new List<SampleDocument>(set.Entries.Count);
            var references = new Dictionary<string, string>(StringComparer.Ordinal);
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (WotDocumentSetEntry entry in set.Entries)
            {
                string id = WotRegistryService.NormalizeSegment(entry.Href, nameof(entry.Href));
                if (!identifiers.Add(id))
                {
                    throw new InvalidOperationException($"Linked document identities collide at registry id '{id}'.");
                }
                references.Add(entry.Href, id);
            }
            foreach (WotDocumentSetEntry entry in set.Entries)
            {
                JsonNode root = JsonNode.Parse(entry.Document.Utf8Json.Span)!;
                RewriteDocumentReferences(root, references);
                string id = references[entry.Href];
                string fileName = string.Join("-", id.Split(['-'], StringSplitOptions.RemoveEmptyEntries)) + ".json";
                documents.Add(new SampleDocument(
                    id,
                    resourcePrefix + "/" + fileName,
                    DocumentKind(entry.Document.RootElement),
                    FormatJson(root)));
            }
            return documents.ToArrayOf();
        }

        private static void RewriteDocumentReferences(JsonNode node, Dictionary<string, string> references)
        {
            if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    if (item is not null)
                    {
                        RewriteDocumentReferences(item, references);
                    }
                }
            }
            else if (node is JsonObject value)
            {
                foreach (string name in value.Select(property => property.Key).ToArray())
                {
                    JsonNode? member = value[name];
                    if (name is "uav:nodes" or "uav:nodeSet" or "uav:metadata" or
                        "uav:propertyConfiguration" or "uav:actionConfiguration" or "uav:eventConfiguration" or
                        "forms" or "@context" or "const" or "default" or "examples")
                    {
                        continue;
                    }
                    if (name is "href" or "tm:ref" && member is JsonValue text &&
                        text.TryGetValue(out string? reference) && reference is not null)
                    {
                        int fragment = reference.IndexOf('#', StringComparison.Ordinal);
                        string href = fragment < 0 ? reference : reference[..fragment];
                        if (references.TryGetValue(href, out string? replacement))
                        {
                            value[name] = replacement + (fragment < 0 ? string.Empty : reference[fragment..]);
                        }
                    }
                    else if (member is not null)
                    {
                        RewriteDocumentReferences(member, references);
                    }
                }
            }
        }

        public static IWotNodeResolver CreateCompanionNodeResolver(string repositoryRoot)
        {
            var sets = new List<WotDocumentSet>();
            try
            {
                foreach (ModelSource model in CompanionModels)
                {
                    sets.Add(RequireValue(WotNodeSetConverter.FromNodeSetDocuments(
                        ReadNodeSet(Path.Combine(repositoryRoot, model.SourcePath)),
                        model.ResourceId,
                        null,
                        CreateLargeDocumentOptions()), model.ResourceId));
                }
                return new WotDocumentNodeResolver(sets.SelectMany(
                    set => set.Entries.ToList().Select(entry => entry.Document)));
            }
            finally
            {
                foreach (WotDocumentSet set in sets)
                {
                    set.Dispose();
                }
            }
        }

        public static void AssertEquivalentNodeSets(
            UANodeSet expected,
            UANodeSet actual,
            string origin,
            WotNodeSetConverterOptions? options = null)
        {
            NodeSetComparisonResult comparison = WotNodeSetConverter.CompareDocumentSet(
                expected, actual, options ?? CreateLargeDocumentOptions());
            if (!comparison.AreEquivalent)
            {
                throw new InvalidOperationException(
                    $"{origin}: linked documents are incomplete: " +
                    string.Join("; ", comparison.Differences.Take(20)));
            }
        }

        private static async Task AssertPumpBindingsPreserveSourceAsync(
            UANodeSet source,
            ArrayOf<SampleDocument> declarations,
            ArrayOf<SampleDocument> documents,
            IWotNodeResolver nodeResolver,
            CancellationToken cancellationToken)
        {
            var entries = new List<WotDocumentSetEntry>();
            try
            {
                for (int documentIndex = 0; documentIndex < documents.Count; documentIndex++)
                {
                    SampleDocument document = documents[documentIndex];
                    entries.Add(new WotDocumentSetEntry(document.ResourceId,
                        WotDocument.Parse(document.Json.ToArray(), CreateLargeDocumentOptions())));
                }
                using var set = new WotDocumentSet(PumpModelDirectory, entries.ToArrayOf());
                entries.Clear();
                UANodeSet restored = RequireValue(await WotNodeSetConverter.ToNodeSetAsync(
                    set, CreateLargeDocumentOptions(), nodeResolver, cancellationToken).ConfigureAwait(false),
                    PumpModelDirectory);
                AssertEquivalentNodeSets(
                    WithBindingResidue(source, restored, declarations, documents), restored, "Bound pump documents");
            }
            finally
            {
                foreach (WotDocumentSetEntry entry in entries)
                {
                    entry.Dispose();
                }
            }
        }

        /// <summary>
        /// Reads exactly the artifacts selected by the manifest, including
        /// linked directories rather than the retained standalone regressions.
        /// </summary>
        public static ArrayOf<SampleDocument> ReadManifestDocuments(string documentsDirectory)
        {
            using var manifest = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(documentsDirectory, "documents.json")));
            var documents = new List<SampleDocument>();
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                string path = entry.GetProperty("path").GetString()!;
                documents.Add(new SampleDocument(
                    entry.GetProperty("resourceId").GetString()!,
                    path,
                    entry.GetProperty("documentKind").GetString() switch
                    {
                        "ThingModel" => WoTDocumentKindEnum.ThingModel,
                        "ThingDescription" => WoTDocumentKindEnum.ThingDescription,
                        _ => throw new InvalidOperationException($"Unknown document kind in '{path}'.")
                    },
                    ByteString.From(File.ReadAllBytes(
                        Path.Combine(documentsDirectory, path.Replace('/', Path.DirectorySeparatorChar))))));
            }
            return documents.ToArrayOf();
        }

        /// <summary>
        /// Computes logical and portable-node dependencies and a stable
        /// topological order. File enumeration order is not a dependency.
        /// </summary>
        public static ArrayOf<ManifestEntry> GetManifestEntries(ArrayOf<SampleDocument> documents)
        {
            var index = new Dictionary<string, SampleDocument>(StringComparer.Ordinal);
            foreach (SampleDocument document in documents)
            {
                if (!index.TryAdd(document.ResourceId, document))
                {
                    throw new InvalidOperationException(
                        $"Duplicate sample resource '{document.ResourceId}'.");
                }
            }

            var owners = new DocumentDependencyIndex(documents);
            var dependencies = new Dictionary<string, ArrayOf<string>>(StringComparer.Ordinal);
            foreach (SampleDocument document in documents)
            {
                dependencies.Add(document.ResourceId, owners.GetDependencies(document.ResourceId));
            }

            var entries = new List<ManifestEntry>(documents.Count);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var dependencyPath = new List<string>();
            foreach (string resourceId in index.Keys.OrderBy(id => id, StringComparer.Ordinal))
            {
                Visit(resourceId);
            }
            return entries.ToArrayOf();

            void Visit(string resourceId)
            {
                if (visited.Contains(resourceId))
                {
                    return;
                }
                if (!visiting.Add(resourceId))
                {
                    throw new InvalidOperationException(
                        $"Cyclic sample document dependency at '{resourceId}': " +
                        string.Join(" -> ", dependencyPath.Append(resourceId)) + ".");
                }
                dependencyPath.Add(resourceId);
                foreach (string dependency in dependencies[resourceId])
                {
                    Visit(dependency);
                }
                visiting.Remove(resourceId);
                dependencyPath.RemoveAt(dependencyPath.Count - 1);
                visited.Add(resourceId);
                SampleDocument document = index[resourceId];
                entries.Add(new ManifestEntry(
                    resourceId, document.Path, document.DocumentKind, dependencies[resourceId]));
            }
        }

        public static ByteString GenerateManifest(ArrayOf<SampleDocument> documents)
        {
            var manifest = new JsonArray();
            foreach (ManifestEntry entry in GetManifestEntries(documents))
            {
                manifest.Add(new JsonObject
                {
                    ["resourceId"] = entry.ResourceId,
                    ["path"] = entry.Path,
                    ["documentKind"] = entry.DocumentKind.ToString(),
                    ["groupId"] = entry.DocumentKind == WoTDocumentKindEnum.ThingModel
                        ? "thingmodels"
                        : "thingdescriptions",
                    ["dependsOn"] = StringArray(entry.DependsOn.ToList())
                });
            }
            return FormatJson(manifest);
        }

        public static async Task WriteDocumentsAsync(
            string documentsDirectory,
            ArrayOf<SampleDocument> documents,
            CancellationToken cancellationToken = default)
        {
            ByteString manifest = GenerateManifest(documents);
            for (int documentIndex = 0; documentIndex < documents.Count; documentIndex++)
            {
                SampleDocument document = documents[documentIndex];
                string path = Path.Combine(
                    documentsDirectory, document.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await WriteBytesAsync(path, document.Json, cancellationToken).ConfigureAwait(false);
            }
            await WriteBytesAsync(
                Path.Combine(documentsDirectory, "documents.json"), manifest, cancellationToken)
                .ConfigureAwait(false);

            foreach (string directory in CompanionModels.ToList().Select(model => model.ResourceId)
                .Append(PumpModelDirectory))
            {
                string target = Path.Combine(documentsDirectory, directory);
                var expected = new HashSet<string>(
                    documents.ToList().Where(document => document.Path.StartsWith(
                        directory + "/", StringComparison.Ordinal))
                        .Select(document => Path.GetFileName(document.Path)),
                    StringComparer.Ordinal);
                foreach (string file in Directory.EnumerateFiles(target, "*.json"))
                {
                    if (!expected.Contains(Path.GetFileName(file)))
                    {
                        File.Delete(file);
                    }
                }
            }
        }

        public static async Task WriteBytesAsync(
            string path,
            ByteString bytes,
            CancellationToken cancellationToken = default)
        {
            using var stream = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
#if NETFRAMEWORK
            byte[] buffer = bytes.ToArray();
            await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
#else
            await stream.WriteAsync(bytes.Memory, cancellationToken).ConfigureAwait(false);
#endif
        }

        private static WoTDocumentKindEnum DocumentKind(JsonElement root)
        {
            if (root.TryGetProperty("@type", out JsonElement type) &&
                (type.ValueKind == JsonValueKind.String && type.GetString() == "tm:ThingModel" ||
                type.ValueKind == JsonValueKind.Array &&
                type.EnumerateArray().Any(item => item.GetString() == "tm:ThingModel")))
            {
                return WoTDocumentKindEnum.ThingModel;
            }
            return WoTDocumentKindEnum.ThingDescription;
        }

        internal sealed record ModelSource(
            string ResourceId,
            string SourcePath,
            string Title,
            string StandaloneFile);

        internal sealed class SampleThingResolver(ArrayOf<SampleDocument> documents) : IWotThingResolver
        {
            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string href = reference.StartsWith("./", StringComparison.Ordinal) ? reference[2..] : reference;
                foreach (SampleDocument document in documents)
                {
                    if (string.Equals(document.ResourceId, href, StringComparison.Ordinal) ||
                        string.Equals(document.Path, href, StringComparison.Ordinal))
                    {
                        return new ValueTask<WotResolverResult>(
                            WotResolverResult.FromBytes(document.Json.ToArray()));
                    }
                }
                return new ValueTask<WotResolverResult>(WotResolverResult.NotFound);
            }
        }

        private static readonly ArrayOf<ModelSource> s_companionModels = new ModelSource[]
        {
            new(
                "opc-ua-di",
                Path.Combine("tests", "Opc.Ua.SourceGeneration.Core.Tests", "Resources", "Opc.Ua.Di.NodeSet2.xml"),
                "OPC UA Device Integration",
                "Opc.Ua.Di.tm.json"),
            new(
                "opc-ua-machinery",
                Path.Combine("samples", "DI", "PumpDeviceIntegrationServer", "Model", "Opc.Ua.Machinery.NodeSet2.xml"),
                "OPC UA Machinery",
                "Opc.Ua.Machinery.tm.json"),
            new(
                "opc-ua-pumps",
                Path.Combine("samples", "DI", "PumpDeviceIntegrationServer", "Model", "Opc.Ua.Pumps.NodeSet2.xml"),
                "OPC UA Pumps",
                "Opc.Ua.Pumps.tm.json")
        };
    }
}
