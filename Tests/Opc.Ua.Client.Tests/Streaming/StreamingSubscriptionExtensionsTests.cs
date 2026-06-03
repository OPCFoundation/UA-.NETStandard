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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Subscriptions.Streaming;

// Test assertions use literal expected-value arrays inline; lifting
// every one to a static field per CA1861 would only add noise.
// Tests run on the default TaskScheduler so CA2007's sync-context
// risk does not apply.
#pragma warning disable CA1861, CA2007

namespace Opc.Ua.Client.Tests.Streaming
{
    [TestFixture, Category("StreamingExtensions"), Parallelizable]
    public class StreamingSubscriptionExtensionsTests
    {
        [Test]
        public async Task TakeUntilAsyncStopsWhenPredicateMatches()
        {
            var items = new List<int>();
            await foreach (int item in Range(10).TakeUntilAsync(x => x == 3))
            {
                items.Add(item);
            }
            Assert.That(items, Is.EqualTo(new[] { 0, 1, 2, 3 }));
        }

        [Test]
        public async Task TakeAsyncTakesExactlyN()
        {
            var items = new List<int>();
            await foreach (int item in Range(10).TakeAsync(4))
            {
                items.Add(item);
            }
            Assert.That(items, Is.EqualTo(new[] { 0, 1, 2, 3 }));
        }

        [Test]
        public async Task BufferedAsyncCollectsN()
        {
            IReadOnlyList<int> buffer = await Range(10).BufferedAsync(3);
            Assert.That(buffer, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public async Task WithTimeoutAsyncCompletesWhenTimeoutElapses()
        {
            var items = new List<int>();
            await foreach (int item in InfiniteWithDelay(50)
                .WithTimeoutAsync(TimeSpan.FromMilliseconds(80)))
            {
                items.Add(item);
                if (items.Count > 100)
                {
                    break;
                }
            }
            Assert.That(items, Has.Count.LessThan(10));
        }

        [Test]
        public void TakeAsyncThrowsForNonPositiveCount()
        {
            Assert.That(() => Range(10).TakeAsync(0).GetAsyncEnumerator(),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void TakeUntilAsyncThrowsForNullPredicate()
        {
            Assert.That(() => Range(10).TakeUntilAsync(null!).GetAsyncEnumerator(),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void BufferedAsyncWithNullSourceThrowsArgumentNullException()
        {
            Assert.That(async () =>
                await StreamingSubscriptionExtensions.BufferedAsync<int>(null!, 5),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void BufferedAsyncWithNonPositiveCountThrowsArgumentOutOfRangeException(int count)
        {
            Assert.That(async () => await Range(3).BufferedAsync(count),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task BufferedAsyncReturnsShorterBatchWhenSourceEndsEarly()
        {
            IReadOnlyList<int> buffer = await Range(3).BufferedAsync(10);

            Assert.That(buffer, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(buffer, Has.Count.EqualTo(3));
        }

        [Test]
        public void TakeUntilAsyncRespectsExplicitCancellationToken()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(async () =>
            {
                await foreach (int _ in InfiniteWithDelay(0)
                    .TakeUntilAsync(_ => false, cts.Token))
                {
                    // No-op; the iterator should throw on first item.
                }
            }, Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void WithTimeoutAsyncWithNullSourceThrowsArgumentNullException()
        {
            Assert.That(
                () => StreamingSubscriptionExtensions
                    .WithTimeoutAsync<int>(null!, TimeSpan.FromMilliseconds(50)),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void WithTimeoutAsyncPropagatesOuterCancellationAsOperationCanceledException()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(async () =>
            {
                await foreach (int _ in InfiniteWithDelay(10)
                    .WithTimeoutAsync(TimeSpan.FromSeconds(5), ct: cts.Token))
                {
                    // Drains until the outer CT cancels — should throw OCE,
                    // not silently complete the way the internal timeout
                    // does.
                }
            }, Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task WithTimeoutAsyncDisposesEnumeratorOnExit()
        {
            var probe = new DisposeProbeEnumerable();

            await foreach (int _ in probe.WithTimeoutAsync(TimeSpan.FromMilliseconds(20)))
            {
                // drain until timeout fires
            }

            Assert.That(probe.LastEnumerator, Is.Not.Null);
            Assert.That(probe.LastEnumerator!.DisposeCalls, Is.GreaterThanOrEqualTo(1));
        }

        private static async IAsyncEnumerable<int> Range(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return i;
                await Task.Yield();
            }
        }

        private static async IAsyncEnumerable<int> InfiniteWithDelay(int delayMs,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            for (int i = 0; ; i++)
            {
                yield return i;
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// IAsyncEnumerable probe that records DisposeAsync calls on the
        /// enumerator returned to the caller. Used to verify that
        /// <c>WithTimeoutAsync</c> disposes the source enumerator in
        /// its finally block.
        /// </summary>
        private sealed class DisposeProbeEnumerable : IAsyncEnumerable<int>
        {
            public DisposeProbeEnumerator? LastEnumerator { get; private set; }

            public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                LastEnumerator = new DisposeProbeEnumerator(cancellationToken);
                return LastEnumerator;
            }
        }

        private sealed class DisposeProbeEnumerator : IAsyncEnumerator<int>
        {
            private readonly CancellationToken m_ct;
            public int DisposeCalls { get; private set; }

            public DisposeProbeEnumerator(CancellationToken ct)
            {
                m_ct = ct;
            }

            public int Current => 0;

            public ValueTask DisposeAsync()
            {
                DisposeCalls++;
                return default;
            }

            public async ValueTask<bool> MoveNextAsync()
            {
                // Block until the linked CT fires (the WithTimeoutAsync
                // internal timeout). Throws OCE which the production
                // code swallows; the finally block then disposes us.
                await Task.Delay(Timeout.Infinite, m_ct).ConfigureAwait(false);
                return false;
            }
        }
    }
}