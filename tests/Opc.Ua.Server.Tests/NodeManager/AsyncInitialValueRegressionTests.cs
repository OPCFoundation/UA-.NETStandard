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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    public sealed class AsyncInitialValueRegressionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task PendingInitialReadAllowsAtomicMonitoredItemSnapshotAsync(bool samplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queues, new FakeTimeProvider());
            using (queues)
            await using (TestableAsyncCustomNodeManager manager = CreateManager(server.Object, samplingGroups))
            {
                await AssertPendingReadAsync(manager).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancelledInitialReadRemovesPartialRegistrationAsync(bool samplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queues, new FakeTimeProvider());
            using (queues)
            await using (TestableAsyncCustomNodeManager manager = CreateManager(server.Object, samplingGroups))
            {
                await AssertCancelledReadAsync(manager).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DeletingItemDuringInitialReadDoesNotPublishOrResurrectItAsync(bool samplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queues, new FakeTimeProvider());
            using (queues)
            await using (TestableAsyncCustomNodeManager manager = CreateManager(server.Object, samplingGroups))
            {
                await AssertDeletionDuringReadAsync(manager).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposalWaitsForPendingInitialReadAndClosesAdmissionAsync(bool samplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queues, new FakeTimeProvider());
            using (queues)
            await using (TestableAsyncCustomNodeManager manager = CreateManager(server.Object, samplingGroups))
            {
                await AssertDisposalWaitsForReadAsync(manager).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EnablingMonitoringReadsAsyncValueWithoutHoldingRegistrySemaphoreAsync(bool samplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queues, new FakeTimeProvider());
            using (queues)
            await using (TestableAsyncCustomNodeManager manager = CreateManager(server.Object, samplingGroups))
            {
                BaseDataVariableState node = await AddVariableAsync(manager).ConfigureAwait(false);
                using MonitoredItem item = await CreateItemAsync(manager, node, mode: MonitoringMode.Disabled)
                    .ConfigureAwait(false);
                AttachSession(item);
                var lifecycle = (INodeManagerMonitoredItemLifecycle)manager;
                int reads = 0;
                node.OnReadValueAsync = async (_, _, _, _, ct) =>
                {
                    reads++;
                    IReadOnlyList<IMonitoredItem> snapshot = await lifecycle
                        .GetMonitoredItemsSnapshotAsync(cancellationToken: ct).ConfigureAwait(false);
                    Assert.That(snapshot, Has.Count.EqualTo(1));
                    Assert.That(snapshot[0], Is.SameAs(item));
                    return CurrentValue(73);
                };
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.SetMonitoringMode, RequestLifetime.None, item.Session);
                var errors = new ServiceResult[1];
                await manager.SetMonitoringModeAsync(
                    context, MonitoringMode.Reporting, [item], [false], errors, cancellation.Token)
                    .ConfigureAwait(false);

                Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(reads, Is.EqualTo(1));
                Queue<MonitoredItemNotification> notifications = Drain(item);
                Assert.That(notifications, Has.Count.EqualTo(1));
                DataValue first = notifications.Dequeue().Value;
                Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(first.WrappedValue.TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(73));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LifecycleValidationAndRecoveryReadAsyncValueOutsideRegistrySemaphoreAsync(bool samplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queues, new FakeTimeProvider());
            using (queues)
            await using (TestableAsyncCustomNodeManager manager = CreateManager(server.Object, samplingGroups))
            {
                BaseDataVariableState node = await AddVariableAsync(manager).ConfigureAwait(false);
                node.OnReadValueAsync = (_, _, _, _, _) => new ValueTask<AttributeReadResult>(CurrentValue(1));
                using MonitoredItem item = await CreateItemAsync(manager, node).ConfigureAwait(false);
                AttachSession(item);
                var lifecycle = (INodeManagerMonitoredItemLifecycle)manager;
                Assert.That((await lifecycle.DetachMonitoredItemAsync(item).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                Drain(item);
                int reads = 0;
                node.OnReadValueAsync = async (_, _, _, _, ct) =>
                {
                    reads++;
                    IReadOnlyList<IMonitoredItem> snapshot = await lifecycle
                        .GetMonitoredItemsSnapshotAsync(cancellationToken: ct).ConfigureAwait(false);
                    Assert.That(snapshot, Is.Empty);
                    return CurrentValue(73);
                };
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                ServiceResult validation = await lifecycle.CanAttachMonitoredItemAsync(item, cancellation.Token)
                    .ConfigureAwait(false);
                ServiceResult recovered = await lifecycle.RecoverMonitoredItemAsync(item, cancellation.Token)
                    .ConfigureAwait(false);
                Queue<MonitoredItemNotification> notifications = Drain(item);

                Assert.Multiple(() =>
                {
                    Assert.That(validation.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(recovered.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(reads, Is.EqualTo(2));
                    Assert.That(notifications, Has.Count.EqualTo(1));
                });
                DataValue first = notifications.Dequeue().Value;
                Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(first.WrappedValue.TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(73));
            }
        }

        private static async Task AssertDisposalWaitsForReadAsync(TestableAsyncCustomNodeManager manager)
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            BaseDataVariableState node = await AddVariableAsync(manager).ConfigureAwait(false);
            node.OnReadValueAsync = async (_, _, _, _, ct) =>
            {
                started.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return CurrentValue(73);
            };
            Task<MonitoredItem> create = CreateItemAsync(manager, node);
            Task disposal = null;
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                disposal = manager.DisposeAsync().AsTask();
                Assert.That(disposal.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                using MonitoredItem item = await create.ConfigureAwait(false);
                if (disposal != null)
                {
                    await disposal.ConfigureAwait(false);
                }
            }
            try
            {
                await ((INodeManagerMonitoredItemLifecycle)manager).GetMonitoredItemsSnapshotAsync()
                    .ConfigureAwait(false);
                Assert.Fail("A disposed node manager must reject new operations.");
            }
            catch (ObjectDisposedException error)
            {
                Assert.That(error.ObjectName, Is.EqualTo(nameof(TestableAsyncCustomNodeManager)));
            }
        }

        private static async Task AssertDeletionDuringReadAsync(TestableAsyncCustomNodeManager manager)
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            BaseDataVariableState node = await AddVariableAsync(manager).ConfigureAwait(false);
            node.OnReadValueAsync = async (_, _, _, _, ct) =>
            {
                started.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return CurrentValue(73);
            };
            Task<(ServiceResult Error, MonitoredItem Item)> create = CreateItemResultAsync(manager, node);
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                IReadOnlyList<IMonitoredItem> snapshot = await ((INodeManagerMonitoredItemLifecycle)manager)
                    .GetMonitoredItemsSnapshotAsync().ConfigureAwait(false);
                Assert.That(snapshot, Has.Count.EqualTo(1));
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.DeleteMonitoredItems, RequestLifetime.None);
                var errors = new ServiceResult[1];
                await manager.DeleteMonitoredItemsAsync(context, [snapshot[0]], [false], errors)
                    .ConfigureAwait(false);
                Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(manager.MonitoredItems, Is.Empty);
            }
            finally
            {
                release.TrySetResult(true);
                (ServiceResult error, MonitoredItem item) = await create.ConfigureAwait(false);
                using (item)
                {
                    Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
                    Assert.That(item, Is.Null);
                }
            }
            Assert.That(manager.MonitoredItems, Is.Empty);
            node.OnReadValueAsync = (_, _, _, _, _) => new ValueTask<AttributeReadResult>(CurrentValue(73));
            using MonitoredItem replacement = await CreateItemAsync(manager, node).ConfigureAwait(false);
            Assert.That(manager.MonitoredItems[replacement.Id], Is.SameAs(replacement));
        }

        private static async Task AssertPendingReadAsync(TestableAsyncCustomNodeManager manager)
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            BaseDataVariableState node = await AddVariableAsync(manager).ConfigureAwait(false);
            node.OnReadValueAsync = async (_, _, _, _, ct) =>
            {
                started.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return CurrentValue(73);
            };

            Task<MonitoredItem> create = CreateItemAsync(manager, node);
            Task<IReadOnlyList<IMonitoredItem>> snapshot = null;
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                snapshot = ((INodeManagerMonitoredItemLifecycle)manager)
                    .GetMonitoredItemsSnapshotAsync().AsTask();
                Assert.That(snapshot.IsCompleted, Is.True,
                    "Provider I/O must not hold the monitored-item registry semaphore.");
            }
            finally
            {
                release.TrySetResult(true);
                using MonitoredItem item = await create.ConfigureAwait(false);
                if (snapshot != null)
                {
                    IReadOnlyList<IMonitoredItem> items = await snapshot.ConfigureAwait(false);
                    Assert.That(items, Has.Count.EqualTo(1));
                    Assert.That(items[0], Is.SameAs(item));
                }
            }
        }

        private static async Task AssertCancelledReadAsync(TestableAsyncCustomNodeManager manager)
        {
            using var cancellation = new CancellationTokenSource();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            BaseDataVariableState node = await AddVariableAsync(manager).ConfigureAwait(false);
            node.OnReadValueAsync = async (_, _, _, _, ct) =>
            {
                started.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                return CurrentValue(73);
            };

            Task<MonitoredItem> create = CreateItemAsync(manager, node, cancellationToken: cancellation.Token);
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            finally
            {
                cancellation.Cancel();
                try
                {
                    using MonitoredItem item = await create.ConfigureAwait(false);
                    Assert.Fail("Initial provider cancellation must propagate.");
                }
                catch (OperationCanceledException error)
                {
                    Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
                }
            }
            Assert.That(manager.MonitoredItems, Is.Empty);
            Assert.That(manager.MonitoredNodes, Has.Count.Zero);
        }

        private static TestableAsyncCustomNodeManager CreateManager(IServerInternal server, bool samplingGroups)
        {
            var manager = new TestableAsyncCustomNodeManager(
                server,
                new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxNotificationQueueSize = 100,
                        MaxDurableNotificationQueueSize = 100,
                        AvailableSamplingRates = []
                    }
                },
                samplingGroups,
                NullLogger.Instance,
                DeterministicServerMock.TestNamespaceUri);
            if (samplingGroups)
            {
                manager.InstallTrackingSamplingGroupManager();
            }
            return manager;
        }

        private static async Task<BaseDataVariableState> AddVariableAsync(TestableAsyncCustomNodeManager manager)
        {
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("AsyncValue", manager.NamespaceIndex),
                BrowseName = new QualifiedName("AsyncValue", manager.NamespaceIndex),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = -1
            };
            node.CreateAsPredefinedNode(manager.SystemContext);
            await manager.AddNodeAsync(manager.SystemContext, NodeId.Null, node).ConfigureAwait(false);
            return node;
        }

        private static async Task<MonitoredItem> CreateItemAsync(
            TestableAsyncCustomNodeManager manager,
            NodeState node,
            MonitoringMode mode = MonitoringMode.Reporting,
            CancellationToken cancellationToken = default)
        {
            (ServiceResult error, MonitoredItem item) = await CreateItemResultAsync(
                manager, node, mode, cancellationToken).ConfigureAwait(false);
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(item, Is.Not.Null);
            return item;
        }

        private static async Task<(ServiceResult Error, MonitoredItem Item)> CreateItemResultAsync(
            TestableAsyncCustomNodeManager manager,
            NodeState node,
            MonitoringMode mode = MonitoringMode.Reporting,
            CancellationToken cancellationToken = default)
        {
            var request = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = mode,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 1000,
                    QueueSize = 10,
                    DiscardOldest = true
                }
            };
            var errors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var items = new List<IMonitoredItem> { null };
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None, CreateSession());
            await manager.CreateMonitoredItemsAsync(
                context, 1, 1000, TimestampsToReturn.Both, [request], errors, filterErrors, items,
                false, new MonitoredItemIdFactory(), cancellationToken).ConfigureAwait(false);
            return (errors[0], items[0] as MonitoredItem);
        }

        private static AttributeReadResult CurrentValue(int value)
        {
            return new AttributeReadResult(ServiceResult.Good, new Variant(value), StatusCodes.Good, DateTimeUtc.Now);
        }

        private static void AttachSession(MonitoredItem item)
        {
            ISession session = CreateSession();
            var subscription = new Mock<ISubscription>();
            subscription.SetupGet(value => value.Session).Returns(session);
            subscription.SetupGet(value => value.EffectiveIdentity).Returns(session.EffectiveIdentity);
            item.SubscriptionCallback = subscription.Object;
        }

        private static ISession CreateSession()
        {
            var identity = new Mock<IUserIdentity>();
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Identity).Returns(identity.Object);
            session.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
            session.SetupGet(value => value.PreferredLocales).Returns([]);
            return session.Object;
        }

        private static Queue<MonitoredItemNotification> Drain(MonitoredItem item)
        {
            var notifications = new Queue<MonitoredItemNotification>();
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Publish, RequestLifetime.None);
            item.SetupResendDataTrigger();
            item.Publish(context, notifications, new Queue<DiagnosticInfo>(), 100, NullLogger.Instance);
            return notifications;
        }
    }
}
