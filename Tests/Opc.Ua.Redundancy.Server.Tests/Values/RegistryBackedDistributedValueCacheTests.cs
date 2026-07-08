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

#pragma warning disable CA2000

#pragma warning disable CA2007

#nullable enable

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
