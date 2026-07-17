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
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

using ManagedSessionClass = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Unit tests for the static
    /// <c>ManagedSession.RunRevalidationLoopAsync</c> loop body that
    /// drives debounced server-certificate revalidation for trust-list
    /// and CRL changes (issue #3160 follow-up).
    /// </summary>
    /// <remarks>
    /// These tests use the real <see cref="TimeProvider.System"/> with
    /// a short debounce window so each test runs in well under a
    /// second. A <c>FakeTimeProvider</c> seam was considered but
    /// rejected because the loop's
    /// <c>ReadAsync → Delay → drain</c> sequence is racy to drive with
    /// a fake clock — the fake timer is not created until after the
    /// first signal has been consumed, so an early <c>Advance</c> is
    /// a no-op.
    /// </remarks>
    [TestFixture]
    [Category("ManagedSession")]
    [Category("CertificateChanges")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ManagedSessionRevalidationLoopTests
    {
        private static readonly TimeSpan s_shortDebounce = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan s_settleWindow = TimeSpan.FromMilliseconds(250);

        [Test]
        public async Task BurstOfSignalsCollapsesToSingleValidationAsync()
        {
            Channel<int> channel = CreateChannel();
            int invocations = 0;

            using var cts = new CancellationTokenSource();
            Task loop = ManagedSessionClass.RunRevalidationLoopAsync(
                channel.Reader,
                TimeProvider.System,
                s_shortDebounce,
                _ =>
                {
                    Interlocked.Increment(ref invocations);
                    return Task.CompletedTask;
                },
                NullLogger.Instance,
                cts.Token);

            // 50 events in a tight burst — the bounded-1 channel plus
            // DropWrite means only one signal is observable to the
            // reader.
            for (int i = 0; i < 50; i++)
            {
                channel.Writer.TryWrite(0);
            }

            // Wait through the debounce + a safety window.
            await Task.Delay(s_shortDebounce + s_settleWindow).ConfigureAwait(false);

            await CancelAndAwaitAsync(cts, loop).ConfigureAwait(false);

            Assert.That(Volatile.Read(ref invocations), Is.EqualTo(1),
                "A burst of 50 signals must collapse to exactly one validation.");
        }

        [Test]
        public async Task SignalDuringInflightValidationReArmsExactlyOnceAsync()
        {
            Channel<int> channel = CreateChannel();
            int invocations = 0;
            var firstStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var firstCanComplete = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            using var cts = new CancellationTokenSource();
            Task loop = ManagedSessionClass.RunRevalidationLoopAsync(
                channel.Reader,
                TimeProvider.System,
                s_shortDebounce,
                async _ =>
                {
                    int n = Interlocked.Increment(ref invocations);
                    if (n == 1)
                    {
                        firstStarted.TrySetResult(true);
                        await firstCanComplete.Task.ConfigureAwait(false);
                    }
                },
                NullLogger.Instance,
                cts.Token);

            channel.Writer.TryWrite(0);
            await firstStarted.Task.ConfigureAwait(false);

            // While the first validation is in-flight, more signals
            // arrive. The bounded channel collapses them to one
            // observable signal.
            for (int i = 0; i < 10; i++)
            {
                channel.Writer.TryWrite(0);
            }

            // Release the first validation; the loop must observe the
            // pending signal and run validation exactly once more.
            firstCanComplete.TrySetResult(true);
            await Task.Delay(s_shortDebounce + s_settleWindow).ConfigureAwait(false);

            await CancelAndAwaitAsync(cts, loop).ConfigureAwait(false);

            Assert.That(Volatile.Read(ref invocations), Is.EqualTo(2),
                "Re-arm must fire exactly once for events received during the in-flight validation.");
        }

        [Test]
        public async Task NoSignalMeansNoValidationAsync()
        {
            Channel<int> channel = CreateChannel();
            int invocations = 0;

            using var cts = new CancellationTokenSource();
            Task loop = ManagedSessionClass.RunRevalidationLoopAsync(
                channel.Reader,
                TimeProvider.System,
                s_shortDebounce,
                _ =>
                {
                    Interlocked.Increment(ref invocations);
                    return Task.CompletedTask;
                },
                NullLogger.Instance,
                cts.Token);

            // Idle: no signals at all, wait a few debounce windows.
            await Task.Delay(TimeSpan.FromTicks(s_shortDebounce.Ticks * 4)).ConfigureAwait(false);

            await CancelAndAwaitAsync(cts, loop).ConfigureAwait(false);

            Assert.That(Volatile.Read(ref invocations), Is.Zero,
                "Loop must not invoke validation when no signals arrive.");
        }

        [Test]
        public async Task CancellationDuringDebounceAbortsPendingValidationAsync()
        {
            Channel<int> channel = CreateChannel();
            int invocations = 0;

            // Use a longer debounce so we can reliably cancel while
            // the loop is still inside Delay.
            var debounce = TimeSpan.FromSeconds(2);

            var cts = new CancellationTokenSource();
            Task loop = ManagedSessionClass.RunRevalidationLoopAsync(
                channel.Reader,
                TimeProvider.System,
                debounce,
                _ =>
                {
                    Interlocked.Increment(ref invocations);
                    return Task.CompletedTask;
                },
                NullLogger.Instance,
                cts.Token);

            channel.Writer.TryWrite(0);

            // Give the loop a moment to read the signal and start the
            // debounce Delay.
            await Task.Delay(50).ConfigureAwait(false);

            await CancelAndAwaitAsync(cts, loop).ConfigureAwait(false);

            Assert.That(Volatile.Read(ref invocations), Is.Zero,
                "Cancellation during the debounce window must abort the pending validation.");
            cts.Dispose();
        }

        [Test]
        public async Task ValidationExceptionsAreSwallowedAndLoopContinuesAsync()
        {
            Channel<int> channel = CreateChannel();
            int invocations = 0;

            using var cts = new CancellationTokenSource();
            Task loop = ManagedSessionClass.RunRevalidationLoopAsync(
                channel.Reader,
                TimeProvider.System,
                s_shortDebounce,
                _ =>
                {
                    int n = Interlocked.Increment(ref invocations);
                    if (n == 1)
                    {
                        throw new InvalidOperationException("simulated transient failure");
                    }
                    return Task.CompletedTask;
                },
                NullLogger.Instance,
                cts.Token);

            channel.Writer.TryWrite(0);
            await Task.Delay(s_shortDebounce + s_settleWindow).ConfigureAwait(false);

            // The first validation threw; the next signal must still
            // drive a fresh run.
            channel.Writer.TryWrite(0);
            await Task.Delay(s_shortDebounce + s_settleWindow).ConfigureAwait(false);

            await CancelAndAwaitAsync(cts, loop).ConfigureAwait(false);

            Assert.That(Volatile.Read(ref invocations), Is.EqualTo(2),
                "Loop must survive a transient validation exception and process subsequent signals.");
        }

        private static Channel<int> CreateChannel()
        {
            return Channel.CreateBounded<int>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        }

        private static async Task CancelAndAwaitAsync(
            CancellationTokenSource cts,
            Task loop)
        {
            cts.Cancel();
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
