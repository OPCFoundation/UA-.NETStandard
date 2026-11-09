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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Frame
{
    /// <summary>
    /// Validates complete capture containers before importing or exporting their raw bytes.
    /// </summary>
    internal static class CaptureFileValidator
    {
        /// <summary>
        /// Rejects a key artifact even when its configured name resembles a capture file.
        /// </summary>
        /// <exception cref="PcapDiagnosticsException">
        /// The capture path identifies the configured key-log artifact.
        /// </exception>
        internal static void RejectKeyLogPath(string capturePath, string? keyLogPath)
        {
            if (!string.IsNullOrWhiteSpace(keyLogPath) &&
                string.Equals(
                    Path.GetFullPath(capturePath),
                    Path.GetFullPath(keyLogPath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new PcapDiagnosticsException("Key-log artifacts cannot be used as packet captures.");
            }
        }

        /// <summary>
        /// Validates an on-disk capture without buffering its packet bodies.
        /// </summary>
        internal static async ValueTask<(FormatKind Kind, long FrameCount)> ValidateFileAsync(
            string filePath,
            CancellationToken ct)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (stream.ConfigureAwait(false))
            {
                return await ValidateAsync(stream, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Validates the exact seekable stream that will be imported or returned, without allocating packet bodies.
        /// </summary>
        internal static async ValueTask<(FormatKind Kind, long FrameCount)> ValidateAsync(
            Stream stream,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ct.ThrowIfCancellationRequested();

            stream.Position = 0;
            byte[] magic = new byte[4];
            await ReadExactlyAsync(stream, magic, ct).ConfigureAwait(false);
            stream.Position = 0;
            if (BinaryPrimitives.ReadUInt32LittleEndian(magic) == 0x0A0D0D0A)
            {
                return (FormatKind.PcapNg, await ValidatePcapNgAsync(stream, ct).ConfigureAwait(false));
            }

            return (FormatKind.Pcap, await ValidatePcapAsync(stream, ct).ConfigureAwait(false));
        }

        /// <summary>
        /// Validates a classic PCAP global header and every complete packet record.
        /// </summary>
        /// <exception cref="PcapDiagnosticsException">
        /// The PCAP magic number, global header, or packet record is invalid.
        /// </exception>
        private static async ValueTask<long> ValidatePcapAsync(Stream stream, CancellationToken ct)
        {
            byte[] header = new byte[24];
            await ReadExactlyAsync(stream, header, ct).ConfigureAwait(false);
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
            bool littleEndian = magic switch
            {
                0xA1B2C3D4 or 0xA1B23C4D => true,
                0xD4C3B2A1 or 0x4D3CB2A1 => false,
                _ => throw new PcapDiagnosticsException("Invalid pcap magic number.")
            };
            bool nanoseconds = magic is 0xA1B23C4D or 0x4D3CB2A1;
            uint snapLength = ReadUInt32(header.AsSpan(16), littleEndian);
            if (ReadUInt16(header.AsSpan(4), littleEndian) != 2 ||
                ReadUInt16(header.AsSpan(6), littleEndian) != 4 ||
                snapLength == 0)
            {
                throw new PcapDiagnosticsException("Invalid PCAP global header.");
            }

            long count = 0;
            byte[] record = new byte[16];
            while (stream.Position < stream.Length)
            {
                await ReadExactlyAsync(stream, record, ct).ConfigureAwait(false);
                uint fraction = ReadUInt32(record.AsSpan(4), littleEndian);
                uint capturedLength = ReadUInt32(record.AsSpan(8), littleEndian);
                uint originalLength = ReadUInt32(record.AsSpan(12), littleEndian);
                if (fraction >= (nanoseconds ? 1_000_000_000U : 1_000_000U) ||
                    capturedLength > snapLength ||
                    capturedLength > originalLength)
                {
                    throw new PcapDiagnosticsException("Invalid PCAP packet record.");
                }

                SkipBytes(stream, capturedLength, stream.Length);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Validates PCAPNG sections, interfaces, packet bodies, options and matching block trailers.
        /// </summary>
        /// <exception cref="PcapDiagnosticsException">
        /// A PCAPNG section, block, interface, or packet is invalid or incomplete, or contains decryption secrets.
        /// </exception>
        private static async ValueTask<long> ValidatePcapNgAsync(Stream stream, CancellationToken ct)
        {
            byte[] header = new byte[12];
            byte[] fields = new byte[20];
            byte[] trailer = new byte[4];
            List<uint> snapLengths = [];
            bool littleEndian = true;
            bool hasSection = false;
            long sectionEnd = -1;
            long count = 0;
            while (stream.Position < stream.Length)
            {
                long start = stream.Position;
                await ReadExactlyAsync(stream, header, ct).ConfigureAwait(false);
                bool isSection = BinaryPrimitives.ReadUInt32LittleEndian(header) == 0x0A0D0D0A;
                if (isSection)
                {
                    if (sectionEnd >= 0 && start != sectionEnd)
                    {
                        throw new PcapDiagnosticsException("Invalid PCAPNG section length.");
                    }

                    littleEndian = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)) switch
                    {
                        0x1A2B3C4D => true,
                        0x4D3C2B1A => false,
                        _ => throw new PcapDiagnosticsException("Invalid PCAPNG byte-order magic.")
                    };
                }
                else if (!hasSection)
                {
                    throw new PcapDiagnosticsException("The PCAPNG section header is missing.");
                }

                uint type = ReadUInt32(header, littleEndian);
                uint length = ReadUInt32(header.AsSpan(4), littleEndian);
                if (length < 12 || (length & 3) != 0 || length > stream.Length - start)
                {
                    throw new PcapDiagnosticsException("Invalid or truncated PCAPNG block.");
                }

                long end = start + length;
                if (!isSection && sectionEnd >= 0 && end > sectionEnd)
                {
                    throw new PcapDiagnosticsException("A PCAPNG block exceeds its section.");
                }

                stream.Position = start + 8;
                if (isSection)
                {
                    RequireBlockLength(length, 28);
                    await ReadExactlyAsync(stream, fields.AsMemory(0, 16), ct).ConfigureAwait(false);
                    long sectionLength = ReadInt64(fields.AsSpan(8), littleEndian);
                    if (ReadUInt16(fields.AsSpan(4), littleEndian) != 1 ||
                        ReadUInt16(fields.AsSpan(6), littleEndian) != 0 ||
                        sectionLength < -1 ||
                        (sectionLength >= 0 && ((sectionLength & 3) != 0 || sectionLength > stream.Length - end)))
                    {
                        throw new PcapDiagnosticsException("Invalid PCAPNG section header.");
                    }

                    sectionEnd = sectionLength == -1 ? -1 : end + sectionLength;
                    snapLengths.Clear();
                    hasSection = true;
                    await ValidateOptionsAsync(stream, end - 4, littleEndian, ct).ConfigureAwait(false);
                }
                else if (type == 1)
                {
                    RequireBlockLength(length, 20);
                    await ReadExactlyAsync(stream, fields.AsMemory(0, 8), ct).ConfigureAwait(false);
                    if (ReadUInt16(fields.AsSpan(2), littleEndian) != 0)
                    {
                        throw new PcapDiagnosticsException("Invalid PCAPNG interface header.");
                    }

                    snapLengths.Add(ReadUInt32(fields.AsSpan(4), littleEndian));
                    await ValidateOptionsAsync(stream, end - 4, littleEndian, ct).ConfigureAwait(false);
                }
                else if (type is 2 or 6)
                {
                    RequireBlockLength(length, 32);
                    await ReadExactlyAsync(stream, fields, ct).ConfigureAwait(false);
                    uint interfaceId = type == 2
                        ? ReadUInt16(fields, littleEndian)
                        : ReadUInt32(fields, littleEndian);
                    uint capturedLength = ReadUInt32(fields.AsSpan(12), littleEndian);
                    uint originalLength = ReadUInt32(fields.AsSpan(16), littleEndian);
                    if (interfaceId >= snapLengths.Count ||
                        capturedLength > originalLength ||
                        (snapLengths[(int)interfaceId] != 0 && capturedLength > snapLengths[(int)interfaceId]))
                    {
                        throw new PcapDiagnosticsException("Invalid PCAPNG packet record.");
                    }

                    SkipBytes(stream, ((long)capturedLength + 3) & ~3L, end - 4);
                    await ValidateOptionsAsync(stream, end - 4, littleEndian, ct).ConfigureAwait(false);
                    count++;
                }
                else if (type == 3)
                {
                    RequireBlockLength(length, 16);
                    if (snapLengths.Count == 0)
                    {
                        throw new PcapDiagnosticsException("The PCAPNG packet interface is missing.");
                    }

                    await ReadExactlyAsync(stream, fields.AsMemory(0, 4), ct).ConfigureAwait(false);
                    uint originalLength = ReadUInt32(fields, littleEndian);
                    uint capturedLength = snapLengths[0] == 0
                        ? originalLength
                        : Math.Min(originalLength, snapLengths[0]);
                    SkipBytes(stream, ((long)capturedLength + 3) & ~3L, end - 4);
                    if (stream.Position != end - 4)
                    {
                        throw new PcapDiagnosticsException("Invalid PCAPNG simple packet length.");
                    }

                    count++;
                }
                else if (type == 10)
                {
                    throw new PcapDiagnosticsException(
                        "PCAPNG decryption-secret blocks cannot be exported as ordinary packet captures.");
                }
                else
                {
                    stream.Position = end - 4;
                }

                await ReadExactlyAsync(stream, trailer, ct).ConfigureAwait(false);
                if (ReadUInt32(trailer, littleEndian) != length)
                {
                    throw new PcapDiagnosticsException("The PCAPNG block lengths do not match.");
                }
            }

            if (!hasSection || (sectionEnd >= 0 && stream.Position != sectionEnd))
            {
                throw new PcapDiagnosticsException("The PCAPNG section is incomplete.");
            }

            return count;
        }

        /// <summary>
        /// Validates the length-delimited options at the end of a PCAPNG block.
        /// </summary>
        /// <exception cref="PcapDiagnosticsException">
        /// An option is truncated or its end-of-options marker is invalid.
        /// </exception>
        private static async ValueTask ValidateOptionsAsync(
            Stream stream,
            long end,
            bool littleEndian,
            CancellationToken ct)
        {
            byte[] header = new byte[4];
            while (stream.Position < end)
            {
                if (end - stream.Position < header.Length)
                {
                    throw new PcapDiagnosticsException("Truncated PCAPNG option.");
                }

                await ReadExactlyAsync(stream, header, ct).ConfigureAwait(false);
                ushort code = ReadUInt16(header, littleEndian);
                ushort length = ReadUInt16(header.AsSpan(2), littleEndian);
                if (code == 0 && (length != 0 || stream.Position != end))
                {
                    throw new PcapDiagnosticsException("Invalid PCAPNG end-of-options marker.");
                }

                SkipBytes(stream, ((long)length + 3) & ~3L, end);
            }
        }

        /// <summary>
        /// Rejects a block that cannot contain its fixed fields and trailer.
        /// </summary>
        /// <exception cref="PcapDiagnosticsException">
        /// The block length is less than the required minimum.
        /// </exception>
        private static void RequireBlockLength(uint length, uint minimum)
        {
            if (length < minimum)
            {
                throw new PcapDiagnosticsException("Truncated PCAPNG block fields.");
            }
        }

        /// <summary>
        /// Skips only a complete body within its enclosing container.
        /// </summary>
        /// <exception cref="PcapDiagnosticsException">
        /// The body extends beyond its enclosing container.
        /// </exception>
        private static void SkipBytes(Stream stream, long length, long end)
        {
            if (length > end - stream.Position)
            {
                throw new PcapDiagnosticsException("Truncated capture packet or option body.");
            }

            stream.Position += length;
        }

        /// <summary>
        /// Reads a complete header without accepting a partial or missing file tail.
        /// </summary>
        /// <exception cref="PcapDiagnosticsException">
        /// The header is missing or truncated, or the stream ends before the header is complete.
        /// </exception>
        private static async ValueTask ReadExactlyAsync(Stream stream, Memory<byte> bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (bytes.Length > stream.Length - stream.Position)
            {
                throw new PcapDiagnosticsException("The capture contains a missing or truncated header.");
            }

            try
            {
                await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
            }
            catch (EndOfStreamException ex)
            {
                throw new PcapDiagnosticsException("The capture file is incomplete.", ex);
            }
        }

        /// <summary>
        /// Reads an unsigned short in the container's byte order.
        /// </summary>
        private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool littleEndian)
        {
            return littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
                : BinaryPrimitives.ReadUInt16BigEndian(bytes);
        }

        /// <summary>
        /// Reads an unsigned integer in the container's byte order.
        /// </summary>
        private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool littleEndian)
        {
            return littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
                : BinaryPrimitives.ReadUInt32BigEndian(bytes);
        }

        /// <summary>
        /// Reads a signed section length in the container's byte order.
        /// </summary>
        private static long ReadInt64(ReadOnlySpan<byte> bytes, bool littleEndian)
        {
            return littleEndian
                ? BinaryPrimitives.ReadInt64LittleEndian(bytes)
                : BinaryPrimitives.ReadInt64BigEndian(bytes);
        }
    }
}
