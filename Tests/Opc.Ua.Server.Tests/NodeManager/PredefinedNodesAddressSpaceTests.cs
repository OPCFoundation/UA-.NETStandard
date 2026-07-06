/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
#pragma warning disable CA2007

using System;
using System.Linq;
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
            NodeIdDictionary<NodeState> predefinedNodes = [];
            BaseObjectState parent = CreateObject("Parent");
            BaseObjectState child = CreateObject("Child", parent);
            predefinedNodes[parent.NodeId] = parent;
            predefinedNodes[child.NodeId] = child;

            var addressSpace = CreateAddressSpace(predefinedNodes);

            NodeState[] nodes = addressSpace.Nodes.ToArray();

            Assert.That(nodes, Has.Length.EqualTo(1));
            Assert.That(nodes[0], Is.SameAs(parent));
        }

        [Test]
        public async Task AddOrUpdateNodeAsyncInvokesDelegateAndRaisesEventAsync()
        {
            NodeState? added = null;
            NodeState? observed = null;
            BaseObjectState node = CreateObject("Added");
            var addressSpace = new PredefinedNodesAddressSpace(
                CreateContext(),
                [],
                (n, _) =>
                {
                    added = n;
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
                    new[] { CreateObject("One"), CreateObject("Two") },
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
            NodeIdDictionary<NodeState> predefinedNodes = [];
            BaseObjectState node = CreateObject("Lookup");
            predefinedNodes[node.NodeId] = node;
            var addressSpace = CreateAddressSpace(predefinedNodes);

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
            NodeIdDictionary<NodeState> predefinedNodes = [];
            Func<NodeState, CancellationToken, ValueTask> addAsync = (_, _) => default;
            Func<NodeId, CancellationToken, ValueTask<bool>> removeAsync = (_, _) => new ValueTask<bool>(false);

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
            NodeIdDictionary<NodeState> predefinedNodes)
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
