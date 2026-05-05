using System;
using System.Collections.Generic;
using System.Reflection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public class SubscriptionTests
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

            // ServerSystemContext requires invoked server mock to have properties setup
            m_serverMock.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(m_serverMock.Object));

            m_sessionMock.Setup(s => s.Id).Returns(new NodeId(Guid.NewGuid()));

            m_diagnosticsNodeManagerMock
                .Setup(d => d.CreateSubscriptionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(),
                    It.IsAny<SubscriptionDiagnosticsDataType>(),
                    It.IsAny<NodeValueSimpleEventHandler>()))
                .ReturnsAsync(new NodeId(1));
        }

        private Subscription CreateSubscription(double publishingInterval = 1000)
        {
            return new Subscription(
                m_serverMock.Object,
                m_sessionMock.Object,
                subscriptionId: 1,
                publishingInterval: publishingInterval,
                maxLifetimeCount: 10,
                maxKeepAliveCount: 5,
                maxNotificationsPerPublish: 0,
                priority: 0,
                publishingEnabled: true,
                maxMessageCount: 10);
        }

        private static void SetExpiryTime(Subscription subscription, long expiryTime)
        {
            FieldInfo field = typeof(Subscription).GetField("m_publishTimerExpiry", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_publishTimerExpiry not found");
            field.SetValue(subscription, expiryTime);
        }

        private static void ResetKeepAlive(Subscription subscription)
        {
            FieldInfo field = typeof(Subscription).GetField("m_keepAliveCounter", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_keepAliveCounter not found");
            field.SetValue(subscription, (uint)0);
        }

        private static void AddMonitoredItem(Subscription subscription, IMonitoredItem item)
        {
            // Subscription has:
            // private readonly Dictionary<uint, LinkedListNode<IMonitoredItem>> m_monitoredItems;
            // private readonly LinkedList<IMonitoredItem> m_itemsToCheck;

            FieldInfo monitoredItemsField = typeof(Subscription).GetField("m_monitoredItems", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_monitoredItems not found");
            FieldInfo itemsToCheckField = typeof(Subscription).GetField("m_itemsToCheck", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_itemsToCheck not found");

            var monitoredItems = (System.Collections.IDictionary)monitoredItemsField.GetValue(subscription);
            var itemsToCheck = (LinkedList<IMonitoredItem>)itemsToCheckField.GetValue(subscription);

            // Add to itemsToCheck first to get the node
            LinkedListNode<IMonitoredItem> node = itemsToCheck.AddLast(item);
            // Add to dictionary
            monitoredItems.Add(item.Id, node);
        }

        private static void AddTriggerLink(Subscription subscription, uint triggeringId, ITriggeredMonitoredItem triggeredItem)
        {
            // private readonly Dictionary<uint, List<ITriggeredMonitoredItem>> m_itemsToTrigger;
            FieldInfo itemsToTriggerField = typeof(Subscription).GetField("m_itemsToTrigger", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_itemsToTrigger not found");
            var itemsToTrigger = (System.Collections.IDictionary)itemsToTriggerField.GetValue(subscription);

            if (!itemsToTrigger.Contains(triggeringId))
            {
                itemsToTrigger.Add(triggeringId, new List<ITriggeredMonitoredItem>());
            }
            var list = (List<ITriggeredMonitoredItem>)itemsToTrigger[triggeringId];
            list.Add(triggeredItem);
        }

        private static int GetItemsToPublishCount(Subscription subscription)
        {
            FieldInfo itemsToPublishField = typeof(Subscription).GetField("m_itemsToPublish", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_itemsToPublish not found");
            var itemsToPublish = (LinkedList<IMonitoredItem>)itemsToPublishField.GetValue(subscription);
            return itemsToPublish.Count;
        }

        [Test]
        public void PublishTimerExpired_NotExpired_ReturnsIdle()
        {
            using Subscription subscription = CreateSubscription(1000);
            ResetKeepAlive(subscription);

            // Set expiry in far future
            SetExpiryTime(subscription, HiResClock.TickCount64 + 100000);

            PublishingState state = subscription.PublishTimerExpired();

            Assert.That(state, Is.EqualTo(PublishingState.Idle));
        }

        [Test]
        public void PublishTimerExpired_Expired_ReturnsNotificationsAvailable_ForKeepAlive()
        {
            using Subscription subscription = CreateSubscription(1000);
            // Don't reset keepalive, it should be maxKeepAliveCount initially.

            SetExpiryTime(subscription, HiResClock.TickCount64 - 100);

            PublishingState state = subscription.PublishTimerExpired();

            Assert.That(state, Is.EqualTo(PublishingState.NotificationsAvailable));
        }

        [Test]
        public void PublishTimerExpired_Expired_ItemsReady_ReturnsNotificationsAvailable()
        {
            using Subscription subscription = CreateSubscription(1000);
            ResetKeepAlive(subscription);
            SetExpiryTime(subscription, HiResClock.TickCount64 - 100);

            // Mock Monitored Item
            var itemMock = new Mock<IMonitoredItem>();
            itemMock.Setup(i => i.Id).Returns(1);
            itemMock.Setup(i => i.IsReadyToPublish).Returns(true);

            AddMonitoredItem(subscription, itemMock.Object);

            PublishingState state = subscription.PublishTimerExpired();

            Assert.That(state, Is.EqualTo(PublishingState.NotificationsAvailable));
            Assert.That(GetItemsToPublishCount(subscription), Is.EqualTo(1));
        }

        [Test]
        public void PublishTimerExpired_Expired_ItemsNotReady_ReturnsIdle()
        {
            using Subscription subscription = CreateSubscription(1000);
            ResetKeepAlive(subscription);
            SetExpiryTime(subscription, HiResClock.TickCount64 - 100);

            // Mock Monitored Item
            var itemMock = new Mock<IMonitoredItem>();
            itemMock.Setup(i => i.Id).Returns(1);
            itemMock.Setup(i => i.IsReadyToPublish).Returns(false);

            AddMonitoredItem(subscription, itemMock.Object);

            PublishingState state = subscription.PublishTimerExpired();

            Assert.That(state, Is.EqualTo(PublishingState.Idle));
            Assert.That(GetItemsToPublishCount(subscription), Is.Zero);
        }

        [Test]
        public void PublishTimerExpired_Expired_IncrementsKeepAlive()
        {
            using Subscription subscription = CreateSubscription(1000);
            ResetKeepAlive(subscription);
            SetExpiryTime(subscription, HiResClock.TickCount64 - 100);

            Assert.That(subscription.Diagnostics.CurrentKeepAliveCount, Is.Zero);

            subscription.PublishTimerExpired();

            Assert.That(subscription.Diagnostics.CurrentKeepAliveCount, Is.EqualTo(1));
        }

        [Test]
        public void PublishTimerExpired_Triggering_CorrectlyTriggersAndPublishes()
        {
            using Subscription subscription = CreateSubscription(1000);
            ResetKeepAlive(subscription);
            SetExpiryTime(subscription, HiResClock.TickCount64 - 100);

            // Item A: Triggering item. Ready to publish, Ready to trigger.
            var itemAMock = new Mock<IMonitoredItem>();
            itemAMock.Setup(i => i.Id).Returns(1);
            itemAMock.Setup(i => i.IsReadyToPublish).Returns(true);
            itemAMock.SetupProperty(i => i.IsReadyToTrigger, true); // Use property behavior so it can be set to false by Subscription

            // Item B: Triggered item. Initially NOT ready to publish.
            // B must implement ITriggeredMonitoredItem as well.
            var itemBMock = new Mock<IMonitoredItem>();
            itemBMock.As<ITriggeredMonitoredItem>();
            Mock<ITriggeredMonitoredItem> triggeredItemB = itemBMock.As<ITriggeredMonitoredItem>();

            itemBMock.Setup(i => i.Id).Returns(2);
            triggeredItemB.Setup(i => i.Id).Returns(2);

            // "State" of ready
            bool bIsReady = false;
            itemBMock.Setup(i => i.IsReadyToPublish).Returns(() => bIsReady);

            // SetTriggered updates state
            triggeredItemB.Setup(i => i.SetTriggered()).Returns(() =>
            {
                bIsReady = true;
                return true; // True indicates it has something to publish
            });

            // Add both items
            AddMonitoredItem(subscription, itemAMock.Object);
            AddMonitoredItem(subscription, itemBMock.Object);

            // Add trigger link A -> B
            AddTriggerLink(subscription, 1, triggeredItemB.Object);

            PublishingState state = subscription.PublishTimerExpired();

            Assert.That(state, Is.EqualTo(PublishingState.NotificationsAvailable));

            // Both items should be in publish queue
            Assert.That(GetItemsToPublishCount(subscription), Is.EqualTo(2), "Both items should be ready to publish");

            // Verify trigger was called
            triggeredItemB.Verify(i => i.SetTriggered(), Times.Once);

            // Verify IsReadyToTrigger on A was reset to false
            Assert.That(itemAMock.Object.IsReadyToTrigger, Is.False, "IsReadyToTrigger should be reset");
        }

        private static void AddMonitoredItemToPublish(Subscription subscription, IMonitoredItem item)
        {
            FieldInfo itemsToPublishField = typeof(Subscription).GetField("m_itemsToPublish", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_itemsToPublish not found");
            var itemsToPublish = (LinkedList<IMonitoredItem>)itemsToPublishField.GetValue(subscription);
            itemsToPublish.AddLast(item);
        }

        [Test]
        public void Publish_MultipleTimes_WithMaxMessageCount()
        {
            using var subscription = new Subscription(m_serverMock.Object, m_sessionMock.Object, 1, 100, 1000, 10, 1, 0, true, 2);
            var itemMock = new Mock<IDataChangeMonitoredItem2>();

            var values = new List<MonitoredItemNotification>
            {
                new MonitoredItemNotification { Value = new DataValue(1) },
                new MonitoredItemNotification { Value = new DataValue(2) },
                new MonitoredItemNotification { Value = new DataValue(3) }
            };

            int counter = 0;
            itemMock.Setup(i => i.Publish(
                It.IsAny<OperationContext>(),
                It.IsAny<Queue<MonitoredItemNotification>>(),
                It.IsAny<Queue<DiagnosticInfo>>(),
                It.IsAny<uint>(),
                It.IsAny<Microsoft.Extensions.Logging.ILogger>()))
                .Returns<OperationContext, Queue<MonitoredItemNotification>, Queue<DiagnosticInfo>, uint, Microsoft.Extensions.Logging.ILogger>(
                (ctx, nq, dq, max, logger) =>
                {
                    if (counter < values.Count)
                    {
                        nq.Enqueue(values[counter++]);
                        dq.Enqueue(new DiagnosticInfo());
                        itemMock.SetupGet(x => x.IsReadyToPublish).Returns(counter < values.Count);
                        return counter < values.Count;
                    }
                    return false;
                });
            itemMock.SetupGet(i => i.Id).Returns(1);
            itemMock.SetupGet(i => i.IsReadyToPublish).Returns(true);
            itemMock.SetupGet(i => i.AttributeId).Returns(Attributes.Value);
            itemMock.SetupGet(i => i.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);

            AddMonitoredItem(subscription, itemMock.Object);
            SetExpiryTime(subscription, HiResClock.TickCount64 - 100);
            PublishingState state = subscription.PublishTimerExpired();

            AddMonitoredItemToPublish(subscription, itemMock.Object);

            var messages = new List<NotificationMessage>();

            // First publish
            var ctx1 = new OperationContext(m_sessionMock.Object, new DiagnosticsMasks());
            var message = subscription.Publish(ctx1, out var availableSequenceNumbers, out bool moreNotifications1);
            messages.Add(message);

            // Should be more because we generated multiple notifications and limit the max per publish to 1 for tests.
            Assert.That(moreNotifications1, Is.True);

            // Second publish
            var message2 = subscription.Publish(ctx1, out availableSequenceNumbers, out bool moreNotifications2);

            // third publish
            var message3 = subscription.Publish(ctx1, out availableSequenceNumbers, out bool moreNotifications3);
            
            Assert.That(message2, Is.Not.Null);
            Assert.That(message3, Is.Not.Null);
            Assert.That(moreNotifications2, Is.True);
            Assert.That(moreNotifications3, Is.False);
        }
    }
}
