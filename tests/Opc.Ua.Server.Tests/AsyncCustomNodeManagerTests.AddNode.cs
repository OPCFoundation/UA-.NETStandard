// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests that the synchronous <see cref="IAsyncNodeManager.AddNode"/> takes the
    /// same registration steps as <c>AddPredefinedNodeAsync</c>, apart from the
    /// asynchronous behaviour attachment.
    /// </summary>
#pragma warning disable CA1001 // NUnit fixture; see AsyncCustomNodeManagerTests.cs.
    public partial class AsyncCustomNodeManagerTests
#pragma warning restore CA1001
    {
        /// <summary>
        /// A subtree added through <c>AddNode</c> raises the same per-node added
        /// notifications, in the same order, as one added through
        /// <c>AddPredefinedNodeAsync</c>.
        /// </summary>
        [Test]
        public async Task AddNodeRaisesSameNotificationsAsAddPredefinedNodeAsync()
        {
            using ITestNodeManager testManager = CreateManager();
            Assume.That(testManager is TestableAsyncCustomNodeManager,
                "Requires AsyncCustomNodeManager registration features");
            var manager = (TestableAsyncCustomNodeManager)testManager;
            ushort namespaceIndex = manager.NamespaceIndexes[0];

            BaseObjectState viaAsync = CreateObjectWithProperties("ViaAsync", 1000, namespaceIndex);
            await testManager.AddPredefinedNodeAsync(manager.SystemContext, viaAsync).ConfigureAwait(false);
            string[] asyncOrder = TakeNotifications(manager);

            BaseObjectState viaAddNode = CreateObjectWithProperties("ViaAddNode", 2000, namespaceIndex);
            manager.AddNode(viaAddNode);
            string[] addNodeOrder = TakeNotifications(manager);

            Assert.Multiple(() =>
            {
                Assert.That(asyncOrder, Is.EqualTo(s_viaAsyncOrder));
                Assert.That(addNodeOrder, Is.EqualTo(s_viaAddNodeOrder));
                Assert.That(manager.Find(viaAddNode.NodeId), Is.SameAs(viaAddNode));
                Assert.That(manager.Find(new NodeId(2001, namespaceIndex)), Is.Not.Null);
            });
        }

        /// <summary>
        /// <c>AddNode</c> assigns NodeIds to instance nodes that have none, like
        /// <c>AddPredefinedNodeAsync</c>, instead of indexing them under the null NodeId.
        /// </summary>
        [Test]
        public void AddNodeAssignsMissingInstanceNodeIds()
        {
            using ITestNodeManager testManager = CreateManager();
            Assume.That(testManager is TestableAsyncCustomNodeManager,
                "Requires AsyncCustomNodeManager registration features");
            var manager = (TestableAsyncCustomNodeManager)testManager;
            ushort namespaceIndex = manager.NamespaceIndexes[0];

            var root = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("Unnamed", namespaceIndex),
                DisplayName = LocalizedText.From("Unnamed"),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };
            var child = new PropertyState(root)
            {
                BrowseName = new QualifiedName("Child", namespaceIndex),
                DisplayName = LocalizedText.From("Child"),
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                TypeDefinitionId = VariableTypeIds.PropertyType
            };
            root.AddChild(child);

            manager.AddNode(root);

            Assert.Multiple(() =>
            {
                Assert.That(root.NodeId.IsNull, Is.False);
                Assert.That(child.NodeId.IsNull, Is.False);
                Assert.That(root.NodeId.NamespaceIndex, Is.EqualTo(namespaceIndex));
                Assert.That(child.NodeId.NamespaceIndex, Is.EqualTo(namespaceIndex));
                Assert.That(root.NodeId, Is.Not.EqualTo(child.NodeId));
                Assert.That(manager.Find(root.NodeId), Is.SameAs(root));
                Assert.That(manager.Find(child.NodeId), Is.SameAs(child));
            });
        }

        private static readonly string[] s_viaAsyncOrder = ["First", "Second", "ViaAsync"];
        private static readonly string[] s_viaAddNodeOrder = ["First", "Second", "ViaAddNode"];

        private static string[] TakeNotifications(TestableAsyncCustomNodeManager manager)
        {
            lock (manager.PredefinedNodeAddedNotifications)
            {
                string[] names = manager.PredefinedNodeAddedNotifications
                    .Select(node => node.BrowseName.Name!)
                    .ToArray();
                manager.PredefinedNodeAddedNotifications.Clear();
                return names;
            }
        }

        private static BaseObjectState CreateObjectWithProperties(
            string name,
            uint firstId,
            ushort namespaceIndex)
        {
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId(firstId, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = LocalizedText.From(name),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };
            CreateProperty(root, "First", firstId + 1, namespaceIndex);
            CreateProperty(root, "Second", firstId + 2, namespaceIndex);
            return root;
        }

        private static PropertyState CreateProperty(
            BaseObjectState parent,
            string name,
            uint id,
            ushort namespaceIndex)
        {
            var property = new PropertyState(parent)
            {
                NodeId = new NodeId(id, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = LocalizedText.From(name),
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                TypeDefinitionId = VariableTypeIds.PropertyType
            };
            parent.AddChild(property);
            return property;
        }
    }
}
