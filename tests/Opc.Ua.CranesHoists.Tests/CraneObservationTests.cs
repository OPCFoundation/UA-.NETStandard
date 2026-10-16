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
using ISession = Opc.Ua.Client.ISession;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.CranesHoists.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Tests exact observation bounds, callback errors and exclusively owned subscription cleanup.
    /// </summary>
    [TestFixture]
    public sealed class CraneObservationTests
    {
        /// <summary>
        /// Notification bounds preserve the retained value and report same-batch drops and server overflow.
        /// </summary>
        [Test]
        public async Task DataObservationRetainsQualityAndCountsDroppedNotifications()
        {
            var harness = new Harness();
            NodeId node = new(42, 1);
            Task<CraneObservation> operation = harness.Client.ObserveValuesAsync([node],
                new CraneObservationOptions { MaximumNotifications = 1 }).AsTask();
            var first = new DataValue(Variant.From(12.5), StatusCodes.Uncertain, CraneRuntimeTestData.SourceTime);
            var overflow = new DataValue(Variant.From(13.5), StatusCodes.Good.SetOverflow(true),
                CraneRuntimeTestData.SourceTime);
            await harness.Handler.OnDataChangeNotificationAsync(harness.Subscription.Object,
                1, DateTime.UtcNow,
                new DataValueChange[]
                {
                    new(harness.Item.Object, first, null),
                    new(harness.Item.Object, overflow, null)
                }, PublishState.None, Array.Empty<string>()).ConfigureAwait(false);
            CraneObservation result = await operation.ConfigureAwait(false);
            Assert.That(result.Values, Has.Count.EqualTo(1));
            Assert.That(result.Values[0].Value, Is.EqualTo(first));
            Assert.That(result.Values[0].NodeId, Is.EqualTo(node));
            Assert.That(result.LimitReached, Is.True);
            Assert.That(result.DroppedNotifications, Is.EqualTo(2));
            harness.Subscription.Verify(value => value.DisposeAsync(), Times.Once);
            harness.Session.Verify(value => value.Dispose(), Times.Never);
        }

        /// <summary>
        /// The generated event filter's raw fields and exact ByteString identity survive observation.
        /// </summary>
        [Test]
        public async Task EventObservationPreservesIdentityAndOverflowEvidence()
        {
            var harness = new Harness();
            ByteString eventId = ByteString.From(new byte[] { 1, 2, 3 });
            Task<CraneObservation> operation = harness.Client.ObserveEventsAsync(new NodeId(42, 1),
                new CraneObservationOptions { MaximumNotifications = 1 }).AsTask();
            await harness.EmitEventAsync(Opc.Ua.ObjectTypeIds.EventQueueOverflowEventType, eventId)
                .ConfigureAwait(false);
            CraneObservation result = await operation.ConfigureAwait(false);
            Assert.That(result.Events, Has.Count.EqualTo(1));
            Assert.That(result.Events[0].EventId, Is.EqualTo(eventId));
            Assert.That(result.Events[0].EventType, Is.EqualTo(Opc.Ua.ObjectTypeIds.EventQueueOverflowEventType));
            Assert.That(result.FieldPaths.Count, Is.EqualTo(result.Events[0].Fields.Count));
            Assert.That(result.DroppedNotifications, Is.EqualTo(1));
            harness.Subscription.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Payload overflow fails explicitly for both data and event callbacks and still disposes the subscription.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task OversizedPayloadDoesNotReturnTruncatedSuccess(bool events)
        {
            var harness = new Harness();
            var options = new CraneObservationOptions { MaximumPayloadBytes = 1 };
            Task<CraneObservation> operation = events
                ? harness.Client.ObserveEventsAsync(new NodeId(42, 1), options).AsTask()
                : harness.Client.ObserveValuesAsync([new NodeId(42, 1)], options).AsTask();
            if (events)
            {
                await harness.EmitEventAsync(Opc.Ua.ObjectTypeIds.BaseEventType, ByteString.From(new byte[] { 1 }))
                    .ConfigureAwait(false);
            }
            else
            {
                await harness.Handler.OnDataChangeNotificationAsync(harness.Subscription.Object, 1, DateTime.UtcNow,
                    new DataValueChange[] { new(harness.Item.Object, new DataValue(12), null) },
                    PublishState.None, Array.Empty<string>()).ConfigureAwait(false);
            }
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            harness.Subscription.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Empty observation at normal window expiry is explicit and cleans up owned resources.
        /// </summary>
        [Test]
        public async Task NormalWindowExpiryReturnsABoundedEmptyResult()
        {
            var harness = new Harness();
            Task<CraneObservation> operation = harness.Client.ObserveEventsAsync(new NodeId(42, 1)).AsTask();
            harness.ExpireWindow();
            CraneObservation result = await operation.ConfigureAwait(false);
            Assert.That(result.Values, Is.Empty);
            Assert.That(result.Events, Is.Empty);
            Assert.That(result.LimitReached, Is.False);
            harness.Subscription.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// A setup timeout differs from a successfully established empty observation.
        /// </summary>
        [Test]
        public void SetupTimeoutIsAnErrorAndReleasesTheSubscription()
        {
            var harness = new Harness();
            harness.Item.SetupGet(value => value.Created).Returns(false);
            Task<CraneObservation> operation = harness.Client.ObserveEventsAsync(new NodeId(42, 1)).AsTask();
            harness.ExpireWindow();
            Assert.ThrowsAsync<TimeoutException>(async () => await operation.ConfigureAwait(false));
            harness.Subscription.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Monitored-item creation failure is surfaced rather than mistaken for optional-member absence.
        /// </summary>
        [Test]
        public void ItemErrorPreservesItsOriginalStatus()
        {
            var harness = new Harness();
            harness.Item.SetupGet(value => value.Created).Returns(false);
            harness.Item.SetupGet(value => value.Error).Returns(new ServiceResult(StatusCodes.BadUserAccessDenied));
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Client.ObserveEventsAsync(new NodeId(42, 1)).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.Subscription.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Subscription loss is observable and cannot become empty success.
        /// </summary>
        [Test]
        public async Task SubscriptionFailureIsPropagated()
        {
            var harness = new Harness();
            Task<CraneObservation> operation = harness.Client.ObserveEventsAsync(new NodeId(42, 1)).AsTask();
            await harness.Handler.OnKeepAliveNotificationAsync(
                harness.Subscription.Object, 1, DateTime.UtcNow, PublishState.None).ConfigureAwait(false);
            await harness.Handler.OnSubscriptionStateChangedAsync(harness.Subscription.Object,
                SubscriptionState.Error, PublishState.Stopped).ConfigureAwait(false);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await operation.ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            harness.Subscription.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Caller cancellation retains the caller token and disposes the subscription once.
        /// </summary>
        [Test]
        public void CancellationRetainsTheCallerToken()
        {
            var harness = new Harness();
            using var cancellation = new CancellationTokenSource();
            Task<CraneObservation> operation = harness.Client.ObserveEventsAsync(
                new NodeId(42, 1), cancellationToken: cancellation.Token).AsTask();
            cancellation.Cancel();
            OperationCanceledException error = Assert.ThrowsAsync<OperationCanceledException>(
                async () => await operation.ConfigureAwait(false))!;
            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            harness.Subscription.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Cleanup failures are not hidden by normal observation-window completion.
        /// </summary>
        [Test]
        public void CleanupFailureIsNotReportedAsSuccessfulObservation()
        {
            var harness = new Harness();
            var failure = new InvalidOperationException("Test disposal failure.");
            harness.Subscription.Setup(value => value.DisposeAsync())
                .Returns(new ValueTask(Task.FromException(failure)));
            Task<CraneObservation> operation = harness.Client.ObserveEventsAsync(new NodeId(42, 1)).AsTask();
            harness.ExpireWindow();
            InvalidOperationException actual = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await operation.ConfigureAwait(false))!;
            Assert.That(actual, Is.SameAs(failure));
        }

        /// <summary>
        /// Uses only existing framework/Moq dependencies to control the observation timer and callbacks.
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// Creates one explicitly owned mock subscription.
            /// </summary>
            internal Harness()
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var message = ServiceMessageContext.Create(telemetry);
                Session.SetupGet(value => value.MessageContext).Returns(message);
                Session.SetupGet(value => value.NamespaceUris).Returns(message.NamespaceUris);
                ISubscriptionManager? manager = Manager.Object;
                Session.Setup(value => value.TryGetSubscriptionManager(out manager)).Returns(true);
                Manager.Setup(value => value.Add(It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<SubscriptionOptions>>()))
                    .Callback<ISubscriptionNotificationHandler, IOptionsMonitor<SubscriptionOptions>>(
                        (handler, _) => Handler = handler)
                    .Returns(Subscription.Object);
                IMonitoredItem? item = Item.Object;
                Items.Setup(value => value.TryAdd(It.IsAny<string>(),
                    It.IsAny<IOptionsMonitor<MonitoredItemOptions>>(), out item)).Returns(true);
                Subscription.SetupGet(value => value.MonitoredItems).Returns(Items.Object);
                Item.SetupGet(value => value.Created).Returns(true);
                Item.SetupGet(value => value.Name).Returns("0");
                var timer = new Mock<ITimer>();
                m_clock.Setup(value => value.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>(),
                    It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                    .Callback<TimerCallback, object?, TimeSpan, TimeSpan>(
                        (callback, state, _, _) => m_expire = () => callback(state))
                    .Returns(timer.Object);
                Client = new CranesHoistsClient(Session.Object, telemetry, timeProvider: m_clock.Object);
            }

            /// <summary>
            /// Gets the borrowed session mock.
            /// </summary>
            internal Mock<ISession> Session { get; } = new();

            /// <summary>
            /// Gets the session's subscription manager.
            /// </summary>
            internal Mock<ISubscriptionManager> Manager { get; } = new();

            /// <summary>
            /// Gets the exclusively owned subscription.
            /// </summary>
            internal Mock<ISubscription> Subscription { get; } = new();

            /// <summary>
            /// Gets its item collection.
            /// </summary>
            internal Mock<IMonitoredItemCollection> Items { get; } = new();

            /// <summary>
            /// Gets the created monitored item.
            /// </summary>
            internal Mock<IMonitoredItem> Item { get; } = new();

            /// <summary>
            /// Gets the actual client callback receiver.
            /// </summary>
            internal ISubscriptionNotificationHandler Handler { get; private set; } = null!;

            /// <summary>
            /// Gets the actual typed client.
            /// </summary>
            internal CranesHoistsClient Client { get; }

            /// <summary>
            /// Fires the registered window timer without relying on wall-clock scheduling.
            /// </summary>
            internal void ExpireWindow()
            {
                m_expire!();
            }

            /// <summary>
            /// Emits fields in the generated standard filter's order.
            /// </summary>
            internal ValueTask EmitEventAsync(NodeId eventType, ByteString eventId)
            {
                var fields = new List<Variant>();
                foreach (QualifiedName[] path in EventRecordDecoderRegistry.Default.StandardFields)
                {
                    fields.Add(path.Length == 0 ? Variant.Null : path[^1].Name switch
                    {
                        "EventId" => Variant.From(eventId),
                        "EventType" => Variant.From(eventType),
                        "Message" => Variant.From(new LocalizedText("A simulated event")),
                        _ => Variant.Null
                    });
                }
                return Handler.OnEventDataNotificationAsync(Subscription.Object, 1, DateTime.UtcNow,
                    new EventNotification[] { new(Item.Object, fields.ToArrayOf()) },
                    PublishState.None, Array.Empty<string>());
            }

            private readonly Mock<TimeProvider> m_clock = new();
            private Action? m_expire;
        }
    }
}
