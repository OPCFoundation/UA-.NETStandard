/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Schema.Json;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Tests for the schema generation dependency injection registration.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class SchemaServiceCollectionExtensionsTests
    {
        [Test]
        public void AddSchemaGenerationResolvesProviderAndResolvesRegisteredType()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddSchemaGeneration();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            DataTypeDefinitionRegistry registry = serviceProvider.GetRequiredService<DataTypeDefinitionRegistry>();
            UaTypeDescription type = SchemaTestData.Structure(
                3001,
                "SampleType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            registry.Add(type);

            ISchemaProvider provider = serviceProvider.GetRequiredService<ISchemaProvider>();
            bool resolved = provider.TryGetSchema(
                type.TypeId,
                UaSchemaFormat.JsonCompact,
                UaSchemaScope.Type,
                out IUaSchema? schema);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.True);
                Assert.That(schema, Is.Not.Null);
                Assert.That(schema!.Format, Is.EqualTo(UaSchemaFormat.JsonCompact));
            });
        }

        [Test]
        public void AddSchemaGenerationResolvesUriFormRegisteredNode()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddSchemaGeneration();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(SchemaTestData.TestNamespace);
            var node = new DataTypeNode
            {
                NodeId = new NodeId(3002, SchemaTestData.TestNamespaceIndex),
                BrowseName = new QualifiedName("RegisteredNode", SchemaTestData.TestNamespaceIndex),
                DataTypeDefinition = new ExtensionObject(new StructureDefinition
                {
                    BaseDataType = DataTypeIds.Structure,
                    StructureType = StructureType.Structure,
                    Fields =
                    [
                        SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32))
                    ]
                })
            };
            DataTypeDefinitionRegistry registry =
                serviceProvider.GetRequiredService<DataTypeDefinitionRegistry>();

            bool added = registry.TryAddDataType(node, namespaceUris);
            ISchemaProvider provider = serviceProvider.GetRequiredService<ISchemaProvider>();
            bool resolved = provider.TryGetSchema(
                NodeId.ToExpandedNodeId(node.NodeId, namespaceUris),
                UaSchemaFormat.JsonCompact,
                UaSchemaScope.Type,
                out IUaSchema? schema);

            Assert.Multiple(() =>
            {
                Assert.That(added, Is.True);
                Assert.That(resolved, Is.True);
                Assert.That(schema, Is.Not.Null);
            });
        }

        [TestCaseSource(typeof(SchemaTestData), nameof(SchemaTestData.TypeIdentifierCases))]
        public void RegisteredIdentifierResolvesWithoutUsingBrowseNameNamespace(NodeId nodeId)
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddSchemaGeneration();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(SchemaTestData.TestNamespace);
            namespaceUris.Append(SchemaTestData.OtherNamespace);
            var node = new DataTypeNode
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("RegisteredEnum", SchemaTestData.OtherNamespaceIndex),
                DataTypeDefinition = new ExtensionObject(new EnumDefinition
                {
                    Fields = [new EnumField { Name = "Running", Value = 2 }]
                })
            };
            DataTypeDefinitionRegistry registry =
                serviceProvider.GetRequiredService<DataTypeDefinitionRegistry>();
            ISchemaProvider provider = serviceProvider.GetRequiredService<ISchemaProvider>();

            Assert.That(registry.TryAddDataType(node, namespaceUris), Is.True);
            Assert.That(
                provider.TryGetSchema(
                    NodeId.ToExpandedNodeId(nodeId, namespaceUris),
                    UaSchemaFormat.JsonCompact,
                    UaSchemaScope.Type,
                    out IUaSchema? uriSchema),
                Is.True);
            Assert.That(
                provider.TryGetSchema(
                    new ExpandedNodeId(nodeId),
                    UaSchemaFormat.JsonCompact,
                    UaSchemaScope.Type,
                    out IUaSchema? indexSchema),
                Is.True);
            Assert.That(uriSchema, Is.TypeOf<JsonSchemaDocument>());
            var document = (JsonSchemaDocument)uriSchema!;
            Assert.That(document.Root["title"]!.GetValue<string>(), Is.EqualTo("RegisteredEnum"));
            Assert.That(uriSchema.ToSchemaString(), Does.Contain("Running"));
            Assert.That(indexSchema!.ToSchemaString(), Is.EqualTo(uriSchema.ToSchemaString()));
        }

        [Test]
        public void AddSchemaGenerationThrowsForNullBuilder()
        {
            Assert.That(
                () => SchemaServiceCollectionExtensions.AddSchemaGeneration(null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("builder"));
        }

        [Test]
        public void TryGetSchemaReturnsFalseForUnknownType()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddSchemaGeneration();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            ISchemaProvider provider = serviceProvider.GetRequiredService<ISchemaProvider>();
            bool resolved = provider.TryGetSchema(
                new ExpandedNodeId(new NodeId(9999, 1)),
                UaSchemaFormat.JsonCompact,
                UaSchemaScope.Type,
                out IUaSchema? schema);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.False);
                Assert.That(schema, Is.Null);
            });
        }
    }
}
