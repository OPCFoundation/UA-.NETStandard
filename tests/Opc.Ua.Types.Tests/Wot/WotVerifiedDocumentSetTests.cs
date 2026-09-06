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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotVerifiedDocumentSetTests
    {
        [Test]
        public async Task CompleteReadableSetIsVerifiedWithoutNativeFallbackAsync()
        {
            UANodeSet source = CreateReadableRoots();
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Never
            };

            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                    source, "model", options: options).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            using WotDocumentSet set = result.Value!;
            Assert.That(set.Entries, Has.Count.EqualTo(2));
            Assert.That(
                set.Entries.ToArray()!.Select(entry => entry.Href),
                Is.EqualTo(s_readableRootHrefs));
            foreach (WotDocumentSetEntry entry in set.Entries)
            {
                Assert.That(entry.Document.TryGetNativeProjection(out _), Is.False);
                Assert.That(entry.Document.TryGetEnvelope(out _), Is.False);
            }
            await AssertEquivalentAsync(source, set, options).ConfigureAwait(false);
        }

        [Test]
        public async Task UnrepresentableMetadataUsesLinkedPartitionsWithoutReadableOverlaysAsync()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Never
            };

            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                    source, "model", options: options).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(
                result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.NativeProjectionIncomplete &&
                    diagnostic.Severity == WotDiagnosticSeverity.Warning),
                Is.True);
            using WotDocumentSet set = result.Value!;
            Assert.That(set.Entries, Has.Count.GreaterThan(1));
            Assert.That(set.RootHref, Is.EqualTo("model"));
            Assert.That(set.Entries[0].Document.TryGetNativeProjection(out _), Is.True);
            Assert.That(set.Entries[0].Document.TryGetEnvelope(out _), Is.False);
            await AssertEquivalentAsync(source, set, options).ConfigureAwait(false);
        }

        [Test]
        public async Task LinkedObjectsPreserveParentsAndDeclaredVariableOwnershipReadablyAsync()
        {
            var source = new UANodeSet
            {
                NamespaceUris = ["urn:test:documents"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:documents" }],
                Aliases =
                [
                    new NodeIdAlias { Alias = "Double", Value = "i=11" },
                    new NodeIdAlias { Alias = "HasComponent", Value = "i=47" },
                    new NodeIdAlias { Alias = "HasTypeDefinition", Value = "i=40" }
                ],
                Items =
                [
                    new UAObject
                    {
                        NodeId = "ns=1;s=Pump",
                        BrowseName = "1:Pump",
                        DisplayName = [new Export.LocalizedText { Value = "Pump" }],
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", Value = "i=58" },
                            new Reference { ReferenceType = "HasComponent", Value = "ns=1;s=Child" }
                        ]
                    },
                    new UAObject
                    {
                        NodeId = "ns=1;s=Child",
                        BrowseName = "1:Child",
                        DisplayName = [new Export.LocalizedText { Value = "Child" }],
                        ParentNodeId = "ns=1;s=Pump",
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", Value = "i=58" },
                            new Reference { ReferenceType = "HasComponent", Value = "ns=1;s=Speed" },
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = false,
                                Value = "ns=1;s=Pump"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;s=Speed",
                        BrowseName = "1:Speed",
                        DisplayName = [new Export.LocalizedText { Value = "Speed" }],
                        ParentNodeId = "ns=1;s=Child",
                        DataType = "Double",
                        Value = WotTestData.ParseValue(
                            "<uax:Double xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                            "42.5</uax:Double>"),
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", Value = "i=68" },
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = false,
                                Value = "ns=1;s=Child"
                            }
                        ]
                    }
                ]
            };
            var resolver = new Mock<IWotNodeResolver>();
            resolver.Setup(context => context.ResolveByNodeIdAsync("i=58", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WotResolvedNode?>(
                    new WotResolvedNode("i=58", WotExpectedNodeClass.ObjectType)));
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Never
            };

            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                    source, "pump", options: options, nodeResolver: resolver.Object).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            using WotDocumentSet set = result.Value!;
            Assert.That(set.Entries, Has.Count.EqualTo(2), Describe(result));
            foreach (WotDocumentSetEntry entry in set.Entries)
            {
                Assert.That(entry.Document.TryGetNativeProjection(out _), Is.False);
                Assert.That(entry.Document.TryGetEnvelope(out _), Is.False);
            }
            await AssertEquivalentAsync(source, set, options, resolver.Object).ConfigureAwait(false);
        }

        [Test]
        public async Task PartitionArchivesRetainDocumentTablesAndExtensionsAsync()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Always
            };

            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                    source, "archive", "Explicit archival title", options).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            using WotDocumentSet set = result.Value!;
            Assert.That(set.Entries, Has.Count.GreaterThan(1));
            foreach (WotDocumentSetEntry entry in set.Entries)
            {
                Assert.That(entry.Document.TryGetEnvelope(out _), Is.True);
            }
            await AssertEquivalentAsync(source, set, options).ConfigureAwait(false);
        }

        [Test]
        public async Task EqualBrowseNamesInSharedNamespacesKeepDistinctIdentitiesAsync()
        {
            UANodeSet source = CreateReadableRoots();
            source.NamespaceUris = ["urn:test:documents", "urn:test:other"];
            source.Items![0].BrowseName = "1:Pump";
            source.Items[1].NodeId = "ns=2;s=BetaType";
            source.Items[1].BrowseName = "2:Pump";
            source.Aliases =
            [
                new NodeIdAlias { Alias = "SubtypeAlias", Value = "i=45" }
            ];
            foreach (UANode node in source.Items)
            {
                node.References![0].ReferenceType = "SubtypeAlias";
            }

            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(source, "pumps").ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            using WotDocumentSet set = result.Value!;
            Assert.That(
                set.Entries.ToArray()!.Select(entry => entry.Href).Distinct().Count(),
                Is.EqualTo(set.Entries.Count));
            await AssertEquivalentAsync(source, set, new WotNodeSetConverterOptions()).ConfigureAwait(false);
        }

        [Test]
        public async Task UnsupportedRawRangeEncodingIsRetainedRatherThanReinterpretedAsync()
        {
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet(withInstrumentRange: false);
            UAVariable range = source.Items!.OfType<UAVariable>().Single(node => node.BrowseName == "EURange");
            range.Value = WotTestData.ParseValue(
                "<uax:ExtensionObject xmlns:uax=\"" + WotAnalogTestData.UaXmlNamespace +
                "\"><uax:TypeId><uax:Identifier>i=886</uax:Identifier></uax:TypeId>" +
                "<uax:Body><uax:Range><uax:Low>0</uax:Low><uax:High>100</uax:High>" +
                "</uax:Range></uax:Body></uax:ExtensionObject>");
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Never
            };

            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                    source, "analog", options: options).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            using WotDocumentSet set = result.Value!;
            Assert.That(set.Entries[0].Document.TryGetNativeProjection(out _), Is.True);
            await AssertEquivalentAsync(source, set, options).ConfigureAwait(false);
        }

        [Test]
        public async Task VerifiedExportReportsSizeLimitWithoutPartialSuccessAsync()
        {
            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                    CreateReadableRoots(), "model",
                    options: new WotNodeSetConverterOptions { MaxNodeSetSize = 1 }).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.NodeSetTooLarge),
                Is.True);
        }

        [Test]
        public void ReadableExportRejectsAnOversizedSiblingWithoutReturningAPartialSet()
        {
            UANodeSet source = CreateReadableRoots();
            source.Items![1].DisplayName = [new Export.LocalizedText { Value = new string('x', 4096) }];
            WotConversionResult<WotDocumentSet> unrestricted =
                WotNodeSetConverter.FromNodeSetDocuments(source, "model");
            Assert.That(unrestricted.Success, Is.True, Describe(unrestricted));
            using WotDocumentSet reference = unrestricted.Value!;
            var options = new WotNodeSetConverterOptions
            {
                MaxJsonDocumentSize = reference.Entries[0].Document.Utf8Json.Length
            };

            WotConversionResult<WotDocumentSet> result =
                WotNodeSetConverter.FromNodeSetDocuments(source, "model", options: options);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.JsonDocumentTooLarge &&
                    diagnostic.Location?.Reference == "model-betatype"),
                Is.True,
                Describe(result));
        }

        [Test]
        public async Task VerifiedExportDoesNotAcceptOversizedReadableDocumentsAsync()
        {
            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                    CreateReadableRoots(), "model",
                    options: new WotNodeSetConverterOptions { MaxJsonDocumentSize = 1 }).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.JsonDocumentTooLarge),
                Is.True);
        }

        [Test]
        public async Task OnlyIncompletePartitionUsesNativePreservationAsync()
        {
            UANodeSet source = CreateReadableRoots();
            source.Items![0].SymbolicName = "RetainedSymbol";
            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(source, "model").ConfigureAwait(false);
            Assert.That(result.Success, Is.True, Describe(result));
            using WotDocumentSet set = result.Value!;
            Assert.That(set.Entries, Has.Count.EqualTo(2));
            Assert.That(set.Entries[0].Document.TryGetNativeProjection(out _), Is.True);
            Assert.That(set.Entries[1].Document.TryGetNativeProjection(out _), Is.False);
            using WotDocumentSet reloaded = Reload(set);
            await AssertEquivalentAsync(source, reloaded, new WotNodeSetConverterOptions()).ConfigureAwait(false);
        }

        [Test]
        public async Task InverseOnlyNestedObjectsRetainSeparatePartitionsAsync()
        {
            var source = new UANodeSet
            {
                NamespaceUris = ["urn:test:documents"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:documents" }],
                Aliases = [new NodeIdAlias { Alias = "HasComponent", Value = "i=47" }],
                Items =
                [
                    new UAObject { NodeId = "ns=1;s=Parent", BrowseName = "1:Parent" },
                    new UAObject
                    {
                        NodeId = "ns=1;s=Child",
                        BrowseName = "1:Child",
                        ParentNodeId = "ns=1;s=Parent",
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = false,
                                Value = "ns=1;s=Parent"
                            }
                        ]
                    }
                ]
            };
            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(source, "parent").ConfigureAwait(false);
            Assert.That(result.Success, Is.True, Describe(result));
            using WotDocumentSet set = result.Value!;
            Assert.That(set.Entries, Has.Count.EqualTo(2));
            using WotDocumentSet reloaded = Reload(set);
            await AssertEquivalentAsync(source, reloaded, new WotNodeSetConverterOptions()).ConfigureAwait(false);
        }

        [TestCase("tests\\Opc.Ua.SourceGeneration.Core.Tests\\Resources\\Opc.Ua.Di.NodeSet2.xml")]
        [TestCase("samples\\DI\\PumpDeviceIntegrationServer\\Model\\Opc.Ua.Machinery.NodeSet2.xml")]
        [TestCase("samples\\DI\\PumpDeviceIntegrationServer\\Model\\Opc.Ua.Pumps.NodeSet2.xml")]
        [TestCase("samples\\WotCon\\AggregationClient\\Documents\\SamplePump.NodeSet2.xml")]
        public async Task RepositorySourceSurvivesSerializedLinkedPartitionsAsync(string relativePath)
        {
            DirectoryInfo directory = new(TestContext.CurrentContext.TestDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
            {
                directory = directory.Parent;
            }
            Assert.That(directory, Is.Not.Null);
            using FileStream stream = File.OpenRead(Path.Combine(directory!.FullName, relativePath));
            UANodeSet source = UANodeSet.Read(stream)!;
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Never
            };
            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(source, "model", options: options)
                    .ConfigureAwait(false);
            Assert.That(result.Success, Is.True, Describe(result));
            using WotDocumentSet set = result.Value!;
            Assert.That(set.Entries, Has.Count.GreaterThan(1));
            var owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (WotDocumentSetEntry entry in set.Entries)
            {
                Assert.That(entry.Document.TryGetEnvelope(out _), Is.False);
                if (entry.Document.TryGetNativeProjection(out JsonElement native))
                {
                    foreach (JsonElement node in native.GetProperty("nodes").EnumerateArray())
                    {
                        Assert.That(owned.Add(node.GetProperty("nodeId").GetString()!), Is.True, entry.Href);
                    }
                }
            }
            using WotDocumentSet reloaded = Reload(set);
            await AssertEquivalentAsync(source, reloaded, options).ConfigureAwait(false);
            TestContext.Out.WriteLine(
                $"{Path.GetFileName(relativePath)}: {set.Entries.Count} linked partitions, " +
                $"{set.Entries.ToArray().Count(entry => entry.Document.TryGetNativeProjection(out _))} native.");
        }

        [Test]
        public void LinkedComparisonIgnoresOnlyTopLevelNodeOrder()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            UANodeSet reordered = WotTestData.CreateRichNodeSet();
            Array.Reverse(reordered.Items!);
            Assert.That(WotNodeSetConverter.CompareDocumentSet(source, reordered).AreEquivalent, Is.True);
            Assert.That(NodeSetComparer.CompareEquivalent(source, reordered).AreEquivalent, Is.False);
            Assert.That(source.Items![0].NodeId, Is.EqualTo("ns=1;i=1001"));
        }

        [Test]
        public async Task ConvertedPartitionsMergeWithoutMutatingTheirSourceNodes()
        {
            UANodeSet source = CreateReadableRoots();
            source.Items![0].SymbolicName = "Alpha";
            source.Items[1].SymbolicName = "Beta";
            WotConversionResult<WotDocumentSet> exported = await WotNodeSetConverter
                .FromNodeSetDocumentsAsync(source, "model").ConfigureAwait(false);
            Assert.That(exported.Success, Is.True, Describe(exported));
            using WotDocumentSet documents = exported.Value!;
            ArrayOf<UANodeSet> partitions = documents.Entries.ConvertAll(entry =>
                WotNodeSetConverter.ToNodeSet(entry.Document));
            string[] referenceTypes = partitions.ToList()
                .Select(partition => partition.Items![0].References![0].ReferenceType!).ToArray();

            WotConversionResult<UANodeSet> merged = WotNodeSetConverter.MergeNodeSetPartitions(documents, partitions);

            Assert.That(merged.Success, Is.True, Describe(merged));
            Assert.That(WotNodeSetConverter.CompareDocumentSet(source, merged.Value!).AreEquivalent, Is.True);
            Assert.That(partitions.ToList().Select(partition => partition.Items![0].References![0].ReferenceType),
                Is.EqualTo(referenceTypes));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ConvertedPartitionMergeHonorsNodeAndByteBounds(bool nodeBound)
        {
            WotConversionResult<WotDocumentSet> exported = await WotNodeSetConverter
                .FromNodeSetDocumentsAsync(CreateReadableRoots(), "model").ConfigureAwait(false);
            Assert.That(exported.Success, Is.True, Describe(exported));
            using WotDocumentSet documents = exported.Value!;
            ArrayOf<UANodeSet> partitions = documents.Entries.ConvertAll(entry =>
                WotNodeSetConverter.ToNodeSet(entry.Document));
            var options = new WotNodeSetConverterOptions();
            if (nodeBound)
            {
                options.MaxNodeCount = 1;
            }
            else
            {
                options.MaxNodeSetSize = 1;
            }

            WotConversionResult<UANodeSet> merged =
                WotNodeSetConverter.MergeNodeSetPartitions(documents, partitions, options);

            Assert.That(merged.Success, Is.False);
            Assert.That(merged.Value, Is.Null);
            Assert.That(merged.Diagnostics.Any(diagnostic => diagnostic.Code ==
                (nodeBound ? WotDiagnosticCode.NodeCountExceeded : WotDiagnosticCode.NodeSetTooLarge)), Is.True);
        }

        [TestCase("model")]
        [TestCase("reference")]
        [TestCase("value")]
        [TestCase("fields")]
        [TestCase("extension")]
        [TestCase("missing")]
        public void LinkedComparisonStillRejectsChangedSourceFacts(string fact)
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            UANodeSet changed = WotTestData.CreateRichNodeSet();
            switch (fact)
            {
                case "model":
                    changed.Models![0].Version = "changed";
                    break;
                case "reference":
                    changed.Items![0].References![0].IsForward = true;
                    break;
                case "value":
                    changed.Items!.OfType<UAVariable>().First().Value!.InnerText = "999";
                    break;
                case "fields":
                    Array.Reverse(changed.Items!.OfType<UADataType>().Single().Definition!.Field!);
                    break;
                case "extension":
                    changed.Extensions![0].InnerText = "changed";
                    break;
                default:
                    changed.Items = changed.Items!.Take(changed.Items.Length - 1).ToArray();
                    break;
            }
            Assert.That(WotNodeSetConverter.CompareDocumentSet(source, changed).AreEquivalent, Is.False, fact);
        }

        [Test]
        public async Task ReadableDocumentCannotAddNodesToANativePartitionAsync()
        {
            UANodeSet source = CreateReadableRoots();
            source.Items![0].SymbolicName = "RetainedSymbol";
            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(source, "model").ConfigureAwait(false);
            Assert.That(result.Success, Is.True, Describe(result));
            using WotDocumentSet set = result.Value!;
            JsonObject altered = JsonNode.Parse(set.Entries[1].Document.Utf8Json.Span)!.AsObject();
            altered["properties"] = new JsonObject
            {
                ["Injected"] = new JsonObject
                {
                    ["uav:id"] = "nsu=urn:test:documents;s=Injected",
                    ["uav:componentOf"] = new JsonArray("nsu=urn:test:documents;s=AlphaType"),
                    ["type"] = "boolean"
                }
            };
            using var documents = new WotDocumentSet(set.RootHref, new ArrayOf<WotDocumentSetEntry>(
            [
                new(set.Entries[0].Href, WotDocument.Parse(set.Entries[0].Document.Utf8Json)),
                new(set.Entries[1].Href, WotDocument.Parse(Encoding.UTF8.GetBytes(altered.ToJsonString())))
            ]));
            WotConversionResult<UANodeSet> restored =
                await WotNodeSetConverter.ToNodeSetAsync(documents).ConfigureAwait(false);
            Assert.That(restored.Success, Is.False);
            Assert.That(
                restored.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict),
                Is.True);
        }

        [TestCase("header")]
        [TestCase("duplicate")]
        public async Task ConflictingAuthoritativePartitionsFailClosedAsync(string conflict)
        {
            UANodeSet source = CreateReadableRoots();
            source.Items![0].SymbolicName = "Alpha";
            source.Items[1].SymbolicName = "Beta";
            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(source, "model").ConfigureAwait(false);
            Assert.That(result.Success, Is.True, Describe(result));
            using WotDocumentSet set = result.Value!;
            JsonObject second = JsonNode.Parse(set.Entries[1].Document.Utf8Json.Span)!.AsObject();
            if (conflict == "header")
            {
                second["uav:nodes"]!["models"]![0]!["version"] = "conflicting-version";
            }
            else
            {
                JsonObject first = JsonNode.Parse(set.Entries[0].Document.Utf8Json.Span)!.AsObject();
                second["uav:nodes"]!["nodes"]!.AsArray().Add(
                    first["uav:nodes"]!["nodes"]![0]!.DeepClone());
            }
            using var documents = new WotDocumentSet(set.RootHref, new ArrayOf<WotDocumentSetEntry>(
            [
                new(set.Entries[0].Href, WotDocument.Parse(set.Entries[0].Document.Utf8Json)),
                new(set.Entries[1].Href, WotDocument.Parse(Encoding.UTF8.GetBytes(second.ToJsonString())))
            ]));
            WotConversionResult<UANodeSet> restored =
                await WotNodeSetConverter.ToNodeSetAsync(documents).ConfigureAwait(false);
            Assert.That(restored.Value, Is.Null);
            Assert.That(restored.Success, Is.False);
            Assert.That(
                restored.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict),
                Is.True);
        }

        private static WotDocumentSet Reload(WotDocumentSet set)
        {
            var entries = new List<WotDocumentSetEntry>();
            foreach (WotDocumentSetEntry entry in set.Entries)
            {
                entries.Add(new WotDocumentSetEntry(entry.Href, WotDocument.Parse(entry.Document.Utf8Json)));
            }
            return new WotDocumentSet(set.RootHref, entries.ToArrayOf());
        }

        [TestCase("i=2", "-128", "SByte")]
        [TestCase("i=3", "255", "Byte")]
        [TestCase("i=4", "-32768", "Int16")]
        [TestCase("i=5", "65535", "UInt16")]
        [TestCase("i=6", "-2147483648", "Int32")]
        [TestCase("i=7", "4294967295", "UInt32")]
        [TestCase("i=8", "-9223372036854775808", "Int64")]
        [TestCase("i=9", "18446744073709551615", "UInt64")]
        [TestCase("i=10", "3.25", "Float")]
        [TestCase("i=11", "42.5", "Double")]
        public async Task ScalarDefaultsBecomeTypedValuesWithoutMappedResidueAsync(
            string dataType,
            string value,
            string elementName)
        {
            using WotDocument document = ScalarDocument(dataType, "default", value);
            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetResultAsync(document).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(d => d.ToString())));
            UAVariable variable = result.Value!.Items!.OfType<UAVariable>().Single();
            Assert.That(variable.Value, Is.Not.Null);
            Assert.That(variable.Value!.LocalName, Is.EqualTo(elementName));
            Assert.That(variable.Value.InnerText, Is.EqualTo(value));
            Assert.That(result.Value.Extensions, Is.Null.Or.Empty);

            using WotDocument regenerated = WotNodeSetConverter.FromNodeSet(result.Value);
            Assert.That(
                regenerated.Properties["Value"].GetProperty("const").ValueKind,
                Is.EqualTo(JsonValueKind.Number));
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(regenerated);
            NodeSetComparisonResult comparison = NodeSetComparer.CompareEquivalent(result.Value, restored);
            Assert.That(comparison.AreEquivalent, Is.True, string.Join("; ", comparison.Differences));
        }

        [TestCase("i=3", "256")]
        [TestCase("i=11", "\"not a number\"")]
        public async Task InvalidTypedConstantIsReportedAsync(string dataType, string value)
        {
            using WotDocument document = ScalarDocument(dataType, "const", value);

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetResultAsync(document).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ValidationError &&
                    diagnostic.Location?.JsonPointer == "/properties/Value/const"),
                Is.True);
        }

        [TestCase("473.15")]
        [TestCase("0.1")]
        [TestCase("-17.23")]
        public async Task GeneratedDoubleConstantsUseCanonicalRoundTripSpelling(string literal)
        {
            using WotDocument input = ScalarDocument("i=11", "default", literal);
            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetResultAsync(input).ConfigureAwait(false);
            Assert.That(result.Success, Is.True, Describe(result));

            using WotDocument generated = WotNodeSetConverter.FromNodeSet(result.Value!);
            using WotDocument canonical = WotDocument.Parse(generated.ToCanonicalUtf8());

            Assert.That(canonical.Properties["Value"].GetProperty("const").GetRawText(), Is.EqualTo(literal));
        }

        private static WotDocument ScalarDocument(string dataType, string member, string value)
        {
            var root = new JsonObject
            {
                ["@context"] = new JsonObject
                {
                    ["uav"] = WotNodeSetConverter.VocabularyNamespace
                },
                ["@type"] = "tm:ThingModel",
                ["title"] = "Root",
                ["uav:id"] = "nsu=urn:test:scalar;s=Root",
                ["uav:browseName"] = "nsu=urn:test:scalar;Root",
                ["properties"] = new JsonObject
                {
                    ["Value"] = new JsonObject
                    {
                        ["uav:id"] = "nsu=urn:test:scalar;s=Value",
                        ["uav:browseName"] = "nsu=urn:test:scalar;Value",
                        ["uav:mapToType"] = dataType,
                        [member] = JsonNode.Parse(value)
                    }
                }
            };
            return WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
        }

        private static UANodeSet CreateReadableRoots()
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:documents"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:documents" }],
                Aliases = [new NodeIdAlias { Alias = "HasSubtype", Value = "i=45" }],
                Items = [Root("AlphaType"), Root("BetaType")]
            };

            static UAObjectType Root(string name)
            {
                return new UAObjectType
                {
                    NodeId = "ns=1;s=" + name,
                    BrowseName = "1:" + name,
                    DisplayName = [new Export.LocalizedText { Value = name }],
                    References =
                    [
                        new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=58" }
                    ]
                };
            }
        }

        private static async Task AssertEquivalentAsync(
            UANodeSet source,
            WotDocumentSet documents,
            WotNodeSetConverterOptions options,
            IWotNodeResolver nodeResolver = null)
        {
            WotConversionResult<UANodeSet> restored =
                await WotNodeSetConverter.ToNodeSetAsync(documents, options, nodeResolver).ConfigureAwait(false);
            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics.Select(d => d.ToString())));
            NodeSetComparisonResult comparison =
                WotNodeSetConverter.CompareDocumentSet(source, restored.Value!, options);
            Assert.That(comparison.AreEquivalent, Is.True, string.Join("; ", comparison.Differences));
        }

        private static string Describe<T>(WotConversionResult<T> result)
            where T : class
        {
            IEnumerable<WotDiagnostic> errors = result.Diagnostics.Where(
                diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error);
            return string.Join("; ", (errors.Any() ? errors : result.Diagnostics.Take(5))
                .Select(diagnostic => diagnostic.ToString()));
        }

        private static readonly string[] s_readableRootHrefs = ["model", "model-betatype"];
    }
}
