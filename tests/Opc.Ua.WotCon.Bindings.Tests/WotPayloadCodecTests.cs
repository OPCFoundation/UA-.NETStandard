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

using System;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for the built-in payload codecs and the codec registry.
    /// </summary>
    [TestFixture]
    public sealed class WotPayloadCodecTests
    {
        private static readonly WotPayloadDescriptor s_jsonPayload =
            new WotPayloadDescriptor("application/json", "json");

        private static readonly WotPayloadDescriptor s_textPayload =
            new WotPayloadDescriptor("text/plain", "text");

        private static readonly WotPayloadDescriptor s_octetPayload =
            new WotPayloadDescriptor("application/octet-stream", "octet-stream");

        [TestCase(null, true)]
        [TestCase("", true)]
        [TestCase("application/json", true)]
        [TestCase("APPLICATION/JSON", true)]
        [TestCase("application/json; charset=utf-8", true)]
        [TestCase("application/thing+json", true)]
        [TestCase("application/ld+json", true)]
        [TestCase("text/plain", false)]
        [TestCase("application/octet-stream", false)]
        [TestCase("application/cbor", false)]
        public void JsonCodecCanHandleRecognizesContentTypes(string? contentType, bool expected)
        {
            Assert.That(JsonWotPayloadCodec.Instance.CanHandle(contentType), Is.EqualTo(expected));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("text/plain", true)]
        [TestCase("text/csv", true)]
        [TestCase("TEXT/HTML", true)]
        [TestCase("application/json", false)]
        [TestCase("application/octet-stream", false)]
        public void TextCodecCanHandleRecognizesContentTypes(string? contentType, bool expected)
        {
            Assert.That(TextWotPayloadCodec.Instance.CanHandle(contentType), Is.EqualTo(expected));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("application/octet-stream", true)]
        [TestCase("application/octet-stream; charset=binary", true)]
        [TestCase("APPLICATION/OCTET-STREAM", true)]
        [TestCase("application/json", false)]
        [TestCase("text/plain", false)]
        public void OctetStreamCodecCanHandleRecognizesContentTypes(string? contentType, bool expected)
        {
            Assert.That(OctetStreamWotPayloadCodec.Instance.CanHandle(contentType), Is.EqualTo(expected));
        }

        [Test]
        public void JsonCodecHasStableId()
        {
            Assert.That(JsonWotPayloadCodec.Instance.Id, Is.EqualTo("json"));
        }

        [Test]
        public void TextCodecHasStableId()
        {
            Assert.That(TextWotPayloadCodec.Instance.Id, Is.EqualTo("text"));
        }

        [Test]
        public void OctetStreamCodecHasStableId()
        {
            Assert.That(OctetStreamWotPayloadCodec.Instance.Id, Is.EqualTo("octet-stream"));
        }

        [Test]
        public void JsonCodecDecodesEmptyDataAsNullVariant()
        {
            WotDecodeResult result = JsonWotPayloadCodec.Instance.Decode(
                ReadOnlyMemory<byte>.Empty, s_jsonPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.IsNull, Is.True);
        }

        [Test]
        public void JsonCodecDecodesTrueValue()
        {
            byte[] data = Encoding.UTF8.GetBytes("true");
            WotDecodeResult result = JsonWotPayloadCodec.Instance.Decode(data, s_jsonPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.AsBoxedObject(), Is.True);
        }

        [Test]
        public void JsonCodecDecodesFalseValue()
        {
            byte[] data = Encoding.UTF8.GetBytes("false");
            WotDecodeResult result = JsonWotPayloadCodec.Instance.Decode(data, s_jsonPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.AsBoxedObject(), Is.False);
        }

        [Test]
        public void JsonCodecDecodesNullValue()
        {
            byte[] data = Encoding.UTF8.GetBytes("null");
            WotDecodeResult result = JsonWotPayloadCodec.Instance.Decode(data, s_jsonPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.IsNull, Is.True);
        }

        [Test]
        public void JsonCodecDecodesStringValue()
        {
            byte[] data = Encoding.UTF8.GetBytes("\"hello world\"");
            WotDecodeResult result = JsonWotPayloadCodec.Instance.Decode(data, s_jsonPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.AsBoxedObject(), Is.EqualTo("hello world"));
        }

        [Test]
        public void JsonCodecDecodesIntegerAsLong()
        {
            byte[] data = Encoding.UTF8.GetBytes("42");
            WotDecodeResult result = JsonWotPayloadCodec.Instance.Decode(data, s_jsonPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.AsBoxedObject(), Is.EqualTo(42L));
        }

        [Test]
        public void JsonCodecDecodesDoubleWhenNotFitsInt64()
        {
            byte[] data = Encoding.UTF8.GetBytes("1.5");
            WotDecodeResult result = JsonWotPayloadCodec.Instance.Decode(data, s_jsonPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.AsBoxedObject(), Is.EqualTo(1.5));
        }

        [Test]
        public void JsonCodecDecodesObjectAsRawText()
        {
            byte[] data = Encoding.UTF8.GetBytes("{\"a\":1}");
            WotDecodeResult result = JsonWotPayloadCodec.Instance.Decode(data, s_jsonPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.AsBoxedObject(), Is.EqualTo("{\"a\":1}"));
        }

        [Test]
        public void JsonCodecDecodesArrayAsRawText()
        {
            byte[] data = Encoding.UTF8.GetBytes("[1,2,3]");
            WotDecodeResult result = JsonWotPayloadCodec.Instance.Decode(data, s_jsonPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.AsBoxedObject(), Is.EqualTo("[1,2,3]"));
        }

        [Test]
        public void JsonCodecReportsMalformedJson()
        {
            byte[] data = Encoding.UTF8.GetBytes("{not valid json}");
            WotDecodeResult result = JsonWotPayloadCodec.Instance.Decode(data, s_jsonPayload);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void JsonCodecEncodesBooleanTrue()
        {
            WotEncodeResult result = JsonWotPayloadCodec.Instance.Encode(
                new Variant(true), s_jsonPayload);

            Assert.That(result.Success, Is.True);
            string text = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(text, Is.EqualTo("true"));
        }

        [Test]
        public void JsonCodecEncodesBooleanFalse()
        {
            WotEncodeResult result = JsonWotPayloadCodec.Instance.Encode(
                new Variant(false), s_jsonPayload);

            Assert.That(result.Success, Is.True);
            string text = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(text, Is.EqualTo("false"));
        }

        [Test]
        public void JsonCodecEncodesString()
        {
            WotEncodeResult result = JsonWotPayloadCodec.Instance.Encode(
                new Variant("hello"), s_jsonPayload);

            Assert.That(result.Success, Is.True);
            string text = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(text, Is.EqualTo("\"hello\""));
        }

        [Test]
        public void JsonCodecEncodesNullVariantAsNull()
        {
            WotEncodeResult result = JsonWotPayloadCodec.Instance.Encode(
                Variant.Null, s_jsonPayload);

            Assert.That(result.Success, Is.True);
            string text = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(text, Is.EqualTo("null"));
        }

        [Test]
        public void JsonCodecEncodesByte()
        {
            WotEncodeResult result = JsonWotPayloadCodec.Instance.Encode(
                new Variant((byte)200), s_jsonPayload);

            Assert.That(result.Success, Is.True);
            string text = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(text, Is.EqualTo("200"));
        }

        [Test]
        public void JsonCodecEncodesSByte()
        {
            WotEncodeResult result = JsonWotPayloadCodec.Instance.Encode(
                new Variant((sbyte)-100), s_jsonPayload);

            Assert.That(result.Success, Is.True);
            string text = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(text, Is.EqualTo("-100"));
        }

        [Test]
        public void JsonCodecEncodesShort()
        {
            WotEncodeResult result = JsonWotPayloadCodec.Instance.Encode(
                new Variant((short)-32000), s_jsonPayload);

            Assert.That(result.Success, Is.True);
            string text = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(text, Is.EqualTo("-32000"));
        }

        [Test]
        public void JsonCodecEncodesUShort()
        {
            WotEncodeResult result = JsonWotPayloadCodec.Instance.Encode(
                new Variant((ushort)60000), s_jsonPayload);

            Assert.That(result.Success, Is.True);
            string text = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(text, Is.EqualTo("60000"));
        }

        [Test]
        public void JsonCodecEncodesInt()
        {
            WotEncodeResult result = JsonWotPayloadCodec.Instance.Encode(
                new Variant(123456), s_jsonPayload);

            Assert.That(result.Success, Is.True);
            string text = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(text, Is.EqualTo("123456"));
        }

        [Test]
        public void JsonCodecEncodesUInt()
        {
            WotEncodeResult result = JsonWotPayloadCodec.Instance.Encode(
                new Variant(3000000000U), s_jsonPayload);

            Assert.That(result.Success, Is.True);
            string text = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(text, Is.EqualTo("3000000000"));
        }

        [Test]
        public void JsonCodecEncodesLong()
        {
            WotEncodeResult result = JsonWotPayloadCodec.Instance.Encode(
                new Variant(9000000000000L), s_jsonPayload);

            Assert.That(result.Success, Is.True);
            string text = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(text, Is.EqualTo("9000000000000"));
        }

        [Test]
        public void JsonCodecEncodesULong()
        {
            WotEncodeResult result = JsonWotPayloadCodec.Instance.Encode(
                new Variant(9000000000000UL), s_jsonPayload);

            Assert.That(result.Success, Is.True);
            string text = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(text, Is.EqualTo("9000000000000"));
        }

        [Test]
        public void JsonCodecEncodesFloat()
        {
            WotEncodeResult result = JsonWotPayloadCodec.Instance.Encode(
                new Variant(1.5f), s_jsonPayload);

            Assert.That(result.Success, Is.True);
            string text = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(text, Does.Contain("1.5"));
        }

        [Test]
        public void JsonCodecEncodesDouble()
        {
            WotEncodeResult result = JsonWotPayloadCodec.Instance.Encode(
                new Variant(3.14), s_jsonPayload);

            Assert.That(result.Success, Is.True);
            string text = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(text, Does.Contain("3.14"));
        }

        [Test]
        public void TextCodecEncodesValueAsUtf8()
        {
            WotEncodeResult result = TextWotPayloadCodec.Instance.Encode(
                new Variant("café"), s_textPayload);

            Assert.That(result.Success, Is.True);
            string decoded = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(decoded, Is.EqualTo("café"));
        }

        [Test]
        public void TextCodecEncodesNullVariantAsEmpty()
        {
            WotEncodeResult result = TextWotPayloadCodec.Instance.Encode(
                Variant.Null, s_textPayload);

            Assert.That(result.Success, Is.True);
            string decoded = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(decoded, Is.EqualTo(string.Empty));
        }

        [Test]
        public void TextCodecEncodesInteger()
        {
            WotEncodeResult result = TextWotPayloadCodec.Instance.Encode(
                new Variant(42), s_textPayload);

            Assert.That(result.Success, Is.True);
            string decoded = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.That(decoded, Is.EqualTo("42"));
        }

        [Test]
        public void TextCodecDecodesUtf8BytesAsString()
        {
            byte[] data = Encoding.UTF8.GetBytes("hello");
            WotDecodeResult result = TextWotPayloadCodec.Instance.Decode(data, s_textPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.AsBoxedObject(), Is.EqualTo("hello"));
        }

        [Test]
        public void TextCodecDecodesEmptyBytesAsEmptyString()
        {
            WotDecodeResult result = TextWotPayloadCodec.Instance.Decode(
                ReadOnlyMemory<byte>.Empty, s_textPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.AsBoxedObject(), Is.EqualTo(string.Empty));
        }

        [Test]
        public void OctetStreamCodecEncodesByteString()
        {
            var bs = new ByteString(new byte[] { 1, 2, 3 });
            WotEncodeResult result = OctetStreamWotPayloadCodec.Instance.Encode(
                new Variant(bs), s_octetPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void OctetStreamCodecEncodesNullVariantAsEmpty()
        {
            WotEncodeResult result = OctetStreamWotPayloadCodec.Instance.Encode(
                Variant.Null, s_octetPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Data.IsEmpty, Is.True);
        }

        [Test]
        public void OctetStreamCodecEncodeStringAsUtf8()
        {
            WotEncodeResult result = OctetStreamWotPayloadCodec.Instance.Encode(
                new Variant("abc"), s_octetPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(Encoding.UTF8.GetString(result.Data.ToArray()), Is.EqualTo("abc"));
        }

        [Test]
        public void OctetStreamCodecDecodesAsByteString()
        {
            byte[] data = { 10, 20, 30 };
            WotDecodeResult result = OctetStreamWotPayloadCodec.Instance.Decode(data, s_octetPayload);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.TryGetValue(out ByteString bs), Is.True);
            Assert.That(bs.IsNull, Is.False);
            Assert.That(bs.Memory.ToArray(), Is.EqualTo(new byte[] { 10, 20, 30 }));
        }

        [Test]
        public void RegistryDefaultHasThreeBuiltInCodecs()
        {
            var registry = new WotPayloadCodecRegistry();

            Assert.That(registry.TrySelect("application/json", out _), Is.True);
            Assert.That(registry.TrySelect("text/plain", out _), Is.True);
            Assert.That(registry.TrySelect("application/octet-stream", out _), Is.True);
        }

        [Test]
        public void RegistryTrySelectReturnsFalseForUnknownContentType()
        {
            var registry = new WotPayloadCodecRegistry();

            bool found = registry.TrySelect("application/x-unknown-format-xyzzy", out IWotPayloadCodec codec);

            Assert.That(found, Is.False);
            Assert.That(codec, Is.Not.Null);
        }

        [Test]
        public void RegistryCustomCodecWinsOverBuiltIn()
        {
            var registry = new WotPayloadCodecRegistry();
            var custom = new StubCodec("application/x-custom");
            registry.Register(custom);

            bool found = registry.TrySelect("application/x-custom", out IWotPayloadCodec selected);

            Assert.That(found, Is.True);
            Assert.That(selected, Is.SameAs(custom));
        }

        [Test]
        public void RegistryRegisterNullThrows()
        {
            var registry = new WotPayloadCodecRegistry();
            Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
        }

        [Test]
        public void RegistryTrySelectNullReturnsTrueWithJsonCodec()
        {
            var registry = new WotPayloadCodecRegistry();

            bool found = registry.TrySelect(null, out IWotPayloadCodec codec);

            Assert.That(found, Is.True);
            Assert.That(codec.Id, Is.EqualTo("json"));
        }

        [Test]
        public void WotEncodeResultOkCarriesData()
        {
            byte[] data = { 1, 2, 3 };
            WotEncodeResult result = WotEncodeResult.Ok(data);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Data.ToArray(), Is.EqualTo(data));
        }

        [Test]
        public void WotEncodeResultFailCarriesError()
        {
            WotEncodeResult result = WotEncodeResult.Fail("something went wrong");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo("something went wrong"));
            Assert.That(result.Data.IsEmpty, Is.True);
        }

        [Test]
        public void WotDecodeResultOkCarriesValue()
        {
            WotDecodeResult result = WotDecodeResult.Ok(new Variant(99));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Value.AsBoxedObject(), Is.EqualTo(99));
        }

        [Test]
        public void WotDecodeResultFailCarriesError()
        {
            WotDecodeResult result = WotDecodeResult.Fail("decode failure");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo("decode failure"));
            Assert.That(result.Value.IsNull, Is.True);
        }

        private sealed class StubCodec : IWotPayloadCodec
        {
            private readonly string m_contentType;

            public StubCodec(string contentType)
            {
                m_contentType = contentType;
            }

            public string Id => "stub";

            public bool CanHandle(string? contentType)
            {
                return string.Equals(contentType, m_contentType, StringComparison.OrdinalIgnoreCase);
            }

            public WotEncodeResult Encode(Variant value, WotPayloadDescriptor payload)
            {
                return WotEncodeResult.Ok(Array.Empty<byte>());
            }

            public WotDecodeResult Decode(ReadOnlyMemory<byte> data, WotPayloadDescriptor payload)
            {
                return WotDecodeResult.Ok(Variant.Null);
            }
        }
    }
}
