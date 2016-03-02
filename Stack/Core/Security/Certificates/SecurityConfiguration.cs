/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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


using Windows.Storage;

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
                RejectedCertificateStore.StorePath = ApplicationData.Current.LocalFolder.Path + "\\OPC Foundation\\CertificateStores\\RejectedCertificates";
            }             
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
