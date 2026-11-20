/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#pragma warning disable CA2000, CA2007

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="RegistryBackedDistributedValueCache"/>: it resolves the node-state store from
    /// the registry accessor on every call and fails fast before the server has started.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class RegistryBackedDistributedValueCacheTests
    {
        private const ushort NamespaceIndex = 1;
        private IServiceMessageContext m_messageContext = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:registry-cache");
            m_messageContext = messageContext;
        }

        [Test]
        public void RejectsNullRegistryAccessor()
        {
            Assert.That(
                () => new RegistryBackedDistributedValueCache(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task ResolvesStoreAndRoundTripsValueAsync()
        {
            var time = new FakeTimeProvider();
            using var kv = new InMemorySharedKeyValueStore();
            var store = new InMemoryNodeStateStore(kv, m_messageContext);
            var registry = new Mock<INodeStateStoreRegistry>();
            registry.Setup(r => r.Resolve(It.IsAny<NodeId>())).Returns(store);

            var cache = new RegistryBackedDistributedValueCache(() => registry.Object, time);
            var nodeId = new NodeId("v", NamespaceIndex);

            await cache.CacheAsync(nodeId, new DataValue(new Variant(2.5), StatusCodes.Good, time.GetUtcNow())).ConfigureAwait(false);
            (bool fresh, DataValue value) = await cache.TryGetAsync(nodeId, TimeSpan.FromMinutes(1)).ConfigureAwait(false);

            Assert.That(fresh, Is.True);
            Assert.That(value.WrappedValue, Is.EqualTo(new Variant(2.5)));
        }

        [Test]
        public void ThrowsWhenRegistryNotAvailable()
        {
            var cache = new RegistryBackedDistributedValueCache(() => null);

            Assert.That(
                async () => await cache.TryGetAsync(new NodeId("v", NamespaceIndex), TimeSpan.FromMinutes(1)).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("not available"));
        }

        [Test]
        public void ThrowsWhenStoreNotRegisteredForNode()
        {
            var registry = new Mock<INodeStateStoreRegistry>();
            registry.Setup(r => r.Resolve(It.IsAny<NodeId>())).Returns((INodeStateStore?)null);
            var cache = new RegistryBackedDistributedValueCache(() => registry.Object);

            Assert.That(
                async () => await cache.CacheAsync(
                    new NodeId("v", NamespaceIndex), new DataValue(new Variant(1.0))).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("No distributed node-state store"));
        }
    }
}
