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
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public class EventNotificationBufferTests
    {
        /// <summary>
        /// Verifies that a rented buffer has the next power-of-two capacity for both arrays.
        /// </summary>
        [Test]
        public void RentRoundsCapacityUpToPowerOfTwo()
        {
            var buffer = EventNotificationBuffer.Rent(100);

            Assert.That(buffer.Count, Is.EqualTo(100));
            Assert.That(buffer.Events, Has.Length.EqualTo(128));
            Assert.That(buffer.Stamps, Has.Length.EqualTo(128));
            buffer.Return();
        }

        /// <summary>
        /// Verifies that returning a buffer releases the events and admission stamps it held.
        /// </summary>
        [Test]
        public void ReturnClearsEventsAndStamps()
        {
            // a bucket that no message of another test uses, so nobody rents the buffer again
            // before the asserts.
            var buffer = EventNotificationBuffer.Rent(1500);
            EventFieldList[] events = buffer.Events;
            object?[] stamps = buffer.Stamps;
            for (int ii = 0; ii < 3; ii++)
            {
                events[ii] = new EventFieldList { ClientHandle = (uint)ii + 1, EventFields = [new Variant(ii)] };
                stamps[ii] = new object();
            }

            buffer.Return();

            Assert.That(buffer.Count, Is.Zero);
            for (int ii = 0; ii < 3; ii++)
            {
                Assert.That(events[ii].ClientHandle, Is.Zero);
                Assert.That(events[ii].EventFields.IsNull, Is.True);
                Assert.That(stamps[ii], Is.Null);
            }
        }

        /// <summary>
        /// Verifies that a returned buffer is handed out again for a count of the same bucket.
        /// </summary>
        [Test]
        public void ReturnedBufferIsReused()
        {
            // the largest pooled bucket, which no other test rents from.
            var first = EventNotificationBuffer.Rent(EventNotificationBuffer.MaxPooledCapacity - 1);
            first.Return();

            var second = EventNotificationBuffer.Rent(EventNotificationBuffer.MaxPooledCapacity);

            Assert.That(second, Is.SameAs(first));
            Assert.That(second.Count, Is.EqualTo(EventNotificationBuffer.MaxPooledCapacity));
            second.Return();
        }

        /// <summary>
        /// Verifies that a buffer above the largest pooled capacity is sized exactly and never
        /// retained.
        /// </summary>
        [Test]
        public void OversizedBufferIsNotRetained()
        {
            int count = EventNotificationBuffer.MaxPooledCapacity + 1;
            var first = EventNotificationBuffer.Rent(count);
            Assert.That(first.Events, Has.Length.EqualTo(count));
            first.Return();

            var second = EventNotificationBuffer.Rent(count);

            Assert.That(second, Is.Not.SameAs(first));
            second.Return();
        }

        /// <summary>
        /// Verifies that each bucket is bounded by count and retained bytes.
        /// </summary>
        [Test]
        public void BucketsAreBoundedByCountAndBytes()
        {
            int entryBytes = Unsafe.SizeOf<EventFieldList>() + IntPtr.Size;
            for (int capacity = EventNotificationBuffer.MinCapacity;
                capacity <= EventNotificationBuffer.MaxPooledCapacity;
                capacity <<= 1)
            {
                int max = EventNotificationBuffer.GetMaxRetainedCount(capacity);
                Assert.That(max, Is.InRange(1, EventNotificationBuffer.MaxBuffersPerBucket));
                Assert.That(
                    (long)max * capacity * entryBytes,
                    Is.LessThanOrEqualTo(EventNotificationBuffer.MaxRetainedBytesPerBucket));
            }
            Assert.That(
                EventNotificationBuffer.GetMaxRetainedCount(EventNotificationBuffer.MinCapacity),
                Is.EqualTo(EventNotificationBuffer.MaxBuffersPerBucket));
        }

        /// <summary>
        /// Verifies that renting no events is rejected.
        /// </summary>
        [Test]
        public void RentRejectsNonPositiveCount()
        {
            Assert.That(() => EventNotificationBuffer.Rent(0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
