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
using System.Text;

namespace Vision.VisualInspectionCell
{
    /// <summary>
    /// Minimal PNG decoder for the fixture images used by this sample.
    /// </summary>
    internal static class PngDecoder
    {
        public static (byte[] Rgb, int Width, int Height) Decode(byte[] png)
        {
            if (png == null)
            {
                throw new ArgumentNullException(nameof(png));
            }
            if (png.Length < 8 || png[0] != 0x89 || png[1] != (byte)'P' || png[2] != (byte)'N' ||
                png[3] != (byte)'G' || png[4] != 0x0D || png[5] != 0x0A || png[6] != 0x1A || png[7] != 0x0A)
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
                string chunkType = Encoding.ASCII.GetString(png, offset + 4, 4);
                int dataStart = offset + 8;
                if (dataStart + length + 4 > png.Length)
                {
                    throw new InvalidDataException("PNG chunk exceeds stream length.");
                }
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
            if (bitDepth != 8 || (colourType != 2 && colourType != 6))
            {
                throw new NotSupportedException(
                    $"PNG must be 8-bit RGB/RGBA; got type={colourType}, depth={bitDepth}.");
            }
            if (interlace != 0)
            {
                throw new NotSupportedException("Interlaced PNGs are not supported by the sample decoder.");
            }
            if (width <= 0 || height <= 0)
            {
                throw new InvalidDataException("PNG dimensions are invalid.");
            }

            int bytesPerPixel = colourType == 6 ? 4 : 3;
            byte[] rawFiltered = InflateZlib(idat.ToArray());
            int rowBytes = width * bytesPerPixel;
            int expected = height * (rowBytes + 1);
            if (rawFiltered.Length != expected)
            {
                throw new InvalidDataException(
                    $"Decoded filtered size {rawFiltered.Length} does not match {expected}.");
            }
            byte[] unfiltered = new byte[height * rowBytes];
            Unfilter(rawFiltered, unfiltered, width, height, bytesPerPixel);
            if (bytesPerPixel == 3)
            {
                return (unfiltered, width, height);
            }

            byte[] rgb = new byte[width * height * 3];
            for (int source = 0, target = 0; source < unfiltered.Length; source += 4, target += 3)
            {
                rgb[target] = unfiltered[source];
                rgb[target + 1] = unfiltered[source + 1];
                rgb[target + 2] = unfiltered[source + 2];
            }
            return (rgb, width, height);
        }

        private static byte[] InflateZlib(byte[] zlib)
        {
            if (zlib.Length < 6)
            {
                throw new InvalidDataException("IDAT chunk is too short.");
            }
            using var input = new MemoryStream(zlib, index: 2, count: zlib.Length - 6, writable: false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress, leaveOpen: false);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }

        private static void Unfilter(byte[] filtered, byte[] rgb, int width, int height, int bytesPerPixel)
        {
            int rowBytes = width * bytesPerPixel;
            for (int y = 0; y < height; y++)
            {
                int srcRow = y * (rowBytes + 1);
                int dstRow = y * rowBytes;
                byte type = filtered[srcRow];
                for (int x = 0; x < rowBytes; x++)
                {
                    byte value = filtered[srcRow + 1 + x];
                    byte left = x >= bytesPerPixel ? rgb[dstRow + x - bytesPerPixel] : (byte)0;
                    byte up = y > 0 ? rgb[(y - 1) * rowBytes + x] : (byte)0;
                    byte upLeft = y > 0 && x >= bytesPerPixel
                        ? rgb[(y - 1) * rowBytes + x - bytesPerPixel]
                        : (byte)0;
                    rgb[dstRow + x] = type switch
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
            return pb <= pc ? up : upLeft;
        }

        private static int ReadUInt32BE(byte[] source, int offset)
        {
            return (source[offset] << 24) | (source[offset + 1] << 16) |
                (source[offset + 2] << 8) | source[offset + 3];
        }
    }
}
