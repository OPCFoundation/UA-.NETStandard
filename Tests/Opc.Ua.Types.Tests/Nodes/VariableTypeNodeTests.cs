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

using System.IO;
using System.Runtime.Serialization;
using NUnit.Framework;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Opc.Ua.Types.Tests.Nodes
{
    [TestFixture]
    [Category("VariableTypeNode")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VariableTypeNodeTests
    {

        /// <summary>
        /// Test subclass that exposes protected Read/Write methods for direct testing.
        /// </summary>
        private sealed class TestableVariableTypeNode : VariableTypeNode
        {
            public Variant TestRead(uint attributeId) => Read(attributeId);
            public ServiceResult TestWrite(uint attributeId, Variant value) => Write(attributeId, value);
        }

        /// <summary>
        /// Creates a fully populated VariableTypeNode for testing.
        /// </summary>
        private static VariableTypeNode CreatePopulatedNode()
        {
            return new VariableTypeNode
            {
                NodeId = new NodeId(42u),
                BrowseName = new QualifiedName("TestVarType"),
                DisplayName = new LocalizedText("Test Variable Type"),
                Description = new LocalizedText("A test variable type node"),
                Value = new Variant(100),
                DataType = new NodeId(6u),
                ValueRank = -1,
                ArrayDimensions = new UInt32Collection(),
                IsAbstract = false
            };
        }

        /// <summary>
        /// Creates a fully populated TestableVariableTypeNode for Read/Write testing.
        /// </summary>
        private static TestableVariableTypeNode CreatePopulatedTestableNode()
        {
            return new TestableVariableTypeNode
            {
                NodeId = new NodeId(42u),
                BrowseName = new QualifiedName("TestVarType"),
                DisplayName = new LocalizedText("Test Variable Type"),
                Description = new LocalizedText("A test variable type node"),
                Value = new Variant(100),
                DataType = new NodeId(6u),
                ValueRank = -1,
                ArrayDimensions = new UInt32Collection(),
                IsAbstract = false
            };
        }

        /// <summary>
        /// Covers lines 46-57: copy constructor with IVariableType source.
        /// </summary>
        [Test]
        public void CopyConstructorFromVariableTypeCopiesAllProperties()
        {
            var source = new VariableTypeNode
            {
                NodeId = new NodeId(99u),
                BrowseName = new QualifiedName("SourceType"),
                DisplayName = new LocalizedText("Source Type"),
                Value = new Variant(42.5),
                DataType = new NodeId(11u),
                ValueRank = 1,
                ArrayDimensions = new UInt32Collection { 10 },
                IsAbstract = true
            };

            var copy = new VariableTypeNode(source);

            Assert.That(copy.NodeClass, Is.EqualTo(NodeClass.VariableType));
            Assert.That(copy.IsAbstract, Is.EqualTo(source.IsAbstract));
            Assert.That(copy.DataType, Is.EqualTo(source.DataType));
            Assert.That(copy.ValueRank, Is.EqualTo(source.ValueRank));
            Assert.That(copy.Value.Value, Is.EqualTo(source.Value.Value));
            Assert.That(copy.ArrayDimensions.Count, Is.EqualTo(source.ArrayDimensions.Count));
        }

        /// <summary>
        /// Covers line 48-50: copy constructor from non-IVariableType ILocalNode
        /// (the if-branch at line 50 is false, so lines 51-57 are skipped).
        /// </summary>
        [Test]
        public void CopyConstructorFromNonVariableTypeNodeSetsNodeClass()
        {
            // ObjectTypeNode implements ILocalNode but not IVariableType
            var source = new ObjectTypeNode
            {
                NodeId = new NodeId(77u),
                BrowseName = new QualifiedName("ObjType"),
                DisplayName = new LocalizedText("Object Type")
            };

            var node = new VariableTypeNode(source);

            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.VariableType));
            // VariableType-specific properties should remain at defaults
            Assert.That(node.Value.IsNull, Is.True);
            Assert.That(node.ValueRank, Is.EqualTo(0));
        }

        /// <summary>
        /// Covers lines 105, 109-114: ArrayDimensions getter and null-coercion in setter.
        /// </summary>
        [Test]
        public void ArrayDimensionsSetToNullCoercesToEmpty()
        {
            var node = new VariableTypeNode
            {
                ArrayDimensions = null
            };

            Assert.That(node.ArrayDimensions, Is.Not.Null);
            Assert.That(node.ArrayDimensions.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Covers lines 105, 109: ArrayDimensions getter/setter with non-null value.
        /// </summary>
        [Test]
        public void ArrayDimensionsSetNonNullValue()
        {
            var node = new VariableTypeNode();
            var dims = new UInt32Collection { 3, 5, 7 };
            node.ArrayDimensions = dims;

            Assert.That(node.ArrayDimensions.Count, Is.EqualTo(3));
            Assert.That(node.ArrayDimensions[0], Is.EqualTo(3));
            Assert.That(node.ArrayDimensions[1], Is.EqualTo(5));
            Assert.That(node.ArrayDimensions[2], Is.EqualTo(7));
        }

        /// <summary>
        /// Covers lines 235-236: explicit IVariableBase.ArrayDimensions getter and setter.
        /// </summary>
        [Test]
        public void ExplicitInterfaceArrayDimensionsAccessors()
        {
            var node = new VariableTypeNode();
            IVariableBase varBase = node;

            // Initially empty
            Assert.That(varBase.ArrayDimensions.IsEmpty, Is.True);

            // Set via the public property, read via the explicit interface getter (line 235)
            node.ArrayDimensions = new UInt32Collection { 10, 20 };
            Assert.That(varBase.ArrayDimensions.Count, Is.EqualTo(2));

            // Set via explicit interface setter (line 236)
            varBase.ArrayDimensions = new uint[] { 1, 2, 3 };
            Assert.That(node.ArrayDimensions.Count, Is.EqualTo(3));
        }

        /// <summary>
        /// Covers line 125: TypeId property.
        /// </summary>
        [Test]
        public void TypeIdReturnsExpectedValue()
        {
            var node = new VariableTypeNode();
            Assert.That(node.TypeId, Is.EqualTo(DataTypeIds.VariableTypeNode));
        }

        /// <summary>
        /// Covers line 128: BinaryEncodingId property.
        /// </summary>
        [Test]
        public void BinaryEncodingIdReturnsExpectedValue()
        {
            var node = new VariableTypeNode();
            Assert.That(node.BinaryEncodingId, Is.EqualTo(ObjectIds.VariableTypeNode_Encoding_DefaultBinary));
        }

        /// <summary>
        /// Covers line 131: XmlEncodingId property.
        /// </summary>
        [Test]
        public void XmlEncodingIdReturnsExpectedValue()
        {
            var node = new VariableTypeNode();
            Assert.That(node.XmlEncodingId, Is.EqualTo(ObjectIds.VariableTypeNode_Encoding_DefaultXml));
        }

        /// <summary>
        /// Covers line 134: JsonEncodingId property.
        /// </summary>
        [Test]
        public void JsonEncodingIdReturnsExpectedValue()
        {
            var node = new VariableTypeNode();
            Assert.That(node.JsonEncodingId, Is.EqualTo(ObjectIds.VariableTypeNode_Encoding_DefaultJson));
        }

        /// <summary>
        /// Covers lines 138-150 (Encode) and 154-166 (Decode) via binary round-trip.
        /// </summary>
        [Test]
        public void EncodeDecodeRoundTripPreservesAllProperties()
        {
            VariableTypeNode original = CreatePopulatedNode();
            original.ArrayDimensions = new UInt32Collection { 5 };
            original.ValueRank = 1;

            var context = new ServiceMessageContext();

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                original.Encode(encoder);
                buffer = encoder.CloseAndReturnBuffer();
            }

            var decoded = new VariableTypeNode();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }

            Assert.That(original.IsEqual(decoded), Is.True);
        }

        /// <summary>
        /// Covers lines 170-173: IsEqual with same reference returns true.
        /// </summary>
        [Test]
        public void IsEqualWithSameReferenceReturnsTrue()
        {
            VariableTypeNode node = CreatePopulatedNode();
            Assert.That(node.IsEqual(node), Is.True);
        }

        /// <summary>
        /// Covers lines 170, 176-178: IsEqual with non-VariableTypeNode returns false.
        /// </summary>
        [Test]
        public void IsEqualWithNonVariableTypeNodeReturnsFalse()
        {
            var node = new VariableTypeNode();
            var other = new ObjectTypeNode();
            Assert.That(node.IsEqual(other), Is.False);
        }

        /// <summary>
        /// Covers lines 170-207: IsEqual with identical node returns true (all checks pass).
        /// </summary>
        [Test]
        public void IsEqualWithIdenticalNodeReturnsTrue()
        {
            VariableTypeNode node1 = CreatePopulatedNode();
            var node2 = (VariableTypeNode)node1.Clone();

            Assert.That(node1.IsEqual(node2), Is.True);
        }

        /// <summary>
        /// Covers lines 181-183: IsEqual returns false when Value differs.
        /// </summary>
        [Test]
        public void IsEqualWithDifferentValueReturnsFalse()
        {
            var node1 = new VariableTypeNode { Value = new Variant(1) };
            var node2 = new VariableTypeNode { Value = new Variant(2) };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        /// <summary>
        /// Covers lines 186-188: IsEqual returns false when DataType differs.
        /// </summary>
        [Test]
        public void IsEqualWithDifferentDataTypeReturnsFalse()
        {
            var node1 = new VariableTypeNode { DataType = new NodeId(1u) };
            var node2 = new VariableTypeNode { DataType = new NodeId(2u) };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        /// <summary>
        /// Covers lines 191-193: IsEqual returns false when ValueRank differs.
        /// </summary>
        [Test]
        public void IsEqualWithDifferentValueRankReturnsFalse()
        {
            var node1 = new VariableTypeNode { ValueRank = -1 };
            var node2 = new VariableTypeNode { ValueRank = 1 };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        /// <summary>
        /// Covers lines 196-198: IsEqual returns false when ArrayDimensions differ.
        /// </summary>
        [Test]
        public void IsEqualWithDifferentArrayDimensionsReturnsFalse()
        {
            var node1 = new VariableTypeNode { ArrayDimensions = new UInt32Collection { 5 } };
            var node2 = new VariableTypeNode { ArrayDimensions = new UInt32Collection { 10 } };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        /// <summary>
        /// Covers lines 201-203: IsEqual returns false when IsAbstract differs.
        /// </summary>
        [Test]
        public void IsEqualWithDifferentIsAbstractReturnsFalse()
        {
            var node1 = new VariableTypeNode { IsAbstract = true };
            var node2 = new VariableTypeNode { IsAbstract = false };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        /// <summary>
        /// Covers lines 176-178: IsEqual with null returns false.
        /// </summary>
        [Test]
        public void IsEqualWithNullReturnsFalse()
        {
            var node = new VariableTypeNode();
            Assert.That(node.IsEqual(null), Is.False);
        }

        /// <summary>
        /// Covers lines 211-213 (Clone) and 217-227 (MemberwiseClone).
        /// </summary>
        [Test]
        public void CloneCreatesEqualCopy()
        {
            VariableTypeNode original = CreatePopulatedNode();
            original.ArrayDimensions = new UInt32Collection { 8 };
            original.ValueRank = 1;

            var clone = (VariableTypeNode)original.Clone();

            Assert.That(original.IsEqual(clone), Is.True);
            Assert.That(clone.Value.Value, Is.EqualTo(original.Value.Value));
            Assert.That(clone.DataType, Is.EqualTo(original.DataType));
            Assert.That(clone.ValueRank, Is.EqualTo(original.ValueRank));
            Assert.That(clone.IsAbstract, Is.EqualTo(original.IsAbstract));
            Assert.That(clone.ArrayDimensions.Count, Is.EqualTo(original.ArrayDimensions.Count));
        }

        /// <summary>
        /// Covers lines 217-227: MemberwiseClone creates independent copy.
        /// </summary>
        [Test]
        public void CloneIsIndependentOfOriginal()
        {
            var original = new VariableTypeNode
            {
                Value = new Variant(50),
                DataType = new NodeId(6u),
                ValueRank = 1,
                ArrayDimensions = new UInt32Collection { 10 },
                IsAbstract = false
            };

            var clone = (VariableTypeNode)original.Clone();

            // Modify the original
            original.Value = new Variant(999);
            original.DataType = new NodeId(99u);
            original.ValueRank = 2;
            original.IsAbstract = true;

            // Clone should not be affected
            Assert.That(clone.Value.Value, Is.EqualTo(50));
            Assert.That(clone.DataType, Is.EqualTo(new NodeId(6u)));
            Assert.That(clone.ValueRank, Is.EqualTo(1));
            Assert.That(clone.IsAbstract, Is.False);
        }

        /// <summary>
        /// Covers line 249: SupportsAttribute for Value when Value is not null.
        /// </summary>
        [Test]
        public void SupportsAttributeValueWhenNotNullReturnsTrue()
        {
            var node = new VariableTypeNode { Value = new Variant(42) };
            Assert.That(node.SupportsAttribute(Attributes.Value), Is.True);
        }

        /// <summary>
        /// Covers line 249: SupportsAttribute for Value when Value is null.
        /// </summary>
        [Test]
        public void SupportsAttributeValueWhenNullReturnsFalse()
        {
            var node = new VariableTypeNode { Value = Variant.Null };
            Assert.That(node.SupportsAttribute(Attributes.Value), Is.False);
        }

        /// <summary>
        /// Covers line 253: SupportsAttribute returns true for DataType.
        /// </summary>
        [Test]
        public void SupportsAttributeDataTypeReturnsTrue()
        {
            var node = new VariableTypeNode();
            Assert.That(node.SupportsAttribute(Attributes.DataType), Is.True);
        }

        /// <summary>
        /// Covers line 253: SupportsAttribute returns true for ValueRank.
        /// </summary>
        [Test]
        public void SupportsAttributeValueRankReturnsTrue()
        {
            var node = new VariableTypeNode();
            Assert.That(node.SupportsAttribute(Attributes.ValueRank), Is.True);
        }

        /// <summary>
        /// Covers line 253: SupportsAttribute returns true for IsAbstract.
        /// </summary>
        [Test]
        public void SupportsAttributeIsAbstractReturnsTrue()
        {
            var node = new VariableTypeNode();
            Assert.That(node.SupportsAttribute(Attributes.IsAbstract), Is.True);
        }

        /// <summary>
        /// Covers line 255: SupportsAttribute for ArrayDimensions when populated.
        /// </summary>
        [Test]
        public void SupportsAttributeArrayDimensionsWhenPopulatedReturnsTrue()
        {
            var node = new VariableTypeNode { ArrayDimensions = new UInt32Collection { 5 } };
            Assert.That(node.SupportsAttribute(Attributes.ArrayDimensions), Is.True);
        }

        /// <summary>
        /// Covers line 255: SupportsAttribute for ArrayDimensions when empty.
        /// </summary>
        [Test]
        public void SupportsAttributeArrayDimensionsWhenEmptyReturnsFalse()
        {
            var node = new VariableTypeNode { ArrayDimensions = new UInt32Collection() };
            Assert.That(node.SupportsAttribute(Attributes.ArrayDimensions), Is.False);
        }

        /// <summary>
        /// Covers line 257: SupportsAttribute delegates to base for non-variable-type attributes.
        /// </summary>
        [Test]
        public void SupportsAttributeBaseAttributeDelegatesToBase()
        {
            var node = new VariableTypeNode();
            // Base Node supports NodeId, BrowseName, DisplayName etc.
            Assert.That(node.SupportsAttribute(Attributes.NodeId), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.BrowseName), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.DisplayName), Is.True);
        }

        /// <summary>
        /// Covers line 271: Read returns DataType as Variant.
        /// </summary>
        [Test]
        public void ReadDataTypeAttribute()
        {
            var node = new TestableVariableTypeNode { DataType = new NodeId(11u) };
            Variant result = node.TestRead(Attributes.DataType);

            Assert.That((NodeId)result, Is.EqualTo(new NodeId(11u)));
        }

        /// <summary>
        /// Covers line 273: Read returns ValueRank as Variant.
        /// </summary>
        [Test]
        public void ReadValueRankAttribute()
        {
            var node = new TestableVariableTypeNode { ValueRank = 2 };
            Variant result = node.TestRead(Attributes.ValueRank);

            Assert.That((int)result, Is.EqualTo(2));
        }

        /// <summary>
        /// Covers line 276: Read returns Value as Variant.
        /// </summary>
        [Test]
        public void ReadValueAttribute()
        {
            var node = new TestableVariableTypeNode { Value = new Variant("hello") };
            Variant result = node.TestRead(Attributes.Value);

            Assert.That((string)result, Is.EqualTo("hello"));
        }

        /// <summary>
        /// Covers line 282: Read returns ArrayDimensions when populated.
        /// </summary>
        [Test]
        public void ReadArrayDimensionsWhenPopulated()
        {
            var node = new TestableVariableTypeNode
            {
                ArrayDimensions = new UInt32Collection { 5, 10 }
            };
            Variant result = node.TestRead(Attributes.ArrayDimensions);

            Assert.That(result.IsNull, Is.False);
        }

        /// <summary>
        /// Covers lines 278-280: Read returns BadAttributeIdInvalid when ArrayDimensions empty.
        /// </summary>
        [Test]
        public void ReadArrayDimensionsWhenEmptyReturnsBadStatus()
        {
            var node = new TestableVariableTypeNode
            {
                ArrayDimensions = new UInt32Collection()
            };
            Variant result = node.TestRead(Attributes.ArrayDimensions);

            // When ArrayDimensions is empty, Read returns StatusCodes.BadAttributeIdInvalid as a Variant
            var statusCode = (StatusCode)result;
            Assert.That(StatusCode.IsBad(statusCode), Is.True);
        }

        /// <summary>
        /// Covers line 284: Read delegates to base for attributes not handled by VariableTypeNode.
        /// </summary>
        [Test]
        public void ReadBaseAttribute()
        {
            var node = new TestableVariableTypeNode
            {
                BrowseName = new QualifiedName("TestBrowseName")
            };
            Variant result = node.TestRead(Attributes.BrowseName);

            Assert.That((QualifiedName)result, Is.EqualTo(new QualifiedName("TestBrowseName")));
        }

        /// <summary>
        /// Covers lines 300-301: Write Value clones and stores the value.
        /// </summary>
        [Test]
        public void WriteValueAttribute()
        {
            var node = new TestableVariableTypeNode();
            ServiceResult result = node.TestWrite(Attributes.Value, new Variant(42));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That((int)node.Value, Is.EqualTo(42));
        }

        /// <summary>
        /// Covers lines 303-312: Write DataType when different from current resets Value to Null.
        /// </summary>
        [Test]
        public void WriteDataTypeWithDifferentTypeResetsValue()
        {
            var node = new TestableVariableTypeNode
            {
                DataType = new NodeId(6u),
                Value = new Variant(100)
            };

            ServiceResult result = node.TestWrite(Attributes.DataType, new Variant(new NodeId(11u)));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.DataType, Is.EqualTo(new NodeId(11u)));
            Assert.That(node.Value.IsNull, Is.True);
        }

        /// <summary>
        /// Covers lines 303-312: Write DataType with same type preserves Value.
        /// </summary>
        [Test]
        public void WriteDataTypeWithSameTypePreservesValue()
        {
            var node = new TestableVariableTypeNode
            {
                DataType = new NodeId(6u),
                Value = new Variant(100)
            };

            ServiceResult result = node.TestWrite(Attributes.DataType, new Variant(new NodeId(6u)));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.DataType, Is.EqualTo(new NodeId(6u)));
            Assert.That(node.Value.IsNull, Is.False);
        }

        /// <summary>
        /// Covers lines 314-323: Write ValueRank with different rank resets Value to Null.
        /// </summary>
        [Test]
        public void WriteValueRankWithDifferentRankResetsValue()
        {
            var node = new TestableVariableTypeNode
            {
                ValueRank = -1,
                Value = new Variant(50)
            };

            ServiceResult result = node.TestWrite(Attributes.ValueRank, new Variant(1));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.ValueRank, Is.EqualTo(1));
            Assert.That(node.Value.IsNull, Is.True);
        }

        /// <summary>
        /// Covers lines 314-323: Write ValueRank with same rank preserves Value.
        /// </summary>
        [Test]
        public void WriteValueRankWithSameRankPreservesValue()
        {
            var node = new TestableVariableTypeNode
            {
                ValueRank = -1,
                Value = new Variant(50)
            };

            ServiceResult result = node.TestWrite(Attributes.ValueRank, new Variant(-1));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.ValueRank, Is.EqualTo(-1));
            Assert.That(node.Value.IsNull, Is.False);
        }

        /// <summary>
        /// Covers lines 325-334: Write ArrayDimensions adjusts ValueRank when mismatched.
        /// </summary>
        [Test]
        public void WriteArrayDimensionsWithMismatchedRankAdjustsValueRank()
        {
            var node = new TestableVariableTypeNode
            {
                ValueRank = -1,
                Value = new Variant(50)
            };

            ServiceResult result = node.TestWrite(Attributes.ArrayDimensions, new Variant(new uint[] { 5, 10 }));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            // ValueRank should be updated to match the number of dimensions (2)
            Assert.That(node.ValueRank, Is.EqualTo(2));
            // Value should be reset since ValueRank changed
            Assert.That(node.Value.IsNull, Is.True);
        }

        /// <summary>
        /// Covers lines 325-334: Write ArrayDimensions with matching ValueRank.
        /// </summary>
        [Test]
        public void WriteArrayDimensionsWithMatchingRank()
        {
            var node = new TestableVariableTypeNode
            {
                ValueRank = 2,
                Value = new Variant(50)
            };

            ServiceResult result = node.TestWrite(Attributes.ArrayDimensions, new Variant(new uint[] { 5, 10 }));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            // ValueRank matches dimensions count, so it should not change
            Assert.That(node.ValueRank, Is.EqualTo(2));
            // Value should NOT be reset since rank matches
            Assert.That(node.Value.IsNull, Is.False);
        }

        /// <summary>
        /// Covers lines 325-334: Write empty ArrayDimensions does not adjust ValueRank.
        /// </summary>
        [Test]
        public void WriteArrayDimensionsWithEmptyArray()
        {
            var node = new TestableVariableTypeNode
            {
                ValueRank = 1,
                Value = new Variant(50)
            };

            ServiceResult result = node.TestWrite(Attributes.ArrayDimensions, new Variant(System.Array.Empty<uint>()));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            // Empty array: Count == 0, so the adjustment condition is false
            Assert.That(node.ValueRank, Is.EqualTo(1));
        }

        /// <summary>
        /// Covers lines 336, 338: Write delegates to base for non-variable-type attributes.
        /// </summary>
        [Test]
        public void WriteBaseAttributeDelegatesToBase()
        {
            var node = new TestableVariableTypeNode
            {
                DisplayName = new LocalizedText("Original")
            };

            ServiceResult result = node.TestWrite(Attributes.DisplayName, new Variant(new LocalizedText("Updated")));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.DisplayName, Is.EqualTo(new LocalizedText("Updated")));
        }

        /// <summary>
        /// Covers lines 68-70: OnDeserializing callback invoked during DataContract deserialization.
        /// The Initialize method sets defaults before properties are deserialized.
        /// </summary>
        [Test]
        public void DataContractDeserializationTriggersInitialize()
        {
            var original = new VariableTypeNode
            {
                IsAbstract = false,
                ValueRank = 3,
                DataType = new NodeId(6u)
            };
            var serializer = new DataContractSerializer(typeof(VariableTypeNode));

            using var stream = new MemoryStream();
            serializer.WriteObject(stream, original);
            stream.Position = 0;

            var deserialized = (VariableTypeNode)serializer.ReadObject(stream);

            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.IsAbstract, Is.EqualTo(false));
            Assert.That(deserialized.ValueRank, Is.EqualTo(3));
        }

        /// <summary>
        /// Tests reading Value attribute through the public ILocalNode.Read API.
        /// </summary>
        [Test]
        public void PublicReadApiReturnsValueAttribute()
        {
            VariableTypeNode node = CreatePopulatedNode();
            var dataValue = new DataValue();
            ServiceResult result = node.Read(null, Attributes.Value, dataValue);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That((int)dataValue.WrappedValue, Is.EqualTo(100));
        }

        /// <summary>
        /// Tests writing Value attribute through the public ILocalNode.Write API.
        /// </summary>
        [Test]
        public void PublicWriteApiSetsValueAttribute()
        {
            VariableTypeNode node = CreatePopulatedNode();
            ServiceResult result = node.Write(Attributes.Value, new DataValue(new Variant(999)));

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That((int)node.Value, Is.EqualTo(999));
        }

        /// <summary>
        /// Tests that reading an unsupported attribute returns bad status.
        /// </summary>
        [Test]
        public void PublicReadApiUnsupportedAttributeReturnsBad()
        {
            var node = new VariableTypeNode { Value = Variant.Null };
            var dataValue = new DataValue();
            ServiceResult result = node.Read(null, Attributes.Value, dataValue);

            Assert.That(ServiceResult.IsBad(result), Is.True);
        }
    }
}
