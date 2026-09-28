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

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Whether a value is written as an inline matrix (OPC 10000-6 5.2.5
    /// Table 28) depends on the declared rank of the field, not on the
    /// storage of the value: a one dimensional MatrixOf is an array, a
    /// Variable value is a Variant, and only a field declared as a matrix
    /// is an inline matrix (normalized by WriteInlineMatrixValue).
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class InlineMatrixDeclaredRankTests
    {
        private const uint BadEncodingLimitsExceeded = 0x80080000;
        private const uint BadEncodingErrorCode = 0x80060000;
        private static readonly int[] s_zeroByZero = [0, 0];
        private static readonly int[] s_zeroByTwo = [0, 2];
        private static readonly int[] s_zeroByThree = [0, 3];
        private static readonly int[] s_twoByThree = [2, 3];
        private static readonly int[] s_fourFive = [4, 5];
        private static readonly double[] s_threeDoubles = [1.5, 2.5, 3.5];

        public enum Codec
        {
            Binary,
            Json,
            Xml
        }

        /// <summary>
        /// A one dimensional MatrixOf (including MatrixOf.Empty) in an array
        /// field is written as an array, as before inline matrices, and reads
        /// back as the array without desynchronizing the next field.
        /// </summary>
        [Test]
        [Combinatorial]
        public void OneDimensionalMatrixOfInArrayFieldIsAnArray(
            [Values] Codec codec,
            [Values(0, 3)] int count)
        {
            MatrixOf<double> matrix = count == 0
                ? MatrixOf<double>.Empty
                : s_threeDoubles.ToArrayOf().ToMatrix(3);

            Variant decoded = RoundTrip(
                codec,
                e => e.WriteVariantValue("M", Variant.From(matrix)),
                d => d.ReadVariantValue("M", TypeInfo.Create(BuiltInType.Double, ValueRanks.OneDimension)));

            Assert.That(decoded.GetDoubleArray().ToArray(), Is.EqualTo(matrix.Span.ToArray()));
        }

        /// <summary>
        /// The binary array encoding of an empty one dimensional MatrixOf is
        /// the length 0 (not the 0 x 0 inline matrix dimensions).
        /// </summary>
        [Test]
        public void BinaryEmptyMatrixOfInArrayFieldIsEmptyArray()
        {
            using var encoder = new BinaryEncoder(CreateContext());
            encoder.WriteVariantValue(null, Variant.From(MatrixOf<int>.Empty));
            encoder.WriteInt32(null, 0x11223344);

            Assert.That(encoder.CloseAndReturnBuffer(), Is.EqualTo(Int32s(0, 0x11223344)));
        }

        /// <summary>
        /// A field declared as a matrix is always a conformant inline matrix:
        /// an empty value with a single dimension becomes 0 x 0, a null
        /// array a null matrix, a non empty single dimension is rejected.
        /// </summary>
        [Test]
        [Combinatorial]
        public void MatrixFieldIsNormalizedToInlineMatrix([Values] Codec codec)
        {
            var type = TypeInfo.Create(BuiltInType.Double, ValueRanks.TwoDimensions);
            foreach (Variant value in new[]
            {
                Variant.From(MatrixOf<double>.Empty),
                Variant.From(ArrayOf<double>.Empty),
                Variant.From(Array.Empty<double>().ToArrayOf().ToMatrix(0, 0))
            })
            {
                Variant decoded = RoundTrip(
                    codec,
                    e => e.WriteInlineMatrixValue("M", value),
                    d => d.ReadVariantValue("M", type));
                MatrixOf<double> matrix = decoded.GetDoubleMatrix();
                if (codec == Codec.Xml)
                {
                    // XML Matrix dimensions must be > 0 (OPC 10000-6
                    // 5.3.1.17): an empty matrix field is written as null.
                    Assert.That(matrix.IsNull, Is.True);
                    continue;
                }
                Assert.That(matrix.IsNull, Is.False);
                Assert.That(matrix.Dimensions, Is.EqualTo(s_zeroByZero));
            }

            Variant nullArray = RoundTrip(
                codec,
                e => e.WriteInlineMatrixValue("M", Variant.From(default(ArrayOf<double>))),
                d => d.ReadVariantValue("M", type));
            Assert.That(nullArray.GetDoubleMatrix().IsNull, Is.True);

            Variant full = Variant.From(new double[] { 1, 2, 3, 4, 5, 6 }.ToArrayOf().ToMatrix(2, 3));
            Variant decodedFull = RoundTrip(
                codec,
                e => e.WriteInlineMatrixValue("M", full),
                d => d.ReadVariantValue("M", type));
            Assert.That(decodedFull.GetDoubleMatrix().Dimensions, Is.EqualTo(s_twoByThree));

            using var encoder = new BinaryEncoder(CreateContext());
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteInlineMatrixValue(
                    "M",
                    Variant.From(new double[] { 1, 2 }.ToArrayOf().ToMatrix(2))));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        /// <summary>
        /// The DataTypeDefinition driven structure codec writes an empty
        /// MatrixOf in a matrix field as 0 x 0 and a one dimensional MatrixOf
        /// in an array field as an array; a null scalar field is written as
        /// the default value, not as nothing.
        /// </summary>
        [Test]
        public void StructureFieldsAreWrittenByDeclaredRank()
        {
            ServiceMessageContext context = CreateContext();
            Structure structure = CreateStructure();
            structure["Name"] = Variant.Null;
            structure["Matrix"] = Variant.From(MatrixOf<double>.Empty);
            structure["Array"] = Variant.From(s_fourFive.ToArrayOf().ToMatrix(2));
            structure["Count"] = Variant.From(42);

            byte[] encoded;
            using (var encoder = new BinaryEncoder(context))
            {
                structure.Encode(encoder);
                encoded = encoder.CloseAndReturnBuffer();
            }

            Assert.That(encoded, Is.EqualTo(Int32s(-1, 2, 0, 0, 2, 4, 5, 42)));

            Structure decoded = CreateStructure();
            using (var decoder = new BinaryDecoder(encoded, context))
            {
                decoded.Decode(decoder);
            }
            Assert.That(decoded["Name"].GetString(), Is.Null);
            Assert.That(decoded["Matrix"].GetDoubleMatrix().Dimensions, Is.EqualTo(s_zeroByZero));
            Assert.That(decoded["Array"].GetInt32Array().ToArray(), Is.EqualTo(s_fourFive));
            Assert.That(decoded["Count"].GetInt32(), Is.EqualTo(42));
        }

        /// <summary>
        /// The raw XML value without a field name is the content of a
        /// Variant (a Variable value, a serialized Variant): a null matrix is
        /// a nil ListOf element, which loads as XML.
        /// </summary>
        [Test]
        public void XmlVariantContentOfNullMatrixIsValidXml()
        {
            using var encoder = new XmlEncoder(CreateContext());
            encoder.WriteVariantValue(null, Variant.From(default(MatrixOf<double>)));
            string xml = encoder.CloseAndReturnText();

            var document = new XmlDocument();
            document.LoadInnerXml(xml);
            Assert.That(document.DocumentElement.LocalName, Is.EqualTo("ListOfDouble"));
        }

        /// <summary>
        /// The DataContract surrogate of a Variant round trips null, empty
        /// and populated matrices.
        /// </summary>
        [Test]
        public void SerializableVariantRoundTripsMatrices()
        {
            ServiceMessageContext context = CreateContext();
            foreach (Variant value in MatrixValues())
            {
                var source = new SerializableVariant(value) { Context = context };
                System.Xml.XmlElement xml = source.XmlEncodedValue;
                Assert.That(xml, Is.Not.Null);

                var target = new SerializableVariant { Context = context };
                target.XmlEncodedValue = xml;
                AssertSameContent(value, target.Value);
            }
        }

        /// <summary>
        /// A Variable whose value is a null, empty or 2 x 3 matrix is
        /// exported to a NodeSet and imported again.
        /// </summary>
        [Test]
        public void NodeSetRoundTripsMatrixVariableValues()
        {
            SystemContext context = CreateSystemContext();
            var collection = new NodeStateCollection();
            Variant[] values = [.. MatrixValues()];
            for (int ii = 0; ii < values.Length; ii++)
            {
                collection.Add(new BaseDataVariableState(null)
                {
                    NodeId = new NodeId((uint)(7100 + ii), 1),
                    BrowseName = new QualifiedName("M" + ii, 1),
                    DisplayName = new LocalizedText("M" + ii),
                    DataType = DataTypeIds.Double,
                    ValueRank = ValueRanks.OneOrMoreDimensions,
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    Value = values[ii]
                });
            }

            using var stream = new MemoryStream();
            collection.SaveAsNodeSet2(context, stream);
            stream.Position = 0;
            Export.UANodeSet nodeSet = Export.UANodeSet.Read(stream);
            var imported = new NodeStateCollection();
            nodeSet.Import(context, imported);

            for (int ii = 0; ii < values.Length; ii++)
            {
                var variable = imported
                    .OfType<BaseVariableState>()
                    .Single(v => v.NodeId == new NodeId((uint)(7100 + ii), 1));
                AssertSameContent(values[ii], variable.Value);
            }
        }

        /// <summary>
        /// A copy of a null or empty ExtensionObject, DataValue or Variant
        /// matrix keeps its matrix identity: every encoding of the copy is
        /// the encoding of the original.
        /// </summary>
        [Test]
        [Combinatorial]
        public void CopyOfNullOrEmptyMatrixEncodesLikeTheOriginal(
            [Values] Codec codec,
            [Values(BuiltInType.ExtensionObject, BuiltInType.DataValue, BuiltInType.Variant)]
            BuiltInType builtInType,
            [Values(true, false)] bool isNull,
            [Values(true, false)] bool asMatrixField)
        {
            Variant value = builtInType switch
            {
                BuiltInType.ExtensionObject => isNull
                    ? Variant.From(default(MatrixOf<ExtensionObject>))
                    : Variant.From(MatrixOf<ExtensionObject>.Empty),
                BuiltInType.DataValue => isNull
                    ? Variant.From(default(MatrixOf<DataValue>))
                    : Variant.From(MatrixOf<DataValue>.Empty),
                _ => isNull
                    ? Variant.From(default(MatrixOf<Variant>))
                    : Variant.From(MatrixOf<Variant>.Empty)
            };
            Variant copy = value.Copy();

            Assert.That(
                Encode(codec, copy, asMatrixField),
                Is.EqualTo(Encode(codec, value, asMatrixField)));
        }

        /// <summary>
        /// The encoders enforce MaxArrayLength for inline matrices like for
        /// arrays, instead of leaving it to the peer's decoder.
        /// </summary>
        [Test]
        public void BinaryInlineMatrixBeyondMaxArrayLengthIsRejectedByTheEncoder()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxArrayLength = 100;
            using var encoder = new BinaryEncoder(context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteInlineMatrixValue(
                    "M",
                    Variant.From(new double[110].ToArrayOf().ToMatrix(11, 10))));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));

            ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteInlineMatrixValue(
                    "M",
                    Variant.From(new string[110].ToArrayOf().ToMatrix(11, 10))));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));

            InlineMatrixFieldTests.Pair[] pairs = [.. Enumerable.Range(0, 110)
                .Select(i => new InlineMatrixFieldTests.Pair { X = i })];
            ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteEncodeableMatrix("M", pairs.ToArrayOf().ToMatrix(11, 10)));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteEncodeableMatrix(
                    "M",
                    pairs.ToArrayOf().ToMatrix(11, 10),
                    new ExpandedNodeId(77790u)));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// The encoders check the shape of an empty inline matrix like the
        /// decoders do: the product of the non zero dimensions is bounded by
        /// MaxArrayLength (and must fit an array), so they do not emit a
        /// shape the peer rejects.
        /// </summary>
        [TestCase(new[] { 0, 70000 }, BadEncodingLimitsExceeded)]
        [TestCase(new[] { 100000, 100000, 0 }, BadEncodingErrorCode)]
        public void EmptyInlineMatrixShapeIsBoundedByTheEncoders(int[] dimensions, uint expected)
        {
            ServiceMessageContext context = CreateContext();
            context.MaxArrayLength = 65535;
            MatrixOf<double> doubles = Array.Empty<double>().ToArrayOf().ToMatrix(dimensions);
            MatrixOf<InlineMatrixFieldTests.Pair> pairs =
                Array.Empty<InlineMatrixFieldTests.Pair>().ToArrayOf().ToMatrix(dimensions);

            using var binary = new BinaryEncoder(context);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => binary.WriteInlineMatrixValue("M", Variant.From(doubles)));
            Assert.That(ex.StatusCode, Is.EqualTo(new StatusCode(expected)));
            ex = Assert.Throws<ServiceResultException>(
                () => binary.WriteEncodeableMatrix("M", pairs));
            Assert.That(ex.StatusCode, Is.EqualTo(new StatusCode(expected)));

            using var json = new JsonEncoder(context, JsonEncoderOptions.Verbose);
            ex = Assert.Throws<ServiceResultException>(
                () => json.WriteInlineMatrixValue("M", Variant.From(doubles)));
            Assert.That(ex.StatusCode, Is.EqualTo(new StatusCode(expected)));
            ex = Assert.Throws<ServiceResultException>(
                () => json.WriteEncodeableMatrix("N", pairs));
            Assert.That(ex.StatusCode, Is.EqualTo(new StatusCode(expected)));
        }

        /// <summary>
        /// The dimensions of an empty inline matrix are bounded too: the
        /// product of the non zero dimensions must neither overflow nor
        /// exceed MaxArrayLength, or a consumer materializing the shape runs
        /// out of memory.
        /// </summary>
        [TestCase(new[] { 100000, 100000, 0 }, 0u)]
        [TestCase(new[] { 0, int.MaxValue }, 0u)]
        [TestCase(new[] { 0, 70000 }, BadEncodingLimitsExceeded)]
        [TestCase(new[] { -1, 70000 }, BadEncodingLimitsExceeded)]
        public void BinaryEmptyInlineMatrixDimensionsAreBounded(int[] dimensions, uint expected)
        {
            byte[] encoded = Int32s([dimensions.Length, .. dimensions]);
            foreach (int maxArrayLength in new[] { 65535, 0 })
            {
                ServiceMessageContext context = CreateContext();
                context.MaxArrayLength = maxArrayLength;
                StatusCode status = expected == 0 || maxArrayLength == 0
                    ? StatusCodes.BadDecodingError
                    : new StatusCode(expected);
                if (maxArrayLength == 0 && expected != 0)
                {
                    // no limit, a bounded empty shape is fine
                    using var accepting = new BinaryDecoder(encoded, context);
                    Assert.That(
                        accepting.ReadVariantValue(
                            "M",
                            TypeInfo.Create(BuiltInType.Double, ValueRanks.TwoDimensions))
                            .GetDoubleMatrix().Count,
                        Is.Zero);
                    continue;
                }

                using var decoder = new BinaryDecoder(encoded, context);
                ServiceResultException ex = Assert.Throws<ServiceResultException>(
                    () => decoder.ReadVariantValue(
                        "M",
                        TypeInfo.Create(BuiltInType.Double, ValueRanks.TwoDimensions)));
                Assert.That(ex.StatusCode, Is.EqualTo(status));

                using var encodeables = new BinaryDecoder(encoded, context);
                ex = Assert.Throws<ServiceResultException>(
                    () => encodeables.ReadEncodeableMatrix<InlineMatrixFieldTests.Pair>("M"));
                Assert.That(ex.StatusCode, Is.EqualTo(status));
            }
        }

        /// <summary>
        /// A negative dimension means no values in every encoding (Table 28):
        /// it is decoded as an empty matrix with the dimension 0 by the
        /// binary, JSON and XML decoders alike.
        /// </summary>
        [Test]
        public void NegativeInlineMatrixDimensionIsEmptyInEveryEncoding()
        {
            ServiceMessageContext context = CreateContext();
            var type = TypeInfo.Create(BuiltInType.Double, ValueRanks.TwoDimensions);

            using (var binary = new BinaryDecoder(Int32s(2, -1, 3, 2, -5, 2), context))
            {
                Assert.That(binary.ReadVariantValue("M", type).GetDoubleMatrix().Dimensions,
                    Is.EqualTo(s_zeroByThree));
                Assert.That(binary.ReadEncodeableMatrix<InlineMatrixFieldTests.Pair>("E").Dimensions,
                    Is.EqualTo(s_zeroByTwo));
            }

            using (var json = new JsonDecoder(
                "{\"M\":{\"Array\":[],\"Dimensions\":[-1,3]},\"E\":{\"Dimensions\":[-5,2],\"Array\":[]}}",
                context))
            {
                Assert.That(json.ReadVariantValue("M", type).GetDoubleMatrix().Dimensions,
                    Is.EqualTo(s_zeroByThree));
                Assert.That(json.ReadEncodeableMatrix<InlineMatrixFieldTests.Pair>("E").Dimensions,
                    Is.EqualTo(s_zeroByTwo));
            }

            string xml =
                "<Root xmlns=\"" + Namespaces.OpcUaXsd + "\">" +
                "<M><Matrix><Dimensions><Int32>-1</Int32><Int32>3</Int32></Dimensions></Matrix></M>" +
                "<E><Dimensions><Int32>-5</Int32><Int32>2</Int32></Dimensions></E>" +
                "</Root>";
            DelegateEncodeable.Reader = d =>
            {
                Assert.That(d.ReadVariantValue("M", type).GetDoubleMatrix().Dimensions,
                    Is.EqualTo(s_zeroByThree));
                Assert.That(d.ReadEncodeableMatrix<InlineMatrixFieldTests.Pair>("E").Dimensions,
                    Is.EqualTo(s_zeroByTwo));
                return Variant.From(true);
            };
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            using (var reader = XmlReader.Create(stream, CoreUtils.DefaultXmlReaderSettings()))
            using (var xmlDecoder = new XmlDecoder(reader, context))
            {
                xmlDecoder.PushNamespace(Namespaces.OpcUaXsd);
                DelegateEncodeable.LastRead = default;
                xmlDecoder.ReadEncodeable<DelegateEncodeable>("Root");
                Assert.That(DelegateEncodeable.LastRead.GetBoolean(), Is.True, "XmlDecoder");
            }

            using var parser = new XmlParser(xml, context);
            parser.PushNamespace(Namespaces.OpcUaXsd);
            DelegateEncodeable.LastRead = default;
            parser.ReadEncodeable<DelegateEncodeable>("Root");
            Assert.That(DelegateEncodeable.LastRead.GetBoolean(), Is.True, "XmlParser");
        }

        /// <summary>
        /// The JSON and XML decoders bound the shape of an empty inline
        /// matrix by MaxArrayLength like the binary decoder: [0, 70000] has no
        /// values but is rejected under a limit of 65535.
        /// </summary>
        [Test]
        public void EmptyInlineMatrixShapeIsBoundedByTheTextDecoders()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxArrayLength = 65535;
            var type = TypeInfo.Create(BuiltInType.Double, ValueRanks.TwoDimensions);

            using (var json = new JsonDecoder(
                "{\"M\":{\"Array\":[],\"Dimensions\":[0,70000]},\"E\":{\"Dimensions\":[0,70000],\"Array\":[]}}",
                context))
            {
                ServiceResultException ex = Assert.Throws<ServiceResultException>(
                    () => json.ReadVariantValue("M", type));
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded), "JSON M");
                ex = Assert.Throws<ServiceResultException>(
                    () => json.ReadEncodeableMatrix<InlineMatrixFieldTests.Pair>("E"));
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded), "JSON E");
            }

            // One field per document: after a read throws, the XML reader is
            // left inside that field, so a second field would not be found.
            AssertXmlShapeRejected(
                context,
                "<M><Dimensions><Int32>0</Int32><Int32>70000</Int32></Dimensions><Elements /></M>",
                d => d.ReadVariantValue("M", type),
                "XML M");
            AssertXmlShapeRejected(
                context,
                "<E><Dimensions><Int32>0</Int32><Int32>70000</Int32></Dimensions></E>",
                d => d.ReadEncodeableMatrix<InlineMatrixFieldTests.Pair>("E"),
                "XML E");
        }

        private static void AssertXmlShapeRejected(
            ServiceMessageContext context,
            string field,
            Action<IDecoder> read,
            string message)
        {
            string xml = "<Root xmlns=\"" + Namespaces.OpcUaXsd + "\">" + field + "</Root>";
            DelegateEncodeable.Reader = d =>
            {
                ServiceResultException ex = Assert.Throws<ServiceResultException>(() => read(d));
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded), message);
                return Variant.From(true);
            };
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            using (var reader = XmlReader.Create(stream, CoreUtils.DefaultXmlReaderSettings()))
            using (var xmlDecoder = new XmlDecoder(reader, context))
            {
                xmlDecoder.PushNamespace(Namespaces.OpcUaXsd);
                DelegateEncodeable.LastRead = default;
                xmlDecoder.ReadEncodeable<DelegateEncodeable>("Root");
                Assert.That(DelegateEncodeable.LastRead.GetBoolean(), Is.True, message + " XmlDecoder");
            }

            using var parser = new XmlParser(xml, context);
            parser.PushNamespace(Namespaces.OpcUaXsd);
            DelegateEncodeable.LastRead = default;
            parser.ReadEncodeable<DelegateEncodeable>("Root");
            Assert.That(DelegateEncodeable.LastRead.GetBoolean(), Is.True, message + " XmlParser");
        }

        private static IEnumerable<Variant> MatrixValues()
        {
            yield return Variant.From(default(MatrixOf<double>));
            yield return Variant.From(MatrixOf<double>.Empty);
            yield return Variant.From(new double[] { 1, 2, 3, 4, 5, 6 }.ToArrayOf().ToMatrix(2, 3));
        }

        /// <summary>
        /// The Variant content of a matrix Variable value: a 2 x 3 matrix
        /// stays a matrix, an empty single dimension matrix is an empty
        /// array and a null matrix is null.
        /// </summary>
        private static void AssertSameContent(Variant expected, Variant actual)
        {
            MatrixOf<double> matrix = expected.GetDoubleMatrix();
            if (matrix.IsNull)
            {
                Assert.That(actual.GetDoubleArray().IsNull, Is.True, "null");
                return;
            }
            if (matrix.Dimensions.Length == 1)
            {
                Assert.That(actual.GetDoubleArray().ToArray(), Is.EqualTo(matrix.Span.ToArray()));
                return;
            }
            Assert.That(actual.GetDoubleMatrix().Dimensions, Is.EqualTo(matrix.Dimensions));
            Assert.That(actual.GetDoubleMatrix().Span.ToArray(), Is.EqualTo(matrix.Span.ToArray()));
        }

        private static string Encode(Codec codec, Variant value, bool asMatrixField)
        {
            ServiceMessageContext context = CreateContext();
            void Write(IEncoder encoder)
            {
                encoder.WriteInt32("A", 7);
                if (asMatrixField)
                {
                    encoder.WriteInlineMatrixValue("M", value);
                }
                else
                {
                    encoder.WriteVariantValue("M", value);
                }
                encoder.WriteInt32("B", 9);
            }
            switch (codec)
            {
                case Codec.Binary:
                {
                    using var encoder = new BinaryEncoder(context);
                    Write(encoder);
                    return Convert.ToBase64String(encoder.CloseAndReturnBuffer());
                }
                case Codec.Json:
                {
                    using var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose);
                    Write(encoder);
                    return encoder.CloseAndReturnText();
                }
                default:
                {
                    using var encoder = new XmlEncoder(context);
                    encoder.PushNamespace(Namespaces.OpcUaXsd);
                    encoder.WriteEncodeable("Root", new DelegateEncodeable(Write));
                    encoder.PopNamespace();
                    return encoder.CloseAndReturnText();
                }
            }
        }

        private static Variant RoundTrip(
            Codec codec,
            Action<IEncoder> write,
            Func<IDecoder, Variant> read)
        {
            ServiceMessageContext context = CreateContext();
            void Write(IEncoder encoder)
            {
                encoder.WriteInt32("A", 7);
                write(encoder);
                encoder.WriteInt32("B", 9);
            }
            Variant Read(IDecoder decoder)
            {
                Assert.That(decoder.ReadInt32("A"), Is.EqualTo(7));
                Variant value = read(decoder);
                Assert.That(decoder.ReadInt32("B"), Is.EqualTo(9), "the field after the value");
                return value;
            }
            switch (codec)
            {
                case Codec.Binary:
                {
                    byte[] buffer;
                    using (var encoder = new BinaryEncoder(context))
                    {
                        Write(encoder);
                        buffer = encoder.CloseAndReturnBuffer();
                    }
                    using var decoder = new BinaryDecoder(buffer, context);
                    return Read(decoder);
                }
                case Codec.Json:
                {
                    string json;
                    using (var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose))
                    {
                        Write(encoder);
                        json = encoder.CloseAndReturnText();
                    }
                    using var decoder = new JsonDecoder(json, context);
                    return Read(decoder);
                }
                default:
                {
                    string xml;
                    using (var encoder = new XmlEncoder(context))
                    {
                        encoder.PushNamespace(Namespaces.OpcUaXsd);
                        encoder.WriteEncodeable("Root", new DelegateEncodeable(Write));
                        encoder.PopNamespace();
                        xml = encoder.CloseAndReturnText();
                    }
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
                    using var reader = XmlReader.Create(stream, CoreUtils.DefaultXmlReaderSettings());
                    using var decoder = new XmlDecoder(reader, context);
                    decoder.PushNamespace(Namespaces.OpcUaXsd);
                    // XML fields need an enclosing element, decoded through
                    // an encodeable that calls back into this round trip.
                    DelegateEncodeable.Reader = Read;
                    decoder.ReadEncodeable<DelegateEncodeable>("Root");
                    decoder.PopNamespace();
                    return DelegateEncodeable.LastRead;
                }
            }
        }

        private static Structure CreateStructure()
        {
            var definition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField { Name = "Name", DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Matrix", DataType = DataTypeIds.Double, ValueRank = ValueRanks.TwoDimensions },
                    new StructureField { Name = "Array", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.OneDimension },
                    new StructureField { Name = "Count", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }
                ]
            };
            return new Structure(
                new XmlQualifiedName("TestStructure", Namespaces.OpcUaXsd),
                new ExpandedNodeId(77800u),
                new ExpandedNodeId(77801u),
                new ExpandedNodeId(77802u),
                definition,
                new Dictionary<string, BuiltInType>
                {
                    ["Name"] = BuiltInType.String,
                    ["Matrix"] = BuiltInType.Double,
                    ["Array"] = BuiltInType.Int32,
                    ["Count"] = BuiltInType.Int32
                });
        }

        private static byte[] Int32s(params int[] values)
        {
            return values.SelectMany(BitConverter.GetBytes).ToArray();
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }

        private static SystemContext CreateSystemContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:inline-matrix");
            return new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        /// <summary>
        /// Encloses the fields written by a delegate in a structure, and on
        /// decode reads them back with the round trip reader.
        /// </summary>
        public sealed class DelegateEncodeable : IEncodeable
        {
            [ThreadStatic]
            internal static Func<IDecoder, Variant> Reader;

            [ThreadStatic]
            internal static Variant LastRead;

            private readonly Action<IEncoder> m_write;

            public DelegateEncodeable()
            {
            }

            public DelegateEncodeable(Action<IEncoder> write)
            {
                m_write = write;
            }

            public ExpandedNodeId TypeId => new(77803, 0);
            public ExpandedNodeId BinaryEncodingId => new(77804, 0);
            public ExpandedNodeId XmlEncodingId => new(77805, 0);

            public void Encode(IEncoder encoder)
            {
                m_write(encoder);
            }

            public void Decode(IDecoder decoder)
            {
                LastRead = Reader(decoder);
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return ReferenceEquals(this, encodeable);
            }

            public object Clone()
            {
                return new DelegateEncodeable(m_write);
            }
        }
    }
}
