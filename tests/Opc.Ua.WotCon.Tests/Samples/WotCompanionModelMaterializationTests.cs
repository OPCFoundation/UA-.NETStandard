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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using ModelSource = Opc.Ua.WotCon.Tests.Samples.WotAggregationDocumentGenerator.ModelSource;
using SampleDocument = Opc.Ua.WotCon.Tests.Samples.WotAggregationDocumentGenerator.SampleDocument;

namespace Opc.Ua.WotCon.Tests.Samples
{
    /// <summary>
    /// Explicit, reproducible writers for the complete aggregation document set.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Category("Samples")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class WotCompanionModelMaterializationTests
    {
        [Test]
        [Explicit("Rewrites the authoritative two-pump source and all demo documents.")]
        public async Task WriteAggregationDocuments()
        {
            await WriteAllDocumentsAsync().ConfigureAwait(false);
        }

        [Test]
        [Explicit("Rewrites the complete linked sample set and manifest, including companion models.")]
        public async Task WriteCompanionModelSets()
        {
            await WriteAllDocumentsAsync().ConfigureAwait(false);
        }

        [Test]
        [Explicit("Rewrites only the authoritative two-pump NodeSet source.")]
        public async Task WritePumpNodeSetSource()
        {
            await WriteSourceAsync().ConfigureAwait(false);
        }

        private static async Task WriteAllDocumentsAsync()
        {
            await WriteSourceAsync().ConfigureAwait(false);
            ArrayOf<SampleDocument> generated = await WotAggregationDocumentGenerator
                .GenerateAggregationDocumentsAsync(RepositoryRoot).ConfigureAwait(false);
            await WotAggregationDocumentGenerator.WriteDocumentsAsync(
                DocumentsDirectory, generated).ConfigureAwait(false);

            for (int modelIndex = 0; modelIndex < WotAggregationDocumentGenerator.CompanionModels.Count; modelIndex++)
            {
                ModelSource model = WotAggregationDocumentGenerator.CompanionModels[modelIndex];
                ByteString standalone = ByteString.From(WotAggregationDocumentGenerator.GenerateThingModel(
                    Path.Combine(RepositoryRoot, model.SourcePath), model.Title));
                await WotAggregationDocumentGenerator.WriteBytesAsync(
                    Path.Combine(DocumentsDirectory, model.StandaloneFile), standalone).ConfigureAwait(false);
            }
            ByteString legacyPump = ByteString.From(WotAggregationDocumentGenerator.GeneratePumpThingDescription(
                Path.Combine(DocumentsDirectory, "SamplePump.NodeSet2.xml")));
            await WotAggregationDocumentGenerator.WriteBytesAsync(
                Path.Combine(DocumentsDirectory, "SamplePump.td.json"), legacyPump).ConfigureAwait(false);

            var generatedDocuments = generated.ToList();
            foreach (var group in generatedDocuments.GroupBy(document =>
                document.Path.Contains('/', StringComparison.Ordinal)
                    ? document.Path[..document.Path.IndexOf('/', StringComparison.Ordinal)]
                    : "asset-projections"))
            {
                TestContext.Out.WriteLine($"{group.Key}: {group.Count()} documents");
            }
            foreach (string pumpName in new[] { "Pump1", "Pump2" })
            {
                string identity = WotAggregationDocumentGenerator.LocalNodeId(pumpName, string.Empty);
                foreach (SampleDocument document in generatedDocuments)
                {
                    using var json = JsonDocument.Parse(document.Json.ToArray());
                    if (json.RootElement.TryGetProperty("uav:id", out JsonElement localId) &&
                        string.Equals(localId.GetString(), identity, StringComparison.Ordinal))
                    {
                        TestContext.Out.WriteLine($"{pumpName}: {document.ResourceId} -> {document.Path}");
                    }
                }
                TestContext.Out.WriteLine(WotAggregationDocumentGenerator.AffordanceReference(
                    generatedDocuments.Where(document => document.Path.StartsWith(
                        "sample-pump/", StringComparison.Ordinal)).ToArrayOf(),
                    "properties",
                    WotAggregationDocumentGenerator.LocalNodeId(
                        pumpName, "Operational.Measurements.DifferentialPressure")));
            }
            TestContext.Out.WriteLine($"documents.json: {generated.Count} exact manifest entries");
            Assert.That(generatedDocuments, Is.Not.Empty);
        }

        private static async Task WriteSourceAsync()
        {
            string path = Path.Combine(DocumentsDirectory, "SamplePump.NodeSet2.xml");
            ByteString source = WotAggregationDocumentGenerator.GeneratePumpNodeSetXml(path);
            await WotAggregationDocumentGenerator.WriteBytesAsync(path, source).ConfigureAwait(false);
            TestContext.Out.WriteLine($"SamplePump.NodeSet2.xml: {source.Length} bytes");
        }

        private static string DocumentsDirectory => Path.Combine(
            RepositoryRoot, "samples", "WotCon", "AggregationClient", "Documents");

        private static string RepositoryRoot
        {
            get
            {
                DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
                {
                    directory = directory.Parent;
                }
                return directory?.FullName
                    ?? throw new DirectoryNotFoundException("The repository root was not found.");
            }
        }
    }
}
