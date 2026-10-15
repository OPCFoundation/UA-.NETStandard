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
using System.Buffers;

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// Applies RawData element padding to native Part 6 matrices without discarding their dimensions.
    /// </summary>
    internal static class UadpRawMatrix
    {
        /// <summary>
        /// Writes the actual dimensions and elements, followed by metadata-required zero padding.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static void Write(
            ref UadpBinaryWriter writer, in Variant value, BuiltInType type, int rank,
            uint maxStringLength, ArrayOf<uint> maxima, IServiceMessageContext context)
        {
            bool nullMatrix = value.IsNull ||
                (value.TypeInfo.ValueRank == ValueRanks.OneOrMoreDimensions && value.ValueIsDefaultOrNull);
            if (!value.IsNull && ((!nullMatrix && !value.TypeInfo.IsMatrix) || value.TypeInfo.BuiltInType != type))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "RawData requires the declared matrix type.");
            }
            int capacity = Capacity(writer.Remaining, context);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);
            try
            {
                int length;
                using (var encoder = new BinaryEncoder(buffer, 0, capacity, context))
                {
                    if (nullMatrix)
                    {
                        encoder.WriteInt32Array(null, ArrayOf<int>.Null);
                    }
                    else
                    {
                        encoder.WriteVariantValue(null, value);
                    }
                    length = encoder.Close();
                }
                using var decoder = new BinaryDecoder(buffer, 0, length, context);
                ArrayOf<int> dimensions = decoder.ReadInt32Array(null);
                int count = Validate(dimensions, maxima, rank, context);
                WriteDimensions(ref writer, dimensions);
                var scalar = TypeInfo.Create(type, ValueRanks.Scalar);
                for (int index = 0; index < count; index++)
                {
                    Variant element = decoder.ReadVariantValue(null, scalar);
                    writer.WriteRawScalar(element, type, ValueRanks.Scalar, maxStringLength, default, context);
                }
                int padding = PaddingBytes(type, maxStringLength, maxima, count);
                _ = writer.Reserve(padding);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }

        /// <summary>
        /// Reads the actual dimensions and retains matrix shape while skipping only trailing padding.
        /// </summary>
        public static Variant Read(
            ref UadpBinaryReader reader, BuiltInType type, int rank, uint maxStringLength,
            ArrayOf<uint> maxima, IServiceMessageContext context)
        {
            int capacity = Capacity(reader.Remaining, context);
            ArrayOf<int> dimensions;
            using (var header = new BinaryDecoder(
                reader.Buffer, reader.Origin + reader.Position, reader.Remaining, context))
            {
                dimensions = header.ReadInt32Array(null);
                reader.Advance(header.Position);
            }
            int count = Validate(dimensions, maxima, rank, context);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);
            try
            {
                int length;
                using (var encoder = new BinaryEncoder(buffer, 0, capacity, context))
                {
                    encoder.WriteInt32Array(null, dimensions);
                    for (int index = 0; index < count; index++)
                    {
                        Variant element = reader.ReadRawScalar(
                            type, ValueRanks.Scalar, maxStringLength, default, context);
                        encoder.WriteVariantValue(null, element);
                    }
                    length = encoder.Close();
                }
                reader.Advance(PaddingBytes(type, maxStringLength, maxima, count));
                using var decoder = new BinaryDecoder(buffer, 0, length, context);
                return decoder.ReadVariantValue(null, TypeInfo.Create(type, rank));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }

        private static int Validate(
            ArrayOf<int> dimensions, ArrayOf<uint> maxima, int rank, IServiceMessageContext context)
        {
            if (rank is < 2 or > 32 ||
                (!maxima.IsEmpty && maxima.Count != rank) ||
                (!dimensions.IsNull && dimensions.Count != rank))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "RawData matrix rank does not match metadata.");
            }
            long count = dimensions.IsNull ? 0 : 1;
            for (int index = 0; index < dimensions.Count; index++)
            {
                int dimension = dimensions[index];
                if (dimension < 0 || (!maxima.IsEmpty && maxima[index] != 0 && dimension > maxima[index]))
                {
                    throw new ServiceResultException(StatusCodes.BadOutOfRange,
                        "A RawData matrix dimension exceeds its declared maximum.");
                }
                count *= dimension;
                if (count > int.MaxValue || (context.MaxArrayLength > 0 && count > context.MaxArrayLength))
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
            }
            return (int)count;
        }

        private static void WriteDimensions(ref UadpBinaryWriter writer, ArrayOf<int> dimensions)
        {
            writer.WriteUInt32Le(dimensions.IsNull ? uint.MaxValue : (uint)dimensions.Count);
            foreach (int dimension in dimensions)
            {
                writer.WriteUInt32Le((uint)dimension);
            }
        }

        private static int PaddingBytes(BuiltInType type, uint maxStringLength, ArrayOf<uint> maxima, int count)
        {
            if (maxima.IsEmpty)
            {
                return 0;
            }
            long expected = 1;
            foreach (uint maximum in maxima)
            {
                if (maximum == 0)
                {
                    return 0;
                }
                expected *= maximum;
                if (expected > int.MaxValue)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
            }
            int size = type switch
            {
                BuiltInType.Boolean or BuiltInType.SByte or BuiltInType.Byte => 1,
                BuiltInType.Int16 or BuiltInType.UInt16 => 2,
                BuiltInType.Int32 or BuiltInType.UInt32 or BuiltInType.Float or BuiltInType.StatusCode => 4,
                BuiltInType.Int64 or BuiltInType.UInt64 or BuiltInType.Double or BuiltInType.DateTime => 8,
                BuiltInType.Guid => 16,
                BuiltInType.String or BuiltInType.ByteString or BuiltInType.XmlElement when maxStringLength > 0 =>
                    UadpBinaryWriter.GetPaddedStringSize(maxStringLength),
                _ => 0
            };
            long bytes = (expected - count) * size;
            if (bytes is < 0 or > int.MaxValue)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            return (int)bytes;
        }

        private static int Capacity(int available, IServiceMessageContext context)
        {
            if (available < sizeof(int))
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            return context.MaxMessageSize > 0 ? Math.Min(available, context.MaxMessageSize) : available;
        }
    }
}
