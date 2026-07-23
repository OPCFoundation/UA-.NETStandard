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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Core.TestFramework;

#pragma warning disable UA_NETStandard_Avro // experimental encoder surface under test
#pragma warning disable UA_NETStandard_Arrow // experimental encoder surface under test

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
                EncodingType.Avro => ReplaceAvroDimensions(payload, dimensions),
                EncodingType.Arrow => ReplaceArrowDimensions(payload, dimensions),
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
                foreach (KeyValuePair<string, JsonNode> property in jsonObject)
                {
                    JsonNode child = property.Value;
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

        // The value under test is a 2x3 Int32 matrix {1,2,3,4,5,6}. On the Avro wire the
        // Variant matrix body is a MatrixBody record { dimensions: plain int array, values:
        // plain int array }. The dimensions plain array is located by the unique byte
        // signature formed by the original dimensions immediately followed by the values
        // plain array, and only that dimensions prefix is rewritten.
        private static byte[] ReplaceAvroDimensions(byte[] payload, int[] dimensions)
        {
            byte[] originalDimensions = EncodeAvroIntArray([2, 3]);
            byte[] values = EncodeAvroIntArray([1, 2, 3, 4, 5, 6]);
            byte[] signature = new byte[originalDimensions.Length + values.Length];
            originalDimensions.CopyTo(signature, 0);
            values.CopyTo(signature, originalDimensions.Length);

            int at = IndexOf(payload, signature);
            Assert.That(
                at,
                Is.GreaterThanOrEqualTo(0),
                "The encoded Avro payload does not contain the expected matrix body.");

            byte[] newDimensions = EncodeAvroIntArray(dimensions);
            int remainderStart = at + originalDimensions.Length;
            byte[] result = new byte[payload.Length - originalDimensions.Length + newDimensions.Length];
            Array.Copy(payload, 0, result, 0, at);
            newDimensions.CopyTo(result, at);
            Array.Copy(
                payload,
                remainderStart,
                result,
                at + newDimensions.Length,
                payload.Length - remainderStart);
            return result;
        }

        // The Arrow matrix Struct stores dimensions as a List&lt;int32&gt; child: an offsets
        // buffer [0, 2] followed by a values buffer [2, 3] for the 2x3 matrix under test.
        // The dimensions are corrupted in place (never changing any buffer size, so the
        // IPC record-batch metadata stays valid): a two-dimension replacement rewrites the
        // values, and a rank-one replacement shrinks the list to a single element via its
        // offset and rewrites the first value.
        private static byte[] ReplaceArrowDimensions(byte[] payload, int[] dimensions)
        {
            byte[] result = (byte[])payload.Clone();
            byte[] originalValues = [0x02, 0, 0, 0, 0x03, 0, 0, 0];

            int valuesOffset = -1;
            for (int i = 4; i + originalValues.Length <= result.Length; i++)
            {
                if (BytesMatch(result, i, originalValues) &&
                    BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(i - 4)) == 0)
                {
                    valuesOffset = i;
                    break;
                }
            }
            Assert.That(
                valuesOffset,
                Is.GreaterThanOrEqualTo(0),
                "The encoded Arrow payload does not contain the expected dimensions values buffer.");

            // The list length lives in the offsets buffer as the last non-zero int32 that
            // precedes the (padding-separated) values buffer.
            int lengthOffset = valuesOffset - 4;
            while (lengthOffset >= 4 &&
                BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(lengthOffset)) == 0)
            {
                lengthOffset -= sizeof(int);
            }
            Assert.That(
                BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(lengthOffset)),
                Is.EqualTo(2),
                "Unexpected Arrow dimensions offsets layout.");

            if (dimensions.Length == 1)
            {
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(lengthOffset), 1);
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(valuesOffset), dimensions[0]);
            }
            else
            {
                for (int i = 0; i < dimensions.Length; i++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(
                        result.AsSpan(valuesOffset + (i * sizeof(int))),
                        dimensions[i]);
                }
            }
            return result;
        }

        private static bool BytesMatch(byte[] data, int offset, byte[] pattern)
        {
            for (int i = 0; i < pattern.Length; i++)
            {
                if (data[offset + i] != pattern[i])
                {
                    return false;
                }
            }
            return true;
        }

        // Encodes an Avro plain array&lt;int&gt;: zig-zag block count, zig-zag items, 0 terminator.
        private static byte[] EncodeAvroIntArray(int[] values)
        {
            var buffer = new List<byte>();
            WriteAvroLong(buffer, values.Length);
            foreach (int value in values)
            {
                WriteAvroLong(buffer, value);
            }
            buffer.Add(0x00);
            return buffer.ToArray();
        }

        private static void WriteAvroLong(List<byte> buffer, long value)
        {
            ulong zig = (ulong)((value << 1) ^ (value >> 63));
            while ((zig & ~0x7FUL) != 0)
            {
                buffer.Add((byte)((zig & 0x7F) | 0x80));
                zig >>= 7;
            }
            buffer.Add((byte)zig);
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
