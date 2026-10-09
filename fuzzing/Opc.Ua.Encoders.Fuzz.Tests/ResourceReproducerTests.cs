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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Reproducers for the resource abuse findings of the codec audit, built in process by
    /// the <see cref="ResourceReproducerBuilders"/> so the malicious shape is reviewable code
    /// rather than opaque bytes. Each test asserts that the fixed decoder rejects the input
    /// cleanly; the encoder fuzz tools regenerate the same bytes in the fuzz corpus,
    /// where every target replays them under the allocation, time and stack oracles.
    /// </summary>
    [TestFixture]
    [Category("Fuzzing")]
    public sealed class ResourceReproducerTests
    {
        /// <summary>
        /// B1-1: nested DataValue arrays each declaring the maximum element count multiply the
        /// storage a decoder allocates from the length prefixes. The fix checks the declared
        /// count against the remaining message before allocating, so the decode is rejected at
        /// the outermost array.
        /// </summary>
        [Test]
        public void NestedDataValueArraysAreRejectedBeforeAllocation()
        {
            byte[] input = ResourceReproducerBuilders.BuildNestedDataValueArrays();

            AssertVariantDecodeRejected(input);
        }

        /// <summary>
        /// T1-3: an ExtensionObject Decimal body carrying more unscaled octets than the fixed
        /// cap makes every later rendering as decimal digits quadratic. The fix rejects the
        /// body before it is read.
        /// </summary>
        [Test]
        public void OversizedDecimalExtensionObjectIsRejectedBeforeRendering()
        {
            byte[] input = ResourceReproducerBuilders.BuildOversizedDecimalExtensionObject();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => DecodeExtensionObject(input))!;

            Assert.That(
                ex!.StatusCode,
                Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded)
                    .Or.EqualTo(StatusCodes.BadDecodingError));
            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzExtensionObjectBinary(input));
            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzBinaryJsonEncoderCompact(input));
        }

        /// <summary>
        /// X1-1: an XmlElement value nested past the encoding nesting level overflowed the stack
        /// through the recursive System.Xml copy. The fix bounds the element depth. The input is
        /// replayed in a child process by FuzzStackTestcasesAsync, because the pre-fix overflow
        /// cannot be caught.
        /// </summary>
        [Test]
        public void DeeplyNestedXmlVariantIsRejectedWithinTheNestingLimit()
        {
            byte[] input = ResourceReproducerBuilders.BuildDeeplyNestedXmlVariant();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => DecodeVariantXml(input))!;
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));

            // The pre-fix codec overflowed the stack reading this; the target swallows the
            // bounded rejection, and FuzzStackTestcasesAsync replays it in a child process.
            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzVariantXml(input));
        }

        /// <summary>
        /// Regeneration reproduces the reviewed inputs byte-for-byte and does not alter unrelated files.
        /// </summary>
        [Test]
        public async Task RegenerationMatchesReviewedCorpusAndPreservesUnrelatedFilesAsync()
        {
            string repository = Path.Combine(Path.GetTempPath(), $"opcua-resource-corpus-{Guid.NewGuid():N}");
            try
            {
                string project = Path.Combine(repository, "fuzzing", "Opc.Ua.Encoders.Fuzz.Tests");
                Directory.CreateDirectory(project);
                using (File.Create(Path.Combine(project, "Opc.Ua.Encoders.Fuzz.Tests.csproj")))
                {
                }
                string unrelated = Path.Combine(project, "unrelated.bin");
                using (var stream = new FileStream(unrelated, FileMode.CreateNew))
                {
                    stream.WriteByte(0xFF);
                }

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    await ResourceReproducerBuilders.RegenerateAsync(repository, CancellationToken.None)
                        .ConfigureAwait(false);
                    foreach ((string root, string relative) in new[]
                    {
                        ("Opc.Ua.Encoders.Fuzz.Tests", Path.Combine(
                            "Assets", "Repo", "crash-32176652575b3960405facb617a09cc636cfe38a")),
                        ("Opc.Ua.Encoders.Fuzz.Tests", Path.Combine(
                            "Assets", "Repo", "crash-50148ad03465f3d6a7405466f2cca2c99233723e")),
                        ("Opc.Ua.Encoders.Fuzz.Corpus", Path.Combine(
                            "StackTestcases", "crash-54c094edc6382efb17e52df511a964d467fa5bf3"))
                    })
                    {
                        byte[] expected = await ReadBytesAsync(Path.Combine(AppContext.BaseDirectory, relative))
                            .ConfigureAwait(false);
                        byte[] actual = await ReadBytesAsync(Path.Combine(repository, "fuzzing", root, relative))
                            .ConfigureAwait(false);
                        Assert.That(actual, Is.EqualTo(expected), relative);
                    }
                    Assert.That(
                        Directory.GetFiles(repository, "*", SearchOption.AllDirectories), Has.Length.EqualTo(5));
                    Assert.That(await ReadBytesAsync(unrelated).ConfigureAwait(false), Is.EqualTo(new byte[] { 0xFF }));
                }
            }
            finally
            {
                Directory.Delete(repository, true);
            }
        }

        /// <summary>
        /// Invalid roots and cancelled requests fail without creating or modifying corpus directories.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RegenerationRejectsMissingRepositoryOrCancellation(bool cancelled)
        {
            string repository = Path.Combine(Path.GetTempPath(), $"opcua-resource-corpus-{Guid.NewGuid():N}");
            if (cancelled)
            {
                Assert.That(() => ResourceReproducerBuilders.RegenerateAsync(repository, new CancellationToken(true)),
                    Throws.TypeOf<OperationCanceledException>());
            }
            else
            {
                Assert.That(() => ResourceReproducerBuilders.RegenerateAsync(repository, CancellationToken.None),
                    Throws.TypeOf<DirectoryNotFoundException>());
            }
            Assert.That(Directory.Exists(repository), Is.False);
        }

        /// <summary>
        /// A missing root is rejected rather than implicitly selecting the current working directory.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        public void RegenerationRejectsMissingRoot(string? repositoryRoot)
        {
            Assert.That(() => ResourceReproducerBuilders.RegenerateAsync(repositoryRoot!, CancellationToken.None),
                Throws.InstanceOf<ArgumentException>());
        }

        private static async Task<byte[]> ReadBytesAsync(string path)
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(false);
            return buffer.ToArray();
        }

        private static void DecodeVariantXml(byte[] input)
        {
            using var stream = new MemoryStream(input, writable: false);
            using var reader = System.Xml.XmlReader.Create(
                stream, Utils.DefaultXmlReaderSettings());
            reader.MoveToContent();
            using var decoder = new XmlDecoder(reader, FuzzableCode.MessageContext);
            decoder.PushNamespace("http://opcfoundation.org/UA/2008/02/Types.xsd");
            _ = decoder.ReadVariant("Value");
        }

        private static void DecodeExtensionObject(byte[] input)
        {
            using var stream = new MemoryStream(input, writable: false);
            using var decoder = new BinaryDecoder(stream, FuzzableCode.MessageContext);
            _ = decoder.ReadExtensionObject(null);
        }

        private static void DecodeVariant(byte[] input)
        {
            using var stream = new MemoryStream(input, writable: false);
            using var decoder = new BinaryDecoder(stream, FuzzableCode.MessageContext);
            _ = decoder.ReadVariant(null);
        }

        private static void AssertVariantDecodeRejected(byte[] input)
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => DecodeVariant(input))!;

            Assert.That(
                ex!.StatusCode,
                Is.EqualTo(StatusCodes.BadDecodingError)
                    .Or.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzVariantBinary(input));
            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzBinaryDecoder(input));
        }

    }
}
