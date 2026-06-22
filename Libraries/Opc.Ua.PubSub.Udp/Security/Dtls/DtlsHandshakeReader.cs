/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Buffers.Binary;

namespace Opc.Ua.PubSub.Udp.Security.Dtls
{
    internal ref struct DtlsHandshakeReader
    {
        public DtlsHandshakeReader(ReadOnlySpan<byte> data)
        {
            m_data = data;
            m_offset = 0;
        }

        public bool EndOfData => m_offset == m_data.Length;

        public byte ReadByte()
        {
            EnsureAvailable(1);
            return m_data[m_offset++];
        }

        public ushort ReadUInt16()
        {
            EnsureAvailable(2);
            ushort value = BinaryPrimitives.ReadUInt16BigEndian(m_data.Slice(m_offset, 2));
            m_offset += 2;
            return value;
        }

        public byte[] ReadBytes(int length)
        {
            EnsureAvailable(length);
            byte[] value = m_data.Slice(m_offset, length).ToArray();
            m_offset += length;
            return value;
        }

        public byte[] ReadOpaque8()
        {
            int length = ReadByte();
            return ReadBytes(length);
        }

        public byte[] ReadOpaque16()
        {
            int length = ReadUInt16();
            return ReadBytes(length);
        }

        public void EnsureComplete()
        {
            if (!EndOfData)
            {
                throw new DtlsHandshakeException("Trailing bytes remain in DTLS handshake vector.");
            }
        }

        private void EnsureAvailable(int length)
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
