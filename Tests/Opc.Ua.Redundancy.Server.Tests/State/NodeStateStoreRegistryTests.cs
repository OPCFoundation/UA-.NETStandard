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

#nullable enable

using System;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Server.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NodeStateStoreRegistry"/>: the NodeId / namespace / default
    /// binding precedence, unregistration bookkeeping, and disposal fan-out.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeStateStoreRegistryTests
    {
        [Test]
        public void ConstructorThrowsOnNullNamespaceTable()
        {
            Assert.That(() => new NodeStateStoreRegistry(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void RegisterForNodeThrowsOnNullNodeId()
        {
            using var registry = new NodeStateStoreRegistry(new NamespaceTable());
            Assert.That(
                () => registry.RegisterForNode(NodeId.Null, Mock.Of<INodeStateStore>()),
                Throws.ArgumentException);
        }

        [Test]
        public void RegisterForNodeThrowsOnNullStore()
        {
            using var registry = new NodeStateStoreRegistry(new NamespaceTable());
            Assert.That(
                () => registry.RegisterForNode(new NodeId(1u), null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void RegisterForNamespaceThrowsOnEmptyUri()
        {
            using var registry = new NodeStateStoreRegistry(new NamespaceTable());
            Assert.That(
                () => registry.RegisterForNamespace(string.Empty, Mock.Of<INodeStateStore>()),
                Throws.ArgumentException);
        }

        [Test]
        public void RegisterForNamespaceThrowsOnNullStore()
        {
            using var registry = new NodeStateStoreRegistry(new NamespaceTable());
            Assert.That(
                () => registry.RegisterForNamespace("http://test/ns", null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void RegisterDefaultThrowsOnNullStore()
        {
            using var registry = new NodeStateStoreRegistry(new NamespaceTable());
            Assert.That(() => registry.RegisterDefault(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void ResolveReturnsNullForNullNodeId()
        {
            using var registry = new NodeStateStoreRegistry(new NamespaceTable());
            registry.RegisterDefault(Mock.Of<INodeStateStore>());
            Assert.That(registry.Resolve(NodeId.Null), Is.Null);
        }

        [Test]
        public void ResolveReturnsNodeScopedStore()
        {
            using var registry = new NodeStateStoreRegistry(new NamespaceTable());
            INodeStateStore store = Mock.Of<INodeStateStore>();
            var nodeId = new NodeId(42u);

            registry.RegisterForNode(nodeId, store);

            Assert.That(registry.Resolve(nodeId), Is.SameAs(store));
            Assert.That(registry.Resolve(new NodeId(99u)), Is.Null, "an unbound node has no store");
        }

        [Test]
        public void ResolveFallsBackToNamespaceScopedStore()
        {
            var namespaceTable = new NamespaceTable();
            int nsIndex = namespaceTable.Append("http://test/ns");
            using var registry = new NodeStateStoreRegistry(namespaceTable);
            INodeStateStore store = Mock.Of<INodeStateStore>();

            registry.RegisterForNamespace("http://test/ns", store);

            Assert.That(registry.Resolve(new NodeId(7u, (ushort)nsIndex)), Is.SameAs(store));
        }

        [Test]
        public void ResolveFallsBackToDefault()
        {
            using var registry = new NodeStateStoreRegistry(new NamespaceTable());
            INodeStateStore store = Mock.Of<INodeStateStore>();

            registry.RegisterDefault(store);

            Assert.That(registry.Resolve(new NodeId(123u)), Is.SameAs(store));
        }

        [Test]
        public void ResolvePrefersNodeThenNamespaceThenDefault()
        {
            var namespaceTable = new NamespaceTable();
            int nsIndex = namespaceTable.Append("http://test/ns");
            using var registry = new NodeStateStoreRegistry(namespaceTable);
            INodeStateStore nodeStore = Mock.Of<INodeStateStore>();
            INodeStateStore namespaceStore = Mock.Of<INodeStateStore>();
            INodeStateStore defaultStore = Mock.Of<INodeStateStore>();

            var boundNode = new NodeId(7u, (ushort)nsIndex);
            registry.RegisterForNode(boundNode, nodeStore);
            registry.RegisterForNamespace("http://test/ns", namespaceStore);
            registry.RegisterDefault(defaultStore);

            Assert.That(registry.Resolve(boundNode), Is.SameAs(nodeStore), "node binding wins");
            Assert.That(
                registry.Resolve(new NodeId(8u, (ushort)nsIndex)),
                Is.SameAs(namespaceStore),
                "namespace binding wins over default");
            Assert.That(registry.Resolve(new NodeId(9u)), Is.SameAs(defaultStore), "default is the last resort");
        }

        [Test]
        public void UnregisterForNodeRemovesBinding()
        {
            using var registry = new NodeStateStoreRegistry(new NamespaceTable());
            INodeStateStore store = Mock.Of<INodeStateStore>();
            var nodeId = new NodeId(42u);
            registry.RegisterForNode(nodeId, store);

            Assert.That(registry.UnregisterForNode(nodeId), Is.True);
            Assert.That(registry.Resolve(nodeId), Is.Null);
            Assert.That(registry.UnregisterForNode(nodeId), Is.False, "a second unregister is a no-op");
            Assert.That(registry.UnregisterForNode(NodeId.Null), Is.False);
            Assert.That(registry.Stores, Is.Empty, "the store is dropped when no binding references it");
        }

        [Test]
        public void UnregisterForNamespaceRemovesBinding()
        {
            var namespaceTable = new NamespaceTable();
            int nsIndex = namespaceTable.Append("http://test/ns");
            using var registry = new NodeStateStoreRegistry(namespaceTable);
            INodeStateStore store = Mock.Of<INodeStateStore>();
            registry.RegisterForNamespace("http://test/ns", store);

            Assert.That(registry.UnregisterForNamespace("http://test/ns"), Is.True);
            Assert.That(registry.Resolve(new NodeId(7u, (ushort)nsIndex)), Is.Null);
            Assert.That(registry.UnregisterForNamespace("http://test/ns"), Is.False);
            Assert.That(registry.UnregisterForNamespace(string.Empty), Is.False);
        }

        [Test]
        public void ClearDefaultRemovesDefault()
        {
            using var registry = new NodeStateStoreRegistry(new NamespaceTable());
            INodeStateStore store = Mock.Of<INodeStateStore>();
            registry.RegisterDefault(store);

            registry.ClearDefault();

            Assert.That(registry.Resolve(new NodeId(1u)), Is.Null);
            Assert.That(registry.Stores, Is.Empty);
            Assert.That(() => registry.ClearDefault(), Throws.Nothing, "clearing an empty default is a no-op");
        }

        [Test]
        public void StoresReflectsRegisteredStoresDeduplicated()
        {
            using var registry = new NodeStateStoreRegistry(new NamespaceTable());
            INodeStateStore shared = Mock.Of<INodeStateStore>();

            registry.RegisterForNode(new NodeId(1u), shared);
            registry.RegisterDefault(shared);

            Assert.That(registry.Stores, Has.Count.EqualTo(1), "the same store instance is tracked once");
            Assert.That(registry.Stores, Contains.Item(shared));
        }

        [Test]
        public void UnregisterKeepsStoreWhenStillReferencedElsewhere()
        {
            using var registry = new NodeStateStoreRegistry(new NamespaceTable());
            INodeStateStore shared = Mock.Of<INodeStateStore>();
            var nodeId = new NodeId(1u);

            registry.RegisterForNode(nodeId, shared);
            registry.RegisterDefault(shared);

            Assert.That(registry.UnregisterForNode(nodeId), Is.True);
            Assert.That(registry.Stores, Contains.Item(shared), "the store remains while the default still references it");
            Assert.That(registry.Resolve(new NodeId(2u)), Is.SameAs(shared));
        }

        [Test]
        public void UnregisterNamespaceKeepsStoreWhenReferencedByNode()
        {
            var namespaceTable = new NamespaceTable();
            int nsIndex = namespaceTable.Append("http://test/ns");
            using var registry = new NodeStateStoreRegistry(namespaceTable);
            INodeStateStore shared = Mock.Of<INodeStateStore>();
            var nodeId = new NodeId(1u);

            registry.RegisterForNode(nodeId, shared);
            registry.RegisterForNamespace("http://test/ns", shared);

            Assert.That(registry.UnregisterForNamespace("http://test/ns"), Is.True);
            Assert.That(
                registry.Stores,
                Contains.Item(shared),
                "the store remains while a node binding still references it");
            Assert.That(registry.Resolve(nodeId), Is.SameAs(shared));
            Assert.That(registry.Resolve(new NodeId(2u, (ushort)nsIndex)), Is.Null);
        }

        [Test]
        public void UnregisterNodeKeepsStoreWhenReferencedByNamespace()
        {
            var namespaceTable = new NamespaceTable();
            int nsIndex = namespaceTable.Append("http://test/ns");
            using var registry = new NodeStateStoreRegistry(namespaceTable);
            INodeStateStore shared = Mock.Of<INodeStateStore>();
            var nodeId = new NodeId(1u);

            registry.RegisterForNode(nodeId, shared);
            registry.RegisterForNamespace("http://test/ns", shared);

            Assert.That(registry.UnregisterForNode(nodeId), Is.True);
            Assert.That(
                registry.Stores,
                Contains.Item(shared),
                "the store remains while a namespace binding still references it");
            Assert.That(registry.Resolve(new NodeId(7u, (ushort)nsIndex)), Is.SameAs(shared));
        }

        [Test]
        public void UnregisterNodeKeepsStoreWhenReferencedByAnotherNode()
        {
            using var registry = new NodeStateStoreRegistry(new NamespaceTable());
            INodeStateStore shared = Mock.Of<INodeStateStore>();
            var firstNode = new NodeId(1u);
            var secondNode = new NodeId(2u);

            registry.RegisterForNode(firstNode, shared);
            registry.RegisterForNode(secondNode, shared);

            Assert.That(registry.UnregisterForNode(firstNode), Is.True);
            Assert.That(
                registry.Stores,
                Contains.Item(shared),
                "the store remains while another node binding still references it");
            Assert.That(registry.Resolve(secondNode), Is.SameAs(shared));
        }

        [Test]
        public void DisposeDisposesDisposableStores()
        {
            var registry = new NodeStateStoreRegistry(new NamespaceTable());
            var disposable = new Mock<INodeStateStore>();
            Mock<IDisposable> disposer = disposable.As<IDisposable>();
            INodeStateStore plain = Mock.Of<INodeStateStore>();

            registry.RegisterDefault(disposable.Object);
            registry.RegisterForNode(new NodeId(1u), plain);

            registry.Dispose();

            disposer.Verify(d => d.Dispose(), Times.Once);
            Assert.That(registry.Stores, Is.Empty, "disposal clears all bindings");
        }
    }
}
