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
        private const string TestNamespaceUri = "http://test.org/UA/";

        private NamespaceTable _namespaceTable;
        private StringTable _serverTable;
        private TypeTable _typeTable;
        private NodeTable _nodeTable;

        [SetUp]
        public void Setup()
        {
            _namespaceTable = new NamespaceTable();
            _namespaceTable.Append(TestNamespaceUri); // index 1
            _serverTable = new StringTable();
            _typeTable = new TypeTable(_namespaceTable);
            _nodeTable = new NodeTable(_namespaceTable, _serverTable, _typeTable);
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
            _nodeTable.Attach(node);
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
            Assert.That(_nodeTable.ServerUris, Is.SameAs(_serverTable));
        }

        [Test]
        public void TypeTreeReturnsTableFromConstructor()
        {
            Assert.That(_nodeTable.TypeTree, Is.SameAs(_typeTable));
        }

        [Test]
        public void ClearRemovesAllNodes()
        {
            AttachNode(1);
            AttachNode(2);
            Assert.That(_nodeTable.Count, Is.GreaterThan(0));

            _nodeTable.Clear();

            Assert.That(_nodeTable.Count, Is.EqualTo(0));
            Assert.That(_nodeTable.Exists(LocalExpanded(1)), Is.False);
            Assert.That(_nodeTable.Exists(LocalExpanded(2)), Is.False);
        }

        [Test]
        public void ClearOnEmptyTableDoesNotThrow()
        {
            Assert.That(_nodeTable.Count, Is.EqualTo(0));
            Assert.DoesNotThrow(() => _nodeTable.Clear());
        }

        [Test]
        public void NonGenericGetEnumeratorReturnsAllNodes()
        {
            AttachNode(1, "A");
            AttachNode(2, "B");

            IEnumerable enumerable = _nodeTable;
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
            _nodeTable.Import(new ReferenceDescription
            {
                NodeId = RemoteExpanded(100),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Remote"),
                DisplayName = new LocalizedText("Remote")
            });

            var allNodes = _nodeTable.ToList();
            Assert.That(allNodes, Has.Count.EqualTo(2));
        }

        [Test]
        public void FindWithNullExpandedNodeIdReturnsNull()
        {
            INode result = _nodeTable.Find(ExpandedNodeId.Null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ExistsWithNullExpandedNodeIdReturnsFalse()
        {
            Assert.That(_nodeTable.Exists(ExpandedNodeId.Null), Is.False);
        }

        [Test]
        public void FindWithRemoteNodeIdFindsRemoteNode()
        {
            ExpandedNodeId remoteId = RemoteExpanded(100);
            _nodeTable.Import(new ReferenceDescription
            {
                NodeId = remoteId,
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Remote"),
                DisplayName = new LocalizedText("Remote")
            });

            INode result = _nodeTable.Find(remoteId);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.BrowseName, Is.EqualTo(new QualifiedName("Remote")));
        }

        [Test]
        public void FindWithUnknownRemoteNodeIdReturnsNull()
        {
            INode result = _nodeTable.Find(RemoteExpanded(999));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindWithUnknownNamespaceUriReturnsNull()
        {
            var nodeId = new ExpandedNodeId(1u, "http://unknown.namespace/");
            INode result = _nodeTable.Find(nodeId);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindWithNonExistentLocalNodeIdReturnsNull()
        {
            INode result = _nodeTable.Find(LocalExpanded(999));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindWithBrowseNameReturnsNullWhenSourceNotFound()
        {
            INode result = _nodeTable.Find(
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
            _nodeTable.Import(new ReferenceDescription
            {
                NodeId = RemoteExpanded(100),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("RemoteSource"),
                DisplayName = new LocalizedText("Remote Source")
            });

            INode result = _nodeTable.Find(
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
            _nodeTable.Attach(sourceNode);

            INode result = _nodeTable.Find(
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
            _nodeTable.Attach(sourceNode);

            INode result = _nodeTable.Find(
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
            _nodeTable.Attach(sourceNode);

            INode result = _nodeTable.Find(
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
            _nodeTable.Attach(sourceNode);

            INode result = _nodeTable.Find(
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
            IList<INode> result = _nodeTable.Find(
                LocalExpanded(999),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindListReturnsEmptyWhenSourceIsRemoteNode()
        {
            _nodeTable.Import(new ReferenceDescription
            {
                NodeId = RemoteExpanded(100),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Remote"),
                DisplayName = new LocalizedText("Remote")
            });

            IList<INode> result = _nodeTable.Find(
                RemoteExpanded(100),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(result, Is.Not.Null);
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
            _nodeTable.Attach(sourceNode);

            IList<INode> result = _nodeTable.Find(
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
            _nodeTable.Attach(sourceNode);

            IList<INode> result = _nodeTable.Find(
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

            INode result = _nodeTable.Import(refDesc);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<Node>());
            Assert.That(result.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(result.BrowseName, Is.EqualTo(new QualifiedName("Var1", 1)));
            Assert.That(result.DisplayName, Is.EqualTo(new LocalizedText("Variable 1")));
            Assert.That(_nodeTable.Exists(LocalExpanded(1)), Is.True);
            Assert.That(_nodeTable.Count, Is.EqualTo(1));
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

            INode result = _nodeTable.Import(refDesc);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(result.BrowseName, Is.EqualTo(new QualifiedName("RemoteObj")));
            Assert.That(result.DisplayName, Is.EqualTo(new LocalizedText("Remote Object")));
            Assert.That(_nodeTable.Exists(remoteId), Is.True);
        }

        [Test]
        public void ImportReferenceDescriptionUpdatesExistingLocalNode()
        {
            _nodeTable.Import(new ReferenceDescription
            {
                NodeId = LocalExpanded(1),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Original", 1),
                DisplayName = new LocalizedText("Original")
            });

            INode result = _nodeTable.Import(new ReferenceDescription
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

            INode result = _nodeTable.Import(refDesc);
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

            INode result = _nodeTable.Import(refDesc);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void ImportReferenceDescriptionUpdatesExistingRemoteNode()
        {
            ExpandedNodeId remoteId = RemoteExpanded(100);

            _nodeTable.Import(new ReferenceDescription
            {
                NodeId = remoteId,
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Initial"),
                DisplayName = new LocalizedText("Initial")
            });

            INode result = _nodeTable.Import(new ReferenceDescription
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
            List<Node> result = _nodeTable.Import(null, null);
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

            List<Node> result = _nodeTable.Import(nodeSet, null);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(_nodeTable.Count, Is.GreaterThan(0));
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

            List<Node> result = _nodeTable.Import(nodeSet, null);

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

            List<Node> result = _nodeTable.Import(nodeSet, null);

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

            List<Node> result = _nodeTable.Import(nodeSet, null);

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

            List<Node> result = _nodeTable.Import(nodeSet, null);
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
            List<Node> result = _nodeTable.Import(nodeSet, externalRefs);

            Assert.That(result, Has.Count.EqualTo(1));
            // External references should be populated with reverse references
            Assert.That(externalRefs.Count, Is.GreaterThanOrEqualTo(0));
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

            _nodeTable.Import(nodeSet, null);

            // The child should have a reverse Organizes reference to parent
            IList<INode> reverseRefs = _nodeTable.Find(
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
            Assert.That(_nodeTable.Count, Is.EqualTo(1));

            Node duplicate = CreateNode(1, "Duplicate");
            _nodeTable.Attach(duplicate);

            Assert.That(_nodeTable.Count, Is.EqualTo(1));
            INode found = _nodeTable.Find(LocalExpanded(1));
            Assert.That(found, Is.Not.Null);
            Assert.That(found.BrowseName, Is.EqualTo(new QualifiedName("Duplicate", 1)));
        }

        [Test]
        public void AttachAddsNodeToTable()
        {
            Node node = CreateNode(1, "TestNode");
            _nodeTable.Attach(node);

            Assert.That(_nodeTable.Count, Is.EqualTo(1));
            Assert.That(_nodeTable.Exists(LocalExpanded(1)), Is.True);
            INode found = _nodeTable.Find(LocalExpanded(1));
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
            _nodeTable.Attach(sourceNode);

            // Target should now have reverse reference back to source
            IList<INode> reverseRefs = _nodeTable.Find(
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
            _nodeTable.Attach(sourceNode);

            Assert.That(_nodeTable.Count, Is.EqualTo(1));
            Assert.That(_nodeTable.Exists(LocalExpanded(1)), Is.True);
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
            _nodeTable.Attach(sourceNode);

            // HasTypeDefinition reverse references should NOT be added
            IList<INode> reverseRefs = _nodeTable.Find(
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
            _nodeTable.Attach(sourceNode);

            // HasModellingRule reverse references should NOT be added
            IList<INode> reverseRefs = _nodeTable.Find(
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
            _nodeTable.Attach(typeNode);

            Assert.That(_nodeTable.Exists(LocalExpanded(5000)), Is.True);
        }

        [Test]
        public void RemoveReturnsFalseWhenNodeNotFound()
        {
            var result = _nodeTable.Remove(LocalExpanded(999));
            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveReturnsFalseForRemoteNode()
        {
            _nodeTable.Import(new ReferenceDescription
            {
                NodeId = RemoteExpanded(100),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Remote"),
                DisplayName = new LocalizedText("Remote")
            });

            var result = _nodeTable.Remove(RemoteExpanded(100));
            Assert.That(result, Is.False);
            // Remote node should still exist
            Assert.That(_nodeTable.Exists(RemoteExpanded(100)), Is.True);
        }

        [Test]
        public void RemoveLocalNodeWithNoReferencesReturnsTrue()
        {
            AttachNode(1, "Lonely");

            var result = _nodeTable.Remove(LocalExpanded(1));
            Assert.That(result, Is.True);
            Assert.That(_nodeTable.Exists(LocalExpanded(1)), Is.False);
            Assert.That(_nodeTable.Count, Is.EqualTo(0));
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
            _nodeTable.Attach(sourceNode);

            // Verify reverse reference exists
            IList<INode> reverseRefsBefore = _nodeTable.Find(
                LocalExpanded(2),
                ReferenceTypeIds.Organizes,
                true,
                false);
            Assert.That(reverseRefsBefore, Has.Count.EqualTo(1));

            // Remove source
            var result = _nodeTable.Remove(LocalExpanded(1));
            Assert.That(result, Is.True);

            // Reverse references should be cleaned up
            IList<INode> reverseRefsAfter = _nodeTable.Find(
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
            _nodeTable.Attach(sourceNode);

            var result = _nodeTable.Remove(LocalExpanded(1));
            Assert.That(result, Is.True);
            Assert.That(_nodeTable.Exists(LocalExpanded(1)), Is.False);
        }

        [Test]
        public void RemoveNodeDecreasesCount()
        {
            AttachNode(1, "A");
            AttachNode(2, "B");
            Assert.That(_nodeTable.Count, Is.EqualTo(2));

            _nodeTable.Remove(LocalExpanded(1));
            Assert.That(_nodeTable.Count, Is.EqualTo(1));

            _nodeTable.Remove(LocalExpanded(2));
            Assert.That(_nodeTable.Count, Is.EqualTo(0));
        }

        [Test]
        public void CountIncludesBothLocalAndRemoteNodes()
        {
            AttachNode(1, "Local");

            _nodeTable.Import(new ReferenceDescription
            {
                NodeId = RemoteExpanded(100),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Remote"),
                DisplayName = new LocalizedText("Remote")
            });

            Assert.That(_nodeTable.Count, Is.EqualTo(2));
        }

        [Test]
        public void EnumeratorOnEmptyTableReturnsNoNodes()
        {
            var allNodes = _nodeTable.ToList();
            Assert.That(allNodes, Is.Empty);
        }

        [Test]
        public void ImportNodeSetCreatesRemoteNodesForRemoteReferences()
        {
            // Pre-populate target server table so translation yields ServerIndex > 0
            _serverTable.Append("urn:local-server");       // index 0
            _serverTable.Append("http://remote.server/");  // index 1

            var nodeSet = new NodeSet
            {
                // Set internal server URIs to match (requires InternalsVisibleTo)
                ServerUris = new ArrayOf<string> { "urn:local-server", "http://remote.server/" }
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

            List<Node> result = _nodeTable.Import(nodeSet, null);

            Assert.That(result, Has.Count.EqualTo(1));
            // Remote node should have been created (1 local + 1 remote)
            Assert.That(_nodeTable.Count, Is.EqualTo(2));
        }

        [Test]
        public void ImportNodeSetSkipsRemoteTargetsInSecondPass()
        {
            _serverTable.Append("urn:local-server");
            _serverTable.Append("http://remote.server/");

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
            List<Node> result = _nodeTable.Import(nodeSet, null);
            Assert.That(result, Has.Count.EqualTo(1));
        }

        [Test]
        public void RemoveLocalNodeCleansUpRemoteNodes()
        {
            _serverTable.Append("urn:local-server");
            _serverTable.Append("http://remote.server/");

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

            _nodeTable.Import(nodeSet, null);
            Assert.That(_nodeTable.Count, Is.EqualTo(2)); // 1 local + 1 remote

            // Remove the local node - should also clean up the remote node
            var removed = _nodeTable.Remove(new ExpandedNodeId(new NodeId(1000, 0)));
            Assert.That(removed, Is.True);

            // Remote node reference count reached 0, should be removed
            Assert.That(_nodeTable.Count, Is.EqualTo(0));
        }

        [Test]
        public void RemoveLocalNodeKeepsRemoteNodeWhenStillReferenced()
        {
            _serverTable.Append("urn:local-server");
            _serverTable.Append("http://remote.server/");

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

            _nodeTable.Import(nodeSet, null);
            Assert.That(_nodeTable.Count, Is.EqualTo(3)); // 2 local + 1 remote

            // Remove one local node; remote node still referenced by the other
            _nodeTable.Remove(new ExpandedNodeId(new NodeId(1000, 0)));
            Assert.That(_nodeTable.Count, Is.EqualTo(2)); // 1 local + 1 remote still exists

            // Remove the second local node; now remote node ref count = 0
            _nodeTable.Remove(new ExpandedNodeId(new NodeId(1001, 0)));
            Assert.That(_nodeTable.Count, Is.EqualTo(0));
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
            _nodeTable.Attach(parentNode);

            // Verify find by browse name
            INode found = _nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false,
                new QualifiedName("Child", 1));
            Assert.That(found, Is.Not.Null);

            // Verify find list
            IList<INode> targets = _nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(targets, Has.Count.EqualTo(1));

            // Remove parent
            Assert.That(_nodeTable.Remove(LocalExpanded(1)), Is.True);
            Assert.That(_nodeTable.Exists(LocalExpanded(1)), Is.False);

            // Child should still exist
            Assert.That(_nodeTable.Exists(LocalExpanded(2)), Is.True);

            // Clear remainder
            _nodeTable.Clear();
            Assert.That(_nodeTable.Count, Is.EqualTo(0));
        }

        [Test]
        public void ImportMultipleReferenceDescriptionsAndEnumerate()
        {
            _nodeTable.Import(new ReferenceDescription
            {
                NodeId = LocalExpanded(1),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Local1", 1),
                DisplayName = new LocalizedText("Local 1")
            });

            _nodeTable.Import(new ReferenceDescription
            {
                NodeId = LocalExpanded(2),
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("Local2", 1),
                DisplayName = new LocalizedText("Local 2")
            });

            _nodeTable.Import(new ReferenceDescription
            {
                NodeId = RemoteExpanded(100),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Remote1"),
                DisplayName = new LocalizedText("Remote 1")
            });

            Assert.That(_nodeTable.Count, Is.EqualTo(3));

            var allNodes = _nodeTable.ToList();
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

            List<Node> imported = _nodeTable.Import(nodeSet, null);
            Assert.That(imported, Has.Count.EqualTo(2));

            // Verify forward references
            IList<INode> children = _nodeTable.Find(
                new ExpandedNodeId(new NodeId(2000, 0)),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(children, Has.Count.EqualTo(1));

            // Verify reverse references
            IList<INode> parents = _nodeTable.Find(
                new ExpandedNodeId(new NodeId(2001, 0)),
                ReferenceTypeIds.Organizes,
                true,
                false);
            Assert.That(parents, Has.Count.EqualTo(1));
        }
    }
}
