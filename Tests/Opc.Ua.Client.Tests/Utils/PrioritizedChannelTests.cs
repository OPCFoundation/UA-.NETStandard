/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#if !NET8_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Types.Polyfills.Tests
{
    [TestFixture]
    [Category("Polyfills")]
    [Category("PrioritizedChannel")]
    public sealed class PrioritizedChannelTests
    {
        private Channel<int> CreateChannel(IComparer<int> comparer = null)
        {
            return Channel.CreateUnboundedPrioritized(
                new UnboundedPrioritizedChannelOptions<int>
                {
                    Comparer = comparer,
                    SingleReader = false
                });
        }

        [Test]
        public async Task ItemsReadInPriorityOrder()
        {
            Channel<int> channel = CreateChannel();

            Assert.That(channel.Writer.TryWrite(5), Is.True);
            Assert.That(channel.Writer.TryWrite(1), Is.True);
            Assert.That(channel.Writer.TryWrite(3), Is.True);
            Assert.That(channel.Writer.TryWrite(2), Is.True);
            Assert.That(channel.Writer.TryWrite(4), Is.True);

            var results = new List<int>();

            for (int i = 0; i < 5; i++)
            {
                bool canRead = await channel.Reader.WaitToReadAsync()
                    .ConfigureAwait(false);
                Assert.That(canRead, Is.True);
                Assert.That(channel.Reader.TryRead(out int item), Is.True);
                results.Add(item);
            }

            Assert.That(results, Is.EqualTo([1, 2, 3, 4, 5]));
        }

        [Test]
        public async Task SingleItemWriteAndRead()
        {
            Channel<int> channel = CreateChannel();

            Assert.That(channel.Writer.TryWrite(42), Is.True);

            bool canRead = await channel.Reader.WaitToReadAsync()
                .ConfigureAwait(false);
            Assert.That(canRead, Is.True);
            Assert.That(channel.Reader.TryRead(out int item), Is.True);
            Assert.That(item, Is.EqualTo(42));
        }

        [Test]
        public void TryWriteReturnsFalseAfterComplete()
        {
            Channel<int> channel = CreateChannel();

            Assert.That(channel.Writer.TryComplete(), Is.True);
            Assert.That(channel.Writer.TryWrite(1), Is.False);
        }

        [Test]
        public async Task WaitToReadReturnsFalseAfterComplete()
        {
            Channel<int> channel = CreateChannel();

            Assert.That(channel.Writer.TryComplete(), Is.True);

            bool canRead = await channel.Reader.WaitToReadAsync()
                .ConfigureAwait(false);
            Assert.That(canRead, Is.False);
        }

        [Test]
        public async Task ConcurrentWriteAndRead()
        {
            Channel<int> channel = CreateChannel();

            const int writerCount = 4;
            const int itemsPerWriter = 50;
            const int totalItems = writerCount * itemsPerWriter;

            var writers = new Task[writerCount];
            for (int w = 0; w < writerCount; w++)
            {
                int start = w * itemsPerWriter;
                writers[w] = Task.Run(() =>
                {
                    for (int i = 0; i < itemsPerWriter; i++)
                    {
                        channel.Writer.TryWrite(start + i);
                    }
                });
            }

            await Task.WhenAll(writers).ConfigureAwait(false);
            channel.Writer.TryComplete();

            var results = new List<int>();

            while (await channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out int item))
                {
                    results.Add(item);
                }
            }

            Assert.That(results, Has.Count.EqualTo(totalItems));

            for (int i = 1; i < results.Count; i++)
            {
                Assert.That(results[i], Is.GreaterThanOrEqualTo(results[i - 1]),
                    $"Item at index {i} ({results[i]}) is less than item at index {i - 1} ({results[i - 1]})");
            }
        }

        [Test]
        public void TryReadReturnsFalseWhenEmpty()
        {
            Channel<int> channel = CreateChannel();

            Assert.That(channel.Reader.TryRead(out _), Is.False);
        }

        [Test]
        public async Task LargeVolumeOrdering()
        {
            Channel<int> channel = CreateChannel();

            var random = new Random(12345);
            var values = new List<int>(1000);

            for (int i = 0; i < 1000; i++)
            {
                values.Add(random.Next(0, 10000));
            }

            foreach (int v in values)
            {
                Assert.That(channel.Writer.TryWrite(v), Is.True);
            }

            var results = new List<int>(1000);

            for (int i = 0; i < 1000; i++)
            {
                bool canRead = await channel.Reader.WaitToReadAsync()
                    .ConfigureAwait(false);
                Assert.That(canRead, Is.True);
                Assert.That(channel.Reader.TryRead(out int item), Is.True);
                results.Add(item);
            }

            values.Sort();
            Assert.That(results, Is.EqualTo(values));
        }

        [Test]
        public async Task CompletionWithPendingItems()
        {
            Channel<int> channel = CreateChannel();

            Assert.That(channel.Writer.TryWrite(3), Is.True);
            Assert.That(channel.Writer.TryWrite(1), Is.True);
            Assert.That(channel.Writer.TryWrite(2), Is.True);

            Assert.That(channel.Writer.TryComplete(), Is.True);

            var results = new List<int>();

            while (await channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out int item))
                {
                    results.Add(item);
                }
            }

            Assert.That(results, Is.EqualTo([1, 2, 3]));
        }
    }
}
#endif
