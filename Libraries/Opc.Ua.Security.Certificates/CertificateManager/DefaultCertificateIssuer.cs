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
        /// <inheritdoc/>
        public Certificate IssueCertificate(
            ICertificateBuilder builder,
            Certificate issuerCertificate)
        {
            var issuerBuilder = ((ICertificateBuilderSetIssuer)builder)
                .SetIssuer(issuerCertificate);

            if (X509PfxUtils.IsECDsaSignature(issuerCertificate))
            {
                return ((ICertificateBuilderCreateForECDsa)issuerBuilder)
                    .CreateForECDsa();
            }

            return ((ICertificateBuilderCreateForRSA)issuerBuilder)
                .CreateForRSA();
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
