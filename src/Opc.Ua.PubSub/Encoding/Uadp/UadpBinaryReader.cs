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
using SysText = System.Text;

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// Cursor-style binary reader over a <see cref="byte"/> buffer.
    /// Mirrors <see cref="UadpBinaryWriter"/>: bounds-checked
    /// little-endian primitive reads with an integrated
    /// <see cref="BinaryDecoder"/> fall-back for Variant / DataValue /
    /// ByteString values.
    /// </summary>
    /// <remarks>
    /// Implements the low-level read path used by the UADP decoder
    /// (<see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2">
    /// Part 14 Annex A</see>). All read methods return
    /// <see langword="false"/> instead of throwing when the cursor
    /// would walk past the end of the buffer; this lets the decoder
    /// soft-reject truncated frames per
    /// <see cref="INetworkMessageDecoder"/> contract.
    /// </remarks>
    internal struct UadpBinaryReader
    {
        private readonly byte[] m_buffer;
        private readonly int m_origin;
        private readonly int m_length;
        private int m_position;

        /// <summary>
        /// Creates a reader over <paramref name="buffer"/> starting
        /// at <paramref name="origin"/> for <paramref name="length"/>
        /// bytes.
        /// </summary>
        /// <param name="buffer">Backing buffer (not null).</param>
        /// <param name="origin">Index of the first readable byte.</param>
        /// <param name="length">Number of readable bytes from <paramref name="origin"/>.</param>
        public UadpBinaryReader(byte[] buffer, int origin, int length)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if ((uint)origin > (uint)buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(origin));
            }
            if ((uint)length > (uint)(buffer.Length - origin))
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            m_buffer = buffer;
            m_origin = origin;
            m_length = length;
            m_position = 0;
        }

        /// <summary>
        /// Number of bytes consumed so far relative to
        /// <see cref="Origin"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public int Position
        {
            readonly get => m_position;
            set
            {
                if ((uint)value > (uint)m_length)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                m_position = value;
            }
        }

        /// <summary>
        /// Total readable capacity.
        /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
        public readonly int Capacity => m_length;
#pragma warning restore RCS1085 // Use auto-implemented property

        /// <summary>
        /// Bytes remaining to read.
        /// </summary>
        public readonly int Remaining => m_length - m_position;

        /// <summary>
        /// Origin of the readable region inside the backing buffer.
        /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
        public readonly int Origin => m_origin;
#pragma warning restore RCS1085 // Use auto-implemented property

        /// <summary>
        /// Underlying backing buffer; exposed for direct integration
        /// with <see cref="BinaryDecoder"/>.
        /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
        public readonly byte[] Buffer => m_buffer;
