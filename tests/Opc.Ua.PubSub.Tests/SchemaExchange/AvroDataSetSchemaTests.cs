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
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Encoding.Tests
{
    /// <summary>
    /// Verifies deriving an Avro schema from a <see cref="DataSetMetaDataType"/> alone, with no
    /// AddressSpace (§6.7).
    /// </summary>
    [TestFixture]
    public sealed class AvroDataSetSchemaTests
    {
        private static FieldMetaData Field(
            string name,
            BuiltInType builtInType,
            int valueRank = ValueRanks.Scalar,
            NodeId dataType = default)
        {
            return new FieldMetaData
            {
                Name = name,
                BuiltInType = (byte)builtInType,
                ValueRank = valueRank,
                DataType = dataType.IsNull ? new NodeId((uint)builtInType) : dataType
            };
        }

        [Test]
        public void DerivesAParseableSchemaFromMetaDataAlone()
        {
            var metaData = new DataSetMetaDataType
            {
                Name = "Boiler",
                Fields =
                [
                    Field("Temperature", BuiltInType.Double),
                    Field("Serial", BuiltInType.String)
                ]
            };

            string schema = AvroDataSetSchema.Create(metaData, DataSetFieldContentMask.RawData, out ByteString id);

            Assert.DoesNotThrow(() => AvroParsingCanonicalForm.Compute(schema));
            Assert.That(schema, Does.Contain("\"name\":\"Boiler\""));
            Assert.That(schema, Does.Contain("\"name\":\"Temperature\""));
            Assert.That(id.Span.Length, Is.EqualTo(8), "the SchemaId is the 8 Rabin fingerprint bytes");
        }

        [Test]
        public void FieldOrderAndCountFollowTheMetaData()
        {
            var metaData = new DataSetMetaDataType
            {
                Name = "Ordered",
                Fields = [Field("B", BuiltInType.Int32), Field("A", BuiltInType.Int32)]
            };

            string schema = AvroDataSetSchema.Create(metaData, DataSetFieldContentMask.RawData);

            // §6.2 step 3 applies unchanged to this path: never sorted, never omitted.
            int bIndex = schema.IndexOf("\"name\":\"B\"", StringComparison.Ordinal);
            int aIndex = schema.IndexOf("\"name\":\"A\"", StringComparison.Ordinal);
            Assert.That(bIndex, Is.GreaterThan(-1));
            Assert.That(aIndex, Is.GreaterThan(bIndex), "declaration order is preserved");
        }

        [Test]
        public void AnnotationsDoNotChangeTheSchema()
        {
            var plain = new DataSetMetaDataType
            {
                Name = "Annotated",
                Fields = [Field("Value", BuiltInType.Int32)]
            };
            var annotated = new DataSetMetaDataType
            {
                Name = "Annotated",
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Value",
                        BuiltInType = (byte)BuiltInType.Int32,
                        ValueRank = ValueRanks.Scalar,
                        DataType = new NodeId((uint)BuiltInType.Int32),
                        // §6.7: these constrain or annotate values but must not alter the Parsing
                        // Canonical Form, otherwise two publishers of the same DataSet would
                        // disagree on its SchemaId.
                        MaxStringLength = 128,
                        Description = new LocalizedText("en", "a description"),
                        DataSetFieldId = new Uuid(Guid.NewGuid())
                    }
                ]
            };

            Assert.That(
                AvroDataSetSchema.Create(annotated, DataSetFieldContentMask.RawData),
                Is.EqualTo(AvroDataSetSchema.Create(plain, DataSetFieldContentMask.RawData)));
        }

        [Test]
        public void ConfigurationVersionIsNotAnInputToTheSchema()
        {
            var first = new DataSetMetaDataType
            {
                Name = "Versioned",
                Fields = [Field("Value", BuiltInType.Int32)],
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 1 }
            };
            var second = new DataSetMetaDataType
            {
                Name = "Versioned",
                Fields = [Field("Value", BuiltInType.Int32)],
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 9, MinorVersion = 9 }
            };

            // §6.3: the SchemaId identifies the canonical form, explicitly not a ConfigurationVersion.
            Assert.That(
                AvroDataSetSchema.Create(second, DataSetFieldContentMask.RawData),
                Is.EqualTo(AvroDataSetSchema.Create(first, DataSetFieldContentMask.RawData)));
        }

        [Test]
        public void EnumerationsMapToTheNumericValue()
        {
            var enumType = new NodeId(4242u, 3);
            var metaData = new DataSetMetaDataType
            {
                Name = "WithEnum",
                Fields = [Field("Mode", BuiltInType.Int32, ValueRanks.Scalar, enumType)],
                EnumDataTypes =
                [
                    new EnumDescription
                    {
                        DataTypeId = enumType,
                        Name = new QualifiedName("ModeEnum", 3),
                        BuiltInType = (byte)BuiltInType.Int32
                    }
                ]
            };

            string schema = AvroDataSetSchema.Create(metaData, DataSetFieldContentMask.RawData);

            // §5.3: never a symbolic Avro enum, so an unknown value stays forward-compatible.
            Assert.That(schema, Does.Not.Contain("\"type\":\"enum\""));
            Assert.DoesNotThrow(() => AvroParsingCanonicalForm.Compute(schema));
        }

        [Test]
        public void StructuresAreExpandedIntoRecords()
        {
            var pointType = new NodeId(7001u, 2);
            var metaData = new DataSetMetaDataType
            {
                Name = "WithStruct",
                Fields = [Field("Location", BuiltInType.ExtensionObject, ValueRanks.Scalar, pointType)],
                StructureDataTypes =
                [
                    new StructureDescription
                    {
                        DataTypeId = pointType,
                        Name = new QualifiedName("Point", 2),
                        StructureDefinition = new StructureDefinition
                        {
                            StructureType = StructureType.Structure,
                            Fields =
                            [
                                new StructureField
                                    {
                                        Name = "X",
                                        DataType = new NodeId((uint)BuiltInType.Double),
                                        ValueRank = ValueRanks.Scalar
                                    },
                                new StructureField
                                    {
                                        Name = "Y",
                                        DataType = new NodeId((uint)BuiltInType.Double),
                                        ValueRank = ValueRanks.Scalar
                                    }
                            ]
                        }
                    }
                ]
            };

            string schema = AvroDataSetSchema.Create(metaData, DataSetFieldContentMask.RawData);

            Assert.That(schema, Does.Contain("\"name\":\"Point\""));
            Assert.That(schema, Does.Contain("\"name\":\"X\""));
            Assert.That(schema, Does.Contain("\"name\":\"Y\""));
            Assert.DoesNotThrow(() => AvroParsingCanonicalForm.Compute(schema));
        }

        [Test]
        public void OptionalStructureFieldsUseTheWrapperRecord()
        {
            var personType = new NodeId(7002u, 2);
            var metaData = new DataSetMetaDataType
            {
                Name = "WithOptional",
                Fields = [Field("Who", BuiltInType.ExtensionObject, ValueRanks.Scalar, personType)],
                StructureDataTypes =
                [
                    new StructureDescription
                    {
                        DataTypeId = personType,
                        Name = new QualifiedName("Person", 2),
                        StructureDefinition = new StructureDefinition
                        {
                            StructureType = StructureType.StructureWithOptionalFields,
                            Fields =
                            [
                                new StructureField
                                    {
                                        Name = "Name",
                                        DataType = new NodeId((uint)BuiltInType.String),
                                        ValueRank = ValueRanks.Scalar
                                    },
                                new StructureField
                                    {
                                        Name = "Email",
                                        DataType = new NodeId((uint)BuiltInType.String),
                                        ValueRank = ValueRanks.Scalar,
                                        IsOptional = true
                                    }
                            ]
                        }
                    }
                ]
            };

            string schema = AvroDataSetSchema.Create(metaData, DataSetFieldContentMask.RawData);

            // §5.6: absent and present-but-null must stay distinguishable, which is what the
            // wrapper record buys.
            Assert.That(schema, Does.Contain("Person_Email_Optional"));
            Assert.DoesNotThrow(() => AvroParsingCanonicalForm.Compute(schema));
        }

        [Test]
        public void UnionStructuresUseSwitchAndValue()
        {
            var unionType = new NodeId(7003u, 2);
            var metaData = new DataSetMetaDataType
            {
                Name = "WithUnion",
                Fields = [Field("Measure", BuiltInType.ExtensionObject, ValueRanks.Scalar, unionType)],
                StructureDataTypes =
                [
                    new StructureDescription
                    {
                        DataTypeId = unionType,
                        Name = new QualifiedName("Measurement", 2),
                        StructureDefinition = new StructureDefinition
                        {
                            StructureType = StructureType.Union,
                            Fields =
                            [
                                new StructureField
                                    {
                                        Name = "AsInt",
                                        DataType = new NodeId((uint)BuiltInType.Int32),
                                        ValueRank = ValueRanks.Scalar
                                    },
                                new StructureField
                                    {
                                        Name = "AsText",
                                        DataType = new NodeId((uint)BuiltInType.String),
                                        ValueRank = ValueRanks.Scalar
                                    }
                            ]
                        }
                    }
                ]
            };

            string schema = AvroDataSetSchema.Create(metaData, DataSetFieldContentMask.RawData);

            Assert.That(schema, Does.Contain("\"name\":\"switch\""));
            Assert.That(schema, Does.Contain("Measurement_AsInt_Branch"));
            Assert.That(schema, Does.Contain("Measurement_AsText_Branch"));
            Assert.DoesNotThrow(() => AvroParsingCanonicalForm.Compute(schema));
        }

        [Test]
        public void AnUndeclaredCustomDataTypeFailsRatherThanGuessing()
        {
            var metaData = new DataSetMetaDataType
            {
                Name = "Incomplete",
                Fields =
                [
                    // Declares a custom structure but ships no StructureDataTypes entry for it.
                    Field("Mystery", BuiltInType.ExtensionObject, ValueRanks.Scalar, new NodeId(9999u, 4))
                ]
            };

            // §6.7: substituting an opaque type would produce a schema that looks correct while
            // silently losing the structure, so generation must fail instead.
            Assert.That(
                () => AvroDataSetSchema.Create(metaData, DataSetFieldContentMask.RawData),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void RecursiveStructuresTerminate()
        {
            var nodeType = new NodeId(7004u, 2);
            var metaData = new DataSetMetaDataType
            {
                Name = "Recursive",
                Fields = [Field("Root", BuiltInType.ExtensionObject, ValueRanks.Scalar, nodeType)],
                StructureDataTypes =
                [
                    new StructureDescription
                    {
                        DataTypeId = nodeType,
                        Name = new QualifiedName("TreeNode", 2),
                        StructureDefinition = new StructureDefinition
                        {
                            StructureType = StructureType.Structure,
                            Fields =
                            [
                                new StructureField
                                    {
                                        Name = "Label",
                                        DataType = new NodeId((uint)BuiltInType.String),
                                        ValueRank = ValueRanks.Scalar
                                    },
                                new StructureField
                                    {
                                        Name = "Child",
                                        DataType = nodeType,
                                        ValueRank = ValueRanks.Scalar
                                    }
                            ]
                        }
                    }
                ]
            };

            // A self-referencing structure must reference the enclosing record by name rather than
            // expand forever.
            string schema = AvroDataSetSchema.Create(metaData, DataSetFieldContentMask.RawData);
            Assert.DoesNotThrow(() => AvroParsingCanonicalForm.Compute(schema));
        }

        [Test]
        public void FramingFollowsTheDataSetFieldContentMask()
        {
            var metaData = new DataSetMetaDataType
            {
                Name = "Framed",
                Fields = [Field("Value", BuiltInType.Int32)]
            };

            string raw = AvroDataSetSchema.Create(metaData, DataSetFieldContentMask.RawData);
            string variant = AvroDataSetSchema.Create(metaData, DataSetFieldContentMask.None);
            string dataValue = AvroDataSetSchema.Create(metaData, DataSetFieldContentMask.StatusCode);

            // §8.2: the framing is not part of the metadata, so the same DataSet yields three
            // different schemas depending on how the writer frames its fields.
            Assert.That(raw, Does.Not.Contain("\"name\":\"builtInType\""));
            Assert.That(variant, Does.Contain("\"name\":\"builtInType\""));
            Assert.That(dataValue, Does.Contain("\"name\":\"sourceTimestamp\""));
        }
    }
}
