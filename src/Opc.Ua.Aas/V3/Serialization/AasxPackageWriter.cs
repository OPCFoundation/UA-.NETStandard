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
using System.IO.Packaging;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// Writes AAS environments as AASX Open Packaging Conventions packages.
    /// </summary>
    public sealed class AasxPackageWriter
    {
        /// <summary>
        /// Writes an AASX package with a JSON environment document.
        /// </summary>
        /// <param name="stream">The destination package stream.</param>
        /// <param name="environment">The environment to package.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task that completes when the package has been written.</returns>
        public Task WriteAsync(
            Stream stream,
            AasEnvironment environment,
            CancellationToken cancellationToken = default)
        {
            return WriteAsync(
                stream,
                environment,
                ArrayOf<AasxSupplementaryFile>.Empty,
                AasxPackageSerialization.Json,
                cancellationToken);
        }

        /// <summary>
        /// Writes an AASX package.
        /// </summary>
        /// <param name="stream">The destination package stream.</param>
        /// <param name="environment">The environment to package.</param>
        /// <param name="supplementaryFiles">The supplementary file parts to include.</param>
        /// <param name="serialization">The environment document serialization.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task that completes when the package has been written.</returns>
        public async Task WriteAsync(
            Stream stream,
            AasEnvironment environment,
            ArrayOf<AasxSupplementaryFile> supplementaryFiles,
            AasxPackageSerialization serialization = AasxPackageSerialization.Json,
            CancellationToken cancellationToken = default)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (environment is null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            using Package package = Package.Open(stream, FileMode.Create, FileAccess.ReadWrite);
            Uri originUri = PackUriHelper.CreatePartUri(new Uri("/aasx/aasx-origin", UriKind.Relative));
            PackagePart originPart = package.CreatePart(
                originUri,
                "application/vnd.admin-shell.aasx-origin",
                CompressionOption.Maximum);

            package.CreateRelationship(originUri, TargetMode.Internal, AasxPackageRelationshipTypes.Origin);

            Uri environmentUri = GetEnvironmentPartUri(serialization);
            PackagePart environmentPart = package.CreatePart(
                environmentUri,
                GetEnvironmentContentType(serialization),
                CompressionOption.Maximum);

            originPart.CreateRelationship(
                environmentUri,
                TargetMode.Internal,
                AasxPackageRelationshipTypes.Environment);

            using (Stream environmentStream = environmentPart.GetStream(FileMode.Create, FileAccess.Write))
            {
                if (serialization == AasxPackageSerialization.Json)
                {
                    await new AasJsonWriter().WriteAsync(environmentStream, environment, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await new AasXmlWriter().WriteAsync(environmentStream, environment, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            await WriteSupplementaryFilesAsync(environmentPart, package, supplementaryFiles, cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task WriteSupplementaryFilesAsync(
            PackagePart environmentPart,
            Package package,
            ArrayOf<AasxSupplementaryFile> supplementaryFiles,
            CancellationToken cancellationToken)
        {
            if (supplementaryFiles.IsNull || supplementaryFiles.Count == 0)
            {
                return;
            }

            for (int ii = 0; ii < supplementaryFiles.Count; ii++)
            {
                AasxSupplementaryFile file = supplementaryFiles[ii];
                if (file is null)
                {
                    throw new ArgumentException(
                        "The supplementary file collection contains a null file.",
                        nameof(supplementaryFiles));
                }

                if (file.PartUri is null)
                {
                    throw new ArgumentException(
                        "A supplementary file part URI is null.",
                        nameof(supplementaryFiles));
                }

                if (file.ContentType is null)
                {
                    throw new ArgumentException(
                        "A supplementary file content type is null.",
                        nameof(supplementaryFiles));
                }

                Uri partUri = PackUriHelper.CreatePartUri(file.PartUri);
                PackagePart part = package.CreatePart(partUri, file.ContentType, CompressionOption.Maximum);

                // Anchored on the environment part, which is where IDTA places
                // it and where the reference implementation looks for it.
                environmentPart.CreateRelationship(
                    partUri,
                    TargetMode.Internal,
                    AasxPackageRelationshipTypes.SupplementaryFile);

                using Stream partStream = part.GetStream(FileMode.Create, FileAccess.Write);
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                await partStream.WriteAsync(file.Content.Memory, cancellationToken).ConfigureAwait(false);
#else
                byte[] bytes = file.Content.ToArray();
                await partStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
#endif
            }
        }

        private static Uri GetEnvironmentPartUri(AasxPackageSerialization serialization)
        {
            string path = serialization == AasxPackageSerialization.Json
                ? "/aasx/environment/aasenv.json"
                : "/aasx/environment/aasenv.xml";
            return PackUriHelper.CreatePartUri(new Uri(path, UriKind.Relative));
        }

        private static string GetEnvironmentContentType(AasxPackageSerialization serialization)
        {
            return serialization == AasxPackageSerialization.Json
                ? "application/asset-administration-shell+json"
                : "application/asset-administration-shell+xml";
        }
    }
}
