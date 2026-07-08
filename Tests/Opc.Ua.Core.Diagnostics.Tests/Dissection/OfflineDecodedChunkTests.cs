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
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Dissection;

namespace Opc.Ua.Pcap.Tests.Dissection
{
    /// <summary>
    /// Equality and projection-property tests for the
    /// <see cref="OfflineDecodedChunk"/> readonly struct.
    /// </summary>
    [TestFixture]
    public sealed class OfflineDecodedChunkTests
    {
        [Test]
        public void ConstructorAssignsEveryProperty()
        {
            byte[] body = [1, 2, 3, 4];

            var chunk = new OfflineDecodedChunk(
                messageType: 0x46534D47U,
                channelId: 17,
                tokenId: 42,
                sequenceNumber: 100,
                requestId: 5,
                isFinal: true,
                isAbort: false,
                body: body);

            Assert.That(chunk.MessageType, Is.EqualTo(0x46534D47U));
            Assert.That(chunk.ChannelId, Is.EqualTo(17U));
            Assert.That(chunk.TokenId, Is.EqualTo(42U));
            Assert.That(chunk.SequenceNumber, Is.EqualTo(100U));
            Assert.That(chunk.RequestId, Is.EqualTo(5U));
            Assert.That(chunk.IsFinal, Is.True);
            Assert.That(chunk.IsAbort, Is.False);
            Assert.That(chunk.Body.ToArray(), Is.EqualTo(body).AsCollection);
        }

        [Test]
        public void EqualsReturnsTrueForIdenticalValues()
        {
            byte[] body = [7, 8, 9];
            var left = new OfflineDecodedChunk(1, 2, 3, 4, 5, true, false, body);
            var right = new OfflineDecodedChunk(1, 2, 3, 4, 5, true, false, body);

            bool equalsResult = left.Equals(right);
            bool equalityOperator = left == right;
            bool inequalityOperator = left != right;

            Assert.That(equalsResult, Is.True);
            Assert.That(left, Is.EqualTo(right));
            Assert.That(equalityOperator, Is.True);
            Assert.That(inequalityOperator, Is.False);
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }

        [Test]
        public void EqualsCompareBodyContentNotReference()
        {
            byte[] body1 = [1, 2, 3];
            byte[] body2 = [1, 2, 3];
            var left = new OfflineDecodedChunk(1, 2, 3, 4, 5, true, false, body1);
            var right = new OfflineDecodedChunk(1, 2, 3, 4, 5, true, false, body2);

            Assert.That(left, Is.EqualTo(right),
                "Equals must compare body bytes, not buffer references.");
        }

        [TestCase((uint)0, (uint)2, (uint)3, (uint)4, (uint)5, true, false)]
        [TestCase((uint)1, (uint)0, (uint)3, (uint)4, (uint)5, true, false)]
        [TestCase((uint)1, (uint)2, (uint)0, (uint)4, (uint)5, true, false)]
        [TestCase((uint)1, (uint)2, (uint)3, (uint)0, (uint)5, true, false)]
        [TestCase((uint)1, (uint)2, (uint)3, (uint)4, (uint)0, true, false)]
        [TestCase((uint)1, (uint)2, (uint)3, (uint)4, (uint)5, false, false)]
        [TestCase((uint)1, (uint)2, (uint)3, (uint)4, (uint)5, true, true)]
        public void EqualsReturnsFalseWhenAnyFieldDiffers(
            uint messageType,
            uint channelId,
            uint tokenId,
            uint sequenceNumber,
            uint requestId,
            bool isFinal,
            bool isAbort)
        {
            byte[] body = [9];
            var baseline = new OfflineDecodedChunk(1, 2, 3, 4, 5, true, false, body);
            var modified = new OfflineDecodedChunk(
                messageType,
                channelId,
                tokenId,
                sequenceNumber,
                requestId,
                isFinal,
                isAbort,
                body);

            bool equalsResult = modified.Equals(baseline);
            bool equalityOperator = modified == baseline;
            bool inequalityOperator = modified != baseline;

            Assert.That(equalsResult, Is.False);
            Assert.That(equalityOperator, Is.False);
            Assert.That(inequalityOperator, Is.True);
        }

        [Test]
        public void EqualsReturnsFalseWhenBodyContentDiffers()
        {
            var left = new OfflineDecodedChunk(1, 2, 3, 4, 5, true, false, new byte[] { 1 });
            var right = new OfflineDecodedChunk(1, 2, 3, 4, 5, true, false, new byte[] { 2 });

            Assert.That(left, Is.Not.EqualTo(right));
        }

        [Test]
        public void EqualsObjectReturnsFalseForDifferentType()
        {
            var chunk = new OfflineDecodedChunk(1, 2, 3, 4, 5, true, false, Array.Empty<byte>());
            bool equalsString = chunk.Equals("not a chunk");
            bool equalsNull = chunk.Equals(null);

            Assert.That(equalsString, Is.False);
            Assert.That(equalsNull, Is.False);
        }

        [Test]
        public void EqualsObjectReturnsTrueForBoxedEqualValue()
        {
            var chunk = new OfflineDecodedChunk(1, 2, 3, 4, 5, true, false, Array.Empty<byte>());
            object boxed = chunk;
            bool equalsBoxed = chunk.Equals(boxed);

            Assert.That(equalsBoxed, Is.True);
        }
    }
}
