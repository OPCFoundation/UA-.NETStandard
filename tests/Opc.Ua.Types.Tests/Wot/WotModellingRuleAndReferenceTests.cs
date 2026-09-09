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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// The four modelling rules and the ReferenceType relations of WoT Binding
    /// Sections 5.3 and 9.1, in both directions.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotModellingRuleAndReferenceTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ComponentSubtypeAndIndependentReferencesSurviveInEitherOrder(bool reverseOrder)
        {
            const string component = """
                {"rel":"ua:HasOrderedComponent","href":"nsu=urn:test:graph;i=2"}
                """;
            const string organizes = """
                {"rel":"ua:OrganizedBy","href":"nsu=urn:test:graph;i=2"}
                """;
            string links = reverseOrder ? organizes + "," + component : component + "," + organizes;
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(
                $$"""
                {
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "title": "Root",
                  "uav:id": "nsu=urn:test:graph;i=1",
                  "uav:hasComponent": ["nsu=urn:test:graph;i=2"],
                  "links": [{{links}}]
                }
                """));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            Reference[] references = result.Value!.Items![0].References!
                .Where(r => r.Value == "ns=1;i=2").ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(references, Has.Length.EqualTo(2));
                Assert.That(references.Any(r => r.IsForward && r.ReferenceType == "i=49"), Is.True);
                Assert.That(references.Any(r => !r.IsForward && r.ReferenceType == "i=35"), Is.True);
            });
        }

        [TestCase("HasComponent", "i=47", false)]
        [TestCase("HasComponent", "i=47", true)]
        [TestCase("HasOrderedComponent", "i=49", false)]
        [TestCase("HasOrderedComponent", "i=49", true)]
        public void EitherReferenceDirectionExportsReadableVariableAndMethodAffordances(
            string referenceType,
            string referenceId,
            bool inverseOnly)
        {
            UANodeSet source = NodeSetWithModellingRule("i=78");
            UANode root = source.Items![0];
            UAVariable variable = source.Items!.OfType<UAVariable>().Single();
            root.References = root.References!.Where(r => r.ReferenceType != "HasComponent").ToArray();
            variable.References = variable.References!.Where(r => r.ReferenceType != "HasComponent").ToArray();
            var method = new UAMethod { NodeId = "ns=1;i=6002", BrowseName = "1:Reset" };
            source.Items = [.. source.Items!, method];
            foreach (UANode child in new UANode[] { variable, method })
            {
                if (inverseOnly)
                {
                    child.References =
                    [
                        .. child.References ?? [],
                        new Reference { ReferenceType = referenceType, IsForward = false, Value = root.NodeId }
                    ];
                }
                else
                {
                    root.References =
                    [
                        .. root.References,
                        new Reference { ReferenceType = referenceType, IsForward = true, Value = child.NodeId }
                    ];
                }
            }
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                options: new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Never });

            Assert.Multiple(() =>
            {
                Assert.That(document.Properties.ContainsKey("Speed"), Is.True);
                Assert.That(document.Actions.ContainsKey("Reset"), Is.True);
            });
            Assert.That(document.Properties["Speed"].GetProperty("@type").GetString(), Is.EqualTo("uav:variable"));
            JsonObject readable = JsonNode.Parse(document.Utf8Json.Span)!.AsObject();
            readable.Remove("uav:nodes");
            readable.Remove("uav:nodeSet");
            using WotDocument readableDocument = WotDocument.Parse(WotTestData.Utf8(readable.ToJsonString()));
            WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ToNodeSetResult(readableDocument);
            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics.Select(d => d.Message)));
            foreach (UANode child in restored.Value!.Items!.Where(n => n is UAVariable or UAMethod))
            {
                Assert.That(child.References!.Any(r =>
                    !r.IsForward &&
                    (r.ReferenceType == referenceId || r.ReferenceType == referenceType) &&
                    r.Value == "ns=1;i=1001"), Is.True, child.BrowseName);
            }
        }

        [Test]
        public void AnObjectTypeSupertypeIsReadableAndReconstructsExactly()
        {
            UANodeSet source = NodeSetWithModellingRule("i=78");
            source.Items![0].References!.Single(r => r.ReferenceType == "HasSubtype").Value = "ns=1;i=1000";
            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(document.Links.Any(link =>
                link.GetProperty("rel").GetString() == "ua:HasSupertype" &&
                link.GetProperty("href").GetString() == "nsu=urn:test:rules;i=1000" &&
                link.GetProperty("uav:refId").GetString() == "i=45"), Is.True);

            JsonObject readable = JsonNode.Parse(document.Utf8Json.Span)!.AsObject();
            readable.Remove("uav:nodes");
            readable.Remove("uav:nodeSet");
            using WotDocument readableDocument = WotDocument.Parse(WotTestData.Utf8(readable.ToJsonString()));
            WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ToNodeSetResult(readableDocument);

            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics.Select(d => d.Message)));
            Assert.That(restored.Value!.Items![0].References!
                .Single(r => !r.IsForward && r.ReferenceType is "HasSubtype" or "i=45").Value,
                Is.EqualTo("ns=1;i=1000"));
            Assert.That(WotNodeSetConverter.TryDescribeTypeDeclarations(
                readableDocument, out _, out ArrayOf<string> supertypes), Is.True);
            Assert.That(supertypes.Count, Is.EqualTo(1));
            Assert.That(supertypes[0], Is.EqualTo("nsu=urn:test:rules;i=1000"));
        }

        [TestCase("i=37", "i=78")]
        [TestCase("RuleLink", "Required")]
        public void NumericAndAliasedModellingRulesAreReadable(string referenceType, string ruleTarget)
        {
            UANodeSet source = NodeSetWithModellingRule(ruleTarget);
            source.Aliases =
            [
                .. source.Aliases ?? [],
                new NodeIdAlias { Alias = "RuleLink", Value = "i=37" },
                new NodeIdAlias { Alias = "Required", Value = "i=78" }
            ];
            source.Items!.OfType<UAVariable>().Single().References!
                .Single(r => r.ReferenceType == "HasModellingRule").ReferenceType = referenceType;
            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(
                document.Properties["Speed"].TryGetProperty("uav:modellingRule", out JsonElement rule), Is.True);
            Assert.That(rule.GetString(), Is.EqualTo("Mandatory"));
            JsonObject readable = JsonNode.Parse(document.Utf8Json.Span)!.AsObject();
            readable.Remove("uav:nodes");
            readable.Remove("uav:nodeSet");
            using WotDocument readableDocument = WotDocument.Parse(WotTestData.Utf8(readable.ToJsonString()));
            WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ToNodeSetResult(readableDocument);

            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics.Select(d => d.Message)));
            Assert.That(restored.Value!.Items!.OfType<UAVariable>().Single().References!
                .Single(r => r.IsForward && r.ReferenceType == "HasModellingRule").Value, Is.EqualTo("i=78"));
        }

        [TestCase("References", "i=31")]
        [TestCase("NonHierarchicalReferences", "i=32")]
        [TestCase("HierarchicalReferences", "i=33")]
        [TestCase("HasChild", "i=34")]
        [TestCase("Aggregates", "i=44")]
        public void AbstractReferenceTypesCannotBeInstantiated(string name, string referenceId)
        {
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(
                $$"""
                {
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "title": "Root",
                  "uav:id": "nsu=urn:test:graph;i=1",
                  "links": [{ "rel": "ua:{{name}}", "href": "i=85", "uav:refId": "{{referenceId}}" }]
                }
                """));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.Diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Message.Contains("abstract", StringComparison.OrdinalIgnoreCase)), Is.True);
                Assert.That(result.Value!.Items![0].References!.Any(r => r.ReferenceType == referenceId), Is.False);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CompanionComponentSubtypesExportReadableAffordancesAsync(bool inverseOnly)
        {
            UANodeSet source = NodeSetWithModellingRule("i=78");
            var componentType = new UAReferenceType
            {
                NodeId = "ns=1;i=9000",
                BrowseName = "1:Owns",
                InverseName = [new Export.LocalizedText { Value = "OwnedBy" }],
                References = [new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=49" }]
            };
            source.Items = [.. source.Items!, componentType];
            UANode root = source.Items[0];
            UAVariable variable = source.Items.OfType<UAVariable>().Single();
            root.References = root.References!.Where(r => r.ReferenceType != "HasComponent").ToArray();
            variable.References = variable.References!.Where(r => r.ReferenceType != "HasComponent").ToArray();
            var edge = new Reference
            {
                ReferenceType = "ns=1;i=9000",
                IsForward = !inverseOnly,
                Value = inverseOnly ? root.NodeId : variable.NodeId
            };
            if (inverseOnly)
            {
                variable.References = [.. variable.References, edge];
            }
            else
            {
                root.References = [.. root.References, edge];
            }
            using WotDocument definition = WotNodeSetConverter.FromNodeSet(new UANodeSet
            {
                NamespaceUris = source.NamespaceUris,
                Models = source.Models,
                Items = [componentType]
            });
            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(document.Properties.ContainsKey("Speed"), Is.True);
            JsonObject readable = JsonNode.Parse(document.Utf8Json.Span)!.AsObject();
            readable.Remove("uav:nodes");
            readable.Remove("uav:nodeSet");
            using WotDocument readableDocument = WotDocument.Parse(WotTestData.Utf8(readable.ToJsonString()));
            WotConversionResult<UANodeSet> restored = await WotNodeSetConverter.ToNodeSetResultAsync(
                readableDocument, null, null, null, new WotDocumentNodeResolver([definition])).ConfigureAwait(false);

            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics.Select(d => d.Message)));
            UAVariable restoredVariable = restored.Value!.Items!.OfType<UAVariable>().Single();
            Assert.That(restoredVariable.References!.Any(r =>
                !r.IsForward && r.ReferenceType == "ns=1;i=9000" && r.Value == "ns=1;i=1001"), Is.True);
        }

        // OPC 10000-5 assigns these identifiers. They are neither adjacent nor
        // in name order, which is exactly why they were transposed: 11509 is
        // not a ModellingRule Object at all.
        [TestCase("Mandatory", "i=78")]
        [TestCase("Optional", "i=80")]
        [TestCase("OptionalPlaceholder", "i=11508")]
        [TestCase("MandatoryPlaceholder", "i=11510")]
        public void ModellingRuleSynthesizesTheStandardObject(string rule, string expected)
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(
                WotTestData.Utf8(ThingModelWithRule(rule)));

            UAVariable variable = nodeSet.Items!.OfType<UAVariable>().Single();
            Assert.That(
                variable.References!.Single(r =>
                    r.ReferenceType == "HasModellingRule" && r.IsForward).Value,
                Is.EqualTo(expected));
        }

        [TestCase("Mandatory", "i=78")]
        [TestCase("Optional", "i=80")]
        [TestCase("OptionalPlaceholder", "i=11508")]
        [TestCase("MandatoryPlaceholder", "i=11510")]
        public void ModellingRuleIsReadBackFromTheStandardObject(string rule, string nodeId)
        {
            UANodeSet source = NodeSetWithModellingRule(nodeId);

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(
                document.Properties["Speed"].GetProperty("uav:modellingRule").GetString(),
                Is.EqualTo(rule));
        }

        [TestCase("Mandatory")]
        [TestCase("Optional")]
        [TestCase("OptionalPlaceholder")]
        [TestCase("MandatoryPlaceholder")]
        public void ModellingRuleSurvivesAFullRoundTrip(string rule)
        {
            using WotDocument first = WotDocument.Parse(
                WotTestData.Utf8(ThingModelWithRule(rule)));
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(first);

            using WotDocument second = WotNodeSetConverter.FromNodeSet(nodeSet);

            Assert.That(
                second.Properties["Speed"].GetProperty("uav:modellingRule").GetString(),
                Is.EqualTo(rule));
        }

        [TestCase("Mandatory", "i=78")]
        [TestCase("Optional", "i=80")]
        [TestCase("OptionalPlaceholder", "i=11508")]
        [TestCase("MandatoryPlaceholder", "i=11510")]
        public void ModellingRuleSurvivesTheNativeProjection(string rule, string nodeId)
        {
            UANodeSet source = NodeSetWithModellingRule(nodeId);
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Never
            };

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source, options: options);
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document, options);

            UAVariable variable = restored.Items!.OfType<UAVariable>().Single();
            Assert.That(
                variable.References!.Single(r =>
                    r.ReferenceType == "HasModellingRule" && r.IsForward).Value,
                Is.EqualTo(nodeId));
            Assert.That(
                document.Properties["Speed"].GetProperty("uav:modellingRule").GetString(),
                Is.EqualTo(rule));
        }

        [Test]
        public async Task ComponentOfAliasPlacesTheNodeUnderItsParentAsync()
        {
            // WoT Binding Section 9.1 declares ua:ComponentOf an alias of
            // uav:componentOf, so both shall place the projected Object under
            // the named parent.
            UANodeSet withAlias = await ConvertWithParentAsync("ua:ComponentOf")
                .ConfigureAwait(false);
            UANodeSet withTerm = await ConvertWithParentAsync("uav:componentOf")
                .ConfigureAwait(false);

            foreach (UANodeSet nodeSet in new[] { withAlias, withTerm })
            {
                UAObject root = nodeSet.Items!.OfType<UAObject>().Single();
                Assert.That(
                    root.References!.Any(r =>
                        r.ReferenceType == "HasComponent" &&
                        !r.IsForward &&
                        r.Value == LocalNodeId(nodeSet, ParentNodeId)),
                    Is.True,
                    "The parent placement should be an inverse HasComponent.");
            }
        }

        [Test]
        public async Task ComponentOfAliasIsNotAlsoRealizedAsATypedLinkAsync()
        {
            UANodeSet nodeSet = await ConvertWithParentAsync("ua:ComponentOf")
                .ConfigureAwait(false);

            UAObject root = nodeSet.Items!.OfType<UAObject>().Single();
            Assert.That(
                root.References!.Count(r => r.Value == LocalNodeId(nodeSet, ParentNodeId)),
                Is.EqualTo(1),
                "The alias is a Binding term, so it places the node once and " +
                "is not additionally emitted as a generic reference.");
        }

        [Test]
        public void AnInverseNameEmitsAnInverseReference()
        {
            // ua:OrderedComponentOf is the InverseName of HasOrderedComponent,
            // so the reference it names runs the other way.
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(
                WotTestData.Utf8(ThingModelWithLink(
                    "ua:OrderedComponentOf",
                    "\"uav:refName\":\"Assembly\"")));

            UAObjectType root = nodeSet.Items!.OfType<UAObjectType>().Single();
            Reference reference = root.References!.Single(r => r.Value == LocalNodeId(nodeSet, LinkTarget));
            Assert.That(reference.ReferenceType, Is.EqualTo("i=49"));
            Assert.That(reference.IsForward, Is.False);
        }

        [Test]
        public void AForwardNameEmitsAForwardReference()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(
                WotTestData.Utf8(ThingModelWithLink(
                    "ua:HasOrderedComponent",
                    "\"uav:refName\":\"Assembly\"")));

            UAObjectType root = nodeSet.Items!.OfType<UAObjectType>().Single();
            Reference reference = root.References!.Single(r => r.Value == LocalNodeId(nodeSet, LinkTarget));
            Assert.That(reference.ReferenceType, Is.EqualTo("i=49"));
            Assert.That(reference.IsForward, Is.True);
        }

        [Test]
        public void ANameAgreeingWithItsRefIdIsAccepted()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(
                WotTestData.Utf8(ThingModelWithLink(
                    "ua:OrderedComponentOf",
                    "\"uav:refId\":\"i=49\"")));

            UAObjectType root = nodeSet.Items!.OfType<UAObjectType>().Single();
            Reference reference = root.References!.Single(r => r.Value == LocalNodeId(nodeSet, LinkTarget));
            Assert.That(reference.ReferenceType, Is.EqualTo("i=49"));
            Assert.That(reference.IsForward, Is.False);
        }

        [Test]
        public void ANameDisagreeingWithItsRefIdIsReported()
        {
            using WotDocument document = WotDocument.Parse(
                WotTestData.Utf8(ThingModelWithLink(
                    "ua:OrderedComponentOf",
                    "\"uav:refId\":\"i=47\"")));

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ModelConceptConflict),
                Is.True,
                "A name and an identifier that name different ReferenceTypes " +
                "shall be reported rather than silently preferring one.");
        }

        [Test]
        public void ARefIdWithoutAResolvableNameStaysForward()
        {
            // Release 1.0 behaviour: uav:refId names the ReferenceType only,
            // never a direction, so the reference reads forward.
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(
                WotTestData.Utf8(ThingModelWithLink(
                    "pump:MaterialReference",
                    "\"uav:refId\":\"nsu=http://example.com/demo/pump;i=5001\"")));

            UAObjectType root = nodeSet.Items!.OfType<UAObjectType>().Single();
            Reference reference = root.References!.Single(r => r.Value == LocalNodeId(nodeSet, LinkTarget));
            Assert.That(reference.IsForward, Is.True);
        }

        [Test]
        public async Task ACompanionReferenceTypeResolvesThroughTheLocalContextAsync()
        {
            var resolver = new StubReferenceTypeResolver();
            resolver.Forward["MaterialReference"] =
                "nsu=http://example.com/demo/pump;i=5001";
            resolver.Inverse["MaterialOf"] =
                "nsu=http://example.com/demo/pump;i=5001";

            using WotDocument forward = WotDocument.Parse(
                WotTestData.Utf8(ThingModelWithLink(
                    "pump:MaterialReference", "\"uav:refName\":\"Material\"")));
            WotConversionResult<UANodeSet> forwardResult = await WotNodeSetConverter
                .ToNodeSetResultAsync(forward, null, null, null, resolver)
                .ConfigureAwait(false);

            using WotDocument inverse = WotDocument.Parse(
                WotTestData.Utf8(ThingModelWithLink(
                    "pump:MaterialOf", "\"uav:refName\":\"Material\"")));
            WotConversionResult<UANodeSet> inverseResult = await WotNodeSetConverter
                .ToNodeSetResultAsync(inverse, null, null, null, resolver)
                .ConfigureAwait(false);

            Reference forwardReference = forwardResult.Value!.Items!
                .OfType<UAObjectType>().Single()
                .References!.Single(r => r.Value == LocalNodeId(forwardResult.Value!, LinkTarget));
            Reference inverseReference = inverseResult.Value!.Items!
                .OfType<UAObjectType>().Single()
                .References!.Single(r => r.Value == LocalNodeId(inverseResult.Value!, LinkTarget));

            // A NodeSet2 document states a ReferenceType as a NodeSet-local
            // NodeId, so the portable identity the relation resolved to is
            // read back through the NodeSet's own namespace table.
            string forwardType = LocalNodeId(
                forwardResult.Value!, "nsu=http://example.com/demo/pump;i=5001");
            string inverseType = LocalNodeId(
                inverseResult.Value!, "nsu=http://example.com/demo/pump;i=5001");

            Assert.Multiple(() =>
            {
                Assert.That(forwardReference.ReferenceType, Is.EqualTo(forwardType));
                Assert.That(forwardReference.IsForward, Is.True);
                Assert.That(inverseReference.ReferenceType, Is.EqualTo(inverseType));
                Assert.That(
                    inverseReference.IsForward,
                    Is.False,
                    "A relation named by the companion type's InverseName runs " +
                    "the other way.");
            });
        }

        /// <summary>
        /// Maps a portable ExpandedNodeId onto the NodeSet-local NodeId a
        /// converted NodeSet uses for a ReferenceType or Reference target.
        /// </summary>
        private static string LocalNodeId(UANodeSet nodeSet, string portable)
        {
            int separator = portable.IndexOf(';', StringComparison.Ordinal);
            int index = Array.IndexOf(nodeSet.NamespaceUris!, portable[4..separator]) + 1;
            Assert.That(index, Is.GreaterThan(0));
            return "ns=" + index.ToString(CultureInfo.InvariantCulture) + portable[separator..];
        }

        private static async Task<UANodeSet> ConvertWithParentAsync(string rel)
        {
            string json =
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"}]," +
                "\"@type\":\"uav:object\",\"title\":\"Pump01\"," +
                "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;Pump\"," +
                "\"links\":[{\"rel\":\"" + rel + "\",\"href\":\"" + ParentNodeId + "\"}]}";

            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(json));
            var resolver = new StubParentResolver();
            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(document, null, null, null, resolver)
                .ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty,
                $"'{rel}' should be accepted as a parent placement relation.");
            Assert.That(result.Value, Is.Not.Null);
            return result.Value!;
        }

        private static string ThingModelWithRule(string rule)
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\"," +
                "\"uav:browseName\":\"nsu=http://example.com/demo/pump;PumpType\"," +
                "\"uav:id\":\"nsu=http://example.com/demo/pump;i=1001\"," +
                "\"properties\":{\"speed\":{\"@type\":\"uav:variableType\"," +
                "\"uav:browseName\":\"nsu=http://example.com/demo/pump;Speed\"," +
                "\"type\":\"number\",\"uav:modellingRule\":\"" + rule + "\"}}}";
        }

        private static string ThingModelWithLink(string rel, string? extra)
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"http://example.com/demo/pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\"," +
                "\"uav:browseName\":\"nsu=http://example.com/demo/pump;PumpType\"," +
                "\"uav:id\":\"nsu=http://example.com/demo/pump;i=1001\"," +
                "\"links\":[{\"rel\":\"" + rel + "\",\"href\":\"" + LinkTarget + "\"" +
                (extra is null ? string.Empty : "," + extra) + "}]}";
        }

        private static UANodeSet NodeSetWithModellingRule(string ruleNodeId)
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:rules"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:rules" }],
                Aliases =
                [
                    new NodeIdAlias { Alias = "HasComponent", Value = "i=47" },
                    new NodeIdAlias { Alias = "HasModellingRule", Value = "i=37" },
                    new NodeIdAlias { Alias = "HasSubtype", Value = "i=45" },
                    new NodeIdAlias { Alias = "HasTypeDefinition", Value = "i=40" },
                    new NodeIdAlias { Alias = "Double", Value = "i=11" }
                ],
                Items =
                [
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
                                ReferenceType = "HasComponent",
                                IsForward = true,
                                Value = "ns=1;i=6001"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=6001",
                        BrowseName = "1:Speed",
                        DisplayName = [new Export.LocalizedText { Value = "Speed" }],
                        DataType = "Double",
                        ParentNodeId = "ns=1;i=1001",
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasTypeDefinition",
                                IsForward = true,
                                Value = "i=63"
                            },
                            new Reference
                            {
                                ReferenceType = "HasModellingRule",
                                IsForward = true,
                                Value = ruleNodeId
                            },
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = false,
                                Value = "ns=1;i=1001"
                            }
                        ]
                    }
                ]
            };
        }

        /// <summary>
        /// A local context that holds the parent Node a placement link names.
        /// </summary>
        private sealed class StubParentResolver : IWotNodeResolver
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
                return new ValueTask<WotResolvedNode?>(
                    string.Equals(expandedNodeId, ParentNodeId, StringComparison.Ordinal)
                        ? new WotResolvedNode(ParentNodeId, WotExpectedNodeClass.Any)
                        : null);
            }
        }

        /// <summary>
        /// A local context that also declares companion ReferenceTypes, by
        /// BrowseName and by InverseName.
        /// </summary>
        private sealed class StubReferenceTypeResolver
            : IWotNodeResolver, IWotReferenceTypeResolver
        {
            public Dictionary<string, string> Forward { get; } = new(StringComparer.Ordinal);

            public Dictionary<string, string> Inverse { get; } = new(StringComparer.Ordinal);

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

            public ValueTask<ArrayOf<WotResolvedReferenceType>> ResolveReferenceTypesAsync(
                string namespaceUri,
                string name,
                CancellationToken cancellationToken = default)
            {
                if (Forward.TryGetValue(name, out string? forward))
                {
                    return new ValueTask<ArrayOf<WotResolvedReferenceType>>(
                        new ArrayOf<WotResolvedReferenceType>(
                            [new WotResolvedReferenceType(forward, name, true)]));
                }
                if (Inverse.TryGetValue(name, out string? inverse))
                {
                    return new ValueTask<ArrayOf<WotResolvedReferenceType>>(
                        new ArrayOf<WotResolvedReferenceType>(
                            [new WotResolvedReferenceType(inverse, name, false)]));
                }
                return new ValueTask<ArrayOf<WotResolvedReferenceType>>(
                    ArrayOf<WotResolvedReferenceType>.Empty);
            }
        }

        private const string ParentNodeId = "nsu=http://example.com/demo/pump;i=1001";
        private const string LinkTarget = "nsu=http://example.com/demo/pump;s=Blade_1";
    }
}
