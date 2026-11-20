/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Tests.Binding
{
    /// <summary>
    /// Round-trip and selection tests for the built-in payload codecs.
    /// </summary>
    [TestFixture]
    public sealed class WotCodecTests
    {
        private static readonly WotPayloadDescriptor s_json = new("application/json", "json");
        private static readonly WotPayloadDescriptor s_text = new("text/plain", "text");

        private static readonly WotPayloadDescriptor s_octet =
            new("application/octet-stream", "octet-stream");

        [Test]
        public void JsonRoundTripsScalars()
        {
            AssertRoundTrip(JsonWotPayloadCodec.Instance, s_json, new Variant(42L), 42L);
            AssertRoundTrip(JsonWotPayloadCodec.Instance, s_json, new Variant(true), true);
            AssertRoundTrip(JsonWotPayloadCodec.Instance, s_json, new Variant("hello"), "hello");
            AssertRoundTrip(JsonWotPayloadCodec.Instance, s_json, new Variant(3.5), 3.5);
        }

        [Test]
        public void TextRoundTripsString()
        {
            WotEncodeResult encoded = TextWotPayloadCodec.Instance.Encode(new Variant("abc"), s_text);
            Assert.That(encoded.Success, Is.True);
            WotDecodeResult decoded = TextWotPayloadCodec.Instance.Decode(encoded.Data, s_text);
            Assert.That(decoded.Value.AsBoxedObject(), Is.EqualTo("abc"));
        }

        [Test]
        public void OctetStreamRoundTripsBytes()
        {
            byte[] payload = [1, 2, 3, 4];
            WotEncodeResult encoded = OctetStreamWotPayloadCodec.Instance.Encode(
                new Variant(new ByteString(payload)), s_octet);
            Assert.That(encoded.Success, Is.True);
            Assert.That(encoded.Data.ToArray(), Is.EqualTo(payload));
            WotDecodeResult decoded = OctetStreamWotPayloadCodec.Instance.Decode(encoded.Data, s_octet);
            WotEncodeResult reencoded = OctetStreamWotPayloadCodec.Instance.Encode(decoded.Value, s_octet);
            Assert.That(reencoded.Data.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public void RegistrySelectsByContentType()
        {
            WotPayloadCodecRegistry registry = WotPayloadCodecRegistry.Default;

            Assert.That(registry.TrySelect("application/json", out IWotPayloadCodec json), Is.True);
            Assert.That(json.Id, Is.EqualTo("json"));
            Assert.That(registry.TrySelect("text/plain", out IWotPayloadCodec text), Is.True);
            Assert.That(text.Id, Is.EqualTo("text"));
            Assert.That(registry.TrySelect("application/octet-stream", out IWotPayloadCodec octet), Is.True);
            Assert.That(octet.Id, Is.EqualTo("octet-stream"));
        }

        private static void AssertRoundTrip(
            JsonWotPayloadCodec codec, WotPayloadDescriptor payload, Variant value, object expected)
        {
            WotEncodeResult encoded = codec.Encode(value, payload);
            Assert.That(encoded.Success, Is.True);
            WotDecodeResult decoded = codec.Decode(encoded.Data, payload);
            Assert.That(decoded.Success, Is.True);
            Assert.That(decoded.Value.AsBoxedObject(), Is.EqualTo(expected));
        }
    }
}
