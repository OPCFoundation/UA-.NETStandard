// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using FluentAssertions;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Opc.Ua.Client.Nodes;
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;
    using Opc.Ua;
    using System.Xml;
    using System.Linq;

    public class DataTypeDescriptionResolverTests
    {
        public DataTypeDescriptionResolverTests()
        {
            _nodeCacheMock = new Mock<INodeCache>();
            _contextMock = new Mock<IServiceMessageContext>();
            _loggerMock = new Mock<ILogger<DataTypeDescriptionResolver>>();
            _dataTypeSystemsMock = new Mock<IDataTypeSystemManager>();
            _factoryMock = new Mock<IEncodeableFactory>();
            _contextMock.Setup(c => c.Factory).Returns(_factoryMock.Object);
            _contextMock.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        }

        [Fact]
        public void GetInstanceIdShoudlBeInstanceIdOfFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            _factoryMock.Setup(x => x.InstanceId).Returns(4).Verifiable(Times.Once);

            // Act
            sut.InstanceId.Should().Be(4);

            // Assert
            _factoryMock.Verify();
        }

        [Fact]
        public void GetEncodeableTypesShouldReturnAllEncodeableTypesOfFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var type = typeof(StructureValue);
            var typeId = ExpandedNodeId.Parse("i=1024");
            _factoryMock.Setup(x => x.EncodeableTypes).Returns(new Dictionary<ExpandedNodeId, Type>
            {
                [typeId] = type
            }).Verifiable(Times.Once);

            // Act
            var encodeableTypes = sut.EncodeableTypes;

            // Assert
            encodeableTypes.Should().ContainSingle().Which.Key.Should().Be(typeId);
            encodeableTypes.Should().ContainSingle().Which.Value.Should().Be(type);
            _factoryMock.Verify();
        }

        [Fact]
        public void AddEncodeableTypeShouldAddTypeToFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var systemType = typeof(StructureValue);

            // Act
            sut.AddEncodeableType(systemType);

            // Assert
            _factoryMock.Verify(f => f.AddEncodeableType(systemType), Times.Once);
        }

        [Fact]
        public void AddEncodeableTypeShouldAddTypeWithEncodingIdToFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var systemType = typeof(StructureValue);
            var encodingId = ExpandedNodeId.Parse("i=1024");

            // Act
            sut.AddEncodeableType(encodingId, systemType);

            // Assert
            _contextMock.Verify(c => c.Factory.AddEncodeableType(encodingId, systemType), Times.Once);
        }

        [Fact]
        public void AddEncodeableTypesShouldAddAssemblyToFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var assembly = typeof(StructureValue).Assembly;

            // Act
            sut.AddEncodeableTypes(assembly);

            // Assert
            _contextMock.Verify(c => c.Factory.AddEncodeableTypes(assembly), Times.Once);
        }

        [Fact]
        public void AddEncodeableTypesShouldAddSystemTypesToFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var systemTypes = new List<Type> { typeof(StructureValue), typeof(EnumValue) };

            // Act
            sut.AddEncodeableTypes(systemTypes);

            // Assert
            _contextMock.Verify(c => c.Factory.AddEncodeableTypes(systemTypes), Times.Once);
        }

        [Fact]
        public void AddEncodeableTypesWithAssemblyShouldAddTypesToFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var assembly = Assembly.GetExecutingAssembly();

            // Act
            sut.AddEncodeableTypes(assembly);

            // Assert
            _factoryMock.Verify(f => f.AddEncodeableTypes(assembly), Times.Once);
        }

        [Fact]
        public void AddEncodeableTypesWithSystemTypesShouldAddTypesToFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var systemTypes = new List<Type> { typeof(StructureValue), typeof(EnumValue) };

            // Act
            sut.AddEncodeableTypes(systemTypes);

            // Assert
            _factoryMock.Verify(f => f.AddEncodeableTypes(systemTypes), Times.Once);
        }

        [Fact]
        public void AddEncodeableTypeWithEncodingIdShouldAddTypeToFactory()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var encodingId = ExpandedNodeId.Parse("i=1004");
            var systemType = typeof(StructureValue);

            // Act
            sut.AddEncodeableType(encodingId, systemType);

            // Assert
            _factoryMock.Verify(f => f.AddEncodeableType(encodingId, systemType), Times.Once);
        }

        [Fact]
        public void CloneShouldCreateShallowCopy()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var clone = sut.Clone();

            // Assert
            clone.Should().BeOfType<DataTypeDescriptionResolver>();
            clone.Should().NotBeSameAs(sut);
        }

        [Fact]
        public async Task GetDataTypeAsyncShouldReturnDataTypeNodeWhenNodeIsDataTypeNodeAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = NodeId.Parse("i=1021");
            var dataTypeNode = new DataTypeNode { NodeId = typeId };
            _nodeCacheMock.Setup(nc => nc.GetNodeAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);

            // Act
            var result = await sut.GetDataTypeAsync(typeId, CancellationToken.None);

            // Assert
            result.Should().Be(dataTypeNode);
            _nodeCacheMock.Verify(nc => nc.GetNodeAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetDataTypeAsyncShouldReturnDataTypeNodeWhenNodeIsEncodingNodeAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var encodingVariableId = NodeId.Parse("i=1026");
            var variableNode = new VariableNode { NodeId = encodingVariableId, DataType = DataTypeIds.String };
            var dataTypeId = NodeId.Parse("i=1028");
            var dataTypeNode = new DataTypeNode { NodeId = dataTypeId };

            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(
                    encodingVariableId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(variableNode);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    encodingVariableId,
                    ReferenceTypeIds.HasEncoding,
                    true, false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([dataTypeNode]);
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(
                    dataTypeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);

            // Act
            var result = await sut.GetDataTypeAsync(encodingVariableId, CancellationToken.None);

            // Assert
            result.Should().Be(dataTypeNode);
            _nodeCacheMock
                .Verify(nc => nc.GetNodeAsync(
                    It.IsAny<NodeId>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            _nodeCacheMock
                .Verify(nc => nc.GetReferencesAsync(
                    encodingVariableId, ReferenceTypeIds.HasEncoding, true, false,
                    It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetDataTypeAsyncShouldReturnDataTypeNodeWhenNodeIsVariableNodeAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = NodeId.Parse("i=1022");
            var dataTypeId = NodeId.Parse("i=1023");
            var variableNode = new VariableNode { NodeId = typeId, DataType = dataTypeId };
            var dataTypeNode = new DataTypeNode { NodeId = dataTypeId };

            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(variableNode);
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(dataTypeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    true, false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            // Act
            var result = await sut.GetDataTypeAsync(typeId, CancellationToken.None);

            // Assert
            result.Should().Be(dataTypeNode);
            _nodeCacheMock
                .Verify(nc => nc.GetNodeAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Fact]
        public async Task GetDataTypeAsyncShouldReturnDataTypeNodeWhenNodeIsVariableTypeNodeAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = NodeId.Parse("i=1024");
            var dataTypeId = NodeId.Parse("i=1025");
            var variableTypeNode = new VariableTypeNode { NodeId = typeId, DataType = dataTypeId };
            var dataTypeNode = new DataTypeNode { NodeId = dataTypeId };

            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(variableTypeNode);
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(dataTypeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    true, false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Never);

            // Act
            var result = await sut.GetDataTypeAsync(typeId, CancellationToken.None);

            // Assert
            result.Should().Be(dataTypeNode);
            _nodeCacheMock
                .Verify(nc => nc.GetNodeAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
            _nodeCacheMock.Verify();
        }

        [Fact]
        public async Task GetDataTypeAsyncShouldReturnNullWhenNodeIsNotDataTypeNodeOrVariableNodeOrVariableTypeNodeAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = NodeId.Parse("i=1029");
            var node = new Node { NodeId = typeId };

            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(node);

            // Act
            var result = await sut.GetDataTypeAsync(typeId, CancellationToken.None);

            // Assert
            result.Should().BeNull();
            _nodeCacheMock
                .Verify(nc => nc.GetNodeAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()),
                    Times.Once);
        }

        [Fact]
        public void GetDataTypeDescriptionShouldReturnDescriptionWhenTypeIsKnown()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1011");
            var structureDescription = StructureDescription.Create(sut, typeId,
                new StructureDefinition(), new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null, false);
            sut.Add(typeId, structureDescription.StructureDefinition,
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null, new XmlQualifiedName(), false);

            // Act
            var result = sut.GetDataTypeDescription(typeId);

            // Assert
            result.Should().BeEquivalentTo(structureDescription);
        }

        [Fact]
        public void GetDataTypeDescriptionShouldReturnNullWhenTypeIsUnknown()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1012");

            // Act
            var result = sut.GetDataTypeDescription(typeId);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task GetDataTypeDescriptionAsyncShouldLoadAndReturnDescriptionWhenTypeIsUnknownAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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

            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultXml, NodeId = xmlEncodingId }
                ]);

            // Act
            var result = await sut.GetDataTypeDescriptionAsync(typeId, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            _nodeCacheMock
                .Verify(nc => nc.GetNodeAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()),
                    Times.Once);
        }

        [Fact]
        public async Task GetDataTypeDescriptionAsyncShouldLoadTypeFromServerWhenTypeIsUnknownAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = NodeId.Parse("i=1019");
            var structureDefinition = new StructureDefinition
            {
                Fields = [new StructureField { Name = "Field1", DataType = typeId, ValueRank = ValueRanks.Scalar }]
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = "MyStruct",
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            _nodeCacheMock.Setup(nc => nc.GetNodeAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            _nodeCacheMock.Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            // Act
            var result = await sut.GetDataTypeDescriptionAsync(typeId, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeAssignableTo<StructureDescription>();
            _nodeCacheMock.Verify(nc => nc.GetNodeAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()), Times.Once);
            _nodeCacheMock.Verify(nc => nc.GetReferencesAsync(
                It.IsAny<NodeId>(),
                It.IsAny<NodeId>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetDataTypeDescriptionAsyncShouldReturnDescriptionWhenTypeIsKnownAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1007");
            var structureDescription = StructureDescription.Create(sut, typeId, new StructureDefinition(),
                new XmlQualifiedName(), ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null, false);
            sut.Add(typeId, structureDescription.StructureDefinition, ExpandedNodeId.Null,
                ExpandedNodeId.Null, ExpandedNodeId.Null, new XmlQualifiedName(), false);

            // Act
            var result = await sut.GetDataTypeDescriptionAsync(typeId, CancellationToken.None);

            // Assert
            result.Should().BeEquivalentTo(structureDescription);
        }

        [Fact]
        public async Task GetDataTypeDescriptionAsyncShouldReturnNullWhenTypeIsUnknownAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1008");

            // Act
            var result = await sut.GetDataTypeDescriptionAsync(typeId, CancellationToken.None);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task GetDefinitionsAsyncShouldReturnAllDependentDefinitions1Async()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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
            result.Should().ContainKey(typeId);
            result.Should().ContainKey(dependentTypeId);
        }

        [Fact]
        public async Task GetDefinitionsAsyncShouldReturnAllDependentDefinitions2Async()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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
            result.Should().ContainKey(typeId);
            result.Should().ContainKey(dependentTypeId);
        }

        [Fact]
        public void GetEnumDescriptionShouldLoadAndReturnDescriptionWhenEnumIsUnknown()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = NodeId.Parse("i=1028");
            var dataTypeNode = new DataTypeNode
            {
                BrowseName = new QualifiedName("TestEnum"),
                NodeId = typeId,
                DataTypeDefinition = new ExtensionObject(new EnumDefinition())
            };
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");

            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultXml, NodeId = xmlEncodingId }
                ]);

            // Act
            var result = sut.GetEnumDescription(typeId);

            // Assert
            result.Should().NotBeNull();
            _nodeCacheMock.Verify(nc => nc.GetNodeAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void GetEnumDescriptionShouldReturnEnumDescriptionWhenTypeIsKnown()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1006");
            var enumDescription = new EnumDescription(typeId, new EnumDefinition(), new XmlQualifiedName(),
                ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null, false);
            sut.Add(typeId, enumDescription.EnumDefinition, ExpandedNodeId.Null, ExpandedNodeId.Null,
                ExpandedNodeId.Null, new XmlQualifiedName(), false);

            // Act
            var result = sut.GetEnumDescription(typeId);

            // Assert
            result.Should().BeEquivalentTo(enumDescription);
        }

        [Fact]
        public void GetStructureDescriptionShouldLoadAndReturnDescriptionWhenStructureIsUnknown()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
               _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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

            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultXml, NodeId = xmlEncodingId }
                ]);

            // Act
            var result = sut.GetStructureDescription(typeId);

            // Assert
            result.Should().NotBeNull();
            _nodeCacheMock
                .Verify(nc => nc.GetNodeAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()),
                    Times.Once);
        }

        [Fact]
        public void GetStructureDescriptionShouldReturnStructureDescriptionWhenTypeIsKnown()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1005");
            var structureDescription = StructureDescription.Create(sut, typeId, new StructureDefinition(),
                new XmlQualifiedName(), ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null, false);
            sut.Add(typeId, structureDescription.StructureDefinition, ExpandedNodeId.Null, ExpandedNodeId.Null,
                ExpandedNodeId.Null, new XmlQualifiedName(), false);

            // Act
            var result = sut.GetStructureDescription(typeId);

            // Assert
            result.Should().BeEquivalentTo(structureDescription);
        }

        [Fact]
        public void GetSystemTypeShouldReturnNullWhenTypeIsUnknown()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1023");

            // Act
            var result = sut.GetSystemType(typeId);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetSystemTypeShouldReturnEnumValueTypeWhenTypeIsEnum()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1002");
            sut.Add(typeId, new EnumDefinition(), ExpandedNodeId.Null, ExpandedNodeId.Null, ExpandedNodeId.Null,
                new XmlQualifiedName(), false);
            var expected = typeof(EnumValue);

            // Act
            var result = sut.GetSystemType(typeId);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void GetSystemTypeShouldReturnEnumValueTypeWhenTypeIsKnownEnum()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1022");
            sut.Add(typeId, new EnumDefinition(), ExpandedNodeId.Null, ExpandedNodeId.Null,
                ExpandedNodeId.Null, new XmlQualifiedName("TestEnum"), false);
            var expected = typeof(EnumValue);

            // Act
            var result = sut.GetSystemType(typeId);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void GetSystemTypeShouldReturnStructureValueTypeWhenTypeIsKnownStructure()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1021");
            var expected = typeof(StructureValue);
            sut.Add(typeId, new StructureDefinition(), ExpandedNodeId.Null, ExpandedNodeId.Null,
                ExpandedNodeId.Null, new XmlQualifiedName("TestStructure"), false);

            // Act
            var result = sut.GetSystemType(typeId);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void GetSystemTypeShouldReturnStructureValueTypeWhenTypeIsStructure()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = ExpandedNodeId.Parse("i=1001");
            sut.Add(typeId, new StructureDefinition(), ExpandedNodeId.Null, ExpandedNodeId.Null,
                ExpandedNodeId.Null, new XmlQualifiedName(), false);
            var expected = typeof(StructureValue);

            // Act
            var result = sut.GetSystemType(typeId);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public async Task PreloadAllDataTypeAsyncShouldLoadUnknownTypesAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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
                BrowseName = "MyRecursiveStruct",
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };
            _nodeCacheMock
                .SetupSequence(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IReadOnlyList<INode>>([dataTypeNode]))
                .Returns(new ValueTask<IReadOnlyList<INode>>([dataTypeNode]))
                .Returns(new ValueTask<IReadOnlyList<INode>>([]));
            _nodeCacheMock.Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);

            // Act
            var result = await sut.PreloadAllDataTypeAsync(CancellationToken.None);

            // Assert
            result.Should().BeTrue();
            var added = sut.GetStructureDescription(typeId);
            added.Should().NotBeNull();
            added!.StructureDefinition.Should().BeEquivalentTo(structureDefinition);
            _nodeCacheMock.Verify();
        }

        [Fact]
        public async Task PreloadAllDataTypeAsyncReturnsFalseIfDataTypesAreMissingAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, null, _loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var badDefinition = new StructureDefinition
            {
                Fields = [new StructureField { Name = "Field1", DataType = typeId }]
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = "BadDataType",
                DataTypeDefinition = new ExtensionObject(badDefinition)
            };
            _nodeCacheMock
                .SetupSequence(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IReadOnlyList<INode>>([dataTypeNode]))
                .Returns(new ValueTask<IReadOnlyList<INode>>([]));
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);

            // Act
            var result = await sut.PreloadAllDataTypeAsync(CancellationToken.None);

            // Assert
            result.Should().BeFalse();
            var added = sut.GetStructureDescription(typeId);
            added.Should().BeNull();
            _nodeCacheMock.Verify();
        }

        [Fact]
        public async Task PreloadAllDataTypeAsyncShouldNotLoadAnyDataTypesThatAreAlreadyKnownAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            _contextMock.Setup(c => c.Factory).Returns(new EncodeableFactory()); // Could just mock GetSystemType
            var dataTypeNode = new DataTypeNode { NodeId = DataTypeIds.ReadAnnotationDataDetails };
            _nodeCacheMock
                .SetupSequence(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IReadOnlyList<INode>>([dataTypeNode]))
                .Returns(new ValueTask<IReadOnlyList<INode>>([dataTypeNode]))
                .Returns(new ValueTask<IReadOnlyList<INode>>([]));

            // Act
            var result = await sut.PreloadAllDataTypeAsync(CancellationToken.None);

            // Assert
            result.Should().BeTrue();
            _nodeCacheMock.Verify();
        }

        [Fact]
        public async Task PreloadAllDataTypeAsyncShouldPreloadAllDataTypesAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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

            _nodeCacheMock
                .SetupSequence(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([dataTypeNode1, dataTypeNode2])
                .ReturnsAsync([]);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultXml, NodeId = xmlEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultJson, NodeId = jsonEncodingId }
                ]);

            // Act
            var result = await sut.PreloadAllDataTypeAsync(CancellationToken.None);

            // Assert
            result.Should().BeTrue();
            _nodeCacheMock
                .Verify(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()),
                    Times.AtLeast(2));
        }

        [Fact]
        public async Task PreloadDataTypeAsyncShouldHandleRecursiveDataTypesAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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
                BrowseName = "MyRecursiveStruct",
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(typeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    new NodeId[] { typeId },
                    new NodeId[] { ReferenceTypeIds.HasSubtype },
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultXml, NodeId = xmlEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultJson, NodeId = jsonEncodingId },
                    new DataTypeNode { BrowseName = new QualifiedName("Special", 0), NodeId = specialEncodingId }
                ]);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            var description = sut.GetStructureDescription(typeId);

            // Assert
            description.Should().NotBeNull();
            Assert.NotNull(description);
            description.BinaryEncodingId.Should().Be(binaryEncodingId);
            description.JsonEncodingId.Should().Be(jsonEncodingId);
            description.XmlEncodingId.Should().Be(xmlEncodingId);

            _nodeCacheMock
                .Verify(nc => nc.GetNodeAsync(
                    typeId,
                    It.IsAny<CancellationToken>()),
                    Times.Once);
            _nodeCacheMock
                .Verify(nc => nc.GetReferencesAsync(
                    new NodeId[] { typeId },
                    new NodeId[] { ReferenceTypeIds.HasSubtype },
                    false,
                    false,
                    It.IsAny<CancellationToken>()),
                    Times.Once);
        }

        [Fact]
        public async Task PreloadDataTypeAsyncShouldLoadEnumDataTypeWhenTypeIsUnknownAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var typeId = NodeId.Parse("i=1009");
            var enumDefinition = new EnumDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            var jsonEncodingId = NodeId.Parse("i=1019");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = "MyEnum",
                DataTypeDefinition = new ExtensionObject(enumDefinition)
            };
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(typeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            _nodeCacheMock.Setup(nc => nc.GetReferencesAsync(
                    new NodeId[] { typeId },
                    new NodeId[] { ReferenceTypeIds.HasSubtype },
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            _nodeCacheMock.Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultJson, NodeId = jsonEncodingId }
                ])
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            var enumDescription = sut.GetDataTypeDescription(typeId);
            enumDescription.Should().BeOfType<EnumDescription>().Which.EnumDefinition.Should().Be(enumDefinition);
            Assert.NotNull(enumDescription);
            enumDescription.BinaryEncodingId.Should().Be(binaryEncodingId);
            enumDescription.JsonEncodingId.Should().Be(jsonEncodingId);
            enumDescription.XmlEncodingId.Should().Be(ExpandedNodeId.Null);

            _nodeCacheMock.Verify();
        }

        [Fact]
        public async Task PreloadDataTypeAsyncShouldLoadStructureDataTypeWhenTypeIsUnknownAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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
                BrowseName = "MyRecursiveStruct",
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(typeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            _nodeCacheMock.Setup(nc => nc.GetReferencesAsync(
                    new NodeId[] { typeId },
                    new NodeId[] { ReferenceTypeIds.HasSubtype },
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            _nodeCacheMock.Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
        }

        [Fact]
        public async Task PreloadDataTypeAsyncShouldLoadSubTypesWhenIncludeSubTypesIsTrueAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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
                BrowseName = "MyRecursiveStruct",
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
                BrowseName = "MySubTypeStruct",
                DataTypeDefinition = new ExtensionObject(subTypeStructureDefinition)
            };

            _nodeCacheMock.Setup(nc => nc.GetNodeAsync(typeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            _nodeCacheMock.Setup(nc => nc.GetNodeAsync(subTypeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(subTypeNode);
            _nodeCacheMock.SetupSequence(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    new NodeId[] { ReferenceTypeIds.HasSubtype },
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([subTypeNode])
                .ReturnsAsync([]);
            _nodeCacheMock.Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
        }

        [Fact]
        public async Task PreloadDataTypeAsyncShouldNotLoadKnownTypeAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = "AlreadyKnown"
            };
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(typeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            _factoryMock.Setup(f => f.GetSystemType(typeId)).Returns(typeof(StructureValue));

            // Act
            await sut.PreloadDataTypeAsync(typeId, false, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
            _factoryMock.Verify();
        }

        [Fact]
        public async Task PreloadDataTypeAsyncShouldNotLoadSubTypesWhenIncludeSubTypesIsFalseAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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
                BrowseName = "MyStruct",
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(typeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, false, CancellationToken.None);

            // Assert
            _nodeCacheMock
                .Verify(nc => nc.GetNodeAsync(
                    typeId,
                    It.IsAny<CancellationToken>()), Times.Once);
            _nodeCacheMock
                .Verify(nc => nc.GetReferencesAsync(
                    new NodeId[] { typeId },
                    new NodeId[] { ReferenceTypeIds.HasSubtype },
                    false,
                    false,
                    It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PreloadDataTypeAsyncShouldNotLoadUnknownTypesWithSubtypesAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);

            // Act
            await sut.PreloadDataTypeAsync(DataTypeIds.ReadAnnotationDataDetails, true, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
        }

        [Fact]
        public async Task PreloadDataTypeAsyncShouldNotLoadUnknownTypesWithoutSubtypesAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);

            // Act
            await sut.PreloadDataTypeAsync(DataTypeIds.ReadAnnotationDataDetails, false, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
        }

        [Fact]
        public async Task PreloadDataTypeAsyncShouldPreloadDataTypeOfVariableAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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

            _nodeCacheMock
                .SetupSequence(nc => nc.GetNodeAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(variable)
                .ReturnsAsync(dataTypeNode);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    true,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            // Act
            await sut.PreloadDataTypeAsync(typeId, false, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
        }

        [Fact]
        public async Task PreloadDataTypeAsyncShouldPreloadDataTypeOfVariableAndSkipKnownFieldDataTypesAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            _factoryMock.Setup(e => e.GetSystemType(DataTypeIds.ReadValueId)).Returns(typeof(ReadValueId));
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
            _nodeCacheMock
                .SetupSequence(nc => nc.GetNodeAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(variable)
                .ReturnsAsync(dataTypeNode);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    true,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, false, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
        }

        [Fact]
        public async Task PreloadDataTypeAsyncShouldPreloadSubTypesWhenIncludeSubTypesIsTrueAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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

            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(
                    It.Is<NodeId>(id => id == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode);
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(
                    subTypeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(subTypeNode);
            _nodeCacheMock
                .SetupSequence(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([subTypeNode])
                .ReturnsAsync([]);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultXml, NodeId = xmlEncodingId }
                ]);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            _nodeCacheMock
                .Verify(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Fact]
        public async Task PreloadDataTypeAsyncShouldPreloadSubTypesWhenStructureCanContainThemAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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

            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(
                    It.Is<NodeId>(id => id == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Exactly(5));
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.Is<IReadOnlyList<NodeId>>(i => i.Count == 1 && i.Contains(typeId)),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([subTypeNode])
                .Verifiable(Times.Exactly(4));
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.Is<IReadOnlyList<NodeId>>(i => !i.Contains(typeId)),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Exactly(4));
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<NodeId>(),
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultXml, NodeId = xmlEncodingId }
                ]);

            // Act
            await sut.PreloadDataTypeAsync(typeId, false, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
        }

        [Fact]
        public async Task PreloadDataTypeAsyncDoesNotAddMalformedTypesAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, null, _loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var badDefinition = new StructureDefinition
            {
                Fields = [new StructureField { Name = "", DataType = typeId, ValueRank = ValueRanks.TwoDimensions }]
            };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = "BadDataType",
                DataTypeDefinition = new ExtensionObject(badDefinition)
            };
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(
                    It.Is<NodeId>(id => id == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IReadOnlyList<INode>>([]))
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([])
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            var added = sut.GetDataTypeDescription(typeId);
            added.Should().BeNull();
            _nodeCacheMock.Verify();
        }

        [Fact]
        public async Task PreloadStructureDataTypeAsyncShouldFallbackToLegacyTypeSystemIfDataTypeIsNotReadableAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = "MyStructure"
            };
            var binaryDefinition = new StructureDefinition();
            var xmlDefinition = new StructureDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(
                    It.Is<NodeId>(id => id == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IReadOnlyList<INode>>([]))
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultXml, NodeId = xmlEncodingId }
                ])
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultBinary,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(binaryDefinition,
                    new XmlQualifiedName(), binaryEncodingId))
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultXml,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(xmlDefinition,
                    new XmlQualifiedName(), xmlEncodingId))
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
            _dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            type.Should().BeOfType<StructureDescription.Structure>()
                .Which.StructureDefinition.Should().Be(binaryDefinition);
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            binary.Should().BeOfType<StructureDescription.Structure>()
                .Which.StructureDefinition.Should().Be(binaryDefinition);
            var xml = sut.GetDataTypeDescription(xmlEncodingId);
            xml.Should().BeOfType<StructureDescription.Structure>()
                .Which.StructureDefinition.Should().Be(xmlDefinition);
        }

        [Fact]
        public async Task PreloadStructureDataTypeAsyncShouldFallbackToLegacyTypeSystemIfDataTypeIsNotReadableJustBinaryAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = "MyStructure"
            };
            var binaryDefinition = new StructureDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(
                    It.Is<NodeId>(id => id == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IReadOnlyList<INode>>([]))
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultXml, NodeId = xmlEncodingId }
                ])
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultBinary,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(binaryDefinition,
                    new XmlQualifiedName(), binaryEncodingId))
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultXml,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DictionaryDataTypeDefinition?)null)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
            _dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            type.Should().BeOfType<StructureDescription.Structure>()
                .Which.StructureDefinition.Should().Be(binaryDefinition);
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            binary.Should().BeOfType<StructureDescription.Structure>()
                .Which.StructureDefinition.Should().Be(binaryDefinition);
            var xml = sut.GetDataTypeDescription(xmlEncodingId);
            xml.Should().BeOfType<StructureDescription.Structure>()
                .Which.StructureDefinition.Should().Be(binaryDefinition);
        }

        [Fact]
        public async Task PreloadStructureDataTypeAsyncShouldFallbackToLegacyTypeSystemIfDataTypeIsNotReadableJustXmlAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = "MyStructure"
            };
            var xmlDefinition = new StructureDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(
                    It.Is<NodeId>(id => id == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IReadOnlyList<INode>>([]))
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultXml, NodeId = xmlEncodingId }
                ])
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultXml,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(xmlDefinition,
                    new XmlQualifiedName(), binaryEncodingId))
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultBinary,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DictionaryDataTypeDefinition?)null)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
            _dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            type.Should().BeOfType<StructureDescription.Structure>()
                .Which.StructureDefinition.Should().Be(xmlDefinition);
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            binary.Should().BeOfType<StructureDescription.Structure>()
                .Which.StructureDefinition.Should().Be(xmlDefinition);
            var xml = sut.GetDataTypeDescription(xmlEncodingId);
            xml.Should().BeOfType<StructureDescription.Structure>()
                .Which.StructureDefinition.Should().Be(xmlDefinition);
        }

        [Fact]
        public async Task PreloadEnumDataTypeAsyncShouldFallbackToLegacyTypeSystemIfDataTypeIsNotReadableAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = "MyEnum"
            };
            var binaryDefinition = new EnumDefinition();
            var xmlDefinition = new EnumDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(
                    It.Is<NodeId>(id => id == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IReadOnlyList<INode>>([]))
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultXml, NodeId = xmlEncodingId }
                ])
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultBinary,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(binaryDefinition,
                    new XmlQualifiedName(), binaryEncodingId))
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultXml,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(xmlDefinition,
                    new XmlQualifiedName(), xmlEncodingId))
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
            _dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            type.Should().BeOfType<EnumDescription>()
                .Which.EnumDefinition.Should().Be(binaryDefinition);
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            binary.Should().BeOfType<EnumDescription>()
                .Which.EnumDefinition.Should().Be(binaryDefinition);
            var xml = sut.GetDataTypeDescription(xmlEncodingId);
            xml.Should().BeOfType<EnumDescription>()
                .Which.EnumDefinition.Should().Be(xmlDefinition);
        }

        [Fact]
        public async Task PreloadEnumDataTypeAsyncShouldFallbackToLegacyTypeSystemIfDataTypeIsNotReadableJustBinaryAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = "MyEnum"
            };
            var binaryDefinition = new EnumDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(
                    It.Is<NodeId>(id => id == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IReadOnlyList<INode>>([]))
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultXml, NodeId = xmlEncodingId }
                ])
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultBinary,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(binaryDefinition,
                    new XmlQualifiedName(), binaryEncodingId))
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultXml,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DictionaryDataTypeDefinition?)null)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
            _dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            type.Should().BeOfType<EnumDescription>()
                .Which.EnumDefinition.Should().Be(binaryDefinition);
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            binary.Should().BeOfType<EnumDescription>()
                .Which.EnumDefinition.Should().Be(binaryDefinition);
            var xml = sut.GetDataTypeDescription(xmlEncodingId);
            xml.Should().BeOfType<EnumDescription>()
                .Which.EnumDefinition.Should().Be(binaryDefinition);
        }

        [Fact]
        public async Task PreloadEnumDataTypeAsyncShouldFallbackToLegacyTypeSystemIfDataTypeIsNotReadableJustXmlAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = "MyEnum"
            };
            var xmlDefinition = new EnumDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(
                    It.Is<NodeId>(id => id == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IReadOnlyList<INode>>([]))
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultXml, NodeId = xmlEncodingId }
                ])
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultXml,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(xmlDefinition,
                    new XmlQualifiedName(), binaryEncodingId))
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultBinary,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DictionaryDataTypeDefinition?)null)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
            _dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            type.Should().BeOfType<EnumDescription>()
                .Which.EnumDefinition.Should().Be(xmlDefinition);
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            binary.Should().BeOfType<EnumDescription>()
                .Which.EnumDefinition.Should().Be(xmlDefinition);
            var xml = sut.GetDataTypeDescription(xmlEncodingId);
            xml.Should().BeOfType<EnumDescription>()
                .Which.EnumDefinition.Should().Be(xmlDefinition);
        }

        [Fact]
        public async Task PreloadEnumDataTypeAsyncShouldFallbackToLegacyTypeSystemUsesXmlDefinitionEvenWithoutXmlEncodingIdAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = "MyEnum"
            };
            var xmlDefinition = new EnumDefinition();
            var binaryEncodingId = NodeId.Parse("i=1017");
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(
                    It.Is<NodeId>(id => id == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IReadOnlyList<INode>>([]))
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId }
                ])
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultXml,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DictionaryDataTypeDefinition(xmlDefinition,
                    new XmlQualifiedName(), binaryEncodingId))
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultBinary,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DictionaryDataTypeDefinition?)null)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
            _dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            type.Should().BeOfType<EnumDescription>()
                .Which.EnumDefinition.Should().Be(xmlDefinition);
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            binary.Should().BeOfType<EnumDescription>()
                .Which.EnumDefinition.Should().Be(xmlDefinition);
        }

        [Fact]
        public async Task PreloadEnumDataTypeAsyncShouldFallbackToLegacyTypeSystemAndFinallyFailWithUnknownTypeAsync()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);

            var typeId = NodeId.Parse("i=1009");
            var dataTypeNode = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = "MyEnum"
            };
            var binaryEncodingId = NodeId.Parse("i=1017");
            var xmlEncodingId = NodeId.Parse("i=1018");
            _nodeCacheMock
                .Setup(nc => nc.GetNodeAsync(
                    It.Is<NodeId>(id => id == typeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataTypeNode)
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IReadOnlyList<INode>>([]))
                .Verifiable(Times.Once);
            _nodeCacheMock
                .Setup(nc => nc.GetReferencesAsync(
                    typeId,
                    ReferenceTypeIds.HasEncoding,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new DataTypeNode { BrowseName = BrowseNames.DefaultBinary, NodeId = binaryEncodingId },
                    new DataTypeNode { BrowseName = BrowseNames.DefaultXml, NodeId = xmlEncodingId }
                ])
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultXml,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DictionaryDataTypeDefinition?)null)
                .Verifiable(Times.Once);
            _dataTypeSystemsMock
                .Setup(x => x.GetDataTypeDefinitionAsync(
                    BrowseNames.DefaultBinary,
                    typeId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DictionaryDataTypeDefinition?)null)
                .Verifiable(Times.Once);

            // Act
            await sut.PreloadDataTypeAsync(typeId, true, CancellationToken.None);

            // Assert
            _nodeCacheMock.Verify();
            _dataTypeSystemsMock.Verify();
            var type = sut.GetDataTypeDescription(typeId);
            type.Should().BeNull();
            var binary = sut.GetDataTypeDescription(binaryEncodingId);
            binary.Should().BeNull();
            var xml = sut.GetDataTypeDescription(xmlEncodingId);
            xml.Should().BeNull();
        }

        [Fact]
        public void GetDataTypeDefinitionShouldReturnEnumDefinitionWhenBodyIsEnumDefinitionWithValidFields()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var enumFields = new EnumFieldCollection
            {
                new EnumField { Name = "Value1", Value = 1 },
                new EnumField { Name = "Value2", Value = 2 }
            };
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(enumDefinition)
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            result.Should().Be(enumDefinition);
        }

        [Fact]
        public void GetDataTypeDefinitionShouldReturnEnumDefinitionWhenBodyIsEnumDefinitionWithEmptyFields()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var enumDefinition = new EnumDefinition { Fields = [] };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(enumDefinition)
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            result.Should().Be(enumDefinition);
        }

        [Fact]
        public void GetDataTypeDefinitionShouldReturnEnumDefinitionWhenBodyIsEnumDefinitionWithDuplicateFieldNames()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var enumFields = new EnumFieldCollection
            {
                new EnumField { Name = "Duplicate", Value = 1 },
                new EnumField { Name = "Duplicate", Value = 2 }
            };
            var enumDefinition = new EnumDefinition { Fields = enumFields };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(enumDefinition)
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            result.Should().Be(enumDefinition);
        }

        [Fact]
        public void GetDataTypeDefinitionShouldReturnStructureDefinitionWhenBodyIsStructureDefinitionWithValidFields()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var structureFields = new StructureFieldCollection
            {
                new StructureField { Name = "Field1", DataType = new NodeId(2), ValueRank = ValueRanks.Scalar },
                new StructureField { Name = "Field2", DataType = new NodeId(3), ValueRank = ValueRanks.OneDimension }
            };
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
            result.Should().Be(structureDefinition);
        }

        [Fact]
        public void GetDataTypeDefinitionShouldReturnStructureDefinitionWhenBodyIsStructureDefinitionWithEmptyFields()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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
            result.Should().Be(structureDefinition);
        }

        [Fact]
        public void GetDataTypeDefinitionShouldReturnNullWhenStructureDefinitionHasInvalidStructureType()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
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
            result.Should().BeNull();
        }

        [Fact]
        public void GetDataTypeDefinitionShouldReturnNullWhenStructureDefinitionFieldHasMissingName()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var structureFields = new StructureFieldCollection
            {
                new StructureField { Name = null, DataType = new NodeId(2), ValueRank = ValueRanks.Scalar }
            };
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
            result.Should().BeNull();
        }

        [Fact]
        public void GetDataTypeDefinitionShouldReturnNullWhenStructureDefinitionFieldHasMissingDataType()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var structureFields = new StructureFieldCollection
            {
                new StructureField { Name = "Field1", DataType = NodeId.Null, ValueRank = ValueRanks.Scalar }
            };
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
            result.Should().BeNull();
        }

        [Fact]
        public void GetDataTypeDefinitionShouldReturnStructureDefinitionWhenBodyIsStructureDefinitionWithDuplicateFieldNames()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var structureFields = new StructureFieldCollection
            {
                new StructureField { Name = "Duplicate", DataType = new NodeId(2), ValueRank = ValueRanks.Scalar },
                new StructureField { Name = "Duplicate", DataType = new NodeId(2), ValueRank = ValueRanks.Scalar }
            };
            var structureDefinition = new StructureDefinition { Fields = structureFields };
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            result.Should().Be(structureDefinition);
        }

        [Fact]
        public void GetDataTypeDefinitionShouldReturnNullWhenDataTypeDefinitionIsNull()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = null
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetDataTypeDefinitionShouldReturnNullWhenDataTypeDefinitionBodyIsNull()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject() // Body is null
            };

            // Act
            var result = sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetDataTypeDefinitionShouldThrowExceptionWhenBodyIsUnsupportedType()
        {
            // Arrange
            var sut = new DataTypeDescriptionResolver(
                _nodeCacheMock.Object, _contextMock.Object, _dataTypeSystemsMock.Object, _loggerMock.Object);
            var unsupportedDefinition = new ReadValueId(); // Unsupported type
            var dataTypeNode = new DataTypeNode
            {
                NodeId = new NodeId(1),
                DataTypeDefinition = new ExtensionObject(unsupportedDefinition)
            };

            // Act
            Action act = () => sut.GetDataTypeDefinition(dataTypeNode);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("*Data type definition information provided by server is not valid*");
        }

        private readonly Mock<IServiceMessageContext> _contextMock;
        private readonly Mock<IDataTypeSystemManager> _dataTypeSystemsMock;
        private readonly Mock<IEncodeableFactory> _factoryMock;
        private readonly Mock<ILogger<DataTypeDescriptionResolver>> _loggerMock;
        private readonly Mock<INodeCache> _nodeCacheMock;
    }
}
