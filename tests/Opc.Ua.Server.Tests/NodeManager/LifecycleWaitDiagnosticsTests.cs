// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies diagnostic observation does not alter lifecycle wait behavior.
    /// </summary>
    public sealed class LifecycleWaitDiagnosticsTests
    {
        /// <summary>
        /// A completed wait does not emit a delayed warning.
        /// </summary>
        [Test]
        public async Task CompletedWaitDoesNotWarnAsync()
        {
            var clock = new FakeTimeProvider();
            int warnings = 0;

            await LifecycleWaitDiagnostics.WaitAsync(
                Task.CompletedTask,
                clock,
                TimeSpan.FromSeconds(30),
                _ => warnings++).ConfigureAwait(false);

            Assert.That(warnings, Is.Zero);
        }

        /// <summary>
        /// A long wait warns once and continues awaiting the original operation.
        /// </summary>
        [Test]
        public async Task PendingWaitWarnsAndContinuesAwaitingAsync()
        {
            var clock = new FakeTimeProvider();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var warning = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            int warnings = 0;

            Task wait = LifecycleWaitDiagnostics.WaitAsync(
                completion.Task,
                clock,
                TimeSpan.FromSeconds(30),
                elapsed =>
                {
                    warnings++;
                    warning.TrySetResult(elapsed);
                });

            clock.Advance(TimeSpan.FromSeconds(30));
            TimeSpan elapsed = await warning.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromMinutes(1));

            Assert.That(wait.IsCompleted, Is.False);
            Assert.That(elapsed, Is.EqualTo(TimeSpan.FromSeconds(30)));
            completion.TrySetResult(true);
            await wait.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(wait.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(warnings, Is.EqualTo(1));
        }

        /// <summary>
        /// An operation failure after a diagnostic is still propagated to the caller.
        /// </summary>
        [Test]
        public async Task FailureAfterWarningIsPropagatedAsync()
        {
            var clock = new FakeTimeProvider();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var warning = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new InvalidOperationException("Lifecycle operation failed.");
            int warnings = 0;

            Task wait = LifecycleWaitDiagnostics.WaitAsync(
                completion.Task,
                clock,
                TimeSpan.FromSeconds(30),
                _ =>
                {
                    warnings++;
                    warning.TrySetResult(true);
                });

            clock.Advance(TimeSpan.FromSeconds(30));
            await warning.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            completion.TrySetException(failure);

            InvalidOperationException? caught = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await wait.ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(caught, Is.SameAs(failure));
            Assert.That(warnings, Is.EqualTo(1));
        }

        /// <summary>
        /// An operation failure before the threshold propagates without warning.
        /// </summary>
        [Test]
        public async Task FailureBeforeThresholdDoesNotWarnAsync()
        {
            var clock = new FakeTimeProvider();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new InvalidOperationException("Lifecycle operation failed.");
            int warnings = 0;

            Task wait = LifecycleWaitDiagnostics.WaitAsync(
                completion.Task,
                clock,
                TimeSpan.FromSeconds(30),
                _ => warnings++);
            completion.TrySetException(failure);

            InvalidOperationException? caught = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await wait.ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(caught, Is.SameAs(failure));
            Assert.That(warnings, Is.Zero);
        }

        /// <summary>
        /// Cancellation ends the caller's wait without warning or cancelling the operation.
        /// </summary>
        [Test]
        public async Task CancellationBeforeThresholdDoesNotWarnOrCancelOperationAsync()
        {
            var clock = new FakeTimeProvider();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource();
            int warnings = 0;

            Task wait = LifecycleWaitDiagnostics.WaitAsync(
                completion.Task,
                clock,
                TimeSpan.FromSeconds(30),
                _ => warnings++,
                cancellation.Token);

            cancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () => await wait.ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(warnings, Is.Zero);
            Assert.That(completion.Task.IsCanceled, Is.False);
        }
    }
}
