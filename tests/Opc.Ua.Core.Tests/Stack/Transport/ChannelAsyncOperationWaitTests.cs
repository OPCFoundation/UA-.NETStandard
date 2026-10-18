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
    /// The task-free wait the client channel uses for a request's response.
    /// </summary>
    [TestFixture]
    [Category("TcpTransport")]
    [Parallelizable]
    public sealed class ChannelAsyncOperationWaitTests
    {
        private readonly ILogger m_logger = NUnitTelemetryContext.Create().CreateLogger<ChannelAsyncOperationWaitTests>();

        [Test]
        [CancelAfter(10000)]
        public async Task WaitCompletesWhenTheOperationCompletesLaterAsync()
        {
            using var operation = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);

            ValueTask wait = operation.WaitForCompletionAsync(CancellationToken.None);
            Assert.That(wait.IsCompleted, Is.False);

            Assert.That(operation.Complete(true, 7), Is.True);
            await wait.ConfigureAwait(false);

            Assert.That(operation.End(0), Is.EqualTo(7));
        }

        [Test]
        public void WaitOnACompletedOperationCompletesSynchronously()
        {
            using var operation = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            operation.Complete(true, 0);

            ValueTask wait = operation.WaitForCompletionAsync(CancellationToken.None);

            Assert.That(wait.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void WaitOnAFaultedOperationThrowsTheError()
        {
            using var operation = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            operation.Fault(new ServiceResult(StatusCodes.BadSecureChannelClosed));

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await operation.WaitForCompletionAsync(CancellationToken.None).ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadSecureChannelClosed));
        }

        [Test]
        [CancelAfter(10000)]
        public void WaitThrowsTheErrorOfAFaultWhileWaiting()
        {
            using var operation = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            ValueTask wait = operation.WaitForCompletionAsync(CancellationToken.None);

            operation.Fault(true, new ServiceResult(StatusCodes.BadUnknownResponse));

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await wait.ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadUnknownResponse));
        }

        [Test]
        [CancelAfter(10000)]
        public void CancellingTheTokenInterruptsTheWait()
        {
            using var operation = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            using var cts = new CancellationTokenSource();
            ValueTask wait = operation.WaitForCompletionAsync(cts.Token);

            cts.Cancel();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await wait.ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadRequestInterrupted));

            // a response that arrives afterwards completes the operation quietly.
            Assert.That(operation.Complete(true, 0), Is.True);
        }

        [Test]
        public void AnAlreadyCancelledTokenInterruptsTheWait()
        {
            using var operation = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await operation.WaitForCompletionAsync(cts.Token).ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadRequestInterrupted));
        }

        [Test]
        [CancelAfter(10000)]
        public void DisposingTheOperationInterruptsTheWait()
        {
            var operation = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            ValueTask wait = operation.WaitForCompletionAsync(CancellationToken.None);

            operation.Dispose();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await wait.ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadRequestInterrupted));
        }

        /// <summary>
        /// The continuation must not run on the thread that completes the
        /// operation: that is the channel's receive loop.
        /// </summary>
        [Test]
        [CancelAfter(10000)]
        public async Task TheContinuationDoesNotRunOnTheCompletingThreadAsync()
        {
            using var operation = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            int completingThread = -1;
            int continuationThread = -1;
            using var completed = new ManualResetEventSlim();

            async Task WaitAndRecordContinuationAsync()
            {
                await operation.WaitForCompletionAsync(CancellationToken.None).ConfigureAwait(false);
                continuationThread = Environment.CurrentManagedThreadId;
            }

            // Returning from the direct call guarantees that the pending continuation is registered.
            Task waiter = WaitAndRecordContinuationAsync();
            Assert.That(waiter.IsCompleted, Is.False);
            var thread = new Thread(() =>
            {
                completingThread = Environment.CurrentManagedThreadId;
                operation.Complete(true, 0);
                // keep the thread busy so an inline continuation would be visible.
                completed.Wait(TimeSpan.FromSeconds(5));
            });
            thread.Start();

            try
            {
                await waiter.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            finally
            {
                completed.Set();
                Assert.That(thread.Join(TimeSpan.FromSeconds(5)), Is.True);
            }

            Assert.That(continuationThread, Is.Not.EqualTo(completingThread));
        }

        /// <summary>
        /// Completion, cancellation and disposal racing each other always end the
        /// wait exactly once, with a success or BadRequestInterrupted.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task CompletionRacingCancellationEndsTheWaitOnceAsync()
        {
            for (int ii = 0; ii < 2000; ii++)
            {
                using var operation = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
                using var cts = new CancellationTokenSource();
                ValueTask wait = operation.WaitForCompletionAsync(cts.Token);

                using var start = new Barrier(2);
                Task complete = Task.Run(() =>
                {
                    start.SignalAndWait();
                    operation.Complete(true, 0);
                });
                Task cancel = Task.Run(() =>
                {
                    start.SignalAndWait();
                    cts.Cancel();
                });

                try
                {
                    await wait.ConfigureAwait(false);
                }
                catch (ServiceResultException ex)
                {
                    Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadRequestInterrupted));
                }
                await Task.WhenAll(complete, cancel).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The timeout timer uses a shared callback; it still faults the operation.
        /// </summary>
        [Test]
        [CancelAfter(10000)]
        public void TheTimeoutStillFaultsTheOperation()
        {
            using var operation = new ChannelAsyncOperation<int>(50, null, null, m_logger);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await operation.WaitForCompletionAsync(CancellationToken.None).ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadRequestTimeout));
        }

        /// <summary>
        /// Completing with the default value shares a box; the value still reads
        /// back, for value and reference types.
        /// </summary>
        [Test]
        public void CompletingWithTheDefaultValueReturnsIt()
        {
            using var ints = new ChannelAsyncOperation<int>(int.MaxValue, null, null, m_logger);
            ints.Complete(true, 0);
            Assert.That(ints.End(0), Is.Zero);

            using var strings = new ChannelAsyncOperation<string?>(int.MaxValue, null, null, m_logger);
            strings.Complete(true, null);
            Assert.That(strings.End(0), Is.Null);
            Assert.That(strings.Error, Is.EqualTo(ServiceResult.Good));
        }
    }
}
