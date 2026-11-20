// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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

        /// <summary>
        /// Notifies the manager that the stores backing the specified trust
        /// list were modified outside of its own write APIs (e.g. by the GDS
        /// push model writing directly to the stores). The manager drops any
        /// cached validation state so subsequent validations observe the new
        /// trust material, and raises the corresponding
        /// <see cref="CertificateChangeKind.TrustListUpdated"/> and/or
        /// <see cref="CertificateChangeKind.CrlUpdated"/> events. A no-op
        /// when neither flag is set.
        /// </summary>
        /// <param name="trustList">The trust list whose stores changed.</param>
        /// <param name="trustChanged">
        /// <see langword="true"/> when trusted or issuer certificates changed.
        /// </param>
        /// <param name="crlChanged">
        /// <see langword="true"/> when certificate revocation lists changed.
        /// </param>
        void NotifyTrustListChanged(
            TrustListIdentifier trustList,
            bool trustChanged,
            bool crlChanged);
    }
}
