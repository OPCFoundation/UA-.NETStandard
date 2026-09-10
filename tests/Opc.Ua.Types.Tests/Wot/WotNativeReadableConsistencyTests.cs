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
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable(ParallelScope.All)]
    public sealed class WotNativeReadableConsistencyTests
    {
        [TestCase("uav:id", "\"nsu=urn:test:model;i=9999\"")]
        [TestCase("uav:browseName", "\"nsu=urn:test:model;OtherSpeed\"")]
        [TestCase("uav:dataTypeId", "\"i=12\"")]
        [TestCase("uav:mapToType", "\"ua:String\"")]
        [TestCase("type", "\"boolean\"")]
        [TestCase("uav:valueRank", "1")]
        [TestCase("uav:arrayDimensions", "[3]")]
        [TestCase("const", "99.5")]
        [TestCase("readOnly", "true")]
        [TestCase("description", "\"A different description\"")]
        [TestCase("uav:modellingRule", "\"Optional\"")]
        public async Task NativeOnlyReadableContradictionsFailWithoutChangingNativeFacts(string member, string value)
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            JsonObject root = NativeDocument(source);
            root["properties"]!["Speed"]![member] = JsonNode.Parse(value);
            string native = root["uav:nodes"]!.ToJsonString();
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(document)
                .ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(item =>
                item.Code == WotDiagnosticCode.NativeProjectionConflict &&
                item.Location?.JsonPointer?.StartsWith("/properties/Speed", StringComparison.Ordinal) == true &&
                item.Location.NodeId == (member == "uav:id" ? "ns=1;i=9999" : "ns=1;i=6001")),
                Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.Value!.Items.Select(item => item.NodeId),
                Is.EquivalentTo(source.Items.Select(item => item.NodeId)));
            UAVariable expected = source.Items.OfType<UAVariable>().Single(item => item.NodeId == "ns=1;i=6001");
            UAVariable actual = result.Value.Items.OfType<UAVariable>().Single(item => item.NodeId == expected.NodeId);
            Assert.That(actual.DataType, Is.EqualTo(expected.DataType));
            Assert.That(actual.ValueRank, Is.EqualTo(expected.ValueRank));
            Assert.That(actual.BrowseName, Is.EqualTo(expected.BrowseName));
            Assert.That(actual.Value.OuterXml, Is.EqualTo(expected.Value.OuterXml));
            Assert.That(document.RootElement.GetProperty("uav:nodes").GetRawText(), Is.EqualTo(native));
        }

        [TestCase("uav:id", "\"nsu=urn:test:model;i=9999\"")]
        [TestCase("uav:browseName", "\"nsu=urn:test:model;OtherType\"")]
        [TestCase("@type", "\"uav:object\"")]
        [TestCase("uav:hasComponent", "[\"nsu=urn:test:model;i=9999\"]")]
        [TestCase("links", /*lang=json,strict*/ "[{\"rel\":\"ua:HasSupertype\",\"href\":\"i=62\"}]")]
        public void NativeOnlyRootFactsRemainAuthoritative(string member, string value)
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            JsonObject root = NativeDocument(source);
            root[member] = JsonNode.Parse(value);
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(item =>
                item.Code == WotDiagnosticCode.NativeProjectionConflict &&
                item.Location?.JsonPointer?.StartsWith("/" + member, StringComparison.Ordinal) == true &&
                !string.IsNullOrEmpty(item.Location.NodeId)), Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(result.Value!.Items.Select(item => item.NodeId),
                Is.EquivalentTo(source.Items.Select(item => item.NodeId)));
        }

        [TestCase("input")]
        [TestCase("output")]
        public void NativeOnlyZeroArgumentMethodsCannotAcquireReadableArguments(string member)
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            JsonObject root = NativeDocument(source);
            JsonObject action = root["actions"]!["Reset"]!.AsObject();
            Assert.That(action.ContainsKey(member), Is.False);
            action[member] = JsonNode.Parse("""{"type":"integer","uav:mapToType":"i=6"}""");
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(item =>
                item.Code == WotDiagnosticCode.NativeProjectionConflict &&
                item.Location?.JsonPointer == "/actions/Reset/" + member), Is.True, string.Join("; ", result.Diagnostics));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void NativeSymmetricReferencesAcceptEitherStoredDirectionButAsymmetricReferencesDoNot(bool symmetric)
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            var referenceType = new UAReferenceType
            {
                NodeId = "ns=1;i=90001",
                BrowseName = "1:Connects",
                Symmetric = symmetric,
                References = [new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=32" }]
            };
            source.Items = [.. source.Items, referenceType];
            UANode owner = source.Items.Single(node => node.NodeId == "ns=1;i=1001");
            owner.References =
            [
                .. owner.References,
                new Reference { ReferenceType = referenceType.NodeId, IsForward = false, Value = "ns=1;i=6001" }
            ];
            JsonObject root = NativeDocument(source);
            root["links"] = JsonNode.Parse(
                """
                [{
                  "rel":"ns1:Connects", "uav:refId":"nsu=urn:test:model;i=90001",
                  "href":"nsu=urn:test:model;i=6001"
                }]
                """);
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.EqualTo(symmetric), string.Join("; ", result.Diagnostics));
            if (!symmetric)
            {
                Assert.That(result.Diagnostics.Any(item =>
                    item.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    item.Location?.JsonPointer == "/links/0"), Is.True);
            }
            Assert.That(result.Value!.Items.Single(node => node.NodeId == owner.NodeId).References.Any(reference =>
                reference.ReferenceType == referenceType.NodeId &&
                !reference.IsForward &&
                reference.Value == "ns=1;i=6001"), Is.True);
        }

        [TestCase("value")]
        [TestCase("node-extension")]
        [TestCase("model-extension")]
        public void NativeXmlFragmentsKeepWhitespaceText(string location)
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            var xml = new System.Xml.XmlDocument { XmlResolver = null, PreserveWhitespace = true };
            const string whitespace = "\n  \t";
            using var text = new System.IO.StringReader(
                "<uax:String xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                whitespace +
                "</uax:String>");
            using var reader = System.Xml.XmlReader.Create(
                text,
                new System.Xml.XmlReaderSettings
                {
                    DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                    XmlResolver = null
                });
            xml.Load(reader);
            System.Xml.XmlElement expected = xml.DocumentElement!;
            UAVariable variable = source.Items.OfType<UAVariable>().Single(node => node.NodeId == "ns=1;i=6001");
            if (location == "value")
            {
                variable.DataType = "i=12";
                variable.Value = expected;
            }
            else if (location == "node-extension")
            {
                variable.Extensions = [expected];
            }
            else
            {
                source.Extensions = [expected];
            }
            JsonObject generated = NativeDocument(source);
            var root = new JsonObject { ["uav:nodes"] = generated["uav:nodes"]!.DeepClone() };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            UAVariable restored = result.Value!.Items.OfType<UAVariable>().Single(node => node.NodeId == variable.NodeId);
            System.Xml.XmlElement actual = location switch
            {
                "value" => restored.Value,
                "node-extension" => restored.Extensions![0],
                _ => result.Value.Extensions![0]
            };
            Assert.That(actual.InnerText, Is.EqualTo(whitespace));
            Assert.That(actual.OuterXml, Is.EqualTo(expected.OuterXml));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeLocalizedFactsUseTheDocumentFallbackButRejectAnotherLanguage(bool german)
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            UAVariable variable = source.Items.OfType<UAVariable>().Single(node => node.NodeId == "ns=1;i=6001");
            variable.Description = [new Export.LocalizedText { Locale = "en", Value = "Speed description" }];
            JsonObject root = NativeDocument(source);
            var context = new JsonObject
            {
                ["uav"] = "http://opcfoundation.org/UA/WoT-Binding/",
                ["ns1"] = "urn:test:model"
            };
            if (german)
            {
                context["@language"] = "de";
            }
            root["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1", context);
            root.Remove("description");
            root.Remove("actions");
            root.Remove("events");
            root.Remove("links");
            root["properties"] = JsonNode.Parse(
                """{"speed":{"uav:id":"nsu=urn:test:model;i=6001","description":"Speed description"}}""");
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.EqualTo(!german), string.Join("; ", result.Diagnostics));
            if (german)
            {
                Assert.That(result.Diagnostics.Any(item =>
                    item.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    item.Location?.JsonPointer == "/properties/speed/description"), Is.True);
            }
        }

        [TestCase("title", "titles")]
        [TestCase("description", "descriptions")]
        public void GeneratedSingletonNonDefaultLocaleRemainsConsistentWithNative(string singular, string plural)
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            UAVariable variable = source.Items.OfType<UAVariable>().Single(node => node.NodeId == "ns=1;i=6001");
            Export.LocalizedText[] text = [new Export.LocalizedText { Locale = "de", Value = "Drehzahl" }];
            if (singular == "title")
            {
                variable.DisplayName = text;
            }
            else
            {
                variable.Description = text;
            }
            JsonObject root = NativeDocument(source);
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(root["properties"]!["Speed"]![singular]!.GetValue<string>(), Is.EqualTo("Drehzahl"));
            Assert.That(root["properties"]!["Speed"]![plural]!["de"]!.GetValue<string>(), Is.EqualTo("Drehzahl"));
            UAVariable restored = result.Value!.Items.OfType<UAVariable>().Single(node => node.NodeId == variable.NodeId);
            Export.LocalizedText[] restoredText = singular == "title" ? restored.DisplayName : restored.Description;
            Assert.That(restoredText, Has.Length.EqualTo(1));
            Assert.That(restoredText[0].Locale, Is.EqualTo("de"));
            Assert.That(restoredText[0].Value, Is.EqualTo("Drehzahl"));
        }

        [TestCase(false, 4)]
        [TestCase(true, 4)]
        [TestCase(true, 1001)]
        public void UnassertedNativeFactsDoNotConsumeReadableSchemaDepth(bool readableIdentity, int rank)
        {
            JsonObject root = CreateNativeRankedDocument(rank);
            if (readableIdentity)
            {
                root["properties"] = JsonNode.Parse("""{"P":{"uav:id":"nsu=urn:test;i=2","type":"array"}}""");
            }
            var options = new WotNodeSetConverterOptions { MaxJsonDepth = 6 };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), options);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document, options);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(result.Value!.Items, Has.Length.EqualTo(2));
            UAVariable restored = result.Value.Items.OfType<UAVariable>().Single();
            Assert.That(restored.NodeId, Is.EqualTo("ns=1;i=2"));
            Assert.That(restored.DataType, Is.EqualTo("i=6"));
            Assert.That(restored.ValueRank, Is.EqualTo(rank));
        }

        [Test]
        public void EmptyReadableDefinitionListsDoNotRegenerateUnassertedSchemas()
        {
            JsonObject root = CreateNativeRankedDocument(1001);
            root["properties"] = JsonNode.Parse("""{"P":{"uav:id":"nsu=urn:test;i=2","type":"array"}}""");
            root["uav:dataTypeDefinitions"] = new JsonArray();
            var options = new WotNodeSetConverterOptions { MaxJsonDepth = 6 };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), options);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document, options);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(result.Value!.Items.OfType<UAVariable>().Single().ValueRank, Is.EqualTo(1001));
        }

        [Test]
        public void DataTypeAssertionsDoNotRegenerateUnrelatedHighRankProperties()
        {
            JsonObject root = CreateNativeRankedDocument(1001);
            root["uav:nodes"]!["nodes"]!.AsArray().Add(JsonNode.Parse(
                """
                {
                  "nodeClass":"DataType", "nodeId":"ns=1;i=3", "browseName":"1:T",
                  "references":[{"referenceType":"i=45","isForward":false,"target":"i=6"}]
                }
                """));
            root["uav:dataTypeDefinitions"] = JsonNode.Parse("""[{"uav:dataTypeId":"nsu=urn:test;i=3"}]""");
            var options = new WotNodeSetConverterOptions { MaxJsonDepth = 6 };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), options);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document, options);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(result.Value!.Items, Has.Length.EqualTo(3));
            Assert.That(result.Value.Items.OfType<UAVariable>().Single().ValueRank, Is.EqualTo(1001));
            Assert.That(result.Value.Items.OfType<UADataType>().Single().NodeId, Is.EqualTo("ns=1;i=3"));
        }

        [Test]
        public void DataTypeOnlyAssertionsDoNotRequestRootEventData()
        {
            JsonObject root = CreateNativeRankedDocument(1001);
            root["uav:nodes"]!["nodes"]![0]!["nodeClass"] = "ObjectType";
            root["uav:nodes"]!["nodes"]![0]!["references"] = JsonNode.Parse(
                """
                [
                  {"referenceType":"i=45","isForward":false,"target":"i=2041"},
                  {"referenceType":"i=46","target":"ns=1;i=2"}
                ]
                """);
            root["uav:nodes"]!["nodes"]!.AsArray().Add(JsonNode.Parse(
                """
                {
                  "nodeClass":"DataType", "nodeId":"ns=1;i=3", "browseName":"1:T",
                  "references":[{"referenceType":"i=45","isForward":false,"target":"i=6"}]
                }
                """));
            root["uav:dataTypeDefinitions"] = JsonNode.Parse("""[{"uav:dataTypeId":"nsu=urn:test;i=3"}]""");
            var options = new WotNodeSetConverterOptions { MaxJsonDepth = 6 };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), options);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document, options);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(result.Value!.Items.OfType<UAVariable>().Single().ValueRank, Is.EqualTo(1001));
            Assert.That(result.Value.Items.OfType<UADataType>().Single().NodeId, Is.EqualTo("ns=1;i=3"));
        }

        [TestCase(false, "T", false)]
        [TestCase(true, "T", true)]
        [TestCase(true, "Missing", false)]
        public void NativeDataTypeDefinitionsResolveNameOnlyIdentities(
            bool nativeType, string typeName, bool compatible)
        {
            JsonObject root = CreateNativeRankedDocument(-1);
            if (nativeType)
            {
                root["uav:nodes"]!["nodes"]!.AsArray().Add(JsonNode.Parse(
                    """
                    {
                      "nodeClass":"DataType", "nodeId":"ns=1;i=3", "browseName":"1:T",
                      "references":[{"referenceType":"i=45","isForward":false,"target":"i=6"}]
                    }
                    """));
            }
            root["@context"] = new JsonObject { ["model"] = "urn:test" };
            root["uav:dataTypeDefinitions"] = new JsonArray
            {
                new JsonObject { ["uav:dataTypeName"] = "model:" + typeName }
            };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.EqualTo(compatible), string.Join("; ", result.Diagnostics));
            Assert.That(result.Value!.Items.OfType<UADataType>().Count(), Is.EqualTo(nativeType ? 1 : 0));
            if (!compatible)
            {
                Assert.That(result.Diagnostics.Any(item =>
                    item.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    item.Location?.JsonPointer == "/uav:dataTypeDefinitions/0"), Is.True);
            }
        }

        [Test]
        public void ReadableAndRegeneratedDataTypeNamesUseTheirRespectiveContexts()
        {
            JsonObject root = CreateNativeRankedDocument(-1);
            root["uav:nodes"]!["nodes"]!.AsArray().Add(JsonNode.Parse(
                """
                {
                  "nodeClass":"DataType", "nodeId":"ns=1;i=3", "browseName":"1:T",
                  "references":[{"referenceType":"i=45","isForward":false,"target":"i=6"}]
                }
                """));
            root["@context"] = new JsonObject { ["model"] = "urn:test" };
            root["uav:dataTypeDefinitions"] = JsonNode.Parse(
                """[{"uav:dataTypeName":"model:T","uav:dataTypeId":"nsu=urn:test;i=3"}]""");
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(result.Value!.Items.OfType<UADataType>().Single().BrowseName, Is.EqualTo("1:T"));
        }

        [TestCase("properties")]
        [TestCase("actions")]
        [TestCase("events")]
        public void EmptyNativeModelsRejectEveryUnmatchedAffordanceIdentity(string collection)
        {
            JsonObject root = CreateNativeRankedDocument(-1);
            root["uav:nodes"]!["nodes"] = new JsonArray();
            root[collection] = JsonNode.Parse("""{"P":{"uav:id":"nsu=urn:test;i=9"}}""");
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value!.Items, Is.Empty);
            Assert.That(result.Diagnostics.Any(item =>
                item.Code == WotDiagnosticCode.NativeProjectionConflict &&
                item.Location?.JsonPointer == "/" + collection + "/P/uav:id" &&
                item.Location.NodeId == "ns=1;i=9"), Is.True, string.Join("; ", result.Diagnostics));
        }

        [TestCase("i=13", true)]
        [TestCase("i=13", false)]
        [TestCase("i=14", true)]
        [TestCase("i=14", false)]
        [TestCase("i=15", true)]
        [TestCase("i=15", false)]
        [TestCase("i=23751", true)]
        [TestCase("i=23751", false)]
        public void NativeScalarTypeChecksRetainEveryStringRefinement(string dataType, bool compatible)
        {
            JsonObject root = CreateNativeRankedDocument(-1, dataType);
            root["properties"] = new JsonObject
            {
                ["P"] = new JsonObject
                {
                    ["uav:id"] = "nsu=urn:test;i=2",
                    ["type"] = compatible ? "string" : "boolean"
                }
            };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.EqualTo(compatible), string.Join("; ", result.Diagnostics));
            Assert.That(result.Value!.Items.OfType<UAVariable>().Single().DataType, Is.EqualTo(dataType));
            if (!compatible)
            {
                Assert.That(result.Diagnostics.Any(item =>
                    item.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    item.Location?.JsonPointer == "/properties/P/type" &&
                    item.Location.NodeId == "ns=1;i=2"), Is.True);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void NativeFactProjectionSpendsTheAffordanceBudgetOnlyOnAssertedSchemas(bool compatible)
        {
            JsonObject root = CreateNativeEventDocument();
            root["events"] = new JsonObject
            {
                ["E"] = new JsonObject
                {
                    ["uav:id"] = "nsu=urn:test;i=3",
                    ["data"] = new JsonObject { ["type"] = compatible ? "object" : "boolean" }
                }
            };
            var options = new WotNodeSetConverterOptions { MaxAffordanceCount = 1 };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), options);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document, options);

            Assert.That(result.Success, Is.EqualTo(compatible), string.Join("; ", result.Diagnostics));
            Assert.That(result.Value!.Items, Has.Length.EqualTo(3));
            if (!compatible)
            {
                Assert.That(result.Diagnostics.Any(item =>
                    item.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    item.Location?.JsonPointer == "/events/E/data"), Is.True);
            }
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        public void NativeFactProjectionCannotDiscardItsOwnBudgetDiagnostics(int limit, bool compatible)
        {
            JsonObject root = CreateNativeEventDocument();
            JsonObject second = root["uav:nodes"]!["nodes"]![2]!.DeepClone().AsObject();
            second["nodeId"] = "ns=1;i=4";
            second["browseName"] = "1:F";
            root["uav:nodes"]!["nodes"]!.AsArray().Add(second);
            root["uav:nodes"]!["nodes"]![0]!["references"]!.AsArray().Add(
                JsonNode.Parse("""{"referenceType":"i=41","target":"ns=1;i=4"}"""));
            root["events"] = JsonNode.Parse(
                """
                {
                  "E":{"uav:id":"nsu=urn:test;i=3","data":{"type":"object"}},
                  "F":{"uav:id":"nsu=urn:test;i=4","data":{"type":"object"}}
                }
                """);
            var options = new WotNodeSetConverterOptions { MaxAffordanceCount = limit };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), options);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document, options);

            Assert.That(result.Success, Is.EqualTo(compatible), string.Join("; ", result.Diagnostics));
            if (!compatible)
            {
                Assert.That(result.Diagnostics.Any(item =>
                    item.Severity == WotDiagnosticSeverity.Error &&
                    item.Code == WotDiagnosticCode.AffordanceCountExceeded), Is.True);
            }
        }

        [Test]
        public void RequiredNativeProjectionDepthFailureIsReportedWithoutThrowing()
        {
            JsonObject root = CreateNativeEventDocument();
            root["uav:nodes"]!["nodes"]![2]!["references"]!.AsArray().Add(
                JsonNode.Parse("""{"referenceType":"i=46","target":"ns=1;i=4"}"""));
            root["uav:nodes"]!["nodes"]!.AsArray().Add(JsonNode.Parse(
                """
                {
                  "nodeClass":"Variable", "nodeId":"ns=1;i=4", "browseName":"1:Values",
                  "dataType":"i=6", "valueRank":1001
                }
                """));
            root["events"] = JsonNode.Parse("""{"E":{"uav:id":"nsu=urn:test;i=3","data":{"type":"object"}}}""");
            var options = new WotNodeSetConverterOptions { MaxJsonDepth = 6 };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), options);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document, options);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value!.Items, Has.Length.EqualTo(4));
            Assert.That(result.Diagnostics.Any(item =>
                item.Severity == WotDiagnosticSeverity.Error &&
                item.Message.Contains("depth", StringComparison.OrdinalIgnoreCase)), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyNativeModelsRejectAnUnmatchedReadableDataTypeIdentity(bool addDefinition)
        {
            JsonObject root = JsonNode.Parse(
                """
                {
                  "uav:nodes": {
                    "@type": "uav:NodeModel", "profileVersion": "1.0",
                    "namespaceUris": ["urn:test"], "nodes": []
                  }
                }
                """)!.AsObject();
            if (addDefinition)
            {
                root["uav:dataTypeDefinitions"] = JsonNode.Parse(
                    """
                    [{
                      "@type": "uav:SimpleDataType",
                      "uav:dataTypeName": "nsu=urn:test;Extra",
                      "uav:dataTypeId": "nsu=urn:test;i=9"
                    }]
                    """);
            }
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.EqualTo(!addDefinition), string.Join("; ", result.Diagnostics));
            Assert.That(result.Value!.Items, Is.Empty);
            if (addDefinition)
            {
                Assert.That(result.Diagnostics.Any(item =>
                    item.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    item.Location?.JsonPointer?.StartsWith("/uav:dataTypeDefinitions/0", StringComparison.Ordinal) ==
                        true &&
                    item.Location.NodeId == "ns=1;i=9"), Is.True, string.Join("; ", result.Diagnostics));
            }
        }

        [TestCase("all", true)]
        [TestCase("reversed", true)]
        [TestCase("event-only", true)]
        [TestCase("comment-only", false)]
        [TestCase("duplicate-event", false)]
        [TestCase("string-comment", false)]
        public void NativeConditionPairingKeepsLocalOwnershipAndOnlyCommentMayBeOptional(
            string required, bool compatible)
        {
            UANodeSet source = CreateNativeConditionSource(required == "string-comment" ? "i=12" : "i=21");
            UAMethod method = source.Items.OfType<UAMethod>().Single(node => node.NodeId == "ns=1;i=7001");
            UAVariable arguments = source.Items.OfType<UAVariable>().Single(node => node.NodeId == "ns=1;i=90002");
            JsonObject root = NativeDocument(source);
            JsonObject action = root["actions"]!["Reset"]!.AsObject();
            Assert.That(root["events"]!["Alarm"], Is.Not.Null);
            action["uav:conditionAction"] = "Acknowledge";
            action["uav:actsOn"] = "Alarm";
            action["input"]!["required"] = required switch
            {
                "event-only" or "string-comment" => new JsonArray("EventId"),
                "comment-only" => new JsonArray("Comment"),
                "duplicate-event" => new JsonArray("EventId", "EventId"),
                "reversed" => new JsonArray("Comment", "EventId"),
                _ => new JsonArray("EventId", "Comment")
            };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.EqualTo(compatible), string.Join("; ", result.Diagnostics));
            UAMethod restored = result.Value!.Items.OfType<UAMethod>().Single(node => node.NodeId == method.NodeId);
            Assert.That(restored.ParentNodeId, Is.EqualTo(method.ParentNodeId));
            Assert.That(result.Value.Items.OfType<UAVariable>().Single(node => node.NodeId == arguments.NodeId).Value
                .OuterXml, Is.EqualTo(arguments.Value.OuterXml));
            if (!compatible)
            {
                Assert.That(result.Diagnostics.Any(item =>
                    item.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    item.Location?.JsonPointer == "/actions/Reset/input"), Is.True);
            }
        }

        [TestCase(1, true, true)]
        [TestCase(1, true, false)]
        [TestCase(2, true, true)]
        [TestCase(2, true, false)]
        [TestCase(1, false, false)]
        public void NativeConditionPairingValidatesTargetsWithoutTruncatingContext(
            int budget, bool standard, bool matchingTarget)
        {
            UANodeSet source = CreateNativeConditionSource("i=21");
            UAMethod method = source.Items.OfType<UAMethod>().Single(node => node.NodeId == "ns=1;i=7001");
            string name = standard ? "Acknowledge" : "Reset";
            if (standard)
            {
                method.BrowseName = name;
                method.MethodDeclarationId = "i=9111";
            }
            JsonObject root = NativeDocument(source);
            root.Remove("properties");
            root.Remove("uav:dataTypeDefinitions");
            root["events"] = JsonNode.Parse("""{"Alarm":{"uav:id":"nsu=urn:test:model;i=90001"}}""");
            root["actions"]![name]!["uav:conditionAction"] = "Acknowledge";
            root["actions"]![name]!["uav:actsOn"] = matchingTarget ? "Alarm" : "Missing";
            var options = new WotNodeSetConverterOptions { MaxAffordanceCount = budget };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), options);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document, options);

            Assert.That(result.Success, Is.EqualTo(matchingTarget), string.Join("; ", result.Diagnostics));
            Assert.That(result.Value!.Items.OfType<UAMethod>().Single().MethodDeclarationId,
                Is.EqualTo(method.MethodDeclarationId));
            if (!matchingTarget)
            {
                Assert.That(result.Diagnostics.Any(item =>
                    item.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    item.Location?.JsonPointer == "/actions/" + name + "/uav:actsOn"), Is.True);
            }
        }

        [TestCase("uav:defaultEncodings", "[\"Binary\"]", false)]
        [TestCase("uav:defaultEncodings", "[\"JSON\",\"Binary\",\"XML\"]", true)]
        [TestCase("uav:defaultEncodingId", "\"nsu=urn:test:model;s=WrongEncoding\"", false)]
        [TestCase("uav:defaultEncodingId", "$binary", true)]
        [TestCase("uav:binaryEncodingId", "\"nsu=urn:test:model;s=WrongEncoding\"", false)]
        [TestCase("uav:xmlEncodingId", "\"nsu=urn:test:model;s=WrongEncoding\"", false)]
        [TestCase("uav:jsonEncodingId", "\"nsu=urn:test:model;s=WrongEncoding\"", false)]
        public void NativeOnlyDataTypeEncodingAssertionsCannotChangeTheEncodingSet(
            string member, string value, bool compatible)
        {
            const string model = /*lang=json,strict*/ """
                {
                  "@context": {
                    "uav":"http://opcfoundation.org/UA/WoT-Binding/",
                    "ua":"http://opcfoundation.org/UA/",
                    "model":"urn:test:model"
                  },
                  "@type":["tm:ThingModel","uav:objectType"],
                  "title":"Container",
                  "uav:id":"nsu=urn:test:model;i=1001",
                  "uav:browseName":"model:Container",
                  "uav:dataTypeDefinitions":[{
                    "@id":"urn:test:model:Payload",
                    "@type":"uav:StructureDefinition",
                    "uav:dataTypeName":"model:Payload",
                    "uav:dataTypeId":"nsu=urn:test:model;i=90001",
                    "uav:structureType":"Structure",
                    "uav:fields":[{
                      "@type":"uav:StructureField",
                      "uav:fieldName":"Value",
                      "uav:fieldDataTypeId":"i=11"
                    }]
                  }]
                }
                """;
            UANodeSet source = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(model));
            using WotDocument readable = WotNodeSetConverter.FromNodeSet(
                source, options: new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Never });
            JsonObject root = JsonNode.Parse(readable.Utf8Json.Span)!.AsObject();
            var diagnostics = new System.Collections.Generic.List<WotDiagnostic>();
            byte[] native = WotNativeProjection.Write(source, new WotNodeSetConverterOptions(), diagnostics);
            Assert.That(diagnostics, Is.Empty);
            root["uav:nodes"] = JsonNode.Parse(native);
            JsonNode definition = root["uav:dataTypeDefinitions"]![0]!;
            definition[member] = value == "$binary"
                ? definition["uav:binaryEncodingId"]!.DeepClone() : JsonNode.Parse(value);
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.EqualTo(compatible), string.Join("; ", result.Diagnostics));
            if (!compatible)
            {
                Assert.That(result.Diagnostics.Any(item =>
                    item.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    item.Location?.JsonPointer?.StartsWith("/uav:dataTypeDefinitions/0", StringComparison.Ordinal) == true &&
                    item.Location.NodeId == "ns=1;i=90001"),
                    Is.True, string.Join("; ", result.Diagnostics));
            }
            Assert.That(result.Value!.Items.Select(item => item.NodeId),
                Is.EquivalentTo(source.Items.Select(item => item.NodeId)));
        }

        [Test]
        public async Task NativeOnlyReadableSubsetsAndRoutingMetadataDoNotOverlayNativeContent()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            JsonObject root = NativeDocument(source);
            root.Remove("actions");
            root.Remove("events");
            root.Remove("links");
            root.Remove("uav:dataTypeDefinitions");
            root["properties"] = JsonNode.Parse(
                """
                {
                  "speedAlias": {
                    "uav:id":"nsu=urn:test:model;i=6001",
                    "uav:dataTypeId":"i=11",
                    "forms":[{"href":"https://routing.invalid/speed","op":"readproperty"}]
                  }
                }
                """);
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(document)
                .ConfigureAwait(false);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(result.Value!.Items.Select(item => item.NodeId),
                Is.EquivalentTo(source.Items.Select(item => item.NodeId)));
            UAVariable value = result.Value.Items.OfType<UAVariable>().Single(item => item.NodeId == "ns=1;i=6001");
            Assert.That(value.Value.OuterXml,
                Is.EqualTo(source.Items.OfType<UAVariable>().Single(item => item.NodeId == value.NodeId).Value.OuterXml));
        }

        [Test]
        public async Task LinkedNativePartitionsStillRejectContradictionsAfterContextPreparation()
        {
            var options = new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Never };
            WotConversionResult<WotDocumentSet> exported = await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                WotTestData.CreateRichNodeSet(), "model", options: options).ConfigureAwait(false);
            Assert.That(exported.Success, Is.True, string.Join("; ", exported.Diagnostics));
            using WotDocumentSet original = exported.Value!;
            var entries = new System.Collections.Generic.List<WotDocumentSetEntry>();
            int changed = 0;
            try
            {
                foreach (WotDocumentSetEntry entry in original.Entries)
                {
                    JsonObject root = JsonNode.Parse(entry.Document.Utf8Json.Span)!.AsObject();
                    if (entry.Document.TryGetNativeProjection(out _) &&
                        root["properties"] is JsonObject properties &&
                        properties["Speed"] is JsonObject speed)
                    {
                        speed["uav:dataTypeId"] = "i=12";
                        changed++;
                    }
                    entries.Add(new WotDocumentSetEntry(
                        entry.Href, WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()))));
                }
                Assert.That(changed, Is.EqualTo(1));
                using var documents = new WotDocumentSet(original.RootHref, entries.ToArrayOf());
                entries.Clear();

                WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetAsync(documents, options)
                    .ConfigureAwait(false);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(item =>
                    item.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    item.Location?.JsonPointer == "/properties/Speed/uav:dataTypeId" &&
                    item.Location.NodeId == "ns=1;i=6001"), Is.True, string.Join("; ", result.Diagnostics));
            }
            finally
            {
                foreach (WotDocumentSetEntry entry in entries)
                {
                    entry.Dispose();
                }
            }
        }

        private static UANodeSet CreateNativeConditionSource(string commentDataType)
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            UANode owner = source.Items.Single(node => node.NodeId == "ns=1;i=1001");
            UAMethod method = source.Items.OfType<UAMethod>().Single(node => node.NodeId == "ns=1;i=7001");
            var alarm = new UAObjectType
            {
                NodeId = "ns=1;i=90001",
                BrowseName = "1:Alarm",
                References = [new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=2915" }]
            };
            owner.References =
            [
                .. owner.References,
                new Reference { ReferenceType = "i=41", Value = alarm.NodeId }
            ];
            var arguments = new UAVariable
            {
                NodeId = "ns=1;i=90002",
                BrowseName = "InputArguments",
                ParentNodeId = method.NodeId,
                DataType = "i=296",
                ValueRank = 1,
                ArrayDimensions = "2",
                References =
                [
                    new Reference { ReferenceType = "i=40", Value = "i=68" },
                    new Reference { ReferenceType = "i=46", IsForward = false, Value = method.NodeId }
                ],
                Value = WotTestData.ParseValue(
                    """
                    <uax:ListOfExtensionObject xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                      <uax:ExtensionObject><uax:TypeId><uax:Identifier>i=297</uax:Identifier></uax:TypeId>
                        <uax:Body><uax:Argument><uax:Name>EventId</uax:Name>
                          <uax:DataType><uax:Identifier>i=15</uax:Identifier></uax:DataType>
                          <uax:ValueRank>-1</uax:ValueRank>
                        </uax:Argument></uax:Body>
                      </uax:ExtensionObject>
                      <uax:ExtensionObject><uax:TypeId><uax:Identifier>i=297</uax:Identifier></uax:TypeId>
                        <uax:Body><uax:Argument><uax:Name>Comment</uax:Name>
                          <uax:DataType><uax:Identifier>i=21</uax:Identifier></uax:DataType>
                          <uax:ValueRank>-1</uax:ValueRank>
                        </uax:Argument></uax:Body>
                      </uax:ExtensionObject>
                    </uax:ListOfExtensionObject>
                    """.Replace("i=21", commentDataType, StringComparison.Ordinal))
            };
            method.References = [.. method.References, new Reference { ReferenceType = "i=46", Value = arguments.NodeId }];
            source.Items = [.. source.Items, alarm, arguments];
            return source;
        }

        private static JsonObject CreateNativeRankedDocument(int rank, string dataType = "i=6")
        {
            JsonObject root = JsonNode.Parse(
                """
                {
                  "uav:nodes": {
                    "@type": "uav:NodeModel", "profileVersion": "1.0",
                    "namespaceUris": ["urn:test"],
                    "nodes": [
                      {
                        "nodeClass": "Object", "nodeId": "ns=1;i=1", "browseName": "1:R",
                        "references": [{"referenceType": "i=47", "target": "ns=1;i=2"}]
                      },
                      {
                        "nodeClass": "Variable", "nodeId": "ns=1;i=2", "browseName": "1:P",
                        "dataType": "i=6", "valueRank": 4
                      }
                    ]
                  }
                }
                """)!.AsObject();
            root["uav:nodes"]!["nodes"]![1]!["valueRank"] = rank;
            root["uav:nodes"]!["nodes"]![1]!["dataType"] = dataType;
            return root;
        }

        private static JsonObject CreateNativeEventDocument()
        {
            JsonObject root = CreateNativeRankedDocument(-1);
            root["uav:nodes"]!["nodes"]![0]!["references"]!.AsArray().Add(
                JsonNode.Parse("""{"referenceType":"i=41","target":"ns=1;i=3"}"""));
            root["uav:nodes"]!["nodes"]!.AsArray().Add(JsonNode.Parse(
                """
                {
                  "nodeClass":"ObjectType", "nodeId":"ns=1;i=3", "browseName":"1:E",
                  "references":[{"referenceType":"i=45","isForward":false,"target":"i=2041"}]
                }
                """));
            return root;
        }

        private static JsonObject NativeDocument(UANodeSet source)
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source, options: new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Never });
            Assert.That(document.TryGetEnvelope(out _), Is.False);
            Assert.That(document.TryGetNativeProjection(out _), Is.True);
            return JsonNode.Parse(document.Utf8Json.Span)!.AsObject();
        }
    }
}
