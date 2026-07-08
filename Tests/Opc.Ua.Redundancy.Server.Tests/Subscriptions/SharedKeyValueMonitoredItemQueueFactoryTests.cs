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

using System.Threading.Tasks;
using NUnit.Framework;
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
            IDataChangeMonitoredItemQueue restored = await restoreFactory
                .RestoreDataChangeQueueAsync(42)
                .ConfigureAwait(false);

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
            IDataChangeMonitoredItemQueue restored = await restoreFactory
                .RestoreDataChangeQueueAsync(7)
                .ConfigureAwait(false);

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.Dequeue(out _, out ServiceResult restoredError), Is.True);
            Assert.That(restoredError.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNoData));
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
            IEventMonitoredItemQueue restored = await restoreFactory
                .RestoreEventQueueAsync(11)
                .ConfigureAwait(false);

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
            IDataChangeMonitoredItemQueue restored = await restoreFactory
                .RestoreDataChangeQueueAsync(3)
                .ConfigureAwait(false);

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.ItemsInQueue, Is.EqualTo(2));
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

            IDataChangeMonitoredItemQueue restored = await store
                .RestoreDataChangeMonitoredItemQueueAsync(21)
                .ConfigureAwait(false);

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

        private static ServiceMessageContext CreateContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.CreateEmpty(telemetry);
            context.NamespaceUris.GetIndexOrAppend("urn:test:queues");
            return context;
        }
    }
}
