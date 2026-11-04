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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Subscriptions
{
    [TestFixture]
    public sealed class SingleReaderPriorityQueueTests
    {
        [Test]
        public void TryReadReturnsItemsInPriorityOrder()
        {
            using var queue = new SingleReaderPriorityQueue<int>((a, b) => a - b);
            // Scrambled order with duplicates.
            int[] items = [.. Enumerable.Range(0, 500).Select(i => i * 7919 % 997)];
            foreach (int item in items)
            {
                Assert.That(queue.TryWrite(item), Is.True);
            }
            Assert.That(queue.Count, Is.EqualTo(items.Length));

            var read = new List<int>();
            while (queue.TryRead(out int item))
            {
                read.Add(item);
            }

            int[] expected = [.. items];
            Array.Sort(expected);
            Assert.That(read, Is.EqualTo(expected));
            Assert.That(queue.Count, Is.Zero);
        }

        [Test]
        public async Task WaitIsCompletedByWriteAsync()
        {
            using var queue = new SingleReaderPriorityQueue<int>((a, b) => a - b);
            for (int round = 0; round < 3; round++)
            {
                ValueTask<bool> wait = queue.WaitToReadAsync(CancellationToken.None);
                Assert.That(wait.IsCompleted, Is.False);

                Assert.That(queue.TryWrite(round), Is.True);

                Assert.That(await wait, Is.True);
                Assert.That(queue.TryRead(out int item), Is.True);
                Assert.That(item, Is.EqualTo(round));
            }
        }

        [Test]
        public async Task WaitReturnsTrueWhileItemsAreQueuedAsync()
        {
            using var queue = new SingleReaderPriorityQueue<int>((a, b) => a - b);
            queue.TryWrite(1);

            ValueTask<bool> wait = queue.WaitToReadAsync(CancellationToken.None);

            Assert.That(wait.IsCompleted, Is.True);
            Assert.That(await wait, Is.True);
        }

        [Test]
        public async Task CompleteDrainsQueuedItemsThenEndsAsync()
        {
            using var queue = new SingleReaderPriorityQueue<int>((a, b) => a - b);
            queue.TryWrite(2);
            queue.TryWrite(1);

            Assert.That(queue.TryComplete(), Is.True);
            Assert.That(queue.TryComplete(), Is.False);
            Assert.That(queue.TryWrite(3), Is.False);

            Assert.That(await queue.WaitToReadAsync(CancellationToken.None), Is.True);
            Assert.That(queue.TryRead(out int first), Is.True);
            Assert.That(queue.TryRead(out int second), Is.True);
            Assert.That((first, second), Is.EqualTo((1, 2)));
            Assert.That(await queue.WaitToReadAsync(CancellationToken.None), Is.False);
        }

        [Test]
        public async Task CompleteWakesPendingWaitAsync()
        {
            using var queue = new SingleReaderPriorityQueue<int>((a, b) => a - b);
            ValueTask<bool> wait = queue.WaitToReadAsync(CancellationToken.None);

            queue.TryComplete();

            Assert.That(await wait, Is.False);
        }

        [Test]
        public async Task CancellationFaultsPendingWaitAsync()
        {
            using var queue = new SingleReaderPriorityQueue<int>((a, b) => a - b);
            using var cts = new CancellationTokenSource();

            // First wait registers the token; it must still cancel a later wait.
            ValueTask<bool> wait = queue.WaitToReadAsync(cts.Token);
            queue.TryWrite(1);
            Assert.That(await wait, Is.True);
            Assert.That(queue.TryRead(out _), Is.True);

            wait = queue.WaitToReadAsync(cts.Token);
            Assert.That(wait.IsCompleted, Is.False);
            cts.Cancel();

            OperationCanceledException? ex = await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await wait);
            Assert.That(ex!.CancellationToken, Is.EqualTo(cts.Token));
            Assert.That(queue.TryWrite(2), Is.True);
            await Assert.ThrowsAsync<TaskCanceledException>(
                async () => await queue.WaitToReadAsync(cts.Token));
        }

        [Test]
        public async Task ConcurrentWritersAreAllReadAsync()
        {
            const int kWriters = 4;
            const int kPerWriter = 5000;
            using var queue = new SingleReaderPriorityQueue<int>((a, b) => a - b);

            Task reader = Task.Run(async () =>
            {
                int count = 0;
                while (await queue.WaitToReadAsync(CancellationToken.None))
                {
                    while (queue.TryRead(out _))
                    {
                        count++;
                    }
                }
                Assert.That(count, Is.EqualTo(kWriters * kPerWriter));
            });

            await Task.WhenAll(Enumerable.Range(0, kWriters).Select(w => Task.Run(() =>
            {
                for (int i = 0; i < kPerWriter; i++)
                {
                    Assert.That(queue.TryWrite((w * kPerWriter) + i), Is.True);
                }
            })));
            queue.TryComplete();

            Assert.That(
                await Task.WhenAny(reader, Task.Delay(TimeSpan.FromSeconds(30))),
                Is.SameAs(reader));
            await reader;
        }

        [Test]
        public async Task WaitThatLostARaceIsCompletedByLaterWriteAsync()
        {
            // MessageProcessor races the wait against the gap timer and keeps
            // the losing wait outstanding instead of starting a new one.
            using var queue = new SingleReaderPriorityQueue<int>((a, b) => a - b);
            using var cts = new CancellationTokenSource();
            Task<bool> wait = queue.WaitToReadAsync(cts.Token).AsTask();
            var timer = new TaskCompletionSource<bool>();
            timer.SetResult(true);

            Assert.That(await Task.WhenAny(wait, timer.Task), Is.SameAs(timer.Task));
            Assert.That(wait.IsCompleted, Is.False);

            Assert.That(queue.TryWrite(7), Is.True);
            Assert.That(await wait, Is.True);
            Assert.That(queue.TryRead(out int item), Is.True);
            Assert.That(item, Is.EqualTo(7));

            // The waiter is reusable after the outstanding wait completed.
            ValueTask<bool> next = queue.WaitToReadAsync(cts.Token);
            Assert.That(next.IsCompleted, Is.False);
            queue.TryWrite(8);
            Assert.That(await next, Is.True);
        }

        [Test]
        public async Task CancellationRacingWriteCompletesWaitOnceAsync()
        {
            using var queue = new SingleReaderPriorityQueue<int>((a, b) => a - b);
            int written = 0;
            int read = 0;
            for (int i = 0; i < 2000; i++)
            {
                // A new token per round also exercises re-registration.
                using var cts = new CancellationTokenSource();
                ValueTask<bool> wait = queue.WaitToReadAsync(cts.Token);
                using var start = new Barrier(2);
                Task cancel = Task.Run(() =>
                {
                    start.SignalAndWait();
                    cts.Cancel();
                });
                Task write = Task.Run(() =>
                {
                    start.SignalAndWait();
                    Assert.That(queue.TryWrite(i), Is.True);
                });
                try
                {
                    Assert.That(await wait, Is.True);
                }
                catch (OperationCanceledException)
                {
                }
                await Task.WhenAll(cancel, write);
                written++;
                while (queue.TryRead(out _))
                {
                    read++;
                }
            }

            // No write was lost and the waiter still works afterwards.
            Assert.That(read, Is.EqualTo(written));
            ValueTask<bool> last = queue.WaitToReadAsync(CancellationToken.None);
            queue.TryWrite(-1);
            Assert.That(await last, Is.True);
        }

        [Test]
        public async Task CompletionRacingWriteKeepsAcceptedItemsAsync()
        {
            for (int i = 0; i < 2000; i++)
            {
                using var queue = new SingleReaderPriorityQueue<int>((a, b) => a - b);
                ValueTask<bool> wait = queue.WaitToReadAsync(CancellationToken.None);
                using var start = new Barrier(2);
                bool accepted = false;
                Task complete = Task.Run(() =>
                {
                    start.SignalAndWait();
                    queue.TryComplete();
                });
                Task write = Task.Run(() =>
                {
                    start.SignalAndWait();
                    accepted = queue.TryWrite(i);
                });

                bool readable = await wait;
                await Task.WhenAll(complete, write);

                // An accepted write is always readable; after completion
                // the next wait reports the end once drained.
                Assert.That(readable || !accepted, Is.True);
                Assert.That(queue.TryRead(out _), Is.EqualTo(accepted));
                Assert.That(await queue.WaitToReadAsync(CancellationToken.None), Is.False);
            }
        }
    }
}
