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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Server.Redundancy;
using Opc.Ua.Tests;
using Opc.Ua.Redundancy;

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
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(telemetry);
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
            await cache.CacheAsync(nodeId, new DataValue(new Variant(2.0), StatusCodes.Good, time.GetUtcNow()));

            int liveCalls = 0;
            DataValue result = await DistributedValueParticipation.ReadThroughAsync(
                cache, nodeId, TimeSpan.FromMinutes(1), LiveRead);

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
                cache, nodeId, TimeSpan.FromMinutes(1), LiveRead);

            Assert.That(liveCalls, Is.EqualTo(1));
            Assert.That(result.WrappedValue, Is.EqualTo(new Variant(7.0)));

            (bool fresh, DataValue cached) = await cache.TryGetAsync(nodeId, TimeSpan.FromMinutes(1));
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
                m_systemContext, variable, default, new Variant(8.0), default);
            Assert.That(ServiceResult.IsGood(writeResult.Result), Is.True);

            AttributeReadResult readResult = await variable.OnReadValueAsync!(
                m_systemContext, variable, default, new QualifiedName(), default);

            Assert.That(readResult.Value, Is.EqualTo(new Variant(8.0)), "the written value is served from the cache");
            Assert.That(liveCalls, Is.Zero, "a fresh written value must be served without a live read");

            ValueTask<DataValue> LiveRead(CancellationToken ct)
            {
                liveCalls++;
                return new ValueTask<DataValue>(new DataValue(new Variant(123.0), StatusCodes.Good, DateTimeUtc.Now));
            }
        }
    }
}
