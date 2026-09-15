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
using Moq;
using NUnit.Framework;

// CA2000: test code; disposables are ownership-transferred to the EventManager
// under test or are torn down with the fixture.
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Deterministic, offline unit tests for <see cref="EventManager"/>.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("EventManager")]
    [Parallelizable(ParallelScope.All)]
    public class EventManagerTests
    {
        private static OperationContext NewContext()
        {
            return new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
        }

        private static MonitoredItemCreateRequest NewCreateRequest(
            double samplingInterval,
            uint queueSize)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId(),
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 42,
                    SamplingInterval = samplingInterval,
                    QueueSize = queueSize,
                    DiscardOldest = true
                }
            };
        }

        private static EventManager CreateManager(
            uint maxQueueSize,
            uint maxDurableQueueSize,
            out Mock<IServerInternal> mockServer,
            out Mock<IAsyncNodeManager> mockNodeManager)
        {
            mockServer = DeterministicServerMock.Create(out _);
            mockNodeManager = new Mock<IAsyncNodeManager>();
            return new EventManager(mockServer.Object, maxQueueSize, maxDurableQueueSize);
        }

        [Test]
        public void ConstructorWithNullServerThrows()
        {
            Assert.That(
                () => new EventManager(null!, 10, 10),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void CreateMonitoredItemAddsItemToManager()
        {
            EventManager manager = CreateManager(100, 100, out _, out Mock<IAsyncNodeManager> nm);
            var idFactory = new MonitoredItemIdFactory();

            IEventMonitoredItem item = manager.CreateMonitoredItem(
                NewContext(),
                nm.Object,
                null!,
                1,
                idFactory,
                TimestampsToReturn.Both,
                1000.0,
                NewCreateRequest(1000.0, 5),
                new EventFilter(),
                false);

            Assert.That(item, Is.Not.Null);
            Assert.That(manager.GetMonitoredItems(), Has.Count.EqualTo(1));
            Assert.That(manager.GetMonitoredItems()[0].Id, Is.EqualTo(item.Id));
        }

        [Test]
        public void CreateMonitoredItemClampsQueueSizeToMax()
        {
            EventManager manager = CreateManager(3, 3, out _, out Mock<IAsyncNodeManager> nm);
            var idFactory = new MonitoredItemIdFactory();

            IEventMonitoredItem item = manager.CreateMonitoredItem(
                NewContext(),
                nm.Object,
                null!,
                1,
                idFactory,
                TimestampsToReturn.Both,
                1000.0,
                NewCreateRequest(1000.0, 1000),
                new EventFilter(),
                false);

            Assert.That(item, Is.InstanceOf<MonitoredItem>());
            Assert.That(((MonitoredItem)item).QueueSize, Is.EqualTo(3));
        }

        /// <summary>
        /// Part 4 §7.21: for event monitored items a requested queueSize of 0
        /// means the server default and 1 means the minimum queue size the
        /// server requires for Event Notifications. Neither may be taken
        /// literally, otherwise a burst of events (for example the
        /// OpenSecureChannel, CreateSession and ActivateSession audit events of
        /// one connect) collapses to the last event before the next publish.
        /// </summary>
        [TestCase(0u)]
        [TestCase(1u)]
        public void CreateMonitoredItemRevisesSpecialEventQueueSizes(uint requestedQueueSize)
        {
            EventManager manager = CreateManager(10000, 10000, out _, out Mock<IAsyncNodeManager> nm);
            var idFactory = new MonitoredItemIdFactory();

            IEventMonitoredItem item = manager.CreateMonitoredItem(
                NewContext(), nm.Object, null!, 1, idFactory,
                TimestampsToReturn.Both, 1000.0, NewCreateRequest(1000.0, requestedQueueSize),
                new EventFilter(), false);

            Assert.That(
                ((MonitoredItem)item).QueueSize,
                Is.EqualTo(EventManager.DefaultEventQueueSize));
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
            EventManager manager = CreateManager(
                maxQueueSize,
                10000,
                out _,
                out Mock<IAsyncNodeManager> nm);
            var idFactory = new MonitoredItemIdFactory();

            IEventMonitoredItem item = manager.CreateMonitoredItem(
                NewContext(), nm.Object, null!, 1, idFactory,
                TimestampsToReturn.Both, 1000.0, NewCreateRequest(1000.0, requestedQueueSize),
                new EventFilter(), false);

            Assert.That(((MonitoredItem)item).QueueSize, Is.EqualTo(maxQueueSize));
        }

        [Test]
        public void ModifyMonitoredItemRevisesEventQueueSizeOne()
        {
            EventManager manager = CreateManager(10000, 10000, out _, out Mock<IAsyncNodeManager> nm);
            var idFactory = new MonitoredItemIdFactory();

            IEventMonitoredItem item = manager.CreateMonitoredItem(
                NewContext(), nm.Object, null!, 1, idFactory,
                TimestampsToReturn.Both, 1000.0, NewCreateRequest(1000.0, 5),
                new EventFilter(), false);

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

            Assert.That(
                ((MonitoredItem)item).QueueSize,
                Is.EqualTo(EventManager.DefaultEventQueueSize));
        }

        /// <summary>
        /// Regression for the CTT "Auditing Connections" failures: the CTT audit
        /// subscription requests QueueSize 1 on Server.EventNotifier. Every event
        /// raised between two publishes must still be delivered.
        /// </summary>
        [Test]
        public void EventBurstIsNotCollapsedWhenQueueSizeOneRequested()
        {
            EventManager manager = CreateManager(10000, 10000, out _, out Mock<IAsyncNodeManager> nm);
            var idFactory = new MonitoredItemIdFactory();

            IEventMonitoredItem item = manager.CreateMonitoredItem(
                NewContext(), nm.Object, null!, 1, idFactory,
                TimestampsToReturn.Both, 1000.0, NewCreateRequest(1000.0, 1),
                new EventFilter(), false);

            for (int ii = 0; ii < 3; ii++)
            {
                ((MonitoredItem)item).QueueEvent(new EventFieldList
                {
                    ClientHandle = 42,
                    EventFields = [new Variant("event" + ii)]
                });
            }

            var notifications = new Queue<EventFieldList>();
            ((MonitoredItem)item).Publish(NewContext(), notifications, 100);

            // an overflowing queue would drop events and append an
            // EventQueueOverflowEvent, so compare the exact payloads.
            Assert.That(
                notifications.Select(n => n.EventFields[0].GetString()),
                Is.EqualTo(new[] { "event0", "event1", "event2" }));
        }

        /// <summary>
        /// A durable event queue persisted with the literal size 1 must be resized
        /// to the revised size when the monitored item is restored.
        /// </summary>
        [Test]
        public void RestoreMonitoredItemResizesRestoredEventQueue()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using var manager = new EventManager(server.Object, 10000, 10000);

            IEventMonitoredItemQueue persisted = queueFactory.CreateEventQueue(false, 7);
            persisted.SetQueueSize(1, true);
            persisted.Enqueue(new EventFieldList
            {
                ClientHandle = 42,
                EventFields = [new Variant("event0")]
            });

            var stored = new StoredMonitoredItem
            {
                Id = 7,
                SubscriptionId = 1,
                TypeMask = MonitoredItemTypeMask.Events,
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = ObjectIds.Server,
                AttributeId = Attributes.EventNotifier,
                ClientHandle = 42,
                QueueSize = 1,
                DiscardOldest = true,
                SamplingInterval = 0,
                TimestampsToReturn = TimestampsToReturn.Both,
                DiagnosticsMasks = DiagnosticsMasks.None,
                OriginalFilter = new EventFilter(),
                FilterToUse = new EventFilter(),
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null,
                RestoredEventQueue = persisted
            };

            var item = (MonitoredItem)manager.RestoreMonitoredItem(
                new Mock<IAsyncNodeManager>().Object,
                null!,
                stored);

            Assert.That(item.QueueSize, Is.EqualTo(EventManager.DefaultEventQueueSize));

            for (int ii = 1; ii < 3; ii++)
            {
                item.QueueEvent(new EventFieldList
                {
                    ClientHandle = 42,
                    EventFields = [new Variant("event" + ii)]
                });
            }

            var notifications = new Queue<EventFieldList>();
            item.Publish(NewContext(), notifications, 100);

            Assert.That(
                notifications.Select(n => n.EventFields[0].GetString()),
                Is.EqualTo(new[] { "event0", "event1", "event2" }));
        }

        [Test]
        public void CreateMonitoredItemUsesPublishingIntervalWhenSamplingNegative()
        {
            EventManager manager = CreateManager(100, 100, out _, out Mock<IAsyncNodeManager> nm);
            var idFactory = new MonitoredItemIdFactory();

            IEventMonitoredItem item = manager.CreateMonitoredItem(
                NewContext(),
                nm.Object,
                null!,
                1,
                idFactory,
                TimestampsToReturn.Both,
                2500.0,
                NewCreateRequest(-1.0, 5),
                new EventFilter(),
                false);

            Assert.That(item.SamplingInterval, Is.EqualTo(2500.0));
        }

        [Test]
        public void CreateMonitoredItemAssignsDistinctIds()
        {
            EventManager manager = CreateManager(100, 100, out _, out Mock<IAsyncNodeManager> nm);
            var idFactory = new MonitoredItemIdFactory();

            IEventMonitoredItem first = manager.CreateMonitoredItem(
                NewContext(), nm.Object, null!, 1, idFactory,
                TimestampsToReturn.Both, 1000.0, NewCreateRequest(1000.0, 5),
                new EventFilter(), false);
            IEventMonitoredItem second = manager.CreateMonitoredItem(
                NewContext(), nm.Object, null!, 1, idFactory,
                TimestampsToReturn.Both, 1000.0, NewCreateRequest(1000.0, 5),
                new EventFilter(), false);

            Assert.That(first.Id, Is.Not.EqualTo(second.Id));
            Assert.That(manager.GetMonitoredItems(), Has.Count.EqualTo(2));
        }

        [Test]
        public void DeleteMonitoredItemRemovesItem()
        {
            EventManager manager = CreateManager(100, 100, out _, out Mock<IAsyncNodeManager> nm);
            var idFactory = new MonitoredItemIdFactory();

            IEventMonitoredItem item = manager.CreateMonitoredItem(
                NewContext(), nm.Object, null!, 1, idFactory,
                TimestampsToReturn.Both, 1000.0, NewCreateRequest(1000.0, 5),
                new EventFilter(), false);

            manager.DeleteMonitoredItem(item.Id);

            Assert.That(manager.GetMonitoredItems(), Is.Empty);
        }

        [Test]
        public void ModifyMonitoredItemIgnoresForeignItem()
        {
            EventManager manager = CreateManager(100, 100, out _, out _);
            var foreign = new Mock<IEventMonitoredItem>();
            foreign.SetupGet(m => m.Id).Returns(9999);
            foreign.SetupGet(m => m.IsDurable).Returns(false);

            Assert.DoesNotThrow(() => manager.ModifyMonitoredItem(
                NewContext(),
                foreign.Object,
                TimestampsToReturn.Both,
                new MonitoredItemModifyRequest
                {
                    RequestedParameters = new MonitoringParameters { QueueSize = 5 }
                },
                new EventFilter()));

            foreign.Verify(
                m => m.ModifyAttributes(
                    It.IsAny<DiagnosticsMasks>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<uint>(),
                    It.IsAny<MonitoringFilter>(),
                    It.IsAny<MonitoringFilter>(),
                    It.IsAny<Range>(),
                    It.IsAny<double>(),
                    It.IsAny<uint>(),
                    It.IsAny<bool>()),
                Times.Never);
        }

        [Test]
        public void ModifyMonitoredItemUpdatesOwnedItem()
        {
            EventManager manager = CreateManager(100, 100, out _, out Mock<IAsyncNodeManager> nm);
            var idFactory = new MonitoredItemIdFactory();

            IEventMonitoredItem item = manager.CreateMonitoredItem(
                NewContext(), nm.Object, null!, 1, idFactory,
                TimestampsToReturn.Both, 1000.0, NewCreateRequest(1000.0, 5),
                new EventFilter(), false);

            Assert.DoesNotThrow(() => manager.ModifyMonitoredItem(
                NewContext(),
                item,
                TimestampsToReturn.Both,
                new MonitoredItemModifyRequest
                {
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 7,
                        SamplingInterval = 2000.0,
                        QueueSize = 8,
                        DiscardOldest = false
                    }
                },
                new EventFilter()));

            Assert.That(((MonitoredItem)item).QueueSize, Is.EqualTo(8));
        }

        [Test]
        public void GetMonitoredItemsReturnsSnapshot()
        {
            EventManager manager = CreateManager(100, 100, out _, out Mock<IAsyncNodeManager> nm);
            var idFactory = new MonitoredItemIdFactory();

            manager.CreateMonitoredItem(
                NewContext(), nm.Object, null!, 1, idFactory,
                TimestampsToReturn.Both, 1000.0, NewCreateRequest(1000.0, 5),
                new EventFilter(), false);

            IList<IEventMonitoredItem> first = manager.GetMonitoredItems();
            first.Clear();

            Assert.That(manager.GetMonitoredItems(), Has.Count.EqualTo(1));
        }

        [Test]
        public void DisposeClearsMonitoredItems()
        {
            EventManager manager = CreateManager(100, 100, out _, out Mock<IAsyncNodeManager> nm);
            var idFactory = new MonitoredItemIdFactory();

            manager.CreateMonitoredItem(
                NewContext(), nm.Object, null!, 1, idFactory,
                TimestampsToReturn.Both, 1000.0, NewCreateRequest(1000.0, 5),
                new EventFilter(), false);

            manager.Dispose();

            Assert.That(manager.GetMonitoredItems(), Is.Empty);
        }

        [Test]
        public void ReportEventWithNullEventThrows()
        {
#pragma warning disable CS0618 // testing the obsolete overload deliberately
            Assert.That(
                () => EventManager.ReportEvent(null!, []),
                Throws.TypeOf<ArgumentNullException>());
#pragma warning restore CS0618
        }

        [Test]
        public void ReportEventQueuesEventToEachItem()
        {
            var filterTarget = new Mock<IFilterTarget>();
            var item1 = new Mock<IEventMonitoredItem>();
            var item2 = new Mock<IEventMonitoredItem>();

#pragma warning disable CS0618 // testing the obsolete overload deliberately
            EventManager.ReportEvent(
                filterTarget.Object,
                [item1.Object, item2.Object]);
#pragma warning restore CS0618

            item1.Verify(m => m.QueueEvent(filterTarget.Object), Times.Once);
            item2.Verify(m => m.QueueEvent(filterTarget.Object), Times.Once);
        }
    }
}
