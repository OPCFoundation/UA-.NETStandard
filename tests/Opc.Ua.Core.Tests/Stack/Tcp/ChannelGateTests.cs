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
    /// The three properties that matter are proved here rather than argued:
    /// it excludes, it tolerates the re-entrancy the channel code relies on, and
    /// work started while it is held does not inherit the right to re-enter.
    /// The last one is the failure a naive implementation would ship with, and it
    /// would be silent.
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

            // A cancelled wait must not have taken the gate, and must not leave
            // the cancelled context believing it holds it.
            Assert.That(gate.IsHeldByCurrentContext, Is.False);

            blocking.TrySetResult(true);
            await holder.ConfigureAwait(false);

            using (gate.Enter())
            {
                Assert.That(gate.IsHeldByCurrentContext, Is.True);
            }
        }

        [Test]
        public void ReleasersCompareByGateAndHolder()
        {
            var gate = new ChannelGate();

            using ChannelGate.Releaser outer = gate.Enter();
            using ChannelGate.Releaser nested = gate.Enter();

            ChannelGate.Releaser copy = outer;

            Assert.Multiple(() =>
            {
                Assert.That(copy, Is.EqualTo(outer));
                Assert.That(nested, Is.Not.EqualTo(outer));
                Assert.That(copy.GetHashCode(), Is.EqualTo(outer.GetHashCode()));
            });
        }

        [Test]
        [CancelAfter(30000)]
        public async Task InheritedReentryDoesNotStrandTheGateAsync()
        {
            var gate = new ChannelGate();
            var forkEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var ownerLeaving = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Task fork;

            using (gate.Enter())
            {
                // Work started from inside the guarded region without disclaiming
                // inherits the holder. A timer callback whose timer was created
                // here behaves exactly like this.
                fork = Task.Run(async () =>
                {
                    using (gate.Enter())
                    {
                        forkEntered.TrySetResult(true);
                        await ownerLeaving.Task.ConfigureAwait(false);
                    }
                });

                await forkEntered.Task.ConfigureAwait(false);
            }

            // The owner has left, so the gate must be free even though the forked
            // context is still inside its own nested entry.
            ownerLeaving.TrySetResult(true);

            using (ChannelGate.Releaser entered = await gate
                .EnterAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token)
                .ConfigureAwait(false))
            {
                Assert.That(gate.IsHeldByCurrentContext, Is.True);
            }

            await fork.ConfigureAwait(false);
        }

        [Test]
        public void EnterIsReentrantOnTheSameContext()
        {            var gate = new ChannelGate();

            using (gate.Enter())
            {
                Assert.That(gate.IsHeldByCurrentContext, Is.True);

                // The channel does exactly this: HandleIncomingMessage holds the
                // gate and calls ForceChannelFault, which takes it again.
                using (gate.Enter())
                {
                    Assert.That(gate.IsHeldByCurrentContext, Is.True);
                }

                Assert.That(
                    gate.IsHeldByCurrentContext,
                    Is.True,
                    "leaving a nested entry must not release the gate");
            }

            Assert.That(gate.IsHeldByCurrentContext, Is.False);
        }

        [Test]
        public async Task EnterAsyncIsReentrantOnTheSameContextAsync()
        {
            var gate = new ChannelGate();

            using (await gate.EnterAsync())
            {
                using (await gate.EnterAsync())
                {
                    Assert.That(gate.IsHeldByCurrentContext, Is.True);
                }

                Assert.That(gate.IsHeldByCurrentContext, Is.True);
            }

            Assert.That(gate.IsHeldByCurrentContext, Is.False);
        }

        /// <summary>
        /// Re-entrancy must survive a suspension point, or the open path could
        /// not await at all.
        /// </summary>
        [Test]
        public async Task ReentrancySurvivesAnAwaitAsync()
        {
            var gate = new ChannelGate();

            using (await gate.EnterAsync())
            {
                await Task.Yield();

                Assert.That(gate.IsHeldByCurrentContext, Is.True);

                using (gate.Enter())
                {
                    Assert.That(gate.IsHeldByCurrentContext, Is.True);
                }
            }

            Assert.That(gate.IsHeldByCurrentContext, Is.False);
        }

        [Test]
        public async Task TheGateExcludesADifferentContextAsync()
        {
            var gate = new ChannelGate();
            var entered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            ChannelGate.Releaser held = await gate.EnterAsync();

            Task contender = Task.Run(async () =>
            {
                // A genuinely different context. Started from the holder here only
                // because a test has to, so it disclaims the inheritance the way
                // every detached path in the channel does.
                gate.LeaveInheritedContext();

                using (await gate.EnterAsync())
                {
                    entered.TrySetResult(true);
                }
            });

            Task first = await Task.WhenAny(contender, Task.Delay(250));

            Assert.That(
                first,
                Is.Not.SameAs(contender),
                "another context must not enter while the gate is held");

            held.Dispose();

            await contender.ConfigureAwait(false);
            Assert.That(await entered.Task.ConfigureAwait(false), Is.True);
        }

        /// <summary>
        /// This is the hazard the design has to answer. Work started while the
        /// gate is held inherits the logical context, so without an explicit
        /// disclaimer it would believe it already holds the gate and would run
        /// inside the guarded region alongside its parent.
        /// </summary>
        [Test]
        public async Task WorkStartedWhileHeldMustDisclaimTheInheritedContextAsync()
        {
            var gate = new ChannelGate();
            bool inheritedWithoutDisclaimer;
            bool inheritedAfterDisclaimer;

            using (await gate.EnterAsync())
            {
                inheritedWithoutDisclaimer = await Task.Run(() => gate.IsHeldByCurrentContext)
                    .ConfigureAwait(false);

                inheritedAfterDisclaimer = await Task.Run(() =>
                {
                    gate.LeaveInheritedContext();
                    return gate.IsHeldByCurrentContext;
                }).ConfigureAwait(false);

                Assert.That(
                    gate.IsHeldByCurrentContext,
                    Is.True,
                    "a branch disclaiming its inheritance must not release the holder's gate");
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    inheritedWithoutDisclaimer,
                    Is.True,
                    "the inheritance is real, which is why every detached path must disclaim it");
                Assert.That(
                    inheritedAfterDisclaimer,
                    Is.False,
                    "disclaiming must drop the inherited entitlement");
            });
        }

        /// <summary>
        /// After disclaiming, detached work must actually be excluded, not merely
        /// report that it does not hold the gate.
        /// </summary>
        [Test]
        public async Task DisclaimedWorkIsExcludedUntilTheHolderLeavesAsync()
        {
            var gate = new ChannelGate();
            var reachedGuardedRegion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task detached;

            using (await gate.EnterAsync())
            {
                detached = Task.Run(async () =>
                {
                    gate.LeaveInheritedContext();

                    using (await gate.EnterAsync())
                    {
                        reachedGuardedRegion.TrySetResult(true);
                    }
                });

                Task first = await Task.WhenAny(detached, Task.Delay(250));

                Assert.That(
                    first,
                    Is.Not.SameAs(detached),
                    "detached work must wait for the holder");
            }

            await detached.ConfigureAwait(false);
            Assert.That(await reachedGuardedRegion.Task.ConfigureAwait(false), Is.True);
        }

        /// <summary>
        /// Contending contexts must see a consistent view, which is the whole
        /// point of replacing the monitor rather than removing it.
        /// </summary>
        [Test]
        public async Task ConcurrentContextsNeverOverlapAsync()
        {
            var gate = new ChannelGate();
            int inside = 0;
            int maximumObserved = 0;

            async Task WorkerAsync()
            {
                gate.LeaveInheritedContext();

                for (int ii = 0; ii < 50; ii++)
                {
                    using (await gate.EnterAsync())
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
                gate.LeaveInheritedContext();

                using (await gate.EnterAsync())
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

        [Test]
        public void ReleaserEqualityDistinguishesInstances()
        {
            var gate = new ChannelGate();

            ChannelGate.Releaser outer = gate.Enter();
            ChannelGate.Releaser nested = gate.Enter();
            ChannelGate.Releaser sameAsOuter = outer;

            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(outer, Is.Not.EqualTo(nested));
                    Assert.That(sameAsOuter, Is.EqualTo(outer));
                    Assert.That(nested, Is.Not.EqualTo((object)outer));
                    Assert.That(outer.GetHashCode(), Is.Not.EqualTo(nested.GetHashCode()));
                });
            }
            finally
            {
                nested.Dispose();
                outer.Dispose();
            }
        }

        /// <summary>
        /// A monitor is re-entrant per thread. A completion callback invoked
        /// inline runs on the thread that already holds the gate but under the
        /// context captured when the operation was started, so context alone
        /// would not recognise it and the thread would deadlock against itself.
        /// </summary>
        /// <remarks>
        /// This is not hypothetical: <c>ChannelAsyncOperation</c> invokes its
        /// callback inline, and the whole channel test suite crashes without it.
        /// </remarks>
        [Test]
        public void EnterIsReentrantOnTheSameThreadUnderADifferentContext()
        {
            var gate = new ChannelGate();
            bool reentered = false;

            using (gate.Enter())
            {
                // Runs on this thread but under the captured context, which is the
                // one that existed before the gate was entered. This is what an
                // inline completion callback does.
                ExecutionContext captured = CaptureContextWithoutTheGate(gate);

                ExecutionContext.Run(
                    captured,
                    _ =>
                    {
                        Assert.That(
                            gate.IsHeldByCurrentContext,
                            Is.False,
                            "the captured context predates the entry");

                        using (gate.Enter())
                        {
                            reentered = true;
                        }
                    },
                    null);
            }

            Assert.That(reentered, Is.True, "the same thread must be able to re-enter");
        }

        private static ExecutionContext CaptureContextWithoutTheGate(ChannelGate gate)
        {
            ExecutionContext captured = null!;

            var thread = new Thread(() =>
            {
                gate.LeaveInheritedContext();
                captured = ExecutionContext.Capture();
            });

            thread.Start();
            thread.Join(TimeSpan.FromSeconds(5));

            return captured!;
        }

        /// <summary>
        /// A synchronous holder that suspends releases its thread, and the pool is
        /// free to run something else on it. That work must not be mistaken for
        /// the holder.
        /// </summary>
        /// <remarks>
        /// This is the defect that hung the Server integration suite: a
        /// <see cref="ChannelGate.Enter"/> handle was held across an
        /// <see langword="await"/> in the token renewal path, so an unrelated
        /// continuation reusing the thread re-entered the guarded region and
        /// decremented the holder's depth, releasing the gate while the holder was
        /// still inside.
        /// </remarks>
        [Test]
        public async Task WorkReusingASuspendedHoldersThreadDoesNotInheritTheGateAsync()
        {
            var gate = new ChannelGate();
            int managedThreadId = Environment.CurrentManagedThreadId;

            using (await gate.EnterAsync().ConfigureAwait(false))
            {
                // Simulates a pool thread being reused while the holder is
                // suspended: same thread identity, unrelated logical context.
                bool observed = await Task.Factory.StartNew(
                    () =>
                    {
                        gate.LeaveInheritedContext();
                        return gate.IsHeldByCurrentContext;
                    },
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    TaskScheduler.Default).ConfigureAwait(false);

                Assert.That(
                    observed,
                    Is.False,
                    "an asynchronous holder must not grant entry by thread identity");
            }

            Assert.Multiple(() =>
            {
                Assert.That(gate.IsHeldByCurrentContext, Is.False);
                Assert.That(managedThreadId, Is.GreaterThan(0));
            });
        }

        /// <summary>
        /// Work started while the gate is held must not run its prologue on the
        /// holder's stack, disclaim the holder's entitlement, and then block on the
        /// gate the holder still owns.
        /// </summary>
        /// <remarks>
        /// This is the deadlock that stopped the secure channel handshake: the
        /// channel started its write inline, so <see cref="ChannelGate.LeaveInheritedContext"/>
        /// ran on the caller's stack and stripped the caller's own right to
        /// re-enter; when the send completed synchronously the completion then
        /// blocked on a gate that very thread was holding. The channel now queues
        /// the write, and this test states the rule the fix relies on.
        /// </remarks>
        [Test]
        public async Task DisclaimingOnTheHoldersOwnStackWouldSelfDeadlockAsync()
        {
            var gate = new ChannelGate();

            using (await gate.EnterAsync().ConfigureAwait(false))
            {
                // Running the disclaimer inline is what the channel used to do.
                gate.LeaveInheritedContext();

                Assert.That(
                    gate.IsHeldByCurrentContext,
                    Is.False,
                    "disclaiming inline strips the holder's own entitlement, which is " +
                    "why detached work must be queued rather than started inline");
            }

            // Once the holder has left, the gate is free again, which shows the
            // depth accounting was not corrupted by the disclaimer.
            using (await gate.EnterAsync().ConfigureAwait(false))
            {
                Assert.That(gate.IsHeldByCurrentContext, Is.True);
            }
        }

        /// <summary>
        /// A contended acquisition must leave the caller holding the gate, not
        /// merely owning the semaphore.
        /// </summary>
        /// <remarks>
        /// The holder is published by writing an <see cref="AsyncLocal{T}"/>. On
        /// the contended path that write happens inside the awaiting state
        /// machine, and an async method's context is not restored into its
        /// caller — so the caller would come back holding the semaphore but with
        /// no record of it, and the next re-entrant acquisition would block on a
        /// gate its own context already owns.
        /// <para>
        /// Every re-entrant path in the channel reaches this:
        /// <c>HandleIncomingMessageAsync</c> enters with <c>EnterAsync</c> and
        /// calls <c>ForceChannelFault</c>, which enters with <c>Enter</c>.
        /// </para>
        /// </remarks>
        [Test]
        public async Task ContendedEntryStillRecordsTheHolderAsync()
        {
            var gate = new ChannelGate();
            var blocking = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var aboutToEnter = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // Hold the gate from an unrelated context so the entry below must wait.
            Task holder = Task.Run(async () =>
            {
                gate.LeaveInheritedContext();

                using (await gate.EnterAsync().ConfigureAwait(false))
                {
                    await blocking.Task.ConfigureAwait(false);
                }
            });

            while (!gate.IsHeldBySomeContextForTest)
            {
                await Task.Delay(5).ConfigureAwait(false);
            }

            Task<bool> contended = Task.Run(async () =>
            {
                gate.LeaveInheritedContext();

                aboutToEnter.TrySetResult(true);

                using (await gate.EnterAsync().ConfigureAwait(false))
                {
                    if (!gate.IsHeldByCurrentContext)
                    {
                        return false;
                    }

                    // Re-entering must not block. This is what ForceChannelFault
                    // does from inside HandleIncomingMessageAsync.
                    using (gate.Enter())
                    {
                        return true;
                    }
                }
            });

            // Let the contender actually block on the semaphore before the holder
            // lets go, so the contended path is the one under test.
            await aboutToEnter.Task.ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);

            blocking.TrySetResult(true);
            await holder.ConfigureAwait(false);

            Task first = await Task.WhenAny(contended, Task.Delay(5000)).ConfigureAwait(false);

            Assert.That(
                first,
                Is.SameAs(contended),
                "a contended acquisition left the caller unable to re-enter");
            Assert.That(
                await contended.ConfigureAwait(false),
                Is.True,
                "a contended acquisition did not record the holder");
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current = Volatile.Read(ref target);

            while (value > current)
            {
                int previous = Interlocked.CompareExchange(ref target, value, current);

                if (previous == current)
                {
                    return;
                }

                current = previous;
            }
        }
    }
}
