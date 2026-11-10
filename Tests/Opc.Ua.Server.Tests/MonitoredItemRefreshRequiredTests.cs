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
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// CTT Base Info EventQueueOverflow EventType / 001: after an event queue overflow
    /// that lost Condition events the Server sends a RefreshRequiredEventType event.
    /// </summary>
    /// <remarks>
    /// OPC 10000-9 5.11.4 RefreshRequiredEventType: "When a Server detects an Event
    /// queue overflow, it shall track if any Condition Events have been lost, if any
    /// Condition Events were lost, it shall issue a RefreshRequiredEventType Event to
    /// the Client after the Event queue is no longer in an overflow state."
    /// Condition events are queued as <see cref="InstanceStateSnapshot"/> of a
    /// <see cref="ConditionState"/> (ConditionState.ReportStateChange).
    /// </remarks>
    [TestFixture]
    [Category("MonitoredItem")]
    [Parallelizable]
    public class MonitoredItemRefreshRequiredTests
    {
        /// <summary>
        /// DiscardOldest: the oldest event, a Condition event, is discarded. The publish
        /// returns the overflow event first, the remaining events, and then the
        /// RefreshRequired event once the queue has left the overflow state.
        /// </summary>
        [Test]
        public void DiscardOldestLosingConditionEventQueuesRefreshRequired()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, 2, discardOldest: true);

            monitoredItem.QueueEvent(CreateConditionEvent(telemetry));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 10);

            object[] handles = [.. result.Select(r => r.Handle)];
            Assert.That(handles, Has.Length.EqualTo(4));
            Assert.That(handles[0], Is.AssignableTo<EventQueueOverflowEventState>());
            Assert.That(handles[1], Is.AssignableTo<AuditUrlMismatchEventState>());
            Assert.That(handles[2], Is.AssignableTo<AuditUrlMismatchEventState>());
            Assert.That(handles[3], Is.AssignableTo<RefreshRequiredEventState>());
        }

        /// <summary>
        /// No DiscardOldest: the incoming Condition event is dropped. The overflow event
        /// and then the RefreshRequired event follow the queued events.
        /// </summary>
        [Test]
        public void NoDiscardLosingConditionEventQueuesRefreshRequired()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, 2, discardOldest: false);

            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AlarmConditionState(null));

            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 10);

            object[] handles = [.. result.Select(r => r.Handle)];
            Assert.That(handles, Has.Length.EqualTo(4));
            Assert.That(handles[0], Is.AssignableTo<AuditUrlMismatchEventState>());
            Assert.That(handles[1], Is.AssignableTo<AuditUrlMismatchEventState>());
            Assert.That(handles[2], Is.AssignableTo<EventQueueOverflowEventState>());
            Assert.That(handles[3], Is.AssignableTo<RefreshRequiredEventState>());
        }

        /// <summary>
        /// An overflow that lost only non-Condition events needs no ConditionRefresh:
        /// the publish is unchanged (overflow event plus the queued events).
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void OverflowWithoutLostConditionEventsDoesNotQueueRefreshRequired(bool discardOldest)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, 2, discardOldest);

            monitoredItem.QueueEvent(CreateConditionEvent(telemetry));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            if (discardOldest)
            {
                // drop the oldest (the condition) and queue a new audit event...
                monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
                var drained = new Queue<EventFieldList>();
                monitoredItem.Publish(new OperationContext(monitoredItem), drained, 10);
            }

            // ...then overflow again, losing only audit events.
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 10);

            Assert.That(result.Select(r => r.Handle), Has.None.AssignableTo<RefreshRequiredEventState>());
            Assert.That(result.Select(r => r.Handle), Has.One.AssignableTo<EventQueueOverflowEventState>());
        }

        /// <summary>
        /// The RefreshRequired event is sent only once the overflow event was delivered;
        /// when the publish is full it follows in the next publish.
        /// </summary>
        [Test]
        public void RefreshRequiredIsDeferredWhenThePublishIsFull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, 2, discardOldest: false);

            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(CreateConditionEvent(telemetry));

            var first = new Queue<EventFieldList>();
            bool more = monitoredItem.Publish(new OperationContext(monitoredItem), first, 3);
            Assert.That(first.Select(r => r.Handle), Has.None.AssignableTo<RefreshRequiredEventState>());
            Assert.That(first.Last().Handle, Is.AssignableTo<EventQueueOverflowEventState>());
            Assert.That(more, Is.True, "The RefreshRequired event is still pending.");

            var second = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), second, 3);
            Assert.That(second.Select(r => r.Handle).ToArray(), Has.Length.EqualTo(1));
            Assert.That(second.Single().Handle, Is.AssignableTo<RefreshRequiredEventState>());

            var third = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), third, 3);
            Assert.That(third, Is.Empty, "The RefreshRequired event is sent once.");
        }

        /// <summary>
        /// DiscardOldest, queue size 3, budget 3: the oldest event, a Condition event, is
        /// discarded. The queue is refilled to three entries (without overflowing) before
        /// every publish; the RefreshRequired event still follows the overflow event.
        /// </summary>
        [Test]
        public void DiscardOldestRefreshRequiredIsNotStarvedByARefilledQueue()
        {
            const uint queueSize = 3;
            const uint budget = 3;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, queueSize, discardOldest: true);

            monitoredItem.QueueEvent(CreateConditionEvent(telemetry));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            var publishes = new List<object[]>
            {
                Publish(monitoredItem, budget)
            };
            Assert.That(publishes[0][0], Is.AssignableTo<EventQueueOverflowEventState>());

            for (int ii = 0; ii < 5; ii++)
            {
                Refill(monitoredItem, (int)queueSize - monitoredItem.ItemsInQueue);
                publishes.Add(Publish(monitoredItem, budget));
            }

            AssertRefreshRequiredSentOnceAfterOverflow(publishes, budget);
        }

        /// <summary>
        /// No DiscardOldest, queue size 5, budget 3: five audit events fill the queue and
        /// the incoming Condition event is dropped. After the overflow event was
        /// delivered, three audit events arrive before every publish; the RefreshRequired
        /// event still follows the overflow event.
        /// </summary>
        [Test]
        public void NoDiscardRefreshRequiredIsNotStarvedByNewEvents()
        {
            const uint queueSize = 5;
            const uint budget = 3;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, queueSize, discardOldest: false);

            Refill(monitoredItem, (int)queueSize);
            monitoredItem.QueueEvent(CreateConditionEvent(telemetry));

            List<object[]> publishes = PublishUntilOverflowEventIsDelivered(monitoredItem, budget);

            for (int ii = 0; ii < 5; ii++)
            {
                Refill(monitoredItem, 3);
                publishes.Add(Publish(monitoredItem, budget));
            }

            AssertRefreshRequiredSentOnceAfterOverflow(publishes, budget);
        }

        /// <summary>
        /// A budget of one: no publish returns more than one notification, and the
        /// RefreshRequired event still arrives exactly once after the overflow event
        /// while the queue keeps being refilled to its size.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void RefreshRequiredWithABudgetOfOneStaysWithinTheBudget(bool discardOldest)
        {
            const uint queueSize = 3;
            const uint budget = 1;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, queueSize, discardOldest);

            if (discardOldest)
            {
                monitoredItem.QueueEvent(CreateConditionEvent(telemetry));
                Refill(monitoredItem, (int)queueSize);
            }
            else
            {
                Refill(monitoredItem, (int)queueSize);
                monitoredItem.QueueEvent(CreateConditionEvent(telemetry));
            }

            List<object[]> publishes = PublishUntilOverflowEventIsDelivered(monitoredItem, budget);

            for (int ii = 0; ii < 5; ii++)
            {
                Refill(monitoredItem, (int)queueSize - monitoredItem.ItemsInQueue);
                publishes.Add(Publish(monitoredItem, budget));
            }

            AssertRefreshRequiredSentOnceAfterOverflow(publishes, budget);
        }

        /// <summary>
        /// DiscardOldest, queue size 3, budget 3, no new events: the overflow publish is
        /// full, so, as without DiscardOldest, the RefreshRequired event follows the
        /// queued events in the next publish and the item stays ready to publish.
        /// </summary>
        [Test]
        public void DiscardOldestRefreshRequiredIsDeferredWhenTheOverflowPublishIsFull()
        {
            const uint queueSize = 3;
            const uint budget = 3;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, queueSize, discardOldest: true);

            monitoredItem.QueueEvent(CreateConditionEvent(telemetry));
            Refill(monitoredItem, (int)queueSize);

            var first = new Queue<EventFieldList>();
            bool more = monitoredItem.Publish(new OperationContext(monitoredItem), first, budget);
            object[] handles = [.. first.Select(r => r.Handle)];
            Assert.That(handles, Has.Length.EqualTo(3));
            Assert.That(handles[0], Is.AssignableTo<EventQueueOverflowEventState>());
            Assert.That(handles[1], Is.AssignableTo<AuditUrlMismatchEventState>());
            Assert.That(handles[2], Is.AssignableTo<AuditUrlMismatchEventState>());
            Assert.That(more, Is.True, "The RefreshRequired event is still pending.");

            handles = Publish(monitoredItem, budget);
            Assert.That(handles, Has.Length.EqualTo(2));
            Assert.That(handles[0], Is.AssignableTo<AuditUrlMismatchEventState>());
            Assert.That(handles[1], Is.AssignableTo<RefreshRequiredEventState>());

            Assert.That(Publish(monitoredItem, budget), Is.Empty, "The RefreshRequired event is sent once.");
        }

        private static void AssertRefreshRequiredSentOnceAfterOverflow(
            List<object[]> publishes,
            uint budget)
        {
            for (int ii = 0; ii < publishes.Count; ii++)
            {
                Assert.That(
                    publishes[ii],
                    Has.Length.LessThanOrEqualTo((int)budget),
                    $"Publish {ii} exceeds the budget.");
            }

            object[] handles = [.. publishes.SelectMany(p => p)];
            Assert.That(handles, Has.One.AssignableTo<EventQueueOverflowEventState>());
            Assert.That(
                handles,
                Has.One.AssignableTo<RefreshRequiredEventState>(),
                "The RefreshRequired event is sent once, although the queue keeps being refilled.");

            int overflowIndex = Array.FindIndex(handles, h => h is EventQueueOverflowEventState);
            int refreshIndex = Array.FindIndex(handles, h => h is RefreshRequiredEventState);
            Assert.That(refreshIndex, Is.GreaterThan(overflowIndex));
        }

        private static List<object[]> PublishUntilOverflowEventIsDelivered(
            MonitoredItem monitoredItem,
            uint budget)
        {
            var publishes = new List<object[]>();
            for (int ii = 0; ii < 20; ii++)
            {
                object[] handles = Publish(monitoredItem, budget);
                publishes.Add(handles);
                if (handles.Any(h => h is EventQueueOverflowEventState))
                {
                    return publishes;
                }
            }
            Assert.Fail("The overflow event was not delivered.");
            return publishes;
        }

        private static void Refill(MonitoredItem monitoredItem, int count)
        {
            for (int ii = 0; ii < count; ii++)
            {
                monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            }
        }

        private static object[] Publish(MonitoredItem monitoredItem, uint budget)
        {
            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, budget);
            return [.. result.Select(r => r.Handle)];
        }

        private static InstanceStateSnapshot CreateConditionEvent(ITelemetryContext telemetry)
        {
            var condition = new AlarmConditionState(null);
            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(new SystemContext(telemetry), condition);
            return snapshot;
        }

        private static MonitoredItem CreateMonitoredItem(
            ITelemetryContext telemetry,
            uint queueSize,
            bool discardOldest)
        {
            var filter = new EventFilter();

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            serverMock.Setup(s => s.MonitoredItemQueueFactory)
                .Returns(new MonitoredItemQueueFactory(telemetry));

            var nodeMangerMock = new Mock<INodeManager>();

            return new MonitoredItem(
                serverMock.Object,
                nodeMangerMock.Object,
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
                1000);
        }
    }
}
