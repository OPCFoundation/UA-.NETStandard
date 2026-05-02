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
        /// The CA certificate (with private key) used to sign the new certificate.
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
        /// Optional effective date for the CRL. Defaults to <see cref="DateTime.UtcNow"/>.
        /// </param>
        /// <param name="nextUpdate">
        /// Optional next-update date for the CRL.
        /// </param>
        /// <returns>The updated CRL containing the revoked certificates.</returns>
        X509CRL RevokeCertificates(
            Certificate issuerCertificate,
            X509CRLCollection existingCrls,
            CertificateCollection revokedCertificates,
            DateTime? thisUpdate = null,
            DateTime? nextUpdate = null);
    }
}
