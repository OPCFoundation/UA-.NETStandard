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
        [Test]
        public void GetNext_ProducesUniqueMessageRandomBytes()
        {
            using var provider = new RandomNonceProvider(
                PublisherId.FromUInt32(0x12345678U));
            byte[] a = new byte[12];
            byte[] b = new byte[12];
            provider.GetNext(a);
            provider.GetNext(b);
            (uint randomA, _) = AesCtrNonceLayout.Parse(a);
            (uint randomB, _) = AesCtrNonceLayout.Parse(b);
            Assert.That(randomA, Is.Not.EqualTo(randomB));
        }

        [Test]
        public void GetNext_PublisherIdProjectionIsStable()
        {
            var publisherId = PublisherId.FromUInt32(0xDEADBEEFU);
            using var provider = new RandomNonceProvider(publisherId);
            byte[] a = new byte[12];
            byte[] b = new byte[12];
            provider.GetNext(a);
            provider.GetNext(b);
            (_, ulong projectionA) = AesCtrNonceLayout.Parse(a);
            (_, ulong projectionB) = AesCtrNonceLayout.Parse(b);
            Assert.Multiple(() =>
            {
                Assert.That(projectionA, Is.EqualTo(0xDEADBEEFUL));
                Assert.That(projectionB, Is.EqualTo(projectionA));
                Assert.That(provider.PublisherIdLow64, Is.EqualTo(0xDEADBEEFUL));
            });
        }

        [Test]
        public void GetNext_RejectsWrongBufferLength()
        {
            using var provider = new RandomNonceProvider(PublisherId.FromUInt16(1));
            byte[] tooSmall = new byte[10];
            Assert.That(
                () => provider.GetNext(tooSmall),
                Throws.ArgumentException);
        }

        [Test]
        public async Task GetNext_IsThreadSafe()
        {
            using var provider = new RandomNonceProvider(PublisherId.FromUInt32(7U));
            const int iterations = 256;
            const int parallelism = 8;
            var bag = new System.Collections.Concurrent.ConcurrentBag<uint>();
            Task[] workers = new Task[parallelism];
            for (int t = 0; t < parallelism; t++)
            {
                workers[t] = Task.Run(() =>
                {
                    byte[] buffer = new byte[12];
                    for (int i = 0; i < iterations; i++)
                    {
                        provider.GetNext(buffer);
                        (uint random, _) = AesCtrNonceLayout.Parse(buffer);
                        bag.Add(random);
                    }
                });
            }
            await Task.WhenAll(workers);
            // Verify no torn writes — every entry has a corresponding integer.
            Assert.That(bag, Has.Count.EqualTo(parallelism * iterations));
            // Statistical check: the random sequence should produce a
            // very high number of distinct values; allow a margin to
            // avoid flakiness on a constrained 4-byte space.
            var distinct = new HashSet<uint>(bag);
            Assert.That(distinct, Has.Count.GreaterThan(parallelism * iterations / 2));
        }

        [Test]
        public void Dispose_BlocksFurtherCalls()
        {
            var provider = new RandomNonceProvider(PublisherId.FromUInt16(1));
            provider.Dispose();
            Assert.That(
                () => provider.GetNext(new byte[12]),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var provider = new RandomNonceProvider(PublisherId.FromUInt16(1));
            provider.Dispose();
            Assert.DoesNotThrow(() => provider.Dispose());
        }
    }
}
