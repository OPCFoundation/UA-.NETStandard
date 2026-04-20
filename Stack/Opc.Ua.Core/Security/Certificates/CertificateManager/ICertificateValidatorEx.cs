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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Extended certificate validator that supports trust-list scoping,
    /// validation options, and structured validation results.
    /// </summary>
    /// <remarks>
    /// This interface is distinct from the existing
    /// <see cref="ICertificateValidator"/> which provides a simpler
    /// validation contract. Implementations may wrap or extend
    /// <see cref="ICertificateValidator"/>.
    /// </remarks>
    public interface ICertificateValidatorEx
    {
        /// <summary>
        /// Validates a certificate chain against the specified trust list.
        /// </summary>
        /// <param name="chain">The certificate chain to validate.</param>
        /// <param name="trustList">
        /// An optional trust list to validate against. When <see langword="null"/>,
        /// the default trust list is used.
        /// </param>
        /// <param name="options">
        /// Optional validation options that control which checks are performed.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// A <see cref="CertificateValidationResult"/> describing the outcome.
        /// </returns>
        Task<CertificateValidationResult> ValidateAsync(
            CertificateCollection chain,
            TrustListIdentifier? trustList = null,
            CertificateValidationOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Validates a single certificate against the specified trust list.
        /// </summary>
        /// <param name="certificate">The certificate to validate.</param>
        /// <param name="trustList">
        /// An optional trust list to validate against. When <see langword="null"/>,
        /// the default trust list is used.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// A <see cref="CertificateValidationResult"/> describing the outcome.
        /// </returns>
        Task<CertificateValidationResult> ValidateAsync(
            Certificate certificate,
            TrustListIdentifier? trustList = null,
            CancellationToken ct = default);

        // TODO: Add CertificateValidation event when Core dependency is resolved
    }
}
