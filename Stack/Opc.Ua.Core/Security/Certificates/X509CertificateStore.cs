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
using System.Text;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Provides access to a simple X509Store based certificate store.
    /// </summary>
    public class X509CertificateStore : ICertificateStore
    {
        /// <summary>
        /// Create an instance of the certificate store.
        /// </summary>
        public X509CertificateStore()
        {
            // defaults
            m_storeName = "My";
            m_storeLocation = StoreLocation.CurrentUser;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Dispose method for derived classes.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Close();
            }
        }

        /// <summary cref="ICertificateStore.Open(string, bool)" />
        /// <remarks>
        /// Syntax: StoreLocation\StoreName
        /// Example:
        ///   CurrentUser\My
        /// </remarks>
        public void Open(string location, bool noPrivateKeys = true)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));

            m_storePath = location;
            m_noPrivateKeys = noPrivateKeys;
            location = location.Trim();

            if (string.IsNullOrEmpty(location))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Store Location cannot be empty.");
            }

            // extract store name.
            int index = location.IndexOf('\\');
            if (index == -1)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Path does not specify a store name. Path={0}",
                    location);
            }

            // extract store location.
            string storeLocation = location.Substring(0, index);
            bool found = false;
            foreach (StoreLocation availableLocation in (StoreLocation[])Enum.GetValues(typeof(StoreLocation)))
            {
                if (availableLocation.ToString().Equals(storeLocation, StringComparison.OrdinalIgnoreCase))
                {
                    m_storeLocation = availableLocation;
                    found = true;
                }
            }
            if (!found)
            {
                var message = new StringBuilder();
                message.AppendLine("Store location specified not available.");
                message.AppendLine("Store location={0}");
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                    message.ToString(), storeLocation);
            }

            m_storeName = location.Substring(index + 1);
        }

        /// <inheritdoc/>
        public void Close()
        {
            // nothing to do
        }

        /// <inheritdoc/>
        public string StoreType => CertificateStoreType.X509Store;

        /// <inheritdoc/>
        public string StorePath => m_storePath;

        /// <inheritdoc/>
        public Task<X509Certificate2Collection> Enumerate()
        {
            using (X509Store store = new X509Store(m_storeName, m_storeLocation))
            {
                store.Open(OpenFlags.ReadOnly);
                return Task.FromResult(new X509Certificate2Collection(store.Certificates));
            }
        }

        /// <inheritdoc/>
        public Task Add(X509Certificate2 certificate, string password = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));

            using (X509Store store = new X509Store(m_storeName, m_storeLocation))
            {
                store.Open(OpenFlags.ReadWrite);
                if (!store.Certificates.Contains(certificate))
                {
#if NETSTANDARD2_1 || NET5_0_OR_GREATER || NET472_OR_GREATER
                    if (certificate.HasPrivateKey && !m_noPrivateKeys &&
                        (Environment.OSVersion.Platform == PlatformID.Win32NT))
                    {
                        // see https://github.com/dotnet/runtime/issues/29144
                        var temp = X509Utils.GeneratePasscode();
                        using (var persistable = new X509Certificate2(certificate.Export(X509ContentType.Pfx, temp), temp,
                            X509KeyStorageFlags.PersistKeySet))
                        {
                            store.Add(persistable);
                        }
                    }
                    else
#endif
                    if (certificate.HasPrivateKey && m_noPrivateKeys)
                    {
                        // ensure no private key is added to store
                        store.Add(new X509Certificate2(certificate.RawData));
                    }
                    else
                    {
                        store.Add(certificate);
                    }

                    Utils.LogCertificate("Added certificate to X509Store {0}.", certificate, store.Name);
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<bool> Delete(string thumbprint)
        {
            using (X509Store store = new X509Store(m_storeName, m_storeLocation))
            {
                store.Open(OpenFlags.ReadWrite);

                foreach (X509Certificate2 certificate in store.Certificates)
                {
                    if (certificate.Thumbprint == thumbprint)
                    {
                        store.Remove(certificate);
                    }
                }
            }

            return Task.FromResult(true);
        }

        /// <inheritdoc/>
        public Task<X509Certificate2Collection> FindByThumbprint(string thumbprint)
        {
            using (X509Store store = new X509Store(m_storeName, m_storeLocation))
            {
                store.Open(OpenFlags.ReadOnly);

                X509Certificate2Collection collection = new X509Certificate2Collection();

                foreach (X509Certificate2 certificate in store.Certificates)
                {
                    if (certificate.Thumbprint == thumbprint)
                    {
                        collection.Add(certificate);
                    }
                }

                return Task.FromResult(collection);
            }
        }

        /// <inheritdoc/>
        public bool SupportsLoadPrivateKey => false;

        /// <inheritdoc/>
        /// <remarks>The LoadPrivateKey special handling is not necessary in this store.</remarks>
        public Task<X509Certificate2> LoadPrivateKey(string thumbprint, string subjectName, string password)
        {
            return Task.FromResult<X509Certificate2>(null);
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are not supported here.</remarks>
        public bool SupportsCRLs => false;

        /// <inheritdoc/>
        /// <remarks>CRLs are not supported here.</remarks>
        public Task<StatusCode> IsRevoked(X509Certificate2 issuer, X509Certificate2 certificate)
        {
            return Task.FromResult((StatusCode)StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are not supported here.</remarks>
        public Task<X509CRLCollection> EnumerateCRLs()
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are not supported here.</remarks>
        public Task<X509CRLCollection> EnumerateCRLs(X509Certificate2 issuer, bool validateUpdateTime = true)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are not supported here.</remarks>
        public Task AddCRL(X509CRL crl)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are not supported here.</remarks>
        public Task<bool> DeleteCRL(X509CRL crl)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        private bool m_noPrivateKeys;
        private string m_storeName;
        private string m_storePath;
        private StoreLocation m_storeLocation;
    }
}
