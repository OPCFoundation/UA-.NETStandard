// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

#nullable enable

using Moq;
using System;
using System.Collections.Generic;
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    [TestFixture]
    public class StructureDescriptionTests
    {
        private readonly Mock<IDataTypeDescriptionResolver> m_mockedTypeSystem;

        public StructureDescriptionTests() => m_mockedTypeSystem = new Mock<IDataTypeDescriptionResolver>();

        [Test]
        public void StructureDescriptionDecodeJsonShouldReturnValues()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Structure,
                Fields = structureFields
            };

            var structureDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonDecoder = new JsonDecoder("""{"Field1": 1, "Field2": 2}""", ServiceMessageContext.Create(null));

            // Act
            var result = structureDescription.Decode(jsonDecoder);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Is.Not.Null);
            Assert.That(result[0], Is.EqualTo(1));
            Assert.That(result[1], Is.EqualTo(2));
        }

        [Test]
        public void StructureDescriptionDecodeShouldReturnValues()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Structure,
                Fields = structureFields
            };

            var structureDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryDecoder = new BinaryDecoder([1, 0, 0, 0, 2, 0, 0, 0], ServiceMessageContext.Create(null));

            // Act
            var result = structureDescription.Decode(binaryDecoder);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Is.Not.Null);
            Assert.That(result[0], Is.EqualTo(1));
            Assert.That(result[1], Is.EqualTo(2));
        }

        [Test]
        public void StructureDescriptionEncodeJsonShouldEncodeValuesInReversibleEncoding()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Structure,
                Fields = structureFields
            };
            var structureDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Verbose);
            var values = new object[] { 1u, 2u };

            // Act
            structureDescription.Encode(jsonEncoder, values);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            Assert.That(json, Does.Contain("\"Field1\":1"));
            Assert.That(json, Does.Contain("\"Field2\":2"));
        }

        [Test]
        public void StructureDescriptionEncodeShouldEncodeValues()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Structure,
                Fields = structureFields
            };
            var structureDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(ServiceMessageContext.Create(null));
            var values = new object[] { 1u, 2u };

            // Act
            structureDescription.Encode(binaryEncoder, values);

            // Assert
            var encodedBytes = binaryEncoder.CloseAndReturnBuffer();
            Assert.That(encodedBytes, Is.EqualTo([1, 0, 0, 0, 2, 0, 0, 0]));
        }

        [Test]
        public void StructureDescriptionEncodeWithInvalidValuesShouldThrowException()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Structure,
                Fields = structureFields
            };
            var structureDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(ServiceMessageContext.Create(null));
            var values = new object[] { 1u }; // Not enough values

            // Act
            Action act = () => structureDescription.Encode(binaryEncoder, values);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Not enough values for all fields."));
        }

        [Test]
        public void StructureDescriptionNullInstanceShouldThrowExceptionOnDecode()
        {
            // Arrange
            var nullStructureDescription = StructureDescription.Null;

            // Act
            Action act = () => nullStructureDescription.Decode(null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Data type not found"));
        }

        [Test]
        public void StructureDescriptionNullInstanceShouldThrowExceptionOnEncode()
        {
            // Arrange
            var nullStructureDescription = StructureDescription.Null;

            // Act
            Action act = () => nullStructureDescription.Encode(null!, null);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Data type not found"));
        }

        [Test]
        public void StructureWithOptionalFieldsDecodeJsonShouldReturnValues()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = structureFields
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonDecoder = new JsonDecoder("""{"EncodingMask": 1, "Field2": 2}""", ServiceMessageContext.Create(null));

            // Act
            var result = structureWithOptionalFieldsDescription.Decode(jsonDecoder);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result, Is.Not.Null);
            Assert.That(result[0], Is.EqualTo(1u));
            Assert.That(result[1], Is.Null);
            Assert.That(result[2], Is.EqualTo(2));
        }

        [Test]
        public void StructureWithOptionalFieldsDecodeShouldReturnValues()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = structureFields
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryDecoder = new BinaryDecoder([1, 0, 0, 0, 2, 0, 0, 0], ServiceMessageContext.Create(null));

            // Act
            var result = structureWithOptionalFieldsDescription.Decode(binaryDecoder);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result, Is.Not.Null);
            Assert.That(result[0], Is.EqualTo(1u));
            Assert.That(result[1], Is.Null);
            Assert.That(result[2], Is.EqualTo(2));
        }

        [Test]
        public void StructureWithOptionalFieldsDecodeWithMissingEncodingMaskShouldThrowException()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = structureFields
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryDecoder = new BinaryDecoder([2, 0, 0, 0], ServiceMessageContext.Create(null));

            // Act
            Action act = () => structureWithOptionalFieldsDescription.Decode(binaryDecoder);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Contain("end of stream"));
        }

        [Test]
        public void StructureWithOptionalFieldsEncodeJsonShouldEncodeValuesInNonReversibleEncoding()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = structureFields
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Compact);
            var values = new object?[] { 1u, null, 2u };

            // Act
            structureWithOptionalFieldsDescription.Encode(jsonEncoder, values);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            Assert.That(json, Does.Not.Contain("\"EncodingMask\":1"));
            Assert.That(json, Does.Contain("\"Field2\":2"));
        }

        [Test]
        public void StructureWithOptionalFieldsEncodeJsonShouldEncodeValuesInReversibleEncoding()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = structureFields
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Verbose);
            var values = new object?[] { 1u, null, 2u };

            // Act
            structureWithOptionalFieldsDescription.Encode(jsonEncoder, values);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            Assert.That(json, Does.Contain("\"EncodingMask\":1"));
            Assert.That(json, Does.Contain("\"Field2\":2"));
        }

        [Test]
        public void StructureWithOptionalFieldsEncodeJsonWithMissingEncodingMaskShouldThrowException()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = structureFields
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Compact);
            var values = new object?[] { null, null, 2u }; // Missing encoding mask

            // Act
            Action act = () => structureWithOptionalFieldsDescription.Encode(jsonEncoder, values);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Encoding mask missing or less values than expected"));
        }

        [Test]
        public void StructureWithOptionalFieldsEncodeShouldEncodeValues()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = structureFields
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(ServiceMessageContext.Create(null));
            var values = new object?[] { 1u, null, 2u };

            // Act
            structureWithOptionalFieldsDescription.Encode(binaryEncoder, values);

            // Assert
            var encodedBytes = binaryEncoder.CloseAndReturnBuffer();
            Assert.That(encodedBytes, Is.EqualTo([1, 0, 0, 0, 2, 0, 0, 0]));
        }

        [Test]
        public void StructureWithOptionalFieldsEncodeWithMissingEncodingMaskShouldThrowException()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar, IsOptional = true },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.StructureWithOptionalFields,
                Fields = structureFields
            };
            var structureWithOptionalFieldsDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(ServiceMessageContext.Create(null));
            var values = new object?[] { null, null, 2u };

            // Act
            Action act = () => structureWithOptionalFieldsDescription.Encode(binaryEncoder, values);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Encoding mask missing or less values than expected"));
        }

        [Test]
        public void UnionDescriptionDecodeJsonShouldReturnValues()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field3", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field4", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = structureFields
            };
            var unionDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName("ssss"), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            Assert.That(unionDescription.StructureDefinition.StructureType, Is.EqualTo(StructureType.Union));
            Assert.That(unionDescription.XmlName.Name, Is.EqualTo("ssss"));
            Assert.That(unionDescription.BinaryEncodingId, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(unionDescription.JsonEncodingId, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(unionDescription.XmlEncodingId, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(unionDescription.TypeId, Is.EqualTo(new ExpandedNodeId(333)));
            Assert.That(unionDescription.IsAbstract, Is.False);
            Assert.That(unionDescription.FieldsCanHaveSubtypedValues, Is.False);

            var jsonDecoder = new JsonDecoder("""{"SwitchField": 1, "Value": 3}""", ServiceMessageContext.Create(null));

            // Act
            var result = unionDescription.Decode(jsonDecoder);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Is.Not.Null);
            Assert.That(result[0], Is.EqualTo(1u));
            Assert.That(result[1], Is.EqualTo(3u));
        }

        [Test]
        public void UnionDescriptionDecodeJsonWithNullValueShouldReturnNull()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = structureFields
            };
            var unionDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonDecoder = new JsonDecoder("""{"SwitchField": 0, "Value": null}""", ServiceMessageContext.Create(null));

            // Act
            var result = unionDescription.Decode(jsonDecoder);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Is.Not.Null);
            Assert.That(result[0], Is.EqualTo(0u));
            Assert.That(result[1], Is.Null);
        }

        [Test]
        public void UnionDescriptionDecodeShouldReturnValues()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field3", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field4", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.UnionWithSubtypedValues,
                Fields = structureFields
            };
            var unionDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName("ssss"), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            Assert.That(unionDescription.StructureDefinition.StructureType, Is.EqualTo(StructureType.UnionWithSubtypedValues));
            Assert.That(unionDescription.XmlName.Name, Is.EqualTo("ssss"));
            Assert.That(unionDescription.BinaryEncodingId, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(unionDescription.JsonEncodingId, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(unionDescription.XmlEncodingId, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(unionDescription.TypeId, Is.EqualTo(new ExpandedNodeId(333)));
            Assert.That(unionDescription.IsAbstract, Is.False);
            Assert.That(unionDescription.FieldsCanHaveSubtypedValues, Is.True);

            var binaryDecoder = new BinaryDecoder([1, 0, 0, 0, 3, 0, 0, 0], ServiceMessageContext.Create(null));

            // Act
            var result = unionDescription.Decode(binaryDecoder);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Is.Not.Null);
            Assert.That(result[0], Is.EqualTo(1u));
            Assert.That(result[1], Is.EqualTo(3u));
        }

        [Test]
        public void UnionDescriptionDecodeWithInvalidSwitchFieldShouldThrowException()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
            new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
            new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
        ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = structureFields
            };
            var unionDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryDecoder = new BinaryDecoder([3, 0, 0, 0], ServiceMessageContext.Create(null));

            // Act
            Action act = () => unionDescription.Decode(binaryDecoder);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Union selector out of range"));
        }

        [Test]
        public void UnionDescriptionDecodeWithNullValueShouldReturnNull()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = structureFields
            };
            var unionDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryDecoder = new BinaryDecoder([0, 0, 0, 0], ServiceMessageContext.Create(null));

            // Act
            var result = unionDescription.Decode(binaryDecoder);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result, Is.Not.Null);
            Assert.That(result[0], Is.EqualTo(0u));
            Assert.That(result[1], Is.Null);
        }

        [Test]
        public void UnionDescriptionEncodeJsonShouldEncodeValuesInNonReversibleEncoding()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field3", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field4", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = structureFields
            };
            var unionDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Compact);
            var values = new object?[] { 2u, 3u };

            // Act
            unionDescription.Encode(jsonEncoder, values);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            Assert.That(json, Does.Not.Contain("\"SwitchField\":1"));
            Assert.That(json, Does.Contain("\"Field2\":3"));
        }

        [Test]
        public void UnionDescriptionEncodeJsonShouldEncodeValuesInReversibleEncoding()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field3", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field4", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = structureFields
            };
            var unionDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Verbose);
            var values = new object?[] { 1u, 3u };

            // Act
            unionDescription.Encode(jsonEncoder, values);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            Assert.That(json, Does.Contain("\"SwitchField\":1"));
            Assert.That(json, Does.Contain("\"Value\":3"));
        }

        [Test]
        public void UnionDescriptionEncodeJsonWithInvalidSwitchFieldShouldThrowException()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = structureFields
            };
            var unionDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Compact);
            var values = new object?[] { 3u, 3u }; // Invalid switch field

            // Act
            Action act = () => unionDescription.Encode(jsonEncoder, values);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Union selector out of range"));
        }

        [Test]
        public void UnionDescriptionEncodeJsonWithNullValueShouldEncodeCorrectlyWithNonReversibleEncoding()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = structureFields
            };
            var unionDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Compact);
            var values = new object?[] { 0u, null };

            // Act
            unionDescription.Encode(jsonEncoder, values);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            Assert.That(json, Is.EqualTo("{null}"));
        }

        [Test]
        public void UnionDescriptionEncodeJsonWithNullValueShouldEncodeCorrectlyWithReversibleEncoding()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = structureFields
            };
            var unionDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Verbose);
            var values = new object?[] { 0u, null };

            // Act
            unionDescription.Encode(jsonEncoder, values);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            Assert.That(json, Is.EqualTo("""{"SwitchField":0}"""));
        }

        [Test]
        public void UnionDescriptionEncodeShouldEncodeValues()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field3", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field4", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = structureFields
            };
            var unionDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(ServiceMessageContext.Create(null));
            var values = new object?[] { 1u, 3u };

            // Act
            unionDescription.Encode(binaryEncoder, values);

            // Assert
            var encodedBytes = binaryEncoder.CloseAndReturnBuffer();
            Assert.That(encodedBytes, Is.EqualTo([1, 0, 0, 0, 3, 0, 0, 0]));
        }

        [Test]
        public void UnionDescriptionEncodeWithInvalidSwitchFieldShouldThrowException()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
            new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
            new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
        ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = structureFields
            };
            var unionDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(ServiceMessageContext.Create(null));
            var values = new object?[] { 3u, 3u };

            // Act
            Action act = () => unionDescription.Encode(binaryEncoder, values);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Union selector out of range"));
        }
        [Test]
        public void UnionDescriptionEncodeWithNullValueShouldEncodeCorrectly()
        {
            // Arrange
            var structureFields = (ArrayOf<StructureField>)[
                new() { Name = "Field1", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar },
                new() { Name = "Field2", DataType = DataTypeIds.UInt32, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = structureFields
            };
            var unionDescription = StructureDescription.Create(
                m_mockedTypeSystem.Object, new ExpandedNodeId(333),
                structureDefinition, new XmlQualifiedName(), ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(ServiceMessageContext.Create(null));
            var values = new object?[] { 0u, null };

            // Act
            unionDescription.Encode(binaryEncoder, values);

            // Assert
            var encodedBytes = binaryEncoder.CloseAndReturnBuffer();
            Assert.That(encodedBytes, Is.EqualTo([0, 0, 0, 0]));
        }
    }
}
