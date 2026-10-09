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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Builds and regenerates the reviewed resource-abuse regression corpus.
    /// </summary>
    internal static class ResourceReproducerBuilders
    {
        /// <summary>
        /// Builds nested DataValue arrays whose declared lengths exceed the supplied message.
        /// </summary>
        internal static byte[] BuildNestedDataValueArrays(int depth = 80, int arrayLength = 4096)
        {
            using var stream = new MemoryStream();
            for (int level = 0; level < depth; level++)
            {
                stream.WriteByte(kVariantArrayBit | kDataValueType);
                WriteInt32(stream, arrayLength);
                stream.WriteByte(kDataValueHasValue);
            }
            stream.WriteByte(0x00);
            return stream.ToArray();
        }

        /// <summary>
        /// Builds a Decimal ExtensionObject whose unscaled value exceeds the decoder's fixed cap.
        /// </summary>
        internal static byte[] BuildOversizedDecimalExtensionObject(int octetCount = 4096)
        {
            byte[] body = new byte[2 + octetCount];
            for (int ii = 2; ii < body.Length; ii++)
            {
                body[ii] = 0xFF;
            }
            var extension = new ExtensionObject(
                new ExpandedNodeId(DataTypes.Decimal),
                ByteString.From(body));
            using var encoder = new BinaryEncoder(FuzzableCode.MessageContext);
            encoder.WriteExtensionObject(null, extension);
            return encoder.CloseAndReturnBuffer()!;
        }

        /// <summary>
        /// Builds an XML Variant nested beyond the encoding limit for isolated stack replay.
        /// </summary>
        internal static byte[] BuildDeeplyNestedXmlVariant(int depth = 512)
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

        /// <summary>
        /// Rewrites only the three reviewed inputs, preserving their libFuzzer content-addressed filenames.
        /// </summary>
        internal static async Task RegenerateAsync(string repositoryRoot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string root = Path.GetFullPath(repositoryRoot);
            string project = Path.Combine(
                root, "fuzzing", "Opc.Ua.Encoders.Fuzz.Tests");
            if (!File.Exists(Path.Combine(project, "Opc.Ua.Encoders.Fuzz.Tests.csproj")))
            {
                throw new DirectoryNotFoundException("The repository does not contain the encoder test project.");
            }
            string binaryRoot = Path.Combine(project, "Assets", "Repo");
            string stackRoot = Path.Combine(
                root, "fuzzing", "Opc.Ua.Encoders.Fuzz.Corpus", "StackTestcases");
            await WriteCrashInputAsync(binaryRoot, BuildNestedDataValueArrays(), cancellationToken)
                .ConfigureAwait(false);
            await WriteCrashInputAsync(binaryRoot, BuildOversizedDecimalExtensionObject(), cancellationToken)
                .ConfigureAwait(false);
            await WriteCrashInputAsync(stackRoot, BuildDeeplyNestedXmlVariant(), cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the libFuzzer filename hash, not a security or evidence digest.
        /// </summary>
#pragma warning disable CA5350, CA1850, CA1307, CA1872 // libFuzzer SHA-1 names on net48; TODO: migrate names with upstream.
        internal static string ContentHash(byte[] input)
        {
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            return BitConverter.ToString(sha1.ComputeHash(input))
                .Replace("-", string.Empty).ToLowerInvariant();
        }
#pragma warning restore CA5350, CA1850, CA1307, CA1872

        private static async Task WriteCrashInputAsync(
            string directory,
            byte[] input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "crash-" + ContentHash(input));
            using var stream = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
#if NETFRAMEWORK
            await stream.WriteAsync(input, 0, input.Length, cancellationToken).ConfigureAwait(false);
#else
            await stream.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
#endif
        }

        private static void WriteInt32(MemoryStream stream, int value)
        {
            byte[] buffer = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            stream.Write(buffer, 0, buffer.Length);
        }

        /// <summary>Variant array flag, followed by an Int32 element count.</summary>
        private const byte kVariantArrayBit = 0x80;

        /// <summary>DataValue built-in type identifier.</summary>
        private const byte kDataValueType = 23;

        /// <summary>DataValue encoding mask containing a Variant value.</summary>
        private const byte kDataValueHasValue = 0x01;
    }
}
