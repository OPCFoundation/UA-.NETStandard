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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Covers the engineering-unit, EURange and InstrumentRange mapping of WoT
    /// Binding Sections 6.4, 6.4.1, 7 and 9.1 in both directions.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotUnitsAndRangesTests
    {
        [Test]
        public void EngineeringUnitsProjectAllFourEuInformationFields()
        {
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement units = document.Properties["EngineeringUnits"]
                .GetProperty("uav:engineeringUnits");
            Assert.Multiple(() =>
            {
                Assert.That(
                    units.GetProperty("namespaceUri").GetString(),
                    Is.EqualTo(WotAnalogTestData.UnitAuthority),
                    "The authority that defines the unit is not recoverable from " +
                    "the display string.");
                Assert.That(units.GetProperty("unitId").GetInt32(), Is.EqualTo(4408652));
                Assert.That(units.GetProperty("displayName").GetString(), Is.EqualTo("°C"));
                Assert.That(
                    units.GetProperty("description").GetString(),
                    Is.EqualTo("degree Celsius"));
            });
        }

        [Test]
        public void AnnotatedVariableCarriesTheUnitAndACanonicalSiblingPointer()
        {
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement measurement = document.Properties["Measurement"];
            Assert.Multiple(() =>
            {
                Assert.That(measurement.GetProperty("unit").GetString(), Is.EqualTo("°C"));
                Assert.That(
                    measurement.GetProperty("uav:unitProperty").GetString(),
                    Is.EqualTo("/properties/EngineeringUnits"),
                    "Section 6.4 fixes the pointer at /properties/<name>.");
            });

            // The pointer resolves, in the same document, to a sibling property
            // affordance whose DataSchema type is string.
            Assert.That(
                WotDocument.TryEvaluatePointer(
                    document.RootElement,
                    measurement.GetProperty("uav:unitProperty").GetString()!,
                    out JsonElement target),
                Is.True);
            Assert.That(target.GetProperty("type").GetString(), Is.EqualTo("string"));
        }

        [Test]
        public void UnitAffordanceKeepsItsDefinitiveEuInformationDataType()
        {
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement unit = document.Properties["EngineeringUnits"];
            Assert.Multiple(() =>
            {
                Assert.That(
                    unit.GetProperty("type").GetString(),
                    Is.EqualTo("string"),
                    "A client reads a unit string at run time.");
                Assert.That(
                    unit.GetProperty("uav:mapToType").GetString(),
                    Is.EqualTo("i=887"),
                    "The Node behind it still holds an EUInformation.");
            });
        }

        [Test]
        public void EuRangeProjectsMinimumAndMaximum()
        {
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement measurement = document.Properties["Measurement"];
            Assert.Multiple(() =>
            {
                Assert.That(measurement.GetProperty("minimum").GetDouble(), Is.EqualTo(-5d));
                Assert.That(measurement.GetProperty("maximum").GetDouble(), Is.EqualTo(95d));
            });
        }

        [Test]
        public void InstrumentRangeProjectsItsOwnTerm()
        {
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement instrument = document.Properties["Measurement"]
                .GetProperty("uav:instrumentRange");
            Assert.Multiple(() =>
            {
                Assert.That(instrument.GetProperty("minimum").GetDouble(), Is.EqualTo(-50d));
                Assert.That(instrument.GetProperty("maximum").GetDouble(), Is.EqualTo(150d));
            });
        }

        [Test]
        public void QuantityKindIsNeverWrittenIntoTheUnitMember()
        {
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement measurement = document.Properties["Measurement"];
            Assert.Multiple(() =>
            {
                Assert.That(
                    measurement.GetProperty("unit").GetString(),
                    Does.Not.Contain("quantitykind"),
                    "Section 6.4: unit carries an engineering unit, never a " +
                    "quantity kind.");
                Assert.That(
                    measurement.TryGetProperty("qudt:hasQuantityKind", out _),
                    Is.False,
                    "A NodeSet states no quantity kind, so none is invented.");
            });
        }

        [Test]
        public void AuthoredQuantityKindStaysDistinctFromTheUnit()
        {
            using WotDocument original = ParseThingModel(
                "\"properties\":{\"speed\":{\"type\":\"number\",\"unit\":\"rpm\"," +
                "\"qudt:hasQuantityKind\":\"qudt-quantitykind:AngularVelocity\"," +
                "\"uav:unitProperty\":\"/properties/speedUnit\"}," +
                "\"speedUnit\":{\"type\":\"string\"," +
                "\"uav:browseName\":\"ua:EngineeringUnits\"," +
                "\"uav:engineeringUnits\":{" +
                "\"namespaceUri\":\"" +
                WotAnalogTestData.UnitAuthority +
                "\"," +
                "\"unitId\":5340017,\"displayName\":\"rpm\"}}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement speed = restored.Properties["speed"];
            Assert.Multiple(() =>
            {
                Assert.That(speed.GetProperty("unit").GetString(), Is.EqualTo("rpm"));
                Assert.That(
                    speed.GetProperty("qudt:hasQuantityKind").GetString(),
                    Is.EqualTo("qudt-quantitykind:AngularVelocity"),
                    "The quantity kind is a separate fact and survives unchanged.");
            });
        }

        [Test]
        public void PrimitiveWidthIsNeverMistakenForAnEngineeringRange()
        {
            var source = new UANodeSet
            {
                NamespaceUris = ["urn:test:analog"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:analog" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1000",
                        BrowseName = "1:CounterType",
                        DisplayName = WotAnalogTestData.Text("CounterType"),
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
                                Value = "ns=1;i=1001"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:Count",
                        DisplayName = WotAnalogTestData.Text("Count"),
                        ParentNodeId = "ns=1;i=1000",

                        // Int16 reads from -32768 to 32767; that is the width of
                        // the machine representation, not an engineering range.
                        DataType = "i=4",
                        AccessLevel = 1,
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = false,
                                Value = "ns=1;i=1000"
                            }
                        ]
                    }
                ]
            };

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement count = document.Properties["Count"];
            Assert.Multiple(() =>
            {
                Assert.That(count.TryGetProperty("minimum", out _), Is.False);
                Assert.That(count.TryGetProperty("maximum", out _), Is.False);
                Assert.That(count.TryGetProperty("uav:instrumentRange", out _), Is.False);
            });
        }

        [Test]
        public void ScaleFactorAndDecimalPlacesAreNeverInvented()
        {
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            foreach (KeyValuePair<string, JsonElement> affordance in document.Properties)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(
                        affordance.Value.TryGetProperty("uav:scaleFactor", out _),
                        Is.False,
                        $"'{affordance.Key}' gained a scale factor the source never " +
                        "stated; Section 6.4 forbids deriving one from the analog " +
                        "Properties.");
                    Assert.That(
                        affordance.Value.TryGetProperty("uav:decimalPlaces", out _),
                        Is.False);
                });
            }
        }

        [Test]
        public void ScaleFactorAndDecimalPlacesSurviveWithoutBecomingAnalogFacts()
        {
            using WotDocument original = ParseThingModel(
                "\"properties\":{\"speed\":{\"type\":\"number\",\"unit\":\"rpm\"," +
                "\"uav:scaleFactor\":0.1,\"uav:decimalPlaces\":1," +
                "\"minimum\":0,\"maximum\":3600}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);

            UAVariable speed = Variables(nodeSet).Single(v => v.BrowseName == "1:speed");
            Assert.That(
                Children(nodeSet, speed.NodeId!)
                    .Any(v => v.BrowseName is "EngineeringUnits"),
                Is.False,
                "Section 6.4: neither term derives an EngineeringUnits Property.");

            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);
            JsonElement affordance = restored.Properties["speed"];
            Assert.Multiple(() =>
            {
                Assert.That(affordance.GetProperty("uav:scaleFactor").GetDouble(), Is.EqualTo(0.1));
                Assert.That(affordance.GetProperty("uav:decimalPlaces").GetInt32(), Is.EqualTo(1));
            });
        }

        [Test]
        public void AuthoredRangesMaterializeAsPropertiesOfTheAnnotatedVariable()
        {
            using WotDocument original = ParseThingModel(
                "\"properties\":{\"speed\":{\"type\":\"number\",\"unit\":\"rpm\"," +
                "\"minimum\":0,\"maximum\":3600," +
                "\"uav:instrumentRange\":{\"minimum\":-50,\"maximum\":4200}}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);

            UAVariable speed = Variables(nodeSet).Single(v => v.BrowseName == "1:speed");
            UAVariable euRange = Children(nodeSet, speed.NodeId!)
                .Single(v => v.BrowseName == "EURange");
            UAVariable instrument = Children(nodeSet, speed.NodeId!)
                .Single(v => v.BrowseName == "InstrumentRange");
            Assert.Multiple(() =>
            {
                Assert.That(euRange.DataType, Is.EqualTo("i=884"));
                Assert.That(euRange.Value!.InnerXml, Does.Contain("<uax:Low>0</uax:Low>"));
                Assert.That(euRange.Value!.InnerXml, Does.Contain("<uax:High>3600</uax:High>"));
                Assert.That(
                    ModellingRuleOf(euRange),
                    Is.EqualTo("i=78"),
                    "OPC 10000-8 declares EURange Mandatory on AnalogItemType.");
                Assert.That(instrument.DataType, Is.EqualTo("i=884"));
                Assert.That(ModellingRuleOf(instrument), Is.EqualTo("i=80"));
                Assert.That(
                    TypeDefinitionOf(euRange),
                    Is.EqualTo("i=68"),
                    "A range is a Property.");
                Assert.That(
                    ReferenceTypeTo(nodeSet, speed.NodeId!, euRange.NodeId!),
                    Is.EqualTo("HasProperty"));
            });
        }

        [Test]
        public void AuthoredEngineeringUnitsMaterializeUnderTheAnnotatedVariable()
        {
            using WotDocument original = ParseThingModel(
                "\"properties\":{\"speed\":{\"type\":\"number\",\"unit\":\"rpm\"," +
                "\"uav:unitProperty\":\"/properties/speedUnit\"}," +
                "\"speedUnit\":{\"type\":\"string\"," +
                "\"uav:browseName\":\"ua:EngineeringUnits\"," +
                "\"uav:engineeringUnits\":{" +
                "\"namespaceUri\":\"" +
                WotAnalogTestData.UnitAuthority +
                "\"," +
                "\"unitId\":5340017,\"displayName\":\"rpm\"," +
                "\"description\":\"revolutions per minute\"}}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);

            UAVariable speed = Variables(nodeSet).Single(v => v.BrowseName == "1:speed");
            UAVariable unit = Children(nodeSet, speed.NodeId!)
                .Single(v => v.BrowseName == "EngineeringUnits");
            Assert.Multiple(() =>
            {
                Assert.That(
                    unit.DataType,
                    Is.EqualTo("i=887"),
                    "The pointed-to Node holds an EUInformation.");
                Assert.That(unit.Value!.InnerXml, Does.Contain("i=888"));
                Assert.That(
                    unit.Value!.InnerXml,
                    Does.Contain("<uax:UnitId>5340017</uax:UnitId>"));
                Assert.That(unit.Value!.InnerXml, Does.Contain("<uax:Text>rpm</uax:Text>"));
                Assert.That(
                    unit.Value!.InnerXml,
                    Does.Contain("<uax:Text>revolutions per minute</uax:Text>"));
                Assert.That(
                    ReferenceTypeTo(nodeSet, speed.NodeId!, unit.NodeId!),
                    Is.EqualTo("HasProperty"),
                    "OPC 10000-3 reaches a Property through HasProperty.");
            });
        }

        [Test]
        public void AnalogNodeSetRoundTripsWithoutTheStructuredFallback()
        {
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);
            Assert.That(
                document.RootElement.TryGetProperty("uav:nodes", out _),
                Is.False,
                "The readable mapping now covers the three analog Properties.");

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);
            Assert.That(
                NodeSetComparer.CompareEquivalent(
                    source, restored).AreEquivalent,
                Is.True);
        }

        [Test]
        public void AnalogNodeSetWithoutAnInstrumentRangeRoundTrips()
        {
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet(withInstrumentRange: false);

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);
            Assert.That(
                document.Properties["Measurement"].TryGetProperty("uav:instrumentRange", out _),
                Is.False);

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);
            Assert.That(
                NodeSetComparer.CompareEquivalent(
                    source, restored).AreEquivalent,
                Is.True);
        }

        [Test]
        public void AnUndecodableRangeValueIsPreservedRatherThanGuessed()
        {
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet(withInstrumentRange: false);
            UAVariable euRange = Variables(source).Single(v => v.BrowseName == "EURange");

            // A foreign encoding is a value this direction cannot read.
            euRange.Value = WotTestData.ParseValue(
                "<uax:ExtensionObject xmlns:uax=\"" +
                WotAnalogTestData.UaXmlNamespace +
                "\"><uax:TypeId><uax:Identifier>i=99999</uax:Identifier></uax:TypeId>" +
                "<uax:Body><uax:Range><uax:Low>1</uax:Low><uax:High>2</uax:High>" +
                "</uax:Range></uax:Body></uax:ExtensionObject>");

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.Multiple(() =>
            {
                Assert.That(
                    document.Properties["Measurement"].TryGetProperty("minimum", out _),
                    Is.False,
                    "A value the converter could not read is never restated as a bound.");
                Assert.That(
                    document.RootElement.TryGetProperty("uav:nodes", out _),
                    Is.True,
                    "The gap is reported through the preservation projection.");
            });

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);
            Assert.That(
                NodeSetComparer.CompareEquivalent(
                    source, restored).AreEquivalent,
                Is.True,
                "Nothing is silently lost.");
        }

        [Test]
        public void ReversedRangeIsReportedRatherThanNarrowed()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"minimum\":100,\"maximum\":10}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidRangeValue),
                Is.True);
        }

        [Test]
        public void ReversedInstrumentRangeIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"uav:instrumentRange\":{\"minimum\":10,\"maximum\":-10}}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidRangeValue),
                Is.True);
        }

        [Test]
        public void EngineeringRangeOutsideTheInstrumentRangeIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"minimum\":-100,\"maximum\":5000," +
                "\"uav:instrumentRange\":{\"minimum\":-50,\"maximum\":4200}}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidRangeValue),
                Is.True,
                "An engineering range outside what the instrument can measure is " +
                "not a fact about any instrument.");
        }

        [Test]
        public void NestedEngineeringRangeInsideTheInstrumentRangeIsAccepted()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"minimum\":0,\"maximum\":3600," +
                "\"uav:instrumentRange\":{\"minimum\":-50,\"maximum\":4200}}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidRangeValue),
                Is.False,
                WotAnalogTestData.Describe(result.Diagnostics));
        }

        [Test]
        public void InstrumentRangeThatIsNotAnObjectIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"uav:instrumentRange\":[0,10]}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidRangeValue),
                Is.True);
        }

        [Test]
        public void EngineeringUnitsWithoutAUnitIdIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speedUnit\":{\"type\":\"string\"," +
                "\"uav:engineeringUnits\":{" +
                "\"namespaceUri\":\"" +
                WotAnalogTestData.UnitAuthority +
                "\"," +
                "\"displayName\":\"rpm\"}}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidEngineeringUnits),
                Is.True,
                "A display string alone is lossy.");
        }

        [Test]
        public void EngineeringUnitsWithAFractionalUnitIdIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"speedUnit\":{\"type\":\"string\"," +
                "\"uav:engineeringUnits\":{" +
                "\"namespaceUri\":\"" +
                WotAnalogTestData.UnitAuthority +
                "\"," +
                "\"unitId\":1.5,\"displayName\":\"rpm\"}}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidEngineeringUnits),
                Is.True);
        }

        [Test]
        public async Task ThePublishedThingModelProjectsItsUnitPointerAndRangesAsync()
        {
            using var document = WotDocument.Parse(
                ReadExample("02-thing-model-pump.jsonld"));

            WotConversionResult<UANodeSet> result =
                await WotSpecExampleResolver.ConvertAsync(document).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty,
                WotAnalogTestData.Describe(result.Diagnostics));
            Assert.That(result.Value, Is.Not.Null);

            UAVariable pumpSpeed = Variables(result.Value!)
                .Single(v => v.BrowseName!.EndsWith(":PumpSpeed", StringComparison.Ordinal));
            UAVariable unit = Children(result.Value!, pumpSpeed.NodeId!)
                .Single(v => v.BrowseName == "EngineeringUnits");
            UAVariable euRange = Children(result.Value!, pumpSpeed.NodeId!)
                .Single(v => v.BrowseName == "EURange");
            UAVariable instrument = Children(result.Value!, pumpSpeed.NodeId!)
                .Single(v => v.BrowseName == "InstrumentRange");
            Assert.Multiple(() =>
            {
                Assert.That(unit.DataType, Is.EqualTo("i=887"));
                Assert.That(
                    unit.Value!.InnerXml,
                    Does.Contain("<uax:UnitId>5340017</uax:UnitId>"));
                Assert.That(euRange.Value!.InnerXml, Does.Contain("<uax:High>3600</uax:High>"));
                Assert.That(
                    instrument.Value!.InnerXml,
                    Does.Contain("<uax:High>4200</uax:High>"));
            });
        }

        [Test]
        public async Task ThePublishedThingModelImportsAsANodeSetAsync()
        {
            using var document = WotDocument.Parse(
                ReadExample("02-thing-model-pump.jsonld"));

            // The example links its event affordances to the definitions
            // example 27 declares, so converting it is converting a document
            // set: the resolver is the set, and nothing is fetched.
            WotConversionResult<UANodeSet> result =
                await WotSpecExampleResolver.ConvertAsync(document).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty,
                WotAnalogTestData.Describe(result.Diagnostics));

            UANodeSet nodeSet = result.Value!;
            using var stream = new System.IO.MemoryStream();
            nodeSet.Write(stream);
            stream.Position = 0;
            var reread = UANodeSet.Read(stream);

            Assert.That(reread, Is.Not.Null);
            Assert.That(reread!.Items, Has.Length.EqualTo(nodeSet.Items!.Length));
        }

        private static string ModellingRuleOf(UANode node)
        {
            return node.References!
                .First(r => r.ReferenceType == "HasModellingRule" && r.IsForward)
                .Value!;
        }

        private static string TypeDefinitionOf(UANode node)
        {
            return node.References!
                .First(r => r.ReferenceType == "HasTypeDefinition" && r.IsForward)
                .Value!;
        }

        private static string ReferenceTypeTo(UANodeSet nodeSet, string owner, string target)
        {
            UANode node = nodeSet.Items!.First(n => n.NodeId == owner);
            return node.References!
                .First(r => r.IsForward && r.Value == target)
                .ReferenceType!;
        }

        internal static IEnumerable<UAVariable> Variables(UANodeSet nodeSet)
        {
            return nodeSet.Items!.OfType<UAVariable>();
        }

        internal static IEnumerable<UAVariable> Children(UANodeSet nodeSet, string owner)
        {
            return Variables(nodeSet).Where(v =>
                v.References is not null &&
                v.References.Any(r =>
                    !r.IsForward &&
                    r.Value == owner &&
                    (r.ReferenceType is "HasProperty" or "HasComponent")));
        }

        internal static byte[] ReadExample(string name)
        {
            string resource = typeof(WotUnitsAndRangesTests).Assembly
                .GetManifestResourceNames()
                .Single(n => n.EndsWith("Wot.Assets." + name, StringComparison.Ordinal));
            using System.IO.Stream? stream = typeof(WotUnitsAndRangesTests).Assembly
                .GetManifestResourceStream(resource);
            Assert.That(stream, Is.Not.Null, $"The example '{name}' should be embedded.");
            using var buffer = new System.IO.MemoryStream();
            stream!.CopyTo(buffer);
            return buffer.ToArray();
        }

        private static WotConversionResult<UANodeSet> Convert(string members)
        {
            using WotDocument document = ParseThingModel(members);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }

        internal static WotDocument ParseThingModel(string members)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"qudt\":\"http://qudt.org/schema/qudt/\"," +
                "\"qudt-quantitykind\":\"http://qudt.org/vocab/quantitykind/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"Pump\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                members +
                "}");
            return WotDocument.Parse(json);
        }
    }
}