#pragma warning restore RCS1085 // Use auto-implemented property

        /// <summary>
        /// Advances the cursor by <paramref name="byteCount"/> bytes
        /// after an external reader has consumed that slice in place.
        /// </summary>
        /// <param name="byteCount">Number of bytes already consumed.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void Advance(int byteCount)
        {
            if (byteCount < 0 || byteCount > Remaining)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            }
            m_position += byteCount;
        }

        /// <summary>
        /// Reads a single byte.
        /// </summary>
        /// <param name="value">Decoded byte.</param>
        /// <returns><see langword="true"/> on success.</returns>
        public bool TryReadByte(out byte value)
        {
            if (Remaining < 1)
            {
                value = 0;
                return false;
            }
            value = m_buffer[m_origin + m_position];
            m_position++;
            return true;
        }

        /// <summary>
        /// Reads a 16-bit unsigned integer (little-endian).
        /// </summary>
        /// <param name="value">Decoded value.</param>
        /// <returns><see langword="true"/> on success.</returns>
        public bool TryReadUInt16Le(out ushort value)
        {
            if (Remaining < 2)
            {
                value = 0;
                return false;
            }
            value = BinaryPrimitives.ReadUInt16LittleEndian(
                new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, 2));
            m_position += 2;
            return true;
        }

        /// <summary>
        /// Reads a 32-bit unsigned integer (little-endian).
        /// </summary>
        /// <param name="value">Decoded value.</param>
        /// <returns><see langword="true"/> on success.</returns>
        public bool TryReadUInt32Le(out uint value)
        {
            if (Remaining < 4)
            {
                value = 0;
                return false;
            }
            value = BinaryPrimitives.ReadUInt32LittleEndian(
                new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, 4));
            m_position += 4;
            return true;
        }

        /// <summary>
        /// Reads a 64-bit unsigned integer (little-endian).
        /// </summary>
        /// <param name="value">Decoded value.</param>
        /// <returns><see langword="true"/> on success.</returns>
        public bool TryReadUInt64Le(out ulong value)
        {
            if (Remaining < 8)
            {
                value = 0;
                return false;
            }
            value = BinaryPrimitives.ReadUInt64LittleEndian(
                new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, 8));
            m_position += 8;
            return true;
        }

        /// <summary>
        /// Reads a 64-bit signed integer (little-endian).
        /// </summary>
        /// <param name="value">Decoded value.</param>
        /// <returns><see langword="true"/> on success.</returns>
        public bool TryReadInt64Le(out long value)
        {
            if (Remaining < 8)
            {
                value = 0;
                return false;
            }
            value = BinaryPrimitives.ReadInt64LittleEndian(
                new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, 8));
            m_position += 8;
            return true;
        }

        /// <summary>
        /// Reads a length-prefixed UA-binary UTF-8 string. A length
        /// of <c>-1</c> decodes to <see langword="null"/>; <c>0</c>
        /// decodes to the empty string.
        /// </summary>
        /// <param name="value">Decoded string.</param>
        /// <returns><see langword="true"/> on success.</returns>
        public bool TryReadString(out string? value)
        {
            value = null;
            if (Remaining < 4)
            {
                return false;
            }
            int length = BinaryPrimitives.ReadInt32LittleEndian(
                new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, 4));
            m_position += 4;
            if (length == -1)
            {
                value = null;
                return true;
            }
            if (length < 0 || length > Remaining)
            {
                return false;
            }
            value = length == 0
                ? string.Empty
                : SysText.Encoding.UTF8.GetString(m_buffer, m_origin + m_position, length);
            m_position += length;
            return true;
        }

        /// <summary>
        /// Reads the 16 raw bytes of a <see cref="Guid"/>.
        /// </summary>
        /// <param name="value">Decoded GUID.</param>
        /// <returns><see langword="true"/> on success.</returns>
        public bool TryReadGuid(out Guid value)
        {
            if (Remaining < 16)
            {
                value = Guid.Empty;
                return false;
            }
#if NET6_0_OR_GREATER
            value = new Guid(new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, 16));
#else
            byte[] tmp = new byte[16];
            System.Buffer.BlockCopy(m_buffer, m_origin + m_position, tmp, 0, 16);
            value = new Guid(tmp);
