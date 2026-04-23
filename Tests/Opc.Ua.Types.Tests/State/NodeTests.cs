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
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Tests;

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
            object handle = new();
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

        [Test]
        public void ReadNodeIdAttribute()
        {
            var node = new Node { NodeId = new NodeId(8001) };
            var dataValue = new DataValue();

            ServiceResult result = node.Read(null, Attributes.NodeId, dataValue);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(dataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That((NodeId)dataValue.WrappedValue, Is.EqualTo(new NodeId(8001)));
        }

        [Test]
        public void ReadNodeClassAttribute()
        {
            var node = new Node { NodeClass = NodeClass.Variable };
            var dataValue = new DataValue();

            ServiceResult result = node.Read(null, Attributes.NodeClass, dataValue);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That((NodeClass)(int)dataValue.WrappedValue, Is.EqualTo(NodeClass.Variable));
        }

        [Test]
        public void ReadBrowseNameAttribute()
        {
            var node = new Node { BrowseName = new QualifiedName("ReadTest") };
            var dataValue = new DataValue();

            ServiceResult result = node.Read(null, Attributes.BrowseName, dataValue);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That((QualifiedName)dataValue.WrappedValue, Is.EqualTo(new QualifiedName("ReadTest")));
        }

        [Test]
        public void ReadDisplayNameAttribute()
        {
            var node = new Node { DisplayName = new LocalizedText("Display Read") };
            var dataValue = new DataValue();

            ServiceResult result = node.Read(null, Attributes.DisplayName, dataValue);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That((LocalizedText)dataValue.WrappedValue, Is.EqualTo(new LocalizedText("Display Read")));
        }

        [Test]
        public void ReadDescriptionAttribute()
        {
            var node = new Node { Description = new LocalizedText("Desc Read") };
            var dataValue = new DataValue();

            ServiceResult result = node.Read(null, Attributes.Description, dataValue);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That((LocalizedText)dataValue.WrappedValue, Is.EqualTo(new LocalizedText("Desc Read")));
        }

        [Test]
        public void ReadWriteMaskAttribute()
        {
            var node = new Node { WriteMask = 0xAB };
            var dataValue = new DataValue();

            ServiceResult result = node.Read(null, Attributes.WriteMask, dataValue);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That((uint)dataValue.WrappedValue, Is.EqualTo(0xABu));
        }

        [Test]
        public void ReadUserWriteMaskAttribute()
        {
            var node = new Node { UserWriteMask = 0xCD };
            var dataValue = new DataValue();

            ServiceResult result = node.Read(null, Attributes.UserWriteMask, dataValue);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That((uint)dataValue.WrappedValue, Is.EqualTo(0xCDu));
        }

        [Test]
        public void ReadAccessRestrictionsAttribute()
        {
            var node = new Node { AccessRestrictions = 7 };
            var dataValue = new DataValue();

            ServiceResult result = node.Read(null, Attributes.AccessRestrictions, dataValue);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That((ushort)dataValue.WrappedValue, Is.EqualTo((ushort)7));
        }

        [Test]
        public void ReadRolePermissionsAttribute()
        {
            var node = new Node();
            var dataValue = new DataValue();

            ServiceResult result = node.Read(null, Attributes.RolePermissions, dataValue);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
        }

        [Test]
        public void ReadUserRolePermissionsAttribute()
        {
            var node = new Node();
            var dataValue = new DataValue();

            ServiceResult result = node.Read(null, Attributes.UserRolePermissions, dataValue);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
        }

        [Test]
        public void ReadUnsupportedAttributeReturnsBadStatus()
        {
            var node = new Node();
            var dataValue = new DataValue();

            ServiceResult result = node.Read(null, Attributes.Value, dataValue);

            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
        }

        [Test]
        public void WriteNodeIdReturnsNotWritable()
        {
            var node = new Node { NodeId = new NodeId(8100) };
            var dataValue = new DataValue { WrappedValue = new NodeId(8200) };

            ServiceResult result = node.Write(Attributes.NodeId, dataValue);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
        }

        [Test]
        public void WriteNodeClassReturnsNotWritable()
        {
            var node = new Node { NodeClass = NodeClass.Object };
            var dataValue = new DataValue { WrappedValue = Variant.From(NodeClass.Variable) };

            ServiceResult result = node.Write(Attributes.NodeClass, dataValue);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
        }

        [Test]
        public void WriteUnsupportedAttributeReturnsBadStatus()
        {
            var node = new Node();
            var dataValue = new DataValue { WrappedValue = 42 };

            ServiceResult result = node.Write(Attributes.Value, dataValue);

            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
        }

        [Test]
        public void WriteWithTypeMismatchReturnsBadTypeMismatch()
        {
            var node = new Node();
            var dataValue = new DataValue { WrappedValue = "wrong type" };

            ServiceResult result = node.Write(Attributes.WriteMask, dataValue);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void EncodeDecodeRoundTrip()
        {
            var original = new Node
            {
                NodeId = new NodeId(8300),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("RoundTrip"),
                DisplayName = new LocalizedText("Round Trip Node"),
                Description = new LocalizedText("Round trip test"),
                WriteMask = 42,
                UserWriteMask = 7,
                AccessRestrictions = 3
            };

            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetryContext);

            byte[] encoded;
            using (var encoder = new BinaryEncoder(messageContext))
            {
                original.Encode(encoder);
                encoded = encoder.CloseAndReturnBuffer();
            }

            var decoded = new Node();
            using (var decoder = new BinaryDecoder(encoded, messageContext))
            {
                decoded.Decode(decoder);
            }

            Assert.That(decoded.NodeId, Is.EqualTo(original.NodeId));
            Assert.That(decoded.NodeClass, Is.EqualTo(original.NodeClass));
            Assert.That(decoded.BrowseName, Is.EqualTo(original.BrowseName));
            Assert.That(decoded.DisplayName, Is.EqualTo(original.DisplayName));
            Assert.That(decoded.Description, Is.EqualTo(original.Description));
            Assert.That(decoded.WriteMask, Is.EqualTo(original.WriteMask));
            Assert.That(decoded.UserWriteMask, Is.EqualTo(original.UserWriteMask));
            Assert.That(decoded.AccessRestrictions, Is.EqualTo(original.AccessRestrictions));
        }

        [Test]
        public void CopyConstructorFromReferenceDescription()
        {
            var reference = new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(8400),
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("RefCopy"),
                DisplayName = new LocalizedText("Ref Copy Node")
            };

            var node = new Node(reference);

            Assert.That(node.NodeId, Is.EqualTo(new NodeId(8400)));
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(node.BrowseName, Is.EqualTo(new QualifiedName("RefCopy")));
            Assert.That(node.DisplayName, Is.EqualTo(new LocalizedText("Ref Copy Node")));
        }

        [Test]
        public void CopyConstructorFromILocalNode()
        {
            var source = new Node
            {
                NodeId = new NodeId(8500),
                NodeClass = NodeClass.Method,
                BrowseName = new QualifiedName("SourceNode"),
                DisplayName = new LocalizedText("Source"),
                Description = new LocalizedText("Source Desc"),
                WriteMask = 10,
                UserWriteMask = 5
            };

#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var copy = new Node((ILocalNode)source);
#pragma warning restore IDE0004 // Remove Unnecessary Cast

            Assert.That(copy.NodeId, Is.EqualTo(source.NodeId));
            Assert.That(copy.NodeClass, Is.EqualTo(source.NodeClass));
            Assert.That(copy.BrowseName, Is.EqualTo(source.BrowseName));
            Assert.That(copy.DisplayName, Is.EqualTo(source.DisplayName));
            Assert.That(copy.Description, Is.EqualTo(source.Description));
            Assert.That(copy.WriteMask, Is.EqualTo(source.WriteMask));
            Assert.That(copy.UserWriteMask, Is.EqualTo(source.UserWriteMask));
        }

        [Test]
        public void CopyConstructorFromNullILocalNode()
        {
            var node = new Node((ILocalNode)null);

            Assert.That(node.NodeId, Is.Default);
            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Unspecified));
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentRolePermissions()
        {
            var rp1 = new RolePermissionType { RoleId = new NodeId(100), Permissions = 1 };
            var rp2 = new RolePermissionType { RoleId = new NodeId(200), Permissions = 2 };

            var node1 = new Node { NodeId = new NodeId(8600), RolePermissions = [rp1] };
            var node2 = new Node { NodeId = new NodeId(8600), RolePermissions = [rp2] };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentUserRolePermissions()
        {
            var rp1 = new RolePermissionType { RoleId = new NodeId(110), Permissions = 1 };
            var rp2 = new RolePermissionType { RoleId = new NodeId(220), Permissions = 2 };

            var node1 = new Node { NodeId = new NodeId(8601), UserRolePermissions = [rp1] };
            var node2 = new Node { NodeId = new NodeId(8601), UserRolePermissions = [rp2] };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentReferences()
        {
            var ref1 = new ReferenceNode(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId(900));
            var ref2 = new ReferenceNode(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId(901));

            var node1 = new Node { NodeId = new NodeId(8602), References = [ref1] };
            var node2 = new Node { NodeId = new NodeId(8602), References = [ref2] };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForNonNodeEncodeable()
        {
            var node = new Node { NodeId = new NodeId(8603) };
            var other = new ReferenceNode();

            Assert.That(node.IsEqual(other), Is.False);
        }

        [Test]
        public void ReferenceExistsReturnsTrueWhenPresent()
        {
            var node = new Node { NodeId = new NodeId(8700) };
            var targetId = new ExpandedNodeId(8701);
            node.ReferenceTable.Add(ReferenceTypeIds.HasComponent, false, targetId);

            bool exists = node.ReferenceExists(ReferenceTypeIds.HasComponent, false, targetId);

            Assert.That(exists, Is.True);
        }

        [Test]
        public void ReferenceExistsReturnsFalseWhenAbsent()
        {
            var node = new Node { NodeId = new NodeId(8710) };

            bool exists = node.ReferenceExists(
                ReferenceTypeIds.HasComponent, false, new ExpandedNodeId(9999));

            Assert.That(exists, Is.False);
        }

        [Test]
        public void FindReturnsMatchingReferences()
        {
            var node = new Node { NodeId = new NodeId(8720) };
            var target1 = new ExpandedNodeId(8721);
            var target2 = new ExpandedNodeId(8722);
            node.ReferenceTable.Add(ReferenceTypeIds.HasComponent, false, target1);
            node.ReferenceTable.Add(ReferenceTypeIds.HasComponent, false, target2);
            node.ReferenceTable.Add(ReferenceTypeIds.HasProperty, false, new ExpandedNodeId(8723));

            IList<IReference> found = node.Find(ReferenceTypeIds.HasComponent, false);

            Assert.That(found, Has.Count.EqualTo(2));
        }

        [Test]
        public void FindReturnsEmptyWhenNoMatch()
        {
            var node = new Node { NodeId = new NodeId(8730) };

            IList<IReference> found = node.Find(ReferenceTypeIds.HasComponent, false);

            Assert.That(found, Is.Empty);
        }

        [Test]
        public void FindTargetReturnsMatchingTarget()
        {
            var node = new Node { NodeId = new NodeId(8740) };
            var target = new ExpandedNodeId(8741);
            node.ReferenceTable.Add(ReferenceTypeIds.Organizes, false, target);

            ExpandedNodeId foundTarget = node.FindTarget(ReferenceTypeIds.Organizes, false, 0);

            Assert.That(foundTarget, Is.EqualTo(target));
        }

        [Test]
        public void FindTargetReturnsDefaultWhenNotFound()
        {
            var node = new Node { NodeId = new NodeId(8750) };

            ExpandedNodeId foundTarget = node.FindTarget(ReferenceTypeIds.Organizes, false, 0);

            Assert.That(foundTarget, Is.Default);
        }

        [Test]
        public void GetSuperTypeReturnsDefaultWhenNoReferences()
        {
            var node = new Node { NodeId = new NodeId(8800) };

            ExpandedNodeId superType = node.GetSuperType(null);

            Assert.That(superType, Is.Default);
        }

        [Test]
        public void GetSuperTypeReturnsTargetWhenHasSubtypeInverse()
        {
            var node = new Node { NodeId = new NodeId(8810) };
            var parentType = new ExpandedNodeId(8811);
            node.ReferenceTable.Add(ReferenceTypeIds.HasSubtype, true, parentType);

            ExpandedNodeId superType = node.GetSuperType(null);

            Assert.That(superType, Is.EqualTo(parentType));
        }

        [Test]
        public void TypeDefinitionIdReturnsDefaultWhenNoReferenceTable()
        {
            var node = new Node { NodeId = new NodeId(8820) };

            ExpandedNodeId typeDefId = node.TypeDefinitionId;

            Assert.That(typeDefId, Is.Default);
        }

        [Test]
        public void TypeDefinitionIdReturnsTargetWhenPresent()
        {
            var node = new Node { NodeId = new NodeId(8830) };
            var typeDef = new ExpandedNodeId(8831);
            node.ReferenceTable.Add(ReferenceTypeIds.HasTypeDefinition, false, typeDef);

            ExpandedNodeId typeDefId = node.TypeDefinitionId;

            Assert.That(typeDefId, Is.EqualTo(typeDef));
        }

        [Test]
        public void GetHashCodeReturnsConsistentValue()
        {
            var node = new Node
            {
                NodeId = new NodeId(8900),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("HashTest")
            };

            int hash1 = node.GetHashCode();
            int hash2 = node.GetHashCode();

            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void ModellingRuleReturnsDefaultWhenNotSet()
        {
            var node = new Node { NodeId = new NodeId(8910) };

            NodeId modellingRule = node.ModellingRule;

            Assert.That(modellingRule, Is.Default);
        }

        [Test]
        public void ILocalNodeWriteMaskPropertyMaps()
        {
            var node = new Node { WriteMask = 0xFF };

            ILocalNode localNode = node;

#pragma warning disable RCS1257 // Use enum field explicitly
            Assert.That(localNode.WriteMask, Is.EqualTo((AttributeWriteMask)0xFF));
#pragma warning restore RCS1257 // Use enum field explicitly

            localNode.WriteMask = AttributeWriteMask.DisplayName;
            Assert.That(node.WriteMask, Is.EqualTo((uint)AttributeWriteMask.DisplayName));
        }

        [Test]
        public void ILocalNodeUserWriteMaskPropertyMaps()
        {
            var node = new Node { UserWriteMask = 0x0F };

            ILocalNode localNode = node;

#pragma warning disable RCS1257 // Use enum field explicitly
            Assert.That(localNode.UserWriteMask, Is.EqualTo((AttributeWriteMask)0x0F));
#pragma warning restore RCS1257 // Use enum field explicitly

            localNode.UserWriteMask = AttributeWriteMask.Description;
            Assert.That(node.UserWriteMask, Is.EqualTo((uint)AttributeWriteMask.Description));
        }

        [Test]
        public void ILocalNodeReferencesReturnsReferenceTable()
        {
            var node = new Node { NodeId = new NodeId(8920) };
            node.ReferenceTable.Add(ReferenceTypeIds.HasComponent, false, new ExpandedNodeId(8921));

            ILocalNode localNode = node;

            Assert.That(localNode.References, Is.Not.Null);
            Assert.That(localNode.References, Is.SameAs(node.ReferenceTable));
        }

        [Test]
        public void INodeNodeIdReturnsExpandedNodeId()
        {
            INode iNode = new Node { NodeId = new NodeId(8930) };

            Assert.That(iNode.NodeId, Is.EqualTo(new ExpandedNodeId(new NodeId(8930))));
        }

        [Test]
        public void SupportsAttributeForRolePermissions()
        {
            var node = new Node();
            Assert.That(node.SupportsAttribute(Attributes.RolePermissions), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.UserRolePermissions), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.AccessRestrictions), Is.True);
        }
    }
}
