// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Security.Cryptography;
using NUnit.Framework;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Regressions for extracting exactly one signed DER envelope from a blob.
    /// The envelopes are generated in memory and do not contain certificates or keys.
    /// </summary>
    [TestFixture]
    [Category("Certificate")]
    [Parallelizable]
    public sealed class AsnBlobParsingTests
    {
        [TestCase(0, 2)]
        [TestCase(100, 3)]
        [TestCase(256, 4)]
        [TestCase(65536, 5)]
        public void ParseX509BlobReturnsWholeStandaloneEnvelope(int payloadLength, int headerLength)
        {
            byte[] envelope = CreateEnvelope(payloadLength);
            AssertHeaderLength(envelope, headerLength);

            ReadOnlyMemory<byte> parsed = AsnUtils.ParseX509Blob(envelope);

            Assert.That(parsed.ToArray(), Is.EqualTo(envelope));
        }

        [TestCase(0, 2)]
        [TestCase(100, 3)]
        [TestCase(256, 4)]
        [TestCase(65536, 5)]
        public void ParseX509BlobStopsAtFirstEnvelopeInSlicedMemory(
            int payloadLength,
            int headerLength)
        {
            byte[] first = CreateEnvelope(payloadLength);
            byte[] following = CreateEnvelope(256);
            AssertHeaderLength(first, headerLength);
            byte[] storage = [0xFF, .. first, .. following, 0xFF];
            ReadOnlyMemory<byte> blob = storage.AsMemory(1, first.Length + following.Length);

            ReadOnlyMemory<byte> parsed = AsnUtils.ParseX509Blob(blob);

            Assert.That(parsed.ToArray(), Is.EqualTo(first));
            ReadOnlyMemory<byte> remainder = blob[parsed.Length..];
            Assert.That(remainder.ToArray(), Is.EqualTo(following));
            Assert.That(AsnUtils.ParseX509Blob(remainder).ToArray(), Is.EqualTo(following));
        }

        [TestCase(0)]
        [TestCase(100)]
        [TestCase(256)]
        [TestCase(65536)]
        public void ParseX509BlobRejectsTruncatedContentAsCryptographicException(int payloadLength)
        {
            byte[] envelope = CreateEnvelope(payloadLength);
            ReadOnlyMemory<byte> truncated = envelope.AsMemory(0, envelope.Length - 1);

            Assert.That(
                () => AsnUtils.ParseX509Blob(truncated),
                Throws.TypeOf<CryptographicException>());
        }

        [TestCaseSource(nameof(IncompleteEnvelopes))]
        public void ParseX509BlobRejectsIncompleteEnvelopeAsCryptographicException(byte[] blob)
        {
            Assert.That(
                () => AsnUtils.ParseX509Blob(blob),
                Throws.TypeOf<CryptographicException>());
        }

        [Test]
        public void ParseX509BlobRejectsUnusedSignatureBits()
        {
            byte[] envelope = CreateEnvelope(256, unusedBitCount: 1);

            Assert.That(
                () => AsnUtils.ParseX509Blob(envelope),
                Throws.TypeOf<CryptographicException>());
        }

        [Test]
        public void ParseX509BlobRejectsTrailingFieldInsideEnvelope()
        {
            byte[] envelope = CreateEnvelope(256, appendNull: true);

            Assert.That(
                () => AsnUtils.ParseX509Blob(envelope),
                Throws.TypeOf<CryptographicException>());
        }

        public static IEnumerable<TestCaseData> IncompleteEnvelopes()
        {
            yield return new TestCaseData(Array.Empty<byte>())
                .SetName("ParseX509BlobRejectsEmptyInput");
            yield return new TestCaseData(new byte[] { 0x30, 0x00 })
                .SetName("ParseX509BlobRejectsEmptyEnvelope");
            yield return new TestCaseData(new byte[] { 0x30 })
                .SetName("ParseX509BlobRejectsMissingLength");
            yield return new TestCaseData(new byte[] { 0x30, 0x81 })
                .SetName("ParseX509BlobRejectsTruncated81Length");
            yield return new TestCaseData(new byte[] { 0x30, 0x82, 0x01 })
                .SetName("ParseX509BlobRejectsTruncated82Length");
            yield return new TestCaseData(new byte[] { 0x30, 0x83, 0x01, 0x00 })
                .SetName("ParseX509BlobRejectsTruncated83Length");
            yield return new TestCaseData(new byte[] { 0x30, 0x82, 0x01, 0x00, 0x00 })
                .SetName("ParseX509BlobRejectsDeclaredContentBeyondInput");
        }

        private static void AssertHeaderLength(byte[] envelope, int headerLength)
        {
            Assert.That(envelope[0], Is.EqualTo(0x30));
            if (headerLength == 2)
            {
                Assert.That(envelope[1], Is.LessThan(0x80), "The fixture must use a short DER length.");
            }
            else
            {
                Assert.That(envelope[1], Is.EqualTo(0x80 | (headerLength - 2)));
            }
        }

        private static byte[] CreateEnvelope(
            int payloadLength,
            int unusedBitCount = 0,
            bool appendNull = false)
        {
            byte[] payload = new byte[payloadLength];
            payload.AsSpan().Fill(0x5A);
            byte[] signature = new byte[32];
            signature.AsSpan().Fill(0xA4);

            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.PushSequence();
            writer.WriteOctetString(payload);
            writer.PopSequence();
            writer.PushSequence();
            writer.WriteObjectIdentifier("1.2.840.113549.1.1.11");
            writer.WriteNull();
            writer.PopSequence();
            writer.WriteBitString(signature, unusedBitCount);
            if (appendNull)
            {
                writer.WriteNull();
            }
            writer.PopSequence();
            return writer.Encode();
        }
    }
}
