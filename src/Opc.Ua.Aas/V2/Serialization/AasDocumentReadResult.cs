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

namespace Opc.Ua.Aas.V2
{
    /// <summary>
    /// The result of reading an AAS V2 document serialization.
    /// </summary>
    public sealed record AasDocumentReadResult
    {
        private AasDocumentReadResult(AasEnvironment? environment, string? error)
        {
            Environment = environment;
            Error = error;
        }

        /// <summary>
        /// Gets the parsed environment when <see cref="Succeeded"/> is <c>true</c>.
        /// </summary>
        public AasEnvironment? Environment { get; }

        /// <summary>
        /// Gets the diagnostic when <see cref="Succeeded"/> is <c>false</c>.
        /// </summary>
        public string? Error { get; }

        /// <summary>
        /// Gets whether the document was read successfully.
        /// </summary>
        public bool Succeeded => Environment is not null;

        /// <summary>
        /// Creates a successful read result.
        /// </summary>
        /// <param name="environment">The parsed environment.</param>
        /// <returns>A successful read result.</returns>
        public static AasDocumentReadResult Success(AasEnvironment environment)
        {
            if (environment is null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            return new AasDocumentReadResult(environment, null);
        }

        /// <summary>
        /// Creates a failed read result.
        /// </summary>
        /// <param name="error">The diagnostic.</param>
        /// <returns>A failed read result.</returns>
        public static AasDocumentReadResult Failure(string error)
        {
            if (error is null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            return new AasDocumentReadResult(null, error);
        }
    }
}
