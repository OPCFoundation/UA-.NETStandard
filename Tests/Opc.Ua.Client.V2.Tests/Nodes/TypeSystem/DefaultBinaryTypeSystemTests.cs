// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using Moq;
using Opc.Ua;
using Opc.Ua.Schema.Binary;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using System.Xml;
using System.Threading.Tasks;
using System.Text;

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    [TestFixture]
    public sealed class DefaultBinaryTypeSystemTests
    {
        private Mock<INodeCache> m_nodeCacheMock;
        private Mock<IServiceMessageContext> m_contextMock;
        private Mock<ILogger<DefaultBinaryTypeSystem>> m_loggerMock;

        [SetUp]
        public void SetUp()
        {
            m_nodeCacheMock = new Mock<INodeCache>();
            m_loggerMock = new Mock<ILogger<DefaultBinaryTypeSystem>>();
            m_contextMock = new Mock<IServiceMessageContext>();
            m_contextMock.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        }

        [Test]
        public void TypeSystemIdShouldReturnCorrectValue()
        {
            var sut = new DefaultBinaryTypeSystem(m_nodeCacheMock.Object,
                m_contextMock.Object, m_loggerMock.Object);

            // Act
            var typeSystemId = sut.TypeSystemId;

            // Assert
            Assert.That(typeSystemId, Is.EqualTo(Objects.OPCBinarySchema_TypeSystem));
        }

        [Test]
        public void TypeSystemNameShouldReturnCorrectValue()
        {
            var sut = new DefaultBinaryTypeSystem(m_nodeCacheMock.Object,
                m_contextMock.Object, m_loggerMock.Object);
            // Act
            var typeSystemName = sut.TypeSystemName;

            // Assert
            Assert.That(typeSystemName, Is.EqualTo((QualifiedName)BrowseNames.OPCBinarySchema_TypeSystem));
        }

        [Test]
        public void EncodingNameShouldReturnCorrectValue()
        {
            var sut = new DefaultBinaryTypeSystem(m_nodeCacheMock.Object,
               m_contextMock.Object, m_loggerMock.Object);
            // Act
            var encodingName = sut.EncodingName;

            // Assert
            Assert.That(encodingName, Is.EqualTo((QualifiedName)BrowseNames.DefaultBinary));
        }

        [Test]
        public void ToStructureDefinitionShouldThrowWhenFieldHasIsLengthInBytesOrTerminator()
        {
            // Arrange
            var structuredType = new StructuredType
            {
                Name = "TestType",
                Field =
                [
                    new FieldType
                    {
                        Name = "Field1",
                        TypeName = new XmlQualifiedName("UInt32", Namespaces.OpcUa),
                        IsLengthInBytes = true
                    }
                ]
            };

            var defaultEncodingId = new ExpandedNodeId(1, 1);
            var dataTypeNodeId = new ExpandedNodeId(2, 1);
            var typeDictionary = new Dictionary<XmlQualifiedName, (ExpandedNodeId, ExpandedNodeId)>
            {
                { new XmlQualifiedName("TestType", "http://test.org"), (dataTypeNodeId, defaultEncodingId) }
            };
            var namespaceUris = new NamespaceTable();

            // Act
            Action act = () => DefaultBinaryTypeSystem.ToStructureDefinition(
                structuredType, defaultEncodingId, typeDictionary, namespaceUris, dataTypeNodeId);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*The structure definition uses a Terminator or LengthInBytes, which are not supported.*"));
        }

        [Test]
        public void ToStructureDefinitionShouldThrowWhenStructureIsUnionAndHasBitField()
        {
            // Arrange
            var structuredType = new StructuredType
            {
                Name = "TestType",
                Field =
                [
                    new FieldType
                    {
                        Name = "BitField",
                        TypeName = new XmlQualifiedName("Bit", Namespaces.OpcBinarySchema)
                    },
                    new FieldType
                    {
                        Name = "UnionField",
                        TypeName = new XmlQualifiedName("UInt32", Namespaces.OpcUa),
                        SwitchValue = 1
                    }
                ]
            };

            var defaultEncodingId = new ExpandedNodeId(2, 1);
            var dataTypeNodeId = new ExpandedNodeId(3, 1);
            var typeDictionary = new Dictionary<XmlQualifiedName, (ExpandedNodeId, ExpandedNodeId)>
            {
                { new XmlQualifiedName("TestType", "http://test.org"), (dataTypeNodeId, defaultEncodingId) }
            };
            var namespaceUris = new NamespaceTable();

            // Act
            Action act = () => DefaultBinaryTypeSystem.ToStructureDefinition(
                structuredType, defaultEncodingId, typeDictionary, namespaceUris, dataTypeNodeId);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*The structure definition combines a Union and a bit filed, both of which are not supported in a single structure.*"));
        }

        [Test]
        public void ToStructureDefinitionShouldThrowWhenOptionalBitsNot32()
        {
            // Arrange
            var structuredType = new StructuredType
            {
                Name = "TestType",
                Field =
                [
                    new FieldType
                    {
                        Name = "BitField",
                        TypeName = new XmlQualifiedName("Bit", Namespaces.OpcBinarySchema),
                        Length = 16 // Less than 32 bits
                    },
                    new FieldType
                    {
                        Name = "OptionalField",
                        TypeName = new XmlQualifiedName("UInt32", Namespaces.OpcUa),
                        SwitchField = "BitField"
                    }
                ]
            };

            var defaultEncodingId = new ExpandedNodeId(2, 1);
            var dataTypeNodeId = new ExpandedNodeId(3, 1);
            var typeDictionary = new Dictionary<XmlQualifiedName, (ExpandedNodeId, ExpandedNodeId)>
            {
                { new XmlQualifiedName("TestType", "http://test.org"), (dataTypeNodeId, defaultEncodingId) }
            };
            var namespaceUris = new NamespaceTable();

            // Act
            Action act = () => DefaultBinaryTypeSystem.ToStructureDefinition(
                structuredType, defaultEncodingId, typeDictionary, namespaceUris, dataTypeNodeId);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*Bitwise option selectors must have 32 bits.*"));
        }

        [Test]
        public void ToStructureDefinitionShouldThrowWhenArrayLengthFieldDoesNotPrecedeArray()
        {
            // Arrange
            var structuredType = new StructuredType
            {
                Name = "ArrayType",
                Field =
                [
                    new FieldType
                    {
                        Name = "Array",
                        TypeName = new XmlQualifiedName("UInt32", Namespaces.OpcUa),
                        LengthField = "LengthField"
                    },
                    new FieldType
                    {
                        Name = "LengthField",
                        TypeName = new XmlQualifiedName("UInt32", Namespaces.OpcUa)
                    }
                ]
            };

            var defaultEncodingId = new ExpandedNodeId(2, 1);
            var dataTypeNodeId = new ExpandedNodeId(3, 1);
            var typeDictionary = new Dictionary<XmlQualifiedName, (ExpandedNodeId, ExpandedNodeId)>
            {
                { new XmlQualifiedName("ArrayType", "http://test.org"), (dataTypeNodeId, defaultEncodingId) }
            };
            var namespaceUris = new NamespaceTable();

            // Act
            Action act = () => DefaultBinaryTypeSystem.ToStructureDefinition(
                structuredType, defaultEncodingId, typeDictionary, namespaceUris, dataTypeNodeId);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*The length field must precede the type field of an array.*"));
        }

        [Test]
        public void ToStructureDefinitionShouldThrowWhenUnionSwitchFieldNotFirstField()
        {
            // Arrange
            var structuredType = new StructuredType
            {
                Name = "UnionType",
                Field =
                [
                    new FieldType
                    {
                        Name = "UnionField",
                        TypeName = new XmlQualifiedName("UInt32", Namespaces.OpcUa),
                        SwitchField = "SwitchField"
                    },
                    new FieldType
                    {
                        Name = "SwitchField",
                        TypeName = new XmlQualifiedName("UInt32", Namespaces.OpcUa)
                    }
                ]
            };

            var defaultEncodingId = new ExpandedNodeId(2, 1);
            var dataTypeNodeId = new ExpandedNodeId(3, 1);
            var typeDictionary = new Dictionary<XmlQualifiedName, (ExpandedNodeId, ExpandedNodeId)>
            {
                { new XmlQualifiedName("UnionType", "http://test.org"), (dataTypeNodeId, defaultEncodingId) }
            };
            var namespaceUris = new NamespaceTable();

            // Act
            Action act = () => DefaultBinaryTypeSystem.ToStructureDefinition(
                structuredType, defaultEncodingId, typeDictionary, namespaceUris, dataTypeNodeId);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*The switch field for SwitchField does not exist*"));
        }

        [Test]
        public void ToStructureDefinitionShouldThrowWhenSwitchFieldDoesNotExist()
        {
            // Arrange
            var structuredType = new StructuredType
            {
                Name = "OptionalFieldType",
                Field =
                [
                    new FieldType
                    {
                        Name = "BitField",
                        TypeName = new XmlQualifiedName("Bit", Namespaces.OpcBinarySchema),
                        Length = 32
                    },
                    new FieldType
                    {
                        Name = "OptionalField",
                        TypeName = new XmlQualifiedName("UInt32", Namespaces.OpcUa),
                        SwitchField = "NonExistentSwitchField"
                    }
                ]
            };

            var defaultEncodingId = new ExpandedNodeId(2, 1);
            var dataTypeNodeId = new ExpandedNodeId(3, 1);
            var typeDictionary = new Dictionary<XmlQualifiedName, (ExpandedNodeId, ExpandedNodeId)>
            {
                { new XmlQualifiedName("OptionalFieldType", "http://test.org"), (dataTypeNodeId, defaultEncodingId) }
            };
            var namespaceUris = new NamespaceTable();

            // Act
            Action act = () => DefaultBinaryTypeSystem.ToStructureDefinition(
                structuredType, defaultEncodingId, typeDictionary, namespaceUris, dataTypeNodeId);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*The switch field for NonExistentSwitchField does not exist.*"));
        }

        [Test]
        public void ToStructureDefinitionShouldThrowWhenUnionFieldOrderIsIncorrect()
        {
            // Arrange
            var structuredType = new StructuredType
            {
                Name = "UnionType",
                Field =
                [
                    new FieldType
                    {
                        Name = "SwitchField",
                        TypeName = new XmlQualifiedName("UInt32", Namespaces.OpcUa),
                        SwitchValue = 0
                    },
                    new FieldType
                    {
                        Name = "ExtraField",
                        TypeName = new XmlQualifiedName("UInt32", Namespaces.OpcUa)
                    },
                    new FieldType
                    {
                        Name = "UnionField",
                        TypeName = new XmlQualifiedName("UInt32", Namespaces.OpcUa),
                        SwitchField = "SwitchField",
                        SwitchValue = 1
                    }
                ]
            };

            var defaultEncodingId = new ExpandedNodeId(2, 1);
            var dataTypeNodeId = new ExpandedNodeId(3, 1);
            var typeDictionary = new Dictionary<XmlQualifiedName, (ExpandedNodeId, ExpandedNodeId)>
            {
                { new XmlQualifiedName("UnionType", "http://test.org"), (dataTypeNodeId, defaultEncodingId) }
            };
            var namespaceUris = new NamespaceTable();

            // Act
            Action act = () => DefaultBinaryTypeSystem.ToStructureDefinition(
                structuredType, defaultEncodingId, typeDictionary, namespaceUris, dataTypeNodeId);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*The switch field of a union must be the first field in the complex type.*"));
        }

        [Test]
        public void ToStructureDefinitionShouldHandleNestedTypesSuccessfully()
        {
            // Arrange
            var nestedStructuredType = new StructuredType
            {
                Name = "NestedType",
                QName = new XmlQualifiedName("NestedType", "http://test.org"),
                Field =
                [
                    new FieldType
                    {
                        Name = "InnerField",
                        TypeName = new XmlQualifiedName("UInt32", Namespaces.OpcUa)
                    }
                ]
            };

            var structuredType = new StructuredType
            {
                Name = "ParentType",
                QName = new XmlQualifiedName("ParentType", "http://test.org"),
                Field =
                [
                    new FieldType
                    {
                        Name = "NestedField",
                        TypeName = nestedStructuredType.QName
                    }
                ]
            };

            var defaultEncodingId = new ExpandedNodeId(2, 1);
            var parentTypeNodeId = new ExpandedNodeId(3, 1);
            var nestedTypeNodeId = new ExpandedNodeId(4, 1);

            var typeDictionary = new Dictionary<XmlQualifiedName, (ExpandedNodeId, ExpandedNodeId)>
            {
                { structuredType.QName, (parentTypeNodeId, defaultEncodingId) },
                { nestedStructuredType.QName, (nestedTypeNodeId, defaultEncodingId) }
            };
            var namespaceUris = new NamespaceTable();

            // Act
            var result = DefaultBinaryTypeSystem.ToStructureDefinition(
                structuredType, defaultEncodingId, typeDictionary, namespaceUris, parentTypeNodeId);

            // Assert
            Assert.That(result.Fields, Has.Count.EqualTo(1));
            Assert.That(result.Fields[0].Name, Is.EqualTo("NestedField"));
            Assert.That(result.Fields[0].DataType, Is.EqualTo(ExpandedNodeId.ToNodeId(nestedTypeNodeId, namespaceUris)));
        }

        [Test]
        public void ToStructureDefinitionWithValidStructuredTypeShouldReturnCorrectStructureDefinition()
        {
            // Arrange
            var structuredType = new StructuredType
            {
                Name = "TestStructure",
                QName = new XmlQualifiedName("TestStructure", "http://test.org/UA/"),
                Field =
                [
                    new FieldType
                    {
                        Name = "Field1",
                        TypeName = new XmlQualifiedName("String", Namespaces.OpcUa)
                    },
                    new FieldType
                    {
                        Name = "Field2",
                        TypeName = new XmlQualifiedName("Int32", Namespaces.OpcUa)
                    }
                ]
            };

            var defaultEncodingId = ExpandedNodeId.Parse("ns=1;i=1001");
            var dataTypeNodeId = ExpandedNodeId.Parse("ns=1;i=1000");

            var typeDictionary = new Dictionary<XmlQualifiedName, (ExpandedNodeId, ExpandedNodeId)>
            {
                { structuredType.QName, (dataTypeNodeId, defaultEncodingId) },
                { new XmlQualifiedName("Int32", Namespaces.OpcUa), (DataTypeIds.Int32, defaultEncodingId) },
                { new XmlQualifiedName("String", Namespaces.OpcUa), (DataTypeIds.String, defaultEncodingId) }
            };

            var namespaceTable = new NamespaceTable();
            namespaceTable.Append("http://test.org/UA/");

            // Act
            var structureDefinition = DefaultBinaryTypeSystem.ToStructureDefinition(structuredType,
                defaultEncodingId, typeDictionary, namespaceTable, dataTypeNodeId);

            // Assert
            Assert.That(structureDefinition, Is.Not.Null);
            Assert.That(structureDefinition.StructureType, Is.EqualTo(StructureType.Structure));
            Assert.That(structureDefinition.Fields, Has.Count.EqualTo(2));
            Assert.That(structureDefinition.Fields[0].Name, Is.EqualTo("Field1"));
            Assert.That(structureDefinition.Fields[0].DataType, Is.EqualTo(DataTypeIds.String));
            Assert.That(structureDefinition.Fields[1].Name, Is.EqualTo("Field2"));
            Assert.That(structureDefinition.Fields[1].DataType, Is.EqualTo(DataTypeIds.Int32));
        }

        [Test]
        public void ToStructureDefinitionWithUnionTypeShouldReturnUnionStructureDefinition()
        {
            // Arrange
            var structuredType = new StructuredType
            {
                Name = "TestUnion",
                QName = new XmlQualifiedName("TestUnion", "http://test.org/UA/"),
                Field =
                [
                    new FieldType
                    {
                        Name = "SwitchField",
                        TypeName = new XmlQualifiedName("Int32", Namespaces.OpcUa),
                        SwitchField = null
                    },
                    new FieldType
                    {
                        Name = "Field1",
                        TypeName = new XmlQualifiedName("Double", Namespaces.OpcUa),
                        SwitchValue = 1,
                        SwitchField = "SwitchField"
                    },
                    new FieldType
                    {
                        Name = "Field2",
                        TypeName = new XmlQualifiedName("String", Namespaces.OpcUa),
                        SwitchValue = 2,
                        SwitchField = "SwitchField"
                    }
                ]
            };

            var defaultEncodingId = ExpandedNodeId.Parse("ns=1;i=2001");
            var dataTypeNodeId = ExpandedNodeId.Parse("ns=1;i=2000");

            var typeDictionary = new Dictionary<XmlQualifiedName, (ExpandedNodeId, ExpandedNodeId)>
            {
                { structuredType.QName, (dataTypeNodeId, defaultEncodingId) },
                { new XmlQualifiedName("Double", Namespaces.OpcUa), (DataTypeIds.Double, defaultEncodingId) },
                { new XmlQualifiedName("String", Namespaces.OpcUa), (DataTypeIds.String, defaultEncodingId) }
            };

            var namespaceTable = new NamespaceTable();
            namespaceTable.Append("http://test.org/UA/");

            // Act
            var structureDefinition = DefaultBinaryTypeSystem.ToStructureDefinition(
                structuredType, defaultEncodingId, typeDictionary, namespaceTable,
                dataTypeNodeId);

            // Assert
            Assert.That(structureDefinition, Is.Not.Null);
            Assert.That(structureDefinition.StructureType, Is.EqualTo(StructureType.Union));
            Assert.That(structureDefinition.Fields, Has.Count.EqualTo(2));
            Assert.That(structureDefinition.Fields[0].Name, Is.EqualTo("Field1"));
            Assert.That(structureDefinition.Fields[0].DataType, Is.EqualTo(DataTypeIds.Double));
            Assert.That(structureDefinition.Fields[1].Name, Is.EqualTo("Field2"));
            Assert.That(structureDefinition.Fields[1].DataType, Is.EqualTo(DataTypeIds.String));
        }

        [Test]
        public void ToStructureDefinitionWithInvalidStructureShouldThrowException()
        {
            var sut = new DefaultBinaryTypeSystem(m_nodeCacheMock.Object,
            m_contextMock.Object, m_loggerMock.Object);
            // Arrange
            var structuredType = new StructuredType
            {
                Name = "InvalidStructure",
                QName = new XmlQualifiedName("InvalidStructure", "http://test.org/UA/"),
                Field =
                [
                    new FieldType
                    {
                        Name = "Field1",
                        TypeName = new XmlQualifiedName("String", Namespaces.OpcUa),
                        IsLengthInBytes = true // Invalid setting
                    }
                ]
            };

            var defaultEncodingId = ExpandedNodeId.Parse("ns=1;i=3001");
            var dataTypeNodeId = ExpandedNodeId.Parse("ns=1;i=3000");

            var typeDictionary = new Dictionary<XmlQualifiedName, (ExpandedNodeId, ExpandedNodeId)>
            {
                { structuredType.QName, (dataTypeNodeId, defaultEncodingId) },
                { new XmlQualifiedName("Double", Namespaces.OpcUa), (DataTypeIds.Double, defaultEncodingId) },
                { new XmlQualifiedName("String", Namespaces.OpcUa), (DataTypeIds.String, defaultEncodingId) }
            };

            var namespaceTable = new NamespaceTable();
            namespaceTable.Append("http://test.org/UA/");

            // Act
            Action act = () => DefaultBinaryTypeSystem.ToStructureDefinition(structuredType,
                defaultEncodingId, typeDictionary, namespaceTable, dataTypeNodeId);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*Terminator or LengthInBytes, which are not supported.*"));
        }

        [Test]
        public void ToEnumDefinitionWithValidEnumeratedTypeShouldReturnCorrectEnumDefinition()
        {
            // Arrange
            var enumeratedType = new EnumeratedType
            {
                Name = "TestEnum",
                EnumeratedValue =
                [
                    new EnumeratedValue { Name = "Value1", Value = 0 },
                    new EnumeratedValue { Name = "Value2", Value = 1 },
                    new EnumeratedValue { Name = "Value3", Value = 2 }
                ]
            };

            // Act
            var enumDefinition = DefaultBinaryTypeSystem.ToEnumDefinition(enumeratedType);

            // Assert
            Assert.That(enumDefinition, Is.Not.Null);
            Assert.That(enumDefinition.Fields, Has.Count.EqualTo(3));

            Assert.That(enumDefinition.Fields[0].Name, Is.EqualTo("Value1"));
            Assert.That(enumDefinition.Fields[0].Value, Is.EqualTo(0));

            Assert.That(enumDefinition.Fields[1].Name, Is.EqualTo("Value2"));
            Assert.That(enumDefinition.Fields[1].Value, Is.EqualTo(1));

            Assert.That(enumDefinition.Fields[2].Name, Is.EqualTo("Value3"));
            Assert.That(enumDefinition.Fields[2].Value, Is.EqualTo(2));
        }

        [Test]
        public async Task GetDataTypeDefinitionAsyncWithUnknownDataTypeShouldReturnNullAsync()
        {
            // Arrange
            var sut = new DefaultBinaryTypeSystem(m_nodeCacheMock.Object,
                m_contextMock.Object, m_loggerMock.Object);
            var dataTypeId = ExpandedNodeId.Parse("i=9999");

            m_nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.Is<NodeId>(nodeId => nodeId == new NodeId(9999u)),
                    ReferenceTypeIds.HasProperty,
                    false, false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);

            // Act
            var result = await sut.GetDataTypeDefinitionAsync(dataTypeId, CancellationToken.None);

            // Assert
            Assert.That(result, Is.Null);
            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task LoadAsyncShouldNotThrowWhenNoDictionariesAreFoundInTypeSystemAsync()
        {
            // Arrange
            var sut = new DefaultBinaryTypeSystem(m_nodeCacheMock.Object,
                m_contextMock.Object, m_loggerMock.Object);
            var ct = CancellationToken.None;

            m_nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    ct))
                .ReturnsAsync([])
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.LoadAsync(ct);

            // Assert
            Assert.DoesNotThrowAsync(async () => await act());
            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task LoadAsyncShouldLoadNoTypeDefinitionsFromZeroLengthDictionaryAndNotThrowAsync()
        {
            // Arrange
            var sut = new DefaultBinaryTypeSystem(m_nodeCacheMock.Object,
                m_contextMock.Object, m_loggerMock.Object);
            var ct = CancellationToken.None;
            var dictionaryNodeId = new NodeId(123);

            var dictionaryNode = new VariableNode
            {
                NodeId = dictionaryNodeId,
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("TestDictionary"),
                Value = "Test Dictionary Content"
            };
            var nsNode = new VariableNode
            {
                NodeId = new NodeId(1),
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName(BrowseNames.NamespaceUri),
                Value = "http://test.org/UA/"
            };

            var dictionaryContent = new DataValue(new Variant(Array.Empty<byte>()));

            m_nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    sut.TypeSystemId,
                    ReferenceTypeIds.HasComponent,
                    false,
                    false,
                    ct))
                .ReturnsAsync([dictionaryNode])
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetValuesAsync(
                    new List<NodeId> { dictionaryNodeId },
                    ct))
                .ReturnsAsync([dictionaryContent])
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    dictionaryNodeId,
                    ReferenceTypeIds.HasProperty,
                    false, false,
                    ct))
                .ReturnsAsync([nsNode])
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetValueAsync(
                    nsNode.NodeId,
                    ct))
                .ReturnsAsync(new DataValue(nsNode.Value))
                .Verifiable(Times.Once);

            // Act
            await sut.LoadAsync(ct);

            // Assert
            m_nodeCacheMock.Verify();
            Assert.That(sut.TypeCount, Is.EqualTo(0));
        }

        [Test]
        public async Task LoadAsyncShouldLoadTypeDefinitionsFromMachineryAndGmsDictionariesAsync()
        {
            // Arrange
            var sut = new DefaultBinaryTypeSystem(m_nodeCacheMock.Object,
                m_contextMock.Object, m_loggerMock.Object);
            var ct = CancellationToken.None;
            var machineryDictionaryId = new NodeId(123);
            var machineryDictionary = new VariableNode
            {
                NodeId = machineryDictionaryId,
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("TypeDictionary"),
                Value = "Test Dictionary Content"
            };
            var machinary = new DataValue(new Variant(Encoding.UTF8.GetBytes(MachineryResultTypeSystem)));
            var machineryNsNode = new VariableNode
            {
                NodeId = new NodeId(1),
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName(BrowseNames.NamespaceUri),
                Value = "http://opcfoundation.org/UA/Machinery/Result/"
            };
            var gmsDictionaryId = new NodeId(1243);
            var gmsDictionary = new VariableNode
            {
                NodeId = gmsDictionaryId,
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("TypeDictionary"),
                Value = "Test Dictionary Content"
            };
            var gmsNsNode = new VariableNode
            {
                NodeId = new NodeId(2),
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName(BrowseNames.NamespaceUri),
                Value = "http://opcfoundation.org/UA/GMS/"
            };
            var gms = new DataValue(new Variant(Encoding.UTF8.GetBytes(GmsTypeSystem)));

            m_nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    sut.TypeSystemId,
                    ReferenceTypeIds.HasComponent,
                    false,
                    false,
                    ct))
                .ReturnsAsync([machineryDictionary, gmsDictionary])
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetValuesAsync(
                    new List<NodeId> { machineryDictionaryId, gmsDictionaryId },
                    ct))
                .ReturnsAsync([machinary, gms])
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    machineryDictionaryId,
                    ReferenceTypeIds.HasProperty,
                    false, false,
                    ct))
                .ReturnsAsync([machineryNsNode])
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    gmsDictionaryId,
                    ReferenceTypeIds.HasProperty,
                    false, false,
                    ct))
                .ReturnsAsync([gmsNsNode])
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetValueAsync(
                    machineryNsNode.NodeId,
                    ct))
                .ReturnsAsync(new DataValue(machineryNsNode.Value))
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetValueAsync(
                    gmsNsNode.NodeId,
                    ct))
                .ReturnsAsync(new DataValue(gmsNsNode.Value))
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    gmsDictionaryId,
                    ReferenceTypeIds.HasComponent,
                    false,
                    false,
                    ct))
                .ReturnsAsync(
                [
                    new VariableNode { BrowseName = new QualifiedName("Description1"), NodeId = new NodeId(13) },
                    new VariableNode { BrowseName = new QualifiedName("Description2"), NodeId = new NodeId(14) },
                    new VariableNode { BrowseName = new QualifiedName("Description2"), NodeId = new NodeId(15) }
                ])
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetValuesAsync(
                    new NodeId[] { new(13), new(14), new(15) },
                    ct))
                .ReturnsAsync(
                [
                    new ("MeasurementReasonEnum"),
                    new ("ToleranceLimitEnum"),
                    new ("ToolAlignmentState")
                ])
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    new NodeId[] { new(13), new(14), new(15) },
                    new NodeId[] { ReferenceTypeIds.HasDescription },
                    true,
                    false,
                    ct))
                .ReturnsAsync(
                [
                    new VariableNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = new NodeId(16) },
                    new VariableNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = new NodeId(17) },
                    new VariableNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = new NodeId(18) }
                ])
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    new NodeId[] { new(16), new(17), new(18) },
                    new NodeId[] { ReferenceTypeIds.HasEncoding },
                    true,
                    false,
                    ct))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = new QualifiedName("MeasurementReasonEnum"), NodeId = new NodeId(19) },
                    new DataTypeNode { BrowseName = new QualifiedName("ToleranceLimitEnum"), NodeId = new NodeId(20) },
                    new DataTypeNode { BrowseName = new QualifiedName("ToolAlignmentState"), NodeId = new NodeId(21) }
                ])
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    machineryDictionaryId,
                    ReferenceTypeIds.HasComponent,
                    false,
                    false,
                    ct))
                .ReturnsAsync(
                [
                    new VariableNode { BrowseName = new QualifiedName("Description1"), NodeId = new NodeId(3) },
                    new VariableNode { BrowseName = new QualifiedName("Description2"), NodeId = new NodeId(4) },
                    new VariableNode { BrowseName = new QualifiedName("Description2"), NodeId = new NodeId(5) }
                ])
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetValuesAsync(
                    new NodeId[] { new(3), new(4), new(5) },
                    ct))
                .ReturnsAsync(
                [
                    new ("ProcessingTimesDataType"),
                    new ("ResultDataType"),
                    new ("ResultTransferOptionsDataType")
                ])
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    new NodeId[] { new(3), new(4), new(5) },
                    new NodeId[] { ReferenceTypeIds.HasDescription },
                    true,
                    false,
                    ct))
                .ReturnsAsync(
                [
                    new VariableNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = new NodeId(6) },
                    new VariableNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = new NodeId(7) },
                    new VariableNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = new NodeId(8) }
                ])
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    new NodeId[] { new(6), new(7), new(8) },
                    new NodeId[] { ReferenceTypeIds.HasEncoding },
                    true,
                    false,
                    ct))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = new QualifiedName("ProcessingTimesDataType"), NodeId = new NodeId(9) },
                    new DataTypeNode { BrowseName = new QualifiedName("ResultDataType"), NodeId = new NodeId(10) },
                    new DataTypeNode { BrowseName = new QualifiedName("ResultTransferOptionsDataType"), NodeId = new NodeId(11) }
                ])
                .Verifiable(Times.Once);

            // Act
            await sut.LoadAsync(ct);

            var processingTimesDataType1 = await sut.GetDataTypeDefinitionAsync(new NodeId(9), default);
            var resultDataType1 = await sut.GetDataTypeDefinitionAsync(new NodeId(10), default);
            var resultTransferOptionsDataType1 = await sut.GetDataTypeDefinitionAsync(new NodeId(11), default);
            var processingTimesDataType2 = await sut.GetDataTypeDefinitionAsync(new NodeId(6), default);
            var resultDataType2 = await sut.GetDataTypeDefinitionAsync(new NodeId(7), default);
            var resultTransferOptionsDataType2 = await sut.GetDataTypeDefinitionAsync(new NodeId(8), default);

            var measurementReasonEnum1 = await sut.GetDataTypeDefinitionAsync(new NodeId(19), default);
            var toleranceLimitEnum1 = await sut.GetDataTypeDefinitionAsync(new NodeId(20), default);
            var toolAlignmentState1 = await sut.GetDataTypeDefinitionAsync(new NodeId(21), default);

            // Assert
            m_nodeCacheMock.Verify();
            Assert.That(sut.TypeCount, Is.EqualTo(12));
            Assert.That(processingTimesDataType1, Is.Not.Null);
            Assert.That(processingTimesDataType1, Is.EqualTo(processingTimesDataType2));
            Assert.That(resultDataType1, Is.Not.Null);
            Assert.That(resultDataType1, Is.EqualTo(resultDataType2));
            Assert.That(resultTransferOptionsDataType1, Is.Not.Null);
            Assert.That(resultTransferOptionsDataType1, Is.EqualTo(resultTransferOptionsDataType2));
            Assert.That(processingTimesDataType1!.Definition!, Is.TypeOf<StructureDefinition>());
            Assert.That(((StructureDefinition)processingTimesDataType1!.Definition!).Fields, Has.Count.EqualTo(4));
            Assert.That(processingTimesDataType1!.Definition!, Is.TypeOf<StructureDefinition>());
            Assert.That(((StructureDefinition)processingTimesDataType1!.Definition!).StructureType, Is.EqualTo(StructureType.StructureWithOptionalFields));
            Assert.That(resultDataType1!.Definition!, Is.TypeOf<StructureDefinition>());
            Assert.That(((StructureDefinition)resultDataType1!.Definition!).Fields, Has.Count.EqualTo(3));
            Assert.That(resultDataType1!.Definition!, Is.TypeOf<StructureDefinition>());
            Assert.That(((StructureDefinition)resultDataType1!.Definition!).StructureType, Is.EqualTo(StructureType.Structure));
            Assert.That(resultTransferOptionsDataType1!.Definition!, Is.TypeOf<StructureDefinition>());
            Assert.That(((StructureDefinition)resultTransferOptionsDataType1!.Definition!).Fields, Has.Count.EqualTo(1));
            Assert.That(resultTransferOptionsDataType1!.Definition!, Is.TypeOf<StructureDefinition>());
            Assert.That(((StructureDefinition)resultTransferOptionsDataType1!.Definition!).StructureType, Is.EqualTo(StructureType.Structure));
            Assert.That(toolAlignmentState1!.Definition!, Is.TypeOf<EnumDefinition>());
            Assert.That(((EnumDefinition)toolAlignmentState1!.Definition!).Fields, Has.Count.EqualTo(3));
            Assert.That(toleranceLimitEnum1!.Definition!, Is.TypeOf<EnumDefinition>());
            Assert.That(((EnumDefinition)toleranceLimitEnum1!.Definition!).Fields, Has.Count.EqualTo(3));
            Assert.That(measurementReasonEnum1!.Definition!, Is.TypeOf<EnumDefinition>());
            Assert.That(((EnumDefinition)measurementReasonEnum1!.Definition!).Fields, Has.Count.EqualTo(6));
        }

        public const string GmsTypeSystem = """
        <opc:TypeDictionary xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:tns="http://opcfoundation.org/UA/GMS/" DefaultByteOrder="LittleEndian" xmlns:opc="http://opcfoundation.org/BinarySchema/" xmlns:ua="http://opcfoundation.org/UA/" TargetNamespace="http://opcfoundation.org/UA/GMS/">
         <opc:Import Namespace="http://opcfoundation.org/UA/"/>
         <opc:StructuredType BaseType="ua:ExtensionObject" Name="WorkspaceType"/>
         <opc:StructuredType BaseType="tns:WorkspaceType" Name="CartesianWorkspaceType">
          <opc:Field TypeName="opc:Double" Name="Length"/>
          <opc:Field TypeName="opc:Double" Name="Width"/>
          <opc:Field TypeName="opc:Double" Name="Height"/>
         </opc:StructuredType>
         <opc:StructuredType BaseType="tns:WorkspaceType" Name="CylindricalWorkspaceType">
          <opc:Field TypeName="opc:Double" Name="Length"/>
          <opc:Field TypeName="opc:Double" Name="Radius"/>
         </opc:StructuredType>
         <opc:EnumeratedType LengthInBits="32" Name="MeasurementReasonEnum">
          <opc:EnumeratedValue Name="ContinuousMeasurements" Value="0"/>
          <opc:EnumeratedValue Name="SpecialMeasurement" Value="1"/>
          <opc:EnumeratedValue Name="AuditMeasurement" Value="2"/>
          <opc:EnumeratedValue Name="MinMastering" Value="3"/>
          <opc:EnumeratedValue Name="MedMastering" Value="4"/>
          <opc:EnumeratedValue Name="MaxMastering" Value="5"/>
         </opc:EnumeratedType>
         <opc:EnumeratedType LengthInBits="32" Name="ToleranceLimitEnum">
          <opc:EnumeratedValue Name="NoLimit" Value="0"/>
          <opc:EnumeratedValue Name="LimitValue" Value="1"/>
          <opc:EnumeratedValue Name="NaturalLimit" Value="2"/>
         </opc:EnumeratedType>
         <opc:EnumeratedType LengthInBits="32" Name="ToolAlignmentState">
          <opc:EnumeratedValue Name="Fixed" Value="0"/>
          <opc:EnumeratedValue Name="Indexed" Value="1"/>
          <opc:EnumeratedValue Name="Continuous" Value="2"/>
         </opc:EnumeratedType>
         <opc:EnumeratedType LengthInBits="32" Name="ToolIsQualifiedStatus">
          <opc:EnumeratedValue Name="Qualified" Value="0"/>
          <opc:EnumeratedValue Name="Imprecise" Value="1"/>
          <opc:EnumeratedValue Name="NotQualified" Value="2"/>
         </opc:EnumeratedType>
        </opc:TypeDictionary>
        """;
        public const string MachineryResultTypeSystem = """
        <opc:TypeDictionary xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:tns="http://opcfoundation.org/UA/Machinery/Result/" DefaultByteOrder="LittleEndian" xmlns:opc="http://opcfoundation.org/BinarySchema/" xmlns:ua="http://opcfoundation.org/UA/" TargetNamespace="http://opcfoundation.org/UA/Machinery/Result/">
         <opc:Import Namespace="http://opcfoundation.org/UA/"/>
         <opc:StructuredType BaseType="ua:ExtensionObject" Name="BaseResultTransferOptionsDataType">
          <opc:Documentation>Abstract type containing information which file should be provided.</opc:Documentation>
          <opc:Field TypeName="opc:CharArray" Name="ResultId"/>
         </opc:StructuredType>
         <opc:StructuredType BaseType="tns:BaseResultTransferOptionsDataType" Name="ResultTransferOptionsDataType">
          <opc:Documentation>Contains information which file should be provided.</opc:Documentation>
          <opc:Field SourceType="tns:BaseResultTransferOptionsDataType" TypeName="opc:CharArray" Name="ResultId"/>
         </opc:StructuredType>
         <opc:StructuredType BaseType="ua:ExtensionObject" Name="ProcessingTimesDataType">
          <opc:Documentation>Contains measured times that were generated during the execution of a recipe.</opc:Documentation>
          <opc:Field TypeName="opc:Bit" Name="AcquisitionDurationSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="ProcessingDurationSpecified"/>
          <opc:Field Length="30" TypeName="opc:Bit" Name="Reserved1"/>
          <opc:Field TypeName="opc:DateTime" Name="StartTime"/>
          <opc:Field TypeName="opc:DateTime" Name="EndTime"/>
          <opc:Field SwitchField="AcquisitionDurationSpecified" TypeName="opc:Double" Name="AcquisitionDuration"/>
          <opc:Field SwitchField="ProcessingDurationSpecified" TypeName="opc:Double" Name="ProcessingDuration"/>
         </opc:StructuredType>
         <opc:StructuredType BaseType="ua:ExtensionObject" Name="ResultDataType">
          <opc:Documentation>Contains fields that were created during the execution of a recipe.</opc:Documentation>
          <opc:Field TypeName="ua:ExtensionObject" Name="ResultMetaData"/>
          <opc:Field TypeName="opc:Int32" Name="NoOfResultContent"/>
          <opc:Field LengthField="NoOfResultContent" TypeName="ua:Variant" Name="ResultContent"/>
         </opc:StructuredType>
         <opc:StructuredType BaseType="ua:ExtensionObject" Name="ResultMetaDataType">
          <opc:Documentation>Meta data of a result, describing the result.</opc:Documentation>
          <opc:Field TypeName="opc:Bit" Name="HasTransferableDataOnFileSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="IsPartialSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="IsSimulatedSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="ResultStateSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="StepIdSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="PartIdSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="ExternalRecipeIdSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="InternalRecipeIdSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="ProductIdSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="ExternalConfigurationIdSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="InternalConfigurationIdSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="JobIdSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="CreationTimeSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="ProcessingTimesSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="ResultUriSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="ResultEvaluationSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="ResultEvaluationCodeSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="ResultEvaluationDetailsSpecified"/>
          <opc:Field TypeName="opc:Bit" Name="FileFormatSpecified"/>
          <opc:Field Length="13" TypeName="opc:Bit" Name="Reserved1"/>
          <opc:Field TypeName="opc:CharArray" Name="ResultId"/>
          <opc:Field SwitchField="HasTransferableDataOnFileSpecified" TypeName="opc:Boolean" Name="HasTransferableDataOnFile"/>
          <opc:Field SwitchField="IsPartialSpecified" TypeName="opc:Boolean" Name="IsPartial"/>
          <opc:Field SwitchField="IsSimulatedSpecified" TypeName="opc:Boolean" Name="IsSimulated"/>
          <opc:Field SwitchField="ResultStateSpecified" TypeName="opc:Int32" Name="ResultState"/>
          <opc:Field SwitchField="StepIdSpecified" TypeName="opc:CharArray" Name="StepId"/>
          <opc:Field SwitchField="PartIdSpecified" TypeName="opc:CharArray" Name="PartId"/>
          <opc:Field SwitchField="ExternalRecipeIdSpecified" TypeName="opc:CharArray" Name="ExternalRecipeId"/>
          <opc:Field SwitchField="InternalRecipeIdSpecified" TypeName="opc:CharArray" Name="InternalRecipeId"/>
          <opc:Field SwitchField="ProductIdSpecified" TypeName="opc:CharArray" Name="ProductId"/>
          <opc:Field SwitchField="ExternalConfigurationIdSpecified" TypeName="opc:CharArray" Name="ExternalConfigurationId"/>
          <opc:Field SwitchField="InternalConfigurationIdSpecified" TypeName="opc:CharArray" Name="InternalConfigurationId"/>
          <opc:Field SwitchField="JobIdSpecified" TypeName="opc:CharArray" Name="JobId"/>
          <opc:Field SwitchField="CreationTimeSpecified" TypeName="opc:DateTime" Name="CreationTime"/>
          <opc:Field SwitchField="ProcessingTimesSpecified" TypeName="tns:ProcessingTimesDataType" Name="ProcessingTimes"/>
          <opc:Field SwitchField="ResultUriSpecified" TypeName="opc:Int32" Name="NoOfResultUri"/>
          <opc:Field LengthField="NoOfResultUri" SwitchField="ResultUriSpecified" TypeName="opc:CharArray" Name="ResultUri"/>
          <opc:Field SwitchField="ResultEvaluationSpecified" TypeName="tns:ResultEvaluationEnum" Name="ResultEvaluation"/>
          <opc:Field SwitchField="ResultEvaluationCodeSpecified" TypeName="opc:Int64" Name="ResultEvaluationCode"/>
          <opc:Field SwitchField="ResultEvaluationDetailsSpecified" TypeName="ua:LocalizedText" Name="ResultEvaluationDetails"/>
          <opc:Field SwitchField="FileFormatSpecified" TypeName="opc:Int32" Name="NoOfFileFormat"/>
          <opc:Field LengthField="NoOfFileFormat" SwitchField="FileFormatSpecified" TypeName="opc:CharArray" Name="FileFormat"/>
         </opc:StructuredType>
         <opc:EnumeratedType LengthInBits="32" Name="ResultEvaluationEnum">
          <opc:Documentation>Indicates whether a result was in tolerance</opc:Documentation>
          <opc:EnumeratedValue Name="Undefined" Value="0"/>
          <opc:EnumeratedValue Name="OK" Value="1"/>
          <opc:EnumeratedValue Name="NotOK" Value="2"/>
          <opc:EnumeratedValue Name="NotDecidable" Value="3"/>
         </opc:EnumeratedType>
        </opc:TypeDictionary>
        """;
    }
}
