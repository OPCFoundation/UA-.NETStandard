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

// CA2000: test code; disposables are ownership-transferred to test fixtures or are short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianProviderRegistryTests
    {
        [Test]
        public async Task ResolveByExactNodeBeatsNamespaceAndDefaultAsync()
        {
            await Task.Yield();
            var nsTable = new NamespaceTable();
            nsTable.Append("urn:test:ns1");

            var registry = new HistorianProviderRegistry(nsTable);

            using var defaultProvider = new InMemoryHistorianProvider();
            using var namespaceProvider = new InMemoryHistorianProvider();
            using var nodeProvider = new InMemoryHistorianProvider();

            registry.RegisterDefault(defaultProvider);
            registry.RegisterForNamespace("urn:test:ns1", namespaceProvider);

            var exactNode = new NodeId("ExactNode", 1);
            registry.RegisterForNode(exactNode, nodeProvider);

            Assert.That(registry.Resolve(exactNode), Is.SameAs(nodeProvider));
            Assert.That(registry.Resolve(new NodeId("AnotherInNs1", 1)), Is.SameAs(namespaceProvider));
            Assert.That(registry.Resolve(new NodeId("InNs0", 0)), Is.SameAs(defaultProvider));
        }

        [Test]
        public async Task UnregisterNamespaceLeavesOtherBindingsAsync()
        {
            await Task.Yield();
            var nsTable = new NamespaceTable();
            nsTable.Append("urn:test:ns1");

            var registry = new HistorianProviderRegistry(nsTable);

            using var defaultProvider = new InMemoryHistorianProvider();
            using var namespaceProvider = new InMemoryHistorianProvider();

            registry.RegisterDefault(defaultProvider);
            registry.RegisterForNamespace("urn:test:ns1", namespaceProvider);

            Assert.That(registry.UnregisterForNamespace("urn:test:ns1"), Is.True);
            Assert.That(registry.Resolve(new NodeId("InNs1", 1)), Is.SameAs(defaultProvider));
        }

        [Test]
        public async Task ProvidersReflectsUnionAsync()
        {
            await Task.Yield();
            var nsTable = new NamespaceTable();
            nsTable.Append("urn:test:ns1");
            nsTable.Append("urn:test:ns2");

            var registry = new HistorianProviderRegistry(nsTable);

            using var p1 = new InMemoryHistorianProvider();
            using var p2 = new InMemoryHistorianProvider();
            using var p3 = new InMemoryHistorianProvider();

            registry.RegisterDefault(p1);
            registry.RegisterForNamespace("urn:test:ns1", p2);
            registry.RegisterForNamespace("urn:test:ns2", p3);

            Assert.That(registry.Providers, Has.Count.EqualTo(3));
            Assert.That(registry.Providers, Does.Contain(p1));
            Assert.That(registry.Providers, Does.Contain(p2));
            Assert.That(registry.Providers, Does.Contain(p3));
        }

        [Test]
        public async Task ResolveReturnsNullForNullOrEmptyNodeIdAsync()
        {
            await Task.Yield();
            var nsTable = new NamespaceTable();
            var registry = new HistorianProviderRegistry(nsTable);

            using var provider = new InMemoryHistorianProvider();
            registry.RegisterDefault(provider);

            Assert.That(registry.Resolve(NodeId.Null), Is.Null);
        }
    }
}
