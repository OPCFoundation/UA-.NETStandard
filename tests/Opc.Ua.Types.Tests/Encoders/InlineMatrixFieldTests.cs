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
        /// A null matrix is encoded the way WriteEncodeableMatrix encodes one:
        /// a null dimensions array and a null values array.
        /// </summary>
        [TestCase(Shape.Null)]
        [TestCase(Shape.TypedNull)]
        public void BinaryNullInlineMatrixIsNullDimensionsAndValues(Shape shape)
        {
            byte[] encoded = EncodeBinary(Create(BuiltInType.Double, shape));

            Assert.That(encoded, Is.EqualTo(Int32s(7, -1, -1, 9)));
        }

        /// <summary>
        /// A populated matrix is the dimensions array followed by the values,
        /// without the Variant encoding byte.
        /// </summary>
        [Test]
        public void BinaryInlineMatrixIsDimensionsThenValues()
        {
            Variant value = Variant.From(new int[] { 1, 2, 3, 4, 5, 6 }.ToArrayOf().ToMatrix(2, 3));

            byte[] encoded = EncodeBinary(value);

            Assert.That(encoded, Is.EqualTo(Int32s(7, 2, 2, 3, 6, 1, 2, 3, 4, 5, 6, 9)));
        }

        /// <summary>
        /// An empty matrix keeps its dimensions and carries no values.
        /// </summary>
        [Test]
        public void BinaryEmptyInlineMatrixKeepsItsDimensions()
        {
            Variant value = Variant.From(Array.Empty<int>().ToArrayOf().ToMatrix(0, 3));

            byte[] encoded = EncodeBinary(value);

            Assert.That(encoded, Is.EqualTo(Int32s(7, 2, 0, 3, 0, 9)));
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
        /// Earlier versions wrote a null matrix of a DataTypeDefinition driven
        /// structure as an empty dimensions array and a null values array;
        /// that is still read as a null matrix.
        /// </summary>
        [Test]
        public void BinaryLegacyEmptyDimensionsReadAsNullMatrix()
        {
            ServiceMessageContext context = CreateContext();
            using var decoder = new BinaryDecoder(Int32s(7, 0, -1, 9), context);

            Assert.That(decoder.ReadInt32("A"), Is.EqualTo(7));
            Variant value = decoder.ReadVariantValue(
                "M",
                TypeInfo.Create(BuiltInType.Double, ValueRanks.TwoDimensions));
            Assert.That(decoder.ReadInt32("B"), Is.EqualTo(9));
            Assert.That(value.GetDoubleMatrix().IsNull, Is.True);
        }

        /// <summary>
        /// Inline matrix dimensions are checked against the values.
        /// </summary>
        [Test]
        public void BinaryInlineMatrixWithInconsistentDimensionsIsRejected()
        {
            ServiceMessageContext context = CreateContext();
            using var decoder = new BinaryDecoder(Int32s(2, 2, 3, 2, 1, 2), context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariantValue(
                    "M",
                    TypeInfo.Create(BuiltInType.Int32, ValueRanks.TwoDimensions)));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
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
            Assert.That(actual.Dimensions, Is.EqualTo(expected.Dimensions), "dimensions");
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
            encoder.WriteVariantValue("M", value);
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
