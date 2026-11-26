// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public class DataChangeNotificationBufferTests
    {
        /// <summary>
        /// Verifies that a rented buffer has the next power-of-two capacity.
        /// </summary>
        [Test]
        public void RentRoundsCapacityUpToPowerOfTwo()
        {
            var buffer = DataChangeNotificationBuffer.Rent(600);

            Assert.That(buffer.Count, Is.EqualTo(600));
            Assert.That(buffer.Items, Has.Length.EqualTo(1024));
            buffer.Return();
        }

        /// <summary>
        /// Verifies that a small count gets the smallest pooled capacity.
        /// </summary>
        [Test]
        public void RentOfOneGetsTheSmallestCapacity()
        {
            var buffer = DataChangeNotificationBuffer.Rent(1);

            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(buffer.Items, Has.Length.EqualTo(DataChangeNotificationBuffer.MinCapacity));
            buffer.Return();
        }

        /// <summary>
        /// Verifies that returning a buffer releases the notifications it held.
        /// </summary>
        [Test]
        public void ReturnClearsTheNotifications()
        {
            // a bucket that no message of another test uses, so nobody rents the buffer again
            // before the asserts.
            var buffer = DataChangeNotificationBuffer.Rent(1500);
            MonitoredItemNotification[] items = buffer.Items;
            for (int ii = 0; ii < 3; ii++)
            {
                items[ii] = new MonitoredItemNotification
                {
                    ClientHandle = (uint)ii + 1,
                    Value = new DataValue(new Variant("value" + ii))
                };
            }

            buffer.Return();

            Assert.That(buffer.Count, Is.Zero);
            for (int ii = 0; ii < 3; ii++)
            {
                Assert.That(items[ii], Is.Default);
            }
        }

        /// <summary>
        /// Verifies that a returned buffer is handed out again for a count of the same bucket.
        /// </summary>
        [Test]
        public void ReturnedBufferIsReused()
        {
            // the largest pooled bucket, which no other test rents from.
            var first = DataChangeNotificationBuffer.Rent(DataChangeNotificationBuffer.MaxPooledCapacity - 1);
            first.Return();

            var second = DataChangeNotificationBuffer.Rent(DataChangeNotificationBuffer.MaxPooledCapacity);

            Assert.That(second, Is.SameAs(first));
            Assert.That(second.Count, Is.EqualTo(DataChangeNotificationBuffer.MaxPooledCapacity));
            second.Return();
        }

        /// <summary>
        /// Verifies that a buffer above the largest pooled capacity is sized exactly and never
        /// retained.
        /// </summary>
        [Test]
        public void OversizedBufferIsNotRetained()
        {
            int count = DataChangeNotificationBuffer.MaxPooledCapacity + 1;
            var first = DataChangeNotificationBuffer.Rent(count);
            Assert.That(first.Items, Has.Length.EqualTo(count));
            first.Return();

            var second = DataChangeNotificationBuffer.Rent(count);

            Assert.That(second, Is.Not.SameAs(first));
            second.Return();
        }

        /// <summary>
        /// Verifies that each bucket is bounded by count and retained bytes.
        /// </summary>
        [Test]
        public void BucketsAreBoundedByCountAndBytes()
        {
            int entryBytes = DataChangeNotificationBuffer.EntryBytes;
            for (int capacity = DataChangeNotificationBuffer.MinCapacity;
                capacity <= DataChangeNotificationBuffer.MaxPooledCapacity;
                capacity <<= 1)
            {
                int max = DataChangeNotificationBuffer.GetMaxRetainedCount(capacity);
                Assert.That(max, Is.InRange(1, DataChangeNotificationBuffer.MaxBuffersPerBucket));
                Assert.That(
                    (long)max * capacity * entryBytes,
                    Is.LessThanOrEqualTo(DataChangeNotificationBuffer.MaxRetainedBytesPerBucket));
            }
            Assert.That(
                DataChangeNotificationBuffer.GetMaxRetainedCount(DataChangeNotificationBuffer.MinCapacity),
                Is.EqualTo(DataChangeNotificationBuffer.MaxBuffersPerBucket));
        }

        /// <summary>
        /// Verifies that a full bucket drops returned buffers instead of growing past its bound.
        /// </summary>
        [Test]
        public void FullBucketDropsReturnedBuffers()
        {
            // the 2048 bucket, which no message of another test rents from (not the 4096
            // bucket, whose queue order ReturnedBufferIsReused relies on).
            const int count = 1800;
            int max = DataChangeNotificationBuffer.GetMaxRetainedCount(count);
            var returned = new HashSet<DataChangeNotificationBuffer>();
            var rented = new DataChangeNotificationBuffer[max + 1];
            for (int ii = 0; ii < rented.Length; ii++)
            {
                rented[ii] = DataChangeNotificationBuffer.Rent(count);
            }
            foreach (DataChangeNotificationBuffer buffer in rented)
            {
                returned.Add(buffer);
                buffer.Return();
            }

            int reused = 0;
            for (int ii = 0; ii < rented.Length; ii++)
            {
                rented[ii] = DataChangeNotificationBuffer.Rent(count);
                if (returned.Contains(rented[ii]))
                {
                    reused++;
                }
            }
            foreach (DataChangeNotificationBuffer buffer in rented)
            {
                buffer.Return();
            }

            Assert.That(returned, Has.Count.EqualTo(max + 1));
            Assert.That(reused, Is.LessThanOrEqualTo(max));
        }

        /// <summary>
        /// Verifies that renting no notifications is rejected.
        /// </summary>
        [Test]
        public void RentRejectsNonPositiveCount()
        {
            Assert.That(() => DataChangeNotificationBuffer.Rent(0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
