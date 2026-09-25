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

namespace Opc.Ua.Types.Tests.Utils
{
    /// <summary>
    /// Pins timeout and cancellation semantics of Task.WaitAsync across the
    /// BCL and older-framework polyfills. Completed tasks retain their results,
    /// source cancellation stays cancellation, and abandoning a wait does not
    /// cancel the underlying task.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class TaskWaitAsyncTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task CancellationOfTheSourceTaskRemainsCancellationAsync(bool generic, bool alreadyCanceled)
        {
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync().ConfigureAwait(false);
            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (alreadyCanceled)
            {
                Assert.That(source.TrySetCanceled(canceled.Token), Is.True);
            }
            Task wait = generic
                ? source.Task.WaitAsync(TimeSpan.FromSeconds(30))
                : ((Task)source.Task).WaitAsync(TimeSpan.FromSeconds(30));
            if (!alreadyCanceled)
            {
                Assert.That(source.TrySetCanceled(canceled.Token), Is.True);
            }
            OperationCanceledException exception = Assert.CatchAsync<OperationCanceledException>(async () =>
                await wait.ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(wait.IsCanceled, Is.True);
                Assert.That(exception.CancellationToken, Is.EqualTo(canceled.Token));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SourceFaultIsNotReplacedByATimeout(bool generic)
        {
            var original = new InvalidOperationException("source failure");
            Task<int> source = Task.FromException<int>(original);
            Task wait = generic
                ? source.WaitAsync(TimeSpan.FromSeconds(30))
                : ((Task)source).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.That(Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await wait.ConfigureAwait(false)), Is.SameAs(original));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActualDeadlineExpiryDoesNotCancelTheSource(bool generic)
        {
            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task wait = generic
                ? source.Task.WaitAsync(TimeSpan.Zero)
                : ((Task)source.Task).WaitAsync(TimeSpan.Zero);
            Assert.ThrowsAsync<TimeoutException>(async () => await wait.ConfigureAwait(false));
            Assert.That(source.Task.IsCompleted, Is.False);
            source.SetResult(42);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancelingTheWaitDoesNotCancelTheSourceAsync(bool generic)
        {
            using var canceled = new CancellationTokenSource();
            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task wait = generic
                ? source.Task.WaitAsync(TimeSpan.FromSeconds(30), canceled.Token)
                : ((Task)source.Task).WaitAsync(TimeSpan.FromSeconds(30), canceled.Token);
            await canceled.CancelAsync().ConfigureAwait(false);
            OperationCanceledException exception = Assert.CatchAsync<OperationCanceledException>(async () =>
                await wait.ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(source.Task.IsCompleted, Is.False);
                Assert.That(exception.CancellationToken, Is.EqualTo(canceled.Token));
            });
            source.SetResult(42);
        }

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
