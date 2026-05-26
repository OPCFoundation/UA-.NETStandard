/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
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
    }
}