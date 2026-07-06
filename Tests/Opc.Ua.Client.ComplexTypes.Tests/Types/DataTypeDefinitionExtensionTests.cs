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
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Unit tests for the pure binary-schema to DataTypeDefinition converters.
    /// </summary>
    [TestFixture]
    [Category("ComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DataTypeDefinitionExtensionTests
    {
        private const uint EncodingNumericId = 4711u;
        private const string CustomNamespace = "http://test.opcfoundation.org/ComplexTypesTests/";
        private const string BinarySchemaNamespace = "http://opcfoundation.org/BinarySchema/";
        private const string OpcUaNamespace = "http://opcfoundation.org/UA/";

        private static readonly NodeId s_recursiveDataTypeNodeId = new(9999u);

        /// <summary>
        /// A plain structure maps every field and carries the encoding id and base type.
        /// </summary>
        [Test]
        public void ToStructureDefinitionSimpleStructureMapsFieldsAndMetadata()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "Simple",
                Field("Id", UaType("Int32")),
                Field("Amount", UaType("Double")));

            StructureDefinition definition = Convert(structuredType);

            Assert.Multiple(() =>
            {
                Assert.That(definition.StructureType, Is.EqualTo(StructureType.Structure));
                Assert.That(definition.BaseDataType, Is.EqualTo(NodeId.Null));
                Assert.That(definition.DefaultEncodingId, Is.EqualTo(new NodeId(EncodingNumericId)));
                Assert.That(definition.Fields, Has.Count.EqualTo(2));
                Assert.That(definition.Fields[0].Name, Is.EqualTo("Id"));
                Assert.That(definition.Fields[0].DataType, Is.EqualTo(DataTypeIds.Int32));
                Assert.That(definition.Fields[0].ValueRank, Is.EqualTo(-1));
                Assert.That(definition.Fields[0].IsOptional, Is.False);
                Assert.That(definition.Fields[1].Name, Is.EqualTo("Amount"));
                Assert.That(definition.Fields[1].DataType, Is.EqualTo(DataTypeIds.Double));
                Assert.That(definition.Fields[1].ValueRank, Is.EqualTo(-1));
            });
        }

        /// <summary>
        /// An array whose length field directly precedes it collapses into a single
        /// array field with rank one.
        /// </summary>
        [Test]
        public void ToStructureDefinitionArrayWithPrecedingLengthFieldSetsValueRankOne()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "WithArray",
                Field("NoOfItems", UaType("Int32")),
                Field("Items", UaType("Int32"), lengthField: "NoOfItems"));

            StructureDefinition definition = Convert(structuredType);

            Assert.Multiple(() =>
            {
                Assert.That(definition.StructureType, Is.EqualTo(StructureType.Structure));
                Assert.That(definition.Fields, Has.Count.EqualTo(1));
                Assert.That(definition.Fields[0].Name, Is.EqualTo("Items"));
                Assert.That(definition.Fields[0].DataType, Is.EqualTo(DataTypeIds.Int32));
                Assert.That(definition.Fields[0].ValueRank, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// An array length field that is not the direct predecessor is rejected.
        /// </summary>
        [Test]
        public void ToStructureDefinitionArrayLengthFieldNotPrecedingThrows()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "BadArray",
                Field("NoOfItems", UaType("Int32")),
                Field("Middle", UaType("Int32")),
                Field("Items", UaType("Int32"), lengthField: "NoOfItems"));

            Assert.Throws<DataTypeNotSupportedException>(() => Convert(structuredType));
        }

        /// <summary>
        /// A union maps its members and skips the leading switch selector field.
        /// </summary>
        [Test]
        public void ToStructureDefinitionUnionMapsMembers()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "Choice",
                Field("Selector", UaType("UInt32")),
                Field("Alpha", UaType("Int32"), switchField: "Selector", switchValue: 1),
                Field("Beta", UaType("Double"), switchField: "Selector", switchValue: 2));

            StructureDefinition definition = Convert(structuredType);

            Assert.Multiple(() =>
            {
                Assert.That(definition.StructureType, Is.EqualTo(StructureType.Union));
                Assert.That(definition.Fields, Has.Count.EqualTo(2));
                Assert.That(definition.Fields[0].Name, Is.EqualTo("Alpha"));
                Assert.That(definition.Fields[0].DataType, Is.EqualTo(DataTypeIds.Int32));
                Assert.That(definition.Fields[0].IsOptional, Is.False);
                Assert.That(definition.Fields[1].Name, Is.EqualTo("Beta"));
                Assert.That(definition.Fields[1].DataType, Is.EqualTo(DataTypeIds.Double));
            });
        }

        /// <summary>
        /// A union whose switch selector is not the first field is rejected.
        /// </summary>
        [Test]
        public void ToStructureDefinitionUnionSwitchFieldNotFirstThrows()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "BadUnion",
                Field("Alpha", UaType("Int32"), switchField: "Selector", switchValue: 1),
                Field("Selector", UaType("UInt32")));

            Assert.Throws<DataTypeNotSupportedException>(() => Convert(structuredType));
        }

        /// <summary>
        /// A field encoded as a length in bytes is not supported.
        /// </summary>
        [Test]
        public void ToStructureDefinitionLengthInBytesThrows()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "BadLength",
                Field("Data", UaType("ByteString"), isLengthInBytes: true));

            Assert.Throws<DataTypeNotSupportedException>(() => Convert(structuredType));
        }

        /// <summary>
        /// A field that uses a terminator is not supported.
        /// </summary>
        [Test]
        public void ToStructureDefinitionTerminatorThrows()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "BadTerminator",
                Field("Data", BinaryType("CharArray"), terminator: [0]));

            Assert.Throws<DataTypeNotSupportedException>(() => Convert(structuredType));
        }

        /// <summary>
        /// A non-bit field that declares a fixed length is not supported.
        /// </summary>
        [Test]
        public void ToStructureDefinitionFixedLengthFieldThrows()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "BadFixed",
                Field("Fixed", UaType("Int32"), length: 4));

            Assert.Throws<DataTypeNotSupportedException>(() => Convert(structuredType));
        }

        /// <summary>
        /// Combining a union and a bit field in one structure is not supported.
        /// </summary>
        [Test]
        public void ToStructureDefinitionUnionWithBitFieldThrows()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "UnionAndBits",
                Field("Flag", BitType()),
                Field("Alpha", UaType("Int32"), switchField: "Selector", switchValue: 1));

            Assert.Throws<DataTypeNotSupportedException>(() => Convert(structuredType));
        }

        /// <summary>
        /// Optional fields whose selector bits add up to 32 map to a structure with
        /// optional fields and preserve the optional flag.
        /// </summary>
        [Test]
        public void ToStructureDefinitionOptionalFieldsMapsBitSelectors()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "Optional",
                Field("EnabledSpecified", BitType(), length: 1),
                Field("Reserved", BitType(), length: 31),
                Field("Count", UaType("Int32")),
                Field("Enabled", UaType("Double"), switchField: "EnabledSpecified"));

            StructureDefinition definition = Convert(structuredType);

            Assert.Multiple(() =>
            {
                Assert.That(definition.StructureType, Is.EqualTo(StructureType.StructureWithOptionalFields));
                Assert.That(definition.Fields, Has.Count.EqualTo(2));
                Assert.That(definition.Fields[0].Name, Is.EqualTo("Count"));
                Assert.That(definition.Fields[0].DataType, Is.EqualTo(DataTypeIds.Int32));
                Assert.That(definition.Fields[0].IsOptional, Is.False);
                Assert.That(definition.Fields[1].Name, Is.EqualTo("Enabled"));
                Assert.That(definition.Fields[1].DataType, Is.EqualTo(DataTypeIds.Double));
                Assert.That(definition.Fields[1].IsOptional, Is.True);
            });
        }

        /// <summary>
        /// Bit selectors that do not add up to 32 bits before a field are rejected.
        /// </summary>
        [Test]
        public void ToStructureDefinitionBitSelectorsNotThirtyTwoBitsThrows()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "ShortBits",
                Field("EnabledSpecified", BitType(), length: 1),
                Field("Value", UaType("Int32")));

            Assert.Throws<DataTypeNotSupportedException>(() => Convert(structuredType));
        }

        /// <summary>
        /// A bit selector that appears after a regular field is rejected.
        /// </summary>
        [Test]
        public void ToStructureDefinitionBitSelectorAfterFieldThrows()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "LateBit",
                Field("Value", UaType("Int32")),
                Field("Late", BitType(), length: 1));

            Assert.Throws<DataTypeNotSupportedException>(() => Convert(structuredType));
        }

        /// <summary>
        /// A field that references the enclosing structure resolves to the supplied
        /// data type node id.
        /// </summary>
        [Test]
        public void ToStructureDefinitionRecursiveFieldUsesDataTypeNodeId()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "Node",
                Field("Value", UaType("Int32")),
                Field("Next", CustomType("Node")));

            StructureDefinition definition = Convert(structuredType);

            Assert.Multiple(() =>
            {
                Assert.That(definition.Fields, Has.Count.EqualTo(2));
                Assert.That(definition.Fields[1].Name, Is.EqualTo("Next"));
                Assert.That(definition.Fields[1].DataType, Is.EqualTo(s_recursiveDataTypeNodeId));
            });
        }

        /// <summary>
        /// A custom-namespace type resolves through the supplied type dictionary.
        /// </summary>
        [Test]
        public void ToStructureDefinitionCustomNamespaceTypeResolvesFromDictionary()
        {
            var customType = new XmlQualifiedName("MyType", CustomNamespace);
            var referencedNodeId = new NodeId(777u, 3);
            var dictionary = new Dictionary<XmlQualifiedName, NodeId>
            {
                [customType] = referencedNodeId
            };
            Schema.Binary.StructuredType structuredType = Struct(
                "Container",
                Field("Ref", customType));

            StructureDefinition definition = Convert(structuredType, dictionary);

            Assert.Multiple(() =>
            {
                Assert.That(definition.Fields, Has.Count.EqualTo(1));
                Assert.That(definition.Fields[0].DataType, Is.EqualTo(referencedNodeId));
            });
        }

        /// <summary>
        /// A custom-namespace type missing from the type dictionary resolves to the
        /// null node id.
        /// </summary>
        [Test]
        public void ToStructureDefinitionCustomNamespaceTypeNotInDictionaryReturnsNullNodeId()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "Container",
                Field("Ref", new XmlQualifiedName("Unknown", CustomNamespace)));

            StructureDefinition definition = Convert(structuredType);

            Assert.Multiple(() =>
            {
                Assert.That(definition.Fields, Has.Count.EqualTo(1));
                Assert.That(definition.Fields[0].DataType, Is.EqualTo(NodeId.Null));
            });
        }

        /// <summary>
        /// The special binary schema type names map to their well-known data types.
        /// </summary>
        [Test]
        public void ToStructureDefinitionSpecialTypeNamesMapToWellKnownDataTypes()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "Specials",
                Field("Text", BinaryType("CharArray")),
                Field("Any", UaType("Variant")),
                Field("Ext", UaType("ExtensionObject")));

            StructureDefinition definition = Convert(structuredType);

            Assert.Multiple(() =>
            {
                Assert.That(definition.Fields, Has.Count.EqualTo(3));
                Assert.That(definition.Fields[0].DataType, Is.EqualTo(DataTypeIds.String));
                Assert.That(definition.Fields[1].DataType, Is.EqualTo(DataTypeIds.BaseDataType));
                Assert.That(definition.Fields[2].DataType, Is.EqualTo(DataTypeIds.Structure));
            });
        }

        /// <summary>
        /// An unknown standard type name resolves to the null node id.
        /// </summary>
        [Test]
        public void ToStructureDefinitionUnknownStandardTypeNameReturnsNullNodeId()
        {
            Schema.Binary.StructuredType structuredType = Struct(
                "Weird",
                Field("Mystery", UaType("NoSuchStandardType")));

            StructureDefinition definition = Convert(structuredType);

            Assert.Multiple(() =>
            {
                Assert.That(definition.Fields, Has.Count.EqualTo(1));
                Assert.That(definition.Fields[0].DataType, Is.EqualTo(NodeId.Null));
            });
        }

        /// <summary>
        /// A binary enumerated type maps its named values, documentation and display names.
        /// </summary>
        [Test]
        public void ToEnumDefinitionEnumeratedTypeMapsNamedValues()
        {
            var enumeratedType = new Schema.Binary.EnumeratedType
            {
                Name = "Color",
                EnumeratedValue =
                [
                    new Schema.Binary.EnumeratedValue
                    {
                        Name = "Red",
                        Value = 1,
                        Documentation = new Schema.Binary.Documentation { Text = ["Red color"] }
                    },
                    new Schema.Binary.EnumeratedValue { Name = "Green", Value = 2 }
                ]
            };

            EnumDefinition definition = enumeratedType.ToEnumDefinition("Color");

            Assert.That(definition, Is.Not.Null);
            EnumDefinition result = definition;
            Assert.Multiple(() =>
            {
                Assert.That(result.Fields, Has.Count.EqualTo(2));
                Assert.That(result.Fields[0].Name, Is.EqualTo("Red"));
                Assert.That(result.Fields[0].Value, Is.EqualTo(1));
                Assert.That(result.Fields[0].DisplayName.Text, Is.EqualTo("Red"));
                Assert.That(result.Fields[0].Description.Text, Is.EqualTo("Red color"));
                Assert.That(result.Fields[1].Name, Is.EqualTo("Green"));
                Assert.That(result.Fields[1].Value, Is.EqualTo(2));
                Assert.That(result.Fields[1].DisplayName.Text, Is.EqualTo("Green"));
                Assert.That(result.Fields[1].Description.Text, Is.Null);
            });
        }

        /// <summary>
        /// A binary enumerated value with an empty name uses the composed fallback name.
        /// </summary>
        [Test]
        public void ToEnumDefinitionEnumeratedTypeEmptyNameUsesFallback()
        {
            var enumeratedType = new Schema.Binary.EnumeratedType
            {
                EnumeratedValue = [new Schema.Binary.EnumeratedValue { Name = null, Value = 7 }]
            };

            EnumDefinition definition = enumeratedType.ToEnumDefinition("Status");

            Assert.That(definition, Is.Not.Null);
            EnumDefinition result = definition;
            Assert.Multiple(() =>
            {
                Assert.That(result.Fields, Has.Count.EqualTo(1));
                Assert.That(result.Fields[0].Name, Is.EqualTo("Status_7"));
                Assert.That(result.Fields[0].Value, Is.EqualTo(7));
                Assert.That(result.Fields[0].DisplayName.Text, Is.Null);
                Assert.That(result.Fields[0].Description.Text, Is.Null);
            });
        }

        /// <summary>
        /// A binary enumerated value with an empty name and empty type name is rejected.
        /// </summary>
        [Test]
        public void ToEnumDefinitionEnumeratedTypeEmptyNameAndTypeReturnsNull()
        {
            var enumeratedType = new Schema.Binary.EnumeratedType
            {
                EnumeratedValue = [new Schema.Binary.EnumeratedValue { Name = null, Value = 3 }]
            };

            EnumDefinition definition = enumeratedType.ToEnumDefinition(string.Empty);

            Assert.That(definition, Is.Null);
        }

        /// <summary>
        /// A binary enumerated type without values maps to an empty enum definition.
        /// </summary>
        [Test]
        public void ToEnumDefinitionEnumeratedTypeNullValuesReturnsEmptyDefinition()
        {
            var enumeratedType = new Schema.Binary.EnumeratedType
            {
                Name = "Empty",
                EnumeratedValue = null
            };

            EnumDefinition definition = enumeratedType.ToEnumDefinition("Empty");

            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Fields.Count, Is.Zero);
        }

        /// <summary>
        /// A list of EnumValueType extension objects maps entries and skips non-enum values.
        /// </summary>
        [Test]
        public void ToEnumDefinitionEnumValueTypesMapsAndSkipsNonEnumValues()
        {
            ArrayOf<ExtensionObject> values =
            [
                new ExtensionObject(new EnumValueType { Value = 10, DisplayName = new LocalizedText("Ten") }),
                new ExtensionObject(new EnumDefinition()),
                new ExtensionObject(new EnumValueType { Value = 20, DisplayName = LocalizedText.Null })
            ];

            EnumDefinition definition = values.ToEnumDefinition("Nums");

            Assert.That(definition, Is.Not.Null);
            EnumDefinition result = definition;
            Assert.Multiple(() =>
            {
                Assert.That(result.Fields, Has.Count.EqualTo(2));
                Assert.That(result.Fields[0].Name, Is.EqualTo("Ten"));
                Assert.That(result.Fields[0].Value, Is.EqualTo(10));
                Assert.That(result.Fields[0].DisplayName.Text, Is.EqualTo("Ten"));
                Assert.That(result.Fields[1].Name, Is.EqualTo("Nums_20"));
                Assert.That(result.Fields[1].Value, Is.EqualTo(20));
                Assert.That(result.Fields[1].DisplayName.Text, Is.EqualTo("Nums_20"));
            });
        }

        /// <summary>
        /// An EnumValueType with empty display name and empty type name is rejected.
        /// </summary>
        [Test]
        public void ToEnumDefinitionEnumValueTypesEmptyNameAndTypeReturnsNull()
        {
            ArrayOf<ExtensionObject> values =
            [
                new ExtensionObject(new EnumValueType { Value = 5, DisplayName = LocalizedText.Null })
            ];

            EnumDefinition definition = values.ToEnumDefinition(string.Empty);

            Assert.That(definition, Is.Null);
        }

        /// <summary>
        /// An empty EnumValueType list maps to an empty enum definition.
        /// </summary>
        [Test]
        public void ToEnumDefinitionEnumValueTypesEmptyListReturnsEmptyDefinition()
        {
            ArrayOf<ExtensionObject> values = [];

            EnumDefinition definition = values.ToEnumDefinition("Nums");

            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Fields.Count, Is.Zero);
        }

        /// <summary>
        /// A list of localized text names auto-numbers the enum values from zero.
        /// </summary>
        [Test]
        public void ToEnumDefinitionLocalizedTextAutoNumbersValues()
        {
            ArrayOf<LocalizedText> names =
            [
                LocalizedText.From("Zero"),
                LocalizedText.From("One"),
                LocalizedText.From("Two")
            ];

            EnumDefinition definition = names.ToEnumDefinition("Digit");

            Assert.That(definition, Is.Not.Null);
            EnumDefinition result = definition;
            Assert.Multiple(() =>
            {
                Assert.That(result.Fields, Has.Count.EqualTo(3));
                Assert.That(result.Fields[0].Name, Is.EqualTo("Zero"));
                Assert.That(result.Fields[0].Value, Is.Zero);
                Assert.That(result.Fields[0].DisplayName.Text, Is.EqualTo("Zero"));
                Assert.That(result.Fields[1].Name, Is.EqualTo("One"));
                Assert.That(result.Fields[1].Value, Is.EqualTo(1));
                Assert.That(result.Fields[2].Name, Is.EqualTo("Two"));
                Assert.That(result.Fields[2].Value, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// A localized text entry with empty text uses the composed fallback name.
        /// </summary>
        [Test]
        public void ToEnumDefinitionLocalizedTextEmptyTextUsesFallback()
        {
            ArrayOf<LocalizedText> names =
            [
                LocalizedText.From("First"),
                LocalizedText.Null
            ];

            EnumDefinition definition = names.ToEnumDefinition("Item");

            Assert.That(definition, Is.Not.Null);
            EnumDefinition result = definition;
            Assert.Multiple(() =>
            {
                Assert.That(result.Fields, Has.Count.EqualTo(2));
                Assert.That(result.Fields[0].Name, Is.EqualTo("First"));
                Assert.That(result.Fields[0].Value, Is.Zero);
                Assert.That(result.Fields[1].Name, Is.EqualTo("Item_1"));
                Assert.That(result.Fields[1].Value, Is.EqualTo(1));
                Assert.That(result.Fields[1].DisplayName.Text, Is.EqualTo("Item_1"));
            });
        }

        /// <summary>
        /// A localized text entry with empty text and empty type name is rejected.
        /// </summary>
        [Test]
        public void ToEnumDefinitionLocalizedTextEmptyTextAndTypeReturnsNull()
        {
            ArrayOf<LocalizedText> names = [LocalizedText.Null];

            EnumDefinition definition = names.ToEnumDefinition(string.Empty);

            Assert.That(definition, Is.Null);
        }

        /// <summary>
        /// An empty localized text list maps to an empty enum definition.
        /// </summary>
        [Test]
        public void ToEnumDefinitionLocalizedTextEmptyListReturnsEmptyDefinition()
        {
            ArrayOf<LocalizedText> names = [];

            EnumDefinition definition = names.ToEnumDefinition("Digit");

            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Fields.Count, Is.Zero);
        }

        private static StructureDefinition Convert(
            Schema.Binary.StructuredType structuredType,
            Dictionary<XmlQualifiedName, NodeId> typeDictionary = null)
        {
            return structuredType.ToStructureDefinition(
                new ExpandedNodeId(EncodingNumericId),
                typeDictionary ?? [],
                new NamespaceTable(),
                s_recursiveDataTypeNodeId);
        }

        private static Schema.Binary.StructuredType Struct(
            string name,
            params Schema.Binary.FieldType[] fields)
        {
            return new Schema.Binary.StructuredType
            {
                Name = name,
                QName = new XmlQualifiedName(name, CustomNamespace),
                Field = fields
            };
        }

        private static Schema.Binary.FieldType Field(
            string name,
            XmlQualifiedName typeName,
            string lengthField = null,
            string switchField = null,
            uint switchValue = 0,
            uint length = 0,
            bool isLengthInBytes = false,
            byte[] terminator = null)
        {
            return new Schema.Binary.FieldType
            {
                Name = name,
                TypeName = typeName,
                LengthField = lengthField,
                SwitchField = switchField,
                SwitchValue = switchValue,
                Length = length,
                IsLengthInBytes = isLengthInBytes,
                Terminator = terminator
            };
        }

        private static XmlQualifiedName UaType(string name)
        {
            return new XmlQualifiedName(name, OpcUaNamespace);
        }

        private static XmlQualifiedName BinaryType(string name)
        {
            return new XmlQualifiedName(name, BinarySchemaNamespace);
        }

        private static XmlQualifiedName BitType()
        {
            return new XmlQualifiedName("Bit", BinarySchemaNamespace);
        }

        private static XmlQualifiedName CustomType(string name)
        {
            return new XmlQualifiedName(name, CustomNamespace);
        }
    }
}
