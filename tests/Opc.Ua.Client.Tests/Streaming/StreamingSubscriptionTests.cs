/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Subscriptions.Streaming;

// Tests run on the default TaskScheduler so CA2007's sync-context risk does not apply.
#pragma warning disable CA2007

namespace Opc.Ua.Client.Tests.Streaming
{
    /// <summary>
    /// Deterministic null-guard tests for <see cref="StreamingSubscription"/>.
    /// </summary>
    /// <remarks>
    /// Full lazy <c>EnsureSubscriptionAsync</c> / monitored-item routing /
    /// dispose coverage requires a real <c>ISubscriptionManager</c> +
    /// <c>ISubscription</c> pipeline and lives in the integration test
    /// suite. Only the cheap argument-validation guards are exercised
    /// here.
    /// </remarks>
    [TestFixture]
    [Category("Client")]
    [Category("StreamingExtensions")]
    [Parallelizable]
    public sealed class StreamingSubscriptionTests
    {
        [Test]
        public void ConstructorWithNullSubscriptionManagerThrowsArgumentNullException()
        {
            Assert.That(
                () => new StreamingSubscription(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task SubscribeDataChangesAsyncWithNullNodeIdThrowsArgumentNullException()
        {
            await using var sub = new StreamingSubscription(NewStubManager());

            // NodeId.Null is the canonical "null" value of the INullable
            // struct. The production guard checks nodeId.IsNull and throws
            // synchronously before any subscription-manager interaction.
            Assert.That(
                () => sub.SubscribeDataChangesAsync(NodeId.Null),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task SubscribeDataChangesAsyncWithNullNodeIdsListThrowsArgumentNullException()
        {
            await using var sub = new StreamingSubscription(NewStubManager());

            Assert.That(
                () => sub.SubscribeDataChangesAsync(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task SubscribeEventsAsyncWithNullNotifierIdThrowsArgumentNullException()
        {
            await using var sub = new StreamingSubscription(NewStubManager());

            Assert.That(
                () => sub.SubscribeEventsAsync(NodeId.Null, new EventFilter()),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task SubscribeEventsAsyncWithNullFilterThrowsArgumentNullException()
        {
            await using var sub = new StreamingSubscription(NewStubManager());

            Assert.That(
                () => sub.SubscribeEventsAsync(ObjectIds.Server, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task SubscribeEventsAsyncWhenMonitoredItemCannotBeAddedThrowsAsync()
        {
            IMonitoredItem? monitoredItem = null;
            var monitoredItems = new Mock<IMonitoredItemCollection>();
            monitoredItems
                .Setup(items => items.TryAdd(
                    It.IsAny<string>(),
                    It.IsAny<IOptionsMonitor<
                        Subscriptions.MonitoredItems.MonitoredItemOptions>>(),
                    out monitoredItem))
                .Returns(false);
            var subscription = new Mock<ISubscription>();
            subscription.SetupGet(value => value.MonitoredItems).Returns(monitoredItems.Object);
            var manager = new Mock<ISubscriptionManager>();
            IOptionsMonitor<Subscriptions.SubscriptionOptions>? capturedOptions =
                null;
            manager
                .Setup(value => value.Add(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<
                        IOptionsMonitor<Subscriptions.SubscriptionOptions>>()))
                .Callback<
                    ISubscriptionNotificationHandler,
                    IOptionsMonitor<Subscriptions.SubscriptionOptions>>(
                    (_, options) => capturedOptions = options)
                .Returns(subscription.Object);

            await using var streaming = new StreamingSubscription(
                manager.Object,
                new Subscriptions.SubscriptionOptions
                {
                    PublishingInterval = TimeSpan.FromSeconds(2)
                });
            await using IAsyncEnumerator<EventNotification> enumerator = streaming
                .SubscribeEventsAsync(ObjectIds.Server, new EventFilter())
                .GetAsyncEnumerator();

            InvalidOperationException? exception = null;
            try
            {
                await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException caught)
            {
                exception = caught;
            }

            Assert.That(exception, Is.Not.Null);
            Assert.That(
                capturedOptions?.CurrentValue.PublishingEnabled,
                Is.True);
        }

        /// <summary>
        /// DisposeAsync racing the first subscriber (which is still creating
        /// the shared subscription) must dispose the subscription that
        /// subscriber creates and fail the subscriber with
        /// <see cref="ObjectDisposedException"/>, instead of leaking it.
        /// </summary>
        [Test]
        public async Task DisposeRacingFirstSubscribeDisposesCreatedSubscriptionAsync()
        {
            var subscription = new Mock<ISubscription>();
            var manager = new Mock<ISubscriptionManager>();
            StreamingSubscription? streaming = null;
            Task? disposal = null;
            manager
                .Setup(value => value.Add(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<Subscriptions.SubscriptionOptions>>()))
                .Callback(() => disposal = streaming!.DisposeAsync().AsTask())
                .Returns(subscription.Object);

            streaming = new StreamingSubscription(manager.Object);
            await using (streaming)
            {
                await using IAsyncEnumerator<EventNotification> enumerator = streaming
                    .SubscribeEventsAsync(ObjectIds.Server, new EventFilter())
                    .GetAsyncEnumerator();

                Assert.That(
                    async () => await enumerator.MoveNextAsync().ConfigureAwait(false),
                    Throws.InstanceOf<ObjectDisposedException>());
                Assert.That(disposal, Is.Not.Null);
                await disposal!.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                subscription.Verify(value => value.DisposeAsync(), Times.Once);
            }
        }

        /// <summary>
        /// Stub subscription manager — its methods are never invoked
        /// because the argument guards trip synchronously before
        /// <c>EnsureSubscriptionAsync</c> is entered. Using a loose
        /// <see cref="Mock{T}"/> avoids hand-implementing every
        /// member of the <see cref="ISubscriptionManager"/> surface.
        /// </summary>
        private static ISubscriptionManager NewStubManager()
        {
            return new Mock<ISubscriptionManager>(MockBehavior.Loose).Object;
        }
    }
}
