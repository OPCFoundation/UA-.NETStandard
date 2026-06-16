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
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Tests;
using JsonActionNetworkMessage = Opc.Ua.PubSub.Encoding.Json.JsonActionNetworkMessage;
using JsonDecoder = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;
using JsonEncoder = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// Round-trip coverage for the JSON Action NetworkMessage
    /// (<c>ua-action</c>) per Part 14 §7.2.5.6 (sub-task 16e).
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    public sealed class JsonActionNetworkMessageTests
    {
        [Test]
        [TestSpec("7.2.5.6.1")]
        public async Task Encode_Request_RoundTripsAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var msg = new JsonActionNetworkMessage
            {
                MessageId = "act-req-1",
                PublisherId = PublisherId.FromUInt16(0x100),
                Action = "urn:test:action:start",
                RequestId = "req-1",
                Parameters = new Dictionary<string, Variant>
                {
                    ["Mode"] = new Variant("Auto"),
                    ["Speed"] = new Variant(42)
                }
            };
            var encoder = new JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(msg, ctx)
                .ConfigureAwait(false);

            var decoder = new JsonDecoder();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(bytes, ctx)
                .ConfigureAwait(false);

            var act = decoded as JsonActionNetworkMessage;
            Assert.That(act, Is.Not.Null);
            Assert.That(act!.Action, Is.EqualTo("urn:test:action:start"));
            Assert.That(act.RequestId, Is.EqualTo("req-1"));
            Assert.That(act.IsResponse, Is.False);
            Assert.That(act.Parameters, Has.Count.EqualTo(2));
            Assert.That(act.Parameters["Mode"].TryGetValue(out string mode), Is.True);
            Assert.That(mode, Is.EqualTo("Auto"));
        }

        [Test]
        [TestSpec("7.2.5.6.2")]
        public async Task Encode_Response_RoundTripsAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var msg = new JsonActionNetworkMessage
            {
                MessageId = "act-resp-1",
                PublisherId = PublisherId.FromUInt16(0x100),
                Action = "urn:test:action:start",
                RequestId = "req-1",
                ResponseId = "resp-1",
                Parameters = new Dictionary<string, Variant>
                {
                    ["Result"] = new Variant("OK"),
                    ["Code"] = new Variant(0u)
                }
            };
            var encoder = new JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(msg, ctx)
                .ConfigureAwait(false);

            var decoder = new JsonDecoder();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(bytes, ctx)
                .ConfigureAwait(false);

            var act = decoded as JsonActionNetworkMessage;
            Assert.That(act, Is.Not.Null);
            Assert.That(act!.IsResponse, Is.True);
            Assert.That(act.ResponseId, Is.EqualTo("resp-1"));
            Assert.That(act.RequestId, Is.EqualTo("req-1"));
        }

        [Test]
        [TestSpec("7.2.5.6.1")]
        public async Task Decode_MissingRequestId_RejectsAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            ReadOnlyMemory<byte> bytes = System.Text.Encoding.UTF8.GetBytes(
                "{\"MessageType\":\"ua-action\",\"Action\":\"urn:test:noid\"," +
                "\"Parameters\":{}}");
            var decoder = new JsonDecoder();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(bytes, ctx)
                .ConfigureAwait(false);
            Assert.That(decoded, Is.Null);
        }

        [Test]
        [TestSpec("7.2.5.6.1")]
        public async Task Encode_NestedVariantParameters_RoundTripsAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var matrix = new Variant(new long[] { 1, 2, 3, 4, 5 });
            var msg = new JsonActionNetworkMessage
            {
                MessageId = "act-nested",
                PublisherId = PublisherId.FromUInt16(0x100),
                Action = "urn:test:action:configure",
                RequestId = "req-7",
                Parameters = new Dictionary<string, Variant>
                {
                    ["Bool"] = new Variant(true),
                    ["Array"] = matrix,
                    ["Bytes"] = new Variant(new byte[] { 0x01, 0x02, 0x03 })
                }
            };
            var encoder = new JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(msg, ctx)
                .ConfigureAwait(false);

            var decoder = new JsonDecoder();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(bytes, ctx)
                .ConfigureAwait(false);

            var act = decoded as JsonActionNetworkMessage;
            Assert.That(act, Is.Not.Null);
            Assert.That(act!.Parameters, Has.Count.EqualTo(3));
            Assert.That(act.Parameters["Bool"].TryGetValue(out bool b), Is.True);
            Assert.That(b, Is.True);
            Assert.That(act.Parameters["Array"].TypeInfo.BuiltInType,
                Is.EqualTo(BuiltInType.Int64));
        }

        [Test]
        [TestSpec("7.2.5.6.1")]
        public void Encode_EmptyAction_Rejects()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var msg = new JsonActionNetworkMessage
            {
                MessageId = "act-bad",
                PublisherId = PublisherId.FromUInt16(0x100),
                Action = string.Empty,
                RequestId = "req-x"
            };
            var encoder = new JsonEncoder();

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await encoder.EncodeAsync(msg, ctx).ConfigureAwait(false));
        }
    }
}
