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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// A throwing cancellation callback cannot return shutdown before the real worker's retained drain.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task WorkerCancellationFailureStillJoinsRetainedDrainAsync(bool subscriptionWorker)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IServerInternal server = m_server.CurrentInstance;
            object manager = subscriptionWorker ? server.SubscriptionManager : server.SessionManager;
            Type managerType = subscriptionWorker ? typeof(SubscriptionManager) : typeof(SessionManager);
            FieldInfo cancellationField = GetShutdownTestField(managerType, "m_workerCts");
            FieldInfo taskField = GetShutdownTestField(
                managerType, subscriptionWorker ? "m_publishWorkerTask" : "m_monitorWorkerTask");
            var cancellation = (CancellationTokenSource?)cancellationField.GetValue(manager)
                ?? throw new AssertionException("The live worker cancellation source was not found.");
            var realWorker = (Task?)taskField.GetValue(manager)
                ?? throw new AssertionException("The live worker task was not found.");
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var expected = new InvalidOperationException("The worker cancellation callback failed.");
            Task retainedDrain = RetainWorkerDrainAsync();
            taskField.SetValue(manager, retainedDrain);
            using CancellationTokenRegistration callback = cancellation.Token.Register(() =>
            {
                cancelEntered.TrySetResult(true);
                throw expected;
            });
            Task? stopping = null;
            try
            {
                stopping = subscriptionWorker
                    ? server.SubscriptionManager.ShutdownAsync(timeout.Token).AsTask()
                    : server.SessionManager.ShutdownAsync(timeout.Token).AsTask();
                await cancelEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(stopping.IsCompleted, Is.False,
                    "Shutdown must join its retained worker even when signaling cancellation throws.");
                release.TrySetResult(true);
                AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(
                    () => stopping.WaitAsync(timeout.Token)).ConfigureAwait(false);
                Assert.That(failure!.Flatten().InnerExceptions, Has.Exactly(1).SameAs(expected));
                Assert.That(retainedDrain.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            }
            finally
            {
                release.TrySetResult(true);
                callback.Dispose();
                try
                {
                    if (stopping is not null)
                    {
                        try
                        {
                            await stopping.WaitAsync(timeout.Token).ConfigureAwait(false);
                        }
                        catch (AggregateException failure) when (
                            failure.Flatten().InnerExceptions.Count == 1 &&
                            ReferenceEquals(failure.Flatten().InnerExceptions[0], expected))
                        {
                        }
                    }
                }
                finally
                {
                    await retainedDrain.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }

            async Task RetainWorkerDrainAsync()
            {
                await realWorker.ConfigureAwait(false);
                await release.Task.ConfigureAwait(false);
            }
        }
    }
}
