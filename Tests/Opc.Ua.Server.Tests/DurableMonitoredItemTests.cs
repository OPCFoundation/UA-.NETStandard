using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;
using Quickstarts.Servers;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test MonitoredItem
    /// </summary>
    [TestFixture]
    [Category("MonitoredItem")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    public class DurableMonitoredItemTests
    {
        /// <summary>
        /// Queue Factories to run the test with
        /// </summary>
        public static readonly object[] FixtureArgs =
        [
            new object[] { new MonitoredItemQueueFactory(null) },
            new object[] { new DurableMonitoredItemQueueFactory(null) }
        ];

        public DurableMonitoredItemTests(IMonitoredItemQueueFactory factory)
        {
            m_factory = factory;
        }

        private readonly IMonitoredItemQueueFactory m_factory;

        [Test]
        public void EnqueueDequeueDataValue()
        {
            IDataChangeMonitoredItemQueue queue = m_factory.CreateDataChangeQueue(false, 1);

            Assert.That(queue.QueueSize, Is.EqualTo(0));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
            Assert.That(queue.IsDurable, Is.EqualTo(false));
            Assert.That(queue.Dequeue(out _, out _), Is.EqualTo(false));
            Assert.That(queue.PeekLastValue(), Is.EqualTo(null));
            Assert.Throws<InvalidOperationException>(
                () => queue.OverwriteLastValue(new DataValue(), null));
            Assert.Throws<InvalidOperationException>(() => queue.Enqueue(new DataValue(), null));

            queue.ResetQueue(2, true);

            Assert.That(queue.QueueSize, Is.EqualTo(2));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            queue.Enqueue(dataValue, statuscode);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            Assert.That(queue.PeekLastValue(), Is.EqualTo(dataValue));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            var dataValue2 = new DataValue(new Variant(false));

            queue.Enqueue(dataValue2, null);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(2));

            Assert.That(queue.PeekLastValue(), Is.EqualTo(dataValue2));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(2));

            bool status = queue.Dequeue(out DataValue result, out ServiceResult resultError);

            Assert.That(status, Is.True);
            Assert.That(result, Is.EqualTo(dataValue));
            Assert.That(resultError, Is.EqualTo(statuscode));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            bool status2 = queue.Dequeue(out DataValue result2, out ServiceResult resultError2);

            Assert.That(status2, Is.True);
            Assert.That(result2, Is.EqualTo(dataValue2));
            Assert.That(resultError2, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            bool status3 = queue.Dequeue(out DataValue result3, out ServiceResult resultError3);

            Assert.That(status3, Is.False);
            Assert.That(result3, Is.Null);
            Assert.That(resultError3, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
        }

        [Test]
        public void DataValueOverflow()
        {
            IDataChangeMonitoredItemQueue queue = m_factory.CreateDataChangeQueue(false, 1);

            queue.ResetQueue(2, true);

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            queue.Enqueue(dataValue, statuscode);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            Assert.That(queue.PeekLastValue(), Is.EqualTo(dataValue));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            var dataValue2 = new DataValue(new Variant(false));

            queue.Enqueue(dataValue2, null);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(2));

            bool status = queue.Dequeue(out DataValue result, out ServiceResult resultError);

            Assert.That(status, Is.True);
            Assert.That(result, Is.EqualTo(dataValue));
            Assert.That(resultError, Is.EqualTo(statuscode));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            var dataValue3 = new DataValue(new Variant("Test"));

            queue.Enqueue(dataValue3, null);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(2));

            queue.Enqueue(dataValue3, null);
            _ = queue.ItemsInQueue;

            Assert.That(queue.ItemsInQueue, Is.EqualTo(2));

            bool status2 = queue.Dequeue(out DataValue result2, out ServiceResult resultError2);

            Assert.That(status2, Is.True);
            Assert.That(result2, Is.EqualTo(dataValue3));
            Assert.That(resultError2, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            bool status3 = queue.Dequeue(out DataValue result3, out ServiceResult resultError3);

            Assert.That(status3, Is.True);
            Assert.That(result3, Is.EqualTo(dataValue3));
            Assert.That(resultError3, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            bool status4 = queue.Dequeue(out DataValue result4, out ServiceResult resultError4);

            Assert.That(status4, Is.False);
            Assert.That(result4, Is.Null);
            Assert.That(resultError4, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
        }

        [Test]
        public void DataValueQueueSize1()
        {
            IDataChangeMonitoredItemQueue queue = m_factory.CreateDataChangeQueue(false, 1);

            queue.ResetQueue(1, false);

            Assert.That(queue.QueueSize, Is.EqualTo(1));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            queue.Enqueue(dataValue, statuscode);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            Assert.That(queue.PeekLastValue(), Is.EqualTo(dataValue));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            var dataValue2 = new DataValue(new Variant(false));

            queue.Enqueue(dataValue2, null);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            Assert.That(queue.PeekLastValue(), Is.EqualTo(dataValue2));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            bool status = queue.Dequeue(out DataValue result, out ServiceResult resultError);

            Assert.That(status, Is.True);
            Assert.That(result, Is.EqualTo(dataValue2));
            Assert.That(resultError, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            bool status2 = queue.Dequeue(out DataValue result2, out ServiceResult resultError2);

            Assert.That(status2, Is.False);
            Assert.That(result2, Is.Null);
            Assert.That(resultError2, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
        }

        [Test]
        public void DataValueQueueSize10()
        {
            IDataChangeMonitoredItemQueue queue = m_factory.CreateDataChangeQueue(false, 1);

            queue.ResetQueue(10, true);

            Assert.That(queue.QueueSize, Is.EqualTo(10));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            for (int i = 0; i < 10; i++)
            {
                queue.Enqueue(dataValue, statuscode);
            }

            Assert.That(queue.ItemsInQueue, Is.EqualTo(10));

            for (int i = 0; i < 10; i++)
            {
                bool status = queue.Dequeue(out DataValue result, out ServiceResult resultError);

                Assert.That(status, Is.True);
                Assert.That(result, Is.EqualTo(dataValue));
                Assert.That(resultError, Is.EqualTo(statuscode));
            }

            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            bool status2 = queue.Dequeue(out DataValue result2, out ServiceResult resultError2);

            Assert.That(status2, Is.False);
            Assert.That(result2, Is.Null);
            Assert.That(resultError2, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
        }

        [Benchmark]
        public void QueueDequeueValues()
        {
            IDataChangeMonitoredItemQueue queue = m_factory.CreateDataChangeQueue(false, 1);
            queue.ResetQueue(1000, false);

            for (int j = 0; j < 10_000; j++)
            {
                queue.Enqueue(new DataValue(new Variant(false)), null);
                queue.Dequeue(out _, out _);
            }
        }

        [Benchmark]
        public void QueueDequeueValuesWithOverflow()
        {
            IDataChangeMonitoredItemQueue queue = m_factory.CreateDataChangeQueue(false, 1);
            queue.ResetQueue(100, false);

            for (int j = 0; j < 100; j++)
            {
                for (int i = 0; i < 100; i++)
                {
                    queue.Enqueue(new DataValue(new Variant(false)), null);
                }
                for (int v = 0; v < 90; v++)
                {
                    queue.Dequeue(out _, out _);
                }
            }
        }

        [Benchmark]
        public void QueueDequeueEvents()
        {
            IEventMonitoredItemQueue queue = m_factory.CreateEventQueue(false, 1);
            queue.SetQueueSize(1000, false);

            for (int j = 0; j < 10_000; j++)
            {
                var value = new EventFieldList { EventFields = [new Variant(true)] };
                queue.Enqueue(value);
                queue.Dequeue(out _);
            }
        }

        [Benchmark]
        public void QueueDequeueEventssWithOverflow()
        {
            IEventMonitoredItemQueue queue = m_factory.CreateEventQueue(false, 1);
            queue.SetQueueSize(100, false);

            for (int j = 0; j < 100; j++)
            {
                for (int i = 0; i < 100; i++)
                {
                    var value = new EventFieldList { EventFields = [new Variant(true)] };
                    queue.Enqueue(value);
                }
                for (int v = 0; v < 90; v++)
                {
                    queue.Dequeue(out _);
                }
            }
        }

        [Test]
        public void EnqueueDequeueEvent()
        {
            IEventMonitoredItemQueue queue = m_factory.CreateEventQueue(false, 1);

            Assert.That(queue.QueueSize, Is.EqualTo(0));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
            Assert.That(queue.IsDurable, Is.EqualTo(false));
            Assert.That(queue.Dequeue(out _), Is.EqualTo(false));
            Assert.That(queue.IsEventContainedInQueue(null), Is.EqualTo(false));
            //Assert.Throws<InvalidOperationException>(() => queue.Enqueue(new EventFieldList()));

            queue.SetQueueSize(2, false);

            Assert.That(queue.QueueSize, Is.EqualTo(2));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var value = new EventFieldList { EventFields = [new Variant(true)] };

            queue.Enqueue(value);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            var value2 = new EventFieldList { EventFields = [new Variant(false)] };

            queue.Enqueue(value2);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(2));

            bool status = queue.Dequeue(out EventFieldList result);

            Assert.That(status, Is.True);
            Assert.That(result, Is.EqualTo(value));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            bool status2 = queue.Dequeue(out EventFieldList result2);

            Assert.That(status2, Is.True);
            Assert.That(result2, Is.EqualTo(value2));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            bool status3 = queue.Dequeue(out EventFieldList result3);

            Assert.That(status3, Is.False);
            Assert.That(result3, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
        }

        [Test]
        public void EventOverflow()
        {
            IEventMonitoredItemQueue queue = m_factory.CreateEventQueue(false, 1);

            queue.SetQueueSize(2, false);

            var value = new EventFieldList { EventFields = [new Variant(true)] };

            queue.Enqueue(value);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            var value2 = new EventFieldList { EventFields = [new Variant(false)] };

            queue.Enqueue(value2);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(2));

            bool status = queue.Dequeue(out EventFieldList result);

            Assert.That(status, Is.True);
            Assert.That(result, Is.EqualTo(value));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            queue.Enqueue(value2);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(2));

            queue.Enqueue(value2);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(2));

            bool status2 = queue.Dequeue(out EventFieldList result2);

            Assert.That(status2, Is.True);
            Assert.That(result2, Is.EqualTo(value2));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            bool status3 = queue.Dequeue(out EventFieldList result3);

            Assert.That(status3, Is.True);
            Assert.That(result3, Is.EqualTo(value2));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            bool status4 = queue.Dequeue(out EventFieldList result4);

            Assert.That(status4, Is.False);
            Assert.That(result4, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
        }

        [Test]
        public void EventQueueSize1()
        {
            IEventMonitoredItemQueue queue = m_factory.CreateEventQueue(false, 1);

            queue.SetQueueSize(1, false);

            Assert.That(queue.QueueSize, Is.EqualTo(1));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var value = new EventFieldList { EventFields = [new Variant(true)] };

            queue.Enqueue(value);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            var value2 = new EventFieldList { EventFields = [new Variant(false)] };

            queue.Enqueue(value2);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            bool status = queue.Dequeue(out EventFieldList result);

            Assert.That(status, Is.True);
            Assert.That(result, Is.EqualTo(value2));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            bool status2 = queue.Dequeue(out EventFieldList result2);

            Assert.That(status2, Is.False);
            Assert.That(result2, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
        }

        [Test]
        public void EventQueueIsEventContainedInQueue()
        {
            IEventMonitoredItemQueue queue = m_factory.CreateEventQueue(false, 1);

            queue.SetQueueSize(2, false);

            Assert.That(queue.QueueSize, Is.EqualTo(2));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var value = new EventFieldList
            {
                EventFields = [new Variant(true)],
                Handle = new AuditSessionEventState(null)
            };

            queue.Enqueue(value);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));
            var value2 = new EventFieldList
            {
                EventFields = [new Variant(false)],
                Handle = new AuditUrlMismatchEventState(null)
            };

            bool result = queue.IsEventContainedInQueue((IFilterTarget)value.Handle);

            Assert.That(result, Is.True);

            bool result2 = queue.IsEventContainedInQueue((IFilterTarget)value2.Handle);

            Assert.That(result2, Is.False);

            queue.Enqueue(value2);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(2));

            bool result3 = queue.IsEventContainedInQueue((IFilterTarget)value.Handle);

            Assert.That(result3, Is.True);

            bool result4 = queue.IsEventContainedInQueue((IFilterTarget)value2.Handle);

            Assert.That(result4, Is.True);
        }

        [Test]
        public void EventQueueSize10()
        {
            IEventMonitoredItemQueue queue = m_factory.CreateEventQueue(false, 1);

            queue.SetQueueSize(10, false);

            Assert.That(queue.QueueSize, Is.EqualTo(10));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var value = new EventFieldList { EventFields = [new Variant(true)] };

            for (int i = 0; i < 10; i++)
            {
                queue.Enqueue(value);
            }

            Assert.That(queue.ItemsInQueue, Is.EqualTo(10));

            for (int i = 0; i < 10; i++)
            {
                bool status = queue.Dequeue(out EventFieldList result);

                Assert.That(status, Is.True);
                Assert.That(result, Is.EqualTo(value));
            }

            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            bool status2 = queue.Dequeue(out EventFieldList result2);

            Assert.That(status2, Is.False);
            Assert.That(result2, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
        }

        [Test]
        public void EventQueueSizeChangeRequeuesValues()
        {
            IEventMonitoredItemQueue queue = m_factory.CreateEventQueue(false, 1);

            queue.SetQueueSize(10, false);

            Assert.That(queue.QueueSize, Is.EqualTo(10));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var value = new EventFieldList { EventFields = [new Variant(true)] };

            for (int i = 0; i < 10; i++)
            {
                queue.Enqueue(value);
            }

            Assert.That(queue.ItemsInQueue, Is.EqualTo(10));

            queue.SetQueueSize(20, false);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(10));
            Assert.That(queue.QueueSize, Is.EqualTo(20));

            queue.SetQueueSize(5, false);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(5));
            Assert.That(queue.QueueSize, Is.EqualTo(5));

            for (int i = 0; i < 5; i++)
            {
                bool status = queue.Dequeue(out EventFieldList result);

                Assert.That(status, Is.True);
                Assert.That(result, Is.EqualTo(value));
            }

            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            bool status2 = queue.Dequeue(out EventFieldList result2);

            Assert.That(status2, Is.False);
            Assert.That(result2, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
        }

        [Test]
        public void EventDecreaseQueueSizeDiscardsOldest()
        {
            IEventMonitoredItemQueue queue = m_factory.CreateEventQueue(false, 1);

            queue.SetQueueSize(10, false);

            Assert.That(queue.QueueSize, Is.EqualTo(10));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var value = new EventFieldList { EventFields = [new Variant(true)] };

            for (int i = 0; i < 5; i++)
            {
                queue.Enqueue(value);
            }

            var value2 = new EventFieldList { EventFields = [new Variant(false)] };

            for (int i = 0; i < 5; i++)
            {
                queue.Enqueue(value2);
            }

            Assert.That(queue.ItemsInQueue, Is.EqualTo(10));

            queue.SetQueueSize(5, true);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(5));
            Assert.That(queue.QueueSize, Is.EqualTo(5));

            for (int i = 0; i < 5; i++)
            {
                bool status = queue.Dequeue(out EventFieldList result);

                Assert.That(status, Is.True);
                Assert.That(result, Is.EqualTo(value2));
            }

            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            bool status2 = queue.Dequeue(out EventFieldList result2);

            Assert.That(status2, Is.False);
            Assert.That(result2, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
        }

        [Test]
        public void EventDecreaseQueueSizeDiscardsNewest()
        {
            IEventMonitoredItemQueue queue = m_factory.CreateEventQueue(false, 1);

            queue.SetQueueSize(10, false);

            Assert.That(queue.QueueSize, Is.EqualTo(10));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var value = new EventFieldList { EventFields = [new Variant(true)] };

            for (int i = 0; i < 5; i++)
            {
                queue.Enqueue(value);
            }

            var value2 = new EventFieldList { EventFields = [new Variant(false)] };

            for (int i = 0; i < 5; i++)
            {
                queue.Enqueue(value2);
            }

            Assert.That(queue.ItemsInQueue, Is.EqualTo(10));

            queue.SetQueueSize(5, false);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(5));
            Assert.That(queue.QueueSize, Is.EqualTo(5));

            for (int i = 0; i < 5; i++)
            {
                bool status = queue.Dequeue(out EventFieldList result);

                Assert.That(status, Is.True);
                Assert.That(result, Is.EqualTo(value));
            }

            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            bool status2 = queue.Dequeue(out EventFieldList result2);

            Assert.That(status2, Is.False);
            Assert.That(result2, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
        }

        [Test]
        public void EventQueueHandlerOverflow()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var queueHandler = new EventQueueHandler(false, m_factory, 1, telemetry);

            queueHandler.SetQueueSize(1, false);

            var value = new EventFieldList { EventFields = [new Variant(true)] };

            queueHandler.QueueEvent(value);

            Assert.That(queueHandler.SetQueueOverflowIfFull(), Is.True);
            Assert.That(queueHandler.Overflow, Is.True);

            Assert.Throws<InvalidOperationException>(() => queueHandler.QueueEvent(value));
        }

        [Test]
        public void EventQueueHandlerEnqueueSize1()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var queueHandler = new EventQueueHandler(false, m_factory, 1, telemetry);

            queueHandler.SetQueueSize(1, true);

            var value = new EventFieldList { EventFields = [new Variant(true)] };

            queueHandler.QueueEvent(value);

            var value2 = new EventFieldList { EventFields = [new Variant(true)] };

            queueHandler.QueueEvent(value2);

            Assert.That(queueHandler.Overflow, Is.EqualTo(true));

            var events = new Queue<EventFieldList>();
            queueHandler.Publish(null, events, 1);
            Assert.That(events, Is.Not.Empty);
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events.Peek(), Is.Not.EqualTo(value));
            Assert.That(events.Peek(), Is.EqualTo(value2));
        }

        [Test]
        public void EventQueueHandlerEnqueueSize2()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var queueHandler = new EventQueueHandler(false, m_factory, 1, telemetry);

            queueHandler.SetQueueSize(2, false);

            var value = new EventFieldList { EventFields = [new Variant(true)] };

            queueHandler.QueueEvent(value);

            var value2 = new EventFieldList { EventFields = [new Variant(true)] };

            queueHandler.QueueEvent(value2);

            Assert.That(queueHandler.Overflow, Is.EqualTo(false));

            var events = new Queue<EventFieldList>();
            queueHandler.Publish(null, events, 1);
            Assert.That(events, Is.Not.Empty);
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events.Peek(), Is.Not.EqualTo(value2));
            Assert.That(events.Peek(), Is.EqualTo(value));

            events = new Queue<EventFieldList>();
            queueHandler.Publish(null, events, 1);
            Assert.That(events, Is.Not.Empty);
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events.Peek(), Is.Not.EqualTo(value));
            Assert.That(events.Peek(), Is.EqualTo(value2));

            events = new Queue<EventFieldList>();
            queueHandler.Publish(null, events, 1);
            Assert.That(events, Is.Empty);
        }

        [Test]
        public void EventQueueHandlerPublish()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var queueHandler = new EventQueueHandler(false, m_factory, 1, telemetry);

            queueHandler.SetQueueSize(2, false);

            var value = new EventFieldList { EventFields = [new Variant(true)] };

            queueHandler.QueueEvent(value);

            var value2 = new EventFieldList { EventFields = [new Variant(true)] };

            queueHandler.QueueEvent(value2);

            Assert.That(queueHandler.Overflow, Is.EqualTo(false));

            var events = new Queue<EventFieldList>();
            queueHandler.Publish(null, events, 2);
            Assert.That(events, Is.Not.Empty);
            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events.Dequeue(), Is.EqualTo(value));
            Assert.That(events.Dequeue(), Is.EqualTo(value2));

            events = new Queue<EventFieldList>();
            queueHandler.Publish(null, events, 1);
            Assert.That(events, Is.Empty);
        }

        [Test]
        public void DataValueQueueDiscardedValueHandlerInvoked()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            bool called = false;
            void DiscardedValueHandler() => called = true;

            var queueHandler = new DataChangeQueueHandler(
                1,
                false,
                m_factory,
                telemetry,
                DiscardedValueHandler);

            queueHandler.SetQueueSize(1, true, DiagnosticsMasks.All);

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            queueHandler.QueueValue(dataValue, statuscode);

            var statuscode2 = new ServiceResult(StatusCodes.BadAggregateNotSupported);
            var dataValue2 = new DataValue(new Variant(false));

            queueHandler.QueueValue(dataValue2, statuscode2);

            Assert.That(called, Is.True);

            bool success = queueHandler.PublishSingleValue(
                out DataValue result,
                out ServiceResult resultError);

            Assert.That(success, Is.True);
            Assert.That(result, Is.EqualTo(dataValue2));
            //Queue size 1 no overflow set
            Assert.That(result.StatusCode.Overflow, Is.False);
            Assert.That(resultError.StatusCode.Overflow, Is.False);
        }

        [Test]
        public void DataValueQueueDiscardedValueHandlerInvokedNoDiscardOldest()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            bool called = false;
            void DiscardValueHandler() => called = true;

            var queueHandler = new DataChangeQueueHandler(
                1,
                false,
                m_factory,
                telemetry,
                DiscardValueHandler);

            queueHandler.SetQueueSize(2, false, DiagnosticsMasks.All);

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            queueHandler.QueueValue(dataValue, statuscode);
            queueHandler.QueueValue(dataValue, statuscode);

            var statuscode2 = new ServiceResult(StatusCodes.Good);
            var dataValue2 = new DataValue(new Variant(false));

            queueHandler.QueueValue(dataValue2, statuscode2);

            Assert.That(called, Is.True);

            bool success = queueHandler.PublishSingleValue(
                out DataValue result,
                out ServiceResult resultError);

            Assert.That(success, Is.True);
            Assert.That(result, Is.EqualTo(dataValue));
            Assert.That(resultError, Is.EqualTo(statuscode));

            bool success2 = queueHandler.PublishSingleValue(
                out DataValue result2,
                out ServiceResult resultError2);

            Assert.That(success2, Is.True);
            Assert.That(result2, Is.EqualTo(dataValue2));
            Assert.That(result2.StatusCode.Overflow, Is.True);
            Assert.That(resultError2.StatusCode.Overflow, Is.True);
        }

        [Test]
        public void DataValueQueueSamplingIntervalOverwrites()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            bool called = false;
            void DiscardValueHandler() => called = true;

            var queueHandler = new DataChangeQueueHandler(
                1,
                false,
                m_factory,
                telemetry,
                DiscardValueHandler);

            queueHandler.SetQueueSize(3, true, DiagnosticsMasks.All);

            queueHandler.SetSamplingInterval(5000);

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            queueHandler.QueueValue(dataValue, statuscode);

            var statuscode2 = new ServiceResult(StatusCodes.Good);
            var dataValue2 = new DataValue(new Variant(false));

            queueHandler.QueueValue(dataValue2, statuscode2);

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(1));
            Assert.That(called, Is.True);

            bool success = queueHandler.PublishSingleValue(
                out DataValue result,
                out ServiceResult resultError);

            Assert.That(success, Is.True);
            Assert.That(result, Is.EqualTo(dataValue2));
            Assert.That(resultError, Is.EqualTo(statuscode2));
        }

        [Test]
        public async Task DataValueQueueSamplingIntervalChangeAppliedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            bool called = false;
            void DiscardValueHandler() => called = true;

            var queueHandler = new DataChangeQueueHandler(
                1,
                false,
                m_factory,
                telemetry,
                DiscardValueHandler);

            queueHandler.SetQueueSize(3, true, DiagnosticsMasks.All);

            queueHandler.SetSamplingInterval(2);

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            queueHandler.QueueValue(dataValue, statuscode);

            var statuscode2 = new ServiceResult(StatusCodes.Good);
            var dataValue2 = new DataValue(new Variant(false));

            await Task.Delay(5).ConfigureAwait(false);

            queueHandler.QueueValue(dataValue2, statuscode2);

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(2));
            Assert.That(called, Is.False);

            bool success = queueHandler.PublishSingleValue(
                out DataValue result,
                out ServiceResult resultError);

            Assert.That(success, Is.True);
            Assert.That(result, Is.EqualTo(dataValue));
            Assert.That(resultError, Is.EqualTo(statuscode));
        }

        [Test]
        public void DataValueQueueSizeChangeRequeuesValues()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            bool called = false;
            void DiscardValueHandler() => called = true;

            var queueHandler = new DataChangeQueueHandler(
                1,
                false,
                m_factory,
                telemetry,
                DiscardValueHandler);

            queueHandler.SetQueueSize(10, true, DiagnosticsMasks.All);

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(0));

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            for (int i = 0; i < 10; i++)
            {
                queueHandler.QueueValue(dataValue, statuscode);
            }

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(10));

            queueHandler.SetQueueSize(20, true, DiagnosticsMasks.All);

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(10));

            queueHandler.SetQueueSize(5, true, DiagnosticsMasks.All);

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(5));
            Assert.That(called, Is.True);

            for (int i = 0; i < 5; i++)
            {
                bool status = queueHandler.PublishSingleValue(
                    out DataValue result,
                    out ServiceResult resultError);

                Assert.That(status, Is.True);
                Assert.That(result, Is.EqualTo(dataValue));
            }

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(0));

            bool status2 = queueHandler.PublishSingleValue(
                out DataValue result2,
                out ServiceResult resultError2);

            Assert.That(status2, Is.False);
            Assert.That(result2, Is.Null);
            Assert.That(resultError2, Is.Null);
            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(0));
        }

        [Test]
        public void DataValueDecreaseQueueSizeDiscardsOldest()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            bool called = false;
            void DiscardValueHandler() => called = true;

            var queueHandler = new DataChangeQueueHandler(
                1,
                false,
                m_factory,
                telemetry,
                DiscardValueHandler);

            queueHandler.SetQueueSize(10, true, DiagnosticsMasks.All);

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(0));

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            for (int i = 0; i < 5; i++)
            {
                queueHandler.QueueValue(dataValue, statuscode);
            }

            var statuscode2 = new ServiceResult(StatusCodes.Good);
            var dataValue2 = new DataValue(new Variant(true));

            for (int i = 0; i < 5; i++)
            {
                queueHandler.QueueValue(dataValue2, statuscode2);
            }

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(10));

            queueHandler.SetQueueSize(5, true, DiagnosticsMasks.All);

            Assert.That(called, Is.True);
            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(5));

            for (int i = 0; i < 5; i++)
            {
                bool status = queueHandler.PublishSingleValue(
                    out DataValue result,
                    out ServiceResult resultError);

                Assert.That(status, Is.True);
                Assert.That(result, Is.EqualTo(dataValue2));
                //Assert.That(resultError, Is.EqualTo(statuscode).Or.Property(nameof(result.StatusCode)).Property(nameof(result.StatusCode.Overflow)).True);
            }

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(0));

            bool status2 = queueHandler.PublishSingleValue(
                out DataValue result2,
                out ServiceResult resultError2);

            Assert.That(status2, Is.False);
            Assert.That(result2, Is.Null);
            Assert.That(resultError2, Is.Null);
            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(0));
        }

        [Test]
        public void DataValueInitialDataOverWritesLastValue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            bool called = false;
            void DiscardValueHandler() => called = true;

            var queueHandler = new DataChangeQueueHandler(
                1,
                false,
                m_factory,
                telemetry,
                DiscardValueHandler);

            queueHandler.SetQueueSize(10, true, DiagnosticsMasks.All);

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(0));

            var dataValue = new DataValue(new Variant(true))
            {
                StatusCode = StatusCodes.BadWaitingForInitialData
            };

            for (int i = 0; i < 5; i++)
            {
                queueHandler.QueueValue(dataValue, null);
            }

            var statuscode2 = new ServiceResult(StatusCodes.Good);
            var dataValue2 = new DataValue(new Variant(true));

            queueHandler.QueueValue(dataValue2, statuscode2);

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(1));

            Assert.That(called, Is.False);

            bool status = queueHandler.PublishSingleValue(
                out DataValue result,
                out ServiceResult resultError);

            Assert.That(status, Is.True);
            Assert.That(result, Is.EqualTo(dataValue2));
        }

        [Test]
        public void FactorySupportsDurable()
        {
            if (m_factory.SupportsDurableQueues)
            {
                IDataChangeMonitoredItemQueue dataChangeQueue = m_factory.CreateDataChangeQueue(
                    true,
                    1);

                Assert.That(dataChangeQueue.IsDurable, Is.True);

                IEventMonitoredItemQueue eventQueue = m_factory.CreateEventQueue(true, 1);

                Assert.That(eventQueue.IsDurable, Is.True);
            }
            else
            {
                Assert.Throws<ArgumentException>(() => m_factory.CreateDataChangeQueue(true, 1));
                Assert.Throws<ArgumentException>(() => m_factory.CreateEventQueue(true, 1));
            }
        }

        [Test]
        public void CreateDurableMI()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<DurableMonitoredItemTests>();

            if (m_factory.SupportsDurableQueues)
            {
                MonitoredItem monitoredItem = CreateDurableMonitoredItem();
                Assert.That(monitoredItem, Is.Not.Null);
                Assert.That(monitoredItem.IsDurable, Is.True);
                Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(0));

                var statuscode = new ServiceResult(StatusCodes.Good);
                var dataValue = new DataValue(new Variant(true));

                monitoredItem.QueueValue(dataValue, statuscode);

                Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(1));

                var result = new Queue<MonitoredItemNotification>();
                var result2 = new Queue<DiagnosticInfo>();
                monitoredItem.Publish(new OperationContext(monitoredItem), result, result2, 1, logger);

                Assert.That(result, Is.Not.Empty);
                Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(0));
                MonitoredItemNotification publishResult = result.FirstOrDefault();
                Assert.That(publishResult?.Value, Is.EqualTo(dataValue));
                DiagnosticInfo publishErrorResult = result2.FirstOrDefault();
                Assert.That(
                    publishErrorResult.InnerStatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.Good));
            }
            else
            {
                Assert.Throws<ServiceResultException>(() => CreateDurableMonitoredItem());
            }
        }

        [Test]
        public void CreateDurableEventMI()
        {
            if (!m_factory.SupportsDurableQueues)
            {
                Assert.Ignore("Test only works with durable queues");
            }
            MonitoredItem monitoredItem = CreateDurableMonitoredItem(true);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.IsDurable, Is.True);
            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(0));

            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(1));

            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 1);

            Assert.That(result, Is.Not.Empty);
            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(0));
            EventFieldList publishResult = result.FirstOrDefault();
            Assert.That(publishResult, Is.Not.Null);
            Assert.That(publishResult.Handle, Is.AssignableTo<AuditUrlMismatchEventState>());
        }

        [Test]
        public void CreateDurableMIQueueNoQueue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<DurableMonitoredItemTests>();

            if (!m_factory.SupportsDurableQueues)
            {
                Assert.Ignore("Test only works with durable queues");
            }

            MonitoredItem monitoredItem = CreateDurableMonitoredItem(false, 0);

            Assert.That(monitoredItem.QueueSize, Is.EqualTo(1));

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            monitoredItem.QueueValue(dataValue, statuscode);

            var result = new Queue<MonitoredItemNotification>();
            var result2 = new Queue<DiagnosticInfo>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, result2, 1, logger);

            Assert.That(result, Is.Not.Empty);
            MonitoredItemNotification publishResult = result.FirstOrDefault();
            Assert.That(publishResult?.Value, Is.EqualTo(dataValue));
            DiagnosticInfo publishErrorResult = result2.FirstOrDefault();
            Assert.That(
                publishErrorResult.InnerStatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good));
        }

        [Test]
        public void CreateDurableEventMIOverflow()
        {
            if (!m_factory.SupportsDurableQueues)
            {
                Assert.Ignore("Test only works with durable queues");
            }
            MonitoredItem monitoredItem = CreateDurableMonitoredItem(true, 2);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.IsDurable, Is.True);
            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(0));

            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(2));

            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(2));

            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 3);

            Assert.That(result, Is.Not.Empty);
            EventFieldList publishResult = result.LastOrDefault();
            Assert.That(publishResult, Is.Not.Null);
            Assert.That(publishResult.Handle, Is.AssignableTo<EventQueueOverflowEventState>());
        }

        [Test]
        public async Task DurableEventQueueVerifyReferenceBatchingAsync()
        {
            if (!m_factory.SupportsDurableQueues)
            {
                Assert.Ignore("Test only works with durable queues");
            }

            IEventMonitoredItemQueue queue = m_factory.CreateEventQueue(true, 0);

            queue.SetQueueSize(3000, false);

            for (uint i = 0; i < 3000; i++)
            {
                queue.Enqueue(new EventFieldList { ClientHandle = i });
            }

            // wait for persisting to take place
            await Task.Delay(1000).ConfigureAwait(false);

            for (uint i = 0; i < 3000; i++)
            {
                Assert.That(
                    queue.Dequeue(out EventFieldList value),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Dequeue operation failed for the {0}st item",
                        i));
                Assert.That(i, Is.EqualTo(value.ClientHandle));

                //simulate publishing operation
                if (i % 501 == 0)
                {
                    await Task.Delay(1200).ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task DurableDataValueQueueVerifyReferenceBatchingAsync()
        {
            if (!m_factory.SupportsDurableQueues)
            {
                Assert.Ignore("Test only works with durable queues");
            }

            IDataChangeMonitoredItemQueue queue = m_factory.CreateDataChangeQueue(true, 0);

            queue.ResetQueue(3000, false);

            for (uint i = 0; i < 3000; i++)
            {
                queue.Enqueue(new DataValue(new Variant(i)), null);
            }

            // wait for persisting to take place
            await Task.Delay(1000).ConfigureAwait(false);

            for (uint i = 0; i < 3000; i++)
            {
                Assert.That(
                    queue.Dequeue(out DataValue value, out ServiceResult _),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Dequeue operation failed for the {0}st item",
                        i));
                Assert.That(i, Is.EqualTo((uint)value.Value));

                //simulate publishing operation
                if (i % 501 == 0)
                {
                    await Task.Delay(1200).ConfigureAwait(false);
                }
            }
        }

        private MonitoredItem CreateDurableMonitoredItem(bool events = false, uint queueSize = 10)
        {
            MonitoringFilter filter = events ? new EventFilter() : new MonitoringFilter();

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(m_factory);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));

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
                false,
                1000,
                true);
        }
    }
}
