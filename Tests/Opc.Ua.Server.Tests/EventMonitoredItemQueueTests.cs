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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    public class EventMonitoredItemQueueTests
    {
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void ConstructorThrowsWhenDurableRequested()
        {
            Assert.Throws<ArgumentException>(() =>
                new EventMonitoredItemQueue(true, 1, m_telemetry));
        }

        [Test]
        public void ConstructorSucceedsForNonDurable()
        {
            using var queue = new EventMonitoredItemQueue(false, 42, m_telemetry);
            Assert.That(queue.MonitoredItemId, Is.EqualTo(42));
            Assert.That(queue.QueueSize, Is.Zero);
            Assert.That(queue.ItemsInQueue, Is.Zero);
            Assert.That(queue.IsDurable, Is.False);
        }

        [Test]
        public void EnqueueThrowsWhenQueueSizeIsZero()
        {
            using var queue = new EventMonitoredItemQueue(false, 1, m_telemetry);
            var item = new EventFieldList();

            Assert.Throws<ServiceResultException>(() => queue.Enqueue(item));
        }

        [Test]
        public void SetQueueSizeUpdatesSize()
        {
            using var queue = new EventMonitoredItemQueue(false, 1, m_telemetry);
            queue.SetQueueSize(5, true);

            Assert.That(queue.QueueSize, Is.EqualTo(5));
        }

        [Test]
        public void EnqueueAndDequeueSingleEvent()
        {
            using var queue = new EventMonitoredItemQueue(false, 1, m_telemetry);
            queue.SetQueueSize(5, true);

            var item = new EventFieldList { Handle = new object() };
            queue.Enqueue(item);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            bool dequeued = queue.Dequeue(out EventFieldList result);

            Assert.That(dequeued, Is.True);
            Assert.That(result, Is.SameAs(item));
            Assert.That(queue.ItemsInQueue, Is.Zero);
        }

        [Test]
        public void DequeueReturnsFalseWhenEmpty()
        {
            using var queue = new EventMonitoredItemQueue(false, 1, m_telemetry);
            queue.SetQueueSize(5, true);

            bool result = queue.Dequeue(out _);

            Assert.That(result, Is.False);
        }

        [Test]
        public void EnqueueDiscardsOldestWhenFull()
        {
            using var queue = new EventMonitoredItemQueue(false, 1, m_telemetry);
            queue.SetQueueSize(2, true);

            var event1 = new EventFieldList { Handle = "first" };
            var event2 = new EventFieldList { Handle = "second" };
            var event3 = new EventFieldList { Handle = "third" };

            queue.Enqueue(event1);
            queue.Enqueue(event2);
            queue.Enqueue(event3);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(2));

            queue.Dequeue(out EventFieldList first);
            Assert.That(first.Handle, Is.EqualTo("second"));

            queue.Dequeue(out EventFieldList second);
            Assert.That(second.Handle, Is.EqualTo("third"));
        }

        [Test]
        public void SetQueueSizeDiscardOldestTruncatesFromFront()
        {
            using var queue = new EventMonitoredItemQueue(false, 1, m_telemetry);
            queue.SetQueueSize(5, true);

            for (int i = 0; i < 5; i++)
            {
                queue.Enqueue(new EventFieldList { Handle = $"event{i}" });
            }

            queue.SetQueueSize(2, true);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(2));

            queue.Dequeue(out EventFieldList first);
            Assert.That(first.Handle, Is.EqualTo("event3"));

            queue.Dequeue(out EventFieldList second);
            Assert.That(second.Handle, Is.EqualTo("event4"));
        }

        [Test]
        public void SetQueueSizeDiscardNewestTruncatesFromEnd()
        {
            using var queue = new EventMonitoredItemQueue(false, 1, m_telemetry);
            queue.SetQueueSize(5, true);

            for (int i = 0; i < 5; i++)
            {
                queue.Enqueue(new EventFieldList { Handle = $"event{i}" });
            }

            queue.SetQueueSize(2, false);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(2));

            queue.Dequeue(out EventFieldList first);
            Assert.That(first.Handle, Is.EqualTo("event0"));

            queue.Dequeue(out EventFieldList second);
            Assert.That(second.Handle, Is.EqualTo("event1"));
        }

        [Test]
        public void IsEventContainedInQueueReturnsTrueForMatchingHandle()
        {
            using var queue = new EventMonitoredItemQueue(false, 1, m_telemetry);
            queue.SetQueueSize(10, true);

            var mockFilterTarget = new MockFilterTarget();
            // The Handle must be the same reference as the IFilterTarget instance
            var item = new EventFieldList { Handle = mockFilterTarget };
            queue.Enqueue(item);

            bool result = queue.IsEventContainedInQueue(mockFilterTarget);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsEventContainedInQueueReturnsFalseWhenNotPresent()
        {
            using var queue = new EventMonitoredItemQueue(false, 1, m_telemetry);
            queue.SetQueueSize(10, true);

            var item = new EventFieldList { Handle = new object() };
            queue.Enqueue(item);

            var mockFilterTarget = new MockFilterTarget();
            bool result = queue.IsEventContainedInQueue(mockFilterTarget);

            Assert.That(result, Is.False);
        }

        [Test]
        public void IsEventContainedInQueueReturnsFalseWhenEmpty()
        {
            using var queue = new EventMonitoredItemQueue(false, 1, m_telemetry);
            queue.SetQueueSize(10, true);

            var mockFilterTarget = new MockFilterTarget();
            bool result = queue.IsEventContainedInQueue(mockFilterTarget);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Minimal IFilterTarget implementation for testing IsEventContainedInQueue,
        /// which checks reference equality on Handle.
        /// </summary>
        private sealed class MockFilterTarget : IFilterTarget
        {
            public Variant GetAttributeValue(
                IFilterContext context,
                NodeId typeDefinitionId,
                ArrayOf<QualifiedName> relativePath,
                uint attributeId,
                NumericRange indexRange)
            {
                return default;
            }

            public bool IsTypeOf(IFilterContext context, NodeId typeDefinitionId)
            {
                return false;
            }
        }
    }
}
