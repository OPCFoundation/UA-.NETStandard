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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Certificates;
using Windows.Storage.Streams;

namespace Opc.Ua
{
    /// <summary>
    /// Provides access to a simple file based certificate store.
    /// </summary>
    public class WindowsCertificateStore : ICertificateStore
    {
        #region Constructors
        /// <summary>
        /// Initializes a store.
        /// </summary>
        public WindowsCertificateStore()
        {
        }
        #endregion
        
        #region IDisposable Members
        /// <summary>
        /// May be called by the application to clean up resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Cleans up all resources held by the object.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            // clean up managed resources.
            if (disposing)
            {
                Close();
            }
        }
        #endregion

        #region ICertificateStore Members
        /// <summary cref="ICertificateStore.Open(string)" />
		/// <remarks>
		/// Syntax (items enclosed in [] are optional):
		///
		/// [\\HostName\]StoreType[\(ServiceName | UserSid)]\SymbolicName		
		///
		/// HostName     - the name of the machine where the store resides.
		/// SymbolicName - one of LocalMachine, CurrentUser, User or Service
		/// ServiceName  - the name of an NT service.
		/// UserSid      - the SID for a user account.
		/// SymbolicName - the symbolic name of the store (e.g. My, Root, Trust, CA, etc.).
		///
		/// Examples:
		///
		/// \\MYPC\LocalMachine\My
		/// CurrentUser\Trust
		/// \\MYPC\Service\My UA Server\UA Applications
		/// User\S-1-5-25\Root
		/// </remarks>
        public Task Open(string location)
        {
            lock (m_lock)
            {   
	            Parse(location);
            }

            return Task.FromResult(true);
        }

        /// <summary cref="ICertificateStore.Close()" />
        public void Close()
        {
            // do nothing.
        }

