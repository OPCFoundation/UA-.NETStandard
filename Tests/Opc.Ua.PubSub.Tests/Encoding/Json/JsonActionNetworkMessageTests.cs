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
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Tests;
using PubSubJsonActionNetworkMessage = Opc.Ua.PubSub.Encoding.Json.JsonActionNetworkMessage;
using PubSubJsonDecoder = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;
using PubSubJsonEncoder = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// Round-trip coverage for the JSON Action NetworkMessage
    /// (<c>ua-action</c>) per Part 14 §7.2.5.6.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    public sealed class JsonActionNetworkMessageTests
    {
        [Test]
        [TestSpec("7.2.5.6.1")]
        public async Task EncodeActionRequestRoundTripsAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var request = new JsonActionRequestMessage
            {
                DataSetWriterId = 11,
                ActionTargetId = 22,
                DataSetWriterName = "Writer",
                WriterGroupName = "Group",
                MetaDataVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 2
                },
                MinorVersion = 3,
                Timestamp = new DateTime(2026, 6, 22, 8, 0, 0, DateTimeKind.Utc),
                MessageType = "ua-action-request",
                RequestId = 44,
                ActionState = ActionState.Executing,
                Payload = new ExtensionObject(CreatePayload("Speed", (byte)BuiltInType.Double))
            };
            var msg = new PubSubJsonActionNetworkMessage
            {
                MessageId = "act-1",
                PublisherId = PublisherId.FromString("publisher-1"),
                ResponseAddress = "mqtt://broker/responses",
                CorrelationData = new ByteString(new byte[] { 1, 2, 3, 4 }),
                RequestorId = "requestor-1",
                TimeoutHint = 12_000,
                Messages =
                [
                    new ExtensionObject(request)
                ]
            };
            var encoder = new PubSubJsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(msg, ctx)
                .ConfigureAwait(false);

            using (JsonDocument document = JsonDocument.Parse(bytes))
            {
                JsonElement root = document.RootElement;
                Assert.That(root.GetProperty("MessageType").GetString(), Is.EqualTo(
                    PubSubJsonActionNetworkMessage.MessageTypeActionRequest));
                Assert.That(root.GetProperty("ResponseAddress").GetString(),
                    Is.EqualTo("mqtt://broker/responses"));
                Assert.That(root.GetProperty("Messages").GetArrayLength(), Is.EqualTo(1));
            }

            var decoder = new PubSubJsonDecoder();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(bytes, ctx)
                .ConfigureAwait(false);

            var act = decoded as PubSubJsonActionNetworkMessage;
            Assert.That(act, Is.Not.Null);
            Assert.That(act!.NetworkMessage, Is.Not.Null);
            Assert.That(act.MessageId, Is.EqualTo("act-1"));
            Assert.That(act.ResponseAddress, Is.EqualTo("mqtt://broker/responses"));
            Assert.That(act.CorrelationData, Is.EqualTo(
                new ByteString(new byte[] { 1, 2, 3, 4 })));
            Assert.That(act.RequestorId, Is.EqualTo("requestor-1"));
            Assert.That(act.TimeoutHint, Is.EqualTo(12_000));
            Assert.That(act.Messages, Has.Count.EqualTo(1));
            Assert.That(act.Messages[0].TryGetValue(out IEncodeable? first), Is.True);
            Assert.That(first, Is.TypeOf<JsonActionRequestMessage>());
            var roundTripRequest = (JsonActionRequestMessage)first!;
            Assert.That(roundTripRequest.RequestId, Is.EqualTo(44));
            Assert.That(roundTripRequest.ActionTargetId, Is.EqualTo(22));
            Assert.That(roundTripRequest.ActionState, Is.EqualTo(ActionState.Executing));
            AssertPayload(roundTripRequest.Payload, "Speed");

        }

        [Test]
        [TestSpec("7.2.5.6.3")]
        public async Task EncodeActionResponseUsesResponseMessageTypeAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var response = new JsonActionResponseMessage
            {
                DataSetWriterId = 11,
                ActionTargetId = 22,
                Status = StatusCodes.BadTimeout,
                MessageType = "ua-action-response",
                RequestId = 44,
                ActionState = ActionState.Done,
                Payload = new ExtensionObject(CreatePayload("Result", (byte)BuiltInType.String))
            };
            var msg = new PubSubJsonActionNetworkMessage
            {
                MessageId = "act-response-1",
                PublisherId = PublisherId.FromString("publisher-1"),
                RequestorId = "requestor-1",
                Messages = [new ExtensionObject(response)]
            };
            var encoder = new PubSubJsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(msg, ctx)
                .ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(bytes);
            Assert.That(document.RootElement.GetProperty("MessageType").GetString(),
                Is.EqualTo(PubSubJsonActionNetworkMessage.MessageTypeActionResponse));

            var decoder = new PubSubJsonDecoder();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(bytes, ctx)
                .ConfigureAwait(false);

            var act = decoded as PubSubJsonActionNetworkMessage;
            Assert.That(act, Is.Not.Null);
            Assert.That(act!.Messages, Has.Count.EqualTo(1));
            Assert.That(act.Messages[0].TryGetValue(out IEncodeable? body), Is.True);
            Assert.That(body, Is.TypeOf<JsonActionResponseMessage>());
            var roundTripResponse = (JsonActionResponseMessage)body!;
            Assert.That(roundTripResponse.Status, Is.EqualTo(StatusCodes.BadTimeout));
            AssertPayload(roundTripResponse.Payload, "Result");
        }

        [Test]
        [TestSpec("7.2.5.6.3")]
        public async Task EncodeActionMetaDataRoundTripsAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            DataSetMetaDataType requestMetaData = JsonTestUtilities.CreateMetaData("ActionRequest");
            DataSetMetaDataType responseMetaData = JsonTestUtilities.CreateMetaData("ActionResponse");
            var message = new PubSubJsonActionNetworkMessage
            {
                MetaDataMessage = new JsonActionMetaDataMessage
                {
                    MessageId = "action-md-1",
                    PublisherId = "publisher-1",
                    DataSetWriterId = 9,
                    DataSetWriterName = "ActionWriter",
                    Timestamp = new DateTime(2026, 6, 22, 8, 1, 0, DateTimeKind.Utc),
                    Request = requestMetaData,
                    Response = responseMetaData,
                    ActionTargets =
                    [
                        new ActionTargetDataType
                        {
                            ActionTargetId = 22,
                            Name = "Target"
                        }
                    ],
                    ActionMethods =
                    [
                        new ActionMethodDataType
                        {
                            ObjectId = new NodeId(Objects.Server),
                            MethodId = new NodeId(Methods.Server_GetMonitoredItems)
                        }
                    ]
                }
            };
            var encoder = new PubSubJsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(message, ctx)
                .ConfigureAwait(false);

            var decoder = new PubSubJsonDecoder();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(bytes, ctx)
                .ConfigureAwait(false);

            var action = decoded as PubSubJsonActionNetworkMessage;
            Assert.That(action, Is.Not.Null);
            Assert.That(action!.MetaDataMessage, Is.Not.Null);
            Assert.That(action.MetaDataMessage!.MessageType, Is.EqualTo(
                PubSubJsonActionNetworkMessage.MessageTypeActionMetaData));
            Assert.That(action.MetaDataMessage.DataSetWriterId, Is.EqualTo(9));
            Assert.That(action.MetaDataMessage.Request.Name, Is.EqualTo("ActionRequest"));
            Assert.That(action.MetaDataMessage.Response.Name, Is.EqualTo("ActionResponse"));
            Assert.That(action.MetaDataMessage.ActionTargets, Has.Count.EqualTo(1));
            Assert.That(action.MetaDataMessage.ActionMethods, Has.Count.EqualTo(1));
        }

        [Test]
        [TestSpec("7.2.5.6.1")]
        public async Task DecodeMissingMessagesRejectsAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            ReadOnlyMemory<byte> bytes = System.Text.Encoding.UTF8.GetBytes(
                "{\"MessageType\":\"ua-action-request\",\"Messages\":[]}");
            var decoder = new PubSubJsonDecoder();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(bytes, ctx)
                .ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        [TestSpec("7.2.5.6.1")]
        public void EncodeMissingMessagesRejects()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var msg = new PubSubJsonActionNetworkMessage
            {
                MessageId = "act-bad",
                PublisherId = PublisherId.FromUInt16(0x100)
            };
            var encoder = new PubSubJsonEncoder();

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await encoder.EncodeAsync(msg, ctx).ConfigureAwait(false));
        }

        private static FieldMetaData CreatePayload(string name, byte builtInType)
        {
            return new FieldMetaData
            {
                Name = name,
                BuiltInType = builtInType,
                ValueRank = ValueRanks.Scalar
            };
        }

        private static void AssertPayload(ExtensionObject payload, string expectedName)
        {
            Assert.That(payload.IsNull, Is.False);
            if (payload.TryGetValue(out IEncodeable? body))
            {
                Assert.That(body, Is.TypeOf<FieldMetaData>());
                var field = (FieldMetaData)body!;
                Assert.That(field.Name, Is.EqualTo(expectedName));
                return;
            }

            Assert.That(payload.TryGetAsJson(out string? json), Is.True);
            Assert.That(json, Does.Contain(expectedName));
        }
    }
}
