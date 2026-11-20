// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
    /// Ensures the single-message mode emits the flat layout described
    /// in Annex A.3.3 and Part 14 §7.3.4.7.3 (no wrapping
    /// <c>Messages</c> array).
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [TestSpec("7.3.4.7.3")]
    [TestSpec("A.3.3")]
    public sealed class JsonSingleMessageModeTests
    {
        [Test]
        public async Task SingleMessageMode_OmitsMessagesArrayAsync()
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            var registry = new DataSetMetaDataRegistry();
            registry.Register(
                new DataSetMetaDataKey(PublisherId.FromUInt16(300), 0, 1, Uuid.Empty, 1),
                meta);
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext(registry);
            var dsm = new Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage
            {
                DataSetWriterId = 1,
                SequenceNumber = 1,
                MessageType = PubSubDataSetMessageType.KeyFrame,
                MetaDataVersion = meta.ConfigurationVersion,
                Fields = JsonTestUtilities.CreateFields()
            };
            var msg = new Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage
            {
                MessageId = "single-1",
                PublisherId = PublisherId.FromUInt16(300),
                DataSetClassId = Uuid.Empty,
                DataSetMessages = [dsm],
                SingleMessageMode = true
            };
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder
                .EncodeAsync(msg, ctx).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            Assert.That(root.TryGetProperty("Messages", out JsonElement messages), Is.True);
            Assert.That(messages.ValueKind, Is.EqualTo(JsonValueKind.Object),
                "Part 14 §7.2.5.3 SingleDataSetMessage uses an object instead of a Messages array.");
            Assert.That(root.GetProperty("MessageId").GetString(), Is.EqualTo("single-1"));
            Assert.That(root.GetProperty("MessageType").GetString(), Is.EqualTo(
                Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage.MessageTypeData));
            Assert.That(messages.TryGetProperty("DataSetWriterId", out JsonElement w), Is.True);
            Assert.That(w.GetUInt16(), Is.EqualTo(1));
            Assert.That(messages.TryGetProperty("Payload", out _), Is.True);
        }

        [Test]
        public async Task SingleMessageMode_RoundTripsAsync()
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            var registry = new DataSetMetaDataRegistry();
            registry.Register(
                new DataSetMetaDataKey(PublisherId.FromUInt16(300), 0, 1, Uuid.Empty, 1),
                meta);
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext(registry);
            var dsm = new Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage
            {
                DataSetWriterId = 1,
                SequenceNumber = 42,
                MessageType = PubSubDataSetMessageType.KeyFrame,
                MetaDataVersion = meta.ConfigurationVersion,
                Fields = JsonTestUtilities.CreateFields()
            };
            var msg = new Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage
            {
                MessageId = "single-rt",
                PublisherId = PublisherId.FromUInt16(300),
                DataSetClassId = Uuid.Empty,
                DataSetMessages = [dsm],
                SingleMessageMode = true
            };
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder
                .EncodeAsync(msg, ctx).ConfigureAwait(false);
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();
            PubSubNetworkMessage? decoded = await decoder
                .TryDecodeAsync(bytes, ctx).ConfigureAwait(false);
            Assert.That(decoded, Is.Not.Null);
            var asJson = decoded as Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
            Assert.That(asJson, Is.Not.Null);
            Assert.That(((PubSubDataSetMessage[]?)asJson!.DataSetMessages) ?? [], Has.Length.EqualTo(1));
            Assert.That(asJson.SingleMessageMode, Is.True);
        }

        [Test]
        public async Task SingleMessageMode_WrongPayload_ThrowsAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var msg = new Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage
            {
                MessageId = "bad-single",
                PublisherId = PublisherId.FromUInt16(300),
                DataSetMessages = [new ForeignDataSetMessage()],
                SingleMessageMode = true
            };
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder();
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await encoder.EncodeAsync(msg, ctx).ConfigureAwait(false));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private sealed record ForeignDataSetMessage : PubSubDataSetMessage
        {
        }
    }
}
