// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Describes the kind of certificate change that occurred.
    /// </summary>
    public enum CertificateChangeKind
    {
        /// <summary>
        /// An application certificate was updated.
        /// </summary>
        ApplicationCertificateUpdated,

        /// <summary>
        /// A trust list was updated.
        /// </summary>
        TrustListUpdated,

        /// <summary>
        /// A certificate revocation list was updated.
        /// </summary>
        CrlUpdated,

        /// <summary>
        /// A certificate was rejected during validation.
        /// </summary>
        CertificateRejected,

        /// <summary>
        /// A certificate is approaching its expiry date.
        /// </summary>
        CertificateExpiring
    }

    /// <summary>
    /// Represents a certificate change event raised by the certificate
    /// manager when certificates, trust lists, or CRLs are modified.
    /// </summary>
    /// <param name="Kind">The kind of change that occurred.</param>
    /// <param name="TrustList">
    /// The trust list affected by the change.
    /// </param>
    /// <param name="CertificateType">
    /// The OPC UA certificate type <see cref="NodeId"/>, or
    /// <c>null</c> if not applicable.
    /// </param>
    /// <param name="OldCertificate">
    /// The previous certificate, or <c>null</c> if not applicable.
    /// </param>
    /// <param name="NewCertificate">
    /// The new certificate, or <c>null</c> if not applicable.
    /// </param>
    /// <param name="IssuerChain">
    /// The issuer chain for the new certificate, or <c>null</c> if
    /// not applicable.
    /// </param>
    public sealed record CertificateChangeEvent(
        CertificateChangeKind Kind,
        TrustListIdentifier TrustList,
        NodeId? CertificateType,
        Certificate? OldCertificate,
        Certificate? NewCertificate,
        CertificateCollection? IssuerChain);
}
