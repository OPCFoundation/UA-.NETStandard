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
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.PubSub.Tests.Security
{
    /// <summary>
    /// Tests for <see cref="RandomNonceProvider"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.4.3.2", Summary = "PubSub random message-nonce generator")]
    public class RandomNonceProviderTests
    {
        private const uint KeyId = 1U;
        private static readonly byte[] s_keyNonce = [0xA1, 0xB2, 0xC3, 0xD4];

        [Test]
        public void GetNext_ProducesUniqueMessageRandomBytes()
        {
            using var provider = new RandomNonceProvider(
                PublisherId.FromUInt32(0x12345678U));
            byte[] a = new byte[12];
            byte[] b = new byte[12];
            provider.GetNext(KeyId, s_keyNonce, a);
            provider.GetNext(KeyId, s_keyNonce, b);
            (uint randomA, _) = AesCtrNonceLayout.Parse(a);
            (uint randomB, _) = AesCtrNonceLayout.Parse(b);
            Assert.That(randomA, Is.Not.EqualTo(randomB));
        }

        [Test]
        public void GetNext_AppendsMonotonicSequenceNumber()
        {
            var publisherId = PublisherId.FromUInt32(0xDEADBEEFU);
            using var provider = new RandomNonceProvider(publisherId);
            byte[] a = new byte[12];
            byte[] b = new byte[12];
            byte[] c = new byte[12];
            provider.GetNext(KeyId, s_keyNonce, a);
            provider.GetNext(KeyId, s_keyNonce, b);
            provider.GetNext(KeyId, s_keyNonce, c);
            (_, ulong seqA) = AesCtrNonceLayout.Parse(a);
            (_, ulong seqB) = AesCtrNonceLayout.Parse(b);
            (_, ulong seqC) = AesCtrNonceLayout.Parse(c);
            Assert.Multiple(() =>
            {
                Assert.That(seqA, Is.Zero);
                Assert.That(seqB, Is.EqualTo(1UL));
                Assert.That(seqC, Is.EqualTo(2UL));
                Assert.That(provider.PublisherIdLow64, Is.EqualTo(0xDEADBEEFUL));
            });
        }

        [Test]
        public void GetNext_ProducesDistinctNoncesUnderSameKey()
        {
            using var provider = new RandomNonceProvider(PublisherId.FromUInt32(7U));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            byte[] buffer = new byte[12];
            for (int i = 0; i < 1000; i++)
            {
                provider.GetNext(KeyId, s_keyNonce, buffer);
                Assert.That(
                    seen.Add(AesCtrNonceLayout.ToDiagnosticString(buffer)),
                    Is.True,
                    $"nonce repeated at message {i}");
            }
        }

        [Test]
        public void GetNext_ResetsSequenceNumberWhenKeyChanges()
        {
            using var provider = new RandomNonceProvider(PublisherId.FromUInt32(7U));
            byte[] a = new byte[12];
            byte[] b = new byte[12];
            provider.GetNext(KeyId, s_keyNonce, a);
            provider.GetNext(KeyId, s_keyNonce, a);
            provider.GetNext(2U, s_keyNonce, b);
            (_, ulong seqAfterRollover) = AesCtrNonceLayout.Parse(b);
            Assert.That(seqAfterRollover, Is.Zero);
        }

        [Test]
        public void GetNext_ThrowsWhenPerKeyCapReached()
        {
            using var provider = new RandomNonceProvider(
                PublisherId.FromUInt32(7U),
                maxMessagesPerKey: 3UL);
            byte[] buffer = new byte[12];
            Assert.Multiple(() =>
            {
                Assert.That(() => provider.GetNext(KeyId, s_keyNonce, buffer), Throws.Nothing);
                Assert.That(() => provider.GetNext(KeyId, s_keyNonce, buffer), Throws.Nothing);
                Assert.That(() => provider.GetNext(KeyId, s_keyNonce, buffer), Throws.Nothing);
                Assert.That(
                    () => provider.GetNext(KeyId, s_keyNonce, buffer),
                    Throws.TypeOf<InvalidOperationException>());
            });
        }

        [Test]
        public void GetNext_CapIsScopedPerKey()
        {
            using var provider = new RandomNonceProvider(
                PublisherId.FromUInt32(7U),
                maxMessagesPerKey: 2UL);
            byte[] buffer = new byte[12];
            provider.GetNext(KeyId, s_keyNonce, buffer);
            provider.GetNext(KeyId, s_keyNonce, buffer);
            // Switching key resets the per-key counter, so the cap does
            // not carry over.
            Assert.That(
                () => provider.GetNext(2U, s_keyNonce, buffer),
                Throws.Nothing);
        }

        [Test]
        public void Constructor_RejectsZeroCap()
        {
            Assert.That(
                () => new RandomNonceProvider(PublisherId.FromUInt16(1), maxMessagesPerKey: 0UL),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void GetNext_RejectsWrongBufferLength()
        {
            using var provider = new RandomNonceProvider(PublisherId.FromUInt16(1));
            byte[] tooSmall = new byte[10];
            Assert.That(
                () => provider.GetNext(KeyId, s_keyNonce, tooSmall),
                Throws.ArgumentException);
        }

        [Test]
        public async Task GetNext_IsThreadSafe()
        {
            using var provider = new RandomNonceProvider(PublisherId.FromUInt32(7U));
            const int iterations = 256;
            const int parallelism = 8;
            var bag = new System.Collections.Concurrent.ConcurrentBag<ulong>();
            Task[] workers = new Task[parallelism];
            for (int t = 0; t < parallelism; t++)
            {
                workers[t] = Task.Run(() =>
                {
                    byte[] buffer = new byte[12];
                    for (int i = 0; i < iterations; i++)
                    {
                        provider.GetNext(KeyId, s_keyNonce, buffer);
                        (_, ulong sequenceNumber) = AesCtrNonceLayout.Parse(buffer);
                        bag.Add(sequenceNumber);
                    }
                });
            }
            await Task.WhenAll(workers).ConfigureAwait(false);
            // The monotonic counter is serialised, so every call must
            // observe a distinct sequence number with no torn writes.
            Assert.That(bag, Has.Count.EqualTo(parallelism * iterations));
            var distinct = new HashSet<ulong>(bag);
            Assert.That(distinct, Has.Count.EqualTo(parallelism * iterations));
        }

        [Test]
        public void Dispose_BlocksFurtherCalls()
        {
            var provider = new RandomNonceProvider(PublisherId.FromUInt16(1));
            provider.Dispose();
            Assert.That(
                () => provider.GetNext(KeyId, s_keyNonce, new byte[12]),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var provider = new RandomNonceProvider(PublisherId.FromUInt16(1));
            provider.Dispose();
            Assert.DoesNotThrow(provider.Dispose);
        }
    }
}
