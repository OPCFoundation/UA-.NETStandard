/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Regression tests for the WoT converter / resolver / residue defects
    /// reported by the read-only bug audit of Opc.Ua.Types.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotAuditRegressionTests
    {
        [Test]
        public void NonFiniteRangeBoundsAreNotProjected()
        {
            // double.TryParse accepts NaN - which XmlConvert writes for an
            // uninitialised Range - and WriteNumber then threw
            // ArgumentOutOfRangeException out of FromNodeSet.
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet(withInstrumentRange: false);
            UAVariable range = source.Items!.OfType<UAVariable>()
                .First(v => v.BrowseName == "EURange");
            range.Value = WotAnalogTestData.RangeValue(double.NaN, double.NaN);

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement measurement = document.Properties["Measurement"];
            Assert.Multiple(() =>
            {
                Assert.That(measurement.TryGetProperty("minimum", out _), Is.False);
                Assert.That(measurement.TryGetProperty("maximum", out _), Is.False);
            });
        }

        [Test]
        public void FiniteRangeBoundsAreStillProjected()
        {
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet(withInstrumentRange: false);

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement measurement = document.Properties["Measurement"];
            Assert.Multiple(() =>
            {
                Assert.That(measurement.GetProperty("minimum").GetDouble(), Is.EqualTo(-5));
                Assert.That(measurement.GetProperty("maximum").GetDouble(), Is.EqualTo(95));
            });
        }

        [Test]
        public void StructureOfBaseDataTypeFieldsStaysAStructure()
        {
            // Every field of this Structure reads back as BaseDataType with no
            // Value, which the enumeration heuristic mistook for an
            // EnumDefinition.
            UANodeSet source = CreateDataTypeNodeSet(
                "Bag",
                "i=22",
                new Export.DataTypeDefinition
                {
                    Name = "1:Bag",
                    Field =
                    [
                        new Export.DataTypeField { Name = "First", DataType = "i=24" },
                        new Export.DataTypeField { Name = "Second", DataType = "i=24" }
                    ]
                });

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(
                DefinitionKindOf(document, "Bag"),
                Is.EqualTo("uav:StructureDefinition"));
        }

        [Test]
        public void EnumerationWithANegativeValueStaysAnEnumeration()
        {
            UANodeSet source = CreateDataTypeNodeSet(
                "State",
                "i=29",
                new Export.DataTypeDefinition
                {
                    Name = "1:State",
                    Field =
                    [
                        new Export.DataTypeField { Name = "Faulted", Value = -1 },
                        new Export.DataTypeField { Name = "Stopped", Value = 0 }
                    ]
                });

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(
                DefinitionKindOf(document, "State"),
                Is.EqualTo("uav:EnumDefinition"));
        }

        [Test]
        public async Task PortableBrowseNamesResolveInTheDocumentNodeResolverAsync()
        {
            // The nsu= form was split on the first ':', so the namespace of
            // "nsu=http://example.com/x;PumpType" became "http".
            const string json =
                "{" +
                "\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"Pump\"," +
                "\"uav:id\":\"nsu=http://example.com/x;s=PumpType\"," +
                "\"uav:browseName\":\"nsu=http://example.com/x;PumpType\"" +
                "}";

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));
            var resolver = new WotDocumentNodeResolver([document]);

            Assert.That(
                await resolver.HoldsNamespaceAsync("http://example.com/x").ConfigureAwait(false),
                Is.True,
                "the portable namespace should have been indexed");

            ArrayOf<WotResolvedNode> resolved = await resolver
                .ResolveByBrowseNameAsync(
                    "http://example.com/x",
                    "PumpType",
                    WotExpectedNodeClass.ObjectType)
                .ConfigureAwait(false);

            Assert.That(resolved.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task AnEscapedPortableNamespaceIsUnescapedWhenIndexedAsync()
        {
            // The nsu= namespace was indexed raw while every other reader of the
            // same member unescapes it, so a namespace URI containing ';' or '%'
            // was keyed one way and looked up the other.
            const string json =
                "{" +
                "\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"Pump\"," +
                "\"uav:id\":\"nsu=http://example.com/a%3Bb;s=PumpType\"," +
                "\"uav:browseName\":\"nsu=http://example.com/a%3Bb;PumpType\"" +
                "}";

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));
            var resolver = new WotDocumentNodeResolver([document]);

            Assert.That(
                await resolver.HoldsNamespaceAsync("http://example.com/a;b").ConfigureAwait(false),
                Is.True,
                "the unescaped namespace should have been indexed");

            ArrayOf<WotResolvedNode> resolved = await resolver
                .ResolveByBrowseNameAsync(
                    "http://example.com/a;b",
                    "PumpType",
                    WotExpectedNodeClass.ObjectType)
                .ConfigureAwait(false);

            Assert.That(resolved.Count, Is.EqualTo(1));
        }

        [Test]
        public void AffordanceLinkResidueStaysOnItsAffordance()
        {
            // Residue captured for /properties/<name>/links/- was appended to
            // the Thing's own links array, so the affordance lost its extras
            // and the Thing gained a second type definition link.
            string authored = CreateThingWithAffordanceLink(withExtra: true);
            string generated = CreateThingWithAffordanceLink(withExtra: false);

            var options = new WotNodeSetConverterOptions();
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test:audit"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:audit" }]
            };
            var diagnostics = new List<WotDiagnostic>();

            using (WotDocument authoredDocument = WotDocument.Parse(
                Encoding.UTF8.GetBytes(authored), options))
            {
                WotJsonResidue.Replace(nodeSet, authoredDocument, options, diagnostics);
            }

            byte[] merged = WotJsonResidue.Apply(
                Encoding.UTF8.GetBytes(generated), nodeSet, options, diagnostics);

            using JsonDocument parsed = JsonDocument.Parse(merged);
            JsonElement rootElement = parsed.RootElement;

            Assert.Multiple(() =>
            {
                Assert.That(
                    diagnostics.Count(d => d.Severity == WotDiagnosticSeverity.Error),
                    Is.Zero);
                Assert.That(
                    rootElement.TryGetProperty("links", out _),
                    Is.False,
                    "the Thing's own links must not gain the affordance's residue");

                JsonElement links = rootElement
                    .GetProperty("properties")
                    .GetProperty("Speed")
                    .GetProperty("links");
                Assert.That(links.GetArrayLength(), Is.EqualTo(1));
                Assert.That(
                    links[0].GetProperty("x:extra").GetString(),
                    Is.EqualTo("keep"));
            });
        }

        [Test]
        public void MalformedResidueIsReportedInsteadOfThrown()
        {
            // RemoveDocumentSetLinks parsed every residue member unguarded, so
            // a malformed one threw a JsonException out of the public
            // MergeNodeSetPartitions entry point.
            const string json =
                "{" +
                "\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"Thing\"," +
                "\"uav:browseName\":\"ns1:ThingType\"" +
                "}";

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));
            using var documents = new WotDocumentSet(
                "root",
                new[] { new WotDocumentSetEntry("root", document) }.ToArrayOf());

            var partition = new UANodeSet
            {
                NamespaceUris = ["urn:test:audit"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:audit" }],
                Items = [],
                Extensions = [CreateResidueExtension("/title", "{ not json")]
            };

            var diagnostics = new List<WotDiagnostic>();

            Assert.DoesNotThrow(() => WotJsonResidue.RemoveDocumentSetLinks(
                partition,
                document,
                documents,
                new WotNodeSetConverterOptions(),
                diagnostics));

            Assert.That(
                diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Code == WotDiagnosticCode.ResidueInvalid),
                Is.True,
                "the malformed member should have been reported");
        }

        private static System.Xml.XmlElement CreateResidueExtension(
            string pointer,
            string payload)
        {
            var document = new System.Xml.XmlDocument { XmlResolver = null };
            System.Xml.XmlElement root = document.CreateElement(
                "uav", "WoTJsonResidue", WotVocabulary.VocabularyNamespace);
            root.SetAttribute("Version", "1.0");
            document.AppendChild(root);

            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            System.Xml.XmlElement member = document.CreateElement(
                "uav", "Member", WotVocabulary.VocabularyNamespace);
            member.SetAttribute("Pointer", pointer);
            member.SetAttribute("Encoding", WotVocabulary.Base64Encoding);
            member.SetAttribute(
                "Sha256",
                CoreUtils.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
            member.InnerText = Convert.ToBase64String(bytes);
            root.AppendChild(member);
            return root;
        }

        private static string CreateThingWithAffordanceLink(bool withExtra)
        {
            return
                "{" +
                "\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"Thing\"," +
                "\"uav:browseName\":\"ns1:ThingType\"," +
                "\"properties\":{\"Speed\":{\"type\":\"number\",\"links\":[{" +
                "\"rel\":\"ua:HasTypeDefinition\",\"href\":\"i=68\"" +
                (withExtra ? ",\"x:extra\":\"keep\"" : string.Empty) +
                "}]}}" +
                "}";
        }

        private static UANodeSet CreateDataTypeNodeSet(
            string name,
            string baseTypeId,
            Export.DataTypeDefinition definition)
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:audit"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:audit" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;s=Root",
                        BrowseName = "1:RootType",
                        DisplayName = WotAnalogTestData.Text("RootType"),
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "i=45",
                                IsForward = false,
                                Value = "i=58"
                            }
                        ]
                    },
                    new UADataType
                    {
                        NodeId = "ns=1;s=" + name,
                        BrowseName = "1:" + name,
                        DisplayName = WotAnalogTestData.Text(name),
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "i=45",
                                IsForward = false,
                                Value = baseTypeId
                            }
                        ],
                        Definition = definition
                    }
                ]
            };
        }

        private static string? DefinitionKindOf(WotDocument document, string name)
        {
            JsonElement definitions = document.RootElement
                .GetProperty("uav:dataTypeDefinitions");
            foreach (JsonElement definition in definitions.EnumerateArray())
            {
                string? declared = definition.TryGetProperty(
                    "uav:dataTypeName", out JsonElement declaredName)
                    ? declaredName.GetString()
                    : null;
                if (declared is not null &&
                    declared.EndsWith(name, StringComparison.Ordinal))
                {
                    return definition.GetProperty("@type").GetString();
                }
            }
            return null;
        }
    }
}
