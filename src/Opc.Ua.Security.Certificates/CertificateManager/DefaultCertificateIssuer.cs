/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Default implementation of <see cref="ICertificateIssuer"/> that
    /// delegates to the types available in the Security.Certificates library.
    /// </summary>
    public sealed class DefaultCertificateIssuer : ICertificateIssuer
    {
        /// <summary>
        /// Gets the shared singleton instance of <see cref="DefaultCertificateIssuer"/>.
        /// </summary>
        /// <remarks>
        /// Use this singleton when no dependency-injected
        /// <see cref="ICertificateIssuer"/> is available. The default issuer
        /// is stateless and safe to share across threads.
        /// </remarks>
        public static ICertificateIssuer Instance { get; } = new DefaultCertificateIssuer();

        /// <inheritdoc/>
        public Certificate IssueCertificate(
            ICertificateBuilder builder,
            Certificate issuerCertificate)
        {
            ICertificateBuilderIssuer issuerBuilder = builder.SetIssuer(issuerCertificate);

            if (X509PfxUtils.IsECDsaSignature(issuerCertificate))
            {
                return ((ICertificateBuilderCreateForECDsa)issuerBuilder)
                    .CreateForECDsa();
            }

            return issuerBuilder.CreateForRSA();
        }

        /// <inheritdoc/>
        public X509CRL RevokeCertificates(
            Certificate issuerCertificate,
            X509CRLCollection existingCrls,
            CertificateCollection revokedCertificates,
            DateTime? thisUpdate = null,
            DateTime? nextUpdate = null)
        {
            if (!issuerCertificate.HasPrivateKey)
            {
                throw new InvalidOperationException(
                    "Issuer certificate has no private key, cannot revoke certificate.");
            }

            DateTime effectiveThisUpdate = thisUpdate ?? DateTime.UtcNow;
            DateTime effectiveNextUpdate = nextUpdate ?? effectiveThisUpdate.AddMonths(12);

            BigInteger crlSerialNumber = 0;
            var crlRevokedList = new Dictionary<string, RevokedCertificate>();

            // merge all existing revocation lists
            if (existingCrls != null)
            {
                foreach (X509CRL issuerCrl in existingCrls)
                {
                    X509CrlNumberExtension? extension = issuerCrl.CrlExtensions
                        .FindExtension<X509CrlNumberExtension>();

                    if (extension != null && extension.CrlNumber > crlSerialNumber)
                    {
                        crlSerialNumber = extension.CrlNumber;
                    }

                    foreach (RevokedCertificate revokedCertificate in issuerCrl.RevokedCertificates)
                    {
                        if (!crlRevokedList.ContainsKey(revokedCertificate.SerialNumber))
                        {
                            crlRevokedList[revokedCertificate.SerialNumber] = revokedCertificate;
                        }
                    }
                }
            }

            // add serial numbers of newly revoked certificates
            if (revokedCertificates != null)
            {
                foreach (Certificate cert in revokedCertificates)
                {
                    if (!crlRevokedList.ContainsKey(cert.SerialNumber))
                    {
                        crlRevokedList[cert.SerialNumber] = new RevokedCertificate(
                            cert.SerialNumber,
                            CRLReason.PrivilegeWithdrawn);
                    }
                }
            }

            CrlBuilder crlBuilder = CrlBuilder
                .Create(issuerCertificate.SubjectName)
                .AddRevokedCertificates([.. crlRevokedList.Values])
                .SetThisUpdate(effectiveThisUpdate)
                .SetNextUpdate(effectiveNextUpdate)
                .AddCRLExtension(issuerCertificate.BuildAuthorityKeyIdentifier())
                .AddCRLExtension(X509Extensions.BuildCRLNumber(crlSerialNumber + 1));

            if (X509PfxUtils.IsECDsaSignature(issuerCertificate))
            {
                return new X509CRL(crlBuilder.CreateForECDsa(issuerCertificate));
            }

            return new X509CRL(crlBuilder.CreateForRSA(issuerCertificate));
        }
    }
}
