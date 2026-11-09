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
using Opc.Ua.Extrusion.Server;

namespace Opc.Ua.Extrusion.Tests
{
    /// <summary>
    /// Proves that controller shutdown includes queued callers and their final cleanup.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class ExtrusionOperationLifetimeTests
    {
        /// <summary>
        /// Both terminal callers wait even when the only remaining call has never acquired its gate.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentDisposalWaitsForQueuedCleanupAsync(bool includeActive)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var lifetime = new ExtrusionOperationLifetime("test-operation-owner");
            using var gate = new SemaphoreSlim(1, 1);
            await gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            using ExtrusionOperationLifetime.Operation? active = includeActive
                ? lifetime.Begin(deadline.Token) : null;
            var queuedStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken queuedToken = default;
            int executions = 0;
            Task queued = QueueAsync();
            Task first = lifetime.DisposeAsync().AsTask();
            Task second = lifetime.DisposeAsync().AsTask();
            try
            {
                await queuedStopped.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(queuedToken.IsCancellationRequested, Is.True);
                    Assert.That(first.IsCompleted, Is.False, "The first disposer must include queued cleanup.");
                    Assert.That(second.IsCompleted, Is.False, "Concurrent disposal must await the same drain.");
                    Assert.That(queued.IsCompleted, Is.False);
                    if (active != null)
                    {
                        Assert.That(active.Token.IsCancellationRequested, Is.True);
                    }
                });
                ObjectDisposedException? failure = Assert.Throws<ObjectDisposedException>(() =>
                {
                    using ExtrusionOperationLifetime.Operation rejected = lifetime.Begin(CancellationToken.None);
                });
                Assert.That(failure!.ObjectName, Is.EqualTo("test-operation-owner"),
                    "Admission must reject the call, not fail by touching a disposed token source.");
            }
            finally
            {
                gate.Release();
                active?.Dispose();
                finishCleanup.TrySetResult(true);
                await Assert.ThatAsync(
                    async () => await queued.WaitAsync(deadline.Token).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                await Task.WhenAll(first, second).WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            Assert.That(executions, Is.Zero, "Cancellation must prevent the queued operation from running.");

            async Task QueueAsync()
            {
                using ExtrusionOperationLifetime.Operation pending = lifetime.Begin(deadline.Token);
                queuedToken = pending.Token;
                bool entered = false;
                try
                {
                    await gate.WaitAsync(pending.Token).ConfigureAwait(false);
                    entered = true;
                    Interlocked.Increment(ref executions);
                }
                finally
                {
                    if (entered)
                    {
                        gate.Release();
                    }
                    queuedStopped.TrySetResult(true);
                    await finishCleanup.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                }
            }
        }
    }
}
