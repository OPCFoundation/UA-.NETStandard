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
    public class DataChangeMonitoredItemQueueTests
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
                new DataChangeMonitoredItemQueue(true, 1, m_telemetry));
        }

        [Test]
        public void ConstructorSucceedsForNonDurable()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 42, m_telemetry);
            Assert.That(queue.MonitoredItemId, Is.EqualTo(42));
            Assert.That(queue.QueueSize, Is.Zero);
            Assert.That(queue.ItemsInQueue, Is.Zero);
            Assert.That(queue.IsDurable, Is.False);
        }

        [Test]
        public void EnqueueThrowsWhenQueueSizeNotSet()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            Assert.Throws<InvalidOperationException>(() =>
                queue.Enqueue(new DataValue(), null!));
        }

        [Test]
        public void ResetQueueSetsQueueSize()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            queue.ResetQueue(5, false);

            Assert.That(queue.QueueSize, Is.EqualTo(5));
            Assert.That(queue.ItemsInQueue, Is.Zero);
        }

        [Test]
        public void EnqueueAndDequeueSingleValue()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            queue.ResetQueue(3, true);

            var value = new DataValue(new Variant(42));
            var error = new ServiceResult(StatusCodes.Good);
            queue.Enqueue(value, error);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));

            bool dequeued = queue.Dequeue(out DataValue dequeuedValue, out ServiceResult dequeuedError);

            Assert.That(dequeued, Is.True);
            Assert.That(dequeuedValue.WrappedValue, Is.EqualTo(new Variant(42)));
            Assert.That(dequeuedError.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(queue.ItemsInQueue, Is.Zero);
        }

        [Test]
        public void DequeueReturnsFalseWhenEmpty()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            queue.ResetQueue(3, false);

            bool result = queue.Dequeue(out _, out _);

            Assert.That(result, Is.False);
        }

        [Test]
        public void EnqueueDiscardsOldestWhenFull()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            queue.ResetQueue(2, true);

            queue.Enqueue(new DataValue(new Variant(1)), null!);
            queue.Enqueue(new DataValue(new Variant(2)), null!);
            queue.Enqueue(new DataValue(new Variant(3)), null!);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(2));

            queue.Dequeue(out DataValue first, out _);
            Assert.That(first.WrappedValue, Is.EqualTo(new Variant(2)));

            queue.Dequeue(out DataValue second, out _);
            Assert.That(second.WrappedValue, Is.EqualTo(new Variant(3)));
        }

        [Test]
        public void EnqueueWrapsAroundBuffer()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            queue.ResetQueue(3, true);

            queue.Enqueue(new DataValue(new Variant(1)), null!);
            queue.Enqueue(new DataValue(new Variant(2)), null!);

            // Dequeue one to advance start
            queue.Dequeue(out _, out _);

            queue.Enqueue(new DataValue(new Variant(3)), null!);
            queue.Enqueue(new DataValue(new Variant(4)), null!);

            Assert.That(queue.ItemsInQueue, Is.EqualTo(3));

            queue.Dequeue(out DataValue val, out _);
            Assert.That(val.WrappedValue, Is.EqualTo(new Variant(2)));
        }

        [Test]
        public void TryPeekLastValueReturnsFalseWhenEmpty()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            queue.ResetQueue(3, false);

            bool result = queue.TryPeekLastValue(out _);

            Assert.That(result, Is.False);
        }

        [Test]
        public void TryPeekLastValueReturnsLastEnqueued()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            queue.ResetQueue(3, true);

            queue.Enqueue(new DataValue(new Variant(10)), null!);
            queue.Enqueue(new DataValue(new Variant(20)), null!);

            bool result = queue.TryPeekLastValue(out DataValue value);

            Assert.That(result, Is.True);
            Assert.That(value.WrappedValue, Is.EqualTo(new Variant(20)));
        }

        [Test]
        public void TryPeekOldestValueReturnsFalseWhenEmpty()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            queue.ResetQueue(3, false);

            bool result = queue.TryPeekOldestValue(out _);

            Assert.That(result, Is.False);
        }

        [Test]
        public void TryPeekOldestValueReturnsFirstEnqueued()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            queue.ResetQueue(3, true);

            queue.Enqueue(new DataValue(new Variant(10)), null!);
            queue.Enqueue(new DataValue(new Variant(20)), null!);

            bool result = queue.TryPeekOldestValue(out DataValue value);

            Assert.That(result, Is.True);
            Assert.That(value.WrappedValue, Is.EqualTo(new Variant(10)));
        }

        [Test]
        public void OverwriteLastValueThrowsWhenEmpty()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            queue.ResetQueue(3, false);

            Assert.Throws<InvalidOperationException>(() =>
                queue.OverwriteLastValue(new DataValue(), null!));
        }

        [Test]
        public void OverwriteLastValueReplacesLastEnqueued()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            queue.ResetQueue(3, true);

            queue.Enqueue(new DataValue(new Variant(10)), null!);
            queue.Enqueue(new DataValue(new Variant(20)), null!);

            queue.OverwriteLastValue(new DataValue(new Variant(99)), null!);

            queue.Dequeue(out DataValue first, out _);
            Assert.That(first.WrappedValue, Is.EqualTo(new Variant(10)));

            queue.Dequeue(out DataValue second, out _);
            Assert.That(second.WrappedValue, Is.EqualTo(new Variant(99)));
        }

        [Test]
        public void ResetQueueWithErrorsTracksErrors()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            queue.ResetQueue(3, true);

            var error = new ServiceResult(StatusCodes.BadOutOfRange);
            queue.Enqueue(new DataValue(new Variant(1)), error);

            queue.Dequeue(out _, out ServiceResult dequeuedError);
            Assert.That(dequeuedError.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void ResetQueueWithoutErrorsDoesNotTrackErrors()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            queue.ResetQueue(3, false);

            queue.Enqueue(new DataValue(new Variant(1)), new ServiceResult(StatusCodes.BadOutOfRange));

            queue.Dequeue(out _, out ServiceResult dequeuedError);
            Assert.That(dequeuedError, Is.Null);
        }

        [Test]
        public void TryPeekLastValueAfterWrapAround()
        {
            using var queue = new DataChangeMonitoredItemQueue(false, 1, m_telemetry);
            queue.ResetQueue(3, true);

            // Fill and dequeue to force wrap-around
            queue.Enqueue(new DataValue(new Variant(1)), null!);
            queue.Enqueue(new DataValue(new Variant(2)), null!);
            queue.Enqueue(new DataValue(new Variant(3)), null!);
            queue.Dequeue(out _, out _);
            queue.Dequeue(out _, out _);

            // Now add more after wrap
            queue.Enqueue(new DataValue(new Variant(4)), null!);
            queue.Enqueue(new DataValue(new Variant(5)), null!);

            bool result = queue.TryPeekLastValue(out DataValue value);
            Assert.That(result, Is.True);
            Assert.That(value.WrappedValue, Is.EqualTo(new Variant(5)));
        }
    }
}
