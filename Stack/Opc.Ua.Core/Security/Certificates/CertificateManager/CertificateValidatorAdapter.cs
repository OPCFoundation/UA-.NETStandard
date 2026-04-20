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

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Adapter that wraps the new <see cref="ICertificateValidatorEx"/>
    /// and exposes the legacy <see cref="ICertificateValidator"/> interface.
    /// </summary>
    public sealed class CertificateValidatorAdapter : ICertificateValidator
    {
        private readonly ICertificateValidatorEx _inner;

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateValidatorAdapter"/> class.
        /// </summary>
        /// <param name="inner">The extended validator to wrap.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="inner"/> is <see langword="null"/>.
        /// </exception>
        public CertificateValidatorAdapter(ICertificateValidatorEx inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <inheritdoc/>
        public async Task ValidateAsync(Certificate certificate, CancellationToken ct)
        {
            var result = await _inner.ValidateAsync(certificate, ct: ct).ConfigureAwait(false);
            if (!result.IsValid)
            {
                throw new ServiceResultException(result.StatusCode);
            }
        }

        /// <inheritdoc/>
        public async Task ValidateAsync(CertificateCollection certificateChain, CancellationToken ct)
        {
            var result = await _inner.ValidateAsync(certificateChain, ct: ct).ConfigureAwait(false);
            if (!result.IsValid)
            {
                throw new ServiceResultException(result.StatusCode);
            }
        }
    }
}
