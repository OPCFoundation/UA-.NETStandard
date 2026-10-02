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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for the subscription publishing state machine, triggering,
    /// diagnostics and condition refresh behaviour (OPC 10000-4 §5.13/§5.14,
    /// OPC 10000-5 §12.15, OPC 10000-9 §5.5.7).
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public class SubscriptionStateMachineRegressionTests
    {
        private Mock<IServerInternal> m_serverMock;
        private Mock<ISession> m_sessionMock;
        private Mock<IDiagnosticsNodeManager> m_diagnosticsNodeManagerMock;
        private Mock<IMasterNodeManager> m_nodeManagerMock;
        private Mock<IMonitoredItemQueueFactory> m_queueFactoryMock;
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_serverMock = new Mock<IServerInternal>();
            m_sessionMock = new Mock<ISession>();
            m_diagnosticsNodeManagerMock = new Mock<IDiagnosticsNodeManager>();
            m_nodeManagerMock = new Mock<IMasterNodeManager>();
            m_queueFactoryMock = new Mock<IMonitoredItemQueueFactory>();

            m_serverMock.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_serverMock.Setup(s => s.DiagnosticsNodeManager).Returns(m_diagnosticsNodeManagerMock.Object);
            m_serverMock.Setup(s => s.NodeManager).Returns(m_nodeManagerMock.Object);
            m_serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(m_queueFactoryMock.Object);

            var namespaceUris = new NamespaceTable();
            m_serverMock.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            m_serverMock.Setup(s => s.ServerUris).Returns(new StringTable());
            m_serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            m_serverMock.Setup(s => s.Factory).Returns(new Mock<IEncodeableFactory>().Object);
            m_serverMock.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(m_serverMock.Object));

            var identity = new UserIdentity(new AnonymousIdentityToken());
            m_sessionMock.Setup(s => s.Id).Returns(new NodeId(Guid.NewGuid()));
            m_sessionMock.Setup(s => s.Identity).Returns(identity);
            m_sessionMock.Setup(s => s.IdentityToken).Returns(identity.TokenHandler);
            m_sessionMock.Setup(s => s.ClientApplicationUri)
                .Returns("urn:localhost:opcfoundation.org:SubscriptionStateMachineRegressionTests");

            m_diagnosticsNodeManagerMock
                .Setup(d => d.CreateSubscriptionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(),
                    It.IsAny<SubscriptionDiagnosticsDataType>(),
                    It.IsAny<NodeValueSimpleEventHandler>()))
                .ReturnsAsync(new NodeId(1));
        }

        /// <summary>
        /// M1-1: OPC 10000-4 §5.14.1.4 StartPublishingTimer() advances the lifetime counter
        /// on every expiry, so an idle subscription expires after MaxLifetimeCount cycles
        /// even while it is in the KEEPALIVE state.
        /// </summary>
        [Test]
        public void IdleSubscriptionExpiresAfterMaxLifetimeCountCycles()
        {
            var clock = new FakeTimeProvider();
            using Subscription subscription = CreateSubscription(
                clock,
                maxLifetimeCount: 15,
                maxKeepAliveCount: 5);
            using var context = new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);

            // the first cycle sends the initial keep-alive.
            clock.Advance(TimeSpan.FromMilliseconds(101));
            Assert.That(subscription.PublishTimerExpired(), Is.EqualTo(PublishingState.NotificationsAvailable));
            Assert.That(subscription.Publish(context, out _, out _), Is.Not.Null);

            // the client then stops sending Publish requests.
            for (int cycle = 1; cycle < 15; cycle++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(100));
                Assert.That(subscription.PublishTimerExpired(), Is.Not.EqualTo(PublishingState.Expired),
                    $"cycle {cycle}");
            }

            Assert.That(subscription.Diagnostics.CurrentLifetimeCount, Is.EqualTo(14u));
            clock.Advance(TimeSpan.FromMilliseconds(100));
            Assert.That(subscription.PublishTimerExpired(), Is.EqualTo(PublishingState.Expired));
        }

        /// <summary>
        /// M1-1: a client that keeps answering keep-alives never lets the lifetime expire.
        /// </summary>
        [Test]
        public void IdleSubscriptionServedByKeepAlivesDoesNotExpire()
        {
            var clock = new FakeTimeProvider();
            using Subscription subscription = CreateSubscription(
                clock,
                maxLifetimeCount: 15,
                maxKeepAliveCount: 5);
            using var context = new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);

            for (int cycle = 0; cycle < 100; cycle++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(100));
                PublishingState state = subscription.PublishTimerExpired();
                Assert.That(state, Is.Not.EqualTo(PublishingState.Expired), $"cycle {cycle}");
                if (state == PublishingState.NotificationsAvailable)
                {
                    Assert.That(subscription.Publish(context, out _, out _), Is.Not.Null);
                }
            }
        }

        /// <summary>
        /// M1-6: latePublishRequestCount counts the entries into the LATE state, not the
        /// publishing cycles spent in it (OPC 10000-5 §12.15).
        /// </summary>
        [Test]
        public void LatePublishRequestCountIncrementsOncePerLateEpisode()
        {
            var clock = new FakeTimeProvider();
            using Subscription subscription = CreateSubscription(
                clock,
                maxLifetimeCount: 1000,
                maxKeepAliveCount: 2);
            using var context = new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);

            // initial keep-alive due but not served -> LATE for 20 cycles.
            for (int cycle = 0; cycle < 20; cycle++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(100));
                subscription.PublishTimerExpired();
            }
            Assert.That(subscription.Diagnostics.LatePublishRequestCount, Is.EqualTo(1u));

            // served, then late again.
            Assert.That(subscription.Publish(context, out _, out _), Is.Not.Null);
            for (int cycle = 0; cycle < 20; cycle++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(100));
                subscription.PublishTimerExpired();
            }
            Assert.That(subscription.Diagnostics.LatePublishRequestCount, Is.EqualTo(2u));
        }

        /// <summary>
        /// M1-2: a triggering item draining a backlog over several Publish calls triggers its
        /// linked items for every notification it reports (OPC 10000-4 §5.13.1.6).
        /// </summary>
        [Test]
        public void TriggeringItemDrainingBacklogKeepsTriggeringLinkedItems()
        {
            var clock = new FakeTimeProvider();
            using var subscription = new Subscription(
                m_serverMock.Object,
                m_sessionMock.Object,
                subscriptionId: 1,
                publishingInterval: 100,
                maxLifetimeCount: 1000,
                maxKeepAliveCount: 10,
                maxNotificationsPerPublish: 1,
                priority: 0,
                publishingEnabled: true,
                maxMessageCount: 1,
                timeProvider: clock);
            ResetKeepAlive(subscription);

            Mock<IDataChangeMonitoredItem> triggering = CreateDataChangeItem(1, 3);
            triggering.SetupProperty(i => i.IsReadyToTrigger, true);
            (Mock<IDataChangeMonitoredItem> linked, Mock<ITriggeredMonitoredItem> linkedTrigger) =
                CreateTriggeredItem(2);
            AddMonitoredItem(subscription, triggering.Object);
            AddMonitoredItem(subscription, linked.Object);
            AddTriggerLink(subscription, 1, linkedTrigger.Object);
            using var context = new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);

            clock.Advance(TimeSpan.FromMilliseconds(101));
            Assert.That(subscription.PublishTimerExpired(), Is.EqualTo(PublishingState.NotificationsAvailable));
            linkedTrigger.Verify(i => i.SetTriggered(), Times.Once);
            Assert.That(subscription.Publish(context, out _, out bool moreNotifications), Is.Not.Null);
            Assert.That(moreNotifications, Is.True);

            // the triggering item samples a new value while it still drains its backlog.
            triggering.Object.IsReadyToTrigger = true;
            clock.Advance(TimeSpan.FromMilliseconds(100));
            subscription.PublishTimerExpired();
            Assert.That(subscription.Publish(context, out _, out _), Is.Not.Null);

            linkedTrigger.Verify(i => i.SetTriggered(), Times.Exactly(2));
            Assert.That(triggering.Object.IsReadyToTrigger, Is.False);
        }

        /// <summary>
        /// M1-4: a notification queued before the first link was created must not trigger the
        /// new link (OPC 10000-4 §5.13.1.6).
        /// </summary>
        [Test]
        public void SetTriggeringDiscardsTriggerQueuedBeforeFirstLink()
        {
            var clock = new FakeTimeProvider();
            using Subscription subscription = CreateSubscription(clock, 1000, 10);
            ResetKeepAlive(subscription);

            var triggering = new Mock<IMonitoredItem>();
            triggering.SetupGet(i => i.MonitoringMode).Returns(MonitoringMode.Reporting);
            triggering.SetupGet(i => i.Id).Returns(1);
            triggering.SetupProperty(i => i.IsReadyToTrigger, true);
            (Mock<IDataChangeMonitoredItem> linked, Mock<ITriggeredMonitoredItem> linkedTrigger) =
                CreateTriggeredItem(2);
            AddMonitoredItem(subscription, triggering.Object);
            AddMonitoredItem(subscription, linked.Object);
            using var context = new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);

            subscription.SetTriggering(
                context,
                1,
                [2u],
                [],
                out ArrayOf<StatusCode> addResults,
                out _,
                out _,
                out _);

            Assert.That(addResults[0], Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(triggering.Object.IsReadyToTrigger, Is.False);

            clock.Advance(TimeSpan.FromMilliseconds(101));
            subscription.PublishTimerExpired();
            linkedTrigger.Verify(i => i.SetTriggered(), Times.Never);

            // a notification queued after the link exists triggers it.
            triggering.Object.IsReadyToTrigger = true;
            clock.Advance(TimeSpan.FromMilliseconds(100));
            subscription.PublishTimerExpired();
            linkedTrigger.Verify(i => i.SetTriggered(), Times.Once);
        }

        /// <summary>
        /// M1-5: while publishing is disabled a Publish request yields only a keep-alive; the
        /// messages already built stay queued until publishing is enabled again
        /// (OPC 10000-4 §5.14.1.2).
        /// </summary>
        [Test]
        public void QueuedMessagesAreHeldWhilePublishingIsDisabled()
        {
            var clock = new FakeTimeProvider();
            using var subscription = new Subscription(
                m_serverMock.Object,
                m_sessionMock.Object,
                subscriptionId: 1,
                publishingInterval: 100,
                maxLifetimeCount: 1000,
                maxKeepAliveCount: 10,
                maxNotificationsPerPublish: 1,
                priority: 0,
                publishingEnabled: true,
                maxMessageCount: 10,
                timeProvider: clock);
            AddMonitoredItem(subscription, CreateDataChangeItem(1, 3).Object);
            using var context = new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);

            clock.Advance(TimeSpan.FromMilliseconds(101));
            Assert.That(subscription.PublishTimerExpired(), Is.EqualTo(PublishingState.NotificationsAvailable));
            NotificationMessage first = subscription.Publish(context, out _, out bool moreNotifications);
            Assert.That(first.SequenceNumber, Is.EqualTo(1u));
            Assert.That(moreNotifications, Is.True);

            subscription.SetPublishingMode(context, false);

            NotificationMessage keepAlive = subscription.Publish(
                context,
                out ArrayOf<uint> available,
                out moreNotifications);
            Assert.Multiple(() =>
            {
                Assert.That(keepAlive.NotificationData, Is.Empty);
                Assert.That(keepAlive.SequenceNumber, Is.EqualTo(2u),
                    "The keep-alive announces the next message that will be sent.");
                Assert.That(available.ToArray(), Is.EqualTo(new uint[] { 1 }));
                Assert.That(moreNotifications, Is.False);
            });

            subscription.SetPublishingMode(context, true);

            NotificationMessage second = subscription.Publish(context, out _, out moreNotifications);
            Assert.Multiple(() =>
            {
                Assert.That(second.SequenceNumber, Is.EqualTo(2u));
                Assert.That(second.NotificationData, Has.Count.EqualTo(1));
                Assert.That(moreNotifications, Is.True);
            });
        }

        /// <summary>
        /// Review U21: messages held while publishing is disabled are not in the
        /// retransmission queue yet, so TransferSubscriptions does not advertise them and
        /// Republish does not serve them; Publish delivers each of them exactly once.
        /// </summary>
        [Test]
        public void HeldMessagesAreNotOfferedForRetransmission()
        {
            var clock = new FakeTimeProvider();
            using var subscription = new Subscription(
                m_serverMock.Object,
                m_sessionMock.Object,
                subscriptionId: 1,
                publishingInterval: 100,
                maxLifetimeCount: 1000,
                maxKeepAliveCount: 10,
                maxNotificationsPerPublish: 1,
                priority: 0,
                publishingEnabled: true,
                maxMessageCount: 10,
                timeProvider: clock);
            AddMonitoredItem(subscription, CreateDataChangeItem(1, 3).Object);
            using var context = new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);

            clock.Advance(TimeSpan.FromMilliseconds(101));
            Assert.That(subscription.PublishTimerExpired(), Is.EqualTo(PublishingState.NotificationsAvailable));
            NotificationMessage first = subscription.Publish(context, out _, out _);
            Assert.That(first.SequenceNumber, Is.EqualTo(1u));
            subscription.SetPublishingMode(context, false);

            Assert.That(subscription.AvailableSequenceNumbersForRetransmission().ToArray(), Is.EqualTo(new uint[] { 1 }));
            Assert.That(subscription.Republish(context, 1).SequenceNumber, Is.EqualTo(1u));
            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => subscription.Republish(context, 2));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadMessageNotAvailable));

            subscription.SetPublishingMode(context, true);
            NotificationMessage second = subscription.Publish(context, out ArrayOf<uint> available, out _);
            Assert.That(second.SequenceNumber, Is.EqualTo(2u));
            Assert.That(available.ToArray(), Is.EqualTo(new uint[] { 1, 2 }));
            Assert.That(subscription.Republish(context, 2).SequenceNumber, Is.EqualTo(2u));
        }

        /// <summary>
        /// M1-7: EventQueueOverflowCount counts the EventQueueOverflowEventType events
        /// generated by the subscription's items (OPC 10000-5 §12.15).
        /// </summary>
        [Test]
        public void EventQueueOverflowHandlerIncrementsEventQueueOverflowCount()
        {
            using Subscription subscription = CreateSubscription(new FakeTimeProvider(), 1000, 10);

            subscription.AsPipeline().EventQueueOverflowHandler();
            subscription.AsPipeline().EventQueueOverflowHandler();

            Assert.That(subscription.Diagnostics.EventQueueOverflowCount, Is.EqualTo(2u));
            Assert.That(subscription.Diagnostics.MonitoringQueueOverflowCount, Is.Zero);
        }

        /// <summary>
        /// M1-8: a refresh is in progress from the RefreshStartEvent until its
        /// RefreshEndEvent is queued (OPC 10000-9 §5.5.7).
        /// </summary>
        [Test]
        public async Task ConditionRefreshIsInProgressUntilRefreshEndIsQueuedAsync()
        {
            using Subscription subscription = CreateSubscription(new FakeTimeProvider(), 1000, 10);
            using var context = new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);
            var observations = new List<(string Event, bool Rejected)>();

            var eventItem = new Mock<IEventMonitoredItem>();
            eventItem.SetupGet(i => i.MonitoringMode).Returns(MonitoringMode.Reporting);
            eventItem.SetupGet(i => i.Id).Returns(1);
            eventItem.SetupGet(i => i.EventFilter).Returns(new EventFilter());
            eventItem
                .Setup(i => i.QueueEvent(It.IsAny<IFilterTarget>(), It.IsAny<bool>()))
                .Callback<IFilterTarget, bool>((e, _) =>
                {
                    bool rejected;
                    try
                    {
                        subscription.ValidateConditionRefresh(context);
                        rejected = false;
                    }
                    catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadRefreshInProgress)
                    {
                        rejected = true;
                    }
                    observations.Add((e.GetType().Name, rejected));
                });
            AddMonitoredItem(subscription, eventItem.Object);

            bool rejectedDuringNodeManagerCall = false;
            m_nodeManagerMock
                .Setup(n => n.ConditionRefreshAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<IList<IEventMonitoredItem>>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    try
                    {
                        subscription.ValidateConditionRefresh(context);
                    }
                    catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadRefreshInProgress)
                    {
                        rejectedDuringNodeManagerCall = true;
                    }
                })
                .Returns(default(ValueTask));

            await subscription.ConditionRefreshAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(observations, Is.EqualTo(new[]
                {
                    (nameof(RefreshStartEventState), true),
                    (nameof(RefreshEndEventState), true)
                }));
                Assert.That(rejectedDuringNodeManagerCall, Is.True);
                Assert.DoesNotThrow(() => subscription.ValidateConditionRefresh(context),
                    "The refresh ends once its RefreshEndEvent has been queued.");
            });
        }

        /// <summary>
        /// M1-8: a failing NodeManager refresh does not leave the subscription stuck in
        /// the in-progress state.
        /// </summary>
        [Test]
        public void FailedConditionRefreshClearsTheInProgressState()
        {
            using Subscription subscription = CreateSubscription(new FakeTimeProvider(), 1000, 10);
            using var context = new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);
            var eventItem = new Mock<IEventMonitoredItem>();
            eventItem.SetupGet(i => i.MonitoringMode).Returns(MonitoringMode.Reporting);
            eventItem.SetupGet(i => i.Id).Returns(1);
            eventItem.SetupGet(i => i.EventFilter).Returns(new EventFilter());
            AddMonitoredItem(subscription, eventItem.Object);
            m_nodeManagerMock
                .Setup(n => n.ConditionRefreshAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<IList<IEventMonitoredItem>>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("refresh failed"));

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await subscription.ConditionRefreshAsync().ConfigureAwait(false));
            Assert.DoesNotThrow(() => subscription.ValidateConditionRefresh(context));
        }

        /// <summary>
        /// Review U20: the pipeline applies the reporting rule to every IMonitoredItem
        /// implementation. A pending resend of a DISABLED or SAMPLING item is not
        /// published (OPC 10000-4 §5.12.1.3), a triggered SAMPLING item is.
        /// </summary>
        [TestCase(MonitoringMode.Disabled, false, false)]
        [TestCase(MonitoringMode.Sampling, false, false)]
        [TestCase(MonitoringMode.Disabled, true, false)]
        [TestCase(MonitoringMode.Sampling, true, true)]
        [TestCase(MonitoringMode.Reporting, false, true)]
        public void PipelinePublishesAResendOnlyForReportingItems(
            MonitoringMode mode,
            bool readyToPublish,
            bool expectPublished)
        {
            var clock = new FakeTimeProvider();
            using Subscription subscription = CreateSubscription(clock, 1000, 10);
            Mock<IDataChangeMonitoredItem> item = CreateResendItem(1, () => mode, readyToPublish);
            AddMonitoredItem(subscription, item.Object);
            using var context = new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);

            clock.Advance(TimeSpan.FromMilliseconds(101));
            subscription.PublishTimerExpired();
            NotificationMessage message = subscription.Publish(context, out _, out _);

            Assert.That(message?.NotificationData.Count ?? 0, Is.EqualTo(expectPublished ? 1 : 0));
            VerifyPublished(item, expectPublished ? Times.Once() : Times.Never());
        }

        /// <summary>
        /// Review U20: an item that leaves REPORTING after it was queued for publishing
        /// does not report its pending resend.
        /// </summary>
        [TestCase(MonitoringMode.Disabled)]
        [TestCase(MonitoringMode.Sampling)]
        public void ItemLeavingReportingAfterItWasQueuedIsNotPublished(MonitoringMode newMode)
        {
            var clock = new FakeTimeProvider();
            using Subscription subscription = CreateSubscription(clock, 1000, 10);
            MonitoringMode mode = MonitoringMode.Reporting;
            Mock<IDataChangeMonitoredItem> item = CreateResendItem(1, () => mode, readyToPublish: false);
            AddMonitoredItem(subscription, item.Object);
            using var context = new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);

            clock.Advance(TimeSpan.FromMilliseconds(101));
            Assert.That(subscription.PublishTimerExpired(), Is.EqualTo(PublishingState.NotificationsAvailable));

            mode = newMode;
            NotificationMessage message = subscription.Publish(context, out _, out _);

            Assert.That(message == null || message.NotificationData.Count == 0, Is.True);
            VerifyPublished(item, Times.Never());
        }

        private static Mock<IDataChangeMonitoredItem> CreateResendItem(
            uint id,
            Func<MonitoringMode> mode,
            bool readyToPublish)
        {
            // like the sample DataChangeMonitoredItem: the resend flag survives a mode
            // change and Publish reports the last value whenever it is set.
            var item = new Mock<IDataChangeMonitoredItem>();
            item.SetupGet(i => i.Id).Returns(id);
            item.SetupGet(i => i.MonitoringMode).Returns(mode);
            item.SetupGet(i => i.IsResendData).Returns(true);
            item.SetupGet(i => i.IsReadyToPublish).Returns(readyToPublish);
            item.SetupGet(i => i.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
            item.Setup(i => i.Publish(
                    It.IsAny<OperationContext>(),
                    It.IsAny<Queue<MonitoredItemNotification>>(),
                    It.IsAny<Queue<DiagnosticInfo>>(),
                    It.IsAny<uint>(),
                    It.IsAny<ILogger>()))
                .Returns<OperationContext, Queue<MonitoredItemNotification>, Queue<DiagnosticInfo>, uint, ILogger>(
                    (_, notifications, diagnostics, _, _) =>
                    {
                        notifications.Enqueue(new MonitoredItemNotification { ClientHandle = id, Value = new DataValue(1) });
                        diagnostics.Enqueue(new DiagnosticInfo());
                        return false;
                    });
            return item;
        }

        private static void VerifyPublished(Mock<IDataChangeMonitoredItem> item, Times times)
        {
            item.Verify(
                i => i.Publish(
                    It.IsAny<OperationContext>(),
                    It.IsAny<Queue<MonitoredItemNotification>>(),
                    It.IsAny<Queue<DiagnosticInfo>>(),
                    It.IsAny<uint>(),
                    It.IsAny<ILogger>()),
                times);
        }

        /// <summary>
        /// M1-9: an item removed from the subscription is no longer counted in the
        /// diagnostics even when its NodeManager reports a failure.
        /// </summary>
        [Test]
        public async Task DeletedItemIsUncountedWhenNodeManagerReportsFailureAsync()
        {
            using Subscription subscription = CreateSubscription(new FakeTimeProvider(), 1000, 10);
            var item = new Mock<IMonitoredItem>();
            item.SetupGet(i => i.Id).Returns(7);
            item.SetupGet(i => i.MonitoringMode).Returns(MonitoringMode.Disabled);
            AddMonitoredItem(subscription, item.Object);
            subscription.UpdateDiagnostics(d =>
            {
                d.MonitoredItemCount = 1;
                d.DisabledMonitoredItemCount = 1;
            });
            m_nodeManagerMock
                .Setup(n => n.DeleteMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<uint>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<OperationContext, uint, IList<IMonitoredItem>, IList<ServiceResult>, CancellationToken>(
                    (_, _, _, errors, _) => errors[0] = StatusCodes.BadMonitoredItemIdInvalid)
                .Returns(default(ValueTask));
            using var context = new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);

            DeleteMonitoredItemsResponse response = await subscription.DeleteMonitoredItemsAsync(
                context,
                [7u]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(response.Results[0], Is.EqualTo((StatusCode)StatusCodes.BadMonitoredItemIdInvalid));
                Assert.That(subscription.MonitoredItemCount, Is.Zero);
                Assert.That(subscription.Diagnostics.MonitoredItemCount, Is.Zero);
                Assert.That(subscription.Diagnostics.DisabledMonitoredItemCount, Is.Zero);
            });
        }

        /// <summary>
        /// M1-3: triggering links are part of the stored subscription and rebuilt on restore
        /// for the items that were restored.
        /// </summary>
        [Test]
        public async Task TriggeringLinksSurviveStoreAndRestoreAsync()
        {
            StoredSubscription stored;
            using (Subscription subscription = CreateSubscription(new FakeTimeProvider(), 1000, 10))
            {
                var triggering = new Mock<IMonitoredItem>();
                triggering.SetupGet(i => i.MonitoringMode).Returns(MonitoringMode.Reporting);
                triggering.SetupGet(i => i.Id).Returns(1);
                triggering.Setup(i => i.ToStorableMonitoredItem())
                    .Returns(new StoredMonitoredItem { Id = 1, SubscriptionId = 1 });
                (Mock<IDataChangeMonitoredItem> linked, _) = CreateTriggeredItem(2);
                linked.Setup(i => i.ToStorableMonitoredItem())
                    .Returns(new StoredMonitoredItem { Id = 2, SubscriptionId = 1 });
                AddMonitoredItem(subscription, triggering.Object);
                AddMonitoredItem(subscription, linked.Object);
                using var context = new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);
                subscription.SetTriggering(context, 1, [2u], [], out _, out _, out _, out _);

                stored = (StoredSubscription)subscription.ToStorableSubscription();
            }

            Assert.That(stored.TriggeringLinks, Is.Not.Null);
            Assert.That(stored.TriggeringLinks[1], Is.EqualTo(new uint[] { 2 }));

            var restoredTriggering = new Mock<IMonitoredItem>();
            restoredTriggering.SetupGet(i => i.MonitoringMode).Returns(MonitoringMode.Reporting);
            restoredTriggering.SetupGet(i => i.Id).Returns(1);
            (Mock<IDataChangeMonitoredItem> restoredLinked, _) = CreateTriggeredItem(2);
            m_nodeManagerMock
                .Setup(n => n.RestoreMonitoredItemsAsync(
                    It.IsAny<IList<IStoredMonitoredItem>>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IUserIdentity>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IList<IStoredMonitoredItem>, IList<IMonitoredItem>, IUserIdentity, CancellationToken>(
                    (_, items, _, _) =>
                    {
                        items[0] = restoredTriggering.Object;
                        items[1] = restoredLinked.Object;
                    })
                .Returns(default(ValueTask));
            stored.SentMessages = [];

            using Subscription restored = await Subscription.RestoreAsync(m_serverMock.Object, stored)
                .ConfigureAwait(false);

            IReadOnlyDictionary<uint, IReadOnlyList<uint>> restoredLinks =
                ((IStoredSubscriptionTriggering)restored.ToStorableSubscription()).TriggeringLinks;
            Assert.That(restoredLinks, Is.Not.Null);
            Assert.That(restoredLinks[1], Is.EqualTo(new uint[] { 2 }));
        }

        /// <summary>
        /// M1-3: links to items that could not be restored are dropped.
        /// </summary>
        [Test]
        public async Task TriggeringLinksToUnrestoredItemsAreDroppedAsync()
        {
            var stored = new StoredSubscription
            {
                Id = 5,
                IsDurable = true,
                PublishingInterval = 1000,
                MaxKeepaliveCount = 10,
                MaxLifetimeCount = 30,
                MaxMessageCount = 10,
                SequenceNumber = 1,
                SentMessages = [],
                MonitoredItems =
                [
                    new StoredMonitoredItem { Id = 1, SubscriptionId = 5 },
                    new StoredMonitoredItem { Id = 2, SubscriptionId = 5 }
                ],
                TriggeringLinks = new Dictionary<uint, IReadOnlyList<uint>> { [1] = [2u] }
            };
            var restoredTriggering = new Mock<IMonitoredItem>();
            restoredTriggering.SetupGet(i => i.MonitoringMode).Returns(MonitoringMode.Reporting);
            restoredTriggering.SetupGet(i => i.Id).Returns(1);
            m_nodeManagerMock
                .Setup(n => n.RestoreMonitoredItemsAsync(
                    It.IsAny<IList<IStoredMonitoredItem>>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IUserIdentity>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IList<IStoredMonitoredItem>, IList<IMonitoredItem>, IUserIdentity, CancellationToken>(
                    (_, items, _, _) => items[0] = restoredTriggering.Object)
                .Returns(default(ValueTask));

            using Subscription restored = await Subscription.RestoreAsync(m_serverMock.Object, stored)
                .ConfigureAwait(false);

            Assert.That(
                ((IStoredSubscriptionTriggering)restored.ToStorableSubscription()).TriggeringLinks,
                Is.Null);
        }

        private Subscription CreateSubscription(
            TimeProvider timeProvider,
            uint maxLifetimeCount,
            uint maxKeepAliveCount)
        {
            return new Subscription(
                m_serverMock.Object,
                m_sessionMock.Object,
                subscriptionId: 1,
                publishingInterval: 100,
                maxLifetimeCount: maxLifetimeCount,
                maxKeepAliveCount: maxKeepAliveCount,
                maxNotificationsPerPublish: 0,
                priority: 0,
                publishingEnabled: true,
                maxMessageCount: 10,
                timeProvider: timeProvider);
        }

        private static Mock<IDataChangeMonitoredItem> CreateDataChangeItem(uint id, int notificationCount)
        {
            var item = new Mock<IDataChangeMonitoredItem>();
            item.SetupGet(i => i.MonitoringMode).Returns(MonitoringMode.Reporting);
            var pending = new Queue<MonitoredItemNotification>();
            for (int index = 0; index < notificationCount; index++)
            {
                pending.Enqueue(new MonitoredItemNotification { ClientHandle = id, Value = new DataValue(index) });
            }
            item.SetupGet(i => i.Id).Returns(id);
            item.SetupGet(i => i.IsReadyToPublish).Returns(() => pending.Count > 0);
            item.SetupGet(i => i.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
            item.Setup(i => i.Publish(
                    It.IsAny<OperationContext>(),
                    It.IsAny<Queue<MonitoredItemNotification>>(),
                    It.IsAny<Queue<DiagnosticInfo>>(),
                    It.IsAny<uint>(),
                    It.IsAny<ILogger>()))
                .Returns<OperationContext, Queue<MonitoredItemNotification>, Queue<DiagnosticInfo>, uint, ILogger>(
                    (_, notifications, diagnostics, maxNotificationsPerPublish, _) =>
                    {
                        for (uint published = 0; pending.Count > 0 && published < maxNotificationsPerPublish; published++)
                        {
                            notifications.Enqueue(pending.Dequeue());
                            diagnostics.Enqueue(new DiagnosticInfo());
                        }
                        return pending.Count > 0;
                    });
            return item;
        }

        private static (Mock<IDataChangeMonitoredItem> Item, Mock<ITriggeredMonitoredItem> Trigger)
            CreateTriggeredItem(uint id)
        {
            var item = new Mock<IDataChangeMonitoredItem>();
            item.SetupGet(i => i.MonitoringMode).Returns(MonitoringMode.Reporting);
            item.SetupGet(i => i.Id).Returns(id);
            item.SetupGet(i => i.IsReadyToPublish).Returns(false);
            item.SetupGet(i => i.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
            Mock<ITriggeredMonitoredItem> trigger = item.As<ITriggeredMonitoredItem>();
            trigger.SetupGet(i => i.Id).Returns(id);
            trigger.Setup(i => i.SetTriggered()).Returns(false);
            return (item, trigger);
        }

        private static void ResetKeepAlive(Subscription subscription)
        {
            typeof(Subscription)
                .GetField("m_keepAliveCounter", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(subscription, 0u);
        }

        private static void AddMonitoredItem(Subscription subscription, IMonitoredItem item)
        {
            var monitoredItems = (System.Collections.IDictionary)typeof(Subscription)
                .GetField("m_monitoredItems", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(subscription);
            var itemsToCheck = (LinkedList<IMonitoredItem>)typeof(Subscription)
                .GetField("m_itemsToCheck", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(subscription);
            monitoredItems.Add(item.Id, itemsToCheck.AddLast(item));
        }

        private static void AddTriggerLink(
            Subscription subscription,
            uint triggeringId,
            ITriggeredMonitoredItem triggeredItem)
        {
            var itemsToTrigger = (Dictionary<uint, List<ITriggeredMonitoredItem>>)typeof(Subscription)
                .GetField("m_itemsToTrigger", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(subscription);
            if (!itemsToTrigger.TryGetValue(triggeringId, out List<ITriggeredMonitoredItem> list))
            {
                itemsToTrigger[triggeringId] = list = [];
            }
            list.Add(triggeredItem);
        }
    }
}
