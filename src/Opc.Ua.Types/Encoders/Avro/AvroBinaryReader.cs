/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Reads Avro binary primitive values from the underlying stream.
    /// </summary>
    internal sealed class AvroBinaryReader
    {
        private const int BufferSize = 8192;
        private const int StackallocThreshold = 256;
        private readonly Stream m_stream;
        private byte[] m_buffer;
        private int m_bufferOffset;
        private int m_bufferLength;
        private bool m_released;

        /// <summary>
        /// Initializes a new AvroBinaryReader instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "stream">The stream that receives or supplies the encoded payload.</param>
        public AvroBinaryReader(Stream stream)
        {
            m_stream = stream;
            m_buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        }

        /// <summary>
        /// Returns the pooled read buffer to the shared array pool.
        /// </summary>
        public void Release()
        {
            if (m_released)
            {
                return;
            }

            m_released = true;
            byte[] buffer = m_buffer;
            m_buffer = Array.Empty<byte>();
            ArrayPool<byte>.Shared.Return(buffer);
        }

        /// <summary>
        /// Reads Boolean from the experimental encoded representation.
        /// </summary>
        /// <returns>The result produced by this codec helper.</returns>
        public bool ReadBoolean()
        {
            return ReadByte() != 0;
        }

        /// <summary>
        /// Reads Byte from the experimental encoded representation.
        /// </summary>
        /// <returns>The result produced by this codec helper.</returns>
        public byte ReadByte()
        {
            if (m_bufferOffset == m_bufferLength)
            {
                FillBuffer();
            }

            return m_buffer[m_bufferOffset++];
        }

        /// <summary>
        /// Reads Fixed from the experimental encoded representation.
        /// </summary>
        /// <param name = "length">The number of bytes to read.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public byte[] ReadFixed(int length)
        {
            byte[] bytes = new byte[length];
            ReadExactly(bytes);
            return bytes;
        }

        /// <summary>
        /// Reads Float from the experimental encoded representation.
        /// </summary>
        /// <returns>The result produced by this codec helper.</returns>
        public float ReadFloat()
        {
            Span<byte> b = stackalloc byte[4];
            ReadExactly(b);
            return EncoderCompat.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b));
        }

        /// <summary>
        /// Reads Double from the experimental encoded representation.
        /// </summary>
        /// <returns>The result produced by this codec helper.</returns>
        public double ReadDouble()
        {
            Span<byte> b = stackalloc byte[8];
            ReadExactly(b);
            return EncoderCompat.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(b));
        }

        /// <summary>
        /// Reads Int from the experimental encoded representation.
        /// </summary>
        /// <returns>The result produced by this codec helper.</returns>
        public int ReadInt()
        {
            return checked((int)ReadLong());
        }

        /// <summary>
        /// Reads Long from the experimental encoded representation.
        /// </summary>
        /// <returns>The result produced by this codec helper.</returns>
        public long ReadLong()
        {
            ulong raw = 0;
            int shift = 0;
            while (shift < 64)
            {
                byte b = ReadByte();
                raw |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return (long)(raw >> 1) ^ -((long)raw & 1L);
                }

                shift += 7;
            }

            throw new FormatException("Invalid Avro variable-length integer.");
        }

        /// <summary>
        /// Reads one OPC UA byte string from the Avro binary stream.
        /// </summary>
        /// <param name = "maxLength">The maximum permitted length, or 0 for unbounded.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public byte[] ReadBytes(int maxLength = 0)
        {
            long length = ReadLong();
            if (length < 0 || length > int.MaxValue)
            {
                throw new FormatException("Invalid Avro byte length.");
            }

            if (maxLength > 0 && length > maxLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxByteStringLength {0} < {1}",
                    maxLength,
                    length);
            }

            byte[] bytes = new byte[length];
            ReadExactly(bytes);
            return bytes;
        }

        /// <summary>
        /// Reads String from the experimental encoded representation.
        /// </summary>
        /// <param name = "maxLength">The maximum permitted length, or 0 for unbounded.</param>
        /// <returns>The result produced by this codec helper.</returns>
        public string ReadString(int maxLength = 0)
        {
            long length = ReadLong();
            if (length < 0 || length > int.MaxValue)
            {
                throw new FormatException("Invalid Avro string length.");
            }

            if (maxLength > 0 && length > maxLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxStringLength {0} < {1}",
                    maxLength,
                    length);
            }

            if (length == 0)
            {
                return string.Empty;
            }

            int count = (int)length;
            if (count <= m_bufferLength - m_bufferOffset)
            {
                string value = Encoding.UTF8.GetString(m_buffer.AsSpan(m_bufferOffset, count));
                m_bufferOffset += count;
                return value;
            }

            if (count <= StackallocThreshold)
            {
                Span<byte> bytes = stackalloc byte[StackallocThreshold];
                ReadExactly(bytes[..count]);
                return Encoding.UTF8.GetString(bytes[..count]);
            }

            byte[] rented = ArrayPool<byte>.Shared.Rent(count);
            try
            {
                Span<byte> bytes = rented.AsSpan(0, count);
                ReadExactly(bytes);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        private void ReadExactly(Span<byte> buffer)
        {
            while (buffer.Length > 0)
            {
                int buffered = m_bufferLength - m_bufferOffset;
                if (buffered > 0)
                {
                    int copy = Math.Min(buffered, buffer.Length);
                    m_buffer.AsSpan(m_bufferOffset, copy).CopyTo(buffer);
                    m_bufferOffset += copy;
                    buffer = buffer[copy..];
                    continue;
                }

                if (buffer.Length >= m_buffer.Length)
                {
                    m_stream.ReadExactly(buffer);
                    return;
                }

                FillBuffer();
            }
        }

        private void FillBuffer()
        {
            m_bufferOffset = 0;
            m_bufferLength = m_stream.Read(m_buffer);
            if (m_bufferLength == 0)
            {
                throw new EndOfStreamException();
            }
        }
    }
}
