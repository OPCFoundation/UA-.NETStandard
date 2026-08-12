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

namespace Opc.Ua.Aas
{
    /// <summary>
    /// A supplementary file part carried by an AASX package.
    /// </summary>
    /// <param name="PartUri">The OPC package part URI.</param>
    /// <param name="ContentType">The media type of the part.</param>
    /// <param name="Content">The part content.</param>
    public sealed record AasxPackageSupplementaryFile(Uri PartUri, string ContentType, ByteString Content);

    /// <summary>
    /// The neutral AASX environment part and supplementary files read from a package.
    /// </summary>
    public sealed record AasxPackageDocumentReadResult
    {
        private AasxPackageDocumentReadResult(
            AasxPackageEnvironmentPart? environmentPart,
            ArrayOf<AasxPackageSupplementaryFile> supplementaryFiles,
            string? error)
        {
            EnvironmentPart = environmentPart;
            SupplementaryFiles = supplementaryFiles;
            Error = error;
        }

        /// <summary>
        /// Gets the environment document part when <see cref="Succeeded"/> is <c>true</c>.
        /// </summary>
        public AasxPackageEnvironmentPart? EnvironmentPart { get; }

        /// <summary>
        /// Gets the supplementary files carried by the package.
        /// </summary>
        public ArrayOf<AasxPackageSupplementaryFile> SupplementaryFiles { get; }

        /// <summary>
        /// Gets the diagnostic when <see cref="Succeeded"/> is <c>false</c>.
        /// </summary>
        public string? Error { get; }

        /// <summary>
        /// Gets whether the package was read successfully.
        /// </summary>
        public bool Succeeded => EnvironmentPart is not null;

        /// <summary>
        /// Creates a successful package document read result.
        /// </summary>
        /// <param name="environmentPart">The environment part.</param>
        /// <param name="supplementaryFiles">The supplementary files.</param>
        /// <returns>A successful read result.</returns>
        public static AasxPackageDocumentReadResult Success(
            AasxPackageEnvironmentPart environmentPart,
            ArrayOf<AasxPackageSupplementaryFile> supplementaryFiles)
        {
            if (environmentPart is null)
            {
                throw new ArgumentNullException(nameof(environmentPart));
            }

            return new AasxPackageDocumentReadResult(environmentPart, supplementaryFiles, null);
        }

        /// <summary>
        /// Creates a failed package document read result.
        /// </summary>
        /// <param name="error">The diagnostic.</param>
        /// <returns>A failed read result.</returns>
        public static AasxPackageDocumentReadResult Failure(string error)
        {
            if (error is null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            return new AasxPackageDocumentReadResult(null, ArrayOf<AasxPackageSupplementaryFile>.Empty, error);
        }
    }

    /// <summary>
    /// The AAS environment document part carried by an AASX package.
    /// </summary>
    /// <param name="PartUri">The OPC package part URI.</param>
    /// <param name="ContentType">The part media type.</param>
    /// <param name="Content">The document bytes.</param>
    public sealed record AasxPackageEnvironmentPart(Uri PartUri, string ContentType, ByteString Content);
}

