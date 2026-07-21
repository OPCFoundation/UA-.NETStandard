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

using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotDocumentTests
    {
        [Test]
        public void ParseAndWritePreservesUnknownMembersExactly()
        {
            byte[] json = Encoding.UTF8.GetBytes(
                "{\"@context\":[],\"title\":\"T\",\"vendor:unknown\":{\"b\":2,\"a\":1}}");

            using WotDocument document = WotDocument.Parse(json);
            using var output = new System.IO.MemoryStream();
            document.Write(output);

            Assert.That(output.ToArray(), Is.EqualTo(json));
        }

        [Test]
        public void LexicalSurfaceExposesTypedAccess()
        {
            byte[] json = Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"id\":\"urn:pump\"," +
                "\"uav:browseName\":\"1:PumpType\"," +
                "\"properties\":{\"speed\":{\"type\":\"number\"}}," +
                "\"actions\":{\"reset\":{}},\"events\":{\"hot\":{}}," +
                "\"links\":[{\"rel\":\"tm:extends\",\"href\":\"x\"}]," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"schemaDefinitions\":{\"S\":{\"type\":\"object\"}}}");

            using WotDocument document = WotDocument.Parse(json);

            Assert.That(document.Kind, Is.EqualTo(WotDocumentKind.ThingModel));
            Assert.That(document.Title, Is.EqualTo("PumpType"));
            Assert.That(document.Id, Is.EqualTo("urn:pump"));
            Assert.That(document.TypeTokens, Does.Contain("uav:objectType"));
            Assert.That(document.Properties.ContainsKey("speed"), Is.True);
            Assert.That(document.Actions.ContainsKey("reset"), Is.True);
            Assert.That(document.Events.ContainsKey("hot"), Is.True);
            Assert.That(document.Links, Has.Count.EqualTo(1));
            Assert.That(document.SecurityDefinitions.ContainsKey("nosec_sc"), Is.True);
            Assert.That(document.SchemaDefinitions.ContainsKey("S"), Is.True);
            Assert.That(document.TryGetUav("browseName", out JsonElement browseName), Is.True);
            Assert.That(browseName.GetString(), Is.EqualTo("1:PumpType"));
        }

        [Test]
        public void JsonPointerResolvesNestedMembersIncludingEscapes()
        {
            byte[] json = Encoding.UTF8.GetBytes(
                "{\"properties\":{\"speed\":{\"uav:unit~x\":\"rpm\",\"items\":[10,20]}}}");

            using WotDocument document = WotDocument.Parse(json);

            Assert.That(
                document.TryEvaluatePointer("/properties/speed/items/1", out JsonElement item),
                Is.True);
            Assert.That(item.GetInt32(), Is.EqualTo(20));

            Assert.That(
                document.TryEvaluatePointer("/properties/speed/uav:unit~0x", out JsonElement unit),
                Is.True);
            Assert.That(unit.GetString(), Is.EqualTo("rpm"));

            Assert.That(document.TryEvaluatePointer("/missing", out _), Is.False);
        }

        [Test]
        public void CanonicalWriterProducesDeterministicSortedOutput()
        {
            byte[] first = Encoding.UTF8.GetBytes(
                "{ \"b\": 2, \"a\": 1, \"nested\": { \"y\": 2, \"x\": 1 } }");
            byte[] second = Encoding.UTF8.GetBytes(
                "{\"a\":1,\"nested\":{\"x\":1,\"y\":2},\"b\":2}");

            using WotDocument firstDocument = WotDocument.Parse(first);
            using WotDocument secondDocument = WotDocument.Parse(second);

            byte[] firstCanonical = firstDocument.ToCanonicalUtf8();
            byte[] secondCanonical = secondDocument.ToCanonicalUtf8();

            Assert.That(firstCanonical, Is.EqualTo(secondCanonical));
            Assert.That(
                Encoding.UTF8.GetString(firstCanonical),
                Is.EqualTo("{\"a\":1,\"b\":2,\"nested\":{\"x\":1,\"y\":2}}"));
        }

        [Test]
        public void CanonicalWriterIsSeparateFromExactWrite()
        {
            byte[] json = Encoding.UTF8.GetBytes("{ \"b\" : 2, \"a\" : 1 }");
            using WotDocument document = WotDocument.Parse(json);

            using var exact = new System.IO.MemoryStream();
            document.Write(exact);

            Assert.That(exact.ToArray(), Is.EqualTo(json));
            Assert.That(document.ToCanonicalUtf8(), Is.Not.EqualTo(json));
        }
    }
}
