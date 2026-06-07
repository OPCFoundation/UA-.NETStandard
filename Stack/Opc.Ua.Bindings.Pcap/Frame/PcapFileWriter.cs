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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings.Pcap.Capture;

namespace Opc.Ua.Bindings.Pcap.Frame
{
    /// <summary>
    /// Writes little-endian libpcap files.
    /// </summary>
    public sealed class PcapFileWriter : IAsyncDisposable
    {
        /// <summary>BSD loopback link type.</summary>
        public const uint LinkTypeNull = 0;

        /// <summary>Ethernet link type.</summary>
        public const uint LinkTypeEthernet = 1;

        /// <summary>Raw IPv4 link type.</summary>
        public const uint LinkTypeRaw = 101;

        /// <summary>IPv4 link type.</summary>
        public const uint LinkTypeIPv4 = 228;

        /// <summary>
        /// Constructs a pcap writer and writes the global header.
        /// </summary>
        public PcapFileWriter(string filePath, uint linkType, uint snapLen = 65535)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            m_stream = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            m_snapLen = snapLen;
            WriteGlobalHeader(m_stream, linkType, snapLen);
        }

        /// <summary>
        /// Writes one packet record.
        /// </summary>
        public async ValueTask WriteAsync(
            DateTimeOffset timestamp,
            ReadOnlyMemory<byte> packetData,
            CancellationToken ct)
        {
            if (packetData.Length > m_snapLen)
            {
                throw new PcapDiagnosticsException("Packet exceeds the configured pcap snap length.");
            }

            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                long micros = (timestamp.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks) / 10;
                byte[] header = new byte[16];
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(), checked((uint)(micros / 1_000_000L)));
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)(micros % 1_000_000L)));
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), checked((uint)packetData.Length));
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), checked((uint)packetData.Length));
                await m_stream.WriteAsync(header, ct).ConfigureAwait(false);
                await m_stream.WriteAsync(packetData, ct).ConfigureAwait(false);
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

        private static void WriteGlobalHeader(Stream stream, uint linkType, uint snapLen)
        {
            Span<byte> header = stackalloc byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(header, 0xA1B2C3D4U);
            BinaryPrimitives.WriteUInt16LittleEndian(header[4..], 2);
            BinaryPrimitives.WriteUInt16LittleEndian(header[6..], 4);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header[12..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..], snapLen);
            BinaryPrimitives.WriteUInt32LittleEndian(header[20..], linkType);
            stream.Write(header);
        }

        private readonly FileStream m_stream;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly uint m_snapLen;
        private bool m_disposed;
    }
}
