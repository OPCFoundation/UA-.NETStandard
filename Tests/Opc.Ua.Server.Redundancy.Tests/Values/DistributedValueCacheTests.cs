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
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Server.Redundancy;
using Opc.Ua.Tests;
using Opc.Ua.Redundancy;

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
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(telemetry);
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

            await cache.CacheAsync(nodeId, new DataValue(new Variant(1.5), StatusCodes.Good, time.GetUtcNow()));
            (bool fresh, DataValue value) = await cache.TryGetAsync(nodeId, TimeSpan.FromMinutes(1));

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

            await cache.CacheAsync(nodeId, new DataValue(new Variant(1.5), StatusCodes.Good, time.GetUtcNow()));
            time.Advance(TimeSpan.FromMinutes(2));
            (bool fresh, DataValue value) = await cache.TryGetAsync(nodeId, TimeSpan.FromMinutes(1));

            Assert.That(fresh, Is.False);
            Assert.That(value.WrappedValue, Is.EqualTo(new Variant(1.5)));
        }

        [Test]
        public async Task MissingValueReportsNotFreshAndNullAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var cache = new DistributedValueCache(new InMemoryNodeStateStore(kv, m_messageContext));

            (bool fresh, DataValue value) = await cache.TryGetAsync(
                new NodeId("missing", NamespaceIndex), TimeSpan.FromMinutes(1));

            Assert.That(fresh, Is.False);
            Assert.That(value.IsNull, Is.True);
        }
    }
}
