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
        public int Position
        {
            get => m_position;
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
        public int Capacity => m_length;

        /// <summary>
        /// Bytes remaining to read.
        /// </summary>
        public int Remaining => m_length - m_position;

        /// <summary>
        /// Origin of the readable region inside the backing buffer.
        /// </summary>
        public int Origin => m_origin;

        /// <summary>
        /// Underlying backing buffer; exposed for direct integration
        /// with <see cref="BinaryDecoder"/>.
        /// </summary>
        public byte[] Buffer => m_buffer;

        /// <summary>
        /// Advances the cursor by <paramref name="byteCount"/> bytes
        /// after an external reader has consumed that slice in place.
        /// </summary>
        /// <param name="byteCount">Number of bytes already consumed.</param>
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
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
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
            m_position += read;
            return result;
        }

        /// <summary>
        /// Decodes a UA <see cref="DataValue"/> using the stack
        /// <see cref="BinaryDecoder"/>.
        /// </summary>
        /// <param name="context">Stack service message context.</param>
        /// <returns>The decoded DataValue.</returns>
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
            m_position += read;
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
        /// scalar is read as a fixed-size
        /// <paramref name="maxStringLength"/> byte block (trailing
        /// NUL bytes are trimmed). When
        /// <paramref name="arrayDimensions"/> is non-empty the
        /// array is read as a fixed-size matrix of
        /// <c>product(arrayDimensions)</c> elements with no length
        /// prefix. All other inputs fall back to the legacy
        /// length-prefixed layout.
        /// </summary>
        /// <param name="builtInType">Built-in type from metadata.</param>
        /// <param name="valueRank">Value rank from metadata.</param>
        /// <param name="maxStringLength">Per-field <c>MaxStringLength</c>; 0 disables padding.</param>
        /// <param name="arrayDimensions">Per-field <c>ArrayDimensions</c>; <c>default</c> / empty disables array padding.</param>
        /// <param name="context">Stack service message context.</param>
        /// <returns>The decoded value as a <see cref="Variant"/>.</returns>
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
                    builtInType, expectedCount, maxStringLength, out Variant array))
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
            m_position += read;
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
                {
                    string s = ReadPaddedUtf8(maxStringLength);
                    value = new Variant(s);
                    return true;
                }
                case BuiltInType.ByteString:
                {
                    ByteString bs = ReadPaddedBytes(maxStringLength);
                    value = new Variant(bs);
                    return true;
                }
                case BuiltInType.XmlElement:
                {
                    string xmlText = ReadPaddedUtf8(maxStringLength);
                    XmlElement xml = XmlElement.From(
                        string.IsNullOrEmpty(xmlText) ? null : xmlText);
                    value = new Variant(xml);
                    return true;
                }
                default:
                    value = Variant.Null;
                    return false;
            }
        }

        private string ReadPaddedUtf8(uint maxStringLength)
        {
            int total = checked((int)maxStringLength);
            if (Remaining < total)
            {
                throw new ArgumentException(
                    $"Padded RawData payload is truncated: need {total} bytes, " +
                    $"have {Remaining}.");
            }
            int trimmed = TrimTrailingNuls(total);
            string result = trimmed == 0
                ? string.Empty
                : SysText.Encoding.UTF8.GetString(
                    m_buffer, m_origin + m_position, trimmed);
            m_position += total;
            return result;
        }

        private ByteString ReadPaddedBytes(uint maxLength)
        {
            int total = checked((int)maxLength);
            if (Remaining < total)
            {
                throw new ArgumentException(
                    $"Padded RawData payload is truncated: need {total} bytes, " +
                    $"have {Remaining}.");
            }
            int trimmed = TrimTrailingNuls(total);
            if (trimmed == 0)
            {
                m_position += total;
                return ByteString.Empty;
            }
            byte[] bytes = new byte[trimmed];
            new ReadOnlySpan<byte>(m_buffer, m_origin + m_position, trimmed)
                .CopyTo(bytes);
            m_position += total;
            return new ByteString(bytes);
        }

        private int TrimTrailingNuls(int length)
        {
            int trimmed = length;
            int start = m_origin + m_position;
            while (trimmed > 0 && m_buffer[start + trimmed - 1] == 0)
            {
                trimmed--;
            }
            return trimmed;
        }

        private bool TryReadPaddedArray(
            BuiltInType builtInType,
            int expectedCount,
            uint maxStringLength,
            out Variant value)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    value = ReadPaddedBooleanArray(expectedCount);
                    return true;
                case BuiltInType.SByte:
                    value = ReadPaddedSByteArray(expectedCount);
                    return true;
                case BuiltInType.Byte:
                    value = ReadPaddedByteArray(expectedCount);
                    return true;
                case BuiltInType.Int16:
                    value = ReadPaddedInt16Array(expectedCount);
                    return true;
                case BuiltInType.UInt16:
                    value = ReadPaddedUInt16Array(expectedCount);
                    return true;
                case BuiltInType.Int32:
                    value = ReadPaddedInt32Array(expectedCount);
                    return true;
                case BuiltInType.UInt32:
                    value = ReadPaddedUInt32Array(expectedCount);
                    return true;
                case BuiltInType.Int64:
                    value = ReadPaddedInt64Array(expectedCount);
                    return true;
                case BuiltInType.UInt64:
                    value = ReadPaddedUInt64Array(expectedCount);
                    return true;
                case BuiltInType.Float:
                    value = ReadPaddedFloatArray(expectedCount);
                    return true;
                case BuiltInType.Double:
                    value = ReadPaddedDoubleArray(expectedCount);
                    return true;
                case BuiltInType.String:
                    if (maxStringLength == 0)
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = ReadPaddedStringArray(expectedCount, maxStringLength);
                    return true;
                case BuiltInType.ByteString:
                    if (maxStringLength == 0)
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = ReadPaddedByteStringArray(expectedCount, maxStringLength);
                    return true;
                default:
                    value = Variant.Null;
                    return false;
            }
        }

        private Variant ReadPaddedBooleanArray(int expectedCount)
        {
            EnsureRemaining(expectedCount);
            var arr = new bool[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = m_buffer[m_origin + m_position++] != 0;
            }
            return new Variant(new ArrayOf<bool>(arr));
        }

        private Variant ReadPaddedSByteArray(int expectedCount)
        {
            EnsureRemaining(expectedCount);
            var arr = new sbyte[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = (sbyte)m_buffer[m_origin + m_position++];
            }
            return new Variant(new ArrayOf<sbyte>(arr));
        }

        private Variant ReadPaddedByteArray(int expectedCount)
        {
            EnsureRemaining(expectedCount);
            var arr = new byte[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = m_buffer[m_origin + m_position++];
            }
            return new Variant(new ArrayOf<byte>(arr));
        }

        private Variant ReadPaddedInt16Array(int expectedCount)
        {
            EnsureRemaining(checked(expectedCount * 2));
            var arr = new short[expectedCount];
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
            var arr = new ushort[expectedCount];
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
            var arr = new int[expectedCount];
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
            var arr = new uint[expectedCount];
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
            var arr = new long[expectedCount];
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
            var arr = new ulong[expectedCount];
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
            var arr = new float[expectedCount];
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
            var arr = new double[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = ReadDoubleLittleEndian(m_buffer, m_origin + m_position);
                m_position += 8;
            }
            return new Variant(new ArrayOf<double>(arr));
        }

        private Variant ReadPaddedStringArray(int expectedCount, uint maxStringLength)
        {
            var arr = new string[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = ReadPaddedUtf8(maxStringLength);
            }
            return new Variant(new ArrayOf<string>(arr));
        }

        private Variant ReadPaddedByteStringArray(int expectedCount, uint maxLength)
        {
            var arr = new ByteString[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                arr[i] = ReadPaddedBytes(maxLength);
            }
            return new Variant(new ArrayOf<ByteString>(arr));
        }

        private void EnsureRemaining(int byteCount)
        {
            if (Remaining < byteCount)
            {
                throw new ArgumentException(
                    $"Padded RawData payload is truncated: need {byteCount} bytes, " +
                    $"have {Remaining}.");
            }
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
            var coerced = new string[raw.Count];
            for (int i = 0; i < raw.Count; i++)
            {
                coerced[i] = raw[i] ?? string.Empty;
            }
            return new Variant(new ArrayOf<string>(coerced));
        }
    }
}
