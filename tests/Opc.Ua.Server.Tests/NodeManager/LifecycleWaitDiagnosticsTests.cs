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
