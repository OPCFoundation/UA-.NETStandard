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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Nodes
{
    /// <summary>
    /// Coverage tests for the <see cref="NodeTable"/> class.
    /// </summary>
    [TestFixture]
    [Category("NodeTable")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeTableCoverageTests
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

        #region Helpers

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
            var node = CreateNode(id, name);
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

        #endregion

        #region Constructor and Properties

        // Covers line 133: ServerUris getter
        [Test]
        public void ServerUrisReturnsTableFromConstructor()
        {
            Assert.That(_nodeTable.ServerUris, Is.SameAs(_serverTable));
        }

        // Covers line 136: TypeTree getter
        [Test]
        public void TypeTreeReturnsTableFromConstructor()
        {
            Assert.That(_nodeTable.TypeTree, Is.SameAs(_typeTable));
        }

        #endregion

        #region Clear

        // Covers lines 633-636
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

        // Covers lines 633-636 on an empty table
        [Test]
        public void ClearOnEmptyTableDoesNotThrow()
        {
            Assert.That(_nodeTable.Count, Is.EqualTo(0));
            Assert.DoesNotThrow(() => _nodeTable.Clear());
        }

        #endregion

        #region IEnumerable

        // Covers lines 273-276: non-generic GetEnumerator
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

        // Covers lines 256-264: generic GetEnumerator includes remote nodes
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

        #endregion

        #region Find(ExpandedNodeId) Edge Cases

        // Covers lines 700-702: InternalFind with null ExpandedNodeId
        [Test]
        public void FindWithNullExpandedNodeIdReturnsNull()
        {
            var result = _nodeTable.Find(ExpandedNodeId.Null);
            Assert.That(result, Is.Null);
        }

        // Covers lines 700-702
        [Test]
        public void ExistsWithNullExpandedNodeIdReturnsFalse()
        {
            Assert.That(_nodeTable.Exists(ExpandedNodeId.Null), Is.False);
        }

        // Covers lines 706-710: InternalFind with remote node lookup
        [Test]
        public void FindWithRemoteNodeIdFindsRemoteNode()
        {
            var remoteId = RemoteExpanded(100);
            _nodeTable.Import(new ReferenceDescription
            {
                NodeId = remoteId,
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Remote"),
                DisplayName = new LocalizedText("Remote")
            });

            var result = _nodeTable.Find(remoteId);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.BrowseName, Is.EqualTo(new QualifiedName("Remote")));
        }

        // Covers line 713: InternalFind with remote node not found
        [Test]
        public void FindWithUnknownRemoteNodeIdReturnsNull()
        {
            var result = _nodeTable.Find(RemoteExpanded(999));
            Assert.That(result, Is.Null);
        }

        // Covers lines 719-721: InternalFind with namespace URI not in table
        [Test]
        public void FindWithUnknownNamespaceUriReturnsNull()
        {
            var nodeId = new ExpandedNodeId(1u, "http://unknown.namespace/");
            var result = _nodeTable.Find(nodeId);
            Assert.That(result, Is.Null);
        }

        // Covers line 730: InternalFind with local node not found
        [Test]
        public void FindWithNonExistentLocalNodeIdReturnsNull()
        {
            var result = _nodeTable.Find(LocalExpanded(999));
            Assert.That(result, Is.Null);
        }

        #endregion

        #region Find (5-param with QualifiedName)

        // Covers lines 157-163: source not found
        [Test]
        public void FindWithBrowseNameReturnsNullWhenSourceNotFound()
        {
            var result = _nodeTable.Find(
                LocalExpanded(999),
                ReferenceTypeIds.Organizes,
                false,
                false,
                new QualifiedName("Target"));
            Assert.That(result, Is.Null);
        }

        // Covers lines 167-169: source is a remote node (not ILocalNode)
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

            var result = _nodeTable.Find(
                RemoteExpanded(100),
                ReferenceTypeIds.Organizes,
                false,
                false,
                new QualifiedName("Target"));
            Assert.That(result, Is.Null);
        }

        // Covers lines 173-191: browseName is null, returns first target
        [Test]
        public void FindWithBrowseNameReturnsFirstTargetWhenBrowseNameIsNull()
        {
            AttachNode(2, "Target");
            var sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(2));
            _nodeTable.Attach(sourceNode);

            var result = _nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false,
                QualifiedName.Null);
            Assert.That(result, Is.Not.Null);
        }

        // Covers lines 194-196: matching browseName
        [Test]
        public void FindWithBrowseNameReturnsMatchingTarget()
        {
            AttachNode(2, "Target");
            var sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(2));
            _nodeTable.Attach(sourceNode);

            var result = _nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false,
                new QualifiedName("Target", 1));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.BrowseName, Is.EqualTo(new QualifiedName("Target", 1)));
        }

        // Covers lines 180-186, 201-202: target not in table, returns null
        [Test]
        public void FindWithBrowseNameReturnsNullWhenTargetNotInTable()
        {
            var sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(999)); // target not in table
            _nodeTable.Attach(sourceNode);

            var result = _nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false,
                new QualifiedName("NonExistent"));
            Assert.That(result, Is.Null);
        }

        // Covers lines 194, 198, 201-202: no matching browse name
        [Test]
        public void FindWithBrowseNameReturnsNullWhenNoMatchingBrowseName()
        {
            AttachNode(2, "Target");
            var sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(2));
            _nodeTable.Attach(sourceNode);

            var result = _nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false,
                new QualifiedName("WrongName", 1));
            Assert.That(result, Is.Null);
        }

        #endregion

        #region Find (4-param returning IList)

        // Covers lines 210-219: source not found
        [Test]
        public void FindListReturnsEmptyWhenSourceNotFound()
        {
            var result = _nodeTable.Find(
                LocalExpanded(999),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        // Covers lines 223-225: source is remote node
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

            var result = _nodeTable.Find(
                RemoteExpanded(100),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        // Covers lines 229-248: returns matching targets
        [Test]
        public void FindListReturnsMatchingTargets()
        {
            AttachNode(2, "Target1");
            AttachNode(3, "Target2");

            var sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(ReferenceTypeIds.Organizes, false, LocalExpanded(2));
            sourceNode.ReferenceTable.Add(ReferenceTypeIds.Organizes, false, LocalExpanded(3));
            _nodeTable.Attach(sourceNode);

            var result = _nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(result, Has.Count.EqualTo(2));
        }

        // Covers lines 236-243: skips targets not in table
        [Test]
        public void FindListSkipsTargetsNotInTable()
        {
            AttachNode(2, "Target");

            var sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(ReferenceTypeIds.Organizes, false, LocalExpanded(2));
            sourceNode.ReferenceTable.Add(ReferenceTypeIds.Organizes, false, LocalExpanded(999)); // not in table
            _nodeTable.Attach(sourceNode);

            var result = _nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(result, Has.Count.EqualTo(1));
        }

        #endregion

        #region Import(ReferenceDescription)

        // Covers lines 439-461: creates new local node
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

            var result = _nodeTable.Import(refDesc);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<Node>());
            Assert.That(result.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(result.BrowseName, Is.EqualTo(new QualifiedName("Var1", 1)));
            Assert.That(result.DisplayName, Is.EqualTo(new LocalizedText("Variable 1")));
            Assert.That(_nodeTable.Exists(LocalExpanded(1)), Is.True);
            Assert.That(_nodeTable.Count, Is.EqualTo(1));
        }

        // Covers lines 446-451, 483-490: creates new remote node
        [Test]
        public void ImportReferenceDescriptionCreatesRemoteNode()
        {
            var remoteId = RemoteExpanded(100);
            var refDesc = new ReferenceDescription
            {
                NodeId = remoteId,
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("RemoteObj"),
                DisplayName = new LocalizedText("Remote Object")
            };

            var result = _nodeTable.Import(refDesc);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(result.BrowseName, Is.EqualTo(new QualifiedName("RemoteObj")));
            Assert.That(result.DisplayName, Is.EqualTo(new LocalizedText("Remote Object")));
            Assert.That(_nodeTable.Exists(remoteId), Is.True);
        }

        // Covers lines 441, 466-478: updates existing local node
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

            var result = _nodeTable.Import(new ReferenceDescription
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

        // Covers lines 472-476: adds HasTypeDefinition reference when present
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

            var result = _nodeTable.Import(refDesc);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<Node>());
        }

        // Covers line 472 (false branch): skips type definition when null
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

            var result = _nodeTable.Import(refDesc);
            Assert.That(result, Is.Not.Null);
        }

        // Covers lines 483-490: updates existing remote node attributes
        [Test]
        public void ImportReferenceDescriptionUpdatesExistingRemoteNode()
        {
            var remoteId = RemoteExpanded(100);

            _nodeTable.Import(new ReferenceDescription
            {
                NodeId = remoteId,
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Initial"),
                DisplayName = new LocalizedText("Initial")
            });

            var result = _nodeTable.Import(new ReferenceDescription
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

        #endregion

        #region Import(NodeSet)

        // Covers lines 292-297: null nodeSet
        [Test]
        public void ImportNullNodeSetReturnsEmptyList()
        {
            var result = _nodeTable.Import(null, null);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        // Covers lines 292-364, 367-428: imports valid nodes
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

            var result = _nodeTable.Import(nodeSet, null);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(_nodeTable.Count, Is.GreaterThan(0));
        }

        // Covers lines 312-314: auto-assigns BrowseName when null
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

            var result = _nodeTable.Import(nodeSet, null);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].BrowseName.IsNull, Is.False);
        }

        // Covers lines 318-320: auto-assigns DisplayName when null
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

            var result = _nodeTable.Import(nodeSet, null);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].DisplayName, Is.Not.EqualTo(LocalizedText.Null));
        }

        // Covers lines 324-343: node with valid references
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
            node1.References.Add(new ReferenceNode(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId(1001, 0))));

            nodeSet.Add(node1);
            nodeSet.Add(node2);

            var result = _nodeTable.Import(nodeSet, null);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        // Covers lines 324-330: skips references with null reference type or target
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
            node.References.Add(new ReferenceNode(
                NodeId.Null,
                false,
                new ExpandedNodeId(new NodeId(1001, 0))));

            nodeSet.Add(node);

            var result = _nodeTable.Import(nodeSet, null);
            Assert.That(result, Has.Count.EqualTo(1));
        }

        // Covers lines 386-414: external references handling
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
            node.References.Add(new ReferenceNode(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId(9999, 0))));

            nodeSet.Add(node);

            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            var result = _nodeTable.Import(nodeSet, externalRefs);

            Assert.That(result, Has.Count.EqualTo(1));
            // External references should be populated with reverse references
            Assert.That(externalRefs.Count, Is.GreaterThanOrEqualTo(0));
        }

        // Covers lines 418-423: reverse references between imported nodes
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

            parent.References.Add(new ReferenceNode(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId(1001, 0))));

            nodeSet.Add(parent);
            nodeSet.Add(child);

            _nodeTable.Import(nodeSet, null);

            // The child should have a reverse Organizes reference to parent
            var reverseRefs = _nodeTable.Find(
                new ExpandedNodeId(new NodeId(1001, 0)),
                ReferenceTypeIds.Organizes,
                true,  // isInverse
                false);
            Assert.That(reverseRefs, Has.Count.EqualTo(1));
        }

        #endregion

        #region Attach

        // Covers lines 507-510: removes duplicate node
        [Test]
        public void AttachRemovesDuplicateNode()
        {
            AttachNode(1, "Original");
            Assert.That(_nodeTable.Count, Is.EqualTo(1));

            var duplicate = CreateNode(1, "Duplicate");
            _nodeTable.Attach(duplicate);

            Assert.That(_nodeTable.Count, Is.EqualTo(1));
            var found = _nodeTable.Find(LocalExpanded(1));
            Assert.That(found, Is.Not.Null);
            Assert.That(found.BrowseName, Is.EqualTo(new QualifiedName("Duplicate", 1)));
        }

        // Covers lines 548-549: adds node to table
        [Test]
        public void AttachAddsNodeToTable()
        {
            var node = CreateNode(1, "TestNode");
            _nodeTable.Attach(node);

            Assert.That(_nodeTable.Count, Is.EqualTo(1));
            Assert.That(_nodeTable.Exists(LocalExpanded(1)), Is.True);
            var found = _nodeTable.Find(LocalExpanded(1));
            Assert.That(found, Is.Not.Null);
            Assert.That(found.BrowseName, Is.EqualTo(new QualifiedName("TestNode", 1)));
        }

        // Covers lines 552-564: adds reverse references to targets
        [Test]
        public void AttachAddsReverseReferencesToTargets()
        {
            AttachNode(2, "Target");
            var sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(2));
            _nodeTable.Attach(sourceNode);

            // Target should now have reverse reference back to source
            var reverseRefs = _nodeTable.Find(
                LocalExpanded(2),
                ReferenceTypeIds.Organizes,
                true, // isInverse
                false);
            Assert.That(reverseRefs, Has.Count.EqualTo(1));
        }

        // Covers lines 552-557: skips reverse references when target not in table
        [Test]
        public void AttachSkipsReverseReferencesForMissingTargets()
        {
            var sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(999)); // target not in table
            _nodeTable.Attach(sourceNode);

            Assert.That(_nodeTable.Count, Is.EqualTo(1));
            Assert.That(_nodeTable.Exists(LocalExpanded(1)), Is.True);
        }

        // Covers lines 560-564: skips reverse references for HasTypeDefinition
        [Test]
        public void AttachSkipsReverseReferencesForTypeDefinitions()
        {
            AttachNode(2, "TypeDef");

            var sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.HasTypeDefinition,
                false,
                LocalExpanded(2));
            _nodeTable.Attach(sourceNode);

            // HasTypeDefinition reverse references should NOT be added
            var reverseRefs = _nodeTable.Find(
                LocalExpanded(2),
                ReferenceTypeIds.HasTypeDefinition,
                true,
                false);
            Assert.That(reverseRefs, Is.Empty);
        }

        // Covers lines 560-564: skips reverse references for HasModellingRule
        [Test]
        public void AttachSkipsReverseReferencesForModellingRule()
        {
            AttachNode(2, "Model");

            var sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.HasModellingRule,
                false,
                LocalExpanded(2));
            _nodeTable.Attach(sourceNode);

            // HasModellingRule reverse references should NOT be added
            var reverseRefs = _nodeTable.Find(
                LocalExpanded(2),
                ReferenceTypeIds.HasModellingRule,
                true,
                false);
            Assert.That(reverseRefs, Is.Empty);
        }

        // Covers line 569: typeTree?.Add(node)
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

        #endregion

        #region Remove

        // Covers lines 578-584: node not found
        [Test]
        public void RemoveReturnsFalseWhenNodeNotFound()
        {
            var result = _nodeTable.Remove(LocalExpanded(999));
            Assert.That(result, Is.False);
        }

        // Covers lines 588-590: cannot remove remote node directly
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

        // Covers lines 594, 624, 626: remove local node with no references
        [Test]
        public void RemoveLocalNodeWithNoReferencesReturnsTrue()
        {
            AttachNode(1, "Lonely");

            var result = _nodeTable.Remove(LocalExpanded(1));
            Assert.That(result, Is.True);
            Assert.That(_nodeTable.Exists(LocalExpanded(1)), Is.False);
            Assert.That(_nodeTable.Count, Is.EqualTo(0));
        }

        // Covers lines 594-621: remove local node removes inverse references
        [Test]
        public void RemoveLocalNodeRemovesInverseReferences()
        {
            AttachNode(2, "Target");

            var sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(2));
            _nodeTable.Attach(sourceNode);

            // Verify reverse reference exists
            var reverseRefsBefore = _nodeTable.Find(
                LocalExpanded(2),
                ReferenceTypeIds.Organizes,
                true,
                false);
            Assert.That(reverseRefsBefore, Has.Count.EqualTo(1));

            // Remove source
            var result = _nodeTable.Remove(LocalExpanded(1));
            Assert.That(result, Is.True);

            // Reverse references should be cleaned up
            var reverseRefsAfter = _nodeTable.Find(
                LocalExpanded(2),
                ReferenceTypeIds.Organizes,
                true,
                false);
            Assert.That(reverseRefsAfter, Is.Empty);
        }

        // Covers lines 598-600: reference target not in table during remove
        [Test]
        public void RemoveLocalNodeWithReferenceToMissingTargetSucceeds()
        {
            var sourceNode = CreateNode(1, "Source");
            sourceNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(999)); // target not in table
            _nodeTable.Attach(sourceNode);

            var result = _nodeTable.Remove(LocalExpanded(1));
            Assert.That(result, Is.True);
            Assert.That(_nodeTable.Exists(LocalExpanded(1)), Is.False);
        }

        // Covers lines 624, 656-664: InternalRemove(ILocalNode)
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

        #endregion

        #region Count and Enumeration

        // Covers line 282: Count with both local and remote nodes
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

        // Covers lines 256-264: enumerator with empty table
        [Test]
        public void EnumeratorOnEmptyTableReturnsNoNodes()
        {
            var allNodes = _nodeTable.ToList();
            Assert.That(allNodes, Is.Empty);
        }

        #endregion

        #region Import(NodeSet) with Remote References

        // Covers lines 346-355: remote node creation during Import(NodeSet)
        // Covers lines 670-678: InternalAdd(RemoteNode)
        [Test]
        public void ImportNodeSetCreatesRemoteNodesForRemoteReferences()
        {
            // Pre-populate target server table so translation yields ServerIndex > 0
            _serverTable.Append("urn:local-server");       // index 0
            _serverTable.Append("http://remote.server/");  // index 1

            var nodeSet = new NodeSet();
            // Set internal server URIs to match (requires InternalsVisibleTo)
            nodeSet.ServerUris = new StringCollection { "urn:local-server", "http://remote.server/" };

            var node = new Node
            {
                NodeId = new NodeId(1000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("LocalNode"),
                DisplayName = new LocalizedText("Local Node")
            };

            // Add reference to a remote target (ServerIndex=1 → "http://remote.server/")
            node.References.Add(new ReferenceNode(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId(999, 0), null, 1u)));

            nodeSet.Add(node);

            var result = _nodeTable.Import(nodeSet, null);

            Assert.That(result, Has.Count.EqualTo(1));
            // Remote node should have been created (1 local + 1 remote)
            Assert.That(_nodeTable.Count, Is.EqualTo(2));
        }

        // Covers lines 380-382: remote target skip in second pass
        [Test]
        public void ImportNodeSetSkipsRemoteTargetsInSecondPass()
        {
            _serverTable.Append("urn:local-server");
            _serverTable.Append("http://remote.server/");

            var nodeSet = new NodeSet();
            nodeSet.ServerUris = new StringCollection { "urn:local-server", "http://remote.server/" };

            var node = new Node
            {
                NodeId = new NodeId(1000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("LocalNode"),
                DisplayName = new LocalizedText("Local Node")
            };

            node.References.Add(new ReferenceNode(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId(888, 0), null, 1u)));

            nodeSet.Add(node);

            // Should not throw; remote targets are skipped in second pass
            var result = _nodeTable.Import(nodeSet, null);
            Assert.That(result, Has.Count.EqualTo(1));
        }

        #endregion

        #region Remove with Remote Nodes

        // Covers lines 605-612: RemoteNode cleanup during Remove
        // Covers lines 684-692: InternalRemove(RemoteNode)
        [Test]
        public void RemoveLocalNodeCleansUpRemoteNodes()
        {
            _serverTable.Append("urn:local-server");
            _serverTable.Append("http://remote.server/");

            var nodeSet = new NodeSet();
            nodeSet.ServerUris = new StringCollection { "urn:local-server", "http://remote.server/" };

            var node = new Node
            {
                NodeId = new NodeId(1000, 0),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("LocalNode"),
                DisplayName = new LocalizedText("Local Node")
            };

            node.References.Add(new ReferenceNode(
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

        // Covers lines 605-612: RemoteNode NOT removed when still referenced
        [Test]
        public void RemoveLocalNodeKeepsRemoteNodeWhenStillReferenced()
        {
            _serverTable.Append("urn:local-server");
            _serverTable.Append("http://remote.server/");

            var nodeSet = new NodeSet();
            nodeSet.ServerUris = new StringCollection { "urn:local-server", "http://remote.server/" };

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
            node1.References.Add(new ReferenceNode(
                ReferenceTypeIds.Organizes, false, remoteTargetId));
            node2.References.Add(new ReferenceNode(
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

        #endregion

        #region Integration Scenarios

        [Test]
        public void AttachFindRemoveFullLifecycle()
        {
            // Attach two connected nodes
            AttachNode(2, "Child");
            var parentNode = CreateNode(1, "Parent");
            parentNode.ReferenceTable.Add(
                ReferenceTypeIds.Organizes,
                false,
                LocalExpanded(2));
            _nodeTable.Attach(parentNode);

            // Verify find by browse name
            var found = _nodeTable.Find(
                LocalExpanded(1),
                ReferenceTypeIds.Organizes,
                false,
                false,
                new QualifiedName("Child", 1));
            Assert.That(found, Is.Not.Null);

            // Verify find list
            var targets = _nodeTable.Find(
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
            parentNode.References.Add(new ReferenceNode(
                ReferenceTypeIds.Organizes,
                false,
                new ExpandedNodeId(new NodeId(2001, 0))));

            nodeSet.Add(parentNode);
            nodeSet.Add(childNode);

            var imported = _nodeTable.Import(nodeSet, null);
            Assert.That(imported, Has.Count.EqualTo(2));

            // Verify forward references
            var children = _nodeTable.Find(
                new ExpandedNodeId(new NodeId(2000, 0)),
                ReferenceTypeIds.Organizes,
                false,
                false);
            Assert.That(children, Has.Count.EqualTo(1));

            // Verify reverse references
            var parents = _nodeTable.Find(
                new ExpandedNodeId(new NodeId(2001, 0)),
                ReferenceTypeIds.Organizes,
                true,
                false);
            Assert.That(parents, Has.Count.EqualTo(1));
        }

        #endregion
    }
}
