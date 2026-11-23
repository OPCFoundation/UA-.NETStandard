// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Represents a transaction for modifying a trust-list.
    /// Changes are staged until <see cref="CommitAsync"/> is called.
    /// Disposing before a commit discards the staged changes.
    /// </summary>
    /// <remarks>
    /// The last staged operation wins for each certificate thumbprint or identical CRL encoding.
    /// Certificate thumbprints are compared without regard to case.
    /// </remarks>
    public interface ITrustListTransaction : IAsyncDisposable
    {
        /// <summary>
        /// The trust-list being modified.
        /// </summary>
        TrustListIdentifier TrustList { get; }

        /// <summary>
        /// Adds a certificate to the trusted store.
        /// </summary>
        /// <param name="certificate">The certificate to add.</param>
        /// <param name="ct">Cancellation token.</param>
        Task AddTrustedCertificateAsync(
            Certificate certificate,
            CancellationToken ct = default);

        /// <summary>
        /// Removes a certificate from the trusted store.
        /// </summary>
        /// <param name="thumbprint">The thumbprint of the certificate to remove.</param>
        /// <param name="ct">Cancellation token.</param>
        Task RemoveTrustedCertificateAsync(
            string thumbprint,
            CancellationToken ct = default);

        /// <summary>
        /// Adds a certificate to the issuer store.
        /// </summary>
        /// <param name="certificate">The certificate to add.</param>
        /// <param name="ct">Cancellation token.</param>
        Task AddIssuerCertificateAsync(
            Certificate certificate,
            CancellationToken ct = default);

        /// <summary>
        /// Removes a certificate from the issuer store.
        /// </summary>
        /// <param name="thumbprint">The thumbprint of the certificate to remove.</param>
        /// <param name="ct">Cancellation token.</param>
        Task RemoveIssuerCertificateAsync(
            string thumbprint,
            CancellationToken ct = default);

        /// <summary>
        /// Adds a CRL to the trust-list.
        /// </summary>
        /// <param name="crl">The CRL to add.</param>
        /// <param name="ct">Cancellation token.</param>
        Task AddCrlAsync(X509CRL crl, CancellationToken ct = default);

        /// <summary>
        /// Removes a CRL from the trust-list.
        /// </summary>
        /// <param name="crl">The CRL to remove.</param>
        /// <param name="ct">Cancellation token.</param>
        Task RemoveCrlAsync(X509CRL crl, CancellationToken ct = default);

        /// <summary>
        /// Applies all staged changes.
        /// A <c>TrustListUpdatedAuditEvent</c> should be emitted after
        /// a successful commit.
        /// </summary>
        /// <remarks>
        /// Required store configuration is checked before writes begin. A backing-store failure
        /// during commit can leave partial writes; disposal does not undo already-applied changes.
        /// </remarks>
        /// <param name="ct">Cancellation token.</param>
        Task CommitAsync(CancellationToken ct = default);
    }
}
