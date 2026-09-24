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

#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Bindings
{
    [TestFixture]
    [Category("BufferManager")]
    [NonParallelizable]
    // TODO: Remove when CA2025 recognizes deliberate concurrent-disposal tests.
    [SuppressMessage("Reliability", "CA2025",
        Justification = "Registered renters deliberately outlive limiter disposal and are joined in finally.")]
    public sealed class BufferManagerMemoryLimiterWakeupTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void IdleCapacityChangesNeverAccumulateWakeups(int change)
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            for (int ii = 0; ii < 256; ii++)
            {
                long reservation = limiter.Reserve(8, CancellationToken.None);
                if (change == 0)
                {
                    limiter.Cancel(reservation);
                }
                else if (change == 1)
                {
                    Assert.That(limiter.TryBind(reservation, new byte[9]), Is.False);
                }
                else
                {
                    byte[] buffer = new byte[change == 2 ? 4 : 8];
                    Assert.That(limiter.TryBind(reservation, buffer), Is.True);
                    limiter.CompleteReturn(limiter.BeginReturn(buffer));
                }
            }
            Assert.That(limiter.WaitStateForTesting, Is.EqualTo((0, 0)));
        }

        [Test]
        public async Task SingleCapacityChangeWakesEveryRegisteredWaiterAsync()
        {
            using var limiter = new BufferManagerMemoryLimiter(16);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var allowWait = new ManualResetEventSlim();
            TaskCompletionSource<bool> registered = Signal();
            int registrations = 0;
            limiter.BeforeCapacityWaitForTesting = () =>
            {
                if (Interlocked.Increment(ref registrations) == 4)
                {
                    registered.TrySetResult(true);
                }
                allowWait.Wait(timeout.Token);
            };
            long held = limiter.Reserve(16, CancellationToken.None);
            Task<ReservationResult>[] renters =
            [
                ReserveAsync(limiter, 4, timeout.Token),
                ReserveAsync(limiter, 4, timeout.Token),
                ReserveAsync(limiter, 4, timeout.Token),
                ReserveAsync(limiter, 4, timeout.Token)
            ];
            try
            {
                await registered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((4, 0)));
                limiter.Cancel(held);
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((4, 4)));
                allowWait.Set();

                ReservationResult[] results = await Task.WhenAll(renters)
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                foreach (ReservationResult result in results)
                {
                    Assert.That(result.Error, Is.Null);
                    limiter.Cancel(result.Id);
                }
                Assert.That(registrations, Is.EqualTo(4), "No renter should spin through an obsolete wakeup.");
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((0, 0)));
            }
            finally
            {
                limiter.Dispose();
                allowWait.Set();
                await Task.WhenAll(renters).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RepeatedSignalsAndDisposalNeverOutnumberRegisteredWaitersAsync()
        {
            using var limiter = new BufferManagerMemoryLimiter(1);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var allowWait = new ManualResetEventSlim();
            TaskCompletionSource<bool> registered = Signal();
            int registrations = 0;
            limiter.BeforeCapacityWaitForTesting = () =>
            {
                if (Interlocked.Increment(ref registrations) == 3)
                {
                    registered.TrySetResult(true);
                }
                allowWait.Wait(timeout.Token);
            };
            long held = limiter.Reserve(1, CancellationToken.None);
            Task<ReservationResult>[] renters =
            [
                ReserveAsync(limiter, 1, timeout.Token),
                ReserveAsync(limiter, 1, timeout.Token),
                ReserveAsync(limiter, 1, timeout.Token)
            ];
            try
            {
                await registered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                limiter.Cancel(held);
                for (int ii = 0; ii < 256; ii++)
                {
                    limiter.Cancel(limiter.Reserve(1, CancellationToken.None));
                }
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((3, 3)));
                limiter.Dispose();
                limiter.Dispose();
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((3, 3)));
                allowWait.Set();

                ReservationResult[] results = await Task.WhenAll(renters)
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                foreach (ReservationResult result in results)
                {
                    Assert.That(result.Error, Is.TypeOf<ObjectDisposedException>());
                }
                Assert.That(registrations, Is.EqualTo(3));
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((0, 0)));
            }
            finally
            {
                limiter.Dispose();
                allowWait.Set();
                await Task.WhenAll(renters).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CanceledRegisteredWaiterCannotLeaveAStaleWakeupAsync()
        {
            using var limiter = new BufferManagerMemoryLimiter(1);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var cancelWaiter = new CancellationTokenSource();
            using var allowWait = new ManualResetEventSlim();
            TaskCompletionSource<bool> registered = Signal();
            limiter.BeforeCapacityWaitForTesting = () =>
            {
                registered.TrySetResult(true);
                allowWait.Wait(cancelWaiter.Token);
            };
            long held = limiter.Reserve(1, CancellationToken.None);
            Task<ReservationResult> renter = ReserveAsync(limiter, 1, cancelWaiter.Token);
            try
            {
                await registered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                limiter.Cancel(held);
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((1, 1)));
                held = limiter.Reserve(1, CancellationToken.None);
                cancelWaiter.Cancel();

                ReservationResult canceled = await renter.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(canceled.Error, Is.InstanceOf<OperationCanceledException>());
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((0, 0)));

                TaskCompletionSource<bool> nextRegistered = Signal();
                limiter.BeforeCapacityWaitForTesting = () =>
                {
                    nextRegistered.TrySetResult(true);
                    allowWait.Wait(timeout.Token);
                };
                renter = ReserveAsync(limiter, 1, timeout.Token);
                await nextRegistered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((1, 0)));
                limiter.Cancel(held);
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((1, 1)));
                allowWait.Set();

                ReservationResult acquired = await renter.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(acquired.Error, Is.Null);
                limiter.Cancel(acquired.Id);
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((0, 0)));
            }
            finally
            {
                limiter.Dispose();
                allowWait.Set();
                await renter.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CancelingOneRegisteredWaiterPreservesTheOthersWakeupAsync()
        {
            using var limiter = new BufferManagerMemoryLimiter(1);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var cancelFirst = new CancellationTokenSource();
            using var firstGate = new ManualResetEventSlim();
            using var secondGate = new ManualResetEventSlim();
            TaskCompletionSource<bool> firstRegistered = Signal();
            TaskCompletionSource<bool> bothRegistered = Signal();
            int registrations = 0;
            limiter.BeforeCapacityWaitForTesting = () =>
            {
                if (Interlocked.Increment(ref registrations) == 1)
                {
                    firstRegistered.TrySetResult(true);
                    firstGate.Wait(timeout.Token);
                }
                else
                {
                    bothRegistered.TrySetResult(true);
                    secondGate.Wait(timeout.Token);
                }
            };
            long held = limiter.Reserve(1, CancellationToken.None);
            Task<ReservationResult> first = ReserveAsync(limiter, 1, cancelFirst.Token);
            Task<ReservationResult>? second = null;
            try
            {
                await firstRegistered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                second = ReserveAsync(limiter, 1, timeout.Token);
                await bothRegistered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                limiter.Cancel(held);
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((2, 2)));
                cancelFirst.Cancel();
                firstGate.Set();
                ReservationResult canceled = await first.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(canceled.Error, Is.InstanceOf<OperationCanceledException>());
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((1, 1)));

                secondGate.Set();
                ReservationResult acquired = await second.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(acquired.Error, Is.Null);
                limiter.Cancel(acquired.Id);
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((0, 0)));
            }
            finally
            {
                limiter.Dispose();
                firstGate.Set();
                secondGate.Set();
                await first.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (second != null)
                {
                    await second.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task SmallerRenterProgressesWhileLargerRenterWaitsForMoreCapacityAsync()
        {
            using var limiter = new BufferManagerMemoryLimiter(8);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var cancelLarge = new CancellationTokenSource();
            using var largeGate = new ManualResetEventSlim();
            using var smallGate = new ManualResetEventSlim();
            using var retryGate = new ManualResetEventSlim();
            TaskCompletionSource<bool> largeRegistered = Signal();
            TaskCompletionSource<bool> smallRegistered = Signal();
            TaskCompletionSource<bool> largeRetried = Signal();
            int registrations = 0;
            limiter.BeforeCapacityWaitForTesting = () =>
            {
                switch (Interlocked.Increment(ref registrations))
                {
                    case 1:
                        largeRegistered.TrySetResult(true);
                        largeGate.Wait(timeout.Token);
                        break;
                    case 2:
                        smallRegistered.TrySetResult(true);
                        smallGate.Wait(timeout.Token);
                        break;
                    default:
                        largeRetried.TrySetResult(true);
                        retryGate.Wait(cancelLarge.Token);
                        break;
                }
            };
            long held = limiter.Reserve(8, CancellationToken.None);
            Task<ReservationResult> large = ReserveAsync(limiter, 8, cancelLarge.Token);
            Task<ReservationResult>? small = null;
            try
            {
                await largeRegistered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                small = ReserveAsync(limiter, 4, timeout.Token);
                await smallRegistered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                byte[] buffer = new byte[4];
                Assert.That(limiter.TryBind(held, buffer), Is.True);
                largeGate.Set();
                await largeRetried.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((2, 1)));
                smallGate.Set();
                ReservationResult acquired = await small.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(acquired.Error, Is.Null);
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((1, 0)));
                cancelLarge.Cancel();
                ReservationResult canceled = await large.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(canceled.Error, Is.InstanceOf<OperationCanceledException>());
                Assert.That(registrations, Is.EqualTo(3));
                limiter.Cancel(acquired.Id);
                limiter.CompleteReturn(limiter.BeginReturn(buffer));
                Assert.That(limiter.WaitStateForTesting, Is.EqualTo((0, 0)));
            }
            finally
            {
                limiter.Dispose();
                largeGate.Set();
                smallGate.Set();
                retryGate.Set();
                await large.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (small != null)
                {
                    await small.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
        }

        private static TaskCompletionSource<bool> Signal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static Task<ReservationResult> ReserveAsync(
            BufferManagerMemoryLimiter limiter, int length, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                try
                {
                    return new ReservationResult(limiter.Reserve(length, ct), null);
                }
                catch (Exception error)
                {
                    return new ReservationResult(0, error);
                }
            });
        }

        private readonly record struct ReservationResult(long Id, Exception? Error);
    }
}
