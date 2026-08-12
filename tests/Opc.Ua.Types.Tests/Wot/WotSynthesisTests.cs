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
            "\"events\":{\"overTemp\":{\"uav:isEvent\":true," +
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
                root.References!.Any(r => r.ReferenceType == "HasComponent" && r.IsForward && r.Value == "ns=1;s=PumpType/PumpSpeed"),
                Is.True);
            Assert.That(
                root.References!.Any(r => r.ReferenceType == "GeneratesEvent" && r.IsForward),
                Is.True);

            UAVariable variable = nodeSet.Items!.OfType<UAVariable>().Single();
            Assert.That(variable.NodeId, Is.EqualTo("ns=1;s=PumpType/PumpSpeed"));

            // A bare "number" infers the abstract Number, not Double: §6.11.4
            // reads the schema for exactly what it says and leaves a concrete
            // width to an explicit annotation.
            Assert.That(variable.DataType, Is.EqualTo("i=26"));
            Assert.That(variable.AccessLevel, Is.EqualTo(1));
            Assert.That(
                variable.References!.Any(r => r.ReferenceType == "HasModellingRule" && r.Value == "i=78"),
                Is.True);

            UAMethod method = nodeSet.Items!.OfType<UAMethod>().Single();
            Assert.That(method.NodeId, Is.EqualTo("ns=1;s=PumpType/Reset"));
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
                Is.EqualTo(new[] { "Idle", "Active" }));
            Assert.That(
                inferred.Definition.Field!.Select(f => f.Value),
                Is.EqualTo(new[] { 0, 7 }));
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
                Is.EqualTo(new[] { "Second", "First" }));
            Assert.That(
                inferred.Definition.Field!.Select(f => f.IsOptional),
                Is.EqualTo(new[] { false, true }));
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
            Assert.That(root.NodeId, Is.EqualTo("ns=1;s=Pump"));
            Assert.That(
                root.References!.Any(r => r.ReferenceType == "HasTypeDefinition" && r.IsForward && r.Value == "i=58"),
                Is.True);

            UAVariable variable = nodeSet.Items!.OfType<UAVariable>().Single();
            Assert.That(variable.NodeId, Is.EqualTo("ns=1;s=Pump/Speed"));
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
    }
}
