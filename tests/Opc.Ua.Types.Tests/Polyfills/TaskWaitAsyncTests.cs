// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Polyfills
{
    [TestFixture]
    public sealed class TaskWaitAsyncTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task SourceCancellationIsNotReportedAsTimeoutAsync(bool generic, bool alreadyCompleted)
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (alreadyCompleted)
            {
                Assert.That(source.TrySetCanceled(cancellation.Token), Is.True);
            }
            Task waiting = WaitAsync(source.Task, generic, s_timeout);
            if (!alreadyCompleted)
            {
                Assert.That(source.TrySetCanceled(cancellation.Token), Is.True);
            }

            await Assert.ThatAsync(() => waiting, Throws.InstanceOf<OperationCanceledException>()
                .With.Property(nameof(OperationCanceledException.CancellationToken)).EqualTo(cancellation.Token))
                .ConfigureAwait(false);
            Assert.That(waiting.IsCanceled, Is.True);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task WaitCancellationKeepsItsTokenAsync(bool generic, bool alreadyCancelled)
        {
            using var cancellation = new CancellationTokenSource();
            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (alreadyCancelled)
            {
                cancellation.Cancel();
            }
            Task waiting = WaitAsync(source.Task, generic, s_timeout, cancellation.Token);
            if (!alreadyCancelled)
            {
                cancellation.Cancel();
            }
            await Assert.ThatAsync(() => waiting, Throws.InstanceOf<OperationCanceledException>()
                .With.Property(nameof(OperationCanceledException.CancellationToken)).EqualTo(cancellation.Token))
                .ConfigureAwait(false);
            Assert.That(waiting.IsCanceled, Is.True);
            Assert.That(source.Task.IsCompleted, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExpiredWaitReportsTimeoutWithoutCancellingSourceAsync(bool generic)
        {
            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task waiting = WaitAsync(source.Task, generic, TimeSpan.Zero);
            await Assert.ThatAsync(() => waiting, Throws.TypeOf<TimeoutException>()).ConfigureAwait(false);
            Assert.That(waiting.IsFaulted, Is.True);
            Assert.That(source.Task.IsCompleted, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SourceFailureIsPreservedAsync(bool generic)
        {
            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task waiting = WaitAsync(source.Task, generic, s_timeout);
            source.SetException(new InvalidOperationException("Source failure"));
            await Assert.ThatAsync(() => waiting, Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo("Source failure")).ConfigureAwait(false);
            Assert.That(waiting.IsFaulted, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SuccessfulSourceCompletesTheWaitAsync(bool generic)
        {
            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task waiting = WaitAsync(source.Task, generic, s_timeout);
            source.SetResult(42);
            await waiting.ConfigureAwait(false);
            Assert.That(await source.Task.ConfigureAwait(false), Is.EqualTo(42));
            Assert.That(waiting.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        private static Task WaitAsync(
            Task<int> source, bool generic, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return generic
                ? source.WaitAsync(timeout, cancellationToken)
                : ((Task)source).WaitAsync(timeout, cancellationToken);
        }

        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);
    }
}
