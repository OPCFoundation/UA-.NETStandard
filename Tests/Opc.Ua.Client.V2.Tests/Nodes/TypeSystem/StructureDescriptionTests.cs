// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using FluentAssertions;
    using Moq;
    using System;
    using System.Collections.Generic;
    using System.Xml;
    using Xunit;

    public class StructureDescriptionTests
    {
        private readonly Mock<IDataTypeDescriptionResolver> _mockedTypeSystem;

        public StructureDescriptionTests() => _mockedTypeSystem = new Mock<IDataTypeDescriptionResolver>();

        [Fact]
        public void StructureDescriptionDecodeJsonShouldReturnValues()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Structure,
                Fields = new StructureFieldCollection(structureFields)
            };

            var structureDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonDecoder = new JsonDecoder("""{"Field1": 1, "Field2": 2}""", new ServiceMessageContext());

            // Act
            var result = structureDescription.Decode(jsonDecoder);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            Assert.NotNull(result);
            result[0].Should().Be(1);
            result[1].Should().Be(2);
        }

        [Fact]
        public void StructureDescriptionDecodeShouldReturnValues()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Structure,
                Fields = new StructureFieldCollection(structureFields)
            };

            var structureDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryDecoder = new BinaryDecoder([1, 0, 0, 0, 2, 0, 0, 0], new ServiceMessageContext());

            // Act
            var result = structureDescription.Decode(binaryDecoder);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            Assert.NotNull(result);
            result[0].Should().Be(1);
            result[1].Should().Be(2);
        }

        [Fact]
        public void StructureDescriptionEncodeJsonShouldEncodeValuesInReversibleEncoding()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Structure,
                Fields = new StructureFieldCollection(structureFields)
            };
            var structureDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(new ServiceMessageContext(), true);
            var values = new object[] { 1u, 2u };

            // Act
            structureDescription.Encode(jsonEncoder, values);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            json.Should().Contain("\"Field1\":1");
            json.Should().Contain("\"Field2\":2");
        }

        [Fact]
        public void StructureDescriptionEncodeShouldEncodeValues()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Structure,
                Fields = new StructureFieldCollection(structureFields)
            };
            var structureDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(new ServiceMessageContext());
            var values = new object[] { 1u, 2u };

            // Act
            structureDescription.Encode(binaryEncoder, values);

            // Assert
            var encodedBytes = binaryEncoder.CloseAndReturnBuffer();
            encodedBytes.Should().Equal([1, 0, 0, 0, 2, 0, 0, 0]);
        }

        [Fact]
        public void StructureDescriptionEncodeWithInvalidValuesShouldThrowException()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Structure,
                Fields = new StructureFieldCollection(structureFields)
            };
            var structureDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(new ServiceMessageContext());
            var values = new object[] { 1u }; // Not enough values

            // Act
            Action act = () => structureDescription.Encode(binaryEncoder, values);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Not enough values for all fields.");
        }

        [Fact]
        public void StructureDescriptionNullInstanceShouldThrowExceptionOnDecode()
        {
            // Arrange
            var nullStructureDescription = StructureDescription.Null;

            // Act
            Action act = () => nullStructureDescription.Decode(null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Data type not found");
        }

        [Fact]
        public void StructureDescriptionNullInstanceShouldThrowExceptionOnEncode()
        {
            // Arrange
            var nullStructureDescription = StructureDescription.Null;

            // Act
            Action act = () => nullStructureDescription.Encode(null!, null);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Data type not found");
        }

        [Fact]
        public void StructureWithOptionalFieldsDecodeJsonShouldReturnValues()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = new StructureFieldCollection(structureFields)
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonDecoder = new JsonDecoder("""{"EncodingMask": 1, "Field2": 2}""", new ServiceMessageContext());

            // Act
            var result = structureWithOptionalFieldsDescription.Decode(jsonDecoder);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(3);
            Assert.NotNull(result);
            result[0].Should().Be(1u);
            result[1].Should().BeNull();
            result[2].Should().Be(2);
        }

        [Fact]
        public void StructureWithOptionalFieldsDecodeShouldReturnValues()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = new StructureFieldCollection(structureFields)
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryDecoder = new BinaryDecoder([1, 0, 0, 0, 2, 0, 0, 0], new ServiceMessageContext());

            // Act
            var result = structureWithOptionalFieldsDescription.Decode(binaryDecoder);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(3);
            Assert.NotNull(result);
            result[0].Should().Be(1u);
            result[1].Should().BeNull();
            result[2].Should().Be(2);
        }

        [Fact]
        public void StructureWithOptionalFieldsDecodeWithMissingEncodingMaskShouldThrowException()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = new StructureFieldCollection(structureFields)
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryDecoder = new BinaryDecoder([2, 0, 0, 0], new ServiceMessageContext());

            // Act
            Action act = () => structureWithOptionalFieldsDescription.Decode(binaryDecoder);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .Which.Message.Contains("end of stream", StringComparison.InvariantCulture);
        }

        [Fact]
        public void StructureWithOptionalFieldsEncodeJsonShouldEncodeValuesInNonReversibleEncoding()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = new StructureFieldCollection(structureFields)
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(new ServiceMessageContext(), false);
            var values = new object?[] { 1u, null, 2u };

            // Act
            structureWithOptionalFieldsDescription.Encode(jsonEncoder, values);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            json.Should().NotContain("\"EncodingMask\":1");
            json.Should().Contain("\"Field2\":2");
        }

        [Fact]
        public void StructureWithOptionalFieldsEncodeJsonShouldEncodeValuesInReversibleEncoding()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = new StructureFieldCollection(structureFields)
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(new ServiceMessageContext(), true);
            var values = new object?[] { 1u, null, 2u };

            // Act
            structureWithOptionalFieldsDescription.Encode(jsonEncoder, values);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            json.Should().Contain("\"EncodingMask\":1");
            json.Should().Contain("\"Field2\":2");
        }

        [Fact]
        public void StructureWithOptionalFieldsEncodeJsonWithMissingEncodingMaskShouldThrowException()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = new StructureFieldCollection(structureFields)
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(new ServiceMessageContext(), false);
            var values = new object?[] { null, null, 2u }; // Missing encoding mask

            // Act
            Action act = () => structureWithOptionalFieldsDescription.Encode(jsonEncoder, values);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Encoding mask missing or less values than expected");
        }

        [Fact]
        public void StructureWithOptionalFieldsEncodeShouldEncodeValues()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = new StructureFieldCollection(structureFields)
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(new ServiceMessageContext());
            var values = new object?[] { 1u, null, 2u };

            // Act
            structureWithOptionalFieldsDescription.Encode(binaryEncoder, values);

            // Assert
            var encodedBytes = binaryEncoder.CloseAndReturnBuffer();
            encodedBytes.Should().Equal([1, 0, 0, 0, 2, 0, 0, 0]);
        }

        [Fact]
        public void StructureWithOptionalFieldsEncodeWithMissingEncodingMaskShouldThrowException()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = new StructureFieldCollection(structureFields)
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(new ServiceMessageContext());
            var values = new object?[] { null, null, 2u };

            // Act
            Action act = () => structureWithOptionalFieldsDescription.Encode(binaryEncoder, values);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Encoding mask missing or less values than expected");
        }

        [Fact]
        public void UnionDescriptionDecodeJsonShouldReturnValues()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field3", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field4", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = new StructureFieldCollection(structureFields)
            };
            var unionDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName("ssss"), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            unionDescription.StructureDefinition.StructureType.Should().Be(StructureType.Union);
            unionDescription.XmlName.Name.Should().Be("ssss");
            unionDescription.BinaryEncodingId.Should().Be(ExpandedNodeId.Null);
            unionDescription.JsonEncodingId.Should().Be(ExpandedNodeId.Null);
            unionDescription.XmlEncodingId.Should().Be(ExpandedNodeId.Null);
            unionDescription.TypeId.Should().Be(new ExpandedNodeId(333));
            unionDescription.IsAbstract.Should().BeFalse();
            unionDescription.FieldsCanHaveSubtypedValues.Should().BeFalse();

            var jsonDecoder = new JsonDecoder("""{"SwitchField": 1, "Value": 3}""", new ServiceMessageContext());

            // Act
            var result = unionDescription.Decode(jsonDecoder);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            Assert.NotNull(result);
            result[0].Should().Be(1u);
            result[1].Should().Be(3u);
        }

        [Fact]
        public void UnionDescriptionDecodeJsonWithNullValueShouldReturnNull()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = new StructureFieldCollection(structureFields)
            };
            var unionDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonDecoder = new JsonDecoder("""{"SwitchField": 0, "Value": null}""", new ServiceMessageContext());

            // Act
            var result = unionDescription.Decode(jsonDecoder);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            Assert.NotNull(result);
            result[0].Should().Be(0u);
            result[1].Should().BeNull();
        }

        [Fact]
        public void UnionDescriptionDecodeShouldReturnValues()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field3", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field4", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.UnionWithSubtypedValues,
                Fields = new StructureFieldCollection(structureFields)
            };
            var unionDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName("ssss"), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            unionDescription.StructureDefinition.StructureType.Should().Be(StructureType.UnionWithSubtypedValues);
            unionDescription.XmlName.Name.Should().Be("ssss");
            unionDescription.BinaryEncodingId.Should().Be(ExpandedNodeId.Null);
            unionDescription.JsonEncodingId.Should().Be(ExpandedNodeId.Null);
            unionDescription.XmlEncodingId.Should().Be(ExpandedNodeId.Null);
            unionDescription.TypeId.Should().Be(new ExpandedNodeId(333));
            unionDescription.IsAbstract.Should().BeFalse();
            unionDescription.FieldsCanHaveSubtypedValues.Should().BeTrue();

            var binaryDecoder = new BinaryDecoder([1, 0, 0, 0, 3, 0, 0, 0], new ServiceMessageContext());

            // Act
            var result = unionDescription.Decode(binaryDecoder);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            Assert.NotNull(result);
            result[0].Should().Be(1u);
            result[1].Should().Be(3u);
        }

        [Fact]
        public void UnionDescriptionDecodeWithInvalidSwitchFieldShouldThrowException()
        {
            // Arrange
            var structureFields = new List<StructureField>
        {
            new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
            new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
        };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = new StructureFieldCollection(structureFields)
            };
            var unionDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryDecoder = new BinaryDecoder([3, 0, 0, 0], new ServiceMessageContext());

            // Act
            Action act = () => unionDescription.Decode(binaryDecoder);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Union selector out of range");
        }

        [Fact]
        public void UnionDescriptionDecodeWithNullValueShouldReturnNull()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = new StructureFieldCollection(structureFields)
            };
            var unionDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryDecoder = new BinaryDecoder([0, 0, 0, 0], new ServiceMessageContext());

            // Act
            var result = unionDescription.Decode(binaryDecoder);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            Assert.NotNull(result);
            result[0].Should().Be(0u);
            result[1].Should().BeNull();
        }

        [Fact]
        public void UnionDescriptionEncodeJsonShouldEncodeValuesInNonReversibleEncoding()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field3", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field4", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = new StructureFieldCollection(structureFields)
            };
            var unionDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(new ServiceMessageContext(), false);
            var values = new object?[] { 2u, 3u };

            // Act
            unionDescription.Encode(jsonEncoder, values);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            json.Should().NotContain("\"SwitchField\":1");
            json.Should().Contain("\"Field2\":3");
        }

        [Fact]
        public void UnionDescriptionEncodeJsonShouldEncodeValuesInReversibleEncoding()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field3", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field4", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = new StructureFieldCollection(structureFields)
            };
            var unionDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(new ServiceMessageContext(), true);
            var values = new object?[] { 1u, 3u };

            // Act
            unionDescription.Encode(jsonEncoder, values);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            json.Should().Contain("\"SwitchField\":1");
            json.Should().Contain("\"Value\":3");
        }

        [Fact]
        public void UnionDescriptionEncodeJsonWithInvalidSwitchFieldShouldThrowException()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = new StructureFieldCollection(structureFields)
            };
            var unionDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(new ServiceMessageContext(), false);
            var values = new object?[] { 3u, 3u }; // Invalid switch field

            // Act
            Action act = () => unionDescription.Encode(jsonEncoder, values);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Union selector out of range");
        }

        [Fact]
        public void UnionDescriptionEncodeJsonWithNullValueShouldEncodeCorrectlyWithNonReversibleEncoding()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = new StructureFieldCollection(structureFields)
            };
            var unionDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(new ServiceMessageContext(), false);
            var values = new object?[] { 0u, null };

            // Act
            unionDescription.Encode(jsonEncoder, values);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            json.Should().Be("{null}");
        }

        [Fact]
        public void UnionDescriptionEncodeJsonWithNullValueShouldEncodeCorrectlyWithReversibleEncoding()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = new StructureFieldCollection(structureFields)
            };
            var unionDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(new ServiceMessageContext(), true);
            var values = new object?[] { 0u, null };

            // Act
            unionDescription.Encode(jsonEncoder, values);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            json.Should().Be("""{"SwitchField":0}""");
        }

        [Fact]
        public void UnionDescriptionEncodeShouldEncodeValues()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field3", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field4", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = new StructureFieldCollection(structureFields)
            };
            var unionDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(new ServiceMessageContext());
            var values = new object?[] { 1u, 3u };

            // Act
            unionDescription.Encode(binaryEncoder, values);

            // Assert
            var encodedBytes = binaryEncoder.CloseAndReturnBuffer();
            encodedBytes.Should().Equal([1, 0, 0, 0, 3, 0, 0, 0]);
        }

        [Fact]
        public void UnionDescriptionEncodeWithInvalidSwitchFieldShouldThrowException()
        {
            // Arrange
            var structureFields = new List<StructureField>
        {
            new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
            new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
        };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = new StructureFieldCollection(structureFields)
            };
            var unionDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(new ServiceMessageContext());
            var values = new object?[] { 3u, 3u };

            // Act
            Action act = () => unionDescription.Encode(binaryEncoder, values);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Union selector out of range");
        }
        [Fact]
        public void UnionDescriptionEncodeWithNullValueShouldEncodeCorrectly()
        {
            // Arrange
            var structureFields = new List<StructureField>
            {
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = new StructureFieldCollection(structureFields)
            };
            var unionDescription = StructureDescription.Create(
                _mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(new ServiceMessageContext());
            var values = new object?[] { 0u, null };

            // Act
            unionDescription.Encode(binaryEncoder, values);

            // Assert
            var encodedBytes = binaryEncoder.CloseAndReturnBuffer();
            encodedBytes.Should().Equal([0, 0, 0, 0]);
        }
    }
}
