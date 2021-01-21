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
            // nothing to do
        }

        /// <summary cref="ICertificateStore.Open(string)" />
        /// <remarks>
        /// Syntax: StoreLocation\StoreName
        /// Example:
        ///   CurrentUser\My
        /// </remarks>
        public void Open(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

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
            bool found = false;
            foreach (StoreLocation availableLocation in (StoreLocation[])Enum.GetValues(typeof(StoreLocation)))
            {
                if (availableLocation.ToString() == storeLocation)
                {
                    m_storeLocation = availableLocation;
                    found = true;
                }
            }
            if (found == false)
            {
                var message = new StringBuilder();
                message.AppendLine("Store location specified not available.");
                message.AppendLine("Store location={0}");
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                    message.ToString(), storeLocation);
            }

            m_storeName = path.Substring(index + 1);
        }

        /// <inheritdoc/>
        public void Close()
        {
            // nothing to do
        }

        /// <inheritdoc/>
        public Task<X509Certificate2Collection> Enumerate()
        {
            using (X509Store store = new X509Store(m_storeName, m_storeLocation))
            {
                store.Open(OpenFlags.ReadOnly);
                return Task.FromResult(new X509Certificate2Collection(store.Certificates));
            }
        }

        /// <summary cref="ICertificateStore.Add(X509Certificate2, string)" />
        public Task Add(X509Certificate2 certificate, string password = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));

            using (X509Store store = new X509Store(m_storeName, m_storeLocation))
            {
                store.Open(OpenFlags.ReadWrite);
                if (!store.Certificates.Contains(certificate))
                {
#if NETSTANDARD2_1
                    if (certificate.HasPrivateKey &&
                        (Environment.OSVersion.Platform == PlatformID.Win32NT))
                    {
                        // see https://github.com/dotnet/runtime/issues/29144
                        var temp = Guid.NewGuid().ToString();
                        using (var persistable = new X509Certificate2(certificate.Export(X509ContentType.Pfx, temp), temp,
                            X509KeyStorageFlags.PersistKeySet))
                        {
                            store.Add(persistable);
                        }
                    }
                    else
#endif
                    {
                        store.Add(certificate);
                    }
                    Utils.Trace(Utils.TraceMasks.Information, "Added cert {0} to X509Store {1}.", certificate.ToString(), store.Name);
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
        /// <remarks>CRLs are not supported here.</remarks>
        public bool SupportsCRLs => false;

        /// <inheritdoc/>
        /// <remarks>CRLs are not supported here.</remarks>
        public StatusCode IsRevoked(X509Certificate2 issuer, X509Certificate2 certificate)
        {
            return StatusCodes.BadNotSupported;
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are not supported here.</remarks>
        public List<X509CRL> EnumerateCRLs()
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are not supported here.</remarks>
        public List<X509CRL> EnumerateCRLs(X509Certificate2 issuer, bool validateUpdateTime = true)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are not supported here.</remarks>
        public void AddCRL(X509CRL crl)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are not supported here.</remarks>
        public bool DeleteCRL(X509CRL crl)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        private string m_storeName;
        private StoreLocation m_storeLocation;
    }
}
