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

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// The AAS environment and supplementary package parts read from an AASX package.
    /// </summary>
    public sealed record AasxPackageReadResult
    {
        private AasxPackageReadResult(
            AasEnvironment? environment,
            ArrayOf<AasxSupplementaryFile> supplementaryFiles,
            string? error)
        {
            Environment = environment;
            SupplementaryFiles = supplementaryFiles;
            Error = error;
        }

        /// <summary>
        /// Gets the parsed environment when <see cref="Succeeded"/> is <c>true</c>.
        /// </summary>
        public AasEnvironment? Environment { get; }

        /// <summary>
        /// Gets the supplementary files carried by the package.
        /// </summary>
        public ArrayOf<AasxSupplementaryFile> SupplementaryFiles { get; }

        /// <summary>
        /// Gets the diagnostic when <see cref="Succeeded"/> is <c>false</c>.
        /// </summary>
        public string? Error { get; }

        /// <summary>
        /// Gets whether the package was read successfully.
        /// </summary>
        public bool Succeeded => Environment is not null;

        /// <summary>
        /// Creates a successful AASX read result.
        /// </summary>
        /// <param name="environment">The parsed environment.</param>
        /// <param name="supplementaryFiles">The supplementary files carried by the package.</param>
        /// <returns>A successful read result.</returns>
        public static AasxPackageReadResult Success(
            AasEnvironment environment,
            ArrayOf<AasxSupplementaryFile> supplementaryFiles)
        {
            if (environment is null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            return new AasxPackageReadResult(environment, supplementaryFiles, null);
        }

        /// <summary>
        /// Creates a failed AASX read result.
        /// </summary>
        /// <param name="error">The diagnostic.</param>
        /// <returns>A failed read result.</returns>
        public static AasxPackageReadResult Failure(string error)
        {
            if (error is null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            return new AasxPackageReadResult(null, ArrayOf<AasxSupplementaryFile>.Empty, error);
        }
    }

    /// <summary>
    /// A supplementary file part carried by an AASX package.
    /// </summary>
    /// <param name="PartUri">The OPC package part URI.</param>
    /// <param name="ContentType">The media type of the part.</param>
    /// <param name="Content">The part content.</param>
    public sealed record AasxSupplementaryFile(Uri PartUri, string ContentType, ByteString Content);
}
