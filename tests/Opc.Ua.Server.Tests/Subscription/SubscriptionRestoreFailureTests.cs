// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Covers the deprecated synchronous node manager API on purpose.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// A durable subscription restore that fails must surface instead of
    /// being logged and dropped, unless the manager is told to continue.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    public sealed class SubscriptionRestoreFailureTests
    {
        [Test]
        public async Task StoreThatThrowsFailsTheRestore()
        {
            var failure = new InvalidOperationException("store unavailable");
            using var fixture = new Fixture(store => store
                .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(failure));

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(exception.InnerException, Is.SameAs(failure));
        }

        [Test]
        public async Task StoreThatReportsFailureFailsTheRestore()
        {
            using var fixture = new Fixture(store => store
                .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RestoreSubscriptionResult(false, null)));

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
        }

        [Test]
        public async Task StoreWithNothingToRestoreSucceedsAsync()
        {
            using var fixture = new Fixture(store => store
                .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RestoreSubscriptionResult(true, [])));

            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(fixture.Manager.GetSubscriptions(), Is.Empty);
        }

        [Test]
        public async Task SubscriptionThatCannotBeRestoredFailsTheRestore()
        {
            using var fixture = new Fixture(
                store => store
                    .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new RestoreSubscriptionResult(true, [CreateStored(41)])),
                failingSubscriptionId: 41);

            AggregateException exception = await Assert.ThrowsAsync<AggregateException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false));

            Assert.That(exception.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(
                ((ServiceResultException)exception.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            fixture.Store.Verify(
                value => value.OnSubscriptionRestoreCompleteAsync(
                    It.IsAny<Dictionary<uint, ArrayOf<uint>>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ContinueOnRestoreFailureKeepsTheRestoredSubscriptionsAsync()
        {
            using var fixture = new Fixture(
                store => store
                    .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new RestoreSubscriptionResult(
                        true,
                        [CreateStored(41), CreateStored(42)])),
                failingSubscriptionId: 41);
            fixture.Manager.ContinueOnRestoreFailure = true;

            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(fixture.Manager.GetSubscriptions(), Has.Count.EqualTo(1));
            Assert.That(fixture.Manager.GetSubscriptions()[0].Id, Is.EqualTo(42u));
            fixture.Store.Verify(
                value => value.OnSubscriptionRestoreCompleteAsync(
                    It.Is<Dictionary<uint, ArrayOf<uint>>>(created =>
                        created.Count == 1 && created.ContainsKey(42u)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ContinueOnRestoreFailureLogsAStoreThatReportsFailureAsync()
        {
            using var fixture = new Fixture(store => store
                .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RestoreSubscriptionResult(false, null)));
            fixture.Manager.ContinueOnRestoreFailure = true;

            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(fixture.Manager.GetSubscriptions(), Is.Empty);
            Assert.That(
                fixture.Logs.Entries,
                Has.Some.Matches<(LogLevel Level, string Message)>(entry =>
                    entry.Level == LogLevel.Error &&
                    entry.Message.Contains("could not be restored", StringComparison.Ordinal)));
            fixture.Store.Verify(
                value => value.OnSubscriptionRestoreCompleteAsync(
                    It.IsAny<Dictionary<uint, ArrayOf<uint>>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ContinueOnRestoreFailureLogsTheLostSubscriptionsAsync()
        {
            using var fixture = new Fixture(
                store => store
                    .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new RestoreSubscriptionResult(
                        true,
                        [CreateStored(41), CreateStored(42)])),
                failingSubscriptionId: 41);
            fixture.Manager.ContinueOnRestoreFailure = true;

            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(
                fixture.Logs.Entries,
                Has.Some.Matches<(LogLevel Level, string Message)>(entry =>
                    entry.Level == LogLevel.Warning &&
                    entry.Message.StartsWith("1 of 2 stored durable subscriptions", StringComparison.Ordinal)));
        }

        [Test]
        public async Task StoreThatReportsFailureIsLoggedBeforeTheRestoreFails()
        {
            using var fixture = new Fixture(store => store
                .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RestoreSubscriptionResult(false, null)));

            await Assert.ThrowsAsync<ServiceResultException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false));

            Assert.That(
                fixture.Logs.Entries,
                Has.Some.Matches<(LogLevel Level, string Message)>(entry => entry.Level == LogLevel.Error));
        }

        [Test]
        public async Task ContinueOnRestoreFailureIgnoresAFailingStoreAsync()
        {
            using var fixture = new Fixture(store => store
                .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("store unavailable")));
            fixture.Manager.ContinueOnRestoreFailure = true;

            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(fixture.Manager.GetSubscriptions(), Is.Empty);
        }

        [Test]
        public async Task FailedRestoreDeletesTheRecreatedSubscriptionsAndCanBeRetriedAsync()
        {
            using var fixture = new Fixture(
                store => store
                    .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => new RestoreSubscriptionResult(
                        true,
                        [CreateStored(41), CreateStored(42)])),
                failingSubscriptionId: 41);

            await Assert.ThrowsAsync<AggregateException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false));

            AssertNothingRestored(fixture);
            fixture.Diagnostics.Verify(
                value => value.DeleteSubscriptionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(),
                    new NodeId(42u),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            fixture.Manager.FailingSubscriptionId = 0;
            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            AssertBothRestored(fixture, 41u, 42u);
        }

        [Test]
        public async Task CancelledRestoreDeletesTheRecreatedSubscriptionsAndCanBeRetriedAsync()
        {
            using var cancellation = new CancellationTokenSource();
            bool cancelRestoreOf43 = true;
            using var fixture = new Fixture(store =>
            {
                store
                    .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => new RestoreSubscriptionResult(
                        true,
                        [CreateStored(42), CreateStored(43)]));

                // The restore of 43 is cancelled after the subscription object exists.
                store.As<ISubscriptionRetransmissionStore>()
                    .Setup(value => value.LoadRetransmissionStateAsync(
                        It.IsAny<uint>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((uint subscriptionId, CancellationToken _) =>
                    {
                        if (subscriptionId == 43 && cancelRestoreOf43)
                        {
                            cancellation.Cancel();
                            throw new OperationCanceledException(cancellation.Token);
                        }
                        return new ValueTask<SubscriptionRetransmissionState?>(
                            (SubscriptionRetransmissionState?)null);
                    });
            });

            await Assert.CatchAsync<OperationCanceledException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync(cancellation.Token)
                    .ConfigureAwait(false));

            AssertNothingRestored(fixture);
            foreach (uint id in new[] { 42u, 43u })
            {
                fixture.Diagnostics.Verify(
                    value => value.DeleteSubscriptionDiagnosticsAsync(
                        It.IsAny<ServerSystemContext>(),
                        new NodeId(id),
                        It.IsAny<CancellationToken>()),
                    Times.Once);
            }

            cancelRestoreOf43 = false;
            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            AssertBothRestored(fixture, 42u, 43u);
        }

        [Test]
        public async Task RetryWithTheSameStoredObjectsRestoresTheirMonitoredItemsAsync()
        {
            // The store returns the same objects to every restore, and 43 fails after
            // its event item was recreated.
            var stored = new RestoreSubscriptionResult(
                true,
                [
                    CreateStored(42, CreateStoredDataItem(1002, 42, queueSize: 1)),
                    CreateStored(43, CreateStoredEventItem(1003, 43))
                ]);
            var source = new EventSourceNodeManager { FailRestoreOf = 1003 };
            using var fixture = new Fixture(
                store => store
                    .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(stored),
                nodeManager: source.Object);

            await Assert.ThrowsAsync<AggregateException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false));
            AssertNothingRestored(fixture);
            Assert.That(
                stored.Subscriptions!.SelectMany(subscription => subscription.MonitoredItems)
                    .Select(item => item.IsRestored),
                Is.All.False,
                "the stored objects are returned to the store as it provided them");

            source.FailRestoreOf = 0;
            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            AssertMonitoredItems(fixture, 42, 1002);
            AssertMonitoredItems(fixture, 43, 1003);
        }

        [Test]
        public async Task FailedRestoreKeepsThePersistedQueuesOfTheRecreatedSubscriptionsAsync()
        {
            var queues = new PersistentQueues();
            queues.SeedDataChangeQueue(1002);
            using var fixture = new Fixture(
                store =>
                {
                    store
                        .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(() => new RestoreSubscriptionResult(
                            true,
                            [CreateStored(41), CreateStored(42, CreateStoredDataItem(1002, 42, queueSize: 10))]));
                    queues.Serve(store);
                },
                failingSubscriptionId: 41);

            await Assert.ThrowsAsync<AggregateException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false));
            AssertNothingRestored(fixture);

            queues.AssertDataChangeQueueKept(1002);

            fixture.Manager.FailingSubscriptionId = 0;
            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            AssertMonitoredItems(fixture, 42, 1002);
        }

        [Test]
        public async Task FailedRestoreKeepsTheOriginalValuesOfAFullPersistedQueueAsync()
        {
            var queues = new PersistentQueues();
            queues.SeedDataChangeQueue(1002, queueSize: 2);
            using var fixture = new Fixture(
                store =>
                {
                    store
                        .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(() => new RestoreSubscriptionResult(
                            true,
                            [CreateStored(42, CreateStoredDataItem(1002, 42, queueSize: 2)), CreateStored(43)]));
                    queues.Serve(store);
                },
                failingSubscriptionId: 43);

            // Restoring the unowned item of 42 queues Bad_NodeIdUnknown into its full
            // queue, which discards the oldest in-memory value before 43 fails.
            await Assert.ThrowsAsync<AggregateException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false));
            AssertNothingRestored(fixture);
            queues.AssertDataChangeQueueKept(1002);

            fixture.Manager.FailingSubscriptionId = 0;
            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            AssertMonitoredItems(fixture, 42, 1002);
            Assert.That(queues.DataChangeReads, Has.Count.EqualTo(2));
            Assert.That(
                queues.DataChangeReads[1].Values,
                Is.EqualTo(new[] { new Variant(1), new Variant(2) }),
                "the retry restores every stored value, in order");

            // The completed restore writes the restored queue.
            Variant[] stored = queues.ReadStoredDataChangeValues(1002);
            Assert.That(stored, Has.Length.EqualTo(2));
            Assert.That(stored[0], Is.EqualTo(new Variant(2)));
        }

        [Test]
        public async Task RestoreCancelledDuringItemRestorationKeepsThePersistedQueuesAsync()
        {
            var queues = new PersistentQueues();
            queues.SeedDataChangeQueue(1002);
            queues.SeedEventQueue(1003);
            using var cancellation = new CancellationTokenSource();
            var source = new EventSourceNodeManager
            {
                FailRestoreOf = 1003,
                RestoreFailure = () =>
                {
                    cancellation.Cancel();
                    return new OperationCanceledException(cancellation.Token);
                }
            };
            using var fixture = new Fixture(
                store =>
                {
                    store
                        .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(() => new RestoreSubscriptionResult(
                            true,
                            [
                                CreateStored(42, CreateStoredDataItem(1002, 42, queueSize: 10)),
                                CreateStored(43, CreateStoredEventItem(1003, 43))
                            ]));
                    queues.Serve(store);
                },
                nodeManager: source.Object);

            // 43's event item is recreated, then the restore of its other items is cancelled.
            await Assert.CatchAsync<OperationCanceledException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync(cancellation.Token)
                    .ConfigureAwait(false));
            AssertNothingRestored(fixture);
            Assert.That(fixture.Events.GetMonitoredItems(), Is.Empty);

            queues.AssertDataChangeQueueKept(1002);
            queues.AssertEventQueueKept(1003);

            source.FailRestoreOf = 0;
            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            AssertMonitoredItems(fixture, 42, 1002);
            AssertMonitoredItems(fixture, 43, 1003);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task FailedCreationOfARestoredItemKeepsItsPersistedQueueAsync(bool asynchronous)
        {
            var queues = new PersistentQueues();
            queues.SeedDataChangeQueue(1002);
            ICreationFailingNodeManager? owner = null;
            using var fixture = new Fixture(
                store =>
                {
                    store
                        .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(() => new RestoreSubscriptionResult(
                            true,
                            [CreateStored(42, CreateStoredOwnedDataItem(1002, 42))]));
                    queues.Serve(store);
                },
                createNodeManager: server =>
                {
                    if (asynchronous)
                    {
                        var manager = new AsyncCreationFailingNodeManager(server) { FailCreationOf = 1002 };
                        owner = manager;
                        return manager;
                    }
                    var synchronous = new SyncCreationFailingNodeManager(server) { FailCreationOf = 1002 };
                    owner = synchronous;
                    return synchronous.ToAsyncNodeManager();
                });

            // The node manager releases the item, and with it the restored queue, when
            // the creation of the item fails.
            AggregateException exception = await Assert.ThrowsAsync<AggregateException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false));
            Assert.That(exception.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
            AssertNothingRestored(fixture);
            queues.AssertDataChangeQueueKept(1002);

            owner!.FailCreationOf = 0;
            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            AssertMonitoredItems(fixture, 42, 1002);
            Assert.That(queues.DataChangeReads, Has.Count.EqualTo(2));
            Assert.That(
                queues.DataChangeReads[1].Values,
                Is.EqualTo(new[] { new Variant(1), new Variant(2) }),
                "the retry restores every stored value, in order");
        }

        [Test]
        public async Task RestoreCancelledWhileSubscribingAnEventItemReleasesItAsync()
        {
            using var cancellation = new CancellationTokenSource();
            var source = new EventSourceNodeManager
            {
                FailSubscribeOf = 1003,
                SubscribeFailure = () =>
                {
                    cancellation.Cancel();
                    return new OperationCanceledException(cancellation.Token);
                }
            };
            using var fixture = new Fixture(
                store => store
                    .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => new RestoreSubscriptionResult(
                        true,
                        [CreateStored(42), CreateStored(43, CreateStoredEventItem(1003, 43))])),
                nodeManager: source.Object);

            await Assert.CatchAsync<OperationCanceledException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync(cancellation.Token)
                    .ConfigureAwait(false));
            AssertNothingRestored(fixture);
            Assert.That(
                fixture.Events.GetMonitoredItems(),
                Is.Empty,
                "the event item registered before the cancellation is released");

            source.FailSubscribeOf = 0;
            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            AssertMonitoredItems(fixture, 43, 1003);
            Assert.That(fixture.Events.GetMonitoredItems(), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task EventItemWhoseSubscriptionIsRejectedIsReleasedAsync()
        {
            var queue = new DisposalRecordingEventQueue(1003, NUnitTelemetryContext.Create());
            var source = new EventSourceNodeManager { RejectSubscribeOf = 1003 };
            using var fixture = new Fixture(
                store =>
                {
                    store
                        .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(() => new RestoreSubscriptionResult(
                            true,
                            [CreateStored(43, CreateStoredEventItem(1003, 43))]));
                    store
                        .Setup(value => value.RestoreEventMonitoredItemQueueAsync(
                            It.IsAny<uint>(),
                            It.IsAny<CancellationToken>()))
                        .Returns(new ValueTask<IEventMonitoredItemQueue?>(queue));
                    store
                        .Setup(value => value.RestoreEventMonitoredItemQueue(It.IsAny<uint>()))
                        .Returns(queue);
                },
                nodeManager: source.Object);

            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            AssertMonitoredItems(fixture, 43);
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Events.GetMonitoredItems(), Is.Empty, "the event item is unregistered");
                Assert.That(queue.Disposed, Is.True, "the item that the subscription does not take is released");
            });
        }

        [Test]
        public async Task QueueThatFailsToCommitItsRestoreDoesNotFailTheRestoreAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var queues = new Dictionary<uint, StagedQueue>
            {
                [1002] = new StagedQueue(1002, telemetry) { FailCommit = true },
                [1004] = new StagedQueue(1004, telemetry),
                [1003] = new StagedQueue(1003, telemetry)
            };
            using var fixture = new Fixture(store =>
            {
                store
                    .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => new RestoreSubscriptionResult(
                        true,
                        [
                            CreateStored(
                                42,
                                CreateStoredDataItem(1002, 42, queueSize: 10),
                                CreateStoredDataItem(1004, 42, queueSize: 10)),
                            CreateStored(43, CreateStoredDataItem(1003, 43, queueSize: 10))
                        ]));
                store
                    .Setup(value => value.RestoreDataChangeMonitoredItemQueueAsync(
                        It.IsAny<uint>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((uint monitoredItemId, CancellationToken _) =>
                        new ValueTask<IDataChangeMonitoredItemQueue?>(queues[monitoredItemId]));
            });

            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            AssertBothRestored(fixture, 42u, 43u);
            Assert.Multiple(() =>
            {
                foreach (StagedQueue queue in queues.Values)
                {
                    Assert.That(queue.Began, Is.True, $"queue {queue.MonitoredItemId} is staged");
                    Assert.That(queue.CommitCalls, Is.EqualTo(1), $"queue {queue.MonitoredItemId} is committed");
                    Assert.That(queue.Aborted, Is.False, $"queue {queue.MonitoredItemId} is not aborted");
                }
            });
            Assert.That(
                fixture.Logs.Entries,
                Has.Some.Matches<(LogLevel Level, string Message)>(entry =>
                    entry.Level == LogLevel.Error &&
                    entry.Message.Contains("monitored item with id 1002", StringComparison.Ordinal)));
        }

        [Test]
        public async Task SubscriptionRestoredWithoutTheManagerWritesItsQueuesAtOnceAsync()
        {
            var queues = new PersistentQueues();
            queues.SeedDataChangeQueue(1002);
            using var fixture = new Fixture(store => queues.Serve(store));

            // Restoring the unowned item queues Bad_NodeIdUnknown into its queue, which a
            // queue that is not staged writes to its storage at once.
            using Subscription subscription = await Subscription.RestoreAsync(
                fixture.Server,
                CreateStored(42, CreateStoredDataItem(1002, 42, queueSize: 10)))
                .ConfigureAwait(false);

            Variant[] stored = queues.ReadStoredDataChangeValues(1002);
            Assert.That(stored, Has.Length.EqualTo(3), "the restored queue writes its changes");
            Assert.That(stored.Take(2), Is.EqualTo(new[] { new Variant(1), new Variant(2) }));

            await subscription.DeleteAsync(null!).ConfigureAwait(false);
            Assert.That(
                queues.IsDataChangeQueueStored(1002),
                Is.False,
                "deleting the subscription removes the queue it owns");
        }

        private static void AssertMonitoredItems(Fixture fixture, uint subscriptionId, params uint[] monitoredItemIds)
        {
            Assert.That(fixture.Manager.TryGetSubscription(subscriptionId, out ISubscription? subscription), Is.True);
            subscription!.GetMonitoredItems(out ArrayOf<uint> ids, out _);
            Assert.That(ids.ToArray(), Is.EquivalentTo(monitoredItemIds));
        }

        private static StoredMonitoredItem CreateStoredDataItem(uint id, uint subscriptionId, uint queueSize)
        {
            // No node manager owns the node, so the item is restored detached.
            return new StoredMonitoredItem
            {
                Id = id,
                SubscriptionId = subscriptionId,
                TypeMask = MonitoredItemTypeMask.DataChange,
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = new NodeId(id),
                AttributeId = Attributes.Value,
                QueueSize = queueSize,
                DiscardOldest = true,
                SamplingInterval = 1000,
                TimestampsToReturn = TimestampsToReturn.Both,
                DiagnosticsMasks = DiagnosticsMasks.None,
                FilterToUse = new MonitoringFilter(),
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null,
                LastValue = new DataValue(new Variant(0))
            };
        }

        private static StoredMonitoredItem CreateStoredOwnedDataItem(uint id, uint subscriptionId)
        {
            StoredMonitoredItem item = CreateStoredDataItem(id, subscriptionId, queueSize: 10);
            item.NodeId = CreationFailingVariable.NodeId;
            return item;
        }

        private static StoredMonitoredItem CreateStoredEventItem(uint id, uint subscriptionId)
        {
            var filter = new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        AttributeId = Attributes.Value,
                        BrowsePath = [new QualifiedName(BrowseNames.EventId)]
                    }
                ]
            };
            return new StoredMonitoredItem
            {
                Id = id,
                SubscriptionId = subscriptionId,
                TypeMask = MonitoredItemTypeMask.Events,
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = EventSourceNodeManager.SourceId,
                AttributeId = Attributes.EventNotifier,
                QueueSize = 10,
                DiscardOldest = true,
                TimestampsToReturn = TimestampsToReturn.Both,
                DiagnosticsMasks = DiagnosticsMasks.None,
                OriginalFilter = filter,
                FilterToUse = filter,
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null
            };
        }

        private static void AssertNothingRestored(Fixture fixture)
        {
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Manager.GetSubscriptions(), Is.Empty);
                Assert.That(fixture.Manager.CaptureAbandonedPublishTimerSnapshot(), Is.Empty);
                Assert.That(fixture.ServerDiagnostics.CurrentSubscriptionCount, Is.Zero);
                Assert.That(fixture.ServerDiagnostics.CumulatedSubscriptionCount, Is.Zero);
            });
            fixture.Store.Verify(
                value => value.OnSubscriptionRestoreCompleteAsync(
                    It.IsAny<Dictionary<uint, ArrayOf<uint>>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            fixture.Store.Verify(
                value => value.StoreSubscriptionsAsync(
                    It.IsAny<IEnumerable<IStoredSubscription>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static void AssertBothRestored(Fixture fixture, uint first, uint second)
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    fixture.Manager.GetSubscriptions().Select(subscription => subscription.Id),
                    Is.EquivalentTo(new[] { first, second }));
                Assert.That(fixture.Manager.CaptureAbandonedPublishTimerSnapshot(), Has.Count.EqualTo(2));
                Assert.That(fixture.ServerDiagnostics.CurrentSubscriptionCount, Is.EqualTo(2u));
                Assert.That(fixture.ServerDiagnostics.CumulatedSubscriptionCount, Is.EqualTo(2u));
            });
            fixture.Store.Verify(
                value => value.OnSubscriptionRestoreCompleteAsync(
                    It.Is<Dictionary<uint, ArrayOf<uint>>>(created =>
                        created.Count == 2 && created.ContainsKey(first) && created.ContainsKey(second)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            fixture.Store.Verify(
                value => value.StoreSubscriptionsAsync(
                    It.IsAny<IEnumerable<IStoredSubscription>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        internal static StoredSubscription CreateStored(uint id, params StoredMonitoredItem[] monitoredItems)
        {
            return new StoredSubscription
            {
                Id = id,
                IsDurable = true,
                PublishingInterval = 1_000,
                MaxKeepaliveCount = 1,
                MaxLifetimeCount = 3,
                MaxMessageCount = 10,
                SequenceNumber = 1,
                UserIdentityToken = new AnonymousIdentityToken(),
                SentMessages = [],
                MonitoredItems = monitoredItems
            };
        }

        private sealed class Fixture : IDisposable
        {
            public Fixture(
                Action<Mock<ISubscriptionStore>> setupStore,
                uint failingSubscriptionId = 0,
                IAsyncNodeManager? nodeManager = null,
                Func<IServerInternal, IAsyncNodeManager>? createNodeManager = null)
            {
                var clock = new FakeTimeProvider();
                Mock<IServerInternal> server = DeterministicServerMock.Create(
                    out m_queueFactory, clock);
                server.SetupGet(value => value.IsRunning).Returns(false);
                Server = server.Object;
                Logs = new RecordingLogger();
                var loggerFactory = new Mock<ILoggerFactory>();
                loggerFactory.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(Logs);
                var telemetry = new Mock<ITelemetryContext>();
                telemetry.SetupGet(value => value.LoggerFactory).Returns(loggerFactory.Object);
                server.SetupGet(value => value.Telemetry).Returns(telemetry.Object);
                Diagnostics = new Mock<IDiagnosticsNodeManager>();
                Diagnostics
                    .Setup(value => value.CreateSubscriptionDiagnosticsAsync(
                        It.IsAny<ServerSystemContext>(),
                        It.IsAny<SubscriptionDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((
                        ServerSystemContext _,
                        SubscriptionDiagnosticsDataType diagnostics,
                        NodeValueSimpleEventHandler _,
                        CancellationToken _) =>
                        new ValueTask<NodeId>(new NodeId(diagnostics.SubscriptionId)));
                server.SetupGet(value => value.DiagnosticsNodeManager).Returns(Diagnostics.Object);
                ServerDiagnostics = new ServerDiagnosticsSummaryDataType();
                server
                    .Setup(value => value.UpdateServerDiagnostics(
                        It.IsAny<Action<ServerDiagnosticsSummaryDataType>>()))
                    .Callback((Action<ServerDiagnosticsSummaryDataType> update) => update(ServerDiagnostics));
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(Mock.Of<IConfigurationNodeManager>());
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(Mock.Of<ICoreNodeManager>());
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                Store = new Mock<ISubscriptionStore>();
                setupStore(Store);
                server.SetupGet(value => value.SubscriptionStore).Returns(Store.Object);
                var configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        DurableSubscriptionsEnabled = true,
                        MaxSubscriptionCount = 10,
                        MinSubscriptionLifetime = 0,
                        MaxDurableSubscriptionLifetimeInHours = 24
                    }
                };
                m_master = new MasterNodeManager(
                    server.Object,
                    configuration,
                    null,
                    (nodeManager ?? createNodeManager?.Invoke(server.Object)) is IAsyncNodeManager owner
                        ? [owner]
                        : [],
                    null);
                server.SetupGet(value => value.NodeManager).Returns(m_master);
                Events = new EventManager(server.Object, 100, 100);
                server.SetupGet(value => value.EventManager).Returns(Events);
                Manager = new FailingSubscriptionManager(
                    server.Object,
                    configuration,
                    clock,
                    failingSubscriptionId);
            }

            public IServerInternal Server { get; }

            public Mock<ISubscriptionStore> Store { get; }

            public Mock<IDiagnosticsNodeManager> Diagnostics { get; }

            public ServerDiagnosticsSummaryDataType ServerDiagnostics { get; }

            public EventManager Events { get; }

            public FailingSubscriptionManager Manager { get; }

            public RecordingLogger Logs { get; }

            public void Dispose()
            {
                Manager.Dispose();
                Events.Dispose();
                m_master.Dispose();
                m_queueFactory.Dispose();
            }

            private readonly MasterNodeManager m_master;
            private readonly MonitoredItemQueueFactory m_queueFactory;
        }

        private sealed class FailingSubscriptionManager : SubscriptionManager
        {
            public FailingSubscriptionManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                TimeProvider timeProvider,
                uint failingSubscriptionId)
                : base(server, configuration, timeProvider)
            {
                FailingSubscriptionId = failingSubscriptionId;
            }

            public uint FailingSubscriptionId { get; set; }

            protected override ValueTask<ISubscription> RestoreSubscriptionAsync(
                IStoredSubscription storedSubscription,
                CancellationToken cancellationToken = default)
            {
                if (storedSubscription.Id == FailingSubscriptionId)
                {
                    throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                }

                return base.RestoreSubscriptionAsync(storedSubscription, cancellationToken);
            }
        }

        /// <summary>
        /// Node manager that owns an event source and can fail the subscription to
        /// it or the restore of the items it owns.
        /// </summary>
        private sealed class EventSourceNodeManager
        {
            public static NodeId SourceId { get; } = new NodeId("RestoreSource", 1);

            public EventSourceNodeManager()
            {
                var source = new BaseObjectState(null)
                {
                    NodeId = SourceId,
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                m_mock = new Mock<IAsyncNodeManager>();
                m_mock.SetupGet(value => value.NamespaceUris).Returns([DeterministicServerMock.TestNamespaceUri]);
                m_mock.Setup(value => value.GetManagerHandleAsync(SourceId, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<object?>(source));
                m_mock
                    .Setup(value => value.SubscribeToEventsAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<object>(),
                        It.IsAny<uint>(),
                        It.IsAny<IEventMonitoredItem>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((
                        OperationContext _,
                        object _,
                        uint _,
                        IEventMonitoredItem monitoredItem,
                        bool unsubscribe,
                        CancellationToken _) =>
                        !unsubscribe && monitoredItem.Id == FailSubscribeOf
                            ? throw SubscribeFailure()
                            : new ValueTask<ServiceResult>(
                                !unsubscribe && monitoredItem.Id == RejectSubscribeOf
                                    ? new ServiceResult(StatusCodes.BadNotSupported)
                                    : ServiceResult.Good));
                m_mock
                    .Setup(value => value.RestoreMonitoredItemsAsync(
                        It.IsAny<IList<IStoredMonitoredItem>>(),
                        It.IsAny<IList<IMonitoredItem>>(),
                        It.IsAny<IUserIdentity>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((
                        IList<IStoredMonitoredItem> itemsToRestore,
                        IList<IMonitoredItem> _,
                        IUserIdentity _,
                        CancellationToken _) =>
                        itemsToRestore.Any(item => item.Id == FailRestoreOf)
                            ? throw RestoreFailure()
                            : default);
            }

            public IAsyncNodeManager Object => m_mock.Object;

            public uint FailSubscribeOf { get; set; }

            public uint RejectSubscribeOf { get; set; }

            public Func<Exception> SubscribeFailure { get; set; } =
                () => new InvalidOperationException("subscribe failed");

            public uint FailRestoreOf { get; set; }

            public Func<Exception> RestoreFailure { get; set; } =
                () => new InvalidOperationException("restore failed");

            private readonly Mock<IAsyncNodeManager> m_mock;
        }

        /// <summary>
        /// The variable owned by the node managers that can fail the creation of the
        /// monitored items they restore for it.
        /// </summary>
        private static class CreationFailingVariable
        {
            public static NodeId NodeId { get; } = new NodeId("RestoreVariable", 1);

            public static BaseDataVariableState Create()
            {
                return new BaseDataVariableState(null)
                {
                    NodeId = NodeId,
                    BrowseName = new QualifiedName("RestoreVariable", 1),
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Value = 1
                };
            }

            public static ApplicationConfiguration Configuration => new()
            {
                ServerConfiguration = new ServerConfiguration { MaxNotificationQueueSize = 100 }
            };
        }

        private interface ICreationFailingNodeManager
        {
            uint FailCreationOf { get; set; }
        }

        private sealed class AsyncCreationFailingNodeManager : AsyncCustomNodeManager, ICreationFailingNodeManager
        {
            public AsyncCreationFailingNodeManager(IServerInternal server)
                : base(
                    server,
                    CreationFailingVariable.Configuration,
                    NullLogger.Instance,
                    DeterministicServerMock.TestNamespaceUri)
            {
                AddPredefinedNodeSynchronously(CreationFailingVariable.Create());
            }

            public uint FailCreationOf { get; set; }

            protected override void OnMonitoredItemCreated(
                ServerSystemContext context,
                NodeHandle handle,
                ISampledDataChangeMonitoredItem monitoredItem)
            {
                if (monitoredItem.Id == FailCreationOf)
                {
                    throw new InvalidOperationException("creation failed");
                }
                base.OnMonitoredItemCreated(context, handle, monitoredItem);
            }
        }

        private sealed class SyncCreationFailingNodeManager : CustomNodeManager2, ICreationFailingNodeManager
        {
            public SyncCreationFailingNodeManager(IServerInternal server)
                : base(
                    server,
                    CreationFailingVariable.Configuration,
                    NullLogger.Instance,
                    DeterministicServerMock.TestNamespaceUri)
            {
                AddPredefinedNode(SystemContext, CreationFailingVariable.Create());
            }

            public uint FailCreationOf { get; set; }

            protected override void OnMonitoredItemCreated(
                ServerSystemContext context,
                NodeHandle handle,
                ISampledDataChangeMonitoredItem monitoredItem)
            {
                if (monitoredItem.Id == FailCreationOf)
                {
                    throw new InvalidOperationException("creation failed");
                }
                base.OnMonitoredItemCreated(context, handle, monitoredItem);
            }
        }

        /// <summary>
        /// A persistent queue test double for the subscription restore contract.
        /// </summary>
        private sealed class PersistentQueues
        {
            /// <summary>
            /// The values of every data change queue the restore read from storage, by
            /// monitored item, in the order the reads happened.
            /// </summary>
            public List<(uint MonitoredItemId, Variant[] Values)> DataChangeReads { get; } = [];

            public void SeedDataChangeQueue(uint monitoredItemId, uint queueSize = 10)
            {
                m_data[monitoredItemId] = (queueSize, [new DataValue(1), new DataValue(2)]);
            }

            public void SeedEventQueue(uint monitoredItemId)
            {
                m_events[monitoredItemId] = s_seededEventHandles;
            }

            public void Serve(Mock<ISubscriptionStore> store)
            {
                store
                    .Setup(value => value.RestoreDataChangeMonitoredItemQueueAsync(
                        It.IsAny<uint>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync((uint monitoredItemId, CancellationToken _) => ReadDataChangeQueue(monitoredItemId));
                store
                    .Setup(value => value.RestoreEventMonitoredItemQueueAsync(
                        It.IsAny<uint>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync((uint monitoredItemId, CancellationToken _) => ReadEventQueue(monitoredItemId));
            }

            /// <summary>
            /// Returns the values stored for the data change queue, oldest first.
            /// </summary>
            public Variant[] ReadStoredDataChangeValues(uint monitoredItemId)
            {
                return [.. m_data[monitoredItemId].Values.Select(value => value.WrappedValue)];
            }

            public bool IsDataChangeQueueStored(uint monitoredItemId)
            {
                return m_data.ContainsKey(monitoredItemId);
            }

            public void AssertDataChangeQueueKept(uint monitoredItemId)
            {
                Assert.That(
                    ReadStoredDataChangeValues(monitoredItemId),
                    Is.EqualTo(new[] { new Variant(1), new Variant(2) }),
                    "the aborted restore leaves the stored values as they were");
            }

            public void AssertEventQueueKept(uint monitoredItemId)
            {
                Assert.That(m_events.TryGetValue(monitoredItemId, out uint[]? handles), Is.True,
                    "the persisted event queue survives the aborted restore");
                Assert.That(handles, Is.EqualTo(s_seededEventHandles),
                    "the aborted restore leaves the stored event intact");
            }

            private PersistentDataQueue? ReadDataChangeQueue(uint monitoredItemId)
            {
                if (!m_data.TryGetValue(monitoredItemId, out var saved))
                {
                    return null;
                }
                DataChangeReads.Add((monitoredItemId, ReadStoredDataChangeValues(monitoredItemId)));
                return new PersistentDataQueue(this, monitoredItemId, saved.Size, saved.Values);
            }

            private PersistentEventQueue? ReadEventQueue(uint monitoredItemId)
            {
                return m_events.TryGetValue(monitoredItemId, out uint[]? handles)
                    ? new PersistentEventQueue(this, monitoredItemId, handles)
                    : null;
            }

            private static readonly uint[] s_seededEventHandles = [7u];
            private readonly Dictionary<uint, (uint Size, DataValue[] Values)> m_data = [];
            private readonly Dictionary<uint, uint[]> m_events = [];
            private readonly ITelemetryContext m_telemetry = NUnitTelemetryContext.Create();

            private sealed class PersistentDataQueue : DataChangeMonitoredItemQueue, IRestorableMonitoredItemQueue
            {
                public PersistentDataQueue(PersistentQueues owner, uint id, uint size, DataValue[] values)
                    : base(false, id, owner.m_telemetry)
                {
                    m_owner = owner;
                    base.ResetQueue(size, false);
                    foreach (DataValue value in values)
                    {
                        base.Enqueue(value, ServiceResult.Good);
                    }
                }

                public void BeginRestore() => m_suspendWrites = true;

                public void AbortRestore() => m_suspendWrites = true;

                public void CommitRestore()
                {
                    m_suspendWrites = false;
                    Persist();
                }

                public override void Enqueue(in DataValue value, ServiceResult error)
                {
                    base.Enqueue(value, error);
                    Persist();
                }

                public override void OverwriteLastValue(in DataValue value, ServiceResult error)
                {
                    base.OverwriteLastValue(value, error);
                    Persist();
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing && !m_suspendWrites)
                    {
                        m_owner.m_data.Remove(MonitoredItemId);
                    }
                    base.Dispose(disposing);
                }

                private void Persist()
                {
                    if (m_suspendWrites)
                    {
                        return;
                    }
                    var values = new DataValue[ItemsInQueue];
                    DataValue[] buffer = m_values!;
                    for (int i = 0; i < values.Length; i++)
                    {
                        values[i] = buffer[(m_start + i) % buffer.Length];
                    }
                    m_owner.m_data[MonitoredItemId] = (QueueSize, values);
                }

                private readonly PersistentQueues m_owner;
                private bool m_suspendWrites;
            }

            private sealed class PersistentEventQueue : EventMonitoredItemQueue, IRestorableMonitoredItemQueue
            {
                public PersistentEventQueue(PersistentQueues owner, uint id, uint[] handles)
                    : base(false, id, owner.m_telemetry)
                {
                    m_owner = owner;
                    base.SetQueueSize(10, false);
                    foreach (uint handle in handles)
                    {
                        base.Enqueue(new EventFieldList { ClientHandle = handle });
                    }
                }

                public void BeginRestore() => m_suspendWrites = true;

                public void AbortRestore() => m_suspendWrites = true;

                public void CommitRestore()
                {
                    m_suspendWrites = false;
                    Persist();
                }

                public override void Enqueue(QueuedEvent value)
                {
                    base.Enqueue(value);
                    Persist();
                }

                public override bool Dequeue(out QueuedEvent value)
                {
                    if (!base.Dequeue(out value))
                    {
                        return false;
                    }

                    Persist();
                    return true;
                }

                public override void SetQueueSize(uint queueSize, bool discardOldest)
                {
                    base.SetQueueSize(queueSize, discardOldest);
                    Persist();
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing && !m_suspendWrites)
                    {
                        m_owner.m_events.Remove(MonitoredItemId);
                    }
                    base.Dispose(disposing);
                }

                private void Persist()
                {
                    if (!m_suspendWrites)
                    {
                        m_owner.m_events[MonitoredItemId] = [.. CopyQueuedEvents().Select(value => value.ClientHandle)];
                    }
                }

                private readonly PersistentQueues m_owner;
                private bool m_suspendWrites;
            }
        }

        /// <summary>
        /// An event queue that records whether it was disposed.
        /// </summary>
        private sealed class DisposalRecordingEventQueue : EventMonitoredItemQueue
        {
            public DisposalRecordingEventQueue(uint monitoredItemId, ITelemetryContext telemetry)
                : base(false, monitoredItemId, telemetry)
            {
            }

            public bool Disposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// A restorable queue that records how the restore completes it, and whose
        /// commit can fail.
        /// </summary>
        private sealed class StagedQueue : DataChangeMonitoredItemQueue, IRestorableMonitoredItemQueue
        {
            public StagedQueue(uint monitoredItemId, ITelemetryContext telemetry)
                : base(false, monitoredItemId, telemetry)
            {
                ResetQueue(10, false);
            }

            public bool FailCommit { get; init; }

            public bool Began { get; private set; }

            public int CommitCalls { get; private set; }

            public bool Aborted { get; private set; }

            public void BeginRestore()
            {
                Began = true;
            }

            public void CommitRestore()
            {
                CommitCalls++;
                if (FailCommit)
                {
                    throw new InvalidOperationException("storage unavailable");
                }
            }

            public void AbortRestore()
            {
                Aborted = true;
            }
        }

        private sealed class RecordingLogger : ILogger
        {
            public IReadOnlyList<(LogLevel Level, string Message)> Entries
            {
                get
                {
                    lock (m_lock)
                    {
                        return [.. m_entries];
                    }
                }
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (m_lock)
                {
                    m_entries.Add((logLevel, formatter(state, exception)));
                }
            }

            private readonly Lock m_lock = new();
            private readonly List<(LogLevel Level, string Message)> m_entries = [];
        }
    }
}
