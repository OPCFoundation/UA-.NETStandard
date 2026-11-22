// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CA2007

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable]
    public class PredefinedNodesAddressSpaceTests
    {
        [Test]
        public void NodesSkipsInstanceChildrenWhoseParentIsAlsoPredefined()
        {
            PredefinedNodeDictionary predefinedNodes = [];
            BaseObjectState parent = CreateObject("Parent");
            BaseObjectState child = CreateObject("Child", parent);
            predefinedNodes[parent.NodeId] = parent;
            predefinedNodes[child.NodeId] = child;

            PredefinedNodesAddressSpace addressSpace = CreateAddressSpace(predefinedNodes);

            NodeState[] nodes = [.. addressSpace.Nodes];

            Assert.That(nodes, Has.Length.EqualTo(1));
            Assert.That(nodes[0], Is.SameAs(parent));
        }

        [Test]
        public async Task AddOrUpdateNodeAsyncInvokesDelegateAndRaisesEventAsync()
        {
            NodeState? added = null;
            NodeState? observed = null;
            BaseObjectState node = CreateObject("Added");
            PredefinedNodeDictionary predefinedNodes = [];
            var addressSpace = new PredefinedNodesAddressSpace(
                CreateContext(),
                predefinedNodes,
                (n, _) =>
                {
                    added = n;
                    predefinedNodes[n.NodeId] = n;
                    return default;
                },
                (_, _) => new ValueTask<bool>(false));
            addressSpace.NodeAdded += n => observed = n;

            await addressSpace.AddOrUpdateNodeAsync(node).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(added, Is.SameAs(node));
                Assert.That(observed, Is.SameAs(node));
            });
        }

        [Test]
        public async Task AddOrUpdateNodeAsyncPublishesRegisteredActiveNodeAsync()
        {
            PredefinedNodeDictionary predefinedNodes = [];
            BaseObjectState input = CreateObject("Replaced");
            var active = new FolderState(null)
            {
                NodeId = input.NodeId,
                BrowseName = input.BrowseName
            };
            NodeState? observed = null;
            var addressSpace = new PredefinedNodesAddressSpace(
                CreateContext(),
                predefinedNodes,
                (_, _) =>
                {
                    predefinedNodes[active.NodeId] = active;
                    return default;
                },
                (_, _) => new ValueTask<bool>(false));
            addressSpace.NodeAdded += node => observed = node;

            await addressSpace.AddOrUpdateNodeAsync(input).ConfigureAwait(false);

            Assert.That(observed, Is.SameAs(active));
            Assert.That(addressSpace.TryGetNode(active.NodeId, out NodeState? registered), Is.True);
            Assert.That(observed, Is.SameAs(registered));
        }

        [Test]
        public async Task AddOrUpdateNodeAsyncRejectsUnregisteredResultAsync()
        {
            PredefinedNodesAddressSpace addressSpace = CreateAddressSpace([]);
            bool notified = false;
            addressSpace.NodeAdded += _ => notified = true;

            await Assert.ThatAsync(
                async () => await addressSpace.AddOrUpdateNodeAsync(CreateObject("Missing")).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("without registering")).ConfigureAwait(false);

            Assert.That(notified, Is.False);
        }

        [Test]
        public async Task AddOrUpdateRangeAsyncInvokesDelegateForEachNodeAsync()
        {
            int count = 0;
            var addressSpace = new PredefinedNodesAddressSpace(
                CreateContext(),
                [],
                (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    count++;
                    return default;
                },
                (_, _) => new ValueTask<bool>(false));

            await addressSpace.AddOrUpdateRangeAsync(
                    [CreateObject("One"), CreateObject("Two")],
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public async Task RemoveNodeAsyncRaisesEventOnlyWhenDelegateRemovesNodeAsync()
        {
            NodeId nodeId = new("Removed", 2);
            NodeId? observed = null;
            var addressSpace = new PredefinedNodesAddressSpace(
                CreateContext(),
                [],
                (_, _) => default,
                (id, _) => new ValueTask<bool>(id == nodeId));
            addressSpace.NodeRemoved += id => observed = id;

            bool removed = await addressSpace.RemoveNodeAsync(nodeId).ConfigureAwait(false);
            bool missing = await addressSpace.RemoveNodeAsync(new NodeId("Missing", 2)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(removed, Is.True);
                Assert.That(missing, Is.False);
                Assert.That(observed, Is.EqualTo(nodeId));
            });
        }

        [Test]
        public void TryGetNodeReturnsNodeFromDictionary()
        {
            PredefinedNodeDictionary predefinedNodes = [];
            BaseObjectState node = CreateObject("Lookup");
            predefinedNodes[node.NodeId] = node;
            PredefinedNodesAddressSpace addressSpace = CreateAddressSpace(predefinedNodes);

            bool found = addressSpace.TryGetNode(node.NodeId, out NodeState? result);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(result, Is.SameAs(node));
            });
        }

        [Test]
        public void ConstructorValidatesRequiredArguments()
        {
            ISystemContext context = CreateContext();
            PredefinedNodeDictionary predefinedNodes = [];
            static ValueTask addAsync(NodeState node, CancellationToken ct) => default;
            static ValueTask<bool> removeAsync(NodeId nodeId, CancellationToken ct) => new(false);

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() =>
                    new PredefinedNodesAddressSpace(null!, predefinedNodes, addAsync, removeAsync));
                Assert.Throws<ArgumentNullException>(() =>
                    new PredefinedNodesAddressSpace(context, null!, addAsync, removeAsync));
                Assert.Throws<ArgumentNullException>(() =>
                    new PredefinedNodesAddressSpace(context, predefinedNodes, null!, removeAsync));
                Assert.Throws<ArgumentNullException>(() =>
                    new PredefinedNodesAddressSpace(context, predefinedNodes, addAsync, null!));
            });
        }

        private static PredefinedNodesAddressSpace CreateAddressSpace(
            PredefinedNodeDictionary predefinedNodes)
        {
            return new PredefinedNodesAddressSpace(
                CreateContext(),
                predefinedNodes,
                (_, _) => default,
                (_, _) => new ValueTask<bool>(false));
        }

        private static SystemContext CreateContext()
        {
            return new SystemContext(NUnitTelemetryContext.Create());
        }

        private static BaseObjectState CreateObject(string name, NodeState? parent = null)
        {
            return new BaseObjectState(parent)
            {
                NodeId = new NodeId(name, 2),
                BrowseName = new QualifiedName(name, 2),
                DisplayName = new LocalizedText(name)
            };
        }
    }
}
