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
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Opc.Ua.Client.Tests.Streaming
{
    /// <summary>
    /// Verifies readiness against the actual streaming implementation without a network timing dependency.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class StreamingSubscriptionReadinessTests
    {
        [Test]
        public async Task ReadyCallbackPrecedesNotificationsWithoutOwningSharedSubscriptionAsync()
        {
            var harness = new Harness();
            harness.Item.SetupGet(item => item.Created).Returns(true);
            await using var streaming = new StreamingSubscription(harness.Manager.Object);
            bool ready = false;
            await using (IAsyncEnumerator<EventNotification> events = streaming.SubscribeEventsWhenReadyAsync(
                ObjectIds.Server, new EventFilter(), async token =>
                {
                    token.ThrowIfCancellationRequested();
                    ready = true;
                    await harness.Handler!.OnEventDataNotificationAsync(harness.Subscription.Object, 1,
                        DateTime.UtcNow, new[] { new EventNotification(harness.Item.Object, [Variant.From(37)]) },
                        PublishState.None, []).ConfigureAwait(false);
                }).GetAsyncEnumerator())
            {
                Assert.That(await events.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(ready, Is.True);
                Assert.That(events.Current.Fields[0].TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(37));
            }
            harness.Items.Verify(items => items.TryRemove(42), Times.Once);
            harness.Subscription.Verify(subscription => subscription.DisposeAsync(), Times.Never);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task NativeCreationFailureNeverReportsReadyOrAnEmptyGoodWindowAsync(bool created, bool retryPending)
        {
            var harness = new Harness();
            harness.Item.As<IMonitoredItemApplyState>().SetupGet(item => item.HasPendingChanges).Returns(retryPending);
            harness.Item.SetupGet(item => item.Created).Returns(created);
            harness.Item.SetupGet(item => item.Error).Returns(new ServiceResult(StatusCodes.BadNodeIdUnknown));
            await using var streaming = new StreamingSubscription(harness.Manager.Object);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            bool ready = false;
            await using IAsyncEnumerator<EventNotification> events = streaming.SubscribeEventsWhenReadyAsync(
                ObjectIds.Server, new EventFilter(), _ =>
                {
                    ready = true;
                    return default;
                }, ct: timeout.Token).GetAsyncEnumerator();
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await events.MoveNextAsync().ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(ready, Is.False);
            harness.Items.Verify(items => items.TryRemove(42), Times.Once);
        }

        [Test]
        public async Task PendingApplyErrorDoesNotSignalReadinessBeforeCreationAsync()
        {
            var harness = new Harness();
            harness.Item.As<IMonitoredItemApplyState>().SetupGet(item => item.HasPendingChanges).Returns(true);
            harness.Item.SetupSequence(item => item.Error)
                .Returns(new ServiceResult(StatusCodes.BadWaitingForInitialData))
                .Returns(ServiceResult.Good);
            harness.Item.SetupSequence(item => item.Created).Returns(false).Returns(true);
            await using var streaming = new StreamingSubscription(harness.Manager.Object);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var observed = new InvalidOperationException("Ready after creation.");
            await using IAsyncEnumerator<EventNotification> events = streaming.SubscribeEventsWhenReadyAsync(
                ObjectIds.Server, new EventFilter(), _ => throw observed, ct: timeout.Token).GetAsyncEnumerator();
            Exception? error = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await events.MoveNextAsync().ConfigureAwait(false));
            Assert.That(error, Is.SameAs(observed));
            harness.Item.VerifyGet(item => item.Created, Times.Exactly(2));
            harness.Items.Verify(items => items.TryRemove(42), Times.Once);
        }

        [Test]
        public async Task CancellationDuringPendingCreationRemovesOnlyTheOwnedItemAsync()
        {
            var harness = new Harness();
            harness.Item.SetupGet(item => item.Created).Returns(false);
            using var cancellation = new CancellationTokenSource();
            harness.Item.SetupGet(item => item.Error).Returns(() =>
            {
                cancellation.Cancel();
                return ServiceResult.Good;
            });
            await using var streaming = new StreamingSubscription(harness.Manager.Object);
            bool ready = false;
            await using IAsyncEnumerator<EventNotification> events = streaming.SubscribeEventsWhenReadyAsync(
                ObjectIds.Server, new EventFilter(), _ =>
                {
                    ready = true;
                    return default;
                }, ct: cancellation.Token).GetAsyncEnumerator();
            Assert.That(async () => await events.MoveNextAsync().ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(ready, Is.False);
            harness.Items.Verify(items => items.TryRemove(42), Times.Once);
            harness.Subscription.Verify(subscription => subscription.DisposeAsync(), Times.Never);
        }

        [Test]
        public async Task CallbackFailurePropagatesAndReleasesTheMonitoredItemAsync()
        {
            var harness = new Harness();
            harness.Item.SetupGet(item => item.Created).Returns(true);
            await using var streaming = new StreamingSubscription(harness.Manager.Object);
            var failure = new InvalidOperationException("Readiness consumer failed.");
            await using IAsyncEnumerator<EventNotification> events = streaming.SubscribeEventsWhenReadyAsync(
                ObjectIds.Server, new EventFilter(), _ => throw failure).GetAsyncEnumerator();
            Exception? error = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await events.MoveNextAsync().ConfigureAwait(false));
            Assert.That(error, Is.SameAs(failure));
            harness.Items.Verify(items => items.TryRemove(42), Times.Once);
        }

        [Test]
        public async Task RefreshFollowsVerifiedCreationAndPreservesTheSharedSubscriptionAsync()
        {
            var harness = new Harness();
            harness.Item.SetupGet(item => item.Created).Returns(true);
            harness.Item.Setup(item => item.ConditionRefreshAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return harness.Handler!.OnEventDataNotificationAsync(harness.Subscription.Object, 1,
                        DateTime.UtcNow, new[] { new EventNotification(harness.Item.Object, [Variant.From(73)]) },
                        PublishState.None, []);
                });
            await using var streaming = new StreamingSubscription(harness.Manager.Object);
            await using (IAsyncEnumerator<EventNotification> events = streaming.SubscribeEventsWithRefreshAsync(
                ObjectIds.Server, new EventFilter()).GetAsyncEnumerator())
            {
                Assert.That(await events.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(events.Current.Fields[0].TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(73));
                harness.Item.Verify(item => item.ConditionRefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            harness.Items.Verify(items => items.TryRemove(42), Times.Once);
            harness.Subscription.Verify(subscription => subscription.DisposeAsync(), Times.Never);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task RefreshNeverRunsAfterNativeCreationFailureAsync(bool created, bool retryPending)
        {
            var harness = new Harness();
            harness.Item.As<IMonitoredItemApplyState>().SetupGet(item => item.HasPendingChanges).Returns(retryPending);
            harness.Item.SetupGet(item => item.Created).Returns(created);
            harness.Item.SetupGet(item => item.Error).Returns(new ServiceResult(StatusCodes.BadNodeIdUnknown));
            await using var streaming = new StreamingSubscription(harness.Manager.Object);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using IAsyncEnumerator<EventNotification> events = streaming.SubscribeEventsWithRefreshAsync(
                ObjectIds.Server, new EventFilter(), ct: timeout.Token).GetAsyncEnumerator();
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await events.MoveNextAsync().ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            harness.Item.Verify(item => item.ConditionRefreshAsync(It.IsAny<CancellationToken>()), Times.Never);
            harness.Items.Verify(items => items.TryRemove(42), Times.Once);
        }

        [Test]
        public async Task RefreshFailurePropagatesAndReleasesOnlyItsMonitoredItemAsync()
        {
            var harness = new Harness();
            harness.Item.SetupGet(item => item.Created).Returns(true);
            var failure = new ServiceResultException(StatusCodes.BadUserAccessDenied);
            harness.Item.Setup(item => item.ConditionRefreshAsync(It.IsAny<CancellationToken>())).Throws(failure);
            await using var streaming = new StreamingSubscription(harness.Manager.Object);
            await using IAsyncEnumerator<EventNotification> events = streaming.SubscribeEventsWithRefreshAsync(
                ObjectIds.Server, new EventFilter()).GetAsyncEnumerator();
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await events.MoveNextAsync().ConfigureAwait(false));
            Assert.That(error, Is.SameAs(failure));
            harness.Item.Verify(item => item.ConditionRefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
            harness.Items.Verify(items => items.TryRemove(42), Times.Once);
            harness.Subscription.Verify(subscription => subscription.DisposeAsync(), Times.Never);
        }

        [Test]
        public void StreamsWithoutReadinessCannotClaimSuccessfulSetup()
        {
            var unsupported = new Mock<IStreamingSubscription>();
            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                unsupported.Object.SubscribeEventsWhenReadyAsync(
                    ObjectIds.Server, new EventFilter(), _ => default))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(() => unsupported.Object.SubscribeEventsWithRefreshAsync(ObjectIds.Server, new EventFilter()),
                Throws.InstanceOf<NotSupportedException>());
            unsupported.VerifyNoOtherCalls();
        }

        [Test]
        public async Task RequiredArgumentsFailBeforeSubscriptionCreationAsync()
        {
            Assert.That(() => StreamingSubscriptionExtensions.SubscribeEventsWhenReadyAsync(
                null!, ObjectIds.Server, new EventFilter(), _ => default), Throws.ArgumentNullException);
            Assert.That(() => StreamingSubscriptionExtensions.SubscribeEventsWithRefreshAsync(
                null!, ObjectIds.Server, new EventFilter()), Throws.ArgumentNullException);
            var harness = new Harness();
            await using var streaming = new StreamingSubscription(harness.Manager.Object);
            Assert.That(() => streaming.SubscribeEventsWhenReadyAsync(
                ObjectIds.Server, new EventFilter(), null!), Throws.ArgumentNullException);
            Assert.That(() => streaming.SubscribeEventsWhenReadyAsync(
                default, new EventFilter(), _ => default), Throws.ArgumentNullException);
            Assert.That(() => streaming.SubscribeEventsWhenReadyAsync(
                ObjectIds.Server, null!, _ => default), Throws.ArgumentNullException);
            Assert.That(() => streaming.SubscribeEventsWithRefreshAsync(
                default, new EventFilter()), Throws.ArgumentNullException);
            Assert.That(() => streaming.SubscribeEventsWithRefreshAsync(
                ObjectIds.Server, null!), Throws.ArgumentNullException);
            harness.Manager.VerifyNoOtherCalls();
        }

        private sealed class Harness
        {
            public Harness()
            {
                Item.As<IMonitoredItemApplyState>().SetupGet(item => item.HasPendingChanges).Returns(false);
                Item.SetupGet(item => item.ClientHandle).Returns(42);
                Item.SetupGet(item => item.Error).Returns(ServiceResult.Good);
                IMonitoredItem? item = Item.Object;
                Items.Setup(items => items.TryAdd(
                    It.IsAny<string>(),
                    It.IsAny<IOptionsMonitor<Subscriptions.MonitoredItems.MonitoredItemOptions>>(),
                    out item)).Returns(true);
                Subscription.SetupGet(subscription => subscription.MonitoredItems).Returns(Items.Object);
                Manager.Setup(manager => manager.Add(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<Subscriptions.SubscriptionOptions>>()))
                    .Callback<ISubscriptionNotificationHandler, IOptionsMonitor<Subscriptions.SubscriptionOptions>>(
                        (handler, _) => Handler = handler)
                    .Returns(Subscription.Object);
            }

            public Mock<IMonitoredItem> Item { get; } = new();
            public Mock<IMonitoredItemCollection> Items { get; } = new();
            public Mock<ISubscription> Subscription { get; } = new();
            public Mock<ISubscriptionManager> Manager { get; } = new();
            public ISubscriptionNotificationHandler? Handler { get; private set; }
        }
    }
}
