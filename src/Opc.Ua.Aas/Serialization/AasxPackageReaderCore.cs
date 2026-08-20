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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Aas
{
    /// <summary>
    /// Reads the version-neutral Open Packaging Conventions structure of an AASX package.
    /// </summary>
    public sealed class AasxPackageReaderCore
    {
        /// <summary>
        /// Reads the environment part bytes and supplementary files from an AASX package.
        /// </summary>
        /// <param name="stream">The OPC package stream.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The neutral package parts or a diagnostic.</returns>
        public async Task<AasxPackageDocumentReadResult> ReadAsync(
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
                    return AasxPackageDocumentReadResult.Failure(
                        error ?? "The AASX package has no origin part.");
                }

                if (originPart is null)
                {
                    return AasxPackageDocumentReadResult.Failure("The AASX package has no origin part.");
                }

                if (!TryGetEnvironmentPart(package, originPart, out PackagePart? environmentPart, out error))
                {
                    return AasxPackageDocumentReadResult.Failure(
                        error ?? "The AASX package has no environment part.");
                }

                if (environmentPart is null)
                {
                    return AasxPackageDocumentReadResult.Failure("The AASX package has no environment part.");
                }

                ByteString environmentContent = await ReadPartContentAsync(environmentPart, cancellationToken)
                    .ConfigureAwait(false);
                ArrayOf<AasxPackageSupplementaryFile> supplementaryFiles = await ReadSupplementaryFilesAsync(
                    package,
                    originPart.Uri,
                    environmentPart.Uri,
                    cancellationToken).ConfigureAwait(false);

                return AasxPackageDocumentReadResult.Success(
                    new AasxPackageEnvironmentPart(
                        environmentPart.Uri,
                        environmentPart.ContentType,
                        environmentContent),
                    supplementaryFiles);
            }
            catch (Exception ex) when (IsPackageReadException(ex))
            {
                return AasxPackageDocumentReadResult.Failure("The AASX package is malformed: " + ex.Message);
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

        private static async Task<ArrayOf<AasxPackageSupplementaryFile>> ReadSupplementaryFilesAsync(
            Package package,
            Uri originUri,
            Uri environmentUri,
            CancellationToken cancellationToken)
        {
            var files = new List<AasxPackageSupplementaryFile>();
            var seen = new HashSet<Uri>();

            // IDTA anchors aas-suppl on the environment (aas-spec) part, and the
            // reference implementation reads and writes it there. Packages this
            // library wrote before that was corrected anchor it on the origin
            // part, so both are accepted and the environment part is walked
            // first.
            foreach (Uri sourceUri in new[] { environmentUri, originUri })
            {
                if (!package.PartExists(sourceUri))
                {
                    continue;
                }

                PackagePart sourcePart = package.GetPart(sourceUri);
                foreach (PackageRelationship relationship in sourcePart.GetRelationshipsByType(
                    AasxPackageRelationshipTypes.SupplementaryFile))
                {
                    if (relationship.TargetMode == TargetMode.External)
                    {
                        continue;
                    }

                    Uri partUri = ResolvePartUri(sourceUri, relationship.TargetUri);
                    if (partUri == environmentUri || !package.PartExists(partUri) || !seen.Add(partUri))
                    {
                        continue;
                    }

                    PackagePart part = package.GetPart(partUri);
                    files.Add(new AasxPackageSupplementaryFile(
                        part.Uri,
                        part.ContentType,
                        await ReadPartContentAsync(part, cancellationToken).ConfigureAwait(false)));
                }
            }

            return new ArrayOf<AasxPackageSupplementaryFile>(files.ToArray());
        }

        private static async Task<ByteString> ReadPartContentAsync(
            PackagePart part,
            CancellationToken cancellationToken)
        {
            using Stream partStream = part.GetStream(FileMode.Open, FileAccess.Read);
            using var buffer = new MemoryStream();
            await partStream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
            return ByteString.From(buffer.ToArray());
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
        /// Relationship from the AAS environment part to a supplementary file part.
        /// </summary>
        public const string SupplementaryFile = "http://www.admin-shell.io/aasx/relationships/aas-suppl";
    }
}

