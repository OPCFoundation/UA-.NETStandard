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
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Tests for converting legacy binary-schema type descriptions into OPC UA data type definitions.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class DataTypeDefinitionExtensionTests
    {
        [Test]
        public void ToStructureDefinitionMapsScalarArrayOptionalUnionAndRecursiveFields()
        {
            var typeDictionary = new Dictionary<XmlQualifiedName, NodeId>
            {
                [CustomQName("CustomEnum")] = new NodeId(7001, SchemaTestData.TestNamespaceIndex)
            };
            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(SchemaTestData.TestNamespace);
            NodeId dataTypeId = new(7002, SchemaTestData.TestNamespaceIndex);
            var structuredType = new Schema.Binary.StructuredType
            {
                Name = "Container",
                QName = CustomQName("Container"),
                Field =
                [
                    Field("EncodingMask", "Bit", Namespaces.OpcBinarySchema, length: 32),
                    Field("OptionalName", "CharArray", Namespaces.OpcBinarySchema, switchField: "EncodingMask"),
                    Field("Count", "Int32", Namespaces.OpcUa),
                    Field("Values", "UInt16", Namespaces.OpcUa, lengthField: "Count"),
                    Field("Choice", "UInt32", Namespaces.OpcUa),
                    Field("SelectedEnum", "CustomEnum", SchemaTestData.TestNamespace),
                    Field("Self", "Container", SchemaTestData.TestNamespace)
                ]
            };

            StructureDefinition definition = structuredType.ToStructureDefinition(
                new ExpandedNodeId(new NodeId(8002, SchemaTestData.TestNamespaceIndex)),
                typeDictionary,
                namespaceTable,
                dataTypeId);

            Assert.Multiple(() =>
            {
                Assert.That(definition.StructureType, Is.EqualTo(StructureType.StructureWithOptionalFields));
                Assert.That(definition.Fields, Has.Count.EqualTo(5));
                Assert.That(definition.Fields[0].Name, Is.EqualTo("OptionalName"));
                Assert.That(definition.Fields[0].DataType, Is.EqualTo(DataTypeIds.String));
                Assert.That(definition.Fields[0].IsOptional, Is.True);
                Assert.That(definition.Fields[1].Name, Is.EqualTo("Values"));
                Assert.That(definition.Fields[1].DataType, Is.EqualTo(DataTypeIds.UInt16));
                Assert.That(definition.Fields[1].ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(definition.Fields[3].DataType, Is.EqualTo(typeDictionary[CustomQName("CustomEnum")]));
                Assert.That(definition.Fields[4].DataType, Is.EqualTo(dataTypeId));
            });
        }

        [Test]
        public void ToStructureDefinitionMapsUnionMembers()
        {
            var structuredType = new Schema.Binary.StructuredType
            {
                Name = "Choice",
                QName = CustomQName("Choice"),
                Field =
                [
                    Field("Switch", "UInt32", Namespaces.OpcUa),
                    Field("IntChoice", "Int32", Namespaces.OpcUa, switchField: "Switch", switchValue: 1),
                    Field("TextChoice", "CharArray", Namespaces.OpcBinarySchema, switchField: "Switch", switchValue: 2)
                ]
            };

            StructureDefinition definition = structuredType.ToStructureDefinition(
                ExpandedNodeId.Null,
                [],
                new NamespaceTable(),
                new NodeId(7100, SchemaTestData.TestNamespaceIndex));

            Assert.Multiple(() =>
            {
                Assert.That(definition.StructureType, Is.EqualTo(StructureType.Union));
                StructureField[] fields = definition.Fields.ToArray()!;
                Assert.That(GetNames(fields), Is.EqualTo(UnionFieldNames));
                Assert.That(GetDataTypes(fields), Is.EqualTo(new[] { DataTypeIds.Int32, DataTypeIds.String }));
            });
        }

        [Test]
        public void ToStructureDefinitionRejectsUnsupportedBinarySchemaShapes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => UnsupportedStructure(Field("Payload", "Byte", Namespaces.OpcUa, isLengthInBytes: true)),
                    Throws.TypeOf<DataTypeNotSupportedException>());
                Assert.That(
                    () => UnsupportedStructure(Field("Payload", "Byte", Namespaces.OpcUa, terminator: [0])),
                    Throws.TypeOf<DataTypeNotSupportedException>());
                Assert.That(
                    () => UnsupportedStructure(Field("Payload", "Byte", Namespaces.OpcUa, length: 1)),
                    Throws.TypeOf<DataTypeNotSupportedException>());
                Assert.That(
                    () => UnsupportedStructure(
                        Field("Mask", "Bit", Namespaces.OpcBinarySchema, length: 8),
                        Field("Name", "CharArray", Namespaces.OpcBinarySchema, switchField: "Mask")),
                    Throws.TypeOf<DataTypeNotSupportedException>());
                Assert.That(
                    () => UnsupportedStructure(
                        Field("First", "Int32", Namespaces.OpcUa),
                        Field("Mask", "Bit", Namespaces.OpcBinarySchema)),
                    Throws.TypeOf<DataTypeNotSupportedException>());
                Assert.That(
                    () => UnsupportedStructure(Field("Name", "CharArray", Namespaces.OpcBinarySchema, switchField: "Mask")),
                    Throws.TypeOf<DataTypeNotSupportedException>());
                Assert.That(
                    () => UnsupportedStructure(
                        Field("Length", "Int32", Namespaces.OpcUa),
                        Field("Other", "Int32", Namespaces.OpcUa),
                        Field("Values", "UInt16", Namespaces.OpcUa, lengthField: "Length")),
                    Throws.TypeOf<DataTypeNotSupportedException>());
                Assert.That(
                    () => UnsupportedStructure(
                        Field("Mask", "Bit", Namespaces.OpcBinarySchema, length: 32),
                        Field("Choice", "Int32", Namespaces.OpcUa, switchField: "Mask", switchValue: 1)),
                    Throws.TypeOf<DataTypeNotSupportedException>());
            });
        }

        [Test]
        public void ToEnumDefinitionMapsBinarySchemaValuesAndFallbackNames()
        {
            var enumeratedType = new Schema.Binary.EnumeratedType
            {
                EnumeratedValue =
                [
                    new Schema.Binary.EnumeratedValue
                    {
                        Name = "Ready",
                        Value = 1,
                        Documentation = new Schema.Binary.Documentation { Text = ["Ready state"] }
                    },
                    new Schema.Binary.EnumeratedValue { Value = 2 }
                ]
            };

            EnumDefinition? definition = enumeratedType.ToEnumDefinition("State");
            EnumDefinition? missingName = new Schema.Binary.EnumeratedType
            {
                EnumeratedValue = [new Schema.Binary.EnumeratedValue { Value = 1 }]
            }.ToEnumDefinition(string.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(definition, Is.Not.Null);
                Assert.That(GetNames(definition!.Fields.ToArray()!), Is.EqualTo(BinaryEnumFieldNames));
                Assert.That(definition.Fields[0].Description.Text, Is.EqualTo("Ready state"));
                Assert.That(missingName, Is.Null);
            });
        }

        [Test]
        public void ToEnumDefinitionMapsExtensionObjectValuesAndSkipsInvalidEntries()
        {
            ArrayOf<ExtensionObject> values = new[]
            {
                new ExtensionObject(new EnumValueType { DisplayName = LocalizedText.From("Open"), Value = 10 }),
                new ExtensionObject(new NodeId(9999), "not an enum value"),
                new ExtensionObject(new EnumValueType { Value = 11 })
            };

            EnumDefinition? definition = values.ToEnumDefinition("DoorState");
            ArrayOf<ExtensionObject> missingNameValues = new[]
            {
                new ExtensionObject(new EnumValueType { Value = 1 })
            };
            EnumDefinition? missingName = missingNameValues.ToEnumDefinition(string.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(definition, Is.Not.Null);
                Assert.That(GetNames(definition!.Fields.ToArray()!), Is.EqualTo(ExtensionObjectEnumFieldNames));
                Assert.That(GetValues(definition.Fields.ToArray()!), Is.EqualTo(new long[] { 10, 11 }));
                Assert.That(missingName, Is.Null);
            });
        }

        [Test]
        public void ToEnumDefinitionMapsLocalizedTextValuesAndFallbackNames()
        {
            ArrayOf<LocalizedText> values = new[]
            {
                LocalizedText.From("Stopped"),
                LocalizedText.Null,
                LocalizedText.From("Running")
            };

            ArrayOf<LocalizedText> missingNameValues = new[] { LocalizedText.Null };
            EnumDefinition? definition = values.ToEnumDefinition("MachineState");
            EnumDefinition? missingName = missingNameValues.ToEnumDefinition(string.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(definition, Is.Not.Null);
                Assert.That(GetNames(definition!.Fields.ToArray()!), Is.EqualTo(LocalizedTextEnumFieldNames));
                Assert.That(GetValues(definition.Fields.ToArray()!), Is.EqualTo(new long[] { 0, 1, 2 }));
                Assert.That(missingName, Is.Null);
            });
        }

        private static Schema.Binary.FieldType Field(
            string name,
            string typeName,
            string typeNamespace,
            uint length = 0,
            string? lengthField = null,
            bool isLengthInBytes = false,
            string? switchField = null,
            uint switchValue = 0,
            byte[]? terminator = null)
        {
            return new Schema.Binary.FieldType
            {
                Name = name,
                TypeName = new XmlQualifiedName(typeName, typeNamespace),
                Length = length,
                LengthField = lengthField,
                IsLengthInBytes = isLengthInBytes,
                SwitchField = switchField,
                SwitchValue = switchValue,
                Terminator = terminator
            };
        }

        private static XmlQualifiedName CustomQName(string name)
        {
            return new XmlQualifiedName(name, SchemaTestData.TestNamespace);
        }

        private static void UnsupportedStructure(params Schema.Binary.FieldType[] fields)
        {
            var structuredType = new Schema.Binary.StructuredType
            {
                Name = "Unsupported",
                QName = CustomQName("Unsupported"),
                Field = fields
            };

            _ = structuredType.ToStructureDefinition(
                ExpandedNodeId.Null,
                [],
                new NamespaceTable(),
                new NodeId(7101, SchemaTestData.TestNamespaceIndex));
        }

        private static string?[] GetNames(IEnumerable<EnumField> fields)
        {
            return fields.Select(f => f.Name).ToArray();
        }

        private static string?[] GetNames(IEnumerable<StructureField> fields)
        {
            return fields.Select(f => f.Name).ToArray();
        }

        private static NodeId[] GetDataTypes(IEnumerable<StructureField> fields)
        {
            return fields.Select(f => f.DataType).ToArray();
        }

        private static long[] GetValues(IEnumerable<EnumField> fields)
        {
            return fields.Select(f => f.Value).ToArray();
        }

        private static readonly string[] UnionFieldNames = ["IntChoice", "TextChoice"];
        private static readonly string[] BinaryEnumFieldNames = ["Ready", "State_2"];
        private static readonly string[] ExtensionObjectEnumFieldNames = ["Open", "DoorState_11"];
        private static readonly string[] LocalizedTextEnumFieldNames = ["Stopped", "MachineState_1", "Running"];
    }
}
