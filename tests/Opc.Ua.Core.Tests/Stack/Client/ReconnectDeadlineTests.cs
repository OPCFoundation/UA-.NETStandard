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
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    [TestFixture]
    [Parallelizable]
    public sealed class ReconnectDeadlineTests
    {
        [Test]
        public async Task DeadlineExpiresAtTheExactBoundAsync()
        {
            var time = new FakeTimeProvider();
            await using var deadline = new ReconnectDeadline(time, CancellationToken.None);
            deadline.Tighten(new RetryBudget(TimeSpan.FromSeconds(1), time));

            time.Advance(TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1));
            Assert.That(deadline.Expired, Is.False);
            Assert.That(deadline.Token.IsCancellationRequested, Is.False);
            time.Advance(TimeSpan.FromTicks(1));

            Assert.That(deadline.Expired, Is.True);
            Assert.That(deadline.TryComplete(), Is.False);
            await WaitForCancellationAsync(deadline.Token).ConfigureAwait(false);
        }

        [Test]
        public async Task SuccessfulCompletionRetiresTheDeadlineAsync()
        {
            var time = new FakeTimeProvider();
            await using var deadline = new ReconnectDeadline(time, CancellationToken.None);
            deadline.Tighten(new RetryBudget(TimeSpan.FromSeconds(1), time));
            time.Advance(TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1));

            Assert.That(deadline.TryComplete(), Is.True);
            deadline.Tighten(new RetryBudget(TimeSpan.Zero, time));
            time.Advance(TimeSpan.FromHours(1));

            Assert.That(deadline.Expired, Is.False);
            Assert.That(deadline.Token.IsCancellationRequested, Is.False);
        }

        [Test]
        public async Task LaterJoinersCanOnlyShortenTheExistingWindowAsync()
        {
            var time = new FakeTimeProvider();
            await using var deadline = new ReconnectDeadline(time, CancellationToken.None);
            deadline.Tighten(new RetryBudget(TimeSpan.FromSeconds(10), time));
            time.Advance(TimeSpan.FromSeconds(2));
            deadline.Tighten(new RetryBudget(TimeSpan.FromSeconds(3), time));
            deadline.Tighten(new RetryBudget(TimeSpan.FromMinutes(1), time));

            Assert.That(deadline.Duration, Is.EqualTo(TimeSpan.FromSeconds(5)));
            time.Advance(TimeSpan.FromSeconds(3));
            Assert.That(deadline.Expired, Is.True);
            Assert.That(deadline.Elapsed, Is.EqualTo(TimeSpan.FromSeconds(5)));
        }

        [Test]
        public async Task PreviouslyConsumedCallerBudgetKeepsItsOriginalDeadlineAsync()
        {
            var time = new FakeTimeProvider();
            var budget = new RetryBudget(TimeSpan.FromSeconds(5), time);
            Assert.That(budget.TryConsume(out _), Is.True);
            time.Advance(TimeSpan.FromSeconds(2));
            await using var deadline = new ReconnectDeadline(time, CancellationToken.None);

            deadline.Tighten(budget);
            Assert.That(deadline.Duration, Is.EqualTo(TimeSpan.FromSeconds(3)));
            time.Advance(TimeSpan.FromSeconds(3));
            Assert.That(deadline.Expired, Is.True);
        }

        [Test]
        public async Task ResettingACallerBudgetCannotExtendAnAttachedDeadlineAsync()
        {
            var time = new FakeTimeProvider();
            var budget = new RetryBudget(TimeSpan.FromSeconds(5), time);
            await using var deadline = new ReconnectDeadline(time, CancellationToken.None);
            deadline.Tighten(budget);
            time.Advance(TimeSpan.FromSeconds(2));
            budget.Reset();
            deadline.Tighten(budget);
            time.Advance(TimeSpan.FromSeconds(3));

            Assert.That(deadline.Duration, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(deadline.Expired, Is.True);
        }

        [Test]
        public async Task UnlimitedBudgetLeavesRecoveryUnboundedAsync()
        {
            var time = new FakeTimeProvider();
            await using var deadline = new ReconnectDeadline(time, CancellationToken.None);
            deadline.Tighten(null);
            deadline.Tighten(new RetryBudget(Timeout.InfiniteTimeSpan, time));
            time.Advance(TimeSpan.FromDays(365));

            Assert.That(deadline.Duration, Is.EqualTo(TimeSpan.MaxValue));
            Assert.That(deadline.Expired, Is.False);
            Assert.That(deadline.TryComplete(), Is.True);
        }

        [Test]
        public async Task LongExplicitBudgetRearmsWithoutExceedingTheTimerRangeAsync()
        {
            var time = new FakeTimeProvider();
            await using var deadline = new ReconnectDeadline(time, CancellationToken.None);
            var duration = TimeSpan.FromDays(60);
            deadline.Tighten(new RetryBudget(duration, time));
            var timerMaximum = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
            time.Advance(timerMaximum);

            Assert.That(deadline.Expired, Is.False);
            time.Advance(duration - timerMaximum);
            Assert.That(deadline.Expired, Is.True);
            Assert.That(deadline.Duration, Is.EqualTo(duration));
        }

        [Test]
        public async Task ClosingRecoveryCancelsWithoutReportingExpiryAsync()
        {
            var time = new FakeTimeProvider();
            await using var deadline = new ReconnectDeadline(time, CancellationToken.None);
            deadline.Tighten(new RetryBudget(TimeSpan.FromSeconds(1), time));
            deadline.Cancel();
            Assert.That(deadline.TryComplete(), Is.False);
            time.Advance(TimeSpan.FromHours(1));

            await WaitForCancellationAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(deadline.Expired, Is.False);
            Assert.That(deadline.TryComplete(), Is.False);
        }

        [Test]
        public async Task ShutdownIsNotReportedAsDeadlineExpiryAsync()
        {
            var time = new FakeTimeProvider();
            using var shutdown = new CancellationTokenSource();
            await using var deadline = new ReconnectDeadline(time, shutdown.Token);
            deadline.Tighten(new RetryBudget(TimeSpan.FromSeconds(1), time));
            await shutdown.CancelAsync().ConfigureAwait(false);

            Assert.That(deadline.Token.IsCancellationRequested, Is.True);
            Assert.That(deadline.Expired, Is.False);
            Assert.That(deadline.TryComplete(), Is.False);
        }

        [Test]
        [Repeat(20)]
        public async Task CompletionRacingWithExpiryHasOneVerdictAsync()
        {
            var time = new FakeTimeProvider();
            await using var deadline = new ReconnectDeadline(time, CancellationToken.None);
            deadline.Tighten(new RetryBudget(TimeSpan.FromSeconds(1), time));
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool> completion = Task.Run(async () =>
            {
                await start.Task.ConfigureAwait(false);
                return deadline.TryComplete();
            });
            Task expiration = Task.Run(async () =>
            {
                await start.Task.ConfigureAwait(false);
                time.Advance(TimeSpan.FromSeconds(1));
            });
            start.TrySetResult(true);
            await Task.WhenAll(completion, expiration).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(await completion.ConfigureAwait(false), Is.EqualTo(!deadline.Expired));
            if (deadline.Expired)
            {
                await WaitForCancellationAsync(deadline.Token).ConfigureAwait(false);
            }
            Assert.That(deadline.Token.IsCancellationRequested, Is.EqualTo(deadline.Expired));
        }

        private static async Task WaitForCancellationAsync(CancellationToken token)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = token.Register(() => cancelled.TrySetResult(true));
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
            Assert.That(token.IsCancellationRequested, Is.True);
        }
    }
}
