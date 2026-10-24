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
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Wire
{
    /// <summary>
    /// Checks the exact 24-byte direct datagram prefix and its independent bounds.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class DirectDatagramCodecTests
    {
        /// <summary>
        /// Verifies the literal UA Binary token, revision, sequence and preserved UADP body.
        /// </summary>
        [Test]
        public void DatagramHasExactTwentyFourBytePrefix()
        {
            Guid token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            ByteString body = ByteString.FromHexString("91 82 0c 01 00 00 00");
            ByteString encoded = DirectDatagramCodec.Encode(token, 0x01020304, 0xA0B0C0D0, body, new(), 31);
            Assert.That(encoded.ToHexString().ToLowerInvariant(),
                Is.EqualTo("33221100554477668899aabbccddeeff04030201d0c0b0a091820c01000000"));
            DirectDatagram decoded = DirectDatagramCodec.DecodeExact(encoded, new(), 31);
            Assert.Multiple(() =>
            {
                Assert.That(decoded.Token, Is.EqualTo(token));
                Assert.That(decoded.Revision, Is.EqualTo(0x01020304u));
                Assert.That(decoded.Sequence, Is.EqualTo(0xA0B0C0D0u));
                Assert.That(decoded.Body.ToArray(), Is.EqualTo(body.ToArray()));
            });
        }

        /// <summary>
        /// Rejects incomplete prefix/body, zero fields and empty payload partitions.
        /// </summary>
        [Test]
        public void InvalidDatagramsAreRejected()
        {
            FlowCodecLimits limits = new();
            AssertFailure(() => DirectDatagramCodec.DecodeExact(ByteString.Empty, limits, 1200),
                FlowCodecError.Truncated);
            AssertFailure(() => DirectDatagramCodec.DecodeExact(new ByteString(new byte[24]), limits, 1200),
                FlowCodecError.Truncated);
            AssertFailure(() => DirectDatagramCodec.Encode(
                Guid.Empty, 1, 1, new ByteString(new byte[] { 1 }), limits, 1200),
                FlowCodecError.InvalidFlow);
            AssertFailure(() => DirectDatagramCodec.Encode(
                Guid.NewGuid(), 0, 1, new ByteString(new byte[] { 1 }), limits, 1200),
                FlowCodecError.InvalidRevision);
            AssertFailure(() => DirectDatagramCodec.Encode(
                Guid.NewGuid(), 1, 0, new ByteString(new byte[] { 1 }), limits, 1200),
                FlowCodecError.InvalidMessageId);
            AssertFailure(() => DirectDatagramCodec.Encode(
                Guid.NewGuid(), 1, 1, ByteString.Empty, limits, 1200),
                FlowCodecError.InvalidLength);
            AssertFailure(() => DirectDatagramCodec.DecodeExact(
                ByteString.FromHexString("00000000000000000000000000000000010000000100000001"),
                limits,
                1200),
                FlowCodecError.InvalidFlow);
            byte[] valid = DirectDatagramCodec.Encode(
                Guid.NewGuid(), 1, 1, new ByteString(new byte[] { 1 }), limits, 1200).ToArray();
            Array.Clear(valid, AssociationRevisionOffset, sizeof(uint));
            AssertFailure(() => DirectDatagramCodec.DecodeExact(new ByteString(valid), limits, 1200),
                FlowCodecError.InvalidRevision);
            valid = DirectDatagramCodec.Encode(
                Guid.NewGuid(), 1, 1, new ByteString(new byte[] { 1 }), limits, 1200).ToArray();
            Array.Clear(valid, AssociationSequenceOffset, sizeof(uint));
            AssertFailure(() => DirectDatagramCodec.DecodeExact(new ByteString(valid), limits, 1200),
                FlowCodecError.InvalidMessageId);
        }

        /// <summary>
        /// Enforces the message, application and actual datagram capacities inclusively.
        /// </summary>
        [Test]
        public void DatagramCapacitiesAreInclusive()
        {
            Guid token = Guid.NewGuid();
            ByteString body = new(new byte[8]);
            var limits = new FlowCodecLimits(maxDataBytes: 8, maxRecordBytes: 32);
            ByteString exact = DirectDatagramCodec.Encode(token, 1, 1, body, limits, 32);
            Assert.That(exact.Length, Is.EqualTo(32));
            Assert.That(DirectDatagramCodec.DecodeExact(exact, limits, 32).Body.Length, Is.EqualTo(8));
            AssertFailure(() => DirectDatagramCodec.Encode(
                token, 1, 1, new ByteString(new byte[9]), limits, 64),
                FlowCodecError.TooLarge);
            AssertFailure(() => DirectDatagramCodec.Encode(token, 1, 1, body, limits, 31),
                FlowCodecError.TooLarge);
            AssertFailure(() => DirectDatagramCodec.DecodeExact(
                new ByteString(new byte[33]), limits, 32), FlowCodecError.TooLarge);
            AssertFailure(() => DirectDatagramCodec.DecodeExact(
                new ByteString(new byte[33]),
                new(maxDataBytes: 9, maxRecordBytes: 64),
                32),
                FlowCodecError.TooLarge);
            AssertFailure(() => DirectDatagramCodec.DecodeExact(
                new ByteString(new byte[33]),
                new(maxDataBytes: 9, maxRecordBytes: 32),
                64),
                FlowCodecError.TooLarge);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DirectDatagramCodec.DecodeExact(new ByteString(new byte[25]), limits, 24));
        }

        /// <summary>
        /// Asserts the exact codec failure for malformed or out-of-bounds input.
        /// </summary>
        private static void AssertFailure(Action action, FlowCodecError error)
        {
            FlowCodecException exception = Assert.Throws<FlowCodecException>(action)!;
            Assert.That(exception.Error, Is.EqualTo(error));
        }

        private const int AssociationRevisionOffset = DirectDatagramCodec.AssociationTokenSize;
        private const int AssociationSequenceOffset = AssociationRevisionOffset + sizeof(uint);
    }
}
