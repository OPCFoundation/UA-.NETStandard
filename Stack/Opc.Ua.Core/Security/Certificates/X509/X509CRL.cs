/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using Org.BouncyCastle.X509;
using Opc.Ua.Security.Certificates.X509;

namespace Opc.Ua.Security.Certificates
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
            if (m_issuer == null || !X509Utils.CompareDistinguishedName(certificate.Issuer, m_issuer.Subject))
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
            List<string> issuerList = X509Utils.ParseDistinguishedName(issuerDN);
            issuerList.Reverse();
            Issuer = string.Join(", ", issuerList);
        }
        #endregion

        #region Private Fields
        private X509Certificate2 m_issuer;
        private X509Crl m_crl;
        #endregion
    }
}
