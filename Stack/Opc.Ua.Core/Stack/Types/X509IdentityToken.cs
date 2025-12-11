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

using System;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// The X509IdentityToken class.
    /// </summary>
    public partial class X509IdentityToken
    {
        /// <summary>
        /// The certificate associated with the token.
        /// </summary>
        public X509Certificate2 Certificate { get; set; }

        /// <summary>
        /// Get certificate with validation
        /// </summary>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <returns></returns>
        public X509Certificate2 GetOrCreateCertificate(ITelemetryContext telemetry)
        {
            if (Certificate == null && m_certificateData != null)
            {
                Certificate = CertificateFactory.Create(m_certificateData);
            }
            return Certificate;
        }

        /// <summary>
        /// Creates a signature with the token.
        /// </summary>
        public override SignatureData Sign(
            byte[] dataToSign,
            string securityPolicyUri,
            ITelemetryContext telemetry)
        {
            X509Certificate2 certificate = Certificate ??
                CertificateFactory.Create(m_certificateData);

            SignatureData signatureData = SecurityPolicies.Sign(
                certificate,
                securityPolicyUri,
                dataToSign);

            m_certificateData = certificate.RawData;

            return signatureData;
        }

        /// <summary>
        /// Verifies a signature created with the token.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public override bool Verify(
            byte[] dataToVerify,
            SignatureData signatureData,
            string securityPolicyUri,
            ITelemetryContext telemetry)
        {
            try
            {
                X509Certificate2 certificate = Certificate ??
                    CertificateFactory.Create(m_certificateData);

                bool valid = SecurityPolicies.Verify(
                    certificate,
                    securityPolicyUri,
                    dataToVerify,
                    signatureData);

                m_certificateData = certificate.RawData;

                return valid;
            }
            catch (Exception e)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    e,
                    "Could not verify user signature!");
            }
        }
    }
}
