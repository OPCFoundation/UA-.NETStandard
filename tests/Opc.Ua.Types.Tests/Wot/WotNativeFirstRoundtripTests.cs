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

using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotNativeFirstRoundtripTests
    {
        [Test]
        public void CompleteReadableMappingOmitsStructuredFallback()
        {
            var source = new UANodeSet
            {
                NamespaceUris = ["urn:test:readable"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:readable" }],
                Aliases =
                [
                    // Declared because the reference below names the type
                    // rather than identifying it, and a NodeSet2 document may
                    // only do that for a name it declares.
                    new NodeIdAlias { Alias = "HasSubtype", Value = "i=45" }
                ],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;s=ReadableType",
                        BrowseName = "1:ReadableType",
                        DisplayName =
                        [
                            new Opc.Ua.Export.LocalizedText
                            {
                                Value = "ReadableType"
                            }
                        ],
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=58"
                            }
                        ]
                    }
                ]
            };

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(document.TryGetNativeProjection(out _), Is.False);
            Assert.That(document.TryGetEnvelope(out _), Is.False);
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);
            NodeSetComparisonResult comparison = NodeSetComparer.Compare(source, restored);
            Assert.That(
                comparison.AreEquivalent,
                Is.True,
                string.Join("; ", comparison.Differences));
        }

        [Test]
        public void IncompleteReadableMappingUsesStructuredFallbackWithoutEnvelope()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(document.TryGetNativeProjection(out JsonElement projection), Is.True);
            Assert.That(
                projection.GetProperty("profileVersion").GetString(),
                Is.EqualTo("1.0"));
            Assert.That(document.TryGetEnvelope(out _), Is.False);

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);
            NodeSetComparisonResult comparison = NodeSetComparer.Compare(source, restored);
            Assert.That(
                comparison.AreEquivalent,
                Is.True,
                string.Join("; ", comparison.Differences));
        }

        [Test]
        public void NeverModeProvesCompleteSchemaRoundtripWithoutFallback()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Never
            };

            WotConversionResult<WotDocument> result =
                WotNodeSetConverter.FromNodeSetResult(source, options: options);

            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(document.TryGetEnvelope(out _), Is.False);

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document, options);
            Assert.That(
                WotTestData.Serialize(restored),
                Is.EqualTo(WotTestData.Serialize(source)));
        }

        [Test]
        public void WhenRequiredFallsBackOnlyWhenNativeProjectionIsBounded()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            var fallback = new WotNodeSetConverterOptions
            {
                MaxNodeCount = 1,
                PreservationMode = WotNodeSetPreservationMode.WhenRequired
            };

            WotConversionResult<WotDocument> result =
                WotNodeSetConverter.FromNodeSetResult(source, options: fallback);

            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(document.TryGetEnvelope(out _), Is.True);
            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.NativeProjectionIncomplete),
                Is.True);

            var nativeOnly = new WotNodeSetConverterOptions
            {
                MaxNodeCount = 1,
                PreservationMode = WotNodeSetPreservationMode.Never
            };
            WotConversionResult<WotDocument> rejected =
                WotNodeSetConverter.FromNodeSetResult(source, options: nativeOnly);
            Assert.That(rejected.Value, Is.Null);
            Assert.That(rejected.HasErrors, Is.True);
        }

        [Test]
        public void UnknownJsonLdResidueSurvivesTwoEnvelopeFreeRoundtrips()
        {
            const string json =
                "{" +
                "\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",{" +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"vendor\":\"urn:vendor:\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;PumpType\"," +
                "\"vendor:root\":{\"b\":2,\"a\":1}," +
                "\"properties\":{\"speed\":{" +
                "\"@type\":\"uav:variableType\",\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;Speed\"," +
                "\"type\":\"number\",\"readOnly\":true,\"observable\":true," +
                "\"forms\":[{\"href\":\"opc.tcp://example.test:4840\"," +
                "\"op\":[\"readproperty\"]}]," +
                "\"vendor:quality\":{\"mode\":\"good\"}}}}";

            UANodeSet firstNodeSet =
                WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));
            Assert.That(
                firstNodeSet.Extensions!.Any(e =>
                    e.LocalName == "WoTJsonResidue" &&
                    e.NamespaceURI == WotNodeSetConverter.VocabularyNamespace),
                Is.True);

            using WotDocument first = WotNodeSetConverter.FromNodeSet(firstNodeSet);
            Assert.That(first.TryGetEnvelope(out _), Is.False);
            AssertResidue(first);

            UANodeSet secondNodeSet = WotNodeSetConverter.ToNodeSet(first);
            using WotDocument second = WotNodeSetConverter.FromNodeSet(secondNodeSet);
            Assert.That(second.TryGetEnvelope(out _), Is.False);
            AssertResidue(second);
        }

        [TestCase("tm:extends")]
        [TestCase("ua:SubtypeOf")]
        [TestCase("ua:HasSupertype")]
        public void ContextAndMappedLinkResidueUseStableSelectors(string relation)
        {
            string json =
                "{" +
                "\"@context\":[{\"vendor\":\"urn:vendor:\"}," +
                "\"https://www.w3.org/2022/wot/td/v1.1\",{" +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"extra\":\"urn:extra:\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;PumpType\"," +
                "\"links\":[{\"rel\":\"" + relation + "\"," +
                "\"href\":\"nsu=urn:base;i=1001\",\"hreflang\":\"en\"}]}";

            UANodeSet nodeSet =
                WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));
            Reference supertype = nodeSet.Items!.OfType<UAObjectType>().Single().References!.Single(reference =>
                reference.ReferenceType is "HasSubtype" or "i=45");
            Assert.That(supertype.IsForward, Is.False);
            Assert.That(supertype.Value, Is.EqualTo(WotTestData.LocalNodeId(nodeSet, "nsu=urn:base;i=1001")));
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement context = restored.RootElement.GetProperty("@context");
            Assert.That(
                context[1].GetProperty("extra").GetString(),
                Is.EqualTo("urn:extra:"));
            Assert.That(
                context.EnumerateArray().Any(item =>
                    item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("vendor", out JsonElement vendor) &&
                    vendor.GetString() == "urn:vendor:"),
                Is.True);

            JsonElement links = restored.RootElement.GetProperty("links");
            Assert.That(links.GetArrayLength(), Is.EqualTo(1));
            // The inverse HasSubtype relation is canonical even when authored with a legacy relation spelling.
            Assert.That(links[0].GetProperty("rel").GetString(), Is.EqualTo("ua:HasSupertype"));
            Assert.That(links[0].GetProperty("uav:refId").GetString(), Is.EqualTo("i=45"));
            Assert.That(
                links[0].GetProperty("href").GetString(),
                Is.EqualTo("nsu=urn:base;i=1001"));
            Assert.That(links[0].GetProperty("hreflang").GetString(), Is.EqualTo("en"));

            UANodeSet secondNodeSet = WotNodeSetConverter.ToNodeSet(restored);
            using WotDocument second = WotNodeSetConverter.FromNodeSet(secondNodeSet);
            JsonElement secondLinks = second.RootElement.GetProperty("links");
            Assert.That(secondLinks.GetArrayLength(), Is.EqualTo(1));
            JsonElement secondLink = secondLinks[0];
            Assert.That(secondLink.GetProperty("rel").GetString(), Is.EqualTo("ua:HasSupertype"));
            Assert.That(secondLink.GetProperty("uav:refId").GetString(), Is.EqualTo("i=45"));
            Assert.That(secondLink.GetProperty("href").GetString(), Is.EqualTo("nsu=urn:base;i=1001"));
            Assert.That(secondLink.GetProperty("hreflang").GetString(), Is.EqualTo("en"));
            Reference secondSupertype = secondNodeSet.Items!.OfType<UAObjectType>().Single().References!
                .Single(reference => reference.ReferenceType is "HasSubtype" or "i=45");
            Assert.That(secondSupertype.IsForward, Is.False);
            Assert.That(secondSupertype.Value,
                Is.EqualTo(WotTestData.LocalNodeId(secondNodeSet, "nsu=urn:base;i=1001")));
        }

        [TestCase(WotNodeSetPreservationMode.Never)]
        [TestCase(WotNodeSetPreservationMode.Always)]
        public void InheritanceLinkResidueKeepsIndependentRelationships(WotNodeSetPreservationMode preservation)
        {
            using WotDocument authored = WotDocument.Parse(WotTestData.Utf8(
                """
                {
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "uav:id": "nsu=urn:test:residue-links;i=1",
                  "uav:browseName": "nsu=urn:test:residue-links;RootType",
                  "links": [
                    { "rel": "tm:extends", "href": "nsu=urn:base;i=1001", "hreflang": "en" },
                    { "rel": "ua:Organizes", "href": "nsu=urn:base;i=1001", "hreflang": "de" }
                  ]
                }
                """));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(authored);
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));

            for (int roundTrip = 0; roundTrip < 2; roundTrip++)
            {
                using WotDocument exported = WotNodeSetConverter.FromNodeSet(
                    result.Value!,
                    options: new WotNodeSetConverterOptions { PreservationMode = preservation });
                Assert.That(exported.Links, Has.Count.EqualTo(2));
                JsonElement supertype = exported.Links.Single(link =>
                    link.GetProperty("rel").GetString() == "ua:HasSupertype");
                JsonElement organizes = exported.Links.Single(link =>
                    link.GetProperty("rel").GetString() == "ua:Organizes");
                Assert.That(supertype.GetProperty("href").GetString(), Is.EqualTo("nsu=urn:base;i=1001"));
                Assert.That(supertype.GetProperty("uav:refId").GetString(), Is.EqualTo("i=45"));
                Assert.That(supertype.GetProperty("hreflang").GetString(), Is.EqualTo("en"));
                Assert.That(organizes.GetProperty("href").GetString(), Is.EqualTo("nsu=urn:base;i=1001"));
                Assert.That(organizes.GetProperty("uav:refId").GetString(), Is.EqualTo("i=35"));
                Assert.That(organizes.GetProperty("hreflang").GetString(), Is.EqualTo("de"));

                JsonObject projected = JsonNode.Parse(exported.Utf8Json.Span)!.AsObject();
                if (preservation == WotNodeSetPreservationMode.Never)
                {
                    projected.Remove("uav:nodes");
                    projected.Remove("uav:nodeSet");
                }
                else
                {
                    Assert.That(exported.TryGetEnvelope(out _), Is.True);
                }
                using WotDocument imported = WotDocument.Parse(WotTestData.Utf8(projected.ToJsonString()));
                result = WotNodeSetConverter.ToNodeSetResult(imported);
                Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
                Reference[] references = result.Value!.Items!.OfType<UAObjectType>().Single().References!;
                Assert.That(references, Has.Length.EqualTo(2));
                Assert.That(references.Any(reference =>
                    reference.ReferenceType is "HasSubtype" or "i=45" && !reference.IsForward &&
                    reference.Value == WotTestData.LocalNodeId(result.Value, "nsu=urn:base;i=1001")), Is.True);
                Assert.That(references.Any(reference =>
                    reference.ReferenceType is "Organizes" or "i=35" && reference.IsForward &&
                    reference.Value == WotTestData.LocalNodeId(result.Value, "nsu=urn:base;i=1001")), Is.True);
            }
        }

        [Test]
        public void ResidueHonorsConfiguredDepthAboveFrameworkDefault()
        {
            const int depth = 70;
            var nested = new StringBuilder();
            for (int ii = 0; ii < depth; ii++)
            {
                nested.Append("{\"next\":");
            }
            nested.Append("\"leaf\"");
            for (int ii = 0; ii < depth; ii++)
            {
                nested.Append('}');
            }

            string json =
                "{" +
                "\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",{" +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"DeepType\",\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;DeepType\"," +
                "\"vendor:deep\":" + nested + "}";
            var options = new WotNodeSetConverterOptions { MaxJsonDepth = 96 };

            using WotDocument source = WotDocument.Parse(
                Encoding.UTF8.GetBytes(json),
                options);
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(source, options);
            using WotDocument restored =
                WotNodeSetConverter.FromNodeSet(nodeSet, options: options);

            JsonElement current = restored.RootElement.GetProperty("vendor:deep");
            for (int ii = 0; ii < depth; ii++)
            {
                current = current.GetProperty("next");
            }
            Assert.That(current.GetString(), Is.EqualTo("leaf"));
        }

        [Test]
        public void ResiduePointerBeyondConfiguredDepthProducesDiagnostic()
        {
            const string json =
                "{" +
                "\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",{" +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"BoundedType\",\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;BoundedType\"," +
                "\"vendor:value\":1}";

            UANodeSet nodeSet =
                WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));
            System.Xml.XmlElement residue = nodeSet.Extensions!.Single(e =>
                e.LocalName == "WoTJsonResidue");
            System.Xml.XmlElement member = residue.ChildNodes
                .OfType<System.Xml.XmlElement>()
                .Single();
            member.SetAttribute("Pointer", string.Concat(Enumerable.Repeat("/x", 12)));

            var options = new WotNodeSetConverterOptions { MaxJsonDepth = 8 };
            WotConversionResult<WotDocument> result = null;
            Assert.That(
                () => result = WotNodeSetConverter.FromNodeSetResult(
                    nodeSet,
                    options: options),
                Throws.Nothing);
            using WotDocument document = result!.Value!;
            Assert.That(result.HasErrors, Is.True);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True);
        }

        [Test]
        public void ResidueUsesSameAffordanceCollisionKeysAsReadableMapping()
        {
            const string json =
                "{" +
                "\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",{" +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"CollisionType\",\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;CollisionType\"," +
                "\"properties\":{" +
                "\"first\":{\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;Temp\",\"type\":\"number\"," +
                "\"vendor:value\":\"first\"}," +
                "\"second\":{\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;Temp\",\"type\":\"number\"," +
                "\"vendor:value\":\"second\"}}}";

            UANodeSet nodeSet =
                WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));
            const string identityPrefix =
                "ns=1;s=/nsu=urn%3Aopcua%3Awot%3Asynthesized;CollisionType/" +
                "nsu=urn%3Aopcua%3Awot%3Asynthesized;";
            string[] expectedIds = [identityPrefix + "Temp", identityPrefix + "Temp_2"];
            UAVariable[] variables = nodeSet.Items!.OfType<UAVariable>().ToArray();
            Assert.That(variables.Select(variable => variable.NodeId), Is.EquivalentTo(expectedIds));
            Assert.That(variables.Select(variable => variable.BrowseName), Is.All.EqualTo("1:Temp"));
            Assert.That(nodeSet.Items.OfType<UAObjectType>().Single().References!.Where(reference =>
                reference.ReferenceType is "HasComponent" or "i=47" && reference.IsForward)
                .Select(reference => reference.Value), Is.EquivalentTo(expectedIds));
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement properties = restored.RootElement.GetProperty("properties");
            Assert.That(
                properties.GetProperty("Temp").GetProperty("vendor:value").GetString(),
                Is.EqualTo("first"));
            Assert.That(
                properties.GetProperty("Temp_2").GetProperty("vendor:value").GetString(),
                Is.EqualTo("second"));

            JsonObject readable = JsonNode.Parse(restored.Utf8Json.Span)!.AsObject();
            readable.Remove("uav:nodes");
            readable.Remove("uav:nodeSet");
            using WotDocument readableDocument = WotDocument.Parse(WotTestData.Utf8(readable.ToJsonString()));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(readableDocument);
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(result.Value!.Items!.OfType<UAVariable>().Select(variable => variable.NodeId),
                Is.EquivalentTo(expectedIds));
            using WotDocument second = WotNodeSetConverter.FromNodeSet(result.Value);
            Assert.That(second.Properties["Temp"].GetProperty("vendor:value").GetString(), Is.EqualTo("first"));
            Assert.That(second.Properties["Temp_2"].GetProperty("vendor:value").GetString(), Is.EqualTo("second"));
        }

        [TestCase(WotNodeSetPreservationMode.Never)]
        [TestCase(WotNodeSetPreservationMode.Always)]
        public async Task QualifiedCollisionAllocationAgreesAcrossPublicViewsAsync(
            WotNodeSetPreservationMode preservation)
        {
            using WotDocument authored = WotDocument.Parse(WotTestData.Utf8(
                """
                {
                  "@context": { "a": "urn:measurements:a", "b": "urn:measurements:b" },
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "uav:id": "nsu=urn:test:allocation;i=1",
                  "uav:browseName": "nsu=urn:test:allocation;Root",
                  "properties": {
                    "first": { "uav:browseName": "a:Value", "type": "number", "vendor:marker": "first" },
                    "second": { "uav:browseName": "a:Value", "type": "number", "vendor:marker": "second" },
                    "literal": { "uav:browseName": "a:Value_2", "type": "number", "vendor:marker": "literal" },
                    "other": { "uav:browseName": "b:Value", "type": "number", "vendor:marker": "other" }
                  }
                }
                """));
            const string identityPrefix = "ns=1;s=/nsu=urn%3Atest%3Aallocation;Root/";
            (string Key, string Marker, string Segment)[] expected =
            [
                ("Value", "first", "nsu=urn%3Ameasurements%3Aa;Value"),
                ("Value_2", "second", "nsu=urn%3Ameasurements%3Aa;Value_2"),
                ("Value_2_2", "literal", "nsu=urn%3Ameasurements%3Aa;Value_2_2"),
                ("Value_3", "other", "nsu=urn%3Ameasurements%3Ab;Value")
            ];
            string[] expectedIds = expected.Select(entry => identityPrefix + entry.Segment).ToArray();
            var resolver = new WotDocumentNodeResolver([authored]);
            WotTypeDeclarationSet described = await resolver.ResolveDeclarationsAsync(
                "nsu=urn:test:allocation;i=1", WotDeclarationScope.Direct).ConfigureAwait(false);
            Assert.That(described, Is.Not.Null);
            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetResultAsync(authored).ConfigureAwait(false);
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string[] describedIds = new string[described!.Declarations.Count];
            for (int index = 0; index < describedIds.Length; index++)
            {
                describedIds[index] = WotTestData.LocalNodeId(result.Value!, described.Declarations[index].NodeId!);
            }
            Assert.That(describedIds, Is.EquivalentTo(expectedIds));

            for (int roundTrip = 0; roundTrip < 2; roundTrip++)
            {
                Assert.That(result.Value!.Items!.OfType<UAVariable>().Select(variable => variable.NodeId),
                    Is.EquivalentTo(expectedIds));
                Reference[] ownership = result.Value.Items!.OfType<UAObjectType>().Single().References!
                    .Where(reference => reference.IsForward &&
                        reference.ReferenceType is "HasComponent" or "i=47").ToArray();
                Assert.That(ownership.Select(reference => reference.Value), Is.EquivalentTo(expectedIds));
                using WotDocument exported = WotNodeSetConverter.FromNodeSet(
                    result.Value,
                    options: new WotNodeSetConverterOptions { PreservationMode = preservation });
                Assert.That(exported.Properties, Has.Count.EqualTo(4));
                foreach ((string key, string marker, string segment) in expected)
                {
                    JsonElement property = exported.Properties[key];
                    Assert.That(property.GetProperty("vendor:marker").GetString(), Is.EqualTo(marker));
                    Assert.That(WotTestData.LocalNodeId(result.Value, property.GetProperty("uav:id").GetString()!),
                        Is.EqualTo(identityPrefix + segment));
                }
                JsonObject projected = JsonNode.Parse(exported.Utf8Json.Span)!.AsObject();
                if (preservation == WotNodeSetPreservationMode.Never)
                {
                    projected.Remove("uav:nodes");
                    projected.Remove("uav:nodeSet");
                }
                else
                {
                    Assert.That(exported.TryGetEnvelope(out _), Is.True);
                }
                using WotDocument imported = WotDocument.Parse(WotTestData.Utf8(projected.ToJsonString()));
                result = await WotNodeSetConverter.ToNodeSetResultAsync(imported).ConfigureAwait(false);
                Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CollidingMethodNamesKeepTheirArgumentsAndResidue(bool explicitIds)
        {
            string firstId = explicitIds ? "\"uav:id\":\"nsu=urn:test:methods;i=101\"," : string.Empty;
            string secondId = explicitIds ? "\"uav:id\":\"nsu=urn:test:methods;i=102\"," : string.Empty;
            using WotDocument authored = WotDocument.Parse(WotTestData.Utf8(
                $$"""
                {
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "uav:id": "nsu=urn:test:methods;i=1",
                  "uav:browseName": "nsu=urn:test:methods;Root",
                  "actions": {
                    "first": {
                      {{firstId}}
                      "uav:browseName": "nsu=urn:test:methods;Reset",
                      "vendor:marker": "first",
                      "input": { "type": "number", "vendor:marker": "firstInput" }
                    },
                    "second": {
                      {{secondId}}
                      "uav:browseName": "nsu=urn:test:methods;Reset",
                      "vendor:marker": "second",
                      "input": { "type": "string", "vendor:marker": "secondInput" }
                    }
                  }
                }
                """));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(authored);
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            const string methodPrefix = "ns=1;s=/nsu=urn%3Atest%3Amethods;Root/nsu=urn%3Atest%3Amethods;";
            string[] methodIds = explicitIds
                ? ["ns=1;i=101", "ns=1;i=102"]
                : [methodPrefix + "Reset", methodPrefix + "Reset_2"];
            string[] argumentIds = [methodPrefix + "Reset/InputArguments", methodPrefix + "Reset_2/InputArguments"];

            for (int roundTrip = 0; roundTrip < 2; roundTrip++)
            {
                UANode[] nodes = result.Value!.Items!;
                Assert.That(nodes.Select(node => node.NodeId), Is.Unique);
                Assert.That(nodes.OfType<UAMethod>().Select(method => method.NodeId), Is.EquivalentTo(methodIds));
                for (int index = 0; index < methodIds.Length; index++)
                {
                    UAMethod method = nodes.OfType<UAMethod>().Single(node => node.NodeId == methodIds[index]);
                    Assert.That(method.BrowseName, Is.EqualTo("1:Reset"));
                    Assert.That(method.References!.Any(reference =>
                        reference.ReferenceType is "HasProperty" or "i=46" && reference.IsForward &&
                        reference.Value == argumentIds[index]), Is.True);
                    UAVariable argument = nodes.OfType<UAVariable>().Single(node => node.NodeId == argumentIds[index]);
                    Assert.That(argument.ParentNodeId,
                        Is.EqualTo(methodIds[index]));
                }
                using WotDocument exported = WotNodeSetConverter.FromNodeSet(result.Value);
                Assert.That(exported.Actions, Has.Count.EqualTo(2));
                Assert.That(exported.Actions["Reset"].GetProperty("vendor:marker").GetString(), Is.EqualTo("first"));
                Assert.That(exported.Actions["Reset_2"].GetProperty("vendor:marker").GetString(), Is.EqualTo("second"));
                Assert.That(exported.Actions["Reset"].GetProperty("input").GetProperty("vendor:marker").GetString(),
                    Is.EqualTo("firstInput"));
                Assert.That(exported.Actions["Reset_2"].GetProperty("input").GetProperty("vendor:marker").GetString(),
                    Is.EqualTo("secondInput"));
                JsonObject readable = JsonNode.Parse(exported.Utf8Json.Span)!.AsObject();
                readable.Remove("uav:nodes");
                readable.Remove("uav:nodeSet");
                using WotDocument imported = WotDocument.Parse(WotTestData.Utf8(readable.ToJsonString()));
                result = WotNodeSetConverter.ToNodeSetResult(imported);
                Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CollidingAffordanceLinkResidueStaysOnItsOwningNode(bool escapedName)
        {
            string name = escapedName ? "Value/State" : "Value";
            using WotDocument authored = WotDocument.Parse(WotTestData.Utf8(
                $$"""
                {
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "uav:id": "nsu=urn:test:link-collision;i=1",
                  "uav:browseName": "nsu=urn:test:link-collision;Root",
                  "properties": {
                    "first": {
                      "uav:browseName": "nsu=urn:test:link-collision;{{name}}",
                      "type": "number",
                      "links": [{ "rel": "ua:HasTypeDefinition", "href": "i=63", "vendor:marker": "first" }]
                    },
                    "second": {
                      "uav:browseName": "nsu=urn:test:link-collision;{{name}}",
                      "type": "number",
                      "links": [{ "rel": "ua:HasTypeDefinition", "href": "i=63", "vendor:marker": "second" }]
                    }
                  }
                }
                """));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(authored);
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));

            for (int roundTrip = 0; roundTrip < 2; roundTrip++)
            {
                WotConversionResult<WotDocument> converted = WotNodeSetConverter.FromNodeSetResult(result.Value!);
                using WotDocument exported = converted.Value!;
                Assert.That(converted.Success, Is.True, string.Join("; ", converted.Diagnostics));
                Assert.That(exported.Links.Any(link =>
                    link.GetProperty("rel").GetString() == "ua:HasTypeDefinition"), Is.False);
                JsonElement firstLinks = exported.Properties[name].GetProperty("links");
                JsonElement secondLinks = exported.Properties[name + "_2"].GetProperty("links");
                Assert.That(firstLinks.GetArrayLength(), Is.EqualTo(1));
                Assert.That(secondLinks.GetArrayLength(), Is.EqualTo(1));
                Assert.That(firstLinks[0].GetProperty("href").GetString(), Is.EqualTo("i=63"));
                Assert.That(secondLinks[0].GetProperty("href").GetString(), Is.EqualTo("i=63"));
                Assert.That(firstLinks[0].GetProperty("vendor:marker").GetString(), Is.EqualTo("first"));
                Assert.That(secondLinks[0].GetProperty("vendor:marker").GetString(), Is.EqualTo("second"));
                JsonObject readable = JsonNode.Parse(exported.Utf8Json.Span)!.AsObject();
                readable.Remove("uav:nodes");
                readable.Remove("uav:nodeSet");
                using WotDocument imported = WotDocument.Parse(WotTestData.Utf8(readable.ToJsonString()));
                result = WotNodeSetConverter.ToNodeSetResult(imported);
                Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            }
        }

        private static void AssertResidue(WotDocument document)
        {
            JsonElement root = document.RootElement;
            Assert.That(
                root.GetProperty("vendor:root").GetProperty("b").GetInt32(),
                Is.EqualTo(2));

            JsonElement speed = root
                .GetProperty("properties")
                .GetProperty("Speed");
            Assert.That(
                speed.TryGetProperty("vendor:quality", out JsonElement quality),
                Is.True,
                Encoding.UTF8.GetString(document.Utf8Json.ToArray()));
            Assert.That(
                quality.GetProperty("mode").GetString(),
                Is.EqualTo("good"));
            Assert.That(
                speed.GetProperty("forms")[0].GetProperty("op")[0].GetString(),
                Is.EqualTo("readproperty"));

            JsonElement context = root.GetProperty("@context");
            Assert.That(
                context[1].GetProperty("vendor").GetString(),
                Is.EqualTo("urn:vendor:"));
        }
    }
}
