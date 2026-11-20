// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;

// CS0618: ServiceMessageContext.GlobalContext is obsolete, but obsolete APIs
// are permitted in test code (repo rule) and the context is never used for
// encoding by these resolution-precedence tests.
// TODO: switch to a per-fixture ServiceMessageContext.CreateEmpty() context.
#pragma warning disable CS0618

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="NodeStateStoreRegistry"/> resolution
    /// precedence (node, then namespace, then default).
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class NodeStateStoreRegistryTests
    {
        private const string NamespaceUri = "urn:test:registry";

        [Test]
        public void ResolveByNodeTakesPrecedenceOverNamespaceAndDefault()
        {
            var namespaceTable = new NamespaceTable();
            ushort index = namespaceTable.GetIndexOrAppend(NamespaceUri);
            using var registry = new NodeStateStoreRegistry(namespaceTable);

            using var kv = new InMemorySharedKeyValueStore();
            var nodeStore = new InMemoryNodeStateStore(kv, ServiceMessageContext.GlobalContext);
            var namespaceStore = new InMemoryNodeStateStore(kv, ServiceMessageContext.GlobalContext);
            var defaultStore = new InMemoryNodeStateStore(kv, ServiceMessageContext.GlobalContext);

            var nodeId = new NodeId(5, index);
            registry.RegisterForNode(nodeId, nodeStore);
            registry.RegisterForNamespace(NamespaceUri, namespaceStore);
            registry.RegisterDefault(defaultStore);

            Assert.That(registry.Resolve(nodeId), Is.SameAs(nodeStore));
        }

        [Test]
        public void ResolveByNamespaceWhenNoNodeBinding()
        {
            var namespaceTable = new NamespaceTable();
            ushort index = namespaceTable.GetIndexOrAppend(NamespaceUri);
            using var registry = new NodeStateStoreRegistry(namespaceTable);

            using var kv = new InMemorySharedKeyValueStore();
            var namespaceStore = new InMemoryNodeStateStore(kv, ServiceMessageContext.GlobalContext);
            var defaultStore = new InMemoryNodeStateStore(kv, ServiceMessageContext.GlobalContext);
            registry.RegisterForNamespace(NamespaceUri, namespaceStore);
            registry.RegisterDefault(defaultStore);

            Assert.That(registry.Resolve(new NodeId(99, index)), Is.SameAs(namespaceStore));
        }

        [Test]
        public void ResolveFallsBackToDefault()
        {
            var namespaceTable = new NamespaceTable();
            using var registry = new NodeStateStoreRegistry(namespaceTable);
            using var kv = new InMemorySharedKeyValueStore();
            var defaultStore = new InMemoryNodeStateStore(kv, ServiceMessageContext.GlobalContext);
            registry.RegisterDefault(defaultStore);

            Assert.That(registry.Resolve(new NodeId(1, 0)), Is.SameAs(defaultStore));
        }

        [Test]
        public void ResolveReturnsNullWhenEmpty()
        {
            using var registry = new NodeStateStoreRegistry(new NamespaceTable());

            Assert.That(registry.Resolve(new NodeId(1, 0)), Is.Null);
            Assert.That(registry.Resolve(NodeId.Null), Is.Null);
        }

        [Test]
        public void UnregisterForNodeRemovesBinding()
        {
            var namespaceTable = new NamespaceTable();
            using var registry = new NodeStateStoreRegistry(namespaceTable);
            using var kv = new InMemorySharedKeyValueStore();
            var nodeStore = new InMemoryNodeStateStore(kv, ServiceMessageContext.GlobalContext);
            var nodeId = new NodeId(7, 0);
            registry.RegisterForNode(nodeId, nodeStore);

            bool removed = registry.UnregisterForNode(nodeId);

            Assert.That(removed, Is.True);
            Assert.That(registry.Resolve(nodeId), Is.Null);
            Assert.That(registry.Stores, Has.Count.EqualTo(0));
        }

        [Test]
        public void StoresSnapshotContainsEveryRegisteredStore()
        {
            var namespaceTable = new NamespaceTable();
            using var registry = new NodeStateStoreRegistry(namespaceTable);
            using var kv = new InMemorySharedKeyValueStore();
            var first = new InMemoryNodeStateStore(kv, ServiceMessageContext.GlobalContext);
            var second = new InMemoryNodeStateStore(kv, ServiceMessageContext.GlobalContext);

            registry.RegisterForNode(new NodeId(1, 0), first);
            registry.RegisterDefault(second);

            Assert.That(registry.Stores, Is.EquivalentTo(new INodeStateStore[] { first, second }));
        }
    }
}
