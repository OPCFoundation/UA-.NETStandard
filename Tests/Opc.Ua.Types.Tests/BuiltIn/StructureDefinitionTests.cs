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

#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages
#pragma warning disable CA1508 // Avoid dead conditional code

using System;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the <see cref="StructureDefinition"/> class.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StructureDefinitionTests
    {
        [Test]
        public void DefaultConstructorSetsDefaultValues()
        {
            var definition = new StructureDefinition();

            Assert.That(definition.DefaultEncodingId, Is.Default);
            Assert.That(definition.BaseDataType, Is.Default);
            Assert.That(definition.StructureType, Is.EqualTo(StructureType.Structure));
            Assert.That(definition.Fields, Is.Default);
            Assert.That(definition.FirstExplicitFieldIndex, Is.Zero);
        }

        [Test]
        public void TypeIdReturnsCorrectValue()
        {
            var definition = new StructureDefinition();

            Assert.That(definition.TypeId, Is.EqualTo(DataTypeIds.StructureDefinition));
        }

        [Test]
        public void BinaryEncodingIdReturnsCorrectValue()
        {
            var definition = new StructureDefinition();

            Assert.That(
                definition.BinaryEncodingId,
                Is.EqualTo(ObjectIds.StructureDefinition_Encoding_DefaultBinary));
        }

        [Test]
        public void XmlEncodingIdReturnsCorrectValue()
        {
            var definition = new StructureDefinition();

            Assert.That(
                definition.XmlEncodingId,
                Is.EqualTo(ObjectIds.StructureDefinition_Encoding_DefaultXml));
        }


        [Test]
        public void EncodeDecodeRoundTripWithAllFieldsSet()
        {
            StructureDefinition original = CreatePopulatedDefinition();

            var messageContext = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            messageContext.Factory.AddEncodeableType(typeof(StructureField));

            using var encoder = new BinaryEncoder(messageContext);
            original.Encode(encoder);
            byte[] buffer = encoder.CloseAndReturnBuffer();

            using var decoder = new BinaryDecoder(buffer, messageContext);
            var decoded = new StructureDefinition();
            decoded.Decode(decoder);

            Assert.That(decoded.DefaultEncodingId, Is.EqualTo(original.DefaultEncodingId));
            Assert.That(decoded.BaseDataType, Is.EqualTo(original.BaseDataType));
            Assert.That(decoded.StructureType, Is.EqualTo(original.StructureType));
            Assert.That(decoded.Fields, Is.EqualTo(original.Fields));
        }

        [Test]
        public void EncodeDecodeRoundTripWithDefaultFields()
        {
            var original = new StructureDefinition();

            var messageContext = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());

            using var encoder = new BinaryEncoder(messageContext);
            original.Encode(encoder);
            byte[] buffer = encoder.CloseAndReturnBuffer();

            using var decoder = new BinaryDecoder(buffer, messageContext);
            var decoded = new StructureDefinition();
            decoded.Decode(decoder);

            Assert.That(decoded.DefaultEncodingId, Is.EqualTo(original.DefaultEncodingId));
            Assert.That(decoded.BaseDataType, Is.EqualTo(original.BaseDataType));
            Assert.That(decoded.StructureType, Is.EqualTo(original.StructureType));
            Assert.That(decoded.Fields, Is.EqualTo(original.Fields));
        }

        [Test]
        public void EncodeDecodeRoundTripPreservesIsEqual()
        {
            StructureDefinition original = CreatePopulatedDefinition();

            var messageContext = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            messageContext.Factory.AddEncodeableType(typeof(StructureField));

            using var encoder = new BinaryEncoder(messageContext);
            original.Encode(encoder);
            byte[] buffer = encoder.CloseAndReturnBuffer();

            using var decoder = new BinaryDecoder(buffer, messageContext);
            var decoded = new StructureDefinition();
            decoded.Decode(decoder);

            Assert.That(original.IsEqual(decoded), Is.True);
        }

        [Test]
        public void IsEqualSameReferenceReturnsTrue()
        {
            StructureDefinition definition = CreatePopulatedDefinition();

            Assert.That(definition.IsEqual(definition), Is.True);
        }

        [Test]
        public void IsEqualNullReturnsFalse()
        {
            StructureDefinition definition = CreatePopulatedDefinition();

            Assert.That(definition.IsEqual(null), Is.False);
        }

        [Test]
        public void IsEqualWrongTypeReturnsFalse()
        {
            StructureDefinition definition = CreatePopulatedDefinition();
            var other = new Argument();

            Assert.That(definition.IsEqual(other), Is.False);
        }

        [Test]
        public void IsEqualEqualObjectsReturnsTrue()
        {
            StructureDefinition def1 = CreatePopulatedDefinition();
            StructureDefinition def2 = CreatePopulatedDefinition();

            Assert.That(def1.IsEqual(def2), Is.True);
        }

        [Test]
        public void IsEqualDifferentDefaultEncodingIdReturnsFalse()
        {
            StructureDefinition def1 = CreatePopulatedDefinition();
            StructureDefinition def2 = CreatePopulatedDefinition();
            def2.DefaultEncodingId = new NodeId(999);

            Assert.That(def1.IsEqual(def2), Is.False);
        }

        [Test]
        public void IsEqualDifferentBaseDataTypeReturnsFalse()
        {
            StructureDefinition def1 = CreatePopulatedDefinition();
            StructureDefinition def2 = CreatePopulatedDefinition();
            def2.BaseDataType = DataTypeIds.BaseDataType;

            Assert.That(def1.IsEqual(def2), Is.False);
        }

        [Test]
        public void IsEqualDifferentStructureTypeReturnsFalse()
        {
            StructureDefinition def1 = CreatePopulatedDefinition();
            StructureDefinition def2 = CreatePopulatedDefinition();
            def2.StructureType = StructureType.Union;

            Assert.That(def1.IsEqual(def2), Is.False);
        }

        [Test]
        public void IsEqualDifferentFieldsReturnsFalse()
        {
            StructureDefinition def1 = CreatePopulatedDefinition();
            StructureDefinition def2 = CreatePopulatedDefinition();
            def2.Fields = new StructureField[] {
                CreatePopulatedField("DifferentField")
            }.ToArrayOf();

            Assert.That(def1.IsEqual(def2), Is.False);
        }

        [Test]
        public void IsEqualWithBothFieldsNullReturnsTrue()
        {
            var def1 = new StructureDefinition();
            var def2 = new StructureDefinition();

            Assert.That(def1.IsEqual(def2), Is.True);
        }

        [Test]
        public void EqualsObjectWithEqualObjectReturnsTrue()
        {
            StructureDefinition def1 = CreatePopulatedDefinition();
            object def2 = CreatePopulatedDefinition();

            Assert.That(def1.Equals(def2), Is.True);
        }

        [Test]
        public void EqualsObjectWithNonEqualObjectReturnsFalse()
        {
            StructureDefinition def1 = CreatePopulatedDefinition();
            StructureDefinition def2 = CreatePopulatedDefinition();
            def2.StructureType = StructureType.Union;

            Assert.That(def1.Equals((object)def2), Is.False);
        }

        [Test]
        public void EqualsObjectWithNullReturnsFalse()
        {
            StructureDefinition definition = CreatePopulatedDefinition();

            Assert.That(definition.Equals((object)null), Is.False);
        }

        [Test]
        public void EqualsObjectWithNonStructureDefinitionReturnsFalse()
        {
            StructureDefinition definition = CreatePopulatedDefinition();

            Assert.That(definition.Equals("not a StructureDefinition"), Is.False);
        }

        [Test]
        public void EqualsStructureDefinitionWithEqualReturnsTrue()
        {
            StructureDefinition def1 = CreatePopulatedDefinition();
            StructureDefinition def2 = CreatePopulatedDefinition();

            Assert.That(def1.Equals(def2), Is.True);
        }

        [Test]
        public void EqualsStructureDefinitionWithNonEqualReturnsFalse()
        {
            StructureDefinition def1 = CreatePopulatedDefinition();
            StructureDefinition def2 = CreatePopulatedDefinition();
            def2.BaseDataType = DataTypeIds.Int32;

            Assert.That(def1.Equals(def2), Is.False);
        }

        [Test]
        public void EqualsStructureDefinitionWithNullReturnsFalse()
        {
            StructureDefinition definition = CreatePopulatedDefinition();

#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(definition.Equals((StructureDefinition)null), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void GetHashCodeSameInstanceReturnsSameHash()
        {
            StructureDefinition definition = CreatePopulatedDefinition();
            int hash1 = definition.GetHashCode();
            int hash2 = definition.GetHashCode();

            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void GetHashCodeDefaultInstanceDoesNotThrow()
        {
            var definition = new StructureDefinition();

            Assert.DoesNotThrow(() => definition.GetHashCode());
        }

        [Test]
        public void OperatorEqualWithEqualObjectsReturnsTrue()
        {
            StructureDefinition def1 = CreatePopulatedDefinition();
            StructureDefinition def2 = CreatePopulatedDefinition();

            Assert.That(def1 == def2, Is.True);
        }

        [Test]
        public void OperatorEqualWithBothNullReturnsTrue()
        {
            StructureDefinition def1 = null;
            StructureDefinition def2 = null;

            Assert.That(def1 == def2, Is.True);
        }

        [Test]
        public void OperatorEqualWithOneNullReturnsFalse()
        {
            StructureDefinition def1 = CreatePopulatedDefinition();
            StructureDefinition def2 = null;

            Assert.That(def1 == def2, Is.False);
            Assert.That(def2 == def1, Is.False);
        }

        [Test]
        public void OperatorNotEqualWithDifferentValuesReturnsTrue()
        {
            StructureDefinition def1 = CreatePopulatedDefinition();
            StructureDefinition def2 = CreatePopulatedDefinition();
            def2.DefaultEncodingId = new NodeId(999);

            Assert.That(def1 != def2, Is.True);
        }

        [Test]
        public void OperatorNotEqualWithEqualValuesReturnsFalse()
        {
            StructureDefinition def1 = CreatePopulatedDefinition();
            StructureDefinition def2 = CreatePopulatedDefinition();

            Assert.That(def1 != def2, Is.False);
        }

        [Test]
        public void CloneProducesIndependentCopy()
        {
            StructureDefinition original = CreatePopulatedDefinition();

            var clone = (StructureDefinition)original.Clone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.IsEqual(original), Is.True);

            clone.DefaultEncodingId = new NodeId(999);
            Assert.That(original.DefaultEncodingId, Is.EqualTo(new NodeId(100)));
        }

        [Test]
        public void MemberwiseCloneDeepCopiesAllFields()
        {
            StructureDefinition original = CreatePopulatedDefinition();

            var clone = (StructureDefinition)original.MemberwiseClone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.DefaultEncodingId, Is.EqualTo(original.DefaultEncodingId));
            Assert.That(clone.BaseDataType, Is.EqualTo(original.BaseDataType));
            Assert.That(clone.StructureType, Is.EqualTo(original.StructureType));
            Assert.That(clone.Fields, Is.EqualTo(original.Fields));
            Assert.That(clone.FirstExplicitFieldIndex, Is.EqualTo(original.FirstExplicitFieldIndex));

            // Verify independence: modifying clone does not affect original
            clone.DefaultEncodingId = new NodeId(999);
            clone.BaseDataType = DataTypeIds.Int32;
            clone.StructureType = StructureType.Union;

            Assert.That(original.DefaultEncodingId, Is.EqualTo(new NodeId(100)));
            Assert.That(original.BaseDataType, Is.EqualTo(DataTypeIds.Structure));
            Assert.That(original.StructureType, Is.EqualTo(StructureType.Structure));
        }

        [Test]
        public void CloneWithDefaultValuesProducesEqualCopy()
        {
            var original = new StructureDefinition();

            var clone = (StructureDefinition)original.Clone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.IsEqual(original), Is.True);
        }

        [Test]
        public void SetDefaultEncodingIdWithNullContextThrowsArgumentNullException()
        {
            var definition = new StructureDefinition();
            var typeId = new NodeId(1);
            var dataEncoding = new QualifiedName(BrowseNames.DefaultBinary);

            Assert.Throws<ArgumentNullException>(
                () => definition.SetDefaultEncodingId(null, typeId, dataEncoding));
        }


        [Test]
        public void SetDefaultEncodingIdWithDefaultBinarySetsFromFactory()
        {
            var definition = new StructureDefinition();
            var typeId = new NodeId(42);
            var dataEncoding = new QualifiedName(BrowseNames.DefaultBinary);

            var namespaceUris = new NamespaceTable();
            var expectedBinaryEncodingId = new ExpandedNodeId(200);

            var mockEncodeable = new Mock<IEncodeable>();
            mockEncodeable.Setup(e => e.BinaryEncodingId).Returns(expectedBinaryEncodingId);

            var mockType = new Mock<IEncodeableType>();
            mockType.Setup(t => t.CreateInstance()).Returns(mockEncodeable.Object);

            IEncodeableType outType = mockType.Object;
            var mockFactory = new Mock<IEncodeableFactory>();
            mockFactory
                .Setup(f => f.TryGetEncodeableType(It.IsAny<ExpandedNodeId>(), out outType))
                .Returns(true);

            var contextMock = new Mock<ISystemContext>();
            contextMock.Setup(c => c.NamespaceUris).Returns(namespaceUris);
            contextMock.Setup(c => c.EncodeableFactory).Returns(mockFactory.Object);

            definition.SetDefaultEncodingId(contextMock.Object, typeId, dataEncoding);

            var expected = ExpandedNodeId.ToNodeId(expectedBinaryEncodingId, namespaceUris);
            Assert.That(definition.DefaultEncodingId, Is.EqualTo(expected));
        }

        [Test]
        public void SetDefaultEncodingIdWithDefaultXmlSetsFromFactory()
        {
            var definition = new StructureDefinition();
            var typeId = new NodeId(42);
            var dataEncoding = new QualifiedName(BrowseNames.DefaultXml);

            var namespaceUris = new NamespaceTable();
            var expectedXmlEncodingId = new ExpandedNodeId(300);

            var mockEncodeable = new Mock<IEncodeable>();
            mockEncodeable.Setup(e => e.XmlEncodingId).Returns(expectedXmlEncodingId);

            var mockType = new Mock<IEncodeableType>();
            mockType.Setup(t => t.CreateInstance()).Returns(mockEncodeable.Object);

            IEncodeableType outType = mockType.Object;
            var mockFactory = new Mock<IEncodeableFactory>();
            mockFactory
                .Setup(f => f.TryGetEncodeableType(It.IsAny<ExpandedNodeId>(), out outType))
                .Returns(true);

            var contextMock = new Mock<ISystemContext>();
            contextMock.Setup(c => c.NamespaceUris).Returns(namespaceUris);
            contextMock.Setup(c => c.EncodeableFactory).Returns(mockFactory.Object);

            definition.SetDefaultEncodingId(contextMock.Object, typeId, dataEncoding);

            var expected = ExpandedNodeId.ToNodeId(expectedXmlEncodingId, namespaceUris);
            Assert.That(definition.DefaultEncodingId, Is.EqualTo(expected));
        }

        [Test]
        public void SetDefaultEncodingIdWithNullDataEncodingSetsFromBinaryFactory()
        {
            var definition = new StructureDefinition();
            var typeId = new NodeId(42);
            QualifiedName dataEncoding = QualifiedName.Null;

            var namespaceUris = new NamespaceTable();
            var expectedBinaryEncodingId = new ExpandedNodeId(200);

            var mockEncodeable = new Mock<IEncodeable>();
            mockEncodeable.Setup(e => e.BinaryEncodingId).Returns(expectedBinaryEncodingId);

            var mockType = new Mock<IEncodeableType>();
            mockType.Setup(t => t.CreateInstance()).Returns(mockEncodeable.Object);

            IEncodeableType outType = mockType.Object;
            var mockFactory = new Mock<IEncodeableFactory>();
            mockFactory
                .Setup(f => f.TryGetEncodeableType(It.IsAny<ExpandedNodeId>(), out outType))
                .Returns(true);

            var contextMock = new Mock<ISystemContext>();
            contextMock.Setup(c => c.NamespaceUris).Returns(namespaceUris);
            contextMock.Setup(c => c.EncodeableFactory).Returns(mockFactory.Object);

            definition.SetDefaultEncodingId(contextMock.Object, typeId, dataEncoding);

            var expected = ExpandedNodeId.ToNodeId(expectedBinaryEncodingId, namespaceUris);
            Assert.That(definition.DefaultEncodingId, Is.EqualTo(expected));
        }

        [Test]
        public void SetDefaultEncodingIdWithUnknownTypeDoesNotSetEncodingId()
        {
            var definition = new StructureDefinition();
            NodeId originalEncodingId = definition.DefaultEncodingId;
            var typeId = new NodeId(42);
            var dataEncoding = new QualifiedName(BrowseNames.DefaultBinary);

            var namespaceUris = new NamespaceTable();

            IEncodeableType outType = null;
            var mockFactory = new Mock<IEncodeableFactory>();
            mockFactory
                .Setup(f => f.TryGetEncodeableType(It.IsAny<ExpandedNodeId>(), out outType))
                .Returns(false);

            var contextMock = new Mock<ISystemContext>();
            contextMock.Setup(c => c.NamespaceUris).Returns(namespaceUris);
            contextMock.Setup(c => c.EncodeableFactory).Returns(mockFactory.Object);

            definition.SetDefaultEncodingId(contextMock.Object, typeId, dataEncoding);

            Assert.That(definition.DefaultEncodingId, Is.EqualTo(originalEncodingId));
        }

        private static StructureField CreatePopulatedField(string name = "TestField")
        {
            return new StructureField
            {
                Name = name,
                Description = new LocalizedText("en-US", "A test field"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.OneDimension,
                ArrayDimensions = new uint[] { 10 }.ToArrayOf(),
                MaxStringLength = 256,
                IsOptional = true
            };
        }

        private static StructureDefinition CreatePopulatedDefinition()
        {
            return new StructureDefinition
            {
                DefaultEncodingId = new NodeId(100),
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = new StructureField[] {
                    CreatePopulatedField("Field1"),
                    CreatePopulatedField("Field2")
                }.ToArrayOf(),
                FirstExplicitFieldIndex = 1
            };
        }
    }
}
