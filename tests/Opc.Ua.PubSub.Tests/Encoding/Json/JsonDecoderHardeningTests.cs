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

using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Tests;

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// Regression tests for hostile JSON frames: the decoder must
    /// reject them through the diagnostics counters and never throw.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [TestSpec("7.2.5")]
    public sealed class JsonDecoderHardeningTests
    {
        [TestCase("{\"MessageType\":\"ua-data\",\"Messages\":[{\"DataSetWriterId\":1," +
            "\"MetaDataVersion\":{\"MajorVersion\":\"x\",\"MinorVersion\":[]}}]}")]
        [TestCase("{\"MessageType\":\"ua-data\",\"Messages\":[{\"Status\":{\"Code\":\"x\"}}]}")]
        [TestCase("{\"MessageType\":\"ua-status\",\"WriterConfiguration\":{}," +
            "\"DataSetWriterIds\":[\"x\"]}")]
        public void DecodeNonNumericHeaderValuesDoesNotThrow(string json)
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();

            Assert.DoesNotThrowAsync(async () => await DecodeAsync(json, ctx).ConfigureAwait(false));
        }

        [Test]
        public async Task DecodeFieldBeyondStringLimitCountsFailedDataSetMessageAsync()
        {
            string json =
                "{\"MessageType\":\"ua-data\",\"Messages\":[{\"DataSetWriterId\":1,\"Payload\":{" +
                "\"f\":{\"UaType\":12,\"Value\":\"" + new string('a', 70000) + "\"}}}]}";
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.Zero);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.FailedDataSetMessages),
                Is.EqualTo(1));
        }

        [Test]
        public async Task DecodeFrameAboveMaxMessageSizeIsRejectedAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            ((ServiceMessageContext)ctx.MessageContext).MaxMessageSize = 64;
            string json = "{\"MessageType\":\"ua-data\",\"MessageId\":\"" +
                new string('m', 64) + "\",\"Messages\":[]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Null);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages),
                Is.EqualTo(1));
        }

        [TestCase("{\"MessageType\":\"ua-data\",\"PublisherId\":\"0123456789abcdefX\",\"Messages\":[]}")]
        [TestCase("{\"MessageType\":\"ua-data\",\"MessageId\":\"0123456789abcdefX\",\"Messages\":[]}")]
        [TestCase("{\"MessageType\":\"ua-data\",\"ReplyTo\":[\"0123456789abcdefX\"],\"Messages\":[]}")]
        [TestCase("{\"MessageType\":\"ua-data\",\"ReplyTo\":[\"\",\"\",\"\",\"\",\"\"],\"Messages\":[]}")]
        [TestCase("{\"MessageType\":\"ua-data\",\"Messages\":[{},{},{},{},{}]}")]
        [TestCase("[{},{},{},{},{}]")]
        [TestCase("{\"MessageType\":\"ua-metadata\",\"PublisherId\":\"0123456789abcdefX\"," +
            "\"DataSetWriterId\":1,\"MetaData\":{}}")]
        public async Task DecodeEnvelopeAboveStringOrArrayLimitIsRejectedAsync(string json)
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var messageContext = (ServiceMessageContext)ctx.MessageContext;
            messageContext.MaxStringLength = 16;
            messageContext.MaxArrayLength = 4;

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Null);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages),
                Is.EqualTo(1));
        }

        [Test]
        public async Task DecodePayloadAboveMaxArrayLengthFailsDataSetMessageAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            ((ServiceMessageContext)ctx.MessageContext).MaxArrayLength = 4;
            const string json =
                "{\"MessageType\":\"ua-data\",\"Messages\":[{\"DataSetWriterId\":1,\"Payload\":{" +
                "\"a\":null,\"b\":null,\"c\":null,\"d\":null,\"e\":null}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.Zero);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.FailedDataSetMessages),
                Is.EqualTo(1));
        }

        [Test]
        public async Task DecodeVerboseFieldsWithinLimitsSucceedsAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            const string json =
                "{\"MessageType\":\"ua-data\",\"PublisherId\":\"P\",\"Messages\":[{\"DataSetWriterId\":1," +
                "\"Payload\":{\"a\":{\"UaType\":6,\"Value\":7},\"b\":{\"UaType\":12,\"Value\":\"x\"}}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.EqualTo(1));
            Assert.That(result.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(7)));
            Assert.That(result.DataSetMessages[0].Fields[1].Value, Is.EqualTo(new Variant("x")));
        }

        private static async Task<PubSubNetworkMessage?> DecodeAsync(
            string json,
            PubSubNetworkMessageContext ctx)
        {
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();
            return await decoder.TryDecodeAsync(Encoding.UTF8.GetBytes(json), ctx)
                .ConfigureAwait(false);
        }
    }
}
