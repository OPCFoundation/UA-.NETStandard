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

using NUnit.Framework;

#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable NUnit2010

namespace Opc.Ua.Types.Tests.Nodes
{
    [TestFixture]
    [Category("Node")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VariableNodeTests
    {
        /// <summary>
        /// Creates a fully populated VariableNode for testing.
        /// </summary>
        private static VariableNode CreatePopulatedNode()
        {
            return new VariableNode
            {
                NodeId = new NodeId(42u),
                BrowseName = new QualifiedName("TestVar"),
                DisplayName = new LocalizedText("Test Variable"),
                Description = new LocalizedText("A test variable node"),
                Value = new Variant(100),
                DataType = new NodeId(6u),
                ValueRank = 1,
                ArrayDimensions = [5],
                AccessLevel = 3,
                UserAccessLevel = 1,
                MinimumSamplingInterval = 500.0,
                Historizing = false,
                AccessLevelEx = 7u
            };
        }

        [Test]
        public void DefaultConstructorSetsExpectedDefaults()
        {
            var node = new VariableNode();

            Assert.That(node.Value.IsNull, Is.True);
            Assert.That(node.DataType, Is.Default);
            Assert.That(node.ValueRank, Is.Zero);
            Assert.That(node.ArrayDimensions.IsEmpty, Is.True);
            Assert.That(node.AccessLevel, Is.Zero);
            Assert.That(node.UserAccessLevel, Is.Zero);
            Assert.That(node.MinimumSamplingInterval, Is.Zero);
            Assert.That(node.Historizing, Is.True);
            Assert.That(node.AccessLevelEx, Is.Zero);
        }

        [Test]
        public void CopyConstructorFromVariableNodeCopiesAllProperties()
        {
            var source = new VariableNode
            {
                NodeId = new NodeId(99u),
                BrowseName = new QualifiedName("SourceVar"),
                DisplayName = new LocalizedText("Source Variable"),
                Value = new Variant(42.5),
                DataType = new NodeId(11u),
                ValueRank = 1,
                ArrayDimensions = [10],
                AccessLevel = 5,
                UserAccessLevel = 3,
                MinimumSamplingInterval = 250.0,
                Historizing = true
            };

            var copy = new VariableNode(source);

            Assert.That(copy.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(copy.DataType, Is.EqualTo(source.DataType));
            Assert.That(copy.ValueRank, Is.EqualTo(source.ValueRank));
            Assert.That(copy.Value.Value, Is.EqualTo(source.Value.Value));
            Assert.That(copy.AccessLevel, Is.EqualTo(source.AccessLevel));
            Assert.That(copy.UserAccessLevel, Is.EqualTo(source.UserAccessLevel));
            Assert.That(copy.MinimumSamplingInterval, Is.EqualTo(source.MinimumSamplingInterval));
            Assert.That(copy.Historizing, Is.EqualTo(source.Historizing));
            Assert.That(copy.ArrayDimensions.Count, Is.EqualTo(source.ArrayDimensions.Count));
        }

        [Test]
        public void CopyConstructorFromNonVariableNodeSetsNodeClass()
        {
            var source = new ObjectTypeNode
            {
                NodeId = new NodeId(77u),
                BrowseName = new QualifiedName("ObjType"),
                DisplayName = new LocalizedText("Object Type")
            };

            var node = new VariableNode(source);

            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(node.Value.IsNull, Is.True);
            Assert.That(node.ValueRank, Is.Zero);
        }

        [Test]
        public void TypeIdReturnsExpectedValue()
        {
            var node = new VariableNode();
            Assert.That(node.TypeId, Is.EqualTo(DataTypeIds.VariableNode));
        }

        [Test]
        public void BinaryEncodingIdReturnsExpectedValue()
        {
            var node = new VariableNode();
            Assert.That(node.BinaryEncodingId, Is.EqualTo(ObjectIds.VariableNode_Encoding_DefaultBinary));
        }

        [Test]
        public void XmlEncodingIdReturnsExpectedValue()
        {
            var node = new VariableNode();
            Assert.That(node.XmlEncodingId, Is.EqualTo(ObjectIds.VariableNode_Encoding_DefaultXml));
        }


        [Test]
        public void EncodeDecodeRoundTripPreservesAllProperties()
        {
            VariableNode original = CreatePopulatedNode();

            var context = new ServiceMessageContext();

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                original.Encode(encoder);
                buffer = encoder.CloseAndReturnBuffer();
            }

            var decoded = new VariableNode();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }

            Assert.That(original.IsEqual(decoded), Is.True);
        }

        [Test]
        public void EncodeDecodeRoundTripWithDefaults()
        {
            var original = new VariableNode();

            var context = new ServiceMessageContext();

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                original.Encode(encoder);
                buffer = encoder.CloseAndReturnBuffer();
            }

            var decoded = new VariableNode();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }

