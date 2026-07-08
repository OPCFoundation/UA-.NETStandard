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
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Frame
{
    /// <summary>
    /// A packet record read from a libpcap file.
    /// </summary>
    public readonly struct PcapRecord : IEquatable<PcapRecord>
    {
        /// <summary>
        /// Constructs a pcap packet record.
        /// </summary>
        public PcapRecord(DateTimeOffset timestamp, uint linkType, int originalLength, ReadOnlyMemory<byte> data)
        {
            Timestamp = timestamp;
            LinkType = linkType;
            OriginalLength = originalLength;
            Data = data;
        }

        /// <summary>
        /// Packet timestamp.
        /// </summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>
        /// Link type from the global header.
        /// </summary>
        public uint LinkType { get; }

        /// <summary>
        /// Original packet length.
        /// </summary>
        public int OriginalLength { get; }

        /// <summary>
        /// Captured packet bytes.
        /// </summary>
        public ReadOnlyMemory<byte> Data { get; }

        /// <inheritdoc/>
        public bool Equals(PcapRecord other)
        {
            return Timestamp.Equals(other.Timestamp) &&
                LinkType == other.LinkType &&
                OriginalLength == other.OriginalLength &&
                Data.Span.SequenceEqual(other.Data.Span);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is PcapRecord other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Timestamp, LinkType, OriginalLength, Data.Length);
        }

        /// <summary>
        /// Equality comparison.
        /// </summary>
        public static bool operator ==(PcapRecord left, PcapRecord right) => left.Equals(right);

        /// <summary>
        /// Inequality comparison.
        /// </summary>
        public static bool operator !=(PcapRecord left, PcapRecord right) => !left.Equals(right);
    }

    /// <summary>
    /// Reads packet records from a libpcap file.
    /// </summary>
    public static class PcapFileReader
    {
        /// <summary>
        /// Maximum number of bytes allowed for a single captured packet.
        /// Defends against a malicious pcap whose record header declares
        /// an absurd <c>capturedLength</c> that would otherwise trigger
        /// an <see cref="OutOfMemoryException"/> when allocating the
        /// payload buffer.
        /// </summary>
        public const int MaxPacketBytes = 64 * 1024 * 1024;

        /// <summary>
        /// Reads all packet records from the supplied libpcap file.
        /// </summary>
        public static IAsyncEnumerable<PcapRecord> ReadAllAsync(
            string filePath,
            CancellationToken ct)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            return ReadAllCoreAsync(filePath, ct);
        }

        private static async IAsyncEnumerable<PcapRecord> ReadAllCoreAsync(
            string filePath,
            [EnumeratorCancellation] CancellationToken ct)
        {
            using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] globalHeader = new byte[24];
            if (!await ReadExactOrEndAsync(stream, globalHeader, ct).ConfigureAwait(false))
            {
                yield break;
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(globalHeader);
            bool littleEndian = magic switch
            {
                0xA1B2C3D4U => true,
                0xD4C3B2A1U => false,
                _ => throw new PcapDiagnosticsException("Invalid pcap magic number.")
            };
            uint linkType = ReadUInt32(globalHeader.AsSpan(20, 4), littleEndian);
            byte[] recordHeader = new byte[16];
            while (await ReadExactOrEndAsync(stream, recordHeader, ct).ConfigureAwait(false))
            {
                uint tsSec = ReadUInt32(recordHeader.AsSpan(0, 4), littleEndian);
                uint tsUsec = ReadUInt32(recordHeader.AsSpan(4, 4), littleEndian);
                uint capturedLength = ReadUInt32(recordHeader.AsSpan(8, 4), littleEndian);
                uint originalLength = ReadUInt32(recordHeader.AsSpan(12, 4), littleEndian);
                if (capturedLength > MaxPacketBytes)
                {
                    throw new PcapDiagnosticsException(
                        $"Pcap record capturedLength {capturedLength} exceeds " +
                        $"MaxPacketBytes ({MaxPacketBytes}). The file may be " +
                        "malformed or hostile; refusing to allocate.");
                }

                long remaining = stream.Length - stream.Position;
                if (capturedLength > remaining)
                {
                    throw new PcapDiagnosticsException("Truncated pcap packet record.");
                }

                byte[] data;
                try
                {
                    data = new byte[(int)capturedLength];
                }
                catch (OutOfMemoryException ex)
                {
                    throw new PcapDiagnosticsException(
                        $"Could not allocate {capturedLength}-byte capture frame buffer.",
                        ex);
                }

                if (!await ReadExactOrEndAsync(stream, data, ct).ConfigureAwait(false))
                {
                    throw new PcapDiagnosticsException("Truncated pcap packet record.");
                }

                DateTimeOffset timestamp = DateTimeOffset.UnixEpoch.AddSeconds(tsSec).AddTicks(tsUsec * 10L);
                yield return new PcapRecord(timestamp, linkType, checked((int)originalLength), data);
            }
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> value, bool littleEndian)
        {
            return littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(value)
                : BinaryPrimitives.ReadUInt32BigEndian(value);
        }

        private static async ValueTask<bool> ReadExactOrEndAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer[offset..], ct).ConfigureAwait(false);
                if (read == 0)
                {
                    // EOF reached before the buffer was filled. Whether
                    // this is a clean EOF (offset == 0, no bytes consumed)
                    // or a truncated record (offset > 0, partial read) is
                    // up to the caller to interpret. The contract here is
                    // simply: return true only when the full buffer was
                    // read; otherwise return false so the caller can
                    // either break out of a record loop (clean EOF) or
                    // throw a "truncated" diagnostic (data read).
                    return false;
                }
                offset += read;
            }
            return true;
        }
    }
}
