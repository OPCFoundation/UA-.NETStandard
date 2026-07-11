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

// CA2000: test code; disposables are short-lived or ownership-transferred.
#pragma warning disable CA2000

using System;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

#nullable enable

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Gap-coverage tests for <see cref="HistorianProviderRegistry"/> targeting
    /// branches not exercised by <see cref="HistorianProviderRegistryTests"/>.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianRegistryGapsTests
    {
        [Test]
        public void ConstructorWithNullNamespaceTableThrows()
        {
            Assert.That(
                () => new HistorianProviderRegistry(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void RegisterForNodeWithNullNodeIdThrows()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var provider = new InMemoryHistorianProvider();

            Assert.That(
                () => registry.RegisterForNode(NodeId.Null, provider),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void RegisterForNodeWithNullProviderThrows()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());
            var nodeId = new NodeId("n", 1);

            Assert.That(
                () => registry.RegisterForNode(nodeId, null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void RegisterForNamespaceWithNullUriThrows()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var provider = new InMemoryHistorianProvider();

            Assert.That(
                () => registry.RegisterForNamespace(null!, provider),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void RegisterForNamespaceWithEmptyUriThrows()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var provider = new InMemoryHistorianProvider();

            Assert.That(
                () => registry.RegisterForNamespace(string.Empty, provider),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void RegisterForNamespaceWithNullProviderThrows()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());

            Assert.That(
                () => registry.RegisterForNamespace("urn:test:ns", null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void RegisterDefaultWithNullProviderThrows()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());

            Assert.That(
                () => registry.RegisterDefault(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ClearDefaultRemovesDefaultFromProvidersSet()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var p = new InMemoryHistorianProvider();

            registry.RegisterDefault(p);
            Assert.That(registry.Providers, Does.Contain(p));

            registry.ClearDefault();

            Assert.That(registry.Providers, Does.Not.Contain(p));
            Assert.That(registry.Resolve(new NodeId("any", 1)), Is.Null);
        }

        [Test]
        public void ClearDefaultWhenNoDefaultIsNoOp()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());

            Assert.DoesNotThrow(registry.ClearDefault);
            Assert.That(registry.Providers, Is.Empty);
        }

        [Test]
        public void ClearDefaultKeepsProviderIfStillInNodes()
        {
            var nsTable = new NamespaceTable();
            var registry = new HistorianProviderRegistry(nsTable);
            using var shared = new InMemoryHistorianProvider();
            var nodeId = new NodeId("shared", 1);

            registry.RegisterForNode(nodeId, shared);
            registry.RegisterDefault(shared);

            // After clearing default the provider is still registered for nodeId,
            // so it must remain in Providers.
            registry.ClearDefault();

            Assert.That(registry.Providers, Does.Contain(shared),
                "Provider still used by node binding must stay in Providers.");
        }

        [Test]
        public void UnregisterForNodeWithNullNodeIdReturnsFalse()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());

            bool result = registry.UnregisterForNode(NodeId.Null);

            Assert.That(result, Is.False);
        }

        [Test]
        public void UnregisterForNodeThatDoesNotExistReturnsFalse()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());
            var nodeId = new NodeId("never-registered", 1);

            bool result = registry.UnregisterForNode(nodeId);

            Assert.That(result, Is.False);
        }

        [Test]
        public void UnregisterForNodeRegisteredNodeRemovesFromProvidersWhenNotUsedElsewhere()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var p = new InMemoryHistorianProvider();
            var nodeId = new NodeId("torem", 1);
            registry.RegisterForNode(nodeId, p);

            bool removed = registry.UnregisterForNode(nodeId);

            Assert.That(removed, Is.True);
            Assert.That(registry.Providers, Does.Not.Contain(p));
            Assert.That(registry.Resolve(nodeId), Is.Null);
        }

        [Test]
        public void UnregisterForNodeKeepsProviderWhenAlsoUsedAsDefault()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var shared = new InMemoryHistorianProvider();
            var nodeId = new NodeId("shared2", 1);
            registry.RegisterForNode(nodeId, shared);
            registry.RegisterDefault(shared);

            bool removed = registry.UnregisterForNode(nodeId);

            Assert.That(removed, Is.True);
            Assert.That(registry.Providers, Does.Contain(shared),
                "Provider still used as default must stay in Providers.");
        }

        [Test]
        public void UnregisterForNamespaceWithNullReturnsFalse()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());

            bool result = registry.UnregisterForNamespace(null!);

            Assert.That(result, Is.False);
        }

        [Test]
        public void UnregisterForNamespaceWithEmptyReturnsFalse()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());

            bool result = registry.UnregisterForNamespace(string.Empty);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ResolveByNamespaceUsesNamespaceTableIndex()
        {
            var nsTable = new NamespaceTable();
            const string nsUri = "urn:test:registry-gaps";
            ushort nsIdx = (ushort)nsTable.Append(nsUri);

            var registry = new HistorianProviderRegistry(nsTable);
            using var p = new InMemoryHistorianProvider();
            registry.RegisterForNamespace(nsUri, p);

            IHistorianProvider? resolved = registry.Resolve(new NodeId("any", nsIdx));

            Assert.That(resolved, Is.SameAs(p));
        }

        [Test]
        public void ResolveReturnsNullWhenNamespaceHasNoMatchAndNoDefault()
        {
            var nsTable = new NamespaceTable();
            nsTable.Append("urn:test:registered");

            var registry = new HistorianProviderRegistry(nsTable);
            using var p = new InMemoryHistorianProvider();
            registry.RegisterForNamespace("urn:test:registered", p);

            // Node in a namespace that is NOT registered.
            var nodeId = new NodeId("n", 0);
            Assert.That(registry.Resolve(nodeId), Is.Null);
        }

        [Test]
        public void DisposeDisposesRegisteredIDisposableProviders()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());
            var disposableProvider = new DisposableProvider();
            registry.RegisterDefault(disposableProvider);

            registry.Dispose();

            Assert.That(disposableProvider.Disposed, Is.True);
        }

        [Test]
        public void DisposeSwallowsExceptionsFromProviders()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());
            var throwing = new ThrowingDisposableProvider();
            registry.RegisterDefault(throwing);

            Assert.DoesNotThrow(registry.Dispose);
        }

        [Test]
        public void DisposeNonDisposableProviderDoesNotThrow()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());
            using var nonDisposable = new InMemoryHistorianProvider();
            // InMemoryHistorianProvider IS IDisposable — use a provider that is NOT.
            var ndp = new NonDisposableProvider();
            registry.RegisterDefault(ndp);

            Assert.DoesNotThrow(registry.Dispose);
        }

        // -----------------------------------------------------------------
        //  Helpers
        // -----------------------------------------------------------------

        private sealed class DisposableProvider : HistorianProviderBase, IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class ThrowingDisposableProvider : HistorianProviderBase, IDisposable
        {
            public void Dispose()
            {
                throw new InvalidOperationException("Intentional dispose failure.");
            }
        }

        private sealed class NonDisposableProvider : HistorianProviderBase
        {
            // Does NOT implement IDisposable.
        }
    }
}
