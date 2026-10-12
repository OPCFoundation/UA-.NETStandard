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

#nullable enable

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
    public sealed partial class WotIndependentModelImportTests
    {
        [Test]
        public async Task IndependentReviewEffectiveRootOwnershipRejectsCollisionsAsync(
            [Values(false, true)] bool explicitPeer,
            [Values(false, true)] bool reverse,
            [Values(false, true)] bool preconverted)
        {
            using WotDocument model = ReviewGeneratedModel("urn:test:a");
            using var singleton = new WotDocumentSet("source",
                [new("source", WotDocument.Parse(model.Utf8Json))]);
            WotConversionResult<UANodeSet> generated =
                await WotNodeSetConverter.ToNodeSetAsync(singleton, IndependentOptions()).ConfigureAwait(false);
            Assert.That(generated.Success, Is.True, Describe(generated));
            ExpandedNodeId effectiveRoot = WotNodeSetConverter.TrySelectProjectionRoot(generated.Value!);
            Assert.That(effectiveRoot.IsNull, Is.False);
            JsonObject peer = JsonNode.Parse(model.Utf8Json.Span)!.AsObject();
            if (explicitPeer)
            {
                peer["uav:id"] = effectiveRoot.ToString();
            }
            WotDocumentSetEntry first = new("a", ReviewGeneratedModel("urn:test:a"));
            WotDocumentSetEntry second = new(
                "b", WotDocument.Parse(Encoding.UTF8.GetBytes(peer.ToJsonString())));
            using var documents = new WotDocumentSet("a", reverse ? [second, first] : [first, second]);
            byte[][] original = documents.Entries.ToList().Select(entry => entry.Document.Utf8Json.ToArray()).ToArray();

            WotConversionResult<UANodeSet> result =
                await ReviewImportAsync(documents, preconverted).ConfigureAwait(false);

            Assert.That(result.Success, Is.False, Describe(result));
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                diagnostic.Location?.Reference == documents.Entries[1].Href), Is.True, Describe(result));
            for (int index = 0; index < original.Length; index++)
            {
                Assert.That(documents.Entries[index].Document.Utf8Json.ToArray(), Is.EqualTo(original[index]));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task IndependentReviewGeneratedRootsRetainEquivalentContextCopiesAsync(bool reverse)
        {
            WotDocumentSetEntry a = new("a", ReviewGeneratedModel("urn:test:a"));
            WotDocumentSetEntry b = new("b", ReviewGeneratedModel("urn:test:b"));
            using var documents = new WotDocumentSet("a", reverse ? [b, a] : [a, b]);
            var parts = new List<UANodeSet>();
            var roots = new List<ExpandedNodeId>();
            for (int index = 0; index < documents.Entries.Count; index++)
            {
                WotDocumentSetEntry entry = documents.Entries[index];
                using var singleton = new WotDocumentSet(entry.Href,
                    [new(entry.Href, WotDocument.Parse(entry.Document.Utf8Json))]);
                WotConversionResult<UANodeSet> converted =
                    await WotNodeSetConverter.ToNodeSetAsync(singleton, IndependentOptions()).ConfigureAwait(false);
                Assert.That(converted.Success, Is.True, Describe(converted));
                UANodeSet part = converted.Value!;
                roots.Add(WotNodeSetConverter.TrySelectProjectionRoot(part));
                part.Items =
                [
                    new UAObjectType
                    {
                        NodeId = "i=9999",
                        BrowseName = "Context",
                        DisplayName = [new Export.LocalizedText { Value = "Context" }]
                    },
                    .. part.Items!
                ];
                parts.Add(part);
            }
            byte[][] original = parts.Select(Serialize).ToArray();

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.MergeNodeSetPartitions(documents, parts.ToArrayOf(), IndependentOptions());

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items, Has.Length.EqualTo(3));
            Assert.That(result.Value.Items!.Count(node => node.NodeId == "i=9999"), Is.EqualTo(1));
            var namespaceTable = new NamespaceTable([Namespaces.OpcUa, .. result.Value.NamespaceUris!]);
            foreach (ExpandedNodeId root in roots)
            {
                Assert.That(result.Value.Items!.Select(node => node.NodeId),
                    Does.Contain(ExpandedNodeId.ToNodeId(root, namespaceTable).ToString()));
            }
            for (int index = 0; index < parts.Count; index++)
            {
                Assert.That(Serialize(parts[index]), Is.EqualTo(original[index]));
            }
        }

        [Test]
        public async Task IndependentReviewGeneratedRootDefaultControlAsync()
        {
            using var singleton = new WotDocumentSet(
                "generated", [new("generated", ReviewGeneratedModel("urn:test:a"))]);
            WotConversionResult<UANodeSet> generated =
                await WotNodeSetConverter.ToNodeSetAsync(singleton, IndependentOptions()).ConfigureAwait(false);
            Assert.That(generated.Success, Is.True, Describe(generated));
            JsonObject peer = JsonNode.Parse(singleton.Entries[0].Document.Utf8Json.Span)!.AsObject();
            peer["uav:id"] = WotNodeSetConverter.TrySelectProjectionRoot(generated.Value!).ToString();
            using var documents = new WotDocumentSet("a",
            [
                new("a", ReviewGeneratedModel("urn:test:a")),
                new("b", WotDocument.Parse(Encoding.UTF8.GetBytes(peer.ToJsonString())))
            ]);

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetAsync(documents).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items, Has.Length.EqualTo(1));
        }

        private static WotDocument ReviewGeneratedModel(string uri)
        {
            using WotDocument model = CreateModel(uri);
            JsonObject root = JsonNode.Parse(model.Utf8Json.Span)!.AsObject();
            root.Remove("uav:id");
            return WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
        }

        private static async Task<WotConversionResult<UANodeSet>> ReviewImportAsync(
            WotDocumentSet documents,
            bool preconverted)
        {
            if (!preconverted)
            {
                return await WotNodeSetConverter.ToNodeSetAsync(documents, IndependentOptions()).ConfigureAwait(false);
            }
            var parts = new List<UANodeSet>();
            for (int index = 0; index < documents.Entries.Count; index++)
            {
                WotDocumentSetEntry entry = documents.Entries[index];
                using var singleton = new WotDocumentSet(entry.Href,
                    [new(entry.Href, WotDocument.Parse(entry.Document.Utf8Json))]);
                WotConversionResult<UANodeSet> converted =
                    await WotNodeSetConverter.ToNodeSetAsync(singleton, IndependentOptions()).ConfigureAwait(false);
                Assert.That(converted.Success, Is.True, Describe(converted));
                parts.Add(converted.Value!);
            }
            return WotNodeSetConverter.MergeNodeSetPartitions(
                documents, parts.ToArrayOf(), IndependentOptions());
        }
    }
}
