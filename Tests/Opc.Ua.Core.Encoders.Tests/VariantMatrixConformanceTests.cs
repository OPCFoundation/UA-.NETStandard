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
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Core.TestFramework;

namespace Opc.Ua.Core.Encoders.Tests
{
    /// <summary>
    /// Verifies consistent Variant matrix validation across all supported codecs.
    /// </summary>
    [TestFixture]
    [Category("Encoder")]
    [Category("Matrix")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VariantMatrixConformanceTests : EncoderCommon
    {
        [Theory]
        public void Int32MatrixRoundTripsAcrossCodecs(
            [ValueSource(nameof(EncodingTypesAll))] EncodingTypeGroup encoding)
        {
            MatrixOf<int> expected = new int[2, 3]
            {
                { 1, 2, 3 },
                { 4, 5, 6 }
            };

            Variant actual = RoundTrip(encoding, Variant.From(expected));
            MatrixOf<int> actualMatrix = actual.GetInt32Matrix();

            Assert.That(actualMatrix.Dimensions, Has.Length.EqualTo(2));
            Assert.That(actualMatrix.Dimensions[0], Is.EqualTo(2));
            Assert.That(actualMatrix.Dimensions[1], Is.EqualTo(3));
            Assert.That(actualMatrix.Count, Is.EqualTo(expected.Count));
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.That(actualMatrix.Span[i], Is.EqualTo(expected.Span[i]));
            }
        }

        [Theory]
        public void NullInt32ArrayDecodesAsNoElementsAcrossCodecs(
            [ValueSource(nameof(EncodingTypesAll))] EncodingTypeGroup encoding)
        {
            Variant actual = RoundTripVariantValue(encoding, Variant.From(ArrayOf<int>.Null));

            Assert.That(actual.TypeInfo, Is.EqualTo(TypeInfo.Arrays.Int32));
            Assert.That(actual.GetInt32Array().Count, Is.Zero);
        }

        [Theory]
        public void EmptyInt32ArrayRoundTripsAcrossCodecs(
            [ValueSource(nameof(EncodingTypesAll))] EncodingTypeGroup encoding)
        {
            Variant actual = RoundTripVariantValue(encoding, Variant.From(ArrayOf<int>.Empty));

            Assert.That(actual.TypeInfo, Is.EqualTo(TypeInfo.Arrays.Int32));
            Assert.That(actual.GetInt32Array().IsNull, Is.False);
            Assert.That(actual.GetInt32Array().Count, Is.Zero);
        }

        [Theory]
        public void RankOneMatrixDimensionsAreRejectedAcrossCodecs(
            [ValueSource(nameof(EncodingTypesAll))] EncodingTypeGroup encoding)
        {
            AssertInvalidDimensions(encoding, [6]);
        }

        [Theory]
        public void ZeroMatrixDimensionIsRejectedAcrossCodecs(
            [ValueSource(nameof(EncodingTypesAll))] EncodingTypeGroup encoding)
        {
            AssertInvalidDimensions(encoding, [2, 0]);
        }

        [Theory]
        public void MatrixDimensionProductMismatchIsRejectedAcrossCodecs(
            [ValueSource(nameof(EncodingTypesAll))] EncodingTypeGroup encoding)
        {
            AssertInvalidDimensions(encoding, [2, 2]);
        }

        private Variant RoundTrip(EncodingTypeGroup encoding, Variant value)
        {
            byte[] payload = EncodeDataValue(encoding, value);
            return DecodeDataValue(encoding, payload).WrappedValue;
        }

        private Variant RoundTripVariantValue(EncodingTypeGroup encoding, Variant value)
        {
            using var stream = new MemoryStream();
            using (IEncoder encoder = CreateEncoder(
                encoding.EncoderType,
                Context,
                stream,
                typeof(int),
                encoding.JsonEncodingType))
            {
                encoder.WriteVariantValue("Value", value);
            }

            stream.Position = 0;
            using IDecoder decoder = CreateDecoder(
                encoding.EncoderType,
                encoding.UseXmlParser,
                Context,
                stream,
                typeof(int));
            return decoder.ReadVariantValue("Value", value.TypeInfo);
        }

        private void AssertInvalidDimensions(EncodingTypeGroup encoding, int[] dimensions)
        {
            MatrixOf<int> matrix = new int[2, 3]
            {
                { 1, 2, 3 },
                { 4, 5, 6 }
            };
            byte[] payload = EncodeDataValue(encoding, Variant.From(matrix));
            byte[] invalidPayload = ReplaceDimensions(encoding.EncoderType, payload, dimensions);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => DecodeDataValue(encoding, invalidPayload));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        private byte[] EncodeDataValue(EncodingTypeGroup encoding, Variant value)
        {
            using var stream = new MemoryStream();
            using (IEncoder encoder = CreateEncoder(
                encoding.EncoderType,
                Context,
                stream,
                typeof(DataValue),
                encoding.JsonEncodingType))
            {
                encoder.WriteDataValue("DataValue", new DataValue(value));
            }
            return stream.ToArray();
        }

        private DataValue DecodeDataValue(EncodingTypeGroup encoding, byte[] payload)
        {
            using var stream = new MemoryStream(payload);
            using IDecoder decoder = CreateDecoder(
                encoding.EncoderType,
                encoding.UseXmlParser,
                Context,
                stream,
                typeof(DataValue));
            return decoder.ReadDataValue("DataValue");
        }

        private static byte[] ReplaceDimensions(
            EncodingType encodingType,
            byte[] payload,
            int[] dimensions)
        {
            return encodingType switch
            {
                EncodingType.Binary => ReplaceBinaryDimensions(payload, dimensions),
                EncodingType.Json => ReplaceJsonDimensions(payload, dimensions),
                EncodingType.Xml => ReplaceXmlDimensions(payload, dimensions),
                _ => throw new ArgumentOutOfRangeException(nameof(encodingType), encodingType, null)
            };
        }

        private static byte[] ReplaceBinaryDimensions(byte[] payload, int[] dimensions)
        {
            const int originalDimensionCount = 2;
            int originalDimensionBytes = sizeof(int) * (originalDimensionCount + 1);
            int prefixLength = payload.Length - originalDimensionBytes;
            Assert.That(
                BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(prefixLength, sizeof(int))),
                Is.EqualTo(originalDimensionCount));

            byte[] result = new byte[prefixLength + (sizeof(int) * (dimensions.Length + 1))];
            payload.AsSpan(0, prefixLength).CopyTo(result);
            Span<byte> destination = result.AsSpan(prefixLength);
            BinaryPrimitives.WriteInt32LittleEndian(destination, dimensions.Length);
            for (int i = 0; i < dimensions.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    destination[((i + 1) * sizeof(int))..],
                    dimensions[i]);
            }
            return result;
        }

        private static byte[] ReplaceJsonDimensions(byte[] payload, int[] dimensions)
        {
            JsonNode root = JsonNode.Parse(payload)
                ?? throw new InvalidOperationException("The encoded JSON payload is empty.");
            JsonObject owner = FindJsonDimensionsOwner(root)
                ?? throw new InvalidOperationException("The encoded JSON payload has no Dimensions property.");
            var replacement = new JsonArray();
            for (int i = 0; i < dimensions.Length; i++)
            {
                replacement.Add(dimensions[i]);
            }
            owner["Dimensions"] = replacement;
            return JsonSerializer.SerializeToUtf8Bytes(root);
        }

        private static JsonObject FindJsonDimensionsOwner(JsonNode node)
        {
            if (node is JsonObject jsonObject)
            {
                if (jsonObject.ContainsKey("Dimensions"))
                {
                    return jsonObject;
                }
                foreach ((_, JsonNode child) in jsonObject)
                {
                    if (child != null && FindJsonDimensionsOwner(child) is JsonObject owner)
                    {
                        return owner;
                    }
                }
            }
            else if (node is JsonArray jsonArray)
            {
                for (int i = 0; i < jsonArray.Count; i++)
                {
                    if (jsonArray[i] != null &&
                        FindJsonDimensionsOwner(jsonArray[i]) is JsonObject owner)
                    {
                        return owner;
                    }
                }
            }
            return null;
        }

        private static byte[] ReplaceXmlDimensions(byte[] payload, int[] dimensions)
        {
            using var stream = new MemoryStream(payload);
            XDocument document = XDocument.Load(stream);
            XElement dimensionsElement = document.Root?
                .DescendantsAndSelf()
                .FirstOrDefault(element => element.Name.LocalName == "Dimensions")
                ?? throw new InvalidOperationException("The encoded XML payload has no Dimensions element.");
            XNamespace ns = dimensionsElement.Name.Namespace;
            var elements = new XElement[dimensions.Length];
            for (int i = 0; i < dimensions.Length; i++)
            {
                elements[i] = new XElement(ns + "Int32", dimensions[i]);
            }
            dimensionsElement.ReplaceNodes(elements);
            return Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
        }
    }
}
