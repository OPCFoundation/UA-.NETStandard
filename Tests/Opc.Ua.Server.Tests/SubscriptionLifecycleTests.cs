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
using System.Reflection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Unit tests for Subscription lifecycle operations: Modify, SetPublishingMode,
    /// Acknowledge, Republish, PublishTimeout, SessionClosed, SetTriggering, etc.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public class SubscriptionLifecycleTests
    {
        private Mock<IServerInternal> m_serverMock;
        private Mock<ISession> m_sessionMock;
        private Mock<IDiagnosticsNodeManager> m_diagnosticsNodeManagerMock;
        private Mock<IMonitoredItemQueueFactory> m_queueFactoryMock;
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_serverMock = new Mock<IServerInternal>();
            m_sessionMock = new Mock<ISession>();
            m_diagnosticsNodeManagerMock = new Mock<IDiagnosticsNodeManager>();
            m_queueFactoryMock = new Mock<IMonitoredItemQueueFactory>();

            m_serverMock.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_serverMock.Setup(s => s.DiagnosticsNodeManager).Returns(m_diagnosticsNodeManagerMock.Object);
            m_serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(m_queueFactoryMock.Object);

            var namespaceUris = new NamespaceTable();
            m_serverMock.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            m_serverMock.Setup(s => s.ServerUris).Returns(new StringTable());
            m_serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            m_serverMock.Setup(s => s.Factory).Returns(new Mock<IEncodeableFactory>().Object);
            m_serverMock.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(m_serverMock.Object));

            m_sessionMock.Setup(s => s.Id).Returns(new NodeId(Guid.NewGuid()));

            m_diagnosticsNodeManagerMock
                .Setup(d => d.CreateSubscriptionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(),
                    It.IsAny<SubscriptionDiagnosticsDataType>(),
                    It.IsAny<NodeValueSimpleEventHandler>()))
                .ReturnsAsync(new NodeId(1));

            m_diagnosticsNodeManagerMock
                .Setup(d => d.DeleteSubscriptionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<System.Threading.CancellationToken>()))
                .Returns(default(System.Threading.Tasks.ValueTask));
        }

        private Subscription CreateSubscription(
            double publishingInterval = 1000,
            uint maxLifetimeCount = 10,
            uint maxKeepAliveCount = 5,
            byte priority = 0,
            bool publishingEnabled = true)
        {
            return new Subscription(
                m_serverMock.Object,
                m_sessionMock.Object,
                subscriptionId: 1,
                publishingInterval: publishingInterval,
                maxLifetimeCount: maxLifetimeCount,
                maxKeepAliveCount: maxKeepAliveCount,
                maxNotificationsPerPublish: 0,
                priority: priority,
                publishingEnabled: publishingEnabled,
                maxMessageCount: 10);
        }

        private OperationContext CreateOperationContext()
        {
            return new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);
        }

        private static void InjectSentMessages(Subscription subscription, params NotificationMessage[] messages)
        {
            FieldInfo field = typeof(Subscription).GetField("m_sentMessages",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_sentMessages not found");
            var sentMessages = (List<NotificationMessage>)field.GetValue(subscription);
            sentMessages.AddRange(messages);
        }


        [Test]
        public void ConstructorThrowsArgumentNullExceptionForNullServer()
        {
            Assert.That(
                () => new Subscription(
                    server: null,
                    m_sessionMock.Object,
                    subscriptionId: 1,
                    publishingInterval: 1000,
                    maxLifetimeCount: 10,
                    maxKeepAliveCount: 5,
                    maxNotificationsPerPublish: 0,
                    priority: 0,
                    publishingEnabled: true,
                    maxMessageCount: 10),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorThrowsArgumentNullExceptionForNullSession()
        {
            Assert.That(
                () => new Subscription(
                    m_serverMock.Object,
                    session: null,
                    subscriptionId: 1,
                    publishingInterval: 1000,
                    maxLifetimeCount: 10,
                    maxKeepAliveCount: 5,
                    maxNotificationsPerPublish: 0,
                    priority: 0,
                    publishingEnabled: true,
                    maxMessageCount: 10),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void NewSubscriptionHasCorrectInitialDiagnostics()
        {
            const double publishingInterval = 500.0;
            const uint maxKeepAlive = 3;
            const uint maxLifetime = 9;
            const byte priority = 42;

            using Subscription subscription = CreateSubscription(
                publishingInterval: publishingInterval,
                maxLifetimeCount: maxLifetime,
                maxKeepAliveCount: maxKeepAlive,
                priority: priority);

            Assert.That(subscription.Diagnostics.PublishingInterval, Is.EqualTo(publishingInterval));
            Assert.That(subscription.Diagnostics.MaxKeepAliveCount, Is.EqualTo(maxKeepAlive));
            Assert.That(subscription.Diagnostics.MaxLifetimeCount, Is.EqualTo(maxLifetime));
            Assert.That(subscription.Diagnostics.PublishingEnabled, Is.True);
            Assert.That(subscription.Diagnostics.ModifyCount, Is.EqualTo(0u));
            Assert.That(subscription.Diagnostics.EnableCount, Is.EqualTo(0u));
            Assert.That(subscription.Diagnostics.DisableCount, Is.EqualTo(0u));
        }

        [Test]
        public void NewSubscriptionIsDurableIsFalse()
        {
            using Subscription subscription = CreateSubscription();
            Assert.That(subscription.IsDurable, Is.False);
        }

        [Test]
        public void PublishingIntervalReturnsConfiguredValue()
        {
            const double expected = 750.0;
            using Subscription subscription = CreateSubscription(publishingInterval: expected);
            Assert.That(subscription.PublishingInterval, Is.EqualTo(expected));
        }

        [Test]
        public void MonitoredItemCountIsZeroForNewSubscription()
        {
            using Subscription subscription = CreateSubscription();
            Assert.That(subscription.MonitoredItemCount, Is.EqualTo(0));
        }



        [Test]
        public void GetMonitoredItemsReturnsEmptyForNewSubscription()
        {
            using Subscription subscription = CreateSubscription();

            subscription.GetMonitoredItems(out ArrayOf<uint> serverHandles, out ArrayOf<uint> clientHandles);

            Assert.That(serverHandles, Is.Empty);
            Assert.That(clientHandles, Is.Empty);
        }



        [Test]
        public void QueueOverflowHandlerIncrementsOverflowCount()
        {
            using Subscription subscription = CreateSubscription();

            Assert.That(subscription.Diagnostics.MonitoringQueueOverflowCount, Is.EqualTo(0u));

            subscription.QueueOverflowHandler();

            Assert.That(subscription.Diagnostics.MonitoringQueueOverflowCount, Is.EqualTo(1u));
        }

        [Test]
        public void QueueOverflowHandlerCanBeCalledMultipleTimes()
        {
            using Subscription subscription = CreateSubscription();

            subscription.QueueOverflowHandler();
            subscription.QueueOverflowHandler();
            subscription.QueueOverflowHandler();

            Assert.That(subscription.Diagnostics.MonitoringQueueOverflowCount, Is.EqualTo(3u));
        }



        [Test]
        public void SessionClosedClearsSessionReference()
        {
            using Subscription subscription = CreateSubscription();

            Assert.That(subscription.Session, Is.Not.Null);

            subscription.SessionClosed();

            Assert.That(subscription.Session, Is.Null);
        }

        [Test]
        public void SessionIdReturnsDefaultAfterSessionClosed()
        {
            using Subscription subscription = CreateSubscription();

            subscription.SessionClosed();

            Assert.That(subscription.SessionId, Is.EqualTo(default(NodeId)));
        }

        [Test]
        public void SessionIdDiagnosticsIsDefaultAfterSessionClosed()
        {
            using Subscription subscription = CreateSubscription();

            subscription.SessionClosed();

            Assert.That(subscription.Diagnostics.SessionId, Is.EqualTo(default(NodeId)));
        }



        [Test]
        public void PublishTimeoutReturnsBadTimeoutMessage()
        {
            using Subscription subscription = CreateSubscription();

            NotificationMessage message = subscription.PublishTimeout();

            Assert.That(message, Is.Not.Null);
            Assert.That(message.NotificationData, Has.Count.EqualTo(1));
            bool hasNotification = message.NotificationData[0]
                .TryGetValue(out StatusChangeNotification notification);
            Assert.That(hasNotification, Is.True);
            Assert.That(notification, Is.Not.Null);
            Assert.That(notification.Status, Is.EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public void PublishTimeoutReturnsMessageWithSequenceNumber()
        {
            using Subscription subscription = CreateSubscription();

            NotificationMessage message = subscription.PublishTimeout();

            Assert.That(message.SequenceNumber, Is.GreaterThan(0u));
        }

        [Test]
        public void PublishTimeoutMarkSubscriptionAsExpiredCausesPublishToReturnNull()
        {
            using Subscription subscription = CreateSubscription();

            subscription.PublishTimeout();

            OperationContext context = CreateOperationContext();

            NotificationMessage message = subscription.Publish(
                context,
                out _,
                out _);

            Assert.That(message, Is.Null);
        }



        [Test]
        public void SubscriptionTransferredReturnsTransferStatusMessage()
        {
            using Subscription subscription = CreateSubscription();

            NotificationMessage message = subscription.SubscriptionTransferred();

            Assert.That(message, Is.Not.Null);
            Assert.That(message.NotificationData, Has.Count.EqualTo(1));
            bool hasNotification = message.NotificationData[0]
                .TryGetValue(out StatusChangeNotification notification);
            Assert.That(hasNotification, Is.True);
            Assert.That(notification, Is.Not.Null);
            Assert.That(notification.Status, Is.EqualTo(StatusCodes.GoodSubscriptionTransferred));
        }

        [Test]
        public void SubscriptionTransferredReturnsMessageWithSequenceNumber()
        {
            using Subscription subscription = CreateSubscription();

            NotificationMessage message = subscription.SubscriptionTransferred();

            Assert.That(message.SequenceNumber, Is.GreaterThan(0u));
        }



        [Test]
        public void SetSubscriptionDurableReturnsBadNotSupportedWhenNotSupported()
        {
            m_queueFactoryMock.Setup(f => f.SupportsDurableQueues).Returns(false);
            using Subscription subscription = CreateSubscription();

            ServiceResult result = subscription.SetSubscriptionDurable(maxLifetimeCount: 1000);

            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public void SetSubscriptionDurableSetsIsDurableWhenSupported()
        {
            m_queueFactoryMock.Setup(f => f.SupportsDurableQueues).Returns(true);
            using Subscription subscription = CreateSubscription();

            Assert.That(subscription.IsDurable, Is.False);

            ServiceResult result = subscription.SetSubscriptionDurable(maxLifetimeCount: 1000);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(subscription.IsDurable, Is.True);
        }

        [Test]
        public void SetSubscriptionDurableUpdatesMaxLifetimeCount()
        {
            const uint newLifetimeCount = 2000u;
            m_queueFactoryMock.Setup(f => f.SupportsDurableQueues).Returns(true);
            using Subscription subscription = CreateSubscription(maxLifetimeCount: 10);

            subscription.SetSubscriptionDurable(maxLifetimeCount: newLifetimeCount);

            Assert.That(subscription.Diagnostics.MaxLifetimeCount, Is.EqualTo(newLifetimeCount));
        }



        [Test]
        public void ModifyUpdatesPublishingIntervalInDiagnostics()
        {
            const double newInterval = 2000.0;
            using Subscription subscription = CreateSubscription(publishingInterval: 1000);
            OperationContext context = CreateOperationContext();

            subscription.Modify(context,
                publishingInterval: newInterval,
                maxLifetimeCount: 10,
                maxKeepAliveCount: 5,
                maxNotificationsPerPublish: 0,
                priority: 0);

            Assert.That(subscription.Diagnostics.PublishingInterval, Is.EqualTo(newInterval));
            Assert.That(subscription.PublishingInterval, Is.EqualTo(newInterval));
        }

        [Test]
        public void ModifyIncrementsDiagnosticsModifyCount()
        {
            using Subscription subscription = CreateSubscription();
            OperationContext context = CreateOperationContext();

            Assert.That(subscription.Diagnostics.ModifyCount, Is.EqualTo(0u));

            subscription.Modify(context, 1000, 10, 5, 0, 0);

            Assert.That(subscription.Diagnostics.ModifyCount, Is.EqualTo(1u));
        }

        [Test]
        public void ModifyUpdatesPriority()
        {
            const byte newPriority = 100;
            using Subscription subscription = CreateSubscription(priority: 0);
            OperationContext context = CreateOperationContext();

            subscription.Modify(context, 1000, 10, 5, 0, priority: newPriority);

            Assert.That(subscription.Priority, Is.EqualTo(newPriority));
            Assert.That(subscription.Diagnostics.Priority, Is.EqualTo(newPriority));
        }

        [Test]
        public void ModifyUpdatesMaxKeepAliveCount()
        {
            const uint newKeepAlive = 10u;
            using Subscription subscription = CreateSubscription(maxKeepAliveCount: 5);
            OperationContext context = CreateOperationContext();

            subscription.Modify(context, 1000, 10, maxKeepAliveCount: newKeepAlive, 0, 0);

            Assert.That(subscription.Diagnostics.MaxKeepAliveCount, Is.EqualTo(newKeepAlive));
        }

        [Test]
        public void ModifyUpdatesMaxLifetimeCount()
        {
            const uint newLifetime = 30u;
            using Subscription subscription = CreateSubscription(maxLifetimeCount: 10);
            OperationContext context = CreateOperationContext();

            subscription.Modify(context, 1000, maxLifetimeCount: newLifetime, 5, 0, 0);

            Assert.That(subscription.Diagnostics.MaxLifetimeCount, Is.EqualTo(newLifetime));
        }

        [Test]
        public void ModifyCanBeCalledMultipleTimesIncrementingModifyCount()
        {
            using Subscription subscription = CreateSubscription();
            OperationContext context = CreateOperationContext();

            subscription.Modify(context, 1000, 10, 5, 0, 0);
            subscription.Modify(context, 2000, 10, 5, 0, 0);

            Assert.That(subscription.Diagnostics.ModifyCount, Is.EqualTo(2u));
        }



        [Test]
        public void SetPublishingModeDisableIncrementsDisableCount()
        {
            using Subscription subscription = CreateSubscription(publishingEnabled: true);
            OperationContext context = CreateOperationContext();

            Assert.That(subscription.Diagnostics.DisableCount, Is.EqualTo(0u));

            subscription.SetPublishingMode(context, publishingEnabled: false);

            Assert.That(subscription.Diagnostics.DisableCount, Is.EqualTo(1u));
            Assert.That(subscription.Diagnostics.PublishingEnabled, Is.False);
        }

        [Test]
        public void SetPublishingModeEnableIncrementsEnableCount()
        {
            using Subscription subscription = CreateSubscription(publishingEnabled: false);
            OperationContext context = CreateOperationContext();

            Assert.That(subscription.Diagnostics.EnableCount, Is.EqualTo(0u));

            subscription.SetPublishingMode(context, publishingEnabled: true);

            Assert.That(subscription.Diagnostics.EnableCount, Is.EqualTo(1u));
            Assert.That(subscription.Diagnostics.PublishingEnabled, Is.True);
        }

        [Test]
        public void SetPublishingModeNoChangeDoesNotIncrementEnableOrDisableCount()
        {
            using Subscription subscription = CreateSubscription(publishingEnabled: true);
            OperationContext context = CreateOperationContext();

            subscription.SetPublishingMode(context, publishingEnabled: true);

            Assert.That(subscription.Diagnostics.EnableCount, Is.EqualTo(0u));
            Assert.That(subscription.Diagnostics.DisableCount, Is.EqualTo(0u));
        }



        [Test]
        public void AcknowledgeReturnsBadSequenceNumberInvalidForSequenceNumberZero()
        {
            using Subscription subscription = CreateSubscription();
            OperationContext context = CreateOperationContext();

            ServiceResult result = subscription.Acknowledge(context, sequenceNumber: 0);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadSequenceNumberInvalid));
        }

        [Test]
        public void AcknowledgeReturnsBadSequenceNumberUnknownForUnknownMessage()
        {
            using Subscription subscription = CreateSubscription();
            OperationContext context = CreateOperationContext();

            ServiceResult result = subscription.Acknowledge(context, sequenceNumber: 42);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadSequenceNumberUnknown));
        }

        [Test]
        public void AcknowledgeRemovesMessageFromQueue()
        {
            using Subscription subscription = CreateSubscription();
            OperationContext context = CreateOperationContext();

            var message = new NotificationMessage { SequenceNumber = 5 };
            InjectSentMessages(subscription, message);

            Assert.That(subscription.AvailableSequenceNumbersForRetransmission(), Has.Count.EqualTo(1));

            ServiceResult result = subscription.Acknowledge(context, sequenceNumber: 5);

            Assert.That(result, Is.Null, "Successful acknowledge returns null ServiceResult");
            Assert.That(subscription.AvailableSequenceNumbersForRetransmission(), Is.Empty);
        }

        [Test]
        public void AcknowledgeReturnsNullForSuccessfulRemoval()
        {
            using Subscription subscription = CreateSubscription();
            OperationContext context = CreateOperationContext();

            var message = new NotificationMessage { SequenceNumber = 10 };
            InjectSentMessages(subscription, message);

            ServiceResult result = subscription.Acknowledge(context, sequenceNumber: 10);

            Assert.That(result, Is.Null);
        }



        [Test]
        public void RepublishThrowsBadMessageNotAvailableWhenNoMessages()
        {
            using Subscription subscription = CreateSubscription();
            OperationContext context = CreateOperationContext();

            Assert.That(
                () => subscription.Republish(context, retransmitSequenceNumber: 99),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("Code")
                    .EqualTo(StatusCodes.BadMessageNotAvailable));
        }

        [Test]
        public void RepublishReturnsCachedMessage()
        {
            using Subscription subscription = CreateSubscription();
            OperationContext context = CreateOperationContext();

            var message = new NotificationMessage { SequenceNumber = 7 };
            InjectSentMessages(subscription, message);

            NotificationMessage result = subscription.Republish(context, retransmitSequenceNumber: 7);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.SequenceNumber, Is.EqualTo(7u));
        }

        [Test]
        public void RepublishIncrementsDiagnosticsRepublishMessageCount()
        {
            using Subscription subscription = CreateSubscription();
            OperationContext context = CreateOperationContext();

            var message = new NotificationMessage { SequenceNumber = 3 };
            InjectSentMessages(subscription, message);

            Assert.That(subscription.Diagnostics.RepublishMessageCount, Is.EqualTo(0u));

            subscription.Republish(context, retransmitSequenceNumber: 3);

            Assert.That(subscription.Diagnostics.RepublishMessageCount, Is.EqualTo(1u));
        }

        [Test]
        public void RepublishThrowsForUnknownSequenceNumberEvenWhenMessagesExist()
        {
            using Subscription subscription = CreateSubscription();
            OperationContext context = CreateOperationContext();

            var message = new NotificationMessage { SequenceNumber = 3 };
            InjectSentMessages(subscription, message);

            Assert.That(
                () => subscription.Republish(context, retransmitSequenceNumber: 99),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("Code")
                    .EqualTo(StatusCodes.BadMessageNotAvailable));
        }



        [Test]
        public void AvailableSequenceNumbersForRetransmissionReturnsEmptyForNewSubscription()
        {
            using Subscription subscription = CreateSubscription();

            ArrayOf<uint> result = subscription.AvailableSequenceNumbersForRetransmission();

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void AvailableSequenceNumbersForRetransmissionReturnsAllSentMessageSequenceNumbers()
        {
            using Subscription subscription = CreateSubscription();

            InjectSentMessages(subscription,
                new NotificationMessage { SequenceNumber = 1 },
                new NotificationMessage { SequenceNumber = 2 },
                new NotificationMessage { SequenceNumber = 3 });

            ArrayOf<uint> result = subscription.AvailableSequenceNumbersForRetransmission();

            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result.ToArray(), Does.Contain(1u));
            Assert.That(result.ToArray(), Does.Contain(2u));
            Assert.That(result.ToArray(), Does.Contain(3u));
        }



        [Test]
        public void PublishReturnsKeepaliveMessageWhenNoItems()
        {
            using Subscription subscription = CreateSubscription(maxKeepAliveCount: 5);
            OperationContext context = CreateOperationContext();

            NotificationMessage message = subscription.Publish(
                context,
                out ArrayOf<uint> availableSequenceNumbers,
                out bool moreNotifications);

            Assert.That(message, Is.Not.Null);
            Assert.That(message.NotificationData, Is.Empty,
                "Keepalive message has no notification data");
            Assert.That(moreNotifications, Is.False);
        }

        [Test]
        public void PublishIncrementsDiagnosticsPublishRequestCount()
        {
            using Subscription subscription = CreateSubscription();
            OperationContext context = CreateOperationContext();

            Assert.That(subscription.Diagnostics.PublishRequestCount, Is.EqualTo(0u));

            subscription.Publish(context, out _, out _);

            Assert.That(subscription.Diagnostics.PublishRequestCount, Is.EqualTo(1u));
        }

        [Test]
        public void PublishThrowsArgumentNullExceptionForNullContext()
        {
            using Subscription subscription = CreateSubscription();

            Assert.That(
                () => subscription.Publish(context: null, out _, out _),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void PublishReturnsNullAfterPublishTimeout()
        {
            using Subscription subscription = CreateSubscription();
            OperationContext context = CreateOperationContext();

            subscription.PublishTimeout();

            NotificationMessage result = subscription.Publish(context, out _, out _);

            Assert.That(result, Is.Null);
        }



        [Test]
        public void SetTriggeringThrowsBadMonitoredItemIdInvalidWhenTriggeringItemNotFound()
        {
            using Subscription subscription = CreateSubscription();
            OperationContext context = CreateOperationContext();

            Assert.That(
                () => subscription.SetTriggering(
                    context,
                    triggeringItemId: 999,
                    linksToAdd: [1, 2],
                    linksToRemove: [],
                    out _,
                    out _,
                    out _,
                    out _),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("Code")
                    .EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
        }

        [Test]
        public void SetTriggeringThrowsArgumentNullExceptionForNullContext()
        {
            using Subscription subscription = CreateSubscription();

            Assert.That(
                () => subscription.SetTriggering(
                    context: null,
                    triggeringItemId: 1,
                    linksToAdd: [],
                    linksToRemove: [],
                    out _,
                    out _,
                    out _,
                    out _),
                Throws.TypeOf<ArgumentNullException>());
        }



        [Test]
        public void RepublishThrowsArgumentNullExceptionForNullContext()
        {
            using Subscription subscription = CreateSubscription();

            Assert.That(
                () => subscription.Republish(context: null, retransmitSequenceNumber: 1),
                Throws.TypeOf<ArgumentNullException>());
        }

    }
}
