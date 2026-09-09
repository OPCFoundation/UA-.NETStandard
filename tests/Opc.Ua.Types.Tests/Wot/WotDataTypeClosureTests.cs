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
    }
}
