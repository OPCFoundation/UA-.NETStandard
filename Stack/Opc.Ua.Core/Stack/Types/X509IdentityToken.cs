/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
