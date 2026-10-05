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
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Reproducers for the resource abuse findings of the codec audit, built in process by
    /// the <see cref="ResourceReproducerBuilders"/> so the malicious shape is reviewable code
    /// rather than opaque bytes. Each test asserts that the fixed decoder rejects the input
    /// cleanly; the same bytes are written to the fuzz corpus (see <see cref="RegenerateCorpus"/>)
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
                () => DecodeExtensionObject(input));

            Assert.That(
                ex.StatusCode,
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
                () => DecodeVariantXml(input));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));

            // The pre-fix codec overflowed the stack reading this; the target swallows the
            // bounded rejection, and FuzzStackTestcasesAsync replays it in a child process.
            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzVariantXml(input));
        }

        private static void DecodeVariantXml(byte[] input)
        {
            using var stream = new MemoryStream(input, writable: false);
            using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(
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
                () => DecodeVariant(input));

            Assert.That(
                ex.StatusCode,
                Is.EqualTo(StatusCodes.BadDecodingError)
                    .Or.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzVariantBinary(input));
            Assert.DoesNotThrow(() => FuzzableCode.LibfuzzBinaryDecoder(input));
        }

        /// <summary>
        /// Rewrites the checked-in reproducer corpus from the builders. Run explicitly after a
        /// builder changes, then commit the regenerated files. The libFuzzer content-hash names
        /// keep the corpus stable when the bytes are unchanged.
        /// </summary>
        [Test]
        [Explicit("Regenerates checked-in corpus files.")]
        [Category("Regeneration")]
        public void RegenerateCorpus()
        {
            string repo = FindProjectDirectory();
            string binaryRepo = Path.Combine(repo, "Assets", "Repo");
            string stackDir = Path.Combine(
                repo, "..", "Opc.Ua.Encoders.Fuzz.Corpus", "StackTestcases");

            WriteCrashInput(binaryRepo, ResourceReproducerBuilders.BuildNestedDataValueArrays());
            WriteCrashInput(binaryRepo, ResourceReproducerBuilders.BuildOversizedDecimalExtensionObject());
            WriteCrashInput(stackDir, ResourceReproducerBuilders.BuildDeeplyNestedXmlVariant());
        }

        private static void WriteCrashInput(string directory, byte[] input)
        {
            Directory.CreateDirectory(directory);
            string name = "crash-" + ResourceReproducerBuilders.ContentHash(input);
            File.WriteAllBytes(Path.Combine(directory, name), input);
            TestContext.Out.WriteLine($"wrote {name} ({input.Length} bytes) to {directory}");
        }

        private static string FindProjectDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null &&
                !File.Exists(Path.Combine(directory.FullName, "Opc.Ua.Encoders.Fuzz.Tests.csproj")))
            {
                directory = directory.Parent;
            }
            return directory?.FullName
                ?? throw new InvalidOperationException("Could not locate the test project directory.");
        }
    }

    /// <summary>
    /// Builds the resource-abuse reproducer inputs. Each builder documents the wire shape it
    /// produces so the reproducer can be reviewed without decoding it.
    /// </summary>
    internal static class ResourceReproducerBuilders
    {
        // Variant encoding (OPC 10000-6 5.2.2.16): the low six bits are the built-in type, bit
        // 0x80 marks an array whose Int32 ArrayLength follows.
        private const byte kVariantArrayBit = 0x80;
        private const byte kDataValueType = 23;
        private const byte kDataValueHasValue = 0x01;

        /// <summary>
        /// A chain of Variant DataValue-arrays. Each level declares the maximum permitted
        /// element count but supplies only its first element, which is the next level. A
        /// decoder that sizes storage from the count allocates that count at every level.
        /// </summary>
        public static byte[] BuildNestedDataValueArrays(int depth = 80, int arrayLength = 4096)
        {
            using var stream = new MemoryStream();
            for (int level = 0; level < depth; level++)
            {
                // Variant: array of DataValue, ArrayLength = arrayLength.
                stream.WriteByte(kVariantArrayBit | kDataValueType);
                WriteInt32(stream, arrayLength);
                // Element [0]: a DataValue whose only field is its Variant Value (the next level).
                stream.WriteByte(kDataValueHasValue);
            }

            // Innermost Value: a Null Variant terminates the chain.
            stream.WriteByte(0x00);
            return stream.ToArray();
        }

        /// <summary>
        /// An ExtensionObject holding a Decimal whose unscaled value carries more octets than the
        /// fixed cap. Rendering that many octets as decimal digits is quadratic.
        /// </summary>
        public static byte[] BuildOversizedDecimalExtensionObject(int octetCount = 4096)
        {
            // Decimal body (OPC 10000-6 5.1.10): Int16 Scale followed by the unscaled octets.
            byte[] body = new byte[2 + octetCount];
            for (int ii = 2; ii < body.Length; ii++)
            {
                body[ii] = 0xFF;
            }

            // Written through the encoder as a raw-body ExtensionObject typed as the Decimal
            // DataType, so the NodeId and length framing are exactly what a decoder expects and
            // ReadExtensionObject dispatches to Decimal.Decode.
            var extension = new ExtensionObject(
                new ExpandedNodeId(DataTypes.Decimal),
                ByteString.From(body));
            using var encoder = new BinaryEncoder(FuzzableCode.MessageContext);
            encoder.WriteExtensionObject(null, extension);
            return encoder.CloseAndReturnBuffer();
        }

        /// <summary>
        /// The XML form of a Variant whose value is an XmlElement nested past the encoding
        /// nesting level. Reading it recursively through System.Xml overflowed the stack.
        /// </summary>
        public static byte[] BuildDeeplyNestedXmlVariant(int depth = 512)
        {
            var builder = new StringBuilder();
            builder.Append("<uax:Value xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">");
            builder.Append("<uax:Value><uax:XmlElement>");
            for (int ii = 0; ii < depth; ii++)
            {
                builder.Append("<n>");
            }
            for (int ii = 0; ii < depth; ii++)
            {
                builder.Append("</n>");
            }
            builder.Append("</uax:XmlElement></uax:Value></uax:Value>");
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

#pragma warning disable CA5350, CA1850, CA1307, CA1872 // SHA-1 content-address, net48-compatible hex.
        public static string ContentHash(byte[] input)
        {
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            return BitConverter.ToString(sha1.ComputeHash(input))
                .Replace("-", string.Empty).ToLowerInvariant();
        }
#pragma warning restore CA5350, CA1850, CA1307, CA1872

        private static void WriteInt32(MemoryStream stream, int value)
        {
            byte[] buffer = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            stream.Write(buffer, 0, buffer.Length);
        }

        private static void WriteUInt16(MemoryStream stream, ushort value)
        {
            byte[] buffer = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            stream.Write(buffer, 0, buffer.Length);
        }
    }
}
