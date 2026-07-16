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

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// Forward-only big-endian reader over a DTLS handshake message body.
    /// </summary>
    internal ref struct DtlsHandshakeReader
    {
        /// <summary>
        /// Initializes a new <see cref="DtlsHandshakeReader"/> over the supplied data.
        /// </summary>
        public DtlsHandshakeReader(ReadOnlySpan<byte> data)
        {
            m_data = data;
            m_offset = 0;
        }

        /// <summary>
        /// Indicates whether all bytes in the buffer have been consumed.
        /// </summary>
        public readonly bool EndOfData => m_offset == m_data.Length;

        /// <summary>
        /// Reads a single byte and advances the cursor.
        /// </summary>
        public byte ReadByte()
        {
            EnsureAvailable(1);
            return m_data[m_offset++];
        }

        /// <summary>
        /// Reads a big-endian 16-bit value and advances the cursor.
        /// </summary>
        public ushort ReadUInt16()
        {
            EnsureAvailable(2);
            ushort value = BinaryPrimitives.ReadUInt16BigEndian(m_data.Slice(m_offset, 2));
            m_offset += 2;
            return value;
        }

        /// <summary>
        /// Reads the requested number of bytes and advances the cursor.
        /// </summary>
        public byte[] ReadBytes(int length)
        {
            EnsureAvailable(length);
            byte[] value = m_data.Slice(m_offset, length).ToArray();
            m_offset += length;
            return value;
        }

        /// <summary>
        /// Reads a byte sequence prefixed with an 8-bit length.
        /// </summary>
        public byte[] ReadOpaque8()
        {
            int length = ReadByte();
            return ReadBytes(length);
        }

        /// <summary>
        /// Reads a byte sequence prefixed with a big-endian 16-bit length.
        /// </summary>
        public byte[] ReadOpaque16()
        {
            int length = ReadUInt16();
            return ReadBytes(length);
        }

        /// <summary>
        /// Throws when unconsumed trailing bytes remain in the buffer.
        /// </summary>
        /// <exception cref="DtlsHandshakeException"></exception>
        public readonly void EnsureComplete()
        {
            if (!EndOfData)
            {
                throw new DtlsHandshakeException("Trailing bytes remain in DTLS handshake vector.");
            }
        }

        private readonly void EnsureAvailable(int length)
        {
            if (length < 0 || m_offset + length > m_data.Length)
            {
                throw new DtlsHandshakeException("DTLS handshake vector is truncated.");
            }
        }

        private readonly ReadOnlySpan<byte> m_data;
        private int m_offset;
    }
}
