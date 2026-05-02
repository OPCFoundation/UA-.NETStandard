/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// Describes the outcome of a certificate validation operation.
    /// </summary>
    public sealed class CertificateValidationResult
    {
        /// <summary>
        /// A cached successful result.
        /// </summary>
        public static CertificateValidationResult Success { get; } =
            new CertificateValidationResult(true, StatusCodes.Good, [], false);

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateValidationResult"/> class.
        /// </summary>
        /// <param name="isValid">Whether the certificate is valid.</param>
        /// <param name="statusCode">The OPC UA status code.</param>
        /// <param name="errors">
        /// Individual <see cref="ServiceResult"/> entries describing each error.
        /// </param>
        /// <param name="isSuppressible">
        /// Whether the validation errors can be suppressed by a callback.
        /// </param>
        public CertificateValidationResult(
            bool isValid,
            StatusCode statusCode,
            IReadOnlyList<ServiceResult> errors,
            bool isSuppressible)
        {
            IsValid = isValid;
            StatusCode = statusCode;
            Errors = errors;
            IsSuppressible = isSuppressible;
        }

        /// <summary>
        /// Gets a value indicating whether the certificate passed validation.
        /// </summary>
        public bool IsValid { get; }

        /// <summary>
        /// Gets the primary OPC UA status code for the validation result.
        /// </summary>
        public StatusCode StatusCode { get; }

        /// <summary>
        /// Gets the list of individual validation errors.
        /// </summary>
        public IReadOnlyList<ServiceResult> Errors { get; }

        /// <summary>
        /// Gets a value indicating whether the validation errors can be
        /// suppressed by an application-level callback.
        /// </summary>
        public bool IsSuppressible { get; }
    }
}
