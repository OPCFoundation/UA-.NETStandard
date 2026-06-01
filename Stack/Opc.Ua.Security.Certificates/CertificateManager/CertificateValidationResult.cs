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

using System.Collections.Generic;
using Opc.Ua.Types;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Result of certificate validation.
    /// </summary>
    public sealed class CertificateValidationResult
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="CertificateValidationResult"/> class.
        /// </summary>
        /// <param name="isValid">
        /// Whether the certificate is considered valid.
        /// </param>
        /// <param name="statusCode">
        /// The OPC UA status code for the validation outcome.
        /// </param>
        /// <param name="errors">
        /// The list of validation errors (may be empty).
        /// </param>
        /// <param name="isSuppressible">
        /// Whether the validation failure may be suppressed by
        /// application policy.
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
        /// Gets a value indicating whether the certificate is valid.
        /// </summary>
        public bool IsValid { get; }

        /// <summary>
        /// Gets the OPC UA status code for the validation outcome.
        /// </summary>
        public StatusCode StatusCode { get; }

        /// <summary>
        /// Gets the list of validation errors.
        /// </summary>
        public IReadOnlyList<ServiceResult> Errors { get; }

        /// <summary>
        /// Gets a value indicating whether the validation failure
        /// may be suppressed by application policy.
        /// </summary>
        public bool IsSuppressible { get; }

        /// <summary>
        /// Gets a successful validation result.
        /// </summary>
        public static CertificateValidationResult Success { get; } =
            new(true, StatusCodes.Good, [], false);

        /// <summary>
        /// Throws a <see cref="ServiceResultException"/> when the result is
        /// not valid. Use this to flow validation failures through callers
        /// that expect the legacy throwing contract without writing the
        /// <c>if (!result.IsValid) throw …</c> boilerplate.
        /// </summary>
        /// <remarks>
        /// When <see cref="Errors"/> contains at least one entry, the first
        /// entry is used as the inner <see cref="ServiceResult"/> so the
        /// caller sees the detailed error context. Otherwise the exception
        /// carries only <see cref="StatusCode"/>.
        /// </remarks>
        /// <exception cref="ServiceResultException">
        /// Thrown when <see cref="IsValid"/> is <see langword="false"/>.
        /// </exception>
        public void ThrowIfInvalid()
        {
            if (IsValid)
            {
                return;
            }

            if (Errors.Count > 0)
            {
                throw new ServiceResultException(Errors[0]);
            }

            throw new ServiceResultException(StatusCode);
        }
    }
}
