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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils
{
    /// <summary>
    /// Tests for <see cref="BackgroundTaskScope"/>.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BackgroundTaskScopeTests
    {
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

        [Test]
        public async Task RunExecutesTheScheduledWorkAsync()
        {
            await using var sut = new BackgroundTaskScope("test");
            var ran = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Assert.That(sut.Run("op", _ =>
            {
                ran.TrySetResult(true);
                return default;
            }), Is.True);

            Assert.That(await ran.Task.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
        }

        [Test]
        public async Task DisposeAsyncWaitsForWorkStillInFlightAsync()
        {
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = false;

            var sut = new BackgroundTaskScope("test");
            Assert.That(sut.Run("op", async _ =>
            {
                started.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                finished = true;
            }), Is.True);

            await started.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            ValueTask dispose = sut.DisposeAsync();
            Assert.That(dispose.IsCompleted, Is.False, "Disposal must wait for work in flight.");

            release.TrySetResult(true);
            await dispose.ConfigureAwait(false);

            Assert.That(finished, Is.True);
            Assert.That(sut.PendingCount, Is.Zero);
        }

        [Test]
        public async Task DisposeAsyncCancelsTheShutdownTokenAsync()
        {
            var sut = new BackgroundTaskScope("test");
            CancellationToken token = sut.ShutdownToken;
            Assert.That(token.IsCancellationRequested, Is.False);

            await sut.DisposeAsync().ConfigureAwait(false);

            Assert.That(token.IsCancellationRequested, Is.True);
        }

        [Test]
        public async Task RunAfterShutdownIsRejectedAndDoesNotExecuteAsync()
        {
            var sut = new BackgroundTaskScope("test");
            await sut.DisposeAsync().ConfigureAwait(false);

            var ran = false;
            Assert.That(sut.Run("op", _ =>
            {
                ran = true;
                return default;
            }), Is.False);

            Assert.That(ran, Is.False);
            Assert.That(sut.PendingCount, Is.Zero);
        }

        [Test]
        public async Task WorkThatThrowsIsObservedAndDoesNotBreakTheScopeAsync()
        {
            await using var sut = new BackgroundTaskScope("test");
            var second = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Assert.That(sut.Run("boom", _ => throw new InvalidOperationException("boom")), Is.True);
            Assert.That(sut.Run("op", _ =>
            {
                second.TrySetResult(true);
                return default;
            }), Is.True);

            // The scope keeps working, and the faulted operation is not rethrown
            // anywhere the caller could see it.
            Assert.That(await second.Task.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
        }

        [Test]
        public async Task DisposeAsyncIsIdempotentAsync()
        {
            var sut = new BackgroundTaskScope("test");

            await sut.DisposeAsync().ConfigureAwait(false);
            await sut.DisposeAsync().ConfigureAwait(false);

            Assert.That(sut.PendingCount, Is.Zero);
        }

        [Test]
        public async Task MaxConcurrencyBoundsTheOperationsRunningAtOnceAsync()
        {
            const int maxConcurrency = 2;
            const int scheduled = 8;

            await using var sut = new BackgroundTaskScope("test", maxConcurrency: maxConcurrency);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var allStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int running = 0;
            int peak = 0;
            int completed = 0;

            for (int i = 0; i < scheduled; i++)
            {
                Assert.That(sut.Run("op", async _ =>
                {
                    int current = Interlocked.Increment(ref running);
                    int observed = Volatile.Read(ref peak);
                    while (current > observed &&
                        Interlocked.CompareExchange(ref peak, current, observed) != observed)
                    {
                        observed = Volatile.Read(ref peak);
                    }

                    await release.Task.ConfigureAwait(false);
                    Interlocked.Decrement(ref running);
                    if (Interlocked.Increment(ref completed) == scheduled)
                    {
                        allStarted.TrySetResult(true);
                    }
                }), Is.True);
            }

            // Nothing can finish until released, so whatever is running now is
            // everything the limit allows to run concurrently.
            release.TrySetResult(true);
            await allStarted.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            Assert.That(Volatile.Read(ref peak), Is.LessThanOrEqualTo(maxConcurrency));
            Assert.That(Volatile.Read(ref completed), Is.EqualTo(scheduled));
        }

        [Test]
        public async Task RunWithNullWorkThrowsArgumentNullExceptionAsync()
        {
            await using var sut = new BackgroundTaskScope("test");
            Assert.That(() => sut.Run("op", null!), Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorRejectsNegativeConcurrency()
        {
            Assert.That(
                () => new BackgroundTaskScope("test", maxConcurrency: -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task SynchronousDisposeStopsAcceptingWorkWithoutWaitingAsync()
        {
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var sut = new BackgroundTaskScope("test");
            Assert.That(sut.Run("op", async _ =>
            {
                started.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            }), Is.True);

            await started.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            // Returns even though the operation above is still running.
            sut.Dispose();

            Assert.That(sut.ShutdownToken.IsCancellationRequested, Is.True);
            Assert.That(sut.Run("late", _ => default), Is.False);

            release.TrySetResult(true);
            await sut.DisposeAsync().ConfigureAwait(false);
            Assert.That(sut.PendingCount, Is.Zero);
        }

        [Test]
        public void ConstructorRejectsNullOwner()
        {
            Assert.That(() => new BackgroundTaskScope(null!), Throws.ArgumentNullException);
        }
    }
}
