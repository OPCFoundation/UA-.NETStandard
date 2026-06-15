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

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// Validates encode/decode of <c>ua-metadata</c> messages
    /// described by Part 14 §7.2.5.5 (JsonDataSetMetaDataMessage).
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [TestSpec("7.2.5.5")]
    [TestSpec("7.2.5.5.2")]
    public sealed class JsonMetaDataMessageTests
    {
        [Test]
        public async Task EncodeAsync_EmitsUaMetadataEnvelopeAsync()
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var msg = new Opc.Ua.PubSub.Encoding.Json.JsonMetaDataMessage
            {
                MessageId = "meta-1",
                PublisherId = PublisherId.FromUInt16(7),
                DataSetWriterId = 3,
                DataSetClassId = new Uuid(new Guid(
                    "11112222-3333-4444-5555-666677778888")),
                MetaDataPayload = meta
            };
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder
                .EncodeAsync(msg, ctx).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("MessageId").GetString(), Is.EqualTo("meta-1"));
            Assert.That(root.GetProperty("MessageType").GetString(), Is.EqualTo(
                Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage.MessageTypeMetaData));
            Assert.That(root.TryGetProperty("MetaData", out JsonElement md), Is.True);
            Assert.That(md.ValueKind, Is.Not.EqualTo(JsonValueKind.Null));
            Assert.That(root.TryGetProperty("DataSetWriterId", out JsonElement dw), Is.True);
            Assert.That(dw.GetUInt16(), Is.EqualTo(3));
        }

        [Test]
        public async Task RoundTrip_MetaDataMessageAsync()
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData("Roundtrip");
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var msg = new Opc.Ua.PubSub.Encoding.Json.JsonMetaDataMessage
            {
                MessageId = "meta-rt",
                PublisherId = PublisherId.FromUInt16(7),
                DataSetWriterId = 9,
                DataSetClassId = new Uuid(new Guid(
                    "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE")),
                MetaDataPayload = meta
            };
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder
                .EncodeAsync(msg, ctx).ConfigureAwait(false);
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();
            PubSubNetworkMessage? decoded = await decoder
                .TryDecodeAsync(bytes, ctx).ConfigureAwait(false);
            Assert.That(decoded, Is.Not.Null);
            var asMeta = decoded as Opc.Ua.PubSub.Encoding.Json.JsonMetaDataMessage;
            Assert.That(asMeta, Is.Not.Null);
            Assert.That(asMeta!.DataSetWriterId, Is.EqualTo(9));
            Assert.That(asMeta.MessageId, Is.EqualTo("meta-rt"));
            Assert.That(asMeta.PublisherId.IsNull, Is.False);
            Assert.That(asMeta.MetaDataPayload ?? asMeta.MetaData, Is.Not.Null);
        }

        [Test]
        public async Task Encode_MissingPayload_ThrowsAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var msg = new Opc.Ua.PubSub.Encoding.Json.JsonMetaDataMessage
            {
                MessageId = "no-payload",
                PublisherId = PublisherId.FromUInt16(300)
            };
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder();
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await encoder.EncodeAsync(msg, ctx).ConfigureAwait(false));
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}
