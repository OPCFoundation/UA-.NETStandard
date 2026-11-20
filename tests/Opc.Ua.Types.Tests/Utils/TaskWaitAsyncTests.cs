// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils
{
    /// <summary>
    /// Pins the semantics of <c>Task.WaitAsync(CancellationToken)</c> that
    /// callers rely on. On net8.0+ this is the BCL method; on the older
    /// target frameworks it is the polyfill in Opc.Ua.Types, and the two
    /// have to agree - a caller that awaits an already completed task must
    /// get its result on every target, not a result on one and a
    /// cancellation on another.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TaskWaitAsyncTests
    {
        [Test]
        public async Task CompletedTaskWinsOverAnAlreadyCancelledTokenAsync()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Task<int> completed = Task.FromResult(42);

            Assert.That(
                await completed.WaitAsync(cts.Token).ConfigureAwait(false),
                Is.EqualTo(42),
                "a completed task must be handed back rather than reported as cancelled");
        }

        [Test]
        public void CompletedNonGenericTaskWinsOverAnAlreadyCancelledToken()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await Task.CompletedTask.WaitAsync(cts.Token).ConfigureAwait(false),
                Throws.Nothing);
        }

        [Test]
        public void PendingTaskObservesTheCancellation()
        {
            using var cts = new CancellationTokenSource();
            var pending = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Task<int> wait = pending.Task.WaitAsync(cts.Token);
            cts.Cancel();

            Assert.That(
                () => wait,
                Throws.InstanceOf<OperationCanceledException>());

            // The abandoned wait must not fault the underlying task.
            pending.TrySetResult(1);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task TimedWaitPreservesUnderlyingCancellationAsync(bool generic, bool alreadyCompleted)
        {
            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (alreadyCompleted)
            {
                source.SetCanceled();
            }

            Task wait = generic
                ? source.Task.WaitAsync(TimeSpan.FromSeconds(5))
                : ((Task)source.Task).WaitAsync(TimeSpan.FromSeconds(5));
            if (!alreadyCompleted)
            {
                source.SetCanceled();
            }

            await Assert.ThatAsync(
                () => wait,
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(wait.IsCanceled, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TimedWaitReportsTimeoutWithoutCompletingUnderlyingTaskAsync(bool generic)
        {
            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task wait = generic
                ? source.Task.WaitAsync(TimeSpan.Zero)
                : ((Task)source.Task).WaitAsync(TimeSpan.Zero);

            await Assert.ThatAsync(
                () => wait,
                Throws.TypeOf<TimeoutException>()).ConfigureAwait(false);
            Assert.That(source.Task.IsCompleted, Is.False);
            source.SetResult(42);
            Assert.That(await source.Task.ConfigureAwait(false), Is.EqualTo(42));
        }
    }
}
