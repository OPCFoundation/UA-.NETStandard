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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class AsyncNodeManagerDisposalReviewTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposeCoreCanUseGuardedMembersWithoutReopeningCallerAdmissionAsync(bool pause)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var manager = new CleanupNodeManager(server.Object);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                manager.Cleanup = async () =>
                {
                    Assert.That(manager.FindCachedNode(), Is.SameAs(manager.CachedNode));
                    entered.SetResult(true);
                    if (pause)
                    {
                        await release.Task.ConfigureAwait(false);
                    }
                    Assert.That(manager.FindCachedNode(), Is.SameAs(manager.CachedNode));
                    using var context = new OperationContext(
                        new RequestHeader(), null, RequestType.Write, RequestLifetime.None);
                    await manager.WriteAsync(context, [], [], CancellationToken.None).ConfigureAwait(false);
                };

                Task disposal = manager.DisposeAsync().AsTask();
                try
                {
                    Task first = await Task.WhenAny(entered.Task, disposal)
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    if (first == disposal)
                    {
                        await disposal.ConfigureAwait(false);
                    }
                    Assert.That(entered.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                    if (pause)
                    {
                        Assert.That(disposal.IsCompleted, Is.False);
                        Assert.That(() => manager.FindCachedNode(), Throws.TypeOf<ObjectDisposedException>());
                        Task concurrentDisposal = manager.DisposeAsync().AsTask();
                        Assert.That(concurrentDisposal.IsCompleted, Is.False);
                        Assert.That(manager.RetainedNodes, Is.EqualTo(1));
                        Assert.That(manager.OwnedDisposeCalls, Is.Zero);
                        release.SetResult(true);
                        await concurrentDisposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                finally
                {
                    release.TrySetResult(true);
                    await manager.DisposeAsync().ConfigureAwait(false);
                }

                Assert.That(manager.CleanupCalls, Is.EqualTo(1));
                Assert.That(manager.OwnedDisposeCalls, Is.EqualTo(1));
                Assert.That(manager.RetainedNodes, Is.Zero);
                Assert.That(() => manager.FindCachedNode(), Throws.TypeOf<ObjectDisposedException>());
            }
        }

        [Test]
        public async Task SynchronousOwnedCleanupLetsSamplingWorkerReachClosedAdmissionAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var workerFinished = new ManualResetEventSlim())
            {
                var manager = new CleanupNodeManager(server.Object);
                var workerReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var resumeWorker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                bool workerFinishedDuringCleanup = false;
                manager.OwnedCleanup = () =>
                {
                    resumeWorker.SetResult(true);
                    // Model a sampling group joining its worker while disposing synchronously.
                    workerFinishedDuringCleanup = workerFinished.Wait(TimeSpan.FromSeconds(5));
                };
                Task worker = Task.Run(async () =>
                {
                    workerReady.SetResult(true);
                    await resumeWorker.Task.ConfigureAwait(false);
                    try
                    {
                        Assert.That(() => manager.FindCachedNode(), Throws.TypeOf<ObjectDisposedException>());
                    }
                    finally
                    {
                        workerFinished.Set();
                    }
                });

                try
                {
                    await workerReady.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    manager.Dispose();
                    await worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await manager.DisposeAsync().ConfigureAwait(false);

                    Assert.That(workerFinishedDuringCleanup, Is.True,
                        "Owned cleanup must not hold the admission lock while joining a sampling worker.");
                    Assert.That(manager.CleanupCalls, Is.EqualTo(1));
                    Assert.That(manager.OwnedDisposeCalls, Is.EqualTo(1));
                    Assert.That(manager.RetainedNodes, Is.Zero);
                }
                finally
                {
                    resumeWorker.TrySetResult(true);
                    await worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await manager.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task DisposalWaitsForWriteAfterItsSemaphoreIsReleasedAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var manager = new CleanupNodeManager(server.Object);
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.Write, RequestLifetime.None);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                manager.DeferredWrite = async () =>
                {
                    entered.SetResult(true);
                    await release.Task.ConfigureAwait(false);
                };
                ServiceResult[] errors = [ServiceResult.Good];
                Task write = manager.WriteAsync(context,
                    [new WriteValue { NodeId = manager.CachedNode.NodeId, AttributeId = Attributes.Value }],
                    errors, CancellationToken.None).AsTask();

                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await manager.WriteAsync(context, [], [], CancellationToken.None).AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Task disposal = manager.DisposeAsync().AsTask();

                    Assert.That(write.IsCompleted, Is.False);
                    Assert.That(disposal.IsCompleted, Is.False,
                        "Admission lasts through deferred callbacks, not just semaphore ownership.");
                    Assert.That(manager.RetainedNodes, Is.EqualTo(1));
                    Assert.That(manager.OwnedDisposeCalls, Is.Zero);
                    release.SetResult(true);
                    await write.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                finally
                {
                    release.TrySetResult(true);
                    await write.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await manager.DisposeAsync().ConfigureAwait(false);
                }

                Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(manager.OwnedDisposeCalls, Is.EqualTo(1));
                Assert.That(manager.RetainedNodes, Is.Zero);
            }
        }

        [Test]
        public async Task CapturedCleanupContextCannotAdmitWorkAfterTheCallbackReturnsAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var manager = new CleanupNodeManager(server.Object);
                ExecutionContext captured = null;
                manager.Cleanup = () =>
                {
                    Assert.That(manager.FindCachedNode(), Is.SameAs(manager.CachedNode));
                    captured = ExecutionContext.Capture();
                    return default;
                };
                manager.OwnedCleanup = () =>
                {
                    Assert.That(captured, Is.Not.Null);
                    using (captured)
                    {
                        ExecutionContext.Run(captured, _ =>
                            Assert.That(() => manager.FindCachedNode(), Throws.TypeOf<ObjectDisposedException>()), null);
                    }
                };

                await manager.DisposeAsync().ConfigureAwait(false);

                Assert.That(manager.CleanupCalls, Is.EqualTo(1));
                Assert.That(manager.OwnedDisposeCalls, Is.EqualTo(1));
            }
        }

        [Test]
        public void CleanupFailuresAreReportedAfterOwnedResourcesAreReleased()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var coreFailure = new InvalidOperationException("subclass cleanup failed");
                var ownedFailure = new InvalidOperationException("owned cleanup failed");
                var manager = new CleanupNodeManager(server.Object)
                {
                    Cleanup = () => throw coreFailure,
                    OwnedCleanup = () => throw ownedFailure
                };

                AggregateException failure = Assert.ThrowsAsync<AggregateException>(
                    async () => await manager.DisposeAsync().ConfigureAwait(false));
                Assert.That(failure.InnerExceptions, Is.EqualTo(new[] { coreFailure, ownedFailure }));
                AggregateException repeated = Assert.ThrowsAsync<AggregateException>(
                    async () => await manager.DisposeAsync().ConfigureAwait(false));

                Assert.That(repeated, Is.SameAs(failure));
                Assert.That(manager.CleanupCalls, Is.EqualTo(1));
                Assert.That(manager.OwnedDisposeCalls, Is.EqualTo(1));
                Assert.That(manager.RetainedNodes, Is.Zero);
                Assert.That(() => manager.FindCachedNode(), Throws.TypeOf<ObjectDisposedException>());
            }
        }

        private sealed class CleanupNodeManager : AsyncCustomNodeManager
        {
            public CleanupNodeManager(IServerInternal server)
                : base(server, "urn:disposal-review")
            {
                CachedNode = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("cached", NamespaceIndexes[0]),
                    BrowseName = new QualifiedName("Cached", NamespaceIndexes[0]),
                    Value = new Variant(1)
                };
                PredefinedNodes[CachedNode.NodeId] = CachedNode;
                m_handle = new NodeHandle { NodeId = CachedNode.NodeId };
                AddNodeToComponentCache(SystemContext, m_handle, CachedNode);

                IMonitoredItemManager ownedManager = m_monitoredItemManager;
                var monitoredItems = new Mock<IMonitoredItemManager>();
                monitoredItems.Setup(value => value.Dispose()).Callback(() =>
                {
                    OwnedDisposeCalls++;
                    ownedManager.Dispose();
                    OwnedCleanup?.Invoke();
                });
                m_monitoredItemManager = monitoredItems.Object;
            }

            public Func<ValueTask> Cleanup { get; set; }
            public Func<ValueTask> DeferredWrite { get; set; }
            public Action OwnedCleanup { get; set; }
            public NodeState CachedNode { get; }
            public int CleanupCalls { get; private set; }
            public int OwnedDisposeCalls { get; private set; }
            public int RetainedNodes => PredefinedNodes.Count;

            public NodeState FindCachedNode()
            {
                return LookupNodeInComponentCache(SystemContext, m_handle);
            }

            protected override async ValueTask DisposeAsyncCore()
            {
                CleanupCalls++;
                if (Cleanup != null)
                {
                    await Cleanup().ConfigureAwait(false);
                }
                await base.DisposeAsyncCore().ConfigureAwait(false);
            }

            protected override ValueTask<NodeHandle> GetManagerHandleAsync(
                ServerSystemContext context,
                NodeId nodeId,
                IDictionary<NodeId, NodeState> cache,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<NodeHandle>(new NodeHandle { NodeId = nodeId });
            }

            protected override async ValueTask WriteAsync(
                ServerSystemContext context,
                ArrayOf<WriteValue> nodesToWrite,
                IList<ServiceResult> errors,
                List<NodeHandle> nodesToValidate,
                IDictionary<NodeId, NodeState> cache,
                CancellationToken cancellationToken = default)
            {
                await DeferredWrite().ConfigureAwait(false);
                foreach (NodeHandle handle in nodesToValidate)
                {
                    errors[handle.Index] = ServiceResult.Good;
                }
            }

            private readonly NodeHandle m_handle;
        }
    }
}
