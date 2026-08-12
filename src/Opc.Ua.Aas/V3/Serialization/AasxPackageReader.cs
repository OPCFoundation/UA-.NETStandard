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
using System.IO.Packaging;
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

            try
            {
                using Package package = Package.Open(stream, FileMode.Open, FileAccess.Read);
                if (!TryGetOriginPart(package, out PackagePart? originPart, out string? error))
                {
                    return AasxPackageReadResult.Failure(error ?? "The AASX package has no origin part.");
                }

                if (originPart is null)
                {
                    return AasxPackageReadResult.Failure("The AASX package has no origin part.");
                }

                if (!TryGetEnvironmentPart(package, originPart, out PackagePart? environmentPart, out error))
                {
                    return AasxPackageReadResult.Failure(error ?? "The AASX package has no environment part.");
                }

                if (environmentPart is null)
                {
                    return AasxPackageReadResult.Failure("The AASX package has no environment part.");
                }

                AasDocumentReadResult documentResult = await ReadEnvironmentAsync(
                    environmentPart,
                    cancellationToken).ConfigureAwait(false);
                if (!documentResult.Succeeded || documentResult.Environment is null)
                {
                    return AasxPackageReadResult.Failure(
                        documentResult.Error ?? "The AASX environment document is malformed.");
                }

                ArrayOf<AasxSupplementaryFile> supplementaryFiles = await ReadSupplementaryFilesAsync(
                    package,
                    originPart.Uri,
                    environmentPart.Uri,
                    cancellationToken).ConfigureAwait(false);

                return AasxPackageReadResult.Success(documentResult.Environment, supplementaryFiles);
            }
            catch (Exception ex) when (IsPackageReadException(ex))
            {
                return AasxPackageReadResult.Failure("The AASX package is malformed: " + ex.Message);
            }
        }

        private static bool TryGetOriginPart(Package package, out PackagePart? originPart, out string? error)
        {
            foreach (PackageRelationship relationship in package.GetRelationshipsByType(
                AasxPackageRelationshipTypes.Origin))
            {
                if (relationship.TargetMode == TargetMode.External)
                {
                    continue;
                }

                Uri originUri = ToPartUri(relationship.TargetUri);
                if (package.PartExists(originUri))
                {
                    originPart = package.GetPart(originUri);
                    error = null;
                    return true;
                }
            }

            originPart = null;
            error = "The AASX package does not contain an internal aasx-origin relationship.";
            return false;
        }

        private static bool TryGetEnvironmentPart(
            Package package,
            PackagePart originPart,
            out PackagePart? environmentPart,
            out string? error)
        {
            foreach (PackageRelationship relationship in originPart.GetRelationshipsByType(
                AasxPackageRelationshipTypes.Environment))
            {
                if (relationship.TargetMode == TargetMode.External)
                {
                    continue;
                }

                Uri environmentUri = ResolvePartUri(originPart.Uri, relationship.TargetUri);
                if (package.PartExists(environmentUri))
                {
                    environmentPart = package.GetPart(environmentUri);
                    error = null;
                    return true;
                }
            }

            environmentPart = null;
            error = "The AASX origin part does not contain an internal aas-spec relationship.";
            return false;
        }

        private static async Task<AasDocumentReadResult> ReadEnvironmentAsync(
            PackagePart environmentPart,
            CancellationToken cancellationToken)
        {
            using Stream stream = environmentPart.GetStream(FileMode.Open, FileAccess.Read);
            string contentType = environmentPart.ContentType;
            if (IsJsonContentType(contentType))
            {
                return await new AasJsonReader().ReadAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            if (IsXmlContentType(contentType))
            {
                using MemoryStream xmlStream = await CopyWithoutUtf8PreambleAsync(stream, cancellationToken)
                    .ConfigureAwait(false);
                return await new AasXmlReader().ReadAsync(xmlStream, cancellationToken).ConfigureAwait(false);
            }

            return AasDocumentReadResult.Failure(
                "The AASX environment part content type is not supported: " + contentType);
        }

        private static async Task<ArrayOf<AasxSupplementaryFile>> ReadSupplementaryFilesAsync(
            Package package,
            Uri originUri,
            Uri environmentUri,
            CancellationToken cancellationToken)
        {
            var files = new List<AasxSupplementaryFile>();
            PackagePart originPart = package.GetPart(originUri);
            foreach (PackageRelationship relationship in originPart.GetRelationshipsByType(
                AasxPackageRelationshipTypes.SupplementaryFile))
            {
                if (relationship.TargetMode == TargetMode.External)
                {
                    continue;
                }

                Uri partUri = ResolvePartUri(originUri, relationship.TargetUri);
                if (partUri == environmentUri || !package.PartExists(partUri))
                {
                    continue;
                }

                PackagePart part = package.GetPart(partUri);
                using Stream partStream = part.GetStream(FileMode.Open, FileAccess.Read);
                using var buffer = new MemoryStream();
                await partStream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                files.Add(new AasxSupplementaryFile(
                    part.Uri,
                    part.ContentType,
                    ByteString.From(buffer.ToArray())));
            }

            return new ArrayOf<AasxSupplementaryFile>(files.ToArray());
        }

        private static Uri ResolvePartUri(Uri sourceUri, Uri targetUri)
        {
            if (targetUri.OriginalString.Length > 0 && targetUri.OriginalString[0] == '/')
            {
                return ToPartUri(targetUri);
            }

            return PackUriHelper.ResolvePartUri(sourceUri, targetUri);
        }

        private static Uri ToPartUri(Uri uri)
        {
            return PackUriHelper.CreatePartUri(uri);
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

        private static async Task<MemoryStream> CopyWithoutUtf8PreambleAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
            byte[] bytes = buffer.ToArray();
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

        private static bool IsPackageReadException(Exception ex)
        {
            return ex is IOException ||
                ex is InvalidDataException ||
                ex is FileFormatException ||
                ex is NotSupportedException ||
                ex is ArgumentException;
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
        public const string Origin = "http://www.admin-shell.io/aasx/relationships/aasx-origin";

        /// <summary>
        /// Relationship from the AASX origin part to the AAS environment part.
        /// </summary>
        public const string Environment = "http://www.admin-shell.io/aasx/relationships/aas-spec";

        /// <summary>
        /// Relationship from the AASX origin part to a supplementary file part.
        /// </summary>
        public const string SupplementaryFile = "http://www.admin-shell.io/aasx/relationships/aas-suppl";
    }
}
