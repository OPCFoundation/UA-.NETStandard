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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Coverage for UADP action encoder/decoder.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.4.2")]
    [TestSpec("7.2.4.5.9")]
    [TestSpec("7.2.4.5.10")]
    public class UadpActionTests
    {
        [Test]
        public void ActionRequestRoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var request = new UadpActionRequestMessage
            {
                PublisherId = PublisherId.FromUInt16(0x4242),
                DataSetClassId = (Uuid)Guid.NewGuid(),
                DataSetWriterId = 0x1234,
                ActionTargetId = 0x0021,
                RequestId = 0x1001,
                ActionState = ActionState.Executing,
                ResponseAddress = "opc.udp://response",
                CorrelationData = ByteString.From(new byte[] { 1, 2, 3 }),
                RequestorId = new Variant("requestor-1"),
                TimeoutHint = 2500,
                Payload =
                [
                    new DataSetField
                    {
                        Name = "Input",
                        Value = new Variant(42),
                        Encoding = PubSubFieldEncoding.Variant
                    }
                ]
            };

            byte[] encoded = UadpActionCoder.Encode(request, context);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            Assert.That(decoded, Is.InstanceOf<UadpActionRequestMessage>());
            var decodedRequest = (UadpActionRequestMessage)decoded!;
            Assert.That(decodedRequest.PublisherId, Is.EqualTo(request.PublisherId));
            Assert.That(decodedRequest.DataSetClassId, Is.EqualTo(request.DataSetClassId));
            Assert.That(decodedRequest.DataSetWriterId, Is.EqualTo(0x1234));
            Assert.That(decodedRequest.ActionTargetId, Is.EqualTo(0x0021));
            Assert.That(decodedRequest.RequestId, Is.EqualTo(0x1001));
            Assert.That(decodedRequest.ActionState, Is.EqualTo(ActionState.Executing));
            Assert.That(decodedRequest.ResponseAddress, Is.EqualTo("opc.udp://response"));
            Assert.That(decodedRequest.CorrelationData.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(decodedRequest.RequestorId.TryGetValue(out string? requestorId), Is.True);
            Assert.That(requestorId, Is.EqualTo("requestor-1"));
            Assert.That(decodedRequest.TimeoutHint, Is.EqualTo(2500));
            Assert.That(decodedRequest.Payload.Count, Is.EqualTo(1));
            Assert.That(decodedRequest.Payload[0].Value.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
        }

        [Test]
        public void ActionResponseRoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var response = new UadpActionResponseMessage
            {
                PublisherId = PublisherId.FromString("responder"),
                DataSetWriterId = 0x77,
                ActionTargetId = 0x20,
                RequestId = 0x1002,
                ActionState = ActionState.Done,
                Status = StatusCodes.BadTimeout,
                CorrelationData = ByteString.From(new byte[] { 9, 8 }),
                RequestorId = new Variant("requestor-2"),
                Payload =
                [
                    new DataSetField
                    {
                        Name = "Output",
                        Value = new Variant("done"),
                        Encoding = PubSubFieldEncoding.Variant
                    }
                ]
            };

            byte[] encoded = UadpActionCoder.Encode(response, context);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            Assert.That(decoded, Is.InstanceOf<UadpActionResponseMessage>());
            var decodedResponse = (UadpActionResponseMessage)decoded!;
            Assert.That(decodedResponse.PublisherId, Is.EqualTo(response.PublisherId));
            Assert.That(decodedResponse.DataSetWriterId, Is.EqualTo(0x77));
            Assert.That(decodedResponse.ActionTargetId, Is.EqualTo(0x20));
            Assert.That(decodedResponse.RequestId, Is.EqualTo(0x1002));
            Assert.That(decodedResponse.ActionState, Is.EqualTo(ActionState.Done));
            Assert.That(decodedResponse.Status.Code, Is.EqualTo(StatusCodes.Good),
                "Part 14 v1.05.07 Table 167 has no UADP Status field in the response payload.");
            Assert.That(decodedResponse.CorrelationData.Span.ToArray(), Is.EqualTo(new byte[] { 9, 8 }));
            Assert.That(decodedResponse.RequestorId.TryGetValue(out string? requestorId), Is.True);
            Assert.That(requestorId, Is.EqualTo("requestor-2"));
            Assert.That(decodedResponse.Payload[0].Value.TryGetValue(out string? value), Is.True);
            Assert.That(value, Is.EqualTo("done"));
        }


        [TestCase(false, PubSubFieldEncoding.Variant)]
        [TestCase(false, PubSubFieldEncoding.RawData)]
        [TestCase(true, PubSubFieldEncoding.Variant)]
        [TestCase(true, PubSubFieldEncoding.RawData)]
        [TestSpec("7.2.4.5.9")]
        [TestSpec("7.2.4.5.10")]
        public void ActionPayloadRoundTripsAllowedFieldEncodings(
            bool response,
            PubSubFieldEncoding fieldEncoding)
        {
            DataSetMetaDataType metaData = CreateActionMetaData();
            var registry = new DataSetMetaDataRegistry();
            PublisherId publisherId = PublisherId.FromUInt16(0x55);
            registry.Register(
                new DataSetMetaDataKey(publisherId, 0, 0x33, Uuid.Empty, 0),
                metaData);
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext(
                registry,
                uadpActionFieldEncoding: fieldEncoding);
            PubSubNetworkMessage message = response
                ? CreateActionResponse(publisherId, fieldEncoding, metaData)
                : CreateActionRequest(publisherId, fieldEncoding, metaData);

            byte[] encoded = UadpActionCoder.Encode(message, context);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            if (response)
            {
                var decodedResponse = decoded as UadpActionResponseMessage;
                Assert.That(decodedResponse, Is.Not.Null);
                Assert.That(decodedResponse!.FieldEncoding, Is.EqualTo(fieldEncoding));
                Assert.That(decodedResponse.Payload[0].Value.TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(1234));
            }
            else
            {
                var decodedRequest = decoded as UadpActionRequestMessage;
                Assert.That(decodedRequest, Is.Not.Null);
                Assert.That(decodedRequest!.FieldEncoding, Is.EqualTo(fieldEncoding));
                Assert.That(decodedRequest.Payload[0].Value.TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(1234));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [TestSpec("7.2.4.5.9")]
        [TestSpec("7.2.4.5.10")]
        public void ActionPayloadRejectsDataValueFieldEncoding(bool response)
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            DataSetMetaDataType metaData = CreateActionMetaData();
            PubSubNetworkMessage message = response
                ? CreateActionResponse(PublisherId.FromUInt16(1), PubSubFieldEncoding.DataValue, metaData)
                : CreateActionRequest(PublisherId.FromUInt16(1), PubSubFieldEncoding.DataValue, metaData);

            Assert.That(() => UadpActionCoder.Encode(message, context),
                Throws.InvalidOperationException.And.Message.Contains("Variant or RawData"));
        }

        [Test]
        public async Task ActionRequestEncoderDispatchRoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var encoder = new UadpEncoder();
            var request = new UadpActionRequestMessage
            {
                PublisherId = PublisherId.FromByte(1),
                DataSetWriterId = 2,
                ActionTargetId = 3,
                RequestId = 4,
                ActionState = ActionState.Idle,
                TimeoutHint = 100
            };

            ReadOnlyMemory<byte> encoded = await encoder.EncodeAsync(request, context).ConfigureAwait(false);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            Assert.That(decoded, Is.InstanceOf<UadpActionRequestMessage>());
            Assert.That(((UadpActionRequestMessage)decoded!).RequestId, Is.EqualTo(4));
        }

        [Test]
        public void ActionRequestSecurityBoundaryStartsBeforeActionHeader()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var request = new UadpActionRequestMessage
            {
                PublisherId = PublisherId.FromByte(1),
                DataSetWriterId = 2,
                ActionTargetId = 3,
                RequestId = 4,
                ActionState = ActionState.Executing,
                TimeoutHint = 100,
                Payload =
                [
                    new DataSetField
                    {
                        Value = new Variant(1),
                        Encoding = PubSubFieldEncoding.Variant
                    }
                ]
            };

            ReadOnlyMemory<byte> encoded = UadpEncoder.EncodeWithSecurityBoundary(
                request, context, out int payloadOffset);

            Assert.That(payloadOffset, Is.GreaterThan(0));
            Assert.That(encoded.Span[1] & (byte)ExtendedFlags1EncodingMask.SecurityEnabled, Is.Not.Zero);
            Assert.That(encoded.Span[payloadOffset], Is.EqualTo((byte)(0x01 | 0x10)));
        }


        private static UadpActionRequestMessage CreateActionRequest(
            PublisherId publisherId,
            PubSubFieldEncoding fieldEncoding,
            DataSetMetaDataType metaData)
        {
            return new UadpActionRequestMessage
            {
                PublisherId = publisherId,
                DataSetWriterId = 0x33,
                ActionTargetId = 0x44,
                RequestId = 0x45,
                ActionState = ActionState.Executing,
                TimeoutHint = 1000,
                FieldEncoding = fieldEncoding,
                MetaData = metaData,
                Payload = CreateActionFields(fieldEncoding)
            };
        }

        private static UadpActionResponseMessage CreateActionResponse(
            PublisherId publisherId,
            PubSubFieldEncoding fieldEncoding,
            DataSetMetaDataType metaData)
        {
            return new UadpActionResponseMessage
            {
                PublisherId = publisherId,
                DataSetWriterId = 0x33,
                ActionTargetId = 0x44,
                RequestId = 0x45,
                ActionState = ActionState.Done,
                FieldEncoding = fieldEncoding,
                MetaData = metaData,
                Payload = CreateActionFields(fieldEncoding)
            };
        }

        private static ArrayOf<DataSetField> CreateActionFields(PubSubFieldEncoding fieldEncoding)
        {
            return
            [
                new DataSetField
                {
                    Name = "Value",
                    Value = new Variant(1234),
                    Encoding = fieldEncoding
                }
            ];
        }

        private static DataSetMetaDataType CreateActionMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "ActionPayload",
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Value",
                        BuiltInType = (byte)BuiltInType.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
        }

        [Test]
        public void ActionEncoderNullMessageThrows()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            Assert.That(() => UadpActionCoder.Encode(null!, context),
                Throws.ArgumentNullException);
        }
    }
}
