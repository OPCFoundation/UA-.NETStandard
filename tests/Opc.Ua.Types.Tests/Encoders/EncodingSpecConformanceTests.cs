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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Encoders;
using Opc.Ua.Tests;
using DelegateEncodeable = Opc.Ua.Types.Tests.Encoders.InlineMatrixDeclaredRankTests.DelegateEncodeable;
using Pair = Opc.Ua.Types.Tests.Encoders.InlineMatrixFieldTests.Pair;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Regression tests for OPC 10000-6 conformance of the runtime codecs:
    /// XML matrix structure fields (5.3.4, 5.3.1.17), empty matrix Variants
    /// (5.2.2.16), null DiagnosticInfo fields (5.2.1, 5.2.2.12), union
    /// SwitchField and EncodingMask validation (5.2.7, 5.2.8, 5.3.6, 5.3.7)
    /// and field omission in the JSON encodings (5.4.1).
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EncodingSpecConformanceTests
    {
        private static readonly int[] s_twoByThree = [2, 3];
        private static readonly int[] s_twoByTwo = [2, 2];
        private static readonly string[] s_strings = ["a", "b"];
        private static readonly string[] s_stringElements = ["String", "String"];
        private static readonly string[] s_matrixChildren = ["Dimensions", "Elements"];
        private static readonly double[] s_sixDoubles = [1, 2, 3, 4, 5, 6];
        private static readonly TypeInfo s_doubleMatrix =
            TypeInfo.Create(BuiltInType.Double, ValueRanks.TwoDimensions);

        /// <summary>
        /// A matrix structure field is of the Matrix type (OPC 10000-6
        /// 5.3.4): the field element directly contains Dimensions and
        /// Elements, without a Matrix wrapper element, and reads back.
        /// </summary>
        [Test]
        public void XmlMatrixFieldIsOfMatrixType([Values] bool useParser)
        {
            Variant value = Variant.From(s_sixDoubles.ToArrayOf().ToMatrix(2, 3));
            string xml = EncodeXml(e => e.WriteInlineMatrixValue("M", value));

            System.Xml.XmlElement field = FindField(xml, "M");
            string[] children = [.. field.ChildNodes.OfType<System.Xml.XmlElement>().Select(e => e.LocalName)];
            Assert.That(children, Is.EqualTo(s_matrixChildren), xml);

            Variant decoded = DecodeXml(xml, d => d.ReadVariantValue("M", s_doubleMatrix), useParser);
            Assert.That(decoded.GetDoubleMatrix().Dimensions, Is.EqualTo(s_twoByThree));
            Assert.That(decoded.GetDoubleMatrix().Span.ToArray(), Is.EqualTo(s_sixDoubles));
        }

        /// <summary>
        /// Matrix fields of Variant and ExtensionObject have the same shape.
        /// </summary>
        [Test]
        public void XmlVariantMatrixFieldIsOfMatrixType([Values] bool useParser)
        {
            Variant value = Variant.From(new Variant[]
            {
                Variant.From(1), Variant.From("a"), Variant.From(2.5), Variant.From(true)
            }.ToArrayOf().ToMatrix(2, 2));
            var type = TypeInfo.Create(BuiltInType.Variant, ValueRanks.TwoDimensions);
            string xml = EncodeXml(e => e.WriteInlineMatrixValue("M", value));

            System.Xml.XmlElement field = FindField(xml, "M");
            Assert.That(field.FirstChild.LocalName, Is.EqualTo("Dimensions"), xml);

            Variant decoded = DecodeXml(xml, d => d.ReadVariantValue("M", type), useParser);
            MatrixOf<Variant> matrix = decoded.GetVariantMatrix();
            Assert.That(matrix.Dimensions, Is.EqualTo(s_twoByTwo));
            Assert.That(matrix.Span[1].GetString(), Is.EqualTo("a"));
        }

        /// <summary>
        /// The Matrix wrapper earlier versions wrote around the content of a
        /// matrix structure field is still accepted.
        /// </summary>
        [Test]
        public void XmlLegacyWrappedMatrixFieldIsAccepted([Values] bool useParser)
        {
            string xml =
                "<Root xmlns=\"" + Namespaces.OpcUaXsd + "\"><A>7</A>" +
                "<M><Matrix><Dimensions><Int32>2</Int32><Int32>3</Int32></Dimensions>" +
                "<Elements><Double>1</Double><Double>2</Double><Double>3</Double>" +
                "<Double>4</Double><Double>5</Double><Double>6</Double></Elements>" +
                "</Matrix></M><B>9</B></Root>";

            Variant decoded = DecodeXml(xml, d => d.ReadVariantValue("M", s_doubleMatrix), useParser);
            Assert.That(decoded.GetDoubleMatrix().Dimensions, Is.EqualTo(s_twoByThree));
            Assert.That(decoded.GetDoubleMatrix().Span.ToArray(), Is.EqualTo(s_sixDoubles));
        }

        /// <summary>
        /// The Dimensions of an XML Matrix must be greater than zero
        /// (5.3.1.17): an empty matrix field is written as a nil field (null
        /// and empty are equivalent, 5.1.11) and reads back as null.
        /// </summary>
        [Test]
        public void XmlEmptyMatrixFieldIsWrittenAsNull([Values] bool useParser)
        {
            string xml = EncodeXml(e => e.WriteInlineMatrixValue("M", Variant.From(MatrixOf<double>.Empty)));

            System.Xml.XmlElement field = FindField(xml, "M");
            Assert.That(field.GetAttribute("nil", Namespaces.XmlSchemaInstance), Is.EqualTo("true"), xml);
            Assert.That(field.HasChildNodes, Is.False, xml);

            Variant decoded = DecodeXml(xml, d => d.ReadVariantValue("M", s_doubleMatrix), useParser);
            Assert.That(decoded.GetDoubleMatrix().IsNull, Is.True);
        }

        /// <summary>
        /// An empty encodeable matrix field is written as a nil field too.
        /// </summary>
        [Test]
        public void XmlEmptyEncodeableMatrixFieldIsWrittenAsNull([Values] bool useParser)
        {
            MatrixOf<Pair> empty = Array.Empty<Pair>().ToArrayOf().ToMatrix(0, 2);
            string xml = EncodeXml(e => e.WriteEncodeableMatrix("M", empty));

            System.Xml.XmlElement field = FindField(xml, "M");
            Assert.That(field.GetAttribute("nil", Namespaces.XmlSchemaInstance), Is.EqualTo("true"), xml);

            MatrixOf<Pair> decoded = default;
            DecodeXml(xml, d =>
            {
                decoded = d.ReadEncodeableMatrix<Pair>("M");
                return Variant.Null;
            }, useParser);
            Assert.That(decoded.IsNull, Is.True);
        }

        /// <summary>
        /// A Variant holding an empty matrix is encoded as an empty array
        /// without ArrayDimensions (OPC 10000-6 5.2.2.16) instead of throwing.
        /// </summary>
        [Test]
        public void BinaryEmptyMatrixVariantIsEmptyArray()
        {
            ServiceMessageContext context = CreateContext();
            Variant value = Variant.From(Array.Empty<double>().ToArrayOf().ToMatrix(2, 0));
            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteVariant(null, value);
                buffer = encoder.CloseAndReturnBuffer();
            }

            // Double | Array bit, ArrayLength 0, no ArrayDimensions.
            Assert.That(buffer, Is.EqualTo(new byte[] { (byte)BuiltInType.Double | 0x80, 0, 0, 0, 0 }));

            using var decoder = new BinaryDecoder(buffer, context);
            Variant decoded = decoder.ReadVariant(null);
            Assert.That(decoded.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Double));
            Assert.That(decoded.GetDoubleArray().IsNull, Is.False);
            Assert.That(decoded.GetDoubleArray().Count, Is.Zero);
        }

        /// <summary>
        /// The JSON and XML Variant encodings write an empty matrix as an
        /// empty array too, and read it back.
        /// </summary>
        [Test]
        public void TextEmptyMatrixVariantIsEmptyArray([Values("Json", "Xml", "XmlParser")] string codec)
        {
            ServiceMessageContext context = CreateContext();
            Variant value = Variant.From(Array.Empty<int>().ToArrayOf().ToMatrix(0, 3));
            Variant decoded;
            if (codec == "Json")
            {
                string json;
                using (var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose))
                {
                    encoder.WriteVariant("V", value);
                    json = encoder.CloseAndReturnText();
                }
                Assert.That(json, Does.Not.Contain("Dimensions"), json);
                using var decoder = new JsonDecoder(json, context);
                decoded = decoder.ReadVariant("V");
            }
            else
            {
                string xml = EncodeXml(e => e.WriteVariant("V", value));
                Assert.That(xml, Does.Contain("ListOfInt32"), xml);
                Assert.That(xml, Does.Not.Contain("Dimensions"), xml);
                decoded = DecodeXml(xml, d => d.ReadVariant("V"), codec == "XmlParser");
            }

            Assert.That(decoded.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(decoded.GetInt32Array().IsNull, Is.False);
            Assert.That(decoded.GetInt32Array().Count, Is.Zero);
        }

        /// <summary>
        /// A null DiagnosticInfo field of a structure is written with its null
        /// encoding, the encoding mask 0x00 (OPC 10000-6 5.2.1, 5.2.2.12),
        /// so the fields after it stay in sync.
        /// </summary>
        [Test]
        public void BinaryNullDiagnosticInfoFieldIsWrittenAsNull()
        {
            ServiceMessageContext context = CreateContext();
            Structure structure = CreateStructure(
                StructureType.Structure,
                ("Diagnostics", DataTypeIds.DiagnosticInfo, BuiltInType.DiagnosticInfo, false),
                ("Count", DataTypeIds.Int32, BuiltInType.Int32, false));
            structure["Count"] = Variant.From(0x11223344);

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                structure.Encode(encoder);
                buffer = encoder.CloseAndReturnBuffer();
            }
            Assert.That(buffer, Is.EqualTo(new byte[] { 0x00, 0x44, 0x33, 0x22, 0x11 }));

            Structure decoded = (Structure)structure.CreateInstance();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }
            Assert.That(decoded["Count"].GetInt32(), Is.EqualTo(0x11223344));

            // A peer's non null DiagnosticInfo is consumed as a whole.
            byte[] withDiagnostics = [0x01, 0x05, 0x00, 0x00, 0x00, 0x44, 0x33, 0x22, 0x11];
            using (var decoder = new BinaryDecoder(withDiagnostics, context))
            {
                decoded.Decode(decoder);
            }
            Assert.That(decoded["Count"].GetInt32(), Is.EqualTo(0x11223344));
        }

        /// <summary>
        /// Decoders report an error for a SwitchField greater than the number
        /// of union fields (OPC 10000-6 5.2.8, 5.3.7, 5.4.8).
        /// </summary>
        [Test]
        public void UnionSwitchFieldBeyondFieldCountIsRejected(
            [Values("Binary", "Json", "Xml", "XmlParser")] string codec)
        {
            ServiceMessageContext context = CreateContext();
            var union = (Opc.Ua.Encoders.Union)CreateStructure(
                StructureType.Union,
                ("A", DataTypeIds.Int32, BuiltInType.Int32, false),
                ("B", DataTypeIds.String, BuiltInType.String, false));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
            {
                switch (codec)
                {
                    case "Binary":
                        using (var decoder = new BinaryDecoder(new byte[] { 3, 0, 0, 0, 1, 0, 0, 0 }, context))
                        {
                            union.Decode(decoder);
                        }
                        break;
                    case "Json":
                        using (var decoder = new JsonDecoder("{\"SwitchField\":3,\"A\":1}", context))
                        {
                            union.Decode(decoder);
                        }
                        break;
                    default:
                        DecodeXml(
                            "<Root xmlns=\"" + Namespaces.OpcUaXsd + "\">" +
                            "<SwitchField>3</SwitchField></Root>",
                            d =>
                            {
                                union.Decode(d);
                                return Variant.Null;
                            },
                            codec == "XmlParser",
                            readFrame: false);
                        break;
                }
            });
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));

            // The last field is still a valid selector.
            using (var decoder = new BinaryDecoder(new byte[] { 2, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF }, context))
            {
                union.Decode(decoder);
            }
            Assert.That(union.SwitchField, Is.EqualTo(2u));
        }

        /// <summary>
        /// Binary decoders report an error if EncodingMask bits that are not
        /// assigned to an optional field are set (OPC 10000-6 5.2.7); XML
        /// decoders ignore them (5.3.6).
        /// </summary>
        [Test]
        public void EncodingMaskWithUnassignedBitsIsRejectedInBinary([Values] bool useParser)
        {
            ServiceMessageContext context = CreateContext();
            var structure = (StructureWithOptionalFields)CreateStructure(
                StructureType.StructureWithOptionalFields,
                ("A", DataTypeIds.Int32, BuiltInType.Int32, true),
                ("B", DataTypeIds.Int32, BuiltInType.Int32, true));

            using (var decoder = new BinaryDecoder(new byte[] { 0x05, 0, 0, 0, 1, 0, 0, 0 }, context))
            {
                ServiceResultException ex = Assert.Throws<ServiceResultException>(
                    () => structure.Decode(decoder));
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            }

            using (var decoder = new BinaryDecoder(new byte[] { 0x01, 0, 0, 0, 1, 0, 0, 0 }, context))
            {
                structure.Decode(decoder);
            }
            Assert.That(structure.EncodingMask, Is.EqualTo(1u));

            DecodeXml(
                "<Root xmlns=\"" + Namespaces.OpcUaXsd + "\">" +
                "<EncodingMask>5</EncodingMask><A><Int32>1</Int32></A></Root>",
                d =>
                {
                    structure.Decode(d);
                    return Variant.Null;
                },
                useParser,
                readFrame: false);
            Assert.That(structure.EncodingMask, Is.EqualTo(1u));
            Assert.That(structure["A"].GetInt32(), Is.EqualTo(1));
        }

        /// <summary>
        /// Only the CompactEncoding may omit fields with a default value; the
        /// VerboseEncoding includes all fields (OPC 10000-6 5.4.1, 5.4.2.1).
        /// </summary>
        [Test]
        public void JsonCanOmitFieldsOnlyInCompactEncoding()
        {
            ServiceMessageContext context = CreateContext();
            using (var compact = new JsonEncoder(context, JsonEncoderOptions.Compact))
            {
                Assert.That(compact.CanOmitFields, Is.True);
            }
            using (var verbose = new JsonEncoder(context, JsonEncoderOptions.Verbose))
            {
                Assert.That(verbose.CanOmitFields, Is.False);
            }
            using (var rawData = new JsonEncoder(context, JsonEncoderOptions.RawData))
            {
                Assert.That(rawData.CanOmitFields, Is.False);
            }
            using var xml = new XmlEncoder(context);
            Assert.That(xml.CanOmitFields, Is.True);
        }

        /// <summary>
        /// A structure field is encoded like the typed field of its type
        /// (OPC 10000-6 5.3.1, 5.3.5): a scalar is the content of the field
        /// element and an array field contains the elements named by the
        /// type (5.3.4). The definition driven codec wrapped the value in a
        /// Variant body (&lt;A&gt;&lt;Int32&gt;, &lt;A&gt;&lt;ListOfInt32&gt;).
        /// </summary>
        [Test]
        public void XmlStructureFieldsAreTypedFields([Values] bool useParser)
        {
            Structure structure = CreateStructure(
                StructureType.Structure,
                ("I", DataTypeIds.Int32, BuiltInType.Int32, false),
                ("S", DataTypeIds.String, BuiltInType.String, false),
                ("N", DataTypeIds.NodeId, BuiltInType.NodeId, false));
            structure["I"] = Variant.From(5);
            structure["S"] = Variant.From("x");
            structure["N"] = Variant.From(new NodeId(85u));

            string xml = EncodeXml(e => e.WriteEncodeable("V", structure, structure.TypeId));

            Assert.That(xml, Does.Contain("<I>5</I>"), xml);
            Assert.That(xml, Does.Contain("<S>x</S>"), xml);
            Assert.That(xml, Does.Not.Contain("<Int32>5"), xml);
            Assert.That(xml, Does.Not.Contain("<NodeId>"), xml);

            var array = TypeInfo.Create(BuiltInType.String, ValueRanks.OneDimension);
            string arrayXml = EncodeXml(e => e.WriteVariantValue("F", Variant.From(s_strings.ToArrayOf())));
            System.Xml.XmlElement field = FindField(arrayXml, "F");
            Assert.That(
                field.ChildNodes.OfType<System.Xml.XmlElement>().Select(e => e.LocalName),
                Is.EqualTo(s_stringElements),
                arrayXml);
            Variant decoded = DecodeXml(arrayXml, d => d.ReadVariantValue("F", array), useParser);
            Assert.That(decoded.GetStringArray().ToArray(), Is.EqualTo(s_strings));
        }

        /// <summary>
        /// Every element of an XML enumeration array is written (OPC 10000-6
        /// 5.3.4); the value 0 was omitted like a default field, which
        /// dropped it from the array.
        /// </summary>
        [Test]
        public void XmlEnumerationArrayKeepsZeroElements([Values] bool useParser)
        {
            ArrayOf<ZeroEnum> typed = new[] { ZeroEnum.One, ZeroEnum.Zero, ZeroEnum.One }.ToArrayOf();
            string xml = EncodeXml(e => e.WriteEnumeratedArray("F", typed));
            Variant decoded = DecodeXml(
                xml,
                d => d.ReadVariantValue("F", TypeInfo.Create(BuiltInType.Enumeration, ValueRanks.OneDimension)),
                useParser);
            Assert.That(decoded.GetEnumerationArray().ToArray().Select(e => e.Value), Is.EqualTo(s_oneZeroOne), xml);

            ArrayOf<EnumValue> values = new[] { new EnumValue(0, "Zero"), new EnumValue(1) }.ToArrayOf();
            xml = EncodeXml(e => e.WriteVariantValue("F", Variant.From(values)));
            decoded = DecodeXml(
                xml,
                d => d.ReadVariantValue("F", TypeInfo.Create(BuiltInType.Enumeration, ValueRanks.OneDimension)),
                useParser);
            Assert.That(decoded.GetEnumerationArray().ToArray().Select(e => e.Value), Is.EqualTo(s_zeroOne), xml);
        }

        /// <summary>
        /// An enumeration with a 0 member.
        /// </summary>
        public enum ZeroEnum
        {
            /// <summary>Zero.</summary>
            Zero = 0,

            /// <summary>One.</summary>
            One = 1
        }

        private static readonly int[] s_oneZeroOne = [1, 0, 1];
        private static readonly int[] s_zeroOne = [0, 1];

        /// <summary>
        /// The Variant body earlier versions wrapped a structure field value
        /// in is still read, and an empty array field is an empty array.
        /// </summary>
        [TestCase("<F><Int32>5</Int32></F>", BuiltInType.Int32, ValueRanks.Scalar, "5")]
        [TestCase("<F>5</F>", BuiltInType.Int32, ValueRanks.Scalar, "5")]
        [TestCase("<F><String>a</String></F>", BuiltInType.String, ValueRanks.Scalar, "a")]
        [TestCase("<F><NodeId><Identifier>i=85</Identifier></NodeId></F>", BuiltInType.NodeId, ValueRanks.Scalar, "i=85")]
        [TestCase("<F><Identifier>i=85</Identifier></F>", BuiltInType.NodeId, ValueRanks.Scalar, "i=85")]
        [TestCase("<F><ListOfString><String>a</String><String>b</String></ListOfString></F>",
            BuiltInType.String, ValueRanks.OneDimension, "a,b")]
        [TestCase("<F><String>a</String><String>b</String></F>",
            BuiltInType.String, ValueRanks.OneDimension, "a,b")]
        [TestCase("<F><ListOfInt32><Int32>1</Int32></ListOfInt32></F>",
            BuiltInType.Enumeration, ValueRanks.OneDimension, "1")]
        [TestCase("<F><Int32>1</Int32></F>", BuiltInType.Enumeration, ValueRanks.Scalar, "1")]
        [TestCase("<F>Green_1</F>", BuiltInType.Enumeration, ValueRanks.Scalar, "1")]
        [TestCase("<F />", BuiltInType.Int32, ValueRanks.OneDimension, "")]
        public void XmlStructureFieldReadsTypedAndLegacyShape(
            string field,
            BuiltInType builtInType,
            int valueRank,
            string expected)
        {
            foreach (bool useParser in new[] { false, true })
            {
                string xml =
                    "<Root xmlns=\"" + Namespaces.OpcUaXsd + "\"><A>7</A>" + field + "<B>9</B></Root>";
                Variant decoded = DecodeXml(
                    xml,
                    d => d.ReadVariantValue("F", TypeInfo.Create(builtInType, valueRank)),
                    useParser);

                Assert.That(decoded.IsNull, Is.False, xml);
                string actual = valueRank == ValueRanks.Scalar
                    ? Format(decoded)
                    : decoded.TypeInfo.BuiltInType switch
                    {
                        BuiltInType.String => string.Join(",", decoded.GetStringArray().ToArray()),
                        BuiltInType.Int32 => string.Join(",", decoded.GetInt32Array().ToArray()),
                        _ => string.Join(",", decoded.GetEnumerationArray().ToArray().Select(e => e.Value))
                    };
                Assert.That(actual, Is.EqualTo(expected), xml);
            }

            static string Format(object value)
            {
                return value switch
                {
                    Variant v when v.TypeInfo.BuiltInType == BuiltInType.Enumeration =>
                        v.GetEnumeration().Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Variant v => Format(v.Raw),
                    EnumValue e => e.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    NodeId n => n.ToString(),
                    _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                };
            }
        }

        private static Structure CreateStructure(
            StructureType structureType,
            params (string Name, NodeId DataType, BuiltInType BuiltInType, bool IsOptional)[] fields)
        {
            var definition = new StructureDefinition
            {

                BaseDataType = DataTypeIds.Structure,

                StructureType = structureType,
                Fields =
                [
                    .. fields.Select(f => new StructureField
                    {
                        Name = f.Name,
                        DataType = f.DataType,
                        ValueRank = ValueRanks.Scalar,
                        IsOptional = f.IsOptional
                    })
                ]
            };
            var xmlName = new XmlQualifiedName("TestStructure", Namespaces.OpcUaXsd);
            var fieldTypes = fields.ToDictionary(f => f.Name, f => f.BuiltInType);
            return structureType switch
            {
                StructureType.Union => new Opc.Ua.Encoders.Union(
                    xmlName,
                    new ExpandedNodeId(77900u),
                    new ExpandedNodeId(77901u),
                    new ExpandedNodeId(77902u),
                    definition,
                    fieldTypes),
                StructureType.StructureWithOptionalFields => new StructureWithOptionalFields(
                    xmlName,
                    new ExpandedNodeId(77910u),
                    new ExpandedNodeId(77911u),
                    new ExpandedNodeId(77912u),
                    definition,
                    fieldTypes),
                _ => new Structure(
                    xmlName,
                    new ExpandedNodeId(77920u),
                    new ExpandedNodeId(77921u),
                    new ExpandedNodeId(77922u),
                    definition,
                    fieldTypes)
            };
        }

        /// <summary>
        /// Writes the fields A, the value and B in a Root element.
        /// </summary>
        private static string EncodeXml(Action<IEncoder> write)
        {
            using var encoder = new XmlEncoder(CreateContext());
            encoder.PushNamespace(Namespaces.OpcUaXsd);
            encoder.WriteEncodeable("Root", new DelegateEncodeable(e =>
            {
                e.WriteInt32("A", 7);
                write(e);
                e.WriteInt32("B", 9);
            }));
            encoder.PopNamespace();
            return encoder.CloseAndReturnText();
        }

        /// <summary>
        /// Reads the content of the Root element; with <paramref name="readFrame"/>
        /// the fields A and B around the value are checked.
        /// </summary>
        private static Variant DecodeXml(
            string xml,
            Func<IDecoder, Variant> read,
            bool useParser,
            bool readFrame = true)
        {
            ServiceMessageContext context = CreateContext();
            DelegateEncodeable.Reader = d =>
            {
                if (readFrame)
                {
                    Assert.That(d.ReadInt32("A"), Is.EqualTo(7));
                }
                Variant value = read(d);
                if (readFrame)
                {
                    Assert.That(d.ReadInt32("B"), Is.EqualTo(9), "the field after the value");
                }
                return value;
            };
            DelegateEncodeable.LastRead = default;
            if (useParser)
            {
                using var parser = new XmlParser(xml, context);
                parser.PushNamespace(Namespaces.OpcUaXsd);
                parser.ReadEncodeable<DelegateEncodeable>("Root");
                return DelegateEncodeable.LastRead;
            }
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            using var reader = XmlReader.Create(stream, CoreUtils.DefaultXmlReaderSettings());
            using var decoder = new XmlDecoder(reader, context);
            decoder.PushNamespace(Namespaces.OpcUaXsd);
            decoder.ReadEncodeable<DelegateEncodeable>("Root");
            return DelegateEncodeable.LastRead;
        }

        private static System.Xml.XmlElement FindField(string xml, string name)
        {
            var document = new XmlDocument();
            document.LoadInnerXml(xml);
            return document.DocumentElement.ChildNodes
                .OfType<System.Xml.XmlElement>()
                .Single(e => e.LocalName == name);
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }
    }
}
