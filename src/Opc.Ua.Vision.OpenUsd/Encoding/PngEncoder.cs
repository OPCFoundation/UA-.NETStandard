/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Vision.OpenUsd.Encoding
{
    /// <summary>
    /// Minimal pure-managed PNG encoder for 8-bit RGBA images. Emits a
    /// single IHDR, an IDAT wrapping a zlib stream of DEFLATE stored blocks
    /// (uncompressed - trades size for zero dependencies), and IEND. Output
    /// is byte-identical to what the probe's PngWriter produced; every PNG
    /// reader in the wild accepts it.
    /// </summary>
    /// <remarks>
    /// The Vision use case is a fresh render per frame, so encode time is
    /// dominated by network / OPC UA framing anyway. When smaller payloads
    /// matter the caller can plug a different encoder in behind
    /// <see cref="ISceneCameraCaptureProvider"/>; the interface never
    /// exposes the encoder.
    /// </remarks>
    internal static class PngEncoder
    {
        private static readonly byte[] s_signature =
        [
            0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A,
        ];

        /// <summary>
        /// Encodes an <paramref name="rgba"/> RGBA8 top-down pixel buffer
        /// as a PNG and returns the bytes.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="width"/> or <paramref name="height"/> is not positive.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="rgba"/> is not exactly <c>width * height * 4</c> bytes.
        /// </exception>
        public static byte[] EncodeRgba8(int width, int height, ReadOnlySpan<byte> rgba)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }
            int expected = checked(width * height * 4);
            if (rgba.Length != expected)
            {
                throw new ArgumentException(
                    $"rgba length {rgba.Length} != width*height*4 ({expected}).",
                    nameof(rgba));
            }

            using var ms = new MemoryStream(expected + 512);
            ms.Write(s_signature, 0, s_signature.Length);

            Span<byte> ihdr = stackalloc byte[13];
            WriteUInt32BE(ihdr, 0, (uint)width);
            WriteUInt32BE(ihdr, 4, (uint)height);
            ihdr[8] = 8;
            ihdr[9] = 6;
            ihdr[10] = 0;
            ihdr[11] = 0;
            ihdr[12] = 0;
            WriteChunk(ms, "IHDR", ihdr);

            int rowBytes = width * 4;
            int filteredSize = height * (rowBytes + 1);
            byte[] filtered = new byte[filteredSize];
            for (int y = 0; y < height; y++)
            {
                int srcOff = y * rowBytes;
                int dstOff = y * (rowBytes + 1);
                filtered[dstOff] = 0;
                rgba.Slice(srcOff, rowBytes).CopyTo(filtered.AsSpan(dstOff + 1, rowBytes));
            }
            byte[] zlib = ZlibWrapStored(filtered);
            WriteChunk(ms, "IDAT", zlib);

            WriteChunk(ms, "IEND", ReadOnlySpan<byte>.Empty);
            return ms.ToArray();
        }

        private static byte[] ZlibWrapStored(byte[] payload)
        {
            using var ms = new MemoryStream(payload.Length + 32);
            ms.WriteByte(0x78);
            ms.WriteByte(0x01);

            int offset = 0;
            while (offset < payload.Length)
            {
                int chunk = Math.Min(65535, payload.Length - offset);
                bool final = offset + chunk >= payload.Length;
                ms.WriteByte((byte)(final ? 1 : 0));
                ms.WriteByte((byte)(chunk & 0xFF));
                ms.WriteByte((byte)((chunk >> 8) & 0xFF));
                int nlen = ~chunk & 0xFFFF;
                ms.WriteByte((byte)(nlen & 0xFF));
                ms.WriteByte((byte)((nlen >> 8) & 0xFF));
                ms.Write(payload, offset, chunk);
                offset += chunk;
            }

            uint adler = Adler32(payload);
            ms.WriteByte((byte)((adler >> 24) & 0xFF));
            ms.WriteByte((byte)((adler >> 16) & 0xFF));
            ms.WriteByte((byte)((adler >> 8) & 0xFF));
            ms.WriteByte((byte)(adler & 0xFF));
            return ms.ToArray();
        }

        private static uint Adler32(ReadOnlySpan<byte> data)
        {
            const uint mod = 65521;
            uint a = 1;
            uint b = 0;
            for (int i = 0; i < data.Length; i++)
            {
                a = (a + data[i]) % mod;
                b = (b + a) % mod;
            }
            return (b << 16) | a;
        }

        private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
        {
            Span<byte> len = stackalloc byte[4];
            WriteUInt32BE(len, 0, (uint)data.Length);
            s.Write(len);

            Span<byte> typeBytes = stackalloc byte[4];
            System.Text.Encoding.ASCII.GetBytes(type, typeBytes);
            s.Write(typeBytes);
            if (data.Length > 0)
            {
                s.Write(data);
            }

            uint crc = Crc32.Compute(typeBytes, data);
            Span<byte> crcBytes = stackalloc byte[4];
            WriteUInt32BE(crcBytes, 0, crc);
            s.Write(crcBytes);
        }

        private static void WriteUInt32BE(Span<byte> buf, int offset, uint value)
        {
            buf[offset] = (byte)((value >> 24) & 0xFF);
            buf[offset + 1] = (byte)((value >> 16) & 0xFF);
            buf[offset + 2] = (byte)((value >> 8) & 0xFF);
            buf[offset + 3] = (byte)(value & 0xFF);
        }

        private static class Crc32
        {
            private static readonly uint[] s_table = BuildTable();

            private static uint[] BuildTable()
            {
                uint[] t = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                    {
                        c = (c & 1) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                    }
                    t[n] = c;
                }
                return t;
            }

            public static uint Compute(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
            {
                uint c = 0xFFFFFFFFu;
                for (int i = 0; i < a.Length; i++)
                {
                    c = s_table[(c ^ a[i]) & 0xFF] ^ (c >> 8);
                }
                for (int i = 0; i < b.Length; i++)
                {
                    c = s_table[(c ^ b[i]) & 0xFF] ^ (c >> 8);
                }
                return c ^ 0xFFFFFFFFu;
            }
        }
    }
}
