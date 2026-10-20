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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Verifies that request timeouts and linked tokens are enforced by the shared sweep of
    /// <see cref="RequestDeadlineMonitor"/> within its resolution, with exactly one terminal
    /// state per request and no lifetime left behind.
    /// </summary>
    [TestFixture]
    [Category("RequestLifetime")]
    [Parallelizable]
    public class RequestDeadlineMonitorTests
    {
        [Test]
        public void TimeoutIsCancelledByTheFirstSweepAfterTheDeadline()
        {
            var clock = new FakeClock();
            using var monitor = new RequestDeadlineMonitor(TimeSpan.FromMilliseconds(20), clock.Read, automatic: false);
            using var lifetime = new RequestLifetime(monitor, TimeSpan.FromMilliseconds(100), default, default);
            Assert.That(monitor.Count, Is.EqualTo(1));

            clock.Advance(TimeSpan.FromMilliseconds(99));
            Assert.That(monitor.Sweep(), Is.Zero);
            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.False);

            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(monitor.Sweep(), Is.EqualTo(1));
            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good), "a timeout reports Good, mapped to BadTimeout by the endpoint");
            Assert.That(lifetime.TryCancel(StatusCodes.BadUnexpectedError), Is.False);
            Assert.That(monitor.Count, Is.Zero);
        }

        [Test]
        public void LifetimeWithoutTimeoutOrCancellableTokensIsNotMonitored()
        {
            using var monitor = new RequestDeadlineMonitor(TimeSpan.FromMilliseconds(20), new FakeClock().Read, automatic: false);
            using var lifetime = new RequestLifetime(monitor, Timeout.InfiniteTimeSpan, CancellationToken.None, default);

            Assert.That(monitor.Count, Is.Zero);
            Assert.That(lifetime.TryCancel(StatusCodes.BadUnexpectedError), Is.True);
        }

        [Test]
        public void CancelledLinkedTokenIsPropagatedBySweep()
        {
            var clock = new FakeClock();
            using var monitor = new RequestDeadlineMonitor(TimeSpan.FromMilliseconds(20), clock.Read, automatic: false);
            using var server = new CancellationTokenSource();
            using var transport = new CancellationTokenSource();
            using var lifetime = new RequestLifetime(monitor, Timeout.InfiniteTimeSpan, server.Token, transport.Token);

            transport.Cancel();
            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.False, "propagated by the sweep, not a registration");
            Assert.That(monitor.Sweep(), Is.EqualTo(1));
            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(monitor.Count, Is.Zero);
        }

        [Test]
        public void LinkedTokenCancelledBeforeAnExplicitCancellationOwnsTheTerminalState()
        {
            using var monitor = new RequestDeadlineMonitor(TimeSpan.FromMilliseconds(20), new FakeClock().Read, automatic: false);
            using var server = new CancellationTokenSource();
            using var lifetime = new RequestLifetime(monitor, TimeSpan.FromMinutes(1), server.Token, default);

            server.Cancel();

            // not swept yet: the explicit cancellation still loses to the earlier token.
            Assert.That(lifetime.TryCancel(StatusCodes.BadUnexpectedError), Is.False);
            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(monitor.Sweep(), Is.Zero, "a cancelled lifetime is not cancelled twice");
            lifetime.Dispose();
            Assert.That(monitor.Count, Is.Zero);
        }

        [Test]
        public void AlreadyCancelledLinkedTokenCancelsAtConstructionWithoutMonitoring()
        {
            using var monitor = new RequestDeadlineMonitor(TimeSpan.FromMilliseconds(20), new FakeClock().Read, automatic: false);
            using var server = new CancellationTokenSource();
            server.Cancel();
            using var lifetime = new RequestLifetime(monitor, TimeSpan.FromMinutes(1), server.Token, default);

            Assert.That(lifetime.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(monitor.Count, Is.Zero);
        }

        [Test]
        public void CompletedLifetimesLeaveTheMonitor()
        {
            using var monitor = new RequestDeadlineMonitor(TimeSpan.FromMilliseconds(20), new FakeClock().Read, automatic: false);
            using var server = new CancellationTokenSource();
            var lifetimes = new List<RequestLifetime>();
            for (int ii = 0; ii < 1000; ii++)
            {
                lifetimes.Add(new RequestLifetime(monitor, TimeSpan.FromMinutes(1), server.Token, default));
            }
            Assert.That(monitor.Count, Is.EqualTo(1000));

            Parallel.ForEach(lifetimes, lifetime => lifetime.MarkCompleted());

            Assert.That(monitor.Count, Is.Zero);
            server.Cancel();
            Assert.That(monitor.Sweep(), Is.Zero);
            Assert.That(lifetimes.TrueForAll(lifetime => !lifetime.CancellationToken.IsCancellationRequested), Is.True);
        }

        /// <summary>
        /// The sweep, an explicit cancellation and completion race for the same requests: each
        /// request has one terminal owner, its callback runs at most once and observes the
        /// winning status, and every request leaves the monitor.
        /// </summary>
        [Test]
        [NonParallelizable]
        public async Task SweepCancelAndCompleteRaceHasOneTerminalOwnerAsync()
        {
            const int attempts = 4096;
            var clock = new FakeClock();
            using var monitor = new RequestDeadlineMonitor(TimeSpan.FromMilliseconds(20), clock.Read, automatic: false);
            var lifetimes = new RequestLifetime[attempts];
            var callbacks = new int[attempts];
            var observed = new StatusCode[attempts];
            var cancelWon = new bool[attempts];
            for (int ii = 0; ii < attempts; ii++)
            {
                int index = ii;
                lifetimes[ii] = new RequestLifetime(monitor, TimeSpan.FromMilliseconds(10), default, default);
                lifetimes[ii].CancellationToken.Register(() =>
                {
                    observed[index] = lifetimes[index].StatusCode;
                    Interlocked.Increment(ref callbacks[index]);
                });
            }
            clock.Advance(TimeSpan.FromMilliseconds(10));

            using var start = new Barrier(3);
            Task sweeper = Task.Run(() =>
            {
                start.SignalAndWait();
                monitor.Sweep();
            });
            Task canceller = Task.Run(() =>
            {
                start.SignalAndWait();
                for (int ii = attempts - 1; ii >= 0; ii--)
                {
                    cancelWon[ii] = lifetimes[ii].TryCancel(StatusCodes.BadUnexpectedError);
                }
            });
            Task completer = Task.Run(() =>
            {
                start.SignalAndWait();
                for (int ii = 0; ii < attempts; ii += 2)
                {
                    lifetimes[ii].MarkCompleted();
                }
            });
            await Task.WhenAll(sweeper, canceller, completer).WaitAsync(TimeSpan.FromMinutes(1)).ConfigureAwait(false);

            int mismatches = 0;
            for (int ii = 0; ii < attempts; ii++)
            {
                StatusCode status = lifetimes[ii].StatusCode;
                bool cancelled = lifetimes[ii].CancellationToken.IsCancellationRequested;
                bool consistent = callbacks[ii] == (cancelled ? 1 : 0) &&
                    (!cancelled || observed[ii] == status) &&
                    (cancelWon[ii] == (status == StatusCodes.BadUnexpectedError)) &&
                    (cancelled || ii % 2 == 0);
                if (!consistent)
                {
                    mismatches++;
                }
            }

            Assert.That(mismatches, Is.Zero);
            foreach (RequestLifetime lifetime in lifetimes)
            {
                lifetime.Dispose();
            }
            Assert.That(monitor.Count, Is.Zero);
        }

        /// <summary>
        /// The timer stops whenever the monitor runs empty and starts again with the next
        /// lifetime. Lifetimes added while it stops, from several threads, must still be swept.
        /// </summary>
        [Test]
        [NonParallelizable]
        public async Task LifetimesAddedWhileTheTimerStopsAreStillSweptAsync()
        {
            using var monitor = new RequestDeadlineMonitor(TimeSpan.FromMilliseconds(1));
            int missed = 0;
            var workers = new Task[4];
            for (int worker = 0; worker < workers.Length; worker++)
            {
                workers[worker] = Task.Run(() =>
                {
                    for (int ii = 0; ii < 250; ii++)
                    {
                        using var lifetime = new RequestLifetime(monitor, TimeSpan.FromMilliseconds(1), default, default);
                        if (!lifetime.CancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                        {
                            Interlocked.Increment(ref missed);
                        }
                    }
                });
            }

            await Task.WhenAll(workers).WaitAsync(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            Assert.That(missed, Is.Zero, "a lifetime was never swept");
            Assert.That(monitor.Count, Is.Zero);
        }

        [Test]
        public void SharedMonitorCancelsATimedOutRequestWithinItsResolution()
        {
            var watch = Stopwatch.StartNew();
            using var lifetime = new RequestLifetime(TimeSpan.FromMilliseconds(50), default, default);

            Assert.That(lifetime.CancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)), Is.True);
            watch.Stop();
            Assert.That(watch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(45)));
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));
            TestContext.Out.WriteLine($"50 ms timeout fired after {watch.Elapsed.TotalMilliseconds:F1} ms");
        }

        [Test]
        public void SharedMonitorPropagatesALinkedTokenWithinItsResolution()
        {
            using var server = new CancellationTokenSource();
            using var lifetime = new RequestLifetime(Timeout.InfiniteTimeSpan, server.Token, default);

            server.Cancel();

            Assert.That(lifetime.CancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)), Is.True);
            Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        private sealed class FakeClock
        {
            public long Read()
            {
                return Interlocked.Read(ref m_now);
            }

            public void Advance(TimeSpan time)
            {
                Interlocked.Add(ref m_now, (long)(time.TotalSeconds * Stopwatch.Frequency));
            }

            private long m_now = 1_000_000;
        }
    }
}
