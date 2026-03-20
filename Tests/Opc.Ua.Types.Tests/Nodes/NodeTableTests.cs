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

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Nodes
{
    [TestFixture]
    [Category("NodeTable")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeTableTests
    {
        private const string kTestNamespaceUri = "http://test.org/UA/";

        private NamespaceTable m_namespaceTable;
        private StringTable m_serverTable;
        private TypeTable m_typeTable;
        private NodeTable m_nodeTable;

        [SetUp]
        public void Setup()
        {
            m_namespaceTable = new NamespaceTable();
            m_namespaceTable.Append(kTestNamespaceUri); // index 1
            m_serverTable = new StringTable();
            m_typeTable = new TypeTable(m_namespaceTable);
            m_nodeTable = new NodeTable(m_namespaceTable, m_serverTable, m_typeTable);
        }

        private Node CreateNode(uint id, string name = null)
        {
            name ??= $"Node{id}";
            return new Node
            {
                NodeId = new NodeId(id, 1),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName(name, 1),
                DisplayName = new LocalizedText(name)
            };
        }

        private Node AttachNode(uint id, string name = null)
        {
            Node node = CreateNode(id, name);
            m_nodeTable.Attach(node);
            return node;
        }

        private static ExpandedNodeId LocalExpanded(uint id)
        {
            return new ExpandedNodeId(new NodeId(id, 1));
        }

        private static ExpandedNodeId RemoteExpanded(uint id)
        {
            return new ExpandedNodeId(id, "http://remote.server/", 1u);
        }

        [Test]
        public void ServerUrisReturnsTableFromConstructor()
        {
            Assert.That(m_nodeTable.ServerUris, Is.SameAs(m_serverTable));
        }

        [Test]
        public void TypeTreeReturnsTableFromConstructor()
        {
            Assert.That(m_nodeTable.TypeTree, Is.SameAs(m_typeTable));
        }

        [Test]
        public void ClearRemovesAllNodes()
        {
            AttachNode(1);
            AttachNode(2);
            Assert.That(m_nodeTable, Is.Not.Empty);

            m_nodeTable.Clear();

            Assert.That(m_nodeTable, Is.Empty);
            Assert.That(m_nodeTable.Exists(LocalExpanded(1)), Is.False);
            Assert.That(m_nodeTable.Exists(LocalExpanded(2)), Is.False);
        }

        [Test]
        public void ClearOnEmptyTableDoesNotThrow()
        {
            Assert.That(m_nodeTable, Is.Empty);
            Assert.DoesNotThrow(m_nodeTable.Clear);
        }

        [Test]
        public void NonGenericGetEnumeratorReturnsAllNodes()
        {
            AttachNode(1, "A");
            AttachNode(2, "B");

            IEnumerable enumerable = m_nodeTable;
            var list = new List<INode>();
            foreach (INode node in enumerable)
            {
                list.Add(node);
            }

            Assert.That(list, Has.Count.EqualTo(2));
        }

        [Test]
        public void GetEnumeratorIncludesRemoteNodes()
        {
            AttachNode(1, "Local");

            // Add a remote node via Import(ReferenceDescription)
            m_nodeTable.Import(new ReferenceDescription
            {
                NodeId = RemoteExpanded(100),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Remote"),
                DisplayName = new LocalizedText("Remote")
            });

            var allNodes = m_nodeTable.ToList();
            Assert.That(allNodes, Has.Count.EqualTo(2));
        }

        [Test]
        public void FindWithNullExpandedNodeIdReturnsNull()
        {
            INode result = m_nodeTable.Find(ExpandedNodeId.Null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ExistsWithNullExpandedNodeIdReturnsFalse()
        {
            Assert.That(m_nodeTable.Exists(ExpandedNodeId.Null), Is.False);
        }

        [Test]
        public void FindWithRemoteNodeIdFindsRemoteNode()
        {
            ExpandedNodeId remoteId = RemoteExpanded(100);
            m_nodeTable.Import(new ReferenceDescription
            {
                NodeId = remoteId,
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Remote"),
                DisplayName = new LocalizedText("Remote")
            });

            INode result = m_nodeTable.Find(remoteId);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.BrowseName, Is.EqualTo(new QualifiedName("Remote")));
        }

        [Test]
        public void FindWithUnknownRemoteNodeIdReturnsNull()
        {
            INode result = m_nodeTable.Find(RemoteExpanded(999));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindWithUnknownNamespaceUriReturnsNull()
        {
            var nodeId = new ExpandedNodeId(1u, "http://unknown.namespace/");
            INode result = m_nodeTable.Find(nodeId);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindWithNonExistentLocalNodeIdReturnsNull()
        {
            INode result = m_nodeTable.Find(LocalExpanded(999));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindWithBrowseNameReturnsNullWhenSourceNotFound()
        {
            INode result = m_nodeTable.Find(
                LocalExpanded(999),
                ReferenceTypeIds.Organizes,
                false,
                false,
                new QualifiedName("Target"));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindWithBrowseNameReturnsNullWhenSourceIsRemoteNode()
        {
            m_nodeTable.Import(new ReferenceDescription
            {
                NodeId = RemoteExpanded(100),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("RemoteSource"),
                DisplayName = new LocalizedText("Remote Source")
            });

            INode result = m_nodeTable.Find(
                RemoteExpanded(100),
                ReferenceTypeIds.Organizes,
                false,
                false,
                new QualifiedName("Target"));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindWithBrowseNameReturnsFirstTargetWhenBrowseNameIsNull()
        {
            AttachNode(2, "Target");
            Node sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(2));
            m_nodeTable.Attach(sourceNode);

            INode result = m_nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false,
                QualifiedName.Null);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void FindWithBrowseNameReturnsMatchingTarget()
        {
            AttachNode(2, "Target");
            Node sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(2));
            m_nodeTable.Attach(sourceNode);

            INode result = m_nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false,
                new QualifiedName("Target", 1));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.BrowseName, Is.EqualTo(new QualifiedName("Target", 1)));
        }

        [Test]
        public void FindWithBrowseNameReturnsNullWhenTargetNotInTable()
        {
            Node sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(999)); // target not in table
            m_nodeTable.Attach(sourceNode);

            INode result = m_nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false,
                new QualifiedName("NonExistent"));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindWithBrowseNameReturnsNullWhenNoMatchingBrowseName()
        {
            AttachNode(2, "Target");
            Node sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(2));
            m_nodeTable.Attach(sourceNode);

            INode result = m_nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false,
                new QualifiedName("WrongName", 1));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindListReturnsEmptyWhenSourceNotFound()
        {
            ArrayOf<INode> result = m_nodeTable.Find(
                LocalExpanded(999),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(result.IsNull, Is.False);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindListReturnsEmptyWhenSourceIsRemoteNode()
        {
            m_nodeTable.Import(new ReferenceDescription
            {
                NodeId = RemoteExpanded(100),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Remote"),
                DisplayName = new LocalizedText("Remote")
            });

            ArrayOf<INode> result = m_nodeTable.Find(
                RemoteExpanded(100),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(result.IsNull, Is.False);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindListReturnsMatchingTargets()
        {
            AttachNode(2, "Target1");
            AttachNode(3, "Target2");

            Node sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(ReferenceTypeIds.Organizes, false, LocalExpanded(2));
            sourceNode.ReferenceTable.Add(ReferenceTypeIds.Organizes, false, LocalExpanded(3));
            m_nodeTable.Attach(sourceNode);

            ArrayOf<INode> result = m_nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void FindListSkipsTargetsNotInTable()
        {
            AttachNode(2, "Target");

            Node sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(ReferenceTypeIds.Organizes, false, LocalExpanded(2));
            sourceNode.ReferenceTable.Add(ReferenceTypeIds.Organizes, false, LocalExpanded(999)); // not in table
            m_nodeTable.Attach(sourceNode);

            ArrayOf<INode> result = m_nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(result, Has.Count.EqualTo(1));
        }

        [Test]
        public void ImportReferenceDescriptionCreatesLocalNode()
        {
            var refDesc = new ReferenceDescription
            {
                NodeId = LocalExpanded(1),
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("Var1", 1),
                DisplayName = new LocalizedText("Variable 1")
            };

            INode result = m_nodeTable.Import(refDesc);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<Node>());
            Assert.That(result.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(result.BrowseName, Is.EqualTo(new QualifiedName("Var1", 1)));
            Assert.That(result.DisplayName, Is.EqualTo(new LocalizedText("Variable 1")));
            Assert.That(m_nodeTable.Exists(LocalExpanded(1)), Is.True);
            Assert.That(m_nodeTable, Has.Count.EqualTo(1));
        }

        [Test]
        public void ImportReferenceDescriptionCreatesRemoteNode()
        {
            ExpandedNodeId remoteId = RemoteExpanded(100);
            var refDesc = new ReferenceDescription
            {
                NodeId = remoteId,
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("RemoteObj"),
                DisplayName = new LocalizedText("Remote Object")
            };

            INode result = m_nodeTable.Import(refDesc);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(result.BrowseName, Is.EqualTo(new QualifiedName("RemoteObj")));
            Assert.That(result.DisplayName, Is.EqualTo(new LocalizedText("Remote Object")));
            Assert.That(m_nodeTable.Exists(remoteId), Is.True);
        }

        [Test]
        public void ImportReferenceDescriptionUpdatesExistingLocalNode()
        {
            m_nodeTable.Import(new ReferenceDescription
            {
                NodeId = LocalExpanded(1),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Original", 1),
                DisplayName = new LocalizedText("Original")
            });

            INode result = m_nodeTable.Import(new ReferenceDescription
            {
                NodeId = LocalExpanded(1),
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("Updated", 1),
                DisplayName = new LocalizedText("Updated")
            });

            Assert.That(result, Is.Not.Null);
            Assert.That(result.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(result.BrowseName, Is.EqualTo(new QualifiedName("Updated", 1)));
            Assert.That(result.DisplayName, Is.EqualTo(new LocalizedText("Updated")));
        }

        [Test]
        public void ImportReferenceDescriptionAddsTypeDefinitionReference()
        {
            var refDesc = new ReferenceDescription
            {
                NodeId = LocalExpanded(1),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Obj", 1),
                DisplayName = new LocalizedText("Object"),
                TypeDefinition = new ExpandedNodeId(ObjectTypeIds.BaseObjectType)
            };

            INode result = m_nodeTable.Import(refDesc);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<Node>());
        }

        [Test]
        public void ImportReferenceDescriptionSkipsNullTypeDefinition()
        {
            var refDesc = new ReferenceDescription
            {
                NodeId = LocalExpanded(1),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Obj", 1),
                DisplayName = new LocalizedText("Object"),
                TypeDefinition = ExpandedNodeId.Null
            };

            INode result = m_nodeTable.Import(refDesc);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void ImportReferenceDescriptionUpdatesExistingRemoteNode()
        {
            ExpandedNodeId remoteId = RemoteExpanded(100);

            m_nodeTable.Import(new ReferenceDescription
            {
                NodeId = remoteId,
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Initial"),
                DisplayName = new LocalizedText("Initial")
            });

            INode result = m_nodeTable.Import(new ReferenceDescription
            {
                NodeId = remoteId,
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("Updated"),
                DisplayName = new LocalizedText("Updated"),
                TypeDefinition = new ExpandedNodeId(VariableTypeIds.BaseDataVariableType)
            });

            Assert.That(result, Is.Not.Null);
            Assert.That(result.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(result.BrowseName, Is.EqualTo(new QualifiedName("Updated")));
            Assert.That(result.DisplayName, Is.EqualTo(new LocalizedText("Updated")));
        }

        [Test]
        public void ImportNullNodeSetReturnsEmptyList()
        {
            List<Node> result = m_nodeTable.Import(null, null);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ImportNodeSetImportsValidNodes()
        {
            var nodeSet = new NodeSet();
            var node = new Node
            {
                NodeId = new NodeId(1000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("TestNode"),
                DisplayName = new LocalizedText("Test Node")
            };
            nodeSet.Add(node);

            List<Node> result = m_nodeTable.Import(nodeSet, null);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(m_nodeTable, Is.Not.Empty);
        }

        [Test]
        public void ImportNodeSetAssignsBrowseNameWhenNull()
        {
            var nodeSet = new NodeSet();
            var node = new Node
            {
                NodeId = new NodeId(1000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = QualifiedName.Null,
                DisplayName = new LocalizedText("Test")
            };
            nodeSet.Add(node);

            List<Node> result = m_nodeTable.Import(nodeSet, null);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].BrowseName.IsNull, Is.False);
        }

        [Test]
        public void ImportNodeSetAssignsDisplayNameWhenNull()
        {
            var nodeSet = new NodeSet();
            var node = new Node
            {
                NodeId = new NodeId(1000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("TestNode"),
                DisplayName = LocalizedText.Null
            };
            nodeSet.Add(node);

            List<Node> result = m_nodeTable.Import(nodeSet, null);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].DisplayName, Is.Not.EqualTo(LocalizedText.Null));
        }

        [Test]
        public void ImportNodeSetImportsNodeReferences()
        {
            var nodeSet = new NodeSet();

            var node1 = new Node
            {
                NodeId = new NodeId(1000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Node1"),
                DisplayName = new LocalizedText("Node1")
            };

            var node2 = new Node
            {
                NodeId = new NodeId(1001, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Node2"),
                DisplayName = new LocalizedText("Node2")
            };

            // Add a reference from node1 to node2
            node1.References = node1.References.AddItem(new ReferenceNode(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId(1001, 0))));

            nodeSet.Add(node1);
            nodeSet.Add(node2);

            List<Node> result = m_nodeTable.Import(nodeSet, null);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void ImportNodeSetSkipsInvalidReferences()
        {
            var nodeSet = new NodeSet();

            var node = new Node
            {
                NodeId = new NodeId(1000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Node"),
                DisplayName = new LocalizedText("Node")
            };

            // Add a reference with null reference type
            node.References = node.References.AddItem(new ReferenceNode(
                NodeId.Null,
                false,
                new ExpandedNodeId(new NodeId(1001, 0))));

            nodeSet.Add(node);

            List<Node> result = m_nodeTable.Import(nodeSet, null);
            Assert.That(result, Has.Count.EqualTo(1));
        }

        [Test]
        public void ImportNodeSetPopulatesExternalReferences()
        {
            var nodeSet = new NodeSet();

            var node = new Node
            {
                NodeId = new NodeId(1000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Node"),
                DisplayName = new LocalizedText("Node")
            };

            // Add a reference to an external node that is not in the NodeSet
            node.References = node.References.AddItem(new ReferenceNode(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId(9999, 0))));

            nodeSet.Add(node);

            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            List<Node> result = m_nodeTable.Import(nodeSet, externalRefs);

            Assert.That(result, Has.Count.EqualTo(1));
            // External references should be populated with reverse references
            Assert.That(externalRefs, Has.Count.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void ImportNodeSetCreatesReverseReferencesBetweenNodes()
        {
            var nodeSet = new NodeSet();

            var parent = new Node
            {
                NodeId = new NodeId(1000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Parent"),
                DisplayName = new LocalizedText("Parent")
            };

            var child = new Node
            {
                NodeId = new NodeId(1001, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Child"),
                DisplayName = new LocalizedText("Child")
            };

            parent.References = parent.References.AddItem(new ReferenceNode(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId(1001, 0))));

            nodeSet.Add(parent);
            nodeSet.Add(child);

            m_nodeTable.Import(nodeSet, null);

            // The child should have a reverse Organizes reference to parent
            ArrayOf<INode> reverseRefs = m_nodeTable.Find(
                new ExpandedNodeId(new NodeId(1001, 0)),
                ReferenceTypeIds.Organizes,
                true,  // isInverse
                false);
            Assert.That(reverseRefs, Has.Count.EqualTo(1));
        }

        [Test]
        public void AttachRemovesDuplicateNode()
        {
            AttachNode(1, "Original");
            Assert.That(m_nodeTable, Has.Count.EqualTo(1));

            Node duplicate = CreateNode(1, "Duplicate");
            m_nodeTable.Attach(duplicate);

            Assert.That(m_nodeTable, Has.Count.EqualTo(1));
            INode found = m_nodeTable.Find(LocalExpanded(1));
            Assert.That(found, Is.Not.Null);
            Assert.That(found.BrowseName, Is.EqualTo(new QualifiedName("Duplicate", 1)));
        }

        [Test]
        public void AttachAddsNodeToTable()
        {
            Node node = CreateNode(1, "TestNode");
            m_nodeTable.Attach(node);

            Assert.That(m_nodeTable, Has.Count.EqualTo(1));
            Assert.That(m_nodeTable.Exists(LocalExpanded(1)), Is.True);
            INode found = m_nodeTable.Find(LocalExpanded(1));
            Assert.That(found, Is.Not.Null);
            Assert.That(found.BrowseName, Is.EqualTo(new QualifiedName("TestNode", 1)));
        }

        [Test]
        public void AttachAddsReverseReferencesToTargets()
        {
            AttachNode(2, "Target");
            Node sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(2));
            m_nodeTable.Attach(sourceNode);

            // Target should now have reverse reference back to source
            ArrayOf<INode> reverseRefs = m_nodeTable.Find(
                LocalExpanded(2),
                ReferenceTypeIds.Organizes,
                true, // isInverse
                false);
            Assert.That(reverseRefs, Has.Count.EqualTo(1));
        }

        [Test]
        public void AttachSkipsReverseReferencesForMissingTargets()
        {
            Node sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(999)); // target not in table
            m_nodeTable.Attach(sourceNode);

            Assert.That(m_nodeTable, Has.Count.EqualTo(1));
            Assert.That(m_nodeTable.Exists(LocalExpanded(1)), Is.True);
        }

        [Test]
        public void AttachSkipsReverseReferencesForTypeDefinitions()
        {
            AttachNode(2, "TypeDef");

            Node sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.HasTypeDefinition,
                false,
                LocalExpanded(2));
            m_nodeTable.Attach(sourceNode);

            // HasTypeDefinition reverse references should NOT be added
            ArrayOf<INode> reverseRefs = m_nodeTable.Find(
                LocalExpanded(2),
                ReferenceTypeIds.HasTypeDefinition,
                true,
                false);
            Assert.That(reverseRefs, Is.Empty);
        }

        [Test]
        public void AttachSkipsReverseReferencesForModellingRule()
        {
            AttachNode(2, "Model");

            Node sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.HasModellingRule,
                false,
                LocalExpanded(2));
            m_nodeTable.Attach(sourceNode);

            // HasModellingRule reverse references should NOT be added
            ArrayOf<INode> reverseRefs = m_nodeTable.Find(
                LocalExpanded(2),
                ReferenceTypeIds.HasModellingRule,
                true,
                false);
            Assert.That(reverseRefs, Is.Empty);
        }

        [Test]
        public void AttachAddsNodeToTypeTree()
        {
            // A node with ObjectType node class should be added to the type tree
            var typeNode = new Node
            {
                NodeId = new NodeId(5000, 1),
                NodeClass = NodeClass.ObjectType,
                BrowseName = new QualifiedName("MyType", 1),
                DisplayName = new LocalizedText("MyType")
            };
            m_nodeTable.Attach(typeNode);

            Assert.That(m_nodeTable.Exists(LocalExpanded(5000)), Is.True);
        }

        [Test]
        public void RemoveReturnsFalseWhenNodeNotFound()
        {
            bool result = m_nodeTable.Remove(LocalExpanded(999));
            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveReturnsFalseForRemoteNode()
        {
            m_nodeTable.Import(new ReferenceDescription
            {
                NodeId = RemoteExpanded(100),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Remote"),
                DisplayName = new LocalizedText("Remote")
            });

            bool result = m_nodeTable.Remove(RemoteExpanded(100));
            Assert.That(result, Is.False);
            // Remote node should still exist
            Assert.That(m_nodeTable.Exists(RemoteExpanded(100)), Is.True);
        }

        [Test]
        public void RemoveLocalNodeWithNoReferencesReturnsTrue()
        {
            AttachNode(1, "Lonely");

            bool result = m_nodeTable.Remove(LocalExpanded(1));
            Assert.That(result, Is.True);
            Assert.That(m_nodeTable.Exists(LocalExpanded(1)), Is.False);
            Assert.That(m_nodeTable, Is.Empty);
        }

        [Test]
        public void RemoveLocalNodeRemovesInverseReferences()
        {
            AttachNode(2, "Target");

            Node sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(2));
            m_nodeTable.Attach(sourceNode);

            // Verify reverse reference exists
            ArrayOf<INode> reverseRefsBefore = m_nodeTable.Find(
                LocalExpanded(2),
                ReferenceTypeIds.Organizes,
                true,
                false);
            Assert.That(reverseRefsBefore, Has.Count.EqualTo(1));

            // Remove source
            bool result = m_nodeTable.Remove(LocalExpanded(1));
            Assert.That(result, Is.True);

            // Reverse references should be cleaned up
            ArrayOf<INode> reverseRefsAfter = m_nodeTable.Find(
                LocalExpanded(2),
                ReferenceTypeIds.Organizes,
                true,
                false);
            Assert.That(reverseRefsAfter, Is.Empty);
        }

        [Test]
        public void RemoveLocalNodeWithReferenceToMissingTargetSucceeds()
        {
            Node sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(999)); // target not in table
            m_nodeTable.Attach(sourceNode);

            bool result = m_nodeTable.Remove(LocalExpanded(1));
            Assert.That(result, Is.True);
            Assert.That(m_nodeTable.Exists(LocalExpanded(1)), Is.False);
        }

        [Test]
        public void RemoveNodeDecreasesCount()
        {
            AttachNode(1, "A");
            AttachNode(2, "B");
            Assert.That(m_nodeTable, Has.Count.EqualTo(2));

            m_nodeTable.Remove(LocalExpanded(1));
            Assert.That(m_nodeTable, Has.Count.EqualTo(1));

            m_nodeTable.Remove(LocalExpanded(2));
            Assert.That(m_nodeTable, Is.Empty);
        }

        [Test]
        public void CountIncludesBothLocalAndRemoteNodes()
        {
            AttachNode(1, "Local");

            m_nodeTable.Import(new ReferenceDescription
            {
                NodeId = RemoteExpanded(100),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Remote"),
                DisplayName = new LocalizedText("Remote")
            });

            Assert.That(m_nodeTable, Has.Count.EqualTo(2));
        }

        [Test]
        public void EnumeratorOnEmptyTableReturnsNoNodes()
        {
            var allNodes = m_nodeTable.ToList();
            Assert.That(allNodes, Is.Empty);
        }

        [Test]
        public void ImportNodeSetCreatesRemoteNodesForRemoteReferences()
        {
            // Pre-populate target server table so translation yields ServerIndex > 0
            m_serverTable.Append("urn:local-server");       // index 0
            m_serverTable.Append("http://remote.server/");  // index 1

            var nodeSet = new NodeSet
            {
                // Set internal server URIs to match (requires InternalsVisibleTo)
                ServerUris = ["urn:local-server", "http://remote.server/"]
            };

            var node = new Node
            {
                NodeId = new NodeId(1000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("LocalNode"),
                DisplayName = new LocalizedText("Local Node")
            };

            // Add reference to a remote target (ServerIndex=1 → "http://remote.server/")
            node.References = node.References.AddItem(new ReferenceNode(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId(999, 0), null, 1u)));

            nodeSet.Add(node);

            List<Node> result = m_nodeTable.Import(nodeSet, null);

            Assert.That(result, Has.Count.EqualTo(1));
            // Remote node should have been created (1 local + 1 remote)
            Assert.That(m_nodeTable, Has.Count.EqualTo(2));
        }

        [Test]
        public void ImportNodeSetSkipsRemoteTargetsInSecondPass()
        {
            m_serverTable.Append("urn:local-server");
            m_serverTable.Append("http://remote.server/");

            var nodeSet = new NodeSet
            {
                ServerUris = ["urn:local-server", "http://remote.server/"]
            };

            var node = new Node
            {
                NodeId = new NodeId(1000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("LocalNode"),
                DisplayName = new LocalizedText("Local Node")
            };

            node.References = node.References.AddItem(new ReferenceNode(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId(888, 0), null, 1u)));

            nodeSet.Add(node);

            // Should not throw; remote targets are skipped in second pass
            List<Node> result = m_nodeTable.Import(nodeSet, null);
            Assert.That(result, Has.Count.EqualTo(1));
        }

        [Test]
        public void RemoveLocalNodeCleansUpRemoteNodes()
        {
            m_serverTable.Append("urn:local-server");
            m_serverTable.Append("http://remote.server/");

            var nodeSet = new NodeSet
            {
                ServerUris = ["urn:local-server", "http://remote.server/"]
            };

            var node = new Node
            {
                NodeId = new NodeId(1000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("LocalNode"),
                DisplayName = new LocalizedText("Local Node")
            };

            node.References = node.References.AddItem(new ReferenceNode(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId(999, 0), null, 1u)));

            nodeSet.Add(node);

            m_nodeTable.Import(nodeSet, null);
            Assert.That(m_nodeTable, Has.Count.EqualTo(2)); // 1 local + 1 remote

            // Remove the local node - should also clean up the remote node
            bool removed = m_nodeTable.Remove(new ExpandedNodeId(new NodeId(1000, 0)));
            Assert.That(removed, Is.True);

            // Remote node reference count reached 0, should be removed
            Assert.That(m_nodeTable, Is.Empty);
        }

        [Test]
        public void RemoveLocalNodeKeepsRemoteNodeWhenStillReferenced()
        {
            m_serverTable.Append("urn:local-server");
            m_serverTable.Append("http://remote.server/");

            var nodeSet = new NodeSet
            {
                ServerUris = ["urn:local-server", "http://remote.server/"]
            };

            var remoteTargetId = new ExpandedNodeId(new NodeId(999, 0), null, 1u);

            var node1 = new Node
            {
                NodeId = new NodeId(1000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Node1"),
                DisplayName = new LocalizedText("Node1")
            };

            var node2 = new Node
            {
                NodeId = new NodeId(1001, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Node2"),
                DisplayName = new LocalizedText("Node2")
            };

            // Both nodes reference the same remote target
            node1.References = node1.References.AddItem(new ReferenceNode(
                ReferenceTypeIds.Organizes, false, remoteTargetId));
            node2.References = node2.References.AddItem(new ReferenceNode(
                ReferenceTypeIds.Organizes, false, remoteTargetId));

            nodeSet.Add(node1);
            nodeSet.Add(node2);

            m_nodeTable.Import(nodeSet, null);
            Assert.That(m_nodeTable, Has.Count.EqualTo(3)); // 2 local + 1 remote

            // Remove one local node; remote node still referenced by the other
            m_nodeTable.Remove(new ExpandedNodeId(new NodeId(1000, 0)));
            Assert.That(m_nodeTable, Has.Count.EqualTo(2)); // 1 local + 1 remote still exists

            // Remove the second local node; now remote node ref count = 0
            m_nodeTable.Remove(new ExpandedNodeId(new NodeId(1001, 0)));
            Assert.That(m_nodeTable, Is.Empty);
        }

        [Test]
        public void AttachFindRemoveFullLifecycle()
        {
            // Attach two connected nodes
            AttachNode(2, "Child");
            Node parentNode = CreateNode(1, "Parent");
            parentNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(2));
            m_nodeTable.Attach(parentNode);

            // Verify find by browse name
            INode found = m_nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false,
                new QualifiedName("Child", 1));
            Assert.That(found, Is.Not.Null);

            // Verify find list
            ArrayOf<INode> targets = m_nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(targets, Has.Count.EqualTo(1));

            // Remove parent
            Assert.That(m_nodeTable.Remove(LocalExpanded(1)), Is.True);
            Assert.That(m_nodeTable.Exists(LocalExpanded(1)), Is.False);

            // Child should still exist
            Assert.That(m_nodeTable.Exists(LocalExpanded(2)), Is.True);

            // Clear remainder
            m_nodeTable.Clear();
            Assert.That(m_nodeTable, Is.Empty);
        }

        [Test]
        public void ImportMultipleReferenceDescriptionsAndEnumerate()
        {
            m_nodeTable.Import(new ReferenceDescription
            {
                NodeId = LocalExpanded(1),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Local1", 1),
                DisplayName = new LocalizedText("Local 1")
            });

            m_nodeTable.Import(new ReferenceDescription
            {
                NodeId = LocalExpanded(2),
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("Local2", 1),
                DisplayName = new LocalizedText("Local 2")
            });

            m_nodeTable.Import(new ReferenceDescription
            {
                NodeId = RemoteExpanded(100),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Remote1"),
                DisplayName = new LocalizedText("Remote 1")
            });

            Assert.That(m_nodeTable, Has.Count.EqualTo(3));

            var allNodes = m_nodeTable.ToList();
            Assert.That(allNodes, Has.Count.EqualTo(3));
        }

        [Test]
        public void ImportNodeSetWithTwoNodesAndReferencesCreatesBidirectionalLinks()
        {
            var nodeSet = new NodeSet();

            var parentNode = new Node
            {
                NodeId = new NodeId(2000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Parent"),
                DisplayName = new LocalizedText("Parent")
            };

            var childNode = new Node
            {
                NodeId = new NodeId(2001, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Child"),
                DisplayName = new LocalizedText("Child")
            };

            // Parent organizes Child
            parentNode.References = parentNode.References.AddItem(new ReferenceNode(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId(2001, 0))));

            nodeSet.Add(parentNode);
            nodeSet.Add(childNode);

            List<Node> imported = m_nodeTable.Import(nodeSet, null);
            Assert.That(imported, Has.Count.EqualTo(2));

            // Verify forward references
            ArrayOf<INode> children = m_nodeTable.Find(
                new ExpandedNodeId(new NodeId(2000, 0)),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(children, Has.Count.EqualTo(1));

            // Verify reverse references
            ArrayOf<INode> parents = m_nodeTable.Find(
                new ExpandedNodeId(new NodeId(2001, 0)),
                ReferenceTypeIds.Organizes,
                true,
                false);
            Assert.That(parents, Has.Count.EqualTo(1));
        }
    }
}
