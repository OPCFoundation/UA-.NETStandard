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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// Covers the coalesced wake token of the fair request queue's workers.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Parallelizable]
    public sealed class WorkerWakeSignalTests
    {
        [Test]
        public async Task TokenIssuedWithoutAWaiterIsTakenByTheNextWaitAsync()
        {
            var signal = new WorkerWakeSignal();
            var waiter = new WorkerWakeSignal.Waiter(signal);
            Assert.That(signal.TryIssue(), Is.True);
            Assert.That(signal.IsPending, Is.True);

            ValueTask wait = signal.WaitAsync(waiter, CancellationToken.None);
            Assert.That(wait.IsCompletedSuccessfully, Is.True);
            await wait.ConfigureAwait(false);
            Assert.That(signal.IsPending, Is.False);
            Assert.That(signal.ParkedCount, Is.Zero);
        }

        [Test]
        public async Task IssuesAreCoalescedUntilTheWokenWaiterConsumesTheTokenAsync()
        {
            var signal = new WorkerWakeSignal();
            var first = new WorkerWakeSignal.Waiter(signal);
            var second = new WorkerWakeSignal.Waiter(signal);
            ValueTask firstWait = signal.WaitAsync(first, CancellationToken.None);
            ValueTask secondWait = signal.WaitAsync(second, CancellationToken.None);
            Assert.That(signal.ParkedCount, Is.EqualTo(2));

            Assert.That(signal.TryIssue(), Is.True);
            // the token is handed to a waiter that has not run yet
            Assert.That(signal.TryIssue(), Is.False);
            Assert.That(signal.TryIssue(), Is.False);
            Assert.That(signal.ParkedCount, Is.EqualTo(1));

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await secondWait.AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(signal.IsPending, Is.False);
            Assert.That(firstWait.IsCompleted, Is.False);

            Assert.That(signal.TryIssue(), Is.True);
            await firstWait.AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(signal.ParkedCount, Is.Zero);
        }

        [Test]
        public async Task WakesTheMostRecentlyParkedWaiterFirstAsync()
        {
            var signal = new WorkerWakeSignal();
            var waiters = new WorkerWakeSignal.Waiter[4];
            var waits = new Task[waiters.Length];
            for (int ii = 0; ii < waiters.Length; ii++)
            {
                waiters[ii] = new WorkerWakeSignal.Waiter(signal);
                waits[ii] = signal.WaitAsync(waiters[ii], CancellationToken.None).AsTask();
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            for (int ii = waiters.Length - 1; ii >= 0; ii--)
            {
                Assert.That(signal.TryIssue(), Is.True);
                Task woken = await Task.WhenAny(waits).WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(woken, Is.SameAs(waits[ii]));
                await woken.ConfigureAwait(false);
                waits[ii] = new TaskCompletionSource<bool>().Task;
            }
        }

        [Test]
        public async Task WokenWaiterContinuesOnAnotherThreadThanTheIssuerAsync()
        {
            var signal = new WorkerWakeSignal();
            var waiter = new WorkerWakeSignal.Waiter(signal);
            int issuer = -1;
            int continuation = -1;
            bool issued = false;
            var parked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task wait = Task.Run(async () =>
            {
                ValueTask pending = signal.WaitAsync(waiter, CancellationToken.None);
                parked.SetResult(true);
                await pending.ConfigureAwait(false);
                continuation = Environment.CurrentManagedThreadId;
            });
            await parked.Task.ConfigureAwait(false);
            var issuing = new Thread(() =>
            {
                issuer = Environment.CurrentManagedThreadId;
                issued = signal.TryIssue();
                // the issuer returns before the woken worker runs on it
                Thread.Sleep(50);
            });
            issuing.Start();
            issuing.Join();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await wait.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(issued, Is.True);
            Assert.That(continuation, Is.Not.EqualTo(issuer));
        }

        [Test]
        public async Task CancelledWaiterLeavesTheStackAndTheNextIssueWakesAnotherAsync()
        {
            var signal = new WorkerWakeSignal();
            var first = new WorkerWakeSignal.Waiter(signal);
            var second = new WorkerWakeSignal.Waiter(signal);
            using var cancellation = new CancellationTokenSource();
            Task firstWait = signal.WaitAsync(first, CancellationToken.None).AsTask();
            Task secondWait = signal.WaitAsync(second, cancellation.Token).AsTask();
            cancellation.Cancel();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await Assert.CatchAsync<OperationCanceledException>(
                async () => await secondWait.WaitAsync(deadline.Token).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(signal.ParkedCount, Is.EqualTo(1));

            Assert.That(signal.TryIssue(), Is.True);
            await firstWait.WaitAsync(deadline.Token).ConfigureAwait(false);

            // the cancelled waiter is reusable
            ValueTask again = signal.WaitAsync(second, CancellationToken.None);
            Assert.That(signal.TryIssue(), Is.True);
            await again.AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
        }

        [Test]
        public async Task CancelledWaiterBelowTheTopLeavesTheStackInOrderAsync()
        {
            var signal = new WorkerWakeSignal();
            using var cancellation = new CancellationTokenSource();
            Task bottom = signal.WaitAsync(new WorkerWakeSignal.Waiter(signal), CancellationToken.None).AsTask();
            Task middle = signal.WaitAsync(new WorkerWakeSignal.Waiter(signal), cancellation.Token).AsTask();
            Task top = signal.WaitAsync(new WorkerWakeSignal.Waiter(signal), CancellationToken.None).AsTask();
            cancellation.Cancel();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await Assert.CatchAsync<OperationCanceledException>(
                async () => await middle.WaitAsync(deadline.Token).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(signal.ParkedCount, Is.EqualTo(2));

            Assert.That(signal.TryIssue(), Is.True);
            await top.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(bottom.IsCompleted, Is.False);
            Assert.That(signal.TryIssue(), Is.True);
            await bottom.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(signal.ParkedCount, Is.Zero);

            // closing twice is harmless
            signal.Close();
            signal.Close();
        }

        [Test]
        public async Task CancellationRacingAWakeNeverLosesTheTokenAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            for (int round = 0; round < 500; round++)
            {
                var signal = new WorkerWakeSignal();
                var stayer = new WorkerWakeSignal.Waiter(signal);
                var leaver = new WorkerWakeSignal.Waiter(signal);
                using var cancellation = new CancellationTokenSource();
                Task stayerWait = signal.WaitAsync(stayer, CancellationToken.None).AsTask();
                Task leaverWait = signal.WaitAsync(leaver, cancellation.Token).AsTask();

                using var start = new Barrier(2);
                Task issue = Task.Run(() =>
                {
                    start.SignalAndWait();
                    Assert.That(signal.TryIssue(), Is.True);
                });
                Task cancel = Task.Run(() =>
                {
                    start.SignalAndWait();
                    cancellation.Cancel();
                });
                await Task.WhenAll(issue, cancel).WaitAsync(deadline.Token).ConfigureAwait(false);

                // exactly one waiter has the token: the leaver if the wake came first,
                // otherwise the stayer
                bool leaverWoken;
                try
                {
                    await leaverWait.WaitAsync(deadline.Token).ConfigureAwait(false);
                    leaverWoken = true;
                }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested)
                {
                    leaverWoken = false;
                }
                if (leaverWoken)
                {
                    Assert.That(stayerWait.IsCompleted, Is.False, $"round {round}");
                    signal.Close();
                    await Assert.CatchAsync<OperationCanceledException>(
                        async () => await stayerWait.WaitAsync(deadline.Token).ConfigureAwait(false))
                        .ConfigureAwait(false);
                }
                else
                {
                    await stayerWait.WaitAsync(deadline.Token).ConfigureAwait(false);
                }
                Assert.That(signal.IsPending, Is.False, $"round {round}");
                Assert.That(signal.ParkedCount, Is.Zero, $"round {round}");
            }
        }

        [Test]
        public async Task CloseEndsParkedAndLaterWaitsAsync()
        {
            var signal = new WorkerWakeSignal();
            var waiters = new Task[3];
            for (int ii = 0; ii < waiters.Length; ii++)
            {
                waiters[ii] = signal.WaitAsync(new WorkerWakeSignal.Waiter(signal), CancellationToken.None).AsTask();
            }
            signal.Close();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            foreach (Task waiter in waiters)
            {
                await Assert.CatchAsync<OperationCanceledException>(
                    async () => await waiter.WaitAsync(deadline.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }
            Assert.That(signal.ParkedCount, Is.Zero);
            await Assert.CatchAsync<OperationCanceledException>(async () => await signal.WaitAsync(
                new WorkerWakeSignal.Waiter(signal), CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task NoWakeIsLostBetweenIssuersAndReusedWaitersAsync()
        {
            const int workers = 6;
            const int items = 20000;
            var signal = new WorkerWakeSignal();
            int queued = 0;
            int taken = 0;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // A miniature of the queue protocol: publish an item, then issue; take items until
            // none is left, then wait; a woken worker issues again while items remain.
            bool TryTake()
            {
                while (true)
                {
                    int current = Volatile.Read(ref queued);
                    if (current == 0)
                    {
                        return false;
                    }
                    if (Interlocked.CompareExchange(ref queued, current - 1, current) == current)
                    {
                        if (Interlocked.Increment(ref taken) == items)
                        {
                            done.TrySetResult(true);
                        }
                        return true;
                    }
                }
            }

            Task[] consumers = new Task[workers];
            for (int w = 0; w < workers; w++)
            {
                consumers[w] = Task.Run(async () =>
                {
                    var waiter = new WorkerWakeSignal.Waiter(signal);
                    try
                    {
                        while (true)
                        {
                            while (TryTake())
                            {
                                if (Volatile.Read(ref queued) > 0)
                                {
                                    signal.TryIssue();
                                }
                            }
                            await signal.WaitAsync(waiter, CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // closed
                    }
                });
            }
            Task[] producers = new Task[3];
            for (int p = 0; p < producers.Length; p++)
            {
                int share = items / producers.Length + (p < items % producers.Length ? 1 : 0);
                producers[p] = Task.Run(() =>
                {
                    for (int ii = 0; ii < share; ii++)
                    {
                        Interlocked.Increment(ref queued);
                        signal.TryIssue();
                        if (ii % 32 == 0)
                        {
                            Thread.Yield();
                        }
                    }
                });
            }
            await Task.WhenAll(producers).WaitAsync(deadline.Token).ConfigureAwait(false);
            await done.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            signal.Close();
            await Task.WhenAll(consumers).WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(taken, Is.EqualTo(items));
            Assert.That(queued, Is.Zero);
        }
    }
}
