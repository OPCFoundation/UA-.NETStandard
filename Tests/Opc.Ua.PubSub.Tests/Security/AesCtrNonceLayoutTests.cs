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
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.PubSub.Tests.Security
{
    /// <summary>
    /// Tests for the AES-CTR nonce layout per Part 14 Table 156.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.4.3.2", Summary = "PubSub AES-CTR nonce layout (Table 156)")]
    public class AesCtrNonceLayoutTests
    {
        [Test]
        public void Build_PlacesMessageRandomBigEndianFirst()
        {
            byte[] nonce = new byte[12];
            AesCtrNonceLayout.Build(0x01020304U, 0UL, nonce);
            Assert.That(nonce[0], Is.EqualTo(0x01));
            Assert.That(nonce[1], Is.EqualTo(0x02));
            Assert.That(nonce[2], Is.EqualTo(0x03));
            Assert.That(nonce[3], Is.EqualTo(0x04));
        }

        [Test]
        public void Build_PlacesPublisherIdLittleEndianAtOffsetFour()
        {
            byte[] nonce = new byte[12];
            AesCtrNonceLayout.Build(0U, 0xAABBCCDDEEFF0011UL, nonce);
            Assert.That(nonce[4], Is.EqualTo(0x11));
            Assert.That(nonce[5], Is.Zero);
            Assert.That(nonce[6], Is.EqualTo(0xFF));
            Assert.That(nonce[7], Is.EqualTo(0xEE));
        }

        [Test]
        public void Parse_RoundTrips()
        {
            byte[] nonce = new byte[12];
            AesCtrNonceLayout.Build(0xCAFEBABEU, 0xDEADBEEFCAFEBABEUL, nonce);
            (uint random, ulong publisherIdLow64) = AesCtrNonceLayout.Parse(nonce);
            Assert.Multiple(() =>
            {
                Assert.That(random, Is.EqualTo(0xCAFEBABEU));
                Assert.That(publisherIdLow64, Is.EqualTo(0xDEADBEEFCAFEBABEUL));
            });
        }

        [Test]
        public void Build_RejectsWrongBufferLength()
        {
            Assert.That(
                () => AesCtrNonceLayout.Build(0U, 0UL, new byte[10]),
                Throws.ArgumentException);
        }

        [Test]
        public void Parse_RejectsWrongBufferLength()
        {
            Assert.That(
                () => AesCtrNonceLayout.Parse(new byte[10]),
                Throws.ArgumentException);
        }

        [Test]
        public void ToLow64_NumericPublisherIds_AreZeroExtended()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    AesCtrNonceLayout.ToLow64(PublisherId.FromByte(0x42)),
                    Is.EqualTo(0x42UL));
                Assert.That(
                    AesCtrNonceLayout.ToLow64(PublisherId.FromUInt16(0x1234)),
                    Is.EqualTo(0x1234UL));
                Assert.That(
                    AesCtrNonceLayout.ToLow64(PublisherId.FromUInt32(0x11223344)),
                    Is.EqualTo(0x11223344UL));
                Assert.That(
                    AesCtrNonceLayout.ToLow64(PublisherId.FromUInt64(0xAABBCCDDEEFF1122UL)),
                    Is.EqualTo(0xAABBCCDDEEFF1122UL));
            });
        }

        [Test]
        public void ToLow64_StringPublisherId_UsesFirstEightUtf8Bytes()
        {
            ulong projection = AesCtrNonceLayout.ToLow64(PublisherId.FromString("Pub-1"));
            Assert.That(projection, Is.Not.Zero);
        }

        [Test]
        public void ToLow64_StringPublisherIdShorterThanEightBytes_ZeroPadded()
        {
            ulong shortProjection = AesCtrNonceLayout.ToLow64(PublisherId.FromString("ab"));
            ulong otherShortProjection = AesCtrNonceLayout.ToLow64(PublisherId.FromString("ab\0"));
            Assert.That(shortProjection, Is.EqualTo(otherShortProjection));
        }

        [Test]
        public void ToLow64_GuidPublisherId_UsesFirstEightBytes()
        {
            Guid guid = new("11223344-5566-7788-99AA-BBCCDDEEFF00");
            ulong projection = AesCtrNonceLayout.ToLow64(PublisherId.FromGuid(guid));
            Assert.That(projection, Is.Not.Zero);
        }

        [Test]
        public void ToLow64_NullPublisherId_ReturnsZero()
        {
            Assert.That(AesCtrNonceLayout.ToLow64(PublisherId.Null), Is.Zero);
        }

        [Test]
        public void ToDiagnosticString_ProducesHexString()
        {
            byte[] nonce = new byte[12];
            for (int i = 0; i < nonce.Length; i++)
            {
                nonce[i] = (byte)i;
            }
            string hex = AesCtrNonceLayout.ToDiagnosticString(nonce);
            Assert.That(hex, Is.EqualTo("000102030405060708090a0b"));
        }

        [Test]
        public void ToDiagnosticString_RejectsWrongLength()
        {
            Assert.That(
                AesCtrNonceLayout.ToDiagnosticString(new byte[10]),
                Is.Empty);
        }
    }
}
