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
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Proves that what the converter hands back can actually be loaded.
    /// </summary>
    /// <remarks>
    /// A converted NodeSet2 document is only useful if a Server can import it,
    /// and the importer rejects any name used where a NodeId is expected that
    /// the document does not declare in <c>&lt;Aliases&gt;</c>. Nothing in the
    /// converter test suite used to call <c>Import</c>, so a document that
    /// parsed, compared equal and round-tripped could still fail to load. These
    /// tests run the same Write - Read - Import sequence the runtime
    /// materialization path runs.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotNodeSetImportTests
    {
        [Test]
        public void EveryPublishedExampleThatConvertsCanBeImported()
        {
            IReadOnlyList<string> names = ExampleNames();
            Assert.That(names, Is.Not.Empty, "The example fixtures should be embedded.");

            var imported = new List<string>();
            foreach (string name in names)
            {
                using WotDocument document = WotDocument.Parse(ReadExample(name));
                WotConversionResult<UANodeSet> result =
                    WotNodeSetConverter.ToNodeSetResult(document);
                if (result.Value is null || result.HasErrors)
                {
                    // A projection document declares affordances it does not
                    // define, so it converts to no NodeSet at all. Those are
                    // covered by the projection tests; only what converts can
                    // be imported.
                    continue;
                }

                AssertImportable(result.Value, name);
                imported.Add(name);
            }

            Assert.That(
                imported,
                Is.Not.Empty,
                "At least one published example should convert to a NodeSet.");
        }

        [Test]
        public async Task SynthesizedNodeSetCanBeImportedAsync()
        {
            using WotDocument document = WotDocument.Parse(
                ReadExample("01-opcua-td-pump.jsonld"));

            WotConversionResult<UANodeSet> result =
                await WotSpecExampleResolver.ConvertAsync(document).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Is.Empty);

            NodeStateCollection nodes = AssertImportable(result.Value!, "readable synthesis");
            Assert.That(nodes, Is.Not.Empty);
        }

        [Test]
        public void NativelyProjectedNodeSetCanBeImported()
        {
            using WotDocument document = WotDocument.Parse(
                ReadExample("05-native-node-model.jsonld"));
            Assert.That(
                document.TryGetNativeProjection(out _),
                Is.True,
                "The fixture should take the uav:nodes restoration path.");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(document);

            AssertImportable(nodeSet, "uav:nodes restoration");
        }

        [Test]
        public void EnvelopeRestoredNodeSetCanBeImported()
        {
            using WotDocument document = WotDocument.Parse(
                ReadExample("03-nodeset-preservation-envelope.jsonld"));
            Assert.That(
                document.TryGetEnvelope(out _),
                Is.True,
                "The fixture should take the uav:nodeSet restoration path.");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(document);

            AssertImportable(nodeSet, "uav:nodeSet restoration");
        }

        [Test]
        public void DataTypeDefinitionNodeSetCanBeImported()
        {
            using WotDocument document = WotDocument.Parse(
                ReadExample("23-datatype-definitions.jsonld"));

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(document);

            AssertImportable(nodeSet, "DataType definitions");
        }

        [Test]
        public void RoundTripThroughEveryPreservationModeStaysImportable()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();

            foreach (WotNodeSetPreservationMode mode in new[]
            {
                WotNodeSetPreservationMode.Never,
                WotNodeSetPreservationMode.WhenRequired,
                WotNodeSetPreservationMode.Always
            })
            {
                var options = new WotNodeSetConverterOptions { PreservationMode = mode };
                using WotDocument document = WotNodeSetConverter.FromNodeSet(source, options: options);
                UANodeSet restored = WotNodeSetConverter.ToNodeSet(document, options);

                AssertImportable(restored, mode.ToString());
            }
        }

        [Test]
        public void ANameTheConverterCannotResolveStillFailsTheImport()
        {
            // The completion pass declares only what it can resolve, so an
            // undeclared vendor alias is reported rather than swallowed.
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test:unresolved"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:unresolved" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:VendorType",
                        DisplayName = [new Export.LocalizedText { Value = "VendorType" }],
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "VendorSpecificReference",
                                IsForward = true,
                                Value = "i=58"
                            }
                        ]
                    }
                ]
            };

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => Import(nodeSet))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(exception.Message, Does.Contain("VendorSpecificReference"));
        }

        /// <summary>
        /// Serializes a converted NodeSet, reads it back and imports it, which
        /// is exactly what the runtime materialization path does.
        /// </summary>
        internal static NodeStateCollection AssertImportable(UANodeSet nodeSet, string what)
        {
            Assert.That(nodeSet, Is.Not.Null, $"'{what}' produced no NodeSet.");

            byte[] xml;
            using (var buffer = new MemoryStream())
            {
                nodeSet.Write(buffer);
                xml = buffer.ToArray();
            }

            UANodeSet reread;
            using (var buffer = new MemoryStream(xml, writable: false))
            {
                reread = UANodeSet.Read(buffer)!;
            }
            Assert.That(reread, Is.Not.Null, $"'{what}' did not re-read as a NodeSet.");

            NodeStateCollection nodes = null!;
            Assert.DoesNotThrow(
                () => nodes = Import(reread),
                $"'{what}' produced a NodeSet that cannot be imported:{Environment.NewLine}" +
                Encoding.UTF8.GetString(xml));
            return nodes;
        }

        private static NodeStateCollection Import(UANodeSet nodeSet)
        {
            var namespaces = new NamespaceTable();
            foreach (string namespaceUri in nodeSet.NamespaceUris ?? [])
            {
                namespaces.GetIndexOrAppend(namespaceUri);
            }
            var context = new SystemContext(telemetry: null!) { NamespaceUris = namespaces };
            var nodes = new NodeStateCollection();
            nodeSet.Import(context, nodes);
            return nodes;
        }

        private static IReadOnlyList<string> ExampleNames()
        {
            return [.. typeof(WotNodeSetImportTests).Assembly
                .GetManifestResourceNames()
                .Where(n => n.Contains(ResourcePrefix, StringComparison.Ordinal) &&
                    n.EndsWith(".jsonld", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal)];
        }

        private static byte[] ReadExample(string name)
        {
            string resource = ExampleNames()
                .Single(n => n.EndsWith(name, StringComparison.Ordinal));
            using Stream stream = typeof(WotNodeSetImportTests).Assembly
                .GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing fixture '{name}'.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        private const string ResourcePrefix = "Wot.Assets.";
    }
}
