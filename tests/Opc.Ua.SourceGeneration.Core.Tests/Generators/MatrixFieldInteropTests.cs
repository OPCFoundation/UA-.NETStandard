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
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// D-5: a generated data type with multi-dimensional fields has to be
    /// wire compatible with the DataTypeDefinition driven structure codec
    /// (<see cref="Encoders.Structure"/>, used by the complex type system and
    /// by any other stack) reading the StructureDefinition the type
    /// publishes: matrix fields are inline matrices (OPC 10000-6 5.2.5,
    /// 5.4.5), not Variants, and a field with a single ArrayDimensions entry
    /// is a plain array. Each direction is checked for null, empty and
    /// populated matrices in every encoding.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MatrixFieldInteropTests
    {
        private const string ModelUri = "http://test.org/UA/MX/";

        public enum Codec
        {
            Binary,
            JsonCompact,
            JsonVerbose,
            XmlDecoder,
            XmlParser
        }

        public enum Content
        {
            Null,
            Empty,
            Full
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_generated = DataTypeModelTests.Generate(Model);
            m_assembly = DataTypeModelTests.Compile(m_generated);
            m_gridsType = m_assembly.GetType("Test.MX.Grids", throwOnError: true)!;
            m_cellType = m_assembly.GetType("Test.MX.Cell", throwOnError: true)!;
            m_colorType = m_assembly.GetType("Test.MX.Color", throwOnError: true)!;
        }

        /// <summary>
        /// The generated class writes matrix fields through the raw
        /// WriteVariantValue / ReadVariantValue pair (no Variant framing) and
        /// a single dimension "matrix" as an array.
        /// </summary>
        [Test]
        public void GeneratedCodeUsesInlineMatrixCalls()
        {
            string code = m_generated
                .Where(f => f.Key.EndsWith("DataTypes.g.cs", StringComparison.Ordinal))
                .Select(f => f.Value)
                .Single();
            Assert.Multiple(() =>
            {
                Assert.That(code, Does.Contain(
                    "global::Opc.Ua.EncoderExtensions.WriteInlineMatrixValue(encoder, \"Doubles\", global::Opc.Ua.Variant.From(Doubles));"));
                Assert.That(code, Does.Contain(
                    "Doubles = decoder.ReadVariantValue(\"Doubles\", global::Opc.Ua.TypeInfo.Create(" +
                    "global::Opc.Ua.BuiltInType.Double, global::Opc.Ua.ValueRanks.TwoDimensions)).GetDoubleMatrix();"));
                Assert.That(code, Does.Contain(
                    "Cube = decoder.ReadVariantValue(\"Cube\", global::Opc.Ua.TypeInfo.Create(" +
                    "global::Opc.Ua.BuiltInType.Int32, 3)).GetInt32Matrix();"));
                Assert.That(code, Does.Contain(
                    "global::Opc.Ua.BuiltInType.Enumeration, global::Opc.Ua.ValueRanks.TwoDimensions))" +
                    ".GetEnumerationMatrix<global::Test.MX.Color>();"));
                Assert.That(code, Does.Contain(
                    "global::Opc.Ua.BuiltInType.UInt32, global::Opc.Ua.ValueRanks.TwoDimensions)).GetUInt32Matrix();"));
                Assert.That(code, Does.Contain("encoder.WriteEncodeableMatrix(\"Cells\", Cells);"));
                Assert.That(code, Does.Contain("encoder.WriteDoubleArray(\"Row\", Row);"));
                Assert.That(code, Does.Contain("Row = decoder.ReadDoubleArray(\"Row\");"));
                Assert.That(code, Does.Not.Match(@"encoder\.WriteVariant\(""(Doubles|Strings|Colors|Flags|Objects|Values|Cube)"""));
            });
            Assert.That(
                m_gridsType.GetProperty("Row")!.PropertyType,
                Is.EqualTo(typeof(ArrayOf<double>)),
                "a single ArrayDimensions entry is a one dimensional array");
        }

        /// <summary>
        /// The published StructureDefinition matches what Encode writes.
        /// </summary>
        [Test]
        public void StructureDefinitionDescribesTheFieldsAsWritten()
        {
            StructureDefinition definition = CreateDefinition();
            Dictionary<string, StructureField> fields = definition.Fields.ToArray()!
                .ToDictionary(f => f.Name!);
            Assert.Multiple(() =>
            {
                Assert.That(fields["Doubles"].ValueRank, Is.EqualTo(ValueRanks.TwoDimensions));
                Assert.That(fields["Cube"].ValueRank, Is.EqualTo(3));
                Assert.That(fields["Row"].ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(fields["Row"].ArrayDimensions.ToArray(), Is.EqualTo(s_rowDimensions));
            });
        }

        /// <summary>
        /// A StructureField ValueRank is -1 or &gt;= 1, never 0 (OPC 10000-3
        /// 8.51): a OneOrMoreDimensions field without ArrayDimensions is
        /// published (and read) as a two dimensional matrix of unknown
        /// lengths.
        /// </summary>
        [Test]
        public void UnrankedMatrixFieldIsPublishedAsTwoDimensions()
        {
            StructureDefinition definition = CreateDefinition("CreateUnranked");
            StructureField field = definition.Fields.ToArray()!.Single(f => f.Name == "M");
            Assert.That(field.ValueRank, Is.EqualTo(ValueRanks.TwoDimensions));
            Assert.That(field.ArrayDimensions.ToArray(), Is.EqualTo(s_unknownTwoDimensions));
            Assert.That(
                CreateDefinition().Fields.ToArray()!.Select(f => f.ValueRank),
                Has.None.EqualTo(ValueRanks.OneOrMoreDimensions));

            string code = m_generated
                .Where(f => f.Key.EndsWith("DataTypes.g.cs", StringComparison.Ordinal))
                .Select(f => f.Value)
                .Single();
            Assert.That(code, Does.Contain(
                "M = decoder.ReadVariantValue(\"M\", global::Opc.Ua.TypeInfo.Create(" +
                "global::Opc.Ua.BuiltInType.Int32, global::Opc.Ua.ValueRanks.TwoDimensions)).GetInt32Matrix();"));
        }

        /// <summary>
        /// The generated binary encoding is the inline matrix of OPC 10000-6
        /// 5.2.5 Table 28: Int32 dimensions (-1 for null) followed by the
        /// product of the dimensions values, without a length prefix.
        /// </summary>
        [TestCase(Content.Null)]
        [TestCase(Content.Empty)]
        [TestCase(Content.Full)]
        public void GeneratedBinaryIsTheInlineMatrix(Content content)
        {
            IEncodeable original = CreateGrids(content);
            ServiceMessageContext context = CreateGeneratedContext();
            byte[] encoded;
            using (var encoder = new BinaryEncoder(context))
            {
                original.Encode(encoder);
                encoded = encoder.CloseAndReturnBuffer()!;
            }

            byte[] expected;
            using (var e = new BinaryEncoder(context))
            {
                e.WriteInt32(null, 7);
                switch (content)
                {
                    case Content.Null:
                        for (int ii = 0; ii < 8; ii++)
                        {
                            e.WriteInt32(null, -1); // null dimensions, no values
                        }
                        e.WriteInt32(null, 0); // Row: the generated default is empty
                        break;
                    case Content.Empty:
                        Dimensions(e, 0, 3); // Doubles
                        Dimensions(e, 0, 0); // Strings (MatrixOf.Empty)
                        Dimensions(e, 2, 0); // Colors
                        Dimensions(e, 1, 0); // Flags
                        Dimensions(e, 0, 0); // Objects
                        Dimensions(e, 0, 2); // Values
                        Dimensions(e, 0, 2); // Cells
                        Dimensions(e, 0, 0, 0); // Cube
                        e.WriteInt32(null, 0); // Row
                        break;
                    case Content.Full:
                        Dimensions(e, 2, 3);
                        foreach (double d in s_doubles)
                        {
                            e.WriteDouble(null, d);
                        }
                        Dimensions(e, 2, 2);
                        e.WriteString(null, "a");
                        e.WriteString(null, null);
                        e.WriteString(null, "c");
                        e.WriteString(null, "d");
                        Dimensions(e, 2, 2);
                        foreach (int c in s_colors)
                        {
                            e.WriteInt32(null, c);
                        }
                        Dimensions(e, 2, 2);
                        foreach (uint f in s_flags)
                        {
                            e.WriteUInt32(null, f);
                        }
                        Dimensions(e, 1, 2);
                        e.WriteExtensionObject(null, new ExtensionObject(Cell(11)));
                        e.WriteExtensionObject(null, ExtensionObject.Null);
                        Dimensions(e, 2, 1);
                        e.WriteVariant(null, Variant.From(1));
                        e.WriteVariant(null, Variant.From("x"));
                        Dimensions(e, 1, 2);
                        e.WriteInt32(null, 1); // Cell.V
                        e.WriteInt32(null, 2);
                        Dimensions(e, 2, 1, 2);
                        foreach (int c in s_cube)
                        {
                            e.WriteInt32(null, c);
                        }
                        e.WriteDoubleArray(null, s_row.ToArrayOf());
                        break;
                }
                e.WriteInt32(null, 9);
                expected = e.CloseAndReturnBuffer()!;
            }

            Assert.That(encoded, Is.EqualTo(expected));

            static void Dimensions(BinaryEncoder encoder, params int[] dimensions)
            {
                encoder.WriteInt32Array(null, dimensions.ToArrayOf());
            }
        }

        /// <summary>
        /// Generated Encode is read by the DataTypeDefinition driven codec,
        /// whose Encode is read back by the generated Decode, losslessly.
        /// </summary>
        [Test]
        [Combinatorial]
        public void GeneratedAndDefinitionDrivenCodecsInteroperate(
            [Values(Codec.Binary, Codec.JsonCompact, Codec.JsonVerbose)] Codec codec,
            [Values] Content content)
        {
            IEncodeable original = CreateGrids(content);
            ServiceMessageContext generatedContext = CreateGeneratedContext();
            ServiceMessageContext runtimeContext = CreateRuntimeContext(out ExpandedNodeId typeId);

            // generated -> definition driven codec
            object payload = Encode(codec, generatedContext, original, typeId);
            var structure = (IStructure)Decode(codec, runtimeContext, payload, typeId);
            Assert.That(structure, Is.InstanceOf<Encoders.Structure>());
            AssertDefinitionDrivenValues(structure, content);

            // definition driven codec -> generated
            object payload2 = Encode(codec, runtimeContext, (IEncodeable)structure, typeId);
            IEncodeable decoded = Decode(codec, generatedContext, payload2, typeId);
            Assert.That(decoded.GetType(), Is.EqualTo(m_gridsType));
            Assert.That(decoded.IsEqual(original), Is.True, "generated -> structure -> generated");

            if (codec == Codec.Binary)
            {
                Assert.That(payload2, Is.EqualTo(payload), "both codecs write the same bytes");
            }

            // generated round trip
            IEncodeable again = Decode(codec, generatedContext, Encode(codec, generatedContext, original, typeId), typeId);
            Assert.That(again.IsEqual(original), Is.True, "generated round trip");
        }

        /// <summary>
        /// The DataTypeDefinition driven codec writes every XML field as a
        /// Variant body (e.g. &lt;Before&gt;&lt;Int32&gt;7&lt;/Int32&gt;&lt;/Before&gt;),
        /// so a whole structure does not interoperate with generated code in
        /// XML (a separate, pre-existing difference). The matrix fields must
        /// however be written identically by both, and each side must round
        /// trip its own XML.
        /// </summary>
        [Test]
        [Combinatorial]
        public void XmlMatrixFieldsMatchDefinitionDrivenCodec(
            [Values(Codec.XmlDecoder, Codec.XmlParser)] Codec codec,
            [Values] Content content)
        {
            IEncodeable original = CreateGrids(content);
            ServiceMessageContext generatedContext = CreateGeneratedContext();
            ServiceMessageContext runtimeContext = CreateRuntimeContext(out ExpandedNodeId typeId);
            IEncodeable structure = Decode(
                Codec.Binary,
                runtimeContext,
                Encode(Codec.Binary, generatedContext, original, typeId),
                typeId);

            string generatedXml = (string)Encode(codec, generatedContext, original, typeId);
            string runtimeXml = (string)Encode(codec, runtimeContext, structure, typeId);

            XElement generatedRoot = XElement.Parse(generatedXml);
            XElement runtimeRoot = XElement.Parse(runtimeXml);
            XNamespace ns = ModelUri;
            foreach (string field in s_matrixFields)
            {
                XElement generatedField = generatedRoot.Element(ns + field)!;
                XElement runtimeField = runtimeRoot.Element(ns + field)!;
                Assert.That(generatedField, Is.Not.Null, field);
                Assert.That(
                    XNode.DeepEquals(generatedField, runtimeField),
                    Is.True,
                    field + ":\n" + generatedField + "\n" + runtimeField);
            }

            // The Dimensions of an XML Matrix must be greater than zero (OPC
            // 10000-6 5.3.1.17): an empty matrix field is written as null,
            // which is equivalent to an empty array (5.1.11).
            IEncodeable expected = original;
            IEncodeable expectedStructure = structure;
            if (content == Content.Empty)
            {
                expected = CreateGrids(Content.Null);
                Set(expected, "Row", ArrayOf.Empty<double>());
                expectedStructure = Decode(
                    Codec.Binary,
                    runtimeContext,
                    Encode(Codec.Binary, generatedContext, expected, typeId),
                    typeId);
            }

            IEncodeable decoded = Decode(codec, generatedContext, generatedXml, typeId);
            Assert.That(decoded.IsEqual(expected), Is.True, "generated round trip");
            IEncodeable decodedStructure = Decode(codec, runtimeContext, runtimeXml, typeId);
            Assert.That(decodedStructure.IsEqual(expectedStructure), Is.True, "definition driven round trip");
        }

        private void AssertDefinitionDrivenValues(IStructure structure, Content content)
        {
            Assert.That(structure["Before"].GetInt32(), Is.EqualTo(7));
            Assert.That(structure["After"].GetInt32(), Is.EqualTo(9));

            MatrixOf<double> doubles = structure["Doubles"].GetDoubleMatrix();
            MatrixOf<int> cube = structure["Cube"].GetInt32Matrix();
            ArrayOf<double> row = structure["Row"].GetDoubleArray();
            MatrixOf<int> colors = structure["Colors"].GetInt32Matrix();
            MatrixOf<uint> flags = structure["Flags"].GetUInt32Matrix();
            MatrixOf<Variant> values = structure["Values"].GetVariantMatrix();
            MatrixOf<ExtensionObject> objects = structure["Objects"].GetExtensionObjectMatrix();
            switch (content)
            {
                case Content.Null:
                    Assert.That(doubles.IsNull, Is.True);
                    Assert.That(cube.IsNull, Is.True);
                    Assert.That(colors.IsNull, Is.True);
                    Assert.That(flags.IsNull, Is.True);
                    Assert.That(values.IsNull, Is.True);
                    Assert.That(objects.IsNull, Is.True);
                    break;
                case Content.Empty:
                    Assert.That(doubles.IsNull, Is.False);
                    Assert.That(doubles.Dimensions, Is.EqualTo(s_emptyRows));
                    Assert.That(cube.Dimensions, Is.EqualTo(s_emptyCube));
                    Assert.That(row.IsNull, Is.False);
                    Assert.That(row.Count, Is.Zero);
                    Assert.That(colors.Dimensions, Is.EqualTo(s_emptyColumns));
                    Assert.That(values.Count, Is.Zero);
                    Assert.That(objects.Count, Is.Zero);
                    break;
                case Content.Full:
                    Assert.That(doubles.Dimensions, Is.EqualTo(s_twoByThree));
                    Assert.That(doubles.Span.ToArray(), Is.EqualTo(s_doubles));
                    Assert.That(cube.Dimensions, Is.EqualTo(s_cubeDimensions));
                    Assert.That(cube.Span.ToArray(), Is.EqualTo(s_cube));
                    Assert.That(row.ToArray(), Is.EqualTo(s_row));
                    Assert.That(colors.Span.ToArray(), Is.EqualTo(s_colors));
                    Assert.That(flags.Span.ToArray(), Is.EqualTo(s_flags));
                    Assert.That(values.Span[1].GetString(), Is.EqualTo("x"));
                    Assert.That(objects.Dimensions, Is.EqualTo(s_oneByTwo));
                    break;
            }
        }

        private IEncodeable CreateGrids(Content content)
        {
            var grids = (IEncodeable)Activator.CreateInstance(m_gridsType)!;
            Set(grids!, "Before", 7);
            Set(grids, "After", 9);
            switch (content)
            {
                case Content.Null:
                    break;
                case Content.Empty:
                    Set(grids, "Doubles", MatrixOf.From<double>(new double[0, 3]));
                    Set(grids, "Strings", MatrixOf<string>.Empty);
                    Set(grids, "Colors", MatrixFrom(m_colorType, Array.CreateInstance(m_colorType, 2, 0)));
                    Set(grids, "Flags", MatrixOf.From<uint>(new uint[1, 0]));
                    Set(grids, "Objects", MatrixOf.From<ExtensionObject>(new ExtensionObject[0, 0]));
                    Set(grids, "Values", MatrixOf.From<Variant>(new Variant[0, 2]));
                    Set(grids, "Cells", MatrixFrom(m_cellType, Array.CreateInstance(m_cellType, 0, 2)));
                    Set(grids, "Cube", MatrixOf.From<int>(new int[0, 0, 0]));
                    Set(grids, "Row", ArrayOf.Empty<double>());
                    break;
                case Content.Full:
                    Set(grids, "Doubles", MatrixOf.From<double>(new double[,] { { 1.5, 2.5, 3.5 }, { 4.5, 5.5, 6.5 } }));
                    Set(grids, "Strings", MatrixOf.From<string>(new string[,] { { "a", null! }, { "c", "d" } }));
                    Array colors = Array.CreateInstance(m_colorType, 2, 2);
                    colors.SetValue(Enum.ToObject(m_colorType, 0), 0, 0);
                    colors.SetValue(Enum.ToObject(m_colorType, 1), 0, 1);
                    colors.SetValue(Enum.ToObject(m_colorType, 2), 1, 0);
                    colors.SetValue(Enum.ToObject(m_colorType, 1), 1, 1);
                    Set(grids, "Colors", MatrixFrom(m_colorType, colors));
                    Set(grids, "Flags", MatrixOf.From<uint>(new uint[,] { { 1, 2 }, { 3, 0 } }));
                    Set(grids, "Objects", MatrixOf.From<ExtensionObject>(new ExtensionObject[,]
                    {
                        { new ExtensionObject(Cell(11)), ExtensionObject.Null }
                    }));
                    Set(grids, "Values", MatrixOf.From<Variant>(new Variant[,]
                    {
                        { Variant.From(1) },
                        { Variant.From("x") }
                    }));
                    Array cells = Array.CreateInstance(m_cellType, 1, 2);
                    cells.SetValue(Cell(1), 0, 0);
                    cells.SetValue(Cell(2), 0, 1);
                    Set(grids, "Cells", MatrixFrom(m_cellType, cells));
                    Set(grids, "Cube", MatrixOf.From<int>(new int[,,] { { { 1, 2 } }, { { 3, 4 } } }));
                    Set(grids, "Row", new double[] { 1, 2, 3, 4, 5 }.ToArrayOf());
                    break;
            }
            return grids;
        }

        private IEncodeable Cell(int value)
        {
            var cell = (IEncodeable)Activator.CreateInstance(m_cellType)!;
            Set(cell!, "V", value);
            return cell;
        }

        private static object MatrixFrom(Type elementType, Array array)
        {
            return typeof(MatrixOf)
                .GetMethod(nameof(MatrixOf.From), BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(elementType)
                .Invoke(null, [array])!;
        }

        private static void Set(object instance, string name, object value)
        {
            instance.GetType().GetProperty(name)!.SetValue(instance, value);
        }

        private StructureDefinition CreateDefinition(string factory = "CreateGrids")
        {
            MethodInfo create = m_assembly.GetTypes()
                .Select(t => t.GetMethod(factory, BindingFlags.Public | BindingFlags.Static))
                .First(m => m != null)!;
            return (StructureDefinition)create!.Invoke(null, [CreateNamespaceTable()])!;
        }

        private static NamespaceTable CreateNamespaceTable()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(ModelUri);
            return namespaceUris;
        }

        private ServiceMessageContext CreateGeneratedContext()
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris = CreateNamespaceTable();
            context.Factory.Builder.AddEncodeableTypes(m_assembly).Commit();
            return context;
        }

        /// <summary>
        /// A context where Grids is the DataTypeDefinition driven structure,
        /// built the way the complex type system builds it: the built-in type
        /// of every field is what the field's DataType resolves to.
        /// </summary>
        private ServiceMessageContext CreateRuntimeContext(out ExpandedNodeId typeId)
        {
            var template = (IEncodeable)Activator.CreateInstance(m_gridsType)!;
            typeId = template!.TypeId;
            var fieldTypes = new Dictionary<string, BuiltInType>
            {
                ["Before"] = BuiltInType.Int32,
                ["Doubles"] = BuiltInType.Double,
                ["Strings"] = BuiltInType.String,
                ["Colors"] = BuiltInType.Enumeration,
                ["Flags"] = BuiltInType.UInt32,
                ["Objects"] = BuiltInType.ExtensionObject,
                ["Values"] = BuiltInType.Variant,
                ["Cells"] = BuiltInType.Null,
                ["Cube"] = BuiltInType.Int32,
                ["Row"] = BuiltInType.Double,
                ["After"] = BuiltInType.Int32
            };
            var structure = new Encoders.Structure(
                new XmlQualifiedName("Grids", ModelUri),
                template.TypeId,
                template.BinaryEncodingId,
                template.XmlEncodingId,
                CreateDefinition(),
                fieldTypes);

            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris = CreateNamespaceTable();
            context.Factory.Builder
                .AddEncodeableType(m_cellType)
                .AddEncodeableType(structure)
                .Commit();
            return context;
        }

        private static object Encode(Codec codec, ServiceMessageContext context, IEncodeable value, ExpandedNodeId typeId)
        {
            switch (codec)
            {
                case Codec.Binary:
                {
                    using var encoder = new BinaryEncoder(context);
                    encoder.WriteEncodeable("Grids", value, typeId);
                    return encoder.CloseAndReturnBuffer()!;
                }
                case Codec.JsonCompact:
                case Codec.JsonVerbose:
                {
                    using var encoder = new JsonEncoder(
                        context,
                        codec == Codec.JsonCompact ? JsonEncoderOptions.Compact : JsonEncoderOptions.Verbose);
                    encoder.WriteEncodeable("Grids", value, typeId);
                    return encoder.CloseAndReturnText();
                }
                default:
                {
                    using var encoder = new XmlEncoder(context);
                    encoder.PushNamespace(ModelUri);
                    encoder.WriteEncodeable("Grids", value, typeId);
                    encoder.PopNamespace();
                    return encoder.CloseAndReturnText()!;
                }
            }
        }

        private static IEncodeable Decode(
            Codec codec,
            ServiceMessageContext context,
            object payload,
            ExpandedNodeId typeId)
        {
            switch (codec)
            {
                case Codec.Binary:
                {
                    using var decoder = new BinaryDecoder((byte[])payload, context);
                    return decoder.ReadEncodeable<IEncodeable>("Grids", typeId);
                }
                case Codec.JsonCompact:
                case Codec.JsonVerbose:
                {
                    using var decoder = new JsonDecoder((string)payload, context);
                    return decoder.ReadEncodeable<IEncodeable>("Grids", typeId);
                }
                case Codec.XmlParser:
                {
                    using var parser = new XmlParser((string)payload, context);
                    parser.PushNamespace(ModelUri);
                    IEncodeable value = parser.ReadEncodeable<IEncodeable>("Grids", typeId);
                    parser.PopNamespace();
                    return value;
                }
                default:
                {
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes((string)payload));
                    using var reader = XmlReader.Create(stream, CoreUtils.DefaultXmlReaderSettings());
                    using var decoder = new XmlDecoder(reader, context);
                    decoder.PushNamespace(ModelUri);
                    IEncodeable value = decoder.ReadEncodeable<IEncodeable>("Grids", typeId);
                    decoder.PopNamespace();
                    return value;
                }
            }
        }

        private const string Model =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns="http://test.org/UA/MX/"
              TargetNamespace="http://test.org/UA/MX/">
              <opc:Namespaces>
                <opc:Namespace Name="MX" Prefix="Test.MX">http://test.org/UA/MX/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="Color" BaseType="ua:Enumeration">
                <opc:Fields>
                  <opc:Field Name="Red" Identifier="0" />
                  <opc:Field Name="Green" Identifier="1" />
                  <opc:Field Name="Blue" Identifier="2" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Flags" BaseType="ua:UInt32" IsOptionSet="true">
                <opc:Fields>
                  <opc:Field Name="Low" BitMask="00000001" />
                  <opc:Field Name="High" BitMask="00000002" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Cell" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="V" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Grids" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Before" DataType="ua:Int32" />
                  <opc:Field Name="Doubles" DataType="ua:Double" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Strings" DataType="ua:String" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Colors" DataType="Color" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Flags" DataType="Flags" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Objects" DataType="ua:Structure" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Values" DataType="ua:BaseDataType" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Cells" DataType="Cell" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0" />
                  <opc:Field Name="Cube" DataType="ua:Int32" ValueRank="OneOrMoreDimensions" ArrayDimensions="0,0,0" />
                  <opc:Field Name="Row" DataType="ua:Double" ValueRank="OneOrMoreDimensions" ArrayDimensions="5" />
                  <opc:Field Name="After" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Unranked" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="M" DataType="ua:Int32" ValueRank="OneOrMoreDimensions" />
                  <opc:Field Name="Tail" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;

        private static readonly string[] s_matrixFields =
            ["Doubles", "Strings", "Colors", "Flags", "Objects", "Values", "Cells", "Cube"];
        private static readonly uint[] s_rowDimensions = [5];
        private static readonly uint[] s_unknownTwoDimensions = [0, 0];
        private static readonly int[] s_emptyRows = [0, 3];
        private static readonly int[] s_emptyCube = [0, 0, 0];
        private static readonly int[] s_emptyColumns = [2, 0];
        private static readonly int[] s_twoByThree = [2, 3];
        private static readonly int[] s_cubeDimensions = [2, 1, 2];
        private static readonly int[] s_oneByTwo = [1, 2];
        private static readonly double[] s_doubles = [1.5, 2.5, 3.5, 4.5, 5.5, 6.5];
        private static readonly int[] s_cube = [1, 2, 3, 4];
        private static readonly double[] s_row = [1, 2, 3, 4, 5];
        private static readonly int[] s_colors = [0, 1, 2, 1];
        private static readonly uint[] s_flags = [1, 2, 3, 0];
        private Dictionary<string, string> m_generated;
        private Assembly m_assembly;
        private Type m_gridsType;
        private Type m_cellType;
        private Type m_colorType;
    }
}
