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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Lens.Artifacts.Tests
{
    /// <summary>
    /// Checks the PNG contract emitted by the managed Vision capture encoder.
    /// </summary>
    internal static class NativeImageEvidence
    {
        /// <summary>
        /// Reads two bounded regular files and requires a visible change at the same dimensions.
        /// </summary>
        internal static async Task<NativeImageFrame[]> ReadAsync(
            string directory,
            CancellationToken cancellationToken = default)
        {
            string[] names = ["native-before.png", "native-after.png"];
            var frames = new NativeImageFrame[names.Length];
            for (int index = 0; index < names.Length; index++)
            {
                string path = Path.Combine(directory, names[index]);
                var file = new FileInfo(path);
                Require(file.Exists && (file.Attributes & FileAttributes.ReparsePoint) == 0,
                    $"Missing regular native image artifact: {names[index]}");
                Require(file.Length is > 33 and <= MaximumBytes,
                    $"Native image artifact is outside its byte budget: {names[index]}");
                var stream = new FileStream(path, new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });
                await using (stream.ConfigureAwait(false))
                {
                    byte[] image = new byte[MaximumBytes + 1];
                    int count = await stream.ReadAtLeastAsync(
                        image, image.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                    Require(count <= MaximumBytes, "Native image artifact grew beyond its byte budget.");
                    frames[index] = Parse(image.AsSpan(0, count), names[index]);
                }
            }

            Require(frames[0].Width == frames[1].Width && frames[0].Height == frames[1].Height,
                "The native stage update unexpectedly changed capture dimensions.");
            Require(frames[0].PixelSha256 != frames[1].PixelSha256,
                "The native stage update did not produce a different image.");
            return frames;
        }

        /// <summary>
        /// Validates chunk checksums, bounded RGBA8 scanlines and nonuniform decoded pixels.
        /// </summary>
        internal static NativeImageFrame Parse(ReadOnlySpan<byte> png, string file)
        {
            Require(png.Length is > 33 and <= MaximumBytes, "Native PNG is outside its byte budget.");
            Require(png[..8].SequenceEqual(s_signature), "Native image artifact is not a PNG.");
            int offset = 8;
            ReadOnlySpan<byte> header = ReadChunk(png, ref offset, "IHDR"u8);
            ReadOnlySpan<byte> compressed = ReadChunk(png, ref offset, "IDAT"u8);
            ReadOnlySpan<byte> end = ReadChunk(png, ref offset, "IEND"u8);
            Require(end.IsEmpty && offset == png.Length,
                "Native PNG does not match the capture encoder's chunk contract.");
            ReadOnlySpan<byte> format = [8, 6, 0, 0, 0];
            Require(header.Length == 13 && header[8..].SequenceEqual(format),
                "Native PNG must be non-interlaced RGBA8.");
            uint width = BinaryPrimitives.ReadUInt32BigEndian(header);
            uint height = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            Require(width is >= 1 and <= 1024 && height is >= 1 and <= 1024,
                "Native image dimensions are outside the capture budget.");
            int stride = checked(((int)width * 4) + 1);
            int expected = checked(stride * (int)height);
            byte[] pixels = new byte[expected + 1];
            using var source = new ExactReadStream(compressed.ToArray());
            using (var decoder = new ZLibStream(source, CompressionMode.Decompress, leaveOpen: true))
            {
                int count = decoder.ReadAtLeast(pixels, pixels.Length, throwOnEndOfStream: false);
                Require(count == expected && source.Position == source.Length,
                    "Native PNG pixel storage does not match its dimensions.");
            }
            Require(compressed.Length >= 6 &&
                BinaryPrimitives.ReadUInt32BigEndian(compressed[^4..]) == Adler32(pixels.AsSpan(0, expected)),
                "Native PNG zlib checksum is invalid or truncated.");

            ReadOnlySpan<byte> first = pixels.AsSpan(1, 4);
            bool different = false;
            for (int row = 0; row < height; row++)
            {
                Require(pixels[row * stride] == 0,
                    "Native PNG does not match the capture encoder's row-filter contract.");
                for (int column = 0; column < width; column++)
                {
                    different |= !pixels.AsSpan((row * stride) + 1 + (column * 4), 4).SequenceEqual(first);
                }
            }
            Require(different, "Native capture contains only a uniform image.");
            return new NativeImageFrame(
                file, (int)width, (int)height,
                Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant(),
                Convert.ToHexString(SHA256.HashData(pixels.AsSpan(0, expected))).ToLowerInvariant());
        }

        private static ReadOnlySpan<byte> ReadChunk(
            ReadOnlySpan<byte> png,
            ref int offset,
            ReadOnlySpan<byte> expectedKind)
        {
            Require(offset <= png.Length - 12, "Native PNG has a truncated chunk.");
            uint size = BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
            Require(size <= png.Length - offset - 12, "Native PNG chunk exceeds the image bounds.");
            int length = (int)size;
            ReadOnlySpan<byte> kind = png.Slice(offset + 4, 4);
            Require(kind.SequenceEqual(expectedKind),
                "Native PNG does not match the capture encoder's chunk contract.");
            uint checksum = BinaryPrimitives.ReadUInt32BigEndian(png[(offset + 8 + length)..]);
            Require(checksum == Crc32(png.Slice(offset + 4, length + 4)), "Native PNG checksum is invalid.");
            ReadOnlySpan<byte> payload = png.Slice(offset + 8, length);
            offset += length + 12;
            return payload;
        }

        private static uint Crc32(ReadOnlySpan<byte> bytes)
        {
            uint crc = uint.MaxValue;
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320u;
                }
            }
            return ~crc;
        }

        private static uint Adler32(ReadOnlySpan<byte> bytes)
        {
            uint a = 1;
            uint b = 0;
            foreach (byte value in bytes)
            {
                a = (a + value) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }

        /// <summary>
        /// Prevents decompressor read-ahead from hiding bytes after the zlib stream.
        /// </summary>
        private sealed class ExactReadStream(byte[] bytes) : MemoryStream(bytes, writable: false)
        {
            public override int Read(byte[] buffer, int offset, int count)
            {
                return base.Read(buffer, offset, Math.Min(count, 1));
            }

            public override int Read(Span<byte> buffer)
            {
                return base.Read(buffer[..Math.Min(buffer.Length, 1)]);
            }
        }

        /// <summary>The maximum encoded image size admitted by the capture check.</summary>
        internal const int MaximumBytes = (4 * 1024 * 1024) + 16384;

        private static readonly byte[] s_signature = [137, 80, 78, 71, 13, 10, 26, 10];
    }

    /// <summary>
    /// Identifies an accepted native frame and its decoded pixel content.
    /// </summary>
    /// <param name="File">Artifact filename.</param>
    /// <param name="Width">Image width in pixels.</param>
    /// <param name="Height">Image height in pixels.</param>
    /// <param name="Sha256">Encoded-file digest.</param>
    /// <param name="PixelSha256">Decoded-scanline digest.</param>
    internal sealed record NativeImageFrame(string File, int Width, int Height, string Sha256, string PixelSha256);
}
