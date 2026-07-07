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
using NUnit.Framework;
using Opc.Ua.Schema;
using Opc.Ua.Schema.Json;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Tests schema-registration support for complex types loaded from a resolver.
    /// </summary>
    [TestFixture]
    [Category("ComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SchemaRegistrationTests
    {
        /// <summary>
        /// Verifies loaded structure and enum definitions can be registered for schema generation.
        /// </summary>
        [Test]
        public async Task RegisterDataTypeDefinitionsAddsLoadedStructureAndEnumDefinitions()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var mockResolver = new MockResolver();
            ushort namespaceIndex = mockResolver.NamespaceUris.GetIndexOrAppend(Namespaces.MockResolverUrl);
            uint nodeId = 6000;

            var enumDefinition = new EnumDefinition
            {
                Fields =
                [
                    new EnumField { Name = "Red", Value = 0 },
                    new EnumField { Name = "Blue", Value = 1 }
                ]
            };
            var enumNode = new DataTypeNode
            {
                NodeId = new NodeId(nodeId++, namespaceIndex),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName("VehicleColor", namespaceIndex),
                DisplayName = LocalizedText.From("VehicleColor"),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(enumDefinition)
            };

            var structureDefinition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField
                    {
                        Name = "Model",
                        Description = LocalizedText.From("The model"),
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    new StructureField
                    {
                        Name = "Color",
                        Description = LocalizedText.From("The color"),
                        DataType = enumNode.NodeId,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
            var structureNode = new DataTypeNode
            {
                NodeId = new NodeId(nodeId++, namespaceIndex),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName("VehicleType", namespaceIndex),
                DisplayName = LocalizedText.From("VehicleType"),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(structureDefinition)
            };

            AddEncodingNodes(mockResolver, structureNode, namespaceIndex, ref nodeId);
            mockResolver.DataTypeNodes[enumNode.NodeId] = enumNode;
            mockResolver.DataTypeNodes[structureNode.NodeId] = structureNode;

            var typeSystem = new ComplexTypeSystem(mockResolver, new ComplexTypeBuilderFactory(), telemetry);
            bool loaded = await typeSystem.LoadAsync(throwOnError: true).ConfigureAwait(false);

            var registry = new DataTypeDefinitionRegistry();
            DataTypeDefinitionRegistry returnedRegistry = typeSystem.RegisterDataTypeDefinitions(registry);

            bool structureResolved = registry.TryResolve(
                new ExpandedNodeId(structureNode.NodeId),
                out UaTypeDescription structureDescription);
            bool enumResolved = registry.TryResolve(
                new ExpandedNodeId(enumNode.NodeId),
                out UaTypeDescription enumDescription);
            var provider = new DefaultSchemaProvider(registry, [CreateJsonSchemaGenerator()]);
            bool schemaResolved = provider.TryGetSchema(
                new ExpandedNodeId(structureNode.NodeId),
                UaSchemaFormat.JsonCompact,
                UaSchemaScope.Type,
                out IUaSchema schema);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.True);
                Assert.That(returnedRegistry, Is.SameAs(registry));
                Assert.That(structureResolved, Is.True);
                Assert.That(structureDescription, Is.Not.Null);
                Assert.That(structureDescription!.TypeId.InnerNodeId, Is.EqualTo(structureNode.NodeId));
                Assert.That(structureDescription.BrowseName, Is.EqualTo(structureNode.BrowseName));
                Assert.That(structureDescription.Definition, Is.SameAs(structureDefinition));
                Assert.That(
                    structureDescription.NamespaceUri,
                    Is.EqualTo(Namespaces.MockResolverUrl));
                Assert.That(enumResolved, Is.True);
                Assert.That(enumDescription, Is.Not.Null);
                Assert.That(enumDescription!.TypeId.InnerNodeId, Is.EqualTo(enumNode.NodeId));
                Assert.That(enumDescription.BrowseName, Is.EqualTo(enumNode.BrowseName));
                Assert.That(enumDescription.Definition, Is.SameAs(enumDefinition));
                Assert.That(schemaResolved, Is.True);
                Assert.That(schema, Is.Not.Null);
                Assert.That(schema!.ToSchemaString(), Does.Contain("VehicleType"));
            });
        }

        private static IUaSchemaGenerator CreateJsonSchemaGenerator()
        {
            Type generatorType = typeof(JsonSchemaDocument).Assembly.GetType(
                "Opc.Ua.Schema.Json.JsonSchemaGenerator",
                throwOnError: true)!;
            return (IUaSchemaGenerator)Activator.CreateInstance(generatorType, nonPublic: true)!;
        }

        private static void AddEncodingNodes(
            MockResolver mockResolver,
            DataTypeNode dataTypeNode,
            ushort namespaceIndex,
            ref uint nodeId)
        {
            AddEncodingNode(mockResolver, dataTypeNode, BrowseNames.DefaultBinary, namespaceIndex, ref nodeId);
            AddEncodingNode(mockResolver, dataTypeNode, BrowseNames.DefaultXml, namespaceIndex, ref nodeId);
        }

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
