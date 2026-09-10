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
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Tests;

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// Decoder robustness for malformed / partial JSON envelopes.
    /// Every case must result in <see langword="null"/> rather than an
    /// exception.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [TestSpec("7.2.5.3")]
    public sealed class JsonMalformedInputTests
    {
        [Test]
        public async Task TruncatedJson_ReturnsNullAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();
            PubSubNetworkMessage? result = await decoder.TryDecodeAsync(
                Encoding.UTF8.GetBytes("{\"MessageType\":\"ua-da"),
                ctx).ConfigureAwait(false);
            Assert.That(result, Is.Null);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages),
                Is.GreaterThan(0));
        }

        [Test]
        public async Task EmptyInput_ReturnsNullAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();
            PubSubNetworkMessage? result = await decoder.TryDecodeAsync(
                ReadOnlyMemory<byte>.Empty,
                ctx).ConfigureAwait(false);
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task NonObjectRoot_ReturnsNullAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();
            PubSubNetworkMessage? result = await decoder.TryDecodeAsync(
                Encoding.UTF8.GetBytes("[1,2,3]"),
                ctx).ConfigureAwait(false);
            Assert.That(result, Is.Null);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages),
                Is.GreaterThan(0));
        }

        [Test]
        public async Task MissingMessageType_ReturnsNullAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();
            PubSubNetworkMessage? result = await decoder.TryDecodeAsync(
                Encoding.UTF8.GetBytes("{\"MessageId\":\"x\"}"),
                ctx).ConfigureAwait(false);
            Assert.That(result, Is.Null);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages),
                Is.GreaterThan(0));
        }

        [Test]
        public async Task UnsupportedMessageType_ReturnsNullAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();
            PubSubNetworkMessage? result = await decoder.TryDecodeAsync(
                Encoding.UTF8.GetBytes(
                    "{\"MessageId\":\"x\",\"MessageType\":\"ua-other\"}"),
                ctx).ConfigureAwait(false);
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task MessageTypeNotString_ReturnsNullAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();
            PubSubNetworkMessage? result = await decoder.TryDecodeAsync(
                Encoding.UTF8.GetBytes(
                    "{\"MessageId\":\"x\",\"MessageType\":123}"),
                ctx).ConfigureAwait(false);
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task MalformedVerboseFieldValue_ReturnsNetworkMessageWithoutDataSetMessageAsync()
        {
            const string json =
                "{\"MessageType\":\"ua-data\",\"Messages\":[{\"DataSetWriterId\":1,\"Payload\":{" +
                "\"Running\":{\"UaType\":1,\"Valueoseconds\":10,\"ServerTimestamp\":\"2026-06-15T12:00:01Z\"}" +
                "}}]}";
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();

            PubSubNetworkMessage? result = await decoder.TryDecodeAsync(
                Encoding.UTF8.GetBytes(json),
                ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.Zero);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.FailedDataSetMessages),
                Is.GreaterThan(0));
        }

        [Test]
        public async Task ValidVerboseFieldValue_DecodesAsync()
        {
            const string json =
                "{\"MessageType\":\"ua-data\",\"Messages\":[{\"DataSetWriterId\":1,\"Payload\":{" +
                "\"Running\":{\"UaType\":1,\"Value\":true,\"ServerTimestamp\":\"2026-06-15T12:00:01Z\"}" +
                "}}]}";
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();

            PubSubNetworkMessage? result = await decoder.TryDecodeAsync(
                Encoding.UTF8.GetBytes(json),
                ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.EqualTo(1));
            Assert.That(result.DataSetMessages[0].Fields, Has.Count.EqualTo(1));
            Assert.That(result.DataSetMessages[0].Fields[0].Value.TryGetValue(out bool running), Is.True);
            Assert.That(running, Is.True);
        }

        [Test]
        public async Task InvalidUtf8InJsonFieldName_ReturnsNullAsync()
        {
            byte[] prefix = Encoding.UTF8.GetBytes(
                "{\"MessageType\":\"ua-data\",\"Messages\":[{\"DataSetWriterId\":1,\"Payload\":{\"");
            byte[] suffix = Encoding.UTF8.GetBytes("\":{\"UaType\":1,\"Value\":true}}}]}");
            byte[] frame = [.. prefix, 0xFF, .. suffix];
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();

            PubSubNetworkMessage? result = await decoder.TryDecodeAsync(
                frame,
                ctx).ConfigureAwait(false);

            Assert.That(result, Is.Null);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages),
                Is.GreaterThan(0));
        }

        [Test]
        public async Task Utf8JsonFieldName_DecodesAsync()
        {
            const string json =
                "{\"MessageType\":\"ua-data\",\"Messages\":[{\"DataSetWriterId\":1,\"Payload\":{" +
                "\"Running\":{\"UaType\":1,\"Value\":true}" +
                "}}]}";
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();

            PubSubNetworkMessage? result = await decoder.TryDecodeAsync(
                Encoding.UTF8.GetBytes(json),
                ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.EqualTo(1));
            Assert.That(result.DataSetMessages[0].Fields, Has.Count.EqualTo(1));
        }

        [Test]
        public void PublicDecodeFieldsStillSurfacesMalformedPayloads()
        {
            // Frame decoding tolerates a malformed field, but the public helper must not
            // silently return an empty set and hide the failure from its own callers.
            using JsonDocument document = JsonDocument.Parse(
                "{\"Broken\":{\"UaType\":1,\"Value\":\"not-a-boolean\"}}");
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();

            Assert.That(
                () => JsonFieldDecoder.DecodeFields(
                    document.RootElement,
                    metaData: null,
                    JsonEncodingMode.Verbose,
                    ctx.MessageContext),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadDecodingError));
        }

        [Test]
        public void PublicDecodeFieldsDecodesAWellFormedPayload()
        {
            using JsonDocument document = JsonDocument.Parse(
                "{\"Running\":{\"UaType\":1,\"Value\":true}}");
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();

            ArrayOf<DataSetField> fields = JsonFieldDecoder.DecodeFields(
                document.RootElement,
                metaData: null,
                JsonEncodingMode.Verbose,
                ctx.MessageContext);

            Assert.That(fields, Has.Count.EqualTo(1));
            Assert.That(fields[0].Value.TryGetValue(out bool running), Is.True);
            Assert.That(running, Is.True);
        }
    }
}
