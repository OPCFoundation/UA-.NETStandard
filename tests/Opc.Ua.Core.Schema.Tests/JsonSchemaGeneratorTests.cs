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
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Schema.Json;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Tests for the JSON Schema generation of OPC UA data types.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class JsonSchemaGeneratorTests
    {
        [Test]
        public void CompactStructureProducesObjectSchemaWithProperties()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3001,
                "SampleType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Name", SchemaTestData.BuiltIn(BuiltInType.String)));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonCompact);

            JsonObject definition = Definition(schema, "SampleType");
            JsonObject properties = definition["properties"]!.AsObject();
            Assert.Multiple(() =>
            {
                Assert.That(definition["type"]!.GetValue<string>(), Is.EqualTo("object"));
                Assert.That(definition["additionalProperties"]!.GetValue<bool>(), Is.False);
                Assert.That(properties["Id"]!["type"]!.GetValue<string>(), Is.EqualTo("integer"));
                Assert.That(properties["Name"]!["type"]!.GetValue<string>(), Is.EqualTo("string"));
                Assert.That(RequiredNames(definition), Does.Contain("Id"));
                Assert.That(RequiredNames(definition), Does.Contain("Name"));
            });
        }

        [Test]
        public void OptionalFieldIsNotRequired()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3001,
                "SampleType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Note", SchemaTestData.BuiltIn(BuiltInType.String), optional: true));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonCompact);

            JsonObject definition = Definition(schema, "SampleType");
            Assert.Multiple(() =>
            {
                Assert.That(RequiredNames(definition), Does.Contain("Id"));
                Assert.That(RequiredNames(definition), Does.Not.Contain("Note"));
            });
        }

        [Test]
        public void ArrayFieldProducesArraySchema()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3001,
                "SampleType",
                SchemaTestData.Field(
                    "Items",
                    SchemaTestData.BuiltIn(BuiltInType.Int32),
                    ValueRanks.OneDimension));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonCompact);

            JsonObject items = Definition(schema, "SampleType")["properties"]!["Items"]!.AsObject();
            Assert.Multiple(() =>
            {
                Assert.That(items["type"]!.GetValue<string>(), Is.EqualTo("array"));
                Assert.That(items["items"]!["type"]!.GetValue<string>(), Is.EqualTo("integer"));
            });
        }

        [Test]
        public void Int64FieldIsEncodedAsString()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3001,
                "SampleType",
                SchemaTestData.Field("Big", SchemaTestData.BuiltIn(BuiltInType.Int64)));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonCompact);

            JsonObject big = Definition(schema, "SampleType")["properties"]!["Big"]!.AsObject();
            Assert.That(big["type"]!.GetValue<string>(), Is.EqualTo("string"));
        }

        [Test]
        public void ByteStringFieldIsEncodedAsBase64String()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3001,
                "SampleType",
                SchemaTestData.Field("Blob", SchemaTestData.BuiltIn(BuiltInType.ByteString)));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonCompact);

            JsonObject blob = Definition(schema, "SampleType")["properties"]!["Blob"]!.AsObject();
            Assert.Multiple(() =>
            {
                Assert.That(blob["type"]!.GetValue<string>(), Is.EqualTo("string"));
                Assert.That(blob["contentEncoding"]!.GetValue<string>(), Is.EqualTo("base64"));
            });
        }

        [Test]
        public void CompactEnumProducesIntegerWithOptions()
        {
            UaTypeDescription type = SchemaTestData.Enumeration(
                3010,
                "Color",
                ("Red", 0),
                ("Green", 1),
                ("Blue", 2));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonCompact);

            JsonObject definition = Definition(schema, "Color");
            Assert.Multiple(() =>
            {
                Assert.That(definition["type"]!.GetValue<string>(), Is.EqualTo("integer"));
                Assert.That(definition["oneOf"]!.AsArray(), Has.Count.EqualTo(3));
            });
        }

        [Test]
        public void VerboseEnumProducesStringEnum()
        {
            UaTypeDescription type = SchemaTestData.Enumeration(
                3010,
                "Color",
                ("Red", 0),
                ("Green", 1));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonVerbose);

            JsonObject definition = Definition(schema, "Color");
            var names = new List<string>();
            foreach (JsonNode? node in definition["enum"]!.AsArray())
            {
                if (node != null)
                {
                    names.Add(node.GetValue<string>());
                }
            }
            Assert.Multiple(() =>
            {
                Assert.That(definition["type"]!.GetValue<string>(), Is.EqualTo("string"));
                Assert.That(names, Does.Contain("Red_0"));
                Assert.That(names, Does.Contain("Green_1"));
            });
        }

        [Test]
        public void ReferencedTypeProducesRefAndInlinesDependency()
        {
            UaTypeDescription inner = SchemaTestData.Structure(
                3002,
                "Inner",
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            UaTypeDescription outer = SchemaTestData.Structure(
                3001,
                "Outer",
                SchemaTestData.Field("Child", new NodeId(3002, SchemaTestData.TestNamespaceIndex)));
            ISchemaProvider provider = SchemaTestData.CreateProvider(inner, outer);

            IUaSchema schema = provider.CreateSchema(outer, UaSchemaFormat.JsonCompact);

            JsonObject outerDefinition = Definition(schema, "Outer");
            string reference = outerDefinition["properties"]!["Child"]!["$ref"]!.GetValue<string>();
            Assert.Multiple(() =>
            {
                Assert.That(reference, Is.EqualTo("#/$defs/Inner"));
                Assert.That(Definitions(schema).ContainsKey("Inner"), Is.True);
            });
        }

        [Test]
        public void CrossNamespaceTypesWithSameNameProduceDistinctDefinitions()
        {
            UaTypeDescription localDuplicate = SchemaTestData.Structure(
                3031,
                "Duplicate",
                SchemaTestData.Field("LocalValue", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            UaTypeDescription foreignDuplicate = SchemaTestData.Structure(
                3032,
                "Duplicate",
                SchemaTestData.OtherNamespace,
                SchemaTestData.OtherNamespaceIndex,
                SchemaTestData.Field("ForeignValue", SchemaTestData.BuiltIn(BuiltInType.String)));
            UaTypeDescription outer = SchemaTestData.Structure(
                3030,
                "Outer",
                SchemaTestData.Field("Local", new NodeId(3031, SchemaTestData.TestNamespaceIndex)),
                SchemaTestData.Field("Foreign", new NodeId(3032, SchemaTestData.OtherNamespaceIndex)));
            ISchemaProvider provider = SchemaTestData.CreateProvider(localDuplicate, foreignDuplicate, outer);

            IUaSchema schema = provider.CreateSchema(outer, UaSchemaFormat.JsonCompact);

            JsonObject definitions = Definitions(schema);
            JsonObject outerDefinition = Definition(schema, "Outer");
            string localReference = outerDefinition["properties"]!["Local"]!["$ref"]!.GetValue<string>();
            string foreignReference = outerDefinition["properties"]!["Foreign"]!["$ref"]!.GetValue<string>();
            string foreignKey = DefinitionName(foreignReference);
            Assert.Multiple(() =>
            {
                Assert.That(localReference, Is.EqualTo("#/$defs/Duplicate"));
                Assert.That(foreignReference, Is.Not.EqualTo("#/$defs/Duplicate"));
                Assert.That(definitions.ContainsKey("Duplicate"), Is.True);
                Assert.That(definitions.ContainsKey(foreignKey), Is.True);
                Assert.That(definitions[foreignKey]!["title"]!.GetValue<string>(), Is.EqualTo("Duplicate"));
            });
        }

        [Test]
        public void AnyValueRankAllowsScalarOrUnconstrainedArray()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3033,
                "AnyRankType",
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32), ValueRanks.Any));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonCompact);

            JsonArray options = Definition(schema, "AnyRankType")["properties"]!["Value"]!["oneOf"]!.AsArray();
            Assert.Multiple(() =>
            {
                Assert.That(options, Has.Count.EqualTo(2));
                Assert.That(options[0]!["type"]!.GetValue<string>(), Is.EqualTo("integer"));
                Assert.That(options[1]!["type"]!.GetValue<string>(), Is.EqualTo("array"));
                Assert.That(options[1]!.AsObject().ContainsKey("items"), Is.False);
            });
        }

        [Test]
        public void ScalarOrOneDimensionValueRankAllowsOnlyScalarOrOneDimensionalArray()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3034,
                "ScalarOrArrayType",
                SchemaTestData.Field(
                    "Value",
                    SchemaTestData.BuiltIn(BuiltInType.Int32),
                    ValueRanks.ScalarOrOneDimension));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonCompact);

            JsonArray options = Definition(schema, "ScalarOrArrayType")["properties"]!["Value"]!["oneOf"]!.AsArray();
            Assert.Multiple(() =>
            {
                Assert.That(options, Has.Count.EqualTo(2));
                Assert.That(options[0]!["type"]!.GetValue<string>(), Is.EqualTo("integer"));
                Assert.That(options[1]!["type"]!.GetValue<string>(), Is.EqualTo("array"));
                Assert.That(options[1]!["items"]!["type"]!.GetValue<string>(), Is.EqualTo("integer"));
            });
        }

        [Test]
        public void UnionProducesOneOfSchema()
        {
            UaTypeDescription type = SchemaTestData.Union(
                3020,
                "Choice",
                SchemaTestData.Field("Number", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Text", SchemaTestData.BuiltIn(BuiltInType.String)));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonCompact);

            JsonObject definition = Definition(schema, "Choice");
            Assert.That(definition["oneOf"]!.AsArray(), Has.Count.EqualTo(2));
        }

        [Test]
        public void NamespaceScopeIncludesAllTypes()
        {
            UaTypeDescription inner = SchemaTestData.Structure(
                3002,
                "Inner",
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            UaTypeDescription outer = SchemaTestData.Structure(
                3001,
                "Outer",
                SchemaTestData.Field("Child", new NodeId(3002, SchemaTestData.TestNamespaceIndex)));
            ISchemaProvider provider = SchemaTestData.CreateProvider(inner, outer);

            IUaSchema schema = provider.CreateSchema(outer, UaSchemaFormat.JsonCompact, UaSchemaScope.Namespace);

            JsonObject definitions = Definitions(schema);
            Assert.Multiple(() =>
            {
                Assert.That(definitions.ContainsKey("Outer"), Is.True);
                Assert.That(definitions.ContainsKey("Inner"), Is.True);
            });
        }

        [Test]
        public void GeneratedSchemaIsValidJsonWithDialect()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3001,
                "SampleType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonCompact);

            string json = schema.ToSchemaString();
            Assert.Multiple(() =>
            {
                Assert.That(() => JsonNode.Parse(json), Throws.Nothing);
                Assert.That(((JsonSchemaDocument)schema).Root["$schema"]!.GetValue<string>(),
                    Is.EqualTo(JsonSchemaConstants.Dialect));
                Assert.That(schema.MediaType, Is.EqualTo("application/schema+json"));
            });
        }

        [Test]
        public void CompactUnionIncludesSwitchField()
        {
            UaTypeDescription type = SchemaTestData.Union(
                3020,
                "Choice",
                SchemaTestData.Field("Number", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Text", SchemaTestData.BuiltIn(BuiltInType.String)));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonCompact);

            JsonObject firstOption = Definition(schema, "Choice")["oneOf"]!.AsArray()[0]!.AsObject();
            JsonObject switchField = firstOption["properties"]!["SwitchField"]!.AsObject();
            Assert.Multiple(() =>
            {
                Assert.That(switchField["const"]!.GetValue<int>(), Is.EqualTo(1));
                Assert.That(RequiredNames(firstOption), Does.Contain("SwitchField"));
            });
        }

        [Test]
        public void VerboseUnionOmitsSwitchField()
        {
            UaTypeDescription type = SchemaTestData.Union(
                3020,
                "Choice",
                SchemaTestData.Field("Number", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonVerbose);

            JsonObject firstOption = Definition(schema, "Choice")["oneOf"]!.AsArray()[0]!.AsObject();
            Assert.That(firstOption["properties"]!.AsObject().ContainsKey("SwitchField"), Is.False);
        }

        [Test]
        public void CompactOptionalStructIncludesEncodingMask()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3001,
                "OptionalType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Note", SchemaTestData.BuiltIn(BuiltInType.String), optional: true));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonCompact);

            JsonObject definition = Definition(schema, "OptionalType");
            Assert.Multiple(() =>
            {
                Assert.That(definition["properties"]!.AsObject().ContainsKey("EncodingMask"), Is.True);
                Assert.That(RequiredNames(definition), Does.Contain("EncodingMask"));
                Assert.That(RequiredNames(definition), Does.Not.Contain("Note"));
            });
        }

        [Test]
        public void VerboseOptionalStructOmitsEncodingMask()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3001,
                "OptionalType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Note", SchemaTestData.BuiltIn(BuiltInType.String), optional: true));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonVerbose);

            JsonObject definition = Definition(schema, "OptionalType");
            Assert.That(definition["properties"]!.AsObject().ContainsKey("EncodingMask"), Is.False);
        }

        private static JsonObject Definitions(IUaSchema schema)
        {
            return ((JsonSchemaDocument)schema).Root["$defs"]!.AsObject();
        }

        private static JsonObject Definition(IUaSchema schema, string name)
        {
            return Definitions(schema)[name]!.AsObject();
        }

        private static string DefinitionName(string reference)
        {
            const string prefix = "#/$defs/";
            Assert.That(reference, Does.StartWith(prefix));
            return reference[prefix.Length..];
        }

        private static List<string> RequiredNames(JsonObject definition)
        {
            var names = new List<string>();
            if (definition["required"] is JsonArray array)
            {
                foreach (JsonNode? node in array)
                {
                    if (node != null)
                    {
                        names.Add(node.GetValue<string>());
                    }
                }
            }
            return names;
        }
    }
}
