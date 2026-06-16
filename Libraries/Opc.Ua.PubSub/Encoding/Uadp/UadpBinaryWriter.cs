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
    /// Cursor-style binary writer over a pre-allocated
    /// <see cref="byte"/> buffer. Emits little-endian primitives and
    /// reserves a slot pattern for back-patching unknown sizes (used
    /// by the payload header's per-DataSetMessage size array).
    /// </summary>
    /// <remarks>
    /// Implements the low-level write path used by the UADP encoder
    /// (<see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2">
    /// Part 14 Annex A</see>). The struct holds a reference to the
    /// caller-supplied buffer (typically rented from
    /// <see cref="System.Buffers.ArrayPool{T}"/>) and is not
    /// thread-safe — pass by <c>ref</c> on every call site so cursor
    /// updates persist.
    /// </remarks>
    internal struct UadpBinaryWriter
    {
        private readonly byte[] m_buffer;
        private readonly int m_origin;
        private readonly int m_length;
        private int m_position;

        /// <summary>
        /// Creates a writer that targets <paramref name="buffer"/>
        /// from <paramref name="origin"/> for <paramref name="length"/>
        /// bytes.
        /// </summary>
        /// <param name="buffer">Backing buffer. Must not be null.</param>
        /// <param name="origin">Index of the first writable byte.</param>
        /// <param name="length">
        /// Number of writable bytes from <paramref name="origin"/>.
        /// </param>
        public UadpBinaryWriter(byte[] buffer, int origin, int length)
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
        /// Number of bytes written so far relative to
        /// <see cref="Origin"/>.
        /// </summary>
        public int Position => m_position;

        /// <summary>
        /// Origin offset of the writable region inside the backing
        /// buffer.
        /// </summary>
        public int Origin => m_origin;

        /// <summary>
        /// Total writable capacity of this writer instance.
        /// </summary>
        public int Capacity => m_length;

        /// <summary>
        /// Bytes remaining in the writable region.
        /// </summary>
        public int Remaining => m_length - m_position;

        /// <summary>
        /// Writable slice exposing the bytes that have already been
        /// produced.
        /// </summary>
        /// <returns>
        /// A <see cref="ReadOnlySpan{T}"/> over the bytes already
        /// written.
        /// </returns>
        public ReadOnlySpan<byte> WrittenSpan()
            => new(m_buffer, m_origin, m_position);

        /// <summary>
        /// Underlying backing buffer; exposed for direct integration
        /// with <see cref="BinaryEncoder"/>.
        /// </summary>
        public byte[] Buffer => m_buffer;

        /// <summary>
        /// Advances the cursor by <paramref name="byteCount"/> bytes
        /// after an external writer (e.g. <see cref="BinaryEncoder"/>)
        /// has filled that slice in place.
        /// </summary>
        /// <param name="byteCount">Number of bytes already written.</param>
        public void Advance(int byteCount)
        {
            if (byteCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            }
            EnsureCapacity(byteCount);
            m_position += byteCount;
        }

        /// <summary>
        /// Writes a single byte.
        /// </summary>
        /// <param name="value">Byte to write.</param>
        public void WriteByte(byte value)
        {
            EnsureCapacity(1);
            m_buffer[m_origin + m_position] = value;
            m_position++;
        }

        /// <summary>
        /// Writes a 16-bit unsigned integer in little-endian order.
        /// </summary>
        /// <param name="value">Value to write.</param>
        public void WriteUInt16Le(ushort value)
        {
            EnsureCapacity(2);
            BinaryPrimitives.WriteUInt16LittleEndian(
                new Span<byte>(m_buffer, m_origin + m_position, 2),
                value);
            m_position += 2;
        }

        /// <summary>
        /// Writes a 32-bit unsigned integer in little-endian order.
        /// </summary>
        /// <param name="value">Value to write.</param>
        public void WriteUInt32Le(uint value)
        {
            EnsureCapacity(4);
            BinaryPrimitives.WriteUInt32LittleEndian(
                new Span<byte>(m_buffer, m_origin + m_position, 4),
                value);
            m_position += 4;
        }

        /// <summary>
        /// Writes a 64-bit unsigned integer in little-endian order.
        /// </summary>
        /// <param name="value">Value to write.</param>
        public void WriteUInt64Le(ulong value)
        {
            EnsureCapacity(8);
            BinaryPrimitives.WriteUInt64LittleEndian(
                new Span<byte>(m_buffer, m_origin + m_position, 8),
                value);
            m_position += 8;
        }

        /// <summary>
        /// Writes a 64-bit signed integer in little-endian order.
        /// </summary>
        /// <param name="value">Value to write.</param>
        public void WriteInt64Le(long value)
        {
            EnsureCapacity(8);
            BinaryPrimitives.WriteInt64LittleEndian(
                new Span<byte>(m_buffer, m_origin + m_position, 8),
                value);
            m_position += 8;
        }

        /// <summary>
        /// Writes a UA-binary length-prefixed UTF-8 string. A
        /// <see langword="null"/> string is encoded as a length of
        /// <c>-1</c>; an empty string as a length of <c>0</c>.
        /// </summary>
        /// <param name="value">String to write; may be null.</param>
        public void WriteString(string? value)
        {
            if (value is null)
            {
                EnsureCapacity(4);
                BinaryPrimitives.WriteInt32LittleEndian(
                    new Span<byte>(m_buffer, m_origin + m_position, 4),
                    -1);
                m_position += 4;
                return;
            }
            int byteCount = SysText.Encoding.UTF8.GetByteCount(value);
            EnsureCapacity(4 + byteCount);
            BinaryPrimitives.WriteInt32LittleEndian(
                new Span<byte>(m_buffer, m_origin + m_position, 4),
                byteCount);
            m_position += 4;
            if (byteCount > 0)
            {
                SysText.Encoding.UTF8.GetBytes(
                    value, 0, value.Length, m_buffer, m_origin + m_position);
                m_position += byteCount;
            }
        }

        /// <summary>
        /// Writes the raw bytes of a <see cref="Guid"/> (16 bytes,
        /// per OPC UA UA-Binary Guid layout).
        /// </summary>
        /// <param name="value">Guid to write.</param>
        public void WriteGuid(Guid value)
        {
            EnsureCapacity(16);
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            value.TryWriteBytes(new Span<byte>(m_buffer, m_origin + m_position, 16));
#else
            byte[] tmp = value.ToByteArray();
            System.Buffer.BlockCopy(tmp, 0, m_buffer, m_origin + m_position, 16);
#endif
            m_position += 16;
        }

        /// <summary>
        /// Copies the contents of <paramref name="source"/> into the
        /// buffer.
        /// </summary>
        /// <param name="source">Source bytes to copy.</param>
        public void WriteBytes(ReadOnlySpan<byte> source)
        {
            if (source.IsEmpty)
            {
                return;
            }
            EnsureCapacity(source.Length);
            source.CopyTo(new Span<byte>(m_buffer, m_origin + m_position, source.Length));
            m_position += source.Length;
        }

        /// <summary>
        /// Reserves <paramref name="byteCount"/> bytes to be patched
        /// later via <see cref="PatchUInt16Le"/> / <see cref="PatchUInt32Le"/>.
        /// </summary>
        /// <param name="byteCount">Number of bytes to reserve.</param>
        /// <returns>
        /// The absolute position (relative to
        /// <see cref="Origin"/>) of the first reserved byte.
        /// </returns>
        public int Reserve(int byteCount)
        {
            EnsureCapacity(byteCount);
            int slot = m_position;
            // Zero the slot to make patches deterministic if some are
            // skipped at write time.
            for (int i = 0; i < byteCount; i++)
            {
                m_buffer[m_origin + m_position + i] = 0;
            }
            m_position += byteCount;
            return slot;
        }

        /// <summary>
        /// Patches a previously reserved <c>UInt16</c> slot with
        /// <paramref name="value"/>.
        /// </summary>
        /// <param name="position">Reserved slot position.</param>
        /// <param name="value">Value to patch.</param>
        public void PatchUInt16Le(int position, ushort value)
        {
            if ((uint)position > (uint)(m_length - 2))
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }
            BinaryPrimitives.WriteUInt16LittleEndian(
                new Span<byte>(m_buffer, m_origin + position, 2),
                value);
        }

        /// <summary>
        /// Patches a previously reserved <c>UInt32</c> slot with
        /// <paramref name="value"/>.
        /// </summary>
        /// <param name="position">Reserved slot position.</param>
        /// <param name="value">Value to patch.</param>
        public void PatchUInt32Le(int position, uint value)
        {
            if ((uint)position > (uint)(m_length - 4))
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }
            BinaryPrimitives.WriteUInt32LittleEndian(
                new Span<byte>(m_buffer, m_origin + position, 4),
                value);
        }

        /// <summary>
        /// Writes a UA <see cref="Variant"/> using the stack
        /// <see cref="BinaryEncoder"/>. The encoder writes directly
        /// into the buffer at the current position; no intermediate
        /// allocation occurs.
        /// </summary>
        /// <param name="value">Variant to encode.</param>
        /// <param name="context">Stack service message context.</param>
        public void WriteVariant(in Variant value, IServiceMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            int available = m_length - m_position;
            if (available <= 0)
            {
                throw new InvalidOperationException("UADP writer buffer is full.");
            }
            int written;
            using (var encoder = new BinaryEncoder(
                m_buffer, m_origin + m_position, available, context))
            {
                encoder.WriteVariant(null, value);
                written = encoder.Close();
            }
            m_position += written;
        }

        /// <summary>
        /// Writes a UA <see cref="DataValue"/> using the stack
        /// <see cref="BinaryEncoder"/>.
        /// </summary>
        /// <param name="value">DataValue to encode.</param>
        /// <param name="context">Stack service message context.</param>
        public void WriteDataValue(in DataValue value, IServiceMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            int available = m_length - m_position;
            if (available <= 0)
            {
                throw new InvalidOperationException("UADP writer buffer is full.");
            }
            int written;
            using (var encoder = new BinaryEncoder(
                m_buffer, m_origin + m_position, available, context))
            {
                encoder.WriteDataValue(null, value);
                written = encoder.Close();
            }
            m_position += written;
        }

        /// <summary>
        /// Writes a UA scalar of the supplied built-in type taken
        /// from a <see cref="Variant"/> (RawData field encoding).
        /// </summary>
        /// <param name="value">Source variant (its built-in type drives the on-wire layout).</param>
        /// <param name="builtInType">Expected built-in type from metadata.</param>
        /// <param name="valueRank">Value rank from metadata.</param>
        /// <param name="context">Stack service message context.</param>
        public void WriteRawScalar(
            in Variant value,
            BuiltInType builtInType,
            int valueRank,
            IServiceMessageContext context)
        {
            WriteRawScalar(
                value, builtInType, valueRank,
                maxStringLength: 0,
                arrayDimensions: default,
                context);
        }

        /// <summary>
        /// Writes a UA scalar / array of the supplied built-in type
        /// taken from a <see cref="Variant"/> (RawData field encoding)
        /// applying the
        /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.11">
        /// Part 14 §7.2.4.5.11</see> padding rule: when
        /// <paramref name="maxStringLength"/> &gt; 0 the
        /// <c>String</c> / <c>ByteString</c> / <c>XmlElement</c>
        /// scalar is emitted as a fixed-size <paramref name="maxStringLength"/>
        /// byte block (raw UTF-8 / raw bytes, NUL padded, no
        /// length prefix). When <paramref name="arrayDimensions"/>
        /// is non-empty the array is emitted as a fixed-size
        /// matrix of <c>product(arrayDimensions)</c> elements
        /// (no length prefix). All other inputs fall back to the
        /// legacy length-prefixed layout.
        /// </summary>
        /// <param name="value">Source variant (its built-in type drives the on-wire layout).</param>
        /// <param name="builtInType">Expected built-in type from metadata.</param>
        /// <param name="valueRank">Value rank from metadata.</param>
        /// <param name="maxStringLength">
        /// Per-field <c>MaxStringLength</c> from
        /// <see cref="FieldMetaData"/>. 0 disables padding for the
        /// field (legacy length-prefixed behaviour).
        /// </param>
        /// <param name="arrayDimensions">
        /// Per-field <c>ArrayDimensions</c> from
        /// <see cref="FieldMetaData"/>. <c>default</c> / empty
        /// disables array padding (legacy length-prefixed
        /// behaviour).
        /// </param>
        /// <param name="context">Stack service message context.</param>
        public void WriteRawScalar(
            in Variant value,
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
            int available = m_length - m_position;
            if (available <= 0)
            {
                throw new InvalidOperationException("UADP writer buffer is full.");
            }

            if (valueRank == ValueRanks.Scalar &&
                maxStringLength > 0 &&
                TryWritePaddedScalar(value, builtInType, maxStringLength))
            {
                return;
            }

            if (valueRank != ValueRanks.Scalar &&
                TryComputePaddedArrayCount(arrayDimensions, out int expectedCount) &&
                TryWritePaddedArray(value, builtInType, expectedCount, maxStringLength))
            {
                return;
            }

            int written;
            using (var encoder = new BinaryEncoder(
                m_buffer, m_origin + m_position, available, context))
            {
                if (valueRank == ValueRanks.Scalar)
                {
                    WriteRawScalarCore(encoder, value, builtInType);
                }
                else
                {
                    WriteRawArrayCore(encoder, value, builtInType);
                }
                written = encoder.Close();
            }
            m_position += written;
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
                    throw new ArgumentException(
                        "ArrayDimensions product exceeds Int32.MaxValue.",
                        nameof(arrayDimensions));
                }
            }
            count = (int)product;
            return true;
        }

        private bool TryWritePaddedScalar(
            in Variant value, BuiltInType builtInType, uint maxStringLength)
        {
            switch (builtInType)
            {
                case BuiltInType.String:
                {
                    value.TryGetValue(out string? s);
                    WritePaddedUtf8(s ?? string.Empty, maxStringLength);
                    return true;
                }
                case BuiltInType.ByteString:
                {
                    value.TryGetValue(out ByteString bs);
                    WritePaddedBytes(bs, maxStringLength);
                    return true;
                }
                case BuiltInType.XmlElement:
                {
                    value.TryGetValue(out XmlElement xml);
                    string text = xml.IsNull ? string.Empty : (xml.OuterXml ?? string.Empty);
                    WritePaddedUtf8(text, maxStringLength);
                    return true;
                }
                default:
                    return false;
            }
        }

        private void WritePaddedUtf8(string value, uint maxStringLength)
        {
            int byteCount = value.Length == 0
                ? 0
                : SysText.Encoding.UTF8.GetByteCount(value);
            if ((uint)byteCount > maxStringLength)
            {
                throw new ArgumentException(
                    $"MaxStringLength exceeded: payload is {byteCount} bytes but only " +
                    $"{maxStringLength} bytes are allowed.",
                    nameof(value));
            }
            int total = checked((int)maxStringLength);
            EnsureCapacity(total);
            if (byteCount > 0)
            {
                SysText.Encoding.UTF8.GetBytes(
                    value, 0, value.Length, m_buffer, m_origin + m_position);
            }
            int padCount = total - byteCount;
            if (padCount > 0)
            {
                Array.Clear(m_buffer, m_origin + m_position + byteCount, padCount);
            }
            m_position += total;
        }

        private void WritePaddedBytes(ByteString value, uint maxLength)
        {
            ReadOnlySpan<byte> src = value.IsNull
                ? ReadOnlySpan<byte>.Empty
                : value.Span;
            if ((uint)src.Length > maxLength)
            {
                throw new ArgumentException(
                    $"MaxStringLength exceeded: payload is {src.Length} bytes but only " +
                    $"{maxLength} bytes are allowed.",
                    nameof(value));
            }
            int total = checked((int)maxLength);
            EnsureCapacity(total);
            if (!src.IsEmpty)
            {
                src.CopyTo(new Span<byte>(m_buffer, m_origin + m_position, src.Length));
            }
            int padCount = total - src.Length;
            if (padCount > 0)
            {
                Array.Clear(m_buffer, m_origin + m_position + src.Length, padCount);
            }
            m_position += total;
        }

        private bool TryWritePaddedArray(
            in Variant value,
            BuiltInType builtInType,
            int expectedCount,
            uint maxStringLength)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    WritePaddedBooleanArray(value, expectedCount);
                    return true;
                case BuiltInType.SByte:
                    WritePaddedSByteArray(value, expectedCount);
                    return true;
                case BuiltInType.Byte:
                    WritePaddedByteArray(value, expectedCount);
                    return true;
                case BuiltInType.Int16:
                    WritePaddedInt16Array(value, expectedCount);
                    return true;
                case BuiltInType.UInt16:
                    WritePaddedUInt16Array(value, expectedCount);
                    return true;
                case BuiltInType.Int32:
                    WritePaddedInt32Array(value, expectedCount);
                    return true;
                case BuiltInType.UInt32:
                    WritePaddedUInt32Array(value, expectedCount);
                    return true;
                case BuiltInType.Int64:
                    WritePaddedInt64Array(value, expectedCount);
                    return true;
                case BuiltInType.UInt64:
                    WritePaddedUInt64Array(value, expectedCount);
                    return true;
                case BuiltInType.Float:
                    WritePaddedFloatArray(value, expectedCount);
                    return true;
                case BuiltInType.Double:
                    WritePaddedDoubleArray(value, expectedCount);
                    return true;
                case BuiltInType.String:
                    if (maxStringLength == 0)
                    {
                        return false;
                    }
                    WritePaddedStringArray(value, expectedCount, maxStringLength);
                    return true;
                case BuiltInType.ByteString:
                    if (maxStringLength == 0)
                    {
                        return false;
                    }
                    WritePaddedByteStringArray(value, expectedCount, maxStringLength);
                    return true;
                default:
                    return false;
            }
        }

        private void WritePaddedBooleanArray(in Variant value, int expectedCount)
        {
            value.TryGetValue(out ArrayOf<bool> arr);
            int actual = arr.IsNull ? 0 : arr.Count;
            EnsureArrayWithinBounds(actual, expectedCount);
            EnsureCapacity(expectedCount);
            for (int i = 0; i < expectedCount; i++)
            {
                bool v = i < actual && arr[i];
                m_buffer[m_origin + m_position++] = (byte)(v ? 1 : 0);
            }
        }

        private void WritePaddedSByteArray(in Variant value, int expectedCount)
        {
            value.TryGetValue(out ArrayOf<sbyte> arr);
            int actual = arr.IsNull ? 0 : arr.Count;
            EnsureArrayWithinBounds(actual, expectedCount);
            EnsureCapacity(expectedCount);
            for (int i = 0; i < expectedCount; i++)
            {
                sbyte v = i < actual ? arr[i] : (sbyte)0;
                m_buffer[m_origin + m_position++] = (byte)v;
            }
        }

        private void WritePaddedByteArray(in Variant value, int expectedCount)
        {
            value.TryGetValue(out ArrayOf<byte> arr);
            int actual = arr.IsNull ? 0 : arr.Count;
            EnsureArrayWithinBounds(actual, expectedCount);
            EnsureCapacity(expectedCount);
            for (int i = 0; i < expectedCount; i++)
            {
                m_buffer[m_origin + m_position++] = i < actual ? arr[i] : (byte)0;
            }
        }

        private void WritePaddedInt16Array(in Variant value, int expectedCount)
        {
            value.TryGetValue(out ArrayOf<short> arr);
            int actual = arr.IsNull ? 0 : arr.Count;
            EnsureArrayWithinBounds(actual, expectedCount);
            EnsureCapacity(checked(expectedCount * 2));
            for (int i = 0; i < expectedCount; i++)
            {
                short v = i < actual ? arr[i] : (short)0;
                BinaryPrimitives.WriteInt16LittleEndian(
                    new Span<byte>(m_buffer, m_origin + m_position, 2), v);
                m_position += 2;
            }
        }

        private void WritePaddedUInt16Array(in Variant value, int expectedCount)
        {
            value.TryGetValue(out ArrayOf<ushort> arr);
            int actual = arr.IsNull ? 0 : arr.Count;
            EnsureArrayWithinBounds(actual, expectedCount);
            EnsureCapacity(checked(expectedCount * 2));
            for (int i = 0; i < expectedCount; i++)
            {
                ushort v = i < actual ? arr[i] : (ushort)0;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    new Span<byte>(m_buffer, m_origin + m_position, 2), v);
                m_position += 2;
            }
        }

        private void WritePaddedInt32Array(in Variant value, int expectedCount)
        {
            value.TryGetValue(out ArrayOf<int> arr);
            int actual = arr.IsNull ? 0 : arr.Count;
            EnsureArrayWithinBounds(actual, expectedCount);
            EnsureCapacity(checked(expectedCount * 4));
            for (int i = 0; i < expectedCount; i++)
            {
                int v = i < actual ? arr[i] : 0;
                BinaryPrimitives.WriteInt32LittleEndian(
                    new Span<byte>(m_buffer, m_origin + m_position, 4), v);
                m_position += 4;
            }
        }

        private void WritePaddedUInt32Array(in Variant value, int expectedCount)
        {
            value.TryGetValue(out ArrayOf<uint> arr);
            int actual = arr.IsNull ? 0 : arr.Count;
            EnsureArrayWithinBounds(actual, expectedCount);
            EnsureCapacity(checked(expectedCount * 4));
            for (int i = 0; i < expectedCount; i++)
            {
                uint v = i < actual ? arr[i] : 0u;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    new Span<byte>(m_buffer, m_origin + m_position, 4), v);
                m_position += 4;
            }
        }

        private void WritePaddedInt64Array(in Variant value, int expectedCount)
        {
            value.TryGetValue(out ArrayOf<long> arr);
            int actual = arr.IsNull ? 0 : arr.Count;
            EnsureArrayWithinBounds(actual, expectedCount);
            EnsureCapacity(checked(expectedCount * 8));
            for (int i = 0; i < expectedCount; i++)
            {
                long v = i < actual ? arr[i] : 0L;
                BinaryPrimitives.WriteInt64LittleEndian(
                    new Span<byte>(m_buffer, m_origin + m_position, 8), v);
                m_position += 8;
            }
        }

        private void WritePaddedUInt64Array(in Variant value, int expectedCount)
        {
            value.TryGetValue(out ArrayOf<ulong> arr);
            int actual = arr.IsNull ? 0 : arr.Count;
            EnsureArrayWithinBounds(actual, expectedCount);
            EnsureCapacity(checked(expectedCount * 8));
            for (int i = 0; i < expectedCount; i++)
            {
                ulong v = i < actual ? arr[i] : 0UL;
                BinaryPrimitives.WriteUInt64LittleEndian(
                    new Span<byte>(m_buffer, m_origin + m_position, 8), v);
                m_position += 8;
            }
        }

        private void WritePaddedFloatArray(in Variant value, int expectedCount)
        {
            value.TryGetValue(out ArrayOf<float> arr);
            int actual = arr.IsNull ? 0 : arr.Count;
            EnsureArrayWithinBounds(actual, expectedCount);
            EnsureCapacity(checked(expectedCount * 4));
            for (int i = 0; i < expectedCount; i++)
            {
                float v = i < actual ? arr[i] : 0f;
                WriteFloatLittleEndian(m_buffer, m_origin + m_position, v);
                m_position += 4;
            }
        }

        private void WritePaddedDoubleArray(in Variant value, int expectedCount)
        {
            value.TryGetValue(out ArrayOf<double> arr);
            int actual = arr.IsNull ? 0 : arr.Count;
            EnsureArrayWithinBounds(actual, expectedCount);
            EnsureCapacity(checked(expectedCount * 8));
            for (int i = 0; i < expectedCount; i++)
            {
                double v = i < actual ? arr[i] : 0d;
                WriteDoubleLittleEndian(m_buffer, m_origin + m_position, v);
                m_position += 8;
            }
        }

        private void WritePaddedStringArray(
            in Variant value, int expectedCount, uint maxStringLength)
        {
            value.TryGetValue(out ArrayOf<string> arr);
            int actual = arr.IsNull ? 0 : arr.Count;
            EnsureArrayWithinBounds(actual, expectedCount);
            for (int i = 0; i < expectedCount; i++)
            {
                string s = i < actual ? (arr[i] ?? string.Empty) : string.Empty;
                WritePaddedUtf8(s, maxStringLength);
            }
        }

        private void WritePaddedByteStringArray(
            in Variant value, int expectedCount, uint maxStringLength)
        {
            value.TryGetValue(out ArrayOf<ByteString> arr);
            int actual = arr.IsNull ? 0 : arr.Count;
            EnsureArrayWithinBounds(actual, expectedCount);
            for (int i = 0; i < expectedCount; i++)
            {
                ByteString bs = i < actual ? arr[i] : default;
                WritePaddedBytes(bs, maxStringLength);
            }
        }

        private static void EnsureArrayWithinBounds(int actual, int expectedCount)
        {
            if (actual > expectedCount)
            {
                throw new ArgumentException(
                    $"ArrayDimensions exceeded: payload has {actual} elements but only " +
                    $"{expectedCount} elements are allowed.");
            }
        }

        private static void WriteFloatLittleEndian(byte[] buffer, int offset, float value)
        {
#if NET5_0_OR_GREATER
            BinaryPrimitives.WriteSingleLittleEndian(
                new Span<byte>(buffer, offset, 4), value);
#else
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            BinaryPrimitives.WriteInt32LittleEndian(
                new Span<byte>(buffer, offset, 4), bits);
#endif
        }

        private static void WriteDoubleLittleEndian(byte[] buffer, int offset, double value)
        {
#if NET5_0_OR_GREATER
            BinaryPrimitives.WriteDoubleLittleEndian(
                new Span<byte>(buffer, offset, 8), value);
#else
            long bits = BitConverter.DoubleToInt64Bits(value);
            BinaryPrimitives.WriteInt64LittleEndian(
                new Span<byte>(buffer, offset, 8), bits);
#endif
        }

        private static void WriteRawScalarCore(
            BinaryEncoder encoder, Variant value, BuiltInType builtInType)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    encoder.WriteBoolean(null, value.TryGetValue(out bool b) && b);
                    break;
                case BuiltInType.SByte:
                    value.TryGetValue(out sbyte sb);
                    encoder.WriteSByte(null, sb);
                    break;
                case BuiltInType.Byte:
                    value.TryGetValue(out byte by);
                    encoder.WriteByte(null, by);
                    break;
                case BuiltInType.Int16:
                    value.TryGetValue(out short i16);
                    encoder.WriteInt16(null, i16);
                    break;
                case BuiltInType.UInt16:
                    value.TryGetValue(out ushort u16);
                    encoder.WriteUInt16(null, u16);
                    break;
                case BuiltInType.Int32:
                    value.TryGetValue(out int i32);
                    encoder.WriteInt32(null, i32);
                    break;
                case BuiltInType.UInt32:
                    value.TryGetValue(out uint u32);
                    encoder.WriteUInt32(null, u32);
                    break;
                case BuiltInType.Int64:
                    value.TryGetValue(out long i64);
                    encoder.WriteInt64(null, i64);
                    break;
                case BuiltInType.UInt64:
                    value.TryGetValue(out ulong u64);
                    encoder.WriteUInt64(null, u64);
                    break;
                case BuiltInType.Float:
                    value.TryGetValue(out float f);
                    encoder.WriteFloat(null, f);
                    break;
                case BuiltInType.Double:
                    value.TryGetValue(out double d);
                    encoder.WriteDouble(null, d);
                    break;
                case BuiltInType.String:
                    value.TryGetValue(out string s);
                    encoder.WriteString(null, s ?? string.Empty);
                    break;
                case BuiltInType.DateTime:
                    value.TryGetValue(out DateTimeUtc dt);
                    encoder.WriteDateTime(null, dt);
                    break;
                case BuiltInType.Guid:
                    value.TryGetValue(out Uuid g);
                    encoder.WriteGuid(null, g);
                    break;
                case BuiltInType.ByteString:
                    value.TryGetValue(out ByteString bs);
                    encoder.WriteByteString(null, bs);
                    break;
                case BuiltInType.XmlElement:
                    value.TryGetValue(out XmlElement xml);
                    encoder.WriteXmlElement(null, xml);
                    break;
                case BuiltInType.NodeId:
                    value.TryGetValue(out NodeId nid);
                    encoder.WriteNodeId(null, nid);
                    break;
                case BuiltInType.ExpandedNodeId:
                    value.TryGetValue(out ExpandedNodeId enid);
                    encoder.WriteExpandedNodeId(null, enid);
                    break;
                case BuiltInType.StatusCode:
                    value.TryGetValue(out StatusCode sc);
                    encoder.WriteStatusCode(null, sc);
                    break;
                case BuiltInType.QualifiedName:
                    value.TryGetValue(out QualifiedName qn);
                    encoder.WriteQualifiedName(null, qn);
                    break;
                case BuiltInType.LocalizedText:
                    value.TryGetValue(out LocalizedText lt);
                    encoder.WriteLocalizedText(null, lt);
                    break;
                case BuiltInType.Variant:
                    encoder.WriteVariant(null, value);
                    break;
                case BuiltInType.DataValue:
                    value.TryGetValue(out DataValue dv);
                    encoder.WriteDataValue(null, dv);
                    break;
                case BuiltInType.ExtensionObject:
                    value.TryGetValue(out ExtensionObject eo);
                    encoder.WriteExtensionObject(null, eo);
                    break;
                default:
                    encoder.WriteVariant(null, value);
                    break;
            }
        }

        private static void WriteRawArrayCore(
            BinaryEncoder encoder, Variant value, BuiltInType builtInType)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    if (value.TryGetValue(out ArrayOf<bool> bools))
                    {
                        encoder.WriteBooleanArray(null, bools);
                    }
                    else
                    {
                        encoder.WriteInt32(null, -1);
                    }
                    break;
                case BuiltInType.Byte:
                    if (value.TryGetValue(out ArrayOf<byte> bytes))
                    {
                        encoder.WriteByteArray(null, bytes);
                    }
                    else
                    {
                        encoder.WriteInt32(null, -1);
                    }
                    break;
                case BuiltInType.SByte:
                    if (value.TryGetValue(out ArrayOf<sbyte> sbytes))
                    {
                        encoder.WriteSByteArray(null, sbytes);
                    }
                    else
                    {
                        encoder.WriteInt32(null, -1);
                    }
                    break;
                case BuiltInType.UInt16:
                    if (value.TryGetValue(out ArrayOf<ushort> u16))
                    {
                        encoder.WriteUInt16Array(null, u16);
                    }
                    else
                    {
                        encoder.WriteInt32(null, -1);
                    }
                    break;
                case BuiltInType.Int16:
                    if (value.TryGetValue(out ArrayOf<short> i16))
                    {
                        encoder.WriteInt16Array(null, i16);
                    }
                    else
                    {
                        encoder.WriteInt32(null, -1);
                    }
                    break;
                case BuiltInType.UInt32:
                    if (value.TryGetValue(out ArrayOf<uint> u32))
                    {
                        encoder.WriteUInt32Array(null, u32);
                    }
                    else
                    {
                        encoder.WriteInt32(null, -1);
                    }
                    break;
                case BuiltInType.Int32:
                    if (value.TryGetValue(out ArrayOf<int> i32))
                    {
                        encoder.WriteInt32Array(null, i32);
                    }
                    else
                    {
                        encoder.WriteInt32(null, -1);
                    }
                    break;
                case BuiltInType.UInt64:
                    if (value.TryGetValue(out ArrayOf<ulong> u64))
                    {
                        encoder.WriteUInt64Array(null, u64);
                    }
                    else
                    {
                        encoder.WriteInt32(null, -1);
                    }
                    break;
                case BuiltInType.Int64:
                    if (value.TryGetValue(out ArrayOf<long> i64))
                    {
                        encoder.WriteInt64Array(null, i64);
                    }
                    else
                    {
                        encoder.WriteInt32(null, -1);
                    }
                    break;
                case BuiltInType.Float:
                    if (value.TryGetValue(out ArrayOf<float> floats))
                    {
                        encoder.WriteFloatArray(null, floats);
                    }
                    else
                    {
                        encoder.WriteInt32(null, -1);
                    }
                    break;
                case BuiltInType.Double:
                    if (value.TryGetValue(out ArrayOf<double> doubles))
                    {
                        encoder.WriteDoubleArray(null, doubles);
                    }
                    else
                    {
                        encoder.WriteInt32(null, -1);
                    }
                    break;
                case BuiltInType.String:
                    if (value.TryGetValue(out ArrayOf<string> strings))
                    {
                        encoder.WriteStringArray(null, strings);
                    }
                    else
                    {
                        encoder.WriteInt32(null, -1);
                    }
                    break;
                case BuiltInType.Variant:
                    if (value.TryGetValue(out ArrayOf<Variant> variants))
                    {
                        encoder.WriteVariantArray(null, variants);
                    }
                    else
                    {
                        encoder.WriteInt32(null, -1);
                    }
                    break;
                default:
                    encoder.WriteVariant(null, value);
                    break;
            }
        }

        private void EnsureCapacity(int byteCount)
        {
            if (m_position + byteCount > m_length)
            {
                throw new InvalidOperationException(
                    $"UADP writer needs {byteCount} bytes but only {m_length - m_position} remain.");
            }
        }
    }
}
