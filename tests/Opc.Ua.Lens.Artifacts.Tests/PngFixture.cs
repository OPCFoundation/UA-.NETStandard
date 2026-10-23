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
using System.IO.Compression;

namespace Opc.Ua.Lens.Artifacts.Tests
{
    /// <summary>
    /// Makes independently encoded PNG fixtures for the capture validator.
    /// </summary>
    internal static class PngFixture
    {
        /// <summary>
        /// Encodes a colored first pixel followed by opaque black pixels.
        /// </summary>
        internal static byte[] Create(
            int width = 2,
            int height = 1,
            byte red = 255,
            byte green = 0,
            CompressionLevel compression = CompressionLevel.SmallestSize,
            byte filter = 0)
        {
            using var output = new MemoryStream();
            output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
            Span<byte> header = stackalloc byte[13];
            header.Clear();
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
            BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)height);
            header[8] = 8;
            header[9] = 6;
            WriteChunk(output, "IHDR"u8, header);
            byte[] pixels = new byte[height * ((width * 4) + 1)];
            int stride = (width * 4) + 1;
            for (int row = 0; row < height; row++)
            {
                pixels[row * stride] = filter;
                for (int column = 0; column < width; column++)
                {
                    pixels[(row * stride) + (column * 4) + 4] = 255;
                }
            }
            if (pixels.Length > 4)
            {
                pixels[1] = red;
                pixels[2] = green;
            }
            using var compressed = new MemoryStream();
            using (var encoder = new ZLibStream(compressed, compression, leaveOpen: true))
            {
                encoder.Write(pixels);
            }
            WriteChunk(output, "IDAT"u8, compressed.ToArray());
            WriteChunk(output, "IEND"u8, []);
            return output.ToArray();
        }

        /// <summary>
        /// Replaces one chunk payload while recomputing its checksum.
        /// </summary>
        internal static byte[] ReplaceChunk(byte[] png, string kind, Func<byte[], byte[]> replace)
        {
            using var output = new MemoryStream();
            output.Write(png.AsSpan(0, 8));
            for (int offset = 8; offset < png.Length;)
            {
                int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset)));
                ReadOnlySpan<byte> type = png.AsSpan(offset + 4, 4);
                byte[] bytes = png.AsSpan(offset + 8, length).ToArray();
                if (System.Text.Encoding.ASCII.GetString(type) == kind)
                {
                    bytes = replace(bytes);
                }
                WriteChunk(output, type, bytes);
                offset += length + 12;
            }
            return output.ToArray();
        }

        private static void WriteChunk(Stream output, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> bytes)
        {
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(number, (uint)bytes.Length);
            output.Write(number);
            output.Write(kind);
            output.Write(bytes);
            uint crc = uint.MaxValue;
            foreach (byte value in kind)
            {
                crc = UpdateChecksum(crc, value);
            }
            foreach (byte value in bytes)
            {
                crc = UpdateChecksum(crc, value);
            }
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
            output.Write(number);
        }

        private static uint UpdateChecksum(uint crc, byte value)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320u;
            }
            return crc;
        }
    }
}
