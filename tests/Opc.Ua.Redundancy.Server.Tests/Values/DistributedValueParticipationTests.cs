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

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="DistributedValueParticipation"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class DistributedValueParticipationTests
    {
        private const ushort NamespaceIndex = 1;
        private IServiceMessageContext m_messageContext = null!;
        private SystemContext m_systemContext = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:participation");
            m_messageContext = messageContext;
            m_systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public async Task ReadThroughServesFreshCacheWithoutLiveReadAsync()
        {
            var time = new FakeTimeProvider();
            using var kv = new InMemorySharedKeyValueStore();
            var cache = new DistributedValueCache(new InMemoryNodeStateStore(kv, m_messageContext), time);
            var nodeId = new NodeId("v", NamespaceIndex);
            await cache.CacheAsync(nodeId, new DataValue(new Variant(2.0), StatusCodes.Good, time.GetUtcNow())).ConfigureAwait(false);

            int liveCalls = 0;
            DataValue result = await DistributedValueParticipation.ReadThroughAsync(
                cache, nodeId, TimeSpan.FromMinutes(1), LiveRead).ConfigureAwait(false);

            Assert.That(liveCalls, Is.Zero, "fresh cached value must be served without a live read");
            Assert.That(result.WrappedValue, Is.EqualTo(new Variant(2.0)));

            ValueTask<DataValue> LiveRead(CancellationToken ct)
            {
                liveCalls++;
                return new ValueTask<DataValue>(new DataValue(new Variant(99.0), StatusCodes.Good, time.GetUtcNow()));
            }
        }

        [Test]
        public async Task ReadThroughReadsLiveWhenStaleAndCachesAsync()
        {
            var time = new FakeTimeProvider();
            using var kv = new InMemorySharedKeyValueStore();
            var cache = new DistributedValueCache(new InMemoryNodeStateStore(kv, m_messageContext), time);
            var nodeId = new NodeId("v", NamespaceIndex);

            int liveCalls = 0;
            DataValue result = await DistributedValueParticipation.ReadThroughAsync(
                cache, nodeId, TimeSpan.FromMinutes(1), LiveRead).ConfigureAwait(false);

            Assert.That(liveCalls, Is.EqualTo(1));
            Assert.That(result.WrappedValue, Is.EqualTo(new Variant(7.0)));

            (bool fresh, DataValue cached) = await cache.TryGetAsync(nodeId, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
            Assert.That(fresh, Is.True, "the live value should now be cached and fresh");
            Assert.That(cached.WrappedValue, Is.EqualTo(new Variant(7.0)));

            ValueTask<DataValue> LiveRead(CancellationToken ct)
            {
                liveCalls++;
                return new ValueTask<DataValue>(new DataValue(new Variant(7.0), StatusCodes.Good, time.GetUtcNow()));
            }
        }

        [Test]
        public async Task EnableParticipationWiresWriteThroughAndCachedReadAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var cache = new DistributedValueCache(new InMemoryNodeStateStore(kv, m_messageContext));
            var nodeId = new NodeId("wired", NamespaceIndex);
            var variable = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("Wired", NamespaceIndex),
                DisplayName = new LocalizedText("Wired"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant(0.0)
            };

            int liveCalls = 0;
            variable.EnableDistributedValueParticipation(cache, TimeSpan.FromMinutes(1), LiveRead);

            AttributeWriteResult writeResult = await variable.OnWriteValueAsync!(
                m_systemContext, variable, default, new Variant(8.0), default).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(writeResult.Result), Is.True);

            AttributeReadResult readResult = await variable.OnReadValueAsync!(
                m_systemContext, variable, default, new QualifiedName(), default).ConfigureAwait(false);

            Assert.That(readResult.Value, Is.EqualTo(new Variant(8.0)), "the written value is served from the cache");
            Assert.That(liveCalls, Is.Zero, "a fresh written value must be served without a live read");

            ValueTask<DataValue> LiveRead(CancellationToken ct)
            {
                liveCalls++;
                return new ValueTask<DataValue>(new DataValue(new Variant(123.0), StatusCodes.Good, DateTimeUtc.Now));
            }
        }

        [Test]
        public async Task CachedValuePropagatesAcrossReplicasSharingOneStoreAsync()
        {
            // Two replicas backed by the SAME shared key/value store: what the
            // active replica caches, a promoted replica reads back. This is the
            // mechanism behind "the Counter continues across failover" - the new
            // leader seeds from the last value written by the former leader.
            using var kv = new InMemorySharedKeyValueStore();
            var leader = new DistributedValueCache(new InMemoryNodeStateStore(kv, m_messageContext));
            var promoted = new DistributedValueCache(new InMemoryNodeStateStore(kv, m_messageContext));
            var nodeId = new NodeId("counter", NamespaceIndex);

            await leader
                .CacheAsync(nodeId, new DataValue(new Variant(41), StatusCodes.Good, DateTimeUtc.Now))
                .ConfigureAwait(false);
            await leader
                .CacheAsync(nodeId, new DataValue(new Variant(42), StatusCodes.Good, DateTimeUtc.Now))
                .ConfigureAwait(false);

            (bool fresh, DataValue value) = await promoted
                .TryGetAsync(nodeId, TimeSpan.FromMinutes(1))
                .ConfigureAwait(false);

            Assert.That(fresh, Is.True, "the promoted replica must observe the shared value as fresh");
            Assert.That(value.WrappedValue.TryGetValue(out int counter), Is.True);
            Assert.That(counter, Is.EqualTo(42), "the last value written by the former leader is served");
        }

        [Test]
        public async Task ReadOnlyParticipationSkipsCacheWritesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var cache = new DistributedValueCache(store, isWriter: () => false);
            var nodeId = new NodeId("readonly", NamespaceIndex);
            var variable = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("ReadOnly", NamespaceIndex),
                DisplayName = new LocalizedText("ReadOnly"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant(0.0)
            };

            int liveCalls = 0;
            variable.EnableDistributedValueParticipation(cache, TimeSpan.FromMinutes(1), LiveRead);

            AttributeReadResult firstRead = await variable.OnReadValueAsync!(
                m_systemContext, variable, default, new QualifiedName(), default).ConfigureAwait(false);
            AttributeReadResult secondRead = await variable.OnReadValueAsync!(
                m_systemContext, variable, default, new QualifiedName(), default).ConfigureAwait(false);
            AttributeWriteResult writeResult = await variable.OnWriteValueAsync!(
                m_systemContext, variable, default, new Variant(8.0), default).ConfigureAwait(false);

            Assert.That(firstRead.Value, Is.EqualTo(new Variant(5.0)));
            Assert.That(secondRead.Value, Is.EqualTo(new Variant(5.0)));
            Assert.That(liveCalls, Is.EqualTo(2), "a read-only replica should not cache read-through refreshes");
            Assert.That(ServiceResult.IsGood(writeResult.Result), Is.True);

            (bool found, _) = await store.TryReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(found, Is.False, "a read-only replica should not persist write-through updates");

            ValueTask<DataValue> LiveRead(CancellationToken ct)
            {
                liveCalls++;
                return new ValueTask<DataValue>(new DataValue(new Variant(5.0), StatusCodes.Good, DateTimeUtc.Now));
            }
        }
    }
}
