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
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// An abstract interface to certficate stores.
    /// </summary>
    public interface ICertificateStore : IDisposable
    {
        /// <summary>
        /// Opens the store at the specified location.
        /// </summary>
        /// <param name="location">The location.</param>
        /// <param name="noPrivateKeys">Indicates whether NO private keys are found in the store. Default <c>true</c>.</param>
        /// <remarks>
        /// The syntax depends on the store implementation.
        /// </remarks>
        void Open(string location, bool noPrivateKeys = true);

        /// <summary>
        /// Closes the store.
        /// </summary>
        void Close();

        /// <summary>
        /// The store type.
        /// </summary>
        string StoreType { get; }

        /// <summary>
        /// The store path used to open the store.
        /// </summary>
        string StorePath { get; }

        /// <summary>
        /// Enumerates the certificates in the store.
        /// </summary>
        Task<X509Certificate2Collection> Enumerate();

        /// <summary>
        /// Adds a certificate to the store.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <param name="password">The certificate password.</param>
        Task Add(X509Certificate2 certificate, string password = null);

        /// <summary>
        /// Deletes a certificate from the store.
        /// </summary>
        /// <param name="thumbprint">The thumbprint.</param>
        /// <returns>True if the certificate exists.</returns>
        Task<bool> Delete(string thumbprint);

        /// <summary>
        /// Finds the certificate with the specified thumprint.
        /// </summary>
        /// <param name="thumbprint">The thumbprint.</param>
        /// <returns>The matching certificate</returns>
        Task<X509Certificate2Collection> FindByThumbprint(string thumbprint);

        /// <summary>
        /// If the store supports the LoadPrivateKey operation.
        /// </summary>
        bool SupportsLoadPrivateKey { get; }

        /// <summary>
        /// Finds the certificate with the specified thumprint.
        /// </summary>
        /// <param name="thumbprint">The thumbprint.</param>
        /// <param name="subjectName">The certificate subject.</param>
        /// <param name="password">The certificate password.</param>
        /// <remarks>Returns always null if SupportsLoadPrivateKey returns false.</remarks>
        /// <returns>The matching certificate with private key</returns>
        Task<X509Certificate2> LoadPrivateKey(string thumbprint, string subjectName, string password);

        /// <summary>
        /// Checks if issuer has revoked the certificate.
        /// </summary>
        Task<StatusCode> IsRevoked(X509Certificate2 issuer, X509Certificate2 certificate);

        /// <summary>
        /// Whether the store supports CRLs.
        /// </summary>
        bool SupportsCRLs { get; }

        /// <summary>
        /// Returns the CRLs in the store.
        /// </summary>
        Task<X509CRLCollection> EnumerateCRLs();

        /// <summary>
        /// Returns the CRLs for the issuer.
        /// </summary>
        Task<X509CRLCollection> EnumerateCRLs(X509Certificate2 issuer, bool validateUpdateTime = true);

        /// <summary>
        /// Adds a CRL to the store.
        /// </summary>
        Task AddCRL(X509CRL crl);

        /// <summary>
        /// Removes a CRL from the store.
        /// </summary>
        Task<bool> DeleteCRL(X509CRL crl);
    };
}
