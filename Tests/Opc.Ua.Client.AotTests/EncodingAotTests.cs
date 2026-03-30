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

namespace Opc.Ua.Client.AotTests
{
    /// <summary>
    /// AOT integration tests for binary and JSON encoding round-trips.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class EncodingAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task BinaryEncodeDecodeDataValue()
        {
            var original = new DataValue(Variant.From(42))
            {
                StatusCode = StatusCodes.Good
            };

            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(
                stream, fixture.Session.MessageContext, true))
            {
                encoder.WriteDataValue("Value", original);
            }

            stream.Position = 0;
            using var decoder = new BinaryDecoder(
                stream, fixture.Session.MessageContext, true);
            DataValue decoded = decoder.ReadDataValue("Value");

            await Assert.That(decoded).IsNotNull();
            await Assert.That(StatusCode.IsGood(decoded.StatusCode)).IsTrue();
        }

        [Test]
        public async Task BinaryEncodeDecodeNodeId()
        {
            NodeId[] nodeIds =
            [
                new NodeId(12345),
                new NodeId("TestString", 2),
                new NodeId(Guid.NewGuid(), 0),
                NodeId.Parse("ns=0;b=AQIDBA==")
            ];

            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(
                stream, fixture.Session.MessageContext, true))
            {
                foreach (NodeId nodeId in nodeIds)
                {
                    encoder.WriteNodeId("NodeId", nodeId);
                }
            }

            stream.Position = 0;
            using var decoder = new BinaryDecoder(
                stream, fixture.Session.MessageContext, true);

            for (int i = 0; i < nodeIds.Length; i++)
            {
                NodeId decoded = decoder.ReadNodeId("NodeId");
                await Assert.That(decoded).IsEqualTo(nodeIds[i]);
            }
        }

        [Test]
        public async Task BinaryEncodeDecodeExtensionObject()
        {
            var readValueId = new ReadValueId
            {
                NodeId = VariableIds.Server_ServerStatus,
                AttributeId = Attributes.Value
            };
            var original = new ExtensionObject(readValueId);

            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(
                stream, fixture.Session.MessageContext, true))
            {
                encoder.WriteExtensionObject("ExtObj", original);
            }

            stream.Position = 0;
            using var decoder = new BinaryDecoder(
                stream, fixture.Session.MessageContext, true);
            ExtensionObject decoded = decoder.ReadExtensionObject("ExtObj");

            await Assert.That(decoded.TypeId.IsNull).IsFalse();
        }

        [Test]
        public async Task JsonEncodeDecodeDataValue()
        {
            var original = new DataValue(Variant.From(42))
            {
                StatusCode = StatusCodes.Good
            };

            string json;
            using (var encoder = new JsonEncoder(
                fixture.Session.MessageContext))
            {
                encoder.WriteDataValue("Value", original);
                json = encoder.CloseAndReturnText();
            }

            await Assert.That(json).IsNotNull();
            await Assert.That(json).Contains("42");

            using var decoder = new JsonDecoder(
                json, fixture.Session.MessageContext);
            DataValue decoded = decoder.ReadDataValue("Value");

            await Assert.That(decoded).IsNotNull();
            await Assert.That(StatusCode.IsGood(decoded.StatusCode)).IsTrue();
        }

        [Test]
        public async Task BinaryEncodeDecodeVariant()
        {
            Variant[] variants =
            [
                Variant.From(123),
                Variant.From("Hello AOT"),
                Variant.From((ArrayOf<int>)[1, 2, 3]),
                Variant.From(DateTimeUtc.Now)
            ];

            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(
                stream, fixture.Session.MessageContext, true))
            {
                foreach (Variant v in variants)
                {
                    encoder.WriteVariant("Variant", v);
                }
            }

            stream.Position = 0;
            using var decoder = new BinaryDecoder(
                stream, fixture.Session.MessageContext, true);

            for (int i = 0; i < variants.Length; i++)
            {
                Variant decoded = decoder.ReadVariant("Variant");
                await Assert.That(decoded.AsBoxedObject()).IsNotNull();
            }
        }
    }
}
