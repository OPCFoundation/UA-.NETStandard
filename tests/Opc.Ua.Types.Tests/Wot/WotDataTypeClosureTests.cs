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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Moq;
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
                "; after: " +
                string.Concat(restored.Value!.Extensions?.Select(extension => extension.OuterXml) ?? []));
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
            Assert.That(result.Value!.Items!.OfType<UAObject>()
                .Single(node => node.BrowseName == "Default Binary").NodeId,
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
            Assert.That(result.Value!.Items!.OfType<UAObject>()
                .Single(node => node.BrowseName == "Default Binary").NodeId,
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
                    .Single(item =>
                        item.GetProperty("uav:dataTypeId").GetString() == "nsu=urn:test:datatype-closure;i=3000");
                Assert.That(emitted.GetProperty("uav:defaultEncodings")
                    .EnumerateArray().Select(item => item.GetString()),
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
                diagnostic.Message.Contains("encoding", StringComparison.OrdinalIgnoreCase)),
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
                diagnostic.Message.Contains("DefaultEncodingId", StringComparison.Ordinal)),
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

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task SiblingDataTypeReferencesUseTheirDefinitionsOwnContextAsync(bool available, bool composite)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition["@context"] = new JsonObject { ["t"] = "urn:test:datatype-closure" };
            JsonObject source = Document(definition);
            source["@context"] = new JsonObject { ["t"] = "urn:wrong:source-root" };
            source["uav:browseName"] = "nsu=urn:test:datatype-closure;Definitions";
            using WotDocument definingDocument = Parse(source);

            JsonObject consumer = Document(new JsonObject { ["@id"] = "urn:dtd:Reading" });
            consumer.Remove("uav:dataTypeDefinitions");
            consumer["@context"] = new JsonObject { ["t"] = "urn:wrong:consumer" };
            consumer["uav:id"] = "nsu=urn:test:datatype-closure;i=2";
            consumer["uav:browseName"] = "nsu=urn:test:datatype-closure;Consumer";
            consumer["properties"] = new JsonObject
            {
                ["Payload"] = new JsonObject
                {
                    ["type"] = "object",
                    ["uav:dataTypeDefinition"] = new JsonObject { ["@id"] = "urn:dtd:Reading" }
                }
            };
            using WotDocument document = Parse(consumer);
            byte[] sourceBytes = definingDocument.Utf8Json.ToArray();
            byte[] consumerBytes = document.Utf8Json.ToArray();
            IWotNodeResolver resolver = new WotDocumentNodeResolver(
                available ? new[] { definingDocument, document } : [document]);
            if (composite)
            {
                resolver = new WotCompositeNodeResolver(resolver, NullWotNodeResolver.Instance);
            }

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, resolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.EqualTo(available), Describe(result));
            if (available)
            {
                UANodeSet nodeSet = result.Value!;
                string expectedId = WotTestData.LocalNodeId(nodeSet, "nsu=urn:test:datatype-closure;i=3000");
                Assert.That(nodeSet.Items!.OfType<UAVariable>().Single().DataType, Is.EqualTo(expectedId));
                UADataType type = nodeSet.Items!.OfType<UADataType>().Single();
                Assert.That(type.NodeId, Is.EqualTo(expectedId));
                QualifiedName name = QualifiedName.Parse(type.BrowseName!);
                Assert.That(name.Name, Is.EqualTo("Reading"));
                Assert.That(nodeSet.NamespaceUris![name.NamespaceIndex - 1], Is.EqualTo("urn:test:datatype-closure"));
                Assert.That(nodeSet.Items!.OfType<UAObject>().All(encoding =>
                    encoding.NodeId == "ns=1;s=DataTypes/Reading/" + encoding.BrowseName), Is.True);
                WotNodeSetImportTests.AssertImportable(nodeSet, "sibling DTD closure");
            }
            else
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid), Is.True, Describe(result));
            }
            Assert.That(definingDocument.Utf8Json.ToArray(), Is.EqualTo(sourceBytes));
            Assert.That(document.Utf8Json.ToArray(), Is.EqualTo(consumerBytes));
        }

        [Test]
        public async Task DocumentSetsResolveSharedDataTypeDefinitionsOnceAsync()
        {
            JsonObject consumer = Consumer("urn:dtd:Reading");
            using var documents = new WotDocumentSet("consumer", new ArrayOf<WotDocumentSetEntry>(
            [
                new("definitions", Parse(Document(Structure("nsu=urn:test:datatype-closure;i=3000")))),
                new("consumer", Parse(consumer))
            ]));

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetAsync(documents).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items!.Select(node => node.NodeId), Is.Unique);
            Assert.That(result.Value.Items!.OfType<UADataType>().ToArray(), Has.Length.EqualTo(1));
            Assert.That(result.Value.Items!.OfType<UAVariable>().Single().DataType, Is.EqualTo("ns=1;i=3000"));
            WotNodeSetImportTests.AssertImportable(result.Value, "document-set DTD closure");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DataTypeDefinitionClosureIncludesTransitiveFieldReferencesAsync(bool nestedAvailable)
        {
            JsonObject outer = Structure("nsu=urn:test:datatype-closure;i=3000");
            outer["uav:fields"]![0]!.AsObject().Remove("uav:fieldDataTypeId");
            outer["uav:fields"]![0]!["uav:fieldDataTypeDefinition"] = new JsonObject { ["@id"] = "urn:dtd:Nested" };
            JsonObject nested = Structure("nsu=urn:test:datatype-closure;i=3001");
            nested["@id"] = "urn:dtd:Nested";
            nested["uav:dataTypeName"] = "t:Nested";
            JsonObject nestedRoot = Document(nested);
            nestedRoot["uav:id"] = "nsu=urn:test:datatype-closure;i=3";
            nestedRoot["uav:browseName"] = "t:NestedDefinitions";
            using WotDocument outerDocument = Parse(Document(outer));
            using WotDocument nestedDocument = Parse(nestedRoot);
            using WotDocument consumer = Parse(Consumer("urn:dtd:Reading"));
            var resolver = new WotDocumentNodeResolver(nestedAvailable
                ? new[] { outerDocument, nestedDocument, consumer }
                : [outerDocument, consumer]);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, null, null, resolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.EqualTo(nestedAvailable), Describe(result));
            if (nestedAvailable)
            {
                UADataType[] types = result.Value!.Items!.OfType<UADataType>().ToArray();
                Assert.That(types, Has.Length.EqualTo(2));
                Assert.That(types.Single(type => type.NodeId == "ns=1;i=3000").Definition!.Field![0].DataType,
                    Is.EqualTo("ns=1;i=3001"));
                WotNodeSetImportTests.AssertImportable(result.Value, "transitive DTD closure");
            }
        }

        [Test]
        public async Task ConflictingSiblingDefinitionOwnersAreRejectedAsync()
        {
            using WotDocument first = Parse(Document(Structure("nsu=urn:test:datatype-closure;i=3000")));
            JsonObject secondRoot = Document(Structure("nsu=urn:test:datatype-closure;i=3001"));
            secondRoot["uav:id"] = "nsu=urn:test:datatype-closure;i=3";
            secondRoot["uav:browseName"] = "t:OtherDefinitions";
            using WotDocument second = Parse(secondRoot);
            using WotDocument consumer = Parse(Consumer("urn:dtd:Reading"));
            var resolver = new WotDocumentNodeResolver([first, second, consumer]);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, null, null, resolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Location?.Reference == "urn:dtd:Reading"), Is.True, Describe(result));
        }

        [TestCase("ua:Double", "i=11")]
        [TestCase("standard:Double", "i=11")]
        [TestCase("nsu=http://opcfoundation.org/UA/;Double", "i=11")]
        [TestCase("ua:String", "i=12")]
        public void NameOnlyStandardFieldsBindToTheActualDataType(string name, string expected)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            JsonObject field = definition["uav:fields"]![0]!.AsObject();
            field.Remove("uav:fieldDataTypeId");
            field["uav:fieldDataTypeName"] = name;
            field["@context"] = new JsonObject { ["standard"] = "http://opcfoundation.org/UA/" };
            JsonObject root = Consumer("urn:dtd:Reading");
            root["@context"]!["standard"] = "urn:wrong:root";
            root["uav:dataTypeDefinitions"] = new JsonArray(definition);
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            UANodeSet nodeSet = result.Value!;
            Assert.That(nodeSet.Items!.OfType<UAVariable>().Single().DataType, Is.EqualTo("ns=1;i=3000"));
            UADataType type = nodeSet.Items!.OfType<UADataType>().Single();
            Assert.That(type.NodeId, Is.EqualTo("ns=1;i=3000"));
            Assert.That(type.Definition!.Field, Has.Length.EqualTo(1));
            Assert.That(type.Definition.Field![0].Name, Is.EqualTo("Value"));
            Assert.That(type.Definition.Field[0].DataType, Is.EqualTo(expected));
            Assert.That(type.References!.Single(reference =>
                reference.ReferenceType == "HasSubtype" && !reference.IsForward).Value, Is.EqualTo("i=22"));
            WotNodeSetImportTests.AssertImportable(nodeSet, "qualified standard field DataType");
        }

        [Test]
        public void NamedPropertySchemasUseTheirSharedAllocatedDataType()
        {
            JsonObject root = Consumer("urn:dtd:Reading");
            root["properties"] = new JsonObject
            {
                ["First"] = InferredReading(),
                ["Second"] = InferredReading()
            };
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            UANodeSet nodeSet = result.Value!;
            UAVariable[] consumers = nodeSet.Items!.OfType<UAVariable>().ToArray();
            Assert.That(consumers, Has.Length.EqualTo(2));
            Assert.That(consumers.Select(consumer => consumer.DataType),
                Is.All.EqualTo("ns=1;s=DataTypes/Reading"));
            UADataType type = nodeSet.Items!.OfType<UADataType>().Single();
            Assert.That(type.NodeId, Is.EqualTo("ns=1;s=DataTypes/Reading"));
            Assert.That(type.Definition!.Field, Has.Length.EqualTo(1));
            Assert.That(type.Definition.Field![0].Name, Is.EqualTo("Value"));
            Assert.That(type.Definition.Field[0].DataType, Is.EqualTo("i=1"));
            Assert.That(type.Definition.Field[0].IsOptional, Is.False);
            Assert.That(type.Definition.Field[0].ValueRank, Is.EqualTo(-1));
            Assert.That(nodeSet.Items!.Select(node => node.NodeId), Is.Unique);
            WotNodeSetImportTests.AssertImportable(nodeSet, "shared inferred property DataType");
        }

        [TestCase("input")]
        [TestCase("output")]
        [TestCase("event")]
        public void NamedActionAndEventSchemasBindTheirMaterializedConsumers(string location)
        {
            JsonObject root = Consumer("urn:dtd:Reading");
            root.Remove("properties");
            if (location == "event")
            {
                root["events"] = new JsonObject
                {
                    ["Changed"] = new JsonObject
                    {
                        ["data"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject { ["Payload"] = InferredReading() }
                        }
                    }
                };
            }
            else
            {
                root["actions"] = new JsonObject
                {
                    ["Exchange"] = new JsonObject { [location] = InferredReading() }
                };
            }
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            UANodeSet nodeSet = result.Value!;
            UAVariable consumer = nodeSet.Items!.OfType<UAVariable>().Single();
            if (location == "event")
            {
                Assert.That(consumer.DataType, Is.EqualTo("ns=1;s=DataTypes/Reading"));
                Assert.That(consumer.References!.Single(reference =>
                    reference.ReferenceType == "HasProperty" && !reference.IsForward).Value,
                    Is.EqualTo(consumer.ParentNodeId));
                Assert.That(nodeSet.Items!.OfType<UAObjectType>().Single(node =>
                    node.NodeId == consumer.ParentNodeId).References!.Any(reference =>
                        reference.ReferenceType == "HasProperty" && reference.IsForward &&
                        reference.Value == consumer.NodeId), Is.True);
            }
            else
            {
                XNamespace ua = "http://opcfoundation.org/UA/2008/02/Types.xsd";
                XElement argument = XElement.Parse(consumer.Value!.OuterXml).Descendants(ua + "Argument").Single();
                Assert.That(argument.Element(ua + "DataType")!.Element(ua + "Identifier")!.Value,
                    Is.EqualTo("ns=1;s=DataTypes/Reading"));
                UAMethod method = nodeSet.Items!.OfType<UAMethod>().Single();
                Assert.That(consumer.ParentNodeId, Is.EqualTo(method.NodeId));
                Assert.That(consumer.BrowseName,
                    Is.EqualTo(location == "input" ? "InputArguments" : "OutputArguments"));
                Assert.That(method.References!.Any(reference =>
                    reference.ReferenceType == "HasProperty" && reference.IsForward &&
                    reference.Value == consumer.NodeId), Is.True);
            }
            UADataType type = nodeSet.Items!.OfType<UADataType>().Single();
            Assert.That(type.NodeId, Is.EqualTo("ns=1;s=DataTypes/Reading"));
            Assert.That(type.Definition!.Field, Has.Length.EqualTo(1));
            Assert.That(type.Definition.Field![0].Name, Is.EqualTo("Value"));
            Assert.That(type.Definition.Field[0].DataType, Is.EqualTo("i=1"));
            Assert.That(type.Definition.Field[0].IsOptional, Is.False);
            WotNodeSetImportTests.AssertImportable(nodeSet, "inferred " + location + " DataType");
        }

        [Test]
        public void KnownLocalDataTypeNamesBindFieldsAndConsumersToTheDeclaredIdentity()
        {
            JsonObject reading = Structure("nsu=urn:test:datatype-closure;i=3000");
            JsonObject envelope = Structure("nsu=urn:test:datatype-closure;i=3001");
            envelope["@id"] = "urn:dtd:Envelope";
            envelope["uav:dataTypeName"] = "t:Envelope";
            JsonObject field = envelope["uav:fields"]![0]!.AsObject();
            field.Remove("uav:fieldDataTypeId");
            field["uav:fieldDataTypeName"] = "t:Reading";
            JsonObject schema = new() { ["uav:dataTypeName"] = "t:Reading", ["type"] = "object" };
            JsonObject root = Consumer("urn:dtd:Reading");
            root["uav:dataTypeDefinitions"] = new JsonArray(envelope, reading);
            root["properties"]!["Payload"] = schema.DeepClone();
            root["actions"] = new JsonObject
            {
                ["Exchange"] = new JsonObject { ["input"] = schema.DeepClone(), ["output"] = schema.DeepClone() }
            };
            root["events"] = new JsonObject
            {
                ["Changed"] = new JsonObject
                {
                    ["data"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["Payload"] = schema.DeepClone() }
                    }
                }
            };
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            UANodeSet nodeSet = result.Value!;
            UADataType[] types = nodeSet.Items!.OfType<UADataType>().ToArray();
            Assert.That(types, Has.Length.EqualTo(2));
            Assert.That(types.Single(type => type.NodeId == "ns=1;i=3001").Definition!.Field![0].DataType,
                Is.EqualTo("ns=1;i=3000"));
            UAVariable[] variables = nodeSet.Items!.OfType<UAVariable>().ToArray();
            Assert.That(variables.Where(variable => variable.DataType != "i=296")
                .Select(variable => variable.DataType), Is.All.EqualTo("ns=1;i=3000"));
            XNamespace ua = "http://opcfoundation.org/UA/2008/02/Types.xsd";
            XElement[] arguments = variables.Where(variable => variable.DataType == "i=296")
                .Select(variable => XElement.Parse(variable.Value!.OuterXml)
                    .Descendants(ua + "Argument").Single()).ToArray();
            Assert.That(arguments, Has.Length.EqualTo(2));
            Assert.That(arguments.Select(argument => argument.Element(ua + "DataType")!
                .Element(ua + "Identifier")!.Value), Is.All.EqualTo("ns=1;i=3000"));
            Assert.That(nodeSet.Items!.Any(node => node.NodeId == "ns=1;s=DataTypes/Reading"), Is.False);
            WotNodeSetImportTests.AssertImportable(nodeSet, "declared DataType name consumers");
        }

        [Test]
        public async Task TypedDefinitionCallbacksRetainTransitiveOwnersAndTheirLexicalContextsAsync()
        {
            JsonObject reading = Structure("nsu=urn:test:datatype-closure;i=3000");
            reading["@context"] = new JsonObject { ["t"] = "urn:test:datatype-closure" };
            JsonObject field = reading["uav:fields"]![0]!.AsObject();
            field.Remove("uav:fieldDataTypeId");
            field["uav:fieldDataTypeDefinition"] = new JsonObject { ["@id"] = "urn:dtd:Nested" };
            JsonObject source = Document(reading);
            source["@context"]!["t"] = "urn:wrong:source";
            source["base"] = "https://example.test/definitions/";
            using WotDocument readingDocument = Parse(source);
            JsonObject nested = Structure("nsu=urn:test:datatype-closure;i=3001");
            nested["@id"] = "urn:dtd:Nested";
            nested["uav:dataTypeName"] = "t:Nested";
            using WotDocument nestedDocument = Parse(Document(nested));
            using WotDocument consumer = Parse(Consumer("urn:dtd:Reading"));
            byte[] originalSource = readingDocument.Utf8Json.ToArray();
            var resolver = new Mock<IWotNodeResolver>();
            Mock<IWotDataTypeDefinitionResolver> definitions = resolver.As<IWotDataTypeDefinitionResolver>();
            definitions.Setup(provider => provider.ResolveDataTypeDefinitionsAsync(
                "urn:dtd:Reading", It.IsAny<CancellationToken>())).Returns(
                    new ValueTask<ArrayOf<WotDataTypeDefinitionSource>>(new ArrayOf<WotDataTypeDefinitionSource>(
                    [
                        new(readingDocument, readingDocument.RootElement.GetProperty("uav:dataTypeDefinitions")[0])
                    ])));
            definitions.Setup(provider => provider.ResolveDataTypeDefinitionsAsync(
                "urn:dtd:Nested", It.IsAny<CancellationToken>())).Returns(
                    new ValueTask<ArrayOf<WotDataTypeDefinitionSource>>(new ArrayOf<WotDataTypeDefinitionSource>(
                    [
                        new(nestedDocument, nestedDocument.RootElement.GetProperty("uav:dataTypeDefinitions")[0])
                    ])));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, null, null, resolver.Object).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            UANodeSet nodeSet = result.Value!;
            Assert.That(nodeSet.Items!.OfType<UAVariable>().Single().DataType, Is.EqualTo("ns=1;i=3000"));
            UADataType[] types = nodeSet.Items!.OfType<UADataType>().ToArray();
            UADataType outerType = types.Single(type => type.NodeId == "ns=1;i=3000");
            Assert.That(outerType.BrowseName, Is.EqualTo("1:Reading"));
            Assert.That(outerType.Definition!.Name, Is.EqualTo("1:Reading"));
            Assert.That(outerType.Definition.Field![0].DataType, Is.EqualTo("ns=1;i=3001"));
            UADataType innerType = types.Single(type => type.NodeId == "ns=1;i=3001");
            Assert.That(innerType.Definition!.Name, Is.EqualTo("1:Nested"));
            Assert.That(innerType.Definition.Field![0].DataType, Is.EqualTo("i=11"));
            Assert.That(readingDocument.Utf8Json.ToArray(), Is.EqualTo(originalSource));
            WotNodeSetImportTests.AssertImportable(nodeSet, "typed transitive DTD callback");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LoadedDataTypeNamesBindFieldsAndArgumentsWithoutInventingTypesAsync(bool sibling)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            JsonObject field = definition["uav:fields"]![0]!.AsObject();
            field.Remove("uav:fieldDataTypeId");
            field["uav:fieldDataTypeName"] = "loaded:Temperature";
            JsonObject root = Document(definition);
            root["@context"]!["loaded"] = "urn:test:loaded";
            root["actions"] = new JsonObject
            {
                ["Set"] = new JsonObject
                {
                    ["input"] = new JsonObject { ["type"] = "number", ["uav:dataTypeName"] = "loaded:Temperature" }
                }
            };
            using WotDocument document = Parse(root);
            var resolver = new Mock<IWotNodeResolver>();
            resolver.Setup(provider => provider.HoldsNamespaceAsync(
                "urn:test:loaded", It.IsAny<CancellationToken>())).Returns(new ValueTask<bool>(true));
            resolver.Setup(provider => provider.ResolveByBrowseNameAsync(
                "urn:test:loaded", "Temperature", WotExpectedNodeClass.DataType,
                It.IsAny<CancellationToken>())).Returns(
                    new ValueTask<ArrayOf<WotResolvedNode>>(new ArrayOf<WotResolvedNode>(
                    [
                        new("nsu=urn:test:loaded;i=5000", WotExpectedNodeClass.DataType)
                    ])));
            JsonObject loadedType = Structure("nsu=urn:test:loaded;i=5000");
            loadedType["@id"] = "urn:dtd:Temperature";
            loadedType["@type"] = "uav:SimpleDataType";
            loadedType["uav:dataTypeName"] = "loaded:Temperature";
            loadedType["uav:dataTypeSubtypeOf"] = "i=11";
            loadedType.Remove("uav:structureType");
            loadedType.Remove("uav:fields");
            JsonObject loadedRoot = Document(loadedType);
            loadedRoot["@context"]!["loaded"] = "urn:test:loaded";
            loadedRoot["uav:id"] = "nsu=urn:test:loaded;i=1";
            loadedRoot["uav:browseName"] = "loaded:Definitions";
            using WotDocument loadedDocument = Parse(loadedRoot);
            IWotNodeResolver nodeResolver = sibling
                ? new WotDocumentNodeResolver([loadedDocument])
                : resolver.Object;

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, nodeResolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            UANodeSet nodeSet = result.Value!;
            Assert.That(nodeSet.NamespaceUris![1], Is.EqualTo("urn:test:loaded"));
            UADataType type = nodeSet.Items!.OfType<UADataType>().Single();
            Assert.That(type.NodeId, Is.EqualTo("ns=1;i=3000"));
            Assert.That(type.Definition!.Field![0].DataType, Is.EqualTo("ns=2;i=5000"));
            UAVariable input = nodeSet.Items!.OfType<UAVariable>().Single();
            XNamespace ua = "http://opcfoundation.org/UA/2008/02/Types.xsd";
            XElement argument = XElement.Parse(input.Value!.OuterXml).Descendants(ua + "Argument").Single();
            Assert.That(argument.Element(ua + "DataType")!.Element(ua + "Identifier")!.Value,
                Is.EqualTo("ns=2;i=5000"));
            Assert.That(nodeSet.Items!.Any(node => node.NodeId == "ns=2;s=DataTypes/Temperature"), Is.False);
            WotNodeSetImportTests.AssertImportable(nodeSet, "loaded DataType name callback");
        }

        [TestCase("ua:MissingDataType")]
        [TestCase("ua:HasComponent")]
        [TestCase("t:MissingDataType")]
        public void UnresolvedFieldNamesNeverBecomePhantomDataTypeIdentities(string name)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            JsonObject field = definition["uav:fields"]![0]!.AsObject();
            field.Remove("uav:fieldDataTypeId");
            field["uav:fieldDataTypeName"] = name;
            using WotDocument document = Parse(Document(definition));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False, Describe(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Location?.Reference == name), Is.True, Describe(result));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DocumentSetConsumersShareOnlyMatchingInferredTypeContractsAsync(bool conflicting)
        {
            JsonObject first = Consumer("urn:dtd:Reading");
            first["properties"]!["Payload"] = InferredReading();
            JsonObject second = Consumer("urn:dtd:Reading");
            second["uav:id"] = "nsu=urn:test:datatype-closure;i=3";
            second["uav:browseName"] = "t:Second";
            second.Remove("properties");
            JsonObject input = InferredReading();
            if (conflicting)
            {
                input["properties"]!["Value"]!["type"] = "string";
            }
            second["actions"] = new JsonObject { ["Exchange"] = new JsonObject { ["input"] = input } };
            using var documents = new WotDocumentSet("first", new ArrayOf<WotDocumentSetEntry>(
            [
                new("first", Parse(first)),
                new("second", Parse(second))
            ]));

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetAsync(documents).ConfigureAwait(false);

            Assert.That(result.Success, Is.EqualTo(!conflicting), Describe(result));
            if (conflicting)
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid), Is.True, Describe(result));
                return;
            }
            UANodeSet nodeSet = result.Value!;
            Assert.That(nodeSet.Items!.Select(node => node.NodeId), Is.Unique);
            UADataType type = nodeSet.Items!.OfType<UADataType>().Single();
            Assert.That(type.NodeId, Is.EqualTo("ns=1;s=DataTypes/Reading"));
            Assert.That(type.Definition!.Field![0].Name, Is.EqualTo("Value"));
            Assert.That(type.Definition.Field[0].DataType, Is.EqualTo("i=1"));
            UAVariable property = nodeSet.Items!.OfType<UAVariable>().Single(variable => variable.DataType != "i=296");
            Assert.That(property.DataType, Is.EqualTo("ns=1;s=DataTypes/Reading"));
            UAVariable arguments = nodeSet.Items!.OfType<UAVariable>()
                .Single(variable => variable.DataType == "i=296");
            XNamespace ua = "http://opcfoundation.org/UA/2008/02/Types.xsd";
            XElement argument = XElement.Parse(arguments.Value!.OuterXml).Descendants(ua + "Argument").Single();
            Assert.That(argument.Element(ua + "DataType")!.Element(ua + "Identifier")!.Value,
                Is.EqualTo("ns=1;s=DataTypes/Reading"));
            WotNodeSetImportTests.AssertImportable(nodeSet, "closure-wide inferred datatype allocation");
        }

        [Test]
        public void InferredDataTypesFillTheirLiveRootAndRetainPartialEncodingPresence()
        {
            JsonObject root = InferredReading();
            root["@context"] = new JsonObject { ["t"] = "urn:test:datatype-closure" };
            root["@type"] = new JsonArray("tm:ThingModel", "uav:dataType");
            root["uav:id"] = "nsu=urn:test:datatype-closure;s=DataTypes/Reading";
            root["uav:browseName"] = "t:Reading";
            root["title"] = "Root title";
            root["uav:defaultEncodings"] = new JsonArray("Binary", "JSON");
            root["links"] = new JsonArray(new JsonObject { ["rel"] = "ua:Organizes", ["href"] = "i=85" });
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            UANodeSet nodeSet = result.Value!;
            Assert.That(nodeSet.Items!.Select(node => node.NodeId), Is.Unique);
            UADataType type = nodeSet.Items!.OfType<UADataType>().Single();
            Assert.That(type.NodeId, Is.EqualTo("ns=1;s=DataTypes/Reading"));
            Assert.That(type.DisplayName![0].Value, Is.EqualTo("Root title"));
            Assert.That(type.Definition!.Field![0].Name, Is.EqualTo("Value"));
            Assert.That(type.Definition.Field[0].DataType, Is.EqualTo("i=1"));
            Assert.That(type.References!.Single(reference =>
                reference.ReferenceType == "HasSubtype" && !reference.IsForward).Value, Is.EqualTo("i=22"));
            Assert.That(type.References!.Any(reference =>
                reference.ReferenceType is "Organizes" or "i=35" &&
                reference.IsForward && reference.Value == "i=85"), Is.True);
            UAObject[] encodings = nodeSet.Items!.OfType<UAObject>().ToArray();
            Assert.That(encodings.Select(encoding => encoding.BrowseName), Is.EquivalentTo(s_binaryJsonNames));
            foreach (UAObject encoding in encodings)
            {
                Assert.That(encoding.References!.Single(reference =>
                    reference.ReferenceType == "HasEncoding" && !reference.IsForward).Value,
                    Is.EqualTo("ns=1;s=DataTypes/Reading"));
                Assert.That(type.References!.Any(reference =>
                    reference.ReferenceType == "HasEncoding" && reference.IsForward &&
                    reference.Value == encoding.NodeId), Is.True);
            }
            WotNodeSetImportTests.AssertImportable(nodeSet, "inferred live DataType root");
        }

        [TestCase("nsu=urn:test:datatype-closure;i=3000")]
        [TestCase("nsu=urn:test:datatype-closure;s=DataTypes/Reading/Default Binary")]
        public async Task SharedTypeAllocationsCannotBeHiddenByAnotherDocumentsOrdinaryRootAsync(string identity)
        {
            JsonObject first = Consumer("urn:dtd:Reading");
            first["uav:dataTypeDefinitions"] = new JsonArray(Structure("nsu=urn:test:datatype-closure;i=3000"));
            JsonObject second = Consumer("urn:dtd:Reading");
            second.Remove("properties");
            second["uav:id"] = identity;
            second["uav:browseName"] = "t:Unrelated";
            using var documents = new WotDocumentSet("first", new ArrayOf<WotDocumentSetEntry>(
            [
                new("first", Parse(first)),
                new("second", Parse(second))
            ]));

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetAsync(documents).ConfigureAwait(false);

            Assert.That(result.Success, Is.False, Describe(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code is WotDiagnosticCode.DataTypeDefinitionInvalid or WotDiagnosticCode.ValidationError),
                Is.True, Describe(result));
        }

        [TestCase("\"ua:Double\"")]
        [TestCase("\"standard:Double\"")]
        [TestCase("\"nsu=http://opcfoundation.org/UA/;Double\"")]
        [TestCase("{\"uav:dataTypeName\":\"standard:Double\"}")]
        public void QualifiedBaseTypeNamesResolveInTheirCarryingDefinition(string baseReference)
        {
            JsonObject definition = Structure("nsu=urn:test:datatype-closure;i=3000");
            definition["@type"] = "uav:SimpleDataType";
            definition["@context"] = new JsonObject { ["standard"] = "http://opcfoundation.org/UA/" };
            definition.Remove("uav:structureType");
            definition.Remove("uav:fields");
            definition["uav:dataTypeSubtypeOf"] = JsonNode.Parse(baseReference);
            JsonObject root = Consumer("urn:dtd:Reading");
            root["@context"]!["standard"] = "urn:wrong:root";
            root["properties"]!["Payload"]!["type"] = "number";
            root["uav:dataTypeDefinitions"] = new JsonArray(definition);
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            UADataType type = result.Value!.Items!.OfType<UADataType>().Single();
            Assert.That(type.NodeId, Is.EqualTo("ns=1;i=3000"));
            Assert.That(type.References!.Single(reference =>
                reference.ReferenceType == "HasSubtype" && !reference.IsForward).Value, Is.EqualTo("i=11"));
            Assert.That(result.Value.Items!.OfType<UAVariable>().Single().DataType, Is.EqualTo("ns=1;i=3000"));
            Assert.That(result.Value.Items!.OfType<UAObject>(), Is.Empty);
            WotNodeSetImportTests.AssertImportable(result.Value, "qualified SimpleDataType base");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NamedArraySchemasBindTheConsumerToTheirInferredElementType(bool nameOnArray)
        {
            JsonObject element = InferredReading();
            JsonObject schema = new()
            {
                ["type"] = "array",
                ["items"] = element,
                ["uav:valueRank"] = 1,
                ["uav:arrayDimensions"] = new JsonArray(2)
            };
            if (nameOnArray)
            {
                element.Remove("uav:dataTypeName");
                schema["uav:dataTypeName"] = "t:Reading";
            }
            JsonObject root = Consumer("urn:dtd:Reading");
            root["properties"]!["Payload"] = schema;
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            UAVariable consumer = result.Value!.Items!.OfType<UAVariable>().Single();
            Assert.That(consumer.DataType, Is.EqualTo("ns=1;s=DataTypes/Reading"));
            Assert.That(consumer.ValueRank, Is.EqualTo(1));
            Assert.That(consumer.ArrayDimensions, Is.EqualTo("2"));
            UADataType type = result.Value.Items!.OfType<UADataType>().Single();
            Assert.That(type.NodeId, Is.EqualTo("ns=1;s=DataTypes/Reading"));
            Assert.That(type.Definition!.Field![0].Name, Is.EqualTo("Value"));
            Assert.That(type.Definition.Field[0].DataType, Is.EqualTo("i=1"));
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.UnsupportedSchema),
                Is.False, Describe(result));
            WotNodeSetImportTests.AssertImportable(result.Value, "named array element DataType");
        }

        [TestCase("object")]
        [TestCase("array")]
        public void AnIncompleteNamedSchemaCannotLeaveItsConsumerBoundToAnUnemittedType(string schemaType)
        {
            JsonObject root = Consumer("urn:dtd:Reading");
            root["properties"]!["Payload"] = new JsonObject
            {
                ["uav:dataTypeName"] = "t:Unknown",
                ["type"] = schemaType
            };
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False, Describe(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Location?.Reference == "t:Unknown"), Is.True, Describe(result));
        }

        [Test]
        public void InferredConsumersCannotUseAContradictoryDefaultEncodingIdentity()
        {
            JsonObject schema = InferredReading();
            schema["uav:defaultEncodings"] = new JsonArray("Binary", "JSON");
            schema["uav:binaryEncodingId"] = "nsu=urn:test:datatype-closure;i=4001";
            schema["uav:jsonEncodingId"] = "nsu=urn:test:datatype-closure;i=4002";
            schema["uav:defaultEncodingId"] = "nsu=urn:test:datatype-closure;i=4002";
            JsonObject root = Consumer("urn:dtd:Reading");
            root["properties"]!["Payload"] = schema;
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False, Describe(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Message.Contains("Default Binary", StringComparison.Ordinal)),
                Is.True, Describe(result));
        }

        private static JsonObject InferredReading()
        {
            return new JsonObject
            {
                ["uav:dataTypeName"] = "t:Reading",
                ["type"] = "object",
                ["properties"] = new JsonObject { ["Value"] = new JsonObject { ["type"] = "boolean" } },
                ["required"] = new JsonArray("Value")
            };
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

        private static JsonObject Consumer(string graphId)
        {
            JsonObject root = Document(new JsonObject { ["@id"] = graphId });
            root.Remove("uav:dataTypeDefinitions");
            root["uav:id"] = "nsu=urn:test:datatype-closure;i=2";
            root["uav:browseName"] = "t:Consumer";
            root["properties"] = new JsonObject
            {
                ["Payload"] = new JsonObject
                {
                    ["type"] = "object",
                    ["uav:dataTypeDefinition"] = new JsonObject { ["@id"] = graphId }
                }
            };
            return root;
        }

        private static string Describe(WotConversionResult<UANodeSet> result)
        {
            return string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
        }

        private static readonly string[] s_binaryJsonNames = ["Default Binary", "Default JSON"];
        private static readonly string[] s_binaryJsonPresence = ["Binary", "JSON"];
    }
}
