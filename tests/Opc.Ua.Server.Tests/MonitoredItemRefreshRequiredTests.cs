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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;
using Quickstarts.Servers;

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
            using MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, 2, discardOldest: true);

            monitoredItem.QueueEvent(CreateConditionEvent(telemetry));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 10);

            object?[] handles = [.. result.Select(r => r.Handle)];
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
            using MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, 2, discardOldest: false);

            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AlarmConditionState(null));

            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 10);

            object?[] handles = [.. result.Select(r => r.Handle)];
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
            using MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, 2, discardOldest);

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
            using MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, 2, discardOldest: false);

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
        /// Shrinking the queue discards the oldest events (DiscardOldest) or the newest; a
        /// Condition event among them is lost like on any other overflow.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ShrinkingTheQueueLosingConditionEventQueuesRefreshRequired(bool discardOldest)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, 3, discardOldest);

            // the Condition event is the one the shrink discards.
            if (discardOldest)
            {
                monitoredItem.QueueEvent(CreateConditionEvent(telemetry));
            }
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            if (!discardOldest)
            {
                monitoredItem.QueueEvent(CreateConditionEvent(telemetry));
            }

            ModifyQueueSize(monitoredItem, 2, discardOldest);

            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 10);

            object?[] handles = [.. result.Select(r => r.Handle)];
            Assert.That(handles, Has.Length.EqualTo(4));
            Assert.That(handles, Has.Exactly(2).AssignableTo<AuditUrlMismatchEventState>());
            Assert.That(handles, Has.One.AssignableTo<EventQueueOverflowEventState>());
            Assert.That(handles[3], Is.AssignableTo<RefreshRequiredEventState>());
        }

        /// <summary>
        /// Shrinking the queue that discards only non-Condition events needs no
        /// ConditionRefresh.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ShrinkingTheQueueWithoutLostConditionEventsDoesNotQueueRefreshRequired(bool discardOldest)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, 3, discardOldest);

            // the Condition event is the one the shrink keeps.
            if (!discardOldest)
            {
                monitoredItem.QueueEvent(CreateConditionEvent(telemetry));
            }
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            if (discardOldest)
            {
                monitoredItem.QueueEvent(CreateConditionEvent(telemetry));
            }

            ModifyQueueSize(monitoredItem, 2, discardOldest);

            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 10);

            Assert.That(result.Select(r => r.Handle), Has.None.AssignableTo<RefreshRequiredEventState>());
            Assert.That(result.Select(r => r.Handle), Has.One.AssignableTo<EventQueueOverflowEventState>());
        }

        /// <summary>
        /// A durable queue persists a batch without the handles of its events and restores
        /// it without them. DiscardOldest then discards the restored Condition event, which
        /// is still known to be one.
        /// </summary>
        /// <remarks>
        /// Not parallelizable: other tests delete the batch files they do not own.
        /// </remarks>
        [Test]
        [NonParallelizable]
        public void DiscardingConditionEventOfRestoredBatchQueuesRefreshRequired()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using IDisposable scope = AmbientMessageContext.SetScopedContext(
                ServiceMessageContext.Create(telemetry));
            var persistor = new BatchPersistor(telemetry);
            var durableQueue = new DurableEventMonitoredItemQueue(true, kDurableMonitoredItemId, persistor, telemetry);
            var queueFactory = new Mock<IMonitoredItemQueueFactory>();
            queueFactory.Setup(f => f.CreateEventQueue(It.IsAny<bool>(), It.IsAny<uint>()))
                .Returns(durableQueue);
            try
            {
                using MonitoredItem monitoredItem = CreateMonitoredItem(
                    telemetry,
                    2,
                    discardOldest: true,
                    queueFactory.Object);

                monitoredItem.QueueEvent(CreateConditionEvent(telemetry));
                monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

                // spill the batch holding both events to disk and read it back.
                var batch = (EventBatch)typeof(DurableEventMonitoredItemQueue)
                    .GetField("m_dequeueBatch", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(durableQueue)!;
                persistor.PersistSynchronously(batch);
                Assert.That(batch.IsPersisted, Is.True);
                persistor.RestoreSynchronously(batch);
                Assert.That(batch.Events, Has.Count.EqualTo(2));
                Assert.That(batch.Events.Select(e => e.Handle), Is.All.Null);

                monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

                var result = new Queue<EventFieldList>();
                monitoredItem.Publish(new OperationContext(monitoredItem), result, 10);

                object?[] handles = [.. result.Select(r => r.Handle)];
                Assert.That(handles, Has.Length.EqualTo(4));
                Assert.That(handles[0], Is.AssignableTo<EventQueueOverflowEventState>());
                Assert.That(handles[1], Is.Null, "The restored audit event.");
                Assert.That(handles[2], Is.AssignableTo<AuditUrlMismatchEventState>());
                Assert.That(handles[3], Is.AssignableTo<RefreshRequiredEventState>());
            }
            finally
            {
                string batchDirectory = Path.Combine(Environment.CurrentDirectory, "Durable Subscriptions", "Batches");
                if (Directory.Exists(batchDirectory))
                {
                    foreach (string file in Directory.GetFiles(batchDirectory, $"{kDurableMonitoredItemId}_*"))
                    {
                        File.Delete(file);
                    }
                }
            }
        }

        /// <summary>
        /// A queue restored after a restart holds events whose kind was never recorded and
        /// that came back without their handle: discarding one may have lost a Condition
        /// event, so the Server asks for a ConditionRefresh.
        /// </summary>
        [Test]
        public void DiscardingRestoredEventOfUnknownKindTracksConditionEventLoss()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var restoredQueue = new EventMonitoredItemQueue(false, 1, telemetry);
            restoredQueue.SetQueueSize(2, true);
            restoredQueue.Enqueue(new EventFieldList());
            restoredQueue.Enqueue(new EventFieldList());

            using var handler = new EventQueueHandler(restoredQueue, true, telemetry)
            {
                IsConditionEvent = _ => false
            };

            // discards both restored events.
            handler.QueueEvent(new EventFieldList { Handle = new AuditUrlMismatchEventState(null) });
            handler.QueueEvent(new EventFieldList { Handle = new AuditUrlMismatchEventState(null) });
            Assert.That(handler.ConditionEventsLost, Is.True);

            // the events queued since then are known not to be Condition events.
            handler.ConditionEventsLost = false;
            handler.QueueEvent(new EventFieldList { Handle = new AuditUrlMismatchEventState(null) });
            Assert.That(handler.ConditionEventsLost, Is.False);
        }

        /// <summary>
        /// A queue that cannot hand back its oldest event when the handler makes room
        /// discards it itself while queueing the new one. The handler still tracks the lost
        /// Condition event, and the pending transform skips it.
        /// </summary>
        [Test]
        public void EventTheQueueDiscardsItselfIsTrackedAndLeavesThePendingTransform()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var queue = new CustomEventQueue();
            using var handler = new EventQueueHandler(queue, true, telemetry)
            {
                IsConditionEvent = handle => handle is ConditionState
            };
            handler.SetQueueSize(2, true);
            handler.QueueEvent(new EventFieldList { ClientHandle = 1, Handle = new AlarmConditionState(null) });
            handler.QueueEvent(new EventFieldList { ClientHandle = 2, Handle = new AuditUrlMismatchEventState(null) });
            handler.TransformQueuedEvents(fields => new EventFieldList { ClientHandle = fields.ClientHandle + 100 });

            queue.FailNextDequeue = true;
            handler.QueueEvent(new EventFieldList { ClientHandle = 3, Handle = new AuditUrlMismatchEventState(null) });
            Assert.That(handler.ConditionEventsLost, Is.True);

            var notifications = new Queue<EventFieldList>();
            handler.Publish(null!, notifications, 10);
            Assert.That(notifications.Select(n => n.ClientHandle), Is.EqualTo(new uint[] { 102, 3 }));
        }

        /// <summary>
        /// The built-in queue keeps the handles of its events, so an event is classified
        /// only when it is discarded: queueing costs no classification.
        /// </summary>
        [Test]
        public void BuiltInQueueClassifiesOnlyDiscardedEvents()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            AssertClassifiesOnlyDiscardedEvents(new EventMonitoredItemQueue(false, 1, telemetry), telemetry);
        }

        /// <summary>
        /// The mirroring queue of a redundancy server keeps the handles of its events too.
        /// </summary>
        [Test]
        public async Task MirroringQueueClassifiesOnlyDiscardedEventsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var store = new InMemorySharedKeyValueStore();
            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(
                store,
                ServiceMessageContext.Create(telemetry),
                telemetry: telemetry);
            AssertClassifiesOnlyDiscardedEvents(factory.CreateEventQueue(false, 1), telemetry);
        }

        private static void AssertClassifiesOnlyDiscardedEvents(IEventMonitoredItemQueue queue, ITelemetryContext telemetry)
        {
            int classified = 0;
            using var handler = new EventQueueHandler(queue, true, telemetry)
            {
                IsConditionEvent = handle =>
                {
                    classified++;
                    return handle is ConditionState;
                }
            };
            handler.SetQueueSize(2, true);
            handler.QueueEvent(new EventFieldList { Handle = new AlarmConditionState(null) });
            handler.QueueEvent(new EventFieldList { Handle = new AuditUrlMismatchEventState(null) });
            Assert.That(classified, Is.Zero);

            // discards the Condition event.
            handler.QueueEvent(new EventFieldList { Handle = new AuditUrlMismatchEventState(null) });
            Assert.That(classified, Is.EqualTo(1));
            Assert.That(handler.ConditionEventsLost, Is.True);
        }

        /// <summary>
        /// Any other queue may store its events without their handles, so an event is
        /// classified when it is queued: shrinking the queue after the handles are gone
        /// still finds the discarded Condition event, for both discard policies.
        /// </summary>
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void ShrinkingAQueueThatMayDropHandlesTracksLostConditionEvent(bool discardOldest, bool conditionDiscarded)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var handler = new EventQueueHandler(new CustomEventQueue(), discardOldest, telemetry)
            {
                IsConditionEvent = handle => handle is ConditionState
            };
            handler.SetQueueSize(3, discardOldest);

            // the shrink discards the oldest event (DiscardOldest) or the newest.
            var condition = new EventFieldList { Handle = new AlarmConditionState(null) };
            EventFieldList[] events = discardOldest == conditionDiscarded
                ? [condition, new EventFieldList(), new EventFieldList()]
                : [new EventFieldList(), new EventFieldList(), condition];
            foreach (EventFieldList fields in events)
            {
                handler.QueueEvent(fields);
            }
            condition.Handle = null;

            handler.SetQueueSize(2, discardOldest);
            Assert.That(handler.ConditionEventsLost, Is.EqualTo(conditionDiscarded));
        }

        private const uint kDurableMonitoredItemId = 4924;

        /// <summary>
        /// An event queue other than the built-in one. Its next dequeue can fail, e.g. while
        /// a durable queue restores a batch, and it discards its oldest event itself when it
        /// is full.
        /// </summary>
        private sealed class CustomEventQueue : IEventMonitoredItemQueue
        {
            public bool FailNextDequeue { get; set; }

            public uint MonitoredItemId => 1;

            public bool IsDurable => true;

            public uint QueueSize { get; private set; }

            public int ItemsInQueue => m_events.Count;

            public void SetQueueSize(uint queueSize, bool discardOldest)
            {
                QueueSize = queueSize;
                int excess = m_events.Count - (int)queueSize;
                if (excess > 0)
                {
                    m_events.RemoveRange(discardOldest ? 0 : (int)queueSize, excess);
                }
            }

            public bool IsEventContainedInQueue(IFilterTarget instance)
            {
                return false;
            }

            public void Enqueue(EventFieldList value)
            {
                if (m_events.Count == QueueSize)
                {
                    m_events.RemoveAt(0);
                }
                m_events.Add(value);
            }

            public bool Dequeue(out EventFieldList value)
            {
                value = null!;
                if (FailNextDequeue || m_events.Count == 0)
                {
                    FailNextDequeue = false;
                    return false;
                }
                value = m_events[0];
                m_events.RemoveAt(0);
                return true;
            }

            public void Dispose()
            {
            }

            private readonly List<EventFieldList> m_events = [];
        }

        private static void ModifyQueueSize(MonitoredItem item, uint queueSize, bool discardOldest)
        {
            ServiceResult? result = item.ModifyAttributes(
                item.DiagnosticsMasks,
                TimestampsToReturn.Server,
                item.ClientHandle,
                item.Filter,
                item.Filter,
                null,
                item.SamplingInterval,
                queueSize,
                discardOldest);
            Assert.That(ServiceResult.IsBad(result), Is.False);
            Assert.That(item.QueueSize, Is.EqualTo(queueSize));
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
            bool discardOldest,
            IMonitoredItemQueueFactory? queueFactory = null)
        {
            var filter = new EventFilter();

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            using var defaultQueueFactory = new MonitoredItemQueueFactory(telemetry);
            serverMock.Setup(s => s.MonitoredItemQueueFactory)
                .Returns(queueFactory ?? defaultQueueFactory);

            var nodeMangerMock = new Mock<IAsyncNodeManager>();

            return new MonitoredItem(
                serverMock.Object,
                nodeMangerMock.Object,
                null!,
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
