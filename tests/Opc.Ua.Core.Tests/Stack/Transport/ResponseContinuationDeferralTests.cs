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
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// The receive loop runs the continuations of the requests it completed once it
    /// waits for the next chunk (see <see cref="ResponseContinuationDeferral"/>).
    /// </summary>
    [TestFixture]
    [Category("TcpTransport")]
    [Parallelizable]
    public sealed class ResponseContinuationDeferralTests
    {
        private readonly ILogger m_logger =
            NUnitTelemetryContext.Create().CreateLogger<ResponseContinuationDeferralTests>();

        [Test]
        [CancelAfter(10000)]
        public async Task CompletionOutsideAScopeIsNotDeferredAsync()
        {
            using var operation = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            ValueTask wait = operation.WaitForCompletionAsync(CancellationToken.None);

            Assert.That(operation.Complete(true, 7), Is.True);

            Assert.That(wait.IsCompleted, Is.True);
            await wait.ConfigureAwait(false);
            Assert.That(operation.End(0), Is.EqualTo(7));
        }

        [Test]
        [CancelAfter(10000)]
        public async Task CompletionInAScopeRunsItsContinuationWhenPendingCompletesInlineAsync()
        {
            using var operation = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            Task<int> waiter = ContinuationThreadAsync(operation.WaitForCompletionAsync(CancellationToken.None));

            ResponseContinuationDeferral.Enter();
            try
            {
                Assert.That(operation.Complete(true, 7), Is.True);
            }
            finally
            {
                ResponseContinuationDeferral.Exit();
            }
            Assert.That(waiter.IsCompleted, Is.False, "the completion is deferred");

            ResponseContinuationDeferral.CompletePending(inline: true);

            Assert.That(waiter.IsCompleted, Is.True, "the continuation ran inline");
            Assert.That(await waiter.ConfigureAwait(false), Is.EqualTo(Environment.CurrentManagedThreadId));
            Assert.That(operation.End(0), Is.EqualTo(7));
        }

        [Test]
        [CancelAfter(10000)]
        public async Task CompletionsDeferredOnLoopExitRunOnThePoolAsync()
        {
            using var operation = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            Task<int> waiter = ContinuationThreadAsync(operation.WaitForCompletionAsync(CancellationToken.None));

            ResponseContinuationDeferral.Enter();
            try
            {
                operation.Complete(true, 7);
            }
            finally
            {
                ResponseContinuationDeferral.Exit();
            }
            ResponseContinuationDeferral.CompletePending(inline: false);

            await waiter.ConfigureAwait(false);
            Assert.That(operation.End(0), Is.EqualTo(7));
        }

        [Test]
        [CancelAfter(10000)]
        public async Task LaterCompletionCompletesTheHeldOneOnThePoolAsync()
        {
            using var first = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            using var second = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            Task<int> firstWaiter = ContinuationThreadAsync(first.WaitForCompletionAsync(CancellationToken.None));
            Task<int> secondWaiter = ContinuationThreadAsync(second.WaitForCompletionAsync(CancellationToken.None));

            ResponseContinuationDeferral.Enter();
            try
            {
                first.Complete(true, 1);
                second.Complete(true, 2);
            }
            finally
            {
                ResponseContinuationDeferral.Exit();
            }

            // the first completes on the pool without the loop suspending; waited for on
            // this thread, which holds the second
            Assert.That(firstWaiter.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(secondWaiter.IsCompleted, Is.False, "the latest completion is held");

            ResponseContinuationDeferral.CompletePending(inline: true);
            Assert.That(secondWaiter.IsCompleted, Is.True);
            Assert.That(await secondWaiter.ConfigureAwait(false), Is.EqualTo(Environment.CurrentManagedThreadId));
        }

        [Test]
        [CancelAfter(10000)]
        public async Task HeldCompletionGoesToThePoolAfterFourMoreChunksAsync()
        {
            using var operation = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            Task<int> waiter = ContinuationThreadAsync(operation.WaitForCompletionAsync(CancellationToken.None));

            ResponseContinuationDeferral.Enter();
            try
            {
                operation.Complete(true, 7);
            }
            finally
            {
                ResponseContinuationDeferral.Exit();
            }

            // chunks without responses, received without the loop suspending
            for (int ii = 0; ii < 4; ii++)
            {
                ResponseContinuationDeferral.Enter();
                ResponseContinuationDeferral.Exit();
            }
            Assert.That(waiter.IsCompleted, Is.False, "held while the loop dispatches four more chunks");

            ResponseContinuationDeferral.Enter();
            ResponseContinuationDeferral.Exit();
            await waiter.ConfigureAwait(false);
            Assert.That(operation.End(0), Is.EqualTo(7));
            ResponseContinuationDeferral.CompletePending(inline: true);
        }

        /// <summary>
        /// Two responses in a row: the continuation of the first blocks until the second
        /// request completes, which must not wait for the blocked thread.
        /// </summary>
        [Test]
        [CancelAfter(20000)]
        public async Task ContinuationBlockingOnTheNextResponseDoesNotDeadlockAsync()
        {
            using var first = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            using var second = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            var receive = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<int> secondWaiter = ContinuationThreadAsync(second.WaitForCompletionAsync(CancellationToken.None));
            Task<bool> firstCaller = BlockOnAsync(first.WaitForCompletionAsync(CancellationToken.None), secondWaiter);

            Task<int> loop = Task.Run(async () =>
            {
                ResponseContinuationDeferral.Enter();
                try
                {
                    first.Complete(true, 1);
                    second.Complete(true, 2);
                }
                finally
                {
                    ResponseContinuationDeferral.Exit();
                }
                return await ResponseContinuationDeferral.AwaitAndComplete(new ValueTask<int>(receive.Task));
            });

            Assert.That(await firstCaller.ConfigureAwait(false), Is.True, "the second response completed");
            receive.SetResult(1);
            Assert.That(await loop.ConfigureAwait(false), Is.EqualTo(1));
        }

        private static async Task<bool> BlockOnAsync(ValueTask wait, Task other)
        {
            await wait.ConfigureAwait(false);
            return other.Wait(TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// A continuation that blocks until the receive loop resumes does not stall the
        /// loop: the loop registers its continuation on the pending receive before it
        /// runs the deferred continuations on its thread.
        /// </summary>
        [Test]
        [CancelAfter(20000)]
        public async Task BlockingContinuationDoesNotStallTheLoopAsync()
        {
            using var operation = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            var receive = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var loopResumed = new ManualResetEventSlim();

            // the caller of the request: completes the next receive, then blocks until
            // the loop has processed it, as a synchronous call on the channel would
            Task<bool> caller = CallerAsync(
                operation.WaitForCompletionAsync(CancellationToken.None),
                receive,
                loopResumed);

            Task<int> loop = Task.Run(() => LoopAsync(operation, receive, loopResumed));

            Assert.That(await loop.ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await caller.ConfigureAwait(false), Is.True, "the loop resumed while the caller blocked");
        }

        private static async Task<int> LoopAsync(
            ChannelAsyncOperation<int> operation,
            TaskCompletionSource<int> receive,
            ManualResetEventSlim loopResumed)
        {
            ResponseContinuationDeferral.Enter();
            try
            {
                operation.Complete(true, 7);
            }
            finally
            {
                ResponseContinuationDeferral.Exit();
            }
            int received = await ResponseContinuationDeferral.AwaitAndComplete(new ValueTask<int>(receive.Task));
            loopResumed.Set();
            return received;
        }

        private static async Task<bool> CallerAsync(
            ValueTask wait,
            TaskCompletionSource<int> receive,
            ManualResetEventSlim loopResumed)
        {
            await wait.ConfigureAwait(false);
            receive.SetResult(1);
            return loopResumed.Wait(TimeSpan.FromSeconds(5));
        }

        private static async Task<int> ContinuationThreadAsync(ValueTask wait)
        {
            await wait.ConfigureAwait(false);
            return Environment.CurrentManagedThreadId;
        }
    }
}
