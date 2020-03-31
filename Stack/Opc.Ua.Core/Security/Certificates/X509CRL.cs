/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using Org.BouncyCastle.X509;

namespace Opc.Ua
{
    
    /// <summary>
    /// Provides access to an X509 CRL object.
    /// </summary>
    public sealed class X509CRL : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Loads a CRL from a file.
        /// </summary>
        public X509CRL(string filePath)
        {
            RawData = File.ReadAllBytes(filePath);
            Initialize(RawData);
        }

        /// <summary>
        /// Loads a CRL from a memory buffer.
        /// </summary>
        public X509CRL(byte[] crl)
        {
            RawData = crl;
            Initialize(RawData);
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// The finializer implementation.
        /// </summary>
        ~X509CRL()
        {
            Dispose(false);
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        private void Dispose(bool disposing)
        {
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The subject name of the Issuer for the CRL.
        /// </summary>
        public string Issuer { get; private set; }

        /// <summary>
        /// When the CRL was last updated.
        /// </summary>
        public DateTime UpdateTime { get; private set; }

        /// <summary>
        /// When the CRL is due for its next update.
        /// </summary>
        public DateTime NextUpdateTime { get; private set; }

        /// <summary>
        /// The raw data for the CRL.
        /// </summary>
        public byte[] RawData { get; private set; }
        #endregion

        #region Public Methods
        /// <summary>
        /// Verifies the signature on the CRL.
        /// </summary>
        public bool VerifySignature(X509Certificate2 issuer, bool throwOnError)
        {
            m_issuer = issuer;
            try
            {
                Org.BouncyCastle.X509.X509Certificate bccert = new X509CertificateParser().ReadCertificate(issuer.RawData);
                m_crl.Verify(bccert.GetPublicKey());
            }
            catch (Exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Could not verify signature on CRL.");
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// Returns true the certificate is in the CRL.
        /// </summary>
        public bool IsRevoked(X509Certificate2 certificate)
        {
            // check that the issuer matches.
            if (m_issuer == null || !Utils.CompareDistinguishedName(certificate.Issuer, m_issuer.Subject))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Certificate was not created by the CRL issuer.");
            }

            Org.BouncyCastle.X509.X509Certificate bccert = new X509CertificateParser().ReadCertificate(certificate.RawData);
            return m_crl.IsRevoked(bccert);
        }
        #endregion
        
        #region Private Methods
        private void Initialize(byte[] crl)
        {
            X509CrlParser parser = new X509CrlParser();
            m_crl = parser.ReadCrl(crl);
            UpdateTime = m_crl.ThisUpdate;
            NextUpdateTime = (m_crl.NextUpdate == null) ? DateTime.MinValue : m_crl.NextUpdate.Value;
            // a few conversions to match System.Security conventions
            string issuerDN = m_crl.IssuerDN.ToString();
            // replace state ST= with S= 
            issuerDN = issuerDN.Replace("ST=", "S=");
            // reverse DN order to match System.Security
            List<string> issuerList = Utils.ParseDistinguishedName(issuerDN);
            issuerList.Reverse();
            Issuer = string.Join(", ", issuerList);
        }
        #endregion

        #region Private Fields
        X509Certificate2 m_issuer;
        private X509Crl m_crl;
        #endregion
    }
}
