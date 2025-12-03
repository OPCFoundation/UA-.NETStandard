/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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
            return GetCertificatesAsync(null);
        }

        /// <summary>
        /// Returns the certificates in the trust list.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async Task<X509Certificate2Collection> GetCertificatesAsync(
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            var collection = new X509Certificate2Collection();

            if (!string.IsNullOrEmpty(StorePath))
            {
                ICertificateStore store = null;
                try
                {
                    store = OpenStore(telemetry);

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
                    ILogger<CertificateTrustList> logger = telemetry.CreateLogger<CertificateTrustList>();
                    logger.LogError("Could not load certificates from store: {StorePath}.", StorePath);
                }
                finally
                {
                    store?.Close();
                }
            }

            foreach (CertificateIdentifier trustedCertificate in TrustedCertificates)
            {
                X509Certificate2 certificate = await trustedCertificate.FindAsync(null, telemetry, ct)
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
