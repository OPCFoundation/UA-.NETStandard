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
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.State
{
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeTests
    {
        [Test]
        public void DefaultConstructorInitializesDefaults()
        {
            var node = new Node();
            Assert.That(node, Is.Not.Null);
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Unspecified));
            Assert.That(node.WriteMask, Is.Zero);
            Assert.That(node.UserWriteMask, Is.Zero);
            Assert.That(node.AccessRestrictions, Is.Zero);
            Assert.That(node.References.IsNull, Is.False);
            Assert.That(node.RolePermissions.IsNull, Is.False);
            Assert.That(node.UserRolePermissions.IsNull, Is.False);
        }

        [Test]
        public void PropertiesSetAndGet()
        {
            var node = new Node
            {
                NodeId = new NodeId(7000),
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("TestNode"),
                DisplayName = new LocalizedText("Test Node"),
                Description = new LocalizedText("A test node"),
                WriteMask = 0xFF,
                UserWriteMask = 0x0F,
                AccessRestrictions = 5
            };

            Assert.That(node.NodeId, Is.EqualTo(new NodeId(7000)));
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(node.BrowseName, Is.EqualTo(new QualifiedName("TestNode")));
            Assert.That(node.DisplayName, Is.EqualTo(new LocalizedText("Test Node")));
            Assert.That(node.Description, Is.EqualTo(new LocalizedText("A test node")));
            Assert.That(node.WriteMask, Is.EqualTo(0xFFu));
            Assert.That(node.UserWriteMask, Is.EqualTo(0x0Fu));
            Assert.That(node.AccessRestrictions, Is.EqualTo((ushort)5));
        }

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var node = new Node
            {
                NodeId = new NodeId(7001),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("OrigNode"),
                DisplayName = new LocalizedText("Original Node"),
                Description = new LocalizedText("Description"),
                WriteMask = 10,
                UserWriteMask = 5
            };

            var clone = (Node)node.Clone();
            Assert.That(clone, Is.Not.SameAs(node));
            Assert.That(clone.NodeId, Is.EqualTo(node.NodeId));
            Assert.That(clone.NodeClass, Is.EqualTo(node.NodeClass));
            Assert.That(clone.BrowseName, Is.EqualTo(node.BrowseName));
            Assert.That(clone.DisplayName.Text, Is.EqualTo(node.DisplayName.Text));
            Assert.That(clone.WriteMask, Is.EqualTo(node.WriteMask));
        }

        [Test]
        public void IsEqualReturnsTrueForSameReference()
        {
            var node = new Node { NodeId = new NodeId(7002), NodeClass = NodeClass.Object };
            Assert.That(node.IsEqual(node), Is.True);
        }

        [Test]
        public void IsEqualReturnsTrueForEqualNodes()
        {
            var node1 = new Node
            {
                NodeId = new NodeId(7003),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("EqNode"),
                DisplayName = new LocalizedText("Equal Node"),
                Description = new LocalizedText("Desc"),
                WriteMask = 1,
                UserWriteMask = 2,
                AccessRestrictions = 3
            };
            var node2 = (Node)node1.Clone();
            Assert.That(node1.IsEqual(node2), Is.True);
        }

        [Test]
        public void IsEqualReturnsFalseForNull()
        {
            var node = new Node { NodeId = new NodeId(7004) };
            Assert.That(node.IsEqual(null), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentNodeId()
        {
            var node1 = new Node { NodeId = new NodeId(7005) };
            var node2 = new Node { NodeId = new NodeId(7006) };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentNodeClass()
        {
            var node1 = new Node { NodeId = new NodeId(7007), NodeClass = NodeClass.Object };
            var node2 = new Node { NodeId = new NodeId(7007), NodeClass = NodeClass.Variable };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentBrowseName()
        {
            var node1 = new Node { NodeId = new NodeId(7008), BrowseName = new QualifiedName("A") };
            var node2 = new Node { NodeId = new NodeId(7008), BrowseName = new QualifiedName("B") };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentDisplayName()
        {
            var node1 = new Node { NodeId = new NodeId(7009), DisplayName = new LocalizedText("A") };
            var node2 = new Node { NodeId = new NodeId(7009), DisplayName = new LocalizedText("B") };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentDescription()
        {
            var node1 = new Node { NodeId = new NodeId(7010), Description = new LocalizedText("D1") };
            var node2 = new Node { NodeId = new NodeId(7010), Description = new LocalizedText("D2") };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentWriteMask()
        {
            var node1 = new Node { NodeId = new NodeId(7011), WriteMask = 1 };
            var node2 = new Node { NodeId = new NodeId(7011), WriteMask = 2 };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentUserWriteMask()
        {
            var node1 = new Node { NodeId = new NodeId(7012), UserWriteMask = 1 };
            var node2 = new Node { NodeId = new NodeId(7012), UserWriteMask = 2 };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentAccessRestrictions()
        {
            var node1 = new Node { NodeId = new NodeId(7013), AccessRestrictions = 1 };
            var node2 = new Node { NodeId = new NodeId(7013), AccessRestrictions = 2 };
            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void ToStringWithDisplayName()
        {
            var node = new Node { DisplayName = new LocalizedText("MyDisplayName") };
            Assert.That(node.ToString(), Is.EqualTo("MyDisplayName"));
        }

        [Test]
        public void ToStringFallsToBrowseName()
        {
            var node = new Node { BrowseName = new QualifiedName("MyBrowseName") };
            Assert.That(node.ToString(), Is.EqualTo("MyBrowseName"));
        }

        [Test]
        public void ToStringFallsToNodeClass()
        {
            var node = new Node { NodeClass = NodeClass.Variable };
            Assert.That(node.ToString(), Does.Contain("variable"));
        }

        [Test]
        public void ToStringWithFormatThrows()
        {
            var node = new Node();
            Assert.That(() => node.ToString("G", null), Throws.TypeOf<FormatException>());
        }

        [Test]
        public void HandleProperty()
        {
            var node = new Node();
            object handle = new object();
            node.Handle = handle;
            Assert.That(node.Handle, Is.SameAs(handle));
        }

        [Test]
        public void EncodingIdProperties()
        {
            var node = new Node();
            Assert.That(node.TypeId, Is.EqualTo(DataTypeIds.Node));
            Assert.That(node.BinaryEncodingId, Is.EqualTo(ObjectIds.Node_Encoding_DefaultBinary));
            Assert.That(node.XmlEncodingId, Is.EqualTo(ObjectIds.Node_Encoding_DefaultXml));
            Assert.That(node.JsonEncodingId, Is.EqualTo(ObjectIds.Node_Encoding_DefaultJson));
        }

        [Test]
        public void SupportsAttributeForBaseAttributes()
        {
            var node = new Node();
            Assert.That(node.SupportsAttribute(Attributes.NodeId), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.NodeClass), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.BrowseName), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.DisplayName), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.Description), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.WriteMask), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.UserWriteMask), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.Value), Is.False);
        }

        [Test]
        public void CopyReturnsCorrectSubtypes()
        {
            Assert.That(Node.Copy(null), Is.Null);

            var varNode = new Node { NodeId = new NodeId(7030), NodeClass = NodeClass.Variable };
            Assert.That(Node.Copy(varNode), Is.InstanceOf<VariableNode>());

            var objNode = new Node { NodeId = new NodeId(7031), NodeClass = NodeClass.Object };
            Assert.That(Node.Copy(objNode), Is.InstanceOf<ObjectNode>());

            var mtdNode = new Node { NodeId = new NodeId(7032), NodeClass = NodeClass.Method };
            Assert.That(Node.Copy(mtdNode), Is.InstanceOf<MethodNode>());

            var vwNode = new Node { NodeId = new NodeId(7033), NodeClass = NodeClass.View };
            Assert.That(Node.Copy(vwNode), Is.InstanceOf<ViewNode>());

            var otNode = new Node { NodeId = new NodeId(7034), NodeClass = NodeClass.ObjectType };
            Assert.That(Node.Copy(otNode), Is.InstanceOf<ObjectTypeNode>());

            var vtNode = new Node { NodeId = new NodeId(7035), NodeClass = NodeClass.VariableType };
            Assert.That(Node.Copy(vtNode), Is.InstanceOf<VariableTypeNode>());

            var dtNode = new Node { NodeId = new NodeId(7036), NodeClass = NodeClass.DataType };
            Assert.That(Node.Copy(dtNode), Is.InstanceOf<DataTypeNode>());

            var rtNode = new Node { NodeId = new NodeId(7037), NodeClass = NodeClass.ReferenceType };
            Assert.That(Node.Copy(rtNode), Is.InstanceOf<ReferenceTypeNode>());
        }

        [Test]
        public void CreateCopyReturnsNodeWithNewId()
        {
            var node = new Node { NodeId = new NodeId(7040), NodeClass = NodeClass.Object, BrowseName = new QualifiedName("CC") };
            var newNodeId = new NodeId(7041);
            ILocalNode copy = node.CreateCopy(newNodeId);
            Assert.That(copy, Is.Not.Null);
            Assert.That(copy.NodeId, Is.EqualTo(newNodeId));
        }

        [Test]
        public void DataLockReturnsSelf()
        {
            var node = new Node();
            Assert.That(node.DataLock, Is.SameAs(node));
        }

        [Test]
        public void ReferenceTableLazyInit()
        {
            var node = new Node();
            ReferenceCollection table = node.ReferenceTable;
            Assert.That(table, Is.Not.Null);
        }
    }
}
