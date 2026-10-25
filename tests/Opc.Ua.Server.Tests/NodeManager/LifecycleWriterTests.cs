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

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies writer ownership independently of request, binding and retirement implementations.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class LifecycleWriterTests
    {
        /// <summary>
        /// A canceled waiter cannot release another mutation or overtake its successor.
        /// </summary>
        [Test]
        public async Task MutationOwnershipOrdersWaitersAndCancellationCannotReleasePeerAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var writer = new LifecycleWriter();
            using LifecycleWriter.OperationLifetime operation = writer.EnterOperation();
            using LifecycleWriter.Mutation first = await writer.BeginMutationAsync(timeout.Token).ConfigureAwait(false);
            using var canceled = new CancellationTokenSource();
            Task<LifecycleWriter.Mutation> canceledWaiter = writer.BeginMutationAsync(canceled.Token).AsTask();
            Task<LifecycleWriter.Mutation> secondWaiter = writer.BeginMutationAsync(timeout.Token).AsTask();
            canceled.Cancel();
            try
            {
                await canceledWaiter.ConfigureAwait(false);
                Assert.Fail("Canceled mutation admission unexpectedly succeeded.");
            }
            catch (OperationCanceledException)
            {
                Assert.That(secondWaiter.IsCompleted, Is.False);
            }
            first.Dispose();
            using LifecycleWriter.Mutation second = await secondWaiter.WaitAsync(timeout.Token).ConfigureAwait(false);
            Task<LifecycleWriter.Mutation> thirdWaiter = writer.BeginMutationAsync(timeout.Token).AsTask();
            Assert.That(thirdWaiter.IsCompleted, Is.False, "The second owner must still exclude its successor.");
            second.Dispose();
            using LifecycleWriter.Mutation third = await thirdWaiter.WaitAsync(timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Decision rejection follows spawned execution contexts only for the lifetime of the decision.
        /// </summary>
        [Test]
        public async Task DecisionRejectsSpawnedReentrancyAndExpiresInCapturedContextAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var writer = new LifecycleWriter();
            using LifecycleWriter.OperationLifetime operation = writer.EnterOperation();
            using LifecycleWriter.Mutation mutation = await writer.BeginMutationAsync(timeout.Token)
                .ConfigureAwait(false);
            var rejected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var expired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? spawned = null;
            await mutation.DecideAsync(async token =>
            {
                Assert.Throws<InvalidOperationException>(() => writer.EnterOperation());
                spawned = Task.Run(async () =>
                {
                    Assert.Throws<InvalidOperationException>(() => writer.EnterOperation());
                    rejected.TrySetResult(true);
                    await expired.Task.WaitAsync(token).ConfigureAwait(false);
                    using LifecycleWriter.OperationLifetime allowed = writer.EnterOperation();
                }, token);
                await rejected.Task.WaitAsync(token).ConfigureAwait(false);
            }, timeout.Token).ConfigureAwait(false);
            expired.TrySetResult(true);
            Assert.That(spawned, Is.Not.Null);
            await spawned!.WaitAsync(timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Releasing mutation serialization does not release the operation's shutdown exclusion.
        /// </summary>
        [Test]
        public async Task ShutdownWaitsForOperationAfterMutationOwnershipEndsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var writer = new LifecycleWriter();
            using LifecycleWriter.OperationLifetime operation = writer.EnterOperation();
            using LifecycleWriter.Mutation mutation = await writer.BeginMutationAsync(timeout.Token)
                .ConfigureAwait(false);
            mutation.Dispose();
            Task drained = await writer.BeginShutdownAsync(timeout.Token).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(writer.IsShuttingDown, Is.True);
                Assert.That(drained.IsCompleted, Is.False);
                Assert.Throws<InvalidOperationException>(() => writer.EnterOperation());
            }
            operation.Dispose();
            await drained.WaitAsync(timeout.Token).ConfigureAwait(false);
            writer.EndShutdown(prepared: false, completing: true);
        }
    }
}
