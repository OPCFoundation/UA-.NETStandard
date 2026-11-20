// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="DistributedValueCache"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class DistributedValueCacheTests
    {
        private const ushort NamespaceIndex = 1;
        private IServiceMessageContext m_messageContext = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:cache");
            m_messageContext = messageContext;
        }

        [Test]
        public async Task CachedValueIsFreshWithinMaxAgeAsync()
        {
            var time = new FakeTimeProvider();
            using var kv = new InMemorySharedKeyValueStore();
            var cache = new DistributedValueCache(new InMemoryNodeStateStore(kv, m_messageContext), time);
            var nodeId = new NodeId("v", NamespaceIndex);

            await cache.CacheAsync(nodeId, new DataValue(new Variant(1.5), StatusCodes.Good, time.GetUtcNow())).ConfigureAwait(false);
            (bool fresh, DataValue value) = await cache.TryGetAsync(nodeId, TimeSpan.FromMinutes(1)).ConfigureAwait(false);

            Assert.That(fresh, Is.True);
            Assert.That(value.WrappedValue, Is.EqualTo(new Variant(1.5)));
        }

        [Test]
        public async Task CachedValueIsStaleBeyondMaxAgeButStillReturnedAsync()
        {
            var time = new FakeTimeProvider();
            using var kv = new InMemorySharedKeyValueStore();
            var cache = new DistributedValueCache(new InMemoryNodeStateStore(kv, m_messageContext), time);
            var nodeId = new NodeId("v", NamespaceIndex);

            await cache.CacheAsync(nodeId, new DataValue(new Variant(1.5), StatusCodes.Good, time.GetUtcNow())).ConfigureAwait(false);
            time.Advance(TimeSpan.FromMinutes(2));
            (bool fresh, DataValue value) = await cache.TryGetAsync(nodeId, TimeSpan.FromMinutes(1)).ConfigureAwait(false);

            Assert.That(fresh, Is.False);
            Assert.That(value.WrappedValue, Is.EqualTo(new Variant(1.5)));
        }

        [Test]
        public async Task MissingValueReportsNotFreshAndNullAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var cache = new DistributedValueCache(new InMemoryNodeStateStore(kv, m_messageContext));

            (bool fresh, DataValue value) = await cache.TryGetAsync(
                new NodeId("missing", NamespaceIndex), TimeSpan.FromMinutes(1)).ConfigureAwait(false);

            Assert.That(fresh, Is.False);
            Assert.That(value.IsNull, Is.True);
        }
    }
}
