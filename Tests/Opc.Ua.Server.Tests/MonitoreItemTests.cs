using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test MonitoredItem
    /// </summary>
    [TestFixture, Category("MonitoredItem")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    public class MonitoreItemTests
    {
        private IMonitoredItemQueueFactory m_factory;
        #region Benchmark Setup
        /// <summary>
        /// Set up a Reference Server a session
        /// </summary>
        [OneTimeSetUp]
        [GlobalSetup]
        public void GlobalSetup()
        {
            m_factory = new MonitoredItemQueueFactory();
        }
        #endregion
        #region dataChangeQueue

        [Test]
        public void EnqueueDequeueDataValue()
        {
            var queue = m_factory.CreateDataChangeQueue(false);

            Assert.That(queue.QueueSize, Is.EqualTo(0));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
            Assert.That(queue.IsDurable, Is.EqualTo(false));
            Assert.That(queue.Dequeue(out _, out _), Is.EqualTo(false));
            Assert.That(queue.PeekLastValue(), Is.EqualTo(null));
            Assert.Throws<InvalidOperationException>(() => queue.OverwriteLastValue(new DataValue(), null));
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
            var queue = m_factory.CreateDataChangeQueue(false);

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

            var size = queue.ItemsInQueue;


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
            var queue = m_factory.CreateDataChangeQueue(false);

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
            var queue = m_factory.CreateDataChangeQueue(false);

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
        #endregion
        #region benchmarks
        [Benchmark]
        public void QueueDequeueValues()
        {
            var queue = m_factory.CreateDataChangeQueue(false);
            queue.ResetQueue(1000, false);

            for (int j = 0; j < 10_000; j++)
            {
                queue.Enqueue(new DataValue(new Variant(false)), null);

                queue.Dequeue(out var dataValue, out var _);
            }
        }
        [Benchmark]
        public void QueueDequeueValuesWithOverflow()
        {
            var queue = m_factory.CreateDataChangeQueue(false);
            queue.ResetQueue(100, false);

            for (int j = 0; j < 100; j++)
            {
                for (int i = 0; i < 100; i++)
                {
                    queue.Enqueue(new DataValue(new Variant(false)), null);
                }
                for (int v = 0; v < 90; v++)
                {
                    queue.Dequeue(out var dataValue, out var _);
                }
            }
        }

        [Benchmark]
        public void QueueDequeueEvents()
        {
            var queue = m_factory.CreateEventQueue(false);
            queue.SetQueueSize(1000, false);

            for (int j = 0; j < 10_000; j++)
            {
                var value = new EventFieldList {
                    EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
                };
                queue.Enqueue(value);
                queue.Dequeue(out var value2);
            }
        }
        [Benchmark]
        public void QueueDequeueEventssWithOverflow()
        {
            var queue = m_factory.CreateEventQueue(false);
            queue.SetQueueSize(100, false);

            for (int j = 0; j < 100; j++)
            {
                for (int i = 0; i < 100; i++)
                {
                    var value = new EventFieldList {
                        EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
                    };
                    queue.Enqueue(value);
                }
                for (int v = 0; v < 90; v++)
                {
                    queue.Dequeue(out var value);
                }
            }
        }
        #endregion
        #region eventQueue

        [Test]
        public void EnqueueDequeueEvent()
        {
            var queue = m_factory.CreateEventQueue(false);

            Assert.That(queue.QueueSize, Is.EqualTo(0));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));
            Assert.That(queue.IsDurable, Is.EqualTo(false));
            Assert.That(queue.Dequeue(out _), Is.EqualTo(false));
            Assert.That(queue.IsEventContainedInQueue(null), Is.EqualTo(false));
            //Assert.Throws<InvalidOperationException>(() => queue.Enqueue(new EventFieldList()));

            queue.SetQueueSize(2, false);

            Assert.That(queue.QueueSize, Is.EqualTo(2));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var value = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
            };

            queue.Enqueue(value);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));


            var value2 = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(false)
                    }
            };

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
            var queue = m_factory.CreateEventQueue(false);

            queue.SetQueueSize(2, false);

            var value = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
            };

            queue.Enqueue(value);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));


            var value2 = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(false)
                    }
            };

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
            var queue = m_factory.CreateEventQueue(false);

            queue.SetQueueSize(1, false);

            Assert.That(queue.QueueSize, Is.EqualTo(1));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var value = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
            };

            queue.Enqueue(value);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));


            var value2 = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(false)
                    }
            };

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
        public void EventQueueSize10()
        {
            var queue = m_factory.CreateEventQueue(false);

            queue.SetQueueSize(10, false);

            Assert.That(queue.QueueSize, Is.EqualTo(10));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var value = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
            };

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
            var queue = m_factory.CreateEventQueue(false);

            queue.SetQueueSize(10, false);

            Assert.That(queue.QueueSize, Is.EqualTo(10));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var value = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
            };

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
            var queue = m_factory.CreateEventQueue(false);

            queue.SetQueueSize(10, false);

            Assert.That(queue.QueueSize, Is.EqualTo(10));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var value = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
            };

            for (int i = 0; i < 5; i++)
            {
                queue.Enqueue(value);
            }

            var value2 = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(false)
                    }
            };

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
            var queue = m_factory.CreateEventQueue(false);

            queue.SetQueueSize(10, false);

            Assert.That(queue.QueueSize, Is.EqualTo(10));
            Assert.That(queue.ItemsInQueue, Is.EqualTo(0));

            var value = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
            };

            for (int i = 0; i < 5; i++)
            {
                queue.Enqueue(value);
            }

            var value2 = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(false)
                    }
            };

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
        #endregion
        #region EventQueueHandler
        [Test]
        public void EventQueueHandlerOverflow()
        {
            var queueHandler = new EventQueueHandler(false, m_factory);

            queueHandler.SetQueueSize(1, false);

            var value = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
            };

            queueHandler.QueueEvent(value);

            Assert.That(queueHandler.SetQueueOverflowIfFull(), Is.True);
            Assert.That(queueHandler.Overflow, Is.True);

            Assert.Throws<InvalidOperationException>(() => queueHandler.QueueEvent(value));
        }

        [Test]
        public void EventQueueHandlerEnqueueSize1()
        {
            var queueHandler = new EventQueueHandler(false, m_factory);

            queueHandler.SetQueueSize(1, true);

            var value = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
            };

            queueHandler.QueueEvent(value);

            var value2 = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
            };

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
            var queueHandler = new EventQueueHandler(false, m_factory);

            queueHandler.SetQueueSize(2, false);

            var value = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
            };

            queueHandler.QueueEvent(value);

            var value2 = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
            };

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
            var queueHandler = new EventQueueHandler(false, m_factory);

            queueHandler.SetQueueSize(2, false);

            var value = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
            };

            queueHandler.QueueEvent(value);

            var value2 = new EventFieldList {
                EventFields = new VariantCollection(1) {
                        new Variant(true)
                    }
            };

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
        #endregion
        #region DataChangeQueueHandler

        [Test]
        public void DataValueQueueDiscardedValueHandlerInvoked()
        {
            bool called = false;
            Action discardedValueHandler = () => { called = true; };
            var queueHandler = new DataChangeQueueHandler(1, false, m_factory, discardedValueHandler);

            queueHandler.SetQueueSize(1, true, DiagnosticsMasks.All);


            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            queueHandler.QueueValue(dataValue, statuscode);

            var statuscode2 = new ServiceResult(StatusCodes.BadAggregateNotSupported);
            var dataValue2 = new DataValue(new Variant(false));

            queueHandler.QueueValue(dataValue2, statuscode2);

            Assert.That(called, Is.True);

            bool success = queueHandler.PublishSingleValue(out var result, out var resultError);

            Assert.That(success, Is.True);
            Assert.That(result, Is.EqualTo(dataValue2));
            Assert.That(result.StatusCode.Overflow, Is.True);
            Assert.That(resultError.StatusCode.Overflow, Is.True);
        }

        [Test]
        public void DataValueQueueDiscardedValueHandlerInvokedNoDiscardOldest()
        {
            bool called = false;
            Action discardedValueHandler = () => { called = true; };
            var queueHandler = new DataChangeQueueHandler(1, false, m_factory, discardedValueHandler);

            queueHandler.SetQueueSize(2, false, DiagnosticsMasks.All);


            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            queueHandler.QueueValue(dataValue, statuscode);
            queueHandler.QueueValue(dataValue, statuscode);

            var statuscode2 = new ServiceResult(StatusCodes.Good);
            var dataValue2 = new DataValue(new Variant(false));

            queueHandler.QueueValue(dataValue2, statuscode2);

            Assert.That(called, Is.True);

            bool success = queueHandler.PublishSingleValue(out var result, out var resultError);

            Assert.That(success, Is.True);
            Assert.That(result, Is.EqualTo(dataValue));
            Assert.That(resultError, Is.EqualTo(statuscode));


            bool success2 = queueHandler.PublishSingleValue(out var result2, out var resultError2);

            Assert.That(success2, Is.True);
            Assert.That(result2, Is.EqualTo(dataValue2));
            Assert.That(result2.StatusCode.Overflow, Is.True);
            Assert.That(resultError2.StatusCode.Overflow, Is.True);
        }

        [Test]
        public void DataValueQueueSamplingIntervalOverwrites()
        {
            bool called = false;
            Action discardedValueHandler = () => { called = true; };

            var queueHandler = new DataChangeQueueHandler(1, false, m_factory, discardedValueHandler);

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

            bool success = queueHandler.PublishSingleValue(out var result, out var resultError);

            Assert.That(success, Is.True);
            Assert.That(result, Is.EqualTo(dataValue2));
            Assert.That(resultError, Is.EqualTo(statuscode2));
        }

        [Test]
        public async Task DataValueQueueSamplingIntervalChangeApplied()
        {
            bool called = false;
            Action discardedValueHandler = () => { called = true; };

            var queueHandler = new DataChangeQueueHandler(1, false, m_factory, discardedValueHandler);


            queueHandler.SetQueueSize(3, true, DiagnosticsMasks.All);

            queueHandler.SetSamplingInterval(2);


            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));


            queueHandler.QueueValue(dataValue, statuscode);


            var statuscode2 = new ServiceResult(StatusCodes.Good);
            var dataValue2 = new DataValue(new Variant(false));

            await Task.Delay(5);

            queueHandler.QueueValue(dataValue2, statuscode2);

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(2));
            Assert.That(called, Is.False);

            bool success = queueHandler.PublishSingleValue(out var result, out var resultError);

            Assert.That(success, Is.True);
            Assert.That(result, Is.EqualTo(dataValue));
            Assert.That(resultError, Is.EqualTo(statuscode));
        }

        [Test]
        public void DataValueQueueSizeChangeRequeuesValues()
        {
            bool called = false;
            Action discardedValueHandler = () => { called = true; };

            var queueHandler = new DataChangeQueueHandler(1, false, m_factory, discardedValueHandler);

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
                bool status = queueHandler.PublishSingleValue(out DataValue result, out ServiceResult resultError);

                Assert.That(status, Is.True);
                Assert.That(result, Is.EqualTo(dataValue));
                Assert.That(resultError, Is.EqualTo(statuscode).Or.Property(nameof(result.StatusCode)).Property(nameof(result.StatusCode.Overflow)).True);
            }

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(0));

            bool status2 = queueHandler.PublishSingleValue(out DataValue result2, out ServiceResult resultError2);

            Assert.That(status2, Is.False);
            Assert.That(result2, Is.Null);
            Assert.That(resultError2, Is.Null);
            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(0));
        }

        [Test]
        public void DataValueDecreaseQueueSizeDiscardsOldest()
        {
            bool called = false;
            Action discardedValueHandler = () => { called = true; };

            var queueHandler = new DataChangeQueueHandler(1, false, m_factory, discardedValueHandler);

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
                bool status = queueHandler.PublishSingleValue(out DataValue result, out ServiceResult resultError);

                Assert.That(status, Is.True);
                Assert.That(result, Is.EqualTo(dataValue2));
                Assert.That(resultError, Is.EqualTo(statuscode).Or.Property(nameof(result.StatusCode)).Property(nameof(result.StatusCode.Overflow)).True);
            }

            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(0));

            bool status2 = queueHandler.PublishSingleValue(out DataValue result2, out ServiceResult resultError2);

            Assert.That(status2, Is.False);
            Assert.That(result2, Is.Null);
            Assert.That(resultError2, Is.Null);
            Assert.That(queueHandler.ItemsInQueue, Is.EqualTo(0));
        }
        #endregion
    }
}
