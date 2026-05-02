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
    /// Represents a transaction for modifying a trust-list.
    /// Changes are staged until <see cref="CommitAsync"/> is called.
    /// Disposing without committing rolls back all changes.
    /// </summary>
    public interface ITrustListTransaction : IAsyncDisposable
    {
        /// <summary>The trust-list being modified.</summary>
        TrustListIdentifier TrustList { get; }

        /// <summary>Adds a certificate to the trusted store.</summary>
        /// <param name="certificate">The certificate to add.</param>
        /// <param name="ct">Cancellation token.</param>
        Task AddTrustedCertificateAsync(
            Certificate certificate,
            CancellationToken ct = default);

        /// <summary>Removes a certificate from the trusted store.</summary>
        /// <param name="thumbprint">The thumbprint of the certificate to remove.</param>
        /// <param name="ct">Cancellation token.</param>
        Task RemoveTrustedCertificateAsync(
            string thumbprint,
            CancellationToken ct = default);

        /// <summary>Adds a certificate to the issuer store.</summary>
        /// <param name="certificate">The certificate to add.</param>
        /// <param name="ct">Cancellation token.</param>
        Task AddIssuerCertificateAsync(
            Certificate certificate,
            CancellationToken ct = default);

        /// <summary>Removes a certificate from the issuer store.</summary>
        /// <param name="thumbprint">The thumbprint of the certificate to remove.</param>
        /// <param name="ct">Cancellation token.</param>
        Task RemoveIssuerCertificateAsync(
            string thumbprint,
            CancellationToken ct = default);

        /// <summary>Adds a CRL to the trust-list.</summary>
        /// <param name="crl">The CRL to add.</param>
        /// <param name="ct">Cancellation token.</param>
        Task AddCrlAsync(X509CRL crl, CancellationToken ct = default);

        /// <summary>Removes a CRL from the trust-list.</summary>
        /// <param name="crl">The CRL to remove.</param>
        /// <param name="ct">Cancellation token.</param>
        Task RemoveCrlAsync(X509CRL crl, CancellationToken ct = default);

        /// <summary>
        /// Commits all staged changes atomically.
        /// A <c>TrustListUpdatedAuditEvent</c> should be emitted after
        /// a successful commit.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        Task CommitAsync(CancellationToken ct = default);
    }
}
