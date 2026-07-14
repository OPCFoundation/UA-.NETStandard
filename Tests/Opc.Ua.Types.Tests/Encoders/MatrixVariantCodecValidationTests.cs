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
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Verifies that the Binary, JSON and XML codecs enforce the OPC UA Part 6
    /// 5.2.2.16 rules for multi-dimensional array (matrix) Variants consistently:
    /// ArrayDimensions are only written for rank &gt;= 2 matrices, every dimension
    /// is greater than zero, and the product of the dimensions equals the
    /// flattened element count. Encoders must refuse to emit inconsistent
    /// dimensions with BadEncodingError and decoders must reject them with
    /// BadDecodingError instead of silently accepting or crashing. The XML side
    /// covers both decoder implementations: the streaming
    /// <see cref="XmlDecoder"/> and the in-memory <see cref="XmlParser"/>.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MatrixVariantCodecValidationTests
    {
        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }

        private static Variant ValidMatrixVariant()
        {
            return Variant.From(new int[,] { { 1, 2, 3 }, { 4, 5, 6 } });
        }

        private static Variant DegenerateMatrixVariant()
        {
            return Variant.From(new int[2, 0]);
        }

        private const string ZeroDimensionMatrixXml =
            "<v xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
            "<Value><Matrix><Dimensions><Int32>2</Int32><Int32>0</Int32>" +
            "</Dimensions><Elements /></Matrix></Value></v>";

        private const string ProductMismatchMatrixXml =
            "<v xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
            "<Value><Matrix><Dimensions><Int32>2</Int32><Int32>3</Int32>" +
            "</Dimensions><Elements><Int32>1</Int32><Int32>2</Int32>" +
            "</Elements></Matrix></Value></v>";

        [Test]
        public void IsValidMatrixAcceptsWellFormedDimensions()
        {
            Assert.Multiple(() =>
            {
                Assert.That(MatrixOf.IsValidMatrix([2, 3]), Is.True);
                Assert.That(MatrixOf.IsValidMatrix([2, 3], 6), Is.True);
                Assert.That(MatrixOf.IsValidMatrix([2, 2, 2], 8), Is.True);
            });
        }

        [Test]
        public void IsValidMatrixRejectsInvalidDimensions()
        {
            Assert.Multiple(() =>
            {
                Assert.That(MatrixOf.IsValidMatrix(null), Is.False);
                Assert.That(MatrixOf.IsValidMatrix([]), Is.False);
                Assert.That(MatrixOf.IsValidMatrix([4]), Is.False, "rank < 2");
                Assert.That(MatrixOf.IsValidMatrix([2, 0]), Is.False, "zero dimension");
                Assert.That(MatrixOf.IsValidMatrix([0, 3]), Is.False, "zero dimension");
                Assert.That(MatrixOf.IsValidMatrix([-1, 2]), Is.False, "negative dimension");
                Assert.That(MatrixOf.IsValidMatrix([2, 3], 5), Is.False, "product != count");
                Assert.That(MatrixOf.IsValidMatrix([65536, 65537], 1), Is.False, "overflow");
            });
        }

        [Test]
        public void BinaryEncodeValidMatrixRoundTrips()
        {
            ServiceMessageContext ctx = CreateContext();
            byte[] encoded;
            using (var encoder = new BinaryEncoder(ctx))
            {
                encoder.WriteVariant(null, ValidMatrixVariant());
                encoded = encoder.CloseAndReturnBuffer();
            }
            using var decoder = new BinaryDecoder(encoded, ctx);
            MatrixOf<int> matrix = decoder.ReadVariant(null).GetInt32Matrix();

            Assert.That(matrix.Dimensions, Is.EqualTo([2, 3]));
            Assert.That(matrix.Count, Is.EqualTo(6));
        }

        [Test]
        public void BinaryEncodeDegenerateMatrixThrowsBadEncodingError()
        {
            ServiceMessageContext ctx = CreateContext();
            using var encoder = new BinaryEncoder(ctx);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteVariant(null, DegenerateMatrixVariant()));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void BinaryEncodeDegenerateMatrixRawRoundTrips()
        {
            ServiceMessageContext ctx = CreateContext();
            byte[] encoded;
            using (var encoder = new BinaryEncoder(ctx))
            {
                encoder.WriteVariantValue(null, DegenerateMatrixVariant());
                encoded = encoder.CloseAndReturnBuffer();
            }
            using var decoder = new BinaryDecoder(encoded, ctx);
            MatrixOf<int> matrix = decoder.ReadVariantValue(
                null,
                TypeInfo.Create(BuiltInType.Int32, ValueRanks.TwoDimensions)).GetInt32Matrix();

            Assert.That(matrix.Dimensions, Is.EqualTo([2, 0]));
            Assert.That(matrix.Count, Is.Zero);
        }

        [Test]
        public void BinaryDecodeZeroDimensionMatrixThrowsBadDecodingError()
        {
            // EncodingMask Int32|Array|ArrayDimensions, ArrayLength 0,
            // ArrayDimensionsLength 2, ArrayDimensions [2,0].
            byte[] bytes =
            [
                0xC6,
                0x00, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00
            ];
            ServiceMessageContext ctx = CreateContext();
            using var decoder = new BinaryDecoder(bytes, ctx);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariant(null));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void BinaryDecodeDimensionsProductMismatchThrowsBadDecodingError()
        {
            // ArrayLength 2 but ArrayDimensions [2,3] (product 6 != 2).
            byte[] bytes =
            [
                0xC6,
                0x02, 0x00, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0x03, 0x00, 0x00, 0x00
            ];
            ServiceMessageContext ctx = CreateContext();
            using var decoder = new BinaryDecoder(bytes, ctx);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariant(null));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void BinaryDecodeNegativeDimensionThrowsBadDecodingError()
        {
            // ArrayLength 0, ArrayDimensionsLength 1, ArrayDimensions [-1].
            byte[] bytes =
            [
                0xC6,
                0x00, 0x00, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00,
                0xFF, 0xFF, 0xFF, 0xFF
            ];
            ServiceMessageContext ctx = CreateContext();
            using var decoder = new BinaryDecoder(bytes, ctx);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariant(null));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void JsonEncodeValidMatrixRoundTrips()
        {
            ServiceMessageContext ctx = CreateContext();
            string json;
            using (var encoder = new JsonEncoder(ctx))
            {
                encoder.WriteVariant("v", ValidMatrixVariant());
                json = encoder.CloseAndReturnText();
            }
            using var decoder = new JsonDecoder(json, ctx);
            MatrixOf<int> matrix = decoder.ReadVariant("v").GetInt32Matrix();

            Assert.That(matrix.Dimensions, Is.EqualTo([2, 3]));
            Assert.That(matrix.Count, Is.EqualTo(6));
        }

        [Test]
        public void JsonEncodeDegenerateMatrixThrowsBadEncodingError()
        {
            ServiceMessageContext ctx = CreateContext();
            using var encoder = new JsonEncoder(ctx);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteVariant("v", DegenerateMatrixVariant()));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void JsonDecodeZeroDimensionMatrixThrowsBadDecodingError()
        {
            ServiceMessageContext ctx = CreateContext();
            using var decoder = new JsonDecoder(
                /*lang=json,strict*/ """{"v":{"UaType":6,"Value":[],"Dimensions":[2,0]}}""",
                ctx);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariant("v"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void JsonDecodeDimensionsProductMismatchThrowsBadDecodingError()
        {
            ServiceMessageContext ctx = CreateContext();
            using var decoder = new JsonDecoder(
                /*lang=json,strict*/ """{"v":{"UaType":6,"Value":[1,2],"Dimensions":[2,3]}}""",
                ctx);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariant("v"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void JsonDecodeNegativeDimensionThrowsBadDecodingError()
        {
            ServiceMessageContext ctx = CreateContext();
            using var decoder = new JsonDecoder(
                /*lang=json,strict*/ """{"v":{"UaType":6,"Value":[],"Dimensions":[-1,2]}}""",
                ctx);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariant("v"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void XmlEncodeValidMatrixRoundTrips()
        {
            ServiceMessageContext ctx = CreateContext();
            string xml = EncodeXmlVariant(ctx, ValidMatrixVariant());

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            using var reader = XmlReader.Create(
                stream,
                CoreUtils.DefaultXmlReaderSettings());
            using var decoder = new XmlDecoder(reader, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);
            MatrixOf<int> matrix = decoder.ReadVariant("v").GetInt32Matrix();
            decoder.PopNamespace();

            Assert.That(matrix.Dimensions, Is.EqualTo([2, 3]));
            Assert.That(matrix.Count, Is.EqualTo(6));
        }

        [Test]
        public void XmlEncodeDegenerateMatrixThrowsBadEncodingError()
        {
            ServiceMessageContext ctx = CreateContext();
            using var encoder = new XmlEncoder(ctx);
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteVariant("v", DegenerateMatrixVariant()));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void XmlDecodeZeroDimensionMatrixThrowsBadDecodingError()
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => DecodeXmlVariant(ZeroDimensionMatrixXml));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void XmlDecodeDimensionsProductMismatchThrowsBadDecodingError()
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => DecodeXmlVariant(ProductMismatchMatrixXml));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void XmlParserDecodeValidMatrixRoundTrips()
        {
            ServiceMessageContext ctx = CreateContext();
            string xml = EncodeXmlVariant(ctx, ValidMatrixVariant());

            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);
            MatrixOf<int> matrix = decoder.ReadVariant("v").GetInt32Matrix();
            decoder.PopNamespace();

            Assert.That(matrix.Dimensions, Is.EqualTo([2, 3]));
            Assert.That(matrix.Count, Is.EqualTo(6));
        }

        [Test]
        public void XmlParserDecodeZeroDimensionMatrixThrowsBadDecodingError()
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => DecodeXmlParserVariant(ZeroDimensionMatrixXml));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void XmlParserDecodeDimensionsProductMismatchThrowsBadDecodingError()
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => DecodeXmlParserVariant(ProductMismatchMatrixXml));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void XmlParserDecodeDataValueMatrixZeroDimensionThrowsBadDecodingError()
        {
            byte[] payload = MutateDataValueXmlDimensions(
                EncodeDataValueXml(ValidMatrixVariant()),
                [2, 0]);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => DecodeDataValueViaXmlParser(payload));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void XmlParserDecodeDataValueMatrixProductMismatchThrowsBadDecodingError()
        {
            byte[] payload = MutateDataValueXmlDimensions(
                EncodeDataValueXml(ValidMatrixVariant()),
                [2, 2]);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => DecodeDataValueViaXmlParser(payload));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        private static string EncodeXmlVariant(ServiceMessageContext ctx, Variant variant)
        {
            using var encoder = new XmlEncoder(ctx);
            encoder.PushNamespace(Namespaces.OpcUaXsd);
            encoder.WriteVariant("v", variant);
            encoder.PopNamespace();
            return encoder.CloseAndReturnText();
        }

        private static void DecodeXmlVariant(string xml)
        {
            ServiceMessageContext ctx = CreateContext();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            using var reader = XmlReader.Create(
                stream,
                CoreUtils.DefaultXmlReaderSettings());
            using var decoder = new XmlDecoder(reader, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);
            decoder.ReadVariant("v");
            decoder.PopNamespace();
        }

        private static void DecodeXmlParserVariant(string xml)
        {
            ServiceMessageContext ctx = CreateContext();
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);
            decoder.ReadVariant("v");
            decoder.PopNamespace();
        }

        private static byte[] EncodeDataValueXml(Variant variant)
        {
            ServiceMessageContext ctx = CreateContext();
            using var stream = new MemoryStream();
            using (var writer = XmlWriter.Create(
                stream,
                new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
            using (var encoder = new XmlEncoder(typeof(DataValue), writer, ctx))
            {
                encoder.WriteDataValue("DataValue", new DataValue(variant));
            }
            return stream.ToArray();
        }

        private static byte[] MutateDataValueXmlDimensions(byte[] payload, int[] dimensions)
        {
            using var stream = new MemoryStream(payload);
            XDocument document = XDocument.Load(stream);
            XElement dimensionsElement = document.Root?
                .DescendantsAndSelf()
                .FirstOrDefault(element => element.Name.LocalName == "Dimensions")
                ?? throw new InvalidOperationException(
                    "The encoded XML payload has no Dimensions element.");
            XNamespace ns = dimensionsElement.Name.Namespace;
            dimensionsElement.ReplaceNodes(
                dimensions.Select(value => new XElement(ns + "Int32", value)).ToArray());
            return Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
        }

        private static void DecodeDataValueViaXmlParser(byte[] payload)
        {
            ServiceMessageContext ctx = CreateContext();
            using var stream = new MemoryStream(payload);
            using var decoder = new XmlParser(typeof(DataValue), stream, ctx);
            decoder.ReadDataValue("DataValue");
        }
    }
}
