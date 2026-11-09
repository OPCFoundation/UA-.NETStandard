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
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

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
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var workerExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var retainedWorker = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var expected = new InvalidOperationException("The worker cancellation callback failed.");
            var fixture = new ServerFixture<RetainedWorkerTestServer>(
                telemetry => new RetainedWorkerTestServer(telemetry, subscriptionWorker, DecorateWorker))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            CancellationTokenRegistration callback = default;
            Task? retainedDrain = null;
            Task? stopping = null;
            try
            {
                RetainedWorkerTestServer owner = await fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
                IServerInternal server = owner.CurrentInstance;
                retainedDrain = await retainedWorker.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                CancellationToken cancellation = subscriptionWorker
                    ? ((SubscriptionManager)server.SubscriptionManager).PublishWorkerCancellationToken
                    : ((SessionManager)server.SessionManager).SessionMonitorCancellationToken;
                Assert.That(cancellation.CanBeCanceled, Is.True);
                callback = cancellation.Register(() =>
                {
                    cancelEntered.TrySetResult(true);
                    throw expected;
                });
                stopping = subscriptionWorker
                    ? server.SubscriptionManager.ShutdownAsync(timeout.Token).AsTask()
                    : server.SessionManager.ShutdownAsync(timeout.Token).AsTask();
                await cancelEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await workerExited.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
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
                    try
                    {
                        if (retainedDrain is not null)
                        {
                            await retainedDrain.WaitAsync(timeout.Token).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        await fixture.StopAsync().ConfigureAwait(false);
                    }
                }
            }

            Task DecorateWorker(Task realWorker)
            {
                Task drain = RetainWorkerDrainAsync(realWorker);
                retainedWorker.SetResult(drain);
                return drain;
            }

            async Task RetainWorkerDrainAsync(Task realWorker)
            {
                await realWorker.ConfigureAwait(false);
                workerExited.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Decorates one real manager worker before server startup retains its task.
        /// </summary>
        // TODO: Remove when CA1812 recognizes construction through ServerFixture<T>.
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1812:Avoid uninstantiated internal classes",
            Justification = "ServerFixture<T> creates this server from the test factory.")]
        private sealed class RetainedWorkerTestServer : ReferenceServer
        {
            /// <summary>
            /// Creates a server with a construction-time decorator for the selected worker.
            /// </summary>
            public RetainedWorkerTestServer(
                ITelemetryContext telemetry,
                bool subscriptionWorker,
                Func<Task, Task> workerDecorator)
                : base(telemetry)
            {
                m_subscriptionWorker = subscriptionWorker;
                m_workerDecorator = workerDecorator;
            }

            /// <inheritdoc/>
            protected override ISessionManager CreateSessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return m_subscriptionWorker
                    ? base.CreateSessionManager(server, configuration)
                    : new SessionManager(server, configuration, TimeProvider, m_workerDecorator);
            }

            /// <inheritdoc/>
            protected override ISubscriptionManager CreateSubscriptionManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return m_subscriptionWorker
                    ? new SubscriptionManager(server, configuration, TimeProvider, m_workerDecorator)
                    : base.CreateSubscriptionManager(server, configuration);
            }

            /// <summary>Whether to decorate the subscription-publish worker.</summary>
            private readonly bool m_subscriptionWorker;

            /// <summary>The construction-time worker decorator.</summary>
            private readonly Func<Task, Task> m_workerDecorator;
        }
    }
}
