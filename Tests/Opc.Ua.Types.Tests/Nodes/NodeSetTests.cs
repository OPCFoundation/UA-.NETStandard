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
    [TestFixture]
    [Category("NodeSet")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeSetTests
    {
        private const string kCustomUri = "http://test.org/custom/";
        private const string kOtherUri = "http://test.org/other/";
        private const string kServerUri = "http://test.org/server/";

        private static NamespaceTable SourceTable(params string[] extra)
        {
            var table = new NamespaceTable();
            foreach (string uri in extra)
            {
                table.Append(uri);
            }
            return table;
        }

        private static ObjectNode CreateObject(uint id, ushort ns = 0, string name = "Obj")
        {
            return new ObjectNode
            {
                NodeId = new NodeId(id, ns),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName(name, ns),
                DisplayName = new LocalizedText(name)
            };
        }

        private static VariableNode CreateVariable(NodeId nodeId, Variant value, NodeId dataType)
        {
            return new VariableNode
            {
                NodeId = nodeId,
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("Var", nodeId.NamespaceIndex),
                DisplayName = new LocalizedText("Var"),
                Value = value,
                DataType = dataType
            };
        }

        [Test]
        public void NewNodeSetIsEmpty()
        {
            var set = new NodeSet();
            Assert.That(set.Count(), Is.Zero);
        }

        [Test]
        public void AddNullNodeThrowsArgumentNullException()
        {
            var set = new NodeSet();
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => set.Add(null!));
            Assert.That(ex.ParamName, Is.EqualTo("node"));
        }

        [Test]
        public void AddNodeWithNullNodeIdThrowsArgumentException()
        {
            var set = new NodeSet();
            var node = new ObjectNode { NodeId = NodeId.Null };
            Assert.Throws<ArgumentException>(() => set.Add(node));
        }

        [Test]
        public void AddDuplicateNodeThrowsArgumentException()
        {
            var set = new NodeSet();
            set.Add(CreateObject(1));
            Assert.Throws<ArgumentException>(() => set.Add(CreateObject(1)));
        }

        [Test]
        public void AddContainsFindReturnStoredNodes()
        {
            var set = new NodeSet();
            Node a = CreateObject(1, 0, "A");
            Node b = CreateObject(2, 0, "B");
            set.Add(a);
            set.Add(b);

            Assert.That(set.Count(), Is.EqualTo(2));
            Assert.That(set.Contains(new NodeId(1u)), Is.True);
            Assert.That(set.Contains(new NodeId(2u)), Is.True);
            Assert.That(set.Find(new NodeId(1u)), Is.SameAs(a));
            Assert.That(set.Find(new NodeId(2u)), Is.SameAs(b));
        }

        [Test]
        public void EnumeratorYieldsAllNodes()
        {
            var set = new NodeSet();
            set.Add(CreateObject(1, 0, "A"));
            set.Add(CreateObject(2, 0, "B"));
            set.Add(CreateObject(3, 0, "C"));

            var ids = new List<NodeId>();
            foreach (Node node in set)
            {
                ids.Add(node.NodeId);
            }

            Assert.That(ids, Is.EquivalentTo([new NodeId(1u), new NodeId(2u), new NodeId(3u)]));
        }

        [Test]
        public void NonGenericEnumeratorYieldsAllNodes()
        {
            var set = new NodeSet();
            set.Add(CreateObject(1, 0, "A"));
            set.Add(CreateObject(2, 0, "B"));

            var ids = new List<NodeId>();
            foreach (object node in (IEnumerable)set)
            {
                ids.Add(((Node)node).NodeId);
            }

            Assert.That(ids, Is.EquivalentTo([new NodeId(1u), new NodeId(2u)]));
        }

        [Test]
        public void RemoveExistingNodeReturnsTrue()
        {
            var set = new NodeSet();
            set.Add(CreateObject(1));
            Assert.That(set.Remove(new NodeId(1u)), Is.True);
            Assert.That(set.Contains(new NodeId(1u)), Is.False);
            Assert.That(set.Count(), Is.Zero);
        }

        [Test]
        public void RemoveAbsentNodeReturnsFalse()
        {
            var set = new NodeSet();
            Assert.That(set.Remove(new NodeId(99u)), Is.False);
        }

        [Test]
        public void ContainsAbsentNodeReturnsFalse()
        {
            var set = new NodeSet();
            Assert.That(set.Contains(new NodeId(5u)), Is.False);
        }

        [Test]
        public void FindAbsentNodeReturnsNull()
        {
            var set = new NodeSet();
            Assert.That(set.Find(new NodeId(5u)), Is.Null);
        }

        [Test]
        public void FindWithNamespaceTableNullNodeIdThrowsArgumentNullException()
        {
            var set = new NodeSet();
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => set.Find(NodeId.Null, new NamespaceTable()));
            Assert.That(ex.ParamName, Is.EqualTo("nodeId"));
        }

        [Test]
        public void FindWithNamespaceTableNullTableThrowsArgumentNullException()
        {
            var set = new NodeSet();
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => set.Find(new NodeId(1u), null!));
            Assert.That(ex.ParamName, Is.EqualTo("namespaceUris"));
        }

        [Test]
        public void FindWithNamespaceTableUnknownIndexReturnsNull()
        {
            var set = new NodeSet();
            set.Add(CreateObject(1));
            // Namespace index 5 is not present in the supplied table.
            Assert.That(set.Find(new NodeId(1u, 5), new NamespaceTable()), Is.Null);
        }

        [Test]
        public void FindWithNamespaceTableUnknownUriReturnsNull()
        {
            var set = new NodeSet();
            set.Add(CreateObject(1, 1));
            NamespaceTable source = SourceTable(kCustomUri); // index 1 -> custom uri
            // The nodeset table does not contain the custom uri, so lookup fails.
            Assert.That(set.Find(new NodeId(1u, 1), source), Is.Null);
        }

        [Test]
        public void FindWithNamespaceTableTranslatesAndReturnsNode()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kCustomUri }.ToArrayOf();
            Node node = CreateObject(7, 1, "Boiler");
            set.Add(node);

            NamespaceTable source = SourceTable(kOtherUri, kCustomUri); // index 2 -> custom uri
            Node found = set.Find(new NodeId(7u, 2), source);

            Assert.That(found, Is.SameAs(node));
        }

        [Test]
        public void ExportNodeIdAppendsUriAndRemapsIndex()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kCustomUri); // index 1 -> custom uri
            NodeId exported = set.Export(new NodeId("Boiler", 1), source);

            Assert.That(exported, Is.EqualTo(new NodeId("Boiler", 1)));
            Assert.That(set.NamespaceUris.ToArray(), Is.EqualTo([Namespaces.OpcUa, kCustomUri]));
        }

        [Test]
        public void ExportNodeIdInDefaultNamespaceIsUnchanged()
        {
            var set = new NodeSet();
            NodeId exported = set.Export(new NodeId(42u), new NamespaceTable());
            Assert.That(exported, Is.EqualTo(new NodeId(42u)));
            Assert.That(exported.NamespaceIndex, Is.Zero);
        }

        [Test]
        public void ImportNodeIdRemapsIntoCallerTable()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kOtherUri, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            NodeId imported = set.Import(new NodeId("Boiler", 2), callerTable);

            Assert.That(imported, Is.EqualTo(new NodeId("Boiler", 1)));
            Assert.That(callerTable.GetString(1), Is.EqualTo(kCustomUri));
        }

        [Test]
        public void ExportExpandedNodeIdNonAbsoluteRemapsNamespace()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kCustomUri);
            var servers = new StringTable();

            ExpandedNodeId exported = set.Export(
                new ExpandedNodeId(new NodeId("Boiler", 1)), source, servers);

            Assert.That(exported.NamespaceIndex, Is.EqualTo(1));
            Assert.That(exported.InnerNodeId, Is.EqualTo(new NodeId("Boiler", 1)));
            Assert.That(exported.ServerIndex, Is.Zero);
            Assert.That(set.NamespaceUris.ToArray(), Is.EqualTo([Namespaces.OpcUa, kCustomUri]));
        }

        [Test]
        public void ExportExpandedNodeIdWithNamespaceUriRemaps()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kCustomUri);
            var servers = new StringTable();

            ExpandedNodeId exported = set.Export(
                new ExpandedNodeId(new NodeId(5u), kCustomUri), source, servers);

            Assert.That(exported.NamespaceIndex, Is.EqualTo(1));
            Assert.That(exported.ServerIndex, Is.Zero);
            Assert.That(set.NamespaceUris.ToArray(), Is.EqualTo([Namespaces.OpcUa, kCustomUri]));
        }

        [Test]
        public void ExportExpandedNodeIdWithServerIndexRemapsServer()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kCustomUri);
            var servers = new StringTable();
            servers.Append("http://local/");   // index 0
            servers.Append(kServerUri);         // index 1

            var absolute = new ExpandedNodeId(new NodeId(9u), kCustomUri, 1u);
            ExpandedNodeId exported = set.Export(absolute, source, servers);

            Assert.That(exported.ServerIndex, Is.Zero);
            Assert.That(exported.NamespaceUri, Is.EqualTo(kCustomUri));
            Assert.That(set.ServerUris.ToArray(), Is.EqualTo([kServerUri]));
        }

        [Test]
        public void ImportExpandedNodeIdRemapsIntoCallerTables()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            var callerServers = new StringTable();

            ExpandedNodeId imported = set.Import(
                new ExpandedNodeId(new NodeId("Node", 1)), callerTable, callerServers);

            Assert.That(imported.NamespaceIndex, Is.EqualTo(1));
            Assert.That(imported.InnerNodeId, Is.EqualTo(new NodeId("Node", 1)));
            Assert.That(callerTable.GetString(1), Is.EqualTo(kCustomUri));
        }

        [Test]
        public void AddReferenceTranslatesAndAppendsReference()
        {
            var set = new NodeSet();
            Node node = CreateObject(1);
            NamespaceTable source = SourceTable(kCustomUri);
            var servers = new StringTable();

            var reference = new ReferenceNode(
                new NodeId(47u), false, new ExpandedNodeId(new NodeId("Target", 1)));

            set.AddReference(node, reference, source, servers);

            Assert.That(node.References.Count, Is.EqualTo(1));
            ReferenceNode added = node.References[0];
            Assert.That(added.ReferenceTypeId, Is.EqualTo(new NodeId(47u)));
            Assert.That(added.IsInverse, Is.False);
            Assert.That(added.TargetId.NamespaceIndex, Is.EqualTo(1));
            Assert.That(added.TargetId.InnerNodeId, Is.EqualTo(new NodeId("Target", 1)));
            Assert.That(set.NamespaceUris.ToArray(), Is.EqualTo([Namespaces.OpcUa, kCustomUri]));
        }

        [Test]
        public void AddLocalNodeCopiesAttributesAndReferences()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kCustomUri);
            var servers = new StringTable();

            Node original = CreateObject(3, 1, "Motor");
            original.ReferenceTable.Add(new NodeId(47u), false, new ExpandedNodeId(new NodeId("T", 1)));

            Node exported = set.Add(original, source, servers);

            Assert.That(exported, Is.InstanceOf<ObjectNode>());
            Assert.That(exported.NodeId, Is.EqualTo(new NodeId(3u, 1)));
            Assert.That(exported.BrowseName, Is.EqualTo(new QualifiedName("Motor", 1)));
            Assert.That(exported.References.Count, Is.EqualTo(1));
            Assert.That(exported.References[0].TargetId.InnerNodeId, Is.EqualTo(new NodeId("T", 1)));
            Assert.That(set.Contains(new NodeId(3u, 1)), Is.True);
            Assert.That(set.NamespaceUris.ToArray(), Is.EqualTo([Namespaces.OpcUa, kCustomUri]));
        }

        [Test]
        public void AddLocalVariableWithScalarNodeIdValueTranslatesValue()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kOtherUri, kCustomUri); // custom at index 2

            var variable = CreateVariable(
                new NodeId(10u),
                new Variant(new NodeId("Sensor", 2)),
                new NodeId(11u, 2));

            var exported = (VariableNode)set.Add(variable, source, new StringTable());

            Assert.That(exported.Value.GetNodeId(), Is.EqualTo(new NodeId("Sensor", 1)));
            Assert.That(exported.DataType, Is.EqualTo(new NodeId(11u, 1)));
            Assert.That(set.NamespaceUris.ToArray(), Is.EqualTo([Namespaces.OpcUa, kCustomUri]));
        }

        [Test]
        public void AddLocalVariableWithScalarExpandedNodeIdValueTranslatesValue()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kOtherUri, kCustomUri);

            var variable = CreateVariable(
                new NodeId(12u),
                new Variant(new ExpandedNodeId(new NodeId("E", 2))),
                new NodeId(13u));

            var exported = (VariableNode)set.Add(variable, source, new StringTable());

            ExpandedNodeId value = exported.Value.GetExpandedNodeId();
            Assert.That(value.NamespaceIndex, Is.EqualTo(1));
            Assert.That(value.InnerNodeId, Is.EqualTo(new NodeId("E", 1)));
        }

        [Test]
        public void AddLocalVariableWithScalarQualifiedNameValueTranslatesValue()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kOtherUri, kCustomUri);

            var variable = CreateVariable(
                new NodeId(14u),
                new Variant(new QualifiedName("Temp", 2)),
                new NodeId(15u));

            var exported = (VariableNode)set.Add(variable, source, new StringTable());

            Assert.That(exported.Value.GetQualifiedName(), Is.EqualTo(new QualifiedName("Temp", 1)));
        }

        [Test]
        public void AddLocalVariableWithScalarExtensionObjectValuePreservesArgument()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kCustomUri);

            var argument = new Argument { Name = "arg", DataType = new NodeId(20u), ValueRank = -1 };
            var variable = CreateVariable(
                new NodeId(16u),
                new Variant(new ExtensionObject(argument)),
                new NodeId(17u));

            var exported = (VariableNode)set.Add(variable, source, new StringTable());

            Assert.That(exported.Value.GetExtensionObject().TryGetValue(out Argument result), Is.True);
            Assert.That(result.Name, Is.EqualTo("arg"));
            Assert.That(result.DataType, Is.EqualTo(new NodeId(20u)));
        }

        [Test]
        public void AddLocalVariableWithScalarInt32ValueIsUnchanged()
        {
            var set = new NodeSet();
            var variable = CreateVariable(new NodeId(18u), new Variant(42), new NodeId(19u));

            var exported = (VariableNode)set.Add(variable, new NamespaceTable(), new StringTable());

            Assert.That(exported.Value.GetInt32(), Is.EqualTo(42));
        }

        [Test]
        public void AddLocalVariableWithNodeIdArrayValueTranslatesEachElement()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kOtherUri, kCustomUri);

            var value = new Variant(new[] { new NodeId("A", 2), new NodeId("B", 2) }.ToArrayOf());
            var variable = CreateVariable(new NodeId(21u), value, new NodeId(22u));

            var exported = (VariableNode)set.Add(variable, source, new StringTable());

            ArrayOf<NodeId> result = exported.Value.GetNodeIdArray();
            Assert.That(result, Is.EqualTo(new[] { new NodeId("A", 1), new NodeId("B", 1) }.ToArrayOf()));
        }

        [Test]
        public void AddLocalVariableWithExpandedNodeIdArrayValueTranslatesEachElement()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kOtherUri, kCustomUri);

            var value = new Variant(new[]
            {
                new ExpandedNodeId(new NodeId("A", 2)),
                new ExpandedNodeId(new NodeId("B", 2))
            }.ToArrayOf());
            var variable = CreateVariable(new NodeId(23u), value, new NodeId(24u));

            var exported = (VariableNode)set.Add(variable, source, new StringTable());

            ArrayOf<ExpandedNodeId> result = exported.Value.GetExpandedNodeIdArray();
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].NamespaceIndex, Is.EqualTo(1));
            Assert.That(result[1].InnerNodeId, Is.EqualTo(new NodeId("B", 1)));
        }

        [Test]
        public void AddLocalVariableWithQualifiedNameArrayValueTranslatesEachElement()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kOtherUri, kCustomUri);

            var value = new Variant(new[]
            {
                new QualifiedName("A", 2),
                new QualifiedName("B", 2)
            }.ToArrayOf());
            var variable = CreateVariable(new NodeId(25u), value, new NodeId(26u));

            var exported = (VariableNode)set.Add(variable, source, new StringTable());

            ArrayOf<QualifiedName> result = exported.Value.GetQualifiedNameArray();
            Assert.That(result, Is.EqualTo(new[] { new QualifiedName("A", 1), new QualifiedName("B", 1) }.ToArrayOf()));
        }

        [Test]
        public void AddLocalVariableWithExtensionObjectArrayValuePreservesArguments()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kCustomUri);

            var value = new Variant(new[]
            {
                new ExtensionObject(new Argument { Name = "a", DataType = new NodeId(1u), ValueRank = -1 }),
                new ExtensionObject(new Argument { Name = "b", DataType = new NodeId(2u), ValueRank = -1 })
            }.ToArrayOf());
            var variable = CreateVariable(new NodeId(27u), value, new NodeId(28u));

            var exported = (VariableNode)set.Add(variable, source, new StringTable());

            ArrayOf<ExtensionObject> result = exported.Value.GetExtensionObjectArray();
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].TryGetValue(out Argument first), Is.True);
            Assert.That(first.Name, Is.EqualTo("a"));
        }

        [Test]
        public void AddLocalVariableWithNodeIdMatrixValueTranslatesEachElement()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kOtherUri, kCustomUri);

            var matrix = new NodeId[,] { { new NodeId("A", 2), new NodeId("B", 2) } }.ToMatrixOf();
            var variable = CreateVariable(new NodeId(29u), new Variant(matrix), new NodeId(30u));

            var exported = (VariableNode)set.Add(variable, source, new StringTable());

            MatrixOf<NodeId> result = exported.Value.GetNodeIdMatrix();
            int[] expectedDimensions = [1, 2];
            Assert.That(result.Dimensions, Is.EqualTo(expectedDimensions));
            Assert.That(result.ToArrayOf(), Is.EqualTo(new[] { new NodeId("A", 1), new NodeId("B", 1) }.ToArrayOf()));
        }

        [Test]
        public void AddLocalVariableWithExpandedNodeIdMatrixValueTranslatesEachElement()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kOtherUri, kCustomUri);

            var matrix = new ExpandedNodeId[,]
            {
                { new ExpandedNodeId(new NodeId("A", 2)), new ExpandedNodeId(new NodeId("B", 2)) }
            }.ToMatrixOf();
            var variable = CreateVariable(new NodeId(31u), new Variant(matrix), new NodeId(32u));

            var exported = (VariableNode)set.Add(variable, source, new StringTable());

            MatrixOf<ExpandedNodeId> result = exported.Value.GetExpandedNodeIdMatrix();
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.ToArrayOf()[0].NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void AddLocalVariableWithQualifiedNameMatrixValueTranslatesEachElement()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kOtherUri, kCustomUri);

            var matrix = new QualifiedName[,] { { new QualifiedName("A", 2), new QualifiedName("B", 2) } }.ToMatrixOf();
            var variable = CreateVariable(new NodeId(33u), new Variant(matrix), new NodeId(34u));

            var exported = (VariableNode)set.Add(variable, source, new StringTable());

            MatrixOf<QualifiedName> result = exported.Value.GetQualifiedNameMatrix();
            Assert.That(result.ToArrayOf(),
                Is.EqualTo(new[] { new QualifiedName("A", 1), new QualifiedName("B", 1) }.ToArrayOf()));
        }

        [Test]
        public void AddLocalVariableWithExtensionObjectMatrixValuePreservesArguments()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kCustomUri);

            var matrix = new ExtensionObject[,]
            {
                {
                    new ExtensionObject(new Argument { Name = "a", DataType = new NodeId(1u), ValueRank = -1 }),
                    new ExtensionObject(new Argument { Name = "b", DataType = new NodeId(2u), ValueRank = -1 })
                }
            }.ToMatrixOf();
            var variable = CreateVariable(new NodeId(35u), new Variant(matrix), new NodeId(36u));

            var exported = (VariableNode)set.Add(variable, source, new StringTable());

            MatrixOf<ExtensionObject> result = exported.Value.GetExtensionObjectMatrix();
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.ToArrayOf()[1].TryGetValue(out Argument second), Is.True);
            Assert.That(second.Name, Is.EqualTo("b"));
        }

        [Test]
        public void AddLocalVariableTypeTranslatesValueAndDataType()
        {
            var set = new NodeSet();
            NamespaceTable source = SourceTable(kOtherUri, kCustomUri);

            var variableType = new VariableTypeNode
            {
                NodeId = new NodeId(40u),
                NodeClass = NodeClass.VariableType,
                BrowseName = new QualifiedName("VarType"),
                DisplayName = new LocalizedText("VarType"),
                Value = new Variant(new NodeId("Def", 2)),
                DataType = new NodeId(41u, 2)
            };

            var exported = (VariableTypeNode)set.Add(variableType, source, new StringTable());

            Assert.That(exported.Value.GetNodeId(), Is.EqualTo(new NodeId("Def", 1)));
            Assert.That(exported.DataType, Is.EqualTo(new NodeId(41u, 1)));
        }

        [Test]
        public void CopyVariableWithScalarNodeIdValueRemapsIntoCallerTable()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kOtherUri, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            var variable = CreateVariable(
                new NodeId(50u, 2),
                new Variant(new NodeId("V", 2)),
                new NodeId(51u, 2));

            var copy = (VariableNode)set.Copy(variable, callerTable, new StringTable());

            Assert.That(copy.NodeId, Is.EqualTo(new NodeId(50u, 1)));
            Assert.That(copy.Value.GetNodeId(), Is.EqualTo(new NodeId("V", 1)));
            Assert.That(copy.DataType, Is.EqualTo(new NodeId(51u, 1)));
            Assert.That(callerTable.GetString(1), Is.EqualTo(kCustomUri));
        }

        [Test]
        public void CopyVariableWithScalarExpandedNodeIdValueRemaps()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kOtherUri, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            var variable = CreateVariable(
                new NodeId(52u, 2),
                new Variant(new ExpandedNodeId(new NodeId("E", 2))),
                new NodeId(53u, 2));

            var copy = (VariableNode)set.Copy(variable, callerTable, new StringTable());

            Assert.That(copy.Value.GetExpandedNodeId().NamespaceIndex, Is.EqualTo(1));
            Assert.That(copy.Value.GetExpandedNodeId().InnerNodeId, Is.EqualTo(new NodeId("E", 1)));
        }

        [Test]
        public void CopyVariableWithScalarQualifiedNameValueRemaps()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kOtherUri, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            var variable = CreateVariable(
                new NodeId(54u, 2),
                new Variant(new QualifiedName("Q", 2)),
                new NodeId(55u, 2));

            var copy = (VariableNode)set.Copy(variable, callerTable, new StringTable());

            Assert.That(copy.Value.GetQualifiedName(), Is.EqualTo(new QualifiedName("Q", 1)));
        }

        [Test]
        public void CopyVariableWithScalarExtensionObjectValuePreservesArgument()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            var argument = new Argument { Name = "arg", DataType = new NodeId(60u), ValueRank = -1 };
            var variable = CreateVariable(
                new NodeId(56u, 1),
                new Variant(new ExtensionObject(argument)),
                new NodeId(57u, 1));

            var copy = (VariableNode)set.Copy(variable, callerTable, new StringTable());

            Assert.That(copy.Value.GetExtensionObject().TryGetValue(out Argument result), Is.True);
            Assert.That(result.Name, Is.EqualTo("arg"));
        }

        [Test]
        public void CopyVariableWithNodeIdArrayValueRemaps()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kOtherUri, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            var value = new Variant(new[] { new NodeId("A", 2), new NodeId("B", 2) }.ToArrayOf());
            var variable = CreateVariable(new NodeId(58u, 2), value, new NodeId(59u, 2));

            var copy = (VariableNode)set.Copy(variable, callerTable, new StringTable());

            Assert.That(copy.Value.GetNodeIdArray(),
                Is.EqualTo(new[] { new NodeId("A", 1), new NodeId("B", 1) }.ToArrayOf()));
        }

        [Test]
        public void CopyVariableWithExpandedNodeIdArrayValueRemaps()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kOtherUri, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            var value = new Variant(new[]
            {
                new ExpandedNodeId(new NodeId("A", 2)),
                new ExpandedNodeId(new NodeId("B", 2))
            }.ToArrayOf());
            var variable = CreateVariable(new NodeId(61u, 2), value, new NodeId(62u, 2));

            var copy = (VariableNode)set.Copy(variable, callerTable, new StringTable());

            Assert.That(copy.Value.GetExpandedNodeIdArray()[0].NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void CopyVariableWithQualifiedNameArrayValueRemaps()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kOtherUri, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            var value = new Variant(new[] { new QualifiedName("A", 2), new QualifiedName("B", 2) }.ToArrayOf());
            var variable = CreateVariable(new NodeId(63u, 2), value, new NodeId(64u, 2));

            var copy = (VariableNode)set.Copy(variable, callerTable, new StringTable());

            Assert.That(copy.Value.GetQualifiedNameArray(),
                Is.EqualTo(new[] { new QualifiedName("A", 1), new QualifiedName("B", 1) }.ToArrayOf()));
        }

        [Test]
        public void CopyVariableWithExtensionObjectArrayValuePreservesArguments()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            var value = new Variant(new[]
            {
                new ExtensionObject(new Argument { Name = "a", DataType = new NodeId(1u), ValueRank = -1 }),
                new ExtensionObject(new Argument { Name = "b", DataType = new NodeId(2u), ValueRank = -1 })
            }.ToArrayOf());
            var variable = CreateVariable(new NodeId(65u, 1), value, new NodeId(66u, 1));

            var copy = (VariableNode)set.Copy(variable, callerTable, new StringTable());

            Assert.That(copy.Value.GetExtensionObjectArray().Count, Is.EqualTo(2));
        }

        [Test]
        public void CopyVariableWithNodeIdMatrixValueRemaps()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kOtherUri, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            var matrix = new NodeId[,] { { new NodeId("A", 2), new NodeId("B", 2) } }.ToMatrixOf();
            var variable = CreateVariable(new NodeId(67u, 2), new Variant(matrix), new NodeId(68u, 2));

            var copy = (VariableNode)set.Copy(variable, callerTable, new StringTable());

            Assert.That(copy.Value.GetNodeIdMatrix().ToArrayOf(),
                Is.EqualTo(new[] { new NodeId("A", 1), new NodeId("B", 1) }.ToArrayOf()));
        }

        [Test]
        public void CopyVariableWithExpandedNodeIdMatrixValueRemaps()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kOtherUri, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            var matrix = new ExpandedNodeId[,]
            {
                { new ExpandedNodeId(new NodeId("A", 2)), new ExpandedNodeId(new NodeId("B", 2)) }
            }.ToMatrixOf();
            var variable = CreateVariable(new NodeId(69u, 2), new Variant(matrix), new NodeId(70u, 2));

            var copy = (VariableNode)set.Copy(variable, callerTable, new StringTable());

            Assert.That(copy.Value.GetExpandedNodeIdMatrix().ToArrayOf()[0].NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void CopyVariableWithQualifiedNameMatrixValueRemaps()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kOtherUri, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            var matrix = new QualifiedName[,] { { new QualifiedName("A", 2), new QualifiedName("B", 2) } }.ToMatrixOf();
            var variable = CreateVariable(new NodeId(71u, 2), new Variant(matrix), new NodeId(72u, 2));

            var copy = (VariableNode)set.Copy(variable, callerTable, new StringTable());

            Assert.That(copy.Value.GetQualifiedNameMatrix().ToArrayOf(),
                Is.EqualTo(new[] { new QualifiedName("A", 1), new QualifiedName("B", 1) }.ToArrayOf()));
        }

        [Test]
        public void CopyVariableWithExtensionObjectMatrixValuePreservesArguments()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            var matrix = new ExtensionObject[,]
            {
                {
                    new ExtensionObject(new Argument { Name = "a", DataType = new NodeId(1u), ValueRank = -1 }),
                    new ExtensionObject(new Argument { Name = "b", DataType = new NodeId(2u), ValueRank = -1 })
                }
            }.ToMatrixOf();
            var variable = CreateVariable(new NodeId(73u, 1), new Variant(matrix), new NodeId(74u, 1));

            var copy = (VariableNode)set.Copy(variable, callerTable, new StringTable());

            Assert.That(copy.Value.GetExtensionObjectMatrix().Count, Is.EqualTo(2));
        }

        [Test]
        public void CopyVariableWithReferencesTranslatesTargets()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kCustomUri }.ToArrayOf();

            var callerTable = new NamespaceTable();
            var variable = CreateVariable(new NodeId(75u, 1), new Variant(1), new NodeId(76u));
            variable.References = new[]
            {
                new ReferenceNode(new NodeId(47u), false, new ExpandedNodeId(new NodeId("Tgt", 1)))
            }.ToArrayOf();

            var copy = (VariableNode)set.Copy(variable, callerTable, new StringTable());

            Assert.That(copy.References.Count, Is.EqualTo(1));
            Assert.That(copy.References[0].TargetId.InnerNodeId, Is.EqualTo(new NodeId("Tgt", 1)));
            Assert.That(copy.References[0].TargetId.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void NamespaceUrisRoundTripThroughProperty()
        {
            var set = new NodeSet();
            set.NamespaceUris = new[] { Namespaces.OpcUa, kCustomUri, kOtherUri }.ToArrayOf();
            Assert.That(set.NamespaceUris.ToArray(),
                Is.EqualTo([Namespaces.OpcUa, kCustomUri, kOtherUri]));
        }

        [Test]
        public void NamespaceUrisNullResetsToDefaultTable()
        {
            var set = new NodeSet();
            set.NamespaceUris = ArrayOf.Null<string>();
            Assert.That(set.NamespaceUris.ToArray(), Is.EqualTo([Namespaces.OpcUa]));
        }

        [Test]
        public void ServerUrisRoundTripThroughProperty()
        {
            var set = new NodeSet();
            string[] uris = ["http://a/", "http://b/"];
            set.ServerUris = uris.ToArrayOf();
            Assert.That(set.ServerUris.ToArray(), Is.EqualTo(uris));
        }

        [Test]
        public void ServerUrisNullResetsToEmptyTable()
        {
            var set = new NodeSet();
            string[] uris = ["http://a/"];
            set.ServerUris = uris.ToArrayOf();
            set.ServerUris = ArrayOf.Null<string>();
            Assert.That(set.ServerUris.ToArray(), Is.Empty);
        }

        [Test]
        public void NodesRoundTripThroughProperty()
        {
            var set = new NodeSet();
            Node a = CreateObject(1, 0, "A");
            Node b = CreateObject(2, 0, "B");
            set.Nodes = new[] { a, b }.ToArrayOf();

            Assert.That(set.Contains(new NodeId(1u)), Is.True);
            Assert.That(set.Contains(new NodeId(2u)), Is.True);
            Assert.That(set.Nodes.Count, Is.EqualTo(2));
        }
    }
}
