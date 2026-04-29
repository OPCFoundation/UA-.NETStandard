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

namespace Opc.Ua.Types.Tests.State
{
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VariableNodeTests
    {
        [Test]
        public void DefaultConstructorInitializesDefaults()
        {
            var vn = new VariableNode();
            Assert.That(vn, Is.Not.Null);
            Assert.That(vn.Value, Is.EqualTo(Variant.Null));
            Assert.That(vn.DataType, Is.EqualTo(NodeId.Null));
            Assert.That(vn.ValueRank, Is.Zero);
            Assert.That(vn.AccessLevel, Is.Zero);
            Assert.That(vn.UserAccessLevel, Is.Zero);
            Assert.That(vn.MinimumSamplingInterval, Is.Zero);
            Assert.That(vn.Historizing, Is.True);
            Assert.That(vn.AccessLevelEx, Is.Zero);
            Assert.That(vn.ArrayDimensions.IsNull, Is.False);
        }

        [Test]
        public void PropertiesSetAndGet()
        {
            var vn = new VariableNode
            {
                NodeId = new NodeId(8000),
                NodeClass = NodeClass.Variable,
                Value = new Variant(42),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                MinimumSamplingInterval = 100.0,
                Historizing = false,
                AccessLevelEx = 0x01
            };

            Assert.That((int)vn.Value, Is.EqualTo(42));
            Assert.That(vn.DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(vn.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(vn.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(vn.MinimumSamplingInterval, Is.EqualTo(100.0));
            Assert.That(vn.Historizing, Is.False);
            Assert.That(vn.AccessLevelEx, Is.EqualTo(0x01u));
        }

        [Test]
        public void ArrayDimensionsSetValue()
        {
            var vn = new VariableNode
            {
                ArrayDimensions = [3, 4]
            };
            Assert.That(vn.ArrayDimensions.Count, Is.EqualTo(2));
            Assert.That(vn.ArrayDimensions[0], Is.EqualTo(3u));
        }

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var vn = new VariableNode
            {
                NodeId = new NodeId(8001),
                Value = new Variant("hello"),
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = 0x03,
                UserAccessLevel = 0x01,
                MinimumSamplingInterval = 50.0,
                Historizing = true,
                AccessLevelEx = 0x05
            };

            var clone = (VariableNode)vn.Clone();
            Assert.That(clone, Is.Not.SameAs(vn));
            Assert.That(clone.NodeId, Is.EqualTo(vn.NodeId));
            Assert.That((string)clone.Value, Is.EqualTo("hello"));
            Assert.That(clone.DataType, Is.EqualTo(vn.DataType));
            Assert.That(clone.AccessLevel, Is.EqualTo(vn.AccessLevel));
            Assert.That(clone.Historizing, Is.EqualTo(vn.Historizing));
            Assert.That(clone.AccessLevelEx, Is.EqualTo(vn.AccessLevelEx));
        }

        [Test]
        public void IsEqualReturnsTrueForSameReference()
        {
            var vn = new VariableNode { NodeId = new NodeId(8002), Value = new Variant(10) };
            Assert.That(vn.IsEqual(vn), Is.True);
        }

        [Test]
        public void IsEqualReturnsTrueForEqualNodes()
        {
            var vn1 = new VariableNode
            {
                NodeId = new NodeId(8003),
                Value = new Variant(42),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = 1,
                UserAccessLevel = 1,
                MinimumSamplingInterval = 0,
                Historizing = false,
                AccessLevelEx = 0
            };
            var vn2 = (VariableNode)vn1.Clone();
            Assert.That(vn1.IsEqual(vn2), Is.True);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentType()
        {
            var vn = new VariableNode { NodeId = new NodeId(8004) };
            var node = new Node { NodeId = new NodeId(8004) };
            Assert.That(vn.IsEqual(node), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentValue()
        {
            var vn1 = new VariableNode { NodeId = new NodeId(8005), Value = new Variant(1) };
            var vn2 = new VariableNode { NodeId = new NodeId(8005), Value = new Variant(2) };
            Assert.That(vn1.IsEqual(vn2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentDataType()
        {
            var vn1 = new VariableNode { NodeId = new NodeId(8006), DataType = DataTypeIds.Int32 };
            var vn2 = new VariableNode { NodeId = new NodeId(8006), DataType = DataTypeIds.String };
            Assert.That(vn1.IsEqual(vn2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentValueRank()
        {
            var vn1 = new VariableNode { NodeId = new NodeId(8007), ValueRank = ValueRanks.Scalar };
            var vn2 = new VariableNode { NodeId = new NodeId(8007), ValueRank = ValueRanks.OneDimension };
            Assert.That(vn1.IsEqual(vn2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentAccessLevel()
        {
            var vn1 = new VariableNode { NodeId = new NodeId(8008), AccessLevel = 1 };
            var vn2 = new VariableNode { NodeId = new NodeId(8008), AccessLevel = 2 };
            Assert.That(vn1.IsEqual(vn2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentHistorizing()
        {
            var vn1 = new VariableNode { NodeId = new NodeId(8011), Historizing = true };
            var vn2 = new VariableNode { NodeId = new NodeId(8011), Historizing = false };
            Assert.That(vn1.IsEqual(vn2), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseForDifferentAccessLevelEx()
        {
            var vn1 = new VariableNode { NodeId = new NodeId(8012), AccessLevelEx = 1 };
            var vn2 = new VariableNode { NodeId = new NodeId(8012), AccessLevelEx = 2 };
            Assert.That(vn1.IsEqual(vn2), Is.False);
        }

        [Test]
        public void EncodingIdProperties()
        {
            var vn = new VariableNode();
            Assert.That(vn.TypeId, Is.EqualTo(DataTypeIds.VariableNode));
            Assert.That(vn.BinaryEncodingId, Is.EqualTo(ObjectIds.VariableNode_Encoding_DefaultBinary));
            Assert.That(vn.XmlEncodingId, Is.EqualTo(ObjectIds.VariableNode_Encoding_DefaultXml));

        }

        [Test]
        public void SupportsAttributeForVariableAttributes()
        {
            var vn = new VariableNode();
            Assert.That(vn.SupportsAttribute(Attributes.Value), Is.True);
            Assert.That(vn.SupportsAttribute(Attributes.DataType), Is.True);
            Assert.That(vn.SupportsAttribute(Attributes.ValueRank), Is.True);
            Assert.That(vn.SupportsAttribute(Attributes.AccessLevel), Is.True);
            Assert.That(vn.SupportsAttribute(Attributes.Historizing), Is.True);
            Assert.That(vn.SupportsAttribute(Attributes.ArrayDimensions), Is.False);
        }

        [Test]
        public void SupportsAttributeForArrayDimensionsWhenPopulated()
        {
            var vn = new VariableNode { ArrayDimensions = [5] };
            Assert.That(vn.SupportsAttribute(Attributes.ArrayDimensions), Is.True);
        }
    }
}
