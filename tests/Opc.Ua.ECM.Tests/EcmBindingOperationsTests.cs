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
using Opc.Ua.ECM.Server;

namespace Opc.Ua.ECM.Tests
{
    /// <summary>
    /// Exercises the shared callback lifetime independently of either generated power graph.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class EcmBindingOperationsTests
    {
        /// <summary>
        /// A provider cancellation callback cannot skip draining admitted work or removing graph callbacks.
        /// </summary>
        [Test]
        public async Task ShutdownCancellationFailureStillDrainsAndRemovesCallbacks()
        {
            var operations = new EcmBindingOperations();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int removals = 0;
            Task<int> pending = operations.ExecuteAsync<int>(CallbackAsync, CancellationToken.None).AsTask();
            Task? stopping = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                stopping = operations.StopAsync(() => removals++).AsTask();
                Assert.That(stopping.IsCompleted, Is.False);
                Assert.That(removals, Is.Zero);
                await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await operations.ExecuteAsync<int>(static _ => new ValueTask<int>(99), CancellationToken.None));
            }
            finally
            {
                release.TrySetResult(true);
                Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(10)), Is.EqualTo(42));
                if (stopping != null)
                {
                    AggregateException failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                        await stopping.WaitAsync(TimeSpan.FromSeconds(10)));
                    AggregateException flattened = failure.Flatten();
                    Assert.That(flattened.InnerExceptions, Has.Count.EqualTo(1));
                    Assert.That(flattened.InnerExceptions[0].Message, Is.EqualTo("Cancellation observer failed."));
                }
            }
            Assert.That(removals, Is.EqualTo(1));
            Assert.That(operations.StopAsync(() => removals++).AsTask(), Is.SameAs(stopping));
            Assert.That(removals, Is.EqualTo(1));
            await Assert.ThrowsAsync<AggregateException>(async () =>
                await operations.DisposeAsync());

            async ValueTask<int> CallbackAsync(CancellationToken token)
            {
                using CancellationTokenRegistration registration = token.Register(static () =>
                    throw new InvalidOperationException("Cancellation observer failed."));
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                return 42;
            }
        }

        /// <summary>
        /// Reentrant shutdown from provider cancellation observes the same task and removes callbacks only once.
        /// </summary>
        [Test]
        public async Task ReentrantCancellationObservesOneSharedShutdown()
        {
            await using var operations = new EcmBindingOperations();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int removals = 0;
            Task? nested = null;
            Task<int> pending = operations.ExecuteAsync<int>(CallbackAsync, CancellationToken.None).AsTask();
            Task? stopping = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                stopping = operations.StopAsync(() => removals++).AsTask();
                Assert.That(nested, Is.SameAs(stopping));
                Assert.That(stopping.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                await pending.WaitAsync(TimeSpan.FromSeconds(10));
                await operations.StopAsync(() => removals++).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            }
            Assert.That(removals, Is.EqualTo(1));
            Assert.That(nested, Is.SameAs(stopping));

            async ValueTask<int> CallbackAsync(CancellationToken token)
            {
                using CancellationTokenRegistration registration = token.Register(() =>
                    nested = operations.StopAsync(() => removals++).AsTask());
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                return 42;
            }
        }

        /// <summary>
        /// Cancelling a queued operation neither executes it nor releases another callback's serialization.
        /// </summary>
        [Test]
        public async Task CancelledQueuedOperationCannotOverlapTheActiveCallback()
        {
            await using var operations = new EcmBindingOperations();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int queuedCalls = 0;
            Task<int> first = operations.ExecuteAsync<int>(FirstAsync, CancellationToken.None).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                using var cancellation = new CancellationTokenSource();
                Task<int> cancelled = operations.ExecuteAsync<int>(_ =>
                {
                    queuedCalls++;
                    return new ValueTask<int>(99);
                }, cancellation.Token).AsTask();
                cancellation.Cancel();
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await cancelled.WaitAsync(TimeSpan.FromSeconds(10)));
                Task<int> next = operations.ExecuteAsync<int>(_ =>
                {
                    queuedCalls++;
                    return new ValueTask<int>(17);
                }, CancellationToken.None).AsTask();
                Assert.That(next.IsCompleted, Is.False);
                Assert.That(queuedCalls, Is.Zero);
                release.TrySetResult(true);
                Assert.That(await first.WaitAsync(TimeSpan.FromSeconds(10)), Is.EqualTo(5));
                Assert.That(await next.WaitAsync(TimeSpan.FromSeconds(10)), Is.EqualTo(17));
                Assert.That(queuedCalls, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                await first.WaitAsync(TimeSpan.FromSeconds(10));
                await operations.StopAsync(static () => { });
            }

            async ValueTask<int> FirstAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                return 5;
            }
        }

        /// <summary>
        /// Work still queued when admission closes is canceled even when the active callback releases the slot
        /// before shutdown requests cancellation, while the admitted active callback completes normally.
        /// </summary>
        [Test]
        public async Task QueuedOperationReleasedAfterAdmissionClosesBeforeCancellationNeverRuns()
        {
            var operations = new EcmBindingOperations();
            var activeEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int queuedInvocations = 0;
            bool stopTokenSetAtRelease = true;
            bool stopTokenSetAfterQueued = true;
            bool queuedCanceledBeforeCancellation = false;
            int activeResult = 0;
            Task<int> active = operations.ExecuteAsync<int>(ActiveAsync, CancellationToken.None).AsTask();
            await activeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task<int> queued = operations.ExecuteAsync<int>(QueuedAsync, CancellationToken.None).AsTask();
            Assert.That(queued.IsCompleted, Is.False);
            operations.BeforeStopCancellation = async () =>
            {
                stopTokenSetAtRelease = operations.StoppingToken.IsCancellationRequested;
                releaseActive.TrySetResult(true);
                activeResult = await active.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                try
                {
                    await queued.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    queuedCanceledBeforeCancellation = true;
                }
                stopTokenSetAfterQueued = operations.StoppingToken.IsCancellationRequested;
            };

            await operations.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(stopTokenSetAtRelease, Is.False);
            Assert.That(stopTokenSetAfterQueued, Is.False);
            Assert.That(activeResult, Is.EqualTo(7));
            Assert.That(queuedCanceledBeforeCancellation, Is.True);
            await Assert.CatchAsync<OperationCanceledException>(async () => await queued.ConfigureAwait(false));
            Assert.That(Volatile.Read(ref queuedInvocations), Is.Zero);

            async ValueTask<int> ActiveAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                activeEntered.TrySetResult(true);
                await releaseActive.Task.ConfigureAwait(false);
                return 7;
            }

            ValueTask<int> QueuedAsync(CancellationToken token)
            {
                Interlocked.Increment(ref queuedInvocations);
                token.ThrowIfCancellationRequested();
                return new ValueTask<int>(8);
            }
        }

        /// <summary>
        /// Work still queued at shutdown is canceled when the active callback releases the slot after the stop
        /// token is set but before shutdown signals the queued waiter's linked token.
        /// </summary>
        [Test]
        public async Task QueuedOperationReleasedWhileShutdownSignalsTokensNeverRuns()
        {
            var operations = new EcmBindingOperations();
            var activeEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int queuedInvocations = 0;
            bool stopTokenSetAtRelease = false;
            bool queuedFinishedInWindow = false;
            Task<int> active = operations.ExecuteAsync<int>(ActiveAsync, CancellationToken.None).AsTask();
            await activeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var queuedCompleted = new ManualResetEventSlim(false);

            // The queued operation and this registration are created on the same thread without an intervening
            // await, so cancellation runs this later registration before it signals the queued linked token.
            Task<int> queued = operations.ExecuteAsync<int>(QueuedAsync, CancellationToken.None).AsTask();
            Assert.That(queued.IsCompleted, Is.False);
            _ = queued.ContinueWith(
                _ => queuedCompleted.Set(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            using (operations.StoppingToken.Register(() =>
            {
                stopTokenSetAtRelease = operations.StoppingToken.IsCancellationRequested;
                releaseActive.TrySetResult(true);

                // Cancellation callbacks are synchronous, so this barrier holds shutdown inside Cancel until the
                // queued waiter has acquired the released slot and finished.
                queuedFinishedInWindow = queuedCompleted.Wait(TimeSpan.FromSeconds(30));
            }))
            {
                await operations.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(40));
            }

            Assert.That(stopTokenSetAtRelease, Is.True);
            Assert.That(queuedFinishedInWindow, Is.True);
            Assert.That(await active.WaitAsync(TimeSpan.FromSeconds(10)), Is.EqualTo(7));
            await Assert.CatchAsync<OperationCanceledException>(async () => await queued.ConfigureAwait(false));
            Assert.That(Volatile.Read(ref queuedInvocations), Is.Zero);

            async ValueTask<int> ActiveAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                activeEntered.TrySetResult(true);
                await releaseActive.Task.ConfigureAwait(false);
                return 7;
            }

            ValueTask<int> QueuedAsync(CancellationToken token)
            {
                Interlocked.Increment(ref queuedInvocations);
                token.ThrowIfCancellationRequested();
                return new ValueTask<int>(8);
            }
        }
    }
}
