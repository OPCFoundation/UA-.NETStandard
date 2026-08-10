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
using NUnit.Framework;
using Opc.Ua.Vision.OpenUsd.Encoding;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Pins the byte-level contract of <see cref="PngEncoder.EncodeRgba8"/>
    /// - PNG signature, IHDR / IDAT / IEND chunk layout, and round-trip
    /// through the standard <see cref="ZLibStream"/> decoder so any PNG
    /// reader (including .NET's own <c>Image</c>) can consume the output.
    /// The encoder is used for every simulated-camera frame the Vision
    /// server publishes, so a regression that produces invalid PNG bytes
    /// would silently break every client.
    /// </summary>
    [TestFixture]
    public sealed class PngEncoderTests
    {
        [Test]
        public void EncodeRgba8OutputStartsWithPngSignature()
        {
            byte[] rgba = MakeGradient(4, 4);

            byte[] png = PngEncoder.EncodeRgba8(4, 4, rgba);

            Assert.Multiple(() =>
            {
                Assert.That(png.Length, Is.GreaterThan(8));
                Assert.That(png[0], Is.EqualTo(0x89));
                Assert.That(png[1], Is.EqualTo((byte)'P'));
                Assert.That(png[2], Is.EqualTo((byte)'N'));
                Assert.That(png[3], Is.EqualTo((byte)'G'));
                Assert.That(png[4], Is.EqualTo(0x0D));
                Assert.That(png[5], Is.EqualTo(0x0A));
                Assert.That(png[6], Is.EqualTo(0x1A));
                Assert.That(png[7], Is.EqualTo(0x0A));
            });
        }

        [Test]
        public void EncodeRgba8ProducesIhdrIdatIendInThatOrder()
        {
            byte[] png = PngEncoder.EncodeRgba8(4, 4, MakeGradient(4, 4));

            int idxIhdr = FindChunk(png, "IHDR");
            int idxIdat = FindChunk(png, "IDAT");
            int idxIend = FindChunk(png, "IEND");

            Assert.Multiple(() =>
            {
                Assert.That(idxIhdr, Is.EqualTo(8),
                    "IHDR must be the first chunk after the PNG signature.");
                Assert.That(idxIdat, Is.GreaterThan(idxIhdr));
                Assert.That(idxIend, Is.GreaterThan(idxIdat));
            });
        }

        [Test]
        public void IhdrCarriesWidthHeightBitDepth8ColorType6()
        {
            byte[] png = PngEncoder.EncodeRgba8(7, 5, MakeGradient(7, 5));

            int idx = FindChunk(png, "IHDR");
            int dataOffset = idx + 8;
            uint w = ReadUInt32BE(png, dataOffset);
            uint h = ReadUInt32BE(png, dataOffset + 4);
            byte bitDepth = png[dataOffset + 8];
            byte colorType = png[dataOffset + 9];
            byte compression = png[dataOffset + 10];
            byte filter = png[dataOffset + 11];
            byte interlace = png[dataOffset + 12];

            Assert.Multiple(() =>
            {
                Assert.That(w, Is.EqualTo(7u));
                Assert.That(h, Is.EqualTo(5u));
                Assert.That(bitDepth, Is.EqualTo(8));
                Assert.That(colorType, Is.EqualTo(6),
                    "PngEncoder is RGBA-only: colour type 6 is truecolour with alpha.");
                Assert.That(compression, Is.EqualTo(0));
                Assert.That(filter, Is.EqualTo(0));
                Assert.That(interlace, Is.EqualTo(0));
            });
        }

        [Test]
        public void IdatDecompressesBackToFilterZeroPrefixedRowsWithOriginalPixels()
        {
            const int W = 3;
            const int H = 2;
            byte[] rgba = MakeGradient(W, H);

            byte[] png = PngEncoder.EncodeRgba8(W, H, rgba);
            byte[] filtered = DecompressIdat(png);

            Assert.That(filtered.Length, Is.EqualTo(H * ((W * 4) + 1)),
                "Decompressed IDAT must be H rows of (1 filter byte + W*4 pixel bytes).");
            for (int y = 0; y < H; y++)
            {
                int rowStart = y * ((W * 4) + 1);
                Assert.That(filtered[rowStart], Is.EqualTo(0),
                    "PngEncoder emits filter type 0 (None) on every row.");
                for (int x = 0; x < W * 4; x++)
                {
                    int srcIdx = (y * W * 4) + x;
                    int dstIdx = rowStart + 1 + x;
                    Assert.That(filtered[dstIdx], Is.EqualTo(rgba[srcIdx]),
                        $"Pixel byte round-trip mismatch at y={y}, x={x}.");
                }
            }
        }

        [Test]
        public void EncodeRgba8ThrowsArgumentOutOfRangeForNonPositiveWidth()
        {
            Assert.That(
                () => PngEncoder.EncodeRgba8(0, 1, new byte[4]),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => PngEncoder.EncodeRgba8(-1, 1, new byte[4]),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void EncodeRgba8ThrowsArgumentOutOfRangeForNonPositiveHeight()
        {
            Assert.That(
                () => PngEncoder.EncodeRgba8(1, 0, new byte[4]),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => PngEncoder.EncodeRgba8(1, -1, new byte[4]),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void EncodeRgba8ThrowsArgumentExceptionForMismatchedBufferLength()
        {
            Assert.That(
                () => PngEncoder.EncodeRgba8(4, 4, new byte[10]),
                Throws.TypeOf<ArgumentException>());
        }

        private static int FindChunk(byte[] png, string fourcc)
        {
            if (fourcc.Length != 4)
            {
                throw new ArgumentException("Chunk type must be exactly 4 ASCII bytes.", nameof(fourcc));
            }
            for (int i = 8; i + 8 <= png.Length; )
            {
                uint length = ReadUInt32BE(png, i);
                if (length > (uint)int.MaxValue)
                {
                    return -1;
                }
                if (png[i + 4] == (byte)fourcc[0]
                    && png[i + 5] == (byte)fourcc[1]
                    && png[i + 6] == (byte)fourcc[2]
                    && png[i + 7] == (byte)fourcc[3])
                {
                    return i;
                }
                i += 4 + 4 + (int)length + 4;
            }
            return -1;
        }

        private static byte[] DecompressIdat(byte[] png)
        {
            int idx = FindChunk(png, "IDAT");
            Assert.That(idx, Is.GreaterThan(0), "Encoder must emit at least one IDAT chunk.");
            uint len = ReadUInt32BE(png, idx);
            byte[] payload = new byte[len];
            Array.Copy(png, idx + 8, payload, 0, (int)len);
            using var input = new MemoryStream(payload);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }

        private static uint ReadUInt32BE(byte[] buf, int offset)
        {
            return ((uint)buf[offset] << 24)
                | ((uint)buf[offset + 1] << 16)
                | ((uint)buf[offset + 2] << 8)
                | buf[offset + 3];
        }

        private static byte[] MakeGradient(int width, int height)
        {
            byte[] rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = ((y * width) + x) * 4;
                    rgba[i] = (byte)(x * 17);
                    rgba[i + 1] = (byte)(y * 23);
                    rgba[i + 2] = (byte)((x + y) * 5);
                    rgba[i + 3] = 0xFF;
                }
            }
            return rgba;
        }
    }
}
