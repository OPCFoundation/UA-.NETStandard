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
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Lens.Artifacts.Tests
{
    /// <summary>
    /// Keeps native rendering acceptance independent of file bytes or success markers alone.
    /// </summary>
    [TestFixture]
    public sealed class NativeImageEvidenceTests
    {
        /// <summary>
        /// Decoded scanlines retain their exact dimensions and pixel digest.
        /// </summary>
        [TestCase(CompressionLevel.NoCompression)]
        [TestCase(CompressionLevel.SmallestSize)]
        public void ValidRgbaFrameRetainsDecodedEvidence(CompressionLevel compression)
        {
            byte[] png = PngFixture.Create(compression: compression);
            byte[] pixels = [0, 255, 0, 0, 255, 0, 0, 0, 255];

            NativeImageFrame frame = NativeImageEvidence.Parse(png, "frame.png");

            Assert.That(frame.File, Is.EqualTo("frame.png"));
            Assert.That(frame.Width, Is.EqualTo(2));
            Assert.That(frame.Height, Is.EqualTo(1));
            Assert.That(frame.Sha256, Is.EqualTo(Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant()));
            Assert.That(frame.PixelSha256, Is.EqualTo(Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant()));
        }

        /// <summary>
        /// Invalid headers and chunks cannot be mistaken for a native capture.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [TestCase("signature", "not a PNG")]
        [TestCase("crc", "checksum")]
        [TestCase("truncated", "truncated chunk")]
        [TestCase("trailing", "chunk contract")]
        [TestCase("interlaced", "RGBA8")]
        [TestCase("rgb", "RGBA8")]
        [TestCase("iend", "chunk contract")]
        [TestCase("zlib-trailing", "pixel storage")]
        [TestCase("zlib-truncated", "checksum")]
        [TestCase("pixels", "pixel storage")]
        public void InvalidPngIsRejected(string corruption, string message)
        {
            byte[] png = PngFixture.Create();
            switch (corruption)
            {
                case "signature":
                    png[0] = 0;
                    break;
                case "crc":
                    png[44] ^= 1;
                    break;
                case "truncated":
                    png = png[..^1];
                    break;
                case "trailing":
                    png = [.. png, 0];
                    break;
                case "interlaced":
                    png = PngFixture.ReplaceChunk(png, "IHDR", data =>
                    {
                        data[12] = 1;
                        return data;
                    });
                    break;
                case "rgb":
                    png = PngFixture.ReplaceChunk(png, "IHDR", data =>
                    {
                        data[9] = 2;
                        return data;
                    });
                    break;
                case "iend":
                    png = PngFixture.ReplaceChunk(png, "IEND", _ => [0]);
                    break;
                case "zlib-trailing":
                    png = PngFixture.ReplaceChunk(png, "IDAT", data => [.. data, 0]);
                    break;
                case "zlib-truncated":
                    png = PngFixture.ReplaceChunk(png, "IDAT", data => data[..^1]);
                    break;
                case "pixels":
                    png = PngFixture.ReplaceChunk(png, "IHDR", data =>
                    {
                        BinaryPrimitives.WriteUInt32BigEndian(data, 3);
                        return data;
                    });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(corruption));
            }

            Assert.That(() => NativeImageEvidence.Parse(png, "bad.png"),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contains(message));
        }

        /// <summary>
        /// Dimensions are bounded before decompression or pixel allocation.
        /// </summary>
        [TestCase(0u, 1u)]
        [TestCase(1u, 0u)]
        [TestCase(1025u, 1u)]
        [TestCase(1u, 1025u)]
        [TestCase(uint.MaxValue, 1u)]
        public void OutOfBudgetDimensionsAreRejected(uint width, uint height)
        {
            byte[] png = PngFixture.ReplaceChunk(PngFixture.Create(), "IHDR", data =>
            {
                BinaryPrimitives.WriteUInt32BigEndian(data, width);
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), height);
                return data;
            });

            Assert.That(() => NativeImageEvidence.Parse(png, "large.png"),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contains("capture budget"));
        }

        /// <summary>
        /// The documented dimension ceiling is accepted rather than an off-by-one smaller limit.
        /// </summary>
        [TestCase(1024, 1)]
        [TestCase(1, 1024)]
        [TestCase(1024, 1024)]
        public void MaximumDimensionsAreAccepted(int width, int height)
        {
            NativeImageFrame frame = NativeImageEvidence.Parse(PngFixture.Create(width, height), "maximum.png");

            Assert.That(frame.Width, Is.EqualTo(width));
            Assert.That(frame.Height, Is.EqualTo(height));
        }

        /// <summary>
        /// Uniform images and unsupported row filters cannot establish rendered content.
        /// </summary>
        [Test]
        public void UniformFrameAndNonzeroFilterAreRejected()
        {
            Assert.That(() => NativeImageEvidence.Parse(PngFixture.Create(red: 0), "blank.png"),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contains("uniform image"));
            Assert.That(() => NativeImageEvidence.Parse(PngFixture.Create(filter: 1), "filtered.png"),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contains("row-filter"));
        }

        /// <summary>
        /// The file budget is enforced before parsing.
        /// </summary>
        [Test]
        public void OversizedFileIsRejected()
        {
            Assert.That(() => NativeImageEvidence.Parse(new byte[NativeImageEvidence.MaximumBytes + 1], "large.png"),
                Throws.InstanceOf<InvalidDataException>().With.Message.Contains("byte budget"));
        }

        /// <summary>
        /// Two valid files still fail if their decoded pixels match or dimensions differ.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UnchangedPixelsOrChangedDimensionsAreRejectedAsync(bool changeDimensions)
        {
            string directory = Path.Combine(Path.GetTempPath(), "ualens-image-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                byte[] before = PngFixture.Create(compression: CompressionLevel.NoCompression);
                byte[] after = PngFixture.Create(width: changeDimensions ? 3 : 2);
                Assert.That(before, Is.Not.EqualTo(after), "The encoded bytes must differ in both cases.");
                await File.WriteAllBytesAsync(Path.Combine(directory, "native-before.png"), before).ConfigureAwait(false);
                await File.WriteAllBytesAsync(Path.Combine(directory, "native-after.png"), after).ConfigureAwait(false);

                Assert.That(() => NativeImageEvidence.ReadAsync(directory),
                    Throws.InstanceOf<InvalidDataException>().With.Message.Contains(
                        changeDimensions ? "dimensions" : "different image"));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
