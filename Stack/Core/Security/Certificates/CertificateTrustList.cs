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

using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Opc.Ua
{
    #region CertificateTrustList Class
    /// <summary>
    /// A list of trusted certificates.
    /// </summary>
    /// <remarks>
    /// Administrators can create a list of trusted certificates by designating all certificates 
    /// in a particular certificate store as trusted and/or by explictly specifying a list of 
    /// individual certificates.
    /// 
    /// A trust list can contain either instance certificates or certification authority certificates.
    /// If the list contains instance certificates the application will trust peers that use the
    /// instance certificate (provided the ApplicationUri and HostName match the certificate).
    /// 
    /// If the list contains certification authority certificates then the application will trust
    /// peers that have certificates issued by one of the authorities.
    /// 
    /// Any certificate could be revoked by the issuer (CAs may issue certificates for other CAs).
    /// The RevocationMode specifies whether this check should be done each time a certificate
    /// in the list are used.
    /// </remarks>
    public partial class CertificateTrustList : CertificateStoreIdentifier
    {
        #region Public Methods
        /// <summary>
        /// Returns the certificates in the trust list.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public async Task<X509Certificate2Collection> GetCertificates()
        {
            X509Certificate2Collection collection = new X509Certificate2Collection();

            CertificateStoreIdentifier id = new CertificateStoreIdentifier();

            id.StoreType = this.StoreType;
            id.StorePath = this.StorePath;

            if (!String.IsNullOrEmpty(id.StorePath))
            {
                try
                {
                    ICertificateStore store = id.OpenStore();

                    try
                    {
                        collection = await store.Enumerate();
                    }
                    finally
                    {
                        store.Close();
                    }
                }
                catch (Exception)
                {
                    Utils.Trace("Could not load certificates from store: {0}.", this.StorePath);
                }
            }

            foreach (CertificateIdentifier trustedCertificate in TrustedCertificates)
            {
                X509Certificate2 certificate = await trustedCertificate.Find();

                if (certificate != null)
                {
                    collection.Add(certificate);
                }
            }

            return collection;
        }
        #endregion
    }
    #endregion
}
