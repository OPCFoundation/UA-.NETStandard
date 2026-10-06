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
        private static readonly int[] s_composedEventValues = [10, 11, 12, 13, 20];
        private static readonly int[] s_firstTransformedEventValue = [11];
        private static readonly int[] s_lastTransformedEventValue = [4];

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

        [Test]
        public void DataChangeOverflowBitSurvivesQueueRestoreWhenNotDiscardingOldest()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var queue = new DataChangeMonitoredItemQueue(false, 1, telemetry);

            using (var handler = new DataChangeQueueHandler(queue, false, 0, telemetry))
            {
                handler.SetQueueSize(2, false, DiagnosticsMasks.All);
                handler.QueueValue(new DataValue(new Variant(1)), ServiceResult.Good);
                handler.QueueValue(new DataValue(new Variant(2)), ServiceResult.Good);
                // the queue is full: 3 replaces 2 and reports the loss.
                Assert.That(
                    handler.QueueValue(new DataValue(new Variant(3)), ServiceResult.Good),
                    Is.True);
            }

            // a server restart hands the persisted queue to a new handler.
            var restored = new DataChangeMonitoredItemQueue(false, 1, telemetry);
            restored.ResetQueue(2, true);
            while (queue.Dequeue(out DataValue value, out ServiceResult error))
            {
                restored.Enqueue(value, error);
            }

            using var restoredHandler = new DataChangeQueueHandler(restored, false, 0, telemetry);
            Assert.That(restoredHandler.PublishSingleValue(out DataValue first, out _), Is.True);
            Assert.That(first.WrappedValue, Is.EqualTo(new Variant(1)));
            Assert.That(first.StatusCode.Overflow, Is.False);
            Assert.That(restoredHandler.PublishSingleValue(out DataValue second, out _), Is.True);
            Assert.That(second.WrappedValue, Is.EqualTo(new Variant(3)));
            Assert.That(second.StatusCode.Overflow, Is.True);
        }

        /// <summary>
        /// A durable queue transiently returns no event while it restores a persisted batch.
        /// Queued events are brought in line with a modification when they are published, so
        /// such a queue can neither reorder them nor leave some in the old layout.
        /// </summary>
        [Test]
        public void EventTransformKeepsOrderWhenDequeueFailsTransiently()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queue = new FlakyEventQueue(telemetry);
            queue.SetQueueSize(10, true);
            using var handler = new EventQueueHandler(queue, true, telemetry);
            for (int i = 0; i < 4; i++)
            {
                handler.QueueEvent(NewEvent(i));
            }

            queue.FailEveryOtherDequeue = true;
            handler.TransformQueuedEvents(fields => NewEvent(fields.EventFields[0].GetInt32() + 10));
            handler.QueueEvent(NewEvent(20));

            var notifications = new Queue<EventFieldList>();
            for (int attempt = 0; attempt < 20 && handler.ItemsInQueue > 0; attempt++)
            {
                handler.Publish(null!, notifications, 10);
            }

            Assert.That(
                notifications.Select(n => n.EventFields[0].GetInt32()),
                Is.EqualTo(s_composedEventValues));
            Assert.That(handler.Overflow, Is.False);
        }

        /// <summary>
        /// Transforms of successive modifications compose for the events queued before the
        /// first one, and an event the transform drops is reported by an overflow event at
        /// its position (discardOldest FALSE).
        /// </summary>
        [Test]
        public void EventTransformsComposeAndReportDroppedEventsInPlace()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queue = new EventMonitoredItemQueue(false, 1, telemetry);
            queue.SetQueueSize(10, false);
            using var handler = new EventQueueHandler(queue, false, telemetry);
            handler.QueueEvent(NewEvent(1));
            handler.QueueEvent(NewEvent(2));
            handler.TransformQueuedEvents(fields => NewEvent(fields.EventFields[0].GetInt32() * 10));
            handler.QueueEvent(NewEvent(3));
            handler.TransformQueuedEvents(fields =>
                fields.EventFields[0].GetInt32() == 20 ? null : NewEvent(fields.EventFields[0].GetInt32() + 1));

            var notifications = new Queue<EventFieldList>();
            handler.Publish(null!, notifications, 10, out bool overflowEventDue);

            // 1 -> 10 -> 11 is published, 2 -> 20 is dropped, the overflow event follows 11.
            Assert.That(notifications.Select(n => n.EventFields[0].GetInt32()), Is.EqualTo(s_firstTransformedEventValue));
            Assert.That(overflowEventDue, Is.True);

            notifications.Clear();
            handler.Publish(null!, notifications, 10, out overflowEventDue);
            Assert.That(notifications.Select(n => n.EventFields[0].GetInt32()), Is.EqualTo(s_lastTransformedEventValue));
            Assert.That(overflowEventDue, Is.False);
            Assert.That(handler.Overflow, Is.False);
        }

        /// <summary>
        /// With discardOldest FALSE the overflow event follows the events queued before the
        /// loss. A dequeue that transiently fails must not hand it out ahead of them.
        /// </summary>
        [Test]
        public void EventOverflowStaysBehindTransientlyFailingDequeue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queue = new FlakyEventQueue(telemetry);
            queue.SetQueueSize(2, false);
            using var handler = new EventQueueHandler(queue, false, telemetry);
            handler.QueueEvent(NewEvent(1));
            handler.QueueEvent(NewEvent(2));
            Assert.That(handler.SetQueueOverflowIfFull(), Is.True);

            queue.FailedDequeues = 1;
            var notifications = new Queue<EventFieldList>();
            Assert.That(handler.Publish(null!, notifications, 10, out bool due), Is.Zero);
            Assert.That(due, Is.False, "the events queued before the loss are still queued");
            Assert.That(handler.Overflow, Is.True);

            Assert.That(handler.Publish(null!, notifications, 10, out due), Is.EqualTo(2u));
            Assert.That(due, Is.True);
            Assert.That(handler.Overflow, Is.False);
        }

        private static EventFieldList NewEvent(int value)
        {
            return new EventFieldList { EventFields = [new Variant(value)] };
        }

        /// <summary>
        /// An in-memory event queue whose dequeue fails on demand, the way a durable queue
        /// does while it restores a persisted batch.
        /// </summary>
        private sealed class FlakyEventQueue : EventMonitoredItemQueue
        {
            public FlakyEventQueue(ITelemetryContext telemetry)
                : base(false, 1, telemetry)
            {
            }

            public int FailedDequeues { get; set; }

            public bool FailEveryOtherDequeue { get; set; }

            public override bool Dequeue(out EventFieldList value)
            {
                if (FailedDequeues > 0)
                {
                    FailedDequeues--;
                    value = null!;
                    return false;
                }

                if (FailEveryOtherDequeue)
                {
                    m_failNext = !m_failNext;
                    if (m_failNext)
                    {
                        value = null!;
                        return false;
                    }
                }

                return base.Dequeue(out value);
            }

            private bool m_failNext;
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
