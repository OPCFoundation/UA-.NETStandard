using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
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
            m_serverMock.Setup(s => s.DiagnosticsWriteLock).Returns(new object());
            m_serverMock.Setup(s => s.ServerDiagnostics).Returns(new ServerDiagnosticsSummaryDataType());

            var namespaceUris = new NamespaceTable();
            m_serverMock.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            m_serverMock.Setup(s => s.ServerUris).Returns(new StringTable());
            m_serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            m_serverMock.Setup(s => s.Factory).Returns(new Mock<IEncodeableFactory>().Object);

            // ServerSystemContext requires invoked server mock to have properties setup
            m_serverMock.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(m_serverMock.Object));

            m_sessionMock.Setup(s => s.Id).Returns(new NodeId(Guid.NewGuid()));
            m_sessionMock.Setup(s => s.DiagnosticsLock).Returns(new object());
            m_sessionMock.Setup(s => s.SessionDiagnostics).Returns(new SessionDiagnosticsDataType());

            m_diagnosticsNodeManagerMock
                .Setup(d => d.CreateSubscriptionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(),
                    It.IsAny<SubscriptionDiagnosticsDataType>(),
                    It.IsAny<NodeValueSimpleEventHandler>()))
                .ReturnsAsync(new NodeId(1));
        }

        private Subscription CreateSubscription(
            double publishingInterval = 1000,
            uint maxNotificationsPerPublish = 0,
            TimeProvider timeProvider = null)
        {
            return new Subscription(
                m_serverMock.Object,
                m_sessionMock.Object,
                subscriptionId: 1,
                publishingInterval: publishingInterval,
                maxLifetimeCount: 10,
                maxKeepAliveCount: 5,
                maxNotificationsPerPublish: maxNotificationsPerPublish,
                priority: 0,
                publishingEnabled: true,
                maxMessageCount: 10,
                timeProvider: timeProvider);
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
            SetExpiryTime(subscription, TimeProvider.System.GetTimestampMilliseconds() + 100000);

            PublishingState state = subscription.PublishTimerExpired();

            Assert.That(state, Is.EqualTo(PublishingState.Idle));
        }

        [Test]
        public void PublishTimerExpired_Expired_ReturnsNotificationsAvailable_ForKeepAlive()
        {
            using Subscription subscription = CreateSubscription(1000);
            // Don't reset keepalive, it should be maxKeepAliveCount initially.

            SetExpiryTime(subscription, TimeProvider.System.GetTimestampMilliseconds() - 100);

            PublishingState state = subscription.PublishTimerExpired();

            Assert.That(state, Is.EqualTo(PublishingState.NotificationsAvailable));
        }

        [Test]
        public void PublishTimerExpired_Expired_ItemsReady_ReturnsNotificationsAvailable()
        {
            using Subscription subscription = CreateSubscription(1000);
            ResetKeepAlive(subscription);
            SetExpiryTime(subscription, TimeProvider.System.GetTimestampMilliseconds() - 100);

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
            SetExpiryTime(subscription, TimeProvider.System.GetTimestampMilliseconds() - 100);

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
            SetExpiryTime(subscription, TimeProvider.System.GetTimestampMilliseconds() - 100);

            Assert.That(subscription.Diagnostics.CurrentKeepAliveCount, Is.Zero);

            subscription.PublishTimerExpired();

            Assert.That(subscription.Diagnostics.CurrentKeepAliveCount, Is.EqualTo(1));
        }

        [Test]
        public void PublishTimerExpired_Triggering_CorrectlyTriggersAndPublishes()
        {
            using Subscription subscription = CreateSubscription(1000);
            ResetKeepAlive(subscription);
            SetExpiryTime(subscription, TimeProvider.System.GetTimestampMilliseconds() - 100);

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
                new() { Value = new DataValue(1) },
                new() { Value = new DataValue(2) },
                new() { Value = new DataValue(3) }
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
            SetExpiryTime(subscription, TimeProvider.System.GetTimestampMilliseconds() - 100);
            PublishingState state = subscription.PublishTimerExpired();

            AddMonitoredItemToPublish(subscription, itemMock.Object);

            var messages = new List<NotificationMessage>();

            // First publish
            var ctx1 = new OperationContext(m_sessionMock.Object, new DiagnosticsMasks());
            NotificationMessage message = subscription.Publish(ctx1, out ArrayOf<uint> availableSequenceNumbers, out bool moreNotifications1);
            messages.Add(message);

            // Should be more because we generated multiple notifications and limit the max per publish to 1 for tests.
            Assert.That(moreNotifications1, Is.True);

            // Second publish
            NotificationMessage message2 = subscription.Publish(ctx1, out availableSequenceNumbers, out bool moreNotifications2);

            // third publish
            NotificationMessage message3 = subscription.Publish(ctx1, out availableSequenceNumbers, out bool moreNotifications3);

            Assert.That(message2, Is.Not.Null);
            Assert.That(message3, Is.Not.Null);
            Assert.That(moreNotifications2, Is.True);
            Assert.That(moreNotifications3, Is.False);
        }

        [Test]
        public async Task PublishWithZeroLimitDrainsEventAndDataChangeQueuesAsync()
        {
            var timeProvider = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using Subscription subscription = CreateSubscription(
                publishingInterval: 100,
                maxNotificationsPerPublish: 0,
                timeProvider);
            var publishLimits = new List<uint>();
            Mock<IEventMonitoredItem> eventItem = CreateEventMonitoredItem(
                id: 1,
                notificationCount: 2,
                publishLimits);
            Mock<IDataChangeMonitoredItem> dataChangeItem = CreateDataChangeMonitoredItem(
                id: 2,
                notificationCount: 2,
                publishLimits);
            await RegisterMonitoredItemsAsync(
                subscription,
                eventItem.Object,
                dataChangeItem.Object).ConfigureAwait(false);

            timeProvider.Advance(TimeSpan.FromMilliseconds(101));
            Assert.That(
                subscription.PublishTimerExpired(),
                Is.EqualTo(PublishingState.NotificationsAvailable));

            var context = new OperationContext(m_sessionMock.Object, new DiagnosticsMasks());
            NotificationMessage message = subscription.Publish(
                context,
                out _,
                out bool moreNotifications);

            Assert.That(message.NotificationData, Has.Count.EqualTo(2));
            var eventNotification = (EventNotificationList)ExtensionObject.ToEncodeable(
                message.NotificationData[0]);
            var dataChangeNotification = (DataChangeNotification)ExtensionObject.ToEncodeable(
                message.NotificationData[1]);
            Assert.Multiple(() =>
            {
                Assert.That(moreNotifications, Is.False);
                Assert.That(eventNotification.Events, Has.Count.EqualTo(2));
                Assert.That(dataChangeNotification.MonitoredItems, Has.Count.EqualTo(2));
                Assert.That(dataChangeNotification.DiagnosticInfos, Has.Count.EqualTo(2));
                Assert.That(
                    publishLimits,
                    Is.EqualTo(new[] { uint.MaxValue, uint.MaxValue }));
            });
        }

        [Test]
        public async Task PublishWithFiniteLimitBuildsAndDrainsQueuedMessagesAsync()
        {
            var timeProvider = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using Subscription subscription = CreateSubscription(
                publishingInterval: 100,
                maxNotificationsPerPublish: 2,
                timeProvider);
            var publishLimits = new List<uint>();
            Mock<IEventMonitoredItem> firstEventItem = CreateEventMonitoredItem(
                id: 1,
                notificationCount: 3,
                publishLimits);
            Mock<IEventMonitoredItem> secondEventItem = CreateEventMonitoredItem(
                id: 2,
                notificationCount: 1,
                publishLimits);
            Mock<IDataChangeMonitoredItem> dataChangeItem = CreateDataChangeMonitoredItem(
                id: 3,
                notificationCount: 2,
                publishLimits);
            await RegisterMonitoredItemsAsync(
                subscription,
                firstEventItem.Object,
                secondEventItem.Object,
                dataChangeItem.Object).ConfigureAwait(false);

            timeProvider.Advance(TimeSpan.FromMilliseconds(101));
            Assert.That(
                subscription.PublishTimerExpired(),
                Is.EqualTo(PublishingState.NotificationsAvailable));

            var context = new OperationContext(m_sessionMock.Object, new DiagnosticsMasks());
            NotificationMessage firstMessage = subscription.Publish(
                context,
                out _,
                out bool moreAfterFirst);
            NotificationMessage secondMessage = subscription.Publish(
                context,
                out _,
                out bool moreAfterSecond);
            NotificationMessage thirdMessage = subscription.Publish(
                context,
                out _,
                out bool moreAfterThird);

            Assert.Multiple(() =>
            {
                Assert.That(firstMessage.NotificationData, Has.Count.EqualTo(1));
                Assert.That(secondMessage.NotificationData, Has.Count.EqualTo(1));
                Assert.That(thirdMessage.NotificationData, Has.Count.EqualTo(1));
            });
            var firstEvents = (EventNotificationList)ExtensionObject.ToEncodeable(
                firstMessage.NotificationData[0]);
            var secondEvents = (EventNotificationList)ExtensionObject.ToEncodeable(
                secondMessage.NotificationData[0]);
            var dataChanges = (DataChangeNotification)ExtensionObject.ToEncodeable(
                thirdMessage.NotificationData[0]);
            Assert.Multiple(() =>
            {
                Assert.That(moreAfterFirst, Is.True);
                Assert.That(moreAfterSecond, Is.True);
                Assert.That(moreAfterThird, Is.False);
                Assert.That(firstEvents.Events, Has.Count.EqualTo(2));
                Assert.That(secondEvents.Events, Has.Count.EqualTo(2));
                Assert.That(dataChanges.MonitoredItems, Has.Count.EqualTo(2));
                Assert.That(publishLimits, Is.EqualTo(new uint[] { 6, 6, 6 }));
            });
        }

        [TestCase(0, 0L, 0L)]
        [TestCase(0, 25L, 25L)]
        [TestCase(10, 0L, 10L)]
        [TestCase(10, 10L, 10L)]
        [TestCase(10, 25L, 10L)]
        [TestCase(0, uint.MaxValue, uint.MaxValue)]
        [TestCase(10, uint.MaxValue, 10L)]
        [TestCase(int.MaxValue, uint.MaxValue, int.MaxValue)]
        public async Task CreateSubscriptionWithNotificationLimitsUsesEffectiveLimitAsync(
            int serverLimit,
            long requestedLimit,
            long expectedLimit)
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationsPerPublish = serverLimit
                }
            };
            using var manager = new SubscriptionManager(
                m_serverMock.Object,
                configuration);

            var context = new OperationContext(m_sessionMock.Object, new DiagnosticsMasks());
            CreateSubscriptionResponse response = await manager.CreateSubscriptionAsync(
                context,
                requestedPublishingInterval: 1000,
                requestedLifetimeCount: 30,
                requestedMaxKeepAliveCount: 10,
                maxNotificationsPerPublish: (uint)requestedLimit,
                publishingEnabled: true,
                priority: 0).ConfigureAwait(false);

            Assert.That(
                manager.TryGetSubscription(response.SubscriptionId, out ISubscription subscription),
                Is.True);
            Assert.That(
                subscription.Diagnostics.MaxNotificationsPerPublish,
                Is.EqualTo((uint)expectedLimit));
        }

        [TestCase(0, 0L, 0L)]
        [TestCase(0, 25L, 25L)]
        [TestCase(10, 0L, 10L)]
        [TestCase(10, 10L, 10L)]
        [TestCase(10, 25L, 10L)]
        [TestCase(0, uint.MaxValue, uint.MaxValue)]
        [TestCase(10, uint.MaxValue, 10L)]
        [TestCase(int.MaxValue, uint.MaxValue, int.MaxValue)]
        public async Task ModifySubscriptionWithNotificationLimitsUsesEffectiveLimitAsync(
            int serverLimit,
            long requestedLimit,
            long expectedLimit)
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationsPerPublish = serverLimit
                }
            };
            using var manager = new SubscriptionManager(
                m_serverMock.Object,
                configuration);

            var context = new OperationContext(m_sessionMock.Object, new DiagnosticsMasks());
            CreateSubscriptionResponse response = await manager.CreateSubscriptionAsync(
                context,
                requestedPublishingInterval: 1000,
                requestedLifetimeCount: 30,
                requestedMaxKeepAliveCount: 10,
                maxNotificationsPerPublish: 1,
                publishingEnabled: true,
                priority: 0).ConfigureAwait(false);

            manager.ModifySubscription(
                context,
                response.SubscriptionId,
                requestedPublishingInterval: 1000,
                requestedLifetimeCount: 30,
                requestedMaxKeepAliveCount: 10,
                maxNotificationsPerPublish: (uint)requestedLimit,
                priority: 0,
                revisedPublishingInterval: out _,
                revisedLifetimeCount: out _,
                revisedMaxKeepAliveCount: out _);

            Assert.That(
                manager.TryGetSubscription(response.SubscriptionId, out ISubscription subscription),
                Is.True);
            Assert.That(
                subscription.Diagnostics.MaxNotificationsPerPublish,
                Is.EqualTo((uint)expectedLimit));
        }

        private async Task RegisterMonitoredItemsAsync(
            Subscription subscription,
            params IMonitoredItem[] monitoredItems)
        {
            m_nodeManagerMock
                .Setup(n => n.CreateMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    subscription.Id,
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<IList<MonitoringFilterResult>>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Callback<
                    OperationContext,
                    uint,
                    double,
                    TimestampsToReturn,
                    ArrayOf<MonitoredItemCreateRequest>,
                    IList<ServiceResult>,
                    IList<MonitoringFilterResult>,
                    IList<IMonitoredItem>,
                    bool,
                    CancellationToken>(
                    (_, _, _, _, _, _, _, createdItems, _, _) =>
                    {
                        for (int ii = 0; ii < monitoredItems.Length; ii++)
                        {
                            createdItems[ii] = monitoredItems[ii];
                        }
                    })
                .Returns(default(ValueTask));

            var requests = new MonitoredItemCreateRequest[monitoredItems.Length];
            for (int ii = 0; ii < monitoredItems.Length; ii++)
            {
                requests[ii] = new MonitoredItemCreateRequest
                {
                    MonitoringMode = MonitoringMode.Reporting
                };
            }
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate = [.. requests];

            var context = new OperationContext(m_sessionMock.Object, new DiagnosticsMasks());
            CreateMonitoredItemsResponse response = await subscription.CreateMonitoredItemsAsync(
                context,
                TimestampsToReturn.Both,
                itemsToCreate).ConfigureAwait(false);

            Assert.That(response.Results, Has.Count.EqualTo(monitoredItems.Length));
        }

        private static Mock<IEventMonitoredItem> CreateEventMonitoredItem(
            uint id,
            int notificationCount,
            List<uint> publishLimits)
        {
            var item = new Mock<IEventMonitoredItem>();
            var createResult = new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.Good,
                RevisedSamplingInterval = 0,
                RevisedQueueSize = (uint)notificationCount
            };
            item.SetupGet(i => i.Id).Returns(id);
            item.SetupGet(i => i.IsReadyToPublish).Returns(true);
            item.SetupGet(i => i.MonitoredItemType).Returns(MonitoredItemTypeMask.Events);
            item.Setup(i => i.GetCreateResult(out createResult)).Returns(ServiceResult.Good);
            item.Setup(i => i.Publish(
                    It.IsAny<OperationContext>(),
                    It.IsAny<Queue<EventFieldList>>(),
                    It.IsAny<uint>()))
                .Returns<OperationContext, Queue<EventFieldList>, uint>(
                    (_, notifications, maxNotificationsPerPublish) =>
                    {
                        publishLimits.Add(maxNotificationsPerPublish);
                        for (int ii = 0; ii < notificationCount; ii++)
                        {
                            notifications.Enqueue(new EventFieldList());
                        }
                        return false;
                    });
            return item;
        }

        private static Mock<IDataChangeMonitoredItem> CreateDataChangeMonitoredItem(
            uint id,
            int notificationCount,
            List<uint> publishLimits)
        {
            var item = new Mock<IDataChangeMonitoredItem>();
            var createResult = new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.Good,
                RevisedSamplingInterval = 0,
                RevisedQueueSize = (uint)notificationCount
            };
            item.SetupGet(i => i.Id).Returns(id);
            item.SetupGet(i => i.IsReadyToPublish).Returns(true);
            item.SetupGet(i => i.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
            item.Setup(i => i.GetCreateResult(out createResult)).Returns(ServiceResult.Good);
            item.Setup(i => i.Publish(
                    It.IsAny<OperationContext>(),
                    It.IsAny<Queue<MonitoredItemNotification>>(),
                    It.IsAny<Queue<DiagnosticInfo>>(),
                    It.IsAny<uint>(),
                    It.IsAny<Microsoft.Extensions.Logging.ILogger>()))
                .Returns<
                    OperationContext,
                    Queue<MonitoredItemNotification>,
                    Queue<DiagnosticInfo>,
                    uint,
                    Microsoft.Extensions.Logging.ILogger>(
                    (_, notifications, diagnostics, maxNotificationsPerPublish, _) =>
                    {
                        publishLimits.Add(maxNotificationsPerPublish);
                        for (int ii = 0; ii < notificationCount; ii++)
                        {
                            notifications.Enqueue(
                                new MonitoredItemNotification { Value = new DataValue(ii) });
                            diagnostics.Enqueue(new DiagnosticInfo());
                        }
                        return false;
                    });
            return item;
        }
    }
}
