// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using System.Collections.Generic;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Certificate management capabilities. The certificate management
    /// capabilities are exposed by the application.
    /// </summary>
    public interface IPkiManagement
    {
        /// <summary>
        /// Enumerate certificates
        /// </summary>
        /// <param name="store"></param>
        /// <param name="includePrivateKey"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<IReadOnlyList<X509Certificate>> ListCertificatesAsync(
            CertificateStoreName store, bool includePrivateKey = false,
            CancellationToken ct = default);

        /// <summary>
        /// Add certificate pfx to store
        /// </summary>
        /// <param name="store"></param>
        /// <param name="pfxBlob"></param>
        /// <param name="password"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask AddCertificateAsync(CertificateStoreName store,
            byte[] pfxBlob, string? password = null,
            CancellationToken ct = default);

        /// <summary>
        /// Add certificate to trusted and issuer stores
        /// </summary>
        /// <param name="certificateChain"></param>
        /// <param name="isSslCertificate"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask AddCertificateChainAsync(byte[] certificateChain,
            bool isSslCertificate = false, CancellationToken ct = default);

        /// <summary>
        /// Remove certificate from store
        /// </summary>
        /// <param name="store"></param>
        /// <param name="thumbprint"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask RemoveCertificateAsync(CertificateStoreName store,
            string thumbprint, CancellationToken ct = default);

        /// <summary>
        /// Approve a rejected certificate
        /// </summary>
        /// <param name="thumbprint"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask ApproveRejectedCertificateAsync(string thumbprint,
            CancellationToken ct = default);

        /// <summary>
        /// List certificate revocation lists
        /// </summary>
        /// <param name="store"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<IReadOnlyList<byte[]>> ListCertificateRevocationListsAsync(
            CertificateStoreName store, CancellationToken ct = default);

        /// <summary>
        /// Add certificate revocation list to store
        /// </summary>
        /// <param name="store"></param>
        /// <param name="crl"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask AddCertificateRevocationListAsync(CertificateStoreName store,
            byte[] crl, CancellationToken ct = default);

        /// <summary>
        /// Remove certificate revocation list from store
        /// </summary>
        /// <param name="store"></param>
        /// <param name="crl"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask RemoveCertificateRevocationListAsync(CertificateStoreName store,
            byte[] crl, CancellationToken ct = default);

        /// <summary>
        /// Clean the certificate store
        /// </summary>
        /// <param name="store"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask CleanAsync(CertificateStoreName store,
            CancellationToken ct = default);
    }
}