        /// <summary>
        /// Returns true if the store exists.
        /// </summary>
        public bool Exists
        {
            get
            {
                lock (m_lock)
                {
                    CertificateStore hStore = null;

                    try
                    {
                        hStore = OpenStore(true, false, true);
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// Deletes the store and all certificates contained within it.
        /// </summary>
        public void PermanentlyDeleteStore()
        {   
           // do nothing.
        }

        /// <summary cref="ICertificateStore.Enumerate()" />
        public async Task<X509Certificate2Collection> Enumerate()
        {
            X509Certificate2Collection certificates = new X509Certificate2Collection();

            // get the certificates.
            IReadOnlyList<Certificate> list = await CertificateStores.FindAllAsync();

            for (int ii = 0; ii < list.Count; ii++)
            {
                // add the certificate.
                IBuffer buffer = list[ii].GetCertificateBlob();
                byte[] cert = new byte[buffer.Length];
                CryptographicBuffer.CopyToByteArray(buffer, out cert);

                X509Certificate2 certificate = new X509Certificate2(cert);
                certificates.Add(certificate);
            }

            return certificates;
        }

        /// <summary cref="ICertificateStore.Add(X509Certificate2)" />
        public async Task Add(X509Certificate2 certificate)
        {
            if (certificate == null) throw new ArgumentNullException("certificate");

            // check for existing certificate.
            byte[] thumbprint = new byte[certificate.Thumbprint.Length];
            for (int i = 0; i < certificate.Thumbprint.Length; i++)
            {
                thumbprint[i] = (byte) certificate.Thumbprint[i];
            }

            CertificateQuery query = new CertificateQuery();
            query.Thumbprint = thumbprint;
            IReadOnlyList<Certificate> pCertContext  = await CertificateStores.FindAllAsync(query);

            if (pCertContext.Count != 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Certificate is already in the store.\r\nType={0}, Name={1}, Subject={2}",
                    m_storeType,
                    m_symbolicName,
                    certificate.Subject);
            }

            lock (m_lock)
            {
                // add certificate.
                CertificateFactory.AddCertificateToWindowsStore(
                    m_storeType == WindowsStoreType.LocalMachine,
                    m_symbolicName,
                    certificate);
            }
        }

        /// <summary cref="ICertificateStore.Delete(string)" />
        public async Task<bool> Delete(string thumbprint)
        {
            // open store.
            CertificateStore hStore = OpenStore(false, false, false);
            if (hStore == null)
            {
                return false;
            }

            // find certificate.
            byte[] byteThumbprint = new byte[thumbprint.Length];
            for (int i = 0; i < thumbprint.Length; i++)
            {
                byteThumbprint[i] = (byte)thumbprint[i];
            }

            CertificateQuery query = new CertificateQuery();
            query.Thumbprint = byteThumbprint;
            IReadOnlyList<Certificate> list = await CertificateStores.FindAllAsync(query);

            // delete certificate.
            if (list.Count > 0)
            {
                lock (m_lock)
                {
                    hStore.Delete(list[0]);
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary cref="ICertificateStore.FindByThumbprint(string)" />
        public async Task<X509Certificate2Collection> FindByThumbprint(string thumbprint)
        {
            X509Certificate2Collection certificates = new X509Certificate2Collection();

            // find existing certificate.
            IBuffer pCertContext = await FindCertificate(thumbprint);
            if (pCertContext == null)
            {
                return certificates;
            }

            // create the certificate.
            byte[] certContext = new byte[pCertContext.Length];
            CryptographicBuffer.CopyToByteArray(pCertContext, out certContext);
            certificates.Add(new X509Certificate2(certContext));
            return certificates;
        }
        
        /// <summary cref="ICertificateStore.SupportsAccessControl" />
        public bool SupportsAccessControl
        {
            get { return false; }
        }

        /// <summary cref="ICertificateStore.GetAccessRules()" />
        public IList<ApplicationAccessRule> GetAccessRules()
        {
            return new List<ApplicationAccessRule>();
        }
        
        /// <summary cref="ICertificateStore.SetAccessRules(IList{ApplicationAccessRule},bool)" />
        public void SetAccessRules(IList<ApplicationAccessRule> rules, bool replaceExisting)
        {
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
        
        /// <summary cref="ICertificateStore.GetPrivateKeyFilePath" />
        public string GetPrivateKeyFilePath(string thumbprint)
        {
            return String.Empty;
        }

        /// <summary cref="ICertificateStore.GetAccessRules(string)" />
        public IList<ApplicationAccessRule> GetAccessRules(string thumbprint)
        {
            return new List<ApplicationAccessRule>();
        }
        
        /// <summary cref="ICertificateStore.SetAccessRules(string, IList{ApplicationAccessRule},bool)" />
        public void SetAccessRules(string thumbprint, IList<ApplicationAccessRule> rules, bool replaceExisting)
        {
        }

        /// <summary>
        /// Whether the store support CRLs.
        /// </summary>
        public bool SupportsCRLs
        {
            get { return false; }
        }

        /// <summary>
        /// Checks if issuer has revoked the certificate.
        /// </summary>
        public StatusCode IsRevoked(X509Certificate2 issuer, X509Certificate2 certificate)
        {
            return StatusCodes.BadNotSupported;
        }
        #endregion
        
        #region Public Methods
        /// <summary>
        /// Returns the string representation of the store.
        /// </summary>
        public string Format()
        {
	        StringBuilder buffer = new StringBuilder();

	        if (!String.IsNullOrEmpty(m_hostName))
	        {
		        buffer.Append("\\\\");
		        buffer.Append(m_hostName);
		        buffer.Append("\\");
	        }

	        switch (m_storeType)
	        {
		        case WindowsStoreType.LocalMachine:
		        {
			        buffer.Append("LocalMachine");
			        break;
		        }

		        case WindowsStoreType.CurrentUser:
		        {
			        buffer.Append("CurrentUser");
			        break;
		        }

		        case WindowsStoreType.User:
		        {
			        buffer.Append("User");
			        break;
		        }

		        case WindowsStoreType.Service:
		        {
			        buffer.Append("Service");
			        break;
		        }
	        }

	        buffer.Append("\\");

	        if (!String.IsNullOrEmpty(m_serviceNameOrUserSid))
	        {
		        buffer.Append(m_serviceNameOrUserSid);
		        buffer.Append("\\");
	        }

	        buffer.Append(m_symbolicName);

	        return buffer.ToString();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Opens the certificate store.
        /// </summary>
        private CertificateStore OpenStore(bool readOnly, bool createAlways, bool throwIfNotExist)
        {
            // check for a valid name.
            if (String.IsNullOrEmpty(m_symbolicName))
            {
                if (throwIfNotExist)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "WindowsCertificateStore object has not been initialized properly.");
                }

                return null;
            }

            CertificateStore hStore = CertificateStores.GetStoreByName(m_symbolicName);
            if (hStore == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Could not open the certificate store.\r\nType={0}, Name={1}",
                    m_storeType,
                    m_symbolicName);
            }
            else
            {
                // return the handle.
                return hStore;
            }
        }

        /// <summary>
        /// Finds a certificate in the store.
        /// </summary>
        /// <param name="hStore">The handle for the store to search.</param>
        /// <param name="thumbprint">The thumbprint of the certificate to find.</param>
        /// <returns>The context for the matching certificate.</returns>
        private static async Task<IBuffer> FindCertificate(string thumbprint)
        {
            byte[] byteThumbprint = new byte[thumbprint.Length];
            for (int i = 0; i < thumbprint.Length; i++)
            {
                byteThumbprint[i] = (byte)thumbprint[i];
            }

            CertificateQuery query = new CertificateQuery();
            query.Thumbprint = byteThumbprint;
            IReadOnlyList<Certificate> list = await CertificateStores.FindAllAsync(query);
            if (list.Count > 0)
            {
                return list[0].GetCertificateBlob();
            }
            else
            {
                return null;
            }
        }
        
        /// <summary>
        /// Returns the display name for the certificate store.
        /// </summary>
        private static string GetStoreDisplayName(WindowsStoreType storeType, string serviceNameOrUserSid, string storeName)
        {
	        int index = storeName.LastIndexOf('\\');

	        if (index != -1)
	        {
		        storeName = storeName.Substring(index+1);
	        }

	        if (storeType == WindowsStoreType.LocalMachine)
	        {
                return Utils.Format("LocalMachine\\{0}", storeName);
	        }

	        if (storeType == WindowsStoreType.CurrentUser)
	        {
                return Utils.Format("CurrentUser\\{0}", storeName);
	        }

	        if (storeType == WindowsStoreType.Service)
	        {
                return Utils.Format("{0}\\{1}", serviceNameOrUserSid, storeName);
	        }

	        if (storeType == WindowsStoreType.User)
	        {
		        string userName = String.Empty;
        		
		        index = userName.LastIndexOf('\\');

		        if (index != -1)
		        {
			        userName = userName.Substring(index+1);
		        }

		        return Utils.Format("{0}\\{1}", userName, storeName);
	        }

	        return storeName;
        }
        
        /// <summary>
        /// Parses the a string representing the store location.
        /// </summary>
        private void Parse(string location)
        {
	        if (location == null) throw new ArgumentNullException("location");

	        location = location.Trim();

            if (String.IsNullOrEmpty(location))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Store Location cannot be a empty string.");
            }

	        string hostName = null;
	        WindowsStoreType storeType = WindowsStoreType.LocalMachine;
	        string serviceNameOrUserSid = null;

	        if (location.Contains("LocalMachine"))
	        {
		        storeType = WindowsStoreType.LocalMachine;
	        }
	        else if (location.Contains("CurrentUser"))
	        {
		        storeType = WindowsStoreType.CurrentUser;
	        }
	        else if (location.Contains("Service"))
	        {
		        storeType = WindowsStoreType.Service;
	        }
	        else if (location.Contains("User"))
	        {
		        storeType = WindowsStoreType.User;
	        }
	        else
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Location does not specify a valid store type. Location={0}",
                    location);
	        }
        		
	        m_hostName = hostName;
	        m_storeType = storeType;
	        m_serviceNameOrUserSid = serviceNameOrUserSid;
	        m_symbolicName = storeType.ToString();
	        m_displayName = GetStoreDisplayName(m_storeType, m_serviceNameOrUserSid, m_symbolicName);

            if (String.IsNullOrEmpty(m_symbolicName))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Location does not specify a store name. Location={0}",
                    location);
            }
        }
        #endregion
        
