// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Tests for the schema provider convenience extension methods.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class SchemaProviderExtensionsTests
    {
        [Test]
        public void GetJsonSchemaUsesCompactByDefault()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3001,
                "SampleType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.GetJsonSchema(type);

            Assert.That(schema.Format, Is.EqualTo(UaSchemaFormat.JsonCompact));
        }

        [Test]
        public void GetJsonSchemaVerboseUsesVerboseFlavor()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3001,
                "SampleType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.GetJsonSchema(type, verbose: true);

            Assert.That(schema.Format, Is.EqualTo(UaSchemaFormat.JsonVerbose));
        }

        [Test]
        public void CreateSchemaThrowsWhenNoGeneratorSupportsFormat()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3001,
                "SampleType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            var provider = new DefaultSchemaProvider(new DataTypeDefinitionRegistry(), []);

            Assert.That(
                () => provider.CreateSchema(type, UaSchemaFormat.JsonCompact),
                Throws.TypeOf<NotSupportedException>().With.Message.Contains("JsonCompact"));
        }

        [Test]
        public void TryGetJsonSchemaResolvesRegisteredType()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3001,
                "SampleType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            bool resolved = provider.TryGetJsonSchema(type.TypeId, out IUaSchema? schema);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.True);
                Assert.That(schema, Is.Not.Null);
            });
        }
    }
}
