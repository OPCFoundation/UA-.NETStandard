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
    public partial class CertificateTrustList : CertificateStoreIdentifier, IDisposable
    {
        #region Public Methods
        /// <summary>
        /// OpenStore to use a cached store of the trust list,
        /// no private keys.
        /// </summary>
        public override ICertificateStore OpenStore()
        {
            if (m_store == null || m_store.StoreType != this.StoreType)
            {
                m_store = CreateStore(this.StoreType);
            }
            m_store.Open(this.StorePath, true);
            return m_store;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Implements the dispose function.
        /// </summary>
        public virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Utils.SilentDispose(m_store);
                m_store = null;
            }
        }

        /// <summary>
        /// Returns the certificates in the trust list.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public async Task<X509Certificate2Collection> GetCertificates()
        {
            X509Certificate2Collection collection = new X509Certificate2Collection();

            if (!String.IsNullOrEmpty(this.StorePath))
            {
                try
                {
                    ICertificateStore store = OpenStore();

                    try
                    {
                        collection = await store.Enumerate().ConfigureAwait(false);
                    }
                    finally
                    {
                        store.Close();
                    }
                }
                catch (Exception)
                {
                    // TODO check all StorePath references to not leak information
                    Utils.LogError("Could not load certificates from store: {0}.", this.StorePath);
                }
            }

            foreach (CertificateIdentifier trustedCertificate in TrustedCertificates)
            {
                X509Certificate2 certificate = await trustedCertificate.Find().ConfigureAwait(false);

                if (certificate != null)
                {
                    collection.Add(certificate);
                }
            }

            return collection;
        }
        #endregion

        #region Private Members
        ICertificateStore m_store;
        #endregion
    }
    #endregion
}