        #region Public Properties
        /// <summary>
        /// The symbolic name for the store.
        /// </summary>
		public string SymbolicName
        {
            get { return m_symbolicName; }
        }

        /// <summary>
        /// The type of windows store.
        /// </summary>
		public WindowsStoreType StoreType
        {
            get { return m_storeType; }
        }

        /// <summary>
        /// The name of the machine.
        /// </summary>
		public string HostName
        {
            get { return m_hostName; }
        }
		
        /// <summary>
        /// The service name or user SID.
        /// </summary>
        public string ServiceNameOrUserSid
        {
            get { return m_serviceNameOrUserSid; }
        }

        /// <summary>
        /// A display name for the store.
        /// </summary>
		public string DisplayName
        {
            get { return m_displayName; }
        }
        #endregion
                
        #region Private Fields
        private object m_lock = new object();
		private string m_symbolicName;
		private WindowsStoreType m_storeType;
		private string m_hostName;
		private string m_serviceNameOrUserSid;
		private string m_displayName;
        #endregion
    }
    
    /// <summary>
    /// The type of certificate store.
    /// </summary>
    public enum WindowsStoreType
    {
        /// <summary>
        /// The local machine.
        /// </summary>
	    LocalMachine,

        /// <summary>
        /// The current user.
        /// </summary>
	    CurrentUser,

        /// <summary>
        /// A user account stores.
        /// </summary>
	    User,

        /// <summary>
        /// A service account store.
        /// </summary>
	    Service
    }
}
