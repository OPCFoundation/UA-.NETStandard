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

using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for the in-memory monitored item queues and their handlers.
    /// </summary>
    [TestFixture]
    [Category("MonitoredItem")]
    [Parallelizable]
    public class MonitoredItemQueueHandlerTests
    {
        [Test]
        public void EventQueueDuplicateCheckScansNewestEntries()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queue = new EventMonitoredItemQueue(false, 1, telemetry);
            queue.SetQueueSize(5000, true);

            for (int i = 0; i < 1500; i++)
            {
                queue.Enqueue(new EventFieldList
                {
                    EventFields = [new Variant(i)],
                    Handle = new AuditSessionEventState(null)
                });
            }

            var newest = new AuditSessionEventState(null);
            queue.Enqueue(new EventFieldList { EventFields = [new Variant(true)], Handle = newest });

            Assert.That(queue.IsEventContainedInQueue(newest), Is.True);
            Assert.That(
                queue.IsEventContainedInQueue(new AuditSessionEventState(null)),
                Is.False);
        }

        [Test]
        public void EventOverflowKeepsPositionWhenNotDiscardingOldest()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            int discarded = 0;
            int overflowEvents = 0;
            using var handler = new EventQueueHandler(
                false,
                new MonitoredItemQueueFactory(telemetry),
                1,
                telemetry,
                () => discarded++,
                () => overflowEvents++);
            handler.SetQueueSize(3, false);

            EventFieldList e1 = NewEvent(1);
            EventFieldList e2 = NewEvent(2);
            EventFieldList e3 = NewEvent(3);
            EventFieldList e5 = NewEvent(5);
            handler.QueueEvent(e1);
            handler.QueueEvent(e2);
            handler.QueueEvent(e3);

            // e4 is lost.
            Assert.That(handler.SetQueueOverflowIfFull(), Is.True);
            Assert.That(handler.Overflow, Is.True);

            var notifications = new Queue<EventFieldList>();
            Assert.That(handler.Publish(null!, notifications, 2), Is.EqualTo(2u));
            Assert.That(notifications, Is.EqualTo(new[] { e1, e2 }));
            Assert.That(handler.Overflow, Is.True);

            // e5 arrives after the loss and must be published after the overflow event.
            Assert.That(handler.SetQueueOverflowIfFull(), Is.False);
            handler.QueueEvent(e5);

            notifications.Clear();
            Assert.That(handler.Publish(null!, notifications, 2), Is.EqualTo(1u));
            Assert.That(notifications, Is.EqualTo(new[] { e3 }));
            // the caller appends the overflow event here, as there is room left.
            Assert.That(handler.Overflow, Is.False);

            notifications.Clear();
            Assert.That(handler.Publish(null!, notifications, 2), Is.EqualTo(1u));
            Assert.That(notifications, Is.EqualTo(new[] { e5 }));

            Assert.That(discarded, Is.EqualTo(1));
            Assert.That(overflowEvents, Is.EqualTo(1));
        }

        [Test]
        public void EventOverflowAtPublishLimitIsKeptForNextPublish()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var handler = new EventQueueHandler(
                false,
                new MonitoredItemQueueFactory(telemetry),
                1,
                telemetry);
            handler.SetQueueSize(2, false);
            handler.QueueEvent(NewEvent(1));
            handler.QueueEvent(NewEvent(2));
            Assert.That(handler.SetQueueOverflowIfFull(), Is.True);

            var notifications = new Queue<EventFieldList>();
            Assert.That(handler.Publish(null!, notifications, 2), Is.EqualTo(2u));
            // the overflow event did not fit, so it is still pending.
            Assert.That(handler.Overflow, Is.True);

            notifications.Clear();
            Assert.That(handler.Publish(null!, notifications, 2), Is.Zero);
            Assert.That(handler.Overflow, Is.False);
        }

        [Test]
        public void EventOverflowGeneratesOneOverflowEventPerLoss()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            int discarded = 0;
            int overflowEvents = 0;
            using var handler = new EventQueueHandler(
                false,
                new MonitoredItemQueueFactory(telemetry),
                1,
                telemetry,
                () => discarded++,
                () => overflowEvents++);
            handler.SetQueueSize(2, true);

            for (int i = 0; i < 5; i++)
            {
                handler.QueueEvent(NewEvent(i));
            }

            Assert.That(discarded, Is.EqualTo(3));
            Assert.That(overflowEvents, Is.EqualTo(1));
            Assert.That(handler.Overflow, Is.True);

            var notifications = new Queue<EventFieldList>();
            handler.Publish(null!, notifications, 10);
            Assert.That(handler.Overflow, Is.False);

            handler.QueueEvent(NewEvent(5));
            handler.QueueEvent(NewEvent(6));
            handler.QueueEvent(NewEvent(7));

            Assert.That(discarded, Is.EqualTo(4));
            Assert.That(overflowEvents, Is.EqualTo(2));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void EventQueueShrinkSignalsOverflow(bool discardOldest)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            int discarded = 0;
            int overflowEvents = 0;
            using var handler = new EventQueueHandler(
                false,
                new MonitoredItemQueueFactory(telemetry),
                1,
                telemetry,
                () => discarded++,
                () => overflowEvents++);
            handler.SetQueueSize(10, discardOldest);
            for (int i = 0; i < 10; i++)
            {
                handler.QueueEvent(NewEvent(i));
            }

            Assert.That(handler.Overflow, Is.False);

            handler.SetQueueSize(4, discardOldest);

            Assert.That(handler.ItemsInQueue, Is.EqualTo(4));
            Assert.That(handler.Overflow, Is.True);
            Assert.That(discarded, Is.EqualTo(6));
            Assert.That(overflowEvents, Is.EqualTo(1));

            var notifications = new Queue<EventFieldList>();
            Assert.That(handler.Publish(null!, notifications, 10), Is.EqualTo(4u));
            Assert.That(handler.Overflow, Is.False);
        }

        [Test]
        public void MonitoredItemPublishesOverflowEventAtPositionOfLoss()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using MonitoredItem monitoredItem = CreateEventMonitoredItem(telemetry, 3, false);

            var e1 = new AuditUrlMismatchEventState(null);
            var e2 = new AuditUrlMismatchEventState(null);
            var e3 = new AuditUrlMismatchEventState(null);
            var e4 = new AuditUrlMismatchEventState(null);
            var e5 = new AuditUrlMismatchEventState(null);
            monitoredItem.QueueEvent(e1);
            monitoredItem.QueueEvent(e2);
            monitoredItem.QueueEvent(e3);
            monitoredItem.QueueEvent(e4);

            var published = new List<object>();
            var notifications = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), notifications, 2);
            published.AddRange(notifications.Select(n => n.Handle));

            monitoredItem.QueueEvent(e5);

            for (int i = 0; i < 3; i++)
            {
                notifications.Clear();
                monitoredItem.Publish(new OperationContext(monitoredItem), notifications, 2);
                published.AddRange(notifications.Select(n => n.Handle));
            }

            Assert.That(published, Has.Count.EqualTo(5));
            Assert.That(published[0], Is.SameAs(e1));
            Assert.That(published[1], Is.SameAs(e2));
            Assert.That(published[2], Is.SameAs(e3));
            Assert.That(published[3], Is.AssignableTo<EventQueueOverflowEventState>());
            Assert.That(published[4], Is.SameAs(e5));
        }

        private static EventFieldList NewEvent(int value)
        {
            return new EventFieldList { EventFields = [new Variant(value)] };
        }

        private static MonitoredItem CreateEventMonitoredItem(
            ITelemetryContext telemetry,
            uint queueSize,
            bool discardOldest)
        {
            var filter = new EventFilter();
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.MonitoredItemQueueFactory)
                .Returns(new MonitoredItemQueueFactory(telemetry));
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));

            return new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                null,
                1,
                2,
                new ReadValueId(),
                DiagnosticsMasks.All,
                TimestampsToReturn.Server,
                MonitoringMode.Reporting,
                3,
                filter,
                filter,
                null,
                1000.0,
                queueSize,
                discardOldest,
                1000,
                false);
        }
    }
}
