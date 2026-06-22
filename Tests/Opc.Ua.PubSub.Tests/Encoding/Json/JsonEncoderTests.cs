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
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Tests;

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// JSON encoder fixture exercising every Part 6 §5.4.1 encoding
    /// profile (Verbose, Compact, RawData) and every
    /// <see cref="PubSubDataSetMessageType"/> (KeyFrame, DeltaFrame,
    /// Event, KeepAlive) used by Part 14 §7.2.5 (v1.05.06).
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [TestSpec("7.2.5.3")]
    [TestSpec("7.2.5.4")]
    public sealed class JsonEncoderTests
    {
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Verbose)]
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Compact)]
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.RawData)]
        public async Task EncodeKeyFrameAsync(
            Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode mode)
        {
            await EncodeAndAssertEnvelopeAsync(mode, PubSubDataSetMessageType.KeyFrame)
                .ConfigureAwait(false);
        }

        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Verbose)]
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Compact)]
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.RawData)]
        public async Task EncodeDeltaFrameAsync(
            Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode mode)
        {
            await EncodeAndAssertEnvelopeAsync(mode, PubSubDataSetMessageType.DeltaFrame)
                .ConfigureAwait(false);
        }

        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Verbose)]
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Compact)]
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.RawData)]
        public async Task EncodeEventAsync(
            Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode mode)
        {
            await EncodeAndAssertEnvelopeAsync(mode, PubSubDataSetMessageType.Event)
                .ConfigureAwait(false);
        }

        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Verbose)]
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Compact)]
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.RawData)]
        public async Task EncodeKeepAliveAsync(
            Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode mode)
        {
            await EncodeAndAssertEnvelopeAsync(mode, PubSubDataSetMessageType.KeepAlive)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task EncodeAsync_NullMessage_ThrowsAsync()
        {
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder();
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await encoder.EncodeAsync(null!, ctx).ConfigureAwait(false));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task EncodeAsync_NullContext_ThrowsAsync()
        {
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder();
            var msg = new Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage();
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await encoder.EncodeAsync(msg, null!).ConfigureAwait(false));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task EncodeAsync_WrongMessageType_ThrowsAsync()
        {
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder();
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var foreign = new ForeignNetworkMessage();
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await encoder.EncodeAsync(foreign, ctx).ConfigureAwait(false));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public void Encoder_Defaults_ExposeJsonProfile()
        {
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder();
            Assert.That(encoder.TransportProfileUri, Is.EqualTo(Profiles.PubSubMqttJsonTransport));
            Assert.That(encoder.EstimatedHeaderOverhead, Is.EqualTo(256));
            Assert.That(encoder.Mode, Is.EqualTo(
                Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Verbose));
        }

        private static async Task EncodeAndAssertEnvelopeAsync(
            Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode mode,
            PubSubDataSetMessageType type)
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            var registry = new DataSetMetaDataRegistry();
            registry.Register(
                new DataSetMetaDataKey(PublisherId.FromUInt16(300), 1, 1, Uuid.Empty, 1),
                meta);
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext(registry);
            var dsm = new Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage
            {
                DataSetWriterId = 1,
                SequenceNumber = 7,
                MessageType = type,
                MetaDataVersion = meta.ConfigurationVersion,
                Fields = type == PubSubDataSetMessageType.KeepAlive
                    ? []
                    : JsonTestUtilities.CreateFields(),
                MessageTypeName = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessageType
                    .ToWireString(type)
            };
            var message = new Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage
            {
                MessageId = "msg-1",
                PublisherId = PublisherId.FromUInt16(300),
                DataSetClassId = Uuid.Empty,
                DataSetMessages = [dsm]
            };
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder(mode);
            ReadOnlyMemory<byte> bytes = await encoder
                .EncodeAsync(message, ctx).ConfigureAwait(false);
            Assert.That(bytes.IsEmpty, Is.False);
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            Assert.That(root.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(root.GetProperty("MessageId").GetString(), Is.EqualTo("msg-1"));
            Assert.That(root.GetProperty("MessageType").GetString(), Is.EqualTo(
                Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage.MessageTypeData));
            Assert.That(root.TryGetProperty("Messages", out JsonElement msgs), Is.True);
            Assert.That(msgs.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(msgs.GetArrayLength(), Is.EqualTo(1));
            JsonElement only = msgs[0];
            Assert.That(only.GetProperty("DataSetWriterId").GetUInt16(), Is.EqualTo(1));
            Assert.That(only.GetProperty("MessageType").GetString(), Is.EqualTo(
                Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessageType.ToWireString(type)));
            if (type != PubSubDataSetMessageType.KeepAlive)
            {
                Assert.That(only.TryGetProperty("Payload", out JsonElement payload), Is.True);
                Assert.That(payload.ValueKind, Is.EqualTo(JsonValueKind.Object));
                Assert.That(payload.TryGetProperty("BoolField", out _), Is.True);
            }
            else
            {
                Assert.That(only.TryGetProperty("Payload", out _), Is.False,
                    "Part 14 §7.2.5.4.1 keep-alive DataSetMessages shall have no Payload field.");
            }
        }

        [Test]
        [TestSpec("7.2.5.3")]
        [TestSpec("7.2.5.4.1")]
        public async Task HeaderSuppressionEmitsBarePayloadObjectAsync()
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var dsm = new Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage
            {
                ContentMask = JsonDataSetMessageContentMask.None,
                Fields = JsonTestUtilities.CreateFields()
            };
            var message = new Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage
            {
                ContentMask = JsonNetworkMessageContentMask.SingleDataSetMessage,
                SingleMessageMode = true,
                MetaData = meta,
                DataSetMessages = [dsm]
            };
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder
                .EncodeAsync(message, ctx).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            Assert.That(root.TryGetProperty("MessageId", out _), Is.False);
            Assert.That(root.TryGetProperty("MessageType", out _), Is.False);
            Assert.That(root.TryGetProperty("Payload", out _), Is.False);
            Assert.That(root.TryGetProperty("BoolField", out _), Is.True);
        }

        [Test]
        [TestSpec("7.2.5.3")]
        [TestSpec("7.2.5.4.1")]
        public async Task OptionalNamesAndDataSetPublisherIdEmitByMaskAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var dsm = new Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage
            {
                ContentMask = JsonDataSetMessageContentMask.DataSetWriterName
                    | JsonDataSetMessageContentMask.PublisherId
                    | JsonDataSetMessageContentMask.WriterGroupName
                    | JsonDataSetMessageContentMask.MessageType,
                DataSetWriterName = "WriterA",
                PublisherId = PublisherId.FromString("publisher-dsm"),
                WriterGroupName = "GroupA",
                Fields = []
            };
            var message = new Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage
            {
                ContentMask = JsonNetworkMessageContentMask.DataSetMessageHeader
                    | JsonNetworkMessageContentMask.SingleDataSetMessage
                    | JsonNetworkMessageContentMask.WriterGroupName,
                WriterGroupName = string.Empty,
                DataSetMessages = [dsm]
            };
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder
                .EncodeAsync(message, ctx).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement dsmJson = document.RootElement;
            Assert.That(dsmJson.GetProperty("DataSetWriterName").GetString(), Is.EqualTo("WriterA"));
            Assert.That(dsmJson.GetProperty("PublisherId").GetString(), Is.EqualTo("publisher-dsm"));
            Assert.That(dsmJson.GetProperty("WriterGroupName").GetString(), Is.EqualTo("GroupA"));
        }

        private sealed record ForeignNetworkMessage : PubSubNetworkMessage
        {
            public override string TransportProfileUri => "urn:test";
        }
    }
}
