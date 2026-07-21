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

using NUnit.Framework;
using Opc.Ua.WotCon.Binding;

namespace Opc.Ua.WotCon.Tests.Binding
{
    /// <summary>Round-trip and selection tests for the built-in payload codecs.</summary>
    [TestFixture]
    public sealed class WotCodecTests
    {
        private static readonly WotPayloadDescriptor s_json = new WotPayloadDescriptor("application/json", "json");
        private static readonly WotPayloadDescriptor s_text = new WotPayloadDescriptor("text/plain", "text");
        private static readonly WotPayloadDescriptor s_octet =
            new WotPayloadDescriptor("application/octet-stream", "octet-stream");

        [Test]
        public void Json_RoundTripsScalars()
        {
            AssertRoundTrip(JsonWotPayloadCodec.Instance, s_json, new Variant(42L), 42L);
            AssertRoundTrip(JsonWotPayloadCodec.Instance, s_json, new Variant(true), true);
            AssertRoundTrip(JsonWotPayloadCodec.Instance, s_json, new Variant("hello"), "hello");
            AssertRoundTrip(JsonWotPayloadCodec.Instance, s_json, new Variant(3.5), 3.5);
        }

        [Test]
        public void Text_RoundTripsString()
        {
            WotEncodeResult encoded = TextWotPayloadCodec.Instance.Encode(new Variant("abc"), s_text);
            Assert.That(encoded.Success, Is.True);
            WotDecodeResult decoded = TextWotPayloadCodec.Instance.Decode(encoded.Data, s_text);
            Assert.That(decoded.Value.AsBoxedObject(), Is.EqualTo("abc"));
        }

        [Test]
        public void OctetStream_RoundTripsBytes()
        {
            byte[] payload = { 1, 2, 3, 4 };
            WotEncodeResult encoded = OctetStreamWotPayloadCodec.Instance.Encode(
                new Variant(new ByteString(payload)), s_octet);
            Assert.That(encoded.Success, Is.True);
            Assert.That(encoded.Data.ToArray(), Is.EqualTo(payload));
            WotDecodeResult decoded = OctetStreamWotPayloadCodec.Instance.Decode(encoded.Data, s_octet);
            WotEncodeResult reencoded = OctetStreamWotPayloadCodec.Instance.Encode(decoded.Value, s_octet);
            Assert.That(reencoded.Data.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public void Registry_SelectsByContentType()
        {
            var registry = WotPayloadCodecRegistry.Default;

            Assert.That(registry.TrySelect("application/json", out IWotPayloadCodec json), Is.True);
            Assert.That(json.Id, Is.EqualTo("json"));
            Assert.That(registry.TrySelect("text/plain", out IWotPayloadCodec text), Is.True);
            Assert.That(text.Id, Is.EqualTo("text"));
            Assert.That(registry.TrySelect("application/octet-stream", out IWotPayloadCodec octet), Is.True);
            Assert.That(octet.Id, Is.EqualTo("octet-stream"));
        }

        private static void AssertRoundTrip(
            IWotPayloadCodec codec, WotPayloadDescriptor payload, Variant value, object expected)
        {
            WotEncodeResult encoded = codec.Encode(value, payload);
            Assert.That(encoded.Success, Is.True);
            WotDecodeResult decoded = codec.Decode(encoded.Data, payload);
            Assert.That(decoded.Success, Is.True);
            Assert.That(decoded.Value.AsBoxedObject(), Is.EqualTo(expected));
        }
    }
}