            Assert.That(original.IsEqual(decoded), Is.True);
        }

        [Test]
        public void IsEqualWithSameReferenceReturnsTrue()
        {
            VariableNode node = CreatePopulatedNode();
            Assert.That(node.IsEqual(node), Is.True);
        }

        [Test]
        public void IsEqualWithIdenticalNodeReturnsTrue()
        {
            VariableNode node1 = CreatePopulatedNode();
            var node2 = (VariableNode)node1.Clone();

            Assert.That(node1.IsEqual(node2), Is.True);
        }

        [Test]
        public void IsEqualWithNullReturnsFalse()
        {
            var node = new VariableNode();
            Assert.That(node.IsEqual(null), Is.False);
        }

        [Test]
        public void IsEqualWithWrongTypeReturnsFalse()
        {
            var node = new VariableNode();
            var other = new ObjectTypeNode();
            Assert.That(node.IsEqual(other), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentValueReturnsFalse()
        {
            var node1 = new VariableNode { Value = new Variant(1) };
            var node2 = new VariableNode { Value = new Variant(2) };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentDataTypeReturnsFalse()
        {
            var node1 = new VariableNode { DataType = new NodeId(1u) };
            var node2 = new VariableNode { DataType = new NodeId(2u) };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentValueRankReturnsFalse()
        {
            var node1 = new VariableNode { ValueRank = -1 };
            var node2 = new VariableNode { ValueRank = 1 };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentArrayDimensionsReturnsFalse()
        {
            var node1 = new VariableNode { ArrayDimensions = [5] };
            var node2 = new VariableNode { ArrayDimensions = [10] };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentAccessLevelReturnsFalse()
        {
            var node1 = new VariableNode { AccessLevel = 1 };
            var node2 = new VariableNode { AccessLevel = 2 };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentUserAccessLevelReturnsFalse()
        {
            var node1 = new VariableNode { UserAccessLevel = 1 };
            var node2 = new VariableNode { UserAccessLevel = 2 };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentMinimumSamplingIntervalReturnsFalse()
        {
            var node1 = new VariableNode { MinimumSamplingInterval = 100.0 };
            var node2 = new VariableNode { MinimumSamplingInterval = 200.0 };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentHistorizingReturnsFalse()
        {
            var node1 = new VariableNode { Historizing = true };
            var node2 = new VariableNode { Historizing = false };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentAccessLevelExReturnsFalse()
        {
            var node1 = new VariableNode { AccessLevelEx = 1u };
            var node2 = new VariableNode { AccessLevelEx = 2u };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentBaseFieldReturnsFalse()
        {
            var node1 = new VariableNode { NodeId = new NodeId(1u) };
            var node2 = new VariableNode { NodeId = new NodeId(2u) };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void EqualsObjectWithSameReferenceReturnsTrue()
        {
            VariableNode node = CreatePopulatedNode();
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(node.Equals((object)node), Is.True);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void EqualsObjectWithDifferentReferenceReturnsFalse()
        {
            VariableNode node1 = CreatePopulatedNode();
            var node2 = (VariableNode)node1.Clone();

#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(node1.Equals((object)node2), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
#pragma warning disable CA1508
        public void EqualsObjectWithNullReturnsFalse()
        {
            var node = new VariableNode();
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(node.Equals((object)null), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }
#pragma warning restore CA1508

        [Test]
        public void GetHashCodeIsDeterministic()
        {
            VariableNode node = CreatePopulatedNode();
            int hash = node.GetHashCode();

            Assert.That(node.GetHashCode(), Is.EqualTo(hash));
        }

        [Test]
        public void CloneCreatesEqualCopy()
        {
            VariableNode original = CreatePopulatedNode();

            var clone = (VariableNode)original.Clone();

            Assert.That(original.IsEqual(clone), Is.True);
            Assert.That(clone.Value.Value, Is.EqualTo(original.Value.Value));
            Assert.That(clone.DataType, Is.EqualTo(original.DataType));
            Assert.That(clone.ValueRank, Is.EqualTo(original.ValueRank));
            Assert.That(clone.AccessLevel, Is.EqualTo(original.AccessLevel));
            Assert.That(clone.UserAccessLevel, Is.EqualTo(original.UserAccessLevel));
            Assert.That(clone.MinimumSamplingInterval, Is.EqualTo(original.MinimumSamplingInterval));
            Assert.That(clone.Historizing, Is.EqualTo(original.Historizing));
            Assert.That(clone.AccessLevelEx, Is.EqualTo(original.AccessLevelEx));
            Assert.That(clone.ArrayDimensions.Count, Is.EqualTo(original.ArrayDimensions.Count));
        }

        [Test]
        public void CloneIsIndependentOfOriginal()
        {
            var original = new VariableNode
            {
                Value = new Variant(50),
                DataType = new NodeId(6u),
                ValueRank = 1,
                ArrayDimensions = [10],
                AccessLevel = 3,
                UserAccessLevel = 1,
                MinimumSamplingInterval = 500.0,
                Historizing = false,
                AccessLevelEx = 7u
            };

            var clone = (VariableNode)original.Clone();

            original.Value = new Variant(999);
            original.DataType = new NodeId(99u);
            original.ValueRank = 2;
            original.AccessLevel = 255;
            original.Historizing = true;

            Assert.That(clone.Value.Value, Is.EqualTo(50));
            Assert.That(clone.DataType, Is.EqualTo(new NodeId(6u)));
            Assert.That(clone.ValueRank, Is.EqualTo(1));
            Assert.That(clone.AccessLevel, Is.EqualTo((byte)3));
            Assert.That(clone.Historizing, Is.False);
        }

        [Test]
        public void MemberwiseCloneCreatesEqualCopy()
        {
            VariableNode original = CreatePopulatedNode();

            var clone = (VariableNode)original.MemberwiseClone();

            Assert.That(original.IsEqual(clone), Is.True);
        }

        [Test]
        public void SupportsAttributeValueReturnsTrue()
        {
            var node = new VariableNode { Value = new Variant(42) };
            Assert.That(node.SupportsAttribute(Attributes.Value), Is.True);
        }

        [Test]
        public void SupportsAttributeDataTypeReturnsTrue()
        {
            var node = new VariableNode();
            Assert.That(node.SupportsAttribute(Attributes.DataType), Is.True);
        }

        [Test]
        public void SupportsAttributeValueRankReturnsTrue()
        {
            var node = new VariableNode();
            Assert.That(node.SupportsAttribute(Attributes.ValueRank), Is.True);
        }

        [Test]
        public void SupportsAttributeAccessLevelReturnsTrue()
        {
            var node = new VariableNode();
            Assert.That(node.SupportsAttribute(Attributes.AccessLevel), Is.True);
        }

        [Test]
        public void SupportsAttributeUserAccessLevelReturnsTrue()
        {
            var node = new VariableNode();
            Assert.That(node.SupportsAttribute(Attributes.UserAccessLevel), Is.True);
        }

        [Test]
        public void SupportsAttributeMinimumSamplingIntervalReturnsTrue()
        {
            var node = new VariableNode();
            Assert.That(node.SupportsAttribute(Attributes.MinimumSamplingInterval), Is.True);
        }

        [Test]
        public void SupportsAttributeHistorizingReturnsTrue()
        {
            var node = new VariableNode();
            Assert.That(node.SupportsAttribute(Attributes.Historizing), Is.True);
        }

        [Test]
        public void SupportsAttributeAccessLevelExReturnsTrue()
        {
            var node = new VariableNode();
            Assert.That(node.SupportsAttribute(Attributes.AccessLevelEx), Is.True);
        }

        [Test]
        public void SupportsAttributeArrayDimensionsWhenPopulatedReturnsTrue()
        {
            var node = new VariableNode { ArrayDimensions = [5] };
            Assert.That(node.SupportsAttribute(Attributes.ArrayDimensions), Is.True);
        }

        [Test]
        public void SupportsAttributeArrayDimensionsWhenEmptyReturnsFalse()
        {
            var node = new VariableNode { ArrayDimensions = [] };
            Assert.That(node.SupportsAttribute(Attributes.ArrayDimensions), Is.False);
        }

        [Test]
        public void SupportsAttributeBaseAttributeDelegatesToBase()
        {
            var node = new VariableNode();
            Assert.That(node.SupportsAttribute(Attributes.NodeId), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.BrowseName), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.DisplayName), Is.True);
        }
    }
}
