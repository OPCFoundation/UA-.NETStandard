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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using NUnit.Framework;

// The encodeable type registry API is experimental; the schema factory source
// is built on top of it.
#pragma warning disable UA_NETStandard_1

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Unit tests for the complex type system using an in-memory resolver.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class ComplexTypeSystemCoverageTests
    {
        [Test]
        public async Task LoadAsyncBuildsEnumAndCachesDefinition()
        {
            TestComplexTypeResolver resolver = CreateResolverWithEnumStructureAndOptionSet();
            var factory = new RecordingComplexTypeFactory();
            var system = new ComplexTypeSystem(resolver, factory, null!);

            bool loaded = await system.LoadAsync();
            NodeIdDictionary<DataTypeDefinition> dependencies = system.GetDataTypeDefinitionsForDataType(
                TestIds.EnumType);
            var registry = new DataTypeDefinitionRegistry();
            DataTypeDefinitionRegistry returnedRegistry = system.RegisterDataTypeDefinitions(registry);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.True);
                Assert.That(system.GetDefinedTypes().Select(t => t.Name), Does.Contain("TestEnum"));
                Assert.That(system.GetDefinedDataTypeIds(), Does.Contain(TestIds.EnumType));
                Assert.That(dependencies.Keys, Does.Contain(TestIds.EnumNodeId));
                Assert.That(returnedRegistry, Is.SameAs(registry));
                Assert.That(
                    registry.TryResolve(new ExpandedNodeId(TestIds.EnumNodeId), out UaTypeDescription? description),
                    Is.True);
                Assert.That(description!.Definition, Is.InstanceOf<EnumDefinition>());
                Assert.That(resolver.FactoryBuilder.TryGetEnumeratedType(TestIds.EnumType, out _), Is.True);
            });
        }

        [Test]
        public async Task LoadNamespaceAsyncLoadsOnlyMatchingNamespaceTypes()
        {
            TestComplexTypeResolver resolver = CreateResolverWithEnumStructureAndOptionSet();
            NodeId otherNamespaceTypeId = new(7100, SchemaTestData.OtherNamespaceIndex);
            resolver.AddDataType(
                CreateEnumNode(otherNamespaceTypeId, "OtherEnum", CreateEnumDefinition("Other")),
                DataTypeIds.Enumeration);
            var system = new ComplexTypeSystem(resolver, new RecordingComplexTypeFactory(), null!);

            bool loaded = await system.LoadNamespaceAsync(SchemaTestData.TestNamespace);
            bool unknownLoaded = await system.LoadNamespaceAsync("urn:missing");
            NodeIdDictionary<DataTypeDefinition> otherDefinitions = system.GetDataTypeDefinitionsForDataType(
                new ExpandedNodeId(otherNamespaceTypeId));

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.True);
                Assert.That(unknownLoaded, Is.False);
                Assert.That(system.GetDefinedDataTypeIds(), Does.Contain(TestIds.EnumType));
                Assert.That(system.GetDefinedDataTypeIds(), Does.Not.Contain(new ExpandedNodeId(otherNamespaceTypeId)));
                Assert.That(otherDefinitions, Is.Empty);
            });
        }

        [Test]
        public async Task LoadTypeAsyncReturnsNullOrThrowsWhenResolverFails()
        {
            var resolver = new ThrowingComplexTypeResolver();
            var system = new ComplexTypeSystem(resolver, new DefaultComplexTypeFactory(), null!);

            IType? ignored = await system.LoadTypeAsync(TestIds.StructureType);

            Assert.Multiple(() =>
            {
                Assert.That(ignored, Is.Null);
                Func<Task> throwingLoad = () => system.LoadTypeAsync(
                    TestIds.StructureType,
                    throwOnError: true);
                Assert.That(
                    throwingLoad,
                    Throws.TypeOf<ServiceResultException>());
            });
        }

        [Test]
        public async Task LoadAsyncReturnsFalseWhenBothDefinitionSourcesAreDisabled()
        {
            TestComplexTypeResolver resolver = CreateResolverWithEnumStructureAndOptionSet();
            var system = new ComplexTypeSystem(resolver, new DefaultComplexTypeFactory(), null!)
            {
                DisableDataTypeDefinition = true,
                DisableDataTypeDictionary = true
            };

            bool loaded = await system.LoadAsync();

            Assert.That(loaded, Is.False);
        }

        [Test]
        public async Task LoadAsyncUsesBinaryDictionaryWhenDataTypeDefinitionsAreDisabled()
        {
            var resolver = new TestComplexTypeResolver();
            NodeId enumTypeId = new(7601, SchemaTestData.TestNamespaceIndex);
            NodeId structureTypeId = new(7602, SchemaTestData.TestNamespaceIndex);
            NodeId enumDescriptionId = new(8601, SchemaTestData.TestNamespaceIndex);
            NodeId structureDescriptionId = new(8602, SchemaTestData.TestNamespaceIndex);
            resolver.AddDataType(
                new DataTypeNode
                {
                    NodeId = enumTypeId,
                    BrowseName = new QualifiedName("DictionaryEnum", SchemaTestData.TestNamespaceIndex)
                },
                DataTypeIds.Enumeration);
            resolver.AddDataType(
                new DataTypeNode
                {
                    NodeId = structureTypeId,
                    BrowseName = new QualifiedName("DictionaryStructure", SchemaTestData.TestNamespaceIndex)
                },
                DataTypeIds.Structure);
            resolver.AddDictionaryDescription(enumDescriptionId, enumTypeId);
            resolver.AddDictionaryDescription(structureDescriptionId, structureTypeId);
            resolver.DataTypeSystem[new NodeId(9600, SchemaTestData.TestNamespaceIndex)] = CreateBinaryDictionary(
                enumDescriptionId,
                structureDescriptionId);
            var factory = new RecordingComplexTypeFactory();
            var system = new ComplexTypeSystem(resolver, factory, null!)
            {
                DisableDataTypeDefinition = true
            };

            bool loaded = await system.LoadAsync();

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.True);
                Assert.That(factory.GetTypes().Select(t => t.XmlName.Name), Does.Contain("DictionaryEnum"));
                Assert.That(factory.GetTypes().Select(t => t.XmlName.Name), Does.Contain("DictionaryStructure"));
                Assert.That(
                    system.GetDefinedDataTypeIds(),
                    Does.Contain(NodeId.ToExpandedNodeId(enumTypeId, resolver.NamespaceUris)));
                Assert.That(
                    system.GetDefinedDataTypeIds(),
                    Does.Contain(NodeId.ToExpandedNodeId(structureTypeId, resolver.NamespaceUris)));
            });
        }

        [Test]
        public async Task LoadAsyncFallsBackToEnumStringsWhenDataTypeDefinitionIsDisabled()
        {
            var resolver = new TestComplexTypeResolver();
            NodeId enumTypeId = new(7200, SchemaTestData.TestNamespaceIndex);
            resolver.AddDataType(
                new DataTypeNode
                {
                    NodeId = enumTypeId,
                    BrowseName = new QualifiedName("StringEnum", SchemaTestData.TestNamespaceIndex)
                },
                DataTypeIds.Enumeration);
            ArrayOf<LocalizedText> enumStrings = new[]
            {
                LocalizedText.From("Zero"),
                LocalizedText.From("One")
            };
            resolver.SetEnumTypeArray(enumTypeId, new Variant(enumStrings));
            var factory = new RecordingComplexTypeFactory();
            var system = new ComplexTypeSystem(resolver, factory, null!)
            {
                DisableDataTypeDefinition = true
            };

            bool loaded = await system.LoadAsync(onlyEnumTypes: true);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.True);
                Assert.That(system.GetDefinedDataTypeIds(), Is.Empty);
                Assert.That(factory.GetTypes(), Is.Empty);
            });
        }

        [Test]
        public void RegisterDataTypeDefinitionsRejectsNullRegistry()
        {
            var system = new ComplexTypeSystem(
                CreateResolverWithEnumStructureAndOptionSet(),
                new DefaultComplexTypeFactory(),
                null!);

            Assert.That(
                () => system.RegisterDataTypeDefinitions(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("registry"));
        }

        [Test]
        public void ClearDataTypeCacheRemovesCollectedDefinitions()
        {
            var system = new ComplexTypeSystem(
                CreateResolverWithEnumStructureAndOptionSet(),
                new DefaultComplexTypeFactory(),
                null!);

            AddCachedDefinition(system, TestIds.EnumNodeId, "CachedEnum", CreateEnumDefinition("Cached"));
            Assert.That(system.GetDefinedDataTypeIds(), Is.Not.Empty);

            system.ClearDataTypeCache();

            Assert.That(system.GetDefinedDataTypeIds(), Is.Empty);
        }

        [Test]
        public void DefaultComplexTypeFactoryCreatesEnumOptionSetStructureAndUnionTypes()
        {
            var factory = new DefaultComplexTypeFactory();
            IComplexTypeBuilder builder = factory.Create(SchemaTestData.TestNamespace, SchemaTestData.TestNamespaceIndex);

            IEnumeratedType enumType = builder.AddEnumType(
                new QualifiedName("FactoryEnum", SchemaTestData.TestNamespaceIndex),
                CreateEnumDefinition("A", "B"));
            IEncodeableType optionSetType = builder.AddOptionSetType(
                new QualifiedName("FactoryOptionSet", SchemaTestData.TestNamespaceIndex),
                new ExpandedNodeId(new NodeId(7301, SchemaTestData.TestNamespaceIndex), SchemaTestData.TestNamespace),
                new ExpandedNodeId(new NodeId(7302, SchemaTestData.TestNamespaceIndex), SchemaTestData.TestNamespace),
                ExpandedNodeId.Null,
                CreateEnumDefinition("Bit0"));
            IEncodeableType structureType = CreateDefaultStructureType(
                builder,
                "FactoryStructure",
                StructureType.Structure,
                BuiltInType.Int32);
            IEncodeableType optionalStructureType = CreateDefaultStructureType(
                builder,
                "FactoryOptionalStructure",
                StructureType.StructureWithOptionalFields,
                BuiltInType.String,
                optional: true);
            IEncodeableType unionType = CreateDefaultStructureType(
                builder,
                "FactoryUnion",
                StructureType.Union,
                BuiltInType.Boolean);

            Assert.Multiple(() =>
            {
                Assert.That(enumType.XmlName.Name, Is.EqualTo("FactoryEnum"));
                Assert.That(optionSetType.XmlName.Name, Is.EqualTo("FactoryOptionSet"));
                Assert.That(structureType.XmlName.Name, Is.EqualTo("FactoryStructure"));
                Assert.That(optionalStructureType.XmlName.Name, Is.EqualTo("FactoryOptionalStructure"));
                Assert.That(unionType.XmlName.Name, Is.EqualTo("FactoryUnion"));
                Assert.That(factory.GetTypes().Select(t => t.XmlName.Name), Does.Contain("FactoryEnum"));
                Assert.That(factory.GetTypes().Select(t => t.XmlName.Name), Does.Contain("FactoryUnion"));
            });
        }

        private static TestComplexTypeResolver CreateResolverWithEnumStructureAndOptionSet()
        {
            var resolver = new TestComplexTypeResolver();
            resolver.AddDataType(
                CreateEnumNode(TestIds.EnumNodeId, "TestEnum", CreateEnumDefinition("First", "Second")),
                DataTypeIds.Enumeration);
            resolver.AddDataType(
                CreateStructureNode(TestIds.StructureNodeId),
                DataTypeIds.Structure);
            resolver.AddDataType(
                CreateEnumNode(TestIds.OptionSetNodeId, "TestOptionSet", CreateEnumDefinition("Bit0", "Bit1")),
                DataTypeIds.OptionSet);
            return resolver;
        }

        private static DataTypeNode CreateEnumNode(
            NodeId nodeId,
            string browseName,
            EnumDefinition enumDefinition)
        {
            return new DataTypeNode
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName(browseName, nodeId.NamespaceIndex),
                DataTypeDefinition = new ExtensionObject(enumDefinition)
            };
        }

        private static DataTypeNode CreateStructureNode(NodeId nodeId)
        {
            var definition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    CreateStructureField("Counter", DataTypeIds.Int32),
                    CreateStructureField("State", TestIds.EnumNodeId)
                ]
            };
            return new DataTypeNode
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("TestStructure", nodeId.NamespaceIndex),
                DataTypeDefinition = new ExtensionObject(definition)
            };
        }

        private static StructureField CreateStructureField(string name, NodeId dataType)
        {
            return new StructureField
            {
                Name = name,
                DataType = dataType,
                ValueRank = ValueRanks.Scalar
            };
        }

        private static IEncodeableType CreateDefaultStructureType(
            IComplexTypeBuilder builder,
            string name,
            StructureType structureType,
            BuiltInType fieldBuiltInType,
            bool optional = false)
        {
            StructureDefinition definition = new()
            {
                StructureType = structureType,
                Fields =
                [
                    new StructureField
                    {
                        Name = "Value",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        IsOptional = optional
                    }
                ]
            };
            IComplexTypeFieldBuilder fieldBuilder = builder.AddStructuredType(
                new QualifiedName(name, SchemaTestData.TestNamespaceIndex),
                definition);
            fieldBuilder.AddTypeIdAttribute(
                new ExpandedNodeId(new NodeId(7401, SchemaTestData.TestNamespaceIndex), SchemaTestData.TestNamespace),
                new ExpandedNodeId(new NodeId(7402, SchemaTestData.TestNamespaceIndex), SchemaTestData.TestNamespace),
                ExpandedNodeId.Null);
            fieldBuilder.AddField(
                definition.Fields[0],
                new BuiltInFieldType(fieldBuiltInType),
                1,
                allowSubTypes: optional);
            return fieldBuilder.CreateType();
        }

        private static EnumDefinition CreateEnumDefinition(params string[] names)
        {
            var fields = new EnumField[names.Length];
            for (int index = 0; index < names.Length; index++)
            {
                fields[index] = new EnumField
                {
                    Name = names[index],
                    Value = index
                };
            }
            return new EnumDefinition { Fields = fields };
        }

        private static DataDictionary CreateBinaryDictionary(
            NodeId enumDescriptionId,
            NodeId structureDescriptionId)
        {
            var dictionary = (DataDictionary)Activator.CreateInstance(typeof(DataDictionary), nonPublic: true)!;
            SetInternalProperty(dictionary, nameof(DataDictionary.Name), "CoverageDictionary");
            SetInternalProperty(
                dictionary,
                nameof(DataDictionary.TypeSystemId),
                new NodeId(Objects.OPCBinarySchema_TypeSystem));
            SetInternalProperty(
                dictionary,
                nameof(DataDictionary.DataTypes),
                new NodeIdDictionary<QualifiedName>
                {
                    [enumDescriptionId] = new QualifiedName("DictionaryEnum", SchemaTestData.TestNamespaceIndex),
                    [structureDescriptionId] = new QualifiedName(
                        "DictionaryStructure",
                        SchemaTestData.TestNamespaceIndex)
                });
            SetInternalProperty(
                dictionary,
                nameof(DataDictionary.TypeDictionary),
                new Schema.Binary.TypeDictionary
                {
                    TargetNamespace = SchemaTestData.TestNamespace,
                    Items =
                    [
                        new Schema.Binary.EnumeratedType
                        {
                            Name = "DictionaryEnum",
                            EnumeratedValue =
                            [
                                new Schema.Binary.EnumeratedValue { Name = "Zero", Value = 0 },
                                new Schema.Binary.EnumeratedValue { Name = "One", Value = 1 }
                            ]
                        },
                        new Schema.Binary.StructuredType
                        {
                            Name = "DictionaryStructure",
                            QName = new XmlQualifiedName("DictionaryStructure", SchemaTestData.TestNamespace),
                            Field =
                            [
                                new Schema.Binary.FieldType
                                {
                                    Name = "Value",
                                    TypeName = new XmlQualifiedName("Int32", Namespaces.OpcUa)
                                }
                            ]
                        }
                    ]
                });
            return dictionary;
        }

        private static void SetInternalProperty<T>(
            DataDictionary dictionary,
            string propertyName,
            T value)
        {
            typeof(DataDictionary).GetProperty(propertyName)!.SetValue(dictionary, value);
        }

        private static void AddCachedDefinition(
            ComplexTypeSystem system,
            NodeId nodeId,
            string browseName,
            DataTypeDefinition definition)
        {
            typeof(ComplexTypeSystem)
                .GetMethod("AddDataTypeDefinitionToCache", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)!
                .Invoke(system, [nodeId, new QualifiedName(browseName, nodeId.NamespaceIndex), definition]);
        }

        private static class TestIds
        {
            public static readonly NodeId EnumNodeId = new(7001, SchemaTestData.TestNamespaceIndex);

            public static readonly NodeId StructureNodeId = new(7002, SchemaTestData.TestNamespaceIndex);

            public static readonly NodeId OptionSetNodeId = new(7003, SchemaTestData.TestNamespaceIndex);

            public static readonly ExpandedNodeId EnumType = new(
                EnumNodeId,
                SchemaTestData.TestNamespace);

            public static readonly ExpandedNodeId StructureType = new(
                StructureNodeId,
                SchemaTestData.TestNamespace);

            public static readonly ExpandedNodeId OptionSetType = new(
                OptionSetNodeId,
                SchemaTestData.TestNamespace);
        }

        private sealed class TestComplexTypeResolver : IComplexTypeResolver
        {
            public TestComplexTypeResolver()
            {
                NamespaceUris.Append(SchemaTestData.TestNamespace);
                NamespaceUris.Append(SchemaTestData.OtherNamespace);
                FactoryBuilder = Factory.Builder;
                m_superTypes[DataTypeIds.OptionSet] = DataTypeIds.Structure;
                m_superTypes[DataTypeIds.Structure] = DataTypeIds.BaseDataType;
                m_superTypes[DataTypeIds.Enumeration] = DataTypeIds.BaseDataType;
                m_superTypes[DataTypeIds.Int32] = DataTypeIds.Int32;
            }

            public NamespaceTable NamespaceUris { get; } = new();

            public IEncodeableFactory Factory { get; } = EncodeableFactory.Create();

            public IEncodeableFactoryBuilder FactoryBuilder { get; }

            public NodeIdDictionary<DataDictionary> DataTypeSystem { get; } = [];

            public void AddDataType(DataTypeNode node, NodeId superType)
            {
                m_nodes[node.NodeId] = node;
                m_superTypes[node.NodeId] = superType;
            }

            public void SetEnumTypeArray(NodeId nodeId, Variant value)
            {
                m_enumTypeArrays[nodeId] = value;
            }

            public void AddDictionaryDescription(NodeId descriptionId, NodeId dataTypeId)
            {
                m_dictionaryDescriptions[descriptionId] = dataTypeId;
            }

            public Task<IReadOnlyDictionary<NodeId, DataDictionary>> LoadDataTypeSystem(
                NodeId dataTypeSystem = default,
                CancellationToken ct = default)
            {
                return Task.FromResult<IReadOnlyDictionary<NodeId, DataDictionary>>(DataTypeSystem);
            }

            public Task<(
                ExpandedNodeId typeId,
                ExpandedNodeId encodingId,
                DataTypeNode? dataTypeNode
            )> BrowseTypeIdsForDictionaryComponentAsync(
                ExpandedNodeId nodeId,
                CancellationToken ct = default)
            {
                NodeId localNodeId = ExpandedNodeId.ToNodeId(nodeId, NamespaceUris);
                NodeId dataTypeId = m_dictionaryDescriptions.TryGetValue(localNodeId, out NodeId mappedId)
                    ? mappedId
                    : localNodeId;
                m_nodes.TryGetValue(dataTypeId, out DataTypeNode? dataTypeNode);
                var typeId = NodeId.ToExpandedNodeId(dataTypeId, NamespaceUris);
                var encodingId = new ExpandedNodeId(new NodeId(50000, dataTypeId.NamespaceIndex));
                return Task.FromResult((typeId, encodingId, dataTypeNode));
            }

            public Task<ArrayOf<NodeId>> BrowseForEncodingsAsync(
                ArrayOf<ExpandedNodeId> nodeIds,
                string[] supportedEncodings,
                CancellationToken ct = default)
            {
                return Task.FromResult<ArrayOf<NodeId>>([]);
            }

            public Task<(
                ArrayOf<NodeId> encodings,
                ExpandedNodeId binaryEncodingId,
                ExpandedNodeId xmlEncodingId
            )> BrowseForEncodingsAsync(
                ExpandedNodeId nodeId,
                string[] supportedEncodings,
                CancellationToken ct = default)
            {
                NodeId localNodeId = ExpandedNodeId.ToNodeId(nodeId, NamespaceUris);
                Assert.That(localNodeId.TryGetValue(out uint identifier), Is.True);
                var binaryEncoding = new NodeId(50000 + identifier, localNodeId.NamespaceIndex);
                var xmlEncoding = new NodeId(60000 + identifier, localNodeId.NamespaceIndex);
                ArrayOf<NodeId> encodings = new[] { binaryEncoding, xmlEncoding };
                return Task.FromResult((
                    encodings,
                    NodeId.ToExpandedNodeId(binaryEncoding, NamespaceUris),
                    NodeId.ToExpandedNodeId(xmlEncoding, NamespaceUris)));
            }

            public Task<ArrayOf<INode>> LoadDataTypesAsync(
                ExpandedNodeId dataType,
                bool nestedSubTypes = false,
                bool addRootNode = false,
                bool filterUATypes = true,
                CancellationToken ct = default)
            {
                NodeId requestedType = ExpandedNodeId.ToNodeId(dataType, NamespaceUris);
                var result = new List<INode>();

                if (addRootNode && m_nodes.TryGetValue(requestedType, out DataTypeNode? rootNode))
                {
                    result.Add(rootNode);
                }

                foreach (DataTypeNode node in m_nodes.Values)
                {
                    if (IsSubtypeOf(node.NodeId, requestedType) &&
                        (!filterUATypes || node.NodeId.NamespaceIndex != 0))
                    {
                        result.Add(node);
                    }
                }

                return Task.FromResult<ArrayOf<INode>>(result.ToArray());
            }

            public Task<INode?> FindAsync(ExpandedNodeId nodeId, CancellationToken ct = default)
            {
                m_nodes.TryGetValue(ExpandedNodeId.ToNodeId(nodeId, NamespaceUris), out DataTypeNode? node);
                return Task.FromResult<INode?>(node);
            }

            public Task<Variant> GetEnumTypeArrayAsync(
                ExpandedNodeId nodeId,
                CancellationToken ct = default)
            {
                NodeId localNodeId = ExpandedNodeId.ToNodeId(nodeId, NamespaceUris);
                return Task.FromResult(m_enumTypeArrays.TryGetValue(localNodeId, out Variant value) ? value : default);
            }

            public Task<NodeId> FindSuperTypeAsync(NodeId typeId, CancellationToken ct = default)
            {
                return Task.FromResult(m_superTypes.TryGetValue(typeId, out NodeId superType) ? superType : NodeId.Null);
            }

            private bool IsSubtypeOf(NodeId candidate, NodeId requestedType)
            {
                NodeId current = candidate;
                while (m_superTypes.TryGetValue(current, out NodeId superType))
                {
                    if (superType == requestedType)
                    {
                        return true;
                    }
                    current = superType;
                }
                return false;
            }

            private readonly Dictionary<NodeId, DataTypeNode> m_nodes = [];
            private readonly Dictionary<NodeId, NodeId> m_superTypes = [];
            private readonly Dictionary<NodeId, Variant> m_enumTypeArrays = [];
            private readonly Dictionary<NodeId, NodeId> m_dictionaryDescriptions = [];
        }

        private sealed class RecordingComplexTypeFactory : IComplexTypeFactory
        {
            public IComplexTypeBuilder Create(
                string targetNamespace,
                int targetNamespaceIndex,
                string? moduleName = null)
            {
                return new RecordingComplexTypeBuilder(this, targetNamespace, targetNamespaceIndex);
            }

            public IReadOnlyList<IType> GetTypes()
            {
                return m_types;
            }

            public void AddType(IType type)
            {
                m_types.Add(type);
            }

            private readonly List<IType> m_types = [];
        }

        private sealed class BuiltInFieldType : IBuiltInType
        {
            public BuiltInFieldType(BuiltInType builtInType)
            {
                BuiltInType = builtInType;
            }

            public Type Type => typeof(int);

            public XmlQualifiedName XmlName => new(BuiltInType.ToString(), Namespaces.OpcUa);

            public BuiltInType BuiltInType { get; }
        }

        private sealed class RecordingComplexTypeBuilder : IComplexTypeBuilder
        {
            public RecordingComplexTypeBuilder(
                RecordingComplexTypeFactory factory,
                string targetNamespace,
                int targetNamespaceIndex)
            {
                m_factory = factory;
                TargetNamespace = targetNamespace;
                TargetNamespaceIndex = targetNamespaceIndex;
            }

            public string TargetNamespace { get; }

            public int TargetNamespaceIndex { get; }

            public IEnumeratedType AddEnumType(
                QualifiedName typeName,
                EnumDefinition enumDefinition)
            {
                var type = new RecordingEnumeratedType(new XmlQualifiedName(typeName.Name, TargetNamespace));
                m_factory.AddType(type);
                return type;
            }

            public IComplexTypeFieldBuilder AddStructuredType(
                QualifiedName name,
                StructureDefinition structureDefinition)
            {
                return new RecordingComplexTypeFieldBuilder(
                    m_factory,
                    new XmlQualifiedName(name.Name, TargetNamespace),
                    structureDefinition);
            }

            public IEncodeableType AddOptionSetType(
                QualifiedName typeName,
                ExpandedNodeId typeId,
                ExpandedNodeId binaryEncodingId,
                ExpandedNodeId xmlEncodingId,
                EnumDefinition enumDefinition)
            {
                var type = new RecordingEncodeableType(new XmlQualifiedName(typeName.Name, TargetNamespace));
                m_factory.AddType(type);
                return type;
            }

            private readonly RecordingComplexTypeFactory m_factory;
        }

        private sealed class RecordingComplexTypeFieldBuilder : IComplexTypeFieldBuilder
        {
            public RecordingComplexTypeFieldBuilder(
                RecordingComplexTypeFactory factory,
                XmlQualifiedName xmlName,
                StructureDefinition definition)
            {
                m_factory = factory;
                m_xmlName = xmlName;
                m_definition = definition;
            }

            public void AddTypeIdAttribute(
                ExpandedNodeId complexTypeId,
                ExpandedNodeId binaryEncodingId,
                ExpandedNodeId xmlEncodingId)
            {
            }

            public void AddField(
                StructureField field,
                IType? fieldType,
                int order,
                bool allowSubTypes)
            {
                m_fields.Add(field.Name!);
            }

            public IEncodeableType GetStructureType()
            {
                return new RecordingEncodeableType(m_xmlName);
            }

            public IEncodeableType CreateType()
            {
                Assert.That(m_fields, Has.Count.EqualTo(m_definition.Fields.Count));
                var type = new RecordingEncodeableType(m_xmlName);
                m_factory.AddType(type);
                return type;
            }

            private readonly RecordingComplexTypeFactory m_factory;
            private readonly XmlQualifiedName m_xmlName;
            private readonly StructureDefinition m_definition;
            private readonly List<string> m_fields = [];
        }

        private sealed class RecordingEncodeableType : IEncodeableType
        {
            public RecordingEncodeableType(XmlQualifiedName xmlName)
            {
                XmlName = xmlName;
            }

            public Type Type => typeof(RecordingEncodeableType);

            public XmlQualifiedName XmlName { get; }

            public IEncodeable CreateInstance()
            {
                throw new NotSupportedException();
            }
        }

        private sealed class RecordingEnumeratedType : IEnumeratedType
        {
            public RecordingEnumeratedType(XmlQualifiedName xmlName)
            {
                XmlName = xmlName;
            }

            public Type Type => typeof(RecordingEnumeratedType);

            public EnumValue Default => default;

            public XmlQualifiedName XmlName { get; }

            public bool TryGetSymbol(int value, out string? symbol)
            {
                symbol = null;
                return false;
            }

            public bool TryGetValue(string symbol, out int value)
            {
                value = default;
                return false;
            }
        }

        private sealed class ThrowingComplexTypeResolver : IComplexTypeResolver
        {
            public NamespaceTable NamespaceUris { get; } = new();

            public IEncodeableFactoryBuilder FactoryBuilder { get; } = EncodeableFactory.Create().Builder;

            public NodeIdDictionary<DataDictionary> DataTypeSystem { get; } = [];

            public Task<IReadOnlyDictionary<NodeId, DataDictionary>> LoadDataTypeSystem(
                NodeId dataTypeSystem = default,
                CancellationToken ct = default)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }

            public Task<(
                ExpandedNodeId typeId,
                ExpandedNodeId encodingId,
                DataTypeNode? dataTypeNode
            )> BrowseTypeIdsForDictionaryComponentAsync(
                ExpandedNodeId nodeId,
                CancellationToken ct = default)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }

            public Task<ArrayOf<NodeId>> BrowseForEncodingsAsync(
                ArrayOf<ExpandedNodeId> nodeIds,
                string[] supportedEncodings,
                CancellationToken ct = default)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }

            public Task<(
                ArrayOf<NodeId> encodings,
                ExpandedNodeId binaryEncodingId,
                ExpandedNodeId xmlEncodingId
            )> BrowseForEncodingsAsync(
                ExpandedNodeId nodeId,
                string[] supportedEncodings,
                CancellationToken ct = default)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }

            public Task<ArrayOf<INode>> LoadDataTypesAsync(
                ExpandedNodeId dataType,
                bool nestedSubTypes = false,
                bool addRootNode = false,
                bool filterUATypes = true,
                CancellationToken ct = default)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }

            public Task<INode?> FindAsync(ExpandedNodeId nodeId, CancellationToken ct = default)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }

            public Task<Variant> GetEnumTypeArrayAsync(
                ExpandedNodeId nodeId,
                CancellationToken ct = default)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }

            public Task<NodeId> FindSuperTypeAsync(NodeId typeId, CancellationToken ct = default)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError);
            }
        }
    }
}
