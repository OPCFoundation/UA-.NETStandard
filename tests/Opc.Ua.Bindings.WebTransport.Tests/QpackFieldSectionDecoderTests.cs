// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers;
using NUnit.Framework;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Verifies bounded static-table, literal, and Huffman QPACK decoding.
    /// </summary>
    [TestFixture]
    public sealed class QpackFieldSectionDecoderTests
    {
        /// <summary>
        /// Decodes representative entries at the beginning, middle, and end of the QPACK static table.
        /// </summary>
        [TestCase(new byte[] { 0x00, 0x00, 0xc0 }, ":authority", "")]
        [TestCase(new byte[] { 0x00, 0x00, 0xc1 }, ":path", "/")]
        [TestCase(new byte[] { 0x00, 0x00, 0xff, 0x23 }, "x-frame-options", "sameorigin")]
        public void Decode_IndexedStaticField_ReturnsField(
            byte[] encoded,
            string expectedName,
            string expectedValue)
        {
            QpackDecodeStatus status = Decode(encoded, 4096, 8, out QpackHeaderField[] fields);

            Assert.That(status, Is.EqualTo(QpackDecodeStatus.Success));
            Assert.That(fields, Has.Length.EqualTo(1));
            Assert.That(fields[0].Name, Is.EqualTo(expectedName));
            Assert.That(fields[0].Value, Is.EqualTo(expectedValue));
        }

        /// <summary>
        /// Rejects static-table indexes beyond the final defined index.
        /// </summary>
        [Test]
        public void Decode_IndexedStaticField_InvalidIndex_IsRejected()
        {
            QpackDecodeStatus status = Decode(
                [0x00, 0x00, 0xff, 0x24],
                4096,
                8,
                out QpackHeaderField[] fields);

            Assert.That(status, Is.EqualTo(QpackDecodeStatus.InvalidStaticIndex));
            Assert.That(fields, Is.Empty);
        }

        /// <summary>
        /// Decodes a literal static-name field while preserving arbitrary field-value octets.
        /// </summary>
        [Test]
        public void Decode_LiteralStaticName_PreservesValueOctets()
        {
            QpackDecodeStatus status = Decode(
                [0x00, 0x00, 0x50, 0x01, 0xff],
                43,
                8,
                out QpackHeaderField[] fields);

            Assert.That(status, Is.EqualTo(QpackDecodeStatus.Success));
            Assert.That(fields[0].Name, Is.EqualTo(":authority"));
            Assert.That(fields[0].Value, Is.EqualTo("\u00ff"));
            Assert.That(
                Decode([0x00, 0x00, 0x50, 0x01, 0xff], 42, 8, out _),
                Is.EqualTo(QpackDecodeStatus.FieldSectionTooLarge));
        }

        /// <summary>
        /// Decodes a literal field name whose encoded length crosses the three-bit prefix boundary.
        /// </summary>
        [Test]
        public void Decode_LiteralNameLengthContinuation_UsesThreeBitPrefix()
        {
            QpackDecodeStatus status = Decode(
                [0x00, 0x00, 0x27, 0x01, (byte)'x', (byte)'y', (byte)'z', (byte)'a',
                    (byte)'b', (byte)'c', (byte)'d', (byte)'e', 0x01, (byte)'v'],
                64,
                8,
                out QpackHeaderField[] fields);

            Assert.That(status, Is.EqualTo(QpackDecodeStatus.Success));
            Assert.That(fields[0].Name, Is.EqualTo("xyzabcde"));
            Assert.That(fields[0].Value, Is.EqualTo("v"));
        }

        /// <summary>
        /// Rejects uppercase field names rather than normalizing protocol input.
        /// </summary>
        [Test]
        public void Decode_LiteralNameUppercase_IsRejected()
        {
            Assert.That(
                Decode(
                    [0x00, 0x00, 0x23, (byte)'F', (byte)'o', (byte)'o', 0x00],
                    64,
                    8,
                    out QpackHeaderField[] fields),
                Is.EqualTo(QpackDecodeStatus.InvalidFieldName));
            Assert.That(fields, Is.Empty);
        }

        /// <summary>
        /// Rejects forbidden CR, LF and NUL octets in decoded field values.
        /// </summary>
        [TestCase((byte)0)]
        [TestCase((byte)0x0a)]
        [TestCase((byte)0x0d)]
        public void Decode_FieldValueControlOctet_IsRejected(byte value)
        {
            Assert.That(
                Decode([0x00, 0x00, 0x50, 0x01, value], 64, 8, out _),
                Is.EqualTo(QpackDecodeStatus.InvalidFieldValue));
        }

        /// <summary>
        /// Decodes the RFC 7541 Appendix C example and validates its EOS padding.
        /// </summary>
        [Test]
        public void Huffman_RfcExample_DecodesString()
        {
            byte[] encoded = [0xf1, 0xe3, 0xc2, 0xe5, 0xf2, 0x3a, 0x6b, 0xa0, 0xab, 0x90, 0xf4, 0xff];

            Assert.That(QpackHuffmanDecoder.TryDecode(encoded, 15, out byte[] decoded), Is.True);
            Assert.That(decoded, Is.EqualTo("www.example.com"u8.ToArray()));
        }

        /// <summary>
        /// Decodes a Huffman-coded value through the QPACK field-section representation.
        /// </summary>
        [Test]
        public void Decode_LiteralStaticName_HuffmanValue()
        {
            byte[] encoded =
            [
                0x00, 0x00, 0x50, 0x8c,
                0xf1, 0xe3, 0xc2, 0xe5, 0xf2, 0x3a, 0x6b, 0xa0, 0xab, 0x90, 0xf4, 0xff
            ];

            QpackDecodeStatus status = Decode(encoded, 57, 8, out QpackHeaderField[] fields);

            Assert.That(status, Is.EqualTo(QpackDecodeStatus.Success));
            Assert.That(fields[0].Value, Is.EqualTo("www.example.com"));
        }

        /// <summary>
        /// Rejects Huffman EOS symbols, invalid padding, and output beyond the configured bound.
        /// </summary>
        [TestCase(new byte[] { 0xff, 0xff, 0xff, 0xfc }, 32)]
        [TestCase(new byte[] { 0x00 }, 32)]
        [TestCase(new byte[] { 0xf1, 0xe3, 0xc2, 0xe5, 0xf2, 0x3a, 0x6b, 0xa0, 0xab, 0x90, 0xf4, 0xff }, 14)]
        public void Huffman_InvalidOrOversizedInput_IsRejected(byte[] encoded, int maximumDecodedBytes)
        {
            Assert.That(QpackHuffmanDecoder.TryDecode(encoded, maximumDecodedBytes, out byte[] decoded), Is.False);
            Assert.That(decoded, Is.Empty);
        }

        /// <summary>
        /// Enforces exact decoded-size and field-count boundaries.
        /// </summary>
        [Test]
        public void Decode_FieldSizeAndCountLimits_AreEnforced()
        {
            byte[] encoded = [0x00, 0x00, 0x23, (byte)'f', (byte)'o', (byte)'o', 0x03,
                (byte)'b', (byte)'a', (byte)'r'];

            Assert.That(Decode(encoded, 37, 1, out _), Is.EqualTo(QpackDecodeStatus.FieldSectionTooLarge));
            Assert.That(Decode(encoded, 38, 0, out _), Is.EqualTo(QpackDecodeStatus.TooManyFields));
            Assert.That(Decode(encoded, 38, 1, out _), Is.EqualTo(QpackDecodeStatus.Success));
        }

        /// <summary>
        /// Rejects dynamic table references because this profile advertises zero capacity.
        /// </summary>
        [Test]
        public void Decode_RequiredInsertCount_IsRejected()
        {
            Assert.That(
                Decode([0x01, 0x00], 4096, 8, out QpackHeaderField[] fields),
                Is.EqualTo(QpackDecodeStatus.DynamicTableReference));
            Assert.That(fields, Is.Empty);
        }

        /// <summary>
        /// Encodes and decodes a bounded WebTransport CONNECT request without dynamic table state.
        /// </summary>
        [Test]
        public void ConnectRequestEncoder_RoundTripsExactPseudoHeadersAndOrigin()
        {
            Assert.That(
                QpackConnectRequestEncoder.TryEncode(
                    "device.example:443",
                    "/ua/wt?resource=service",
                    "https://client.example",
                    256,
                    out byte[] encoded),
                Is.True);

            QpackDecodeStatus decodeStatus = Decode(encoded, 4096, 16, out QpackHeaderField[] fields);
            Http3ExtendedConnectRequestStatus requestStatus =
                Http3ExtendedConnectRequestParser.TryParse(fields, out Http3ExtendedConnectRequest request);

            Assert.That(decodeStatus, Is.EqualTo(QpackDecodeStatus.Success));
            Assert.That(requestStatus, Is.EqualTo(Http3ExtendedConnectRequestStatus.Success));
            Assert.That(request.Authority, Is.EqualTo("device.example:443"));
            Assert.That(request.Path, Is.EqualTo("/ua/wt?resource=service"));
            Assert.That(request.Origin, Is.EqualTo("https://client.example"));
            Assert.That(
                QpackConnectRequestEncoder.TryEncode(
                    "device.example:443",
                    "/ua/wt?resource=service",
                    "https://client.example",
                    encoded.Length - 1,
                    out byte[] rejected),
                Is.False);
            Assert.That(rejected, Is.Empty);
        }

        /// <summary>
        /// Encodes a request without Origin when the explicit policy omits that field.
        /// </summary>
        [Test]
        public void ConnectRequestEncoder_OmittedOrigin_RemainsValid()
        {
            Assert.That(
                QpackConnectRequestEncoder.TryEncode("device.example", "/ua/wt", null, 128, out byte[] encoded),
                Is.True);
            Assert.That(Decode(encoded, 4096, 16, out QpackHeaderField[] fields), Is.EqualTo(QpackDecodeStatus.Success));
            Assert.That(
                Http3ExtendedConnectRequestParser.TryParse(fields, out Http3ExtendedConnectRequest request),
                Is.EqualTo(Http3ExtendedConnectRequestStatus.Success));
            Assert.That(request.Origin, Is.Null);
        }

        /// <summary>
        /// Encodes the successful CONNECT response and the recommended Capsule-Protocol indication.
        /// </summary>
        [Test]
        public void ConnectResponseEncoder_UsesSuccessfulStatusAndCapsuleProtocol()
        {
            Assert.That(
                QpackConnectRequestEncoder.TryEncodeCapsuleConnectResponse(128, out byte[] encoded),
                Is.True);

            Assert.That(Decode(encoded, 4096, 8, out QpackHeaderField[] fields), Is.EqualTo(QpackDecodeStatus.Success));
            Assert.That(fields, Has.Length.EqualTo(2));
            Assert.That(fields[0], Is.EqualTo(new QpackHeaderField(":status", "200")));
            Assert.That(fields[1], Is.EqualTo(new QpackHeaderField("capsule-protocol", "?1")));
        }

        private static QpackDecodeStatus Decode(
            ReadOnlySpan<byte> encoded,
            int maximumDecodedBytes,
            int maximumFields,
            out QpackHeaderField[] fields)
        {
            return QpackFieldSectionDecoder.TryDecode(
                new ReadOnlySequence<byte>(encoded.ToArray()),
                4096,
                maximumDecodedBytes,
                maximumFields,
                out fields);
        }
    }
}
