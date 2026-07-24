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

namespace Opc.Ua
{
    /// <summary>
    /// Writes Avro binary primitive values to the underlying stream.
    /// </summary>
    internal sealed class AvroBinaryWriter
    {
        private const int BufferSize = 8192;
        private const int StackallocThreshold = 256;
        private readonly Stream m_stream;
        private byte[] m_buffer;
        private int m_buffered;
        private bool m_released;

        /// <summary>
        /// Initializes a new AvroBinaryWriter instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "stream">The stream that receives or supplies the encoded payload.</param>
        public AvroBinaryWriter(Stream stream)
        {
            m_stream = stream;
            m_buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        }

        /// <summary>
        /// Gets the current stream position when the Avro writer can seek.
        /// </summary>
        public long Position
        {
            get { return m_stream.CanSeek ? m_stream.Position + m_buffered : 0; }
        }

        /// <summary>
        /// Flushes buffered Avro binary data to the underlying stream.
        /// </summary>
        public void Flush()
        {
            FlushBuffer();
            m_stream.Flush();
        }

        /// <summary>
        /// Returns the pooled write buffer to the shared array pool.
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
        /// Writes Boolean to the experimental encoded representation.
        /// </summary>
        /// <param name = "value">The primitive or OPC UA value to process.</param>
        public void WriteBoolean(bool value)
        {
            WriteByte(value ? (byte)1 : (byte)0);
        }

        /// <summary>
        /// Writes Fixed to the experimental encoded representation.
        /// </summary>
        /// <param name = "bytes">The byte sequence to encode or decode.</param>
        public void WriteFixed(ReadOnlySpan<byte> bytes)
        {
            WriteRaw(bytes);
        }

        /// <summary>
        /// Writes Float to the experimental encoded representation.
        /// </summary>
        /// <param name = "value">The primitive or OPC UA value to process.</param>
        public void WriteFloat(float value)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(b, EncoderCompat.SingleToInt32Bits(value));
            WriteRaw(b);
        }

        /// <summary>
        /// Writes Double to the experimental encoded representation.
        /// </summary>
        /// <param name = "value">The primitive or OPC UA value to process.</param>
        public void WriteDouble(double value)
        {
            Span<byte> b = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(b, EncoderCompat.DoubleToInt64Bits(value));
            WriteRaw(b);
        }

        /// <summary>
        /// Writes Int to the experimental encoded representation.
        /// </summary>
        /// <param name = "value">The primitive or OPC UA value to process.</param>
        public void WriteInt(int value)
        {
            WriteLong(value);
        }

        /// <summary>
        /// Writes Long to the experimental encoded representation.
        /// </summary>
        /// <param name = "value">The primitive or OPC UA value to process.</param>
        public void WriteLong(long value)
        {
            ulong zigzag = ((ulong)value << 1) ^ (ulong)(value >> 63);
            Span<byte> bytes = stackalloc byte[10];
            int count = 0;
            while ((zigzag & ~0x7FUL) != 0)
            {
                bytes[count++] = (byte)((zigzag & 0x7F) | 0x80);
                zigzag >>= 7;
            }

            bytes[count++] = (byte)zigzag;
            WriteRaw(bytes[..count]);
        }

        /// <summary>
        /// Writes a length-delimited Avro byte sequence.
        /// </summary>
        /// <param name = "value">The primitive or OPC UA value to process.</param>
        public void WriteBytes(ReadOnlySpan<byte> value)
        {
            WriteLong(value.Length);
            WriteRaw(value);
        }

        /// <summary>
        /// Writes a UTF-8 Avro string as a length-delimited field value.
        /// </summary>
        /// <param name = "value">The primitive or OPC UA value to process.</param>
        public void WriteString(string value)
        {
            int length = Encoding.UTF8.GetByteCount(value);
            WriteLong(length);
            if (length <= StackallocThreshold)
            {
                Span<byte> bytes = stackalloc byte[StackallocThreshold];
                int written = Encoding.UTF8.GetBytes(value, bytes);
                WriteRaw(bytes[..written]);
                return;
            }

            byte[] rented = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                Span<byte> bytes = rented.AsSpan(0, length);
                int written = Encoding.UTF8.GetBytes(value, bytes);
                WriteRaw(bytes[..written]);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        private void WriteByte(byte value)
        {
            if (m_buffered == m_buffer.Length)
            {
                FlushBuffer();
            }

            m_buffer[m_buffered++] = value;
        }

        private void WriteRaw(ReadOnlySpan<byte> value)
        {
            if (value.Length == 0)
            {
                return;
            }

            if (value.Length > m_buffer.Length)
            {
                FlushBuffer();
                m_stream.Write(value);
                return;
            }

            if (value.Length > m_buffer.Length - m_buffered)
            {
                FlushBuffer();
            }

            value.CopyTo(m_buffer.AsSpan(m_buffered));
            m_buffered += value.Length;
        }

        private void FlushBuffer()
        {
            if (m_buffered == 0)
            {
                return;
            }

            m_stream.Write(m_buffer.AsSpan(0, m_buffered));
            m_buffered = 0;
        }
    }
}
