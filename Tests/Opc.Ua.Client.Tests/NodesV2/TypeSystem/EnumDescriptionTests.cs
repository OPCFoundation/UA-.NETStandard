#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using Moq;
using Opc.Ua;
using System.Collections.Generic;
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    [TestFixture]
    public class EnumDescriptionTests
    {
        [Test]
        public void DecodeWithEmptyEnumDefinitionShouldReturnNull()
        {
            // Arrange
            var enumDefinition = new EnumDefinition { Fields = [] };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonDecoder = new JsonDecoder("""{"TestField": 1}""", ServiceMessageContext.Create(null));

            // Act
            var result = enumDescription.Decode(jsonDecoder, "TestField");

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void DecodeWithIntegerShouldReturnEnumValueWithIntegerCode()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 },
                new() { Name = "TestField4", Value = 4 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonDecoder = new JsonDecoder("""{"TestField":4}""", ServiceMessageContext.Create(null));

            // Act
            var result = enumDescription.Decode(jsonDecoder, "TestField");

            // Assert
            Assert.That(result, Is.Not.Null.And.InstanceOf<EnumValue>());
            Assert.That(result, Is.Not.Null);
            var enumValue = (EnumValue)result;
            Assert.That(enumValue.Symbol, Is.EqualTo("TestField4"));
            Assert.That(enumValue.Value, Is.EqualTo(4));
        }

        [Test]
        public void DecodeWithWithDecoderExtensionShouldReturnEnumValueWithSymbolAndIntegerCode()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 },
                new() { Name = "TestField4", Value = 4 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName("ssss"),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var decoder = new Mock<IExtendedCodec>();
            decoder.Setup(x => x.ReadEnumerated("TestField", enumDefinition))
                .Returns(new EnumValue("TestField4", 4));

            // Act
            var result = enumDescription.Decode(decoder.Object, "TestField");

            // Assert
            Assert.That(enumDescription.XmlName.Name, Is.EqualTo("ssss"));
            Assert.That(result, Is.Not.Null.And.InstanceOf<EnumValue>());
            Assert.That(result, Is.Not.Null);
            var enumValue = (EnumValue)result;
            Assert.That(enumValue.Symbol, Is.EqualTo("TestField4"));
            Assert.That(enumValue.Value, Is.EqualTo(4));
        }

        [Test]
        public void DecodeWithIntegerShouldReturnEnumValueWithSymbolAndIntegerCode()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 },
                new() { Name = "TestField4", Value = 4 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName("ssss"),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonDecoder = new JsonDecoder("""{"TestField":"TestField_4"}""", ServiceMessageContext.Create(null));

            // Act
            var result = enumDescription.Decode(jsonDecoder, "TestField");

            // Assert
            Assert.That(enumDescription.XmlName.Name, Is.EqualTo("ssss"));
            Assert.That(result, Is.Not.Null.And.InstanceOf<EnumValue>());
            Assert.That(result, Is.Not.Null);
            var enumValue = (EnumValue)result;
            Assert.That(enumValue.Symbol, Is.EqualTo("TestField4"));
            Assert.That(enumValue.Value, Is.EqualTo(4));
        }

        [Test]
        public void DecodeWithInvalidJsonTokenShouldReturnFirstEnumField1()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonDecoder = new JsonDecoder("""{"TestField": "InvalidToken"}""", ServiceMessageContext.Create(null));

            // Act
            var result = enumDescription.Decode(jsonDecoder, "TestField");

            // Assert
            Assert.That(result, Is.Not.Null.And.InstanceOf<EnumValue>());
            Assert.That(result, Is.Not.Null);
            var enumValue = (EnumValue)result;
            Assert.That(enumValue.Symbol, Is.EqualTo("TestField1"));
            Assert.That(enumValue.Value, Is.EqualTo(1));
        }

        [Test]
        public void DecodeWithInvalidJsonTokenShouldReturnFirstEnumField2()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonDecoder = new JsonDecoder("""{"TestField": [1, 2, 3]}""", ServiceMessageContext.Create(null));

            // Act
            var result = enumDescription.Decode(jsonDecoder, "TestField");

            // Assert
            Assert.That(result, Is.Not.Null.And.InstanceOf<EnumValue>());
            Assert.That(result, Is.Not.Null);
            var enumValue = (EnumValue)result;
            Assert.That(enumValue.Symbol, Is.EqualTo("TestField1"));
            Assert.That(enumValue.Value, Is.EqualTo(1));
        }

        [Test]
        public void DecodeWithInvalidJsonTokenShouldReturnFirstEnumField3()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonDecoder = new JsonDecoder("{}", ServiceMessageContext.Create(null));

            // Act
            var result = enumDescription.Decode(jsonDecoder, "TestField");

            // Assert
            Assert.That(result, Is.Not.Null.And.InstanceOf<EnumValue>());
            Assert.That(result, Is.Not.Null);
            var enumValue = (EnumValue)result;
            Assert.That(enumValue.Symbol, Is.EqualTo("TestField1"));
            Assert.That(enumValue.Value, Is.EqualTo(1));
        }

        [Test]
        public void DecodeWithUnknownFieldNameShouldReturnFirstEnumField()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonDecoder = new JsonDecoder("""{"UnknownField": 1}""", ServiceMessageContext.Create(null));

            // Act
            var result = enumDescription.Decode(jsonDecoder, "UnknownField");

            // Assert
            Assert.That(result, Is.Not.Null.And.InstanceOf<EnumValue>());
            Assert.That(result, Is.Not.Null);
            var enumValue = (EnumValue)result;
            Assert.That(enumValue.Symbol, Is.EqualTo("TestField1"));
            Assert.That(enumValue.Value, Is.EqualTo(1));
        }

        [Test]
        public void EncodeWithNoFieldsAndNullEnumValueShouldContainZeroWithNonReversibleEncoding()
        {
            // Arrange
            var enumDefinition = new EnumDefinition { Fields = [] };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Verbose);

            // Act
            enumDescription.Encode(jsonEncoder, "TestField", null);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            Assert.That(json, Does.Contain("""
            "TestField":0
            """));
        }

        [Test]
        public void EncodeWithNoFieldsAndNullEnumValueShouldContainZeroWithReversibleEncoding()
        {
            // Arrange
            var enumDefinition = new EnumDefinition { Fields = [] };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Compact);

            // Act
            enumDescription.Encode(jsonEncoder, "TestField", null);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            Assert.That(json, Does.Contain("""
            "TestField":"0"
            """));
        }

        [Test]
        public void EncodeWithWithDecoderExtensionShouldReturnEnumValueWithSymbolAndIntegerCode()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 },
                new() { Name = "TestField4", Value = 4 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName("ssss"),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);
            var enumValue = new EnumValue("TestField4", 4);

            var encoder = new Mock<IExtendedCodec>();
            encoder.Setup(x => x.WriteEnumerated("TestField", enumValue, enumDefinition)).Verifiable(Times.Once);

            // Act
            enumDescription.Encode(encoder.Object, "TestField", enumValue);

            // Assert
            encoder.Verify();
        }

        [Test]
        public void EncodeWithNullEnumValueShouldEncodeDefaultWithNonReversibleEncoding()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Verbose);

            // Act
            enumDescription.Encode(jsonEncoder, "TestField", null);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            Assert.That(json, Does.Contain("""
            "TestField":1
            """));
        }

        [Test]
        public void EncodeWithNullEnumValueShouldEncodeDefaultWithReversibleEncoding()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Compact);

            // Act
            enumDescription.Encode(jsonEncoder, "TestField", null);

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            Assert.That(json, Does.Contain("""
            "TestField":"TestField1"
            """));
        }
        [Test]
        public void EnumDescriptionDecodeDefaultDecoderShouldReturnEnumValue()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryDecoder = new BinaryDecoder([1, 0, 0, 0], ServiceMessageContext.Create(null));

            // Act
            var result = enumDescription.Decode(binaryDecoder, "TestField");

            // Assert
            Assert.That(result, Is.Not.Null.And.InstanceOf<EnumValue>());
            Assert.That(result, Is.Not.Null);
            var enumValue = (EnumValue)result;
            Assert.That(enumValue.Symbol, Is.EqualTo("TestField1"));
            Assert.That(enumValue.Value, Is.EqualTo(1));
        }

        [Test]
        public void EnumDescriptionDecodeJsonDecoderShouldReturnEnumValue()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonDecoder = new JsonDecoder("""{"TestField": 1}""", ServiceMessageContext.Create(null));

            // Act
            var result = enumDescription.Decode(jsonDecoder, "TestField");

            // Assert
            Assert.That(result, Is.Not.Null.And.InstanceOf<EnumValue>());
            Assert.That(result, Is.Not.Null);
            var enumValue = (EnumValue)result;
            Assert.That(enumValue.Symbol, Is.EqualTo("TestField1"));
            Assert.That(enumValue.Value, Is.EqualTo(1));
        }

        [Test]
        public void EnumDescriptionEncodeDefaultEncoderShouldEncodeEnumValue()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var binaryEncoder = new BinaryEncoder(ServiceMessageContext.Create(null));

            // Act
            enumDescription.Encode(binaryEncoder, "TestField", new EnumValue("TestField1", 1));

            // Assert
            var encodedBytes = binaryEncoder.CloseAndReturnBuffer();
            Assert.That(encodedBytes, Is.EqualTo([1, 0, 0, 0]));
        }

        [Test]
        public void EnumDescriptionEncodeJsonEncoderShouldEncodeEnumValueWithNonReversibleEncoding()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Verbose);

            // Act
            enumDescription.Encode(jsonEncoder, "TestField", new EnumValue("TestField1", 1));

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            Assert.That(json, Does.Contain("""
            "TestField":1
            """));
        }

        [Test]
        public void EnumDescriptionEncodeJsonEncoderShouldEncodeEnumValueWithReversibleEncoding()
        {
            // Arrange
            var enumFields = (ArrayOf<EnumField>)[
                new() { Name = "TestField1", Value = 1 },
                new() { Name = "TestField2", Value = 2 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var enumDescription = new EnumDescription(new ExpandedNodeId(333), enumDefinition, new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null);

            var jsonEncoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Compact);

            // Act
            enumDescription.Encode(jsonEncoder, "TestField", new EnumValue("TestField1", 1));

            // Assert
            var json = jsonEncoder.CloseAndReturnText();
            Assert.That(json, Does.Contain("""
            "TestField":"TestField1"
            """));
        }
        [Test]
        public void EnumDescriptionNullInstanceShouldReturnNull()
        {
            // Arrange
            var nullEnumDescription = EnumDescription.Null;

            // Act
            var result = nullEnumDescription.Decode(null!, "TestField");

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void EnumValueInstancesWithDifferentValuesShouldNotBeEqual()
        {
            // Arrange
            var enumValue1 = new EnumValue("TestField1", 1);
            var enumValue2 = new EnumValue("TestField2", 2);

            // Act & Assert
            Assert.That(enumValue1, Is.Not.EqualTo(enumValue2));
        }

        [Test]
        public void EnumValueInstancesWithSameValuesShouldBeEqual()
        {
            // Arrange
            var enumValue1 = new EnumValue("TestField1", 1);
            var enumValue2 = new EnumValue("TestField1", 1);

            // Act & Assert
            Assert.That(enumValue1, Is.EqualTo(enumValue2));
        }

        [Test]
        public void EnumValuePropertiesShouldBeSetCorrectly()
        {
            // Arrange
            var enumValue = new EnumValue("TestField1", 1);

            // Act & Assert
            Assert.That(enumValue.Symbol, Is.EqualTo("TestField1"));
            Assert.That(enumValue.Value, Is.EqualTo(1));
        }

        public interface IExtendedCodec : IEncoder, IDecoder,
            IEnumValueTypeDecoder, IEnumValueTypeEncoder;
    }
}
#endif
