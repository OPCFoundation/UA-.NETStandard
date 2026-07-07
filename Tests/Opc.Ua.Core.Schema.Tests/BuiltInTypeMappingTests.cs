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
    /// Tests for Part 6 JSON mappings of OPC UA built-in data types.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class BuiltInTypeMappingTests
    {
        [Test]
        public void CompactJsonMapsBuiltInFieldsToPartSixShapes()
        {
            UaTypeDescription type = SchemaTestData.Structure(
                3301,
                "AllBuiltIns",
                Field(BuiltInType.Boolean),
                Field(BuiltInType.SByte),
                Field(BuiltInType.Byte),
                Field(BuiltInType.Int16),
                Field(BuiltInType.UInt16),
                Field(BuiltInType.Int32),
                Field(BuiltInType.UInt32),
                Field(BuiltInType.Int64),
                Field(BuiltInType.UInt64),
                Field(BuiltInType.Float),
                Field(BuiltInType.Double),
                Field(BuiltInType.String),
                Field(BuiltInType.DateTime),
                Field(BuiltInType.Guid),
                Field(BuiltInType.ByteString),
                Field(BuiltInType.XmlElement),
                Field(BuiltInType.NodeId),
                Field(BuiltInType.StatusCode),
                Field(BuiltInType.QualifiedName),
                Field(BuiltInType.LocalizedText),
                Field(BuiltInType.ExtensionObject),
                Field(BuiltInType.DataValue),
                Field(BuiltInType.Variant),
                Field(BuiltInType.DiagnosticInfo),
                Field(BuiltInType.Enumeration));
            ISchemaProvider provider = SchemaTestData.CreateProvider(type);

            IUaSchema schema = provider.CreateSchema(type, UaSchemaFormat.JsonCompact);

            JsonObject properties = Definition(schema, "AllBuiltIns")["properties"]!.AsObject();
            JsonObject definitions = Definitions(schema);
            Assert.Multiple(() =>
            {
                Assert.That(TypeName(properties, "Boolean"), Is.EqualTo("boolean"));
                Assert.That(TypeName(properties, "SByte"), Is.EqualTo("integer"));
                Assert.That(TypeName(properties, "Byte"), Is.EqualTo("integer"));
                Assert.That(TypeName(properties, "Int16"), Is.EqualTo("integer"));
                Assert.That(TypeName(properties, "UInt16"), Is.EqualTo("integer"));
                Assert.That(TypeName(properties, "Int32"), Is.EqualTo("integer"));
                Assert.That(TypeName(properties, "UInt32"), Is.EqualTo("integer"));
                Assert.That(TypeName(properties, "Int64"), Is.EqualTo("string"));
                Assert.That(TypeName(properties, "UInt64"), Is.EqualTo("string"));
                Assert.That(TypeNames(properties, "Float"), Is.EquivalentTo(s_numberOrStringTypes));
                Assert.That(TypeNames(properties, "Double"), Is.EquivalentTo(s_numberOrStringTypes));
                Assert.That(TypeName(properties, "String"), Is.EqualTo("string"));
                Assert.That(TypeName(properties, "DateTime"), Is.EqualTo("string"));
                Assert.That(PropertyValue(properties, "DateTime", "format"), Is.EqualTo("date-time"));
                Assert.That(TypeName(properties, "Guid"), Is.EqualTo("string"));
                Assert.That(PropertyValue(properties, "Guid", "format"), Is.EqualTo("uuid"));
                Assert.That(TypeName(properties, "ByteString"), Is.EqualTo("string"));
                Assert.That(PropertyValue(properties, "ByteString", "contentEncoding"), Is.EqualTo("base64"));
                Assert.That(TypeName(properties, "XmlElement"), Is.EqualTo("string"));
                Assert.That(Reference(properties, "NodeId"), Is.EqualTo("#/$defs/Ua_NodeId"));
                Assert.That(TypeName(properties, "StatusCode"), Is.EqualTo("integer"));
                Assert.That(Reference(properties, "QualifiedName"), Is.EqualTo("#/$defs/Ua_QualifiedName"));
                Assert.That(Reference(properties, "LocalizedText"), Is.EqualTo("#/$defs/Ua_LocalizedText"));
                Assert.That(Reference(properties, "ExtensionObject"), Is.EqualTo("#/$defs/Ua_ExtensionObject"));
                Assert.That(Reference(properties, "DataValue"), Is.EqualTo("#/$defs/Ua_DataValue"));
                Assert.That(Reference(properties, "Variant"), Is.EqualTo("#/$defs/Ua_Variant"));
                Assert.That(Reference(properties, "DiagnosticInfo"), Is.EqualTo("#/$defs/Ua_DiagnosticInfo"));
                Assert.That(TypeName(properties, "Enumeration"), Is.EqualTo("integer"));
                Assert.That(definitions.ContainsKey("Ua_NodeId"), Is.True);
                Assert.That(definitions.ContainsKey("Ua_QualifiedName"), Is.True);
                Assert.That(definitions.ContainsKey("Ua_LocalizedText"), Is.True);
                Assert.That(definitions.ContainsKey("Ua_ExtensionObject"), Is.True);
                Assert.That(definitions.ContainsKey("Ua_DataValue"), Is.True);
                Assert.That(definitions.ContainsKey("Ua_Variant"), Is.True);
                Assert.That(definitions.ContainsKey("Ua_DiagnosticInfo"), Is.True);
            });
        }

        private static StructureField Field(BuiltInType builtInType)
        {
            return SchemaTestData.Field(builtInType.ToString(), SchemaTestData.BuiltIn(builtInType));
        }

        private static JsonObject Definitions(IUaSchema schema)
        {
            return ((JsonSchemaDocument)schema).Root["$defs"]!.AsObject();
        }

        private static JsonObject Definition(IUaSchema schema, string name)
        {
            return Definitions(schema)[name]!.AsObject();
        }

        private static string TypeName(JsonObject properties, string name)
        {
            return properties[name]!["type"]!.GetValue<string>();
        }

        private static List<string> TypeNames(JsonObject properties, string name)
        {
            var result = new List<string>();
            foreach (JsonNode? node in properties[name]!["type"]!.AsArray())
            {
                if (node != null)
                {
                    result.Add(node.GetValue<string>());
                }
            }
            return result;
        }

        private static string Reference(JsonObject properties, string name)
        {
            return properties[name]!["$ref"]!.GetValue<string>();
        }

        private static string PropertyValue(JsonObject properties, string name, string propertyName)
        {
            return properties[name]![propertyName]!.GetValue<string>();
        }

        private static readonly string[] s_numberOrStringTypes = ["number", "string"];
    }
}