#endif
            m_position += 16;
            return true;
        }

        /// <summary>
        /// Reads <paramref name="byteCount"/> raw bytes into a new
        /// array.
        /// </summary>
        /// <param name="byteCount">Number of bytes to read.</param>
        /// <param name="value">Decoded bytes.</param>
        /// <returns><see langword="true"/> on success.</returns>
        public bool TryReadBytes(int byteCount, out byte[] value)
        {
            if (byteCount < 0 || Remaining < byteCount)
            {
                value = [];
                return false;
            }
            value = new byte[byteCount];
            new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, byteCount).CopyTo(value);
            m_position += byteCount;
            return true;
        }

        /// <summary>
        /// Decodes a UA <see cref="Variant"/> using the stack
        /// <see cref="BinaryDecoder"/>.
        /// </summary>
        /// <param name="context">Stack service message context.</param>
        /// <returns>The decoded Variant.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public Variant ReadVariant(IServiceMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            int read;
            Variant result;
            using (var decoder = new BinaryDecoder(
                m_buffer, m_origin + m_position, Remaining, context))
            {
                result = decoder.ReadVariant(null);
                read = decoder.Position;
            }
            AdvanceDecoded(read);
            return result;
        }

        /// <summary>
        /// Decodes a UA <see cref="DataValue"/> using the stack
        /// <see cref="BinaryDecoder"/>.
        /// </summary>
        /// <param name="context">Stack service message context.</param>
        /// <returns>The decoded DataValue.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public DataValue ReadDataValue(IServiceMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            int read;
            DataValue result;
            using (var decoder = new BinaryDecoder(
                m_buffer, m_origin + m_position, Remaining, context))
            {
                result = decoder.ReadDataValue(null);
                read = decoder.Position;
            }
            AdvanceDecoded(read);
            return result;
        }

        /// <summary>
        /// Decodes a raw scalar of the supplied built-in type per
        /// the RawData field-encoding rules of Part 14 §7.2.4.5.4.
        /// </summary>
        /// <param name="builtInType">Built-in type from metadata.</param>
        /// <param name="valueRank">Value rank from metadata.</param>
        /// <param name="context">Stack service message context.</param>
        /// <returns>The decoded value as a <see cref="Variant"/>.</returns>
        public Variant ReadRawScalar(
            BuiltInType builtInType,
            int valueRank,
            IServiceMessageContext context)
        {
            return ReadRawScalar(
                builtInType, valueRank,
                maxStringLength: 0,
                arrayDimensions: default,
                context);
        }

        /// <summary>
        /// Decodes a raw scalar / array of the supplied built-in
        /// type applying the
        /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.11">
        /// Part 14 §7.2.4.5.11</see> padding rule: when
        /// <paramref name="maxStringLength"/> &gt; 0 the
        /// <c>String</c> / <c>ByteString</c> / <c>XmlElement</c>
        /// scalar is read like a Structure field (Int32 length prefix and
        /// value) from a fixed-size <c>4 + MaxStringLength</c> byte block
        /// whose remainder is zero padding. When
        /// <paramref name="arrayDimensions"/> is non-empty the array's
        /// Part 6 length (or dimensions) prefix gives the actual element
        /// count and the block is padded to
        /// <c>product(arrayDimensions)</c> elements. All other inputs use
        /// the unpadded Part 6 layout.
        /// </summary>
        /// <param name="builtInType">Built-in type from metadata.</param>
        /// <param name="valueRank">Value rank from metadata.</param>
        /// <param name="maxStringLength">Per-field <c>MaxStringLength</c>; 0 disables padding.</param>
        /// <param name="arrayDimensions">Per-field <c>ArrayDimensions</c>; <c>default</c> / empty disables array padding.</param>
        /// <param name="context">Stack service message context.</param>
        /// <returns>The decoded value as a <see cref="Variant"/>.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public Variant ReadRawScalar(
            BuiltInType builtInType,
            int valueRank,
            uint maxStringLength,
            ArrayOf<uint> arrayDimensions,
            IServiceMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (valueRank == ValueRanks.Scalar &&
                maxStringLength > 0 &&
                TryReadPaddedScalar(builtInType, maxStringLength, out Variant scalar))
            {
                return scalar;
            }

            if (valueRank != ValueRanks.Scalar &&
                TryComputePaddedArrayCount(arrayDimensions, out int expectedCount) &&
                TryReadPaddedArray(
                    builtInType, arrayDimensions, expectedCount, maxStringLength,
                    out Variant array))
            {
                return array;
            }

            int read;
            Variant result;
            using (var decoder = new BinaryDecoder(
                m_buffer, m_origin + m_position, Remaining, context))
            {
                result = valueRank == ValueRanks.Scalar
                    ? ReadRawScalarCore(decoder, builtInType)
                    : ReadRawArrayCore(decoder, builtInType);
                read = decoder.Position;
            }
            AdvanceDecoded(read);
            return result;
        }

        private static bool TryComputePaddedArrayCount(
            ArrayOf<uint> arrayDimensions, out int count)
        {
            count = 0;
            if (arrayDimensions.IsNull || arrayDimensions.Count == 0)
            {
                return false;
            }
            ulong product = 1UL;
            for (int i = 0; i < arrayDimensions.Count; i++)
            {
                uint dim = arrayDimensions[i];
                if (dim == 0)
                {
                    return false;
                }
                product *= dim;
                if (product > int.MaxValue)
                {
                    return false;
                }
            }
            count = (int)product;
            return true;
        }

        private bool TryReadPaddedScalar(
            BuiltInType builtInType, uint maxStringLength, out Variant value)
        {
            switch (builtInType)
            {
                case BuiltInType.String:
                    string? s = ReadPaddedUtf8(maxStringLength);
                    value = s is null ? Variant.Null : new Variant(s);
                    return true;
                case BuiltInType.ByteString:
                    ByteString bs = ReadPaddedBytes(maxStringLength);
                    value = new Variant(bs);
                    return true;
                case BuiltInType.XmlElement:
                    string? xmlText = ReadPaddedUtf8(maxStringLength);
                    var xml = XmlElement.From(
                        string.IsNullOrEmpty(xmlText) ? null : xmlText);
                    value = new Variant(xml);
                    return true;
                default:
                    value = Variant.Null;
                    return false;
            }
        }

        private string? ReadPaddedUtf8(uint maxStringLength)
        {
            int length = ReadPaddedStringLength(maxStringLength, out int total);
            string? result = length switch
            {
                < 0 => null,
                0 => string.Empty,
                _ => SysText.Encoding.UTF8.GetString(
                    m_buffer, m_origin + m_position + 4, length)
            };
            m_position += total;
            return result;
        }

        private ByteString ReadPaddedBytes(uint maxLength)
        {
            int length = ReadPaddedStringLength(maxLength, out int total);
            if (length <= 0)
            {
                m_position += total;
                return length < 0 ? default : ByteString.Empty;
            }
            byte[] bytes = new byte[length];
            new ReadOnlySpan<byte>(m_buffer, m_origin + m_position + 4, length)
                .CopyTo(bytes);
            m_position += total;
            return new ByteString(bytes);
        }

        /// <summary>
        /// Validates a padded String / ByteString block
        /// (<c>Int32 length + MaxStringLength bytes</c>) at the cursor and
        /// returns the encoded value length without moving the cursor.
        /// A null value is returned as length -1.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private readonly int ReadPaddedStringLength(uint maxLength, out int total)
        {
            if (maxLength > int.MaxValue - 4)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "MaxStringLength {0} is too large for a padded RawData field.",
                    maxLength);
            }
            total = 4 + (int)maxLength;
            EnsureRemaining(total);
            int length = BinaryPrimitives.ReadInt32LittleEndian(
                new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, 4));
            if (length == -1)
            {
                return -1;
            }
            if (length < 0 || (uint)length > maxLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Padded RawData length {0} is outside 0..MaxStringLength {1}.",
                    length,
                    maxLength);
            }
            return length;
        }

        private bool TryReadPaddedArray(
            BuiltInType builtInType,
            ArrayOf<uint> arrayDimensions,
            int expectedCount,
            uint maxStringLength,
            out Variant value)
        {
            int elementSize;
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                case BuiltInType.SByte:
                case BuiltInType.Byte:
                    elementSize = 1;
                    break;
                case BuiltInType.Int16:
                case BuiltInType.UInt16:
                    elementSize = 2;
                    break;
                case BuiltInType.Int32:
                case BuiltInType.UInt32:
                case BuiltInType.Float:
                    elementSize = 4;
                    break;
                case BuiltInType.Int64:
                case BuiltInType.UInt64:
                case BuiltInType.Double:
                    elementSize = 8;
                    break;
                case BuiltInType.String:
                case BuiltInType.ByteString:
                    if (maxStringLength == 0)
                    {
                        value = Variant.Null;
                        return false;
                    }
                    if (maxStringLength > int.MaxValue - 4)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "MaxStringLength {0} is too large for a padded RawData field.",
                            maxStringLength);
                    }
                    elementSize = 4 + (int)maxStringLength;
                    break;
                default:
                    value = Variant.Null;
                    return false;
            }

            int count = ReadPaddedArrayLength(arrayDimensions, expectedCount);
            // The block always holds product(ArrayDimensions) elements;
            // validate it before allocating the (smaller) actual array.
            long paddedSize = (long)expectedCount * elementSize;
            EnsureRemaining(paddedSize);
            int start = m_position;
            value = count < 0 ? NullArray(builtInType) : builtInType switch
            {
                BuiltInType.Boolean => ReadPaddedBooleanArray(count),
                BuiltInType.SByte => ReadPaddedSByteArray(count),
                BuiltInType.Byte => ReadPaddedByteArray(count),
                BuiltInType.Int16 => ReadPaddedInt16Array(count),
                BuiltInType.UInt16 => ReadPaddedUInt16Array(count),
                BuiltInType.Int32 => ReadPaddedInt32Array(count),
                BuiltInType.UInt32 => ReadPaddedUInt32Array(count),
                BuiltInType.Int64 => ReadPaddedInt64Array(count),
                BuiltInType.UInt64 => ReadPaddedUInt64Array(count),
                BuiltInType.Float => ReadPaddedFloatArray(count),
                BuiltInType.Double => ReadPaddedDoubleArray(count),
                BuiltInType.String => ReadPaddedStringArray(count, maxStringLength),
                _ => ReadPaddedByteStringArray(count, maxStringLength)
            };
            // Skip the zero padding up to product(ArrayDimensions) elements.
            m_position = start + (int)paddedSize;
            return true;
        }

        /// <summary>
        /// The value of a null array (length -1, Part 6 §5.2.5), shaped like
        /// the result of <see cref="ReadRawArrayCore"/>.
        /// </summary>
        private static Variant NullArray(BuiltInType builtInType)
        {
            return builtInType switch
            {
                BuiltInType.Boolean => new Variant(default(ArrayOf<bool>)),
                BuiltInType.SByte => new Variant(default(ArrayOf<sbyte>)),
                BuiltInType.Byte => new Variant(default(ArrayOf<byte>)),
                BuiltInType.Int16 => new Variant(default(ArrayOf<short>)),
                BuiltInType.UInt16 => new Variant(default(ArrayOf<ushort>)),
                BuiltInType.Int32 => new Variant(default(ArrayOf<int>)),
                BuiltInType.UInt32 => new Variant(default(ArrayOf<uint>)),
                BuiltInType.Int64 => new Variant(default(ArrayOf<long>)),
                BuiltInType.UInt64 => new Variant(default(ArrayOf<ulong>)),
                BuiltInType.Float => new Variant(default(ArrayOf<float>)),
                BuiltInType.Double => new Variant(default(ArrayOf<double>)),
                BuiltInType.ByteString => new Variant(default(ArrayOf<ByteString>)),
                _ => Variant.Null
            };
        }

        /// <summary>
        /// Reads the Part 6 array header of a padded RawData array: the
        /// Int32 length for one dimension or the Int32 dimensions array
        /// for ValueRank &gt; 1. Returns the actual element count, which
        /// shall not exceed the configured ArrayDimensions, or -1 for a
        /// null one-dimensional array.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private int ReadPaddedArrayLength(ArrayOf<uint> arrayDimensions, int expectedCount)
        {
            if (!TryReadUInt32Le(out uint header))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Padded RawData array length is truncated.");
            }
            if (arrayDimensions.Count == 1)
            {
                if (header == uint.MaxValue)
                {
                    // Null array (Part 6 §5.2.5).
                    return -1;
                }
                if (header > (uint)expectedCount)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Padded RawData array length {0} exceeds ArrayDimensions {1}.",
                        header,
                        expectedCount);
                }
                return (int)header;
            }
            if (header != (uint)arrayDimensions.Count)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Padded RawData array has {0} dimensions, expected {1}.",
                    header,
                    arrayDimensions.Count);
            }
            long count = 1;
            for (int i = 0; i < arrayDimensions.Count; i++)
            {
                if (!TryReadUInt32Le(out uint dimension) || dimension > arrayDimensions[i])
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Padded RawData array dimension {0} is truncated or exceeds ArrayDimensions.",
                        i);
                }
                count *= dimension;
            }
            return (int)count;
        }

        private Variant ReadPaddedBooleanArray(int expectedCount)
        {
            EnsureRemaining(expectedCount);
            bool[] arr = new bool[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = m_buffer[m_origin + m_position++] != 0;
            }
            return new Variant(new ArrayOf<bool>(arr));
        }

        private Variant ReadPaddedSByteArray(int expectedCount)
        {
            EnsureRemaining(expectedCount);
            sbyte[] arr = new sbyte[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = (sbyte)m_buffer[m_origin + m_position++];
            }
            return new Variant(new ArrayOf<sbyte>(arr));
        }

        private Variant ReadPaddedByteArray(int expectedCount)
        {
            EnsureRemaining(expectedCount);
            byte[] arr = new byte[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = m_buffer[m_origin + m_position++];
            }
            return new Variant(new ArrayOf<byte>(arr));
        }

        private Variant ReadPaddedInt16Array(int expectedCount)
        {
            EnsureRemaining(checked(expectedCount * 2));
            short[] arr = new short[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = BinaryPrimitives.ReadInt16LittleEndian(
                    new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, 2));
                m_position += 2;
            }
            return new Variant(new ArrayOf<short>(arr));
        }

        private Variant ReadPaddedUInt16Array(int expectedCount)
        {
            EnsureRemaining(checked(expectedCount * 2));
            ushort[] arr = new ushort[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = BinaryPrimitives.ReadUInt16LittleEndian(
                    new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, 2));
                m_position += 2;
            }
            return new Variant(new ArrayOf<ushort>(arr));
        }

        private Variant ReadPaddedInt32Array(int expectedCount)
        {
            EnsureRemaining(checked(expectedCount * 4));
            int[] arr = new int[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = BinaryPrimitives.ReadInt32LittleEndian(
                    new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, 4));
                m_position += 4;
            }
            return new Variant(new ArrayOf<int>(arr));
        }

        private Variant ReadPaddedUInt32Array(int expectedCount)
        {
            EnsureRemaining(checked(expectedCount * 4));
            uint[] arr = new uint[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = BinaryPrimitives.ReadUInt32LittleEndian(
                    new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, 4));
                m_position += 4;
            }
            return new Variant(new ArrayOf<uint>(arr));
        }

        private Variant ReadPaddedInt64Array(int expectedCount)
        {
            EnsureRemaining(checked(expectedCount * 8));
            long[] arr = new long[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = BinaryPrimitives.ReadInt64LittleEndian(
                    new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, 8));
                m_position += 8;
            }
            return new Variant(new ArrayOf<long>(arr));
        }

        private Variant ReadPaddedUInt64Array(int expectedCount)
        {
            EnsureRemaining(checked(expectedCount * 8));
            ulong[] arr = new ulong[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = BinaryPrimitives.ReadUInt64LittleEndian(
                    new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, 8));
                m_position += 8;
            }
            return new Variant(new ArrayOf<ulong>(arr));
        }

        private Variant ReadPaddedFloatArray(int expectedCount)
        {
            EnsureRemaining(checked(expectedCount * 4));
            float[] arr = new float[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = ReadFloatLittleEndian(m_buffer, m_origin + m_position);
                m_position += 4;
            }
            return new Variant(new ArrayOf<float>(arr));
        }

        private Variant ReadPaddedDoubleArray(int expectedCount)
        {
            EnsureRemaining(checked(expectedCount * 8));
            double[] arr = new double[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = ReadDoubleLittleEndian(m_buffer, m_origin + m_position);
                m_position += 8;
            }
            return new Variant(new ArrayOf<double>(arr));
        }

        private Variant ReadPaddedStringArray(int expectedCount, uint maxStringLength)
        {
            EnsureRemaining(expectedCount);
            string[] arr = new string[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                // Null elements become empty like in DecodeStringArrayVariant.
                arr[i] = ReadPaddedUtf8(maxStringLength) ?? string.Empty;
            }
            return new Variant(new ArrayOf<string>(arr));
        }

        private Variant ReadPaddedByteStringArray(int expectedCount, uint maxLength)
        {
            EnsureRemaining(expectedCount);
            var arr = new ByteString[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = ReadPaddedBytes(maxLength);
            }
            return new Variant(new ArrayOf<ByteString>(arr));
        }

        private readonly void EnsureRemaining(long byteCount)
        {
            if (byteCount < 0 || Remaining < byteCount)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Padded RawData payload is truncated: need {0} bytes, have {1}.",
                    byteCount,
                    Remaining);
            }
        }

        private void AdvanceDecoded(int byteCount)
        {
            if (byteCount < 0 || byteCount > Remaining)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Decoded value length exceeds remaining UADP payload.");
            }
            m_position += byteCount;
        }

        private static float ReadFloatLittleEndian(byte[] buffer, int offset)
        {
#if NET5_0_OR_GREATER
            return BinaryPrimitives.ReadSingleLittleEndian(
                new ReadOnlySpan<byte>(buffer, offset, 4));
#else
            int bits = BinaryPrimitives.ReadInt32LittleEndian(
                new ReadOnlySpan<byte>(buffer, offset, 4));
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
#endif
        }

        private static double ReadDoubleLittleEndian(byte[] buffer, int offset)
        {
#if NET5_0_OR_GREATER
            return BinaryPrimitives.ReadDoubleLittleEndian(
                new ReadOnlySpan<byte>(buffer, offset, 8));
#else
            long bits = BinaryPrimitives.ReadInt64LittleEndian(
                new ReadOnlySpan<byte>(buffer, offset, 8));
            return BitConverter.Int64BitsToDouble(bits);
#endif
        }

        private static Variant ReadRawScalarCore(BinaryDecoder decoder, BuiltInType builtInType)
        {
            return builtInType switch
            {
                BuiltInType.Boolean => new Variant(decoder.ReadBoolean(null)),
                BuiltInType.SByte => new Variant(decoder.ReadSByte(null)),
                BuiltInType.Byte => new Variant(decoder.ReadByte(null)),
                BuiltInType.Int16 => new Variant(decoder.ReadInt16(null)),
                BuiltInType.UInt16 => new Variant(decoder.ReadUInt16(null)),
                BuiltInType.Int32 => new Variant(decoder.ReadInt32(null)),
                BuiltInType.UInt32 => new Variant(decoder.ReadUInt32(null)),
                BuiltInType.Int64 => new Variant(decoder.ReadInt64(null)),
                BuiltInType.UInt64 => new Variant(decoder.ReadUInt64(null)),
                BuiltInType.Float => new Variant(decoder.ReadFloat(null)),
                BuiltInType.Double => new Variant(decoder.ReadDouble(null)),
                BuiltInType.String => new Variant(decoder.ReadString(null) ?? string.Empty),
                BuiltInType.DateTime => new Variant(decoder.ReadDateTime(null)),
                BuiltInType.Guid => new Variant(decoder.ReadGuid(null)),
                BuiltInType.ByteString => new Variant(decoder.ReadByteString(null)),
                BuiltInType.XmlElement => new Variant(decoder.ReadXmlElement(null)),
                BuiltInType.NodeId => new Variant(decoder.ReadNodeId(null)),
                BuiltInType.ExpandedNodeId => new Variant(decoder.ReadExpandedNodeId(null)),
                BuiltInType.StatusCode => new Variant(decoder.ReadStatusCode(null)),
                BuiltInType.QualifiedName => new Variant(decoder.ReadQualifiedName(null)),
                BuiltInType.LocalizedText => new Variant(decoder.ReadLocalizedText(null)),
                BuiltInType.Variant => decoder.ReadVariant(null),
                BuiltInType.DataValue => new Variant(decoder.ReadDataValue(null)),
                BuiltInType.ExtensionObject => new Variant(decoder.ReadExtensionObject(null)),
                _ => decoder.ReadVariant(null)
            };
        }

        private static Variant ReadRawArrayCore(BinaryDecoder decoder, BuiltInType builtInType)
        {
            return builtInType switch
            {
                BuiltInType.Boolean => new Variant(decoder.ReadBooleanArray(null)),
                BuiltInType.SByte => new Variant(decoder.ReadSByteArray(null)),
                BuiltInType.Byte => new Variant(decoder.ReadByteArray(null)),
                BuiltInType.Int16 => new Variant(decoder.ReadInt16Array(null)),
                BuiltInType.UInt16 => new Variant(decoder.ReadUInt16Array(null)),
                BuiltInType.Int32 => new Variant(decoder.ReadInt32Array(null)),
                BuiltInType.UInt32 => new Variant(decoder.ReadUInt32Array(null)),
                BuiltInType.Int64 => new Variant(decoder.ReadInt64Array(null)),
                BuiltInType.UInt64 => new Variant(decoder.ReadUInt64Array(null)),
                BuiltInType.Float => new Variant(decoder.ReadFloatArray(null)),
                BuiltInType.Double => new Variant(decoder.ReadDoubleArray(null)),
                BuiltInType.String => DecodeStringArrayVariant(decoder),
                BuiltInType.Variant => new Variant(decoder.ReadVariantArray(null)),
                _ => decoder.ReadVariant(null)
            };
        }

        private static Variant DecodeStringArrayVariant(BinaryDecoder decoder)
        {
            ArrayOf<string?> raw = decoder.ReadStringArray(null);
            if (raw.IsNull)
            {
                return Variant.Null;
            }
            string[] coerced = new string[raw.Count];
            for (int i = 0; i < raw.Count; i++)
            {
                coerced[i] = raw[i] ?? string.Empty;
            }
            return new Variant(new ArrayOf<string>(coerced));
        }
    }
}
