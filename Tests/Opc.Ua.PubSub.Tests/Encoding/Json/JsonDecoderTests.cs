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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Tests;

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// Inverse of <c>JsonEncoderTests</c> — every mode and every
    /// DataSetMessage kind must round-trip cleanly when the metadata
    /// is registered.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [TestSpec("7.2.5.3")]
    [TestSpec("7.2.5.4")]
    public sealed class JsonDecoderTests
    {
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Verbose,
            PubSubDataSetMessageType.KeyFrame)]
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Verbose,
            PubSubDataSetMessageType.DeltaFrame)]
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Verbose,
            PubSubDataSetMessageType.Event)]
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Verbose,
            PubSubDataSetMessageType.KeepAlive)]
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Compact,
            PubSubDataSetMessageType.KeyFrame)]
        [TestCase(Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.RawData,
            PubSubDataSetMessageType.KeyFrame)]
        public async Task RoundTripAsync(
            Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode mode,
            PubSubDataSetMessageType type)
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
                SequenceNumber = 99,
                MessageType = type,
                MetaDataVersion = meta.ConfigurationVersion,
                Fields = type == PubSubDataSetMessageType.KeepAlive
                    ? []
                    : JsonTestUtilities.CreateFields()
            };
            var message = new Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage
            {
                MessageId = "rt-1",
                PublisherId = PublisherId.FromUInt16(300),
                DataSetClassId = Uuid.Empty,
                DataSetMessages = [dsm]
            };
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder(mode);
            ReadOnlyMemory<byte> bytes = await encoder
                .EncodeAsync(message, ctx).ConfigureAwait(false);
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();
            PubSubNetworkMessage? decoded = await decoder
                .TryDecodeAsync(bytes, ctx).ConfigureAwait(false);
            Assert.That(decoded, Is.Not.Null, $"Decoder returned null for mode={mode} type={type}");
            var data = decoded as Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
            Assert.That(data, Is.Not.Null);
            Assert.That(data!.MessageId, Is.EqualTo("rt-1"));
            Assert.That(data.PublisherId.IsNull, Is.False);
            Assert.That(((PubSubDataSetMessage[]?)data.DataSetMessages) ?? [], Has.Length.EqualTo(1),
                $"Expected exactly one decoded DataSetMessage for mode={mode} type={type}; got {data.DataSetMessages.Count}");
            var receivedDsm = data.DataSetMessages[0]
                as Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
            Assert.That(receivedDsm, Is.Not.Null);
            Assert.That(receivedDsm!.DataSetWriterId, Is.EqualTo(1));
            Assert.That(receivedDsm.SequenceNumber, Is.EqualTo(99));
            Assert.That(receivedDsm.MessageType, Is.EqualTo(type));
            if (type != PubSubDataSetMessageType.KeepAlive
                && mode == Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Verbose)
            {
                Assert.That(((DataSetField[]?)receivedDsm.Fields) ?? [], Has.Length.EqualTo(3));
            }
        }

        [Test]
        public void Decoder_Defaults_ExposeJsonProfile()
        {
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();
            Assert.That(decoder.TransportProfileUri,
                Is.EqualTo(Profiles.PubSubMqttJsonTransport));
        }

        [Test]
        public async Task TryDecodeAsync_NullContext_ThrowsAsync()
        {
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await decoder.TryDecodeAsync(
                    new ReadOnlyMemory<byte>([1, 2, 3]),
                    null!).ConfigureAwait(false));
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}
