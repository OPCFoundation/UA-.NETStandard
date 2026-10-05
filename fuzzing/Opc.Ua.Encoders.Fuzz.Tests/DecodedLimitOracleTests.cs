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

using System.IO;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// The limit oracle flags decoded values a decoder should have rejected, such as the
    /// JSON NodeId strings that were not checked against MaxStringLength (J1-1).
    /// </summary>
    [TestFixture]
    [Category("Fuzzing")]
    public class DecodedLimitOracleTests
    {
        [Test]
        public void LimitOracleAcceptsTheSeedMessages()
        {
            Assert.DoesNotThrow(() => CheckLimits(Testcases.CreateRichReadRequest()));
            Assert.DoesNotThrow(() => CheckLimits(Testcases.CreateRichReadResponse()));
            Assert.DoesNotThrow(() => CheckLimits(Testcases.CreatePublishResponse()));
            Assert.DoesNotThrow(() => CheckLimits(Testcases.CreateRichWriteRequest()));
        }

        [Test]
        public void LimitOracleReportsAStringNodeIdBeyondMaxStringLength()
        {
            ReadRequest request = CreateReadRequest(new NodeId(LongString(), 1));

            ResourceBudgetException ex = Assert.Throws<ResourceBudgetException>(() => CheckLimits(request));

            Assert.That(ex.Kind, Is.EqualTo(ResourceFindingKind.Limit));
            Assert.That(ex.Message, Does.Contain("NodeId.Identifier"));
            Assert.That(ex.Message, Does.Contain(nameof(IServiceMessageContext.MaxStringLength)));
        }

        [Test]
        public void LimitOracleReportsAnOpaqueNodeIdBeyondMaxByteStringLength()
        {
            byte[] opaque = new byte[FuzzableCode.FuzzMaxByteStringLength + 1];
            ReadRequest request = CreateReadRequest(new NodeId(ByteString.From(opaque), 1));

            ResourceBudgetException ex = Assert.Throws<ResourceBudgetException>(() => CheckLimits(request));

            Assert.That(ex.Message, Does.Contain(nameof(IServiceMessageContext.MaxByteStringLength)));
        }

        [Test]
        public void LimitOracleReportsAnArrayBeyondMaxArrayLength()
        {
            var nodes = new ReadValueId[FuzzableCode.FuzzMaxArrayLength + 1];
            for (int ii = 0; ii < nodes.Length; ii++)
            {
                nodes[ii] = new ReadValueId { NodeId = new NodeId((uint)ii), AttributeId = Attributes.Value };
            }
            var request = new ReadRequest { NodesToRead = nodes };

            ResourceBudgetException ex = Assert.Throws<ResourceBudgetException>(() => CheckLimits(request));

            Assert.That(ex.Message, Does.Contain(nameof(IServiceMessageContext.MaxArrayLength)));
        }

        [Test]
        public void LimitOracleReportsAStringInsideADataValue()
        {
            var response = new ReadResponse
            {
                Results = [new DataValue(Variant.From(new QualifiedName(LongString(), 2)))]
            };

            ResourceBudgetException ex = Assert.Throws<ResourceBudgetException>(() => CheckLimits(response));

            Assert.That(ex.Message, Does.Contain("Name"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void JsonDecoderRejectsANodeIdStringBeyondMaxStringLength(bool longNodeId)
        {
            // J1-1: the JSON string form of a NodeId was not checked against MaxStringLength.
            // The fuzz target now sees the decoder reject it instead of the limit oracle
            // flagging the decoded value.
            string json = EncodeJsonWithoutLimits(CreateReadRequest(
                longNodeId ? new NodeId(LongString(), 1) : new NodeId(1000, 1),
                longNodeId ? new QualifiedName("Name", 1) : new QualifiedName(LongString(), 1)));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => FuzzableCode.FuzzJsonDecoderCore(json, throwAll: true));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzJsonDecoder(Encoding.UTF8.GetBytes(json)));
        }

        [Test]
        public void LimitOracleFlagsTheSameValueFromADecoderWithoutTheCheck()
        {
            // Stands in for a decoder that does not enforce the limit: the permissive
            // context decodes the value, and the oracle judges it by the fuzz context.
            string json = EncodeJsonWithoutLimits(CreateReadRequest(new NodeId(LongString(), 1)));
            ServiceMessageContext permissive = CreatePermissiveContext();
            IEncodeable decoded;
            using (var decoder = new JsonDecoder(json, permissive))
            {
                decoded = decoder.DecodeMessage<IEncodeable>();
            }

            ResourceBudgetException ex = Assert.Throws<ResourceBudgetException>(() => CheckLimits(decoded));

            Assert.That(ex.Kind, Is.EqualTo(ResourceFindingKind.Limit));
        }

        [Test]
        public void BinaryDecoderRejectsANodeIdStringBeyondMaxStringLength()
        {
            byte[] message;
            using (var encoder = new BinaryEncoder(CreatePermissiveContext()))
            {
                encoder.EncodeMessage(CreateReadRequest(new NodeId(LongString(), 1)));
                message = encoder.CloseAndReturnBuffer();
            }

            using var stream = new MemoryStream(message);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => FuzzableCode.FuzzBinaryDecoderCore(stream, throwAll: true));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        private static void CheckLimits(object value)
        {
            FuzzOracles.CheckDecodedLimits(value, FuzzableCode.MessageContext, "test");
        }

        private static string LongString()
        {
            return new string('a', FuzzableCode.FuzzMaxStringLength + 1);
        }

        private static ReadRequest CreateReadRequest(NodeId nodeId, QualifiedName dataEncoding = default)
        {
            return new ReadRequest
            {
                NodesToRead =
                [
                    new ReadValueId
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        DataEncoding = dataEncoding
                    }
                ]
            };
        }

        private static ServiceMessageContext CreatePermissiveContext()
        {
            ServiceMessageContext context = FuzzableCode.CreateMessageContext();
            context.MaxStringLength = 0;
            context.MaxByteStringLength = 0;
            context.MaxArrayLength = 0;
            return context;
        }

        private static string EncodeJsonWithoutLimits(ReadRequest message)
        {
            using var stream = new MemoryStream();
            using (var encoder = new JsonEncoder(stream, CreatePermissiveContext()))
            {
                encoder.EncodeMessage(message, message.TypeId);
                encoder.Close();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
