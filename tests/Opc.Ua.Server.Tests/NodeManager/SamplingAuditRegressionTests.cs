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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    public sealed class SamplingAuditRegressionTests
    {
        [TestCase(0, 100)]
        [TestCase(50, 100)]
        [TestCase(100, 100)]
        [TestCase(250, 250)]
        public void SamplingGroupsCanLowerCreatedIntervals(double requested, double revised)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (OperationContext operation = CreateContext())
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                var configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration()
                };
                using var manager = new SamplingGroupMonitoredItemManager(
                    nodeManager.Object, server.Object, configuration);
                ServerSystemContext context = server.Object.DefaultSystemContext.Copy(operation);
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId(1, 1),
                    MinimumSamplingInterval = 100
                };
                ISampledDataChangeMonitoredItem item = manager.CreateMonitoredItem(
                    server.Object, nodeManager.Object, context, new NodeHandle { Node = node },
                    1, 500, DiagnosticsMasks.None, TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.Value },
                        MonitoringMode = MonitoringMode.Disabled,
                        RequestedParameters = new MonitoringParameters { SamplingInterval = 1000, QueueSize = 1 }
                    },
                    new Range(), new DataChangeFilter(), 1000, 1, false,
                    new MonitoredItemIdFactory(), (_, _, current) => current);
                double sourceMinimum = item.MinimumSamplingInterval;

                ServiceResult error = manager.ModifyMonitoredItem(
                    context, DiagnosticsMasks.None, TimestampsToReturn.Both,
                    new DataChangeFilter(), new Range(), revised, 1, item,
                    new MonitoredItemModifyRequest
                    {
                        MonitoredItemId = item.Id,
                        RequestedParameters = new MonitoringParameters { SamplingInterval = requested, QueueSize = 1 }
                    });

                Assert.Multiple(() =>
                {
                    Assert.That(sourceMinimum, Is.EqualTo(100));
                    Assert.That(ServiceResult.IsGood(error), Is.True);
                    Assert.That(item.SamplingInterval, Is.EqualTo(revised));
                });
            }
        }

        [Test]
        public async Task NonFiniteIntervalWithDiscreteRateGroupDoesNotSpinAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (OperationContext context = CreateContext())
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                var item = new Mock<ISampledDataChangeMonitoredItem>();
                item.SetupGet(value => value.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
                item.SetupGet(value => value.MonitoringMode).Returns(MonitoringMode.Reporting);
                item.SetupGet(value => value.SamplingInterval).Returns(double.NaN);
                var adjusting = Task.Run(() =>
                {
                    using var group = new SamplingGroup(
                        server.Object, nodeManager.Object, [new SamplingRateGroup(100, 0, 1)], context, double.NaN);
                    Assert.That(group.StartMonitoring(context, item.Object), Is.True);
                    item.Verify(value => value.SetSamplingInterval(100), Times.Once);
                });
                await adjusting.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task NodeManagerDisposesSamplingOutsideItsNodeLockAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var readCompleted = new ManualResetEventSlim())
            {
                var monitoredItems = new Mock<IMonitoredItemManager>();
                using var manager = new TestNodeManager(server.Object, monitoredItems.Object);
                Task reader = Task.CompletedTask;
                monitoredItems.Setup(value => value.Dispose()).Callback(() =>
                {
                    reader = Task.Run(() =>
                    {
                        try
                        {
                            using var context = new OperationContext(
                                new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
                            Assert.That(() => manager.Read(context, 0, [], [], []),
                                Throws.TypeOf<ObjectDisposedException>());
                        }
                        finally
                        {
                            readCompleted.Set();
                        }
                    });
                    Assert.That(readCompleted.Wait(TimeSpan.FromSeconds(2)), Is.True,
                        "Sampling shutdown cannot join a reader while holding the reader's node lock.");
                });
                try
                {
                    await Task.Run(manager.Dispose).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                finally
                {
                    await reader.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task ShutdownRejectsLifecycleCallsWhileOwnedDisposalIsRunningAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var release = new ManualResetEventSlim())
            {
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var monitoredItems = new Mock<IMonitoredItemManager>();
                Mock<IMonitoredItemManagerLifecycle> lifecycle = monitoredItems.As<IMonitoredItemManagerLifecycle>();
                lifecycle.Setup(value => value.GetMonitoredItemsSnapshot(It.IsAny<IReadOnlyCollection<NodeId>>()))
                    .Returns([]);
                using var manager = new TestNodeManager(server.Object, monitoredItems.Object);
                monitoredItems.Setup(value => value.Dispose()).Callback(() =>
                {
                    entered.TrySetResult(true);
                    Assert.That(release.Wait(TimeSpan.FromSeconds(5)), Is.True);
                });
                var disposal = Task.Run(manager.Dispose);
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(async () =>
                        await ((INodeManagerMonitoredItemLifecycle)manager)
                            .GetMonitoredItemsSnapshotAsync(null, CancellationToken.None).ConfigureAwait(false),
                        Throws.TypeOf<ObjectDisposedException>());
                    lifecycle.Verify(value => value.GetMonitoredItemsSnapshot(
                        It.IsAny<IReadOnlyCollection<NodeId>>()), Times.Never);
                }
                finally
                {
                    release.Set();
                    await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                monitoredItems.Verify(value => value.Dispose(), Times.Once);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ShutdownDrainsAdmittedCallsBeforeDisposingTheirManagerAsync(bool throughAdapter)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (OperationContext context = CreateContext())
            {
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var monitoredItems = new Mock<IMonitoredItemManager>();
                using var manager = new TestNodeManager(server.Object, monitoredItems.Object);
                manager.PendingCall = async cancellationToken =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    Assert.That(manager.Find(manager.RetainedNode.NodeId), Is.SameAs(manager.RetainedNode));
                    Assert.That(manager.FindPredefinedNode<BaseDataVariableState>(manager.RetainedNode.NodeId),
                        Is.SameAs(manager.RetainedNode));
                    Assert.That(FindPredefinedNodeByType(manager), Is.SameAs(manager.RetainedNode));
                    DataValue[] values = [default];
                    ServiceResult[] errors = [ServiceResult.Good];
                    manager.Read(context, 0,
                        [new ReadValueId { NodeId = manager.RetainedNode.NodeId, AttributeId = Attributes.Value }],
                        values, errors);
                    Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant(1)));
                };
                IAsyncNodeManager adapter = manager.ToAsyncNodeManager();
                IDisposable owner = throughAdapter ? (IDisposable)adapter : manager;
                int cleanupCalls = 0;
                int cleanupCallsAtCommit = -1;
                monitoredItems.Setup(value => value.Dispose()).Callback(() => Interlocked.Increment(ref cleanupCalls));
                monitoredItems.Setup(value => value.ApplyChanges())
                    .Callback(() => cleanupCallsAtCommit = Volatile.Read(ref cleanupCalls));
                Task operation = throughAdapter
                    ? adapter.CallAsync(context, [], [], []).AsTask()
                    : manager.CallAsync(context, [], [], []).AsTask();
                Task disposal = Task.CompletedTask;
                Task concurrentDisposal = Task.CompletedTask;
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    disposal = DisposeAsync(owner);
                    manager.Dispose();
                    concurrentDisposal = manager.DisposeAsync().AsTask();
                    Assert.Multiple(() =>
                    {
                        Assert.That(disposal.IsCompleted, Is.False);
                        Assert.That(concurrentDisposal.IsCompleted, Is.False);
                        Assert.That(Volatile.Read(ref cleanupCalls), Is.Zero);
                        Assert.That(manager.RetainedNodes, Is.EqualTo(1));
                    });
                }
                finally
                {
                    release.TrySetResult(true);
                    await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await concurrentDisposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await DisposeAsync(owner).ConfigureAwait(false);
                }
                Assert.Multiple(() =>
                {
                    Assert.That(cleanupCallsAtCommit, Is.Zero);
                    Assert.That(Volatile.Read(ref cleanupCalls), Is.EqualTo(1));
                    Assert.That(manager.RetainedNodes, Is.Zero);
                });
                monitoredItems.Verify(value => value.ApplyChanges(), Times.Once);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ReturnedOperationContextCannotAdmitWorkDuringOrAfterDisposalAsync(
            bool asynchronous,
            bool whileDraining)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (OperationContext context = CreateContext())
            {
                var monitoredItems = new Mock<IMonitoredItemManager>();
                using var manager = new TestNodeManager(server.Object, monitoredItems.Object);
                ExecutionContext capturedContext = null;
                if (asynchronous)
                {
                    manager.PendingCall = _ =>
                    {
                        capturedContext = ExecutionContext.Capture();
                        return default;
                    };
                    await manager.CallAsync(context, [], [], []).ConfigureAwait(false);
                }
                else
                {
                    manager.RetainedNode.OnSimpleReadValue = (_, _, ref value) =>
                    {
                        capturedContext = ExecutionContext.Capture();
                        return ServiceResult.Good;
                    };
                    DataValue[] values = [default];
                    ServiceResult[] errors = [ServiceResult.Good];
                    manager.Read(context, 0,
                        [new ReadValueId { NodeId = manager.RetainedNode.NodeId, AttributeId = Attributes.Value }],
                        values, errors);
                    manager.RetainedNode.OnSimpleReadValue = null;
                    Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant(1)));
                }
                Assert.That(capturedContext, Is.Not.Null);
                using (capturedContext)
                {
                    var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    manager.PendingCall = async cancellationToken =>
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                        Assert.That(manager.Find(manager.RetainedNode.NodeId), Is.SameAs(manager.RetainedNode));
                    };
                    Task operation = manager.CallAsync(context, [], [], []).AsTask();
                    Task disposal = Task.CompletedTask;
                    try
                    {
                        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        disposal = manager.DisposeAsync().AsTask();
                        Assert.That(disposal.IsCompleted, Is.False);
                        if (!whileDraining)
                        {
                            release.TrySetResult(true);
                            await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                            await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        }
                        Assert.That(manager.RetainedNodes, Is.EqualTo(whileDraining ? 1 : 0));
                        monitoredItems.Verify(value => value.Dispose(), Times.Exactly(whileDraining ? 0 : 1));

                        ExecutionContext.Run(capturedContext, _ =>
                        {
                            Assert.That(() => manager.Read(context, 0, [], [], []),
                                Throws.TypeOf<ObjectDisposedException>());
                            Assert.That(manager.Find(manager.RetainedNode.NodeId), Is.Null);
                            Assert.That(
                                manager.FindPredefinedNode<BaseDataVariableState>(manager.RetainedNode.NodeId),
                                Is.Null);
                            Assert.That(FindPredefinedNodeByType(manager), Is.Null);
                        }, null);
                    }
                    finally
                    {
                        release.TrySetResult(true);
                        await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                }
                Assert.That(manager.RetainedNodes, Is.Zero);
                monitoredItems.Verify(value => value.Dispose(), Times.Once);
                monitoredItems.Verify(value => value.ApplyChanges(), Times.Exactly(asynchronous ? 2 : 1));
            }
        }

        [Test]
        public async Task DisposeAsyncStartsBaseDrainWhenLegacyOverrideOmitsBaseAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (OperationContext context = CreateContext())
            {
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var monitoredItems = new Mock<IMonitoredItemManager>();
                var manager = new TestNodeManager(server.Object, monitoredItems.Object)
                {
                    CallBaseDispose = false,
                    PendingCall = async ct =>
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                };
                Task operation = manager.CallAsync(context, [], [], []).AsTask();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Task disposal = manager.DisposeAsync().AsTask();
                try
                {
                    Assert.That(disposal.IsCompleted, Is.False);
                    Assert.That(manager.RetainedNodes, Is.EqualTo(1));
                    monitoredItems.Verify(value => value.Dispose(), Times.Never);
                    release.TrySetResult(true);
                    await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                    Assert.That(manager.DisposalCalls, Is.EqualTo(1));
                    Assert.That(manager.RetainedNodes, Is.Zero);
                    monitoredItems.Verify(value => value.Dispose(), Times.Once);
                    Assert.That(() => manager.Read(context, 0, [], [], []),
                        Throws.TypeOf<ObjectDisposedException>());
                }
                finally
                {
                    release.TrySetResult(true);
                    manager.DisposeBaseResources();
                    await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task AdapterAsyncDisposalInvokesNonIdempotentOverrideOnceAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var cleanup = new CancellationTokenSource())
            {
                var monitoredItems = new Mock<IMonitoredItemManager>();
                var manager = new TestNodeManager(server.Object, monitoredItems.Object)
                {
                    Disposing = () =>
                    {
                        cleanup.Cancel();
                        cleanup.Dispose();
                    }
                };
                try
                {
                    await DisposeAsync((IDisposable)manager.ToAsyncNodeManager())
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                    Assert.That(manager.DisposalCalls, Is.EqualTo(1));
                    Assert.That(manager.RetainedNodes, Is.Zero);
                    monitoredItems.Verify(value => value.Dispose(), Times.Once);
                }
                finally
                {
                    manager.DisposeBaseResources();
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ShutdownDrainsSynchronousServiceAndLifecycleOwnersOutsideTheirLocksAsync(bool lifecycleCall)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (OperationContext context = CreateContext())
            using (var release = new ManualResetEventSlim())
            using (var workerFinished = new ManualResetEventSlim())
            {
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var monitoredItems = new Mock<IMonitoredItemManager>();
                Mock<IMonitoredItemManagerLifecycle> lifecycle = monitoredItems.As<IMonitoredItemManagerLifecycle>();
                IReadOnlyList<IMonitoredItem> expectedSnapshot = [Mock.Of<IMonitoredItem>()];
                lifecycle.Setup(value => value.GetMonitoredItemsSnapshot(It.IsAny<IReadOnlyCollection<NodeId>>()))
                    .Returns(() =>
                    {
                        entered.TrySetResult(true);
                        Assert.That(release.Wait(TimeSpan.FromSeconds(5)), Is.True);
                        return expectedSnapshot;
                    });
                using var manager = new TestNodeManager(server.Object, monitoredItems.Object);
                manager.RetainedNode.OnSimpleReadValue = (_, _, ref value) =>
                {
                    entered.TrySetResult(true);
                    Assert.That(release.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    value = new Variant(42);
                    return ServiceResult.Good;
                };
                bool workerFinishedDuringCleanup = false;
                monitoredItems.Setup(value => value.Dispose()).Callback(() =>
                    workerFinishedDuringCleanup = workerFinished.Wait(TimeSpan.FromSeconds(5)));
                DataValue[] values = [default];
                ServiceResult[] errors = [ServiceResult.Good];
                IReadOnlyList<IMonitoredItem> snapshot = null;
                var operation = Task.Run(async () =>
                {
                    try
                    {
                        if (lifecycleCall)
                        {
                            snapshot = await ((INodeManagerMonitoredItemLifecycle)manager)
                                .GetMonitoredItemsSnapshotAsync(null, CancellationToken.None).ConfigureAwait(false);
                        }
                        else
                        {
                            manager.Read(context, 0,
                                [new ReadValueId
                                {
                                    NodeId = manager.RetainedNode.NodeId,
                                    AttributeId = Attributes.Value
                                }],
                                values, errors);
                        }
                    }
                    finally
                    {
                        workerFinished.Set();
                    }
                });
                Task disposal = Task.CompletedTask;
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    disposal = manager.DisposeAsync().AsTask();
                    Assert.That(disposal.IsCompleted, Is.False);
                    Assert.That(manager.RetainedNodes, Is.EqualTo(1));
                    monitoredItems.Verify(value => value.Dispose(), Times.Never);
                    await Task.Run(() =>
                        Assert.That(() => manager.Read(context, 0, [], [], []),
                            Throws.TypeOf<ObjectDisposedException>()))
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                finally
                {
                    release.Set();
                    await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await manager.DisposeAsync().ConfigureAwait(false);
                }
                Assert.Multiple(() =>
                {
                    Assert.That(workerFinishedDuringCleanup, Is.True,
                        "The last operation must not dispose and join its own sampling worker inline.");
                    Assert.That(manager.RetainedNodes, Is.Zero);
                    if (lifecycleCall)
                    {
                        Assert.That(snapshot, Is.SameAs(expectedSnapshot));
                    }
                    else
                    {
                        Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant(42)));
                    }
                });
                monitoredItems.Verify(value => value.Dispose(), Times.Once);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedOrCancelledAdmittedCallsStillReleaseTheirDisposalLeaseAsync(bool cancel)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (OperationContext context = CreateContext())
            using (var cancellation = new CancellationTokenSource())
            {
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var failure = new ServiceResultException(StatusCodes.BadCommunicationError);
                var monitoredItems = new Mock<IMonitoredItemManager>();
                using var manager = new TestNodeManager(server.Object, monitoredItems.Object)
                {
                    PendingCall = async ct =>
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                        throw failure;
                    }
                };
                Task operation = manager.CallAsync(context, [], [], [], cancellation.Token).AsTask();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Task disposal = manager.DisposeAsync().AsTask();
                Exception operationFailure = null;
                try
                {
                    Assert.That(disposal.IsCompleted, Is.False);
                    monitoredItems.Verify(value => value.Dispose(), Times.Never);
                }
                finally
                {
                    if (cancel)
                    {
                        cancellation.Cancel();
                    }
                    else
                    {
                        release.TrySetResult(true);
                    }
                    try
                    {
                        await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException exception) when (cancel)
                    {
                        operationFailure = exception;
                    }
                    catch (ServiceResultException exception) when (!cancel)
                    {
                        operationFailure = exception;
                    }
                    finally
                    {
                        release.TrySetResult(true);
                        await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        await manager.DisposeAsync().ConfigureAwait(false);
                    }
                }
                if (cancel)
                {
                    Assert.That(operationFailure, Is.InstanceOf<OperationCanceledException>());
                    Assert.That(((OperationCanceledException)operationFailure).CancellationToken,
                        Is.EqualTo(cancellation.Token));
                }
                else
                {
                    Assert.That(operationFailure, Is.SameAs(failure));
                }
                Assert.That(manager.RetainedNodes, Is.Zero);
                monitoredItems.Verify(value => value.ApplyChanges(), Times.Never);
                monitoredItems.Verify(value => value.Dispose(), Times.Once);
            }
        }

        [Test]
        public async Task DisposalFromAnAdmittedReadCallbackDoesNotWaitForItselfAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (OperationContext context = CreateContext())
            {
                var monitoredItems = new Mock<IMonitoredItemManager>();
                using var manager = new TestNodeManager(server.Object, monitoredItems.Object);
                Task disposal = null;
                manager.RetainedNode.OnSimpleReadValue = (_, _, ref value) =>
                {
                    manager.Dispose();
                    disposal = manager.DisposeAsync().AsTask();
                    Assert.That(disposal.IsCompleted, Is.False);
                    monitoredItems.Verify(item => item.Dispose(), Times.Never);
                    value = new Variant(42);
                    return ServiceResult.Good;
                };
                DataValue[] values = [default];
                ServiceResult[] errors = [ServiceResult.Good];

                manager.Read(context, 0,
                    [new ReadValueId { NodeId = manager.RetainedNode.NodeId, AttributeId = Attributes.Value }],
                    values, errors);
                Assert.That(disposal, Is.Not.Null);
                await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant(42)));
                    Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(manager.RetainedNodes, Is.Zero);
                });
                monitoredItems.Verify(value => value.Dispose(), Times.Once);
            }
        }

        [Test]
        public void OwnedCleanupFailureIsSharedAndStillClearsRetainedNodes()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var failure = new InvalidOperationException("Monitored-item cleanup failed.");
                var monitoredItems = new Mock<IMonitoredItemManager>();
                using var manager = new TestNodeManager(server.Object, monitoredItems.Object);
                monitoredItems.Setup(value => value.Dispose()).Throws(failure);

                manager.Dispose();
                InvalidOperationException first = Assert.ThrowsAsync<InvalidOperationException>(
                    () => manager.DisposeAsync().AsTask());
                InvalidOperationException repeated = Assert.ThrowsAsync<InvalidOperationException>(
                    () => DisposeAsync((IDisposable)manager.ToAsyncNodeManager()));

                Assert.Multiple(() =>
                {
                    Assert.That(first, Is.SameAs(failure));
                    Assert.That(repeated, Is.SameAs(failure));
                    Assert.That(manager.RetainedNodes, Is.Zero);
                });
                monitoredItems.Verify(value => value.Dispose(), Times.Once);
            }
        }

        [Test]
        public async Task ClosedManagerRejectsServiceAndLifecycleEntryPointsAsync(
            [Values] bool cleanupComplete,
            [Values(
                "Read", "Write", "HistoryRead", "HistoryUpdate", "Call", "CallAsync",
                "CreateMonitoredItems", "RestoreMonitoredItems", "ModifyMonitoredItems", "DeleteMonitoredItems",
                "TransferMonitoredItems", "SetMonitoringMode", "SubscribeToEvents", "SubscribeToAllEvents",
                "ConditionRefresh", "SessionActivated", "Snapshot", "CanAttach", "Attach", "Detach", "Recover",
                "Find", "FindPredefinedNode", "FindPredefinedNodeByType",
                "GetManagerHandle", "GetNodeMetadata", "GetPermissionMetadata",
                "TranslateBrowsePath", "Browse", "DeleteNode", "CreateNode", "CreateAddressSpace", "DeleteAddressSpace",
                "AddReferences", "DeleteReference", "FindMethodState", "IsNodeInView", "ValidateRolePermissions",
                "ValidateEventRolePermissions", "ValidateEventReceivePermissions")] string operationName)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (OperationContext context = CreateContext())
            {
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var monitoredItems = new Mock<IMonitoredItemManager>();
                using var manager = new TestNodeManager(server.Object, monitoredItems.Object)
                {
                    PendingCall = async ct => await release.Task.WaitAsync(ct).ConfigureAwait(false)
                };
                Task active = cleanupComplete
                    ? Task.CompletedTask
                    : manager.CallAsync(context, [], [], []).AsTask();
                manager.Dispose();
                try
                {
                    if (cleanupComplete)
                    {
                        await manager.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        monitoredItems.Verify(value => value.Dispose(), Times.Never);
                    }
                    ObjectDisposedException failure = null;
                    try
                    {
                        await InvokeOperationAsync(manager, context, operationName)
                            .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException exception)
                    {
                        failure = exception;
                    }
                    if (operationName is "Find" or "FindPredefinedNode" or "FindPredefinedNodeByType")
                    {
                        Assert.That(failure, Is.Null);
                    }
                    else
                    {
                        Assert.That(failure, Is.TypeOf<ObjectDisposedException>());
                        Assert.That(failure.ObjectName, Is.EqualTo(manager.GetType().Name));
                    }
                }
                finally
                {
                    release.TrySetResult(true);
                    await active.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await manager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                monitoredItems.Verify(value => value.Dispose(), Times.Once);
            }
        }

        private static async Task InvokeOperationAsync(
            TestNodeManager manager,
            OperationContext context,
            string operationName)
        {
            var lifecycle = (INodeManagerMonitoredItemLifecycle)manager;
            switch (operationName)
            {
                case "Read":
                    manager.Read(context, 0, [], [], []);
                    break;
                case "Write":
                    manager.Write(context, [], []);
                    break;
                case "HistoryRead":
                    manager.HistoryRead(
                        context, new ReadRawModifiedDetails(), TimestampsToReturn.Both, false, [], [], []);
                    break;
                case "HistoryUpdate":
                    manager.HistoryUpdate(context, typeof(UpdateDataDetails), [], [], []);
                    break;
                case "Call":
                    manager.Call(context, [], [], []);
                    break;
                case "CallAsync":
                    await manager.CallAsync(context, [], [], []).ConfigureAwait(false);
                    break;
                case "CreateMonitoredItems":
                    manager.CreateMonitoredItems(context, 1, 1000, TimestampsToReturn.Both, [],
                        [], [], [], false, new MonitoredItemIdFactory());
                    break;
                case "RestoreMonitoredItems":
                    manager.RestoreMonitoredItems([], [], null);
                    break;
                case "ModifyMonitoredItems":
                    manager.ModifyMonitoredItems(context, TimestampsToReturn.Both, [], [], [], []);
                    break;
                case "DeleteMonitoredItems":
                    manager.DeleteMonitoredItems(context, [], [], []);
                    break;
                case "TransferMonitoredItems":
                    manager.TransferMonitoredItems(context, false, [], [], [], new MonitoredItemTransferOptions());
                    break;
                case "SetMonitoringMode":
                    manager.SetMonitoringMode(context, MonitoringMode.Disabled, [], [], []);
                    break;
                case "SubscribeToEvents":
                    manager.SubscribeToEvents(context, new NodeHandle(), 1, null, false);
                    break;
                case "SubscribeToAllEvents":
                    manager.SubscribeToAllEvents(context, 1, null, false);
                    break;
                case "ConditionRefresh":
                    manager.ConditionRefresh(context, []);
                    break;
                case "SessionActivated":
                    manager.SessionActivated(context, new NodeId(1));
                    break;
                case "Snapshot":
                    await lifecycle.GetMonitoredItemsSnapshotAsync(null, CancellationToken.None).ConfigureAwait(false);
                    break;
                case "CanAttach":
                    await lifecycle.CanAttachMonitoredItemAsync(null, CancellationToken.None).ConfigureAwait(false);
                    break;
                case "Attach":
                    await lifecycle.AttachMonitoredItemAsync(null, CancellationToken.None).ConfigureAwait(false);
                    break;
                case "Detach":
                    await lifecycle.DetachMonitoredItemAsync(null, CancellationToken.None).ConfigureAwait(false);
                    break;
                case "Recover":
                    await lifecycle.RecoverMonitoredItemAsync(null, CancellationToken.None).ConfigureAwait(false);
                    break;
                case "Find":
                    Assert.That(manager.Find(manager.RetainedNode.NodeId), Is.Null);
                    break;
                case "FindPredefinedNode":
                    Assert.That(manager.FindPredefinedNode<BaseDataVariableState>(manager.RetainedNode.NodeId),
                        Is.Null);
                    break;
                case "FindPredefinedNodeByType":
                    Assert.That(FindPredefinedNodeByType(manager), Is.Null);
                    break;
                case "GetManagerHandle":
                    manager.GetManagerHandle(manager.RetainedNode.NodeId);
                    break;
                case "GetNodeMetadata":
                    manager.GetNodeMetadata(context, new NodeHandle(), BrowseResultMask.All);
                    break;
                case "GetPermissionMetadata":
                    manager.GetPermissionMetadata(context, new NodeHandle(), BrowseResultMask.All, [], false);
                    break;
                case "TranslateBrowsePath":
                    manager.TranslateBrowsePath(context, new NodeHandle(), new RelativePathElement(), [], []);
                    break;
                case "Browse":
                    ContinuationPoint continuationPoint = null;
                    manager.Browse(context, ref continuationPoint, []);
                    break;
                case "DeleteNode":
                    manager.DeleteNode(manager.SystemContext, manager.RetainedNode.NodeId);
                    break;
                case "CreateNode":
                    manager.CreateNode(manager.SystemContext, default, default, default, manager.RetainedNode);
                    break;
                case "CreateAddressSpace":
                    manager.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());
                    break;
                case "DeleteAddressSpace":
                    manager.DeleteAddressSpace();
                    break;
                case "AddReferences":
                    manager.AddReferences(new Dictionary<NodeId, IList<IReference>>());
                    break;
                case "DeleteReference":
                    manager.DeleteReference(new NodeHandle(), default, false, default, false);
                    break;
                case "FindMethodState":
                    manager.FindMethodState(context, new CallMethodRequest());
                    break;
                case "IsNodeInView":
                    manager.IsNodeInView(context, default, new NodeHandle());
                    break;
                case "ValidateRolePermissions":
                    manager.ValidateRolePermissions(context, manager.RetainedNode.NodeId, PermissionType.None);
                    break;
                case "ValidateEventRolePermissions":
                    manager.ValidateEventRolePermissions(null, null);
                    break;
                case "ValidateEventReceivePermissions":
                    manager.ValidateEventReceivePermissions(context, default, default);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operationName), operationName, null);
            }
        }

        private static NodeState FindPredefinedNodeByType(TestNodeManager manager)
        {
            // Bind the legacy overload without suppressing obsolete or prefer-generic diagnostics.
            MethodInfo method = typeof(CustomNodeManager2)
                .GetMethod(nameof(CustomNodeManager2.FindPredefinedNode), [typeof(NodeId), typeof(Type)])!;
#if NET8_0_OR_GREATER
            Func<NodeId, Type, NodeState> lookup = method.CreateDelegate<Func<NodeId, Type, NodeState>>(manager);
#else
            var lookup = (Func<NodeId, Type, NodeState>)method
                .CreateDelegate(typeof(Func<NodeId, Type, NodeState>), manager);
#endif
            return lookup(manager.RetainedNode.NodeId, typeof(BaseDataVariableState));
        }

        private static Task DisposeAsync(IDisposable owner)
        {
            if (owner is IAsyncDisposable asynchronous)
            {
                return asynchronous.DisposeAsync().AsTask();
            }
            owner.Dispose();
            return Task.CompletedTask;
        }

        private static OperationContext CreateContext()
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId(1));
            return new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None, session.Object);
        }

        private sealed class TestNodeManager : CustomNodeManager2, ICallAsyncNodeManager
        {
            public TestNodeManager(IServerInternal server, IMonitoredItemManager manager)
                : base(server, NullLogger.Instance, "urn:tests:sampling-audit")
            {
                m_monitoredItemManager.Dispose();
                m_monitoredItemManager = manager;
                RetainedNode = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId(1, NamespaceIndex),
                    BrowseName = new QualifiedName("Retained", NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Value = new Variant(1)
                };
                PredefinedNodes[RetainedNode.NodeId] = RetainedNode;
            }

            public Func<CancellationToken, ValueTask> PendingCall { get; set; }

            public Action Disposing { get; set; }

            public bool CallBaseDispose { get; set; } = true;

            public int DisposalCalls { get; private set; }

            public int RetainedNodes => PredefinedNodes.Count;

            public BaseDataVariableState RetainedNode { get; }

            public void DisposeBaseResources()
            {
                base.Dispose(true);
            }

            protected override void Dispose(bool disposing)
            {
                DisposalCalls++;
                Disposing?.Invoke();
                if (CallBaseDispose)
                {
                    base.Dispose(disposing);
                }
            }

            protected override async ValueTask CallInternalAsync(
                OperationContext context,
                ArrayOf<CallMethodRequest> methodsToCall,
                IList<CallMethodResult> results,
                IList<ServiceResult> errors,
                bool sync,
                CancellationToken cancellationToken = default)
            {
                if (PendingCall is null)
                {
                    await base.CallInternalAsync(
                        context, methodsToCall, results, errors, sync, cancellationToken).ConfigureAwait(false);
                    return;
                }
                await PendingCall(cancellationToken).ConfigureAwait(false);
                m_monitoredItemManager.ApplyChanges();
            }
        }
    }
}
