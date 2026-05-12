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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Manages named trust lists, each consisting of a trusted-certificate
    /// store and an optional issuer-certificate store.
    /// </summary>
    public interface ICertificateTrustListManager
    {
        /// <summary>
        /// Gets the collection of registered trust list identifiers.
        /// </summary>
        IReadOnlyCollection<TrustListIdentifier> TrustLists { get; }

        /// <summary>
        /// Registers a trust list with the manager, associating it with
        /// the specified trusted and optional issuer store paths.
        /// </summary>
        /// <param name="trustList">The trust list identifier to register.</param>
        /// <param name="trustedStorePath">
        /// The store path for trusted certificates.
        /// </param>
        /// <param name="issuerStorePath">
        /// An optional store path for issuer certificates.
        /// </param>
        void RegisterTrustList(
            TrustListIdentifier trustList,
            string trustedStorePath,
            string? issuerStorePath = null);

        /// <summary>
        /// Opens the trusted-certificate store for the specified trust list.
        /// </summary>
        /// <param name="trustList">The trust list identifier.</param>
        /// <returns>
        /// An <see cref="ICertificateStore"/> for the trusted certificates.
        /// </returns>
        ICertificateStore OpenTrustedStore(TrustListIdentifier trustList);

        /// <summary>
        /// Opens the issuer-certificate store for the specified trust list,
        /// if one has been configured.
        /// </summary>
        /// <param name="trustList">The trust list identifier.</param>
        /// <returns>
        /// An <see cref="ICertificateStore"/> for the issuer certificates,
        /// or <see langword="null"/> if no issuer store is configured.
        /// </returns>
        ICertificateStore? OpenIssuerStore(TrustListIdentifier trustList);

        /// <summary>
        /// Begins a transaction for modifying a trust-list.
        /// Disposing the transaction without committing rolls back changes.
        /// </summary>
        /// <param name="trustList">The trust list to modify.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// An <see cref="ITrustListTransaction"/> that stages changes
        /// until <see cref="ITrustListTransaction.CommitAsync"/> is called.
        /// </returns>
        Task<ITrustListTransaction> BeginUpdateAsync(
            TrustListIdentifier trustList,
            CancellationToken ct = default);
    }
}
