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
using System.Text;

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Reads bounded binary CONTROL fields without retaining or copying the encoded body.
    /// </summary>
    internal ref struct FlowBinaryReader
    {
        /// <summary>
        /// Borrows an already admitted complete CONTROL body.
        /// </summary>
        internal FlowBinaryReader(ReadOnlySpan<byte> source)
        {
            m_remaining = source;
        }

        /// <summary>
        /// Reads one byte.
        /// </summary>
        internal byte Byte()
        {
            return Take(1)[0];
        }

        /// <summary>
        /// Reads one unsigned little-endian integer.
        /// </summary>
        internal uint UInt32()
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        }

        /// <summary>
        /// Reads one unsigned little-endian long integer.
        /// </summary>
        internal ulong UInt64()
        {
            return BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
        }

        /// <summary>
        /// Reads a closed discriminator without numeric fallback.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal byte Discriminator(byte maximum)
        {
            byte value = Byte();
            if (value > maximum)
            {
                throw new FlowCodecException(FlowCodecError.UnknownDiscriminator);
            }
            return value;
        }

        /// <summary>
        /// Reads only the two defined Boolean encodings.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal bool Boolean()
        {
            return Byte() switch
            {
                0 => false,
                1 => true,
                _ => throw new FlowCodecException(FlowCodecError.OutOfRange)
            };
        }

        /// <summary>
        /// Reads a finite nonnegative binary64 value and canonicalizes either zero representation.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal double Rate()
        {
            double value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(Take(8)));
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                throw new FlowCodecException(FlowCodecError.OutOfRange);
            }
            return value == 0 ? 0 : value;
        }

        /// <summary>
        /// Reads the OPC UA Binary Guid field order without allocating a byte array.
        /// </summary>
        internal Guid Guid()
        {
            ReadOnlySpan<byte> bytes = Take(16);
            return new Guid(
                BinaryPrimitives.ReadInt32LittleEndian(bytes),
                BinaryPrimitives.ReadInt16LittleEndian(bytes[4..]),
                BinaryPrimitives.ReadInt16LittleEndian(bytes[6..]),
                bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15]);
        }

        /// <summary>
        /// Reads the fixed-size challenge into storage owned by the decoded value.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal ByteString Challenge()
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(Take(4)) != 32)
            {
                throw new FlowCodecException(FlowCodecError.InvalidChallenge);
            }
            return new ByteString(Take(32).ToArray());
        }

        /// <summary>
        /// Validates the encoded length and Unicode before allocating the decoded string.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal unsafe string String(int maximumBytes)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(Take(4));
            if (length <= 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidLength);
            }
            if (length > maximumBytes)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            ReadOnlySpan<byte> bytes = Take(length);
            FlowWireText.ValidateUtf8(bytes);
            // The pointer overload avoids an intermediate encoded-byte copy on net48 as well.
            fixed (byte* pointer = bytes)
            {
                return Encoding.UTF8.GetString(pointer, length);
            }
        }

        /// <summary>
        /// Requires the operation to consume the complete body.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal readonly void RequireEnd()
        {
            if (!m_remaining.IsEmpty)
            {
                throw new FlowCodecException(FlowCodecError.TrailingBytes);
            }
        }

        /// <summary>
        /// Advances only within the admitted input.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private ReadOnlySpan<byte> Take(int count)
        {
            if (count > m_remaining.Length)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
            ReadOnlySpan<byte> value = m_remaining[..count];
            m_remaining = m_remaining[count..];
            return value;
        }

        private ReadOnlySpan<byte> m_remaining;
    }

    /// <summary>
    /// Counts or writes canonical binary CONTROL fields into one bounded owned allocation.
    /// </summary>
    internal ref struct FlowBinaryWriter
    {
        /// <summary>
        /// Creates a counting pass for an empty destination, or a bounded writing pass otherwise.
        /// </summary>
        internal FlowBinaryWriter(Span<byte> destination, int maximum)
        {
            m_destination = destination;
            m_maximum = maximum;
            m_length = 0;
        }

        /// <summary>
        /// Gets the exact encoded size.
        /// </summary>
        internal readonly int Length => m_length;

        /// <summary>
        /// Writes one byte.
        /// </summary>
        internal void Byte(byte value)
        {
            Span<byte> target = Reserve(1);
            if (!target.IsEmpty)
            {
                target[0] = value;
            }
        }

        /// <summary>
        /// Writes one unsigned little-endian integer.
        /// </summary>
        internal void UInt32(uint value)
        {
            Span<byte> target = Reserve(4);
            if (!target.IsEmpty)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(target, value);
            }
        }

        /// <summary>
        /// Writes one unsigned little-endian long integer.
        /// </summary>
        internal void UInt64(ulong value)
        {
            Span<byte> target = Reserve(8);
            if (!target.IsEmpty)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(target, value);
            }
        }

        /// <summary>
        /// Writes the fixed OPC UA Binary Guid representation.
        /// </summary>
        internal void Guid(Guid value)
        {
            Span<byte> target = Reserve(16);
            if (!target.IsEmpty)
            {
#if NET8_0_OR_GREATER
                value.TryWriteBytes(target);
#else
                value.ToByteArray().AsSpan().CopyTo(target);
#endif
            }
        }

        /// <summary>
        /// Writes a canonical finite nonnegative binary64 rate.
        /// </summary>
        internal void Rate(double value)
        {
            Span<byte> target = Reserve(8);
            if (!target.IsEmpty)
            {
                BinaryPrimitives.WriteInt64LittleEndian(target, BitConverter.DoubleToInt64Bits(value == 0 ? 0 : value));
            }
        }

        /// <summary>
        /// Writes a Boolean as exactly one byte.
        /// </summary>
        internal void Boolean(bool value)
        {
            Byte(value ? (byte)1 : (byte)0);
        }

        /// <summary>
        /// Writes the challenge's fixed length and owned bytes.
        /// </summary>
        internal void Challenge(ByteString value)
        {
            UInt32(32);
            Raw(value.Span);
        }

        /// <summary>
        /// Writes a strict UTF-8 string without escape processing or an intermediate byte array.
        /// </summary>
        internal void String(string value)
        {
            UInt32((uint)FlowWireText.Utf8Length(value));
            for (int index = 0; index < value.Length; index++)
            {
                int scalar = FlowWireText.ReadScalar(value, ref index);
                if (scalar < 0x80)
                {
                    Byte((byte)scalar);
                }
                else if (scalar < 0x800)
                {
                    Byte((byte)(0xC0 | (scalar >> 6)));
                    Byte((byte)(0x80 | (scalar & 0x3F)));
                }
                else if (scalar < 0x10000)
                {
                    Byte((byte)(0xE0 | (scalar >> 12)));
                    Byte((byte)(0x80 | ((scalar >> 6) & 0x3F)));
                    Byte((byte)(0x80 | (scalar & 0x3F)));
                }
                else
                {
                    Byte((byte)(0xF0 | (scalar >> 18)));
                    Byte((byte)(0x80 | ((scalar >> 12) & 0x3F)));
                    Byte((byte)(0x80 | ((scalar >> 6) & 0x3F)));
                    Byte((byte)(0x80 | (scalar & 0x3F)));
                }
            }
        }

        /// <summary>
        /// Writes bytes whose shape has already been validated.
        /// </summary>
        private void Raw(scoped ReadOnlySpan<byte> bytes)
        {
            Span<byte> target = Reserve(bytes.Length);
            if (!target.IsEmpty)
            {
                bytes.CopyTo(target);
            }
        }

        /// <summary>
        /// Checks the full field before advancing either counting or writing state.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private Span<byte> Reserve(int count)
        {
            if (count > m_maximum - m_length)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            int offset = m_length;
            m_length += count;
            return m_destination.IsEmpty ? [] : m_destination.Slice(offset, count);
        }

        private readonly Span<byte> m_destination;
        private readonly int m_maximum;
        private int m_length;
    }
}
