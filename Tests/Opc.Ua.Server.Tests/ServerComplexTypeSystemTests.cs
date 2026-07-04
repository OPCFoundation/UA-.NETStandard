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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for building and registering dynamic stand-in encodeables for the
    /// runtime-loaded custom DataTypes of a server address space.
    /// </summary>
    [TestFixture]
    [Category("ComplexTypes")]
    [Parallelizable]
    public class ServerComplexTypeSystemTests
    {
        private const string TestNamespaceUri = "http://test.org/UA/ComplexTypes/";

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();

            m_namespaceUris = new NamespaceTable();
            m_namespaceUris.GetIndexOrAppend(Types.Namespaces.OpcUa);
            m_ns = m_namespaceUris.GetIndexOrAppend(TestNamespaceUri);

            m_typeTree = new TypeTable(m_namespaceUris);
            m_factory = EncodeableFactory.Create();
            m_nodesById = [];

            m_structTypeId = new NodeId(6001, m_ns);
            m_structEncodingId = new NodeId(7001, m_ns);
            m_enumTypeId = new NodeId(6002, m_ns);

            BuildAddressSpace();

            m_mockNodeManager = new Mock<IMasterNodeManager>();
            m_mockNodeManager
                .Setup(m => m.FindNodeInAddressSpaceAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .Returns<NodeId, CancellationToken>((id, _) =>
                    new ValueTask<NodeState>(
                        m_nodesById.TryGetValue(id, out NodeState node) ? node : null));

            m_mockServer = new Mock<IServerInternal>();
            m_mockServer.Setup(s => s.NamespaceUris).Returns(m_namespaceUris);
            m_mockServer.Setup(s => s.TypeTree).Returns(m_typeTree);
            m_mockServer.Setup(s => s.Factory).Returns(m_factory);
            m_mockServer.Setup(s => s.NodeManager).Returns(m_mockNodeManager.Object);
            m_mockServer.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_mockServer.Setup(s => s.DefaultSystemContext)
                .Returns(new ServerSystemContext(m_mockServer.Object));
        }

        [Test]
        public async Task LoadComplexTypesBuildsStandInsForRuntimeStructAndEnum()
        {
            var options = new ServerComplexTypeOptions { ThrowOnError = true };

            await m_mockServer.Object
                .LoadComplexTypesAsync(m_telemetry, options)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    m_factory.TryGetEncodeableType(
                        NodeId.ToExpandedNodeId(m_structTypeId, m_namespaceUris),
                        out IEncodeableType structType),
                    Is.True,
                    "the runtime structure type should be registered in the factory");
                Assert.That(structType, Is.Not.Null);

                Assert.That(
                    m_factory.TryGetEncodeableType(
                        NodeId.ToExpandedNodeId(m_structEncodingId, m_namespaceUris),
                        out _),
                    Is.True,
                    "the binary encoding id should resolve to the structure stand-in");

                Assert.That(
                    m_factory.TryGetEnumeratedType(
                        NodeId.ToExpandedNodeId(m_enumTypeId, m_namespaceUris),
                        out IEnumeratedType enumType),
                    Is.True,
                    "the runtime enumeration type should be registered in the factory");
                Assert.That(enumType, Is.Not.Null);
            });
        }

        [Test]
        public async Task LoadComplexTypesSkipsAlreadyKnownTypes()
        {
            var options = new ServerComplexTypeOptions { ThrowOnError = true };

            // first pass builds the stand-ins into the factory
            await m_mockServer.Object
                .LoadComplexTypesAsync(m_telemetry, options)
                .ConfigureAwait(false);
            int knownAfterFirstPass = m_factory.KnownTypeIds.Count();

            // second pass must skip everything that is already known to the factory
            await m_mockServer.Object
                .LoadComplexTypesAsync(m_telemetry, options)
                .ConfigureAwait(false);

            Assert.That(
                m_factory.KnownTypeIds.Count(),
                Is.EqualTo(knownAfterFirstPass),
                "types already present in the factory must not be rebuilt");
        }

        [Test]
        public async Task LoadComplexTypesReturnsFactoryBackedResolver()
        {
            var options = new ServerComplexTypeOptions { ThrowOnError = true };

            // a schema-only type that has no encodeable in the factory, supplied
            // through the optional registry, must still resolve via the composite
            var registry = new DataTypeDefinitionRegistry();
            var schemaOnlyId = new NodeId(6009, m_ns);
            registry.Add(new UaTypeDescription(
                new ExpandedNodeId(schemaOnlyId),
                new QualifiedName("SchemaOnly", m_ns),
                new StructureDefinition
                {
                    BaseDataType = DataTypeIds.Structure,
                    StructureType = StructureType.Structure,
                    Fields = []
                },
                TestNamespaceUri));

            IDataTypeDefinitionResolver resolver = await m_mockServer.Object
                .LoadComplexTypesAsync(m_telemetry, options, registry)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                // the primed factory is the source for the runtime stand-ins
                Assert.That(
                    resolver.TryResolve(
                        NodeId.ToExpandedNodeId(m_structTypeId, m_namespaceUris),
                        out UaTypeDescription structDescription),
                    Is.True);
                Assert.That(structDescription.Definition, Is.InstanceOf<StructureDefinition>());
                Assert.That(structDescription.NamespaceUri, Is.EqualTo(TestNamespaceUri));

                Assert.That(
                    resolver.TryResolve(
                        NodeId.ToExpandedNodeId(m_enumTypeId, m_namespaceUris),
                        out UaTypeDescription enumDescription),
                    Is.True);
                Assert.That(enumDescription.Definition, Is.InstanceOf<EnumDefinition>());

                // the composite falls through to the registry for schema-only types
                Assert.That(
                    resolver.TryResolve(
                        new ExpandedNodeId(schemaOnlyId),
                        out UaTypeDescription schemaOnlyDescription),
                    Is.True);
                Assert.That(schemaOnlyDescription.Name, Is.EqualTo("SchemaOnly"));
            });
        }

        [Test]
        public void AddComplexTypeSystemRegistersOptionsAndResolver()
        {
            var services = new ServiceCollection();
            services
                .AddOpcUa()
                .AddServer(options => options.ApplicationName = "TestComplexTypeServer")
                .AddComplexTypeSystem(options => options.OnlyEnumTypes = true);

            using ServiceProvider provider = services.BuildServiceProvider();

            ServerComplexTypeOptions registeredOptions =
                provider.GetService<ServerComplexTypeOptions>();
            Assert.That(registeredOptions, Is.Not.Null);
            Assert.That(registeredOptions.OnlyEnumTypes, Is.True);
            Assert.That(provider.GetService<IDataTypeDefinitionResolver>(), Is.Not.Null);
        }

        [Test]
        public async Task GetEnumTypeArrayIgnoresUnrelatedPropertyAndSelectsEnumStrings()
        {
            // DataType with a decoy HasProperty listed BEFORE the real EnumStrings
            // property. The old "first non-null" logic would have returned the
            // decoy; the resolver must match by BrowseName instead.
            var enumWithProps = new NodeId(6003, m_ns);
            var decoyPropId = new NodeId(7101, m_ns);
            var enumStringsPropId = new NodeId(7102, m_ns);

            var dataTypeNode = new DataTypeState
            {
                NodeId = enumWithProps,
                BrowseName = new QualifiedName("EnumWithProps", m_ns),
                SuperTypeId = DataTypeIds.Enumeration,
                IsAbstract = false
            };
            dataTypeNode.AddReference(
                ReferenceTypeIds.HasProperty, false, new ExpandedNodeId(decoyPropId));
            dataTypeNode.AddReference(
                ReferenceTypeIds.HasProperty, false, new ExpandedNodeId(enumStringsPropId));
            m_nodesById[enumWithProps] = dataTypeNode;

            m_nodesById[decoyPropId] = new BaseDataVariableState(null)
            {
                NodeId = decoyPropId,
                BrowseName = new QualifiedName("SomethingElse", m_ns),
                Value = new Variant(new int[] { 42 })
            };
            m_nodesById[enumStringsPropId] = new BaseDataVariableState(null)
            {
                NodeId = enumStringsPropId,
                BrowseName = new QualifiedName(BrowseNames.EnumStrings),
                Value = new Variant(new LocalizedText[]
                {
                    new LocalizedText("Red"),
                    new LocalizedText("Green")
                })
            };

            var resolver = new AddressSpaceComplexTypeResolver(m_mockServer.Object);

            Variant result = await resolver
                .GetEnumTypeArrayAsync(NodeId.ToExpandedNodeId(enumWithProps, m_namespaceUris))
                .ConfigureAwait(false);

            Assert.That(
                result.TryGetValue(out ArrayOf<LocalizedText> texts),
                Is.True,
                "the EnumStrings property must be selected and the decoy ignored");
            Assert.That(texts.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task GetEnumTypeArrayPrefersEnumValuesOverEnumStrings()
        {
            // Both EnumStrings (listed first) and EnumValues are present; the
            // richer EnumValues must win regardless of reference order.
            var enumWithProps = new NodeId(6004, m_ns);
            var enumStringsPropId = new NodeId(7201, m_ns);
            var enumValuesPropId = new NodeId(7202, m_ns);

            var dataTypeNode = new DataTypeState
            {
                NodeId = enumWithProps,
                BrowseName = new QualifiedName("EnumWithBoth", m_ns),
                SuperTypeId = DataTypeIds.Enumeration,
                IsAbstract = false
            };
            dataTypeNode.AddReference(
                ReferenceTypeIds.HasProperty, false, new ExpandedNodeId(enumStringsPropId));
            dataTypeNode.AddReference(
                ReferenceTypeIds.HasProperty, false, new ExpandedNodeId(enumValuesPropId));
            m_nodesById[enumWithProps] = dataTypeNode;

            m_nodesById[enumStringsPropId] = new BaseDataVariableState(null)
            {
                NodeId = enumStringsPropId,
                BrowseName = new QualifiedName(BrowseNames.EnumStrings),
                Value = new Variant(new LocalizedText[] { new LocalizedText("Red") })
            };
            m_nodesById[enumValuesPropId] = new BaseDataVariableState(null)
            {
                NodeId = enumValuesPropId,
                BrowseName = new QualifiedName("EnumValues"),
                Value = new Variant(new ExtensionObject[]
                {
                    new ExtensionObject(new EnumValueType
                    {
                        Value = 0,
                        DisplayName = new LocalizedText("Red")
                    })
                })
            };

            var resolver = new AddressSpaceComplexTypeResolver(m_mockServer.Object);

            Variant result = await resolver
                .GetEnumTypeArrayAsync(NodeId.ToExpandedNodeId(enumWithProps, m_namespaceUris))
                .ConfigureAwait(false);

            Assert.That(
                result.TryGetValue(out ArrayOf<ExtensionObject> enumValues),
                Is.True,
                "EnumValues must be preferred over EnumStrings");
            Assert.That(enumValues.Count, Is.EqualTo(1));
        }

        private void BuildAddressSpace()
        {
            m_typeTree.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
            m_typeTree.AddSubtype(DataTypeIds.Structure, DataTypeIds.BaseDataType);
            m_typeTree.AddSubtype(DataTypeIds.Enumeration, DataTypeIds.BaseDataType);
            m_typeTree.AddSubtype(m_structTypeId, DataTypeIds.Structure);
            m_typeTree.AddSubtype(m_enumTypeId, DataTypeIds.Enumeration);

            // Abstract base DataTypes are findable but carry no definition so the
            // schema walk skips them cleanly (without falling back to a read).
            AddAbstractBaseType(DataTypeIds.BaseDataType, "BaseDataType");
            AddAbstractBaseType(DataTypeIds.Structure, "Structure");
            AddAbstractBaseType(DataTypeIds.Enumeration, "Enumeration");

            var structureDefinition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField
                    {
                        Name = "Number",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new StructureField
                    {
                        Name = "Text",
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
            var structureNode = new DataTypeState
            {
                NodeId = m_structTypeId,
                BrowseName = new QualifiedName("TestStructure", m_ns),
                SuperTypeId = DataTypeIds.Structure,
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };
            structureNode.AddReference(
                ReferenceTypeIds.HasEncoding,
                false,
                new ExpandedNodeId(m_structEncodingId));
            m_nodesById[m_structTypeId] = structureNode;

            m_nodesById[m_structEncodingId] = new BaseObjectState(null)
            {
                NodeId = m_structEncodingId,
                BrowseName = new QualifiedName(BrowseNames.DefaultBinary)
            };

            var enumDefinition = new EnumDefinition
            {
                Fields =
                [
                    new EnumField { Name = "Red", Value = 0 },
                    new EnumField { Name = "Green", Value = 1 },
                    new EnumField { Name = "Blue", Value = 2 }
                ]
            };
            m_nodesById[m_enumTypeId] = new DataTypeState
            {
                NodeId = m_enumTypeId,
                BrowseName = new QualifiedName("TestEnum", m_ns),
                SuperTypeId = DataTypeIds.Enumeration,
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(enumDefinition)
            };
        }

        private void AddAbstractBaseType(NodeId nodeId, string name)
        {
            m_nodesById[nodeId] = new DataTypeState
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName(name),
                IsAbstract = true
            };
        }

        private ITelemetryContext m_telemetry;
        private NamespaceTable m_namespaceUris;
        private ushort m_ns;
        private TypeTable m_typeTree;
        private IEncodeableFactory m_factory;
        private Dictionary<NodeId, NodeState> m_nodesById;
        private Mock<IMasterNodeManager> m_mockNodeManager;
        private Mock<IServerInternal> m_mockServer;
        private NodeId m_structTypeId;
        private NodeId m_structEncodingId;
        private NodeId m_enumTypeId;
    }
}
