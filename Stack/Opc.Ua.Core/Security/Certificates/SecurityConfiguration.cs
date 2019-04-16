/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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

using System.IO;

namespace Opc.Ua
{
    #region SecurityConfiguration Class
    /// <summary>
    /// The security configuration for the application.
    /// </summary>
    public partial class SecurityConfiguration
    {
        #region Public Methods
        /// <summary>
        /// Adds a certificate as a trusted peer.
        /// </summary>
        public void AddTrustedPeer(byte[] certificate)
        {
            this.TrustedPeerCertificates.TrustedCertificates.Add(new CertificateIdentifier(certificate));
        }

        /// <summary>
        /// Validates the security configuration.
        /// </summary>
        public void Validate()
        {
            if (m_applicationCertificate == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ApplicationCertificate must be specified.");
            }

            TrustedIssuerCertificates = CreateDefaultTrustList(TrustedIssuerCertificates);
            TrustedPeerCertificates = CreateDefaultTrustList(TrustedPeerCertificates);

            //set a default rejected certificate store.
            if (RejectedCertificateStore == null)
            {
                RejectedCertificateStore = new CertificateStoreIdentifier();
                RejectedCertificateStore.StoreType = CertificateStoreType.Directory;
                RejectedCertificateStore.StorePath = Utils.DefaultLocalFolder + Path.DirectorySeparatorChar + "Rejected";
            }

            // replace subjectName DC=localhost with DC=hostname
            ApplicationCertificate.SubjectName = Utils.ReplaceDCLocalhost(ApplicationCertificate.SubjectName);

        }

        /// <summary>
        /// Ensure valid trust lists.
        /// </summary>
        private CertificateTrustList CreateDefaultTrustList(CertificateTrustList trustList)
        {
            if (trustList != null)
            {
                if (trustList.StorePath != null)
                {
                    return trustList;
                }
            }

            return new CertificateTrustList();
        }
        #endregion
    }
    #endregion
}
