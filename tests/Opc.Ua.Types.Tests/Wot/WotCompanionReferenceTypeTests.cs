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

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Arbitrary companion-model ReferenceTypes, in both directions and in both
    /// conversion directions: WoT Binding Sections 5.1.2, 5.1.5, 5.3 and 6.2
    /// name a relation by a compact model name that may be a BrowseName or an
    /// InverseName, and settle it - where the name alone cannot - with the
    /// definitive <c>uav:refId</c>.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotCompanionReferenceTypeTests
    {
        private const string PumpNamespace = "http://example.com/demo/pump";
        private const string ValveNamespace = "http://example.com/demo/valve";
        private const string PumpMaterialReference = "nsu=http://example.com/demo/pump;i=5001";
        private const string ValveMaterialReference = "nsu=http://example.com/demo/valve;i=7001";
        private const string PumpConnectedTo = "nsu=http://example.com/demo/pump;i=5002";
        private const string LinkTarget = "nsu=http://example.com/demo/pump;s=Blade_1";

        [Test]
        public async Task ACompanionBrowseNameRunsForwardAsync()
        {
            ConvertedLink link = await ConvertLinkAsync(
                "pump:MaterialReference", null, Catalog()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(link.ReferenceType, Is.EqualTo(link.Local(PumpMaterialReference)));
                Assert.That(link.Reference.IsForward, Is.True);
            });
        }

        [Test]
        public async Task ACompanionInverseNameRunsBackwardsAsync()
        {
            ConvertedLink link = await ConvertLinkAsync(
                "pump:MaterialOf", null, Catalog()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(link.ReferenceType, Is.EqualTo(link.Local(PumpMaterialReference)));
                Assert.That(
                    link.Reference.IsForward,
                    Is.False,
                    "OPC 10000-3 reads a reference named by its InverseName the " +
                    "other way (WoT Binding Section 5.1.2).");
            });
        }

        [Test]
        public async Task ASymmetricReferenceTypeAlwaysRunsForwardAsync()
        {
            // A symmetric ReferenceType has one name for both directions, so
            // the local context offers it once, forward.
            ConvertedLink link = await ConvertLinkAsync(
                "pump:ConnectedTo", null, Catalog()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(link.ReferenceType, Is.EqualTo(link.Local(PumpConnectedTo)));
                Assert.That(link.Reference.IsForward, Is.True);
            });
        }

        [Test]
        public async Task TheContextDisambiguatesTheSameLocalNameInTwoNamespacesAsync()
        {
            StubReferenceTypeResolver catalog = Catalog();

            ConvertedLink pump = await ConvertLinkAsync(
                "pump:MaterialReference", null, catalog).ConfigureAwait(false);
            ConvertedLink valve = await ConvertLinkAsync(
                "valve:MaterialReference", null, catalog).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(pump.ReferenceType, Is.EqualTo(pump.Local(PumpMaterialReference)));
                Assert.That(
                    valve.ReferenceType,
                    Is.EqualTo(valve.Local(ValveMaterialReference)),
                    "The prefix binds the namespace, so one local name in two " +
                    "namespaces is two ReferenceTypes.");
            });
        }

        [Test]
        public async Task ANameAgreeingWithItsRefIdIsAcceptedAsync()
        {
            ConvertedLink link = await ConvertLinkAsync(
                "pump:MaterialOf",
                "\"uav:refId\":\"" + PumpMaterialReference + "\"",
                Catalog()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(link.ReferenceType, Is.EqualTo(link.Local(PumpMaterialReference)));
                Assert.That(link.Reference.IsForward, Is.False);
            });
        }

        [Test]
        public async Task ANameDisagreeingWithItsRefIdIsReportedAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ConvertLinkDiagnosticsAsync(
                "pump:MaterialReference",
                "\"uav:refId\":\"" + ValveMaterialReference + "\"",
                Catalog()).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ModelConceptConflict),
                Is.True,
                "A name and an identifier naming different ReferenceTypes are " +
                "reported rather than silently reconciled.");
        }

        [Test]
        public async Task AnAmbiguousNameWithoutARefIdIsReportedAsync()
        {
            StubReferenceTypeResolver catalog = Catalog();

            // "Feeds" is one ReferenceType's BrowseName and another's
            // InverseName in the same namespace, so the name alone selects
            // neither.
            catalog.Add(PumpNamespace, "Feeds", PumpMaterialReference, true);
            catalog.Add(PumpNamespace, "Feeds", PumpConnectedTo, false);

            IReadOnlyList<WotDiagnostic> diagnostics = await ConvertLinkDiagnosticsAsync(
                "pump:Feeds", null, catalog).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ReferenceTypeAmbiguous),
                Is.True);
        }

        [Test]
        public async Task AnAmbiguousNameIsSettledByItsRefIdAsync()
        {
            StubReferenceTypeResolver catalog = Catalog();
            catalog.Add(PumpNamespace, "Feeds", PumpMaterialReference, true);
            catalog.Add(PumpNamespace, "Feeds", PumpConnectedTo, false);

            ConvertedLink link = await ConvertLinkAsync(
                "pump:Feeds",
                "\"uav:refId\":\"" + PumpConnectedTo + "\"",
                catalog).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(link.ReferenceType, Is.EqualTo(link.Local(PumpConnectedTo)));
                Assert.That(
                    link.Reference.IsForward,
                    Is.False,
                    "The identifier selects the candidate, and the candidate " +
                    "carries the direction its matched name expressed.");
            });
        }

        [Test]
        public async Task AnAmbiguousNameWhoseRefIdNamesNeitherCandidateIsReportedAsync()
        {
            StubReferenceTypeResolver catalog = Catalog();
            catalog.Add(PumpNamespace, "Feeds", PumpMaterialReference, true);
            catalog.Add(PumpNamespace, "Feeds", PumpConnectedTo, false);

            IReadOnlyList<WotDiagnostic> diagnostics = await ConvertLinkDiagnosticsAsync(
                "pump:Feeds",
                "\"uav:refId\":\"" + ValveMaterialReference + "\"",
                catalog).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ModelConceptConflict),
                Is.True);
        }

        [Test]
        public async Task ARelationNamingANodeOfTheWrongNodeClassIsReportedAsync()
        {
            StubReferenceTypeResolver catalog = Catalog();

            // The model defines "PumpType", but as an ObjectType, so it is not
            // "unknown" - it is the wrong kind of Node for a relation.
            catalog.Nodes[PumpNamespace + "|PumpType"] =
                new WotResolvedNode(
                    "nsu=http://example.com/demo/pump;i=1001",
                    WotExpectedNodeClass.ObjectType);

            IReadOnlyList<WotDiagnostic> diagnostics = await ConvertLinkDiagnosticsAsync(
                "pump:PumpType", null, catalog).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ReferenceTypeNodeClassInvalid),
                Is.True);
        }

        [Test]
        public async Task ARefIdNamingANodeOfTheWrongNodeClassIsReportedAsync()
        {
            StubReferenceTypeResolver catalog = Catalog();
            catalog.Identities["nsu=http://example.com/demo/pump;i=1001"] =
                new WotResolvedNode(
                    "nsu=http://example.com/demo/pump;i=1001",
                    WotExpectedNodeClass.ObjectType);

            IReadOnlyList<WotDiagnostic> diagnostics = await ConvertLinkDiagnosticsAsync(
                "pump:MaterialReference",
                "\"uav:refId\":\"nsu=http://example.com/demo/pump;i=1001\"",
                catalog).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ReferenceTypeNodeClassInvalid),
                Is.True);
        }

        [Test]
        public async Task AnUnresolvedRelationWithoutARefIdIsReportedAsync()
        {
            IReadOnlyList<WotDiagnostic> diagnostics = await ConvertLinkDiagnosticsAsync(
                "pump:NeverDefined", null, Catalog()).ConfigureAwait(false);

            Assert.That(
                diagnostics.Any(d => d.Code == WotDiagnosticCode.ModelConceptUnresolved),
                Is.True);
        }

        [Test]
        public async Task TheFirstPartOfTheLocalContextSettlesTheRelationAsync()
        {
            // Section 5.1.5 consults the siblings of the conversion before a
            // loaded AddressSpace, so a name both hold resolves to the
            // sibling's ReferenceType.
            var siblings = new StubReferenceTypeResolver();
            siblings.Add(PumpNamespace, "MaterialReference", PumpMaterialReference, true);
            var addressSpace = new StubReferenceTypeResolver();
            addressSpace.Add(PumpNamespace, "MaterialReference", ValveMaterialReference, true);

            var composite = new WotCompositeNodeResolver(siblings, addressSpace);

            ArrayOf<WotResolvedReferenceType> matches = await composite
                .ResolveReferenceTypesAsync(PumpNamespace, "MaterialReference")
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(matches.Count, Is.EqualTo(1));
                Assert.That(matches[0].NodeId, Is.EqualTo(PumpMaterialReference));
            });
        }

        [Test]
        public async Task APartWithoutTheCapabilityDoesNotEndTheWalkAsync()
        {
            var addressSpace = new StubReferenceTypeResolver();
            addressSpace.Add(PumpNamespace, "MaterialReference", PumpMaterialReference, true);

            var composite = new WotCompositeNodeResolver(
                new PlainResolver(), addressSpace);

            ArrayOf<WotResolvedReferenceType> matches = await composite
                .ResolveReferenceTypesAsync(PumpNamespace, "MaterialReference")
                .ConfigureAwait(false);

            Assert.That(matches.Count, Is.EqualTo(1));
        }

        [Test]
        public void ACompanionReferenceTypeIsWrittenAsATypedLink()
        {
            using WotDocumentSet documents = Emit();
            WotDocument document = DocumentFor(documents, "nsu=" + PumpNamespace + ";i=1001");

            JsonElement link = SingleLink(document, "ns1:MaterialReference");
            Assert.Multiple(() =>
            {
                Assert.That(
                    link.GetProperty("href").GetString(),
                    Is.EqualTo("nsu=http://example.com/demo/pump;i=7001"));
                Assert.That(
                    link.GetProperty("uav:refId").GetString(),
                    Is.EqualTo(PumpMaterialReference),
                    "Section 6.2 carries the definitive ExpandedNodeId beside " +
                    "the compact model name.");
            });
        }

        [Test]
        public void AnInverseCompanionReferenceIsWrittenUnderItsInverseName()
        {
            using WotDocumentSet documents = Emit();
            WotDocument document = DocumentFor(documents, "nsu=" + PumpNamespace + ";i=1001");

            JsonElement link = SingleLink(document, "ns1:MaterialOf");
            Assert.Multiple(() =>
            {
                Assert.That(
                    link.GetProperty("href").GetString(),
                    Is.EqualTo("nsu=http://example.com/demo/pump;i=7002"));
                Assert.That(
                    link.GetProperty("uav:refId").GetString(),
                    Is.EqualTo(PumpMaterialReference));
            });
        }

        [Test]
        public void ASymmetricReferenceIsWrittenUnderItsBrowseNameInBothDirections()
        {
            using WotDocumentSet documents = Emit();
            WotDocument document = DocumentFor(documents, "nsu=" + PumpNamespace + ";i=1001");

            int connected = 0;
            foreach (JsonElement link in document.Links)
            {
                if (link.TryGetProperty("rel", out JsonElement rel) &&
                    string.Equals(rel.GetString(), "ns1:ConnectedTo", StringComparison.Ordinal))
                {
                    connected++;
                }
            }
            Assert.That(
                connected,
                Is.EqualTo(2),
                "A symmetric ReferenceType has one name for both directions, " +
                "so both references read under the BrowseName.");
        }

        [Test]
        public void AProjectedReferenceTypeCarriesBothOfItsNames()
        {
            using WotDocumentSet documents = Emit();
            WotDocument document = DocumentFor(documents, PumpMaterialReference);

            Assert.Multiple(() =>
            {
                Assert.That(
                    document.RootElement.GetProperty("uav:inverseName").GetString(),
                    Is.EqualTo("MaterialOf"));
                Assert.That(
                    document.RootElement.TryGetProperty("uav:symmetric", out _),
                    Is.False,
                    "An asymmetric ReferenceType states no Symmetric flag.");
            });
        }

        [Test]
        public void ASymmetricProjectedReferenceTypeStatesItsFlag()
        {
            using WotDocumentSet documents = Emit();
            WotDocument document = DocumentFor(documents, PumpConnectedTo);

            Assert.Multiple(() =>
            {
                Assert.That(
                    document.RootElement.GetProperty("uav:symmetric").GetBoolean(),
                    Is.True);
                Assert.That(
                    document.RootElement.TryGetProperty("uav:inverseName", out _),
                    Is.False,
                    "A symmetric ReferenceType states no InverseName.");
            });
        }

        [Test]
        public async Task ADocumentSetRoundTripsEveryCompanionRelationAsync()
        {
            using WotDocumentSet documents = Emit();
            List<WotDocument> documentList = DocumentsOf(documents);

            // Section 5.1.5: the siblings of the conversion are the first part
            // of the local context, so the set resolves its own ReferenceTypes.
            var resolver = new WotDocumentNodeResolver(documentList);
            WotConversionResult<UANodeSet> back = await WotNodeSetConverter
                .ToNodeSetAsync(documents, null, resolver).ConfigureAwait(false);

            Assert.That(
                back.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
            UANode restored = back.Value!.Items!.Single(n => n.NodeId == "ns=1;i=1001");
            string materialReference = LocalReferenceType(back.Value!, PumpMaterialReference);
            Assert.Multiple(() =>
            {
                Assert.That(
                    restored.References!.Any(r =>
                        r.ReferenceType == materialReference && r.IsForward),
                    Is.True,
                    "The forward companion relation comes back with its exact " +
                    "ReferenceType.");
                Assert.That(
                    restored.References!.Any(r =>
                        r.ReferenceType == materialReference && !r.IsForward),
                    Is.True,
                    "The inverse companion relation comes back inverse, not as " +
                    "a second forward reference and not as HasComponent.");
                Assert.That(
                    restored.References!.Any(r =>
                        r.ReferenceType == "HasComponent" &&
                        r.Value!.Contains("7001", StringComparison.Ordinal)),
                    Is.False,
                    "Nothing falls back to HasComponent.");
            });

            UAReferenceType restoredType = back.Value!.Items!
                .OfType<UAReferenceType>()
                .Single(n => n.NodeId == "ns=1;i=5001");
            Assert.Multiple(() =>
            {
                Assert.That(restoredType.InverseName![0].Value, Is.EqualTo("MaterialOf"));
                Assert.That(restoredType.Symmetric, Is.False);
            });
        }

        [Test]
        public async Task ADocumentSetRoundTripImportsAsync()
        {
            using WotDocumentSet documents = Emit();

            // No resolver: Section 5.1.5 makes the documents of the set the
            // first part of the local context, so the set resolves its own
            // ReferenceTypes without the caller wiring one.
            WotConversionResult<UANodeSet> back = await WotNodeSetConverter
                .ToNodeSetAsync(documents).ConfigureAwait(false);

            Assert.That(
                back.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
            string materialReference = LocalReferenceType(back.Value!, PumpMaterialReference);
            Assert.That(
                back.Value!.Items!.Single(n => n.NodeId == "ns=1;i=1001")
                    .References!.Any(r => r.ReferenceType == materialReference && !r.IsForward),
                Is.True,
                "The inverse companion relation keeps its direction without a " +
                "caller-supplied local context.");

            // Every emitted relation has to survive the importer, which is what
            // makes the converted NodeSet loadable at all.
            WotNodeSetImportTests.AssertImportable(back.Value!, "companion relations");
        }

        /// <summary>
        /// A NodeSet defining its own ReferenceTypes and using them in both
        /// directions - the shape a companion model has.
        /// </summary>
        private static UANodeSet CompanionNodeSet()
        {
            return new UANodeSet
            {
                NamespaceUris = [PumpNamespace],
                Models = [new ModelTableEntry { ModelUri = PumpNamespace }],
                Aliases =
                [
                    new NodeIdAlias { Alias = "HasSubtype", Value = "i=45" },
                    new NodeIdAlias { Alias = "HasTypeDefinition", Value = "i=40" },
                    new NodeIdAlias
                    {
                        Alias = "NonHierarchicalReferences",
                        Value = "i=32"
                    }
                ],
                Items =
                [
                    new UAReferenceType
                    {
                        NodeId = "ns=1;i=5001",
                        BrowseName = "1:MaterialReference",
                        DisplayName = [new Export.LocalizedText { Value = "MaterialReference" }],
                        InverseName = [new Export.LocalizedText { Value = "MaterialOf" }],
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=32"
                            }
                        ]
                    },
                    new UAReferenceType
                    {
                        NodeId = "ns=1;i=5002",
                        BrowseName = "1:ConnectedTo",
                        DisplayName = [new Export.LocalizedText { Value = "ConnectedTo" }],
                        Symmetric = true,
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=32"
                            }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:PumpType",
                        DisplayName = [new Export.LocalizedText { Value = "PumpType" }],
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=58"
                            },
                            new Reference
                            {
                                ReferenceType = "ns=1;i=5001",
                                IsForward = true,
                                Value = "ns=1;i=7001"
                            },
                            new Reference
                            {
                                ReferenceType = "ns=1;i=5001",
                                IsForward = false,
                                Value = "ns=1;i=7002"
                            },
                            new Reference
                            {
                                ReferenceType = "ns=1;i=5002",
                                IsForward = true,
                                Value = "ns=1;i=7003"
                            },
                            new Reference
                            {
                                ReferenceType = "ns=1;i=5002",
                                IsForward = false,
                                Value = "ns=1;i=7004"
                            }
                        ]
                    }
                ]
            };
        }

        private static JsonElement SingleLink(WotDocument document, string rel)
        {
            foreach (JsonElement link in document.Links)
            {
                if (link.TryGetProperty("rel", out JsonElement value) &&
                    string.Equals(value.GetString(), rel, StringComparison.Ordinal))
                {
                    return link;
                }
            }
            Assert.Fail($"No link states '{rel}'.");
            return default;
        }

        /// <summary>
        /// Emits the companion NodeSet as a document set, which is the shape a
        /// companion model takes: one document per Node, ReferenceTypes
        /// included.
        /// </summary>
        private static WotDocumentSet Emit()
        {
            WotConversionResult<WotDocumentSet> result =
                WotNodeSetConverter.FromNodeSetDocuments(CompanionNodeSet(), "pump");
            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
            Assert.That(result.Value, Is.Not.Null);
            return result.Value!;
        }

        private static WotDocument DocumentFor(WotDocumentSet documents, string nodeId)
        {
            for (int ii = 0; ii < documents.Entries.Count; ii++)
            {
                WotDocument document = documents.Entries[ii].Document;
                if (document.RootElement.TryGetProperty("uav:id", out JsonElement id) &&
                    string.Equals(id.GetString(), nodeId, StringComparison.Ordinal))
                {
                    return document;
                }
            }
            Assert.Fail($"The set holds no document for '{nodeId}'.");
            return null!;
        }

        private static List<WotDocument> DocumentsOf(WotDocumentSet documents)
        {
            var list = new List<WotDocument>(documents.Entries.Count);
            for (int ii = 0; ii < documents.Entries.Count; ii++)
            {
                list.Add(documents.Entries[ii].Document);
            }
            return list;
        }

        private static async Task<ConvertedLink> ConvertLinkAsync(
            string rel,
            string? extra,
            StubReferenceTypeResolver resolver)
        {
            using WotDocument document = WotDocument.Parse(
                WotTestData.Utf8(ThingModelWithLink(rel, extra)));
            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(document, null, null, null, resolver)
                .ConfigureAwait(false);
            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
            return new ConvertedLink(
                result.Value!,
                result.Value!.Items!
                    .OfType<UAObjectType>().Single()
                    .References!.Single(r => r.Value == LinkTarget));
        }

        private static async Task<IReadOnlyList<WotDiagnostic>> ConvertLinkDiagnosticsAsync(
            string rel,
            string? extra,
            StubReferenceTypeResolver resolver)
        {
            using WotDocument document = WotDocument.Parse(
                WotTestData.Utf8(ThingModelWithLink(rel, extra)));
            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(document, null, null, null, resolver)
                .ConfigureAwait(false);
            return result.Diagnostics;
        }

        /// <summary>
        /// Maps a portable ExpandedNodeId onto the NodeSet-local NodeId a
        /// converted NodeSet states a ReferenceType as.
        /// </summary>
        private static string LocalReferenceType(UANodeSet nodeSet, string portable)
        {
            int separator = portable.IndexOf(';', StringComparison.Ordinal);
            string namespaceUri = portable[4..separator];
            int index = Array.IndexOf(nodeSet.NamespaceUris!, namespaceUri) + 1;
            Assert.That(
                index,
                Is.GreaterThan(0),
                $"'{namespaceUri}' should be in the NodeSet's namespace table.");
            return "ns=" + index.ToString(CultureInfo.InvariantCulture) + portable[separator..];
        }

        private static StubReferenceTypeResolver Catalog()
        {
            var resolver = new StubReferenceTypeResolver();
            resolver.Add(PumpNamespace, "MaterialReference", PumpMaterialReference, true);
            resolver.Add(PumpNamespace, "MaterialOf", PumpMaterialReference, false);
            resolver.Add(PumpNamespace, "ConnectedTo", PumpConnectedTo, true);
            resolver.Add(ValveNamespace, "MaterialReference", ValveMaterialReference, true);
            return resolver;
        }

        private static string ThingModelWithLink(string rel, string? extra)
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"" + PumpNamespace + "\"," +
                "\"valve\":\"" + ValveNamespace + "\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\"," +
                "\"uav:browseName\":\"nsu=" + PumpNamespace + ";PumpType\"," +
                "\"uav:id\":\"nsu=" + PumpNamespace + ";i=1001\"," +
                "\"links\":[{\"rel\":\"" + rel + "\",\"href\":\"" + LinkTarget + "\"" +
                (extra is null ? ",\"uav:refName\":\"Material\"" : "," + extra) + "}]}";
        }

        /// <summary>
        /// A converted link, together with the NodeSet it was written into.
        /// </summary>
        /// <remarks>
        /// A NodeSet2 document states a ReferenceType as a NodeSet-local
        /// NodeId, never as the portable ExpandedNodeId a relation resolves to,
        /// because the importer rejects anything else. The NodeSet is carried
        /// alongside the reference so a test can name the ReferenceType it
        /// expects portably and have it mapped through the NodeSet's own
        /// namespace table.
        /// </remarks>
        /// <param name="NodeSet">The NodeSet the conversion produced.</param>
        /// <param name="Reference">The reference the link became.</param>
        private readonly record struct ConvertedLink(UANodeSet NodeSet, Reference Reference)
        {
            /// <summary>
            /// Gets the reference's ReferenceType.
            /// </summary>
            public string ReferenceType => Reference.ReferenceType!;

            /// <summary>
            /// Maps a portable ExpandedNodeId onto the NodeSet-local NodeId the
            /// converted NodeSet states it as.
            /// </summary>
            /// <param name="portable">The portable ExpandedNodeId.</param>
            /// <returns>The NodeSet-local NodeId.</returns>
            public string Local(string portable)
            {
                return LocalReferenceType(NodeSet, portable);
            }
        }

        /// <summary>
        /// A local context that holds only what a test puts in it.
        /// </summary>
        private sealed class StubReferenceTypeResolver
            : IWotNodeResolver, IWotReferenceTypeResolver
        {
            public Dictionary<string, WotResolvedNode> Nodes { get; } =
                new(StringComparer.Ordinal);

            public Dictionary<string, WotResolvedNode> Identities { get; } =
                new(StringComparer.Ordinal);

            public void Add(
                string namespaceUri, string name, string nodeId, bool isForward)
            {
                string key = namespaceUri + "|" + name;
                if (!m_referenceTypes.TryGetValue(
                    key, out List<WotResolvedReferenceType>? matches))
                {
                    matches = [];
                    m_referenceTypes[key] = matches;
                }
                matches.Add(new WotResolvedReferenceType(nodeId, name, isForward));
            }

            public ValueTask<bool> HoldsNamespaceAsync(
                string namespaceUri, CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(false);
            }

            public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
                string namespaceUri,
                string browseName,
                WotExpectedNodeClass expected,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ArrayOf<WotResolvedNode>>(
                    Nodes.TryGetValue(namespaceUri + "|" + browseName, out WotResolvedNode node)
                        ? new ArrayOf<WotResolvedNode>([node])
                        : ArrayOf<WotResolvedNode>.Empty);
            }

            public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
                string expandedNodeId, CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotResolvedNode?>(
                    Identities.TryGetValue(expandedNodeId, out WotResolvedNode node)
                        ? node
                        : null);
            }

            public ValueTask<ArrayOf<WotResolvedReferenceType>> ResolveReferenceTypesAsync(
                string namespaceUri,
                string name,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ArrayOf<WotResolvedReferenceType>>(
                    m_referenceTypes.TryGetValue(
                        namespaceUri + "|" + name,
                        out List<WotResolvedReferenceType>? matches)
                        ? new ArrayOf<WotResolvedReferenceType>(matches.ToArray())
                        : ArrayOf<WotResolvedReferenceType>.Empty);
            }

            private readonly Dictionary<string, List<WotResolvedReferenceType>>
                m_referenceTypes = new(StringComparer.Ordinal);
        }

        /// <summary>
        /// A part of the local context that offers no ReferenceType capability
        /// at all.
        /// </summary>
        private sealed class PlainResolver : IWotNodeResolver
        {
            public ValueTask<bool> HoldsNamespaceAsync(
                string namespaceUri, CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(false);
            }

            public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
                string namespaceUri,
                string browseName,
                WotExpectedNodeClass expected,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ArrayOf<WotResolvedNode>>(ArrayOf<WotResolvedNode>.Empty);
            }

            public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
                string expandedNodeId, CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotResolvedNode?>((WotResolvedNode?)null);
            }
        }
    }
}
