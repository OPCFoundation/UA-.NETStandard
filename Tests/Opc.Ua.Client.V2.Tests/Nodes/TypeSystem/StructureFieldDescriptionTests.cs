// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using Moq;
using Opc.Ua;
using System;
using System.Globalization;
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    [TestFixture]
    public class StructureFieldDescriptionTests
    {
        [SetUp]
        public void SetUp()
        {
            m_mockEncoder = new Mock<IEncoder>();
            m_mockDecoder = new Mock<IDecoder>();
            m_mockDataTypeSystem = new Mock<IDataTypeDescriptionResolver>();
        }

        [Test]
        public void EncodeBooleanScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Boolean), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const bool value = true;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteBoolean("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeBooleanScalarValueWithFieldNameTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Boolean), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const bool value = true;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value, "Value");

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteBoolean("Value", value), Times.Once);
        }

        [Test]
        public void EncodeSByteScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.SByte), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const sbyte value = 1;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteSByte("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeByteScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Byte), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const byte value = 1;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteByte("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeInt16ScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Int16), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const short value = 1;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteInt16("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeUInt16ScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.UInt16), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const ushort value = 1;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteUInt16("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeInt32ScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Int32), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const int value = 1;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteInt32("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeUInt32ScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.UInt32), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const uint value = 1;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteUInt32("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeInt64ScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Int64), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const long value = 1;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteInt64("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeUInt64ScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.UInt64), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const ulong value = 1;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteUInt64("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeFloatScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Float), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const float value = 1.0f;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteFloat("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeDoubleScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Double), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const double value = 1.0;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteDouble("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeStringScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.String), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const string value = "test";

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteString("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeDateTimeScalarValue1Test()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.DateTime), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = DateTime.Parse("2023-10-10T00:00:00Z", CultureInfo.InvariantCulture);

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteDateTime("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeDateTimeScalarValue2Test()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.DateTime), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, null);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteDateTime("Field1", DateTime.MinValue), Times.Once);
        }

        [Test]
        public void EncodeGuidScalarValue1Test()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Guid), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = Guid.NewGuid();

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteGuid("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeGuidScalarValue2Test()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Guid), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = new Uuid(Guid.NewGuid());

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteGuid("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeGuidScalarValue3Test()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Guid), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = Guid.Empty;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, null);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteGuid("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeByteStringScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.ByteString), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = new byte[] { 0x01, 0x02 };

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteByteString("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeXmlElementScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.XmlElement), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = new XmlDocument().CreateElement("test");

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteXmlElement("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeNodeIdScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.NodeId), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = NodeId.Parse("ns=1;i=1234");

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteNodeId("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeExpandedNodeIdScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.ExpandedNodeId), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = ExpandedNodeId.Parse("ns=1;i=1234");

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteExpandedNodeId("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeStatusCodeScalarValue1Test()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.StatusCode), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const uint value = StatusCodes.Good;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteStatusCode("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeStatusCodeScalarValue2Test()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.StatusCode), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            StatusCode value = StatusCodes.Good;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteStatusCode("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeStatusCodeScalarValue3Test()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.StatusCode), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, null);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteStatusCode("Field1", (StatusCode)StatusCodes.Good), Times.Once);
        }

        [Test]
        public void EncodeDiagnosticInfoScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.DiagnosticInfo), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = new DiagnosticInfo();

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteDiagnosticInfo("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeQualifiedNameScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.QualifiedName), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = new QualifiedName("test");

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteQualifiedName("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeLocalizedTextScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.LocalizedText), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = new LocalizedText("test");

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteLocalizedText("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeDataValueScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.DataValue), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = new DataValue();

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteDataValue("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeVariantScalarValue1Test()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Variant), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = new Variant();

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteVariant("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeVariantScalarValue2Test()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Variant), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, null);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteVariant("Field1", Variant.Null), Times.Once);
        }

        [Test]
        public void EncodeExtensionObjectScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.ExtensionObject), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = new ExtensionObject();

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteExtensionObject("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeEnumerationScalarValue1Test()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Enumeration), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const int value = 1;

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteInt32("Field1", value), Times.Once);
        }

        [Test]
        public void EncodeEnumerationScalarValue2Test()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Enumeration), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var value = new EnumValue("test", 1);

            m_mockDataTypeSystem.Setup(d => d.GetEnumDescription(It.IsAny<ExpandedNodeId>()))
                .Returns(new EnumDescription(new ExpandedNodeId(333), new EnumDefinition
                {
                    Fields =
                    [
                        new EnumField { Name = "test", Value = 1 }
                    ]
                }, new XmlQualifiedName(), ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null));

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
            m_mockEncoder.Verify(e => e.WriteInt32("Field1", 1), Times.Once);
        }

        [Test]
        public void EncodeNullScalarTypeThrowsException()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Null), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, new object());

            // Assert
            Assert.Throws<ServiceResultException>(() => act());
        }

        [Test]
        public void EncodeWithUnknownValueRankThrowsException()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Null), ValueRank = -203 };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, new object());

            // Assert
            Assert.Throws<ServiceResultException>(() => act());
        }

        
        [TestCase(BuiltInType.Boolean, true)]
        [TestCase(BuiltInType.SByte, (sbyte)1)]
        [TestCase(BuiltInType.Byte, (byte)1)]
        [TestCase(BuiltInType.Int16, (short)1)]
        [TestCase(BuiltInType.UInt16, (ushort)1)]
        [TestCase(BuiltInType.Int32, 1)]
        [TestCase(BuiltInType.UInt32, (uint)1)]
        [TestCase(BuiltInType.Int64, (long)1)]
        [TestCase(BuiltInType.UInt64, (ulong)1)]
        [TestCase(BuiltInType.Float, 1.0f)]
        [TestCase(BuiltInType.Double, 1.0)]
        [TestCase(BuiltInType.String, "test")]
        [TestCase(BuiltInType.ByteString, new byte[] { 0x01, 0x02 })]
        [TestCase(BuiltInType.StatusCode, StatusCodes.Good)]
        [TestCase(BuiltInType.Enumeration, 1)]
        public void EncodeValidArrayBuiltInTypesDoesNotThrow(BuiltInType builtInType, object? value)
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)builtInType), ValueRank = ValueRanks.OneDimension };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value);

            // Assert
            Assert.DoesNotThrow(() => act());
        }

        
        [TestCase(BuiltInType.Boolean, true)]
        [TestCase(BuiltInType.SByte, (sbyte)1)]
        [TestCase(BuiltInType.Byte, (byte)1)]
        [TestCase(BuiltInType.Int16, (short)1)]
        [TestCase(BuiltInType.UInt16, (ushort)1)]
        [TestCase(BuiltInType.Int32, 1)]
        [TestCase(BuiltInType.UInt32, (uint)1)]
        [TestCase(BuiltInType.Int64, (long)1)]
        [TestCase(BuiltInType.UInt64, (ulong)1)]
        [TestCase(BuiltInType.Float, 1.0f)]
        [TestCase(BuiltInType.Double, 1.0)]
        [TestCase(BuiltInType.String, "test")]
        [TestCase(BuiltInType.ByteString, new byte[] { 0x01, 0x02 })]
        [TestCase(BuiltInType.StatusCode, StatusCodes.Good)]
        [TestCase(BuiltInType.Enumeration, 1)]
        public void EncodeValidArrayBuiltInTypesWithFieldNameOverride(BuiltInType builtInType, object? value)
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)builtInType), ValueRank = ValueRanks.OneDimension };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);

            // Act
            Action act = () => description.Encode(m_mockEncoder.Object, value, "Value");

            // Assert
            Assert.DoesNotThrow(() => act());
        }
        [Test]
        public void DecodeBooleanScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Boolean), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            m_mockDecoder.Setup(d => d.ReadBoolean("Field1")).Returns(true);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(true));
            m_mockDecoder.Verify(d => d.ReadBoolean("Field1"), Times.Once);
        }

        [Test]
        public void DecodeSByteScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.SByte), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            m_mockDecoder.Setup(d => d.ReadSByte("Field1")).Returns(1);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo((sbyte))1);
            m_mockDecoder.Verify(d => d.ReadSByte("Field1"), Times.Once);
        }

        [Test]
        public void DecodeByteScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Byte), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            m_mockDecoder.Setup(d => d.ReadByte("Field1")).Returns(1);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo((byte))1);
            m_mockDecoder.Verify(d => d.ReadByte("Field1"), Times.Once);
        }

        [Test]
        public void DecodeInt16ScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Int16), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            m_mockDecoder.Setup(d => d.ReadInt16("Field1")).Returns(1);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo((short))1);
            m_mockDecoder.Verify(d => d.ReadInt16("Field1"), Times.Once);
        }

        [Test]
        public void DecodeUInt16ScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.UInt16), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            m_mockDecoder.Setup(d => d.ReadUInt16("Field1")).Returns(1);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo((ushort))1);
            m_mockDecoder.Verify(d => d.ReadUInt16("Field1"), Times.Once);
        }

        [Test]
        public void DecodeInt32ScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Int32), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            m_mockDecoder.Setup(d => d.ReadInt32("Field1")).Returns(1);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(1));
            m_mockDecoder.Verify(d => d.ReadInt32("Field1"), Times.Once);
        }

        [Test]
        public void DecodeUInt32ScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.UInt32), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            m_mockDecoder.Setup(d => d.ReadUInt32("Field1")).Returns(1);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo((uint))1);
            m_mockDecoder.Verify(d => d.ReadUInt32("Field1"), Times.Once);
        }

        [Test]
        public void DecodeInt64ScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Int64), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            m_mockDecoder.Setup(d => d.ReadInt64("Field1")).Returns(1);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo((long))1);
            m_mockDecoder.Verify(d => d.ReadInt64("Field1"), Times.Once);
        }

        [Test]
        public void DecodeUInt64ScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.UInt64), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            m_mockDecoder.Setup(d => d.ReadUInt64("Field1")).Returns(1);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo((ulong))1);
            m_mockDecoder.Verify(d => d.ReadUInt64("Field1"), Times.Once);
        }

        [Test]
        public void DecodeFloatScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Float), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            m_mockDecoder.Setup(d => d.ReadFloat("Field1")).Returns(1.0f);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(1.0f));
            m_mockDecoder.Verify(d => d.ReadFloat("Field1"), Times.Once);
        }

        [Test]
        public void DecodeDoubleScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Double), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            m_mockDecoder.Setup(d => d.ReadDouble("Field1")).Returns(1.0);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(1.0));
            m_mockDecoder.Verify(d => d.ReadDouble("Field1"), Times.Once);
        }

        [Test]
        public void DecodeStringScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.String), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            m_mockDecoder.Setup(d => d.ReadString("Field1")).Returns("test");

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo("test"));
            m_mockDecoder.Verify(d => d.ReadString("Field1"), Times.Once);
        }

        [Test]
        public void DecodeDateTimeScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.DateTime), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var expectedValue = DateTime.Parse("2023-10-10T00:00:00Z", CultureInfo.InvariantCulture);
            m_mockDecoder.Setup(d => d.ReadDateTime("Field1")).Returns(expectedValue);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
            m_mockDecoder.Verify(d => d.ReadDateTime("Field1"), Times.Once);
        }

        [Test]
        public void DecodeGuidScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Guid), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var expectedValue = Guid.NewGuid();
            m_mockDecoder.Setup(d => d.ReadGuid("Field1")).Returns(new Uuid(expectedValue));

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
            m_mockDecoder.Verify(d => d.ReadGuid("Field1"), Times.Once);
        }

        [Test]
        public void DecodeByteStringScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.ByteString), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var expectedValue = new byte[] { 0x01, 0x02 };
            m_mockDecoder.Setup(d => d.ReadByteString("Field1")).Returns(expectedValue);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
            m_mockDecoder.Verify(d => d.ReadByteString("Field1"), Times.Once);
        }

        [Test]
        public void DecodeXmlElementScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.XmlElement), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var expectedValue = new XmlDocument().CreateElement("test");
            m_mockDecoder.Setup(d => d.ReadXmlElement("Field1")).Returns(expectedValue);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
            m_mockDecoder.Verify(d => d.ReadXmlElement("Field1"), Times.Once);
        }

        [Test]
        public void DecodeNodeIdScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.NodeId), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var expectedValue = NodeId.Parse("ns=1;i=1234");
            m_mockDecoder.Setup(d => d.ReadNodeId("Field1")).Returns(expectedValue);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
            m_mockDecoder.Verify(d => d.ReadNodeId("Field1"), Times.Once);
        }

        [Test]
        public void DecodeExpandedNodeIdScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.ExpandedNodeId), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var expectedValue = ExpandedNodeId.Parse("ns=1;i=1234");
            m_mockDecoder.Setup(d => d.ReadExpandedNodeId("Field1")).Returns(expectedValue);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
            m_mockDecoder.Verify(d => d.ReadExpandedNodeId("Field1"), Times.Once);
        }

        [Test]
        public void DecodeStatusCodeScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.StatusCode), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const uint expectedValue = StatusCodes.Good;
            m_mockDecoder.Setup(d => d.ReadStatusCode("Field1")).Returns(expectedValue);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
            m_mockDecoder.Verify(d => d.ReadStatusCode("Field1"), Times.Once);
        }

        [Test]
        public void DecodeDiagnosticInfoScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.DiagnosticInfo), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var expectedValue = new DiagnosticInfo();
            m_mockDecoder.Setup(d => d.ReadDiagnosticInfo("Field1")).Returns(expectedValue);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
            m_mockDecoder.Verify(d => d.ReadDiagnosticInfo("Field1"), Times.Once);
        }

        [Test]
        public void DecodeQualifiedNameScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.QualifiedName), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var expectedValue = new QualifiedName("test");
            m_mockDecoder.Setup(d => d.ReadQualifiedName("Field1")).Returns(expectedValue);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
            m_mockDecoder.Verify(d => d.ReadQualifiedName("Field1"), Times.Once);
        }

        [Test]
        public void DecodeLocalizedTextScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.LocalizedText), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var expectedValue = new LocalizedText("test");
            m_mockDecoder.Setup(d => d.ReadLocalizedText("Field1")).Returns(expectedValue);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
            m_mockDecoder.Verify(d => d.ReadLocalizedText("Field1"), Times.Once);
        }

        [Test]
        public void DecodeDataValueScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.DataValue), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var expectedValue = new DataValue();
            m_mockDecoder.Setup(d => d.ReadDataValue("Field1")).Returns(expectedValue);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
            m_mockDecoder.Verify(d => d.ReadDataValue("Field1"), Times.Once);
        }

        [Test]
        public void DecodeVariantScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Variant), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var expectedValue = new Variant();
            m_mockDecoder.Setup(d => d.ReadVariant("Field1")).Returns(expectedValue);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
            m_mockDecoder.Verify(d => d.ReadVariant("Field1"), Times.Once);
        }

        [Test]
        public void DecodeExtensionObjectScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.ExtensionObject), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var expectedValue = new ExtensionObject();
            m_mockDecoder.Setup(d => d.ReadExtensionObject("Field1")).Returns(expectedValue);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
            m_mockDecoder.Verify(d => d.ReadExtensionObject("Field1"), Times.Once);
        }

        [Test]
        public void DecodeEnumerationScalarValueTest()
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)BuiltInType.Enumeration), ValueRank = ValueRanks.Scalar };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            const int expectedValue = 1;
            m_mockDecoder.Setup(d => d.ReadInt32("Field1")).Returns(expectedValue);

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
            m_mockDecoder.Verify(d => d.ReadInt32("Field1"), Times.Once);
        }

        
        [TestCase(BuiltInType.Boolean, new bool[] { true, false })]
        [TestCase(BuiltInType.SByte, new sbyte[] { 1, 2 })]
        [TestCase(BuiltInType.Byte, new byte[] { 1, 2 })]
        [TestCase(BuiltInType.Int16, new short[] { 1, 2 })]
        [TestCase(BuiltInType.UInt16, new ushort[] { 1, 2 })]
        [TestCase(BuiltInType.Int32, new int[] { 1, 2 })]
        [TestCase(BuiltInType.UInt32, new uint[] { 1, 2 })]
        [TestCase(BuiltInType.Int64, new long[] { 1, 2 })]
        [TestCase(BuiltInType.UInt64, new ulong[] { 1, 2 })]
        [TestCase(BuiltInType.Float, new float[] { 1.0f, 2.0f })]
        [TestCase(BuiltInType.Double, new double[] { 1.0, 2.0 })]
        [TestCase(BuiltInType.String, new string[] { "test1", "test2" })]
        [TestCase(BuiltInType.Enumeration, new int[] { 1, 2 })]
        public void DecodeValidArrayBuiltInTypesDoesNotThrow(BuiltInType builtInType, object value)
        {
            // Arrange
            var field = new StructureField { Name = "Field1", DataType = new NodeId((uint)builtInType), ValueRank = ValueRanks.OneDimension };
            var description = new StructureFieldDescription(m_mockDataTypeSystem.Object, field, false, 0);
            var expectedValue = value;

            var builder = m_mockDecoder.Setup(d => d.ReadArray("Field1", ValueRanks.OneDimension, builtInType, null, null));
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    builder.Returns((bool[])value);
                    break;
                case BuiltInType.SByte:
                    builder.Returns((sbyte[])value);
                    break;
                case BuiltInType.Byte:
                    builder.Returns((byte[])value);
                    break;
                case BuiltInType.Int16:
                    builder.Returns((short[])value);
                    break;
                case BuiltInType.UInt16:
                    builder.Returns((ushort[])value);
                    break;
                case BuiltInType.Int32:
                    builder.Returns((int[])value);
                    break;
                case BuiltInType.UInt32:
                    builder.Returns((uint[])value);
                    break;
                case BuiltInType.Int64:
                    builder.Returns((long[])value);
                    break;
                case BuiltInType.UInt64:
                    builder.Returns((ulong[])value);
                    break;
                case BuiltInType.Float:
                    builder.Returns((float[])value);
                    break;
                case BuiltInType.Double:
                    builder.Returns((double[])value);
                    break;
                case BuiltInType.String:
                    builder.Returns((string[])value);
                    break;
                case BuiltInType.Enumeration:
                    builder.Returns((int[])value);
                    break;
            }

            // Act
            var result = description.Decode(m_mockDecoder.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expectedValue));
        }

        private Mock<IDataTypeDescriptionResolver> m_mockDataTypeSystem;
        private Mock<IEncoder> m_mockEncoder;
        private Mock<IDecoder> m_mockDecoder;
    }
}
