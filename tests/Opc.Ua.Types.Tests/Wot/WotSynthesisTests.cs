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
            "\"title\":\"PumpType\",\"uav:browseName\":\"1:PumpType\"," +
            "\"uav:id\":\"nsu=http://example.com/demo/pump;i=1001\"," +
            "\"properties\":{\"pumpSpeed\":{\"@type\":\"uav:variableType\"," +
            "\"uav:browseName\":\"1:PumpSpeed\",\"type\":\"number\"," +
            "\"uav:modellingRule\":\"Mandatory\",\"readOnly\":true}}," +
            "\"actions\":{\"reset\":{\"@type\":\"uav:method\"," +
            "\"uav:browseName\":\"1:Reset\",\"uav:modellingRule\":\"Optional\"}}," +
            "\"events\":{\"overTemp\":{\"uav:isEvent\":true,\"uav:browseName\":\"1:OverTemp\"}}}";

        private const string ThingDescription =
            "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
            "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
            "\"@type\":\"uav:object\",\"title\":\"Pump01\"," +
            "\"uav:browseName\":\"1:Pump\"," +
            "\"properties\":{\"speed\":{\"@type\":\"uav:variable\"," +
            "\"uav:browseName\":\"1:Speed\",\"type\":\"number\",\"readOnly\":true}}}";

        [Test]
        public void ThingModelSynthesizesObjectTypeWithMembers()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(ThingModel));

            Assert.That(nodeSet.Models, Is.Not.Null);
            Assert.That(nodeSet.Models![0].ModelUri, Is.EqualTo("http://example.com/demo/pump"));

            UAObjectType root = nodeSet.Items!.OfType<UAObjectType>()
                .Single(t => t.BrowseName == "1:PumpType");
            Assert.That(root.NodeId, Is.EqualTo("ns=1;s=PumpType"));
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
            Assert.That(variable.DataType, Is.EqualTo("i=11"));
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

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(model));
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
