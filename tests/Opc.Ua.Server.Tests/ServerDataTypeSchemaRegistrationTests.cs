/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Schema;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests server-side data type schema registration.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    [Parallelizable]
    public class ServerDataTypeSchemaRegistrationTests
    {
        [Test]
        public void RegisterDataTypeSchemasRegistersDataTypeStateDefinition()
        {
            const string namespaceUri = "urn:opcfoundation.org:tests:server:schema";
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(Types.Namespaces.OpcUa);
            ushort namespaceIndex = namespaceUris.GetIndexOrAppend(namespaceUri);
            var typeId = new NodeId(6001, namespaceIndex);

            var dataType = new DataTypeState
            {
                NodeId = typeId,
                BrowseName = new QualifiedName("ServerSchemaType", namespaceIndex),
                SuperTypeId = DataTypeIds.Structure,
                DataTypeDefinition = new ExtensionObject(new StructureDefinition
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
                })
            };
            var nodes = new NodeStateCollection
            {
                dataType,
                new BaseObjectState(null)
            };
            var services = new ServiceCollection();
            services.AddOpcUa().AddSchemaGeneration();

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            DataTypeDefinitionRegistry registry = serviceProvider.GetRequiredService<DataTypeDefinitionRegistry>();

            int registered = nodes.RegisterDataTypeSchemas(registry, namespaceUris);
            ISchemaProvider schemaProvider = serviceProvider.GetRequiredService<ISchemaProvider>();
            bool resolved = schemaProvider.TryGetSchema(
                new ExpandedNodeId(typeId),
                UaSchemaFormat.JsonCompact,
                UaSchemaScope.Type,
                out IUaSchema? schema);

            Assert.Multiple(() =>
            {
                Assert.That(registered, Is.EqualTo(1));
                Assert.That(registry.TryResolve(typeId, out UaTypeDescription? description), Is.True);
                Assert.That(description, Is.Not.Null);
                Assert.That(description!.NamespaceUri, Is.EqualTo(namespaceUri));
                Assert.That(resolved, Is.True);
                Assert.That(schema, Is.Not.Null);
            });
        }
    }
}
