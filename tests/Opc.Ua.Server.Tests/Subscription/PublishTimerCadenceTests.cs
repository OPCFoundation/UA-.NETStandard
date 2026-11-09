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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// The publish timer loop starts its passes on a fixed cadence: the time a pass takes
    /// is not added to the period, and a pass that overruns the period is followed by one
    /// pass at once instead of a burst of catch-up passes. A pass runs on the sweep thread
    /// until it uses up its budget and checks the remaining queues in parallel.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public sealed class PublishTimerCadenceTests
    {
        private const int kPeriod = 100;
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

        [Test]
        public async Task PassDurationDoesNotDelayTheNextPassAsync()
        {
            var clock = new DelayRecordingTimeProvider();
            List<long> starts = await RunPassesAsync(clock, [40, 40, 40, 40, 40]).ConfigureAwait(false);

            Assert.That(starts, Is.EqualTo(new long[] { 0, 100, 200, 300, 400 }));
            Assert.That(clock.Delays, Is.EqualTo(new long[] { 60, 60, 60, 60 }));
        }

        [Test]
        public async Task OverrunStartsOneImmediatePassWithoutCatchUpBurstAsync()
        {
            var clock = new DelayRecordingTimeProvider();

            // the second pass takes 2.5 periods; the periods at 200 and 300 are missed.
            List<long> starts = await RunPassesAsync(clock, [10, 250, 10, 10, 10]).ConfigureAwait(false);

            // no wait follows the overrun, and the cadence restarts at 350.
            Assert.That(starts, Is.EqualTo(new long[] { 0, 100, 350, 450, 550 }));
            Assert.That(clock.Delays, Is.EqualTo(new long[] { 90, 90, 90 }));
        }

        [Test]
        public async Task LateWakeRestartsTheCadenceWithoutCatchUpPassAsync()
        {
            var clock = new DelayRecordingTimeProvider();

            // the first wait ends 250 ms after its deadline (a GC pause or a starved pool).
            List<long> starts = await RunPassesAsync(clock, [10, 10, 10, 10], lateWakes: [250]).ConfigureAwait(false);

            // the late pass at 350 restarts the cadence; no extra pass follows it at once.
            Assert.That(starts, Is.EqualTo(new long[] { 0, 350, 450, 550 }));
            Assert.That(clock.Delays, Is.EqualTo(new long[] { 90, 90, 90 }));
        }

        [Test]
        public async Task SmallWakeJitterKeepsTheCadenceAsync()
        {
            var clock = new DelayRecordingTimeProvider();

            // a wait that ends 15 ms late does not move the following deadlines.
            List<long> starts = await RunPassesAsync(clock, [10, 10, 10], lateWakes: [15]).ConfigureAwait(false);

            Assert.That(starts, Is.EqualTo(new long[] { 0, 115, 200 }));
            Assert.That(clock.Delays, Is.EqualTo(new long[] { 90, 75 }));
        }

        [Test]
        public async Task CancellationStopsTheLoopWhileWaitingAsync()
        {
            var clock = new DelayRecordingTimeProvider();
            using var cts = new CancellationTokenSource();
            int passes = 0;

            Task loop = SubscriptionManager.RunAtFixedPeriodAsync(
                clock,
                kPeriod,
                () =>
                {
                    passes++;
                    return true;
                },
                cts.Token).AsTask();

            Assert.That(await clock.TimerCreated.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            cts.Cancel();

            Assert.That(
                async () => await loop.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(passes, Is.EqualTo(1));
        }

        [Test]
        public void CheapPassChecksEveryQueueOnTheCallingThread()
        {
            var clock = new FakeTimeProvider();
            int[] queues = CreateQueues(1000);
            var checkedOn = new ConcurrentDictionary<int, int>();

            bool parallel = SubscriptionManager.CheckPublishTimers(
                queues,
                queue => checkedOn.TryAdd(queue, Environment.CurrentManagedThreadId),
                clock,
                BudgetTicks(clock, 25));

            Assert.That(parallel, Is.False);
            Assert.That(checkedOn.Keys, Is.EquivalentTo(queues));
            Assert.That(
                checkedOn.Values,
                Is.All.EqualTo(Environment.CurrentManagedThreadId));
        }

        [Test]
        public void PassOverBudgetChecksTheRemainingQueuesOnceInParallel()
        {
            var clock = new FakeTimeProvider();
            int[] queues = CreateQueues(200);
            var checks = new ConcurrentDictionary<int, int>();

            // the first three queues take 10 ms each, so the budget is used up after the third.
            bool parallel = SubscriptionManager.CheckPublishTimers(
                queues,
                queue =>
                {
                    checks.AddOrUpdate(queue, 1, (_, count) => count + 1);
                    if (queue < 3)
                    {
                        clock.Advance(TimeSpan.FromMilliseconds(10));
                    }
                },
                clock,
                BudgetTicks(clock, 25));

            Assert.That(parallel, Is.True);
            Assert.That(checks.Keys, Is.EquivalentTo(queues));
            Assert.That(checks.Values, Is.All.EqualTo(1));
        }

        private static int[] CreateQueues(int count)
        {
            int[] queues = new int[count];
            for (int ii = 0; ii < count; ii++)
            {
                queues[ii] = ii;
            }
            return queues;
        }

        private static long BudgetTicks(TimeProvider clock, int milliseconds)
        {
            return milliseconds * clock.TimestampFrequency / 1000;
        }

        /// <summary>
        /// Runs one pass per entry, each advancing the fake clock by its duration, and
        /// returns the clock reading at the start of every pass. Entry n of
        /// <paramref name="lateWakes"/> delays the end of the n-th wait beyond its deadline.
        /// </summary>
        private static async Task<List<long>> RunPassesAsync(
            DelayRecordingTimeProvider clock,
            long[] durations,
            long[]? lateWakes = null)
        {
            var starts = new List<long>();
            long origin = clock.GetTimestampMilliseconds();
            int pass = 0;

            Task loop = SubscriptionManager.RunAtFixedPeriodAsync(
                clock,
                kPeriod,
                () =>
                {
                    starts.Add(clock.GetTimestampMilliseconds() - origin);
                    clock.Fake.Advance(TimeSpan.FromMilliseconds(durations[pass]));
                    return ++pass < durations.Length;
                },
                CancellationToken.None).AsTask();

            // fire each wait the loop starts until the last pass returned false.
            while (true)
            {
                Task<bool> timer = clock.TimerCreated.WaitAsync(s_timeout);
                if (await Task.WhenAny(loop, timer).ConfigureAwait(false) == loop)
                {
                    break;
                }
                Assert.That(await timer.ConfigureAwait(false), Is.True, "the loop did not wait");
                int wait = clock.Delays.Count - 1;
                long late = lateWakes != null && wait < lateWakes.Length ? lateWakes[wait] : 0;
                clock.Fake.Advance(TimeSpan.FromMilliseconds(clock.Delays[wait] + late));
            }

            await loop.ConfigureAwait(false);
            Assert.That(pass, Is.EqualTo(durations.Length));
            return starts;
        }

        /// <summary>
        /// A fake clock that records the due time of every timer the loop creates.
        /// </summary>
        private sealed class DelayRecordingTimeProvider : TimeProvider
        {
            public FakeTimeProvider Fake { get; } = new FakeTimeProvider();

            public List<long> Delays { get; } = [];

            public SemaphoreSlim TimerCreated { get; } = new SemaphoreSlim(0);

            public override long TimestampFrequency => Fake.TimestampFrequency;

            public override long GetTimestamp()
            {
                return Fake.GetTimestamp();
            }

            public override DateTimeOffset GetUtcNow()
            {
                return Fake.GetUtcNow();
            }

            public override ITimer CreateTimer(
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                ITimer timer = Fake.CreateTimer(callback, state, dueTime, period);
                lock (Delays)
                {
                    Delays.Add((long)dueTime.TotalMilliseconds);
                }
                TimerCreated.Release();
                return timer;
            }
        }
    }
}
