/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;
using Quickstarts.Servers;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that durable queues wait for resident batches and release file readers before deleting restored data.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class DurableBatchRestoreRegressionTests
    {
        /// <summary>
        /// Verifies that a nonresident data-change batch requests restoration without losing its queued value.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void DataBatchMustBecomeResidentBeforeDequeue(bool persisted)
        {
            var persistor = new Mock<IBatchPersistor>();
            using var queue = new DurableDataChangeMonitoredItemQueue(
                true, 1, persistor.Object, NUnitTelemetryContext.Create());
            queue.ResetQueue(2, false);
            var expected = new DataValue(new Variant(42));
            queue.Enqueue(expected, null);
            DataChangeBatch batch = queue.ToStorableQueue().DequeueBatch;
            if (persisted)
            {
                batch.SetPersisted();
            }
            else
            {
                batch.PersistingInProgress = true;
            }

            Assert.That(queue.Dequeue(out DataValue pending, out ServiceResult error), Is.False);
            Assert.That(pending.IsNull, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));
            persistor.Verify(p => p.RequestBatchRestore(batch), Times.Once);

            batch.PersistingInProgress = false;
            batch.Restore([(expected, null)]);
            Assert.That(queue.Dequeue(out DataValue actual, out _), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(queue.ItemsInQueue, Is.Zero);
        }

        /// <summary>
        /// Verifies that a nonresident event batch requests restoration without consuming the pending event.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void EventBatchMustBecomeResidentBeforeDequeue(bool persisted)
        {
            var persistor = new Mock<IBatchPersistor>();
            using var queue = new DurableEventMonitoredItemQueue(
                true, 1, persistor.Object, NUnitTelemetryContext.Create());
            queue.SetQueueSize(2, false);
            var expected = new EventFieldList { ClientHandle = 42 };
            queue.Enqueue(expected);
            // the live batch: ToStorableQueue returns detached copies.
            var batch = (EventBatch)typeof(DurableEventMonitoredItemQueue)
                .GetField("m_dequeueBatch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(queue)!;
            if (persisted)
            {
                batch.SetPersisted();
            }
            else
            {
                batch.PersistingInProgress = true;
            }

            Assert.That(queue.Dequeue(out EventFieldList pending), Is.False);
            Assert.That(pending, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));
            persistor.Verify(p => p.RequestBatchRestore(batch), Times.Once);

            batch.PersistingInProgress = false;
            batch.Restore([expected]);
            Assert.That(queue.Dequeue(out EventFieldList actual), Is.True);
            Assert.That(actual.ClientHandle, Is.EqualTo(42));
            Assert.That(queue.ItemsInQueue, Is.Zero);
        }

        /// <summary>
        /// Verifies that the duplicate event check also scans the dequeue batch, which holds
        /// the newest event right after a full enqueue batch was handed over to it.
        /// </summary>
        [Test]
        public void DuplicateCheckScansTheDequeueBatch()
        {
            var persistor = new Mock<IBatchPersistor>();
            using var queue = new DurableEventMonitoredItemQueue(
                true, 1, persistor.Object, NUnitTelemetryContext.Create());
            queue.SetQueueSize(2000, false);
            IFilterTarget instance = new Mock<IFilterTarget>().Object;
            for (uint i = 1; i < kEventBatchSize; i++)
            {
                queue.Enqueue(new EventFieldList { ClientHandle = i });
            }
            queue.Enqueue(new EventFieldList { ClientHandle = kEventBatchSize, Handle = instance });

            StorableEventQueue stored = queue.ToStorableQueue();
            Assert.That(stored.DequeueBatch, Is.Not.SameAs(stored.EnqueueBatch));
            Assert.That(stored.EnqueueBatch.Events, Is.Empty);
            Assert.That(queue.IsEventContainedInQueue(instance), Is.True);
            Assert.That(queue.IsEventContainedInQueue(new Mock<IFilterTarget>().Object), Is.False);
        }

        /// <summary>
        /// A trailing filtered-retain event is queued with a wrapper of the event instance as
        /// its handle; the duplicate check must still match the instance.
        /// </summary>
        [Test]
        public void DuplicateCheckMatchesWrappedFilteredRetainEvent()
        {
            var persistor = new Mock<IBatchPersistor>();
            using var queue = new DurableEventMonitoredItemQueue(
                true, 1, persistor.Object, NUnitTelemetryContext.Create());
            queue.SetQueueSize(10, false);
            IFilterTarget instance = new Mock<IFilterTarget>().Object;
            queue.Enqueue(new EventFieldList { ClientHandle = 1, Handle = new FilteredRetainTarget(instance) });

            Assert.That(queue.IsEventContainedInQueue(instance), Is.True);
            Assert.That(queue.IsEventContainedInQueue(new Mock<IFilterTarget>().Object), Is.False);
        }

        /// <summary>
        /// Verifies that an event queue restored from its stored form reports and delivers
        /// every stored event once, in order.
        /// </summary>
        [TestCase(3u)]
        [TestCase(1500u)]
        public void RestoredEventQueueDeliversEveryStoredEventOnce(uint count)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var persistor = new Mock<IBatchPersistor>();
            using var queue = new DurableEventMonitoredItemQueue(
                true, 1, persistor.Object, telemetry);
            queue.SetQueueSize(2000, false);
            for (uint i = 1; i <= count; i++)
            {
                queue.Enqueue(new EventFieldList { ClientHandle = i });
            }

            StorableEventQueue template = RoundTrip(
                queue.ToStorableQueue(),
                ServiceMessageContext.Create(telemetry));
            using var restored = new DurableEventMonitoredItemQueue(template, persistor.Object);

            Assert.That(restored.ItemsInQueue, Is.EqualTo((int)count));
            for (uint i = 1; i <= count; i++)
            {
                Assert.That(restored.Dequeue(out EventFieldList value), Is.True);
                Assert.That(value.ClientHandle, Is.EqualTo(i));
            }
            Assert.That(restored.ItemsInQueue, Is.Zero);
            Assert.That(restored.Dequeue(out _), Is.False);

            // the restored queue keeps working without serving stale events.
            restored.Enqueue(new EventFieldList { ClientHandle = 4242 });
            Assert.That(restored.Dequeue(out EventFieldList next), Is.True);
            Assert.That(next.ClientHandle, Is.EqualTo(4242u));
            Assert.That(restored.Dequeue(out _), Is.False);
        }

        /// <summary>
        /// Batches spilled to disk are stored with the queue: after a restart (new batch ids,
        /// no batch files) every event is still delivered once, in order.
        /// </summary>
        [Test]
        [NonParallelizable]
        public void SpilledBatchesSurviveStoreAndRestore()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using IDisposable scope = AmbientMessageContext.SetScopedContext(
                ServiceMessageContext.Create(telemetry));
            string batchDirectory = Path.Combine(Environment.CurrentDirectory, "Durable Subscriptions", "Batches");
            void DeleteBatchFiles()
            {
                if (Directory.Exists(batchDirectory))
                {
                    foreach (string file in Directory.GetFiles(batchDirectory, "77_*"))
                    {
                        File.Delete(file);
                    }
                }
            }

            DeleteBatchFiles();
            try
            {
                var persistor = new BatchPersistor(telemetry);
                const uint count = (kEventBatchSize * 3) + 500;
                using var queue = new DurableEventMonitoredItemQueue(true, 77, persistor, telemetry);
                queue.SetQueueSize(10000, false);
                for (uint i = 1; i <= count; i++)
                {
                    queue.Enqueue(new EventFieldList { ClientHandle = i });
                }

                // the queue spills full batches in the background; wait until one is on disk
                // and no longer held in memory.
                var batches = (List<EventBatch>)typeof(DurableEventMonitoredItemQueue)
                    .GetField("m_eventBatches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(queue)!;
                bool Spilled() => batches.Exists(b => b.IsPersisted && b.Events == null);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                while (!Spilled() && stopwatch.Elapsed < TimeSpan.FromSeconds(10))
                {
                    Thread.Sleep(10);
                }
                Assert.That(Spilled(), Is.True, "a batch must be spilled to disk for this test");

                StorableEventQueue template = RoundTrip(
                    queue.ToStorableQueue(),
                    ServiceMessageContext.Create(telemetry));
                using var restored = new DurableEventMonitoredItemQueue(template, new Mock<IBatchPersistor>().Object);

                Assert.That(restored.ItemsInQueue, Is.EqualTo((int)count));
                for (uint i = 1; i <= count; i++)
                {
                    Assert.That(restored.Dequeue(out EventFieldList value), Is.True, $"event {i}");
                    Assert.That(value.ClientHandle, Is.EqualTo(i));
                }
                Assert.That(restored.Dequeue(out _), Is.False);
            }
            finally
            {
                DeleteBatchFiles();
            }
        }

        /// <summary>
        /// A batch whose spill is in flight when the queue is stored may lose its events to
        /// the persistor before it is encoded; the stored queue holds a stable copy.
        /// </summary>
        [Test]
        public void BatchSpilledWhileStoringKeepsItsEvents()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            const uint count = (kEventBatchSize * 2) + 500;
            using var queue = new DurableEventMonitoredItemQueue(
                true, 78, new Mock<IBatchPersistor>().Object, telemetry);
            queue.SetQueueSize(10000, false);
            for (uint i = 1; i <= count; i++)
            {
                queue.Enqueue(new EventFieldList { ClientHandle = i });
            }

            var batches = (List<EventBatch>)typeof(DurableEventMonitoredItemQueue)
                .GetField("m_eventBatches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(queue)!;
            Assert.That(batches, Is.Not.Empty);
            EventBatch inFlight = batches[0];
            inFlight.PersistingInProgress = true;

            StorableEventQueue stored = queue.ToStorableQueue();

            // the persistor finishes the spill before the stored queue is encoded.
            lock (inFlight)
            {
                inFlight.SetPersisted();
            }

            StorableEventQueue template = RoundTrip(stored, ServiceMessageContext.Create(telemetry));
            using var restored = new DurableEventMonitoredItemQueue(template, new Mock<IBatchPersistor>().Object);
            Assert.That(restored.ItemsInQueue, Is.EqualTo((int)count));
            for (uint i = 1; i <= count; i++)
            {
                Assert.That(restored.Dequeue(out EventFieldList value), Is.True, $"event {i}");
                Assert.That(value.ClientHandle, Is.EqualTo(i));
            }
        }

        /// <summary>
        /// Files written before the shared enqueue/dequeue batch was stored once hold that
        /// batch in both slots; it is restored once, so every event is delivered once.
        /// </summary>
        [Test]
        public void LegacyFileWithSharedBatchInBothSlotsDeliversEventsOnce()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            var events = new List<EventFieldList>
            {
                new() { ClientHandle = 1 },
                new() { ClientHandle = 2 },
                new() { ClientHandle = 3 }
            };
            var batch = new EventBatch(events, kEventBatchSize, 5);

            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                // the previous writer: queue header, then the same batch as enqueue and dequeue batch.
                encoder.WriteBoolean(null, true);
                encoder.WriteUInt32(null, 5);
                encoder.WriteUInt32(null, 100);
                WriteLegacyEventBatch(encoder, batch);
                WriteLegacyEventBatch(encoder, batch);
                encoder.WriteInt32(null, 0);
            }
            stream.Position = 0;
            StorableEventQueue template;
            using (var decoder = new BinaryDecoder(stream, context, true))
            {
                template = DurableMonitoredItemQueueFactory.DecodeEventQueue(decoder);
            }

            using var restored = new DurableEventMonitoredItemQueue(template, new Mock<IBatchPersistor>().Object);
            Assert.That(restored.ItemsInQueue, Is.EqualTo(3));
            for (uint i = 1; i <= 3; i++)
            {
                Assert.That(restored.Dequeue(out EventFieldList value), Is.True);
                Assert.That(value.ClientHandle, Is.EqualTo(i));
            }
            Assert.That(restored.Dequeue(out _), Is.False);
        }

        private static void WriteLegacyEventBatch(BinaryEncoder encoder, EventBatch batch)
        {
            encoder.WriteBoolean(null, true);
            encoder.WriteGuid(null, batch.Id);
            encoder.WriteUInt32(null, batch.BatchSize);
            encoder.WriteUInt32(null, batch.MonitoredItemId);
            encoder.WriteBoolean(null, false);
            encoder.WriteInt32(null, batch.Events.Count);
            foreach (EventFieldList value in batch.Events)
            {
                encoder.WriteEncodeableAsExtensionObject(null, value);
            }
        }

        private static StorableEventQueue RoundTrip(
            StorableEventQueue original,
            IServiceMessageContext context)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                DurableMonitoredItemQueueFactory.EncodeEventQueue(encoder, original);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            return DurableMonitoredItemQueueFactory.DecodeEventQueue(decoder);
        }

        private const uint kEventBatchSize = 1000;

        /// <summary>
        /// Verifies that restoring a persisted batch closes its reader so the backing file can be deleted.
        /// </summary>
        [Test]
        [NonParallelizable]
        public void RestoreClosesItsReaderBeforeDeletingTheBatchFile()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using IDisposable scope = AmbientMessageContext.SetScopedContext(
                ServiceMessageContext.Create(telemetry));
            var expected = new DataValue(new Variant(42));
            var batch = new DataChangeBatch([(expected, null)], 1, 42);
            var persistor = new BatchPersistor(telemetry);
            string path = Path.Combine(
                Environment.CurrentDirectory,
                "Durable Subscriptions",
                "Batches",
                $"{batch.MonitoredItemId}_{batch.Id}_batch.bin");
            try
            {
                persistor.PersistSynchronously(batch);
                Assert.That(batch.IsPersisted, Is.True);
                Assert.That(File.Exists(path), Is.True);

                persistor.RestoreSynchronously(batch);

                Assert.That(batch.IsPersisted, Is.False);
                Assert.That(batch.Values, Has.Count.EqualTo(1));
                Assert.That(batch.Values[0].Item1, Is.EqualTo(expected));
                Assert.That(File.Exists(path), Is.False);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
