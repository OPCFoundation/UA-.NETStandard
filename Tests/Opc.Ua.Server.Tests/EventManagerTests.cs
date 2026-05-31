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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("EventManager")]
    [Parallelizable]
    public class EventManagerTests
    {
        private Mock<IServerInternal> m_mockServer;
        private EventManager m_eventManager;

        [SetUp]
        public void SetUp()
        {
            m_mockServer = new Mock<IServerInternal>();
            m_mockServer.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            m_eventManager = new EventManager(m_mockServer.Object, 1000, 5000);
        }

        [TearDown]
        public void TearDown()
        {
            m_eventManager?.Dispose();
        }

        [Test]
        public void ConstructorThrowsWhenServerNull()
        {
            Assert.That(() => new EventManager(null!, 100, 200), Throws.ArgumentNullException);
        }

        [Test]
        public void GetMonitoredItemsReturnsEmptyListInitially()
        {
            var items = m_eventManager.GetMonitoredItems();

            Assert.That(items, Is.Empty);
        }

        [Test]
        public void DeleteMonitoredItemDoesNotThrowWhenItemNotFound()
        {
            Assert.DoesNotThrow(() => m_eventManager.DeleteMonitoredItem(9999));
        }

        [Test]
        public void DisposeCanBeCalledMultipleTimes()
        {
            m_eventManager.Dispose();
            Assert.DoesNotThrow(() => m_eventManager.Dispose());
        }

        [Test]
        public void ReportEventThrowsWhenEventNull()
        {
#pragma warning disable CS0618 // testing obsolete method
            Assert.That(
                () => EventManager.ReportEvent(null!, new List<IEventMonitoredItem>()),
                Throws.ArgumentNullException);
#pragma warning restore CS0618
        }

        [Test]
        public void ReportEventQueuesEventToAllItems()
        {
            var item1 = new Mock<IEventMonitoredItem>();
            var item2 = new Mock<IEventMonitoredItem>();
            var ev = new Mock<IFilterTarget>();
            IList<IEventMonitoredItem> items = [item1.Object, item2.Object];

#pragma warning disable CS0618 // testing obsolete method
            EventManager.ReportEvent(ev.Object, items);
#pragma warning restore CS0618

            item1.Verify(m => m.QueueEvent(ev.Object), Times.Once);
            item2.Verify(m => m.QueueEvent(ev.Object), Times.Once);
        }

        [Test]
        public async Task ReportEventAsyncThrowsWhenEventNullAsync()
        {
            var nm = new Mock<IAsyncNodeManager>();
            var items = new List<IEventMonitoredItem>();

            await Assert.ThatAsync(
                async () => await EventManager.ReportEventAsync(null!, nm.Object, items).ConfigureAwait(false),
                Throws.ArgumentNullException).ConfigureAwait(false);
        }

        [Test]
        public async Task ReportEventAsyncThrowsWhenNodeManagerNullAsync()
        {
            var ev = new Mock<IFilterTarget>();
            var items = new List<IEventMonitoredItem>();

            await Assert.ThatAsync(
                async () => await EventManager.ReportEventAsync(ev.Object, null!, items).ConfigureAwait(false),
                Throws.ArgumentNullException).ConfigureAwait(false);
        }

        [Test]
        public async Task ReportEventAsyncThrowsWhenMonitoredItemsNullAsync()
        {
            var ev = new Mock<IFilterTarget>();
            var nm = new Mock<IAsyncNodeManager>();

            await Assert.ThatAsync(
                async () => await EventManager.ReportEventAsync(ev.Object, nm.Object, null!).ConfigureAwait(false),
                Throws.ArgumentNullException).ConfigureAwait(false);
        }

        [Test]
        public async Task ReportEventAsyncSkipsNullItemsAsync()
        {
            var nm = new Mock<IAsyncNodeManager>();
            nm.Setup(m => m.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

            var item1 = new Mock<IEventMonitoredItem>();
            var ev = new Mock<IFilterTarget>();
            IList<IEventMonitoredItem> items = [null!, item1.Object];

            await EventManager.ReportEventAsync(ev.Object, nm.Object, items).ConfigureAwait(false);

            item1.Verify(m => m.QueueEvent(ev.Object), Times.Once);
        }

        [Test]
        public async Task ReportEventAsyncSkipsItemsWithBadPermissionAsync()
        {
            var nm = new Mock<IAsyncNodeManager>();
            nm.Setup(m => m.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadUserAccessDenied)));

            var item1 = new Mock<IEventMonitoredItem>();
            var ev = new Mock<IFilterTarget>();
            IList<IEventMonitoredItem> items = [item1.Object];

            await EventManager.ReportEventAsync(ev.Object, nm.Object, items).ConfigureAwait(false);

            item1.Verify(m => m.QueueEvent(It.IsAny<IFilterTarget>()), Times.Never);
        }

        [Test]
        public void CreateMonitoredItemReturnsItemAndAddsToList()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);

            using var eventManager = new EventManager(serverMock.Object, 100, 500);
            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            var idFactory = new MonitoredItemIdFactory();

            var createRequest = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = new NodeId(1), AttributeId = Attributes.EventNotifier },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    SamplingInterval = -1,
                    QueueSize = 50,
                    DiscardOldest = true,
                    ClientHandle = 1,
                    Filter = new ExtensionObject(new EventFilter())
                }
            };

            IEventMonitoredItem item = eventManager.CreateMonitoredItem(
                opContext,
                nodeManagerMock.Object,
                "handle",
                1,
                idFactory,
                TimestampsToReturn.Both,
                1000.0,
                createRequest,
                new EventFilter(),
                false);

            Assert.That(item, Is.Not.Null);
            Assert.That(item.Id, Is.GreaterThan(0u));

            IList<IEventMonitoredItem> items = eventManager.GetMonitoredItems();
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0], Is.SameAs(item));
        }

        [Test]
        public void CreateMonitoredItemCapsQueueSizeForNonDurable()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);

            // maxQueueSize = 10 for non-durable
            using var eventManager = new EventManager(serverMock.Object, 10, 500);
            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            var idFactory = new MonitoredItemIdFactory();

            var createRequest = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = new NodeId(1), AttributeId = Attributes.EventNotifier },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    SamplingInterval = 1000,
                    QueueSize = 9999, // exceeds max
                    DiscardOldest = true,
                    ClientHandle = 1,
                    Filter = new ExtensionObject(new EventFilter())
                }
            };

            IEventMonitoredItem item = eventManager.CreateMonitoredItem(
                opContext,
                nodeManagerMock.Object,
                "handle",
                1,
                idFactory,
                TimestampsToReturn.Both,
                1000.0,
                createRequest,
                new EventFilter(),
                false);

            // The queue size should have been capped to maxQueueSize (10)
            Assert.That(((ISampledDataChangeMonitoredItem)item).QueueSize, Is.EqualTo(10u));
        }

        [Test]
        public void ModifyMonitoredItemModifiesExistingItem()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);

            using var eventManager = new EventManager(serverMock.Object, 100, 500);
            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            var idFactory = new MonitoredItemIdFactory();

            var createRequest = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = new NodeId(1), AttributeId = Attributes.EventNotifier },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    SamplingInterval = 1000,
                    QueueSize = 50,
                    DiscardOldest = true,
                    ClientHandle = 1,
                    Filter = new ExtensionObject(new EventFilter())
                }
            };

            IEventMonitoredItem item = eventManager.CreateMonitoredItem(
                opContext,
                nodeManagerMock.Object,
                "handle",
                1,
                idFactory,
                TimestampsToReturn.Both,
                1000.0,
                createRequest,
                new EventFilter(),
                false);

            var modifyRequest = new MonitoredItemModifyRequest
            {
                RequestedParameters = new MonitoringParameters
                {
                    SamplingInterval = 2000,
                    QueueSize = 25,
                    DiscardOldest = false,
                    ClientHandle = 2,
                    Filter = new ExtensionObject(new EventFilter())
                }
            };

            eventManager.ModifyMonitoredItem(
                opContext,
                item,
                TimestampsToReturn.Source,
                modifyRequest,
                new EventFilter());

            Assert.That(((ISampledDataChangeMonitoredItem)item).QueueSize, Is.EqualTo(25u));
        }

        [Test]
        public void ModifyMonitoredItemDoesNothingWhenItemNotOwned()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);

            using var eventManager = new EventManager(serverMock.Object, 100, 500);
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);

            // Create a mock item with an ID that's not in the event manager
            var mockItem = new Mock<IEventMonitoredItem>();
            mockItem.Setup(m => m.Id).Returns(99999u);

            var modifyRequest = new MonitoredItemModifyRequest
            {
                RequestedParameters = new MonitoringParameters
                {
                    QueueSize = 25,
                    ClientHandle = 2,
                    Filter = new ExtensionObject(new EventFilter())
                }
            };

            // Should not throw
            Assert.DoesNotThrow(() => eventManager.ModifyMonitoredItem(
                opContext,
                mockItem.Object,
                TimestampsToReturn.Source,
                modifyRequest,
                new EventFilter()));

            // The mock item's ModifyAttributes should NOT have been called
            mockItem.Verify(m => m.ModifyAttributes(
                It.IsAny<DiagnosticsMasks>(),
                It.IsAny<TimestampsToReturn>(),
                It.IsAny<uint>(),
                It.IsAny<MonitoringFilter>(),
                It.IsAny<MonitoringFilter>(),
                It.IsAny<Range>(),
                It.IsAny<double>(),
                It.IsAny<uint>(),
                It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public void DeleteMonitoredItemRemovesFromList()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);

            using var eventManager = new EventManager(serverMock.Object, 100, 500);
            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            var idFactory = new MonitoredItemIdFactory();

            var createRequest = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = new NodeId(1), AttributeId = Attributes.EventNotifier },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    SamplingInterval = 1000,
                    QueueSize = 50,
                    DiscardOldest = true,
                    ClientHandle = 1,
                    Filter = new ExtensionObject(new EventFilter())
                }
            };

            IEventMonitoredItem item = eventManager.CreateMonitoredItem(
                opContext,
                nodeManagerMock.Object,
                "handle",
                1,
                idFactory,
                TimestampsToReturn.Both,
                1000.0,
                createRequest,
                new EventFilter(),
                false);

            eventManager.DeleteMonitoredItem(item.Id);

            IList<IEventMonitoredItem> items = eventManager.GetMonitoredItems();
            Assert.That(items, Is.Empty);
        }

        [Test]
        public void CreateMonitoredItemUsesPublishingIntervalWhenSamplingNegative()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);

            using var eventManager = new EventManager(serverMock.Object, 100, 500);
            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            var idFactory = new MonitoredItemIdFactory();

            var createRequest = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = new NodeId(1), AttributeId = Attributes.EventNotifier },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    SamplingInterval = -1, // negative means use publishing interval
                    QueueSize = 50,
                    DiscardOldest = true,
                    ClientHandle = 1,
                    Filter = new ExtensionObject(new EventFilter())
                }
            };

            IEventMonitoredItem item = eventManager.CreateMonitoredItem(
                opContext,
                nodeManagerMock.Object,
                "handle",
                1,
                idFactory,
                TimestampsToReturn.Both,
                2000.0, // publishing interval
                createRequest,
                new EventFilter(),
                false);

            Assert.That(item, Is.Not.Null);
            Assert.That(((ISampledDataChangeMonitoredItem)item).SamplingInterval, Is.EqualTo(2000.0));
        }
    }
}
