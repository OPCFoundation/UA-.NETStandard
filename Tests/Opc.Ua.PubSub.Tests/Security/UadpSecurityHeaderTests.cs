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
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.PubSub.Tests.Security
{
    /// <summary>
    /// Tests for the on-wire <see cref="UadpSecurityHeader"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.4.3", Summary = "UADP NetworkMessage SecurityHeader")]
    public class UadpSecurityHeaderTests
    {
        [Test]
        public void RoundTrip_WithoutSecurityFooter()
        {
            byte[] nonce = new byte[12];
            for (int i = 0; i < nonce.Length; i++)
            {
                nonce[i] = (byte)(i + 1);
            }
            var header = new UadpSecurityHeader(
                (byte)(UadpSecurityFlagsEncodingMask.NetworkMessageSigned |
                    UadpSecurityFlagsEncodingMask.NetworkMessageEncrypted),
                0xDEADBEEFU,
                nonce);
            byte[] buffer = new byte[header.GetEncodedSize()];
            header.WriteTo(buffer, out int written);
            Assert.That(written, Is.EqualTo(buffer.Length));
            Assert.That(UadpSecurityHeader.TryRead(buffer, out UadpSecurityHeader read, out int consumed), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(consumed, Is.EqualTo(buffer.Length));
                Assert.That(read.SecurityFlags, Is.EqualTo(header.SecurityFlags));
                Assert.That(read.SecurityTokenId, Is.EqualTo(header.SecurityTokenId));
                Assert.That(read.MessageNonce.ToArray(), Is.EqualTo(nonce));
                Assert.That(read.SecurityFooterSize, Is.Zero);
            });
        }

        [Test]
        public void RoundTrip_WithSecurityFooter()
        {
            byte[] nonce = new byte[12];
            const byte flags = (byte)(UadpSecurityFlagsEncodingMask.NetworkMessageSigned |
                UadpSecurityFlagsEncodingMask.NetworkMessageEncrypted |
                UadpSecurityFlagsEncodingMask.SecurityFooterEnabled);
            var header = new UadpSecurityHeader(flags, 1U, nonce, securityFooterSize: 16);
            byte[] buffer = new byte[header.GetEncodedSize()];
            header.WriteTo(buffer, out int written);
            Assert.That(written, Is.EqualTo(buffer.Length));
            Assert.That(UadpSecurityHeader.TryRead(buffer, out UadpSecurityHeader read, out _), Is.True);
            Assert.That(read.SecurityFooterSize, Is.EqualTo(16));
        }

        [Test]
        public void GetEncodedSize_ReflectsFlagsAndNonceLength()
        {
            byte[] nonce = new byte[12];
            var without = new UadpSecurityHeader(0, 0U, nonce);
            var with = new UadpSecurityHeader(
                (byte)UadpSecurityFlagsEncodingMask.SecurityFooterEnabled, 0U, nonce);
            Assert.Multiple(() =>
            {
                Assert.That(without.GetEncodedSize(), Is.EqualTo(1 + 4 + 1 + 12));
                Assert.That(with.GetEncodedSize(), Is.EqualTo(1 + 4 + 1 + 12 + 2));
            });
        }

        [Test]
        public void Constructor_RejectsNonceLongerThan255()
        {
            byte[] tooLong = new byte[256];
            Assert.That(
                () => new UadpSecurityHeader(0, 0U, tooLong),
                Throws.ArgumentException);
        }

        [Test]
        public void TryRead_ReturnsFalseOnTruncation()
        {
            byte[] nonce = new byte[12];
            var header = new UadpSecurityHeader(0, 1U, nonce);
            byte[] buffer = new byte[header.GetEncodedSize()];
            header.WriteTo(buffer, out int written);
            Assert.Multiple(() =>
            {
                // Truncated mid-nonce.
                Assert.That(UadpSecurityHeader.TryRead(buffer.AsSpan(0, 10), out _, out _), Is.False);
                // Truncated header preamble.
                Assert.That(UadpSecurityHeader.TryRead(ReadOnlySpan<byte>.Empty, out _, out _), Is.False);
                Assert.That(UadpSecurityHeader.TryRead(buffer.AsSpan(0, 5), out _, out _), Is.False);
            });
        }

        [Test]
        public void TryRead_ReturnsFalseWhenFooterMissing()
        {
            byte[] nonce = new byte[12];
            const byte flags = (byte)UadpSecurityFlagsEncodingMask.SecurityFooterEnabled;
            var header = new UadpSecurityHeader(flags, 0U, nonce, securityFooterSize: 16);
            byte[] buffer = new byte[header.GetEncodedSize()];
            header.WriteTo(buffer, out int written);
            // Drop the last 2 footer-size bytes.
            Assert.That(
                UadpSecurityHeader.TryRead(buffer.AsSpan(0, written - 2), out _, out _),
                Is.False);
        }

        [Test]
        public void WriteTo_RejectsTooSmallBuffer()
        {
            byte[] nonce = new byte[12];
            var header = new UadpSecurityHeader(0, 0U, nonce);
            byte[] tooSmall = new byte[header.GetEncodedSize() - 1];
            Assert.That(
                () => header.WriteTo(tooSmall, out _),
                Throws.ArgumentException);
        }
    }
}
