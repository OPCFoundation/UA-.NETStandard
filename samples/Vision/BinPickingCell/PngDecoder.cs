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
using System.IO;
using System.IO.Compression;

namespace Vision.BinPickingCell
{
    /// <summary>
    /// Minimal PNG decoder for 8-bit RGBA images. Handles the output of
    /// the in-repo <c>PngEncoder</c> used by the OpenUSD capture provider
    /// (single IHDR + single or few IDATs + IEND, colour-type 6, bit-depth
    /// 8, filter type 0 or 1 per row) and any similarly-conformant PNG.
    /// Adds no NuGet dependency; the DEFLATE step goes through the BCL's
    /// <see cref="DeflateStream"/>.
    /// </summary>
    /// <remarks>
    /// The decoder is deliberately restricted to what the sample diagnostic
    /// needs. It rejects interlaced streams, palette images, and 16-bit
    /// depths with <see cref="NotSupportedException"/>; the OpenUSD
    /// provider only ever emits the RGBA8 non-interlaced flavour.
    /// </remarks>
    internal static class PngDecoder
    {
        public static (byte[] Rgba, int Width, int Height) Decode(byte[] png)
        {
            if (png == null)
            {
                throw new ArgumentNullException(nameof(png));
            }
            if (png.Length < 8 ||
                png[0] != 0x89 || png[1] != (byte)'P' || png[2] != (byte)'N' || png[3] != (byte)'G' ||
                png[4] != 0x0D || png[5] != 0x0A || png[6] != 0x1A || png[7] != 0x0A)
            {
                throw new InvalidDataException("Not a PNG stream.");
            }
            int width = 0;
            int height = 0;
            byte bitDepth = 0;
            byte colourType = 0;
            byte interlace = 0;
            using var idat = new MemoryStream();
            int offset = 8;
            while (offset + 8 <= png.Length)
            {
                int length = ReadUInt32BE(png, offset);
                string chunkType = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
                int dataStart = offset + 8;
                if (chunkType == "IHDR")
                {
                    width = ReadUInt32BE(png, dataStart);
                    height = ReadUInt32BE(png, dataStart + 4);
                    bitDepth = png[dataStart + 8];
                    colourType = png[dataStart + 9];
                    interlace = png[dataStart + 12];
                }
                else if (chunkType == "IDAT")
                {
                    idat.Write(png, dataStart, length);
                }
                else if (chunkType == "IEND")
                {
                    break;
                }
                offset = dataStart + length + 4;
            }
            if (bitDepth != 8 || colourType != 6)
            {
                throw new NotSupportedException(
                    $"PNG must be 8-bit RGBA (colour type 6, bit depth 8); got type={colourType}, depth={bitDepth}.");
            }
            if (interlace != 0)
            {
                throw new NotSupportedException("Interlaced PNGs are not supported by the sample decoder.");
            }
            if (width <= 0 || height <= 0)
            {
                throw new InvalidDataException("PNG dimensions are invalid.");
            }
            byte[] zlibBytes = idat.ToArray();
            if (zlibBytes.Length < 2)
            {
                throw new InvalidDataException("IDAT chunk is too short.");
            }
            byte[] rawFiltered = InflateZlib(zlibBytes);
            int rowBytes = width * 4;
            int expected = height * (rowBytes + 1);
            if (rawFiltered.Length != expected)
            {
                throw new InvalidDataException(
                    $"Decoded filtered size {rawFiltered.Length} does not match {expected}.");
            }
            byte[] rgba = new byte[height * rowBytes];
            Unfilter(rawFiltered, rgba, width, height);
            return (rgba, width, height);
        }

        private static byte[] InflateZlib(byte[] zlib)
        {
            using var input = new MemoryStream(zlib, index: 2, count: zlib.Length - 6, writable: false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress, leaveOpen: false);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }

        private static void Unfilter(byte[] filtered, byte[] rgba, int width, int height)
        {
            int rowBytes = width * 4;
            for (int y = 0; y < height; y++)
            {
                int srcRow = y * (rowBytes + 1);
                int dstRow = y * rowBytes;
                byte type = filtered[srcRow];
                for (int x = 0; x < rowBytes; x++)
                {
                    byte value = filtered[srcRow + 1 + x];
                    byte left = x >= 4 ? rgba[dstRow + x - 4] : (byte)0;
                    byte up = y > 0 ? rgba[(y - 1) * rowBytes + x] : (byte)0;
                    byte upLeft = y > 0 && x >= 4 ? rgba[(y - 1) * rowBytes + x - 4] : (byte)0;
                    rgba[dstRow + x] = type switch
                    {
                        0 => value,
                        1 => (byte)(value + left),
                        2 => (byte)(value + up),
                        3 => (byte)(value + ((left + up) / 2)),
                        4 => (byte)(value + Paeth(left, up, upLeft)),
                        _ => throw new NotSupportedException($"Unknown PNG row filter {type}.")
                    };
                }
            }
        }

        private static byte Paeth(byte left, byte up, byte upLeft)
        {
            int p = left + up - upLeft;
            int pa = Math.Abs(p - left);
            int pb = Math.Abs(p - up);
            int pc = Math.Abs(p - upLeft);
            if (pa <= pb && pa <= pc)
            {
                return left;
            }
            if (pb <= pc)
            {
                return up;
            }
            return upLeft;
        }

        private static int ReadUInt32BE(byte[] source, int offset)
        {
            return (source[offset] << 24)
                | (source[offset + 1] << 16)
                | (source[offset + 2] << 8)
                | source[offset + 3];
        }
    }
}
