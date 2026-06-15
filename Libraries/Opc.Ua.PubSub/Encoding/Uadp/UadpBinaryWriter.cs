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
