#if OPCUA_CLIENT_V2
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
#endif
