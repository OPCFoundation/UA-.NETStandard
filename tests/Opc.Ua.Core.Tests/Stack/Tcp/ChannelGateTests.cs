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
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Tcp
{
    /// <summary>
    /// Covers the gate that replaces the channel's monitor.
    /// </summary>
    /// <remarks>
    /// Two properties matter and are proved here rather than argued: it excludes
    /// across both entry modes, and work started while it is held is excluded
    /// too. The second used to be the hazard — the gate tracked ownership in an
    /// <c>AsyncLocal</c>, which is inherited, so anything started from inside the
    /// guarded region believed it was already the holder unless it opted out by
    /// hand. That opt-out is gone along with the re-entrancy, and these tests pin
    /// the behaviour that replaced it.
    /// </remarks>
    [TestFixture]
    [Category("ChannelGate")]
    [Parallelizable(ParallelScope.All)]
    [SetCulture("en-us")]
    public class ChannelGateTests
    {
        [Test]
        [CancelAfter(30000)]
        public async Task CancellingAContendedEntryLeavesTheGateUsableAsync()
        {
            var gate = new ChannelGate();
            var blocking = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Task holder = Task.Run(async () =>
            {
                using (gate.Enter())
                {
                    await blocking.Task.ConfigureAwait(false);
                }
            });

            while (!gate.IsHeldBySomeContextForTest)
            {
                await Task.Delay(5).ConfigureAwait(false);
            }

            using var cts = new CancellationTokenSource();
            ValueTask<ChannelGate.Releaser> pending = gate.EnterAsync(cts.Token);
            cts.Cancel();

            Assert.That(
                async () => await pending.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            blocking.TrySetResult(true);
            await holder.ConfigureAwait(false);

            // A cancelled wait must not have consumed the permit.
            using (gate.Enter())
            {
                Assert.That(gate.IsHeldBySomeContextForTest, Is.True);
            }

            Assert.That(gate.IsHeldBySomeContextForTest, Is.False);
        }

        [Test]
        [CancelAfter(30000)]
        public async Task AnUncontendedAsynchronousEntryCompletesSynchronouslyAsync()
        {
            var gate = new ChannelGate();

            ValueTask<ChannelGate.Releaser> entry = gate.EnterAsync();

            Assert.That(
                entry.IsCompleted,
                Is.True,
                "an uncontended entry must not suspend or allocate a task");

            using (await entry.ConfigureAwait(false))
            {
                Assert.That(gate.IsHeldBySomeContextForTest, Is.True);
            }
        }

        [Test]
        [CancelAfter(30000)]
        public async Task TheGateExcludesADifferentContextAsync()
        {
            var gate = new ChannelGate();
            var entered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            ChannelGate.Releaser held = await gate.EnterAsync().ConfigureAwait(false);

            Task contender = Task.Run(async () =>
            {
                using (await gate.EnterAsync().ConfigureAwait(false))
                {
                    entered.TrySetResult(true);
                }
            });

            // Released in a finally so that a failing assertion reports the
            // failure rather than stranding the gate and hanging the contender
            // until the timeout.
            try
            {
                Task first = await Task.WhenAny(contender, Task.Delay(250)).ConfigureAwait(false);

                Assert.That(
                    first,
                    Is.Not.SameAs(contender),
                    "another context must not enter while the gate is held");
            }
            finally
            {
                held.Dispose();
            }

            await contender.ConfigureAwait(false);
            Assert.That(await entered.Task.ConfigureAwait(false), Is.True);
        }

        /// <summary>
        /// The defect this design removed. Work started while the gate is held
        /// inherits the logical execution context, and the gate used to record
        /// ownership there — so such work believed it was already the holder and
        /// ran inside the guarded region alongside its parent unless it opted out
        /// by hand. Ownership is no longer contextual, so it is simply excluded.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task WorkStartedWhileHeldIsExcludedWithoutOptingOutAsync()
        {
            var gate = new ChannelGate();
            var started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            bool ranInsideTheRegion = false;

            ChannelGate.Releaser held = gate.Enter();

            // Started from inside the guarded region and deliberately not
            // disclaiming anything, which is what every fire-and-forget path
            // in the channel used to have to do.
            Task detached = Task.Run(async () =>
            {
                started.TrySetResult(true);

                using (await gate.EnterAsync().ConfigureAwait(false))
                {
                    ranInsideTheRegion = true;
                }
            });

            // The gate is released in a finally rather than by a using, because
            // it has to be released before the assertions below are awaited to
            // completion but exactly once. A failing assertion would otherwise
            // strand it and leave the detached task blocked until the timeout,
            // reporting a hang instead of the failure.
            try
            {
                Assert.That(await started.Task.ConfigureAwait(false), Is.True);

                Task first = await Task.WhenAny(detached, Task.Delay(250)).ConfigureAwait(false);

                Assert.That(
                    first,
                    Is.Not.SameAs(detached),
                    "work started while the gate was held entered the guarded region");
                Assert.That(ranInsideTheRegion, Is.False);
            }
            finally
            {
                held.Dispose();
            }

            await detached.ConfigureAwait(false);

            Assert.That(ranInsideTheRegion, Is.True);
        }

        /// <summary>
        /// Contending contexts must see a consistent view, which is the whole
        /// point of replacing the monitor rather than removing it.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task ConcurrentContextsNeverOverlapAsync()
        {
            var gate = new ChannelGate();
            int inside = 0;
            int maximumObserved = 0;

            async Task WorkerAsync()
            {
                for (int ii = 0; ii < 50; ii++)
                {
                    using (await gate.EnterAsync().ConfigureAwait(false))
                    {
                        int now = Interlocked.Increment(ref inside);
                        InterlockedMax(ref maximumObserved, now);

                        await Task.Yield();

                        Interlocked.Decrement(ref inside);
                    }
                }
            }

            var workers = new Task[8];
            for (int ii = 0; ii < workers.Length; ii++)
            {
                workers[ii] = Task.Run(WorkerAsync);
            }

            await Task.WhenAll(workers).ConfigureAwait(false);

            Assert.That(
                maximumObserved,
                Is.EqualTo(1),
                "two contexts were inside the guarded region at once");
        }

        [Test]
        public void SynchronousAndAsynchronousEntrantsExcludeEachOther()
        {
            var gate = new ChannelGate();
            var releaseHolder = new ManualResetEventSlim(false);
            var holderEntered = new ManualResetEventSlim(false);
            bool contenderEntered = false;

            var holder = new Thread(() =>
            {
                using (gate.Enter())
                {
                    holderEntered.Set();
                    releaseHolder.Wait(TimeSpan.FromSeconds(5));
                }
            });

            holder.Start();
            Assert.That(holderEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            Task contender = Task.Run(async () =>
            {
                using (await gate.EnterAsync().ConfigureAwait(false))
                {
                    contenderEntered = true;
                }
            });

            Assert.That(
                contender.Wait(TimeSpan.FromMilliseconds(250)),
                Is.False,
                "an asynchronous entrant must wait for a synchronous holder");

            releaseHolder.Set();
            holder.Join(TimeSpan.FromSeconds(5));

            Assert.That(contender.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(contenderEntered, Is.True);
        }

        /// <summary>
        /// A thread that already holds the gate is not recognised, so re-entering
        /// blocks. The channel code therefore calls a lock-free core method on
        /// every path that used to re-enter.
        /// </summary>
        [Test]
        public void ReenteringFromTheHoldingThreadBlocks()
        {
            var gate = new ChannelGate();

            using (gate.Enter())
            {
                using var reentry = new CancellationTokenSource();
                ValueTask<ChannelGate.Releaser> nested = gate.EnterAsync(reentry.Token);

                Assert.That(
                    nested.IsCompleted,
                    Is.False,
                    "the gate must not be re-entrant");

                reentry.Cancel();

                Assert.That(
                    async () => await nested.ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>());
            }
        }

        [Test]
        public void ReleaserEqualityComparesTheGate()
        {
            var gate = new ChannelGate();
            var other = new ChannelGate();

            ChannelGate.Releaser held = gate.Enter();
            ChannelGate.Releaser copy = held;
            ChannelGate.Releaser onOther = other.Enter();

            try
            {
                bool copyEqualsHeld = copy == held;
                bool otherDiffersFromHeld = onOther != held;

                Assert.Multiple(() =>
                {
                    Assert.That(copy, Is.EqualTo(held));
                    Assert.That(copyEqualsHeld, Is.True);
                    Assert.That(onOther, Is.Not.EqualTo(held));
                    Assert.That(otherDiffersFromHeld, Is.True);
                    Assert.That(onOther, Is.Not.EqualTo((object)held));
                    Assert.That(copy.GetHashCode(), Is.EqualTo(held.GetHashCode()));
                    Assert.That(default(ChannelGate.Releaser), Is.Not.EqualTo(held));
                    Assert.That(default(ChannelGate.Releaser).GetHashCode(), Is.Zero);
                });
            }
            finally
            {
                onOther.Dispose();
                held.Dispose();
            }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int seen = Volatile.Read(ref target);

            while (value > seen)
            {
                int previous = Interlocked.CompareExchange(ref target, value, seen);

                if (previous == seen)
                {
                    return;
                }

                seen = previous;
            }
        }
    }
}
