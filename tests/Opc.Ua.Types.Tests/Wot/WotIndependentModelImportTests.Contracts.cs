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
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    public sealed partial class WotIndependentModelImportTests
    {
        [TestCase(WotDocumentSetMode.PartitionReconstruction, false)]
        [TestCase(WotDocumentSetMode.IndependentReadableModels, false)]
        [TestCase(WotDocumentSetMode.PartitionReconstruction, true)]
        [TestCase(WotDocumentSetMode.IndependentReadableModels, true)]
        public async Task EveryModeRejectsAuthoritativeNamespaceReorderingAsync(WotDocumentSetMode mode, bool archive)
        {
            UANodeSet first = CreateAuthoritativePartition(1);
            UANodeSet second = CreateAuthoritativePartition(2);
            first.NamespaceUris = ["urn:test:a", "urn:test:b"];
            second.NamespaceUris = ["urn:test:b", "urn:test:a"];
            first.Aliases = null;
            second.Aliases = null;
            second.Items![0].NodeId = "ns=2;i=2";
            using var documents = new WotDocumentSet(
                "a",
                [new("a", AuthoritativeDocument(first, archive)), new("b", AuthoritativeDocument(second, archive))]);
            var options = new WotNodeSetConverterOptions { DocumentSetMode = mode };

            WotConversionResult<UANodeSet> converted =
                await WotNodeSetConverter.ToNodeSetAsync(documents, options).ConfigureAwait(false);
            WotConversionResult<UANodeSet> merged =
                WotNodeSetConverter.MergeNodeSetPartitions(documents, [first, second], options);

            foreach (WotConversionResult<UANodeSet> result in new[] { converted, merged })
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    diagnostic.Message == "Document-set partitions do not share a coherent namespace table." &&
                    diagnostic.Location?.Reference == "b"), Is.True, Describe(result));
            }
        }

        [TestCase(WotDocumentSetMode.PartitionReconstruction, false)]
        [TestCase(WotDocumentSetMode.IndependentReadableModels, false)]
        [TestCase(WotDocumentSetMode.PartitionReconstruction, true)]
        [TestCase(WotDocumentSetMode.IndependentReadableModels, true)]
        public async Task EveryModeRejectsConflictingAuthoritativeHeadersAsync(WotDocumentSetMode mode, bool archive)
        {
            UANodeSet first = CreateAuthoritativePartition(1);
            UANodeSet second = CreateAuthoritativePartition(2);
            second.Models![0].Version = "conflicting";
            using var documents = new WotDocumentSet(
                "a",
                [new("a", AuthoritativeDocument(first, archive)), new("b", AuthoritativeDocument(second, archive))]);
            var options = new WotNodeSetConverterOptions { DocumentSetMode = mode };

            WotConversionResult<UANodeSet> converted =
                await WotNodeSetConverter.ToNodeSetAsync(documents, options).ConfigureAwait(false);
            WotConversionResult<UANodeSet> merged =
                WotNodeSetConverter.MergeNodeSetPartitions(documents, [first, second], options);

            foreach (WotConversionResult<UANodeSet> result in new[] { converted, merged })
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    diagnostic.Message == "Authoritative document-set headers disagree." &&
                    diagnostic.Location?.Reference == "b"), Is.True, Describe(result));
            }
        }

        [TestCase(WotDocumentSetMode.PartitionReconstruction, false)]
        [TestCase(WotDocumentSetMode.IndependentReadableModels, false)]
        [TestCase(WotDocumentSetMode.PartitionReconstruction, true)]
        [TestCase(WotDocumentSetMode.IndependentReadableModels, true)]
        public async Task EveryModeRejectsReadableOverlaysOnAuthoritativePartitionsAsync(
            WotDocumentSetMode mode,
            bool archive)
        {
            using WotDocument readable = CreateModel("urn:test:b");
            JsonObject altered = JsonNode.Parse(readable.Utf8Json.Span)!.AsObject();
            altered["properties"] = new JsonObject
            {
                ["Injected"] = new JsonObject
                {
                    ["uav:id"] = "nsu=urn:test:a;s=Injected",
                    ["uav:componentOf"] = new JsonArray("nsu=urn:test:a;i=1"),
                    ["type"] = "boolean"
                }
            };
            using var documents = new WotDocumentSet(
                "a",
                [
                    new("a", AuthoritativeDocument(CreateAuthoritativePartition(1), archive)),
                    new("b", WotDocument.Parse(Encoding.UTF8.GetBytes(altered.ToJsonString())))
                ]);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetAsync(
                documents, new WotNodeSetConverterOptions { DocumentSetMode = mode }).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                diagnostic.Message.StartsWith("A readable document cannot add a Node", StringComparison.Ordinal) &&
                diagnostic.Location?.Reference == "b"), Is.True, Describe(result));
        }

        [Test]
        public void IndependentPartitionsPreserveModelConstraintsAndMultilingualMetadata()
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            UANodeSet b = CreatePartition("urn:test:b");
            var date = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            b.Models![0].Version = "2.4";
            b.Models[0].ModelVersion = "2.4.0";
            b.Models[0].XmlSchemaUri = "urn:test:b:xml";
            b.Models[0].PublicationDate = date;
            b.Models[0].PublicationDateSpecified = true;
            b.Models[0].AccessRestrictions = 3;
            b.Models[0].RequiredModel =
            [
                new ModelTableEntry
                {
                    ModelUri = "urn:test:a",
                    Version = "1.0",
                    ModelVersion = "1.0.0",
                    PublicationDate = date,
                    PublicationDateSpecified = true
                }
            ];
            b.LastModified = date;
            b.LastModifiedSpecified = true;
            b.Items![0].DisplayName =
            [
                new Export.LocalizedText { Locale = "en", Value = "Root" },
                new Export.LocalizedText { Locale = "de", Value = "Wurzel" }
            ];
            b.Items[0].Description = [new Export.LocalizedText { Locale = "de", Value = "ns=1;i=42" }];
            b.Items[0].Category = ["ns=1;category"];
            b.Items[0].Documentation = "source-b";
            b.Items[0].Extensions =
                [WotTestData.ParseValue("<origin xmlns=\"urn:test:provenance\">source-b ns=1;i=42</origin>")];
            byte[] original = Serialize(b);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.MergeNodeSetPartitions(
                documents, [CreatePartition("urn:test:a"), b], IndependentOptions());

            Assert.That(result.Success, Is.True, Describe(result));
            ModelTableEntry model = result.Value!.Models!.Single(item => item.ModelUri == "urn:test:b");
            Assert.That(model.Version, Is.EqualTo("2.4"));
            Assert.That(model.ModelVersion, Is.EqualTo("2.4.0"));
            Assert.That(model.XmlSchemaUri, Is.EqualTo("urn:test:b:xml"));
            Assert.That(model.PublicationDate, Is.EqualTo(date));
            Assert.That(model.PublicationDateSpecified, Is.True);
            Assert.That(model.AccessRestrictions, Is.EqualTo(3));
            Assert.That(model.RequiredModel, Has.Length.EqualTo(1));
            Assert.That(model.RequiredModel![0].ModelUri, Is.EqualTo("urn:test:a"));
            Assert.That(model.RequiredModel[0].Version, Is.EqualTo("1.0"));
            Assert.That(model.RequiredModel[0].ModelVersion, Is.EqualTo("1.0.0"));
            Assert.That(model.RequiredModel[0].PublicationDate, Is.EqualTo(date));
            Assert.That(model.RequiredModel[0].PublicationDateSpecified, Is.True);
            Assert.That(result.Value.LastModified, Is.EqualTo(date));
            Assert.That(result.Value.LastModifiedSpecified, Is.True);
            UANode node = result.Value.Items!.Single(item => item.NodeId == "ns=2;i=1");
            Assert.That(node.DisplayName!.Select(text => (text.Locale, text.Value)), Is.EqualTo(s_localizedNames));
            Assert.That(node.Description![0].Locale, Is.EqualTo("de"));
            Assert.That(node.Description[0].Value, Is.EqualTo("ns=1;i=42"));
            Assert.That(node.Category, Is.EqualTo(s_literalCategories));
            Assert.That(node.Documentation, Is.EqualTo("source-b"));
            Assert.That(node.Extensions![0].NamespaceURI, Is.EqualTo("urn:test:provenance"));
            Assert.That(node.Extensions[0].InnerText, Is.EqualTo("source-b ns=1;i=42"));
            Assert.That(Serialize(b), Is.EqualTo(original));
        }

        [TestCase("version")]
        [TestCase("modelVersion")]
        [TestCase("publicationDate")]
        [TestCase("schema")]
        [TestCase("requiredModel")]
        [TestCase("permissions")]
        [TestCase("accessRestrictions")]
        [TestCase("lastModified")]
        [TestCase("servers")]
        public void IndependentPartitionsRejectConflictingModelFacts(string fact)
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:a", 2))]);
            UANodeSet a = CreatePartition("urn:test:a");
            UANodeSet b = CreatePartition("urn:test:a");
            b.Items![0].NodeId = "ns=1;i=2";
            ModelTableEntry model = b.Models![0];
            ModelTableEntry baseline = a.Models![0];
            switch (fact)
            {
                case "version":
                    baseline.Version = "1";
                    model.Version = "2";
                    break;
                case "modelVersion":
                    baseline.ModelVersion = "1.0.0";
                    model.ModelVersion = "2.0.0";
                    break;
                case "publicationDate":
                    baseline.PublicationDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    baseline.PublicationDateSpecified = true;
                    model.PublicationDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
                    model.PublicationDateSpecified = true;
                    break;
                case "schema":
                    baseline.XmlSchemaUri = "urn:original:schema";
                    model.XmlSchemaUri = "urn:different:schema";
                    break;
                case "requiredModel":
                    baseline.RequiredModel = [new ModelTableEntry { ModelUri = "urn:dependency", Version = "1" }];
                    model.RequiredModel = [new ModelTableEntry { ModelUri = "urn:dependency", Version = "99" }];
                    break;
                case "permissions":
                    baseline.RolePermissions = [new RolePermission { Value = "i=15644", Permissions = 2 }];
                    model.RolePermissions = [new RolePermission { Value = "i=15644", Permissions = 1 }];
                    break;
                case "accessRestrictions":
                    baseline.AccessRestrictions = 2;
                    model.AccessRestrictions = 1;
                    break;
                case "lastModified":
                    a.LastModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    a.LastModifiedSpecified = true;
                    b.LastModified = a.LastModified.AddDays(1);
                    b.LastModifiedSpecified = true;
                    break;
                default:
                    b.ServerUris = ["urn:server"];
                    break;
            }

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.MergeNodeSetPartitions(documents, [a, b], IndependentOptions());

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                diagnostic.Location?.Reference == "b"), Is.True, Describe(result));
        }

        [Test]
        public void IndependentPartitionsDeduplicateNamespaceUrisAndEquivalentSharedNodes()
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            UANodeSet a = CreatePartition("urn:test:a");
            a.NamespaceUris = ["urn:test:a", "urn:test:shared"];
            a.Items = [a.Items![0], new UAObjectType { NodeId = "ns=2;i=9", BrowseName = "2:Shared" }];
            UANodeSet b = CreateValuePartition(
                "<uax:NodeId><uax:Identifier>ns=2;i=42</uax:Identifier></uax:NodeId>");
            b.NamespaceUris = ["urn:test:b", "urn:test:b", "urn:test:shared"];
            b.Items = [.. b.Items!, new UAObjectType { NodeId = "ns=3;i=9", BrowseName = "3:Shared" }];

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.MergeNodeSetPartitions(documents, [a, b], IndependentOptions());

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.NamespaceUris, Is.EqualTo(s_sharedNamespaces));
            Assert.That(result.Value.Items!.Count(node => node.NodeId == "ns=3;i=9"), Is.EqualTo(1));
            Assert.That(result.Value.Items, Has.Length.EqualTo(4));
            Assert.That(ValueText(result.Value.Items!.OfType<UAVariable>().Single().Value!, "Identifier"),
                Is.EqualTo("ns=2;i=42"));
        }

        [Test]
        public void IndependentPartitionsRejectConflictingSharedNodes()
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            UANodeSet a = CreatePartition("urn:test:a");
            a.NamespaceUris = ["urn:test:a", "urn:test:shared"];
            a.Items = [a.Items![0], new UAObjectType { NodeId = "ns=2;i=9", BrowseName = "2:Shared" }];
            UANodeSet b = CreatePartition("urn:test:b");
            b.NamespaceUris = ["urn:test:b", "urn:test:shared"];
            b.Items = [b.Items![0], new UAVariableType { NodeId = "ns=2;i=9", BrowseName = "2:Shared" }];

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.MergeNodeSetPartitions(documents, [a, b], IndependentOptions());

            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                diagnostic.Location?.NodeId == "ns=3;i=9" &&
                diagnostic.Location.Reference == "b"), Is.True, Describe(result));
        }

        [TestCase("nodes", -1)]
        [TestCase("nodes", 0)]
        [TestCase("nodes", 1)]
        [TestCase("documents", -1)]
        [TestCase("documents", 0)]
        [TestCase("documents", 1)]
        [TestCase("json", -1)]
        [TestCase("json", 0)]
        [TestCase("json", 1)]
        [TestCase("documentBytes", -1)]
        [TestCase("documentBytes", 0)]
        [TestCase("documentBytes", 1)]
        [TestCase("totalBytes", -1)]
        [TestCase("totalBytes", 0)]
        [TestCase("totalBytes", 1)]
        [TestCase("resultBytes", -1)]
        [TestCase("resultBytes", 0)]
        [TestCase("resultBytes", 1)]
        public async Task IndependentReadableImportHonorsExactAggregateLimitsAsync(string bound, int delta)
        {
            using var documents = new WotDocumentSet(
                "a", [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            WotNodeSetConverterOptions options = IndependentOptions();
            switch (bound)
            {
                case "nodes":
                    options.MaxNodeCount = 2 + delta;
                    break;
                case "documents":
                    options.MaxResolverDocuments = 2 + delta;
                    break;
                case "json":
                    options.MaxJsonDocumentSize = documents.Entries[0].Document.Utf8Json.Length + delta;
                    break;
                case "documentBytes":
                    options.MaxResolverDocumentBytes = documents.Entries[0].Document.Utf8Json.Length + delta;
                    break;
                case "totalBytes":
                    options.MaxResolverTotalBytes = documents.Entries[0].Document.Utf8Json.Length +
                        documents.Entries[1].Document.Utf8Json.Length + delta;
                    break;
                default:
                    WotConversionResult<UANodeSet> baseline =
                        await WotNodeSetConverter.ToNodeSetAsync(documents, options).ConfigureAwait(false);
                    Assert.That(baseline.Success, Is.True, Describe(baseline));
                    options.MaxNodeSetSize = Serialize(baseline.Value!).Length + delta;
                    break;
            }

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetAsync(documents, options).ConfigureAwait(false);

            Assert.That(result.Success, Is.EqualTo(delta >= 0), Describe(result));
            if (delta < 0)
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error &&
                    diagnostic.Location?.Reference is not null), Is.True, Describe(result));
            }
        }

        [TestCase(WotDocumentSetMode.PartitionReconstruction)]
        [TestCase(WotDocumentSetMode.IndependentReadableModels)]
        public async Task EmptyAndSingleDocumentSetsReportTheirSelectedModeAsync(WotDocumentSetMode mode)
        {
            var options = new WotNodeSetConverterOptions { DocumentSetMode = mode };
            using var empty = new WotDocumentSet("empty", []);
            using var single = new WotDocumentSet("b", [new("b", CreateModel("urn:test:b"))]);

            WotConversionResult<UANodeSet> emptyResult =
                await WotNodeSetConverter.ToNodeSetAsync(empty, options).ConfigureAwait(false);
            WotConversionResult<UANodeSet> singleResult =
                await WotNodeSetConverter.ToNodeSetAsync(single, options).ConfigureAwait(false);

            Assert.That(emptyResult.Success, Is.True, Describe(emptyResult));
            Assert.That(emptyResult.Value!.Items, Is.Null.Or.Empty);
            Assert.That(singleResult.Success, Is.True, Describe(singleResult));
            Assert.That(singleResult.Value!.Items, Has.Length.EqualTo(1));
            Assert.That(singleResult.Value.Items![0].NodeId, Is.EqualTo("ns=1;i=1"));
            foreach (WotConversionResult<UANodeSet> result in new[] { emptyResult, singleResult })
            {
                Assert.That(result.Diagnostics.Count(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.DocumentSetModeSelected &&
                    diagnostic.Message.EndsWith(mode + ".", StringComparison.Ordinal)), Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IndependentReadableImportHonorsCancellationEvenWhenEmpty(bool empty)
        {
            using var documents = new WotDocumentSet(
                "a", empty ? [] : [new("a", CreateModel("urn:test:a")), new("b", CreateModel("urn:test:b"))]);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(async () => await WotNodeSetConverter.ToNodeSetAsync(
                documents, IndependentOptions(), cancellationToken: cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void IndependentPartitionMergeHonorsCancellation()
        {
            using var documents = new WotDocumentSet("b", [new("b", CreateModel("urn:test:b"))]);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(() => WotNodeSetConverter.MergeNodeSetPartitions(
                documents, [CreatePartition("urn:test:b")], IndependentOptions(), cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
        }

        private static UANodeSet CreateAuthoritativePartition(uint identifier)
        {
            UANodeSet part = CreatePartition("urn:test:a");
            part.Items![0].NodeId = new NodeId(identifier, 1).ToString();
            part.Items[0].BrowseName = identifier == 1 ? "1:First" : "1:Second";
            part.Items[0].SymbolicName = identifier == 1 ? "FirstSymbol" : "SecondSymbol";
            part.Items[0].References =
                [new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=58" }];
            return part;
        }

        private static WotDocument AuthoritativeDocument(UANodeSet source, bool archive)
        {
            return WotNodeSetConverter.FromNodeSet(source, options: new WotNodeSetConverterOptions
            {
                PreservationMode = archive ? WotNodeSetPreservationMode.Always : WotNodeSetPreservationMode.Never
            });
        }

        private static readonly (string Locale, string Value)[] s_localizedNames = [("en", "Root"), ("de", "Wurzel")];
        private static readonly string[] s_literalCategories = ["ns=1;category"];
        private static readonly string[] s_sharedNamespaces = ["urn:test:a", "urn:test:b", "urn:test:shared"];
    }
}
