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
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
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
