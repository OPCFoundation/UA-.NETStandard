/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.

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
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Provides access to a simple file based certificate store.
    /// </summary>
    public class X509CertificateStore : ICertificateStore
    {
        public X509CertificateStore()
        {
            m_store = null;
        }
        
        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            // clean up managed resources.
            if (disposing)
            {
                Close();
            }
        }

        /// <summary cref="ICertificateStore.Open(string)" />
		/// <remarks>
		/// Syntax: StoreLocation\StoreName		
		/// Examples:
		/// LocalMachine\My
		/// CurrentUser\Trust
		/// </remarks>
        public void Open(string path)
        {
            if (path == null) throw new ArgumentNullException("path");

            path = path.Trim();

            if (string.IsNullOrEmpty(path))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Store Location cannot be empty.");
            }

            // extract store name.
            int index = path.IndexOf('\\');
            if (index == -1)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Path does not specify a store name. Path={0}",
                    path);
            }

            // extract store location.
            string storeLocation = path.Substring(0, index);
            StoreLocation selectedLocation = StoreLocation.CurrentUser;
            bool found = false;
            foreach (StoreLocation availableLocation in (StoreLocation[])Enum.GetValues(typeof(StoreLocation)))
            {
                if (availableLocation.ToString() == storeLocation)
                {
                    selectedLocation = availableLocation;
                    found = true;
                }
            }
            if (found == false)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Store location specified not available.\r\nStore location={0}",
                    storeLocation);
            }

            string storeName = path.Substring(index + 1);
            m_store = new X509Store(storeName, selectedLocation);
            m_store.Open(OpenFlags.ReadWrite);
        }

        /// <summary cref="ICertificateStore.Close()" />
        public void Close()
        {
            if (m_store != null)
            {
                m_store.Dispose();
                m_store = null;
            }
        }

       /// <summary cref="ICertificateStore.Enumerate()" />
        public Task<X509Certificate2Collection> Enumerate()
        {
            if (m_store == null)
            {
                throw new NullReferenceException("Store null. Call Open() on the store first!");
            }

            X509Certificate2Collection certificates = new X509Certificate2Collection();

            certificates.AddRange(m_store.Certificates);

	        return Task.FromResult(certificates);
        }
            
        /// <summary cref="ICertificateStore.Add(X509Certificate2)" />
        public Task Add(X509Certificate2 certificate)
        {
            if (m_store == null)
            {
                throw new NullReferenceException("Store null. Call Open() on the store first!");
            }

            if (certificate == null) throw new ArgumentNullException("certificate");

            if (!m_store.Certificates.Contains(certificate))
            {
                m_store.Add(certificate);
            }      
            
            return Task.CompletedTask;
        }

        /// <summary cref="ICertificateStore.Delete(string)" />
        public Task<bool> Delete(string thumbprint)
        {
            if (m_store == null)
            {
                throw new NullReferenceException("Store null. Call Open() on the store first!");
            }

            foreach (X509Certificate2 certificate in m_store.Certificates)
            {
                if (certificate.Thumbprint == thumbprint)
                {
                    m_store.Certificates.Remove(certificate);
                }
            }
        
		    return Task.FromResult(true);
        }
        
        /// <summary cref="ICertificateStore.FindByThumbprint(string)" />
        public Task<X509Certificate2Collection> FindByThumbprint(string thumbprint)
        {
            if (m_store == null)
            {
                throw new NullReferenceException("Store null. Call Open() on the store first!");
            }

            X509Certificate2Collection collection = new X509Certificate2Collection();

            foreach (X509Certificate2 certificate in m_store.Certificates)
            {
                if (certificate.Thumbprint == thumbprint)
                {
                    collection.Add(certificate);
                }
            }
                                    
            return Task.FromResult(collection);
        }
        
        /// <summary cref="ICertificateStore.SupportsAccessControl" />
        public bool SupportsAccessControl
        {
            get { return false; }
        }

        /// <summary cref="ICertificateStore.GetAccessRules()" />
        public IList<ApplicationAccessRule> GetAccessRules()
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }
        
        /// <summary cref="ICertificateStore.SetAccessRules(IList{ApplicationAccessRule},bool)" />
        public void SetAccessRules(IList<ApplicationAccessRule> rules, bool replaceExisting)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }
        
        /// <summary cref="ICertificateStore.SupportsCertificateAccessControl" />
        public bool SupportsCertificateAccessControl
        {
            get { return false; }
        }
        
        /// <summary cref="ICertificateStore.SupportsPrivateKeys" />
        public bool SupportsPrivateKeys
        {
            get { return true; }
        }
        
        /// <summary cref="ICertificateStore.GetAccessRules(string)" />
        public IList<ApplicationAccessRule> GetAccessRules(string thumbprint)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }
        
        /// <summary cref="ICertificateStore.SetAccessRules(string, IList{ApplicationAccessRule},bool)" />
        public void SetAccessRules(string thumbprint, IList<ApplicationAccessRule> rules, bool replaceExisting)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <summary>
        /// Whether the store support CRLs.
        /// </summary>
        public bool SupportsCRLs { get { return false; } }

        /// <summary>
        /// Checks if issuer has revoked the certificate.
        /// </summary>
        public StatusCode IsRevoked(X509Certificate2 issuer, X509Certificate2 certificate)
        {
            return StatusCodes.BadNotSupported;
        }

        /// <summary>
        /// Returns the CRLs in the store.
        /// </summary>
        public List<X509CRL> EnumerateCRLs()
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <summary>
        /// Returns the CRLs for the issuer.
        /// </summary>
        public List<X509CRL> EnumerateCRLs(X509Certificate2 issuer)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <summary>
        /// Adds a CRL to the store.
        /// </summary>
        public void AddCRL(X509CRL crl)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <summary>
        /// Removes a CRL from the store.
        /// </summary>
        public bool DeleteCRL(X509CRL crl)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        private X509Store m_store;
    }
}
