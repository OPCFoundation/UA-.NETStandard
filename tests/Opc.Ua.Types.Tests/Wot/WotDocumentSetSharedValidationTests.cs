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

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Verifies that validating many documents of one set against the shared merged
    /// NodeSet keeps the per-document verdicts of the preserved native facts.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotDocumentSetSharedValidationTests
    {
        [Test]
        public async Task ManyNativePartitionsRoundTripToTheSourceAsync()
        {
            UANodeSet source = CreateRoots();

            using WotDocumentSet set = await ExportAsync(source).ConfigureAwait(false);

            Assert.That(set.Entries, Has.Count.EqualTo(PartitionCount));
            WotConversionResult<UANodeSet> restored =
                await WotNodeSetConverter.ToNodeSetAsync(set).ConfigureAwait(false);
            Assert.That(restored.Success, Is.True, Describe(restored));
            NodeSetComparisonResult comparison =
                WotNodeSetConverter.CompareDocumentSet(source, restored.Value!);
            Assert.That(comparison.AreEquivalent, Is.True, string.Join("; ", comparison.Differences));
        }

        [TestCase(0)]
        [TestCase(PartitionCount - 1)]
        public async Task TamperedReadableFactIsReportedWhicheverPartitionCarriesItAsync(int tampered)
        {
            UANodeSet source = CreateRoots();
            using WotDocumentSet set = await ExportAsync(source).ConfigureAwait(false);
            var entries = new List<WotDocumentSetEntry>();
            for (int index = 0; index < set.Entries.Count; index++)
            {
                WotDocumentSetEntry entry = set.Entries[index];
                if (index != tampered)
                {
                    entries.Add(new WotDocumentSetEntry(entry.Href, WotDocument.Parse(entry.Document.Utf8Json)));
                    continue;
                }
                JsonObject altered = JsonNode.Parse(entry.Document.Utf8Json.Span)!.AsObject();
                altered["properties"] = new JsonObject
                {
                    ["Injected"] = new JsonObject
                    {
                        ["uav:id"] = "nsu=urn:test:documents;s=Injected" + index,
                        ["uav:componentOf"] = new JsonArray("nsu=urn:test:documents;s=Root" + index),
                        ["type"] = "boolean"
                    }
                };
                entries.Add(new WotDocumentSetEntry(
                    entry.Href, WotDocument.Parse(Encoding.UTF8.GetBytes(altered.ToJsonString()))));
            }
            using var documents = new WotDocumentSet(set.RootHref, entries.ToArrayOf());

            WotConversionResult<UANodeSet> restored =
                await WotNodeSetConverter.ToNodeSetAsync(documents).ConfigureAwait(false);

            Assert.That(restored.Success, Is.False);
            Assert.That(
                restored.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict),
                Is.True);
        }

        [Test]
        public async Task ConflictingHeaderInTheLastPartitionFailsClosedAsync()
        {
            UANodeSet source = CreateRoots();
            using WotDocumentSet set = await ExportAsync(source).ConfigureAwait(false);
            var entries = new List<WotDocumentSetEntry>();
            for (int index = 0; index < set.Entries.Count; index++)
            {
                WotDocumentSetEntry entry = set.Entries[index];
                if (index != PartitionCount - 1)
                {
                    entries.Add(new WotDocumentSetEntry(entry.Href, WotDocument.Parse(entry.Document.Utf8Json)));
                    continue;
                }
                JsonObject altered = JsonNode.Parse(entry.Document.Utf8Json.Span)!.AsObject();
                altered["uav:nodes"]!["models"]![0]!["version"] = "conflicting-version";
                entries.Add(new WotDocumentSetEntry(
                    entry.Href, WotDocument.Parse(Encoding.UTF8.GetBytes(altered.ToJsonString()))));
            }
            using var documents = new WotDocumentSet(set.RootHref, entries.ToArrayOf());

            WotConversionResult<UANodeSet> restored =
                await WotNodeSetConverter.ToNodeSetAsync(documents).ConfigureAwait(false);

            Assert.That(restored.Value, Is.Null);
            Assert.That(restored.Success, Is.False);
            Assert.That(
                restored.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict),
                Is.True);
        }

        private static async Task<WotDocumentSet> ExportAsync(UANodeSet source)
        {
            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(source, "model").ConfigureAwait(false);
            Assert.That(result.Success, Is.True, Describe(result));
            return result.Value!;
        }

        private static UANodeSet CreateRoots()
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:documents"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:documents" }],
                Aliases = [new NodeIdAlias { Alias = "HasSubtype", Value = "i=45" }],
                Items = [.. Enumerable.Range(0, PartitionCount).Select(Root)]
            };

            static UAObjectType Root(int index)
            {
                return new UAObjectType
                {
                    NodeId = "ns=1;s=Root" + index,
                    BrowseName = "1:Root" + index,
                    SymbolicName = "Symbol" + index,
                    DisplayName = [new Export.LocalizedText { Value = "Root" + index }],
                    References =
                    [
                        new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=58" }
                    ]
                };
            }
        }

        private static string Describe<T>(WotConversionResult<T> result)
            where T : class
        {
            IEnumerable<WotDiagnostic> errors = result.Diagnostics.Where(
                diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error);
            return string.Join("; ", (errors.Any() ? errors : result.Diagnostics.Take(5))
                .Select(diagnostic => diagnostic.ToString()));
        }

        private const int PartitionCount = 50;
    }
}
