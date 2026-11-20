/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Pcap.Frame
{
    /// <summary>
    /// Writes a minimal little-endian pcapng stream with one interface.
    /// </summary>
    public sealed class PcapNgFileWriter : IAsyncDisposable
    {
        /// <summary>
        /// Constructs a pcapng writer over the supplied stream.
        /// </summary>
        public PcapNgFileWriter(Stream stream, uint linkType)
        {
            ArgumentNullException.ThrowIfNull(stream);

            m_stream = stream;
            WriteShb(stream);
            WriteIdb(stream, linkType);
        }

        /// <summary>
        /// Writes a single Enhanced Packet Block.
        /// </summary>
        public async ValueTask WriteAsync(
            DateTimeOffset timestamp,
            ReadOnlyMemory<byte> packetData,
            CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ulong timestampMicros = checked((ulong)((timestamp.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks) / 10));
                int pad = (4 - (packetData.Length & 3)) & 3;
                uint totalLength = checked((uint)(8 + 20 + packetData.Length + pad + 4));
                byte[] header = new byte[28];
                BinaryPrimitives.WriteUInt32LittleEndian(header, BlockTypeEpb);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), totalLength);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 0);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)(timestampMicros >> 32));
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), (uint)timestampMicros);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), checked((uint)packetData.Length));
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), checked((uint)packetData.Length));
                await m_stream.WriteAsync(header, ct).ConfigureAwait(false);
                await m_stream.WriteAsync(packetData, ct).ConfigureAwait(false);
                if (pad != 0)
                {
                    await m_stream.WriteAsync(s_padding.AsMemory(0, pad), ct).ConfigureAwait(false);
                }
                byte[] trailer = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(trailer, totalLength);
                await m_stream.WriteAsync(trailer, ct).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            await m_gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                m_disposed = true;
                await m_stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                await m_stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
                m_gate.Dispose();
            }
        }

        private static void WriteShb(Stream stream)
        {
            Span<byte> block = stackalloc byte[28];
            BinaryPrimitives.WriteUInt32LittleEndian(block, BlockTypeShb);
            BinaryPrimitives.WriteUInt32LittleEndian(block[4..], 28);
            BinaryPrimitives.WriteUInt32LittleEndian(block[8..], ByteOrderMagic);
            BinaryPrimitives.WriteUInt16LittleEndian(block[12..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(block[14..], 0);
            BinaryPrimitives.WriteInt64LittleEndian(block[16..], -1);
            BinaryPrimitives.WriteUInt32LittleEndian(block[24..], 28);
            stream.Write(block);
        }

        private static void WriteIdb(Stream stream, uint linkType)
        {
            Span<byte> block = stackalloc byte[20];
            BinaryPrimitives.WriteUInt32LittleEndian(block, BlockTypeIdb);
            BinaryPrimitives.WriteUInt32LittleEndian(block[4..], 20);
            BinaryPrimitives.WriteUInt16LittleEndian(block[8..], checked((ushort)linkType));
            BinaryPrimitives.WriteUInt16LittleEndian(block[10..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(block[12..], 65535);
            BinaryPrimitives.WriteUInt32LittleEndian(block[16..], 20);
            stream.Write(block);
        }

        private const uint BlockTypeShb = 0x0A0D0D0A;
        private const uint BlockTypeIdb = 0x00000001;
        private const uint BlockTypeEpb = 0x00000006;
        private const uint ByteOrderMagic = 0x1A2B3C4D;
        private static readonly byte[] s_padding = new byte[3];
        private readonly Stream m_stream;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private bool m_disposed;
    }
}
