/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua.Schema.Json;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Tests that source-generated encodeable types expose their data type
    /// definition through the encodeable factory.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class GeneratedTypeDefinitionTests
    {
        [Test]
        public void GeneratedStructureActivatorExposesDefinition()
        {
            DataTypeDefinition definition =
                ArgumentActivator.Instance.GetDataTypeDefinition(new NamespaceTable());

            Assert.That(definition, Is.InstanceOf<StructureDefinition>());
        }

        [Test]
        public void GeneratedEnumerationActivatorExposesDefinition()
        {
            DataTypeDefinition definition =
                NamingRuleTypeActivator.Instance.GetDataTypeDefinition(new NamespaceTable());

            Assert.That(definition, Is.InstanceOf<EnumDefinition>());
        }

        [Test]
        public void SchemasAreProducedFromGeneratedFactoryDefinitions()
        {
            var namespaceUris = new NamespaceTable();
            IEncodeableFactory factory = EncodeableFactory.Create();
            var resolver = new EncodeableFactoryDefinitionSource(factory, namespaceUris);
            var provider = new DefaultSchemaProvider(resolver, [new JsonSchemaGenerator()]);

            bool structureResolved = provider.TryGetSchema(
                new ExpandedNodeId(DataTypeIds.Argument),
                UaSchemaFormat.JsonCompact,
                UaSchemaScope.Type,
                out IUaSchema? structureSchema);
            bool enumerationResolved = provider.TryGetSchema(
                new ExpandedNodeId(DataTypeIds.NamingRuleType),
                UaSchemaFormat.JsonCompact,
                UaSchemaScope.Type,
                out IUaSchema? enumerationSchema);

            Assert.Multiple(() =>
            {
                Assert.That(structureResolved, Is.True);
                Assert.That(structureSchema!.ToSchemaString(), Does.Contain("Argument"));
                Assert.That(enumerationResolved, Is.True);
                Assert.That(enumerationSchema!.ToSchemaString(), Does.Contain("NamingRuleType"));
            });
        }
    }
}
