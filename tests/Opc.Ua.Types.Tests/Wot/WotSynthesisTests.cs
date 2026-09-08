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
using System.Linq;
using System.Text.Json;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotSynthesisTests
    {
        /// <summary>
        /// The Annex G.1 escaping of the demo namespace, which every generated
        /// path element in this model is qualified by.
        /// </summary>
        private const string PumpNs = "nsu=http%3A%2F%2Fexample.com%2Fdemo%2Fpump;";

        /// <summary>
        /// The Annex G.1 escaping of the namespace a Thing Description with no
        /// id falls back to.
        /// </summary>
        private const string SynthNs = "nsu=urn%3Aopcua%3Awot%3Asynthesized;";

        private const string ThingModel =
            "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
            "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
            "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
            "\"title\":\"PumpType\"," +
            "\"uav:browseName\":\"nsu=http://example.com/demo/pump;PumpType\"," +
            "\"uav:id\":\"nsu=http://example.com/demo/pump;i=1001\"," +
            "\"properties\":{\"pumpSpeed\":{\"@type\":\"uav:variableType\"," +
            "\"uav:browseName\":\"nsu=http://example.com/demo/pump;PumpSpeed\"," +
            "\"type\":\"number\"," +
            "\"uav:modellingRule\":\"Mandatory\",\"readOnly\":true}}," +
            "\"actions\":{\"reset\":{\"@type\":\"uav:method\"," +
            "\"uav:browseName\":\"nsu=http://example.com/demo/pump;Reset\"," +
            "\"uav:modellingRule\":\"Optional\"}}," +
            "\"events\":{\"overTemp\":{" +
            "\"uav:browseName\":\"nsu=http://example.com/demo/pump;OverTemp\"}}}";

        private const string ThingDescription =
            "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
            "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
            "\"@type\":\"uav:object\",\"title\":\"Pump01\"," +
            "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;Pump\"," +
            "\"properties\":{\"speed\":{\"@type\":\"uav:variable\"," +
            "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;Speed\"," +
            "\"type\":\"number\",\"readOnly\":true}}}";

        [Test]
        public void ThingModelSynthesizesObjectTypeWithMembers()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(ThingModel));

            Assert.That(nodeSet.Models, Is.Not.Null);
            Assert.That(nodeSet.Models![0].ModelUri, Is.EqualTo("http://example.com/demo/pump"));

            UAObjectType root = nodeSet.Items!.OfType<UAObjectType>()
                .Single(t => t.BrowseName == "1:PumpType");
            Assert.That(root.NodeId, Is.EqualTo("ns=1;i=1001"));
            Assert.That(
                root.References!.Any(r => r.ReferenceType == "HasSubtype" && !r.IsForward && r.Value == "i=58"),
                Is.True);
            Assert.That(
                root.References!.Any(
                    r => r.ReferenceType == "HasComponent" &&
                        r.IsForward &&
                        r.Value == "ns=1;s=/" + PumpNs + "PumpType/" + PumpNs + "PumpSpeed"),
                Is.True);
            Assert.That(
                root.References!.Any(r => r.ReferenceType == "GeneratesEvent" && r.IsForward),
                Is.True);

            UAVariable variable = nodeSet.Items!.OfType<UAVariable>().Single();
            Assert.That(variable.NodeId, Is.EqualTo("ns=1;s=/" + PumpNs + "PumpType/" + PumpNs + "PumpSpeed"));

            // A bare "number" infers the abstract Number, not Double: §6.11.4
            // reads the schema for exactly what it says and leaves a concrete
            // width to an explicit annotation.
            Assert.That(variable.DataType, Is.EqualTo("i=26"));
            Assert.That(variable.AccessLevel, Is.EqualTo(1));
            Assert.That(
                variable.References!.Any(r => r.ReferenceType == "HasModellingRule" && r.Value == "i=78"),
                Is.True);

            UAMethod method = nodeSet.Items!.OfType<UAMethod>().Single();
            Assert.That(method.NodeId, Is.EqualTo("ns=1;s=/" + PumpNs + "PumpType/" + PumpNs + "Reset"));
            Assert.That(
                method.References!.Any(r => r.ReferenceType == "HasModellingRule" && r.Value == "i=80"),
                Is.True);

            UAObjectType eventType = nodeSet.Items!.OfType<UAObjectType>()
                .Single(t => t.BrowseName == "1:OverTemp");
            Assert.That(
                eventType.References!.Any(r => r.ReferenceType == "HasSubtype" && !r.IsForward && r.Value == "i=2041"),
                Is.True);
        }

        // The canonical schema-to-DataType table of WoT Binding §6.11.4. A bare
        // integer and number infer the abstract Integer and Number; a string is
        // refined by contentEncoding and format, which is the only way a
        // ByteString, DateTime, Guid or UriString survives as JSON Schema.
        [TestCase("\"type\":\"boolean\"", "i=1")]
        [TestCase("\"type\":\"integer\"", "i=27")]
        [TestCase("\"type\":\"number\"", "i=26")]
        [TestCase("\"type\":\"string\"", "i=12")]
        [TestCase("\"type\":\"string\",\"contentEncoding\":\"base64\"", "i=15")]
        [TestCase("\"type\":\"string\",\"format\":\"date-time\"", "i=13")]
        [TestCase("\"type\":\"string\",\"format\":\"uuid\"", "i=14")]
        [TestCase("\"type\":\"string\",\"format\":\"uri\"", "i=23751")]
        [TestCase("\"type\":\"null\"", "i=24")]
        [TestCase("", "i=24")]
        public void SchemaInfersCanonicalDataType(string schema, string expected)
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(
                Encoding.UTF8.GetBytes(SchemaThing(schema)));

            UAVariable variable = nodeSet.Items!.OfType<UAVariable>().Single();
            Assert.That(variable.DataType, Is.EqualTo(expected));
        }

        // §6.11.4: an explicit annotation selects a concrete or otherwise
        // different built-in, and outranks whatever the json type would infer.
        [Test]
        public void ExplicitDataTypeAnnotationOutranksInference()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(
                Encoding.UTF8.GetBytes(SchemaThing("\"type\":\"number\",\"uav:dataTypeId\":\"i=10\"")));

            UAVariable variable = nodeSet.Items!.OfType<UAVariable>().Single();
            Assert.That(variable.DataType, Is.EqualTo("i=10"));
        }

        // §6.11.4: inference fails rather than guesses. JSON member order
        // carries no meaning, so beyond one property the order is mandatory;
        // and a bare integer or number inside a Structure is ambiguous, because
        // permitting subtype values would need a subtyped-value kind.
        [TestCase(
            "\"uav:dataTypeName\":\"demo:NoOrder\",\"type\":\"object\"," +
            "\"properties\":{\"A\":{\"type\":\"boolean\"},\"B\":{\"type\":\"boolean\"}}",
            "uav:fieldOrder")]
        [TestCase(
            "\"uav:dataTypeName\":\"demo:BareInteger\",\"type\":\"object\"," +
            "\"uav:fieldOrder\":[\"A\"],\"properties\":{\"A\":{\"type\":\"integer\"}}," +
            "\"required\":[\"A\"]",
            "ambiguous")]
        [TestCase(
            "\"uav:dataTypeName\":\"demo:BareNumber\",\"type\":\"object\"," +
            "\"uav:fieldOrder\":[\"A\"],\"properties\":{\"A\":{\"type\":\"number\"}}," +
            "\"required\":[\"A\"]",
            "ambiguous")]
        [TestCase(
            "\"uav:dataTypeName\":\"demo:NoBase\",\"type\":\"integer\",\"minimum\":0",
            "uav:dataTypeSubtypeOf")]
        [TestCase(
            "\"uav:dataTypeName\":\"demo:Missing\",\"type\":\"object\"," +
            "\"uav:fieldOrder\":[\"Absent\"],\"properties\":{\"A\":{\"type\":\"boolean\"}}",
            "does not define")]
        public void AmbiguousSchemaFailsRatherThanGuesses(string schema, string expected)
        {
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                WotDocument.Parse(Encoding.UTF8.GetBytes(SchemaThing(schema))));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Has.Some.Contains(expected));
        }

        // §6.11.5: a bare enum array states values but never names them, so it
        // shall not infer an Enumeration.
        [Test]
        public void BareEnumArrayDoesNotInferAnEnumeration()
        {
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                WotDocument.Parse(Encoding.UTF8.GetBytes(SchemaThing(
                    "\"uav:dataTypeName\":\"demo:Bare\",\"type\":\"integer\"," +
                    "\"enum\":[0,1,2],\"uav:dataTypeSubtypeOf\":{\"uav:dataTypeId\":\"i=7\"}"))));

            UADataType inferred = result.Value!.Items!.OfType<UADataType>().Single();
            Assert.That(inferred.Definition, Is.Null);
        }

        // §6.11.5: an integer oneOf whose branches each carry a const and a
        // name does infer one, values intact.
        [Test]
        public void NamedOneOfInfersAnEnumeration()
        {
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                WotDocument.Parse(Encoding.UTF8.GetBytes(SchemaThing(
                    "\"uav:dataTypeName\":\"demo:Mode\",\"type\":\"integer\",\"oneOf\":[" +
                    "{\"const\":0,\"uav:enumName\":\"Idle\"}," +
                    "{\"const\":7,\"uav:enumName\":\"Active\"}]"))));

            UADataType inferred = result.Value!.Items!.OfType<UADataType>().Single();
            Assert.That(
                inferred.Definition!.Field!.Select(f => f.Name),
                Is.EqualTo(s_namedOneOfEnumNames));
            Assert.That(
                inferred.Definition.Field!.Select(f => f.Value),
                Is.EqualTo(s_namedOneOfEnumValues));
        }

        // §6.11.4: the required array decides optionality, and uav:fieldOrder
        // decides encoding order rather than JSON member order.
        [Test]
        public void RequiredDecidesOptionalityAndOrderIsTaken()
        {
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                WotDocument.Parse(Encoding.UTF8.GetBytes(SchemaThing(
                    "\"uav:dataTypeName\":\"demo:Rec\",\"type\":\"object\"," +
                    "\"uav:fieldOrder\":[\"Second\",\"First\"]," +
                    "\"properties\":{\"First\":{\"type\":\"boolean\"}," +
                    "\"Second\":{\"type\":\"string\"}},\"required\":[\"Second\"]"))));

            UADataType inferred = result.Value!.Items!.OfType<UADataType>().Single();
            Assert.That(
                inferred.Definition!.Field!.Select(f => f.Name),
                Is.EqualTo(s_statedFieldOrder));
            Assert.That(
                inferred.Definition.Field!.Select(f => f.IsOptional),
                Is.EqualTo(s_statedFieldOptionality));
        }

        // The reference validator shipped with the specification enforces these
        // as mutations of its own example: each breaks one rule and shall be
        // caught. They matter to a converter as much as to a validator, because
        // accepting them materializes a malformed OPC UA definition silently.
        [TestCase("optional field in a plain Structure", "\"uav:isOptional\":true", "no room")]
        [TestCase("subtyped value in a plain Structure", "\"uav:allowSubtypes\":true", "subtyped-value")]
        public void FacetContradictingItsStructureKindIsRejected(
            string scenario,
            string facet,
            string expected)
        {
            _ = scenario;
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                WotDocument.Parse(Encoding.UTF8.GetBytes(DefinitionThing(
                    "\"uav:structureType\":\"Structure\",\"uav:fields\":[{" +
                    "\"uav:fieldName\":\"A\",\"uav:fieldDataTypeId\":\"i=11\"," +
                    facet + "}]"))));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Has.Some.Contains(expected));
        }

        // ArrayDimensions carries one bound per dimension, so a rank of two
        // with a single dimension describes nothing coherent.
        [Test]
        public void ArrayDimensionsDisagreeingWithValueRankIsRejected()
        {
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                WotDocument.Parse(Encoding.UTF8.GetBytes(DefinitionThing(
                    "\"uav:fields\":[{\"uav:fieldName\":\"A\"," +
                    "\"uav:fieldDataTypeId\":\"i=11\",\"uav:valueRank\":2," +
                    "\"uav:arrayDimensions\":[3]}]"))));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Has.Some.Contains("one bound per dimension"));
        }

        // The same shape stated coherently is accepted, so the checks above
        // reject the contradiction rather than the facet.
        [Test]
        public void CoherentRankAndDimensionsAreAccepted()
        {
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                WotDocument.Parse(Encoding.UTF8.GetBytes(DefinitionThing(
                    "\"uav:fields\":[{\"uav:fieldName\":\"A\"," +
                    "\"uav:fieldDataTypeId\":\"i=11\",\"uav:valueRank\":2," +
                    "\"uav:arrayDimensions\":[3,3]}]"))));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Is.Empty);
            Assert.That(
                result.Value!.Items!.OfType<UADataType>().Single()
                    .Definition!.Field![0].ArrayDimensions,
                Is.EqualTo("3,3"));
        }

        // More rules the specification's reference validator enforces as
        // mutations of its own example. Each would otherwise materialize
        // silently into a definition that cannot be read back unambiguously.
        [TestCase(
            "two definitions on one identity",
            "\"uav:dataTypeDefinitions\":[" +
            "{\"@id\":\"urn:t#A\",\"@type\":\"uav:StructureDefinition\"," +
            "\"uav:dataTypeName\":\"demo:A\",\"uav:dataTypeId\":\"nsu=http://x/;s=Same\"}," +
            "{\"@id\":\"urn:t#B\",\"@type\":\"uav:StructureDefinition\"," +
            "\"uav:dataTypeName\":\"demo:B\",\"uav:dataTypeId\":\"nsu=http://x/;s=Same\"}]",
            "claim the identity")]
        [TestCase(
            "two types on one encoding",
            "\"uav:dataTypeDefinitions\":[" +
            "{\"@id\":\"urn:t#A\",\"@type\":\"uav:StructureDefinition\"," +
            "\"uav:dataTypeName\":\"demo:A\",\"uav:binaryEncodingId\":\"nsu=http://x/;s=Shared\"}," +
            "{\"@id\":\"urn:t#B\",\"@type\":\"uav:StructureDefinition\"," +
            "\"uav:dataTypeName\":\"demo:B\",\"uav:binaryEncodingId\":\"nsu=http://x/;s=Shared\"}]",
            "ambiguous to decode")]
        [TestCase(
            "a default naming a fourth encoding",
            "\"uav:dataTypeDefinitions\":[" +
            "{\"@id\":\"urn:t#A\",\"@type\":\"uav:StructureDefinition\"," +
            "\"uav:dataTypeName\":\"demo:A\"," +
            "\"uav:defaultEncodingId\":\"nsu=http://x/;s=Elsewhere\"}]",
            "none of the three")]
        [TestCase(
            "two enumeration fields on one value",
            "\"uav:dataTypeDefinitions\":[" +
            "{\"@id\":\"urn:t#E\",\"@type\":\"uav:EnumDefinition\"," +
            "\"uav:dataTypeName\":\"demo:E\",\"uav:enumFields\":[" +
            "{\"uav:enumName\":\"A\",\"uav:enumValue\":0}," +
            "{\"uav:enumName\":\"B\",\"uav:enumValue\":0}]}]",
            "share the value")]
        [TestCase(
            "an OptionSet numbering a negative bit",
            "\"uav:dataTypeDefinitions\":[" +
            "{\"@id\":\"urn:t#O\",\"@type\":\"uav:EnumDefinition\"," +
            "\"uav:dataTypeName\":\"demo:O\",\"uav:isOptionSet\":true," +
            "\"uav:dataTypeSubtypeOf\":{\"uav:dataTypeId\":\"i=7\"}," +
            "\"uav:enumFields\":[{\"uav:enumName\":\"A\",\"uav:enumValue\":-1}]}]",
            "no negative bit")]
        public void DefinitionThatCannotBeReadBackIsRejected(
            string scenario,
            string definitions,
            string expected)
        {
            _ = scenario;
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                WotDocument.Parse(Encoding.UTF8.GetBytes(
                    "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                    "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                    "\"demo\":\"http://example.com/demo/pump\"}]," +
                    "\"@type\":\"uav:object\",\"title\":\"Thing\"," +
                    "\"uav:browseName\":\"nsu=http://example.com/demo/pump;Thing\"," +
                    definitions + "}")));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Has.Some.Contains(expected));
        }

        // §6.11.3 states inherited fields first, and the encoding writes them
        // before the subtype's own, so a renamed or dropped inherited field
        // shifts everything after it and the value decodes as something else.
        [TestCase("\"Renamed\",\"State\",\"Extra\"", "where it inherits")]
        [TestCase("\"Value\",\"Extra\"", "where it inherits")]
        [TestCase("\"State\",\"Value\",\"Extra\"", "where it inherits")]
        [TestCase("\"Value\"", "dropping one shifts")]
        public void SubtypeChangingItsInheritedFieldsIsRejected(string fields, string expected)
        {
            string names = string.Join(",", fields.Split(',')
                .Select(f => "{\"uav:fieldName\":" + f + ",\"uav:fieldDataTypeId\":\"i=11\"}"));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                WotDocument.Parse(Encoding.UTF8.GetBytes(InheritanceThing(names))));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Has.Some.Contains(expected));
        }

        // The same subtype stating its inherited prefix faithfully is accepted,
        // so the check rejects the change rather than the inheritance.
        [Test]
        public void SubtypeRepeatingItsInheritedFieldsIsAccepted()
        {
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                WotDocument.Parse(Encoding.UTF8.GetBytes(InheritanceThing(
                    "{\"uav:fieldName\":\"Value\",\"uav:fieldDataTypeId\":\"i=11\"}," +
                    "{\"uav:fieldName\":\"State\",\"uav:fieldDataTypeId\":\"i=11\"}," +
                    "{\"uav:fieldName\":\"Extra\",\"uav:fieldDataTypeId\":\"i=11\"}"))));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Is.Empty);
        }

        // §6.11.5, narrowed by the spec PR: an OptionSet's base shall be the
        // concrete Byte, UInt16, UInt32 or UInt64, and shall be wide enough for
        // the highest authored bit. An abstract base says only that some bits
        // exist, not how many.
        [TestCase("i=28", 0, "does not say how many bits")]
        [TestCase("i=27", 0, "does not say how many bits")]
        [TestCase("i=3", 8, "cannot represent")]
        [TestCase("i=5", 16, "cannot represent")]
        [TestCase("i=7", 32, "cannot represent")]
        public void OptionSetBaseThatCannotCarryItsBitsIsRejected(
            string baseId,
            int bit,
            string expected)
        {
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                WotDocument.Parse(Encoding.UTF8.GetBytes(OptionSetThing(baseId, bit))));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Has.Some.Contains(expected));
        }

        // The same OptionSet inside its base's width is accepted, so the check
        // rejects the overflow rather than the OptionSet.
        [TestCase("i=3", 7)]
        [TestCase("i=5", 15)]
        [TestCase("i=7", 31)]
        [TestCase("i=9", 63)]
        public void OptionSetWithinItsBaseWidthIsAccepted(string baseId, int bit)
        {
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                WotDocument.Parse(Encoding.UTF8.GetBytes(OptionSetThing(baseId, bit))));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Is.Empty);
        }

        // A NodeSet may write the encoding link from either end, and real
        // companion models write it from the Object: the DI NodeSet carries no
        // forward Reference on the DataType at all. Looking only one way marks
        // an ordinary Structure as having no encodings, which the way back then
        // believes.
        [Test]
        public void EncodingDeclaredByTheObjectIsStillFound()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["http://example.com/demo/pump"],
                Items =
                [
                    new UADataType
                    {
                        NodeId = "ns=1;i=100",
                        BrowseName = "1:PayloadDataType",
                        Definition = new Opc.Ua.Export.DataTypeDefinition
                        {
                            Name = "1:PayloadDataType",
                            Field =
                            [
                                new Opc.Ua.Export.DataTypeField
                                {
                                    Name = "Code",
                                    DataType = "i=6"
                                }
                            ]
                        }
                    },
                    new UAObject
                    {
                        NodeId = "ns=1;i=101",
                        BrowseName = "Default Binary",
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasEncoding",
                                IsForward = false,
                                Value = "ns=1;i=100"
                            }
                        ]
                    }
                ]
            };

            using WotDocument document = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement definition = document.RootElement
                .GetProperty("uav:dataTypeDefinitions")[0];
            Assert.That(
                definition.TryGetProperty("uav:hasDefaultEncoding", out _),
                Is.False,
                "A type whose encoding is declared by the Object still has one.");
        }

        // §6.11.2, tightened by the spec PR: the subtype graph is acyclic and
        // kind-compatible. A cycle is the worst of these — resolving the
        // inherited prefix or the terminal base would not terminate.
        [TestCase("cycle", "\"@id\":\"urn:t#B\"", "\"@id\":\"urn:t#A\"",
            "uav:StructureDefinition", "uav:StructureDefinition", "its own ancestor")]
        [TestCase("enumeration under a structure", "\"@id\":\"urn:t#B\"", "",
            "uav:EnumDefinition", "uav:StructureDefinition", "within its own kind")]
        [TestCase("structure under an enumeration", "\"@id\":\"urn:t#B\"", "",
            "uav:StructureDefinition", "uav:EnumDefinition", "within its own kind")]
        public void IncompatibleSubtypeGraphIsRejected(
            string scenario,
            string aBase,
            string bBase,
            string aKind,
            string bKind,
            string expected)
        {
            _ = scenario;
            string b = "{\"@id\":\"urn:t#B\",\"@type\":\"" + bKind + "\"," +
                "\"uav:dataTypeName\":\"demo:B\"" +
                (bBase.Length == 0 ? string.Empty : ",\"uav:dataTypeSubtypeOf\":{" + bBase + "}") + "}";
            string a = "{\"@id\":\"urn:t#A\",\"@type\":\"" + aKind + "\"," +
                "\"uav:dataTypeName\":\"demo:A\",\"uav:dataTypeSubtypeOf\":{" + aBase + "}}";

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                WotDocument.Parse(Encoding.UTF8.GetBytes(
                    "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                    "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                    "\"demo\":\"http://example.com/demo/pump\"}]," +
                    "\"@type\":\"uav:object\",\"title\":\"Thing\"," +
                    "\"uav:browseName\":\"nsu=http://example.com/demo/pump;Thing\"," +
                    "\"uav:dataTypeDefinitions\":[" + a + "," + b + "]}")));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Has.Some.Contains(expected));
        }

        /// <summary>
        /// A NodeSet is not a single Thing. A companion model states many type
        /// definitions side by side, and §9.1 gives each its own document, so
        /// choosing one root leaves every other type unreachable and forces the
        /// whole model into the native projection.
        /// </summary>
        [Test]
        public void EveryTypeDefinitionRootsItsOwnDocument()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["http://example.com/demo/pump"],
                Items =
                [
                    TypeWithVariable("ns=1;i=100", "1:AlphaType", "ns=1;i=101", "1:AlphaValue"),
                    Variable("ns=1;i=101", "1:AlphaValue", "ns=1;i=100"),
                    TypeWithVariable("ns=1;i=200", "1:BetaType", "ns=1;i=201", "1:BetaValue"),
                    Variable("ns=1;i=201", "1:BetaValue", "ns=1;i=200"),
                    TypeWithVariable("ns=1;i=300", "1:GammaType", "ns=1;i=301", "1:GammaValue"),
                    Variable("ns=1;i=301", "1:GammaValue", "ns=1;i=300")
                ]
            };

            WotConversionResult<WotDocumentSet> result =
                WotNodeSetConverter.FromNodeSetDocuments(nodeSet, "model");

            using WotDocumentSet set = result.Value!;
            Assert.That(set.Entries, Has.Count.EqualTo(3));
            var counts = new List<int>();
            foreach (WotDocumentSetEntry entry in set.Entries)
            {
                counts.Add(entry.Document.Properties.Count);
            }
            Assert.That(
                counts,
                Is.EqualTo(s_onePropertyPerDocument),
                "Each type's own Variable belongs to that type's document.");
        }

        /// <summary>
        /// A ReferenceType is a type definition like any other. §9.1 maps it to
        /// the compact name a link <c>rel</c> uses, which says how it is
        /// referred to, not how it is defined — so without a document of its
        /// own its BrowseName, supertype and inverse name have nowhere to live
        /// and the type is lost.
        /// </summary>
        [Test]
        public void ReferenceTypeRootsItsOwnDocument()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["http://example.com/demo/pump"],
                Items =
                [
                    new UAReferenceType
                    {
                        NodeId = "ns=1;i=900",
                        BrowseName = "1:ConnectsTo",
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=32"
                            }
                        ]
                    }
                ]
            };

            WotConversionResult<WotDocumentSet> result =
                WotNodeSetConverter.FromNodeSetDocuments(nodeSet, "model");

            using WotDocumentSet set = result.Value!;
            Assert.That(set.Entries, Has.Count.EqualTo(1));

            UANodeSet back = WotNodeSetConverter.ToNodeSet(set.Entries[0].Document);
            UAReferenceType restored = back.Items!.OfType<UAReferenceType>().Single();
            Assert.That(restored.BrowseName, Does.EndWith(":ConnectsTo"));
        }

        private static UAObjectType TypeWithVariable(
            string nodeId,
            string browseName,
            string childId,
            string childName)
        {
            _ = childName;
            return new UAObjectType
            {
                NodeId = nodeId,
                BrowseName = browseName,
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
                        Value = childId
                    }
                ]
            };
        }

        private static UAVariable Variable(string nodeId, string browseName, string parentId)
        {
            return new UAVariable
            {
                NodeId = nodeId,
                BrowseName = browseName,
                ParentNodeId = parentId,
                DataType = "i=11",
                References =
                [
                    new Reference
                    {
                        ReferenceType = "HasComponent",
                        IsForward = false,
                        Value = parentId
                    }
                ]
            };
        }

        /// <summary>
        /// A Variable may hold Variables of its own — EURange and
        /// EngineeringUnits sit below an analog Variable, two levels under the
        /// Node that roots the document. Walking only the root's references
        /// leaves them behind, and they come back re-parented onto the Thing.
        /// </summary>
        [Test]
        public void VariableChildrenOfAVariableSurviveWithTheirParent()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["http://example.com/demo/pump"],
                Items =
                [
                    TypeWithVariable("ns=1;i=100", "1:PumpType", "ns=1;i=101", "1:Flow"),
                    NestingVariable("ns=1;i=101", "1:Flow", "ns=1;i=100", "ns=1;i=102"),
                    Variable("ns=1;i=102", "1:EURange", "ns=1;i=101")
                ]
            };

            using WotDocument document = WotNodeSetConverter.FromNodeSet(nodeSet);

            Assert.That(document.Properties, Has.Count.EqualTo(2));

            // The child names the Variable it belongs to, not the Thing.
            Assert.That(
                document.Properties["EURange"].GetProperty("uav:componentOf")[0].GetString(),
                Is.EqualTo("nsu=http://example.com/demo/pump;i=101"));

            // Read it back through the readable mapping. With the projection
            // present the way back prefers it, and the readable terms would
            // never be exercised at all.
            UANodeSet back = WotNodeSetConverter.ToNodeSet(StripProjection(document));
            UAVariable range = back.Items!.OfType<UAVariable>()
                .Single(v => v.BrowseName!.EndsWith(":EURange", StringComparison.Ordinal));
            UAVariable flow = back.Items!.OfType<UAVariable>()
                .Single(v => v.BrowseName!.EndsWith(":Flow", StringComparison.Ordinal));

            Assert.That(
                range.References!.Any(r => r.ReferenceType == "HasComponent" &&
                    !r.IsForward && r.Value == flow.NodeId),
                Is.True,
                "The child belongs to the Variable that held it, not to the Thing.");
            Assert.That(
                flow.References!.Any(r => r.ReferenceType == "HasComponent" &&
                    r.IsForward && r.Value == range.NodeId),
                Is.True);
        }

        private static WotDocument StripProjection(WotDocument document)
        {
            using var buffer = new System.IO.MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (System.Text.Json.JsonProperty member in
                    document.RootElement.EnumerateObject())
                {
                    if (member.Name is "uav:nodes" or "uav:nodeSet")
                    {
                        continue;
                    }
                    writer.WritePropertyName(member.Name);
                    member.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            return WotDocument.Parse(buffer.ToArray());
        }

        /// <summary>
        /// A Method holds its InputArguments and OutputArguments as Variables.
        /// They are two levels below the Node that roots the document, so the
        /// affordance walk never reached them and every argument of every
        /// Method was left behind.
        /// </summary>
        [Test]
        public void MethodArgumentsSurviveWithTheirMethod()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["http://example.com/demo/pump"],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=100",
                        BrowseName = "1:PumpType",
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = true,
                                Value = "ns=1;i=110"
                            }
                        ]
                    },
                    new UAMethod
                    {
                        NodeId = "ns=1;i=110",
                        BrowseName = "1:Start",
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasProperty",
                                IsForward = true,
                                Value = "ns=1;i=111"
                            }
                        ]
                    },
                    Variable("ns=1;i=111", "1:InputArguments", "ns=1;i=110")
                ]
            };

            using WotDocument document = WotNodeSetConverter.FromNodeSet(nodeSet);

            Assert.That(document.Actions, Has.Count.EqualTo(1));
            Assert.That(document.Properties, Has.Count.EqualTo(1));
            Assert.That(
                document.Properties["InputArguments"].GetProperty("uav:componentOf")[0].GetString(),
                Is.EqualTo("nsu=http://example.com/demo/pump;i=110"));

            UANodeSet back = WotNodeSetConverter.ToNodeSet(StripProjection(document));
            UAVariable arguments = back.Items!.OfType<UAVariable>().Single();
            Assert.That(
                arguments.References!.Any(r => r.ReferenceType == "HasComponent" &&
                    !r.IsForward && r.Value == "ns=1;i=110"),
                Is.True,
                "The arguments belong to the Method, not to the Thing.");
        }

        private static UAVariable NestingVariable(
            string nodeId,
            string browseName,
            string parentId,
            string childId)
        {
            return new UAVariable
            {
                NodeId = nodeId,
                BrowseName = browseName,
                ParentNodeId = parentId,
                DataType = "i=11",
                References =
                [
                    new Reference
                    {
                        ReferenceType = "HasComponent",
                        IsForward = false,
                        Value = parentId
                    },
                    new Reference
                    {
                        ReferenceType = "HasProperty",
                        IsForward = true,
                        Value = childId
                    }
                ]
            };
        }

        private static string OptionSetThing(string baseId, int bit)
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"demo\":\"http://example.com/demo/pump\"}]," +
                "\"@type\":\"uav:object\",\"title\":\"Thing\"," +
                "\"uav:browseName\":\"nsu=http://example.com/demo/pump;Thing\"," +
                "\"uav:dataTypeDefinitions\":[{" +
                "\"@id\":\"urn:t#O\",\"@type\":\"uav:EnumDefinition\"," +
                "\"uav:dataTypeName\":\"demo:Flags\",\"uav:isOptionSet\":true," +
                "\"uav:dataTypeSubtypeOf\":{\"uav:dataTypeId\":\"" + baseId + "\"}," +
                "\"uav:enumFields\":[{\"uav:enumName\":\"Bit\",\"uav:enumValue\":" +
                bit.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}]}]}";
        }

        private static string InheritanceThing(string subtypeFields)
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"demo\":\"http://example.com/demo/pump\"}]," +
                "\"@type\":\"uav:object\",\"title\":\"Thing\"," +
                "\"uav:browseName\":\"nsu=http://example.com/demo/pump;Thing\"," +
                "\"uav:dataTypeDefinitions\":[" +
                "{\"@id\":\"urn:t#Base\",\"@type\":\"uav:StructureDefinition\"," +
                "\"uav:dataTypeName\":\"demo:BaseDataType\",\"uav:fields\":[" +
                "{\"uav:fieldName\":\"Value\",\"uav:fieldDataTypeId\":\"i=11\"}," +
                "{\"uav:fieldName\":\"State\",\"uav:fieldDataTypeId\":\"i=11\"}]}," +
                "{\"@id\":\"urn:t#Sub\",\"@type\":\"uav:StructureDefinition\"," +
                "\"uav:dataTypeName\":\"demo:SubDataType\"," +
                "\"uav:dataTypeSubtypeOf\":{\"@id\":\"urn:t#Base\"}," +
                "\"uav:fields\":[" + subtypeFields + "]}]}";
        }

        private static string DefinitionThing(string body)
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"demo\":\"http://example.com/demo/pump\"}]," +
                "\"@type\":\"uav:object\",\"title\":\"Thing\"," +
                "\"uav:browseName\":\"nsu=http://example.com/demo/pump;Thing\"," +
                "\"uav:dataTypeDefinitions\":[{" +
                "\"@id\":\"urn:t#A\",\"@type\":\"uav:StructureDefinition\"," +
                "\"uav:dataTypeName\":\"demo:ADataType\"," + body + "}]}";
        }

        private static string SchemaThing(string schema)
        {
            string members = schema.Length == 0 ? string.Empty : schema + ",";
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"demo\":\"http://example.com/demo/pump\"}]," +
                "\"@type\":\"uav:object\",\"title\":\"Thing\"," +
                "\"uav:browseName\":\"nsu=http://example.com/demo/pump;Thing\"," +
                "\"properties\":{\"sample\":{\"@type\":\"uav:variable\"," + members +
                "\"uav:browseName\":\"nsu=http://example.com/demo/pump;Sample\"}}}";
        }

        [Test]
        public void ThingDescriptionSynthesizesObjectWithTypeDefinition()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(ThingDescription));

            UAObject root = nodeSet.Items!.OfType<UAObject>().Single();
            Assert.That(root.NodeId, Is.EqualTo("ns=1;s=/" + SynthNs + "Pump"));
            Assert.That(
                root.References!.Any(r => r.ReferenceType == "HasTypeDefinition" && r.IsForward && r.Value == "i=58"),
                Is.True);

            UAVariable variable = nodeSet.Items!.OfType<UAVariable>().Single();
            Assert.That(variable.NodeId, Is.EqualTo("ns=1;s=/" + SynthNs + "Pump/" + SynthNs + "Speed"));
            Assert.That(variable.AccessLevel, Is.EqualTo(1));
        }

        [Test]
        public void SynthesisIsDeterministic()
        {
            UANodeSet first = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(ThingModel));
            UANodeSet second = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(ThingModel));

            Assert.That(WotTestData.Serialize(first), Is.EqualTo(WotTestData.Serialize(second)));
        }

        [Test]
        public void UnsupportedSchemaProducesDiagnostic()
        {
            const string model =
                "{\"@type\":[\"tm:ThingModel\",\"uav:objectType\"],\"title\":\"T\"," +
                "\"uav:browseName\":\"1:T\",\"properties\":{\"blob\":{" +
                "\"@type\":\"uav:variableType\",\"uav:browseName\":\"1:Blob\"," +
                "\"uav:externalSchema\":\"https://example.com/schema.json\"}}}";

            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(model));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnsupportedSchema),
                Is.True);
        }

        [Test]
        public void SynthesizedNodeSetSerializesToValidXml()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(ThingModel));
            byte[] xml = WotTestData.Serialize(nodeSet);

            using var stream = new System.IO.MemoryStream(xml);
            bool valid = UANodeSet.Validate(stream, out System.Collections.Generic.IReadOnlyList<string> errors);
            Assert.That(valid, Is.True, string.Join("; ", errors));
        }

        /// <summary>
        /// The enumeration names §6.11.5 infers from a named <c>oneOf</c>, in
        /// the order its branches are written.
        /// </summary>
        private static readonly string[] s_namedOneOfEnumNames = ["Idle", "Active"];

        /// <summary>
        /// The enumeration values the same branches carry as their
        /// <c>const</c>, which the inference keeps intact.
        /// </summary>
        private static readonly int[] s_namedOneOfEnumValues = [0, 7];

        /// <summary>
        /// The encoding order <c>uav:fieldOrder</c> states, which is the
        /// reverse of the JSON member order §6.11.4 would otherwise take.
        /// </summary>
        private static readonly string[] s_statedFieldOrder = ["Second", "First"];

        /// <summary>
        /// The optionality of the same fields: <c>required</c> names Second
        /// alone, so only First is optional.
        /// </summary>
        private static readonly bool[] s_statedFieldOptionality = [false, true];

        /// <summary>
        /// One Property per document, once for each of the three types.
        /// </summary>
        private static readonly int[] s_onePropertyPerDocument = [1, 1, 1];
    }
}
