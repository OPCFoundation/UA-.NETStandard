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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// Reads AASX packages using Open Packaging Conventions relationships.
    /// </summary>
    public sealed class AasxPackageReader
    {
        /// <summary>
        /// Reads an AASX package from a stream.
        /// </summary>
        /// <param name="stream">The OPC package stream.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The parsed environment, supplementary files or a diagnostic.</returns>
        public async Task<AasxPackageReadResult> ReadAsync(
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            AasxPackageDocumentReadResult packageResult = await new AasxPackageReaderCore()
                .ReadAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            if (!packageResult.Succeeded || packageResult.EnvironmentPart is null)
            {
                return AasxPackageReadResult.Failure(packageResult.Error ?? "The AASX package is malformed.");
            }

            AasDocumentReadResult documentResult = await ReadEnvironmentAsync(
                packageResult.EnvironmentPart,
                cancellationToken).ConfigureAwait(false);
            if (!documentResult.Succeeded || documentResult.Environment is null)
            {
                return AasxPackageReadResult.Failure(
                    documentResult.Error ?? "The AASX environment document is malformed.");
            }

            return AasxPackageReadResult.Success(
                documentResult.Environment,
                ToV3SupplementaryFiles(packageResult.SupplementaryFiles));
        }

        private static async Task<AasDocumentReadResult> ReadEnvironmentAsync(
            AasxPackageEnvironmentPart environmentPart,
            CancellationToken cancellationToken)
        {
            using Stream stream = ToStream(environmentPart.Content);
            string contentType = environmentPart.ContentType;
            if (IsJsonContentType(contentType))
            {
                return await new AasJsonReader().ReadAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            if (IsXmlContentType(contentType))
            {
                using MemoryStream xmlStream = CopyWithoutUtf8Preamble(environmentPart.Content);
                return await new AasXmlReader().ReadAsync(xmlStream, cancellationToken).ConfigureAwait(false);
            }

            return AasDocumentReadResult.Failure(
                "The AASX environment part content type is not supported: " + contentType);
        }

        private static ArrayOf<AasxSupplementaryFile> ToV3SupplementaryFiles(
            ArrayOf<Opc.Ua.Aas.AasxPackageSupplementaryFile> files)
        {
            var result = new List<AasxSupplementaryFile>();
            foreach (Opc.Ua.Aas.AasxPackageSupplementaryFile file in files)
            {
                result.Add(new AasxSupplementaryFile(file.PartUri, file.ContentType, file.Content));
            }

            return new ArrayOf<AasxSupplementaryFile>(result.ToArray());
        }

        private static MemoryStream ToStream(ByteString content)
        {
            return new MemoryStream(content.ToArray(), writable: false);
        }

        private static bool IsJsonContentType(string contentType)
        {
            string mediaType = GetMediaType(contentType);
            return mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mediaType, "text/json", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsXmlContentType(string contentType)
        {
            string mediaType = GetMediaType(contentType);
            return mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mediaType, "application/xml", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mediaType, "text/xml", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetMediaType(string contentType)
        {
            int separator = -1;
            for (int ii = 0; ii < contentType.Length; ii++)
            {
                if (contentType[ii] == ';')
                {
                    separator = ii;
                    break;
                }
            }

            return separator < 0 ? contentType.Trim() : contentType.Substring(0, separator).Trim();
        }

        private static MemoryStream CopyWithoutUtf8Preamble(ByteString content)
        {
            byte[] bytes = content.ToArray();
            byte[] preamble = Encoding.UTF8.GetPreamble();
            int offset = StartsWith(bytes, preamble) ? preamble.Length : 0;
            return new MemoryStream(bytes, offset, bytes.Length - offset, writable: false);
        }

        private static bool StartsWith(byte[] bytes, byte[] prefix)
        {
            if (bytes.Length < prefix.Length)
            {
                return false;
            }

            for (int ii = 0; ii < prefix.Length; ii++)
            {
                if (bytes[ii] != prefix[ii])
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// AASX relationship type URIs.
    /// </summary>
    public static class AasxPackageRelationshipTypes
    {
        /// <summary>
        /// Package relationship from the package root to the AASX origin part.
        /// </summary>
        public const string Origin = Opc.Ua.Aas.AasxPackageRelationshipTypes.Origin;

        /// <summary>
        /// Relationship from the AASX origin part to the AAS environment part.
        /// </summary>
        public const string Environment = Opc.Ua.Aas.AasxPackageRelationshipTypes.Environment;

        /// <summary>
        /// Relationship from the AASX origin part to a supplementary file part.
        /// </summary>
        public const string SupplementaryFile = Opc.Ua.Aas.AasxPackageRelationshipTypes.SupplementaryFile;
    }
}

