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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
using Opc.Ua.X509StoreExtensions;

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
            GC.SuppressFinalize(this);
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

        /// <inheritdoc/>
        /// <remarks>
        /// Syntax: StoreLocation\StoreName
        /// Example:
        ///   CurrentUser\My
        /// </remarks>
        public void Open(string location, bool noPrivateKeys = true)
        {
            StorePath = location ?? throw new ArgumentNullException(nameof(location));
            NoPrivateKeys = noPrivateKeys;
            location = location.Trim();

            if (string.IsNullOrEmpty(location))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Store Location cannot be empty.");
            }

            // extract store name.
            int index = location.IndexOf('\\', StringComparison.Ordinal);
            if (index == -1)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "Path does not specify a store name. Path={0}",
                    location);
            }

            // extract store location.
            string storeLocation = location[..index];
            bool found = false;
            foreach (StoreLocation availableLocation in new[] {
                StoreLocation.LocalMachine,
                StoreLocation.CurrentUser })
            {
                if (availableLocation.ToString()
                    .Equals(storeLocation, StringComparison.OrdinalIgnoreCase))
                {
                    m_storeLocation = availableLocation;
                    found = true;
                }
            }
            if (!found)
            {
                var message = new StringBuilder();
                message.AppendLine("Store location specified not available.")
                    .AppendLine("Store location={0}");
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    message.ToString(),
                    storeLocation);
            }

            m_storeName = location[(index + 1)..];
        }

        /// <inheritdoc/>
        public void Close()
        {
            // nothing to do
        }

        /// <inheritdoc/>
        public string StoreType => CertificateStoreType.X509Store;

        /// <inheritdoc/>
        public string StorePath { get; private set; }

        /// <inheritdoc/>
        public bool NoPrivateKeys { get; private set; }

        /// <inheritdoc/>
        public Task<X509Certificate2Collection> EnumerateAsync(CancellationToken ct = default)
        {
            using var store = new X509Store(m_storeName, m_storeLocation);
            store.Open(OpenFlags.ReadOnly);
            return Task.FromResult(new X509Certificate2Collection(store.Certificates));
        }

        /// <inheritdoc/>
        public Task AddAsync(
            X509Certificate2 certificate,
            string password = null,
            CancellationToken ct = default)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            using (var store = new X509Store(m_storeName, m_storeLocation))
            {
                store.Open(OpenFlags.ReadWrite);
                if (!store.Certificates.Contains(certificate))
                {
                    if (certificate.HasPrivateKey && !NoPrivateKeys)
                    {
                        // X509Store needs a persisted private key
                        X509Certificate2 persistedCertificate = X509Utils.CreateCopyWithPrivateKey(
                            certificate,
                            true);
                        store.Add(persistedCertificate);
                    }
                    else if (certificate.HasPrivateKey && NoPrivateKeys)
                    {
                        // ensure no private key is added to store
                        using X509Certificate2 publicKey = X509CertificateLoader.LoadCertificate(
                            certificate.RawData);
                        store.Add(publicKey);
                    }
                    else
                    {
                        store.Add(certificate);
                    }

                    Utils.LogCertificate(
                        "Added certificate to X509Store {0}.",
                        certificate,
                        store.Name);
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<bool> DeleteAsync(string thumbprint, CancellationToken ct = default)
        {
            using (var store = new X509Store(m_storeName, m_storeLocation))
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
        public Task<X509Certificate2Collection> FindByThumbprintAsync(
            string thumbprint,
            CancellationToken ct = default)
        {
            using var store = new X509Store(m_storeName, m_storeLocation);
            store.Open(OpenFlags.ReadOnly);

            var collection = new X509Certificate2Collection();

            foreach (X509Certificate2 certificate in store.Certificates)
            {
                if (certificate.Thumbprint == thumbprint)
                {
                    collection.Add(certificate);
                }
            }

            return Task.FromResult(collection);
        }

        /// <inheritdoc/>
        public bool SupportsLoadPrivateKey => false;

        /// <inheritdoc/>
        /// <remarks>The LoadPrivateKey special handling is not necessary in this store.</remarks>
        public Task<X509Certificate2> LoadPrivateKeyAsync(
            string thumbprint,
            string subjectName,
            string applicationUri,
            NodeId certificateType,
            string password,
            CancellationToken ct = default)
        {
            return Task.FromResult<X509Certificate2>(null);
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are only supported on Windows Platform.</remarks>
        public bool SupportsCRLs => PlatformHelper.IsWindowsWithCrlSupport();

        /// <inheritdoc/>
        /// <remarks>CRLs are only supported on Windows Platform.</remarks>
        public async Task<StatusCode> IsRevokedAsync(
            X509Certificate2 issuer,
            X509Certificate2 certificate,
            CancellationToken ct = default)
        {
            if (!SupportsCRLs)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            if (issuer == null)
            {
                throw new ArgumentNullException(nameof(issuer));
            }

            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            X509CRLCollection crls = await EnumerateCRLsAsync(ct).ConfigureAwait(false);
            // check for CRL.

            bool crlExpired = true;

            foreach (X509CRL crl in crls)
            {
                if (!X509Utils.CompareDistinguishedName(crl.IssuerName, issuer.SubjectName))
                {
                    continue;
                }

                if (!crl.VerifySignature(issuer, false))
                {
                    continue;
                }

                if (crl.IsRevoked(certificate))
                {
                    return (StatusCode)StatusCodes.BadCertificateRevoked;
                }

                if (crl.ThisUpdate <= DateTime.UtcNow &&
                    (crl.NextUpdate == DateTime.MinValue || crl.NextUpdate >= DateTime.UtcNow))
                {
                    crlExpired = false;
                }
            }

            // certificate is fine.
            if (!crlExpired)
            {
                return (StatusCode)StatusCodes.Good;
            }

            // can't find a valid CRL.
            return (StatusCode)StatusCodes.BadCertificateRevocationUnknown;
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are only supported on Windows Platform.</remarks>
        public Task<X509CRLCollection> EnumerateCRLsAsync(CancellationToken ct = default)
        {
            if (!SupportsCRLs)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            var crls = new X509CRLCollection();
            using (var store = new X509Store(m_storeName, m_storeLocation))
            {
                store.Open(OpenFlags.ReadOnly);

                foreach (byte[] rawCrl in store.EnumerateCrls())
                {
                    try
                    {
                        var crl = new X509CRL(rawCrl);
                        crls.Add(crl);
                    }
                    catch (Exception e)
                    {
                        Utils.LogError(e, "Failed to parse CRL in store {0}.", store.Name);
                    }
                }
            }
            return Task.FromResult(crls);
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are only supported on Windows Platform.</remarks>
        public async Task<X509CRLCollection> EnumerateCRLsAsync(
            X509Certificate2 issuer,
            bool validateUpdateTime = true,
            CancellationToken ct = default)
        {
            if (!SupportsCRLs)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            if (issuer == null)
            {
                throw new ArgumentNullException(nameof(issuer));
            }
            var crls = new X509CRLCollection();
            foreach (X509CRL crl in await EnumerateCRLsAsync(ct).ConfigureAwait(false))
            {
                if (!X509Utils.CompareDistinguishedName(crl.IssuerName, issuer.SubjectName))
                {
                    continue;
                }

                if (!crl.VerifySignature(issuer, false))
                {
                    continue;
                }

                if (!validateUpdateTime ||
                    (
                        crl.ThisUpdate <= DateTime.UtcNow &&
                        (crl.NextUpdate == DateTime.MinValue || crl.NextUpdate >= DateTime.UtcNow)))
                {
                    crls.Add(crl);
                }
            }

            return crls;
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are only supported on Windows Platform.</remarks>
        public async Task AddCRLAsync(X509CRL crl, CancellationToken ct = default)
        {
            if (!SupportsCRLs)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            if (crl == null)
            {
                throw new ArgumentNullException(nameof(crl));
            }

            X509Certificate2 issuer = null;
            X509Certificate2Collection certificates = await EnumerateAsync(ct).ConfigureAwait(
                false);
            foreach (X509Certificate2 certificate in certificates)
            {
                if (X509Utils.CompareDistinguishedName(certificate.SubjectName, crl.IssuerName) &&
                    crl.VerifySignature(certificate, false))
                {
                    issuer = certificate;
                    break;
                }
            }

            if (issuer == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid,
                    "Could not find issuer of the CRL.");
            }
            using var store = new X509Store(m_storeName, m_storeLocation);
            store.Open(OpenFlags.ReadWrite);

            store.AddCrl(crl.RawData);
        }

        /// <inheritdoc/>
        /// <remarks>CRLs are only supported on Windows Platform.</remarks>
        public Task<bool> DeleteCRLAsync(X509CRL crl, CancellationToken ct = default)
        {
            if (!SupportsCRLs)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            if (crl == null)
            {
                throw new ArgumentNullException(nameof(crl));
            }
            using var store = new X509Store(m_storeName, m_storeLocation);
            store.Open(OpenFlags.ReadWrite);

            return Task.FromResult(store.DeleteCrl(crl.RawData));
        }

        /// <inheritdoc/>
        public Task AddRejectedAsync(
            X509Certificate2Collection certificates,
            int maxCertificates,
            CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        private string m_storeName;
        private StoreLocation m_storeLocation;
    }
}
