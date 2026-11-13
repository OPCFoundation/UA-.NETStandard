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
        public async Task ScopeDefersAtMostSixteenCompletionsAsync()
        {
            var operations = new ChannelAsyncOperation<int>[17];
            var waits = new ValueTask[operations.Length];
            for (int ii = 0; ii < operations.Length; ii++)
            {
                operations[ii] = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
                // each wait is awaited once, below
#pragma warning disable CA2012
                waits[ii] = operations[ii].WaitForCompletionAsync(CancellationToken.None);
#pragma warning restore CA2012
            }
            try
            {
                ResponseContinuationDeferral.Enter();
                try
                {
                    foreach (ChannelAsyncOperation<int> operation in operations)
                    {
                        operation.Complete(true, 1);
                    }
                }
                finally
                {
                    ResponseContinuationDeferral.Exit();
                }

                for (int ii = 0; ii < 16; ii++)
                {
                    Assert.That(waits[ii].IsCompleted, Is.False, $"wait {ii} is deferred");
                }
                Assert.That(waits[16].IsCompleted, Is.True, "the 17th completion is not deferred");

                ResponseContinuationDeferral.CompletePending(inline: true);
                foreach (ValueTask wait in waits)
                {
                    Assert.That(wait.IsCompleted, Is.True);
                    await wait.ConfigureAwait(false);
                }
            }
            finally
            {
                foreach (ChannelAsyncOperation<int> operation in operations)
                {
                    operation.Dispose();
                }
            }
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
