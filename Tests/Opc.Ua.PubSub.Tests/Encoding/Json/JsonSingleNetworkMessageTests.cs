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
using JsonDataSetMessage = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using JsonDecoder = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;
using JsonEncoder = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;
using JsonNetworkMessage = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// Runtime enforcement coverage for the JSON
    /// <c>SingleDataSetMessage</c> mode (Part 14 §7.2.5.4.5,
    /// §7.3.4.7.3, Annex A.3.3).
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [TestSpec("7.2.5.4.5")]
    [TestSpec("7.3.4.7.3")]
    [TestSpec("A.3.3")]
    public sealed class JsonSingleNetworkMessageTests
    {
        [Test]
        [TestSpec("A.3.3")]
        public async Task Encode_SingleNetworkMessage_OmitsEnvelopeWrapperAsync()
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            var registry = new DataSetMetaDataRegistry();
            registry.Register(
                new DataSetMetaDataKey(PublisherId.FromUInt16(700), 0, 1, Uuid.Empty, 1),
                meta);
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext(registry);
            var dsm = new JsonDataSetMessage
            {
                DataSetWriterId = 1,
                SequenceNumber = 11,
                MessageType = PubSubDataSetMessageType.KeyFrame,
                MetaDataVersion = meta.ConfigurationVersion,
                Fields = JsonTestUtilities.CreateFields()
            };
            var msg = new JsonNetworkMessage
            {
                MessageId = "single-envelope",
                PublisherId = PublisherId.FromUInt16(700),
                DataSetMessages = [dsm],
                SingleMessageMode = true
            };
            var encoder = new JsonEncoder();

            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(msg, ctx)
                .ConfigureAwait(false);

            using JsonDocument doc = JsonDocument.Parse(bytes);
            JsonElement root = doc.RootElement;
            Assert.That(root.TryGetProperty("Messages", out JsonElement messages), Is.True);
            Assert.That(messages.ValueKind, Is.EqualTo(JsonValueKind.Object),
                "Part 14 §7.2.5.3 SingleDataSetMessage uses an object instead of an array.");
            Assert.That(messages.TryGetProperty("Payload", out _), Is.True);
            Assert.That(messages.TryGetProperty("DataSetWriterId", out JsonElement w), Is.True);
            Assert.That(w.GetUInt16(), Is.EqualTo(1));
        }

        [Test]
        [TestSpec("7.3.4.7.3")]
        public Task Encode_SingleNetworkMessage_RejectsMultipleMessagesAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var dsm1 = new JsonDataSetMessage { DataSetWriterId = 1 };
            var dsm2 = new JsonDataSetMessage { DataSetWriterId = 2 };
            var msg = new JsonNetworkMessage
            {
                MessageId = "single-too-many",
                PublisherId = PublisherId.FromUInt16(700),
                DataSetMessages = [dsm1, dsm2],
                SingleMessageMode = true
            };
            var encoder = new JsonEncoder();

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await encoder.EncodeAsync(msg, ctx).ConfigureAwait(false));
            return Task.CompletedTask;
        }

        [Test]
        [TestSpec("7.2.5.4.5")]
        public Task Encode_SingleNetworkMessage_RejectsZeroMessagesAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var msg = new JsonNetworkMessage
            {
                MessageId = "single-empty",
                PublisherId = PublisherId.FromUInt16(700),
                DataSetMessages = [],
                SingleMessageMode = true
            };
            var encoder = new JsonEncoder();

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await encoder.EncodeAsync(msg, ctx).ConfigureAwait(false));
            return Task.CompletedTask;
        }

        [Test]
        [TestSpec("A.3.3")]
        public async Task Decode_SingleNetworkMessage_RecognisesBareDataSetAsync()
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            var registry = new DataSetMetaDataRegistry();
            registry.Register(
                new DataSetMetaDataKey(PublisherId.FromUInt16(700), 0, 1, Uuid.Empty, 1),
                meta);
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext(registry);
            var dsm = new JsonDataSetMessage
            {
                DataSetWriterId = 1,
                SequenceNumber = 99,
                MessageType = PubSubDataSetMessageType.KeyFrame,
                MetaDataVersion = meta.ConfigurationVersion,
                Fields = JsonTestUtilities.CreateFields()
            };
            var msg = new JsonNetworkMessage
            {
                MessageId = "single-bare",
                PublisherId = PublisherId.FromUInt16(700),
                DataSetMessages = [dsm],
                SingleMessageMode = true
            };
            var encoder = new JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(msg, ctx)
                .ConfigureAwait(false);

            var decoder = new JsonDecoder();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(bytes, ctx)
                .ConfigureAwait(false);

            Assert.That(decoded, Is.Not.Null);
            var asJson = decoded as JsonNetworkMessage;
            Assert.That(asJson, Is.Not.Null);
            Assert.That(asJson!.SingleMessageMode, Is.True);
            Assert.That(((PubSubDataSetMessage[]?)asJson.DataSetMessages) ?? [], Has.Length.EqualTo(1));
        }

        [Test]
        [TestSpec("7.2.5.4.5")]
        public async Task RoundTrip_SingleNetworkMessage_RehydratesViaRegistryAsync()
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData("Boiler-RT");
            var registry = new DataSetMetaDataRegistry();
            registry.Register(
                new DataSetMetaDataKey(PublisherId.FromUInt16(815), 0, 7, Uuid.Empty, 1),
                meta);
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext(registry);
            var dsm = new JsonDataSetMessage
            {
                DataSetWriterId = 7,
                SequenceNumber = 21,
                MessageType = PubSubDataSetMessageType.KeyFrame,
                MetaDataVersion = meta.ConfigurationVersion,
                Fields = JsonTestUtilities.CreateFields()
            };
            var msg = new JsonNetworkMessage
            {
                MessageId = "single-rt-meta",
                PublisherId = PublisherId.FromUInt16(815),
                DataSetMessages = [dsm],
                SingleMessageMode = true
            };
            var encoder = new JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(msg, ctx)
                .ConfigureAwait(false);

            var decoder = new JsonDecoder();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(bytes, ctx)
                .ConfigureAwait(false);

            var asJson = decoded as JsonNetworkMessage;
            Assert.That(asJson, Is.Not.Null);
            JsonDataSetMessage rt = (JsonDataSetMessage)asJson!.DataSetMessages[0];
            Assert.That(rt.DataSetWriterId, Is.EqualTo(7));
            Assert.That(((DataSetField[]?)rt.Fields) ?? [], Has.Length.EqualTo(3));
        }
    }
}
