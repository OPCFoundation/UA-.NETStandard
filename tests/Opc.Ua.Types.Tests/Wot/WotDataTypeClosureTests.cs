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

using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotDataTypeClosureTests
    {
        [Test]
        public void MatchingDataTypeDefinitionPopulatesTheRootWithoutDuplicatingItsIdentity()
        {
            JsonObject root = Document(Structure("nsu=urn:test:datatype-closure;i=3000"));
            root["@type"] = new JsonArray("tm:ThingModel", "uav:dataType");
            root["uav:id"] = "nsu=urn:test:datatype-closure;i=3000";
            root["uav:browseName"] = "t:Reading";
            root["title"] = "Root title";
            root["links"] = new JsonArray(new JsonObject { ["rel"] = "ua:Organizes", ["href"] = "i=85" });
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items!.Select(node => node.NodeId), Is.Unique);
            UADataType type = result.Value.Items!.OfType<UADataType>().Single();
            Assert.That(type.NodeId, Is.EqualTo("ns=1;i=3000"));
            Assert.That(type.BrowseName, Is.EqualTo("1:Reading"));
            Assert.That(type.DisplayName![0].Value, Is.EqualTo("Root title"));
            Assert.That(type.Definition!.Field, Has.Length.EqualTo(1));
            Assert.That(type.Definition.Field![0].Name, Is.EqualTo("Value"));
            Assert.That(type.Definition.Field[0].DataType, Is.EqualTo("i=11"));
            Assert.That(type.References!.Any(reference =>
                reference.ReferenceType is "HasSubtype" or "i=45" &&
                !reference.IsForward && reference.Value == "i=22"), Is.True);
            Assert.That(type.References!.Any(reference =>
                reference.ReferenceType is "Organizes" or "i=35" &&
                reference.IsForward && reference.Value == "i=85"), Is.True);
        }

        [TestCase("uav:dataTypeId")]
        [TestCase("uav:binaryEncodingId")]
        public void DataTypeOrEncodingCannotClaimAnOrdinaryRootIdentity(string identityMember)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition[identityMember] = "nsu=urn:test:datatype-closure;i=1";
            using WotDocument document = Parse(Document(definition));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Code is WotDiagnosticCode.ValidationError or WotDiagnosticCode.DataTypeDefinitionInvalid),
                Is.True, Describe(result));
        }

        [TestCase("uav:browseName", "\"t:Other\"")]
        [TestCase("uav:isAbstract", "true")]
        public void MatchingDataTypeRootRejectsContradictoryIdentityFacts(string member, string json)
        {
            JsonObject root = Document(Structure("nsu=urn:test:datatype-closure;i=3000"));
            root["@type"] = new JsonArray("tm:ThingModel", "uav:dataType");
            root["uav:id"] = "nsu=urn:test:datatype-closure;i=3000";
            root["uav:browseName"] = "t:Reading";
            root[member] = JsonNode.Parse(json);
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False, Describe(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid), Is.True, Describe(result));
        }

        [TestCase("uav:dataTypeId")]
        [TestCase("uav:binaryEncodingId")]
        public void DataTypeOrEncodingCannotClaimAnOrdinaryAffordanceIdentity(string identityMember)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition[identityMember] = "nsu=urn:test:datatype-closure;i=2";
            JsonObject root = Document(definition);
            root["properties"] = new JsonObject
            {
                ["Existing"] = new JsonObject
                {
                    ["uav:id"] = "nsu=urn:test:datatype-closure;i=2",
                    ["type"] = "number"
                }
            };
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ValidationError &&
                diagnostic.Location?.NodeId == "ns=1;i=2"), Is.True, Describe(result));
        }

        [Test]
        public void AnEncodingCannotClaimItsDataTypesIdentity()
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition["uav:binaryEncodingId"] = "nsu=urn:test:datatype-closure;i=3000";
            using WotDocument document = Parse(Document(definition));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ValidationError &&
                diagnostic.Location?.NodeId == "ns=1;i=3000"), Is.True, Describe(result));
        }

        [TestCase("nsu=urn:test:datatype-closure;i=3000")]
        [TestCase("nsu=urn:test:datatype-closure;g=01234567-89ab-cdef-0123-456789abcdef")]
        [TestCase("nsu=urn:test:datatype-closure;b=AQIDBA==")]
        [TestCase("nsu=urn:test:datatype-closure;s=ActualReading")]
        public void EncodingBacklinksNameTheActualDataTypeIdentity(string typeId)
        {
            JsonObject root = Document(Structure(typeId));
            root.Remove("@context");
            root["uav:browseName"] = "nsu=urn:test:datatype-closure;Root";
            root["uav:dataTypeDefinitions"]![0]!["uav:dataTypeName"] = "nsu=urn:test:datatype-closure;Reading";
            using WotDocument document = Parse(root);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);
            Assert.That(result.Success, Is.True, Describe(result));
            UANodeSet source = result.Value!;
            string actualId = WotTestData.LocalNodeId(source, typeId);
            UADataType type = source.Items!.OfType<UADataType>().Single();
            Assert.That(type.NodeId, Is.EqualTo(actualId));
            UAObject[] encodings = source.Items!.OfType<UAObject>().ToArray();
            Assert.That(encodings, Has.Length.EqualTo(3));
            foreach (UAObject encoding in encodings)
            {
                Assert.That(encoding.NodeId, Is.EqualTo("ns=1;s=DataTypes/Reading/" + encoding.BrowseName));
                Assert.That(encoding.References!.Single(reference =>
                    reference.ReferenceType == "HasEncoding" && !reference.IsForward).Value, Is.EqualTo(actualId));
                Assert.That(type.References!.Any(reference =>
                    reference.ReferenceType == "HasEncoding" && reference.IsForward &&
                    reference.Value == encoding.NodeId), Is.True);
            }
            Assert.That(source.Items!.Any(node => node.NodeId == "ns=1;s=DataTypes/Reading"), Is.False);
            WotNodeSetImportTests.AssertImportable(source, "actual DataType encoding identity");
            byte[] expected = WotTestData.Serialize(source);
            using WotDocument archived = WotNodeSetConverter.FromNodeSet(
                source,
                options: new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Always });
            WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ToNodeSetResult(archived);
            Assert.That(restored.Success, Is.True, Describe(restored));
            Assert.That(WotTestData.Serialize(restored.Value!), Is.EqualTo(expected),
                "Before: " + string.Concat(source.Extensions?.Select(extension => extension.OuterXml) ?? []) +
                "; after: " + string.Concat(restored.Value!.Extensions?.Select(extension => extension.OuterXml) ?? []));
            Assert.That(WotTestData.Serialize(source), Is.EqualTo(expected));
        }

        [TestCase("nsu=urn:test:datatype-closure;i=3000")]
        [TestCase("nsu=urn:test:datatype-closure;g=01234567-89ab-cdef-0123-456789abcdef")]
        [TestCase("nsu=urn:test:datatype-closure;b=AQIDBA==")]
        public void DefaultEncodingIdUsesTheNameDerivedBinaryIdentity(string typeId)
        {
            JsonObject definition = Structure(typeId);
            definition["uav:defaultEncodingId"] = "nsu=urn:test:datatype-closure;s=DataTypes/Reading/Default Binary";
            using WotDocument document = Parse(Document(definition));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items!.OfType<UAObject>().Single(node => node.BrowseName == "Default Binary").NodeId,
                Is.EqualTo("ns=1;s=DataTypes/Reading/Default Binary"));
        }

        [TestCase("uav:xmlEncodingId")]
        [TestCase("uav:jsonEncodingId")]
        public void DefaultEncodingIdCannotSelectAnExplicitNonBinaryEncoding(string encodingTerm)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition[encodingTerm] = "nsu=urn:test:datatype-closure;i=4002";
            definition["uav:defaultEncodingId"] = "nsu=urn:test:datatype-closure;i=4002";
            using WotDocument document = Parse(Document(definition));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid), Is.True, Describe(result));
        }

        [Test]
        public void DefaultEncodingIdCanSupplyTheBinaryIdentity()
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition["uav:defaultEncodingId"] = "nsu=urn:test:datatype-closure;i=4001";
            using WotDocument document = Parse(Document(definition));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items!.OfType<UAObject>().Single(node => node.BrowseName == "Default Binary").NodeId,
                Is.EqualTo("ns=1;i=4001"));
        }

        [TestCase("nsu=urn%3Atest%3Adatatype-closure;i=04001", true)]
        [TestCase("nsu=urn:test:datatype-closure;i=4002", false)]
        public void DefaultEncodingIdAgreesWithTheNormalizedBinaryIdentity(string defaultId, bool agrees)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition["uav:binaryEncodingId"] = "nsu=urn:test:datatype-closure;i=4001";
            definition["uav:xmlEncodingId"] = "nsu=urn:test:datatype-closure;i=4002";
            definition["uav:defaultEncodingId"] = defaultId;
            using WotDocument document = Parse(Document(definition));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.EqualTo(agrees), Describe(result));
            if (agrees)
            {
                Assert.That(result.Value!.Items!.OfType<UAObject>()
                    .Single(node => node.BrowseName == "Default Binary").NodeId, Is.EqualTo("ns=1;i=4001"));
            }
        }

        [TestCase("uav:xmlEncodingId", "Default XML")]
        [TestCase("uav:jsonEncodingId", "Default JSON")]
        public void SuppressingGeneratedDefaultsPreservesExplicitNonBinaryEncodings(string term, string name)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition["uav:hasDefaultEncoding"] = false;
            definition[term] = "nsu=urn:test:datatype-closure;i=4002";
            using WotDocument document = Parse(Document(definition));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            UAObject encoding = result.Value!.Items!.OfType<UAObject>().Single();
            Assert.That(encoding.NodeId, Is.EqualTo("ns=1;i=4002"));
            Assert.That(encoding.BrowseName, Is.EqualTo(name));
            Assert.That(encoding.References!.Single(reference =>
                reference.ReferenceType == "HasEncoding" && !reference.IsForward).Value, Is.EqualTo("ns=1;i=3000"));
        }

        [Test]
        public void AnExactBinaryJsonEncodingSetStaysPartialThroughReadableRoundTrips()
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition["uav:defaultEncodings"] = new JsonArray("Binary", "JSON");
            using WotDocument authored = Parse(Document(definition));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(authored);

            for (int roundTrip = 0; roundTrip < 2; roundTrip++)
            {
                Assert.That(result.Success, Is.True, Describe(result));
                Assert.That(result.Value!.Items!.OfType<UAObject>().Select(node => node.BrowseName),
                    Is.EquivalentTo(s_binaryJsonNames));
                using WotDocument exported = WotNodeSetConverter.FromNodeSet(result.Value);
                JsonElement emitted = exported.RootElement.GetProperty("uav:dataTypeDefinitions").EnumerateArray()
                    .Single(item => item.GetProperty("uav:dataTypeId").GetString() == "nsu=urn:test:datatype-closure;i=3000");
                Assert.That(emitted.GetProperty("uav:defaultEncodings").EnumerateArray().Select(item => item.GetString()),
                    Is.EqualTo(s_binaryJsonPresence));
                Assert.That(emitted.TryGetProperty("uav:xmlEncodingId", out _), Is.False);
                JsonObject readable = JsonNode.Parse(exported.Utf8Json.Span)!.AsObject();
                readable.Remove("uav:nodes");
                readable.Remove("uav:nodeSet");
                using WotDocument imported = Parse(readable);
                result = WotNodeSetConverter.ToNodeSetResult(imported);
            }
        }

        [TestCase("[\"XML\",\"JSON\"]", false, true)]
        [TestCase("[]", false, true)]
        [TestCase("[]", true, false)]
        [TestCase("[\"XML\"]", true, false)]
        [TestCase("[\"Binary\"]", false, false)]
        [TestCase("[\"Binary\",\"Binary\"]", true, false)]
        [TestCase("[\"Binary\",\"YAML\"]", true, false)]
        [TestCase("\"Binary\"", true, false)]
        public void ExactEncodingPresenceAgreesWithTheDefaultPolicy(string json, bool hasDefault, bool valid)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition["uav:hasDefaultEncoding"] = hasDefault;
            definition["uav:defaultEncodings"] = JsonNode.Parse(json);
            using WotDocument document = Parse(Document(definition));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.EqualTo(valid), Describe(result));
            if (valid)
            {
                string[] expected = definition["uav:defaultEncodings"]!.AsArray()
                    .Select(value => "Default " + value!.GetValue<string>()).ToArray();
                Assert.That(result.Value!.Items!.OfType<UAObject>().Select(node => node.BrowseName),
                    Is.EquivalentTo(expected));
            }
            else
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid), Is.True, Describe(result));
            }
        }

        [TestCase("uav:xmlEncodingId")]
        [TestCase("uav:jsonEncodingId")]
        public void AnExplicitEncodingIdentityCannotOverrideExactAbsence(string term)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition["uav:defaultEncodings"] = new JsonArray("Binary");
            definition[term] = "nsu=urn:test:datatype-closure;i=4002";
            using WotDocument document = Parse(Document(definition));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid), Is.True, Describe(result));
        }

        [TestCase("uav:binaryEncodingId")]
        [TestCase("uav:defaultEncodingId")]
        public void ANullDefaultCannotDeclareABinaryIdentity(string term)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition["uav:hasDefaultEncoding"] = false;
            definition[term] = "nsu=urn:test:datatype-closure;i=4001";
            using WotDocument document = Parse(Document(definition));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid), Is.True, Describe(result));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OnlyAnEmptyEncodingPresenceIsAllowedOnAnAbstractStructure(bool binary)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition["uav:isAbstract"] = true;
            definition["uav:defaultEncodings"] = binary ? new JsonArray("Binary") : new JsonArray();
            using WotDocument document = Parse(Document(definition));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.EqualTo(!binary), Describe(result));
            Assert.That(result.Value!.Items!.OfType<UAObject>(), Is.Empty);
        }

        [TestCase("uav:SimpleDataType", false)]
        [TestCase("uav:SimpleDataType", true)]
        [TestCase("uav:EnumDefinition", false)]
        [TestCase("uav:EnumDefinition", true)]
        public void NonStructureKindsCannotDeclareEncodingPolicy(string kind, bool identity)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition["@type"] = kind;
            definition.Remove("uav:structureType");
            if (kind == "uav:SimpleDataType")
            {
                definition.Remove("uav:fields");
                definition["uav:dataTypeSubtypeOf"] = "i=12";
            }
            else
            {
                definition["uav:fields"] = new JsonArray(new JsonObject
                {
                    ["@type"] = "uav:EnumField",
                    ["uav:fieldName"] = "Off",
                    ["uav:value"] = 0
                });
            }
            if (identity)
            {
                definition["uav:xmlEncodingId"] = "nsu=urn:test:datatype-closure;i=4002";
            }
            else
            {
                definition["uav:defaultEncodings"] = new JsonArray();
            }
            using WotDocument document = Parse(Document(definition));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Message.Contains("encoding", System.StringComparison.OrdinalIgnoreCase)),
                Is.True, Describe(result));
        }

        [Test]
        public void ExplicitXmlDoesNotMakeANestedOnlyTypeDirectlySelectable()
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition["uav:hasDefaultEncoding"] = false;
            definition["uav:defaultEncodings"] = new JsonArray("XML");
            JsonObject root = Document(definition);
            root["properties"] = new JsonObject
            {
                ["Value"] = new JsonObject
                {
                    ["type"] = "object",
                    ["uav:mapToType"] = "nsu=urn:test:datatype-closure;i=3000"
                }
            };
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value!.Items!.OfType<UAObject>().Single().BrowseName, Is.EqualTo("Default XML"));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Message.Contains("DefaultEncodingId", System.StringComparison.Ordinal)),
                Is.True, Describe(result));
        }

        [Test]
        public void PartialEncodingSetsRemainExactAndImportableWithAnArchive()
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition["uav:dataTypeName"] = "nsu=urn:test:datatype-closure;Reading";
            definition["uav:defaultEncodings"] = new JsonArray("Binary", "JSON");
            definition["uav:binaryEncodingId"] = "nsu=urn:test:datatype-closure;i=4001";
            definition["uav:jsonEncodingId"] = "nsu=urn:test:datatype-closure;i=4003";
            JsonObject root = Document(definition);
            root.Remove("@context");
            root["uav:browseName"] = "nsu=urn:test:datatype-closure;Root";
            using WotDocument authored = Parse(root);
            WotConversionResult<UANodeSet> source = WotNodeSetConverter.ToNodeSetResult(authored);
            Assert.That(source.Success, Is.True, Describe(source));
            WotNodeSetImportTests.AssertImportable(source.Value!, "partial Binary/JSON encoding set");
            byte[] expected = WotTestData.Serialize(source.Value!);

            using WotDocument archived = WotNodeSetConverter.FromNodeSet(
                source.Value!,
                options: new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Always });
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(archived);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(WotTestData.Serialize(result.Value!), Is.EqualTo(expected));
            Assert.That(result.Value!.Items!.OfType<UAObject>().Select(node => node.BrowseName),
                Is.EquivalentTo(s_binaryJsonNames));
            WotNodeSetImportTests.AssertImportable(result.Value!, "archived partial Binary/JSON encoding set");
        }

        private static JsonObject Structure(string typeId)
        {
            return new JsonObject
            {
                ["@id"] = "urn:dtd:Reading",
                ["@type"] = "uav:StructureDefinition",
                ["uav:dataTypeName"] = "t:Reading",
                ["uav:dataTypeId"] = typeId,
                ["uav:structureType"] = "Structure",
                ["uav:fields"] = new JsonArray(new JsonObject
                {
                    ["@type"] = "uav:StructureField",
                    ["uav:fieldName"] = "Value",
                    ["uav:fieldDataTypeId"] = "i=11"
                })
            };
        }

        private static JsonObject Document(JsonObject definition)
        {
            return new JsonObject
            {
                ["@context"] = new JsonObject { ["t"] = "urn:test:datatype-closure" },
                ["@type"] = new JsonArray("tm:ThingModel", "uav:objectType"),
                ["uav:id"] = "nsu=urn:test:datatype-closure;i=1",
                ["uav:browseName"] = "t:Root",
                ["uav:dataTypeDefinitions"] = new JsonArray(definition)
            };
        }

        private static WotDocument Parse(JsonObject root)
        {
            return WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));
        }

        private static string Describe(WotConversionResult<UANodeSet> result)
        {
            return string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
        }

        private static readonly string[] s_binaryJsonNames = ["Default Binary", "Default JSON"];
        private static readonly string[] s_binaryJsonPresence = ["Binary", "JSON"];
    }
}
