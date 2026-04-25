#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Moq;
using Opc.Ua.Client.Nodes;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using System.Xml;
using System.Linq;

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    [TestFixture]
    public class DataTypeDescriptionResolverTests
    {
        [SetUp]
        public void SetUp()
        {
            m_nodeCacheMock = new Mock<INodeCache>();
            m_contextMock = new Mock<IServiceMessageContext>();
            m_loggerMock = new Mock<ILogger<DataTypeDescriptionResolver>>();
            m_dataTypeSystemsMock = new Mock<IDataTypeSystemManager>();
            m_factoryMock = new Mock<IEncodeableFactory>();
            m_contextMock.Setup(c => c.Factory).Returns(m_factoryMock.Object);
            m_contextMock.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        }

        [Test]
        public void AddEncodeableTypeShouldAddTypeToFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var systemType = typeof(StructureValue);

            // Act
            sut.AddEncodeableType(systemType);

            // Assert
            m_factoryMock.Verify(f => f.AddEncodeableType(systemType), Times.Once);
        }

        [Test]
        public void AddEncodeableTypeShouldAddTypeWithEncodingIdToFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var systemType = typeof(StructureValue);
            var encodingId = ExpandedNodeId.Parse("i=1024");

            // Act
            sut.AddEncodeableType(encodingId, systemType);

            // Assert
            m_contextMock.Verify(c => c.Factory.AddEncodeableType(encodingId, systemType), Times.Once);
        }

        [Test]
        public void AddEncodeableTypesShouldAddAssemblyToFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var assembly = typeof(StructureValue).Assembly;

            // Act
            sut.AddEncodeableTypes(assembly);

            // Assert
            m_contextMock.Verify(c => c.Factory.AddEncodeableTypes(assembly), Times.Once);
        }

        [Test]
        public void AddEncodeableTypesShouldAddSystemTypesToFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var systemTypes = new List<Type> { typeof(StructureValue), typeof(EnumValue) };

            // Act
            sut.AddEncodeableTypes(systemTypes);

            // Assert
            m_contextMock.Verify(c => c.Factory.AddEncodeableTypes(systemTypes), Times.Once);
        }

        [Test]
        public void AddEncodeableTypesWithAssemblyShouldAddTypesToFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var assembly = Assembly.GetExecutingAssembly();

            // Act
            sut.AddEncodeableTypes(assembly);

            // Assert
            m_factoryMock.Verify(f => f.AddEncodeableTypes(assembly), Times.Once);
        }

        [Test]
        public void AddEncodeableTypesWithSystemTypesShouldAddTypesToFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var systemTypes = new List<Type> { typeof(StructureValue), typeof(EnumValue) };

            // Act
            sut.AddEncodeableTypes(systemTypes);

            // Assert
            m_factoryMock.Verify(f => f.AddEncodeableTypes(systemTypes), Times.Once);
        }

        [Test]
        public void AddEncodeableTypeWithEncodingIdShouldAddTypeToFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var encodingId = ExpandedNodeId.Parse("i=1004");
            var systemType = typeof(StructureValue);

            // Act
            sut.AddEncodeableType(encodingId, systemType);

            // Assert
            m_factoryMock.Verify(f => f.AddEncodeableType(encodingId, systemType), Times.Once);
        }

        [Test]
        public async Task GetDataTypeAsyncShouldReturnDataTypeNodeWhenNodeIsDataTypeNodeAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1021");
            var dataTypeNode = new DataTypeNode { NodeId = typeId };
            m_nodeCacheMock.Setup(nc => nc.FindAsync(It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);

            // Act
            var result = await sut.GetDataTypeAsync(typeId, CancellationToken.None);

            // Assert
            Assert.That(result, Is.EqualTo(dataTypeNode));
            m_nodeCacheMock.Verify(nc => nc.FindAsync(It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task GetDataTypeAsyncShouldReturnDataTypeNodeWhenNodeIsEncodingNodeAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var encodingVariableId = NodeId.Parse("i=1026");
            var variableNode = new VariableNode { NodeId = encodingVariableId, DataType = DataTypeIds.String };
            var dataTypeId = NodeId.Parse("i=1028");
            var dataTypeNode = new DataTypeNode { NodeId = dataTypeId };

            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(
                    new ExpandedNodeId(encodingVariableId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(variableNode);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(encodingVariableId),
                    ReferenceTypeIds.HasEncoding,
                    true, false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)[dataTypeNode]);
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(
                    new ExpandedNodeId(dataTypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);

            // Act
            var result = await sut.GetDataTypeAsync(encodingVariableId, CancellationToken.None);

            // Assert
            Assert.That(result, Is.EqualTo(dataTypeNode));
            m_nodeCacheMock
                .Verify(nc => nc.FindAsync(
                    It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            m_nodeCacheMock
                .Verify(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(encodingVariableId), ReferenceTypeIds.HasEncoding, true, false,
                    It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task GetDataTypeAsyncShouldReturnDataTypeNodeWhenNodeIsVariableNodeAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1022");
            var dataTypeId = NodeId.Parse("i=1023");
            var variableNode = new VariableNode { NodeId = typeId, DataType = dataTypeId };
            var dataTypeNode = new DataTypeNode { NodeId = dataTypeId };

            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(variableNode);
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(new ExpandedNodeId(dataTypeId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    true, false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty);

            // Act
            var result = await sut.GetDataTypeAsync(typeId, CancellationToken.None);

            // Assert
            Assert.That(result, Is.EqualTo(dataTypeNode));
            m_nodeCacheMock
                .Verify(nc => nc.FindAsync(
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Test]
        public async Task GetDataTypeAsyncShouldReturnDataTypeNodeWhenNodeIsVariableTypeNodeAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1024");
            var dataTypeId = NodeId.Parse("i=1025");
            var variableTypeNode = new VariableTypeNode { NodeId = typeId, DataType = dataTypeId };
            var dataTypeNode = new DataTypeNode { NodeId = dataTypeId };

            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(variableTypeNode);
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(new ExpandedNodeId(dataTypeId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    true, false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Never);

            // Act
            var result = await sut.GetDataTypeAsync(typeId, CancellationToken.None);

            // Assert
            Assert.That(result, Is.EqualTo(dataTypeNode));
            m_nodeCacheMock
                .Verify(nc => nc.FindAsync(
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task GetDataTypeAsyncShouldReturnNullWhenNodeIsNotDataTypeNodeOrVariableNodeOrVariableTypeNodeAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1029");
            var node = new Node { NodeId = typeId };

            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(node);

            // Act
            var result = await sut.GetDataTypeAsync(typeId, CancellationToken.None);

            // Assert
            Assert.That(result, Is.Null);
            m_nodeCacheMock
                .Verify(nc => nc.FindAsync(
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<CancellationToken>()),
                    Times.Once);
        }

        [Test]
        public void GetDataTypeDescriptionShouldReturnDescriptionWhenTypeIsKnown()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1011");
            var structureDescription = StructureDescription.Create(sut, typeId,
                new StructureDefinition(), new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null, false);
            sut.Add(typeId, structureDescription.StructureDefinition,
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null, new XmlQualifiedName(), false);

            // Act
            var result = sut.GetDataTypeDescription(typeId);

            // Assert
            Assert.That(result, Is.EqualTo(structureDescription));
        }

        [Test]
        public void GetDataTypeDescriptionShouldReturnNullWhenTypeIsUnknown()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1012");

            // Act
            var result = sut.GetDataTypeDescription(typeId);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task GetDataTypeDescriptionAsyncShouldLoadAndReturnDescriptionWhenTypeIsUnknownAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1030");
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            var structureDefinition = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                Fields = [new StructureField { Name = "Field1", DataType = typeId, ValueRank = ValueRanks.Scalar }]
            };
            var dataTypeNode = new DataTypeNode
            {
                BrowseName = new QualifiedName("TestStruct"),
                NodeId = typeId,
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultXml), NodeId = xmlEncodingId }
                ]);

            // Act
            var result = await sut.GetDataTypeDescriptionAsync(typeId, CancellationToken.None);

            // Assert
            Assert.That(result, Is.Not.Null);
            m_nodeCacheMock
                .Verify(nc => nc.FindAsync(
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<CancellationToken>()),
                    Times.Once);
        }

        [Test]
        public async Task GetDataTypeDescriptionAsyncShouldLoadTypeFromServerWhenTypeIsUnknownAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1019");
            var structureDefinition = new StructureDefinition
            {
                Fields = [new StructureField { Name = "Field1", DataType = typeId, ValueRank = ValueRanks.Scalar }]
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyStruct"),
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            m_nodeCacheMock.Setup(nc => nc.FindAsync(It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            m_nodeCacheMock.Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty);

            // Act
            var result = await sut.GetDataTypeDescriptionAsync(typeId, CancellationToken.None);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.AssignableFrom<StructureDescription>());
            m_nodeCacheMock.Verify(nc => nc.FindAsync(It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()), Times.Once);
            m_nodeCacheMock.Verify(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                It.IsAny<NodeId>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task GetDataTypeDescriptionAsyncShouldReturnDescriptionWhenTypeIsKnownAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1007");
            var structureDescription = StructureDescription.Create(sut, typeId, new StructureDefinition(),
                new XmlQualifiedName(), ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null, false);
            sut.Add(typeId, structureDescription.StructureDefinition, ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null, new XmlQualifiedName(), false);

            // Act
            var result = await sut.GetDataTypeDescriptionAsync(typeId, CancellationToken.None);

            // Assert
            Assert.That(result, Is.EqualTo(structureDescription));
        }

        [Test]
        public async Task GetDataTypeDescriptionAsyncShouldReturnNullWhenTypeIsUnknownAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1008");

            // Act
            var result = await sut.GetDataTypeDescriptionAsync(typeId, CancellationToken.None);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task GetDefinitionsAsyncShouldReturnAllDependentDefinitions1Async()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1017");
            var dependentTypeId = NodeId.Parse("i=1018");
            var structureDefinition = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                Fields =
                [
                    new StructureField { Name = "Field1", DataType = dependentTypeId, ValueRank = ValueRanks.Scalar }
                ]
            };
            var dependentStructureDefinition = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                Fields = []
            };

            sut.Add(typeId, structureDefinition, ExpandedNodeId.Null, ExpandedNodeId.Null,
                ExpandedNodeId.Null, new XmlQualifiedName(), false);
            sut.Add(dependentTypeId, dependentStructureDefinition, ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null, new XmlQualifiedName(), false);

            // Act
            var result = await sut.GetDefinitionsAsync(typeId, CancellationToken.None);

            // Assert
            Assert.That(result, Does.ContainKey(typeId));
            Assert.That(result, Does.ContainKey(dependentTypeId));
        }

        [Test]
        public async Task GetDefinitionsAsyncShouldReturnAllDependentDefinitions2Async()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1017");
            var dependentTypeId = NodeId.Parse("i=1018");
            var structureDefinition = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                Fields =
                [
                    new StructureField { Name = "Field1", DataType = dependentTypeId, ValueRank = ValueRanks.Scalar }
                ]
            };
            var dependentEnumDefinition = new EnumDefinition();

            sut.Add(typeId, structureDefinition, ExpandedNodeId.Null, ExpandedNodeId.Null,
                ExpandedNodeId.Null, new XmlQualifiedName(), false);
            sut.Add(dependentTypeId, dependentEnumDefinition, ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null, new XmlQualifiedName(), false);

            // Act
            var result = await sut.GetDefinitionsAsync(typeId, CancellationToken.None);

            // Assert
            Assert.That(result, Does.ContainKey(typeId));
            Assert.That(result, Does.ContainKey(dependentTypeId));
        }

        [Test]
        public void GetEnumDescriptionShouldLoadAndReturnDescriptionWhenEnumIsUnknown()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1028");
            var dataTypeNode = new DataTypeNode
            {
                BrowseName = new QualifiedName("TestEnum"),
                NodeId = typeId,
                DataTypeDefinition = new ExtensionObject(new EnumDefinition())
            };
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");

            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultXml), NodeId = xmlEncodingId }
                ]);

            // Act
            var result = sut.GetEnumDescription(typeId);

            // Assert
            Assert.That(result, Is.Not.Null);
            m_nodeCacheMock.Verify(nc => nc.FindAsync(It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void GetEnumDescriptionShouldReturnEnumDescriptionWhenTypeIsKnown()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1006");
            var enumDescription = new EnumDescription(typeId, new EnumDefinition(), new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null, false);
            sut.Add(typeId, enumDescription.EnumDefinition, ExpandedNodeId.Null, ExpandedNodeId.Null,
                ExpandedNodeId.Null, new XmlQualifiedName(), false);

            // Act
            var result = sut.GetEnumDescription(typeId);

            // Assert
            Assert.That(result, Is.EqualTo(enumDescription));
        }

        [Test]
        public void GetStructureDescriptionShouldLoadAndReturnDescriptionWhenStructureIsUnknown()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
               m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1026");
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            var structureDefinition = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                Fields =
                [
                    new StructureField { Name = "Field1", DataType = typeId, ValueRank = ValueRanks.Scalar }
                ]
            };
            var dataTypeNode = new DataTypeNode
            {
                BrowseName = new QualifiedName("TestStruct"),
                NodeId = typeId,
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultXml), NodeId = xmlEncodingId }
                ]);

            // Act
            var result = sut.GetStructureDescription(typeId);

            // Assert
            Assert.That(result, Is.Not.Null);
            m_nodeCacheMock
                .Verify(nc => nc.FindAsync(
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<CancellationToken>()),
                    Times.Once);
        }

        [Test]
        public void GetStructureDescriptionShouldReturnStructureDescriptionWhenTypeIsKnown()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1005");
            var structureDescription = StructureDescription.Create(sut, typeId, new StructureDefinition(),
                new XmlQualifiedName(), ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null, false);
            sut.Add(typeId, structureDescription.StructureDefinition, ExpandedNodeId.Null, ExpandedNodeId.Null,
                ExpandedNodeId.Null, new XmlQualifiedName(), false);

            // Act
            var result = sut.GetStructureDescription(typeId);

            // Assert
            Assert.That(result, Is.EqualTo(structureDescription));
        }

        [Test]
        public void GetSystemTypeShouldReturnNullWhenTypeIsUnknown()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1023");

            // Act
            var result = sut.GetSystemType(typeId);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetSystemTypeShouldReturnEnumValueTypeWhenTypeIsEnum()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1002");
            sut.Add(typeId, new EnumDefinition(), ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null,
                new XmlQualifiedName(), false);
            var expected = typeof(EnumValue);

            // Act
            var result = sut.GetSystemType(typeId);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetSystemTypeShouldReturnEnumValueTypeWhenTypeIsKnownEnum()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1022");
            sut.Add(typeId, new EnumDefinition(), ExpandedNodeId.Null, ExpandedNodeId.Null,
                ExpandedNodeId.Null, new XmlQualifiedName("TestEnum"), false);
            var expected = typeof(EnumValue);

            // Act
            var result = sut.GetSystemType(typeId);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetSystemTypeShouldReturnStructureValueTypeWhenTypeIsKnownStructure()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1021");
            var expected = typeof(StructureValue);
            sut.Add(typeId, new StructureDefinition(), ExpandedNodeId.Null, ExpandedNodeId.Null,
                ExpandedNodeId.Null, new XmlQualifiedName("TestStructure"), false);

            // Act
            var result = sut.GetSystemType(typeId);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetSystemTypeShouldReturnStructureValueTypeWhenTypeIsStructure()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1001");
            sut.Add(typeId, new StructureDefinition(), ExpandedNodeId.Null, ExpandedNodeId.Null,
                ExpandedNodeId.Null, new XmlQualifiedName(), false);
            var expected = typeof(StructureValue);

            // Act
            var result = sut.GetSystemType(typeId);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public async Task PreloadAllDataTypeAsyncShouldLoadUnknownTypesAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1009");
            var structureDefinition = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                Fields =
                [
                    new StructureField { Name = "Field1", DataType = typeId, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Field2", DataType = typeId, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Field3", DataType = typeId, ValueRank = ValueRanks.Scalar }
                ]
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyRecursiveStruct"),
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };
            m_nodeCacheMock
                .SetupSequence(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)[dataTypeNode])
                .ReturnsAsync((ArrayOf<INode>)[dataTypeNode])
                .ReturnsAsync(ArrayOf<INode>.Empty);
            m_nodeCacheMock.Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);

            // Act
            var result = await sut.PreloadAllDataTypeAsync(CancellationToken.None);

            // Assert
            Assert.That(result, Is.True);
            var added = sut.GetStructureDescription(typeId);
            Assert.That(added, Is.Not.Null);
            Assert.That(added!.StructureDefinition, Is.EqualTo(structureDefinition));
            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task PreloadAllDataTypeAsyncReturnsFalseIfDataTypesAreMissingAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, null, m_loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var badDefinition = new StructureDefinition
            {
                Fields = [new StructureField { Name = "Field1", DataType = typeId }]
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("BadDataType"),
                DataTypeDefinition = new ExtensionObject(badDefinition)
            };
            m_nodeCacheMock
                .SetupSequence(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)[dataTypeNode])
                .ReturnsAsync(ArrayOf<INode>.Empty);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);

            // Act
            var result = await sut.PreloadAllDataTypeAsync(CancellationToken.None);

            // Assert
            Assert.That(result, Is.False);
            var added = sut.GetStructureDescription(typeId);
            Assert.That(added, Is.Null);
            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task PreloadAllDataTypeAsyncShouldNotLoadAnyDataTypesThatAreAlreadyKnownAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            m_contextMock.Setup(c => c.Factory).Returns(EncodeableFactory.Create()); // Could just mock GetSystemType
            var dataTypeNode = new DataTypeNode { NodeId = DataTypeIds.ReadAnnotationDataDetails };
            m_nodeCacheMock
                .SetupSequence(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)[dataTypeNode])
                .ReturnsAsync((ArrayOf<INode>)[dataTypeNode])
                .ReturnsAsync(ArrayOf<INode>.Empty);

            // Act
            var result = await sut.PreloadAllDataTypeAsync(CancellationToken.None);

            // Assert
            Assert.That(result, Is.True);
            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task PreloadAllDataTypeAsyncShouldPreloadAllDataTypesAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var baseTypeId = DataTypeIds.BaseDataType;
            var structureDefinition1 = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                Fields = [new StructureField { Name = "Field1", DataType = baseTypeId, ValueRank = ValueRanks.Scalar }]
            };
            var dataTypeNode1 = new DataTypeNode
            {
                BrowseName = new QualifiedName("TestStruct1"),
                NodeId = NodeId.Parse("i=1034"),
                DataTypeDefinition = new ExtensionObject(structureDefinition1)
            };
            var structureDefinition2 = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                Fields = [new StructureField { Name = "Field1", DataType = baseTypeId, ValueRank = ValueRanks.Scalar }]
            };
            var dataTypeNode2 = new DataTypeNode
            {
                BrowseName = new QualifiedName("TestStruct2"),
                NodeId = NodeId.Parse("i=1035"),
                DataTypeDefinition = new ExtensionObject(structureDefinition2)
            };
            var xmlEncodingId = NodeId.Parse("i=1018");
            var jsonEncodingId = NodeId.Parse("i=1019");

            m_nodeCacheMock
                .SetupSequence(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)[dataTypeNode1, dataTypeNode2])
                .ReturnsAsync(ArrayOf<INode>.Empty);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultXml), NodeId = xmlEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultJson), NodeId = jsonEncodingId }
                ]);

            // Act
            var result = await sut.PreloadAllDataTypeAsync(CancellationToken.None);

            // Assert
            Assert.That(result, Is.True);
            m_nodeCacheMock
                .Verify(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()),
                    Times.AtLeast(2));
        }

        [Test]
        public async Task PreloadDataTypeAsyncShouldHandleRecursiveDataTypesAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1016");
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            var jsonEncodingId = NodeId.Parse("i=1019");
            var specialEncodingId = NodeId.Parse("i=1020");
            var structureDefinition = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                Fields =
                [
                    new StructureField { Name = "Field1", DataType = typeId, ValueRank = ValueRanks.Scalar }
                ]
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyRecursiveStruct"),
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(new ExpandedNodeId(typeId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultXml), NodeId = xmlEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultJson), NodeId = jsonEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName("Special", 0), NodeId = specialEncodingId }
                ]);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            var description = sut.GetStructureDescription(typeId);

            // Assert
            Assert.That(description, Is.Not.Null);
            Assert.That(description, Is.Not.Null);
            Assert.That(description.BinaryEncodingId, Is.EqualTo(binaryEncodingId));
            Assert.That(description.JsonEncodingId, Is.EqualTo(jsonEncodingId));
            Assert.That(description.XmlEncodingId, Is.EqualTo(xmlEncodingId));

            m_nodeCacheMock
                .Verify(nc => nc.FindAsync(
                    new ExpandedNodeId(typeId),
                    It.IsAny<CancellationToken>()),
                    Times.Once);
            m_nodeCacheMock
                .Verify(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()),
                    Times.Once);
        }

        [Test]
        public async Task PreloadDataTypeAsyncShouldLoadEnumDataTypeWhenTypeIsUnknownAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1009");
            var enumDefinition = new EnumDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            var jsonEncodingId = NodeId.Parse("i=1019");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyEnum"),
                DataTypeDefinition = new ExtensionObject(enumDefinition)
            };
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(new ExpandedNodeId(typeId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            m_nodeCacheMock.Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);
            m_nodeCacheMock.Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultJson), NodeId = jsonEncodingId }
                ])
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            var enumDescription = sut.GetDataTypeDescription(typeId);
            Assert.That(enumDescription, Is.TypeOf<EnumDescription>());
            Assert.That(((EnumDescription)enumDescription!).EnumDefinition, Is.EqualTo(enumDefinition));
            Assert.That(enumDescription, Is.Not.Null);
            Assert.That(enumDescription.BinaryEncodingId, Is.EqualTo(binaryEncodingId));
            Assert.That(enumDescription.JsonEncodingId, Is.EqualTo(jsonEncodingId));
            Assert.That(enumDescription.XmlEncodingId, Is.EqualTo(ExpandedNodeId.Null));

            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task PreloadDataTypeAsyncShouldLoadStructureDataTypeWhenTypeIsUnknownAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1009");
            var structureDefinition = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                Fields =
                [
                    new StructureField { Name = "Field1", DataType = typeId, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Field2", DataType = typeId, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Field3", DataType = typeId, ValueRank = ValueRanks.Scalar }
                ]
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyRecursiveStruct"),
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(new ExpandedNodeId(typeId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            m_nodeCacheMock.Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);
            m_nodeCacheMock.Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task PreloadDataTypeAsyncShouldLoadSubTypesWhenIncludeSubTypesIsTrueAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1013");
            var subTypeId = NodeId.Parse("i=1014");
            var structureDefinition = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                Fields =
                [
                    new StructureField { Name = "Field1", DataType = typeId, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Field2", DataType = typeId, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Field3", DataType = typeId, ValueRank = ValueRanks.Scalar }
                ]
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyRecursiveStruct"),
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };
            var subTypeStructureDefinition = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                Fields = []
            };
            var subTypeNode = new DataTypeNode
            {
                NodeId = subTypeId,
                BrowseName = new QualifiedName("MySubTypeStruct"),
                DataTypeDefinition = new ExtensionObject(subTypeStructureDefinition)
            };

            m_nodeCacheMock.Setup(nc => nc.FindAsync(new ExpandedNodeId(typeId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            m_nodeCacheMock.Setup(nc => nc.FindAsync(new ExpandedNodeId(subTypeId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(subTypeNode);
            m_nodeCacheMock.SetupSequence(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)[subTypeNode])
                .ReturnsAsync(ArrayOf<INode>.Empty);
            m_nodeCacheMock.Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task PreloadDataTypeAsyncShouldNotLoadKnownTypeAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("AlreadyKnown")
            };
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(new ExpandedNodeId(typeId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            m_factoryMock.Setup(f => f.GetSystemType(typeId)).Returns(typeof(StructureValue));

            // Act
            await sut.PreloadDataTypeAsync(typeId, false, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
            m_factoryMock.Verify();
        }

        [Test]
        public async Task PreloadDataTypeAsyncShouldNotLoadSubTypesWhenIncludeSubTypesIsFalseAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1015");
            var structureDefinition = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                Fields =
                [
                    new StructureField { Name = "Field1", DataType = typeId, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Field2", DataType = typeId, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Field3", DataType = typeId, ValueRank = ValueRanks.Scalar }
                ]
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyStruct"),
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(new ExpandedNodeId(typeId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, false, CancellationToken.None);

            // Assert
            m_nodeCacheMock
                .Verify(nc => nc.FindAsync(
                    new ExpandedNodeId(typeId),
                    It.IsAny<CancellationToken>()), Times.Once);
            m_nodeCacheMock
                .Verify(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task PreloadDataTypeAsyncShouldNotLoadUnknownTypesWithSubtypesAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);

            // Act
            await sut.PreloadDataTypeAsync(DataTypeIds.ReadAnnotationDataDetails, true, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task PreloadDataTypeAsyncShouldNotLoadUnknownTypesWithoutSubtypesAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);

            // Act
            await sut.PreloadDataTypeAsync(DataTypeIds.ReadAnnotationDataDetails, false, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task PreloadDataTypeAsyncShouldPreloadDataTypeOfVariableAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1031");
            var dataTypeNode = new DataTypeNode
            {
                BrowseName = new QualifiedName("TestEnum"),
                NodeId = typeId,
                DataTypeDefinition = new ExtensionObject(new EnumDefinition())
            };
            var variable = new VariableNode
            {
                NodeId = NodeId.Parse("i=1032"),
                DataType = typeId
            };

            m_nodeCacheMock
                .SetupSequence(nc => nc.FindAsync(
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(variable)
                .ReturnsAsync(dataTypeNode);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    true,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty);

            // Act
            await sut.PreloadDataTypeAsync(typeId, false, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task PreloadDataTypeAsyncShouldPreloadDataTypeOfVariableAndSkipKnownFieldDataTypesAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            m_factoryMock.Setup(e => e.GetSystemType(DataTypeIds.ReadValueId)).Returns(typeof(ReadValueId));
            var typeId = NodeId.Parse("i=1031");
            var structureDefinition = new StructureDefinition
            {
                Fields = [new StructureField { Name = "Field1", DataType = DataTypeIds.ReadValueId, ValueRank = ValueRanks.Scalar }]
            };
            var dataTypeNode = new DataTypeNode
            {
                BrowseName = new QualifiedName("TestStructure"),
                NodeId = typeId,
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };
            var variable = new VariableNode
            {
                NodeId = NodeId.Parse("i=1032"),
                DataType = typeId
            };
            m_nodeCacheMock
                .SetupSequence(nc => nc.FindAsync(
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(variable)
                .ReturnsAsync(dataTypeNode);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    true,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, false, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task PreloadDataTypeAsyncShouldPreloadSubTypesWhenIncludeSubTypesIsTrueAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1032");
            var subTypeId = NodeId.Parse("i=1033");
            var structureDefinition = new StructureDefinition
            {
                Fields = [new StructureField { Name = "Field1", DataType = typeId, ValueRank = ValueRanks.Scalar }]
            };
            var dataTypeNode = new DataTypeNode
            {
                BrowseName = new QualifiedName("TestStruct"),
                NodeId = typeId,
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };
            var subTypeDefinition = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                Fields =
                [
                    new StructureField { Name = "Field1", DataType = typeId, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Field2", DataType = typeId, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Field3", DataType = typeId, ValueRank = ValueRanks.Scalar }
                ]
            };
            var subTypeNode = new DataTypeNode
            {
                BrowseName = new QualifiedName("SubTestStruct"),
                NodeId = subTypeId,
                DataTypeDefinition = new ExtensionObject(subTypeDefinition)
            };
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");

            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(
                    It.Is<ExpandedNodeId>(id => id == new ExpandedNodeId(typeId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(
                    new ExpandedNodeId(subTypeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(subTypeNode);
            m_nodeCacheMock
                .SetupSequence(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)[subTypeNode])
                .ReturnsAsync(ArrayOf<INode>.Empty);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultXml), NodeId = xmlEncodingId }
                ]);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            m_nodeCacheMock
                .Verify(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Test]
        public async Task PreloadDataTypeAsyncShouldPreloadSubTypesWhenStructureCanContainThemAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var typeId = NodeId.Parse("i=1032");
            var subTypeId = NodeId.Parse("i=1033");
            var structureDefinition = new StructureDefinition
            {
                StructureType = StructureType.UnionWithSubtypedValues,
                Fields = [new StructureField { Name = "Field1", DataType = typeId, ValueRank = ValueRanks.Scalar }]
            };
            var dataTypeNode = new DataTypeNode
            {
                BrowseName = new QualifiedName("TestStruct"),
                NodeId = typeId,
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };
            var subTypeDefinition = new StructureDefinition
            {
                BaseDataType = NodeId.Parse("i=555"),
                StructureType = StructureType.StructureWithSubtypedValues,
                Fields =
                [
                    new StructureField { Name = "Field1", DataType = typeId, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Field2", DataType = typeId, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Field3", DataType = typeId, ValueRank = ValueRanks.Scalar }
                ]
            };
            var subTypeNode = new DataTypeNode
            {
                BrowseName = new QualifiedName("SubTestStruct"),
                NodeId = subTypeId,
                DataTypeDefinition = new ExtensionObject(subTypeDefinition)
            };
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");

            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(
                    It.Is<ExpandedNodeId>(id => id == new ExpandedNodeId(typeId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Exactly(5));
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.Is<ArrayOf<ExpandedNodeId>>(i => i.Count == 1),
                    It.IsAny<ArrayOf<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)[subTypeNode])
                .Verifiable(Times.Exactly(4));
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.Is<ArrayOf<ExpandedNodeId>>(i => !i.Contains(new ExpandedNodeId(typeId))),
                    It.IsAny<ArrayOf<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Exactly(4));
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ExpandedNodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultXml), NodeId = xmlEncodingId }
                ]);

            // Act
            await sut.PreloadDataTypeAsync(typeId, false, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task PreloadDataTypeAsyncDoesNotAddMalformedTypesAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, null, m_loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var badDefinition = new StructureDefinition
            {
                Fields = [new StructureField { Name = "", DataType = typeId, ValueRank = ValueRanks.TwoDimensions }]
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("BadDataType"),
                DataTypeDefinition = new ExtensionObject(badDefinition)
            };
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(
                    It.Is<ExpandedNodeId>(id => id == new ExpandedNodeId(typeId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            var added = sut.GetDataTypeDescription(typeId);
            Assert.That(added, Is.Null);
            m_nodeCacheMock.Verify();
        }

        [Test]
        public async Task PreloadStructureDataTypeAsyncShouldFallbackToLegacyTypeSystemIfDataTypeIsNotReadableAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyStructure")
            };
            var binaryDefinition = new StructureDefinition();
            var xmlDefinition = new StructureDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(
                    It.Is<ExpandedNodeId>(id => id == new ExpandedNodeId(typeId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultXml), NodeId = xmlEncodingId }
                ])
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultBinary),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(binaryDefinition,
                    new XmlQualifiedName(), binaryEncodingId))
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultXml),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(xmlDefinition,
                    new XmlQualifiedName(), xmlEncodingId))
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
            m_dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            Assert.That(type, Is.TypeOf<StructureDescription.Structure>());
            Assert.That(((StructureDescription)type!).StructureDefinition, Is.EqualTo(binaryDefinition));
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            Assert.That(binary, Is.TypeOf<StructureDescription.Structure>());
            Assert.That(((StructureDescription)binary!).StructureDefinition, Is.EqualTo(binaryDefinition));
            var xml = sut.GetDataTypeDescription(xmlEncodingId);
            Assert.That(xml, Is.TypeOf<StructureDescription.Structure>());
            Assert.That(((StructureDescription)xml!).StructureDefinition, Is.EqualTo(xmlDefinition));
        }

        [Test]
        public async Task PreloadStructureDataTypeAsyncShouldFallbackToLegacyTypeSystemIfDataTypeIsNotReadableJustBinaryAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyStructure")
            };
            var binaryDefinition = new StructureDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(
                    It.Is<ExpandedNodeId>(id => id == new ExpandedNodeId(typeId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultXml), NodeId = xmlEncodingId }
                ])
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultBinary),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(binaryDefinition,
                    new XmlQualifiedName(), binaryEncodingId))
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultXml),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DictionaryDataTypeDefinition)null!)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
            m_dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            Assert.That(type, Is.TypeOf<StructureDescription.Structure>());
            Assert.That(((StructureDescription)type!).StructureDefinition, Is.EqualTo(binaryDefinition));
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            Assert.That(binary, Is.TypeOf<StructureDescription.Structure>());
            Assert.That(((StructureDescription)binary!).StructureDefinition, Is.EqualTo(binaryDefinition));
            var xml = sut.GetDataTypeDescription(xmlEncodingId);
            Assert.That(xml, Is.TypeOf<StructureDescription.Structure>());
            Assert.That(((StructureDescription)xml!).StructureDefinition, Is.EqualTo(binaryDefinition));
        }

        [Test]
        public async Task PreloadStructureDataTypeAsyncShouldFallbackToLegacyTypeSystemIfDataTypeIsNotReadableJustXmlAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyStructure")
            };
            var xmlDefinition = new StructureDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(
                    It.Is<ExpandedNodeId>(id => id == new ExpandedNodeId(typeId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultXml), NodeId = xmlEncodingId }
                ])
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultXml),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(xmlDefinition,
                    new XmlQualifiedName(), binaryEncodingId))
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultBinary),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DictionaryDataTypeDefinition)null!)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
            m_dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            Assert.That(type, Is.TypeOf<StructureDescription.Structure>());
            Assert.That(((StructureDescription)type!).StructureDefinition, Is.EqualTo(xmlDefinition));
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            Assert.That(binary, Is.TypeOf<StructureDescription.Structure>());
            Assert.That(((StructureDescription)binary!).StructureDefinition, Is.EqualTo(xmlDefinition));
            var xml = sut.GetDataTypeDescription(xmlEncodingId);
            Assert.That(xml, Is.TypeOf<StructureDescription.Structure>());
            Assert.That(((StructureDescription)xml!).StructureDefinition, Is.EqualTo(xmlDefinition));
        }

        [Test]
        public async Task PreloadEnumDataTypeAsyncShouldFallbackToLegacyTypeSystemIfDataTypeIsNotReadableAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyEnum")
            };
            var binaryDefinition = new EnumDefinition();
            var xmlDefinition = new EnumDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(
                    It.Is<ExpandedNodeId>(id => id == new ExpandedNodeId(typeId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultXml), NodeId = xmlEncodingId }
                ])
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultBinary),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(binaryDefinition,
                    new XmlQualifiedName(), binaryEncodingId))
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultXml),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(xmlDefinition,
                    new XmlQualifiedName(), xmlEncodingId))
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
            m_dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            Assert.That(type, Is.TypeOf<EnumDescription>());
            Assert.That(((EnumDescription)type!).EnumDefinition, Is.EqualTo(binaryDefinition));
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            Assert.That(binary, Is.TypeOf<EnumDescription>());
            Assert.That(((EnumDescription)binary!).EnumDefinition, Is.EqualTo(binaryDefinition));
            var xml = sut.GetDataTypeDescription(xmlEncodingId);
            Assert.That(xml, Is.TypeOf<EnumDescription>());
            Assert.That(((EnumDescription)xml!).EnumDefinition, Is.EqualTo(xmlDefinition));
        }

        [Test]
        public async Task PreloadEnumDataTypeAsyncShouldFallbackToLegacyTypeSystemIfDataTypeIsNotReadableJustBinaryAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyEnum")
            };
            var binaryDefinition = new EnumDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(
                    It.Is<ExpandedNodeId>(id => id == new ExpandedNodeId(typeId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultXml), NodeId = xmlEncodingId }
                ])
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultBinary),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(binaryDefinition,
                    new XmlQualifiedName(), binaryEncodingId))
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultXml),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DictionaryDataTypeDefinition)null!)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
            m_dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            Assert.That(type, Is.TypeOf<EnumDescription>());
            Assert.That(((EnumDescription)type!).EnumDefinition, Is.EqualTo(binaryDefinition));
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            Assert.That(binary, Is.TypeOf<EnumDescription>());
            Assert.That(((EnumDescription)binary!).EnumDefinition, Is.EqualTo(binaryDefinition));
            var xml = sut.GetDataTypeDescription(xmlEncodingId);
            Assert.That(xml, Is.TypeOf<EnumDescription>());
            Assert.That(((EnumDescription)xml!).EnumDefinition, Is.EqualTo(binaryDefinition));
        }

        [Test]
        public async Task PreloadEnumDataTypeAsyncShouldFallbackToLegacyTypeSystemIfDataTypeIsNotReadableJustXmlAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyEnum")
            };
            var xmlDefinition = new EnumDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(
                    It.Is<ExpandedNodeId>(id => id == new ExpandedNodeId(typeId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultXml), NodeId = xmlEncodingId }
                ])
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultXml),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(xmlDefinition,
                    new XmlQualifiedName(), binaryEncodingId))
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultBinary),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DictionaryDataTypeDefinition)null!)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
            m_dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            Assert.That(type, Is.TypeOf<EnumDescription>());
            Assert.That(((EnumDescription)type!).EnumDefinition, Is.EqualTo(xmlDefinition));
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            Assert.That(binary, Is.TypeOf<EnumDescription>());
            Assert.That(((EnumDescription)binary!).EnumDefinition, Is.EqualTo(xmlDefinition));
            var xml = sut.GetDataTypeDescription(xmlEncodingId);
            Assert.That(xml, Is.TypeOf<EnumDescription>());
            Assert.That(((EnumDescription)xml!).EnumDefinition, Is.EqualTo(xmlDefinition));
        }

        [Test]
        public async Task PreloadEnumDataTypeAsyncShouldFallbackToLegacyTypeSystemUsesXmlDefinitionEvenWithoutXmlEncodingIdAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyEnum")
            };
            var xmlDefinition = new EnumDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(
                    It.Is<ExpandedNodeId>(id => id == new ExpandedNodeId(typeId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId }
                ])
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultXml),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(xmlDefinition,
                    new XmlQualifiedName(), binaryEncodingId))
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultBinary),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DictionaryDataTypeDefinition)null!)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
            m_dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            Assert.That(type, Is.TypeOf<EnumDescription>());
            Assert.That(((EnumDescription)type!).EnumDefinition, Is.EqualTo(xmlDefinition));
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            Assert.That(binary, Is.TypeOf<EnumDescription>());
            Assert.That(((EnumDescription)binary!).EnumDefinition, Is.EqualTo(xmlDefinition));
        }

        [Test]
        public async Task PreloadEnumDataTypeAsyncShouldFallbackToLegacyTypeSystemAndFinallyFailWithUnknownTypeAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("MyEnum")
            };
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            m_nodeCacheMock
                .Setup(nc => nc.FindAsync(
                    It.Is<ExpandedNodeId>(id => id == new ExpandedNodeId(typeId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArrayOf<INode>.Empty)
                .Verifiable(Times.Once);
            m_nodeCacheMock
                .Setup(nc => nc.FindReferencesAsync(
                    new ExpandedNodeId(typeId),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<INode>)                [
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultBinary), NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName(BrowseNames.DefaultXml), NodeId = xmlEncodingId }
                ])
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultXml),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DictionaryDataTypeDefinition)null!)
                .Verifiable(Times.Once);
            m_dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    new QualifiedName(BrowseNames.DefaultBinary),
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DictionaryDataTypeDefinition)null!)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            m_nodeCacheMock.Verify();
            m_dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            Assert.That(type, Is.Null);
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            Assert.That(binary, Is.Null);
            var xml = sut.GetDataTypeDescription(xmlEncodingId);
            Assert.That(xml, Is.Null);
        }

        [Test]
        public void GetDataTypeDefinitionShouldReturnEnumDefinitionWhenBodyIsEnumDefinitionWithValidFields()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var enumFields = (ArrayOf<EnumField>)[
                new EnumField { Name = "Value1", Value = 1 },
                new EnumField { Name = "Value2", Value = 2 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(enumDefinition)
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            Assert.That(result, Is.EqualTo(enumDefinition));
        }

        [Test]
        public void GetDataTypeDefinitionShouldReturnEnumDefinitionWhenBodyIsEnumDefinitionWithEmptyFields()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var enumDefinition = new EnumDefinition { Fields = [] };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(enumDefinition)
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            Assert.That(result, Is.EqualTo(enumDefinition));
        }

        [Test]
        public void GetDataTypeDefinitionShouldReturnEnumDefinitionWhenBodyIsEnumDefinitionWithDuplicateFieldNames()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var enumFields = (ArrayOf<EnumField>)[
                new EnumField { Name = "Duplicate", Value = 1 },
                new EnumField { Name = "Duplicate", Value = 2 }
            ];
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(enumDefinition)
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            Assert.That(result, Is.EqualTo(enumDefinition));
        }

        [Test]
        public void GetDataTypeDefinitionShouldReturnStructureDefinitionWhenBodyIsStructureDefinitionWithValidFields()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var structureFields = (ArrayOf<StructureField>)[
                new StructureField { Name = "Field1", DataType = new NodeId(2), ValueRank = ValueRanks.Scalar },
                new StructureField { Name = "Field2", DataType = new NodeId(3), ValueRank = ValueRanks.OneDimension }
            ];
            var structureDefinition = new StructureDefinition
            {
                Fields = structureFields,
                StructureType = StructureType.Structure
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            Assert.That(result, Is.EqualTo(structureDefinition));
        }

        [Test]
        public void GetDataTypeDefinitionShouldReturnStructureDefinitionWhenBodyIsStructureDefinitionWithEmptyFields()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var structureDefinition = new StructureDefinition
            {
                Fields = [],
                StructureType = StructureType.Structure
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            Assert.That(result, Is.EqualTo(structureDefinition));
        }

        [Test]
        public void GetDataTypeDefinitionShouldReturnNullWhenStructureDefinitionHasInvalidStructureType()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var structureDefinition = new StructureDefinition
            {
                StructureType = (StructureType)999, // Invalid structure type
                Fields = []
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetDataTypeDefinitionShouldReturnNullWhenStructureDefinitionFieldHasMissingName()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var structureFields = (ArrayOf<StructureField>)[
                new StructureField { Name = null, DataType = new NodeId(2), ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                Fields = structureFields,
                StructureType = StructureType.Structure
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetDataTypeDefinitionShouldReturnNullWhenStructureDefinitionFieldHasMissingDataType()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var structureFields = (ArrayOf<StructureField>)[
                new StructureField { Name = "Field1", DataType = NodeId.Null, ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition
            {
                Fields = structureFields,
                StructureType = StructureType.Structure
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetDataTypeDefinitionShouldReturnStructureDefinitionWhenBodyIsStructureDefinitionWithDuplicateFieldNames()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var structureFields = (ArrayOf<StructureField>)[
                new StructureField { Name = "Duplicate", DataType = new NodeId(2), ValueRank = ValueRanks.Scalar },
                new StructureField { Name = "Duplicate", DataType = new NodeId(2), ValueRank = ValueRanks.Scalar }
            ];
            var structureDefinition = new StructureDefinition { Fields = structureFields };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            Assert.That(result, Is.EqualTo(structureDefinition));
        }

        [Test]
        public void GetDataTypeDefinitionShouldReturnNullWhenDataTypeDefinitionIsNull()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = ExtensionObject.Null
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetDataTypeDefinitionShouldReturnNullWhenDataTypeDefinitionBodyIsNull()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject() // Body is null
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetDataTypeDefinitionShouldThrowExceptionWhenBodyIsUnsupportedType()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                m_nodeCacheMock.Object, m_contextMock.Object, m_dataTypeSystemsMock.Object, m_loggerMock.Object);
            var unsupportedDefinition = new ReadValueId(); // Unsupported type
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(unsupportedDefinition)
            };

            // Act
            Action act = () => sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("*Data type definition information provided by server is not valid*"));
        }

        private Mock<IServiceMessageContext> m_contextMock;
        private Mock<IDataTypeSystemManager> m_dataTypeSystemsMock;
        private Mock<IEncodeableFactory> m_factoryMock;
        private Mock<ILogger<DataTypeDescriptionResolver>> m_loggerMock;
        private Mock<INodeCache> m_nodeCacheMock;
    }
}
#endif
