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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// A list of trusted certificates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Administrators can create a list of trusted certificates by designating all certificates
    /// in a particular certificate store as trusted and/or by explictly specifying a list of
    /// individual certificates.
    /// </para>
    /// <para>
    /// A trust list can contain either instance certificates or certification authority certificates.
    /// If the list contains instance certificates the application will trust peers that use the
    /// instance certificate (provided the ApplicationUri and HostName match the certificate).
    /// </para>
    /// <para>
    /// If the list contains certification authority certificates then the application will trust
    /// peers that have certificates issued by one of the authorities.
    /// </para>
    /// <para>
    /// Any certificate could be revoked by the issuer (CAs may issue certificates for other CAs).
    /// The RevocationMode specifies whether this check should be done each time a certificate
    /// in the list are used.
    /// </para>
    /// </remarks>
    public partial class CertificateTrustList : CertificateStoreIdentifier
    {
        /// <summary>
        /// Returns the certificates in the trust list.
        /// </summary>
        [Obsolete("Use GetCertificatesAsync() instead.")]
        public Task<X509Certificate2Collection> GetCertificates()
        {
            return GetCertificatesAsync();
        }

        /// <summary>
        /// Returns the certificates in the trust list.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async Task<X509Certificate2Collection> GetCertificatesAsync(
            CancellationToken ct = default)
        {
            var collection = new X509Certificate2Collection();

            if (!string.IsNullOrEmpty(StorePath))
            {
                ICertificateStore store = null;
                try
                {
                    store = OpenStore();

                    if (store == null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadConfigurationError,
                            "Failed to open certificate store.");
                    }

                    collection = await store.EnumerateAsync(ct).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    Utils.LogError("Could not load certificates from store: {0}.", StorePath);
                }
                finally
                {
                    store?.Close();
                }
            }

            foreach (CertificateIdentifier trustedCertificate in TrustedCertificates)
            {
                X509Certificate2 certificate = await trustedCertificate.FindAsync(ct: ct)
                    .ConfigureAwait(false);

                if (certificate != null)
                {
                    collection.Add(certificate);
                }
            }

            return collection;
        }
    }
}
