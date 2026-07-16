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
using System.Threading.Tasks;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Unit tests for the pure and mockable surface of the complex type system that
    /// can be exercised through a hand-written resolver without a live session.
    /// </summary>
    [TestFixture]
    [Category("ComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ComplexTypeSystemUnitTests
    {
        /// <summary>
        /// The complex type system exposes the resolver data type system dictionary as-is.
        /// </summary>
        [Test]
        public void DataTypeSystemReturnsResolverDictionary()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var mockResolver = new MockResolver();

            var typeSystem = new ComplexTypeSystem(mockResolver, telemetry);

            Assert.That(typeSystem.DataTypeSystem, Is.SameAs(mockResolver.DataTypeSystem));
            Assert.That(typeSystem.DataTypeSystem, Is.Empty);
        }

        /// <summary>
        /// Constructing with an explicit resolver and factory yields an empty type system.
        /// </summary>
        [Test]
        public void ConstructWithResolverAndFactoryInitializesEmptyCaches()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var mockResolver = new MockResolver();
            var factory = new DefaultComplexTypeFactory();

            var typeSystem = new ComplexTypeSystem(mockResolver, factory, telemetry);

            Assert.Multiple(() =>
            {
                Assert.That(typeSystem.GetDefinedTypes(), Is.Empty);
                Assert.That(typeSystem.GetDefinedDataTypeIds(), Is.Empty);
                Assert.That(factory.GetTypes(), Is.Empty);
            });
        }

        /// <summary>
        /// Registering with a null registry throws an argument null exception.
        /// </summary>
        [Test]
        public void RegisterDataTypeDefinitionsWithNullRegistryThrows()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var typeSystem = new ComplexTypeSystem(new MockResolver(), telemetry);

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => typeSystem.RegisterDataTypeDefinitions(null));
            Assert.That(exception.ParamName, Is.EqualTo("registry"));
        }

        /// <summary>
        /// An unknown data type id resolves to an empty definition dictionary.
        /// </summary>
        [Test]
        public void GetDataTypeDefinitionsForDataTypeWithUnknownIdReturnsEmpty()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var typeSystem = new ComplexTypeSystem(new MockResolver(), telemetry);

            NodeIdDictionary<DataTypeDefinition> definitions =
                typeSystem.GetDataTypeDefinitionsForDataType(new ExpandedNodeId(12345u));

            Assert.That(definitions, Is.Empty);
        }

        /// <summary>
        /// A null data type id resolves to an empty definition dictionary.
        /// </summary>
        [Test]
        public void GetDataTypeDefinitionsForDataTypeWithNullIdReturnsEmpty()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var typeSystem = new ComplexTypeSystem(new MockResolver(), telemetry);

            NodeIdDictionary<DataTypeDefinition> definitions =
                typeSystem.GetDataTypeDefinitionsForDataType(ExpandedNodeId.Null);

            Assert.That(definitions, Is.Empty);
        }

        /// <summary>
        /// The disable flags round-trip through their setters and default to false.
        /// </summary>
        [Test]
        public void DisableFlagsRoundTripThroughSetters()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var typeSystem = new ComplexTypeSystem(new MockResolver(), telemetry);

            Assert.Multiple(() =>
            {
                Assert.That(typeSystem.DisableDataTypeDefinition, Is.False);
                Assert.That(typeSystem.DisableDataTypeDictionary, Is.False);
            });

            typeSystem.DisableDataTypeDefinition = true;
            typeSystem.DisableDataTypeDictionary = true;

            Assert.Multiple(() =>
            {
                Assert.That(typeSystem.DisableDataTypeDefinition, Is.True);
                Assert.That(typeSystem.DisableDataTypeDictionary, Is.True);
            });

            typeSystem.DisableDataTypeDefinition = false;
            typeSystem.DisableDataTypeDictionary = false;

            Assert.Multiple(() =>
            {
                Assert.That(typeSystem.DisableDataTypeDefinition, Is.False);
                Assert.That(typeSystem.DisableDataTypeDictionary, Is.False);
            });
        }

        /// <summary>
        /// Loading with both definition and dictionary support disabled returns false.
        /// </summary>
        [Test]
        public async Task LoadAsyncWithDefinitionAndDictionaryDisabledReturnsFalseAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var typeSystem = new ComplexTypeSystem(new MockResolver(), telemetry)
            {
                DisableDataTypeDefinition = true,
                DisableDataTypeDictionary = true
            };

            bool loaded = await typeSystem.LoadAsync().ConfigureAwait(false);

            Assert.That(loaded, Is.False);
        }

        /// <summary>
        /// Loading a single structure type creates it in the factory and reports its name.
        /// </summary>
        [Test]
        public async Task LoadTypeAsyncCreatesStructureTypeAndRegistersItAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            MockResolver mockResolver = CreateCarTypeResolver(
                out DataTypeNode structureNode,
                out StructureDefinition structureDefinition);
            var factory = new DefaultComplexTypeFactory();
            var typeSystem = new ComplexTypeSystem(mockResolver, factory, telemetry);

            IType loaded = await typeSystem
                .LoadTypeAsync(structureNode.NodeId, false, true)
                .ConfigureAwait(false);

            var expectedId = NodeId.ToExpandedNodeId(
                structureNode.NodeId,
                mockResolver.NamespaceUris);
            var expectedName = new XmlQualifiedName("CarType", Namespaces.MockResolverUrl);
            NodeIdDictionary<DataTypeDefinition> definitions =
                typeSystem.GetDataTypeDefinitionsForDataType(structureNode.NodeId);

            Assert.Multiple(() =>
            {
                Assert.That(loaded.XmlName, Is.EqualTo(expectedName));
                Assert.That(factory.GetTypes(), Has.Count.EqualTo(1));
                Assert.That(factory.GetTypes()[0].XmlName, Is.EqualTo(expectedName));
                Assert.That(typeSystem.GetDefinedTypes(), Has.Count.EqualTo(1));
                Assert.That(typeSystem.GetDefinedTypes(), Has.Member(expectedName));
                Assert.That(
                    typeSystem.GetDefinedDataTypeIds(),
                    Is.EqualTo([expectedId]));
                Assert.That(definitions, Has.Count.EqualTo(1));
                Assert.That(definitions[structureNode.NodeId], Is.EqualTo(structureDefinition));
            });
        }

        /// <summary>
        /// Clearing the data type cache removes loaded definitions but keeps created types.
        /// </summary>
        [Test]
        public async Task ClearDataTypeCacheRemovesLoadedDefinitionsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            MockResolver mockResolver = CreateCarTypeResolver(
                out DataTypeNode structureNode,
                out _);
            var factory = new DefaultComplexTypeFactory();
            var typeSystem = new ComplexTypeSystem(mockResolver, factory, telemetry);

            _ = await typeSystem
                .LoadTypeAsync(structureNode.NodeId, false, true)
                .ConfigureAwait(false);
            Assert.That(typeSystem.GetDefinedDataTypeIds(), Is.Not.Empty);

            typeSystem.ClearDataTypeCache();

            Assert.Multiple(() =>
            {
                Assert.That(typeSystem.GetDefinedDataTypeIds(), Is.Empty);
                Assert.That(
                    typeSystem.GetDataTypeDefinitionsForDataType(structureNode.NodeId),
                    Is.Empty);
                Assert.That(factory.GetTypes(), Has.Count.EqualTo(1));
            });
        }

        /// <summary>
        /// Extracting definitions for a nested structure returns the dependent definitions.
        /// </summary>
        [Test]
        public async Task GetDataTypeDefinitionsForDataTypeReturnsNestedStructuresAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var mockResolver = new MockResolver();
            ushort namespaceIndex = mockResolver.NamespaceUris.GetIndexOrAppend(
                Namespaces.MockResolverUrl);
            uint nodeId = 7100;

            var innerDefinition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField
                    {
                        Name = "Value",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
            var innerNode = new DataTypeNode
            {
                NodeId = new NodeId(nodeId++, namespaceIndex),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName("InnerType", namespaceIndex),
                DisplayName = LocalizedText.From("InnerType"),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(innerDefinition)
            };

            var outerDefinition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField
                    {
                        Name = "Inner",
                        DataType = innerNode.NodeId,
                        ValueRank = ValueRanks.Scalar
                    },
                    new StructureField
                    {
                        Name = "Label",
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
            var outerNode = new DataTypeNode
            {
                NodeId = new NodeId(nodeId++, namespaceIndex),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName("OuterType", namespaceIndex),
                DisplayName = LocalizedText.From("OuterType"),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(outerDefinition)
            };

            AddEncodingNodes(mockResolver, innerNode, namespaceIndex, ref nodeId);
            AddEncodingNodes(mockResolver, outerNode, namespaceIndex, ref nodeId);
            mockResolver.DataTypeNodes[innerNode.NodeId] = innerNode;
            mockResolver.DataTypeNodes[outerNode.NodeId] = outerNode;

            var typeSystem = new ComplexTypeSystem(mockResolver, telemetry);
            bool loaded = await typeSystem.LoadAsync(throwOnError: true).ConfigureAwait(false);

            NodeIdDictionary<DataTypeDefinition> definitions =
                typeSystem.GetDataTypeDefinitionsForDataType(outerNode.NodeId);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.True);
                Assert.That(definitions, Has.Count.EqualTo(2));
                Assert.That(definitions[outerNode.NodeId], Is.EqualTo(outerDefinition));
                Assert.That(definitions[innerNode.NodeId], Is.EqualTo(innerDefinition));
            });
        }

        /// <summary>
        /// A freshly created default complex type factory exposes no types.
        /// </summary>
        [Test]
        public void DefaultComplexTypeFactoryGetTypesOnFreshFactoryReturnsEmpty()
        {
            var factory = new DefaultComplexTypeFactory();

            Assert.That(factory.GetTypes(), Is.Empty);
        }

        /// <summary>
        /// Builds a resolver that exposes a single scalar structure type named CarType.
        /// </summary>
        /// <param name="structureNode">The created structure data type node.</param>
        /// <param name="structureDefinition">The structure definition of the node.</param>
        /// <returns>The mock resolver populated with the structure and its encodings.</returns>
        private static MockResolver CreateCarTypeResolver(
            out DataTypeNode structureNode,
            out StructureDefinition structureDefinition)
        {
            var mockResolver = new MockResolver();
            ushort namespaceIndex = mockResolver.NamespaceUris.GetIndexOrAppend(
                Namespaces.MockResolverUrl);
            uint nodeId = 7000;

            structureDefinition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField
                    {
                        Name = "Make",
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    new StructureField
                    {
                        Name = "NoOfPassengers",
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
            structureNode = new DataTypeNode
            {
                NodeId = new NodeId(nodeId++, namespaceIndex),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName("CarType", namespaceIndex),
                DisplayName = LocalizedText.From("CarType"),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            AddEncodingNodes(mockResolver, structureNode, namespaceIndex, ref nodeId);
            mockResolver.DataTypeNodes[structureNode.NodeId] = structureNode;
            return mockResolver;
        }

        /// <summary>
        /// Adds the default binary and xml encoding nodes for a data type node.
        /// </summary>
        /// <param name="mockResolver">The resolver to populate.</param>
        /// <param name="dataTypeNode">The data type node that owns the encodings.</param>
        /// <param name="namespaceIndex">The namespace index of the encoding nodes.</param>
        /// <param name="nodeId">The running node identifier counter.</param>
        private static void AddEncodingNodes(
            MockResolver mockResolver,
            DataTypeNode dataTypeNode,
            ushort namespaceIndex,
            ref uint nodeId)
        {
            AddEncodingNode(
                mockResolver, dataTypeNode, BrowseNames.DefaultBinary, namespaceIndex, ref nodeId);
            AddEncodingNode(
                mockResolver, dataTypeNode, BrowseNames.DefaultXml, namespaceIndex, ref nodeId);
        }

        /// <summary>
        /// Adds a single encoding node and its HasEncoding reference to a data type node.
        /// </summary>
        /// <param name="mockResolver">The resolver to populate.</param>
        /// <param name="dataTypeNode">The data type node that owns the encoding.</param>
        /// <param name="browseName">The browse name of the encoding node.</param>
        /// <param name="namespaceIndex">The namespace index of the encoding node.</param>
        /// <param name="nodeId">The running node identifier counter.</param>
        private static void AddEncodingNode(
            MockResolver mockResolver,
            DataTypeNode dataTypeNode,
            string browseName,
            ushort namespaceIndex,
            ref uint nodeId)
        {
            var description = new ReferenceDescription
            {
                NodeId = new NodeId(nodeId++, namespaceIndex),
                ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                BrowseName = QualifiedName.From(browseName),
                DisplayName = LocalizedText.From(browseName),
                IsForward = true,
                NodeClass = NodeClass.Object
            };
            var encoding = new Node(description);
            var reference = new ReferenceNode
            {
                ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                IsInverse = false,
                TargetId = description.NodeId
            };

            mockResolver.DataTypeNodes[encoding.NodeId] = encoding;
            dataTypeNode.References += reference;
        }
    }
}
