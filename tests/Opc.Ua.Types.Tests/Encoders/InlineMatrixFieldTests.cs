/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// A multi-dimensional structure field is encoded as the inline matrix
    /// of OPC 10000-6 5.2.5 (binary), 5.4.5 (JSON) through the raw
    /// <see cref="IEncoder.WriteVariantValue(string, in Variant)"/> /
    /// <see cref="IDecoder.ReadVariantValue(string, TypeInfo)"/> pair, which
    /// the DataTypeDefinition driven structure codec and the generated data
    /// types use. Null, empty and populated matrices must round trip for
    /// every encoding without desynchronizing the fields that follow.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class InlineMatrixFieldTests
    {
        public enum Codec
        {
            Binary,
            Json,
            XmlDecoder,
            XmlParser
        }

        public enum Shape
        {
            /// <summary>Variant.From(default(MatrixOf&lt;T&gt;)).</summary>
            Null,
            /// <summary>A null value that keeps the matrix type info.</summary>
            TypedNull,
            /// <summary>MatrixOf&lt;T&gt;.Empty, a single zero dimension.</summary>
            Empty,
            /// <summary>An empty matrix with a zero dimension.</summary>
            EmptyRows,
            /// <summary>A 2 x 3 matrix.</summary>
            Full,
            /// <summary>A 2 x 1 x 2 matrix.</summary>
            Cube
        }

        private static readonly int[] s_twoByTwo = [2, 2];
        private static readonly int[] s_twoByZero = [2, 0];
        private static readonly int[] s_zeroByZero = [0, 0];
        private static readonly int[] s_zeroByThree = [0, 3];
        private static readonly int[] s_zeroByFive = [0, 5];
        private static readonly int[] s_emptyCube = [100, 200, 0];
        private static readonly int[] s_oneToFour = [1, 2, 3, 4];
        private static readonly int[] s_oneToSix = [1, 2, 3, 4, 5, 6];
        private static readonly double[] s_halfDoubles = [0.5, 1.5, 2.5, 3.5, 4.5, 5.5];

        private static readonly BuiltInType[] s_elementTypes =
        [
            BuiltInType.Double,
            BuiltInType.String,
            BuiltInType.Enumeration,
            BuiltInType.ExtensionObject,
            BuiltInType.Variant
        ];

        [Test]
        [Combinatorial]
        public void InlineMatrixFieldRoundTrips(
            [Values] Codec codec,
            [ValueSource(nameof(s_elementTypes))] BuiltInType builtInType,
            [Values] Shape shape)
        {
            Variant value = Create(builtInType, shape);
            int rank = shape == Shape.Cube ? 3 : ValueRanks.TwoDimensions;

            Variant decoded = RoundTrip(codec, value, TypeInfo.Create(builtInType, rank));

            AssertSameMatrix(builtInType, value, decoded);
        }

        /// <summary>
        /// The XML decoders reject a populated inline matrix whose rank is
        /// not the declared rank of the field; a null or empty matrix, whose
        /// rank follows its (lost or 0 x 0) dimensions, is still accepted.
        /// </summary>
        [Test]
        [Combinatorial]
        public void XmlInlineMatrixWithOtherRankIsRejected(
            [Values(Codec.XmlDecoder, Codec.XmlParser)] Codec codec,
            [ValueSource(nameof(s_elementTypes))] BuiltInType builtInType)
        {
            Assert.That(
                () => RoundTrip(codec, Create(builtInType, Shape.Full), TypeInfo.Create(builtInType, 3)),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(
                () => RoundTrip(codec, Create(builtInType, Shape.Cube), TypeInfo.Create(builtInType, ValueRanks.TwoDimensions)),
                Throws.TypeOf<ServiceResultException>());
            foreach (Shape shape in new[] { Shape.Null, Shape.TypedNull, Shape.Empty, Shape.EmptyRows })
            {
                Variant value = Create(builtInType, shape);
                AssertSameMatrix(builtInType, value, RoundTrip(codec, value, TypeInfo.Create(builtInType, 3)));
            }
        }

        /// <summary>
        /// A null matrix is a null dimensions array (length -1) and nothing
        /// else (OPC 10000-6 5.2.5 Table 28).
        /// </summary>
        [TestCase(Shape.Null)]
        [TestCase(Shape.TypedNull)]
        public void BinaryNullInlineMatrixIsNullDimensions(Shape shape)
        {
            byte[] encoded = EncodeBinary(Create(BuiltInType.Double, shape));

            Assert.That(encoded, Is.EqualTo(Int32s(7, -1, 9)));
            Assert.That(DecodeBinary(encoded, BuiltInType.Double).GetDoubleMatrix().IsNull, Is.True);
        }

        /// <summary>
        /// A populated matrix is the dimensions array followed by the values,
        /// without the Variant encoding byte and without a length prefix.
        /// </summary>
        [Test]
        public void BinaryInlineMatrixIsDimensionsThenValues()
        {
            Variant value = Variant.From(s_oneToSix.ToArrayOf().ToMatrix(2, 3));

            byte[] encoded = EncodeBinary(value);

            Assert.That(encoded, Is.EqualTo(Int32s(7, 2, 2, 3, 1, 2, 3, 4, 5, 6, 9)));
        }

        /// <summary>
        /// A 2 x 3 Double matrix: dimensions, then six Doubles.
        /// </summary>
        [Test]
        public void BinaryDoubleInlineMatrixExactBytes()
        {
            Variant value = Variant.From(s_halfDoubles
                .ToArrayOf().ToMatrix(2, 3));

            byte[] encoded = EncodeBinary(value);

            byte[] expected = Concat(
                Int32s(7, 2, 2, 3),
                Doubles(0.5, 1.5, 2.5, 3.5, 4.5, 5.5),
                Int32s(9));
            Assert.That(encoded, Is.EqualTo(expected));
            AssertSameMatrix(BuiltInType.Double, value, DecodeBinary(encoded, BuiltInType.Double));
        }

        /// <summary>
        /// A 2 x 1 x 2 String matrix: three dimensions, then four Strings
        /// (a null String is length -1).
        /// </summary>
        [Test]
        public void BinaryStringCubeInlineMatrixExactBytes()
        {
            Variant value = Variant.From(new string[] { "a", null, string.Empty, "bc" }
                .ToArrayOf().ToMatrix(2, 1, 2));

            byte[] encoded = EncodeBinary(value);

            byte[] expected = Concat(
                Int32s(7, 3, 2, 1, 2),
                Int32s(1), Encoding.UTF8.GetBytes("a"),
                Int32s(-1),
                Int32s(0),
                Int32s(2), Encoding.UTF8.GetBytes("bc"),
                Int32s(9));
            Assert.That(encoded, Is.EqualTo(expected));
            AssertSameMatrix(
                BuiltInType.String,
                value,
                DecodeBinary(encoded, BuiltInType.String, 3));
        }

        /// <summary>
        /// Enumeration elements are Int32 values.
        /// </summary>
        [Test]
        public void BinaryEnumerationInlineMatrixExactBytes()
        {
            Variant value = Variant.From(new EnumValue[]
            {
                new(1), new(2), new(0), new(5)
            }.ToArrayOf().ToMatrix(2, 2));

            byte[] encoded = EncodeBinary(value);

            Assert.That(encoded, Is.EqualTo(Int32s(7, 2, 2, 2, 1, 2, 0, 5, 9)));
            AssertSameMatrix(
                BuiltInType.Enumeration,
                value,
                DecodeBinary(encoded, BuiltInType.Enumeration));
        }

        /// <summary>
        /// ExtensionObject elements are encoded one after the other, a null
        /// ExtensionObject is the null NodeId and encoding byte 0.
        /// </summary>
        [Test]
        public void BinaryExtensionObjectInlineMatrixExactBytes()
        {
            Variant value = Variant.From(new ExtensionObject[]
            {
                ExtensionObject.Null,
                new(new ExpandedNodeId(5001u), ByteString.From(new byte[] { 1, 42 }))
            }.ToArrayOf().ToMatrix(1, 2));

            byte[] encoded = EncodeBinary(value);

            byte[] expected = Concat(
                Int32s(7, 2, 1, 2),
                // null: two byte NodeId 0, no body
                [0x00, 0x00, 0x00],
                // four byte NodeId ns=0;i=5001, ByteString body
                [0x01, 0x00, 0x89, 0x13, 0x01],
                Int32s(2),
                [1, 42],
                Int32s(9));
            Assert.That(encoded, Is.EqualTo(expected));
            AssertSameMatrix(
                BuiltInType.ExtensionObject,
                value,
                DecodeBinary(encoded, BuiltInType.ExtensionObject));
        }

        /// <summary>
        /// Variant elements are complete Variants with their encoding byte.
        /// </summary>
        [Test]
        public void BinaryVariantInlineMatrixExactBytes()
        {
            Variant value = Variant.From(new Variant[]
            {
                Variant.From(3), Variant.Null
            }.ToArrayOf().ToMatrix(2, 1));

            byte[] encoded = EncodeBinary(value);

            byte[] expected = Concat(
                Int32s(7, 2, 2, 1),
                [(byte)BuiltInType.Int32],
                Int32s(3),
                [0x00],
                Int32s(9));
            Assert.That(encoded, Is.EqualTo(expected));
            AssertSameMatrix(BuiltInType.Variant, value, DecodeBinary(encoded, BuiltInType.Variant));
        }

        /// <summary>
        /// An encodeable matrix is the dimensions followed by the encoded
        /// structures, no length prefix.
        /// </summary>
        [Test]
        public void BinaryEncodeableInlineMatrixExactBytes()
        {
            MatrixOf<Pair> value = new Pair[] { new() { X = 1 }, new() { X = 2 }, new() { X = 3 }, new() { X = 4 } }
                .ToArrayOf().ToMatrix(2, 2);
            ServiceMessageContext context = CreateContext();

            byte[] encoded;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteInt32("A", 7);
                encoder.WriteEncodeableMatrix("M", value);
                encoder.WriteInt32("B", 9);
                encoded = encoder.CloseAndReturnBuffer();
            }

            Assert.That(encoded, Is.EqualTo(Int32s(7, 2, 2, 2, 1, 2, 3, 4, 9)));

            using var decoder = new BinaryDecoder(encoded, context);
            Assert.That(decoder.ReadInt32("A"), Is.EqualTo(7));
            MatrixOf<Pair> decoded = decoder.ReadEncodeableMatrix<Pair>("M");
            Assert.That(decoder.ReadInt32("B"), Is.EqualTo(9));
            Assert.That(decoded.Dimensions, Is.EqualTo(s_twoByTwo));
            Assert.That(decoded.Span.ToArray().Select(p => p.X), Is.EqualTo(s_oneToFour));
        }

        /// <summary>
        /// Null and empty encodeable matrices.
        /// </summary>
        [Test]
        public void BinaryNullAndEmptyEncodeableInlineMatrixExactBytes()
        {
            ServiceMessageContext context = CreateContext();
            byte[] encoded;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteEncodeableMatrix("N", default(MatrixOf<Pair>));
                encoder.WriteEncodeableMatrix("E", MatrixOf<Pair>.Empty);
                encoder.WriteEncodeableMatrix("R", Array.Empty<Pair>().ToArrayOf().ToMatrix(2, 0));
                encoder.WriteEncodeableMatrix(
                    "T",
                    Array.Empty<Pair>().ToArrayOf().ToMatrix(0, 0),
                    new ExpandedNodeId(77790u));
                encoder.WriteInt32("B", 9);
                encoded = encoder.CloseAndReturnBuffer();
            }

            Assert.That(encoded, Is.EqualTo(Int32s(-1, 2, 0, 0, 2, 2, 0, 2, 0, 0, 9)));

            using var decoder = new BinaryDecoder(encoded, context);
            Assert.That(decoder.ReadEncodeableMatrix<Pair>("N").IsNull, Is.True);
            MatrixOf<Pair> empty = decoder.ReadEncodeableMatrix<Pair>("E");
            Assert.That(empty.IsNull, Is.False);
            Assert.That(empty.Dimensions, Is.EqualTo(s_zeroByZero));
            Assert.That(decoder.ReadEncodeableMatrix<Pair>("R").Dimensions, Is.EqualTo(s_twoByZero));
            Assert.That(decoder.ReadEncodeableMatrix<Pair>("T", new ExpandedNodeId(77790u)).Count, Is.Zero);
            Assert.That(decoder.ReadInt32("B"), Is.EqualTo(9));
        }

        /// <summary>
        /// An empty matrix keeps its dimensions and carries no values.
        /// </summary>
        [Test]
        public void BinaryEmptyInlineMatrixKeepsItsDimensions()
        {
            Variant value = Variant.From(Array.Empty<int>().ToArrayOf().ToMatrix(0, 3));

            byte[] encoded = EncodeBinary(value);

            Assert.That(encoded, Is.EqualTo(Int32s(7, 2, 0, 3, 9)));
            Assert.That(
                DecodeBinary(encoded, BuiltInType.Int32).GetInt32Matrix().Dimensions,
                Is.EqualTo(s_zeroByThree));
        }

        /// <summary>
        /// An inline matrix has at least 2 dimensions: the empty MatrixOf
        /// (a single zero dimension) is written as the 0 x 0 matrix.
        /// </summary>
        [Test]
        public void BinaryEmptyMatrixOfIsWrittenAsZeroByZero()
        {
            byte[] encoded = EncodeBinary(Variant.From(MatrixOf<double>.Empty));

            Assert.That(encoded, Is.EqualTo(Int32s(7, 2, 0, 0, 9)));
            MatrixOf<double> decoded = DecodeBinary(encoded, BuiltInType.Double).GetDoubleMatrix();
            Assert.That(decoded.IsNull, Is.False);
            Assert.That(decoded.Dimensions, Is.EqualTo(s_zeroByZero));
        }

        /// <summary>
        /// A non empty matrix with a single dimension cannot be written as an
        /// inline matrix (it would be taken for an array by nobody and for
        /// dimensions by the peer) and is rejected.
        /// </summary>
        [Test]
        public void BinaryOneDimensionalMatrixIsRejectedByTheEncoder()
        {
            using var encoder = new BinaryEncoder(CreateContext());
            var value = Variant.From(new double[] { 1, 2 }.ToArrayOf().ToMatrix(2));
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteInlineMatrixValue("M", value));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));

            ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteEncodeableMatrix(
                    "M",
                    new Pair[] { new() }.ToArrayOf().ToMatrix(1)));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        /// <summary>
        /// A dimension &lt;= 0 means no values follow (Table 28); a negative
        /// dimension is decoded as 0.
        /// </summary>
        [Test]
        public void BinaryNegativeDimensionIsAnEmptyMatrix()
        {
            MatrixOf<double> decoded = DecodeBinary(Int32s(7, 2, -3, 5, 9), BuiltInType.Double)
                .GetDoubleMatrix();

            Assert.That(decoded.IsNull, Is.False);
            Assert.That(decoded.Dimensions, Is.EqualTo(s_zeroByFive));
        }

        /// <summary>
        /// A zero dimension makes the matrix empty, but the other dimensions
        /// are still bounded: a consumer materializing the shape of an empty
        /// [65536, 65537, 0] matrix would run out of memory.
        /// </summary>
        [Test]
        public void BinaryZeroDimensionWithHugeOtherDimensionsIsRejected()
        {
            AssertDecodingFails(
                Int32s(7, 3, 65536, 65537, 0, 9),
                d => ReadField(d, TypeInfo.Create(BuiltInType.Int32, 3)),
                StatusCodes.BadDecodingError);

            MatrixOf<int> decoded = DecodeBinary(
                Int32s(7, 3, 100, 200, 0, 9),
                BuiltInType.Int32,
                3).GetInt32Matrix();
            Assert.That(decoded.Count, Is.Zero);
            Assert.That(decoded.Dimensions, Is.EqualTo(s_emptyCube));
        }

        /// <summary>
        /// The Variant encoding of a matrix (OPC 10000-6 5.2.2.16) is not
        /// affected: encoding byte, array length, values, dimensions.
        /// </summary>
        [Test]
        public void VariantMatrixEncodingIsUnchanged()
        {
            ServiceMessageContext context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            encoder.WriteVariant(null, Variant.From(s_oneToSix.ToArrayOf().ToMatrix(2, 3)));
            byte[] encoded = encoder.CloseAndReturnBuffer();

            byte[] expected = Concat(
                [(byte)((byte)BuiltInType.Int32 | 0x80 | 0x40)],
                Int32s(6, 1, 2, 3, 4, 5, 6, 2, 2, 3));
            Assert.That(encoded, Is.EqualTo(expected));
        }

        /// <summary>
        /// A null matrix field is a null JSON field, like a null encodeable
        /// matrix.
        /// </summary>
        [Test]
        public void JsonNullInlineMatrixIsNullField()
        {
            ServiceMessageContext context = CreateContext();
            string json;
            using (var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose))
            {
                encoder.WriteInt32("A", 7);
                encoder.WriteVariantValue("M", Variant.From(default(MatrixOf<double>)));
                encoder.WriteInt32("B", 9);
                json = encoder.CloseAndReturnText();
            }

            Assert.That(json, Does.Contain("\"M\":null"));
        }

        /// <summary>
        /// The Variant encoding still rejects a matrix with a zero dimension
        /// (OPC 10000-6 5.2.2.16), only the inline matrix may be empty.
        /// </summary>
        [Test]
        public void VariantEncodingStillRejectsEmptyMatrix()
        {
            ServiceMessageContext context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            Variant value = Variant.From(Array.Empty<int>().ToArrayOf().ToMatrix(0, 3));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteVariant(null, value));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        /// <summary>
        /// An inline matrix has at least 2 dimensions (Table 28); an empty or
        /// a single dimensions array is rejected, for the Variant value and
        /// the encodeable matrix alike.
        /// </summary>
        [TestCase(0)]
        [TestCase(1, 3)]
        [TestCase(1, 0)]
        public void BinaryInlineMatrixWithFewerThanTwoDimensionsIsRejected(params int[] dimensions)
        {
            byte[] encoded = Int32s([.. dimensions, 1, 2, 3]);

            AssertDecodingFails(
                encoded,
                d => d.ReadVariantValue("M", TypeInfo.Create(BuiltInType.Int32, ValueRanks.TwoDimensions)),
                StatusCodes.BadDecodingError);
            AssertDecodingFails(
                encoded,
                d => d.ReadEncodeableMatrix<Pair>("M"),
                StatusCodes.BadDecodingError);
        }

        /// <summary>
        /// A product of the dimensions beyond Int32 is rejected.
        /// </summary>
        [Test]
        public void BinaryInlineMatrixWithOverflowingDimensionsIsRejected()
        {
            byte[] encoded = Int32s(2, 65536, 65537);

            AssertDecodingFails(
                encoded,
                d => d.ReadVariantValue("M", TypeInfo.Create(BuiltInType.Byte, ValueRanks.TwoDimensions)),
                StatusCodes.BadDecodingError);
            AssertDecodingFails(
                encoded,
                d => d.ReadEncodeableMatrix<Pair>("M"),
                StatusCodes.BadDecodingError);
        }

        /// <summary>
        /// A product of the dimensions beyond MaxArrayLength is rejected
        /// before the values are allocated.
        /// </summary>
        [Test]
        public void BinaryInlineMatrixBeyondMaxArrayLengthIsRejected()
        {
            byte[] encoded = Int32s(2, 1000, 1000);

            AssertDecodingFails(
                encoded,
                d => d.ReadVariantValue("M", TypeInfo.Create(BuiltInType.Double, ValueRanks.TwoDimensions)),
                StatusCodes.BadEncodingLimitsExceeded);
            AssertDecodingFails(
                encoded,
                d => d.ReadEncodeableMatrix<Pair>("M"),
                StatusCodes.BadEncodingLimitsExceeded);
        }

        /// <summary>
        /// Without an array length limit, dimensions the remaining message
        /// cannot hold are rejected before the values are allocated.
        /// </summary>
        [TestCase(BuiltInType.Double)]
        [TestCase(BuiltInType.Variant)]
        [TestCase(BuiltInType.String)]
        public void BinaryHugeInlineMatrixIsRejectedWithoutAllocation(BuiltInType builtInType)
        {
            ServiceMessageContext context = CreateContext();
            context.MaxArrayLength = 0;
            byte[] encoded = Int32s(2, 46340, 46340, 1, 2, 3);
            using var decoder = new BinaryDecoder(encoded, context);

#if NET
            long before = GC.GetAllocatedBytesForCurrentThread();
#endif
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariantValue("M", TypeInfo.Create(builtInType, ValueRanks.TwoDimensions)));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
#if NET
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.LessThan(1024 * 1024), "bytes allocated");
#endif
        }

        /// <summary>
        /// Values missing at the end of the message are a decoding error.
        /// </summary>
        [Test]
        public void BinaryTruncatedInlineMatrixIsRejected()
        {
            AssertDecodingFails(
                Int32s(2, 2, 3, 1, 2, 3, 4, 5),
                d => d.ReadVariantValue("M", TypeInfo.Create(BuiltInType.Int32, ValueRanks.TwoDimensions)),
                StatusCodes.BadDecodingError);
            AssertDecodingFails(
                Int32s(2, 2, 2, 1, 2, 3),
                d => d.ReadEncodeableMatrix<Pair>("M"),
                StatusCodes.BadDecodingError);
        }

        /// <summary>
        /// JSON and XML write the empty MatrixOf with two zero dimensions and
        /// reject an inline matrix with fewer than two dimensions.
        /// </summary>
        [Test]
        public void JsonEmptyMatrixOfHasTwoDimensions()
        {
            ServiceMessageContext context = CreateContext();
            string json;
            using (var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose))
            {
                encoder.WriteInlineMatrixValue("M", Variant.From(MatrixOf<double>.Empty));
                encoder.WriteEncodeableMatrix("E", MatrixOf<Pair>.Empty);
                json = encoder.CloseAndReturnText();
            }

            Assert.That(json, Does.Contain("\"M\":{\"Array\":[],\"Dimensions\":[0,0]}"));
            Assert.That(json, Does.Contain("\"E\":{\"Dimensions\":[0,0],\"Array\":[]}"));

            using var decoder = new JsonDecoder(
                "{\"M\":{\"Array\":[],\"Dimensions\":[0]},\"N\":{\"Dimensions\":[2],\"Array\":[{},{}]}}",
                context);
            Assert.That(
                () => decoder.ReadVariantValue("M", TypeInfo.Create(BuiltInType.Double, ValueRanks.TwoDimensions)),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(
                () => decoder.ReadEncodeableMatrix<Pair>("N"),
                Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// Earlier versions wrote the empty encodeable MatrixOf to JSON and
        /// XML with its single dimension 0; such documents still load as the
        /// empty 0 x 0 matrix (binary stays strict).
        /// </summary>
        [Test]
        public void LegacyOneDimensionalEmptyEncodeableMatrixLoads()
        {
            ServiceMessageContext context = CreateContext();

            using (var jsonDecoder = new JsonDecoder(
                "{\"E\":{\"Dimensions\":[0],\"Array\":[]},\"T\":{\"Array\":[],\"Dimensions\":[0]}}",
                context))
            {
                MatrixOf<Pair> empty = jsonDecoder.ReadEncodeableMatrix<Pair>("E");
                Assert.That(empty.IsNull, Is.False);
                Assert.That(empty.Count, Is.Zero);
                Assert.That(empty.Dimensions, Is.EqualTo(s_zeroByZero));
                MatrixOf<Pair> typed = jsonDecoder.ReadEncodeableMatrix<Pair>("T", new ExpandedNodeId(77790u));
                Assert.That(typed.IsNull, Is.False);
                Assert.That(typed.Dimensions, Is.EqualTo(s_zeroByZero));
            }

            string xml;
            using (var encoder = new XmlEncoder(context))
            {
                encoder.PushNamespace(Namespaces.OpcUaXsd);
                encoder.WriteEncodeableMatrix("E", MatrixOf<Pair>.Empty);
                encoder.PopNamespace();
                xml = encoder.CloseAndReturnText();
            }
            // Rewrite the dimensions to what earlier versions wrote.
            int start = xml.IndexOf("<Dimensions>", StringComparison.Ordinal);
            int end = xml.IndexOf("</Dimensions>", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            Assert.That(end, Is.GreaterThan(start));
            string legacy =
                xml[..start] +
                "<Dimensions><Int32>0</Int32></Dimensions>" +
                xml[(end + "</Dimensions>".Length)..];
            Assert.That(legacy, Is.Not.EqualTo(xml));

            using (var parser = new XmlParser(legacy, context))
            {
                parser.PushNamespace(Namespaces.OpcUaXsd);
                MatrixOf<Pair> parsed = parser.ReadEncodeableMatrix<Pair>("E");
                Assert.That(parsed.IsNull, Is.False);
                Assert.That(parsed.Dimensions, Is.EqualTo(s_zeroByZero));
            }

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacy));
            using var reader = XmlReader.Create(stream, CoreUtils.DefaultXmlReaderSettings());
            using var xmlDecoder = new XmlDecoder(reader, context);
            xmlDecoder.PushNamespace(Namespaces.OpcUaXsd);
            MatrixOf<Pair> decoded = xmlDecoder.ReadEncodeableMatrix<Pair>("E");
            Assert.That(decoded.IsNull, Is.False);
            Assert.That(decoded.Dimensions, Is.EqualTo(s_zeroByZero));
        }

        private static void AssertDecodingFails(
            byte[] encoded,
            Action<BinaryDecoder> read,
            StatusCode expected)
        {
            using var decoder = new BinaryDecoder(encoded, CreateContext());
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => read(decoder));
            Assert.That(ex.StatusCode, Is.EqualTo(expected));
        }

        private static Variant DecodeBinary(byte[] encoded, BuiltInType builtInType, int rank = 2)
        {
            using var decoder = new BinaryDecoder(encoded, CreateContext());
            return ReadField(decoder, TypeInfo.Create(builtInType, rank));
        }

        private static byte[] Doubles(params double[] values)
        {
            return values.SelectMany(BitConverter.GetBytes).ToArray();
        }

        private static byte[] Concat(params byte[][] parts)
        {
            return parts.SelectMany(p => p).ToArray();
        }

        private static Variant Create(BuiltInType builtInType, Shape shape)
        {
            int[] dimensions = shape switch
            {
                Shape.Empty => [0],
                Shape.EmptyRows => [2, 0],
                Shape.Full => [2, 3],
                Shape.Cube => [2, 1, 2],
                _ => null
            };
            if (shape == Shape.TypedNull)
            {
                return Variant.CreateDefault(TypeInfo.Create(builtInType, ValueRanks.TwoDimensions));
            }
            int count = dimensions?.Aggregate(1, (a, b) => a * b) ?? 0;
            switch (builtInType)
            {
                case BuiltInType.Double:
                    return Variant.From(Matrix(
                        Enumerable.Range(0, count).Select(i => i + 0.5).ToArray(),
                        dimensions));
                case BuiltInType.String:
                    return Variant.From(Matrix(
                        Enumerable.Range(0, count).Select(i => i == 1 ? null : "s" + i).ToArray(),
                        dimensions));
                case BuiltInType.Enumeration:
                    return Variant.From(Matrix(
                        Enumerable.Range(0, count).Select(i => new EnumValue(i % 3)).ToArray(),
                        dimensions));
                case BuiltInType.ExtensionObject:
                    return Variant.From(Matrix(
                        Enumerable.Range(0, count)
                            .Select(i => i == 0
                                ? ExtensionObject.Null
                                : new ExtensionObject(
                                    new ExpandedNodeId((uint)(5000 + i)),
                                    ByteString.From(new byte[] { (byte)i, 42 })))
                            .ToArray(),
                        dimensions));
                case BuiltInType.Variant:
                    return Variant.From(Matrix(
                        Enumerable.Range(0, count)
                            .Select(i => i % 2 == 0 ? Variant.From(i) : Variant.From("v" + i))
                            .ToArray(),
                        dimensions));
                default:
                    throw new ArgumentOutOfRangeException(nameof(builtInType));
            }
        }

        private static MatrixOf<T> Matrix<T>(T[] values, int[] dimensions)
        {
            return dimensions == null ? default : values.ToArrayOf().ToMatrix(dimensions);
        }

        private static void AssertSameMatrix(BuiltInType builtInType, Variant expected, Variant actual)
        {
            switch (builtInType)
            {
                case BuiltInType.Double:
                    AssertSameMatrix(expected.GetDoubleMatrix(), actual.GetDoubleMatrix());
                    break;
                case BuiltInType.String:
                    AssertSameMatrix(expected.GetStringMatrix(), actual.GetStringMatrix());
                    break;
                case BuiltInType.Enumeration:
                    AssertSameMatrix(expected.GetInt32Matrix(), actual.GetInt32Matrix());
                    break;
                case BuiltInType.ExtensionObject:
                    AssertSameMatrix(
                        expected.GetExtensionObjectMatrix(),
                        actual.GetExtensionObjectMatrix());
                    break;
                case BuiltInType.Variant:
                    AssertSameMatrix(expected.GetVariantMatrix(), actual.GetVariantMatrix());
                    break;
            }
        }

        private static void AssertSameMatrix<T>(MatrixOf<T> expected, MatrixOf<T> actual)
        {
            Assert.That(actual.IsNull, Is.EqualTo(expected.IsNull), "null");
            if (expected.IsNull)
            {
                return;
            }
            // An inline matrix has at least two dimensions, the empty MatrixOf
            // (a single zero dimension) comes back as the 0 x 0 matrix.
            int[] dimensions = expected.Dimensions.Length < 2 && expected.Count == 0
                ? [0, 0]
                : expected.Dimensions;
            Assert.That(actual.Dimensions, Is.EqualTo(dimensions), "dimensions");
            Assert.That(actual.Span.ToArray(), Is.EqualTo(expected.Span.ToArray()), "values");
        }

        private static Variant RoundTrip(Codec codec, Variant value, TypeInfo typeInfo)
        {
            ServiceMessageContext context = CreateContext();
            Variant decoded;
            switch (codec)
            {
                case Codec.Binary:
                {
                    using var decoder = new BinaryDecoder(EncodeBinary(value), context);
                    decoded = ReadField(decoder, typeInfo);
                    break;
                }
                case Codec.Json:
                {
                    string json;
                    using (var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose))
                    {
                        WriteField(encoder, value);
                        json = encoder.CloseAndReturnText();
                    }
                    using var decoder = new JsonDecoder(json, context);
                    decoded = ReadField(decoder, typeInfo);
                    break;
                }
                default:
                {
                    // The XML fields need an enclosing element.
                    string xml;
                    using (var encoder = new XmlEncoder(context))
                    {
                        encoder.PushNamespace(Namespaces.OpcUaXsd);
                        encoder.WriteEncodeable("Root", new FieldHolder { Value = value });
                        encoder.PopNamespace();
                        xml = encoder.CloseAndReturnText();
                    }
                    FieldHolder.ReadTypeInfo = typeInfo;
                    FieldHolder holder;
                    if (codec == Codec.XmlParser)
                    {
                        using var parser = new XmlParser(xml, context);
                        parser.PushNamespace(Namespaces.OpcUaXsd);
                        holder = parser.ReadEncodeable<FieldHolder>("Root");
                        parser.PopNamespace();
                    }
                    else
                    {
                        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
                        using var reader = XmlReader.Create(stream, CoreUtils.DefaultXmlReaderSettings());
                        using var xmlDecoder = new XmlDecoder(reader, context);
                        xmlDecoder.PushNamespace(Namespaces.OpcUaXsd);
                        holder = xmlDecoder.ReadEncodeable<FieldHolder>("Root");
                        xmlDecoder.PopNamespace();
                    }
                    Assert.That(holder, Is.Not.Null);
                    decoded = holder.Value;
                    break;
                }
            }
            return decoded;
        }

        private static byte[] EncodeBinary(Variant value)
        {
            using var encoder = new BinaryEncoder(CreateContext());
            WriteField(encoder, value);
            return encoder.CloseAndReturnBuffer();
        }

        private static void WriteField(IEncoder encoder, Variant value)
        {
            encoder.WriteInt32("A", 7);
            encoder.WriteInlineMatrixValue("M", value);
            encoder.WriteInt32("B", 9);
        }

        private static Variant ReadField(IDecoder decoder, TypeInfo typeInfo)
        {
            Assert.That(decoder.ReadInt32("A"), Is.EqualTo(7));
            Variant value = decoder.ReadVariantValue("M", typeInfo);
            Assert.That(decoder.ReadInt32("B"), Is.EqualTo(9), "the field after the matrix");
            return value;
        }

        private static byte[] Int32s(params int[] values)
        {
            return values.SelectMany(BitConverter.GetBytes).ToArray();
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }

        /// <summary>
        /// A structure with a single Int32 field.
        /// </summary>
        public sealed class Pair : IEncodeable
        {
            public int X { get; set; }

            public ExpandedNodeId TypeId => new(77790, 0);
            public ExpandedNodeId BinaryEncodingId => new(77791, 0);
            public ExpandedNodeId XmlEncodingId => new(77792, 0);

            public void Encode(IEncoder encoder)
            {
                encoder.WriteInt32("X", X);
            }

            public void Decode(IDecoder decoder)
            {
                X = decoder.ReadInt32("X");
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is Pair other && other.X == X;
            }

            public object Clone()
            {
                return new Pair { X = X };
            }
        }

        /// <summary>
        /// Encloses the matrix field and its neighbours in a structure.
        /// </summary>
        public sealed class FieldHolder : IEncodeable
        {
            [ThreadStatic]
            internal static TypeInfo ReadTypeInfo;

            public Variant Value { get; set; }

            public ExpandedNodeId TypeId => new(77787, 0);
            public ExpandedNodeId BinaryEncodingId => new(77788, 0);
            public ExpandedNodeId XmlEncodingId => new(77789, 0);

            public void Encode(IEncoder encoder)
            {
                WriteField(encoder, Value);
            }

            public void Decode(IDecoder decoder)
            {
                Value = ReadField(decoder, ReadTypeInfo);
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return ReferenceEquals(this, encodeable);
            }

            public object Clone()
            {
                return new FieldHolder { Value = Value };
            }
        }
    }
}
