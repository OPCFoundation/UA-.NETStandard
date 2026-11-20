// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Provides certificate authority (CA) signing and certificate
    /// revocation list (CRL) management operations.
    /// </summary>
    public interface ICertificateIssuer
    {
        /// <summary>
        /// Issues a new certificate by signing the builder output with
        /// the specified issuer certificate.
        /// </summary>
        /// <param name="builder">
        /// A configured certificate builder whose output will be signed.
        /// </param>
        /// <param name="issuerCertificate">
        /// The CA certificate (with private key) used to sign the new
        /// certificate.
        /// </param>
        /// <returns>The newly issued and signed certificate.</returns>
        Certificate IssueCertificate(
            ICertificateBuilder builder,
            Certificate issuerCertificate);

        /// <summary>
        /// Creates or updates a certificate revocation list (CRL) for the
        /// specified issuer by revoking the given certificates.
        /// </summary>
        /// <param name="issuerCertificate">
        /// The CA certificate (with private key) that signs the CRL.
        /// </param>
        /// <param name="existingCrls">
        /// Existing CRLs to merge with (may be empty).
        /// </param>
        /// <param name="revokedCertificates">
        /// The certificates to revoke.
        /// </param>
        /// <param name="thisUpdate">
        /// Optional effective date for the CRL.
        /// Defaults to <see cref="DateTime.UtcNow"/>.
        /// </param>
        /// <param name="nextUpdate">
        /// Optional next-update date for the CRL.
        /// </param>
        /// <returns>The updated CRL containing the revoked certificates.
        /// </returns>
        X509CRL RevokeCertificates(
            Certificate issuerCertificate,
            X509CRLCollection existingCrls,
            CertificateCollection revokedCertificates,
            DateTime? thisUpdate = null,
            DateTime? nextUpdate = null);
    }
}
