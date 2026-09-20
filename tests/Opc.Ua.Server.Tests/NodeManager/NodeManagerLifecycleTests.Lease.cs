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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task CommittedBatchReleasesOperationLeaseBeforeOptionalDisposalAsync(
            bool failPublication, bool disposeBeforeShutdown)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            IServerInternal server = m_server.CurrentInstance;
            TrackingLifecycleNodeManager manager = null;
            IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kFirstRegistrationValue, value => manager = value))], timeout.Token).ConfigureAwait(false);
            var failure = new IOException("The committed publication callback failed.");
            bool drainedWithoutDisposal;
            try
            {
                NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, () =>
                {
                    if (failPublication)
                    {
                        throw failure;
                    }
                }, timeout.Token).ConfigureAwait(false);
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(result.Registrations.Count, Is.EqualTo(1));
                if (failPublication)
                {
                    Assert.That(result.CleanupFailure, Is.TypeOf<AggregateException>());
                    Assert.That(((AggregateException)result.CleanupFailure).Flatten().InnerExceptions,
                        Does.Contain(failure));
                }
                else
                {
                    Assert.That(result.CleanupFailure, Is.Null);
                }
                if (disposeBeforeShutdown)
                {
                    await prepared.DisposeAsync().ConfigureAwait(false);
                }
                Task shutdown = lifecycle.BeginShutdownAsync(server, timeout.Token).AsTask();
                drainedWithoutDisposal = shutdown.IsCompleted;
                await prepared.DisposeAsync().ConfigureAwait(false);
                await prepared.DisposeAsync().ConfigureAwait(false);
                await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(manager.DisposeCount, Is.Zero, "Disposing the committed owner must not undo publication.");
                Assert.That(lifecycle.Registrations[0], Is.SameAs(result.Registrations[0]));
            }
            finally
            {
                await prepared.DisposeAsync().ConfigureAwait(false);
                await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
                AssertServerInternalsDisposed();
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
            Assert.That(drainedWithoutDisposal, Is.True,
                "A completed commit, including a committed warning, must not retain an operation until DisposeAsync.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UncommittedBatchRetainsOperationLeaseUntilAbortAsync(bool rejectDecision)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            IServerInternal server = m_server.CurrentInstance;
            TrackingLifecycleNodeManager candidate = null;
            IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                    kFirstRegistrationValue, value => candidate = value))], timeout.Token).ConfigureAwait(false);
            try
            {
                if (rejectDecision)
                {
                    await Assert.ThatAsync(() => prepared.CommitAsync(
                        _ => throw new IOException("Confirmed noncommit."), timeout.Token).AsTask(),
                        Throws.TypeOf<IOException>()).ConfigureAwait(false);
                }
                Task shutdown = lifecycle.BeginShutdownAsync(server, timeout.Token).AsTask();
                Assert.That(shutdown.IsCompleted, Is.False,
                    "Unpublished candidate cleanup still owns the operation until it is asynchronously disposed.");
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(candidate.DisposeCount, Is.Zero);
                await prepared.DisposeAsync().ConfigureAwait(false);
                await prepared.DisposeAsync().ConfigureAwait(false);
                await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(candidate.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(candidate.DisposeCount, Is.EqualTo(1));
                Assert.That(lifecycle.Registrations.IsEmpty, Is.True);
            }
            finally
            {
                await prepared.DisposeAsync().ConfigureAwait(false);
                await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);
                AssertServerInternalsDisposed();
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CommittedBatchKeepsOperationLeaseWhileReadinessAndDisposalArePendingAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager manager = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                {
                    manager = new ReadinessLifecycleNodeManager(server, configuration, m_logger, kFirstRegistrationValue)
                    {
                        ReadinessCallback = async _ =>
                        {
                            entered.TrySetResult(true);
                            await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        }
                    };
                    return new ValueTask<IAsyncNodeManager>(manager);
                });
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(_ => default, timeout.Token).AsTask();
            Task disposal = null;
            Task shutdown = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(prepared.IsCommitted, Is.True);
                disposal = prepared.DisposeAsync().AsTask();
                shutdown = lifecycle.BeginShutdownAsync(m_server.CurrentInstance, timeout.Token).AsTask();
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(commit.IsCompleted, Is.False);
                    Assert.That(disposal.IsCompleted, Is.False);
                    Assert.That(shutdown.IsCompleted, Is.False);
                    Assert.That(manager.DisposeCount, Is.Zero);
                }
            }
            finally
            {
                release.TrySetResult(true);
                await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
                await prepared.DisposeAsync().ConfigureAwait(false);
                if (disposal is not null)
                {
                    await disposal.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                if (shutdown is not null)
                {
                    await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                Assert.That(manager.ReadinessCompletedCount, Is.EqualTo(1));
                await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
                AssertServerInternalsDisposed();
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }
    }
}
