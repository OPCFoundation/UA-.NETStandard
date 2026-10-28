using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Queue size revision of event monitored items in <see cref="EventManager"/>.
    /// </summary>
    /// <remarks>
    /// Part 4 §7.21: for event monitored items a requested queueSize of 0 means
    /// the server default and 1 the minimum queue size the server requires for
    /// Event Notifications. Neither may be taken literally, otherwise a burst of
    /// events (for example the OpenSecureChannel, CreateSession and
    /// ActivateSession audit events of one connect) collapses to the last event
    /// before the next publish.
    /// </remarks>
    [TestFixture]
    [Category("EventManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EventManagerTests
    {
        [TestCase(0u)]
        [TestCase(1u)]
        public void CreateMonitoredItemRevisesSpecialEventQueueSizes(uint requestedQueueSize)
        {
            EventManager manager = CreateManager(10000, 10000, out INodeManager nodeManager);

            IEventMonitoredItem item = CreateItem(manager, nodeManager, requestedQueueSize);

            Assert.That(((MonitoredItem)item).QueueSize, Is.EqualTo(EventManager.DefaultEventQueueSize));
        }

        /// <summary>
        /// The server default is still limited by the configured maximum, and a
        /// configured maximum of 1 is honored rather than read as another
        /// queueSize 1 request.
        /// </summary>
        [TestCase(0u, 3u)]
        [TestCase(1u, 3u)]
        [TestCase(0u, 1u)]
        [TestCase(1u, 1u)]
        public void CreateMonitoredItemClampsDefaultEventQueueSizeToMax(
            uint requestedQueueSize,
            uint maxQueueSize)
        {
            EventManager manager = CreateManager(maxQueueSize, 10000, out INodeManager nodeManager);

            IEventMonitoredItem item = CreateItem(manager, nodeManager, requestedQueueSize);

            Assert.That(((MonitoredItem)item).QueueSize, Is.EqualTo(maxQueueSize));
        }

        [Test]
        public void CreateMonitoredItemKeepsOtherEventQueueSizes()
        {
            EventManager manager = CreateManager(10000, 10000, out INodeManager nodeManager);

            IEventMonitoredItem item = CreateItem(manager, nodeManager, 5);

            Assert.That(((MonitoredItem)item).QueueSize, Is.EqualTo(5));
        }

        [Test]
        public void ModifyMonitoredItemRevisesEventQueueSizeOne()
        {
            EventManager manager = CreateManager(10000, 10000, out INodeManager nodeManager);
            IEventMonitoredItem item = CreateItem(manager, nodeManager, 5);

            manager.ModifyMonitoredItem(
                NewContext(),
                item,
                TimestampsToReturn.Both,
                new MonitoredItemModifyRequest
                {
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 42,
                        SamplingInterval = 1000.0,
                        QueueSize = 1,
                        DiscardOldest = true
                    }
                },
                new EventFilter());

            Assert.That(((MonitoredItem)item).QueueSize, Is.EqualTo(EventManager.DefaultEventQueueSize));
        }

        private static EventManager CreateManager(
            uint maxQueueSize,
            uint maxDurableQueueSize,
            out INodeManager nodeManager)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            serverMock.Setup(s => s.MonitoredItemQueueFactory)
                .Returns(new MonitoredItemQueueFactory(telemetry));
            nodeManager = new Mock<INodeManager>().Object;
            return new EventManager(serverMock.Object, maxQueueSize, maxDurableQueueSize);
        }

        private static IEventMonitoredItem CreateItem(
            EventManager manager,
            INodeManager nodeManager,
            uint queueSize)
        {
            return manager.CreateMonitoredItem(
                NewContext(),
                nodeManager,
                null,
                1,
                new MonitoredItemIdFactory(),
                TimestampsToReturn.Both,
                1000.0,
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId(),
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 42,
                        SamplingInterval = 1000.0,
                        QueueSize = queueSize,
                        DiscardOldest = true
                    }
                },
                new EventFilter(),
                false);
        }

        private static OperationContext NewContext()
        {
            return new OperationContext(new RequestHeader(), null, RequestType.CreateMonitoredItems);
        }
    }
}
