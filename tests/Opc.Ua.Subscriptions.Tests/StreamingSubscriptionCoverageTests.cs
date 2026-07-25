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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// Deterministic offline unit-test coverage for
    /// <see cref="StreamingSubscription"/> and
    /// <see cref="StreamingSubscriptionExtensions"/>. Uses hand-rolled
    /// stub implementations of the subscription interfaces (no Moq) and
    /// drives notifications by invoking the captured notification
    /// handler, so every assertion is data-driven and never relies on
    /// wall-clock timing.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Streaming")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class StreamingSubscriptionCoverageTests
    {
        private static readonly TimeSpan s_safetyTimeout = TimeSpan.FromSeconds(10);

        private static readonly DateTime s_publishTime =
            new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly IReadOnlyList<string> s_emptyStringTable =
            [];

        private static readonly NodeId s_nodeA = new("SensorA", 2);
        private static readonly NodeId s_nodeB = new("SensorB", 2);
        private static readonly NodeId s_notifier = new("Area1", 3);
        private static readonly int[] s_oneTwo = [1, 2];
        private static readonly int[] s_oneTwoThree = [1, 2, 3];

        private const uint GoodStatus = 0x00000000u;
        private const uint UncertainStatus = 0x40920000u;

        [Test]
        public void ConstructorWithNullManagerThrowsArgumentNullException()
        {
            ArgumentNullException? ex = Assert.Throws<ArgumentNullException>(
                () => _ = new StreamingSubscription(null!));
            Assert.That(ex!.ParamName, Is.EqualTo("subscriptionManager"));
        }

        [Test]
        public void SubscribeDataChangesWithNullNodeIdThrowsArgumentNullException()
        {
            var manager = new StubSubscriptionManager();
            var subscription = new StreamingSubscription(manager);

            ArgumentNullException? ex = Assert.Throws<ArgumentNullException>(
                () => _ = subscription.SubscribeDataChangesAsync(NodeId.Null));
            Assert.That(ex!.ParamName, Is.EqualTo("nodeId"));
        }

        [Test]
        public void SubscribeDataChangesWithNullNodeListThrowsArgumentNullException()
        {
            var manager = new StubSubscriptionManager();
            var subscription = new StreamingSubscription(manager);

            ArgumentNullException? ex = Assert.Throws<ArgumentNullException>(
                () => _ = subscription.SubscribeDataChangesAsync(null!));
            Assert.That(ex!.ParamName, Is.EqualTo("nodeIds"));
        }

        [Test]
        public void SubscribeEventsWithNullNotifierThrowsArgumentNullException()
        {
            var manager = new StubSubscriptionManager();
            var subscription = new StreamingSubscription(manager);

            ArgumentNullException? ex = Assert.Throws<ArgumentNullException>(
                () => _ = subscription.SubscribeEventsAsync(NodeId.Null, new EventFilter()));
            Assert.That(ex!.ParamName, Is.EqualTo("notifierId"));
        }

        [Test]
        public void SubscribeEventsWithNullFilterThrowsArgumentNullException()
        {
            var manager = new StubSubscriptionManager();
            var subscription = new StreamingSubscription(manager);

            ArgumentNullException? ex = Assert.Throws<ArgumentNullException>(
                () => _ = subscription.SubscribeEventsAsync(s_notifier, null!));
            Assert.That(ex!.ParamName, Is.EqualTo("filter"));
        }

        [Test]
        public async Task SubscribeCreatesSingleSharedSubscriptionAsync()
        {
            var manager = new StubSubscriptionManager();
            await using var subscription = new StreamingSubscription(manager);

            (IAsyncEnumerator<DataValueChange> firstEnumerator, Task<bool> firstMove) =
                StartDataChanges(subscription, s_nodeA, CancellationToken.None);
            (IAsyncEnumerator<DataValueChange> secondEnumerator, Task<bool> secondMove) =
                StartDataChanges(subscription, s_nodeB, CancellationToken.None);

            Assert.That(manager.AddCallCount, Is.EqualTo(1));
            StubMonitoredItemCollection collection = manager.Subscription!.Collection;
            Assert.That(collection.Count, Is.EqualTo(2u));
            Assert.That(collection.Added[0].ClientHandle,
                Is.Not.EqualTo(collection.Added[1].ClientHandle));

            await subscription.DisposeAsync().ConfigureAwait(false);
            Assert.That(await WithinTimeoutAsync(firstMove).ConfigureAwait(false), Is.False);
            Assert.That(await WithinTimeoutAsync(secondMove).ConfigureAwait(false), Is.False);
            await firstEnumerator.DisposeAsync().ConfigureAwait(false);
            await secondEnumerator.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task DataChangeNotificationFlowsToSubscriberAsync()
        {
            var manager = new StubSubscriptionManager();
            await using var subscription = new StreamingSubscription(manager);

            (IAsyncEnumerator<DataValueChange> enumerator, Task<bool> move) =
                StartDataChanges(subscription, s_nodeA, CancellationToken.None);

            Assert.That(manager.AddCallCount, Is.EqualTo(1));
            StubMonitoredItem item = manager.Subscription!.Collection.Added[0];
            Assert.That(item.Options.StartNodeId, Is.EqualTo(s_nodeA));
            Assert.That(item.Options.AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(item.Name, Is.EqualTo($"stream_data_1_{s_nodeA}"));

            var change = new DataValueChange(item,
                new DataValue(Variant.From(42), new StatusCode(UncertainStatus)), null);
            await FireDataChangeAsync(manager, change).ConfigureAwait(false);

            Assert.That(await WithinTimeoutAsync(move).ConfigureAwait(false), Is.True);
            DataValueChange received = enumerator.Current;
            Assert.That(received.Value.WrappedValue, Is.EqualTo(Variant.From(42)));
            Assert.That(received.Value.StatusCode.Code, Is.EqualTo(UncertainStatus));
            Assert.That(received.MonitoredItem, Is.SameAs(item));

            await enumerator.DisposeAsync().ConfigureAwait(false);
            Assert.That(manager.Subscription!.Collection.RemovedHandles,
                Does.Contain(item.ClientHandle));
        }

        [Test]
        public async Task DataChangeListOverloadAddsAllItemsAsync()
        {
            var manager = new StubSubscriptionManager();
            await using var subscription = new StreamingSubscription(manager);

            var nodes = new List<NodeId> { s_nodeA, s_nodeB };
            IAsyncEnumerator<DataValueChange> enumerator =
                subscription.SubscribeDataChangesAsync(nodes).GetAsyncEnumerator();
            Task<bool> move = enumerator.MoveNextAsync().AsTask();

            Assert.That(manager.AddCallCount, Is.EqualTo(1));
            StubMonitoredItemCollection collection = manager.Subscription!.Collection;
            Assert.That(collection.Count, Is.EqualTo(2u));
            StubMonitoredItem second = collection.Added[1];
            Assert.That(second.Options.StartNodeId, Is.EqualTo(s_nodeB));

            var change = new DataValueChange(second,
                new DataValue(Variant.From(99), new StatusCode(GoodStatus)), null);
            await FireDataChangeAsync(manager, change).ConfigureAwait(false);

            Assert.That(await WithinTimeoutAsync(move).ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Value.WrappedValue, Is.EqualTo(Variant.From(99)));
            Assert.That(enumerator.Current.MonitoredItem, Is.SameAs(second));

            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task EventNotificationFlowsToSubscriberAsync()
        {
            var manager = new StubSubscriptionManager();
            await using var subscription = new StreamingSubscription(manager);

            var filter = new EventFilter();
            IAsyncEnumerator<EventNotification> enumerator =
                subscription.SubscribeEventsAsync(s_notifier, filter).GetAsyncEnumerator();
            Task<bool> move = enumerator.MoveNextAsync().AsTask();

            Assert.That(manager.AddCallCount, Is.EqualTo(1));
            StubMonitoredItem item = manager.Subscription!.Collection.Added[0];
            Assert.That(item.Options.AttributeId, Is.EqualTo(Attributes.EventNotifier));
            Assert.That(item.Options.Filter, Is.SameAs(filter));
            Assert.That(item.Options.QueueSize, Is.EqualTo(10u));
            Assert.That(item.Name, Is.EqualTo($"stream_event_1_{s_notifier}"));

            var notification = new EventNotification(item,
                ArrayOf.Wrapped(Variant.From("AlarmActive"), Variant.From(7)));
            await FireEventAsync(manager, notification).ConfigureAwait(false);

            Assert.That(await WithinTimeoutAsync(move).ConfigureAwait(false), Is.True);
            EventNotification received = enumerator.Current;
            Assert.That(received.Fields.Count, Is.EqualTo(2));
            Assert.That(received.Fields[0], Is.EqualTo(Variant.From("AlarmActive")));
            Assert.That(received.Fields[1], Is.EqualTo(Variant.From(7)));
            Assert.That(received.MonitoredItem, Is.SameAs(item));

            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task EventUsesProvidedQueueSizeAsync()
        {
            var manager = new StubSubscriptionManager();
            await using var subscription = new StreamingSubscription(manager);

            var options = new MonitoredItemOptions { QueueSize = 5 };
            IAsyncEnumerator<EventNotification> enumerator = subscription
                .SubscribeEventsAsync(s_notifier, new EventFilter(), options)
                .GetAsyncEnumerator();
            Task<bool> move = enumerator.MoveNextAsync().AsTask();

            StubMonitoredItem item = manager.Subscription!.Collection.Added[0];
            Assert.That(item.Options.QueueSize, Is.EqualTo(5u));

            await DrainAsync(subscription, enumerator, move).ConfigureAwait(false);
        }

        [Test]
        public async Task ConcurrentSubscribersReceiveOnlyMatchingNotificationsAsync()
        {
            var manager = new StubSubscriptionManager();
            await using var subscription = new StreamingSubscription(manager);

            (IAsyncEnumerator<DataValueChange> enumeratorA, Task<bool> moveA) =
                StartDataChanges(subscription, s_nodeA, CancellationToken.None);
            (IAsyncEnumerator<DataValueChange> enumeratorB, Task<bool> moveB) =
                StartDataChanges(subscription, s_nodeB, CancellationToken.None);

            Assert.That(manager.AddCallCount, Is.EqualTo(1));
            StubMonitoredItem itemA = manager.Subscription!.Collection.Added[0];

            var change = new DataValueChange(itemA,
                new DataValue(Variant.From(11), new StatusCode(GoodStatus)), null);
            await FireDataChangeAsync(manager, change).ConfigureAwait(false);

            Assert.That(await WithinTimeoutAsync(moveA).ConfigureAwait(false), Is.True);
            Assert.That(enumeratorA.Current.Value.WrappedValue, Is.EqualTo(Variant.From(11)));
            Assert.That(moveB.IsCompleted, Is.False);

            await enumeratorA.DisposeAsync().ConfigureAwait(false);
            await subscription.DisposeAsync().ConfigureAwait(false);
            Assert.That(await WithinTimeoutAsync(moveB).ConfigureAwait(false), Is.False);
            await enumeratorB.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task CancellingOneSubscriberRemovesOnlyItsMonitoredItemAsync()
        {
            var manager = new StubSubscriptionManager();
            await using var subscription = new StreamingSubscription(manager);
            using var cancelA = new CancellationTokenSource();

            (IAsyncEnumerator<DataValueChange> enumeratorA, Task<bool> moveA) =
                StartDataChanges(subscription, s_nodeA, cancelA.Token);
            (IAsyncEnumerator<DataValueChange> enumeratorB, Task<bool> moveB) =
                StartDataChanges(subscription, s_nodeB, CancellationToken.None);

            StubMonitoredItemCollection collection = manager.Subscription!.Collection;
            StubMonitoredItem itemA = collection.Added[0];
            StubMonitoredItem itemB = collection.Added[1];

            cancelA.Cancel();
            await AwaitFaultAsync<OperationCanceledException>(moveA).ConfigureAwait(false);

            Assert.That(collection.RemovedHandles, Does.Contain(itemA.ClientHandle));
            Assert.That(collection.RemovedHandles, Does.Not.Contain(itemB.ClientHandle));
            Assert.That(collection.ContainsHandle(itemB.ClientHandle), Is.True);

            await enumeratorA.DisposeAsync().ConfigureAwait(false);
            await subscription.DisposeAsync().ConfigureAwait(false);
            Assert.That(await WithinTimeoutAsync(moveB).ConfigureAwait(false), Is.False);
            await enumeratorB.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task DisposeAsyncDisposesUnderlyingSubscriptionAndIsIdempotentAsync()
        {
            var manager = new StubSubscriptionManager();
            var subscription = new StreamingSubscription(manager);

            (IAsyncEnumerator<DataValueChange> enumerator, Task<bool> move) =
                StartDataChanges(subscription, s_nodeA, CancellationToken.None);
            Assert.That(manager.AddCallCount, Is.EqualTo(1));

            await subscription.DisposeAsync().ConfigureAwait(false);
            Assert.That(manager.Subscription!.DisposeCount, Is.EqualTo(1));

            await subscription.DisposeAsync().ConfigureAwait(false);
            Assert.That(manager.Subscription!.DisposeCount, Is.EqualTo(1));

            Assert.That(await WithinTimeoutAsync(move).ConfigureAwait(false), Is.False);
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task SubscribeAfterDisposeThrowsObjectDisposedExceptionAsync()
        {
            var manager = new StubSubscriptionManager();
            var subscription = new StreamingSubscription(manager);
            await subscription.DisposeAsync().ConfigureAwait(false);

            (IAsyncEnumerator<DataValueChange> enumerator, Task<bool> move) =
                StartDataChanges(subscription, s_nodeA, CancellationToken.None);

            await AwaitFaultAsync<ObjectDisposedException>(move).ConfigureAwait(false);
            Assert.That(manager.AddCallCount, Is.Zero);
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionCreateFailureSurfacesToCallerAsync()
        {
            var manager = new StubSubscriptionManager(failAdd: true);
            await using var subscription = new StreamingSubscription(manager);

            (IAsyncEnumerator<DataValueChange> enumerator, Task<bool> move) =
                StartDataChanges(subscription, s_nodeA, CancellationToken.None);

            InvalidOperationException ex =
                await AwaitFaultAsync<InvalidOperationException>(move).ConfigureAwait(false);
            Assert.That(ex.Message, Is.EqualTo("subscription create failed"));
            Assert.That(manager.AddCallCount, Is.EqualTo(1));
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task FailedMonitoredItemAddYieldsNoItemsAsync()
        {
            var manager = new StubSubscriptionManager(failItemAdd: true);
            await using var subscription = new StreamingSubscription(manager);

            (IAsyncEnumerator<DataValueChange> enumerator, Task<bool> move) =
                StartDataChanges(subscription, s_nodeA, CancellationToken.None);

            Assert.That(manager.AddCallCount, Is.EqualTo(1));
            StubMonitoredItemCollection collection = manager.Subscription!.Collection;
            Assert.That(collection.Count, Is.Zero);
            Assert.That(collection.Added, Is.Empty);

            await DrainAsync(subscription, enumerator, move).ConfigureAwait(false);
        }

        [Test]
        public async Task DataChangeWithNullMonitoredItemIsIgnoredAsync()
        {
            var manager = new StubSubscriptionManager();
            await using var subscription = new StreamingSubscription(manager);

            (IAsyncEnumerator<DataValueChange> enumerator, Task<bool> move) =
                StartDataChanges(subscription, s_nodeA, CancellationToken.None);
            StubMonitoredItem item = manager.Subscription!.Collection.Added[0];

            var ignored = new DataValueChange(null,
                new DataValue(Variant.From(1), new StatusCode(GoodStatus)), null);
            await FireDataChangeAsync(manager, ignored).ConfigureAwait(false);
            Assert.That(move.IsCompleted, Is.False);

            var delivered = new DataValueChange(item,
                new DataValue(Variant.From(55), new StatusCode(GoodStatus)), null);
            await FireDataChangeAsync(manager, delivered).ConfigureAwait(false);

            Assert.That(await WithinTimeoutAsync(move).ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Value.WrappedValue, Is.EqualTo(Variant.From(55)));
            Assert.That(enumerator.Current.MonitoredItem, Is.SameAs(item));

            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task EventWithNullMonitoredItemIsIgnoredAsync()
        {
            var manager = new StubSubscriptionManager();
            await using var subscription = new StreamingSubscription(manager);

            IAsyncEnumerator<EventNotification> enumerator =
                subscription.SubscribeEventsAsync(s_notifier, new EventFilter()).GetAsyncEnumerator();
            Task<bool> move = enumerator.MoveNextAsync().AsTask();
            StubMonitoredItem item = manager.Subscription!.Collection.Added[0];

            var ignored = new EventNotification(null, ArrayOf.Wrapped(Variant.From(1)));
            await FireEventAsync(manager, ignored).ConfigureAwait(false);
            Assert.That(move.IsCompleted, Is.False);

            var delivered = new EventNotification(item, ArrayOf.Wrapped(Variant.From("Ping")));
            await FireEventAsync(manager, delivered).ConfigureAwait(false);

            Assert.That(await WithinTimeoutAsync(move).ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Fields.Count, Is.EqualTo(1));
            Assert.That(enumerator.Current.Fields[0], Is.EqualTo(Variant.From("Ping")));

            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task KeepAliveAndStateChangeNotificationsAreNoOpsAsync()
        {
            var manager = new StubSubscriptionManager();
            await using var subscription = new StreamingSubscription(manager);

            (IAsyncEnumerator<DataValueChange> enumerator, Task<bool> move) =
                StartDataChanges(subscription, s_nodeA, CancellationToken.None);
            Assert.That(manager.AddCallCount, Is.EqualTo(1));

            ValueTask keepAlive = manager.Handler!.OnKeepAliveNotificationAsync(
                manager.Subscription!, 7u, s_publishTime, PublishState.None);
            Assert.That(keepAlive.IsCompleted, Is.True);
            await keepAlive.ConfigureAwait(false);

            ValueTask stateChanged = manager.Handler!.OnSubscriptionStateChangedAsync(
                manager.Subscription!, SubscriptionState.Created, PublishState.None);
            Assert.That(stateChanged.IsCompleted, Is.True);
            await stateChanged.ConfigureAwait(false);

            Assert.That(move.IsCompleted, Is.False);
            await DrainAsync(subscription, enumerator, move).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionOptionsMonitorIsQueriedOnCreateAsync()
        {
            var manager = new StubSubscriptionManager();
            await using var subscription = new StreamingSubscription(manager);

            (IAsyncEnumerator<DataValueChange> enumerator, Task<bool> move) =
                StartDataChanges(subscription, s_nodeA, CancellationToken.None);

            Assert.That(manager.AddCallCount, Is.EqualTo(1));
            Assert.That(manager.CapturedOptions, Is.Not.Null);
            Assert.That(manager.CapturedOnChange, Is.Null);

            await DrainAsync(subscription, enumerator, move).ConfigureAwait(false);
        }

        [Test]
        public async Task DisposeAsyncSwallowsUnderlyingDisposeFailureAsync()
        {
            var manager = new StubSubscriptionManager(failDispose: true);
            var subscription = new StreamingSubscription(manager);

            (IAsyncEnumerator<DataValueChange> enumerator, Task<bool> move) =
                StartDataChanges(subscription, s_nodeA, CancellationToken.None);
            Assert.That(manager.AddCallCount, Is.EqualTo(1));

            await subscription.DisposeAsync().ConfigureAwait(false);
            Assert.That(manager.Subscription!.DisposeCount, Is.EqualTo(1));

            Assert.That(await WithinTimeoutAsync(move).ConfigureAwait(false), Is.False);
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task DataSubscriberFinallySwallowsRemoveFailureAsync()
        {
            var manager = new StubSubscriptionManager(failRemove: true);
            await using var subscription = new StreamingSubscription(manager);
            using var cancel = new CancellationTokenSource();

            (IAsyncEnumerator<DataValueChange> enumerator, Task<bool> move) =
                StartDataChanges(subscription, s_nodeA, cancel.Token);
            Assert.That(manager.AddCallCount, Is.EqualTo(1));
            StubMonitoredItem item = manager.Subscription!.Collection.Added[0];

            cancel.Cancel();
            await AwaitFaultAsync<OperationCanceledException>(move).ConfigureAwait(false);

            Assert.That(manager.Subscription!.Collection.RemoveAttempts,
                Does.Contain(item.ClientHandle));
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task EventSubscriberFinallySwallowsRemoveFailureAsync()
        {
            var manager = new StubSubscriptionManager(failRemove: true);
            await using var subscription = new StreamingSubscription(manager);
            using var cancel = new CancellationTokenSource();

            IAsyncEnumerator<EventNotification> enumerator = subscription
                .SubscribeEventsAsync(s_notifier, new EventFilter(), ct: cancel.Token)
                .GetAsyncEnumerator(cancel.Token);
            Task<bool> move = enumerator.MoveNextAsync().AsTask();
            Assert.That(manager.AddCallCount, Is.EqualTo(1));
            StubMonitoredItem item = manager.Subscription!.Collection.Added[0];

            cancel.Cancel();
            await AwaitFaultAsync<OperationCanceledException>(move).ConfigureAwait(false);

            Assert.That(manager.Subscription!.Collection.RemoveAttempts,
                Does.Contain(item.ClientHandle));
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ConcurrentCreatorsShareSingleSubscriptionViaInitLockAsync()
        {
            using var addEntered = new SemaphoreSlim(0, 1);
            using var itemsAdded = new SemaphoreSlim(0, 2);
            using var releaseAdd = new ManualResetEventSlim(false);
            var manager = new StubSubscriptionManager
            {
                OnAddEntered = () => addEntered.Release(),
                AddGate = () => _ = releaseAdd.Wait(s_safetyTimeout),
                OnItemAdded = () => itemsAdded.Release()
            };
            await using var subscription = new StreamingSubscription(manager);

            // First creator acquires the init lock and blocks inside Add so the
            // shared subscription is still null while a second creator queues.
            IAsyncEnumerator<DataValueChange> firstEnumerator =
                subscription.SubscribeDataChangesAsync(s_nodeA).GetAsyncEnumerator();
            Task<bool> firstMove = Task.Run(() => firstEnumerator.MoveNextAsync().AsTask());
            Assert.That(await addEntered.WaitAsync(s_safetyTimeout).ConfigureAwait(false), Is.True);

            // Second creator passes the pre-lock null check then suspends on the
            // held init lock; releasing the gate forces it through the
            // double-check path where the subscription now already exists.
            IAsyncEnumerator<DataValueChange> secondEnumerator =
                subscription.SubscribeDataChangesAsync(s_nodeB).GetAsyncEnumerator();
            Task<bool> secondMove = secondEnumerator.MoveNextAsync().AsTask();

            releaseAdd.Set();
            Assert.That(await itemsAdded.WaitAsync(s_safetyTimeout).ConfigureAwait(false), Is.True);
            Assert.That(await itemsAdded.WaitAsync(s_safetyTimeout).ConfigureAwait(false), Is.True);

            Assert.That(manager.AddCallCount, Is.EqualTo(1));
            StubMonitoredItemCollection collection = manager.Subscription!.Collection;
            Assert.That(collection.Count, Is.EqualTo(2u));

            await subscription.DisposeAsync().ConfigureAwait(false);
            Assert.That(await WithinTimeoutAsync(firstMove).ConfigureAwait(false), Is.False);
            Assert.That(await WithinTimeoutAsync(secondMove).ConfigureAwait(false), Is.False);
            await firstEnumerator.DisposeAsync().ConfigureAwait(false);
            await secondEnumerator.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task TakeUntilAsyncIsInclusiveOfMatchAsync()
        {
            var collected = new List<int>();
            await foreach (int value in RangeAsync(1, 2, 3, 4).TakeUntilAsync(static v => v == 3))
            {
                collected.Add(value);
            }
            Assert.That(collected, Is.EqualTo(s_oneTwoThree));
        }

        [Test]
        public void TakeUntilAsyncWithNullSourceThrowsArgumentNullException()
        {
            ArgumentNullException? ex = Assert.Throws<ArgumentNullException>(
                () => _ = StreamingSubscriptionExtensions.TakeUntilAsync<int>(
                    null!, static v => true));
            Assert.That(ex!.ParamName, Is.EqualTo("source"));
        }

        [Test]
        public void TakeUntilAsyncWithNullPredicateThrowsArgumentNullException()
        {
            ArgumentNullException? ex = Assert.Throws<ArgumentNullException>(
                () => _ = RangeAsync(1).TakeUntilAsync(null!));
            Assert.That(ex!.ParamName, Is.EqualTo("predicate"));
        }

        [Test]
        public async Task TakeAsyncYieldsExactCountAsync()
        {
            var collected = new List<int>();
            await foreach (int value in RangeAsync(1, 2, 3, 4, 5).TakeAsync(2))
            {
                collected.Add(value);
            }
            Assert.That(collected, Is.EqualTo(s_oneTwo));
        }

        [Test]
        public void TakeAsyncWithNonPositiveCountThrowsArgumentOutOfRangeException()
        {
            ArgumentOutOfRangeException? ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => _ = RangeAsync(1).TakeAsync(0));
            Assert.That(ex!.ParamName, Is.EqualTo("count"));
        }

        [Test]
        public void TakeAsyncWithNullSourceThrowsArgumentNullException()
        {
            ArgumentNullException? ex = Assert.Throws<ArgumentNullException>(
                () => _ = StreamingSubscriptionExtensions.TakeAsync<int>(null!, 1));
            Assert.That(ex!.ParamName, Is.EqualTo("source"));
        }

        [Test]
        public async Task BufferedAsyncReturnsRequestedCountAsync()
        {
            IReadOnlyList<int> buffer = await RangeAsync(1, 2, 3, 4).BufferedAsync(3).ConfigureAwait(false);
            Assert.That(buffer, Is.EqualTo(s_oneTwoThree));
        }

        [Test]
        public async Task BufferedAsyncWithNullSourceThrowsArgumentNullExceptionAsync()
        {
            ArgumentNullException ex = await AwaitFaultAsync<ArgumentNullException>(
                StreamingSubscriptionExtensions.BufferedAsync<int>(null!, 1).AsTask()).ConfigureAwait(false);
            Assert.That(ex.ParamName, Is.EqualTo("source"));
        }

        [Test]
        public async Task BufferedAsyncWithNonPositiveCountThrowsArgumentOutOfRangeExceptionAsync()
        {
            ArgumentOutOfRangeException ex = await AwaitFaultAsync<ArgumentOutOfRangeException>(
                RangeAsync(1).BufferedAsync(0).AsTask()).ConfigureAwait(false);
            Assert.That(ex.ParamName, Is.EqualTo("count"));
        }

        [Test]
        public void WithTimeoutAsyncWithNullSourceThrowsArgumentNullException()
        {
            ArgumentNullException? ex = Assert.Throws<ArgumentNullException>(
                () => _ = StreamingSubscriptionExtensions.WithTimeoutAsync<int>(
                    null!, TimeSpan.FromSeconds(1)));
            Assert.That(ex!.ParamName, Is.EqualTo("source"));
        }

        [Test]
        public async Task WithTimeoutAsyncPassesThroughItemsBeforeTimeoutAsync()
        {
            var collected = new List<int>();
            await foreach (int value in RangeAsync(1, 2, 3)
                .WithTimeoutAsync(TimeSpan.FromSeconds(30)))
            {
                collected.Add(value);
            }
            Assert.That(collected, Is.EqualTo(s_oneTwoThree));
        }

        private static (IAsyncEnumerator<DataValueChange> Enumerator, Task<bool> Move)
            StartDataChanges(StreamingSubscription subscription, NodeId nodeId, CancellationToken ct)
        {
            IAsyncEnumerator<DataValueChange> enumerator =
                subscription.SubscribeDataChangesAsync(nodeId, ct: ct).GetAsyncEnumerator(ct);
            return (enumerator, enumerator.MoveNextAsync().AsTask());
        }

        private static async Task FireDataChangeAsync(
            StubSubscriptionManager manager, DataValueChange change)
        {
            await manager.Handler!.OnDataChangeNotificationAsync(
                manager.Subscription!, 1u, s_publishTime,
                new ReadOnlyMemory<DataValueChange>([change]),
                PublishState.None, s_emptyStringTable).ConfigureAwait(false);
        }

        private static async Task FireEventAsync(
            StubSubscriptionManager manager, EventNotification notification)
        {
            await manager.Handler!.OnEventDataNotificationAsync(
                manager.Subscription!, 1u, s_publishTime,
                new ReadOnlyMemory<EventNotification>([notification]),
                PublishState.None, s_emptyStringTable).ConfigureAwait(false);
        }

        private static async Task DrainAsync<T>(
            StreamingSubscription subscription, IAsyncEnumerator<T> enumerator, Task<bool> move)
        {
            await subscription.DisposeAsync().ConfigureAwait(false);
            Assert.That(await WithinTimeoutAsync(move).ConfigureAwait(false), Is.False);
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task<T> WithinTimeoutAsync<T>(Task<T> task)
        {
            using var cts = new CancellationTokenSource();
            var delay = Task.Delay(s_safetyTimeout, cts.Token);
            Task winner = await Task.WhenAny(task, delay).ConfigureAwait(false);
            Assert.That(winner, Is.SameAs(task),
                "operation did not complete within the safety timeout");
            cts.Cancel();
            return await task.ConfigureAwait(false);
        }

        private static async Task<TException> AwaitFaultAsync<TException>(Task task)
            where TException : Exception
        {
            using var cts = new CancellationTokenSource();
            var delay = Task.Delay(s_safetyTimeout, cts.Token);
            Task winner = await Task.WhenAny(task, delay).ConfigureAwait(false);
            Assert.That(winner, Is.SameAs(task),
                "operation did not fault within the safety timeout");
            cts.Cancel();

            TException? caught = null;
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (TException ex)
            {
                caught = ex;
            }
            Assert.That(caught, Is.Not.Null, "expected exception was not thrown");
            return caught!;
        }

        private static async IAsyncEnumerable<int> RangeAsync(params int[] values)
        {
            foreach (int value in values)
            {
                await Task.Yield();
                yield return value;
            }
        }

        private sealed class StubSubscriptionManager : ISubscriptionManager
        {
            private readonly bool m_failAdd;
            private readonly bool m_failItemAdd;
            private readonly bool m_failDispose;
            private readonly bool m_failRemove;
            private StubSubscription? m_subscription;

            public StubSubscriptionManager(
                bool failAdd = false,
                bool failItemAdd = false,
                bool failDispose = false,
                bool failRemove = false)
            {
                m_failAdd = failAdd;
                m_failItemAdd = failItemAdd;
                m_failDispose = failDispose;
                m_failRemove = failRemove;
            }

            public int AddCallCount { get; private set; }

            public ISubscriptionNotificationHandler? Handler { get; private set; }

            public StubSubscription? Subscription => m_subscription;

            public SubscriptionOptions? CapturedOptions { get; private set; }

            public IDisposable? CapturedOnChange { get; private set; }

            public Action? OnAddEntered { get; init; }

            public Action? AddGate { get; init; }

            public Action? OnItemAdded { get; init; }

            public ISubscription Add(
                ISubscriptionNotificationHandler handler,
                IOptionsMonitor<SubscriptionOptions> options)
            {
                AddCallCount++;
                Handler = handler;
                CapturedOptions = options.Get(null);
                CapturedOnChange = options.OnChange(static (_, _) => { });
                if (m_failAdd)
                {
                    throw new InvalidOperationException("subscription create failed");
                }
                OnAddEntered?.Invoke();
                AddGate?.Invoke();
                m_subscription ??= new StubSubscription(
                    m_failItemAdd, m_failDispose, m_failRemove, OnItemAdded);
                return m_subscription;
            }

            public DiagnosticsMasks ReturnDiagnostics
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public int MaxPublishWorkerCount
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public int MinPublishWorkerCount
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public bool PoolNotifications
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public int PublishWorkerCount => throw new NotSupportedException();

            public int GoodPublishRequestCount => throw new NotSupportedException();

            public int BadPublishRequestCount => throw new NotSupportedException();

            public long MissingMessageCount => throw new NotSupportedException();

            public long RepublishMessageCount => throw new NotSupportedException();

            public int Count => throw new NotSupportedException();

            public IEnumerable<ISubscription> Items => throw new NotSupportedException();

            public ValueTask SaveAsync(
                Stream stream,
                IServiceMessageContext messageContext,
                IEnumerable<ISubscription>? subscriptions = null,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask<IReadOnlyList<ISubscription>> LoadAsync(
                Stream stream,
                IServiceMessageContext messageContext,
                Func<string, ISubscriptionNotificationHandler> handlerFactory,
                bool transferSubscriptions = false,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class StubSubscription : ISubscription
        {
            private readonly bool m_failDispose;
            private int m_disposeCount;

            public StubSubscription(
                bool failItemAdd, bool failDispose, bool failRemove, Action? onItemAdded)
            {
                m_failDispose = failDispose;
                Collection = new StubMonitoredItemCollection(failItemAdd, failRemove, onItemAdded);
            }

            public StubMonitoredItemCollection Collection { get; }

            public IMonitoredItemCollection MonitoredItems => Collection;

            public int DisposeCount => m_disposeCount;

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref m_disposeCount);
                if (m_failDispose)
                {
                    throw new InvalidOperationException("dispose failed");
                }
                return default;
            }

            public bool Created => throw new NotSupportedException();

            public TimeSpan CurrentPublishingInterval => throw new NotSupportedException();

            public byte CurrentPriority => throw new NotSupportedException();

            public uint CurrentLifetimeCount => throw new NotSupportedException();

            public uint CurrentKeepAliveCount => throw new NotSupportedException();

            public bool CurrentPublishingEnabled => throw new NotSupportedException();

            public uint CurrentMaxNotificationsPerPublish => throw new NotSupportedException();

            public long MissingMessageCount => throw new NotSupportedException();

            public long RepublishMessageCount => throw new NotSupportedException();

            public ValueTask ConditionRefreshAsync(CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask RecreateAsync(CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask<TimeSpan> SetAsDurableAsync(
                TimeSpan lifetime, CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask<SetTriggeringResult> SetTriggeringAsync(
                IMonitoredItem triggeringItem,
                IReadOnlyCollection<IMonitoredItem>? linksToAdd = null,
                IReadOnlyCollection<IMonitoredItem>? linksToRemove = null,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class StubMonitoredItemCollection : IMonitoredItemCollection
        {
            private readonly Lock m_lock = new();
            private readonly Dictionary<uint, StubMonitoredItem> m_items = [];
            private readonly List<StubMonitoredItem> m_added = [];
            private readonly List<uint> m_removed = [];
            private readonly List<uint> m_removeAttempts = [];
            private readonly bool m_failItemAdd;
            private readonly bool m_failRemove;
            private readonly Action? m_onItemAdded;
            private uint m_nextHandle = 1000;

            public StubMonitoredItemCollection(
                bool failItemAdd, bool failRemove, Action? onItemAdded)
            {
                m_failItemAdd = failItemAdd;
                m_failRemove = failRemove;
                m_onItemAdded = onItemAdded;
            }

            public uint Count
            {
                get
                {
                    lock (m_lock)
                    {
                        return (uint)m_items.Count;
                    }
                }
            }

            public IEnumerable<IMonitoredItem> Items
            {
                get
                {
                    lock (m_lock)
                    {
                        return [.. m_items.Values];
                    }
                }
            }

            public StubMonitoredItem[] Added
            {
                get
                {
                    lock (m_lock)
                    {
                        return [.. m_added];
                    }
                }
            }

            public IReadOnlyList<uint> RemovedHandles
            {
                get
                {
                    lock (m_lock)
                    {
                        return [.. m_removed];
                    }
                }
            }

            public IReadOnlyList<uint> RemoveAttempts
            {
                get
                {
                    lock (m_lock)
                    {
                        return [.. m_removeAttempts];
                    }
                }
            }

            public bool ContainsHandle(uint clientHandle)
            {
                lock (m_lock)
                {
                    return m_items.ContainsKey(clientHandle);
                }
            }

            public bool TryAdd(
                string name,
                IOptionsMonitor<MonitoredItemOptions> options,
                out IMonitoredItem? monitoredItem)
            {
                if (m_failItemAdd)
                {
                    monitoredItem = null;
                    return false;
                }
                lock (m_lock)
                {
                    var item = new StubMonitoredItem(m_nextHandle++, name, options.CurrentValue);
                    m_items[item.ClientHandle] = item;
                    m_added.Add(item);
                    monitoredItem = item;
                }
                m_onItemAdded?.Invoke();
                return true;
            }

            public bool TryRemove(uint clientHandle)
            {
                lock (m_lock)
                {
                    m_removeAttempts.Add(clientHandle);
                    if (m_failRemove)
                    {
                        throw new InvalidOperationException("remove failed");
                    }
                    m_removed.Add(clientHandle);
                    return m_items.Remove(clientHandle);
                }
            }

            public bool TryGetMonitoredItemByClientHandle(
                uint clientHandle, out IMonitoredItem? monitoredItem)
            {
                throw new NotSupportedException();
            }

            public bool TryGetMonitoredItemByName(string name, out IMonitoredItem? monitoredItem)
            {
                throw new NotSupportedException();
            }

            public IReadOnlyList<IMonitoredItem> Update(
                IReadOnlyList<(string Name,
                    IOptionsMonitor<MonitoredItemOptions> Options)> state)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class StubMonitoredItem : IMonitoredItem
        {
            public StubMonitoredItem(uint clientHandle, string name, MonitoredItemOptions options)
            {
                ClientHandle = clientHandle;
                Name = name;
                Options = options;
            }

            public uint ClientHandle { get; }

            public string Name { get; }

            public MonitoredItemOptions Options { get; }

            public uint Order => throw new NotSupportedException();

            public uint ServerId => throw new NotSupportedException();

            public bool Created => throw new NotSupportedException();

            public ServiceResult Error => throw new NotSupportedException();

            public MonitoringFilterResult? FilterResult => throw new NotSupportedException();

            public MonitoringMode CurrentMonitoringMode => throw new NotSupportedException();

            public TimeSpan CurrentSamplingInterval => throw new NotSupportedException();

            public uint CurrentQueueSize => throw new NotSupportedException();

            public IEnumerable<IMonitoredItem> TriggeringItems => throw new NotSupportedException();

            public IEnumerable<IMonitoredItem> TriggeredItems => throw new NotSupportedException();

            public ValueTask ConditionRefreshAsync(CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }
        }
    }
}
