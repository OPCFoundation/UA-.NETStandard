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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="SharedKeyValueMonitoredItemQueueFactory"/> and its integration with
    /// <see cref="SharedKeyValueSubscriptionStore"/> queue restore.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Category("Subscription")]
    [Parallelizable(ParallelScope.All)]
    public class SharedKeyValueMonitoredItemQueueFactoryTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ProtectedQueuesRejectAnotherMonitoredItemKeyAsync(bool eventQueue)
        {
            using var kv = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var random = RandomNumberGenerator.Create();
            byte[] key = new byte[32];
            random.GetBytes(key);
            using var protector = new AesCbcHmacRecordProtector(key);
            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, context, protector, telemetry);
            if (eventQueue)
            {
                IEventMonitoredItemQueue queue = factory.CreateEventQueue(false, 11);
                queue.SetQueueSize(2, false);
                queue.Enqueue(new EventFieldList { ClientHandle = 5, EventFields = [Variant.From(12)] });
            }
            else
            {
                IDataChangeMonitoredItemQueue queue = factory.CreateDataChangeQueue(false, 11);
                queue.ResetQueue(2, false);
                queue.Enqueue(new DataValue(Variant.From(12)), ServiceResult.Good);
            }
            await factory.FlushAsync().ConfigureAwait(false);
            string prefix = eventQueue ? "monitored-item-queue/event/" : "monitored-item-queue/data/";
            (bool found, ByteString record) = await kv.TryGetAsync(prefix + "11").ConfigureAwait(false);
            Assert.That(found, Is.True);
            await kv.SetAsync(prefix + "12", record).ConfigureAwait(false);

            if (eventQueue)
            {
                Assert.That(await factory.RestoreEventQueueAsync(12).ConfigureAwait(false), Is.Null);
                IEventMonitoredItemQueue restored = (await factory.RestoreEventQueueAsync(11).ConfigureAwait(false))!;
                Assert.That(restored, Is.Not.Null);
                Assert.That(restored.Dequeue(out EventFieldList value), Is.True);
                Assert.That(value.ClientHandle, Is.EqualTo(5));
                Assert.That(value.EventFields[0], Is.EqualTo(Variant.From(12)));
            }
            else
            {
                Assert.That(await factory.RestoreDataChangeQueueAsync(12).ConfigureAwait(false), Is.Null);
                IDataChangeMonitoredItemQueue restored = (await factory.RestoreDataChangeQueueAsync(11)
                    .ConfigureAwait(false))!;
                Assert.That(restored, Is.Not.Null);
                Assert.That(restored.Dequeue(out DataValue value, out _), Is.True);
                Assert.That(value.WrappedValue, Is.EqualTo(Variant.From(12)));
            }
        }

        [Test]
        public async Task DataChangeQueueMirrorsAndRestoresAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var values = new DataValue[3];
            await using (var factory = new SharedKeyValueMonitoredItemQueueFactory(kv, context, telemetry: telemetry))
            {
                IDataChangeMonitoredItemQueue queue = factory.CreateDataChangeQueue(false, 42);
                queue.ResetQueue(5, false);
                for (int i = 0; i < 3; i++)
                {
                    values[i] = new DataValue(new Variant(i));
                    queue.Enqueue(values[i], ServiceResult.Good);
                }
                await factory.FlushAsync().ConfigureAwait(false);
            }

            await using var restoreFactory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, context, telemetry: telemetry);
            IDataChangeMonitoredItemQueue restored = (await restoreFactory
                .RestoreDataChangeQueueAsync(42)
                .ConfigureAwait(false))!;

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.QueueSize, Is.EqualTo(5));
            Assert.That(restored.ItemsInQueue, Is.EqualTo(3));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(restored.Dequeue(out DataValue result, out _), Is.True);
                Assert.That(result, Is.EqualTo(values[i]));
            }
        }

        [Test]
        public async Task DataChangeQueuePreservesErrorsWhenQueuedAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var error = new ServiceResult(StatusCodes.BadNoData);
            await using (var factory = new SharedKeyValueMonitoredItemQueueFactory(kv, context, telemetry: telemetry))
            {
                IDataChangeMonitoredItemQueue queue = factory.CreateDataChangeQueue(false, 7);
                queue.ResetQueue(3, true);
                queue.Enqueue(new DataValue(new Variant(1)), error);
                await factory.FlushAsync().ConfigureAwait(false);
            }

            await using var restoreFactory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, context, telemetry: telemetry);
            IDataChangeMonitoredItemQueue restored = (await restoreFactory
                .RestoreDataChangeQueueAsync(7)
                .ConfigureAwait(false))!;

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.Dequeue(out _, out ServiceResult restoredError), Is.True);
            Assert.That(restoredError.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
        }

        [Test]
        public async Task EventQueueMirrorsAndRestoresAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            await using (var factory = new SharedKeyValueMonitoredItemQueueFactory(kv, context, telemetry: telemetry))
            {
                IEventMonitoredItemQueue queue = factory.CreateEventQueue(false, 11);
                queue.SetQueueSize(5, true);
                for (uint i = 0; i < 3; i++)
                {
                    queue.Enqueue(new EventFieldList { ClientHandle = i, EventFields = [new Variant(i)] });
                }
                await factory.FlushAsync().ConfigureAwait(false);
            }

            await using var restoreFactory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, context, telemetry: telemetry);
            IEventMonitoredItemQueue restored = (await restoreFactory
                .RestoreEventQueueAsync(11)
                .ConfigureAwait(false))!;

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.QueueSize, Is.EqualTo(5));
            Assert.That(restored.ItemsInQueue, Is.EqualTo(3));
            for (uint i = 0; i < 3; i++)
            {
                Assert.That(restored.Dequeue(out EventFieldList result), Is.True);
                Assert.That(result.ClientHandle, Is.EqualTo(i));
            }
        }

        [Test]
        public async Task DequeueShrinksMirroredQueueAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(kv, context, telemetry: telemetry);
            IDataChangeMonitoredItemQueue queue = factory.CreateDataChangeQueue(false, 3);
            queue.ResetQueue(5, false);
            for (int i = 0; i < 4; i++)
            {
                queue.Enqueue(new DataValue(new Variant(i)), ServiceResult.Good);
            }
            queue.Dequeue(out _, out _);
            queue.Dequeue(out _, out _);
            await factory.FlushAsync().ConfigureAwait(false);

            await using var restoreFactory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, context, telemetry: telemetry);
            IDataChangeMonitoredItemQueue restored = (await restoreFactory
                .RestoreDataChangeQueueAsync(3)
                .ConfigureAwait(false))!;

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.ItemsInQueue, Is.EqualTo(2));
        }

        [Test]
        public async Task OverwriteLastValueUpdatesMirroredSnapshotAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(kv, context, telemetry: telemetry);
            IDataChangeMonitoredItemQueue queue = factory.CreateDataChangeQueue(false, 33);
            queue.ResetQueue(1, false);
            queue.Enqueue(new DataValue(new Variant(1)), ServiceResult.Good);
            queue.OverwriteLastValue(new DataValue(new Variant(99)), ServiceResult.Good);
            await factory.FlushAsync().ConfigureAwait(false);

            await using var restoreFactory = new SharedKeyValueMonitoredItemQueueFactory(
                kv,
                context,
                telemetry: telemetry);
            IDataChangeMonitoredItemQueue restored = (await restoreFactory
                .RestoreDataChangeQueueAsync(33)
                .ConfigureAwait(false))!;

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.Dequeue(out DataValue restoredValue, out _), Is.True);
            Assert.That(restoredValue.WrappedValue.TryGetValue(out int restoredInt), Is.True);
            Assert.That(restoredInt, Is.EqualTo(99));
        }

        [Test]
        public async Task RestoreReturnsNullWhenNothingStoredAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(kv, context, telemetry: telemetry);

            Assert.That(await factory.RestoreDataChangeQueueAsync(99).ConfigureAwait(false), Is.Null);
            Assert.That(await factory.RestoreEventQueueAsync(99).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task DisposingQueueRemovesMirroredStateAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(kv, context, telemetry: telemetry);
            IDataChangeMonitoredItemQueue queue = factory.CreateDataChangeQueue(false, 5);
            queue.ResetQueue(2, false);
            queue.Enqueue(new DataValue(new Variant(1)), ServiceResult.Good);
            await factory.FlushAsync().ConfigureAwait(false);

            Assert.That(await factory.RestoreDataChangeQueueAsync(5).ConfigureAwait(false), Is.Not.Null);

            queue.Dispose();
            await factory.FlushAsync().ConfigureAwait(false);

            Assert.That(await factory.RestoreDataChangeQueueAsync(5).ConfigureAwait(false), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StagedRestoreWritesOnlyOnCommitAsync(bool eventQueue)
        {
            using var kv = new InMemorySharedKeyValueStore();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, CreateContext(), telemetry: telemetry);
            IRestorableMonitoredItemQueue restored = await StoreAndRestoreAsync(factory, eventQueue)
                .ConfigureAwait(false);

            restored.BeginRestore();
            Enqueue(restored, 2);
            await factory.FlushAsync().ConfigureAwait(false);
            Assert.That(await CountStoredAsync(factory, eventQueue).ConfigureAwait(false), Is.EqualTo(1));

            restored.CommitRestore();
            await factory.FlushAsync().ConfigureAwait(false);
            Assert.That(await CountStoredAsync(factory, eventQueue).ConfigureAwait(false), Is.EqualTo(2));

            Enqueue(restored, 3);
            await factory.FlushAsync().ConfigureAwait(false);
            Assert.That(await CountStoredAsync(factory, eventQueue).ConfigureAwait(false), Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AbortedRestoreKeepsTheStoredQueueAsync(bool eventQueue)
        {
            using var kv = new InMemorySharedKeyValueStore();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, CreateContext(), telemetry: telemetry);
            IRestorableMonitoredItemQueue restored = await StoreAndRestoreAsync(factory, eventQueue)
                .ConfigureAwait(false);

            restored.BeginRestore();
            Enqueue(restored, 2);
            restored.AbortRestore();
            restored.CommitRestore();
            ((IDisposable)restored).Dispose();
            await factory.FlushAsync().ConfigureAwait(false);

            Assert.That(await CountStoredAsync(factory, eventQueue).ConfigureAwait(false), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposingAStagedQueueKeepsTheStoredQueueAsync(bool eventQueue)
        {
            using var kv = new InMemorySharedKeyValueStore();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, CreateContext(), telemetry: telemetry);
            IRestorableMonitoredItemQueue restored = await StoreAndRestoreAsync(factory, eventQueue)
                .ConfigureAwait(false);

            // The queue is released before the restore was committed or aborted.
            restored.BeginRestore();
            Enqueue(restored, 2);
            ((IDisposable)restored).Dispose();
            await factory.FlushAsync().ConfigureAwait(false);

            Assert.That(await CountStoredAsync(factory, eventQueue).ConfigureAwait(false), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AbortDuringADrainDropsTheWritesTheDrainTookAsync(bool eventQueue)
        {
            using var inner = new InMemorySharedKeyValueStore();
            var kv = new GatedSharedKeyValueStore(inner);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // One write at a time, so that the write of the restored queue has not started when it is aborted.
            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, CreateContext(), null, telemetry, maxConcurrentWrites: 1);
            IRestorableMonitoredItemQueue restored = await StoreAndRestoreAsync(factory, eventQueue)
                .ConfigureAwait(false);

            // The write of queue 1 holds the drain until queue 2 and the restored queue changed.
            Gate first = kv.Hold(DataChangeKey(1));
            Gate second = kv.Hold(DataChangeKey(2));
            CreateMirroredQueue(factory, 1);
            await first.WaitEnteredAsync().ConfigureAwait(false);
            CreateMirroredQueue(factory, 2);
            Enqueue(restored, 2);
            first.Release();

            // The next drain took both writes; the restore is aborted while it writes queue 2.
            await second.WaitEnteredAsync().ConfigureAwait(false);
            restored.AbortRestore();
            second.Release();
            await factory.FlushAsync().ConfigureAwait(false);

            Assert.That(await CountStoredAsync(factory, eventQueue).ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await CountStoredDataChangeAsync(factory, 2).ConfigureAwait(false), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AbortDuringAFailedDrainDoesNotRetryTheWritesTheDrainTookAsync(bool eventQueue)
        {
            using var inner = new InMemorySharedKeyValueStore();
            var kv = new GatedSharedKeyValueStore(inner);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // One write at a time, so that the write of the restored queue has not started when it is aborted.
            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, CreateContext(), null, telemetry, maxConcurrentWrites: 1);
            IRestorableMonitoredItemQueue restored = await StoreAndRestoreAsync(factory, eventQueue)
                .ConfigureAwait(false);

            Gate first = kv.Hold(DataChangeKey(1));
            Gate second = kv.Hold(DataChangeKey(2));
            Gate restoredWrite = kv.Hold(eventQueue ? EventKey(9) : DataChangeKey(9));
            CreateMirroredQueue(factory, 1);
            await first.WaitEnteredAsync().ConfigureAwait(false);
            CreateMirroredQueue(factory, 2);
            Enqueue(restored, 2);
            first.Release();

            // The drain fails to write queue 2 and the restored queue, which is aborted while it is written.
            await second.WaitEnteredAsync().ConfigureAwait(false);
            second.Fail(new IOException("store unavailable"));
            await restoredWrite.WaitEnteredAsync().ConfigureAwait(false);
            restored.AbortRestore();
            restoredWrite.Fail(new IOException("store unavailable"));

            // The retry writes queue 2 only.
            await factory.FlushAsync().ConfigureAwait(false);

            Assert.That(await CountStoredAsync(factory, eventQueue).ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await CountStoredDataChangeAsync(factory, 2).ConfigureAwait(false), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AbortDuringAStartedWriteLetsOnlyThatWriteCompleteAsync(bool eventQueue)
        {
            using var inner = new InMemorySharedKeyValueStore();
            var kv = new GatedSharedKeyValueStore(inner);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, CreateContext(), telemetry: telemetry);
            IRestorableMonitoredItemQueue restored = await StoreAndRestoreAsync(factory, eventQueue)
                .ConfigureAwait(false);
            string key = eventQueue ? EventKey(9) : DataChangeKey(9);
            int writesBefore = kv.StartedWrites(key);

            // The queue is not staged, so its change is written; the write is in progress when it is aborted.
            Gate held = kv.Hold(key);
            Enqueue(restored, 2);
            await held.WaitEnteredAsync().ConfigureAwait(false);
            restored.AbortRestore();
            Enqueue(restored, 3);
            ((IDisposable)restored).Dispose();
            held.Release();
            await factory.FlushAsync().ConfigureAwait(false);

            Assert.That(kv.StartedWrites(key) - writesBefore, Is.EqualTo(1), "no write starts after the abort");
            Assert.That(
                await CountStoredAsync(factory, eventQueue).ConfigureAwait(false),
                Is.EqualTo(2),
                "the write in progress completes");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StagedQueueStartsNoWriteBeforeItsRestoreIsCommittedAsync(bool eventQueue)
        {
            using var inner = new InMemorySharedKeyValueStore();
            var kv = new GatedSharedKeyValueStore(inner);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, CreateContext(), telemetry: telemetry);
            IRestorableMonitoredItemQueue restored = await StoreAndRestoreAsync(factory, eventQueue)
                .ConfigureAwait(false);
            string key = eventQueue ? EventKey(9) : DataChangeKey(9);
            int writesBefore = kv.StartedWrites(key);

            restored.BeginRestore();
            Enqueue(restored, 2);
            await factory.FlushAsync().ConfigureAwait(false);
            restored.AbortRestore();
            Enqueue(restored, 3);
            ((IDisposable)restored).Dispose();
            await factory.FlushAsync().ConfigureAwait(false);

            Assert.That(kv.StartedWrites(key), Is.EqualTo(writesBefore), "the staged queue starts no write");
            Assert.That(await CountStoredAsync(factory, eventQueue).ConfigureAwait(false), Is.EqualTo(1));
        }

        [Test]
        public async Task WritesOfDifferentQueuesOverlapAndTheWritesOfAQueueKeepTheirOrderAsync()
        {
            using var inner = new InMemorySharedKeyValueStore();
            var kv = new GatedSharedKeyValueStore(inner);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, CreateContext(), telemetry: telemetry);

            // The write of queue 1 holds the drain until queues 2 and 3 changed.
            Gate first = kv.Hold(DataChangeKey(1));
            Gate second = kv.Hold(DataChangeKey(2));
            Gate third = kv.Hold(DataChangeKey(3));
            CreateMirroredQueue(factory, 1);
            await first.WaitEnteredAsync().ConfigureAwait(false);
            IDataChangeMonitoredItemQueue queue2 = CreateMirroredQueue(factory, 2);
            CreateMirroredQueue(factory, 3);
            first.Release();

            // Both writes are in progress at once.
            await second.WaitEnteredAsync().ConfigureAwait(false);
            await third.WaitEnteredAsync().ConfigureAwait(false);

            // The next change of queue 2 is written after the write in progress completed.
            queue2.Enqueue(new DataValue(new Variant(22)), ServiceResult.Good);
            third.Release();
            await Task.Delay(100).ConfigureAwait(false);
            second.Release();
            await factory.FlushAsync().ConfigureAwait(false);

            Assert.That(kv.MaxConcurrentWritesOfAKey, Is.EqualTo(1), "the writes of a queue never overlap");
            Assert.That(kv.StartedWrites(DataChangeKey(2)), Is.GreaterThanOrEqualTo(2));
            IDataChangeMonitoredItemQueue? stored = await factory.RestoreDataChangeQueueAsync(2)
                .ConfigureAwait(false);
            ((IRestorableMonitoredItemQueue)stored!).AbortRestore();
            Assert.That(stored.ItemsInQueue, Is.EqualTo(2), "the last change of queue 2 is stored last");
            Assert.That(stored.Dequeue(out DataValue oldest, out _), Is.True);
            Assert.That(stored.Dequeue(out DataValue newest, out _), Is.True);
            Assert.That(oldest.WrappedValue, Is.EqualTo(new Variant(2)));
            Assert.That(newest.WrappedValue, Is.EqualTo(new Variant(22)));
        }

        private static IDataChangeMonitoredItemQueue CreateMirroredQueue(
            SharedKeyValueMonitoredItemQueueFactory factory,
            uint monitoredItemId)
        {
            IDataChangeMonitoredItemQueue queue = factory.CreateDataChangeQueue(false, monitoredItemId);
            queue.ResetQueue(5, false);
            queue.Enqueue(new DataValue(new Variant((int)monitoredItemId)), ServiceResult.Good);
            return queue;
        }

        private static async Task<int> CountStoredDataChangeAsync(
            SharedKeyValueMonitoredItemQueueFactory factory,
            uint monitoredItemId)
        {
            IDataChangeMonitoredItemQueue? stored = await factory.RestoreDataChangeQueueAsync(monitoredItemId)
                .ConfigureAwait(false);
            if (stored == null)
            {
                return 0;
            }
            ((IRestorableMonitoredItemQueue)stored).AbortRestore();
            return stored.ItemsInQueue;
        }

        [Test]
        public async Task RemovalWaitsForBothKeysWhenOneThrowsSynchronouslyAsync()
        {
            using var inner = new InMemorySharedKeyValueStore();
            var kv = new GatedSharedKeyValueStore(inner);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, CreateContext(), telemetry: telemetry);
            IDataChangeMonitoredItemQueue queue = factory.CreateDataChangeQueue(false, 5);
            queue.ResetQueue(2, false);
            queue.Enqueue(new DataValue(new Variant(1)), ServiceResult.Good);
            await factory.FlushAsync().ConfigureAwait(false);

            // The removal of the data-change key is held; the removal of the event key throws at once.
            Gate dataRemoval = kv.Hold(DataChangeKey(5));
            kv.ThrowOnNextDelete(EventKey(5));
            queue.Dispose();
            Task flush = factory.FlushAsync().AsTask();
            Task finished;
            try
            {
                await dataRemoval.WaitEnteredAsync().ConfigureAwait(false);
                finished = await Task.WhenAny(flush, Task.Delay(500)).ConfigureAwait(false);
            }
            finally
            {
                dataRemoval.Release();
            }
            await flush.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            Assert.That(finished, Is.Not.SameAs(flush), "the batch waits for the removal still in progress");
        }

        private static string DataChangeKey(uint monitoredItemId)
        {
            return "monitored-item-queue/data/" + monitoredItemId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string EventKey(uint monitoredItemId)
        {
            return "monitored-item-queue/event/" + monitoredItemId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static async Task<IRestorableMonitoredItemQueue> StoreAndRestoreAsync(
            SharedKeyValueMonitoredItemQueueFactory factory,
            bool eventQueue)
        {
            if (eventQueue)
            {
                IEventMonitoredItemQueue queue = factory.CreateEventQueue(false, 9);
                queue.SetQueueSize(5, false);
                queue.Enqueue(new EventFieldList { ClientHandle = 1 });
                await factory.FlushAsync().ConfigureAwait(false);
                return (IRestorableMonitoredItemQueue)(await factory.RestoreEventQueueAsync(9)
                    .ConfigureAwait(false))!;
            }

            IDataChangeMonitoredItemQueue dataChangeQueue = factory.CreateDataChangeQueue(false, 9);
            dataChangeQueue.ResetQueue(5, false);
            dataChangeQueue.Enqueue(new DataValue(new Variant(1)), ServiceResult.Good);
            await factory.FlushAsync().ConfigureAwait(false);
            return (IRestorableMonitoredItemQueue)(await factory.RestoreDataChangeQueueAsync(9)
                .ConfigureAwait(false))!;
        }

        private static void Enqueue(IRestorableMonitoredItemQueue queue, int value)
        {
            if (queue is IEventMonitoredItemQueue eventQueue)
            {
                eventQueue.Enqueue(new EventFieldList { ClientHandle = (uint)value });
                return;
            }
            ((IDataChangeMonitoredItemQueue)queue).Enqueue(new DataValue(new Variant(value)), ServiceResult.Good);
        }

        private static async Task<int> CountStoredAsync(
            SharedKeyValueMonitoredItemQueueFactory factory,
            bool eventQueue)
        {
            // A queue restored for reading only must not write back, so it is aborted.
            if (eventQueue)
            {
                IEventMonitoredItemQueue? stored = await factory.RestoreEventQueueAsync(9).ConfigureAwait(false);
                ((IRestorableMonitoredItemQueue)stored!).AbortRestore();
                return stored.ItemsInQueue;
            }
            IDataChangeMonitoredItemQueue? storedValues = await factory.RestoreDataChangeQueueAsync(9)
                .ConfigureAwait(false);
            ((IRestorableMonitoredItemQueue)storedValues!).AbortRestore();
            return storedValues.ItemsInQueue;
        }

        [Test]
        public async Task CleanupRemovesStaleQueuesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            await using var factory = new SharedKeyValueMonitoredItemQueueFactory(kv, context, telemetry: telemetry);
            foreach (uint id in new uint[] { 1, 2 })
            {
                IDataChangeMonitoredItemQueue queue = factory.CreateDataChangeQueue(false, id);
                queue.ResetQueue(2, false);
                queue.Enqueue(new DataValue(new Variant((int)id)), ServiceResult.Good);
            }
            await factory.FlushAsync().ConfigureAwait(false);

            await factory.CleanupAsync([1]).ConfigureAwait(false);

            Assert.That(await factory.RestoreDataChangeQueueAsync(1).ConfigureAwait(false), Is.Not.Null);
            Assert.That(await factory.RestoreDataChangeQueueAsync(2).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task SubscriptionStoreDelegatesQueueRestoreToFactoryAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            await using (var factory = new SharedKeyValueMonitoredItemQueueFactory(kv, context, telemetry: telemetry))
            {
                IDataChangeMonitoredItemQueue queue = factory.CreateDataChangeQueue(false, 21);
                queue.ResetQueue(3, false);
                queue.Enqueue(new DataValue(new Variant(123)), ServiceResult.Good);
                await factory.FlushAsync().ConfigureAwait(false);
            }

            await using var restoreFactory = new SharedKeyValueMonitoredItemQueueFactory(
                kv, context, telemetry: telemetry);
            await using var store = new SharedKeyValueSubscriptionStore(
                kv, context, queueFactory: restoreFactory);

            IDataChangeMonitoredItemQueue restored = (await store
                .RestoreDataChangeMonitoredItemQueueAsync(21)
                .ConfigureAwait(false))!;

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.ItemsInQueue, Is.EqualTo(1));
        }

        [Test]
        public async Task SubscriptionStoreWithoutFactoryReturnsNullQueuesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using var store = new SharedKeyValueSubscriptionStore(kv, CreateContext());

            Assert.That(await store.RestoreDataChangeMonitoredItemQueueAsync(1).ConfigureAwait(false), Is.Null);
            Assert.That(await store.RestoreEventMonitoredItemQueueAsync(1).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task DisposeAsyncCancelsBlockedQueueMirrorWritesAsync()
        {
            await using var kv = new HangingWriteSharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var factory = new SharedKeyValueMonitoredItemQueueFactory(kv, context, telemetry: telemetry);
            IDataChangeMonitoredItemQueue queue = factory.CreateDataChangeQueue(false, 55);
            queue.ResetQueue(2, false);
            queue.Enqueue(new DataValue(new Variant(1)), ServiceResult.Good);

            await kv.WaitForWriteAsync().ConfigureAwait(false);
            Task disposeTask = factory.DisposeAsync().AsTask();
            Task completed = await Task.WhenAny(disposeTask, Task.Delay(2000)).ConfigureAwait(false);

            Assert.That(completed, Is.SameAs(disposeTask));
            await disposeTask.ConfigureAwait(false);
        }

        private static ServiceMessageContext CreateContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.CreateEmpty(telemetry);
            context.NamespaceUris.GetIndexOrAppend("urn:test:queues");
            return context;
        }

        /// <summary>
        /// Holds the next write of a key until the test releases or fails it.
        /// </summary>
        private sealed class Gate
        {
            public async Task WaitEnteredAsync()
            {
                Task completed = await Task.WhenAny(m_entered.Task, Task.Delay(TimeSpan.FromSeconds(30)))
                    .ConfigureAwait(false);
                Assert.That(completed, Is.SameAs(m_entered.Task), "the write reached the store");
            }

            public void Release()
            {
                m_released.TrySetResult(true);
            }

            public void Fail(Exception exception)
            {
                m_released.TrySetException(exception);
            }

            public async Task EnterAsync(CancellationToken ct)
            {
                m_entered.TrySetResult(true);

                // A held write ends when the factory is disposed, so a failed test does not hang.
                using CancellationTokenRegistration registration = ct.Register(() => m_released.TrySetCanceled());
                await m_released.Task.ConfigureAwait(false);
            }

            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_released =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// A store whose writes of a held key wait for the test.
        /// </summary>
        private sealed class GatedSharedKeyValueStore : ISharedKeyValueStore
        {
            public GatedSharedKeyValueStore(ISharedKeyValueStore inner)
            {
                m_inner = inner;
            }

            public Gate Hold(string key)
            {
                var gate = new Gate();
                lock (m_gates)
                {
                    m_gates[key] = gate;
                }
                return gate;
            }

            /// <summary>
            /// The most writes of one key that were in progress at once.
            /// </summary>
            public int MaxConcurrentWritesOfAKey
            {
                get
                {
                    lock (m_gates)
                    {
                        return m_maxConcurrentWritesOfAKey;
                    }
                }
            }

            /// <summary>
            /// The number of writes and removals of the key that started.
            /// </summary>
            public int StartedWrites(string key)
            {
                lock (m_gates)
                {
                    return m_started.TryGetValue(key, out int count) ? count : 0;
                }
            }

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
            {
                return m_inner.TryGetAsync(key, ct);
            }

            public async ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                try
                {
                    await PassAsync(key, ct).ConfigureAwait(false);
                    await m_inner.SetAsync(key, value, ct).ConfigureAwait(false);
                }
                finally
                {
                    Complete(key);
                }
            }

            public ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                return m_inner.CompareAndSwapAsync(key, expected, value, ct);
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                lock (m_gates)
                {
                    if (m_throwSynchronously.Remove(key))
                    {
                        throw new IOException("store rejected the removal");
                    }
                }
                return DeleteCoreAsync(key, ct);
            }

            /// <summary>
            /// Makes the next removal of the key throw before it returns a task.
            /// </summary>
            public void ThrowOnNextDelete(string key)
            {
                lock (m_gates)
                {
                    m_throwSynchronously.Add(key);
                }
            }

            private async ValueTask<bool> DeleteCoreAsync(string key, CancellationToken ct)
            {
                try
                {
                    await PassAsync(key, ct).ConfigureAwait(false);
                    return await m_inner.DeleteAsync(key, ct).ConfigureAwait(false);
                }
                finally
                {
                    Complete(key);
                }
            }

            public IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                return m_inner.ScanAsync(keyPrefix, ct);
            }

            public IAsyncEnumerable<KeyValueChange> WatchAsync(string keyPrefix, CancellationToken ct = default)
            {
                return m_inner.WatchAsync(keyPrefix, ct);
            }

            private Task PassAsync(string key, CancellationToken ct)
            {
                Gate? gate;
                lock (m_gates)
                {
                    m_started[key] = (m_started.TryGetValue(key, out int started) ? started : 0) + 1;
                    int inFlight = (m_inFlight.TryGetValue(key, out int count) ? count : 0) + 1;
                    m_inFlight[key] = inFlight;
                    m_maxConcurrentWritesOfAKey = Math.Max(m_maxConcurrentWritesOfAKey, inFlight);
                    if (m_gates.TryGetValue(key, out gate))
                    {
                        m_gates.Remove(key);
                    }
                }
                return gate?.EnterAsync(ct) ?? Task.CompletedTask;
            }

            private void Complete(string key)
            {
                lock (m_gates)
                {
                    m_inFlight[key]--;
                }
            }

            private readonly ISharedKeyValueStore m_inner;
            private readonly Dictionary<string, Gate> m_gates = [];
            private readonly Dictionary<string, int> m_started = [];
            private readonly Dictionary<string, int> m_inFlight = [];
            private readonly HashSet<string> m_throwSynchronously = [];
            private int m_maxConcurrentWritesOfAKey;
        }

        private sealed class HangingWriteSharedKeyValueStore : ISharedKeyValueStore, IAsyncDisposable
        {
            public Task<bool> WaitForWriteAsync()
            {
                return m_writeStarted.Task;
            }

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, System.Threading.CancellationToken ct = default)
            {
                return new ValueTask<(bool Found, ByteString Value)>((false, default));
            }

            public ValueTask SetAsync(string key, ByteString value, System.Threading.CancellationToken ct = default)
            {
                m_writeStarted.TrySetResult(true);
                return new ValueTask(Task.Delay(System.Threading.Timeout.Infinite, ct));
            }

            public ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                System.Threading.CancellationToken ct = default)
            {
                return new ValueTask<bool>(true);
            }

            public ValueTask<bool> DeleteAsync(string key, System.Threading.CancellationToken ct = default)
            {
                m_writeStarted.TrySetResult(true);
                return WaitForDeleteCancellationAsync(ct);
            }

            public IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                System.Threading.CancellationToken ct = default)
            {
                return TestAsyncEnumerable.Empty<KeyValuePair<string, ByteString>>();
            }

            public IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix,
                System.Threading.CancellationToken ct = default)
            {
                return TestAsyncEnumerable.Empty<KeyValueChange>();
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }

            private static async ValueTask<bool> WaitForDeleteCancellationAsync(System.Threading.CancellationToken ct)
            {
                await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
                return false;
            }

            private readonly TaskCompletionSource<bool> m_writeStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
