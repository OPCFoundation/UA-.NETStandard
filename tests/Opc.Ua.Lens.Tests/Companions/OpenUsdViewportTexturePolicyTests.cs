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
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class OpenUsdViewportTexturePolicyTests
    {
        [TestCase(1, 1)]
        [TestCase(2048, 1)]
        [TestCase(1, 2048)]
        public void TextureDimensionsAcceptTheirExactBoundaries(int width, int height)
        {
            ByteString png = Png(width, height);
            Assert.That(() => Validate(png), Throws.Nothing);
        }

        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(2049, 1)]
        [TestCase(1, 2049)]
        public void TextureDimensionsRejectBeforeAnyNativeDecode(int width, int height)
        {
            ByteString png = Png(width, height);
            Assert.That(() => Validate(png), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void DecodedTextureBudgetIsIndependentOfTinyCompressedAssetSize()
        {
            ByteString png = Png(2048, 2048);
            Assert.That(png.Length, Is.LessThan(100_000), "The test must exercise a compressed expansion boundary.");
            OpenUsdVerifiedAsset root = OpenUsdViewportScenePolicyTests.Layer(
                "cell.usda", "#usda 1.0\ndef Cube \"C\" {}");
            Assert.That(() => OpenUsdViewportScenePolicy.Validate("cell.usda",
                [root, new("one.png", png), new("two.png", png)]), Throws.Nothing,
                "Exactly 32 MiB decoded is admitted.");
            Assert.That(() => OpenUsdViewportScenePolicy.Validate("cell.usda",
                [root, new("one.png", png), new("two.png", png), new("three.png", png)]),
                Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void LaterImageHeadersCannotReplacePreviouslyValidatedDimensions()
        {
            ByteString original = Png(2, 1);
            using var output = new MemoryStream();
            output.Write(original.Span[..33]);
            output.Write(original.Span.Slice(8, 25));
            output.Write(original.Span[33..]);
            Assert.That(() => Validate(ByteString.From(output.ToArray())), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void CompressedProfileMetadataIsRejectedBeforeDecoding()
        {
            ByteString original = Png(2, 1);
            using var output = new MemoryStream();
            output.Write(original.Span[..33]);
            Chunk(output, "iCCP"u8, [1, 2, 3, 4]);
            output.Write(original.Span[33..]);
            Assert.That(() => Validate(ByteString.From(output.ToArray())), Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void TruncatedTexturePayloadDoesNotEnterTheNativeCache()
        {
            ByteString original = Png(2, 1);
            Assert.That(() => Validate(ByteString.From(original.Span[..^1].ToArray())),
                Throws.TypeOf<InvalidDataException>());
        }

        private static void Validate(ByteString png)
        {
            OpenUsdViewportScenePolicy.Validate("cell.usda",
            [
                OpenUsdViewportScenePolicyTests.Layer("cell.usda", "#usda 1.0\ndef Cube \"C\" {}"),
                new("image.png", png)
            ]);
        }

        private static ByteString Png(int width, int height)
        {
            using var output = new MemoryStream();
            output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
            Span<byte> header = stackalloc byte[13];
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
            BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)height);
            header[8] = 8;
            header[9] = 6;
            header[10] = 0;
            header[11] = 0;
            header[12] = 0;
            Chunk(output, "IHDR"u8, header);
            using var compressed = new MemoryStream();
            using (var deflate = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                byte[] row = new byte[(width * 4) + 1];
                for (int y = 0; y < height; y++)
                {
                    deflate.Write(row);
                }
            }
            Chunk(output, "IDAT"u8, compressed.ToArray());
            Chunk(output, "IEND"u8, []);
            return ByteString.From(output.ToArray());
        }

        private static void Chunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload)
        {
            Span<byte> encoded = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(encoded, (uint)payload.Length);
            stream.Write(encoded);
            stream.Write(type);
            stream.Write(payload);
            uint crc = 0xffffffff;
            foreach (byte value in type)
            {
                crc = Update(crc, value);
            }
            foreach (byte value in payload)
            {
                crc = Update(crc, value);
            }
            BinaryPrimitives.WriteUInt32BigEndian(encoded, ~crc);
            stream.Write(encoded);
        }

        private static uint Update(uint crc, byte value)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
            }
            return crc;
        }
    }
}
