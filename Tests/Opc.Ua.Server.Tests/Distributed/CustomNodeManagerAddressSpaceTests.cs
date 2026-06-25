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

using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Distributed;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Distributed
{
    /// <summary>
    /// Tests the <see cref="ILocalAddressSpace"/> adapter exposed by
    /// <see cref="CustomNodeManager2"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class CustomNodeManagerAddressSpaceTests
    {
        private const ushort NamespaceIndex = 1;
        private const string NamespaceUri = "urn:test:custom-node-manager-address-space";

        [Test]
        public void CreateLocalAddressSpaceAdaptsPredefinedNodes()
        {
            Mock<IServerInternal> server = CreateServer();
            using var nodeManager = new TestNodeManager(server.Object, NamespaceUri);
            BaseDataVariableState root = NewVariable("root", null);
            BaseDataVariableState child = NewVariable("child", root);
            BaseDataVariableState secondRoot = NewVariable("second-root", null);
            root.AddChild(child);
            nodeManager.AddPredefinedNodePublic(nodeManager.SystemContext, root);
            nodeManager.AddPredefinedNodePublic(nodeManager.SystemContext, secondRoot);

            var source = (ILocalAddressSpaceSource)nodeManager;
            ILocalAddressSpace addressSpace = source.CreateLocalAddressSpace();

            Assert.That(addressSpace.Context, Is.SameAs(nodeManager.SystemContext));
            Assert.That(
                addressSpace.Nodes,
                Has.Exactly(1).Property(nameof(NodeState.NodeId)).EqualTo(root.NodeId));
            Assert.That(
                addressSpace.Nodes,
                Has.Exactly(1).Property(nameof(NodeState.NodeId)).EqualTo(secondRoot.NodeId));
            Assert.That(addressSpace.Nodes, Has.None.Property(nameof(NodeState.NodeId)).EqualTo(child.NodeId));
            Assert.That(addressSpace.TryGetNode(root.NodeId, out NodeState? found), Is.True);
            Assert.That(found, Is.SameAs(root));

            BaseDataVariableState added = NewVariable("added", null);
            NodeState? addedEventNode = null;
            addressSpace.NodeAdded += node => addedEventNode = node;

            addressSpace.AddOrUpdateNode(added);

            Assert.That(addedEventNode, Is.SameAs(added));
            Assert.That(addressSpace.TryGetNode(added.NodeId, out NodeState? addedFound), Is.True);
            Assert.That(addedFound, Is.SameAs(added));

            NodeId? removedEventNodeId = null;
            addressSpace.NodeRemoved += nodeId => removedEventNodeId = nodeId;

            bool removed = addressSpace.RemoveNode(added.NodeId);

            Assert.That(removed, Is.True);
            Assert.That(removedEventNodeId, Is.EqualTo(added.NodeId));
            Assert.That(addressSpace.TryGetNode(added.NodeId, out _), Is.False);
            Assert.That(addressSpace.RemoveNode(added.NodeId), Is.False);
        }

        private static BaseDataVariableState NewVariable(string id, NodeState? parent)
        {
            return new BaseDataVariableState(parent)
            {
                NodeId = new NodeId(id, NamespaceIndex),
                BrowseName = new QualifiedName(id, NamespaceIndex),
                DisplayName = new LocalizedText(id),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                Value = new Variant(1.0)
            };
        }

        private static Mock<IServerInternal> CreateServer()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(NamespaceUri);
            var serverUris = new StringTable();
            var server = new Mock<IServerInternal>();
            var masterNodeManager = new Mock<IMasterNodeManager>();
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.ServerUris).Returns(serverUris);
            server.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return server;
        }

        private sealed class TestNodeManager : CustomNodeManager2
        {
            public TestNodeManager(IServerInternal server, params string[] namespaceUris)
                : base(server, namespaceUris)
            {
            }

            public void AddPredefinedNodePublic(ISystemContext context, NodeState node)
            {
                AddPredefinedNode(context, node);
            }

            public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
            {
            }
        }
    }
}
